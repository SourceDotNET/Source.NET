using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_embers")]
[NetworkName("CEmbers")]
public partial class Embers : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Density)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Lifetime)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Speed)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Emit)) { Bits = 2, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nDensity")]
	public partial int Density { get; set; }
	[NetworkVar("m_nLifetime")]
	public partial int Lifetime { get; set; }
	[NetworkVar("m_nSpeed")]
	public new partial int Speed { get; set; }
	[NetworkVar("m_bEmit")]
	public partial int Emit { get; set; }
}
