using Source.Common;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class Teeth : BaseVSShader
{
	public static string HelpString = "Help for Teeth_DX9";
	public static int Flags = 0;
	public static List<ShaderParam> ShaderParams = [];
	public static ShaderParam[] ShaderParamOverrides = new ShaderParam[(int)ShaderMaterialVars.Count];

	public class ShaderParam
	{
		public readonly ShaderParamInfo Info;
		public readonly int Index;
		public ShaderParam(ShaderMaterialVars var, ShaderParamType type, ReadOnlySpan<char> defaultParam, ReadOnlySpan<char> help, int flags) {
			Info.Name = "override";
			Info.Type = type;
			Info.DefaultValue = new(defaultParam);
			Info.Help = new(help);
			Info.Flags = (ShaderParamFlags)flags;

			if (ShaderParamOverrides[(int)var] == null) {

			}
			else {
				AssertMsg(false, "ShaderParamOverrides at var index had null value");
			}

			ShaderParamOverrides[(int)var] = this;
			Index = (int)var;
		}
		public ShaderParam(string name, ShaderParamType type, ReadOnlySpan<char> defaultParam, ReadOnlySpan<char> help, int flags = 0) {
			Info.Name = name;
			Info.Type = type;
			Info.DefaultValue = new(defaultParam);
			Info.Help = new(help);
			Info.Flags = (ShaderParamFlags)flags;
			Index = (int)ShaderMaterialVars.Count + ShaderParams.Count;
			ShaderParams.Add(this);
		}
		public static implicit operator int(ShaderParam param) => param.Index;
		public ReadOnlySpan<char> GetName() => Info.Name;
		public ShaderParamType GetType() => Info.Type;
		public ReadOnlySpan<char> GetDefaultValue() => Info.DefaultValue;
		public int GetFlags() => (int)Info.Flags;
		public ReadOnlySpan<char> GetHelp() => Info.Help;
	}

	public static readonly ShaderParam ILLUMFACTOR = new($"${nameof(ILLUMFACTOR)}", ShaderParamType.Float, "1", "Amount to darken or brighten the teeth");
	public static readonly ShaderParam FORWARD = new($"${nameof(FORWARD)}", ShaderParamType.Vec3, "[1 0 0]", "Forward direction vector for teeth lighting");
	public static readonly ShaderParam BUMPMAP = new($"${nameof(BUMPMAP)}", ShaderParamType.Texture, "models/shadertest/shader1_normal", "bump map");
	public static readonly ShaderParam PHONGEXPONENT = new($"${nameof(PHONGEXPONENT)}", ShaderParamType.Float, "100", "phong exponent");
	public static readonly ShaderParam INTRO = new($"${nameof(INTRO)}", ShaderParamType.Bool, "0", "is teeth in the ep1 intro");
	public static readonly ShaderParam ENTITYORIGIN = new($"${nameof(ENTITYORIGIN)}", ShaderParamType.Vec3, "0.0", "center if the model in world space");
	public static readonly ShaderParam WARPPARAM = new($"${nameof(WARPPARAM)}", ShaderParamType.Float, "0.0", "animation param between 0 and 1");

	protected override void OnInitShaderParams(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		if (HardwareConfig.SupportsBorderColor())
			parms[(int)ShaderMaterialVars.FlashLightTexture].SetStringValue("effects/flashlight_border");
		else
			parms[(int)ShaderMaterialVars.FlashLightTexture].SetStringValue("effects/flashlight001");

		SetFlags2(parms, MaterialVarFlags2.SupportsHardwareSkinning);

		if (!parms[INTRO].IsDefined())
			parms[INTRO].SetIntValue(0);
	}

	public override string? GetFallbackShader(IMaterialVar[] vars) => null;
	public override int GetFlags() => Flags;
	public override int GetNumParams() => base.GetNumParams() + ShaderParams.Count;
	public override ReadOnlySpan<char> GetParamName(int paramIndex) {
		int baseClassParamCount = base.GetNumParams();
		if (paramIndex < baseClassParamCount)
			return base.GetParamName(paramIndex);
		else
			return ShaderParams[paramIndex - baseClassParamCount].GetName();
	}
	public override ReadOnlySpan<char> GetParamHelp(int paramIndex) {
		int baseClassParamCount = base.GetNumParams();
		if (paramIndex < baseClassParamCount)
			return base.GetParamHelp(paramIndex);
		else
			return ShaderParams[paramIndex - baseClassParamCount].GetHelp();
	}
	public override ShaderParamType GetParamType(int paramIndex) {
		int baseClassParamCount = base.GetNumParams();
		if (paramIndex < baseClassParamCount)
			return base.GetParamType(paramIndex);
		else
			return ShaderParams[paramIndex - baseClassParamCount].GetType();
	}
	public override ReadOnlySpan<char> GetParamDefault(int paramIndex) {
		int baseClassParamCount = base.GetNumParams();
		if (paramIndex < baseClassParamCount)
			return base.GetParamDefault(paramIndex);
		else
			return ShaderParams[paramIndex - baseClassParamCount].GetDefaultValue();
	}

	protected override void OnInitShaderInstance(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		LoadTexture((int)ShaderMaterialVars.FlashLightTexture, (int)TextureFlags.SRGB);
		LoadTexture((int)ShaderMaterialVars.BaseTexture, (int)TextureFlags.SRGB);

		if (parms[BUMPMAP].IsDefined())
			LoadTexture(BUMPMAP);
	}

	void DrawUsingVertexShader(IMaterialVar[] parms, IShaderDynamicAPI? shaderAPI, IShaderShadow? shaderShadow, VertexCompressionType vertexCompression) {
		bool hasBump = parms[BUMPMAP].IsTexture();

		BlendType blendType = EvaluateBlendRequirements((int)ShaderMaterialVars.BaseTexture, true);
		bool fullyOpaque = (blendType != BlendType.BlendAdd) && (blendType != BlendType.Blend) && !IsFlagSet(parms, MaterialVarFlags.AlphaTest);

		if (shaderShadow != null) {
			shaderShadow.EnableTexture(Sampler.Sampler0, true);

			VertexFormat flags = VertexFormat.Position | VertexFormat.Normal;
			int texCoordCount = 1;
			int userDataSize = 0;

			if (hasBump) {
				shaderShadow.EnableTexture(Sampler.Sampler1, true);
				shaderShadow.EnableSRGBRead(Sampler.Sampler1, false);
				shaderShadow.EnableTexture(Sampler.Sampler2, true);
				shaderShadow.EnableSRGBRead(Sampler.Sampler2, false);
				userDataSize = 4;
			}

			shaderShadow.VertexShaderVertexFormat(flags, texCoordCount, null, userDataSize);

			if (hasBump) {
				if (!HardwareConfig.HasFastVertexTextures()) {
					bool useStaticControlFlow = HardwareConfig.SupportsStaticControlFlow();

					StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "teeth_bump");
					vshIndex.Set("INTRO", parms[INTRO].GetIntValue() != 0 ? 1 : 0);
					vshIndex.Set("USE_STATIC_CONTROL_FLOW", useStaticControlFlow);
					shaderShadow.SetVertexShader("teeth_bump", vshIndex.GetIndex());

					StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "teeth_bump");
					shaderShadow.SetPixelShader("teeth_bump", pshIndex.GetIndex());
				}
				else {
					SetFlags2(parms, MaterialVarFlags2.UsesVertexID);

					StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "teeth_bump");
					vshIndex.Set("INTRO", parms[INTRO].GetIntValue() != 0 ? 1 : 0);
					shaderShadow.SetVertexShader("teeth_bump", vshIndex.GetIndex());

					StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "teeth_bump");
					shaderShadow.SetPixelShader("teeth_bump", pshIndex.GetIndex());
				}
			}
			else {
				if (!HardwareConfig.HasFastVertexTextures()) {
					bool useStaticControlFlow = HardwareConfig.SupportsStaticControlFlow();

					StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "teeth");
					vshIndex.Set("INTRO", parms[INTRO].GetIntValue() != 0 ? 1 : 0);
					vshIndex.Set("USE_STATIC_CONTROL_FLOW", useStaticControlFlow);
					shaderShadow.SetVertexShader("teeth", vshIndex.GetIndex());

					StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "teeth");
					shaderShadow.SetPixelShader("teeth", pshIndex.GetIndex());
				}
				else {
					SetFlags2(parms, MaterialVarFlags2.UsesVertexID);

					StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "teeth");
					vshIndex.Set("INTRO", parms[INTRO].GetIntValue() != 0 ? 1 : 0);
					shaderShadow.SetVertexShader("teeth", vshIndex.GetIndex());

					StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "teeth");
					shaderShadow.SetPixelShader("teeth", pshIndex.GetIndex());
				}
			}

			shaderShadow.EnableSRGBRead(Sampler.Sampler0, true);
			shaderShadow.EnableSRGBWrite(true);

			FogToFogColor();

			shaderShadow.EnableAlphaWrites(fullyOpaque);
		}
		else if (shaderAPI != null) {
			BindTexture(Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, (int)ShaderMaterialVars.Frame);
			if (hasBump)
				BindTexture(Sampler.Sampler1, BUMPMAP);

			shaderAPI.BindStandardTexture(Sampler.Sampler2, StandardTextureId.NormalizationCubemapSigned);
			shaderAPI.SetPixelShaderStateAmbientLightCube((int)PixelShaderConst.AmbientCube, false);
			shaderAPI.CommitPixelShaderLighting((int)PixelShaderConst.LightInfoArray);

			Span<float> lighting = [0, 0, 0, 0];
			parms[FORWARD].GetVecValue(lighting[..3]);
			lighting[3] = parms[ILLUMFACTOR].GetFloatValue();
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst0, lighting);

			shaderAPI.GetDX9LightState(out LightState lightState);

			shaderAPI.SetPixelShaderFogParams((int)PixelShaderConst.FogParams);

			Span<float> eyePos_SpecExponent = [0, 0, 0, 0];
			shaderAPI.GetWorldSpaceCameraPosition(eyePos_SpecExponent);
			eyePos_SpecExponent[3] = 0.0f;
			shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.EyePosSpecExponent, eyePos_SpecExponent);

			if (hasBump) {
				if (!HardwareConfig.HasFastVertexTextures()) {
					bool useStaticControlFlow = HardwareConfig.SupportsStaticControlFlow();

					DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
					vshIndex.Set("DOWATERFOG", shaderAPI.GetSceneFogMode() == MaterialFogMode.LinearBelowFogZ);
					vshIndex.Set("SKINNING", shaderAPI.GetCurrentNumBones() > 0);
					vshIndex.Set("STATIC_LIGHT", lightState.StaticLightVertex ? 1 : 0);
					vshIndex.Set("COMPRESSED_VERTS", (int)vertexCompression);
					vshIndex.Set("NUM_LIGHTS", useStaticControlFlow ? 0 : lightState.NumLights);
					shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

					if (HardwareConfig.SupportsPixelShaders_2_b()) {
						Span<float> specExponent = [0, 0, 0, parms[PHONGEXPONENT].GetFloatValue()];
						shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.EyePosSpecExponent, specExponent);

						DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
						pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
						pshIndex.Set("NUM_LIGHTS", lightState.NumLights);
						pshIndex.Set("AMBIENT_LIGHT", lightState.AmbientLight ? 1 : 0);
						pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", fullyOpaque && shaderAPI.ShouldWriteDepthToDestAlpha());
						shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
					}
					else {
						DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
						pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo());
						pshIndex.Set("NUM_LIGHTS", lightState.NumLights);
						pshIndex.Set("AMBIENT_LIGHT", lightState.AmbientLight ? 1 : 0);
						shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
					}
				}
				else {
					DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
					vshIndex.Set("DOWATERFOG", shaderAPI.GetSceneFogMode() == MaterialFogMode.LinearBelowFogZ);
					vshIndex.Set("SKINNING", shaderAPI.GetCurrentNumBones() > 0);
					vshIndex.Set("STATIC_LIGHT", lightState.StaticLightVertex ? 1 : 0);
					vshIndex.Set("MORPHING", shaderAPI.IsHWMorphingEnabled());
					vshIndex.Set("COMPRESSED_VERTS", (int)vertexCompression);
					shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

					Span<float> specExponent = [0, 0, 0, parms[PHONGEXPONENT].GetFloatValue()];
					shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.EyePosSpecExponent, specExponent);

					DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
					pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
					pshIndex.Set("NUM_LIGHTS", lightState.NumLights);
					pshIndex.Set("AMBIENT_LIGHT", lightState.AmbientLight ? 1 : 0);
					pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", fullyOpaque && shaderAPI.ShouldWriteDepthToDestAlpha());
					shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
				}
			}
			else {
				SetAmbientCubeDynamicStateVertexShader();

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

					if (HardwareConfig.SupportsPixelShaders_2_b()) {
						DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
						pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
						pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", fullyOpaque && shaderAPI.ShouldWriteDepthToDestAlpha());
						shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
					}
					else {
						DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
						pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo());
						shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
					}
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

					DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
					pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo1(true));
					pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", fullyOpaque && shaderAPI.ShouldWriteDepthToDestAlpha());
					shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
				}
			}

			if (parms[INTRO].GetIntValue() != 0) {
				float curTime = parms[WARPPARAM].GetFloatValue();
				Span<float> timeVec = [0.0f, 0.0f, 0.0f, curTime];
				Assert(parms[ENTITYORIGIN].IsDefined());
				parms[ENTITYORIGIN].GetVecValue(timeVec[..3]);
				shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst1, timeVec);
			}
		}
		Draw();
	}

	void DrawFlashlight(IMaterialVar[] parms, IShaderDynamicAPI? shaderAPI, IShaderShadow? shaderShadow, VertexCompressionType vertexCompression)
		=> throw new NotImplementedException();

	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (ShaderShadow != null)
			SetFlags2(vars, MaterialVarFlags2.LightingVertexLit);

		bool hasFlashlight = UsingFlashlight(vars);
		if (!hasFlashlight || r_flashlight_version2.GetInt() != 0) {
			DrawUsingVertexShader(vars, ShaderAPI, ShaderShadow, vertexCompression);
			if (ShaderShadow != null)
				SetInitialShadowState();
		}
		if (hasFlashlight)
			DrawFlashlight(vars, ShaderAPI, ShaderShadow, vertexCompression);
	}
}
