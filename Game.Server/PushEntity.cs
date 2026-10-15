using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Game.Server;

public class PhysicsPushedEntities
{
	protected struct RotatingPushMove
	{
		public Vector3 Origin;
		public Matrix3x4 StartLocalToWorld;
		public Matrix3x4 EndLocalToWorld;
		public QAngle AMove;
	}

	protected struct PhysicsPusherInfo
	{
		public BaseEntity Entity;
		public Vector3 StartAbsOrigin;
	}

	protected struct PhysicsPushedInfo
	{
		public BaseEntity Entity;
		public Vector3 StartAbsOrigin;
		public Trace Trace;
		public bool Blocked;
		public bool PusherIsGround;
	}

	protected readonly List<PhysicsPusherInfo> Pusher = new(8);
	protected readonly List<PhysicsPushedInfo> Moved = new(32);
	protected int BlockerIndex;
	protected bool IsUnblockableByPlayer;
	protected Vector3 RootPusherStartLocalOrigin;
	protected QAngle RootPusherStartLocalAngles;
	protected TimeUnit_t RootPusherStartLocaltime;
	protected TimeUnit_t MoveTime = -1.0;

	public int CountMovedEntities() => Moved.Count;

	public void StoreMovedEntities(PhysicsPushList list) {
		list.LocalMoveTime = RootPusherStartLocaltime;
		list.LocalOrigin = RootPusherStartLocalOrigin;
		list.LocalAngles = RootPusherStartLocalAngles;
		list.PushedCount = CountMovedEntities();
		Assert(list.PushedCount < PhysicsPushList.MAX_PUSHED_ENTITIES);
		if (list.PushedCount > PhysicsPushList.MAX_PUSHED_ENTITIES)
			list.PushedCount = PhysicsPushList.MAX_PUSHED_ENTITIES;

		for (int i = 0; i < list.PushedCount; i++) {
			EHANDLE handle = new();
			handle.Set(Moved[i].Entity);
			list.PushedEnts[i] = handle;
			list.PushVec[i] = Moved[i].Entity.GetAbsOrigin() - Moved[i].StartAbsOrigin;
		}
	}

	public void BeginPush(BaseEntity root) {
		Moved.Clear();
		Pusher.Clear();

		RootPusherStartLocalOrigin = root.GetLocalOrigin();
		RootPusherStartLocalAngles = root.GetLocalAngles();
		RootPusherStartLocaltime = root.GetLocalTime();
	}

	protected void AddEntity(BaseEntity ent) {
		Moved.Add(new PhysicsPushedInfo {
			Entity = ent,
			StartAbsOrigin = ent.GetAbsOrigin()
		});
	}

	protected void UnlinkPusherList(Span<SpatialTempHandle_t> pusherHandles) {
		for (int i = Pusher.Count; --i >= 0;)
			pusherHandles[i] = partition.HideElement(Pusher[i].Entity.CollisionProp().GetPartitionHandle());
	}

	protected void RelinkPusherList(Span<SpatialTempHandle_t> pusherHandles) {
		for (int i = Pusher.Count; --i >= 0;)
			partition.UnhideElement(Pusher[i].Entity.CollisionProp().GetPartitionHandle(), pusherHandles[i]);
	}

	protected void ComputeRotationalPushDirection(BaseEntity blocker, in RotatingPushMove rotPushMove, ref Vector3 move, BaseEntity root) {
		Vector3 start = blocker.CollisionProp().GetCollisionOrigin();
		if (root.GetSolid() == SolidType.VPhysics) {
			blocker.CollisionProp().WorldSpaceAABB(out Vector3 absMins, out Vector3 absMaxs);
			start.X = (move.X < 0) ? absMaxs.X : absMins.X;
			start.Y = (move.Y < 0) ? absMaxs.Y : absMins.Y;
			start.Z = (move.Z < 0) ? absMaxs.Z : absMins.Z;

			BasePlayer? player = ToBasePlayer(blocker);
			player?.SetPhysicsFlag(PlayerPhysFlag.GamePhysicsRotPush, true);
		}

		MathLib.VectorITransform(start, rotPushMove.StartLocalToWorld, out Vector3 local);
		MathLib.VectorTransform(local, rotPushMove.EndLocalToWorld, out Vector3 end);

		move = end - start;
	}

