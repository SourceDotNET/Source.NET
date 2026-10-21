using Game.Shared;

using Source.Common;

using System.Numerics;

namespace Game.Client;

[NetworkName("CPropVehicleDriveable")]
public partial class C_PropVehicleDriveable : C_BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Player),
		nameof(Speed),
		new(nameof(RPM)) { Bits = 13 },
		new(nameof(Throttle)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoostTimeLeft)) { Bits = 8 },
		nameof(HasBoost),
		nameof(EnterAnimOn),
		nameof(ExitAnimOn),
		nameof(UnableToFire),
		new(nameof(EyeExitEndpoint)) { Bits = 0, Flags = PropFlags.Coord },
		nameof(HasGun),
		new(nameof(GunCrosshair)) { Bits = 0, Flags = PropFlags.Coord },
		nameof(Locked),
	];

	[NetworkVar("m_hPlayer")]
	public EHANDLE Player = new();
	[NetworkVar("m_nRPM")]
	public int RPM;
	[NetworkVar("m_flThrottle")]
	public float Throttle;
	[NetworkVar("m_nBoostTimeLeft")]
	public int BoostTimeLeft;
	[NetworkVar("m_nHasBoost")]
	public bool HasBoost;
	public bool ScannerDisabledWeapons;
	public bool ScannerDisabledVehicle;
	[NetworkVar("m_bEnterAnimOn")]
	public bool EnterAnimOn;
	[NetworkVar("m_bExitAnimOn")]
	public bool ExitAnimOn;
	[NetworkVar("m_bUnableToFire")]
	public bool UnableToFire;
	[NetworkVar("m_vecEyeExitEndpoint")]
	public Vector3 EyeExitEndpoint;
	[NetworkVar("m_bHasGun")]
	public bool HasGun;
	[NetworkVar("m_vecGunCrosshair")]
	public Vector3 GunCrosshair;
	[NetworkVar("m_bLocked")]
	public bool Locked;
}
