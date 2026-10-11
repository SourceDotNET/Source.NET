global using static Game.Client.ViewPostProcess;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Engine;

using System.Numerics;

namespace Game.Client;

[EngineComponent]
public static class ViewPostProcess
{
	public static bool g_bUseCustomAutoExposureMin = false;
	public static bool g_bUseCustomAutoExposureMax = false;
	public static bool g_bUseCustomBloomScale = false;
	public static float g_flCustomAutoExposureMin = 0;
	public static float g_flCustomAutoExposureMax = 0;
	public static float g_flCustomBloomScale = 0.0f;
	public static float g_flCustomBloomScaleMinimum = 0.0f;

	public static bool g_bFlashlightIsOn = false;

	internal static IMaterialSystemHardwareConfig HardwareConfig => field ??= Singleton<IMaterialSystemHardwareConfig>();

	public static readonly ConVar mat_bloomscale = new("mat_bloomscale", "1");

	public static readonly ConVar mat_bloomamount_rate = new("mat_bloomamount_rate", "0.05f", FCvar.Cheat);
	static readonly ConVar debug_postproc = new("mat_debug_postprocessing_effects", "0", FCvar.None, "0 = off, 1 = show post-processing passes in quadrants of the screen, 2 = only apply post-processing to the centre of the screen");
	static readonly ConVar mat_postprocessing_combine = new("mat_postprocessing_combine", "1", FCvar.None, "Combine bloom, software anti-aliasing and color correction into one post-processing pass");
	static readonly ConVar mat_dynamic_tonemapping = new("mat_dynamic_tonemapping", "1", FCvar.Cheat);
	static readonly ConVar mat_tonemapping_occlusion_use_stencil = new("mat_tonemapping_occlusion_use_stencil", "0");
	public static readonly ConVar mat_debug_autoexposure = new("mat_debug_autoexposure", "0", FCvar.Cheat);
	static readonly ConVar mat_autoexposure_max = new("mat_autoexposure_max", "2");
	static readonly ConVar mat_autoexposure_min = new("mat_autoexposure_min", "0.5");
	public static readonly ConVar mat_hdr_tonemapscale = new("mat_hdr_tonemapscale", "1.0", FCvar.Cheat);
	public static readonly ConVar mat_hdr_uncapexposure = new("mat_hdr_uncapexposure", "0", FCvar.Cheat);
	public static readonly ConVar mat_force_bloom = new("mat_force_bloom", "0", FCvar.Cheat);
	public static readonly ConVar mat_disable_bloom = new("mat_disable_bloom", "0");
	public static readonly ConVar mat_debug_bloom = new("mat_debug_bloom", "0", FCvar.Cheat);

	public static readonly ConVar mat_non_hdr_bloom_scalefactor = new("mat_non_hdr_bloom_scalefactor", ".3");
	static readonly ConVar mat_bloom_scalefactor_scalar = new("mat_bloom_scalefactor_scalar", "0.5");

	public static readonly ConVar mat_exposure_center_region_x = new("mat_exposure_center_region_x", "0.9", FCvar.Cheat);
	public static readonly ConVar mat_exposure_center_region_y = new("mat_exposure_center_region_y", "0.85", FCvar.Cheat);
	public static readonly ConVar mat_exposure_center_region_x_flashlight = new("mat_exposure_center_region_x_flashlight", "0.9", FCvar.Cheat);
	public static readonly ConVar mat_exposure_center_region_y_flashlight = new("mat_exposure_center_region_y_flashlight", "0.85", FCvar.Cheat);

	public static readonly ConVar mat_tonemap_algorithm = new("mat_tonemap_algorithm", "1", FCvar.Cheat, "0 = Original Algorithm 1 = New Algorithm");
	public static readonly ConVar mat_tonemap_percent_target = new("mat_tonemap_percent_target", "60.0", FCvar.Cheat);
	public static readonly ConVar mat_tonemap_percent_bright_pixels = new("mat_tonemap_percent_bright_pixels", "2.0", FCvar.Cheat);
	public static readonly ConVar mat_tonemap_min_avglum = new("mat_tonemap_min_avglum", "3.0", FCvar.Cheat);

	static readonly ConVar mat_force_tonemap_scale = new("mat_force_tonemap_scale", "0.0", FCvar.Cheat);

	static readonly ConVar mat_postprocess_x = new("mat_postprocess_x", "4");
	static readonly ConVar mat_postprocess_y = new("mat_postprocess_y", "1");

	static void SetRenderTargetAndViewPort(ITexture rt) {
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetRenderTarget(rt);
		renderContext.Viewport(0, 0, rt.GetActualWidth(), rt.GetActualHeight());
	}

