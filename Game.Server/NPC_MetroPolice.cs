using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;

namespace Game.Server;

using DEFINE = DEFINE<NPC_MetroPolice>;

[LinkEntityToClass("npc_metropolice")]
public class NPC_MetroPolice : AI_BaseActor
{
	public const int SF_METROPOLICE_SIMPLE_VERSION = 0x00020000;
	public const int SF_METROPOLICE_ALWAYS_STITCH = 0x00080000;
	public const int SF_METROPOLICE_NOCHATTER = 0x00100000;
	public const int SF_METROPOLICE_ARREST_ENEMY = 0x00200000;
	public const int SF_METROPOLICE_NO_FAR_STITCH = 0x00400000;
	public const int SF_METROPOLICE_NO_MANHACK_DEPLOY = 0x00800000;
	public const int SF_METROPOLICE_ALLOWED_TO_RESPOND = 0x01000000;
	public const int SF_METROPOLICE_MID_RANGE_ATTACK = 0x02000000;

	public const float METROPOLICE_MID_RANGE_ATTACK_RANGE = 3500.0f;

	public const int METROPOLICE_BODYGROUP_MANHACK = 1;

	public static readonly ConVar sk_metropolice_health = new("sk_metropolice_health", "0");
	public static readonly ConVar sk_metropolice_simple_health = new("sk_metropolice_simple_health", "26");

	public static readonly new DataMap DataDesc = new(typeof(NPC_MetroPolice), AI_BaseNPC.DataDesc, [
		DEFINE.KEYFIELD(nameof(WeaponDrawn), FieldType.Boolean, "weapondrawn"),
		DEFINE.KEYFIELD(nameof(Manhacks), FieldType.Integer, "manhacks"),

		DEFINE.OUTPUT(nameof(OnStunnedPlayer), "OnStunnedPlayer", eventFuncs),
		DEFINE.OUTPUT(nameof(OnCupCopped), "OnCupCopped", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public bool WeaponDrawn;
	public int Manhacks;
	bool SimpleCops;
	TimeUnit_t ChasePlayerTime;

	public OutputEvent OnStunnedPlayer = new();
	public OutputEvent OnCupCopped = new();

	public override void Precache() {
		if (HasSpawnFlags(SF_NPC_START_EFFICIENT))
			SetModelName("models/police_cheaple.mdl");
		else
			SetModelName("models/police.mdl");

		PrecacheModel(GetModelName());

		Util.PrecacheOther("npc_manhack");

		PrecacheScriptSound("NPC_Metropolice.Shove");
		PrecacheScriptSound("NPC_MetroPolice.WaterSpeech");
		PrecacheScriptSound("NPC_MetroPolice.HidingSpeech");

		base.Precache();
	}

	public override void Spawn() {
		Precache();

		SetModel(GetModelName());

		SetHullType(AI_HullType.Human);
		SetHullSizeNormal();

		SetSolid(SolidType.BBox);
		AddSolidFlags(SolidFlags.NotStandable);
		SetMoveType(Source.MoveType.Step);
		SetBloodColor(Shared.BloodColor.Red);
		SimpleCops = HasSpawnFlags(SF_METROPOLICE_SIMPLE_VERSION);
		if (HasSpawnFlags(SF_METROPOLICE_NOCHATTER))
			AddSpawnFlags(SF_NPC_GAG);

		if (!SimpleCops)
			Health = (int)sk_metropolice_health.GetFloat();
		else
			Health = (int)sk_metropolice_simple_health.GetFloat();

		FieldOfView = -0.2f;
		NPCState = NPCState.None;
		if (!HasSpawnFlags(SF_NPC_START_EFFICIENT)) {
			CapabilitiesAdd(Capability.TurnHead | Capability.AnimatedFace);
			CapabilitiesAdd(Capability.AimGun | Capability.MoveShoot);
		}
		CapabilitiesAdd(Capability.MoveGround);
		CapabilitiesAdd(Capability.UseWeapons | Capability.NoHitSquadmates);
		CapabilitiesAdd(Capability.Squad);
		CapabilitiesAdd(Capability.Duck | Capability.AutoDoors | Capability.OpenDoors);
		CapabilitiesAdd(Capability.UseShotRegulator);

		NPCInit();

		if (HasSpawnFlags(SF_METROPOLICE_MID_RANGE_ATTACK)) {
			DistTooFar = METROPOLICE_MID_RANGE_ATTACK_RANGE;
			SetDistLook(METROPOLICE_MID_RANGE_ATTACK_RANGE);
		}

		BaseCombatWeapon? weapon = GetActiveWeapon();
		if (weapon != null) {
			if (!FClassnameIs(weapon, "weapon_pistol"))
				WeaponDrawn = true;

			if (!WeaponDrawn)
				weapon.AddEffects(EntityEffects.NoDraw);
		}

		if (HasSpawnFlags(SF_METROPOLICE_ALWAYS_STITCH)) {
			if (Weapon_OwnsThisType("weapon_smg1", 0) == null) {
				Warning("Warning! Metrocop is trying to use the stitch behavior but he has no smg1!\n");
				RemoveSpawnFlags(SF_METROPOLICE_ALWAYS_STITCH);
			}
		}

		ChasePlayerTime = 0;

		if (Manhacks != 0)
			SetBodygroup(METROPOLICE_BODYGROUP_MANHACK, 1);
	}

	public override void Weapon_Equip(BaseCombatWeapon weapon) {
		base.Weapon_Equip(weapon);

		if (HasSpawnFlags(SF_METROPOLICE_MID_RANGE_ATTACK) && GetActiveWeapon() != null) {
			GetActiveWeapon()!.MaxRange1 = METROPOLICE_MID_RANGE_ATTACK_RANGE;
			GetActiveWeapon()!.MaxRange2 = METROPOLICE_MID_RANGE_ATTACK_RANGE;
		}
	}

	public override int GetSoundInterests() {
		return (int)(SoundInstanceType.World | SoundInstanceType.Combat | SoundInstanceType.Player | SoundInstanceType.PlayerVehicle | SoundInstanceType.Danger |
			SoundInstanceType.PhysicsDanger | SoundInstanceType.BulletImpact | SoundInstanceType.MoveAway);
	}

	public override float MaxYawSpeed() {
		switch (GetActivity()) {
			case Activity.ACT_TURN_LEFT:
			case Activity.ACT_TURN_RIGHT:
				return 120;

			case Activity.ACT_RUN:
			case Activity.ACT_RUN_HURT:
				return 15;

			case Activity.ACT_WALK:
			case Activity.ACT_WALK_CROUCH:
			case Activity.ACT_RUN_CROUCH:
				return 25;

			default:
				return 45;
		}
	}

	public override Class_T Classify() => Class_T.MetroPolice;

	public bool PlayerIsCriminal() {
		if (GlobalEntity.GetState("gordon_precriminal") == GlobalEState.On)
			return false;

		return true;
	}

	public override Disposition IRelationType(BaseEntity? target) {
		Disposition disp = base.IRelationType(target);

		if (target == null)
			return disp;

		if (target.Classify() == Class_T.Player) {
			if (!PlayerIsCriminal() && (disp == Disposition.HT)) {
				if (ChasePlayerTime != 0 && ChasePlayerTime > gpGlobals.CurTime)
					return Disposition.HT;
				return Disposition.NU;
			}
		}

		return disp;
	}
}
