using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("point_tesla")]
[NetworkName("CTesla")]
public partial class Tesla : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SoundName)) { Encoding = NetEncoding.String },
		new(nameof(SpriteName)) { Encoding = NetEncoding.String },
	];

	[NetworkVar("m_SoundName")]
	public partial ref readonly InlineArray64<char> SoundName { get; }
	[NetworkVar("m_iszSpriteName")]
	public partial ref readonly InlineArray256<char> SpriteName { get; }
}
