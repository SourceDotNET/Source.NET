#if CLIENT_DLL || GAME_DLL
using Source.Common;
namespace Game.Shared.HL1;
using FIELD = Source.FIELD<WeaponHandGrenade>;
[LinkEntityToClass("weapon_handgrenade")]
[NetworkName("CWeaponHandGrenade")]
public class WeaponHandGrenade : BaseHL1MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponHandGrenade = new(DT_BaseHL1MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropFloat(FIELD.OF(nameof(StartThrow))),
			RecvPropFloat(FIELD.OF(nameof(ReleaseThrow))),
#else
			SendPropFloat(FIELD.OF(nameof(StartThrow)), 0, PropFlags.NoScale),
			SendPropFloat(FIELD.OF(nameof(ReleaseThrow)), 0, PropFlags.NoScale),
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponHandGrenade);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponHandGrenade);
#endif
	[NetworkName("m_flStartThrow")]
	public TimeUnit_t StartThrow;
	[NetworkName("m_flReleaseThrow")]
	public TimeUnit_t ReleaseThrow;
}
#endif
