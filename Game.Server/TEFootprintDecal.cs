using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEFootprintDecal")]
public partial class TEFootprintDecal(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Direction)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Entity)) { Bits = 11, Flags = PropFlags.Unsigned },
		new(nameof(Index)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;
	[NetworkVar("m_nEntity")]
	public int Entity;
	[NetworkVar("m_nIndex")]
	public int Index;
}

public static partial class TempEnts
{
	static readonly TEFootprintDecal g_TEFootprintDecal = new("Footprint Decal");

	public static void TE_FootprintDecal<IRF>(scoped ref IRF filter, float delay, in Vector3 origin, in Vector3 right, int entity, int index, byte materialType) where IRF : IRecipientFilter {
		g_TEFootprintDecal.Origin = origin;
		g_TEFootprintDecal.Direction = right;
		g_TEFootprintDecal.Entity = entity;
		g_TEFootprintDecal.Index = index;

		MathLib.VectorNormalize(ref g_TEFootprintDecal.Direction);

		g_TEFootprintDecal.Create(ref filter, delay);
	}
}
