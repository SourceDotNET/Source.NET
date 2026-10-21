#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
namespace Game.Shared.GarrysMod;

[LinkEntityToClass("weapon_frag")]
[PrecacheWeaponRegister("weapon_frag")]
[NetworkName("CWeaponFrag")]
public partial class WeaponFrag : BaseHL2MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Redraw),
		nameof(DrawbackFinished),
		new(nameof(AttackPaused)) { Bits = 4 },
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(Redraw),
		nameof(DrawbackFinished),
		nameof(AttackPaused),
	];

	[NetworkVar("m_bRedraw")]
	public partial bool Redraw { get; set; }
	[NetworkVar("m_fDrawbackFinished")]
	public partial bool DrawbackFinished { get; set; }
	[NetworkVar("m_AttackPaused")]
	public partial int AttackPaused { get; set; }
}
#endif
