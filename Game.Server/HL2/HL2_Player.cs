using Game.Shared;

using System.Numerics;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Physics;
using Source;
using Game.Shared.HL2;

namespace Game.Server.HL2;

using DEFINE = Source.DEFINE<HL2_Player>;
using FIELD = Source.FIELD<HL2_Player>;

[PrecacheRegister("player")]
[NetworkName("CHL2_Player")]
public class HL2_Player : BaseMultiplayerPlayer
{
	public static readonly SendTable DT_HL2_Player = new(DT_BasePlayer, [
		SendPropDataTable("m_HL2Local", FIELD.OF(nameof(HL2Local)), HL2PlayerLocalData.DT_HL2Local, SendProxy_SendLocalDataTable),
		SendPropBool(FIELD.OF(nameof(m_bIsSprinting)))
	]);
	public static new readonly ServerClass ServerClass = new ServerClass(DT_HL2_Player);

	public static readonly new DataMap DataDesc = new(typeof(HL2_Player), BasePlayer.DataDesc, [
		DEFINE.INPUTFUNC(FieldType.Void, "ForceDropPhysObjects", nameof(InputForceDropPhysObjects), (INPUTFUNCPTR)((self, data) => ((HL2_Player)self).InputForceDropPhysObjects(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	[NetworkName("m_HL2Local")]
	public readonly HL2PlayerLocalData HL2Local = new();

#if HL2MP
	const int HL2_WALK_SPEED = 150;
	public const int HL2_NORM_SPEED = 190;
	const int HL2_SPRINT_SPEED = 320;
#else
#endif

	TimeUnit_t IdleTime;
	TimeUnit_t MoveTime;
	TimeUnit_t LastDamageTime;
	TimeUnit_t TargetFindTime;
	TimeUnit_t TimeUseSuspended;

	public virtual void SuspendUse(TimeUnit_t duration) { TimeUseSuspended = gpGlobals.CurTime + duration; }

	bool SprintEnabled;

	[NetworkName("m_fIsSprinting")]
	public bool m_bIsSprinting;
	public bool m_bIsWalking;
	public bool m_bPlayUseDenySound;

	public HL2_Player() => SprintEnabled = true;

	public ref LadderMove GetLadderMove() => ref HL2Local.LadderMove;

	public override void Precache() {
		base.Precache();

		PrecacheScriptSound("HL2Player.SprintNoPower");
		PrecacheScriptSound("HL2Player.SprintStart");
		PrecacheScriptSound("HL2Player.UseDeny");
		PrecacheScriptSound("HL2Player.FlashLightOn");
		PrecacheScriptSound("HL2Player.FlashLightOff");
		PrecacheScriptSound("HL2Player.PickupWeapon");
		PrecacheScriptSound("HL2Player.TrainUse");
		PrecacheScriptSound("HL2Player.Use");
		PrecacheScriptSound("HL2Player.BurnPain");
	}

	void CheckSuitZoom() { }

	void EquipSuit(bool playEffects) { }

	void RemoveSuit() { }

	void HandleSpeedChanges() { }

	void HandleArmorReduction() { }

	void PreThink() { }

	void PostThink() { }

	void StartAdmireGlovesAnimation() { }

	void HandleAdmireGlovesAnimation() { }

	const TimeUnit_t HL2PLAYER_RELOADGAME_ATTACK_DELAY = 1.0;

	public override void Activate() {
		base.Activate();
		InitSprinting();

#if HL2_EPISODIC
		if (GetActiveWeapon() != null) {
			TimeUnit_t remain = GetActiveWeapon()!.NextPrimaryAttack - gpGlobals.CurTime;

			if (remain < HL2PLAYER_RELOADGAME_ATTACK_DELAY)
				GetActiveWeapon()!.NextPrimaryAttack = gpGlobals.CurTime + HL2PLAYER_RELOADGAME_ATTACK_DELAY;

			remain = GetActiveWeapon()!.NextSecondaryAttack - gpGlobals.CurTime;

			if (remain < HL2PLAYER_RELOADGAME_ATTACK_DELAY)
				GetActiveWeapon()!.NextSecondaryAttack = gpGlobals.CurTime + HL2PLAYER_RELOADGAME_ATTACK_DELAY;
		}

#endif

		// GetPlayerProxy();
	}

	// Class_T Classify() { }

	// bool HandleInteraction(int interactionType, void* data, BaseCombatCharacter sourceEnt) { }

	public override void PlayerRunCommand(UserCmd ucmd, IMoveHelper moveHelper) {
		// if (PhysicsFlags & PFLAG_ONBARNACLE) { // TODO
		// 	ucmd.forwardmove = 0;
		// 	ucmd.sidemove = 0;
		// 	ucmd.upmove = 0;
		// 	ucmd.buttons &= ~IN_USE;
		// }

		if (IsDead())
			ucmd.Buttons &= ~InButtons.Use;

		if ((ucmd.ForwardMove != 0) || (ucmd.SideMove != 0) || (ucmd.UpMove != 0)) {
			IdleTime -= TICK_INTERVAL * 2.0f;

			if (IdleTime < 0.0f)
				IdleTime = 0.0f;

			MoveTime += TICK_INTERVAL;

			if (MoveTime > 4.0f)
				MoveTime = 4.0f;
		}
		else {
			IdleTime += TICK_INTERVAL;

			if (IdleTime > 4.0f)
				IdleTime = 4.0f;

			MoveTime -= TICK_INTERVAL * 2.0f;

			if (MoveTime < 0.0f)
				MoveTime = 0.0f;
		}

		base.PlayerRunCommand(ucmd, moveHelper);
	}

	public override void Spawn() {
#if !HL2MP
#if !PORTAL
		SetModel("models/player.mdl");
#endif
#endif

		base.Spawn();

		// if (!IsSuitEquipped())
		StartWalking();

		// SuitPower_SetCharge(100);

		// Local.HideHUD |= HideHudBits.Chat;

		// PlayerAISquad = g_AI_SquadManager.FindCreateSquad(AllocPooledString(PLAYER_SQUADNAME));

		InitSprinting();

#if HL2_EPISODIC
		HL2Local.FlashBattery = 100.0f;
#endif

		// GetPlayerProxy();

		// SetFlashlightPowerDrainScale(1.0f);
	}

	void UpdateLocatorPosition(Vector3 position) { }

	void InitSprinting() => StopSprinting();

	bool CanSprint() {
		throw new NotImplementedException();
	}

	void StartAutoSprint() { }

	void StartSprinting() { }

	void StopSprinting() { }

	public void EnableSprint(bool enable) {
		if (!enable && IsSprinting())
			StopSprinting();

		SprintEnabled = enable;
	}

	void StartWalking() {
		SetMaxSpeed(HL2_WALK_SPEED);
		m_bIsWalking = true;
	}

	void StopWalking() {
		SetMaxSpeed(HL2_NORM_SPEED);
		m_bIsWalking = false;
	}

	bool IsWalking() => m_bIsWalking;

	// float GetIdleTime() => IdleTime - MoveTime;

	// float GetMoveTime() => MoveTime - IdleTime;

	// float GetLastDamageTime() => LastDamageTime;

	bool IsDucking() => (GetFlags() & EntityFlags.Ducking) != 0;

	bool CanZoom(BaseEntity requester) {
		throw new NotImplementedException();
	}

	void ToggleZoom() { }

	void StartZooming() { }

	public void StopZooming() { }

	bool IsZooming() {
		throw new NotImplementedException();
	}

	void InitVCollision(Vector3 absOrigin, Vector3 absVelocity) { }

	// bool CommanderFindGoal(commandgoal_t goal) { }

	AI_BaseNPC GetSquadCommandRepresentative() {
		throw new NotImplementedException();
	}

	int GetNumSquadCommandables() {
		throw new NotImplementedException();
	}

	int GetNumSquadCommandableMedics() {
		throw new NotImplementedException();
	}

	void CommanderUpdate() { }

	// bool CommanderExecuteOne(AI_BaseNPC npc, commandgoal_t goal, AI_BaseNPC Allies, int numAllies) { }

	// void CommanderExecute(CommanderCommand_t command) { }

	void CommanderMode() { }

	void CheatImpulseCommands(int impulse) { }

	public override void SetupVisibility(BaseEntity? viewEntity, byte[] pvs, int pvssize) {
		base.SetupVisibility(viewEntity, pvs, pvssize);

		ScriptIntro? introScript = ScriptIntro.g_hIntroScript.Get();
		if (introScript != null) {
			if (introScript.GetIncludedPVSOrigin(out Vector3 origin, out _))
				engine.AddOriginToPVS(origin);
		}
	}

	void SuitPower_Update() { }

	void SuitPower_Initialize() { }

	bool SuitPower_Drain(float power) {
		throw new NotImplementedException();
	}

	void SuitPower_Charge(float power) { }

	// bool SuitPower_IsDeviceActive(SuitPowerDevice device) { }

	// bool SuitPower_AddDevice(SuitPowerDevice device) { }

	// bool SuitPower_RemoveDevice(SuitPowerDevice device) { }

	bool SuitPower_ShouldRecharge() {
		throw new NotImplementedException();
	}

	bool ApplyBattery(float powerMultiplier) {
		throw new NotImplementedException();
	}

	int FlashlightIsOn() {
		throw new NotImplementedException();
	}

	void FlashlightTurnOn() { }

	void FlashlightTurnOff() { }

	bool IsIlluminatedByFlashlight(BaseEntity entity, float returnDot) {
		throw new NotImplementedException();
	}

	void CheckFlashlight() { }

	public override void SetPlayerUnderwater(bool state) {
		// if (state)
		// 	SuitPower_AddDevice(SuitDeviceBreather);
		// else
		// 	SuitPower_RemoveDevice(SuitDeviceBreather);

		base.SetPlayerUnderwater(state);
	}

	// bool PassesDamageFilter(TakeDamageInfo info) { }

	void SetFlashlightEnabled(bool state) { }

	// void InputDisableFlashlight(inputdata_t inputdata ) { }

	// void InputEnableFlashlight(inputdata_t inputdata ) { }

	// void InputIgnoreFallDamage(inputdata_t inputdata ) { }

	// void InputIgnoreFallDamageWithoutReset(inputdata_t inputdata ) { }

	// void OnSquadMemberKilled(inputdata_t data ) { }

	void NotifyFriendsOfDamage(BaseEntity attackerEntity) { }

	// int OnTakeDamage(TakeDamageInfo info) { }

	// int OnTakeDamage_Alive(TakeDamageInfo info) { }

	// void OnDamagedByExplosion(TakeDamageInfo info) { }

	bool ShouldShootMissTarget(BaseCombatCharacter attacker) {
		throw new NotImplementedException();
	}

	void CombineBallSocketed(PropCombineBall combineBall) { }

	// void Event_KilledOther(BaseEntity pVictim, TakeDamageInfo info) { }

	// void Event_Killed(TakeDamageInfo info) { }

	void NotifyScriptsOfDeath() { }

	// void GetAutoaimVector(autoaim_params_t params ) { }

	bool ShouldKeepLockedAutoaimTarget(EHANDLE lockedTarget) {
		throw new NotImplementedException();
	}

	public override int GiveAmmo(int count, int ammoIndex, bool suppressSound = false) {
		if (ammoIndex < 0)
			return 0;

		bool checkAutoSwitch = false;
		if (!HasAnyAmmoOfType(ammoIndex))
			checkAutoSwitch = true;

		int add = base.GiveAmmo(count, ammoIndex, suppressSound);

		if (checkAutoSwitch) {
			BaseCombatWeapon? weapon = g_pGameRules.GetNextBestWeapon(this, GetActiveWeapon());

			if (weapon != null && weapon.PrimaryAmmoType == ammoIndex)
				SwitchToNextBestWeapon(GetActiveWeapon()!);
		}

		return add;
	}

	public override bool Weapon_CanUse(BaseCombatWeapon weapon) {
		return base.Weapon_CanUse(weapon);
	}

	public override void Weapon_Equip(BaseCombatWeapon weapon) {
		if (GetActiveWeapon() == null)
			HL2Local.WeaponLowered = false;

		base.Weapon_Equip(weapon);
	}

	public override bool BumpWeapon(BaseCombatWeapon weapon) {
		return base.BumpWeapon(weapon);
	}

	bool ClientCommand(in TokenizedCommand args) {
		throw new NotImplementedException();
	}

	public override void PlayerUse() {
		// Was use pressed or released?
		if (((Buttons | AfButtonPressed | AfButtonReleased) & InButtons.Use) == 0)
			return;

		if ((AfButtonPressed & InButtons.Use) != 0) {
			// Currently using a latched entity?
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
						EmitSound("HL2Player.TrainUse");
						return;
					}
				}
			}

			// Tracker 3926:  We can't +USE something if we're climbing a ladder
			if (GetMoveType() == Source.MoveType.Ladder)
				return;
		}

		if (TimeUseSuspended > gpGlobals.CurTime) {
			// Something has temporarily stopped us being able to USE things.
			// Obviously, this should be used very carefully.(sjb)
			return;
		}

		BaseEntity? useEntity = FindUseEntity();

		bool usedSomething = false;

		// Found an object
		if (useEntity != null) {
			//!!!UNDONE: traceline here to prevent +USEing buttons through walls
			EntityCapabilities caps = useEntity.ObjectCaps();
			Variant_t emptyVariant = default;

			if ((AfButtonPressed & InButtons.Use) != 0) {
				// Robin: Don't play sounds for NPCs, because NPCs will allow respond with speech.
				if (useEntity.MyNPCPointer() == null)
					EmitSound("HL2Player.Use");
			}

			if (((Buttons & InButtons.Use) != 0 && (caps & EntityCapabilities.ContinuousUse) != 0) ||
				 ((AfButtonPressed & InButtons.Use) != 0 && (caps & (EntityCapabilities.ImpulseUse | EntityCapabilities.OnOffUse)) != 0)) {
				if ((caps & EntityCapabilities.ContinuousUse) != 0)
					PhysicsFlags |= PlayerPhysFlag.Using;

				useEntity.AcceptInput("Use", this, this, emptyVariant, (int)UseType.Toggle);

				usedSomething = true;
			}
			// UNDONE: Send different USE codes for ON/OFF.  Cache last ONOFF_USE object to send 'off' if you turn away
			else if ((AfButtonReleased & InButtons.Use) != 0 && (useEntity.ObjectCaps() & EntityCapabilities.OnOffUse) != 0) {    // BUGBUG This is an "off" use
				useEntity.AcceptInput("Use", this, this, emptyVariant, (int)UseType.Toggle);

				usedSomething = true;
			}

#if HL2_SINGLE_PRIMARY_WEAPON_MODE

			//Check for weapon pick-up
			if ((AfButtonPressed & InButtons.Use) != 0) {
				BaseCombatWeapon? weapon = useEntity as BaseCombatWeapon;

				if ((weapon != null) && (Weapon_CanSwitchTo(weapon))) {
					//Try to take ammo or swap the weapon
					if (Weapon_OwnsThisType(weapon.GetClassname(), weapon.GetSubType()) != null) {
						Weapon_EquipAmmoOnly(weapon);
					}
					else {
						Weapon_DropSlot(weapon.GetSlot());
						Weapon_Equip(weapon);
					}

					usedSomething = true;
				}
			}
#endif
		}
		else if ((AfButtonPressed & InButtons.Use) != 0) {
			// Signal that we want to play the deny sound, unless the user is +USEing on a ladder!
			// The sound is emitted in ItemPostFrame, since that occurs after GameMovement::ProcessMove which
			// lets the ladder code unset this flag.
			m_bPlayUseDenySound = true;
		}

		// Debounce the use key
		if (usedSomething && useEntity != null) {
			Local.OldButtons |= (int)InButtons.Use;
			AfButtonPressed &= ~InButtons.Use;
		}
	}

	void UpdateWeaponPosture() { }

	bool Weapon_Lower() {
		throw new NotImplementedException();
	}

	bool Weapon_Ready() {
		throw new NotImplementedException();
	}

	bool Weapon_CanSwitchTo(BaseCombatWeapon weapon) {
		throw new NotImplementedException();
	}

	public override void PickupObject(BaseEntity obj, bool limitMassAndSize = true) => HL2_PickupObject(obj, limitMassAndSize);

	protected void HL2_PickupObject(BaseEntity obj, bool limitMassAndSize) {
		// can't pick up what you're standing on
		if (GetGroundEntity() == obj)
			return;

		if (limitMassAndSize == true) {
			if (CanPickupObject(obj, 35, 128) == false)
				return;
		}

		// Can't be picked up if NPCs are on me
		// if ( pObject->HasNPCsOnIt() )
		// 	return;

		PlayerPickupController.PlayerPickupObject(this, obj);
	}

	public bool IsHoldingEntity(BaseEntity? ent) {
		return PlayerPickupController.PlayerPickupControllerIsHoldingEntity(UseEntity.Get(), ent);
	}

	public override float GetHeldObjectMass(IPhysicsObject? heldObject) {
		float mass = PlayerPickupController.PlayerPickupGetHeldObjectMass(UseEntity.Get(), heldObject!);
		// if ( mass == 0.0f )
		// 	mass = PhysCannonGetHeldObjectMass( GetActiveWeapon(), pHeldObject );
		return mass;
	}

	/// <summary>
	/// Force the player to drop any physics objects he's carrying
	/// </summary>
	public override void ForceDropOfCarriedPhysObjects(BaseEntity? onlyIfHoldingThis) {
		if (PhysIsInCallback()) {
			Variant_t value = default;
			g_EventQueue.AddEvent(this, "ForceDropPhysObjects", value, 0.01f, onlyIfHoldingThis, this);
			return;
		}

		// Drop any objects being handheld.
		ClearUseEntity();

		// Then force the physcannon to drop anything it's holding, if it's our active weapon
		// PhysCannonForceDrop( GetActiveWeapon(), NULL );
	}

	public void InputForceDropPhysObjects(InputData data) {
		ForceDropOfCarriedPhysObjects(data.Activator);
	}

	void UpdateClientData() { }

	void OnRestore() { }

	public override Vector3 EyeDirection2D() {
		throw new NotImplementedException();
	}

	public override Vector3 EyeDirection3D() {
		Vector3 vecForward;

		// Return the vehicle angles if we request them
		if (GetVehicle() != null) {
			CacheVehicleView();
			EyeVectors(out vecForward);
			return vecForward;
		}

		Source.Common.Mathematics.MathLib.AngleVectors(EyeAngles(), out vecForward);
		return vecForward;
	}

	bool Weapon_Switch(BaseCombatWeapon weapon, int viewmodelindex) {
		throw new NotImplementedException();
	}

	// WeaponProficiency_t CalcWeaponProficiency(BaseCombatWeapon weapon) { }

	bool TestHitboxes(Ray ray, uint fContentsMask, Trace tr) {
		throw new NotImplementedException();
	}

	public override void DrawDebugGeometryOverlays() { }

	public void ExitLadder() { }

	SurfaceData GetLadderSurface(Vector3 origin) {
		throw new NotImplementedException();
	}

	/// <summary>
	/// Queues up a use deny sound, played in ItemPostFrame.
	/// </summary>
	public override void PlayUseDenySound() {
		m_bPlayUseDenySound = true;
	}

	public override void ItemPostFrame() {
		base.ItemPostFrame();

		if (m_bPlayUseDenySound) {
			m_bPlayUseDenySound = false;
			EmitSound("HL2Player.UseDeny");
		}
	}

	void StartWaterDeathSounds() { }

	void StopWaterDeathSounds() { }

	void MissedAR2AltFire() { }

	public void DisplayLadderHudHint() { }

	void StopLoopingSounds() { }

	// void ModifyOrAppendPlayerCriteria(AI_CriteriaSet set ) { }

	//  impactdamagetable_t GetPhysicsImpactDamageTable() { }

	void Splash() { }

	// LogicPlayerProxy GetPlayerProxy() { }

	// void FirePlayerProxyOutput(ReadOnlySpan<char> outputName, variant_t variant, BaseEntity activator, BaseEntity caller) { }

	public bool IsSprinting() => m_bIsSprinting;
}