	enum HistogramEntryState
	{
		Initial = 0,
		FirstQueryInFlight,
		QueryInFlight,
		QueryDone,
	}

	const int N_LUMINANCE_RANGES = 31;
	const int N_LUMINANCE_RANGES_NEW = 17;
	const int MAX_QUERIES_PER_FRAME = 1;

	class HistogramEntry
	{
		public HistogramEntryState State;
		public OcclusionQueryObjectHandle_t OccHandle;
		public int FrameQueued;
		public int NPixels;
		public int NPixelsInRange;
		public float MinLum, MaxLum;
		public float MinX, MinY, MaxX, MaxY;

		public bool ContainsValidData() => (State == HistogramEntryState.QueryDone) || (State == HistogramEntryState.QueryInFlight);

		public void IssueQuery(int frmNum) {
			using MatRenderContextPtr renderContext = new(materials);
			if (OccHandle == 0)
				OccHandle = renderContext.CreateOcclusionQueryObject();

			renderContext.GetViewport(out int xl, out int yl, out int destWidth, out int destHeight);

			float testRangeMin = MinLum;
			float testRangeMax = (MaxLum == 1.0f) ? 10000.0f : MaxLum;

			IMaterial testMat = materials.FindMaterial("dev/lumcompare", MaterialDefines.TEXTURE_GROUP_OTHER, true)!;
			IMaterialVar minVar = testMat.FindVar("$C0_X", out _);
			minVar.SetFloatValue(testRangeMin);
			IMaterialVar maxVar = testMat.FindVar("$C0_Y", out _);
			maxVar.SetFloatValue(testRangeMax);
			int scrxMin = (int)MathLib.Lerp(xl, (xl + destWidth - 1), 0, 1, MinX);
			int scrxMax = (int)MathLib.Lerp(xl, (xl + destWidth - 1), 0, 1, MaxX);
			int scryMin = (int)MathLib.Lerp(yl, (yl + destHeight - 1), 0, 1, MinY);
			int scryMax = (int)MathLib.Lerp(yl, (yl + destHeight - 1), 0, 1, MaxY);

			float exposureWidthScale, exposureHeightScale;

			if (g_bFlashlightIsOn) {
				exposureWidthScale = 0.5f * (1.0f - mat_exposure_center_region_x_flashlight.GetFloat());
				exposureHeightScale = 0.5f * (1.0f - mat_exposure_center_region_y_flashlight.GetFloat());
			}
			else {
				exposureWidthScale = 0.5f * (1.0f - mat_exposure_center_region_x.GetFloat());
				exposureHeightScale = 0.5f * (1.0f - mat_exposure_center_region_y.GetFloat());
			}
			int skipEdgeX = (int)((1 + scrxMax - scrxMin) * exposureWidthScale);
			int skipEdgeY = (int)((1 + scryMax - scryMin) * exposureHeightScale);

			float tscale = 1.0f;
			if (HardwareConfig.GetHDRType() == HDRType.Float)
				tscale = renderContext.GetToneMappingScaleLinear().X;
			IMaterialVar useTScale = testMat.FindVar("$C0_Z", out _);
			useTScale.SetFloatValue(tscale);

			NPixels = (1 + scrxMax - scrxMin) * (1 + scryMax - scryMin);

			if (mat_tonemapping_occlusion_use_stencil.GetInt() != 0) {
				renderContext.SetStencilWriteMask(1);

				renderContext.SetStencilEnable(true);
				renderContext.SetStencilPassOperation(StencilOperation.Replace);
				renderContext.SetStencilCompareFunction(StencilComparisonFunction.Always);
				renderContext.SetStencilFailOperation(StencilOperation.Keep);
				renderContext.SetStencilZFailOperation(StencilOperation.Keep);
				renderContext.SetStencilReferenceValue(1);
			}
			else
				renderContext.BeginOcclusionQueryDrawing(OccHandle);

			scrxMin += skipEdgeX;
			scryMin += skipEdgeY;
			scrxMax -= skipEdgeX;
			scryMax -= skipEdgeY;
			Singleton<RenderUtils>().DrawScreenSpaceRectangle(testMat,
				scrxMin, scryMin,
				1 + scrxMax - scrxMin,
				1 + scryMax - scryMin,
				scrxMin, scryMin,
				scrxMax, scryMax,
				destWidth, destHeight, null, 1, 1, 0);

			if (mat_tonemapping_occlusion_use_stencil.GetInt() != 0) {
				renderContext.BeginOcclusionQueryDrawing(OccHandle);

				renderContext.SetStencilEnable(true);
				renderContext.SetStencilTestMask(1);
				renderContext.SetStencilPassOperation(StencilOperation.Keep);
				renderContext.SetStencilCompareFunction(StencilComparisonFunction.Equal);
				renderContext.SetStencilFailOperation(StencilOperation.Keep);
				renderContext.SetStencilZFailOperation(StencilOperation.Keep);
				renderContext.SetStencilReferenceValue(1);
				IMaterial stestMat = materials.FindMaterial("dev/no_pixel_write", MaterialDefines.TEXTURE_GROUP_OTHER, true)!;
				Singleton<RenderUtils>().DrawScreenSpaceRectangle(stestMat,
					scrxMin, scryMin,
					1 + scrxMax - scrxMin,
					1 + scryMax - scryMin,
					scrxMin, scryMin,
					scrxMax, scryMax,
					destWidth, destHeight, null, 1, 1, 0);
				renderContext.SetStencilEnable(false);
			}
			renderContext.EndOcclusionQueryDrawing(OccHandle);
			if (State == HistogramEntryState.Initial)
				State = HistogramEntryState.FirstQueryInFlight;
			else
				State = HistogramEntryState.QueryInFlight;
			FrameQueued = frmNum;
		}
	}

