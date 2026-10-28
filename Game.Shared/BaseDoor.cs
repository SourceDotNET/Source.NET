#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared;
#if !CLIENT_DLL
[LinkEntityToClass("func_door")]
[LinkEntityToClass("func_water")]
#endif
[NetworkName("CBaseDoor")]
public partial class BaseDoor : BaseToggle
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

	public float WaveHeight;
}
#endif
