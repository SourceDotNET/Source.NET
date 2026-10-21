using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CSmokeStack")]
public partial class C_SmokeStack : C_BaseParticleEntity
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
		new(nameof(Twist)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(RollSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MaterialModel)) { Bits = 16, Encoding = NetEncoding.IntWithMinusOneFlag },
		new(nameof(AmbientLight.Pos)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(AmbientLight.Color)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(AmbientLight.Intensity)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DirLight.Pos)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DirLight.Color)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DirLight.Intensity)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Wind)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_SpreadSpeed")]
	public float SpreadSpeed;
	[NetworkVar("m_Speed")]
	public new float Speed;
	[NetworkVar("m_StartSize")]
	public float StartSize;
	[NetworkVar("m_EndSize")]
	public float EndSize;
	[NetworkVar("m_Rate")]
	public float Rate;
	[NetworkVar("m_JetLength")]
	public float JetLength;
	[NetworkVar("m_bEmit")]
	public bool Emit;
	[NetworkVar("m_flBaseSpread")]
	public float BaseSpread;
	[NetworkVar("m_flRollSpeed")]
	public float RollSpeed;
	[NetworkVar("m_AmbientLight")]
	public ParticleLightInfo AmbientLight;
	[NetworkVar("m_DirLight")]
	public ParticleLightInfo DirLight;
	[NetworkVar("m_vWind")]
	public Vector3 Wind;
	[NetworkVar("m_flTwist")]
	public float Twist;
	[NetworkVar("m_iMaterialModel")]
	public int MaterialModel;
}
