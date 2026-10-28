#if CLIENT_DLL || GAME_DLL

#if CLIENT_DLL
using Game.Client.GarrysMod;
#else
using Game.Server.GarrysMod;
#endif

#if HL2_DLL
using Game.Shared.HL2;
#endif

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;
using Source.Engine;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Game.Shared;


public enum SpeedCropped
{
	Reset,
	Duck,
	Weapon
}
public class GameMovement : IGameMovement
{
	static bool g_bMovementOptimizations = true;
	public void FinishTrackPredictionErrors(BasePlayer player) {

	}

	public Vector3 GetPlayerMaxs(bool ducked) {
		return ducked ? VEC_DUCK_HULL_MAX_SCALED(Player) : VEC_HULL_MAX_SCALED(Player);
	}

	public Vector3 GetPlayerMins(bool ducked) {
		return ducked ? VEC_DUCK_HULL_MIN_SCALED(Player) : VEC_HULL_MIN_SCALED(Player);
	}

	public Vector3 GetPlayerViewOffset(bool ducked) {
		return ducked ? VEC_DUCK_VIEW_SCALED(Player) : VEC_VIEW_SCALED(Player);
	}


	public void ProcessMovement(BasePlayer player, MoveData pMove) {
		TimeUnit_t storeFrametime = gpGlobals.FrameTime;

		//!!HACK HACK: Adrian - slow down all player movement by this factor.
		//!!Blame Yahn for this one.
		gpGlobals.FrameTime *= player.GetLaggedMovementValue();

		ResetGetPointContentsCache();

		// Cropping movement speed scales mv.m_fForwardSpeed etc. globally
		// Once we crop, we don't want to recursively crop again, so we set the crop
		//  flag globally here once per usercmd cycle.
		SpeedCropped = SpeedCropped.Reset;

		// StartTrackPredictionErrors should have set this
		Assert(this.Player == player);
		this.Player = player;
		mv = pMove;

		mv.MaxSpeed = player.GetPlayerMaxSpeed();

		// Run the command.
		PlayerMove();

		FinishMove();

		// CheckV( Player.CurrentCommandNumber(), "EndPos", mv.GetAbsOrigin() );

		if (player.Impulse == 154) {
			// Garry's Mod lua calls todo...	
			if (player.MoveType == (byte)MoveType.Noclip) {
				player.SetMoveType(MoveType.Walk);
			}
			else {
				player.SetMoveType(MoveType.Noclip);
			}
		}

		//This is probably not needed, but just in case.
		gpGlobals.FrameTime = storeFrametime;
	}

	public void StartTrackPredictionErrors(BasePlayer player) {
		this.Player = player;
	}

	protected MoveData mv = null!;

	protected WaterLevel OldWaterLevel;
	protected TimeUnit_t WaterEntryTime;
	protected int iOnLadder;

	protected Vector3 Forward;
	protected Vector3 Right;
	protected Vector3 Up;
	public static readonly ConVar sv_optimizedmovement = new("sv_optimizedmovement", "1", FCvar.Replicated | FCvar.DevelopmentOnly);



	protected virtual void PlayerMove() {
		CheckParameters();

		// clear output applied velocity
		mv!.WishVel.Init();
		mv!.JumpVel.Init();

		MoveHelper().ResetTouchList();                    // Assume we don't touch anything

		ReduceTimers();

		MathLib.AngleVectors(mv.ViewAngles, out Forward, out Right, out Up);  // Determine movement angles

		// Always try and unstick us unless we are using a couple of the movement modes
		if (Player.GetMoveType() != MoveType.Noclip &&
			 Player.GetMoveType() != MoveType.None &&
			 Player.GetMoveType() != MoveType.Isometric &&
			 Player.GetMoveType() != MoveType.Observer &&
			 !Player.pl.DeadFlag) {
			if (CheckInterval(IntervalType.Stuck)) {
				if (CheckStuck()) {
					// Can't move, we're stuck
					return;
				}
			}
		}

		// Now that we are "unstuck", see where we are (Player.GetWaterLevel() and type, Player.GetGroundEntity()).
		if (Player.GetMoveType() != MoveType.Walk ||
			mv.GameCodeMovedPlayer ||
			!sv_optimizedmovement.GetBool()) {
			CategorizePosition();
		}
		else {
			if (mv.Velocity.Z > 250.0f)
				SetGroundEntity(ref Trace.NULL);
		}

		// Store off the starting water level
		OldWaterLevel = Player.GetWaterLevel();

		// If we are not on ground, store off how fast we are moving down
		if (Player.GetGroundEntity() == null)
			Player.Local.FallVelocity = -mv.Velocity[2];

		iOnLadder = 0;

		Player.UpdateStepSound(Player.SurfaceData, mv.GetAbsOrigin(), mv.Velocity);

		UpdateDuckJumpEyeOffset();
		Duck();

		// Don't run ladder code if dead on on a train
		if (!Player.pl.DeadFlag && 0 == (Player.GetFlags() & EntityFlags.OnTrain)) {
			// If was not on a ladder now, but was on one before, 
			//  get off of the ladder
			if (!LadderMove() && (Player.GetMoveType() == MoveType.Ladder)) {
				Player.SetMoveType(MoveType.Walk);
				Player.SetMoveCollide(MoveCollide.Default);
			}
		}

		// Handle movement modes.
		switch (Player.GetMoveType()) {
			case MoveType.None:
				break;

			case MoveType.Noclip:
#if GMOD_DLL
				FullNoClipMove_NoAcceleration(sv_noclipspeed.GetFloat(), sv_noclipaccelerate.GetFloat());
#else
				FullNoClipMove(sv_noclipspeed.GetFloat(), sv_noclipaccelerate.GetFloat());
#endif
				break;

			case MoveType.Fly:
			case MoveType.FlyGravity:
				FullTossMove();
				break;

			case MoveType.Ladder:
				FullLadderMove();
				break;

			case MoveType.Walk:
				FullWalkMove();
				break;

			case MoveType.Isometric:
				//IsometricMove();
				// Could also try:  FullTossMove();
				FullWalkMove();
				break;

			case MoveType.Observer:
				FullObserverMove(); // clips against world&players
				break;

			default:
				DevMsg(1, $"Bogus pmove player movetype {Player.GetMoveType()} on ({(Player.IsServer() ? '1' : '0')}) 0=cl 1=sv\n");
				break;
		}
	}
	protected void FinishMove() {
		mv!.OldButtons = mv.Buttons;
		mv!.OldForwardMove = mv.ForwardMove;
	}
	protected virtual float CalcRoll(in QAngle angles, in Vector3 velocity, float rollangle, float rollspeed) {
		float sign;
		float side;
		float value;
		Vector3 forward, right, up;

		MathLib.AngleVectors(angles, out forward, out right, out up);

		side = MathLib.DotProduct(velocity, right);
		sign = side < 0 ? -1 : 1;
		side = MathF.Abs(side);
		value = rollangle;
		if (side < rollspeed)
			side = side * value / rollspeed;
		else
			side = value;

		return side * sign;
	}

	public const float PUNCH_DAMPING = 9.0f;            // bigger number makes the response more damped, smaller is less damped
														// currently the system will overshoot, with larger damping values it won't
	public const float PUNCH_SPRING_CONSTANT = 65.0f;   // bigger number increases the speed at which the view corrects

	protected virtual void DecayPunchAngle() {
		if (Player.Local.PunchAngle.LengthSqr() > 0.001 || Player.Local.PunchAngleVel.LengthSqr() > 0.001) {
			Player.Local.PunchAngle += Player.Local.PunchAngleVel * (float)gpGlobals.FrameTime;
			float damping = 1 - (PUNCH_DAMPING * (float)gpGlobals.FrameTime);

			if (damping < 0)
				damping = 0;

			Player.Local.PunchAngleVel *= damping;

			// torsional spring
			// UNDONE: Per-axis spring constant?
			float springForceMagnitude = PUNCH_SPRING_CONSTANT * (float)gpGlobals.FrameTime;
			springForceMagnitude = Math.Clamp(springForceMagnitude, 0f, 2f);
			Player.Local.PunchAngleVel -= Player.Local.PunchAngle * springForceMagnitude;

			// don't wrap around
			Player.Local.PunchAngle = new QAngle(
				Math.Clamp(Player.Local.PunchAngle.X, -89f, 89f),
				Math.Clamp(Player.Local.PunchAngle.Y, -179f, 179f),
				Math.Clamp(Player.Local.PunchAngle.Z, -89f, 89f));
		}
		else {
			Player.Local.PunchAngle = new QAngle(0, 0, 0);
			Player.Local.PunchAngleVel = new QAngle(0, 0, 0);
		}
	}

	protected virtual void CheckWaterJump() {
		Vector3 flatforward = default;
		Vector3 forward = default;
		Vector3 flatvelocity = default;
		float curspeed;

		MathLib.AngleVectors(mv.ViewAngles, out forward);  // Determine movement angles

		// Already water jumping.
		if (Player.WaterJumpTime != 0)
			return;

		// Don't hop out if we just jumped in
		if (mv.Velocity[2] < -180)
			return; // only hop out if we are moving up

		// See if we are backing up
		flatvelocity[0] = mv.Velocity[0];
		flatvelocity[1] = mv.Velocity[1];
		flatvelocity[2] = 0;

		// Must be moving
		curspeed = MathLib.VectorNormalizeFast(ref flatvelocity);

		// see if near an edge
		flatforward[0] = forward[0];
		flatforward[1] = forward[1];
		flatforward[2] = 0;
		MathLib.VectorNormalizeFast(ref flatforward);

		// Are we backing into water from steps or something?  If so, don't pop forward
		if (curspeed != 0.0 && (MathLib.DotProduct(flatvelocity, flatforward) < 0.0))
			return;

		// Start line trace at waist height (using the center of the player for this here)
		Vector3 vecStart = mv.GetAbsOrigin() + (GetPlayerMins() + GetPlayerMaxs()) * 0.5f;

		MathLib.VectorMA(vecStart, 24.0f, flatforward, out Vector3 vecEnd);

		Trace tr;
		TracePlayerBBox(vecStart, vecEnd, PlayerSolidMask(), CollisionGroup.PlayerMovement, out tr);
		if (tr.Fraction < 1.0)      // solid at waist
		{
			IPhysicsObject? physObj = tr.Ent!.VPhysicsGetObject();
			if (physObj != null) {
				if ((physObj.GetGameFlags() & PhysicsFlags.PlayerHeld) != 0)
					return;
			}

			vecStart.Z = mv.GetAbsOrigin().Z + Player.GetViewOffset().Z + WATERJUMP_HEIGHT;
			MathLib.VectorMA(vecStart, 24.0f, flatforward, out vecEnd);
			MathLib.VectorMA(vec3_origin, -50.0f, tr.Plane.Normal, out Player.WaterJumpVel);

			TracePlayerBBox(vecStart, vecEnd, PlayerSolidMask(), CollisionGroup.PlayerMovement, out tr);
			if (tr.Fraction == 1.0)     // open at eye level
			{
				// Now trace down to see if we would actually land on a standable surface.
				MathLib.VectorCopy(vecEnd, out vecStart);
				vecEnd.Z -= 1024.0f;
				TracePlayerBBox(vecStart, vecEnd, PlayerSolidMask(), CollisionGroup.PlayerMovement, out tr);
				if ((tr.Fraction < 1.0f) && (tr.Plane.Normal.Z >= 0.7)) {
					mv.Velocity[2] = 256.0f;          // Push up
					mv!.OldButtons |= InButtons.Jump;       // Don't jump again until released
					Player.AddFlag(EntityFlags.WaterJump);
					Player.WaterJumpTime = 2000.0;    // Do this for 2 seconds
				}
			}
		}
	}

	protected virtual void WaterMove() {
		int i;
		Vector3 wishvel = default;
		float wishspeed;
		Vector3 wishdir;
		Vector3 start, dest;
		Vector3 temp;
		Trace pm;
		float speed, newspeed, addspeed, accelspeed;
		Vector3 forward, right, up;

		MathLib.AngleVectors(mv!.ViewAngles, out forward, out right, out up);  // Determine movement angles

		// user intentions
		for (i = 0; i < 3; i++)
			wishvel[i] = forward[i] * mv.ForwardMove + right[i] * mv.SideMove;


		// if we have the jump key down, move us up as well
		if ((mv.Buttons & InButtons.Jump) != 0)
			wishvel[2] += mv.ClientMaxSpeed;
		// Sinking after no other movement occurs
		else if (mv.ForwardMove == 0 && mv.SideMove == 0 && mv.UpMove == 0)
			wishvel[2] -= 60;       // drift towards bottom
		else  // Go straight up by upmove amount.
		{
			// exaggerate upward movement along forward as well
			float upwardMovememnt = mv.ForwardMove * forward.Z * 2;
			upwardMovememnt = Math.Clamp(upwardMovememnt, 0, mv.ClientMaxSpeed);
			wishvel[2] += mv.UpMove + upwardMovememnt;
		}

		// Copy it over and determine speed
		MathLib.VectorCopy(wishvel, out wishdir);
		wishspeed = MathLib.VectorNormalizeFast(ref wishdir);

		// Cap speed.
		if (wishspeed > mv.MaxSpeed) {
			MathLib.VectorScale(wishvel, mv.MaxSpeed / wishspeed, out wishvel);
			wishspeed = mv.MaxSpeed;
		}

		// Slow us down a bit.
		wishspeed *= 0.8f;

		// Water friction
		MathLib.VectorCopy(mv.Velocity, out temp);
		speed = MathLib.VectorNormalizeFast(ref temp);
		if (speed != 0) {
			newspeed = speed - (float)gpGlobals.FrameTime * speed * sv_friction.GetFloat() * Player.SurfaceFriction;
			if (newspeed < 0.1f) {
				newspeed = 0;
			}

			MathLib.VectorScale(mv.Velocity, newspeed / speed, out mv.Velocity);
		}
		else
			newspeed = 0;

		// water acceleration
		if (wishspeed >= 0.1f)  // old !
		{
			addspeed = wishspeed - newspeed;
			if (addspeed > 0) {
				MathLib.VectorNormalizeFast(ref wishvel);
				accelspeed = sv_accelerate.GetFloat() * wishspeed * (float)gpGlobals.FrameTime * Player.SurfaceFriction;
				if (accelspeed > addspeed) {
					accelspeed = addspeed;
				}

				for (i = 0; i < 3; i++) {
					float deltaSpeed = accelspeed * wishvel[i];
					mv.Velocity[i] += deltaSpeed;
					mv.WishVel[i] += deltaSpeed;
				}
			}
		}

		MathLib.VectorAdd(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);

		// Now move
		// assume it is a stair or a slope, so press down from stepheight above
		MathLib.VectorMA(mv.GetAbsOrigin(), (float)gpGlobals.FrameTime, mv.Velocity, out dest);

		TracePlayerBBox(mv.GetAbsOrigin(), dest, PlayerSolidMask(), CollisionGroup.PlayerMovement, out pm);
		if (pm.Fraction == 1.0f) {
			MathLib.VectorCopy(dest, out start);
			if (Player.Local.AllowAutoMovement)
				start[2] += Player.Local.StepSize + 1;

			TracePlayerBBox(start, dest, PlayerSolidMask(), CollisionGroup.PlayerMovement, out pm);

			if (!pm.StartSolid && !pm.AllSolid) {
				float stepDist = pm.EndPos.Z - mv.GetAbsOrigin().Z;
				mv.StepHeight += stepDist;
				// walked up the step, so just keep result and exit
				mv.SetAbsOrigin(pm.EndPos);
				MathLib.VectorSubtract(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);
				return;
			}

			// Try moving straight along out normal path.
			TryPlayerMove();
		}
		else {
			if (Player.GetGroundEntity() == null) {
				TryPlayerMove();
				MathLib.VectorSubtract(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);
				return;
			}

			StepMove(ref dest, ref pm);
		}

		MathLib.VectorSubtract(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);
	}

