#if CLIENT_DLL || GAME_DLL

global using static Game.Shared.MultiplayerAnimStateGlobals;

#if CLIENT_DLL
using Game.Client;
#endif

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Mathematics;

using System.Numerics;

using FIELD = Source.FIELD<Game.Shared.MultiPlayerAnimState>;

namespace Game.Shared;

public enum PlayerAnimEvent
{
	AttackPrimary,
	AttackSecondary,
	AttackGrenade,
	Reload,
	ReloadLoop,
	ReloadEnd,
	Jump,
	Swim,
	Die,
	FlinchChest,
	FlinchHead,
	FlinchLeftArm,
	FlinchRightArm,
	FlinchLeftLeg,
	FlinchRightLeg,
	DoubleJump,
	Cancel,
	CancelReload = 23,
	Spawn = 17,
	SnapYaw,
	Custom,
	CustomGesture,
	CustomSequence,
	CustomGestureSequence,

	// CHECK: Is this stuff in Garry's Mod? It might be removed, AttackPre conflicts with CancelReload as well.
	AttackPre, 
	AttackPost,
	Grenade1Draw,
	Grenade2Draw,
	Grenade1Throw,
	Grenade2Throw,
	VoiceCommandGesture,
	DoubleJumpCrouch,
	StunBegin,
	StunMiddle,
	StunEnd,
	PasstimeThrowBegin,
	PasstimeThrowMiddle,
	PasstimeThrowEnd,
	PasstimeThrowCancel,
	AttackPrimarySuper,
	Count
}

public enum GestureSlotIndex
{
	Invalid = -1,
	AttackAndReload = 0,
	Grenade,
	Jump,
	Swim,
	Flinch,
	VCD,
	Custom,
	Count
}

public static class MultiplayerAnimStateGlobals
{
	public static readonly ConVar anim_showmainactivity = new("anim_showmainactivity", "0", FCvar.Cheat, "Show the idle, walk, run, and/or sprint activities.");
	public static readonly ConVar anim_showstate = new("anim_showstate", "-1", FCvar.Cheat | FCvar.Replicated | FCvar.DevelopmentOnly, "Show the (client) animation state for the specified entity (-1 for none).");
	public static readonly ConVar anim_showstatelog = new("anim_showstatelog", "0", FCvar.Cheat | FCvar.Replicated | FCvar.DevelopmentOnly, "1 to output anim_showstate to Msg(). 2 to store in AnimState.log. 3 for both.");
	public static readonly ConVar mp_showgestureslots = new("mp_showgestureslots", "-1", FCvar.Cheat | FCvar.Replicated | FCvar.DevelopmentOnly, "Show multiplayer client/server gesture slot information for the specified player index (-1 for no one).");
	public static readonly ConVar mp_slammoveyaw = new("mp_slammoveyaw", "0", FCvar.Replicated | FCvar.DevelopmentOnly, "Force movement yaw along an animation path.");

	public static bool IsCustomPlayerAnimEvent(PlayerAnimEvent evt) =>
		(evt == PlayerAnimEvent.Custom) || (evt == PlayerAnimEvent.CustomGesture) ||
		(evt == PlayerAnimEvent.CustomSequence) || (evt == PlayerAnimEvent.CustomGestureSequence);
}

public class GestureSlot
{
	public int GestureSlotIndex;
	public Activity Activity;
	public bool AutoKill;
	public bool Active;
	public AnimationLayerRef? AnimLayer;
}

public struct MultiPlayerPoseData
{
	public int MoveX;
	public int MoveY;
	public int AimYaw;
	public int AimPitch;
	public int BodyHeight;
	public int MoveYaw;
	public int MoveScale;

	public float EstimateYaw;
	public float LastAimTurnTime;

	public void Init() {
		MoveX = 0;
		MoveY = 0;
		AimYaw = 0;
		AimPitch = 0;
		BodyHeight = 0;
		MoveYaw = 0;
		MoveScale = 0;
		EstimateYaw = 0.0f;
		LastAimTurnTime = 0.0f;
	}
}

