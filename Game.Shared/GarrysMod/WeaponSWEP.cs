using System.Numerics;
#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Game.Shared;

using Source;
using Source.Common;

#if CLIENT_DLL
namespace Game.Client;
using Game.Client.GarrysMod;
using FIELD = Source.FIELD<C_WeaponSWEP>;
#else
namespace Game.Server;

using Game.Server.GarrysMod;

using FIELD = Source.FIELD<WeaponSWEP>;
#endif

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

[LinkEntityToClass("weapon_swep")]
[NetworkName("CWeaponSWEP")]
public partial class
#if CLIENT_DLL
    C_WeaponSWEP : C_BaseHL2MPCombatWeapon
#else
	WeaponSWEP : BaseHL2MPCombatWeapon
#endif
{
	public static readonly Table DT_WeaponSWEP = new(DT_BaseHL2MPCombatWeapon, [
#if CLIENT_DLL
		RecvPropDataTable("m_ScriptedEntity", FIELD.OF(nameof(ScriptedEntity)), ScriptedEntity.DT_ScriptedEntity, 0, RECV_GET_OBJECT_AT_FIELD(FIELD.OF(nameof(ScriptedEntity)))),
		RecvPropString(FIELD.OF(nameof(HoldType)))
#elif GAME_DLL
		SendPropDataTable("m_ScriptedEntity", FIELD.OF(nameof(ScriptedEntity)), ScriptedEntity.DT_ScriptedEntity),
		SendPropString(FIELD.OF(nameof(HoldType)))
#endif
	]);

	public static readonly new Class
#if CLIENT_DLL
		ClientClass
#else
		ServerClass
#endif
		= new Class(DT_WeaponSWEP);

#if CLIENT_DLL
	public static readonly new DataMap PredMap = new([], typeof(C_WeaponSWEP), BaseHL2MPCombatWeapon.PredMap); public override DataMap? GetPredDescMap() => PredMap;
#endif

	[NetworkName("m_strHoldType")]
	public InlineArray80<char> HoldType;

	public readonly ScriptedEntity ScriptedEntity = new();

#if CLIENT_DLL
	public C_WeaponSWEP() {
#else
	public WeaponSWEP() {
#endif
		ScriptedEntity.Init(this, 2);
		ScriptedEntity.SetModuleName("weapons");
		ScriptedEntity.SetSelfName("Weapon");
	}

	string ViewModelName = "";
	string WorldModelName = "";
	string PrintName = "";
	int Slot = -1;
	int SlotPos = -1;
	long LastLuaDataUpdate;
	bool LuaDataLocked;
	InlineArray80<char> LastHoldType;
	BaseEntity? LastOwner;
	int MaxClip1;
	int DefaultClip1;
	bool PrimaryAutomatic;
	int MaxClip2;
	int DefaultClip2;
	bool SecondaryAutomatic;
#if CLIENT_DLL
	bool AccurateCrosshair;
#else
	int Weight;
	bool AutoSwitchTo;
	bool AutoSwitchFrom;
#endif

	public virtual void UpdateLuaData(bool force) {
		if (!ScriptedEntity.Started)
			return;

		bool update = false;
		if (!LuaDataLocked)
			update = force;
		if (!update && LastLuaDataUpdate == gpGlobals.FrameCount)
			return;

		LastLuaDataUpdate = gpGlobals.FrameCount;
		SetClassname(ScriptedEntity.GetScriptName());

		LuaObject? table = GetLuaTable();
		if (table == null)
			return;

		ViewModelName = table.GetMemberStr("ViewModel", "models/weapons/v_pistol.mdl") ?? "";
		WorldModelName = table.GetMemberStr("WorldModel", "models/weapons/w_pistol.mdl") ?? "";
		PrintName = table.GetMemberStr("PrintName", "<MISSING SWEP PRINT NAME>") ?? "";
#if CLIENT_DLL
		AccurateCrosshair = table.GetMemberBool("AccurateCrosshair", false);
#else
		Weight = table.GetMemberInt("Weight", 0);
		AutoSwitchTo = table.GetMemberBool("AutoSwitchTo", true);
		AutoSwitchFrom = table.GetMemberBool("AutoSwitchFrom", true);
#endif

		LuaObject primary = new();
		table.GetMember("Primary", primary);
		string? ammo = null;
		if (primary.isTable()) {
			MaxClip1 = primary.GetMemberInt("ClipSize", 0);
			DefaultClip1 = primary.GetMemberInt("DefaultClip", 0);
			PrimaryAutomatic = primary.GetMemberBool("Automatic", true);
			ammo = primary.GetMemberStr("Ammo", null);
		}
		primary.UnReference();
		if (ammo != null) {
			int ammoType = GetAmmoDef().Index(ammo);
			if (PrimaryAmmoType != ammoType) {
#if GAME_DLL
				NetworkStateChanged();
#endif
				PrimaryAmmoType = ammoType;
			}
		}
		else {
			if (PrimaryAmmoType != -1) {
#if GAME_DLL
				NetworkStateChanged();
#endif
				PrimaryAmmoType = -1;
			}
			PrimaryAutomatic = true;
			MaxClip1 = -1;
			DefaultClip1 = -1;
		}

		LuaObject secondary = new();
		table.GetMember("Secondary", secondary);
		ammo = null;
		if (secondary.isTable()) {
			MaxClip2 = secondary.GetMemberInt("ClipSize", 0);
			DefaultClip2 = secondary.GetMemberInt("DefaultClip", 0);
			SecondaryAutomatic = secondary.GetMemberBool("Automatic", true);
			ammo = secondary.GetMemberStr("Ammo", null);
		}
		secondary.UnReference();
		if (ammo != null) {
			int ammoType = GetAmmoDef().Index(ammo);
			if (SecondaryAmmoType != ammoType) {
#if GAME_DLL
				NetworkStateChanged();
#endif
				SecondaryAmmoType = ammoType;
			}
		}
		else {
			if (SecondaryAmmoType != -1) {
#if GAME_DLL
				NetworkStateChanged();
#endif
				SecondaryAmmoType = -1;
			}
			SecondaryAutomatic = true;
			MaxClip2 = -1;
			DefaultClip2 = -1;
		}

		if (iViewModelIndex != 0) {
#if GAME_DLL
			NetworkStateChanged();
#endif
			iViewModelIndex = 0;
		}
		if (WorldModelIndex != 0) {
#if GAME_DLL
			NetworkStateChanged();
#endif
			WorldModelIndex = 0;
		}
		if (!GetViewModel(0).IsEmpty) {
			int index = PrecacheModel(GetViewModel(0));
			if (iViewModelIndex != index) {
#if GAME_DLL
				NetworkStateChanged();
#endif
				iViewModelIndex = index;
			}
		}
		if (!GetWorldModel().IsEmpty) {
			int index = PrecacheModel(GetWorldModel());
			if (WorldModelIndex != index) {
#if GAME_DLL
				NetworkStateChanged();
#endif
				WorldModelIndex = index;
			}
		}

		if (strcmp(LastHoldType, HoldType) != 0)
			UpdateHoldType();
	}

	public virtual void UpdateHoldType() {
		if (HoldType[0] != '\0' && ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.SetWeaponHoldType)) {
			g_Lua!.PushString(((ReadOnlySpan<char>)HoldType).SliceNullTerminatedString());
			ScriptedEntity.Call(1, 0);
		}
		strcpy(LastHoldType, HoldType);
	}

	public override void Precache() { }

	public override ReadOnlySpan<char> GetHoldType() => ((ReadOnlySpan<char>)HoldType).SliceNullTerminatedString();

	public override void SetHoldType(ReadOnlySpan<char> holdType) {
		if (holdType.IsEmpty || holdType[0] == '\0')
			return;
#if GAME_DLL
		NetworkStateChanged();
#endif
		strcpy(HoldType, holdType);
	}

#if CLIENT_DLL
	public virtual void RunSWEPCmd(ReadOnlySpan<char> function, ReadOnlySpan<char> args) {
		if (ScriptedEntity.StartFunction(function)) {
			g_Lua!.PushString(args);
			ScriptedEntity.Call(1, 0);
		}
	}
#endif

	public bool GetPrimaryAutomatic() {
		UpdateLuaData(true);
		return PrimaryAutomatic;
	}

	public bool GetSecondaryAutomatic() {
		UpdateLuaData(true);
		return SecondaryAutomatic;
	}

#if GAME_DLL
	public override void Spawn() {
		UpdateLuaData(true);
		base.Spawn();
		bool allowPrecache = IsPrecacheAllowed();
		SetAllowPrecache(true);
		ScriptedEntity.CallFunction("Initialize", false);
		SetAllowPrecache(allowPrecache);
	}

	public override void OnRestore() {
		base.OnRestore();
		ScriptedEntity.InitializeDataTables();
		ScriptedEntity.CallFunction("OnRestore", true);
	}
#endif

	public override void UpdateOnRemove() {
		if (EntIndex() != -1 && ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.OnRemove)) {
#if GAME_DLL
			g_Lua!.PushBool(false);
#else
			g_Lua!.PushBool(DestroyedOnRecreateEntities);
#endif
			ScriptedEntity.Call(1, 0);
		}
		base.UpdateOnRemove();
	}

	public override void OnOwnerChanged() {
		if (GetOwnerEntity() == LastOwner)
			return;
		UpdateLuaData(true);
		LastOwner = GetOwnerEntity();
		if (ScriptedEntity.Started)
			ScriptedEntity.CallFunction("OwnerChanged", false);
	}

	public override bool Deploy() {
		UpdateLuaData(true);
		base.Deploy();
		if (ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.Deploy) && ScriptedEntity.Call(0, 1))
			return g_Lua!.GetReturn(0).GetBool();
		return true;
	}

	public override bool Holster(BaseCombatWeapon? switchingTo) {
		if (ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.Holster)) {
			LuaEntity.Push_Entity(switchingTo);
			if (ScriptedEntity.Call(1, 1) && !g_Lua!.GetReturn(0).GetBool())
				return false;
		}
		if (switchingTo != this)
			base.Holster(switchingTo!);
		UpdateLuaData(true);
