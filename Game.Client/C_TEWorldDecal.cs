using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEWorldDecal")]
public partial class C_TEWorldDecal : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Index)) { Bits = 9, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_nIndex")]
	public int Index;
}

public static partial class TempEnts
{
	public static void TE_WorldDecal(IRecipientFilter filter, float delay, in Vector3 pos, int index) {
		throw new NotImplementedException();
	}
}
