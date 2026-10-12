using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

using EventType = Game.Shared.ChoreoEvent.EventType;

public enum PoseParameter_t
{
	POSE_END = int.MaxValue
}

public enum FlexWeight_t
{
	FLEX_END = int.MaxValue
}

public class AILookTargetArgs
{
	public EHANDLE Target = new();
	public Vector3 TargetPosition;
	public float Duration;
	public float Influence;
	public float Ramp;
	public bool ExcludePlayers;
	public AI_InterestTarget? Queue;
}

public class AI_BaseActor : AI_ExpresserHost_AI_BaseHumanoid
{
	public void Init(ref PoseParameter_t index, ReadOnlySpan<char> name) => index = (PoseParameter_t)LookupPoseParameter(name);
	public void Set(PoseParameter_t index, float value) => SetPoseParameter((int)index, value);
	public float Get(PoseParameter_t index) => GetPoseParameter((int)index);

	static readonly string[] g_ServerSideFlexControllers = [
		"body_rightleft",
		"chest_rightleft",
		"head_forwardback",
		"head_rightleft",
		"head_updown",
		"head_tilt",

		"gesture_updown",
		"gesture_rightleft"
	];

	public static bool IsServerSideFlexController(ReadOnlySpan<char> name) {
		int c = g_ServerSideFlexControllers.Length;
		for (int i = 0; i < c; ++i) {
			if (stricmp(name, g_ServerSideFlexControllers[i]) == 0)
				return true;
		}
		return false;
	}

	public void Init(ref FlexWeight_t index, ReadOnlySpan<char> name) {
		if (!IsServerSideFlexController(name))
			Error($"You forgot to add flex controller {name} to list in CAI_BaseActor::IsServerSideFlexController().");

		index = (FlexWeight_t)FindFlexController(name);
	}
	public void Set(FlexWeight_t index, float value) => SetFlexWeight((LocalFlexController)index, value);
	public float Get(FlexWeight_t index) => GetFlexWeight((LocalFlexController)index);

	public override void Precache() {
		base.Precache();

		if (ExpressionOverride != null)
			PrecacheInstancedScene(ExpressionOverride);

		if (IdleExpression != null)
			PrecacheInstancedScene(IdleExpression);

		if (CombatExpression != null)
			PrecacheInstancedScene(CombatExpression);

		if (AlertExpression != null)
			PrecacheInstancedScene(AlertExpression);

		if (DeathExpression != null)
			PrecacheInstancedScene(DeathExpression);
	}

	public override void SetModel(ReadOnlySpan<char> modelName) {
		base.SetModel(modelName);

		Init(ref ParameterBodyYaw, "body_yaw");
		Init(ref ParameterSpineYaw, "spine_yaw");
		Init(ref ParameterNeckTrans, "neck_trans");
		Init(ref ParameterHeadYaw, "head_yaw");
		Init(ref ParameterHeadPitch, "head_pitch");
		Init(ref ParameterHeadRoll, "head_roll");

		Init(ref FlexweightBodyRightLeft, "body_rightleft");
		Init(ref FlexweightChestRightLeft, "chest_rightleft");
		Init(ref FlexweightHeadForwardBack, "head_forwardback");
		Init(ref FlexweightHeadRightLeft, "head_rightleft");
		Init(ref FlexweightHeadUpDown, "head_updown");
		Init(ref FlexweightHeadTilt, "head_tilt");

		Init(ref ParameterGestureHeight, "gesture_height");
		Init(ref ParameterGestureWidth, "gesture_width");
		Init(ref FlexweightGestureUpDown, "gesture_updown");
		Init(ref FlexweightGestureRightLeft, "gesture_rightleft");
	}

	public override AI_Expresser? GetExpresser() {
		return Expresser;
	}

	public override bool CreateComponents() {
		if (!base.CreateComponents())
			return false;

		Expresser = CreateExpresser();
		if (Expresser == null)
			return false;

		Expresser.Connect(this);

		return true;
	}

	public virtual AI_Expresser? CreateExpresser() {
		Expresser = new AI_Expresser(this);
		return Expresser;
	}

	AI_Expresser? Expresser;

	public static readonly ConVar flex_minplayertime = new("flex_minplayertime", "5");
	public static readonly ConVar flex_maxplayertime = new("flex_maxplayertime", "7");
	public static readonly ConVar flex_minawaytime = new("flex_minawaytime", "0.5");
	public static readonly ConVar flex_maxawaytime = new("flex_maxawaytime", "1.0");
	public static readonly ConVar ai_debug_looktargets = new("ai_debug_looktargets", "0");
	public static readonly ConVar ai_debug_expressions = new("ai_debug_expressions", "0", FCvar.None, "Show random expression decisions for NPCs.");
	static readonly ConVar scene_clamplookat = new("scene_clamplookat", "1", FCvar.None, "Clamp head turns to a max of 20 degrees per think.");

	[Flags]
	enum HumanoidLatched
	{
		Eye = 0x0001,
		Head = 0x0002,
		All = 0x0003,
	}

	const float MIN_LOOK_TARGET_DIST = 1.0f;
	const float MAX_FULL_LOOK_TARGET_DIST = 10.0f;

