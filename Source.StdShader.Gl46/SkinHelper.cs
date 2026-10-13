using Source.Common;
using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

using System.Numerics;

namespace Source.StdShader.Gl46;

public partial class BaseVSShader
{
	internal readonly static ConVar r_rimlight = new("r_rimlight", "1", FCvar.Cheat);

	private void InitParamsSkin(BaseVSShader shader, IMaterialVar[] parms, ReadOnlySpan<char> materialName, ref VertexLitGeneric_Vars info) {
		Assert(info.FlashlightTexture >= 0);

		if (HardwareConfig.SupportsBorderColor())
			parms[(int)ShaderMaterialVars.FlashLightTexture].SetStringValue("effects/flashlight_border");
		else
			parms[(int)ShaderMaterialVars.FlashLightTexture].SetStringValue("effects/flashlight001");

		if (info.Albedo != -1 && Config.UseBumpmapping() && info.Bumpmap != -1 && parms[info.Bumpmap].IsDefined() && parms[info.Albedo].IsDefined() &&
			parms[info.BaseTexture].IsDefined()) {
			parms[info.BaseTexture].SetStringValue(parms[info.Albedo].GetStringValue());
		}

		SetFlags2(parms, MaterialVarFlags2.SupportsHardwareSkinning);
		SetFlags2(parms, MaterialVarFlags2.LightingVertexLit);

		if (!parms[info.BaseTexture].IsDefined())
			ClearFlags(parms, MaterialVarFlags.BaseAlphaEnvMapMask);

		if (IsFlagSet(parms, MaterialVarFlags.Decal))
			SetFlags(parms, MaterialVarFlags.NoDebugOverride);

		bool bump = (info.Bumpmap != -1) && Config.UseBumpmapping() && parms[info.Bumpmap].IsDefined();
		bool envMap = (info.Envmap != -1) && parms[info.Envmap].IsDefined();
		bool diffuseWarp = (info.DiffuseWarpTexture != -1) && parms[info.DiffuseWarpTexture].IsDefined();
		bool phong = (info.Phong != -1) && parms[info.Phong].IsDefined();
		if (bump || envMap || diffuseWarp || phong)
			SetFlags2(parms, MaterialVarFlags2.NeedsTangentSpaces);
		else
			ClearFlags(parms, MaterialVarFlags.NormalMapAlphaEnvMapMask);

		if ((info.SelfIllumFresnel != -1) && (!parms[info.SelfIllumFresnel].IsDefined()))
			parms[info.SelfIllumFresnel].SetIntValue(0);

		if ((info.SelfIllumFresnelMinMaxExp != -1) && (!parms[info.SelfIllumFresnelMinMaxExp].IsDefined()))
			parms[info.SelfIllumFresnelMinMaxExp].SetVecValue(0.0f, 1.0f, 1.0f);

		if ((info.BaseMapAlphaPhongMask != -1) && (!parms[info.BaseMapAlphaPhongMask].IsDefined()))
			parms[info.BaseMapAlphaPhongMask].SetIntValue(0);

		if ((info.EnvmapFresnel != -1) && (!parms[info.EnvmapFresnel].IsDefined()))
			parms[info.EnvmapFresnel].SetFloatValue(0);
	}

	public static readonly ConVar r_flashlight_version2 = new("r_flashlight_version2", "0", FCvar.Cheat | FCvar.DevelopmentOnly);
	internal void DrawSkin(BaseVSShader shader, IMaterialVar[] parms, IShaderDynamicAPI? shaderAPI, IShaderShadow? shaderShadow, ref VertexLitGeneric_Vars info, VertexCompressionType vertexCompression, ref BasePerMaterialContextData? contextData) {
		bool hasFlashlight = shader.UsingFlashlight(parms);

		if (hasFlashlight || r_flashlight_version2.GetBool()) {
			DrawSkin_Internal(shader, parms, shaderAPI, shaderShadow, false, ref info, vertexCompression, ref contextData);
			if (shaderShadow != null)
				SetInitialShadowState();
		}

		DrawSkin_Internal(shader, parms, shaderAPI, shaderShadow, hasFlashlight, ref info, vertexCompression, ref contextData);
	}

