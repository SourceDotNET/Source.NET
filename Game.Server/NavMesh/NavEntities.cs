using Game.Shared;
using Source.Common;
using Source.Common.Engine;

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

	public override int DrawDebugTextOverlays() {
		int offset = base.DrawDebugTextOverlays();

		if ((DebugOverlays & DebugOverlayBits.Text) != 0) {
			if (CanObstructNavAreas())
				EntityText(offset++, "Obstructing nav", (float)IVDebugOverlay.NDEBUG_PERSIST_TILL_NEXT_SERVER);
			else
				EntityText(offset++, "Not obstructing nav", (float)IVDebugOverlay.NDEBUG_PERSIST_TILL_NEXT_SERVER);
		}

		return offset;
	}

	public override void UpdateOnRemove() {
		NavMesh.Instance!.UnregisterAvoidanceObstacle(this);

		base.UpdateOnRemove();
	}

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
