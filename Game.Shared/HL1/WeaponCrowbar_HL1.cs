#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_crowbar_hl1")]
[NetworkName("CWeaponCrowbar_HL1")]
public partial class WeaponCrowbar_HL1 : BaseHL1MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
	];

	public float InZoom;
}
#endif