	class LuminanceHistogramSystem
	{
		readonly HistogramEntry[] CurHistogram = new HistogramEntry[N_LUMINANCE_RANGES];
		int CurQueryFrame;

		public LuminanceHistogramSystem() {
			for (int i = 0; i < CurHistogram.Length; i++)
				CurHistogram[i] = new();
			UpdateLuminanceRanges();
		}

		public void Update() {
			UpdateLuminanceRanges();

			int nQueriesIssuedThisFrame = 0;
			CurQueryFrame++;

			int numRanges = N_LUMINANCE_RANGES;
			if (mat_tonemap_algorithm.GetInt() == 1)
				numRanges = N_LUMINANCE_RANGES_NEW;

			for (int i = 0; i < numRanges; i++) {
				switch (CurHistogram[i].State) {
					case HistogramEntryState.Initial:
						if (nQueriesIssuedThisFrame < MAX_QUERIES_PER_FRAME) {
							CurHistogram[i].IssueQuery(CurQueryFrame);
							nQueriesIssuedThisFrame++;
						}
						break;

					case HistogramEntryState.FirstQueryInFlight:
					case HistogramEntryState.QueryInFlight:
						if (CurQueryFrame > CurHistogram[i].FrameQueued + 2) {
							using MatRenderContextPtr renderContext = new(materials);
							int np = renderContext.OcclusionQuery_GetNumPixelsRendered(CurHistogram[i].OccHandle);
							if (np != -1) {
								CurHistogram[i].NPixelsInRange = np;
								CurHistogram[i].State = HistogramEntryState.QueryDone;
							}
						}
						break;
				}
			}

			while (nQueriesIssuedThisFrame < MAX_QUERIES_PER_FRAME) {
				numRanges = N_LUMINANCE_RANGES;
				if (mat_tonemap_algorithm.GetInt() == 1)
					numRanges = N_LUMINANCE_RANGES_NEW;

				int oldestSoFar = -1;
				for (int i = 0; i < numRanges; i++)
					if ((CurHistogram[i].State == HistogramEntryState.QueryDone) &&
						((oldestSoFar == -1) ||
						 (CurHistogram[i].FrameQueued <
						  CurHistogram[oldestSoFar].FrameQueued)))
						oldestSoFar = i;
				if (oldestSoFar == -1)
					break;
				CurHistogram[oldestSoFar].IssueQuery(CurQueryFrame);
				nQueriesIssuedThisFrame++;
			}
		}

		public float FindLocationOfPercentBrightPixels(float percentBrightPixels, float percentTargetToSnapToIfInSameBin = -1.0f) {
			if (mat_tonemap_algorithm.GetInt() == 1) {
				int totalValidPixels = 0;
				for (int i = 0; i < N_LUMINANCE_RANGES_NEW - 1; i++) {
					if (CurHistogram[i].ContainsValidData())
						totalValidPixels += CurHistogram[i].NPixelsInRange;
				}

				if (totalValidPixels == 0)
					return -1.0f;

				float totalPercentRangeTested = 0.0f;
				float totalPercentPixelsTested = 0.0f;
				for (int i = N_LUMINANCE_RANGES_NEW - 2; i >= 0; i--) {
					if (!CurHistogram[i].ContainsValidData())
						return -1.0f;

					float pixelPercentNeeded = (percentBrightPixels / 100.0f) - totalPercentPixelsTested;
					float thisBinPercentOfTotalPixels = (float)CurHistogram[i].NPixelsInRange / (float)totalValidPixels;
					float thisBinLuminanceRange = CurHistogram[i].MaxLum - CurHistogram[i].MinLum;
					if (thisBinPercentOfTotalPixels >= pixelPercentNeeded) {
						if (percentTargetToSnapToIfInSameBin >= 0.0f) {
							if ((CurHistogram[i].MinLum <= (percentTargetToSnapToIfInSameBin / 100.0f)) && (CurHistogram[i].MaxLum >= (percentTargetToSnapToIfInSameBin / 100.0f)))
								return percentTargetToSnapToIfInSameBin / 100.0f;
						}

						float percentOfThesePixelsNeeded = pixelPercentNeeded / thisBinPercentOfTotalPixels;
						float percentLocationOfBorder = 1.0f - (totalPercentRangeTested + (thisBinLuminanceRange * percentOfThesePixelsNeeded));
						percentLocationOfBorder = Math.Max(CurHistogram[i].MinLum, Math.Min(CurHistogram[i].MaxLum, percentLocationOfBorder));
						return percentLocationOfBorder;
					}

					totalPercentPixelsTested += thisBinPercentOfTotalPixels;
					totalPercentRangeTested += thisBinLuminanceRange;
				}

				return -1.0f;
			}
			else
				return -1.0f;
		}

