#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
namespace Game.Shared.GarrysMod;
using FIELD = Source.FIELD<WeaponFrag>;

[LinkEntityToClass("weapon_frag")]
[PrecacheWeaponRegister("weapon_frag")]
[NetworkName("CWeaponFrag")]
public class WeaponFrag : BaseHL2MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponFrag = new(DT_BaseHL2MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropBool(FIELD.OF(nameof(Redraw))),
			RecvPropBool(FIELD.OF(nameof(DrawbackFinished))),
			RecvPropInt(FIELD.OF(nameof(AttackPaused)))
#else
			SendPropBool(FIELD.OF(nameof(Redraw))),
			SendPropBool(FIELD.OF(nameof(DrawbackFinished))),
			SendPropInt(FIELD.OF(nameof(AttackPaused)), 4)
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponFrag);
	public static readonly new DataMap PredMap = new([], typeof(WeaponFrag), BaseHL2MPCombatWeapon.PredMap); public override DataMap? GetPredDescMap() => PredMap;

#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponFrag);
#endif
	[NetworkName("m_bRedraw")]
	public bool Redraw;
	[NetworkName("m_fDrawbackFinished")]
	public bool DrawbackFinished;
	[NetworkName("m_AttackPaused")]
	public int AttackPaused;
}
#endif
