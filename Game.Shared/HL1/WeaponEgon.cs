#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_egon")]
[NetworkName("CWeaponEgon")]
public partial class WeaponEgon : BaseHL1MPCombatWeapon
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
