#if CLIENT_DLL || GAME_DLL
using Source.Common;

using System.Numerics;
namespace Game.Shared;
using FIELD = Source.FIELD<Sprite>;
[LinkEntityToClass("env_sprite")]
#if !CLIENT_DLL
[LinkEntityToClass("env_glow")]
[LinkEntityToClass("env_sprite_clientside")]
#endif
[NetworkName("CSprite")]
public class Sprite : BaseEntity
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_Sprite = new(DT_BaseEntity, [
#if CLIENT_DLL
		RecvPropEHandle(FIELD.OF(nameof(AttachedToEntity))),
		RecvPropInt(FIELD.OF(nameof(Attachment))),
		RecvPropFloat(FIELD.OF(nameof(ScaleTime))),
		// todo: RecvProxy_SpriteScale
		RecvPropFloat(FIELD.OF(nameof(SpriteScale))),
		RecvPropFloat(FIELD.OF(nameof(GlowProxySize))),
		RecvPropFloat(FIELD.OF(nameof(HDRColorScale))),
		RecvPropFloat(FIELD.OF(nameof(SpriteFramerate))),
		RecvPropFloat(FIELD.OF(nameof(Frame))),
		RecvPropFloat(FIELD.OF(nameof(BrightnessTime))),
		RecvPropInt(FIELD.OF(nameof(Brightness))),
		RecvPropBool(FIELD.OF(nameof(WorldSpaceScale)))
#else
		SendPropEHandle(FIELD.OF(nameof(AttachedToEntity))),
		SendPropInt(FIELD.OF(nameof(Attachment)), 8),
		SendPropFloat(FIELD.OF(nameof(ScaleTime)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(SpriteScale)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(GlowProxySize)), 6, PropFlags.RoundUp, 0.0f, 64.0f),
		SendPropFloat(FIELD.OF(nameof(HDRColorScale)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(SpriteFramerate)), 8, PropFlags.RoundUp, 0, 60.0f),
		SendPropFloat(FIELD.OF(nameof(Frame)), 20, PropFlags.RoundDown, 0, 256),
		SendPropFloat(FIELD.OF(nameof(BrightnessTime)), 0, PropFlags.NoScale, 0, 0),
		SendPropInt(FIELD.OF(nameof(Brightness)), 8, PropFlags.Unsigned),
		SendPropBool(FIELD.OF(nameof(WorldSpaceScale)))
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_Sprite);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_Sprite);
#endif
	[NetworkName("m_hAttachedToEntity")]
	public EHANDLE AttachedToEntity = new();
	[NetworkName("m_nAttachment")]
	public int Attachment;
	[NetworkName("m_flScaleTime")]
	public TimeUnit_t ScaleTime;
	[NetworkName("m_flSpriteScale")]
	public float SpriteScale;
	[NetworkName("m_flGlowProxySize")]
	public float GlowProxySize;
	[NetworkName("m_flHDRColorScale")]
	public float HDRColorScale;
	[NetworkName("m_flSpriteFramerate")]
	public TimeUnit_t SpriteFramerate;
	[NetworkName("m_flFrame")]
	public TimeUnit_t Frame;
	[NetworkName("m_flBrightnessTime")]
	public TimeUnit_t BrightnessTime;
	[NetworkName("m_nBrightness")]
	public int Brightness;
	[NetworkName("m_bWorldSpaceScale")]
	public bool WorldSpaceScale;
}
[LinkEntityToClass("env_sprite_oriented")]
[NetworkName("CSpriteOriented")]
public class SpriteOriented : Sprite
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_SpriteOriented = new(DT_Sprite, [
#if CLIENT_DLL

#else

#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_SpriteOriented);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_SpriteOriented);
#endif
}
#endif
