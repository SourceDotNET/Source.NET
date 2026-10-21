using Game.Server.HL2;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Commands;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server.HL2MP;

[NetworkName("CHL2MP_Player")]
public partial class HL2MP_Player : HL2_Player
{
	[NetworkTable("DT_HL2MPLocalPlayerExclusive")]
	public static readonly new NetworkProp[] LocalPlayerExclusiveProps = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.NoScale | PropFlags.ChangesOften, Low = 0.0f, High = Constants.HIGH_DEFAULT },
		new(nameof(AngEyeAngles)) { Encoding = NetEncoding.VectorElements, Count = 2, Bits = 11, Flags = PropFlags.ChangesOften | PropFlags.RoundDown },
	];

	[NetworkTable("DT_HL2MPNonLocalPlayerExclusive")]
	public static readonly NetworkProp[] NonLocalPlayerExclusiveProps = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.CoordMPLowPrecision | PropFlags.ChangesOften, Low = 0.0f, High = Constants.HIGH_DEFAULT },
		new(nameof(AngEyeAngles)) { Encoding = NetEncoding.VectorElements, Count = 2, Bits = 11, Flags = PropFlags.ChangesOften | PropFlags.RoundDown },
	];

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(PoseParameter)) { Exclude = true },
		new(nameof(PlaybackRate)) { Exclude = true },
		new(nameof(Sequence)) { Exclude = true },
		new(nameof(Rotation)) { Exclude = true },
		new("overlay_vars") { Exclude = true, DataTable = "DT_BaseAnimatingOverlay" },

		new(nameof(Origin)) { Exclude = true },
		new(nameof(Cycle)) { Exclude = true, DataTable = "DT_ServerAnimationData" },
		new(nameof(AnimTime)) { Exclude = true, DataTable = "DT_AnimTimeMustBeFirst" },
		new(nameof(FlexWeight)) { Exclude = true },
		new(nameof(BlinkToggle)) { Exclude = true },
		new("m_viewtarget") { Exclude = true, DataTable = "DT_BaseFlex" },

		new(nameof(LocalPlayerExclusiveProps)) { Name = "hl2mplocaldata", Proxy = typeof(HL2MPLocalDataProxy) },
		new(nameof(NonLocalPlayerExclusiveProps)) { Name = "hl2mpnonlocaldata", Proxy = typeof(HL2MPNonLocalDataProxy) },

		nameof(Ragdoll),
		new(nameof(SpawnInterpCounter)) { Bits = 4 },
		nameof(IsWalking),
	];

	public static class HL2MPLocalDataProxy
	{
		public static object? Send(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID)
			=> SendProxy_SendLocalDataTable(prop, instance, data, recipients, objectID);
	}

	public static class HL2MPNonLocalDataProxy
	{
		public static object? Send(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID)
			=> SendProxy_SendNonLocalDataTable(prop, instance, data, recipients, objectID);
	}

	public const TimeUnit_t MODEL_CHANGE_INTERVAL = 5.0;
	public const TimeUnit_t TEAM_CHANGE_INTERVAL = 5.0;

	static readonly string[] RandomCitizenModels = [
		"models/humans/group03/male_01.mdl",
		"models/humans/group03/male_02.mdl",
		"models/humans/group03/female_01.mdl",
		"models/humans/group03/male_03.mdl",
		"models/humans/group03/female_02.mdl",
		"models/humans/group03/male_04.mdl",
		"models/humans/group03/female_03.mdl",
		"models/humans/group03/male_05.mdl",
		"models/humans/group03/female_04.mdl",
		"models/humans/group03/male_06.mdl",
		"models/humans/group03/female_06.mdl",
		"models/humans/group03/male_07.mdl",
		"models/humans/group03/female_07.mdl",
		"models/humans/group03/male_08.mdl",
		"models/humans/group03/male_09.mdl",
	];

	static readonly string[] RandomCombineModels = [
		"models/combine_soldier.mdl",
		"models/combine_soldier_prisonguard.mdl",
		"models/combine_super_soldier.mdl",
		"models/police.mdl",
	];

	static int LastCitizenModel = 0;
	static int LastCombineModel = 0;

	static BaseEntity? g_LastCombineSpawn = null;
	static BaseEntity? g_LastRebelSpawn = null;

	public int ModelType;

	[NetworkVar("m_angEyeAngles")]
	public partial QAngle AngEyeAngles { get; set; }
	[NetworkVar("m_hRagdoll")]
	public partial EHANDLE Ragdoll { get; set; }
	[NetworkVar("m_iSpawnInterpCounter")]
	public partial int SpawnInterpCounter { get; set; }
	public int PlayerSoundType;
	[NetworkVar("m_fIsWalking")]
	public partial bool IsWalking { get; set; }

	public readonly HL2MPPlayerAnimState PlayerAnimState;

	TimeUnit_t NextModelChangeTime;
	TimeUnit_t NextTeamChangeTime;
	TimeUnit_t SlamProtectTime;

	public HL2MP_Player() {
		Ragdoll = new();
		PlayerAnimState = HL2MPPlayerAnimState.CreateHL2MPPlayerAnimState(this);
		AngEyeAngles.Init();

		base.ChangeTeam(0);

#if GMOD_DLL
		UseClientSideAnimation(true);
#endif
	}

	public override void UpdateOnRemove() {
		if (Ragdoll.Get() != null) {
			Util.RemoveImmediate(Ragdoll.Get());
			Ragdoll = EHANDLE.FromEntity(null);
		}

		base.UpdateOnRemove();
	}

	public override void Precache() {
		base.Precache();

		PrecacheModel("sprites/glow01.vmt");

		for (int i = 0; i < RandomCitizenModels.Length; ++i)
			PrecacheModel(RandomCitizenModels[i]);

		for (int i = 0; i < RandomCombineModels.Length; ++i)
			PrecacheModel(RandomCombineModels[i]);

		// PrecacheFootStepSounds();

		PrecacheScriptSound("NPC_MetroPolice.Die");
		PrecacheScriptSound("NPC_CombineS.Die");
		PrecacheScriptSound("NPC_Citizen.die");
	}

	void GiveAllItems() { }

	void GiveDefaultItems() { }

	void PickDefaultSpawnTeam() {
		if (GetTeamNumber() == 0) {
			if (HL2MPRules().IsTeamplay() == false) {
				if (GetModelPtr() == null) {
					ReadOnlySpan<char> modelName = engine.GetClientConVarValue(engine.IndexOfEdict(Edict()), "cl_playermodel");

					if (ValidatePlayerModel(modelName) == false)
						engine.ClientCommand(Edict(), "cl_playermodel models/combine_soldier.mdl\n");

					ChangeTeam(Constants.TEAM_UNASSIGNED);
				}
			}
			else {
				Team? combine = GetGlobalTeam(TEAM_COMBINE);
				Team? rebels = GetGlobalTeam(TEAM_REBELS);

				if (combine == null || rebels == null)
					ChangeTeam(random.RandomInt(TEAM_COMBINE, TEAM_REBELS));
				else {
					if (combine.GetNumPlayers() > rebels.GetNumPlayers())
						ChangeTeam(TEAM_REBELS);
					else if (combine.GetNumPlayers() < rebels.GetNumPlayers())
						ChangeTeam(TEAM_COMBINE);
					else
						ChangeTeam(random.RandomInt(TEAM_COMBINE, TEAM_REBELS));
				}
			}
		}
	}

	public override void Spawn() {
		NextModelChangeTime = 0;
		NextTeamChangeTime = 0;

		PickDefaultSpawnTeam();

		base.Spawn();

		if (!IsObserver()) {
			pl.DeadFlag = false;
			// RemoveSolidFlags(SolidFlags.NotSolid);
			RemoveEffects(EntityEffects.NoDraw);
			GiveDefaultItems();
		}

		SetNumAnimOverlays(3);
		ResetAnimation();

		RenderFX = (byte)Source.RenderMode.Normal;

		Local.HideHUD = false;

		AddFlag(EntityFlags.OnGround);

		// ImpactEnergyScale todo

		// if (HL2MPRules().IsIntermission()) {} else {
		RemoveFlag(EntityFlags.Frozen);

		SpawnInterpCounter = (SpawnInterpCounter + 1) % 8;

		Local.Ducked = false;

		SetPlayerUnderwater(false);

		// Ready = true;
	}

	public override void PickupObject(BaseEntity obj, bool limitMassAndSize = true) {

	}

	bool ValidatePlayerModel(ReadOnlySpan<char> model) {
		for (int i = 0; i < RandomCitizenModels.Length; ++i)
			if (stricmp(RandomCitizenModels[i], model) == 0)
				return true;

		for (int i = 0; i < RandomCombineModels.Length; ++i)
			if (stricmp(RandomCombineModels[i], model) == 0)
				return true;

		return false;
	}

	void SetPlayerTeamModel() {
		ReadOnlySpan<char> modelName = engine.GetClientConVarValue(engine.IndexOfEdict(Edict()), "cl_playermodel");

		int modelIndex = modelinfo.GetModelIndex(modelName);

		if (modelIndex == -1 || ValidatePlayerModel(modelName) == false) {
			modelName = "models/Combine_Soldier.mdl";
			ModelType = TEAM_COMBINE;

			engine.ClientCommand(Edict(), $"cl_playermodel {modelName}\n");
		}

		if (GetTeamNumber() == TEAM_COMBINE) {
			if (!stristr(modelName, "models/human").IsEmpty) {
				LastCombineModel = (LastCombineModel + 1) % RandomCombineModels.Length;
				modelName = RandomCombineModels[LastCombineModel];
			}

			ModelType = TEAM_COMBINE;
		}
		else if (GetTeamNumber() == TEAM_REBELS) {
			if (stristr(modelName, "models/human").IsEmpty) {
				LastCitizenModel = (LastCitizenModel + 1) % RandomCitizenModels.Length;
				modelName = RandomCitizenModels[LastCitizenModel];
			}

			ModelType = TEAM_REBELS;
		}

		SetModel(modelName);
		SetupPlayerSoundsByModel(modelName);

		NextModelChangeTime = gpGlobals.CurTime + MODEL_CHANGE_INTERVAL;
	}

	void SetPlayerModel() {
		ReadOnlySpan<char> currentModelName = modelinfo.GetModelName(GetModel());
		ReadOnlySpan<char> modelName = engine.GetClientConVarValue(engine.IndexOfEdict(Edict()), "cl_playermodel");

		if (ValidatePlayerModel(modelName) == false) {
			if (ValidatePlayerModel(currentModelName) == false)
				currentModelName = "models/Combine_Soldier.mdl";

			engine.ClientCommand(Edict(), $"cl_playermodel {currentModelName}\n");

			modelName = currentModelName;
		}

		if (GetTeamNumber() == TEAM_COMBINE) {
			LastCombineModel = (LastCombineModel + 1) % RandomCombineModels.Length;
			modelName = RandomCombineModels[LastCombineModel];

			ModelType = TEAM_COMBINE;
		}
		else if (GetTeamNumber() == TEAM_REBELS) {
			LastCitizenModel = (LastCitizenModel + 1) % RandomCitizenModels.Length;
			modelName = RandomCitizenModels[LastCitizenModel];

			ModelType = TEAM_REBELS;
		}
		else {
			if (strlen(modelName) == 0)
				modelName = RandomCitizenModels[0];

			if (!stristr(modelName, "models/human").IsEmpty)
				ModelType = TEAM_REBELS;
			else
				ModelType = TEAM_COMBINE;
		}

		int modelIndex = modelinfo.GetModelIndex(modelName);

		if (modelIndex == -1) {
			modelName = "models/Combine_Soldier.mdl";
			ModelType = TEAM_COMBINE;

			engine.ClientCommand(Edict(), $"cl_playermodel {modelName}\n");
		}

		SetModel(modelName);
		SetupPlayerSoundsByModel(modelName);

		NextModelChangeTime = gpGlobals.CurTime + MODEL_CHANGE_INTERVAL;
	}

	void SetupPlayerSoundsByModel(ReadOnlySpan<char> modelName) { }

	void ResetAnimation() {
		if (IsAlive()) {
			SetSequence(-1);
			// SetActivity(Activity.ACT_INVALID);

			if (GetAbsVelocity().X == 0 && GetAbsVelocity().Y == 0)
				SetAnimation(PlayerAnim.Idle);
			else if ((GetAbsVelocity().X != 0 || GetAbsVelocity().Y != 0) && (GetFlags() & EntityFlags.OnGround) != 0)
				SetAnimation(PlayerAnim.Walk);
			else if (GetWaterLevel() > Shared.WaterLevel.Feet)
				SetAnimation(PlayerAnim.Walk);
		}
	}

	public override bool Weapon_Switch(BaseCombatWeapon? weapon, int viewmodelindex = 0) {
		bool ret = base.Weapon_Switch(weapon, viewmodelindex);

		if (ret == true)
			ResetAnimation();

		return ret;
	}

	public override void PreThink() {
		QAngle oldAngles = GetLocalAngles();
		QAngle tempAngles = EyeAngles();

		if (tempAngles[PITCH] > 180)
			tempAngles[PITCH] -= 360;

		SetLocalAngles(tempAngles);

		base.PreThink();
		State_PreThink();

		// TotalBulletForce = vec3_origin;
		SetLocalAngles(oldAngles);
	}

	public void DoAnimationEvent(PlayerAnimEvent evt, int data, bool predicted) {
		PlayerAnimState.DoAnimationEvent(evt, data);
		TEPlayerAnimEvent.TE_PlayerAnimEvent(this, evt, data, predicted);
	}

	public override void PostThink() {
		base.PostThink();

		if ((GetFlags() & EntityFlags.Ducking) != 0) {
			// collision bounds todo
		}

		PlayerAnimState.Update(AngEyeAngles[YAW], AngEyeAngles[PITCH]);

		AngEyeAngles = EyeAngles();

		QAngle angles = GetLocalAngles();
		angles[PITCH] = 0;
		SetLocalAngles(angles);
	}

	void PlayerDeathThink() {
		// if (!IsObserver())
		// 	base.PlayerDeathThink();
	}

	void FireBullets(FireBulletsInfo info) { }

	void NoteWeaponFired() { }

	public override bool WantsLagCompensationOnEntity(BaseEntity entity, in UserCmd cmd, ref readonly MaxEdictsBitSet entityTransmitBits) {
		return base.WantsLagCompensationOnEntity(entity, in cmd, in entityTransmitBits);
	}

	static Activity TranslateTeamActivity(Activity actToTranslate) {
		// if (ModelType == TEAM_COMBINE)
		// 	return actToTranslate; // todo

		if (actToTranslate == Activity.ACT_RUN)
			return Activity.ACT_RUN_AIM_AGITATED;

		if (actToTranslate == Activity.ACT_IDLE)
			return Activity.ACT_IDLE_AIM_AGITATED;

		if (actToTranslate == Activity.ACT_WALK)
			return Activity.ACT_WALK_AIM_AGITATED;

		return actToTranslate;
	}

	public override void SetAnimation(PlayerAnim playerAnim) {
		int animDesired;
		float speed = GetAbsVelocity().Length();

		if ((GetFlags() & (EntityFlags.Frozen | EntityFlags.AtControls)) != 0) {
			speed = 0;
			playerAnim = PlayerAnim.Idle;
		}

		Activity idealActivity = Activity.ACT_HL2MP_RUN;
		if (playerAnim == PlayerAnim.Jump)
			idealActivity = Activity.ACT_HL2MP_JUMP;
		else if (playerAnim == PlayerAnim.Die) {
			if (LifeState == 0)
				return;
		}
		else if (playerAnim == PlayerAnim.Attack1) {
			if (GetActivity() == Activity.ACT_HOVER ||
					GetActivity() == Activity.ACT_SWIM ||
					GetActivity() == Activity.ACT_HOP ||
					GetActivity() == Activity.ACT_LEAP ||
					GetActivity() == Activity.ACT_DIESIMPLE) {
				idealActivity = GetActivity();
			}
			else
				idealActivity = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK;
		}
		else if (playerAnim == PlayerAnim.Reload) {
			idealActivity = Activity.ACT_HL2MP_GESTURE_RELOAD;
		}
		else if (playerAnim == PlayerAnim.Idle || playerAnim == PlayerAnim.Walk) {
			if ((GetFlags() & EntityFlags.OnGround) == 0 && GetActivity() == Activity.ACT_HL2MP_JUMP) // Still jumping
				idealActivity = GetActivity();
			else {
				if ((GetFlags() & EntityFlags.Ducking) != 0) {
					if (speed > 0)
						idealActivity = Activity.ACT_HL2MP_WALK_CROUCH;
					else
						idealActivity = Activity.ACT_HL2MP_IDLE_CROUCH;
				}
				else {
					if (speed > 0)
						idealActivity = Activity.ACT_HL2MP_RUN;
					else
						idealActivity = Activity.ACT_HL2MP_IDLE;
				}
			}

			idealActivity = TranslateTeamActivity(idealActivity);
		}

		if (idealActivity == Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK) {
			// RestartGesture(Weapon_TranslateActivity(idealActivity));
			// Weapon_SetActivity(Weapon_TranslateActivity(Activity.ACT_RANGE_ATTACK1), 0);
			return;
		}
		else if (idealActivity == Activity.ACT_HL2MP_GESTURE_RELOAD) {
			// RestartGesture(Weapon_TranslateActivity(idealActivity));
			return;
		}
		else {
			// SetActivity(idealActivity);

			animDesired = 0;//SelectWeightedSequence(Weapon_TranslateActivity(idealActivity));

			if (animDesired == -1) {
				animDesired = SelectWeightedSequence(idealActivity);

				if (animDesired == -1)
					animDesired = 0;
			}

			if (GetSequence() == animDesired)
				return;

			PlaybackRate = 1.0;
			ResetSequence(animDesired);
			SetCycle(0);
			return;
		}

		if (GetSequence() == animDesired)
			return;

		ResetSequence(animDesired);
		SetCycle(0);
	}

	public override bool BumpWeapon(BaseCombatWeapon weapon) {
		BaseCombatCharacter? owner = weapon.GetOwner();

		if (!IsAllowedToPickupWeapons())
			return false;

		if (owner != null || !Weapon_CanUse(weapon) || !g_pGameRules.CanHavePlayerItem(this, weapon)) {
			if (gEvilImpulse101)
				Util.Remove(weapon);
			return false;
		}

		if (!weapon.FVisible(this, Source.Common.Formats.BSP.Mask.Solid) && (GetFlags() & EntityFlags.NoTarget) == 0)
			return false;

		bool ownsWeaponAlready = Weapon_OwnsThisType(weapon.GetClassname(), weapon.GetSubType()) != null;

		if (ownsWeaponAlready == true) {
			if (Weapon_EquipAmmoOnly(weapon)) {
				weapon.CheckRespawn();

				Util.Remove(weapon);
				return true;
			}
			else
				return false;
		}

		weapon.CheckRespawn();
		Weapon_Equip(weapon);

		return true;
	}

