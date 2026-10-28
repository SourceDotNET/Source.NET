using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_entity_dissolver")]
[NetworkName("CEntityDissolve")]
public partial class EntityDissolve : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeInStart)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeInLength)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeOutModelStart)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeOutModelLength)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeOutStart)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeOutLength)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DissolveType)) { Bits = 3, Flags = PropFlags.Unsigned },
		new(nameof(DissolverOrigin)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Magnitude)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_flStartTime")]
	public partial float StartTime { get; set; }
	[NetworkVar("m_flFadeInStart")]
	public partial float FadeInStart { get; set; }
	[NetworkVar("m_flFadeInLength")]
	public partial float FadeInLength { get; set; }
	[NetworkVar("m_flFadeOutModelStart")]
	public partial float FadeOutModelStart { get; set; }
	[NetworkVar("m_flFadeOutModelLength")]
	public partial float FadeOutModelLength { get; set; }
	[NetworkVar("m_flFadeOutStart")]
	public partial float FadeOutStart { get; set; }
	[NetworkVar("m_flFadeOutLength")]
	public partial float FadeOutLength { get; set; }
	[NetworkVar("m_nDissolveType")]
	public partial int DissolveType { get; set; }
	[NetworkVar("m_vDissolverOrigin")]
	public partial Vector3 DissolverOrigin { get; set; }
	[NetworkVar("m_nMagnitude")]
	public partial int Magnitude { get; set; }
}
