using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

using DEFINE = Source.DEFINE<FogController>;

[LinkEntityToClass("env_fog_controller")]
[NetworkName("CFogController")]
public partial class FogController : BaseEntity
{
	public const int SF_FOG_MASTER = 0x0001;

	const int FOG_CONTROLLER_COLORPRIMARY_LERP = 1;
	const int FOG_CONTROLLER_COLORSECONDARY_LERP = 2;
	const int FOG_CONTROLLER_START_LERP = 4;
	const int FOG_CONTROLLER_END_LERP = 8;

	[NetworkVar("m_fog")]
	public partial ref readonly FogParams Fog { get; }
	[NetworkTable(NoBase = true)]
	public static readonly new NetworkProp[] Props = [
		nameof(Fog.Enable),
		nameof(Fog.Blend),
		nameof(Fog.Radial),
		new(nameof(Fog.DirPrimary)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Fog.ColorPrimary)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Fog.ColorSecondary)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Fog.ColorPrimaryHDR)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Fog.ColorSecondaryHDR)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Fog.Start)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Fog.End)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Fog.MaxDensity)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Fog.FarZ)) { Bits = 0, Flags = PropFlags.NoScale },

		new(nameof(Fog.ColorPrimaryLerpTo)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Fog.ColorSecondaryLerpTo)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Fog.StartLerpTo)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
		new(nameof(Fog.EndLerpTo)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
		new(nameof(Fog.MaxDensityLerpTo)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
		new(nameof(Fog.LerpTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
		new(nameof(Fog.Duration)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
		new(nameof(Fog.HDRColorScale)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
	];

	public bool UseAngles;
	public int ChangedVariables;

	public static readonly new DataMap DataDesc = new(typeof(FogController), BaseEntity.DataDesc, [
		DEFINE.INPUTFUNC(FieldType.Float, "SetStartDist", nameof(InputSetStartDist), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputSetStartDist(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetEndDist", nameof(InputSetEndDist), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputSetEndDist(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetMaxDensity", nameof(InputSetMaxDensity), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputSetMaxDensity(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "TurnOn", nameof(InputTurnOn), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputTurnOn(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "TurnOff", nameof(InputTurnOff), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputTurnOff(data))),
		DEFINE.INPUTFUNC(FieldType.Color32, "SetColor", nameof(InputSetColor), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputSetColor(data))),
		DEFINE.INPUTFUNC(FieldType.Color32, "SetColorSecondary", nameof(InputSetColorSecondary), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputSetColorSecondary(data))),
		DEFINE.INPUTFUNC(FieldType.Integer, "SetFarZ", nameof(InputSetFarZ), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputSetFarZ(data))),
		DEFINE.INPUTFUNC(FieldType.String, "SetAngles", nameof(InputSetAngles), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputSetAngles(data))),
		DEFINE.INPUTFUNC(FieldType.Boolean, "SetRadial", nameof(InputSetRadial), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputSetRadial(data))),

		DEFINE.INPUTFUNC(FieldType.Color32, "SetColorLerpTo", nameof(InputSetColorLerpTo), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputSetColorLerpTo(data))),
		DEFINE.INPUTFUNC(FieldType.Color32, "SetColorSecondaryLerpTo", nameof(InputSetColorSecondaryLerpTo), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputSetColorSecondaryLerpTo(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetStartDistLerpTo", nameof(InputSetStartDistLerpTo), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputSetStartDistLerpTo(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetEndDistLerpTo", nameof(InputSetEndDistLerpTo), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputSetEndDistLerpTo(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "StartFogTransition", nameof(InputStartFogTransition), (INPUTFUNCPTR)((self, data) => ((FogController)self).InputStartFogTransition(data))),

		DEFINE.KEYFIELD(nameof(UseAngles), FieldType.Boolean, "use_angles"),
		DEFINE.KEYFIELD("Fog.ColorPrimary", FieldType.Color32, "fogcolor"),
		DEFINE.KEYFIELD("Fog.ColorSecondary", FieldType.Color32, "fogcolor2"),
		DEFINE.KEYFIELD("Fog.DirPrimary", FieldType.Vector, "fogdir"),
		DEFINE.KEYFIELD("Fog.Enable", FieldType.Boolean, "fogenable"),
		DEFINE.KEYFIELD("Fog.Blend", FieldType.Boolean, "fogblend"),
		DEFINE.KEYFIELD("Fog.Start", FieldType.Float, "fogstart"),
		DEFINE.KEYFIELD("Fog.End", FieldType.Float, "fogend"),
		DEFINE.KEYFIELD("Fog.MaxDensity", FieldType.Float, "fogmaxdensity"),
		DEFINE.KEYFIELD("Fog.FarZ", FieldType.Float, "farz"),
		DEFINE.KEYFIELD("Fog.Duration", FieldType.Float, "foglerptime"),
		DEFINE.KEYFIELD("Fog.Radial", FieldType.Boolean, "fogradial"),

		DEFINE.THINKFUNC(nameof(SetLerpValues)),

		DEFINE.FIELD(nameof(ChangedVariables), FieldType.Integer),

		DEFINE.FIELD("Fog.LerpTime", FieldType.Time),
		DEFINE.FIELD("Fog.ColorPrimaryLerpTo", FieldType.Color32),
		DEFINE.FIELD("Fog.ColorSecondaryLerpTo", FieldType.Color32),
		DEFINE.FIELD("Fog.StartLerpTo", FieldType.Float),
		DEFINE.FIELD("Fog.EndLerpTo", FieldType.Float),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public FogController() {
		// Make sure that old maps without fog fields don't get wacked out fog values.
		FogForModify().Enable = false;
		FogForModify().MaxDensity = 1.0f;
	}

	public override void Spawn() {
		base.Spawn();

		ref FogParams fog = ref FogForModify();
		fog.ColorPrimaryLerpTo = fog.ColorPrimary;
		fog.ColorSecondaryLerpTo = fog.ColorSecondary;
	}

	public override void Activate() {
		base.Activate();

		if (UseAngles) {
			ref FogParams fog = ref FogForModify();
			MathLib.AngleVectors(GetAbsAngles(), out fog.DirPrimary);
			fog.DirPrimary *= -1.0f;
		}
	}

	public override EdictFlags UpdateTransmitState() => SetTransmitState(EdictFlags.Always);

	public bool IsMaster() => HasSpawnFlags(SF_FOG_MASTER);

	public void InputSetStartDist(InputData inputdata) => FogForModify().Start = inputdata.Value.Float();

	public void InputSetEndDist(InputData inputdata) => FogForModify().End = inputdata.Value.Float();

	// Input handler for setting the maximum density of the fog. This lets us bring
	// the start distance in without the scene fogging too much.
	public void InputSetMaxDensity(InputData inputdata) => FogForModify().MaxDensity = inputdata.Value.Float();

	public void InputTurnOn(InputData inputdata) => FogForModify().Enable = true;

	public void InputTurnOff(InputData inputdata) => FogForModify().Enable = false;

	public void InputSetColor(InputData inputdata) => FogForModify().ColorPrimary = inputdata.Value.Color32();

	public void InputSetColorSecondary(InputData inputdata) => FogForModify().ColorSecondary = inputdata.Value.Color32();

	public void InputSetFarZ(InputData inputdata) => FogForModify().FarZ = inputdata.Value.Int();

	public void InputSetAngles(InputData inputdata) {
		Span<float> anglesBase = stackalloc float[3];
		UTIL_StringToVector(anglesBase, inputdata.Value.String());
		QAngle angles = new(anglesBase[0], anglesBase[1], anglesBase[2]);

		SetAbsAngles(angles);

		ref FogParams fog = ref FogForModify();
		MathLib.AngleVectors(GetAbsAngles(), out fog.DirPrimary);
		fog.DirPrimary *= -1.0f;
	}

	public void InputSetRadial(InputData inputdata) => FogForModify().Radial = inputdata.Value.Bool();

	public override int DrawDebugTextOverlays() {
		int textOffset = base.DrawDebugTextOverlays();

		if ((DebugOverlays & DebugOverlayBits.Text) != 0) {
			ref readonly FogParams fog = ref Fog;

			EntityText(textOffset++, $"State: {(fog.Enable ? "On" : "Off")}", 0);
			EntityText(textOffset++, $"Start: {fog.Start,3:0}", 0);
			EntityText(textOffset++, $"End  : {fog.End,3:0}", 0);

			Color color = fog.ColorPrimary;
			EntityText(textOffset++, $"1) Red  : {color.R}", 0);
			EntityText(textOffset++, $"1) Green: {color.G}", 0);
			EntityText(textOffset++, $"1) Blue : {color.B}", 0);

			color = fog.ColorSecondary;
			EntityText(textOffset++, $"2) Red  : {color.R}", 0);
			EntityText(textOffset++, $"2) Green: {color.G}", 0);
			EntityText(textOffset++, $"2) Blue : {color.B}", 0);
		}

		return textOffset;
	}

	public void InputSetColorLerpTo(InputData inputdata) {
		ChangedVariables |= FOG_CONTROLLER_COLORPRIMARY_LERP;
		FogForModify().ColorPrimaryLerpTo = inputdata.Value.Color32();
	}

	public void InputSetColorSecondaryLerpTo(InputData inputdata) {
		ChangedVariables |= FOG_CONTROLLER_COLORSECONDARY_LERP;
		FogForModify().ColorSecondaryLerpTo = inputdata.Value.Color32();
	}

	public void InputSetStartDistLerpTo(InputData inputdata) {
		ChangedVariables |= FOG_CONTROLLER_START_LERP;
		FogForModify().StartLerpTo = inputdata.Value.Float();
	}

	public void InputSetEndDistLerpTo(InputData inputdata) {
		ChangedVariables |= FOG_CONTROLLER_END_LERP;
		FogForModify().EndLerpTo = inputdata.Value.Float();
	}

	public void InputStartFogTransition(InputData inputdata) {
		SetThink(SetLerpValues);

		ref FogParams fog = ref FogForModify();
		fog.LerpTime = gpGlobals.CurTime + fog.Duration + 0.1;
		SetNextThink(gpGlobals.CurTime + fog.Duration);
	}

	public void SetLerpValues() {
		ref FogParams fog = ref FogForModify();

		if ((ChangedVariables & FOG_CONTROLLER_COLORPRIMARY_LERP) != 0)
			fog.ColorPrimary = fog.ColorPrimaryLerpTo;

		if ((ChangedVariables & FOG_CONTROLLER_COLORSECONDARY_LERP) != 0)
			fog.ColorSecondary = fog.ColorSecondaryLerpTo;

		if ((ChangedVariables & FOG_CONTROLLER_START_LERP) != 0)
			fog.Start = fog.StartLerpTo;

		if ((ChangedVariables & FOG_CONTROLLER_END_LERP) != 0)
			fog.End = fog.EndLerpTo;

		ChangedVariables = 0;
		fog.LerpTime = gpGlobals.CurTime;
	}
}

public class FogSystem(ReadOnlySpan<char> name) : AutoGameSystem(name)
{
	public static readonly FogSystem s_FogSystem = new("FogSystem");

	FogController? MasterController;

	// Clear out the fog controller.
	public override void LevelInitPreEntity() {
		MasterController = null;
	}

	// On level load find the master fog controller.  If no controller is
	// set as Master, use the first fog controller found.
	public override void LevelInitPostEntity() {
		FogController? fogController = null;
		do {
			fogController = (FogController?)gEntList.FindEntityByClassname(fogController, "env_fog_controller");
			if (fogController != null) {
				// If this is an official map then force on radial fog.
				if (g_pGameRules.IsOfficialMap())
					fogController.FogForModify().Radial = true;

				if (MasterController == null)
					MasterController = fogController;
				else {
					if (fogController.IsMaster())
						MasterController = fogController;
				}
			}
		} while (fogController != null);

		// HACK: Singleplayer games don't get a call to CBasePlayer::Spawn on level transitions.
		// CBasePlayer::Activate is called before this is called so that's too soon to set up the fog controller.
		// We don't have a hook similar to Activate that happens after LevelInitPostEntity
		// is called, or we could just do this in the player itself.
		if (gpGlobals.MaxClients == 1) {
			BasePlayer? player = Util.GetLocalPlayer();
			if (player != null && player.Local.PlayerFog.Ctrl.Get() == null)
				player.InitFogController();
		}
	}

	public FogController? GetMasterFogController() => MasterController;
}
