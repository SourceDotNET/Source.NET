using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTELargeFunnel")]
public partial class C_TELargeFunnel : C_TEParticleSystem
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Reversed)) { Bits = 2, Flags = PropFlags.Unsigned },
	];

	public int ModelIndex;
	[NetworkVar("m_nReversed")]
	public int Reversed;
}

public static partial class TempEnts
{
	public static void TE_LargeFunnel(IRecipientFilter filter, float delay, in Vector3 pos, int modelIndex, int reversed) {
		throw new NotImplementedException();
	}
}
