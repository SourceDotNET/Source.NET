#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;

using System.Numerics;
namespace Game.Shared.GarrysMod;

using FIELD = Source.FIELD<WeaponRPG>;

[LinkEntityToClass("weapon_rpg")]
[PrecacheWeaponRegister("weapon_rpg")]
[NetworkName("CWeaponRPG")]
public class WeaponRPG : BaseHL2MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponRPG = new(DT_BaseHL2MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropBool(FIELD.OF(nameof(InitialStateUpdate))),
			RecvPropBool(FIELD.OF(nameof(Guiding))),
			RecvPropBool(FIELD.OF(nameof(HideGuiding))),
			// todo: RecvProxy_MissileDied
			RecvPropEHandle(FIELD.OF(nameof(Missile))),
			RecvPropVector(FIELD.OF(nameof(LaserDot))),
#else
			SendPropBool(FIELD.OF(nameof(InitialStateUpdate))),
			SendPropBool(FIELD.OF(nameof(Guiding))),
			SendPropBool(FIELD.OF(nameof(HideGuiding))),
			SendPropEHandle(FIELD.OF(nameof(Missile))),
			SendPropVector(FIELD.OF(nameof(LaserDot)), 0, PropFlags.NoScale),
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponRPG);
	public static readonly new DataMap PredMap = new([], typeof(WeaponRPG), BaseHL2MPCombatWeapon.PredMap); public override DataMap? GetPredDescMap() => PredMap;
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponRPG);
#endif
	[NetworkName("m_bInitialStateUpdate")]
	public bool InitialStateUpdate;
	[NetworkName("m_bGuiding")]
	public bool Guiding;
	[NetworkName("m_bHideGuiding")]
	public bool HideGuiding;
	[NetworkName("m_hMissile")]
	public EHANDLE Missile = new();
	[NetworkName("m_vecLaserDot")]
	public Vector3 LaserDot;
	public override float GetFireRate() => 1f;
}

[LinkEntityToClass("env_laserdot")]
#if CLIENT_DLL
[LinkEntityToClass("laser_spot")]
#endif
[NetworkName("CLaserDot")]
public class LaserDot : BaseEntity
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_LaserDot = new(DT_BaseEntity, []);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_LaserDot);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_LaserDot);
#endif
}
#endif