public struct DebugPlayerAnimData
{
	public float Speed;
	public float AimPitch;
	public float AimYaw;
	public float BodyHeight;
	public Vector2 MoveYaw;

	public void Init() {
		Speed = 0.0f;
		AimPitch = 0.0f;
		AimYaw = 0.0f;
		BodyHeight = 0.0f;
		MoveYaw = default;
	}
}

public struct MultiPlayerMovementData
{
	public float WalkSpeed;
	public float RunSpeed;
	public float SprintSpeed;
	public float BodyYawRate;
}

public class MultiPlayerAnimState
{
	public bool ForceAimYaw;

	protected readonly List<GestureSlot> GestureSlots = [];

	protected BasePlayer? Player;

	protected QAngle AngRender;

	protected bool PoseParameterInit;
	protected MultiPlayerPoseData PoseParameterData;
	protected DebugPlayerAnimData DebugAnimData;

	protected bool CurrentFeetYawInitialized;
	protected TimeUnit_t LastAnimationStateClearTime;

	protected float EyeYaw;
	protected float EyePitch;
	protected float GoalFeetYaw;
	protected float CurrentFeetYaw;
	protected TimeUnit_t LastAimTurnTime;

	protected MultiPlayerMovementData MovementData;

	protected bool Jumping;
	protected TimeUnit_t JumpStartTime;
	protected bool FirstJumpFrame;

	protected bool InSwim;
	protected bool FirstSwimFrame;

	protected bool Dying;
	protected bool FirstDyingFrame;

	protected Activity CurrentMainSequenceActivity;

	protected int SpecificMainSequence;

	protected EHANDLE ActiveWeapon = new();

#if CLIENT_DLL
	protected TimeUnit_t LastGroundSpeedUpdateTime;
	protected readonly InterpolatedVar<float> iv_MaxGroundSpeed = new();
#endif
	protected float MaxGroundSpeed;

	protected int MovementSequence;
	protected LegAnimType LegAnimType;

	public MultiPlayerAnimState() { }
	public MultiPlayerAnimState(BasePlayer player, ref MultiPlayerMovementData movementData) {
		PoseParameterInit = false;
		PoseParameterData.Init();
		DebugAnimData.Init();

		Player = null;
		AngRender.Init();

		CurrentFeetYawInitialized = false;
		LastAnimationStateClearTime = 0.0f;

		EyeYaw = 0.0f;
		EyePitch = 0.0f;
		GoalFeetYaw = 0.0f;
		CurrentFeetYaw = 0.0f;
		LastAimTurnTime = 0.0f;

		Jumping = false;
		JumpStartTime = 0.0f;
		FirstJumpFrame = false;

		InSwim = false;
		FirstSwimFrame = true;

		Dying = false;
		FirstDyingFrame = true;

		CurrentMainSequenceActivity = Activity.ACT_INVALID;
		SpecificMainSequence = -1;

		ActiveWeapon.Set(null);

#if CLIENT_DLL
		iv_MaxGroundSpeed.Setup(this, FIELD.OF(nameof(MaxGroundSpeed)), LatchFlags.LatchAnimationVar | LatchFlags.InterpolateLinearOnly);
		LastGroundSpeedUpdateTime = 0.0f;
#endif

		MaxGroundSpeed = 0.0f;

		ForceAimYaw = false;

		Init(player, ref movementData);

		MovementSequence = -1;
		LegAnimType = LegAnimType.Anim9Way;

		InitGestureSlots();
	}

	protected virtual void Init(BasePlayer player, ref MultiPlayerMovementData movementData) {
		Player = player;

		MovementData = movementData;
	}