		public float GetTargetTonemapScalar(bool getIdealTargetForDebugMode = false) {
			if (mat_tonemap_algorithm.GetInt() == 1) {
				float percentLocationOfTarget;
				if (getIdealTargetForDebugMode == true)
					percentLocationOfTarget = FindLocationOfPercentBrightPixels(mat_tonemap_percent_bright_pixels.GetFloat());
				else
					percentLocationOfTarget = FindLocationOfPercentBrightPixels(mat_tonemap_percent_bright_pixels.GetFloat(), mat_tonemap_percent_target.GetFloat());
				if (percentLocationOfTarget < 0.0f)
					percentLocationOfTarget = mat_tonemap_percent_target.GetFloat() / 100.0f;

				percentLocationOfTarget = Math.Max(0.0001f, percentLocationOfTarget);

				float targetScalar = (mat_tonemap_percent_target.GetFloat() / 100.0f) / percentLocationOfTarget;

				float averageLuminanceLocation = FindLocationOfPercentBrightPixels(50.0f);
				if (averageLuminanceLocation > 0.0f) {
					float targetScalar2 = (mat_tonemap_min_avglum.GetFloat() / 100.0f) / averageLuminanceLocation;

					if (targetScalar2 > targetScalar)
						targetScalar = targetScalar2;
				}

				using MatRenderContextPtr renderContext = new(materials);
				float lastScale = renderContext.GetToneMappingScaleLinear().X;
				targetScalar *= lastScale;

				targetScalar = Math.Max(0.001f, targetScalar);
				return targetScalar;
			}
			else {
				float averageLuminance = 0.5f;

				float total = 0;
				int totalPixels = 0;
				float scaleValue = 1.0f;
				if (CurHistogram[N_LUMINANCE_RANGES - 1].ContainsValidData()) {
					scaleValue = CurHistogram[N_LUMINANCE_RANGES - 1].NPixels * (1.0f / CurHistogram[N_LUMINANCE_RANGES - 1].NPixelsInRange);

					if (mat_debug_autoexposure.GetInt() != 0)
						engine.Con_NPrintf(20, $"Scale value = {scaleValue:F6}");
				}
				else
					averageLuminance = 0.5f;

				if (!float.IsFinite(scaleValue))
					scaleValue = 1.0f;

				for (int i = 0; i < N_LUMINANCE_RANGES - 1; i++) {
					if (CurHistogram[i].ContainsValidData()) {
						total += scaleValue * CurHistogram[i].NPixelsInRange * ((CurHistogram[i].MinLum + CurHistogram[i].MaxLum) * 0.5f);
						totalPixels += CurHistogram[i].NPixels;
					}
					else
						averageLuminance = 0.5f;
				}
				if (totalPixels > 0)
					averageLuminance = total * (1.0f / totalPixels);
				else
					averageLuminance = 0.5f;

				averageLuminance = Math.Max(0.0001f, averageLuminance);

				float targetScalar = 0.005f / averageLuminance;

				return targetScalar;
			}
		}

		static int s_nCurrentBucketAlgorithm = -1;
		static bool s_bFirstTime = true;
		static readonly string[] sModsForOriginalAlgorithm = ["dod", "cstrike", "lostcoast"];

