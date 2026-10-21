using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[LinkEntityToClass("beam_spotlight")]
[NetworkName("CBeamSpotlight")]
public partial class C_BeamSpotlight : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(HaloIndex)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(SpotlightOn)) { Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(HasDynamicLight)) { Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(SpotlightMaxLength)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SpotlightGoalWidth)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(HDRColorScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(RotationAxis)) { Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(RotationSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_nHaloIndex")]
	public int HaloIndex;
	[NetworkVar("m_bSpotlightOn")]
	public int SpotlightOn;
	[NetworkVar("m_bHasDynamicLight")]
	public int HasDynamicLight;
	[NetworkVar("m_flSpotlightMaxLength")]
	public float SpotlightMaxLength;
	[NetworkVar("m_flSpotlightGoalWidth")]
	public float SpotlightGoalWidth;
	[NetworkVar("m_flHDRColorScale")]
	public float HDRColorScale;
	[NetworkVar("m_nRotationAxis")]
	public int RotationAxis;
	[NetworkVar("m_flRotationSpeed")]
	public float RotationSpeed;
}
