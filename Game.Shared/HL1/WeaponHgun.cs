#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_hornetgun")]
[NetworkName("CWeaponHgun")]
public partial class WeaponHgun : BaseHL1MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(RechargeTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FirePhase)) { Bits = 4, Flags = PropFlags.Unsigned },
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(RechargeTime),
		nameof(FirePhase),
	];

	[NetworkVar("m_flRechargeTime")]
	public partial TimeUnit_t RechargeTime { get; set; }
	[NetworkVar("m_iFirePhase")]
	public partial int FirePhase { get; set; }
}
#endif
