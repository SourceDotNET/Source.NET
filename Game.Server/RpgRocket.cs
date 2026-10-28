using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;

[LinkEntityToClass("rpg_rocket")]
[NetworkName("CRpgRocket")]
public partial class RpgRocket : BaseGrenade
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
