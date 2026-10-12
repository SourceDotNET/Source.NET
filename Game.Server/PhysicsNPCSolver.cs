global using static Game.Server.PhysicsNPCSolverGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

using DEFINE = Source.DEFINE<Game.Server.PhysicsNPCSolver>;

namespace Game.Server;

[LinkEntityToClass("physics_npc_solver")]
public class PhysicsNPCSolver : LogicalEntity, IMotionEvent
{
	public static readonly new DataMap DataDesc = new(typeof(PhysicsNPCSolver), LogicalEntity.DataDesc, [
		DEFINE.FIELD(nameof(NPC), FieldType.EHandle),
		DEFINE.FIELD(nameof(Entity), FieldType.EHandle),
		DEFINE.FIELD(nameof(SeparationDuration), FieldType.Float),
		DEFINE.FIELD(nameof(CancelTime), FieldType.Time),
		DEFINE.FIELD(nameof(AllowIntersection), FieldType.Boolean),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public static readonly List<PhysicsNPCSolver> g_SolverList = [];

	Handle<AI_BaseNPC> NPC = new();
	EHANDLE Entity = new();
	IPhysicsMotionController? Controller;
	float SeparationDuration;
	TimeUnit_t CancelTime;
	bool AllowIntersection;

	public PhysicsNPCSolver() {
		g_SolverList.Add(this);
	}

	public override void Term() {
		g_SolverList.Remove(this);
		base.Term();
	}

	public override void OnRestore() {
		base.OnRestore();
		if (AllowIntersection)
			PhysDisableEntityCollisions(NPC.Get(), Entity.Get());
	}

	public static bool NPCPhysics_SolverExists(AI_BaseNPC npc, BaseEntity physicsObject) {
		foreach (PhysicsNPCSolver solver in g_SolverList) {
			if (solver.Entity.Get() == physicsObject && solver.NPC.Get() == npc)
				return true;
		}

		return false;
	}

	public static PhysicsNPCSolver Create(AI_BaseNPC npc, BaseEntity physicsObject, bool disableCollisions, float separationTime) {
		PhysicsNPCSolver solver = (PhysicsNPCSolver)CreateNoSpawn("physics_npc_solver", vec3_origin, vec3_angle, null)!;
		solver.Init(npc, physicsObject, disableCollisions, separationTime);
		solver.Spawn();
		return solver;
	}

	public void Init(AI_BaseNPC npc, BaseEntity physicsObject, bool disableCollisions, float separationTime) {
		NPC.Set(npc);
		Entity.Set(physicsObject);
		Controller = null;
		SeparationDuration = separationTime;
		AllowIntersection = disableCollisions;
	}

	void ResetCancelTime() {
		CancelTime = gpGlobals.CurTime + SeparationDuration;
		SetNextThink(CancelTime);
	}

	void BecomePenetrationSolver() {
		BaseEntity? entity = Entity.Get();
		if (entity != null) {
			AllowIntersection = true;
			Span<IPhysicsObject> list = new IPhysicsObject[VPHYSICS_MAX_OBJECT_LIST_COUNT];
			int listCount = entity.VPhysicsGetObjectList(list);
			PhysDisableEntityCollisions(NPC.Get(), entity);
			Controller = physenv.CreateMotionController(this);
			for (int i = 0; i < listCount; i++) {
				Controller.AttachObject(list[i], false);
				list[i].Wake();
			}
			Controller.SetPriority(IPhysicsMotionController.Priority.HighPriority);
		}
	}

	public override void Spawn() {
		if (AllowIntersection)
			BecomePenetrationSolver();
		else {
#if GMOD_DLL
			Entity.Get()?.SetNavIgnore();
#else
			Entity.Get()!.SetNavIgnore();
#endif
		}
		ResetCancelTime();
	}

	public override void UpdateOnRemove() {
		if (AllowIntersection) {
			physenv.DestroyMotionController(Controller!);
			Controller = null;
			PhysEnableEntityCollisions(NPC.Get(), Entity.Get());
		}
		else
			Entity.Get()?.ClearNavIgnore();

		base.UpdateOnRemove();
	}

	bool IsIntersecting() {
		AI_BaseNPC? npc = NPC.Get();
		BaseEntity? physics = Entity.Get();
		if (npc != null && physics != null) {
			Ray ray = default;
			Vector3 mins = npc.WorldAlignMins() - new Vector3(1, 1, 1);
			Vector3 maxs = npc.WorldAlignMaxs() + new Vector3(1, 1, 1);

			ray.Init(npc.GetAbsOrigin(), npc.GetAbsOrigin(), mins, maxs);
			Trace tr = default;
			enginetrace.ClipRayToEntity(in ray, npc.PhysicsSolidMaskForEntity(), physics, ref tr);
			if (tr.StartSolid)
				return true;
		}
		return false;
	}

	static bool IsContactOnNPCHead(IPhysicsFrictionSnapshot snapshot, IPhysicsObject physics, AI_BaseNPC npc) {
		float heightCheck = npc.GetAbsOrigin().Z + npc.GetHullMaxs().Z;
		physics.GetVelocity(out Vector3 vel, out _);
		snapshot.GetContactPoint(out Vector3 point);
		if (vel.LengthSquared() < 10.0f * 10.0f) {
			float topdist = MathF.Abs(point.Z - heightCheck);
			if (topdist < 2.0f)
				return true;
		}
		return false;
	}

	bool CheckTouching() {
		AI_BaseNPC? npc = NPC.Get();
		if (npc == null)
			return false;

		BaseEntity? physicsEnt = Entity.Get();
		if (physicsEnt == null)
			return false;

		IPhysicsObject? physics = physicsEnt.VPhysicsGetObject();
		IPhysicsObject? npcPhysics = npc.VPhysicsGetObject();
		if (npcPhysics == null || physics == null)
			return false;

		IPhysicsFrictionSnapshot snapshot = physics.CreateFrictionSnapshot();
		bool found = false;
		bool penetrate = false;

		while (snapshot.IsValid()) {
			IPhysicsObject? other = snapshot.GetObject(1);
			if (other == npcPhysics) {
				found = true;
				if (IsContactOnNPCHead(snapshot, physics, npc)) {
					penetrate = true;
					snapshot.MarkContactForDelete();
				}
				break;
			}
			snapshot.NextFrictionData();
		}
		snapshot.DeleteAllMarkedContacts(true);
		physics.DestroyFrictionSnapshot(snapshot);

		if (!penetrate && (physics.GetGameFlags() & PhysicsFlags.Penetrating) != 0)
			penetrate = IsIntersecting();

		if (penetrate) {
			physicsEnt.ClearNavIgnore();
			BecomePenetrationSolver();
		}

		return found;
	}

	public override void Think() {
		bool finished = AllowIntersection ? !IsIntersecting() : !CheckTouching();

		if (finished) {
			Util.Remove(this);
			return;
		}
		if (AllowIntersection) {
			IPhysicsObject? obj = Entity.Get()!.VPhysicsGetObject();
			if (obj == null) {
				Util.Remove(this);
				return;
			}
			obj.Wake();
		}
		ResetCancelTime();
	}

	public SimResult Simulate(IPhysicsMotionController? controller, IPhysicsObject? obj, TimeUnit_t deltaTime, out Vector3 linear, out Vector3 angular) {
		linear = default;
		angular = default;

		if (IsIntersecting()) {
			const float PUSH_SPEED = 150.0f;

			if ((obj!.GetGameFlags() & PhysicsFlags.PlayerHeld) != 0) {
#if GMOD_DLL
				for (int i = 0; i < gpGlobals.MaxClients; i++) {
					BasePlayer? player = Util.PlayerByIndex(i);
					player?.ForceDropOfCarriedPhysObjects(Entity.Get());
				}
#endif
			}

			ResetCancelTime();
			angular = default;
			linear = default;

			if ((obj.GetGameFlags() & PhysicsFlags.MultiObjectEntity) != 0) {
				if (Entity.Get()!.GetServerVehicle() != null)
					return SimResult.Nothing;
			}

			obj.GetPosition(out Vector3 origin, out _);
			obj.GetVelocity(out Vector3 vel, out _);
			Vector3 dir = origin - NPC.Get()!.GetAbsOrigin();
			dir.Z = dir.Z > 0 ? 0.1f : -0.1f;
			MathLib.VectorNormalize(ref dir);
			Vector3 angVel = default;

			PhysComputeSlideDirection(obj, dir * PUSH_SPEED, angVel, out Vector3 pushImpulse, out _, false, 0);

			dir = pushImpulse;
			MathLib.VectorNormalize(ref dir);

			if (Vector3.Dot(vel, dir) < PUSH_SPEED * 0.5f) {
				linear = pushImpulse;
				if (obj.GetContactPoint(out _, null!))
					linear.Z += GetCurrentGravity();
			}
			return SimResult.GlobalAcceleration;
		}
		return SimResult.Nothing;
	}
}

public static class PhysicsNPCSolverGlobals
{
	public static BaseEntity? NPCPhysics_CreateSolver(AI_BaseNPC npc, BaseEntity? physicsObject, bool disableCollisions, TimeUnit_t separationDuration) {
		if (disableCollisions) {
			if (PhysEntityCollisionsAreDisabled(npc, physicsObject!))
				return null;
		}
		else {
			if (physicsObject!.IsNavIgnored())
				return null;
		}
		return PhysicsNPCSolver.Create(npc, physicsObject!, disableCollisions, (float)separationDuration);
	}

	public static BaseEntity? EntityPhysics_CreateSolver(BaseEntity movingEntity, BaseEntity physicsObject, bool disableCollisions, TimeUnit_t separationDuration) => null;
}
