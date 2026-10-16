namespace Source.VPK
{
	public readonly struct VpkDirectory(int pathOffset, ushort pathLength)
	{
		public readonly int PathOffset = pathOffset;
		public readonly ushort PathLength = pathLength;

		public override string ToString() => $"VpkDirectory [path<{PathOffset}-{PathLength}>]";
	}
}
