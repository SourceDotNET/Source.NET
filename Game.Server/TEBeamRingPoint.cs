using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamRingPoint")]
public partial class TEBeamRingPoint(ReadOnlySpan<char> name) : BaseBeam(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Center)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(LStartRadius)) { Bits = 16, Flags = PropFlags.RoundUp, Low = 0.0f, High = 4096.0f },
		new(nameof(LEndRadius)) { Bits = 16, Flags = PropFlags.RoundUp, Low = 0.0f, High = 4096.0f },
	];

	[NetworkVar("m_vecCenter")]
	public Vector3 Center;
	[NetworkVar("m_flStartRadius")]
	public float LStartRadius;
	[NetworkVar("m_flEndRadius")]
	public float LEndRadius;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		Center = current_origin;
		LEndRadius = 256.0f;
		LStartRadius = 16.0f;
		ModelIndex = BaseCombatWeapon.g_sModelIndexSmoke;
		StartFrame = 0;
		FrameRate = 2;
		Life = 10.0f;
		Width = 2.0f;
		Amplitude = 1.0f;
		R = 255;
		G = 255;
		B = 0;
		A = 127;
		Speed = 5;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEBeamRingPoint g_TEBeamRingPoint = new("BeamRingPoint");

	public static void TE_BeamRingPoint<IRF>(scoped ref IRF filter, float delay, in Vector3 center, float startRadius, float endRadius, int modelindex, int haloindex, int startframe, int framerate, float life, float width, int spread, float amplitude, int r, int g, int b, int a, int speed, int flags = 0) where IRF : IRecipientFilter {
		g_TEBeamRingPoint.Center = center;
		g_TEBeamRingPoint.LStartRadius = startRadius;
		g_TEBeamRingPoint.LEndRadius = endRadius;
		g_TEBeamRingPoint.ModelIndex = modelindex;
		g_TEBeamRingPoint.HaloIndex = haloindex;
		g_TEBeamRingPoint.StartFrame = startframe;
		g_TEBeamRingPoint.FrameRate = framerate;
		g_TEBeamRingPoint.Life = life;
		g_TEBeamRingPoint.Width = width;
		g_TEBeamRingPoint.EndWidth = width;
		g_TEBeamRingPoint.FadeLength = 0;
		g_TEBeamRingPoint.Amplitude = amplitude;
		g_TEBeamRingPoint.Speed = speed;
		g_TEBeamRingPoint.R = r;
		g_TEBeamRingPoint.G = g;
		g_TEBeamRingPoint.B = b;
		g_TEBeamRingPoint.A = a;
		g_TEBeamRingPoint.Flags = flags;

		g_TEBeamRingPoint.Create(ref filter, delay);
	}
}
