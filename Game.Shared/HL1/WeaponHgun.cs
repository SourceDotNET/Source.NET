#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
using FIELD = Source.FIELD<WeaponHgun>;
[LinkEntityToClass("weapon_hornetgun")]
[NetworkName("CWeaponHgun")]
public class WeaponHgun : BaseHL1MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponHgun = new(DT_BaseHL1MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropFloat(FIELD.OF(nameof(RechargeTime))),
			RecvPropInt(FIELD.OF(nameof(FirePhase))),
#else
			SendPropFloat(FIELD.OF(nameof(RechargeTime)), 0, PropFlags.NoScale),
			SendPropInt(FIELD.OF(nameof(FirePhase)), 4, PropFlags.Unsigned),
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponHgun);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponHgun);
#endif
	[NetworkName("m_flRechargeTime")]
	public TimeUnit_t RechargeTime;
	[NetworkName("m_iFirePhase")]
	public int FirePhase;
}
#endif
