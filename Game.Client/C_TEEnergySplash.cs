using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEEnergySplash", NoBase = true)]
public partial class C_TEEnergySplash : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Pos)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Dir)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Explosive)) { Bits = 1, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecPos")]
	public Vector3 Pos;
	[NetworkVar("m_vecDir")]
	public Vector3 Dir;
	[NetworkVar("m_bExplosive")]
	public int Explosive;
}

public static partial class TempEnts
{
	public static void TE_EnergySplash(IRecipientFilter filter, float delay, in Vector3 pos, in Vector3 dir, bool explosive) {
		throw new NotImplementedException();
	}
}
