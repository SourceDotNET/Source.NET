#if CLIENT_DLL || GAME_DLL
using Source.Common;

using System.Numerics;
namespace Game.Shared;
using FIELD = Source.FIELD<SpriteTrail>;
[LinkEntityToClass("env_spritetrail")]
[NetworkName("CSpriteTrail")]
public class SpriteTrail : Sprite
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_SpriteTrail = new(DT_Sprite, [
#if CLIENT_DLL
		RecvPropFloat(FIELD.OF(nameof(LifeTime))),
		RecvPropFloat(FIELD.OF(nameof(StartWidth))),
		RecvPropFloat(FIELD.OF(nameof(EndWidth))),
		RecvPropFloat(FIELD.OF(nameof(StartWidthVariance))),
		RecvPropFloat(FIELD.OF(nameof(TextureRes))),
		RecvPropFloat(FIELD.OF(nameof(FadeLength))),
		RecvPropVector(FIELD.OF(nameof(SkyboxOrigin))),
		RecvPropFloat(FIELD.OF(nameof(SkyboxScale))),
#else
		SendPropFloat(FIELD.OF(nameof(LifeTime)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(StartWidth)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(EndWidth)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(StartWidthVariance)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(TextureRes)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(FadeLength)), 0, PropFlags.NoScale),
		SendPropVector(FIELD.OF(nameof(SkyboxOrigin)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(SkyboxScale)), 0, PropFlags.NoScale),
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_SpriteTrail);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_SpriteTrail);
#endif

	[NetworkName("m_flLifeTime")]
	public TimeUnit_t LifeTime;
	[NetworkName("m_flStartWidth")]
	public float StartWidth;
	[NetworkName("m_flEndWidth")]
	public float EndWidth;
	[NetworkName("m_flStartWidthVariance")]
	public float StartWidthVariance;
	[NetworkName("m_flTextureRes")]
	public float TextureRes;
	[NetworkName("m_flMinFadeLength")]
	public float FadeLength;
	[NetworkName("m_vecSkyboxOrigin")]
	public Vector3 SkyboxOrigin;
	[NetworkName("m_flSkyboxScale")]
	public float SkyboxScale;
}
#endif