	public virtual void ClearAnimationState() {
		Jumping = false;
		Dying = false;
		CurrentFeetYawInitialized = false;
		LastAnimationStateClearTime = gpGlobals.CurTime;
		SpecificMainSequence = -1;

		ResetGestureSlots();
	}
	public virtual void DoAnimationEvent(PlayerAnimEvent evt, int data = 0) => throw new NotImplementedException();
	protected virtual void PlayFlinchGesture(Activity activity) => throw new NotImplementedException();
	protected bool InitGestureSlots() {
		for (int i = 0; i < (int)GestureSlotIndex.Count; ++i)
			GestureSlots.Add(new());

		for (int iGesture = 0; iGesture < (int)GestureSlotIndex.Count; ++iGesture)
			GestureSlots[iGesture].AnimLayer = null;

		BasePlayer player = GetBasePlayer();

		player.SetNumAnimOverlays((int)GestureSlotIndex.Count);

		for (int iGesture = 0; iGesture < (int)GestureSlotIndex.Count; ++iGesture) {
			GestureSlots[iGesture].AnimLayer = player.GetAnimOverlay(iGesture);
			if (GestureSlots[iGesture].AnimLayer == null)
				return false;

			ResetGestureSlot(iGesture);
		}

		return true;
	}

	protected void ShutdownGestureSlots() => GestureSlots.Clear();

	public void ResetGestureSlots() {
		for (int iGesture = 0; iGesture < (int)GestureSlotIndex.Count; ++iGesture)
			ResetGestureSlot(iGesture);
	}

	public void ResetGestureSlot(int gestureSlot) {
		Assert(gestureSlot >= 0 && gestureSlot < (int)GestureSlotIndex.Count);

		if (!VerifyAnimLayerInSlot(gestureSlot))
			return;

		GestureSlot pGestureSlot = GestureSlots[gestureSlot];
		if (pGestureSlot != null) {
#if CLIENT_DLL
			pGestureSlot.AnimLayer!.Cycle = 1.0;

			RunGestureSlotAnimEventsToCompletion(pGestureSlot);
#endif

			pGestureSlot.GestureSlotIndex = (int)GestureSlotIndex.Invalid;
			pGestureSlot.Activity = Activity.ACT_INVALID;
			pGestureSlot.AutoKill = false;
			pGestureSlot.Active = false;
			if (pGestureSlot.AnimLayer != null) {
				pGestureSlot.AnimLayer.SetOrder(MAX_OVERLAYS);
#if CLIENT_DLL
				pGestureSlot.AnimLayer.Reset();
#endif
			}
		}
	}
#if CLIENT_DLL
	protected void RunGestureSlotAnimEventsToCompletion(GestureSlot gesture) {
		BasePlayer player = GetBasePlayer();
		if (player == null)
			return;

		StudioHdr? studioHdr = player.GetModelPtr();
		if (studioHdr == null)
			return;

		MStudioSeqDesc seqdesc = studioHdr.Seqdesc(gesture.AnimLayer!.Sequence);
		if (seqdesc.NumEvents > 0) {
			for (int i = 0; i < seqdesc.NumEvents; i++) {
				MStudioEvent pevent = seqdesc.Event(i);

				if (((AnimEventType)pevent.Type & AnimEventType.NewEventSystem) != 0) {
					if (((AnimEventType)pevent.Type & AnimEventType.Client) == 0)
						continue;
				}
				else if (pevent.Event < 5000)
					continue;

				if (pevent.Cycle > gesture.AnimLayer.PrevCycle &&
					pevent.Cycle <= gesture.AnimLayer.Cycle) {
					// FireEvent is not ported yet.
					// player.FireEvent(player.GetAbsOrigin(), player.GetAbsAngles(), pevent.Event, pevent.Options());
				}
			}
		}
	}
#endif
	public bool IsGestureSlotActive(int gestureSlot) {
		Assert(gestureSlot >= 0 && gestureSlot < (int)GestureSlotIndex.Count);
		return GestureSlots[gestureSlot].Active;
	}

	public bool VerifyAnimLayerInSlot(int gestureSlot) {
		if (gestureSlot < 0 || gestureSlot >= (int)GestureSlotIndex.Count)
			return false;

		if (GetBasePlayer().GetNumAnimOverlays() < gestureSlot + 1) {
			AssertMsg(false, $"Player {GetBasePlayer().EntIndex()} doesn't have gesture slot {gestureSlot} any more.");
			Msg($"Player {GetBasePlayer().EntIndex()} doesn't have gesture slot {gestureSlot} any more.\n");
			GestureSlots[gestureSlot].AnimLayer = null;
			return false;
		}

		AnimationLayerRef expected = GetBasePlayer().GetAnimOverlay(gestureSlot);
		if (GestureSlots[gestureSlot].AnimLayer != expected)
			GestureSlots[gestureSlot].AnimLayer = expected;

		return true;
	}

