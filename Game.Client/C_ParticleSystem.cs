using Game.Shared;

using Source;
using Source.Common;

namespace Game.Client;

[NetworkName("CParticleSystem", NoBase = true)]
public partial class C_ParticleSystem : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Origin)) { Encoding = NetEncoding.Vector, Bits = 0, Flags = PropFlags.Coord | PropFlags.ChangesOften },
		new(nameof(OwnerEntity)) { Encoding = NetEncoding.EHandle },
		new(nameof(NetworkMoveParent)) { Encoding = NetEncoding.Int, Proxy = typeof(MoveParentProxy) },
		new(nameof(ParentAttachment)) { Encoding = NetEncoding.Int, Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Rotation)) { Encoding = NetEncoding.Vector, Bits = 13, Flags = PropFlags.RoundDown | PropFlags.ChangesOften },
		new(nameof(EffectIndex)) { Bits = 12, Flags = PropFlags.Unsigned },
		nameof(Active),
		new(nameof(StartTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		nameof(ControlPointEnts),
		new(nameof(ControlPointParents)) { Bits = 3, Flags = PropFlags.Unsigned },
		nameof(WeatherEffect),
	];

	[NetworkVar("m_iEffectIndex")]
	public int EffectIndex;
	[NetworkVar("m_bActive")]
	public bool Active;
	[NetworkVar("m_flStartTime")]
	public TimeUnit_t StartTime;
	[NetworkVar("m_hControlPointEnts")]
	public InlineArrayNewMaxControlPoints<EHANDLE> ControlPointEnts = new();

	[NetworkVar("m_iControlPointParents")]
	public InlineArrayNewMaxControlPoints<byte> ControlPointParents = new();
	[NetworkVar("m_bWeatherEffect")]
	public bool WeatherEffect;
}

