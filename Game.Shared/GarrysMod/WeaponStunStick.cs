#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
#if GAME_DLL
using Game.Server;
#endif
using Source;
using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;
namespace Game.Shared.GarrysMod;
using FIELD = Source.FIELD<WeaponStunStick>;

[LinkEntityToClass("weapon_stunstick")]
[PrecacheWeaponRegister("weapon_stunstick")]
[NetworkName("CWeaponStunStick")]
public partial class WeaponStunStick : BaseHL2MPBludgeonWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponStunStick = new(DT_BaseHL2MPBludgeonWeapon, [
#if CLIENT_DLL
		RecvPropBool(FIELD.OF(nameof(Active)))
#else
		SendPropBool(FIELD.OF(nameof(Active)))
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(null, null, DT_WeaponStunStick);
	public static readonly new DataMap PredMap = new([], typeof(WeaponStunStick), BaseHL2MPBludgeonWeapon.PredMap); public override DataMap? GetPredDescMap() => PredMap;
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponStunStick);
#endif
	[NetworkName("m_bActive")]
#if CLIENT_DLL
	public bool Active;
#else
	[NetworkVar] public partial bool Active { get; set; }
#endif

	public const float STUNSTICK_RANGE = 75.0f;
	public const float STUNSTICK_REFIRE = 0.8f;
	public const string STUNSTICK_BEAM_MATERIAL = "sprites/lgtning.vmt";

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
#endif

	public WeaponStunStick() {
		Active = false;
	}

	public override void Spawn() {
		Precache();

		base.Spawn();
		AddSolidFlags(SolidFlags.NotStandable);
	}

	public override void Precache() {
		base.Precache();

		PrecacheScriptSound("Weapon_StunStick.Activate");
		PrecacheScriptSound("Weapon_StunStick.Deactivate");

		PrecacheModel(STUNSTICK_BEAM_MATERIAL);
		PrecacheModel("sprites/light_glow02_add.vmt");
		PrecacheModel("effects/blueflare1.vmt");
		PrecacheModel("sprites/light_glow02_add_noz.vmt");
	}

	public override float GetRange() => STUNSTICK_RANGE;
	public override float GetFireRate() => STUNSTICK_REFIRE;

	public override float GetDamageForActivity(Activity hitActivity) => 40.0f;

	public override bool PlayFleshyHittySoundOnHit() => true;

	public override void SecondaryAttack() { }

#if !CLIENT_DLL
	public override SCOND_t WeaponMeleeAttack1Condition(float dot, float dist) {
		AI_BaseNPC npc = GetOwner()!.MyNPCPointer()!;
		BaseEntity? enemy = npc.GetEnemy();
		if (enemy == null)
			return SCOND_t.COND_NONE;

		enemy.GetVelocity(out Vector3 velocity, out _);

		float dt = WeaponCrowbar.sk_crowbar_lead_time.GetFloat();
		dt += random.RandomFloat(-0.3f, 0.2f);
		if (dt < 0.0f)
			dt = 0.0f;

		MathLib.VectorMA(enemy.WorldSpaceCenter(), dt, velocity, out Vector3 extrapolatedPos);

		Vector3 delta = extrapolatedPos - npc.WorldSpaceCenter();

		if (MathF.Abs(delta.Z) > 70)
			return SCOND_t.COND_TOO_FAR_TO_ATTACK;

		Vector3 forward = npc.BodyDirection2D();
		delta.Z = 0.0f;
		float extrapolatedDot = MathLib.DotProduct2D(delta.AsVector2D(), forward.AsVector2D());
		if ((dot < 0.7) && (extrapolatedDot < 0.7))
			return SCOND_t.COND_NOT_FACING_ATTACK;

		float extrapolatedDist = MathLib.Vector2DLength(delta.AsVector2D());

		if (enemy.IsPlayer()) {
			Vector3 projectEnemy = enemy.GetAbsOrigin() + (enemy.GetAbsVelocity() * 0.35f);
			Vector3 projectMe = GetAbsOrigin();

			if (MathLib.Vector2DLength((projectMe - projectEnemy).AsVector2D()) <= 48.0f)
				return SCOND_t.COND_CAN_MELEE_ATTACK1;
		}

		float targetDist = 48.0f;
		if ((dist > targetDist) && (extrapolatedDist > targetDist))
			return SCOND_t.COND_TOO_FAR_TO_ATTACK;

		return SCOND_t.COND_CAN_MELEE_ATTACK1;
	}

	public override void Operator_HandleAnimEvent(ref AnimEvent animEvent, BaseCombatCharacter op) {
		switch (animEvent.Event) {
			case EVENT_WEAPON_MELEE_HIT: {
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

					MathLib.VectorMA(op.Weapon_ShootPosition(), 32, direction, out Vector3 end);
					BaseEntity? hurt = op.CheckTraceHullAttack(op.Weapon_ShootPosition(), end,
						new(-16, -16, -40), new(16, 16, 16), GetDamageForActivity(GetActivity()), DamageType.Club, 0.5f, false);

					if (hurt != null) {
						WeaponSound(Shared.WeaponSound.MeleeHit);

						BasePlayer? player = ToBasePlayer(hurt);

						if (player != null && (player.GetFlags() & EntityFlags.GodMode) == 0) {
							float yawKick = random.RandomFloat(-48, -24);

							player.ViewPunch(new QAngle(-16, yawKick, 2));

							Vector3 dir = hurt.GetAbsOrigin() - GetAbsOrigin();

							if (player.GetGroundEntity() == op) {
								dir = direction;
								dir.Z = 0;
							}

							MathLib.VectorNormalize(ref dir);

							dir *= 500.0f;

							if ((player.GetFlags() & EntityFlags.OnGround) == 0)
								dir.Z = 0.0f;

							hurt.ApplyAbsVelocityImpulse(dir);

							Util.ScreenFade(player, new(128, 0, 0, 128), 0.5f, 0.1f, FadeFlags.In);

							player.ForceDropOfCarriedPhysObjects(null);
						}
					}
					else
						WeaponSound(Shared.WeaponSound.MeleeMiss);
				}
				break;
			default:
				base.Operator_HandleAnimEvent(ref animEvent, op);
				break;
		}
	}
#endif

	public void SetStunState(bool state) {
		Active = state;

		if (Active) {
#if !CLIENT_DLL
			GetAttachment(1, out Vector3 attachment, out QAngle _);
			g_pEffects.Sparks(attachment);
#endif

			EmitSound("Weapon_StunStick.Activate");
		}
		else
			EmitSound("Weapon_StunStick.Deactivate");
	}

	public bool GetStunState() => Active;

	public override bool Deploy() {
		SetStunState(true);

		return base.Deploy();
	}

	public override bool Holster(BaseCombatWeapon? switchingTo = null) {
		if (base.Holster(switchingTo) == false)
			return false;

		SetStunState(false);
		SetWeaponVisible(false);

		return true;
	}

	public override void Drop(in Vector3 velocity) {
		SetStunState(false);

#if !CLIENT_DLL
		Util.Remove(this);
#endif
	}
}
#endif
