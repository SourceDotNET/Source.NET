#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_shotgun_hl1")]
[NetworkName("CWeaponShotgun_HL1")]
public partial class WeaponShotgun_HL1 : BaseHL1MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(PumpTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(InSpecialReload)) { Encoding = NetEncoding.Int, Bits = 2, Flags = PropFlags.Unsigned },
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(PumpTime),
		nameof(InSpecialReload),
	];

	[NetworkVar("m_flPumpTime")]
	public partial float PumpTime { get; set; }
	[NetworkVar("m_fInSpecialReload")]
	public partial float InSpecialReload { get; set; }
}
#endif