	public override void StudioFrameAdvance() {
		LatchedPositions &= ~HumanoidLatched.All;

		base.StudioFrameAdvance();
	}

	public override void SetViewtarget(in Vector3 viewtarget) {
		LatchedPositions &= ~HumanoidLatched.Eye;

		base.SetViewtarget(viewtarget);
	}

	void UpdateLatchedValues() {
		if ((LatchedPositions & HumanoidLatched.Head) == 0) {
			LatchedPositions |= HumanoidLatched.Head;

			if (!HasCondition((int)SCOND_t.COND_IN_PVS) || !GetAttachment("eyes", out LatchedEyeOrigin, out LatchedHeadDirection, out _, out _)) {
				LatchedEyeOrigin = base.EyePosition();
				MathLib.AngleVectors(GetLocalAngles(), out LatchedHeadDirection);
			}
			LatchedPositions &= ~HumanoidLatched.Eye;
		}

		if ((LatchedPositions & HumanoidLatched.Eye) == 0) {
			LatchedPositions |= HumanoidLatched.Eye;

			if ((CapabilitiesGet() & Server.Capability.AnimatedFace) != 0) {
				LatchedEyeDirection = GetViewtarget() - LatchedEyeOrigin;
				MathLib.VectorNormalize(ref LatchedEyeDirection);
			}
			else
				LatchedEyeDirection = LatchedHeadDirection;
		}
	}

	public override Vector3 EyePosition() {
		UpdateLatchedValues();

		return LatchedEyeOrigin;
	}

	public override bool ValidEyeTarget(in Vector3 lookTargetPos) {
		Vector3 headDir = HeadDirection3D();
		Vector3 lookTargetDir = lookTargetPos - EyePosition();
		float dist = MathLib.VectorNormalize(ref lookTargetDir);

		if (dist < MIN_LOOK_TARGET_DIST)
			return false;

		float dotPr = Vector3.Dot(lookTargetDir, headDir);

		if (dotPr > 0.259f)
			return true;
		return false;
	}

	public virtual bool ValidHeadTarget(in Vector3 lookTargetPos) {
		Vector3 facing = BodyDirection3D();
		Vector3 lookTargetDir = lookTargetPos - EyePosition();
		float dist = MathLib.VectorNormalize(ref lookTargetDir);

		if (dist < MIN_LOOK_TARGET_DIST)
			return false;

		float dotPr = Vector3.Dot(lookTargetDir, facing);
		if (dotPr > 0 && MathF.Abs(lookTargetDir.Z) < 0.7f)
			return true;
		return false;
	}

	public virtual float HeadTargetValidity(in Vector3 lookTargetPos) {
		Vector3 facing = BodyDirection3D();

		int forward = LookupAttachment("forward");
		if (forward > 0)
			GetAttachment(forward, out _, out facing, out _, out _);

		Vector3 lookTargetDir = lookTargetPos - EyePosition();
		float dist = lookTargetDir.Length2D();
		MathLib.VectorNormalize(ref lookTargetDir);

		if (dist <= MIN_LOOK_TARGET_DIST)
			return 0;

		float dotPr = Vector3.Dot(lookTargetDir, facing);
		float interest = Math.Clamp(3.4142f + 3.4142f * dotPr, 0.0f, 1.0f);

		if (dist < MAX_FULL_LOOK_TARGET_DIST)
			interest = interest * (dist - MIN_LOOK_TARGET_DIST) / (MAX_FULL_LOOK_TARGET_DIST - MIN_LOOK_TARGET_DIST);

		return interest;
	}

	public override void SetHeadDirection(in Vector3 targetPos, TimeUnit_t interval) {
		Assert(false);
	}

	public float ClampWithBias(PoseParameter_t index, float value, float baseValue) => EdgeLimitPoseParameter((int)index, value, baseValue);

	public bool SetAccumulatedYawAndUpdate() {
		if (AccumYawScale > 0.0f) {
			float diff = AccumYawDelta / AccumYawScale;
			float facing = GetLocalAngles().Y + diff;

			AccumYawDelta = 0.0f;
			AccumYawScale = 0.0f;

			if (IsCurSchedule(SCHED_SCENE_GENERIC)) {
				if (!IsMoving()) {
					GetMotor()!.SetIdealYawAndUpdate(facing);
					return true;
				}
			}
		}
		return false;
	}

	public void UpdateBodyControl() {
		Set(ParameterBodyYaw, Get(FlexweightBodyRightLeft) + GoalBodyYaw);
		Set(ParameterSpineYaw, Get(FlexweightChestRightLeft) + GoalSpineYaw);
		Set(ParameterNeckTrans, Get(FlexweightHeadForwardBack));
	}

