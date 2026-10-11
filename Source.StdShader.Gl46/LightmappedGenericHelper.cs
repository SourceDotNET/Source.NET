using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Source.StdShader.Gl46;

public partial class BaseVSShader
{
	static readonly ConVar mat_disable_lightwarp = new("mat_disable_lightwarp", "0");
	static readonly ConVar mat_disable_fancy_blending = new("mat_disable_fancy_blending", "0");
	static readonly ConVar r_lightmap_bicubic = new("r_lightmap_bicubic", "0", FCvar.None, "Enable bi-cubic (high quality) lightmap sampling."); // TF2 Backport

	internal void InitParamsLightmappedGeneric(BaseVSShader shader, IMaterialVar[] parms, ReadOnlySpan<char> materialName, ref LightmappedGeneric_Vars info) {
		if (HardwareConfig.SupportsBorderColor())
			parms[(int)ShaderMaterialVars.FlashLightTexture].SetStringValue("effects/flashlight_border");
		else
			parms[(int)ShaderMaterialVars.FlashLightTexture].SetStringValue("effects/flashlight001");

		if (Config.UseBumpmapping() && parms[info.Bumpmap].IsDefined() && parms[info.Albedo].IsDefined() && parms[info.BaseTexture].IsDefined() && !(parms[info.NoDiffuseBumpLighting].IsDefined() && parms[info.NoDiffuseBumpLighting].GetIntValue() != 0))
			parms[info.BaseTexture].SetStringValue(parms[info.Albedo].GetStringValue());

		// if (shader.IsUsingGraphics() && parms[info.Envmap].IsDefined() && !shader.CanUseEditorMaterials()) {
		// 	if (stricmp(parms[info.Envmap].GetStringValue(), "env_cubemap") == 0) {
		// 		Warning($"env_cubemap used on world geometry without rebuilding map. . ignoring: {materialName.SliceNullTerminatedString()}\n");
		// 		parms[info.Envmap].SetUndefined();
		// 	}
		// }

		if (mat_disable_lightwarp.GetBool() && (info.LightWarpTexture != -1))
			parms[info.LightWarpTexture].SetUndefined();
		if (mat_disable_fancy_blending.GetBool() && (info.BlendModulateTexture != -1))
			parms[info.BlendModulateTexture].SetUndefined();

		if (!parms[info.EnvmapTint].IsDefined())
			parms[info.EnvmapTint].SetVecValue(1.0f, 1.0f, 1.0f);

		if (!parms[info.NoDiffuseBumpLighting].IsDefined())
			parms[info.NoDiffuseBumpLighting].SetIntValue(0);

		if (!parms[info.SelfIllumTint].IsDefined())
			parms[info.SelfIllumTint].SetVecValue(1.0f, 1.0f, 1.0f);

		if (!parms[info.DetailScale].IsDefined())
			parms[info.DetailScale].SetFloatValue(4.0f);

		if (!parms[info.DetailTint].IsDefined())
			parms[info.DetailTint].SetVecValue(1.0f, 1.0f, 1.0f, 1.0f);

		InitFloatParam(info.DetailTextureBlendFactor, parms, 1.0f);
		InitIntParam(info.DetailTextureCombineMode, parms, 0);

		if (!parms[info.FresnelReflection].IsDefined())
			parms[info.FresnelReflection].SetFloatValue(1.0f);

		if (!parms[info.EnvmapMaskFrame].IsDefined())
			parms[info.EnvmapMaskFrame].SetIntValue(0);

		if (!parms[info.EnvmapFrame].IsDefined())
			parms[info.EnvmapFrame].SetIntValue(0);

		if (!parms[info.BumpFrame].IsDefined())
			parms[info.BumpFrame].SetIntValue(0);

		if (!parms[info.DetailFrame].IsDefined())
			parms[info.DetailFrame].SetIntValue(0);

		if (!parms[info.EnvmapContrast].IsDefined())
			parms[info.EnvmapContrast].SetFloatValue(0.0f);

		if (!parms[info.EnvmapSaturation].IsDefined())
			parms[info.EnvmapSaturation].SetFloatValue(1.0f);

		InitFloatParam(info.AlphaTestReference, parms, 0.0f);

		if (!parms[info.BaseTexture].IsDefined()) {
			ClearFlags(parms, MaterialVarFlags.SelfIllum);
			ClearFlags(parms, MaterialVarFlags.BaseAlphaEnvMapMask);
		}

		if (parms[info.Bumpmap].IsDefined())
			parms[info.EnvmapMask].SetUndefined();

		if (IsFlagSet(parms, MaterialVarFlags.Decal))
			SetFlags(parms, MaterialVarFlags.NoDebugOverride);

		SetFlags2(parms, MaterialVarFlags2.LightingLightmap);
		if (Config.UseBumpmapping() && parms[info.Bumpmap].IsDefined() && (parms[info.NoDiffuseBumpLighting].GetIntValue() == 0))
			SetFlags2(parms, MaterialVarFlags2.LightingBumpedLightmap);

		if (!Config.UseSpecular() && parms[info.Envmap].IsDefined() && parms[info.BaseTexture].IsDefined())
			parms[info.Envmap].SetUndefined();

		if (!parms[info.BaseTextureNoEnvmap].IsDefined())
			parms[info.BaseTextureNoEnvmap].SetIntValue(0);
		if (!parms[info.BaseTexture2NoEnvmap].IsDefined())
			parms[info.BaseTexture2NoEnvmap].SetIntValue(0);

		if ((info.SelfShadowedBumpFlag != -1) && (!parms[info.SelfShadowedBumpFlag].IsDefined()))
			parms[info.SelfShadowedBumpFlag].SetIntValue(0);

		InitFloatParam(info.EdgeSoftnessStart, parms, 0.5f);
		InitFloatParam(info.EdgeSoftnessEnd, parms, 0.5f);
		InitFloatParam(info.OutlineAlpha, parms, 1.0f);
	}

