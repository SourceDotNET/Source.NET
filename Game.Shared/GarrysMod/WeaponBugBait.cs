#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
namespace Game.Shared.GarrysMod;

[LinkEntityToClass("weapon_bugbait")]
[PrecacheWeaponRegister("weapon_bugbait")]
[NetworkName("CWeaponBugBait")]
public partial class WeaponBugBait : BaseHL2MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Redraw),
		nameof(DrawbackFinished),
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(Redraw),
		nameof(DrawbackFinished),
	];

	[NetworkVar("m_bRedraw")]
	public partial bool Redraw { get; set; }
	[NetworkVar("m_bDrawBackFinished")]
	public partial bool DrawbackFinished { get; set; }
}
#endif
