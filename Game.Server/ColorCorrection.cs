using Source.Common;
using Source;

using Game.Shared;
using System.Numerics;
using Source.Common.MaterialSystem;

namespace Game.Server;


[LinkEntityToClass("color_correction")]
[NetworkName("CColorCorrection", NoBase = true)]
public partial class ColorCorrection : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Origin)) { Encoding = NetEncoding.Vector, Bits = 0, Flags = PropFlags.NoScale, Proxy = typeof(OriginProxy) },
		new(nameof(MinFalloff)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MaxFalloff)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(CurWeight)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MaxWeight)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeInDuration)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeOutDuration)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(NetLookupFilename)) { Encoding = NetEncoding.String },
		nameof(Enabled),
		nameof(ClientSide),
		nameof(Exclusive),
	];

	[NetworkVar("m_MinFalloff")]
	public partial float MinFalloff { get; set; }
	[NetworkVar("m_MaxFalloff")]
	public partial float MaxFalloff { get; set; }
	[NetworkVar("m_flCurWeight")]
	public partial float CurWeight { get; set; }
	[NetworkVar("m_flMaxWeight")]
	public partial float MaxWeight { get; set; }
	[NetworkVar("m_flFadeInDuration")]
	public partial float FadeInDuration { get; set; }
	[NetworkVar("m_flFadeOutDuration")]
	public partial float FadeOutDuration { get; set; }
	[NetworkVar("m_netlookupFilename")]
	public partial ref readonly InlineArrayMaxPath<char> NetLookupFilename { get; }
	[NetworkVar("m_bEnabled")]
	public partial bool Enabled { get; set; }
	[NetworkVar("m_bClientSide")]
	public partial bool ClientSide { get; set; }
	[NetworkVar("m_bExclusive")]
	public partial bool Exclusive { get; set; }
}
