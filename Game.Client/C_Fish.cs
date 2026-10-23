using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CFish", Table = "DT_CFish", NoBase = true)]
public partial class C_Fish : C_BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(PoolOrigin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(X)) { Bits = 7, Low = -255.0f, High = 255.0f },
		new(nameof(Y)) { Bits = 7, Low = -255.0f, High = 255.0f },
		new(nameof(Z)) { Bits = 0, Flags = PropFlags.Coord | PropFlags.NoScale },
		new(nameof(Le)) { Bits = 7, Low = 0.0f, High = 360.0f },
		new(nameof(ModelIndex)) { Encoding = NetEncoding.Int, Bits = 14 },
		new(nameof(LifeState)) { Bits = 3, Flags = PropFlags.Unsigned },
		new(nameof(WaterLevel)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_poolOrigin")]
	public Vector3 PoolOrigin;
	[NetworkVar("m_x")]
	public float X;
	[NetworkVar("m_y")]
	public float Y;
	[NetworkVar("m_z")]
	public float Z;
	[NetworkVar("m_angle")]
	public float Le;
	[NetworkVar("m_waterLevel")]
	public new float WaterLevel;
}
