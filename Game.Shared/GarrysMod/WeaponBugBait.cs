#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
namespace Game.Shared.GarrysMod;
using FIELD = Source.FIELD<WeaponBugBait>;

[LinkEntityToClass("weapon_bugbait")]
[PrecacheWeaponRegister("weapon_bugbait")]
[NetworkName("CWeaponBugBait")]
public class WeaponBugBait : BaseHL2MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponBugBait = new(DT_BaseHL2MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropBool(FIELD.OF(nameof(Redraw))),
			RecvPropBool(FIELD.OF(nameof(DrawbackFinished)))
#else
			SendPropBool(FIELD.OF(nameof(Redraw))),
			SendPropBool(FIELD.OF(nameof(DrawbackFinished)))
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponBugBait);
	public static readonly new DataMap PredMap = new([], typeof(WeaponBugBait), BaseHL2MPCombatWeapon.PredMap); public override DataMap? GetPredDescMap() => PredMap;

#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponBugBait);
#endif
	[NetworkName("m_bRedraw")]
	public bool Redraw;
	[NetworkName("m_bDrawBackFinished")]
	public bool DrawbackFinished;
}
#endif