#if GAME_DLL
		GameInterface.EntityMessageBegin(this, true);
		WRITE_BYTE(1);
		GameInterface.MessageEnd();
#endif
		return true;
	}

	public override void ItemPostFrame() {
		if (GetOwner() == null)
			return;
		UpdateLuaData(true);
		RandomSeed(GetPredictionRandomSeed());
		ScriptedEntity.CallFunction((int)LUA_POOLEDSTRING.Tick, false);
		ScriptedEntity.CallFunction((int)LUA_POOLEDSTRING.Think, false);
		LuaDataLocked = true;
		if (UsesClipsForAmmo1() || UsesClipsForAmmo2())
			CheckReload();
		LuaDataLocked = false;
		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner == null || !owner.CanAttack())
			return;
		if ((owner.Buttons & InButtons.Attack2) != 0 && NextSecondaryAttack <= gpGlobals.CurTime)
			SecondaryAttack();
		if ((owner.Buttons & InButtons.Attack) != 0 && NextPrimaryAttack <= gpGlobals.CurTime)
			PrimaryAttack();
		if ((owner.Buttons & InButtons.Reload) != 0 && !InReload)
			Reload();
	}

	public override bool Reload() {
		ScriptedEntity.CallFunction((int)LUA_POOLEDSTRING.Reload, false);
		return false;
	}

	public override void PrimaryAttack() {
		if (GetOwner() == null)
			return;
		BaseCombatCharacter? owner = GetOwner();
		if (owner == null || !owner.IsPlayer() || GetPrimaryAutomatic() || (((BasePlayer)owner).AfButtonPressed & InButtons.Attack) != 0)
			ScriptedEntity.CallFunction((int)LUA_POOLEDSTRING.PrimaryAttack, false);
	}

	public override void SecondaryAttack() {
		BaseCombatCharacter? owner = GetOwner();
		if (owner != null && owner.IsPlayer() && !GetSecondaryAutomatic() && (((BasePlayer)owner).AfButtonPressed & InButtons.Attack2) == 0)
			return;
		ScriptedEntity.CallFunction((int)LUA_POOLEDSTRING.SecondaryAttack, false);
	}

	public override Activity ActivityOverride(Activity baseAct, ref bool required) {
		if (ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.TranslateActivity)) {
			g_Lua!.PushNumber((int)baseAct);
			if (ScriptedEntity.Call(1, 1)) {
				int act = g_Lua.GetReturn(0).GetInt();
				return act == -1 ? baseAct : (Activity)act;
			}
		}
		return baseAct;
	}

	public override void Equip(BaseCombatCharacter owner) {
		base.Equip(owner);
		UpdateLuaData(true);
		if (ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.Equip)) {
			LuaEntity.Push_Entity(owner);
			ScriptedEntity.Call(1, 0);
		}
	}

	public override void Drop(in Vector3 velocity) {
		BaseCombatCharacter? owner = GetOwner();
		base.Drop(velocity);
		SetOwner(null);
		SetOwnerEntity(null);
		UpdateLuaData(true);
		if (ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.OnDrop)) {
			LuaEntity.Push_Entity(owner);
			ScriptedEntity.Call(1, 0);
		}
	}

