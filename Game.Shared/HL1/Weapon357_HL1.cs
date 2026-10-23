#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_357_hl1")]
[NetworkName("CWeapon357_HL1")]
public partial class Weapon357_HL1 : BaseHL1MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(InZoom)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(InZoom),
	];

	[NetworkVar("m_fInZoom")]
	public partial float InZoom { get; set; }
}
#endif
