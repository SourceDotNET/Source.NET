using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

using System.Runtime.InteropServices;

namespace Source.StdShader.Gl46;

public class VertexLitGeneric : BaseVSShader
{

	public static string HelpString = "Help for VertexLitGeneric";
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
			AssertMsg(ShaderParamOverrides[(int)var] == null, "Shader parameter override duplicately defined!");
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
	public static readonly ShaderParam COMPRESS = new($"${nameof(COMPRESS)}", ShaderParamType.Texture, "shadertest/BaseTexture", "compression wrinklemap");
	public static readonly ShaderParam STRETCH = new($"${nameof(STRETCH)}", ShaderParamType.Texture, "shadertest/BaseTexture", "expansion wrinklemap");
	public static readonly ShaderParam SELFILLUMTINT = new($"${nameof(SELFILLUMTINT)}", ShaderParamType.Color, "[1 1 1]", "Self-illumination tint");
	public static readonly ShaderParam DETAIL = new($"${nameof(DETAIL)}", ShaderParamType.Texture, "shadertest/detail", "detail texture");
	public static readonly ShaderParam DETAILFRAME = new($"${nameof(DETAILFRAME)}", ShaderParamType.Integer, "0", "frame number for $detail");
	public static readonly ShaderParam DETAILSCALE = new($"${nameof(DETAILSCALE)}", ShaderParamType.Float, "4", "scale of the detail texture");
	public static readonly ShaderParam ENVMAP = new($"${nameof(ENVMAP)}", ShaderParamType.Texture, "shadertest/shadertest_env", "envmap");
	public static readonly ShaderParam ENVMAPFRAME = new($"${nameof(ENVMAPFRAME)}", ShaderParamType.Integer, "0", "envmap frame number");
	public static readonly ShaderParam ENVMAPMASK = new($"${nameof(ENVMAPMASK)}", ShaderParamType.Texture, "shadertest/shadertest_envmask", "envmap mask");
	public static readonly ShaderParam ENVMAPMASKFRAME = new($"${nameof(ENVMAPMASKFRAME)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam ENVMAPMASKTRANSFORM = new($"${nameof(ENVMAPMASKTRANSFORM)}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$envmapmask texcoord transform");
	public static readonly ShaderParam ENVMAPTINT = new($"${nameof(ENVMAPTINT)}", ShaderParamType.Color, "[1 1 1]", "envmap tint");
	public static readonly ShaderParam BUMPMAP = new($"${nameof(BUMPMAP)}", ShaderParamType.Texture, "models/shadertest/shader1_normal", "bump map");
	public static readonly ShaderParam BUMPCOMPRESS = new($"${nameof(BUMPCOMPRESS)}", ShaderParamType.Texture, "models/shadertest/shader3_normal", "compression bump map");
	public static readonly ShaderParam BUMPSTRETCH = new($"${nameof(BUMPSTRETCH)}", ShaderParamType.Texture, "models/shadertest/shader1_normal", "expansion bump map");
	public static readonly ShaderParam BUMPFRAME = new($"${nameof(BUMPFRAME)}", ShaderParamType.Integer, "0", "frame number for $bumpmap");
	public static readonly ShaderParam BUMPTRANSFORM = new($"${nameof(BUMPTRANSFORM)}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$bumpmap texcoord transform");
	public static readonly ShaderParam ENVMAPCONTRAST = new($"${nameof(ENVMAPCONTRAST)}", ShaderParamType.Float, "0.0", "contrast 0 == normal 1 == color*color");
	public static readonly ShaderParam ENVMAPSATURATION = new($"${nameof(ENVMAPSATURATION)}", ShaderParamType.Float, "1.0", "saturation 0 == greyscale 1 == normal");
	public static readonly ShaderParam SELFILLUM_ENVMAPMASK_ALPHA = new($"${nameof(SELFILLUM_ENVMAPMASK_ALPHA)}", ShaderParamType.Float, "0.0", "defines that self illum value comes from env map mask alpha");
	public static readonly ShaderParam SELFILLUMFRESNEL = new($"${nameof(SELFILLUMFRESNEL)}", ShaderParamType.Bool, "0", "Self illum fresnel");
	public static readonly ShaderParam SELFILLUMFRESNELMINMAXEXP = new($"${nameof(SELFILLUMFRESNELMINMAXEXP)}", ShaderParamType.Vec4, "0", "Self illum fresnel min, max, exp");
	public static readonly ShaderParam ALPHATESTREFERENCE = new($"${nameof(ALPHATESTREFERENCE)}", ShaderParamType.Float, "0.0", "");
	public static readonly ShaderParam FLASHLIGHTNOLAMBERT = new($"${nameof(FLASHLIGHTNOLAMBERT)}", ShaderParamType.Bool, "0", "Flashlight pass sets N.L=1.0");
	public static readonly ShaderParam AMBIENTONLY = new($"${nameof(AMBIENTONLY)}", ShaderParamType.Integer, "0", "Control drawing of non-ambient light ()");
	public static readonly ShaderParam PHONGEXPONENT = new($"${nameof(PHONGEXPONENT)}", ShaderParamType.Float, "5.0", "Phong exponent for local specular lights");
	public static readonly ShaderParam PHONGTINT = new($"${nameof(PHONGTINT)}", ShaderParamType.Vec3, "5.0", "Phong tint for local specular lights");
	public static readonly ShaderParam PHONGALBEDOTINT = new($"${nameof(PHONGALBEDOTINT)}", ShaderParamType.Bool, "1.0", "Apply tint by albedo (controlled by spec exponent texture");
	public static readonly ShaderParam LIGHTWARPTEXTURE = new($"${nameof(LIGHTWARPTEXTURE)}", ShaderParamType.Texture, "shadertest/BaseTexture", "1D ramp texture for tinting scalar diffuse term");
	public static readonly ShaderParam PHONGWARPTEXTURE = new($"${nameof(PHONGWARPTEXTURE)}", ShaderParamType.Texture, "shadertest/BaseTexture", "warp the specular term");
	public static readonly ShaderParam PHONGFRESNELRANGES = new($"${nameof(PHONGFRESNELRANGES)}", ShaderParamType.Vec3, "[0  0.5  1]", "Parameters for remapping fresnel output");
	public static readonly ShaderParam PHONGBOOST = new($"${nameof(PHONGBOOST)}", ShaderParamType.Float, "1.0", "Phong overbrightening factor (specular mask channel should be authored to account for this)");
	public static readonly ShaderParam PHONGEXPONENTTEXTURE = new($"${nameof(PHONGEXPONENTTEXTURE)}", ShaderParamType.Texture, "shadertest/BaseTexture", "Phong Exponent map");
	public static readonly ShaderParam PHONG = new($"${nameof(PHONG)}", ShaderParamType.Bool, "0", "enables phong lighting");
	public static readonly ShaderParam BASEMAPALPHAPHONGMASK = new($"${nameof(BASEMAPALPHAPHONGMASK)}", ShaderParamType.Integer, "0", "indicates that there is no normal map and that the phong mask is in base alpha");
	public static readonly ShaderParam INVERTPHONGMASK = new($"${nameof(INVERTPHONGMASK)}", ShaderParamType.Integer, "0", "invert the phong mask (0=full phong, 1=no phong)");
	public static readonly ShaderParam ENVMAPFRESNEL = new($"${nameof(ENVMAPFRESNEL)}", ShaderParamType.Float, "0", "Degree to which Fresnel should be applied to env map");
	public static readonly ShaderParam SELFILLUMMASK = new($"${nameof(SELFILLUMMASK)}", ShaderParamType.Texture, "shadertest/BaseTexture", "If we bind a texture here, it overrides base alpha (if any) for self illum");
	public static readonly ShaderParam DETAILBLENDMODE = new($"${nameof(DETAILBLENDMODE)}", ShaderParamType.Integer, "0", "mode for combining detail texture with base. 0=normal, 1= additive, 2=alpha blend detail over base, 3=crossfade");
	public static readonly ShaderParam DETAILBLENDFACTOR = new($"${nameof(DETAILBLENDFACTOR)}", ShaderParamType.Float, "1", "blend amount for detail texture.");
	public static readonly ShaderParam DETAILTINT = new($"${nameof(DETAILTINT)}", ShaderParamType.Color, "[1 1 1]", "detail texture tint");
	public static readonly ShaderParam DETAILTEXTURETRANSFORM = new($"${nameof(DETAILTEXTURETRANSFORM)}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$detail texcoord transform");
	public static readonly ShaderParam RIMLIGHT = new($"${nameof(RIMLIGHT)}", ShaderParamType.Bool, "0", "enables rim lighting");
	public static readonly ShaderParam RIMLIGHTEXPONENT = new($"${nameof(RIMLIGHTEXPONENT)}", ShaderParamType.Float, "4.0", "Exponent for rim lights");
	public static readonly ShaderParam RIMLIGHTBOOST = new($"${nameof(RIMLIGHTBOOST)}", ShaderParamType.Float, "1.0", "Boost for rim lights");
	public static readonly ShaderParam RIMMASK = new($"${nameof(RIMMASK)}", ShaderParamType.Bool, "0", "Indicates whether or not to use alpha channel of exponent texture to mask the rim term");
	public static readonly ShaderParam SEAMLESS_BASE = new($"${nameof(SEAMLESS_BASE)}", ShaderParamType.Bool, "0", "whether to apply seamless mapping to the base texture. requires a smooth model.");
	public static readonly ShaderParam SEAMLESS_DETAIL = new($"${nameof(SEAMLESS_DETAIL)}", ShaderParamType.Bool, "0", "where to apply seamless mapping to the detail texture.");
	public static readonly ShaderParam SEAMLESS_SCALE = new($"${nameof(SEAMLESS_SCALE)}", ShaderParamType.Float, "1.0", "the scale for the seamless mapping. # of repetions of texture per inch.");
	public static readonly ShaderParam EMISSIVEBLENDENABLED = new($"${nameof(EMISSIVEBLENDENABLED)}", ShaderParamType.Bool, "0", "Enable emissive blend pass");
	public static readonly ShaderParam EMISSIVEBLENDBASETEXTURE = new($"${nameof(EMISSIVEBLENDBASETEXTURE)}", ShaderParamType.Texture, "", "self-illumination map");
	public static readonly ShaderParam EMISSIVEBLENDSCROLLVECTOR = new($"${nameof(EMISSIVEBLENDSCROLLVECTOR)}", ShaderParamType.Vec2, "[0.11 0.124]", "Emissive scroll vec");
	public static readonly ShaderParam EMISSIVEBLENDSTRENGTH = new($"${nameof(EMISSIVEBLENDSTRENGTH)}", ShaderParamType.Float, "1.0", "Emissive blend strength");
	public static readonly ShaderParam EMISSIVEBLENDTEXTURE = new($"${nameof(EMISSIVEBLENDTEXTURE)}", ShaderParamType.Texture, "", "self-illumination map");
	public static readonly ShaderParam EMISSIVEBLENDTINT = new($"${nameof(EMISSIVEBLENDTINT)}", ShaderParamType.Color, "[1 1 1]", "Self-illumination tint");
	public static readonly ShaderParam EMISSIVEBLENDFLOWTEXTURE = new($"${nameof(EMISSIVEBLENDFLOWTEXTURE)}", ShaderParamType.Texture, "", "flow map");
	public static readonly ShaderParam TIME = new($"${nameof(TIME)}", ShaderParamType.Float, "0.0", "Needs CurrentTime Proxy");
	public static readonly ShaderParam CLOAKPASSENABLED = new($"${nameof(CLOAKPASSENABLED)}", ShaderParamType.Bool, "0", "Enables cloak render in a second pass");
	public static readonly ShaderParam CLOAKFACTOR = new($"${nameof(CLOAKFACTOR)}", ShaderParamType.Float, "0.0", "");
	public static readonly ShaderParam CLOAKCOLORTINT = new($"${nameof(CLOAKCOLORTINT)}", ShaderParamType.Color, "[1 1 1]", "Cloak color tint");
	public static readonly ShaderParam REFRACTAMOUNT = new($"${nameof(REFRACTAMOUNT)}", ShaderParamType.Float, "2", "");
	public static readonly ShaderParam SHEENPASSENABLED = new($"${nameof(SHEENPASSENABLED)}", ShaderParamType.Bool, "0", "Enables weapon sheen render in a second pass");
	public static readonly ShaderParam SHEENMAP = new($"${nameof(SHEENMAP)}", ShaderParamType.Texture, "shadertest/shadertest_env", "sheenmap");
	public static readonly ShaderParam SHEENMAPMASK = new($"${nameof(SHEENMAPMASK)}", ShaderParamType.Texture, "shadertest/shadertest_envmask", "sheenmap mask");
	public static readonly ShaderParam SHEENMAPMASKFRAME = new($"${nameof(SHEENMAPMASKFRAME)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam SHEENMAPTINT = new($"${nameof(SHEENMAPTINT)}", ShaderParamType.Color, "[1 1 1]", "sheenmap tint");
	public static readonly ShaderParam SHEENMAPMASKSCALEX = new($"${nameof(SHEENMAPMASKSCALEX)}", ShaderParamType.Float, "1", "X Scale the size of the map mask to the size of the target");
	public static readonly ShaderParam SHEENMAPMASKSCALEY = new($"${nameof(SHEENMAPMASKSCALEY)}", ShaderParamType.Float, "1", "Y Scale the size of the map mask to the size of the target");
	public static readonly ShaderParam SHEENMAPMASKOFFSETX = new($"${nameof(SHEENMAPMASKOFFSETX)}", ShaderParamType.Float, "0", "X Offset of the mask relative to model space coords of target");
	public static readonly ShaderParam SHEENMAPMASKOFFSETY = new($"${nameof(SHEENMAPMASKOFFSETY)}", ShaderParamType.Float, "0", "Y Offset of the mask relative to model space coords of target");
	public static readonly ShaderParam SHEENMAPMASKDIRECTION = new($"${nameof(SHEENMAPMASKDIRECTION)}", ShaderParamType.Integer, "0", "The direction the sheen should move (length direction of weapon) XYZ, 0,1,2");
	public static readonly ShaderParam SHEENINDEX = new($"${nameof(SHEENINDEX)}", ShaderParamType.Integer, "0", "Index of the Effect Type (Color Additive, Override etc...)");
	public static readonly ShaderParam FLESHINTERIORENABLED = new($"${nameof(FLESHINTERIORENABLED)}", ShaderParamType.Bool, "0", "Enable Flesh interior blend pass");
	public static readonly ShaderParam FLESHINTERIORTEXTURE = new($"${nameof(FLESHINTERIORTEXTURE)}", ShaderParamType.Texture, "", "Flesh color texture");
	public static readonly ShaderParam FLESHINTERIORNOISETEXTURE = new($"${nameof(FLESHINTERIORNOISETEXTURE)}", ShaderParamType.Texture, "", "Flesh noise texture");
	public static readonly ShaderParam FLESHBORDERTEXTURE1D = new($"${nameof(FLESHBORDERTEXTURE1D)}", ShaderParamType.Texture, "", "Flesh border 1D texture");
	public static readonly ShaderParam FLESHNORMALTEXTURE = new($"${nameof(FLESHNORMALTEXTURE)}", ShaderParamType.Texture, "", "Flesh normal texture");
	public static readonly ShaderParam FLESHSUBSURFACETEXTURE = new($"${nameof(FLESHSUBSURFACETEXTURE)}", ShaderParamType.Texture, "", "Flesh subsurface texture");
	public static readonly ShaderParam FLESHCUBETEXTURE = new($"${nameof(FLESHCUBETEXTURE)}", ShaderParamType.Texture, "", "Flesh cubemap texture");
	public static readonly ShaderParam FLESHBORDERNOISESCALE = new($"${nameof(FLESHBORDERNOISESCALE)}", ShaderParamType.Float, "1.5", "Flesh Noise UV scalar for border");
	public static readonly ShaderParam FLESHDEBUGFORCEFLESHON = new($"${nameof(FLESHDEBUGFORCEFLESHON)}", ShaderParamType.Bool, "0", "Flesh Debug full flesh");
	public static readonly ShaderParam FLESHEFFECTCENTERRADIUS1 = new($"${nameof(FLESHEFFECTCENTERRADIUS1)}", ShaderParamType.Vec4, "[0 0 0 0.001]", "Flesh effect center and radius");
	public static readonly ShaderParam FLESHEFFECTCENTERRADIUS2 = new($"${nameof(FLESHEFFECTCENTERRADIUS2)}", ShaderParamType.Vec4, "[0 0 0 0.001]", "Flesh effect center and radius");
	public static readonly ShaderParam FLESHEFFECTCENTERRADIUS3 = new($"${nameof(FLESHEFFECTCENTERRADIUS3)}", ShaderParamType.Vec4, "[0 0 0 0.001]", "Flesh effect center and radius");
	public static readonly ShaderParam FLESHEFFECTCENTERRADIUS4 = new($"${nameof(FLESHEFFECTCENTERRADIUS4)}", ShaderParamType.Vec4, "[0 0 0 0.001]", "Flesh effect center and radius");
	public static readonly ShaderParam FLESHSUBSURFACETINT = new($"${nameof(FLESHSUBSURFACETINT)}", ShaderParamType.Color, "[1 1 1]", "Subsurface Color");
	public static readonly ShaderParam FLESHBORDERWIDTH = new($"${nameof(FLESHBORDERWIDTH)}", ShaderParamType.Float, "0.3", "Flesh border");
	public static readonly ShaderParam FLESHBORDERSOFTNESS = new($"${nameof(FLESHBORDERSOFTNESS)}", ShaderParamType.Float, "0.42", "Flesh border softness (> 0.0 && <= 0.5)");
	public static readonly ShaderParam FLESHBORDERTINT = new($"${nameof(FLESHBORDERTINT)}", ShaderParamType.Color, "[1 1 1]", "Flesh border Color");
	public static readonly ShaderParam FLESHGLOBALOPACITY = new($"${nameof(FLESHGLOBALOPACITY)}", ShaderParamType.Float, "1.0", "Flesh global opacity");
	public static readonly ShaderParam FLESHGLOSSBRIGHTNESS = new($"${nameof(FLESHGLOSSBRIGHTNESS)}", ShaderParamType.Float, "0.66", "Flesh gloss brightness");
	public static readonly ShaderParam FLESHSCROLLSPEED = new($"${nameof(FLESHSCROLLSPEED)}", ShaderParamType.Float, "1.0", "Flesh scroll speed");
	public static readonly ShaderParam SEPARATEDETAILUVS = new($"${nameof(SEPARATEDETAILUVS)}", ShaderParamType.Bool, "0", "Use texcoord1 for detail texture");
	public static readonly ShaderParam LINEARWRITE = new($"${nameof(LINEARWRITE)}", ShaderParamType.Integer, "0", "Disables SRGB conversion of shader results.");
	public static readonly ShaderParam DEPTHBLEND = new($"${nameof(DEPTHBLEND)}", ShaderParamType.Integer, "0", "fade at intersection boundaries. Only supported without bumpmaps");
	public static readonly ShaderParam DEPTHBLENDSCALE = new($"${nameof(DEPTHBLENDSCALE)}", ShaderParamType.Float, "50.0", "Amplify or reduce DEPTHBLEND fading. Lower values make harder edges.");
	public static readonly ShaderParam BLENDTINTBYBASEALPHA = new($"${nameof(BLENDTINTBYBASEALPHA)}", ShaderParamType.Bool, "0", "Use the base alpha to blend in the $color modulation");
	public static readonly ShaderParam BLENDTINTCOLOROVERBASE = new($"${nameof(BLENDTINTCOLOROVERBASE)}", ShaderParamType.Float, "0", "blend between tint acting as a multiplication versus a replace");


	protected override void OnInitShaderParams(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		VertexLitGeneric_Vars shaderVars = new();
		SetupVars(ref shaderVars);
		InitParamsVertexLitGeneric(this, parms, materialName, true, ref shaderVars);

		if (!parms[CLOAKPASSENABLED].IsDefined())
			parms[CLOAKPASSENABLED].SetIntValue(0);
		else if (parms[CLOAKPASSENABLED].GetIntValue() != 0) {
			// CloakBlendedPassVars_t info;
			// SetupVarsCloakBlendedPass(info);
			// InitParamsCloakBlendedPass(this, parms, pMaterialName, info);
		}

		if (!parms[SHEENPASSENABLED].IsDefined())
			parms[SHEENPASSENABLED].SetIntValue(0);
		else if (parms[SHEENPASSENABLED].GetIntValue() != 0) {
			// WeaponSheenPassVars_t info;
			// SetupVarsWeaponSheenPass(info);
			// InitParamsWeaponSheenPass(this, parms, pMaterialName, info);
		}

		if (!parms[EMISSIVEBLENDENABLED].IsDefined())
			parms[EMISSIVEBLENDENABLED].SetIntValue(0);
		else if (parms[EMISSIVEBLENDENABLED].GetIntValue() != 0) {
			// EmissiveScrollBlendedPassVars_t info;
			// SetupVarsEmissiveScrollBlendedPass(info);
			// InitParamsEmissiveScrollBlendedPass(this, parms, pMaterialName, info);
		}

		if (!parms[FLESHINTERIORENABLED].IsDefined())
			parms[FLESHINTERIORENABLED].SetIntValue(0);
		else if (parms[FLESHINTERIORENABLED].GetIntValue() != 0) {
			// FleshInteriorBlendedPassVars_t info;
			// SetupVarsFleshInteriorBlendedPass(info);
			// InitParamsFleshInteriorBlendedPass(this, parms, pMaterialName, info);
		}
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
		InitVertexLitGeneric(this, parms, true, ref vars);

		if (parms[CLOAKPASSENABLED].GetIntValue() != 0) {
			// CloakBlendedPassVars_t info;
			// SetupVarsCloakBlendedPass(info);
			// InitCloakBlendedPass(this, parms, info);
		}

		if (parms[SHEENPASSENABLED].GetIntValue() != 0) {
			// WeaponSheenPassVars_t info;
			// SetupVarsWeaponSheenPass(info);
			// InitWeaponSheenPass(this, parms, info);
		}

		if (parms[EMISSIVEBLENDENABLED].GetIntValue() != 0) {
			// EmissiveScrollBlendedPassVars_t info;
			// SetupVarsEmissiveScrollBlendedPass(info);
			// InitEmissiveScrollBlendedPass(this, parms, info);
		}

		if (parms[FLESHINTERIORENABLED].GetIntValue() != 0) {
			// FleshInteriorBlendedPassVars_t info;
			// SetupVarsFleshInteriorBlendedPass(info);
			// InitFleshInteriorBlendedPass(this, parms, info);
		}
	}

	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression, ref BasePerMaterialContextData? contextData) {
		bool drawStandardPass = true;
		if (vars[CLOAKPASSENABLED].GetIntValue() != 0 && ShaderShadow == null) {

		}

		if (drawStandardPass) {
			VertexLitGeneric_Vars shaderVars = new();
			SetupVars(ref shaderVars);
			DrawVertexLitGeneric(this, vars, ShaderAPI, ShaderShadow, true, ref shaderVars, vertexCompression, ref contextData);
		}
		else
			Draw(false);

		if (vars[SHEENPASSENABLED].GetIntValue() != 0) {

		}

		if (vars[CLOAKPASSENABLED].GetIntValue() != 0) {

		}

		if (vars[EMISSIVEBLENDENABLED].GetIntValue() != 0) {

		}

		if (vars[FLESHINTERIORENABLED].GetIntValue() != 0) {

		}
	}

	private void SetupVars(ref VertexLitGeneric_Vars info) {
		info.BaseTexture = (int)ShaderMaterialVars.BaseTexture;
		info.Wrinkle = COMPRESS;
		info.Stretch = STRETCH;
		info.BaseTextureFrame = (int)ShaderMaterialVars.Frame;
		info.BaseTextureTransform = (int)ShaderMaterialVars.BaseTextureTransform;
		info.Albedo = ALBEDO;
		info.SelfIllumTint = SELFILLUMTINT;
		info.Detail = DETAIL;
		info.DetailFrame = DETAILFRAME;
		info.DetailScale = DETAILSCALE;
		info.Envmap = ENVMAP;
		info.EnvmapFrame = ENVMAPFRAME;
		info.EnvmapMask = ENVMAPMASK;
		info.EnvmapMaskFrame = ENVMAPMASKFRAME;
		info.EnvmapMaskTransform = ENVMAPMASKTRANSFORM;
		info.EnvmapTint = ENVMAPTINT;
		info.Bumpmap = BUMPMAP;
		info.NormalWrinkle = BUMPCOMPRESS;
		info.NormalStretch = BUMPSTRETCH;
		info.BumpFrame = BUMPFRAME;
		info.BumpTransform = BUMPTRANSFORM;
		info.EnvmapContrast = ENVMAPCONTRAST;
		info.EnvmapSaturation = ENVMAPSATURATION;
		info.AlphaTestReference = ALPHATESTREFERENCE;
		info.FlashlightNoLambert = FLASHLIGHTNOLAMBERT;
		info.FlashlightTexture = (int)ShaderMaterialVars.FlashLightTexture;
		info.FlashlightTextureFrame = (int)ShaderMaterialVars.FlashLightTextureFrame;
		info.SelfIllumEnvMapMask_Alpha = SELFILLUM_ENVMAPMASK_ALPHA;
		info.SelfIllumFresnel = SELFILLUMFRESNEL;
		info.SelfIllumFresnelMinMaxExp = SELFILLUMFRESNELMINMAXEXP;
		info.AmbientOnly = AMBIENTONLY;
		info.PhongExponent = PHONGEXPONENT;
		info.PhongExponentTexture = PHONGEXPONENTTEXTURE;
		info.PhongTint = PHONGTINT;
		info.PhongAlbedoTint = PHONGALBEDOTINT;
		info.DiffuseWarpTexture = LIGHTWARPTEXTURE;
		info.PhongWarpTexture = PHONGWARPTEXTURE;
		info.PhongBoost = PHONGBOOST;
		info.PhongFresnelRanges = PHONGFRESNELRANGES;
		info.Phong = PHONG;
		info.BaseMapAlphaPhongMask = BASEMAPALPHAPHONGMASK;
		info.EnvmapFresnel = ENVMAPFRESNEL;
		info.DetailTextureCombineMode = DETAILBLENDMODE;
		info.DetailTextureBlendFactor = DETAILBLENDFACTOR;
		info.DetailTextureTransform = DETAILTEXTURETRANSFORM;
		info.RimLight = RIMLIGHT;
		info.RimLightPower = RIMLIGHTEXPONENT;
		info.RimLightBoost = RIMLIGHTBOOST;
		info.RimMask = RIMMASK;
		info.SeamlessScale = SEAMLESS_SCALE;
		info.SeamlessDetail = SEAMLESS_DETAIL;
		info.SeamlessBase = SEAMLESS_BASE;
		info.SeparateDetailUVs = SEPARATEDETAILUVS;
		info.LinearWrite = LINEARWRITE;
		info.DetailTint = DETAILTINT;
		info.InvertPhongMask = INVERTPHONGMASK;
		info.DepthBlend = DEPTHBLEND;
		info.DepthBlendScale = DEPTHBLENDSCALE;
		info.SelfIllumMask = SELFILLUMMASK;
		info.BlendTintByBaseAlpha = BLENDTINTBYBASEALPHA;
		info.TintReplacesBaseColor = BLENDTINTCOLOROVERBASE;
	}
}


struct VertexLitGeneric_Vars
{
	public VertexLitGeneric_Vars() => memset(MemoryMarshal.AsBytes(new Span<VertexLitGeneric_Vars>(ref this)), (byte)0xFF);

