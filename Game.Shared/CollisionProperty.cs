#if CLIENT_DLL || GAME_DLL

#if CLIENT_DLL
using Game.Client;
#endif

#if GAME_DLL
using Game.Server;
#endif

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;
using Source.Engine;

using System.Numerics;
using System.Security.AccessControl;
using System.Reflection.Metadata.Ecma335;

using DEFINE = Source.DEFINE<Game.Shared.CollisionProperty>;
using FIELD = Source.FIELD<Game.Shared.CollisionProperty>;

namespace Game.Shared;

public enum SurroundingBoundsType
{
	UseOBBCollisionBounds = 0,
	UseBestCollisionBounds,
	UseHitboxes,
	UseSpecifiedBounds,
	UseGameCode,
	UseRotationExpandedBounds,
	UseCollisionBoundsNeverVPhysics,

	BitCount = 3
}

public class DirtySpatialPartitionEntityList() : AutoGameSystem("DirtySpatialPartitionEntityList"), IPartitionQueryCallback
{
	public static readonly DirtySpatialPartitionEntityList s_DirtyKDTree = new();

	public static void UpdateDirtySpatialPartitionEntities() {
		SpatialPartitionListMask_t listMask = (int)
#if CLIENT_DLL
		PartitionListMask.ClientGameEdicts;
#else
		PartitionListMask.ServerGameEdicts;
#endif

		s_DirtyKDTree.OnPreQuery(listMask);
		s_DirtyKDTree.OnPostQuery(listMask);
	}

	public override bool Init() {
		partition.InstallQueryCallback(this);
		return true;
	}
	public override void Shutdown() {
		partition.RemoveQueryCallback(this);
	}
	public override void LevelShutdownPostEntity() {
		DirtyEntities.Clear();
	}
	public virtual void OnPreQuery_V1() {
		Assert(false);
	}
	public virtual void OnPreQuery(SpatialPartitionListMask_t listMask) {
#if CLIENT_DLL
		const int validMask = (int)PartitionListMask.ClientGameEdicts;
#else
		const int validMask = (int)PartitionListMask.ServerGameEdicts;
#endif

		if ((listMask & validMask) == 0)
			return;

		if (PartitionWriteId != 0 && PartitionWriteId == ThreadGetCurrentId())
			return;

#if CLIENT_DLL
		// FIXME: This should really be an assertion... feh!
		if (!C_BaseEntity.IsAbsRecomputationsEnabled()) {
			LockPartitionForRead();
			return;
		}
#endif

		// if you're holding a read lock, then these are entities that were still dirty after your trace started
		// or became dirty due to some other thread or callback. Updating them may cause corruption further up the
		// stack (e.g. partition iterator).  Ignoring the state change should be safe since it happened after the 
		// trace was requested or was unable to be resolved in a previous attempt (still dirty).
		if (DirtyEntities.Count != 0 && ReadLockCount.Value == 0) {
			List<BaseHandle> vecStillDirty = ListPool<BaseHandle>.Shared.Alloc();
			PartitionMutex.AcquireWriterLock(100000);
			PartitionWriteId = (uint)ThreadGetCurrentId();
			while (DirtyEntities.TryDequeue(out BaseHandle handle)) {
#if !CLIENT_DLL
				BaseEntity? entity = gEntList.GetBaseEntity(handle);
#else
				BaseEntity? entity = cl_entitylist.GetBaseEntityFromHandle(handle);
#endif

				if (entity != null) {
					// If an entity is in the middle of bone setup, don't call UpdatePartition
					//  which can cause it to redo bone setup on the same frame causing a recursive
					//  call to bone setup.
					if (!entity.IsEFlagSet(EFL.SettingUpBones))
						entity.CollisionProp().UpdatePartition();
					else
						vecStillDirty.Add(handle);
				}
			}
			if (vecStillDirty.Count() > 0)
				for (int i = 0; i < vecStillDirty.Count(); i++)
					DirtyEntities.Enqueue(vecStillDirty[i]);


			PartitionWriteId = 0;
			PartitionMutex.ReleaseWriterLock();

			ListPool<BaseHandle>.Shared.Free(vecStillDirty);
		}
		LockPartitionForRead();
	}
	public virtual void OnPostQuery(SpatialPartitionListMask_t listMask) {
#if CLIENT_DLL
		if ((listMask & (int)PartitionListMask.ClientGameEdicts) == 0)
			return;
#elif GAME_DLL
		if ((listMask & (int)PartitionListMask.ServerGameEdicts) == 0)
			return;
#endif

		if (PartitionWriteId != 0)
			return;

		UnlockPartitionForRead();
	}
	public void AddEntity(BaseEntity entity) {
		DirtyEntities.Enqueue(entity.GetRefEHandle());
	}

	public void LockPartitionForRead() {
		if (ReadLockCount.Value == 0)
			PartitionMutex.AcquireReaderLock(100000);
		++ReadLockCount.Value;
	}

	public void UnlockPartitionForRead() {
		--ReadLockCount.Value;
		if (ReadLockCount.Value == 0)
			PartitionMutex.ReleaseReaderLock();
	}

