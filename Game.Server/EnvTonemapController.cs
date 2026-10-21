namespace Game.Server;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Mathematics;

using FIELD = Source.FIELD<EnvTonemapController>;
using DEFINE = Source.DEFINE<EnvTonemapController>;

[LinkEntityToClass("env_tonemap_controller")]
[NetworkName("CEnvTonemapController")]
public partial class EnvTonemapController : PointEntity
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

	public static readonly ConVar mat_hdr_tonemapscale = new("1.0", FCvar.Cheat, "The HDR tonemap scale. 1 = Use autoexposure, 0 = eyes fully closed, 16 = eyes wide open.");

	float BlendTonemapStart;
	float BlendTonemapEnd;
	TimeUnit_t BlendEndTime;
	TimeUnit_t BlendStartTime;

	[NetworkVar("m_bUseCustomAutoExposureMin")]
	public partial bool UseCustomAutoExposureMin { get; set; }
	[NetworkVar("m_bUseCustomAutoExposureMax")]
	public partial bool UseCustomAutoExposureMax { get; set; }
	[NetworkVar("m_bUseCustomBloomScale")]
	public partial bool UseCustomBloomScale { get; set; }
	[NetworkVar("m_flCustomAutoExposureMin")]
	public partial float CustomAutoExposureMin { get; set; }
	[NetworkVar("m_flCustomAutoExposureMax")]
	public partial float CustomAutoExposureMax { get; set; }
	[NetworkVar("m_flCustomBloomScale")]
	public partial float CustomBloomScale { get; set; }
	[NetworkVar("m_flCustomBloomScaleMinimum")]
	public partial float CustomBloomScaleMinimum { get; set; }

	public static readonly new DataMap DataDesc = new(typeof(EnvTonemapController), PointEntity.DataDesc, [
		DEFINE.FIELD(nameof(BlendTonemapStart), FieldType.Float),
		DEFINE.FIELD(nameof(BlendTonemapEnd), FieldType.Float),
		DEFINE.FIELD(nameof(BlendEndTime), FieldType.Time),
		DEFINE.FIELD(nameof(BlendStartTime), FieldType.Time),
		DEFINE.FIELD(nameof(UseCustomAutoExposureMin), FieldType.Boolean),
		DEFINE.FIELD(nameof(UseCustomAutoExposureMax), FieldType.Boolean),
		DEFINE.FIELD(nameof(CustomAutoExposureMin), FieldType.Float),
		DEFINE.FIELD(nameof(CustomAutoExposureMax), FieldType.Float),
		DEFINE.FIELD(nameof(CustomBloomScale), FieldType.Float),
		DEFINE.FIELD(nameof(CustomBloomScaleMinimum), FieldType.Float),
		DEFINE.FIELD(nameof(UseCustomBloomScale), FieldType.Boolean),

		DEFINE.THINKFUNC(nameof(UpdateTonemapScaleBlend)),

		DEFINE.INPUTFUNC(FieldType.Float, "SetTonemapScale", nameof(InputSetTonemapScale), (INPUTFUNCPTR)((self, data) => ((EnvTonemapController)self).InputSetTonemapScale(data))),
		DEFINE.INPUTFUNC(FieldType.String, "BlendTonemapScale", nameof(InputBlendTonemapScale), (INPUTFUNCPTR)((self, data) => ((EnvTonemapController)self).InputBlendTonemapScale(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetTonemapRate", nameof(InputSetTonemapRate), (INPUTFUNCPTR)((self, data) => ((EnvTonemapController)self).InputSetTonemapRate(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetAutoExposureMin", nameof(InputSetAutoExposureMin), (INPUTFUNCPTR)((self, data) => ((EnvTonemapController)self).InputSetAutoExposureMin(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetAutoExposureMax", nameof(InputSetAutoExposureMax), (INPUTFUNCPTR)((self, data) => ((EnvTonemapController)self).InputSetAutoExposureMax(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "UseDefaultAutoExposure", nameof(InputUseDefaultAutoExposure), (INPUTFUNCPTR)((self, data) => ((EnvTonemapController)self).InputUseDefaultAutoExposure(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "UseDefaultBloomScale", nameof(InputUseDefaultBloomScale), (INPUTFUNCPTR)((self, data) => ((EnvTonemapController)self).InputUseDefaultBloomScale(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetBloomScale", nameof(InputSetBloomScale), (INPUTFUNCPTR)((self, data) => ((EnvTonemapController)self).InputSetBloomScale(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetBloomScaleRange", nameof(InputSetBloomScaleRange), (INPUTFUNCPTR)((self, data) => ((EnvTonemapController)self).InputSetBloomScaleRange(data))),
	]); public override DataMap? GetDataDescMap() => DataDesc;

	public override void Spawn() {
		SetSolid(SolidType.None);
		SetMoveType(Source.MoveType.None);
	}

	public override EdictFlags UpdateTransmitState() => SetTransmitState(EdictFlags.Always);

	public void InputSetTonemapScale(InputData inputdata) {
		float remapped = inputdata.Value.Float();
		mat_hdr_tonemapscale.SetValue(remapped);
	}

	public void InputBlendTonemapScale(InputData inputdata) {
		ReadOnlySpan<char> parseString = inputdata.Value.String();

		nexttoken(out ReadOnlySpan<char> param, parseString, ' ', out parseString);
		if (param.IsEmpty) {
			Warning($"{GetClassname()} ({GetDebugName()}) received BlendTonemapScale input without a target tonemap scale. Syntax: <target tonemap scale> <blend time>\n");
			return;
		}
		BlendTonemapEnd = strtof(param, out _);

		nexttoken(out param, parseString, ' ', out parseString);
		if (param.IsEmpty) {
			Warning($"{GetClassname()} ({GetDebugName()}) received BlendTonemapScale input without a blend time. Syntax: <target tonemap scale> <blend time>\n");
			return;
		}
		BlendEndTime = gpGlobals.CurTime + strtof(param, out _);

		BlendStartTime = gpGlobals.CurTime;
		BlendTonemapStart = mat_hdr_tonemapscale.GetFloat();

		SetNextThink(gpGlobals.CurTime + 0.1f);
		SetThink(UpdateTonemapScaleBlend);
	}

	public void InputSetBloomScaleRange(InputData inputdata) {
		int nargs = new ScanF(inputdata.Value.String(), "%f %f").Read(out float bloomMax).Read(out float bloomMin).ReadArguments;
		if (nargs != 2) {
			Warning($"{GetClassname()} ({GetDebugName()}) received SetBloomScaleRange input without 2 arguments. Syntax: <max bloom> <min bloom>\n");
			return;
		}
		CustomBloomScale = bloomMax;
		CustomBloomScaleMinimum = bloomMin;
	}

	static ConVarRef mat_hdr_manual_tonemap_rate;

	public void InputSetTonemapRate(InputData inputdata) {
		mat_hdr_manual_tonemap_rate.Init("mat_hdr_manual_tonemap_rate");

		if (mat_hdr_manual_tonemap_rate.IsValid()) {
			float tonemapRate = inputdata.Value.Float();
			mat_hdr_manual_tonemap_rate.SetValue(tonemapRate);
		}
	}

	public void UpdateTonemapScaleBlend() {
		float remapped = (float)MathLib.RemapValClamped(gpGlobals.CurTime, BlendStartTime, BlendEndTime, BlendTonemapStart, BlendTonemapEnd);
		mat_hdr_tonemapscale.SetValue(remapped);

		if (gpGlobals.CurTime >= BlendEndTime)
			return;

		SetNextThink(gpGlobals.CurTime + 0.1f);
	}

	public void InputSetAutoExposureMin(InputData inputdata) {
		CustomAutoExposureMin = inputdata.Value.Float();
		UseCustomAutoExposureMin = true;
	}

	public void InputSetAutoExposureMax(InputData inputdata) {
		CustomAutoExposureMax = inputdata.Value.Float();
		UseCustomAutoExposureMax = true;
	}

	public void InputUseDefaultAutoExposure(InputData inputdata) {
		UseCustomAutoExposureMin = false;
		UseCustomAutoExposureMax = false;
	}

	public void InputSetBloomScale(InputData inputdata) {
		CustomBloomScale = inputdata.Value.Float();
		CustomBloomScaleMinimum = CustomBloomScale;
		UseCustomBloomScale = true;
	}

	public void InputUseDefaultBloomScale(InputData inputdata) => UseCustomBloomScale = false;
}