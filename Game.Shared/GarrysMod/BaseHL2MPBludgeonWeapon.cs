#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source;
using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;
namespace Game.Shared.GarrysMod;
using FIELD = Source.FIELD<BaseHL2MPBludgeonWeapon>;
[NetworkName("CBaseHL2MPBludgeonWeapon")]
public class BaseHL2MPBludgeonWeapon : BaseHL2MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_BaseHL2MPBludgeonWeapon = new(DT_BaseHL2MPCombatWeapon, [
#if CLIENT_DLL

#else

#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_BaseHL2MPBludgeonWeapon);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_BaseHL2MPBludgeonWeapon);
#endif

	const int BLUDGEON_HULL_DIM = 16;

	static readonly Vector3 g_bludgeonMins = new(-BLUDGEON_HULL_DIM, -BLUDGEON_HULL_DIM, -BLUDGEON_HULL_DIM);
	static readonly Vector3 g_bludgeonMaxs = new(BLUDGEON_HULL_DIM, BLUDGEON_HULL_DIM, BLUDGEON_HULL_DIM);

	public BaseHL2MPBludgeonWeapon() {
		FiresUnderwater = true;
	}

	public override void Spawn() {
		MinRange1 = 0;
		MinRange2 = 0;
		MaxRange1 = 64;
		MaxRange2 = 64;
		base.Spawn();
	}

	public override void ItemPostFrame() {
		BasePlayer? owner = ToBasePlayer(GetOwner());

		if (owner == null)
			return;

		if ((owner.Buttons & InButtons.Attack) != 0 && NextPrimaryAttack <= gpGlobals.CurTime)
			PrimaryAttack();
		else if ((owner.Buttons & InButtons.Attack2) != 0 && NextSecondaryAttack <= gpGlobals.CurTime)
			SecondaryAttack();
		else {
			WeaponIdle();
			return;
		}
	}

	public override void PrimaryAttack() {
#if !CLIENT_DLL
		HL2MP_Player player = ToHL2MPPlayer(GetPlayerOwner())!;
		lagcompensation.StartLagCompensation(player);
#endif
		Swing(false);
#if !CLIENT_DLL
		lagcompensation.FinishLagCompensation(player);
#endif
	}

	public override void SecondaryAttack() {
		Swing(true);
	}

	public override Activity GetPrimaryAttackActivity() => Activity.ACT_VM_HITCENTER;
	public override Activity GetSecondaryAttackActivity() => Activity.ACT_VM_HITCENTER2;

	public override float GetFireRate() => 0.2f;
	public virtual float GetRange() => 32.0f;
	public virtual float GetDamageForActivity(Activity hitActivity) => 1.0f;

	public virtual bool PlayFleshyHittySoundOnHit() => false;

	void Hit(ref Trace traceHit, Activity hitActivity) {
		BasePlayer? player = ToBasePlayer(GetOwner());

		BaseEntity? hitEntity = traceHit.Ent;

		if (hitEntity != null) {
			player!.EyeVectors(out Vector3 hitDirection);
			MathLib.VectorNormalize(ref hitDirection);

#if !CLIENT_DLL
			TakeDamageInfo info = new(GetOwner(), GetOwner(), GetDamageForActivity(hitActivity), DamageType.Club);

			if (player != null && hitEntity.IsNPC())
				info.AdjustPlayerDamageInflictedForSkillLevel();

			CalculateMeleeDamageForce(ref info, hitDirection, traceHit.EndPos, 1.0f);

			hitEntity.DispatchTraceAttack(info, hitDirection, ref traceHit);
			ApplyMultiDamage();

			TraceAttackToTriggers(info, traceHit.StartPos, traceHit.EndPos, hitDirection);
#endif

			if (PlayFleshyHittySoundOnHit())
				WeaponSound(Shared.WeaponSound.MeleeHit);
		}

		ImpactEffect(ref traceHit);
	}

	Activity ChooseIntersectionPointAndActivity(ref Trace hitTrace, in Vector3 mins, in Vector3 maxs, BasePlayer owner) {
		int i, j, k;
		float distance;
		ReadOnlySpan<Vector3> minmaxs = [mins, maxs];
		Vector3 vecHullEnd = hitTrace.EndPos;
		Vector3 vecEnd = default;

		distance = 1e6f;
		Vector3 vecSrc = hitTrace.StartPos;

		vecHullEnd = vecSrc + ((vecHullEnd - vecSrc) * 2);
		Util.TraceLine(vecSrc, vecHullEnd, Mask.ShotHull, owner, Source.CollisionGroup.None,out Trace tmpTrace);
		if (tmpTrace.Fraction == 1.0) {
			for (i = 0; i < 2; i++) {
				for (j = 0; j < 2; j++) {
					for (k = 0; k < 2; k++) {
						vecEnd.X = vecHullEnd.X + minmaxs[i].X;
						vecEnd.Y = vecHullEnd.Y + minmaxs[j].Y;
						vecEnd.Z = vecHullEnd.Z + minmaxs[k].Z;

						Util.TraceLine(vecSrc, vecEnd, Mask.ShotHull, owner, Source.CollisionGroup.None,out tmpTrace);
						if (tmpTrace.Fraction < 1.0) {
							float thisDistance = (tmpTrace.EndPos - vecSrc).Length();
							if (thisDistance < distance) {
								hitTrace = tmpTrace;
								distance = thisDistance;
							}
						}
					}
				}
			}
		}
		else
			hitTrace = tmpTrace;

		return Activity.ACT_VM_HITCENTER;
	}

	bool ImpactWater(in Vector3 start, in Vector3 end) {
		if ((Util.PointContents(start) & (Contents.Water | Contents.Slime)) != 0)
			return false;

		if ((Util.PointContents(end) & (Contents.Water | Contents.Slime)) == 0)
			return false;

		Util.TraceLine(start, end, (Mask)(Contents.Water | Contents.Slime), GetOwner(), Source.CollisionGroup.None,out Trace waterTrace);

		if (waterTrace.Fraction < 1.0f) {
#if !CLIENT_DLL
			EffectData data = new();

			data.Flags = 0;
			data.Origin = waterTrace.EndPos;
			data.Normal = waterTrace.Plane.Normal;
			data.Scale = 8.0f;

			if ((waterTrace.Contents & Contents.Slime) != 0)
				data.Flags |= (int)WaterSplashFlags.InSlime;

			DispatchEffect("watersplash", data);
#endif
		}

		return true;
	}

	protected virtual void ImpactEffect(ref Trace traceHit) {
		if (ImpactWater(traceHit.StartPos, traceHit.EndPos))
			return;
	}

	void Swing(bool isSecondary) {
		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner == null)
			return;

		Vector3 swingStart = owner.Weapon_ShootPosition();
		owner.EyeVectors(out Vector3 forward);

		Vector3 swingEnd = swingStart + forward * GetRange();
		Util.TraceLine(swingStart, swingEnd, Mask.ShotHull, owner, Source.CollisionGroup.None,out Trace traceHit);
		Activity hitActivity = Activity.ACT_VM_HITCENTER;

#if !CLIENT_DLL
		TakeDamageInfo triggerInfo = new(GetOwner(), GetOwner(), GetDamageForActivity(hitActivity), DamageType.Club);
		TraceAttackToTriggers(triggerInfo, traceHit.StartPos, traceHit.EndPos, vec3_origin);
#endif

		if (traceHit.Fraction == 1.0) {
			float bludgeonHullRadius = 1.732f * BLUDGEON_HULL_DIM;

			swingEnd -= forward * bludgeonHullRadius;

			Util.TraceHull(swingStart, swingEnd, g_bludgeonMins, g_bludgeonMaxs, Mask.ShotHull, owner, Source.CollisionGroup.None,out traceHit);
			if (traceHit.Fraction < 1.0 && traceHit.Ent != null) {
				Vector3 vecToTarget = traceHit.Ent.GetAbsOrigin() - swingStart;
				MathLib.VectorNormalize(ref vecToTarget);

				float dot = Vector3.Dot(vecToTarget, forward);

				if (dot < 0.70721f)
					traceHit.Fraction = 1.0f;
				else
					hitActivity = ChooseIntersectionPointAndActivity(ref traceHit, g_bludgeonMins, g_bludgeonMaxs, owner);
			}
		}

		WeaponSound(Shared.WeaponSound.Single);

		if (traceHit.Fraction == 1.0f) {
			hitActivity = isSecondary ? Activity.ACT_VM_MISSCENTER2 : Activity.ACT_VM_MISSCENTER;

			Vector3 testEnd = swingStart + forward * GetRange();

			ImpactWater(swingStart, testEnd);
		}
		else
			Hit(ref traceHit, hitActivity);

		SendWeaponAnim(hitActivity);

		owner.SetAnimation(PlayerAnim.Attack1);

		NextPrimaryAttack = gpGlobals.CurTime + GetFireRate();
		NextSecondaryAttack = gpGlobals.CurTime + SequenceDuration();
	}
}
#endif
