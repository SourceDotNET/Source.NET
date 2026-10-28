using Game.Shared;
using Game.Shared.HL2;

using Source.Common;

namespace Game.Client.HL2;

using FIELD = Source.FIELD<C_BaseHLPlayer>;

[NetworkName("CHL2_Player")]
public partial class C_BaseHLPlayer : C_BasePlayer
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(HL2Local)) { Encoding = NetEncoding.DataTable, DataTable = "DT_HL2Local" },
		nameof(m_bIsSprinting),
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		new(nameof(HL2Local)) { Embedded = true },
		nameof(m_bIsSprinting),
	];

	public C_BaseHLPlayer() {
		AddVar(this, FIELD.OF("Local.PunchAngle"), Local.iv_PunchAngle, LatchFlags.LatchSimulationVar);
		AddVar(this, FIELD.OF("Local.PunchAngleVel"), Local.iv_PunchAngleVel, LatchFlags.LatchSimulationVar);
	}

	public override void OnDataChanged(DataUpdateType updateType) {
		if (updateType == DataUpdateType.Created)
			SetNextClientThink(CLIENT_THINK_ALWAYS);
		base.OnDataChanged(updateType);
	}

	public void ExitLadder(){ }

	[NetworkVar("m_HL2Local")]
	public readonly C_HL2PlayerLocalData HL2Local = new();
	[NetworkVar("m_fIsSprinting")]
	public bool m_bIsSprinting;
	public bool m_bPlayUseDenySound;

	public ref LadderMove GetLadderMove() => ref HL2Local.LadderMove;
	public bool IsSprinting() => m_bIsSprinting;
	public bool IsSprintingBits() => (HL2Local.BitsActiveDevices & 1) != 0;
	public bool IsFlashlightActive() => (HL2Local.BitsActiveDevices & 2) != 0;
	public bool IsBreatherActive() => (HL2Local.BitsActiveDevices & 4) != 0;
}