	protected bool IsPushedPositionValid(BaseEntity blocker) {
		TraceFilterPushFinal pushFilter = new(blocker, blocker.GetCollisionGroup());

		Util.TraceEntity(blocker, blocker.GetAbsOrigin(), blocker.GetAbsOrigin(), blocker.PhysicsSolidMaskForEntity(), ref pushFilter, out Trace trace);

		return !trace.StartSolid;
	}

	protected bool SpeculativelyCheckPush(ref PhysicsPushedInfo info, in Vector3 absPush, bool rotationalPush) {
		BaseEntity blocker = info.Entity;

		Span<SpatialTempHandle_t> pusherHandles = stackalloc SpatialTempHandle_t[Pusher.Count];
		UnlinkPusherList(pusherHandles);
		TraceFilterPushMove pushFilter = new(blocker, blocker.GetCollisionGroup());

		Vector3 pushDestPosition = blocker.GetAbsOrigin() + absPush;
		Util.TraceEntity(blocker, blocker.GetAbsOrigin(), pushDestPosition, blocker.PhysicsSolidMaskForEntity(), ref pushFilter, out info.Trace);

		RelinkPusherList(pusherHandles);
		info.PusherIsGround = false;
		if (blocker.GetGroundEntity() != null && blocker.GetGroundEntity()!.GetRootMoveParent() == Pusher[0].Entity)
			info.PusherIsGround = true;

		bool isUnblockable = IsUnblockableByPlayer && (blocker.IsPlayer() || blocker.MyNPCPointer() != null);
		if (isUnblockable)
			blocker.SetAbsOrigin(pushDestPosition);
		else {
			if (info.Trace.Fraction != 0)
				blocker.SetAbsOrigin(info.Trace.EndPos);

			if (blocker.IsPointSized() || !blocker.IsSolid() || blocker.IsSolidFlagSet(SolidFlags.VolumeContents))
				return true;

			if (!rotationalPush && info.Trace.Fraction == 1.0f) {
				if (!IsPushedPositionValid(blocker))
					Warning($"Interpenetrating entities! ({blocker.GetClassname()} and {Pusher[0].Entity.GetClassname()})\n");

				return true;
			}
		}

		info.Blocked = !IsPushedPositionValid(blocker);

		if (!info.Blocked)
			return true;

		if (isUnblockable) {
			Vector3 org = blocker.GetAbsOrigin();
			for (int checkCount = 0; checkCount < 4; checkCount++) {
				MathLib.MatrixGetColumn(Pusher[0].Entity.EntityToWorldTransform(), checkCount >> 1, out Vector3 move);

				float factor = (checkCount & 1) != 0 ? -0.5f : 0.5f;
				blocker.SetAbsOrigin(org + move * factor);
				info.Blocked = !IsPushedPositionValid(blocker);
				if (!info.Blocked)
					return true;
			}
			blocker.SetAbsOrigin(pushDestPosition);

			DevMsg(1, "Ignoring player blocking train!\n");
			return true;
		}
		return false;
	}

	protected virtual bool SpeculativelyCheckRotPush(in RotatingPushMove rotPushMove, BaseEntity root) {
		Vector3 absPush = default;
		BlockerIndex = -1;
		Span<PhysicsPushedInfo> moved = CollectionsMarshal.AsSpan(Moved);
		for (int i = moved.Length; --i >= 0;) {
			ComputeRotationalPushDirection(moved[i].Entity, rotPushMove, ref absPush, root);
			if (!SpeculativelyCheckPush(ref moved[i], absPush, true)) {
				BlockerIndex = i;
				return false;
			}
		}

		return true;
	}

	protected virtual bool SpeculativelyCheckLinearPush(in Vector3 absPush) {
		BlockerIndex = -1;
		Span<PhysicsPushedInfo> moved = CollectionsMarshal.AsSpan(Moved);
		for (int i = moved.Length; --i >= 0;) {
			if (!SpeculativelyCheckPush(ref moved[i], absPush, false)) {
				BlockerIndex = i;
				return false;
			}
		}

		return true;
	}