	public void UpdateHeadControl(in Vector3 headTarget, float headInfluence) {
		float target;
		float limit;

		if ((CapabilitiesGet() & Server.Capability.TurnHead) == 0)
			return;

		QAngle angBias;

		int eyes = LookupAttachment("eyes");
		int chest = LookupAttachment("chest");
		int forward = LookupAttachment("forward");

		if (eyes <= 0 || forward <= 0) {
			CapabilitiesRemove(Server.Capability.TurnHead);
			return;
		}

		GetAttachment(eyes, out Matrix3x4 eyesToWorld);

		GetAttachment(forward, out Matrix3x4 forwardToWorld);
		MathLib.MatrixInvert(forwardToWorld, out Matrix3x4 worldToForward);

		if (chest > 0) {
			GetAttachment(chest, out Matrix3x4 chestToWorld);
			MathLib.MatrixInvert(chestToWorld, out Matrix3x4 worldToChest);
			MathLib.ConcatTransforms(worldToChest, eyesToWorld, out Matrix3x4 tmpM);
			MathLib.MatrixAngles(tmpM, out angBias);

			angBias.Y -= Get(ParameterHeadYaw);
			angBias.X -= Get(ParameterHeadPitch);
			angBias.Z -= Get(ParameterHeadRoll);
		}
		else
			angBias = new(0, 0, 0);

		Matrix3x4 targetXform = forwardToWorld;
		Vector3 targetDir = headTarget - EyePosition();

		if (scene_clamplookat.GetBool()) {
			MathLib.VectorNormalize(ref targetDir);
			MathLib.VectorIRotate(targetDir, forwardToWorld, out Vector3 targetLocal);
			targetLocal.Z *= Math.Clamp(targetLocal.X, 0.1f, 1.0f);
			MathLib.VectorNormalize(ref targetLocal);
			MathLib.VectorRotate(targetLocal, forwardToWorld, out targetDir);

			headInfluence = headInfluence * Math.Clamp(targetLocal.X * 2.0f + 2.0f, 0.0f, 1.0f);
		}

		BoneSetupShared.Studio_AlignIKMatrix(ref targetXform, targetDir);

		MathLib.ConcatTransforms(worldToForward, targetXform, out Matrix3x4 headXform);
		MathLib.MatrixAngles(headXform, out QAngle targetAngles);

		float s0 = 1.0f - headInfluence + GetHeadDebounce() * headInfluence;
		float s1 = 1.0f - s0;
		GoalHeadCorrection.X = MathLib.Approach(GoalHeadCorrection.X * s0 + targetAngles.X * s1, GoalHeadCorrection.X, 10.0f);
		GoalHeadCorrection.Y = MathLib.Approach(GoalHeadCorrection.Y * s0 + targetAngles.Y * s1, GoalHeadCorrection.Y, 30.0f);
		GoalHeadCorrection.Z = MathLib.Approach(GoalHeadCorrection.Z * s0 + targetAngles.Z * s1, GoalHeadCorrection.Z, 10.0f);

		target = GoalHeadCorrection.Y + Get(FlexweightHeadRightLeft);
		limit = ClampWithBias(ParameterHeadYaw, target, angBias.Y);
		Set(ParameterHeadYaw, limit);

		target = GoalHeadCorrection.X + Get(FlexweightHeadUpDown);
		limit = ClampWithBias(ParameterHeadPitch, target, angBias.X);
		Set(ParameterHeadPitch, limit);

		target = GoalHeadCorrection.Z + Get(FlexweightHeadTilt);
		limit = ClampWithBias(ParameterHeadRoll, target, angBias.Z);
		Set(ParameterHeadRoll, limit);
	}

	public virtual float GetHeadDebounce() => 0.3f;

	public override Vector3 EyeDirection2D() {
		Vector3 eyeDirection = EyeDirection3D();
		eyeDirection.Z = 0;

		Vector2 eyeDirection2D = new(eyeDirection.X, eyeDirection.Y);
		MathLib.VectorNormalize(ref eyeDirection2D);
		eyeDirection.X = eyeDirection2D.X;
		eyeDirection.Y = eyeDirection2D.Y;

		return eyeDirection;
	}

	public override Vector3 EyeDirection3D() {
		UpdateLatchedValues();

		return LatchedEyeDirection;
	}

	public override Vector3 HeadDirection2D() {
		Vector3 headDirection = HeadDirection3D();
		headDirection.Z = 0;

		Vector2 headDirection2D = new(headDirection.X, headDirection.Y);
		MathLib.VectorNormalize(ref headDirection2D);
		headDirection.X = headDirection2D.X;
		headDirection.Y = headDirection2D.Y;

		return headDirection;
	}

	public override Vector3 HeadDirection3D() {
		UpdateLatchedValues();

		return LatchedHeadDirection;
	}

	public BaseEntity? GetLooktarget() => LookTarget.Get();
	public virtual void OnNewLookTarget() { }

	public virtual bool HasActiveLookTargets() => LookQueue.Count != 0;

	public virtual void ClearLookTarget(BaseEntity? target) {
		int index = LookQueue.Find(target);
		if (index != -1)
			LookQueue.RemoveAt(index);

		index = RandomLookQueue.Find(target);
		if (index != -1) {
			RandomLookQueue.RemoveAt(index);

			NextRandomLookTime = gpGlobals.CurTime + 1.0;
			for (int i = 0; i < RandomLookQueue.Count; i++) {
				if (RandomLookQueue[i].EndTime > NextRandomLookTime)
					NextRandomLookTime = RandomLookQueue[i].EndTime + 0.4f;
			}
		}
	}

