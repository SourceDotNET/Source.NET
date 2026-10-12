using Game.Shared;

using System.Numerics;

namespace Game.Server;

public class AI_BlendedMotor : AI_Motor
{
	public AI_BlendedMotor(AI_BaseNPC? outer) : base(outer) {
		PrimaryLayer = -1;
		PrimarySequence = (int)Activity.ACT_INVALID;

		SecondaryLayer = -1;
		SecondarySequence = (int)Activity.ACT_INVALID;
		SecondaryWeight = 0.0f;

		SavedGoalActivity = Activity.ACT_INVALID;
		SavedTranslatedGoalActivity = Activity.ACT_INVALID;
		GoalSequence = (int)Activity.ACT_INVALID;

		PrevMovementSequence = (int)Activity.ACT_INVALID;
		InteriorSequence = (int)Activity.ACT_INVALID;

		DeceleratingToGoal = false;

		StartCycle = 0.0f;

		PredictiveSpeedAdjust = 1.0f;
		ReactiveSpeedAdjust = 1.0f;
		PrevOrigin1 = Vector3.Zero;
		PrevOrigin2 = Vector3.Zero;

		PrevYaw = 0.0f;
		DoTurn = 0.0f;
		DoLeft = 0.0f;
		DoRight = 0.0f;
		NextTurnAct = 0.0;
	}

	public float GetMoveScriptTotalTime() => throw new NotImplementedException();

	public override void ResetMoveCalculations() {
		base.ResetMoveCalculations();
		ScriptMove.Clear();
		ScriptTurn.Clear();
	}

	public override float OverrideMaxYawSpeed(Activity activity) {
		if (IsYawLocked())
			return 0.0f;

		switch (activity) {
			case Activity.ACT_TURN_LEFT:
			case Activity.ACT_TURN_RIGHT:
				return 45;
			default:
				if (GetOuter()!.IsMoving())
					return 15;
				return 45;
		}
	}

	public override void MoveStop() {
		base.MoveStop();

		if (PrimaryLayer != -1) {
			GetOuter()!.RemoveLayer(PrimaryLayer, 0.2f, 0.1f);
			PrimaryLayer = -1;
		}
		if (SecondaryLayer != -1) {
			GetOuter()!.RemoveLayer(SecondaryLayer, 0.2f, 0.1f);
			SecondaryLayer = -1;
		}
		PrimarySequence = (int)Activity.ACT_INVALID;
		SecondarySequence = (int)Activity.ACT_INVALID;
		PrevMovementSequence = (int)Activity.ACT_INVALID;
		InteriorSequence = (int)Activity.ACT_INVALID;
	}

	public override void UpdateYaw(int speed = -1) {
		if (IsYawLocked())
			return;

		GetOuter()!.UpdateTurnGesture();
		base.UpdateYaw(speed);
	}

	public override void MaintainTurnActivity() {
		if (NextTurnGesture > gpGlobals.CurTime || NextTurnAct > gpGlobals.CurTime || GetOuter()!.IsMoving()) {
			DoTurn = DoRight = DoLeft = 0;
			if (GetOuter()!.IsMoving())
				NextTurnAct = gpGlobals.CurTime + 0.3;
		}
		else {
			if (PrevYaw != GetOuter()!.GetAbsAngles().Y) {
				float diff = Util.AngleDiff(PrevYaw, GetOuter()!.GetAbsAngles().Y);
				if (diff < 0.0)
					DoLeft += -diff;
				else
					DoRight += diff;
				PrevYaw = GetOuter()!.GetAbsAngles().Y;
			}
			DoTurn += DoRight + DoLeft;
			DoTurn += RandomFloat(0.4f, 0.6f);
		}

		if (DoTurn > 15.0f) {
			int seq = (int)Activity.ACT_INVALID;
			if (DoLeft > DoRight)
				seq = GetOuter()!.SelectWeightedSequence(Activity.ACT_GESTURE_TURN_LEFT);
			else
				seq = GetOuter()!.SelectWeightedSequence(Activity.ACT_GESTURE_TURN_RIGHT);
			DoLeft = 0;
			DoRight = 0;

			if (seq != (int)Activity.ACT_INVALID) {
				int layer = GetOuter()!.AddGestureSequence(seq);
				if (layer != -1) {
					GetOuter()!.SetLayerPriority(layer, 100);
					float rate = RandomFloat(0.8f, 1.2f);
					if (DoTurn > 90.0)
						rate *= 1.5f;
					GetOuter()!.SetLayerPlaybackRate(layer, rate);
					NextTurnAct = gpGlobals.CurTime + GetOuter()!.GetLayerDuration(layer);
				}
				else
					NextTurnAct = gpGlobals.CurTime + 0.3;
			}
			DoTurn = DoRight = DoLeft = 0;
		}
	}

	public override bool AddTurnGesture(float yd) => false;

	public TimeUnit_t NextTurnGesture;

	public override void RecalculateYawSpeed() {
		if (IsYawLocked()) {
			SetYawSpeed(0.0f);
			return;
		}

		if (GetOuter()!.HasMemory(AI_MemoryFlags.Turning))
			return;

		SetYawSpeed(CalcYawSpeed());
	}

	public struct AI_MovementScript
	{
		public TimeUnit_t Time;
		public TimeUnit_t ElapsedTime;

		public float Dist;

		public float MaxVelocity;

		public float Yaw;
		public float AngularVelocity;

		public bool Looping;
		public int Flags;

		public AI_Waypoint? Waypoint;

		public Vector3 Location;
	}

	readonly List<AI_MovementScript> ScriptMove = [];
	readonly List<AI_MovementScript> ScriptTurn = [];

	public bool DeceleratingToGoal;

	public int PrimaryLayer;
	public int SecondaryLayer;

	public int PrimarySequence;
	public int SecondarySequence;
	public float SecondaryWeight;

	public Activity SavedGoalActivity;
	public Activity SavedTranslatedGoalActivity;
	public int GoalSequence;

	public int PrevMovementSequence;
	public int InteriorSequence;

	public float StartCycle;

	public float PredictiveSpeedAdjust;
	public float ReactiveSpeedAdjust;
	public Vector3 PrevOrigin1;
	public Vector3 PrevOrigin2;

	public float PrevYaw;
	public float DoTurn;
	public float DoLeft;
	public float DoRight;
	public TimeUnit_t NextTurnAct;
}