		public void UpdateLuminanceRanges() {
			if (s_nCurrentBucketAlgorithm == mat_tonemap_algorithm.GetInt())
				return;
			s_nCurrentBucketAlgorithm = mat_tonemap_algorithm.GetInt();

			if (engine == null)
				s_nCurrentBucketAlgorithm = -1;
			else if (s_bFirstTime == true) {
				s_bFirstTime = false;

				ReadOnlySpan<char> gameDirectory = engine.GetGameDirectory();
				for (int i = 0; i < 3; i++) {
					if (gameDirectory.EndsWith(sModsForOriginalAlgorithm[i], StringComparison.OrdinalIgnoreCase)) {
						mat_tonemap_algorithm.SetValue(0);
						s_nCurrentBucketAlgorithm = mat_tonemap_algorithm.GetInt();
						break;
					}
				}
			}

			int numRanges = N_LUMINANCE_RANGES;

			if (mat_tonemap_algorithm.GetInt() == 1)
				numRanges = N_LUMINANCE_RANGES_NEW;

			CurQueryFrame = 0;
			for (int bucket = 0; bucket < numRanges; bucket++) {
				HistogramEntry e = CurHistogram[bucket];
				e.State = HistogramEntryState.Initial;
				e.MinX = 0;
				e.MaxX = 1;
				e.MinY = 0;
				e.MaxY = 1;
				if (bucket != numRanges - 1) {
					if (mat_tonemap_algorithm.GetInt() == 0) {
						e.MinLum = -0.01f + MathF.Exp(MathLib.Lerp(MathF.Log(.01f), MathF.Log(.01f + 1), 0, numRanges - 1, bucket));
						e.MaxLum = -0.01f + MathF.Exp(MathLib.Lerp(MathF.Log(.01f), MathF.Log(.01f + 1), 0, numRanges - 1, bucket + 1));
					}
					else {
						e.MinLum = (float)bucket / (float)(numRanges - 1);
						e.MaxLum = (float)(bucket + 1) / (float)(numRanges - 1);

						e.MinLum = e.MinLum > 0.0f ? MathF.Pow(e.MinLum, 1.5f) : e.MinLum;
						e.MaxLum = e.MaxLum > 0.0f ? MathF.Pow(e.MaxLum, 1.5f) : e.MaxLum;
					}
				}
				else {
					e.MinLum = 0;
					e.MaxLum = 100000.0f;
				}
			}
		}
	}

	static float GetCurrentBloomScale() {
		float currentBloomScale = 1.0f;
		if (g_bUseCustomBloomScale)
			currentBloomScale = g_flCustomBloomScale;
		else
			currentBloomScale = mat_bloomscale.GetFloat();
		return currentBloomScale;
	}

	static void GetExposureRange(out float autoExposureMin, out float autoExposureMax) {
		if (g_bUseCustomAutoExposureMin && (g_flCustomAutoExposureMin > 0.0f))
			autoExposureMin = g_flCustomAutoExposureMin;
		else
			autoExposureMin = mat_autoexposure_min.GetFloat();

		if (g_bUseCustomAutoExposureMax && (g_flCustomAutoExposureMax > 0.0f))
			autoExposureMax = g_flCustomAutoExposureMax;
		else
			autoExposureMax = mat_autoexposure_max.GetFloat();

		if (mat_hdr_uncapexposure.GetInt() != 0) {
			autoExposureMax = 20.0f;
			autoExposureMin = 0.0f;
		}

		if (autoExposureMin > autoExposureMax)
			autoExposureMax = autoExposureMin;
	}

	static readonly LuminanceHistogramSystem g_HDR_HistogramSystem = new();

	static readonly float[] s_MovingAverageToneMapScale = [1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f];
	static int s_nInAverage = 0;

	public static void ResetToneMapping(float value) {
		using MatRenderContextPtr renderContext = new(materials);
		s_nInAverage = 0;
		renderContext.ResetToneMappingScale(value);
	}

	static void SetToneMapScale(MatRenderContextPtr renderContext, float newvalue, float minvalue, float maxvalue) {
		Assert(float.IsFinite(newvalue));
		if (!float.IsFinite(newvalue))
			return;

		float forcedTonemapScale = mat_force_tonemap_scale.GetFloat();

		if (MatSysVars.mat_fullbright.GetInt() == 1)
			forcedTonemapScale = 1.0f;

		if (forcedTonemapScale > 0.0f) {
			mat_hdr_tonemapscale.SetValue(forcedTonemapScale);
			renderContext.ResetToneMappingScale(forcedTonemapScale);
			return;
		}

		mat_hdr_tonemapscale.SetValue(newvalue);
		renderContext.SetGoalToneMappingScale(newvalue);

		if (s_nInAverage < s_MovingAverageToneMapScale.Length)
			s_MovingAverageToneMapScale[s_nInAverage++] = newvalue;
		else {
			for (int i = 0; i < s_MovingAverageToneMapScale.Length - 1; i++)
				s_MovingAverageToneMapScale[i] = s_MovingAverageToneMapScale[i + 1];
			s_MovingAverageToneMapScale[^1] = newvalue;
		}

		if (s_nInAverage == s_MovingAverageToneMapScale.Length) {
			float avg = 0.0f;
			float sumweights = 0;
			int samplePt = s_MovingAverageToneMapScale.Length / 2;
			for (int i = 0; i < s_MovingAverageToneMapScale.Length; i++) {
				float weight = Math.Abs(i - samplePt) * (1.0f / (s_MovingAverageToneMapScale.Length / 2));
				sumweights += weight;
				avg += weight * s_MovingAverageToneMapScale[i];
			}
			avg *= 1.0f / sumweights;
			avg = Math.Min(maxvalue, Math.Max(minvalue, avg));
			renderContext.SetGoalToneMappingScale(avg);
			mat_hdr_tonemapscale.SetValue(avg);
		}
	}

