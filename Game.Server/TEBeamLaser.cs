using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamLaser")]
public partial class TEBeamLaser(ReadOnlySpan<char> name) : BaseBeam(name)
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
	static readonly TEBeamLaser g_TEBeamLaser = new("BeamLaser");

	public static void TE_BeamLaser<IRF>(scoped ref IRF filter, float delay, int start, int end, int modelindex, int haloindex, int startframe, int framerate, float life, float width, float endWidth, int fadeLength, float amplitude, int r, int g, int b, int a, int speed) where IRF : IRecipientFilter {
		g_TEBeamLaser.StartEntity = (start & 0x0FFF) | ((1 & 0xF) << 12);
		g_TEBeamLaser.EndEntity = (end & 0x0FFF) | ((1 & 0xF) << 12);
		g_TEBeamLaser.ModelIndex = modelindex;
		g_TEBeamLaser.HaloIndex = haloindex;
		g_TEBeamLaser.StartFrame = startframe;
		g_TEBeamLaser.FrameRate = framerate;
		g_TEBeamLaser.Life = life;
		g_TEBeamLaser.Width = width;
		g_TEBeamLaser.EndWidth = endWidth;
		g_TEBeamLaser.FadeLength = fadeLength;
		g_TEBeamLaser.Amplitude = amplitude;
		g_TEBeamLaser.Speed = speed;
		g_TEBeamLaser.R = r;
		g_TEBeamLaser.G = g;
		g_TEBeamLaser.B = b;
		g_TEBeamLaser.A = a;

		g_TEBeamLaser.Create(ref filter, delay);
	}
}
