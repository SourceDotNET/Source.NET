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