	internal void InitLightmappedGeneric(BaseVSShader shader, IMaterialVar[] parms, ref LightmappedGeneric_Vars info) {
		if (Config.UseBumpmapping() && parms[info.Bumpmap].IsDefined())
			shader.LoadBumpMap(info.Bumpmap);

		if (Config.UseBumpmapping() && parms[info.Bumpmap2].IsDefined())
			shader.LoadBumpMap(info.Bumpmap2);

		if (Config.UseBumpmapping() && parms[info.BumpMask].IsDefined())
			shader.LoadBumpMap(info.BumpMask);

		if (parms[info.BaseTexture].IsDefined()) {
			shader.LoadTexture(info.BaseTexture, (int)TextureFlags.SRGB);

			if (!parms[info.BaseTexture].GetTextureValue()!.IsTranslucent()) {
				ClearFlags(parms, MaterialVarFlags.SelfIllum);
				ClearFlags(parms, MaterialVarFlags.BaseAlphaEnvMapMask);
			}
		}

		if (parms[info.BaseTexture2].IsDefined())
			shader.LoadTexture(info.BaseTexture2, (int)TextureFlags.SRGB);

		if (parms[info.LightWarpTexture].IsDefined())
			shader.LoadTexture(info.LightWarpTexture);

		if ((info.BlendModulateTexture != -1) && (parms[info.BlendModulateTexture].IsDefined()))
			shader.LoadTexture(info.BlendModulateTexture);

		if (parms[info.Detail].IsDefined()) {
			int detailBlendMode = (info.DetailTextureCombineMode == -1) ? 0 : parms[info.DetailTextureCombineMode].GetIntValue();
			detailBlendMode = detailBlendMode > 1 ? 1 : detailBlendMode;

			shader.LoadTexture(info.Detail, detailBlendMode != 0 ? (int)TextureFlags.SRGB : 0);
		}

		shader.LoadTexture(info.FlashlightTexture, (int)TextureFlags.SRGB);

		if (IsFlagSet(parms, MaterialVarFlags.SelfIllum) || IsFlagSet(parms, MaterialVarFlags.BaseAlphaEnvMapMask))
			ClearFlags(parms, MaterialVarFlags.AlphaTest);

		if (parms[info.Envmap].IsDefined()) {
			if (!IsFlagSet(parms, MaterialVarFlags.EnvMapSphere))
				shader.LoadCubeMap(info.Envmap, HardwareConfig.GetHDRType() == HDRType.None ? (int)TextureFlags.SRGB : 0);
			else
				shader.LoadTexture(info.Envmap);

			if (!HardwareConfig.SupportsCubeMaps())
				SetFlags(parms, MaterialVarFlags.EnvMapSphere);

			if (parms[info.EnvmapMask].IsDefined())
				shader.LoadTexture(info.EnvmapMask);
		}
		else
			parms[info.EnvmapMask].SetUndefined();

		SetFlags2(parms, MaterialVarFlags2.NeedsTangentSpaces);
	}

