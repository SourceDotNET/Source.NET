using CommunityToolkit.HighPerformance;

using SevenZip.Buffer;

using Source.Common;
using Source.Common.Commands;
using Source.Common.DataCache;
using Source.Common.Filesystem;
using Source.Common.Physics;
using Source.Common.Utilities;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.DataCache;


public enum StudioDataFlags : ushort
{
	StudioMeshLoaded = 0x0001,
	VCollisionLoaded = 0x0002,
	ErrorModel = 0x0004,
	NoStudioMesh = 0x0008,
	NoVertexData = 0x0010,
	VCollisionShared = 0x0020,
	LockedMDL = 0x0040,
}

public class StudioData
{
	public MDLHandle_t Handle;

	public StudioHeader? Header = null;
	public readonly VCollide VCollisionData = new();
	public readonly StudioHWData HardwareData = new();

	public VertexFileHeader? VertexCache = null;

	public StudioDataFlags Flags;
	public int RefCount;
	public VirtualModel? VirtualModel;

	public object? UserData;

	public short[] AutoplaySequenceList = [];

	public int AnimBlockCount;
	public object?[]? AnimBlock; // todo: research what this is
}

public class MDLCache : IMDLCache, IStudioDataCache
{
	static readonly ConVar r_rootlod = new("r_rootlod", "0", FCvar.Archive, "Root LOD", 0, Studio.MAX_NUM_LODS);
	static readonly ConVar mod_forcedata = new("mod_forcedata", "0", 0, "Forces all model file data into cache on model load.");
	static readonly ConVar mod_test_not_available = new("mod_test_not_available", "0", FCvar.Cheat);
	static readonly ConVar mod_test_mesh_not_available = new("mod_test_mesh_not_available", "0", FCvar.Cheat);
	static readonly ConVar mod_test_verts_not_available = new("mod_test_verts_not_available", "0", FCvar.Cheat);
	// these do nothing for onw
	static readonly ConVar mod_load_mesh_async = new("mod_load_mesh_async", "0", 0);
	static readonly ConVar mod_load_anims_async = new("mod_load_anims_async", "0", 0);
	static readonly ConVar mod_load_vcollide_async = new("mod_load_vcollide_async", "0", 0);

	static readonly ConVar mod_trace_load = new("mod_trace_load", "0", 0);
	private static void MdlCacheMsg(string v) { if (mod_trace_load.GetBool()) Msg(v); }

	static readonly ConVar mod_lock_mdls_on_load = new("mod_lock_mdls_on_load", "0", 0);
	static readonly ConVar mod_load_fakestall = new("mod_load_fakestall", "0", 0, "Forces all ANI file loading to stall for specified ms\n");

	readonly IFileSystem fileSystem;
	readonly IPhysicsCollision physcollision;
	public MDLCache(IFileSystem fileSystem, IPhysicsCollision physcollision) {
		this.fileSystem = fileSystem;
		this.physcollision = physcollision;
		FrameUnlockCounter = new int[(int)MDLCacheDataType.Count];
		FrameUnlockCounterFieldPtr = new AnonymousSafeFieldPointer<int>[(int)MDLCacheDataType.Count];
		for (MDLCacheDataType i = 0; i < MDLCacheDataType.Count - 1; i++)
			FrameUnlockCounterFieldPtr[(int)i] = new(this, x => ref FrameUnlockCounter[(int)i]);
	}

	public int AddRef(MDLHandle_t handle) {
		return ++HandleToMDLDict[handle].RefCount;
	}

	public void BeginLock() {
		// ModelCacheSection.BeginFrameLocking();
		// MeshCacheSection.BeginFrameLocking();
	}

	public void BeginMapLoad() {
		// TODO: Actually make locking do something, we're super synchronous right now
		foreach (var mdl in HandleToMDLDict) {
			if ((mdl.Value.Flags & StudioDataFlags.LockedMDL) != 0) {
				mdl.Value.Flags &= ~StudioDataFlags.LockedMDL;
			}
		}
	}

	public void EndLock() {
		// MeshCacheSection.EndFrameLocking();
		// ModelCacheSection.EndFrameLocking();
	}

	public void EndMapLoad() {
		FinishPendingLoads();
		// TODO: Remove stray MDL's
	}

	readonly ConcurrentDictionary<UtlSymId_t, StudioData> FileToMDLDict = [];
	readonly ConcurrentDictionary<MDLHandle_t, UtlSymbol> HandleToFileDict = [];
	readonly ConcurrentDictionary<MDLHandle_t, StudioData> HandleToMDLDict = [];
	MDLHandle_t curHandle;
	MDLHandle_t NewHandle() => Interlocked.Increment(ref curHandle);


	public MDLHandle_t FindMDL(ReadOnlySpan<char> mdlRelativePath) {
		Span<char> fixedName = stackalloc char[MAX_PATH];
		strcpy(fixedName, mdlRelativePath);
		StrTools.RemoveDotSlashes(fixedName, '/');
		fixedName = fixedName.SliceNullTerminatedString();

		UtlSymbol fixedNameHash = new(fixedName);
		if (!FileToMDLDict.TryGetValue(fixedNameHash, out var info)) {
			info = InitStudioData(NewHandle());
			FileToMDLDict.TryAdd(fixedNameHash, info);
			HandleToFileDict.TryAdd(info.Handle, fixedNameHash);
		}

		AddRef(info.Handle);
		return info.Handle;
	}

