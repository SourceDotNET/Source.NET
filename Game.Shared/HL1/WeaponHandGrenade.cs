#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_handgrenade")]
[NetworkName("CWeaponHandGrenade")]
public partial class WeaponHandGrenade : BaseHL1MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartThrow)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ReleaseThrow)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(StartThrow),
		nameof(ReleaseThrow),
	];

	[NetworkVar("m_flStartThrow")]
	public partial TimeUnit_t StartThrow { get; set; }
	[NetworkVar("m_flReleaseThrow")]
	public partial TimeUnit_t ReleaseThrow { get; set; }
}
#endif
