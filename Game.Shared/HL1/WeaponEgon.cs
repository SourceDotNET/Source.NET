#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
using FIELD = Source.FIELD<WeaponEgon>;
[LinkEntityToClass("weapon_egon")]
[NetworkName("CWeaponEgon")]
public class WeaponEgon : BaseHL1MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponEgon = new(DT_BaseHL1MPCombatWeapon, []);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponEgon);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponEgon);
#endif
	public float InZoom;
}
#endif
