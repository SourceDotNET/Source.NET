using Microsoft.Win32.SafeHandles;

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

				var reader = _reader.ReadDirectories(this, header.TreeLength);
				while (reader.MoveNext())
					Directories.Add(reader.Current);
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

				var reader = _reader.ReadDirectories(this, header.TreeLength);
				while (reader.MoveNext())
					Directories.Add(reader.Current);
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
