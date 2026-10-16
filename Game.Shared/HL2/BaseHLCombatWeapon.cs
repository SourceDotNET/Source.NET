#if CLIENT_DLL || GAME_DLL

global using static Game.Shared.HL2.HLCombatWeaponGlobals;

#if CLIENT_DLL
global using C_BaseHLCombatWeapon = Game.Shared.HL2.BaseHLCombatWeapon;
#endif

using Source;
using Source.Common;
using Source.Common.Commands;

namespace Game.Shared.HL2;

[EngineComponent]
public static class HLCombatWeaponGlobals
{
	public static readonly ConVar sk_auto_reload_time = new("sk_auto_reload_time", "3", FCvar.Replicated);
}

[LinkEntityToClass("basehlcombatweapon")]
[NetworkName("CBaseHLCombatWeapon")]
public class BaseHLCombatWeapon : BaseCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_BaseHLCombatWeapon = new(DT_BaseCombatWeapon, []);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_BaseHLCombatWeapon);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_BaseHLCombatWeapon);
#endif
}
#endif
