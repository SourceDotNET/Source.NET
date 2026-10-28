using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEBloodStream")]
public partial class C_TEBloodStream : C_TEParticleSystem
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Direction)) { Bits = 11, Low = -10.0f, High = 10.0f },
		new(nameof(R)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(G)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(B)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(A)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Amount)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;
	[NetworkVar("r")]
	public int R;
	[NetworkVar("g")]
	public int G;
	[NetworkVar("b")]
	public int B;
	[NetworkVar("a")]
	public int A;
	[NetworkVar("m_nAmount")]
	public int Amount;
}

public static partial class TempEnts
{
	public static void TE_BloodStream(IRecipientFilter filter, float delay, in Vector3 org, in Vector3 dir, int r, int g, int b, int a, int amount) {
		throw new NotImplementedException();
	}
}
