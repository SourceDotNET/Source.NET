using Source.Common.GUI;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class WorldVertexTransition : BaseVSShader
{

	public static string HelpString = "Help for WorldVertexTransition";
	public static ShaderFlags Flags = ShaderFlags.None;
	public static List<ShaderParam> ShaderParams = [];
	public static ShaderParam[] ShaderParamOverrides = new ShaderParam[(int)ShaderMaterialVars.Count];

	public class ShaderParam
	{
		public readonly ShaderParamInfo Info;
		public readonly int Index;
		public ShaderParam(ShaderMaterialVars var, ShaderParamType type, ReadOnlySpan<char> defaultParam, ReadOnlySpan<char> help, ShaderParamFlags flags) {
			Info.Name = "override";
			Info.Type = type;
			Info.DefaultValue = new(defaultParam);
			Info.Help = new(help);
			Info.Flags = flags;

			if (ShaderParamOverrides[(int)var] == null) {

			}
			else {
				AssertMsg(false, "ShaderParamOverrides at var index had null value");
			}

			ShaderParamOverrides[(int)var] = this;
			Index = (int)var;
		}
		public ShaderParam(string name, ShaderParamType type, ReadOnlySpan<char> defaultParam, ReadOnlySpan<char> help, ShaderParamFlags flags = 0) {
			Info.Name = name;
			Info.Type = type;
			Info.DefaultValue = new(defaultParam);
			Info.Help = new(help);
			Info.Flags = flags;
			Index = (int)ShaderMaterialVars.Count + ShaderParams.Count;
			ShaderParams.Add(this);
		}
		public static implicit operator int(ShaderParam param) => param.Index;
		public ReadOnlySpan<char> GetName() => Info.Name;
		public ShaderParamType GetType() => Info.Type;
		public ReadOnlySpan<char> GetDefaultValue() => Info.DefaultValue;
		public ShaderParamFlags GetFlags() => Info.Flags;
		public ReadOnlySpan<char> GetHelp() => Info.Help;
	}

	public static readonly ShaderParam BASETEXTURE2 = new($"{BASETEXTURE2}", ShaderParamType.Texture, "shadertest/BaseTexture2", "base texture2 help");
	public static readonly ShaderParam FRAME2 = new($"{FRAME2}", ShaderParamType.Integer, "0", "frame number for $baseTexture");
	public static readonly ShaderParam BASETEXTURETRANSFORM2 = new($"{BASETEXTURETRANSFORM2}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$baseTexture texcoord transform");
	public static readonly ShaderParam SELFILLUMTINT = new($"{SELFILLUMTINT}", ShaderParamType.Color, "[1 1 1]", "Self-illumination tint");
	public static readonly ShaderParam DETAIL = new($"{DETAIL}", ShaderParamType.Texture, "shadertest/detail", "detail texture");
	public static readonly ShaderParam DETAILFRAME = new($"{DETAILFRAME}", ShaderParamType.Integer, "0", "frame number for $detail");
	public static readonly ShaderParam DETAILSCALE = new($"{DETAILSCALE}", ShaderParamType.Float, "4", "scale of the detail texture");
	public static readonly ShaderParam ENVMAP = new($"{ENVMAP}", ShaderParamType.Texture, "shadertest/shadertest_env", "envmap");
	public static readonly ShaderParam ENVMAPFRAME = new($"{ENVMAPFRAME}", ShaderParamType.Integer, "", "");
	public static readonly ShaderParam ENVMAPMASK = new($"{ENVMAPMASK}", ShaderParamType.Texture, "shadertest/shadertest_envmask", "envmap mask");
	public static readonly ShaderParam ENVMAPMASKFRAME = new($"{ENVMAPMASKFRAME}", ShaderParamType.Integer, "", "");
	public static readonly ShaderParam ENVMAPMASKSCALE = new($"{ENVMAPMASKSCALE}", ShaderParamType.Float, "1", "envmap mask scale");
	public static readonly ShaderParam ENVMAPTINT = new($"{ENVMAPTINT}", ShaderParamType.Color, "[1 1 1]", "envmap tint");
	public static readonly ShaderParam BUMPMAP = new($"{BUMPMAP}", ShaderParamType.Texture, "models/shadertest/shader1_normal", "bump map for BASETEXTURE");
	public static readonly ShaderParam BUMPFRAME = new($"{BUMPFRAME}", ShaderParamType.Integer, "0", "frame number for $bumpmap");
	public static readonly ShaderParam BUMPTRANSFORM = new($"{BUMPTRANSFORM}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$bumpmap texcoord transform");
	public static readonly ShaderParam ENVMAPCONTRAST = new($"{ENVMAPCONTRAST}", ShaderParamType.Float, "0.0", "contrast 0 == normal 1 == color*color");
	public static readonly ShaderParam ENVMAPSATURATION = new($"{ENVMAPSATURATION}", ShaderParamType.Float, "1.0", "saturation 0 == greyscale 1 == normal");
	public static readonly ShaderParam BUMPBASETEXTURE2WITHBUMPMAP = new($"{BUMPBASETEXTURE2WITHBUMPMAP}", ShaderParamType.Bool, "0", "");
	public static readonly ShaderParam FRESNELREFLECTION = new($"{FRESNELREFLECTION}", ShaderParamType.Float, "0.0", "1.0 == mirror, 0.0 == water");
	public static readonly ShaderParam SSBUMP = new($"{SSBUMP}", ShaderParamType.Integer, "0", "whether or not to use alternate bumpmap format with height");
	public static readonly ShaderParam SEAMLESS_SCALE = new($"{SEAMLESS_SCALE}", ShaderParamType.Float, "0", "Scale factor for 'seamless' texture mapping. 0 means to use ordinary mapping");
	public static readonly ShaderParam BLENDMODULATETEXTURE = new($"{BLENDMODULATETEXTURE}", ShaderParamType.Texture, "", "texture to use r/g channels for blend range for");
	public static readonly ShaderParam BLENDMASKTRANSFORM = new($"{BLENDMASKTRANSFORM}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$blendmodulatetexture texcoord transform");


	protected override void OnInitShaderParams(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		SetFlags2(vars, MaterialVarFlags2.LightingLightmap);

		Params![(int)ShaderMaterialVars.FlashLightTexture].SetStringValue("effects/flashlight001");
		if (!Params[ENVMAPMASKSCALE].IsDefined())
			Params[ENVMAPMASKSCALE].SetFloatValue(1.0f);

		if (!Params[ENVMAPTINT].IsDefined())
			Params[ENVMAPTINT].SetVecValue(1.0f, 1.0f, 1.0f);

		if (!Params[SELFILLUMTINT].IsDefined())
			Params[SELFILLUMTINT].SetVecValue(1.0f, 1.0f, 1.0f);

		if (!Params[DETAILSCALE].IsDefined())
			Params[DETAILSCALE].SetFloatValue(4.0f);

		if (!Params[FRESNELREFLECTION].IsDefined())
			Params[FRESNELREFLECTION].SetFloatValue(1.0f);

		if (!Params[ENVMAPMASKFRAME].IsDefined())
			Params[ENVMAPMASKFRAME].SetIntValue(0);

		if (!Params[ENVMAPFRAME].IsDefined())
			Params[ENVMAPFRAME].SetIntValue(0);

		if (!Params[BUMPFRAME].IsDefined())
			Params[BUMPFRAME].SetIntValue(0);

		if (!Params[ENVMAPCONTRAST].IsDefined())
			Params[ENVMAPCONTRAST].SetFloatValue(0.0f);

		if (!Params[ENVMAPSATURATION].IsDefined())
			Params[ENVMAPSATURATION].SetFloatValue(1.0f);

		// No texture means no self-illum or env mask in base alpha
		if (!Params[(int)ShaderMaterialVars.BaseTexture].IsDefined()) {
			ClearFlags(Params, MaterialVarFlags.SelfIllum);
			ClearFlags(Params, MaterialVarFlags.BaseAlphaEnvMapMask);
		}

		// If in decal mode, no debug override...
		if (IsFlagSet(Params, MaterialVarFlags.Decal))
			SetFlags(Params, MaterialVarFlags.NoDebugOverride);

		if (!Params[BUMPBASETEXTURE2WITHBUMPMAP].IsDefined())
			Params[BUMPBASETEXTURE2WITHBUMPMAP].SetIntValue(0);

		if (!Params[DETAILSCALE].IsDefined())
			Params[DETAILSCALE].SetFloatValue(4.0f);

		if (!Params[DETAILFRAME].IsDefined())
			Params[DETAILFRAME].SetIntValue(0);

		if (Params[SEAMLESS_SCALE].IsDefined() && Params[SEAMLESS_SCALE].GetFloatValue() != 0.0f) {
			// seamless scale is going to be used, so kill some other features. . might implement with these features later.
			Params[DETAIL].SetUndefined();
			Params[BUMPMAP].SetUndefined();
			Params[ENVMAP].SetUndefined();
		}

		if (!Params[SEAMLESS_SCALE].IsDefined())
			// zero means don't do seamless mapping.
			Params[SEAMLESS_SCALE].SetFloatValue(0.0f);

		if (Params[SSBUMP].IsDefined() && Params[SSBUMP].GetIntValue() != 0)
			// turn of normal mapping since we have ssbump defined, which 
			// means that we didn't make a dx8 fallback for this material.
			Params[BUMPMAP].SetUndefined();

	}

	public override string? GetFallbackShader(IMaterialVar[] vars) {
		return "LightmappedGeneric";
	}
	public override ShaderFlags GetFlags() => Flags;
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
	protected override void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		if (Params![(int)ShaderMaterialVars.BaseTexture].IsDefined())
			LoadTexture((int)ShaderMaterialVars.BaseTexture);
		if (Params![BASETEXTURE2].IsDefined())
			LoadTexture((int)ShaderMaterialVars.BaseTexture);
	}
	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		bool bumpedEnvMap = false; // todo
		DrawDetailUsingVertexShader(vars, shaderAPI, ShaderShadow, bumpedEnvMap);
	}

	private void DrawDetailUsingVertexShader(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, IShaderShadow? shaderShadow, bool bumpedEnvMap) {
		DrawDetailNoEnvmap(vars, shaderAPI, shaderShadow, IsFlagSet(vars, MaterialVarFlags.SelfIllum));
	}

	private void DrawDetailNoEnvmap(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, IShaderShadow? shaderShadow, bool dlSelfIllum) {
		if (IsSnapshotting()) {
			ShaderShadow.EnableTexture(Sampler.Sampler0, true);
			ShaderShadow.EnableTexture(Sampler.Sampler1, true);
			ShaderShadow.EnableTexture(Sampler.Sampler2, true); // Lightmap

			SetDefaultBlendingShadowState((int)ShaderMaterialVars.BaseTexture, true);
			ShaderShadow.VertexShaderVertexFormat(VertexFormat.Position | VertexFormat.Normal | VertexFormat.TexCoord2D_0 | VertexFormat.TexCoord2D_1 | VertexFormat.Color, 2, null, 0);

			ShaderShadow.SetVertexShader("worldvertextransition");
			ShaderShadow.SetPixelShader("worldvertextransition");
		}
		else {
			BindTexture(Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, (int)ShaderMaterialVars.Frame);

			BindTexture(Sampler.Sampler1, BASETEXTURE2, FRAME2);
			shaderAPI.SetShaderUniform(shaderAPI.LocateShaderUniform("basetexture2"), 1);

			ShaderAPI!.BindStandardTexture(Sampler.Sampler2, StandardTextureId.Lightmap);
			shaderAPI.SetShaderUniform(shaderAPI.LocateShaderUniform("lightmaptexture"), 2);

			BindTexture(Sampler.Sampler2, DETAIL, (int)ShaderMaterialVars.Frame);
		}
		Draw();
	}
}
