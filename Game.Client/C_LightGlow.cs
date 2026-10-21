using Game.Shared;

using Source;
using Source.Common;

namespace Game.Client;

[NetworkName("CLightGlow", NoBase = true)]
public partial class C_LightGlow : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(RenderColor)) { Encoding = NetEncoding.Int, Proxy = typeof(Color32Proxy) },
		nameof(HorizontalSize),
		nameof(VerticalSize),
		nameof(MinDist),
		nameof(MaxDist),
		nameof(OuterMaxDist),
		nameof(SpawnFlags),
		nameof(Origin),
		nameof(Rotation),
		new(nameof(NetworkMoveParent)) { Encoding = NetEncoding.Int, Proxy = typeof(MoveParentProxy) },
		nameof(GlowProxySize),
		// todo: RecvProxy_HDRColorScale
		nameof(HDRColorScale),
	];

	[NetworkVar("m_clrRender")]
	public Color RenderColor;
	[NetworkVar("m_nHorizontalSize")]
	public int HorizontalSize;
	[NetworkVar("m_nVerticalSize")]
	public int VerticalSize;
	[NetworkVar("m_nMinDist")]
	public int MinDist;
	[NetworkVar("m_nMaxDist")]
	public int MaxDist;
	[NetworkVar("m_nOuterMaxDist")]
	public int OuterMaxDist;
	[NetworkVar("m_flGlowProxySize")]
	public float GlowProxySize;
	[NetworkVar("HDRColorScale")]
	public float HDRColorScale;
}

