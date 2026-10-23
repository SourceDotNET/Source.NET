using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_projectedtexture")]
[NetworkName("CEnvProjectedTexture")]
public partial class EnvProjectedTexture : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(HTargetEntity),
		nameof(State),
		new(nameof(LightFOV)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(EnableShadows),
		nameof(LightOnlyTarget),
		nameof(LightWorld),
		nameof(CameraSpace),
		new(nameof(LinearFloatLightColor)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SpotlightTextureName)) { Encoding = NetEncoding.String },
		new(nameof(SpotlightTextureFrame)) { Bits = 14 },
		new(nameof(NearZ)) { Bits = 16, Flags = PropFlags.RoundDown, Low = 0.0f, High = 500.0f },
		new(nameof(FarZ)) { Bits = 18, Flags = PropFlags.RoundDown, Low = 0.0f, High = 56756.0f },
		nameof(ShadowQuality),
		new(nameof(Style)) { Bits = 8 },
	];

	public EnvProjectedTexture() {
		HTargetEntity = new();
	}


	[NetworkVar("m_hTargetEntity")]
	public partial EHANDLE HTargetEntity { get; set; }
	[NetworkVar("m_bState")]
	public partial bool State { get; set; }
	[NetworkVar("m_flLightFOV")]
	public partial float LightFOV { get; set; }
	[NetworkVar("m_bEnableShadows")]
	public partial bool EnableShadows { get; set; }
	[NetworkVar("m_bLightOnlyTarget")]
	public partial bool LightOnlyTarget { get; set; }
	[NetworkVar("m_bLightWorld")]
	public partial bool LightWorld { get; set; }
	[NetworkVar("m_bCameraSpace")]
	public partial bool CameraSpace { get; set; }
	[NetworkVar("m_LinearFloatLightColor")]
	public partial Vector3 LinearFloatLightColor { get; set; }
	[NetworkVar("m_SpotlightTextureName")]
	public partial ref readonly InlineArrayMaxPath<char> SpotlightTextureName { get; }
	[NetworkVar("m_nSpotlightTextureFrame")]
	public partial int SpotlightTextureFrame { get; set; }
	[NetworkVar("m_flNearZ")]
	public partial float NearZ { get; set; }
	[NetworkVar("m_flFarZ")]
	public partial float FarZ { get; set; }
	[NetworkVar("m_nShadowQuality")]
	public partial bool ShadowQuality { get; set; }
	[NetworkVar("m_iStyle")]
	public partial int Style { get; set; }
}