	internal void InitSkin(BaseVSShader shader, IMaterialVar[] parms, ref VertexLitGeneric_Vars info) {
		Assert(info.FlashlightTexture >= 0);
		shader.LoadTexture(info.FlashlightTexture, (int)TextureFlags.SRGB);

		bool isBaseTextureTranslucent = false;
		if (parms[info.BaseTexture].IsDefined()) {
			shader.LoadTexture(info.BaseTexture, (int)TextureFlags.SRGB);

			if (parms[info.BaseTexture].GetTextureValue()!.IsTranslucent())
				isBaseTextureTranslucent = true;

			if ((info.Wrinkle != -1) && (info.Stretch != -1) &&
				parms[info.Wrinkle].IsDefined() && parms[info.Stretch].IsDefined()) {
				shader.LoadTexture(info.Wrinkle, (int)TextureFlags.SRGB);
				shader.LoadTexture(info.Stretch, (int)TextureFlags.SRGB);
			}
		}

		bool hasSelfIllumMask = IsFlagSet(parms, MaterialVarFlags.SelfIllum) && (info.SelfIllumMask != -1) && parms[info.SelfIllumMask].IsDefined();

		if (!isBaseTextureTranslucent) {
			bool hasSelfIllumFresnel = IsFlagSet(parms, MaterialVarFlags.SelfIllum) && (info.SelfIllumFresnel != -1) && (parms[info.SelfIllumFresnel].GetIntValue() != 0);

			if (!hasSelfIllumFresnel && !hasSelfIllumMask)
				ClearFlags(parms, MaterialVarFlags.SelfIllum);

			ClearFlags(parms, MaterialVarFlags.BaseAlphaEnvMapMask);
		}

		if ((info.PhongExponentTexture != -1) && parms[info.PhongExponentTexture].IsDefined() && (info.Phong != -1) && parms[info.Phong].IsDefined())
			shader.LoadTexture(info.PhongExponentTexture);

		if ((info.DiffuseWarpTexture != -1) && parms[info.DiffuseWarpTexture].IsDefined() && (info.Phong != -1) && parms[info.Phong].IsDefined())
			shader.LoadTexture(info.DiffuseWarpTexture);

		if ((info.PhongWarpTexture != -1) && parms[info.PhongWarpTexture].IsDefined() && (info.Phong != -1) && parms[info.Phong].IsDefined())
			shader.LoadTexture(info.PhongWarpTexture);

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

				if ((info.NormalWrinkle != -1) && (info.NormalStretch != -1) &&
					parms[info.NormalWrinkle].IsDefined() && parms[info.NormalStretch].IsDefined()) {
					shader.LoadTexture(info.NormalWrinkle);
					shader.LoadTexture(info.NormalStretch);
				}
			}
		}

		if (parms[info.Envmap].IsDefined())
			shader.LoadCubeMap(info.Envmap, HardwareConfig.GetHDRType() == HDRType.None ? (int)TextureFlags.SRGB : 0);

		if (hasSelfIllumMask)
			shader.LoadTexture(info.SelfIllumMask);
	}

	private void DrawSkin_Internal(BaseVSShader shader, IMaterialVar[] parms, IShaderDynamicAPI? shaderAPI, IShaderShadow? shaderShadow, bool hasFlashlight, ref VertexLitGeneric_Vars info, VertexCompressionType vertexCompression, ref BasePerMaterialContextData? context) {
		bool hasBaseTexture = (info.BaseTexture != -1) && parms[info.BaseTexture].IsTexture();
		bool hasBump = (info.Bumpmap != -1) && parms[info.Bumpmap].IsTexture();
		bool hasBaseWrinkleTexture = hasBaseTexture && (info.Wrinkle != -1) && parms[info.Wrinkle].IsTexture() && (info.Stretch != -1) && parms[info.Stretch].IsTexture();
		bool hasBumpWrinkle = hasBump && (info.NormalWrinkle != -1) && parms[info.NormalWrinkle].IsTexture() && (info.NormalStretch != -1) && parms[info.NormalStretch].IsTexture();
		bool hasVertexColor = IsFlagSet(parms, MaterialVarFlags.VertexColor);
		bool hasVertexAlpha = IsFlagSet(parms, MaterialVarFlags.VertexAlpha);
		bool isAlphaTested = IsFlagSet(parms, MaterialVarFlags.AlphaTest);
		bool hasSelfIllum = IsFlagSet(parms, MaterialVarFlags.SelfIllum);
		bool hasSelfIllumFresnel = hasSelfIllum && (info.SelfIllumFresnel != -1) && (parms[info.SelfIllumFresnel].GetIntValue() != 0);
		bool hasSelfIllumMask = hasSelfIllum && (info.SelfIllumMask != -1) && parms[info.SelfIllumMask].IsTexture();
		bool hasPhong = (info.Phong != -1) && (parms[info.Phong].GetIntValue() != 0);
		bool hasSpecularExponentTexture = (info.PhongExponentTexture != -1) && parms[info.PhongExponentTexture].IsTexture();
		bool hasPhongTintMap = hasSpecularExponentTexture && (info.PhongAlbedoTint != -1) && (parms[info.PhongAlbedoTint].GetIntValue() != 0);
		bool hasDiffuseWarp = (info.DiffuseWarpTexture != -1) && parms[info.DiffuseWarpTexture].IsTexture();
		bool hasPhongWarp = (info.PhongWarpTexture != -1) && parms[info.PhongWarpTexture].IsTexture();
		bool hasNormalMapAlphaEnvmapMask = IsFlagSet(parms, MaterialVarFlags.NormalMapAlphaEnvMapMask);
		bool isDecal = IsFlagSet(parms, MaterialVarFlags.Decal);
		bool hasRimLight = r_rimlight.GetBool() && hasPhong && (info.RimLight != -1) && (parms[info.RimLight].GetIntValue() != 0);
		bool hasRimMapMask = hasSpecularExponentTexture && hasRimLight && (info.RimMask != -1) && (parms[info.RimMask].GetIntValue() != 0);
		float blendFactor = (info.DetailTextureBlendFactor == -1) ? 1 : parms[info.DetailTextureBlendFactor].GetFloatValue();
		bool hasDetailTexture = (info.Detail != -1) && parms[info.Detail].IsTexture();
		int detailBlendMode = (hasDetailTexture && info.DetailTextureCombineMode != -1) ? parms[info.DetailTextureCombineMode].GetIntValue() : 0;
		bool blendTintByBaseAlpha = IsBoolSet(info.BlendTintByBaseAlpha, parms) && !hasSelfIllum;
		float tintReplacementAmount = GetFloatParam(info.TintReplacesBaseColor, parms);

		BlendType blendType = shader.EvaluateBlendRequirements(blendTintByBaseAlpha ? -1 : info.BaseTexture, true);

		bool fullyOpaque = (blendType != BlendType.BlendAdd) && (blendType != BlendType.Blend) && !isAlphaTested && !hasFlashlight;

		if (context is not Skin_Context contextData) {
			contextData = new();
			context = contextData;
		}

		if (shader.IsSnapshotting()) {
			bool hasEnvmap = !hasFlashlight && parms[info.Envmap].IsTexture();
			bool hasNormal = parms[info.Bumpmap].IsTexture();
			bool canUseBaseAlphaPhongMaskFastPath = (info.BaseMapAlphaPhongMask != -1) && (parms[info.BaseMapAlphaPhongMask].GetIntValue() != 0);

			if (!parms[info.BaseTexture].GetTextureValue()!.IsTranslucent())
				canUseBaseAlphaPhongMaskFastPath = true;

			contextData.FastPath =
				(!hasBump) &&
				(!hasSpecularExponentTexture) &&
				(!hasPhongTintMap) &&
				(!hasPhongWarp) &&
				(!hasRimLight) &&
				(!hasDetailTexture) &&
				canUseBaseAlphaPhongMaskFastPath &&
				(!hasSelfIllum) &&
				(!blendTintByBaseAlpha);

			shaderShadow!.EnableAlphaTest(isAlphaTested);

			if (info.AlphaTestReference != -1 && parms[info.AlphaTestReference].GetFloatValue() > 0.0f)
				shaderShadow.AlphaFunc(ShaderAlphaFunc.GreaterEqual, parms[info.AlphaTestReference].GetFloatValue());

			int shadowFilterMode = 0;
			if (hasFlashlight) {
				if (parms[info.BaseTexture].IsTexture())
					shader.SetAdditiveBlendingShadowState(info.BaseTexture, true);

				if (isAlphaTested) {
					shaderShadow.EnableAlphaTest(false);
					shaderShadow.DepthFunc(ShaderDepthFunc.Equal);
				}
				shaderShadow.EnableBlending(true);
				shaderShadow.EnableDepthWrites(false);

				shaderShadow.EnableAlphaWrites(false);

				shadowFilterMode = HardwareConfig.GetShadowFilterMode();
			}
			else {
				if (parms[info.BaseTexture].IsTexture())
					shader.SetDefaultBlendingShadowState(info.BaseTexture, true);

				if (hasEnvmap) {
					shaderShadow.EnableTexture(Sampler.Sampler8, true);
					if (HardwareConfig.GetHDRType() == HDRType.None)
						shaderShadow.EnableSRGBRead(Sampler.Sampler8, true);
				}
			}

			VertexFormat flags = VertexFormat.Position;
			if (hasNormal)
				flags |= VertexFormat.Normal;

			int userDataSize = 0;

			shaderShadow.EnableTexture(Sampler.Sampler0, true);
			shaderShadow.EnableSRGBRead(Sampler.Sampler0, true);

			if (hasBaseWrinkleTexture || hasBumpWrinkle) {
				shaderShadow.EnableTexture(Sampler.Sampler9, true);
				shaderShadow.EnableSRGBRead(Sampler.Sampler9, true);
				shaderShadow.EnableTexture(Sampler.Sampler10, true);
				shaderShadow.EnableSRGBRead(Sampler.Sampler10, true);
			}

			if (hasDiffuseWarp)
				shaderShadow.EnableTexture(Sampler.Sampler2, true);

			if (hasPhongWarp)
				shaderShadow.EnableTexture(Sampler.Sampler1, true);

			shaderShadow.EnableTexture(Sampler.Sampler7, true);

			if (hasFlashlight) {
				shaderShadow.EnableTexture(Sampler.Sampler4, true);
				shaderShadow.SetShadowDepthFiltering(Sampler.Sampler4);
				shaderShadow.EnableSRGBRead(Sampler.Sampler4, false);
				shaderShadow.EnableTexture(Sampler.Sampler5, true);
				shaderShadow.EnableTexture(Sampler.Sampler6, true);
				shaderShadow.EnableSRGBRead(Sampler.Sampler6, true);
				userDataSize = 4;
			}

			shaderShadow.EnableTexture(Sampler.Sampler3, true);
			userDataSize = 4;
			shaderShadow.EnableTexture(Sampler.Sampler5, true);

			if (hasBaseWrinkleTexture || hasBumpWrinkle) {
				shaderShadow.EnableTexture(Sampler.Sampler11, true);
				shaderShadow.EnableTexture(Sampler.Sampler12, true);
			}

			if (hasDetailTexture) {
				shaderShadow.EnableTexture(Sampler.Sampler13, true);
				if (detailBlendMode != 0)
					shaderShadow.EnableSRGBRead(Sampler.Sampler13, true);
			}

			if (hasSelfIllum)
				shaderShadow.EnableTexture(Sampler.Sampler14, true);

			if (hasVertexColor || hasVertexAlpha)
				flags |= VertexFormat.Color;

			shaderShadow.EnableSRGBWrite(true);

			Span<int> texCoordDim = [2, 0, 3];
			int texCoordCount = 1;

			if (isDecal && HardwareConfig.HasFastVertexTextures())
				texCoordCount = 3;

			// flags |= VertexFormat.Compressed;

			shaderShadow.VertexShaderVertexFormat(flags, texCoordCount, texCoordDim, userDataSize);


			if (!HardwareConfig.HasFastVertexTextures()) {
				bool useStaticControlFlow = HardwareConfig.SupportsStaticControlFlow();

				StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "skin");
				vshIndex.Set("USE_STATIC_CONTROL_FLOW", useStaticControlFlow);
				shaderShadow.SetVertexShader("skin", vshIndex.GetIndex());

				StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "skin");
				pshIndex.Set("FLASHLIGHT", hasFlashlight);
				pshIndex.Set("SELFILLUM", hasSelfIllum && !hasFlashlight);
				pshIndex.Set("SELFILLUMFRESNEL", hasSelfIllumFresnel && !hasFlashlight);
				pshIndex.Set("LIGHTWARPTEXTURE", hasDiffuseWarp && hasPhong);
				pshIndex.Set("PHONGWARPTEXTURE", hasPhongWarp && hasPhong);
				pshIndex.Set("WRINKLEMAP", hasBaseWrinkleTexture || hasBumpWrinkle);
				pshIndex.Set("DETAILTEXTURE", hasDetailTexture);
				pshIndex.Set("DETAIL_BLEND_MODE", detailBlendMode);
				pshIndex.Set("RIMLIGHT", hasRimLight);
				pshIndex.Set("CUBEMAP", hasEnvmap);
				pshIndex.Set("FLASHLIGHTDEPTHFILTERMODE", shadowFilterMode);
				pshIndex.Set("CONVERT_TO_SRGB", 0);
				pshIndex.Set("FASTPATH_NOBUMP", contextData.FastPath);
				pshIndex.Set("BLENDTINTBYBASEALPHA", blendTintByBaseAlpha);
				shaderShadow.SetPixelShader("skin", pshIndex.GetIndex());
			}
			else {
				SetFlags2(parms, MaterialVarFlags2.UsesVertexID);

				StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "skin");
				vshIndex.Set("DECAL", isDecal);
				shaderShadow.SetVertexShader("skin", vshIndex.GetIndex());

				StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "skin");
				pshIndex.Set("FLASHLIGHT", hasFlashlight);
				pshIndex.Set("SELFILLUM", hasSelfIllum && !hasFlashlight);
				pshIndex.Set("SELFILLUMFRESNEL", hasSelfIllumFresnel && !hasFlashlight);
				pshIndex.Set("LIGHTWARPTEXTURE", hasDiffuseWarp && hasPhong);
				pshIndex.Set("PHONGWARPTEXTURE", hasPhongWarp && hasPhong);
				pshIndex.Set("WRINKLEMAP", hasBaseWrinkleTexture || hasBumpWrinkle);
				pshIndex.Set("DETAILTEXTURE", hasDetailTexture);
				pshIndex.Set("DETAIL_BLEND_MODE", detailBlendMode);
				pshIndex.Set("RIMLIGHT", hasRimLight);
				pshIndex.Set("CUBEMAP", hasEnvmap);
				pshIndex.Set("FLASHLIGHTDEPTHFILTERMODE", shadowFilterMode);
				pshIndex.Set("CONVERT_TO_SRGB", 0);
				pshIndex.Set("FASTPATH_NOBUMP", contextData.FastPath);
				pshIndex.Set("BLENDTINTBYBASEALPHA", blendTintByBaseAlpha);
				shaderShadow.SetPixelShader("skin", pshIndex.GetIndex());
			}

			if (hasFlashlight)
				shader.FogToBlack();
			else
				shader.DefaultFog();

			shaderShadow.EnableAlphaWrites(fullyOpaque);
		}
		else if (shaderAPI != null) {
			bool lightingOnly = mat_fullbright.GetInt() == 2 && !IsFlagSet(parms, MaterialVarFlags.NoDebugOverride);
			bool hasEnvmap = !hasFlashlight && parms[info.Envmap].IsTexture();

			if (hasBaseTexture)
				shader.BindTexture(Sampler.Sampler0, info.BaseTexture, info.BaseTextureFrame);
			else
				shaderAPI.BindStandardTexture(Sampler.Sampler0, StandardTextureId.White);

			if (hasBaseWrinkleTexture) {
				shader.BindTexture(Sampler.Sampler9, info.Wrinkle, info.BaseTextureFrame);
				shader.BindTexture(Sampler.Sampler10, info.Stretch, info.BaseTextureFrame);
			}
			else if (hasBumpWrinkle) {
				shader.BindTexture(Sampler.Sampler9, info.BaseTexture, info.BaseTextureFrame);
				shader.BindTexture(Sampler.Sampler10, info.BaseTexture, info.BaseTextureFrame);
			}

			if (hasDiffuseWarp && hasPhong) {
				if (r_lightwarpidentity.GetBool())
					shaderAPI.BindStandardTexture(Sampler.Sampler2, StandardTextureId.IdentityLightwarp);
				else
					shader.BindTexture(Sampler.Sampler2, info.DiffuseWarpTexture);
			}

			if (hasPhongWarp)
				shader.BindTexture(Sampler.Sampler1, info.PhongWarpTexture);

			if (hasSpecularExponentTexture && hasPhong)
				shader.BindTexture(Sampler.Sampler7, info.PhongExponentTexture);
			else
				shaderAPI.BindStandardTexture(Sampler.Sampler7, StandardTextureId.White);

			if (!Config.FastNoBump) {
				if (hasBump)
					shader.BindTexture(Sampler.Sampler3, info.Bumpmap, info.BumpFrame);
				else
					shaderAPI.BindStandardTexture(Sampler.Sampler3, StandardTextureId.NormalMapFlat);

				if (hasBumpWrinkle) {
					shader.BindTexture(Sampler.Sampler11, info.NormalWrinkle, info.BumpFrame);
					shader.BindTexture(Sampler.Sampler12, info.NormalStretch, info.BumpFrame);
				}
				else if (hasBaseWrinkleTexture) {
					shader.BindTexture(Sampler.Sampler11, info.Bumpmap, info.BumpFrame);
					shader.BindTexture(Sampler.Sampler12, info.Bumpmap, info.BumpFrame);
				}
			}
			else {
				if (hasBump)
					shaderAPI.BindStandardTexture(Sampler.Sampler3, StandardTextureId.NormalMapFlat);
				if (hasBaseWrinkleTexture || hasBumpWrinkle) {
					shaderAPI.BindStandardTexture(Sampler.Sampler11, StandardTextureId.NormalMapFlat);
					shaderAPI.BindStandardTexture(Sampler.Sampler12, StandardTextureId.NormalMapFlat);
				}
			}

			if (hasDetailTexture)
				shader.BindTexture(Sampler.Sampler13, info.Detail, info.DetailFrame);

			if (hasSelfIllum) {
				if (hasSelfIllumMask)
					shader.BindTexture(Sampler.Sampler14, info.SelfIllumMask);
				else
					shaderAPI.BindStandardTexture(Sampler.Sampler14, StandardTextureId.Black);
			}

			LightState lightState = default;
			bool flashlightShadows = false;
			if (hasFlashlight) {
				Assert(info.FlashlightTexture >= 0 && info.FlashlightTextureFrame >= 0);
				shader.BindTexture(Sampler.Sampler6, info.FlashlightTexture, info.FlashlightTextureFrame);
				FlashlightState state = ShaderAPI!.GetFlashlightStateEx(out _, out ITexture? flashlightDepthTexture);
				flashlightShadows = state.EnableShadows && (flashlightDepthTexture != null);

				SetFlashLightColorFromState(state, ShaderAPI, (int)PixelShaderConst.FlashlightColor);

				if (flashlightDepthTexture != null && Config.ShadowDepthTexture && state.EnableShadows) {
					shader.BindTexture(Sampler.Sampler4, flashlightDepthTexture, 0);
					ShaderAPI.BindStandardTexture(Sampler.Sampler5, StandardTextureId.ShadowNoise2D);
				}
			}
			else {
				if (hasEnvmap)
					shader.BindTexture(Sampler.Sampler8, info.Envmap, info.EnvmapFrame);

				shaderAPI.GetDX9LightState(out lightState);
			}

			MaterialFogMode fogType = shaderAPI.GetSceneFogMode();
			int fogIndex = (fogType == MaterialFogMode.LinearBelowFogZ) ? 1 : 0;
			int numBones = shaderAPI.GetCurrentNumBones();

			bool writeDepthToAlpha = false;
			bool writeWaterFogToAlpha = false;
			if (fullyOpaque) {
				writeDepthToAlpha = shaderAPI.ShouldWriteDepthToDestAlpha();
				writeWaterFogToAlpha = fogType == MaterialFogMode.LinearBelowFogZ;
				AssertMsg(!(writeDepthToAlpha && writeWaterFogToAlpha), "Can't write two values to alpha at the same time.");
			}

			if (!HardwareConfig.HasFastVertexTextures()) {
				bool useStaticControlFlow = HardwareConfig.SupportsStaticControlFlow();

				DynamicShaderIndex vshIndex = new(shaderAPI!, ShaderType.Vertex);
				vshIndex.Set("DOWATERFOG", fogIndex);
				vshIndex.Set("SKINNING", numBones > 0);
				vshIndex.Set("LIGHTING_PREVIEW", shaderAPI.GetIntRenderingParameter((int)RenderParamInt.EnableFixedLighting) != 0);
				vshIndex.Set("COMPRESSED_VERTS", (int)vertexCompression);
				vshIndex.Set("NUM_LIGHTS", useStaticControlFlow ? 0 : lightState.NumLights);
				shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

				DynamicShaderIndex pshIndex = new(shaderAPI!, ShaderType.Pixel);
				pshIndex.Set("NUM_LIGHTS", lightState.NumLights);
				pshIndex.Set("WRITEWATERFOGTODESTALPHA", writeWaterFogToAlpha);
				pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", writeDepthToAlpha);
				pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
				pshIndex.Set("FLASHLIGHTSHADOWS", flashlightShadows);
				shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
			}
			else {
				// shader.SetHWMorphVertexShaderState(VertexShaderConst.ShaderSpecificConst6, VertexShaderConst.ShaderSpecificConst7, SHADER_VERTEXTEXTURE_SAMPLER0);

				DynamicShaderIndex vshIndex = new(shaderAPI!, ShaderType.Vertex);
				vshIndex.Set("DOWATERFOG", fogIndex);
				vshIndex.Set("SKINNING", numBones > 0);
				vshIndex.Set("LIGHTING_PREVIEW", shaderAPI.GetIntRenderingParameter((int)RenderParamInt.EnableFixedLighting) != 0);
				vshIndex.Set("MORPHING", shaderAPI.IsHWMorphingEnabled());
				vshIndex.Set("COMPRESSED_VERTS", (int)vertexCompression);
				shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

				DynamicShaderIndex pshIndex = new(shaderAPI!, ShaderType.Pixel);
				pshIndex.Set("NUM_LIGHTS", lightState.NumLights);
				pshIndex.Set("WRITEWATERFOGTODESTALPHA", writeWaterFogToAlpha);
				pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", writeDepthToAlpha);
				pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
				pshIndex.Set("FLASHLIGHTSHADOWS", flashlightShadows);
				shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());

				Span<bool> unusedTexCoords = [false, false, !shaderAPI.IsHWMorphingEnabled() || !isDecal];
				shaderAPI.MarkUnusedVertexFields(0, unusedTexCoords);
			}

			shader.SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst0, info.BaseTextureTransform);

			if (hasBump)
				shader.SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst2, info.BumpTransform);

			if (hasDetailTexture) {
				if (IsParamDefined(parms, info.DetailTextureTransform))
					shader.SetVertexShaderTextureScaledTransform(VertexShaderConst.ShaderSpecificConst4, info.DetailTextureTransform, info.DetailScale);
				else
					shader.SetVertexShaderTextureScaledTransform(VertexShaderConst.ShaderSpecificConst4, info.BaseTextureTransform, info.DetailScale);
			}

			shader.SetModulationPixelShaderDynamicState_LinearColorSpace(1);
			shader.SetPixelShaderConstant_W((int)PixelShaderConst.SelfIllumTint, info.SelfIllumTint, blendFactor);
			bool invertPhongMask = (info.InvertPhongMask != -1) && (parms[info.InvertPhongMask].GetIntValue() != 0);
			float fInvertPhongMask = invertPhongMask ? 1 : 0;

			bool hasBaseAlphaPhongMask = (info.BaseMapAlphaPhongMask != -1) && (parms[info.BaseMapAlphaPhongMask].GetIntValue() != 0);
			float fHasBaseAlphaPhongMask = hasBaseAlphaPhongMask ? 1 : 0;
			Span<float> shaderControls = [fHasBaseAlphaPhongMask, 0.0f, tintReplacementAmount, fInvertPhongMask];
			shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.Constant27, shaderControls);

			if (hasSelfIllumFresnel && !hasFlashlight) {
				Span<float> constScaleBiasExp = [1.0f, 0.0f, 1.0f, 0.0f];
				float min = IsParamDefined(parms, info.SelfIllumFresnelMinMaxExp) ? parms[info.SelfIllumFresnelMinMaxExp].GetVecValue()[0] : 0.0f;
				float max = IsParamDefined(parms, info.SelfIllumFresnelMinMaxExp) ? parms[info.SelfIllumFresnelMinMaxExp].GetVecValue()[1] : 1.0f;
				float exp = IsParamDefined(parms, info.SelfIllumFresnelMinMaxExp) ? parms[info.SelfIllumFresnelMinMaxExp].GetVecValue()[2] : 1.0f;

				constScaleBiasExp[1] = (max != 0.0f) ? (min / max) : 0.0f;
				constScaleBiasExp[0] = 1.0f - constScaleBiasExp[1];
				constScaleBiasExp[2] = exp;
				constScaleBiasExp[3] = max;

				shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.SelfIllumScaleBiasExp, constScaleBiasExp);
			}

			shader.SetAmbientCubeDynamicStateVertexShader();

			if (!hasFlashlight) {
				shaderAPI.BindStandardTexture(Sampler.Sampler5, StandardTextureId.NormalizationCubemapSigned);

				Span<float> envMapFresnel_SelfIllumMask = [0.0f, 0.0f, 0.0f, 0.0f];
				envMapFresnel_SelfIllumMask[3] = hasSelfIllumMask ? 1.0f : 0.0f;

				if (hasEnvmap) {
					Span<float> envMapTint_MaskControl = [1.0f, 1.0f, 1.0f, 0.0f];

					if ((info.EnvmapTint != -1) && parms[info.EnvmapTint].IsDefined())
						parms[info.EnvmapTint].GetVecValue(envMapTint_MaskControl);

					envMapTint_MaskControl[3] = hasNormalMapAlphaEnvmapMask ? 1.0f : 0.0f;

					if ((info.EnvmapFresnel != -1) && parms[info.EnvmapFresnel].IsDefined())
						envMapFresnel_SelfIllumMask[0] = parms[info.EnvmapFresnel].GetFloatValue();

					if (lightingOnly)
						envMapTint_MaskControl[0] = envMapTint_MaskControl[1] = envMapTint_MaskControl[2] = 0.0f;

					shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.EnvMapTintShadowTweaks, envMapTint_MaskControl);
				}

				shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.EnvMapFresnelSelfIllumMask, envMapFresnel_SelfIllumMask);
			}

			shaderAPI.SetPixelShaderStateAmbientLightCube((int)PixelShaderConst.AmbientCube, !lightState.AmbientLight);
			shaderAPI.CommitPixelShaderLighting((int)PixelShaderConst.LightInfoArray);

			Span<float> eyePos_SpecExponent = [0, 0, 0, 0], fresnelRanges_SpecBoost = [1, 0.5f, 1, 1], vRimBoost = [1, 1, 1, 1];
			Span<float> specularTint = [1, 1, 1, 4];
			shaderAPI.GetWorldSpaceCameraPosition(eyePos_SpecExponent);

			eyePos_SpecExponent[3] = -1.0f;
			if ((info.PhongExponent != -1) && parms[info.PhongExponent].IsDefined()) {
				float value = parms[info.PhongExponent].GetFloatValue();
				if (value > 0.0f)
					eyePos_SpecExponent[3] = value;
			}

			if ((info.PhongTint != -1) && parms[info.PhongTint].IsDefined())
				parms[info.PhongTint].GetVecValue(specularTint[..3]);

			if (hasRimLight && (info.RimLightPower != -1) && parms[info.RimLightPower].IsDefined()) {
				specularTint[3] = parms[info.RimLightPower].GetFloatValue();
				specularTint[3] = Math.Max(specularTint[3], 1.0f);
			}

			if (hasRimLight && (info.RimLightBoost != -1) && parms[info.RimLightBoost].IsDefined())
				vRimBoost[3] = parms[info.RimLightBoost].GetFloatValue();

			if (!hasFlashlight) {
				Span<float> rimMaskControl = [0, 0, 0, 0];
				rimMaskControl[0] = hasRimMapMask ? parms[info.RimMask].GetFloatValue() : 0.0f;
				shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.FlashlightAttenuation, rimMaskControl);
			}

			if ((specularTint[0] == 0.0f) && (specularTint[1] == 0.0f) && (specularTint[2] == 0.0f)) {
				if (hasPhongTintMap)
					specularTint[0] = -1;
				else {
					specularTint[0] = 1.0f;
					specularTint[1] = 1.0f;
					specularTint[2] = 1.0f;
				}
			}

			if (lightingOnly) {
				if (hasSelfIllum && !hasFlashlight)
					shaderAPI.BindStandardTexture(Sampler.Sampler0, StandardTextureId.GreyAlphaZero);
				else
					shaderAPI.BindStandardTexture(Sampler.Sampler0, StandardTextureId.Grey);

				if (hasDetailTexture)
					shaderAPI.BindStandardTexture(Sampler.Sampler13, StandardTextureId.Grey);

				specularTint[0] = specularTint[1] = specularTint[2] = 0.0f;
			}

			if ((info.PhongFresnelRanges != -1) && parms[info.PhongFresnelRanges].IsDefined()) {
				parms[info.PhongFresnelRanges].GetVecValue(fresnelRanges_SpecBoost[..3]);
				fresnelRanges_SpecBoost[0] = (fresnelRanges_SpecBoost[1] - fresnelRanges_SpecBoost[0]) * 2;
				fresnelRanges_SpecBoost[2] = (fresnelRanges_SpecBoost[2] - fresnelRanges_SpecBoost[1]) * 2;
			}

			if ((info.PhongBoost != -1) && parms[info.PhongBoost].IsDefined())
				fresnelRanges_SpecBoost[3] = parms[info.PhongBoost].GetFloatValue();
			else
				fresnelRanges_SpecBoost[3] = 1.0f;

			shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.EyePosSpecExponent, eyePos_SpecExponent);
			shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.FresnelSpecParams, fresnelRanges_SpecBoost);
			shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.FlashlightPositionRimBoost, vRimBoost);
			shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.SpecRimParams, specularTint);
			shaderAPI.SetPixelShaderFogParams((int)PixelShaderConst.FogParams);

			if (hasFlashlight) {
				Span<float> atten = [0, 0, 0, 0], pos = [0, 0, 0, 0], tweaks = [0, 0, 0, 0];

				FlashlightState flashlightState = shaderAPI.GetFlashlightState(out Matrix4x4 worldToTexture);
				SetFlashLightColorFromState(flashlightState, shaderAPI, (int)PixelShaderConst.FlashlightColor);

				shader.BindTexture(Sampler.Sampler6, flashlightState.SpotlightTexture, flashlightState.SpotlightTextureFrame);

				atten[0] = flashlightState.ConstantAtten;
				atten[1] = flashlightState.LinearAtten;
				atten[2] = flashlightState.QuadraticAtten;
				atten[3] = flashlightState.FarZ;
				shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.FlashlightAttenuation, atten);

				pos[0] = flashlightState.LightOrigin[0];
				pos[1] = flashlightState.LightOrigin[1];
				pos[2] = flashlightState.LightOrigin[2];
				shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.FlashlightPositionRimBoost, pos);

				Span<float> values = [
					worldToTexture.M11, worldToTexture.M12, worldToTexture.M13, worldToTexture.M14,
					worldToTexture.M21, worldToTexture.M22, worldToTexture.M23, worldToTexture.M24,
					worldToTexture.M31, worldToTexture.M32, worldToTexture.M33, worldToTexture.M34,
					worldToTexture.M41, worldToTexture.M42, worldToTexture.M43, worldToTexture.M44
				];
				shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.FlashlightToWorldTexture, values, 4);

				tweaks[0] = ShadowFilterFromState(flashlightState);
				tweaks[1] = ShadowAttenFromState(flashlightState);
				shader.HashShadow2DJitter(flashlightState.ShadowJitterSeed, out tweaks[2], out tweaks[3]);
				shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.EnvMapTintShadowTweaks, tweaks);

				Span<float> screenScale = [1280.0f / 32.0f, 720.0f / 32.0f, 0, 0];
				shaderAPI.GetBackBufferDimensions(out int width, out int height);
				screenScale[0] = width / 32.0f;
				screenScale[1] = height / 32.0f;
				shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.FlashlightScreenScale, screenScale);
			}
		}
		shader.Draw();
	}
}

class Skin_Context : BasePerMaterialContextData
{
	public readonly CommandBufferBuilder<FixedCommandStorageBuffer> SemiStaticCmdsOut = new() { Storage = new FixedCommandStorageBuffer(800) };
	public bool FastPath;
};