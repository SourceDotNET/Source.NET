using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_flare")]
[NetworkName("CFlare")]
public partial class Flare : BaseCombatCharacter
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(TimeBurnOut)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Scale)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Light),
		nameof(Smoke),
		nameof(PropFlare),
	];

	[NetworkVar("m_flTimeBurnOut")]
	public partial float TimeBurnOut { get; set; }
	[NetworkVar("m_flScale")]
	public partial float Scale { get; set; }
	[NetworkVar("m_bLight")]
	public partial bool Light { get; set; }
	[NetworkVar("m_bSmoke")]
	public partial bool Smoke { get; set; }
	[NetworkVar("m_bPropFlare")]
	public partial bool PropFlare { get; set; }
}
