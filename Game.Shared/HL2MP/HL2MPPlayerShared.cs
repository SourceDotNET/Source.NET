#if CLIENT_DLL || GAME_DLL

#if CLIENT_DLL
global using static Game.Client.HL2MP.HL2MPPlayerSharedGlobals;

global using HL2MP_Player = Game.Client.HL2MP.C_HL2MP_Player;

#else
global using static Game.Server.HL2MP.HL2MPPlayerSharedGlobals;

global using HL2MP_Player = Game.Server.HL2MP.HL2MP_Player;

#endif
using Source.Common.Mathematics;

using Game.Shared;

using System.Numerics;

#if CLIENT_DLL
namespace Game.Client.HL2MP;

#else
namespace Game.Server.HL2MP;
#endif

using Source.Common.Commands;
using Source.Common.Physics;
using Source;
using Source.Common;

using System.Runtime.CompilerServices;

public static class HL2MPPlayerSharedGlobals
{
	public static HL2MP_Player? ToHL2MPPlayer(BaseEntity? entity) {
		if (entity == null || !entity.IsPlayer())
			return null;

		return (HL2MP_Player?)entity;
	}
}

public partial class
#if CLIENT_DLL
	C_HL2MP_Player
#elif GAME_DLL
	HL2MP_Player
#endif
{
	public new Vector3 GetAttackSpread(BaseCombatWeapon? weapon, BaseEntity? target = null) {
		if (weapon != null)
			return weapon.GetBulletSpread(WeaponProficiency.Perfect);
		return VECTOR_CONE_15DEGREES;
	}

	public virtual void UpdateLookAt() {
		if (HeadYawPoseParam < 0 || HeadPitchPoseParam < 0)
			return;

		// orient eyes
		ViewTarget = LookAtTarget;

#if CLIENT_DLL
		if (BlinkTimer.IsElapsed()) {
			BlinkToggle = BlinkToggle == 0 ? 1 : 0;
			BlinkTimer.Start(RandomFloat(1.5f, 4.0f));
		}
#endif

		// Figure out where we want to look in world space.
		Vector3 to = LookAtTarget - EyePosition();
		MathLib.VectorAngles(to, out QAngle desiredAngles);

		// Figure out where our body is facing in world space.
		QAngle bodyAngles = new(0, 0, 0);
		bodyAngles[YAW] = GetLocalAngles()[YAW];

		float flBodyYawDiff = bodyAngles[YAW] - LastBodyYaw;
		LastBodyYaw = bodyAngles[YAW];

		// Set the head's yaw.
		float desired = MathLib.AngleNormalize(desiredAngles[YAW] - bodyAngles[YAW]);
		desired = Math.Clamp(desired, HeadYawMin, HeadYawMax);
		CurrentHeadYaw = MathLib.ApproachAngle(desired, CurrentHeadYaw, 130 * (float)gpGlobals.FrameTime);

		// Counterrotate the head from the body rotation so it doesn't rotate past its target.
		CurrentHeadYaw = MathLib.AngleNormalize(CurrentHeadYaw - flBodyYawDiff);
		desired = Math.Clamp(desired, HeadYawMin, HeadYawMax);

		SetPoseParameter(HeadYawPoseParam, CurrentHeadYaw);


		// Set the head's yaw.
		desired = MathLib.AngleNormalize(desiredAngles[PITCH]);
		desired = Math.Clamp(desired, HeadPitchMin, HeadPitchMax);

		CurrentHeadPitch = MathLib.ApproachAngle(desired, CurrentHeadPitch, 130 * (float)gpGlobals.FrameTime);
		CurrentHeadPitch = MathLib.AngleNormalize(CurrentHeadPitch);
		SetPoseParameter(HeadPitchPoseParam, CurrentHeadPitch);
	}
	protected Vector3 LookAtTarget;

	protected int HeadYawPoseParam;
	protected int HeadPitchPoseParam;
	protected float HeadYawMin;
	protected float HeadYawMax;
	protected float HeadPitchMin;
	protected float HeadPitchMax;
	protected float LastBodyYaw;
	protected float CurrentHeadYaw;
	protected float CurrentHeadPitch;
#if CLIENT_DLL
	protected readonly CountdownTimer BlinkTimer = new();
#endif

	internal QAngle GetAnimEyeAngles() => AngEyeAngles;
}

public class PlayerAnimState
{
	public PlayerAnimState(HL2MP_Player outer) {
		Outer = outer;

		GaitYaw = 0.0f;
		GoalFeetYaw = 0.0f;
		CurrentFeetYaw = 0.0f;
		CurrentTorsoYaw = 0.0f;
		m_flLastYaw = 0.0f;
		LastTurnTime = 0.0f;
		TurnCorrectionTime = 0.0f;
	}

	public Activity BodyYawTranslateActivity(Activity activity) {
		if (activity != Activity.ACT_IDLE)
			return activity;

		// Not turning
		switch (TurningInPlace) {
			default:
			case TurnMode.None:
				return activity;
			case TurnMode.Right:
			case TurnMode.Left:
				return mp_ik.GetBool() ? Activity.ACT_TURN : activity;
		}
	}

