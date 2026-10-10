using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Formats.BSP;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static Source.Engine.DispMapload;

namespace Source.Engine;

public ref struct MapLoadHelper
{
	internal static WorldBrushData? Map;
	internal static string? LoadName;
	internal static string? MapName;
	internal static Stream? MapFileHandle;
	internal static BSPDHeader MapHeader;
	static Host? Host;
	public static bool Init(Model? model, ReadOnlySpan<char> loadName) {
		Host = Singleton<Host>();
		ModelLoader ModelLoader = (ModelLoader)Singleton<IModelLoader>();
		IFileSystem fileSystem = Singleton<IFileSystem>();

		Map = null;
		LoadName = null;
		MapFileHandle = null;

		if (model == null)
			MapName = new(loadName);
		else
			MapName = model.StrName.String();

		MapFileHandle = fileSystem.Open(MapName, FileOpenOptions.Read | FileOpenOptions.Binary)?.Stream;
		if (MapFileHandle == null) {
			Host.Error($"MapLoadHelper.Init, unable to open {MapName}");
			return false;
		}

		if (!MapFileHandle.ReadToStruct(ref MapHeader) || MapHeader.Identifier != BSPFileCommon.IDBSPHEADER) {
			MapFileHandle.Close();
			MapFileHandle = null;
			Host.Error($"MapLoadHelper.Init, map {MapName} has wrong identifier\n");
			return false;
		}

		if (MapHeader.Version < BSPFileCommon.MINBSPVERSION || MapHeader.Version > BSPFileCommon.BSPVERSION) {
			MapFileHandle.Close();
			MapFileHandle = null;
			Host.Error($"MapLoadHelper.Init, map {MapName} has wrong version ({MapHeader.Version} when expecting {BSPFileCommon.BSPVERSION})\n");
			return false;
		}

		LoadName = new(loadName);

		if (0 == serverGlobalVariables.MapVersion)
			serverGlobalVariables.MapVersion = MapHeader.MapRevision;

#if !SWDS
		InitDLightGlobals(MapHeader.Version);
#endif
		Map = ModelLoader.WorldBrushData;

		Assert(MapFileHandle != null);
		return true;
	}

	public static void Shutdown() {
		if (MapFileHandle != null) {
			MapFileHandle.Close();
			MapFileHandle = null;
		}

		LoadName = null;
		MapName = null;
		memreset(ref MapHeader);
		Map = null;
	}

	private static void InitDLightGlobals(int version) => Render.InitDLightGlobals(version);

	public readonly WorldBrushData GetMap() => Map!;

	public readonly LumpIndex LumpID;
	public readonly int LumpSize;
	public readonly int LumpOffset;
	public readonly int LumpVersion;

	public static nint GetLumpSize(LumpIndex lumpId) {
		ref BSPLump lump = ref MapHeader.Lumps[(int)lumpId];
		int uncompressedSize = lump.UncompressedSize;
		return uncompressedSize != 0 ? uncompressedSize : lump.FileLength;
	}
	public MapLoadHelper(LumpIndex lumpToLoad) {
		LumpID = lumpToLoad;
		ref BSPLump lump = ref MapHeader.Lumps[(int)lumpToLoad];
		LumpSize = lump.UncompressedSize != 0 ? lump.UncompressedSize : lump.FileLength;
		LumpOffset = lump.FileOffset;
		LumpVersion = lump.Version;
	}

	public bool LoadLumpData<T>(int byteOffset, int bytesLength, scoped Span<T> output) where T : unmanaged {
		ref BSPLump lump = ref MapHeader.Lumps[(int)LumpID];
		T[]? ret = LoadLumpData<T>();
		if (ret == null)
			return false;
		ret.AsSpan().Cast<T, byte>()[byteOffset..(byteOffset + bytesLength)].Cast<byte, T>().ClampedCopyTo(output);
		return true;
	}
	public bool LoadLumpData<T>(scoped Span<T> output) where T : unmanaged {
		ref BSPLump lump = ref MapHeader.Lumps[(int)LumpID];
		T[]? ret = LoadLumpData<T>();
		if (ret == null)
			return false;
		ret.AsSpan().ClampedCopyTo(output);
		return true;
	}

	public byte[] LoadLumpBaseRaw() {
		ref BSPLump lump = ref MapHeader.Lumps[(int)LumpID];
		byte[]? data = (cachedData != null && cachedData is byte[] tarr) ? tarr : lump.ReadBytes<byte>(MapFileHandle!);
		return data ?? [];
	}

	object? cachedData;
	public T[] LoadLumpData<T>(bool throwIfNoElements = false, int maxElements = 0, bool sysErrorIfOOB = false) where T : unmanaged {
		ref BSPLump lump = ref MapHeader.Lumps[(int)LumpID];
		string? error;

		T[]? data = (cachedData != null && cachedData is T[] tarr) ? tarr : lump.ReadBytes<T>(MapFileHandle!);
		cachedData = data;
		if (data == null) {
			error = $"ModelLoader: funny {LumpID} lump size in {LoadName}";
			goto doError;
		}

		if (throwIfNoElements && data.Length < 1) {
			error = $"ModelLoader: lump {LumpID} has no elements in map {LoadName}";
			goto doError;
		}

		if (maxElements > 0 && data.Length > maxElements) {
			error = $"ModelLoader: lump {LumpID} has too many elements ({data.Length} > {maxElements}) in map {LoadName}";
			goto doError;
		}

		return data;
	doError:
		if (sysErrorIfOOB)
			Sys.Error(error);
		else
			Host!.Error(error);
		return [];
	}

	internal ReadOnlySpan<char> GetMapName() {
		return MapName;
	}

	internal ReadOnlySpan<char> GetLoadName() {
		return LoadName;
	}
}

public class MDLCacheNotify : IMDLCacheNotify
{
	public void OnDataLoaded(MDLCacheDataType type, uint handle) {
		Model? model = mdlcache.GetUserData<Model>(handle);

		if (model == null)
			return;

		switch (type) {
			case MDLCacheDataType.StudioHDR:
				SetBoundsFromStudioHdr(model, handle);
				break;

			case MDLCacheDataType.VCollide:
				SetBoundsFromStudioHdr(model, handle);
				// todo
				break;

			case MDLCacheDataType.StudioHWData:
				ComputeModelFlags(model, handle);
				break;
		}
	}

	private void ComputeModelFlags(Model model, uint handle) {
		StudioHeader studioHdr = mdlcache.GetStudioHdr(handle)!;

		model.Flags &= ~(ModelFlag.TranslucentTwoPass | ModelFlag.VertexLit | ModelFlag.Translucent | ModelFlag.MaterialProxy | ModelFlag.FramebufferTexture | ModelFlag.UsesFBTexture | ModelFlag.UsesBumpMapping | ModelFlag.UsesEnvCubemap);

		bool forceOpaque = (studioHdr.Flags & StudioHdrFlags.ForceOpaque) != 0;

		if ((studioHdr.Flags & StudioHdrFlags.TranslucentTwoPass) != 0)
			model.Flags |= ModelFlag.TranslucentTwoPass;
		if ((studioHdr.Flags & StudioHdrFlags.UsesFbTexture) != 0)
			model.Flags |= ModelFlag.UsesFBTexture;
		if ((studioHdr.Flags & StudioHdrFlags.UsesBumpmapping) != 0)
			model.Flags |= ModelFlag.UsesBumpMapping;
		if ((studioHdr.Flags & StudioHdrFlags.UsesEnvCubemap) != 0)
			model.Flags |= ModelFlag.UsesEnvCubemap;
		if ((studioHdr.Flags & StudioHdrFlags.AmbientBoost) != 0)
			model.Flags |= ModelFlag.AmbientBoost;
		if ((studioHdr.Flags & StudioHdrFlags.DoNotCastShadows) != 0)
			model.Flags |= ModelFlag.DoNotCastShadows;

		Span<IMaterial> materials = new IMaterial[128];
		int materialCount = modelLoader.Mod_GetModelMaterials(model, materials);

		for (int i = 0; i < materialCount; ++i) {
			IMaterial material = materials[i];
			if (material == null)
				continue;

			if (material.IsVertexLit())
				model.Flags |= ModelFlag.VertexLit;

			if (!forceOpaque && material.IsTranslucent())
				model.Flags |= ModelFlag.Translucent;

			if (material.HasProxy())
				model.Flags |= ModelFlag.MaterialProxy;

			if (material.NeedsPowerOfTwoFrameBufferTexture(false))
				model.Flags |= ModelFlag.FramebufferTexture;
		}
	}

	private void SetBoundsFromStudioHdr(Model model, uint handle) {
		StudioHeader studioHdr = mdlcache.GetStudioHdr(handle)!;
		model.Mins = studioHdr.HullMin;
		model.Maxs = studioHdr.HullMax;
		model.Radius = 0.0f;
		Span<float> mins = [model.Mins.X, model.Mins.Y, model.Mins.Z];
		Span<float> maxs = [model.Maxs.X, model.Maxs.Y, model.Maxs.Z];
		for (int i = 0; i < 3; i++) {
			if (MathF.Abs(mins[i]) > model.Radius)
				model.Radius = MathF.Abs(mins[i]);
			if (MathF.Abs(maxs[i]) > model.Radius)
				model.Radius = MathF.Abs(maxs[i]);
		}
	}

	public void OnDataUnloaded(MDLCacheDataType type, uint handle) {
		throw new NotImplementedException();
	}

	public static readonly MDLCacheNotify s = new();
}

