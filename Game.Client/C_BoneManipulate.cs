using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;

[LinkEntityToClass("manipulate_bone")]
[NetworkName("CBoneManipulate")]
public partial class C_BoneManipulate : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(BonePos)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoneAng)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoneScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BoneJiggle)) { Bits = 4, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_BonePos")]
	public InlineArrayMaxStudioBones<Vector3> BonePos;
	[NetworkVar("m_BoneAng")]
	public InlineArrayMaxStudioBones<Vector3> BoneAng;
	[NetworkVar("m_BoneScale")]
	public InlineArrayMaxStudioBones<Vector3> BoneScale;
	[NetworkVar("m_BoneJiggle")]
	public InlineArrayMaxStudioBones<int> BoneJiggle;
}
