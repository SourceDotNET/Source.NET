namespace Game.Client.HL2;

using Game.Shared.HL2;

using Source.Common;

using System.Numerics;

public partial class C_HL2PlayerLocalData {
	[NetworkTable("DT_HL2Local")]
	public static readonly NetworkProp[] Props = [
		nameof(SuitPower),
		nameof(Zooming),
		nameof(BitsActiveDevices),
		nameof(SquadMemberCount),
		nameof(SquadMedicCount),
		nameof(SquadInFollowMode),
		nameof(WeaponLowered),
		nameof(Ladder),
		nameof(DisplayReticle),
	];

	[PredictionTable]
	public static readonly PredictionField[] Prediction = [
		nameof(Ladder),
	];

	[NetworkVar("m_flSuitPower")]
	public float SuitPower;
	[NetworkVar("m_bZooming")]
	public bool Zooming;
	[NetworkVar("m_bitsActiveDevices")]
	public int BitsActiveDevices;
	[NetworkVar("m_iSquadMemberCount")]
	public int SquadMemberCount;
	[NetworkVar("m_iSquadMedicCount")]
	public int SquadMedicCount;
	[NetworkVar("m_fSquadInFollowMode")]
	public bool SquadInFollowMode;
	[NetworkVar("m_bWeaponLowered")]
	public bool WeaponLowered;
	[NetworkVar("m_hLadder")]
	public EHANDLE Ladder = new();
	public EHANDLE AutoAimTargetHandle = new();
	public Vector3 AutoAimPoint;
	[NetworkVar("m_bDisplayReticle")]
	public bool DisplayReticle;
	public bool StickyAutoAim;
	public bool AutoAimTarget;
	public LadderMove LadderMove = new();
}
