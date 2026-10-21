using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CEnvParticleScript")]
public partial class C_EnvParticleScript : C_BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SequenceScale)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flSequenceScale")]
	public float SequenceScale;
}
