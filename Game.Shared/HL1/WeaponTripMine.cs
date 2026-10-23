#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_tripmine")]
[NetworkName("CWeaponTripMine")]
public partial class WeaponTripMine : BaseHL1MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(GroundIndex)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(PickedUpIndex)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(GroundIndex),
		nameof(PickedUpIndex),
	];

	[NetworkVar("m_iGroundIndex")]
	public partial float GroundIndex { get; set; }
	[NetworkVar("m_iPickedUpIndex")]
	public partial float PickedUpIndex { get; set; }
}
#endif
