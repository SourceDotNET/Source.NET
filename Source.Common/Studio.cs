using CommunityToolkit.HighPerformance;

using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.Utilities;

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Xml.Linq;
namespace Source.Common;

using static Source.Common.Networking.SVC_ClassInfo;
using static StudioDeps;

[EngineComponent]
public static class StudioDeps
{
	[Dependency] public static IMDLCache MDLCache { get; private set; } = null!;
	[Dependency] public static IVModelInfo modelinfo { get; private set; } = null!;
}


public enum StudioBoneFlags
{
	CalculateMask = 0x1F,
	PhysicallySimulated = 0x01,
	PhysicsProcedural = 0x02,
	AlwaysProcedural = 0x04,
	ScreenAlignSphere = 0x08,
	ScreenAlignCylinder = 0x10
}

public delegate T FactoryFn<T>(object caller, Memory<byte> data);
public static class Studio
{
	public static T UnmanagedFactoryFn<T>(object caller, Memory<byte> data) where T : unmanaged => data.Span.Cast<byte, T>()[0];

	// Some helper functions so I didn't have to write the same boilerplate over and over again for all of the class based views.
	// The way most of this file works differs from Source in that it instantiates class instances from binary data. That decision
	// was made because Source does most of this with pointers - and quite often stores those pointers, which we cant do with
	// reinterpreted struct references in any C#-safe way...
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref T ProduceArrayIdx<T>(
		object caller,            // the caller (some factories need this)
		[NotNull] ref T[]? array, // the array to initialize if null
		int elements,             // how many elements in the array, NumLocalSeq for example
		int dataOffset,           // the offset into data, LocalSeqIndex for example
		int index,                // the array index
		int sizeOfOne,            // size of one component
		Memory<byte> data,        // the raw binary
		FactoryFn<T> factory      // how to create a new instance from the binary data, this also gets cached
	) {
		array ??= new T[elements];

		ArgumentOutOfRangeException.ThrowIfLessThan(index, 0);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, elements);

		array[index] ??= factory(caller, data[(dataOffset + (index * sizeOfOne))..]);
		return ref array[index];
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T ProduceVaradicArrayIdx<T>(
		object caller,                 // the caller (some factories need this)
		[NotNull] ref List<T?>? array, // the array to initialize if null
		int dataOffset,                // the offset into data, LocalSeqIndex for example
		int index,                     // the array index
		int sizeOfOne,                 // size of one component
		Memory<byte> data,             // the raw binary
		FactoryFn<T> factory           // how to create a new instance from the binary data, this also gets cached
	) {
		array ??= [];

		ArgumentOutOfRangeException.ThrowIfLessThan(index, 0);
		array.EnsureCountDefault(index + 1);

		return array[index] ??= factory(caller, data[(dataOffset + (index * sizeOfOne))..]);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string ProduceASCIIString(ref string? cache, in Span<byte> data) {
		if (cache == null) {
			using ASCIIStringView view = new(data);
			cache = new(view);
		}

		return cache;
	}


	public const int STUDIO_VERSION = 48;

	public const int IK_SELF = 1;
	public const int IK_WORLD = 2;
	public const int IK_GROUND = 3;
	public const int IK_RELEASE = 4;
	public const int IK_ATTACHMENT = 5;
	public const int IK_UNLATCH = 6;

	public const int MAXSTUDIOTRIANGLES = 65536;
	public const int MAXSTUDIOVERTS = 65536;
	public const int MAXSTUDIOFLEXVERTS = 10000;

	public const int MAXSTUDIOSKINS = 32;
	public const int MAXSTUDIOBONES = 256;
	public const int MAXSTUDIOFLEXDESC = 1024;
	public const int MAXSTUDIOFLEXCTRL = 96;
	public const int MAXSTUDIOPOSEPARAM = 24;
	public const int MAXSTUDIOBONECTRLS = 4;
	public const int MAXSTUDIOANIMBLOCKS = 256;

	public const int MAX_NUM_LODS = 8;
	public const int MAX_NUM_BONES_PER_VERT = 3;

	public const int MAXSTUDIOBONEBITS = 7;

	public const int MODEL_VERTEX_FILE_ID = (('V' << 24) + ('S' << 16) + ('D' << 8) + 'I');
	public const int MODEL_VERTEX_FILE_VERSION = 4;
	public const int MODEL_VERTEX_FILE_THIN_ID = (('V' << 24) + ('C' << 16) + ('D' << 8) + 'I');

	public const int BONE_USED_MASK = 0x0007FF00;
	public const int BONE_USED_BY_ANYTHING = 0x0007FF00;
	public const int BONE_USED_BY_HITBOX = 0x00000100;
	public const int BONE_USED_BY_ATTACHMENT = 0x00000200;
	public const int BONE_USED_BY_VERTEX_MASK = 0x0003FC00;
	public const int BONE_USED_BY_VERTEX_LOD0 = 0x00000400;
	public const int BONE_USED_BY_VERTEX_LOD1 = 0x00000800;
	public const int BONE_USED_BY_VERTEX_LOD2 = 0x00001000;
	public const int BONE_USED_BY_VERTEX_LOD3 = 0x00002000;
	public const int BONE_USED_BY_VERTEX_LOD4 = 0x00004000;
	public const int BONE_USED_BY_VERTEX_LOD5 = 0x00008000;
	public const int BONE_USED_BY_VERTEX_LOD6 = 0x00010000;
	public const int BONE_USED_BY_VERTEX_LOD7 = 0x00020000;
	public const int BONE_USED_BY_BONE_MERGE = 0x00040000;

	public const int BONE_TYPE_MASK = 0x00F00000;
	public const int ATTACHMENT_FLAG_WORLD_ALIGN = 0x10000;

	public const int BONE_FIXED_ALIGNMENT = 0x00100000;
	public const int BONE_HAS_SAVEFRAME_POS = 0x00200000;
	public const int BONE_HAS_SAVEFRAME_ROT = 0x00400000;

	public const int MAX_NUM_BONE_INDICES = 4;

	public const int USESHADOWLOD = -2;

	public const float VertAnimFixedPointScale = 1.0f / 4096.0f;
	public const float VertAnimFixedPointScaleInv = 1.0f / VertAnimFixedPointScale;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static int BONE_USED_BY_VERTEX_AT_LOD(int lod) => BONE_USED_BY_VERTEX_LOD0 << lod;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static int BONE_USED_BY_ANYTHING_AT_LOD(int lod) => ((BONE_USED_BY_ANYTHING & ~BONE_USED_BY_VERTEX_MASK) | BONE_USED_BY_VERTEX_AT_LOD(lod));
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void SetRootLOD(StudioHeader studioHdr, byte rootLOD) {
		if (studioHdr.NumAllowedRootLODs > 0 &&
			 rootLOD >= studioHdr.NumAllowedRootLODs) {
			rootLOD = (byte)(studioHdr.NumAllowedRootLODs - 1);
		}

		Assert(rootLOD >= 0 && rootLOD < MAX_NUM_LODS);
		rootLOD = Math.Clamp(rootLOD, (byte)0, (byte)(MAX_NUM_LODS - 1));

		// run the lod fixups that culls higher detail lods
		// vertexes are external, fixups ensure relative offsets and counts are cognizant of shrinking data
		// indexes are built in lodN..lod0 order so higher detail lod data can be truncated at load
		// the fixup lookup arrays are filled (or replicated) to ensure all slots valid
		int vertexindex = 0;
		int tangentsindex = 0;
		int bodyPartID;
		for (bodyPartID = 0; bodyPartID < studioHdr.NumBodyParts; bodyPartID++) {
			MStudioBodyParts bodyPart = studioHdr.BodyPart(bodyPartID);
			int modelID;
			for (modelID = 0; modelID < bodyPart.NumModels; modelID++) {
				MStudioModel model = bodyPart.Model(modelID);
				int totalMeshVertexes = 0;
				int meshID;
				for (meshID = 0; meshID < model.NumMeshes; meshID++) {
					MStudioMesh mesh = model.Mesh(meshID);

					// get the fixup, vertexes are reduced
					mesh.NumVertices = mesh.VertexData.NumLODVertexes[rootLOD];
					mesh.VertexOffset = totalMeshVertexes;
					totalMeshVertexes += mesh.NumVertices;
				}

				// stay in sync
				model.NumVertices = totalMeshVertexes;
				model.VertexIndex = vertexindex;
				model.TangentsIndex = tangentsindex;

				vertexindex += totalMeshVertexes * Unsafe.SizeOf<MStudioVertex>();
				tangentsindex += totalMeshVertexes * Unsafe.SizeOf<Vector4>();
			}
		}

		// track the set desired configuration
		studioHdr.RootLOD = rootLOD;
	}
	public sealed class BoneCacheManager
	{
		readonly object _lock = new();
		BoneCache[] _slots = new BoneCache[64];
		ushort[] _serials = new ushort[64];
		readonly Stack<int> _free = new();
		int _count;

		public memhandle_t Create(in BoneCacheParams p) {
			lock (_lock) {
				int idx = _free.Count > 0 ? _free.Pop() : _count++;
				if (idx >= _slots.Length) { Array.Resize(ref _slots, _slots.Length * 2); Array.Resize(ref _serials, _slots.Length); }
				if (_serials[idx] == 0) _serials[idx] = 1;
				_slots[idx] = BoneCache.CreateResource(p);
				return (memhandle_t)(((uint)_serials[idx] << 16) | (uint)(idx + 1));
			}
		}

		public BoneCache Get(memhandle_t h)
		{
			if (h == 0) return default;
			int idx = (int)(h & 0xFFFF) - 1;
			ushort serial = (ushort)(h >> 16);
			lock (_lock) {
				if ((uint)idx >= (uint)_count || _serials[idx] != serial) return default;
				return _slots[idx];
			}
		}

		public void Destroy(memhandle_t h) {
			if (h == 0) return;
			int idx = (int)(h & 0xFFFF) - 1;
			ushort serial = (ushort)(h >> 16);
			lock (_lock) {
				if ((uint)idx >= (uint)_count || _serials[idx] != serial) return;
				_slots[idx] = default;
				_serials[idx]++;            // bump serial → outstanding handles go stale
				if (_serials[idx] == 0) _serials[idx] = 1;
				_free.Push(idx);
			}
		}
	}

	static readonly BoneCacheManager g_StudioBoneCache = new();

	public static BoneCache GetBoneCache(memhandle_t cacheHandle) {
		return g_StudioBoneCache.Get(cacheHandle);
	}

	public static memhandle_t CreateBoneCache(in BoneCacheParams parms) {
		return g_StudioBoneCache.Create(parms);
	}

	public static void DestroyBoneCache(memhandle_t cacheHandle) {
		g_StudioBoneCache.Destroy(cacheHandle);
	}

	public static void InvalidateBoneCache(memhandle_t cacheHandle) {
		BoneCache cache = g_StudioBoneCache.Get(cacheHandle);
		if (!cache.IsNull())
			cache.TimeValid = -1.0;
	}
}

[Flags]
public enum StudioHdrFlags
{
	AutoGeneratedHitbox = 0x00000001,
	UsesEnvCubemap = 0x00000002,
	ForceOpaque = 0x00000004,
	TranslucentTwoPass = 0x00000008,
	StaticProp = 0x00000010,
	UsesFbTexture = 0x00000020,
	HasShadowLod = 0x00000040,
	UsesBumpmapping = 0x00000080,
	UseShadowLodMaterials = 0x00000100,
	Obsolete = 0x00000200,
	Unused = 0x00000400,
	NoForcedFade = 0x00000800,
	ForcePhonemeCrossfade = 0x00001000,
	ConstantDirectionalLightDot = 0x00002000,
	FlexesConverted = 0x00004000,
	BuiltInPreviewMode = 0x00008000,
	AmbientBoost = 0x00010000,
	DoNotCastShadows = 0x00020000,
	CastTextureShadows = 0x00040000,
	VertAnimFixedPointScale = 0x00200000
}


[InlineArray(Studio.MAX_NUM_LODS)] public struct InlineArrayMaxNumLODs<T> { T first; }
[InlineArray(Studio.MAXSTUDIOBONES)] public struct InlineArrayMaxStudioBones<T> { T first; }
[InlineArray(Studio.MAX_NUM_BONES_PER_VERT)] public struct InlineArrayMaxNumBonesPerVert<T> { T first; }

public class VirtualGroup
{
	public nint Cache;
	public readonly List<int> BoneMap = [];
	public readonly List<int> MasterBone = [];
	public readonly List<int> MasterSeq = [];
	public readonly List<int> MasterAnim = [];
	public readonly List<int> MasterAttachment = [];
	public readonly List<int> MasterPose = [];
	public readonly List<int> MasterNode = [];

	internal StudioHeader? GetStudioHdr() {
		return MDLCache.GetStudioHdr((MDLHandle_t)Cache);
	}
}

public struct VirtualSequence
{
	public StudioAnimSeqFlags Flags;
	public int Activity;
	public int Group;
	public int Index;
}

public struct VirtualGeneric
{
	public int Group;
	public int Index;
}

public partial class VirtualModel
{
	public VirtualGroup AnimGroup(int animation) {
		return Group[Anim[animation].Group];
	}
	public VirtualGroup? SeqGroup(int sequence) {
		if ((uint)sequence >= (uint)Seq.Count) {
			Assert(false);
			return null;
		}
		return Group[Seq[sequence].Group];
	}

	public readonly object Lock = new();

	public readonly List<VirtualSequence> Seq = [];
	public readonly List<VirtualGeneric> Anim = [];
	public readonly List<VirtualGeneric> Attachment = [];
	public readonly List<VirtualGeneric> Pose = [];
	public readonly List<VirtualGroup> Group = [];
	public readonly List<VirtualGeneric> Node = [];
	public readonly List<VirtualGeneric> IKLock = [];
	public readonly List<short> AutoplaySequences = [];
}

public enum StudioMeshGroupFlags
{
	IsFlexed = 0x1,
	IsHWSkinned = 0x2,
	IsDeltaFlexed = 0x4,
}

public class StudioMeshGroup
{
	public IMesh? Mesh;
	public int NumStrips;
	public StudioMeshGroupFlags Flags;
	public OptimizedModel.StripHeader[]? StripData;
	public ushort[]? GroupIndexToMeshIndex;
	public int NumVertices;
	public ushort[]? Indices;
	public bool MeshNeedsRestore;
	public int ColorMeshID;
	// IMorph?

