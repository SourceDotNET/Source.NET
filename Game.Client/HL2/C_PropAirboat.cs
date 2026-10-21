using Game.Shared;

using Source.Common;

using System.Numerics;

namespace Game.Client.HL2;

[NetworkName("CPropAirboat")]
public partial class C_PropAirboat : C_PropVehicleDriveable
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(HeadlightIsOn),
		new(nameof(AmmoCount)) { Bits = 9 },
		new(nameof(ExactWaterLevel)) { Bits = 24 },
		new(nameof(WaterLevel)) { Encoding = NetEncoding.Int, Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(PhysVelocity)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_bHeadlightIsOn")]
	public bool HeadlightIsOn;
	[NetworkVar("m_nAmmoCount")]
	public int AmmoCount;
	[NetworkVar("m_nExactWaterLevel")]
	public int ExactWaterLevel;
	[NetworkVar("m_vecPhysVelocity")]
	public Vector3 PhysVelocity;
}
