global using static Game.Server.PlayerConvars;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Game.Server;
using Source.Common.Mathematics;
using Source.Common.Physics;
using Source.Engine;

using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

using FIELD = Source.FIELD<BasePlayer>;

public enum PlayerConnectedState
{
	Connected,
	Disconnecting,
	Disconnected
}

public static class PlayerConvars
{
	public static readonly ConVar sv_noclipduringpause = new("sv_noclipduringpause", "0", FCvar.Replicated | FCvar.Cheat, "If cheats are enabled, then you can noclip with the game paused (for doing screenshots, etc.).");

	public static BaseEntity? FindEntityClassForward(BasePlayer me, ReadOnlySpan<char> classname) {
		me.EyeVectors(out Vector3 forward);
		Util.TraceLine(me.EyePosition(), me.EyePosition() + forward * MAX_COORD_RANGE, Mask.Solid, me, CollisionGroup.None, out Trace tr);
		if (tr.Fraction != 1.0 && tr.DidHitNonWorldEntity()) {
			BaseEntity? hit = tr.Ent;
			if (BaseEntity.FClassnameIs(hit, classname))
				return hit;
		}
		return null;
	}

	public static BaseEntity? FindEntityForward(BasePlayer? me, bool hull) {
		if (me != null) {
			Mask mask;

			if (hull)
				mask = Mask.Solid;
			else
				mask = Mask.Shot;

			me.EyeVectors(out Vector3 forward);
			Util.TraceLine(me.EyePosition(), me.EyePosition() + forward * MAX_COORD_RANGE, mask, me, CollisionGroup.None, out Trace tr);
			if (tr.Fraction != 1.0 && tr.DidHitNonWorldEntity())
				return tr.Ent;
		}
		return null;
	}

	public static BaseEntity? FindPickerEntityClass(BasePlayer player, ReadOnlySpan<char> classname) {
		BaseEntity? entity = FindEntityClassForward(player, classname);

		if (entity == null) {
			player.EyeVectors(out Vector3 forward);
			Vector3 origin = player.WorldSpaceCenter();
			entity = gEntList.FindEntityClassNearestFacing(origin, forward, 0.95f, classname);
		}
		return entity;
	}

	public static BaseEntity? FindPickerEntity(BasePlayer? player) {
		BaseEntity? entity = FindEntityForward(player, true);

		if (entity == null) {
			player!.EyeVectors(out Vector3 forward);
			Vector3 origin = player.WorldSpaceCenter();
			entity = gEntList.FindEntityNearestFacing(origin, forward, 0.95f);
		}
		return entity;
	}
}

public class GamePlayerInfo : IBotController, IPlayerInfo
{
	BasePlayer Parent = null!;
	public GamePlayerInfo() {

	}

	public void ChangeTeam(int iTeamNum) {
		throw new NotImplementedException();
	}

	public QAngle GetAbsAngles() {
		throw new NotImplementedException();
	}

	public Vector3 GetAbsOrigin() {
		throw new NotImplementedException();
	}

	public int GetArmorValue() {
		throw new NotImplementedException();
	}

	public int GetDeathCount() {
		throw new NotImplementedException();
	}

	public int GetFragCount() {
		throw new NotImplementedException();
	}

	public int GetHealth() {
		throw new NotImplementedException();
	}

	public BotCmd GetLastUserCommand() {
		throw new NotImplementedException();
	}

	public QAngle GetLocalAngles() {
		throw new NotImplementedException();
	}

	public Vector3 GetLocalOrigin() {
		throw new NotImplementedException();
	}