	public void Update() {
		AngRender = GetOuter().GetLocalAngles();
		AngRender[PITCH] = AngRender[ROLL] = 0.0f;

		ComputePoseParam_BodyYaw();
		ComputePoseParam_BodyPitch(GetOuter().GetModelPtr());
		ComputePoseParam_BodyLookYaw();

		ComputePlaybackRate();

#if CLIENT_DLL
		GetOuter().UpdateLookAt();
#endif
	}

	public ref readonly QAngle GetRenderAngles() => ref AngRender;


	public HL2MP_Player GetOuter() => Outer!;


	void GetOuterAbsVelocity(out Vector3 vel) {
#if CLIENT_DLL
		GetOuter().EstimateAbsVelocity(out vel);
#else
		vel = GetOuter().GetAbsVelocity();
#endif
	}

	TurnMode ConvergeAngles(float goal, float maxrate, float dt, ref float current) {
		TurnMode direction = TurnMode.None;

		float anglediff = goal - current;
		float anglediffabs = MathF.Abs(anglediff);

		anglediff = MathLib.AngleNormalize(anglediff);

		float scale = 1.0f;
		if (anglediffabs <= FADE_TURN_DEGREES) {
			scale = anglediffabs / FADE_TURN_DEGREES;
			// Always do at least a bit of the turn ( 1% )
			scale = Math.Clamp(scale, 0.01f, 1.0f);
		}

		float maxmove = maxrate * dt * scale;

		if (MathF.Abs(anglediff) < maxmove) {
			current = goal;
		}
		else {
			if (anglediff > 0) {
				current += maxmove;
				direction = TurnMode.Left;
			}
			else {
				current -= maxmove;
				direction = TurnMode.Right;
			}
		}

		current = MathLib.AngleNormalize(current);

		return direction;
	}

