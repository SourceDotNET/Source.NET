using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class UnlitGeneric : BaseVSShader
{

	public static string HelpString = "Help for UnlitGeneric";
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

	public static readonly ShaderParam ALBEDO = new($"${nameof(ALBEDO)}", ShaderParamType.Texture, "shadertest/BaseTexture", "albedo (Base texture with no baked lighting)");
	public static readonly ShaderParam DETAIL = new($"${nameof(DETAIL)}", ShaderParamType.Texture, "shadertest/detail", "detail texture");
	public static readonly ShaderParam DETAILFRAME = new($"${nameof(DETAILFRAME)}", ShaderParamType.Integer, "0", "frame number for $detail");
	public static readonly ShaderParam DETAILSCALE = new($"${nameof(DETAILSCALE)}", ShaderParamType.Float, "4", "scale of the detail texture");
	public static readonly ShaderParam ENVMAP = new($"${nameof(ENVMAP)}", ShaderParamType.Texture, "shadertest/shadertest_env", "envmap");
	public static readonly ShaderParam ENVMAPFRAME = new($"${nameof(ENVMAPFRAME)}", ShaderParamType.Integer, "", "envmap frame number");
	public static readonly ShaderParam ENVMAPMASK = new($"${nameof(ENVMAPMASK)}", ShaderParamType.Texture, "shadertest/shadertest_envmask", "envmap mask");
	public static readonly ShaderParam ENVMAPMASKFRAME = new($"${nameof(ENVMAPMASKFRAME)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam ENVMAPMASKTRANSFORM = new($"${nameof(ENVMAPMASKTRANSFORM)}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$envmapmask texcoord transform");
	public static readonly ShaderParam ENVMAPTINT = new($"${nameof(ENVMAPTINT)}", ShaderParamType.Color, "[1 1 1]", "envmap tint");
	public static readonly ShaderParam ENVMAPCONTRAST = new($"${nameof(ENVMAPCONTRAST)}", ShaderParamType.Float, "0.0", "contrast 0 == normal 1 == color*color");
	public static readonly ShaderParam ENVMAPSATURATION = new($"${nameof(ENVMAPSATURATION)}", ShaderParamType.Float, "1.0", "saturation 0 == greyscale 1 == normal");
	public static readonly ShaderParam ALPHATESTREFERENCE = new($"${nameof(ALPHATESTREFERENCE)}", ShaderParamType.Float, "0.7", "");
	public static readonly ShaderParam VERTEXALPHATEST = new($"${nameof(VERTEXALPHATEST)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam HDRCOLORSCALE = new($"${nameof(HDRCOLORSCALE)}", ShaderParamType.Float, "1.0", "hdr color scale");
	public static readonly ShaderParam PHONGEXPONENT = new($"${nameof(PHONGEXPONENT)}", ShaderParamType.Float, "5.0", "Phong exponent for local specular lights");
	public static readonly ShaderParam PHONGTINT = new($"${nameof(PHONGTINT)}", ShaderParamType.Vec3, "5.0", "Phong tint for local specular lights");
	public static readonly ShaderParam PHONGALBEDOTINT = new($"${nameof(PHONGALBEDOTINT)}", ShaderParamType.Bool, "1.0", "Apply tint by albedo (controlled by spec exponent texture");
	public static readonly ShaderParam LIGHTWARPTEXTURE = new($"${nameof(LIGHTWARPTEXTURE)}", ShaderParamType.Texture, "shadertest/BaseTexture", "1D ramp texture for tinting scalar diffuse term");
	public static readonly ShaderParam PHONGWARPTEXTURE = new($"${nameof(PHONGWARPTEXTURE)}", ShaderParamType.Texture, "shadertest/BaseTexture", "2D map for warping specular");
	public static readonly ShaderParam PHONGFRESNELRANGES = new($"${nameof(PHONGFRESNELRANGES)}", ShaderParamType.Vec3, "[0  0.5  1]", "Parameters for remapping fresnel output");
	public static readonly ShaderParam PHONGBOOST = new($"${nameof(PHONGBOOST)}", ShaderParamType.Float, "1.0", "Phong overbrightening factor (specular mask channel should be authored to account for this)");
	public static readonly ShaderParam PHONGEXPONENTTEXTURE = new($"${nameof(PHONGEXPONENTTEXTURE)}", ShaderParamType.Texture, "shadertest/BaseTexture", "Phong Exponent map");
	public static readonly ShaderParam PHONG = new($"${nameof(PHONG)}", ShaderParamType.Bool, "0", "enables phong lighting");
	public static readonly ShaderParam DETAILBLENDMODE = new($"${nameof(DETAILBLENDMODE)}", ShaderParamType.Integer, "0", "mode for combining detail texture with base. 0=normal, 1= additive, 2=alpha blend detail over base, 3=crossfade");
	public static readonly ShaderParam DETAILBLENDFACTOR = new($"${nameof(DETAILBLENDFACTOR)}", ShaderParamType.Float, "1", "blend amount for detail texture.");
	public static readonly ShaderParam DETAILTEXTURETRANSFORM = new($"${nameof(DETAILTEXTURETRANSFORM)}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$detail texcoord transform");
	public static readonly ShaderParam SELFILLUMMASK = new($"${nameof(SELFILLUMMASK)}", ShaderParamType.Texture, "shadertest/BaseTexture", "If we bind a texture here, it overrides base alpha (if any) for self illum");
	public static readonly ShaderParam DISTANCEALPHA = new($"${nameof(DISTANCEALPHA)}", ShaderParamType.Bool, "0", "Use distance-coded alpha generated from hi-res texture by vtex.");
	public static readonly ShaderParam DISTANCEALPHAFROMDETAIL = new($"${nameof(DISTANCEALPHAFROMDETAIL)}", ShaderParamType.Bool, "0", "Take the distance-coded alpha mask from the detail texture.");
	public static readonly ShaderParam SOFTEDGES = new($"${nameof(SOFTEDGES)}", ShaderParamType.Bool, "0", "Enable soft edges to distance coded textures.");
	public static readonly ShaderParam SCALEEDGESOFTNESSBASEDONSCREENRES = new($"${nameof(SCALEEDGESOFTNESSBASEDONSCREENRES)}", ShaderParamType.Bool, "0", "Scale the size of the soft edges based upon resolution. 1024x768 = nominal.");
	public static readonly ShaderParam EDGESOFTNESSSTART = new($"${nameof(EDGESOFTNESSSTART)}", ShaderParamType.Float, "0.6", "Start value for soft edges for distancealpha.");
	public static readonly ShaderParam EDGESOFTNESSEND = new($"${nameof(EDGESOFTNESSEND)}", ShaderParamType.Float, "0.5", "End value for soft edges for distancealpha.");
	public static readonly ShaderParam GLOW = new($"${nameof(GLOW)}", ShaderParamType.Bool, "0", "Enable glow/shadow for distance coded textures.");
	public static readonly ShaderParam GLOWCOLOR = new($"${nameof(GLOWCOLOR)}", ShaderParamType.Color, "[1 1 1]", "color of outter glow for distance coded line art.");
	public static readonly ShaderParam GLOWALPHA = new($"${nameof(GLOWALPHA)}", ShaderParamType.Float, "1", "Base glow alpha amount for glows/shadows with distance alpha.");
	public static readonly ShaderParam GLOWSTART = new($"${nameof(GLOWSTART)}", ShaderParamType.Float, "0.7", "start value for glow/shadow");
	public static readonly ShaderParam GLOWEND = new($"${nameof(GLOWEND)}", ShaderParamType.Float, "0.5", "end value for glow/shadow");
	public static readonly ShaderParam GLOWX = new($"${nameof(GLOWX)}", ShaderParamType.Float, "0", "texture offset x for glow mask.");
	public static readonly ShaderParam GLOWY = new($"${nameof(GLOWY)}", ShaderParamType.Float, "0", "texture offset y for glow mask.");
	public static readonly ShaderParam OUTLINE = new($"${nameof(OUTLINE)}", ShaderParamType.Bool, "0", "Enable outline for distance coded textures.");
	public static readonly ShaderParam OUTLINECOLOR = new($"${nameof(OUTLINECOLOR)}", ShaderParamType.Color, "[1 1 1]", "color of outline for distance coded images.");
	public static readonly ShaderParam OUTLINEALPHA = new($"${nameof(OUTLINEALPHA)}", ShaderParamType.Float, "0.0", "alpha value for outline");
	public static readonly ShaderParam OUTLINESTART0 = new($"${nameof(OUTLINESTART0)}", ShaderParamType.Float, "0.0", "outer start value for outline");
	public static readonly ShaderParam OUTLINESTART1 = new($"${nameof(OUTLINESTART1)}", ShaderParamType.Float, "0.0", "inner start value for outline");
	public static readonly ShaderParam OUTLINEEND0 = new($"${nameof(OUTLINEEND0)}", ShaderParamType.Float, "0.0", "inner end value for outline");
	public static readonly ShaderParam OUTLINEEND1 = new($"${nameof(OUTLINEEND1)}", ShaderParamType.Float, "0.0", "outer end value for outline");
	public static readonly ShaderParam SCALEOUTLINESOFTNESSBASEDONSCREENRES = new($"${nameof(SCALEOUTLINESOFTNESSBASEDONSCREENRES)}", ShaderParamType.Bool, "0", "Scale the size of the soft part of the outline based upon resolution. 1024x768 = nominal.");
	public static readonly ShaderParam SEPARATEDETAILUVS = new($"${nameof(SEPARATEDETAILUVS)}", ShaderParamType.Bool, "0", "Use texcoord1 for detail texture");
	public static readonly ShaderParam GAMMACOLORREAD = new($"${nameof(GAMMACOLORREAD)}", ShaderParamType.Integer, "0", "Disables SRGB conversion of color texture read.");
	public static readonly ShaderParam LINEARWRITE = new($"${nameof(LINEARWRITE)}", ShaderParamType.Integer, "0", "Disables SRGB conversion of shader results.");
	public static readonly ShaderParam DEPTHBLEND = new($"${nameof(DEPTHBLEND)}", ShaderParamType.Integer, "0", "fade at intersection boundaries");
	public static readonly ShaderParam DEPTHBLENDSCALE = new($"${nameof(DEPTHBLENDSCALE)}", ShaderParamType.Float, "50.0", "Amplify or reduce DEPTHBLEND fading. Lower values make harder edges.");
	public static readonly ShaderParam RECEIVEFLASHLIGHT = new($"${nameof(RECEIVEFLASHLIGHT)}", ShaderParamType.Integer, "0", "Forces this material to receive flashlights.");

	private void SetupVars(ref VertexLitGeneric_Vars info) {
		info.BaseTexture = (int)ShaderMaterialVars.BaseTexture;
		info.BaseTextureFrame = (int)ShaderMaterialVars.Frame;
		info.BaseTextureTransform = (int)ShaderMaterialVars.BaseTextureTransform;
		info.Albedo = ALBEDO;
		info.SelfIllumTint = -1;
		info.Detail = DETAIL;
		info.DetailFrame = DETAILFRAME;
		info.DetailScale = DETAILSCALE;
		info.DetailTextureCombineMode = DETAILBLENDMODE;
		info.DetailTextureBlendFactor = DETAILBLENDFACTOR;
		info.DetailTextureTransform = DETAILTEXTURETRANSFORM;
		info.Envmap = ENVMAP;
		info.EnvmapFrame = ENVMAPFRAME;
		info.EnvmapMask = ENVMAPMASK;
		info.EnvmapMaskFrame = ENVMAPMASKFRAME;
		info.EnvmapMaskTransform = ENVMAPMASKTRANSFORM;
		info.EnvmapTint = ENVMAPTINT;
		info.Bumpmap = -1;
		info.BumpFrame = -1;
		info.BumpTransform = -1;
		info.EnvmapContrast = ENVMAPCONTRAST;
		info.EnvmapSaturation = ENVMAPSATURATION;
		info.AlphaTestReference = ALPHATESTREFERENCE;
		info.VertexAlphaTest = VERTEXALPHATEST;
		info.FlashlightTexture = (int)ShaderMaterialVars.FlashLightTexture;
		info.FlashlightTextureFrame = (int)ShaderMaterialVars.FlashLightTextureFrame;
		info.HDRColorScale = HDRCOLORSCALE;
		info.PhongExponent = -1;
		info.PhongExponentTexture = -1;
		info.DiffuseWarpTexture = -1;
		info.PhongWarpTexture = -1;
		info.PhongBoost = -1;
		info.PhongFresnelRanges = -1;
		info.Phong = -1;
		info.PhongTint = -1;
		info.PhongAlbedoTint = -1;
		info.SelfIllumEnvMapMask_Alpha = -1;
		info.AmbientOnly = -1;
		info.BaseMapAlphaPhongMask = -1;
		info.EnvmapFresnel = -1;
		info.SelfIllumMask = -1;
		info.DistanceAlpha = DISTANCEALPHA;
		info.DistanceAlphaFromDetail = DISTANCEALPHAFROMDETAIL;
		info.SoftEdges = SOFTEDGES;
		info.EdgeSoftnessStart = EDGESOFTNESSSTART;
		info.EdgeSoftnessEnd = EDGESOFTNESSEND;
		info.ScaleEdgeSoftnessBasedOnScreenRes = SCALEEDGESOFTNESSBASEDONSCREENRES;
		info.Glow = GLOW;
		info.GlowColor = GLOWCOLOR;
		info.GlowAlpha = GLOWALPHA;
		info.GlowStart = GLOWSTART;
		info.GlowEnd = GLOWEND;
		info.GlowX = GLOWX;
		info.GlowY = GLOWY;
		info.Outline = OUTLINE;
		info.OutlineColor = OUTLINECOLOR;
		info.OutlineAlpha = OUTLINEALPHA;
		info.OutlineStart0 = OUTLINESTART0;
		info.OutlineStart1 = OUTLINESTART1;
		info.OutlineEnd0 = OUTLINEEND0;
		info.OutlineEnd1 = OUTLINEEND1;
		info.ScaleOutlineSoftnessBasedOnScreenRes = SCALEOUTLINESOFTNESSBASEDONSCREENRES;
		info.SeparateDetailUVs = SEPARATEDETAILUVS;
		info.LinearWrite = LINEARWRITE;
		info.GammaColorRead = GAMMACOLORREAD;
		info.DepthBlend = DEPTHBLEND;
		info.DepthBlendScale = DEPTHBLENDSCALE;
		info.ReceiveFlashlight = RECEIVEFLASHLIGHT;
	}

	protected override void OnInitShaderParams(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		VertexLitGeneric_Vars vars = new();
		SetupVars(ref vars);
		InitParamsVertexLitGeneric(this, parms, materialName, false, ref vars);
	}

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

	protected override void OnInitShaderInstance(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		VertexLitGeneric_Vars vars = new();
		SetupVars(ref vars);
		InitVertexLitGeneric(this, parms, false, ref vars);
	}

	protected override void OnDrawElements(IMaterialVar[] parms, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression, ref BasePerMaterialContextData? contextData) {
		VertexLitGeneric_Vars vars = new();
		SetupVars(ref vars);
		// todo flashlight mode
		DrawVertexLitGeneric(this, parms, ShaderAPI, ShaderShadow, false, ref vars, vertexCompression, ref contextData);
	}
}