	internal void DrawLightmappedGeneric_Internal(BaseVSShader shader, IMaterialVar[] parms, bool hasFlashlight, IShaderDynamicAPI? shaderAPI, IShaderShadow? shaderShadow, ref LightmappedGeneric_Vars info, ref BasePerMaterialContextData? context) {
		LightmappedGenericContext? contextData = context as LightmappedGenericContext;

		if (shaderShadow != null || contextData == null || contextData.MaterialVarsChanged || hasFlashlight) {
			bool hasBaseTexture = parms[info.BaseTexture].IsTexture();
			int alphaChannelTextureVar = hasBaseTexture ? (int)info.BaseTexture : (int)info.EnvmapMask;
			BlendType nBlendType = shader.EvaluateBlendRequirements(alphaChannelTextureVar, hasBaseTexture);
			bool isAlphaTested = IsFlagSet(parms, MaterialVarFlags.AlphaTest);
			bool fullyOpaqueWithoutAlphaTest = (nBlendType != BlendType.BlendAdd) && (nBlendType != BlendType.Blend) && (!hasFlashlight);
			bool fullyOpaque = fullyOpaqueWithoutAlphaTest && !isAlphaTested;
			bool needRegenStaticCmds = contextData == null || shaderShadow != null;

			if (contextData == null) {
				contextData = new();
				context = contextData;
			}

			bool hasBump = parms[info.Bumpmap].IsTexture() && (!HardwareConfig.PreferReducedFillrate());
			bool hasSSBump = hasBump && (info.SelfShadowedBumpFlag != -1) && (parms[info.SelfShadowedBumpFlag].GetIntValue() != 0);
			bool hasBaseTexture2 = hasBaseTexture && parms[info.BaseTexture2].IsTexture();
			bool hasLightWarpTexture = parms[info.LightWarpTexture].IsTexture();
			bool hasBump2 = hasBump && parms[info.Bumpmap2].IsTexture();
			bool hasDetailTexture = parms[info.Detail].IsTexture();
			bool hasSelfIllum = IsFlagSet(parms, MaterialVarFlags.SelfIllum);
			bool hasBumpMask = hasBump && hasBump2 && parms[info.BumpMask].IsTexture() && !hasSelfIllum && !hasDetailTexture && !hasBaseTexture2 && (parms[info.BaseTextureNoEnvmap].GetIntValue() == 0);
			bool hasBlendModulateTexture = (info.BlendModulateTexture != -1) && (parms[info.BlendModulateTexture].IsTexture());
			bool hasNormalMapAlphaEnvmapMask = IsFlagSet(parms, MaterialVarFlags.NormalMapAlphaEnvMapMask);
			bool hasEnvmapMask = parms[info.EnvmapMask].IsTexture();

			if (hasFlashlight) { // TODO
				// CBaseVSShader::DrawFlashlight_dx90_Vars_t vars;
				// vars.Bump = hasBump;
				// vars.BumpmapVar = info.Bumpmap;
				// vars.BumpmapFrame = info.BumpFrame;
				// vars.BumpTransform = info.BumpTransform;
				// vars.FlashlightTextureVar = info.FlashlightTexture;
				// vars.FlashlightTextureFrameVar = info.FlashlightTextureFrame;
				// vars.LightmappedGeneric = true;
				// vars.WorldVertexTransition = hasBaseTexture2;
				// vars.BaseTexture2Var = info.BaseTexture2;
				// vars.BaseTexture2FrameVar = info.BaseTexture2Frame;
				// vars.Bumpmap2Var = info.Bumpmap2;
				// vars.Bumpmap2Frame = info.BumpFrame2;
				// vars.Bump2Transform = info.BumpTransform2;
				// vars.AlphaTestReference = info.AlphaTestReference;
				// vars.SSBump = hasSSBump;
				// vars.DetailVar = info.Detail;
				// vars.DetailScale = info.DetailScale;
				// vars.DetailTextureCombineMode = info.DetailTextureCombineMode;
				// vars.DetailTextureBlendFactor = info.DetailTextureBlendFactor;
				// vars.DetailTint = info.DetailTint;

				// if ((info.SeamlessMappingScale != -1))
				// 	vars.m_fSeamlessScale = parms[info.SeamlessMappingScale].GetFloatValue();
				// else
				// 	vars.m_fSeamlessScale = 0.0;
				// shader.DrawFlashlight_dx90(parms, shaderAPI, shaderShadow, vars);
				return;
			}

			contextData.FullyOpaque = fullyOpaque;
			contextData.FullyOpaqueWithoutAlphaTest = fullyOpaqueWithoutAlphaTest;

			bool hasOutline = IsBoolSet(info.Outline, parms);
			contextData.PixelShaderForceFastPathBecauseOutline = hasOutline;
			bool hasSoftEdges = IsBoolSet(info.SoftEdges, parms);
			float detailBlendFactor = GetFloatParam(info.DetailTextureBlendFactor, parms, 1.0f);

			if (shaderShadow != null || needRegenStaticCmds) {
				bool hasVertexColor = IsFlagSet(parms, MaterialVarFlags.VertexColor);
				bool hasDiffuseBumpmap = hasBump && (parms[info.NoDiffuseBumpLighting].GetIntValue() == 0);
				bool hasEnvmap = parms[info.Envmap].IsTexture();
				bool seamlessMapping = (info.SeamlessMappingScale != -1) && (parms[info.SeamlessMappingScale].GetFloatValue() != 0.0);

				if (needRegenStaticCmds) {
					contextData.ResetStaticCmds();
					CommandBufferBuilder<FixedCommandStorageBuffer> staticCmdsBuf = contextData.StaticCmds;

					if (!hasBaseTexture) {
						if (hasEnvmap)
							staticCmdsBuf.BindStandardTexture(Sampler.Sampler0, StandardTextureId.Black);
						else
							staticCmdsBuf.BindStandardTexture(Sampler.Sampler0, StandardTextureId.White);
					}
					staticCmdsBuf.BindStandardTexture(Sampler.Sampler1, StandardTextureId.Lightmap);

					if (seamlessMapping)
						staticCmdsBuf.SetVertexShaderConstant4(VertexShaderConst.ShaderSpecificConst0, parms[info.SeamlessMappingScale].GetFloatValue(), 0, 0, 0);
					staticCmdsBuf.StoreEyePosInPixelShaderConstant(10);
					staticCmdsBuf.SetPixelShaderFogParams(11);
					staticCmdsBuf.End();
				}

				if (shaderShadow != null) {
					shaderShadow.EnableAlphaTest(isAlphaTested);
					if (info.AlphaTestReference != -1 && parms[info.AlphaTestReference].GetFloatValue() > 0.0f) {
						shaderShadow.AlphaFunc(ShaderAlphaFunc.GreaterEqual, parms[info.AlphaTestReference].GetFloatValue());
					}

					shader.SetDefaultBlendingShadowState(alphaChannelTextureVar, hasBaseTexture);

					VertexFormat flags = VertexFormat.Position;

					shaderShadow.EnableTexture(Sampler.Sampler0, true);
					shaderShadow.EnableSRGBRead(Sampler.Sampler0, true);

					if (hasLightWarpTexture) {
						shaderShadow.EnableTexture(Sampler.Sampler6, true);
						shaderShadow.EnableSRGBRead(Sampler.Sampler6, false);
					}
					if (hasBlendModulateTexture) {
						shaderShadow.EnableTexture(Sampler.Sampler3, true);
						shaderShadow.EnableSRGBRead(Sampler.Sampler3, false);
					}

					if (hasBaseTexture2) {
						shaderShadow.EnableTexture(Sampler.Sampler7, true);
						shaderShadow.EnableSRGBRead(Sampler.Sampler7, true);
					}
					shaderShadow.EnableTexture(Sampler.Sampler1, true);

					if (HardwareConfig.GetHDRType() == HDRType.None)
						shaderShadow.EnableSRGBRead(Sampler.Sampler1, true);
					else
						shaderShadow.EnableSRGBRead(Sampler.Sampler1, false);

					if (hasEnvmap) {
						if (hasEnvmap) {
							shaderShadow.EnableTexture(Sampler.Sampler2, true);
							if (HardwareConfig.GetHDRType() == HDRType.None)
								shaderShadow.EnableSRGBRead(Sampler.Sampler2, true);
						}
						flags |= VertexFormat.TangentS | VertexFormat.TangentT | VertexFormat.Normal;
					}

					int detailBlendMode = 0;
					if (hasDetailTexture) {
						detailBlendMode = GetIntParam(info.DetailTextureCombineMode, parms);
						ITexture pDetailTexture = parms[info.Detail].GetTextureValue();
						if ((pDetailTexture.GetFlags() & (int)TextureFlags.SSBump) != 0) {
							if (hasBump)
								detailBlendMode = 10;
							else
								detailBlendMode = 11;
						}
					}

					if (hasDetailTexture) {
						shaderShadow.EnableTexture(Sampler.Sampler12, true);
						bool bSRGBState = detailBlendMode == 1;
						shaderShadow.EnableSRGBRead(Sampler.Sampler12, bSRGBState);
					}

					if (hasBump || hasNormalMapAlphaEnvmapMask)
						shaderShadow.EnableTexture(Sampler.Sampler4, true);
					if (hasBump2)
						shaderShadow.EnableTexture(Sampler.Sampler5, true);
					if (hasBumpMask)
						shaderShadow.EnableTexture(Sampler.Sampler8, true);
					if (hasEnvmapMask)
						shaderShadow.EnableTexture(Sampler.Sampler5, true);

					if (hasVertexColor || hasBaseTexture2 || hasBump2)
						flags |= VertexFormat.Color;

					int numTexCoords = 2;
					if (hasBump)
						numTexCoords = 3;

					shaderShadow.VertexShaderVertexFormat(flags, numTexCoords, null, 0);

					bool hasBaseAlphaEnvmapMask = IsFlagSet(parms, MaterialVarFlags.BaseAlphaEnvMapMask);

					int bumpmap_variant = hasSSBump ? 2 : (hasBump ? 1 : 0);
					bool bMaskedBlending = (info.MaskedBlending != -1) && parms[info.MaskedBlending].GetIntValue() != 0;

					StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "lightmappedgeneric");
					vshIndex.Set("ENVMAP_MASK", hasEnvmapMask);
					vshIndex.Set("TANGENTSPACE", parms[info.Envmap].IsTexture());
					vshIndex.Set("BUMPMAP", hasBump);
					vshIndex.Set("DIFFUSEBUMPMAP", hasDiffuseBumpmap);
					vshIndex.Set("VERTEXCOLOR", IsFlagSet(parms, MaterialVarFlags.VertexColor));
					vshIndex.Set("VERTEXALPHATEXBLENDFACTOR", hasBaseTexture2 || hasBump2);
					vshIndex.Set("BUMPMASK", hasBumpMask);
					vshIndex.Set("RELIEF_MAPPING", false);
					vshIndex.Set("SEAMLESS", seamlessMapping);
					shaderShadow.SetVertexShader("lightmappedgeneric", vshIndex.GetIndex());

					if (HardwareConfig.SupportsPixelShaders_2_b()) {
						StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "lightmappedgeneric");
						pshIndex.Set("BASETEXTURE2", hasBaseTexture2);
						pshIndex.Set("DETAILTEXTURE", hasDetailTexture);
						pshIndex.Set("BUMPMAP", bumpmap_variant);
						pshIndex.Set("BUMPMAP2", hasBump2);
						pshIndex.Set("BUMPMASK", hasBumpMask);
						pshIndex.Set("DIFFUSEBUMPMAP", hasDiffuseBumpmap);
						pshIndex.Set("CUBEMAP", hasEnvmap);
						pshIndex.Set("ENVMAPMASK", hasEnvmapMask);
						pshIndex.Set("BASEALPHAENVMAPMASK", hasBaseAlphaEnvmapMask);
						pshIndex.Set("SELFILLUM", hasSelfIllum);
						pshIndex.Set("NORMALMAPALPHAENVMAPMASK", hasNormalMapAlphaEnvmapMask);
						pshIndex.Set("BASETEXTURENOENVMAP", parms[info.BaseTextureNoEnvmap].GetIntValue());
						pshIndex.Set("BASETEXTURE2NOENVMAP", parms[info.BaseTexture2NoEnvmap].GetIntValue());
						pshIndex.Set("WARPLIGHTING", hasLightWarpTexture);
						pshIndex.Set("FANCY_BLENDING", hasBlendModulateTexture);
						pshIndex.Set("MASKEDBLENDING", bMaskedBlending);
						pshIndex.Set("RELIEF_MAPPING", false);
						pshIndex.Set("SEAMLESS", seamlessMapping);
						pshIndex.Set("OUTLINE", hasOutline);
						pshIndex.Set("SOFTEDGES", hasSoftEdges);
						pshIndex.Set("DETAIL_BLEND_MODE", detailBlendMode);
						pshIndex.Set("NORMAL_DECODE_MODE", (int)NormalDecodeMode.None);
						pshIndex.Set("NORMALMASK_DECODE_MODE", (int)NormalDecodeMode.None);
						shaderShadow.SetPixelShader("lightmappedgeneric", pshIndex.GetIndex());
					}
					else {
						StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "lightmappedgeneric");
						pshIndex.Set("BASETEXTURE2", hasBaseTexture2);
						pshIndex.Set("DETAILTEXTURE", hasDetailTexture);
						pshIndex.Set("BUMPMAP", bumpmap_variant);
						pshIndex.Set("BUMPMAP2", hasBump2);
						pshIndex.Set("BUMPMASK", hasBumpMask);
						pshIndex.Set("DIFFUSEBUMPMAP", hasDiffuseBumpmap);
						pshIndex.Set("CUBEMAP", hasEnvmap);
						pshIndex.Set("ENVMAPMASK", hasEnvmapMask);
						pshIndex.Set("BASEALPHAENVMAPMASK", hasBaseAlphaEnvmapMask);
						pshIndex.Set("SELFILLUM", hasSelfIllum);
						pshIndex.Set("NORMALMAPALPHAENVMAPMASK", hasNormalMapAlphaEnvmapMask);
						pshIndex.Set("BASETEXTURENOENVMAP", parms[info.BaseTextureNoEnvmap].GetIntValue());
						pshIndex.Set("BASETEXTURE2NOENVMAP", parms[info.BaseTexture2NoEnvmap].GetIntValue());
						pshIndex.Set("WARPLIGHTING", hasLightWarpTexture);
						pshIndex.Set("FANCY_BLENDING", hasBlendModulateTexture);
						pshIndex.Set("MASKEDBLENDING", bMaskedBlending);
						pshIndex.Set("SEAMLESS", seamlessMapping);
						pshIndex.Set("OUTLINE", hasOutline);
						pshIndex.Set("SOFTEDGES", hasSoftEdges);
						pshIndex.Set("DETAIL_BLEND_MODE", detailBlendMode);
						pshIndex.Set("NORMAL_DECODE_MODE", 0);
						pshIndex.Set("NORMALMASK_DECODE_MODE", 0);
						shaderShadow.SetPixelShader("lightmappedgeneric", pshIndex.GetIndex());
					}

					shaderShadow.EnableAlphaWrites(fullyOpaque);
					shaderShadow.EnableSRGBWrite(true);
					shader.DefaultFog();

				}
			}
			if (shaderAPI != null && contextData.MaterialVarsChanged) {
				contextData.SemiStaticCmdsOut.Reset();
				contextData.MaterialVarsChanged = false;

				bool hasBlendMaskTransform = (info.BlendMaskTransform != -1) && (info.MaskedBlending != -1) && (parms[info.MaskedBlending].GetIntValue() != 0) && (!parms[info.BumpTransform].MatrixIsIdentity());
				bool hasTextureTransform = !(parms[info.BaseTextureTransform].MatrixIsIdentity() && parms[info.BumpTransform].MatrixIsIdentity() && parms[info.BumpTransform2].MatrixIsIdentity() && parms[info.EnvmapMaskTransform].MatrixIsIdentity());
				hasTextureTransform |= hasBlendMaskTransform;

				contextData.VertexShaderFastPath = !hasTextureTransform;

				if (parms[info.Detail].IsTexture())
					contextData.VertexShaderFastPath = false;
				if (hasBlendMaskTransform)
					contextData.SemiStaticCmdsOut.SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst10, info.BlendMaskTransform);

				if (!contextData.VertexShaderFastPath) {
					bool seamlessMapping = ((info.SeamlessMappingScale != -1) && (parms[info.SeamlessMappingScale].GetFloatValue() != 0.0));
					if (!seamlessMapping)
						contextData.SemiStaticCmdsOut.SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst0, info.BaseTextureTransform);
					if (hasBump && !hasDetailTexture)
						contextData.SemiStaticCmdsOut.SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst2, info.BumpTransform);
					if (hasEnvmapMask)
						contextData.SemiStaticCmdsOut.SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst4, info.EnvmapMaskTransform);
					else if (hasBump2)
						contextData.SemiStaticCmdsOut.SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst4, info.BumpTransform2);
				}

				contextData.SemiStaticCmdsOut.SetEnvMapTintPixelShaderDynamicState(0, info.EnvmapTint);
				Span<float> color = [1.0f, 1.0f, 1.0f, 1.0f];
				shader.ComputeModulationColor(color);
				float flLScale = shaderAPI.GetLightMapScaleFactor();
				color[0] *= flLScale;
				color[1] *= flLScale;
				color[2] *= flLScale;

				contextData.SemiStaticCmdsOut.SetVertexShaderConstant(VertexShaderConst.ModulationColor, color);

				color[3] *= (IsParamDefined(parms, info.Alpha2) && parms[info.Alpha2].GetFloatValue() > 0.0f) ? parms[info.Alpha2].GetFloatValue() : 1.0f;
				contextData.SemiStaticCmdsOut.SetPixelShaderConstant(12, color);

				if (hasDetailTexture) {
					Span<float> detailTintAndBlend = [1, 1, 1, 1];
					if (info.DetailTint != -1)
						parms[info.DetailTint].GetVecValue(detailTintAndBlend[..3]);

					detailTintAndBlend[3] = detailBlendFactor;
					contextData.SemiStaticCmdsOut.SetPixelShaderConstant(8, detailTintAndBlend);
				}

				Span<float> envmapTintVal = stackalloc float[4];
				Span<float> selfIllumTintVal = stackalloc float[4];
				parms[info.EnvmapTint].GetVecValue(envmapTintVal[..3]);
				parms[info.SelfIllumTint].GetVecValue(selfIllumTintVal[..3]);
				float envmapContrast = parms[info.EnvmapContrast].GetFloatValue();
				float envmapSaturation = parms[info.EnvmapSaturation].GetFloatValue();
				float fresnelReflection = parms[info.FresnelReflection].GetFloatValue();
				bool hasEnvmap = parms[info.Envmap].IsTexture();

				contextData.PixelShaderFastPath = true;
				bool usingContrast = hasEnvmap && (envmapContrast != 0.0f) && (envmapContrast != 1.0f) && (envmapSaturation != 1.0f);
				bool usingFresnel = hasEnvmap && (fresnelReflection != 1.0f);
				bool usingSelfillumTint = IsFlagSet(parms, MaterialVarFlags.SelfIllum) && (selfIllumTintVal[0] != 1.0f || selfIllumTintVal[1] != 1.0f || selfIllumTintVal[2] != 1.0f);
				if (usingContrast || usingFresnel || usingSelfillumTint || !Config.ShowSpecular)
					contextData.PixelShaderFastPath = false;

				if (!contextData.PixelShaderFastPath) {
					contextData.SemiStaticCmdsOut.SetPixelShaderConstants(2, 3);
					contextData.SemiStaticCmdsOut.OutputConstantData(parms[info.EnvmapContrast].GetVecValue());
					contextData.SemiStaticCmdsOut.OutputConstantData(parms[info.EnvmapSaturation].GetVecValue());
					float fresnel = parms[info.FresnelReflection].GetFloatValue();
					contextData.SemiStaticCmdsOut.OutputConstantData4(0.0f, 0.0f, (float)(1.0 - fresnel), fresnel);
					contextData.SemiStaticCmdsOut.SetPixelShaderConstant(7, parms[info.SelfIllumTint].GetVecValue());
				}
				else {
					if (hasOutline) {
						Span<float> flOutlineParms = [GetFloatParam(info.OutlineStart0, parms), GetFloatParam(info.OutlineStart1, parms), GetFloatParam(info.OutlineEnd0, parms), GetFloatParam(info.OutlineEnd1, parms), 0, 0, 0, GetFloatParam(info.OutlineAlpha, parms)];
						if (info.OutlineColor != -1) {
							parms[info.OutlineColor].GetVecValue(flOutlineParms[4..7]);
						}
						contextData.SemiStaticCmdsOut.SetPixelShaderConstant(2, flOutlineParms, 2);
					}

					if (hasSoftEdges)
						contextData.SemiStaticCmdsOut.SetPixelShaderConstant4(4, GetFloatParam(info.EdgeSoftnessStart, parms), GetFloatParam(info.EdgeSoftnessEnd, parms), 0, 0);
				}

				if (hasBaseTexture)
					contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler0, info.BaseTexture, info.BaseTextureFrame);

				bool lightingOnly = mat_fullbright.GetInt() == 2 && !IsFlagSet(parms, MaterialVarFlags.NoDebugOverride);
				if (lightingOnly) {
					if (hasSelfIllum)
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler0, StandardTextureId.GreyAlphaZero);
					else
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler0, StandardTextureId.Grey);

					if (hasBaseTexture2)
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler7, StandardTextureId.Grey);

					if (hasDetailTexture)
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler12, StandardTextureId.Grey);

					contextData.SemiStaticCmdsOut.SetVertexShaderConstant(VertexShaderConst.ModulationColor, [0.0f, 0.0f, 0.0f, 0.0f]);

					envmapTintVal[0] = 0.0f;
					envmapTintVal[1] = 0.0f;
					envmapTintVal[2] = 0.0f;
				}

				if (hasDetailTexture)
					contextData.SemiStaticCmdsOut.SetVertexShaderTextureScaledTransform(VertexShaderConst.ShaderSpecificConst2, info.BaseTextureTransform, info.DetailScale);

				if (hasBaseTexture2)
					contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler7, info.BaseTexture2, info.BaseTexture2Frame);

				if (hasDetailTexture)
					contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler12, info.Detail, info.DetailFrame);

				if (hasBump || hasNormalMapAlphaEnvmapMask) {
					if (!Config.FastNoBump)
						contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler4, info.Bumpmap, info.BumpFrame);
					else
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler4, StandardTextureId.NormalMapFlat);
				}
				if (hasBump2) {
					if (!Config.FastNoBump)
						contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler5, info.Bumpmap2, info.BumpFrame2);
					else
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler5, StandardTextureId.NormalMapFlat);
				}
				if (hasBumpMask) {
					if (!Config.FastNoBump)
						contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler8, info.BumpMask, -1);
					else
						contextData.SemiStaticCmdsOut.BindStandardTexture(Sampler.Sampler8, StandardTextureId.NormalMapFlat);
				}

				if (hasEnvmapMask)
					contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler5, info.EnvmapMask, info.EnvmapMaskFrame);

				if (hasLightWarpTexture)
					contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler6, info.LightWarpTexture, -1);

				if (hasBlendModulateTexture)
					contextData.SemiStaticCmdsOut.BindTexture(shader, Sampler.Sampler3, info.BlendModulateTexture, -1);

				contextData.SemiStaticCmdsOut.End();
			}
		}
		if (shaderAPI != null) {
			DynamicCmdsOut.Reset();
			DynamicCmdsOut.Call(contextData.StaticCmds.Storage);
			DynamicCmdsOut.Call(contextData.SemiStaticCmdsOut.Storage);

			bool hasEnvmap = parms[info.Envmap].IsTexture();
			if (hasEnvmap)
				DynamicCmdsOut.BindTexture(shader, Sampler.Sampler2, info.Envmap, info.EnvmapFrame);

			int fixedLightingMode = shaderAPI.GetIntRenderingParameter((int)RenderParamInt.EnableFixedLighting);

			bool vertexShaderFastPath = contextData.VertexShaderFastPath;

			if (fixedLightingMode != 0) {
				if (contextData.PixelShaderForceFastPathBecauseOutline)
					fixedLightingMode = 0;
				else
					vertexShaderFastPath = false;
			}

			MaterialFogMode fogType = shaderAPI.GetSceneFogMode();
			DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
			vshIndex.Set("DOWATERFOG", fogType == MaterialFogMode.LinearBelowFogZ);
			vshIndex.Set("FASTPATH", vertexShaderFastPath);
			vshIndex.Set("LIGHTING_PREVIEW", (fixedLightingMode != 0) ? 1 : 0);
			DynamicCmdsOut.SetVertexShaderIndex(vshIndex.GetIndex());

			bool pixelShaderFastPath = contextData.PixelShaderFastPath;
			if (fixedLightingMode != 0)
				pixelShaderFastPath = false;
			bool writeDepthToAlpha;
			bool writeWaterFogToAlpha;
			if (contextData.FullyOpaque) {
				writeDepthToAlpha = shaderAPI.ShouldWriteDepthToDestAlpha();
				writeWaterFogToAlpha = fogType == MaterialFogMode.LinearBelowFogZ;
				AssertMsg(!(writeDepthToAlpha && writeWaterFogToAlpha), "Can't write two values to alpha at the same time.");
			}
			else {
				writeDepthToAlpha = false;
				writeWaterFogToAlpha = false;
			}

			float envmapContrast = parms[info.EnvmapContrast].GetFloatValue();
			if (HardwareConfig.SupportsPixelShaders_2_b()) {
				DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
				pshIndex.Set("FASTPATH", pixelShaderFastPath || contextData.PixelShaderForceFastPathBecauseOutline);
				pshIndex.Set("FASTPATHENVMAPCONTRAST", pixelShaderFastPath && envmapContrast == 1.0f);
				pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
				pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", writeDepthToAlpha);
				pshIndex.Set("WRITEWATERFOGTODESTALPHA", writeWaterFogToAlpha);
				pshIndex.Set("LIGHTING_PREVIEW", fixedLightingMode);
				DynamicCmdsOut.SetPixelShaderIndex(pshIndex.GetIndex());
			}
			else {
				DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
				pshIndex.Set("FASTPATH", pixelShaderFastPath);
				pshIndex.Set("FASTPATHENVMAPCONTRAST", pixelShaderFastPath && envmapContrast == 1.0f);
				pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo());
				pshIndex.Set("WRITEWATERFOGTODESTALPHA", writeWaterFogToAlpha);
				pshIndex.Set("LIGHTING_PREVIEW", fixedLightingMode);
				DynamicCmdsOut.SetPixelShaderIndex(pshIndex.GetIndex());
			}

			DynamicCmdsOut.End();
			shaderAPI.ExecuteCommandBuffer(DynamicCmdsOut.Storage);
		}
		shader.Draw();

		// if (IsPC() && IsFlagSet(parms, MaterialVarFlags.AlphaTest) && contextData.FullyOpaqueWithoutAlphaTest)
		// 	shader.DrawEqualDepthToDestAlpha();
	}

	internal void DrawLightmappedGeneric(BaseVSShader pShader, IMaterialVar[] parms, IShaderDynamicAPI? pShaderAPI, IShaderShadow? pShaderShadow, ref LightmappedGeneric_Vars info, ref BasePerMaterialContextData? pContextDataPtr) {
		bool hasFlashlight = pShader.UsingFlashlight(parms);
		if (r_flashlight_version2.GetInt() == 0) {
			DrawLightmappedGeneric_Internal(pShader, parms, hasFlashlight, pShaderAPI, pShaderShadow, ref info, ref pContextDataPtr);
			return;
		}

		DrawLightmappedGeneric_Internal(pShader, parms, hasFlashlight, pShaderAPI, pShaderShadow, ref info, ref pContextDataPtr);
	}

	readonly static CommandBufferBuilder<FixedCommandStorageBuffer> DynamicCmdsOut = new() { Storage = new FixedCommandStorageBuffer(5000) };
}

