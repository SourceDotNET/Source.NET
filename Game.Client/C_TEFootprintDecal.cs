using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEFootprintDecal")]
public partial class C_TEFootprintDecal : C_BaseTempEntity
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
	public int ChMaterialType;
}

public static partial class TempEnts
{
	public static void TE_FootprintDecal(IRecipientFilter filter, float delay, in Vector3 origin, in Vector3 right, int entity, int index, byte materialType) {
		throw new NotImplementedException();
	}
}
