using Source.Common;
using Source.Common.Mathematics;
using Source;

using Game.Shared;

using System.Numerics;
using Source.Common.Physics;
namespace Game.Server;

[LinkEntityToClass("physics_prop_ragdoll")]
[LinkEntityToClass("prop_ragdoll")]
[NetworkName("CRagdollProp", Table = "DT_Ragdoll")]
public partial class RagdollProp : BaseAnimating
{
	public RagdollProp() {
		HUnragdoll = new();
	}

	const int RAGDOLL_MAX_ELEMENTS = 32;
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(RagPos)) { Encoding = NetEncoding.Elements, Count = RAGDOLL_MAX_ELEMENTS, Bits = -1, Flags = PropFlags.Coord },
		new(nameof(RagAngles)) { Encoding = NetEncoding.Elements, Count = RAGDOLL_MAX_ELEMENTS, Bits = 13, Flags = PropFlags.RoundDown },
		nameof(HUnragdoll),
		new(nameof(BlendWeight)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0.0f, High = 1.0f },
		new(nameof(OverlaySequence)) { Bits = 11 },
	];

	[NetworkVar("m_ragPos[ {0} ]")]
	public partial ref readonly InlineArray32<Vector3> RagPos { get; }
	[NetworkVar("m_ragAngles[ {0} ]")]
	public partial ref readonly InlineArray32<QAngle> RagAngles { get; }
	[NetworkVar("m_hUnragdoll")]
	public partial EHANDLE HUnragdoll { get; set; }
	[NetworkVar("m_flBlendWeight")]
	public partial float BlendWeight { get; set; }
	[NetworkVar("m_nOverlaySequence")]
	public partial int OverlaySequence { get; set; }

	readonly Ragdoll ragdoll = new();
	public Ragdoll GetRagdoll() => ragdoll;

	public static bool Ragdoll_IsPropRagdoll(BaseEntity entity) => entity is RagdollProp;

	public static Ragdoll? Ragdoll_GetRagdoll(BaseEntity entity) => (entity as RagdollProp)?.GetRagdoll();

	public override bool IsRagdollProp() => true;

	public override int VPhysicsGetObjectList(Span<IPhysicsObject?> list) {
		for (int i = 0; i < ragdoll.ListCount; i++) 
			if (i < list.Length) 
				list[i] = ragdoll.List[i].Object;

		return ragdoll.ListCount;
	}
}
