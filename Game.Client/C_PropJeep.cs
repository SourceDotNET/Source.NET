using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;
namespace Game.Client;

using FIELD_PJ = Source.FIELD<C_PropJeep>;
using FIELD_PJE = Source.FIELD<C_PropJeepEpisodic>;
[NetworkName("CPropJeep")]
public class C_PropJeep : C_PropVehicleDriveable
{
	public static readonly RecvTable DT_PropJeep = new(DT_PropVehicleDriveable, [
		RecvPropBool(FIELD_PJ.OF(nameof(HeadlightIsOn)))
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_PropJeep);

	[NetworkName("m_bHeadlightIsOn")]
	public bool HeadlightIsOn;

	public override bool IsJeep() => true;
}

[NetworkName("CPropJeepEpisodic")]
public class C_PropJeepEpisodic : C_PropJeep
{
	public static readonly RecvTable DT_CPropJeepEpisodic = new(DT_PropJeep, [
		RecvPropInt(FIELD_PJE.OF(nameof(NumRadarContacts))),

		RecvPropVector(FIELD_PJE.OF_ARRAYINDEX(nameof(RadarContactPos), 0)),
		RecvPropArray2(null!, 24, "m_vecRadarContactPos"),

		RecvPropInt(FIELD_PJE.OF_ARRAYINDEX(nameof(RadarContactType), 0)),
		RecvPropArray2(null!, 24, "m_iRadarContactType"),
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_CPropJeepEpisodic);

	[NetworkName("m_iNumRadarContacts")]
	public int NumRadarContacts;
	[NetworkName("m_vecRadarContactPos")]
	public InlineArray24<Vector3> RadarContactPos;
	[NetworkName("m_iRadarContactType")]
	public InlineArray24<int> RadarContactType;
}
