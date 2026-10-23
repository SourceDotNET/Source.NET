using Game.Shared;

using Game.Shared;

using Source;
using Source.Common;

namespace Game.Client;

[NetworkName("CFuncSmokeVolume")]
public partial class C_FuncSmokeVolume : C_BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Color1)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned, Proxy = typeof(Color32Proxy) },
		new(nameof(Color2)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned, Proxy = typeof(Color32Proxy) },
		new(nameof(MaterialName)) { Encoding = NetEncoding.String },
		new(nameof(ParticleDrawWidth)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ParticleSpacingDistance)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DensityRampSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(RotationSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MovementSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Density)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SpawnFlags)) { Encoding = NetEncoding.Int, Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_Color1")]
	public Color Color1;
	[NetworkVar("m_Color2")]
	public Color Color2;
	[NetworkVar("m_MaterialName")]
	public InlineArray255<char> MaterialName;
	[NetworkVar("m_ParticleDrawWidth")]
	public float ParticleDrawWidth;
	[NetworkVar("m_ParticleSpacingDistance")]
	public float ParticleSpacingDistance;
	[NetworkVar("m_DensityRampSpeed")]
	public float DensityRampSpeed;
	[NetworkVar("m_RotationSpeed")]
	public float RotationSpeed;
	[NetworkVar("m_MovementSpeed")]
	public float MovementSpeed;
	[NetworkVar("m_Density")]
	public float Density;
}

