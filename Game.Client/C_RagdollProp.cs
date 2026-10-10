using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
using FIELD = FIELD<C_RagdollProp>;
[NetworkName("CRagdollProp")]
public class C_RagdollProp : C_BaseAnimating
{
	const int RAGDOLL_MAX_ELEMENTS = 32;
	public static readonly RecvTable DT_Ragdoll = new(DT_BaseAnimating, [
		.. Enumerable.Range(0, RAGDOLL_MAX_ELEMENTS).Select(i => RecvPropVector(FIELD.OF_ARRAYINDEX(nameof(RagPos), i))),
		.. Enumerable.Range(0, RAGDOLL_MAX_ELEMENTS).Select(i => RecvPropQAngles(FIELD.OF_ARRAYINDEX(nameof(RagAngles), i))),

		RecvPropEHandle(FIELD.OF(nameof(HUnragdoll))),
		RecvPropFloat(FIELD.OF(nameof(BlendWeight))),
		RecvPropInt(FIELD.OF(nameof(OverlaySequence))),
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_Ragdoll);

	[NetworkName("m_ragPos[ {0} ]")]
	public InlineArray32<Vector3> RagPos;
	[NetworkName("m_ragAngles[ {0} ]")]
	public InlineArray32<Vector3> RagAngles;
	[NetworkName("m_hUnragdoll")]
	public EHANDLE HUnragdoll = new();
	[NetworkName("m_flBlendWeight")]
	public float BlendWeight;
	[NetworkName("m_nOverlaySequence")]
	public int OverlaySequence;

	public override bool IsRagdollProp() => true;
}
