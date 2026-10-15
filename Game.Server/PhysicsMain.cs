namespace Game.Server;

using Game.Shared;

using Source;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;
using Source.Engine;

using Steamworks;

using System.Numerics;
using System.Runtime.CompilerServices;

public partial class BaseEntity
{
	public void CheckStepSimulationChanged() {
		if (Physics.g_bTestMoveTypeStepSimulation != IsSimulatedEveryTick())
			SetSimulatedEveryTick(Physics.g_bTestMoveTypeStepSimulation);

		bool hadObject = HasDataObjectType(DataObjectType.StepSimulation);

		if (Physics.g_bTestMoveTypeStepSimulation) {
			if (!hadObject)
				CreateDataObject<StepSimulationData>(DataObjectType.StepSimulation);
		}
		else {
			if (hadObject)
				DestroyDataObject(DataObjectType.StepSimulation);
		}
	}

	public void PhysicsStepRunTimestep(TimeUnit_t timestep) {
		bool wasonground;
		bool inwater;
		float speed, newspeed, control;
		float friction;

		PhysicsCheckVelocity();

		wasonground = (GetFlags() & EntityFlags.OnGround) != 0;

		inwater = PhysicsCheckWater();

		bool isfalling = false;

		if (!wasonground) {
			if ((GetFlags() & EntityFlags.Fly) == 0) {
				if (!((GetFlags() & EntityFlags.Swim) != 0 && (GetWaterLevel() > 0))) {
					if (!inwater) {
						PhysicsAddHalfGravity(timestep);
						isfalling = true;
					}
				}
			}
		}

		if ((GetFlags() & EntityFlags.StepMovement) == 0 && (!MathLib.VectorCompare(GetAbsVelocity(), vec3_origin) || !MathLib.VectorCompare(GetBaseVelocity(), vec3_origin))) {
			Vector3 vecAbsVelocity = GetAbsVelocity();

			SetGroundEntity(null);

			if (wasonground) {
				speed = MathLib.VectorLength(vecAbsVelocity);
				if (speed != 0) {
					friction = sv_friction.GetFloat() * GetFriction();

					control = speed < sv_stopspeed.GetFloat() ? sv_stopspeed.GetFloat() : speed;
					newspeed = (float)(speed - timestep * control * friction);

					if (newspeed < 0)
						newspeed = 0;
					newspeed /= speed;

					vecAbsVelocity[0] *= newspeed;
					vecAbsVelocity[1] *= newspeed;
				}
			}

			vecAbsVelocity += GetBaseVelocity();
			SetAbsVelocity(vecAbsVelocity);

			SimulateAngles(timestep);

			PhysicsCheckVelocity();

			PhysicsTryMove(timestep, ref Unsafe.NullRef<Trace>());

			PhysicsCheckVelocity();

			vecAbsVelocity = GetAbsVelocity();
			vecAbsVelocity -= GetBaseVelocity();
			SetAbsVelocity(vecAbsVelocity);

			PhysicsCheckVelocity();

			if ((GetFlags() & EntityFlags.OnGround) == 0)
				PhysicsStepRecheckGround();

			PhysicsTouchTriggers();
		}

		if ((GetFlags() & EntityFlags.OnGround) == 0 && !isfalling)
			PhysicsAddHalfGravity(timestep);
	}

	private void PhysicsAddHalfGravity(double timestep) {
		float entGravity;

		if (GetGravity() != 0)
			entGravity = GetGravity();
		else
			entGravity = 1.0f;

		Vector3 vecAbsVelocity = GetAbsVelocity();
		vecAbsVelocity[2] -= (float)(0.5 * entGravity * GetCurrentGravity() * timestep);
		vecAbsVelocity[2] += (float)(GetBaseVelocity()[2] * gpGlobals.FrameTime);
		SetAbsVelocity(vecAbsVelocity);

		Vector3 vecNewBaseVelocity = GetBaseVelocity();
		vecNewBaseVelocity[2] = 0;
		SetBaseVelocity(vecNewBaseVelocity);

		PhysicsCheckVelocity();
	}

