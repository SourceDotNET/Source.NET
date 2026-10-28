using Source.Common;
using Source;

namespace Game.Client;

[NetworkName("CRagdollManager", NoBase = true)]
public partial class C_RagdollManager : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(CurrentMaxRagdollCount)) { Bits = 6 },
	];

	[NetworkVar("m_iCurrentMaxRagdollCount")]
	public int CurrentMaxRagdollCount;
}
