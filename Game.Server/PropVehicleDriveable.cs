using Game.Shared;

using Source.Common;
using Source.Common.Physics;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

[LinkEntityToClass("prop_vehicle_driveable")]
[NetworkName("CPropVehicleDriveable")]
public partial class PropVehicleDriveable : BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Player),
		new(nameof(Speed)) { Bits = 8 },
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

	public PropVehicleDriveable() {
		Player = new();
	}


	[NetworkVar("m_hPlayer")]
	public partial EHANDLE Player { get; set; }
	[NetworkVar("m_nSpeed")]
	public new partial int Speed { get; set; }
	[NetworkVar("m_nRPM")]
	public partial int RPM { get; set; }
	[NetworkVar("m_flThrottle")]
	public partial float Throttle { get; set; }
	[NetworkVar("m_nBoostTimeLeft")]
	public partial int BoostTimeLeft { get; set; }
	[NetworkVar("m_nHasBoost")]
	public partial bool HasBoost { get; set; }
	public bool ScannerDisabledWeapons;
	public bool ScannerDisabledVehicle;
	[NetworkVar("m_bEnterAnimOn")]
	public partial bool EnterAnimOn { get; set; }
	[NetworkVar("m_bExitAnimOn")]
	public partial bool ExitAnimOn { get; set; }
	[NetworkVar("m_bUnableToFire")]
	public partial bool UnableToFire { get; set; }
	[NetworkVar("m_vecEyeExitEndpoint")]
	public partial Vector3 EyeExitEndpoint { get; set; }
	[NetworkVar("m_bHasGun")]
	public partial bool HasGun { get; set; }
	[NetworkVar("m_vecGunCrosshair")]
	public partial Vector3 GunCrosshair { get; set; }
	[NetworkVar("m_bLocked")]
	public partial bool Locked { get; set; }
	readonly FourWheelVehiclePhysics VehiclePhysics = new();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public FourWheelVehiclePhysics GetPhysics() => VehiclePhysics;

	public override bool IsVehicle() => true;

	public override int VPhysicsGetObjectList(Span<IPhysicsObject?> list) {
		return GetPhysics().VPhysicsGetObjectList(list);
	}
}
