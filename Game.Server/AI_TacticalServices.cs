using System.Numerics;

namespace Game.Server;

public class AI_TacticalServices : AI_Component
{
	public AI_TacticalServices(AI_BaseNPC? outer) : base(outer) {
		Network = null;
	}

	public void Init(AI_Network? network) {
		Assert(network != null);
		Network = network;
		Pathfinder = GetOuter()!.GetPathfinder();
		Assert(Pathfinder != null);
	}

	public bool FindCoverPos(in Vector3 threatPos, in Vector3 threatEyePos, float minDist, float maxDist, out Vector3 result) => throw new NotImplementedException();

	public AI_Network? Network;
	public AI_Pathfinder? Pathfinder;
}