public class ModelLoader(IFileSystem fileSystem, Host Host,
						 MatSysInterface materials,
						 IMaterialSystemHardwareConfig materialSystemHardwareConfig,
						 IMDLCache MDLCache,
						 MatSysInterface matSys,
						 IServiceProvider services) : IModelLoader
{

#if !SWDS
	EngineVGui EngineVGui => field ??= Singleton<EngineVGui>();
#endif

	IStudioRender StudioRender = services.GetService<IStudioRender>()!;
	public int GetCount() {
		throw new NotImplementedException();
	}

	public object? GetExtraData(Model? model) {
		if (model == null)
			return null;

		switch (model.Type) {
			case ModelType.Sprite: {
					// sprites don't use the real cache yet
					if (model.Type == ModelType.Sprite) {
						// The sprite got unloaded.
						return model.Sprite.Sprite; // TODO
					}
				}
				break;

			case ModelType.Studio:
				return MDLCache.GetStudioHdr(model.Studio);
			default:
			case ModelType.Brush:
				// Should never happen
				Assert(false);
				break;
		}

		return null;
	}

	public int GetModelFileSize(ReadOnlySpan<char> name) {
		throw new NotImplementedException();
	}

	public Model GetModelForIndex(int i) {
		throw new NotImplementedException();
	}

	public Model? GetModelForName(ReadOnlySpan<char> name, ModelLoaderFlags referenceType) {
		Model? model = FindModel(name);
		Model? retval = LoadModel(model, ref referenceType);

		return retval;
	}

	InlineArray64<char> ActiveMapName;
	InlineArray64<char> LoadName;

	public ReadOnlySpan<char> ActiveMapNameSliced() => ((ReadOnlySpan<char>)ActiveMapName).SliceNullTerminatedString();
	public ReadOnlySpan<char> LoadNameSliced() => ((ReadOnlySpan<char>)LoadName).SliceNullTerminatedString();

	Model? WorldModel;
	double accumulatedModelLoadTimeStudio;
	double accumulatedModelLoadTimeVCollideSync;
	double accumulatedModelLoadTimeVCollideAsync;
	double accumulatedModelLoadTimeMaterialNamesOnly;
	double accumulatedModelLoadTimeVirtualModel;

	private Model? LoadModel(Model? mod, ref ModelLoaderFlags referenceType) {
		mod!.LoadFlags |= referenceType;

		bool touchAllData = false;
		int serverCount = Host.GetServerCount();
		if (mod.ServerCount != serverCount) {
			mod.ServerCount = serverCount;
			touchAllData = true;
		}

		if (mod.Type == ModelType.Studio && 0 == (mod.LoadFlags & ModelLoaderFlags.LoadedByPreload)) {
			Assert(MDLCache.GetStudioHdr(mod.Studio) != null);
			Assert((mod.LoadFlags & ModelLoaderFlags.Loaded) != 0);

			if (touchAllData) {
				// Touch all related .ani files and sub/dependent models
				// only touches once, when server changes
				Mod_TouchAllData(mod, serverCount);
			}

			return mod;
		}

		if ((mod.LoadFlags & ModelLoaderFlags.Loaded) != 0)
			return mod;

		double st = Platform.Time;
		mod.StrName.String()!.FileBase(LoadName);
		if (Host.developer.GetInt() > 1)
			DevMsg($"Loading: {mod.StrName.String()}\n");

		mod.Type = GetTypeFromName(mod.StrName);
		if (mod.Type == ModelType.Invalid)
			mod.Type = ModelType.Studio;

		switch (mod.Type) {
			case ModelType.Sprite: {
					double t1 = Platform.Time;
					Sprite_LoadModel(mod);
					double t2 = Platform.Time;
					accumulatedModelLoadTimeStudio = (t2 - t1);
				}
				break;
			case ModelType.Studio: {
					double t1 = Platform.Time;
					Studio_LoadModel(mod, touchAllData);
					double t2 = Platform.Time;
					accumulatedModelLoadTimeStudio = (t2 - t1);
				}
				break;
			case ModelType.Brush: {
					fileSystem.AddSearchPath(mod.StrName, "GAME", SearchPathAdd.ToHead, groupName: PathGroupName.Map);

					// exclude textures later

					strcpy(ActiveMapName, mod.StrName);

					fileSystem.BeginMapAccess();
					Map_LoadModel(mod);
					fileSystem.EndMapAccess();
				}
				break;
		}
		return mod;
	}

	private void Sprite_LoadModel(Model mod) {
		Assert((mod.LoadFlags & ModelLoaderFlags.Loaded) == 0);

		mod.LoadFlags |= ModelLoaderFlags.Loaded;

		mod.Type = ModelType.Sprite;
		mod.Sprite.Sprite = new EngineSprite();

		// Fake the bounding box. We need it for PVS culling, and we don't
		// know the scale at which the sprite is going to be rendered at
		// when we load it
		mod.Mins = mod.Maxs = new(0, 0, 0);

		// Figure out the real load name..
		Span<char> loadName = stackalloc char[MAX_PATH];
		bool bIsVideo;
		BuildSpriteLoadName(mod.StrName, loadName, out bIsVideo);
		GetSpriteInfo(loadName, bIsVideo, out mod.Sprite.Width, out mod.Sprite.Height, out mod.Sprite.NumFrames);

#if !SWDS
		if (g_ClientDLL != null && mod.Sprite.Sprite != null)
			g_ClientDLL.InitSprite(mod.Sprite.Sprite, loadName);
#endif
	}

	private void GetSpriteInfo(Span<char> pName, bool bIsVideo, out int nWidth, out int nHeight, out int nFrameCount) {
		nFrameCount = 1;
		nWidth = nHeight = 1;

		// TODO: videos

		IMaterial? pMaterial = null;
		pMaterial = matSys.GL_LoadMaterial(pName, MaterialDefines.TEXTURE_GROUP_OTHER);
		if (pMaterial != null) {
			// Store off our source height, width, frame count
			nWidth = (int)pMaterial.GetMappingWidth();
			nHeight = (int)pMaterial.GetMappingHeight();
			nFrameCount = pMaterial.GetNumAnimationFrames();
		}

		if (pMaterial == matSys.MaterialEmpty)
			DevMsg($"Missing sprite material {pName}\n");
	}

	private void BuildSpriteLoadName(ReadOnlySpan<char> name, Span<char> pOut, out bool isVideo) {
		Span<char> szBase = stackalloc char[MAX_PATH];
		isVideo = false;
		bool bIsVMT = false;
		ReadOnlySpan<char> pExt = name.GetFileExtension();
		if (!pExt.IsEmpty) {
			bIsVMT = pExt.CompareTo("vmt", StringComparison.OrdinalIgnoreCase) != 0;
			if (!bIsVMT) {
				if (false) {
					isVideo = false; // Need to implement video sprites later
				}
			}
		}

		if ((false || bIsVMT) && name.Contains('/') || name.Contains('\\')) {
			// The material system cannot handle a prepended "materials" dir
			// Keep .avi extensions on the material to load avi-based materials
			if (bIsVMT) {
				ReadOnlySpan<char> nameStart = name;
				if (name.StartsWith("materials/") ||
					name.StartsWith("materials\\")) {
					// skip past materials/
					nameStart = name[10..];
				}
				StrTools.StripExtension(nameStart, pOut);
			}
			else {
				// name is good as is
				name.CopyTo(pOut);
			}
		}
		else {
			name.FileBase(szBase);
			unsafe {
				sprintf(pOut, "sprites/%s").S(szBase);
			}
		}

		return;
	}

	public static readonly ConVar r_unloadlightmaps = new("r_unloadlightmaps", "0", FCvar.Cheat);
	static readonly ConVar mod_touchalldata = new("1", 0, "Touch model data during level startup");
	static readonly ConVar mod_forcetouchdata = new("1", 0, "Forces all model file data into cache on model load.");

	private void Studio_LoadModel(Model model, bool touchAllData) {
		if (!mod_touchalldata.GetBool())
			touchAllData = false;

		bool preloaded = (model.LoadFlags & ModelLoaderFlags.LoadedByPreload) != 0;

		bool loadPhysics = true;
		if (model.LoadFlags == ModelLoaderFlags.StaticProp)
			loadPhysics = false;

		model.LoadFlags |= ModelLoaderFlags.Loaded;
		model.LoadFlags &= ~ModelLoaderFlags.LoadedByPreload;

		if (!preloaded) {
			model.Studio = MDLCache.FindMDL(model.StrName);
			MDLCache.SetUserData(model.Studio, model);
			InitStudioModelState(model);
		}

		MDLCache.GetStudioHdr(model.Studio);
		if (loadPhysics && !preloaded) {
			bool synchronous = touchAllData;
			double t1 = Platform.Time;
			MDLCache.GetVCollideEx(model.Studio, synchronous);
			double t2 = Platform.Time;

			if (synchronous)
				accumulatedModelLoadTimeVCollideSync += t2 - t1;
			else
				accumulatedModelLoadTimeVCollideAsync += t2 - t1;
		}

		{
			double t1 = Platform.Time;
			model.Materials = default;

			IMaterial[] materials = ArrayPool<IMaterial>.Shared.Rent(128);
			int nMaterials = Mod_GetModelMaterials(model, materials);

			if ((model.LoadFlags & ModelLoaderFlags.Dynamic) != 0) {
				model.Materials = new Memory<IMaterial>(new IMaterial[nMaterials]);
				for (int i = 0; i < nMaterials; i++)
					model.Materials.Span[i] = materials[i];
			}

			if (nMaterials != 0)
				model.LoadFlags |= ModelLoaderFlags.TouchedMaterials;

			double t2 = Platform.Time;
			accumulatedModelLoadTimeMaterialNamesOnly += (t2 - t1);

			if (touchAllData || preloaded)
				Mod_TouchAllData(model, Host.GetServerCount());

			ArrayPool<IMaterial>.Shared.Return(materials, true);
		}
	}

	internal void Mod_RecomputeTranslucency(Model mod, int skin, int body, object? clientRenderable, float instanceAlphaModulate) {
		if (instanceAlphaModulate < 1.0f) {
			mod.Flags |= ModelFlag.Translucent;
			return;
		}

		mod.Flags &= ~ModelFlag.Translucent;

		switch (mod.Type) {
			case ModelType.Brush: {
					for (int i = 0; i < mod.Brush.NumModelSurfaces; ++i) {
						ref BSPMSurface2 surfID = ref SurfaceHandleFromIndex(mod.Brush.FirstModelSurface + i, mod.Brush.Shared);
						if ((MSurf_Flags(ref surfID) & SurfDraw.NoDraw) != 0)
							continue;

						IMaterial material = MSurf_TexInfo(ref surfID, mod.Brush.Shared).Material!;
						if (material.IsTranslucent()) {
							mod.Flags |= ModelFlag.Translucent;
							break;
						}
					}
				}
				break;

			case ModelType.Studio: {
					StudioHeader studioHdr = MDLCache.GetStudioHdr(mod.Studio)!;
					if ((studioHdr.Flags & StudioHdrFlags.ForceOpaque) != 0)
						return;

					Span<IMaterial> materials = new IMaterial[128];
					int materialCount = StudioRender.GetMaterialListFromBodyAndSkin(mod.Studio, skin, body, materials);
					for (int i = 0; i < materialCount; i++) {
						if (materials[i] != null) {
							using MatRenderContextPtr renderContext = new(SourceDllMain.materials);
							renderContext.Bind(materials[i], clientRenderable);
							bool isTranslucent = materials[i].IsTranslucent();

							if (isTranslucent) {
								mod.Flags |= ModelFlag.Translucent;
								break;
							}
						}
					}
				}
				break;
		}
	}

	internal int Mod_GetModelMaterials(Model model, Span<IMaterial> materials) {
		StudioHeader studioHdr;
		int found = 0;
		int i;

		switch (model.Type) {
			case ModelType.Brush: {
					for (i = 0; i < model.Brush.NumModelSurfaces; ++i) {
						ref BSPMSurface2 surfID = ref SurfaceHandleFromIndex(model.Brush.FirstModelSurface + i, model.Brush.Shared);
						if ((MSurf_Flags(ref surfID) & SurfDraw.NoDraw) != 0)
							continue;

						IMaterial? material = MSurf_TexInfo(ref surfID, model.Brush.Shared).Material;

						int j = found;
						while (--j >= 0) {
							if (materials[j] == material)
								break;
						}
						if (j < 0)
							materials[found++] = material!;

						if (found >= materials.Length)
							return found;
					}
				}
				break;

			case ModelType.Studio:
				if (!model.Materials.IsEmpty) {
					model.Materials.Span.CopyTo(materials);
				}
				else {
					studioHdr = MDLCache.GetStudioHdr(model.Studio);
					found = StudioRender.GetMaterialList(studioHdr, materials);
				}
				break;

			default:
				Assert(false);
				break;
		}

		return found;
	}

	private void Mod_TouchAllData(Model model, int serverCount) {
		double t1 = Platform.Time;

		VirtualModel virtualModel = MDLCache.GetVirtualModel(model.Studio);

		double t2 = Platform.Time;
		accumulatedModelLoadTimeVirtualModel += (t2 - t1);

		if (virtualModel != null && serverCount >= 1) {
			for (int i = 1; i < virtualModel.Group.Count; ++i) {
				// What the hell?
				MDLHandle_t childHandle = (MDLHandle_t)virtualModel.Group[i].Cache & 0xffff;
				Model? childModel = MDLCache.GetUserData<Model>(childHandle);
				if (childModel != null) {
					childModel.LoadFlags |= (model.LoadFlags & ModelLoaderFlags.ReferenceMask);
					childModel.LoadFlags |= ModelLoaderFlags.Loaded;
					childModel.LoadFlags &= ~ModelLoaderFlags.LoadedByPreload;
					childModel.ServerCount = serverCount;
				}
			}
		}

		if (!mod_forcetouchdata.GetBool())
			return;

		MDLCache.TouchAllData(model.Studio);
	}

	private void InitStudioModelState(Model model) {
		Assert(model.Type == ModelType.Studio);

		if (MDLCache.IsDataLoaded(model.Studio, MDLCacheDataType.StudioHDR))
			MDLCacheNotify.s.OnDataLoaded(MDLCacheDataType.StudioHDR, model.Studio);

		if (MDLCache.IsDataLoaded(model.Studio, MDLCacheDataType.StudioHWData))
			MDLCacheNotify.s.OnDataLoaded(MDLCacheDataType.StudioHWData, model.Studio);

		if (MDLCache.IsDataLoaded(model.Studio, MDLCacheDataType.VCollide))
			MDLCacheNotify.s.OnDataLoaded(MDLCacheDataType.VCollide, model.Studio);
	}

	public bool Map_IsValid(ReadOnlySpan<char> name, bool quiet = false) {
		name = name.SliceNullTerminatedString();
		if (name.IsEmpty || name[0] == '\0') {
			if (!quiet)
				ConMsg("ModelLoader.Map_IsValid: Empty mapname!\n");
			return false;
		}

		ReadOnlySpan<char> baseName = name.UnqualifiedFileName();

		bool illegalChar = false;
		for (int i = 0; i < baseName.Length; i++) {
			if (baseName[i] <= 31)
				illegalChar = true;

			switch (baseName[i]) {
				case '<':
				case '>':
				case ':':
				case '"':
				case '/':
				case '\\':
				case '|':
				case '?':
				case '*':
				case ';':
				case '\'':
					illegalChar = true;
					break;
			}
		}

		if (illegalChar) {
			AssertMsg(false, "Map with illegal characters in filename");
			Warning("Map with illegal characters in filename\n");
			return false;
		}

		IFileHandle? mapFile = fileSystem.Open(name, FileOpenOptions.Read | FileOpenOptions.Binary, "GAME");
		if (mapFile != null) {
			BSPDHeader header = default;
			Span<byte> outWrite = MemoryMarshal.Cast<BSPDHeader, byte>(new Span<BSPDHeader>(ref header));
			int len = mapFile.Stream.Read(outWrite);
			mapFile.Dispose();
			if (len != outWrite.Length) {
				if (!quiet)
					Warning($"ModelLoader.Map_IsValid: Map '{name}' was not large enough to contain a BSP header\n");
			}
			else {
				if (header.Identifier == BSPFileCommon.IDBSPHEADER) {
					if (header.Version >= BSPFileCommon.MINBSPVERSION && header.Version <= BSPFileCommon.BSPVERSION) {
						name.ClampedCopyTo(LastMapFile);
						return true;
					}
					else if (!quiet)
						Warning($"ModelLoader.Map_IsValid: Map '{name}' bsp version {header.Version}, expecting {BSPFileCommon.MINBSPVERSION} to {BSPFileCommon.BSPVERSION}\n");
				}
				else if (!quiet)
					Warning($"ModelLoader.Map_IsValid: Map '{name}' is not a valid BSP file\n");
			}
		}
		else if (!quiet)
			Warning($"ModelLoader.Map_IsValid: No such map '{name}'\n");

		return false;
	}

	InlineArrayMaxPath<char> LastMapFile;

	int MapLoadCount;
	public readonly WorldBrushData WorldBrushData = new();

	private void Mod_LoadLump<T>(Model loadmodel, LumpIndex lump, ReadOnlySpan<char> loadName, ref T[]? data) where T : unmanaged {
		int elements = 0;
		Mod_LoadLump(loadmodel, lump, loadName, ref data, ref elements);
	}
	private void Mod_LoadLump<T>(Model loadmodel, LumpIndex lump, ReadOnlySpan<char> loadName, ref T[]? data, ref int elements) where T : unmanaged {
		int elementSize = Unsafe.SizeOf<T>();
		MapLoadHelper lh = new MapLoadHelper(lump);
		if ((lh.LumpSize % elementSize) != 0)
			Host.Error($"Mod_LoadLump: funny lump size in {loadmodel.StrName.String()}");

		elements = lh.LumpSize / elementSize;
		data = lh.LoadLumpData<T>();
	}

	private void Map_LoadModel(Model mod) {
		MapLoadCount++;

		Common.TimestampedLog("Map_LoadModel: Start");

		double startTime = Platform.Time;

#if !SWDS
		EngineVGui.UpdateProgressBar(LevelLoadingProgress.LoadWorldModel);
#endif

		SetWorldModel(mod);
		mod.Brush.Shared = WorldBrushData;
		mod.Brush.RenderHandle = 0;

		Common.TimestampedLog("  Map_CheckForHDR");
		// todo

		Common.TimestampedLog("  CM_LoadMap");
		CM.LoadMap(mod.StrName, false, out uint checksum);

		mod.Type = ModelType.Brush;
		mod.LoadFlags |= ModelLoaderFlags.Loaded;
		if (!MapLoadHelper.Init(mod, LoadNameSliced()))
			return;

		Common.TimestampedLog("  Mod_LoadVertices");
		Mod_LoadVertices();
		Common.TimestampedLog("  Mod_LoadEdges");
		BSPDEdge[] edges = Mod_LoadEdges();
		Common.TimestampedLog("  Mod_LoadSurfedges");
		Mod_LoadSurfedges(edges);
		Common.TimestampedLog("  Mod_LoadPlanes");
		Mod_LoadPlanes();
		// Common.TimestampedLog("  Mod_LoadOcclusion");
		// Mod_LoadOcclusion();
		Common.TimestampedLog("  Mod_LoadTexdata");
		Mod_LoadTexdata();
		Common.TimestampedLog("  Mod_LoadTexinfo");
		Mod_LoadTexinfo();

#if !SWDS
		EngineVGui.UpdateProgressBar(LevelLoadingProgress.LoadWorldModel);
#endif

		Common.TimestampedLog("  Mod_LoadLighting");
		if (materialSystemHardwareConfig.GetHDRType() != HDRType.None && MapLoadHelper.GetLumpSize(LumpIndex.LightingHDR) > 0) {
			MapLoadHelper mlh = new(LumpIndex.LightingHDR);
			Map_LoadLighting(mlh);
		}
		else {
			MapLoadHelper mlh = new(LumpIndex.Lighting);
			Map_LoadLighting(mlh);
		}

		Common.TimestampedLog("  Mod_LoadPrimitives");
		Mod_LoadPrimitives();
		Common.TimestampedLog("  Mod_LoadPrimVerts");
		Mod_LoadPrimVerts();
		Common.TimestampedLog("  Mod_LoadPrimIndices");
		Mod_LoadPrimIndices();

#if !SWDS
		EngineVGui.UpdateProgressBar(LevelLoadingProgress.LoadWorldModel);
#endif

		Common.TimestampedLog("  Mod_LoadFaces");
		Mod_LoadFaces();
		Common.TimestampedLog("  Mod_LoadVertNormals");
		Mod_LoadVertNormals();
		Common.TimestampedLog("  Mod_LoadVertNormalIndices");
		Mod_LoadVertNormalIndices();

#if !SWDS
		EngineVGui.UpdateProgressBar(LevelLoadingProgress.LoadWorldModel);
#endif

		Common.TimestampedLog("  Mod_LoadLeafs");
		Mod_LoadLeafs();
		Common.TimestampedLog("  Mod_LoadMarksurfaces");
		Mod_LoadMarksurfaces();
		Common.TimestampedLog("  Mod_LoadNodes");
		Mod_LoadNodes();
		Common.TimestampedLog("  Mod_LoadLeafWaterData");
		Mod_LoadLeafWaterData();
		Common.TimestampedLog("  Mod_LoadCubemapSamples");
		Mod_LoadCubemapSamples();

		// TODO: Load overlays


		Common.TimestampedLog("  Mod_LoadLeafMinDistToWater");
		Mod_LoadLeafMinDistToWater();

#if !SWDS
		EngineVGui.UpdateProgressBar(LevelLoadingProgress.LoadWorldModel);
#endif

		Common.TimestampedLog("  LUMP_CLIPPORTALVERTS");
		Mod_LoadLump(mod, LumpIndex.ClipPortalVerts, $"{LoadNameSliced()} [clipportalverts]", ref WorldBrushData.ClipPortalVerts);
		Common.TimestampedLog("  LUMP_AREAPORTALS");
		Mod_LoadLump(mod, LumpIndex.AreaPortals, $"{LoadNameSliced()} [areaportals]", ref WorldBrushData.AreaPortals, ref WorldBrushData.NumAreaPortals);
		Common.TimestampedLog("  LUMP_AREAS");
		Mod_LoadLump(mod, LumpIndex.Areas, $"{LoadNameSliced()} [areas]", ref WorldBrushData.Areas, ref WorldBrushData.NumAreas);

		Common.TimestampedLog("  Mod_LoadWorldlights");
		if (materialSystemHardwareConfig.GetHDRType() != HDRType.None && MapLoadHelper.GetLumpSize(LumpIndex.WorldLightsHDR) > 0) {
			MapLoadHelper mlh = new(LumpIndex.WorldLightsHDR);
			Mod_LoadWorldlights(ref mlh, true);
		}
		else {
			MapLoadHelper mlh = new(LumpIndex.WorldLights);
			Mod_LoadWorldlights(ref mlh, false);
		}

		Common.TimestampedLog("  Mod_LoadGameLumpDict");
		LoadGameLumpDict();
#if !SWDS
		EngineVGui.UpdateProgressBar(LevelLoadingProgress.LoadWorldModel);
#endif
		Common.TimestampedLog("  Mod_LoadSubmodels");
		List<BSPMModel> submodelList = [];
		Mod_LoadSubmodels(submodelList);

#if !SWDS
		EngineVGui.UpdateProgressBar(LevelLoadingProgress.LoadWorldModel);
#endif

		Common.TimestampedLog("  SetupSubModels");
		SetupSubModels(mod, submodelList);

		Common.TimestampedLog("  RecomputeSurfaceFlags");
		RecomputeSurfaceFlags(mod);

#if !SWDS
		EngineVGui.UpdateProgressBar(LevelLoadingProgress.LoadWorldModel);
#endif

		Common.TimestampedLog("  Map_VisClear");
		Map_VisClear();

		Common.TimestampedLog("  Map_SetRenderInfoAllocated");
		Map_SetRenderInfoAllocated(false);

		MapLoadHelper.Shutdown();
		double elapsed = Platform.Time - startTime;
		Common.TimestampedLog($"Map_LoadModel: Finish - loading took {elapsed:F4} seconds");
	}

	public void Mod_LoadWorldlights(ref MapLoadHelper lh, bool isHDR) {
		WorldBrushData map = lh.GetMap();
		map.ShadowZBuffers = null;
		if (lh.LumpSize == 0) {
			map.NumWorldLights = 0;
			map.WorldLights = null;
			return;
		}
		map.NumWorldLights = lh.LumpSize / Unsafe.SizeOf<BSPDWorldLight>();
		map.WorldLights = lh.LoadLumpData<BSPDWorldLight>();
#if !SWDS
		if (Render.r_lightcache_zbuffercache.GetInt() != 0) {
			int zbufSize = map.NumWorldLights * Unsafe.SizeOf<LightZBuffer>();
			map.ShadowZBuffers = new LightZBuffer[map.NumWorldLights];
		}
#endif

		// Fixup for backward compatability
		for (int i = 0; i < map.NumWorldLights; i++) {
			if (map.WorldLights![i].Type == EmitType.SpotLight) {
				if ((map.WorldLights![i].ConstantAttn == 0.0) &&
					(map.WorldLights![i].LinearAttn == 0.0) &&
					(map.WorldLights![i].QuadraticAttn == 0.0)) {
					map.WorldLights![i].QuadraticAttn = 1.0f;
				}

				if (map.WorldLights![i].Exponent == 0.0f)
					map.WorldLights![i].Exponent = 1.0f;
			}
			else if (map.WorldLights![i].Type == EmitType.Point) {
				// To match earlier lighting, use quadratic...
				if ((map.WorldLights![i].ConstantAttn == 0.0) && (map.WorldLights![i].LinearAttn == 0.0) && (map.WorldLights![i].QuadraticAttn == 0.0))
					map.WorldLights![i].QuadraticAttn = 1.0f;

			}

			// I replaced the cuttoff_dot field (which took a value from 0 to 1)
			// with a max light radius. Radius of less than 1 will never happen,
			// so I can get away with this. When I set radius to 0, it'll 
			// run the old code which computed a radius
			if (map.WorldLights![i].Radius < 1)
				map.WorldLights![i].Radius = ComputeLightRadius(ref map.WorldLights![i], isHDR);
		}
	}

	public const float LIGHT_MIN_LIGHT_VALUE = 0.03f;

	float ComputeLightRadius(ref BSPDWorldLight light, bool isHDR) {
		float flLightRadius = light.Radius;
		if (flLightRadius == 0.0f) {
			// HACKHACK: Usually our designers scale the light intensity by 0.5 in HDR
			// This keeps the behavior of the cutoff radius consistent between LDR and HDR
			float minLightValue = isHDR ? (LIGHT_MIN_LIGHT_VALUE * 0.5f) : LIGHT_MIN_LIGHT_VALUE;

			// Compute the light range based on attenuation factors
			float flIntensity = MathF.Sqrt(MathLib.DotProduct(light.Intensity, light.Intensity));
			if (light.QuadraticAttn == 0.0f) {
				if (light.LinearAttn == 0.0f)
					// Infinite, but we're not going to draw it as such
					flLightRadius = 2000;
				else
					flLightRadius = (flIntensity / minLightValue - light.ConstantAttn) / light.LinearAttn;
			}
			else {
				float a = light.QuadraticAttn;
				float b = light.LinearAttn;
				float c = light.ConstantAttn - flIntensity / minLightValue;
				float discrim = b * b - 4 * a * c;
				if (discrim < 0.0f)
					// Infinite, but we're not going to draw it as such
					flLightRadius = 2000;
				else {
					flLightRadius = (-b + MathF.Sqrt(discrim)) / (2.0f * a);
					if (flLightRadius < 0)
						flLightRadius = 0;
				}
			}
		}

		return flLightRadius;
	}


	struct BrushBSPIterator : ISpatialLeafEnumerator
	{
		Model World;
		Model Brush;
		WorldBrushData Shared;
		int Count;

		public BrushBSPIterator(Model world, Model brush) {
			World = world;
			Brush = brush;
			Shared = Brush.Brush.Shared!;
			Count = 0;
		}

		public bool EnumerateLeaf(int leaf, nint context) {
			SurfDraw flags = (Shared.Leafs![leaf].LeafWaterDataID == -1) ? SurfDraw.AboveWater : SurfDraw.UnderWater;
			MarkModelSurfaces(flags);
			Count++;
			return true;
		}

		void MarkModelSurfaces(SurfDraw flags) {
			// Iterate over all this models surfaces
			int surfaceCount = Brush.Brush.NumModelSurfaces;
			for (int i = 0; i < surfaceCount; ++i) {
				ref BSPMSurface2 surfID = ref SurfaceHandleFromIndex(Brush.Brush.FirstModelSurface + i, Shared);
				MSurf_Flags(ref surfID) &= ~(SurfDraw.AboveWater | SurfDraw.UnderWater);
				MSurf_Flags(ref surfID) |= flags;
			}
		}

		public void CheckSurfaces() {
			if (Count == 0)
				MarkModelSurfaces(SurfDraw.AboveWater);
		}
	}


	static void MarkBrushModelWaterSurfaces(Model world, in Vector3 mins, in Vector3 maxs, Model brush) {
		Model pTemp = host_state.WorldModel!;
		BrushBSPIterator brushIterator = new(world, brush);
		host_state.SetWorldModel(world);
		g_ToolBSPTree.EnumerateLeavesInBox(mins, maxs, ref brushIterator, brush.GetHashCode());
		brushIterator.CheckSurfaces();
		host_state.SetWorldModel(pTemp);
	}

	public void RecomputeSurfaceFlags(Model mod) {
		for (int i = 0; i < mod.Brush.Shared!.NumSubModels; i++) {
			Model subModel = InlineModels[i];

			Mod_ComputeBrushModelFlags(subModel);

			if (i != 0)
				MarkBrushModelWaterSurfaces(mod, subModel.Mins, subModel.Maxs, subModel);
		}
	}

	public static void Mod_ComputeBrushModelFlags(Model mod) {
		WorldBrushData brushData = mod.Brush.Shared!;
		// Clear out flags we're going to set
		mod.Flags &= ~(ModelFlag.MaterialProxy | ModelFlag.Translucent | ModelFlag.FramebufferTexture | ModelFlag.TranslucentTwoPass);
		mod.Flags = ModelFlag.HasDLight; // force this check the first time

		int i;
		int scount = mod.Brush.NumModelSurfaces;
		bool bHasOpaqueSurfaces = false;
		bool bHasTranslucentSurfaces = false;
		for (i = 0; i < scount; ++i) {
			ref BSPMSurface2 surfID = ref SurfaceHandleFromIndex(mod.Brush.FirstModelSurface + i, brushData);

			// Clear out flags we're going to set
			MSurf_Flags(ref surfID) &= ~(SurfDraw.NoCull | SurfDraw.Trans | SurfDraw.AlphaTest | SurfDraw.NoDecals);

			ref ModelTexInfo pTex = ref MSurf_TexInfo(ref surfID, brushData);
			IMaterial material = pTex.Material!;

			if (material.HasProxy())
				mod.Flags |= ModelFlag.MaterialProxy;


			if (material.NeedsPowerOfTwoFrameBufferTexture(false)) // The false checks if it will ever need the frame buffer, not just this frame
				mod.Flags |= ModelFlag.FramebufferTexture;

			// Deactivate culling if the material is two sided
			if (material.IsTwoSided())
				MSurf_Flags(ref surfID) |= SurfDraw.NoCull;

			if ((pTex.Flags & Surf.Trans) != 0 || material.IsTranslucent()) {
				mod.Flags |= ModelFlag.Translucent;
				MSurf_Flags(ref surfID) |= SurfDraw.Trans;
				bHasTranslucentSurfaces = true;
			}
			else
				bHasOpaqueSurfaces = true;

			if ((pTex.Flags & Surf.NoDecals) != 0 || material.GetMaterialVarFlag(MaterialVarFlags.SuppressDecals) || material.IsAlphaTested())
				MSurf_Flags(ref surfID) |= SurfDraw.NoDecals;

			if (material.IsAlphaTested())
				MSurf_Flags(ref surfID) |= SurfDraw.AlphaTest;
		}

		if (bHasOpaqueSurfaces && bHasTranslucentSurfaces)
			mod.Flags |= ModelFlag.TranslucentTwoPass;
	}

	bool MapRenderInfoLoaded;
	public bool Map_GetRenderInfoAllocated() => MapRenderInfoLoaded;
	private void Map_SetRenderInfoAllocated(bool allocated) => MapRenderInfoLoaded = allocated;

	private void Mod_LoadLeafWaterData() {
		MapLoadHelper lh = new(LumpIndex.LeafWaterData);

		BSPDLeafWaterData[] _in = lh.LoadLumpData<BSPDLeafWaterData>();
		if ((lh.LumpSize % Unsafe.SizeOf<BSPDLeafWaterData>()) != 0)
			Host.Error($"Mod_LoadLeafs: funny lump size in {lh.GetMapName()}");
		int count = lh.LumpSize / Unsafe.SizeOf<BSPDLeafWaterData>();
		BSPMLeafWaterData[] _out = new BSPMLeafWaterData[count];

		lh.GetMap().LeafWaterData = _out;
		for (int i = 0; i < count; i++) {
			_out[i].MinZ = _in[i].MinZ;
			_out[i].SurfaceTexInfoID = _in[i].SurfaceTexInfoID;
			_out[i].SurfaceZ = _in[i].SurfaceZ;
			_out[i].FirstLeafIndex = -1;
		}

		if (count == 1) {
			WorldBrushData brush = lh.GetMap();
			for (int i = 0; i < brush.NumLeafs; i++) {
				if (brush.Leafs![i].LeafWaterDataID >= 0) {
					brush.LeafWaterData![0].FirstLeafIndex = (short)i;
					break;
				}
			}
		}
	}

	private void Mod_LoadCubemapSamples() {
		Span<char> textureName = stackalloc char[512];
		Span<char> loadName = stackalloc char[MAX_PATH];
		ReadOnlySpan<BSPDCubeMapSample> inSample;
		BSPMCubeMapSample[] outSample;
		int count, i;

		MapLoadHelper lh = new(LumpIndex.Cubemaps);
		strcpy(loadName, lh.GetLoadName());

		inSample = lh.LoadLumpData<BSPDCubeMapSample>();
		if ((lh.LumpSize % Unsafe.SizeOf<BSPDCubeMapSample>()) != 0)
			Host.Error($"Mod_LoadCubemapSamples: funny lump size in {lh.GetMapName()}");
		count = lh.LumpSize / Unsafe.SizeOf<BSPDCubeMapSample>();
		outSample = new BSPMCubeMapSample[count];

		lh.GetMap().CubemapSamples = outSample;
		lh.GetMap().NumCubemapSamples = count;

		bool hdr = materialSystemHardwareConfig.GetHDRType() != HDRType.None;
		TextureFlags createFlags = hdr ? 0 : TextureFlags.SRGB;

		// We have separate HDR versions of the textures.  In order to deal with this,
		// we have blahenvmap.hdr.vtf and blahenvmap.vtf.
		ReadOnlySpan<char> hdrExtension = "";
		if (hdr)
			hdrExtension = ".hdr";


		for (i = 0; i < count; i++) {
			ref readonly BSPDCubeMapSample inCurrent = ref inSample[i];
			ref BSPMCubeMapSample outCurrent = ref outSample[i];
			outCurrent.Origin.Init((float)inCurrent.Origin[0], (float)inCurrent.Origin[1], (float)inCurrent.Origin[2]);
			outCurrent.Size = inCurrent.Size;
			textureName.Clear();
			sprintf(textureName, "maps/%s/c%d_%d_%d%s").S(loadName).D((int)inCurrent.Origin[0]).D((int)inCurrent.Origin[1]).D((int)inCurrent.Origin[2]).S(hdrExtension);
			ReadOnlySpan<char> cubemapName = textureName.SliceNullTerminatedString();
			outCurrent.Texture = materialSystem.FindTexture(cubemapName, MaterialDefines.TEXTURE_GROUP_CUBE_MAP, true, (CreateTextureFlags)createFlags);
			if (ITexture.IsError(outCurrent.Texture)) {
				if (hdr) {
					Warning($"Couldn't get HDR '{cubemapName}' -- ");
					// try non hdr version
					textureName.Clear();
					sprintf(textureName, "maps/%s/c%d_%d_%d").S(loadName).D((int)inCurrent.Origin[0]).D((int)inCurrent.Origin[1]).D((int)inCurrent.Origin[2]);
					cubemapName = textureName.SliceNullTerminatedString();
					Warning($"Trying non HDR '{cubemapName}'\n");
					outCurrent.Texture = materialSystem.FindTexture(cubemapName, MaterialDefines.TEXTURE_GROUP_CUBE_MAP, true);
				}

				if (ITexture.IsError(outCurrent.Texture)) {
					textureName.Clear();
					sprintf(textureName, "maps/%s/cubemapdefault").S(loadName);
					cubemapName = textureName.SliceNullTerminatedString();
					outCurrent.Texture = materialSystem.FindTexture(cubemapName, MaterialDefines.TEXTURE_GROUP_CUBE_MAP, true, (CreateTextureFlags)createFlags);
					if (ITexture.IsError(outCurrent.Texture))
						outCurrent.Texture = materialSystem.FindTexture("engine/defaultcubemap", MaterialDefines.TEXTURE_GROUP_CUBE_MAP, true, (CreateTextureFlags)createFlags);

					Warning($"Failed, using default cubemap '{outCurrent.Texture.GetName()}'\n");
				}
			}
			outCurrent.Texture.IncrementReferenceCount();
		}

		using MatRenderContextPtr renderContext = new(materialSystem);

		if (count != 0)
			renderContext.BindLocalCubemap(lh.GetMap().CubemapSamples![0].Texture);
		else {
			if (commandLine.CheckParm("-requirecubemaps"))
				Sys.Error($"Map \"{lh.GetMapName()}\" does not have cubemaps!");

			ITexture? pTexture;
			textureName.Clear();
			sprintf(textureName, "maps/%s/cubemapdefault").S(loadName);
			pTexture = materialSystem.FindTexture(textureName.SliceNullTerminatedString(), MaterialDefines.TEXTURE_GROUP_CUBE_MAP, true, (CreateTextureFlags)createFlags);
			if (ITexture.IsError(pTexture))
				pTexture = materialSystem.FindTexture("engine/defaultcubemap", MaterialDefines.TEXTURE_GROUP_CUBE_MAP, true, (CreateTextureFlags)createFlags);

			pTexture.IncrementReferenceCount();
			renderContext.BindLocalCubemap(pTexture);
		}
	}

	private void Mod_LoadLeafMinDistToWater() {

	}

	private void Mod_LoadNodes() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Nodes);
		BSPDNode[] inNodes = lh.LoadLumpData<BSPDNode>();

		int count = inNodes.Length;
		BSPMNode[] outNodes = new BSPMNode[count];
		for (int i = 0; i < count; i++)
			outNodes[i] = new BSPMNode();

		WorldBrushData map = lh.GetMap();
		map.Nodes = outNodes;
		map.NumNodes = count;

		CollisionPlane[] planes = map.Planes!;
		BSPMLeaf[] leafs = map.Leafs!;

		for (int i = 0; i < count; i++) {
			ref BSPDNode _in = ref inNodes[i];
			BSPMNode _out = outNodes[i];

			Vector3 mins = new(_in.Mins[0], _in.Mins[1], _in.Mins[2]);
			Vector3 maxs = new(_in.Maxs[0], _in.Maxs[1], _in.Maxs[2]);

			_out.Center = (mins + maxs) * 0.5f;
			_out.HalfDiagonal = maxs - _out.Center;

			_out.Plane = planes[_in.PlaneNum];

			_out.FirstSurface = _in.FirstFace;
			_out.NumSurfaces = _in.NumFaces;
			_out.Area = _in.Area;
			_out.Contents = -1;

			for (int j = 0; j < 2; j++) {
				int p = _in.Children[j];
				if (p >= 0)
					_out.Children[j] = outNodes[p];
				else
					_out.Children[j] = leafs[-1 - p];
			}
		}

		if (count == 0)
			return;

		Mod_SetParent(outNodes[0], null);

		for (int i = 0; i < count; i++) {
			BSPMNode pNode = outNodes[i];
			if (pNode.Contents == -1) {
				if (pNode.HalfDiagonal.X <= 50 && pNode.HalfDiagonal.Y <= 50 && pNode.HalfDiagonal.Z <= 50) {
					MarkSmallNode(pNode.Children[0]!);
					MarkSmallNode(pNode.Children[1]!);
				}
				else {
					CheckSmallVolumeDifferences(pNode.Children[0]!, pNode.HalfDiagonal);
					CheckSmallVolumeDifferences(pNode.Children[1]!, pNode.HalfDiagonal);
				}
			}
		}
	}

	static void Mod_SetParent(BSPMNode node, BSPMNode? parent) {
		node.Parent = parent;
		if (node.Contents >= 0)
			return;
		Mod_SetParent(node.Children[0]!, node);
		Mod_SetParent(node.Children[1]!, node);
	}

	static void MarkSmallNode(BSPMNode node) {
		if (node.Contents >= 0)
			return;
		node.Contents = -2;
		MarkSmallNode(node.Children[0]!);
		MarkSmallNode(node.Children[1]!);
	}

	static void CheckSmallVolumeDifferences(BSPMNode pNode, in Vector3 parentSize) {
		if (pNode.Contents >= 0)
			return;

		Vector3 delta = parentSize - pNode.HalfDiagonal;

		if (delta.X < 5 && delta.Y < 5 && delta.Z < 5) {
			pNode.Contents = -3;
			CheckSmallVolumeDifferences(pNode.Children[0]!, parentSize);
			CheckSmallVolumeDifferences(pNode.Children[1]!, parentSize);
		}
	}

	private void Mod_LoadLeafs_Version_0(MapLoadHelper lh) {
		BSPDLeafVersion0[] inLeafs = lh.LoadLumpData<BSPDLeafVersion0>();
		int count = inLeafs.Length;
		BSPMLeaf[] outLeafs = new BSPMLeaf[count];

		lh.GetMap().Leafs = outLeafs;
		lh.GetMap().NumLeafs = count;
		lh.GetMap().LeafAmbient = new MLeafAmbientIndex[count];
		lh.GetMap().AmbientSamples = new MLeafAmbientLighting[count];
		MLeafAmbientIndex[] pTable = lh.GetMap().LeafAmbient!;
		MLeafAmbientLighting[] pSamples = lh.GetMap().AmbientSamples!;

		for (int i = 0; i < count; i++) {
			ref BSPDLeafVersion0 _in = ref inLeafs[i];
			BSPMLeaf _out = outLeafs[i] = new BSPMLeaf();

			Vector3 mins = new(_in.Mins[0], _in.Mins[1], _in.Mins[2]);
			Vector3 maxs = new(_in.Maxs[0], _in.Maxs[1], _in.Maxs[2]);

			_out.Center = (mins + maxs) * 0.5f;
			_out.HalfDiagonal = maxs - _out.Center;

			pTable[i].AmbientSampleCount = 1;
			pTable[i].FirstAmbientSample = (ushort)i;
			pSamples[i].X = pSamples[i].Y = pSamples[i].Z = 128;
			pSamples[i].Pad = 0;
			pSamples[i].Cube = _in.AmbientLighting;

			_out.Contents = _in.Contents;

			_out.Cluster = _in.Cluster;
			_out.Area = _in.Area;
			_out.Flags = _in.Flags;
			_out.FirstMarkSurface = _in.FirstLeafFace;
			_out.NumMarkSurfaces = _in.NumLeafFaces;
			_out.Parent = null;

			_out.DispCount = 0;

			_out.LeafWaterDataID = _in.LeafWaterDataID;
			_out.Index = i;
		}
	}

	private void Mod_LoadLeafs_Version_1(MapLoadHelper lh, MapLoadHelper ambientLightingLump, MapLoadHelper ambientLightingTable) {
		BSPDLeaf[] inLeafs = lh.LoadLumpData<BSPDLeaf>();
		int count = inLeafs.Length;
		BSPMLeaf[] outLeafs = new BSPMLeaf[count];

		lh.GetMap().Leafs = outLeafs;
		lh.GetMap().NumLeafs = count;

		if (ambientLightingLump.LumpVersion != (int)LumpVersions.LUMP_LEAF_AMBIENT_LIGHTING_VERSION || ambientLightingTable.LumpSize == 0) {
			CompressedLightCube[]? inLightCubes = null;
			if (ambientLightingLump.LumpSize != 0) {
				inLightCubes = ambientLightingLump.LoadLumpData<CompressedLightCube>();
				Assert(ambientLightingLump.LumpSize % Unsafe.SizeOf<CompressedLightCube>() == 0);
				Assert(ambientLightingLump.LumpSize / Unsafe.SizeOf<CompressedLightCube>() == lh.LumpSize / Unsafe.SizeOf<BSPDLeaf>());
			}
			lh.GetMap().LeafAmbient = new MLeafAmbientIndex[count];
			lh.GetMap().AmbientSamples = new MLeafAmbientLighting[count];
			MLeafAmbientIndex[] pTable = lh.GetMap().LeafAmbient!;
			MLeafAmbientLighting[] pSamples = lh.GetMap().AmbientSamples!;
			Vector3 gray = new(0.5f, 0.5f, 0.5f);
			MathLib.VectorToColorRGBExp32(gray, out ColorRGBExp32 grayColor);
			for (int i = 0; i < count; i++) {
				pTable[i].AmbientSampleCount = 1;
				pTable[i].FirstAmbientSample = (ushort)i;
				pSamples[i].X = pSamples[i].Y = pSamples[i].Z = 128;
				pSamples[i].Pad = 0;
				if (inLightCubes != null) {
					pSamples[i].Cube = inLightCubes[i];
				}
				else {
					for (int j = 0; j < 6; j++) {
						pSamples[i].Cube.Color[j] = grayColor;
					}
				}
			}
		}
		else {
			Assert(ambientLightingLump.LumpSize % Unsafe.SizeOf<BSPDLeafAmbientLighting>() == 0);
			Assert(ambientLightingTable.LumpSize % Unsafe.SizeOf<BSPDLeafAmbientIndex>() == 0);
			Assert(ambientLightingTable.LumpSize / Unsafe.SizeOf<BSPDLeafAmbientIndex>() == count);
			lh.GetMap().LeafAmbient = ambientLightingTable.LoadLumpData<MLeafAmbientIndex>();
			lh.GetMap().AmbientSamples = ambientLightingLump.LoadLumpData<MLeafAmbientLighting>();
		}

		for (int i = 0; i < count; i++) {
			ref BSPDLeaf _in = ref inLeafs[i];
			BSPMLeaf _out = outLeafs[i] = new BSPMLeaf();

			Vector3 mins = new(_in.Mins[0], _in.Mins[1], _in.Mins[2]);
			Vector3 maxs = new(_in.Maxs[0], _in.Maxs[1], _in.Maxs[2]);

			_out.Center = (mins + maxs) * 0.5f;
			_out.HalfDiagonal = maxs - _out.Center;

			_out.Contents = _in.Contents;

			_out.Cluster = _in.Cluster;
			_out.Area = _in.Area;
			_out.Flags = _in.Flags;
			_out.FirstMarkSurface = _in.FirstLeafFace;
			_out.NumMarkSurfaces = _in.NumLeafFaces;
			_out.Parent = null;

			_out.DispCount = 0;

			_out.LeafWaterDataID = _in.LeafWaterDataID;
			_out.Index = i;
		}
	}

	private void Mod_LoadLeafs() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Leafs);

		switch (lh.LumpVersion) {
			case 0:
				Mod_LoadLeafs_Version_0(lh);
				break;
			case 1:
				if (materialSystemHardwareConfig.GetHDRType() != HDRType.None && MapLoadHelper.GetLumpSize(LumpIndex.LeafAmbientLightingHDR) > 0) {
					MapLoadHelper mlh = new MapLoadHelper(LumpIndex.LeafAmbientLightingHDR);
					MapLoadHelper mlhTable = new MapLoadHelper(LumpIndex.LeafAmbientIndexHDR);
					Mod_LoadLeafs_Version_1(lh, mlh, mlhTable);
				}
				else {
					MapLoadHelper mlh = new MapLoadHelper(LumpIndex.LeafAmbientLighting);
					MapLoadHelper mlhTable = new MapLoadHelper(LumpIndex.LeafAmbientIndex);
					Mod_LoadLeafs_Version_1(lh, mlh, mlhTable);
				}
				break;
			default:
				Assert(false);
				Error("Unknown LUMP_LEAFS version\n");
				break;
		}

		WorldBrushData pMap = lh.GetMap();
		Span<CollisionLeaf> pCLeaf = GetCollisionBSPData().MapLeafs.AsSpan();
		for (int i = 0; i < pMap.Leafs!.Length; i++) {
			pMap.Leafs[i].DispCount = pCLeaf[i].DispCount;
			pMap.Leafs[i].DispListStart = pCLeaf[i].DispListStart;
		}
		pMap.DispInfoReferences = GetCollisionBSPData().MapDispList.Base();
		pMap.NumDispInfoReferences = GetCollisionBSPData().MapDispList.Count;
	}

	private void Mod_LoadMarksurfaces() {
		MapLoadHelper lh = new(LumpIndex.LeafFaces);
		ushort[] _in = lh.LoadLumpData<ushort>();
		int count = _in.Length;
		SurfaceHandle_t[] tempDiskData = new SurfaceHandle_t[count];

		WorldBrushData brushData = lh.GetMap();
		brushData.MarkSurfaces = tempDiskData;
		brushData.NumMarkSurfaces = count;

		int realCount = 0;
		for (int i = 0; i < count; i++) {
			int j = _in[i];
			if (j >= brushData.NumSurfaces)
				Host.Error("Mod_LoadMarksurfaces: bad surface number");
			SurfaceHandle_t surfID = j;
			tempDiskData[i] = surfID;
			ref BSPMSurface2 surf = ref SurfaceHandleFromIndex(surfID, brushData);
			if (!SurfaceHasDispInfo(ref surf) && (MSurf_Flags(ref surf) & SurfDraw.NoDraw) == 0)
				realCount++;
		}

		SurfaceHandle_t[] surfList = new SurfaceHandle_t[realCount];

		int outCount = 0;
		BSPMLeaf[] leaf = brushData.Leafs!;
		for (int i = 0; i < brushData.NumLeafs; i++) {
			int firstMark = outCount;
			int numMark = 0;
			bool foundDetail = false;
			int numMarkNode = 0;
			for (int j = 0; j < leaf[i].NumMarkSurfaces; j++) {
				SurfaceHandle_t surfID = tempDiskData[leaf[i].FirstMarkSurface + j];
				ref BSPMSurface2 surf = ref SurfaceHandleFromIndex(surfID, brushData);
				if (!SurfaceHasDispInfo(ref surf) && (MSurf_Flags(ref surf) & SurfDraw.NoDraw) == 0) {
					surfList[outCount++] = surfID;
					numMark++;
					Assert(outCount <= realCount);
					if ((MSurf_Flags(ref surf) & SurfDraw.Node) != 0) {
						Assert(!foundDetail);
						numMarkNode++;
					}
					else
						foundDetail = true;
				}
			}
			leaf[i].NumMarkSurfaces = (ushort)numMark;
			leaf[i].FirstMarkSurface = (ushort)firstMark;
			leaf[i].NumMarkNodeSurfaces = (ushort)numMarkNode;
		}

		brushData.MarkSurfaces = surfList;
		brushData.NumMarkSurfaces = realCount;
	}

	private void SetupSubModels(Model mod, List<BSPMModel> llist) {
		int i;
		Span<BSPMModel> list = llist.AsSpan();

		InlineModels.EnsureCount(WorldBrushData.NumSubModels);

		for (i = 0; i < WorldBrushData.NumSubModels; i++) {
			Model starmod = InlineModels[i];
			ref BSPMModel bm = ref list[i];
			mod.CopyInstantiatedReferenceTo(starmod);

			starmod.Brush.FirstModelSurface = bm.FirstFace;
			starmod.Brush.NumModelSurfaces = bm.NumFaces;
			starmod.Brush.FirstNode = (ushort)bm.HeadNode;
			if (starmod.Brush.FirstNode >= WorldBrushData.NumNodes)
				Sys.Error($"Inline model {i} has bad firstnode");

			starmod.Maxs = bm.Maxs;
			starmod.Mins = bm.Mins;

			starmod.Radius = bm.Radius;
			if (i == 0)
				starmod.CopyInstantiatedReferenceTo(mod);
			else {
				starmod.StrName = $"*{i}";
				starmod.FileNameHandle = g_pFileSystem.FindOrAddFileName(starmod.StrName);
			}
		}
	}

	static float RadiusFromBounds(in Vector3 mins, in Vector3 maxs) {
		Vector3 corner = default;
		for (int i = 0; i < 3; i++)
			corner[i] = Math.Max(MathF.Abs(mins[i]), MathF.Abs(maxs[i]));

		return MathLib.VectorLength(corner);
	}


	private void Mod_LoadSubmodels(List<BSPMModel> inSubmodelList) {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Models);
		BSPDModel[] inModels = lh.LoadLumpData<BSPDModel>();

		int count = inModels.Length;
		BSPMModel[] outModels = new BSPMModel[count];


		inSubmodelList.EnsureCount(count);
		lh.GetMap().NumSubModels = count;

		Span<BSPMModel> submodelList = inSubmodelList.AsSpan();

		for (int i = 0; i < count; i++) {
			ref BSPDModel dm = ref inModels[i];
			for (int j = 0; j < 3; j++) {
				submodelList[i].Mins[j] = dm.Mins[j] - 1;
				submodelList[i].Maxs[j] = dm.Maxs[j] + 1;
				submodelList[i].Origin[j] = dm.Origin[j];
			}
			submodelList[i].Radius = RadiusFromBounds(submodelList[i].Mins, submodelList[i].Maxs);
			submodelList[i].HeadNode = dm.HeadNode;
			submodelList[i].FirstFace = dm.FirstFace;
			submodelList[i].NumFaces = dm.NumFaces;
		}
	}

	private void Map_LoadLighting(MapLoadHelper lh) {
		if (lh.LumpSize == 0) {
			lh.GetMap().LightData = null;
			return;
		}

		Assert((lh.LumpSize % (sizeof(byte) * 4)) == 0);
		Assert(lh.LumpVersion != 0);

		lh.GetMap().LightData = lh.LoadLumpData<ColorRGBExp32>();
	}

	private void Mod_LoadPrimitives() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Primitives);
		BSPDPrimitive[] inPrims = lh.LoadLumpData<BSPDPrimitive>();
		BSPMPrimitive[] outPrims = new BSPMPrimitive[inPrims.Length];

		lh.GetMap().Primitives = outPrims;
		for (int i = 0; i < outPrims.Length; i++) {
			ref BSPDPrimitive inPrim = ref inPrims[i];
			ref BSPMPrimitive outPrim = ref outPrims[i];
			outPrim.FirstIndex = inPrim.FirstIndex;
			outPrim.FirstVert = inPrim.FirstVert;
			outPrim.IndexCount = inPrim.IndexCount;
			outPrim.Type = (BSPPrimType)inPrim.Type;
			outPrim.VertCount = inPrim.VertCount;
		}
	}
	private void Mod_LoadPrimVerts() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.PrimVerts);
		BSPDPrimVert[] inPrims = lh.LoadLumpData<BSPDPrimVert>();
		BSPMPrimVert[] outPrims = new BSPMPrimVert[inPrims.Length];

		lh.GetMap().PrimVerts = outPrims;
		for (int i = 0; i < outPrims.Length; i++) {
			ref BSPDPrimVert inPrim = ref inPrims[i];
			ref BSPMPrimVert outPrim = ref outPrims[i];
			outPrim.Position = inPrim.Position;
		}
	}
	private void Mod_LoadPrimIndices() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.PrimIndices);
		lh.GetMap().PrimIndices = lh.LoadLumpData<ushort>();
	}
	public static void Mod_LeafAmbientColorAtPos(Span<Vector3> outColors, in Vector3 pos, int leafIndex) {
		for (int i = 0; i < 6; i++)
			outColors[i].Init();

		ref MLeafAmbientIndex ambient = ref host_state.WorldBrush!.LeafAmbient![leafIndex];
		if (ambient.AmbientSampleCount == 0 && ambient.FirstAmbientSample != 0) {
			leafIndex = ambient.FirstAmbientSample;
			ambient = ref host_state.WorldBrush!.LeafAmbient![leafIndex];
		}
		int count = ambient.AmbientSampleCount;
		if (count > 0) {
			int start = host_state.WorldBrush!.LeafAmbient![leafIndex].FirstAmbientSample;
			MLeafAmbientLighting[] samples = host_state.WorldBrush!.AmbientSamples!;
			BSPMLeaf leaf = host_state.WorldBrush!.Leafs![leafIndex];
			float totalFactor = 0;
			for (int i = 0; i < count; i++) {
				ref MLeafAmbientLighting sample = ref samples[start + i];
				Vector3 samplePos = leaf.Center - leaf.HalfDiagonal;
				samplePos.X += sample.X * leaf.HalfDiagonal.X * (2.0f / 255.0f);
				samplePos.Y += sample.Y * leaf.HalfDiagonal.Y * (2.0f / 255.0f);
				samplePos.Z += sample.Z * leaf.HalfDiagonal.Z * (2.0f / 255.0f);

				float dist = (samplePos - pos).LengthSqr();
				float factor = 1.0f / (dist + 1.0f);
				totalFactor += factor;
				for (int j = 0; j < 6; j++) {
					MathLib.ColorRGBExp32ToVector(sample.Cube.Color[j], out Vector3 v);
					outColors[j] += v * factor;
				}
			}
			for (int i = 0; i < 6; i++)
				outColors[i] *= (1.0f / totalFactor);
		}
	}

	public static ref BSPDFace FaceHandleFromIndex(int surfaceIndex, WorldBrushData data) => ref data.Faces![surfaceIndex];
	public static ref BSPMSurface2 SurfaceHandleFromIndex(int surfaceIndex, WorldBrushData? data = null) => ref (data ?? host_state.WorldBrush)!.Surfaces2![surfaceIndex];
	public static ref CollisionPlane MSurf_Plane(ref BSPMSurface2 surfID) => ref surfID.Plane.GetReference();
	public static int MSurf_Index(ref BSPMSurface2 surfID, WorldBrushData? data = null) => (int)surfID.SurfNum;
	public static ref int MSurf_FirstVertIndex(ref BSPMSurface2 surfID) => ref surfID.FirstVertIndex;
	public static ref uint MSurf_FirstVertNormal(ref BSPMSurface2 surfID, WorldBrushData? data = null) {
		data ??= host_state.WorldBrush;
		int surfaceIndex = MSurf_Index(ref surfID, data);
		return ref data!.SurfaceNormals![surfaceIndex].FirstVertNormal;
	}
	public static Span<short> MSurf_LightmapMins(ref BSPMSurface2 surfID, WorldBrushData? data = null) {
		data ??= host_state.WorldBrush;
		int surfaceIndex = MSurf_Index(ref surfID, data);
		return data!.SurfaceLighting![surfaceIndex].LightmapMins;
	}
	public static Span<short> MSurf_LightmapExtents(ref BSPMSurface2 surfID, WorldBrushData? data = null) {
		data ??= host_state.WorldBrush;
		int surfaceIndex = MSurf_Index(ref surfID, data);
		return data!.SurfaceLighting![surfaceIndex].LightmapExtents;
	}
	public static Span<int> MSurf_TextureMins(ref BSPMSurface2 surfID, WorldBrushData? data = null) {
		data ??= host_state.WorldBrush;
		int surfaceIndex = MSurf_Index(ref surfID, data);
		return data!.Surfaces1![surfaceIndex].TextureMins;
	}
	public static Span<short> MSurf_TextureExtents(ref BSPMSurface2 surfID, WorldBrushData? data = null) {
		data ??= host_state.WorldBrush;
		int surfaceIndex = MSurf_Index(ref surfID, data);
		return data!.Surfaces1![surfaceIndex].TextureExtents;
	}
	// FIXME some (all?) of these should be taking handle
	public static ref SurfDraw MSurf_Flags(ref BSPMSurface2 surfID) => ref surfID.Flags;
	public static ref int MSurf_VisFrame(ref BSPMSurface2 surfID) => ref surfID.VisFrame;
	public static bool SurfaceHasDispInfo(ref BSPMSurface2 surfID) => (MSurf_Flags(ref surfID) & SurfDraw.HasDisp) != 0;
	public static ref ushort MSurf_VertBufferIndex(ref BSPMSurface2 surfID) => ref surfID.VertBufferIndex;
	public static ref ShadowDecalHandle_t MSurf_ShadowDecals(ref BSPMSurface2 surfID) => ref surfID.ShadowDecals;
	public const WorldDecalHandle_t WORLD_DECAL_HANDLE_INVALID = 0xFFFF;
	public static ref WorldDecalHandle_t MSurf_Decals(ref BSPMSurface2 surfID) => ref surfID.Decals;
	public static bool SurfaceHasDecals(ref BSPMSurface2 surfID) => MSurf_Decals(ref surfID) != WORLD_DECAL_HANDLE_INVALID;
	public static ref OverlayFragmentHandle_t MSurf_OverlayFragmentList(ref BSPMSurface2 surfID) => ref surfID.FirstOverlayFragment;
	public static ushort MSurf_NumPrims(ref BSPMSurface2 surfID, WorldBrushData data) {
		if (SurfaceHasDispInfo(ref surfID) || !SurfaceHasPrims(ref surfID))
			return 0;

		int surfaceIndex = MSurf_Index(ref surfID, data);
		return data.Surfaces1![surfaceIndex].NumPrims;
	}
	public static ref short MSurf_MaterialSortID(ref BSPMSurface2 surfID) => ref surfID.MaterialSortID;
	public static bool SurfaceHasPrims(ref BSPMSurface2 surfID) => (MSurf_Flags(ref surfID) & SurfDraw.HasPrims) != 0;
	public static int MSurf_SortGroup(ref BSPMSurface2 surfID) => (int)(surfID.Flags & SurfDraw.SortGroupMask) >> (int)SurfDraw.SortGroupShift;
	public static void MSurf_SetSortGroup(ref BSPMSurface2 surfID, int sortGroup) => surfID.Flags |= (SurfDraw)((sortGroup << (int)SurfDraw.SortGroupShift) & (int)SurfDraw.SortGroupMask);
	public static ref ModelTexInfo MSurf_TexInfo(ref BSPMSurface2 surfID, WorldBrushData? data = null) => ref (data ?? host_state.WorldBrush)!.TexInfo![surfID.TexInfo];
	public static Span<short> MSurf_OffsetIntoLightmapPage(ref BSPMSurface2 surfID, WorldBrushData? data = null) {
		data ??= host_state.WorldBrush;
		return data!.SurfaceLighting![MSurf_Index(ref surfID, data)].OffsetIntoLightmapPage;
	}
	public static int MSurf_VertCount(ref BSPMSurface2 surfID) => (int)(((uint)surfID.Flags >> (int)SurfDraw.VertCountShift) & 0xFF);
	public static void MSurf_SetVertCount(ref BSPMSurface2 surfID, uint vertCount) {
		uint flags = (vertCount << (int)SurfDraw.VertCountShift) & (uint)SurfDraw.VertCountMask;
		surfID.Flags |= (SurfDraw)flags;
	}

	private static void LinearToGamma(Span<byte> dstRGB, in Vector3 srcRGB) {
		dstRGB[0] = (byte)MathLib.LinearToScreenGamma(srcRGB.X);
		dstRGB[1] = (byte)MathLib.LinearToScreenGamma(srcRGB.Y);
		dstRGB[2] = (byte)MathLib.LinearToScreenGamma(srcRGB.Z);
	}

	private static void CheckSurfaceLighting(ref BSPMSurface2 surfID, WorldBrushData brushData) {
#if !SWDS
		host_state.WorldBrush = brushData;
		ref BSPMSurfaceLighting lighting = ref SurfaceLighting(ref surfID, brushData);

		if (lighting.Samples.IsEmpty)
			return;

		int smax = lighting.LightmapExtents[0] + 1;
		int tmax = lighting.LightmapExtents[1] + 1;
		int offset = smax * tmax;
		if (SurfHasBumpedLightmaps(ref surfID))
			offset *= Constants.NUM_BUMP_VECTS + 1;

		int maxLightmapIndex = 0;
		for (int maps = 1; maps < BSPFileCommon.MAXLIGHTMAPS && lighting.Styles[maps] != 255; ++maps)
			maxLightmapIndex = maps;

		if (maxLightmapIndex < 1)
			return;

		Span<byte> color = stackalloc byte[4];

		for (int maps = maxLightmapIndex; maps != 0; maps--) {
			Span<ColorRGBExp32> lightmap = lighting.Samples.Span[(maps * offset)..];
			float maxLen = -1;
			Vector3 maxLight = default;
			for (int i = 0; i < offset; i++) {
				MathLib.ColorRGBExp32ToVector(in lightmap[i], out Vector3 c);
				if (c.Length() > maxLen) {
					maxLight = c;
					maxLen = c.Length();
				}
			}

			LinearToGamma(color, in maxLight);
			const int minLightVal = 1;
			if (color[0] <= minLightVal && color[1] <= minLightVal && color[2] <= minLightVal) {
				for (int i = maps; i < maxLightmapIndex; i++) {
					Span<ColorRGBExp32> lightmapOverwrite = lighting.Samples.Span[(i * offset)..];
					lightmapOverwrite[offset..(offset + offset)].CopyTo(lightmapOverwrite);
					lighting.Styles[i] = lighting.Styles[i + 1];
				}

				lighting.Styles[maxLightmapIndex] = 255;
				maxLightmapIndex--;
			}
		}

		if (maxLightmapIndex == 0)
			MSurf_Flags(ref surfID) &= ~SurfDraw.HasLightStyles;
#endif
	}

	private static void CalcSurfaceExtents(in MapLoadHelper lh, ref BSPMSurface2 surfID) {
		Span<float> textureMins = stackalloc float[2];
		Span<float> textureMaxs = stackalloc float[2];
		float val;
		int i, j, e;
		Span<int> bmins = stackalloc int[2];
		Span<int> bmaxs = stackalloc int[2];

		textureMins[0] = textureMins[1] = float.MaxValue;
		textureMaxs[0] = textureMaxs[1] = float.MinValue;

		WorldBrushData brushData = lh.GetMap();
		ref ModelTexInfo tex = ref MSurf_TexInfo(ref surfID, brushData);

		for (i = 0; i < MSurf_VertCount(ref surfID); i++) {
			e = brushData.VertIndices![MSurf_FirstVertIndex(ref surfID) + i];
			ref BSPDertex v = ref brushData.Vertexes![e];

			for (j = 0; j < 2; j++) {
				val = v.Position.X * tex.TextureVecsTexelsPerWorldUnits[j].X + v.Position.Y * tex.TextureVecsTexelsPerWorldUnits[j].Y + v.Position.Z * tex.TextureVecsTexelsPerWorldUnits[j].Z + tex.TextureVecsTexelsPerWorldUnits[j].W;
				if (val < textureMins[j])
					textureMins[j] = val;
				if (val > textureMaxs[j])
					textureMaxs[j] = val;
			}
		}

		for (i = 0; i < 2; i++) {
			if (MSurf_LightmapExtents(ref surfID, brushData)[i] == 0 && brushData.SurfaceLighting![MSurf_Index(ref surfID, brushData)].Samples.IsEmpty)
				MSurf_Flags(ref surfID) |= SurfDraw.NoLight;

			bmins[i] = (int)textureMins[i];
			bmaxs[i] = (int)MathF.Ceiling(textureMaxs[i]);
			MSurf_TextureMins(ref surfID, brushData)[i] = bmins[i];
			MSurf_TextureExtents(ref surfID, brushData)[i] = (short)(bmaxs[i] - bmins[i]);

			if ((tex.Flags & Surf.NoLight) == 0 && MSurf_LightmapExtents(ref surfID, brushData)[i] > MSurf_MaxLightmapSizeWithBorder(ref surfID))
				Sys.Error($"Bad surface extents on texture {tex.Material!.GetName()}");
		}

		CheckSurfaceLighting(ref surfID, brushData);
	}

	private void Mod_LoadFaces() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Faces);
		BSPDFace[] inFaces = lh.LoadLumpData<BSPDFace>();

		int count = inFaces.Length;
		BSPMSurface1[] out1 = new BSPMSurface1[count];
		BSPMSurface2[] out2 = new BSPMSurface2[count];
		BSPMSurfaceLighting[] lighting = new BSPMSurfaceLighting[count];

		WorldBrushData brushData = lh.GetMap();
		brushData.Faces = inFaces;
		brushData.Surfaces1 = out1;
		brushData.Surfaces2 = out2;
		brushData.SurfaceLighting = lighting;
		brushData.SurfaceNormals = new BSPMSurfaceNormal[count];
		brushData.NumSurfaces = count;

		int ti, di;

		for (int surfnum = 0; surfnum < count; surfnum++) {
			ref readonly BSPDFace _in = ref inFaces[surfnum];
			ref BSPMSurface1 _out1 = ref out1[surfnum];
			ref BSPMSurface2 _out2 = ref out2[surfnum];
			ref BSPMSurfaceLighting _light = ref lighting[surfnum];
			_out1.SurfNum = surfnum;
			_out2.SurfNum = surfnum;
			_light.SurfNum = surfnum;

			ref BSPMSurface2 surfID = ref SurfaceHandleFromIndex(surfnum, brushData);

			MSurf_FirstVertIndex(ref surfID) = _in.FirstEdge;

			int vertCount = _in.NumEdges;
			MSurf_Flags(ref surfID) = 0;
			Assert(vertCount <= 255);
			MSurf_SetVertCount(ref surfID, (uint)vertCount);

			int planenum = _in.PlaneNum;
			if (_in.OnNode != 0)
				MSurf_Flags(ref surfID) |= SurfDraw.Node;

			if (_in.Side != 0)
				MSurf_Flags(ref surfID) |= SurfDraw.PlaneBack;

			_out2.Plane = lh.GetMap().Planes![planenum];

			ti = _in.TexInfo;
			if (ti < 0 || ti >= lh.GetMap().NumTexInfo)
				Host.Error("Mod_LoadFaces: bad texinfo number");

			surfID.TexInfo = (ushort)ti;
			surfID.DynamicShadowsEnabled = _in.AreDynamicShadowsEnabled();
			ref ModelTexInfo tex = ref lh.GetMap().TexInfo![ti];

			if (tex.Material == null)
				tex.Material = materials.MaterialEmpty;

			if (Mod_LoadSurfaceLightingV1(ref _light, in _in, lh.GetMap().LightData))
				MSurf_Flags(ref surfID) |= SurfDraw.HasLightStyles;

			// set the drawing flags flag
			if ((tex.Flags & Surf.NoLight) != 0)
				MSurf_Flags(ref surfID) |= SurfDraw.NoLight;

			if ((tex.Flags & Surf.NoShadows) != 0)
				MSurf_Flags(ref surfID) |= SurfDraw.NoShadows;

			if ((tex.Flags & Surf.Warp) != 0)
				MSurf_Flags(ref surfID) |= SurfDraw.WaterSurface;

			if ((tex.Flags & Surf.Sky) != 0)
				MSurf_Flags(ref surfID) |= SurfDraw.Sky;

			di = _in.DispInfo;
			_out2.DispInfo = null;
			if (di != -1) {
				MSurf_Flags(ref surfID) |= SurfDraw.HasDisp;
			}
			else {
				Assert((tex.Flags & Surf.NoDraw) == 0);

				_out1.NumPrims = _in.GetNumPrims();
				_out1.FirstPrimID = _in.FirstPrimID;
				if (_in.GetNumPrims() != 0) {
					MSurf_Flags(ref surfID) |= SurfDraw.HasPrims;
					ref BSPMPrimitive prim = ref brushData.Primitives![_in.FirstPrimID];
					if (prim.VertCount > 0) {
						MSurf_Flags(ref surfID) |= SurfDraw.Dynamic;
					}
				}
			}

			surfID.ShadowDecals = SHADOW_DECAL_HANDLE_INVALID;
			// _out2.Decals = WORLD_DECAL_HANDLE_INVALID;

			// out2.FirstOverlayFragment = OVERLAY_FRAGMENT_INVALID;

			CalcSurfaceExtents(in lh, ref surfID);
		}
	}

	private bool Mod_LoadSurfaceLightingV1(ref BSPMSurfaceLighting light, in BSPDFace _in, ColorRGBExp32[]? lightData) {
		light.LightmapExtents[0] = (short)_in.LightmapTextureSizeInLuxels[0];
		light.LightmapExtents[1] = (short)_in.LightmapTextureSizeInLuxels[1];
		light.LightmapMins[0] = (short)_in.LightmapTextureMinsInLuxels[0];
		light.LightmapMins[1] = (short)_in.LightmapTextureMinsInLuxels[1];

		int i = _in.LightOffset;

		if (i == -1 || lightData == null) {
			light.Samples = null;

			for (i = 0; i < BSPFileCommon.MAXLIGHTMAPS; ++i)
				light.Styles[i] = 255;
		}
		else {
			light.Samples = lightData.AsMemory()[(i / 4)..];

			for (i = 0; i < BSPFileCommon.MAXLIGHTMAPS; ++i)
				light.Styles[i] = _in.Styles[i];
		}

		return ((light.Styles[0] != 0) && (light.Styles[0] != 255)) || (light.Styles[1] != 255);
	}

	private void Mod_LoadVertNormals() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.VertNormals);
		Vector3[] inVertNormals = lh.LoadLumpData<Vector3>();

		lh.GetMap().VertNormals = inVertNormals;
	}
	private void Mod_LoadVertNormalIndices() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.VertNormalIndices);
		ushort[] inVertNormalIndices = lh.LoadLumpData<ushort>();
		lh.GetMap().VertNormalIndices = inVertNormalIndices;
		int normalIndex = 0;
		for (int i = 0; i < lh.GetMap().NumSurfaces; i++) {
			ref BSPMSurface2 surfID = ref SurfaceHandleFromIndex(i, lh.GetMap());
			MSurf_FirstVertNormal(ref surfID, lh.GetMap()) = (uint)normalIndex;
			normalIndex += MSurf_VertCount(ref surfID);
		}
	}
	private void Mod_LoadTexdata() {
		MapLoadHelper.Map!.NumTexData = GetCollisionBSPData().NumSurfaces;
		MapLoadHelper.Map!.TexData = GetCollisionBSPData().MapSurfaces.Base();
	}

	private void Mod_LoadTexinfo() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.TexInfo);
		BSPTexInfo[] inTexInfo = lh.LoadLumpData<BSPTexInfo>();
		ModelTexInfo[] outTexInfo = new ModelTexInfo[inTexInfo.Length];

		lh.GetMap().TexInfo = outTexInfo;
		lh.GetMap().NumTexInfo = inTexInfo.Length;

		bool loadtextures = true; // << todo: convar
		for (int i = 0; i < outTexInfo.Length; ++i) {
			ref BSPTexInfo _in = ref inTexInfo[i];
			ref ModelTexInfo _out = ref outTexInfo[i];
			for (int j = 0; j < 2; ++j) {
				for (int k = 0; k < 4; ++k) {
					_out.TextureVecsTexelsPerWorldUnits[j][k] = _in.TextureVecsTexelsPerWorldUnits[j][k];
					_out.LightmapVecsLuxelsPerWorldUnits[j][k] = _in.LightmapVecsLuxelsPerWorldUnits[j][k];
				}
			}

			_out.LuxelsPerWorldUnit = MathLib.VectorLength(_out.LightmapVecsLuxelsPerWorldUnits[0].AsVector3());
			_out.WorldUnitsPerLuxel = 1.0f / _out.LuxelsPerWorldUnit;
			_out.TexData = _in.TexData;
			_out.Flags = (Surf)_in.Flags;
			_out.TexInfoFlags = 0;

			if (loadtextures) {
				if (_in.TexData >= 0)
					_out.Material = materials.GL_LoadMaterial(lh.GetMap().TexData![_in.TexData].Name, MaterialDefines.TEXTURE_GROUP_WORLD);
				else {
					DevMsg($"Mod_LoadTexinfo: texdata < 0 (index=={i}/{outTexInfo.Length})\n");
					_out.Material = null;
				}
				if (_out.Material == null) {
					DevWarning($"Mod_LoadTexInfo: cannot find material named {lh.GetMap().TexData![_in.TexData].Name}\n");
					_out.Material = materials.MaterialEmpty;
				}
			}
			else
				_out.Material = materials.MaterialEmpty;
		}
	}

	private void Mod_LoadPlanes() {
		MapLoadHelper.Map!.Planes = GetCollisionBSPData().MapPlanes.Base();
		MapLoadHelper.Map!.NumPlanes = GetCollisionBSPData().NumPlanes;
	}

	private void Mod_LoadVertices() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Vertexes);
		lh.GetMap().Vertexes = lh.LoadLumpData<BSPDertex>();
	}

	private BSPDEdge[] Mod_LoadEdges() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.Edges);
		BSPDEdge[] outData = lh.LoadLumpData<BSPDEdge>();
		return outData;
	}
	private void Mod_LoadSurfedges(BSPDEdge[] edges) {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.SurfEdges);
		int[] inData = lh.LoadLumpData<int>(throwIfNoElements: true, maxElements: BSPFileCommon.MAX_MAP_SURFEDGES);
		ushort[] outData = new ushort[inData.Length]; ;
		lh.GetMap().VertIndices = outData;

		for (int i = 0; i < outData.Length; i++) {
			int edge = inData[i];
			int index = 0;
			if (edge < 0) {
				edge = -edge;
				index = 1;
			}
			outData[i] = edges[edge].V[index];
		}
	}
	public void SetWorldModel(Model mod) {
		WorldModel = mod;
	}

	public void ClearWorldModel() {
		WorldModel = null;
	}



	private ModelType GetTypeFromName(ReadOnlySpan<char> modelName) => modelName.GetFileExtension() switch {
		"spr" or "vmt" => ModelType.Sprite,
		"bsp" => ModelType.Brush,
		"mdl" => ModelType.Studio,
		_ => ModelType.Invalid
	};

	readonly List<Model> InlineModels = [];
	readonly Dictionary<FileNameHandle_t, Model> Models = [];

	private Model? FindModel(ReadOnlySpan<char> name) {
		if (name.IsEmpty)
			Sys.Error("ModelLoader.FindModel: NULL name");


		if (name[0] == '*') {
			int.TryParse(name[1..], out int modelNum);
			if (!IsWorldModelSet())
				Sys.Error($"bad inline model number {modelNum}, worldmodel not yet setup");

			if (modelNum < 1 || modelNum >= GetNumWorldSubmodels())
				Sys.Error($"bad inline model number {modelNum}");

			return InlineModels[modelNum];
		}

		Model? model = null;

		FileNameHandle_t fnHandle = fileSystem.FindOrAddFileName(name);

		if (!Models.TryGetValue(fnHandle, out model)) {
			model = new() {
				FileNameHandle = fnHandle,
				LoadFlags = ModelLoaderFlags.NotLoadedOrReferenced,
				StrName = name
			};

			Models[fnHandle] = model;
		}

		Assert(model);

		return model;
	}

	private bool IsWorldModelSet() {
		return WorldModel != null;
	}

	private int GetNumWorldSubmodels() {
		if (!IsWorldModelSet())
			return 0;

		return WorldBrushData.NumSubModels;
	}

	public ReadOnlySpan<char> GetName(Model model) {
		return model == null ? null : model.StrName;
	}

	public void Init() {
		MDLCache.SetCacheNotify(MDLCacheNotify.s);
	}

	public void PurgeUnusedModels() {

	}

	public void UnloadUnreferencedModels() {
		// unload all unreferenced models
		UnloadAllModels(true);
	}

	void UnloadAllModels(bool checkReference) {
		foreach (Model model in Models.Values) {
			if (checkReference) {
				if ((model.LoadFlags & ModelLoaderFlags.ReferenceMask) != 0)
					continue;
			}
			else
				model.LoadFlags &= ~ModelLoaderFlags.ReferenceMask;

			if ((model.LoadFlags & (ModelLoaderFlags.Loaded | ModelLoaderFlags.LoadedByPreload)) != 0)
				UnloadModel(model);
		}
	}

	void UnloadModel(Model model) {
		switch (model.Type) {
			case ModelType.Brush:
				// Let it free data or call destructors..
				Map_UnloadModel(model);

				// Remove from file system
				fileSystem.RemoveSearchPath(model.StrName, "GAME");

				ActiveMapName[0] = '\0';
				break;
			case ModelType.Studio:
				// todo
				break;

			case ModelType.Sprite:
				// todo
				break;
		}
	}

	void Map_UnloadModel(Model mod) {
		Assert((mod.LoadFlags & ModelLoaderFlags.ReferenceMask) == 0);
		mod.LoadFlags &= ~ModelLoaderFlags.Loaded;
	}

	public Model? ReferenceModel(ReadOnlySpan<char> name, ModelLoaderFlags referenceType) {
		AssertMsg(0 == (referenceType & ModelLoaderFlags.Dynamic), "ReferenceModel: do not use for dynamic models");

		Model model = FindModel(name)!;
		model.LoadFlags |= referenceType;
		return model;
	}

	public void ResetModelServerCounts() {
		foreach (Model model in Models.Values)
			model.ServerCount = -1;
	}

	public void UnreferenceAllModels(ModelLoaderFlags referenceType) {
		throw new NotImplementedException();
	}

	public void UnreferenceModel(Model model, ModelLoaderFlags referenceType) {
		AssertMsg((referenceType & ModelLoaderFlags.Dynamic) == 0, "UnreferenceModel: do not use for dynamic models");
		model.LoadFlags &= ~referenceType;
	}

	internal static int MSurf_FirstPrimID(ref BSPMSurface2 surfID, WorldBrushData bsp) {
		if (SurfaceHasDispInfo(ref surfID))
			return 0;
		int surfaceIndex = MSurf_Index(ref surfID, bsp);
		return bsp.Surfaces1![surfaceIndex].FirstPrimID;
	}

	public void Map_LoadDisplacements(MaterialSystem_SortInfo[] materialSortInfoArray, Model model) {
		model.StrName.String()!.FileBase(LoadName);
		if (!MapLoadHelper.Init(model, LoadNameSliced()))
			return;

		DispInfo_LoadDisplacements(model, materialSortInfoArray);
		MapLoadHelper.Shutdown();
	}

	public void Shutdown() {

	}

	internal static ref BSPMSurfaceLighting SurfaceLighting(ref BSPMSurface2 surfID, WorldBrushData data) {
		int surfaceindex = MSurf_Index(ref surfID, data);
		return ref data.SurfaceLighting![surfaceindex];
	}

	internal static int MSurf_MaxLightmapSizeWithBorder(ref BSPMSurface2 surfID) {
		return SurfaceHasDispInfo(ref surfID) ? BSPFileCommon.MAX_DISP_LIGHTMAP_DIM_INCLUDING_BORDER : BSPFileCommon.MAX_BRUSH_LIGHTMAP_DIM_INCLUDING_BORDER;
	}

	internal static bool SurfHasBumpedLightmaps(ref BSPMSurface2 surfID) {
		bool hasBumpmap = false;
		if ((MSurf_TexInfo(ref surfID).Flags & Surf.BumpLight) != 0 &&
			((MSurf_TexInfo(ref surfID).Flags & Surf.Light) == 0) &&
			(host_state.WorldBrush!.LightData != null) &&
			(!MSurf_Samples(ref surfID).IsEmpty)) {
			hasBumpmap = true;
		}
		return hasBumpmap;
	}

	private static Memory<ColorRGBExp32> MSurf_Samples(ref BSPMSurface2 surfID) {
		return host_state.WorldBrush!.SurfaceLighting![MSurf_Index(ref surfID)].Samples;
	}

	internal static bool SurfHasLightmap(ref BSPMSurface2 surfID) {
		bool hasLightmap = false;
		if (((MSurf_TexInfo(ref surfID).Flags & Surf.NoLight) == 0) &&
			(host_state.WorldBrush!.LightData != null) &&
			(!MSurf_Samples(ref surfID).IsEmpty)) {
			hasLightmap = true;
		}
		return hasLightmap;
	}

	struct dgamelump_internal
	{
		public GameLumpId_t ID;
		public ushort Flags;
		public ushort Version;
		public uint Offset;
		public uint UncompressedSize;
		public uint CompressedSize;

		public dgamelump_internal(in BSPDGameLump other, uint compressedSize) {
			ID = other.id;
			Flags = other.Flags;
			Version = other.Version;
			Offset = (uint)Math.Max(other.fileofs, 0);
			UncompressedSize = (uint)Math.Max(other.filelen, 0);
			CompressedSize = compressedSize;
		}
	}
	static readonly List<dgamelump_internal> g_GameLumpDict = [];

	internal static int GameLumpVersion(GameLumpId_t lumpId) {
		Span<dgamelump_internal> gameLumpDict = g_GameLumpDict.AsSpan();
		for (int i = gameLumpDict.Length; --i >= 0;)
			if (gameLumpDict[i].ID == lumpId)
				return gameLumpDict[i].Version;
		return 0;
	}
	internal static int GameLumpSize(GameLumpId_t lumpId) {
		Span<dgamelump_internal> gameLumpDict = g_GameLumpDict.AsSpan();
		for (int i = gameLumpDict.Length; --i >= 0;)
			if (gameLumpDict[i].ID == lumpId)
				return (int)gameLumpDict[i].UncompressedSize;
		return 0;
	}
	static string? g_GameLumpFilename;
	internal static bool LoadGameLump(GameLumpId_t lumpId, Span<byte> outBuffer) {
		Span<dgamelump_internal> gameLumpDict = g_GameLumpDict.AsSpan();
		int i;
		for (i = gameLumpDict.Length; --i >= 0;)
			if (gameLumpDict[i].ID == lumpId)
				break;

		if (i < 0)
			return false;

		bool isCompressed = (gameLumpDict[i].Flags & BSPFileCommon.GAMELUMPFLAG_COMPRESSED) != 0;
		int outSize = (int)gameLumpDict[i].UncompressedSize;

		if (outBuffer.Length < outSize)
			return false;

		Stream? file = g_pFileSystem.Open(g_GameLumpFilename, FileOpenOptions.Read | FileOpenOptions.Binary)?.Stream;
		if (file == null)
			return false;

		using (file) {
			file.Seek(gameLumpDict[i].Offset, SeekOrigin.Begin);

			if (!isCompressed)
				return file.Read(outBuffer[..outSize]) > 0;

			Span<byte> headerBytes = stackalloc byte[Unsafe.SizeOf<LZMA.Header>()];
			file.ReadExactly(headerBytes);

			if (!LZMA.IsCompressed(headerBytes) || LZMA.GetActualSize(headerBytes) != gameLumpDict[i].UncompressedSize) {
				Warning($"Failed loading game lump {lumpId}: lump claims to be compressed but metadata does not match\n");
				return false;
			}

			LZMA.Header header = MemoryMarshal.Read<LZMA.Header>(headerBytes);
			byte[] compressed = new byte[headerBytes.Length + header.LZMASize];
			headerBytes.CopyTo(compressed);
			file.ReadExactly(compressed.AsSpan(headerBytes.Length));

			return LZMA.Uncompress(compressed, outBuffer[..outSize]) == outSize;
		}
	}
	internal static void LoadGameLumpDict() {
		MapLoadHelper lh = new MapLoadHelper(LumpIndex.GameLump);
		g_GameLumpDict.Clear();
		g_GameLumpFilename = new(lh.GetMapName());
		uint lhSize = (uint)Math.Max(lh.LumpSize, 0);
		if (lhSize >= Unsafe.SizeOf<BSPDGameLumpHeader>()) {
			ref readonly BSPDGameLumpHeader gameLumpHeader = ref (lh.LoadLumpBaseRaw().AsSpan()[..Unsafe.SizeOf<BSPDGameLumpHeader>()].Cast<byte, BSPDGameLumpHeader>())[0];

			// Ensure (lumpsize * numlumps + headersize) doesn't overflow
			int nMaxGameLumps = (int.MaxValue - Unsafe.SizeOf<BSPDGameLumpHeader>()) / Unsafe.SizeOf<BSPDGameLump>();
			if (gameLumpHeader.LumpCount < 0 || gameLumpHeader.LumpCount > nMaxGameLumps || Unsafe.SizeOf<BSPDGameLumpHeader>() + Unsafe.SizeOf<BSPDGameLump>() * gameLumpHeader.LumpCount > lhSize) {
				Warning("Bogus gamelump header in map, rejecting\n");
			}
			else {
				// Load in lumps
				ReadOnlySpan<BSPDGameLump> gameLump = lh.LoadLumpBaseRaw().AsSpan()[Unsafe.SizeOf<BSPDGameLumpHeader>()..].Cast<byte, BSPDGameLump>();
				for (int i = 0; i < gameLumpHeader.LumpCount; ++i) {
					if (gameLump[i].fileofs >= 0 && (uint)gameLump[i].fileofs >= (uint)lh.LumpOffset && (uint)gameLump[i].fileofs < (uint)lh.LumpOffset + lhSize && gameLump[i].filelen > 0) {
						uint compressedSize;
						if (gameLump[i].fileofs >= 0 && (uint)gameLump[i].fileofs >= (uint)lh.LumpOffset && (uint)gameLump[i].fileofs < (uint)lh.LumpOffset + lhSize && gameLump[i].filelen > 0)
							compressedSize = (uint)gameLump[i + 1].fileofs - (uint)gameLump[i].fileofs;
						else
							compressedSize = (uint)lh.LumpOffset + lhSize - (uint)gameLump[i].fileofs;
						g_GameLumpDict.Add(new(in gameLump[i], compressedSize));
					}
				}
			}
		}
	}
}
