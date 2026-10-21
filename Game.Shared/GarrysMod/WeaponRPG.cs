#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;

using System.Numerics;
namespace Game.Shared.GarrysMod;


[LinkEntityToClass("weapon_rpg")]
[PrecacheWeaponRegister("weapon_rpg")]
[NetworkName("CWeaponRPG")]
public partial class WeaponRPG : BaseHL2MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(InitialStateUpdate),
		nameof(Guiding),
		nameof(HideGuiding),
		nameof(Missile),
		new(nameof(LaserDot)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(Guiding),
		nameof(HideGuiding),
	];

	public WeaponRPG() {
		Missile = new();
	}

	[NetworkVar("m_bInitialStateUpdate")]
	public partial bool InitialStateUpdate { get; set; }
	[NetworkVar("m_bGuiding")]
	public partial bool Guiding { get; set; }
	[NetworkVar("m_bHideGuiding")]
	public partial bool HideGuiding { get; set; }
	// todo: RecvProxy_MissileDied
	[NetworkVar("m_hMissile")]
	public partial EHANDLE Missile { get; set; }
	[NetworkVar("m_vecLaserDot")]
	public partial Vector3 LaserDot { get; set; }
	public override float GetFireRate() => 1f;
}

[LinkEntityToClass("env_laserdot")]
#if CLIENT_DLL
[LinkEntityToClass("laser_spot")]
#endif
[NetworkName("CLaserDot")]
public partial class LaserDot : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
#endif