	protected void WaterJump() {
		if (Player!.WaterJumpTime > 10000)
			Player!.WaterJumpTime = 10000;

		if (0 == Player!.WaterJumpTime)
			return;

		Player!.WaterJumpTime -= 1000.0 * gpGlobals.FrameTime;

		if (Player!.WaterJumpTime <= 0 || 0 == Player!.GetWaterLevel()) {
			Player!.WaterJumpTime = 0;
			Player!.RemoveFlag(EntityFlags.WaterJump);
		}

		mv!.Velocity[0] = Player!.WaterJumpVel[0];
		mv!.Velocity[1] = Player!.WaterJumpVel[1];
	}

	// Handles both ground friction and water friction
	protected void Friction() {
		float speed, newspeed, control;
		float friction;
		float drop;

		// If we are in water jump cycle, don't apply friction
		if (Player.WaterJumpTime != 0)
			return;

		// Calculate speed
		speed = MathLib.VectorLength(mv.Velocity);

		// If too slow, return
		if (speed < 0.1f) {
			return;
		}

		drop = 0;

		// apply ground friction
		if (Player.GetGroundEntity() != null)  // On an entity that is the ground
		{
			friction = sv_friction.GetFloat() * Player.SurfaceFriction;

			// Bleed off some speed, but if we have less than the bleed
			//  threshold, bleed the threshold amount.
			control = (speed < sv_stopspeed.GetFloat()) ? sv_stopspeed.GetFloat() : speed;

			// Add the amount to the drop amount.
			drop += control * friction * (float)gpGlobals.FrameTime;
		}

		// scale the velocity
		newspeed = speed - drop;
		if (newspeed < 0)
			newspeed = 0;

		if (newspeed != speed) {
			// Determine proportion of old speed we are using.
			newspeed /= speed;
			// Adjust velocity according to proportion.
			MathLib.VectorScale(mv.Velocity, newspeed, out mv.Velocity);
		}

		mv.WishVel -= (1f - newspeed) * mv.Velocity;
	}

	protected virtual void AirAccelerate(ref Vector3 wishdir, float wishspeed, float accel) {
		int i;
		float addspeed, accelspeed, currentspeed;
		float wishspd;

		wishspd = wishspeed;

		if (Player.pl.DeadFlag)
			return;

		if (Player.WaterJumpTime != 0)
			return;

		// Cap speed
		if (wishspd > GetAirSpeedCap())
			wishspd = GetAirSpeedCap();

		// Determine veer amount
		currentspeed = mv!.Velocity.Dot(wishdir);

		// See how much to add
		addspeed = wishspd - currentspeed;

		// If not adding any, done.
		if (addspeed <= 0)
			return;

		// Determine acceleration speed after acceleration
		accelspeed = accel * wishspeed * (float)gpGlobals.FrameTime * Player.SurfaceFriction;

		// Cap it
		if (accelspeed > addspeed)
			accelspeed = addspeed;

		// Adjust pmove vel.
		for (i = 0; i < 3; i++) {
			mv.Velocity[i] += accelspeed * wishdir[i];
			mv.WishVel[i] += accelspeed * wishdir[i];
		}
	}

	protected virtual void AirMove() {
		int i;
		Vector3 wishvel = default;
		float fmove, smove;
		Vector3 wishdir;
		float wishspeed;
		Vector3 forward, right, up;

		MathLib.AngleVectors(mv!.ViewAngles, out forward, out right, out up);  // Determine movement angles

		// Copy movement amounts
		fmove = mv.ForwardMove;
		smove = mv.SideMove;

		// Zero out z components of movement vectors
		forward[2] = 0;
		right[2] = 0;
		MathLib.VectorNormalizeFast(ref forward);  // Normalize remainder of vectors
		MathLib.VectorNormalizeFast(ref right);    // 

		for (i = 0; i < 2; i++)       // Determine x and y parts of velocity
			wishvel[i] = forward[i] * fmove + right[i] * smove;
		wishvel[2] = 0;             // Zero out z part of velocity

		MathLib.VectorCopy(wishvel, out wishdir);   // Determine maginitude of speed of move
		wishspeed = MathLib.VectorNormalizeFast(ref wishdir);

		//
		// clamp to server defined max speed
		//
		if (wishspeed != 0 && (wishspeed > mv.MaxSpeed)) {
			MathLib.VectorScale(wishvel, mv.MaxSpeed / wishspeed, out wishvel);
			wishspeed = mv.MaxSpeed;
		}

		AirAccelerate(ref wishdir, wishspeed, sv_airaccelerate.GetFloat());

		// Add in any base velocity to the current velocity.
		MathLib.VectorAdd(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);

		TryPlayerMove();

		// Now pull the base velocity back out.   Base velocity is set if you are on a moving object, like a conveyor (or maybe another monster?)
		MathLib.VectorSubtract(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);
	}
	protected virtual float GetAirSpeedCap() { return 30f; }

	protected virtual bool CanAccelerate() {
		// Dead players don't accelerate.
		if (Player.pl.DeadFlag)
			return false;

		// If waterjumping, don't accelerate
		if (Player.WaterJumpTime != 0)
			return false;

		return true;
	}
	protected virtual void Accelerate(ref Vector3 wishdir, float wishspeed, float accel) {
		int i;
		float addspeed, accelspeed, currentspeed;

		// This gets overridden because some games (CSPort) want to allow dead (observer) players
		// to be able to move around.
		if (!CanAccelerate())
			return;

		// See if we are changing direction a bit
		currentspeed = mv!.Velocity.Dot(wishdir);

		// Reduce wishspeed by the amount of veer.
		addspeed = wishspeed - currentspeed;

		// If not going to add any speed, done.
		if (addspeed <= 0)
			return;

		// Determine amount of accleration.
		accelspeed = accel * (float)gpGlobals.FrameTime * wishspeed * Player.SurfaceFriction;

		// Cap at addspeed
		if (accelspeed > addspeed)
			accelspeed = addspeed;

		// Adjust velocity.
		for (i = 0; i < 3; i++)
			mv.Velocity[i] += accelspeed * wishdir[i];
	}

	// Only used by players.  Moves along the ground when player is a MoveType.Walk.
	Handle<BaseEntity> WalkMove__oldground = new();
	protected virtual void WalkMove() {
		int i;

		Vector3 wishvel = default;
		float spd;
		float fmove, smove;
		Vector3 wishdir = default;
		float wishspeed;

		Vector3 dest = default;
		Trace pm;
		Vector3 forward, right, up;

		MathLib.AngleVectors(mv!.ViewAngles, out forward, out right, out up);  // Determine movement angles

		WalkMove__oldground.Set(Player.GetGroundEntity());

		// Copy movement amounts
		fmove = mv.ForwardMove;
		smove = mv.SideMove;

		// Zero out z components of movement vectors
		if (g_bMovementOptimizations) {
			if (forward[2] != 0) {
				forward[2] = 0;
				MathLib.VectorNormalizeFast(ref forward);
			}

			if (right[2] != 0) {
				right[2] = 0;
				MathLib.VectorNormalizeFast(ref right);
			}
		}
		else {
			forward[2] = 0;
			right[2] = 0;

			MathLib.VectorNormalizeFast(ref forward);  // Normalize remainder of vectors.
			MathLib.VectorNormalizeFast(ref right);    // 
		}

		for (i = 0; i < 2; i++)       // Determine x and y parts of velocity
			wishvel[i] = forward[i] * fmove + right[i] * smove;

		wishvel[2] = 0;             // Zero out z part of velocity

		MathLib.VectorCopy(wishvel, out wishdir);   // Determine maginitude of speed of move
		wishspeed = MathLib.VectorNormalizeFast(ref wishdir);

		//
		// Clamp to server defined max speed
		//
		if ((wishspeed != 0.0f) && (wishspeed > mv.MaxSpeed)) {
			MathLib.VectorScale(wishvel, mv.MaxSpeed / wishspeed, out wishvel);
			wishspeed = mv.MaxSpeed;
		}

		// Set pmove velocity
		mv.Velocity[2] = 0;
		Accelerate(ref wishdir, wishspeed, sv_accelerate.GetFloat());
		mv.Velocity[2] = 0;

		// Add in any base velocity to the current velocity.
		MathLib.VectorAdd(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);

		spd = MathLib.VectorLength(mv.Velocity);

		if (spd < 1.0f) {
			mv.Velocity.Init();
			// Now pull the base velocity back out.   Base velocity is set if you are on a moving object, like a conveyor (or maybe another monster?)
			MathLib.VectorSubtract(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);
			return;
		}

		// first try just moving to the destination	
		dest[0] = mv.GetAbsOrigin()[0] + mv.Velocity[0] * (float)gpGlobals.FrameTime;
		dest[1] = mv.GetAbsOrigin()[1] + mv.Velocity[1] * (float)gpGlobals.FrameTime;
		dest[2] = mv.GetAbsOrigin()[2];

		// first try moving directly to the next spot
		TracePlayerBBox(mv.GetAbsOrigin(), dest, PlayerSolidMask(), CollisionGroup.PlayerMovement, out pm);

		// If we made it all the way, then copy trace end as new player position.
		mv.WishVel += wishdir * wishspeed;

		if (pm.Fraction == 1) {
			mv.SetAbsOrigin(pm.EndPos);
			// Now pull the base velocity back out.   Base velocity is set if you are on a moving object, like a conveyor (or maybe another monster?)
			MathLib.VectorSubtract(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);

			StayOnGround();
			return;
		}

		// Don't walk up stairs if not on ground.
		if (WalkMove__oldground.Get() == null && Player.GetWaterLevel() == 0) {
			// Now pull the base velocity back out.   Base velocity is set if you are on a moving object, like a conveyor (or maybe another monster?)
			MathLib.VectorSubtract(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);
			return;
		}

		// If we are jumping out of water, don't do anything more.
		if (Player.WaterJumpTime != 0) {
			// Now pull the base velocity back out.   Base velocity is set if you are on a moving object, like a conveyor (or maybe another monster?)
			MathLib.VectorSubtract(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);
			return;
		}

		StepMove(ref dest, ref pm);

		// Now pull the base velocity back out.   Base velocity is set if you are on a moving object, like a conveyor (or maybe another monster?)
		MathLib.VectorSubtract(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);

		StayOnGround();
	}

	// Try to keep a walking player on the ground when running down slopes etc
	protected void StayOnGround() {
		Trace trace;
		Vector3 start = mv.GetAbsOrigin();
		Vector3 end = mv.GetAbsOrigin();
		start.Z += 2;
		end.Z -= Player.GetStepSize();

		// See how far up we can go without getting stuck

		TracePlayerBBox(mv.GetAbsOrigin(), start, PlayerSolidMask(), CollisionGroup.PlayerMovement, out trace);
		start = trace.EndPos;

		// using trace.startsolid is unreliable here, it doesn't get set when
		// tracing bounding box vs. terrain

		// Now trace down from a known safe position
		TracePlayerBBox(start, end, PlayerSolidMask(), CollisionGroup.PlayerMovement, out trace);
		if (trace.Fraction > 0.0f &&            // must go somewhere
			trace.Fraction < 1.0f &&            // must hit something
			!trace.StartSolid &&                // can't be embedded in a solid
			trace.Plane.Normal[2] >= 0.7)       // can't hit a steep slope that we can't stand on anyway
		{
			float flDelta = MathF.Abs(mv.GetAbsOrigin().Z - trace.EndPos.Z);

			//This is incredibly hacky. The real problem is that trace returning that strange value we can't network over.
			if (flDelta > 0.5f * COORD_RESOLUTION)
				mv.SetAbsOrigin(trace.EndPos);
		}
	}

	// Handle MoveType.Walk.
	protected virtual void FullWalkMove() {
		if (!CheckWater()) {
			StartGravity();
		}

		// If we are leaping out of the water, just update the counters.
		if (Player.WaterJumpTime != 0) {
			WaterJump();
			TryPlayerMove();
			// See if we are still in water?
			CheckWater();
			return;
		}

		// If we are swimming in the water, see if we are nudging against a place we can jump up out
		//  of, and, if so, start out jump.  Otherwise, if we are not moving up, then reset jump timer to 0
		if (Player.GetWaterLevel() >= WaterLevel.Waist) {
			if (Player.GetWaterLevel() == WaterLevel.Waist)
				CheckWaterJump();

			// If we are falling again, then we must not trying to jump out of water any more.
			if (mv!.Velocity[2] < 0 && Player.WaterJumpTime != 0)
				Player.WaterJumpTime = 0;

			// Was jump button pressed?
			if ((mv.Buttons & InButtons.Jump) != 0)
				CheckJumpButton();
			else
				mv.OldButtons &= ~InButtons.Jump;


			// Perform regular water movement
			WaterMove();

			// Redetermine position vars
			CategorizePosition();

			// If we are on ground, no downward velocity.
			if (Player.GetGroundEntity() != null)
				mv.Velocity[2] = 0;
		}
		else { // Not fully underwater
			   // Was jump button pressed?
			if ((mv!.Buttons & InButtons.Jump) != 0)
				CheckJumpButton();
			else
				mv.OldButtons &= ~InButtons.Jump;

			// Fricion is handled before we add in any base velocity. That way, if we are on a conveyor, 
			//  we don't slow when standing still, relative to the conveyor.
			if (Player.GetGroundEntity() != null) {
				mv.Velocity[2] = 0.0f;
				Friction();
			}

			// Make sure velocity is valid.
			CheckVelocity();

			if (Player.GetGroundEntity() != null)
				WalkMove();
			else
				AirMove();  // Take into account movement when in air.

			// Set final flags.
			CategorizePosition();

			// Make sure velocity is valid.
			CheckVelocity();

			// Add any remaining gravitational component.
			if (!CheckWater())
				FinishGravity();

			// If we are on ground, no downward velocity.
			if (Player.GetGroundEntity() != null)
				mv.Velocity[2] = 0;
			CheckFalling();
		}

		if ((OldWaterLevel == WaterLevel.NotInWater && Player.GetWaterLevel() != WaterLevel.NotInWater) ||
			  (OldWaterLevel != WaterLevel.NotInWater && Player.GetWaterLevel() == WaterLevel.NotInWater)) {
			PlaySwimSound();
#if !CLIENT_DLL
			// todo: player.Splash();
#endif
		}
	}

