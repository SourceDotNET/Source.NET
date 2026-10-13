#if GMOD_DLL
using Source.Common.MaterialSystem;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class g_Sky : BaseVSShader
{

	public static string HelpString = "Help for g_Sky";
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

	public static readonly ShaderParam DETAIL = new($"${nameof(DETAIL)}", ShaderParamType.Texture, "shadertest/detail", "detail texture");
	public static readonly ShaderParam DETAILSCALE = new($"${nameof(DETAILSCALE)}", ShaderParamType.Float, "4", "scale of the detail texture" );
	public static readonly ShaderParam ENVMAP = new($"${nameof(ENVMAP)}", ShaderParamType.Texture, "shadertest/shadertest_env", "envmap" );
	public static readonly ShaderParam ENVMAPFRAME = new($"${nameof(ENVMAPFRAME)}", ShaderParamType.Integer, "0", "" );
	public static readonly ShaderParam ENVMAPMASK = new($"${nameof(ENVMAPMASK)}", ShaderParamType.Texture, "shadertest/shadertest_envmask", "envmap mask" );
	public static readonly ShaderParam ENVMAPMASKFRAME = new($"${nameof(ENVMAPMASKFRAME)}", ShaderParamType.Integer, "0", "" );
	public static readonly ShaderParam ENVMAPMASKSCALE = new($"${nameof(ENVMAPMASKSCALE)}", ShaderParamType.Float, "1", "envmap mask scale" );
	public static readonly ShaderParam ENVMAPTINT = new($"${nameof(ENVMAPTINT)}", ShaderParamType.Color, "[1 1 1]", "envmap tint" );
	public static readonly ShaderParam ENVMAPOPTIONAL = new($"${nameof(ENVMAPOPTIONAL)}", ShaderParamType.Bool, "0", "Make the envmap only apply to dx9 and higher hardware" );
	public static readonly ShaderParam DETAILBLENDMODE = new($"${nameof(DETAILBLENDMODE)}", ShaderParamType.Integer, "0", "mode for combining detail texture with base. 0=normal, 1= additive, 2=alpha blend detail over base, 3=crossfade" );
	public static readonly ShaderParam ALPHATESTREFERENCE = new($"${nameof(ALPHATESTREFERENCE)}", ShaderParamType.Float, "0.7", "" );
	public static readonly ShaderParam OUTLINE = new($"${nameof(OUTLINE)}", ShaderParamType.Bool, "0", "Enable outline for distance coded textures.");
	public static readonly ShaderParam OUTLINECOLOR = new($"${nameof(OUTLINECOLOR)}", ShaderParamType.Color, "[1 1 1]", "color of outline for distance coded images." );
	public static readonly ShaderParam OUTLINESTART0 = new($"${nameof(OUTLINESTART0)}", ShaderParamType.Float, "0.0", "outer start value for outline");
	public static readonly ShaderParam OUTLINESTART1 = new($"${nameof(OUTLINESTART1)}", ShaderParamType.Float, "0.0", "inner start value for outline");
	public static readonly ShaderParam OUTLINEEND0 = new($"${nameof(OUTLINEEND0)}", ShaderParamType.Float, "0.0", "inner end value for outline");
	public static readonly ShaderParam OUTLINEEND1 = new($"${nameof(OUTLINEEND1)}", ShaderParamType.Float, "0.0", "outer end value for outline");
	public static readonly ShaderParam SEPARATEDETAILUVS = new($"${nameof(SEPARATEDETAILUVS)}", ShaderParamType.Integer, "0", "" );


	protected override void OnInitShaderParams(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		InitParamsUnlitGeneric((int)ShaderMaterialVars.BaseTexture, DETAILSCALE, ENVMAPOPTIONAL, ENVMAP, ENVMAPTINT, ENVMAPMASKSCALE, DETAILBLENDMODE);
	}

	public override string? GetFallbackShader(IMaterialVar[] vars) {
		return null;
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
		InitUnlitGeneric((int)ShaderMaterialVars.BaseTexture, DETAIL, ENVMAP, ENVMAPMASK);
	}
	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (IsSnapshotting()) {
			ShaderShadow.EnableDepthTest(false);
		}
		VertexShaderUnlitGenericPass((int)ShaderMaterialVars.BaseTexture, (int)ShaderMaterialVars.Frame, (int)ShaderMaterialVars.BaseTextureTransform,
			DETAIL, DETAILSCALE, true, ENVMAP, ENVMAPFRAME, ENVMAPMASK,
			ENVMAPMASKFRAME, ENVMAPMASKSCALE, ENVMAPTINT, ALPHATESTREFERENCE,
			DETAILBLENDMODE, OUTLINE, OUTLINECOLOR, OUTLINESTART0, OUTLINEEND1, SEPARATEDETAILUVS, "unlitgeneric");
	}
}
#endif
