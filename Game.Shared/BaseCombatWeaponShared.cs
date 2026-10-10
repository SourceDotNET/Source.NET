#if CLIENT_DLL || GAME_DLL
#if CLIENT_DLL
global using BaseCombatWeapon = Game.Client.C_BaseCombatWeapon;
global using WeaponHL2MPBase = Game.Client.C_WeaponHL2MPBase;
global using BaseHL2MPCombatWeapon = Game.Client.C_BaseHL2MPCombatWeapon;
#elif GAME_DLL
global using BaseCombatWeapon = Game.Server.BaseCombatWeapon;
global using WeaponHL2MPBase = Game.Server.WeaponHL2MPBase;
global using BaseHL2MPCombatWeapon = Game.Server.BaseHL2MPCombatWeapon;
#endif

using Source.Common;
using Source;

using Game.Shared;

using System.Numerics;

using Source.Common.Engine;

using System.Diagnostics;

#if CLIENT_DLL
using Game.Client.HUD;

using Microsoft.VisualBasic;

using System.Reflection;


namespace Game.Client;
#else
namespace Game.Server;
#endif

using Source.Common.SoundEmitterSystem;

using System.Runtime.CompilerServices;

using Source.Common.Audio;

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
#if CLIENT_DLL || GAME_DLL
using FIELD = Source.FIELD<BaseCombatWeapon>;
using DEFINE = Source.DEFINE<BaseCombatWeapon>;

using Microsoft.VisualBasic;

using System.Reflection;
using System.Runtime.InteropServices;
using System.Reflection.Metadata;

#endif

public struct DmgAccumulator
{
	public DmgAccumulator() {

		Active = false;
#if GAME_DLL
		TargetsDmgInfo = [];
#endif
	}

#if GAME_DLL
	public void Start() => Active = true;
	public void AccumulateMultiDamage(in TakeDamageInfo info, BaseEntity? entity) {
		if (null == entity)
			return;

		Assert(Active);

#if GAME_DLL
		ref TakeDamageInfo tInfo = ref CollectionsMarshal.GetValueRefOrNullRef(TargetsDmgInfo, entity.EntIndex());
		if (!Unsafe.IsNullRef(ref tInfo)) {
			// Update
			tInfo.AddDamageType(info.GetDamageType());
			tInfo.SetDamage(tInfo.GetDamage() + info.GetDamage());
			tInfo.SetDamageForce(tInfo.GetDamageForce() + info.GetDamageForce());
			tInfo.SetDamagePosition(info.GetDamagePosition());
			tInfo.SetReportedPosition(info.GetReportedPosition());
			tInfo.SetMaxDamage(Math.Max(tInfo.GetMaxDamage(), info.GetDamage()));
			tInfo.SetAmmoType(info.GetAmmoType());
		}
#endif
	}
	public void Process() {
		foreach (var kvp in TargetsDmgInfo) {
			BaseEntity? entity = Util.EntityByIndex(kvp.Key);
			if (entity != null)
				AddMultiDamage(kvp.Value, entity);
		}

		Active = false;
		TargetsDmgInfo.Clear();
	}

	TakeDamageInfo UpdatedInfo;
	readonly Dictionary<int, TakeDamageInfo> TargetsDmgInfo;
#endif

	private bool Active;
}

[NetworkName("CBaseCombatWeapon")]
public partial class
#if CLIENT_DLL
		C_BaseCombatWeapon : C_BaseAnimating
#elif GAME_DLL
	BaseCombatWeapon : BaseAnimating
#else
	SHUT_UP_ABOUT_GAME_SHARED_INTELLISENSE