	protected bool IsGestureSlotPlaying(int gestureSlot, Activity gestureActivity) {
		Assert(gestureSlot >= 0 && gestureSlot < (int)GestureSlotIndex.Count);

		if (!IsGestureSlotActive(gestureSlot))
			return false;

		return GestureSlots[gestureSlot].Activity == gestureActivity;
	}
	public virtual void RestartGesture(int gestureSlot, Activity gestureActivity, bool autoKill = true) {
		Assert(gestureSlot >= 0 && gestureSlot < (int)GestureSlotIndex.Count);

		if (!VerifyAnimLayerInSlot(gestureSlot))
			return;

		if (!IsGestureSlotPlaying(gestureSlot, gestureActivity)) {
#if CLIENT_DLL
			if (IsGestureSlotActive(gestureSlot)) {
				GestureSlot gesture = GestureSlots[gestureSlot];
				if (gesture != null && gesture.AnimLayer != null) {
					gesture.AnimLayer.Cycle = 1.0;
					RunGestureSlotAnimEventsToCompletion(GestureSlots[gestureSlot]);
				}
			}
#endif

			Activity idealGestureActivity = TranslateActivity(gestureActivity);
			AddToGestureSlot(gestureSlot, idealGestureActivity, autoKill);
			return;
		}

		GestureSlots[gestureSlot].AnimLayer!.Cycle = 0.0f;
		GestureSlots[gestureSlot].AnimLayer!.PrevCycle = 0.0f;
	}

	protected void AddToGestureSlot(int gestureSlot, Activity gestureActivity, bool autoKill) {
		Assert(gestureSlot >= 0 && gestureSlot < (int)GestureSlotIndex.Count);

		BasePlayer player = GetBasePlayer();
		if (player == null)
			return;

		if (GestureSlots[gestureSlot].AnimLayer == null)
			return;

		if (!VerifyAnimLayerInSlot(gestureSlot))
			return;

		int gestureSequence = player.SelectWeightedSequence(gestureActivity);
		if (gestureSequence <= 0)
			return;

		GestureSlot slot = GestureSlots[gestureSlot];
		AnimationLayerRef layer = slot.AnimLayer!;

#if CLIENT_DLL
		slot.GestureSlotIndex = gestureSlot;
		slot.Activity = gestureActivity;
		slot.AutoKill = autoKill;
		slot.Active = true;
		layer.Sequence = gestureSequence;
		layer.Order = gestureSlot;
		layer.Weight = 1.0f;
		layer.PlaybackRate = 1.0f;
		layer.Cycle = 0.0f;
		layer.PrevCycle = 0.0f;
		layer.LayerAnimtime = 0.0f;
		layer.LayerFadeOuttime = 0.0f;

		player.OverlayPrevEventCycle[gestureSlot] = -1.0f;
#else
		slot.GestureSlotIndex = gestureSlot;
		slot.Activity = gestureActivity;
		slot.AutoKill = autoKill;
		slot.Active = true;
		layer.Activity = gestureActivity;
		layer.Order = gestureSlot;
		layer.Priority = 0;
		layer.Cycle = 0.0f;
		layer.PrevCycle = 0.0f;
		layer.PlaybackRate = 1.0f;
		layer.Activity = gestureActivity;
		layer.Sequence = gestureSequence;
		layer.Weight = 1.0f;
		layer.BlendIn = 0.0f;
		layer.BlendOut = 0.0f;
		layer.SequenceFinished = false;
		layer.LastEventCheck = 0.0f;
		layer.LastEventCheck = gpGlobals.CurTime;
		layer.Looping = false;
		if (autoKill)
			layer.Flags |= AnimLayerFlags.AutoKill;
		else
			layer.Flags &= ~AnimLayerFlags.AutoKill;
		layer.Flags |= AnimLayerFlags.Active;
#endif
	}
	public void AddVCDSequenceToGestureSlot(int gestureSlot, int gestureSequence, TimeUnit_t cycle = 0.0f, bool autoKill = true) => throw new NotImplementedException();
	public AnimationLayerRef? GetGestureSlotLayer(int gestureSlot) => GestureSlots[gestureSlot].AnimLayer;
	public virtual void ShowDebugInfo() {
		if (anim_showstate.GetInt() == GetBasePlayer().EntIndex())
			DebugShowAnimStateForPlayer(GetBasePlayer().IsServer());
	}
	public virtual void RestartMainSequence() {
		BaseAnimatingOverlay player = GetBasePlayer();
		if (player != null) {
			player.AnimTime = gpGlobals.CurTime;
			player.SetCycle(0);
		}
	}

