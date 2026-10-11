#if CLIENT_DLL || GAME_DLL
global using static Game.
#if CLIENT_DLL
Client
#else
Server
#endif
	.BaseEntityConstants;



#if CLIENT_DLL
global using BaseEntity = Game.Client.C_BaseEntity;

using Source.Common;

namespace Game.Client;
#else
global using BaseEntity = Game.Server.BaseEntity;

using Source.Common;

namespace Game.Server;
#endif

using CommunityToolkit.HighPerformance;

using Source;

using System.Numerics;

using Source.Common.Mathematics;

using Game.Shared;


using Table =
#if CLIENT_DLL
	RecvTable;
#else
	SendTable;
#endif

using Class =
#if CLIENT_DLL
	ClientClass;
#else
	ServerClass;
#endif

using FIELD = Source.FIELD<BaseEntity>;

using System.Runtime.CompilerServices;

using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Physics;

using System.Text;

public static class BaseEntityConstants
{
#if HL2_EPISODIC
	public static readonly ConVar hl2_episodic = new("hl2_episodic", "1", FCvar.Replicated);
#else
	public static readonly ConVar hl2_episodic = new("hl2_episodic", "0", FCvar.Replicated);
#endif

	public const int NUM_PARENTATTACHMENT_BITS = 8; // < gmod increased 6 . 8
	public const int VPHYSICS_MAX_OBJECT_LIST_COUNT = 1024;
}

[Flags]
public enum EntityCapabilities : uint
{
	MustSpawn = 0x00000001,
	AcrossTransition = 0x00000002,
	ForceTransition = 0x00000004,
	NotifyOnTransition = 0x00000008,
	ImpulseUse = 0x00000010,
	ContinuousUse = 0x00000020,
	OnOffUse = 0x00000040,
	DirectionalUse = 0x00000080,
	UseOnGround = 0x00000100,
	UseInRadius = 0x00000200,
	SaveNonNetworkable = 0x00000400,
	Master = 0x10000000,
	WCEditPosition = 0x40000000,
	DontSave = 0x80000000
}

public enum InvalidatePhysicsBits
{
	PositionChanged = 0x1,
	AnglesChanged = 0x2,
	VelocityChanged = 0x4,
	AnimationChanged = 0x8,
}

public partial class
#if CLIENT_DLL
	C_BaseEntity
#else
	BaseEntity
