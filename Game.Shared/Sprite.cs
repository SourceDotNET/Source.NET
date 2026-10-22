#if CLIENT_DLL || GAME_DLL
using Source.Common;

using System.Numerics;
namespace Game.Shared;
[LinkEntityToClass("env_sprite")]
#if !CLIENT_DLL
[LinkEntityToClass("env_glow")]
[LinkEntityToClass("env_sprite_clientside")]
#endif
[NetworkName("CSprite")]
public partial class Sprite : BaseEntity
{
	[NetworkTable(Side = NetworkSide.Server)]
	public static readonly NetworkProp[] ServerProps = [
		nameof(AttachedToEntity),
		new(nameof(Attachment)) { Bits = 8 },
		new(nameof(ScaleTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SpriteScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(GlowProxySize)) { Bits = 6, Flags = PropFlags.RoundUp, Low = 0.0f, High = 64.0f },
		new(nameof(HDRColorScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SpriteFramerate)) { Encoding = NetEncoding.Float, Bits = 8, Flags = PropFlags.RoundUp, Low = 0, High = 60.0f },
		new(nameof(Frame)) { Encoding = NetEncoding.Float, Bits = 20, Flags = PropFlags.RoundDown, Low = 0, High = 256 },
		new(nameof(BrightnessTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale, Low = 0, High = 0 },
		new(nameof(Brightness)) { Bits = 8, Flags = PropFlags.Unsigned },
		nameof(WorldSpaceScale),
	];

	[NetworkTable(Side = NetworkSide.Client)]
	public static readonly NetworkProp[] ClientProps = [
		nameof(AttachedToEntity),
		new(nameof(Attachment)) { Bits = 8 },
		new(nameof(ScaleTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SpriteScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SpriteFramerate)) { Encoding = NetEncoding.Float, Bits = 8, Flags = PropFlags.RoundUp, Low = 0, High = 60.0f },
		new(nameof(GlowProxySize)) { Bits = 6, Flags = PropFlags.RoundUp, Low = 0.0f, High = 64.0f },
		new(nameof(HDRColorScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Frame)) { Encoding = NetEncoding.Float, Bits = 20, Flags = PropFlags.RoundDown, Low = 0, High = 256 },
		new(nameof(BrightnessTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale, Low = 0, High = 0 },
		new(nameof(Brightness)) { Bits = 8, Flags = PropFlags.Unsigned },
		nameof(WorldSpaceScale),
	];

	public Sprite() {
		AttachedToEntity = new();
	}

	[NetworkVar("m_hAttachedToEntity")]
	public partial EHANDLE AttachedToEntity { get; set; }
	[NetworkVar("m_nAttachment")]
	public partial int Attachment { get; set; }
	[NetworkVar("m_flScaleTime")]
	public partial float ScaleTime { get; set; }
	// todo: RecvProxy_SpriteScale
	[NetworkVar("m_flSpriteScale")]
	public partial float SpriteScale { get; set; }
	[NetworkVar("m_flSpriteFramerate")]
	public partial float SpriteFramerate { get; set; }
	[NetworkVar("m_flGlowProxySize")]
	public partial float GlowProxySize { get; set; }
	[NetworkVar("m_flHDRColorScale")]
	public partial float HDRColorScale { get; set; }
	[NetworkVar("m_flFrame")]
	public partial float Frame { get; set; }
	[NetworkVar("m_flBrightnessTime")]
	public partial float BrightnessTime { get; set; }
	[NetworkVar("m_nBrightness")]
	public partial int Brightness { get; set; }
	[NetworkVar("m_bWorldSpaceScale")]
	public partial bool WorldSpaceScale { get; set; }
}
[LinkEntityToClass("env_sprite_oriented")]
[NetworkName("CSpriteOriented")]
public partial class SpriteOriented : Sprite
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
#endif
