using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;
namespace Game.Client;


[NetworkName("CTEBeamSpline", NoBase = true)]
public partial class C_TEBeamSpline : C_BaseTempEntity
{
	public const int MAX_SPLINE_POINTS = 16;
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		nameof(NumPoints),
		new(nameof(Points)) { Encoding = NetEncoding.Array },
	];

	[NetworkVar("m_nPoints")]
	public int NumPoints;
	[NetworkVar("m_vecPoints")]
	public InlineArrayMaxSplinePoints<Vector3> Points;

	public override void PostDataUpdate(DataUpdateType updateType) {
		DevMsg(1, $"Beam spline with {NumPoints} points received\n");
	}
}

public static partial class TempEnts
{
	public static void TE_BeamSpline(IRecipientFilter filter, float delay, int points, Span<Vector3> rgPoints) {
		throw new NotImplementedException();
	}
}