	protected virtual bool HandleJumping(ref Activity idealActivity) {
		if (Jumping) {
			if (FirstJumpFrame) {
				FirstJumpFrame = false;
				RestartMainSequence();
			}

			if (GetBasePlayer().GetWaterLevel() >= WaterLevel.Waist) {
				Jumping = false;
				RestartMainSequence();
			}
			else if (gpGlobals.CurTime - JumpStartTime > 0.2f) {
				if ((GetBasePlayer().GetFlags() & EntityFlags.OnGround) != 0) {
					Jumping = false;
					RestartMainSequence();
				}
			}
		}
		if (Jumping) {
			idealActivity = Activity.ACT_MP_JUMP;
			return true;
		}
		else
			return false;
	}

	protected virtual bool HandleDucking(ref Activity idealActivity) {
		if ((GetBasePlayer().GetFlags() & EntityFlags.Ducking) != 0) {
			if (GetOuterXYSpeed() > MOVING_MINIMUM_SPEED)
				idealActivity = Activity.ACT_MP_CROUCHWALK;
			else
				idealActivity = Activity.ACT_MP_CROUCH_IDLE;

			return true;
		}

		return false;
	}

	protected virtual bool HandleSwimming(ref Activity idealActivity) {
		if (GetBasePlayer().GetWaterLevel() >= WaterLevel.Waist) {
			if (FirstSwimFrame) {
				RestartMainSequence();
				FirstSwimFrame = false;
			}

			idealActivity = Activity.ACT_MP_SWIM;
			InSwim = true;
			return true;
		}
		else {
			InSwim = false;

			if (!FirstSwimFrame)
				FirstSwimFrame = true;
		}

		return false;
	}

	protected virtual bool HandleDying(ref Activity idealActivity) {
		if (Dying) {
			if (FirstDyingFrame) {
				RestartMainSequence();
				FirstDyingFrame = false;
			}

			idealActivity = Activity.ACT_DIESIMPLE;
			return true;
		}
		else {
			if (!FirstDyingFrame)
				FirstDyingFrame = true;
		}

		return false;
	}
	protected virtual bool HandleMoving(ref Activity idealActivity) {
		float speed = GetOuterXYSpeed();

		if (speed > MOVING_MINIMUM_SPEED)
			idealActivity = Activity.ACT_MP_RUN;

		return true;
	}

	public virtual Activity CalcMainActivity() {
		Activity idealActivity = Activity.ACT_MP_STAND_IDLE;

		if (HandleJumping(ref idealActivity) ||
			HandleDucking(ref idealActivity) ||
			HandleSwimming(ref idealActivity) ||
			HandleDying(ref idealActivity)) {
		}
		else
			HandleMoving(ref idealActivity);

		ShowDebugInfo();

#if CLIENT_DLL
		if (anim_showmainactivity.GetBool())
			DebugShowActivity(idealActivity);
#endif

		return idealActivity;
	}

