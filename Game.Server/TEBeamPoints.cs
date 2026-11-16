using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamPoints")]
public partial class TEBeamPoints(ReadOnlySpan<char> name) : BaseBeam(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartPoint)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(EndPoint)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_vecStartPoint")]
	public Vector3 StartPoint;
	[NetworkVar("m_vecEndPoint")]
	public Vector3 EndPoint;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		ModelIndex = BaseCombatWeapon.g_sModelIndexSmoke;
		StartFrame = 0;
		FrameRate = 10;
		Life = 2.0f;
		Width = 1.0f;
		Amplitude = 1.0f;
		R = 0;
		G = 63;
		B = 127;
		A = 150;
		Speed = 1;

		StartPoint = current_origin;

		StartPoint += new Vector3(0, 0, 30);

		MathLib.AngleVectors(current_angles, out Vector3 forward, out Vector3 right, out _);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		StartPoint += forward * 75.0f;
		EndPoint = StartPoint + right * 25.0f;
		StartPoint += right * -25.0f;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEBeamPoints g_TEBeamPoints = new("BeamPoints");

	public static void TE_BeamPoints<IRF>(scoped ref IRF filter, float delay, in Vector3 start, in Vector3 end, int modelindex, int haloindex, int startframe, int framerate, float life, float width, float endWidth, int fadeLength, float amplitude, int r, int g, int b, int a, int speed) where IRF : IRecipientFilter {
		g_TEBeamPoints.StartPoint = start;
		g_TEBeamPoints.EndPoint = end;
		g_TEBeamPoints.ModelIndex = modelindex;
		g_TEBeamPoints.HaloIndex = haloindex;
		g_TEBeamPoints.StartFrame = startframe;
		g_TEBeamPoints.FrameRate = framerate;
		g_TEBeamPoints.Life = life;
		g_TEBeamPoints.Width = width;
		g_TEBeamPoints.EndWidth = endWidth;
		g_TEBeamPoints.FadeLength = fadeLength;
		g_TEBeamPoints.Amplitude = amplitude;
		g_TEBeamPoints.Speed = speed;
		g_TEBeamPoints.R = r;
		g_TEBeamPoints.G = g;
		g_TEBeamPoints.B = b;
		g_TEBeamPoints.A = a;

		g_TEBeamPoints.Create(ref filter, delay);
	}
}
