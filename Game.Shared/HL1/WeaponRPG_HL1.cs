#if CLIENT_DLL || GAME_DLL
using Source.Common;

using System.Drawing;
namespace Game.Shared.HL1;
[LinkEntityToClass("weapon_rpg_hl1")]
[NetworkName("CWeaponRPG_HL1")]
public partial class WeaponRPG_HL1 : BaseHL1MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(InitialStateUpdate),
		nameof(Guiding),
		nameof(LaserDotSuspended),
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(InitialStateUpdate),
		nameof(Guiding),
		nameof(LaserDotSuspended),
	];

	[NetworkVar("m_bIntialStateUpdate")]
	public partial bool InitialStateUpdate { get; set; }
	[NetworkVar("m_bGuiding")]
	public partial bool Guiding { get; set; }
	[NetworkVar("m_bLaserDotSuspended")]
	public partial bool LaserDotSuspended { get; set; }
}

#if !CLIENT_DLL
[LinkEntityToClass("laser_spot")]
#endif
[NetworkName("CLaserDot_HL1")]
public partial class LaserDot_HL1 : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(IsOn),
	];

	[NetworkVar("m_bIsOn")]
	public partial bool IsOn { get; set; }
}
#endif
