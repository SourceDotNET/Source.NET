using Game.Server;
using Game.Shared;

using Source.Common;
using Source.Common.GUI;


namespace Game.Server;

using Source;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Diagnostics;
using System.Numerics;

using DEFINE = Source.DEFINE<Game.Server.PhysicsProp>;
using FIELD_BPD = Source.FIELD<BasePropDoor>;
using FIELD_DP = Source.FIELD<DynamicProp>;
using FIELD_PBM = Source.FIELD<PhysBoxMultiplayer>;
using FIELD_PP = Source.FIELD<PhysicsProp>;
using FIELD_PPM = Source.FIELD<PhysicsPropMultiplayer>;

public class BaseProp : BaseAnimating
{
	public override void Spawn() {
		ReadOnlySpan<char> szModel = GetModelName();
		if (szModel.IsEmpty) {
			Vector3 org = GetAbsOrigin();
			Warning($"prop at {org.X:0} {org.Y:0} {org.Z:0} missing modelname\n");
			Util.Remove(this);
			return;
		}

		PrecacheModel(szModel);
		Precache();
		SetModel(szModel);

		// Load this prop's data from the propdata file
		ParsePropData();

		SetMoveType(Source.MoveType.Push);
		m_takedamage = (byte)Damage.No;
		SetNextThink(TICK_NEVER_THINK);

		AnimTime = gpGlobals.CurTime;
		PlaybackRate = 0.0;
		SetCycle(0);
	}
	public override void Precache() { }
	public override void Activate() { }
	public void CalculateBlockLOS() { }
	public void ParsePropData() { }
	public virtual new bool IsAlive() => false;
	public virtual bool OverridePropdata() => true;
}

[NetworkName("CBreakableProp")]
public class BreakableProp : BaseProp, IBreakableWithPropData
{
	public static readonly SendTable DT_BreakableProp = new(DT_BaseAnimating, []);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_BreakableProp);

	public bool PropDataOverrodeBlockLOS() => BlockLOSSetByPropData;

	public void DisableAutoFade() {
		FadeScale = 0;
		DefaultFadeScale = 0;
	}

	public void SetDmgModBullet(float dmgMod) => DmgModBullet = dmgMod;
	public void SetDmgModClub(float dmgMod) => DmgModClub = dmgMod;
	public void SetDmgModExplosive(float dmgMod) => DmgModExplosive = dmgMod;
	public float GetDmgModBullet() => DmgModBullet;
	public float GetDmgModClub() => DmgModClub;
	public float GetDmgModExplosive() => DmgModExplosive;
	public void SetExplosiveRadius(float radius) => ExplodeRadius = radius;
	public void SetExplosiveDamage(float damage) => ExplodeDamage = damage;
	public float GetExplosiveRadius() => ExplodeRadius;
	public float GetExplosiveDamage() => ExplodeDamage;
	public void SetPhysicsDamageTable(ReadOnlySpan<char> tableName) => PhysicsDamageTableName = tableName.IsEmpty ? null : new(tableName);
	public ReadOnlySpan<char> GetPhysicsDamageTable() => PhysicsDamageTableName;
	public void SetBreakableModel(ReadOnlySpan<char> model) => BreakableModel = model.IsEmpty ? null : new(model);
	public ReadOnlySpan<char> GetBreakableModel() => BreakableModel;
	public void SetBreakableSkin(int skin) => BreakableSkin = skin;
	public int GetBreakableSkin() => BreakableSkin;
	public void SetBreakableCount(int count) => BreakableCount = count;
	public int GetBreakableCount() => BreakableCount;
	public void SetMaxBreakableSize(int size) => MaxBreakableSize = size;
	public int GetMaxBreakableSize() => MaxBreakableSize;
	public void SetPropDataBlocksLOS(bool blocksLOS) { BlockLOSSetByPropData = true; SetBlocksLOS(blocksLOS); }
	public void SetPropDataIsAIWalkable(bool blocksLOS) => SetAIWalkable(blocksLOS);
	public void SetBasePropData(ReadOnlySpan<char> propdata) => BasePropData = propdata.IsEmpty ? null : new(propdata);
	public ReadOnlySpan<char> GetBasePropData() => BasePropData;
	public void SetInteraction(PropDataInteractions interaction) => Interactions |= (1 << (int)interaction);
	public bool HasInteraction(PropDataInteractions interaction) => (Interactions & (1 << (int)interaction)) != 0;
	public void SetPhysicsMode(PhysicsMultiplayerMode mode) => m_iPhysicsMode = mode;
	public PhysicsMultiplayerMode GetPhysicsMode() => m_iPhysicsMode;
	public void SetMultiplayerBreakMode(MultiplayerBreak mode) => MpBreakMode = mode;
	public MultiplayerBreak GetMultiplayerBreakMode() => MpBreakMode;

	protected float DmgModBullet;
	protected float DmgModClub;
	protected float DmgModExplosive;
	protected string? PhysicsDamageTableName;
	protected string? BreakableModel;
	protected int BreakableSkin;
	protected int BreakableCount;
	protected int MaxBreakableSize;
	protected string? BasePropData;
	protected int Interactions;
	protected float ExplodeDamage;
	protected float ExplodeRadius;
	protected PhysicsMultiplayerMode m_iPhysicsMode;
	protected MultiplayerBreak MpBreakMode;
	bool BlockLOSSetByPropData;
	float DefaultFadeScale;
}

