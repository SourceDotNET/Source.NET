using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;
using Source.Engine;

using Steamworks;

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;

using FIELD = Source.FIELD<Game.Client.C_BasePlayer>;

namespace Game.Client;

public class C_CommandContext
{
	public bool NeedsProcessing;
	public UserCmd Cmd;
	public int CommandNumber;

	public static AnonymousSafeFieldPointer<UserCmd> SafeCmdPointer(C_CommandContext ctx) => new(ctx, FetchCmdRef);
	static ref UserCmd FetchCmdRef(object owner) => ref ((C_CommandContext)owner).Cmd;
}

public struct C_PredictionError
{
	public TimeUnit_t Time;
	public Vector3 Error;
}



[NetworkName("CBasePlayer")]
public partial class C_BasePlayer : C_BaseCombatCharacter, IGameEventListener2
{
	const int FLASHLIGHT_DISTANCE = 1000;

	public override ShadowType ShadowCastType() => ShadowType.None;

	static readonly DynamicAccessor DA_ViewOffset = FIELD.OF(nameof(ViewOffset));
	public readonly InterpolatedVar<Vector3> IV_ViewOffset = new($"{nameof(C_BasePlayer)}.{nameof(IV_ViewOffset)}");

	public C_BasePlayer() {
		AddVar(this, DA_ViewOffset, IV_ViewOffset, LatchFlags.LatchSimulationVar, true);
	}

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		new(nameof(Local)) { Embedded = true },
		new(nameof(pl)) { Embedded = true },
		nameof(FOV),
		nameof(ZoomOwner),
		new(nameof(FOVTime)) { Type = FieldType.Double },
		nameof(FOVStart),
		nameof(Vehicle),
		new(nameof(Maxspeed)) { Type = FieldType.Float, Tolerance = 0.5f },
		new(nameof(Health)) { Type = FieldType.Integer },
		new(nameof(NextThinkTick)) { Type = FieldType.Integer },
		nameof(LifeState),
		new(nameof(WaterLevel)) { Flags = FieldTypeDescFlags.InSendTable },
		new(nameof(BaseVelocity)) { Type = FieldType.Vector, Tolerance = 0.05f },
		new(nameof(Buttons)) { Type = FieldType.Integer },
		new(nameof(WaterJumpTime)) { Type = FieldType.Float },
		new(nameof(Impulse)) { Type = FieldType.Integer },
		new(nameof(StepSoundTime)) { Type = FieldType.Float },
		new(nameof(SwimSoundTime)) { Type = FieldType.Float },
		new(nameof(LadderNormal)) { Type = FieldType.Vector },
		new(nameof(AfButtonLast)) { Type = FieldType.Integer },
		new(nameof(AfButtonPressed)) { Type = FieldType.Integer },
		new(nameof(AfButtonReleased)) { Type = FieldType.Integer },
		nameof(LastWeapon),
		new(nameof(TickBase)) { Type = FieldType.Integer },
		nameof(GroundEntity),
		nameof(ViewModel),
		new(nameof(SurfaceFriction)) { Type = FieldType.Float },
		nameof(ButtonsArray),
		nameof(UseWeaponsInVehicle),
	];

	public Source.InlineArray5<byte> ButtonsArray;

	public override bool IsPlayer() => true;
	public override Mask PhysicsSolidMaskForEntity() => Mask.PlayerSolid;
	public TimeUnit_t GetFinalPredictedTime() => FinalPredictedTick * TICK_INTERVAL;
	public bool IsLocalPlayer() => GetLocalPlayer() == this;

	static EUniverse SteamIDUniverse = EUniverse.k_EUniverseInvalid;
	public bool GetSteamID(out CSteamID id) {
		id = default;
		if (engine.GetPlayerInfo(EntIndex(), out PlayerInfo pi)) {
			if (pi.FriendsID != 0 && SteamAPI.IsSteamRunning()) {
				if (SteamIDUniverse == EUniverse.k_EUniverseInvalid)
					SteamIDUniverse = SteamUtils.GetConnectedUniverse();
				id.InstancedSet(new AccountID_t(pi.FriendsID), 1, SteamIDUniverse, EAccountType.k_EAccountTypeIndividual);
				return true;
			}
		}
		return false;
	}
	PlayerInfo NetworkIDInfo;
	InlineArray33<char> NetworkIDString;
	public ReadOnlySpan<char> GetNetworkIDString() {
		if (engine.GetPlayerInfo(EntIndex(), out NetworkIDInfo)) {
			if (NetworkIDInfo.FakePlayer)
				return "BOT";
			uint friendsID = NetworkIDInfo.FriendsID;
			if (friendsID != 0)
				sprintf(NetworkIDString, "STEAM_0:%u:%u").U(friendsID & 1).U(friendsID >> 1);
			else
				for (int i = 0; i < 33; i++)
					NetworkIDString[i] = (char)NetworkIDInfo.GUID[i];
			return ((ReadOnlySpan<char>)NetworkIDString).SliceNullTerminatedString();
		}
		return "NULL";
	}
	public bool IsHLTV() => IsLocalPlayer() && engine.IsHLTV();
	public bool IsBot() => false; // TODO, gmod
	public static bool ShouldDrawLocalPlayer() {
		return input.CAM_IsThirdPerson(); // todo
	}

	public override bool ShouldDraw() {
		if (IsLocalPlayer())
			return ShouldDrawLocalPlayer();

		return base.ShouldDraw();
	}

	public InButtons AfButtonLast;
	public InButtons AfButtonPressed;
	public InButtons AfButtonReleased;
	public InButtons Buttons;
	public AnonymousSafeFieldPointer<UserCmd> CurrentCommand;
	public Vector3 WaterJumpVel;
	public TimeUnit_t WaterJumpTime;
	public TimeUnit_t StepSoundTime;
	public TimeUnit_t SwimSoundTime;
	public Vector3 LadderNormal;
	public int Impulse;
	bool WasFrozen;
	int Physics;
	bool FiredWeapon;




	public bool HasFiredWeapon() => FiredWeapon;
	public void SetFiredWeapon(bool flag) => FiredWeapon = flag;
	public bool IsObserver() => GetObserverMode() != Shared.ObserverMode.None;
	public virtual bool IsAllowedToSwitchWeapons() => !IsObserver();

	public bool IsPlayerDead() => pl.DeadFlag == true;

	public Source.Common.MaterialSystem.IMaterial? GetHeadLabelMaterial() {
		if (GetClientVoiceMgr() == null)
			return null;

		return GetClientVoiceMgr().GetHeadLabelMaterial();
	}

	public bool AudioStateIsUnderwater(Vector3 mainViewOrigin) {
		if (IsObserver()) {
			Contents cont = enginetrace.GetPointContents(mainViewOrigin, out _);
			return (cont & (Contents)Mask.Water) != 0;
		}

		return GetWaterLevel() >= Shared.WaterLevel.Eyes;
	}

	[NetworkTable("DT_LocalPlayerExclusive")]
	public static readonly NetworkProp[] LocalPlayerExclusiveProps = [
		new(nameof(Local)) { Encoding = NetEncoding.DataTable, DataTable = "DT_Local" },
		new(nameof(ViewOffset)) { Encoding = NetEncoding.VectorElements },
		nameof(Friction),
		nameof(Ammo),
		nameof(TickBase),
		nameof(NextThinkTick),
		nameof(LastWeapon),
		nameof(GroundEntity),
		nameof(BaseVelocity),
		nameof(ConstraintEntity),
		nameof(ConstraintCenter),
		nameof(ConstraintRadius),
		nameof(ConstraintWidth),
		nameof(ConstraintSpeedFactor),
		new(nameof(DeathTime)) { Encoding = NetEncoding.Float },
		new(nameof(LaggedMovementValue)) { Encoding = NetEncoding.Float },
		nameof(TonemapController),
		nameof(ViewEntity),
		nameof(DisableWorldClicking),
	];

	public virtual void ItemPreFrame() { }

	public virtual void UpdateClientData() {
		for (int i = 0; i < WeaponCount(); i++) {
			if (GetWeapon(i) != null)  // each item updates it's successors
				GetWeapon(i).UpdateClientData(this);
		}
	}
	FogParams CurrentFog = new();
	Handle<BaseEntity> OldFogController = new();

	public ref FogParams GetFogParams() => ref CurrentFog;

	public override void OnPreDataChanged(DataUpdateType updateType) {
		for (int i = 0; i < MAX_AMMO_TYPES; ++i)
			OldAmmo[i] = GetAmmoCount(i);

		WasFreezeFraming = GetObserverMode() == Shared.ObserverMode.FreezeCam;
		OldFogController = Local.PlayerFog.Ctrl;

		base.OnPreDataChanged(updateType);
	}

	public void FogControllerChanged(bool snap) {
		if (Local.PlayerFog.Ctrl.Get() is Game.Server.C_FogController ctrl) {
			ref FogParams fogParams = ref ctrl.Fog;

			Local.PlayerFog.OldColor = CurrentFog.ColorPrimary;
			Local.PlayerFog.OldStart = CurrentFog.Start;
			Local.PlayerFog.OldEnd = CurrentFog.End;

			Local.PlayerFog.NewColor = fogParams.ColorPrimary;
			Local.PlayerFog.NewStart = fogParams.Start;
			Local.PlayerFog.NewEnd = fogParams.End;

			Local.PlayerFog.TransitionTime = snap ? -1 : gpGlobals.CurTime;

			CurrentFog = fogParams;

			UpdateFogController();
		}
	}

	public virtual void UpdateFogController() {
		if (Local.PlayerFog.Ctrl.Get() is Game.Server.C_FogController ctrl) {
			if (Local.PlayerFog.TransitionTime == -1 && (OldFogController == Local.PlayerFog.Ctrl))
				CurrentFog = ctrl.Fog;
		}
		else {
			if (CurrentFog.FarZ != -1 || CurrentFog.Enable != false) {
				CurrentFog.FarZ = -1;
				CurrentFog.Enable = false;
			}
		}

		UpdateFogBlend();
	}

	public void UpdateFogBlend() {
		if (Local.PlayerFog.TransitionTime != -1) {
			TimeUnit_t timeDelta = gpGlobals.CurTime - Local.PlayerFog.TransitionTime;
			if (timeDelta < CurrentFog.Duration) {
				float scale = (float)(timeDelta / CurrentFog.Duration);
				CurrentFog.ColorPrimary.R = (byte)((Local.PlayerFog.NewColor.R * scale) + (Local.PlayerFog.OldColor.R * (1.0f - scale)));
				CurrentFog.ColorPrimary.G = (byte)((Local.PlayerFog.NewColor.G * scale) + (Local.PlayerFog.OldColor.G * (1.0f - scale)));
				CurrentFog.ColorPrimary.B = (byte)((Local.PlayerFog.NewColor.B * scale) + (Local.PlayerFog.OldColor.B * (1.0f - scale)));
				CurrentFog.Start = (Local.PlayerFog.NewStart * scale) + (Local.PlayerFog.OldStart * (1.0f - scale));
				CurrentFog.End = (Local.PlayerFog.NewEnd * scale) + (Local.PlayerFog.OldEnd * (1.0f - scale));
			}
			else {
				CurrentFog.ColorPrimary.R = Local.PlayerFog.NewColor.R;
				CurrentFog.ColorPrimary.G = Local.PlayerFog.NewColor.G;
				CurrentFog.ColorPrimary.B = Local.PlayerFog.NewColor.B;
				CurrentFog.Start = Local.PlayerFog.NewStart;
				CurrentFog.End = Local.PlayerFog.NewEnd;
				Local.PlayerFog.TransitionTime = -1;
			}
		}
	}

	public void UpdateFlashlight() {
		if (IsEffectActive(EntityEffects.DimLight)) {
			if (PointFlashlight == null) {
				PointFlashlight = new FlashlightEffect(Index);

				if (PointFlashlight == null)
					return;

				PointFlashlight.TurnOn();
			}

			EyeVectors(out Vector3 forward, out Vector3 right, out Vector3 up);

			PointFlashlight.UpdateLight(EyePosition(), in forward, in right, in up, FLASHLIGHT_DISTANCE);
		}
		else {
			PointFlashlight?.Dispose();
			PointFlashlight = null;
		}
	}

	public void Flashlight() => UpdateFlashlight();

	public override void Simulate() {
		if (this == GetLocalPlayer()) {
			Flashlight();

			UpdateFogController();
		}
		else {
			// todo
		}

		base.Simulate();
		if (IsNoInterpolationFrame() || Teleported())
			ResetLatched();
	}

	public virtual void PreThink() {
		ItemPreFrame();

		UpdateClientData();

		UpdateUnderwaterState();

		UpdateFogController();

		if (LifeState >= (int)Source.LifeState.Dying)
			return;

		// If we're not on the ground, we're falling. Update our falling velocity.
		if (0 == (GetFlags() & EntityFlags.OnGround))
			Local.FallVelocity = -GetAbsVelocity().Z;
	}
	public C_BaseEntity? GetVehicleEntity() => Vehicle.Get();
	public bool IsInAVehicle() => Vehicle.Get() != null;
