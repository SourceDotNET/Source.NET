
using CommunityToolkit.HighPerformance;

using System.Runtime.InteropServices;

using static Source.Common.Networking.SVC_ClassInfo;
using static Source.Common.OptimizedModel;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Source.Common;

public static class OptimizedModel
{
	public class MaterialReplacementHeader
	{
		public const int SIZEOF = sizeof(short) + sizeof(int);

		Memory<byte> Data;
		int ThisOffset;
		public short MaterialID;
		public int ReplacementMaterialNameOffset;

		public MaterialReplacementHeader(Memory<byte> fullData, int thisOffset) {
			Data = fullData;
			ThisOffset = thisOffset;
			Span<byte> data = fullData.Span[thisOffset..];
			MaterialID = data[0..].Cast<byte, short>()[0];
			ReplacementMaterialNameOffset = data[2..].Cast<byte, int>()[0];
		}

		string? nameCache;
		public string MaterialReplacementName() {
			if (nameCache == null) {
				using ASCIIStringView ascii = new(Data.Span[(ThisOffset + ReplacementMaterialNameOffset)..]);
				nameCache = new(ascii);
			}
			return nameCache;
		}
	}
	public class MaterialReplacementListHeader
	{
		public const int SIZEOF = sizeof(int) * 2;

		Memory<byte> Data;
		int ThisOffset;
		public int NumReplacements;
		public int ReplacementOffset;

		public MaterialReplacementListHeader(Memory<byte> fullData, int thisOffset) {
			Data = fullData;
			ThisOffset = thisOffset;
			Span<byte> data = fullData.Span[thisOffset..];
			NumReplacements = data[0..].Cast<byte, int>()[0];
			ReplacementOffset = data[4..].Cast<byte, int>()[0];
		}

		MaterialReplacementHeader[]? partCache;
		public MaterialReplacementHeader MaterialReplacement(int i) {
			if (partCache == null)
				partCache = new MaterialReplacementHeader[NumReplacements];

			return partCache[i] ?? (partCache[i] = new(Data, ThisOffset + ReplacementOffset + (MaterialReplacementHeader.SIZEOF * i)));
		}
	}