	static void DrawBloomDebugBoxes(MatRenderContextPtr renderContext) {
		renderContext.SetRenderTarget(null);
		renderContext.GetRenderTargetDimensions(out int destWidth, out int destHeight);

		renderContext.Viewport(0, 0, destWidth, destHeight);
		renderContext.ClearColor3ub(0, 0, 0);
		renderContext.ClearBuffers(true, true);

		int inset = 64;
		int size = 32;

		s_DebugBoxWx = (s_DebugBoxWx + 1) & 63;

		renderContext.Viewport(destWidth / 2 + s_DebugBoxWx, destHeight / 2, size, size);
		renderContext.ClearColor3ub(255, 255, 255);
		renderContext.ClearBuffers(true, true);

		renderContext.Viewport(inset, inset, size, size);
		renderContext.ClearBuffers(true, true);

		renderContext.Viewport(destWidth - inset - size, inset, size, size);
		renderContext.ClearBuffers(true, true);

		renderContext.Viewport(destWidth - inset - size, destHeight - inset - size, size, size);
		renderContext.ClearBuffers(true, true);

		renderContext.Viewport(inset, destHeight - inset - size, size, size);
		renderContext.ClearBuffers(true, true);

		renderContext.Viewport(0, 0, destWidth, destHeight);
	}
	static int s_DebugBoxWx = 0;

	static float s_CurrentBloomAmount = 1.0f;

	static float GetBloomAmount() {
		HDRType hdrType = HardwareConfig.GetHDRType();

		bool bloomEnabled = MatSysVars.mat_hdr_level.GetInt() >= 1;

		if (!engine.MapHasHDRLighting())
			bloomEnabled = false;
		if (mat_force_bloom.GetInt() != 0)
			bloomEnabled = true;
		if (mat_disable_bloom.GetInt() != 0)
			bloomEnabled = false;
		if (MatSysVars.mat_fullbright.GetInt() == 1)
			bloomEnabled = false;

#if GMOD_DLL
		if (bloomEnabled) {
			ConVarRef pp_bloom = new("pp_bloom");
			if (pp_bloom.IsValid() && pp_bloom.GetBool())
				bloomEnabled = false;
		}
#endif

		float bloomAmount = 0.0f;

		if (bloomEnabled) {
			float rate = mat_bloomamount_rate.GetFloat();

			s_CurrentBloomAmount = GetCurrentBloomScale() * rate + (1.0f - rate) * s_CurrentBloomAmount;
			bloomAmount = s_CurrentBloomAmount;
		}

		if (hdrType == HDRType.None)
			bloomAmount *= mat_non_hdr_bloom_scalefactor.GetFloat();

		bloomAmount *= mat_bloom_scalefactor_scalar.GetFloat();

		return bloomAmount;
	}

	static bool s_bScreenEffectTextureIsUpdated = false;