	public void PhysicsStepRecheckGround() {
		Mask mask = PhysicsSolidMaskForEntity();
		Vector3 mins, maxs, point = default;
		int x, y;
		Trace trace;

		mins = GetAbsOrigin() + WorldAlignMins();
		maxs = GetAbsOrigin() + WorldAlignMaxs();
		point[2] = mins[2] - 1;
		for (x = 0; x <= 1; x++) {
			for (y = 0; y <= 1; y++) {
				point[0] = x != 0 ? maxs[0] : mins[0];
				point[1] = y != 0 ? maxs[1] : mins[1];

				ICollideable? collision = GetCollideable();

				if (collision != null && IsNPC())
					Util.TraceLineFilterEntity(this, point, point, mask, Source.CollisionGroup.None, out trace);
				else
					Util.TraceLine(point, point, mask, this, Source.CollisionGroup.None, out trace);

				if (trace.StartSolid) {
					SetGroundEntity(trace.Ent);
					return;
				}
			}
		}
	}

	private int PhysicsTryMove(double flTime, ref Trace steptrace) {
		int bumpcount, numbumps;
		Vector3 dir;
		float d;
		int numplanes;
		Span<Vector3> planes = stackalloc Vector3[GameMovement.MAX_CLIP_PLANES];
		Vector3 primal_velocity, original_velocity, new_velocity;
		int i, j;
		Trace trace;
		Vector3 end;
		float time_left;
		int blocked;

		Mask mask = PhysicsSolidMaskForEntity();

		new_velocity = default;

		numbumps = 4;

		Vector3 vecAbsVelocity = GetAbsVelocity();

		blocked = 0;
		original_velocity = vecAbsVelocity;
		primal_velocity = vecAbsVelocity;
		numplanes = 0;

		time_left = (float)flTime;

		for (bumpcount = 0; bumpcount < numbumps; bumpcount++) {
			if (vecAbsVelocity == vec3_origin)
				break;

			MathLib.VectorMA(GetAbsOrigin(), time_left, vecAbsVelocity, out end);

			Physics.TraceEntity(this, GetAbsOrigin(), end, (uint)mask, out trace);

			if (trace.StartSolid) {
				SetAbsVelocity(vec3_origin);
				return 4;
			}

			if (trace.Fraction > 0) {
				SetAbsOrigin(trace.EndPos);
				original_velocity = vecAbsVelocity;
				numplanes = 0;
			}

			if (trace.Fraction == 1)
				break;

			if (trace.Ent == null) {
				SetAbsVelocity(vecAbsVelocity);
				Warning("PhysicsTryMove: !trace.u.ent");
				Assert(false);
				return 4;
			}

			if (trace.Plane.Normal[2] > 0.7f) {
				blocked |= 1;
				if (CanStandOn(trace.Ent)) {
					if (GetGroundEntity() != trace.Ent)
						SetGroundChangeTime((float)(gpGlobals.CurTime + (flTime - (1 - trace.Fraction) * time_left)));

					SetGroundEntity(trace.Ent);
				}
			}
			if (trace.Plane.Normal[2] == 0) {
				blocked |= 2;
				if (!Unsafe.IsNullRef(ref steptrace))
					steptrace = trace;
			}

			PhysicsImpact(trace.Ent, trace);
			if (IsMarkedForDeletion() || IsEdictFree())
				break;

			time_left -= time_left * trace.Fraction;

			if (numplanes >= GameMovement.MAX_CLIP_PLANES) {
				SetAbsVelocity(vec3_origin);
				return blocked;
			}

			planes[numplanes] = trace.Plane.Normal;
			numplanes++;

			if (GetMoveType() == Source.MoveType.Walk && ((GetFlags() & EntityFlags.OnGround) == 0 || GetFriction() != 1)) {
				for (i = 0; i < numplanes; i++) {
					if (planes[i][2] > 0.7f) {
						PhysicsClipVelocity(original_velocity, planes[i], out new_velocity, 1);
						original_velocity = new_velocity;
					}
					else
						PhysicsClipVelocity(original_velocity, planes[i], out new_velocity, 1.0f + sv_bounce.GetFloat() * (1 - GetFriction()));
				}

				vecAbsVelocity = new_velocity;
				original_velocity = new_velocity;
			}
			else {
				for (i = 0; i < numplanes; i++) {
					PhysicsClipVelocity(original_velocity, planes[i], out new_velocity, 1);
					for (j = 0; j < numplanes; j++)
						if (j != i) {
							if (Vector3.Dot(new_velocity, planes[j]) < 0)
								break;
						}
					if (j == numplanes)
						break;
				}

				if (i != numplanes)
					vecAbsVelocity = new_velocity;
				else {
					if (numplanes != 2) {
						SetAbsVelocity(vecAbsVelocity);
						return blocked;
					}
					dir = Vector3.Cross(planes[0], planes[1]);
					d = Vector3.Dot(dir, vecAbsVelocity);
					vecAbsVelocity = dir * d;
				}

				if (Vector3.Dot(vecAbsVelocity, primal_velocity) <= 0) {
					SetAbsVelocity(vec3_origin);
					return blocked;
				}
			}
		}

		SetAbsVelocity(vecAbsVelocity);
		return blocked;
	}
}