#if GAME_DLL
	public override ReadOnlySpan<char> GetName() => ScriptedEntity.ScriptNamePooled;
#else
	public override ReadOnlySpan<char> GetName() => ScriptedEntity.GetScriptName();
#endif

	public override int GetSlot() {
		if (Slot >= 0)
			return Slot;
		if (ScriptedEntity.Started && HasLuaTable()) {
			Slot = GetLuaTable()!.GetMemberInt("Slot", 0);
			return Slot;
		}
		return 0;
	}

	public override int GetPosition() {
		if (SlotPos >= 0)
			return SlotPos;
		if (!ScriptedEntity.Started || !HasLuaTable())
			return 0;
		SlotPos = GetLuaTable()!.GetMemberInt("SlotPos", 1) * 100;
		return SlotPos >= 0 ? SlotPos : 0;
	}

	public override ReadOnlySpan<char> GetPrintName() {
		if (PrintName.Length == 0)
			UpdateLuaData(true);
		return PrintName;
	}

	public override ReadOnlySpan<char> GetViewModel(int _ = 0) => ViewModelName;
	public override ReadOnlySpan<char> GetWorldModel() => WorldModelName;

	public override int GetMaxClip1() {
		UpdateLuaData(true);
		return MaxClip1;
	}

	public override int GetMaxClip2() {
		UpdateLuaData(true);
		return MaxClip2;
	}

	public override int GetDefaultClip1() {
		UpdateLuaData(true);
		return DefaultClip1;
	}

	public override int GetDefaultClip2() {
		UpdateLuaData(true);
		return DefaultClip2;
	}

#if GAME_DLL
	public override int GetWeight() {
		UpdateLuaData(true);
		return Weight;
	}

	public override bool AllowsAutoSwitchTo() {
		UpdateLuaData(true);
		return AutoSwitchTo;
	}

	public override bool AllowsAutoSwitchFrom() {
		UpdateLuaData(true);
		return AutoSwitchFrom;
	}
#endif

	public override ReadOnlySpan<char> GetLuaScriptName() => ScriptedEntity.GetScriptName();

	public override void InitializeScriptedEntity(ReadOnlySpan<char> name) {
		ScriptedEntity.SetScriptName(name);
		SetClassname(name);
		ScriptedEntity.StartLua();
	}

#if CLIENT_DLL
	public override void OnDataChanged(DataUpdateType updateType) {
		base.OnDataChanged(updateType);
		if (updateType == DataUpdateType.Created)
			ScriptedEntity.StartLua();
	}
#endif

}
#endif
