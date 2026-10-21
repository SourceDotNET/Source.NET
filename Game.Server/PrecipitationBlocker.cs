using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;

[LinkEntityToClass("func_precipitation_blocker")]
[NetworkName("CPrecipitationBlocker")]
public partial class PrecipitationBlocker : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
