using Game.Server;
using Game.Shared;

using Source.Common;

using System.Numerics;

namespace Game.Server.HL2;
[LinkEntityToClass("prop_vehicle_airboat")]
[NetworkName("CPropAirboat")]
public partial class PropAirboat : PropVehicleDriveable
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(HeadlightIsOn),
		new(nameof(AmmoCount)) { Bits = 9 },
		new(nameof(ExactWaterLevel)) { Bits = 24 },
		new(nameof(WaterLevel)) { Encoding = NetEncoding.Int, Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(PhysVelocity)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_bHeadlightIsOn")]
	public partial bool HeadlightIsOn { get; set; }
	[NetworkVar("m_nAmmoCount")]
	public partial int AmmoCount { get; set; }
	[NetworkVar("m_nExactWaterLevel")]
	public partial int ExactWaterLevel { get; set; }
	[NetworkVar("m_vecPhysVelocity")]
	public partial Vector3 PhysVelocity { get; set; }
}
