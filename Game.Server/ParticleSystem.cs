using Source.Common;
using Source;

using Game.Shared;
using System.Runtime.CompilerServices;

namespace Game.Server;


[LinkEntityToClass("info_particle_system")]
[NetworkName("CParticleSystem", NoBase = true)]
public partial class ParticleSystem : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Origin)) { Encoding = NetEncoding.Vector, Bits = 0, Flags = PropFlags.Coord | PropFlags.ChangesOften, Proxy = typeof(OriginProxy) },
		new(nameof(OwnerEntity)) { Encoding = NetEncoding.EHandle },
		new(nameof(MoveParent)) { Encoding = NetEncoding.EHandle },
		new(nameof(ParentAttachment)) { Encoding = NetEncoding.Int, Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Rotation)) { Encoding = NetEncoding.QAngles, Bits = 13, Flags = PropFlags.RoundDown | PropFlags.ChangesOften, Proxy = typeof(AnglesProxy) },
		new(nameof(EffectIndex)) { Bits = 12, Flags = PropFlags.Unsigned },
		nameof(Active),
		new(nameof(StartTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		nameof(ControlPointEnts),
		new(nameof(ControlPointParents)) { Bits = 3, Flags = PropFlags.Unsigned },
		nameof(WeatherEffect),
	];

	[NetworkVar("m_iEffectIndex")]
	public partial int EffectIndex { get; set; }
	[NetworkVar("m_bActive")]
	public partial bool Active { get; set; }
	[NetworkVar("m_flStartTime")]
	public partial TimeUnit_t StartTime { get; set; }
	[NetworkVar("m_hControlPointEnts")]
	public partial ref readonly InlineArrayNewMaxControlPoints<EHANDLE> ControlPointEnts { get; }

	[NetworkVar("m_iControlPointParents")]
	public partial ref readonly InlineArrayNewMaxControlPoints<byte> ControlPointParents { get; }
	[NetworkVar("m_bWeatherEffect")]
	public partial bool WeatherEffect { get; set; }

	public ParticleSystem() {
		ControlPointEntsForModify() = new();
		ControlPointParentsForModify() = new();
	}
}
