using Source.Common;
using Source;
using System.Numerics;

namespace Game.Client;


[NetworkName("CPropVehicleChoreoGeneric")]
public partial class C_PropVehicleChoreoGeneric : C_DynamicProp
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
	public EHANDLE Player;
	[NetworkVar("m_bEnterAnimOn")]
	public bool EnterAnimOn;
	[NetworkVar("m_bExitAnimOn")]
	public bool ExitAnimOn;
	[NetworkVar("m_bForceEyesToAttachment")]
	public bool ForceEyesToAttachment;
	[NetworkVar("m_vecEyeExitEndpoint")]
	public Vector3 EyeExitEndpoint;
	[NetworkVar("m_vehicleView.bClampEyeAngles")]
	public bool VehicleViewClampEyeAngles;
	[NetworkVar("m_vehicleView.flPitchCurveZero")]
	public float VehicleViewPitchCurveZero;
	[NetworkVar("m_vehicleView.flPitchCurveLinear")]
	public float VehicleViewPitchCurveLinear;
	[NetworkVar("m_vehicleView.flRollCurveZero")]
	public float VehicleViewRollCurveZero;
	[NetworkVar("m_vehicleView.flRollCurveLinear")]
	public float VehicleViewRollCurveLinear;
	[NetworkVar("m_vehicleView.flFOV")]
	public float VehicleViewFOV;
	[NetworkVar("m_vehicleView.flYawMin")]
	public float VehicleViewYawMin;
	[NetworkVar("m_vehicleView.flYawMax")]
	public float VehicleViewYawMax;
	[NetworkVar("m_vehicleView.flPitchMin")]
	public float VehicleViewPitchMin;
	[NetworkVar("m_vehicleView.flPitchMax")]
	public float VehicleViewPitchMax;

}
