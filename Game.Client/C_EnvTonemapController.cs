namespace Game.Server;

using Game.Client;
using Game.Shared;

using Source.Common;


[NetworkName("CEnvTonemapController")]
public partial class C_EnvTonemapController : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(UseCustomAutoExposureMin),
		nameof(UseCustomAutoExposureMax),
		nameof(UseCustomBloomScale),
		new(nameof(CustomAutoExposureMin)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0, High = 0 },
		new(nameof(CustomAutoExposureMax)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0, High = 0 },
		new(nameof(CustomBloomScale)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0, High = 0 },
		new(nameof(CustomBloomScaleMinimum)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0, High = 0 },
	];

	[NetworkVar("m_bUseCustomAutoExposureMin")]
	public bool UseCustomAutoExposureMin;
	[NetworkVar("m_bUseCustomAutoExposureMax")]
	public bool UseCustomAutoExposureMax;
	[NetworkVar("m_bUseCustomBloomScale")]
	public bool UseCustomBloomScale;
	[NetworkVar("m_flCustomAutoExposureMin")]
	public float CustomAutoExposureMin;
	[NetworkVar("m_flCustomAutoExposureMax")]
	public float CustomAutoExposureMax;
	[NetworkVar("m_flCustomBloomScale")]
	public float CustomBloomScale;
	[NetworkVar("m_flCustomBloomScaleMinimum")]
	public float CustomBloomScaleMinimum;

	static EHANDLE g_hTonemapControllerInUse = new();

	public override void UpdateOnRemove() {
		if (g_hTonemapControllerInUse.Get() == this) {
			g_bUseCustomAutoExposureMin = false;
			g_bUseCustomAutoExposureMax = false;
			g_bUseCustomBloomScale = false;
		}
		base.UpdateOnRemove();
	}

	public override void OnDataChanged(DataUpdateType updateType) {
		base.OnDataChanged(updateType);

		g_bUseCustomAutoExposureMin = UseCustomAutoExposureMin;
		g_bUseCustomAutoExposureMax = UseCustomAutoExposureMax;
		g_bUseCustomBloomScale = UseCustomBloomScale;
		g_flCustomAutoExposureMin = CustomAutoExposureMin;
		g_flCustomAutoExposureMax = CustomAutoExposureMax;
		g_flCustomBloomScale = CustomBloomScale;
		g_flCustomBloomScaleMinimum = CustomBloomScaleMinimum;

		g_hTonemapControllerInUse.Set(this);
	}
}