	// allow overridden versions to respond to jumping
	protected virtual void OnJump(float fImpulse) {

	}
	protected virtual void OnLand(float fVelocity) {

	}

	// Implement this if you want to know when the player collides during OnPlayerMove
	protected virtual void OnTryPlayerMoveCollision(ref Trace tr) { }

	protected virtual Vector3 GetPlayerMins() {
		if (Player.IsObserver())
			return VEC_OBS_HULL_MIN_SCALED(Player);
		else
			return Player.Local.Ducked ? VEC_DUCK_HULL_MIN_SCALED(Player) : VEC_HULL_MIN_SCALED(Player);
	}
	protected virtual Vector3 GetPlayerMaxs() {
		if (Player.IsObserver())
			return VEC_OBS_HULL_MAX_SCALED(Player);
		else
			return Player.Local.Ducked ? VEC_DUCK_HULL_MAX_SCALED(Player) : VEC_HULL_MAX_SCALED(Player);
	}

	protected enum IntervalType
	{
		Ground = 0,
		Stuck,
		Ladder
	}


	// Roughly how often we want to update the info about the ground surface we're on.
	// We don't need to do this very often.
	public const float CATEGORIZE_GROUND_SURFACE_INTERVAL = 0.3f;
	public static int CATEGORIZE_GROUND_SURFACE_TICK_INTERVAL => ((int)(CATEGORIZE_GROUND_SURFACE_INTERVAL / TICK_INTERVAL));


	public const float CHECK_STUCK_INTERVAL = 1.0f;
	public static int CHECK_STUCK_TICK_INTERVAL => ((int)(CHECK_STUCK_INTERVAL / TICK_INTERVAL));


	public const float CHECK_STUCK_INTERVAL_SP = 0.2f;
	public static int CHECK_STUCK_TICK_INTERVAL_SP => ((int)(CHECK_STUCK_INTERVAL_SP / TICK_INTERVAL));


	public const float CHECK_LADDER_INTERVAL = 0.2f;
	public static int CHECK_LADDER_TICK_INTERVAL => ((int)(CHECK_LADDER_INTERVAL / TICK_INTERVAL));


	// Useful for things that happen periodically. This lets things happen on the specified interval, but
	// spaces the events onto different frames for different players so they don't all hit their spikes
	// simultaneously.
	protected bool CheckInterval(IntervalType type) {
		int tickInterval = GetCheckInterval(type);

		if (g_bMovementOptimizations)
			return (Player!.CurrentCommandNumber() + Player.EntIndex()) % tickInterval == 0;
		else
			return true;
	}

	protected virtual int GetCheckInterval(IntervalType type) {
		int tickInterval = 1;
		switch (type) {
			default:
				tickInterval = 1;
				break;
			case IntervalType.Ground:
				tickInterval = CATEGORIZE_GROUND_SURFACE_TICK_INTERVAL;
				break;
			case IntervalType.Stuck:
				// If we are in the process of being "stuck", then try a new position every command tick until m_StuckLast gets reset back down to zero
				if (Player!.StuckLast != 0) {
					tickInterval = 1;
				}
				else {
					if (gpGlobals.MaxClients == 1) {
						tickInterval = CHECK_STUCK_TICK_INTERVAL_SP;
					}
					else {
						tickInterval = CHECK_STUCK_TICK_INTERVAL;
					}
				}
				break;
			case IntervalType.Ladder:
				tickInterval = CHECK_LADDER_TICK_INTERVAL;
				break;
		}
		return tickInterval;
	}


	// Decompoosed gravity
	protected void StartGravity() {
		float ent_gravity;

		if (Player.GetGravity() != 0)
			ent_gravity = Player.GetGravity();
		else
			ent_gravity = 1.0F;

		// Add gravity so they'll be in the correct position during movement
		// yes, this 0.5 looks wrong, but it's not.  
		mv!.Velocity[2] -= (ent_gravity * GetCurrentGravity() * 0.5F * (float)gpGlobals.FrameTime);
		mv!.Velocity[2] += Player.GetBaseVelocity()[2] * (float)gpGlobals.FrameTime;

		Vector3 temp = Player.GetBaseVelocity();
		temp[2] = 0;
		Player.SetBaseVelocity(temp);

		CheckVelocity();
	}
	protected void FinishGravity() {
		float ent_gravity;

		if (Player.WaterJumpTime != 0)
			return;

		if (Player.GetGravity() != 0)
			ent_gravity = Player.GetGravity();
		else
			ent_gravity = 1.0F;

		// Get the correct velocity for the end of the dt 
		mv!.Velocity[2] -= (ent_gravity * GetCurrentGravity() * (float)gpGlobals.FrameTime * 0.5F);

		CheckVelocity();
	}

	// Apply normal ( undecomposed ) gravity
	protected void AddGravity() {
		float ent_gravity;

		if (Player.WaterJumpTime != 0)
			return;

		if (Player.GetGravity() != 0)
			ent_gravity = Player.GetGravity();
		else
			ent_gravity = 1.0f;

		// Add gravity incorrectly
		mv!.Velocity[2] -= (ent_gravity * GetCurrentGravity() * (float)gpGlobals.FrameTime);
		mv!.Velocity[2] += Player.GetBaseVelocity()[2] * (float)gpGlobals.FrameTime;
		Vector3 temp = Player.GetBaseVelocity();
		temp[2] = 0;
		Player.SetBaseVelocity(temp);

		CheckVelocity();
	}

	// Handle movement in noclip mode.
	protected void FullNoClipMove_NoAcceleration(float speed, float maxacceleration) {
		Vector3 forward, right, up;
		MathLib.AngleVectors(mv!.ViewAngles, out forward, out right, out up);
		bool isValidMode = Player.GetMoveType() == MoveType.Noclip;

		Vector3 forwardScaled = forward * mv.ForwardMove;
		Vector3 rightScaled = right * mv.SideMove;

		float wantsUp;
		float wantsUpNorm;
		if (isValidMode && (mv.Buttons & InButtons.Jump) != 0) {
			wantsUp = 1.0f;
			wantsUpNorm = 1.0f;
		}
		else {
			wantsUp = 0.0f;
			wantsUpNorm = 1.0e-10f;
		}

		MathLib.VectorNormalizeFast(ref forwardScaled);
		MathLib.VectorNormalizeFast(ref rightScaled);

		float upInvSqrt = 1.0f / MathF.Sqrt(wantsUpNorm);
		float upNorm = upInvSqrt * ((3.0f - upInvSqrt * upInvSqrt * wantsUpNorm) * 0.5f);

		Vector3 wishdir = forwardScaled + rightScaled + new Vector3(0, 0, wantsUp * upNorm);
		MathLib.VectorNormalizeFast(ref wishdir);

		float multiplier = speed * 100.0f;
		Vector3 velocity = wishdir * multiplier;

		if (isValidMode) {
			if ((mv.Buttons & InButtons.Speed) != 0)
				velocity *= 3.0f;
			if ((mv.Buttons & InButtons.Duck) != 0)
				velocity *= 0.1f;
		}

		mv.Velocity = velocity;

		Vector3 newPos = mv.GetAbsOrigin() + mv.Velocity * (float)gpGlobals.FrameTime;
		mv.SetAbsOrigin(newPos);
	}
	protected void FullNoClipMove(float factor, float maxacceleration) {
		Vector3 wishvel = default;
		Vector3 forward, right, up;
		Vector3 wishdir;
		float wishspeed;
		float maxspeed = sv_maxspeed.GetFloat() * factor;

		MathLib.AngleVectors(mv.ViewAngles, out forward, out right, out up);  // Determine movement angles

		if ((mv.Buttons & InButtons.Speed) != 0)
			factor /= 2.0f;

		// Copy movement amounts
		float fmove = mv.ForwardMove * factor;
		float smove = mv.SideMove * factor;

		MathLib.VectorNormalizeFast(ref forward);  // Normalize remainder of vectors
		MathLib.VectorNormalizeFast(ref right);    // 

		for (int i = 0; i < 3; i++)       // Determine x and y parts of velocity
			wishvel[i] = forward[i] * fmove + right[i] * smove;
		wishvel[2] += mv.UpMove * factor;

		MathLib.VectorCopy(in wishvel, out wishdir);   // Determine maginitude of speed of move
		wishspeed = MathLib.VectorNormalizeFast(ref wishdir);

		//
		// Clamp to server defined max speed
		//
		if (wishspeed > maxspeed) {
			MathLib.VectorScale(wishvel, maxspeed / wishspeed, out wishvel);
			wishspeed = maxspeed;
		}

		if (maxacceleration > 0.0f) {
			// Set pmove velocity
			Accelerate(ref wishdir, wishspeed, maxacceleration);

			float spd = MathLib.VectorLength(mv.Velocity);
			if (spd < 1.0f) {
				mv.Velocity.Init();
				return;
			}

			float onequadspeed = maxspeed / 4.0f;
			// Bleed off some speed, but if we have less than the bleed
			//  threshhold, bleed the theshold amount.
			float control = (spd < onequadspeed) ? onequadspeed : spd;

			float friction = sv_friction.GetFloat() * Player.SurfaceFriction;

			// Add the amount to the drop amount.
			float drop = control * friction * (float)gpGlobals.FrameTime;

			// scale the velocity
			float newspeed = spd - drop;
			if (newspeed < 0)
				newspeed = 0;

			// Determine proportion of old speed we are using.
			newspeed /= spd;
			MathLib.VectorScale(mv.Velocity, newspeed, out mv.Velocity);
		}
		else
			MathLib.VectorCopy(wishvel, out mv.Velocity);

		// Just move ( don't clip or anything )
		MathLib.VectorMA(mv.GetAbsOrigin(), (float)gpGlobals.FrameTime, mv.Velocity, out Vector3 outVec);
		mv.SetAbsOrigin(outVec);

		// Zero out velocity if in noaccel mode
		if (maxacceleration < 0.0f)
			mv.Velocity.Init();
	}

	// Returns true if he started a jump (ie: should he play the jump animation)?
	protected virtual bool CheckJumpButton() {
		if (Player.pl.DeadFlag) {
			mv!.OldButtons |= InButtons.Jump;   // don't jump again until released
			return false;
		}

		// See if we are waterjumping.  If so, decrement count and return.
		if (Player.WaterJumpTime != 0) {
			Player.WaterJumpTime -= (float)gpGlobals.FrameTime;
			if (Player.WaterJumpTime < 0)
				Player.WaterJumpTime = 0;

			return false;
		}

		// If we are in the water most of the way...
		if ((int)Player.GetWaterLevel() >= 2) {
			// swimming, not jumping
			SetGroundEntity(ref Trace.NULL);

			if (Player.GetWaterType() == Contents.Water)    // We move up a certain amount
				mv!.Velocity[2] = 100;
			else if (Player.GetWaterType() == Contents.Slime)
				mv!.Velocity[2] = 80;

			// play swiming sound
			if (Player.SwimSoundTime <= 0) {
				// Don't play sound again for 1 second
				Player.SwimSoundTime = 1000;
				PlaySwimSound();
			}

			return false;
		}

		// No more effect
		if (Player.GetGroundEntity() == null) {
			mv.OldButtons |= InButtons.Jump;
			return false;       // in air, so no effect
		}

		// Don't allow jumping when the player is in a stasis field.
#if !HL2_EPISODIC
		if (Player.Local.SlowMovement)
			return false;
#endif

		if ((mv.OldButtons & InButtons.Jump) != 0)
			return false;       // don't pogo stick

		// Cannot jump will in the unduck transition.
		if (Player.Local.Ducking && (Player.GetFlags() & EntityFlags.Ducking) != 0)
			return false;

		// Still updating the eye position.
		if (Player.Local.DuckJumpTime > 0.0f)
			return false;


		// In the air now.
		SetGroundEntity(ref Trace.NULL);

		Player.PlayStepSound(mv.GetAbsOrigin(), Player.SurfaceData, 1.0f, true);

		MoveHelper().PlayerSetAnimation(PlayerAnim.Jump);

		float flGroundFactor = 1.0f;
		if (Player.SurfaceData != null)
			flGroundFactor = Player.SurfaceData.Game.JumpFactor;

#if GMOD_DLL
		float flMul = 0;
#if CLIENT_DLL
		if (Player is C_GMOD_Player gmodPlayer)
			flMul = gmodPlayer.JumpPower;
#else
		if (Player is GMOD_Player gmodPlayer)
			flMul = gmodPlayer.JumpPower;
#endif
#else
		float flMul;
		if (g_bMovementOptimizations)
			flMul = 268.3281572999747f;
		else
			flMul = MathF.Sqrt(2 * GetCurrentGravity() * GAMEMOVEMENT_JUMP_HEIGHT);
#endif
		// Acclerate upward
		// If we are ducking...
		float startz = mv.Velocity[2];
		if ((Player.Local.Ducking) || (Player.GetFlags() & EntityFlags.Ducking) != 0) {
			// d = 0.5 * g * t^2		- distance traveled with linear accel
			// t = sqrt(2.0 * 45 / g)	- how long to fall 45 units
			// v = g * t				- velocity at the end (just invert it to jump up that high)
			// v = g * sqrt(2.0 * 45 / g )
			// v^2 = g * g * 2.0 * 45 / g
			// v = sqrt( g * 2.0 * 45 )
			mv.Velocity[2] = flGroundFactor * flMul;  // 2 * gravity * height
		}
		else {
			mv.Velocity[2] += flGroundFactor * flMul;  // 2 * gravity * height
		}

		FinishGravity();

		mv.JumpVel.Z += mv.Velocity[2] - startz;
		mv.StepHeight += 0.15f;

		// Add a little forward velocity based on your current forward velocity - if you are not sprinting.
#if HL2_DLL || HL2_CLIENT_DLL
		if (gpGlobals.MaxClients == 1) {
			HLMoveData pMoveData = (HLMoveData)mv;
			MathLib.AngleVectors(mv.ViewAngles, out Vector3 vecForward);
			vecForward.Z = 0;
			MathLib.VectorNormalize(ref vecForward);

			// We give a certain percentage of the current forward movement as a bonus to the jump speed.  That bonus is clipped
			// to not accumulate over time.
			float flSpeedBoostPerc = (!pMoveData.IsSprinting && !Player.Local.Ducked) ? 0.5f : 0.1f;
			float flSpeedAddition = MathF.Abs(mv.ForwardMove * flSpeedBoostPerc);
			float flMaxSpeed = mv.MaxSpeed + (mv.MaxSpeed * flSpeedBoostPerc);
			float flNewSpeed = (flSpeedAddition + mv.Velocity.Length2D());

			// If we're over the maximum, we want to only boost as much as will get us to the goal speed
			if (flNewSpeed > flMaxSpeed) {
				flSpeedAddition -= flNewSpeed - flMaxSpeed;
			}

			if (mv.ForwardMove < 0.0f)
				flSpeedAddition *= -1.0f;

			// Add it on
			MathLib.VectorAdd((vecForward * flSpeedAddition), mv.Velocity, out mv.Velocity);
		}
#endif

		OnJump(mv.JumpVel.Z);

		// Flag that we jumped.
		mv.OldButtons |= InButtons.Jump;   // don't jump again until released
		return true;
	}    // Overridden by each game.

