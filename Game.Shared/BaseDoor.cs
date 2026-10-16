#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared;
using FIELD = Source.FIELD<BaseDoor>;
#if !CLIENT_DLL
[LinkEntityToClass("func_door")]
[LinkEntityToClass("func_water")]
#endif
[NetworkName("CBaseDoor")]
public partial class BaseDoor : BaseToggle
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_BaseDoor = new(DT_BaseToggle, [
#if CLIENT_DLL
#else
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_BaseDoor);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_BaseDoor);
#endif
	public float WaveHeight;
}
#endif
