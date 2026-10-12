using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

namespace Game.Server;

using FIELD_BT = FIELD<BaseTrigger>;

public static class TriggerGlobals
{
	public static readonly ConVar showtriggers = new("showtriggers", "0", FCvar.Cheat, "Shows trigger brushes");

	// Global list of triggers that care about weapon fire
	// Doesn't need saving, the triggers re-add themselves on restore.
	public static readonly List<Handle<TriggerMultiple>> g_hWeaponFireTriggers = [];

	// Command to dynamically toggle trigger visibility
	[ConCommand("showtriggers_toggle", "Toggle show triggers", FCvar.Cheat)]
	static void Cmd_ShowtriggersToggle_f(in TokenizedCommand args) {
		// Loop through the entities in the game and make visible anything derived from CBaseTrigger
		BaseEntity? entity = gEntList.FirstEnt();
		while (entity != null) {
			if (IsTriggerClass(entity)) {
				// If a classname is specified, only show triggles of that type
				if (args.ArgC() > 1) {
					ReadOnlySpan<char> classname = args[1];
					if (!classname.IsEmpty) {
						if (!BaseEntity.FClassnameIs(entity, classname)) {
							entity = gEntList.NextEnt(entity);
							continue;
						}
					}
				}

				if (entity.IsEffectActive(EntityEffects.NoDraw))
					entity.RemoveEffects(EntityEffects.NoDraw);
				else
					entity.AddEffects(EntityEffects.NoDraw);
			}

			entity = gEntList.NextEnt(entity);
		}
	}

	public static bool IsTriggerClass(BaseEntity entity) {
		if (entity is BaseTrigger)
			return true;

		return false;
	}

	//-----------------------------------------------------------------------------
	// Purpose: Checks if this point is in any trigger_hurt zones with positive damage
	//-----------------------------------------------------------------------------
	public static bool IsTakingTriggerHurtDamageAtPoint(in Vector3 vecPoint) {
		for (int i = 0; i < TriggerHurt.AutoList.Count; i++) {
			// Some maps use trigger_hurt with negative values as healing triggers; don't consider those
			TriggerHurt trigger = TriggerHurt.AutoList[i];
			if (!trigger.Disabled && trigger.PointIsWithin(vecPoint) && trigger.Damage > 0.0f)
				return true;
		}

		return false;
	}
}

[LinkEntityToClass("trigger")]
[NetworkName("CBaseTrigger")]
public class BaseTrigger : BaseToggle
{
	[Flags]
	public enum SF
	{
		AllowClients = 0x01,       // Players can fire this trigger
		AllowNPCs = 0x02,      // NPCS can fire this trigger
		AllowPushables = 0x04,     // Pushables can fire this trigger
		AllowPhysics = 0x08,       // Physics objects can fire this trigger
		OnlyPlayerAllyNPCs = 0x10,       // *if* NPCs can fire this trigger, this flag means only player allies do so
		OnlyClientsInVehicles = 0x20,        // *if* Players can fire this trigger, this flag means only players inside vehicles can
		AllowAll = 0x40,       // Everything can fire this trigger EXCEPT DEBRIS!
		OnlyClientsOutOfVehicles = 0x200,   // *if* Players can fire this trigger, this flag means only players outside vehicles can
		PushOnce = 0x80,      // trigger_push removes itself after firing once
		PushAffectPlayerOnLadder = 0x100,  // if pushed object is player on a ladder, then this disengages them from the ladder (HL2only)
		TouchDebris = 0x400,  // Will touch physics debris objects
		OnlyNPCsInVehicles = 0X800,  // *if* NPCs can fire this trigger, only NPCs in vehicles do so (respects player ally flag too)
		DisallowBots = 0x1000,   // Bots are not allowed to fire this trigger
	}

