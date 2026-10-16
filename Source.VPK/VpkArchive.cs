using Microsoft.Win32.SafeHandles;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Source.VPK.Exceptions;
using Source.VPK.V1;

namespace Source.VPK
{
	public class VpkArchive : IDisposable
	{
		public List<VpkDirectory> Directories { get; set; }
		public bool IsMultiPart { get; set; }
		private VpkReaderBase _reader;
		internal List<ArchivePart> Parts { get; set; }
		internal string ArchivePath { get; set; }
		public readonly SafeFileHandle FileHandle;
		public byte[] Tree { get; private set; } = [];
		public int EntryCount { get; private set; }

		const int EntryDataSize = sizeof(uint) + sizeof(ushort) + sizeof(ushort) + sizeof(uint) + sizeof(uint) + sizeof(ushort);

		struct TreeReader(byte[] tree)
		{
			readonly byte[] Tree = tree;
			int Position;

			public bool ReadString(out int offset, out ushort length) {
				ReadOnlySpan<byte> remaining = Tree.AsSpan(Position);
				int end = remaining.IndexOf((byte)0);
				offset = Position;
				if (end == -1) {
					end = remaining.Length;
					Position += end;
				}
				else
					Position += end + 1;

				length = (ushort)end;
				return end != 0;
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			T Read<T>() where T : unmanaged {
				T value = MemoryMarshal.Read<T>(Tree.AsSpan(Position));
				Position += Unsafe.SizeOf<T>();
				return value;
			}

			public bool ReadEntry(int pathOffset, ushort pathLength, int extensionOffset, ushort extensionLength, out VpkEntry entry) {
				entry = default;
				if (!ReadString(out int filenameOffset, out ushort filenameLength))
					return false;

				if (Tree.Length - Position < EntryDataSize)
					return false;

				uint crc = Read<uint>();
				ushort preloadBytes = Read<ushort>();
				ushort archiveIndex = Read<ushort>();
				uint entryOffset = Read<uint>();
				uint entryLength = Read<uint>();
				// skip terminator
				Read<ushort>();
				int preloadTreeOffset = Position;
				Position = Math.Min(Position + preloadBytes, Tree.Length);

				entry = new VpkEntry(pathOffset, pathLength, filenameOffset, filenameLength, extensionOffset, extensionLength,
					crc, preloadBytes, preloadTreeOffset, archiveIndex, entryOffset, entryLength);
				return true;
			}
		}

		static void LowerAscii(Span<byte> text) {
			for (int i = 0; i < text.Length; i++) {
				if ((uint)(text[i] - 'A') <= 'Z' - 'A')
					text[i] |= 0x20;
			}
		}

		void ParseTree() {
			TreeReader reader = new(Tree);
			while (reader.ReadString(out int extensionOffset, out ushort extensionLength)) {
				LowerAscii(Tree.AsSpan(extensionOffset, extensionLength)); // once per extension block
				while (reader.ReadString(out int pathOffset, out ushort pathLength)) {
					LowerAscii(Tree.AsSpan(pathOffset, pathLength));
					Directories.Add(new VpkDirectory(pathOffset, pathLength));
					while (reader.ReadEntry(pathOffset, pathLength, extensionOffset, extensionLength, out VpkEntry entry)) {
						LowerAscii(Tree.AsSpan(entry.FilenameOffset, entry.FilenameLength));
						EntryCount++;
					}
				}
			}
		}

		public IEnumerable<VpkEntry> EnumerateEntries() {
			TreeReader reader = new(Tree);
			while (reader.ReadString(out int extensionOffset, out ushort extensionLength)) {
				while (reader.ReadString(out int pathOffset, out ushort pathLength)) {
					while (reader.ReadEntry(pathOffset, pathLength, extensionOffset, extensionLength, out VpkEntry entry))
						yield return entry;
				}
			}
		}

		public ReadOnlySpan<byte> GetPath(in VpkDirectory directory) => Tree.AsSpan(directory.PathOffset, directory.PathLength);
		public ReadOnlySpan<byte> GetPath(in VpkEntry entry) => Tree.AsSpan(entry.PathOffset, entry.PathLength);
		public ReadOnlySpan<byte> GetFilename(in VpkEntry entry) => Tree.AsSpan(entry.FilenameOffset, entry.FilenameLength);
		public ReadOnlySpan<byte> GetExtension(in VpkEntry entry) => Tree.AsSpan(entry.ExtensionOffset, entry.ExtensionLength);
		public ReadOnlySpan<byte> GetPreloadData(in VpkEntry entry) => Tree.AsSpan(entry.PreloadTreeOffset, Math.Min(entry.PreloadBytes, Tree.Length - entry.PreloadTreeOffset));

		public byte[]? ReadPreloadData(in VpkEntry entry) => entry.HasPreloadData ? GetPreloadData(in entry).ToArray() : null;
		public byte[]? ReadAnyData(in VpkEntry entry) => entry.HasPreloadData ? ReadPreloadData(in entry) : ReadData(in entry);

		ArchivePart? FindPart(ushort archiveIndex) {
			if (Parts == null)
				return null;

			for (int i = 0, c = Parts.Count; i < c; i++) {
				ArchivePart part = Parts[i];
				if (part.Index == archiveIndex)
					return part;
			}
			return null;
		}

