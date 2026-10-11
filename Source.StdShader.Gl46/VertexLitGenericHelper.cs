using Source.Common;
using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

using System.Numerics;

namespace Source.StdShader.Gl46;

public partial class BaseVSShader
{
	internal readonly static ConVar mat_fullbright = new("mat_fullbright", "0", FCvar.Cheat);
	internal readonly static ConVar r_lightwarpidentity = new("r_lightwarpidentity", "0", FCvar.Cheat);

	private static bool WantsSkinShader(IMaterialVar[] parms, ref VertexLitGeneric_Vars info) {
		if (info.Phong == -1)
			return false;

		if (parms[info.Phong].GetIntValue() == 0)
			return false;

		if ((info.DiffuseWarpTexture != -1) && parms[info.DiffuseWarpTexture].IsTexture())
			return true;

		if ((info.BaseMapAlphaPhongMask != -1) && parms[info.BaseMapAlphaPhongMask].GetIntValue() != 1) {
			if (info.Bumpmap == -1)
				return false;

			if (!parms[info.Bumpmap].IsTexture())
				return false;
		}
		return true;
	}

	internal void InitParamsVertexLitGeneric(BaseVSShader shader, IMaterialVar[] parms, ReadOnlySpan<char> materialName, bool vertexLitGeneric, ref VertexLitGeneric_Vars info) {
		InitIntParam(info.Phong, parms, 0);

		InitFloatParam(info.AlphaTestReference, parms, 0.0f);
		InitIntParam(info.VertexAlphaTest, parms, 0);

		InitIntParam(info.FlashlightNoLambert, parms, 0);

		if (info.DetailTint != -1 && !parms[info.DetailTint].IsDefined())
			parms[info.DetailTint].SetVecValue(1.0f, 1.0f, 1.0f);

		if (info.EnvmapTint != -1 && !parms[info.EnvmapTint].IsDefined())
			parms[info.EnvmapTint].SetVecValue(1.0f, 1.0f, 1.0f);

		InitIntParam(info.EnvmapFrame, parms, 0);
		InitIntParam(info.BumpFrame, parms, 0);
		InitFloatParam(info.DetailTextureBlendFactor, parms, 1.0f);
		InitIntParam(info.ReceiveFlashlight, parms, 0);

		InitFloatParam(info.DetailScale, parms, 4.0f);

		if ((info.BlendTintByBaseAlpha != -1) && (!parms[info.BlendTintByBaseAlpha].IsDefined()))
			parms[info.BlendTintByBaseAlpha].SetIntValue(0);

		InitFloatParam(info.TintReplacesBaseColor, parms, 0);

		if ((info.SelfIllumTint != -1) && (!parms[info.SelfIllumTint].IsDefined()))
			parms[info.SelfIllumTint].SetVecValue(1.0f, 1.0f, 1.0f);

		if (WantsSkinShader(parms, ref info)) {
			if (!HardwareConfig.SupportsPixelShaders_2_b() || !Config.UsePhong())
				parms[info.Phong].SetIntValue(0);
			else {
				InitParamsSkin(shader, parms, materialName, ref info);
				return;
			}
		}

		if (info.FlashlightTexture != -1) {
			if (HardwareConfig.SupportsBorderColor())
				parms[(int)ShaderMaterialVars.FlashLightTexture].SetStringValue("effects/flashlight_border");
			else
				parms[(int)ShaderMaterialVars.FlashLightTexture].SetStringValue("effects/flashlight001");
		}

		if (info.Albedo != -1 && Config.UseBumpmapping() && info.Bumpmap != -1 && parms[info.Bumpmap].IsDefined() && parms[info.Albedo].IsDefined() &&
			parms[info.BaseTexture].IsDefined()) {
			parms[info.BaseTexture].SetStringValue(parms[info.Albedo].GetStringValue());
		}

		SetFlags2(parms, MaterialVarFlags2.SupportsHardwareSkinning);

		if (vertexLitGeneric)
			SetFlags2(parms, MaterialVarFlags2.LightingVertexLit);
		else
			ClearFlags(parms, MaterialVarFlags.SelfIllum);

		InitIntParam(info.EnvmapMaskFrame, parms, 0);
		InitFloatParam(info.EnvmapContrast, parms, 0.0f);
		InitFloatParam(info.EnvmapSaturation, parms, 1.0f);
		InitFloatParam(info.SeamlessScale, parms, 0.0f);
		InitFloatParam(info.EdgeSoftnessStart, parms, 0.5f);
		InitFloatParam(info.EdgeSoftnessEnd, parms, 0.5f);
		InitFloatParam(info.GlowAlpha, parms, 1.0f);
		InitFloatParam(info.OutlineAlpha, parms, 1.0f);

		if (info.BaseTexture != -1 && !parms[info.BaseTexture].IsDefined()) {
			ClearFlags(parms, MaterialVarFlags.SelfIllum);
			ClearFlags(parms, MaterialVarFlags.BaseAlphaEnvMapMask);
		}

		if (IsFlagSet(parms, MaterialVarFlags.Decal))
			SetFlags(parms, MaterialVarFlags.NoDebugOverride);

		if ((info.Bumpmap != -1) && Config.UseBumpmapping() && parms[info.Bumpmap].IsDefined())
			SetFlags2(parms, MaterialVarFlags2.NeedsTangentSpaces);
		else if ((info.DiffuseWarpTexture != -1) && parms[info.DiffuseWarpTexture].IsDefined())
			SetFlags2(parms, MaterialVarFlags2.NeedsTangentSpaces);
		else
			ClearFlags(parms, MaterialVarFlags.NormalMapAlphaEnvMapMask);

		bool hasNormalMapAlphaEnvmapMask = IsFlagSet(parms, MaterialVarFlags.NormalMapAlphaEnvMapMask);
		if (hasNormalMapAlphaEnvmapMask) {
			parms[info.EnvmapMask].SetUndefined();
			ClearFlags(parms, MaterialVarFlags.BaseAlphaEnvMapMask);
		}

		if (IsFlagSet(parms, MaterialVarFlags.BaseAlphaEnvMapMask) && info.Bumpmap != -1 &&
			parms[info.Bumpmap].IsDefined() && !hasNormalMapAlphaEnvmapMask) {
			Warning($"material {materialName} has a normal map and $basealphaenvmapmask.  Must use $normalmapalphaenvmapmask to get specular.\n\n");
			parms[info.Envmap].SetUndefined();
		}

		if (info.EnvmapMask != -1 && parms[info.EnvmapMask].IsDefined() && info.Bumpmap != -1 && parms[info.Bumpmap].IsDefined()) {
			parms[info.EnvmapMask].SetUndefined();
			if (!hasNormalMapAlphaEnvmapMask) {
				Warning($"material {materialName} has a normal map and an envmapmask.  Must use $normalmapalphaenvmapmask.\n\n");
				parms[info.Envmap].SetUndefined();
			}
		}

		if (!Config.UseSpecular() && info.Envmap != -1 && parms[info.Envmap].IsDefined() && parms[info.BaseTexture].IsDefined())
			parms[info.Envmap].SetUndefined();

		InitFloatParam(info.HDRColorScale, parms, 1.0f);

		InitIntParam(info.LinearWrite, parms, 0);
		InitIntParam(info.GammaColorRead, parms, 0);

		InitIntParam(info.DepthBlend, parms, 0);
		InitFloatParam(info.DepthBlendScale, parms, 50.0f);
	}