public static class Physics
{
	public static bool g_bTestMoveTypeStepSimulation = true;
	static readonly ConVar sv_teststepsimulation = new("1", 0);
	public readonly static ConVar npc_vphysics = new("0", 0);
	public static readonly PhysicsPushedEntities PushedEntities = new();

	const float PLAYER_PACKETS_STOPPED_SO_RETURN_TO_PHYSICS_TIME = 1.0f;

	public static void TraceEntity(BaseEntity entity, in Vector3 start, in Vector3 end, uint mask, out Trace tr) {
		if (entity.GetDamageType() != DamageType.Generic)
			g_pGameRules.WeaponTraceEntity(entity, start, end, (Mask)mask, out tr);
		else
			Util.TraceEntity(entity, start, end, (Mask)mask, out tr);
	}

	static void SimulateEntity(BaseEntity entity) {
		if (entity.Edict() != null) {
			if (entity.IsPlayerSimulated()) {
				BasePlayer? simulatingPlayer = entity.GetSimulatingPlayer();
				if (simulatingPlayer != null && (simulatingPlayer.GetTimeBase() > gpGlobals.CurTime - PLAYER_PACKETS_STOPPED_SO_RETURN_TO_PHYSICS_TIME))
					return;

				entity.UnsetPlayerSimulated();
			}

			if (entity.PredictableId.IsActive()) {
				if (entity.GetOwnerEntity() is BasePlayer playerowner) {
					BasePlayer? pl = Util.PlayerByIndex(entity.PredictableId.GetPlayer() + 1);
					if (pl == playerowner)
						if (pl.IsPredictingWeapons())
							IPredictionSystem.SuppressHostEvents(playerowner);
				}

				entity.PhysicsSimulate();

				IPredictionSystem.SuppressHostEvents(null);
			}
			else
				entity.PhysicsSimulate();
		}
		else
			entity.PhysicsRunThink();
	}

	public static void RunThinkFunctions(bool simulating) {
		g_bTestMoveTypeStepSimulation = sv_teststepsimulation.GetBool();

		TimeUnit_t startTime = gpGlobals.CurTime;

		gEntList.CleanupDeleteList();

		if (!simulating) {
			for (int i = 1; i <= gpGlobals.MaxClients; i++) {
				BasePlayer? player = Util.PlayerByIndex(i);
				if (player != null) {
					gpGlobals.CurTime = startTime;
					player.ForceSimulation();
					SimulateEntity(player);
				}
			}
		}
		else {
			int listMax = SimThinkManager.g_SimThinkManager.ListCount();
			listMax = Math.Max(listMax, 1);
			BaseEntity[] list = new BaseEntity[listMax];

			int count = SimThinkManager.g_SimThinkManager.ListCopy(list, listMax);

			for (int i = 0; i < count; i++) {
				if (list[i] == null)
					continue;

				gpGlobals.CurTime = startTime;
				SimulateEntity(list[i]);
				list[i].NetworkStateChanged();
			}

			Util.EnableRemoveImmediate();
		}

		gpGlobals.CurTime = startTime;
	}
}

