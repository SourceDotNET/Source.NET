using Source.Common;
using Source;

using Game.Shared;

namespace Game.Client;

[NetworkName("CWaterLODControl")]
public partial class C_WaterLODControl : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(CheapWaterStartDistance)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(CheapWaterEndDistance)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flCheapWaterStartDistance")]
	public float CheapWaterStartDistance;
	[NetworkVar("m_flCheapWaterEndDistance")]
	public float CheapWaterEndDistance;

	public override void OnDataChanged(DataUpdateType updateType) {
		view.SetCheapWaterStartDistance(CheapWaterStartDistance);
		view.SetCheapWaterEndDistance(CheapWaterEndDistance);
	}
}