	public virtual float PickLookTarget(bool excludePlayers = false, float minTime = 1.5f, float maxTime = 2.5f) => PickLookTarget(RandomLookQueue, excludePlayers, minTime, maxTime);

	public virtual float PickLookTarget(AI_InterestTarget queue, bool excludePlayers = false, float minTime = 1.5f, float maxTime = 2.5f) {
		AILookTargetArgs args = new();

		args.TargetPosition = vec3_invalid;
		args.Duration = RandomFloat(minTime, maxTime);
		args.Influence = RandomFloat(0.3f, 0.5f);
		args.Ramp = RandomFloat(0.2f, 0.4f);
		args.ExcludePlayers = excludePlayers;
		args.Queue = queue;

		MakeRandomLookTarget(args, minTime, maxTime);

		OnSelectedLookTarget(args);

		if (args.Target.Get() != null) {
			Assert(args.TargetPosition == vec3_invalid);
			queue.Add(args.Target.Get(), args.Influence, args.Duration, args.Ramp);
		}
		else {
			Assert(args.TargetPosition != vec3_invalid);
			queue.Add(args.TargetPosition, args.Influence, args.Duration, args.Ramp);
		}

		return args.Duration;
	}

	public virtual void MakeRandomLookTarget(AILookTargetArgs args, float minTime, float maxTime) {
		GetVectors(out Vector3 forward, out Vector3 right, out Vector3 up);

		args.TargetPosition = EyePosition() + forward * 128 + right * RandomFloat(-32, 32) + up * RandomFloat(-16, 16);

		args.Duration = RandomFloat(minTime, maxTime);
		args.Influence = 0.01f;
		args.Ramp = RandomFloat(0.8f, 2.8f);
	}

	public virtual void OnSelectedLookTarget(AILookTargetArgs args) { }

	public virtual void ExpireCurrentRandomLookTarget() => NextRandomLookTime = gpGlobals.CurTime - 0.1f;