[LinkEntityToClass("func_physbox_multiplayer")]
[NetworkName("CPhysBoxMultiplayer")]
public class PhysBoxMultiplayer : PhysBox, IMultiplayerPhysics
{
	public static readonly SendTable DT_PhysBoxMultiplayer = new(DT_PhysBox, [
		SendPropInt(FIELD_PBM.OF(nameof(PhysicsMode)), 1, PropFlags.Unsigned),
		SendPropFloat(FIELD_PBM.OF(nameof(Mass)), 0, PropFlags.NoScale)
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_PhysBoxMultiplayer);
	[NetworkName("m_iPhysicsMode")]
	public int PhysicsMode;
	[NetworkName("m_fMass")]
	public float Mass;

	public PhysicsMultiplayerMode GetMultiplayerPhysicsMode() => (PhysicsMultiplayerMode)PhysicsMode;
	public float GetMass() => Mass;
	public bool IsAsleep() => VPhysicsGetObject()!.IsAsleep();
}

[LinkEntityToClass("physics_prop")]
[LinkEntityToClass("prop_physics")]
[LinkEntityToClass("prop_physics_override")]
[LinkEntityToClass("prop_physics_multiplayer")]
[LinkEntityToClass("item_crate")]
[LinkEntityToClass("prop_exploding_barrel")]
[LinkEntityToClass("prop_flare")]
[LinkEntityToClass("prop_weighted_cube")]
[NetworkName("CPhysicsProp")]
public class PhysicsProp : BreakableProp
{
	public static readonly SendTable DT_PhysicsProp = new(DT_BreakableProp, [
		SendPropBool(FIELD_PP.OF(nameof(Awake)))
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_PhysicsProp);
	[NetworkName("m_bAwake")]
	public bool Awake;

	public static readonly new DataMap DataDesc = new(typeof(PhysicsProp), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(MassScale), FieldType.Float, "massscale"),
		DEFINE.KEYFIELD(nameof(InertiaScale), FieldType.Float, "inertiascale"),
		DEFINE.KEYFIELD(nameof(DamageType), FieldType.Integer, "Damagetype"),
		DEFINE.KEYFIELD(nameof(OverrideScript), FieldType.String, "overridescript"),
		DEFINE.KEYFIELD(nameof(DamageToEnableMotion), FieldType.Integer, "damagetoenablemotion"),
		DEFINE.KEYFIELD(nameof(ForceToEnableMotion), FieldType.Float, "forcetoenablemotion"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public static int g_ActiveGibCount = 0;

	float MassScale;
	float InertiaScale;
	int DamageType;
	string? OverrideScript;
	int DamageToEnableMotion;
	float ForceToEnableMotion;

	public override void Term() {
		if (HasSpawnFlags(SF_PHYSPROP_IS_GIB))
			g_ActiveGibCount--;

		base.Term();
	}

	public override void Spawn() {
		if (HasSpawnFlags(SF_PHYSPROP_IS_GIB))
			g_ActiveGibCount++;

		// Condense classnames to one, except for "prop_physics_override"
		if (FClassnameIs(this, "physics_prop"))
			SetClassname("prop_physics");

		base.Spawn();

		if (IsMarkedForDeletion())
			return;

		// Now condense all classnames to one
		if (FClassnameIs(this, "prop_physics_override"))
			SetClassname("prop_physics");

		if (HasSpawnFlags(SF_PHYSPROP_DEBRIS) || HasInteraction(PropDataInteractions.PhysgunCreateFlare))
			SetCollisionGroup(HasSpawnFlags(SF_PHYSPROP_FORCE_TOUCH_TRIGGERS) ? Source.CollisionGroup.DebrisTrigger : Source.CollisionGroup.Debris);

		if (HasSpawnFlags(SF_PHYSPROP_NO_ROTORWASH_PUSH))
			AddEFlags(EFL.NoRotorWashPush);

		CreateVPhysics();

		if (!PropDataOverrodeBlockLOS())
			CalculateBlockLOS();

		if (((ReadOnlySpan<char>)GetModelName()).Equals("models/props_c17/door01_left.mdl", StringComparison.Ordinal)) {
			SetFadeDistance(-1, 0);
			DisableAutoFade();
		}
	}

	public static bool PropIsGib(BaseEntity entity) {
		if (FClassnameIs(entity, "prop_physics")) {
			PhysicsProp prop = (PhysicsProp)entity;
			return prop.IsGib();
		}
		return false;
	}

	public bool IsGib() => (SpawnFlags & SF_PHYSPROP_IS_GIB) != 0;

	public virtual bool CreateVPhysics() {
		bool asleep = HasSpawnFlags(SF_PHYSPROP_START_ASLEEP);

		Solid tmpSolid = default;
		PhysModelParseSolid(ref tmpSolid, this, GetModelIndex());

		if (MassScale > 0)
			tmpSolid.Params.Mass *= MassScale;

		if (InertiaScale > 0) {
			tmpSolid.Params.Inertia *= InertiaScale;
			if (tmpSolid.Params.Inertia < 0.5)
				tmpSolid.Params.Inertia = 0.5f;
		}

		PhysGetMassCenterOverride(this, modelinfo.GetVCollide(GetModelIndex()), ref tmpSolid);
		if (HasSpawnFlags(SF_PHYSPROP_NO_COLLISIONS))
			tmpSolid.Params.EnableCollisions = false;

		PhysSolidOverride(ref tmpSolid, OverrideScript);

		IPhysicsObject? physicsObject = VPhysicsInitNormal(SolidType.VPhysics, 0, asleep, ref tmpSolid);

		if (physicsObject == null) {
			SetSolid(SolidType.None);
			SetMoveType(Source.MoveType.None);
			Warning($"ERROR!: Can't create physics object for {GetModelName()}\n");
			return false;
		}
		else {
			if (DamageType == 1)
				PhysSetGameFlags(physicsObject, PhysicsFlags.DamageSlice);

			if (HasSpawnFlags(SF_PHYSPROP_MOTIONDISABLED) || DamageToEnableMotion > 0 || ForceToEnableMotion > 0)
				physicsObject.EnableMotion(false);
		}

		if (HasInteraction(PropDataInteractions.PhysgunLaunchSpinZ)) {
			if ((VPhysicsGetObject()!.GetGameFlags() & PhysicsFlags.DamageSlice) == 0)
				PhysSetGameFlags(physicsObject, PhysicsFlags.DamageSlice);
		}

		if (HasInteraction(PropDataInteractions.PhysgunDamageNone))
			PhysSetGameFlags(physicsObject, PhysicsFlags.NoImpactDamage);

		if (HasSpawnFlags(SF_PHYSPROP_PREVENT_PICKUP))
			PhysSetGameFlags(physicsObject, PhysicsFlags.NoPlayerPickup);

		return true;
	}

	public override EntityCapabilities ObjectCaps() {
		EntityCapabilities caps = base.ObjectCaps() | EntityCapabilities.WorldcraftEditPosition;

		if (HasSpawnFlags(SF_PHYSPROP_ENABLE_PICKUP_OUTPUT)) 
			caps |= EntityCapabilities.ImpulseUse;
		else if (BasePlayer.CanPickupObject(this, 35, 128)) {
			caps |= EntityCapabilities.ImpulseUse;

			if (hl2_episodic.GetBool() && HasInteraction(PropDataInteractions.PhysgunCreateFlare)) {
				caps |= EntityCapabilities.UseInRadius;
			}
		}

		if (HasSpawnFlags(SF_PHYSPROP_RADIUS_PICKUP)) {
			caps |= EntityCapabilities.UseInRadius;
		}

		return caps;
	}

	public override void Use(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		BasePlayer? player = ToBasePlayer(activator);
		if (player != null) {
			// if (HasSpawnFlags(SF_PHYSPROP_ENABLE_PICKUP_OUTPUT))
			// 	OnPlayerUse.FireOutput(this, this);

			player.PickupObject(this);
		}
	}

	public float GetCarryDistanceOffset() => CarryDistanceOffset;

	float CarryDistanceOffset;
}

[LinkEntityToClass("dynamic_prop")]
[LinkEntityToClass("prop_dynamic")]
[LinkEntityToClass("prop_dynamic_override")]
[LinkEntityToClass("asw_door")]
[LinkEntityToClass("dod_control_point")]
[LinkEntityToClass("env_portal_laser")]
[LinkEntityToClass("npc_personality_core")]
[LinkEntityToClass("npc_wheatley_boss")]
[LinkEntityToClass("prop_button")]
[LinkEntityToClass("prop_car_alarm")]
[LinkEntityToClass("prop_car_glass")]
[LinkEntityToClass("prop_dropper")]
[LinkEntityToClass("prop_dynamic_glow")]
[LinkEntityToClass("prop_floor_ball_button")]
[LinkEntityToClass("prop_floor_button")]
[LinkEntityToClass("prop_floor_cube_button")]
[LinkEntityToClass("prop_health_cabinet")]
[LinkEntityToClass("prop_indicator_panel")]
[LinkEntityToClass("prop_laser_catcher")]
[LinkEntityToClass("prop_laser_relay")]
[LinkEntityToClass("prop_portal_stats_display")]
[LinkEntityToClass("prop_testchamber_door")]
[LinkEntityToClass("prop_tractor_beam")]
[LinkEntityToClass("prop_under_button")]
[LinkEntityToClass("prop_under_floor_button")]
[LinkEntityToClass("prop_wall_projector")]
[LinkEntityToClass("team_control_point")]
[NetworkName("CDynamicProp")]
public class DynamicProp : BreakableProp
{
	public static readonly SendTable DT_DynamicProp = new(DT_BreakableProp, [
		SendPropBool(FIELD_DP.OF(nameof(UseHitboxesForRenderBox)))
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_DynamicProp);
	[NetworkName("m_bUseHitboxesForRenderBox")]
	public bool UseHitboxesForRenderBox;
}

[NetworkName("CPhysicsPropMultiplayer")]
public class PhysicsPropMultiplayer : PhysicsProp
{
	public static readonly SendTable DT_PhysicsPropMultiplayer = new(DT_PhysicsProp, [
		SendPropInt(FIELD_PPM.OF(nameof(PhysicsMode)), 2, PropFlags.Unsigned),
		SendPropFloat(FIELD_PPM.OF(nameof(Mass)), 0, PropFlags.NoScale),
		SendPropVector(FIELD_PPM.OF(nameof(CollisionMins)), 0, PropFlags.NoScale),
		SendPropVector(FIELD_PPM.OF(nameof(CollisionMaxs)), 0, PropFlags.NoScale),
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_PhysicsPropMultiplayer);

	[NetworkName("m_iPhysicsMode")]
	public int PhysicsMode;
	[NetworkName("m_fMass")]
	public float Mass;
	[NetworkName("m_collisionMins")]
	public Vector3 CollisionMins;
	[NetworkName("m_collisionMaxs")]
	public Vector3 CollisionMaxs;
}


[NetworkName("CBasePropDoor")]
public class BasePropDoor : DynamicProp
{
	[NetworkName("m_bLocked")]
	bool Locked;
	[NetworkName("m_eDoorState")]
	int DoorState;
	public static readonly SendTable DT_BasePropDoor = new(DT_DynamicProp, [
		SendPropBool(FIELD_BPD.OF(nameof(Locked))),
		SendPropInt(FIELD_BPD.OF(nameof(DoorState)), 3, PropFlags.Unsigned)
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_BasePropDoor);
}

[NetworkName("CPropDoorRotating")]
public class PropDoorRotating : BasePropDoor
{
	public static readonly SendTable DT_PropDoorRotating = new(DT_BasePropDoor, []);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_PropDoorRotating);
}

[LinkEntityToClass("prop_door_rotating")]
[LinkEntityToClass("dz_door")]
[LinkEntityToClass("prop_door_rotating_checkpoint")]
public class PropDoorRotatingBreakable : PropDoorRotating;

public static class Props
{
	public static int g_interactionHitByPlayerThrownPhysObj = 0;

	public static PhysicsProp? CreatePhysicsProp(ReadOnlySpan<char> modelName, in Vector3 traceStart, in Vector3 traceEnd, IHandleEntity? traceIgnore, bool requireVCollide, ReadOnlySpan<char> className = default) {
		if (className.IsStringEmpty)
			className = "physics_prop";
		MDLHandle_t h = mdlcache.FindMDL(modelName);
		if (h == MDLHANDLE_INVALID)
			return null;

		// Must have vphysics to place as a physics prop
		StudioHeader? studioHdr = mdlcache.GetStudioHdr(h);
		if (studioHdr == null)
			return null;

		// Must have vphysics to place as a physics prop
		if (requireVCollide && null == mdlcache.GetVCollide(h))
			return null;

		QAngle angles = new(0.0f, 0.0f, 0.0f);
		Vector3 vecSweepMins = studioHdr.HullMin;
		Vector3 vecSweepMaxs = studioHdr.HullMax;

		Util.TraceHull(traceStart, traceEnd, vecSweepMins, vecSweepMaxs, Mask.NPCSolid, traceIgnore, CollisionGroup.None, out GameTrace tr);

		// No hit? We're done.
		if ((tr.Fraction == 1.0 && (traceEnd - traceStart).Length() > 0.01) || tr.AllSolid) 
			return null;

		MathLib.VectorMA(tr.EndPos, 1.0f, tr.Plane.Normal, out tr.EndPos);

		bool allowPrecache = BaseEntity.IsPrecacheAllowed();
		BaseEntity.SetAllowPrecache(true);

		// Try to create entity
		PhysicsProp? prop = (PhysicsProp?)CreateEntityByName(className);
		if (prop != null) {
			Span<char> buf = stackalloc char[512];
			// Pass in standard key values
			prop.KeyValue("origin", sprintf(buf, "%f %f %f").F(tr.EndPos.X).F(tr.EndPos.Y).F(tr.EndPos.Z).ToSpan());
			prop.KeyValue("angles", sprintf(buf, "%f %f %f").F(angles.X).F(angles.Y).F(angles.Z).ToSpan());
			prop.KeyValue("model", modelName);
			prop.KeyValue("fademindist", "-1");
			prop.KeyValue("fademaxdist", "0");
			prop.KeyValue("fadescale", "1");
			prop.KeyValue("inertiaScale", "1.0");
			prop.KeyValue("physdamagescale", "0.1");
			prop.Precache();
			Util.DispatchSpawn(prop);
			prop.Activate();
		}
		BaseEntity.SetAllowPrecache(allowPrecache);

		return prop;
	}

	[ConCommand("prop_physics_create", "Creates a physics prop with a specific .mdl aimed away from where the player is looking.\n\tArguments: {.mdl name}", FCvar.Cheat)]
	public static void CC_Prop_Physics_Create(in TokenizedCommand args) {
		if (args.ArgC() != 2)
			return;

		Span<char> modelName = stackalloc char[512];
		if (!args[1].StartsWith("models/") && !args[1].StartsWith("models\\"))
			sprintf(modelName, "models/%s").S(args[1]);
		else
			strcpy(modelName, args[1]);
		StrTools.DefaultExtension(modelName, ".mdl");

		BasePlayer? player = Util.GetCommandClient();
		if (player == null)
			return;

		player.EyeVectors(out Vector3 forward);
		CreatePhysicsProp(modelName, player.EyePosition(), player.EyePosition() + forward * MAX_TRACE_LENGTH, player, true);
	}
}