#endif
{
	public const int BASEENTITY_MSG_REMOVE_DECALS = 1;

	// TODO FIXME REVIEW: SHOULD THIS ACTUALLY GO HERE?
	public static Table DT_ScriptedEntity = new(nameof(DT_ScriptedEntity), [
#if CLIENT_DLL
		RecvPropString(FIELD.OF(nameof(ScriptName)))
#elif GAME_DLL
		SendPropString(FIELD.OF(nameof(ScriptName)))
#endif
	]);
	[NetworkName("m_strScriptName")]
	public InlineArrayMaxPath<char> ScriptName;

	public bool IsAnimatedEveryTick() => AnimatedEveryTick;
	public bool IsSimulatedEveryTick() => SimulatedEveryTick;

	static int FireBullets__tracerCount;
	public virtual void FireBullets(in FireBulletsInfo info) {
		// todo
	}
	public virtual Vector3 EyePosition() => GetAbsOrigin() + GetViewOffset();
	public virtual ref readonly QAngle EyeAngles() => ref GetAbsAngles();
	public void InvalidatePhysicsRecursive(InvalidatePhysicsBits changeFlags) {
		EFL dirtyFlags = 0;

		if ((changeFlags & InvalidatePhysicsBits.VelocityChanged) != 0)
			dirtyFlags |= EFL.DirtyAbsVelocity;

		if ((changeFlags & InvalidatePhysicsBits.PositionChanged) != 0) {
			dirtyFlags |= EFL.DirtyAbsTransform;

#if !CLIENT_DLL
			NetworkProp().MarkPVSInformationDirty();
#endif

			CollisionProp().MarkPartitionHandleDirty();
		}

		// NOTE: This has to be done after velocity + position because we change the
		// changeFlags for child entities. An angle change also requires recomputing position.
		if ((changeFlags & InvalidatePhysicsBits.AnglesChanged) != 0) {
			dirtyFlags |= EFL.DirtyAbsTransform;
			if (CollisionProp().DoesRotationInvalidateSurroundingBox())
				// NOTE: This will handle the KD-tree, surrounding bounds, PVS
				// render-to-texture shadow, shadow projection, and client leaf dirty
				CollisionProp().MarkSurroundingBoundsDirty();
			else {
#if CLIENT_DLL
				MarkRenderHandleDirty();
				g_ClientShadowMgr.AddToDirtyShadowList(this);
				g_ClientShadowMgr.MarkRenderToTextureShadowDirty(GetShadowHandle());
#endif
			}
			changeFlags |= InvalidatePhysicsBits.PositionChanged | InvalidatePhysicsBits.VelocityChanged;
		}

		AddEFlags(dirtyFlags);

		bool onlyDueToAttachment = false;
		if ((changeFlags & InvalidatePhysicsBits.AnimationChanged) != 0) {
#if CLIENT_DLL
			g_ClientShadowMgr.MarkRenderToTextureShadowDirty(GetShadowHandle());
#endif

			if ((changeFlags & (InvalidatePhysicsBits.PositionChanged | InvalidatePhysicsBits.VelocityChanged | InvalidatePhysicsBits.AnglesChanged)) == 0)
				onlyDueToAttachment = true;

			changeFlags = InvalidatePhysicsBits.PositionChanged | InvalidatePhysicsBits.AnglesChanged | InvalidatePhysicsBits.VelocityChanged;
		}

		for (BaseEntity? child = FirstMoveChild(); child != null; child = child.NextMovePeer()) {
			if (onlyDueToAttachment) {
#if CLIENT_DLL
				if ((child.ParentAttachment == 0) && !child.IsFollowingEntity())
					continue;
#else
				if (child.ParentAttachment == 0)
					continue;
#endif
			}
			child.InvalidatePhysicsRecursive(changeFlags);
		}
	}


	public static bool IsSimulatingOnAlternateTicks() => false; // TODO

	public bool IsAlive() => LifeState == (int)Source.LifeState.Alive;

	[NetworkName("m_bIsPlayerSimulated")]
	protected bool b_IsPlayerSimulated;
	public bool IsPlayerSimulated() => b_IsPlayerSimulated;

	public void AddFlag(EntityFlags flag) => flags |= (int)flag;
	public void RemoveFlag(EntityFlags flag) => flags &= (int)~flag;
	public void ClearFlags() => flags = 0;
	public void ToggleFlag(EntityFlags flag) => flags ^= (int)flag;

	public int GetFirstThinkTick() {
		int minTick = TICK_NEVER_THINK;

		if (NextThinkTick > 0)
			minTick = (int)NextThinkTick;

		for (int i = 0; i < ThinkFunctions.Count; i++) {
			int next = (int)ThinkFunctions[i].NextThinkTick;
			if (next > 0) {
				if (next < minTick || minTick == TICK_NEVER_THINK)
					minTick = next;
			}
		}

		return minTick;
	}

	public virtual void ModifyEmitSoundParams(ref EmitSound_t parms) {
#if CLIENT_DLL
		if (g_pGameRules != null)
			parms.SoundName = g_pGameRules.TranslateEffectForVisionFilter("sounds", parms.SoundName);
#endif
	}

	public void DispatchTraceAttack(in TakeDamageInfo info, in Vector3 dir, ref Trace ptr, ref DmgAccumulator accumulator) {

	}
	public void DispatchTraceAttack(in TakeDamageInfo info, in Vector3 dir, ref Trace ptr)
		=> DispatchTraceAttack(in info, in dir, ref ptr, ref Unsafe.NullRef<DmgAccumulator>());

	public long GetNextThinkTick(ReadOnlySpan<char> context = default) {
		// Are we currently in a think function with a context?
		int index = 0;
		if (context.IsEmpty) {
#if DEBUG
			if (CurrentThinkContext != NO_THINK_CONTEXT)
				Msg($"Warning: Getting base nextthink time within think context {ThinkFunctions[CurrentThinkContext].Context}\n");
#endif

			if (NextThinkTick == TICK_NEVER_THINK)
				return TICK_NEVER_THINK;

			// Old system
			return NextThinkTick;
		}
		else
			// Find the think function in our list
			index = GetIndexForThinkContext(context);

		if (index == NO_THINK_CONTEXT)
			return TICK_NEVER_THINK;

		ref ThinkFunc tf = ref ThinkFunctions.AsSpan()[index];

		if (tf.NextThinkTick == TICK_NEVER_THINK)
			return TICK_NEVER_THINK;

		return tf.NextThinkTick;
	}

	public void SetLastThink(int contextIndex, TimeUnit_t thinkTime) {
		int thinkTick = (thinkTime == TICK_NEVER_THINK) ? TICK_NEVER_THINK : TIME_TO_TICKS(thinkTime);

		if (contextIndex < 0)
			LastThinkTick = thinkTick;
		else
			ThinkFunctions.AsSpan()[contextIndex].LastThinkTick = thinkTick;
	}

	public TimeUnit_t GetNextThink(int contextIndex) {
		if (contextIndex < 0)
			return NextThinkTick * TICK_INTERVAL;

		return ThinkFunctions.AsSpan()[contextIndex].NextThinkTick * TICK_INTERVAL;
	}

	public long GetNextThinkTick(int contextIndex) {
		if (contextIndex < 0)
			return NextThinkTick;

		return ThinkFunctions.AsSpan()[contextIndex].NextThinkTick;
	}

	public TimeUnit_t GetLastThink(ReadOnlySpan<char> context) {
		// Are we currently in a think function with a context?
		int index = 0;
		if (context.IsEmpty) {
#if DEBUG
			if (CurrentThinkContext != NO_THINK_CONTEXT)
				Msg($"Warning: Getting base lastthink time within think context {ThinkFunctions[CurrentThinkContext].Context}\n");
#endif
			// Old system
			return LastThinkTick * TICK_INTERVAL;
		}
		else
			// Find the think function in our list
			index = GetIndexForThinkContext(context);

		return ThinkFunctions.AsSpan()[index].LastThinkTick * TICK_INTERVAL;
	}

	public long GetLastThinkTick(ReadOnlySpan<char> context) {
		// Are we currently in a think function with a context?
		int index = 0;
		if (context.IsEmpty) {
#if DEBUG
			if (CurrentThinkContext != NO_THINK_CONTEXT)
				Msg($"Warning: Getting base lastthink time within think context {ThinkFunctions[CurrentThinkContext].Context}\n");
#endif
			// Old system
			return LastThinkTick;
		}
		else
			// Find the think function in our list
			index = GetIndexForThinkContext(context);

		return ThinkFunctions.AsSpan()[index].LastThinkTick;
	}

	public bool WillThink() {
		if (NextThinkTick > 0)
			return true;

		for (int i = 0; i < ThinkFunctions.Count; i++)
			if (ThinkFunctions[i].NextThinkTick > 0)
				return true;

		return false;
	}

	public virtual bool ShouldCollide(CollisionGroup collisionGroup, Contents contentsMask) {
		if (CollisionGroup == (int)Source.CollisionGroup.Debris) {
			if ((contentsMask & Contents.Debris) == 0)
				return false;
		}
		return true;
	}

	public virtual bool TestCollision(in Ray ray, Contents mask, ref Trace trace) {
		return false;
	}

	public virtual bool TestHitboxes(in Ray ray, Contents contentsMask, ref Trace tr) {
		return false;
	}

	public BaseEntity GetRootMoveParent() {
		BaseEntity? entity = this;
		BaseEntity? parent = this.GetMoveParent();
		while (parent != null) {
			entity = parent;
			parent = entity.GetMoveParent();
		}
		return entity;
	}

	public void VPhysicsInitShadow(bool allowPhysicsMovement, bool allowPhysicsRotation, ref Solid solid) {

	}
	public void VPhysicsInitShadow(bool allowPhysicsMovement, bool allowPhysicsRotation) => VPhysicsInitShadow(allowPhysicsMovement, allowPhysicsRotation, ref Unsafe.NullRef<Solid>());
	public void VPhysicsDestroyObject() {
		if (PhysicsObject != null) {
#if !CLIENT_DLL
			PhysRemoveShadow(this);
#endif
			PhysDestroyObject(PhysicsObject, this);
			PhysicsObject = null;
		}
	}

	public void ApplyAbsVelocityImpulse(in Vector3 impulse) {
		if (impulse != vec3_origin) {
			Vector3 vecImpulse = impulse;

			// Safety check against receive a huge impulse, which can explode physics
			switch (CheckEntityVelocity(ref vecImpulse)) {
				case -1:
					Warning($"Discarding ApplyAbsVelocityImpulse({impulse.X},{impulse.Y},{impulse.Z}) on {GetDebugName()}\n");
					Assert(false);
					return;
				case 0:
					if (CheckEmitReasonablePhysicsSpew()) {
						Warning($"Clamping ApplyAbsVelocityImpulse({impulse.X},{impulse.Y},{impulse.Z}) on {GetDebugName()}\n");
					}
					break;
			}

			if (GetMoveType() == Source.MoveType.VPhysics)
				VPhysicsGetObject()!.AddVelocity(in vecImpulse, default);
			else {
				// NOTE: Have to use GetAbsVelocity here to ensure it's the correct value
				MathLib.VectorAdd(GetAbsVelocity(), vecImpulse, out Vector3 vecResult);
				SetAbsVelocity(vecResult);
			}
		}
	}
	public void ApplyLocalAngularVelocityImpulse(in Vector3 angImpulse) {
		if (angImpulse != vec3_origin) {
			// Safety check against receive a huge impulse, which can explode physics
			if (!IsEntityAngularVelocityReasonable(angImpulse)) {
				Warning($"Bad ApplyLocalAngularVelocityImpulse({angImpulse.X},{angImpulse.Y},{angImpulse.Z}) on {GetDebugName()}\n");
				Assert(false);
				return;
			}

			if (GetMoveType() == Source.MoveType.VPhysics)
				VPhysicsGetObject()!.AddVelocity(default, in angImpulse);
			else {
				MathLib.AngularImpulseToQAngle(angImpulse, out QAngle vecResult);
				MathLib.VectorAdd(GetLocalAngularVelocity(), vecResult, out Vector3 vec3Result);
				SetLocalAngularVelocity(vec3Result);
			}
		}
	}

	public void ApplyLocalVelocityImpulse(in Vector3 impulse) {
		// NOTE: Don't have to use GetVelocity here because local values
		// are always guaranteed to be correct, unlike abs values which may 
		// require recomputation
		if (impulse != vec3_origin) {
			Vector3 vecImpulse = impulse;

			// Safety check against receive a huge impulse, which can explode physics
			switch (CheckEntityVelocity(ref vecImpulse)) {
				case -1:
					Warning($"Discarding ApplyLocalVelocityImpulse({impulse.X},{impulse.Y},{impulse.Z}) on {GetDebugName()}\n");
					Assert(false);
					return;
				case 0:
					if (CheckEmitReasonablePhysicsSpew()) {
						Warning($"Clamping ApplyLocalVelocityImpulse({impulse.X},{impulse.Y},{impulse.Z}) on {GetDebugName()}\n");
					}
					break;
			}

			if (GetMoveType() == Source.MoveType.VPhysics) {
				VPhysicsGetObject()!.LocalToWorld(out Vector3 worldVel, vecImpulse);
				VPhysicsGetObject()!.AddVelocity(in worldVel, default);
			}
			else {
				InvalidatePhysicsRecursive(InvalidatePhysicsBits.VelocityChanged);
				Velocity += vecImpulse;
			}
		}
	}

	public void CheckHasThinkFunction(bool isThinking) {
		if (IsEFlagSet(EFL.NoThinkFunction) && isThinking) {
			RemoveEFlags(EFL.NoThinkFunction);
		}
		else if (!isThinking && !IsEFlagSet(EFL.NoThinkFunction) && !WillThink()) {
			AddEFlags(EFL.NoThinkFunction);
		}

#if !CLIENT_DLL
		SimThinkManager.g_SimThinkManager.EntityChanged(this);
#endif
	}

	public void CheckHasGamePhysicsSimulation() {
		bool isSimulating = WillSimulateGamePhysics();
		if (isSimulating != IsEFlagSet(EFL.NoGamePhysicsSimulation))
			return;

		if (isSimulating)
			RemoveEFlags(EFL.NoGamePhysicsSimulation);
		else
			AddEFlags(EFL.NoGamePhysicsSimulation);

#if !CLIENT_DLL
		SimThinkManager.g_SimThinkManager.EntityChanged(this);
#endif
	}

	public BASEPTR SetThink(Action? a) => ThinkSet(a == null ? null : _ => a(), 0, null);
	public BASEPTR SetContextThink(Action? a, TimeUnit_t b, ReadOnlySpan<char> context) => ThinkSet(a == null ? null : _ => a(), b, context);
	public BASEPTR? ThinkSet(BASEPTR? func, TimeUnit_t thinkTime, ReadOnlySpan<char> context) {
		if (context.IsEmpty) {
			FnThink = func;
			return FnThink;
		}

		int iIndex = GetIndexForThinkContext(context);
		if (iIndex == NO_THINK_CONTEXT)
			iIndex = RegisterThinkContext(context);

		var thinkFns = ThinkFunctions.AsSpan();

		thinkFns[iIndex].Think = func;
		if (thinkTime != 0) {
			int thinkTick = (thinkTime == TICK_NEVER_THINK) ? TICK_NEVER_THINK : TIME_TO_TICKS(thinkTime);
			thinkFns[iIndex].NextThinkTick = thinkTick;
			CheckHasThinkFunction(thinkTick == TICK_NEVER_THINK ? false : true);
		}

		return func;
	}

	private bool WillSimulateGamePhysics() {
		if (!IsPlayer()) {
			MoveType movetype = GetMoveType();

			if (movetype == Source.MoveType.None || movetype == Source.MoveType.VPhysics)
				return false;

#if !CLIENT_DLL
			if (movetype == Source.MoveType.Push && GetMoveDoneTime() <= 0)
				return false;
#endif
		}

		return true;
	}

	public void SetViewOffset(in Vector3 v) => ViewOffset = v;

	public void SetNextThink(int contextIndex, TimeUnit_t thinkTime) {
		int thinkTick = (thinkTime == TICK_NEVER_THINK) ? TICK_NEVER_THINK : TIME_TO_TICKS(thinkTime);

		if (contextIndex < 0)
			SetNextThink(thinkTime);
		else
			ThinkFunctions.AsSpan()[contextIndex].NextThinkTick = thinkTick;

		CheckHasThinkFunction(thinkTick == TICK_NEVER_THINK ? false : true);
	}

	public void SetNextThink(TimeUnit_t thinkTime, ReadOnlySpan<char> context = default) {
		int thinkTick = (thinkTime == TICK_NEVER_THINK) ? TICK_NEVER_THINK : TIME_TO_TICKS(thinkTime);

		// Are we currently in a think function with a context?
		int iIndex = 0;
		if (context.IsEmpty) {
			if (CurrentThinkContext != NO_THINK_CONTEXT) {
				Msg($"Warning: Setting base think function within think context {ThinkFunctions[CurrentThinkContext].Context}\n");
			}
			// Old system
			NextThinkTick = thinkTick;
			CheckHasThinkFunction(thinkTick == TICK_NEVER_THINK ? false : true);
			return;
		}
		else {
			// Find the think function in our list, and if we couldn't find it, register it
			iIndex = GetIndexForThinkContext(context);
			if (iIndex == NO_THINK_CONTEXT) {
				iIndex = RegisterThinkContext(context);
			}
		}

		// Old system
		ThinkFunctions.AsSpan()[iIndex].NextThinkTick = thinkTick;
		CheckHasThinkFunction(thinkTick == TICK_NEVER_THINK ? false : true);
	}

	public virtual Vector3 EarPosition() => EyePosition();

	public int RegisterThinkContext(ReadOnlySpan<char> context) {
		int iIndex = GetIndexForThinkContext(context);
		if (iIndex != NO_THINK_CONTEXT)
			return iIndex;

		// Make a new think func
		ThinkFunc sNewFunc = new();
		sNewFunc.Think = null;
		sNewFunc.NextThinkTick = 0;
		sNewFunc.Context = new string(context);

		// Insert it into our list
		ThinkFunctions.Add(sNewFunc);
		return ThinkFunctions.Count - 1;
	}

	public int GetIndexForThinkContext(ReadOnlySpan<char> context) {
		var thinkFunctions = ThinkFunctions.AsSpan();
		for (int i = 0; i < thinkFunctions.Length; i++)
			if (0 == strncmp(thinkFunctions[i].Context, context, MAX_CONTEXT_LENGTH))
				return i;

		return NO_THINK_CONTEXT;
	}


	public Contents GetWaterType() {
		Contents outVal = 0;
		if ((WaterType & 1) != 0)
			outVal |= Contents.Water;
		if ((WaterType & 2) != 0)
			outVal |= Contents.Slime;
		return outVal;
	}

	public static BasePlayer? GetPredictionPlayer() => PredictionPlayer;
	public static void SetPredictionPlayer(BasePlayer? player) => PredictionPlayer = player;
	public static int GetPredictionRandomSeed()
#if GAME_DLL
		=> PredictionRandomSeed; // todo: this is more complex
#else
		=> PredictionRandomSeed;
#endif
	public static void SetPredictionRandomSeed(in UserCmd cmd) {
		if (Unsafe.IsNullRef(in cmd)) {
			PredictionRandomSeed = -1;
			return;
		}

		PredictionRandomSeed = cmd.RandomSeed;
#if GAME_DLL
		// todo: predictionrandomseedserver, ServerRandomSeed, etc
#endif
	}

	public BasePlayer? GetSimulatingPlayer() => PlayerSimulationOwner.Get();
	public virtual ref readonly Vector3 WorldSpaceCenter() {
		return ref GetAbsOrigin(); // todo
	}

	public void SetPlayerSimulated(BasePlayer owner) {
		b_IsPlayerSimulated = true;
		owner.AddToPlayerSimulationList(owner);
		PlayerSimulationOwner.Set(owner);
	}

	public void UnsetPlayerSimulated() {
		PlayerSimulationOwner.Get()?.RemoveFromPlayerSimulationList(this);
		PlayerSimulationOwner.Set(null);
		b_IsPlayerSimulated = false;
	}



	public virtual void SetEffects(EntityEffects effects) {
		if (Effects != (int)effects) {
			Effects = (int)effects;
#if !CLIENT_DLL
			DispatchUpdateTransmitState();
#else
			UpdateVisibility();
#endif
		}
	}
	public virtual void AddEffects(EntityEffects effects) {
		Effects |= (int)effects;
		if ((effects & EntityEffects.NoDraw) != 0) {
#if !CLIENT_DLL
			DispatchUpdateTransmitState();
#else
			UpdateVisibility();
#endif
		}
	}

	public void SetWaterType(Contents contents) {
		WaterType = 0;
		if ((contents & Contents.Water) != 0) WaterType |= 1;
		if ((contents & Contents.Slime) != 0) WaterType |= 2;
	}

	public virtual void RemoveEffects(EntityEffects effects) {
		Effects &= ~(int)effects;
		if ((effects & EntityEffects.NoDraw) != 0) {
#if !CLIENT_DLL
			NetworkProp().MarkPVSInformationDirty();
			DispatchUpdateTransmitState();
#else
			UpdateVisibility();
#endif
		}
	}


	public bool IsEffectActive(EntityEffects fx) {
		return ((EntityEffects)Effects & fx) != 0;
	}

	public void FollowEntity(BaseEntity? baseEntity, bool boneMerge = true) {
		if (baseEntity != null) {
			SetParent(baseEntity);
			SetMoveType(Source.MoveType.None);

			if (boneMerge)
				AddEffects(EntityEffects.BoneMerge);

			AddSolidFlags(SolidFlags.NotSolid);
			SetLocalOrigin(vec3_origin);
			SetLocalAngles(vec3_angle);
		}
		else
			StopFollowingEntity();
	}

	public void StopFollowingEntity() {

	}

	public EntityFlags GetFlags() => (EntityFlags)flags;
	public MoveType GetMoveType() => (MoveType)MoveType;
	public MoveCollide GetMoveCollide() => (MoveCollide)MoveCollide;
	public CollisionGroup GetCollisionGroup() => (CollisionGroup)CollisionGroup;

	public void SetCollisionGroup(CollisionGroup collisionGroup) {
		if ((CollisionGroup)CollisionGroup != collisionGroup) {
			CollisionGroup = (int)collisionGroup;
			CollisionRulesChanged();
		}
	}

	public void CollisionRulesChanged() { } // TODO

	public void SetSimulatedEveryTick(bool sim) {
		if (SimulatedEveryTick != sim) {
			SimulatedEveryTick = sim;
#if CLIENT_DLL
			Interp_UpdateInterpolationAmounts(ref GetVarMapping());
#endif
		}
	}

	public void SetAnimatedEveryTick(bool anim) {
		if (AnimatedEveryTick != anim) {
			AnimatedEveryTick = anim;
#if CLIENT_DLL
			Interp_UpdateInterpolationAmounts(ref GetVarMapping());
#endif
		}
	}

	public TimeUnit_t GetAnimTime() => AnimTime;
	public TimeUnit_t GetSimulationTime() => SimulationTime;

	public void SetAnimTime(TimeUnit_t time) => AnimTime = time;
	public void SetSimulationTime(TimeUnit_t time) => SimulationTime = time;

	public virtual void PhysicsUpdate(IPhysicsObject? physicsObject) {

	}

	const double MaxEntityEulerAngle = 360.0 * 1000.0f;
	internal static bool IsEntityCoordinateReasonable(vec_t c) {
		float r = k_flMaxEntityPosCoord;
		return c > -r && c < r;

	}
	internal static bool IsEntityPositionReasonable(Vector3 v) {
		float r = k_flMaxEntityPosCoord;
		return
			v.X > -r && v.X < r &&
			v.Y > -r && v.Y < r &&
			v.Z > -r && v.Z < r;
	}
	internal static bool IsEntityQAngleReasonable(QAngle q) {
		float r = k_flMaxEntityEulerAngle;
		return
			q.X > -r && q.X < r &&
			q.Y > -r && q.Y < r &&
			q.Z > -r && q.Z < r;
	}

	public static bool IsEntityQAngleVelReasonable(in QAngle q) {
		float r = k_flMaxEntitySpinRate;
		return
			q.X > -r && q.X < r &&
			q.Y > -r && q.Y < r &&
			q.Z > -r && q.Z < r;
	}
	internal static bool IsEntityAngularVelocityReasonable(Vector3 q) {
		float r = k_flMaxEntitySpinRate;
		return
			q.X > -r && q.X < r &&
			q.Y > -r && q.Y < r &&
			q.Z > -r && q.Z < r;
	}

	internal static short PrecacheScriptSound(ReadOnlySpan<char> sound) {
		return g_SoundEmitterSystem.PrecacheScriptSound(sound);
	}

	public static void PrefetchScriptSound(ReadOnlySpan<char> soundname) => g_SoundEmitterSystem.PrefetchScriptSound(soundname);

	public static bool PrecacheSound(ReadOnlySpan<char> name) {
#if GAME_DLL
		if (!IsPrecacheAllowed())
			if (!enginesound.IsSoundPrecached(name))
				Warning($"Late precache of {name}\n");
#endif
		return enginesound.PrecacheSound(name, true);
	}

	public static void PrefetchSound(ReadOnlySpan<char> name) => enginesound.PrefetchSound(name);
	public virtual void ParseMapData(EntityMapData mapData) {
		// The map data (and the parser) are byte-based (C++ char*); decode each key/value to ASCII
		// char spans here so KeyValue can work in ReadOnlySpan<char>.
		Span<byte> keyNameBytes = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];
		Span<byte> valueBytes = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];
		Span<char> keyName = stackalloc char[EntityMapData.MAPKEY_MAXLENGTH];
		Span<char> value = stackalloc char[EntityMapData.MAPKEY_MAXLENGTH];

