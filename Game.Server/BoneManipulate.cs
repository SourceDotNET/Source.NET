using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("manipulate_bone")]
[NetworkName("CBoneManipulate")]
public partial class BoneManipulate : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(BonePos)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoneAng)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoneScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoneJiggle)) { Bits = 4, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_BonePos")]
	public partial ref readonly InlineArrayMaxStudioBones<Vector3> BonePos { get; }
	[NetworkVar("m_BoneAng")]
	public partial ref readonly InlineArrayMaxStudioBones<Vector3> BoneAng { get; }
	[NetworkVar("m_BoneScale")]
	public partial ref readonly InlineArrayMaxStudioBones<Vector3> BoneScale { get; }
	[NetworkVar("m_BoneJiggle")]
	public partial ref readonly InlineArrayMaxStudioBones<int> BoneJiggle { get; }
}