	internal void DrawVertexLitGeneric(BaseVSShader shader, IMaterialVar[] parms, IShaderDynamicAPI? shaderAPI, IShaderShadow? shaderShadow, bool vertexLitGeneric, ref VertexLitGeneric_Vars info, VertexCompressionType vertexCompression, ref BasePerMaterialContextData? contextData) {
		if (WantsSkinShader(parms, ref info) && HardwareConfig.SupportsPixelShaders_2_b() && Config.UseBumpmapping() && Config.UsePhong()) {
			DrawSkin(shader, parms, shaderAPI, shaderShadow, ref info, vertexCompression, ref contextData);
			return;
		}

		bool receiveFlashlight = vertexLitGeneric;
		bool hasFlashlight = receiveFlashlight && shader.UsingFlashlight(parms);

		DrawVertexLitGeneric_Internal(shader, parms, shaderAPI, shaderShadow, vertexLitGeneric, hasFlashlight, ref info, vertexCompression, ref contextData);
	}

	private void DrawVertexLitGeneric_Internal(BaseVSShader shader, IMaterialVar[] parms, IShaderDynamicAPI? shaderAPI, IShaderShadow? shaderShadow, bool vertexLitGeneric, bool hasFlashlight, ref VertexLitGeneric_Vars info, VertexCompressionType vertexCompression, ref BasePerMaterialContextData? context) {
		VertexLitGeneric_Context? contextData = context as VertexLitGeneric_Context;

		bool hasBump = IsTextureSet(info.Bumpmap, parms);
		bool isDecal = IsFlagSet(parms, MaterialVarFlags.Decal);
		bool hasDiffuseLighting = vertexLitGeneric;

		if (IsFlagSet(parms, MaterialVarFlags.EnvMapSphere))
			hasFlashlight = false;

		bool isAlphaTested = IsFlagSet(parms, MaterialVarFlags.AlphaTest) != false;
		bool hasDiffuseWarp = !hasFlashlight && hasDiffuseLighting && (info.DiffuseWarpTexture != -1) && parms[info.DiffuseWarpTexture].IsTexture();

		bool flashlightNoLambert = false;
		if ((info.FlashlightNoLambert != -1) && parms[info.FlashlightNoLambert].GetIntValue() != 0)
			flashlightNoLambert = true;

		bool ambientOnly = IsBoolSet(info.AmbientOnly, parms);

		float blendFactor = GetFloatParam(info.DetailTextureBlendFactor, parms, 1.0f);
		bool hasDetailTexture = IsTextureSet(info.Detail, parms);
		int detailBlendMode = hasDetailTexture ? GetIntParam(info.DetailTextureCombineMode, parms) : 0;
		int detailTranslucencyTexture = -1;

		if (hasDetailTexture) {
			if ((detailBlendMode == 6) && (!HardwareConfig.SupportsPixelShaders_2_b()))
				detailBlendMode = 5;

			if ((detailBlendMode == 3) || (detailBlendMode == 8) || (detailBlendMode == 9))
				detailTranslucencyTexture = info.Detail;
		}

		bool blendTintByBaseAlpha = IsBoolSet(info.BlendTintByBaseAlpha, parms);
		float fTintReplaceFactor = GetFloatParam(info.TintReplacesBaseColor, parms, 0.0f);

		BlendType blendType;
		bool hasBaseTexture = IsTextureSet(info.BaseTexture, parms);
		if (hasBaseTexture)
			blendType = shader.EvaluateBlendRequirements(blendTintByBaseAlpha ? -1 : info.BaseTexture, true, detailTranslucencyTexture);
		else
			blendType = shader.EvaluateBlendRequirements(info.EnvmapMask, false);

		bool fullyOpaque = (blendType != BlendType.Add) && (blendType != BlendType.Blend) && !isAlphaTested && !hasFlashlight;
		bool hasEnvmap = !hasFlashlight && info.Envmap != -1 && parms[info.Envmap].IsTexture();

		bool hasVertexColor = !vertexLitGeneric && IsFlagSet(parms, MaterialVarFlags.VertexColor);
		bool hasVertexAlpha = !vertexLitGeneric && IsFlagSet(parms, MaterialVarFlags.VertexAlpha);

		if (shader.IsSnapshotting() || contextData == null || contextData.MaterialVarsChanged) {
			bool seamlessBase = IsBoolSet(info.SeamlessBase, parms);
			bool seamlessDetail = IsBoolSet(info.SeamlessDetail, parms);
			bool distanceAlpha = IsBoolSet(info.DistanceAlpha, parms);
			bool hasSelfIllum = (!hasFlashlight) && IsFlagSet(parms, MaterialVarFlags.SelfIllum);
			bool hasEnvmapMask = (!hasFlashlight) && info.EnvmapMask != -1 && parms[info.EnvmapMask].IsTexture();
			bool hasSelfIllumFresnel = (!IsTextureSet(info.Detail, parms)) && (hasSelfIllum) && (info.SelfIllumFresnel != -1) && (parms[info.SelfIllumFresnel].GetIntValue() != 0);

			bool hasSelfIllumMask = hasSelfIllum && IsTextureSet(info.SelfIllumMask, parms);
			bool hasSelfIllumInEnvMapMask =
				(info.SelfIllumEnvMapMask_Alpha != -1) &&
				(parms[info.SelfIllumEnvMapMask_Alpha].GetFloatValue() != 0.0);

			if (shader.IsSnapshotting()) {
				bool hasBaseAlphaEnvmapMask = IsFlagSet(parms, MaterialVarFlags.BaseAlphaEnvMapMask);
				bool hasNormalMapAlphaEnvmapMask = IsFlagSet(parms, MaterialVarFlags.NormalMapAlphaEnvMapMask);


				if (info.VertexAlphaTest != -1 && parms[info.VertexAlphaTest].GetIntValue() > 0)
					hasVertexAlpha = true;

				if (hasSelfIllumFresnel) {
					ClearFlags(parms, MaterialVarFlags.NormalMapAlphaEnvMapMask);
					hasNormalMapAlphaEnvmapMask = false;
				}

				// bool hasEnvmap = (!hasFlashlight) && (info.Envmap != -1) && parms[info.Envmap].IsTexture();
				bool hasLegacyEnvSphereMap = hasEnvmap && IsFlagSet(parms, MaterialVarFlags.EnvMapSphere);
				bool hasNormal = vertexLitGeneric || hasEnvmap || hasFlashlight || seamlessBase || seamlessDetail;
				if (IsPC())
					hasNormal = true;

				bool halfLambert = IsFlagSet(parms, MaterialVarFlags.HalfLambert);
				shaderShadow!.EnableAlphaTest(isAlphaTested);

				if (info.AlphaTestReference != -1 && parms[info.AlphaTestReference].GetFloatValue() > 0.0f)
					shaderShadow.AlphaFunc(ShaderAlphaFunc.GreaterEqual, parms[info.AlphaTestReference].GetFloatValue());

				int shadowFilterMode = 0;
				if (hasFlashlight) {
					if (HardwareConfig.SupportsPixelShaders_2_b())
						shadowFilterMode = HardwareConfig.GetShadowFilterMode();

					if (parms[info.BaseTexture].IsTexture())
						shader.SetAdditiveBlendingShadowState(info.BaseTexture, true);
					else
						shader.SetAdditiveBlendingShadowState(info.EnvmapMask, false);

					if (isAlphaTested) {
						shaderShadow.EnableAlphaTest(false);
						shaderShadow.DepthFunc(ShaderDepthFunc.Equal);
					}

					shaderShadow.EnableAlphaWrites(false);
					shaderShadow.EnableBlending(true);
					shaderShadow.EnableDepthWrites(false);
				}
				else
					shader.SetBlendingShadowState(blendType);

				VertexFormat flags = VertexFormat.Position;
				if (hasNormal)
					flags |= VertexFormat.Normal;

				int userDataSize = 0;
				bool bSRGBInputAdapter = false;

				shaderShadow.EnableTexture(Sampler.Sampler0, true);
				if (hasBaseTexture) {
					if ((info.GammaColorRead != -1) && (parms[info.GammaColorRead].GetIntValue() == 1))
						shaderShadow.EnableSRGBRead(Sampler.Sampler0, false);
					else
						shaderShadow.EnableSRGBRead(Sampler.Sampler0, true);

					if (IsOSX() && !HardwareConfig.CanDoSRGBReadFromRTs()) {
						ITexture? baseTexture = parms[info.BaseTexture].GetTextureValue();
						if (baseTexture != null && baseTexture.IsRenderTarget())
							bSRGBInputAdapter = true;
					}
				}

				if (hasEnvmap) {
					shaderShadow.EnableTexture(Sampler.Sampler1, true);
					if (HardwareConfig.GetHDRType() == HDRType.None)
						shaderShadow.EnableSRGBRead(Sampler.Sampler1, true);
				}
				if (hasFlashlight) {
					shaderShadow.EnableTexture(Sampler.Sampler8, true);
					shaderShadow.SetShadowDepthFiltering(Sampler.Sampler8);
					shaderShadow.EnableTexture(Sampler.Sampler6, true);
					shaderShadow.EnableTexture(Sampler.Sampler7, true);
					shaderShadow.EnableSRGBRead(Sampler.Sampler7, true);
					userDataSize = 4;
				}

				if (hasDetailTexture) {
					shaderShadow.EnableTexture(Sampler.Sampler2, true);
					if (detailBlendMode != 0)
						shaderShadow.EnableSRGBRead(Sampler.Sampler2, true);
				}

				if (hasBump || hasDiffuseWarp) {
					shaderShadow.EnableTexture(Sampler.Sampler3, true);
					userDataSize = 4;
					shaderShadow.EnableTexture(Sampler.Sampler5, true);
				}
				if (hasEnvmapMask)
					shaderShadow.EnableTexture(Sampler.Sampler4, true);

				if (hasVertexColor || hasVertexAlpha)
					flags |= VertexFormat.Color;

				if (hasDiffuseWarp && (!hasFlashlight) && !hasSelfIllumFresnel)
					shaderShadow.EnableTexture(Sampler.Sampler9, true);

				if ((info.DepthBlend != -1) && (parms[info.DepthBlend].GetIntValue() != 0)) {
					if (hasBump)
						Warning("DEPTHBLEND not supported by bump mapped variations of vertexlitgeneric to avoid shader bloat. Either remove the bump map or convince a graphics programmer that it's worth it.\n");

					shaderShadow.EnableTexture(Sampler.Sampler10, true);
				}

				if (hasSelfIllum)
					shaderShadow.EnableTexture(Sampler.Sampler11, true);

				bool bSRGBWrite = true;
				if ((info.LinearWrite != -1) && (parms[info.LinearWrite].GetIntValue() == 1))
					bSRGBWrite = false;

				shaderShadow.EnableSRGBWrite(bSRGBWrite);

				Span<int> pTexCoordDim = [2, 2, 3];
				int nTexCoordCount = 1;

				if (IsBoolSet(info.SeparateDetailUVs, parms))
					++nTexCoordCount;
				else
					pTexCoordDim[1] = 0;

				if (isDecal && HardwareConfig.HasFastVertexTextures())
					nTexCoordCount = 3;

				// flags |= VERTEX_FORMAT_COMPRESSED; todo?

				shaderShadow.VertexShaderVertexFormat(flags, nTexCoordCount, pTexCoordDim, userDataSize);

				if (hasBump || hasDiffuseWarp) {
					if (!HardwareConfig.HasFastVertexTextures()) {
						bool useStaticControlFlow = HardwareConfig.SupportsStaticControlFlow();

						StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "vertexlitgeneric_bump");
						vshIndex.Set("HALFLAMBERT", halfLambert);
						vshIndex.Set("USE_WITH_2B", HardwareConfig.SupportsPixelShaders_2_b());
						vshIndex.Set("USE_STATIC_CONTROL_FLOW", useStaticControlFlow);
						shaderShadow.SetVertexShader("vertexlitgeneric_bump", vshIndex.GetIndex());

						if (HardwareConfig.SupportsPixelShaders_2_b() || HardwareConfig.ShouldAlwaysUseShaderModel2bShaders()) {
							StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "vertexlitgeneric_bump");
							pshIndex.Set("CUBEMAP", hasEnvmap);
							pshIndex.Set("DIFFUSELIGHTING", hasDiffuseLighting);
							pshIndex.Set("LIGHTWARPTEXTURE", hasDiffuseWarp && !hasSelfIllumFresnel);
							pshIndex.Set("SELFILLUM", hasSelfIllum);
							pshIndex.Set("SELFILLUMFRESNEL", hasSelfIllumFresnel);
							pshIndex.Set("NORMALMAPALPHAENVMAPMASK", hasNormalMapAlphaEnvmapMask && hasEnvmap);
							pshIndex.Set("HALFLAMBERT", halfLambert);
							pshIndex.Set("FLASHLIGHT", hasFlashlight);
							pshIndex.Set("DETAILTEXTURE", hasDetailTexture);
							pshIndex.Set("DETAIL_BLEND_MODE", detailBlendMode);
							pshIndex.Set("FLASHLIGHTDEPTHFILTERMODE", shadowFilterMode);
							pshIndex.Set("BLENDTINTBYBASEALPHA", blendTintByBaseAlpha);
							shaderShadow.SetPixelShader("vertexlitgeneric_bump", pshIndex.GetIndex());
						}
						else {
							StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "vertexlitgeneric_bump");
							pshIndex.Set("CUBEMAP", hasEnvmap);
							pshIndex.Set("DIFFUSELIGHTING", hasDiffuseLighting);
							pshIndex.Set("LIGHTWARPTEXTURE", hasDiffuseWarp && !hasSelfIllumFresnel);
							pshIndex.Set("SELFILLUM", hasSelfIllum);
							pshIndex.Set("SELFILLUMFRESNEL", hasSelfIllumFresnel);
							pshIndex.Set("NORMALMAPALPHAENVMAPMASK", hasNormalMapAlphaEnvmapMask && hasEnvmap);
							pshIndex.Set("HALFLAMBERT", halfLambert);
							pshIndex.Set("FLASHLIGHT", hasFlashlight);
							pshIndex.Set("DETAILTEXTURE", hasDetailTexture);
							pshIndex.Set("DETAIL_BLEND_MODE", detailBlendMode);
							pshIndex.Set("BLENDTINTBYBASEALPHA", blendTintByBaseAlpha);
							shaderShadow.SetPixelShader("vertexlitgeneric_bump", pshIndex.GetIndex());
						}
					}
					else {
						SetFlags2(parms, MaterialVarFlags2.UsesVertexID);

						StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "vertexlitgeneric_bump");
						vshIndex.Set("HALFLAMBERT", halfLambert);
						vshIndex.Set("USE_WITH_2B", true);
						vshIndex.Set("DECAL", isDecal);
						shaderShadow.SetVertexShader("vertexlitgeneric_bump", vshIndex.GetIndex());

						StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "vertexlitgeneric_bump");
						pshIndex.Set("CUBEMAP", hasEnvmap);
						pshIndex.Set("DIFFUSELIGHTING", hasDiffuseLighting);
						pshIndex.Set("LIGHTWARPTEXTURE", hasDiffuseWarp && !hasSelfIllumFresnel);
						pshIndex.Set("SELFILLUM", hasSelfIllum);
						pshIndex.Set("SELFILLUMFRESNEL", hasSelfIllumFresnel);
						pshIndex.Set("NORMALMAPALPHAENVMAPMASK", hasNormalMapAlphaEnvmapMask && hasEnvmap);
						pshIndex.Set("HALFLAMBERT", halfLambert);
						pshIndex.Set("FLASHLIGHT", hasFlashlight);
						pshIndex.Set("DETAILTEXTURE", hasDetailTexture);
						pshIndex.Set("DETAIL_BLEND_MODE", detailBlendMode);
						pshIndex.Set("FLASHLIGHTDEPTHFILTERMODE", shadowFilterMode);
						pshIndex.Set("BLENDTINTBYBASEALPHA", blendTintByBaseAlpha);
						shaderShadow.SetPixelShader("vertexlitgeneric_bump", pshIndex.GetIndex());
					}
				}
				else {
					bool distanceAlphaFromDetail = false;
					bool softMask = false;
					bool bGlow = false;
					bool outline = false;

					bool doDepthBlend = IsBoolSet(info.DepthBlend, parms) && !mat_reduceparticles.GetBool();

					if (distanceAlpha) {
						distanceAlphaFromDetail = IsBoolSet(info.DistanceAlphaFromDetail, parms);
						softMask = IsBoolSet(info.SoftEdges, parms);
						bGlow = IsBoolSet(info.Glow, parms);
						outline = IsBoolSet(info.Outline, parms);
					}

					if (!HardwareConfig.HasFastVertexTextures()) {
						bool useStaticControlFlow = HardwareConfig.SupportsStaticControlFlow();

						StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "vertexlitgeneric");
						vshIndex.Set("VERTEXCOLOR", hasVertexColor || hasVertexAlpha);
						vshIndex.Set("CUBEMAP", hasEnvmap);
						vshIndex.Set("HALFLAMBERT", halfLambert);
						vshIndex.Set("FLASHLIGHT", hasFlashlight);
						vshIndex.Set("SEAMLESS_BASE", seamlessBase);
						vshIndex.Set("SEAMLESS_DETAIL", seamlessDetail);
						vshIndex.Set("SEPARATE_DETAIL_UVS", IsBoolSet(info.SeparateDetailUVs, parms));
						vshIndex.Set("USE_STATIC_CONTROL_FLOW", useStaticControlFlow);
						vshIndex.Set("DONT_GAMMA_CONVERT_VERTEX_COLOR", (!bSRGBWrite) && hasVertexColor);
						shaderShadow.SetVertexShader("vertexlitgeneric", vshIndex.GetIndex());

						if (HardwareConfig.SupportsPixelShaders_2_b() || HardwareConfig.ShouldAlwaysUseShaderModel2bShaders()) {
							StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "vertexlitgeneric");
							pshIndex.Set("SELFILLUM_ENVMAPMASK_ALPHA", hasSelfIllumInEnvMapMask && hasEnvmapMask);
							pshIndex.Set("CUBEMAP", hasEnvmap);
							pshIndex.Set("CUBEMAP_SPHERE_LEGACY", hasLegacyEnvSphereMap);
							pshIndex.Set("DIFFUSELIGHTING", hasDiffuseLighting);
							pshIndex.Set("ENVMAPMASK", hasEnvmapMask);
							pshIndex.Set("BASEALPHAENVMAPMASK", hasBaseAlphaEnvmapMask);
							pshIndex.Set("SELFILLUM", hasSelfIllum);
							pshIndex.Set("VERTEXCOLOR", hasVertexColor);
							pshIndex.Set("FLASHLIGHT", hasFlashlight);
							pshIndex.Set("DETAILTEXTURE", hasDetailTexture);
							pshIndex.Set("DETAIL_BLEND_MODE", detailBlendMode);
							pshIndex.Set("SEAMLESS_BASE", seamlessBase);
							pshIndex.Set("SEAMLESS_DETAIL", seamlessDetail);
							pshIndex.Set("DISTANCEALPHA", distanceAlpha);
							pshIndex.Set("DISTANCEALPHAFROMDETAIL", distanceAlphaFromDetail);
							pshIndex.Set("SOFT_MASK", softMask);
							pshIndex.Set("OUTLINE", outline);
							pshIndex.Set("OUTER_GLOW", bGlow);
							pshIndex.Set("FLASHLIGHTDEPTHFILTERMODE", shadowFilterMode);
							pshIndex.Set("DEPTHBLEND", doDepthBlend);
							pshIndex.Set("SRGB_INPUT_ADAPTER", bSRGBInputAdapter ? 1 : 0);
							pshIndex.Set("BLENDTINTBYBASEALPHA", blendTintByBaseAlpha);
							shaderShadow.SetPixelShader("vertexlitgeneric", pshIndex.GetIndex());
						}
						else {
							StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "vertexlitgeneric");
							pshIndex.Set("SELFILLUM_ENVMAPMASK_ALPHA", hasSelfIllumInEnvMapMask && hasEnvmapMask);
							pshIndex.Set("CUBEMAP", hasEnvmap);
							pshIndex.Set("CUBEMAP_SPHERE_LEGACY", hasLegacyEnvSphereMap);
							pshIndex.Set("DIFFUSELIGHTING", hasDiffuseLighting);
							pshIndex.Set("ENVMAPMASK", hasEnvmapMask);
							pshIndex.Set("BASEALPHAENVMAPMASK", hasBaseAlphaEnvmapMask);
							pshIndex.Set("SELFILLUM", hasSelfIllum);
							pshIndex.Set("VERTEXCOLOR", hasVertexColor);
							pshIndex.Set("FLASHLIGHT", hasFlashlight);
							pshIndex.Set("DETAILTEXTURE", hasDetailTexture);
							pshIndex.Set("DETAIL_BLEND_MODE", detailBlendMode);
							pshIndex.Set("SEAMLESS_BASE", seamlessBase);
							pshIndex.Set("SEAMLESS_DETAIL", seamlessDetail);
							pshIndex.Set("DISTANCEALPHA", distanceAlpha);
							pshIndex.Set("DISTANCEALPHAFROMDETAIL", distanceAlphaFromDetail);
							pshIndex.Set("SOFT_MASK", softMask);
							pshIndex.Set("OUTLINE", outline);
							pshIndex.Set("OUTER_GLOW", bGlow);
							pshIndex.Set("BLENDTINTBYBASEALPHA", blendTintByBaseAlpha);
							shaderShadow.SetPixelShader("vertexlitgeneric", pshIndex.GetIndex());
						}
					}
					else {
						SetFlags2(parms, MaterialVarFlags2.UsesVertexID);

						StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "vertexlitgeneric");
						vshIndex.Set("VERTEXCOLOR", hasVertexColor || hasVertexAlpha);
						vshIndex.Set("CUBEMAP", hasEnvmap);
						vshIndex.Set("HALFLAMBERT", halfLambert);
						vshIndex.Set("FLASHLIGHT", hasFlashlight);
						vshIndex.Set("SEAMLESS_BASE", seamlessBase);
						vshIndex.Set("SEAMLESS_DETAIL", seamlessDetail);
						vshIndex.Set("SEPARATE_DETAIL_UVS", IsBoolSet(info.SeparateDetailUVs, parms));
						vshIndex.Set("DECAL", isDecal);
						vshIndex.Set("DONT_GAMMA_CONVERT_VERTEX_COLOR", bSRGBWrite ? 0 : 1);
						shaderShadow.SetVertexShader("vertexlitgeneric", vshIndex.GetIndex());

						StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "vertexlitgeneric");
						pshIndex.Set("SELFILLUM_ENVMAPMASK_ALPHA", hasSelfIllumInEnvMapMask && hasEnvmapMask);
						pshIndex.Set("CUBEMAP", hasEnvmap);
						pshIndex.Set("CUBEMAP_SPHERE_LEGACY", hasLegacyEnvSphereMap);
						pshIndex.Set("DIFFUSELIGHTING", hasDiffuseLighting);
						pshIndex.Set("ENVMAPMASK", hasEnvmapMask);
						pshIndex.Set("BASEALPHAENVMAPMASK", hasBaseAlphaEnvmapMask);
						pshIndex.Set("SELFILLUM", hasSelfIllum);
						pshIndex.Set("VERTEXCOLOR", hasVertexColor);
						pshIndex.Set("FLASHLIGHT", hasFlashlight);
						pshIndex.Set("DETAILTEXTURE", hasDetailTexture);
						pshIndex.Set("DETAIL_BLEND_MODE", detailBlendMode);
						pshIndex.Set("SEAMLESS_BASE", seamlessBase);
						pshIndex.Set("SEAMLESS_DETAIL", seamlessDetail);
						pshIndex.Set("DISTANCEALPHA", distanceAlpha);
						pshIndex.Set("DISTANCEALPHAFROMDETAIL", distanceAlphaFromDetail);
						pshIndex.Set("SOFT_MASK", softMask);
						pshIndex.Set("OUTLINE", outline);
						pshIndex.Set("OUTER_GLOW", bGlow);
						pshIndex.Set("FLASHLIGHTDEPTHFILTERMODE", shadowFilterMode);
						pshIndex.Set("DEPTHBLEND", doDepthBlend);
						pshIndex.Set("BLENDTINTBYBASEALPHA", blendTintByBaseAlpha);
						shaderShadow.SetPixelShader("vertexlitgeneric", pshIndex.GetIndex());
					}
				}

				if (hasFlashlight)
					shader.FogToBlack();
				else
					shader.DefaultFog();

				shaderShadow.EnableAlphaWrites(fullyOpaque);
			}

			if (shaderAPI != null && ((contextData == null) || contextData.MaterialVarsChanged)) {
				if (contextData == null) {
					contextData = new();
					context = contextData;
				}
				contextData.SemiStaticCmdsOut.Reset();
				contextData.SemiStaticCmdsOut.SetPixelShaderFogParams(21);
				if (hasBaseTexture)
					contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler0, info.BaseTexture, info.BaseTextureFrame);
				else {
					if (hasEnvmap)
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler0, StandardTextureId.Black);
					else
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler0, StandardTextureId.White);
				}
				if (hasDetailTexture)
					contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler2, info.Detail, info.DetailFrame);
				if (hasSelfIllum) {
					if (hasSelfIllumMask)
						contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler11, info.SelfIllumMask, -1);
					else
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler11, StandardTextureId.Black);
				}

				if ((info.DepthBlend != -1) && (parms[info.DepthBlend].GetIntValue() != 0))
					contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler10, StandardTextureId.FrameBufferFullDepth);
				if (seamlessDetail || seamlessBase) {
					Span<float> flSeamlessData = [parms[info.SeamlessScale].GetFloatValue(), 0, 0, 0];
					contextData.SemiStaticCmdsOut.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst2, flSeamlessData);
				}

				if (info.BaseTextureTransform != -1)
					contextData.SemiStaticCmdsOut.SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst0, info.BaseTextureTransform);


				if (hasDetailTexture) {
					if (IsParamDefined(parms, info.DetailTextureTransform))
						contextData.SemiStaticCmdsOut.SetVertexShaderTextureScaledTransform(VertexShaderConst.ShaderSpecificConst4, info.DetailTextureTransform, info.DetailScale);
					else
						contextData.SemiStaticCmdsOut.SetVertexShaderTextureScaledTransform(VertexShaderConst.ShaderSpecificConst4, info.BaseTextureTransform, info.DetailScale);
					if (info.DetailTint != -1)
						contextData.SemiStaticCmdsOut.SetPixelShaderConstantGammaToLinear(10, info.DetailTint);
					else
						contextData.SemiStaticCmdsOut.SetPixelShaderConstant4(10, 1, 1, 1, 1);
				}

				if (distanceAlpha) {
					float softStart = GetFloatParam(info.EdgeSoftnessStart, parms);
					float softEnd = GetFloatParam(info.EdgeSoftnessEnd, parms);
					bool scaleEdges = IsBoolSet(info.ScaleEdgeSoftnessBasedOnScreenRes, parms);
					bool scaleOutline = IsBoolSet(info.ScaleOutlineSoftnessBasedOnScreenRes, parms);
					float resScale;
					float outlineStart0 = GetFloatParam(info.OutlineStart0, parms);
					float outlineStart1 = GetFloatParam(info.OutlineStart1, parms);
					float outlineEnd0 = GetFloatParam(info.OutlineEnd0, parms);
					float outlineEnd1 = GetFloatParam(info.OutlineEnd1, parms);

					if (scaleEdges || scaleOutline) {
						shaderAPI.GetBackBufferDimensions(out int width, out int height);
						resScale = Math.Max(0.5f, Math.Max(1024.0f / width, 768.0f / height));

						if (scaleEdges) {
							float mid = 0.5f * (softStart + softEnd);
							softStart = Math.Clamp(mid + resScale * (softStart - mid), 0.05f, 0.99f);
							softEnd = Math.Clamp(mid + resScale * (softEnd - mid), 0.05f, 0.99f);
						}


						if (scaleOutline) {
							float midS = 0.5f * (outlineStart1 + outlineStart0);
							outlineStart1 = Math.Clamp(midS + resScale * (outlineStart1 - midS), 0.05f, 0.99f);
							float midE = 0.5f * (outlineEnd1 + outlineEnd0);
							outlineEnd1 = Math.Clamp(midE + resScale * (outlineEnd1 - midE), 0.05f, 0.99f);
						}
					}

					Span<float> consts = [
						GetFloatParam(info.GlowX, parms),
						GetFloatParam(info.GlowY, parms),
						GetFloatParam(info.GlowStart, parms),
						GetFloatParam(info.GlowEnd, parms),
						0,0,0,
						GetFloatParam(info.GlowAlpha, parms),
						softStart,
						softEnd,
						0,0,
						0,0,0,
						GetFloatParam(info.OutlineAlpha, parms),
						outlineStart0,
						outlineEnd1,
						outlineEnd0,
						outlineStart1,
					];

					if (info.GlowColor != -1)
						parms[info.GlowColor].GetVecValue(consts.Slice(4, 3));
					if (info.OutlineColor != -1)
						parms[info.OutlineColor].GetVecValue(consts.Slice(12, 3));
					contextData.SemiStaticCmdsOut.SetPixelShaderConstant(5, consts, 5);

				}
				if (!Config.FastNoBump) {
					if (hasBump)
						contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler3, info.Bumpmap, info.BumpFrame);
					else if (hasDiffuseWarp)
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler3, StandardTextureId.NormalMapFlat);
				}
				else {
					if (hasBump)
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler3, StandardTextureId.NormalMapFlat);
				}

				Span<float> envMapSaturation_SelfIllumMask = [1.0f, 1.0f, 1.0f, 0.0f];
				if (info.EnvmapSaturation != -1)
					parms[info.EnvmapSaturation].GetVecValue(envMapSaturation_SelfIllumMask);

				envMapSaturation_SelfIllumMask[3] = hasSelfIllumMask ? 1.0f : 0.0f;
				contextData.SemiStaticCmdsOut.SetPixelShaderConstant(3, envMapSaturation_SelfIllumMask, 1);
				if (hasEnvmap)
					contextData.SemiStaticCmdsOut.SetEnvMapTintPixelShaderDynamicStateGammaToLinear(0, info.EnvmapTint, fTintReplaceFactor);
				else
					contextData.SemiStaticCmdsOut.SetEnvMapTintPixelShaderDynamicStateGammaToLinear(0, -1, fTintReplaceFactor);

				if (hasEnvmapMask)
					contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler4, info.EnvmapMask, info.EnvmapMaskFrame);

				if (hasSelfIllumFresnel && (!hasFlashlight)) {
					Span<float> vConstScaleBiasExp = [1.0f, 0.0f, 1.0f, 0.0f];
					float min = IsParamDefined(parms, info.SelfIllumFresnelMinMaxExp) ? parms[info.SelfIllumFresnelMinMaxExp].GetVecValue()[0] : 0.0f;
					float max = IsParamDefined(parms, info.SelfIllumFresnelMinMaxExp) ? parms[info.SelfIllumFresnelMinMaxExp].GetVecValue()[1] : 1.0f;
					float exp = IsParamDefined(parms, info.SelfIllumFresnelMinMaxExp) ? parms[info.SelfIllumFresnelMinMaxExp].GetVecValue()[2] : 1.0f;

					vConstScaleBiasExp[1] = (max != 0.0f) ? (min / max) : 0.0f;
					vConstScaleBiasExp[0] = 1.0f - vConstScaleBiasExp[1];
					vConstScaleBiasExp[2] = exp;
					vConstScaleBiasExp[3] = max;

					contextData.SemiStaticCmdsOut.SetPixelShaderConstant(11, vConstScaleBiasExp);
				}

				if (hasDiffuseWarp && (!hasFlashlight) && !hasSelfIllumFresnel) {
					if (r_lightwarpidentity.GetBool()) // TODO
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler9, StandardTextureId.IdentityLightwarp);
					else
						contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler9, info.DiffuseWarpTexture, -1);
				}

				if (hasFlashlight) {
					FlashlightState flashlightState = shaderAPI.GetFlashlightState(out _);
					Span<float> tweaks = [0, 0, 0, 0];
					tweaks[0] = flashlightState.ShadowFilterSize / flashlightState.ShadowMapResolution;
					tweaks[1] = ShadowAttenFromState(flashlightState);
					shader.HashShadow2DJitter(flashlightState.ShadowJitterSeed, out tweaks[2], out tweaks[3]);
					shaderAPI.SetPixelShaderConstant(2, tweaks);

					Span<float> screenScale = [1280.0f / 32.0f, 720.0f / 32.0f, 0, 0];
					shaderAPI.GetBackBufferDimensions(out int width, out int height);
					screenScale[0] = width / 32.0f;
					screenScale[1] = height / 32.0f;
					shaderAPI.SetPixelShaderConstant(31, screenScale);
				}

				if ((!hasFlashlight) && (info.EnvmapContrast != -1))
					contextData.SemiStaticCmdsOut.SetPixelShaderConstant(2, info.EnvmapContrast);

				bool lightingOnly = vertexLitGeneric && mat_fullbright.GetInt() == 2 && false && !IsFlagSet(parms, MaterialVarFlags.NoDebugOverride);
				if (lightingOnly) {
					if (hasBaseTexture) {
						if (hasSelfIllum && !hasSelfIllumInEnvMapMask)
							contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler0, StandardTextureId.GreyAlphaZero);
						else
							contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler0, StandardTextureId.Grey);
					}
					if (hasDetailTexture)
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler2, StandardTextureId.Grey);
				}

				if (hasBump || hasDiffuseWarp) {
					contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler5, StandardTextureId.NormalizationCubemapSigned);
					contextData.SemiStaticCmdsOut.SetPixelShaderStateAmbientLightCube(5);
					contextData.SemiStaticCmdsOut.CommitPixelShaderLighting(13);
				}
				contextData.SemiStaticCmdsOut.SetPixelShaderConstant_W(4, info.SelfIllumTint, blendFactor);
				contextData.SemiStaticCmdsOut.SetAmbientCubeDynamicStateVertexShader();
				contextData.SemiStaticCmdsOut.End();
			}
		}

		if (shaderAPI != null) {
			dynamicCmdsOut.Reset();
			dynamicCmdsOut.Call(contextData!.SemiStaticCmdsOut.Storage);
			if (hasEnvmap)
				dynamicCmdsOut.BindTexture(shader, Sampler.Sampler1, info.Envmap, info.EnvmapFrame);

			bool bFlashlightShadows = false;
			if (hasFlashlight) {
				FlashlightState state = shaderAPI.GetFlashlightStateEx(out Matrix4x4 worldToTexture, out ITexture? pFlashlightDepthTexture);
				bFlashlightShadows = state.EnableShadows && (pFlashlightDepthTexture != null);

				if (pFlashlightDepthTexture != null && Config.ShadowDepthTexture && state.EnableShadows) {
					shader.BindTexture(Sampler.Sampler8, pFlashlightDepthTexture, 0);
					dynamicCmdsOut.BindStandardTexture(Sampler.Sampler6, StandardTextureId.ShadowNoise2D);
				}

				SetFlashLightColorFromState(state, shaderAPI, 28, flashlightNoLambert);

				Assert(info.FlashlightTexture >= 0 && info.FlashlightTextureFrame >= 0);
				shader.BindTexture(Sampler.Sampler7, state.SpotlightTexture, state.SpotlightTextureFrame);
			}

			LightState lightState = default;
			if (vertexLitGeneric && (!hasFlashlight))
				shaderAPI.GetDX9LightState(out lightState);

			MaterialFogMode fogType = shaderAPI.GetSceneFogMode();
			int fogIndex = (fogType == MaterialFogMode.LinearBelowFogZ) ? 1 : 0;
			int numBones = shaderAPI.GetCurrentNumBones();

			bool writeDepthToAlpha;
			bool writeWaterFogToAlpha;
			if (fullyOpaque) {
				writeDepthToAlpha = shaderAPI.ShouldWriteDepthToDestAlpha();
				writeWaterFogToAlpha = fogType == MaterialFogMode.LinearBelowFogZ;
				AssertMsg(!(writeDepthToAlpha && writeWaterFogToAlpha), "Can't write two values to alpha at the same time.");
			}
			else {
				writeDepthToAlpha = false;
				writeWaterFogToAlpha = false;
			}

			if (hasBump || hasDiffuseWarp) {
				if (!HardwareConfig.HasFastVertexTextures()) {
					bool useStaticControlFlow = HardwareConfig.SupportsStaticControlFlow();

					DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
					vshIndex.Set("DOWATERFOG", fogIndex);
					vshIndex.Set("SKINNING", numBones > 0);
					vshIndex.Set("COMPRESSED_VERTS", (int)vertexCompression);
					vshIndex.Set("NUM_LIGHTS", useStaticControlFlow ? 0 : lightState.NumLights);
					dynamicCmdsOut.SetVertexShaderIndex(vshIndex.GetIndex());

					if (HardwareConfig.SupportsPixelShaders_2_b() || HardwareConfig.ShouldAlwaysUseShaderModel2bShaders()) {
						DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
						pshIndex.Set("NUM_LIGHTS", lightState.NumLights);
						pshIndex.Set("AMBIENT_LIGHT", lightState.AmbientLight ? 1 : 0);
						pshIndex.Set("FLASHLIGHTSHADOWS", bFlashlightShadows);
						pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
						dynamicCmdsOut.SetPixelShaderIndex(pshIndex.GetIndex());
					}
					else {
						DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
						pshIndex.Set("NUM_LIGHTS", lightState.NumLights);
						pshIndex.Set("AMBIENT_LIGHT", lightState.AmbientLight ? 1 : 0);
						pshIndex.Set("WRITEWATERFOGTODESTALPHA", writeWaterFogToAlpha);
						pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo());
						dynamicCmdsOut.SetPixelShaderIndex(pshIndex.GetIndex());
					}
				}
				else {
					// shader.SetHWMorphVertexShaderState(VERTEX_SHADER_SHADER_SPECIFIC_CONST_10, VERTEX_SHADER_SHADER_SPECIFIC_CONST_11, SHADER_VERTEXTEXTURE_SAMPLER0);

					DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
					vshIndex.Set("DOWATERFOG", fogIndex);
					vshIndex.Set("SKINNING", numBones > 0);
					vshIndex.Set("MORPHING", shaderAPI.IsHWMorphingEnabled());
					vshIndex.Set("COMPRESSED_VERTS", (int)vertexCompression);
					dynamicCmdsOut.SetVertexShaderIndex(vshIndex.GetIndex());

					DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
					pshIndex.Set("NUM_LIGHTS", lightState.NumLights);
					pshIndex.Set("AMBIENT_LIGHT", lightState.AmbientLight ? 1 : 0);
					pshIndex.Set("FLASHLIGHTSHADOWS", bFlashlightShadows);
					pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
					dynamicCmdsOut.SetPixelShaderIndex(pshIndex.GetIndex());

					Span<bool> unusedTexCoords = [false, false, !shaderAPI.IsHWMorphingEnabled() || !isDecal];
					shaderAPI.MarkUnusedVertexFields(0, unusedTexCoords);
				}
			}
			else {
				if (ambientOnly) {
					lightState.AmbientLight = true;
					lightState.StaticLightVertex = false;
					lightState.NumLights = 0;
				}

				if (!HardwareConfig.HasFastVertexTextures()) {
					bool useStaticControlFlow = HardwareConfig.SupportsStaticControlFlow();

					DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
					vshIndex.Set("DYNAMIC_LIGHT", lightState.HasDynamicLight());
					vshIndex.Set("STATIC_LIGHT", lightState.StaticLightVertex ? 1 : 0);
					vshIndex.Set("DOWATERFOG", fogIndex);
					vshIndex.Set("SKINNING", numBones > 0);
					vshIndex.Set("LIGHTING_PREVIEW", shaderAPI.GetIntRenderingParameter((int)RenderParamInt.EnableFixedLighting) != 0);
					vshIndex.Set("COMPRESSED_VERTS", (int)vertexCompression);
					vshIndex.Set("NUM_LIGHTS", useStaticControlFlow ? 0 : lightState.NumLights);
					dynamicCmdsOut.SetVertexShaderIndex(vshIndex.GetIndex());

					if (HardwareConfig.SupportsPixelShaders_2_b() || HardwareConfig.ShouldAlwaysUseShaderModel2bShaders()) {
						DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
						pshIndex.Set("FLASHLIGHTSHADOWS", bFlashlightShadows);
						pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
						pshIndex.Set("LIGHTING_PREVIEW", shaderAPI.GetIntRenderingParameter((int)RenderParamInt.EnableFixedLighting));
						dynamicCmdsOut.SetPixelShaderIndex(pshIndex.GetIndex());
					}
					else {
						DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
						pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo());
						pshIndex.Set("LIGHTING_PREVIEW", shaderAPI.GetIntRenderingParameter((int)RenderParamInt.EnableFixedLighting));
						dynamicCmdsOut.SetPixelShaderIndex(pshIndex.GetIndex());
					}
				}
				else {
					// shader.SetHWMorphVertexShaderState(VERTEX_SHADER_SHADER_SPECIFIC_CONST_10, VERTEX_SHADER_SHADER_SPECIFIC_CONST_11, SHADER_VERTEXTEXTURE_SAMPLER0);

					DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
					vshIndex.Set("DYNAMIC_LIGHT", lightState.HasDynamicLight());
					vshIndex.Set("STATIC_LIGHT", lightState.StaticLightVertex ? 1 : 0);
					vshIndex.Set("DOWATERFOG", fogIndex);
					vshIndex.Set("SKINNING", numBones > 0);
					vshIndex.Set("LIGHTING_PREVIEW", shaderAPI.GetIntRenderingParameter((int)RenderParamInt.EnableFixedLighting) != 0);
					vshIndex.Set("MORPHING", shaderAPI.IsHWMorphingEnabled());
					vshIndex.Set("COMPRESSED_VERTS", (int)vertexCompression);
					dynamicCmdsOut.SetVertexShaderIndex(vshIndex.GetIndex());

					DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
					pshIndex.Set("FLASHLIGHTSHADOWS", bFlashlightShadows);
					pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
					pshIndex.Set("LIGHTING_PREVIEW", shaderAPI.GetIntRenderingParameter((int)RenderParamInt.EnableFixedLighting));
					dynamicCmdsOut.SetPixelShaderIndex(pshIndex.GetIndex());

					Span<bool> unusedTexCoords = [false, false, !shaderAPI.IsHWMorphingEnabled() || !isDecal];
					shaderAPI.MarkUnusedVertexFields(0, unusedTexCoords);
				}
			}

			if ((info.HDRColorScale != -1) && shader.IsHDREnabled())
				shader.SetModulationPixelShaderDynamicState_LinearColorSpace_LinearScale(1, parms[info.HDRColorScale].GetFloatValue());
			else
				shader.SetModulationPixelShaderDynamicState_LinearColorSpace(1);

			Span<float> eyePos = [0, 0, 0, 0];
			shaderAPI.GetWorldSpaceCameraPosition(eyePos);
			dynamicCmdsOut.SetPixelShaderConstant(20, eyePos);

			if (!hasBump && !hasDiffuseWarp)
				dynamicCmdsOut.SetDepthFeatheringPixelShaderConstant(13, GetFloatParam(info.DepthBlendScale, parms, 50.0f));

			float pixelFogType = shaderAPI.GetPixelFogCombo() == 1 ? 1.0f : 0.0f;
			float fWriteDepthToAlpha = writeDepthToAlpha && IsPC() ? 1.0f : 0.0f;
			float fWriteWaterFogToDestAlpha = (shaderAPI.GetPixelFogCombo() == 1 && writeWaterFogToAlpha) ? 1.0f : 0.0f;
			float vertexAlpha = hasVertexAlpha ? 1.0f : 0.0f;

			Span<float> shaderControls = [pixelFogType, fWriteDepthToAlpha, fWriteWaterFogToDestAlpha, vertexAlpha];
			dynamicCmdsOut.SetPixelShaderConstant(12, shaderControls, 1);

			if (hasFlashlight) {
				FlashlightState flashlightState = shaderAPI.GetFlashlightState(out Matrix4x4 worldToTexture);
				SetFlashLightColorFromState(flashlightState, shaderAPI, 28, flashlightNoLambert);

				Span<float> values = [
					worldToTexture.M11, worldToTexture.M12, worldToTexture.M13, worldToTexture.M14,
					worldToTexture.M21, worldToTexture.M22, worldToTexture.M23, worldToTexture.M24,
					worldToTexture.M31, worldToTexture.M32, worldToTexture.M33, worldToTexture.M34,
					worldToTexture.M41, worldToTexture.M42, worldToTexture.M43, worldToTexture.M44
				];

				shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst6, values, 4);
				shader.BindTexture(Sampler.Sampler7, flashlightState.SpotlightTexture, flashlightState.SpotlightTextureFrame);

				Span<float> atten_pos = [
					flashlightState.ConstantAtten,
					flashlightState.LinearAtten,
					flashlightState.QuadraticAtten,
					flashlightState.FarZ,
					flashlightState.LightOrigin[0],
					flashlightState.LightOrigin[1],
					flashlightState.LightOrigin[2],
					1.0f
				];
				dynamicCmdsOut.SetPixelShaderConstant(22, atten_pos, 2);
				dynamicCmdsOut.SetPixelShaderConstant(24, values, 4);
			}

			dynamicCmdsOut.End();
			shaderAPI.ExecuteCommandBuffer(dynamicCmdsOut.Storage);
		}

		shader.Draw();
	}

	internal void InitVertexLitGeneric(VertexLitGeneric shader, IMaterialVar[] parms, bool vertexLitGeneric, ref VertexLitGeneric_Vars info) {
		if (info.Phong != -1 && parms[info.Phong].GetIntValue() != 0 && HardwareConfig.SupportsPixelShaders_2_b()) {
			InitSkin(shader, parms, ref info);
			return;
		}

		if (info.FlashlightTexture != -1)
			shader.LoadTexture(info.FlashlightTexture, (int)TextureFlags.SRGB);

		bool isBaseTextureTranslucent = false;
		if (info.BaseTexture != -1 && parms[info.BaseTexture].IsDefined()) {
			shader.LoadTexture(info.BaseTexture, (info.GammaColorRead != -1) && (parms[info.GammaColorRead].GetIntValue() == 1) ? 0 : (int)TextureFlags.SRGB);

			if (parms[info.BaseTexture].GetTextureValue()!.IsTranslucent())
				isBaseTextureTranslucent = true;
		}

		bool hasSelfIllumMask = IsFlagSet(parms, MaterialVarFlags.SelfIllum) && (info.SelfIllumMask != -1) && parms[info.SelfIllumMask].IsDefined();

		if (!isBaseTextureTranslucent) {
			bool hasSelfIllumFresnel = IsFlagSet(parms, MaterialVarFlags.SelfIllum) && (info.SelfIllumFresnel != -1) && (parms[info.SelfIllumFresnel].GetIntValue() != 0);

			if (!hasSelfIllumFresnel && !hasSelfIllumMask)
				ClearFlags(parms, MaterialVarFlags.SelfIllum);

			ClearFlags(parms, MaterialVarFlags.BaseAlphaEnvMapMask);
		}

		if (info.Detail != -1 && parms[info.Detail].IsDefined()) {
			int detailBlendMode = (info.DetailTextureCombineMode == -1) ? 0 : parms[info.DetailTextureCombineMode].GetIntValue();
			if (detailBlendMode == 0)
				shader.LoadTexture(info.Detail);
			else
				shader.LoadTexture(info.Detail, (int)TextureFlags.SRGB);
		}

		if (Config.UseBumpmapping()) {
			if ((info.Bumpmap != -1) && parms[info.Bumpmap].IsDefined()) {
				shader.LoadBumpMap(info.Bumpmap);
				SetFlags2(parms, MaterialVarFlags2.DiffuseBumpmappedModel);
			}
			else if ((info.DiffuseWarpTexture != -1) && parms[info.DiffuseWarpTexture].IsDefined())
				SetFlags2(parms, MaterialVarFlags2.DiffuseBumpmappedModel);
		}

		if (IsFlagSet(parms, MaterialVarFlags.SelfIllum) || IsFlagSet(parms, MaterialVarFlags.BaseAlphaEnvMapMask))
			ClearFlags(parms, MaterialVarFlags.AlphaTest);

		if (info.Envmap != -1 && parms[info.Envmap].IsDefined()) {
			if (!IsFlagSet(parms, MaterialVarFlags.EnvMapSphere))
				shader.LoadCubeMap(info.Envmap, HardwareConfig.GetHDRType() == HDRType.None ? (int)TextureFlags.SRGB : 0);
			else
				shader.LoadTexture(info.Envmap, HardwareConfig.GetHDRType() == HDRType.None ? (int)TextureFlags.SRGB : 0);

			if (!HardwareConfig.SupportsCubeMaps())
				SetFlags(parms, MaterialVarFlags.EnvMapSphere);
		}
		if (info.EnvmapMask != -1 && parms[info.EnvmapMask].IsDefined())
			shader.LoadTexture(info.EnvmapMask);

		if ((info.DiffuseWarpTexture != -1) && parms[info.DiffuseWarpTexture].IsDefined())
			shader.LoadTexture(info.DiffuseWarpTexture);

		if (hasSelfIllumMask)
			shader.LoadTexture(info.SelfIllumMask);
	}

	readonly static CommandBufferBuilder<FixedCommandStorageBuffer> dynamicCmdsOut = new() { Storage = new FixedCommandStorageBuffer(1000) };
	static ConVarRef mat_reduceparticles = new("mat_reduceparticles");
}

public class VertexLitGeneric_Context : BasePerMaterialContextData
{
	public readonly CommandBufferBuilder<FixedCommandStorageBuffer> SemiStaticCmdsOut = new() { Storage = new FixedCommandStorageBuffer(800) };
}