	readonly Queue<BaseHandle> DirtyEntities = [];
	readonly ReaderWriterLock PartitionMutex = new();
	uint PartitionWriteId;
	readonly ThreadLocal<int> ReadLockCount = new();
}

public partial class CollisionProperty : ICollideable
{
	[NetworkTable("DT_CollisionProperty")]
	public static readonly NetworkProp[] Props = [
		new(nameof(MinsPreScaled)) { Bits = 0, Flags = PropFlags.NoScale, Proxy = typeof(OBBMinsPreScaledProxy) },
		new(nameof(MaxsPreScaled)) { Bits = 0, Flags = PropFlags.NoScale, Proxy = typeof(OBBMaxPreScaledProxy) },
		new(nameof(Mins)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Maxs)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SolidType)) { Bits = 3, Flags = PropFlags.Unsigned, Proxy = typeof(SolidTypeProxy) },
		new(nameof(SolidFlags)) { Bits = (int)Source.SolidFlags.MaxBits, Flags = PropFlags.Unsigned, Proxy = typeof(SolidFlagsProxy) },
		new(nameof(SurroundType)) { Bits = (int)SurroundingBoundsType.BitCount, Flags = PropFlags.Unsigned, Proxy = typeof(IntDirtySurroundProxy) },
		new(nameof(TriggerBloat)) { Bits = 0, Flags = PropFlags.Unsigned, Proxy = typeof(IntDirtySurroundProxy) },
		new(nameof(SpecifiedSurroundingMinsPreScaled)) { Bits = 0, Flags = PropFlags.NoScale, Proxy = typeof(VectorDirtySurroundProxy) },
		new(nameof(SpecifiedSurroundingMaxsPreScaled)) { Bits = 0, Flags = PropFlags.NoScale, Proxy = typeof(VectorDirtySurroundProxy) },
		new(nameof(SpecifiedSurroundingMins)) { Bits = 0, Flags = PropFlags.NoScale, Proxy = typeof(VectorDirtySurroundProxy) },
		new(nameof(SpecifiedSurroundingMaxs)) { Bits = 0, Flags = PropFlags.NoScale, Proxy = typeof(VectorDirtySurroundProxy) },
	];

	[PredictionTable]
	public static readonly PredictionField[] Prediction = [
		nameof(MinsPreScaled),
		nameof(MaxsPreScaled),
		nameof(Mins),
		nameof(Maxs),
		nameof(SolidType),
		nameof(SolidFlags),
		nameof(TriggerBloat),
	];

	public static class OBBMinsPreScaledProxy
	{
#if CLIENT_DLL
		public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field) => RecvProxy_OBBMinsPreScaled(in data, instance, field);
#endif
	}

	public static class OBBMaxPreScaledProxy
	{
#if CLIENT_DLL
		public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field) => RecvProxy_OBBMaxPreScaled(in data, instance, field);
#endif
	}

	public static class SolidTypeProxy
	{
#if CLIENT_DLL
		public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field) => RecvProxy_Solid(in data, instance, field);
#else
		public static void Send(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) => SendProxy_Solid(prop, instance, field, ref outData, element, objectID);
#endif
	}

	public static class SolidFlagsProxy
	{
#if CLIENT_DLL
		public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field) => RecvProxy_SolidFlags(in data, instance, field);
#else
		public static void Send(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) => SendProxy_SolidFlags(prop, instance, field, ref outData, element, objectID);
#endif
	}

	public static class IntDirtySurroundProxy
	{
#if CLIENT_DLL
		public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field) => RecvProxy_IntDirtySurround(in data, instance, field);
#endif
	}

	public static class VectorDirtySurroundProxy
	{
#if CLIENT_DLL
		public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field) => RecvProxy_VectorDirtySurround(in data, instance, field);
#endif
	}

#if CLIENT_DLL
	private static void RecvProxy_VectorDirtySurround(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		Vector3 vecold = field.GetValue<Vector3>(instance);
		Vector3 vecnew = data.Value.Vector;
		if (vecold != vecnew) {
			field.SetValue(instance, in vecnew);
			((CollisionProperty)instance)!.MarkSurroundingBoundsDirty();
		}
	}


	private static void RecvProxy_SolidFlags(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		field.SetValue(instance, data.Value.Int);
	}

	private static void RecvProxy_Solid(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		field.SetValue(instance, data.Value.Int);
	}

	private static void RecvProxy_OBBMinsPreScaled(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		CollisionProperty prop = (CollisionProperty)instance;
		Vector3 vecMins = data.Value.Vector;
		prop.SetCollisionBounds(vecMins, prop.OBBMaxsPreScaled());
	}

	private static void RecvProxy_OBBMaxPreScaled(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		CollisionProperty prop = (CollisionProperty)instance;
		Vector3 vecMaxs = data.Value.Vector;
		prop.SetCollisionBounds(prop.OBBMinsPreScaled(), vecMaxs);
	}

	private static void RecvProxy_IntDirtySurround(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		if (field.GetValue<byte>(instance) != (byte)data.Value.Int) {
			field.SetValue<int>(instance, data.Value.Int);
			((CollisionProperty)instance).MarkSurroundingBoundsDirty();
		}
	}
