using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("prop_vehicle_crane")]
[NetworkName("CPropCrane")]
public partial class PropCrane : BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Player),
		nameof(MagnetOn),
		nameof(EnterAnimOn),
		nameof(ExitAnimOn),
		new(nameof(EyeExitEndpoint)) { Bits = 0, Flags = PropFlags.Coord },
	];

	public PropCrane() {
		Player = new();
	}


	[NetworkVar("m_hPlayer")]
	public partial EHANDLE Player { get; set; }
	[NetworkVar("m_bMagnetOn")]
	public partial bool MagnetOn { get; set; }
	[NetworkVar("m_bEnterAnimOn")]
	public partial bool EnterAnimOn { get; set; }
	[NetworkVar("m_bExitAnimOn")]
	public partial bool ExitAnimOn { get; set; }
	[NetworkVar("m_vecEyeExitEndpoint")]
	public partial Vector3 EyeExitEndpoint { get; set; }

	public override bool IsVehicle() => true;
}
