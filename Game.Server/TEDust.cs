using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEDust")]
public partial class TEDust(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(LSize)) { Bits = 0, Flags = PropFlags.Coord | PropFlags.NoScale },
		new(nameof(LSpeed)) { Bits = 0, Flags = PropFlags.Coord | PropFlags.NoScale },
		new(nameof(Direction)) { Bits = 4, Low = -1.0f, High = 1.0f },
	];

	[NetworkVar("m_flSize")]
	public float LSize;
	[NetworkVar("m_flSpeed")]
	public float LSpeed;
	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;
}

public static partial class TempEnts
{
	static readonly TEDust g_TEDust = new("Dust");

	public static void TE_Dust<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, in Vector3 dir, float size, float speed) where IRF : IRecipientFilter {
		g_TEDust.Origin = pos;
		g_TEDust.Direction = dir;
		g_TEDust.LSize = size;
		g_TEDust.LSpeed = speed;

		Assert(dir.Length() < 1.01);

		g_TEDust.Create(ref filter, delay);
	}
}