	private StudioData InitStudioData(MDLHandle_t handle) {
		StudioData studioData = new();
		studioData.Handle = handle;
		HandleToMDLDict.TryAdd(handle, studioData);
		return studioData;
	}

	public void FinishPendingLoads() {

	}

	public void Flush(MDLCacheFlush flushFlags = MDLCacheFlush.All) {
		throw new NotImplementedException();
	}

	public void Flush(MDLHandle_t handle, MDLCacheFlush flushFlags = MDLCacheFlush.All) {
		StudioData studioData = HandleToMDLDict[handle];
		Assert(studioData != null);

		bool ignoreLock = (flushFlags & MDLCacheFlush.IgnoreLock) != 0;

		if ((flushFlags & MDLCacheFlush.StudioHWData) != 0)
			UnloadHardwareData(handle, true, ignoreLock);

		if ((flushFlags & MDLCacheFlush.VCollide) != 0)
			DestroyVCollide(handle);

		if ((flushFlags & MDLCacheFlush.VirtualModel) != 0)
			FreeVirtualModel(handle);

		if ((flushFlags & MDLCacheFlush.AnimBlock) != 0)
			FreeAnimBlocks(handle);

		if ((flushFlags & MDLCacheFlush.Autoplay) != 0)
			FreeAutoplaySequences(studioData);

		if ((flushFlags & MDLCacheFlush.StudioHDR) != 0) {
			MdlCacheMsg($"MDLCache: Free studiohdr {GetModelName(handle)}\n");

			if ((studioData.Flags & StudioDataFlags.LockedMDL) != 0)
				studioData.Flags &= ~StudioDataFlags.LockedMDL;
			studioData.Header = null;
		}

		if ((flushFlags & MDLCacheFlush.Vertexes) != 0) {
			MdlCacheMsg($"MDLCache: Free VVD {GetModelName(handle)}\n");
			studioData.VertexCache = null;
		}

		if (!IsDataLoaded(handle, MDLCacheDataType.StudioHDR))
			NotifyFileUnloaded(handle, ".mdl");

		if (!IsDataLoaded(handle, MDLCacheDataType.StudioHWData))
			NotifyFileUnloaded(handle, GetVTXExtension());

		if (!IsDataLoaded(handle, MDLCacheDataType.Vertexes))
			NotifyFileUnloaded(handle, ".vvd");

		if (!IsDataLoaded(handle, MDLCacheDataType.VCollide))
			NotifyFileUnloaded(handle, ".phy");
	}

	private void UnloadHardwareData(MDLHandle_t handle, bool cacheRemove, bool lockedOk) {
		// todo
	}

	private void DestroyVCollide(MDLHandle_t handle) {
		StudioData studioData = HandleToMDLDict[handle];

		if ((studioData.Flags & StudioDataFlags.VCollisionShared) != 0)
			return;

		if ((studioData.Flags & StudioDataFlags.VCollisionLoaded) != 0) {
			studioData.Flags &= ~StudioDataFlags.VCollisionLoaded;
			if (studioData.VCollisionData.SolidCount != 0) {
				CacheNotify?.OnDataUnloaded(MDLCacheDataType.VCollide, handle);

				MdlCacheMsg($"MDLCache: Unload vcollide {GetModelName(handle)}\n");

				physcollision.VCollideUnload(studioData.VCollisionData);
			}
		}
	}

	private void FreeAnimBlocks(MDLHandle_t handle) {
		StudioData studioData = HandleToMDLDict[handle];

		if (studioData.AnimBlock != null) {
			for (int i = 0; i < studioData.AnimBlockCount; ++i) {
				MdlCacheMsg($"MDLCache: Free Anim block: {i}\n");
				studioData.AnimBlock[i] = null;
			}

			studioData.AnimBlock = null;
		}

		studioData.AnimBlockCount = 0;
	}

	private void FreeAutoplaySequences(StudioData studioData) {
		studioData.AutoplaySequenceList = [];
	}

	private void NotifyFileUnloaded(MDLHandle_t handle, ReadOnlySpan<char> extension) {
		// todo
	}

	public Memory<byte> GetAnimBlock(MDLHandle_t handle, int block) {
		if (mod_test_not_available.GetBool())
			return null;

		if (handle == MDLHANDLE_INVALID)
			return null;

		StudioData pStudioData = HandleToMDLDict[handle];
		if (pStudioData.AnimBlock == null) {
			StudioHeader pStudioHdr = GetStudioHdr(handle);
			AllocateAnimBlocks(pStudioData, pStudioHdr!.NumAnimBlocks);
		}

		if (block < 0 || block >= pStudioData.AnimBlockCount)
			return null;

		Memory<byte> pData = (byte[])pStudioData.AnimBlock[block];
		if (pData.IsEmpty) {
			pStudioData.AnimBlock[block] = null;

			// It's not in memory, read it off of disk
			pData = UnserializeAnimBlock(handle, block);
		}

		return pData;
	}

	private Memory<byte> UnserializeAnimBlock(MDLHandle_t handle, int block) {
		Assert(block > 0);

		StudioData studioData = HandleToMDLDict[handle];

		StudioHeader studioHdr = GetStudioHdr(handle)!;

		ReadOnlySpan<char> modelName = studioHdr.AnimBlockName();
		MStudioAnimBlock animBlock = studioHdr.AnimBlock(block);
		int size = animBlock.DataEnd - animBlock.DataStart;
		if (size == 0)
			return null;

		studioData.AnimBlock![block] = null;

		MdlCacheMsg($"MDLCache: Begin load Anim Block {GetModelName(handle)} (block {block})\n");

		using IFileHandle? file = fileSystem.Open(modelName, FileOpenOptions.Read, "GAME");
		if (file == null)
			return null;

		byte[] data = new byte[size];
		file.Stream.Seek(animBlock.DataStart, SeekOrigin.Begin);
		file.Stream.ReadExactly(data);

		MdlCacheMsg($"MDLCache: Finish load anim block {studioHdr.GetName()} (block {block})\n");

		studioData.AnimBlock[block] = data;
		return data;
	}