	void EstimateYaw() {
		float dt = (float)gpGlobals.FrameTime;

		if (0 == dt)
			return;

		Vector3 est_velocity;
		QAngle angles;

		GetOuterAbsVelocity(out est_velocity);

		angles = GetOuter().GetLocalAngles();

		if (est_velocity[1] == 0 && est_velocity[0] == 0) {
			float flYawDiff = angles[YAW] - GaitYaw;
			flYawDiff = flYawDiff - (int)(flYawDiff / 360) * 360;
			if (flYawDiff > 180)
				flYawDiff -= 360;
			if (flYawDiff < -180)
				flYawDiff += 360;

			if (dt < 0.25)
				flYawDiff *= dt * 4;
			else
				flYawDiff *= dt;

			GaitYaw += flYawDiff;
			GaitYaw = GaitYaw - (int)(GaitYaw / 360) * 360;
		}
		else {
			GaitYaw = (MathF.Atan2(est_velocity[1], est_velocity[0]) * 180 / MathF.PI);

			if (GaitYaw > 180)
				GaitYaw = 180;
			else if (GaitYaw < -180)
				GaitYaw = -180;
		}
	}
	void ComputePoseParam_BodyYaw() {
		int iYaw = GetOuter().LookupPoseParameter("move_yaw");
		if (iYaw < 0)
			return;

		// view direction relative to movement
		float flYaw;

		EstimateYaw();

		QAngle angles = GetOuter().GetLocalAngles();
		float ang = angles[YAW];
		if (ang > 180.0f)
			ang -= 360.0f;
		else if (ang < -180.0f)
			ang += 360.0f;

		// calc side to side turning
		flYaw = ang - GaitYaw;
		// Invert for mapping into 8way blend
		flYaw = -flYaw;
		flYaw = flYaw - (int)(flYaw / 360) * 360;

		if (flYaw < -180)
			flYaw = flYaw + 360;
		else if (flYaw > 180)
			flYaw = flYaw - 360;

		GetOuter().SetPoseParameter(iYaw, flYaw);

#if !CLIENT_DLL
		GetOuter().SetLocalAngles(new QAngle(GetOuter().GetAnimEyeAngles().X, CurrentFeetYaw, 0));
#endif
	}
	void ComputePoseParam_BodyPitch(StudioHdr? studioHdr) {
		float flPitch = GetOuter().GetLocalAngles()[PITCH];

		if (flPitch > 180.0f)
			flPitch -= 360.0f;

		flPitch = Math.Clamp(flPitch, -90, 90);

		QAngle absangles = GetOuter().GetAbsAngles();
		absangles.X = 0.0f;
		AngRender = absangles;
		AngRender[PITCH] = AngRender[ROLL] = 0.0f;

		// See if we have a blender for pitch
		GetOuter().SetPoseParameter(studioHdr, "aim_pitch", flPitch);
	}
	void ComputePoseParam_BodyLookYaw() {
		QAngle absangles = GetOuter().GetAbsAngles();
		absangles.Y = MathLib.AngleNormalize(absangles.Y);
		AngRender = absangles;
		AngRender[PITCH] = AngRender[ROLL] = 0.0f;

		// See if we even have a blender for pitch
		int upper_body_yaw = GetOuter().LookupPoseParameter("aim_yaw");
		if (upper_body_yaw < 0)
			return;

		// Assume upper and lower bodies are aligned and that we're not turning
		float flGoalTorsoYaw = 0.0f;
		TurnMode turning = TurnMode.None;
		float turnrate = 360.0f;

		GetOuterAbsVelocity(out Vector3 vel);

		bool isMoving = (vel.Length() > 1.0f) ? true : false;

		if (!isMoving) {
			// Just stopped moving, try and clamp feet
			if (LastTurnTime <= 0.0f) {
				LastTurnTime = gpGlobals.CurTime;
				m_flLastYaw = GetOuter().GetAnimEyeAngles().Y;
				// Snap feet to be perfectly aligned with torso/eyes
				GoalFeetYaw = GetOuter().GetAnimEyeAngles().Y;
				CurrentFeetYaw = GoalFeetYaw;
				TurningInPlace = TurnMode.None;
			}

			// If rotating in place, update stasis timer
			if (m_flLastYaw != GetOuter().GetAnimEyeAngles().Y) {
				LastTurnTime = gpGlobals.CurTime;
				m_flLastYaw = GetOuter().GetAnimEyeAngles().Y;
			}

			if (GoalFeetYaw != CurrentFeetYaw)
				LastTurnTime = gpGlobals.CurTime;


			turning = ConvergeAngles(GoalFeetYaw, turnrate, (float)gpGlobals.FrameTime, ref CurrentFeetYaw);

			QAngle eyeAngles = GetOuter().GetAnimEyeAngles();
			QAngle vAngle = GetOuter().GetLocalAngles();

			// See how far off current feetyaw is from true yaw
			float yawdelta = GetOuter().GetAnimEyeAngles().Y - CurrentFeetYaw;
			yawdelta = MathLib.AngleNormalize(yawdelta);

			bool rotated_too_far = false;

			float yawmagnitude = MathF.Abs(yawdelta);

			// If too far, then need to turn in place
			if (yawmagnitude > 45)
				rotated_too_far = true;

			// Standing still for a while, rotate feet around to face forward
			// Or rotated too far
			// FIXME:  Play an in place turning animation
			if (rotated_too_far ||
				(gpGlobals.CurTime > LastTurnTime + mp_facefronttime.GetFloat())) {
				GoalFeetYaw = GetOuter().GetAnimEyeAngles().Y;
				LastTurnTime = gpGlobals.CurTime;

			}

			// Snap upper body into position since the delta is already smoothed for the feet
			flGoalTorsoYaw = yawdelta;
			CurrentTorsoYaw = flGoalTorsoYaw;
		}
		else {
			LastTurnTime = 0.0f;
			TurningInPlace = TurnMode.None;
			CurrentFeetYaw = GoalFeetYaw = GetOuter().GetAnimEyeAngles().Y;
			flGoalTorsoYaw = 0.0f;
			CurrentTorsoYaw = GetOuter().GetAnimEyeAngles().Y - CurrentFeetYaw;
		}


		if (turning == TurnMode.None)
			TurningInPlace = turning;


		if (TurningInPlace != TurnMode.None)
			// If we're close to finishing the turn, then turn off the turning animation
			if (MathF.Abs(CurrentFeetYaw - GoalFeetYaw) < MIN_TURN_ANGLE_REQUIRING_TURN_ANIMATION)
				TurningInPlace = TurnMode.None;

		// Rotate entire body into position
		absangles = GetOuter().GetAbsAngles();
		absangles.Y = CurrentFeetYaw;
		AngRender = absangles;
		AngRender[PITCH] = AngRender[ROLL] = 0.0f;

		GetOuter().SetPoseParameter(upper_body_yaw, Math.Clamp(CurrentTorsoYaw, -60.0f, 60.0f));
	}

	void ComputePlaybackRate() {
		GetOuterAbsVelocity(out Vector3 vel);
		float speed = vel.Length2D();

		bool isMoving = (speed > 0.5f) ? true : false;

		double maxspeed = GetOuter().GetSequenceGroundSpeed(GetOuter().GetSequence());

		if (isMoving && (maxspeed > 0.0f)) {
			float flFactor = 1.0f;
			GetOuter().SetPlaybackRate((speed * flFactor) / maxspeed);
		}
		else
			GetOuter().SetPlaybackRate(1.0f);
	}

	HL2MP_Player? Outer;

	float GaitYaw;
	float StoredCycle;

	// The following variables are used for tweaking the yaw of the upper body when standing still and
	//  making sure that it smoothly blends in and out once the player starts moving
	// Direction feet were facing when we stopped moving
	float GoalFeetYaw;
	float CurrentFeetYaw;

	float CurrentTorsoYaw;

	// To check if they are rotating in place
	float m_flLastYaw;
	// Time when we stopped moving
	TimeUnit_t LastTurnTime;

	// One of the above enums
	TurnMode TurningInPlace;

	QAngle AngRender;

	float TurnCorrectionTime;
}

#endif
