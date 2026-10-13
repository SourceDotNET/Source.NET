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
	public InlineArray64<char> HoldType;

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
	InlineArray64<char> LastHoldType;
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
