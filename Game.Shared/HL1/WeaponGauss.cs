#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_gauss")]
[NetworkName("CWeaponGauss")]
public partial class WeaponGauss : BaseHL1MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(AttackState)) { Bits = 2, Flags = PropFlags.Unsigned },
		nameof(PrimaryFire),
		new(nameof(StartCharge)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(AmmoStartCharge)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		nameof(PlayAftershock),
		nameof(NextAmmoBurn),
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(AttackState),
		nameof(PrimaryFire),
		nameof(StartCharge),
		nameof(AmmoStartCharge),
		nameof(PlayAftershock),
		nameof(NextAmmoBurn),
	];

	[NetworkVar("m_nAttackState")]
	public partial int AttackState { get; set; }
	[NetworkVar("m_bPrimaryFire")]
	public partial bool PrimaryFire { get; set; }
	[NetworkVar("m_flStartCharge")]
	public partial TimeUnit_t StartCharge { get; set; }
	[NetworkVar("m_flAmmoStartCharge")]
	public partial TimeUnit_t AmmoStartCharge { get; set; }
	[NetworkVar("m_flPlayAftershock")]
	public partial TimeUnit_t PlayAftershock { get; set; }
	[NetworkVar("m_flNextAmmoBurn")]
	public partial TimeUnit_t NextAmmoBurn { get; set; }
}
#endif