	// Dead player flying through air., e.g.
	protected virtual void FullTossMove() {
		Trace pm = default;
		Vector3 move;

		CheckWater();

		Assert(mv != null);

		// add velocity if player is moving 
		if ((mv.ForwardMove != 0.0f) || (mv.SideMove != 0.0f) || (mv.UpMove != 0.0f)) {
			Vector3 forward, right, up;
			float fmove, smove;
			Vector3 wishdir = default, wishvel = default;
			float wishspeed;
			int i;

			MathLib.AngleVectors(mv.ViewAngles, out forward, out right, out up);  // Determine movement angles

			// Copy movement amounts
			fmove = mv.ForwardMove;
			smove = mv.SideMove;

			MathLib.VectorNormalizeFast(ref forward);  // Normalize remainder of vectors.
			MathLib.VectorNormalizeFast(ref right);    // 

			for (i = 0; i < 3; i++)       // Determine x and y parts of velocity
				wishvel[i] = forward[i] * fmove + right[i] * smove;

			wishvel[2] += mv.UpMove;

			MathLib.VectorCopy(wishvel, out wishdir);   // Determine maginitude of speed of move
			wishspeed = MathLib.VectorNormalizeFast(ref wishdir);

			//
			// Clamp to server defined max speed
			//
			if (wishspeed > mv.MaxSpeed) {
				MathLib.VectorScale(wishvel, mv.MaxSpeed / wishspeed, out wishvel);
				wishspeed = mv.MaxSpeed;
			}

			// Set pmove velocity
			Accelerate(ref wishdir, wishspeed, sv_accelerate.GetFloat());
		}

		if (mv.Velocity[2] > 0)
			SetGroundEntity();

		// If on ground and not moving, return.
		if (Player.GetGroundEntity() != null) {
			if (MathLib.VectorCompare(Player.GetBaseVelocity(), vec3_origin) && MathLib.VectorCompare(mv!.Velocity, vec3_origin))
				return;
		}

		CheckVelocity();

		// add gravity
		if (Player.GetMoveType() == MoveType.FlyGravity) {
			AddGravity();
		}

		// move origin
		// Base velocity is not properly accounted for since this entity will move again after the bounce without
		// taking it into account
		MathLib.VectorAdd(mv!.Velocity, Player.GetBaseVelocity(), out mv.Velocity);

		CheckVelocity();

		MathLib.VectorScale(mv!.Velocity, (float)gpGlobals.FrameTime, out move);
		MathLib.VectorSubtract(mv!.Velocity, Player.GetBaseVelocity(), out mv.Velocity);

		PushEntity(ref move, ref pm);  // Should this clear basevelocity

		CheckVelocity();

		if (pm.AllSolid) {
			// entity is trapped in another solid
			SetGroundEntity(ref pm);
			mv.Velocity.Init();
			return;
		}

		if (pm.Fraction != 1)
			PerformFlyCollisionResolution(ref pm, ref move);

		// check for in water
		CheckWater();
	}

	// Player is a Observer chasing another player
	protected void FullObserverMove() { throw new NotImplementedException(); }

	// Handle movement when in MoveType.Ladder mode.
	protected virtual void FullLadderMove() {
		CheckWater();

		// Was jump button pressed? If so, set velocity to 270 away from ladder.  
		if ((mv!.Buttons & InButtons.Jump) != 0)
			CheckJumpButton();
		else
			mv!.OldButtons &= ~InButtons.Jump;

		// Perform the move accounting for any base velocity.
		MathLib.VectorAdd(mv!.Velocity, Player!.GetBaseVelocity(), out mv.Velocity);
		TryPlayerMove();
		MathLib.VectorSubtract(mv!.Velocity, Player!.GetBaseVelocity(), out mv.Velocity);
	}

	public const float STOP_EPSILON = 0.1f;
	public const int MAX_CLIP_PLANES = 5;


	// The basic solid body movement clip that slides along multiple planes
	protected virtual int TryPlayerMove(ref Vector3 firstDest, ref Trace firstTrace) {
		int bumpcount, numbumps;
		Vector3 dir;
		float d;
		int numplanes;
		Span<Vector3> planes = stackalloc Vector3[MAX_CLIP_PLANES];
		Vector3 primal_velocity, original_velocity;
		Vector3 new_velocity = default;
		int i, j;
		Trace pm = default;
		Vector3 end;
		float time_left, allFraction;
		int blocked;

		numbumps = 4;           // Bump up to four times

		blocked = 0;           // Assume not blocked
		numplanes = 0;           //  and not sliding along any planes

		MathLib.VectorCopy(mv!.Velocity, out original_velocity);  // Store original velocity
		MathLib.VectorCopy(mv!.Velocity, out primal_velocity);

		allFraction = 0;
		time_left = (float)gpGlobals.FrameTime;   // Total time for this movement operation.

		new_velocity.Init();

		for (bumpcount = 0; bumpcount < numbumps; bumpcount++) {
			if (mv.Velocity.Length() == 0.0)
				break;

			// Assume we can move all the way from the current origin to the
			//  end point.
			MathLib.VectorMA(mv.GetAbsOrigin(), time_left, mv.Velocity, out end);

			// See if we can make it from origin to end point.
			if (g_bMovementOptimizations) {
				// If their velocity Z is 0, then we can avoid an extra trace here during WalkMove.
				if (!Unsafe.IsNullRef(ref firstDest) && end == firstDest)
					pm = firstTrace;
				else
					TracePlayerBBox(mv.GetAbsOrigin(), end, PlayerSolidMask(), CollisionGroup.PlayerMovement, out pm);
			}
			else
				TracePlayerBBox(mv.GetAbsOrigin(), end, PlayerSolidMask(), CollisionGroup.PlayerMovement, out pm);


			allFraction += pm.Fraction;

			// If we started in a solid object, or we were in solid space
			//  the whole way, zero out our velocity and return that we
			//  are blocked by floor and wall.
			if (pm.AllSolid) {
				// entity is trapped in another solid
				MathLib.VectorCopy(vec3_origin, out mv.Velocity);
				return 4;
			}

			// If we moved some portion of the total distance, then
			//  copy the end position into the pmove.origin and 
			//  zero the plane counter.
			if (pm.Fraction > 0) {
				if (numbumps > 0 && pm.Fraction == 1) {
					// There's a precision issue with terrain tracing that can cause a swept box to successfully trace
					// when the end position is stuck in the triangle.  Re-run the test with an uswept box to catch that
					// case until the bug is fixed.
					// If we detect getting stuck, don't allow the movement
					Trace stuck;
					TracePlayerBBox(pm.EndPos, pm.EndPos, PlayerSolidMask(), CollisionGroup.PlayerMovement, out stuck);
					if (stuck.StartSolid || stuck.Fraction != 1.0f) {
						//Msg( "Player will become stuck!!!\n" );
						MathLib.VectorCopy(vec3_origin, out mv.Velocity);
						break;
					}
				}

				// actually covered some distance
				mv.SetAbsOrigin(pm.EndPos);
				MathLib.VectorCopy(mv.Velocity, out original_velocity);
				numplanes = 0;
			}

			// If we covered the entire distance, we are done
			//  and can return.
			if (pm.Fraction == 1)
				break;      // moved the entire distance

			// Save entity that blocked us (since fraction was < 1.0)
			//  for contact
			// Add it if it's not already in the list!!!
			MoveHelper().AddToTouched(ref pm, mv.Velocity);

			// If the plane we hit has a high z component in the normal, then
			//  it's probably a floor
			if (pm.Plane.Normal[2] > 0.7)
				blocked |= 1;       // floor

			// If the plane has a zero z component in the normal, then it's a 
			//  step or wall
			if (0 == pm.Plane.Normal[2])
				blocked |= 2;       // step / wall

			// Reduce amount of m_flFrameTime left by total time left * fraction
			//  that we covered.
			time_left -= time_left * pm.Fraction;

			// Did we run out of planes to clip against?
			if (numplanes >= MAX_CLIP_PLANES) {
				// this shouldn't really happen
				//  Stop our movement if so.
				MathLib.VectorCopy(vec3_origin, out mv.Velocity);
				//Con_DPrintf("Too many planes 4\n");

				break;
			}

			// Set up next clipping plane
			MathLib.VectorCopy(pm.Plane.Normal, out planes[numplanes]);
			numplanes++;

			// modify original_velocity so it parallels all of the clip planes
			//

			// reflect player velocity 
			// Only give this a try for first impact plane because you can get yourself stuck in an acute corner by jumping in place
			//  and pressing forward and nobody was really using this bounce/reflection feature anyway...
			if (numplanes == 1 && Player.GetMoveType() == MoveType.Walk && Player.GetGroundEntity() == null) {
				for (i = 0; i < numplanes; i++) {
					if (planes[i][2] > 0.7f) {
						// floor or slope
						ClipVelocity(original_velocity, planes[i], out new_velocity, 1);
						MathLib.VectorCopy(new_velocity, out original_velocity);
					}
					else
						ClipVelocity(original_velocity, planes[i], out new_velocity, 1.0f + sv_bounce.GetFloat() * (1 - Player.SurfaceFriction));
				}

				MathLib.VectorCopy(new_velocity, out mv.Velocity);
				MathLib.VectorCopy(new_velocity, out original_velocity);
			}
			else {
				for (i = 0; i < numplanes; i++) {
					ClipVelocity(original_velocity, planes[i], out mv.Velocity, 1);

					for (j = 0; j < numplanes; j++)
						if (j != i) {
							// Are we now moving against this plane?
							if (mv.Velocity.Dot(planes[j]) < 0)
								break;  // not ok
						}
					if (j == numplanes)  // Didn't have to clip, so we're ok
						break;
				}

				// Did we go all the way through plane set
				if (i != numplanes) {   // go along this plane
										// pmove.velocity is set in clipping call, no need to set again.
					;
				}
				else {  // go along the crease
					if (numplanes != 2) {
						MathLib.VectorCopy(vec3_origin, out mv.Velocity);
						break;
					}
					MathLib.CrossProduct(planes[0], planes[1], out dir);
					dir.NormalizeInPlace();
					d = dir.Dot(mv.Velocity);
					MathLib.VectorScale(dir, d, out mv.Velocity);
				}

				//
				// if original velocity is against the original velocity, stop dead
				// to avoid tiny occilations in sloping corners
				//
				d = mv.Velocity.Dot(primal_velocity);
				if (d <= 0) {
					//Con_DPrintf("Back\n");
					MathLib.VectorCopy(vec3_origin, out mv.Velocity);
					break;
				}
			}
		}

		if (allFraction == 0)
			MathLib.VectorCopy(vec3_origin, out mv.Velocity);

		// Check if they slammed into a wall
		float fSlamVol = 0.0f;

		float fLateralStoppingAmount = primal_velocity.Length2D() - mv.Velocity.Length2D();
		if (fLateralStoppingAmount > PLAYER_MAX_SAFE_FALL_SPEED * 2.0f)
			fSlamVol = 1.0f;
		else if (fLateralStoppingAmount > PLAYER_MAX_SAFE_FALL_SPEED)
			fSlamVol = 0.85f;

		PlayerRoughLandingEffects(fSlamVol);

		return blocked;
	}
	protected int TryPlayerMove(ref Vector3 firstDest) => TryPlayerMove(ref firstDest, ref Unsafe.NullRef<Trace>());
	protected int TryPlayerMove() => TryPlayerMove(ref Unsafe.NullRef<Vector3>(), ref Unsafe.NullRef<Trace>());

	protected virtual bool LadderMove() {
		Trace pm;
		bool onFloor;
		Vector3 floor = default;
		Vector3 wishdir = default;
		Vector3 end = default;

		if (Player.GetMoveType() == MoveType.Noclip)
			return false;

		if (!GameHasLadders())
			return false;

		// If I'm already moving on a ladder, use the previous ladder direction
		if (Player.GetMoveType() == MoveType.Ladder)
			wishdir = -Player.LadderNormal;
		else {
			// otherwise, use the direction player is attempting to move
			if (mv!.ForwardMove != 0 || mv.SideMove != 0) {
				for (int i = 0; i < 3; i++)       // Determine x and y parts of velocity
					wishdir[i] = Forward[i] * mv.ForwardMove + Right[i] * mv.SideMove;

				MathLib.VectorNormalizeFast(ref wishdir);
			}
			else {
				// Player is not attempting to move, no ladder behavior
				return false;
			}
		}

		// wishdir points toward the ladder if any exists
		MathLib.VectorMA(mv!.GetAbsOrigin(), LadderDistance(), wishdir, out end);
		TracePlayerBBox(mv!.GetAbsOrigin(), end, LadderMask(), CollisionGroup.PlayerMovement, out pm);

		// no ladder in that direction, return
		if (pm.Fraction == 1.0f || !OnLadder(ref pm))
			return false;

		Player.SetMoveType(MoveType.Ladder);
		Player.SetMoveCollide(MoveCollide.Default);

		Player.LadderNormal = pm.Plane.Normal;

		// On ladder, convert movement to be relative to the ladder

		MathLib.VectorCopy(mv.GetAbsOrigin(), out floor);
		floor[2] += GetPlayerMins()[2] - 1;

		if (enginetrace.GetPointContents(floor, out _) == Contents.Solid || Player.GetGroundEntity() != null)
			onFloor = true;
		else
			onFloor = false;

		Player.SetGravity(0);

		float climbSpeed = ClimbSpeed();

		float forwardSpeed = 0, rightSpeed = 0;
		if ((mv.Buttons & InButtons.Back) != 0)
			forwardSpeed -= climbSpeed;

		if ((mv.Buttons & InButtons.Forward) != 0)
			forwardSpeed += climbSpeed;

		if ((mv.Buttons & InButtons.MoveLeft) != 0)
			rightSpeed -= climbSpeed;

		if ((mv.Buttons & InButtons.MoveRight) != 0)
			rightSpeed += climbSpeed;

		if ((mv.Buttons & InButtons.Jump) != 0) {
			Player.SetMoveType(MoveType.Walk);
			Player.SetMoveCollide(MoveCollide.Default);

			MathLib.VectorScale(pm.Plane.Normal, 270, out mv.Velocity);
		}
		else {
			if (forwardSpeed != 0 || rightSpeed != 0) {
				Vector3 velocity, perp, cross, lateral, tmp;

				//ALERT(at_console, "pev %.2f %.2f %.2f - ",
				//	pev.velocity.x, pev.velocity.y, pev.velocity.z);
				// Calculate player's intended velocity
				//Vector velocity = (forward * gpGlobals.v_forward) + (right * gpGlobals.v_right);
				MathLib.VectorScale(Forward, forwardSpeed, out velocity);
				MathLib.VectorMA(velocity, rightSpeed, Right, out velocity);

				// Perpendicular in the ladder plane
				MathLib.VectorCopy(vec3_origin, out tmp);
				tmp[2] = 1;
				MathLib.CrossProduct(tmp, pm.Plane.Normal, out perp);
				MathLib.VectorNormalizeFast(ref perp);

				// decompose velocity into ladder plane
				float normal = MathLib.DotProduct(velocity, pm.Plane.Normal);

				// This is the velocity into the face of the ladder
				MathLib.VectorScale(pm.Plane.Normal, normal, out cross);

				// This is the player's additional velocity
				MathLib.VectorSubtract(velocity, cross, out lateral);

				// This turns the velocity into the face of the ladder into velocity that
				// is roughly vertically perpendicular to the face of the ladder.
				// NOTE: It IS possible to face up and move down or face down and move up
				// because the velocity is a sum of the directional velocity and the converted
				// velocity through the face of the ladder -- by design.
				MathLib.CrossProduct(pm.Plane.Normal, perp, out tmp);

				//=============================================================================
				// HPE_BEGIN
				// [sbodenbender] make ladders easier to climb in cstrike
				//=============================================================================

				//=============================================================================
				// HPE_END
				//=============================================================================

				MathLib.VectorMA(lateral, -normal, tmp, out mv.Velocity);

				if (onFloor && normal > 0)  // On ground moving away from the ladder
					MathLib.VectorMA(mv.Velocity, MAX_CLIMB_SPEED, pm.Plane.Normal, out mv.Velocity);
				//pev.velocity = lateral - (CrossProduct( trace.vecPlaneNormal, perp ) * normal);
			}
			else
				mv.Velocity.Init();
		}

		return true;
	}
	protected virtual bool OnLadder(ref Trace trace) {
		if ((trace.Contents & Contents.Ladder) != 0)
			return true;

		IPhysicsSurfaceProps? physProps = MoveHelper().GetSurfaceProps();
		if (physProps != null) {
			SurfaceData_ptr? surfaceData = physProps.GetSurfaceData(trace.Surface.SurfaceProps);
			if (surfaceData != null)
				if (surfaceData.Struct.Game.Climbable != 0)
					return true;

		}

		return false;
	}
	protected virtual float LadderDistance() => 2.0f;   ///< Returns the distance a player can be from a ladder and still attach to it
	protected virtual Mask LadderMask() => Mask.PlayerSolid;
	protected virtual float ClimbSpeed() => MAX_CLIMB_SPEED;
	protected virtual float LadderLateralMultiplier() => 1.0f;

