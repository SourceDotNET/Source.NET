using Game.Shared;
using Source.Common;
using Source;
using System.Numerics;

namespace Game.Server;


[LinkEntityToClass("prop_vehicle_choreo_generic")]
[NetworkName("CPropVehicleChoreoGeneric")]
public partial class PropVehicleChoreoGeneric : DynamicProp
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Player),
		nameof(EnterAnimOn),
		nameof(ExitAnimOn),
		nameof(ForceEyesToAttachment),
		new(nameof(EyeExitEndpoint)) { Bits = 0, Flags = PropFlags.Coord },
		nameof(VehicleViewClampEyeAngles),
		new(nameof(VehicleViewPitchCurveZero)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(VehicleViewPitchCurveLinear)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(VehicleViewRollCurveZero)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(VehicleViewRollCurveLinear)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(VehicleViewFOV)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(VehicleViewYawMin)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(VehicleViewYawMax)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(VehicleViewPitchMin)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(VehicleViewPitchMax)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_hPlayer")]
	public partial EHANDLE Player { get; set; }
	[NetworkVar("m_bEnterAnimOn")]
	public partial bool EnterAnimOn { get; set; }
	[NetworkVar("m_bExitAnimOn")]
	public partial bool ExitAnimOn { get; set; }
	[NetworkVar("m_bForceEyesToAttachment")]
	public partial bool ForceEyesToAttachment { get; set; }
	[NetworkVar("m_vecEyeExitEndpoint")]
	public partial Vector3 EyeExitEndpoint { get; set; }
	[NetworkVar("m_vehicleView.bClampEyeAngles")]
	public partial bool VehicleViewClampEyeAngles { get; set; }
	[NetworkVar("m_vehicleView.flPitchCurveZero")]
	public partial float VehicleViewPitchCurveZero { get; set; }
	[NetworkVar("m_vehicleView.flPitchCurveLinear")]
	public partial float VehicleViewPitchCurveLinear { get; set; }
	[NetworkVar("m_vehicleView.flRollCurveZero")]
	public partial float VehicleViewRollCurveZero { get; set; }
	[NetworkVar("m_vehicleView.flRollCurveLinear")]
	public partial float VehicleViewRollCurveLinear { get; set; }
	[NetworkVar("m_vehicleView.flFOV")]
	public partial float VehicleViewFOV { get; set; }
	[NetworkVar("m_vehicleView.flYawMin")]
	public partial float VehicleViewYawMin { get; set; }
	[NetworkVar("m_vehicleView.flYawMax")]
	public partial float VehicleViewYawMax { get; set; }
	[NetworkVar("m_vehicleView.flPitchMin")]
	public partial float VehicleViewPitchMin { get; set; }
	[NetworkVar("m_vehicleView.flPitchMax")]
	public partial float VehicleViewPitchMax { get; set; }

}
