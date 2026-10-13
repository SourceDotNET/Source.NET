namespace Game.Server;

using Game.Client;
using Game.Shared;

using Source.Common;

using FIELD = Source.FIELD<C_EnvTonemapController>;

[NetworkName("CEnvTonemapController")]
public class C_EnvTonemapController : C_BaseEntity
{
	public static readonly RecvTable DT_EnvTonemapController = new(DT_BaseEntity, [
		RecvPropBool(FIELD.OF(nameof(UseCustomAutoExposureMin))),
		RecvPropBool(FIELD.OF(nameof(UseCustomAutoExposureMax))),
		RecvPropBool(FIELD.OF(nameof(UseCustomBloomScale))),
		RecvPropFloat(FIELD.OF(nameof(CustomAutoExposureMin))),
		RecvPropFloat(FIELD.OF(nameof(CustomAutoExposureMax))),
		RecvPropFloat(FIELD.OF(nameof(CustomBloomScale))),
		RecvPropFloat(FIELD.OF(nameof(CustomBloomScaleMinimum))),
	]); public static readonly new ClientClass ClientClass = new ClientClass(null, null, DT_EnvTonemapController);

	[NetworkName("m_bUseCustomAutoExposureMin")]
	public bool UseCustomAutoExposureMin;
	[NetworkName("m_bUseCustomAutoExposureMax")]
	public bool UseCustomAutoExposureMax;
	[NetworkName("m_bUseCustomBloomScale")]
	public bool UseCustomBloomScale;
	[NetworkName("m_flCustomAutoExposureMin")]
	public float CustomAutoExposureMin;
	[NetworkName("m_flCustomAutoExposureMax")]
	public float CustomAutoExposureMax;
	[NetworkName("m_flCustomBloomScale")]
	public float CustomBloomScale;
	[NetworkName("m_flCustomBloomScaleMinimum")]
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
