#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
using FIELD = Source.FIELD<WeaponSatchel>;
[LinkEntityToClass("weapon_satchel")]
[NetworkName("CWeaponSatchel")]
public class WeaponSatchel : BaseHL1MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponSatchel = new(DT_BaseHL1MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropInt(FIELD.OF(nameof(RadioViewIndex))),
			RecvPropInt(FIELD.OF(nameof(RadioWorldIndex))),
			RecvPropInt(FIELD.OF(nameof(SatchelViewIndex))),
			RecvPropInt(FIELD.OF(nameof(SatchelWorldIndex))),
			RecvPropInt(FIELD.OF(nameof(ChargeReady))),
#else
			SendPropInt(FIELD.OF(nameof(RadioViewIndex)), 14),
			SendPropInt(FIELD.OF(nameof(RadioWorldIndex)), 14),
			SendPropInt(FIELD.OF(nameof(SatchelViewIndex)), 14),
			SendPropInt(FIELD.OF(nameof(SatchelWorldIndex)), 14),
			SendPropInt(FIELD.OF(nameof(ChargeReady)), 3, PropFlags.Unsigned),
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponSatchel);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponSatchel);
#endif
	[NetworkName("m_iRadioViewIndex")]
	public int RadioViewIndex;
	[NetworkName("m_iRadioWorldIndex")]
	public float RadioWorldIndex;
	[NetworkName("m_iSatchelViewIndex")]
	public float SatchelViewIndex;
	[NetworkName("m_iSatchelWorldIndex")]
	public float SatchelWorldIndex;
	[NetworkName("m_iChargeReady")]
	public float ChargeReady;
}
#endif