	public int BaseTexture;
	public int Wrinkle;
	public int Stretch;
	public int BaseTextureFrame;
	public int BaseTextureTransform;
	public int Albedo;
	public int Detail;
	public int DetailFrame;
	public int DetailScale;
	public int Envmap;
	public int EnvmapFrame;
	public int EnvmapMask;
	public int EnvmapMaskFrame;
	public int EnvmapMaskTransform;
	public int EnvmapTint;
	public int Bumpmap;
	public int NormalWrinkle;
	public int NormalStretch;
	public int BumpFrame;
	public int BumpTransform;
	public int EnvmapContrast;
	public int EnvmapSaturation;
	public int AlphaTestReference;
	public int VertexAlphaTest;
	public int FlashlightNoLambert;
	public int FlashlightTexture;
	public int FlashlightTextureFrame;
	public int SelfIllumTint;
	public int SelfIllumFresnel;
	public int SelfIllumFresnelMinMaxExp;
	public int PhongExponent;
	public int PhongTint;
	public int PhongAlbedoTint;
	public int PhongExponentTexture;
	public int DiffuseWarpTexture;
	public int PhongWarpTexture;
	public int PhongBoost;
	public int PhongFresnelRanges;
	public int SelfIllumEnvMapMask_Alpha;
	public int AmbientOnly;
	public int HDRColorScale;
	public int Phong;
	public int BaseMapAlphaPhongMask;
	public int EnvmapFresnel;
	public int DetailTextureCombineMode;
	public int DetailTextureBlendFactor;
	public int RimLight;
	public int RimLightPower;
	public int RimLightBoost;
	public int RimMask;
	public int SeamlessScale;
	public int SeamlessBase;
	public int SeamlessDetail;
	public int DistanceAlpha;
	public int DistanceAlphaFromDetail;
	public int SoftEdges;
	public int EdgeSoftnessStart;
	public int EdgeSoftnessEnd;
	public int ScaleEdgeSoftnessBasedOnScreenRes;
	public int Glow;
	public int GlowColor;
	public int GlowAlpha;
	public int GlowStart;
	public int GlowEnd;
	public int GlowX;
	public int GlowY;
	public int Outline;
	public int OutlineColor;
	public int OutlineAlpha;
	public int OutlineStart0;
	public int OutlineStart1;
	public int OutlineEnd0;
	public int OutlineEnd1;
	public int ScaleOutlineSoftnessBasedOnScreenRes;
	public int SeparateDetailUVs;
	public int DetailTextureTransform;
	public int LinearWrite;
	public int GammaColorRead;
	public int DetailTint;
	public int InvertPhongMask;
	public int DepthBlend;
	public int DepthBlendScale;
	public int SelfIllumMask;
	public int ReceiveFlashlight;
	public int BlendTintByBaseAlpha;
	public int TintReplacesBaseColor;
};