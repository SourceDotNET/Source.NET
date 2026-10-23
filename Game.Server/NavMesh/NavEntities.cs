using Game.Shared;
using Source.Common;

using System.Numerics;

namespace Game.Server.NavMesh;

class FuncNavCost : BaseEntity
{
	internal float GetCostMultiplier(BaseCombatCharacter who) {
		throw new NotImplementedException();
	}
}

[LinkEntityToClass("func_nav_avoid")]
class FuncNavAvoid : FuncNavCost
{

}

[LinkEntityToClass("func_nav_prefer")]
class FuncNavPrefer : FuncNavCost
{

}

[LinkEntityToClass("func_nav_blocker")]
class FuncNavBlocker : BaseEntity
{
	public static bool CalculateBlocked(bool[] resultByTeam, Vector3 mins, Vector3 maxs) {
		throw new NotImplementedException();
	}
}

[LinkEntityToClass("func_nav_avoidance_obstacle")]
public partial class FuncNavObstruction : BaseEntity, INavAvoidanceObstacle
{
	[NetworkTable("DT_FuncNavObstruction")]
	public static readonly new NetworkProp[] Props = [ // todo

	];

	public bool Disabled;

	int DrawDebugTextOverlays() {
		throw new NotImplementedException();
	}

	void UpdateOnRemove() { }

	public override void Spawn() {
		SetMoveType(Source.MoveType.None);
		SetModel(GetModelName());
		AddEffects(Source.EntityEffects.NoDraw);
		// SetCollisionGroup(CollisionGroup.None);
		SetSolid(Source.SolidType.None);
		AddSolidFlags(Source.SolidFlags.NotSolid);

		if (!Disabled) {
			ObstructNavAreas();
			NavMesh.Instance!.RegisterAvoidanceObstacle(this);
		}
	}

	// void InputEnable(inputdata_t &inputdata ) { }

	// void InputDisable(inputdata_t &inputdata ) { }

	void ObstructNavAreas() {
		Extent extent = default;
		extent.Init(this);
		NavMesh.Instance!.ForAllAreasOverlappingExtent(Invoke, extent);
	}

	bool Invoke(NavArea area) {
		area.MarkObstacleToAvoid(GetNavObstructionHeight());
		return true;
	}

	public bool IsPotentiallyAbleToObstructNavAreas() => true;
	public float GetNavObstructionHeight() => Nav.JumpCrouchHeight;
	public bool CanObstructNavAreas() => Disabled;
	public BaseEntity GetObstructingEntity() => this;
	public void OnNavMeshLoaded() {
		if (!DisableTouchFuncs)
			ObstructNavAreas();
	}
}
