using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_fire_trail")]
[NetworkName("CFireTrail")]
public partial class FireTrail : BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Attachment)) { Bits = 32 },
		new(nameof(Lifetime)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_nAttachment")]
	public partial int Attachment { get; set; }
	[NetworkVar("m_flLifetime")]
	public partial float Lifetime { get; set; }
}
