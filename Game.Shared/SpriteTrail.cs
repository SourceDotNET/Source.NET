#if CLIENT_DLL || GAME_DLL
using Source.Common;

using System.Numerics;
namespace Game.Shared;
[LinkEntityToClass("env_spritetrail")]
[NetworkName("CSpriteTrail")]
public partial class SpriteTrail : Sprite
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(LifeTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(StartWidth)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(EndWidth)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(StartWidthVariance)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(TextureRes)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeLength)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SkyboxOrigin)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SkyboxScale)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flLifeTime")]
	public partial TimeUnit_t LifeTime { get; set; }
	[NetworkVar("m_flStartWidth")]
	public partial float StartWidth { get; set; }
	[NetworkVar("m_flEndWidth")]
	public partial float EndWidth { get; set; }
	[NetworkVar("m_flStartWidthVariance")]
	public partial float StartWidthVariance { get; set; }
	[NetworkVar("m_flTextureRes")]
	public partial float TextureRes { get; set; }
	[NetworkVar("m_flMinFadeLength")]
	public partial float FadeLength { get; set; }
	[NetworkVar("m_vecSkyboxOrigin")]
	public partial Vector3 SkyboxOrigin { get; set; }
	[NetworkVar("m_flSkyboxScale")]
	public partial float SkyboxScale { get; set; }
}
#endif
