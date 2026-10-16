#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
using FIELD = Source.FIELD<WeaponGlock>;
[LinkEntityToClass("weapon_glock_hl1")]
[NetworkName("CWeaponGlock")]
public class WeaponGlock : BaseHL1MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponGlock = new(DT_BaseHL1MPCombatWeapon, []);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponGlock);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponGlock);
#endif
	public float InZoom;
}
#endif