	private void AllocateAnimBlocks(StudioData studioData, int count) {
		studioData.AnimBlockCount = count;
		studioData.AnimBlock = new object?[studioData.AnimBlockCount];
	}

	public bool GetAsyncLoad(MDLCacheDataType type) {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<short> GetAutoplayList(MDLHandle_t handle) {
		if (handle == MDLHANDLE_INVALID)
			return [];

		VirtualModel? virtualModel = GetVirtualModel(handle);
		if (virtualModel != null)
			return virtualModel.AutoplaySequences;

		StudioData studioData = HandleToMDLDict[handle];
		return studioData.AutoplaySequenceList;
	}

	readonly int[] FrameUnlockCounter;
	readonly AnonymousSafeFieldPointer<int>[] FrameUnlockCounterFieldPtr;
	public AnonymousSafeFieldPointer<int> GetFrameUnlockCounterPtr(MDLCacheDataType type) => FrameUnlockCounterFieldPtr[(int)type];

	public StudioHWData? GetHardwareData(MDLHandle_t handle) {
		StudioData studioData = HandleToMDLDict[handle];

		if ((studioData.Flags & (StudioDataFlags.StudioMeshLoaded | StudioDataFlags.NoStudioMesh)) == 0)
			if (!LoadHardwareData(handle))
				return null;

		if ((studioData.Flags & StudioDataFlags.NoStudioMesh) != 0)
			return null;

		return studioData.HardwareData;
	}

	private bool LoadHardwareData(MDLHandle_t handle) {
		StudioData studioData = HandleToMDLDict[handle];

		StudioHeader? studioHdr = GetStudioHdr(handle);
		if (studioHdr == null || studioHdr.NumBodyParts == 0) {
			studioData.Flags |= StudioDataFlags.NoStudioMesh;
			return true;
		}

		if ((studioData.Flags & StudioDataFlags.NoStudioMesh) != 0) {
			return false;
		}

		// Vertex data is required to call LoadModel(), so make sure that's ready
		if (GetVertexData(handle) == null) {
			if ((studioData.Flags & StudioDataFlags.NoVertexData) != 0)
				studioData.Flags |= StudioDataFlags.NoStudioMesh;

			return false;
		}

		MdlCacheMsg($"MDLCache: Begin load VTX {GetModelName(handle)}\n");
		return BuildHardwareData(handle, studioData, studioHdr);
	}
	IStudioRender? studioRender;
	IStudioRender StudioRender => studioRender ??= Singleton<IStudioRender>();
	private bool BuildHardwareData(uint handle, StudioData studioData, StudioHeader studioHdr) {
		Span<char> fileName = stackalloc char[MAX_PATH];
		MakeFilename(handle, GetVTXExtension(), fileName);
		fileName = fileName.SliceNullTerminatedString();

		MemoryStream vtxHeader = new();
		if (!ReadFileNative(fileName, "GAME", vtxHeader))
			return false;

		vtxHeader.Position = 0;
		OptimizedModel.FileHeader? vtxHdr = new(vtxHeader.GetBuffer());
		if (vtxHdr.Version != OptimizedModel.OPTIMIZED_MODEL_FILE_VERSION) {
			Warning($"Error Index File for '{studioHdr.GetName()}' version {vtxHdr.Version} should be {OptimizedModel.OPTIMIZED_MODEL_FILE_VERSION}\n");
			vtxHdr = null;
		}
		else if (vtxHdr.Checksum != studioHdr.Checksum) {
			Warning($"Error Index File for '{studioHdr.GetName()}' checksum {vtxHdr.Checksum} should be {studioHdr.Checksum}\n");
			vtxHdr = null;
		}

		if (vtxHdr == null) {
			studioData.Flags |= StudioDataFlags.NoStudioMesh;
			return false;
		}

		bool loaded = StudioRender.LoadModel(studioHdr, vtxHeader.GetBuffer(), studioData.HardwareData);

		if (loaded)
			studioData.Flags |= StudioDataFlags.StudioMeshLoaded;
		else
			studioData.Flags |= StudioDataFlags.NoStudioMesh;

		CacheNotify?.OnDataLoaded(MDLCacheDataType.StudioHWData, handle);

		return true;
	}

	private ReadOnlySpan<char> GetVTXExtension() {
		return ".dx90.vtx";
	}

	public ReadOnlySpan<char> GetModelName(MDLHandle_t handle) {
		if (handle == MDLHANDLE_INVALID)
			return ERROR_MODEL;

		return HandleToFileDict[handle].String();
	}

	public int GetRef(MDLHandle_t handle) {
		throw new NotImplementedException();
	}
	const string ERROR_MODEL = "models/error.mdl";
	public StudioHeader? GetStudioHdr(MDLHandle_t handle) {
		if (handle == MDLHANDLE_INVALID)
			return null;

		StudioHeader? hdr = HandleToMDLDict[handle].Header;
		if (hdr == null) {
			ReadOnlySpan<char> modelName = GetActualModelName(handle);
			DevMsg($"Loading {modelName}\n");

			MemoryStream buf = new();
			if (!ReadMDLFile(handle, modelName, buf)) {
				bool ok = false;
				if ((HandleToMDLDict[handle].Flags & StudioDataFlags.ErrorModel) == 0) {
					buf.SetLength(0);

					HandleToMDLDict[handle].Flags |= StudioDataFlags.ErrorModel;
					ok = ReadMDLFile(handle, ERROR_MODEL, buf);
				}

				if (!ok) {
					Error($"Model {modelName} not found and {ERROR_MODEL} couldn't be loaded");
					return null;
				}
			}

			hdr = HandleToMDLDict[handle].Header;

			if (hdr != null && (hdr.Flags & StudioHdrFlags.FlexesConverted) == 0) {
				ConvertFlexData(hdr);
				hdr.Flags |= StudioHdrFlags.FlexesConverted;
			}

			CacheNotify?.OnDataLoaded(MDLCacheDataType.StudioHDR, handle);
		}

		return hdr;
	}

	private void ConvertFlexData(StudioHeader studioHdr) {
		for (int i = 0; i < studioHdr.NumBodyParts; i++) {
			MStudioBodyParts body = studioHdr.BodyPart(i);
			for (int j = 0; j < body.NumModels; j++) {
				MStudioModel model = body.Model(j);
				for (int k = 0; k < model.NumMeshes; k++) {
					MStudioMesh mesh = model.Mesh(k);
					for (int l = 0; l < mesh.NumFlexes; l++) {
						MStudioFlex flex = mesh.Flex(l);
						bool isWrinkleAnim = flex.VertAnimType == StudioVertAnimType.Wrinkle;
						for (int m = 0; m < flex.NumVerts; m++) {
							ref MStudioVertAnim vAnim = ref isWrinkleAnim ? ref flex.VertAnimWrinkle(m).VertAnim : ref flex.VertAnim(m);
							ref DStudioVertAnim dAnim = ref Unsafe.As<MStudioVertAnim, DStudioVertAnim>(ref vAnim);
							vAnim.SetDeltaFixed(dAnim.Delta);
							vAnim.SetNDeltaFixed(dAnim.NDelta);
						}
					}
				}
			}
		}
	}

	public const int IDSTUDIOHEADER = (('T' << 24) + ('S' << 16) + ('D' << 8) + 'I');

	private bool ReadMDLFile(MDLHandle_t handle, ReadOnlySpan<char> mdlFileName, MemoryStream buf) {
		Span<char> fileName = stackalloc char[MAX_PATH];
		strcpy(fileName, mdlFileName);
		StrTools.FixSlashes(fileName);
		fileName = fileName.SliceNullTerminatedString();

		MdlCacheMsg($"MDLCache: Load studiohdr {fileName}\n");

		bool ok = ReadFileNative(fileName, "GAME", buf);
		if (!ok) {
			DevWarning($"Failed to load {mdlFileName}!\n");
			return false;
		}

		StudioHeader? studioHdr = ReinterpretDataToStudioHdr(buf);
		if (studioHdr == null) {
			DevWarning($"Failed to read model {mdlFileName} from buffer!\n");
			return false;
		}
		if (studioHdr.ID != IDSTUDIOHEADER) {
			DevWarning($"Model {mdlFileName} not a .MDL format file!\n");
			return false;
		}

		if (studioHdr.NumIncludeModels == 0) {
			int count = studioHdr.CountAutoplaySequences();
			if (count != 0) {
				AllocateAutoplaySequences(HandleToMDLDict[handle], count);
				studioHdr.CopyAutoplaySequences(HandleToMDLDict[handle].AutoplaySequenceList, count);
			}
		}

		HandleToMDLDict[handle].Header = studioHdr;
		studioHdr.VirtualModel = handle;

		if (!VerifyHeaders(studioHdr)) {
			DevWarning($"Model {mdlFileName} has mismatched .vvd + .vtx files!\n");
			return false;
		}

		UnserializeAllVirtualModelsAndAnimBlocks(handle);

		return true;
	}

	private void FreeVirtualModel(MDLHandle_t handle) {
		if (HandleToMDLDict.TryGetValue(handle, out StudioData? studioData) && studioData.VirtualModel != null) {
			int groupCount = studioData.VirtualModel.Group.Count;
			Assert((groupCount >= 1) && (MDLHandle_t)studioData.VirtualModel.Group[0].Cache == handle);

			// NOTE: Start at *1* here because the 0th element contains a reference to *this* handle
			for (int i = 1; i < groupCount; ++i) {
				MDLHandle_t h = (MDLHandle_t)(studioData.VirtualModel.Group[i].Cache & 0xffff);
				FreeVirtualModel(h);
				Release(h);
			}

			studioData.VirtualModel = null;
		}
	}

	private void UnserializeAllVirtualModelsAndAnimBlocks(MDLHandle_t handle) {
		if (handle == MDLHANDLE_INVALID)
			return;

		FreeVirtualModel(handle);
		if (!mod_forcedata.GetBool())
			return;

		GetVirtualModel(handle);
		StudioHeader studioHdr = GetStudioHdr(handle)!;
		for (int i = 1; i < studioHdr.NumAnimBlocks; ++i)
			GetAnimBlock(handle, i);
	}

	private void AllocateAutoplaySequences(StudioData studioData, int count) {
		studioData.AutoplaySequenceList = new short[count];
	}

	private bool VerifyHeaders(StudioHeader studioHdr) {
		// Temporary todo
		return true;
	}

	private StudioHeader? ReinterpretDataToStudioHdr(MemoryStream buf) {
		// NOTE: We now assume that the buffer is locked.
		// TODO: Size check the memory stream before reading!!!

		// Justification for GetBuffer: If the buffer won't have any changes made to it, then we don't need
		// to store another copy of the byte data and can use the MemoryStream's array. When MemoryStream disposes,
		// the internal array will still remain ref'd by the Memory<byte>, so
		// Some operations in studio .mdl's require accessing data later on in the file beyond just the header data
		// and that's what's being set here
		StudioHeader header = new(buf.GetBuffer());
		SpanBinaryReader br = new(header.Data.Span);

		br.Read(out header.ID);
		br.Read(out header.Version);
		br.Read(out header.Checksum);
		br.ReadInto<byte>(header.Name);
		br.Read(out header.Length);

		br.Read(out header.EyePosition);
		br.Read(out header.IllumPosition);
		br.Read(out header.HullMin);
		br.Read(out header.HullMax);
		br.Read(out header.ViewBoundingBoxMin);
		br.Read(out header.ViewBoundingBoxMax);

		header.Flags = (StudioHdrFlags)br.Read<int>();

		br.Read(out header.NumBones);
		br.Read(out header.BoneIndex);
		br.Read(out header.NumBoneControllers);
		br.Read(out header.BoneControllerIndex);
		br.Read(out header.NumHitboxSets);
		br.Read(out header.HitboxSetIndex);
		br.Read(out header.NumLocalAnim);
		br.Read(out header.LocalAnimIndex);
		br.Read(out header.NumLocalSeq);
		br.Read(out header.LocalSeqIndex);
		br.Read(out header.ActivityListVersion);
		br.Read(out header.EventsIndexed);
		br.Read(out header.NumTextures);
		br.Read(out header.TextureIndex);

		br.Read(out header.NumCDTextures);
		br.Read(out header.CDTextureIndex);
		br.Read(out header.NumSkinRef);
		br.Read(out header.NumSkinFamilies);
		br.Read(out header.SkinIndex);
		br.Read(out header.NumBodyParts);
		br.Read(out header.BodyPartIndex);
		br.Read(out header.NumLocalAttachments);
		br.Read(out header.LocalAttachmentIndex);
		br.Read(out header.NumLocalNodes);
		br.Read(out header.LocalNodeIndex);
		br.Read(out header.LocalNodeNameIndex);
		br.Read(out header.NumFlexDesc);
		br.Read(out header.FlexDescIndex);
		br.Read(out header.NumFlexControllers);
		br.Read(out header.FlexControllerIndex);
		br.Read(out header.NumFlexRules);
		br.Read(out header.FlexRuleIndex);
		br.Read(out header.NumIKChains);
		br.Read(out header.IKChainIndex);
		br.Read(out header.NumMouths);
		br.Read(out header.MouthIndex);
		br.Read(out header.NumLocalPoseParameters);
		br.Read(out header.LocalPoseParamIndex);
		br.Read(out header.SurfacePropIndex);
		br.Read(out header.KeyValueIndex);
		br.Read(out header.KeyValueSize);
		br.Read(out header.NumLocalIKAutoplayLocks);
		br.Read(out header.LocalIKAutoplayLockIndex);
		br.Read(out header.Mass);
		br.Read(out header.Contents);
		br.Read(out header.NumIncludeModels);
		br.Read(out header.IncludeModelIndex);
		br.Advance(sizeof(int)); // Virtual model pointer
		br.Read(out header.SzAnimBlockNameIndex);
		br.Read(out header.NumAnimBlocks);
		br.Read(out header.AnimBlockIndex);
		br.Read(out header.AnimBlockModel);
		br.Read(out header.BoneTableByNameIndex);
		br.Read(out header.VertexBase);
		br.Read(out header.IndexBase);

		br.Read(out header.ConstDirectionalLightDot);
		br.Read(out header.RootLOD);
		br.Read(out header.NumAllowedRootLODs);
		br.Advance(sizeof(byte)); // Unused
		br.Advance(sizeof(int)); // Unused 4
		br.Read(out header.NumFlexControllerUI);
		br.Read(out header.FlexControllerUIIndex);
		br.Read(out header.VertAnimFixedPointScale);
		br.Advance(sizeof(int)); // Unused 3
		br.Read(out header.StudioHDR2Index);
		br.Advance(sizeof(int)); // Unused 2

		return header;
	}

	private bool ReadFileNative(ReadOnlySpan<char> fileName, ReadOnlySpan<char> path, MemoryStream buf) {
		using var file = fileSystem.Open(fileName, FileOpenOptions.Read, path);
		if (file == null)
			return false;

		file.Stream.CopyTo(buf);
		buf.Position = 0;
		return true;
	}

	private ReadOnlySpan<char> GetActualModelName(MDLHandle_t handle) {
		if (handle == MDLHANDLE_INVALID)
			return ERROR_MODEL;

		if ((HandleToMDLDict[handle].Flags & StudioDataFlags.ErrorModel) != 0)
			return ERROR_MODEL;

		return HandleToFileDict[handle].String();
	}

	public T? GetUserData<T>(MDLHandle_t handle) {
		if (handle == MDLHANDLE_INVALID)
			return default;
		return (T?)HandleToMDLDict[handle].UserData;
	}

	public VCollide? GetVCollide(MDLHandle_t handle) => GetVCollideEx(handle, true);

	public VCollide? GetVCollideEx(MDLHandle_t handle, bool synchronousLoad = true) {
		if (handle == MDLHANDLE_INVALID)
			return null;

		StudioData studioData = HandleToMDLDict[handle];

		if ((studioData.Flags & StudioDataFlags.VCollisionLoaded) == 0)
			UnserializeVCollide(handle, synchronousLoad);

		if (studioData.VCollisionData.SolidCount == 0)
			return null;

		return studioData.VCollisionData;
	}

	private void UnserializeVCollide(MDLHandle_t handle, bool synchronousLoad) {
		StudioData studioData = HandleToMDLDict[handle];

		studioData.Flags &= ~StudioDataFlags.VCollisionLoaded;
		//studioData.VCollisionData.ClearInstantiatedReference();
		// ^^ Likely don't need this?
		{
			VirtualModel? virtualModel = GetVirtualModel(handle);
			if (virtualModel != null) {
				for (int i = 1; i < virtualModel.Group.Count; i++) {
					MDLHandle_t sharedHandle = (MDLHandle_t)((int)virtualModel.Group[i].Cache & 0xffff);
					StudioData data = HandleToMDLDict[sharedHandle];
					if ((data.Flags & StudioDataFlags.VCollisionLoaded) == 0)
						UnserializeVCollide(sharedHandle, synchronousLoad);

					if (data.VCollisionData.SolidCount > 0) {
						data.VCollisionData.CopyInstantiatedReferenceTo(studioData.VCollisionData);
						studioData.Flags |= StudioDataFlags.VCollisionShared;
						return;
					}
				}
			}
		}

		Span<char> fileName = stackalloc char[MAX_PATH];
		MakeFilename(handle, ".phy", fileName);
		fileName = fileName.SliceNullTerminatedString();

		MdlCacheMsg($"MDLCache: Sync load vcollide {GetModelName(handle)}\n");

		// always marked as loaded, vcollides are not present for every model
		studioData.Flags |= StudioDataFlags.VCollisionLoaded;

		using MemoryStream buf = new();
		if (!ReadFileNative(fileName, "GAME", buf))
			return;

		ReadOnlySpan<byte> phyData = buf.GetBuffer().AsSpan(0, (int)buf.Length);

		// phyheader_t: int size; int id; int solidCount; int checksum;  (16 bytes)
		if (phyData.Length < PHYHEADER_SIZE)
			return;

		int headerSize = MemoryMarshal.Read<int>(phyData);
		int solidCount = MemoryMarshal.Read<int>(phyData[8..]);
		if (headerSize != PHYHEADER_SIZE || solidCount <= 0)
			return;

		physcollision.VCollideLoad(studioData.VCollisionData, solidCount, phyData[headerSize..]);
	}

	const int PHYHEADER_SIZE = 16;

	private void MakeFilename(MDLHandle_t handle, ReadOnlySpan<char> extension, Span<char> fileName) {
		strcpy(fileName, GetActualModelName(handle));
		StrTools.SetExtension(fileName, extension);
		StrTools.FixSlashes(fileName);
		StrTools.ToLower(fileName);
	}

	public bool GetVCollideSize(MDLHandle_t handle, out int VCollideSize) {
		throw new NotImplementedException();
	}

	public VertexFileHeader? GetVertexData(MDLHandle_t handle) {
		if (mod_test_not_available.GetBool())
			return null;

		if (mod_test_verts_not_available.GetBool())
			return null;

		return CacheVertexData(GetStudioHdr(handle));
	}

	public VirtualModel? GetVirtualModel(MDLHandle_t handle) {
		if (mod_test_not_available.GetBool())
			return null;

		if (handle == MDLHANDLE_INVALID)
			return null;

		StudioHeader? studioHdr = GetStudioHdr(handle);

		if (studioHdr == null)
			return null;

		return GetVirtualModelFast(studioHdr, handle);
	}

	public VirtualModel? GetVirtualModelFast(StudioHeader studioHdr, MDLHandle_t handle) {
		if (studioHdr.NumIncludeModels == 0)
			return null;

		if (!HandleToMDLDict.TryGetValue(handle, out StudioData? studioData))
			return null;

		if (studioData.VirtualModel == null) {
			DevMsg(2, $"Loading virtual model for {studioHdr.GetName()}\n");

			studioData.VirtualModel = AllocateVirtualModel(handle);

			// Group has to be zero to ensure refcounting is correct
			var group = studioData.VirtualModel.Group.Count;
			studioData.VirtualModel.Group.Add(new());

			Assert(group == 0);
			studioData.VirtualModel.Group[group].Cache = (nint)handle;

			// Add all dependent data
			studioData.VirtualModel.AppendModels(0, studioHdr);
		}

		return studioData.VirtualModel;
	}

	private VirtualModel AllocateVirtualModel(MDLHandle_t handle) {
		StudioData studioData = HandleToMDLDict[handle];
		Assert(studioData.VirtualModel == null);
		studioData.VirtualModel = new VirtualModel();

		Assert(studioData.AnimBlockCount == 0);
		Assert(studioData.AnimBlock == null);

		return studioData.VirtualModel;
	}

	public void InitPreloadData(bool rebuild) {
		throw new NotImplementedException();
	}

	public bool IsDataLoaded(MDLHandle_t handle, MDLCacheDataType type) {
		if (handle == MDLHANDLE_INVALID || !HandleToMDLDict.TryGetValue(handle, out StudioData? data))
			return false;

		switch (type) {
			case MDLCacheDataType.StudioHDR:
				return data.Header != null;
			case MDLCacheDataType.StudioHWData:
				return (data.Flags & StudioDataFlags.StudioMeshLoaded) != 0;
			case MDLCacheDataType.VCollide:
				return (data.Flags & StudioDataFlags.VCollisionLoaded) != 0;
			case MDLCacheDataType.AnimBlock: {
					return false; // todo
				}
			case MDLCacheDataType.VirtualModel:
				return data.VirtualModel != null;
			case MDLCacheDataType.Vertexes:
				return data.VertexCache != null;
		}

		return false;
	}

	public bool IsErrorModel(MDLHandle_t handle) {
		if (handle == MDLHANDLE_INVALID)
			return false;

		return (HandleToMDLDict[handle].Flags & StudioDataFlags.ErrorModel) != 0;
	}

	public StudioHeader? LockStudioHdr(MDLHandle_t handle) {
		if (handle == MDLHANDLE_INVALID)
			return null;

		StudioHeader? pStdioHdr = GetStudioHdr(handle);
		if (pStdioHdr == null)
			return null;

		return pStdioHdr;
	}

	public void MarkAsLoaded(MDLHandle_t handle) {
		throw new NotImplementedException();
	}

	public void MarkFrame() {

	}

	public bool PreloadModel(MDLHandle_t handle) {
		throw new NotImplementedException();
	}

	public int Release(MDLHandle_t handle) {
		// NOTE: It can be null during shutdown because multiple studiomdls
		// could be referencing the same virtual model
		if (!HandleToMDLDict.TryGetValue(handle, out StudioData? data))
			return 0;

		Assert(data.RefCount > 0);

		int nRefCount = --data.RefCount;
		if (nRefCount <= 0) {
			ShutdownStudioData(handle);
			HandleToMDLDict.Remove(handle, out _);
			if (HandleToFileDict.Remove(handle, out UtlSymbol name))
				FileToMDLDict.Remove(name, out _);
		}

		return nRefCount;
	}

	private void ShutdownStudioData(MDLHandle_t handle) {
		Flush(handle);

		Assert(HandleToMDLDict.ContainsKey(handle));
		HandleToMDLDict[handle] = null!;
	}

	public void ResetErrorModelStatus(MDLHandle_t handle) {
		throw new NotImplementedException();
	}

	public bool SetAsyncLoad(MDLCacheDataType type, bool async) {
		throw new NotImplementedException();
	}

	IMDLCacheNotify? CacheNotify;
	public void SetCacheNotify(IMDLCacheNotify notify) {
		CacheNotify = notify;
	}

	public void SetUserData<T>(MDLHandle_t handle, T? data) {
		if (handle == MDLHANDLE_INVALID)
			return;
		HandleToMDLDict[handle].UserData = (object?)data;
	}

	public void ShutdownPreloadData() {
		throw new NotImplementedException();
	}

	public void TouchAllData(MDLHandle_t handle) {
		StudioHeader? studioHdr = GetStudioHdr(handle);
		VirtualModel? vModel = GetVirtualModel(handle);
		if (vModel != null) {
			for (int i = 1; i < vModel.Group.Count; ++i) {
				// ????????????????
				MDLHandle_t childHandle = (MDLHandle_t)(nint)vModel.Group[i].Cache! & 0xffff;
				if (childHandle != MDLHANDLE_INVALID)
					GetStudioHdr(childHandle);
			}
		}

		for (int i = 1; i < studioHdr.NumAnimBlocks; ++i) {
			studioHdr.GetAnimBlock(i);
		}

		// cache the vertexes
		if (studioHdr.NumBodyParts != 0) {
			CacheVertexData(studioHdr);
			GetHardwareData(handle);
		}
	}

	public VertexFileHeader? CacheVertexData(StudioHeader? studioHdr) {
		VertexFileHeader? vvdHdr;
		Assert(studioHdr != null);
		MDLHandle_t handle = studioHdr.VirtualModel;


		Assert(handle != MDLHANDLE_INVALID);

		vvdHdr = HandleToMDLDict[handle].VertexCache;
		if (vvdHdr != null)
			return vvdHdr;

		HandleToMDLDict[handle].VertexCache = null;

		return LoadVertexData(studioHdr);
	}

	private VertexFileHeader? LoadVertexData(StudioHeader studioHdr) {
		Span<char> fileName = stackalloc char[MAX_PATH];
		MDLHandle_t handle = studioHdr.VirtualModel;
		Assert(HandleToMDLDict[handle].VertexCache == null);

		StudioData studioData = HandleToMDLDict[handle];

		if ((studioData.Flags & StudioDataFlags.NoVertexData) != 0)
			return null;

		// load the VVD file
		// use model name for correct path
		MakeFilename(handle, ".vvd", fileName);
		fileName = fileName.SliceNullTerminatedString();

		MdlCacheMsg($"MDLCache: Begin load VVD {fileName}\n");
		MemoryStream vvdHeader = new();
		if (!ReadFileNative(fileName, "GAME", vvdHeader))
			return null;

		vvdHeader.Position = 0;
		var raw = new VertexFileHeader(vvdHeader.GetBuffer());
		var processed = BuildAndCacheVertexData(studioHdr, raw);
		HandleToMDLDict[handle].VertexCache = processed;
		return processed;
	}

	private VertexFileHeader? BuildAndCacheVertexData(StudioHeader studioHdr, VertexFileHeader rawVvdHdr) {
		MDLHandle_t handle = (MDLHandle_t)(nint)studioHdr.VirtualModel & 0xffff;
		VertexFileHeader? vvdHdr;

		MdlCacheMsg($"MDLCache: Load VVD for {studioHdr.GetName()}\n");

		Assert(rawVvdHdr != null);

		// check header
		if (rawVvdHdr.ID != Studio.MODEL_VERTEX_FILE_ID) {
			Warning($"Error Vertex File for '{studioHdr.GetName()}' id {rawVvdHdr.ID} should be {Studio.MODEL_VERTEX_FILE_ID}\n");
			return null;
		}
		if (rawVvdHdr.Version != Studio.MODEL_VERTEX_FILE_VERSION) {
			Warning($"Error Vertex File for '{studioHdr.GetName()}' version {rawVvdHdr.Version} should be {Studio.MODEL_VERTEX_FILE_VERSION}\n");
			return null;
		}
		if (rawVvdHdr.Checksum != studioHdr.Checksum) {
			Warning($"Error Vertex File for '{studioHdr.GetName()}' checksum {rawVvdHdr.Checksum} should be {studioHdr.Checksum}\n");
			return null;
		}

		Assert(rawVvdHdr.NumLODs != 0);
		if (0 == rawVvdHdr.NumLODs)
			return null;

		bool needsTangentS = true;
		int rootLOD = Math.Min((int)studioHdr.RootLOD, rawVvdHdr.NumLODs - 1);

		// determine final cache footprint, possibly truncated due to lod
		MdlCacheMsg($"MDLCache: Alloc VVD {GetModelName(handle)}\n");

		// allocate cache space
		vvdHdr = new(new byte[Studio_VertexDataSize(rawVvdHdr, rootLOD, needsTangentS)]);

		// load minimum vertexes and fixup
		Studio_LoadVertexes(rawVvdHdr, vvdHdr, rootLOD, needsTangentS);

		return vvdHdr;
	}

	public static int Studio_VertexDataSize(VertexFileHeader vvdHdr, int rootLOD, bool needsTangentS) {
		int numVertexes = vvdHdr.NumLODVertices[rootLOD] + 1;
		int dataLength = vvdHdr.VertexDataStart + numVertexes * Unsafe.SizeOf<MStudioVertex>();
		if (needsTangentS)
			dataLength += numVertexes * Unsafe.SizeOf<Vector4>();

		// allocate this much
		return dataLength;
	}

	public static int Studio_LoadVertexes(VertexFileHeader tempVvdHdr, VertexFileHeader newVvdHdr, int rootLOD, bool needsTangentS) {
		int i;
		int target;
		int numVertexes;
		Span<VertexFileFixup> fixupTable;

		numVertexes = tempVvdHdr.NumLODVertices[rootLOD];

		newVvdHdr.CopyFrom(tempVvdHdr);

		for (i = 0; i < rootLOD; i++)
			newVvdHdr.NumLODVertices[i] = newVvdHdr.NumLODVertices[rootLOD];

		// fixup data starts
		if (needsTangentS)
			// tangent data follows possibly reduced vertex data
			newVvdHdr.TangentDataStart = newVvdHdr.VertexDataStart + numVertexes * Unsafe.SizeOf<MStudioVertex>();
		else
			// no tangent data will be available, mark for identification
			newVvdHdr.TangentDataStart = 0;


		if (0 == newVvdHdr.NumFixups) {
			// fixups not required
			// transfer vertex data
			memcpy(newVvdHdr.Data.AsSpan()[newVvdHdr.VertexDataStart..], tempVvdHdr.Data.AsSpan()[tempVvdHdr.VertexDataStart..], numVertexes * Unsafe.SizeOf<MStudioVertex>());

			if (needsTangentS)
				// transfer tangent data to cache memory
				memcpy(newVvdHdr.Data.AsSpan()[newVvdHdr.TangentDataStart..], tempVvdHdr.Data.AsSpan()[tempVvdHdr.TangentDataStart..], numVertexes * Unsafe.SizeOf<Vector4>());

			return numVertexes;
		}

		// fixups required
		// re-establish mesh ordered vertexes into cache memory, according to table
		target = 0;
		fixupTable = reinterpret<byte, VertexFileFixup>(tempVvdHdr.Data.AsSpan()[tempVvdHdr.FixupTableStart..]);
		for (i = 0; i < tempVvdHdr.NumFixups; i++) {
			if (fixupTable[i].LOD < rootLOD) {
				// working bottom up, skip over copying higher detail lods
				continue;
			}

			// copy vertexes
			memcpy(
				newVvdHdr.Data.AsSpan()[newVvdHdr.VertexDataStart..].Cast<byte, MStudioVertex>()[target..],
				tempVvdHdr.Data.AsSpan()[tempVvdHdr.VertexDataStart..].Cast<byte, MStudioVertex>()[fixupTable[i].SourceVertexID..],
				fixupTable[i].NumVertices
			);

			if (needsTangentS)
				memcpy(
						newVvdHdr.Data.AsSpan()[newVvdHdr.TangentDataStart..].Cast<byte, Vector4>()[target..],
						tempVvdHdr.Data.AsSpan()[tempVvdHdr.TangentDataStart..].Cast<byte, Vector4>()[fixupTable[i].SourceVertexID..],
						fixupTable[i].NumVertices
					);


			// data is placed consecutively
			target += fixupTable[i].NumVertices;
		}

		newVvdHdr.NumFixups = 0;

		return target;
	}



	public void UnlockStudioHdr(MDLHandle_t handle) {

	}
}
