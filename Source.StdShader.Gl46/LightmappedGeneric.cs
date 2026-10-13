using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

using System.Diagnostics;

namespace Source.StdShader.Gl46;

public class LightmappedGeneric : BaseVSShader
{

	public static string HelpString = "Help for LightmappedGeneric";
	public static ShaderFlags Flags = ShaderFlags.None;
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
			AssertMsg(ShaderParamOverrides[(int)var] == null, "Shader parameter override duplicately defined!");
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

	public static readonly ShaderParam ALBEDO = new($"${nameof(ALBEDO)}", ShaderParamType.Texture, "shadertest/BaseTexture", "albedo (Base texture with no baked lighting)");
	public static readonly ShaderParam SELFILLUMTINT = new($"${nameof(SELFILLUMTINT)}", ShaderParamType.Color, "[1 1 1]", "Self-illumination tint");
	public static readonly ShaderParam DETAIL = new($"${nameof(DETAIL)}", ShaderParamType.Texture, "shadertest/detail", "detail texture");
	public static readonly ShaderParam DETAILFRAME = new($"${nameof(DETAILFRAME)}", ShaderParamType.Integer, "0", "frame number for $detail");
	public static readonly ShaderParam DETAILSCALE = new($"${nameof(DETAILSCALE)}", ShaderParamType.Float, "4", "scale of the detail texture");
	public static readonly ShaderParam ALPHA2 = new($"${nameof(ALPHA2)}", ShaderParamType.Float, "1", "");
	public static readonly ShaderParam DETAILBLENDMODE = new($"${nameof(DETAILBLENDMODE)}", ShaderParamType.Integer, "0", "mode for combining detail texture with base. 0=normal, 1= additive, 2=alpha blend detail over base, 3=crossfade");
	public static readonly ShaderParam DETAILBLENDFACTOR = new($"${nameof(DETAILBLENDFACTOR)}", ShaderParamType.Float, "1", "blend amount for detail texture.");
	public static readonly ShaderParam DETAILTINT = new($"${nameof(DETAILTINT)}", ShaderParamType.Color, "[1 1 1]", "detail texture tint");
	public static readonly ShaderParam ENVMAP = new($"${nameof(ENVMAP)}", ShaderParamType.Texture, "shadertest/shadertest_env", "envmap");
	public static readonly ShaderParam ENVMAPFRAME = new($"${nameof(ENVMAPFRAME)}", ShaderParamType.Integer, "", "");
	public static readonly ShaderParam ENVMAPMASK = new($"${nameof(ENVMAPMASK)}", ShaderParamType.Texture, "shadertest/shadertest_envmask", "envmap mask");
	public static readonly ShaderParam ENVMAPMASKFRAME = new($"${nameof(ENVMAPMASKFRAME)}", ShaderParamType.Integer, "", "");
	public static readonly ShaderParam ENVMAPMASKTRANSFORM = new($"${nameof(ENVMAPMASKTRANSFORM)}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$envmapmask texcoord transform");
	public static readonly ShaderParam ENVMAPTINT = new($"${nameof(ENVMAPTINT)}", ShaderParamType.Color, "[1 1 1]", "envmap tint");
	public static readonly ShaderParam BUMPMAP = new($"${nameof(BUMPMAP)}", ShaderParamType.Texture, "models/shadertest/shader1_normal", "bump map");
	public static readonly ShaderParam BUMPFRAME = new($"${nameof(BUMPFRAME)}", ShaderParamType.Integer, "0", "frame number for $bumpmap");
	public static readonly ShaderParam BUMPTRANSFORM = new($"${nameof(BUMPTRANSFORM)}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$bumpmap texcoord transform");
	public static readonly ShaderParam ENVMAPCONTRAST = new($"${nameof(ENVMAPCONTRAST)}", ShaderParamType.Float, "0.0", "contrast 0 == normal 1 == color*color");
	public static readonly ShaderParam ENVMAPSATURATION = new($"${nameof(ENVMAPSATURATION)}", ShaderParamType.Float, "1.0", "saturation 0 == greyscale 1 == normal");
	public static readonly ShaderParam FRESNELREFLECTION = new($"${nameof(FRESNELREFLECTION)}", ShaderParamType.Float, "1.0", "1.0 == mirror, 0.0 == water");
	public static readonly ShaderParam NODIFFUSEBUMPLIGHTING = new($"${nameof(NODIFFUSEBUMPLIGHTING)}", ShaderParamType.Integer, "0", "0 == Use diffuse bump lighting, 1 = No diffuse bump lighting");
	public static readonly ShaderParam BUMPMAP2 = new($"${nameof(BUMPMAP2)}", ShaderParamType.Texture, "models/shadertest/shader3_normal", "bump map");
	public static readonly ShaderParam BUMPFRAME2 = new($"${nameof(BUMPFRAME2)}", ShaderParamType.Integer, "0", "frame number for $bumpmap");
	public static readonly ShaderParam BUMPTRANSFORM2 = new($"${nameof(BUMPTRANSFORM2)}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$bumpmap texcoord transform");
	public static readonly ShaderParam BUMPMASK = new($"${nameof(BUMPMASK)}", ShaderParamType.Texture, "models/shadertest/shader3_normal", "bump map");
	public static readonly ShaderParam BASETEXTURE2 = new($"${nameof(BASETEXTURE2)}", ShaderParamType.Texture, "shadertest/lightmappedtexture", "Blended texture");
	public static readonly ShaderParam FRAME2 = new($"${nameof(FRAME2)}", ShaderParamType.Integer, "0", "frame number for $basetexture2");
	public static readonly ShaderParam BASETEXTURENOENVMAP = new($"${nameof(BASETEXTURENOENVMAP)}", ShaderParamType.Bool, "0", "");
	public static readonly ShaderParam BASETEXTURE2NOENVMAP = new($"${nameof(BASETEXTURE2NOENVMAP)}", ShaderParamType.Bool, "0", "");
	public static readonly ShaderParam DETAIL_ALPHA_MASK_BASE_TEXTURE = new($"${nameof(DETAIL_ALPHA_MASK_BASE_TEXTURE)}", ShaderParamType.Bool, "0", "If this is 1, then when detail alpha=0, no base texture is blended and when sdetail alpha=1, you get detail*base*lightmap");
	public static readonly ShaderParam LIGHTWARPTEXTURE = new($"${nameof(LIGHTWARPTEXTURE)}", ShaderParamType.Texture, "", "light munging lookup texture");
	public static readonly ShaderParam BLENDMODULATETEXTURE = new($"${nameof(BLENDMODULATETEXTURE)}", ShaderParamType.Texture, "", "texture to use r/g channels for blend range for");
	public static readonly ShaderParam MASKEDBLENDING = new($"${nameof(MASKEDBLENDING)}", ShaderParamType.Integer, "0", "blend using texture with no vertex alpha. For using texture blending on non-displacements");
	public static readonly ShaderParam BLENDMASKTRANSFORM = new($"${nameof(BLENDMASKTRANSFORM)}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$blendmodulatetexture texcoord transform");
	public static readonly ShaderParam SSBUMP = new($"${nameof(SSBUMP)}", ShaderParamType.Integer, "0", "whether or not to use alternate bumpmap format with height");
	public static readonly ShaderParam SEAMLESS_SCALE = new($"${nameof(SEAMLESS_SCALE)}", ShaderParamType.Float, "0", "Scale factor for 'seamless' texture mapping. 0 means to use ordinary mapping");
	public static readonly ShaderParam ALPHATESTREFERENCE = new($"${nameof(ALPHATESTREFERENCE)}", ShaderParamType.Float, "0.0", "");
	public static readonly ShaderParam SOFTEDGES = new($"${nameof(SOFTEDGES)}", ShaderParamType.Bool, "0", "Enable soft edges to distance coded textures.");
	public static readonly ShaderParam EDGESOFTNESSSTART = new($"${nameof(EDGESOFTNESSSTART)}", ShaderParamType.Float, "0.6", "Start value for soft edges for distancealpha.");
	public static readonly ShaderParam EDGESOFTNESSEND = new($"${nameof(EDGESOFTNESSEND)}", ShaderParamType.Float, "0.5", "End value for soft edges for distancealpha.");
	public static readonly ShaderParam OUTLINE = new($"${nameof(OUTLINE)}", ShaderParamType.Bool, "0", "Enable outline for distance coded textures.");
	public static readonly ShaderParam OUTLINECOLOR = new($"${nameof(OUTLINECOLOR)}", ShaderParamType.Color, "[1 1 1]", "color of outline for distance coded images.");
	public static readonly ShaderParam OUTLINEALPHA = new($"${nameof(OUTLINEALPHA)}", ShaderParamType.Float, "0.0", "alpha value for outline");
	public static readonly ShaderParam OUTLINESTART0 = new($"${nameof(OUTLINESTART0)}", ShaderParamType.Float, "0.0", "outer start value for outline");
	public static readonly ShaderParam OUTLINESTART1 = new($"${nameof(OUTLINESTART1)}", ShaderParamType.Float, "0.0", "inner start value for outline");
	public static readonly ShaderParam OUTLINEEND0 = new($"${nameof(OUTLINEEND0)}", ShaderParamType.Float, "0.0", "inner end value for outline");
	public static readonly ShaderParam OUTLINEEND1 = new($"${nameof(OUTLINEEND1)}", ShaderParamType.Float, "0.0", "outer end value for outline");