	public virtual Activity TranslateActivity(Activity actDesired) {
		if (InSwim) {
			switch (actDesired) {
				case Activity.ACT_MP_ATTACK_STAND_PRIMARYFIRE: actDesired = Activity.ACT_MP_ATTACK_SWIM_PRIMARYFIRE; break;
				case Activity.ACT_MP_ATTACK_STAND_SECONDARYFIRE: actDesired = Activity.ACT_MP_ATTACK_SWIM_SECONDARYFIRE; break;
				case Activity.ACT_MP_ATTACK_STAND_GRENADE: actDesired = Activity.ACT_MP_ATTACK_SWIM_GRENADE; break;
				case Activity.ACT_MP_RELOAD_STAND: actDesired = Activity.ACT_MP_RELOAD_SWIM; break;
			}
		}

		return actDesired;
	}
	protected virtual float GetCurrentMaxGroundSpeed() {
		StudioHdr? studioHdr = GetBasePlayer().GetModelPtr();

		if (studioHdr == null)
			return 1.0f;

		float prevX = GetBasePlayer().GetPoseParameter(PoseParameterData.MoveX);
		float prevY = GetBasePlayer().GetPoseParameter(PoseParameterData.MoveY);

		float d = Math.Max(MathF.Abs(prevX), MathF.Abs(prevY));
		float newX, newY;
		if (d == 0.0) {
			newX = 1.0f;
			newY = 0.0f;
		}
		else {
			newX = prevX / d;
			newY = prevY / d;
		}

		GetBasePlayer().SetPoseParameter(studioHdr, PoseParameterData.MoveX, newX);
		GetBasePlayer().SetPoseParameter(studioHdr, PoseParameterData.MoveY, newY);

		float speed = (float)GetBasePlayer().GetSequenceGroundSpeed(GetBasePlayer().GetSequence());

		GetBasePlayer().SetPoseParameter(studioHdr, PoseParameterData.MoveX, prevX);
		GetBasePlayer().SetPoseParameter(studioHdr, PoseParameterData.MoveY, prevY);

		return speed;
	}
	protected virtual float CalcMovementSpeed(out bool isMoving) => throw new NotImplementedException();
	protected virtual float CalcMovementPlaybackRate(out bool isMoving) => throw new NotImplementedException();
	protected float GetInterpolatedGroundSpeed() => throw new NotImplementedException();
	protected virtual void ComputeSequences(StudioHdr? studioHdr) {
		ComputeMainSequence();

		UpdateInterpolators();
		ComputeGestureSequence(studioHdr);
	}

	protected void ComputeMainSequence() {
		BaseAnimatingOverlay player = GetBasePlayer();

		Activity idealActivity = CalcMainActivity();

#if CLIENT_DLL
		Activity oldActivity = CurrentMainSequenceActivity;
#endif

		CurrentMainSequenceActivity = idealActivity;

		if (SpecificMainSequence >= 0) {
			if (player.GetSequence() != SpecificMainSequence) {
				player.ResetSequence(SpecificMainSequence);
				ResetGroundSpeed();
				return;
			}

			if (!player.IsSequenceFinished())
				return;

			SpecificMainSequence = -1;
			RestartMainSequence();
			ResetGroundSpeed();
		}

		int animDesired = SelectWeightedSequence(TranslateActivity(idealActivity));
		if (player.GetSequenceActivity(player.GetSequence()) == player.GetSequenceActivity(animDesired))
			return;

		if (animDesired < 0)
			animDesired = 0;

		player.ResetSequence(animDesired);

#if CLIENT_DLL
		if ((oldActivity == Activity.ACT_MP_CROUCH_IDLE || oldActivity == Activity.ACT_MP_STAND_IDLE || oldActivity == Activity.ACT_MP_DEPLOYED_IDLE || oldActivity == Activity.ACT_MP_CROUCH_DEPLOYED_IDLE) &&
			(idealActivity == Activity.ACT_MP_WALK || idealActivity == Activity.ACT_MP_CROUCHWALK))
			ResetGroundSpeed();
#endif
	}

	protected void ResetGroundSpeed() {
#if CLIENT_DLL
		MaxGroundSpeed = GetCurrentMaxGroundSpeed();
		iv_MaxGroundSpeed.Reset();
		iv_MaxGroundSpeed.NoteChanged(gpGlobals.CurTime, 0, false);
#endif
	}

