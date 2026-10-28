#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_snark")]
[NetworkName("CWeaponSnark")]
public partial class WeaponSnark : BaseHL1MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(JustThrown),
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(JustThrown),
	];

	[NetworkVar("m_bJustThrown")]
	public partial bool JustThrown { get; set; }
}
#endif
