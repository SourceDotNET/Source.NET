#if CLIENT_DLL || GAME_DLL

global using static Game.Shared.DataObjectAccessSystemGlobals;

using Game.Shared;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Game.Shared
{
}

// Define physics methods for base entity
#if CLIENT_DLL
namespace Game.Client
#else
namespace Game.Server
#endif
{
	public partial class
#if CLIENT_DLL
		C_BaseEntity
#else
		BaseEntity
#endif
	{
		public BaseEntity? GetGroundEntity() => GroundEntity.Get();

		static BaseEntity? LinkEntity(in BaseHandle handle) => (BaseEntity?)handle.Get();
		static string DebugName(BaseEntity? ent) => ent == null ? "" : new(ent.GetDebugName());

		public TouchLink? GetTouchLinkRoot() {
			ref TouchLink root = ref GetDataObject<TouchLink>(DataObjectType.TouchLink);
			return Unsafe.IsNullRef(ref root) ? null : root;
		}

		public GroundLink? GetGroundLinkRoot() {
			ref GroundLink root = ref GetDataObject<GroundLink>(DataObjectType.GroundLink);
			return Unsafe.IsNullRef(ref root) ? null : root;
		}

		public static readonly ConVar debug_touchlinks = new("debug_touchlinks", "0", 0, "Spew touch link activity");
		static bool DebugTouchlinks() => debug_touchlinks.GetBool();

		static TouchLink? g_pNextLink = null;

		static void FreeTouchLink(TouchLink? link) {
			if (link != null) {
				if (link == g_pNextLink)
					g_pNextLink = link.NextLink;
				link.PrevLink = link.NextLink = null!;
			}
		}

		//-----------------------------------------------------------------------------
		// Purpose:
		// Output : Returns true on success, false on failure.
		//-----------------------------------------------------------------------------
		public bool IsCurrentlyTouching() {
			if (HasDataObjectType(DataObjectType.TouchLink))
				return true;

			return false;
		}

		static bool g_bCleanupDatObject = true;

		//-----------------------------------------------------------------------------
		// Purpose: Checks to see if any entities that have been touching this one
		//			have stopped touching it, and notify the entity if so.
		//			Called at the end of a frame, after all the entities have run
		//-----------------------------------------------------------------------------
		public void PhysicsCheckForEntityUntouch() {
			Assert(g_pNextLink == null);

			TouchLink? link;

			TouchLink? root = GetTouchLinkRoot();
			if (root != null) {
				bool saveCleanup = g_bCleanupDatObject;
				g_bCleanupDatObject = false;

				link = root.NextLink;
				while (link != root) {
					g_pNextLink = link!.NextLink;

					// these touchlinks are not polled.  The ents are touching due to an outside
					// system that will add/delete them as necessary (vphysics in this case)
					if (link.TouchStamp == TouchLink.TOUCHSTAMP_EVENT_DRIVEN) {
						// refresh the touch call
						PhysicsTouch(LinkEntity(link.EntityTouched));
					}
					else {
						// check to see if the touch stamp is up to date
						if (link.TouchStamp != TouchStamp) {
							// stamp is out of data, so entities are no longer touching
							// remove self from other entities touch list
							PhysicsNotifyOtherOfUntouch(this, LinkEntity(link.EntityTouched));

							// remove other entity from this list
							PhysicsRemoveToucher(this, link);
						}
					}

					link = g_pNextLink;
				}

				g_bCleanupDatObject = saveCleanup;

				// Nothing left in list, destroy root
				if (root.NextLink == root &&
					 root.PrevLink == root)
					DestroyDataObject(DataObjectType.TouchLink);
			}

			g_pNextLink = null;

			SetCheckUntouch(false);
		}

		//-----------------------------------------------------------------------------
		// Purpose: notifies an entity than another touching entity has moved out of contact.
		// Input  : *other - the entity to be acted upon
		//-----------------------------------------------------------------------------
		public static void PhysicsNotifyOtherOfUntouch(BaseEntity ent, BaseEntity? other) {
			if (other == null)
				return;

			// loop through ed's touch list, looking for the notifier
			// remove and call untouch if found
			TouchLink? root = other.GetTouchLinkRoot();
			if (root != null) {
				TouchLink link = root.NextLink;
				while (link != root) {
					if (LinkEntity(link.EntityTouched) == ent) {
						PhysicsRemoveToucher(other, link);

						// Check for complete removal
						if (g_bCleanupDatObject &&
							 root.NextLink == root &&
							 root.PrevLink == root)
							other.DestroyDataObject(DataObjectType.TouchLink);
						return;
					}

					link = link.NextLink;
				}
			}
		}

		//-----------------------------------------------------------------------------
		// Purpose: removes a toucher from the list
		// Input  : *link - the link to remove
		//-----------------------------------------------------------------------------
		public static void PhysicsRemoveToucher(BaseEntity? otherEntity, TouchLink link) {
			// Every start Touch gets a corresponding end touch
			BaseEntity? entityTouched = LinkEntity(link.EntityTouched);
			if ((link.Flags & TouchLinkFlags.StartTouch) != 0 &&
				entityTouched != null &&
				otherEntity != null)
				otherEntity.EndTouch(entityTouched);

			link.NextLink.PrevLink = link.PrevLink;
			link.PrevLink.NextLink = link.NextLink;

			if (DebugTouchlinks())
				Msg($"remove: {DebugName(entityTouched)}-{DebugName(otherEntity)} ({entityTouched?.EntIndex()}-{otherEntity?.EntIndex()})\n");
			FreeTouchLink(link);
		}

		//-----------------------------------------------------------------------------
		// Purpose: Clears all touches from the list
		//-----------------------------------------------------------------------------
		public static void PhysicsRemoveTouchedList(BaseEntity ent) {
			TouchLink? link, nextLink;

			TouchLink? root = ent.GetTouchLinkRoot();
			if (root != null) {
				link = root.NextLink;
				bool saveCleanup = g_bCleanupDatObject;
				g_bCleanupDatObject = false;
				while (link != null && link != root) {
					nextLink = link.NextLink;

					// notify the other entity that this ent has gone away
					PhysicsNotifyOtherOfUntouch(ent, LinkEntity(link.EntityTouched));

					// kill it
					if (DebugTouchlinks())
						Msg($"remove: {DebugName(ent)}-{DebugName(LinkEntity(link.EntityTouched))} ({ent.EntIndex()}-{LinkEntity(link.EntityTouched)?.EntIndex()})\n");
					FreeTouchLink(link);
					link = nextLink;
				}

				g_bCleanupDatObject = saveCleanup;
				ent.DestroyDataObject(DataObjectType.TouchLink);
			}

			ent.TouchStamp = 0;
		}

		public bool HasDataObjectType(DataObjectType type) => (DataObjectTypes & (1 << (int)type)) != 0;
		public void AddDataObjectType(DataObjectType type) => DataObjectTypes |= (1 << (int)type);
		public void RemoveDataObjectType(DataObjectType type) => DataObjectTypes &= ~(1 << (int)type);

		public ref T GetDataObject<T>(DataObjectType type) where T : new() {
			if (!HasDataObjectType(type))
				return ref Unsafe.NullRef<T>();
			return ref g_DataObjectAccessSystem.GetDataObject<T>(type, this);
		}

		public ref T CreateDataObject<T>(DataObjectType type) where T : new() {
			AddDataObjectType(type);
			return ref g_DataObjectAccessSystem.CreateDataObject<T>(type, this);
		}

		public void DestroyDataObject(DataObjectType type) {
			if (!HasDataObjectType(type))
				return;
			RemoveDataObjectType(type);
			g_DataObjectAccessSystem.DestroyDataObject(type, this);
		}

		public void DestroyAllDataObjects() {
			for (DataObjectType i = 0; i < DataObjectType.NumTypes; i++)
				if (HasDataObjectType(i))
					DestroyDataObject(i);
		}
		static Trace g_TouchTrace;

		public void PhysicsImpact(BaseEntity? other, in Trace trace) {
			if (other == null)
				return;
			// If either of the entities is flagged to be deleted, 
			//  don't call the touch functions
			if (((GetFlags() | other.GetFlags()) & Source.EntityFlags.KillMe) != 0)
				return;

			PhysicsMarkEntitiesAsTouching(other, trace);
		}

		//-----------------------------------------------------------------------------
		// Purpose: Called every frame that two entities are touching
		// Input  : *pentOther - the entity who it has touched
		//-----------------------------------------------------------------------------
		public void PhysicsTouch(BaseEntity? other) {
			if (other != null) {
				if (!(IsMarkedForDeletion() || other.IsMarkedForDeletion()))
					Touch(other);
			}
		}

		//-----------------------------------------------------------------------------
		// Purpose: Called whenever two entities come in contact
		// Input  : *pentOther - the entity who it has touched
		//-----------------------------------------------------------------------------
		public void PhysicsStartTouch(BaseEntity? other) {
			if (other != null) {
				if (!(IsMarkedForDeletion() || other.IsMarkedForDeletion())) {
					StartTouch(other);
					Touch(other);
				}
			}
		}

		//-----------------------------------------------------------------------------
		// Purpose: Marks in an entity that it is touching another entity, and calls
		//			it's Touch() function if it is a new touch.
		//			Stamps the touch link with the new time so that when we check for
		//			untouch we know things haven't changed.
		// Input  : *other - entity that it is in contact with
		//-----------------------------------------------------------------------------
		public TouchLink? PhysicsMarkEntityAsTouched(BaseEntity other) {
			TouchLink? link;

			if (this == other)
				return null;

			// Entities in hierarchy should not interact
			if ((GetMoveParent() == other) || (this == other.GetMoveParent()))
				return null;

			// check if either entity doesn't generate touch functions
			if (((GetFlags() | other.GetFlags()) & Source.EntityFlags.DontTouch) != 0)
				return null;

			// Pure triggers should not touch each other
			if (IsSolidFlagSet(Source.SolidFlags.Trigger) && other.IsSolidFlagSet(Source.SolidFlags.Trigger)) {
				if (!IsSolid() && !other.IsSolid())
					return null;
			}

			// Don't do touching if marked for deletion
			if (other.IsMarkedForDeletion())
				return null;

			if (IsMarkedForDeletion())
				return null;

			// check if the edict is already in the list
			TouchLink? root = GetTouchLinkRoot();
			if (root != null) {
				for (link = root.NextLink; link != root; link = link.NextLink) {
					if (LinkEntity(link.EntityTouched) == other) {
						// update stamp
						link.TouchStamp = TouchStamp;

						if (!sm_bDisableTouchFuncs)
							PhysicsTouch(other);

						// no more to do
						return link;
					}
				}
			}
			else {
				// Allocate the root object
				root = CreateDataObject<TouchLink>(DataObjectType.TouchLink);
				root.NextLink = root.PrevLink = root;
			}

			// entity is not in list, so it's a new touch
			// add it to the touched list and then call the touch function

			// build new link
			link = new TouchLink();
			if (DebugTouchlinks())
				Msg($"add: {GetDebugName()}-{other.GetDebugName()} ({EntIndex()}-{other.EntIndex()})\n");

			link.TouchStamp = TouchStamp;
			link.EntityTouched.Set(other);
			link.Flags = 0;
			// add it to the list
			link.NextLink = root.NextLink;
			link.PrevLink = root;
			link.PrevLink.NextLink = link;
			link.NextLink.PrevLink = link;

			// non-solid entities don't get touched
			bool shouldTouch = (IsSolid() && !IsSolidFlagSet(Source.SolidFlags.VolumeContents)) || IsSolidFlagSet(Source.SolidFlags.Trigger);
			if (shouldTouch && !other.IsSolidFlagSet(Source.SolidFlags.Trigger)) {
				link.Flags |= TouchLinkFlags.StartTouch;
				if (!sm_bDisableTouchFuncs)
					PhysicsStartTouch(other);
			}

			return link;
		}

		public static ref readonly Trace GetTouchTrace() => ref g_TouchTrace;

		//-----------------------------------------------------------------------------
		// Purpose: Marks the fact that two edicts are in contact
		// Input  : *other - other entity
		//-----------------------------------------------------------------------------
		public void PhysicsMarkEntitiesAsTouching(BaseEntity other, in Trace trace) {
			g_TouchTrace = trace;
			PhysicsMarkEntityAsTouched(other);
			other.PhysicsMarkEntityAsTouched(this);
		}

		public void PhysicsMarkEntitiesAsTouchingEventDriven(BaseEntity other, in Trace trace) {
			g_TouchTrace = trace;
			g_TouchTrace.Ent = other;

			TouchLink? link;
			link = PhysicsMarkEntityAsTouched(other);
			if (link != null) {
				// mark these links as event driven so they aren't untouched the next frame
				// when the physics doesn't refresh them
				link.TouchStamp = TouchLink.TOUCHSTAMP_EVENT_DRIVEN;
			}
			g_TouchTrace.Ent = this;
			link = other.PhysicsMarkEntityAsTouched(this);
			if (link != null)
				link.TouchStamp = TouchLink.TOUCHSTAMP_EVENT_DRIVEN;
		}

		public virtual Mask PhysicsSolidMaskForEntity() => Mask.Solid;

		//-----------------------------------------------------------------------------
		// Purpose:
		// Input  : *other -
		// Output : groundlink_t
		//-----------------------------------------------------------------------------
		public GroundLink? AddEntityToGroundList(BaseEntity? other) {
			GroundLink link;

			if (this == other)
				return null;

			// check if the edict is already in the list
			GroundLink? root = GetGroundLinkRoot();
			if (root != null) {
				for (link = root.NextLink; link != root; link = link.NextLink) {
					if (LinkEntity(link.Entity) == other) {
						// no more to do
						return link;
					}
				}
			}
			else {
				root = CreateDataObject<GroundLink>(DataObjectType.GroundLink);
				root.PrevLink = root.NextLink = root;
			}

			// entity is not in list, so it's a new touch
			// add it to the touched list and then call the touch function

			// build new link
			link = new GroundLink();

			link.Entity.Set(other);
			// add it to the list
			link.NextLink = root.NextLink;
			link.PrevLink = root;
			link.PrevLink.NextLink = link;
			link.NextLink.PrevLink = link;

			PhysicsStartGroundContact(other);

			return link;
		}

		//-----------------------------------------------------------------------------
		// Purpose: Called whenever two entities come in contact
		// Input  : *pentOther - the entity who it has touched
		//-----------------------------------------------------------------------------
		public void PhysicsStartGroundContact(BaseEntity? other) {
			if (other == null)
				return;

			if (!(IsMarkedForDeletion() || other.IsMarkedForDeletion()))
				other.StartGroundContact(this);
		}

		//-----------------------------------------------------------------------------
		// Purpose: notifies an entity than another touching entity has moved out of contact.
		// Input  : *other - the entity to be acted upon
		//-----------------------------------------------------------------------------
		public static void PhysicsNotifyOtherOfGroundRemoval(BaseEntity? ent, BaseEntity? other) {
			if (other == null)
				return;

			// loop through ed's touch list, looking for the notifier
			// remove and call untouch if found
			GroundLink? root = other.GetGroundLinkRoot();
			if (root != null) {
				GroundLink link = root.NextLink;
				while (link != root) {
					if (LinkEntity(link.Entity) == ent) {
						PhysicsRemoveGround(other, link);

						if (root.NextLink == root &&
							 root.PrevLink == root)
							other.DestroyDataObject(DataObjectType.GroundLink);
						return;
					}

					link = link.NextLink;
				}
			}
		}

		//-----------------------------------------------------------------------------
		// Purpose: removes a toucher from the list
		// Input  : *link - the link to remove
		//-----------------------------------------------------------------------------
		public static void PhysicsRemoveGround(BaseEntity? other, GroundLink link) {
			// Every start Touch gets a corresponding end touch
			BaseEntity? linkEntity = LinkEntity(link.Entity);
			if (linkEntity != null) {
				BaseEntity? otherEntity = other;
				if (otherEntity != null)
					linkEntity.EndGroundContact(otherEntity);
			}

			link.NextLink.PrevLink = link.PrevLink;
			link.PrevLink.NextLink = link.NextLink;
		}

		//-----------------------------------------------------------------------------
		// Purpose: static method to remove ground list for an entity
		// Input  : *ent -
		//-----------------------------------------------------------------------------
		public static void PhysicsRemoveGroundList(BaseEntity ent) {
			GroundLink? link, nextLink;

			GroundLink? root = ent.GetGroundLinkRoot();
			if (root != null) {
				link = root.NextLink;
				while (link != null && link != root) {
					nextLink = link.NextLink;

					// notify the other entity that this ent has gone away
					PhysicsNotifyOtherOfGroundRemoval(ent, LinkEntity(link.Entity));

					link = nextLink;
				}

				ent.DestroyDataObject(DataObjectType.GroundLink);
			}
		}

		public void StartGroundContact(BaseEntity ground) {
			AddFlag(Source.EntityFlags.OnGround);
		}

		public void EndGroundContact(BaseEntity ground) {
			RemoveFlag(Source.EntityFlags.OnGround);
		}

		public void SetGroundEntity(BaseEntity? ground) {
			if (GroundEntity.Get() == ground)
				return;

#if GAME_DLL
			// this can happen in-between updates to the held object controller (physcannon, +USE)
			// so trap it here and release held objects when they become player ground
			if (ground != null && IsPlayer() && ground.GetMoveType() == Source.MoveType.VPhysics) {
				BasePlayer? player = ToBasePlayer(this);
				IPhysicsObject? physGround = ground.VPhysicsGetObject();
				if (physGround != null && player != null)
					if ((physGround.GetGameFlags() & PhysicsFlags.PlayerHeld) != 0)
						player.ForceDropOfCarriedPhysObjects(ground);
			}
#endif

			BaseEntity? oldGround = GroundEntity.Get();
			GroundEntity = EHANDLE.FromEntity(ground);

			// Just starting to touch
			if (oldGround == null && ground != null)
				ground.AddEntityToGroundList(this);
			// Just stopping touching
			else if (oldGround != null && ground == null)
				PhysicsNotifyOtherOfGroundRemoval(this, oldGround);
			// Changing out to new ground entity
			else {
				PhysicsNotifyOtherOfGroundRemoval(this, oldGround);
				ground!.AddEntityToGroundList(this);
			}

			// HACK/PARANOID:  This is redundant with the code above, but in case we get out of sync groundlist entries ever, 
			//  this will force the appropriate flags
			if (ground != null)
				AddFlag(Source.EntityFlags.OnGround);
			else
				RemoveFlag(Source.EntityFlags.OnGround);
		}

		public virtual void PhysicsSimulate() {
			if (SimulationTick == gpGlobals.TickCount)
				return;

			SimulationTick = gpGlobals.TickCount;

			Assert(!IsPlayer());
			BaseEntity? moveParent = GetMoveParent();

			if ((GetMoveType() == Source.MoveType.None && moveParent == null) || (GetMoveType() == Source.MoveType.VPhysics)) {
				PhysicsNone();
				return;
			}

			// If ground entity goes away, make sure FL_ONGROUND is valid
			if (GetGroundEntity() == null)
				RemoveFlag(Source.EntityFlags.OnGround);

			if (moveParent != null) {
				moveParent.PhysicsSimulate();
			}
			else {
				UpdateBaseVelocity();

				if (((GetFlags() & Source.EntityFlags.BaseVelocity) == 0) && (GetBaseVelocity() != vec3_origin)) {
					// Apply momentum (add in half of the previous frame of velocity first)
					// BUGBUG: This will break with PhysicsStep() because of the timestep difference
					MathLib.VectorMA(GetAbsVelocity(), 1.0f + (float)(gpGlobals.FrameTime * 0.5), GetBaseVelocity(), out Vector3 absVelocity);
					SetAbsVelocity(absVelocity);
					SetBaseVelocity(vec3_origin);
				}
				RemoveFlag(Source.EntityFlags.BaseVelocity);
			}

			switch (GetMoveType()) {
				case Source.MoveType.Push:
					PhysicsPusher();
					break;
				case Source.MoveType.VPhysics:
					break;
				case Source.MoveType.None:
					Assert(moveParent != null);
					PhysicsRigidChild();
					break;
				case Source.MoveType.Noclip:
					PhysicsNoclip();
					break;
				case Source.MoveType.Step:
					PhysicsStep();
					break;
				case Source.MoveType.Fly:
				case Source.MoveType.FlyGravity:
					PhysicsToss();
					break;
				case Source.MoveType.Custom:
					PhysicsCustom();
					break;
				default:
					Warning($"PhysicsSimulate: {GetClassname()} bad movetype {GetMoveType()}");
					Assert(0);
					break;
			}
		}


		static readonly BASEPTR BaseThink = static self => self.Think();

		public bool PhysicsRunThink(ThinkMethods thinkMethod = ThinkMethods.FireAllFunctions) {
			if (IsEFlagSet(EFL.NoThinkFunction))
				return true;

			bool alive = true;

			if (thinkMethod != ThinkMethods.FireAllButBase) {
				alive = PhysicsRunSpecificThink(-1, BaseThink);
				if (!alive)
					return false;
			}

			if (thinkMethod == ThinkMethods.FireBaseOnly)
				return alive;

			for (int i = 0; i < ThinkFunctions.Count; i++) {
#if DEBUG
				CurrentThinkContext = i;
#endif

				alive = PhysicsRunSpecificThink(i, ThinkFunctions[i].Think);

#if DEBUG
				CurrentThinkContext = NO_THINK_CONTEXT;
#endif

				if (!alive)
					return false;
			}

			return alive;
		}

		public bool PhysicsRunSpecificThink(int contextIndex, BASEPTR? thinkFunc) {
			long thinktick = GetNextThinkTick(contextIndex);

			if (thinktick <= 0 || thinktick > gpGlobals.TickCount)
				return true;

			TimeUnit_t thinktime = thinktick * TICK_INTERVAL;

			if (thinktime < gpGlobals.CurTime)
				thinktime = gpGlobals.CurTime;

			SetNextThink(contextIndex, TICK_NEVER_THINK);

			PhysicsDispatchThink(thinkFunc);

			SetLastThink(contextIndex, gpGlobals.CurTime);

			return !IsMarkedForDeletion();
		}

		public void PhysicsDispatchThink(BASEPTR? thinkFunc) {
			if (IsDormant()) {
				Warning($"Dormant entity {GetClassname()} ({GetDebugName()}) is thinking!!\n");
				Assert(0);
			}

			thinkFunc?.Invoke(this);
		}

		public void UpdateWaterState() {
			// todo
		}

		public int PhysicsClipVelocity(in Vector3 inVec, in Vector3 normal, out Vector3 outVec, float overbounce) {
			float backoff;
			float change;
			float angle;
			int i, blocked;

			blocked = 0;

			angle = normal[2];

			if (angle > 0)
				blocked |= 1;
			if (angle == 0)
				blocked |= 2;

			backoff = Vector3.Dot(inVec, normal) * overbounce;

			outVec = default;
			for (i = 0; i < 3; i++) {
				change = normal[i] * backoff;
				outVec[i] = inVec[i] - change;
				if (outVec[i] > -GameMovement.STOP_EPSILON && outVec[i] < GameMovement.STOP_EPSILON)
					outVec[i] = 0;
			}

			return blocked;
		}

		public void PhysicsCheckVelocity() {
			Vector3 origin = GetAbsOrigin();
			Vector3 vecAbsVelocity = GetAbsVelocity();

			bool reset = false;
			for (int i = 0; i < 3; i++) {
				if (float.IsNaN(vecAbsVelocity[i])) {
					Msg($"Got a NaN velocity on {GetClassname()}\n");
					vecAbsVelocity[i] = 0;
					reset = true;
				}
				if (float.IsNaN(origin[i])) {
					Msg($"Got a NaN origin on {GetClassname()}\n");
					origin[i] = 0;
					reset = true;
				}

				if (vecAbsVelocity[i] > sv_maxvelocity.GetFloat()) {
#if DEBUG
					DevWarning(2, $"Got a velocity too high on {GetClassname()}\n");
#endif
					vecAbsVelocity[i] = sv_maxvelocity.GetFloat();
					reset = true;
				}
				else if (vecAbsVelocity[i] < -sv_maxvelocity.GetFloat()) {
#if DEBUG
					DevWarning(2, $"Got a velocity too low on {GetClassname()}\n");
#endif
					vecAbsVelocity[i] = -sv_maxvelocity.GetFloat();
					reset = true;
				}
			}

			if (reset) {
				SetAbsOrigin(origin);
				SetAbsVelocity(vecAbsVelocity);
			}
		}

		private bool PhysicsCheckWater() {
			if (GetMoveParent() != null)
				return GetWaterLevel() > Shared.WaterLevel.Feet;

			Contents cont = GetWaterType();

			if ((cont & (Contents)(Mask.Water | Mask.Current)) != (Contents)(Mask.Water | Mask.Current))
				return GetWaterLevel() > Shared.WaterLevel.Feet;

			Vector3 v = new(0, 0, 0);
			if ((cont & Contents.Current0) != 0)
				v[0] += 1;
			if ((cont & Contents.Current90) != 0)
				v[1] += 1;
			if ((cont & Contents.Current180) != 0)
				v[0] -= 1;
			if ((cont & Contents.Current270) != 0)
				v[1] -= 1;
			if ((cont & Contents.CurrentUp) != 0)
				v[2] += 1;
			if ((cont & Contents.CurrentDown) != 0)
				v[2] -= 1;

			MathLib.VectorMA(GetBaseVelocity(), 50.0f * (int)GetWaterLevel(), v, out Vector3 newBaseVelocity);
			SetBaseVelocity(newBaseVelocity);

			return GetWaterLevel() > Shared.WaterLevel.Feet;
		}

		public void SimulateAngles(TimeUnit_t frameTime) {
			QAngle angles = GetLocalAngles() + GetLocalAngularVelocity() * (float)frameTime;
			SetLocalAngles(angles);
		}
	}
}

