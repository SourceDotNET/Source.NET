using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEShowLine")]
public partial class C_TEShowLine : C_TEParticleSystem
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(End)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_vecEnd")]
	public Vector3 End;
}

public static partial class TempEnts
{
	public static void TE_ShowLine(IRecipientFilter filter, float delay, in Vector3 start, in Vector3 end) {
		throw new NotImplementedException();
	}
}
