using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEMetalSparks", NoBase = true)]
public partial class TEMetalSparks(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Pos)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Dir)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_vecPos")]
	public Vector3 Pos;
	[NetworkVar("m_vecDir")]
	public Vector3 Dir;
}

public static partial class TempEnts
{
	static readonly TEMetalSparks g_TEMetalSparks = new("Metal Sparks");

	public static void TE_MetalSparks<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, in Vector3 dir) where IRF : IRecipientFilter {
		g_TEMetalSparks.Pos = pos;
		g_TEMetalSparks.Dir = dir;

		Assert(dir.Length() < 1.01);

		g_TEMetalSparks.Create(ref filter, delay);
	}
}
