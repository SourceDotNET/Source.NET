using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("fish")]
[NetworkName("CFish", Table = "DT_CFish", NoBase = true)]
public partial class Fish : BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(PoolOrigin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Le)) { Bits = 7, Low = 0.0f, High = 360.0f },
		new(nameof(X)) { Bits = 7, Low = -255.0f, High = 255.0f },
		new(nameof(Y)) { Bits = 7, Low = -255.0f, High = 255.0f },
		new(nameof(Z)) { Bits = 0, Flags = PropFlags.Coord | PropFlags.NoScale },
		new(nameof(ModelIndex)) { Encoding = NetEncoding.Int, Bits = 14 },
		new(nameof(LifeState)) { Bits = 3, Flags = PropFlags.Unsigned },
		new(nameof(WaterLevel)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_poolOrigin")]
	public partial Vector3 PoolOrigin { get; set; }
	[NetworkVar("m_angle")]
	public partial float Le { get; set; }
	[NetworkVar("m_x")]
	public partial float X { get; set; }
	[NetworkVar("m_y")]
	public partial float Y { get; set; }
	[NetworkVar("m_z")]
	public partial float Z { get; set; }
	[NetworkVar("m_waterLevel")]
	public new partial float WaterLevel { get; set; }
}