#endif
{
#if CLIENT_DLL
	public static readonly new DataMap PredMap = new(typeof(C_BaseCombatWeapon), C_BaseAnimating.PredMap, [
	DEFINE.PRED_FIELD(nameof(NextThinkTick), FieldType.Integer, FieldTypeDescFlags.InSendTable ),
	DEFINE.PRED_FIELD(nameof(Owner), FieldType.EHandle, FieldTypeDescFlags.InSendTable ),
	DEFINE.PRED_FIELD(nameof(State), FieldType.Integer, FieldTypeDescFlags.InSendTable ),
	DEFINE.PRED_FIELD(nameof(iViewModelIndex), FieldType.Integer, FieldTypeDescFlags.InSendTable | FieldTypeDescFlags.ModelIndex ),
	DEFINE.PRED_FIELD(nameof(WorldModelIndex), FieldType.Integer, FieldTypeDescFlags.InSendTable | FieldTypeDescFlags.ModelIndex ),
	DEFINE.PRED_FIELD_TOL(nameof(NextPrimaryAttack), FieldType.Double, FieldTypeDescFlags.InSendTable, TD_MSECTOLERANCE ),
	DEFINE.PRED_FIELD_TOL(nameof(NextSecondaryAttack), FieldType.Double, FieldTypeDescFlags.InSendTable, TD_MSECTOLERANCE ),
	DEFINE.PRED_FIELD_TOL(nameof(TimeWeaponIdle), FieldType.Double, FieldTypeDescFlags.InSendTable, TD_MSECTOLERANCE ),
	DEFINE.PRED_FIELD(nameof(PrimaryAmmoType), FieldType.Integer, FieldTypeDescFlags.InSendTable ),
	DEFINE.PRED_FIELD(nameof(SecondaryAmmoType), FieldType.Integer, FieldTypeDescFlags.InSendTable ),
	DEFINE.PRED_FIELD(nameof(iClip1), FieldType.Integer, FieldTypeDescFlags.InSendTable ),
	DEFINE.PRED_FIELD(nameof(iClip2), FieldType.Integer, FieldTypeDescFlags.InSendTable ),
	DEFINE.PRED_FIELD(nameof(nViewModelIndex), FieldType.Integer, FieldTypeDescFlags.InSendTable ),
	DEFINE.FIELD(nameof(InReload), FieldType.Boolean ),
	DEFINE.FIELD(nameof(FireOnEmpty), FieldType.Boolean ),
	DEFINE.FIELD(nameof(FiringWholeClip), FieldType.Boolean ),
	DEFINE.FIELD(nameof(NextEmptySoundTime), FieldType.Float ),
	DEFINE.FIELD(nameof(Activity), FieldType.Integer ),
	DEFINE.FIELD(nameof(FireDuration), FieldType.Float ),
	DEFINE.FIELD(nameof(FiresUnderwater), FieldType.Boolean ),
	DEFINE.FIELD(nameof(AltFiresUnderwater), FieldType.Boolean ),
	DEFINE.FIELD(nameof(MinRange1), FieldType.Float ),
	DEFINE.FIELD(nameof(MinRange2), FieldType.Float ),
	DEFINE.FIELD(nameof(MaxRange1), FieldType.Float ),
	DEFINE.FIELD(nameof(MaxRange2), FieldType.Float ),
	DEFINE.FIELD(nameof(ReloadsSingly), FieldType.Boolean ),
	DEFINE.FIELD(nameof(Removable), FieldType.Boolean ),
	DEFINE.FIELD(nameof(PrimaryAmmoCount), FieldType.Integer ),
	DEFINE.FIELD(nameof(SecondaryAmmoCount), FieldType.Integer ),
]); public override DataMap? GetPredDescMap() => PredMap;
#endif
#if !CLIENT_DLL && !GAME_DLL // God intellisense is annoying me. Fixme when we can get Intellisense to shut up about Game.Shared (it never gets built)
	public static readonly Table DT_BaseAnimating = new();
#endif
	public static readonly Table DT_LocalWeaponData = new(nameof(DT_LocalWeaponData), [
#if CLIENT_DLL
		RecvPropIntWithMinusOneFlag(FIELD.OF(nameof(iClip1))),
		RecvPropIntWithMinusOneFlag(FIELD.OF(nameof(iClip2))),
		RecvPropInt(FIELD.OF(nameof(PrimaryAmmoType))),
		RecvPropInt(FIELD.OF(nameof(SecondaryAmmoType))),
		RecvPropInt(FIELD.OF(nameof(nViewModelIndex))),
		RecvPropBool(FIELD.OF(nameof(FlipViewModel))),
#elif GAME_DLL
		SendPropIntWithMinusOneFlag(FIELD.OF(nameof(iClip1)), 16),
		SendPropIntWithMinusOneFlag(FIELD.OF(nameof(iClip2)), 16),
		SendPropInt(FIELD.OF(nameof(PrimaryAmmoType)), 9),
		SendPropInt(FIELD.OF(nameof(SecondaryAmmoType)), 9),
		SendPropInt(FIELD.OF(nameof(nViewModelIndex)), BaseViewModel.VIEWMODEL_INDEX_BITS, PropFlags.Unsigned),
		SendPropBool(FIELD.OF(nameof(FlipViewModel))),
#endif
	]);

	public static readonly Table DT_LocalActiveWeaponData = new(nameof(DT_LocalActiveWeaponData), [
#if CLIENT_DLL
		RecvPropTime64(FIELD.OF(nameof(NextPrimaryAttack))),
		RecvPropTime64(FIELD.OF(nameof(NextSecondaryAttack))),
		RecvPropInt(FIELD.OF(nameof(NextThinkTick))),
		RecvPropTime64(FIELD.OF(nameof(TimeWeaponIdle))),
#elif GAME_DLL
		SendPropTime64(FIELD.OF(nameof(NextPrimaryAttack))),
		SendPropTime64(FIELD.OF(nameof(NextSecondaryAttack))),
		SendPropInt(FIELD.OF(nameof(NextThinkTick))),
		SendPropTime64(FIELD.OF(nameof(TimeWeaponIdle))),
#endif
	]);

	public static readonly Table DT_BaseCombatWeapon = new(DT_BaseAnimating, [
#if CLIENT_DLL
		RecvPropDataTable("LocalWeaponData", DT_LocalWeaponData),
		RecvPropDataTable("LocalActiveWeaponData", DT_LocalActiveWeaponData),
		RecvPropInt(FIELD.OF(nameof(iViewModelIndex))),
		RecvPropInt(FIELD.OF(nameof(WorldModelIndex))),
		RecvPropInt(FIELD.OF(nameof(State)), 0, RecvProxy_WeaponState),
		RecvPropEHandle(FIELD.OF(nameof(Owner))),
#elif GAME_DLL
		SendPropDataTable("LocalWeaponData", DT_LocalWeaponData, SendProxy_SendLocalWeaponDataTable),
		SendPropDataTable("LocalActiveWeaponData", DT_LocalActiveWeaponData, SendProxy_SendActiveLocalWeaponDataTable ),
		SendPropModelIndex(FIELD.OF(nameof(iViewModelIndex))),
		SendPropModelIndex(FIELD.OF(nameof(WorldModelIndex))),
		SendPropInt(FIELD.OF(nameof(State)), 4, PropFlags.Unsigned),
		SendPropEHandle(FIELD.OF(nameof(Owner))),
#endif
	]);

#if CLIENT_DLL
	private static void RecvProxy_WeaponState(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		BaseCombatWeapon weapon = (BaseCombatWeapon)instance;
		weapon.State = data.Value.Int;
		weapon.UpdateVisibility();
	}
#else

	private static object? SendProxy_SendLocalWeaponDataTable(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID) {
		BaseCombatWeapon weapon = (BaseCombatWeapon)instance;
		if (weapon != null) {
			BasePlayer? player = ToBasePlayer(weapon.GetOwner());
			if (player != null) {
				// recipients.SetOnly(player.GetClientIndex());
				return data;
			}
		}

		return null;
	}
	private static object? SendProxy_SendActiveLocalWeaponDataTable(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID) {
		BaseCombatWeapon weapon = (BaseCombatWeapon)instance;
		if (weapon != null) {
			BasePlayer? player = ToBasePlayer(weapon.GetOwner());
			if (player != null) {
				// recipients.SetOnly(player.GetClientIndex());
				return data;
			}
		}

		return null;
	}
#endif
	public static readonly new Class
#if CLIENT_DLL
		ClientClass
#else
		ServerClass
#endif
		= new Class(DT_BaseCombatWeapon);


	public virtual bool CanBePickedUpByNPCs() => true;

	TimeUnit_t UnlockTime;
	readonly EHANDLE Locker = new();

	public void Lock(TimeUnit_t lockTime, BaseEntity? locker) {
		UnlockTime = gpGlobals.CurTime + lockTime;
		Locker.Set(locker);
	}

	public bool IsLocked(BaseEntity? asker) => UnlockTime > gpGlobals.CurTime && Locker.Get() != asker;
	public struct ActTable
	{
		public Activity BaseAct;
		public Activity WeaponAct;
		public bool Required;
	}
	public virtual ReadOnlySpan<ActTable> ActivityList() => null;
	public virtual Activity ActivityOverride(Activity baseAct, ref bool required) {
		ReadOnlySpan<ActTable> table = ActivityList();
		int actCount = table.Length;

		for (int i = 0; i < actCount; i++) {
			ref readonly ActTable act = ref table[i];
			if (baseAct == act.BaseAct) {
				if (!Unsafe.IsNullRef(ref required))
					required = act.Required;

				return act.WeaponAct;
			}
		}
		return baseAct;
	}

#if GMOD_DLL
	public virtual ReadOnlySpan<char> GetHoldType() => "normal";
#endif

	public override void Activate() {
		base.Activate();

#if !CLIENT_DLL
		// todo
#endif
	}

	public struct PoseParamTable
	{
		public string Name;
		public float Value;
	}

	[NetworkName("m_iClip1")]
	public int iClip1;
	[NetworkName("m_iClip2")]
	public int iClip2;
	[NetworkName("m_iPrimaryAmmoType")]
	public int PrimaryAmmoType;
	[NetworkName("m_iSecondaryAmmoType")]
	public int SecondaryAmmoType;
	/// <summary>
	/// View model index (entity offset)
	/// </summary>
	[NetworkName("m_nViewModelIndex")]
	public int nViewModelIndex;
	/// <summary>
	/// View model index (art)
	/// </summary>
	[NetworkName("m_iViewModelIndex")]
	public int iViewModelIndex;
	[NetworkName("m_iWorldModelIndex")]
	public int WorldModelIndex;
	[NetworkName("m_bFlipViewModel")]
	public bool FlipViewModel;

	[NetworkName("m_flNextPrimaryAttack")]
	public TimeUnit_t NextPrimaryAttack;
	[NetworkName("m_flNextSecondaryAttack")]
	public TimeUnit_t NextSecondaryAttack;
	[NetworkName("m_flTimeWeaponIdle")]
	public TimeUnit_t TimeWeaponIdle;

	[NetworkName("m_iState")]
	public int State;
	[NetworkName("m_hOwner")]
	public EHANDLE Owner = new();

	Activity Activity;
	int IdealSequence;
	Activity IdealActivity;
	bool Removable;
	int PrimaryAmmoCount;
	int SecondaryAmmoCount;

	int AltFireHudHintCount;
	int ReloadHudHintCount;
	bool AltFireHudHintDisplayed;
	bool ReloadHudHintDisplayed;
	TimeUnit_t HudHintPollTime;
	TimeUnit_t HudHintMinDisplayTime;

	public virtual BaseCombatWeapon? GetLastWeapon() => this;
	public virtual bool IsOverridingViewmodel() => false;
	public virtual int DrawOverriddenViewmodel(BaseViewModel viewmodel, StudioFlags flags) => 0;
	public virtual void ViewModelDrawn(BaseViewModel viewmodelflags) { }
	public virtual int UpdateClientData(BasePlayer player) {
		WeaponState iNewState = WeaponState.IsCarriedByPlayer;

		if (player.GetActiveWeapon() == this) {
			if (player.OnTarget)
				iNewState = WeaponState.IsOnTarget;
			else
				iNewState = WeaponState.IsActive;
		}
		else
			iNewState = WeaponState.IsCarriedByPlayer;

		if (State != (int)iNewState) {
			int iOldState = State;
			State = (int)iNewState;
			OnActiveStateChanged((WeaponState)iOldState);
		}

		return 1;
	}
	public bool IsViewModelSequenceFinished() {
		if (GetActivity() == Activity.ACT_RESET || GetActivity() == Activity.ACT_INVALID)
			return true;

		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner == null) {
			Assert(false);
			return false;
		}

		BaseViewModel? vm = owner.GetViewModel(nViewModelIndex);
		if (vm == null) {
			Assert(false);
			return false;
		}

		return vm.IsSequenceFinished();
	}
	public void MaintainIdealActivity() {
		if (GetActivity() != Activity.ACT_TRANSITION)
			return;

		// Must not be at our ideal already 
		if ((GetActivity() == IdealActivity) && (GetSequence() == IdealSequence))
			return;

		// Must be finished with the current animation
		if (IsViewModelSequenceFinished() == false)
			return;

		// Move to the next animation towards our ideal
		SendWeaponAnim(IdealActivity);
	}
	public virtual void OnActiveStateChanged(WeaponState state) { }
	public virtual void ItemPreFrame() {
		MaintainIdealActivity();
	}
	public virtual void ItemBusyFrame() {
		UpdateAutoFire();
	}
	public virtual void ItemHolsterFrame() {

	}
	public virtual bool IsWeaponVisible() {
		BaseViewModel? vm = null;
		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner != null) {
			vm = owner.GetViewModel(nViewModelIndex);
			if (vm != null)
				return (!vm.IsEffectActive(EntityEffects.NoDraw));
		}

		return false;
	}

	public virtual bool IsHolstered() {
		return false;
	}

	public int GetPrimaryAmmoCount() => PrimaryAmmoCount;
	public void SetPrimaryAmmoCount(int count) => PrimaryAmmoCount = count;

	public int GetSecondaryAmmoCount() => SecondaryAmmoCount;
	public void SetSecondaryAmmoCount(int count) => SecondaryAmmoCount = count;

	public virtual bool ForceWeaponSwitch() => false;

	public virtual bool HasAnyAmmo() {
		if (!UsesPrimaryAmmo() && !UsesSecondaryAmmo())
			return true;

		return HasPrimaryAmmo() || HasSecondaryAmmo();
	}
	public virtual void AddViewKick() {

	}
	public virtual float GetFireRate() => 0;

	public virtual void PrimaryAttack() {
		// If my clip is empty (and I use clips) start reload
		if (UsesClipsForAmmo1() && iClip1 == 0) {
			Reload();
			return;
		}

		// Only the player fires this way so we can cast
		BasePlayer? player = ToBasePlayer(GetOwner());

		if (player == null)
			return;

		player.DoMuzzleFlash();

		SendWeaponAnim(GetPrimaryAttackActivity());

		// player "shoot" animation
		player.SetAnimation(PlayerAnim.Attack1);

		FireBulletsInfo info = default;
		info.Src = player.Weapon_ShootPosition();

		info.DirShooting = player.GetAutoaimVector(AUTOAIM_SCALE_DEFAULT);

		// To make the firing framerate independent, we may have to fire more than one bullet here on low-framerate systems, 
		// especially if the weapon we're firing has a really fast rate of fire.
		info.Shots = 0;
		float fireRate = GetFireRate();

		while (NextPrimaryAttack <= gpGlobals.CurTime) {
			// MUST call sound before removing a round from the clip of a CMachineGun
			WeaponSound(Shared.WeaponSound.Single, NextPrimaryAttack);
			NextPrimaryAttack = NextPrimaryAttack + fireRate;
			info.Shots++;
			if (fireRate == 0)
				break;
		}

		// Make sure we don't fire more than the amount in the clip
		if (UsesClipsForAmmo1()) {
			info.Shots = Math.Min(info.Shots, iClip1);
			iClip1 -= info.Shots;
		}
		else {
			info.Shots = Math.Min(info.Shots, player.GetAmmoCount(PrimaryAmmoType));
			player.RemoveAmmo(info.Shots, PrimaryAmmoType);
		}

		info.Distance = WorldSize.MAX_TRACE_LENGTH;
		info.AmmoType = PrimaryAmmoType;
		info.TracerFreq = 2;

#if !CLIENT_DLL
		// Fire the bullets
		info.Spread = player.GetAttackSpread(this);
#else
		//!!!HACKHACK - what does the client want this function for? 
		info.Spread = GetActiveWeapon()!.GetBulletSpread();
#endif // CLIENT_DLL

		player.FireBullets(in info);

		if (iClip1 == 0 && player.GetAmmoCount(PrimaryAmmoType) <= 0) {
			// HEV suit - indicate out of ammo condition
			player.SetSuitUpdate("!HEV_AMO0", false, 0);
		}

		//Add our view kick in
		AddViewKick();
	}
	public virtual bool CanReload() {
		if (AutoFiresFullClip() && FiringWholeClip)
			return false;

		return true;
	}
	public virtual bool AutoFiresFullClip() => false;
	public virtual void UpdateAutoFire() {
		if (!AutoFiresFullClip())
			return;

		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner == null)
			return;

		if (iClip1 == 0)
			// Ready to reload again
			FiringWholeClip = false;

		if (FiringWholeClip)
			// If it's firing the clip don't let them repress attack to reload
			owner.Buttons &= ~InButtons.Attack;


		// Don't use the regular reload key
		if ((owner.Buttons & InButtons.Reload) != 0)
			owner.Buttons &= ~InButtons.Reload;

		// Try to fire if there's ammo in the clip and we're not holding the button
		bool releaseClip = iClip1 > 0 && 0 == (owner.Buttons & InButtons.Attack);

		if (!releaseClip) {
			if (CanReload() && (owner.Buttons & InButtons.Attack) != 0) {
				// Convert the attack key into the reload key
				owner.Buttons |= InButtons.Reload;
			}

			// Don't allow attack button if we're not attacking
			owner.Buttons &= ~InButtons.Attack;
		}
		else {
			// Fake the attack key
			owner.Buttons |= InButtons.Attack;
		}
	}

	public bool FiresUnderwater;
	public bool AltFiresUnderwater;
	public float MinRange1;
	public float MinRange2;
	public float MaxRange1;
	public float MaxRange2;
	public bool ReloadsSingly;
	public TimeUnit_t FireDuration;

	public virtual void FinishReload() {
		BaseCombatCharacter? owner = GetOwner();

		if (owner != null) {
			// If I use primary clips, reload primary
			if (UsesClipsForAmmo1()) {
				int primary = Math.Min(GetMaxClip1() - iClip1, owner.GetAmmoCount(PrimaryAmmoType));
				iClip1 += primary;
				owner.RemoveAmmo(primary, PrimaryAmmoType);
			}

			// If I use secondary clips, reload secondary
			if (UsesClipsForAmmo2()) {
				int secondary = Math.Min(GetMaxClip2() - iClip2, owner.GetAmmoCount(SecondaryAmmoType));
				iClip2 += secondary;
				owner.RemoveAmmo(secondary, SecondaryAmmoType);
			}

			if (ReloadsSingly)
				InReload = false;
		}
	}
	public virtual void CheckReload() {
		if (ReloadsSingly) {
			BasePlayer? owner = ToBasePlayer(GetOwner());
			if (owner == null)
				return;

			if (InReload && (NextPrimaryAttack <= gpGlobals.CurTime)) {
				if ((owner.Buttons & (InButtons.Attack | InButtons.Attack2)) != 0 && iClip1 > 0) {
					InReload = false;
					return;
				}

				// If out of ammo end reload
				if (owner.GetAmmoCount(PrimaryAmmoType) <= 0) {
					FinishReload();
					return;
				}
				// If clip not full reload again
				else if (iClip1 < GetMaxClip1()) {
					// Add them to the clip
					iClip1 += 1;
					owner.RemoveAmmo(1, PrimaryAmmoType);

					Reload();
					return;
				}
				// Clip full, stop reloading
				else {
					FinishReload();
					NextPrimaryAttack = gpGlobals.CurTime;
					NextSecondaryAttack = gpGlobals.CurTime;
					return;
				}
			}
		}
		else {
			if ((InReload) && (NextPrimaryAttack <= gpGlobals.CurTime)) {
				FinishReload();
				NextPrimaryAttack = gpGlobals.CurTime;
				NextSecondaryAttack = gpGlobals.CurTime;
				InReload = false;
			}
		}
	}
	public virtual bool ReloadOrSwitchWeapons() {
		BasePlayer? owner = ToBasePlayer(GetOwner());
		Assert(owner != null);

		FireOnEmpty = false;

		// If we don't have any ammo, switch to the next best weapon
		if (!HasAnyAmmo() && NextPrimaryAttack < gpGlobals.CurTime && NextSecondaryAttack < gpGlobals.CurTime) {
			// weapon isn't useable, switch.
			if (((GetWeaponFlags() & WeaponFlags.NoAutoSwitchEmpty) == 0) && (g_pGameRules.SwitchToNextBestWeapon(owner, this))) {
				NextPrimaryAttack = gpGlobals.CurTime + 0.3;
				return true;
			}
		}
		else {
			// Weapon is useable. Reload if empty and weapon has waited as long as it has to after firing
			if (UsesClipsForAmmo1() && !AutoFiresFullClip() && (iClip1 == 0) &&
				 (GetWeaponFlags() & WeaponFlags.NoAutoReload) == 0 &&
				 NextPrimaryAttack < gpGlobals.CurTime &&
				 NextSecondaryAttack < gpGlobals.CurTime) {
				// if we're successfully reloading, we're done
				if (Reload())
					return true;
			}
		}

		return false;
	}
	public virtual void HandleFireOnEmpty() {
		if (FireOnEmpty) {
			ReloadOrSwitchWeapons();
			FireDuration = 0.0;
		}
		else {
			if (NextEmptySoundTime < gpGlobals.CurTime) {
				WeaponSound(Shared.WeaponSound.Empty);
				NextEmptySoundTime = gpGlobals.CurTime + 0.5;
			}
			FireOnEmpty = true;
		}
	}
	public virtual bool CanPerformSecondaryAttack() => NextSecondaryAttack <= gpGlobals.CurTime;
	public virtual bool ShouldBlockPrimaryFire() => false;
	public virtual void ItemPostFrame() {
		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner == null)
			return;

		UpdateAutoFire();

		//Track the duration of the fire
		//FIXME: Check for IN_ATTACK2 as well?
		//FIXME: What if we're calling ItemBusyFrame?
		FireDuration = (owner.Buttons & InButtons.Attack) != 0 ? (FireDuration + gpGlobals.FrameTime) : 0.0f;

		if (UsesClipsForAmmo1()) {
			CheckReload();
		}

		bool bFired = false;

		// Secondary attack has priority
		if ((owner.Buttons & InButtons.Attack2) != 0 && CanPerformSecondaryAttack()) {
			if (UsesSecondaryAmmo() && owner.GetAmmoCount(SecondaryAmmoType) <= 0) {
				if (NextEmptySoundTime < gpGlobals.CurTime) {
					WeaponSound(Shared.WeaponSound.Empty);
					NextSecondaryAttack = NextEmptySoundTime = gpGlobals.CurTime + 0.5;
				}
			}
			else if (owner.GetWaterLevel() == Shared.WaterLevel.Eyes && AltFiresUnderwater == false) {
				// This weapon doesn't fire underwater
				WeaponSound(Shared.WeaponSound.Empty);
				NextPrimaryAttack = gpGlobals.CurTime + 0.2;
				return;
			}
			else {
				// FIXME: This isn't necessarily true if the weapon doesn't have a secondary fire!
				// For instance, the crossbow doesn't have a 'real' secondary fire, but it still 
				// stops the crossbow from firing on the 360 if the player chooses to hold down their
				// zoom button. (sjb) Orange Box 7/25/2007
#if !CLIENT_DLL
				if (!ClassMatches("weapon_crossbow"))
#endif
				{
					bFired = ShouldBlockPrimaryFire();
				}

				SecondaryAttack();

				// Secondary ammo doesn't have a reload animation
				if (UsesClipsForAmmo2()) {
					// reload clip2 if empty
					if (iClip2 < 1) {
						owner.RemoveAmmo(1, SecondaryAmmoType);
						iClip2 = iClip2 + 1;
					}
				}
			}
		}

		if (!bFired && (owner.Buttons & InButtons.Attack) != 0 && (NextPrimaryAttack <= gpGlobals.CurTime)) {
			// Clip empty? Or out of ammo on a no-clip weapon?
			if (!IsMeleeWeapon() &&
				((UsesClipsForAmmo1() && iClip1 <= 0) || (!UsesClipsForAmmo1() && owner.GetAmmoCount(PrimaryAmmoType) <= 0))) {
				HandleFireOnEmpty();
			}
			else if (owner.GetWaterLevel() == Shared.WaterLevel.Eyes && FiresUnderwater == false) {
				// This weapon doesn't fire underwater
				WeaponSound(Shared.WeaponSound.Empty);
				NextPrimaryAttack = gpGlobals.CurTime + 0.2;
				return;
			}
			else {
				//NOTENOTE: There is a bug with this code with regards to the way machine guns catch the leading edge trigger
				//			on the player hitting the attack key.  It relies on the gun catching that case in the same frame.
				//			However, because the player can also be doing a secondary attack, the edge trigger may be missed.
				//			We really need to hold onto the edge trigger and only clear the condition when the gun has fired its
				//			first shot.  Right now that's too much of an architecture change -- jdw

				// If the firing button was just pressed, or the alt-fire just released, reset the firing time
				if ((owner.AfButtonPressed & InButtons.Attack) != 0 || (owner.AfButtonReleased & InButtons.Attack2) != 0)
					NextPrimaryAttack = gpGlobals.CurTime;

				PrimaryAttack();

				if (AutoFiresFullClip()) {
					FiringWholeClip = true;
				}

#if CLIENT_DLL
				owner.SetFiredWeapon(true);
#endif
			}
		}

		// -----------------------
		//  Reload pressed / Clip Empty
		//  Can only start the Reload Cycle after the firing cycle
		if ((owner.Buttons & InButtons.Reload) != 0 && NextPrimaryAttack <= gpGlobals.CurTime && UsesClipsForAmmo1() && !InReload) {
			// reload when reload is pressed, or if no buttons are down and weapon is empty.
			Reload();
			FireDuration = 0.0;
		}

		// -----------------------
		//  No buttons down
		// -----------------------
		if (!((owner.Buttons & InButtons.Attack) != 0 || (owner.Buttons & InButtons.Attack2) != 0 || (CanReload() && (owner.Buttons & InButtons.Reload) != 0))) {
			// no fire buttons down or reloading
			if (!ReloadOrSwitchWeapons() && (InReload == false))
				WeaponIdle();
		}
	}

	public virtual void WeaponIdle() {
		//Idle again if we've finished
		if (HasWeaponIdleTimeElapsed())
			SendWeaponAnim(Activity.ACT_VM_IDLE);
	}

	public virtual Activity GetPrimaryAttackActivity() => Activity.ACT_VM_PRIMARYATTACK;
	public virtual Activity GetSecondaryAttackActivity() => Activity.ACT_VM_SECONDARYATTACK;

	static readonly Vector3 cone = VECTOR_CONE_15DEGREES;

	public virtual int GetBulletType() => 0;
	public virtual ref readonly Vector3 GetBulletSpread() => ref cone;
	public virtual Vector3 GetBulletSpread(WeaponProficiency proficiency) => GetBulletSpread();
	public virtual float GetSpreadBias(WeaponProficiency proficiency) => 1.0f;
	public virtual int GetMinBurst() => 1;
	public virtual int GetMaxBurst() => 1;
	public virtual TimeUnit_t GetMinRestTime() => 0.3;
	public virtual TimeUnit_t GetMaxRestTime() => 0.6;
	public virtual int GetRandomBurst() => random.RandomInt(GetMinBurst(), GetMaxBurst());

	public virtual void SecondaryAttack() { }
	public bool DefaultReload(int clipSize1, int clipSize2, Activity activity) {
		BaseCombatCharacter? owner = GetOwner();
		if (owner == null)
			return false;

		// If I don't have any spare ammo, I can't reload
		if (owner.GetAmmoCount(PrimaryAmmoType) <= 0)
			return false;

		bool reload = false;

		// If you don't have clips, then don't try to reload them.
		if (UsesClipsForAmmo1()) {
			// need to reload primary clip?
			int primary = Math.Min(clipSize1 - iClip1, owner.GetAmmoCount(PrimaryAmmoType));
			if (primary != 0)
				reload = true;
		}

		if (UsesClipsForAmmo2()) {
			// need to reload secondary clip?
			int secondary = Math.Min(clipSize2 - iClip2, owner.GetAmmoCount(SecondaryAmmoType));
			if (secondary != 0)
				reload = true;
		}

		if (!reload)
			return false;

#if CLIENT_DLL
		// Play reload
		WeaponSound(Shared.WeaponSound.Reload);
#endif
		SendWeaponAnim(activity);

		// Play the player's reload animation
		if (owner.IsPlayer())
			((BasePlayer)owner).SetAnimation(PlayerAnim.Reload);

		TimeUnit_t sequenceEndTime = gpGlobals.CurTime + SequenceDuration();
		owner.SetNextAttack(sequenceEndTime);
		NextPrimaryAttack = NextSecondaryAttack = sequenceEndTime;

		InReload = true;

		return true;
	}
	public virtual bool Reload() {
		return DefaultReload(GetMaxClip1(), GetMaxClip2(), Activity.ACT_VM_RELOAD);
	}
	public bool HasPrimaryAmmo() {
		// If I use a clip, and have some ammo in it, then I have ammo
		if (UsesClipsForAmmo1()) {
			if (iClip1 > 0)
				return true;
		}

		// Otherwise, I have ammo if I have some in my ammo counts
		BaseCombatCharacter? owner = GetOwner();
		if (owner != null) {
			if (owner.GetAmmoCount(PrimaryAmmoType) > 0)
				return true;
		}
		else {
			// No owner, so return how much primary ammo I have along with me.
			if (GetPrimaryAmmoCount() > 0)
				return true;
		}

		return false;
	}
	public bool HasSecondaryAmmo() {
		// If I use a clip, and have some ammo in it, then I have ammo
		if (UsesClipsForAmmo2()) {
			if (iClip2 > 0)
				return true;
		}

		// Otherwise, I have ammo if I have some in my ammo counts
		BaseCombatCharacter? owner = GetOwner();
		if (owner != null) {
			if (owner.GetAmmoCount(SecondaryAmmoType) > 0)
				return true;
		}

		return false;
	}

	public virtual bool DefaultDeploy(ReadOnlySpan<char> viewModel, ReadOnlySpan<char> weaponModel, Activity activity, ReadOnlySpan<char> animExt) {
		if (!HasAnyAmmo() && AllowsAutoSwitchFrom())
			return false;

		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner != null) {
			// Dead men deploy no weapons
			if (owner.IsAlive() == false)
				return false;

			owner.SetAnimationExtension(animExt);

			SetViewModel();
			SendWeaponAnim(activity);

			owner.SetNextAttack(gpGlobals.CurTime + SequenceDuration());
		}

		// Can't shoot again until we've finished deploying
		NextPrimaryAttack = gpGlobals.CurTime + SequenceDuration();
		NextSecondaryAttack = gpGlobals.CurTime + SequenceDuration();
		HudHintMinDisplayTime = 0;

		AltFireHudHintDisplayed = false;
		ReloadHudHintDisplayed = false;
		HudHintPollTime = gpGlobals.CurTime + 5.0;

		WeaponSound(Shared.WeaponSound.Deploy);

		SetWeaponVisible(true);

		return true;
	}

	public void SetWeaponVisible(bool visible) {
		BaseViewModel? vm = null;

		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner != null)
			vm = owner.GetViewModel(nViewModelIndex);

		if (visible) {
			RemoveEffects(EntityEffects.NoDraw);
			vm?.RemoveEffects(EntityEffects.NoDraw);
		}
		else {
			AddEffects(EntityEffects.NoDraw);
			vm?.AddEffects(EntityEffects.NoDraw);
		}
	}

	public virtual int Clip1() => iClip1;
	public virtual int Clip2() => iClip2;
	public virtual int GetPrimaryAmmoType() => PrimaryAmmoType;
	public virtual int GetSecondaryAmmoType() => SecondaryAmmoType;

	public virtual ReadOnlySpan<PoseParamTable> PoseParamList() => null;

	public void PoseParameterOverride(bool reset) {
		BaseCombatCharacter? owner = GetOwner();
		if (owner == null)
			return;

		StudioHdr? studioHdr = owner.GetModelPtr();
		if (studioHdr == null)
			return;

		ReadOnlySpan<PoseParamTable> poseParamList = PoseParamList();
		if (!poseParamList.IsEmpty) {
			for (int i = 0; i < poseParamList.Length; ++i) {
				int poseParam = owner.LookupPoseParameter(studioHdr, poseParamList[i].Name);

				if (poseParam != -1)
					owner.SetPoseParameter(poseParam, reset ? 0 : poseParamList[i].Value);
			}
		}
	}

	public void SetViewModel() {
		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner == null)
			return;
		BaseViewModel? vm = owner.GetViewModel(nViewModelIndex, false);
		if (vm == null)
			return;
		Assert(vm.ViewModelIndex() == nViewModelIndex);
		vm.SetWeaponModel(GetViewModel(nViewModelIndex), this);
	}

	public virtual Activity GetDrawActivity() => Activity.ACT_VM_DRAW;
	public Activity GetActivity() => this.Activity;

	public virtual bool CanDeploy() => true;
	public virtual bool CanHolster() => true;

	public virtual bool Deploy() {
		bool bResult = DefaultDeploy(GetViewModel(), GetWorldModel(), GetDrawActivity(), GetAnimPrefix());

		// override pose parameters
		PoseParameterOverride(false);

		return bResult;
	}

	public bool InReload;
	public bool FireOnEmpty;
	public bool FiringWholeClip;
	public const string HIDEWEAPON_THINK_CONTEXT = "BaseCombatWeapon_HideThink";

	public virtual bool Holster(BaseCombatWeapon? switchingTo = null) {
		InReload = false;
		FiringWholeClip = false;

		SetThink(null);

		// Send holster animation
		SendWeaponAnim(Activity.ACT_VM_HOLSTER);

		// Some weapon's don't have holster anims yet, so detect that
		TimeUnit_t sequenceDuration = 0;
		if (GetActivity() == Activity.ACT_VM_HOLSTER)
			sequenceDuration = SequenceDuration();

		BaseCombatCharacter? owner = GetOwner();
		if (owner != null)
			owner.SetNextAttack(gpGlobals.CurTime + sequenceDuration);

		// If we don't have a holster anim, hide immediately to avoid timing issues
		if (sequenceDuration == 0)
			SetWeaponVisible(false);
		else
			SetContextThink(HideThink, gpGlobals.CurTime + sequenceDuration, HIDEWEAPON_THINK_CONTEXT);

		// if we were displaying a hud hint, squelch it.
		if (HudHintMinDisplayTime != 0 && gpGlobals.CurTime < HudHintMinDisplayTime) {
			// if (AltFireHudHintDisplayed)				RescindAltFireHudHint();
			// if (ReloadHudHintDisplayed)				RescindReloadHudHint();
		}

		// reset pose parameters
		PoseParameterOverride(true);

		return true;
	}

	public void HideThink() {
		if (GetOwner()?.GetActiveWeapon() == this)
			SetWeaponVisible(false);
	}

	public BaseCombatCharacter? GetOwner() {
		var ret = ToBaseCombatCharacter(Owner.Get());
		return ret;
	}

	public bool SetIdealActivity(Activity ideal) {
		int idealSequence = SelectWeightedSequence(ideal);

		if (idealSequence == -1)
			return false;

		//Take the new activity
		IdealActivity = ideal;
		IdealSequence = idealSequence;

		//Find the next sequence in the potential chain of sequences leading to our ideal one
		int nextSequence = FindTransitionSequence(GetSequence(), IdealSequence);
		// Don't use transitions when we're deploying
		if (ideal != Activity.ACT_VM_DRAW && IsWeaponVisible() && nextSequence != IdealSequence) {
			//Set our activity to the next transitional animation
			SetActivity(Activity.ACT_TRANSITION);
			SetSequence(nextSequence);
			SendViewModelAnim(nextSequence);
		}
		else {
			//Set our activity to the ideal
			SetActivity(IdealActivity);
			SetSequence(IdealSequence);
			SendViewModelAnim(IdealSequence);
		}

		//Set the next time the weapon will idle
		SetWeaponIdleTime(gpGlobals.CurTime + SequenceDuration());
		return true;
	}
