using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_particlescript")]
[NetworkName("CEnvParticleScript")]
public partial class EnvParticleScript : BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SequenceScale)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flSequenceScale")]
	public partial float SequenceScale { get; set; }
}