class LightmappedGenericContext : BasePerMaterialContextData
{
	public readonly CommandBufferBuilder<FixedCommandStorageBuffer> StaticCmds = new() { Storage = new FixedCommandStorageBuffer(5000) };
	public readonly CommandBufferBuilder<FixedCommandStorageBuffer> SemiStaticCmdsOut = new() { Storage = new FixedCommandStorageBuffer(1000) };
	public bool VertexShaderFastPath;
	public bool PixelShaderFastPath;
	public bool PixelShaderForceFastPathBecauseOutline;
	public bool FullyOpaque;
	public bool FullyOpaqueWithoutAlphaTest;

	public void ResetStaticCmds() => StaticCmds.Reset();
}

struct LightmappedGeneric_Vars
{
	public LightmappedGeneric_Vars() => memset(MemoryMarshal.AsBytes(new Span<LightmappedGeneric_Vars>(ref this)), (byte)0xFF);
	public int BaseTexture;
	public int BaseTextureFrame;
	public int BaseTextureTransform;
	public int Albedo;
	public int SelfIllumTint;
	public int Alpha2;
	public int Detail;
	public int DetailFrame;
	public int DetailScale;
	public int DetailTextureCombineMode;
	public int DetailTextureBlendFactor;
	public int DetailTint;
	public int Envmap;
	public int EnvmapFrame;
	public int EnvmapMask;
	public int EnvmapMaskFrame;
	public int EnvmapMaskTransform;
	public int EnvmapTint;
	public int Bumpmap;
	public int BumpFrame;
	public int BumpTransform;
	public int EnvmapContrast;
	public int EnvmapSaturation;
	public int FresnelReflection;
	public int NoDiffuseBumpLighting;
	public int Bumpmap2;
	public int BumpFrame2;
	public int BumpTransform2;
	public int BumpMask;
	public int BaseTexture2;
	public int BaseTexture2Frame;
	public int BaseTextureNoEnvmap;
	public int BaseTexture2NoEnvmap;
	public int DetailAlphaMaskBaseTexture;
	public int FlashlightTexture;
	public int FlashlightTextureFrame;
	public int LightWarpTexture;
	public int BlendModulateTexture;
	public int MaskedBlending;
	public int BlendMaskTransform;
	public int SelfShadowedBumpFlag;
	public int SeamlessMappingScale;
	public int AlphaTestReference;
	public int SoftEdges;
	public int EdgeSoftnessStart;
	public int EdgeSoftnessEnd;
	public int Outline;
	public int OutlineColor;
	public int OutlineAlpha;
	public int OutlineStart0;
	public int OutlineStart1;
	public int OutlineEnd0;
	public int OutlineEnd1;
};