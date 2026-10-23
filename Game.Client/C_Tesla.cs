using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTesla")]
public partial class C_Tesla : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SoundName)) { Encoding = NetEncoding.String },
		new(nameof(SpriteName)) { Encoding = NetEncoding.String },
	];

	[NetworkVar("m_SoundName")]
	public InlineArray64<char> SoundName;
	[NetworkVar("m_iszSpriteName")]
	public InlineArray256<char> SpriteName;
}
