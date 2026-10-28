using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;

public struct SmokeStackLightInfo{
	[NetworkVar("m_vPos")]
	public Vector3 Pos;
	[NetworkVar("m_vColor")]
	public Vector3 Color;
	[NetworkVar("m_flIntensity")]
	public float Intensity;
}

[LinkEntityToClass("env_smokestack")]
[NetworkName("CSmokeStack")]
public partial class SmokeStack : BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SpreadSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Speed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(StartSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(EndSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Rate)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(JetLength)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Emit),
		new(nameof(BaseSpread)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(RollSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DirLight.Pos)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DirLight.Color)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DirLight.Intensity)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(AmbientLight.Pos)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(AmbientLight.Color)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(AmbientLight.Intensity)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Wind)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Twist)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MaterialModel)) { Bits = 16, Encoding = NetEncoding.IntWithMinusOneFlag },
	];

	[NetworkVar("m_SpreadSpeed")]
	public partial float SpreadSpeed { get; set; }
	[NetworkVar("m_Speed")]
	public new partial float Speed { get; set; }
	[NetworkVar("m_StartSize")]
	public partial float StartSize { get; set; }
	[NetworkVar("m_EndSize")]
	public partial float EndSize { get; set; }
	[NetworkVar("m_Rate")]
	public partial float Rate { get; set; }
	[NetworkVar("m_JetLength")]
	public partial float JetLength { get; set; }
	[NetworkVar("m_bEmit")]
	public partial bool Emit { get; set; }
	[NetworkVar("m_flBaseSpread")]
	public partial float BaseSpread { get; set; }
	[NetworkVar("m_flRollSpeed")]
	public partial float RollSpeed { get; set; }

	[NetworkVar("m_AmbientLight")]
	public partial ref readonly SmokeStackLightInfo AmbientLight { get; }
	[NetworkVar("m_DirLight")]
	public partial ref readonly SmokeStackLightInfo DirLight { get; }

	[NetworkVar("m_vWind")]
	public partial Vector3 Wind { get; set; }
	[NetworkVar("m_flTwist")]
	public partial float Twist { get; set; }
	[NetworkVar("m_iMaterialModel")]
	public partial int MaterialModel { get; set; }
}