#if GMOD_DLL
	public virtual bool RestrictPlayerPitch() => false;
	public virtual bool CanAttack() => true;
#endif
	public virtual void SetAnimation(PlayerAnim playerAnim) { } // todo

	public virtual Vector3 GetAutoaimVector(float scale) {
		MathLib.AngleVectors(GetAbsAngles(), out Vector3 forward, out _, out _);
		return forward;
	}
	public virtual void SetSuitUpdate(ReadOnlySpan<char> name, int fgroup, int noRepeat) { }

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetSuitUpdate(ReadOnlySpan<char> name, bool fgroup, int noRepeat) => SetSuitUpdate(name, fgroup ? 1 : 0, noRepeat);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetSuitUpdate(ReadOnlySpan<char> name, int fgroup, bool noRepeat) => SetSuitUpdate(name, fgroup, noRepeat ? 1 : 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetSuitUpdate(ReadOnlySpan<char> name, bool fgroup, bool noRepeat) => SetSuitUpdate(name, fgroup ? 1 : 0, noRepeat ? 1 : 0);

	public virtual void PostThink() {
		if (IsAlive()) {
			// Need to do this on the client to avoid prediction errors
			if ((GetFlags() & EntityFlags.Ducking) != 0)
				SetCollisionBounds(VEC_DUCK_HULL_MIN, VEC_DUCK_HULL_MAX);
			else
				SetCollisionBounds(VEC_HULL_MIN, VEC_HULL_MAX);

			// if (!CommentaryModeShouldSwallowInput(this)) {
			// 	// do weapon stuff
			ItemPostFrame();
			// }

			if ((GetFlags() & EntityFlags.OnGround) != 0)
				Local.FallVelocity = 0;

			// Don't allow bogus sequence on player
			if (GetSequence() == -1)
				SetSequence(0);

			StudioFrameAdvance();
		}

		// Even if dead simulate entities
		SimulatePlayerSimulatedEntities();
	}

	InlineArrayMaxAmmoSlots<int> OldAmmo;
	public int StuckLast;

	public TimeUnit_t GetDeathTime() => DeathTime;

	public virtual bool IsOverridingViewmodel() => false;
	public virtual int DrawOverriddenViewmodel(C_BaseViewModel viewmodel, StudioFlags flags) => 0;
	public override void OnDataChanged(DataUpdateType updateType) {
		if (IsLocalPlayer())
			SetPredictionEligible(true);

		base.OnDataChanged(updateType);

		// Only care about this for local player
		if (IsLocalPlayer()) {
			// Reset engine areabits pointer
			render.SetAreaState(Local.AreaBits, Local.AreaPortalBits);

#if !GMOD_DLL
			// Check for Ammo pickups.
			for (int i = 0; i < MAX_AMMO_TYPES; i++) {
				if (GetAmmoCount(i) > OldAmmo[i]) {
					// Don't add to ammo pickup if the ammo doesn't do it
					FileWeaponInfo? pWeaponData = gWR.GetWeaponFromAmmo(i);

					if (pWeaponData == null || (pWeaponData.Flags & WeaponFlags.NoAmmoPickups) == 0) {
						// We got more ammo for this ammo index. Add it to the ammo history
						HudHistoryResource? pHudHR = gHUD.FindElement("CHudHistoryResource") as HudHistoryResource;
						pHudHR?.AddToHistory(HRType.Ammo, i, Math.Abs(GetAmmoCount(i) - OldAmmo[i]));
					}
				}
			}
#endif

			Soundscape_Update(ref Local.Audio);

			if (OldFogController != Local.PlayerFog.Ctrl)
				FogControllerChanged(updateType == DataUpdateType.Created);
		}
	}
	public void SetViewAngles(in QAngle angles) {
		SetLocalAngles(angles);
		SetNetworkAngles(angles);
	}

	readonly List<Handle<BaseEntity>> SimulatedByThisPlayer = [];

	public override void ReceiveMessage(int classID, bf_read msg) {
		if (classID != GetClientClass().ClassID) {
			// message is for subclass

			base.ReceiveMessage(classID, msg);
			return;
		}

		int messageType = msg.ReadByte();

		switch (messageType) {
			case PLAY_PLAYER_JINGLE:
				PlayPlayerJingle();
				break;
		}
	}

	private void PlayPlayerJingle() {

	}


	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(pl)) { Encoding = NetEncoding.DataTable, DataTable = "DT_PlayerState" },
		nameof(Vehicle),
		nameof(UseEntity),
		nameof(LifeState),
		nameof(ColorCorrectionCtrl), // << gmod specific
		nameof(Maxspeed),
		nameof(flags),
		nameof(ObserverMode), // todo: RecvProxy_ObserverMode
		nameof(ObserverTarget), // todo: RecvProxy_ObserverTarget
		new(nameof(FOV)) { Encoding = NetEncoding.Float },
		new(nameof(FOVStart)) { Encoding = NetEncoding.Float },
		nameof(FOVTime),
		nameof(DefaultFOV),
		nameof(ZoomOwner),
		new(nameof(ViewModel)) { Encoding = NetEncoding.Array },
		nameof(UseWeaponsInVehicle),
		new(nameof(LocalPlayerExclusiveProps)) { Name = "localdata" },
	];


	static C_BasePlayer? localPlayer;
	public static C_BasePlayer? GetLocalPlayer() => localPlayer;

	public void FireGameEvent(IGameEvent ev) {
		throw new NotImplementedException();
	}

	public float MaxSpeed() => Maxspeed;
	public void SetMaxSpeed(float maxSpeed) => Maxspeed = maxSpeed;

	public override bool ShouldPredict() {
		return IsLocalPlayer();
	}

	public int SurfaceProps;
	public SurfaceData_ptr? SurfaceData;
	public float SurfaceFriction;
	public UtlSymId_t TextureType;

	public override void PhysicsSimulate() {
		BaseEntity? pMoveParent = GetMoveParent();
		if (pMoveParent != null)
			pMoveParent.PhysicsSimulate();

		// Make sure not to simulate this guy twice per frame
		if (SimulationTick == gpGlobals.TickCount)
			return;

		SimulationTick = gpGlobals.TickCount;

		if (!IsLocalPlayer())
			return;

		C_CommandContext ctx = GetCommandContext();
		Assert(ctx.NeedsProcessing);
		if (!ctx.NeedsProcessing)
			return;

		ctx.NeedsProcessing = false;

		// Handle FL_FROZEN.
		if ((GetFlags() & EntityFlags.Frozen) != 0) {
			ctx.Cmd.ForwardMove = 0;
			ctx.Cmd.SideMove = 0;
			ctx.Cmd.UpMove = 0;
			ctx.Cmd.Buttons = 0;
			ctx.Cmd.Impulse = 0;
			//VectorCopy ( pl.v_angle, ctx.cmd.viewangles );
		}

		// Run the next command
		prediction.RunCommand(
			this,
			C_CommandContext.SafeCmdPointer(ctx),
			MoveHelper());
	}

	readonly C_CommandContext CommandContext = new();

	public C_CommandContext GetCommandContext() {
		return CommandContext;
	}

	public override void Term() {
		if (this == localPlayer) {
			localPlayer = null;
		}
		base.Term();
	}

	public float GetStepSize() => Local.StepSize;

	float OldPlayerZ;
	float OldPlayerViewOffsetZ;
	bool WasFreezeFraming;
	Vector3 FreezeFrameStart;
	TimeUnit_t FreezeFrameStartTime;
	float FreezeFrameDistance;

	public override void PostDataUpdate(DataUpdateType updateType) {
		if (updateType == DataUpdateType.Created) {
			int localPlayerIndex = engine.GetLocalPlayer();

			if (localPlayerIndex == Index) {
				localPlayer = this;
			}
		}

		bool forceEFNoInterp = IsNoInterpolationFrame();

		if (IsLocalPlayer())
			SetSimulatedEveryTick(true);
		else {
			SetSimulatedEveryTick(false);

			// estimate velocity for non local players
			TimeUnit_t flTimeDelta = SimulationTime - OldSimulationTime;
			if (flTimeDelta > 0 && !(IsNoInterpolationFrame() || forceEFNoInterp)) {
				Vector3 newVelo = (GetNetworkOrigin() - GetOldOrigin()) / (float)flTimeDelta;
				SetAbsVelocity(newVelo);
			}
		}

		base.PostDataUpdate(updateType);

		if (IsLocalPlayer()) {
			engine.GetViewAngles(out QAngle angles);
			if (updateType == DataUpdateType.Created) {
				SetLocalViewAngles(angles);
				OldPlayerZ = GetLocalOrigin().Z;

			}
			SetLocalAngles(angles);

			if (!WasFreezeFraming && GetObserverMode() == Shared.ObserverMode.FreezeCam) {
				// todo: freeze framing
			}
			else if (WasFreezeFraming && GetObserverMode() != Shared.ObserverMode.FreezeCam) {
				// todo: freeze framing
			}

			// todo: force calculate vision when the local vision flags changed
		}

		// If we are updated while paused, allow the player origin to be snapped by the
		//  server if we receive a packet from the server
		if (engine.IsPaused() || forceEFNoInterp)
			ResetLatched();
	}

	public void SetLocalViewAngles(in QAngle angles) {
		pl.ViewingAngle = angles;
	}

	[NetworkVar("pl")]
	internal readonly PlayerState pl = new();
	[NetworkVar("m_Local")]
	public readonly PlayerLocalData Local = new();
	[NetworkVar("m_hVehicle")]
	EHANDLE Vehicle = new();
	[NetworkVar("m_hUseEntity")]
	EHANDLE UseEntity = new();
	[NetworkVar("m_hObserverTarget")]
	EHANDLE ObserverTarget = new();
	[NetworkVar("m_hZoomOwner")]
	EHANDLE ZoomOwner = new();
	[NetworkVar("m_hConstraintEntity")]
	public EHANDLE ConstraintEntity = new();
	[NetworkVar("m_hTonemapController")]
	EHANDLE TonemapController = new();
	[NetworkVar("m_hViewEntity")]
	EHANDLE ViewEntity = new();
	[NetworkVar("m_hViewModel")]
	InlineArrayNewMaxViewmodels<Handle<C_BaseViewModel>> ViewModel = new();
	[NetworkVar("m_bDisableWorldClicking")]
	public bool DisableWorldClicking;
	public bool WorldClicking;
	public Vector3 WorldClickVector;
	[NetworkVar("m_flMaxspeed")]
	public float Maxspeed;
	int BonusProgress;
	int BonusChallenge;
	[NetworkVar("m_iObserverMode")]
	public int ObserverMode;
	[NetworkVar("m_iFOV")]
	public float FOV;
	[NetworkVar("m_iFOVStart")]
	public float FOVStart;
	[NetworkVar("m_flFOVTime")]
	public TimeUnit_t FOVTime;
	[NetworkVar("m_iDefaultFOV")]
	public float DefaultFOV;
	[NetworkVar("m_vecConstraintCenter")]
	public Vector3 ConstraintCenter;
	[NetworkVar("m_flConstraintRadius")]
	public float ConstraintRadius;
	[NetworkVar("m_flConstraintWidth")]
	public float ConstraintWidth;
	[NetworkVar("m_flConstraintSpeedFactor")]
	public float ConstraintSpeedFactor;
	InlineArray18<char> LastPlaceName;
	[NetworkVar("m_hColorCorrectionCtrl")]
	EHANDLE ColorCorrectionCtrl = new();
	[NetworkVar("m_bUseWeaponsInVehicle")]
	bool UseWeaponsInVehicle;
	public bool GetAllowWeaponsInVehicle() => UseWeaponsInVehicle;
	public bool OnTarget;
	[NetworkVar("m_flDeathTime")]
	public double DeathTime;
	[NetworkVar("m_flLaggedMovementValue")]
	public double LaggedMovementValue;
	[NetworkVar("m_nTickBase")]
	public int TickBase;
	public long FinalPredictedTick;
	InlineArray32<char> AnimExtension;
	FlashlightEffect? PointFlashlight;
	public virtual BaseEntity? GetObserverTarget() => null; // todo

	static readonly ConVar demo_fov_override = new("demo_fov_override", "0", FCvar.ClientDLL | FCvar.DontRecord, "If nonzero, this value will be used to override FOV during demo playback.");

	public virtual float GetFOV() {
		// Allow users to override the FOV during demo playback
		bool useDemoOverrideFov = engine.IsPlayingDemo() && demo_fov_override.GetFloat() > 0.0f;

		if (useDemoOverrideFov)
			return Math.Clamp(demo_fov_override.GetFloat(), 10.0f, 90.0f);

		if (GetObserverMode() == Shared.ObserverMode.InEye) {
			C_BasePlayer? targetPlayer = (C_BasePlayer?)GetObserverTarget();

			// get fov from observer target. Not if target is observer itself
			if (targetPlayer != null && !targetPlayer.IsObserver())
				return targetPlayer.GetFOV();
		}

		// Allow our vehicle to override our FOV if it's currently at the default FOV.
		float defaultFOV;
		IClientVehicle? vehicle = GetVehicle();
		if (vehicle != null) {
			CacheVehicleView();
			defaultFOV = (VehicleViewFOV == 0) ? GetDefaultFOV() : VehicleViewFOV;
		}
		else
			defaultFOV = GetDefaultFOV();

		float fFOV = (FOV == 0) ? defaultFOV : FOV;

		// Don't do lerping during prediction. It's only necessary when actually rendering,
		// and it'll cause problems due to prediction timing messiness.
		if (!prediction.InPrediction()) {
			// See if we need to lerp the values for local player
			if (IsLocalPlayer() && (fFOV != FOVStart) && (Local.FOVRate > 0.0f)) {
				TimeUnit_t deltaTime = (float)(gpGlobals.CurTime - FOVTime) / Local.FOVRate;

#if !NO_ENTITY_PREDICTION
				if (GetPredictable()) {
					// m_flFOVTime was set to a predicted time in the future, because the FOV change was predicted.
					deltaTime = (float)(GetFinalPredictedTime() - FOVTime);
					deltaTime += (gpGlobals.InterpolationAmount * TICK_INTERVAL);
					deltaTime /= Local.FOVRate;
				}
#endif

				if (deltaTime >= 1.0f) {
					//If we're past the zoom time, just take the new value and stop lerping
					FOVStart = (int)fFOV;
				}
				else {
					fFOV = (float)MathLib.SimpleSplineRemapValClamped(deltaTime, 0.0f, 1.0f, (float)FOVStart, fFOV);
				}
			}
		}

		return fFOV;
	}

	public Vector3 VehicleViewOrigin;
	public QAngle VehicleViewAngles;
	public float VehicleViewFOV;
	public int VehicleViewSavedFrame;


	public bool IsSuitEquipped() => Local.WearingSuit;

	public TimeUnit_t GetLaggedMovementValue() => LaggedMovementValue;

	public C_BaseViewModel? GetViewModel(int index = 0, bool observerOK = true) {
		C_BaseViewModel? vm = ViewModel[index].Get();
		// TODO: Observer OK

		return vm;
	}

	public IClientVehicle? GetVehicle() {
		C_BaseEntity? vehicleEnt = Vehicle.Get();
		return vehicleEnt?.GetClientVehicle();
	}
	public BaseCombatWeapon? GetLastWeapon() => LastWeapon.Get();

	public static readonly ConVar cl_customsounds = new("cl_customsounds", "0", 0, "Enable customized player sound playback");
	public static readonly ConVar spec_track = new("spec_track", "0", 0, "Tracks an entity in spec mode");
	public static readonly ConVar cl_smooth = new("cl_smooth", "1", 0, "Smooth view/eye origin after prediction errors");
	public static readonly ConVar cl_smoothtime = new(
	"cl_smoothtime",
		"0.1",
		0,
		"Smooth client's view after prediction error over this many seconds",
		0.01, // min/max is 0.01/2.0
		2.0
	 );

	public virtual bool CreateMove(TimeUnit_t inputSampleTime, ref UserCmd cmd) {
		return true;
	}
	public void GetPredictionErrorSmoothingVector(out Vector3 offset) {
		if (engine.IsPlayingDemo() || cl_smooth.GetInt() == 0 || cl_predict.GetInt() == 0 || engine.IsPaused()) {
			offset = default;
			return;
		}

		// TODO: Evaluate how changing cast order affects this accuracy
		float errorAmount = ((float)gpGlobals.CurTime - (float)PredictionErrorTime) / cl_smoothtime.GetFloat();

		if (errorAmount >= 1.0f) {
			offset = default;
			return;
		}

		errorAmount = 1.0f - errorAmount;

		offset = PredictionError * errorAmount;
	}
	public Vector3 PredictionError;
	public Vector3 PreviouslyPredictedOrigin;
	public TimeUnit_t PredictionErrorTime;
	public void NotePredictionError(in Vector3 delta) {
		if (!IsAlive())
			return;

		GetPredictionErrorSmoothingVector(out Vector3 oldDelta);
		PredictionError = delta + oldDelta;
		PredictionErrorTime = gpGlobals.CurTime;
		ResetLatched();
	}

	protected override bool ShouldInterpolate() {
		if (IsLocalPlayer())
			return true;

		return base.ShouldInterpolate();
	}

	public ref readonly QAngle GetPunchAngle() => ref Local.PunchAngle;
	public void SetPunchAngle(in QAngle angle) => Local.PunchAngle = angle;

	public override BaseCombatWeapon? GetActiveWeapon() {
		BasePlayer fromPlayer = this;

		if (fromPlayer == GetLocalPlayer() && GetObserverMode() == Shared.ObserverMode.InEye) {
			C_BaseEntity? target = ObserverTarget.Get();

			if (target != null && target.IsPlayer())
				fromPlayer = (BasePlayer)target;
		}

		return fromPlayer.ActiveWeapon.Get();
	}

	public bool IsPoisoned() => Local.Poisoned;

	public void ResetAutoaim() => OnTarget = false;
	public ObserverMode GetObserverMode() => (ObserverMode)ObserverMode;

	public int CurrentCommandNumber() => CurrentCommand.Get().CommandNumber;

	bool PlayerUnderwater;
	private bool IsPlayerUnderwater() => PlayerUnderwater;

	internal void ThirdPersonSwitch(bool thirdperson) {
		UpdateVisibility();

		if (IsLocalPlayer()) {
			bool shouldDrawLocalPlayer = ShouldDrawLocalPlayer();
			// for (int i = 0; i < GetNumBoneAttachments(); ++i) {
			// 	C_BaseAnimating bone = GetBoneAttachment(i);
			// 	if (bone != null) {
			// 		if (shouldDrawLocalPlayer)
			// 			bone.RemoveEffects(EntityEffects.NoDraw);
			// 		else
			// 			bone.AddEffects(EntityEffects.NoDraw);
			// 	}
			// }
		}
	}
}