public partial class BaseEntity
{
	void PhysicsStep() {
		// EVIL HACK: Force these to appear as if they've changed!!!
		// The underlying values don't actually change, but we need the network sendproxy on origin/angles
		//  to get triggered, and that only happens if NetworkStateChanged() appears to have occured.
		// Getting them for modify marks them as changed automagically.
		OriginForModify();
		RotationForModify();

		SetSimulationTime(gpGlobals.CurTime);

		PhysicsRunThink(ThinkMethods.FireAllButBase);

		long thinkTick = GetNextThinkTick();

		TimeUnit_t thinkTime = thinkTick * TICK_INTERVAL;
		TimeUnit_t deltaThink = thinkTime - gpGlobals.CurTime;

		if (thinkTime <= 0 || deltaThink > 0.5) {
			PhysicsStepRunTimestep(gpGlobals.FrameTime);
			// PhysicsCheckWaterTransition();
			SetLastThink(-1, gpGlobals.CurTime);
			UpdatePhysicsShadowToCurrentPosition(gpGlobals.FrameTime);
			PhysicsRelinkChildren(gpGlobals.FrameTime);
			return;
		}

		Vector3 oldOrigin = GetAbsOrigin();

		bool updateFromVPhysics = Physics.npc_vphysics.GetBool();
		if (HasDataObjectType(DataObjectType.VPhysicsUpdateAI)) {
			VPhysicsUpdateAI update = GetDataObject<VPhysicsUpdateAI>(DataObjectType.VPhysicsUpdateAI);
			if (update.StopUpdateTime > gpGlobals.CurTime)
				updateFromVPhysics = true;
			else {
				IPhysicsShadowController controller = VPhysicsGetObject()!.GetShadowController();
				controller.GetMaxSpeed(out _, out float maxAngular);
				controller.MaxSpeed(update.SavedShadowControllerMaxSpeed, maxAngular);
				DestroyDataObject(DataObjectType.VPhysicsUpdateAI);
			}
		}

		if (updateFromVPhysics && VPhysicsGetObject() != null && GetParent() == null) {
			VPhysicsGetObject()!.GetShadowPosition(out Vector3 position, out _);
			float delta = (GetAbsOrigin() - position).LengthSqr();
			if (delta < 1) {
				Physics.TraceEntity(this, GetAbsOrigin(), GetAbsOrigin(), (uint)PhysicsSolidMaskForEntity(), out Trace tr);
				updateFromVPhysics = tr.StartSolid;
			}

			if (updateFromVPhysics) {
				SetAbsOrigin(position);
				PhysicsTouchTriggers();
			}
		}

		if (thinkTick > gpGlobals.TickCount)
			return;

		if (thinkTime < gpGlobals.CurTime)
			thinkTime = gpGlobals.CurTime;

		TimeUnit_t dt = thinkTime - GetLastThink(null);

		StepSimulationThink(dt);

		// PhysicsCheckWaterTransition();

		if (VPhysicsGetObject() != null) {
			if (!MathLib.VectorCompare(oldOrigin, GetAbsOrigin()))
				VPhysicsGetObject()!.UpdateShadow(GetAbsOrigin(), vec3_angle, (GetFlags() & EntityFlags.Fly) != 0, (float)dt);
		}

		PhysicsRelinkChildren(dt);
	}

	BaseEntity? PhysicsPushMove(TimeUnit_t movetime) {
		IncrementLocalTime(movetime);

		if (GetLocalVelocity() == vec3_origin)
			return null;

		BaseEntity? blocker = Physics.PushedEntities.PerformLinearPush(this, movetime);
		if (blocker != null)
			IncrementLocalTime(-movetime);
		return blocker;
	}

	BaseEntity? PhysicsPushRotate(TimeUnit_t movetime) {
		IncrementLocalTime(movetime);

		if (GetLocalAngularVelocity() == vec3_angle)
			return null;

		BaseEntity? blocker = Physics.PushedEntities.PerformRotatePush(this, movetime);
		if (blocker != null)
			IncrementLocalTime(-movetime);

		return blocker;
	}

	void PhysicsPusher() {
		if (!PhysicsRunThink())
			return;

		VPhysicsUpdateLocalTime = LocalTime;

		TimeUnit_t movetime = GetMoveDoneTime();
		if (movetime > gpGlobals.FrameTime)
			movetime = gpGlobals.FrameTime;

		PerformPush(movetime);
	}

	void PhysicsNone() {
		PhysicsRunThink();
	}