	public int GetMaxHealth() {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetModelName() {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetName() {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetNetworkIDString() {
		throw new NotImplementedException();
	}

	public Vector3 GetPlayerMaxs() {
		throw new NotImplementedException();
	}

	public Vector3 GetPlayerMins() {
		throw new NotImplementedException();
	}

	public int GetTeamIndex() {
		throw new NotImplementedException();
	}

	public int GetUserID() {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetWeaponName() {
		throw new NotImplementedException();
	}

	public bool IsConnected() {
		throw new NotImplementedException();
	}

	public bool IsDead() {
		throw new NotImplementedException();
	}

	public bool IsEFlagSet(int eflagMask) {
		throw new NotImplementedException();
	}

	public bool IsFakeClient() {
		throw new NotImplementedException();
	}

	public bool IsHLTV() {
		throw new NotImplementedException();
	}

	public bool IsInAVehicle() {
		throw new NotImplementedException();
	}

	public bool IsObserver() {
		throw new NotImplementedException();
	}

	public bool IsPlayer() {
		throw new NotImplementedException();
	}

	public bool IsReplay() {
		throw new NotImplementedException();
	}

	public void RemoveAllItems(bool removeSuit) {
		throw new NotImplementedException();
	}

	public void RunPlayerMove(ref BotCmd ucmd) {
		throw new NotImplementedException();
	}

	public void SetAbsAngles(in QAngle ang) {
		throw new NotImplementedException();
	}

	public void SetAbsOrigin(in Vector3 vec) {
		throw new NotImplementedException();
	}

	public void SetActiveWeapon(ReadOnlySpan<char> weaponName) {
		throw new NotImplementedException();
	}

	public void SetLocalAngles(in QAngle angles) {
		throw new NotImplementedException();
	}

	public void SetLocalOrigin(in Vector3 origin) {
		throw new NotImplementedException();
	}

	public void SetParent(BasePlayer parent) => Parent = parent;
}

[Flags]
public enum PlayerPhysFlag
{
	DirOverride = 1 << 0,
	Ducking = 1 << 1,
	Using = 1 << 2,
	Observer = 1 << 3,
	VPhysicsMotionController = 1 << 4,
	GamePhysicsRotPush = 1 << 5,
}

[NetworkName("CBasePlayer")]
public partial class BasePlayer : BaseCombatCharacter
{
	[NetworkName("m_ArmorValue")]
	public int ArmorValue;

	public int GetArmorValue() => ArmorValue;

	public void SetArmorValue(int value) {
		if (ArmorValue != value) {
			NetworkStateChanged();
			ArmorValue = value;
		}
	}

	static InlineArray1024<char> InfoBuffer;

	public ReadOnlySpan<char> GetInfo(ReadOnlySpan<char> name) {
		strcpy(InfoBuffer, engine.GetClientConVarValue(engine.IndexOfEdict(Edict()), name));
		return ((ReadOnlySpan<char>)InfoBuffer).SliceNullTerminatedString();
	}

	public float GetInfoNum(ReadOnlySpan<char> name, float fallback) {
		strcpy(InfoBuffer, engine.GetClientConVarValue(engine.IndexOfEdict(Edict()), name));
		if (InfoBuffer[0] == '\0')
			return fallback;
		return (float)atof(InfoBuffer);
	}

	[Flags]
	public enum SF
	{
		NoRespawn = 1 << 30,
	}
	public const float DOT_30DEGREE = 0.866025403784f;
	public const float DOT_45DEGREE = 0.707106781187f;
	public static readonly SendTable DT_PlayerState = new(nameof(DT_PlayerState), [
		SendPropInt(FIELD<PlayerState>.OF(nameof(PlayerState.DeadFlag)), 1, PropFlags.Unsigned)
	]);

	public override bool IsPlayer() => true;
	public override Class_T Classify() => Class_T.Player;
	public virtual bool IsBot() => (GetFlags() & EntityFlags.FakeClient) != 0;
	public virtual bool IsFakeClient() => (GetFlags() & EntityFlags.FakeClient) != 0;
	public virtual bool IsBotOfType(int botType) => (GetBotType() != 0) && (GetBotType() == botType);
	public virtual int GetBotType() => 0;
	public bool IsHLTV() => pl.HLTV;

	public int GetLockViewanglesTickNumber() => LockViewanglesTickNumber;
	public QAngle GetLockViewanglesData() => LockedViewangles;

	public Vector3 PreviouslyPredictedOrigin;

	public override bool IsNetClient() => true;

	public BaseViewModel? GetViewModel(int index) => ViewModel[index].Get();

	public static readonly SendTable DT_LocalPlayerExclusive = new(nameof(DT_LocalPlayerExclusive), [
		SendPropDataTable("m_Local", FIELD.OF(nameof(Local)), PlayerLocalData.DT_Local),
		SendPropFloat(FIELD.OF(nameof(Friction)), 0, PropFlags.NoScale | PropFlags.RoundDown, 0.0f, 4.0f),
		SendPropArray3(FIELD.OF_ARRAY(nameof(Ammo)), SendPropInt( FIELD.OF_ARRAYINDEX(nameof(Ammo)), 16, PropFlags.Unsigned)),
		SendPropInt(FIELD.OF(nameof(TickBase)), -1, PropFlags.ChangesOften),
		SendPropInt(FIELD.OF(nameof(NextThinkTick))),
		SendPropEHandle(FIELD.OF(nameof(LastWeapon))),
		SendPropEHandle(FIELD.OF(nameof(GroundEntity)), PropFlags.ChangesOften),
		SendPropVector(FIELD.OF(nameof(BaseVelocity)), 0, PropFlags.NoScale),
		SendPropEHandle(FIELD.OF(nameof(ConstraintEntity))),
		SendPropVector(FIELD.OF(nameof(ConstraintCenter)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(ConstraintRadius)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(ConstraintWidth)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(ConstraintSpeedFactor)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(DeathTime)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(LaggedMovementValue)), 0, PropFlags.NoScale),
		SendPropEHandle(FIELD.OF(nameof(TonemapController))),
		SendPropEHandle(FIELD.OF(nameof(ViewEntity))),
		SendPropBool(FIELD.OF(nameof(DisableWorldClicking))),
	]);

	public static readonly SendTable DT_BasePlayer = new(DT_BaseCombatCharacter, [
		SendPropDataTable(nameof(pl), FIELD.OF(nameof(pl)), DT_PlayerState, SendProxy_DataTableToDataTable),
		SendPropEHandle(FIELD.OF(nameof(Vehicle))),
		SendPropEHandle(FIELD.OF(nameof(UseEntity))),
		SendPropInt(FIELD.OF(nameof(LifeState)), 3, PropFlags.Unsigned ),
		SendPropEHandle(FIELD.OF(nameof(ColorCorrectionCtrl))), // << gmod specific
		SendPropFloat(FIELD.OF(nameof(Maxspeed)), 12, PropFlags.RoundDown, 0.0f, 2048.0f ),
		SendPropInt(FIELD.OF("flags"), Constants.PLAYER_FLAG_BITS, PropFlags.Unsigned|PropFlags.ChangesOften, SendProxy_CropFlagsToPlayerFlagBitsLength),
		SendPropInt(FIELD.OF(nameof(ObserverMode)), 3, PropFlags.Unsigned),
		SendPropEHandle(FIELD.OF(nameof(ObserverTarget))),
		SendPropFloat(FIELD.OF(nameof(FOV)), 16, PropFlags.Unsigned, 0, 65536),
		SendPropFloat(FIELD.OF(nameof(FOVStart)), 16, PropFlags.Unsigned, 0, 65536),
		SendPropTime64(FIELD.OF(nameof(FOVTime))),
		SendPropFloat(FIELD.OF(nameof(DefaultFOV)), 16, PropFlags.Unsigned, 0, 65536),
		SendPropEHandle(FIELD.OF(nameof(ZoomOwner))),

		SendPropEHandle(FIELD.OF_SENDINFO_ARRAY(nameof(ViewModel))),
		SendPropArray(FIELD.OF_ARRAY(nameof(ViewModel))),

		SendPropBool(FIELD.OF(nameof(UseWeaponsInVehicle))),
		SendPropDataTable( "localdata", DT_LocalPlayerExclusive, SendProxy_SendLocalDataTable),
	]);

	public static Edict? s_PlayerEdict;
	public static BaseEntity? g_LastSpawn;

	public int StuckLast;
	public float MaxSpeed() => Maxspeed;
	public void SetMaxSpeed(float maxSpeed) => Maxspeed = maxSpeed;
	public TimeUnit_t GetLaggedMovementValue() => LaggedMovementValue;

	public int SurfaceProps;
	public SurfaceData_ptr? SurfaceData;
	public float SurfaceFriction;
	public UtlSymId_t TextureType;
	public UtlSymId_t PreviousTextureType;

	readonly List<CommandContext> CommandContext = [];
	readonly List<PlayerSimInfo> VecPlayerSimInfo = [];

	TimeUnit_t MovementTimeForUserCmdProcessingRemaining;
	TimeUnit_t LastUserCommandTime;
	TimeUnit_t LastObjectiveTime;
	UserCmd LastCmd;

	public int Impulse;

	protected QAngle AutoAim;
	protected Vector3 AdditionalPVSOrigin;
	protected Vector3 CameraPVSOrigin;

	int BodyPitchPoseParam;
	public float ForwardMove;
	public float SideMove;

	public int ObserverLastMode;
	public bool ForcedObserverMode;
	protected PlayerPhysFlag PhysicsFlags;
	public int Train;

	public void SetPhysicsFlag(PlayerPhysFlag flag, bool set) {
		if (set)
			PhysicsFlags |= flag;
		else
			PhysicsFlags &= ~flag;
	}

	int LastDmageAmount;
	Vector3 DmgOrigin;
	Vector3 OldOrigin;
	bool ForceOrigin;
	Vector3 ForcedOrigin;
	Vector3 SmoothedVelocity;
	float DmgTake;
	float DmgSave;
	TimeUnit_t FlashTime;
	DamageType bitsDamageType;
	int HUDDamage;
	TimeUnit_t DeathAnimTime;
	byte[] TimeBasedDamage = new byte[(int)ITBD.TimeBasedDamageCount];

	int DrownDmg;
	int DrownRestored;

	int PoisonDmg;
	int PoisonRestored;
	int LockViewanglesTickNumber;
	QAngle LockedViewangles;

	public int UpdateRate;
	public TimeUnit_t LerpTime;
	public bool LagCompensation;
	public bool PredictWeapons;

	public static void SendProxy_CropFlagsToPlayerFlagBitsLength(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		int mask = (1 << Constants.PLAYER_FLAG_BITS) - 1;
		int data = field.GetValue<int>(instance);
		outData.Int = data & mask;
	}

	GamePlayerInfo PlayerInfo = new();

	public static readonly new ServerClass ServerClass = new ServerClass(DT_BasePlayer);


	public BasePlayer() {
		AddEFlags(EFL.NoAutoEdictAttach);

#if DEBUG
		AutoAim.Init();
		AdditionalPVSOrigin.Init();
		CameraPVSOrigin.Init();
		DmgOrigin.Init();
		LadderNormal.Init();

		OldOrigin.Init();
		SmoothedVelocity.Init();
#endif

		if (s_PlayerEdict != null) {
			// take the assigned edict_t and attach it
			Assert(s_PlayerEdict != null);
			NetworkProp().AttachEdict(s_PlayerEdict);
			s_PlayerEdict = null;
		}

		FlashTime = -1;
		pl.FixAngle = (int)FixAngle.Absolute;
		pl.HLTV = false;
		pl.Replay = false;
		pl.Frags = 0;
		pl.Deaths = 0;

		Netname[0] = '\0';

		Health = 0;
		Weapon_SetLast(null);
		bitsDamageType = 0;

		ForceOrigin = false;
		Vehicle.Set(null);
		CurrentCommand = default;
		LockViewanglesTickNumber = 0;
		LockedViewangles.Init();

		// Setup our default FOV
		DefaultFOV = g_pGameRules.DefaultFOV();

		ZoomOwner.Set(null);

		UpdateRate = 20;  // cl_updaterate defualt
		LerpTime = 0.1f; // cl_interp default
		PredictWeapons = true;
		LagCompensation = false;
		LaggedMovementValue = 1.0f;
		StuckLast = 0;
		ImpactEnergyScale = 1.0f;
		LastPlayerTalkTime = 0.0;
		PlayerInfo.SetParent(this);

		ResetObserverMode();

		SurfaceProps = 0;
		SurfaceData = null;
		SurfaceFriction = 1.0f;
		TextureType = 0;
		PreviousTextureType = 0;

		// SuicideCustomKillFlags = 0;
		// Delay = 0.0f;
		// ReplayEnd = -1;
		// ReplayEntity = 0;

		AutoKickDisabled = false;

		NumCrouches = 0;
		DuckToggled = false;
		PhysicsWasFrozen = false;

		// Used to mask off buttons
		AfButtonDisabled = 0;
		AfButtonForced = 0;

		BodyPitchPoseParam = -1;
		ForwardMove = 0;
		SideMove = 0;

		// NVNT default to no haptics
		HasHaptics = false;

		ConstraintCenter = vec3_origin;

		LastUserCommandTime = 0;
		MovementTimeForUserCmdProcessingRemaining = 0;

		LastObjectiveTime = -1;
	}

	public readonly PlayerState pl = new();
	[NetworkName("m_Local")]
	public readonly PlayerLocalData Local = new();

	public ref AudioParams GetAudioParams() => ref Local.Audio;

	// Used by env_soundscape_triggerable to manage when the player is touching multiple
	// soundscape triggers simultaneously.
	// The one at the HEAD of the list is always the current soundscape for the player.
	public readonly List<EHANDLE> TriggerSoundscapeList = [];

	public void SetBodyPitch(float pitch) {
		if (BodyPitchPoseParam >= 0)
			SetPoseParameter(BodyPitchPoseParam, pitch);
	}

	[NetworkName("m_hVehicle")]
	public EHANDLE Vehicle = new();
	[NetworkName("m_hUseEntity")]
	public EHANDLE UseEntity = new();
	[NetworkName("m_hObserverTarget")]
	public EHANDLE ObserverTarget = new();
	[NetworkName("m_hZoomOwner")]
	public EHANDLE ZoomOwner = new();
	[NetworkName("m_hConstraintEntity")]
	public EHANDLE ConstraintEntity = new();
	[NetworkName("m_hTonemapController")]
	public EHANDLE TonemapController = new();
	[NetworkName("m_hViewEntity")]
	public EHANDLE ViewEntity = new();
	[NetworkName("m_hViewModel")]
	protected InlineArrayNewMaxViewmodels<Handle<BaseViewModel>> ViewModel = new();
	readonly List<Handle<BaseEntity>> SimulatedByThisPlayer = [];

	public IServerVehicle? GetVehicle() => Vehicle.Get()?.GetServerVehicle();
	public override BaseEntity? GetVehicleEntity() => Vehicle.Get();
	public override bool IsInAVehicle() => Vehicle.Get() != null;
#if GMOD_DLL
	public virtual bool RestrictPlayerPitch() => false;
	public virtual bool CanAttack() => true;
#endif
	public float GetStepSize() => Local.StepSize;

	public BaseEntity? GetViewEntity() => ViewEntity.Get();

	public virtual ReadOnlySpan<char> GetSceneSoundToken() => "";

	public void SetViewEntity(BaseEntity? entity) {
		ViewEntity.Set(entity);

		if (ViewEntity.Get() != null)
			engine.SetView(Edict()!, ViewEntity.Get()!.Edict()!);
		else
			engine.SetView(Edict()!, Edict()!);
	}

	public void EnableControl(bool control) {
		if (!control)
			AddFlag(EntityFlags.Frozen);
		else
			RemoveFlag(EntityFlags.Frozen);
	}

	bool HasHaptics;
	[NetworkName("m_bDisableWorldClicking")]
	public bool DisableWorldClicking;
	public bool WorldClicking;
	public Vector3 WorldClickVector;
	[NetworkName("m_flMaxspeed")]
	float Maxspeed;
	[NetworkName("m_iObserverMode")]
	int ObserverMode;
	[NetworkName("m_iFOV")]
	int FOV;
	[NetworkName("m_nTickBase")]
	public int TickBase;
	[NetworkName("m_iFOVStart")]
	int FOVStart;
	[NetworkName("m_flFOVTime")]
	TimeUnit_t FOVTime;
	[NetworkName("m_iDefaultFOV")]
	float DefaultFOV;
	[NetworkName("m_vecConstraintCenter")]
	public Vector3 ConstraintCenter;
	[NetworkName("m_flConstraintRadius")]
	public float ConstraintRadius;
	[NetworkName("m_flConstraintWidth")]
	public float ConstraintWidth;
	[NetworkName("m_flConstraintSpeedFactor")]
	public float ConstraintSpeedFactor;
	InlineArray18<char> LastPlaceName;
	[NetworkName("m_hColorCorrectionCtrl")]
	EHANDLE ColorCorrectionCtrl = new();
	[NetworkName("m_bUseWeaponsInVehicle")]
	bool UseWeaponsInVehicle;
	public bool GetAllowWeaponsInVehicle() => UseWeaponsInVehicle;

	public virtual void StopObserverMode() {
		// todo
	}
	public bool OnTarget;
	[NetworkName("m_flDeathTime")]
	public TimeUnit_t DeathTime;
	[NetworkName("m_flLaggedMovementValue")]
	public double LaggedMovementValue;
	public TimeUnit_t StepSoundTime;

	InlineArray32<char> AnimExtension;
	public Vector3 WaterJumpVel;
	public TimeUnit_t SwimSoundTime;
	public Vector3 LadderNormal;
	public TimeUnit_t WaterJumpTime;
	int DrownDmgRate;
	public PlayerConnectedState Connected;
	TimeUnit_t AirFinished;
	TimeUnit_t PainFinished;
	public int NumCrouches;
	public bool DuckToggled;
	public bool PhysicsWasFrozen;
	public bool AutoKickDisabled;
	public bool IsObserver() => GetObserverMode() != Shared.ObserverMode.None;
	public InButtons Buttons;
	public InButtons AfButtonPressed;
	public InButtons AfButtonReleased;
	public InButtons AfButtonLast;
	public InButtons AfButtonDisabled;
	public InButtons AfButtonForced;

	//-----------------------------------------------------------------------------
	// Purpose: Strips off IN_xxx flags from the player's input
	//-----------------------------------------------------------------------------
	public void ForceButtons(InButtons buttons) {
		AfButtonForced |= buttons;
	}

	//-----------------------------------------------------------------------------
	// Purpose: Re-enables stripped IN_xxx flags to the player's input
	//-----------------------------------------------------------------------------
	public void UnforceButtons(InButtons buttons) {
		AfButtonForced &= ~buttons;
	}

	public bool GamePaused;

	public virtual void EquipSuit(bool playEffects = false) {
		Local.WearingSuit = true;
	}

	public virtual void RemoveSuit() {
		Local.WearingSuit = false;
	}

	public BaseViewModel? GetViewModel(int index = 0, bool observerOK = true) {
		return ViewModel[index].Get();
	}

	public virtual BaseEntity? GetObserverTarget() => null; // todo

	public Vector3 VehicleViewOrigin;
	public QAngle VehicleViewAngles;
	public float VehicleViewFOV;
	public int VehicleViewSavedFrame;

	public int GetFOVForNetworking() {
		int defaultFOV;

		// The vehicle's FOV wins if we're asking for a default value
		if (GetVehicle() != null) {
			CacheVehicleView();
			defaultFOV = (VehicleViewFOV == 0) ? GetDefaultFOV() : (int)VehicleViewFOV;
		}
		else
			defaultFOV = GetDefaultFOV();

		int fFOV = (FOV == 0) ? defaultFOV : FOV;

		// If it's immediate, just do it
		if (Local.FOVRate == 0.0f)
			return fFOV;

		if (gpGlobals.CurTime - FOVTime < Local.FOVRate)
			fFOV = Math.Min(fFOV, FOVStart);

		return fFOV;
	}

	public float GetFOVDistanceAdjustFactorForNetworking() {
		float defaultFOV = GetDefaultFOV();
		float localFOV = GetFOVForNetworking();

		if (localFOV == defaultFOV || defaultFOV < 0.001f)
			return 1.0f;

		return localFOV / defaultFOV;
	}

	public int GetFOV() {
		int defaultFOV;

		// The vehicle's FOV wins if we're asking for a default value
		if (GetVehicle() != null) {
			CacheVehicleView();
			defaultFOV = (VehicleViewFOV == 0) ? GetDefaultFOV() : (int)VehicleViewFOV;
		}
		else {
			defaultFOV = GetDefaultFOV();
		}

		int fFOV = (FOV == 0) ? defaultFOV : FOV;

		// If it's immediate, just do it
		if (Local.FOVRate == 0.0f)
			return fFOV;

		TimeUnit_t deltaTime = (float)(gpGlobals.CurTime - FOVTime) / Local.FOVRate;

		if (deltaTime >= 1.0f)
			//If we're past the zoom time, just take the new value and stop lerping
			FOVStart = fFOV;
		else
			fFOV = (int)MathLib.SimpleSplineRemapValClamped(deltaTime, 0.0f, 1.0f, FOVStart, fFOV);

		return fFOV;
	}


	public TimeUnit_t ConsumeMovementTimeForUserCmdProcessing(TimeUnit_t timeNeeded) {
		if (MovementTimeForUserCmdProcessingRemaining <= 0.0)
			return 0.0;
		else if (timeNeeded > MovementTimeForUserCmdProcessingRemaining + DBL_EPSILON) {
			TimeUnit_t result = MovementTimeForUserCmdProcessingRemaining;
			MovementTimeForUserCmdProcessingRemaining = 0.0;
			return result;
		}
		else {
			MovementTimeForUserCmdProcessingRemaining -= timeNeeded;
			if (MovementTimeForUserCmdProcessingRemaining < 0.0)
				MovementTimeForUserCmdProcessingRemaining = 0.0;
			return timeNeeded;
		}
	}


	public BaseCombatWeapon? GetLastWeapon() => LastWeapon.Get();
	public override BaseCombatWeapon? GetActiveWeapon() => ActiveWeapon.Get();
	public void ResetAutoaim() => OnTarget = false;

	public ObserverMode GetObserverMode() => (ObserverMode)ObserverMode;
	public AnonymousSafeFieldPointer<UserCmd> CurrentCommand;
	public int CurrentCommandNumber() => CurrentCommand.Get().CommandNumber;

	public static bool gEvilImpulse101;

	public virtual bool BumpWeapon(BaseCombatWeapon weapon) {
		BaseCombatCharacter? owner = weapon.GetOwner();

		if (!IsAllowedToPickupWeapons())
			return false;

		if (owner != null || !Weapon_CanUse(weapon) || !g_pGameRules.CanHavePlayerItem(this, weapon)) {
			if (gEvilImpulse101)
				Util.Remove(weapon);
			return false;
		}

		if (hl2_episodic.GetBool()) {
			if (!Util.ItemCanBeTouchedByPlayer(weapon, this) && !gEvilImpulse101)
				return false;
		}
		else {
			if (weapon.FVisible(this, Mask.Solid) == false && (GetFlags() & EntityFlags.NoTarget) == 0)
				return false;
		}

		if (Weapon_OwnsThisType(weapon.GetClassname(), weapon.GetSubType()) != null) {
			if (Weapon_EquipAmmoOnly(weapon)) {
				if (weapon.HasPrimaryAmmo())
					return false;

				Util.Remove(weapon);
				return true;
			}
			else
				return false;
		}
		else {
			weapon.CheckRespawn();

			weapon.AddSolidFlags(SolidFlags.NotSolid);
			weapon.AddEffects(EntityEffects.NoDraw);

			Weapon_Equip(weapon);
			if (IsInAVehicle())
				weapon.Holster();
			else {
#if HL2_DLL
				if (!Game.Shared.GarrysMod.WeaponPhysCannon.PlayerHasMegaPhysCannon()) {
					if (weapon.UsesClipsForAmmo1())
						weapon.iClip1 = weapon.GetMaxClip1();

					Weapon_Switch(weapon);
				}
#endif
			}
			return true;
		}
	}

	public override bool Weapon_CanUse(BaseCombatWeapon weapon) => true;

	public override void Weapon_Equip(BaseCombatWeapon weapon) {
		base.Weapon_Equip(weapon);

		bool shouldSwitch = g_pGameRules.FShouldSwitchWeapon(this, weapon);

		if (shouldSwitch)
			Weapon_Switch(weapon);
	}

	public bool HasAnyAmmoOfType(int ammoIndex) {
		if (ammoIndex < 0)
			return false;

		if (GetAmmoCount(ammoIndex) != 0)
			return true;

		BaseCombatWeapon? weapon;

		for (int i = 0; i < MAX_WEAPONS; i++) {
			weapon = GetWeapon(i);

			if (weapon == null)
				continue;

			if (weapon.UsesClipsForAmmo1() && weapon.PrimaryAmmoType == ammoIndex) {
				if (weapon.HasPrimaryAmmo())
					return true;
			}

			if (weapon.UsesClipsForAmmo2() && weapon.SecondaryAmmoType == ammoIndex) {
				if (weapon.HasSecondaryAmmo())
					return true;
			}
		}

		return false;
	}

	public BaseEntity? GiveNamedItem(ReadOnlySpan<char> name, int subType = 0) {
		if (Weapon_OwnsThisType(name, subType) != null)
			return null;

		BaseEntity? pent = CreateEntityByName(name);
		if (pent == null) {
			Msg("NULL Ent in GiveNamedItem!\n");
			return null;
		}

		pent.SetLocalOrigin(GetLocalOrigin());
		pent.AddSpawnFlags(SF.NoRespawn);

		BaseCombatWeapon? weapon = (BaseCombatWeapon?)((BaseEntity)pent);
		weapon?.SetSubType(subType);

		Util.DispatchSpawn(pent);

		if (pent != null && !(pent.IsMarkedForDeletion()))
			pent.Touch(this);

		return pent;
	}

	public virtual void CheatImpulseCommands(int impulse) {
		if (!sv_cheats.GetBool())
			return;
	}
	public virtual void ImpulseCommands() {
		Trace tr;

		int iImpulse = (int)Impulse;
		switch (iImpulse) {
			case 100:
				// temporary flashlight for level designers
				if (FlashlightIsOn())
					FlashlightTurnOff();
				else
					FlashlightTurnOn();
				break;

			case 200:
				if (sv_cheats.GetBool()) {
					BaseCombatWeapon weapon;

					weapon = GetActiveWeapon()!;

					if (weapon.IsEffectActive(EntityEffects.NoDraw))
						weapon.Deploy();
					else
						weapon.Holster();
				}
				break;

			case 201:// paint decal
				if (gpGlobals.CurTime < NextDecalTime) {
					// too early!
					break;
				} {
					EyeVectors(out Vector3 forward);
					Util.TraceLine(EyePosition(), EyePosition() + forward * 128, Mask.SolidBrushOnly, this, Source.CollisionGroup.None, out tr);
				}

				if (tr.Fraction != 1.0) { // line hit something, so paint a decal
					// todo
				}

				break;
			default:
				// check all of the cheat impulse commands now
				CheatImpulseCommands(iImpulse);
				break;
		}

		Impulse = 0;
	}

	public virtual void InitialSpawn() {
		Connected = PlayerConnectedState.Connected;
		// gamestats todo
	}

	BaseEntity? FindPlayerStart(ReadOnlySpan<char> className) {
		BaseEntity? start = gEntList.FindEntityByClassname(null, className);
		BaseEntity? startFirst = start;

		while (start != null) {
			if (start.HasSpawnFlags(1))
				return start;

			start = gEntList.FindEntityByClassname(start, className);
		}

		return startFirst;
	}

	public virtual BaseEntity? EntSelectSpawnPoint() {
		BaseEntity? spot;
		Edict player = Edict();

		// if coop
		// elseif deathmatch todo

		if (gpGlobals.StartSpot == null || gpGlobals.StartSpot.Length == 0) {
			spot = FindPlayerStart("info_player_start");
			if (spot != null)
				goto ReturnSpawn;
		}
		else {
			spot = gEntList.FindEntityByName(null, gpGlobals.StartSpot);
			if (spot != null)
				goto ReturnSpawn;
		}

	ReturnSpawn:
		if (spot == null) {
			Warning("PutClientInServer: no info_player_start on level\n");
			return Instance(engine.PEntityOfEntIndex(0)!);
		}

		// LastSpawn = spot; todo
		return spot;
	}

	public override void Spawn() {
		SetClassname("player");

		SharedSpawn();

		SetSimulatedEveryTick(true);
		SetAnimatedEveryTick(true);

		// ArmorValue = SpawnArmorValue();
		// SetBlockLOS(false);
		MaxHealth = Health;

		if ((GetFlags() & EntityFlags.FakeClient) != 0) {
			ClearFlags();
			AddFlag(EntityFlags.Client | EntityFlags.FakeClient);
		}
		else {
			ClearFlags();
			AddFlag(EntityFlags.Client);
		}

		AddFlag(EntityFlags.AimTarget);

		EntityEffects effects = (EntityEffects)Effects & EntityEffects.NoShadow;
		SetEffects(effects);

		IncrementInterpolationFrame();

		// InitFogController();

		DmgTake = 0;
		DmgSave = 0;
		HUDDamage = -1;
		// DamageType = 0;
		PhysicsFlags = 0;
		DrownRestored = DrownDmg;

		SetFOV(this, 0);

		NextDecalTime = 0;

		GeigerDelay = gpGlobals.CurTime + 2.0f;    // wait a few seconds until user-defined message registrations

		// FieldOfView = 0.766;

		// AdditionPVSOrigin = vec3_origin;
		// CameraPVSOrigin = vec3_origin;

		// if (!GameHUDInitialized)
		// 	GameRules.SetDefaultPlayerTeam(this);

		g_pGameRules.GetPlayerSpawnSpot(this);

		SetViewOffset(VEC_VIEW_SCALED(this));
		Precache();

		SetPlayerUnderwater(false);

		// Train = TRAIN_NEW;

		// HackedGunPos = new Vector3(0, 32, 0);
		// BonusChallenge;

		SetThink(null);

		InitHUD = true;

		CreateViewModel();

		// more todo

		// GameRules.PlayerSpawn(this);
		LaggedMovementValue = 1.0f;

		base.Spawn();

		InitVCollision(GetAbsOrigin(), GetAbsVelocity());
	}


	public virtual void CreateViewModel(int index = 0) {
		Assert(index >= 0 && index < MAX_VIEWMODELS);

		if (GetViewModel(index) != null)
			return;

		BaseViewModel? vm = (BaseViewModel?)CreateEntityByName("viewmodel");
		if (vm != null) {
			vm.SetAbsOrigin(GetAbsOrigin());
			vm.SetOwner(this);
			vm.SetIndex(index);
			Util.DispatchSpawn(vm);
			vm.FollowEntity(this);
			ViewModel[index].Set(vm);
		}
	}

	public TimeUnit_t GetDeathTime() => DeathTime;

	public virtual void SetAnimation(PlayerAnim playerAnim) { } // todo

#if HL2_DLL
	const int AIRTIME = 7;
	const int DROWNING_DAMAGE_INITIAL = 10;
	const int DROWNING_DAMAGE_MAX = 10;
#else
	const int AIRTIME =  12;
	const int DROWNING_DAMAGE_INITIAL = 2;
	const int DROWNING_DAMAGE_MAX = 5;
#endif

	void WaterMove() {
		if ((GetMoveType() == Source.MoveType.Noclip) && GetMoveParent() == null) {
			AirFinished = gpGlobals.CurTime + AIRTIME;
			return;
		}

		if (Health < 0 || !IsAlive()) {
			UpdateUnderwaterState();
			return;
		}

		if (GetWaterLevel() != Shared.WaterLevel.Eyes || CanBreatheUnderwater()) {
			if (AirFinished < gpGlobals.CurTime)
				EmitSound("Player.DrownStart");

			AirFinished = gpGlobals.CurTime + AIRTIME;
			DrownDmgRate = DROWNING_DAMAGE_INITIAL;

			if (DrownDmg > DrownRestored) {
				bitsDamageType |= DamageType.DrownRecover;
				bitsDamageType &= ~DamageType.Drown;
				TimeBasedDamage[(int)ITBD.DrownRecover] = 0;
			}
		}
		else {
			bitsDamageType &= ~DamageType.DrownRecover;
			TimeBasedDamage[(int)ITBD.DrownRecover] = 0;

			if (AirFinished < gpGlobals.CurTime && (GetFlags() & EntityFlags.GodMode) == 0) // drown!
			{
				if (PainFinished < gpGlobals.CurTime) {
					DrownDmgRate += 1;
					if (DrownDmgRate > DROWNING_DAMAGE_MAX) {
						DrownDmgRate = DROWNING_DAMAGE_MAX;
					}

					// OnTakeDamage(TakeDamageInfo(GetContainingEntity(INDEXENT(0)), GetContainingEntity(INDEXENT(0)), DrownDmgRate, DamageType.Drown));
					PainFinished = gpGlobals.CurTime + 1;

					DrownDmg += DrownDmgRate;
				}
			}
			else
				bitsDamageType &= ~DamageType.Drown;
		}

		UpdateUnderwaterState();
	}

	public virtual bool CanBreatheUnderwater() => false;

	bool PlayerUnderwater;
	private bool IsPlayerUnderwater() => PlayerUnderwater;

	public virtual Vector3 GetAutoaimVector(float scale) {
		MathLib.AngleVectors(GetAbsAngles(), out Vector3 forward, out _, out _);
		return forward;
	}

	// todo
	public virtual void SetSuitUpdate(ReadOnlySpan<char> name, int fgroup, int noRepeat) { }
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetSuitUpdate(ReadOnlySpan<char> name, bool fgroup, int noRepeat) => SetSuitUpdate(name, fgroup ? 1 : 0, noRepeat);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetSuitUpdate(ReadOnlySpan<char> name, int fgroup, bool noRepeat) => SetSuitUpdate(name, fgroup, noRepeat ? 1 : 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetSuitUpdate(ReadOnlySpan<char> name, bool fgroup, bool noRepeat) => SetSuitUpdate(name, fgroup ? 1 : 0, noRepeat ? 1 : 0);

	TimeUnit_t LastPlayerTalkTime;
	public TimeUnit_t LastTimePlayerTalked() => LastPlayerTalkTime;
	public void NotePlayerTalked() => LastPlayerTalkTime = gpGlobals.CurTime;

	public bool CanSpeak() => true;

	public virtual void CheckChatText(ReadOnlySpan<char> text) { }

	int GetCommandContextCount() => CommandContext.Count;

	CommandContext AllocCommandContext() {
		CommandContext ctx = new();
		CommandContext.Add(ctx);

		if (CommandContext.Count > 1000)
			Assert(false);

		return ctx;
	}

	void RemoveCommandContext(int index) => CommandContext.RemoveAt(index);

	CommandContext RemoveAllCommandContextsExceptNewest() {
		int count = CommandContext.Count;
		int toRemove = count - 1;
		if (toRemove > 0)
			CommandContext.RemoveRange(0, toRemove);

		if (CommandContext.Count == 0) {
			Assert(false);
			CommandContext.Add(AllocCommandContext());
		}

		return CommandContext[0];
	}

	CommandContext GetCommandContext(int index) => CommandContext[index];

	void RemoveAllCommandContexts() => CommandContext.Clear();

	void ReplaceContextCommands(CommandContext ctx, UserCmd[] cmds, int commands) {
		ctx.Cmds.Clear();

		ctx.NumCmds = commands;
		ctx.TotalCmds = commands;
		ctx.DroppedPackets = 0;

		for (int i = commands - 1; i >= 0; --i)
			ctx.Cmds.Add(cmds[i]);
	}

	int DetermineSimulationTicks() {
		int commandContextCount = GetCommandContextCount();

		int contextNumber;
		int simulationTicks = 0;

		for (contextNumber = 0; contextNumber < commandContextCount; contextNumber++) {
			CommandContext ctx = GetCommandContext(contextNumber);
			Assert(ctx != null);
			Assert(ctx.NumCmds > 0);
			Assert(ctx.DroppedPackets >= 0);

			simulationTicks += ctx.NumCmds + ctx.DroppedPackets;
		}

		return simulationTicks;
	}

	static readonly ConVar sv_clockcorrection_msecs = new("sv_clockcorrection_msecs", "60", 0, "The server tries to keep each player's m_nTickBase withing this many msecs of the server absolute tickcount");
	static readonly ConVar sv_playerperfhistorycount = new("sv_playerperfhistorycount", "60", 0, "Number of samples to maintain in player perf history", 1.0, 128.0);

	void AdjustPlayerTimeBase(int simulationTicks) {
		Assert(simulationTicks >= 0);
		if (simulationTicks < 0)
			return;

		PlayerSimInfo? pi = null;
		if (sv_playerperfhistorycount.GetInt() > 0) {
			while (VecPlayerSimInfo.Count > sv_playerperfhistorycount.GetInt())
				VecPlayerSimInfo.RemoveAt(0);

			pi = new();
			VecPlayerSimInfo.Add(pi);
		}

		if (gpGlobals.MaxClients == 1)
			TickBase = (int)(gpGlobals.TickCount - simulationTicks + gpGlobals.SimTicksThisFrame);
		else {
			float correctionSeconds = Math.Clamp(sv_clockcorrection_msecs.GetFloat() / 1000.0f, 0.0f, 1.0f);
			int correctionTicks = TIME_TO_TICKS(correctionSeconds);

			int idealFinalTick = (int)(gpGlobals.TickCount + correctionTicks);
			int estimatedFinalTick = (int)(TickBase + simulationTicks);

			int too_fast_limit = idealFinalTick + correctionTicks;
			int too_slow_limit = idealFinalTick - correctionTicks;

			if (estimatedFinalTick > too_fast_limit || estimatedFinalTick < too_slow_limit) {
				int correctedTick = (int)(idealFinalTick - simulationTicks + gpGlobals.SimTicksThisFrame);
				pi?.TicksCorrected = correctionTicks;
				TickBase = correctedTick;
			}
		}

		pi?.FinalSimulationTime = TICKS_TO_TIME(TickBase + simulationTicks + gpGlobals.SimTicksThisFrame);
	}

	bool IsUserCmdDataValid(UserCmd cmd) {
		return true;// todo
	}

	bool ShouldRunCommandsInContext(CommandContext ctx) {
		return true; // todo
	}

	public void SnapEyeAngles(in QAngle viewAngles) {
		pl.ViewingAngle = viewAngles;
		pl.FixAngle = (int)FixAngle.Absolute;
	}

	public UserCmd GetLastUserCommand() => LastCmd; // todo BotCmd

	public void SetLastUserCommand(UserCmd cmd) => LastCmd = cmd;

	static ConVar sv_usercmd_custom_random_seed = new("1", FCvar.Cheat, "When enabled server will populate an additional random seed independent of the client");

	public void ProcessUsercmds(UserCmd[] cmds, int numcmds, int totalcmds, int dropped_packets, bool paused) {
		CommandContext ctx = AllocCommandContext();
		Assert(ctx);

		int i;
		for (i = totalcmds - 1; i >= 0; i--) {
			UserCmd cmd = cmds[totalcmds - 1 - i];

			if (!IsUserCmdDataValid(cmd))
				cmd.MakeInert();

			if (sv_usercmd_custom_random_seed.GetBool()) {
				float timeNow = (float)Platform.Time;
				cmd.ServerRandomSeed = BitConverter.SingleToInt32Bits(timeNow);
			}
			else
				cmd.ServerRandomSeed = cmd.RandomSeed;

			ctx.Cmds.Add(cmd);
		}

		ctx.NumCmds = numcmds;
		ctx.TotalCmds = totalcmds;
		ctx.DroppedPackets = dropped_packets;
		ctx.Paused = paused;

		if (ctx.Paused) {
			bool clear_angles = true;

			if (GetMoveType() == Source.MoveType.Noclip /*&& sv_cheats.GetBool() && sv_noclipduringpause.GetBool()*/)
				clear_angles = false;

			for (i = 0; i < ctx.NumCmds; i++) {
				UserCmd cm = ctx.Cmds[i];
				cm.Buttons = 0;
				if (clear_angles) {
					cm.ForwardMove = 0;
					cm.SideMove = 0;
					cm.UpMove = 0;
					MathLib.VectorCopy(pl.ViewingAngle, out cm.ViewAngles);
				}
				ctx.Cmds[i] = cm;
			}

			ctx.DroppedPackets = 0;
		}

		// GamePaused = paused;

		if (paused) {
			ForceSimulation();
			PhysicsSimulate();
		}

		// if (sv_playerperfhistorycount.GetInt() > 0) {

		// }
	}

	public void ForceSimulation() => SimulationTick = -1;

	public override void PhysicsSimulate() {
		BaseEntity? moveParent = GetMoveParent();
		moveParent?.PhysicsSimulate();

		if (SimulationTick == gpGlobals.TickCount)
			return;

		SimulationTick = gpGlobals.TickCount;

		int simulation_ticks = DetermineSimulationTicks();

		if (simulation_ticks > 0)
			AdjustPlayerTimeBase(simulation_ticks);

		TimeUnit_t savetime = gpGlobals.CurTime;
		TimeUnit_t saveframetime = gpGlobals.FrameTime;

		int command_context_count = GetCommandContextCount();

		List<UserCmd> vecAvailCommands = [];

		for (int context_number = 0; context_number < command_context_count; context_number++) {
			CommandContext ctx = GetCommandContext(context_number);
			if (!ShouldRunCommandsInContext(ctx))
				continue;

			if (ctx.Cmds.Count == 0)
				continue;

			int numbackup = ctx.TotalCmds - ctx.NumCmds;

			if (ctx.DroppedPackets < 24) {
				int droppedcmds = ctx.DroppedPackets;

				while (droppedcmds > numbackup) {
					UserCmd lastCmd = GetLastUserCommand();
					lastCmd.CommandNumber++;
					vecAvailCommands.Add(lastCmd);
					droppedcmds--;
				}

				while (droppedcmds > 0) {
					int cmdnum = ctx.NumCmds + droppedcmds - 1;
					vecAvailCommands.Add(ctx.Cmds[cmdnum]);
					droppedcmds--;
				}
			}

			for (int i = ctx.NumCmds - 1; i >= 0; i--)
				vecAvailCommands.Add(ctx.Cmds[i]);

			LastCmd = ctx.Cmds[ctx.Cmds.Count - 1];
		}

		int commandLimit = IsSimulatingOnAlternateTicks() ? 2 : 1;
		int commandsToRun = vecAvailCommands.Count;
		if (gpGlobals.SimTicksThisFrame >= commandLimit && vecAvailCommands.Count > commandLimit) {
			int commandsToRollOver = Math.Min(vecAvailCommands.Count, (gpGlobals.SimTicksThisFrame - 1));
			commandsToRun = vecAvailCommands.Count - commandsToRollOver;
			Assert(commandsToRun >= 0);
			if (commandsToRollOver > 0) {
				CommandContext ctx = RemoveAllCommandContextsExceptNewest();
				ReplaceContextCommands(ctx, [.. vecAvailCommands.GetRange(vecAvailCommands.Count - commandsToRollOver, commandsToRollOver)], commandsToRollOver);
			}
			else
				RemoveAllCommandContexts();
		}
		else
			RemoveAllCommandContexts();

		TimeUnit_t vphysicsArrivalTime = TICK_INTERVAL;

		int numUsrCmdProcessTicksMax = 0; // sv_maxusrcmdprocessticks.GetInt();
		if (gpGlobals.MaxClients != 1 && numUsrCmdProcessTicksMax > 0) {
			MovementTimeForUserCmdProcessingRemaining += TICK_INTERVAL;

			if (MovementTimeForUserCmdProcessingRemaining > numUsrCmdProcessTicksMax * TICK_INTERVAL)
				MovementTimeForUserCmdProcessingRemaining = numUsrCmdProcessTicksMax * TICK_INTERVAL;
		}
		else
			MovementTimeForUserCmdProcessingRemaining = float.MaxValue;

		if (commandsToRun > 0) {
			LastUserCommandTime = savetime;

			MoveHelperServer.s_MoveHelperServer.SetHost(this);

			if (IsPredictingWeapons())
				IPredictionSystem.SuppressHostEvents(this);

			for (int i = 0; i < commandsToRun; ++i) {
				PlayerRunCommand(vecAvailCommands[i], MoveHelperServer.s_MoveHelperServer);

				// if (PhysicsController != null) { // todo
				// 	// UpdateVPhysicsPosition(vNewVPhysicsPosition, vNewVPhysicsVelocity, vphysicsArrivalTime);
				// 	vphysicsArrivalTime += TICK_INTERVAL;
				// }
			}

			IPredictionSystem.SuppressHostEvents(null);

			MoveHelperServer.s_MoveHelperServer.SetHost(null);

			if (VecPlayerSimInfo.Count > 0) {
				PlayerSimInfo pi = VecPlayerSimInfo[VecPlayerSimInfo.Count - 1];
				pi.Time = Platform.Time;
				pi.AbsOrigin = GetAbsOrigin();
				pi.GameSimulationTime = gpGlobals.CurTime;
				pi.NumCmds = commandsToRun;
			}
		}
		else if (GetTimeSinceLastUserCommand() > 3.0f /*sv_player_usercommand_timeout.GetFloat()*/)
			RunNullCommand();

		gpGlobals.CurTime = savetime;
		gpGlobals.FrameTime = saveframetime;
	}

	private void RunNullCommand() {
		UserCmd cmd = new();

		TimeUnit_t oldFrameTime = gpGlobals.FrameTime;
		TimeUnit_t oldCurTime = gpGlobals.CurTime;

		pl.FixAngle = (int)FixAngle.None;

		cmd.ViewAngles = EyeAngles();

		TimeUnit_t timeBase = gpGlobals.CurTime;
		SetTimeBase(timeBase);

		MoveHelperServer.s_MoveHelperServer.SetHost(this);
		PlayerRunCommand(cmd, MoveHelperServer.s_MoveHelperServer);

		SetLastUserCommand(cmd);

		gpGlobals.FrameTime = oldFrameTime;
		gpGlobals.CurTime = oldCurTime;

		MoveHelperServer.s_MoveHelperServer.SetHost(null);
	}

	public void SetTimeBase(double timeBase) => TickBase = TIME_TO_TICKS(timeBase);

	private class UserCmdRef
	{
		public UserCmd Cmd;
		public AnonymousSafeFieldPointer<UserCmd> Ptr => new(this, static o => ref ((UserCmdRef)o).Cmd);
	}

	bool TouchedPhysObject;

	static readonly ConVar xc_crouch_debounce = new("xc_crouch_debounce", "0", 0);

	public bool GetToggledDuckState() => DuckToggled;
	public void ToggleDuck() {
		DuckToggled = !DuckToggled;
	}

	public virtual void PlayerRunCommand(UserCmd userCmd, IMoveHelper s_MoveHelperServer) {
		TouchedPhysObject = false;

		if (pl.FixAngle == (int)FixAngle.None)
			MathLib.VectorCopy(userCmd.ViewAngles, out pl.ViewingAngle);

		// Handle FL_FROZEN.
		// Prevent player moving for some seconds after New Game, so that they pick up everything
		if ((GetFlags() & EntityFlags.Frozen) != 0 || (developer.GetInt() == 0 && gpGlobals.LoadType == MapLoadType.NewGame && gpGlobals.CurTime < 3.0)) {
			userCmd.ForwardMove = 0;
			userCmd.SideMove = 0;
			userCmd.UpMove = 0;
			userCmd.Buttons = 0;
			userCmd.Impulse = 0;
			MathLib.VectorCopy(pl.ViewingAngle, out userCmd.ViewAngles);
		}
		else {
			// Force a duck if we're toggled
			if (GetToggledDuckState()) {
				// If this is set, we've altered our menu options and need to debounce the duck
				if (xc_crouch_debounce.GetBool()) {
					ToggleDuck();

					// Mark it as handled
					xc_crouch_debounce.SetValue(0);
				}
				else {
					userCmd.Buttons |= InButtons.Duck;
				}
			}
		}

		g_PlayerMove.RunCommand(this, new UserCmdRef { Cmd = userCmd }.Ptr, s_MoveHelperServer);
	}

	public bool IsPredictingWeapons() => false; // todo

	TimeUnit_t GetTimeSinceLastUserCommand() => /*(!IsConnected() || IsFakeClient() || IsBot()) ? 0.0f :*/ gpGlobals.CurTime - LastUserCommandTime;

	public virtual void SetupVisibility(BaseEntity? viewEntity, byte[] pvs, int pvssize) {
		if (viewEntity != null)
			return;

		Vector3 org = EyePosition();

#if GMOD_DLL
		BaseEntity? observerTarget = GetObserverTarget();
		if (observerTarget != null)
			engine.AddOriginToPVS(observerTarget.EyePosition());
#endif

		engine.AddOriginToPVS(org);
	}

	public override EdictFlags UpdateTransmitState() => SetTransmitState(EdictFlags.FullCheck);

	public override EdictFlags ShouldTransmit(CheckTransmitInfo info) {
		if (info.ClientEnt == Edict())
			return EdictFlags.Always;

		// todo

		return base.ShouldTransmit(info);
	}

	public virtual bool WantsLagCompensationOnEntity(BaseEntity entity, in UserCmd cmd, ref readonly MaxEdictsBitSet entityTransmitBits) {
		if (!Unsafe.IsNullRef(in entityTransmitBits) && !entityTransmitBits.IsBitSet(entity.EntIndex()))
			return false;

		return true;
	}

	public ref UserCmd GetCurrentUserCommand() => ref CurrentCommand.IsNull ? ref Unsafe.NullRef<UserCmd>() : ref CurrentCommand.Get();

	public int Frags;
	public int Deaths;

	public TimeUnit_t NextDecalTime;

	public int FragCount() => Frags;
	public int DeathCount() => Deaths;
	public bool IsConnected() => Connected != PlayerConnectedState.Disconnected;
	public bool IsDisconnecting() => Connected == PlayerConnectedState.Disconnecting;
	public bool IsSuitEquipped() => Local.WearingSuit;

	public virtual void ChangeTeam(int teamNum, bool autoTeam = false, bool silent = false, bool autoBalance = false) {
		if (GetGlobalTeam(teamNum) == null) {
			Warning($"CBasePlayer::ChangeTeam( {teamNum} ) - invalid team index.\n");
			return;
		}

		if (teamNum == GetTeamNumber())
			return;

		IGameEvent? ev = gameeventmanager.CreateEvent("player_team");
		if (ev != null) {
			ev.SetInt("userid", GetUserID());
			ev.SetInt("team", teamNum);
			ev.SetInt("oldteam", GetTeamNumber());
			ev.SetInt("disconnect", IsDisconnecting() ? 1 : 0);
			ev.SetInt("autoteam", autoTeam ? 1 : 0);
			ev.SetInt("silent", silent ? 1 : 0);
			ev.SetString("name", GetPlayerName());

			gameeventmanager.FireEvent(ev);
		}

		GetTeam()?.RemovePlayer(this);

		if (teamNum != 0)
			GetGlobalTeam(teamNum)!.AddPlayer(this);

		base.ChangeTeam(teamNum);
	}

	public override Vector3 GetSmoothedVelocity() {
		if (IsInAVehicle())
			return GetVehicle()!.GetVehicleEnt()!.GetSmoothedVelocity();
		return SmoothedVelocity;
	}

	const float SMOOTHING_FACTOR = 0.9f;
	public virtual void PostThink() {
		SmoothedVelocity = SmoothedVelocity * SMOOTHING_FACTOR + GetAbsVelocity() * (1 - SMOOTHING_FACTOR);

		if (!g_fGameOver /*&& !PlayerLocked*/) {
			if (IsAlive()) {
				if ((GetFlags() & EntityFlags.Ducking) != 0)
					SetCollisionBounds(VEC_DUCK_HULL_MIN, VEC_DUCK_HULL_MAX);
				else
					SetCollisionBounds(VEC_HULL_MIN, VEC_HULL_MAX);

				// Handle controlling an entity
				if (UseEntity.Get() != null) {
					// if they've moved too far from the gun, or deployed another weapon, unuse the gun
					if (UseEntity.Get()!.OnControls(this) && (GetActiveWeapon() == null || GetActiveWeapon()!.IsEffectActive(EntityEffects.NoDraw) || (GetActiveWeapon()!.GetActivity() == Activity.ACT_VM_HOLSTER))) {
						UseEntity.Get()!.Use(this, this, UseType.Set, 2);    // try fire the gun
					}
					else {
						// they've moved off the controls
						ClearUseEntity();
					}
				}

				ItemPostFrame();

				if ((GetFlags() & EntityFlags.OnGround) != 0) {
					// if (Local.FallVelocity > 64 && !g_pGameRules.IsMultiplayer())
					// SoundEnt.InsertSound(SOUND_PLAYER, GetAbsOrigin(), m_Local.m_flFallVelocity, 0.2, this);
					Local.FallVelocity = 0;
				}


				if (IsInAVehicle())
					SetAnimation(PlayerAnim.InVehicle);
				else if (GetAbsVelocity().X == 0 && GetAbsVelocity().Y == 0)
					SetAnimation(PlayerAnim.Idle);
				else if ((GetAbsVelocity().X != 0 || GetAbsVelocity().Y != 0) && (GetFlags() & EntityFlags.OnGround) != 0)
					SetAnimation(PlayerAnim.Walk);
				else if (GetWaterLevel() > (WaterLevel)1)
					SetAnimation(PlayerAnim.Walk);
			}

			if (GetSequence() == -1)
				SetSequence(0);

			StudioFrameAdvance();
			// DispatchAnimEvents(this);
			SetSimulationTime(gpGlobals.CurTime);
			// Weapon_FrameUpdate();
			// UpdatePlayerSound();

			// if (ForceOrigin) {
			// 	SetLocalOrigin(ForcedOrigin);
			// 	SetLocalAngles(Local.PunchAngle);
			// 	// Local.PunchAngle = RandomAngle(-25, 25);
			// 	Local.PunchAngleVel.Init();
			// }

			PostThinkVPhysics();
		}

		SimulatePlayerSimulatedEntities();
	}

	IPhysicsPlayerController? PhysicsController;

	public void InitVCollision(in Vector3 vecAbsOrigin, in Vector3 vecAbsVelocity) {
		VPhysicsDestroyObject();

		PhysCollide model = physcollision.BBoxToCollide(GetPlayerMins(), GetPlayerMaxs());

		ObjectParams solid = g_PhysDefaultObjectParams;
		solid.GameData = this;
		solid.Name = "player";
		solid.Mass = 85.0f;
		solid.Inertia = 1e24f;
		solid.EnableCollisions = true;
		solid.DragCoefficient = 0;

		IPhysicsObject? shadow = physenv.CreatePolyObject(model, (int)physprops.GetSurfaceIndex("player"), vecAbsOrigin, vec3_angle, ref solid);
		if (shadow == null)
			return;

		VPhysicsSetObject(shadow);
		PhysAddShadow(this);

		PhysicsController = physenv.CreatePlayerController(shadow);
		PhysicsController.SetPushMassLimit(350.0f);
		PhysicsController.SetPushSpeedLimit(50.0f);

		shadow.SetPosition(vecAbsOrigin, vec3_angle, true);
	}

	bool ShadowCollisionsDisabled;

	void UpdateVPhysicsShadowState() {
		IPhysicsObject? shadow = VPhysicsGetObject();
		if (shadow == null)
			return;

		MoveType mt = (MoveType)GetMoveType();
		bool noclip = mt == Source.MoveType.Noclip || mt == Source.MoveType.Observer;
		if (noclip != ShadowCollisionsDisabled) {
			ShadowCollisionsDisabled = noclip;
			shadow.EnableCollisions(!noclip);
		}
	}

	public void PostThinkVPhysics() {
		if (PhysicsController == null)
			return;

		UpdateVPhysicsShadowState();

		bool onground = (GetFlags() & EntityFlags.OnGround) != 0;
		IPhysicsObject? ground = GetGroundEntity()?.VPhysicsGetObject();
		PhysicsController.Update(GetAbsOrigin(), GetAbsVelocity(), (float)gpGlobals.FrameTime, onground, ground!);
	}

	public float GeigerRange = 1000;     // range to nearest radiation source
	public TimeUnit_t GeigerDelay;      // delay per update of range msg to client
	public int GeigerRangePrev = 1000;

	const float GEIGERDELAY = 0.25f;

	public void UpdateGeigerCounter() {
		byte range;

		// delay per update ie: don't flood net with these msgs
		if (gpGlobals.CurTime < GeigerDelay)
			return;

		GeigerDelay = gpGlobals.CurTime + GEIGERDELAY;

		// send range to radition source to client
		range = (byte)Math.Clamp((int)MathF.Floor(GeigerRange / 4), 0, 255);

		// This is to make sure you aren't driven crazy by geiger while in the airboat
		if (IsInAVehicle())
			range = (byte)Math.Clamp((int)range * 4, 0, 255);

		if (range != GeigerRangePrev) {
			GeigerRangePrev = range;

			SingleUserRecipientFilter user = new(this);
			user.MakeReliable();
			UserMessageBegin(user, "Geiger");
			MessageWriteByte(range);
			MessageEnd();
		}

		// reset counter and semaphore
		if (random.RandomInt(0, 3) == 0)
			GeigerRange = 1000;
	}

	public void CheckSuitUpdate() {
		// Ignore suit updates if no suit
		if (!IsSuitEquipped())
			return;

		// if in range of radiation source, ping geiger counter
		UpdateGeigerCounter();

		if (g_pGameRules.IsMultiplayer()) {
			// don't bother updating HEV voice in multiplayer.
			return;
		}
	}

	public void NotifyNearbyRadiationSource(float range) {
		// if player's current geiger counter range is larger
		// than range to this trigger hurt, reset player's
		// geiger counter range

		if (GeigerRange >= range)
			GeigerRange = range;
	}

	public virtual void PreThink() {
		if (g_fGameOver /*|| PlayerLocked*/)
			return;

		ItemPreFrame();
		WaterMove();

		// if (g_pGameRules && g_pGameRules.FAllowFlashlight())
		// 	Local.HideHUD &= ~HideHudBits.Flashlight;
		// else
		// 	Local.HideHUD |= HideHudBits.Flashlight;

		UpdateClientData();
		// CheckTimeBasedDamage();
		CheckSuitUpdate();

		// if (GetObserverMode() > Shared.ObserverMode.FreezeCam)
		// 	CheckObserverSettings();

		// if (GetLifeState() >= LifeState.Dying) {
		// 	// UpdateLastKnownArea();
		// 	return;
		// }

		// HandleFuncTrain();

		if ((Buttons & InButtons.Jump) != 0)
			Jump();

		if ((Buttons & InButtons.Duck) != 0 || (GetFlags() & EntityFlags.Ducking) != 0 /*|| (m_afPhysicsFlags & PFLAG_DUCKING)*/)
			Duck();

		if ((GetFlags() & EntityFlags.OnGround) == 0)
			Local.FallVelocity = -GetAbsVelocity().Z;

		// UpdateLastKnownArea();
	}

	bool InitHUD;
	public virtual void UpdateClientData() {
		SingleUserRecipientFilter user = new(this);
		user.MakeReliable();

		if (InitHUD) {
			InitHUD = false;
			// gInitHUD = false;

			UserMessageBegin(user, "ResetHUD");
			WRITE_BYTE(0);
			MessageEnd();

			World? world = GetWorldEntity();
			// todo
		}

		// todo

		// g_pGameRules.UpdateClientData(this);
	}

	private void Duck() {

	}

	private void Jump() {

	}

	internal bool IsDead() {
		// throw new NotImplementedException();
		return false;// todo
	}

	public virtual void ForceDropOfCarriedPhysObjects(BaseEntity? ground) { }

	private Action? FnThink;
	internal void Think() => FnThink?.Invoke();
	// todo: thinking about thinking... lots of thinking

	public Activity GetActivity() { return Activity.ACT_IDLE; } // TODO

	internal int GetUserID() => engine.GetPlayerUserId(Edict());

	public bool GetSteamID(out Steamworks.CSteamID id) {
		Steamworks.CSteamID? steamID = engine.GetClientSteamID(Edict());
		id = steamID ?? default;
		return steamID != null;
	}

	internal ReadOnlySpan<char> GetNetworkIDString() {
		ReadOnlySpan<char> str = engine.GetPlayerNetworkIDString(Edict());
		int l = strcpy(NetworkIDString, str);
		return NetworkIDString.AsSpan()[..l];
	}

	TimeUnit_t NextSuicideTime;
	int SuicideCustomKillFlags;

	public virtual float GetHeldObjectMass(IPhysicsObject? heldObject) => 0;

	internal void CommitSuicide(bool explode = false, bool force = false) {
		if (!IsAlive())
			return;

		// prevent suiciding too often
		if (NextSuicideTime > gpGlobals.CurTime && !force)
			return;

		// don't let them suicide for 5 seconds after suiciding
		NextSuicideTime = gpGlobals.CurTime + 5;

		DamageType fDamage = DamageType.PreventPhysicsForce | (explode ? (DamageType.Blast | DamageType.AlwaysGib) : DamageType.NeverGib);

		// have the player kill themself
		Health = 0;
		TakeDamageInfo info = new(this, this, 0, fDamage, SuicideCustomKillFlags);
		Event_Killed(info);
		Event_Dying(info);
		SuicideCustomKillFlags = 0;
	}

	public static int TrainSpeed(int speed, int max) {
		float fSpeed, fMax;
		int ret = 0;

		fMax = (float)max;
		fSpeed = speed;

		fSpeed = fSpeed / fMax;

		if (speed < 0)
			ret = FuncTrackTrain.TRAIN_BACK;
		else if (speed == 0)
			ret = FuncTrackTrain.TRAIN_NEUTRAL;
		else if (fSpeed < 0.33)
			ret = FuncTrackTrain.TRAIN_SLOW;
		else if (fSpeed < 0.66)
			ret = FuncTrackTrain.TRAIN_MEDIUM;
		else
			ret = FuncTrackTrain.TRAIN_FAST;

		return ret;
	}

	public virtual void ObserverUse(bool isPressed) { }

	public virtual void PlayUseDenySound() { }

	public virtual void PickupObject(BaseEntity obj, bool limitMassAndSize = true) { }

	public void SetUseEntity(BaseEntity? useEntity) => UseEntity.Set(useEntity);
	public BaseEntity? GetUseEntity() => UseEntity.Get();

	public static bool CanPickupObject(BaseEntity? obj, float massLimit, float sizeLimit) {
		// UNDONE: Make this virtual and move to HL2 player
		//Must be valid
		if (obj == null)
			return false;

		//Must move with physics
		if (obj.GetMoveType() != Source.MoveType.VPhysics)
			return false;

		IPhysicsObject[] list = ArrayPool<IPhysicsObject>.Shared.Rent(VPHYSICS_MAX_OBJECT_LIST_COUNT);
		try {
			int count = obj.VPhysicsGetObjectList(list);

			//Must have a physics object
			if (count == 0)
				return false;

			float objectMass = 0;
			bool checkEnable = false;
			for (int i = 0; i < count; i++) {
				objectMass += list[i].GetMass();
				if (!list[i].IsMoveable())
					checkEnable = true;
				if ((list[i].GetGameFlags() & Source.Common.Physics.PhysicsFlags.NoPlayerPickup) != 0)
					return false;
				if (list[i].IsHinged())
					return false;
			}

			//Must be under our threshold weight
			if (massLimit > 0 && objectMass > massLimit)
				return false;

			if (checkEnable) {
				// Allowing picking up of bouncebombs.
				// CBounceBomb *pBomb = dynamic_cast<CBounceBomb*>(pObject);
				// if( pBomb )
				// 	return true;

				// Allow pickup of phys props that are motion enabled on player pickup
				PhysicsProp? prop = obj as PhysicsProp;
				PhysBox? box = obj as PhysBox;
				if (prop == null && box == null)
					return false;

				if (prop != null && !(prop.HasSpawnFlags(BreakableProp.SF.EnableOnPhyscannon)))
					return false;

				if (box != null && !(box.HasSpawnFlags(PhysBox.SF.EnableOnPhyscannon)))
					return false;
			}

			if (sizeLimit > 0) {
				ref readonly Vector3 size = ref obj.CollisionProp().OBBSize();
				if (size.X > sizeLimit || size.Y > sizeLimit || size.Z > sizeLimit)
					return false;
			}

			return true;
		}
		finally {
			ArrayPool<IPhysicsObject>.Shared.Return(list, true);
		}
	}

	/// <summary>
	/// Return true if this object can be +used by the player
	/// </summary>
	public virtual bool IsUseableEntity(BaseEntity? entity, EntityCapabilities requiredCaps) {
		if (entity != null) {
			EntityCapabilities caps = entity.ObjectCaps();
			if ((caps & (EntityCapabilities.ImpulseUse | EntityCapabilities.ContinuousUse | EntityCapabilities.OnOffUse | EntityCapabilities.DirectionalUse)) != 0) {
				if ((caps & requiredCaps) == requiredCaps)
					return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Perhaps a poorly-named function. This function traces against the supplied
	/// NPC's hitboxes (instead of hull). If the trace hits a different NPC, the
	/// new NPC is selected. Otherwise, the supplied NPC is determined to be the
	/// one the citizen wants. This function allows the selection of a citizen over
	/// another citizen's shoulder, which is impossible without tracing against
	/// hitboxes instead of the hull (sjb)
	/// </summary>
	public BaseEntity? DoubleCheckUseNPC(BaseEntity? npc, in Vector3 src, in Vector3 dir) {
		Util.TraceLine(src, src + dir * 1024, Mask.Shot, this, Source.CollisionGroup.None, out Trace tr);

		if (tr.Ent != null && tr.Ent.MyNPCPointer() != null && tr.Ent != npc) {
			// Player is selecting a different NPC through some negative space
			// in the first NPC's hitboxes (between legs, over shoulder, etc).
			return tr.Ent;
		}

		return npc;
	}

	public bool ClearUseEntity() {
		if (UseEntity.Get() != null) {
			// Stop controlling the train/object
			// TODO: Send HUD Update
			UseEntity.Get()!.Use(this, this, UseType.Off, 0);
			UseEntity.Set(null);
			return true;
		}

		return false;
	}

	public override void Event_Killed(in TakeDamageInfo info) {
		ref WorldSoundInstance sound = ref Unsafe.NullRef<WorldSoundInstance>();

		// todo: Hints()?.ResetHintTimers();

		g_pGameRules.PlayerKilled(this, info);

		// todo: gamestats.Event_PlayerKilled(this, info);

		// todo: RumbleEffect(RUMBLE_STOP_ALL, 0, RUMBLE_FLAGS_NONE);

		ClearUseEntity();

		// this client isn't going to be thinking for a while, so reset the sound until they respawn
		sound = ref SoundEnt.SoundPointerForIndex(SoundEnt.ClientSoundIndex(Edict()));
		if (!Unsafe.IsNullRef<WorldSoundInstance>(ref sound))
			sound.Reset();

		// don't let the status bar glitch for players with <0 health.
		if (Health < -99)
			Health = 0;

		// holster the current weapon
		GetActiveWeapon()?.Holster();

		SetAnimation(PlayerAnim.Die);

		if (!IsObserver())
			SetViewOffset(VEC_DEAD_VIEWHEIGHT_SCALED(this));

		LifeState = (int)Source.LifeState.Dying;

		pl.DeadFlag = true;
		AddSolidFlags(SolidFlags.NotSolid);
		// force contact points to get flushed if no longer valid
		// UNDONE: Always do this on RecheckCollisionFilter() ?
		IPhysicsObject? pObject = VPhysicsGetObject();
		pObject?.RecheckContactPoints();

		SetMoveType(Source.MoveType.FlyGravity);
		SetGroundEntity(null);

		// clear out the suit message cache so we don't keep chattering
		SetSuitUpdate(null, false, 0);

		// reset FOV
		SetFOV(this, 0);

		if (FlashlightIsOn())
			FlashlightTurnOff();

		DeathTime = gpGlobals.CurTime;

		ClearLastKnownArea();
		base.Event_Killed(in info);
	}

	public virtual void SetFlashlightEnabled() { }
	public virtual bool FlashlightIsOn() => false;
	public virtual void FlashlightTurnOn() { }
	public virtual void FlashlightTurnOff() { }
	public virtual bool IsIlluminatedByFlashlight(BaseEntity entity, ref float _) => false;

	public void Event_Dying(in TakeDamageInfo info) { }

	readonly char[] NetworkIDString = new char[Constants.MAX_NETWORKID_LENGTH];
}

// Something to keep in mind; in base Source, this is stored in a list in the player with value semantics...
// may be worth trying to turn this into a struct one day, review GC usage (? if this is even a problem,
// just happened to notice it)
class CommandContext
{
	public readonly List<UserCmd> Cmds = [];
	public int NumCmds;
	public int TotalCmds;
	public int DroppedPackets;
	public bool Paused;
}

class PlayerSimInfo
{
	public TimeUnit_t Time;
	public int NumCmds;
	public int TicksCorrected;
	public TimeUnit_t FinalSimulationTime;
	public TimeUnit_t GameSimulationTime;
	public TimeUnit_t ServerFrameTime;
	public Vector3 AbsOrigin;
}
