#if CLIENT_DLL || GAME_DLL
using Source.Common;

using System.Drawing;
namespace Game.Shared.HL1;
using FIELD_RPG = Source.FIELD<WeaponRPG_HL1>;
using FIELD_LASER = Source.FIELD<LaserDot_HL1>;
[LinkEntityToClass("weapon_rpg_hl1")]
[NetworkName("CWeaponRPG_HL1")]
public class WeaponRPG_HL1 : BaseHL1MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponRPG_HL1 = new(DT_BaseHL1MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropBool(FIELD_RPG.OF(nameof(InitialStateUpdate))),
			RecvPropBool(FIELD_RPG.OF(nameof(Guiding))),
			RecvPropBool(FIELD_RPG.OF(nameof(LaserDotSuspended)))
#else
			SendPropBool(FIELD_RPG.OF(nameof(InitialStateUpdate))),
			SendPropBool(FIELD_RPG.OF(nameof(Guiding))),
			SendPropBool(FIELD_RPG.OF(nameof(LaserDotSuspended)))
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponRPG_HL1);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponRPG_HL1);
#endif
	[NetworkName("m_bIntialStateUpdate")]
	public bool InitialStateUpdate;
	[NetworkName("m_bGuiding")]
	public bool Guiding;
	[NetworkName("m_bLaserDotSuspended")]
	public bool LaserDotSuspended;
}

#if !CLIENT_DLL
[LinkEntityToClass("laser_spot")]
#endif
[NetworkName("CLaserDot_HL1")]
public class LaserDot_HL1 : BaseEntity
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_LaserDot_HL1 = new(DT_BaseEntity, [
#if CLIENT_DLL
			RecvPropBool(FIELD_LASER.OF(nameof(IsOn))),
#else
			SendPropBool(FIELD_LASER.OF(nameof(IsOn))),
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_LaserDot_HL1);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_LaserDot_HL1);
#endif
	[NetworkName("m_bIsOn")]
	public bool IsOn;
}
#endif
