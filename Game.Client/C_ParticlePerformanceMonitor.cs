using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CParticlePerformanceMonitor")]
public partial class C_ParticlePerformanceMonitor : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(MeasurePerf),
		nameof(DisplayPerf),
	];

	[NetworkVar("m_bMeasurePerf")]
	public bool MeasurePerf;
	[NetworkVar("m_bDisplayPerf")]
	public bool DisplayPerf;
}