#if !CLIENT_DLL
	bool SoundsEnabled;
#endif
	public virtual bool HasWeaponIdleTimeElapsed() => gpGlobals.CurTime > TimeWeaponIdle;
	public virtual TimeUnit_t GetWeaponIdleTime(TimeUnit_t time) => TimeWeaponIdle;
	public virtual void SetWeaponIdleTime(TimeUnit_t time) => TimeWeaponIdle = time;
	public void SendViewModelAnim(int sequence) {
#if CLIENT_DLL
		if (!IsPredicted())
			return;
#endif

		if (sequence < 0)
			return;

		BasePlayer? owner = ToBasePlayer(GetOwner());

		if (owner == null)
			return;

		BaseViewModel? vm = owner.GetViewModel(nViewModelIndex, false);

		if (vm == null)
			return;

		SetViewModel();
		Assert(vm.ViewModelIndex() == nViewModelIndex);
		vm.SendViewModelMatchingSequence(sequence);
	}
	public virtual void SetActivity(Activity act, float duration) {
#if !CLIENT_DLL && (HL2MP || PORTAL)
		if (GetOwner() != null && GetOwner()!.IsPlayer())
			SetModel(GetWorldModel());
#endif

		int sequence = SelectWeightedSequence(act);

		if (sequence == StudioHdr.ACTIVITY_NOT_AVAILABLE)
			sequence = SelectWeightedSequence(Activity.ACT_VM_IDLE);

#if !CLIENT_DLL && (HL2MP || PORTAL)
		if (GetOwner() != null && GetOwner()!.IsPlayer())
			SetModel(GetViewModel());
#endif

		if (sequence != StudioHdr.ACTIVITY_NOT_AVAILABLE) {
			SetSequence(sequence);
			SetActivity(act);
			SetCycle(0);
			ResetSequenceInfo();

			if (duration > 0) {
				TimeUnit_t playbackRate = SequenceDuration(sequence) / duration;
				SetPlaybackRate(Math.Min(playbackRate, 12.0));
			}
			else
				SetPlaybackRate(1.0);
		}
	}
	public void SetActivity(Activity activity) => Activity = activity;
	public bool SendWeaponAnim(Activity act) {
		return SetIdealActivity((Activity)act);
	}
	public ReadOnlySpan<char> GetShootSound(WeaponSound index) => GetWpnData().ShootSounds[(int)index].SliceNullTerminatedString();
	public ReadOnlySpan<char> GetShootSound(int index) => GetWpnData().ShootSounds[index].SliceNullTerminatedString();
	public virtual void WeaponSound(WeaponSound soundType, TimeUnit_t soundTime = 0.0) {
#if !CLIENT_DLL
		if (!SoundsEnabled)
			return;
#endif

		ReadOnlySpan<char> shootsound = GetShootSound(soundType);
		if (shootsound.IsStringEmpty)
			return;

		SoundParameters parms = default;
		if (!GetParametersForSound(shootsound, ref parms, null))
			return;

		if (parms.PlayToOwnerOnly) {
			// Am I only to play to my owner?
			if (GetOwner() != null && GetOwner()!.IsPlayer()) {
				SingleUserRecipientFilter filter = new(ToBasePlayer(GetOwner())!);
				if (IsPredicted() && BaseEntity.GetPredictionPlayer() != null)
					filter.UsePredictionRules();

				EmitSound(filter, GetOwner()!.EntIndex(), shootsound, in Unsafe.NullRef<Vector3>(), soundTime, out _);
			}
		}
		else {
			// Play weapon sound from the owner
			if (GetOwner() != null) {
				PASAttenuationFilter filter = new(GetOwner()!, SNDLVL_TO_ATTN(parms.SoundLevel));
				if (IsPredicted() && BaseEntity.GetPredictionPlayer() != null)
					filter.UsePredictionRules();

				EmitSound(filter, GetOwner()!.EntIndex(), shootsound, in Unsafe.NullRef<Vector3>(), soundTime, out _);

#if !CLIENT_DLL
				// TODO: if (soundType == EMPTY) 
				// TODO: 	CSoundEnt::InsertSound(SOUND_COMBAT, GetOwner()->GetAbsOrigin(), SOUNDENT_VOLUME_EMPTY, 0.2f, GetOwner());
#endif
			}
			// If no owner play from the weapon (this is used for thrown items)
			else {
				PASAttenuationFilter filter = new(this, SNDLVL_TO_ATTN(parms.SoundLevel));
				if (IsPredicted() && BaseEntity.GetPredictionPlayer() != null)
					filter.UsePredictionRules();

				EmitSound(filter, EntIndex(), shootsound, in Unsafe.NullRef<Vector3>(), soundTime, out _);
			}
		}
	}

	public override void Precache() {
		PrimaryAmmoType = SecondaryAmmoType = -1;
		if (WeaponParse.ReadWeaponDataFromFileForSlot(filesystem, GetClassname(), out WeaponFileInfoHandle)) {
			if (GetWpnData().Ammo1[0] != '\0') {
				PrimaryAmmoType = GetAmmoDef().Index(GetWpnData().Ammo1);
				if (PrimaryAmmoType == -1)
					Msg($"ERROR: Weapon ({GetClassname()}) using undefined primary ammo type ({GetWpnData().Ammo1.AsSpan().SliceNullTerminatedString()})\n");
			}
			if (GetWpnData().Ammo2[0] != '\0') {
				SecondaryAmmoType = GetAmmoDef().Index(GetWpnData().Ammo2);
				if (SecondaryAmmoType == -1)
					Msg($"ERROR: Weapon ({GetClassname()}) using undefined secondary ammo type ({GetWpnData().Ammo2.AsSpan().SliceNullTerminatedString()})\n");
			}
#if CLIENT_DLL
			gWR.LoadWeaponSprites(GetWeaponFileInfoHandle());
#endif
			iViewModelIndex = 0;
			WorldModelIndex = 0;
			if (!GetViewModel().IsEmpty)
				iViewModelIndex = PrecacheModel(GetViewModel());
			if (!GetWorldModel().IsEmpty)
				WorldModelIndex = PrecacheModel(GetWorldModel());

			for (int i = 0; i < (int)Shared.WeaponSound.Num; ++i) {
				ReadOnlySpan<char> shootsound = GetShootSound(i);
				if (!shootsound.IsEmpty)
					PrecacheScriptSound(shootsound);
			}
		}
		else
			Warning($"Error reading weapon data file for: {GetClassname()}\n");
	}

	TimeUnit_t NextEmptySoundTime;

	public void SetOwner(BaseCombatCharacter? owner) {
		if (owner == null) {
			// Make sure the weapon updates its state when it's removed from the player
			// We have to force an active state change, because it's being dropped and won't call UpdateClientData()
			WeaponState oldState = (WeaponState)State;
			State = (int)WeaponState.NotCarried;
			OnActiveStateChanged(oldState);

			SetContextThink(null, 0, HIDEWEAPON_THINK_CONTEXT);
		}

		Owner.Set(owner);
#if !CLIENT_DLL
		// DispatchUpdateTransmitState(); todo
#else
		UpdateVisibility();
#endif
	}



	public const int SF_WEAPON_START_CONSTRAINED = 1 << 0;
	public const int SF_WEAPON_NO_PLAYER_PICKUP = 1 << 1;
	public const int SF_WEAPON_NO_PHYSCANNON_PUNT = 1 << 2;

	public virtual void OnPickedUp(BaseCombatCharacter newOwner) {
#if !CLIENT_DLL
		RemoveEffects(EntityEffects.ItemBlink);

		if (newOwner.IsPlayer()) {
			OnPlayerPickup.FireOutput(newOwner, this);

			RecipientFilter filter = new();
			for (int i = 1; i <= gpGlobals.MaxClients; ++i) {
				BasePlayer? player = Util.PlayerByIndex(i);
				if (player != null && !player.IsAlive() && player.GetObserverMode() == Shared.ObserverMode.InEye)
					filter.AddRecipient(player);
			}
			if (filter.GetRecipientCount() != 0)
				EmitSound(filter, newOwner.EntIndex(), "Player.PickupWeapon", in Unsafe.NullRef<Vector3>(), 0, out _);

			SetName((string?)null);
		}
		else
			OnNPCPickup.FireOutput(newOwner, this);

		Removable = false;
#endif
	}

	public void GiveTo(BaseEntity other) => DefaultTouch(other);

	public void DefaultTouch(BaseEntity? other) {
#if !CLIENT_DLL
		if (IsDissolving())
			return;

		BasePlayer? player = ToBasePlayer(other);
		if (player == null)
			return;

		if (Util.ItemCanBeTouchedByPlayer(this, player))
			OnCacheInteraction.FireOutput(other, this);

		if (HasSpawnFlags(SF_WEAPON_NO_PLAYER_PICKUP))
			return;

		if (player.BumpWeapon(this))
			OnPickedUp(player);
#endif
	}

	public void SetPickupTouch() {
#if !CLIENT_DLL
		SetTouch(DefaultTouch);

		if (gpGlobals.MaxClients > 1) {
			if (HasSpawnFlags(BasePlayer.SF_NORESPAWN)) {
				SetThink(SUB_Remove);
				SetNextThink(gpGlobals.CurTime + 30.0f);
			}
		}
#endif
	}

	public virtual void Equip(BaseCombatCharacter owner) {
		SetAbsVelocity(vec3_origin);
		RemoveSolidFlags(SolidFlags.Trigger);
		FollowEntity(owner);
		SetOwner(owner);
		SetOwnerEntity(owner);

		// Break any constraint I might have to the world.
		RemoveEffects(EntityEffects.ItemBlink);

		NextPrimaryAttack = gpGlobals.CurTime;
		NextSecondaryAttack = gpGlobals.CurTime;
#if !CLIENT_DLL
		SetTouch(null);
#endif
		SetThink(null);
#if !CLIENT_DLL
		VPhysicsDestroyObject();
#endif

		if (owner.IsPlayer())
			SetModel(GetViewModel());
		else {
			// Make the weapon ready as soon as any NPC picks it up.
			NextPrimaryAttack = gpGlobals.CurTime;
			NextSecondaryAttack = gpGlobals.CurTime;
			SetModel(GetWorldModel());
		}
	}

	public virtual void Drop(in Vector3 velocity) {

	}

	public void GiveDefaultAmmo() {
		// If I use clips, set my clips to the default
		if (UsesClipsForAmmo1())
			iClip1 = AutoFiresFullClip() ? 0 : GetDefaultClip1();
		else {
			SetPrimaryAmmoCount(GetDefaultClip1());
			iClip1 = WEAPON_NOCLIP;
		}
		if (UsesClipsForAmmo2())
			iClip2 = GetDefaultClip2();
		else {
			SetSecondaryAmmoCount(GetDefaultClip2());
			iClip2 = WEAPON_NOCLIP;
		}
	}

	public override void Spawn() {
		Precache();
		base.Spawn();

		SetSolid(SolidType.BBox);
		NextEmptySoundTime = 0.0;

		// Weapons won't show up in trace calls if they are being carried...
		RemoveEFlags(EFL.UsePartitionWhenNotSolid);

		State = (int)WeaponState.NotCarried;
		// Assume 
		nViewModelIndex = 0;

		GiveDefaultAmmo();

		if (!GetWorldModel().IsEmpty)
			SetModel(GetWorldModel());

#if !CLIENT_DLL // todo
		FallInit();
		SetCollisionGroup(Source.CollisionGroup.Weapon);
		m_takedamage = (byte)Damage.EventsOnly;

		SetBlocksLOS(false);

		// Default to non-removeable, because we don't want the
		// game_weapon_manager entity to remove weapons that have
		// been hand-placed by level designers. We only want to remove
		// weapons that have been dropped by NPC's.
		Removable = false;
#endif

		// Bloat the box for player pickup
		CollisionProp().UseTriggerBounds(true, 36);

		// Use more efficient bbox culling on the client. Otherwise, it'll setup bones for most
		// characters even when they're not in the frustum.
		AddEffects(EntityEffects.BoneMergeFastCull);

		ReloadHudHintCount = 0;
		AltFireHudHintCount = 0;
		HudHintMinDisplayTime = 0;
	}

	public bool VisibleInWeaponSelection() => true;
	public int GetPosition() => GetWpnData().Position;
	public int GetSlot() => GetWpnData().Slot;

	WEAPON_FILE_INFO_HANDLE WeaponFileInfoHandle;
	public WEAPON_FILE_INFO_HANDLE GetWeaponFileInfoHandle() => WeaponFileInfoHandle;

	public FileWeaponInfo GetWpnData() {
		return WeaponParse.GetFileWeaponInfoFromHandle(WeaponFileInfoHandle);
	}

	public ReadOnlySpan<char> GetName() => GetWpnData().ClassName.SliceNullTerminatedString();
	public ReadOnlySpan<char> GetPrintName() => GetWpnData().PrintName.SliceNullTerminatedString();
