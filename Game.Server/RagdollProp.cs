using Source.Common;
using Source;

using Game.Shared;

using System.Numerics;
namespace Game.Server;

using FIELD = FIELD<RagdollProp>;
[LinkEntityToClass("physics_prop_ragdoll")]
[LinkEntityToClass("prop_ragdoll")]
[NetworkName("CRagdollProp")]
public class RagdollProp : BaseAnimating
{
	const int RAGDOLL_MAX_ELEMENTS = 32;
	public static readonly SendTable DT_Ragdoll = new(DT_BaseAnimating, [
		.. Enumerable.Range(0, RAGDOLL_MAX_ELEMENTS).Select(i => SendPropVector(FIELD.OF_ARRAYINDEX(nameof(RagPos), i), -1, PropFlags.Coord)),
		.. Enumerable.Range(0, RAGDOLL_MAX_ELEMENTS).Select(i => SendPropQAngles(FIELD.OF_ARRAYINDEX(nameof(RagAngles), i), 13, PropFlags.RoundDown)),

		SendPropEHandle(FIELD.OF(nameof(HUnragdoll))),
		SendPropFloat(FIELD.OF(nameof(BlendWeight)), 8, PropFlags.RoundDown, 0.0f, 1.0f),
		SendPropInt(FIELD.OF(nameof(OverlaySequence)), 11, 0),
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_Ragdoll);

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

	readonly Ragdoll ragdoll = new();
	public Ragdoll GetRagdoll() => ragdoll;

	public static bool Ragdoll_IsPropRagdoll(BaseEntity entity) => entity is RagdollProp;

	public static Ragdoll? Ragdoll_GetRagdoll(BaseEntity entity) => (entity as RagdollProp)?.GetRagdoll();

	public override bool IsRagdollProp() => true;
}
