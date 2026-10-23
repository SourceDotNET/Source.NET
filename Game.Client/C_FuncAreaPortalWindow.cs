using Source.Common;
using Source.Common.Engine;
using Source.Common.Mathematics;
using Source;

using Game.Shared;

namespace Game.Client;


[NetworkName("CFuncAreaPortalWindow")]
public partial class C_FuncAreaPortalWindow : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(FadeStartDist)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeDist)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(TranslucencyLimit)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(BackgroundModelIndex)) { Encoding = NetEncoding.ModelIndex },
	];

	[NetworkVar("m_flFadeStartDist")]
	public float FadeStartDist;
	[NetworkVar("m_flFadeDist")]
	public float FadeDist;
	[NetworkVar("m_flTranslucencyLimit")]
	public float TranslucencyLimit;
	[NetworkVar("m_iBackgroundModelIndex")]
	public int BackgroundModelIndex;

	public override void ComputeFxBlend() {
		RenderFXBlend = 255;
	}

	public override bool IsTransparent() => true;

	public override int DrawModel(StudioFlags flags) {
		if (!ReadyToDraw)
			return 0;

		Model? model = GetModel();
		if (model == null)
			return 0;

		if (modelinfo.GetModelType(model) != ModelType.Brush)
			return 0;

		render.SetBlend(GetDistanceBlend());

		DrawBrushModelMode mode = DrawBrushModelMode.DrawAll;
		if ((flags & StudioFlags.TwoPass) != 0)
			mode = ((flags & StudioFlags.Transparency) != 0) ? DrawBrushModelMode.DrawTranslucentOnly : DrawBrushModelMode.DrawOpaqueOnly;

		render.DrawBrushModelEx(this, model, GetAbsOrigin(), GetAbsAngles(), mode);

		if (BackgroundModelIndex >= 0) {
			render.SetBlend(1);
			Model? background = modelinfo.GetModel(BackgroundModelIndex);
			if (background != null && modelinfo.GetModelType(background) == ModelType.Brush)
				render.DrawBrushModelEx(this, background, GetAbsOrigin(), GetAbsAngles(), mode);
		}

		return 1;
	}

	float GetDistanceBlend() {
		float dist = CollisionProp().CalcDistanceFromPoint(CurrentViewOrigin());
		C_BasePlayer? local = C_BasePlayer.GetLocalPlayer();
		if (local != null)
			dist *= local.GetFOVDistanceAdjustFactor();

		return (float)MathLib.RemapValClamped(dist, FadeStartDist, FadeDist, TranslucencyLimit, 1);
	}

	public override bool ShouldReceiveProjectedTextures(ShadowFlags flags) => false;
}
