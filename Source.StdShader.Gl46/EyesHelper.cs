using Source.Common;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public partial class BaseVSShader
{
	internal void InitParamsEyes(BaseVSShader shader, IMaterialVar[] parms, ReadOnlySpan<char> materialName, ref Eyes_Vars info) {
		if (HardwareConfig.SupportsBorderColor())
			parms[(int)ShaderMaterialVars.FlashLightTexture].SetStringValue("effects/flashlight_border");
		else
			parms[(int)ShaderMaterialVars.FlashLightTexture].SetStringValue("effects/flashlight001");

		SetFlags2(parms, MaterialVarFlags2.SupportsHardwareSkinning);
		SetFlags2(parms, MaterialVarFlags2.LightingVertexLit);

		Assert(info.Intro != -1);
		if (info.Intro != -1 && !parms[info.Intro].IsDefined())
			parms[info.Intro].SetIntValue(0);
	}

	internal void InitEyes(BaseVSShader shader, IMaterialVar[] parms, ref Eyes_Vars info) {
		shader.LoadTexture((int)ShaderMaterialVars.FlashLightTexture, (int)TextureFlags.SRGB);
		shader.LoadTexture(info.BaseTexture, (int)TextureFlags.SRGB);
		shader.LoadTexture(info.Iris, (int)TextureFlags.SRGB);
		shader.LoadTexture(info.Glint);

		if (!parms[info.Dilation].IsDefined())
			parms[info.Dilation].SetFloatValue(0.0f);
	}

	private void DrawEyesFlashlight(BaseVSShader shader, IMaterialVar[] parms, IShaderDynamicAPI? shaderAPI, IShaderShadow? shaderShadow, ref Eyes_Vars info, VertexCompressionType vertexCompression)
		=> throw new NotImplementedException();

	private void DrawEyesUsingVertexShader(BaseVSShader shader, IMaterialVar[] parms, IShaderDynamicAPI? shaderAPI, IShaderShadow? shaderShadow, ref Eyes_Vars info, VertexCompressionType vertexCompression) {
		if (shaderShadow != null) {
			shaderShadow.EnableTexture(Sampler.Sampler0, true);
			shaderShadow.EnableTexture(Sampler.Sampler1, true);
			shaderShadow.EnableTexture(Sampler.Sampler2, true);

			VertexFormat flags = VertexFormat.Position | VertexFormat.Normal;
			int texCoordCount = 1;
			int userDataSize = 0;
			shaderShadow.VertexShaderVertexFormat(flags, texCoordCount, null, userDataSize);

			shaderShadow.EnableAlphaWrites(true);

			if (!HardwareConfig.HasFastVertexTextures()) {
				bool useStaticControlFlow = HardwareConfig.SupportsStaticControlFlow();

				StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "eyes");
				vshIndex.Set("HALFLAMBERT", IsFlagSet(parms, MaterialVarFlags.HalfLambert));
				vshIndex.Set("INTRO", parms[info.Intro].GetIntValue() != 0 ? 1 : 0);
				vshIndex.Set("USE_STATIC_CONTROL_FLOW", useStaticControlFlow);
				shaderShadow.SetVertexShader("eyes", vshIndex.GetIndex());

				StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "eyes");
				shaderShadow.SetPixelShader("eyes", pshIndex.GetIndex());
			}
			else {
				SetFlags2(parms, MaterialVarFlags2.UsesVertexID);

				StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "eyes");
				vshIndex.Set("HALFLAMBERT", IsFlagSet(parms, MaterialVarFlags.HalfLambert));
				vshIndex.Set("INTRO", parms[info.Intro].GetIntValue() != 0 ? 1 : 0);
				shaderShadow.SetVertexShader("eyes", vshIndex.GetIndex());

				StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "eyes");
				shaderShadow.SetPixelShader("eyes", pshIndex.GetIndex());
			}

			shaderShadow.EnableSRGBRead(Sampler.Sampler0, true);
			shaderShadow.EnableSRGBRead(Sampler.Sampler1, true);
			shaderShadow.EnableSRGBWrite(true);

			shader.FogToFogColor();
		}
		else if (shaderAPI != null) {
			shader.BindTexture(Sampler.Sampler0, info.BaseTexture, info.Frame);
			shader.BindTexture(Sampler.Sampler1, info.Iris, info.IrisFrame);
			shader.BindTexture(Sampler.Sampler2, info.Glint);
			shader.SetAmbientCubeDynamicStateVertexShader();
			shader.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst0, info.EyeOrigin);
			shader.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst1, info.EyeUp);
			shader.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst2, info.IrisU);
			shader.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst3, info.IrisV);
			shader.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst4, info.GlintU);
			shader.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst5, info.GlintV);

			shaderAPI.GetDX9LightState(out LightState lightState);

			if (!HardwareConfig.HasFastVertexTextures()) {
				bool useStaticControlFlow = HardwareConfig.SupportsStaticControlFlow();

				DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
				vshIndex.Set("DOWATERFOG", shaderAPI.GetSceneFogMode() == MaterialFogMode.LinearBelowFogZ);
				vshIndex.Set("SKINNING", shaderAPI.GetCurrentNumBones() > 0);
				vshIndex.Set("DYNAMIC_LIGHT", lightState.HasDynamicLight());
				vshIndex.Set("STATIC_LIGHT", lightState.StaticLightVertex ? 1 : 0);
				vshIndex.Set("COMPRESSED_VERTS", (int)vertexCompression);
				vshIndex.Set("NUM_LIGHTS", useStaticControlFlow ? 0 : lightState.NumLights);
				shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());
			}
			else {
				DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
				vshIndex.Set("DOWATERFOG", shaderAPI.GetSceneFogMode() == MaterialFogMode.LinearBelowFogZ);
				vshIndex.Set("SKINNING", shaderAPI.GetCurrentNumBones() > 0);
				vshIndex.Set("DYNAMIC_LIGHT", lightState.HasDynamicLight());
				vshIndex.Set("STATIC_LIGHT", lightState.StaticLightVertex ? 1 : 0);
				vshIndex.Set("MORPHING", shaderAPI.IsHWMorphingEnabled());
				vshIndex.Set("COMPRESSED_VERTS", (int)vertexCompression);
				shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());
			}

			float glintDamping = Math.Max(0.0f, Math.Min(shaderAPI.GetAmbientLightCubeLuminance(), 1.0f));
			const float dimGlint = 0.01f;

			if (glintDamping > dimGlint)
				glintDamping = 1.0f;
			else
				glintDamping *= (float)MathLib.SimpleSplineRemapVal(glintDamping, 0.0f, dimGlint, 0.0f, 1.0f);

			Span<float> psConst = [parms[info.Dilation].GetFloatValue(), glintDamping, 0.0f, 0.0f];
			shaderAPI.SetPixelShaderConstant(0, psConst);

			shaderAPI.SetPixelShaderFogParams((int)PixelShaderConst.FogParams);

			Span<float> eyePos_SpecExponent = [0, 0, 0, 0];
			shaderAPI.GetWorldSpaceCameraPosition(eyePos_SpecExponent);
			eyePos_SpecExponent[3] = 0.0f;
			shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.EyePosSpecExponent, eyePos_SpecExponent);

			if (!HardwareConfig.HasFastVertexTextures()) {
				if (HardwareConfig.SupportsPixelShaders_2_b()) {
					DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
					pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
					pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", shaderAPI.ShouldWriteDepthToDestAlpha());
					shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
				}
				else {
					DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
					pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo());
					shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
				}
			}
			else {
				DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
				pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
				pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", shaderAPI.ShouldWriteDepthToDestAlpha());
				shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
			}

			Assert(info.Intro != -1);
			if (parms[info.Intro].GetIntValue() != 0) {
				float curTime = parms[info.WarpParam].GetFloatValue();
				Span<float> timeVec = [0.0f, 0.0f, 0.0f, curTime];
				Assert(parms[info.EntityOrigin].IsDefined());
				parms[info.EntityOrigin].GetVecValue(timeVec[..3]);
				shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst6, timeVec);
			}
		}
		shader.Draw();
	}

	private void DrawEyes_Internal(BaseVSShader shader, IMaterialVar[] parms, IShaderDynamicAPI? shaderAPI, IShaderShadow? shaderShadow, bool hasFlashlight, ref Eyes_Vars info, VertexCompressionType vertexCompression) {
		if (!hasFlashlight)
			DrawEyesUsingVertexShader(shader, parms, shaderAPI, shaderShadow, ref info, vertexCompression);
		else
			DrawEyesFlashlight(shader, parms, shaderAPI, shaderShadow, ref info, vertexCompression);
	}

	internal void DrawEyes(BaseVSShader shader, IMaterialVar[] parms, IShaderDynamicAPI? shaderAPI, IShaderShadow? shaderShadow, ref Eyes_Vars info, VertexCompressionType vertexCompression) {
		if (shaderShadow != null)
			SetFlags2(parms, MaterialVarFlags2.LightingVertexLit);

		bool hasFlashlight = shader.UsingFlashlight(parms);
		if (hasFlashlight && r_flashlight_version2.GetInt() != 0) {
			DrawEyes_Internal(shader, parms, shaderAPI, shaderShadow, false, ref info, vertexCompression);
			if (shaderShadow != null)
				shader.SetInitialShadowState();
		}
		DrawEyes_Internal(shader, parms, shaderAPI, shaderShadow, hasFlashlight, ref info, vertexCompression);
	}
}
