using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEMetalSparks", NoBase = true)]
public partial class C_TEMetalSparks : C_BaseTempEntity
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
	public static void TE_MetalSparks(IRecipientFilter filter, float delay, in Vector3 pos, in Vector3 dir) {
		throw new NotImplementedException();
	}
}
