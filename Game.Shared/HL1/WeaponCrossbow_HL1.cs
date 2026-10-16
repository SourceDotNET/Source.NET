#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
using FIELD = Source.FIELD<WeaponCrossbow_HL1>;
[LinkEntityToClass("weapon_crossbow_hl1")]
[NetworkName("CWeaponCrossbow_HL1")]
public class WeaponCrossbow_HL1 : BaseHL1MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponCrossbow_HL1 = new(DT_BaseHL1MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropBool(FIELD.OF(nameof(InZoom)))
#else
			SendPropBool(FIELD.OF(nameof(InZoom)))
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponCrossbow_HL1);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponCrossbow_HL1);
#endif
	[NetworkName("m_fInZoom")]
	public bool InZoom;
}
#endif