	public ushort MeshIndex(int i) => GroupIndexToMeshIndex![Indices![i]];
}

public class StudioMeshData
{
	public int NumGroup;
	public StudioMeshGroup[]? MeshGroup;
}

public class StudioLODData
{
	public StudioMeshData[]? MeshData;
	public float SwitchPoint;
	public IMaterial[]? Materials;
	public int[]? MaterialFlags;
}

public class StudioHWData
{
	public int RootLOD;
	public int NumLODs;
	public StudioLODData[]? LODs;
	public int NumStudioMeshes;

	public int GetLODForMetric(float lodMetric) {
		if (NumLODs == 0)
			return 0;

		int numLODs = (LODs![NumLODs - 1].SwitchPoint < 0.0f) ? NumLODs - 1 : NumLODs;

		for (int i = RootLOD; i < numLODs - 1; i++) {
			float switchPoint = LODs[i + 1].SwitchPoint;
			if (switchPoint <= 0.0f || switchPoint > lodMetric)
				return i;
		}

		return numLODs - 1;
	}

	public float LODMetric(float unitSphereSize) => (unitSphereSize != 0.0f) ? (100.0f / unitSphereSize) : 0.0f;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct MStudioBoneWeight
{
	public InlineArrayMaxNumBonesPerVert<float> Weight;
	public InlineArrayMaxNumBonesPerVert<byte> Bone;
	public byte NumBones;
}

/// <summary>
/// mstudio_meshvertexdata_t
/// </summary>
public class MStudioMeshVertexData
{
	public Memory<byte> Data;
	public MStudioModelVertexData? ModelVertexData;
	public InlineArrayMaxNumLODs<int> NumLODVertexes;
	MStudioMesh mesh;
	public MStudioMeshVertexData(MStudioMesh mesh, Memory<byte> data) {
		Data = data;
		Span<byte> span = data.Span;
		for (int i = 0; i < Studio.MAX_NUM_LODS; i++) {
			NumLODVertexes[i] = span.Cast<byte, int>()[i + 1];
		}
		this.mesh = mesh;
	}

	public bool HasTangentData() => ModelVertexData!.HasTangentData();

	public int GetModelVertexIndex(int i) => mesh.VertexOffset + i;
	public ref Vector3 Position(int i) => ref ModelVertexData!.Position(GetModelVertexIndex(i));
	public ref Vector3 Normal(int i) => ref ModelVertexData!.Normal(GetModelVertexIndex(i));
	public ref Vector4 TangentS(int i) => ref ModelVertexData!.TangentS(GetModelVertexIndex(i));
	public ref Vector2 TexCoord(int i) => ref ModelVertexData!.TexCoord(GetModelVertexIndex(i));
	public ref MStudioBoneWeight BoneWeights(int i) => ref ModelVertexData!.BoneWeights(GetModelVertexIndex(i));
	public ref MStudioVertex Vertex(int i) => ref ModelVertexData!.Vertex(GetModelVertexIndex(i));
}


/// <summary>
/// mstudio_meshvertexdata_t
/// </summary>
public class MStudioAttachment
{
	public const int SIZEOF = 92; // 94 bytes 4 alignment
	public static MStudioAttachment FACTORY(object caller, Memory<byte> data) => new(data);
	public Memory<byte> Data;

	public int NameIndex;
	public uint Flags;
	public int LocalBone;
	public Matrix3x4 Local;
	string? nameCache;
	public string Name() => Studio.ProduceASCIIString(ref nameCache, Data.Span[NameIndex..]);

	public MStudioAttachment(Memory<byte> data) {
		Data = data;
		Span<byte> span = data.Span;

		SpanBinaryReader br = new(data.Span);
		br.Read(out NameIndex);
		br.Read(out Flags);
		br.Read(out LocalBone);
		br.Read(out Local);
	}

}


public class MStudioModelVertexData
{
	public Memory<byte> VertexData;
	public object? TangentData;
	MStudioModel model;

	public MStudioModelVertexData(MStudioModel model) {
		this.model = model;
	}

	public ref Memory<byte> GetVertexData() => ref VertexData;
	public object? GetTangentData() => TangentData;
	public T? GetTangentData<T>() => (T?)TangentData;

	public int GetGlobalVertexIndex(int i) {
		return i + (model.VertexIndex / Unsafe.SizeOf<MStudioVertex>());
	}

	public int GetGlobalTangentIndex(int i) {
		return i + (model.TangentsIndex / Unsafe.SizeOf<Vector4>());
	}

	public ref Vector3 Position(int i) => ref Vertex(i).Position;
	public ref Vector3 Normal(int i) => ref Vertex(i).Normal;
	public ref Vector4 TangentS(int i) => ref ((Memory<Vector4>)GetTangentData()!).Span[GetGlobalTangentIndex(i)];
	public ref Vector2 TexCoord(int i) => ref Vertex(i).TexCoord;
	public ref MStudioBoneWeight BoneWeights(int i) => ref Vertex(i).BoneWeights;
	public ref MStudioVertex Vertex(int i) => ref GetVertexData().Span.Cast<byte, MStudioVertex>()[GetGlobalVertexIndex(i)];

	// todo: verify
	public bool HasTangentData() => TangentData != null;
}

public class MStudioMesh
{
	public const int SIZEOF = 116; // don't feel like typing this out right now
	public Memory<byte> Data;
	public readonly MStudioModel Model;
	public MStudioMesh(MStudioModel model, Memory<byte> data) {
		Data = data;
		Model = model;

		Material = data.Span[0..].Cast<byte, int>()[0];
		ModelIndex = data.Span[4..].Cast<byte, int>()[0];
		NumVertices = data.Span[8..].Cast<byte, int>()[0];
		VertexOffset = data.Span[12..].Cast<byte, int>()[0];

		NumFlexes = data.Span[16..].Cast<byte, int>()[0];
		FlexIndex = data.Span[20..].Cast<byte, int>()[0];
		MaterialType = data.Span[24..].Cast<byte, int>()[0];
		MaterialParam = data.Span[28..].Cast<byte, int>()[0];
		MeshID = data.Span[32..].Cast<byte, int>()[0];
		Center = data.Span[36..].Cast<byte, Vector3>()[0];
		VertexData = new(this, data[48..]);
	}

	public int Material;
	public int ModelIndex;
	public int NumVertices;
	public int VertexOffset;

	public int NumFlexes;
	public int FlexIndex;
	public int MaterialType;
	public int MaterialParam;
	public int MeshID;
	public Vector3 Center;
	public readonly MStudioMeshVertexData VertexData;

	public MStudioMeshVertexData? GetVertexData(IStudioDataCache dataCache, StudioHeader studioHdr) {
		this.Model.GetVertexData(dataCache, studioHdr);
		VertexData.ModelVertexData = this.Model.VertexData;
		if (VertexData.ModelVertexData.VertexData.IsEmpty)
			return null;
		return VertexData;
	}

