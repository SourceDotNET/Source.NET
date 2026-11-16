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

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_MP_STAND_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_SLAM, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCH_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_CROUCH_SLAM, Required = false },
		new() { BaseAct = Activity.ACT_MP_RUN, WeaponAct = Activity.ACT_HL2MP_RUN_SLAM, Required = false },
		new() { BaseAct = Activity.ACT_MP_WALK, WeaponAct = Activity.ACT_HL2MP_WALK_SLAM, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCHWALK, WeaponAct = Activity.ACT_HL2MP_WALK_CROUCH_SLAM, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_STAND_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_SLAM, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_CROUCH_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_SLAM, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_STAND, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_SLAM, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_CROUCH, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_SLAM, Required = false },
		new() { BaseAct = Activity.ACT_MP_JUMP, WeaponAct = Activity.ACT_HL2MP_JUMP_SLAM, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM, WeaponAct = Activity.ACT_HL2MP_SWIM_SLAM, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM_IDLE, WeaponAct = Activity.ACT_HL2MP_SWIM_IDLE_SLAM, Required = false },
	];

	public override ReadOnlySpan<ActTable> ActivityList() => acttable;

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