	public override void MaintainLookTargets(TimeUnit_t interval) {
		int i;

		if (ExpressionScene != null && ExpressionSceneEnt.Get() == null)
			throw new NotImplementedException();

		GoalSpineYaw = GoalSpineYaw * 0.8f;
		GoalBodyYaw = GoalBodyYaw * 0.8f;
		GoalHeadCorrection = GoalHeadCorrection * 0.8f;

		SetAccumulatedYawAndUpdate();
		ProcessSceneEvents();
		MaintainTurnActivity();
		DoBodyLean();
		UpdateBodyControl();
		InvalidateBoneCache();

		Vector3 eyePosition = EyePosition();

		Set(ParameterGestureHeight, Get(FlexweightGestureUpDown));
		Set(ParameterGestureWidth, Get(FlexweightGestureRightLeft));

		Vector3 head = HeadDirection3D();
		float headInfluence = 0.0f;

		LookQueue.Cleanup();

		RandomLookQueue.Cleanup();

		SyntheticLookQueue.Cleanup();

		if (LookQueue.Count != 0 || SyntheticLookQueue.Count != 0) {
			for (i = 0; i < RandomLookQueue.Count; i++) {
				if (gpGlobals.CurTime < RandomLookQueue[i].EndTime - RandomLookQueue[i].Ramp - 0.2f)
					RandomLookQueue[i].EndTime = gpGlobals.CurTime + RandomLookQueue[i].Ramp + 0.2f;
			}
			NextRandomLookTime = gpGlobals.CurTime + 1.0;
		}
		else if (gpGlobals.CurTime >= NextRandomLookTime && GetState() != NPCState.Script)
			NextRandomLookTime = gpGlobals.CurTime + PickLookTarget(RandomLookQueue) - 0.4f;

		if (!HasCondition((int)SCOND_t.COND_IN_PVS))
			return;

		if (NextRandomExpressionTime != 0 && gpGlobals.CurTime > NextRandomExpressionTime) {
			ClearExpression();

			PlayExpressionForState(GetState());
		}

		List<AI_InterestTargetEntry> active = ActiveLookTargets;
		active.Clear();
		for (i = 0; i < RandomLookQueue.Count; i++)
			active.Add(RandomLookQueue[i]);
		for (i = 0; i < LookQueue.Count; i++)
			active.Add(LookQueue[i]);
		for (i = 0; i < SyntheticLookQueue.Count; i++)
			active.Add(SyntheticLookQueue[i]);

		bool validHeadTarget = false;
		for (i = 0; i < active.Count; i++) {
			Vector3 dir;

			float interest = active[i].Interest();

			if (active[i].IsThis(this)) {
				int forward = LookupAttachment("forward");
				if (forward > 0)
					GetAttachment(forward, out _, out dir, out _, out _);
				else
					dir = HeadDirection3D();
			}
			else {
				dir = active[i].GetPosition() - eyePosition;
				MathLib.VectorNormalize(ref dir);
				interest = interest * HeadTargetValidity(active[i].GetPosition());
			}

			if (interest > 0.0f) {
				if (headInfluence == 0.0f) {
					head = dir;
					headInfluence = interest;
				}
				else {
					headInfluence = headInfluence * (1 - interest) + interest;
					float w = interest / headInfluence;
					head = head * (1 - w) + dir * w;
				}

				validHeadTarget = true;
			}
		}

		Assert(headInfluence <= 1.0f);

		if (validHeadTarget) {
			UpdateHeadControl(eyePosition + head * 100, headInfluence);
			GoalHeadDirection = head;
			GoalHeadInfluence = headInfluence;
		}
		else {
			GoalHeadDirection = GoalHeadDirection * 0.8f + head * 0.2f;

			GoalHeadInfluence = Math.Max(GoalHeadInfluence - 0.2f, 0);

			MathLib.VectorNormalize(ref GoalHeadDirection);
			UpdateHeadControl(eyePosition + GoalHeadDirection * 100, GoalHeadInfluence);
		}

		bool foundTarget = false;
		EHANDLE target = new();

		for (i = active.Count - 1; i >= 0; i--) {
			if (active[i].IsThis(this)) {
				foundTarget = true;
				target.Set(this);
				SetViewtarget(eyePosition + HeadDirection3D() * 100);
				break;
			}
			else {
				if (ValidEyeTarget(active[i].GetPosition())) {
					foundTarget = true;
					target = active[i].Target;
					SetViewtarget(active[i].GetPosition());
					break;
				}
			}
		}

		if (LookTarget != target) {
			Blinktime -= 0.5f;
			LookTarget = target;

			if ((DebugOverlays & DebugOverlayBits.NPCSelected) != 0 && ai_debug_looktargets.GetInt() == 2 && LookTarget.Get() != null) {
				if (LookTarget.Get() != this) {
					BaseEntity lookTarget = LookTarget.Get()!;
					Vector3 vecEyePos = lookTarget.EyePosition();
					DebugOverlay.Box(vecEyePos, -new Vector3(5, 5, 5), new Vector3(5, 5, 5), 0, 255, 0, 255, 20);
					DebugOverlay.Line(EyePosition(), vecEyePos, 0, 255, 0, true, 20);
					DebugOverlay.Text(vecEyePos, $"{lookTarget.GetClassname()} ({lookTarget.GetDebugName()})", false, 20);
				}
			}

			OnNewLookTarget();
		}

		if (!foundTarget && !ValidEyeTarget(GetViewtarget())) {
			MathLib.VectorVectors(HeadDirection3D(), out Vector3 right, out Vector3 up);
			SetViewtarget(EyePosition() + HeadDirection3D() * 128 + right * RandomFloat(-32, 32) + up * RandomFloat(-16, 16));
		}

		if (LookTarget.Get() != null) {
			BaseEntity lookTarget = LookTarget.Get()!;
			Vector3 absVel = lookTarget.GetAbsVelocity();
			BaseEntity? ground = lookTarget.GetGroundEntity();
			if (ground != null && ground.GetMoveType() == Source.MoveType.Push)
				absVel = absVel + ground.GetAbsVelocity();

			if (!MathLib.VectorCompare(absVel, vec3_origin)) {
				Vector3 viewTarget = GetViewtarget();

				viewTarget += absVel * (float)interval;

				SetViewtarget(viewTarget);
			}
		}

		if (Blinktime < gpGlobals.CurTime) {
			Blink();
			Blinktime = gpGlobals.CurTime + RandomFloat(1.5f, 4.5f);
		}

		if (ai_debug_looktargets.GetInt() == 1 && (DebugOverlays & DebugOverlayBits.NPCSelected) != 0) {
			DebugOverlay.Box(GetViewtarget(), -new Vector3(2, 2, 2), new Vector3(2, 2, 2), 0, 255, 0, 0, 20);
			DebugOverlay.Line(EyePosition(), GetViewtarget(), 0, 255, 0, false, .1f);
		}
	}

	public virtual void PlayExpressionForState(NPCState state) {
		if (ExpressionOverride != null && state != NPCState.Dead) {
			SetExpression(ExpressionOverride);
			return;
		}

		string? expression = SelectRandomExpressionForState(state);
		if (!string.IsNullOrEmpty(expression)) {
			float duration = SetExpression(expression);
			NextRandomExpressionTime = gpGlobals.CurTime + duration;
			return;
		}
		else
			NextRandomExpressionTime = 0;

		switch (state) {
			case NPCState.Idle:
				if (IdleExpression != null)
					SetExpression(IdleExpression);
				break;

			case NPCState.Combat:
				if (CombatExpression != null)
					SetExpression(CombatExpression);
				break;

			case NPCState.Alert:
				if (AlertExpression != null)
					SetExpression(AlertExpression);
				break;

			case NPCState.PlayDead:
			case NPCState.Dead:
				if (DeathExpression != null)
					SetExpression(DeathExpression);
				break;
		}
	}

	public virtual string? SelectRandomExpressionForState(NPCState state) => null;

	public override void OnStateChange(NPCState oldState, NPCState newState) {
		PlayExpressionForState(newState);

		base.OnStateChange(oldState, newState);
	}

