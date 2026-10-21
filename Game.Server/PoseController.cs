using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("point_posecontroller")]
[NetworkName("CPoseController")]
public partial class PoseController : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(HProps),
		new(nameof(ChPoseIndex)) { Bits = 5, Flags = PropFlags.Unsigned },
		nameof(PoseValueParity),
		new(nameof(PoseValue)) { Bits = 11, Low = 0.0f, High = 1.0f },
		new(nameof(InterpolationTime)) { Bits = 11, Low = 0.0f, High = 10.0f },
		nameof(InterpolationWrap),
		new(nameof(CycleFrequency)) { Bits = 11, Low = -10.0f, High = 10.0f },
		new(nameof(FModType)) { Bits = 3, Flags = PropFlags.Unsigned },
		new(nameof(FModTimeOffset)) { Bits = 11, Low = -1.0f, High = 1.0f },
		new(nameof(FModRate)) { Bits = 11, Low = -10.0f, High = 10.0f },
		new(nameof(FModAmplitude)) { Bits = 11, Low = 0.0f, High = 10.0f },
	];

	public const int MAX_POSE_CONTROLLED_PROPS = 4;
	[NetworkVar("m_hProps")]
	public partial ref readonly InlineArray4<EHANDLE> HProps { get; }
	[NetworkVar("m_chPoseIndex")]
	public partial ref readonly InlineArray4<byte> ChPoseIndex { get; }
	[NetworkVar("m_bPoseValueParity")]
	public partial bool PoseValueParity { get; set; }
	[NetworkVar("m_fPoseValue")]
	public partial float PoseValue { get; set; }
	[NetworkVar("m_fInterpolationTime")]
	public partial float InterpolationTime { get; set; }
	[NetworkVar("m_bInterpolationWrap")]
	public partial bool InterpolationWrap { get; set; }
	[NetworkVar("m_fCycleFrequency")]
	public partial float CycleFrequency { get; set; }
	[NetworkVar("m_nFModType")]
	public partial int FModType { get; set; }
	[NetworkVar("m_fFModTimeOffset")]
	public partial float FModTimeOffset { get; set; }
	[NetworkVar("m_fFModRate")]
	public partial float FModRate { get; set; }
	[NetworkVar("m_fFModAmplitude")]
	public partial float FModAmplitude { get; set; }
}
