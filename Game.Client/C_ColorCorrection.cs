using Game.Shared;

using Source;
using Source.Common;
using System.Net;

using System.Security.Cryptography.X509Certificates;
using Source.Common.MaterialSystem;
using System.Numerics;
namespace Game.Client;

[NetworkName("CColorCorrection")]
public partial class C_ColorCorrection : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Origin)) { Encoding = NetEncoding.Vector, Bits = 0, Flags = PropFlags.NoScale },
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


	[NetworkVar("m_minFalloff")]
	public float MinFalloff;
	[NetworkVar("m_maxFalloff")]
	public float MaxFalloff;
	[NetworkVar("m_flCurWeight")]
	public float CurWeight;
	[NetworkVar("m_flMaxWeight")]
	public float MaxWeight;
	[NetworkVar("m_flFadeInDuration")]
	public float FadeInDuration;
	[NetworkVar("m_flFadeOutDuration")]
	public float FadeOutDuration;
	[NetworkVar("m_netLookupFilename")]
	public InlineArrayMaxPath<char> NetLookupFilename;
	[NetworkVar("m_bEnabled")]
	public bool Enabled;
	[NetworkVar("m_bClientSide")]
	public bool ClientSide;
	[NetworkVar("m_bExclusive")]
	public bool Exclusive;
}

