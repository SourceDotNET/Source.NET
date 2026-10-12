namespace Game.Server;

public class AI_PlayerAlly : AI_BaseActor
{
	public override int SelectSchedule() => throw new NotImplementedException();

	public bool SelectInterjection() => throw new NotImplementedException();

	public override EntityCapabilities ObjectCaps() => UsableNPCObjectCaps(base.ObjectCaps());
}