	protected void FinishPushers() {
		for (int i = Pusher.Count; --i >= 0;) {
			PhysicsPusherInfo info = Pusher[i];

			info.Entity.PhysicsTouchTriggers(info.StartAbsOrigin);

			info.Entity.UpdatePhysicsShadowToCurrentPosition(gpGlobals.FrameTime);
		}
	}

	protected virtual void FinishRotPushedEntity(BaseEntity pushedEntity, in RotatingPushMove rotPushMove) {
		if (pushedEntity.IsPlayer()) {
			QAngle angVel = pushedEntity.GetLocalAngularVelocity();
			angVel[1] = rotPushMove.AMove[1];
			pushedEntity.SetLocalAngularVelocity(angVel);

			BasePlayer player = (BasePlayer)pushedEntity;
			player.pl.FixAngle = (int)FixAngle.Relative;
			player.pl.AngleChange += rotPushMove.AMove;
		}
		else {
			QAngle angles = pushedEntity.GetAbsAngles();

			angles.Y += rotPushMove.AMove.Y;
			pushedEntity.SetAbsAngles(angles);
		}
	}

	protected void FinishPush(bool isRotPush = false, in RotatingPushMove rotPushMove = default) {
		FinishPushers();

		for (int i = Moved.Count; --i >= 0;) {
			PhysicsPushedInfo info = Moved[i];
			BaseEntity pushedEntity = info.Entity;

			info.Entity.PhysicsTouchTriggers(info.StartAbsOrigin);
			info.Entity.UpdatePhysicsShadowToCurrentPosition(gpGlobals.FrameTime);
			AI_BaseNPC? npc = info.Entity.MyNPCPointer();
			if (info.PusherIsGround && npc != null)
				npc.NotifyPushMove();

			if (info.Trace.Ent != null)
				pushedEntity.PhysicsImpact(info.Trace.Ent, info.Trace);

			if (isRotPush)
				FinishRotPushedEntity(pushedEntity, rotPushMove);
		}
	}

	protected BaseEntity RegisterBlockage() {
		Assert(BlockerIndex >= 0);

		PhysicsPushedInfo info = Moved[BlockerIndex];
		if (info.Trace.Ent != null)
			info.Entity.PhysicsImpact(info.Trace.Ent, info.Trace);

		return info.Entity;
	}

	protected void RestoreEntities() {
		for (int i = Moved.Count; --i >= 0;)
			Moved[i].Entity.SetAbsOrigin(Moved[i].StartAbsOrigin);
	}

	protected void GenerateBlockingEntityList() {
		Moved.Clear();
		PushBlockerEnum blockerEnum = new(this);

		for (int i = Pusher.Count; --i >= 0;) {
			BaseEntity pusher = Pusher[i].Entity;

			if (!pusher.IsSolid() || pusher.IsSolidFlagSet(SolidFlags.VolumeContents))
				continue;

			pusher.CollisionProp().WorldSpaceAABB(out Vector3 absMins, out Vector3 absMaxs);
			partition.EnumerateElementsInBox((SpatialPartitionListMask_t)PartitionListMask.EngineNonStaticEdicts, absMins, absMaxs, false, ref blockerEnum);
		}
	}

	protected void GenerateBlockingEntityListAddBox(in Vector3 moved) {
		Moved.Clear();
		PushBlockerEnum blockerEnum = new(this);

		for (int i = Pusher.Count; --i >= 0;) {
			BaseEntity pusher = Pusher[i].Entity;

			if (!pusher.IsSolid() || pusher.IsSolidFlagSet(SolidFlags.VolumeContents))
				continue;

			pusher.CollisionProp().WorldSpaceAABB(out Vector3 absMins, out Vector3 absMaxs);
			for (int axis = 0; axis < 3; ++axis) {
				if (moved[axis] >= 0.0f)
					absMins[axis] -= moved[axis];
				else
					absMaxs[axis] -= moved[axis];
			}

			partition.EnumerateElementsInBox((SpatialPartitionListMask_t)PartitionListMask.EngineNonStaticEdicts, absMins, absMaxs, false, ref blockerEnum);
		}
	}

	protected void SetupAllInHierarchy(BaseEntity? parent) {
		if (parent == null)
			return;

		Pusher.Add(new PhysicsPusherInfo {
			Entity = parent,
			StartAbsOrigin = parent.GetAbsOrigin()
		});

		for (BaseEntity? child = parent.FirstMoveChild(); child != null; child = child.NextMovePeer())
			SetupAllInHierarchy(child);
	}