#if !GMOD_DLL
	public override void ChangeTeam(int team, bool autoTeam = false, bool silent = false, bool autoBalance = false) {
		bool kill = false;

		if (HL2MPRules().IsTeamplay() != true && team != Constants.TEAM_SPECTATOR)
			team = Constants.TEAM_UNASSIGNED;

		if (HL2MPRules().IsTeamplay() == true)
			if (team != GetTeamNumber() && GetTeamNumber() != Constants.TEAM_UNASSIGNED)
				kill = true;

		base.ChangeTeam(team, autoTeam, silent, autoBalance);

		NextTeamChangeTime = gpGlobals.CurTime + TEAM_CHANGE_INTERVAL;

		if (HL2MPRules().IsTeamplay() == true)
			SetPlayerTeamModel();
		else
			SetPlayerModel();

		if (team == Constants.TEAM_SPECTATOR) {
			// RemoveAllItems(true);

			// State_Transition(HL2MPPlayerState.ObserverMode);
		}

		if (kill == true)
			CommitSuicide();
	}
#endif

	bool HandleCommand_JoinTeam(int team) {
		throw new NotImplementedException();
	}

	public bool ClientCommand(in TokenizedCommand args) {
		throw new NotImplementedException();
	}

	void CheatImpulseCommands(int impulse) { }

	bool ShouldRunRateLimitedCommand(in TokenizedCommand args) {
		throw new NotImplementedException();
	}

	public override void CreateViewModel(int index = 0) {
		Assert(index >= 0 && index < MAX_VIEWMODELS);

		if (GetViewModel(index) != null)
			return;

		PredictedViewModel? vm = (PredictedViewModel?)CreateEntityByName("predicted_viewmodel");
		if (vm != null) {
			vm.SetAbsOrigin(GetAbsOrigin());
			vm.SetOwner(this);
			vm.SetIndex(index);
			Util.DispatchSpawn(vm);
			vm.FollowEntity(this, false);
			ViewModelForModify()[index].Set(vm);
		}
	}

	bool BecomeRagdollOnClient(Vector3 force) {
		throw new NotImplementedException();
	}

	void CreateRagdollEntity() { }

	int FlashlightIsOn() {
		throw new NotImplementedException();
	}

	void FlashlightTurnOn() { }

	void FlashlightTurnOff() { }

	void Weapon_Drop(BaseCombatWeapon weapon, Vector3 target, Vector3 velocity) { }

	void DetonateTripmines() { }

	// void Event_Killed(TakeDamageInfo info) {
	// 	throw new NotImplementedException();
	// }

	// int OnTakeDamage(TakeDamageInfo inputInfo) {
	// 	throw new NotImplementedException();
	// }

	// void DeathSound(TakeDamageInfo info) {
	// 	throw new NotImplementedException();
	// }

	public override BaseEntity EntSelectSpawnPoint() {
		BaseEntity? spot = null;
		BaseEntity? lastSpawnPoint = g_LastSpawn;
		Edict player = Edict();
		ReadOnlySpan<char> spawnpointName = "info_player_deathmatch";

		if (HL2MPRules().IsTeamplay() == true) {
			if (GetTeamNumber() == TEAM_COMBINE) {
				spawnpointName = "info_player_combine";
				lastSpawnPoint = g_LastCombineSpawn;
			}
			else if (GetTeamNumber() == TEAM_REBELS) {
				spawnpointName = "info_player_rebel";
				lastSpawnPoint = g_LastRebelSpawn;
			}

			if (gEntList.FindEntityByClassname(null, spawnpointName) == null) {
				spawnpointName = "info_player_deathmatch";
				lastSpawnPoint = g_LastSpawn;
			}
		}

		spot = lastSpawnPoint;

		for (int i = random.RandomInt(1, 5); i > 0; i--)
			spot = gEntList.FindEntityByClassname(spot, spawnpointName);
		if (spot == null)
			spot = gEntList.FindEntityByClassname(spot, spawnpointName);

		BaseEntity? firstSpot = spot;

		do {
			if (spot != null) {
				// if (GameRules.IsSpawnPointValid(spot, this)) {
				// 	if (spot.GetLocalOrigin() == vec3_origin) {
				// 		spot = gEntList.FindEntityByClassname(spot, spawnpointName);
				// 		continue;
				// 	}

				goto ReturnSpot;
				// }
			}
			spot = gEntList.FindEntityByClassname(spot, spawnpointName);
		} while (spot != firstSpot);

		if (spot != null) {
			BaseEntity? ent = null;
			// for (EntitySphereQuery sphere(spot.GetAbsOrigin(), 128); (ent = sphere.GetCurrentEntity()) != null; sphere.NextEntity()) {
			// 	if (ent.IsPlayer() && !(ent.edict() == player))
			// 		ent.TakeDamage(CTakeDamageInfo(GetContainingEntity(INDEXENT(0)), GetContainingEntity(INDEXENT(0)), 300, DMG_GENERIC));
			// }
			goto ReturnSpot;
		}

		if (spot == null) {
			spot = gEntList.FindEntityByClassname(spot, "info_player_start");

			if (spot != null)
				goto ReturnSpot;
		}

	ReturnSpot:

		if (HL2MPRules().IsTeamplay() == true) {
			if (GetTeamNumber() == TEAM_COMBINE)
				g_LastCombineSpawn = spot;
			else if (GetTeamNumber() == TEAM_REBELS)
				g_LastRebelSpawn = spot;
		}

		g_LastSpawn = spot;

		SlamProtectTime = gpGlobals.CurTime + 0.5;

		return spot;
	}

	void Reset() { }

	bool IsReady() {
		throw new NotImplementedException();
	}

	void SetReady(bool ready) { }

	void CheckChatText(ReadOnlySpan<char> text) { }

	// void State_Transition(HL2MPPlayerState newState) {
	// 	throw new NotImplementedException();
	// }

	// void State_Enter(HL2MPPlayerState newState) {
	// 	throw new NotImplementedException();
	// }

	void State_Leave() { }

	void State_PreThink() {
		// pfnPreThink todo
	}

	// CHL2MPPlayerStateInfo State_LookupInfo(HL2MPPlayerState state) {
	// 	throw new NotImplementedException();
	// }

	bool StartObserverMode(int mode) {
		throw new NotImplementedException();
	}

	public override void StopObserverMode() {
		// todo
		base.StopObserverMode();
	}

	void State_Enter_OBSERVER_MODE() { }

	void State_PreThink_OBSERVER_MODE() { }

	void State_Enter_ACTIVE() { }

	void State_PreThink_ACTIVE() { }

	bool CanHearAndReadChatFrom(BasePlayer player) => player != null;
}

[LinkEntityToClass("hl2mp_ragdoll")]
[NetworkName("CHL2MPRagdoll", NoBase = true)]
public partial class HL2MPRagdoll : BaseAnimatingOverlay
{
	public HL2MPRagdoll() {
		Player = new();
	}

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(RagdollOrigin)) { Bits = 0, Flags = PropFlags.Coord },
		nameof(Player),
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(ForceBone)) { Bits = 8 },
		new(nameof(Force)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(RagdollVelocity)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_vecRagdollOrigin")]
	public partial Vector3 RagdollOrigin { get; set; }
	[NetworkVar("m_hPlayer")]
	public partial EHANDLE Player { get; set; }
	[NetworkVar("m_vecRagdollVelocity")]
	public partial Vector3 RagdollVelocity { get; set; }
}
