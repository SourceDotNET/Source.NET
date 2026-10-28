using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;
namespace Game.Server;

[NetworkName("CPropJeep")]
public partial class PropJeep : PropVehicleDriveable
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(HeadlightIsOn),
	];

	[NetworkVar("m_bHeadlightIsOn")]
	public partial bool HeadlightIsOn { get; set; }

	public override bool IsJeep() => true;
}

[LinkEntityToClass("prop_vehicle_jeep")]
[LinkEntityToClass("prop_vehicle_jeep_old")]
[NetworkName("CPropJeepEpisodic", Table = "DT_CPropJeepEpisodic")]
public partial class PropJeepEpisodic : PropJeep
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(NumRadarContacts)) { Bits = 8 },
		new(nameof(RadarContactPos)) { Encoding = NetEncoding.Array2, Bits = 0, Flags = PropFlags.Coord },
		new(nameof(RadarContactType)) { Encoding = NetEncoding.Array2, Bits = 4 },
	];

	[NetworkVar("m_iNumRadarContacts")]
	public partial int NumRadarContacts { get; set; }
	[NetworkVar("m_vecRadarContactPos")]
	public partial ref readonly InlineArray24<Vector3> RadarContactPos { get; }
	[NetworkVar("m_iRadarContactType")]
	public partial ref readonly InlineArray24<int> RadarContactType { get; }
}
