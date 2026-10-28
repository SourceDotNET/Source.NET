using Source.Common;
using Game.Shared;

namespace Game.Server.HL1;

[NetworkName("CBaseHelicopter_HL1")]
public partial class BaseHelicopter_HL1 : AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
