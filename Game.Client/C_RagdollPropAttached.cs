using Source.Common;
using Source;
using System.Numerics;

namespace Game.Client;

[NetworkName("CRagdollPropAttached", Table = "DT_Ragdoll_Attached")]
public partial class C_RagdollPropAttached : C_RagdollProp
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(BoneIndexAttached)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(RagdollAttachedObjectIndex)) { Bits = 6, Flags = PropFlags.Unsigned },
		new(nameof(AttachmentPointBoneSpace)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(AttachmentPointRagdollSpace)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_boneIndexAttached")]
	public int BoneIndexAttached;
	[NetworkVar("m_ragdollAttachedObjectIndex")]
	public int RagdollAttachedObjectIndex;
	[NetworkVar("m_attachmentPointBoneSpace")]
	public Vector3 AttachmentPointBoneSpace;
	[NetworkVar("m_attachmentPointRagdollSpace")]
	public Vector3 AttachmentPointRagdollSpace;
}
