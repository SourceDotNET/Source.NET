using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;

[NetworkName("CTriggerPlayerMovement")]
public partial class C_TriggerPlayerMovement : C_BaseTrigger
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