	public static readonly SendTable DT_BaseTrigger = new(DT_BaseToggle, [
		SendPropBool(FIELD_BT.OF(nameof(ClientSidePredicted))),
		SendPropInt(FIELD_BT.OF(nameof(SpawnFlags)), 32, PropFlags.NoScale)
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_BaseTrigger);
	[NetworkName("m_bClientSidePredicted")]
	public bool ClientSidePredicted;

	public bool Disabled;
	public string? FilterName;
	public Handle<BaseFilter> Filter = new();

	// Outputs
	protected OutputEvent OnStartTouch = new();
	protected OutputEvent OnStartTouchAll = new();
	protected OutputEvent OnEndTouch = new();
	protected OutputEvent OnEndTouchAll = new();
	protected OutputEvent OnTouching = new();
	protected OutputEvent OnNotTouching = new();

	// Entities currently being touched by this trigger
	protected readonly List<EHANDLE> TouchingEntities = [];

	// Global Savedata for base trigger
	public static readonly new DataMap DataDesc = new(typeof(BaseTrigger), BaseToggle.DataDesc, [
		// Keyfields
		DEFINE<BaseTrigger>.KEYFIELD(nameof(FilterName), FieldType.String, "filtername"),
		DEFINE<BaseTrigger>.FIELD(nameof(Filter), FieldType.EHandle),
		DEFINE<BaseTrigger>.KEYFIELD(nameof(Disabled), FieldType.Boolean, "StartDisabled"),
		DEFINE<BaseTrigger>.UTLVECTOR(nameof(TouchingEntities), FieldType.EHandle),

		// Inputs
		DEFINE<BaseTrigger>.INPUTFUNC(FieldType.Void, "Enable", nameof(InputEnable), (INPUTFUNCPTR)((self, data) => ((BaseTrigger)self).InputEnable(data))),
		DEFINE<BaseTrigger>.INPUTFUNC(FieldType.Void, "Disable", nameof(InputDisable), (INPUTFUNCPTR)((self, data) => ((BaseTrigger)self).InputDisable(data))),
		DEFINE<BaseTrigger>.INPUTFUNC(FieldType.Void, "DisableAndEndTouch", nameof(InputDisableAndEndTouch), (INPUTFUNCPTR)((self, data) => ((BaseTrigger)self).InputDisableAndEndTouch(data))),
		DEFINE<BaseTrigger>.INPUTFUNC(FieldType.Void, "Toggle", nameof(InputToggle), (INPUTFUNCPTR)((self, data) => ((BaseTrigger)self).InputToggle(data))),
		DEFINE<BaseTrigger>.INPUTFUNC(FieldType.Void, "TouchTest", nameof(InputTouchTest), (INPUTFUNCPTR)((self, data) => ((BaseTrigger)self).InputTouchTest(data))),

		DEFINE<BaseTrigger>.INPUTFUNC(FieldType.Void, "StartTouch", nameof(InputStartTouch), (INPUTFUNCPTR)((self, data) => ((BaseTrigger)self).InputStartTouch(data))),
		DEFINE<BaseTrigger>.INPUTFUNC(FieldType.Void, "EndTouch", nameof(InputEndTouch), (INPUTFUNCPTR)((self, data) => ((BaseTrigger)self).InputEndTouch(data))),

		// Outputs
		DEFINE<BaseTrigger>.OUTPUT(nameof(OnStartTouch), "OnStartTouch", eventFuncs),
		DEFINE<BaseTrigger>.OUTPUT(nameof(OnStartTouchAll), "OnStartTouchAll", eventFuncs),
		DEFINE<BaseTrigger>.OUTPUT(nameof(OnEndTouch), "OnEndTouch", eventFuncs),
		DEFINE<BaseTrigger>.OUTPUT(nameof(OnEndTouchAll), "OnEndTouchAll", eventFuncs),
		DEFINE<BaseTrigger>.OUTPUT(nameof(OnTouching), "OnTouching", eventFuncs),
		DEFINE<BaseTrigger>.OUTPUT(nameof(OnNotTouching), "OnNotTouching", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public BaseTrigger() {
		AddEFlags(EFL.UsePartitionWhenNotSolid);
	}

	//------------------------------------------------------------------------------
	// Purpose: Input handler to turn on this trigger.
	//------------------------------------------------------------------------------
	public virtual void InputEnable(InputData inputdata) {
		Enable();
	}

	//------------------------------------------------------------------------------
	// Purpose: Input handler to turn off this trigger.
	//------------------------------------------------------------------------------
	public virtual void InputDisable(InputData inputdata) {
		Disable();
	}

	//------------------------------------------------------------------------------
	// Purpose: Input handler to call EndTouch on all touching entities, and then
	//			turn off this trigger
	//------------------------------------------------------------------------------
	public virtual void InputDisableAndEndTouch(InputData inputdata) {
		for (int i = TouchingEntities.Count - 1; i >= 0; i--) {
			BaseEntity? touching = (BaseEntity?)TouchingEntities[i].Get();
			if (touching != null)
				EndTouch(touching);
			else
				TouchingEntities.RemoveAt(i);
		}

		Disable();
	}

	public virtual void InputTouchTest(InputData inputdata) {
		TouchTest();
	}

	//------------------------------------------------------------------------------
	//------------------------------------------------------------------------------
	public override void Spawn() {
		if (HasSpawnFlags(SF.OnlyPlayerAllyNPCs) || HasSpawnFlags(SF.OnlyNPCsInVehicles)) {
			// Automatically set this trigger to work with NPC's.
			AddSpawnFlags(SF.AllowNPCs);
		}

		if (HasSpawnFlags(SF.OnlyClientsInVehicles))
			AddSpawnFlags(SF.AllowClients);

		if (HasSpawnFlags(SF.OnlyClientsOutOfVehicles))
			AddSpawnFlags(SF.AllowClients);

		base.Spawn();
	}

	//------------------------------------------------------------------------------
	// Cleanup
	//------------------------------------------------------------------------------
	public override void UpdateOnRemove() {
		VPhysicsGetObject()?.RemoveTrigger();

		base.UpdateOnRemove();
	}

	//------------------------------------------------------------------------------
	// Purpose: Turns on this trigger.
	//------------------------------------------------------------------------------
	public void Enable() {
		Disabled = false;

		VPhysicsGetObject()?.EnableCollisions(true);

		if (!IsSolidFlagSet(SolidFlags.Trigger)) {
			AddSolidFlags(SolidFlags.Trigger);
			PhysicsTouchTriggers();
		}
	}

	public override int DrawDebugTextOverlays() {
		int text_offset = base.DrawDebugTextOverlays();

		if ((DebugOverlays & DebugOverlayBits.Text) != 0) {
			Span<char> tempstr = stackalloc char[255];
			if (IsSolidFlagSet(SolidFlags.Trigger))
				strcpy(tempstr, "State: Enabled");
			else
				strcpy(tempstr, "State: Disabled");
			EntityText(text_offset, tempstr, 0);
			text_offset++;
		}

		return text_offset;
	}

	//------------------------------------------------------------------------------
	// Purpose :
	//------------------------------------------------------------------------------
	public override void Activate() {
		// Get a handle to my filter entity if there is one
		if (FilterName != null)
			Filter.Set(gEntList.FindEntityByName(null, FilterName) as BaseFilter);

		base.Activate();
	}

	//-----------------------------------------------------------------------------
	// Purpose: Called after player becomes active in the game
	//-----------------------------------------------------------------------------
	public override void PostClientActive() {
		base.PostClientActive();

		if (!Disabled)
			PhysicsTouchTriggers();
	}

	//------------------------------------------------------------------------------
	// Purpose: Turns off this trigger.
	//------------------------------------------------------------------------------
	public void Disable() {
		Disabled = true;

		VPhysicsGetObject()?.EnableCollisions(false);

		if (IsSolidFlagSet(SolidFlags.Trigger)) {
			RemoveSolidFlags(SolidFlags.Trigger);
			PhysicsTouchTriggers();
		}
	}

	//------------------------------------------------------------------------------
	// Purpose: Tests to see if anything is touching this trigger.
	//------------------------------------------------------------------------------
	public void TouchTest() {
		// If the trigger is disabled don't test to see if anything is touching it.
		if (!Disabled) {
			if (TouchingEntities.Count != 0)
				OnTouching.FireOutput(this, this);
			else
				OnNotTouching.FireOutput(this, this);
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose: Return true if the specified point is within this zone
	//-----------------------------------------------------------------------------
	public bool PointIsWithin(in Vector3 vecPoint) {
		Ray ray = default;
		Trace tr = default;
		ICollideable collide = CollisionProp();
		ray.Init(vecPoint, vecPoint);
		enginetrace.ClipRayToCollideable(ray, Mask.All, collide, ref tr);
		return tr.StartSolid;
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	//-----------------------------------------------------------------------------
	public void InitTrigger() {
		SetSolid(GetParent() != null ? SolidType.VPhysics : SolidType.BSP);
		AddSolidFlags(SolidFlags.NotSolid);
		if (Disabled)
			RemoveSolidFlags(SolidFlags.Trigger);
		else
			AddSolidFlags(SolidFlags.Trigger);

		SetMoveType(Source.MoveType.None);
		SetModel(GetModelName());    // set size and link into world
		if (TriggerGlobals.showtriggers.GetInt() == 0)
			AddEffects(EntityEffects.NoDraw);

		TouchingEntities.Clear();

		if (HasSpawnFlags(SF.TouchDebris))
			CollisionProp().AddSolidFlags(SolidFlags.TriggerTouchDebris);
	}

	//-----------------------------------------------------------------------------
	// Purpose: Returns true if this entity passes the filter criteria, false if not.
	// Input  : pOther - The entity to be filtered.
	//-----------------------------------------------------------------------------
	public virtual bool PassesTriggerFilters(BaseEntity other) {
		// First test spawn flag filters
		if (HasSpawnFlags(SF.AllowAll) ||
			(HasSpawnFlags(SF.AllowClients) && (other.GetFlags() & EntityFlags.Client) != 0) ||
			(HasSpawnFlags(SF.AllowNPCs) && (other.GetFlags() & EntityFlags.NPC) != 0) ||
			(HasSpawnFlags(SF.AllowPushables) && FClassnameIs(other, "func_pushable")) ||
			(HasSpawnFlags(SF.AllowPhysics) && other.GetMoveType() == Source.MoveType.VPhysics)
#if HL2_EPISODIC || TF_DLL
			||
			(HasSpawnFlags(SF.TouchDebris) &&
				(other.GetCollisionGroup() == Source.CollisionGroup.Debris ||
				other.GetCollisionGroup() == Source.CollisionGroup.DebrisTrigger ||
				other.GetCollisionGroup() == Source.CollisionGroup.InteractiveDebris)
			)
#endif
			) {
			bool otherIsPlayer = other.IsPlayer();

			if (otherIsPlayer) {
				BasePlayer player = (BasePlayer)other;
				if (!player.IsAlive())
					return false;

				if (HasSpawnFlags(SF.OnlyClientsInVehicles)) {
					if (!player.IsInAVehicle())
						return false;

					// Make sure we're also not exiting the vehicle at the moment
					IServerVehicle? vehicleServer = player.GetVehicle();
					if (vehicleServer == null)
						return false;

					if (vehicleServer.IsPassengerExiting())
						return false;
				}

				if (HasSpawnFlags(SF.OnlyClientsOutOfVehicles)) {
					if (player.IsInAVehicle())
						return false;
				}

				if (HasSpawnFlags(SF.DisallowBots)) {
					if (player.IsFakeClient())
						return false;
				}
			}

			BaseFilter? filter = Filter.Get();
			return (filter == null) ? true : filter.PassesFilter(this, other);
		}
		return false;
	}

	//-----------------------------------------------------------------------------
	// Purpose: Called to simulate what happens when an entity touches the trigger.
	// Input  : pOther - The entity that is touching us.
	//-----------------------------------------------------------------------------
	public virtual void InputStartTouch(InputData inputdata) {
		//Pretend we just touched the trigger.
		StartTouch(inputdata.Caller);
	}

	//-----------------------------------------------------------------------------
	// Purpose: Called to simulate what happens when an entity leaves the trigger.
	// Input  : pOther - The entity that is touching us.
	//-----------------------------------------------------------------------------
	public virtual void InputEndTouch(InputData inputdata) {
		//And... pretend we left the trigger.
		EndTouch(inputdata.Caller);
	}

	public virtual void StartTouchAll() { }
	public virtual void EndTouchAll() { }

	//-----------------------------------------------------------------------------
	// Purpose: Called when an entity starts touching us.
	// Input  : pOther - The entity that is touching us.
	//-----------------------------------------------------------------------------
	public override void StartTouch(BaseEntity? other) {
		if (other != null && PassesTriggerFilters(other)) {
			EHANDLE hOther = new();
			hOther.Set(other);

			bool added = false;
			if (!TouchingEntities.Contains(hOther)) {
				TouchingEntities.Add(hOther);
				added = true;
			}

			OnStartTouch.FireOutput(other, this);

			if (added && (TouchingEntities.Count == 1)) {
				// First entity to touch us that passes our filters
				OnStartTouchAll.FireOutput(other, this);
				StartTouchAll();
			}
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose: Called when an entity stops touching us.
	// Input  : pOther - The entity that was touching us.
	//-----------------------------------------------------------------------------
	public override void EndTouch(BaseEntity? other) {
		if (IsTouching(other)) {
			EHANDLE hOther = new();
			hOther.Set(other);
			TouchingEntities.Remove(hOther);

			//FIXME: Without this, triggers fire their EndTouch outputs when they are disabled!
			//if ( !m_bDisabled )
			//{
			OnEndTouch.FireOutput(other, this);
			//}

			// If there are no more entities touching this trigger, fire the lost all touches
			// Loop through the touching entities backwards. Clean out old ones, and look for existing
			bool foundOtherTouchee = false;
			int size = TouchingEntities.Count;
			for (int i = size - 1; i >= 0; i--) {
				BaseEntity? touchee = (BaseEntity?)TouchingEntities[i].Get();

				if (touchee == null)
					TouchingEntities.RemoveAt(i);
				else if (touchee.IsPlayer() && !touchee.IsAlive())
					TouchingEntities.RemoveAt(i);
				else
					foundOtherTouchee = true;
			}

			//FIXME: Without this, triggers fire their EndTouch outputs when they are disabled!
			// Didn't find one?
			if (!foundOtherTouchee /*&& !m_bDisabled*/ ) {
				OnEndTouchAll.FireOutput(other, this);
				EndTouchAll();
			}
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose: Return true if the specified entity is touching us
	//-----------------------------------------------------------------------------
	public virtual bool IsTouching(BaseEntity? other) {
		EHANDLE hOther = new();
		hOther.Set(other);
		return TouchingEntities.Contains(hOther);
	}

	//-----------------------------------------------------------------------------
	// Purpose: Return a pointer to the first entity of the specified type being touched by this trigger
	//-----------------------------------------------------------------------------
	public BaseEntity? GetTouchedEntityOfType(ReadOnlySpan<char> className) {
		int count = TouchingEntities.Count;
		for (int i = 0; i < count; i++) {
			BaseEntity? entity = (BaseEntity?)TouchingEntities[i].Get();
			if (entity != null && FClassnameIs(entity, className))
				return entity;
		}

		return null;
	}

	//-----------------------------------------------------------------------------
	// Purpose: Toggles this trigger between enabled and disabled.
	//-----------------------------------------------------------------------------
	public virtual void InputToggle(InputData inputdata) {
		if (IsSolidFlagSet(SolidFlags.Trigger))
			RemoveSolidFlags(SolidFlags.Trigger);
		else
			AddSolidFlags(SolidFlags.Trigger);

		PhysicsTouchTriggers();
	}

	public virtual bool UsesFilter() => Filter.Get() != null;
}

//-----------------------------------------------------------------------------
// Purpose: Removes anything that touches it. If the trigger has a targetname,
//			firing it will toggle state.
//-----------------------------------------------------------------------------
[LinkEntityToClass("trigger_remove")]
public class TriggerRemove : BaseTrigger
{
	// Outputs
	OutputEvent OnRemove = new();

	public static readonly new DataMap DataDesc = new(typeof(TriggerRemove), BaseTrigger.DataDesc, [
		// Outputs
		DEFINE<TriggerRemove>.OUTPUT(nameof(OnRemove), "OnRemove", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	//-----------------------------------------------------------------------------
	// Purpose:
	//-----------------------------------------------------------------------------
	public override void Spawn() {
		base.Spawn();
		InitTrigger();
	}

	//-----------------------------------------------------------------------------
	// Purpose: Trigger hurt that causes radiation will do a radius check and set
	//			the player's geiger counter level according to distance from center
	//			of trigger.
	//-----------------------------------------------------------------------------
	public override void Touch(BaseEntity? other) {
		if (!PassesTriggerFilters(other!))
			return;

		Util.Remove(other);
	}
}

//-----------------------------------------------------------------------------
// Purpose: Hurts anything that touches it. If the trigger has a targetname,
//			firing it will toggle state.
//-----------------------------------------------------------------------------
[LinkEntityToClass("trigger_hurt")]
public class TriggerHurt : BaseTrigger
{
	public static readonly List<TriggerHurt> AutoList = [];

	public float OriginalDamage;   // Damage as specified by the level designer.
	public float Damage;           // Damage per second.
	public float DamageCap;        // Maximum damage per second.
	public TimeUnit_t LastDmgTime;    // Time that we last applied damage.
	public TimeUnit_t DmgResetTime;   // For forgiveness, the time to reset the counter that accumulates damage.
	public int BitsDamageInflict;    // DMG_ damage type that the door or tigger does
	public int DamageModel;
	public bool NoDmgForce;        // Should damage from this trigger impart force on what it's hurting

	public const int DAMAGEMODEL_NORMAL = 0;
	public const int DAMAGEMODEL_DOUBLE_FORGIVENESS = 1;

	// Outputs
	public OutputEvent OnHurt = new();
	public OutputEvent OnHurtPlayer = new();

	readonly List<EHANDLE> HurtEntities = [];

	public static readonly new DataMap DataDesc = new(typeof(TriggerHurt), BaseTrigger.DataDesc, [
		// Function Pointers
		DEFINE<TriggerHurt>.FUNCTION(nameof(RadiationThink)),
		DEFINE<TriggerHurt>.FUNCTION(nameof(HurtThink)),

		// Fields
		DEFINE<TriggerHurt>.FIELD(nameof(OriginalDamage), FieldType.Float),
		DEFINE<TriggerHurt>.KEYFIELD(nameof(Damage), FieldType.Float, "damage"),
		DEFINE<TriggerHurt>.KEYFIELD(nameof(DamageCap), FieldType.Float, "damagecap"),
		DEFINE<TriggerHurt>.KEYFIELD(nameof(BitsDamageInflict), FieldType.Integer, "damagetype"),
		DEFINE<TriggerHurt>.KEYFIELD(nameof(DamageModel), FieldType.Integer, "damagemodel"),
		DEFINE<TriggerHurt>.KEYFIELD(nameof(NoDmgForce), FieldType.Boolean, "nodmgforce"),

		DEFINE<TriggerHurt>.FIELD(nameof(LastDmgTime), FieldType.Time),
		DEFINE<TriggerHurt>.FIELD(nameof(DmgResetTime), FieldType.Time),
		DEFINE<TriggerHurt>.UTLVECTOR(nameof(HurtEntities), FieldType.EHandle),

		// Inputs
		DEFINE<TriggerHurt>.INPUT(nameof(Damage), FieldType.Float, "SetDamage"),

		// Outputs
		DEFINE<TriggerHurt>.OUTPUT(nameof(OnHurt), "OnHurt", eventFuncs),
		DEFINE<TriggerHurt>.OUTPUT(nameof(OnHurtPlayer), "OnHurtPlayer", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public TriggerHurt() {
		// This field came along after levels were built so the field defaults to 20 here in the constructor.
		DamageCap = 20.0f;
		AutoList.Add(this);
	}

	public override void UpdateOnRemove() {
		AutoList.Remove(this);
		base.UpdateOnRemove();
	}

	//-----------------------------------------------------------------------------
	// Purpose: Called when spawning, after keyvalues have been handled.
	//-----------------------------------------------------------------------------
	public override void Spawn() {
		base.Spawn();

		InitTrigger();

		OriginalDamage = Damage;

		SetNextThink(TICK_NEVER_THINK);
		SetThink(null);
		if ((BitsDamageInflict & (int)DamageType.Radiation) != 0) {
			SetThink(RadiationThink);
			SetNextThink(gpGlobals.CurTime + random.RandomFloat(0.0f, 0.5f));
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose: Trigger hurt that causes radiation will do a radius check and set
	//			the player's geiger counter level according to distance from center
	//			of trigger.
	//-----------------------------------------------------------------------------
	void RadiationThink() {
		// check to see if a player is in pvs
		// if not, continue
		CollisionProp().WorldSpaceSurroundingBounds(out Vector3 vecSurroundMins, out Vector3 vecSurroundMaxs);
		BasePlayer? player = Util.FindClientInPVS(vecSurroundMins, vecSurroundMaxs) as BasePlayer;

		if (player != null) {
			// get range to player;
			float range = CollisionProp().CalcDistanceFromPoint(player.WorldSpaceCenter());
			range *= 3.0f;
			player.NotifyNearbyRadiationSource(range);
		}

		TimeUnit_t dt = gpGlobals.CurTime - LastDmgTime;
		if (dt >= 0.5)
			HurtAllTouchers((float)dt);

		SetNextThink(gpGlobals.CurTime + 0.25f);
	}

	//-----------------------------------------------------------------------------
	// Purpose: When touched, a hurt trigger does m_flDamage points of damage each half-second.
	// Input  : pOther - The entity that is touching us.
	//-----------------------------------------------------------------------------
	public bool HurtEntity(BaseEntity other, float damage) {
		if (other.m_takedamage == 0 || !PassesTriggerFilters(other))
			return false;

		// If player is disconnected, we're probably in this routine via the
		//  PhysicsRemoveTouchedList() function to make sure all Untouch()'s are called for the
		//  player. Calling TakeDamage() in this case can get into the speaking criteria, which
		//  will then loop through the control points and the touched list again. We shouldn't
		//  need to hurt players that are disconnected, so skip all of this...
		bool playerDisconnected = other.IsPlayer() && (((BasePlayer)other).IsConnected() == false);
		if (playerDisconnected)
			return false;

		if (damage < 0)
			other.TakeHealth(-damage, (DamageType)BitsDamageInflict);
		else {
			// The damage position is the nearest point on the damaged entity
			// to the trigger's center. Not perfect, but better than nothing.
			Vector3 vecCenter = CollisionProp().WorldSpaceCenter();

			other.CollisionProp().CalcNearestPoint(vecCenter, out Vector3 vecDamagePos);

			TakeDamageInfo info = new(this, this, damage, (DamageType)BitsDamageInflict);
			info.SetDamagePosition(vecDamagePos);
			if (!NoDmgForce)
				TakeDamageInfoGlobals.GuessDamageForce(ref info, (vecDamagePos - vecCenter), vecDamagePos, 1.0f);
			else
				info.SetDamageForce(vec3_origin);

			other.TakeDamage(info);
		}

		if (other.IsPlayer())
			OnHurtPlayer.FireOutput(other, this);
		else
			OnHurt.FireOutput(other, this);

		EHANDLE hOther = new();
		hOther.Set(other);
		HurtEntities.Add(hOther);
		//NDebugOverlay::Box( pOther->GetAbsOrigin(), pOther->WorldAlignMins(), pOther->WorldAlignMaxs(), 255,0,0,0,0.5 );
		return true;
	}

	void HurtThink() {
		// if I hurt anyone, think again
		if (HurtAllTouchers(0.5f) <= 0)
			SetThink(null);
		else
			SetNextThink(gpGlobals.CurTime + 0.5f);
	}

	public override void EndTouch(BaseEntity? other) {
		if (other != null && PassesTriggerFilters(other)) {
			EHANDLE hOther = new();
			hOther.Set(other);

			// if this guy has never taken damage, hurt him now
			if (!HurtEntities.Contains(hOther))
				HurtEntity(other, Damage * 0.5f);
		}
		base.EndTouch(other);
	}

	//-----------------------------------------------------------------------------
	// Purpose: called from RadiationThink() as well as HurtThink()
	//			This function applies damage to any entities currently touching the
	//			trigger
	// Input  : dt - time since last call
	// Output : int - number of entities actually hurt
	//-----------------------------------------------------------------------------
	const float TRIGGER_HURT_FORGIVE_TIME = 3.0f;   // time in seconds
	public int HurtAllTouchers(float dt) {
		int hurtCount = 0;
		// half second worth of damage
		float fldmg = Damage * dt;
		LastDmgTime = gpGlobals.CurTime;

		HurtEntities.Clear();

		TouchLink? root = GetTouchLinkRoot();
		if (root != null) {
			for (TouchLink link = root.NextLink; link != root; link = link.NextLink) {
				BaseEntity? touch = (BaseEntity?)link.EntityTouched.Get();
				if (touch != null) {
					if (HurtEntity(touch, fldmg))
						hurtCount++;
				}
			}
		}

		if (DamageModel == DAMAGEMODEL_DOUBLE_FORGIVENESS) {
			if (hurtCount == 0) {
				if (gpGlobals.CurTime > DmgResetTime) {
					// Didn't hurt anyone. Reset the damage if it's time. (hence, the forgiveness)
					Damage = OriginalDamage;
				}
			}
			else {
				// Hurt someone! double the damage
				Damage *= 2.0f;

				if (Damage > DamageCap) {
					// Clamp
					Damage = DamageCap;
				}

				// Now, put the damage reset time into the future. The forgive time is how long the trigger
				// must go without harming anyone in order that its accumulated damage be reset to the amount
				// set by the level designer. This is a stop-gap for an exploit where players could hop through
				// slime and barely take any damage because the trigger would reset damage anytime there was no
				// one in the trigger when this function was called. (sjb)
				DmgResetTime = gpGlobals.CurTime + TRIGGER_HURT_FORGIVE_TIME;
			}
		}

		return hurtCount;
	}

	public override void Touch(BaseEntity? other) {
		if (FnThink == null) {
			SetThink(HurtThink);
			SetNextThink(gpGlobals.CurTime);
		}
	}
}

// ##################################################################################
//	>> TriggerMultiple
// ##################################################################################
//-----------------------------------------------------------------------------
// Purpose: Variable sized repeatable trigger.  Must be targeted at one or more entities.
//			If "delay" is set, the trigger waits some time after activating before firing.
//			"wait" : Seconds between triggerings. (.2 default/minimum)
//-----------------------------------------------------------------------------
[LinkEntityToClass("trigger_multiple")]
public class TriggerMultiple : BaseTrigger
{
	// Outputs
	protected OutputEvent OnTrigger = new();

	public static readonly new DataMap DataDesc = new(typeof(TriggerMultiple), BaseTrigger.DataDesc, [
		// Function Pointers
		DEFINE<TriggerMultiple>.FUNCTION(nameof(MultiTouch)),
		DEFINE<TriggerMultiple>.FUNCTION(nameof(MultiWaitOver)),

		// Outputs
		DEFINE<TriggerMultiple>.OUTPUT(nameof(OnTrigger), "OnTrigger", eventFuncs)
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	//-----------------------------------------------------------------------------
	// Purpose: Called when spawning, after keyvalues have been handled.
	//-----------------------------------------------------------------------------
	public override void Spawn() {
		base.Spawn();

		InitTrigger();

		if (Wait == 0)
			Wait = 0.2f;

		Assert(Health == 0, "trigger_multiple with health");
		SetTouch(MultiTouch);
	}

	//-----------------------------------------------------------------------------
	// Purpose: Touch function. Activates the trigger.
	// Input  : pOther - The thing that touched us.
	//-----------------------------------------------------------------------------
	public void MultiTouch(BaseEntity? other) {
		if (other != null && PassesTriggerFilters(other))
			ActivateMultiTrigger(other);
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	// Input  : pActivator -
	//-----------------------------------------------------------------------------
	public void ActivateMultiTrigger(BaseEntity? activator) {
		if (GetNextThink() > gpGlobals.CurTime)
			return;         // still waiting for reset time

		Activator.Set(activator);

		OnTrigger.FireOutput((BaseEntity?)Activator.Get(), this);

		if (Wait > 0) {
			SetThink(MultiWaitOver);
			SetNextThink(gpGlobals.CurTime + Wait);
		}
		else {
			// we can't just remove (self) here, because this is a touch function
			// called while C code is looping through area links...
			SetTouch(null);
			SetNextThink(gpGlobals.CurTime + 0.1f);
			SetThink(SUB_Remove);
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose: The wait time has passed, so set back up for another activation
	//-----------------------------------------------------------------------------
	public void MultiWaitOver() {
		SetThink(null);
	}
}

// ##################################################################################
//	>> TriggerOnce
// ##################################################################################
[LinkEntityToClass("trigger_once")]
public class TriggerOnce : TriggerMultiple
{
	public override void Spawn() {
		base.Spawn();

		Wait = -1;
	}
}

// ##################################################################################
//	>> TriggerLook
//
//  Triggers once when player is looking at m_target
//
// ##################################################################################
[LinkEntityToClass("trigger_look")]
public class TriggerLook : TriggerOnce
{
	[Flags]
	public enum SF
	{
		FireOnce = 128,
		UseVelocity = 256,
	}

	public EHANDLE LookTarget = new();
	public float FieldOfView;
	public float LookTime;         // How long must I look for
	public float LookTimeTotal;    // How long have I looked
	public TimeUnit_t LookTimeLast;     // When did I last look
	public float TimeoutDuration;  // Number of seconds after start touch to fire anyway
	public bool TimeoutFired;      // True if the OnTimeout output fired since the last StartTouch.
	public new EHANDLE Activator = new();       // The entity that triggered us.

	OutputEvent OnTimeout = new();

	public static readonly new DataMap DataDesc = new(typeof(TriggerLook), TriggerMultiple.DataDesc, [
		DEFINE<TriggerLook>.FIELD(nameof(LookTarget), FieldType.EHandle),
		DEFINE<TriggerLook>.FIELD(nameof(LookTimeTotal), FieldType.Float),
		DEFINE<TriggerLook>.FIELD(nameof(LookTimeLast), FieldType.Time),
		DEFINE<TriggerLook>.KEYFIELD(nameof(TimeoutDuration), FieldType.Float, "timeout"),
		DEFINE<TriggerLook>.FIELD(nameof(TimeoutFired), FieldType.Boolean),
		DEFINE<TriggerLook>.FIELD(nameof(Activator), FieldType.EHandle),

		DEFINE<TriggerLook>.OUTPUT(nameof(OnTimeout), "OnTimeout", eventFuncs),

		DEFINE<TriggerLook>.FUNCTION(nameof(TimeoutThink)),

		// Inputs
		DEFINE<TriggerLook>.INPUT(nameof(FieldOfView), FieldType.Float, "FieldOfView"),
		DEFINE<TriggerLook>.INPUT(nameof(LookTime), FieldType.Float, "LookTime"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	//------------------------------------------------------------------------------
	// Purpose:
	//------------------------------------------------------------------------------
	public override void Spawn() {
		LookTarget.Set(null);
		LookTimeTotal = -1;
		TimeoutFired = false;

		base.Spawn();
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	// Input  : pOther -
	//-----------------------------------------------------------------------------
	public override void StartTouch(BaseEntity? other) {
		base.StartTouch(other);

		if (other != null && other.IsPlayer() && TimeoutDuration != 0) {
			TimeoutFired = false;
			Activator.Set(other);
			SetThink(TimeoutThink);
			SetNextThink(gpGlobals.CurTime + TimeoutDuration);
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	//-----------------------------------------------------------------------------
	void TimeoutThink() {
		Trigger((BaseEntity?)Activator.Get(), true);
	}

	//------------------------------------------------------------------------------
	// Purpose:
	//------------------------------------------------------------------------------
	public override void EndTouch(BaseEntity? other) {
		base.EndTouch(other);

		if (other != null && other.IsPlayer()) {
			SetThink(null);
			SetNextThink(TICK_NEVER_THINK);

			LookTimeTotal = -1;
		}
	}

	//------------------------------------------------------------------------------
	// Purpose:
	//------------------------------------------------------------------------------
	public override void Touch(BaseEntity? other) {
		// Don't fire the OnTrigger if we've already fired the OnTimeout. This will be
		// reset in OnEndTouch.
		if (TimeoutFired)
			return;

		// --------------------------------
		// Make sure we have a look target
		// --------------------------------
		if (LookTarget.Get() == null) {
			LookTarget.Set(GetNextTarget());
			if (LookTarget.Get() == null)
				return;
		}

		// This is designed for single player only
		// so we'll always have the same player
		if (other != null && other.IsPlayer()) {
			// ----------------------------------------
			// Check that toucher is facing the target
			// ----------------------------------------
			Vector3 vLookDir;
			if (HasSpawnFlags(SF.UseVelocity)) {
				vLookDir = other.GetAbsVelocity();
				if (vLookDir == vec3_origin) {
					// See if they're in a vehicle
					BasePlayer player = (BasePlayer)other;
					if (player.IsInAVehicle())
						vLookDir = player.GetVehicle()!.GetVehicleEnt()!.GetSmoothedVelocity();
				}
				MathLib.VectorNormalize(ref vLookDir);
			}
			else
				vLookDir = ((BaseCombatCharacter)other).EyeDirection3D();

			Vector3 vTargetDir = ((BaseEntity)LookTarget.Get()!).GetAbsOrigin() - other.EyePosition();
			MathLib.VectorNormalize(ref vTargetDir);

			float fDotPr = Vector3.Dot(vLookDir, vTargetDir);
			if (fDotPr > FieldOfView) {
				// Is it the first time I'm looking?
				if (LookTimeTotal == -1) {
					LookTimeLast = gpGlobals.CurTime;
					LookTimeTotal = 0;
				}
				else {
					LookTimeTotal += (float)(gpGlobals.CurTime - LookTimeLast);
					LookTimeLast = gpGlobals.CurTime;
				}

				if (LookTimeTotal >= LookTime)
					Trigger(other, false);
			}
			else
				LookTimeTotal = -1;
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose: Called when the trigger is fired by look logic or timeout.
	//-----------------------------------------------------------------------------
	void Trigger(BaseEntity? activator, bool timeout) {
		if (timeout) {
			// Fired due to timeout (player never looked at the target).
			OnTimeout.FireOutput(activator, this);

			// Don't fire the OnTrigger for this toucher.
			TimeoutFired = true;
		}
		else {
			// Fire because the player looked at the target.
			OnTrigger.FireOutput(activator, this);
			LookTimeTotal = -1;

			// Cancel the timeout think.
			SetThink(null);
			SetNextThink(TICK_NEVER_THINK);
		}

		if (HasSpawnFlags(SF.FireOnce)) {
			SetThink(SUB_Remove);
			SetNextThink(gpGlobals.CurTime);
		}
	}
}

//-----------------------------------------------------------------------------
// Purpose: A trigger that pushes the player, NPCs, or objects.
//-----------------------------------------------------------------------------
[LinkEntityToClass("trigger_push")]
public class TriggerPush : BaseTrigger
{
	public Vector3 PushDir;

	float AlternateTicksFix; // Scale factor to apply to the push speed when running with alternate ticks
	float PushSpeed;

	public static readonly new DataMap DataDesc = new(typeof(TriggerPush), BaseTrigger.DataDesc, [
		DEFINE<TriggerPush>.KEYFIELD(nameof(PushDir), FieldType.Vector, "pushdir"),
		DEFINE<TriggerPush>.KEYFIELD(nameof(AlternateTicksFix), FieldType.Float, "alternateticksfix"),
		//DEFINE_FIELD( m_flPushSpeed, FIELD_FLOAT ),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	//-----------------------------------------------------------------------------
	// Purpose: Called when spawning, after keyvalues have been handled.
	//-----------------------------------------------------------------------------
	public override void Spawn() {
		// Convert pushdir from angles to a vector
		QAngle angPushDir = new(PushDir.X, PushDir.Y, PushDir.Z);
		MathLib.AngleVectors(angPushDir, out Vector3 vecAbsDir);

		// Transform the vector into entity space
		Span<float> push = stackalloc float[3];
		MathLib.VectorIRotate([vecAbsDir.X, vecAbsDir.Y, vecAbsDir.Z], EntityToWorldTransform(), push);
		PushDir = new(push[0], push[1], push[2]);

		base.Spawn();

		InitTrigger();

		if (Speed == 0)
			Speed = 100;
	}

	//-----------------------------------------------------------------------------
	//-----------------------------------------------------------------------------
	public override void Activate() {
		// Fix problems with triggers pushing too hard under sv_alternateticks.
		// This is somewhat hacky, but it's simple and we're really close to shipping.
		ConVarRef sv_alternateticks = new("sv_alternateticks");
		if ((AlternateTicksFix != 0) && sv_alternateticks.GetBool())
			PushSpeed = Speed * AlternateTicksFix;
		else
			PushSpeed = Speed;

		base.Activate();
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	// Input  : *pOther -
	//-----------------------------------------------------------------------------
	public override void Touch(BaseEntity? other) {
		if (other == null || !other.IsSolid() || (other.GetMoveType() == Source.MoveType.Push || other.GetMoveType() == Source.MoveType.None))
			return;

		if (!PassesTriggerFilters(other))
			return;

		// FIXME: If something is hierarchically attached, should we try to push the parent?
		if (other.GetMoveParent() != null)
			return;

		// Transform the push dir into global space
		MathLib.VectorRotate(PushDir, EntityToWorldTransform(), out Vector3 vecAbsDir);

		// Instant trigger, just transfer velocity and remove
		if (HasSpawnFlags(SF.PushOnce)) {
			other.ApplyAbsVelocityImpulse(PushSpeed * vecAbsDir);

			if (vecAbsDir.Z > 0)
				other.SetGroundEntity(null);
			Util.Remove(this);
			return;
		}

		switch (other.GetMoveType()) {
			case Source.MoveType.None:
			case Source.MoveType.Push:
			case Source.MoveType.Noclip:
				break;

			case Source.MoveType.VPhysics: {
					IPhysicsObject? phys = other.VPhysicsGetObject();
					if (phys != null) {
						// UNDONE: Assume the velocity is for a 100kg object, scale with mass
						phys.ApplyForceCenter(PushSpeed * vecAbsDir * 100.0f * (float)gpGlobals.FrameTime);
						return;
					}
				}
				break;

			default: {
#if HL2_DLL
					// HACK HACK  HL2 players on ladders will only be disengaged if the sf is set, otherwise no push occurs.
					if (other.IsPlayer() &&
						 other.GetMoveType() == Source.MoveType.Ladder) {
						if (!HasSpawnFlags(SF.PushAffectPlayerOnLadder)) {
							// Ignore the push
							return;
						}
					}
#endif

					Vector3 vecPush = (PushSpeed * vecAbsDir);
					if ((other.GetFlags() & EntityFlags.BaseVelocity) != 0)
						vecPush = vecPush + other.GetBaseVelocity();
					if (vecPush.Z > 0 && (other.GetFlags() & EntityFlags.OnGround) != 0) {
						other.SetGroundEntity(null);
						Vector3 origin = other.GetAbsOrigin();
						origin.Z += 1.0f;
						other.SetAbsOrigin(origin);
					}

#if HL1_DLL
					// Apply the z velocity as a force so it counteracts gravity properly
					Vector3 vecImpulse = new(0, 0, vecPush.Z * 0.025f);//magic hack number

					other.ApplyAbsVelocityImpulse(vecImpulse);

					// apply x, y as a base velocity so we travel at constant speed on conveyors
					vecPush.Z = 0;
#endif

					other.SetBaseVelocity(vecPush);
					other.AddFlag(EntityFlags.BaseVelocity);
				}
				break;
		}
	}
}

//-----------------------------------------------------------------------------
// Teleport trigger
//-----------------------------------------------------------------------------
[LinkEntityToClass("trigger_teleport")]
public class TriggerTeleport : BaseTrigger
{
	[Flags]
	public enum SF
	{
		PreserveAngles = 0x20,  // Preserve angles even when a local landmark is not specified
	}

	public string? Landmark;

	public static readonly new DataMap DataDesc = new(typeof(TriggerTeleport), BaseTrigger.DataDesc, [
		DEFINE<TriggerTeleport>.KEYFIELD(nameof(Landmark), FieldType.String, "landmark"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void Spawn() {
		InitTrigger();
	}

	//-----------------------------------------------------------------------------
	// Purpose: Teleports the entity that touched us to the location of our target,
	//			setting the toucher's angles to our target's angles if they are a
	//			player.
	//
	//			If a landmark was specified, the toucher is offset from the target
	//			by their initial offset from the landmark and their angles are
	//			left alone.
	//
	// Input  : pOther - The entity that touched us.
	//-----------------------------------------------------------------------------
	public override void Touch(BaseEntity? other) {
		BaseEntity? entTarget = null;

		if (other == null || !PassesTriggerFilters(other))
			return;

		// The activator and caller are the same
		entTarget = gEntList.FindEntityByName(entTarget, Target, null, other, other);
		if (entTarget == null)
			return;

		//
		// If a landmark was specified, offset the player relative to the landmark.
		//
		BaseEntity? entLandmark = null;
		Vector3 vecLandmarkOffset = new(0, 0, 0);
		if (Landmark != null) {
			// The activator and caller are the same
			entLandmark = gEntList.FindEntityByName(entLandmark, Landmark, null, other, other);
			if (entLandmark != null)
				vecLandmarkOffset = other.GetAbsOrigin() - entLandmark.GetAbsOrigin();
		}

		other.SetGroundEntity(null);

		Vector3 tmp = entTarget.GetAbsOrigin();

		if (entLandmark == null && other.IsPlayer()) {
			// make origin adjustments in case the teleportee is a player. (origin in center, not at feet)
			tmp.Z -= other.WorldAlignMins().Z;
		}

		//
		// Only modify the toucher's angles and zero their velocity if no landmark was specified.
		//
		QAngle? angles = null;
		Vector3? velocity = null;

		if (entLandmark == null && !HasSpawnFlags(SF.PreserveAngles)) {
			angles = entTarget.GetAbsAngles();

#if HL1_DLL
			velocity = new Vector3(0, 0, 0);
#else
			velocity = null;    //BUGBUG - This does not set the player's velocity to zero!!!
#endif
		}

		tmp += vecLandmarkOffset;
		other.Teleport(tmp, angles, velocity);
	}
}

//-----------------------------------------------------------------------------
// Teleport Relative trigger
//-----------------------------------------------------------------------------
[LinkEntityToClass("trigger_teleport_relative")]
public class TriggerTeleportRelative : BaseTrigger
{
	public Vector3 TeleportOffset;

	public static readonly new DataMap DataDesc = new(typeof(TriggerTeleportRelative), BaseTrigger.DataDesc, [
		DEFINE<TriggerTeleportRelative>.KEYFIELD(nameof(TeleportOffset), FieldType.Vector, "teleportoffset")
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void Spawn() {
		InitTrigger();
	}

	public override void Touch(BaseEntity? other) {
		if (other == null || !PassesTriggerFilters(other))
			return;

		Vector3 finalPos = TeleportOffset + WorldSpaceCenter();
		Vector3 momentum = vec3_origin;

		other.Teleport(finalPos, null, momentum);
	}
}

//-----------------------------------------------------------------------------
// Purpose: Saves the game when the player touches the trigger. Can be enabled or disabled
//-----------------------------------------------------------------------------
[LinkEntityToClass("trigger_togglesave")]
public class TriggerToggleSave : BaseTrigger
{
	public static readonly new DataMap DataDesc = new(typeof(TriggerToggleSave), BaseTrigger.DataDesc, [
		DEFINE<TriggerToggleSave>.KEYFIELD(nameof(Disabled), FieldType.Boolean, "StartDisabled"),

		DEFINE<TriggerToggleSave>.INPUTFUNC(FieldType.Void, "Enable", nameof(InputEnable), (INPUTFUNCPTR)((self, data) => ((TriggerToggleSave)self).InputEnable(data))),
		DEFINE<TriggerToggleSave>.INPUTFUNC(FieldType.Void, "Disable", nameof(InputDisable), (INPUTFUNCPTR)((self, data) => ((TriggerToggleSave)self).InputDisable(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void InputEnable(InputData inputdata) {
		Disabled = false;
	}

	public override void InputDisable(InputData inputdata) {
		Disabled = true;
	}

	//-----------------------------------------------------------------------------
	// Purpose: Called when spawning, after keyvalues have been set.
	//-----------------------------------------------------------------------------
	public override void Spawn() {
		if (g_pGameRules.IsDeathmatch()) {
			Util.Remove(this);
			return;
		}

		InitTrigger();
	}

	//-----------------------------------------------------------------------------
	// Purpose: Performs the autosave when the player touches us.
	// Input  : pOther -
	//-----------------------------------------------------------------------------
	public override void Touch(BaseEntity? other) {
		if (Disabled)
			return;

		// Only save on clients
		if (other == null || !other.IsPlayer())
			return;

		// Can be re-enabled
		Disabled = true;

		engine.ServerCommand("autosave\n");
	}
}

//-----------------------------------------------------------------------------
// Purpose: Saves the game when the player touches the trigger.
//-----------------------------------------------------------------------------
[LinkEntityToClass("trigger_autosave")]
public class TriggerSave : BaseTrigger
{
	bool ForceNewLevelUnit;
	float DangerousTimer;
	int MinHitPoints;

	public static readonly new DataMap DataDesc = new(typeof(TriggerSave), BaseTrigger.DataDesc, [
		DEFINE<TriggerSave>.KEYFIELD(nameof(ForceNewLevelUnit), FieldType.Boolean, "NewLevelUnit"),
		DEFINE<TriggerSave>.KEYFIELD(nameof(MinHitPoints), FieldType.Integer, "MinimumHitPoints"),
		DEFINE<TriggerSave>.KEYFIELD(nameof(DangerousTimer), FieldType.Float, "DangerousTimer"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	//-----------------------------------------------------------------------------
	// Purpose: Called when spawning, after keyvalues have been set.
	//-----------------------------------------------------------------------------
	public override void Spawn() {
		if (g_pGameRules.IsDeathmatch()) {
			Util.Remove(this);
			return;
		}

		InitTrigger();
	}

	//-----------------------------------------------------------------------------
	// Purpose: Performs the autosave when the player touches us.
	// Input  : pOther -
	//-----------------------------------------------------------------------------
	public override void Touch(BaseEntity? other) {
		// Only save on clients
		if (other == null || !other.IsPlayer())
			return;

		if (DangerousTimer != 0.0f) {
			if (g_ServerGameDLL.AutoSaveDangerousTime != 0.0f && g_ServerGameDLL.AutoSaveDangerousTime >= gpGlobals.CurTime) {
				// A previous dangerous auto save was waiting to become safe
				BasePlayer? player = Util.PlayerByIndex(1);

				if (player != null && (player.GetDeathTime() == 0.0f || player.GetDeathTime() > gpGlobals.CurTime)) {
					// The player isn't dead, so make the dangerous auto save safe
					engine.ServerCommand("autosavedangerousissafe\n");
				}
			}
		}

		// this is a one-way transition - there is no way to return to the previous map.
		if (ForceNewLevelUnit)
			engine.ClearSaveDir();
		Util.Remove(this);

		if (DangerousTimer != 0.0f) {
			// There's a dangerous timer. Save if we have enough hitpoints.
			BasePlayer? player = Util.PlayerByIndex(1);

			if (player != null && player.GetHealth() >= MinHitPoints) {
				engine.ServerCommand("autosavedangerous\n");
				g_ServerGameDLL.AutoSaveDangerousTime = gpGlobals.CurTime + DangerousTimer;
			}
		}
		else
			engine.ServerCommand("autosave\n");
	}
}

[LinkEntityToClass("trigger_gravity")]
public class TriggerGravity : BaseTrigger
{
	public override void Spawn() {
		base.Spawn();
		InitTrigger();
		SetTouch(GravityTouch);
	}

	public void GravityTouch(BaseEntity? other) {
		// Only save on clients
		if (other == null || !other.IsPlayer())
			return;

		other.SetGravity(GetGravity());
	}
}

//-----------------------------------------------------------------------------
// Purpose: Starts/stops cd audio tracks
//-----------------------------------------------------------------------------
public class TriggerCDAudio : BaseTrigger
{
	//-----------------------------------------------------------------------------
	// Purpose: Changes tracks or stops CD when player touches
	// Input  : pOther - The entity that touched us.
	//-----------------------------------------------------------------------------
	public override void Touch(BaseEntity? other) {
		if (other == null || !other.IsPlayer())
			return;

		PlayTrack();
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	//-----------------------------------------------------------------------------
	public override void Spawn() {
		base.Spawn();
		InitTrigger();
	}

	public override void Use(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		PlayTrack();
	}

	//-----------------------------------------------------------------------------
	// Purpose: Issues a client command to play a given CD track. Called from
	//			trigger_cdaudio and target_cdaudio.
	// Input  : iTrack - Track number to play.
	//-----------------------------------------------------------------------------
	static void PlayCDTrack(int track) {
		Edict? client;

		// manually find the single player.
		client = engine.PEntityOfEntIndex(1);

		Assert(gpGlobals.MaxClients == 1);

		// Can't play if the client is not connected!
		if (client == null)
			return;

		// UNDONE: Move this to engine sound
		if (track < -1 || track > 30) {
			Warning($"TriggerCDAudio - Track {track} out of range\n");
			return;
		}

		if (track == -1)
			engine.ClientCommand(client, "cd pause\n");
		else
			engine.ClientCommand(client, $"cd play {track,3}\n");
	}

	// only plays for ONE client, so only use in single play!
	void PlayTrack() {
		PlayCDTrack(Health);

		SetTouch(null);
		Util.Remove(this);
	}
}

//-----------------------------------------------------------------------------
// Purpose: Measures the proximity to a specified entity of any entities within
//			the trigger, provided they are within a given radius of the specified
//			entity. The nearest entity distance is output as a number from [0 - 1].
//-----------------------------------------------------------------------------
[LinkEntityToClass("trigger_proximity")]
public class TriggerProximity : BaseTrigger
{
	protected EHANDLE MeasureTarget = new();
	protected string? MeasureTargetName;        // The entity from which we measure proximities.
	protected float Radius;          // The radius around the measure target that we measure within.
	protected int Touchers;          // Number of entities touching us.

	// Outputs
	protected OutputFloat NearestEntityDistance = new();

	public static readonly new DataMap DataDesc = new(typeof(TriggerProximity), BaseTrigger.DataDesc, [
		// Functions
		DEFINE<TriggerProximity>.FUNCTION(nameof(MeasureThink)),

		// Keys
		DEFINE<TriggerProximity>.KEYFIELD(nameof(MeasureTargetName), FieldType.String, "measuretarget"),
		DEFINE<TriggerProximity>.FIELD(nameof(MeasureTarget), FieldType.EHandle),
		DEFINE<TriggerProximity>.KEYFIELD(nameof(Radius), FieldType.Float, "radius"),
		DEFINE<TriggerProximity>.FIELD(nameof(Touchers), FieldType.Integer),

		// Outputs
		DEFINE<TriggerProximity>.OUTPUT(nameof(NearestEntityDistance), "NearestEntityDistance", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	//-----------------------------------------------------------------------------
	// Purpose: Called when spawning, after keyvalues have been handled.
	//-----------------------------------------------------------------------------
	public override void Spawn() {
		// Avoid divide by zero in MeasureThink!
		if (Radius == 0)
			Radius = 32;

		InitTrigger();
	}

	//-----------------------------------------------------------------------------
	// Purpose: Called after all entities have spawned and after a load game.
	//			Finds the reference point from which to measure.
	//-----------------------------------------------------------------------------
	public override void Activate() {
		base.Activate();
		MeasureTarget.Set(gEntList.FindEntityByName(null, MeasureTargetName));

		//
		// Disable our Touch function if we were given a bad measure target.
		//
		BaseEntity? measureTarget = (BaseEntity?)MeasureTarget.Get();
		if ((measureTarget == null) || (measureTarget.Edict() == null))
			Warning("TriggerProximity - Missing measure target or measure target with no origin!\n");
	}

	//-----------------------------------------------------------------------------
	// Purpose: Decrements the touch count and cancels the think if the count reaches
	//			zero.
	// Input  : pOther -
	//-----------------------------------------------------------------------------
	public override void StartTouch(BaseEntity? other) {
		base.StartTouch(other);

		if (other != null && PassesTriggerFilters(other)) {
			Touchers++;

			SetThink(MeasureThink);
			SetNextThink(gpGlobals.CurTime);
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose: Decrements the touch count and cancels the think if the count reaches
	//			zero.
	// Input  : pOther -
	//-----------------------------------------------------------------------------
	public override void EndTouch(BaseEntity? other) {
		base.EndTouch(other);

		if (other != null && PassesTriggerFilters(other)) {
			Touchers--;

			if (Touchers == 0) {
				SetThink(null);
				SetNextThink(TICK_NEVER_THINK);
			}
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose: Think function called every frame as long as we have entities touching
	//			us that we care about. Finds the closest entity to the measure
	//			target and outputs the distance as a normalized value from [0..1].
	//-----------------------------------------------------------------------------
	void MeasureThink() {
		BaseEntity? measureTarget = (BaseEntity?)MeasureTarget.Get();
		if ((measureTarget == null) || (measureTarget.Edict() == null)) {
			SetThink(null);
			SetNextThink(TICK_NEVER_THINK);
			return;
		}

		//
		// Traverse our list of touchers and find the entity that is closest to the
		// measure target.
		//
		float fMinDistance = Radius + 100;
		BaseEntity? nearestEntity = null;

		TouchLink? root = GetTouchLinkRoot();
		if (root != null) {
			TouchLink link = root.NextLink;
			while (link != root) {
				BaseEntity? entity = (BaseEntity?)link.EntityTouched.Get();

				// If this is an entity that we care about, check its distance.
				if ((entity != null) && PassesTriggerFilters(entity)) {
					float flDistance = (entity.GetLocalOrigin() - measureTarget.GetLocalOrigin()).Length();
					if (flDistance < fMinDistance) {
						fMinDistance = flDistance;
						nearestEntity = entity;
					}
				}

				link = link.NextLink;
			}
		}

		// Update our output with the nearest entity distance, normalized to [0..1].
		if (fMinDistance <= Radius) {
			fMinDistance /= Radius;
			if (fMinDistance != NearestEntityDistance.Get())
				NearestEntityDistance.Set(fMinDistance, nearestEntity, this);
		}

		SetNextThink(gpGlobals.CurTime);
	}
}

// ##################################################################################
//	>> TriggerImpact
//
//  Blows physics objects in the trigger
//
// ##################################################################################
[LinkEntityToClass("trigger_impact")]
public class TriggerImpact : TriggerMultiple
{
	const float TRIGGERIMPACT_VIEWKICK_SCALE = 0.1f;

	float Magnitude;
	float Noise;
	float Viewkick;

	// Outputs
	OutputVector OutputForce = new();      // Output force in case anyone else wants to use it

	public static readonly new DataMap DataDesc = new(typeof(TriggerImpact), TriggerMultiple.DataDesc, [
		DEFINE<TriggerImpact>.KEYFIELD(nameof(Magnitude), FieldType.Float, "Magnitude"),
		DEFINE<TriggerImpact>.KEYFIELD(nameof(Noise), FieldType.Float, "Noise"),
		DEFINE<TriggerImpact>.KEYFIELD(nameof(Viewkick), FieldType.Float, "Viewkick"),

		// Inputs
		DEFINE<TriggerImpact>.INPUTFUNC(FieldType.Void, "Impact", nameof(InputImpact), (INPUTFUNCPTR)((self, data) => ((TriggerImpact)self).InputImpact(data))),
		DEFINE<TriggerImpact>.INPUTFUNC(FieldType.Float, "SetMagnitude", nameof(InputSetMagnitude), (INPUTFUNCPTR)((self, data) => ((TriggerImpact)self).InputSetMagnitude(data))),

		// Outputs
		DEFINE<TriggerImpact>.OUTPUT(nameof(OutputForce), "ImpactForce", eventFuncs),

		// Function Pointers
		DEFINE<TriggerImpact>.FUNCTION(nameof(Disable)),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	//------------------------------------------------------------------------------
	// Purpose:
	//------------------------------------------------------------------------------
	public override void Spawn() {
		// Clamp date in case user made an error
		Noise = Math.Clamp(Noise, 0.0f, 1.0f);
		Viewkick = Math.Clamp(Viewkick, 0.0f, 1.0f);

		// Always start disabled
		Disabled = true;
		base.Spawn();
	}

	//------------------------------------------------------------------------------
	// Purpose:
	//------------------------------------------------------------------------------
	public void InputImpact(InputData inputdata) {
		// Output the force vector in case anyone else wants to use it
		MathLib.AngleVectors(GetLocalAngles(), out Vector3 vDir);
		OutputForce.Set(Magnitude * vDir, inputdata.Activator, inputdata.Caller);

		// Enable long enough to throw objects inside me
		Enable();
		SetNextThink(gpGlobals.CurTime + 0.1f);
		SetThink(Disable);
	}

	//------------------------------------------------------------------------------
	// Purpose:
	//------------------------------------------------------------------------------
	public override void StartTouch(BaseEntity? other) {
		//If the entity is valid and has physics, hit it
		if ((other != null) && (other.VPhysicsGetObject() != null)) {
			MathLib.AngleVectors(GetLocalAngles(), out Vector3 vDir);
			vDir += new Vector3(random.RandomFloat(-Noise, Noise), random.RandomFloat(-Noise, Noise), random.RandomFloat(-Noise, Noise));
			other.VPhysicsGetObject()!.ApplyForceCenter(Magnitude * vDir);
		}

		// If the player, so a view kick
		if (other != null && other.IsPlayer() && MathF.Abs(Magnitude) > 0) {
			MathLib.AngleVectors(GetLocalAngles(), out Vector3 vDir);

			float flPunch = -Viewkick * Magnitude * TRIGGERIMPACT_VIEWKICK_SCALE;
			((BasePlayer)other).ViewPunch(new QAngle(vDir.Y * flPunch, 0, vDir.X * flPunch));
		}
	}

	//------------------------------------------------------------------------------
	// Purpose:
	//------------------------------------------------------------------------------
	public void InputSetMagnitude(InputData inputdata) {
		Magnitude = inputdata.Value.Float();
	}
}

[LinkEntityToClass("trigger_serverragdoll")]
public class ServerRagdollTrigger : BaseTrigger
{
	public override void Spawn() {
		base.Spawn();

		InitTrigger();
	}

	public override void StartTouch(BaseEntity? other) {
		base.StartTouch(other);

		if (other == null || other.IsPlayer())
			return;

		if (other is BaseCombatCharacter combatChar)
			combatChar.ForceServerRagdoll = true;
	}

	public override void EndTouch(BaseEntity? other) {
		base.EndTouch(other);

		if (other == null || other.IsPlayer())
			return;

		if (other is BaseCombatCharacter combatChar)
			combatChar.ForceServerRagdoll = false;
	}
}

//-----------------------------------------------------------------------------
// Purpose: A trigger that adds impulse to touching entities
//-----------------------------------------------------------------------------
public class TriggerApplyImpulse : BaseTrigger
{
	Vector3 ImpulseDir;
	float Force;

	public static readonly new DataMap DataDesc = new(typeof(TriggerApplyImpulse), BaseTrigger.DataDesc, [
		DEFINE<TriggerApplyImpulse>.KEYFIELD(nameof(ImpulseDir), FieldType.Vector, "impulse_dir"),
		DEFINE<TriggerApplyImpulse>.KEYFIELD(nameof(Force), FieldType.Float, "force"),
		DEFINE<TriggerApplyImpulse>.INPUTFUNC(FieldType.Void, "ApplyImpulse", nameof(InputApplyImpulse), (INPUTFUNCPTR)((self, data) => ((TriggerApplyImpulse)self).InputApplyImpulse(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public TriggerApplyImpulse() {
		Force = 300.0f;
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	//-----------------------------------------------------------------------------
	public override void Spawn() {
		// Convert pushdir from angles to a vector
		QAngle angPushDir = new(ImpulseDir.X, ImpulseDir.Y, ImpulseDir.Z);
		MathLib.AngleVectors(angPushDir, out Vector3 vecAbsDir);

		// Transform the vector into entity space
		Span<float> impulse = stackalloc float[3];
		MathLib.VectorIRotate([vecAbsDir.X, vecAbsDir.Y, vecAbsDir.Z], EntityToWorldTransform(), impulse);
		ImpulseDir = new(impulse[0], impulse[1], impulse[2]);

		base.Spawn();

		InitTrigger();
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	//-----------------------------------------------------------------------------
	public void InputApplyImpulse(InputData inputdata) {
		Vector3 vecImpulse = Force * ImpulseDir;
		for (int i = 0; i < TouchingEntities.Count; i++) {
			BaseEntity? touching = (BaseEntity?)TouchingEntities[i].Get();
			touching?.ApplyAbsVelocityImpulse(vecImpulse);
		}
	}
}

#if HL1_DLL
//----------------------------------------------------------------------------------
// func_friction
//----------------------------------------------------------------------------------
[LinkEntityToClass("func_friction")]
public class FrictionModifier : BaseTrigger
{
	float FrictionFraction;

	public static readonly new DataMap DataDesc = new(typeof(FrictionModifier), BaseTrigger.DataDesc, [
		DEFINE<FrictionModifier>.FIELD(nameof(FrictionFraction), FieldType.Float),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override EntityCapabilities ObjectCaps() => base.ObjectCaps() & ~EntityCapabilities.AcrossTransition;

	// Modify an entity's friction
	public override void Spawn() {
		base.Spawn();

		InitTrigger();
	}

	// Sets toucher's friction to m_frictionFraction (1.0 = normal friction)
	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		if (FStrEq(keyName, "modifier"))
			FrictionFraction = strtof(value, out _) / 100.0f;
		else
			base.KeyValue(keyName, value);
		return true;
	}

	public override void StartTouch(BaseEntity? other) {
		if (other != null && !other.IsPlayer())     // ignore player
			other.SetFriction(FrictionFraction);
	}

	public override void EndTouch(BaseEntity? other) {
		if (other != null && !other.IsPlayer())     // ignore player
			other.SetFriction(1.0f);
	}
}
#endif
