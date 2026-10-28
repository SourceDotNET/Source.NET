using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("func_precipitation")]
[NetworkName("CPrecipitation")]
public partial class Precipitation : BaseEntity
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
	public partial int PrecipType { get; set; }
	[NetworkVar("m_sParticleNameClose")]
	public partial ref readonly InlineArray512<char> ParticleNameClose { get; }
	[NetworkVar("m_sParticleNameInner")]
	public partial ref readonly InlineArray512<char> ParticleNameInner { get; }
	[NetworkVar("m_sParticleNameOuter")]
	public partial ref readonly InlineArray512<char> ParticleNameOuter { get; }
	[NetworkVar("m_flParticleDist")]
	public partial float ParticleDist { get; set; }
}