#if CLIENT_DLL
	public HudTexture GetSpriteActive() => GetWpnData().IconActive;
	public HudTexture GetSpriteInactive() => GetWpnData().IconInactive;
	public HudTexture GetSpriteAmmo() => GetWpnData().IconAmmo;
	public HudTexture GetSpriteAmmo2() => GetWpnData().IconAmmo2;
	public HudTexture GetSpriteCrosshair() => GetWpnData().IconCrosshair;
	public HudTexture GetSpriteAutoaim() => GetWpnData().IconAutoaim;
	public HudTexture GetSpriteZoomedCrosshair() => GetWpnData().IconZoomedCrosshair;
	public HudTexture GetSpriteZoomedAutoaim() => GetWpnData().IconZoomedAutoaim;
#endif

	public WeaponFlags GetWeaponFlags() => (WeaponFlags)GetWpnData().Flags;

	public bool HasAmmo() {
		if (PrimaryAmmoType == -1 && SecondaryAmmoType == -1)
			return true;
		if ((GetWeaponFlags() & WeaponFlags.SelectionEmpty) != 0)
			return true;

		BasePlayer? player = ToBasePlayer(GetOwner());
		if (player == null)
			return false;
		return (iClip1 > 0 || player.GetAmmoCount(PrimaryAmmoType) != 0 || iClip2 > 0 || player.GetAmmoCount(SecondaryAmmoType) != 0);
	}

	public bool CanBeSelected() {
		if (!VisibleInWeaponSelection())
			return false;

		return HasAmmo();
	}

	public int GetMaxClip1() => GetWpnData().MaxClip1;
	public int GetMaxClip2() => GetWpnData().MaxClip2;
	public int GetDefaultClip1() => GetWpnData().DefaultClip1;
	public int GetDefaultClip2() => GetWpnData().DefaultClip2;
	public bool IsMeleeWeapon() => GetWpnData().MeleeWeapon;

	public int SubType;
	public int GetSubType() => SubType;
	public void SetSubType(int subtype) => SubType = subtype;

	public bool UsesClipsForAmmo1() => GetMaxClip1() != WEAPON_NOCLIP;
	public bool UsesClipsForAmmo2() => GetMaxClip2() != WEAPON_NOCLIP;
	public int GetWeight() => GetWpnData().Weight;
	public bool AllowsAutoSwitchTo() => GetWpnData().AutoSwitchTo;
	public bool AllowsAutoSwitchFrom() => GetWpnData().AutoSwitchFrom;
	public bool UsesPrimaryAmmo() => PrimaryAmmoType >= 0;
	public bool UsesSecondaryAmmo() => SecondaryAmmoType >= 0;
	public ReadOnlySpan<char> GetViewModel(int _ = 0) => GetWpnData().ViewModel.SliceNullTerminatedString();
	public ReadOnlySpan<char> GetWorldModel() => GetWpnData().WorldModel.SliceNullTerminatedString();
	public ReadOnlySpan<char> GetAnimPrefix() => GetWpnData().AnimationPrefix.SliceNullTerminatedString();
}
#endif