	protected void UpdateInterpolators() {
		float curMaxSpeed = GetCurrentMaxGroundSpeed();

#if CLIENT_DLL
		TimeUnit_t groundSpeedInterval = 0.1;

		if (gpGlobals.CurTime - LastGroundSpeedUpdateTime >= groundSpeedInterval) {
			LastGroundSpeedUpdateTime = gpGlobals.CurTime;

			MaxGroundSpeed = curMaxSpeed;
			iv_MaxGroundSpeed.NoteChanged(gpGlobals.CurTime, groundSpeedInterval, false);
		}

		iv_MaxGroundSpeed.Interpolate(gpGlobals.CurTime, groundSpeedInterval);
#else
		MaxGroundSpeed = curMaxSpeed;
#endif
	}

	protected void ComputeFireSequence() { }
	protected void ComputeGestureSequence(StudioHdr? studioHdr) {
		for (int iGesture = 0; iGesture < (int)GestureSlotIndex.Count; ++iGesture) {
			if (!GestureSlots[iGesture].Active)
				continue;

			if (!VerifyAnimLayerInSlot(iGesture))
				continue;

			UpdateGestureLayer(studioHdr, GestureSlots[iGesture]);
		}
	}

	protected void UpdateGestureLayer(StudioHdr? studioHdr, GestureSlot gesture) {
		if (studioHdr == null || gesture == null)
			return;

		BasePlayer player = GetBasePlayer();
		if (player == null)
			return;

#if CLIENT_DLL
		TimeUnit_t cycle = gesture.AnimLayer!.Cycle;
		cycle += player.GetSequenceCycleRate(studioHdr, gesture.AnimLayer.Sequence) * gpGlobals.FrameTime * GetGesturePlaybackRate() * gesture.AnimLayer.PlaybackRate;

		gesture.AnimLayer.PrevCycle = (float)gesture.AnimLayer.Cycle;
		gesture.AnimLayer.Cycle = cycle;

		if (cycle > 1.0) {
			RunGestureSlotAnimEventsToCompletion(gesture);

			if (gesture.AutoKill) {
				ResetGestureSlot(gesture.GestureSlotIndex);
				return;
			}
			else
				gesture.AnimLayer.Cycle = 1.0;
		}
#else
		if (gesture.Activity != Activity.ACT_INVALID && gesture.AnimLayer!.Activity == Activity.ACT_INVALID)
			ResetGestureSlot(gesture.GestureSlotIndex);
#endif
	}
	public virtual void Update(float eyeYaw, float eyePitch) {
		StudioHdr? studioHdr = GetBasePlayer().GetModelPtr();
		if (studioHdr == null)
			return;

		if (!ShouldUpdateAnimState()) {
			ClearAnimationState();
			return;
		}

		EyeYaw = MathLib.AngleNormalize(eyeYaw);
		EyePitch = MathLib.AngleNormalize(eyePitch);

		ComputeSequences(studioHdr);

		if (SetupPoseParameters(studioHdr)) {
			ComputePoseParam_MoveYaw(studioHdr);

			ComputePoseParam_AimPitch(studioHdr);

			ComputePoseParam_AimYaw(studioHdr);
		}

#if CLIENT_DLL
		if (C_BasePlayer.ShouldDrawLocalPlayer())
			GetBasePlayer().SetPlaybackRate(1.0f);
#endif

		if (mp_showgestureslots.GetInt() == GetBasePlayer().EntIndex())
			DebugGestureInfo();
	}

