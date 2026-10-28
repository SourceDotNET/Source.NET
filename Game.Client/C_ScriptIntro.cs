using Game.Shared;

using Source.Common;
using Source.Common.Mathematics;
using Source;
using System.Numerics;

namespace Game.Client;

[NetworkName("CScriptIntro")]
public partial class C_ScriptIntro : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(CameraView)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(CameraViewAngles)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(BlendMode)) { Bits = 5 },
		new(nameof(NextBlendMode)) { Bits = 5 },
		new(nameof(NextBlendTime)) { Encoding = NetEncoding.Time64 },
		new(nameof(BlendStartTime)) { Encoding = NetEncoding.Time64 },
		nameof(Active),
		new(nameof(FOV)) { Bits = 9 },
		new(nameof(NextFOV)) { Bits = 9 },
		new(nameof(StartFOV)) { Bits = 9 },
		new(nameof(NextFOVBlendTime)) { Encoding = NetEncoding.Time64 },
		new(nameof(FOVBlendStartTime)) { Encoding = NetEncoding.Time64 },
		nameof(AlternateFOV),
		new(nameof(FadeAlpha)) { Bits = 10 },
		new(nameof(FadeColor)) { Encoding = NetEncoding.Array2, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeDuration)) { Bits = 10, Flags = PropFlags.RoundDown, Low = 0.0f, High = 255.0f },
		nameof(CameraEntity),
	];

	[NetworkVar("m_vecCameraView")]
	public Vector3 CameraView;
	[NetworkVar("m_vecCameraViewAngles")]
	public Vector3 CameraViewAngles;
	[NetworkVar("m_iBlendMode")]
	public int BlendMode;
	[NetworkVar("m_iNextBlendMode")]
	public int NextBlendMode;
	[NetworkVar("m_flNextBlendTime")]
	public TimeUnit_t NextBlendTime;
	[NetworkVar("m_flBlendStartTime")]
	public TimeUnit_t BlendStartTime;
	[NetworkVar("m_bActive")]
	public bool Active;
	[NetworkVar("m_iFOV")]
	public int FOV;
	[NetworkVar("m_iNextFOV")]
	public int NextFOV;
	[NetworkVar("m_iStartFOV")]
	public int StartFOV;
	[NetworkVar("m_flNextFOVBlendTime")]
	public TimeUnit_t NextFOVBlendTime;
	[NetworkVar("m_flFOVBlendStartTime")]
	public TimeUnit_t FOVBlendStartTime;
	[NetworkVar("m_bAlternateFOV")]
	public bool AlternateFOV;
	[NetworkVar("m_flFadeAlpha")]
	public float FadeAlpha;
	[NetworkVar("m_flFadeColor")]
	public InlineArray3<float> FadeColor;
	[NetworkVar("m_flFadeDuration")]
	public float FadeDuration;
	[NetworkVar("m_hCameraEntity")]
	public EHANDLE CameraEntity = new();

	int PrevFOV;
	readonly IntroData IntroData = new();

	float PrevServerFadeAlpha;
	TimeUnit_t FadeTimeStartedAt;
	float FadeAlphaStartedAt;

	public C_ScriptIntro() {
		Active = false;
		CameraView = vec3_origin;
		CameraViewAngles = vec3_origin;
		BlendMode = 0;
		NextBlendMode = 0;
		NextBlendTime = 0;
		BlendStartTime = 0;
		IntroData.PlayerViewFOV = 0;
		FadeAlpha = 0;
		PrevServerFadeAlpha = 0;
		FadeDuration = 0;
		FadeTimeStartedAt = 0;
		FadeAlphaStartedAt = 0;
		CameraEntity.Set(null);
		PrevFOV = 0;
		StartFOV = 0;

		IntroData.g_pIntroData = null;

		IntroData.CurrentFadeColor[0] = FadeColor[0];
		IntroData.CurrentFadeColor[1] = FadeColor[1];
		IntroData.CurrentFadeColor[2] = FadeColor[2];
		IntroData.CurrentFadeColor[3] = FadeAlpha;
	}

	public override void Release() {
		IntroData.g_pIntroData = null;
		base.Release();
	}

	public override void PostDataUpdate(DataUpdateType updateType) {
		base.PostDataUpdate(updateType);

		SetNextClientThink(CLIENT_THINK_ALWAYS);

		IntroData.CameraView = CameraView;
		IntroData.CameraViewAngles = new(CameraViewAngles.X, CameraViewAngles.Y, CameraViewAngles.Z);

		IntroData.Passes.Clear();

		IntroData.Passes.Add(new() { BlendMode = BlendMode, Alpha = 1.0f });

		IntroData.DrawPrimary = CameraView != vec3_origin;

		// If we're currently blending to a new mode, set the second pass
		if (NextBlendTime > gpGlobals.CurTime)
			IntroData.Passes.Add(new() { BlendMode = NextBlendMode, Alpha = 0.0f });
		else if (IntroData.Passes.Count == 2) {
			C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
			if (player == null)
				return;

			IntroData.Passes.RemoveAt(1);
		}

		if (Active)
			IntroData.g_pIntroData = IntroData;
		else if (IntroData.g_pIntroData == IntroData)
			IntroData.g_pIntroData = null;

		IntroData.CurrentFadeColor[0] = FadeColor[0];
		IntroData.CurrentFadeColor[1] = FadeColor[1];
		IntroData.CurrentFadeColor[2] = FadeColor[2];

		if (FadeAlpha != PrevServerFadeAlpha) {
			FadeTimeStartedAt = gpGlobals.CurTime;
			FadeAlphaStartedAt = IntroData.CurrentFadeColor[3];
			PrevServerFadeAlpha = FadeAlpha;

			if (FadeDuration == 0)
				FadeDuration = 0.01f;
		}

		if (PrevFOV != FOV) {
			IntroData.PlayerViewFOV = FOV;
			PrevFOV = FOV;
		}
	}

	public override void ClientThink() {
		Assert(IntroData.Passes.Count <= 2);

		if (CameraEntity.Get() != null) {
			IntroData.CameraView = CameraEntity.Get()!.GetAbsOrigin();
			IntroData.CameraViewAngles = CameraEntity.Get()!.GetAbsAngles();
		}

		CalculateFOV();
		CalculateAlpha();

		float perc = 1.0f;
		if ((NextBlendTime - BlendStartTime) != 0)
			perc = (float)Math.Clamp((gpGlobals.CurTime - BlendStartTime) / (NextBlendTime - BlendStartTime), 0, 1);

		var passes = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(IntroData.Passes);

		if (perc >= 1.0f) {
			if (passes.Length == 2) {
				passes[0].BlendMode = passes[1].BlendMode;
				passes[0].Alpha = 1.0f;
				IntroData.Passes.RemoveAt(1);
			}
			else
				passes[0].Alpha = 1.0f;

			return;
		}

		if (passes.Length == 2) {
			passes[0].Alpha = 1.0f - perc;
			passes[1].Alpha = perc;
		}
		else
			passes[0].Alpha = 1.0f - perc;
	}

	void CalculateFOV() {
		if (NextFOVBlendTime >= gpGlobals.CurTime)
			IntroData.PlayerViewFOV = ScriptIntroShared.ScriptInfo_CalculateFOV(FOVBlendStartTime, NextFOVBlendTime, StartFOV, NextFOV, AlternateFOV);
	}

	void CalculateAlpha() {
		float newAlpha = (float)MathLib.RemapValClamped(gpGlobals.CurTime, FadeTimeStartedAt, FadeTimeStartedAt + FadeDuration, FadeAlphaStartedAt, FadeAlpha);
		IntroData.CurrentFadeColor[3] = newAlpha;
	}
}
