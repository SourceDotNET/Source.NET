#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
namespace Game.Shared.GarrysMod;

[LinkEntityToClass("weapon_slam")]
[PrecacheWeaponRegister("weapon_slam")]
[NetworkName("CWeapon_SLAM")]
public partial class WeaponSLAM : BaseHL2MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SlamState)) { Bits = 4 },
		nameof(DetonatorArmed),
		nameof(NeedDetonatorDraw),
		nameof(NeedDetonatorHolster),
		nameof(NeedReload),
		nameof(ClearReload),
		nameof(ThrowSatchel),
		nameof(AttachSatchel),
		nameof(AttachTripmine),
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(SlamState),
		nameof(DetonatorArmed),
		nameof(NeedDetonatorDraw),
		nameof(NeedDetonatorHolster),
		nameof(NeedReload),
		nameof(ClearReload),
		nameof(ThrowSatchel),
		nameof(AttachSatchel),
		nameof(AttachTripmine),
	];

	[NetworkVar("m_tSlamState")]
	public partial int SlamState { get; set; }
	[NetworkVar("m_bDetonatorArmed")]
	public partial bool DetonatorArmed { get; set; }
	[NetworkVar("m_bNeedDetonatorDraw")]
	public partial bool NeedDetonatorDraw { get; set; }
	[NetworkVar("m_bNeedDetonatorHolster")]
	public partial bool NeedDetonatorHolster { get; set; }
	[NetworkVar("m_bNeedReload")]
	public partial bool NeedReload { get; set; }
	[NetworkVar("m_bClearReload")]
	public partial bool ClearReload { get; set; }
	[NetworkVar("m_bThrowSatchel")]
	public partial bool ThrowSatchel { get; set; }
	[NetworkVar("m_bAttachSatchel")]
	public partial bool AttachSatchel { get; set; }
	[NetworkVar("m_bAttachTripmine")]
	public partial bool AttachTripmine { get; set; }
}
#endif
