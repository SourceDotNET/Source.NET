using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Source.VPK
{
	internal abstract class VpkReaderBase : IDisposable
	{
		const int EntryDataSize = sizeof(uint) + sizeof(ushort) + sizeof(ushort) + sizeof(uint) + sizeof(uint) + sizeof(ushort);

		readonly Stream stream;
		readonly List<VpkEntry> entryBuffer = [];
		byte[] tree = [];
		int treePosition;
		long treeOffset;

		protected VpkReaderBase(string filename) => stream = File.OpenRead(filename);
		protected VpkReaderBase(byte[] file) => stream = new MemoryStream(file);

		public void Dispose() => stream.Dispose();

		public abstract IVpkArchiveHeader ReadArchiveHeader();

		void LoadTree(uint treeLength) {
			treeOffset = stream.Position;
			long remaining = Math.Max(stream.Length - treeOffset, 0);
			tree = new byte[Math.Min(treeLength, remaining)];
			int read = stream.ReadAtLeast(tree, tree.Length, false);
			if (read != tree.Length)
				Array.Resize(ref tree, read);
			treePosition = 0;
		}

		string ReadNullTerminatedString() {
			ReadOnlySpan<byte> remaining = tree.AsSpan(treePosition);
			int length = remaining.IndexOf((byte)0);
			if (length == -1) {
				length = remaining.Length;
				treePosition += length;
			}
			else
				treePosition += length + 1;

			return length == 0 ? string.Empty : Encoding.Latin1.GetString(remaining[..length]);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		T ReadTree<T>() where T : unmanaged {
			T value = MemoryMarshal.Read<T>(tree.AsSpan(treePosition));
			treePosition += Unsafe.SizeOf<T>();
			return value;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		protected static ref readonly T BytesToStructure<T>(ReadOnlySpan<byte> bytearray) where T : unmanaged {
			return ref MemoryMarshal.Cast<byte, T>(bytearray[..Unsafe.SizeOf<T>()])[0];
		}

		public struct DirectoryReader(VpkReaderBase reader, VpkArchive parentArchive)
		{
			readonly VpkReaderBase Reader = reader;
			readonly VpkArchive ParentArchive = parentArchive;
			string? ext = null;
			public VpkDirectory Current = null!;

			public bool MoveNext() {
				while (true) {
					if (ext == null) {
						ext = Reader.ReadNullTerminatedString();
						if (string.IsNullOrEmpty(ext))
							return false;            // end of tree
						ext = ext.ToLowerInvariant(); // once per extension block
					}

					var path = Reader.ReadNullTerminatedString();
					if (string.IsNullOrEmpty(path)) {
						ext = null;                   // end of this ext's paths, get the next one
						continue;
					}

					Current = new VpkDirectory(ParentArchive, path,
						Reader.ReadEntries(ParentArchive, ext, path).ToList());
					return true;
				}
			}
		}

		#region default
		public DirectoryReader ReadDirectories(VpkArchive parentArchive, uint treeLength) {
			LoadTree(treeLength);
			return new(this, parentArchive);
		}

		public struct EntryReader(VpkReaderBase reader, VpkArchive parentArchive, string ext, string path)
		{
			readonly VpkReaderBase Reader = reader;
			readonly VpkArchive ParentArchive = parentArchive;
			readonly string Ext = ext;
			readonly string Path = path;
			public bool MoveNext() {
				var fileName = Reader.ReadNullTerminatedString();
				if (string.IsNullOrEmpty(fileName))
					return false;

				if (Reader.tree.Length - Reader.treePosition < EntryDataSize)
					return false;

				var crc = Reader.ReadTree<uint>();
				var preloadBytes = Reader.ReadTree<ushort>();
				var archiveIdx = Reader.ReadTree<ushort>();
				var entryOffset = Reader.ReadTree<uint>();
				var entryLen = Reader.ReadTree<uint>();
				// skip terminator
				Reader.ReadTree<ushort>();
				nuint preloadDataOffset = (nuint)(Reader.treeOffset + Reader.treePosition);
				Reader.treePosition = Math.Min(Reader.treePosition + preloadBytes, Reader.tree.Length);

				Current = new VpkEntry(ParentArchive, crc, preloadBytes, preloadDataOffset, archiveIdx, entryOffset, entryLen, Ext, Path, fileName.ToLowerInvariant());
				return true;
			}

			public List<VpkEntry> ToList() {
				List<VpkEntry> buffer = Reader.entryBuffer;
				buffer.Clear();
				while (MoveNext())
					buffer.Add(Current);
				return new(buffer);
			}

			public VpkEntry Current = null!;
		}
		public EntryReader ReadEntries(VpkArchive parentArchive, string ext, string path) => new(this, parentArchive, ext, path.ToLowerInvariant());

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
