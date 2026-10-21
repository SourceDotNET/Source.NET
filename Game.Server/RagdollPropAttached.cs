using Game.Shared;
using Source.Common;
using Source;
using System.Numerics;

namespace Game.Server;

[LinkEntityToClass("prop_ragdoll_attached")]
[NetworkName("CRagdollPropAttached", Table = "DT_Ragdoll_Attached")]
public partial class RagdollPropAttached : RagdollProp
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(BoneIndexAttached)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(RagdollAttachedObjectIndex)) { Bits = 6, Flags = PropFlags.Unsigned },
		new(nameof(AttachmentPointBoneSpace)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(AttachmentPointRagdollSpace)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_boneIndexAttached")]
	public partial int BoneIndexAttached { get; set; }
	[NetworkVar("m_ragdollAttachedObjectIndex")]
	public partial int RagdollAttachedObjectIndex { get; set; }
	[NetworkVar("m_attachmentPointBoneSpace")]
	public partial Vector3 AttachmentPointBoneSpace { get; set; }
	[NetworkVar("m_attachmentPointRagdollSpace")]
	public partial Vector3 AttachmentPointRagdollSpace { get; set; }
}
