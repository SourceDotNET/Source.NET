using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;

[NetworkName("CRpgRocket")]
public partial class C_RpgRocket : C_BaseGrenade
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
