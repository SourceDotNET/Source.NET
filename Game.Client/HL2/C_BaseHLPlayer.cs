using Game.Shared;
using Game.Shared.HL2;

using Source.Common;

namespace Game.Client.HL2;

using DEFINE = Source.DEFINE<C_BaseHLPlayer>;
using FIELD = Source.FIELD<C_BaseHLPlayer>;

[NetworkName("CHL2_Player")]
public partial class C_BaseHLPlayer : C_BasePlayer
{
	public static readonly RecvTable DT_HL2_Player = new(DT_BasePlayer, [
		RecvPropDataTable("m_HL2Local", FIELD.OF(nameof(HL2Local)), C_HL2PlayerLocalData.DT_HL2Local, 0, DataTableRecvProxy_PointerDataTable),
		RecvPropBool(FIELD.OF(nameof(m_bIsSprinting)))
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_HL2_Player);

	public static readonly new DataMap PredMap = new(typeof(C_BaseHLPlayer), C_BasePlayer.PredMap, [
		DEFINE.PRED_TYPEDESCRIPTION( nameof(HL2Local), C_HL2PlayerLocalData.PredMap ),
		DEFINE.PRED_FIELD( nameof(m_bIsSprinting), FieldType.Boolean, FieldTypeDescFlags.InSendTable ),
	]); public override DataMap? GetPredDescMap() => PredMap;

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

	[NetworkName("m_HL2Local")]
	public readonly C_HL2PlayerLocalData HL2Local = new();
	[NetworkName("m_fIsSprinting")]
	public bool m_bIsSprinting;
	public bool m_bPlayUseDenySound;

	public ref LadderMove GetLadderMove() => ref HL2Local.LadderMove;
	public bool IsSprinting() => m_bIsSprinting;
	public bool IsSprintingBits() => (HL2Local.BitsActiveDevices & 1) != 0;
	public bool IsFlashlightActive() => (HL2Local.BitsActiveDevices & 2) != 0;
	public bool IsBreatherActive() => (HL2Local.BitsActiveDevices & 4) != 0;
}
