using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CPrecipitation")]
public partial class C_Precipitation : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(PrecipType)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(ParticleNameClose)) { Encoding = NetEncoding.String },
		new(nameof(ParticleNameInner)) { Encoding = NetEncoding.String },
		new(nameof(ParticleNameOuter)) { Encoding = NetEncoding.String },
		new(nameof(ParticleDist)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_nPrecipType")]
	public int PrecipType;
	[NetworkVar("m_sParticleNameClose")]
	public InlineArray512<char> ParticleNameClose;
	[NetworkVar("m_sParticleNameInner")]
	public InlineArray512<char> ParticleNameInner;
	[NetworkVar("m_sParticleNameOuter")]
	public InlineArray512<char> ParticleNameOuter;
	[NetworkVar("m_flParticleDist")]
	public float ParticleDist;
}
