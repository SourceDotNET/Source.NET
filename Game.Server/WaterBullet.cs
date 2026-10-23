using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("waterbullet")]
[NetworkName("CWaterBullet")]
public partial class WaterBullet : BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
