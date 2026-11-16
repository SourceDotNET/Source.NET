using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamRing")]
public partial class TEBeamRing(ReadOnlySpan<char> name) : BaseBeam(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartEntity)) { Bits = 13, Flags = PropFlags.Unsigned },
		new(nameof(EndEntity)) { Bits = 13, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nStartEntity")]
	public int StartEntity;
	[NetworkVar("m_nEndEntity")]
	public int EndEntity;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		StartEntity = 1;
		EndEntity = 0;
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
	static readonly TEBeamRing g_TEBeamRing = new("BeamRing");

	public static void TE_BeamRing<IRF>(scoped ref IRF filter, float delay, int start, int end, int modelindex, int haloindex, int startframe, int framerate, float life, float width, int spread, float amplitude, int r, int g, int b, int a, int speed, int flags = 0) where IRF : IRecipientFilter {
		g_TEBeamRing.StartEntity = (start & 0x0FFF) | ((1 & 0xF) << 12);
		g_TEBeamRing.EndEntity = (end & 0x0FFF) | ((1 & 0xF) << 12);
		g_TEBeamRing.ModelIndex = modelindex;
		g_TEBeamRing.HaloIndex = haloindex;
		g_TEBeamRing.StartFrame = startframe;
		g_TEBeamRing.FrameRate = framerate;
		g_TEBeamRing.Life = life;
		g_TEBeamRing.Width = width;
		g_TEBeamRing.EndWidth = width;
		g_TEBeamRing.FadeLength = 0;
		g_TEBeamRing.Amplitude = amplitude;
		g_TEBeamRing.Speed = speed;
		g_TEBeamRing.R = r;
		g_TEBeamRing.G = g;
		g_TEBeamRing.B = b;
		g_TEBeamRing.A = a;
		g_TEBeamRing.Flags = flags;

		g_TEBeamRing.Create(ref filter, delay);
	}
}
