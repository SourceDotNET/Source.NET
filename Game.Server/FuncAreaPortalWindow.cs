using Source.Common;
using Source;

using Game.Shared;

using System.Numerics;

namespace Game.Server;

using DEFINE = DEFINE<FuncAreaPortalWindow>;

[LinkEntityToClass("func_areaportalwindow")]
[NetworkName("CFuncAreaPortalWindow")]
public partial class FuncAreaPortalWindow : FuncAreaPortalBase
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(FadeDist)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeStartDist)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(TranslucencyLimit)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BackgroundModelIndex)) { Encoding = NetEncoding.ModelIndex },
	];

	const float FADE_DIST_BUFFER = 10;


	public static readonly new DataMap DataDesc = new(typeof(FuncAreaPortalWindow), FuncAreaPortalBase.DataDesc, [
		DEFINE.KEYFIELD(nameof(PortalNumber), FieldType.Integer, "portalnumber"),
		DEFINE.KEYFIELD(nameof(FadeStartDist), FieldType.Float, "FadeStartDist"),
		DEFINE.KEYFIELD(nameof(FadeDist), FieldType.Float, "FadeDist"),
		DEFINE.KEYFIELD(nameof(TranslucencyLimit), FieldType.Float, "TranslucencyLimit"),
		DEFINE.KEYFIELD(nameof(BackgroundBModelName), FieldType.String, "BackgroundBModel"),

		DEFINE.INPUTFUNC(FieldType.Float, "SetFadeStartDistance", nameof(InputSetFadeStartDistance), (INPUTFUNCPTR)((self, data) => ((FuncAreaPortalWindow)self).InputSetFadeStartDistance(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetFadeEndDistance", nameof(InputSetFadeEndDistance), (INPUTFUNCPTR)((self, data) => ((FuncAreaPortalWindow)self).InputSetFadeEndDistance(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	[NetworkVar("m_flFadeDist")]
	public partial float FadeDist { get; set; }
	[NetworkVar("m_flFadeStartDist")]
	public partial float FadeStartDist { get; set; }
	[NetworkVar("m_flTranslucencyLimit")]
	public partial float TranslucencyLimit { get; set; }
	[NetworkVar("m_iBackgroundModelIndex")]
	public partial int BackgroundModelIndex { get; set; }

	string? BackgroundBModelName;

	public FuncAreaPortalWindow() {
		BackgroundModelIndex = -1;
	}

	public override void Spawn() {
		Precache();

		engine.SetAreaPortalState(PortalNumber, 1);
	}

	public override void Activate() {
		base.Activate();

		BaseEntity? background = gEntList.FindEntityByName(null, BackgroundBModelName);
		if (background != null) {
			BackgroundModelIndex = modelinfo.GetModelIndex(background.GetModelName());
			background.AddEffects(EntityEffects.NoDraw);
		}

		BaseEntity? target = gEntList.FindEntityByName(null, Target);
		if (target != null) {
			SetModel(target.GetModelName());
			SetAbsOrigin(target.GetAbsOrigin());
			target.AddEffects(EntityEffects.NoDraw);
		}
	}

	public bool IsWindowOpen(in Vector3 origin, float fovDistanceAdjustFactor) {
		float dist = CollisionProp().CalcDistanceFromPoint(origin);
		dist *= fovDistanceAdjustFactor;
		return dist <= (FadeDist + FADE_DIST_BUFFER);
	}

	public override bool UpdateVisibility(in Vector3 origin, float fovDistanceAdjustFactor, ref bool isOpenOnClient) {
		if (IsWindowOpen(origin, fovDistanceAdjustFactor))
			return base.UpdateVisibility(origin, fovDistanceAdjustFactor, ref isOpenOnClient);
		else {
			isOpenOnClient = false;
			return false;
		}
	}

	public void InputSetFadeStartDistance(InputData inputdata) => FadeStartDist = inputdata.Value.Float();

	public void InputSetFadeEndDistance(InputData inputdata) => FadeDist = inputdata.Value.Float();
}
