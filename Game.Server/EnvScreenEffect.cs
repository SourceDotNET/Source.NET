using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_screeneffect")]
[NetworkName("CEnvScreenEffect")]
public partial class EnvScreenEffect : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Duration)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Type)) { Bits = 12, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_flDuration")]
	public partial float Duration { get; set; }
	[NetworkVar("m_nType")]
	public partial int Type { get; set; }
}