namespace Game.Shared
{
	public enum ThinkMethods
	{
		FireAllFunctions,
		FireBaseOnly,
		FireAllButBase,
	}

	public class DataObjectAccessSystem : AutoGameSystem
	{
		public override bool Init() {

			return true;
		}

		public override void Shutdown() {
			
		}

		readonly IEntityDataInstantiator[] Accessors = [
			//GroundLink
			new EntityDataInstantiator<GroundLink>(),
			//TouchLink
			new EntityDataInstantiator<TouchLink>(),
			//StepSimulation
			new EntityDataInstantiator<StepSimulationData>(),
			//ModelScale
			null!,
			//PositionWatcher
			new EntityDataInstantiator<WatcherList>(),
			//PhysicsPushList
			new EntityDataInstantiator<PhysicsPushList>(),
			//VPhysicsUpdateAI
			new EntityDataInstantiator<VPhysicsUpdateAI>(),
			//VPhysicsWatcher
			new EntityDataInstantiator<WatcherList>(),
		];
		// Blank for now
		const int MAX_ACCESSORS = 32;

		bool IsValidType(DataObjectType type) {
			if (type < 0 || (int)type >= MAX_ACCESSORS)
				return false;

			if (Accessors[(int)type] == null)
				return false;

			return true;
		}

		public ref T GetDataObject<T>(DataObjectType type, BaseEntity? instance) where T : new() {
			if (!IsValidType(type)) {
				AssertMsg(false, "Bogus type");
				return ref Unsafe.NullRef<T>();
			}
			return ref Accessors[(int)type].GetDataObject<T>(instance!);
		}
		public ref T CreateDataObject<T>(DataObjectType type, BaseEntity? instance) where T : new() {
			if (!IsValidType(type)) {
				AssertMsg(false, "Bogus type");
				return ref Unsafe.NullRef<T>();
			}
			return ref Accessors[(int)type].CreateDataObject<T>(instance!);
		}
		public void DestroyDataObject(DataObjectType type, BaseEntity? instance) {
			if (!IsValidType(type)) {
				AssertMsg(false, "Bogus type");
				return;
			}

			Accessors[(int)type].DestroyDataObject(instance!);
		}
	}

	public static class DataObjectAccessSystemGlobals
	{
		public static readonly DataObjectAccessSystem g_DataObjectAccessSystem = new();
	}
}
#endif
