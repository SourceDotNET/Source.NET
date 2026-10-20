#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Game.Server;

using Source.Common;
namespace Game.Shared.GarrysMod;
using FIELD = Source.FIELD<WeaponCrossbow>;

#if !CLIENT_DLL

[LinkEntityToClass("crossbow_bolt")]
[NetworkName("CCrossbowBolt")]
public class CrossbowBolt : BaseCombatCharacter {
	public static readonly SendTable DT_CrossbowBolt = new(DT_BaseCombatCharacter, []);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_CrossbowBolt);
}
#endif

[LinkEntityToClass("weapon_crossbow")]
[PrecacheWeaponRegister("weapon_crossbow")]
[NetworkName("CWeaponCrossbow")]
public class WeaponCrossbow : BaseHL2MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponCrossbow = new(DT_BaseHL2MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropBool(FIELD.OF(nameof(InZoom))),
			RecvPropBool(FIELD.OF(nameof(MustReload)))
#else
			SendPropBool(FIELD.OF(nameof(InZoom))),
			SendPropBool(FIELD.OF(nameof(MustReload)))
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponCrossbow);
	public static readonly new DataMap PredMap = new([], typeof(WeaponCrossbow), BaseHL2MPCombatWeapon.PredMap); public override DataMap? GetPredDescMap() => PredMap;
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponCrossbow);
#endif
	[NetworkName("m_bInZoom")]
	public bool InZoom;
	[NetworkName("m_bMustReload")]
	public bool MustReload;
}
#endif
