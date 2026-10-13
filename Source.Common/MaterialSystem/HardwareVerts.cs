using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.Common.MaterialSystem;

public static class HardwareVerts
{
	public const int VHV_VERSION = 2;

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct MeshHeader
	{
		public uint Lod;
		public uint Vertexes;
		public uint Offset;
		public InlineArray4<uint> Unused;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct FileHeader
	{
		public int Version;
		public uint Checksum;
		public uint VertexFlags;
		public uint VertexSize;
		public uint Vertexes;
		public int Meshes;
		public InlineArray4<uint> Unused;

		public static ref readonly MeshHeader Mesh(ReadOnlySpan<byte> file, int mesh)
			=> ref MemoryMarshal.Cast<byte, MeshHeader>(file[Unsafe.SizeOf<FileHeader>()..])[mesh];

		public static ReadOnlySpan<byte> VertexBase(ReadOnlySpan<byte> file, int mesh)
			=> file[(int)Mesh(file, mesh).Offset..];
	}
}