	static ReadOnlySpan<char> DescribeAxis(int axis) {
		switch (axis) {
			case 0:
				return "X";
			case 1:
				return "Y";
			case 2:
			default:
				return "Z";
		}
	}
	// See if the player has a bogus velocity value.
	protected void CheckVelocity() {

		int i;

		//
		// bound velocity
		//

		Vector3 org = mv!.GetAbsOrigin();

		for (i = 0; i < 3; i++) {
			// See if it's bogus.
			if (float.IsNaN(mv.Velocity[i])) {
				DevMsg(1, $"PM  Got a NaN velocity {DescribeAxis(i)}\n");
				mv.Velocity[i] = 0;
			}

			if (float.IsNaN(org[i])) {
				DevMsg(1, $"PM  Got a NaN origin on {DescribeAxis(i)}\n");
				org[i] = 0;
				mv.SetAbsOrigin(org);
			}

			// Bound it.
			if (mv.Velocity[i] > sv_maxvelocity.GetFloat()) {
				DevMsg(1, $"PM  Got a velocity too high on {DescribeAxis(i)}\n");
				mv.Velocity[i] = sv_maxvelocity.GetFloat();
			}
			else if (mv.Velocity[i] < -sv_maxvelocity.GetFloat()) {
				DevMsg(1, $"PM  Got a velocity too low on {DescribeAxis(i)}\n");
				mv.Velocity[i] = -sv_maxvelocity.GetFloat();
			}
		}
	}

	// Does not change the entities velocity at all
	protected void PushEntity(ref Vector3 push, ref Trace trace) {
		Vector3 end;

		MathLib.VectorAdd(mv!.GetAbsOrigin(), push, out end);
		TracePlayerBBox(mv.GetAbsOrigin(), end, PlayerSolidMask(), CollisionGroup.PlayerMovement, out trace);
		mv!.SetAbsOrigin(trace.EndPos);

		// So we can run impact function afterwards.
		// If
		if (trace.Fraction < 1.0 && !trace.AllSolid)
			MoveHelper().AddToTouched(ref trace, in mv!.Velocity);
	}

	// Slide off of the impacting object
	// returns the blocked flags:
	// 0x01 == floor
	// 0x02 == step / wall
	protected int ClipVelocity(in Vector3 inVec, in Vector3 normal, out Vector3 outVec, float overbounce) {
		float backoff;
		float change;
		float angle;
		int i, blocked;

		angle = normal[2];
		outVec = default;

		blocked = 0x00;         // Assume unblocked.
		if (angle > 0)          // If the plane that is blocking us has a positive z component, then assume it's a floor.
			blocked |= 0x01;    // 
		if (angle == 0)             // If the plane has no Z, it is vertical (wall/step)
			blocked |= 0x02;    // 


		// Determine how far along plane to slide based on incoming direction.
		backoff = MathLib.DotProduct(inVec, normal) * overbounce;

		for (i = 0; i < 3; i++) {
			change = normal[i] * backoff;
			outVec[i] = inVec[i] - change;
		}

		// iterate once to make sure we aren't still moving through the plane
		float adjust = MathLib.DotProduct(outVec, normal);
		if (adjust < 0.0f) {
			outVec -= (normal * adjust);
			//		Msg( "Adjustment = %lf\n", adjust );
		}

		// Return blocking flags.
		return blocked;
	}

	const TimeUnit_t CHECKSTUCK_MINTIME = 0.05;

	ConVarRef developer = new ConVarRef("developer");

	// If pmove.origin is in a solid position,
	// try nudging slightly on all axis to
	// allow for the cut precision of the net coordinates
	protected virtual bool CheckStuck() {
		Vector3 vecBase;
		Vector3 offset;
		Vector3 test;
		EntityHandle_t hitent;
		int idx;
		TimeUnit_t time;
		Trace traceresult;

		CreateStuckTable();

		hitent = TestPlayerPosition(mv!.GetAbsOrigin(), CollisionGroup.PlayerMovement, out traceresult);
		if (hitent.Index == INVALID_ENTITY_HANDLE) {
			ResetStuckOffsets(Player);
			return false;
		}

		// Deal with stuckness...
#if !DEDICATED
		if (developer.GetBool()) {
			bool isServer = Player.IsServer();
			engine.Con_NPrintf(isServer ? 1 : 0, $"{(isServer ? "server" : "client")} stuck on object {hitent.GetEntryIndex()}/{MoveHelper().GetName(hitent)}");
		}
#endif

		MathLib.VectorCopy(mv.GetAbsOrigin(), out vecBase);

		// 
		// Deal with precision error in network.
		// 
		// World or BSP model
		if (!Player.IsServer()) {
			if (MoveHelper().IsWorldEntity(hitent)) {
				int nReps = 0;
				ResetStuckOffsets(Player);
				do {
					GetRandomStuckOffsets(Player, out offset);
					MathLib.VectorAdd(vecBase, offset, out test);

					if (TestPlayerPosition(test, CollisionGroup.PlayerMovement, out traceresult).Index == INVALID_ENTITY_HANDLE) {
						ResetStuckOffsets(Player);
						mv.SetAbsOrigin(test);
						return false;
					}
					nReps++;
				} while (nReps < 54);
			}
		}

		// Only an issue on the client.
		idx = Player.IsServer() ? 0 : 1;

		time = engine.Time();
		// Too soon?
		if (StuckCheckTime[Player.EntIndex(), idx] >= time - CHECKSTUCK_MINTIME)
			return true;

		StuckCheckTime[Player.EntIndex(), idx] = time;

		MoveHelper().AddToTouched(ref traceresult, mv.Velocity);
		GetRandomStuckOffsets(Player, out offset);
		MathLib.VectorAdd(vecBase, offset, out test);

		if (TestPlayerPosition(test, CollisionGroup.PlayerMovement, out traceresult).Index == INVALID_ENTITY_HANDLE) {
			ResetStuckOffsets(Player);
			mv.SetAbsOrigin(test);
			return false;
		}

		return true;
	}

	static bool CreateStuckTable_firsttime = true;
	static readonly Vector3[] rgv3tStuckTable = new Vector3[54];

	private static int GetRandomStuckOffsets(BasePlayer player, out Vector3 offset) {
		int idx;
		idx = player.StuckLast++;

		MathLib.VectorCopy(rgv3tStuckTable[idx % 54], out offset);

		return (idx % 54);
	}
	private static void ResetStuckOffsets(BasePlayer player) => player.StuckLast = 0;

	private static void CreateStuckTable() {
		float x, y, z;
		int idx;
		int i;
		Span<float> zi = stackalloc float[3];

		if (!CreateStuckTable_firsttime)
			return;

		CreateStuckTable_firsttime = false;

		memreset(rgv3tStuckTable);

		idx = 0;
		// Little Moves.
		x = y = 0;
		// Z moves
		for (z = -0.125f; z <= 0.125f; z += 0.125f) {
			rgv3tStuckTable[idx][0] = x;
			rgv3tStuckTable[idx][1] = y;
			rgv3tStuckTable[idx][2] = z;
			idx++;
		}
		x = z = 0;
		// Y moves
		for (y = -0.125f; y <= 0.125f; y += 0.125f) {
			rgv3tStuckTable[idx][0] = x;
			rgv3tStuckTable[idx][1] = y;
			rgv3tStuckTable[idx][2] = z;
			idx++;
		}
		y = z = 0;
		// X moves
		for (x = -0.125f; x <= 0.125f; x += 0.125f) {
			rgv3tStuckTable[idx][0] = x;
			rgv3tStuckTable[idx][1] = y;
			rgv3tStuckTable[idx][2] = z;
			idx++;
		}

		// Remaining multi axis nudges.
		for (x = -0.125f; x <= 0.125f; x += 0.250f) {
			for (y = -0.125f; y <= 0.125f; y += 0.250f) {
				for (z = -0.125f; z <= 0.125f; z += 0.250f) {
					rgv3tStuckTable[idx][0] = x;
					rgv3tStuckTable[idx][1] = y;
					rgv3tStuckTable[idx][2] = z;
					idx++;
				}
			}
		}

		// Big Moves.
		x = y = 0;
		zi[0] = 0.0f;
		zi[1] = 1.0f;
		zi[2] = 6.0f;

		for (i = 0; i < 3; i++) {
			// Z moves
			z = zi[i];
			rgv3tStuckTable[idx][0] = x;
			rgv3tStuckTable[idx][1] = y;
			rgv3tStuckTable[idx][2] = z;
			idx++;
		}

		x = z = 0;

		// Y moves
		for (y = -2.0f; y <= 2.0f; y += 2.0f) {
			rgv3tStuckTable[idx][0] = x;
			rgv3tStuckTable[idx][1] = y;
			rgv3tStuckTable[idx][2] = z;
			idx++;
		}
		y = z = 0;
		// X moves
		for (x = -2.0f; x <= 2.0f; x += 2.0f) {
			rgv3tStuckTable[idx][0] = x;
			rgv3tStuckTable[idx][1] = y;
			rgv3tStuckTable[idx][2] = z;
			idx++;
		}

		// Remaining multi axis nudges.
		for (i = 0; i < 3; i++) {
			z = zi[i];

			for (x = -2.0f; x <= 2.0f; x += 2.0f) {
				for (y = -2.0f; y <= 2.0f; y += 2.0f) {
					rgv3tStuckTable[idx][0] = x;
					rgv3tStuckTable[idx][1] = y;
					rgv3tStuckTable[idx][2] = z;
					idx++;
				}
			}
		}
		Assert(idx < rgv3tStuckTable.Length);
	}

	// Check if the point is in water.
	// Sets refWaterLevel and refWaterType appropriately.
	// If in water, applies current to baseVelocity, and returns true.
	protected virtual bool CheckWater() {
		Vector3 point = default;
		Contents cont;

		Vector3 vPlayerMins = GetPlayerMins();
		Vector3 vPlayerMaxs = GetPlayerMaxs();

		// Pick a spot just above the players feet.
		point[0] = mv!.GetAbsOrigin()[0] + (vPlayerMins[0] + vPlayerMaxs[0]) * 0.5f;
		point[1] = mv!.GetAbsOrigin()[1] + (vPlayerMins[1] + vPlayerMaxs[1]) * 0.5f;
		point[2] = mv!.GetAbsOrigin()[2] + vPlayerMins[2] + 1;

		// Assume that we are not in water at all.
		Player.SetWaterLevel(WaterLevel.NotInWater);
		Player.SetWaterType(Contents.Empty);

		// Grab point contents.
		cont = GetPointContentsCached(point, 0);

		// Are we under water? (not solid and not empty?)
		if ((cont & (Contents)Mask.Water) != 0) {
			// Set water type
			Player.SetWaterType(cont);

			// We are at least at level one
			Player.SetWaterLevel(WaterLevel.Feet);

			// Now check a point that is at the player hull midpoint.
			point[2] = mv.GetAbsOrigin()[2] + (vPlayerMins[2] + vPlayerMaxs[2]) * 0.5f;
			cont = GetPointContentsCached(point, 1);
			// If that point is also under water...
			if ((cont & (Contents)Mask.Water) != 0) {
				// Set a higher water level.
				Player.SetWaterLevel(WaterLevel.Waist);

				// Now check the eye position.  (view_ofs is relative to the origin)
				point[2] = Player.GetAbsOrigin()[2] + Player.GetViewOffset()[2];
				cont = GetPointContentsCached(point, 2);
				if ((cont & (Contents)Mask.Water) != 0)
					Player.SetWaterLevel(WaterLevel.Eyes);  // In over our eyes
			}

			// Adjust velocity based on water current, if any.
			if ((cont & (Contents)Mask.Current) != 0) {
				Vector3 v = default;
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

				// BUGBUG -- this depends on the value of an unspecified enumerated type
				// The deeper we are, the stronger the current.
				MathLib.VectorMA(Player.GetBaseVelocity(), 50.0f * (int)Player.GetWaterLevel(), v, out Vector3 temp);
				Player.SetBaseVelocity(temp);
			}
		}

		// if we just transitioned from not in water to in water, record the time it happened
		if ((WaterLevel.NotInWater == OldWaterLevel) && (Player.GetWaterLevel() > WaterLevel.NotInWater))
			WaterEntryTime = gpGlobals.CurTime;

		return (Player.GetWaterLevel() > WaterLevel.Feet);
	}