	public enum StripGroupFlags : byte
	{
		IsFlexed = 0x1,
		IsHWSkinned = 0x2,
		IsDeltaFlexed = 0x4,
		SuppressHWMorph = 0x8
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8)]
	public struct BoneStateChangeHeader
	{
		public int HardwareID;
		public int NewBoneID;
	}

	public enum StripHeaderFlags
	{
		IsTriList = 0x01,
		IsTriStrip = 0x02,
	}

	public class StripHeader
	{
		public Memory<byte> Data;

		public int NumIndices;
		public int IndexOffset;
		public int NumVerts;
		public int VertOffset;
		public short NumBones;
		public StripHeaderFlags Flags;
		public int NumBoneStateChanges;
		public int BoneStateChangeOffset;

		public StripHeader() { }
		public StripHeader(StripHeader copyFrom) {
			Data = new byte[copyFrom.Data.Length];
			copyFrom.Data.CopyTo(Data);
			ReadData();
		}
		public StripHeader(Memory<byte> mem) {
			Data = mem;
			ReadData();
		}
		void ReadData() {
			Span<byte> data = Data.Span;
			NumIndices = data[0..].Cast<byte, int>()[0];
			IndexOffset = data[4..].Cast<byte, int>()[0];

			NumVerts = data[8..].Cast<byte, int>()[0];
			VertOffset = data[12..].Cast<byte, int>()[0];

			NumBones = data[16..].Cast<byte, short>()[0];

			Flags = (StripHeaderFlags)data[18];

			NumBoneStateChanges = data[19..].Cast<byte, int>()[0];
			BoneStateChangeOffset = data[23..].Cast<byte, int>()[0];
		}

		public ref BoneStateChangeHeader BoneStateChange(int i) => ref Data.Span[BoneStateChangeOffset..].Cast<byte, BoneStateChangeHeader>()[i];
		public Span<BoneStateChangeHeader> BoneStateChanges(int i) => Data.Span[BoneStateChangeOffset..].Cast<byte, BoneStateChangeHeader>()[i..NumBoneStateChanges];
		public const int SIZEOF = 4 + 4 + 4 + 4 + 2 + 1 + 4 + 4;
		public const int SIZEOF_V49 = SIZEOF + 4 + 4;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct Vertex
	{
		public InlineArrayMaxNumBonesPerVert<byte> BoneWeightIndex;
		public byte NumBones;
		public ushort OrigMeshVertID;
		public InlineArrayMaxNumBonesPerVert<sbyte> BoneID;
	}

	public class StripGroupHeader
	{
		public const int SIZEOF = (sizeof(int) * 6) + 1;

		Memory<byte> Data;
		public int NumVerts;
		public int VertOffset;
		public int NumIndices;
		public int IndexOffset;
		public int NumStrips;
		public int StripOffset;
		byte flags;
		public StripGroupFlags Flags => (StripGroupFlags)flags;

		public const int SIZEOF_V49 = SIZEOF + (sizeof(int) * 2);

		int StudioVersion;

		public StripGroupHeader(Memory<byte> mem, int studioVersion) {
			Data = mem;
			StudioVersion = studioVersion;
			Span<byte> data = mem.Span;
			NumVerts = data[0..].Cast<byte, int>()[0];
			VertOffset = data[4..].Cast<byte, int>()[0];
			NumIndices = data[8..].Cast<byte, int>()[0];
			IndexOffset = data[12..].Cast<byte, int>()[0];
			NumStrips = data[16..].Cast<byte, int>()[0];
			StripOffset = data[20..].Cast<byte, int>()[0];
			flags = data[24..][0];
		}

		public ref Vertex Vertex(int i) => ref Data.Span[VertOffset..].Cast<byte, Vertex>()[i];
		public Span<Vertex> Vertices() => Data.Span[VertOffset..].Cast<byte, Vertex>()[..NumVerts];
		public ref ushort Index(int i) => ref Data.Span[IndexOffset..].Cast<byte, ushort>()[i];
		public Span<ushort> Indices() => Data.Span[IndexOffset..].Cast<byte, ushort>()[..NumIndices];
		StripHeader[]? partCache;
		public StripHeader Strip(int i) {
			if (partCache == null)
				partCache = new StripHeader[NumStrips];

			int stride = StudioVersion < 49 ? StripHeader.SIZEOF : StripHeader.SIZEOF_V49;
			return partCache[i] ?? (partCache[i] = new(Data[(StripOffset + (stride * i))..]));
		}
	}
	public class MeshHeader
	{
		public const int SIZEOF = (sizeof(int) * 2) + 1;

		Memory<byte> Data;
		public int NumStripGroups;
		public int StripGroupHeaderOffset;
		public byte Flags;

		public MeshHeader(Memory<byte> mem) {
			Data = mem;
			Span<byte> data = mem.Span;
			NumStripGroups = data[0..].Cast<byte, int>()[0];
			StripGroupHeaderOffset = data[4..].Cast<byte, int>()[0];
			Flags = data[8];
		}

		StripGroupHeader[]? partCache;
		public StripGroupHeader StripGroup(int i, int studioVersion) {
			if (partCache == null)
				partCache = new StripGroupHeader[NumStripGroups];

			int stride = studioVersion < 49 ? StripGroupHeader.SIZEOF : StripGroupHeader.SIZEOF_V49;
			return partCache[i] ?? (partCache[i] = new(Data[(StripGroupHeaderOffset + (stride * i))..], studioVersion));
		}
	}
	public class ModelLODHeader
	{
		public const int SIZEOF = (sizeof(int) * 2) + (sizeof(float) * 1);

		Memory<byte> Data;
		public int NumMeshes;
		public int MeshOffset;
		public float SwitchPoint;

		public ModelLODHeader(Memory<byte> mem) {
			Data = mem;
			Span<byte> data = mem.Span;
			NumMeshes = data[0..].Cast<byte, int>()[0];
			MeshOffset = data[4..].Cast<byte, int>()[0];
			SwitchPoint = data[8..].Cast<byte, float>()[0];
		}

		MeshHeader[]? partCache;
		public MeshHeader Mesh(int i) {
			if (partCache == null)
				partCache = new MeshHeader[NumMeshes];

			return partCache[i] ?? (partCache[i] = new(Data[(MeshOffset + (MeshHeader.SIZEOF * i))..]));
		}
	}
	public class ModelHeader
	{
		public const int SIZEOF = sizeof(int) * 2;

		Memory<byte> Data;
		public int NumLODs;
		public int LODOffset;

		public ModelHeader(Memory<byte> mem) {
			Data = mem;
			Span<byte> data = mem.Span;
			NumLODs = data[0..].Cast<byte, int>()[0];
			LODOffset = data[4..].Cast<byte, int>()[0];
		}

		ModelLODHeader[]? partCache;
		public ModelLODHeader LOD(int i) {
			if (partCache == null)
				partCache = new ModelLODHeader[NumLODs];

			return partCache[i] ?? (partCache[i] = new(Data[(LODOffset + (ModelLODHeader.SIZEOF * i))..]));
		}
	}
	public class BodyPartHeader
	{
		public const int SIZEOF = sizeof(int) * 2;

		Memory<byte> Data;
		public int NumModels;
		public int ModelOffset;
		public BodyPartHeader(Memory<byte> mem) {
			Data = mem;
			Span<byte> data = mem.Span;
			NumModels = data[0..].Cast<byte, int>()[0];
			ModelOffset = data[4..].Cast<byte, int>()[0];
		}

		ModelHeader[]? partCache;
		public ModelHeader Model(int i) {
			if (partCache == null)
				partCache = new ModelHeader[NumModels];

			return partCache[i] ?? (partCache[i] = new(Data[(ModelOffset + (ModelHeader.SIZEOF * i))..]));
		}
	}
	public const int OPTIMIZED_MODEL_FILE_VERSION = 7;
	public class FileHeader
	{
		public int Version;
		public int VertcacheSize;
		public ushort MaxBonesPerStrip;
		public ushort MaxBonesPerTri;
		public int MaxBonesPerVert;
		public int Checksum;
		public int NumLODs;
		public int MaterialReplacementListOffset;
		public int NumBodyParts;
		public int BodyPartOffset;

		Memory<byte> Data;
		public FileHeader(Memory<byte> mem) {
			Data = mem;
			Span<byte> data = mem.Span;
			Version = data[0..].Cast<byte, int>()[0];
			VertcacheSize = data[4..].Cast<byte, int>()[0];
			MaxBonesPerStrip = data[8..].Cast<byte, ushort>()[0];
			MaxBonesPerTri = data[10..].Cast<byte, ushort>()[0];
			MaxBonesPerVert = data[12..].Cast<byte, int>()[0];
			Checksum = data[16..].Cast<byte, int>()[0];
			NumLODs = data[20..].Cast<byte, int>()[0];
			MaterialReplacementListOffset = data[24..].Cast<byte, int>()[0];
			NumBodyParts = data[28..].Cast<byte, int>()[0];
			BodyPartOffset = data[32..].Cast<byte, int>()[0];
		}

		MaterialReplacementListHeader[]? materialReplacementsCache;
		public MaterialReplacementListHeader MaterialReplacementList(int i) {
			materialReplacementsCache ??= new MaterialReplacementListHeader[NumLODs];
			MaterialReplacementListHeader? ret = materialReplacementsCache[i];
			if (ret == null)
				return materialReplacementsCache[i] = new(Data, MaterialReplacementListOffset + (MaterialReplacementListHeader.SIZEOF * i));

			return ret;
		}

		BodyPartHeader[]? bodyPartCache;
		public BodyPartHeader BodyPart(int i) {
			if (bodyPartCache == null)
				bodyPartCache = new BodyPartHeader[NumBodyParts];

			return bodyPartCache[i] ?? (bodyPartCache[i] = new(Data[(BodyPartOffset + (BodyPartHeader.SIZEOF * i))..]));
		}
	}
}
