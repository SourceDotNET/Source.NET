using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CEmbers")]
public partial class C_Embers : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Density)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Lifetime)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Speed)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Emit)) { Bits = 2, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nDensity")]
	public int Density;
	[NetworkVar("m_nLifetime")]
	public int Lifetime;
	[NetworkVar("m_nSpeed")]
	public new int Speed;
	[NetworkVar("m_bEmit")]
	public int Emit;
}