	public float SetExpression(string? expressionScene) {
		if (string.IsNullOrEmpty(expressionScene)) {
			ClearExpression();
			return 0;
		}

		if (ExpressionScene != null && stricmp(ExpressionScene, expressionScene) == 0)
			return 0;

		if (ExpressionSceneEnt.Get() != null)
			StopScriptedScene(this, ExpressionSceneEnt);

		if (ai_debug_expressions.GetInt() != 0)
			Msg($"{GetClassname()} ({GetDebugName()}) set expression to: {expressionScene}\n");

		ExpressionScene = null;
		float duration = InstancedScriptedScene(this, expressionScene, ref ExpressionSceneEnt, 0.0f, true);

		ExpressionEndTime = gpGlobals.CurTime + duration;

		if (ExpressionSceneEnt.Get() != null)
			ExpressionScene = expressionScene;

		return duration;
	}

	public void ClearExpression() {
		if (ExpressionSceneEnt.Get() != null)
			StopScriptedScene(this, ExpressionSceneEnt);
		ExpressionScene = null;
	}

	public string GetExpression() => ExpressionScene ?? "";

	public override void AddLookTarget(BaseEntity? target, float importance, float duration, float ramp = 0.0f) => LookQueue.Add(target, importance, duration, ramp);
	public override void AddLookTarget(in Vector3 position, float importance, float duration, float ramp = 0.0f) => LookQueue.Add(position, importance, duration, ramp);

	static readonly ConVar scene_showfaceto = new("scene_showfaceto", "0", FCvar.Archive, "When playing back, show the directions of faceto events.");