	// Determine if player is in water, on ground, etc.
	protected virtual void CategorizePosition() {
		Vector3 point = default;
		Trace pm;

		// Reset this each time we-recategorize, otherwise we have bogus friction when we jump into water and plunge downward really quickly
		Player.SurfaceFriction = 1.0f;

		// if the player hull point one unit down is solid, the player
		// is on ground

		// see if standing on something solid	

		// Doing this before we move may introduce a potential latency in water detection, but
		// doing it after can get us stuck on the bottom in water if the amount we move up
		// is less than the 1 pixel 'threshold' we're about to snap to.	Also, we'll call
		// this several times per frame, so we really need to avoid sticking to the bottom of
		// water on each call, and the converse case will correct itself if called twice.
		CheckWater();

		// observers don't have a ground entity
		if (Player.IsObserver())
			return;

		float flOffset = 2.0f;

		point[0] = mv!.GetAbsOrigin()[0];
		point[1] = mv!.GetAbsOrigin()[1];
		point[2] = mv!.GetAbsOrigin()[2] - flOffset;

		Vector3 bumpOrigin = mv!.GetAbsOrigin();

		// Shooting up really fast.  Definitely not on ground.
		// On ladder moving up, so not on ground either
		// NOTE: 145 is a jump.
		const float NON_JUMP_VELOCITY = 140.0f;

		float zvel = mv.Velocity[2];
		bool bMovingUp = zvel > 0.0f;
		bool bMovingUpRapidly = zvel > NON_JUMP_VELOCITY;
		float flGroundEntityVelZ = 0.0f;
		if (bMovingUpRapidly) {
			// Tracker 73219, 75878:  ywb 8/2/07
			// After save/restore (and maybe at other times), we can get a case where we were saved on a lift and 
			//  after restore we'll have a high local velocity due to the lift making our abs velocity appear high.  
			// We need to account for standing on a moving ground object in that case in order to determine if we really 
			//  are moving away from the object we are standing on at too rapid a speed.  Note that CheckJump already sets
			//  ground entity to NULL, so this wouldn't have any effect unless we are moving up rapidly not from the jump button.
			BaseEntity? ground = Player.GetGroundEntity();
			if (ground != null) {
				flGroundEntityVelZ = ground.GetAbsVelocity().Z;
				bMovingUpRapidly = (zvel - flGroundEntityVelZ) > NON_JUMP_VELOCITY;
			}
		}

		// Was on ground, but now suddenly am not
		if (bMovingUpRapidly ||
			(bMovingUp && Player.GetMoveType() == MoveType.Ladder)) {
			SetGroundEntity(ref Trace.NULL);
		}
		else {
			// Try and move down.
			TryTouchGround(bumpOrigin, point, GetPlayerMins(), GetPlayerMaxs(), Mask.PlayerSolid, CollisionGroup.PlayerMovement, out pm);
			// Was on ground, but now suddenly am not.  If we hit a steep plane, we are not on ground
			if (pm.Ent == null || pm.Plane.Normal[2] < 0.7) {
				// Test four sub-boxes, to see if any of them would have found shallower slope we could actually stand on
				TryTouchGroundInQuadrants(bumpOrigin, point, Mask.PlayerSolid, CollisionGroup.PlayerMovement, ref pm);

				if (pm.Ent == null || pm.Plane.Normal[2] < 0.7) {
					SetGroundEntity();
					// probably want to add a check for a +z velocity too!
					if ((mv.Velocity.Z > 0.0f) && (Player.GetMoveType() != MoveType.Noclip))
						Player.SurfaceFriction = 0.25f;
				}
				else {
					SetGroundEntity(ref pm);
				}
			}
			else
				SetGroundEntity(ref pm);  // Otherwise, point to index of ent under us.

#if !CLIENT_DLL

			//Adrian: vehicle code handles for us.
			if (Player.IsInAVehicle() == false) {
				// todo
			}
#endif
		}
	}

	public void TryTouchGround(in Vector3 start, in Vector3 end, in Vector3 mins, in Vector3 maxs, Mask mask, CollisionGroup collisionGroup, out Trace pm) {
		Ray ray = default;
		ray.Init(start, end, mins, maxs);
		Util.TraceRay(ray, mask, mv!.PlayerHandle.Get(), collisionGroup, out pm);
	}

	protected virtual void CheckParameters() {
		ArgumentNullException.ThrowIfNull(mv);
		QAngle v_angle;

		if (Player.GetMoveType() != MoveType.Isometric &&
			 Player.GetMoveType() != MoveType.Noclip &&
			 Player.GetMoveType() != MoveType.Observer) {
			float spd;
			float maxspeed;

			spd = (mv.ForwardMove * mv.ForwardMove) +
				  (mv.SideMove * mv.SideMove) +
				  (mv.UpMove * mv.UpMove);

			maxspeed = mv.ClientMaxSpeed;
			if (maxspeed != 0.0)
				mv.MaxSpeed = Math.Min(maxspeed, mv.MaxSpeed);

			// Slow down by the speed factor
			float speedFactor = 1.0f;
			if (Player.SurfaceData != null)
				speedFactor = Player.SurfaceData.Game.MaxSpeedFactor;

			// If we have a constraint, slow down because of that too.
			float constraintSpeedFactor = ComputeConstraintSpeedFactor();
			if (constraintSpeedFactor < speedFactor)
				speedFactor = constraintSpeedFactor;

			mv.MaxSpeed *= speedFactor;

			if (g_bMovementOptimizations) {
				// Same thing but only do the sqrt if we have to.
				if ((spd != 0.0) && (spd > mv.MaxSpeed * mv.MaxSpeed)) {
					float fRatio = mv.MaxSpeed / MathF.Sqrt(spd);
					mv.ForwardMove *= fRatio;
					mv.SideMove *= fRatio;
					mv.UpMove *= fRatio;
				}
			}
			else {
				spd = MathF.Sqrt(spd);
				if ((spd != 0.0) && (spd > mv.MaxSpeed)) {
					float fRatio = mv.MaxSpeed / spd;
					mv.ForwardMove *= fRatio;
					mv.SideMove *= fRatio;
					mv.UpMove *= fRatio;
				}
			}
		}

		if ((Player.GetFlags() & EntityFlags.Frozen) != 0 ||
			 (Player.GetFlags() & EntityFlags.OnTrain) != 0 ||
			 IsDead()) {
			mv.ForwardMove = 0;
			mv.SideMove = 0;
			mv.UpMove = 0;
		}

		DecayPunchAngle();

		// Take angles from command.
		if (!IsDead()) {
			v_angle = mv.Angles;
			v_angle = v_angle + Player.Local.PunchAngle;

			// Now adjust roll angle
			if (Player.GetMoveType() != MoveType.Isometric &&
				 Player.GetMoveType() != MoveType.Noclip) {
				mv.Angles[ROLL] = CalcRoll(v_angle, mv.Velocity, sv_rollangle.GetFloat(), sv_rollspeed.GetFloat());
			}
			else {
				mv.Angles[ROLL] = 0.0f; // v_angle[ ROLL ];
			}
			mv.Angles[PITCH] = v_angle[PITCH];
			mv.Angles[YAW] = v_angle[YAW];
		}
		else {
			mv.Angles = mv.OldAngles;
		}

		// Set dead player view_offset
		if (IsDead())
			Player.SetViewOffset(VEC_DEAD_VIEWHEIGHT_SCALED(Player));


		// Adjust client view angles to match values used on server.
		if (mv.Angles[YAW] > 180.0f)
			mv.Angles[YAW] -= 360.0f;
	}

	protected virtual void ReduceTimers() {
		float frame_msec = 1000.0f * (float)gpGlobals.FrameTime;

		if (Player.Local.DuckTime > 0) {
			Player.Local.DuckTime -= frame_msec;
			if (Player.Local.DuckTime < 0) {
				Player.Local.DuckTime = 0;
			}
		}
		if (Player.Local.DuckJumpTime > 0) {
			Player.Local.DuckJumpTime -= frame_msec;
			if (Player.Local.DuckJumpTime < 0) {
				Player.Local.DuckJumpTime = 0;
			}
		}
		if (Player.Local.JumpTime > 0) {
			Player.Local.JumpTime -= frame_msec;
			if (Player.Local.JumpTime < 0) {
				Player.Local.JumpTime = 0;
			}
		}
		if (Player.SwimSoundTime > 0) {
			Player.SwimSoundTime -= frame_msec;
			if (Player.SwimSoundTime < 0)
				Player.SwimSoundTime = 0;
		}
	}

	protected virtual void CheckFalling() {
		if (Player.GetGroundEntity() == null || Player.Local.FallVelocity <= 0)
			return;

		if (!IsDead() && Player.Local.FallVelocity >= PLAYER_FALL_PUNCH_THRESHOLD) {
			bool alive = true;
			float fvol = 0.5f;

			if (Player.GetWaterLevel() > 0) {
				// They landed in water.
			}
			else {
				// Scale it down if we landed on something that's floating...
				if (Player.GetGroundEntity()!.IsFloating())
					Player.Local.FallVelocity -= PLAYER_LAND_ON_FLOATING_OBJECT;

				//
				// They hit the ground.
				//
				if (Player.GetGroundEntity()!.GetAbsVelocity().Z < 0.0f) {
					// Player landed on a descending object. Subtract the velocity of the ground entity.
					Player.Local.FallVelocity += Player.GetGroundEntity()!.GetAbsVelocity().Z;
					Player.Local.FallVelocity = MathF.Max(0.1f, Player.Local.FallVelocity);
				}

				if (Player.Local.FallVelocity > PLAYER_MAX_SAFE_FALL_SPEED) {
					//
					// If they hit the ground going this fast they may take damage (and die).
					//
					alive = MoveHelper().PlayerFallingDamage();
					fvol = 1.0f;
				}
				else if (Player.Local.FallVelocity > PLAYER_MAX_SAFE_FALL_SPEED / 2)
					fvol = 0.85f;
				else if (Player.Local.FallVelocity < PLAYER_MIN_BOUNCE_SPEED)
					fvol = 0;
			}

			PlayerRoughLandingEffects(fvol);

			if (alive)
				MoveHelper().PlayerSetAnimation(PlayerAnim.Walk);
		}

		// let any subclasses know that the player has landed and how hard
		OnLand(Player.Local.FallVelocity);

		// Clear the fall velocity so the impact doesn't happen again.
		Player.Local.FallVelocity = 0;
	}

	protected virtual void PlayerRoughLandingEffects(float fvol) {
		if (fvol > 0.0) {
			// Play landing sound right away.
			Player.StepSoundTime = 400;

			// Play step sound for current texture.
			Player.PlayStepSound(mv!.GetAbsOrigin(), Player.SurfaceData, fvol, true);

			// Knock the screen around a little bit, temporary effect.
			QAngle punchAngle = Player.Local.PunchAngle;
			punchAngle[ROLL] = Player.Local.FallVelocity * 0.013f;

			if (punchAngle[PITCH] > 8)
				punchAngle[PITCH] = 8;
			Player.Local.PunchAngle = punchAngle;
		}
	}
	protected void ResetGetPointContentsCache() {
		for (int slot = 0; slot < MAX_PC_CACHE_SLOTS; ++slot)
			for (int i = 0; i < Constants.MAX_PLAYERS; ++i)
				CachedGetPointContents[i, slot] = (Contents)(-9999);
	}
	protected Contents GetPointContentsCached(in Vector3 point, int slot) {
		if (g_bMovementOptimizations) {
			Assert(Player != null);
			Assert(slot >= 0 && slot < MAX_PC_CACHE_SLOTS);

			int idx = Player.EntIndex() - 1;

			if (CachedGetPointContents[idx, slot] == (Contents)(-9999) || point.DistToSqr(CachedGetPointContentsPoint[idx, slot]) > 1) {
				CachedGetPointContents[idx, slot] = enginetrace.GetPointContents(point, out _);
				CachedGetPointContentsPoint[idx, slot] = point;
			}

			return CachedGetPointContents[idx, slot];
		}
		else
			return enginetrace.GetPointContents(point, out _);
	}


	public const float GAMEMOVEMENT_DUCK_TIME = 1000.0f;    // ms
	public const float GAMEMOVEMENT_JUMP_TIME = 510.0f; // ms approx - based on the 21 unit height jump
	public const float GAMEMOVEMENT_JUMP_HEIGHT = 21.0f;  // units
	public float GAMEMOVEMENT_TIME_TO_UNDUCK => (TIME_TO_UNDUCK * 1000.0f);     // ms
	public float GAMEMOVEMENT_TIME_TO_UNDUCK_INV => (GAMEMOVEMENT_DUCK_TIME - GAMEMOVEMENT_TIME_TO_UNDUCK);


	public float TIME_TO_DUCK => Player!.GetDuckSpeed();
	public float TIME_TO_UNDUCK => Player!.GetUnDuckSpeed();
	public float TIME_TO_UNDUCK_MS => TIME_TO_UNDUCK * 1000;

