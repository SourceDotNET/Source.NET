#if CLIENT_DLL || GAME_DLL
global using static Game.Shared.HL2.HL2GameMovement;

#if CLIENT_DLL
global using HL2_Player = Game.Client.HL2.C_BaseHLPlayer;
using Game.Client.HL2;
using Game.Client;
#else
using Game.Server.HL2;
using Game.Server;
#endif

using DStruct.BinaryTrees;
using Source;
using Source.Common.Commands;
using Source.Common.Mathematics;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

using Source.Engine;

namespace Game.Shared.HL2;

public struct NearbyDismount
{
	public InfoLadderDismount Dismount;
	public float DistSqr;
}

public class HL2GameMovement : GameMovement
{
	const int USE_DISMOUNT_SPEED = 100;
	public static readonly ConVar sv_autoladderdismount = new("sv_autoladderdismount", "1", FCvar.Replicated, "Automatically dismount from ladders when you reach the end (don't have to +USE).");
	public static readonly ConVar sv_ladderautomountdot = new("sv_ladderautomountdot", "0.4", FCvar.Replicated, "When auto-mounting a ladder by looking up its axis, this is the tolerance for looking now directly along the ladder axis.");
	public static readonly ConVar sv_ladder_useonly = new("sv_ladder_useonly", "0", FCvar.Replicated, "If set, ladders can only be mounted by pressing +USE");

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public HL2_Player GetHL2Player() => (HL2_Player)Player!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref LadderMove GetLadderMove() {
		HL2_Player p = GetHL2Player();
		if (p == null)
			return ref Unsafe.NullRef<LadderMove>();
		return ref p.GetLadderMove();
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public FuncLadder? GetLadder() => (FuncLadder?)GetHL2Player().HL2Local.Ladder.Get();
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void SetLadder(FuncLadder? ladder) {
		FuncLadder? oldLadder = GetLadder();

		if (ladder == null && oldLadder != null)
			oldLadder.PlayerGotOff(GetHL2Player());

		GetHL2Player().HL2Local.Ladder.Set(ladder);
	}

	protected override int GetCheckInterval(IntervalType type) => type == IntervalType.Ladder ? 1 : base.GetCheckInterval(type);
	public bool IsForceMoveActive() => GetLadderMove().ForceLadderMove;

	public void SwallowUseKey() {
		mv.OldButtons |= InButtons.Use;
		Player.AfButtonPressed &= ~InButtons.Use;

		GetHL2Player().m_bPlayUseDenySound = false;
	}

#if !CLIENT_DLL
	// This is a simple helper class to reserver a player sized hull at a spot, owned by the current player so that nothing
	//  can move into this spot and cause us to get stuck when we get there
	[LinkEntityToClass("reserved_spot")]
	public class ReservePlayerSpot : BaseEntity
	{
		public ReservePlayerSpot() {

		}
		public static ReservePlayerSpot? ReserveSpot(BasePlayer owner, in Vector3 org, in Vector3 mins, in Vector3 maxs, out bool validspot) {
			ReservePlayerSpot? spot = (ReservePlayerSpot?)CreateEntityByName("reserved_spot");
			Assert(spot != null);

			spot.SetAbsOrigin(org);
			Util.SetSize(spot, mins, maxs);
			spot.SetOwnerEntity(owner);
			spot.Spawn();

			// See if spot is valid
			Util.TraceHull(
				org,
				org,
				mins,
				maxs,
				Source.Common.Formats.BSP.Mask.PlayerSolid,
				owner,
				Source.CollisionGroup.PlayerMovement,
				out Trace tr);

			validspot = !tr.StartSolid;

			if (!validspot) {
				Vector3 org2 = org + new Vector3(0, 0, 1);

				// See if spot is valid
				Util.TraceHull(
					org2,
					org2,
					mins,
					maxs,
					Source.Common.Formats.BSP.Mask.PlayerSolid,
					owner,
					Source.CollisionGroup.PlayerMovement,
					out tr
				);
				validspot = !tr.StartSolid;
			}

			return spot;
		}
		public override void Spawn() {
			base.Spawn();

			SetSolid(SolidType.BBox);
			SetMoveType(Source.MoveType.None);
			// Make entity invisible
			AddEffects(EntityEffects.NoDraw);
		}
	}

#endif
	//-----------------------------------------------------------------------------
	// Purpose: 
	// Input  : mounting - 
	//			transit_speed - 
	//			goalpos - 
	//			*ladder - 
	//-----------------------------------------------------------------------------
	public void StartForcedMove(bool mounting, float transit_speed, in Vector3 goalpos, FuncLadder? ladder) {
		ref LadderMove lm = ref GetLadderMove();
		Assert(!Unsafe.IsNullRef(ref lm));
		// Already active, just ignore
		if (lm.ForceLadderMove)
			return;

#if !CLIENT_DLL
		if (ladder != null) {
			ladder.PlayerGotOn(GetHL2Player());

			// If the Ladder only wants to be there for automount checking, abort now
			if (ladder.DontGetOnLadder())
				return;
		}

		// Reserve goal slot here
		bool valid = false;
		lm.ReservedSpot.Set(ReservePlayerSpot.ReserveSpot(
			Player,
			goalpos,
			GetPlayerMins((Player.GetFlags() & EntityFlags.Ducking) != 0),
			GetPlayerMaxs((Player.GetFlags() & EntityFlags.Ducking) != 0),
			out valid)
		);

		if (!valid) {
			// FIXME:  Play a deny sound?
			if (lm.ReservedSpot.Get() != null) {
				Util.Remove(lm.ReservedSpot.Get());
				lm.ReservedSpot.Set(null);
			}
			return;
		}
#endif

		// Use current player origin as start and new origin as dest
		lm.GoalPosition = goalpos;
		lm.StartPosition = mv.GetAbsOrigin();

		// Figure out how long it will take to make the gap based on transit_speed
		Vector3 delta = lm.GoalPosition - lm.StartPosition;

		float distance = delta.Length();

		Assert(transit_speed > 0.001f);

		// Compute time required to move that distance
		TimeUnit_t transit_time = distance / transit_speed;
		if (transit_time < 0.001) {
			transit_time = 0.001;

			lm.ForceLadderMove = true;
			lm.ForceMount = mounting;

			lm.StartTime = gpGlobals.CurTime;
			lm.ArrivalTime = lm.StartTime + transit_time;

			lm.ForceLadder.Set(ladder);

			// Don't get stuck during this traversal since we'll just be slamming the player origin
			Player.SetMoveType(MoveType.None);
			Player.SetMoveCollide(MoveCollide.Default);
			Player.SetSolid(SolidType.None);
			SetLadder(ladder);

			// Debounce the use key
			SwallowUseKey();
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose: Returns false when finished
	//-----------------------------------------------------------------------------
	public bool ContinueForcedMove() {
		ref LadderMove lm = ref GetLadderMove();
		Assert(!Unsafe.IsNullRef(ref lm));
		Assert(lm.ForceLadderMove);

		// Suppress regular motion
		mv.ForwardMove = 0.0f;
		mv.SideMove = 0.0f;
		mv.UpMove = 0.0f;

		// How far along are we
		float frac = (float)((gpGlobals.CurTime - lm.StartTime) / (lm.ArrivalTime - lm.StartTime));
		if (frac > 1.0f) {
			lm.ForceLadderMove = false;
#if !CLIENT_DLL
			// Remove "reservation entity"
			if (lm.ReservedSpot.Get() != null) {
				Util.Remove(lm.ReservedSpot.Get());
				lm.ReservedSpot.Set(null);
			}
#endif
		}

		frac = Math.Clamp(frac, 0.0f, 1.0f);

		// Move origin part of the way
		Vector3 delta = lm.GoalPosition - lm.StartPosition;

		// Compute interpolated position
		MathLib.VectorMA(lm.StartPosition, frac, delta, out Vector3 org);
		mv.SetAbsOrigin(org);

		// If finished moving, reset player to correct movetype (or put them on the ladder)
		if (!lm.ForceLadderMove) {
			Player.SetSolid(SolidType.BBox);
			Player.SetMoveType(MoveType.Walk);

			if (lm.ForceMount && lm.ForceLadder.Get() != null) {
				Player.SetMoveType(MoveType.Ladder);
				SetLadder(lm.ForceLadder.Get());
			}

			// Zero out any velocity
			mv.Velocity.Init();
		}

		// Stil active
		return lm.ForceLadderMove;
	}

	//-----------------------------------------------------------------------------
	// Purpose: Returns true if the player is on a ladder
	// Input  : &trace - ignored
	//-----------------------------------------------------------------------------
	protected override bool OnLadder(ref Trace trace) {
		return (GetLadder() != null) ? true : false;
	}

	//-----------------------------------------------------------------------------
	// Purpose: 
	// Input  : ladders - 
	//			maxdist - 
	//			**ppLadder - 
	//			ladderOrigin - 
	//-----------------------------------------------------------------------------
	public void Findladder(float maxdist, out FuncLadder? ladder, out Vector3 ladderOrigin, FuncLadder? skipLadder) {
		FuncLadder? bestLadder = null;
		float bestDist = MAX_COORD_INTEGER;
		Vector3 bestOrigin = default; bestOrigin.Init();

		float maxdistSqr = maxdist * maxdist;


		int c = FuncLadder.GetLadderCount();
		for (int i = 0; i < c; i++) {
			FuncLadder s_ladder = FuncLadder.GetLadder(i)!;

			if (!s_ladder.IsEnabled())
				continue;

			if (skipLadder != null && s_ladder == skipLadder)
				continue;

			s_ladder.GetTopPosition(out Vector3 topPosition);
			s_ladder.GetBottomPosition(out Vector3 bottomPosition);

			MathLib.CalcClosestPointOnLineSegment(mv.GetAbsOrigin(), bottomPosition, topPosition, out Vector3 closest, out _);

			float distSqr = (closest - mv.GetAbsOrigin()).LengthSqr();

			// Too far away
			if (distSqr > maxdistSqr) {
				continue;
			}

			// Need to trace to see if it's clear
			Util.TraceLine(
				mv.GetAbsOrigin(),
				closest,
				Source.Common.Formats.BSP.Mask.PlayerSolid,
				Player,
				CollisionGroup.None,
				out Trace tr
			);

			if (tr.Fraction != 1.0f &&
				 tr.Ent != null &&
				 tr.Ent != s_ladder) {
				// Try a trace stepped up from the ground a bit, in case there's something at ground level blocking us.
				float sizez = GetPlayerMaxs().Z - GetPlayerMins().Z;

				Util.TraceLine(mv.GetAbsOrigin() + new Vector3(0, 0, sizez * 0.5f), closest,
					Source.Common.Formats.BSP.Mask.PlayerSolid,
					Player,
					CollisionGroup.None,
					out tr);

				if (tr.Fraction != 1.0f &&
					 tr.Ent != null &&
					 tr.Ent != s_ladder &&
					 !tr.Ent!.IsSolidFlagSet(SolidFlags.Trigger)) {
					continue;
				}
			}

			// See if this is the best one so far
			if (distSqr < bestDist) {
				bestDist = distSqr;
				bestLadder = s_ladder;
				bestOrigin = closest;
			}
		}

		// Return best ladder spot
		ladder = bestLadder;
		ladderOrigin = bestOrigin;
	}

	public class NearbyDismountLessT : IComparer<NearbyDismount>
	{
		public static readonly NearbyDismountLessT _ = new();
		public int Compare(NearbyDismount x, NearbyDismount y) {
			return x.DistSqr.CompareTo(y.DistSqr);
		}
	}

	public void GetSortedDismountNodeList(in Vector3 org, float radius, FuncLadder? ladder, RedBlackTree<NearbyDismount> list) {
		float radiusSqr = radius * radius;

		int i;
		int c = ladder.GetDismountCount();
		for (i = 0; i < c; i++) {
			InfoLadderDismount? spot = ladder.GetDismount(i);
			if (spot == null)
				continue;

			float distSqr = (spot.GetAbsOrigin() - org).LengthSqr();
			if (distSqr > radiusSqr)
				continue;

			NearbyDismount nd;
			nd.Dismount = spot;
			nd.DistSqr = distSqr;

			list.Insert(nd);
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose: 
	//			*ladder - 
	// Output : Returns true on success, false on failure.
	//-----------------------------------------------------------------------------
	public bool ExitLadderViaDismountNode(FuncLadder? ladder, bool strict, bool useAlternate = false) {
		// Find the best ladder exit node
		float bestDot = -99999.0f;
		float bestDistance = 99999.0f;
		Vector3 bestDest = default;
		bool found = false;

		// For 'alternate' dismount
		bool foundAlternate = false;
		Vector3 alternateDest = default;
		float alternateDist = 99999.0f;

		RedBlackTree<NearbyDismount> nearbyDismounts = new(NearbyDismountLessT._);

		GetSortedDismountNodeList(mv.GetAbsOrigin(), 100.0f, ladder, nearbyDismounts);

		foreach (NearbyDismount i in nearbyDismounts.InOrderTraverse()) {
			InfoLadderDismount? spot = i.Dismount;
			if (spot == null) {
				AssertMsg(false, "What happened to the spot!!!");
				continue;
			}

			// See if it's valid to put the player there...
			Vector3 org = spot.GetAbsOrigin() + new Vector3(0, 0, 1);

			Util.TraceHull(
				org,
				org,
				GetPlayerMins((Player.GetFlags() & EntityFlags.Ducking) != 0),
				GetPlayerMaxs((Player.GetFlags() & EntityFlags.Ducking) != 0),
				Source.Common.Formats.BSP.Mask.PlayerSolid,
				Player,
				CollisionGroup.PlayerMovement,
				out Trace tr);

			// Nope...
			if (tr.StartSolid)
				continue;

			// Find the best dot product
			Vector3 vecToSpot = org - (mv.GetAbsOrigin() + Player.GetViewOffset());
			vecToSpot.Z = 0.0f;
			float d = MathLib.VectorNormalize(ref vecToSpot);

			float dot = vecToSpot.Dot(Forward);

			// We're not facing at it...ignore
			if (dot < 0.5f) {
				if (useAlternate && d < alternateDist) {
					alternateDest = org;
					alternateDist = d;
					foundAlternate = true;
				}

				continue;
			}

			if (dot > bestDot) {
				bestDest = org;
				bestDistance = d;
				bestDot = dot;
				found = true;
			}
		}

		if (found) {
			// Require a more specific 
			if (strict &&
				((bestDot < 0.7f) || (bestDistance > 40.0f))) {
				return false;
			}

			StartForcedMove(false, Player.MaxSpeed(), bestDest, null);
			return true;
		}

		if (useAlternate) {
			// Desperate. Don't refuse to let a person off of a ladder if it can be helped. Use the
			// alternate dismount if there is one.
			if (foundAlternate && alternateDist <= 60.0f) {
				StartForcedMove(false, Player.MaxSpeed(), alternateDest, null);
				return true;
			}
		}

		return false;
	}

	//-----------------------------------------------------------------------------
	// Purpose: 
	// Input  : bOnLadder - 
	//-----------------------------------------------------------------------------
	protected override void FullLadderMove() {
#if !CLIENT_DLL
		FuncLadder? ladder = GetLadder();
		Assert(ladder);
		if (ladder == null) {
			return;
		}

		CheckWater();

		// Was jump button pressed?  If so, don't do anything here
		if ((mv.Buttons & InButtons.Jump) != 0) {
			CheckJumpButton();
			return;
		}
		else
			mv.OldButtons &= ~InButtons.Jump;

		Player.SetGroundEntity(null);

		// Remember old positions in case we cancel this movement
		Vector3 oldVelocity = mv.Velocity;
		Vector3 oldOrigin = mv.GetAbsOrigin();
		ladder.GetTopPosition(out Vector3 topPosition);
		ladder.GetBottomPosition(out Vector3 bottomPosition);

		// Compute parametric distance along ladder vector...
		MathLib.CalcDistanceSqrToLine(mv.GetAbsOrigin(), topPosition, bottomPosition, out float oldt);

		// Perform the move accounting for any base velocity.
		MathLib.VectorAdd(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);
		TryPlayerMove();
		MathLib.VectorSubtract(mv.Velocity, Player.GetBaseVelocity(), out mv.Velocity);

		// Pressed buttons are "changed(xor)" and'ed with the mask of currently held buttons
		InButtons buttonsChanged = (mv.OldButtons ^ mv.Buttons);  // These buttons have changed this frame
		InButtons buttonsPressed = buttonsChanged & mv.Buttons;
		bool pressed_use = (buttonsPressed & InButtons.Use) != 0;
		bool pressing_forward_or_side = mv.ForwardMove != 0.0f || mv.SideMove != 0.0f;

		Vector3 ladderVec = topPosition - bottomPosition;
		float LadderLength = MathLib.VectorNormalize(ref ladderVec);
		// This test is not perfect by any means, but should help a bit
		bool moving_along_ladder = false;
		if (pressing_forward_or_side) {
			float fwdDot = Forward.Dot(ladderVec);
			if (MathF.Abs(fwdDot) > 0.9f) {
				moving_along_ladder = true;
			}
		}

		// Compute parametric distance along ladder vector...
		MathLib.CalcDistanceSqrToLine(mv.GetAbsOrigin(), topPosition, bottomPosition, out float newt);

		// Fudge of 2 units
		float tolerance = 1.0f / LadderLength;

		bool wouldleaveladder = false;
		// Moving pPast top or bottom?
		if (newt < -tolerance) {
			wouldleaveladder = newt < oldt;
		}
		else if (newt > (1.0f + tolerance)) {
			wouldleaveladder = newt > oldt;
		}

		// See if we are near the top or bottom but not moving
		float dist1sqr, dist2sqr;

		dist1sqr = (topPosition - mv.GetAbsOrigin()).LengthSqr();
		dist2sqr = (bottomPosition - mv.GetAbsOrigin()).LengthSqr();

		float dist = Math.Min(dist1sqr, dist2sqr);
		bool neardismountnode = (dist < 16.0f * 16.0f) ? true : false;
		float ladderUnitsPerTick = (float)(MAX_CLIMB_SPEED * gpGlobals.IntervalPerTick);
		bool neardismountnode2 = (dist < ladderUnitsPerTick * ladderUnitsPerTick) ? true : false;

		// Really close to node, cvar is set, and pressing a key, then simulate a +USE
		bool auto_dismount_use = (neardismountnode2 &&
									sv_autoladderdismount.GetBool() &&
									pressing_forward_or_side &&
									!moving_along_ladder);

		bool fully_underwater = (Player.GetWaterLevel() == WaterLevel.Eyes) ? true : false;

		// If the user manually pressed use or we're simulating it, then use_dismount will occur
		bool use_dismount = pressed_use || auto_dismount_use;

		if (fully_underwater && !use_dismount) {
			// If fully underwater, we require looking directly at a dismount node 
			///  to "float off" a ladder mid way...
			if (ExitLadderViaDismountNode(ladder, true)) {
				// See if they +used a dismount point mid-span..
				return;
			}
		}

		// If the movement would leave the ladder and they're not automated or pressing use, disallow the movement
		if (!use_dismount) {
			if (wouldleaveladder) {
				// Don't let them leave the ladder if they were on it
				mv.Velocity = oldVelocity;
				mv.SetAbsOrigin(oldOrigin);
			}
			return;
		}

		// If the move would not leave the ladder and we're near close to the end, then just accept the move
		if (!wouldleaveladder && !neardismountnode) {
			// Otherwise, if the move would leave the ladder, disallow it.
			if (pressed_use) {
				if (ExitLadderViaDismountNode(ladder, false)) {
					// See if they +used a dismount point mid-span..
					return;
				}

				Player.SetMoveType(MoveType.Walk);
				Player.SetMoveCollide(MoveCollide.Default);
				SetLadder(null);
				GetHL2Player().m_bPlayUseDenySound = false;

				// Dismount with a bit of velocity in facing direction
				MathLib.VectorScale(Forward, USE_DISMOUNT_SPEED, out mv.Velocity);
				mv.Velocity.Z= 50;
			}
			return;
		}

		// Debounce the use key
		if (pressed_use) 
			SwallowUseKey();

		// Try auto exit, if possible
		if (ExitLadderViaDismountNode(ladder, false, pressed_use)) 
			return;

		if (wouldleaveladder) {
			// Otherwise, if the move would leave the ladder, disallow it.
			if (pressed_use) {
				Player.SetMoveType(MoveType.Walk);
				Player.SetMoveCollide(MoveCollide.Default);
				SetLadder(null);

				// Dismount with a bit of velocity in facing direction
				MathLib.VectorScale(Forward, USE_DISMOUNT_SPEED, out mv.Velocity);
				mv.Velocity.Z= 50;
			}
			else {
				mv.Velocity = oldVelocity;
				mv.SetAbsOrigin(oldOrigin);
			}
		}
#endif
	}

	public bool CheckLadderAutoMountEndPoint(FuncLadder? ladder, in Vector3 bestOrigin) {
		// See if we're really near an endpoint
		if (ladder == null)
			return false;

		ladder.GetTopPosition(out Vector3 top);
		ladder.GetBottomPosition(out Vector3 bottom);

		float d1, d2;

		d1 = (top - mv.GetAbsOrigin()).LengthSqr();
		d2 = (bottom - mv.GetAbsOrigin()).LengthSqr();

		if (d1 > 16 * 16 && d2 > 16 * 16)
			return false;

		Vector3 ladderAxis;

		if (d1 < 16 * 16)
			// Close to top
			ladderAxis = bottom - top;
		else
			ladderAxis = top - bottom;

		MathLib.VectorNormalize(ref ladderAxis);

		if (ladderAxis.Dot(Forward) > sv_ladderautomountdot.GetFloat()) {
			StartForcedMove(true, Player.MaxSpeed(), bestOrigin, ladder);
			return true;
		}

		return false;
	}

	public bool CheckLadderAutoMountCone(FuncLadder? ladder, in Vector3 bestOrigin, float maxAngleDelta, float maxDistToLadder) {
		// Never 'back' onto ladders or stafe onto ladders
		if (ladder != null &&
			(mv.ForwardMove > 0.0f)) {
			ladder.GetTopPosition(out Vector3 top);
			ladder.GetBottomPosition(out Vector3 bottom);

			Vector3 ladderAxis = top - bottom;
			MathLib.VectorNormalize(ref ladderAxis);

			Vector3 probe = mv.GetAbsOrigin();

			MathLib.CalcClosestPointOnLineSegment(probe, bottom, top, out Vector3 closest, out _);

			Vector3 vecToLadder = closest - probe;

			float dist = MathLib.VectorNormalize(ref vecToLadder);

			Vector3 flatLadder = vecToLadder;
			flatLadder.Z = 0.0f;
			Vector3 flatForward = Forward;
			flatForward.Z = 0.0f;

			MathLib.VectorNormalize(ref flatLadder);
			MathLib.VectorNormalize(ref flatForward);

			float facingDot = flatForward.Dot(flatLadder);
			float angle = MathF.Acos(facingDot) * 180.0f / MathF.PI;

			bool closetoladder = (dist != 0.0f && dist < maxDistToLadder) ? true : false;
			bool reallyclosetoladder = (dist != 0.0f && dist < 4.0f) ? true : false;

			bool facingladderaxis = (angle < maxAngleDelta) ? true : false;
			bool facingalongaxis = ((float)MathF.Abs(ladderAxis.Dot(Forward)) > sv_ladderautomountdot.GetFloat()) ? true : false;
#if false
		Msg( "close %i length %.3f maxdist %.3f facing %.3f dot %.3f ang %.3f\n",
			closetoladder ? 1 : 0,
			dist,
			maxDistToLadder,
			(float)MathF.Abs( ladderAxis.Dot( Forward ) ),
			facingDot, 
			angle);
#endif

			// Tracker 21776:  Don't mount ladders this way if strafing
			bool strafing = (MathF.Abs(mv.SideMove) < 1.0f) ? false : true;

			if (((facingDot > 0.0f && !strafing) || facingalongaxis) &&
				(facingladderaxis || reallyclosetoladder) &&
				closetoladder) {
				StartForcedMove(true, Player.MaxSpeed(), bestOrigin, ladder);
				return true;
			}
		}

		return false;
	}

	//-----------------------------------------------------------------------------
	// Purpose: Must be facing toward ladder
	// Input  : *ladder - 
	// Output : Returns true on success, false on failure.
	//-----------------------------------------------------------------------------
	public bool LookingAtLadder(FuncLadder? ladder) {
		if (ladder == null)
			return false;

		// Get ladder end points
		ladder.GetTopPosition(out Vector3 top);
		ladder.GetBottomPosition(out Vector3 bottom);

		// Find closest point on ladder to player (could be an endpoint)
		MathLib.CalcClosestPointOnLineSegment(mv.GetAbsOrigin(), bottom, top, out Vector3 closest, out _);

		// Flatten our view direction to 2D
		Vector3 flatForward = Forward;
		flatForward.Z = 0.0f;

		// Because the ladder itself is not a solid, the player's origin may actually be 
		// permitted to pass it, and that will screw up our dot product.
		// So back up the player's origin a bit to do the facing calculation.
		Vector3 vecAdjustedOrigin = mv.GetAbsOrigin() - 8.0f * flatForward;

		// Figure out vector from player to closest point on ladder
		Vector3 vecToLadder = closest - vecAdjustedOrigin;

		// Flatten it to 2D
		Vector3 flatLadder = vecToLadder;
		flatLadder.Z = 0.0f;

		// Normalize the vectors (unnecessary)
		MathLib.VectorNormalize(ref flatLadder);
		MathLib.VectorNormalize(ref flatForward);

		// Compute dot product to see if forward is in same direction as vec to ladder
		float facingDot = flatForward.Dot(flatLadder);

		float requiredDot = (sv_ladder_useonly.GetBool()) ? -0.99f : 0.0f;

		// Facing same direction if dot > = requiredDot...
		bool facingladder = (facingDot >= requiredDot);

		return facingladder;
	}

	//-----------------------------------------------------------------------------
	// Purpose: 
	// Input  : &trace - 
	//-----------------------------------------------------------------------------
	public bool CheckLadderAutoMount(FuncLadder? ladder, in Vector3 bestOrigin) {
#if !CLIENT_DLL

		if (ladder != null) {
			StartForcedMove(true, Player.MaxSpeed(), bestOrigin, ladder);
			return true;
		}

#endif
		return false;
	}

	//-----------------------------------------------------------------------------
	// Purpose: 
	//-----------------------------------------------------------------------------
	protected override bool LadderMove() {
		if (Player.GetMoveType() == MoveType.Noclip) {
			SetLadder(null);
			return false;
		}

		// If being forced to mount/dismount continue to act like we are on the ladder
		if (IsForceMoveActive() && ContinueForcedMove()) {
			return true;
		}

		FuncLadder? bestLadder = null;
		Vector3 bestOrigin = new(0, 0, 0);

		FuncLadder? ladder = GetLadder();

		// Something 1) deactivated the ladder...  or 2) something external applied
		//  a force to us.  In either case  make the player fall, etc.
		if (ladder != null && (!ladder.IsEnabled() || (Player.GetBaseVelocity().LengthSqr() > 1.0f))) {
			GetHL2Player().ExitLadder();
			ladder = null;
		}

		if (ladder == null)
			Findladder(64.0f, out bestLadder, out bestOrigin, null);

#if !CLIENT_DLL
		if (ladder == null && bestLadder != null && sv_ladder_useonly.GetBool()) 
			GetHL2Player().DisplayLadderHudHint();

#endif

		InButtons buttonsChanged = (mv.OldButtons ^ mv.Buttons);  // These buttons have changed this frame
		InButtons buttonsPressed = buttonsChanged & mv.Buttons;
		bool pressed_use = (buttonsPressed & InButtons.Use) != 0;

		// If I'm already moving on a ladder, use the previous ladder direction
		if (ladder == null && !pressed_use) {
			// If flying through air, allow mounting ladders if we are facing < 15 degress from the ladder and we are close
			if (ladder == null && !sv_ladder_useonly.GetBool()) {
				// Tracker 6625:  Don't need to be leaping to auto mount using this method...
				// But if we are on the ground, then we must not be backing into the ladder (Tracker 12961)
				bool onground = Player.GetGroundEntity() != null ? true : false;
				if (!onground || (mv.ForwardMove > 0.0f)) {
					if (CheckLadderAutoMountCone(bestLadder, bestOrigin, 15.0f, 32.0f)) {
						return true;
					}
				}

				// Pressing forward while looking at ladder and standing (or floating) near a mounting point
				if (mv.ForwardMove > 0.0f) {
					if (CheckLadderAutoMountEndPoint(bestLadder, bestOrigin)) {
						return true;
					}
				}
			}

			return false;
		}

		if (ladder == null &&
			LookingAtLadder(bestLadder) &&
			CheckLadderAutoMount(bestLadder, bestOrigin)) {
			return true;
		}

		// Reassign the ladder
		ladder = GetLadder();
		if (ladder == null)
			return false;

		// Don't play the deny sound
		if (pressed_use)
			GetHL2Player().m_bPlayUseDenySound = false;

		// Make sure we are on the ladder
		Player.SetMoveType(MoveType.Ladder);
		Player.SetMoveCollide(MoveCollide.Default);

		Player.SetGravity(0.0f);

		float forwardSpeed = 0.0f;
		float rightSpeed = 0.0f;

		float speed = Player.MaxSpeed();


		if ((mv.Buttons & InButtons.Back) != 0) forwardSpeed -= speed;
		if ((mv.Buttons & InButtons.Forward) != 0) forwardSpeed += speed;
		if ((mv.Buttons & InButtons.MoveLeft) != 0) rightSpeed -= speed;
		if ((mv.Buttons & InButtons.MoveRight) != 0) rightSpeed += speed;

		if ((mv.Buttons & InButtons.Jump) != 0) {
			Player.SetMoveType(MoveType.Walk);
			// Remove from ladder
			SetLadder(null);

			// Jump in view direction
			Vector3 jumpDir = Forward;

			// unless pressing backward or something like that
			if (mv.ForwardMove < 0.0f) {
				jumpDir = -jumpDir;
			}

			MathLib.VectorNormalize(ref jumpDir);

			MathLib.VectorScale(jumpDir, MAX_CLIMB_SPEED, out mv.Velocity);
			// Tracker 13558:  Don't add any extra z velocity if facing downward at all
			if (Forward.Z >= 0.0f) {
				mv.Velocity.Z = mv.Velocity.Z + 50;
			}
			return false;
		}

		if (forwardSpeed != 0 || rightSpeed != 0) {
			// See if the player is looking toward the top or the bottom
			Vector3 velocity;

			MathLib.VectorScale(Forward, forwardSpeed, out velocity);
			MathLib.VectorMA(velocity, rightSpeed, Right, out velocity);

			MathLib.VectorNormalize(ref velocity);

			ladder.ComputeLadderDir(out Vector3 ladderUp);
			MathLib.VectorNormalize(ref ladderUp);

			ladder.GetTopPosition(out Vector3 topPosition);
			ladder.GetBottomPosition(out Vector3 bottomPosition);

			// Check to see if we've mounted the ladder in a bogus spot and, if so, just fall off the ladder...
			float dummyt = 0.0f;
			float distFromLadderSqr = MathLib.CalcDistanceSqrToLine(mv.GetAbsOrigin(), topPosition, bottomPosition, out dummyt);
			if (distFromLadderSqr > 36.0f) {
				// Uh oh, we fell off zee ladder...
				Player.SetMoveType(MoveType.Walk);
				// Remove from ladder
				SetLadder(null);
				return false;
			}

			bool ishorizontal = MathF.Abs(topPosition.Z - bottomPosition.Z) < 64.0f ? true : false;

			float changeover = ishorizontal ? 0.0f : 0.3f;

			float factor = 1.0f;
			if (velocity.Z >= 0) {
				float dotTop = ladderUp.Dot(velocity);
				if (dotTop < -changeover) {
					// Aimed at bottom
					factor = -1.0f;
				}
			}
			else {
				float dotBottom = -ladderUp.Dot(velocity);
				if (dotBottom > changeover) {
					factor = -1.0f;
				}
			}

			mv.Velocity = MAX_CLIMB_SPEED * factor * ladderUp;
		}
		else {
			mv.Velocity.Init();
		}

		return true;
	}

	protected override void SetGroundEntity(ref Trace pm) {
		BaseEntity? newGround = !Unsafe.IsNullRef(ref pm) ? pm.Ent : null;

		//Adrian: Special case for combine balls.
		if (newGround != null && newGround.GetCollisionGroup() == HL2COLLISION_GROUP_COMBINE_BALL_NPC)
			return;

		base.SetGroundEntity(ref pm);
	}

	protected override bool CanAccelerate() {
#if HL2MP
		if (Player.IsObserver()) {
			return true;
		}
#endif

		base.CanAccelerate();

		return true;
	}

	static readonly HL2GameMovement gameMovement = new();
	public static IGameMovement g_pGameMovement {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => gameMovement;
	}
}

#endif