#if DEBUG && GAME_DLL
		// todo later: ValidateDataDescription();
#endif

		// loop through all keys in the data block and pass the info back into the object
		if (mapData.GetFirstKey(keyNameBytes, valueBytes)) {
			do {
				int kl = Encoding.ASCII.GetChars(keyNameBytes[..MapEntity.StrLen(keyNameBytes)], keyName);
				int vl = Encoding.ASCII.GetChars(valueBytes[..MapEntity.StrLen(valueBytes)], value);
				KeyValue(keyName[..kl], value[..vl]);
			}
			while (mapData.GetNextKey(keyNameBytes, valueBytes));
		}
	}
	public void SetRenderColor(byte r, byte g, byte b) => ColorRender = new Color(r, g, b, ColorRender.A);
	public void SetRenderColor(byte r, byte g, byte b, byte a) => ColorRender = new Color(r, g, b, a);
	public void SetRenderColorA(byte a) => ColorRender = new Color(ColorRender.R, ColorRender.G, ColorRender.B, a);

	public virtual bool KeyValue(ReadOnlySpan<char> szKeyName, ReadOnlySpan<char> szValue) {
		//!! temp hack, until worldcraft is fixed
		// strip the # tokens from (duplicate) key names
		ReadOnlySpan<char> key = szKeyName;
		int hash = key.IndexOf('#');
		if (hash >= 0)
			key = key[..hash];

		if (FStrEq(key, "rendercolor") || FStrEq(key, "rendercolor32")) {
			Util.StringToColor32(out Color tmp, szValue);
			SetRenderColor(tmp.R, tmp.G, tmp.B);
			// don't copy alpha, legacy support uses renderamt
			return true;
		}

		if (FStrEq(key, "renderamt")) {
			SetRenderColorA((byte)atoi(szValue));
			return true;
		}

		if (FStrEq(key, "disableshadows")) {
			if (atoi(szValue) != 0)
				AddEffects(EntityEffects.NoShadow);
			return true;
		}

		if (FStrEq(key, "mins")) {
			Vector3 mins = default;
			UTIL_StringToVector(mins.Base(), szValue);
			CollisionProp().SetCollisionBounds(mins, CollisionProp().OBBMaxs());
			return true;
		}

		if (FStrEq(key, "maxs")) {
			Vector3 maxs = default;
			UTIL_StringToVector(maxs.Base(), szValue);
			CollisionProp().SetCollisionBounds(CollisionProp().OBBMins(), maxs);
			return true;
		}

		if (FStrEq(key, "disablereceiveshadows")) {
			if (atoi(szValue) != 0)
				AddEffects(EntityEffects.NoReceiveShadow);
			return true;
		}

		if (FStrEq(key, "nodamageforces")) {
			if (atoi(szValue) != 0)
				AddEFlags(EFL.NoDamageForces);
			return true;
		}

		// Fix up single angles
		if (FStrEq(key, "angle")) {
			ref readonly QAngle localAngles = ref GetLocalAngles();

			float y = strtof(szValue, out _);
			string szBuf;
			if (y >= 0)
				szBuf = $"{localAngles.X} {y} {localAngles.Z}";
			else if ((int)y == -1)
				szBuf = "-90 0 0";
			else
				szBuf = "90 0 0";

			// Do this so inherited classes looking for 'angles' don't have to bother with 'angle'
			return KeyValue(key, szBuf);
		}

		// NOTE: Have to do these separate because they set two values instead of one
		if (FStrEq(key, "angles")) {
			QAngle angles = default;
			UTIL_StringToVector(angles.Base(), szValue);

			// If you're hitting this assert, it's probably because you're
			// calling SetLocalAngles from within a KeyValues method.. use SetAbsAngles instead!
			Assert((GetMoveParent() == null) && !IsEFlagSet(EFL.DirtyAbsTransform));
			SetAbsAngles(angles);
			return true;
		}

		if (FStrEq(key, "origin")) {
			Vector3 vecOrigin = default;
			UTIL_StringToVector(vecOrigin.Base(), szValue);

			// If you're hitting this assert, it's probably because you're
			// calling SetLocalOrigin from within a KeyValues method.. use SetAbsOrigin instead!
			Assert((GetMoveParent() == null) && !IsEFlagSet(EFL.DirtyAbsTransform));
			SetAbsOrigin(vecOrigin);
			return true;
		}

#if GAME_DLL
		if (FStrEq(key, "targetname")) {
			Name = new string(szValue); // m_iName = AllocPooledString(szValue)
			return true;
		}

		if (FStrEq(key, "model")) {
			SetModelName(szValue); // DEFINE_KEYFIELD( m_ModelName, "model" )
			return true;
		}

		for (DataMap? dmap = GetDataDescMap(); dmap != null; dmap = dmap.BaseMap)
			if (SaveRestoreGameDLL.ParseKeyvalue(this, dmap.DataDesc, dmap.DataNumFields, key, szValue))
				return true;
#endif

		// key hasn't been handled
		return false;
	}


	public static float k_flMaxEntityPosCoord = MAX_COORD_FLOAT;
	public static float k_flMaxEntityEulerAngle = 360.0f * 1000.0f; // really should be restricted to +/-180, but some code doesn't adhere to this.  let's just trap NANs, etc
																	// Sometimes the resulting computed speeds are legitimately above the original
																	// constants; use bumped up versions for the downstream validation logic to
																	// account for this.
	public static float k_flMaxEntitySpeed = k_flMaxVelocity * 2.0f;
	public static float k_flMaxEntitySpinRate = k_flMaxAngularVelocity * 10.0f;
	static double s_LastEntityReasonableEmitTime;
	public static bool CheckEmitReasonablePhysicsSpew() {
		// Reported recently?
		double now = Platform.Time;
		if (now >= s_LastEntityReasonableEmitTime && now < s_LastEntityReasonableEmitTime + 5.0) {
			// Already reported recently
			return false;
		}

		// Not reported recently.  Report it now
		s_LastEntityReasonableEmitTime = now;
		return true;
	}
	public static int CheckEntityVelocity(ref Vector3 v) {
		float r = k_flMaxEntitySpeed;
		if (
			v.X > -r && v.X < r &&
			v.Y > -r && v.Y < r &&
			v.Z > -r && v.Z < r) {
			// The usual case.  It's totally reasonable
			return 1;
		}
		float speed = v.Length();
		if (speed < k_flMaxEntitySpeed * 100.0f) {
			// Sort of suspicious.  Clamp it
			v *= k_flMaxEntitySpeed / speed;
			return 0;
		}

		// A terrible, horrible, no good, very bad velocity.
		return -1;
	}
}

#endif