	protected void RotateRootEntity(BaseEntity root, TimeUnit_t movetime, out RotatingPushMove rotation) {
		rotation = default;
		rotation.AMove = root.GetLocalAngularVelocity() * (float)movetime;
		rotation.Origin = root.GetAbsOrigin();

		MathLib.MatrixCopy(root.EntityToWorldTransform(), out rotation.StartLocalToWorld);

		QAngle angles = root.GetLocalAngles();
		angles += root.GetLocalAngularVelocity() * (float)movetime;

		root.SetLocalAngles(angles);

		MathLib.MatrixCopy(root.EntityToWorldTransform(), out rotation.EndLocalToWorld);
	}

	public BaseEntity? PerformRotatePush(BaseEntity root, TimeUnit_t movetime) {
		IsUnblockableByPlayer = (root.GetFlags() & EntityFlags.UnblockableByPlayer) != 0;
		Pusher.Clear();
		SetupAllInHierarchy(root);

		QAngle prevAngles = root.GetLocalAngles();

		RotateRootEntity(root, movetime, out RotatingPushMove rotPushMove);

		GenerateBlockingEntityList();

		if (!SpeculativelyCheckRotPush(rotPushMove, root)) {
			BaseEntity blocker = RegisterBlockage();
			root.SetLocalAngles(prevAngles);
			RestoreEntities();
			return blocker;
		}

		FinishPush(true, rotPushMove);
		return null;
	}

	protected void LinearlyMoveRootEntity(BaseEntity root, TimeUnit_t movetime, out Vector3 absPushVector) {
		Vector3 move = root.GetLocalVelocity() * (float)movetime;
		Vector3 origin = root.GetLocalOrigin();
		origin += move;
		root.SetLocalOrigin(origin);

		absPushVector = root.GetAbsVelocity() * (float)movetime;
	}

	public BaseEntity? PerformLinearPush(BaseEntity root, TimeUnit_t movetime) {
		MoveTime = movetime;

		IsUnblockableByPlayer = (root.GetFlags() & EntityFlags.UnblockableByPlayer) != 0;
		Pusher.Clear();
		SetupAllInHierarchy(root);

		Vector3 prevOrigin = root.GetLocalOrigin();

		LinearlyMoveRootEntity(root, movetime, out Vector3 absPush);

		GenerateBlockingEntityListAddBox(absPush);

		if (!SpeculativelyCheckLinearPush(absPush)) {
			BaseEntity blocker = RegisterBlockage();
			root.SetLocalOrigin(prevOrigin);
			RestoreEntities();
			return blocker;
		}

		FinishPush();
		return null;
	}

	struct TraceFilterPushFinal(BaseEntity entity, CollisionGroup collisionGroup) : ITraceFilter
	{
		TraceFilterSimple Inner = new(entity, collisionGroup);

		public bool ShouldHitEntity(IHandleEntity handleEntity, Contents contentsMask) {
			Assert(handleEntity is BaseEntity);
			BaseEntity testEntity = (BaseEntity)handleEntity;

			if (testEntity.GetMoveType() == MoveType.VPhysics && testEntity.VPhysicsGetObject() != null && testEntity.VPhysicsGetObject()!.IsMoveable())
				return false;

			return Inner.ShouldHitEntity(handleEntity, contentsMask);
		}
	}

	struct TraceFilterAgainstEntityList() : ITraceFilter
	{
		readonly List<IHandleEntity> EntityList = [];

		public readonly bool ShouldHitEntity(IHandleEntity entity, Contents contentsMask) {
			for (int i = EntityList.Count - 1; i >= 0; --i) {
				if (EntityList[i] == entity)
					return true;
			}

			return false;
		}

		public readonly TraceType GetTraceType() => TraceType.EntitiesOnly;

		public readonly void AddEntityToHit(IHandleEntity entity) => EntityList.Add(entity);
	}

	struct PushBlockerEnum : IPartitionEnumerator
	{
		static int EnumCount = 0;
		readonly PhysicsPushedEntities PushedEntities;
		readonly BaseEntity RootHighestParent;
		TraceFilterAgainstEntityList PushersOnly = new();
		InlineArray8<CollisionGroup> CollisionGroups;
		int CollisionGroupCount;

