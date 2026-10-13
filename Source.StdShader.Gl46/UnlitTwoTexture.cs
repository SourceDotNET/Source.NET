using Source.Common;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class UnlitTwoTexture : BaseVSShader
{
	public static string HelpString = "Help for UnlitTwoTexture";
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

	public static readonly ShaderParam TEXTURE2 = new($"${nameof(TEXTURE2)}", ShaderParamType.Texture, "shadertest/BaseTexture", "second texture");
	public static readonly ShaderParam FRAME2 = new($"${nameof(FRAME2)}", ShaderParamType.Integer, "0", "frame number for $texture2");
	public static readonly ShaderParam TEXTURE2TRANSFORM = new($"${nameof(TEXTURE2TRANSFORM)}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$texture2 texcoord transform");

	public static readonly ShaderParam CLOAKPASSENABLED = new($"${nameof(CLOAKPASSENABLED)}", ShaderParamType.Bool, "0", "Enables cloak render in a second pass");
	public static readonly ShaderParam CLOAKFACTOR = new($"${nameof(CLOAKFACTOR)}", ShaderParamType.Float, "0.0", "");
	public static readonly ShaderParam CLOAKCOLORTINT = new($"${nameof(CLOAKCOLORTINT)}", ShaderParamType.Color, "[1 1 1]", "Cloak color tint");
	public static readonly ShaderParam REFRACTAMOUNT = new($"${nameof(REFRACTAMOUNT)}", ShaderParamType.Float, "2", "");

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

	public override bool IsTranslucent(IMaterialVar[]? parms) {
		if (parms![CLOAKPASSENABLED].GetIntValue() != 0) {
			if ((parms[CLOAKFACTOR].GetFloatValue() > 0.0f) && (parms[CLOAKFACTOR].GetFloatValue() < 1.0f))
				return true;
		}

		return IsFlagSet(parms, (int)MaterialVarFlags.Translucent);
	}

	protected override void OnInitShaderParams(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		SetFlags2(parms, MaterialVarFlags2.SupportsHardwareSkinning);

		if (!parms[CLOAKPASSENABLED].IsDefined())
			parms[CLOAKPASSENABLED].SetIntValue(0);
		else if (parms[CLOAKPASSENABLED].GetIntValue() != 0)
			throw new NotImplementedException();
	}

	protected override void OnInitShaderInstance(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		if (parms[(int)ShaderMaterialVars.BaseTexture].IsDefined())
			LoadTexture((int)ShaderMaterialVars.BaseTexture, (int)TextureFlags.SRGB);
		if (parms[TEXTURE2].IsDefined())
			LoadTexture(TEXTURE2, (int)TextureFlags.SRGB);

		if (parms[CLOAKPASSENABLED].GetIntValue() != 0)
			throw new NotImplementedException();
	}

	protected override void OnDrawElements(IMaterialVar[] parms, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (parms[CLOAKPASSENABLED].GetIntValue() != 0)
			throw new NotImplementedException();

		bool drawStandardPass = true;

		bool newFlashlightPath = r_flashlight_version2.GetInt() != 0;
		if (drawStandardPass && (ShaderShadow == null) && (ShaderAPI != null) &&
			!newFlashlightPath && ShaderAPI.InFlashlightMode())
			drawStandardPass = false;

		if (drawStandardPass) {
			BlendType blendType = EvaluateBlendRequirements((int)ShaderMaterialVars.BaseTexture, true);
			bool fullyOpaque = (blendType != BlendType.BlendAdd) && (blendType != BlendType.Blend) && !IsFlagSet(parms, MaterialVarFlags.AlphaTest);

			if (ShaderShadow != null) {
				ShaderShadow.EnableTexture(Sampler.Sampler0, true);
				ShaderShadow.EnableSRGBRead(Sampler.Sampler0, true);

				ShaderShadow.EnableTexture(Sampler.Sampler1, true);
				ShaderShadow.EnableSRGBRead(Sampler.Sampler1, true);

				ShaderShadow.EnableSRGBWrite(true);

				bool isTranslucent = IsAlphaModulating();

				isTranslucent = isTranslucent || TextureIsTranslucent((int)ShaderMaterialVars.BaseTexture, true) ||
					TextureIsTranslucent(TEXTURE2, true);

				if (isTranslucent) {
					if (IsFlagSet(parms, MaterialVarFlags.Additive))
						EnableAlphaBlending(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.One);
					else
						EnableAlphaBlending(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.OneMinusSrcAlpha);
				}
				else {
					if (IsFlagSet(parms, MaterialVarFlags.Additive))
						EnableAlphaBlending(ShaderBlendFactor.One, ShaderBlendFactor.One);
					else
						DisableAlphaBlending();
				}

				VertexFormat flags = VertexFormat.Position | VertexFormat.Normal;
				int texCoordCount = 1;
				int userDataSize = 0;
				if (IsFlagSet(parms, MaterialVarFlags.VertexColor))
					flags |= VertexFormat.Color;
				ShaderShadow.VertexShaderVertexFormat(flags, texCoordCount, null, userDataSize);

				StaticShaderIndex vshIndex = new(ShaderShadow, ShaderType.Vertex, "unlittwotexture");
				ShaderShadow.SetVertexShader("unlittwotexture", vshIndex.GetIndex());

				StaticShaderIndex pshIndex = new(ShaderShadow, ShaderType.Pixel, "unlittwotexture");
				ShaderShadow.SetPixelShader("unlittwotexture", pshIndex.GetIndex());

				DefaultFog();

				ShaderShadow.EnableAlphaWrites(fullyOpaque);
			}
			else if (ShaderAPI != null) {
				BindTexture(Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, (int)ShaderMaterialVars.Frame);
				BindTexture(Sampler.Sampler1, TEXTURE2, FRAME2);
				SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst0, (int)ShaderMaterialVars.BaseTextureTransform);
				SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst2, TEXTURE2TRANSFORM);
				SetModulationPixelShaderDynamicState_LinearColorSpace(1);

				ShaderAPI.SetPixelShaderFogParams((int)PixelShaderConst.FogParams);

				Span<float> eyePos_SpecExponent = [0, 0, 0, 0];
				ShaderAPI.GetWorldSpaceCameraPosition(eyePos_SpecExponent);
				eyePos_SpecExponent[3] = 0.0f;
				ShaderAPI.SetPixelShaderConstant((int)PixelShaderConst.EyePosSpecExponent, eyePos_SpecExponent);

				MaterialFogMode fogType = ShaderAPI.GetSceneFogMode();
				int fogIndex = (fogType == MaterialFogMode.LinearBelowFogZ) ? 1 : 0;
				int numBones = ShaderAPI.GetCurrentNumBones();

				DynamicShaderIndex vshIndex = new(ShaderAPI, ShaderType.Vertex);
				vshIndex.Set("SKINNING", numBones > 0);
				vshIndex.Set("DOWATERFOG", fogIndex);
				vshIndex.Set("COMPRESSED_VERTS", (int)vertexCompression);
				ShaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

				DynamicShaderIndex pshIndex = new(ShaderAPI, ShaderType.Pixel);
				pshIndex.Set("PIXELFOGTYPE", ShaderAPI.GetPixelFogCombo());
				pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", fullyOpaque && ShaderAPI.ShouldWriteDepthToDestAlpha());
				ShaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
			}
			Draw();
		}
		else
			Draw(false);
	}
}
