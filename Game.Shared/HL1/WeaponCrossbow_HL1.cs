#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_crossbow_hl1")]
[NetworkName("CWeaponCrossbow_HL1")]
public partial class WeaponCrossbow_HL1 : BaseHL1MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(InZoom),
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(InZoom),
	];

	[NetworkVar("m_fInZoom")]
	public partial bool InZoom { get; set; }
}
#endif