#else
	public static readonly DataMap DataDesc = new(typeof(CollisionProperty), [
		//		DEFINE_FIELD( m_pOuter, FIELD_CLASSPTR ),
		DEFINE.GLOBAL_FIELD(nameof(MinsPreScaled), FieldType.Vector),
		DEFINE.GLOBAL_FIELD(nameof(MaxsPreScaled), FieldType.Vector),
		DEFINE.GLOBAL_FIELD(nameof(Mins), FieldType.Vector),
		DEFINE.GLOBAL_FIELD(nameof(Maxs), FieldType.Vector),
		DEFINE.KEYFIELD(nameof(SolidType), FieldType.Character, "solid"),
		DEFINE.FIELD(nameof(SolidFlags), FieldType.Short),
		DEFINE.FIELD(nameof(SurroundType), FieldType.Character),
		DEFINE.FIELD(nameof(Radius), FieldType.Float),
		DEFINE.FIELD(nameof(TriggerBloat), FieldType.Character),
		DEFINE.FIELD(nameof(SpecifiedSurroundingMinsPreScaled), FieldType.Vector),
		DEFINE.FIELD(nameof(SpecifiedSurroundingMaxsPreScaled), FieldType.Vector),
		DEFINE.FIELD(nameof(SpecifiedSurroundingMins), FieldType.Vector),
		DEFINE.FIELD(nameof(SpecifiedSurroundingMaxs), FieldType.Vector),
		DEFINE.FIELD(nameof(SurroundingMins), FieldType.Vector),
		DEFINE.FIELD(nameof(SurroundingMaxs), FieldType.Vector),
		//		DEFINE_FIELD( m_Partition, FIELD_SHORT ),
		//		DEFINE_PHYSPTR( m_pPhysicsObject ),
	]);

	private static void SendProxy_SolidFlags(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		outData.Int = ((CollisionProperty)(instance)).SolidFlags;
	}

	private static void SendProxy_Solid(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		outData.Int = ((CollisionProperty)(instance)).SolidType;
	}
#endif

#if !CLIENT_DLL
	public void NetworkStateChanged() => Outer?.NetworkStateChanged();
	public void NetworkStateChanged(IFieldAccessor field) => Outer?.NetworkStateChanged();
