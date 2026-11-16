using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamFollow")]
public partial class TEBeamFollow(ReadOnlySpan<char> name) : BaseBeam(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(EntIndex)) { Bits = 24, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_iEntIndex")]
	public int EntIndex;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		EntIndex = 1;
	}
}

public static partial class TempEnts
{
	static readonly TEBeamFollow g_TEBeamFollow = new("BeamFollow");

	public static void TE_BeamFollow<IRF>(scoped ref IRF filter, float delay, int entIndex, int modelIndex, int haloIndex, float life, float width, float endWidth, float fadeLength, float r, float g, float b, float a) where IRF : IRecipientFilter {
		g_TEBeamFollow.EntIndex = (entIndex & 0x0FFF) | ((1 & 0xF) << 12);
		g_TEBeamFollow.ModelIndex = modelIndex;
		g_TEBeamFollow.HaloIndex = haloIndex;
		g_TEBeamFollow.StartFrame = 0;
		g_TEBeamFollow.FrameRate = 0;
		g_TEBeamFollow.Life = life;
		g_TEBeamFollow.Width = width;
		g_TEBeamFollow.EndWidth = endWidth;
		g_TEBeamFollow.FadeLength = (int)fadeLength;
		g_TEBeamFollow.R = (int)r;
		g_TEBeamFollow.G = (int)g;
		g_TEBeamFollow.B = (int)b;
		g_TEBeamFollow.A = (int)a;

		g_TEBeamFollow.Create(ref filter, delay);
	}
}
