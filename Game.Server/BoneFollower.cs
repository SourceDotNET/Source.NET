using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("phys_bone_follower")]
[NetworkName("CBoneFollower")]
public partial class BoneFollower : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(SolidIndex)) { Bits = 6, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_modelIndex")]
	public new partial int ModelIndex { get; set; }
	[NetworkVar("m_solidIndex")]
	public partial int SolidIndex { get; set; }
}
