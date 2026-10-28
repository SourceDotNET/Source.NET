using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_particle_performance_monitor")]
[NetworkName("CParticlePerformanceMonitor")]
public partial class ParticlePerformanceMonitor : PointEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(DisplayPerf),
		nameof(MeasurePerf),
	];

	[NetworkVar("m_bDisplayPerf")]
	public partial bool DisplayPerf { get; set; }
	[NetworkVar("m_bMeasurePerf")]
	public partial bool MeasurePerf { get; set; }
}
