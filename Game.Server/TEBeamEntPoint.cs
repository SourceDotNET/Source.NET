using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamEntPoint")]
public partial class TEBeamEntPoint(ReadOnlySpan<char> name) : BaseBeam(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartEntity)) { Bits = 24, Flags = PropFlags.Unsigned },
		new(nameof(EndEntity)) { Bits = 24, Flags = PropFlags.Unsigned },
		new(nameof(StartPoint)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(EndPoint)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_nStartEntity")]
	public int StartEntity;
	[NetworkVar("m_nEndEntity")]
	public int EndEntity;
	[NetworkVar("m_vecStartPoint")]
	public Vector3 StartPoint;
	[NetworkVar("m_vecEndPoint")]
	public Vector3 EndPoint;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		StartEntity = 1;
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

		EndPoint = current_origin;

		EndPoint += new Vector3(0, 0, 24);

		MathLib.AngleVectors(current_angles, out Vector3 forward, out Vector3 right, out _);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		EndPoint += forward * 50.0f;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEBeamEntPoint g_TEBeamEntPoint = new("BeamEntPoint");

	public static void TE_BeamEntPoint<IRF>(scoped ref IRF filter, float delay, int startEntity, in Vector3 start, int endEntity, in Vector3 end, int modelindex, int haloindex, int startframe, int framerate, float life, float width, float endWidth, int fadeLength, float amplitude, int r, int g, int b, int a, int speed) where IRF : IRecipientFilter {
		g_TEBeamEntPoint.StartEntity = (startEntity > 0) ? (startEntity & 0x0FFF) | ((1 & 0xF) << 12) : 0;
		g_TEBeamEntPoint.EndEntity = (endEntity > 0) ? (endEntity & 0x0FFF) | ((1 & 0xF) << 12) : 0;
		g_TEBeamEntPoint.StartPoint = start;
		g_TEBeamEntPoint.EndPoint = end;
		g_TEBeamEntPoint.ModelIndex = modelindex;
		g_TEBeamEntPoint.HaloIndex = haloindex;
		g_TEBeamEntPoint.StartFrame = startframe;
		g_TEBeamEntPoint.FrameRate = framerate;
		g_TEBeamEntPoint.Life = life;
		g_TEBeamEntPoint.Width = width;
		g_TEBeamEntPoint.EndWidth = endWidth;
		g_TEBeamEntPoint.FadeLength = fadeLength;
		g_TEBeamEntPoint.Amplitude = amplitude;
		g_TEBeamEntPoint.Speed = speed;
		g_TEBeamEntPoint.R = r;
		g_TEBeamEntPoint.G = g;
		g_TEBeamEntPoint.B = b;
		g_TEBeamEntPoint.A = a;

		g_TEBeamEntPoint.Create(ref filter, delay);
	}
}
