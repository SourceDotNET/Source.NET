using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEGaussExplosion")]
public partial class TEGaussExplosion(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Type)) { Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(Direction)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_nType")]
	public int Type;
	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) { }
}

public static partial class TempEnts
{
	static readonly TEGaussExplosion g_TEGaussExplosion = new("GaussExplosion");

	public static void TE_GaussExplosion<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, in Vector3 dir, int type) where IRF : IRecipientFilter {
		g_TEGaussExplosion.Origin = pos;
		g_TEGaussExplosion.Direction = dir;
		g_TEGaussExplosion.Type = type;

		g_TEGaussExplosion.Create(ref filter, delay);
	}
}