	public override bool StartSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev, ChoreoActor actor, BaseEntity? target) {
		Assert(info != null);
		Assert(info!.Scene != null);
		Assert(info.Event != null);

		switch (info.Event!.GetType()) {
			case EventType.Face:
				return base.StartSceneEvent(info, scene, ev, actor, target);

			case EventType.Generic: {
					if (stricmp(ev.GetParameters(), "AI_BLINK") == 0) {
						info.Type = SceneAIEvent.Blink;
						Blink();
						TimeUnit_t duration = ev.GetEndTime() - scene.GetTime();
						Blinktime = gpGlobals.CurTime + Math.Max(duration, RandomFloat(1.5f, 4.5f));
					}
					else if (stricmp(ev.GetParameters(), "AI_HOLSTER") == 0) {
						info.Type = SceneAIEvent.Holster;
						info.Layer = HolsterWeapon();
						return true;
					}
					else if (stricmp(ev.GetParameters(), "AI_UNHOLSTER") == 0) {
						info.Type = SceneAIEvent.Unholster;
						info.Layer = UnholsterWeapon();
						return true;
					}
					else if (stricmp(ev.GetParameters(), "AI_AIM") == 0) {
						info.Type = SceneAIEvent.Aim;
						info.Target.Set(target);
					}
					else if (stricmp(ev.GetParameters(), "AI_RANDOMLOOK") == 0) {
						info.Type = SceneAIEvent.RandomLook;
						info.Next = 0.0;
					}
					else if (stricmp(ev.GetParameters(), "AI_RANDOMFACEFLEX") == 0) {
						info.Type = SceneAIEvent.RandomFaceFlex;
						info.Next = 0.0;
						info.InitWeight(this);
					}
					else if (stricmp(ev.GetParameters(), "AI_RANDOMHEADFLEX") == 0) {
						info.Type = SceneAIEvent.RandomHeadFlex;
						info.Next = 0.0;
					}
					else if (stricmp(ev.GetParameters(), "AI_IGNORECOLLISION") == 0) {
						BaseEntity? namedTarget = FindNamedEntity(ev.GetParameters2());

						if (namedTarget != null) {
							info.Type = SceneAIEvent.IgnoreCollision;
							info.Target.Set(namedTarget);
							TimeUnit_t remaining = ev.GetEndTime() - scene.GetTime();
							NPCPhysics_CreateSolver(this, namedTarget, true, remaining);
							info.Next = gpGlobals.CurTime + remaining;
							return true;
						}
						else {
							Warning($"CSceneEntity {scene.GetFilename()} unable to find actor named \"{ev.GetParameters2()}\"\n");
							return false;
						}
					}
					else if (stricmp(ev.GetParameters(), "AI_DISABLEAI") == 0)
						info.Type = SceneAIEvent.DisableAI;
					else
						return base.StartSceneEvent(info, scene, ev, actor, target);
					return true;
				}

			default:
				return base.StartSceneEvent(info, scene, ev, actor, target);
		}
	}

	public override bool ProcessSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev) {
		Assert(info != null);
		Assert(info!.Scene != null);
		Assert(info.Event != null);

		switch (info.Event!.GetType()) {
			case EventType.Face: {
					if (info.Target.Get() == null)
						return false;

					bool inScene = false;

					if (!ev.IsLockBodyFacing())
						inScene = EnterSceneSequence(scene, ev, true);

					if (!info.Started) {
						info.InitialYaw = GetLocalAngles().Y;
						info.TargetYaw = info.InitialYaw;
						info.FacingYaw = info.InitialYaw;
						if (IsMoving())
							info.Weight = 1.0f;
						else
							info.Weight = 0.0f;
					}

					if (info.Target.Get() == this)
						return true;

					if (!inScene || info.IsMoving != IsMoving())
						info.InitialYaw = GetLocalAngles().Y;
					info.IsMoving = IsMoving();

					TimeUnit_t time = scene.GetTime();
					if (time < ev.GetStartTime())
						time = ev.GetStartTime();
					else if (time > ev.GetEndTime() - 0.1f)
						time = ev.GetEndTime() - 0.1f;
					float intensity = ev.GetIntensity(time);

					TimeUnit_t duration = scene.GetTime() - ev.GetStartTime();
					float maxIntensity = duration < 0.5 ? MathLib.SimpleSpline((float)(duration / 0.5)) : 1.0f;
					intensity = Math.Clamp(intensity, 0.0f, maxIntensity);

					if (inScene && info.IsMoving)
						info.InitialYaw = GetLocalAngles().Y;

					if (!ev.IsLockBodyFacing()) {
						if (!info.IsMoving && inScene)
							AccumulateIdealYaw(info.FacingYaw, intensity);
					}

					float diff;
					float dir;
					float spineYaw;
					float bodyYaw;

					diff = Util.AngleDiff(info.TargetYaw, GetLocalAngles().Y);
					if (diff < 0) {
						diff = -diff;
						dir = -1;
					}
					else
						dir = 1;
					spineYaw = Math.Min(diff, 30);
					bodyYaw = Math.Min(diff - spineYaw, 30);
					GoalSpineYaw = (float)(GoalSpineYaw * (1.0 - intensity) + intensity * spineYaw * dir);
					GoalBodyYaw = (float)(GoalBodyYaw * (1.0 - intensity) + intensity * bodyYaw * dir);

					AI_BaseNPC? goalNpc = info.Target.Get()!.MyNPCPointer();

					float goalYaw = GetLocalAngles().Y;

					if (goalNpc != null)
						goalYaw = CalcIdealYaw(goalNpc.FacingPosition());
					else
						goalYaw = CalcIdealYaw(info.Target.Get()!.EyePosition());

					if (developer.GetInt() > 0 && scene_showfaceto.GetBool())
						DebugOverlay.YawArrow(GetAbsOrigin() + new Vector3(0, 0, 1), goalYaw, 8 + 32 * intensity, 8, 255, 255, 255, 0, true, 0.12f);

					diff = Util.AngleDiff(goalYaw, info.InitialYaw) * intensity;
					dir = 1.0f;

					info.TargetYaw = Util.AngleMod(info.InitialYaw + diff);

					if (diff < 0) {
						diff = -diff;
						dir = -1;
					}

					float spineintensity = (float)(1.0 - Math.Max(0.0, (intensity - 0.5) / 0.5));
					if (!inScene || ev.IsLockBodyFacing())
						spineintensity = 1.0f;

					spineYaw = Math.Min(diff * spineintensity, 30);
					bodyYaw = Math.Min(diff * spineintensity - spineYaw, 30);
					info.FacingYaw = info.InitialYaw + (diff - bodyYaw - spineYaw) * dir;

					if (!ev.IsLockBodyFacing())
						AddFacingTarget(info.Target.Get(), intensity, 0.2f);
					return true;
				}
			case EventType.Generic: {
					switch (info.Type) {
						case SceneAIEvent.Blink: {
								TimeUnit_t duration = ev.GetEndTime() - scene.GetTime();
								Blinktime = Math.Max(Blinktime, gpGlobals.CurTime + duration);
							}
							return true;
						case SceneAIEvent.Holster:
							return true;
						case SceneAIEvent.Unholster:
							return true;
						case SceneAIEvent.Aim: {
								if (info.Target.Get() != null) {
									Vector3 aimTargetLoc = info.Target.Get()!.EyePosition();
									Vector3 aimDir = aimTargetLoc - EyePosition();

									MathLib.VectorNormalize(ref aimDir);
									SetAim(aimDir);
								}
							}
							return true;
						case SceneAIEvent.RandomLook: {
								if (info.Next < gpGlobals.CurTime) {
									info.Next = gpGlobals.CurTime + PickLookTarget(SyntheticLookQueue) - 0.4;
									if (SyntheticLookQueue.Count > 0) {
										TimeUnit_t duration = ev.GetEndTime() - scene.GetTime();
										int i = SyntheticLookQueue.Count - 1;
										SyntheticLookQueue[i].EndTime = Math.Min(SyntheticLookQueue[i].EndTime, gpGlobals.CurTime + duration);
										SyntheticLookQueue[i].InterestValue = 0.1f;
									}
								}
							}
							return true;
						case SceneAIEvent.RandomFaceFlex:
							return RandomFaceFlex(info, scene, ev);
						case SceneAIEvent.RandomHeadFlex:
							return true;
						case SceneAIEvent.IgnoreCollision:
							if (info.Target.Get() != null && info.Next < gpGlobals.CurTime) {
								TimeUnit_t remaining = ev.GetEndTime() - scene.GetTime();
								NPCPhysics_CreateSolver(this, info.Target.Get(), true, remaining);
								info.Next = gpGlobals.CurTime + remaining;
							}

							return true;
						case SceneAIEvent.DisableAI:
							if (!(GetState() == NPCState.Script || IsCurSchedule(SCHED_SCENE_GENERIC)))
								EnterSceneSequence(scene, ev);
							return true;
						default:
							return false;
					}
				}
			default:
				return base.ProcessSceneEvent(info, scene, ev);
		}
	}

	bool RandomFaceFlex(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev) {
		if (info.Next < gpGlobals.CurTime) {
			FlexSettingHdr? settinghdr = FindSceneFile(ev.GetParameters2());
			if (settinghdr == null)
				settinghdr = FindSceneFile("random");
			if (settinghdr != null) {
				info.Next = gpGlobals.CurTime + RandomFloat(0.3f, 0.5f) * (30.0 / settinghdr.NumFlexSettings);

				FlexSetting setting = settinghdr.Setting(RandomInt(0, settinghdr.NumFlexSettings - 1));

				int truecount = setting.PSetting(0, out ReadOnlySpan<FlexSettingWeight> weights);

				int i;
				for (i = 0; i < truecount; i++) {
					LocalFlexController index = FlexControllerLocalToGlobal(settinghdr, weights[i].Key);

					FlexTarget[(int)index] = weights[i].Weight;
				}
			}
			else
				return false;
		}

		float intensity = info.UpdateWeight(this) * ev.GetIntensity(scene.GetTime());

		for (LocalFlexController i = 0; i < GetNumFlexControllers(); i++) {
			float weight = GetFlexWeight(i);

			if (weight != FlexTarget[(int)i]) {
				float delta = (FlexTarget[(int)i] - weight) / RandomFloat(2.0f, 4.0f);
				weight = weight + delta * intensity;
			}
			weight = Math.Clamp(weight, 0.0f, 1.0f);
			SetFlexWeight(i, weight);
		}

		return true;
	}

	public override bool ClearSceneEvent(SceneEventInfo info, bool fastKill, bool canceled) {
		Assert(info != null);
		Assert(info!.Scene != null);
		Assert(info.Event != null);

		switch (info.Event!.GetType()) {
			case EventType.Face:
				return base.ClearSceneEvent(info, fastKill, canceled);
			default:
				return base.ClearSceneEvent(info, fastKill, canceled);
		}
	}

	public override bool CheckSceneEventCompletion(SceneEventInfo info, TimeUnit_t currenttime, ChoreoScene scene, ChoreoEvent ev) {
		Assert(info != null);
		Assert(info!.Scene != null);
		Assert(info.Event != null);

		switch (ev.GetType()) {
			case EventType.Generic: {
					switch (info.Type) {
						case SceneAIEvent.Holster:
						case SceneAIEvent.Unholster: {
								if (info.Layer == -1)
									return true;

								TimeUnit_t preload = ev.GetEndTime() - currenttime;
								if (preload < 0)
									return true;

								TimeUnit_t t = (1.0 - GetLayerCycle(info.Layer)) * SequenceDuration(GetLayerSequence(info.Layer));

								return t <= preload;
							}
					}
				}
				break;
		}

		return base.CheckSceneEventCompletion(info, currenttime, scene, ev);
	}

	public void AccumulateIdealYaw(float yaw, float intensity) {
		float diff = Util.AngleDiff(yaw, GetLocalAngles().Y);
		AccumYawDelta += diff * intensity;
		AccumYawScale += intensity;
	}

	public float AccumYawDelta;
	public float AccumYawScale;

	HumanoidLatched LatchedPositions;
	Vector3 LatchedEyeOrigin;
	Vector3 LatchedEyeDirection;
	Vector3 LatchedHeadDirection;

	Vector3 GoalHeadDirection;
	float GoalHeadInfluence;

	float GoalSpineYaw;
	float GoalBodyYaw;
	Vector3 GoalHeadCorrection;

	TimeUnit_t Blinktime;
	EHANDLE LookTarget = new();
	readonly AI_InterestTarget LookQueue = new();
	readonly AI_InterestTarget SyntheticLookQueue = new();

	readonly AI_InterestTarget RandomLookQueue = new();
	TimeUnit_t NextRandomLookTime;

	readonly List<AI_InterestTargetEntry> ActiveLookTargets = [];

	string? ExpressionScene;
	EHANDLE ExpressionSceneEnt = new();
	TimeUnit_t NextRandomExpressionTime;

	readonly float[] FlexTarget = new float[64];

	string? ExpressionOverride;

	protected TimeUnit_t ExpressionEndTime;

	protected string? IdleExpression;
	protected string? AlertExpression;
	protected string? CombatExpression;
	protected string? DeathExpression;

	PoseParameter_t ParameterBodyYaw;
	PoseParameter_t ParameterSpineYaw;
	PoseParameter_t ParameterNeckTrans;
	PoseParameter_t ParameterHeadYaw;
	PoseParameter_t ParameterHeadPitch;
	PoseParameter_t ParameterHeadRoll;

	FlexWeight_t FlexweightBodyRightLeft;
	FlexWeight_t FlexweightChestRightLeft;
	FlexWeight_t FlexweightHeadForwardBack;
	FlexWeight_t FlexweightHeadRightLeft;
	FlexWeight_t FlexweightHeadUpDown;
	FlexWeight_t FlexweightHeadTilt;

	PoseParameter_t ParameterGestureHeight;
	PoseParameter_t ParameterGestureWidth;
	FlexWeight_t FlexweightGestureUpDown;
	FlexWeight_t FlexweightGestureRightLeft;
}