	void PhysicsRigidChild() {
		Vector3 prevOrigin = GetAbsOrigin();

		if (!PhysicsRunThink())
			return;

		PhysicsTouchTriggers(prevOrigin);

		IPhysicsObject? physics = VPhysicsGetObject();
		if (physics != null) {
			SolidType solidType = GetSolid();
			bool axisAligned = solidType == SolidType.BBox || solidType == SolidType.None;
			physics.UpdateShadow(GetAbsOrigin(), axisAligned ? vec3_angle : GetAbsAngles(), true, (float)gpGlobals.FrameTime);
		}
	}

	void PhysicsNoclip() {
		if (!PhysicsRunThink()) {
			return;
		}

		SimulateAngles(gpGlobals.FrameTime);

		MathLib.VectorMA(GetLocalOrigin(), gpGlobals.FrameCount, Velocity, out Vector3 origin);
		SetLocalOrigin(origin);
	}

	void PhysicsToss() { }

	void PhysicsCustom() { }

	void PerformPush(TimeUnit_t movetime) {
		uint prevBlocker = Blocker.Index;
		BaseEntity? blocker;
		Physics.PushedEntities.BeginPush(this);
		if (movetime > 0) {
			if (GetLocalAngularVelocity() != vec3_angle) {
				if (GetLocalVelocity() != vec3_origin) {
					TimeUnit_t initialLocalTime = LocalTime;

					blocker = PhysicsPushRotate(movetime);
					if (blocker == null) {
						TimeUnit_t rotateLocalTime = LocalTime;

						LocalTime = initialLocalTime;
						blocker = PhysicsPushMove(movetime);
						if (LocalTime < rotateLocalTime)
							LocalTime = rotateLocalTime;
					}
				}
				else
					blocker = PhysicsPushRotate(movetime);
			}
			else
				blocker = PhysicsPushMove(movetime);

			Blocker.Set(blocker);
			if (Blocker.Index != prevBlocker) {
				if (prevBlocker != Source.Constants.INVALID_EHANDLE_INDEX)
					EndBlocked();
				if (Blocker.Get() != null)
					StartBlocked(blocker);
			}
			if (Blocker.Get() != null)
				Blocked(Blocker.Get());

			VPhysicsGetObject()?.Wake();
		}

		if (VPhysicsGetObject() != null) {
			if (movetime > 0 && Blocker.Get() == null && GetSolid() == SolidType.VPhysics && Physics.PushedEntities.CountMovedEntities() > 0)
				throw new NotImplementedException();
		}
		else {
			if (MoveDoneTime <= LocalTime && MoveDoneTime > 0) {
				SetMoveDoneTime(-1);
				MoveDone();
			}
		}
	}
	void StepSimulationThink(TimeUnit_t dt) {
		CheckStepSimulationChanged();

		ref StepSimulationData step = ref GetDataObject<StepSimulationData>(DataObjectType.StepSimulation);
		if (Unsafe.IsNullRef(ref step)) {
			PhysicsStepRunTimestep(dt);
			PhysicsRunThink(ThinkMethods.FireBaseOnly);
		}
		else {
			step.OriginActive = true;
			step.AnglesActive = true;

			step.LastProcessTickCount = -1;

			step.NetworkOrigin.Init();
			step.NetworkAngles.Init();

			step.Previous2 = step.Previous;

			step.Previous.TickCount = gpGlobals.TickCount;
			step.Previous.Origin = GetStepOrigin();
			QAngle stepAngles = GetStepAngles();
			MathLib.AngleQuaternion(stepAngles, out step.Previous.Rotation);

			PhysicsStepRunTimestep(dt);

			PhysicsRunThink(ThinkMethods.FireBaseOnly);

			if (GetBaseAnimating() != null)
				GetBaseAnimating()!.UpdateStepOrigin();

			step.Next.Origin = GetStepOrigin();
			stepAngles = GetStepAngles();
			MathLib.AngleQuaternion(stepAngles, out step.Next.Rotation);

			step.NextRotation = GetStepAngles();
			step.Next.TickCount = GetNextThinkTick();

			if (IsSimulatingOnAlternateTicks())
				++step.Next.TickCount;

			if (dt > 0) {
				Vector3 deltaOrigin = step.Next.Origin - step.Previous.Origin;
				float velSq = (float)(deltaOrigin.LengthSquared() / (dt * dt));
				if (velSq >= (4096.0f * 4096.0f) /*STEP_TELPORTATION_VEL_SQ*/)
					step.OriginActive = step.AnglesActive = false;
			}
		}
	}
}
