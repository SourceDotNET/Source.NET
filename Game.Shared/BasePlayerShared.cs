#if CLIENT_DLL || GAME_DLL

#if CLIENT_DLL
global using static Game.Client.BasePlayerGlobals;

global using BasePlayer = Game.Client.C_BasePlayer;
global using RecipientFilter = Game.Client.C_RecipientFilter;

#else
global using static Game.Server.BasePlayerGlobals;

global using BasePlayer = Game.Server.BasePlayer;

#endif
using Source.Common.Mathematics;

using Game.Shared;

using System.Numerics;

#if CLIENT_DLL
namespace Game.Client;
#else
namespace Game.Server;
#endif

using Source.Common.Commands;
using Source.Common.Physics;
using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Formats.BSP;
using Source.Common.SoundEmitterSystem;

using System.Runtime.CompilerServices;

using Game.Shared.GarrysMod;

public static class BasePlayerGlobals
{
	public static BasePlayer? ToBasePlayer(BaseEntity? entity) {
		if (entity == null || !entity.IsPlayer())
			return null;

		return (BasePlayer?)entity;
	}

	public static BaseCombatCharacter? ToBaseCombatCharacter(BaseEntity? entity) {
		if (entity == null || !entity.IsBaseCombatCharacter())
			return null;

		return (BaseCombatCharacter?)entity;
	}

	public const TimeUnit_t DEATH_ANIMATION_TIME = 3.0f;

	// PlayerUse defines
	public const float PLAYER_USE_RADIUS = 80.0f;

	public static float IntervalDistance(float x, float x0, float x1) {
		// swap so x0 < x1
		if (x0 > x1) {
			float tmp = x0;
			x0 = x1;
			x1 = tmp;
		}

		if (x < x0)
			return x0 - x;
		else if (x > x1)
			return x - x1;
		return 0;
	}

	public static void CopySoundNameWithModifierToken(Span<char> dest, ReadOnlySpan<char> source, int maxLenInChars, ReadOnlySpan<char> token) {
		source = source.SliceNullTerminatedString();
		token = token.SliceNullTerminatedString();

		int nSource = 0;
		int nDest = 0;
		bool foundPeriod = false;

		while (nSource < source.Length && nDest < maxLenInChars - 2) {
			dest[nDest] = source[nSource];
			nDest++;
			nSource++;

			if (!foundPeriod && source[nSource - 1] == '.') {
				foundPeriod = true;

				int nToken = 0;

				while (nToken < token.Length && nDest < maxLenInChars - 2) {
					dest[nDest] = token[nToken];
					nDest++;
					nToken++;
				}
			}
		}

		dest[nDest] = '\0';
	}

	public enum StepSoundTimes
	{
		Normal = 0,
		OnLadder,
		WaterKnee,
		WaterFoot
	}
}

public partial class
#if CLIENT_DLL
	C_BasePlayer
#elif GAME_DLL
	BasePlayer
