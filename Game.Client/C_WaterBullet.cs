using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;

[NetworkName("CWaterBullet")]
public partial class C_WaterBullet : C_BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
