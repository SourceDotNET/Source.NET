using Game.Client;
using Game.Shared;

using Source.Common;

namespace Game.Client;

[NetworkName("CBreakableProp")]
public partial class C_BreakableProp : C_BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
