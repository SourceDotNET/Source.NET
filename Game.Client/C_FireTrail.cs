using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CFireTrail")]
public partial class C_FireTrail : C_BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Attachment)) { Bits = 32 },
		new(nameof(Lifetime)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_nAttachment")]
	public int Attachment;
	[NetworkVar("m_flLifetime")]
	public float Lifetime;
}
