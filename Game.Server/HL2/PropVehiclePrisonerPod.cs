using Game.Server;
using Game.Shared;

using Source.Common;

using System.Numerics;

namespace Game.Server.HL2;
[LinkEntityToClass("prop_vehicle_prisoner_pod")]
[NetworkName("CPropVehiclePrisonerPod")]
public partial class PropVehiclePrisonerPod : PhysicsProp
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Player),
		nameof(EnterAnimOn),
		nameof(ExitAnimOn),
		new(nameof(EyeExitEndpoint)) { Bits = 0, Flags = PropFlags.Coord },
		nameof(LimitView),
		nameof(Locked),
	];

	public PropVehiclePrisonerPod() {
		Player = new();
		EnterAnimOn = new();
		ExitAnimOn = new();
		EyeExitEndpoint = new();
		LimitView = new();
	}


	[NetworkVar("m_hPlayer")]
	public partial EHANDLE Player { get; set; }
	[NetworkVar("m_bEnterAnimOn")]
	public partial bool EnterAnimOn { get; set; }
	[NetworkVar("m_bExitAnimOn")]
	public partial bool ExitAnimOn { get; set; }
	[NetworkVar("m_vecEyeExitEndpoint")]
	public partial Vector3 EyeExitEndpoint { get; set; }
	[NetworkVar("m_bLimitView")]
	public partial bool LimitView { get; set; }
	[NetworkVar("m_bLocked")]
	public partial bool Locked { get; set; }

	public override bool IsVehicle() => true;
}