	static void Generate8BitBloomTexture(MatRenderContextPtr renderContext, float bloomScale, int x, int y, int w, int h) {
		renderContext.PushRenderTargetAndViewport();
		ITexture src = materials.FindTexture("_rt_FullFrameFB", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET)!;
		int srcWidth = src.GetActualWidth();
		int srcHeight = src.GetActualHeight();

		IMaterial downsampleMat = materials.FindMaterial("dev/downsample_non_hdr", MaterialDefines.TEXTURE_GROUP_OTHER, true)!;

		IMaterial xblurMat = materials.FindMaterial("dev/blurfilterx_nohdr", MaterialDefines.TEXTURE_GROUP_OTHER, true)!;
		IMaterial yblurMat = materials.FindMaterial("dev/blurfiltery_nohdr", MaterialDefines.TEXTURE_GROUP_OTHER, true)!;
		ITexture destRt0 = materials.FindTexture("_rt_SmallFB0", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET)!;
		ITexture destRt1 = materials.FindTexture("_rt_SmallFB1", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET)!;

		Assert(destRt0.GetActualWidth() == src.GetActualWidth() / 4);
		Assert(destRt0.GetActualHeight() == src.GetActualHeight() / 4);
		Assert(destRt1.GetActualWidth() == src.GetActualWidth() / 4);
		Assert(destRt1.GetActualHeight() == src.GetActualHeight() / 4);

		RenderUtils renderUtils = Singleton<RenderUtils>();

		SetRenderTargetAndViewPort(destRt0);
		renderUtils.DrawScreenSpaceRectangle(downsampleMat, 0, 0, srcWidth / 4, srcHeight / 4,
			0, 0, srcWidth - 2, srcHeight - 2,
			srcWidth, srcHeight, null, 1, 1, 0);

		SetRenderTargetAndViewPort(destRt1);
		renderUtils.DrawScreenSpaceRectangle(xblurMat, 0, 0, srcWidth / 4, srcHeight / 4,
			0, 0, srcWidth / 4 - 1, srcHeight / 4 - 1,
			srcWidth / 4, srcHeight / 4, null, 1, 1, 0);

		SetRenderTargetAndViewPort(destRt0);
		IMaterialVar bloomAmountVar = yblurMat.FindVar("$bloomamount", out _);
		bloomAmountVar.SetFloatValue(bloomScale);
		renderUtils.DrawScreenSpaceRectangle(yblurMat, 0, 0, srcWidth / 4, srcHeight / 4,
			0, 0, srcWidth / 4 - 1, srcHeight / 4 - 1,
			srcWidth / 4, srcHeight / 4, null, 1, 1, 0);

		renderContext.PopRenderTargetAndViewport();
	}

	static void DoPreBloomTonemapping(MatRenderContextPtr renderContext, int x, int y, int width, int height, float autoExposureMin, float autoExposureMax) {
		if (mat_dynamic_tonemapping.GetInt() != 0) {
			if (s_bScreenEffectTextureIsUpdated == false) {
				UpdateScreenEffectTexture(0, x, y, width, height, true);
				s_bScreenEffectTextureIsUpdated = true;
			}

			g_HDR_HistogramSystem.Update();

			float targetScalar = g_HDR_HistogramSystem.GetTargetTonemapScalar();
			float targetScalarClamped = Math.Max(autoExposureMin, Math.Min(autoExposureMax, targetScalar));
			targetScalarClamped = Math.Max(0.001f, targetScalarClamped);
			SetToneMapScale(renderContext, targetScalarClamped, autoExposureMin, autoExposureMax);

			if (mat_debug_autoexposure.GetInt() != 0) {
				if (mat_tonemap_algorithm.GetInt() == 0) {
					engine.Con_NPrintf(19, $"(Original algorithm) Target Scalar = {targetScalar:F2}  Min/Max( {autoExposureMin:F2}, {autoExposureMax:F2} )  Final Scalar: {mat_hdr_tonemapscale.GetFloat():F2}  Actual: {renderContext.GetToneMappingScaleLinear().X:F2}");
				}
				else {
					engine.Con_NPrintf(19, $"{mat_tonemap_percent_bright_pixels.GetFloat():F2}% of pixels above {mat_tonemap_percent_target.GetInt()}% target @ {g_HDR_HistogramSystem.FindLocationOfPercentBrightPixels(mat_tonemap_percent_bright_pixels.GetFloat(), mat_tonemap_percent_target.GetFloat()) * 100.0f:F2}%  Target Scalar = {g_HDR_HistogramSystem.GetTargetTonemapScalar(true):F2}  Min/Max( {autoExposureMin:F2}, {autoExposureMax:F2} )  Final Scalar: {mat_hdr_tonemapscale.GetFloat():F2}  Actual: {renderContext.GetToneMappingScaleLinear().X:F2}");
				}
			}
		}
	}

	static void CenterScaleQuadUVs(ref Vector4 quadUVs, in Vector2 uvScale) {
		Vector2 uvMid = 0.5f * new Vector2(quadUVs.Z + quadUVs.X, quadUVs.W + quadUVs.Y);
		Vector2 uvRange = 0.5f * new Vector2(quadUVs.Z - quadUVs.X, quadUVs.W - quadUVs.Y);
		quadUVs.X = uvMid.X - uvScale.X * uvRange.X;
		quadUVs.Y = uvMid.Y - uvScale.Y * uvRange.Y;
		quadUVs.Z = uvMid.X + uvScale.X * uvRange.X;
		quadUVs.W = uvMid.Y + uvScale.Y * uvRange.Y;
	}

