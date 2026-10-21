using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;
namespace Game.Client;

[NetworkName("CPropJeep")]
public partial class C_PropJeep : C_PropVehicleDriveable
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(HeadlightIsOn),
	];

	[NetworkVar("m_bHeadlightIsOn")]
	public bool HeadlightIsOn;

	public override bool IsJeep() => true;
}

[NetworkName("CPropJeepEpisodic", Table = "DT_CPropJeepEpisodic")]
public partial class C_PropJeepEpisodic : C_PropJeep
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(NumRadarContacts),
		new(nameof(RadarContactPos)) { Encoding = NetEncoding.Array2 },
		new(nameof(RadarContactType)) { Encoding = NetEncoding.Array2 },
	];

	[NetworkVar("m_iNumRadarContacts")]
	public int NumRadarContacts;
	[NetworkVar("m_vecRadarContactPos")]
	public InlineArray24<Vector3> RadarContactPos;
	[NetworkVar("m_iRadarContactType")]
	public InlineArray24<int> RadarContactType;
}
