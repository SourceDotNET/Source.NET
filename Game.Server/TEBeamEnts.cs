using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamEnts")]
public partial class TEBeamEnts(ReadOnlySpan<char> name) : BaseBeam(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartEntity)) { Bits = 24, Flags = PropFlags.Unsigned },
		new(nameof(EndEntity)) { Bits = 24, Flags = PropFlags.Unsigned },
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
		FrameRate = 10;
		Life = 2.0f;
		Width = 1.0f;
		Amplitude = 1.0f;
		R = 127;
		G = 63;
		B = 0;
		A = 150;
		Speed = 1;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEBeamEnts g_TEBeamEnts = new("BeamEnts");

	public static void TE_BeamEnts<IRF>(scoped ref IRF filter, float delay, int start, int end, int modelindex, int haloindex, int startframe, int framerate, float life, float width, float endWidth, int fadeLength, float amplitude, int r, int g, int b, int a, int speed) where IRF : IRecipientFilter {
		g_TEBeamEnts.StartEntity = (start & 0x0FFF) | ((1 & 0xF) << 12);
		g_TEBeamEnts.EndEntity = (end & 0x0FFF) | ((1 & 0xF) << 12);
		g_TEBeamEnts.ModelIndex = modelindex;
		g_TEBeamEnts.HaloIndex = haloindex;
		g_TEBeamEnts.StartFrame = startframe;
		g_TEBeamEnts.FrameRate = framerate;
		g_TEBeamEnts.Life = life;
		g_TEBeamEnts.Width = width;
		g_TEBeamEnts.EndWidth = endWidth;
		g_TEBeamEnts.FadeLength = fadeLength;
		g_TEBeamEnts.Amplitude = amplitude;
		g_TEBeamEnts.Speed = speed;
		g_TEBeamEnts.R = r;
		g_TEBeamEnts.G = g;
		g_TEBeamEnts.B = b;
		g_TEBeamEnts.A = a;

		g_TEBeamEnts.Create(ref filter, delay);
	}
}
