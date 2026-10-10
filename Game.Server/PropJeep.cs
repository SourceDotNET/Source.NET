using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;
namespace Game.Server;

using FIELD_PJ = Source.FIELD<PropJeep>;
using FIELD_PJE = Source.FIELD<PropJeepEpisodic>;
[NetworkName("CPropJeep")]
public class PropJeep : PropVehicleDriveable
{
	public static readonly SendTable DT_PropJeep = new(DT_PropVehicleDriveable, [
		SendPropBool(FIELD_PJ.OF(nameof(HeadlightIsOn)))
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_PropJeep);

	[NetworkName("m_bHeadlightIsOn")]
	public bool HeadlightIsOn;

	public override bool IsJeep() => true;
}

[LinkEntityToClass("prop_vehicle_jeep")]
[LinkEntityToClass("prop_vehicle_jeep_old")]
[NetworkName("CPropJeepEpisodic")]
public class PropJeepEpisodic : PropJeep
{
	public static readonly SendTable DT_CPropJeepEpisodic = new(DT_PropJeep, [
		SendPropInt(FIELD_PJE.OF(nameof(NumRadarContacts)), 8),

		SendPropVector(FIELD_PJE.OF_SENDINFO_ARRAY(nameof(RadarContactPos)), 0, PropFlags.Coord),
		SendPropArray2(null!, 24, "m_vecRadarContactPos"),

		SendPropInt(FIELD_PJE.OF_SENDINFO_ARRAY(nameof(RadarContactType)), 4),
		SendPropArray2(null!, 24, "m_iRadarContactType"),
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_CPropJeepEpisodic);

	[NetworkName("m_iNumRadarContacts")]
	public int NumRadarContacts;
	[NetworkName("m_vecRadarContactPos")]
	public InlineArray24<Vector3> RadarContactPos;
	[NetworkName("m_iRadarContactType")]
	public InlineArray24<int> RadarContactType;
}
