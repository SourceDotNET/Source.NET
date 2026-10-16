#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
using FIELD = Source.FIELD<WeaponGauss>;
[LinkEntityToClass("weapon_gauss")]
[NetworkName("CWeaponGauss")]
public class WeaponGauss : BaseHL1MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponGauss = new(DT_BaseHL1MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropInt(FIELD.OF(nameof(AttackState))),
			RecvPropBool(FIELD.OF(nameof(PrimaryFire))),
			RecvPropFloat(FIELD.OF(nameof(StartCharge))),
			RecvPropFloat(FIELD.OF(nameof(AmmoStartCharge))),
			RecvPropTime64(FIELD.OF(nameof(PlayAftershock))),
			RecvPropTime64(FIELD.OF(nameof(NextAmmoBurn)))
#else
			SendPropInt(FIELD.OF(nameof(AttackState)), 2, PropFlags.Unsigned),
			SendPropBool(FIELD.OF(nameof(PrimaryFire))),
			SendPropFloat(FIELD.OF(nameof(StartCharge)), 0, PropFlags.NoScale),
			SendPropFloat(FIELD.OF(nameof(AmmoStartCharge)), 0, PropFlags.NoScale),
			SendPropTime64(FIELD.OF(nameof(PlayAftershock))),
			SendPropTime64(FIELD.OF(nameof(NextAmmoBurn)))
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponGauss);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponGauss);
#endif
	[NetworkName("m_nAttackState")]
	public int AttackState;
	[NetworkName("m_bPrimaryFire")]
	public bool PrimaryFire;
	[NetworkName("m_flStartCharge")]
	public TimeUnit_t StartCharge;
	[NetworkName("m_flAmmoStartCharge")]
	public TimeUnit_t AmmoStartCharge;
	[NetworkName("m_flPlayAftershock")]
	public TimeUnit_t PlayAftershock;
	[NetworkName("m_flNextAmmoBurn")]
	public TimeUnit_t NextAmmoBurn;
}
#endif
