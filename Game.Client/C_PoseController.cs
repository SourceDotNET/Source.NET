using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CPoseController")]
public partial class C_PoseController : C_BaseEntity
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
	public InlineArray4<EHANDLE> HProps;
	[NetworkVar("m_chPoseIndex")]
	public InlineArray4<byte> ChPoseIndex;
	[NetworkVar("m_bPoseValueParity")]
	public bool PoseValueParity;
	[NetworkVar("m_fPoseValue")]
	public float PoseValue;
	[NetworkVar("m_fInterpolationTime")]
	public float InterpolationTime;
	[NetworkVar("m_bInterpolationWrap")]
	public bool InterpolationWrap;
	[NetworkVar("m_fCycleFrequency")]
	public float CycleFrequency;
	[NetworkVar("m_nFModType")]
	public int FModType;
	[NetworkVar("m_fFModTimeOffset")]
	public float FModTimeOffset;
	[NetworkVar("m_fFModRate")]
	public float FModRate;
	[NetworkVar("m_fFModAmplitude")]
	public float FModAmplitude;
}