	public override string? GetFallbackShader(IMaterialVar[] vars) => null;
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

	static LightmappedGeneric_Vars Info;

	protected override void OnInitShaderParams(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		SetupVars(ref Info);
		InitParamsLightmappedGeneric(this, vars, materialName, ref Info);
	}
	protected override void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		SetupVars(ref Info);
		InitLightmappedGeneric(this, vars, ref Info);
	}
	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression, ref BasePerMaterialContextData? context) {
		DrawLightmappedGeneric(this, vars, shaderAPI, ShaderShadow, ref Info, ref context);
	}

	private void SetupVars(ref LightmappedGeneric_Vars info) {
		info.BaseTexture = (int)ShaderMaterialVars.BaseTexture;
		info.BaseTextureFrame = (int)ShaderMaterialVars.Frame;
		info.BaseTextureTransform = (int)ShaderMaterialVars.BaseTextureTransform;
		info.Albedo = ALBEDO;
		info.SelfIllumTint = SELFILLUMTINT;
		info.Alpha2 = ALPHA2;
		info.Detail = DETAIL;
		info.DetailFrame = DETAILFRAME;
		info.DetailScale = DETAILSCALE;
		info.DetailTextureCombineMode = DETAILBLENDMODE;
		info.DetailTextureBlendFactor = DETAILBLENDFACTOR;
		info.DetailTint = DETAILTINT;
		info.Envmap = ENVMAP;
		info.EnvmapFrame = ENVMAPFRAME;
		info.EnvmapMask = ENVMAPMASK;
		info.EnvmapMaskFrame = ENVMAPMASKFRAME;
		info.EnvmapMaskTransform = ENVMAPMASKTRANSFORM;
		info.EnvmapTint = ENVMAPTINT;
		info.Bumpmap = BUMPMAP;
		info.BumpFrame = BUMPFRAME;
		info.BumpTransform = BUMPTRANSFORM;
		info.EnvmapContrast = ENVMAPCONTRAST;
		info.EnvmapSaturation = ENVMAPSATURATION;
		info.FresnelReflection = FRESNELREFLECTION;
		info.NoDiffuseBumpLighting = NODIFFUSEBUMPLIGHTING;
		info.Bumpmap2 = BUMPMAP2;
		info.BumpFrame2 = BUMPFRAME2;
		info.BumpTransform2 = BUMPTRANSFORM2;
		info.BumpMask = BUMPMASK;
		info.BaseTexture2 = BASETEXTURE2;
		info.BaseTexture2Frame = FRAME2;
		info.BaseTextureNoEnvmap = BASETEXTURENOENVMAP;
		info.BaseTexture2NoEnvmap = BASETEXTURE2NOENVMAP;
		info.DetailAlphaMaskBaseTexture = DETAIL_ALPHA_MASK_BASE_TEXTURE;
		info.FlashlightTexture = (int)ShaderMaterialVars.FlashLightTexture;
		info.FlashlightTextureFrame = (int)ShaderMaterialVars.FlashLightTextureFrame;
		info.LightWarpTexture = LIGHTWARPTEXTURE;
		info.BlendModulateTexture = BLENDMODULATETEXTURE;
		info.MaskedBlending = MASKEDBLENDING;
		info.BlendMaskTransform = BLENDMASKTRANSFORM;
		info.SelfShadowedBumpFlag = SSBUMP;
		info.SeamlessMappingScale = SEAMLESS_SCALE;
		info.AlphaTestReference = ALPHATESTREFERENCE;
		info.SoftEdges = SOFTEDGES;
		info.EdgeSoftnessStart = EDGESOFTNESSSTART;
		info.EdgeSoftnessEnd = EDGESOFTNESSEND;
		info.Outline = OUTLINE;
		info.OutlineColor = OUTLINECOLOR;
		info.OutlineAlpha = OUTLINEALPHA;
		info.OutlineStart0 = OUTLINESTART0;
		info.OutlineStart1 = OUTLINESTART1;
		info.OutlineEnd0 = OUTLINEEND0;
		info.OutlineEnd1 = OUTLINEEND1;
	}
}