		public byte[] ReadData(in VpkEntry entry) {
			ArchivePart? part = FindPart(entry.ArchiveIndex);
			if (part == null)
				return [];

			byte[] data = new byte[entry.PreloadBytes + entry.EntryLength];
			GetPreloadData(in entry).CopyTo(data);
			RandomAccess.Read(part.FileHandle, data.AsSpan(entry.PreloadBytes), entry.EntryOffset);
			return data;
		}

		public override string ToString() => $"VpkArchive '{ArchivePath}' [{Directories.Count} directories, {Parts.Count} parts]";

		public VpkArchive() {
			Directories = new List<VpkDirectory>();
		}

		public void Dispose() {
			if (Parts != null)
				foreach (var part in Parts)
					part.Dispose();
			FileHandle?.Dispose();
		}

		public void Load(string filename, VpkVersions.Versions version = VpkVersions.Versions.Any) {
			ArchivePath = filename;
			IsMultiPart = filename.EndsWith("_dir.vpk");
			if (IsMultiPart)
				LoadParts(filename);

			IVpkArchiveHeader header = null!;
			try {
				switch (version) {
					case VpkVersions.Versions.Any:
						// Try V2 first
						_reader = new V2.VpkReaderV2(filename);
						header = _reader.ReadArchiveHeader();
						if (!header.Verify()) {
							// Try V1
							_reader.Dispose();
							_reader = new VpkReaderV1(filename);

							header = _reader.ReadArchiveHeader();
							if (!header.Verify())
								throw new ArchiveParsingException("Invalid archive header (tried V2, then V1)");
						}
						break;
					case VpkVersions.Versions.V1:
						_reader = new VpkReaderV1(filename);

						header = _reader.ReadArchiveHeader();
						if (!header.Verify())
							throw new ArchiveParsingException("Invalid V1 archive header");
						break;
					case VpkVersions.Versions.V2:
						_reader = new V2.VpkReaderV2(filename);

						header = _reader.ReadArchiveHeader();
						if (!header.Verify())
							throw new ArchiveParsingException("Invalid V2 archive header");
						break;
				}

				Tree = _reader.ReadTree(header.TreeLength);
				ParseTree();
			}
			finally {
				_reader?.Dispose();
			}
		}

		public void Load(byte[] file, VpkVersions.Versions version = VpkVersions.Versions.V1) {
			IVpkArchiveHeader header = null!;
			try {
				switch (version) {
					case VpkVersions.Versions.Any:
						// Try V2 first
						_reader = new V2.VpkReaderV2(file);
						header = _reader.ReadArchiveHeader();
						if (!header.Verify()) {
							// Try V1
							_reader.Dispose();
							_reader = new VpkReaderV1(file);

							header = _reader.ReadArchiveHeader();
							if (!header.Verify())
								throw new ArchiveParsingException("Invalid archive header (tried V2, then V1)");
						}
						break;
					case VpkVersions.Versions.V1:
						_reader = new VpkReaderV1(file);

						header = _reader.ReadArchiveHeader();
						if (!header.Verify())
							throw new ArchiveParsingException("Invalid V1 archive header");
						break;
					case VpkVersions.Versions.V2:
						_reader = new V2.VpkReaderV2(file);

						header = _reader.ReadArchiveHeader();
						if (!header.Verify())
							throw new ArchiveParsingException("Invalid V2 archive header");
						break;
				}

				Tree = _reader.ReadTree(header.TreeLength);
				ParseTree();
			}
			finally {
				_reader?.Dispose();
			}
		}

		private void LoadParts(string filePath) {
			Parts = new List<ArchivePart>();

			var fileName = Path.GetFileName(filePath);

			// ignore incorrect files
			if (!fileName.Contains("_dir.vpk")) {
				return;
			}

			var fileBaseName = fileName.Replace("_dir.vpk", "");

			var dir = Path.GetDirectoryName(filePath);

			foreach (var subFile in Directory.GetFiles(dir)) {
				// ignore self
				if (Path.GetFileName(subFile).Equals(Path.GetFileName(filePath))) {
					continue;
				}

				var subFileName = Path.GetFileName(subFile);

				if (!subFileName.Contains('_')) {
					continue;
				}

				var subLastUnderscoreIndex = subFileName.LastIndexOf('_');
				var subFileBaseName = subFileName.Substring(0, subLastUnderscoreIndex);

				// ignore other files and vpk archives of other base archives
				if (!subFileBaseName.Equals(fileBaseName)) {
					continue;
				}

				var fileInfo = new FileInfo(subFile);
				var subSplit = subFileName.Split('_');
				var stringNumber = subSplit[subSplit.Length - 1].Replace(".vpk", "");

				var partIdx = int.Parse(stringNumber);
				Parts.Add(new ArchivePart((uint)fileInfo.Length, partIdx, subFile));
			}

			Parts.Add(new ArchivePart((uint)new FileInfo(filePath).Length, -1, filePath));
			Parts = Parts.OrderBy(p => p.Index).ToList();
		}
	}
}
