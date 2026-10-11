using Source.Common;
using Source;

using Game.Shared;

namespace Game.Client;

using FIELD = FIELD<C_WaterLODControl>;

[NetworkName("CWaterLODControl")]
public class C_WaterLODControl : C_BaseEntity
{
	public static readonly RecvTable DT_WaterLODControl = new([
		RecvPropFloat(FIELD.OF(nameof(CheapWaterStartDistance))),
		RecvPropFloat(FIELD.OF(nameof(CheapWaterEndDistance))),
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WaterLODControl);

	[NetworkName("m_flCheapWaterStartDistance")]
	public float CheapWaterStartDistance;
	[NetworkName("m_flCheapWaterEndDistance")]
	public float CheapWaterEndDistance;

	public override void OnDataChanged(DataUpdateType updateType) {
		view.SetCheapWaterStartDistance(CheapWaterStartDistance);
		view.SetCheapWaterEndDistance(CheapWaterEndDistance);
	}
}