	// Ducking
	protected virtual void Duck() {
		InButtons buttonsChanged = (mv!.OldButtons ^ mv.Buttons);  // These buttons have changed this frame
		InButtons buttonsPressed = buttonsChanged & mv.Buttons;           // The changed ones still down are "pressed"
		InButtons buttonsReleased = buttonsChanged & mv.OldButtons;       // The changed ones which were previously down are "released"

		// Check to see if we are in the air.
		bool bInAir = (Player.GetGroundEntity() == null);
		bool bInDuck = (Player.GetFlags() & EntityFlags.Ducking) != 0;
		bool bDuckJump = (Player.Local.JumpTime > 0.0f);
		bool bDuckJumpTime = (Player.Local.DuckJumpTime > 0.0f);

		if ((mv.Buttons & InButtons.Duck) != 0)
			mv.OldButtons |= InButtons.Duck;
		else
			mv.OldButtons &= ~InButtons.Duck;

		// Handle death.
		if (IsDead())
			return;

		// Slow down ducked players.
		HandleDuckingSpeedCrop();

		// If the player is holding down the duck button, the player is in duck transition, ducking, or duck-jumping.
		if ((mv.Buttons & InButtons.Duck) != 0 || Player.Local.Ducking || bInDuck || bDuckJump) {
			// DUCK
			if ((mv.Buttons & InButtons.Duck) != 0 || bDuckJump) {
				// Have the duck button pressed, but the player currently isn't in the duck position.
				if ((buttonsPressed & InButtons.Duck) != 0 && !bInDuck && !bDuckJump && !bDuckJumpTime) {
					Player.Local.DuckTime = GAMEMOVEMENT_DUCK_TIME;
					Player.Local.Ducking = true;
				}

				// The player is in duck transition and not duck-jumping.
				if (Player.Local.Ducking && !bDuckJump && !bDuckJumpTime) {
					float flDuckMilliseconds = Math.Max(0.0f, GAMEMOVEMENT_DUCK_TIME - (float)Player.Local.DuckTime);
					float flDuckSeconds = flDuckMilliseconds * 0.001f;

					// Finish in duck transition when transition time is over, in "duck", in air.
					if ((flDuckSeconds > TIME_TO_DUCK) || bInDuck || bInAir) {
						FinishDuck();
					}
					else {
						// Calc parametric time
						float flDuckFraction = MathLib.SimpleSpline(flDuckSeconds / TIME_TO_DUCK);
						SetDuckedEyeOffset(flDuckFraction);
					}
				}

				if (bDuckJump) {
					// Make the bounding box small immediately.
					if (!bInDuck) {
						StartUnDuckJump();
					}
					else {
						// Check for a crouch override.
						if ((mv.Buttons & InButtons.Duck) == 0) {
							Trace trace = default;
							if (CanUnDuckJump(ref trace)) {
								FinishUnDuckJump(ref trace);
								Player.Local.DuckJumpTime = (GAMEMOVEMENT_TIME_TO_UNDUCK * (1.0f - trace.Fraction)) + GAMEMOVEMENT_TIME_TO_UNDUCK_INV;
							}
						}
					}
				}
			}
			// UNDUCK (or attempt to...)
			else {
				if (Player.Local.InDuckJump) {
					// Check for a crouch override.
					if ((mv.Buttons & InButtons.Duck) == 0) {
						Trace trace = default;
						if (CanUnDuckJump(ref trace)) {
							FinishUnDuckJump(ref trace);

							if (trace.Fraction < 1.0f)
								Player.Local.DuckJumpTime = (GAMEMOVEMENT_TIME_TO_UNDUCK * (1.0f - trace.Fraction)) + GAMEMOVEMENT_TIME_TO_UNDUCK_INV;
						}
					}
					else {
						Player.Local.InDuckJump = false;
					}
				}

				if (bDuckJumpTime)
					return;

				// Try to unduck unless automovement is not allowed
				// NOTE: When not onground, you can always unduck
				if (Player.Local.AllowAutoMovement || bInAir || Player.Local.Ducking) {
					// We released the duck button, we aren't in "duck" and we are not in the air - start unduck transition.
					if ((buttonsReleased & InButtons.Duck) != 0) {
						if (bInDuck && !bDuckJump) {
							Player.Local.DuckTime = GAMEMOVEMENT_DUCK_TIME;
						}
						else if (Player.Local.Ducking && !Player.Local.Ducked) {
							// Invert time if release before fully ducked!!!
							float unduckMilliseconds = 1000.0f * TIME_TO_UNDUCK;
							float duckMilliseconds = 1000.0f * TIME_TO_DUCK;
							float elapsedMilliseconds = (float)(GAMEMOVEMENT_DUCK_TIME - Player.Local.DuckTime);

							float fracDucked = elapsedMilliseconds / duckMilliseconds;
							float remainingUnduckMilliseconds = fracDucked * unduckMilliseconds;

							Player.Local.DuckTime = GAMEMOVEMENT_DUCK_TIME - unduckMilliseconds + remainingUnduckMilliseconds;
						}
					}


					// Check to see if we are capable of unducking.
					if (CanUnduck()) {
						// or unducking
						if ((Player.Local.Ducking || Player.Local.Ducked)) {
							float flDuckMilliseconds = Math.Max(0.0f, GAMEMOVEMENT_DUCK_TIME - (float)Player.Local.DuckTime);
							float flDuckSeconds = flDuckMilliseconds * 0.001f;

							// Finish ducking immediately if duck time is over or not on ground
							if (flDuckSeconds > TIME_TO_UNDUCK || (bInAir && !bDuckJump)) {
								FinishUnDuck();
							}
							else {
								// Calc parametric time
								float flDuckFraction = MathLib.SimpleSpline(1.0f - (flDuckSeconds / TIME_TO_UNDUCK));
								SetDuckedEyeOffset(flDuckFraction);
								Player.Local.Ducking = true;
							}
						}
					}
					else {
						// Still under something where we can't unduck, so make sure we reset this timer so
						//  that we'll unduck once we exit the tunnel, etc.
						if (Player.Local.DuckTime != GAMEMOVEMENT_DUCK_TIME) {
							SetDuckedEyeOffset(1.0f);
							Player.Local.DuckTime = GAMEMOVEMENT_DUCK_TIME;
							Player.Local.Ducked = true;
							Player.Local.Ducking = false;
							Player.AddFlag(EntityFlags.Ducking);
						}
					}
				}
			}
		}
		// HACK: (jimd 5/25/2006) we have a reoccuring bug (#50063 in Tracker) where the player's
		// view height gets left at the ducked height while the player is standing, but we haven't
		// been  able to repro it to find the cause.  It may be fixed now due to a change I'm
		// also making in UpdateDuckJumpEyeOffset but just in case, this code will sense the 
		// problem and restore the eye to the proper position.  It doesn't smooth the transition,
		// but it is preferable to leaving the player's view too low.
		//
		// If the player is still alive and not an observer, check to make sure that
		// his view height is at the standing height.
		else if (!IsDead() && !Player.IsObserver() && !Player.IsInAVehicle()) {
			if ((Player.Local.DuckJumpTime == 0.0f) && (MathF.Abs(Player.GetViewOffset().Z - GetPlayerViewOffset(false).Z) > 0.1)) {
				// we should rarely ever get here, so assert so a coder knows when it happens
				Assert(false);
				// DevMsg(1, "Restoring player view height\n"); // todo, uncomment when it wants to be less annoying

				// set the eye height to the non-ducked height
				SetDuckedEyeOffset(0.0f);
			}
		}
	}
	protected virtual void HandleDuckingSpeedCrop() {
		if ((SpeedCropped & SpeedCropped.Duck) == 0 && (Player.GetFlags() & EntityFlags.Ducking) != 0 && (Player.GetGroundEntity() != null)) {
			float frac = Player.Local.CrouchedWalkSpeed;
			mv.ForwardMove *= frac;
			mv.SideMove *= frac;
			mv.UpMove *= frac;
			SpeedCropped |= SpeedCropped.Duck;
		}
	}
	protected virtual void FinishUnDuck() {
		int i;
		Vector3 newOrigin = mv!.GetAbsOrigin();

		if (Player.GetGroundEntity() != null)
			for (i = 0; i < 3; i++)
				newOrigin[i] += (VEC_DUCK_HULL_MIN_SCALED(Player)[i] - VEC_HULL_MIN_SCALED(Player)[i]);
		else {
			// If in air an letting go of crouch, make sure we can offset origin to make
			//  up for uncrouching
			Vector3 hullSizeNormal = VEC_HULL_MAX_SCALED(Player) - VEC_HULL_MIN_SCALED(Player);
			Vector3 hullSizeCrouch = VEC_DUCK_HULL_MAX_SCALED(Player) - VEC_DUCK_HULL_MIN_SCALED(Player);
			Vector3 viewDelta = (hullSizeNormal - hullSizeCrouch);
			viewDelta = Vector3.Negate(viewDelta);
			MathLib.VectorAdd(newOrigin, viewDelta, out newOrigin);
		}

		Player.Local.Ducked = false;
		Player.RemoveFlag(EntityFlags.Ducking);
		Player.Local.Ducking = false;
		Player.Local.InDuckJump = false;
		Player.SetViewOffset(GetPlayerViewOffset(false));
		Player.Local.DuckTime = 0;

		mv.SetAbsOrigin(newOrigin);

#if CLIENT_DLL
		Player.ResetLatched();
#endif // CLIENT_DLL

		// Recategorize position since ducking can change origin
		CategorizePosition();
	}
	protected virtual void FinishDuck() {
		if ((Player.GetFlags() & EntityFlags.Ducking) != 0)
			return;

		Player.AddFlag(EntityFlags.Ducking);
		Player.Local.Ducked = true;
		Player.Local.Ducking = false;

		Player.SetViewOffset(GetPlayerViewOffset(true));

		// HACKHACK - Fudge for collision bug - no time to fix this properly
		if (Player.GetGroundEntity() != null) {
			for (int i = 0; i < 3; i++) {
				Vector3 org = mv!.GetAbsOrigin();
				org[i] -= (VEC_DUCK_HULL_MIN_SCALED(Player)[i] - VEC_HULL_MIN_SCALED(Player)[i]);
				mv.SetAbsOrigin(org);
			}
		}
		else {
			Vector3 hullSizeNormal = VEC_HULL_MAX_SCALED(Player) - VEC_HULL_MIN_SCALED(Player);
			Vector3 hullSizeCrouch = VEC_DUCK_HULL_MAX_SCALED(Player) - VEC_DUCK_HULL_MIN_SCALED(Player);
			Vector3 viewDelta = (hullSizeNormal - hullSizeCrouch);
			Vector3 @out;
			MathLib.VectorAdd(mv!.GetAbsOrigin(), viewDelta, out @out);
			mv.SetAbsOrigin(@out);

#if CLIENT_DLL
			Player.ResetLatched();
#endif
		}

		// See if we are stuck?
		FixPlayerCrouchStuck(true);

		// Recategorize position since ducking can change origin
		CategorizePosition();
	}

	public Mask PlayerSolidMask(bool brushOnly = false) => brushOnly ? Mask.PlayerSolidBrushOnly : Mask.PlayerSolid;

	protected virtual bool CanUnduck() {
		int i;
		Trace trace;
		Vector3 newOrigin;

		MathLib.VectorCopy(mv!.GetAbsOrigin(), out newOrigin);

		if (Player.GetGroundEntity() != null) {
			for (i = 0; i < 3; i++) {
				newOrigin[i] += (VEC_DUCK_HULL_MIN_SCALED(Player)[i] - VEC_HULL_MIN_SCALED(Player)[i]);
			}
		}
		else {
			// If in air an letting go of crouch, make sure we can offset origin to make
			//  up for uncrouching
			Vector3 hullSizeNormal = VEC_HULL_MAX_SCALED(Player) - VEC_HULL_MIN_SCALED(Player);
			Vector3 hullSizeCrouch = VEC_DUCK_HULL_MAX_SCALED(Player) - VEC_DUCK_HULL_MIN_SCALED(Player);
			Vector3 viewDelta = (hullSizeNormal - hullSizeCrouch);
			viewDelta.Negate();
			MathLib.VectorAdd(newOrigin, viewDelta, out newOrigin);
		}

		bool saveducked = Player.Local.Ducked;
		Player.Local.Ducked = false;
		TracePlayerBBox(mv.GetAbsOrigin(), newOrigin, PlayerSolidMask(), CollisionGroup.PlayerMovement, out trace);
		Player.Local.Ducked = saveducked;
		if (trace.StartSolid || (trace.Fraction != 1.0))
			return false;

		return true;
	}

	public void TracePlayerBBox(in Vector3 start, in Vector3 end, Mask mask, CollisionGroup collisionGroup, out Trace pm) {
		Ray ray = default;
		ray.Init(start, end, GetPlayerMins(), GetPlayerMaxs());
		Util.TraceRay(in ray, mask, mv!.PlayerHandle.Get(), collisionGroup, out pm);
	}

	protected void UpdateDuckJumpEyeOffset() {
		if (Player.Local.DuckJumpTime != 0.0f) {
			float flDuckMilliseconds = Math.Max(0.0f, GAMEMOVEMENT_DUCK_TIME - (float)Player.Local.DuckJumpTime);
			float flDuckSeconds = flDuckMilliseconds / GAMEMOVEMENT_DUCK_TIME;
			if (flDuckSeconds > TIME_TO_UNDUCK) {
				Player.Local.DuckJumpTime = 0.0f;
				SetDuckedEyeOffset(0.0f);
			}
			else {
				float flDuckFraction = MathLib.SimpleSpline(1.0f - (flDuckSeconds / TIME_TO_UNDUCK));
				SetDuckedEyeOffset(flDuckFraction);
			}
		}
	}
	protected bool CanUnDuckJump(ref Trace trace) {
		// Trace down to the stand position and see if we can stand.
		Vector3 vecEnd = mv.GetAbsOrigin();
		vecEnd.Z -= 36.0f;                      // This will have to change if bounding hull change!
		TracePlayerBBox(mv.GetAbsOrigin(), vecEnd, PlayerSolidMask(), CollisionGroup.PlayerMovement, out trace);
		if (trace.Fraction < 1.0f) {
			// Find the endpoint.
			vecEnd.Z = mv.GetAbsOrigin().Z + (-36.0f * trace.Fraction);

			// Test a normal hull.
			Trace traceUp;
			bool bWasDucked = Player.Local.Ducked;
			Player.Local.Ducked = false;
			TracePlayerBBox(vecEnd, vecEnd, PlayerSolidMask(), CollisionGroup.PlayerMovement, out traceUp);
			Player.Local.Ducked = bWasDucked;
			if (!traceUp.StartSolid)
				return true;
		}

		return false;
	}
	protected void StartUnDuckJump() {
		Player.AddFlag(EntityFlags.Ducking);
		Player.Local.Ducked = true;
		Player.Local.Ducking = false;

		Player.SetViewOffset(GetPlayerViewOffset(true));

		Vector3 hullSizeNormal = VEC_HULL_MAX_SCALED(Player) - VEC_HULL_MIN_SCALED(Player);
		Vector3 hullSizeCrouch = VEC_DUCK_HULL_MAX_SCALED(Player) - VEC_DUCK_HULL_MIN_SCALED(Player);
		Vector3 viewDelta = (hullSizeNormal - hullSizeCrouch);
		Vector3 outVec;
		MathLib.VectorAdd(mv.GetAbsOrigin(), viewDelta, out outVec);
		mv.SetAbsOrigin(outVec);

		// See if we are stuck?
		FixPlayerCrouchStuck(true);

		// Recategorize position since ducking can change origin
		CategorizePosition();
	}
	protected void FinishUnDuckJump(ref Trace trace) {
		Vector3 vecNewOrigin;
		MathLib.VectorCopy(mv!.GetAbsOrigin(), out vecNewOrigin);

		//  Up for uncrouching.
		Vector3 hullSizeNormal = VEC_HULL_MAX_SCALED(Player) - VEC_HULL_MIN_SCALED(Player);
		Vector3 hullSizeCrouch = VEC_DUCK_HULL_MAX_SCALED(Player) - VEC_DUCK_HULL_MIN_SCALED(Player);
		Vector3 viewDelta = (hullSizeNormal - hullSizeCrouch);

		float flDeltaZ = viewDelta.Z;
		viewDelta.Z *= trace.Fraction;
		flDeltaZ -= viewDelta.Z;

		Player.RemoveFlag(EntityFlags.Ducking);
		Player.Local.Ducked = false;
		Player.Local.Ducking = false;
		Player.Local.InDuckJump = false;
		Player.Local.DuckTime = 0.0f;
		Player.Local.DuckJumpTime = 0.0f;
		Player.Local.JumpTime = 0.0f;

		Vector3 vecViewOffset = GetPlayerViewOffset(false);
		vecViewOffset.Z -= flDeltaZ;
		Player.SetViewOffset(vecViewOffset);

		MathLib.VectorSubtract(vecNewOrigin, viewDelta, out vecNewOrigin);
		mv.SetAbsOrigin(vecNewOrigin);

		CategorizePosition();
	}
	protected void SetDuckedEyeOffset(float duckFraction) {
		Vector3 vDuckHullMin = GetPlayerMins(true);
		Vector3 vStandHullMin = GetPlayerMins(false);

		float fMore = (vDuckHullMin.Z - vStandHullMin.Z);

		Vector3 vecDuckViewOffset = GetPlayerViewOffset(true);
		Vector3 vecStandViewOffset = GetPlayerViewOffset(false);
		Vector3 temp = Player.GetViewOffset();
		temp.Z = ((vecDuckViewOffset.Z - fMore) * duckFraction) +
					(vecStandViewOffset.Z * (1 - duckFraction));
		Player.SetViewOffset(temp);
	}
	protected void FixPlayerCrouchStuck(bool upward) {
		EntityHandle_t hitent;
		int i;
		Vector3 test;
		Trace dummy;

		int direction = upward ? 1 : 0;

		hitent = TestPlayerPosition(mv!.GetAbsOrigin(), CollisionGroup.PlayerMovement, out dummy);
		if (hitent.Index == INVALID_ENTITY_HANDLE)
			return;

		MathLib.VectorCopy(mv.GetAbsOrigin(), out test);
		for (i = 0; i < 36; i++) {
			Vector3 org = mv.GetAbsOrigin();
			org.Z += direction;
			mv.SetAbsOrigin(org);
			hitent = TestPlayerPosition(mv.GetAbsOrigin(), CollisionGroup.PlayerMovement, out dummy);
			if (hitent.Index == INVALID_ENTITY_HANDLE)
				return;
		}

		mv.SetAbsOrigin(test); // Failed
	}

