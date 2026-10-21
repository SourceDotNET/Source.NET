using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CBoneFollower")]
public partial class C_BoneFollower : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(SolidIndex)) { Bits = 6, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_modelIndex")]
	public new int ModelIndex;
	[NetworkVar("m_solidIndex")]
	public int SolidIndex;
}
