#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
namespace Game.Shared.GarrysMod;
using FIELD = Source.FIELD<WeaponSLAM>;

[LinkEntityToClass("weapon_slam")]
[PrecacheWeaponRegister("weapon_slam")]
[NetworkName("CWeapon_SLAM")]
public class WeaponSLAM : BaseHL2MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_Weapon_SLAM = new(DT_BaseHL2MPCombatWeapon, [
#if CLIENT_DLL
			RecvPropInt(FIELD.OF(nameof(SlamState))),
			RecvPropBool(FIELD.OF(nameof(DetonatorArmed))),
			RecvPropBool(FIELD.OF(nameof(NeedDetonatorDraw))),
			RecvPropBool(FIELD.OF(nameof(NeedDetonatorHolster))),
			RecvPropBool(FIELD.OF(nameof(NeedReload))),
			RecvPropBool(FIELD.OF(nameof(ClearReload))),
			RecvPropBool(FIELD.OF(nameof(ThrowSatchel))),
			RecvPropBool(FIELD.OF(nameof(AttachSatchel))),
			RecvPropBool(FIELD.OF(nameof(AttachTripmine)))
#else
			SendPropInt(FIELD.OF(nameof(SlamState)), 4),
			SendPropBool(FIELD.OF(nameof(DetonatorArmed))),
			SendPropBool(FIELD.OF(nameof(NeedDetonatorDraw))),
			SendPropBool(FIELD.OF(nameof(NeedDetonatorHolster))),
			SendPropBool(FIELD.OF(nameof(NeedReload))),
			SendPropBool(FIELD.OF(nameof(ClearReload))),
			SendPropBool(FIELD.OF(nameof(ThrowSatchel))),
			SendPropBool(FIELD.OF(nameof(AttachSatchel))),
			SendPropBool(FIELD.OF(nameof(AttachTripmine)))
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_Weapon_SLAM);
	public static readonly new DataMap PredMap = new([], typeof(WeaponSLAM), BaseHL2MPCombatWeapon.PredMap); public override DataMap? GetPredDescMap() => PredMap;
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_Weapon_SLAM);
#endif
	[NetworkName("m_tSlamState")]
	public int SlamState;
	[NetworkName("m_bDetonatorArmed")]
	public bool DetonatorArmed;
	[NetworkName("m_bNeedDetonatorDraw")]
	public bool NeedDetonatorDraw;
	[NetworkName("m_bNeedDetonatorHolster")]
	public bool NeedDetonatorHolster;
	[NetworkName("m_bNeedReload")]
	public bool NeedReload;
	[NetworkName("m_bClearReload")]
	public bool ClearReload;
	[NetworkName("m_bThrowSatchel")]
	public bool ThrowSatchel;
	[NetworkName("m_bAttachSatchel")]
	public bool AttachSatchel;
	[NetworkName("m_bAttachTripmine")]
	public bool AttachTripmine;
}
#endif