	protected float SplineFraction(float value, float scale) {
		float valueSquared;

		value = scale * value;
		valueSquared = value * value;

		// Nice little ease-in, ease-out spline-like curve
		return 3 * valueSquared - 2 * valueSquared * value;
	}

	protected void CategorizeGroundSurface(ref Trace pm) {
		Player.SurfaceProps = pm.Surface.SurfaceProps;
		Player.SurfaceData = MoveHelper().GetSurfaceProps()!.GetSurfaceData(Player.SurfaceProps);
		MoveHelper().GetSurfaceProps()!.GetPhysicsProperties(Player.SurfaceProps, out _, out _, out Player.SurfaceFriction, out _);

		// HACKHACK: Scale this to fudge the relationship between vphysics friction values and player friction values.
		// A value of 0.8f feels pretty normal for vphysics, whereas 1.0f is normal for players.
		// This scaling trivially makes them equivalent.  REVISIT if this affects low friction surfaces too much.
		Player.SurfaceFriction *= 1.25f;
		if (Player.SurfaceFriction > 1.0f)
			Player.SurfaceFriction = 1.0f;

		Player.TextureType = Player.SurfaceData!.Struct.Game.Material;
	}

	protected bool InWater() => Player.GetWaterLevel() > WaterLevel.Feet;

	// Commander view movement
	protected void IsometricMove() { throw new NotImplementedException(); }

	// Traces the player bbox as it is swept from start to end
	protected virtual BaseHandle TestPlayerPosition(in Vector3 pos, CollisionGroup collisionGroup, out Trace pm) {
		Ray ray = default;
		ray.Init(pos, pos, GetPlayerMins(), GetPlayerMaxs());
		Util.TraceRay(ray, PlayerSolidMask(), mv.PlayerHandle.Get(), collisionGroup, out pm);
		if (((Mask)pm.Contents & PlayerSolidMask()) != 0 && pm.Ent != null)
			return pm.Ent!.GetRefEHandle();
		else
			return new(Constants.INVALID_EHANDLE_INDEX);
	}

	// Checks to see if we should actually jump 
	protected void PlaySwimSound() => MoveHelper().StartSound(mv!.GetAbsOrigin(), "Player.Swim");
	protected bool IsDead() => Player.Health <= 0 && !Player.IsAlive();
	// Figures out how the constraint should slow us down
	protected float ComputeConstraintSpeedFactor() {
		if (mv == null || mv.ConstraintRadius == 0.0f)
			return 1.0f;

		float flDistSq = mv.GetAbsOrigin().DistToSqr(mv.ConstraintCenter);

		float flOuterRadiusSq = mv.ConstraintRadius * mv.ConstraintRadius;
		float flInnerRadiusSq = mv.ConstraintRadius - mv.ConstraintWidth;
		flInnerRadiusSq *= flInnerRadiusSq;

		// Only slow us down if we're inside the constraint ring
		if ((flDistSq <= flInnerRadiusSq) || (flDistSq >= flOuterRadiusSq))
			return 1.0f;

		// Only slow us down if we're running away from the center
		Vector3 vecDesired;
		MathLib.VectorMultiply(Forward, mv.ForwardMove, out vecDesired);
		MathLib.VectorMA(vecDesired, mv.SideMove, Right, out vecDesired);
		MathLib.VectorMA(vecDesired, mv.UpMove, Up, out vecDesired);

		Vector3 vecDelta;
		MathLib.VectorSubtract(mv.GetAbsOrigin(), mv.ConstraintCenter, out vecDelta);
		MathLib.VectorNormalizeFast(ref vecDelta);
		MathLib.VectorNormalizeFast(ref vecDesired);
		if (MathLib.DotProduct(vecDelta, vecDesired) < 0.0f)
			return 1.0f;

		float flFrac = (MathF.Sqrt(flDistSq) - (mv.ConstraintRadius - mv.ConstraintWidth)) / mv.ConstraintWidth;

		float flSpeedFactor = float.Lerp(flFrac, 1.0f, mv.ConstraintSpeedFactor);
		return flSpeedFactor;
	}
	protected virtual void SetGroundEntity() => SetGroundEntity(ref Unsafe.NullRef<Trace>());
	protected virtual void SetGroundEntity(ref Trace pm) {
		BaseEntity? newGround = Unsafe.IsNullRef(ref pm) ? null : pm.Ent;

		BaseEntity? oldGround = Player.GetGroundEntity();
		Vector3 vecBaseVelocity = Player.GetBaseVelocity();

		if (oldGround == null && newGround != null) {
			// Subtract ground velocity at instant we hit ground jumping
			vecBaseVelocity -= newGround.GetAbsVelocity();
			vecBaseVelocity.Z = newGround.GetAbsVelocity().Z;
		}
		else if (oldGround != null && newGround == null) {
			// Add in ground velocity at instant we started jumping
			vecBaseVelocity += oldGround.GetAbsVelocity();
			vecBaseVelocity.Z = oldGround.GetAbsVelocity().Z;
		}

		Player.SetBaseVelocity(vecBaseVelocity);
		Player.SetGroundEntity(newGround);

		// If we are on something...

		if (newGround != null) {
			CategorizeGroundSurface(ref pm);

			// Then we are not in water jump sequence
			Player.WaterJumpTime = 0;

			// Standing on an entity other than the world, so signal that we are touching something.
			if (!pm.DidHitWorld())
				MoveHelper().AddToTouched(ref pm, mv!.Velocity);

			mv!.Velocity.Z = 0.0f;
		}
	}
	protected virtual void StepMove(ref Vector3 destination, ref Trace trace) {
		Vector3 vecEndPos;
		MathLib.VectorCopy(destination, out vecEndPos);

		// Try sliding forward both on ground and up 16 pixels
		//  take the move that goes farthest
		Vector3 vecPos, vecVel;
		MathLib.VectorCopy(mv!.GetAbsOrigin(), out vecPos);
		MathLib.VectorCopy(mv!.Velocity, out vecVel);

		// Slide move down.
		TryPlayerMove(ref vecEndPos, ref trace);

		// Down results.
		Vector3 vecDownPos, vecDownVel;
		MathLib.VectorCopy(mv.GetAbsOrigin(), out vecDownPos);
		MathLib.VectorCopy(mv.Velocity, out vecDownVel);

		// Reset original values.
		mv.SetAbsOrigin(vecPos);
		MathLib.VectorCopy(vecVel, out mv!.Velocity);

		// Move up a stair height.
		MathLib.VectorCopy(mv.GetAbsOrigin(), out vecEndPos);
		if (Player.Local.AllowAutoMovement)
			vecEndPos.Z += Player.Local.StepSize + DIST_EPSILON;

		TracePlayerBBox(mv.GetAbsOrigin(), vecEndPos, PlayerSolidMask(), CollisionGroup.PlayerMovement, out trace);
		if (!trace.StartSolid && !trace.AllSolid)
			mv.SetAbsOrigin(trace.EndPos);

		// Slide move up.
		TryPlayerMove();

		// Move down a stair (attempt to).
		MathLib.VectorCopy(mv.GetAbsOrigin(), out vecEndPos);
		if (Player.Local.AllowAutoMovement)
			vecEndPos.Z -= Player.Local.StepSize + DIST_EPSILON;

		TracePlayerBBox(mv.GetAbsOrigin(), vecEndPos, PlayerSolidMask(), CollisionGroup.PlayerMovement, out trace);

		// If we are not on the ground any more then use the original movement attempt.
		if (trace.Plane.Normal[2] < 0.7) {
			mv.SetAbsOrigin(vecDownPos);
			MathLib.VectorCopy(vecDownVel, out mv!.Velocity);
			float stepDist = mv!.GetAbsOrigin().Z - vecPos.Z;
			if (stepDist > 0.0f)
				mv!.StepHeight += stepDist;

			return;
		}

		// If the trace ended up in empty space, copy the end over to the origin.
		if (!trace.StartSolid && !trace.AllSolid)
			mv!.SetAbsOrigin(trace.EndPos);

		// Copy this origin to up.
		Vector3 vecUpPos;
		MathLib.VectorCopy(mv!.GetAbsOrigin(), out vecUpPos);

		// decide which one went farther
		float flDownDist = (vecDownPos.X - vecPos.X) * (vecDownPos.X - vecPos.X) + (vecDownPos.Y - vecPos.Y) * (vecDownPos.Y - vecPos.Y);
		float flUpDist = (vecUpPos.X - vecPos.X) * (vecUpPos.X - vecPos.X) + (vecUpPos.Y - vecPos.Y) * (vecUpPos.Y - vecPos.Y);
		if (flDownDist > flUpDist) {
			mv.SetAbsOrigin(vecDownPos);
			MathLib.VectorCopy(vecDownVel, out mv!.Velocity);
		}
		else
			// copy z value from slide move
			mv.Velocity.Z = vecDownVel.Z;

		float flStepDist = mv!.GetAbsOrigin().Z - vecPos.Z;
		if (flStepDist > 0)
			mv.StepHeight += flStepDist;
	}
	// when we step on ground that's too steep, search to see if there's any ground nearby that isn't too steep
	protected void TryTouchGroundInQuadrants(in Vector3 start, in Vector3 end, Mask mask, CollisionGroup collisionGroup, ref Trace pm) {
		Vector3 mins = default, maxs = default;
		Vector3 minsSrc = GetPlayerMins();
		Vector3 maxsSrc = GetPlayerMaxs();

		float fraction = pm.Fraction;
		Vector3 endpos = pm.EndPos;

		// Check the -x, -y quadrant
		mins = minsSrc;
		maxs.Init(MathF.Min(0, maxsSrc.X), MathF.Min(0, maxsSrc.Y), maxsSrc.Z);
		TryTouchGround(start, end, mins, maxs, mask, collisionGroup, out pm);
		if (pm.Ent != null && pm.Plane.Normal[2] >= 0.7) {
			pm.Fraction = fraction;
			pm.EndPos
			= endpos;
			return;
		}

		// Check the +x, +y quadrant
		mins.Init(MathF.Max(0, minsSrc.X), MathF.Max(0, minsSrc.Y), minsSrc.Z);
		maxs = maxsSrc;
		TryTouchGround(start, end, mins, maxs, mask, collisionGroup, out pm);
		if (pm.Ent != null && pm.Plane.Normal[2] >= 0.7) {
			pm.Fraction = fraction;
			pm.EndPos = endpos;
			return;
		}

		// Check the -x, +y quadrant
		mins.Init(minsSrc.X, MathF.Max(0, minsSrc.Y), minsSrc.Z);
		maxs.Init(MathF.Min(0, maxsSrc.X), maxsSrc.Y, maxsSrc.Z);
		TryTouchGround(start, end, mins, maxs, mask, collisionGroup, out pm);
		if (pm.Ent != null && pm.Plane.Normal[2] >= 0.7) {
			pm.Fraction = fraction;
			pm.EndPos = endpos;
			return;
		}

		// Check the +x, -y quadrant
		mins.Init(MathF.Max(0, minsSrc.X), minsSrc.Y, minsSrc.Z);
		maxs.Init(maxsSrc.X, MathF.Min(0, maxsSrc.Y), maxsSrc.Z);
		TryTouchGround(start, end, mins, maxs, mask, collisionGroup, out pm);
		if (pm.Ent != null && pm.Plane.Normal[2] >= 0.7) {
			pm.Fraction = fraction;
			pm.EndPos = endpos;
			return;
		}

		pm.Fraction = fraction;
		pm.EndPos = endpos;
	}
	protected void PerformFlyCollisionResolution(ref Trace pm, ref Vector3 move) {
		Vector3 @base = default;
		float vel;
		float backoff;

		switch (Player.GetMoveCollide()) {
			case MoveCollide.FlyCustom:
				// Do nothing; the velocity should have been modified by touch
				// FIXME: It seems wrong for touch to modify velocity
				// given that it can be called in a number of places
				// where collision resolution do *not* in fact occur

				// Should this ever occur for players!?
				Assert(false);
				break;

			case MoveCollide.FlyBounce:
			case MoveCollide.Default: {
					if (Player.GetMoveCollide() == MoveCollide.FlyBounce)
						backoff = 2.0f - Player.SurfaceFriction;
					else
						backoff = 1;

					ClipVelocity(mv!.Velocity, pm.Plane.Normal, out mv!.Velocity, backoff);
				}
				break;

			default:
				// Invalid collide type!
				Assert(0);
				break;
		}

		// stop if on ground
		if (pm.Plane.Normal[2] > 0.7) {
			@base.Init();
			if (mv!.Velocity[2] < GetCurrentGravity() * (float)gpGlobals.FrameTime) {
				// we're rolling on the ground, add static friction.
				SetGroundEntity(ref pm);
				mv!.Velocity[2] = 0;
			}

			vel = MathLib.DotProduct(mv.Velocity, mv.Velocity);

			if (vel < (30 * 30) || (Player.GetMoveCollide() != MoveCollide.FlyBounce)) {
				SetGroundEntity(ref pm);
				mv!.Velocity.Init();
			}
			else {
				MathLib.VectorScale(mv.Velocity, (1.0f - pm.Fraction) * (float)gpGlobals.FrameTime * 0.9f, out move);
				PushEntity(ref move, ref pm);
			}
			MathLib.VectorSubtract(mv.Velocity, @base, out mv.Velocity);
		}
	}
	protected virtual bool GameHasLadders() => true;


	protected const int MAX_PC_CACHE_SLOTS = 3;

	// Cache used to remove redundant calls to GetPointContents().
	protected readonly Contents[,] CachedGetPointContents = new Contents[Constants.MAX_PLAYERS, MAX_PC_CACHE_SLOTS];
	protected readonly Vector3[,] CachedGetPointContentsPoint = new Vector3[Constants.MAX_PLAYERS, MAX_PC_CACHE_SLOTS];

	protected Vector3 ProximityMins;      // Used to be globals in sv_user.cpp.
	protected Vector3 ProximityMaxs;

	protected TimeUnit_t FrameTime;
	SpeedCropped SpeedCropped;
	readonly TimeUnit_t[,] StuckCheckTime = new TimeUnit_t[Constants.MAX_PLAYERS + 1, 2];

	public BasePlayer Player = null!;
	public MoveData? GetMoveData() => mv;
}
#endif
