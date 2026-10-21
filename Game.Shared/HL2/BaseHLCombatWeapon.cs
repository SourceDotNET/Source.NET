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
public partial class BaseHLCombatWeapon : BaseCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
#endif
