#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_glock_hl1")]
[NetworkName("CWeaponGlock")]
public partial class WeaponGlock : BaseHL1MPCombatWeapon
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