#endif

	[NetworkVar("m_vecMinsPreScaled")]
	public partial ref readonly Vector3 MinsPreScaled { get; }
	[NetworkVar("m_vecMaxsPreScaled")]
	public partial ref readonly Vector3 MaxsPreScaled { get; }
	[NetworkVar("m_vecMins")]
	public partial ref readonly Vector3 Mins { get; }
	[NetworkVar("m_vecMaxs")]
	public partial ref readonly Vector3 Maxs { get; }
	public Vector3 Size;
	float Radius;
	[NetworkVar("m_nSolidType")]
	public partial byte SolidType { get; set; }
	[NetworkVar("m_usSolidFlags")]
	public partial ushort SolidFlags { get; set; }
	SpatialPartitionHandle_t Partition = PARTITION_INVALID_HANDLE;
	[NetworkVar("m_nSurroundType")]
	partial byte SurroundType { get; set; }

	[NetworkVar("m_triggerBloat")]
	public partial byte TriggerBloat { get; set; }
	Vector3 SurroundingMins;
	Vector3 SurroundingMaxs;
	[NetworkVar("m_vecSpecifiedSurroundingMinsPreScaled")]
	partial ref readonly Vector3 SpecifiedSurroundingMinsPreScaled { get; }
	[NetworkVar("m_vecSpecifiedSurroundingMaxsPreScaled")]
	partial ref readonly Vector3 SpecifiedSurroundingMaxsPreScaled { get; }
	[NetworkVar("m_vecSpecifiedSurroundingMins")]
	partial ref readonly Vector3 SpecifiedSurroundingMins { get; }
	[NetworkVar("m_vecSpecifiedSurroundingMaxs")]
	partial ref readonly Vector3 SpecifiedSurroundingMaxs { get; }

	public void UseTriggerBounds(bool enable, float bloat) {
		Assert(bloat <= 127.0f);
		TriggerBloat = (byte)bloat;
		if (enable) {
			AddSolidFlags(Source.SolidFlags.UseTriggerBounds);
			Assert(bloat > 0.0f);
		}
		else
			RemoveSolidFlags(Source.SolidFlags.UseTriggerBounds);
	}

	public void CheckForUntouch() {
#if !CLIENT_DLL
		if (!IsSolid() && !IsSolidFlagSet(Source.SolidFlags.Trigger)) {
			// If this ent's touch list isn't empty, it's transitioning to not solid
			if (GetOuter().IsCurrentlyTouching()) {
				// mark ent so that at the end of frame it will check to 
				// see if it's no longer touching ents
				GetOuter().SetCheckUntouch(true);
			}
		}
#endif
	}

	public void SetSolid(SolidType val) {
		if ((byte)val == SolidType)
			return;

#if !CLIENT_DLL
		bool wasNotSolid = IsSolid();
#endif

		MarkSurroundingBoundsDirty();

		// OBB is not yet implemented
		if (val == Source.SolidType.BSP) {
			if (GetOuter().GetMoveParent() != null) {
				if (GetOuter().GetRootMoveParent()!.GetSolid() != Source.SolidType.BSP) {
					// must be SOLID_VPHYSICS because parent might rotate
					val = Source.SolidType.VPhysics;
				}
			}
#if !CLIENT_DLL
			// UNDONE: This should be fine in the client DLL too.  Move GetAllChildren() into shared code.
			// If the root of the hierarchy is SOLID_BSP, then assume that the designer
			// wants the collisions to rotate with this hierarchy so that the player can
			// move while riding the hierarchy.
			if (GetOuter().GetMoveParent() == null) {
				// NOTE: This assumes things don't change back from SOLID_BSP
				// NOTE: This is 100% true for HL2 - need to support removing the flag to support changing from SOLID_BSP
				List<BaseEntity> list = [];
				Hierarchy.GetAllChildren(GetOuter(), list);
				for (int i = list.Count - 1; i >= 0; --i)
					list[i].AddSolidFlags(Source.SolidFlags.RootParentAligned);
			}
#endif
		}

		SolidType = (byte)val;

#if !CLIENT_DLL
		GetOuter().CollisionRulesChanged();

		UpdateServerPartitionMask();

		if (wasNotSolid != IsSolid())
			CheckForUntouch();
#endif
	}

	public bool IsBoundsDefinedInEntitySpace() => (((Source.SolidFlags)SolidFlags) & Source.SolidFlags.ForceWorldAligned) == 0 || (SolidType != (byte)Source.SolidType.BBox) && (SolidType != (byte)Source.SolidType.None);

	public bool DoesRotationInvalidateSurroundingBox() {
		if (IsSolidFlagSet(Source.SolidFlags.RootParentAligned))
			return true;
		switch ((SurroundingBoundsType)SurroundType) {
			case SurroundingBoundsType.UseCollisionBoundsNeverVPhysics:
			case SurroundingBoundsType.UseOBBCollisionBounds:
			case SurroundingBoundsType.UseBestCollisionBounds:
				return IsBoundsDefinedInEntitySpace();
			case SurroundingBoundsType.UseHitboxes:
			case SurroundingBoundsType.UseGameCode:
				return true;
			case SurroundingBoundsType.UseRotationExpandedBounds:
			case SurroundingBoundsType.UseSpecifiedBounds:
				return false;
			default:
				Assert(false);
				return true;
		}
	}


	void ComputeVPhysicsSurroundingBox(out Vector3 vecWorldMins, out Vector3 vecWorldMaxs) {
		bool setBounds = false;
		vecWorldMins = default;
		vecWorldMaxs = default;
		IPhysicsObject? physicsObject = GetOuter().VPhysicsGetObject();
		if (physicsObject != null) {
			if (physicsObject.GetCollide() != null) {
				physcollision.CollideGetAABB(out vecWorldMins, out vecWorldMaxs,
					physicsObject.GetCollide(), GetCollisionOrigin(), GetCollisionAngles());
				setBounds = true;
			}
			else if (physicsObject.GetSphereRadius() != 0) {
				float radius = physicsObject.GetSphereRadius();
				Vector3 extents = new(radius, radius, radius);
				MathLib.VectorSubtract(in GetCollisionOrigin(), in extents, out vecWorldMins);
				MathLib.VectorAdd(in GetCollisionOrigin(), in extents, out vecWorldMaxs);
				setBounds = true;
			}
		}

		if (!setBounds) {
			vecWorldMins = GetCollisionOrigin();
			vecWorldMaxs = vecWorldMins;
		}

		if (IsSolidFlagSet(Source.SolidFlags.UseTriggerBounds)) {
			WorldSpaceTriggerBounds(out Vector3 vecWorldTriggerMins, out Vector3 vecWorldTriggerMaxs);
			MathLib.VectorMin(in vecWorldTriggerMins, in vecWorldMins, out vecWorldMins);
			MathLib.VectorMax(in vecWorldTriggerMaxs, in vecWorldMaxs, out vecWorldMaxs);
		}
	}

	bool ComputeHitboxSurroundingBox(out Vector3 vecWorldMins, out Vector3 vecWorldMaxs) {
		BaseAnimating? anim = GetOuter().GetBaseAnimating();
		if (anim != null)
			return anim.ComputeHitboxSurroundingBox(out vecWorldMins, out vecWorldMaxs);

		vecWorldMins = default;
		vecWorldMaxs = default;
		return false;
	}

	void ComputeRotationExpandedBounds(out Vector3 vecWorldMins, out Vector3 vecWorldMaxs) {
		if (!IsBoundsDefinedInEntitySpace()) {
			vecWorldMins = Mins;
			vecWorldMaxs = Maxs;
		}
		else {
			vecWorldMins = default;
			vecWorldMaxs = default;

			float maxVal;
			maxVal = Math.Max(FloatMakePositive(Mins.X), FloatMakePositive(Maxs.X));
			vecWorldMins.X = -maxVal;
			vecWorldMaxs.X = maxVal;

			maxVal = Math.Max(FloatMakePositive(Mins.Y), FloatMakePositive(Maxs.Y));
			vecWorldMins.Y = -maxVal;
			vecWorldMaxs.Y = maxVal;

			maxVal = Math.Max(FloatMakePositive(Mins.Z), FloatMakePositive(Maxs.Z));
			vecWorldMins.Z = -maxVal;
			vecWorldMaxs.Z = maxVal;
		}
	}

	void ComputeCollisionSurroundingBox(bool useVPhysics, out Vector3 vecWorldMins, out Vector3 vecWorldMaxs) {
		Assert(GetSolid() != Source.SolidType.Custom);

		if (useVPhysics)
			ComputeVPhysicsSurroundingBox(out vecWorldMins, out vecWorldMaxs);
		else
			WorldSpaceTriggerBounds(out vecWorldMins, out vecWorldMaxs);
	}

	void ComputeSurroundingBox(out Vector3 vecWorldMins, out Vector3 vecWorldMaxs) {
		if ((GetSolid() == Source.SolidType.Custom) && ((SurroundingBoundsType)SurroundType != SurroundingBoundsType.UseGameCode)) {
			vecWorldMins = GetCollisionOrigin();
			vecWorldMaxs = vecWorldMins;
			return;
		}

		switch ((SurroundingBoundsType)SurroundType) {
			case SurroundingBoundsType.UseOBBCollisionBounds: {
					Assert(GetSolid() != Source.SolidType.Custom);
					bool useVPhysics = false;
					if ((GetSolid() == Source.SolidType.VPhysics) && (GetOuter().GetMoveType() == MoveType.VPhysics)) {
						IPhysicsObject? physics = GetOuter().VPhysicsGetObject();
						useVPhysics = physics != null && physics.IsAsleep();
					}
					ComputeCollisionSurroundingBox(useVPhysics, out vecWorldMins, out vecWorldMaxs);
				}
				break;

			case SurroundingBoundsType.UseBestCollisionBounds:
				Assert(GetSolid() != Source.SolidType.Custom);
				ComputeCollisionSurroundingBox(GetSolid() == Source.SolidType.VPhysics, out vecWorldMins, out vecWorldMaxs);
				break;

			case SurroundingBoundsType.UseCollisionBoundsNeverVPhysics:
				Assert(GetSolid() != Source.SolidType.Custom);
				ComputeCollisionSurroundingBox(false, out vecWorldMins, out vecWorldMaxs);
				break;

			case SurroundingBoundsType.UseHitboxes:
				ComputeHitboxSurroundingBox(out vecWorldMins, out vecWorldMaxs);
				break;

			case SurroundingBoundsType.UseRotationExpandedBounds:
				ComputeRotationExpandedBounds(out vecWorldMins, out vecWorldMaxs);
				break;

			case SurroundingBoundsType.UseSpecifiedBounds:
				MathLib.VectorAdd(in GetCollisionOrigin(), in SpecifiedSurroundingMins, out vecWorldMins);
				MathLib.VectorAdd(in GetCollisionOrigin(), in SpecifiedSurroundingMaxs, out vecWorldMaxs);
				break;

			case SurroundingBoundsType.UseGameCode:
				GetOuter().ComputeWorldSpaceSurroundingBox(out vecWorldMins, out vecWorldMaxs);
				Assert(vecWorldMins.X <= vecWorldMaxs.X);
				Assert(vecWorldMins.Y <= vecWorldMaxs.Y);
				Assert(vecWorldMins.Z <= vecWorldMaxs.Z);
				return;

			default:
				vecWorldMins = default;
				vecWorldMaxs = default;
				break;
		}
	}

	public void MarkSurroundingBoundsDirty() {
		GetOuter().AddEFlags(EFL.DirtySurroundingCollisionBounds);
		MarkPartitionHandleDirty();

#if CLIENT_DLL
		g_ClientShadowMgr.MarkRenderToTextureShadowDirty(GetOuter().GetShadowHandle());
#else
		GetOuter().NetworkProp().MarkPVSInformationDirty();
#endif
	}

	public IHandleEntity? GetEntityHandle() => Outer;

	public ref readonly Vector3 OBBMinsPreScaled() => ref MinsPreScaled;

	public ref readonly Vector3 OBBMaxsPreScaled() => ref MaxsPreScaled;

	public ref readonly Vector3 OBBMins() => ref Mins;
	public ref readonly Vector3 OBBMaxs() => ref Maxs;

	//-----------------------------------------------------------------------------
	// Returns the center in OBB space
	//-----------------------------------------------------------------------------
	public Vector3 OBBCenter() {
		MathLib.VectorLerp(Mins, Maxs, 0.5f, out Vector3 vecResult);
		return vecResult;
	}

	//-----------------------------------------------------------------------------
	// center point of entity
	//-----------------------------------------------------------------------------
	public Vector3 WorldSpaceCenter() => CollisionToWorldSpace(OBBCenter());
	public ref readonly Vector3 OBBSize(){
		MathLib.VectorSubtract(Maxs, Mins, out Size);
		return ref Size;
	}

	public void SetCollisionBounds(in Vector3 mins, in Vector3 maxs) {
		if (MinsPreScaled != mins || MaxsPreScaled != maxs) {
			MinsPreScaledForModify() = mins;
			MaxsPreScaledForModify() = maxs;
		}

		bool dirty = false;

		float scale = (Outer is BaseAnimating anim) ? anim.GetModelScale() : 1.0f;
		if (scale != 1.0f) {
			Vector3 newMins = mins * scale;
			Vector3 newMaxs = maxs * scale;
			if (Mins != newMins || Maxs != newMaxs) {
				MinsForModify() = newMins;
				MaxsForModify() = newMaxs;
				dirty = true;
			}
		}
		else {
			if (Mins != mins || Maxs != maxs) {
				MinsForModify() = mins;
				MaxsForModify() = maxs;
				dirty = true;
			}
		}

		if (dirty) {
			Vector3 size = Maxs - Mins;
			Radius = size.Length() * 0.5f;
			MarkSurroundingBoundsDirty();
		}
	}

	public void WorldSpaceTriggerBounds(out Vector3 vecWorldMins, out Vector3 vecWorldMaxs) {
		WorldSpaceAABB(out vecWorldMins, out vecWorldMaxs);
		if ((GetSolidFlags() & (int)Source.SolidFlags.UseTriggerBounds) == 0)
			return;

		// Don't bloat below, we don't want to trigger it with our heads
		vecWorldMins.X -= TriggerBloat;
		vecWorldMins.Y -= TriggerBloat;

		vecWorldMaxs.X += TriggerBloat;
		vecWorldMaxs.Y += TriggerBloat;
		vecWorldMaxs.Z += (float)TriggerBloat * 0.5f;
	}

	//-----------------------------------------------------------------------------
	// Collision methods implemented in the entity
	// FIXME: This shouldn't happen there!!
	//-----------------------------------------------------------------------------
	public bool TestCollision(in Ray ray, Contents contentsMask, ref Trace tr) {
		return Outer.TestCollision(ray, contentsMask, ref tr);
	}

	public bool TestHitboxes(in Ray ray, Contents contentsMask, ref Trace tr) {
		return Outer.TestHitboxes(ray, contentsMask, ref tr);
	}

	public int GetCollisionModelIndex() {
		return Outer.GetModelIndex();
	}

	public Model? GetCollisionModel() {
		return Outer.GetModel();
	}

	public ref readonly Vector3 GetCollisionOrigin() => ref Outer.GetAbsOrigin();

	static readonly QAngle s_vec3_angle = new(0, 0, 0);
	public ref readonly QAngle GetCollisionAngles() {
		if (IsBoundsDefinedInEntitySpace())
			return ref Outer.GetAbsAngles();

		return ref s_vec3_angle;
	}

	Matrix3x4 CollisionToWorldTransformResult;
	public ref readonly Matrix3x4 CollisionToWorldTransform() {
		if (IsBoundsDefinedInEntitySpace())
			return ref Outer.EntityToWorldTransform();

		MathLib.SetIdentityMatrix(out CollisionToWorldTransformResult);
		MathLib.MatrixSetColumn(in GetCollisionOrigin(), 3, ref CollisionToWorldTransformResult);
		return ref CollisionToWorldTransformResult;
	}

	public void CollisionAABBToWorldAABB(in Vector3 entityMins, in Vector3 entityMaxs, out Vector3 worldMins, out Vector3 worldMaxs) {
		if (!IsBoundsDefinedInEntitySpace() || (GetCollisionAngles() == s_vec3_angle)) {
			MathLib.VectorAdd(in entityMins, in GetCollisionOrigin(), out worldMins);
			MathLib.VectorAdd(in entityMaxs, in GetCollisionOrigin(), out worldMaxs);
		}
		else
			MathLib.TransformAABB(in CollisionToWorldTransform(), in entityMins, in entityMaxs, out worldMins, out worldMaxs);
	}

	public void WorldSpaceAABB(out Vector3 worldMins, out Vector3 worldMaxs) => CollisionAABBToWorldAABB(in Mins, in Maxs, out worldMins, out worldMaxs);

	public SolidType GetSolid() => (SolidType)SolidType;

	public int GetSolidFlags() => SolidFlags;

	//-----------------------------------------------------------------------------
	// IClientUnknown
	//-----------------------------------------------------------------------------
	public IClientUnknown? GetIClientUnknown() {
#if CLIENT_DLL
		return Outer.GetIClientUnknown();
#else
		return null;
#endif
	}

	public CollisionGroup GetCollisionGroup() {
		return Outer.GetCollisionGroup();
	}

	public void WorldSpaceSurroundingBounds(out Vector3 vecMins, out Vector3 vecMaxs) {
		ref readonly Vector3 absOrigin = ref GetCollisionOrigin();
		if (GetOuter().IsEFlagSet(EFL.DirtySurroundingCollisionBounds)) {
			GetOuter().RemoveEFlags(EFL.DirtySurroundingCollisionBounds);
			ComputeSurroundingBox(out vecMins, out vecMaxs);
			MathLib.VectorSubtract(in vecMins, in absOrigin, out SurroundingMins);
			MathLib.VectorSubtract(in vecMaxs, in absOrigin, out SurroundingMaxs);
		}
		else {
			MathLib.VectorAdd(in SurroundingMins, in absOrigin, out vecMins);
			MathLib.VectorAdd(in SurroundingMaxs, in absOrigin, out vecMaxs);
		}
	}

	//-----------------------------------------------------------------------------
	// Transforms a point in OBB space to world space
	//-----------------------------------------------------------------------------
	public Vector3 CollisionToWorldSpace(in Vector3 input) {
		Vector3 result;
		// Makes sure we don't re-use the same temp twice
		if (!IsBoundsDefinedInEntitySpace() || (GetCollisionAngles() == vec3_angle))
			MathLib.VectorAdd(input, GetCollisionOrigin(), out result);
		else
			MathLib.VectorTransform(input, CollisionToWorldTransform(), out result);
		return result;
	}

	//-----------------------------------------------------------------------------
	// Transforms a point in world space to OBB space
	//-----------------------------------------------------------------------------
	public Vector3 NormalizedToCollisionSpace(in Vector3 input) {
		return new(MathLib.Lerp(input.X, Mins.X, Maxs.X),
			MathLib.Lerp(input.Y, Mins.Y, Maxs.Y),
			MathLib.Lerp(input.Z, Mins.Z, Maxs.Z));
	}

	public Vector3 NormalizedToWorldSpace(in Vector3 input) => CollisionToWorldSpace(NormalizedToCollisionSpace(input));

	public bool IsPointInBounds(in Vector3 worldPt) {
		Vector3 localSpace = WorldToCollisionSpace(worldPt);
		return (localSpace.X >= Mins.X && localSpace.X <= Maxs.X) &&
			(localSpace.Y >= Mins.Y && localSpace.Y <= Maxs.Y) &&
			(localSpace.Z >= Mins.Z && localSpace.Z <= Maxs.Z);
	}

	public Vector3 WorldToCollisionSpace(in Vector3 input) {
		Vector3 result;
		if (!IsBoundsDefinedInEntitySpace() || (GetCollisionAngles() == vec3_angle))
			MathLib.VectorSubtract(input, GetCollisionOrigin(), out result);
		else
			MathLib.VectorITransform(input, CollisionToWorldTransform(), out result);
		return result;
	}

	public void CalcNearestPoint(in Vector3 vecWorldPt, out Vector3 vecNearestWorldPt) {
		// Calculate physics force
		Vector3 localPt = WorldToCollisionSpace(vecWorldPt);
		MathLib.CalcClosestPointOnAABB(Mins, Maxs, localPt, out Vector3 localClosestPt);
		vecNearestWorldPt = CollisionToWorldSpace(localClosestPt);
	}

	public float CalcDistanceFromPoint(in Vector3 vecWorldPt) {
		// Calculate physics force
		Vector3 localPt = WorldToCollisionSpace(vecWorldPt);
		MathLib.CalcClosestPointOnAABB(Mins, Maxs, localPt, out Vector3 localClosestPt);
		return Vector3.Distance(localPt, localClosestPt);
	}

	public bool ShouldTouchTrigger(int triggerSolidFlags) {
		// debris only touches certain triggers
		if (GetCollisionGroup() == Source.CollisionGroup.Debris) {
			if ((triggerSolidFlags & (int)Source.SolidFlags.TriggerTouchDebris) != 0)
				return true;

			return false;
		}

		// triggers don't touch other triggers (might be solid to other ents as well as trigger)
		if (IsSolidFlagSet(Source.SolidFlags.Trigger))
			return false;

		return true;
	}

	public ref readonly Matrix3x4 GetRootParentToWorldTransform() {
		if (IsSolidFlagSet(Source.SolidFlags.RootParentAligned)) {
			BaseEntity? entity = Outer.GetRootMoveParent();
			Assert(entity != null);
			if (entity != null)
				return ref entity.CollisionProp().CollisionToWorldTransform();
		}
		return ref System.Runtime.CompilerServices.Unsafe.NullRef<Matrix3x4>();
	}

	internal void SetSolidFlags(SolidFlags flags) {
		int oldFlags = SolidFlags;
		SolidFlags = (ushort)((int)flags & 0xFFFF);
		if (oldFlags == SolidFlags)
			return;

		// These two flags, if changed, can produce different surrounding bounds
		if ((oldFlags & (int)(Source.SolidFlags.ForceWorldAligned | Source.SolidFlags.UseTriggerBounds)) !=
			 (SolidFlags & (int)(Source.SolidFlags.ForceWorldAligned | Source.SolidFlags.UseTriggerBounds)))
			MarkSurroundingBoundsDirty();

		if ((oldFlags & (int)(Source.SolidFlags.NotSolid | Source.SolidFlags.Trigger)) != (SolidFlags & (int)(Source.SolidFlags.NotSolid | Source.SolidFlags.Trigger)))
			GetOuter().CollisionRulesChanged();

#if !CLIENT_DLL
		if ((oldFlags & (int)(Source.SolidFlags.NotSolid | Source.SolidFlags.Trigger)) != (SolidFlags & (int)(Source.SolidFlags.NotSolid | Source.SolidFlags.Trigger))) {
			UpdateServerPartitionMask();
			CheckForUntouch();
		}
#endif
	}

	internal bool IsSolidFlagSet(SolidFlags flagMask) => (SolidFlags & (ushort)flagMask) != 0;

	internal void RemoveSolidFlags(SolidFlags flags) => SetSolidFlags((SolidFlags)(SolidFlags & ~(int)flags));

	internal bool IsSolid() => Constants.IsSolid((SolidType)SolidType, SolidFlags);

	internal void AddSolidFlags(SolidFlags flags) => SetSolidFlags((SolidFlags)SolidFlags | flags);
	internal void ClearSolidFlags() => SetSolidFlags(0);


	public void CreatePartitionHandle() {
		Assert(Partition == PARTITION_INVALID_HANDLE);
		Partition = partition.CreateHandle(GetEntityHandle());
	}
	public void DestroyPartitionHandle() {
		if (Partition != PARTITION_INVALID_HANDLE) {
			partition.DestroyHandle(Partition);
			Partition = PARTITION_INVALID_HANDLE;
		}
	}
	public ushort GetPartitionHandle() => Partition;
	public void MarkPartitionHandleDirty() {
		if (Outer.EntIndex() == 0)
			return;

		if (!Outer.IsEFlagSet(EFL.DirtySpatialPartition)) {
			Outer.AddEFlags(EFL.DirtySpatialPartition);
			DirtySpatialPartitionEntityList.s_DirtyKDTree.AddEntity(Outer);
		}

#if CLIENT_DLL
		GetOuter().MarkRenderHandleDirty();
		g_ClientShadowMgr.AddToDirtyShadowList(GetOuter());
#endif
	}
	public void UpdateServerPartitionMask() {
#if !CLIENT_DLL
		SpatialPartitionHandle_t handle = GetPartitionHandle();
		if (handle == PARTITION_INVALID_HANDLE)
			return;

		// Remove it from whatever lists it may be in at the moment
		// We'll re-add it below if we need to.
		partition.Remove(handle);

		// Don't bother with deleted things
		if (Outer.Edict() == null)
			return;

		// don't add the world
		if (Outer.EntIndex() == 0)
			return;

		// Make sure it's in the list of all entities
		bool bIsSolid = IsSolid() || IsSolidFlagSet(Source.SolidFlags.Trigger);
		if (bIsSolid || Outer.IsEFlagSet(EFL.UsePartitionWhenNotSolid))
			partition.Insert(PartitionListMask.EngineNonStaticEdicts, handle);

		if (!bIsSolid)
			return;

		// Insert it into the appropriate lists.
		// We have to continually reinsert it because its solid type may have changed
		PartitionListMask mask = 0;
		if (!IsSolidFlagSet(Source.SolidFlags.NotSolid))
			mask |= PartitionListMask.EngineSolidEdicts;

		if (IsSolidFlagSet(Source.SolidFlags.Trigger))
			mask |= PartitionListMask.EngineTriggerEdicts;

		Assert(mask != 0);
		partition.Insert(mask, handle);
#endif
	}
	public float BoundingRadius() => Radius;
	internal void UpdatePartition() {
		if (Outer.IsEFlagSet(EFL.DirtySpatialPartition)) {
			Outer.RemoveEFlags(EFL.DirtySpatialPartition);

#if !CLIENT_DLL
			Assert(Outer.EntIndex() != 0);

			// Don't bother with deleted things
			if (Outer.Edict() == null)
				return;

			if (GetPartitionHandle() == PARTITION_INVALID_HANDLE) {
				CreatePartitionHandle();
				UpdateServerPartitionMask();
			}
#else
			if (GetPartitionHandle() == PARTITION_INVALID_HANDLE)
				return;
#endif

			// We don't need to bother if it's not a trigger or solid
			if (IsSolid() || IsSolidFlagSet(Source.SolidFlags.Trigger) || Outer.IsEFlagSet(EFL.UsePartitionWhenNotSolid)) {
				// Bloat a little bit...
				if (BoundingRadius() != 0.0f) {
					WorldSpaceSurroundingBounds(out Vector3 vecSurroundMins, out Vector3 vecSurroundMaxs);
					vecSurroundMins -= new Vector3(1, 1, 1);
					vecSurroundMaxs += new Vector3(1, 1, 1);
					partition.ElementMoved(GetPartitionHandle(), vecSurroundMins, vecSurroundMaxs);
				}
				else {
					partition.ElementMoved(GetPartitionHandle(), GetCollisionOrigin(), GetCollisionOrigin());
				}
			}
		}
	}

	public void Init(BaseEntity entity) {
		Outer = entity;

		MinsPreScaledForModify() = default;
		MaxsPreScaledForModify() = default;
		MinsForModify() = default;
		MaxsForModify() = default;
		Radius = 0.0f;
		TriggerBloat = 0;
		SolidFlags = 0;
		SolidType = (int)Source.SolidType.None;

		SurroundType = (int)SurroundingBoundsType.UseOBBCollisionBounds;
		SurroundingMins = vec3_origin;
		SurroundingMaxs = vec3_origin;
		SpecifiedSurroundingMinsPreScaledForModify() = default;
		SpecifiedSurroundingMaxsPreScaledForModify() = default;
		SpecifiedSurroundingMinsForModify() = default;
		SpecifiedSurroundingMaxsForModify() = default;
	}

	BaseEntity Outer = null!;
	public BaseEntity GetOuter() => Outer;
}
#endif