	MStudioFlex[]? flexCache;
	public MStudioFlex Flex(int i)
		=> Studio.ProduceArrayIdx(this, ref flexCache, NumFlexes, FlexIndex, i, MStudioFlex.SIZEOF, Data, MStudioFlex.FACTORY);
}

public enum StudioVertAnimType : byte
{
	Normal = 0,
	Wrinkle
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DStudioVertAnim
{
	public ushort Index;
	public byte Speed;
	public byte Side;
	public Vector48 Delta;
	public Vector48 NDelta;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MStudioVertAnim
{
	public ushort Index;
	public byte Speed;
	public byte Side;
	InlineArray3<short> delta;
	InlineArray3<short> ndelta;

	public readonly Vector3 GetDeltaFixed() => new(delta[0] * Studio.VertAnimFixedPointScale, delta[1] * Studio.VertAnimFixedPointScale, delta[2] * Studio.VertAnimFixedPointScale);
	public readonly Vector3 GetNDeltaFixed() => new(ndelta[0] * Studio.VertAnimFixedPointScale, ndelta[1] * Studio.VertAnimFixedPointScale, ndelta[2] * Studio.VertAnimFixedPointScale);
	public readonly Vector4 GetDeltaFixed4DAligned() => new(delta[0] * Studio.VertAnimFixedPointScale, delta[1] * Studio.VertAnimFixedPointScale, delta[2] * Studio.VertAnimFixedPointScale, 0.0f);
	public readonly Vector4 GetNDeltaFixed4DAligned() => new(ndelta[0] * Studio.VertAnimFixedPointScale, ndelta[1] * Studio.VertAnimFixedPointScale, ndelta[2] * Studio.VertAnimFixedPointScale, 0.0f);

	public void SetDeltaFixed(in Vector3 input) {
		delta[0] = (short)(input.X * Studio.VertAnimFixedPointScaleInv);
		delta[1] = (short)(input.Y * Studio.VertAnimFixedPointScaleInv);
		delta[2] = (short)(input.Z * Studio.VertAnimFixedPointScaleInv);
	}

	public void SetNDeltaFixed(in Vector3 inputNormal) {
		ndelta[0] = (short)(inputNormal.X * Studio.VertAnimFixedPointScaleInv);
		ndelta[1] = (short)(inputNormal.Y * Studio.VertAnimFixedPointScaleInv);
		ndelta[2] = (short)(inputNormal.Z * Studio.VertAnimFixedPointScaleInv);
	}
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MStudioVertAnimWrinkle
{
	public MStudioVertAnim VertAnim;
	public short WrinkleDelta;

	public readonly Vector4 GetDeltaFixed4DAligned() {
		Vector3 delta = VertAnim.GetDeltaFixed();
		return new(delta.X, delta.Y, delta.Z, WrinkleDelta * Studio.VertAnimFixedPointScale);
	}
	public readonly Vector4 GetNDeltaFixed4DAligned() => VertAnim.GetNDeltaFixed4DAligned();
}

public class MStudioFlex
{
	public const int SIZEOF = 60;
	public static MStudioFlex FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> Data;

	public int FlexDesc;
	public float Target0;
	public float Target1;
	public float Target2;
	public float Target3;
	public int NumVerts;
	public int VertIndex;
	public int FlexPair;
	public StudioVertAnimType VertAnimType;

	public MStudioFlex(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(Data.Span);
		br.Read(out FlexDesc);
		br.Read(out Target0);
		br.Read(out Target1);
		br.Read(out Target2);
		br.Read(out Target3);
		br.Read(out NumVerts);
		br.Read(out VertIndex);
		br.Read(out FlexPair);
		br.Read(out VertAnimType);
	}

	public ref MStudioVertAnim VertAnim(int i) {
		Assert(VertAnimType == StudioVertAnimType.Normal);
		return ref Data.Span[(VertIndex + i * Unsafe.SizeOf<MStudioVertAnim>())..].Cast<byte, MStudioVertAnim>()[0];
	}

	public ref MStudioVertAnimWrinkle VertAnimWrinkle(int i) {
		Assert(VertAnimType == StudioVertAnimType.Wrinkle);
		return ref Data.Span[(VertIndex + i * Unsafe.SizeOf<MStudioVertAnimWrinkle>())..].Cast<byte, MStudioVertAnimWrinkle>()[0];
	}

	public Span<byte> BaseVertAnim() => Data.Span[VertIndex..];
	public int VertAnimSizeBytes() => (VertAnimType == StudioVertAnimType.Normal) ? Unsafe.SizeOf<MStudioVertAnim>() : Unsafe.SizeOf<MStudioVertAnimWrinkle>();
}
public class MStudioMouth
{
	public const int SIZEOF = 20;
	public static MStudioMouth FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> Data;

	public int Bone;
	public Vector3 Forward;
	public int FlexDesc;

	public MStudioMouth(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(Data.Span);
		br.Read(out Bone);
		br.Read(out Forward);
		br.Read(out FlexDesc);
	}
}

public class MStudioEyeball
{
	public const int SIZEOF = 172;
	public static MStudioEyeball FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> Data;

	public int NameIndex;
	public string? nameCache;
	public string Name() => Studio.ProduceASCIIString(ref nameCache, Data.Span[NameIndex..]);

	public int Bone;
	public Vector3 Org;
	public float ZOffset;
	public float Radius;
	public Vector3 Up;
	public Vector3 Forward;
	public int Texture;

	public float IrisScale;

	public InlineArray3<int> UpperFlexDesc;
	public InlineArray3<int> LowerFlexDesc;
	public InlineArray3<float> UpperTarget;
	public InlineArray3<float> LowerTarget;

	public int UpperLidFlexDesc;
	public int LowerLidFlexDesc;
	public bool NonFACS;

	public MStudioEyeball(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(Data.Span);
		br.Read(out NameIndex);
		br.Read(out Bone);
		br.Read(out Org);
		br.Read(out ZOffset);
		br.Read(out Radius);
		br.Read(out Up);
		br.Read(out Forward);
		br.Read(out Texture);
		br.Read<int>();
		br.Read(out IrisScale);
		br.Read<int>();
		br.ReadInto<int>(UpperFlexDesc);
		br.ReadInto<int>(LowerFlexDesc);
		br.ReadInto<float>(UpperTarget);
		br.ReadInto<float>(LowerTarget);
		br.Read(out UpperLidFlexDesc);
		br.Read(out LowerLidFlexDesc);
		br.Advance(sizeof(int) * 4);
		NonFACS = br.Read<byte>() != 0;
	}
}

/// <summary>
/// analog of mstudiomodel_t
/// </summary>
public class MStudioIKLink
{
	public const int SIZEOF = 28;
	public static MStudioIKLink FACTORY(object caller, Memory<byte> data) => new(data);

	public int Bone;
	public Vector3 KneeDir;

	public MStudioIKLink(Memory<byte> data) {
		SpanBinaryReader br = new(data.Span);
		br.Read(out Bone);
		br.Read(out KneeDir);
	}
}

public class MStudioIKChain
{
	public const int SIZEOF = 16;
	public static MStudioIKChain FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> Data;

	public int NameIndex;
	string? nameCache;
	public string Name() => Studio.ProduceASCIIString(ref nameCache, Data.Span[NameIndex..]);

	public int LinkType;
	public int NumLinks;
	public int LinkIndex;
	MStudioIKLink[]? linkCache;
	public MStudioIKLink Link(int i)
		=> Studio.ProduceArrayIdx(this, ref linkCache, NumLinks, LinkIndex, i, MStudioIKLink.SIZEOF, Data, MStudioIKLink.FACTORY);

	public MStudioIKChain(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(data.Span);
		br.Read(out NameIndex);
		br.Read(out LinkType);
		br.Read(out NumLinks);
		br.Read(out LinkIndex);
	}
}

public class MStudioModel
{
	public const int SIZEOF = 148;
	public static MStudioModel FACTORY(object? caller, Memory<byte> data) => new(data);
	Memory<byte> Data;
	InlineArray64<byte> name;
	public int Type;
	public float BoundingRadius;
	public int NumMeshes;
	public int MeshIndex;
	public int NumVertices;
	public int VertexIndex;
	public int TangentsIndex;
	public int NumAttachments;
	public int AttachmentIndex;
	public int NumEyeballs;
	public int EyeballIndex;

	public readonly MStudioModelVertexData VertexData;

	public MStudioModel(Memory<byte> data) {
		Data = data;
		VertexData = new(this);
		SpanBinaryReader br = new(data.Span);
		br.ReadInto<byte>(name);
		br.Read(out Type);
		br.Read(out BoundingRadius);
		br.Read(out NumMeshes);
		br.Read(out MeshIndex);
		br.Read(out NumVertices);
		br.Read(out VertexIndex);
		br.Read(out TangentsIndex);
		br.Read(out NumAttachments);
		br.Read(out AttachmentIndex);
		br.Read(out NumEyeballs);
		br.Read(out EyeballIndex);

	}

	MStudioMesh[]? studioMeshCache;

	public MStudioMesh Mesh(int i) {
		if (studioMeshCache == null)
			studioMeshCache = new MStudioMesh[NumMeshes];

		ArgumentOutOfRangeException.ThrowIfLessThan(i, 0);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(i, NumMeshes);

		if (studioMeshCache[i] == null)
			return studioMeshCache[i] = new(this, Data[(MeshIndex + (i * MStudioMesh.SIZEOF))..]);
		return studioMeshCache[i];
	}

	string? nameCache;
	public string Name() => Studio.ProduceASCIIString(ref nameCache, name);

	MStudioEyeball[]? eyeballCache;
	public MStudioEyeball Eyeball(int i)
		=> Studio.ProduceArrayIdx(this, ref eyeballCache, NumEyeballs, EyeballIndex, i, MStudioEyeball.SIZEOF, Data, MStudioEyeball.FACTORY);

	public MStudioModelVertexData? GetVertexData(IStudioDataCache dataCache, StudioHeader studioHdr) {
		VertexFileHeader? vertexHdr = CacheVertexData(dataCache, studioHdr);
		if (vertexHdr == null) {
			VertexData.VertexData = null;
			VertexData.TangentData = null;
			return null;
		}

		VertexData.VertexData = vertexHdr.GetVertexData().Cast<MStudioVertex, byte>();
		VertexData.TangentData = vertexHdr.GetTangentData();

		if (VertexData.VertexData.IsEmpty)
			return null;

		return VertexData;
	}

	public VertexFileHeader? CacheVertexData(IStudioDataCache mdlCache, StudioHeader studioHdr) {
		return mdlCache.CacheVertexData(studioHdr);
	}
}

public class MStudioBodyParts(Memory<byte> Data)
{
	public const int SIZEOF = sizeof(int) * 4;
	public static MStudioBodyParts FACTORY(object caller, Memory<byte> data) => new(data);

	public int SzNameIndex => MemoryMarshal.Cast<byte, int>(Data.Span)[0];
	public int NumModels => MemoryMarshal.Cast<byte, int>(Data.Span)[1];
	public int Base => MemoryMarshal.Cast<byte, int>(Data.Span)[2];
	public int ModelIndex => MemoryMarshal.Cast<byte, int>(Data.Span)[3];


	string? nameCache;
	public string Name() => Studio.ProduceASCIIString(ref nameCache, Data.Span[SzNameIndex..]);

	MStudioModel[]? studioModelCache;

	public MStudioModel Model(int i) {
		return Studio.ProduceArrayIdx(this, ref studioModelCache, NumModels, ModelIndex, i, MStudioModel.SIZEOF, Data, MStudioModel.FACTORY);
	}
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct VertexFileFixup
{
	public int LOD;
	public int SourceVertexID;
	public int NumVertices;
}

[StructLayout(LayoutKind.Explicit, Pack = 4)]
public struct MStudioVertex
{
	[FieldOffset(0)] public MStudioBoneWeight BoneWeights;
	[FieldOffset(16)] public Vector3 Position;
	[FieldOffset(28)] public Vector3 Normal;
	[FieldOffset(40)] public Vector2 TexCoord;
}

public class VertexFileHeader
{
	public readonly byte[] Data;

	public VertexFileHeader(byte[] data) {
		Data = data;
		using BinaryReader br = new(new MemoryStream(data), System.Text.Encoding.ASCII);
		Parse(br);
	}

	public void Parse(BinaryReader br) {
		ID = br.ReadInt32();
		Version = br.ReadInt32();
		Checksum = br.ReadInt32();
		NumLODs = br.ReadInt32();
		for (int i = 0; i < Studio.MAX_NUM_LODS; i++)
			NumLODVertices[i] = br.ReadInt32();

		NumFixups = br.ReadInt32();
		FixupTableStart = br.ReadInt32();
		VertexDataStart = br.ReadInt32();
		TangentDataStart = br.ReadInt32();
	}
	public void CopyFrom(VertexFileHeader header2) {
		header2.Data.ClampedCopyTo(Data);
		using BinaryReader br = new(new MemoryStream(Data), System.Text.Encoding.ASCII);
		Parse(br);
	}

	public int ID;
	public int Version;
	public int Checksum;
	public int NumLODs;
	public InlineArrayMaxNumLODs<int> NumLODVertices;
	public int NumFixups;
	public int FixupTableStart;
	public int VertexDataStart;
	public int TangentDataStart;

	public Memory<MStudioVertex> GetVertexData() {
		if (ID == Studio.MODEL_VERTEX_FILE_ID && VertexDataStart != 0)
			return Data.AsMemory()[VertexDataStart..].Cast<byte, MStudioVertex>();
		else
			return null;
	}

	public Memory<Vector4> GetTangentData() {
		if (ID == Studio.MODEL_VERTEX_FILE_ID && TangentDataStart != 0)
			return Data.AsMemory()[TangentDataStart..].Cast<byte, Vector4>();
		else
			return null;
	}
}

public class StudioHeader2
{
	public Memory<byte> Data;

	public StudioHeader2(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(data.Span);

		br.Read(out NumSrcBoneTransform);
		br.Read(out SrcBoneTransformIndex);
		br.Read(out IllumPositionAttachmentIndex);
		br.Read(out MaxEyeDeflectionValue);
		br.Read(out LinearBoneIndex);
		br.Read(out SzNameIndex);
		br.Read(out BoneFlexDriverCount);
		br.Read(out BoneFlexDriverIndex);
	}

	public int NumSrcBoneTransform;
	public int SrcBoneTransformIndex;
	public int IllumPositionAttachmentIndex;
	public float MaxEyeDeflectionValue;
	public float MaxEyeDeflection() => MaxEyeDeflectionValue != 0.0f ? MaxEyeDeflectionValue : 0.866f;

	public int LinearBoneIndex;
	MStudioLinearBone? linearBones;
	public MStudioLinearBone? LinearBones() => LinearBoneIndex != 0 ? linearBones ??= new(Data[LinearBoneIndex..]) : null;

	public int SzNameIndex;
	public int BoneFlexDriverCount;
	public int BoneFlexDriverIndex;
	public InlineArray56<int> Reserved;
}

public class MStudioTexture
{
	public const int SIZE_OF_ONE = 64; // msvc reports size 64, alignment 4
	public static MStudioTexture FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> Data;

	public int SzNameIndex;
	public int Flags;
	public int Used;
	public int Unused;

	public IMaterial? Material;
	public object? ClientMaterial;

	public MStudioTexture(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(data.Span);

		SzNameIndex = br.Read<int>();
		Flags = br.Read<int>();
		Used = br.Read<int>();
		Unused = br.Read<int>();
	}

	public string? name;
	public string Name() => Studio.ProduceASCIIString(ref name, Data.Span[SzNameIndex..]);
}

public enum StudioAnimSeqFlags
{
	/// <summary>
	/// ending frame should be the same as the starting frame
	/// </summary>
	Looping = 0x0001,
	/// <summary>
	/// do not interpolate between previous animation and this one
	/// </summary>
	Snap = 0x0002,
	/// <summary>
	/// this sequence "adds" to the base sequences, not slerp blends
	/// </summary>
	Delta = 0x0004,
	/// <summary>
	/// temporary flag that forces the sequence to always play
	/// </summary>
	Autoplay = 0x0008,
	Post = 0x0010,
	/// <summary>
	/// this animation/sequence has no real animation data
	/// </summary>
	AllZeros = 0x0020,
	/// <summary>
	/// cycle index is taken from a pose parameter index
	/// </summary>
	CyclePose = 0x0080,
	/// <summary>
	/// cycle index is taken from a real-time clock, not the animations cycle index
	/// </summary>
	Realtime = 0x0100,
	/// <summary>
	/// sequence has a local context sequence
	/// </summary>
	Local = 0x0200,
	/// <summary>
	/// don't show in default selection views
	/// </summary>
	Hidden = 0x0400,
	/// <summary>
	/// a forward declared sequence (empty)
	/// </summary>
	Override = 0x0800,
	/// <summary>
	/// Has been updated at runtime to activity index
	/// </summary>
	Activity = 0x1000,
	/// <summary>
	/// Has been updated at runtime to event index
	/// </summary>
	Event = 0x2000,
	/// <summary>
	/// sequence blends in worldspace
	/// </summary>
	World = 0x4000
}

public enum StudioAnimFlags : byte
{
	/// <summary>
	/// Vector48
	/// </summary>
	RawPos = 0x01,
	/// <summary>
	/// Quaternion48
	/// </summary>
	RawRot = 0x02,
	/// <summary>
	/// mstudioanim_valueptr_t
	/// </summary>
	AnimPos = 0x04,
	/// <summary>
	/// mstudioanim_valueptr_t
	/// </summary>
	AnimRot = 0x08,
	Delta = 0x10,
	/// <summary>
	/// Quaternion64
	/// </summary>
	RawRot2 = 0x20,
}

public struct MStudioAnimValue
{
	public short Value;
	public readonly byte Valid => (byte)(Value & 0xFF);
	public readonly byte Total => (byte)((Value & 0xFF00) >> 8);
}

public class MStudioAnimValuePtr
{
	public const int SIZEOF = 6;

	public Memory<byte> Data;
	public InlineArray3<short> Offset;

	public MStudioAnimValuePtr(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(data.Span);
		Offset[0] = br.Read<short>();
		Offset[1] = br.Read<short>();
		Offset[2] = br.Read<short>();
	}

	public Span<MStudioAnimValue> Animvalue(int i) {
		if (Offset[i] > 0)
			return Data.Span[Offset[i]..].Cast<byte, MStudioAnimValue>();
		return null;
	}
}

// TODO: THESE ARE NOT EQUATABLE, PAY CLOSE ATTENTION IF SOURCE IS TRYING TO DO POINTER COMPARISON!!!!!!!!!!!!!!!!!!!!!!
// TODO: Should we use structs here instead and reinterpret casts? It's such a small data structure... and I worry heavily about
// the optimizations I tried to make to cope...
public class MStudioAnim
{
	public const int SIZEOF = 4; // Static offset from MSVC stats (mstudiobone_t size 4, alignment 2)
	public static MStudioAnim FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> data;
	public byte Bone;
	public StudioAnimFlags Flags;
	public short NextOffset;

	public MStudioAnim(Memory<byte> data) {
		this.data = data;
		Bone = data.Span[0];
		Flags = (StudioAnimFlags)data.Span[1];
		NextOffset = data.Span[2..4].Cast<byte, short>()[0];
	}


	public Span<byte> Data() => data.Span[SIZEOF..];

	MStudioAnimValuePtr? rotV;
	MStudioAnimValuePtr? posV;

	public MStudioAnimValuePtr RotV() => rotV ??= new(data[SIZEOF..]);
	public MStudioAnimValuePtr PosV() => posV ??= new(data[(SIZEOF + ((Flags & StudioAnimFlags.AnimRot) != 0 ? MStudioAnimValuePtr.SIZEOF : 0))..]);

	public ref Quaternion48 Quat48() => ref Data().Cast<byte, Quaternion48>()[0];
	public ref Quaternion64 Quat64() => ref Data().Cast<byte, Quaternion64>()[0];
	public unsafe ref Vector48 Pos()
		=> ref Data()[(((Flags & StudioAnimFlags.RawRot) != 0 ? 1 : 0) * sizeof(Quaternion48) + ((Flags & StudioAnimFlags.RawRot2) != 0 ? 1 : 0) * sizeof(Quaternion64))..].Cast<byte, Vector48>()[0];


	MStudioAnim? next;
	public MStudioAnim? Next() {
		if (NextOffset != 0)
			return next ??= FACTORY(null!, data[NextOffset..]);
		else
			return null;
	}
}

public struct MStudioIKError
{
	public const int SIZEOF = 28;

	public Vector3 Pos;
	public Quaternion Q;
}

public class MStudioCompressedIKError
{
	public const int SIZEOF = 36;

	public Memory<byte> Data;
	public InlineArray6<float> Scale;
	public InlineArray6<short> Offset;

	public MStudioCompressedIKError(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(data.Span);
		br.ReadInto<float>(Scale);
		br.ReadInto<short>(Offset);
	}

	public Span<MStudioAnimValue> Animvalue(int i) {
		if (Offset[i] > 0)
			return Data.Span[Offset[i]..].Cast<byte, MStudioAnimValue>();
		return null;
	}
}

public class MStudioIKRule
{
	public const int SIZEOF = 152;
	public static MStudioIKRule FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> Data;

	public int Index;

	public int Type;
	public int Chain;

	public int Bone;

	public int Slot;
	public float Height;
	public float Radius;
	public float Floor;
	public Vector3 Pos;
	public Quaternion Q;

	public int CompressedIKErrorIndex;
	MStudioCompressedIKError? compressedErrorCache;
	public MStudioCompressedIKError CompressedError() => compressedErrorCache ??= new(Data[CompressedIKErrorIndex..]);

	public int StartFrame;
	public int IKErrorIndex;
	public ReadOnlySpan<MStudioIKError> Error(int i) {
		if (IKErrorIndex != 0)
			return Data.Span[(IKErrorIndex + (i - StartFrame) * MStudioIKError.SIZEOF)..].Cast<byte, MStudioIKError>();
		return null;
	}

	public float Start;
	public float Peak;
	public float Tail;
	public float End;

	public float Contact;
	public float Drop;
	public float Top;

	public int AttachmentNameIndex;
	string? attachmentCache;
	public string Attachment() => Studio.ProduceASCIIString(ref attachmentCache, Data.Span[AttachmentNameIndex..]);

	public MStudioIKRule(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(data.Span);

		br.Read(out Index);
		br.Read(out Type);
		br.Read(out Chain);
		br.Read(out Bone);
		br.Read(out Slot);
		br.Read(out Height);
		br.Read(out Radius);
		br.Read(out Floor);
		br.Read(out Pos);
		br.Read(out Q);
		br.Read(out CompressedIKErrorIndex);
		br.Advance<int>(1);
		br.Read(out StartFrame);
		br.Read(out IKErrorIndex);
		br.Read(out Start);
		br.Read(out Peak);
		br.Read(out Tail);
		br.Read(out End);
		br.Advance<float>(1);
		br.Read(out Contact);
		br.Read(out Drop);
		br.Read(out Top);
		br.Advance<int>(3);
		br.Read(out AttachmentNameIndex);
	}
}

public class MStudioIKLock
{
	public const int SIZEOF = 32;
	public static MStudioIKLock FACTORY(object caller, Memory<byte> data) => new(data);

	public int Chain;
	public float PosWeight;
	public float LocalQWeight;
	public int Flags;

	public MStudioIKLock(Memory<byte> data) {
		SpanBinaryReader br = new(data.Span);
		br.Read(out Chain);
		br.Read(out PosWeight);
		br.Read(out LocalQWeight);
		br.Read(out Flags);
	}
}

public class MStudioLocalHierarchy
{
	public const int SIZEOF = 48; // Static offset from MSVC stats (mstudiobone_t size 48, alignment 4)
	public static MStudioLocalHierarchy FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> Data;

	public int Bone;
	public int NewParent;
	public float Start;
	public float Peak;
	public float Tail;
	public float End;
	public int StartFrame; // iStart 
	public int LocalAnimIndex;

	public MStudioLocalHierarchy(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(data.Span);

		br.Read(out Bone);
		br.Read(out NewParent);
		br.Read(out Start);
		br.Read(out Peak);
		br.Read(out Tail);
		br.Read(out End);
		br.Read(out StartFrame);
		br.Read(out LocalAnimIndex);
		br.Advance<int>(4);
	}
}

public class MStudioAnimSections
{
	public const int SIZEOF = 8; // Static offset from MSVC stats (mstudiobone_t size 8, alignment 4)
	public static MStudioAnimSections FACTORY(object caller, Memory<byte> data) => new() {
		AnimBlock = data.Span.Cast<byte, int>()[0],
		AnimIndex = data.Span.Cast<byte, int>()[1]
	};
	public int AnimBlock;
	public int AnimIndex;
}

public class MStudioMovement
{
	public const int SIZEOF = 44;
	public static MStudioMovement FACTORY(object caller, Memory<byte> data) => new(data);

	public int EndFrame;
	public int MotionFlags;
	public float V0;
	public float V1;
	public float Angle;
	public Vector3 Vector;
	public Vector3 Position;

	public MStudioMovement(Memory<byte> data) {
		SpanBinaryReader br = new(data.Span);

		br.Read(out EndFrame);
		br.Read(out MotionFlags);
		br.Read(out V0);
		br.Read(out V1);
		br.Read(out Angle);
		br.Read(out Vector);
		br.Read(out Position);
	}
}

public class MStudioAnimDesc
{
	public const int SIZEOF = 100; // Static offset from MSVC stats (mstudiobone_t size 100, alignment 4)
	public static MStudioAnimDesc FACTORY(object caller, Memory<byte> data) => new((StudioHeader)caller ?? throw new NullReferenceException(), data);
	public readonly StudioHeader hdr;
	public StudioHeader Studiohdr() => hdr;
	public Memory<byte> Data;

	public int NameIndex;
	public string? nameCache;
	public string Name() => Studio.ProduceASCIIString(ref nameCache, Data.Span[NameIndex..]);

	public float FPS;
	private int flags;
	public StudioAnimSeqFlags Flags => (StudioAnimSeqFlags)flags;

	public int NumFrames;

	public int NumMovements;
	public int MovementIndex;
	MStudioMovement[]? movements;
	public MStudioMovement Movement(int i) => Studio.ProduceArrayIdx(this, ref movements, NumMovements, MovementIndex, i, MStudioMovement.SIZEOF, Data, MStudioMovement.FACTORY);

	// UNUSED 6 INTEGERS

	int nAnimBlock;
	int nAnimIndex;

	List<List<MStudioAnim?>?> animCache = [];
	MStudioAnim CacheOffBlockIndex(int block, int index, Memory<byte> data) {
		animCache.EnsureCount(block + 1);
		var blockCache = animCache[block] ??= [];
		blockCache.EnsureCountDefault(index + 1);
		return blockCache[index] ??= MStudioAnim.FACTORY(null!, data[index..]);
	}

	public MStudioAnim? AnimBlock(int block, int index) {
		if (block == -1)
			return null;

		if (block == 0)
			return CacheOffBlockIndex(0, index, Data);

		Memory<byte> animBlock = Studiohdr().GetAnimBlock(block);
		if (!animBlock.IsEmpty)
			return CacheOffBlockIndex(block, index, animBlock);

		return null;
	}
	public MStudioAnim? Anim(ref int frame) => Anim(ref frame, out _);
	public MStudioAnim? Anim(ref int frame, out TimeUnit_t stall) {
		stall = default;

		MStudioAnim panim = null;

		int block = nAnimBlock;
		int index = nAnimIndex;
		int section = 0;

		if (SectionFrames != 0) {
			if (NumFrames > SectionFrames && frame == NumFrames - 1) {
				// last frame on long anims is stored separately
				frame = 0;
				section = (NumFrames / SectionFrames) + 1;
			}
			else {
				section = frame / SectionFrames;
				frame -= section * SectionFrames;
			}

			block = Section(section).AnimBlock;
			index = Section(section).AnimIndex;
		}

		if (block == -1) {
			// model needs to be recompiled
			return null;
		}

		panim = AnimBlock(block, index);

		// force a preload on the next block
		if (SectionFrames != 0) {
			int count = (NumFrames / SectionFrames) + 2;
			for (int i = section + 1; i < count; i++) {
				if (Section(i).AnimBlock != block) {
					AnimBlock(Section(i).AnimBlock, Section(i).AnimIndex);
					break;
				}
			}
		}

		if (panim == null) {
			// back up until a previously loaded block is found
			while (--section >= 0) {
				block = Section(section).AnimBlock;
				index = Section(section).AnimIndex;
				panim = AnimBlock(block, index);
				if (panim != null) {
					// set it to the last frame in the last valid section
					frame = SectionFrames - 1;
					break;
				}
			}
		}

		// try to guess a valid stall time interval (tuned for the X360)
		stall = 0.0;
		if (panim == null && section <= 0) {
			ZeroFrameStallTime = Platform.Time;
			stall = 1.0;
		}
		else if (panim != null && ZeroFrameStallTime != 0.0) {
			TimeUnit_t dt = Platform.Time - ZeroFrameStallTime;
			if (dt >= 0.0f)
				stall = MathLib.SimpleSpline(Math.Clamp((0.200f - dt) * 5.0f, 0.0f, 1.0f));

			if (stall == 0.0)
				ZeroFrameStallTime = 0.0;
		}

		return panim;
	}

	public int NumIKRules;
	public int IKRuleIndex;
	public int AnimBlockIKRuleIndex;
	MStudioIKRule?[]? ikRuleCache;
	public MStudioIKRule? IKRule(int i) {
		ikRuleCache ??= new MStudioIKRule?[NumIKRules];
		if (ikRuleCache[i] != null)
			return ikRuleCache[i];

		if (IKRuleIndex != 0)
			return ikRuleCache[i] = MStudioIKRule.FACTORY(this, Data[(IKRuleIndex + i * MStudioIKRule.SIZEOF)..]);
		else if (AnimBlockIKRuleIndex != 0) {
			if (nAnimBlock == 0)
				return ikRuleCache[i] = MStudioIKRule.FACTORY(this, Data[(AnimBlockIKRuleIndex + i * MStudioIKRule.SIZEOF)..]);
			else {
				Memory<byte> animBlocks = Studiohdr().GetAnimBlock(nAnimBlock);

				if (!animBlocks.IsEmpty)
					return ikRuleCache[i] = MStudioIKRule.FACTORY(this, animBlocks[(AnimBlockIKRuleIndex + i * MStudioIKRule.SIZEOF)..]);
			}
		}

		return null;
	}

	public int NumLocalHierarchy;
	public int LocalHierarchyIndex;
	MStudioLocalHierarchy[]? localHierarchyCache;
	public MStudioLocalHierarchy Hierarchy(int i) => Studio.ProduceArrayIdx(this, ref localHierarchyCache, NumLocalHierarchy, LocalHierarchyIndex, i, MStudioLocalHierarchy.SIZEOF, Data, MStudioLocalHierarchy.FACTORY);

	public int SectionIndex;
	public int SectionFrames;
	MStudioAnimSections[]? sections;
	public MStudioAnimSections Section(int i) => Studio.ProduceArrayIdx(this, ref sections, (NumFrames / SectionFrames) + 2, SectionIndex, i, MStudioAnimSections.SIZEOF, Data, MStudioAnimSections.FACTORY);


	public int ZeroFrameSpan;
	public int ZeroFrameCount;
	public int ZeroFrameIndex;
	public Span<byte> ZeroFrameData() {
		if (ZeroFrameIndex != 0) {
			return Data.Span[ZeroFrameIndex..];
		}

		return null;
	}

	public double ZeroFrameStallTime;

	public MStudioAnimDesc(StudioHeader hdr, Memory<byte> data) {
		this.hdr = hdr;
		Data = data;
		SpanBinaryReader br = new(Data.Span);

		br.Read<int>(); // index not needed
		br.Read(out NameIndex);
		br.Read(out FPS);
		br.Read(out flags);
		br.Read(out NumFrames);
		br.Read(out NumMovements);
		br.Read(out MovementIndex);
		br.Advance<int>(6);

		br.Read(out nAnimBlock);
		br.Read(out nAnimIndex);
		br.Read(out NumIKRules);
		br.Read(out IKRuleIndex);
		br.Read(out AnimBlockIKRuleIndex);
		br.Read(out NumLocalHierarchy);
		br.Read(out LocalHierarchyIndex);
		br.Read(out SectionIndex);
		br.Read(out SectionFrames);
		br.Read(out ZeroFrameSpan);
		br.Read(out ZeroFrameCount);
		br.Read(out ZeroFrameIndex);
	}
}

public class MStudioAnimBlock
{
	public const int SIZEOF = 8; // Static offset from MSVC stats (mstudiobone_t size 20, alignment 4)
	public static MStudioAnimBlock FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> Data;
	public int DataStart;
	public int DataEnd;

	public MStudioAnimBlock(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(Data.Span);
		br.Read(out DataStart);
		br.Read(out DataEnd);
	}
}
public class MStudioModelGroup
{
	public const int SIZEOF = 8; // Static offset from MSVC stats (
	public static MStudioModelGroup FACTORY(object caller, Memory<byte> data) => new(data);
	public Memory<byte> Data;
	public int LabelIndex;
	public int NameIndex;
	public MStudioModelGroup(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(Data.Span);
		br.Read(out LabelIndex);
		br.Read(out NameIndex);
	}

	string? labelCache;
	string? nameCache;
	public string Label() => Studio.ProduceASCIIString(ref labelCache, Data.Span[LabelIndex..]);
	public string Name() => Studio.ProduceASCIIString(ref nameCache, Data.Span[NameIndex..]);
}
public class MStudioEvent
{
	public const int SIZEOF = sizeof(float) + sizeof(int) + sizeof(int) + 64 + sizeof(int); // Static offset from MSVC stats (
	public static MStudioEvent FACTORY(object caller, Memory<byte> data) => new(data);
	public Memory<byte> Data;
	public float Cycle;
	public int Event;
	public int Type;
	InlineArray64<byte> options;
	public int EventIndex;
	public MStudioEvent(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(Data.Span);
		br.Read(out Cycle);
		br.Read(out Event);
		br.Read(out Type);
		br.ReadInto<byte>(options);
		br.Read(out EventIndex);
	}

	string? nameCache;
	public Span<byte> Options() => options;
	public string EventName() => Studio.ProduceASCIIString(ref nameCache, Data.Span[EventIndex..]);
}

public class MStudioPoseParamDesc
{
	public const int SIZEOF = 20; // Static offset from MSVC stats (mstudiobone_t size 20, alignment 4)
	public static MStudioPoseParamDesc FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> Data;

	public int NameIndex;
	public string? nameCache;
	public string Name() => Studio.ProduceASCIIString(ref nameCache, Data.Span[NameIndex..]);

	public int Flags;
	public float Start;
	public float End;
	public float Loop;

	public MStudioPoseParamDesc(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(Data.Span);
		br.Read(out NameIndex);
		br.Read(out Flags);
		br.Read(out Start);
		br.Read(out End);
		br.Read(out Loop);
	}
}

public enum LocalFlexController
{
	DummyFlexController = 0x7fffffff
}

public class MStudioFlexController
{
	public const int SIZEOF = 20;
	public static MStudioFlexController FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> Data;

	public int TypeIndex;
	public string? typeCache;
	public string Type() => Studio.ProduceASCIIString(ref typeCache, Data.Span[TypeIndex..]);

	public int NameIndex;
	public string? nameCache;
	public string Name() => Studio.ProduceASCIIString(ref nameCache, Data.Span[NameIndex..]);

	public int LocalToGlobal;
	public float Min;
	public float Max;

	public MStudioFlexController(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(Data.Span);
		br.Read(out TypeIndex);
		br.Read(out NameIndex);
		br.Read(out LocalToGlobal);
		br.Read(out Min);
		br.Read(out Max);
	}
}

public enum StudioFlexOpCode : int
{
	Const = 1,
	Fetch1 = 2,
	Fetch2 = 3,
	Add = 4,
	Sub = 5,
	Mul = 6,
	Div = 7,
	Neg = 8,
	Exp = 9,
	Open = 10,
	Close = 11,
	Comma = 12,
	Max = 13,
	Min = 14,
	TwoWay0 = 15,
	TwoWay1 = 16,
	NWay = 17,
	Combo = 18,
	Dominate = 19,
	DmeLowerEyelid = 20,
	DmeUpperEyelid = 21,
}

[StructLayout(LayoutKind.Explicit)]
public struct MStudioFlexOp
{
	public const int SIZEOF = 8;

	[FieldOffset(0)] public StudioFlexOpCode Op;
	[FieldOffset(4)] public int Index;
	[FieldOffset(4)] public float Value;
}

public class MStudioFlexRule
{
	public const int SIZEOF = 12;
	public static MStudioFlexRule FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> Data;

	public int Flex;
	public int NumOps;
	public int OpIndex;

	public MStudioFlexRule(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(Data.Span);
		br.Read(out Flex);
		br.Read(out NumOps);
		br.Read(out OpIndex);
	}

	public Span<MStudioFlexOp> FlexOp(int i) => Data.Span[OpIndex..].Cast<byte, MStudioFlexOp>()[i..];
}

[Flags]
public enum StudioMotionFlags
{
	X = 0x00000001,
	Y = 0x00000002,
	Z = 0x00000004,
	XR = 0x00000008,
	YR = 0x00000010,
	ZR = 0x00000020,

	LX = 0x00000040,
	LY = 0x00000080,
	LZ = 0x00000100,
	LXR = 0x00000200,
	LYR = 0x00000400,
	LZR = 0x00000800,

	Linear = 0x00001000,

	Types = 0x0003FFFF,
	RLoop = 0x00040000
}

public class MStudioBoneController
{
	public const int SIZEOF = 56;
	public static MStudioBoneController FACTORY(object caller, Memory<byte> data) => new(data);

	public Memory<byte> Data;

	public int Bone;
	private int type;
	public StudioMotionFlags Type => (StudioMotionFlags)type;
	public float Start;
	public float End;
	public int Rest;
	public int InputField;

	public MStudioBoneController(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(Data.Span);
		br.Read(out Bone);
		br.Read(out type);
		br.Read(out Start);
		br.Read(out End);
		br.Read(out Rest);
		br.Read(out InputField);
	}
}

public enum StudioAutolayerFlags
{
	Post = 0x0010,
	Spline = 0x0040,
	XFade = 0x0080,
	NoBlend = 0x0200,
	Local = 0x1000,
	Pose = 0x4000
}
public class MStudioActivityModifier
{
	public const int SIZEOF = 4;
	public static MStudioActivityModifier FACTORY(object caller, Memory<byte> data) => new(data);
	Memory<byte> data;
	public int NameIndex => data.Cast<byte, int>().Span[0];
	string? nameCache;

	public string? Name() {
		if (NameIndex == 0)
			return null;
		else if (nameCache != null)
			return nameCache;

		return Studio.ProduceASCIIString(ref nameCache, data.Span[NameIndex..]);
	}

	public MStudioActivityModifier(Memory<byte> data) {
		this.data = data;
	}
}
public class MStudioAutoLayer
{
	public const int SIZEOF = 24; // Static offset from MSVC stats (mstudiobone_t size 24, alignment 4)
	public static MStudioAutoLayer FACTORY(object caller, Memory<byte> data) => new(data);
	public short Sequence;
	public short Pose;
	private int flags;
	public float Start;
	public float Peak;
	public float Tail;
	public float End;
	public MStudioAutoLayer(Memory<byte> data) {
		SpanBinaryReader br = new(data.Span);
		br.Read(out Sequence);
		br.Read(out Pose);
		br.Read(out flags);
		br.Read(out Start);
		br.Read(out Peak);
		br.Read(out Tail);
		br.Read(out End);
	}
	public StudioAutolayerFlags Flags => (StudioAutolayerFlags)flags;
}
public class MStudioSeqDesc
{
	public const int SIZEOF = 212; // Static offset from MSVC stats (mstudiobone_t size 212, alignment 4)
	public static MStudioSeqDesc FACTORY(object caller, Memory<byte> data) => new((StudioHeader)caller ?? throw new NullReferenceException(), data);

	public readonly StudioHeader hdr;
	public StudioHeader Studiohdr() => hdr;
	public Memory<byte> Data;

	public int LabelIndex;
	public string? labelCache;
	public string Label() => Studio.ProduceASCIIString(ref labelCache, Data.Span[LabelIndex..]);

	public int ActivityNameIndex;
	public string? activityNameCache;
	public string ActivityName() => Studio.ProduceASCIIString(ref activityNameCache, Data.Span[ActivityNameIndex..]);

	private int flags;
	public StudioAnimSeqFlags Flags {
		get => (StudioAnimSeqFlags)flags;
		set => flags = (int)value;
	}

	public int Activity;
	public int ActWeight;

	public int NumEvents;
	public int EventIndex;
	MStudioEvent[]? eventCache;
	public MStudioEvent Event(int i)
		=> Studio.ProduceArrayIdx(this, ref eventCache, NumEvents, EventIndex, i, MStudioEvent.SIZEOF, Data, MStudioEvent.FACTORY);

	public Vector3 BBMin;
	public Vector3 BBMax;

	public int NumBlends;
	public int AnimIndexIndex;
	public int Anim(int x, int y) {
		if (x >= GroupSize[0]) x = GroupSize[0] - 1;
		if (y >= GroupSize[1]) y = GroupSize[1] - 1;

		int offset = y * GroupSize[0] + x;
		Span<short> blends = Data.Span[AnimIndexIndex..].Cast<byte, short>();
		return blends[offset];
	}

	public int MovementIndex;
	public InlineArray2<int> GroupSize;
	public InlineArray2<int> ParamIndex;
	public InlineArray2<float> ParamStart;
	public InlineArray2<float> ParamEnd;
	public int ParamParent;

	public float FadeInTime;
	public float FadeOutTime;

	public int LocalEntryNode;
	public int LocalExitNode;
	public int NodeFlags;

	public float EntryPhase;
	public float ExitPhase;

	public float LastFrame;

	public int NextSeq;
	public int Pose;

	public int NumIKRules;

	public int NumAutoLayers;
	public int AutoLayerIndex;
	MStudioAutoLayer[]? autolayerCache;
	public MStudioAutoLayer Autolayer(int i)
		=> Studio.ProduceArrayIdx(this, ref autolayerCache, NumAutoLayers, AutoLayerIndex, i, MStudioAutoLayer.SIZEOF, Data, MStudioAutoLayer.FACTORY);


	public int WeightListIndex;
	public ref float Boneweight(int i)
		=> ref Data.Span[WeightListIndex..].Cast<byte, float>()[i];
	public Span<float> Boneweights(int i)
		=> Data.Span[WeightListIndex..].Cast<byte, float>()[i..];
	public float Weight(int i) => Boneweight(i);

	public int PoseKeyIndex;
	public ref float PoseKeyRef(int param, int anim) => ref Data.Span[PoseKeyIndex..].Cast<byte, float>()[param * GroupSize[0] + anim];
	public float PoseKey(int param, int anim) => PoseKeyRef(param, anim);

	public int NumIKLocks;
	public int IKLockIndex;
	MStudioIKLock[]? ikLockCache;
	public MStudioIKLock IKLock(int i)
		=> Studio.ProduceArrayIdx(this, ref ikLockCache, NumIKLocks, IKLockIndex, i, MStudioIKLock.SIZEOF, Data, MStudioIKLock.FACTORY);

	public int KeyValueIndex;
	public int KeyValueSize;

	public int CyclePoseIndex;

	public int ActivityModifierIndex;
	public int NumActivityModifiers;
	MStudioActivityModifier[]? activityModifierCache;
	public MStudioActivityModifier ActivityModifier(int i)
		=> Studio.ProduceArrayIdx(this, ref activityModifierCache, NumActivityModifiers, ActivityModifierIndex, i, MStudioActivityModifier.SIZEOF, Data, MStudioActivityModifier.FACTORY);

	public MStudioSeqDesc() { hdr = null!; }
	public MStudioSeqDesc(StudioHeader hdr, Memory<byte> data) {
		this.hdr = hdr;
		Data = data;
		SpanBinaryReader br = new(Data.Span);

		br.Read<int>(); // index not needed
		br.Read(out LabelIndex);
		br.Read(out ActivityNameIndex);
		br.Read(out flags);
		br.Read(out Activity);
		br.Read(out ActWeight);
		br.Read(out NumEvents);
		br.Read(out EventIndex);
		br.Read(out BBMin);
		br.Read(out BBMax);
		br.Read(out NumBlends);
		br.Read(out AnimIndexIndex);
		br.Read(out MovementIndex);
		br.ReadInto<int>(GroupSize);
		br.ReadInto<int>(ParamIndex);
		br.ReadInto<float>(ParamStart);
		br.ReadInto<float>(ParamEnd);
		br.Read(out ParamParent);
		br.Read(out FadeInTime);
		br.Read(out FadeOutTime);
		br.Read(out LocalEntryNode);
		br.Read(out LocalExitNode);
		br.Read(out NodeFlags);
		br.Read(out EntryPhase);
		br.Read(out ExitPhase);
		br.Read(out LastFrame);
		br.Read(out NextSeq);
		br.Read(out Pose);
		br.Read(out NumIKRules);
		br.Read(out NumAutoLayers);
		br.Read(out AutoLayerIndex);
		br.Read(out WeightListIndex);
		br.Read(out PoseKeyIndex);
		br.Read(out NumIKLocks);
		br.Read(out IKLockIndex);
		br.Read(out KeyValueIndex);
		br.Read(out KeyValueSize);
		br.Read(out CyclePoseIndex);
		br.Read(out ActivityModifierIndex);
		br.Read(out NumActivityModifiers);
	}
}

/// <summary>
/// Analog of CStudioHdr
/// </summary>
public class StudioHdr
{
	public bool IsVirtual() => vModel != null;
	public bool IsValid() => studioHdr != null;
	public bool IsReadyForAccess() => studioHdr != null;

	public VirtualModel GetVirtualModel() => vModel!;
	public StudioHeader GetRenderHdr() => studioHdr!;

	private StudioHeader? studioHdr;
	private VirtualModel? vModel;
	public int NumBones() => studioHdr!.NumBones;
	public int BoneFlags(int i) => boneFlags[i];
	public int BoneParent(int i) => boneParent[i];
	public MStudioBone Bone(int i) => studioHdr!.Bone(i);
	public int NumBoneControllers() => studioHdr!.NumBoneControllers;
	public ReadOnlySpan<byte> GetBoneTableSortedByName() => studioHdr!.GetBoneTableSortedByName();
	/// <summary>
	/// Forces a preload of all bones into class views!
	/// </summary>
	/// <returns></returns>
	public Span<MStudioBone> Bones() => studioHdr.Bones();


	readonly List<StudioHeader?> StudioHdrCache = [];

	public StudioHdrFlags Flags() => studioHdr!.Flags;
	public AnonymousSafeFieldPointer<int> FrameUnlockCounterPtr;
	public int FrameUnlockCounter;
	public readonly object FrameUnlockCounterMutex = new();
	public void Init(StudioHeader? studioHdr, IMDLCache mdlcache) {
		this.studioHdr = studioHdr;

		this.vModel = null;
		StudioHdrCache.Clear();

		if (this.studioHdr == null)
			return;

		if (mdlcache != null) {
			FrameUnlockCounterPtr = mdlcache.GetFrameUnlockCounterPtr(MDLCacheDataType.StudioHDR);
			FrameUnlockCounter = FrameUnlockCounterPtr.Get() - 1;
		}

		if (this.studioHdr.NumIncludeModels != 0) {
			ResetVModel(this.studioHdr.GetVirtualModel());
		}

		boneFlags.EnsureCount(NumBones());
		boneParent.EnsureCount(NumBones());
		for (int i = 0; i < NumBones(); i++) {
			boneFlags[i] = Bone(i).Flags;
			boneParent[i] = Bone(i).Parent;
		}
	}

	readonly List<int> boneFlags = [];
	readonly List<int> boneParent = [];

	public bool SequencesAvailable() {
		if (studioHdr!.NumIncludeModels == 0) {
			return true;
		}

		if (vModel == null) {
			return (ResetVModel(studioHdr.GetVirtualModel()) != null);
		}
		else
			return true;
	}

	private VirtualModel? ResetVModel(VirtualModel? virtualModel) {
		if (virtualModel != null) {
			vModel = virtualModel;
			StudioHdrCache!.EnsureCountDefault(vModel.Group.Count());

			for (int i = 0; i < StudioHdrCache.Count; i++)
				StudioHdrCache[i] = null!;

			return vModel;
		}
		else {
			vModel = null;
			return null;
		}
	}

	public MStudioLinearBone? LinearBones() {
		return studioHdr!.LinearBones();
	}

	public int GetNumSeq() => vModel == null ? studioHdr!.NumLocalSeq : vModel.Seq.Count;

	public StudioHeader? GroupStudioHdr(int i) {
		if (FrameUnlockCounter != FrameUnlockCounterPtr.Get()) {
			lock (FrameUnlockCounterMutex) {
				if (FrameUnlockCounterPtr.Get() != FrameUnlockCounter) // i.e., this thread got the mutex
				{
					memcreset(StudioHdrCache.Base());
					FrameUnlockCounter = FrameUnlockCounterPtr.Get();
				}
			}
		}

		if (!StudioHdrCache.IsValidIndex(i)) {
			ReadOnlySpan<char> name = (this.studioHdr != null) ? this.studioHdr.GetName() : "<<null>>";
			Debugger.Break();
			return this.studioHdr; // return something known to probably exist, certainly things will be messed up, but hopefully not crash before the warning is noticed
		}

		StudioHeader? studioHdr = StudioHdrCache[i];

		if (studioHdr == null) {
			VirtualGroup group = vModel!.Group[i];
			studioHdr = group.GetStudioHdr();
			StudioHdrCache[i] = studioHdr;
		}

		Assert(studioHdr != null);
		return studioHdr;
	}

	static readonly MStudioSeqDesc s_nil_seq = new();
	public MStudioSeqDesc Seqdesc(int i) {
		if (i < 0 || i >= GetNumSeq()) {
			if (GetNumSeq() <= 0)
				return s_nil_seq;

			i = 0;
		}

		if (vModel == null)
			return this.studioHdr!.LocalSeqdesc(i);

		StudioHeader studioHdr = GroupStudioHdr(vModel.Seq[i].Group);

		return studioHdr.LocalSeqdesc(vModel.Seq[i].Index);
	}

	public int GetActivityListVersion() {
		if (vModel == null)
			return studioHdr!.ActivityListVersion;

		int version = studioHdr!.ActivityListVersion;

		int i;
		for (i = 1; i < vModel.Group.Count; i++) {
			StudioHeader studioHdr = GroupStudioHdr(i);
			Assert(studioHdr != null);
			version = Math.Min(version, studioHdr.ActivityListVersion);
		}

		return version;
	}
	public void SetActivityListVersion(int version) {
		studioHdr!.ActivityListVersion = version;

		if (vModel == null)
			return;

		int i;
		for (i = 1; i < vModel.Group.Count; i++) {
			StudioHeader studioHdr = GroupStudioHdr(i);
			Assert(studioHdr);
			studioHdr.SetActivityListVersion(version);
		}
	}

	public int GetEventListVersion() {
		if (vModel == null)
			return studioHdr!.EventsIndexed;

		int version = studioHdr!.EventsIndexed;

		int i;
		for (i = 1; i < vModel.Group.Count; i++) {
			StudioHeader studioHdr = GroupStudioHdr(i)!;
			Assert(studioHdr != null);
			version = Math.Min(version, studioHdr.EventsIndexed);
		}

		return version;
	}

	public void SetEventListVersion(int version) {
		studioHdr!.EventsIndexed = version;

		if (vModel == null)
			return;

		int i;
		for (i = 1; i < vModel.Group.Count; i++) {
			StudioHeader studioHdr = GroupStudioHdr(i)!;
			Assert(studioHdr != null);
			studioHdr.EventsIndexed = version;
		}
	}

	public LocalFlexController NumFlexControllers() => (LocalFlexController)studioHdr!.NumFlexControllers;
	public MStudioFlexController FlexController(LocalFlexController i) => studioHdr!.FlexController(i);
	public int NumFlexDesc() => studioHdr!.NumFlexDesc;
	public int NumFlexRules() => studioHdr!.NumFlexRules;
	public MStudioFlexRule FlexRule(int i) => studioHdr!.FlexRule(i);
	public int GetNumIKChains() => studioHdr!.NumIKChains;
	public MStudioIKChain IKChain(int i) => studioHdr!.IKChain(i);

	public int GetNumIKAutoplayLocks() {
		if (vModel == null)
			return studioHdr!.NumLocalIKAutoplayLocks;

		return vModel.IKLock.Count;
	}

	public MStudioIKLock IKAutoplayLock(int i) {
		if (vModel == null)
			return this.studioHdr!.LocalIKAutoplayLock(i);

		StudioHeader studioHdr = GroupStudioHdr(vModel.IKLock[i].Group)!;
		return studioHdr.LocalIKAutoplayLock(vModel.IKLock[i].Index);
	}
	public float MaxEyeDeflection() => studioHdr!.MaxEyeDeflection();

	public void RunFlexRules(ReadOnlySpan<float> src, Span<float> dest) {
		int i, j;

		for (i = 0; i < NumFlexDesc(); i++)
			dest[i] = 0;

		Span<float> stack = stackalloc float[32];
		for (i = 0; i < NumFlexRules(); i++) {
			stack.Clear();
			int k = 0;
			MStudioFlexRule rule = FlexRule(i);

			Span<MStudioFlexOp> pops = rule.FlexOp(0);

			for (j = 0; j < rule.NumOps; j++) {
				ref readonly MStudioFlexOp op = ref pops[j];
				switch (op.Op) {
					case StudioFlexOpCode.Add: stack[k - 2] = stack[k - 2] + stack[k - 1]; k--; break;
					case StudioFlexOpCode.Sub: stack[k - 2] = stack[k - 2] - stack[k - 1]; k--; break;
					case StudioFlexOpCode.Mul: stack[k - 2] = stack[k - 2] * stack[k - 1]; k--; break;
					case StudioFlexOpCode.Div:
						if (stack[k - 1] > 0.0001)
							stack[k - 2] = stack[k - 2] / stack[k - 1];
						else
							stack[k - 2] = 0;
						k--;
						break;
					case StudioFlexOpCode.Neg: stack[k - 1] = -stack[k - 1]; break;
					case StudioFlexOpCode.Max: stack[k - 2] = stack[k - 2] > stack[k - 1] ? stack[k - 2] : stack[k - 1]; k--; break;
					case StudioFlexOpCode.Min: stack[k - 2] = stack[k - 2] < stack[k - 1] ? stack[k - 2] : stack[k - 1]; k--; break;
					case StudioFlexOpCode.Const: stack[k] = op.Value; k++; break;
					case StudioFlexOpCode.Fetch1: {
							int m = FlexController((LocalFlexController)op.Index).LocalToGlobal;
							stack[k] = src[m];
							k++;
							break;
						}
					case StudioFlexOpCode.Fetch2: {
							stack[k] = dest[op.Index]; k++; break;
						}
					case StudioFlexOpCode.Combo: {
							int m = op.Index;
							int km = k - m;
							for (int n = km + 1; n < k; ++n)
								stack[km] *= stack[n];
							k = k - m + 1;
						}
						break;
					case StudioFlexOpCode.Dominate: {
							int m = op.Index;
							int km = k - m;
							float dv = stack[km];
							for (int n = km + 1; n < k; ++n)
								dv *= stack[n];
							stack[km - 1] *= 1.0f - dv;
							k -= m;
						}
						break;
					case StudioFlexOpCode.TwoWay0: {
							int m = FlexController((LocalFlexController)op.Index).LocalToGlobal;
							stack[k] = (float)MathLib.RemapValClamped(src[m], -1.0f, 0.0f, 1.0f, 0.0f);
							k++;
						}
						break;
					case StudioFlexOpCode.TwoWay1: {
							int m = FlexController((LocalFlexController)op.Index).LocalToGlobal;
							stack[k] = (float)MathLib.RemapValClamped(src[m], 0.0f, 1.0f, 0.0f, 1.0f);
							k++;
						}
						break;
					case StudioFlexOpCode.NWay: {
							LocalFlexController valueControllerIndex = (LocalFlexController)(int)stack[k - 1];
							int m = FlexController(valueControllerIndex).LocalToGlobal;
							float flValue = src[m];
							int v = FlexController((LocalFlexController)op.Index).LocalToGlobal;

							Vector4 filterRamp = new(stack[k - 5], stack[k - 4], stack[k - 3], stack[k - 2]);

							if (flValue <= filterRamp.X || flValue >= filterRamp.W)
								flValue = 0.0f;
							else if (flValue < filterRamp.Y)
								flValue = (float)MathLib.RemapValClamped(flValue, filterRamp.X, filterRamp.Y, 0.0f, 1.0f);
							else if (flValue > filterRamp.Z)
								flValue = (float)MathLib.RemapValClamped(flValue, filterRamp.Z, filterRamp.W, 1.0f, 0.0f);
							else
								flValue = 1.0f;

							stack[k - 5] = flValue * src[v];

							k -= 4;
						}
						break;
					case StudioFlexOpCode.DmeLowerEyelid: {
							MStudioFlexController closeLidV = FlexController((LocalFlexController)op.Index);
							float flCloseLidV = (float)MathLib.RemapValClamped(src[closeLidV.LocalToGlobal], closeLidV.Min, closeLidV.Max, 0.0f, 1.0f);

							MStudioFlexController closeLid = FlexController((LocalFlexController)(int)stack[k - 1]);
							float flCloseLid = (float)MathLib.RemapValClamped(src[closeLid.LocalToGlobal], closeLid.Min, closeLid.Max, 0.0f, 1.0f);

							int nEyeUpDownIndex = (int)stack[k - 3];
							float flEyeUpDown = 0.0f;
							if (nEyeUpDownIndex >= 0) {
								MStudioFlexController eyeUpDown = FlexController((LocalFlexController)(int)stack[k - 3]);
								flEyeUpDown = (float)MathLib.RemapValClamped(src[eyeUpDown.LocalToGlobal], eyeUpDown.Min, eyeUpDown.Max, -1.0f, 1.0f);
							}

							if (flEyeUpDown > 0.0)
								stack[k - 3] = (1.0f - flEyeUpDown) * (1.0f - flCloseLidV) * flCloseLid;
							else
								stack[k - 3] = (1.0f - flCloseLidV) * flCloseLid;
							k -= 2;
						}
						break;
					case StudioFlexOpCode.DmeUpperEyelid: {
							MStudioFlexController closeLidV = FlexController((LocalFlexController)op.Index);
							float flCloseLidV = (float)MathLib.RemapValClamped(src[closeLidV.LocalToGlobal], closeLidV.Min, closeLidV.Max, 0.0f, 1.0f);

							MStudioFlexController closeLid = FlexController((LocalFlexController)(int)stack[k - 1]);
							float flCloseLid = (float)MathLib.RemapValClamped(src[closeLid.LocalToGlobal], closeLid.Min, closeLid.Max, 0.0f, 1.0f);

							int nEyeUpDownIndex = (int)stack[k - 3];
							float flEyeUpDown = 0.0f;
							if (nEyeUpDownIndex >= 0) {
								MStudioFlexController eyeUpDown = FlexController((LocalFlexController)(int)stack[k - 3]);
								flEyeUpDown = (float)MathLib.RemapValClamped(src[eyeUpDown.LocalToGlobal], eyeUpDown.Min, eyeUpDown.Max, -1.0f, 1.0f);
							}

							if (flEyeUpDown < 0.0f)
								stack[k - 3] = (1.0f + flEyeUpDown) * flCloseLidV * flCloseLid;
							else
								stack[k - 3] = flCloseLidV * flCloseLid;
							k -= 2;
						}
						break;
				}
			}

			dest[rule.Flex] = stack[0];
		}
	}

	public MStudioBoneController BoneController(int i) => studioHdr!.BoneController(i);

	public float Mass() => studioHdr!.Mass;

	public Vector3 EyePosition() => studioHdr!.EyePosition;

	public MStudioAnimDesc Animdesc(int i) {
		if (vModel == null)
			return this.studioHdr!.LocalAnimdesc(i);

		if (vModel.Anim[i].Group == 0)
			return this.studioHdr!.LocalAnimdesc(vModel.Anim[i].Index);

		StudioHeader studioHdr = GroupStudioHdr(vModel.Anim[i].Group);
		return studioHdr.LocalAnimdesc(vModel.Anim[i].Index);
	}

	public MStudioPoseParamDesc PoseParameter(int i) {
		if (vModel == null)
			return this.studioHdr!.LocalPoseParameter(i);

		if (vModel.Pose[i].Group == 0)
			return this.studioHdr!.LocalPoseParameter(vModel.Pose[i].Index);

		StudioHeader studioHdr = GroupStudioHdr(vModel.Pose[i].Group);
		return studioHdr.LocalPoseParameter(vModel.Pose[i].Index);
	}

	public int GetSharedPoseParameter(int sequence, int localPose) {
		if (vModel == null)
			return localPose;

		if (localPose == -1)
			return localPose;

		Assert(vModel != null);

		int group = vModel.Seq[sequence].Group;
		VirtualGroup? pGroup = vModel.Group.IsValidIndex(group) ? vModel.Group[group] : null;

		return pGroup != null ? pGroup.MasterPose[localPose] : localPose;
	}

	public int iRelativeAnim(int baseseq, int relanim) {
		if (vModel == null)
			return relanim;

		VirtualGroup group = vModel.Group[vModel.Seq[baseseq].Group];
		return group.MasterAnim[relanim];
	}

	public string Name() {
		return studioHdr!.GetName();
	}
	public const int ACTIVITY_NOT_AVAILABLE = -1;

	public delegate void SetActivityForSequenceFn(StudioHdr studiohdr, int i);
	public static event SetActivityForSequenceFn? SetActivityForSequence;
	readonly static UtlSymbolTable g_ActivityModifiersTable = new();

	public class ActivityToSequenceMapping
	{
		public bool Initialized;
		public bool IsInitialized() {
			return Initialized;
		}

		readonly Dictionary<int, int> LOOKUP = [];

		public void Initialize(StudioHdr studiohdr) {
			if (SequenceTuples != null) return;
			SetValidationPair(studiohdr);
			if (!studiohdr.SequencesAvailable()) return;
			Initialized = true;

			// Some studio headers have no activities at all. In those
			// cases we can avoid a lot of this effort.
			bool foundOne = false;

			// for each sequence in the header...
			int NumSeq = studiohdr.GetNumSeq();
			for (int i = 0; i < NumSeq; ++i) {
				MStudioSeqDesc seqdesc = studiohdr.Seqdesc(i);

				if (0 == (seqdesc.Flags & StudioAnimSeqFlags.Activity))
					SetActivityForSequence?.Invoke(studiohdr, i);

				if (seqdesc.Activity >= 0) {
					foundOne = true;
					// look up if we already have an entry. First we need to make a speculative one --
					HashValueType entry = new(seqdesc.Activity, 0, 1, Math.Abs(seqdesc.ActWeight));
					ref HashValueType toUpdate = ref ActToSeqHash.TryGetRef(entry.ActivityIdx, out bool ok);
					if (ok) {
						// we already have an entry and must update it by incrementing count
						toUpdate.Count += 1;
						toUpdate.TotalWeight += Math.Abs(seqdesc.ActWeight);
					}
					else {
						// we do not have an entry yet; create one.
						ActToSeqHash.Add(entry.ActivityIdx, entry);
					}
				}
			}

			if (!foundOne)
				return;

			// Now, create starting indices for each activity. For an activity n, 
			// the starting index is of course the sum of counts [0..n-1]. 
			int sequenceCount = 0;
			int topActivity = 0; // this will store the highest seen activity number (used later to make an ad hoc map on the stack)
			foreach (var entry in ActToSeqHash) {
				ref HashValueType element = ref ActToSeqHash.TryGetRef(entry.Key, out bool ok);
				element.StartingIdx = sequenceCount;
				sequenceCount += element.Count;
				topActivity = Math.Max(topActivity, element.ActivityIdx);
			}


			// Allocate the actual array of sequence information. Note the use of restrict;
			// this is an important optimization, but means that you must never refer to this
			// array through m_pSequenceTuples in the scope of this function.
			SequenceTuple[] tupleList = new SequenceTuple[sequenceCount];
			SequenceTuples = tupleList; // save it off -- NEVER USE m_pSequenceTuples in this function!

			// Now we're going to actually populate that list with the relevant data. 
			// First, create an array on the stack to store how many sequences we've written
			// so far for each activity. (This is basically a very simple way of doing a map.)
			// This stack may potentially grow very large; so if you have problems with it, 
			// go to a utlmap or similar structure.
			int allocsize = (int)(topActivity + 1);
			allocsize = allocsize.AlignValue(16);
			Span<int> seqsPerAct = stackalloc int[allocsize];

			// okay, walk through all the sequences again, and write the relevant data into 
			// our little table.
			for (int i = 0; i < NumSeq; ++i) {
				MStudioSeqDesc seqdesc = studiohdr.Seqdesc(i);
				if (seqdesc.Activity >= 0) {
					ref HashValueType element = ref ActToSeqHash.TryGetRef(seqdesc.Activity, out bool ok);

					// If this assert trips, we've written more sequences per activity than we allocated 
					// (therefore there must have been a miscount in the first for loop above).
					int tupleOffset = seqsPerAct[seqdesc.Activity];
					Assert(tupleOffset < element.Count);

					if (seqdesc.NumActivityModifiers > 0) {
						// add entries for this model's activity modifiers
						(tupleList[element.StartingIdx + tupleOffset]).ActivityModifiers = new UtlSymId_t[seqdesc.NumActivityModifiers];

						for (int k = 0; k < seqdesc.NumActivityModifiers; k++)
							(tupleList[element.StartingIdx + tupleOffset]).ActivityModifiers![k] = g_ActivityModifiersTable.AddString(seqdesc.ActivityModifier(k).Name());

					}
					else {
						(tupleList[element.StartingIdx + tupleOffset]).ActivityModifiers = null;
					}

					// You might be tempted to collapse this pointer math into a single pointer --
					// don't! the tuple list is marked __restrict above.
					(tupleList[element.StartingIdx + tupleOffset]).SeqNum = (short)i; // store sequence number
					(tupleList[element.StartingIdx + tupleOffset]).Weight = (short)Math.Abs(seqdesc.ActWeight);

					// We can't have weights of 0
					// Assert( (tupleList + element.startingIdx + tupleOffset)->weight > 0 );
					if ((tupleList[element.StartingIdx + tupleOffset]).Weight == 0)
						(tupleList[element.StartingIdx + tupleOffset]).Weight = 1;

					seqsPerAct[seqdesc.Activity] += 1;
				}
			}
		}

		StudioHeader? expectedPStudioHdr;
		VirtualModel? expectedVModel;

		public void SetValidationPair(StudioHdr studiohdr) {
			expectedPStudioHdr = studiohdr.GetRenderHdr();
			expectedVModel = studiohdr.GetVirtualModel();
		}

		public bool ValidateAgainst(StudioHdr studiohdr) {
			if (Initialized)
				return studiohdr.GetRenderHdr() == expectedPStudioHdr && studiohdr.GetVirtualModel() == expectedVModel;
			else
				return true;
		}

		public void Reinitialize(StudioHdr studiohdr) {
			Initialized = false;
			SequenceTuples = null;
			ActToSeqHash.Clear();

			Initialize(studiohdr);
		}

		public struct SequenceTuple
		{
			public short SeqNum;
			public short Weight;
			public UtlSymId_t[]? ActivityModifiers;
		}

		public struct HashValueType(int activityIdx, int startingIdx, int count, int totalWeight)
		{
			public int ActivityIdx = activityIdx;
			public int StartingIdx = startingIdx;
			public int Count = count;
			public int TotalWeight = totalWeight;

			public override int GetHashCode() {
				return ActivityIdx.GetHashCode();
			}
		}

		public int NumSequencesForActivity(int forActivity) {
			if (SequenceTuples == null)
				return 0;

			if (ActToSeqHash.TryGetValue(forActivity, out HashValueType entry))
				return entry.Count;
			else
				return 0;
		}

		public SequenceTuple[]? SequenceTuples;
		public readonly Dictionary<int, HashValueType> ActToSeqHash = [];
	}

	public bool HaveSequenceForActivity(int activity) {
		if (!ActivityToSequence.IsInitialized())
			ActivityToSequence.Initialize(this);

		return ActivityToSequence.NumSequencesForActivity(activity) > 0;
	}


	public readonly ActivityToSequenceMapping ActivityToSequence = new();

	public int GetNumPoseParameters() {
		if (vModel == null) {
			if (studioHdr != null)
				return studioHdr.NumLocalPoseParameters;
			else
				return 0;
		}

		return vModel.Pose.Count;
	}

	public int GetTransition(int from, int to) {
		if (vModel == null)
			return studioHdr!.LocalTransition((from - 1) * studioHdr!.NumLocalNodes + (to - 1));
		return to;
	}
	public int EntryNode(int sequence) {
		MStudioSeqDesc seqdesc = Seqdesc(sequence);

		if (vModel == null || seqdesc.LocalEntryNode == 0)
			return seqdesc.LocalEntryNode;

		Assert(vModel != null);

		VirtualGroup group = vModel.Group[vModel.Seq[sequence].Group];

		return group.MasterNode[seqdesc.LocalEntryNode - 1] + 1;
	}

	public int ExitNode(int sequence) {
		MStudioSeqDesc seqdesc = Seqdesc(sequence);

		if (vModel == null || seqdesc.LocalExitNode == 0)
			return seqdesc.LocalExitNode;

		Assert(vModel != null);

		VirtualGroup group = vModel.Group[vModel.Seq[sequence].Group];

		return group.MasterNode[seqdesc.LocalExitNode - 1] + 1;
	}

	internal int RelativeSeq(int baseseq, int relseq) {
		if (vModel == null)
			return relseq;

		VirtualGroup group = vModel.Group[vModel.Seq[baseseq].Group];
		return group.MasterSeq[relseq];
	}

	public int GetAutoplayList(out Span<short> pList) {
		return studioHdr!.GetAutoplayList(out pList);
	}

	public int GetNumAttachments() {
		if (vModel == null)
			return studioHdr!.NumLocalAttachments;
		return vModel.Attachment.Count;
	}

	public MStudioAttachment Attachment(int i) {
		if (vModel == null)
			return this.studioHdr!.LocalAttachment(i);

		StudioHeader studioHdr = GroupStudioHdr(vModel.Attachment[i].Group);
		return studioHdr.LocalAttachment(vModel.Attachment[i].Index);
	}

	public int GetAttachmentBone(int i) {
		if (vModel == null) {
			return studioHdr!.LocalAttachment(i).LocalBone;
		}

		VirtualGroup pGroup = vModel.Group[vModel.Attachment[i].Group];
		MStudioAttachment attachment = Attachment(i);
		int iBone = pGroup.MasterBone[attachment.LocalBone];
		if (iBone == -1)
			return 0;
		return iBone;
	}

	public StudioHeader? SeqStudioHdr(int sequence) {
		if (vModel == null)
			return studioHdr;
		return GroupStudioHdr(vModel.Seq[sequence].Group);
	}

	public StudioHeader? AnimStudioHdr(int sequence) {
		if (vModel == null)
			return studioHdr;
		return GroupStudioHdr(vModel.Anim[sequence].Group);
	}

	public int NumHitboxSets() => studioHdr!.NumHitboxSets;
	public MStudioHitboxSet HitboxSet(int i) => studioHdr!.HitboxSet(i);

	public MStudioBBox Hitbox(int i, int set) => studioHdr!.Hitbox(i, set);
	public int HitboxCount(int set) => studioHdr!.HitboxCount(set);

	public int NumBodyParts() => studioHdr!.NumBodyParts;

	public MStudioBodyParts Bodypart(int i) => studioHdr!.BodyPart(i);
}

public class MStudioBone
{
	public const int SIZEOF = 216; // Static offset from MSVC stats (mstudiobone_t size 216, alignment 4)

	Memory<byte> Data;
	public int NameIndex;
	public int Parent;
	public InlineArray6<int> BoneController;
	public Vector3 Position;
	public Quaternion Quat;
	public RadianEuler Rot;
	public Vector3 PosScale;
	public Vector3 RotScale;
	public Matrix3x4 PoseToBone;
	public Quaternion Alignment;
	public int Flags;
	public int ProcType;
	public int ProcIndex;
	public int PhysicsBone;
	public int SurfacePropIdx;
	public int Contents;
	public InlineArray8<int> Unused;

	string? nameCache;
	public string Name() => Studio.ProduceASCIIString(ref nameCache, Data.Span[NameIndex..]);

	public MStudioBone(Memory<byte> data) {
		Data = data;

		SpanBinaryReader br = new(data.Span);
		br.Read(out NameIndex);
		br.Read(out Parent);
		br.ReadInto<int>(BoneController);
		br.Read(out Position);
		br.Read(out Quat);
		br.Read(out Rot);
		br.Read(out PosScale);
		br.Read(out RotScale);
		br.Read(out PoseToBone);
		br.Read(out Alignment);
		br.Read(out Flags);
		br.Read(out ProcType);
		br.Read(out ProcIndex);
		br.Read(out PhysicsBone);
		br.Read(out SurfacePropIdx);
		br.Read(out Contents);
	}
}

public class MStudioLinearBone
{
	public const int SIZEOF = 64; // Static offset from MSVC stats (mstudiobonelinear_t size 64, alignment 4)
	private Memory<byte> Data;

	public int NumBones;
	public int FlagsIndex;
	public int ParentIndex;
	public int PosIndex;
	public int QuatIndex;
	public int RotIndex;
	public int PoseToBoneIndex;
	public int PosScaleIndex;
	public int RotScaleIndex;
	public int QAlignmentIndex;

	public MStudioLinearBone(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(data.Span);

		br.Read(out NumBones);
		br.Read(out FlagsIndex);
		br.Read(out ParentIndex);
		br.Read(out PosIndex);
		br.Read(out QuatIndex);
		br.Read(out RotIndex);
		br.Read(out PoseToBoneIndex);
		br.Read(out PosScaleIndex);
		br.Read(out RotScaleIndex);
		br.Read(out QAlignmentIndex);
	}

	public int Flags(int i) => Data.Span[FlagsIndex..].Cast<byte, int>()[i];
	public ref int RefFlags(int i) => ref Data.Span[FlagsIndex..].Cast<byte, int>()[i];
	public int Parent(int i) => Data.Span[ParentIndex..].Cast<byte, int>()[i];
	public Vector3 Pos(int i) => Data.Span[PosIndex..].Cast<byte, Vector3>()[i];
	public Quaternion Quat(int i) => Data.Span[QuatIndex..].Cast<byte, Quaternion>()[i];
	public RadianEuler Rot(int i) => Data.Span[RotIndex..].Cast<byte, RadianEuler>()[i];
	public Matrix3x4 PoseToBone(int i) => Data.Span[PoseToBoneIndex..].Cast<byte, Matrix3x4>()[i];
	public Vector3 PosScale(int i) => Data.Span[PosScaleIndex..].Cast<byte, Vector3>()[i];
	public Vector3 RotScale(int i) => Data.Span[RotScaleIndex..].Cast<byte, Vector3>()[i];
	public Quaternion Alignment(int i) => Data.Span[QAlignmentIndex..].Cast<byte, Quaternion>()[i];
}

public class MStudioBBox
{
	public const int SIZEOF = 68;

	Memory<byte> Data;
	public int Bone;
	public int Group;
	public Vector3 BBMin;
	public Vector3 BBMax;
	public int HitboxNameIndex;
	public InlineArray8<int> Unused;

	string? hitboxNameCache;
	public string HitboxName() {
		if (HitboxNameIndex == 0)
			return "";
		return Studio.ProduceASCIIString(ref hitboxNameCache, Data.Span[HitboxNameIndex..]);
	}

	public MStudioBBox(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(data.Span);
		br.Read(out Bone);
		br.Read(out Group);
		br.Read(out BBMin);
		br.Read(out BBMax);
		br.Read(out HitboxNameIndex);
		br.ReadInto<int>(Unused);
	}
}

public class MStudioHitboxSet
{
	public const int SIZEOF = 12;

	Memory<byte> Data;
	public int NameIndex;
	public int NumHitboxes;
	public int HitboxIndex;

	string? nameCache;
	public string Name() => Studio.ProduceASCIIString(ref nameCache, Data.Span[NameIndex..]);

	public MStudioHitboxSet(Memory<byte> data) {
		Data = data;
		SpanBinaryReader br = new(data.Span);
		br.Read(out NameIndex);
		br.Read(out NumHitboxes);
		br.Read(out HitboxIndex);
	}

	public MStudioBBox Hitbox(int i) => new(Data[(HitboxIndex + i * MStudioBBox.SIZEOF)..]);
}

/// <summary>
/// Analog of studiohdr_t
/// </summary>
public class StudioHeader
{
	private StudioHeader() { }
	public StudioHeader(Memory<byte> data) {
		Data = data;
	}
	public readonly Memory<byte> Data;

	public int ID;
	public int Version;
	public int Checksum;
	public InlineArray64<byte> Name;
	public string? nameCache;
	public string GetName() => Studio.ProduceASCIIString(ref nameCache, Name);
	public int Length;

	public Vector3 EyePosition;
	public Vector3 IllumPosition;
	public Vector3 HullMin;
	public Vector3 HullMax;
	public Vector3 ViewBoundingBoxMin;
	public Vector3 ViewBoundingBoxMax;
	public StudioHdrFlags Flags;

	public int NumBones;
	public int BoneIndex;
	MStudioBone[]? studioBoneCache;
	bool preloadedBones = false;

	public MStudioBone Bone(int i) {
		if (studioBoneCache == null)
			studioBoneCache = new MStudioBone[NumBones];

		return studioBoneCache[i] ??= new(Data[(BoneIndex + (i * MStudioBone.SIZEOF))..]);
	}

	public Span<MStudioBone> Bones(int offset = 0) {
		if (preloadedBones)
			return studioBoneCache.AsSpan()[offset..];
		else {
			// Load any bones that haven't been loaded yet.
			for (int i = 0; i < NumBones; i++)
				Bone(i);

			// Cache that all bones are good
			preloadedBones = true;
			return studioBoneCache.AsSpan()[offset..];
		}
	}

	public StudioHeader? FindModel(out MDLHandle_t cache, ReadOnlySpan<char> modelName) {
		MDLHandle_t handle = MDLCache.FindMDL(modelName);
		cache = handle;
		return MDLCache.GetStudioHdr(handle);
	}

	public int NumBoneControllers;
	public int BoneControllerIndex;
	MStudioBoneController[]? boneControllerCache;
	public MStudioBoneController BoneController(int i)
		=> Studio.ProduceArrayIdx(this, ref boneControllerCache, NumBoneControllers, BoneControllerIndex, i, MStudioBoneController.SIZEOF, Data, MStudioBoneController.FACTORY);

	public int NumHitboxSets;
	public int HitboxSetIndex;

	public MStudioHitboxSet HitboxSet(int i) {
		Assert(i >= 0 && i < NumHitboxSets);
		return new(Data[(HitboxSetIndex + i * MStudioHitboxSet.SIZEOF)..]);
	}

	public MStudioBBox Hitbox(int i, int set) {
		MStudioHitboxSet s = HitboxSet(set);
		return s.Hitbox(i);
	}

	public int HitboxCount(int set) {
		MStudioHitboxSet s = HitboxSet(set);
		return s.NumHitboxes;
	}

	public int NumLocalAnim;
	public int LocalAnimIndex;
	MStudioAnimDesc[]? animDescs;
	internal MStudioAnimDesc LocalAnimdesc(int i) {
		if (i < 0 || i >= NumLocalAnim)
			i = 0;

		return Studio.ProduceArrayIdx(this, ref animDescs, NumLocalAnim, LocalAnimIndex, i, MStudioAnimDesc.SIZEOF, Data, MStudioAnimDesc.FACTORY);
	}

	public int NumLocalSeq;
	public int LocalSeqIndex;
	MStudioSeqDesc[]? seqDescs;
	internal MStudioSeqDesc LocalSeqdesc(int i) {
		if (i < 0 || i >= NumLocalSeq)
			i = 0;

		return Studio.ProduceArrayIdx(this, ref seqDescs, NumLocalSeq, LocalSeqIndex, i, MStudioSeqDesc.SIZEOF, Data, MStudioSeqDesc.FACTORY);
	}
	internal MStudioSeqDesc[] LocalSeqdescs() {
		for (int i = 0; i < NumLocalSeq; i++)
			LocalSeqdesc(i);
		return seqDescs ?? Array.Empty<MStudioSeqDesc>();
	}

	public int ActivityListVersion;
	public int EventsIndexed;

	public int NumTextures;
	public int TextureIndex;
	MStudioTexture[]? studioTextures;

	public MStudioTexture Texture(int i) {
		if (studioTextures == null)
			studioTextures = new MStudioTexture[NumTextures];

		return studioTextures[i] ??= new(Data[(TextureIndex + (MStudioTexture.SIZE_OF_ONE * i))..]);

		return Studio.ProduceArrayIdx(this, ref studioTextures, NumTextures, TextureIndex, i, MStudioTexture.SIZE_OF_ONE, Data, MStudioTexture.FACTORY);
	}

	public int NumCDTextures;
	public int CDTextureIndex;
	string[]? cdTextureCache;
	public ReadOnlySpan<char> CDTexture(int i) {
		if (cdTextureCache == null)
			cdTextureCache = new string[NumCDTextures];

		if (cdTextureCache[i] != null)
			return cdTextureCache[i];

		Span<byte> span = Data.Span;

		var offsetTable = MemoryMarshal.Cast<byte, int>(span[CDTextureIndex..]);
		int stringOffset = offsetTable[i];
		var strBytes = span[stringOffset..];

		using ASCIIStringView ascii = new(strBytes);
		cdTextureCache[i] = new(ascii);
		return cdTextureCache[i];
	}

	MStudioBodyParts[]? bodyPartCache;
	bool allCachedBodyParts = false;
	public MStudioBodyParts BodyPart(int i) {
		return Studio.ProduceArrayIdx(this, ref bodyPartCache, NumBodyParts, BodyPartIndex, i, MStudioBodyParts.SIZEOF, Data, MStudioBodyParts.FACTORY);
	}

	public Span<MStudioBodyParts> BodyParts(int idx = 0) {
		if (allCachedBodyParts)
			return bodyPartCache.AsSpan()[idx..];

		for (int i = 0; i < NumBodyParts; i++)
			BodyPart(i);

		allCachedBodyParts = true;
		return bodyPartCache.AsSpan()[idx..];
	}

	public int NumSkinRef;
	public int NumSkinFamilies;
	public int SkinIndex;

	public Span<short> SkinRef(int i) => Data.Span[SkinIndex..].Cast<byte, short>()[i..];

	public int NumBodyParts;
	public int BodyPartIndex;

	public int NumLocalAttachments;
	public int LocalAttachmentIndex;
	MStudioAttachment[]? attachmentCache;
	internal MStudioAttachment LocalAttachment(int i) {
		return Studio.ProduceArrayIdx(this, ref attachmentCache, NumLocalAttachments, LocalAttachmentIndex, i, MStudioAttachment.SIZEOF, Data, MStudioAttachment.FACTORY);
	}

	public int NumLocalNodes;
	public int LocalNodeIndex;

	public int LocalNodeNameIndex;
	string[]? localNodeNameCache;
	public ReadOnlySpan<char> LocalNodeName(int i) {
		if (localNodeNameCache == null)
			localNodeNameCache = new string[NumLocalNodes];

		if (localNodeNameCache[i] != null)
			return localNodeNameCache[i];

		Span<byte> span = Data.Span;

		var offsetTable = MemoryMarshal.Cast<byte, int>(span[LocalNodeNameIndex..]);
		int stringOffset = offsetTable[i];
		var strBytes = span[stringOffset..];

		using ASCIIStringView ascii = new(strBytes);
		localNodeNameCache[i] = new(ascii);
		return localNodeNameCache[i];
	}

	public int NumFlexDesc;
	public int FlexDescIndex;

	public int NumFlexControllers;
	public int FlexControllerIndex;
	MStudioFlexController[]? flexControllerCache;
	public MStudioFlexController FlexController(LocalFlexController i) {
		Assert(NumFlexControllers == 0 || (i >= 0 && (int)i < NumFlexControllers));
		return Studio.ProduceArrayIdx(this, ref flexControllerCache, NumFlexControllers, FlexControllerIndex, (int)i, MStudioFlexController.SIZEOF, Data, MStudioFlexController.FACTORY);
	}

	public int NumFlexRules;
	public int FlexRuleIndex;
	MStudioFlexRule[]? flexRuleCache;
	public MStudioFlexRule FlexRule(int i)
		=> Studio.ProduceArrayIdx(this, ref flexRuleCache, NumFlexRules, FlexRuleIndex, i, MStudioFlexRule.SIZEOF, Data, MStudioFlexRule.FACTORY);

	public int NumIKChains;
	public int IKChainIndex;
	MStudioIKChain[]? ikChainCache;
	public MStudioIKChain IKChain(int i)
		=> Studio.ProduceArrayIdx(this, ref ikChainCache, NumIKChains, IKChainIndex, i, MStudioIKChain.SIZEOF, Data, MStudioIKChain.FACTORY);

	public int NumMouths;
	public int MouthIndex;
	MStudioMouth[]? mouthCache;
	public MStudioMouth Mouth(int i)
		=> Studio.ProduceArrayIdx(this, ref mouthCache, NumMouths, MouthIndex, i, MStudioMouth.SIZEOF, Data, MStudioMouth.FACTORY);

	public int NumLocalPoseParameters;
	public int LocalPoseParamIndex;
	MStudioPoseParamDesc[]? poseParamDescCache;
	public MStudioPoseParamDesc LocalPoseParameter(int i)
		=> Studio.ProduceArrayIdx(this, ref poseParamDescCache, NumLocalPoseParameters, LocalPoseParamIndex, i, MStudioPoseParamDesc.SIZEOF, Data, MStudioPoseParamDesc.FACTORY);

	public int SurfacePropIndex;
	string? surfacePropCache;
	public string SurfaceProp() => Studio.ProduceASCIIString(ref surfacePropCache, Data.Span[SurfacePropIndex..]);
	public int KeyValueIndex;
	public int KeyValueSize;

	public int NumLocalIKAutoplayLocks;
	public int LocalIKAutoplayLockIndex;
	MStudioIKLock[]? localIKAutoplayLockCache;
	public MStudioIKLock LocalIKAutoplayLock(int i)
		=> Studio.ProduceArrayIdx(this, ref localIKAutoplayLockCache, NumLocalIKAutoplayLocks, LocalIKAutoplayLockIndex, i, MStudioIKLock.SIZEOF, Data, MStudioIKLock.FACTORY);

	public float Mass;
	public int Contents;

	public int NumIncludeModels;
	public int IncludeModelIndex;
	MStudioModelGroup[]? modelGroupCache;
	public MStudioModelGroup ModelGroup(int i)
		=> Studio.ProduceArrayIdx(this, ref modelGroupCache, NumIncludeModels, IncludeModelIndex, i, MStudioModelGroup.SIZEOF, Data, MStudioModelGroup.FACTORY);


	public MDLHandle_t VirtualModel;

	public int SzAnimBlockNameIndex;
	public string? animBlockNameCache;
	public string AnimBlockName() => Studio.ProduceASCIIString(ref animBlockNameCache, Data.Span[SzAnimBlockNameIndex..]);

	public int NumAnimBlocks;
	public int AnimBlockIndex;
	MStudioAnimBlock[]? animBlockCache;
	public MStudioAnimBlock AnimBlock(int i)
		=> Studio.ProduceArrayIdx(this, ref animBlockCache, NumAnimBlocks, AnimBlockIndex, i, MStudioAnimBlock.SIZEOF, Data, MStudioAnimBlock.FACTORY);

	public int AnimBlockModel;

	public Memory<byte> GetAnimBlock(int block) {
		return modelinfo.GetAnimBlock(this, block);
	}

	public int BoneTableByNameIndex;
	public ReadOnlySpan<byte> GetBoneTableSortedByName() => Data.Span.Slice(BoneTableByNameIndex, NumBones);

	public int VertexBase;
	public int IndexBase;
	public byte ConstDirectionalLightDot;
	public byte RootLOD;
	public byte NumAllowedRootLODs;
	public int NumFlexControllerUI;
	public int FlexControllerUIIndex;
	public float VertAnimFixedPointScale;

	public int StudioHDR2Index;
	StudioHeader2? studioHdr2;
	public StudioHeader2 StudioHdr2() => studioHdr2 ??= new(Data[StudioHDR2Index..]);

	public MStudioLinearBone? LinearBones() => StudioHDR2Index != 0 ? StudioHdr2().LinearBones() : null;
	public float MaxEyeDeflection() => StudioHDR2Index != 0 ? StudioHdr2().MaxEyeDeflection() : 0.866f;

	internal VirtualModel? GetVirtualModel() {
		if (NumIncludeModels == 0)
			return null;
		return modelinfo.GetVirtualModel(this);
	}

	public ref byte LocalTransition(int i) {
		return ref Data.Span[LocalNodeIndex..][i];
	}


	public int GetAutoplayList(out Span<short> pList) {
		return modelinfo.GetAutoplayList(this, out pList);
	}

	public int GetNumSeq() {
		if (NumIncludeModels == 0)
			return NumLocalSeq;

		VirtualModel? vModel = GetVirtualModel();
		Assert(vModel != null);
		return vModel.Seq.Count;
	}

	public MStudioSeqDesc Seqdesc(int i) {
		if (NumIncludeModels == 0)
			return LocalSeqdesc(i);

		VirtualModel? vModel = GetVirtualModel();
		Assert(vModel != null);

		if (vModel == null)
			return LocalSeqdesc(i);

		VirtualGroup group = vModel.Group[vModel.Seq[i].Group];
		StudioHeader? studioHdr = group.GetStudioHdr();
		Assert(studioHdr != null);

		return studioHdr.LocalSeqdesc(vModel.Seq[i].Index);
	}

	public int CountAutoplaySequences() {
		int count = 0;
		for (int i = 0; i < GetNumSeq(); i++) {
			MStudioSeqDesc seqdesc = Seqdesc(i);
			if ((seqdesc.Flags & StudioAnimSeqFlags.Autoplay) != 0)
				count++;
		}
		return count;
	}

	public void CopyAutoplaySequences(ref Memory<short> autoplaySequenceList, int outCount) {
		int outIndex = 0;
		for (int i = 0; i < GetNumSeq() && outIndex < outCount; i++) {
			MStudioSeqDesc seqdesc = Seqdesc(i);
			if ((seqdesc.Flags & StudioAnimSeqFlags.Autoplay) != 0) {
				autoplaySequenceList.Span[outIndex] = (short)i;
				outIndex++;
			}
		}
		autoplaySequenceList = autoplaySequenceList[..outIndex];
	}

	public int CopyAutoplaySequences(Span<short> autoplaySequenceList, int outCount) {
		int outIndex = 0;
		for (int i = 0; i < GetNumSeq() && outIndex < outCount; i++) {
			MStudioSeqDesc seqdesc = Seqdesc(i);
			if ((seqdesc.Flags & StudioAnimSeqFlags.Autoplay) != 0) {
				autoplaySequenceList[outIndex] = (short)i;
				outIndex++;
			}
		}
		return outIndex;
	}

	public int IllumPositionAttachmentIndex() {
		return StudioHDR2Index != 0 ? StudioHdr2().IllumPositionAttachmentIndex : 0;
	}

	internal void SetActivityListVersion(int actVersion) {
		ActivityListVersion = actVersion;

		if (NumIncludeModels == 0)
			return;

		VirtualModel? vModel = GetVirtualModel();
		Assert(vModel != null);

		int i;
		for (i = 1; i < vModel.Group.Count; i++) {
			VirtualGroup group = vModel.Group[i];
			StudioHeader? studioHdr = group.GetStudioHdr();
			Assert(studioHdr != null);
			studioHdr.SetActivityListVersion(actVersion);
		}
	}
}