#endif
{
	public void SharedSpawn() {
		SetMoveType(Source.MoveType.Walk);
		SetSolid(SolidType.BBox);
		AddSolidFlags(SolidFlags.NotStandable);
		Friction = 1.0f;

		pl.DeadFlag = false;
		LifeState = (int)Source.LifeState.Alive;
		Health = 100;
		m_takedamage = (byte)Damage.Yes;

		Local.DrawViewmodel = true;
		Local.StepSize = sv_stepsize.GetFloat();
		Local.AllowAutoMovement = true;

		RenderFX = (byte)RenderFx.None;
		SetNextAttack(gpGlobals.CurTime);
		Maxspeed = 0.0f;

		SetSequence(SelectWeightedSequence(Activity.ACT_IDLE));

		if ((GetFlags() & EntityFlags.Ducking) != 0)
			SetCollisionBounds(VEC_DUCK_HULL_MIN, VEC_DUCK_HULL_MAX);
		else
			SetCollisionBounds(VEC_HULL_MIN, VEC_HULL_MAX);

		Local.FallVelocity = 0;

		// SetBloodColor(BLOOD_COLOR_RED);
	}

	public TimeUnit_t GetTimeBase() => TickBase * TICK_INTERVAL;
	public virtual void CalcView(ref Vector3 eyeOrigin, ref QAngle eyeAngles, ref float zNear, ref float zFar, ref float fov) {
		CalcPlayerView(ref eyeOrigin, ref eyeAngles, ref fov); // << TODO: There is a lot more logic here for observers, vehicles, etc!
	}

	public override Vector3 EyePosition() {
		if (GetVehicle() != null) {
			// Return the cached result
			CacheVehicleView();
			return VehicleViewOrigin;
		}
		else {
#if CLIENT_DLL
			if (IsObserver()) {
				if (GetObserverMode() == Shared.ObserverMode.Chase || GetObserverMode() == Shared.ObserverMode.PointOfInterest) {
					if (IsLocalPlayer()) {
						return MainViewOrigin();
					}
				}
			}
#endif
			return base.EyePosition();
		}
	}

	public void ResetObserverMode() {
		ObserverTarget.Set(null);
		ObserverMode = (int)Shared.ObserverMode.None;

#if !CLIENT_DLL
		ObserverLastMode = (int)Shared.ObserverMode.Roaming;
		ForcedObserverMode = false;
		PhysicsFlags &= ~PlayerPhysFlag.Observer;
#endif
	}

	public ref readonly QAngle LocalEyeAngles() => ref pl.ViewingAngle;

	static QAngle angEyeWorld;
	public override ref readonly QAngle EyeAngles() {
		// NOTE: Viewangles are measured *relative* to the parent's coordinate system
		BaseEntity? pMoveParent = null; //this.GetMoveParent();

		if (pMoveParent == null)
			return ref pl.ViewingAngle;

		// FIXME: Cache off the angles?
		Matrix3x4 eyesToParent = default, eyesToWorld = default;
		MathLib.AngleMatrix(pl.ViewingAngle, out eyesToParent);
		MathLib.ConcatTransforms(pMoveParent.EntityToWorldTransform(), eyesToParent, out eyesToWorld);

		MathLib.MatrixAngles(in eyesToWorld, out angEyeWorld);
		return ref angEyeWorld;
	}

	public virtual void CalcViewModelView(in Vector3 eyeOrigin, in QAngle eyeAngles) {
		for (int i = 0; i < MAX_VIEWMODELS; i++) {
			BaseViewModel? vm = GetViewModel(i);
			if (vm == null)
				continue;

			vm.CalcViewModelView(this, eyeOrigin, eyeAngles);
		}
	}
	public void CalcViewRoll(ref QAngle angles) {
		// todo
	}
	static readonly ConVar smoothstairs = new("smoothstairs", "1", FCvar.Replicated, "Smooth player eye z coordinate when traversing stairs.");

	public void SmoothViewOnStairs(ref Vector3 eyeOrigin) {
		BaseEntity? groundEntity = GetGroundEntity();
		float currentPlayerZ = GetLocalOrigin().Z;
		float currentPlayerViewOffsetZ = GetViewOffset().Z;

		if ((groundEntity != null && groundEntity.GetMoveType() == Source.MoveType.None) && (currentPlayerZ != OldPlayerZ) && smoothstairs.GetBool() &&
			 OldPlayerViewOffsetZ == currentPlayerViewOffsetZ) {
			int dir = (currentPlayerZ > OldPlayerZ) ? 1 : -1;

			float steptime = (float)gpGlobals.FrameTime;
			if (steptime < 0)
				steptime = 0;

			OldPlayerZ += steptime * 150 * dir;

			float stepSize = GetStepSize();

			if (dir > 0) {
				if (OldPlayerZ > currentPlayerZ)
					OldPlayerZ = currentPlayerZ;
				if (currentPlayerZ - OldPlayerZ > stepSize)
					OldPlayerZ = currentPlayerZ - stepSize;
			}
			else {
				if (OldPlayerZ < currentPlayerZ)
					OldPlayerZ = currentPlayerZ;
				if (currentPlayerZ - OldPlayerZ < -stepSize)
					OldPlayerZ = currentPlayerZ + stepSize;
			}

			eyeOrigin.Z += OldPlayerZ - currentPlayerZ;
		}
		else {
			OldPlayerZ = currentPlayerZ;
			OldPlayerViewOffsetZ = currentPlayerViewOffsetZ;
		}
	}

	private void CalcPlayerView(ref Vector3 eyeOrigin, ref QAngle eyeAngles, ref float fov) {
		eyeOrigin = EyePosition();
		eyeAngles = EyeAngles();

#if CLIENT_DLL
		if (!prediction.InPrediction())
#endif
		{
			SmoothViewOnStairs(ref eyeOrigin);
		}

		Vector3 vecBaseEyePosition = eyeOrigin;

		CalcViewRoll(ref eyeAngles);
		eyeAngles += Local.PunchAngle;

#if CLIENT_DLL
		if (!prediction.InPrediction()) {
			vieweffects.CalcShake();
			vieweffects.ApplyShake(ref eyeOrigin, ref eyeAngles, 1.0f);
		}
#endif

#if CLIENT_DLL
		GetPredictionErrorSmoothingVector(out Vector3 smoothOffset);
		eyeOrigin += smoothOffset;
#endif

		fov = GetFOV();
	}

	public int GetDefaultFOV() {
#if CLIENT_DLL
	if ( GetObserverMode() == Shared.ObserverMode.InEye )
	{
		C_BasePlayer? targetPlayer = (C_BasePlayer?)GetObserverTarget();

		if ( targetPlayer != null && !targetPlayer.IsObserver() )
			return targetPlayer.GetDefaultFOV();
	}
#endif

		float fov = DefaultFOV == 0 ? g_pGameRules.DefaultFOV() : DefaultFOV;
		if (fov > MAX_FOV)
			fov = MAX_FOV;

		return (int)fov;
	}

	public BaseEntity? GetFOVOwner() => ZoomOwner.Get();

	public void ClearZoomOwner() => ZoomOwner.Set(null);

	public bool SetFOV(BaseEntity requester, int fov, float zoomRate = 0, int zoomStart = 0) {
		Assert(requester != null);
		if (requester == null)
			return false;

		// If we already have an owner, we only allow requests from that owner
		if ((ZoomOwner.Get() != null) && (ZoomOwner.Get() != requester)) {
#if GAME_DLL
			if (EnvZoom.CanOverrideEnvZoomOwner(ZoomOwner.Get()) == false)
#endif
				return false;
		}
		else {
			//FIXME: Maybe do this is as an accessor instead
			if (fov == 0)
				ZoomOwner.Set(null);
			else
				ZoomOwner.Set(requester);
		}

		// Setup our FOV and our scaling time

		if (zoomStart > 0)
			FOVStart = zoomStart;
		else
			FOVStart = (int)GetFOV();

		FOVTime = gpGlobals.CurTime;
		FOV = fov;

		Local.FOVRate = zoomRate;

		return true;
	}

	public InlineArrayMaxPlayerNameLength<char> Netname;


	public void CacheVehicleView() {
		if (VehicleViewSavedFrame == gpGlobals.FrameCount)
			return;

#if CLIENT_DLL
		IClientVehicle? vehicle = GetVehicle();
#else
		IServerVehicle? vehicle = GetVehicle();
#endif

		if (vehicle != null) {
			PassengerRole role = vehicle.GetPassengerRole(this);

			// Get our view for this frame
			vehicle.GetVehicleViewPosition(role, out VehicleViewOrigin, out VehicleViewAngles, out VehicleViewFOV);
			VehicleViewSavedFrame = (int)gpGlobals.FrameCount;

#if CLIENT_DLL
			// todo: usevr?
#endif
		}
	}


	public ReadOnlySpan<char> GetPlayerName() {
#if CLIENT_DLL
		return g_pPlayerResource != null ? g_pPlayerResource.GetPlayerName(EntIndex()) : "";
#else
		return ((Span<char>)Netname).SliceNullTerminatedString();
#endif
	}
	public void SetPlayerName(ReadOnlySpan<char> name) {
		strcpy(Netname, name);
	}

	static ConVar sv_suppress_viewpunch = new("sv_suppress_viewpunch", "0", FCvar.Replicated | FCvar.Cheat | FCvar.DevelopmentOnly);
	public const int PLAY_PLAYER_JINGLE = 1;
	public const int UPDATE_PLAYER_RADAR = 2;
	public void SelectItem(ReadOnlySpan<char> str, int subtype) {
		if (str.IsEmpty)
			return;

		BaseCombatWeapon? item = Weapon_OwnsThisType(str, subtype);

		if (item == null)
			return;

		if (GetObserverMode() != Shared.ObserverMode.None)
			return;// Observers can't select things.

		if (!Weapon_ShouldSelectItem(item))
			return;

		// FIX, this needs to queue them up and delay
		// Make sure the current weapon can be holstered
		if (GetActiveWeapon() != null) {
			if (!GetActiveWeapon()!.CanHolster() && !item.ForceWeaponSwitch())
				return;

			ResetAutoaim();
		}

		Weapon_Switch(item);
	}

	public virtual bool Weapon_ShouldSetLast(BaseCombatWeapon? last, BaseCombatWeapon? active) => true;

	public override bool Weapon_Switch(BaseCombatWeapon? weapon, int viewmodelindex = 0) {
		BaseCombatWeapon? lastWeapon = GetActiveWeapon();

		if (base.Weapon_Switch(weapon, viewmodelindex)) {
			if (lastWeapon != null && Weapon_ShouldSetLast(lastWeapon, GetActiveWeapon()))
				Weapon_SetLast(lastWeapon.GetLastWeapon());

			BaseViewModel? pViewModel = GetViewModel(viewmodelindex);
			Assert(pViewModel != null);
			if (pViewModel != null)
				pViewModel.RemoveEffects(EntityEffects.NoDraw);
			ResetAutoaim();
			return true;
		}
		return false;
	}

	public virtual ReadOnlySpan<char> GetOverrideStepSound(ReadOnlySpan<char> baseStepSoundName) => baseStepSoundName;
	public virtual void OnEmitFootstepSound(in SoundParameters parms, in Vector3 origin, float volume) { }

	struct StepSoundCache_t
	{
		public StepSoundCache_t() { SoundNameIndex = 0; }
		public SoundParameters SoundParameters;
		public UtlSymId_t SoundNameIndex;
	}
	StepSoundCache_t[] StepSoundCache = [new(), new()];

	public void PlayStepSound(in Vector3 origin, SurfaceData_ptr? surface, float vol, bool force) {
		if (gpGlobals.MaxClients > 1 && !sv_footsteps.GetBool())
			return;

#if CLIENT_DLL
		if (prediction.InPrediction() && !prediction.IsFirstTimePredicted())
			return;
#endif

		if (surface == null)
			return;

		int side = Local.StepSide;
		UtlSymId_t stepSoundName = side != 0 ? surface.Sounds.StepLeft : surface.Sounds.StepRight;
		if (stepSoundName == 0)
			return;

		Local.StepSide = side != 0 ? 0 : 1;

		SoundParameters parms = new();

		Assert(side == 0 || side == 1);

		if (StepSoundCache[side].SoundNameIndex == stepSoundName)
			parms = StepSoundCache[side].SoundParameters;
		else {
			ReadOnlySpan<char> soundName = MoveHelper().GetSurfaceProps()!.GetString(stepSoundName);

			soundName = GetOverrideStepSound(soundName);

			if (!GetParametersForSound(soundName, ref parms, null))
				return;

			if (parms.Count == 1) {
				StepSoundCache[side].SoundNameIndex = stepSoundName;
				StepSoundCache[side].SoundParameters = parms;
			}
		}

		RecipientFilter filter = new();
		filter.AddRecipientsByPAS(origin);

#if !CLIENT_DLL
		if (gpGlobals.MaxClients > 1)
			filter.RemoveRecipientsByPVS(origin);
#endif

		scoped EmitSound_t ep = new() {
			Channel = (int)SoundEntityChannel.Body,
			SoundName = ((ReadOnlySpan<char>)parms.SoundName).SliceNullTerminatedString(),
			Volume = vol,
			SoundLevel = parms.SoundLevel,
			Flags = 0,
			Pitch = parms.Pitch,
			Origin = ref origin
		};

		EmitSound(filter, EntIndex(), in ep);

		OnEmitFootstepSound(parms, origin, vol);
	}

	public virtual bool Weapon_ShouldSelectItem(BaseCombatWeapon weapon) => weapon != GetActiveWeapon();

	public virtual void UpdateButtonState(InButtons userCmdButtonMask) {
		AfButtonLast = Buttons;
		Buttons = userCmdButtonMask;
		InButtons buttonsChanged = AfButtonLast ^ Buttons;

		// Debounced button codes for pressed/released
		// UNDONE: Do we need auto-repeat?
		AfButtonPressed = buttonsChanged & Buttons;        // The changed ones still down are "pressed"
		AfButtonReleased = buttonsChanged & (~Buttons);    // The ones not down are "released"
	}

	public virtual float GetPlayerMaxSpeed() {
		float maxSpeed = sv_maxspeed.GetFloat();
		if (MaxSpeed() > 0.0f && MaxSpeed() < maxSpeed)
			maxSpeed = MaxSpeed();

		return maxSpeed;
	}

	public bool IsOnLadder() => GetMoveType() == Source.MoveType.Ladder;

	public virtual float GetDuckSpeed() => Local.DuckSpeed;
	public virtual float GetSprintSpeed() => Local.SprintSpeed;
	public virtual float GetSlowWalkSpeed() => Local.SlowWalkSpeed;
	public virtual float GetWalkSpeed() => Local.WalkSpeed;
	public virtual float GetUnDuckSpeed() => Local.UnDuckSpeed;

	// TODO: these should use [NetworkVar]
#if CLIENT_DLL
	public virtual void SetSprintSpeed(float speed) => Local.SprintSpeed = speed;
	public virtual void SetWalkSpeed(float speed) => Local.WalkSpeed = speed;
	public virtual void SetSlowWalkSpeed(float speed) => Local.SlowWalkSpeed = speed;
	public virtual void SetLadderSpeed(float speed) => Local.LadderSpeed = speed;
	public virtual void SetCrouchedWalkSpeed(float speed) => Local.CrouchedWalkSpeed = speed;
	public virtual void SetDuckSpeed(float speed) => Local.DuckSpeed = speed;
	public virtual void SetUnDuckSpeed(float speed) => Local.UnDuckSpeed = speed;
#else
	public virtual void SetSprintSpeed(float speed) {
		if (Local.SprintSpeed != speed) {
			NetworkStateChanged();
			Local.SprintSpeed = speed;
		}
	}

	public virtual void SetWalkSpeed(float speed) {
		if (Local.WalkSpeed != speed) {
			NetworkStateChanged();
			Local.WalkSpeed = speed;
		}
	}

	public virtual void SetSlowWalkSpeed(float speed) {
		if (Local.SlowWalkSpeed != speed) {
			NetworkStateChanged();
			Local.SlowWalkSpeed = speed;
		}
	}

	public virtual void SetLadderSpeed(float speed) {
		if (Local.LadderSpeed != speed) {
			NetworkStateChanged();
			Local.LadderSpeed = speed;
		}
	}

	public virtual void SetCrouchedWalkSpeed(float speed) {
		if (Local.CrouchedWalkSpeed != speed) {
			NetworkStateChanged();
			Local.CrouchedWalkSpeed = speed;
		}
	}

	public virtual void SetDuckSpeed(float speed) {
		if (Local.DuckSpeed != speed) {
			NetworkStateChanged();
			Local.DuckSpeed = speed;
		}
	}

	public virtual void SetUnDuckSpeed(float speed) {
		if (Local.UnDuckSpeed != speed) {
			NetworkStateChanged();
			Local.UnDuckSpeed = speed;
		}
	}
#endif


	int SkipStep;
	public void UpdateStepSound(SurfaceData_ptr? surface, in Vector3 origin, in Vector3 velocity) {
		bool walking;
		float vol = 0;
		Vector3 knee, feet;
		float height, speed, velrun, velwalk;
		bool ladder;

		if (StepSoundTime > 0) {
			StepSoundTime -= 1000.0f * gpGlobals.FrameTime;
			if (StepSoundTime < 0)
				StepSoundTime = 0;
		}

		if (StepSoundTime > 0)
			return;

		if ((GetFlags() & (EntityFlags.Frozen | EntityFlags.AtControls)) != 0)
			return;

		if (GetMoveType() == Source.MoveType.Noclip || GetMoveType() == Source.MoveType.Observer)
			return;

		if (!sv_footsteps.GetBool())
			return;

		speed = MathLib.VectorLength(Velocity);
		float groundSpeed = MathLib.Vector2DLength(Velocity.AsVector2());

		ladder = GetMoveType() == Source.MoveType.Ladder;

		GetStepSoundVelocities(out velwalk, out velrun);

		bool onground = (GetFlags() & EntityFlags.OnGround) != 0;
		bool movingAlongGround = groundSpeed > 0.0001f;
		bool movingFastEnough = speed >= velwalk;

		if (!movingFastEnough || !(ladder || (onground && movingAlongGround)))
			return;

		walking = speed < velrun;

		MathLib.VectorCopy(Origin, out knee);
		MathLib.VectorCopy(Origin, out feet);

		height = GetPlayerMaxs()[2] - GetPlayerMins()[2];
		knee[2] = Origin[2] + 0.2f * height;

		if (ladder) {
			surface = GetLadderSurface(Origin);
			vol = 0.5f;

			SetStepSoundTime(StepSoundTimes.OnLadder, walking);
		}
		else if (GetWaterLevel() == Shared.WaterLevel.Waist) {

			if (SkipStep == 0) {
				SkipStep++;
				return;
			}

			if (SkipStep++ == 3)
				SkipStep = 0;

			surface = physprops.GetSurfaceData(physprops.GetSurfaceIndex("wade"));
			vol = 0.65f;

			SetStepSoundTime(StepSoundTimes.WaterKnee, walking);
		}
		else if (GetWaterLevel() == Shared.WaterLevel.Feet) {
			surface = physprops.GetSurfaceData(physprops.GetSurfaceIndex("water"));
			vol = walking ? .2f : .5f;
			SetStepSoundTime(StepSoundTimes.WaterKnee, walking);
		}
		else {
			if (surface == null)
				return;

			SetStepSoundTime(StepSoundTimes.Normal, walking);

			switch ((CharTex)surface.Game.Material) {
				default:
				case CharTex.Concrete:
					vol = walking ? 0.2f : 0.5f;
					break;

				case CharTex.Metal:
					vol = walking ? 0.2f : 0.5f;
					break;

				case CharTex.Dirt:
					vol = walking ? 0.25f : 0.55f;
					break;

				case CharTex.Vent:
					vol = walking ? 0.4f : 0.7f;
					break;

				case CharTex.Grate:
					vol = walking ? 0.2f : 0.5f;
					break;

				case CharTex.Tile:
					vol = walking ? 0.2f : 0.5f;
					break;

				case CharTex.Slosh:
					vol = walking ? 0.2f : 0.5f;
					break;
			}

			if ((GetFlags() & EntityFlags.Ducking) != 0)
				vol *= 0.65f;
		}

		PlayStepSound(feet, surface, vol, false);
	}

	public ref readonly Vector3 GetPreviouslyPredictedOrigin() => ref PreviouslyPredictedOrigin;
	public void SetPreviouslyPredictedOrigin(in Vector3 absOrigin) => PreviouslyPredictedOrigin = absOrigin;

	private SurfaceData_ptr GetLadderSurface(Vector3 origin) {
#if CLIENT_DLL
		return GetFootstepSurface(origin, "ladder")!;
#else
		return physprops.GetSurfaceData(physprops.GetSurfaceIndex("ladder"))!;
#endif
	}

#if CLIENT_DLL
	private SurfaceData_ptr GetFootstepSurface(Vector3 origin, ReadOnlySpan<char> v) => physprops.GetSurfaceData(physprops.GetSurfaceIndex(v))!;
#endif

	private void GetStepSoundVelocities(out float velwalk, out float velrun) {
		if ((GetFlags() & EntityFlags.Ducking) != 0 || GetMoveType() == Source.MoveType.Ladder) {
			velwalk = 60;
			velrun = 80;
		}
		else {
			velwalk = 90;
			velrun = 220;
		}
	}

	private void SetStepSoundTime(StepSoundTimes stepSoundTime, bool walking) {
		switch (stepSoundTime) {
			case StepSoundTimes.Normal:
			case StepSoundTimes.WaterFoot:
				StepSoundTime = walking ? 400.0f : 300.0f;
				break;
			case StepSoundTimes.OnLadder:
				StepSoundTime = 350.0f;
				break;
			case StepSoundTimes.WaterKnee:
				StepSoundTime = 600.0f;
				break;
			default:
				Assert(false);
				break;
		}

		if ((GetFlags() & EntityFlags.Ducking) != 0 || (GetMoveType() == Source.MoveType.Ladder))
			StepSoundTime += 100.0f;
	}


	Vector3 GetPlayerMins() {
		if (IsObserver())
			return VEC_OBS_HULL_MIN_SCALED(this);
		else {
			if ((GetFlags() & EntityFlags.Ducking) != 0)
				return VEC_DUCK_HULL_MIN_SCALED(this);
			else
				return VEC_HULL_MIN_SCALED(this);
		}
	}

	Vector3 GetPlayerMaxs() {
		if (IsObserver())
			return VEC_OBS_HULL_MAX_SCALED(this);
		else {
			if ((GetFlags() & EntityFlags.Ducking) != 0)
				return VEC_DUCK_HULL_MAX_SCALED(this);
			else
				return VEC_HULL_MAX_SCALED(this);
		}
	}


#if !CLIENT_DLL
	/// <summary>
	/// Called every usercmd by the player PreThink
	/// </summary>
	public virtual void ItemPreFrame() {
		// Handle use events
		PlayerUse();

		BaseCombatWeapon? active = GetActiveWeapon();

		// Allow all the holstered weapons to update
		for (int i = 0; i < WeaponCount(); ++i) {
			BaseCombatWeapon? weapon = GetWeapon(i);

			if (weapon == null)
				continue;

			if (active == weapon)
				continue;

			weapon.ItemHolsterFrame();
		}

		if (gpGlobals.CurTime < NextAttack)
			return;

		if (active == null)
			return;

		active.ItemPreFrame();
	}

	static readonly ConVar sv_debug_player_use = new("sv_debug_player_use", "0", FCvar.Replicated, "Visualizes +use logic. Green cross=trace success, Red cross=trace too far, Green box=radius success");

	const int NUM_TANGENTS = 8;
	// trace a box at successive angles down
	//							forward, 45 deg, 30 deg, 20 deg, 15 deg, 10 deg, -10, -15
	static readonly float[] tangents = [0, 1, 0.57735026919f, 0.3639702342f, 0.267949192431f, 0.1763269807f, -0.1763269807f, -0.267949192431f];

	public virtual BaseEntity? FindUseEntity() {
		EyeVectors(out Vector3 forward, out _, out Vector3 up);

		Trace tr = default;
		// Search for objects in a sphere (tests for entities that are not solid, yet still useable)
		Vector3 searchCenter = EyePosition();

		// NOTE: Some debris objects are useable too, so hit those as well
		// A button, etc. can be made out of clip brushes, make sure it's +useable via a traceline, too.
		Mask useableContents = Mask.Solid | (Mask)Contents.Debris | (Mask)Contents.PlayerClip;

		BaseEntity? foundByTrace = null;

		// UNDONE: Might be faster to just fold this range into the sphere query
		BaseEntity? obj = null;

		float nearestDist = float.MaxValue;
		// try the hit entity if there is one, or the ground entity if there isn't.
		BaseEntity? nearest = null;

		for (int i = 0; i < NUM_TANGENTS; i++) {
			if (i == 0) {
				Util.TraceLine(searchCenter, searchCenter + forward * 1024, useableContents, this, Source.CollisionGroup.None, out tr);
			}
			else {
				Vector3 down = forward - tangents[i] * up;
				MathLib.VectorNormalize(ref down);
				Util.TraceHull(searchCenter, searchCenter + down * 72, -new Vector3(16, 16, 16), new Vector3(16, 16, 16), useableContents, this, Source.CollisionGroup.None, out tr);
			}
			obj = tr.Ent;

			foundByTrace = obj;

			bool usable = IsUseableEntity(obj, 0);
			while (obj != null && !usable && obj.GetMoveParent() != null) {
				obj = obj.GetMoveParent();
				usable = IsUseableEntity(obj, 0);
			}

			if (usable) {
				Vector3 delta = tr.EndPos - tr.StartPos;
				float centerZ = CollisionProp().WorldSpaceCenter().Z;
				delta.Z = IntervalDistance(tr.EndPos.Z, centerZ + CollisionProp().OBBMins().Z, centerZ + CollisionProp().OBBMaxs().Z);
				float dist = delta.Length();
				if (dist < PLAYER_USE_RADIUS) {
					if (sv_debug_player_use.GetBool()) {
						DebugOverlay.Line(searchCenter, tr.EndPos, 0, 255, 0, true, 30);
						DebugOverlay.Cross3D(tr.EndPos, 16, 0, 255, 0, true, 30);
					}

					if (obj!.MyNPCPointer() != null && obj.MyNPCPointer()!.IsPlayerAlly(this)) {
						// If about to select an NPC, do a more thorough check to ensure
						// that we're selecting the right one from a group.
						obj = DoubleCheckUseNPC(obj, searchCenter, forward);
					}

					if (sv_debug_player_use.GetBool())
						Msg($"Trace using: {(obj != null ? obj.GetDebugName() : "no usable entity found")}\n");

					nearest = obj;

					// if this is directly under the cursor just return it now
					if (i == 0)
						return obj;
				}
			}
		}

		// check ground entity first
		// if you've got a useable ground entity, then shrink the cone of this search to 45 degrees
		// otherwise, search out in a 90 degree cone (hemisphere)
		if (GetGroundEntity() != null && IsUseableEntity(GetGroundEntity(), EntityCapabilities.UseOnGround))
			nearest = GetGroundEntity();

		if (nearest != null) {
			// estimate nearest object by distance from the view vector
			nearest.CollisionProp().CalcNearestPoint(searchCenter, out Vector3 point);
			nearestDist = MathLib.CalcDistanceToLine(point, searchCenter, forward, out _);
			if (sv_debug_player_use.GetBool())
				Msg($"Trace found {nearest.GetClassname()}, dist {nearestDist:F2}\n");
		}

		for (EntitySphereQuery sphere = new(searchCenter, PLAYER_USE_RADIUS); (obj = sphere.GetCurrentEntity()) != null; sphere.NextEntity()) {
			if (obj == null)
				continue;

			if (!IsUseableEntity(obj, EntityCapabilities.UseInRadius))
				continue;

			// see if it's more roughly in front of the player than previous guess
			obj.CollisionProp().CalcNearestPoint(searchCenter, out Vector3 point);

			Vector3 dir = point - searchCenter;
			MathLib.VectorNormalize(ref dir);
			float dot = Vector3.Dot(dir, forward);

			// Need to be looking at the object more or less
			if (dot < 0.8)
				continue;

			float dist = MathLib.CalcDistanceToLine(point, searchCenter, forward, out _);

			if (sv_debug_player_use.GetBool())
				Msg($"Radius found {obj.GetClassname()}, dist {dist:F2}\n");

			if (dist < nearestDist) {
				// Since this has purely been a radius search to this point, we now
				// make sure the object isn't behind glass or a grate.
				Util.TraceLine(searchCenter, point, useableContents, this, Source.CollisionGroup.None, out Trace trCheckOccluded);

				if (trCheckOccluded.Fraction == 1.0 || trCheckOccluded.Ent == obj) {
					nearest = obj;
					nearestDist = dist;
				}
			}
		}

		if (nearest == null) {
			// Haven't found anything near the player to use, nor any NPC's at distance.
			// Check to see if the player is trying to select an NPC through a rail, fence, or other 'see-though' volume.
			Util.TraceLine(searchCenter, searchCenter + forward * PLAYER_USE_RADIUS, Mask.OpaqueAndNPCs, this, Source.CollisionGroup.None, out Trace trAllies);

			if (trAllies.Ent != null && IsUseableEntity(trAllies.Ent, 0) && trAllies.Ent.MyNPCPointer() != null && trAllies.Ent.MyNPCPointer()!.IsPlayerAlly(this)) {
				// This is an NPC, take it!
				nearest = trAllies.Ent;
			}
		}

		if (nearest != null && nearest.MyNPCPointer() != null && nearest.MyNPCPointer()!.IsPlayerAlly(this))
			nearest = DoubleCheckUseNPC(nearest, searchCenter, forward);

		if (sv_debug_player_use.GetBool()) {
			if (nearest == null) {
				DebugOverlay.Line(searchCenter, tr.EndPos, 255, 0, 0, true, 30);
				DebugOverlay.Cross3D(tr.EndPos, 16, 255, 0, 0, true, 30);
			}
			else if (nearest == foundByTrace) {
				DebugOverlay.Line(searchCenter, tr.EndPos, 0, 255, 0, true, 30);
				DebugOverlay.Cross3D(tr.EndPos, 16, 0, 255, 0, true, 30);
			}
			else {
				DebugOverlay.Box(nearest.WorldSpaceCenter(), new Vector3(-8, -8, -8), new Vector3(8, 8, 8), 0, 255, 0, 1, 30);
			}
		}

		if (sv_debug_player_use.GetBool())
			Msg($"Radial using: {(nearest != null ? nearest.GetDebugName() : "no usable entity found")}\n");

		return nearest;
	}

	/// <summary>
	/// Handles USE keypress
	/// </summary>
	public virtual void PlayerUse() {
		// Was use pressed or released?
		if (((Buttons | AfButtonPressed | AfButtonReleased) & InButtons.Use) == 0)
			return;

		if (IsObserver()) {
			// do special use operation in oberserver mode
			if ((AfButtonPressed & InButtons.Use) != 0)
				ObserverUse(true);
			else if ((AfButtonReleased & InButtons.Use) != 0)
				ObserverUse(false);

			return;
		}

		if ((AfButtonPressed & InButtons.Use) != 0) {
			// Controlling some latched entity?
			if (ClearUseEntity()) {
				return;
			}
			else {
				if ((PhysicsFlags & PlayerPhysFlag.DirOverride) != 0) {
					PhysicsFlags &= ~PlayerPhysFlag.DirOverride;
					Train = FuncTrackTrain.TRAIN_NEW | FuncTrackTrain.TRAIN_OFF;
					return;
				}
				else {  // Start controlling the train!
					BaseEntity? train = GetGroundEntity();
					if (train != null && (Buttons & InButtons.Jump) == 0 && (GetFlags() & EntityFlags.OnGround) != 0 && (train.ObjectCaps() & EntityCapabilities.DirectionalUse) != 0 && train.OnControls(this)) {
						PhysicsFlags |= PlayerPhysFlag.DirOverride;
						Train = TrainSpeed((int)train.Speed, (int)((FuncTrackTrain)train).GetMaxSpeed());
						Train |= FuncTrackTrain.TRAIN_NEW;
						EmitSound("Player.UseTrain");
						return;
					}
				}
			}
		}

		BaseEntity? useEntity = FindUseEntity();

		// Found an object
		if (useEntity != null) {

			//!!!UNDONE: traceline here to prevent +USEing buttons through walls

			EntityCapabilities caps = useEntity.ObjectCaps();
			Variant_t emptyVariant = default;
			if (((Buttons & InButtons.Use) != 0 && (caps & EntityCapabilities.ContinuousUse) != 0) || ((AfButtonPressed & InButtons.Use) != 0 && (caps & (EntityCapabilities.ImpulseUse | EntityCapabilities.OnOffUse)) != 0)) {
				if ((caps & EntityCapabilities.ContinuousUse) != 0)
					PhysicsFlags |= PlayerPhysFlag.Using;

				if ((useEntity.ObjectCaps() & EntityCapabilities.OnOffUse) != 0)
					useEntity.AcceptInput("Use", this, this, emptyVariant, (int)UseType.On);
				else
					useEntity.AcceptInput("Use", this, this, emptyVariant, (int)UseType.Toggle);
			}
			// UNDONE: Send different USE codes for ON/OFF.  Cache last ONOFF_USE object to send 'off' if you turn away
			else if ((AfButtonReleased & InButtons.Use) != 0 && (useEntity.ObjectCaps() & EntityCapabilities.OnOffUse) != 0) {    // BUGBUG This is an "off" use
				useEntity.AcceptInput("Use", this, this, emptyVariant, (int)UseType.Off);
			}
		}
		else if ((AfButtonPressed & InButtons.Use) != 0) {
			PlayUseDenySound();
		}
	}
#endif

	public void ViewPunch(in QAngle angleOffset) {
		//See if we're suppressing the view punching
		if (sv_suppress_viewpunch.GetBool())
			return;

		// We don't allow view kicks in the vehicle
		if (IsInAVehicle())
			return;

		Local.PunchAngleVel += angleOffset * 20;
	}

	public void ViewPunchReset(float tolerance = 0) {
		if (tolerance != 0) {
			tolerance *= tolerance; // square
			float check = Local.PunchAngleVel.LengthSqr() + Local.PunchAngle.LengthSqr();
			if (check > tolerance)
				return;
		}
		Local.PunchAngle = vec3_angle;
		Local.PunchAngleVel = vec3_angle;
	}

	void Weapon_SetLast(BaseCombatWeapon? pWeapon) {
		LastWeapon.Set(pWeapon);
	}

	public void SetAnimationExtension(ReadOnlySpan<char> extension) {
		strcpy(AnimExtension, extension);
	}


	public void AddToPlayerSimulationList(BaseEntity other) {
		// Already in list
		foreach (var entry in SimulatedByThisPlayer)
			if (entry.Get() == other) return;

		Assert(other.IsPlayerSimulated());

		Handle<BaseEntity> h = new();
		h.Set(other);
		SimulatedByThisPlayer.Add(h);
	}

	public void RemoveFromPlayerSimulationList(BaseEntity? other) {
		if (other == null)
			return;

		Assert(other.IsPlayerSimulated());
		Assert(other.GetSimulatingPlayer() == this);

		foreach (var entry in SimulatedByThisPlayer)
			if (entry.Get() == other) {
				SimulatedByThisPlayer.Remove(entry);
				return;
			}
	}

	public virtual void ItemPostFrame() {
		// Put viewmodels into basically correct place based on new player origin
		CalcViewModelView(EyePosition(), EyeAngles());

		// Don't process items while in a vehicle.
		if (GetVehicle() != null) {
#if CLIENT_DLL
			IClientVehicle vehicle = GetVehicle()!;
#else
			IServerVehicle vehicle = GetVehicle()!;
#endif

			bool usingStandardWeapons = UsingStandardWeaponsInVehicle();

#if CLIENT_DLL
			if (vehicle.IsPredicted())
#endif
			vehicle.ItemPostFrame(this);

			if (!usingStandardWeapons || GetVehicle() == null)
				return;
		}


		// check if the player is using something
		if (UseEntity.Get() != null) {
#if !CLIENT_DLL
			// Assert(!IsInAVehicle());
			ImpulseCommands();// this will call playerUse
#endif
			return;
		}

		if (gpGlobals.CurTime < NextAttack)
			GetActiveWeapon()?.ItemBusyFrame();
		else {
			if (GetActiveWeapon() != null && (!IsInAVehicle() || UsingStandardWeaponsInVehicle())) {
#if CLIENT_DLL
				// Not predicting this weapon
				if (GetActiveWeapon()!.IsPredicted())
#endif
				GetActiveWeapon()!.ItemPostFrame();
			}
		}

#if !CLIENT_DLL
		ImpulseCommands();
#else
		// NOTE: If we ever support full impulse commands on the client,
		// remove this line and call ImpulseCommands instead.
		Impulse = 0;
#endif
	}

	public void EyeVectors(out Vector3 forward, out Vector3 right, out Vector3 up) {
		if (GetVehicle() != null) {
			// Cache or retrieve our calculated position in the vehicle
			CacheVehicleView();
			// TODO MathLib.AngleVectors(VehicleViewAngles, out forward, out right, out up);
			forward = right = up = default;
		}
		else
			MathLib.AngleVectors(EyeAngles(), out forward, out right, out up);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void EyeVectors(out Vector3 forward, out Vector3 right) => EyeVectors(out forward, out right, out _);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void EyeVectors(out Vector3 forward) => EyeVectors(out forward, out _, out _);

	public void EyePositionAndVectors(out Vector3 position, out Vector3 forward, out Vector3 right, out Vector3 up) {
		if (GetVehicle() != null) {
			// CacheVehicleView();
			// AngleVectors(m_vecVehicleViewAngles, pForward, pRight, pUp);
			// if (pPosition != NULL) *pPosition = m_vecVehicleViewOrigin;
			position = forward = right = up = default;
		}
		else {
			position = base.EyePosition();
			MathLib.AngleVectors(EyeAngles(), out forward, out right, out up);
		}
	}
	public void ClearPlayerSimulationList() {
		int c = SimulatedByThisPlayer.Count;
		int i;

		for (i = c - 1; i >= 0; i--) {
			Handle<BaseEntity> h = SimulatedByThisPlayer[i];
			BaseEntity? e = h.Get();
			e?.UnsetPlayerSimulated();
		}

		SimulatedByThisPlayer.Clear();
	}

	public void SimulatePlayerSimulatedEntities() {
		int c = SimulatedByThisPlayer.Count;
		int i;

		for (i = c - 1; i >= 0; i--) {
			Handle<BaseEntity> h = SimulatedByThisPlayer[i];
			BaseEntity? e = h.Get();

			if (e == null || !e.IsPlayerSimulated()) {
				SimulatedByThisPlayer.RemoveAt(i);
				continue;
			}

#if CLIENT_DLL
			if (e.IsClientCreated() && prediction.InPrediction() && !prediction.IsFirstTimePredicted())
				continue;
#endif
			Assert(e.IsPlayerSimulated());
			Assert(e.GetSimulatingPlayer() == this);

			e.PhysicsSimulate();
		}

		// Loop through all entities again, checking their untouch if flagged to do so
		c = SimulatedByThisPlayer.Count;

		for (i = c - 1; i >= 0; i--) {
			Handle<BaseEntity> h = SimulatedByThisPlayer[i];
			BaseEntity? e = h.Get();

			if (e == null || !e.IsPlayerSimulated()) {
				SimulatedByThisPlayer.RemoveAt(i);
				continue;
			}

#if CLIENT_DLL
			if (e.IsClientCreated() && prediction.InPrediction() && !prediction.IsFirstTimePredicted())
				continue;
#endif

			Assert(e.IsPlayerSimulated());
			Assert(e.GetSimulatingPlayer() == this);

			if (!e.GetCheckUntouch())
				continue;

			PhysicsCheckForEntityUntouch();
		}
	}

	public bool UsingStandardWeaponsInVehicle() {
		Assert(IsInAVehicle());
#if !CLIENT_DLL
		IServerVehicle? vehicle = GetVehicle();
#else
		IClientVehicle? vehicle = GetVehicle();
#endif

		if (vehicle == null)
			return true;

		PassengerRole role = vehicle.GetPassengerRole(this);
		bool bUsingStandardWeapons = vehicle.IsPassengerUsingStandardWeapons(role);

		// Fall through and check weapons, etc. if we're using them 
		if (!bUsingStandardWeapons)
			return false;

		return true;
	}

	private void UpdateUnderwaterState() {
		if (GetWaterLevel() == Shared.WaterLevel.Eyes) {
			if (IsPlayerUnderwater() == false)
				SetPlayerUnderwater(true);
			return;
		}

		if (IsPlayerUnderwater())
			SetPlayerUnderwater(false);

		if (GetWaterLevel() == 0) {
			if ((GetFlags() & EntityFlags.InWater) != 0) {
#if !CLIENT_DLL
				if (Health > 0 && IsAlive())
					EmitSound("Player.Wade");
#endif
				RemoveFlag(EntityFlags.InWater);
			}
		}
		else if ((GetFlags() & EntityFlags.InWater) == 0) {
#if !CLIENT_DLL
			if (GetWaterType() == Contents.Water)
				EmitSound("Player.Wade");
#endif

			AddFlag(EntityFlags.InWater);
		}
	}

	public virtual void SetPlayerUnderwater(bool v) {
		// throw new NotImplementedException();
	}

	public float GetFOVDistanceAdjustFactor() {
		// TODO! GetDefaultFOV/GetFOV
		float defaultFOV = DefaultFOV;
		float localFOV = FOV;

		if (localFOV == defaultFOV || defaultFOV < 0.001f)
			return 1.0f;

		return localFOV / defaultFOV;
	}
}
#endif
