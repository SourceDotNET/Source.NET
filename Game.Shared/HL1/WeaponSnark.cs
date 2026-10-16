#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
using FIELD = Source.FIELD<WeaponSnark>;
[LinkEntityToClass("weapon_snark")]
[NetworkName("CWeaponSnark")]
public class WeaponSnark : BaseHL1MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponSnark = new(DT_BaseHL1MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropBool(FIELD.OF(nameof(JustThrown))),
#else
			SendPropBool(FIELD.OF(nameof(JustThrown))),
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponSnark);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponSnark);
#endif
	[NetworkName("m_bJustThrown")]
	public bool JustThrown;
}
#endif
