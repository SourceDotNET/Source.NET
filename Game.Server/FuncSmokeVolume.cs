using Source.Common;
using Source;

using Game.Shared;
using System.Numerics;
using Source.Common.MaterialSystem;
using System;

namespace Game.Server;


[LinkEntityToClass("func_smokevolume")]
[NetworkName("CFuncSmokeVolume")]
public partial class FuncSmokeVolume : BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Color1)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Color2)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
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
	public partial Color Color1 { get; set; }
	[NetworkVar("m_Color2")]
	public partial Color Color2 { get; set; }
	[NetworkVar("m_MaterialName")]
	public partial ref readonly InlineArray255<char> MaterialName { get; }
	[NetworkVar("m_ParticleDrawWidth")]
	public partial float ParticleDrawWidth { get; set; }
	[NetworkVar("m_ParticleSpacingDistance")]
	public partial float ParticleSpacingDistance { get; set; }
	[NetworkVar("m_DensityRampSpeed")]
	public partial float DensityRampSpeed { get; set; }
	[NetworkVar("m_RotationSpeed")]
	public partial float RotationSpeed { get; set; }
	[NetworkVar("m_MovementSpeed")]
	public partial float MovementSpeed { get; set; }
	[NetworkVar("m_Density")]
	public partial float Density { get; set; }
}
