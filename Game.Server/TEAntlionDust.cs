using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEAntlionDust")]
public partial class TEAntlionDust(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Origin)) { Encoding = NetEncoding.Vector, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Angles)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(BlockedSpawner),
	];

	[NetworkVar("m_vecAngles")]
	public Vector3 Angles;
	[NetworkVar("m_bBlockedSpawner")]
	public bool BlockedSpawner;

	// todo: UTIL_CreateAntlionDust sender (hl2/antlion_dust.cpp)

	public override void Test(in Vector3 current_origin, in QAngle current_angles) { }
}
