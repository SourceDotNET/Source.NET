namespace Game.Server;

[Flags]
public enum LinkInfo : byte
{
	StaleSuggested = 0x01,
	Off = 0x02,
}

public struct AI_Link
{
	public AI_Link() { }

	public short SrcID = -1;
	public short DestID = -1;

	public readonly int DestNodeID(int srcID) => srcID == SrcID ? DestID : SrcID;

	public InlineArrayNumHulls<Capability> AcceptedMoveTypes;

	public LinkInfo LinkInfo;

	public TimeUnit_t TimeStaleExpires;
}
