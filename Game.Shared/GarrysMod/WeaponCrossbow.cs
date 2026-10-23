#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Game.Server;

using Source.Common;
namespace Game.Shared.GarrysMod;

#if !CLIENT_DLL

[LinkEntityToClass("crossbow_bolt")]
[NetworkName("CCrossbowBolt")]
public partial class CrossbowBolt : BaseCombatCharacter {
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
#endif

[LinkEntityToClass("weapon_crossbow")]
[PrecacheWeaponRegister("weapon_crossbow")]
[NetworkName("CWeaponCrossbow")]
public partial class WeaponCrossbow : BaseHL2MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(InZoom),
		nameof(MustReload),
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(InZoom),
		nameof(MustReload),
	];

	[NetworkVar("m_bInZoom")]
	public partial bool InZoom { get; set; }
	[NetworkVar("m_bMustReload")]
	public partial bool MustReload { get; set; }
}
#endif
