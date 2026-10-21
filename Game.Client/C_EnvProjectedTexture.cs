using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CEnvProjectedTexture")]
public partial class C_EnvProjectedTexture : C_BaseEntity
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

	[NetworkVar("m_hTargetEntity")]
	public EHANDLE HTargetEntity = new();
	[NetworkVar("m_bState")]
	public bool State;
	[NetworkVar("m_flLightFOV")]
	public float LightFOV;
	[NetworkVar("m_bEnableShadows")]
	public bool EnableShadows;
	[NetworkVar("m_bLightOnlyTarget")]
	public bool LightOnlyTarget;
	[NetworkVar("m_bLightWorld")]
	public bool LightWorld;
	[NetworkVar("m_bCameraSpace")]
	public bool CameraSpace;
	[NetworkVar("m_LinearFloatLightColor")]
	public Vector3 LinearFloatLightColor;
	[NetworkVar("m_SpotlightTextureName")]
	public InlineArrayMaxPath<char> SpotlightTextureName;
	[NetworkVar("m_nSpotlightTextureFrame")]
	public int SpotlightTextureFrame;
	[NetworkVar("m_flNearZ")]
	public float NearZ;
	[NetworkVar("m_flFarZ")]
	public float FarZ;
	[NetworkVar("m_nShadowQuality")]
	public bool ShadowQuality;
	[NetworkVar("m_iStyle")]
	public int Style;
}
