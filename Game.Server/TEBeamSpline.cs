using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamSpline", NoBase = true)]
public partial class TEBeamSpline(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(NumPoints)) { Bits = 5, Flags = PropFlags.Unsigned },
		new(nameof(Points)) { Encoding = NetEncoding.Array, Bits = -1, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_nPoints")]
	public int NumPoints;
	[NetworkVar("m_vecPoints")]
	public InlineArrayMaxSplinePoints<Vector3> Points;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		NumPoints = 6;
		Vector3 vecStart = current_origin;

		vecStart.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward, out Vector3 right, out _);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		vecStart += forward * 100.0f;
		vecStart += right * -128.0f;

		for (int i = 0; i < NumPoints; i++) {
			Points[i] = vecStart;
			vecStart += right * (128 / NumPoints);
			vecStart += forward * (30.0f / NumPoints);
		}

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEBeamSpline g_TEBeamSpline = new("BeamSpline");

	public static void TE_BeamSpline<IRF>(scoped ref IRF filter, float delay, int points, Span<Vector3> rgPoints) where IRF : IRecipientFilter {
		int i;
		g_TEBeamSpline.NumPoints = points;
		for (i = 0; i < points; i++) {
#if GMOD_DLL
			if (i >= MAX_SPLINE_POINTS)
				continue;
#endif
			g_TEBeamSpline.Points[i] = rgPoints[i];
		}

		for (; i < MAX_SPLINE_POINTS; i++)
			g_TEBeamSpline.Points[i] = default;

		g_TEBeamSpline.Create(ref filter, delay);
	}
}
