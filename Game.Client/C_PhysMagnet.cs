using Game.Shared;

using Source.Common;

namespace Game.Client;

[NetworkName("CPhysMagnet")]
public partial class C_PhysMagnet : C_BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
