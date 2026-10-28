using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("beam_spotlight")]
[NetworkName("CBeamSpotlight")]
public partial class BeamSpotlight : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(HaloIndex)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(SpotlightOn)) { Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(HasDynamicLight)) { Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(SpotlightMaxLength)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SpotlightGoalWidth)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(HDRColorScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(RotationSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(RotationAxis)) { Bits = 2, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nHaloIndex")]
	public partial int HaloIndex { get; set; }
	[NetworkVar("m_bSpotlightOn")]
	public partial int SpotlightOn { get; set; }
	[NetworkVar("m_bHasDynamicLight")]
	public partial int HasDynamicLight { get; set; }
	[NetworkVar("m_flSpotlightMaxLength")]
	public partial float SpotlightMaxLength { get; set; }
	[NetworkVar("m_flSpotlightGoalWidth")]
	public partial float SpotlightGoalWidth { get; set; }
	[NetworkVar("m_flHDRColorScale")]
	public partial float HDRColorScale { get; set; }
	[NetworkVar("m_flRotationSpeed")]
	public partial float RotationSpeed { get; set; }
	[NetworkVar("m_nRotationAxis")]
	public partial int RotationAxis { get; set; }
}
