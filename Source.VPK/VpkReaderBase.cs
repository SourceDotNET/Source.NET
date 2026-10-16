using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.VPK
{
	internal abstract class VpkReaderBase : IDisposable
	{
		readonly Stream stream;

		protected VpkReaderBase(string filename) => stream = File.OpenRead(filename);
		protected VpkReaderBase(byte[] file) => stream = new MemoryStream(file);

		public void Dispose() => stream.Dispose();

		public abstract IVpkArchiveHeader ReadArchiveHeader();

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		protected static ref readonly T BytesToStructure<T>(ReadOnlySpan<byte> bytearray) where T : unmanaged {
			return ref MemoryMarshal.Cast<byte, T>(bytearray[..Unsafe.SizeOf<T>()])[0];
		}

		#region default
		public byte[] ReadTree(uint treeLength) {
			long remaining = Math.Max(stream.Length - stream.Position, 0);
			byte[] tree = new byte[Math.Min(treeLength, remaining)];
			int read = stream.ReadAtLeast(tree, tree.Length, false);
			if (read != tree.Length)
				Array.Resize(ref tree, read);
			return tree;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		protected T Read<T>() where T : unmanaged {
			int sizeofT = Unsafe.SizeOf<T>();
			Span<byte> data = stackalloc byte[sizeofT];
			stream.ReadAtLeast(data, sizeofT, false);
			return MemoryMarshal.Cast<byte, T>(data)[0];
		}

		protected ReadOnlySpan<byte> ReadBytes(Span<byte> data) {
			stream.ReadAtLeast(data, data.Length, false);
			return data;
		}

		#endregion

		public abstract uint CalculateEntryOffset(uint offset);
	}
}