	protected virtual bool ShouldUpdateAnimState() {
		if (GetBasePlayer().IsEffectActive(EntityEffects.NoDraw))
			return false;

#if CLIENT_DLL
		if (GetBasePlayer().IsDormant())
			return false;
#endif

		return GetBasePlayer().IsAlive() || Dying;
	}
	protected bool SetupPoseParameters(StudioHdr? studioHdr) {
		if (PoseParameterInit)
			return true;

		if (studioHdr == null)
			return false;

		PoseParameterInit = true;

		PoseParameterData.MoveX = GetBasePlayer().LookupPoseParameter(studioHdr, "move_x");
		PoseParameterData.MoveY = GetBasePlayer().LookupPoseParameter(studioHdr, "move_y");

		PoseParameterData.AimPitch = GetBasePlayer().LookupPoseParameter(studioHdr, "body_pitch");
		PoseParameterData.AimYaw = GetBasePlayer().LookupPoseParameter(studioHdr, "body_yaw");

		PoseParameterData.MoveYaw = GetBasePlayer().LookupPoseParameter(studioHdr, "move_yaw");
		PoseParameterData.MoveScale = GetBasePlayer().LookupPoseParameter(studioHdr, "move_scale");

		return true;
	}
	protected void DoMovementTest(StudioHdr? studioHdr, float x, float y) => throw new NotImplementedException();
	protected void DoMovementTest(StudioHdr? studioHdr) => throw new NotImplementedException();
	protected void GetMovementFlags(StudioHdr? studioHdr) => throw new NotImplementedException();
	protected virtual void ComputePoseParam_MoveYaw(StudioHdr? studioHdr) => throw new NotImplementedException();
	protected virtual void EstimateYaw() => throw new NotImplementedException();
	protected virtual void ComputePoseParam_AimPitch(StudioHdr? studioHdr) => throw new NotImplementedException();
	protected virtual void ComputePoseParam_AimYaw(StudioHdr? studioHdr) => throw new NotImplementedException();
	protected void ConvergeYawAngles(float goalYaw, float yawRate, TimeUnit_t deltaTime, ref float currentYaw) {
		const float FADE_TURN_DEGREES = 60.0f;

		float deltaYaw = goalYaw - currentYaw;
		float deltaYawAbs = MathF.Abs(deltaYaw);
		deltaYaw = MathLib.AngleNormalize(deltaYaw);

		float scale = 1.0f;
		scale = deltaYawAbs / FADE_TURN_DEGREES;
		scale = Math.Clamp(scale, 0.01f, 1.0f);

		float yaw = (float)(yawRate * deltaTime * scale);
		if (deltaYawAbs < yaw)
			currentYaw = goalYaw;
		else {
			float side = (deltaYaw < 0.0f) ? -1.0f : 1.0f;
			currentYaw += (yaw * side);
		}

		currentYaw = MathLib.AngleNormalize(currentYaw);
	}
	public ref readonly QAngle GetRenderAngles() => ref AngRender;

	protected virtual void GetOuterAbsVelocity(out Vector3 vel) {
#if CLIENT_DLL
		GetBasePlayer().EstimateAbsVelocity(out vel);
#else
		vel = GetBasePlayer().GetAbsVelocity();
#endif
	}

	public virtual void Release() { }

	protected float GetOuterXYSpeed() {
		GetOuterAbsVelocity(out Vector3 vel);
		return vel.Length2D();
	}
	protected void DebugShowAnimStateForPlayer(bool isServer) => throw new NotImplementedException();
	protected void DebugShowEyeYaw() => throw new NotImplementedException();
#if CLIENT_DLL
	protected void DebugShowActivity(Activity activity) => throw new NotImplementedException();
#endif
	public virtual void DebugShowAnimState(int startLine) => throw new NotImplementedException();
	protected void DebugGestureInfo() => throw new NotImplementedException();
	public void OnNewModel() => throw new NotImplementedException();

	public virtual void SetRunSpeed(float speed) => MovementData.RunSpeed = speed;
	public virtual void SetWalkSpeed(float speed) => MovementData.WalkSpeed = speed;
	public virtual void SetSprintSpeed(float speed) => MovementData.SprintSpeed = speed;
	public Activity GetCurrentMainActivity() => CurrentMainSequenceActivity;
	protected BasePlayer GetBasePlayer() => Player!;
	protected virtual int SelectWeightedSequence(Activity activity) => GetBasePlayer().SelectWeightedSequence(activity);
	protected virtual float GetGesturePlaybackRate() => 1.0f;
}

#endif