		public PushBlockerEnum(PhysicsPushedEntities pushedEntities) {
			PushedEntities = pushedEntities;
			RootHighestParent = PushedEntities.Pusher[0].Entity.GetRootMoveParent();
			++EnumCount;

			CollisionGroupCount = 0;
			for (int i = PushedEntities.Pusher.Count; --i >= 0;) {
				if (!PushedEntities.Pusher[i].Entity.IsSolid())
					continue;

				PushersOnly.AddEntityToHit(PushedEntities.Pusher[i].Entity);
				CollisionGroup collisionGroup = PushedEntities.Pusher[i].Entity.GetCollisionGroup();
				AddCollisionGroup(collisionGroup);
			}
		}

		public IterationRetval EnumElement(IHandleEntity? handleEntity) {
			BaseEntity? check = GetPushableEntity(handleEntity!);
			if (check == null)
				return IterationRetval.Continue;

			check.PushEnumCount = EnumCount;
			PushedEntities.AddEntity(check);

			return IterationRetval.Continue;
		}

		void AddCollisionGroup(CollisionGroup collisionGroup) {
			for (int i = 0; i < CollisionGroupCount; i++) {
				if (CollisionGroups[i] == collisionGroup)
					return;
			}
			if (CollisionGroupCount < 8) {
				CollisionGroups[CollisionGroupCount] = collisionGroup;
				CollisionGroupCount++;
			}
		}

		readonly bool IsStandingOnPusher(BaseEntity check) {
			BaseEntity? groundEnt = check.GetGroundEntity();
			if ((check.GetFlags() & EntityFlags.OnGround) != 0 || groundEnt != null) {
				for (int i = PushedEntities.Pusher.Count; --i >= 0;) {
					if (PushedEntities.Pusher[i].Entity == groundEnt)
						return true;
				}
			}
			return false;
		}

		bool IntersectsPushers(BaseEntity test) {
			Trace tr = default;

			ICollideable collision = test.GetCollideable()!;
			enginetrace.SweepCollideable(collision, test.GetAbsOrigin(), test.GetAbsOrigin(), collision.GetCollisionAngles(), test.PhysicsSolidMaskForEntity(), ref PushersOnly, ref tr);

			return tr.StartSolid;
		}

		BaseEntity? GetPushableEntity(IHandleEntity handleEntity) {
			BaseEntity? check = gEntList.GetBaseEntity(handleEntity.GetRefEHandle());
			if (check == null)
				return null;

			if (check.PushEnumCount == EnumCount)
				return null;

			if (!check.IsSolid())
				return null;

			if (check.GetMoveType() == MoveType.Push ||
				check.GetMoveType() == MoveType.None ||
				check.GetMoveType() == MoveType.VPhysics ||
				check.GetMoveType() == MoveType.Noclip)
				return null;

			bool collide = false;
			for (int i = 0; i < CollisionGroupCount; i++) {
				if (g_pGameRules.ShouldCollide(check.GetCollisionGroup(), CollisionGroups[i])) {
					collide = true;
					break;
				}
			}
			if (!collide)
				return null;

			BaseEntity checkHighestParent = check.GetRootMoveParent();
			if (checkHighestParent == RootHighestParent)
				return null;

			if (!IsStandingOnPusher(check)) {
				if (!IntersectsPushers(check))
					return null;
			}

			return checkHighestParent;
		}
	}
}

public struct TraceFilterPushMove(BaseEntity entity, CollisionGroup collisionGroup) : ITraceFilter
{
	TraceFilterSimple Inner = new(entity, collisionGroup);
	readonly BaseEntity RootParent = entity.GetRootMoveParent();

	public bool ShouldHitEntity(IHandleEntity handleEntity, Contents contentsMask) {
		Assert(handleEntity is BaseEntity);
		BaseEntity testEntity = (BaseEntity)handleEntity;

		if (EntityHasMatchingRootParent(RootParent, testEntity))
			return false;

		if (testEntity.GetMoveType() == MoveType.VPhysics && testEntity.VPhysicsGetObject() != null && testEntity.VPhysicsGetObject()!.IsMoveable())
			return false;

		return Inner.ShouldHitEntity(handleEntity, contentsMask);
	}
}
