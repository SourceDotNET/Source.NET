using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEArmorRicochet")]
public partial class TEArmorRicochet(ReadOnlySpan<char> name) : TEMetalSparks(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
