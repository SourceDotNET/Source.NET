namespace Source.VPK
{
	public readonly struct VpkEntry(int pathOffset, ushort pathLength, int filenameOffset, ushort filenameLength, int extensionOffset, ushort extensionLength,
		uint crc, ushort preloadBytes, int preloadTreeOffset, ushort archiveIndex, uint entryOffset, uint entryLength)
	{
		public readonly int PathOffset = pathOffset;
		public readonly int FilenameOffset = filenameOffset;
		public readonly int ExtensionOffset = extensionOffset;
		public readonly ushort PathLength = pathLength;
		public readonly ushort FilenameLength = filenameLength;
		public readonly ushort ExtensionLength = extensionLength;

		public readonly uint CRC = crc;
		public readonly ushort PreloadBytes = preloadBytes;
		public readonly ushort ArchiveIndex = archiveIndex;
		public readonly int PreloadTreeOffset = preloadTreeOffset;
		public readonly uint EntryOffset = entryOffset;
		public readonly uint EntryLength = entryLength;

		public bool HasPreloadData => PreloadBytes > 0;

		public override string ToString() => $"VpkEntry [crc {CRC}, entry<{EntryOffset}-{EntryLength}>]";
	}
}
