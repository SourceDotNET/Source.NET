#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
#if GAME_DLL
using Game.Server;
#endif
using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;
namespace Game.Shared.GarrysMod;
using FIELD = Source.FIELD<WeaponCrowbar>;

[LinkEntityToClass("weapon_crowbar")]
[PrecacheWeaponRegister("weapon_crowbar")]
[NetworkName("CWeaponCrowbar")]
public class WeaponCrowbar : BaseHL2MPBludgeonWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponCrowbar = new(DT_BaseHL2MPBludgeonWeapon, [
#if CLIENT_DLL

#else

#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(null, null, DT_WeaponCrowbar);
	public static readonly new DataMap PredMap = new([], typeof(WeaponCrowbar), BaseHL2MPBludgeonWeapon.PredMap); public override DataMap? GetPredDescMap() => PredMap;

#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponCrowbar);
#endif

	public const float CROWBAR_RANGE = 75.0f;
	public const float CROWBAR_REFIRE = 0.4f;

#if !CLIENT_DLL
	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_RANGE_ATTACK1, WeaponAct = Activity.ACT_RANGE_ATTACK_SLAM, Required = true },
		new() { BaseAct = Activity.ACT_HL2MP_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_MELEE, Required = false },
		new() { BaseAct = Activity.ACT_HL2MP_RUN, WeaponAct = Activity.ACT_HL2MP_RUN_MELEE, Required = false },
		new() { BaseAct = Activity.ACT_HL2MP_IDLE_CROUCH, WeaponAct = Activity.ACT_HL2MP_IDLE_CROUCH_MELEE, Required = false },
		new() { BaseAct = Activity.ACT_HL2MP_WALK_CROUCH, WeaponAct = Activity.ACT_HL2MP_WALK_CROUCH_MELEE, Required = false },
		new() { BaseAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_MELEE, Required = false },
		new() { BaseAct = Activity.ACT_HL2MP_GESTURE_RELOAD, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_MELEE, Required = false },
		new() { BaseAct = Activity.ACT_HL2MP_JUMP, WeaponAct = Activity.ACT_HL2MP_JUMP_MELEE, Required = false },
	];

	public override ReadOnlySpan<ActTable> ActivityList() => acttable;

	public static readonly ConVar sk_npc_dmg_crowbar = new("sk_npc_dmg_crowbar", "0");
	public static readonly ConVar sk_crowbar_lead_time = new("sk_crowbar_lead_time", "0.9");

	public override SCOND_t WeaponMeleeAttack1Condition(float dot, float dist) {
		AI_BaseNPC npc = GetOwner()!.MyNPCPointer()!;
		BaseEntity? enemy = npc.GetEnemy();
		if (enemy == null)
			return SCOND_t.COND_NONE;

		Vector3 velocity = enemy.GetSmoothedVelocity();

		float dt = sk_crowbar_lead_time.GetFloat();
		dt += random.RandomFloat(-0.3f, 0.2f);
		if (dt < 0.0f)
			dt = 0.0f;

		MathLib.VectorMA(enemy.WorldSpaceCenter(), dt, velocity, out Vector3 extrapolatedPos);

		Vector3 delta = extrapolatedPos - npc.WorldSpaceCenter();

		if (MathF.Abs(delta.Z) > 70)
			return SCOND_t.COND_TOO_FAR_TO_ATTACK;

		Vector3 forward = npc.BodyDirection2D();
		delta.Z = 0.0f;
		Vector2 delta2D = delta.AsVector2D();
		float extrapolatedDist = delta2D.Length();
		if (extrapolatedDist != 0)
			delta2D /= extrapolatedDist;
		if ((dist > 64) && (extrapolatedDist > 64))
			return SCOND_t.COND_TOO_FAR_TO_ATTACK;

		float extrapolatedDot = MathLib.DotProduct2D(delta2D, forward.AsVector2D());
		if ((dot < 0.7) && (extrapolatedDot < 0.7))
			return SCOND_t.COND_NOT_FACING_ATTACK;

		return SCOND_t.COND_CAN_MELEE_ATTACK1;
	}

	void HandleAnimEventMeleeHit(ref AnimEvent animEvent, BaseCombatCharacter op) {
		MathLib.AngleVectors(GetAbsAngles(), out Vector3 direction);

		BaseEntity? enemy = op.MyNPCPointer()?.GetEnemy();
		if (enemy != null) {
			Vector3 delta = enemy.WorldSpaceCenter() - op.Weapon_ShootPosition();
			MathLib.VectorNormalize(ref delta);

			Vector2 delta2D = delta.AsVector2D();
			float length2D = delta2D.Length();
			if (length2D != 0)
				delta2D /= length2D;
			if (MathLib.DotProduct2D(delta2D, direction.AsVector2D()) > 0.8f)
				direction = delta;
		}

		MathLib.VectorMA(op.Weapon_ShootPosition(), 50, direction, out Vector3 end);
		BaseEntity? hurt = op.CheckTraceHullAttack(op.Weapon_ShootPosition(), end,
			new(-16, -16, -16), new(36, 36, 36), sk_npc_dmg_crowbar.GetFloat(), DamageType.Club, 0.75f);

		if (hurt != null) {
			WeaponSound(Shared.WeaponSound.MeleeHit);

			Util.TraceLine(op.Weapon_ShootPosition(), hurt.GetAbsOrigin(), Mask.ShotHull, op, Source.CollisionGroup.None, out Trace traceHit);
			ImpactEffect(ref traceHit);
		}
		else
			WeaponSound(Shared.WeaponSound.MeleeMiss);
	}

	public override void Operator_HandleAnimEvent(ref AnimEvent animEvent, BaseCombatCharacter op) {
		switch (animEvent.Event) {
			case EVENT_WEAPON_MELEE_HIT:
				HandleAnimEventMeleeHit(ref animEvent, op);
				break;

			default:
				base.Operator_HandleAnimEvent(ref animEvent, op);
				break;
		}
	}
#endif

	public override float GetDamageForActivity(Activity hitActivity) => 25.0f;

	public override void AddViewKick() {
		BasePlayer? player = ToBasePlayer(GetOwner());

		if (player == null)
			return;

		QAngle punchAng = default;

		punchAng.X = SharedRandomFloat("crowbarpax", 1, 2);
		punchAng.Y = SharedRandomFloat("crowbarpay", -2, -1);
		punchAng.Z = 0.0f;

		player.ViewPunch(punchAng);
	}

	public override void SecondaryAttack() { }

	public override void Drop(in Vector3 velocity) {
#if !CLIENT_DLL
		Util.Remove(this);
#endif
	}

	public override float GetRange() => CROWBAR_RANGE;
	public override float GetFireRate() => CROWBAR_REFIRE;
}
#endif
