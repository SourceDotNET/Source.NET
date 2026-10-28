using Game.Shared;

using Source.Common;

namespace Game.Server;

[LinkEntityToClass("phys_magnet")]
[NetworkName("CPhysMagnet")]
public partial class PhysMagnet : BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
