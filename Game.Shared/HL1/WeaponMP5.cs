#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_mp5_hl1")]
[NetworkName("CWeaponMP5")]
public partial class WeaponMP5 : BaseHL1MPCombatWeapon
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