	public static void DoEnginePostProcessing(int x, int y, int w, int h, bool flashlightIsOn, bool postVGui = false) {
		using MatRenderContextPtr renderContext = new(materials);

		float bloomScale = GetBloomAmount();

		HDRType hdrType = HardwareConfig.GetHDRType();

		g_bFlashlightIsOn = flashlightIsOn;

		GetExposureRange(out float autoExposureMin, out float autoExposureMax);

		if (mat_debug_bloom.GetInt() == 1)
			DrawBloomDebugBoxes(renderContext);

		switch (hdrType) {
			case HDRType.None:
			case HDRType.Integer: {
					s_bScreenEffectTextureIsUpdated = false;

					if (hdrType != HDRType.None)
						DoPreBloomTonemapping(renderContext, x, y, w, h, autoExposureMin, autoExposureMax);

					bool performBloom = !postVGui && (bloomScale > 0.0f);
					bool splitScreenHDR = MatSysVars.mat_show_ab_hdr.GetInt() != 0;
					if (performBloom) {
						ITexture src = materials.FindTexture("_rt_FullFrameFB", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET)!;
						int srcWidth = src.GetActualWidth();
						int srcHeight = src.GetActualHeight();

						ITexture destRt1 = materials.FindTexture("_rt_SmallFB1", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET)!;

						if (!s_bScreenEffectTextureIsUpdated) {
							UpdateScreenEffectTexture(0, x, y, w, h, true);
							s_bScreenEffectTextureIsUpdated = true;
						}

						Generate8BitBloomTexture(renderContext, bloomScale, x, y, w, h);

						Vector4 fullViewportPostSrcCorners = new(0.0f, -0.5f, srcWidth / 4 - 1, srcHeight / 4 - 1);
						System.Drawing.Rectangle fullViewportPostDestRect = new(x, y, w, h);

						Vector2 uvScale = new((srcWidth - (srcWidth / (float)w)) / (srcWidth - 1),
											  (srcHeight - (srcHeight / (float)h)) / (srcHeight - 1));
						CenterScaleQuadUVs(ref fullViewportPostSrcCorners, uvScale);

						System.Drawing.Rectangle partialViewportPostDestRect = fullViewportPostDestRect;
						Vector4 partialViewportPostSrcCorners = fullViewportPostSrcCorners;
						if (debug_postproc.GetInt() == 2) {
							partialViewportPostDestRect.X += (int)(0.25f * fullViewportPostDestRect.Width);
							partialViewportPostDestRect.Y += (int)(0.25f * fullViewportPostDestRect.Height);
							partialViewportPostDestRect.Width -= (int)(0.50f * fullViewportPostDestRect.Width);
							partialViewportPostDestRect.Height -= (int)(0.50f * fullViewportPostDestRect.Height);

							Vector2 partialUVScale = new(1.0f - ((w / 2) / (float)(w - 1)),
														 1.0f - ((h / 2) / (float)(h - 1)));
							CenterScaleQuadUVs(ref partialViewportPostSrcCorners, partialUVScale);
						}

						IMaterial bloomMat = materials.FindMaterial("dev/bloomadd", MaterialDefines.TEXTURE_GROUP_OTHER, true)!;
						RenderUtils renderUtils = Singleton<RenderUtils>();

						if (splitScreenHDR)
							renderContext.SetScissorRect(partialViewportPostDestRect.Width / 2, 0, partialViewportPostDestRect.Width, partialViewportPostDestRect.Height, true);

						if (mat_postprocessing_combine.GetInt() != 0) {
							renderUtils.DrawScreenSpaceRectangle(bloomMat,
								0, 0,
								partialViewportPostDestRect.Width, partialViewportPostDestRect.Height,
								partialViewportPostSrcCorners.X, partialViewportPostSrcCorners.Y,
								partialViewportPostSrcCorners.Z, partialViewportPostSrcCorners.W,
								destRt1.GetActualWidth(), destRt1.GetActualHeight(),
								C_World.GetClientWorldEntity(),
								mat_postprocess_x.GetInt(), mat_postprocess_y.GetInt(), 0);
						}
						else {
							renderUtils.DrawScreenSpaceRectangle(bloomMat,
								partialViewportPostDestRect.X, partialViewportPostDestRect.Y,
								partialViewportPostDestRect.Width, partialViewportPostDestRect.Height,
								partialViewportPostSrcCorners.X, partialViewportPostSrcCorners.Y,
								partialViewportPostSrcCorners.Z, partialViewportPostSrcCorners.W,
								destRt1.GetActualWidth(), destRt1.GetActualHeight(),
								C_World.GetClientWorldEntity(), 1, 1, 0);
						}

						if (splitScreenHDR)
							renderContext.SetScissorRect(-1, -1, -1, -1, false);
					}
				}
				break;
		}
	}
}
