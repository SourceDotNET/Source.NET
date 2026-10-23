using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CRagdollProp", Table = "DT_Ragdoll")]
public partial class C_RagdollProp : C_BaseAnimating
{
	const int RAGDOLL_MAX_ELEMENTS = 32;
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(RagPos)) { Encoding = NetEncoding.Elements, Count = RAGDOLL_MAX_ELEMENTS },
		new(nameof(RagAngles)) { Encoding = NetEncoding.Elements, Count = RAGDOLL_MAX_ELEMENTS },
		nameof(HUnragdoll),
		nameof(BlendWeight),
		nameof(OverlaySequence),
	];

	[NetworkVar("m_ragPos[ {0} ]")]
	public InlineArray32<Vector3> RagPos;
	[NetworkVar("m_ragAngles[ {0} ]")]
	public InlineArray32<QAngle> RagAngles;
	[NetworkVar("m_hUnragdoll")]
	public EHANDLE HUnragdoll = new();
	[NetworkVar("m_flBlendWeight")]
	public float BlendWeight;
	[NetworkVar("m_nOverlaySequence")]
	public int OverlaySequence;

	public override bool IsRagdollProp() => true;
}
