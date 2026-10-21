using Game.Shared;
using Source.Common;
using Source;

namespace Game.Server;

[LinkEntityToClass("game_ragdoll_manager")]
[NetworkName("CRagdollManager", NoBase = true)]
public partial class RagdollManager : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(CurrentMaxRagdollCount)) { Bits = 6 },
	];

	[NetworkVar("m_iCurrentMaxRagdollCount")]
	public partial int CurrentMaxRagdollCount { get; set; }

}
