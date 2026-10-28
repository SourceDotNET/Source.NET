#if GMOD_DLL
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class g_sunbeams : BaseVSShader
{
	public static string HelpString = "Help for g_sunbeams";
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

	public static readonly ShaderParam FBTEXTURE = new($"${nameof(FBTEXTURE)}", ShaderParamType.Texture, "_rt_FullFrameFB", "");
	public static readonly ShaderParam darken = new($"${nameof(darken)}", ShaderParamType.Float, "0.9", "darken");
	public static readonly ShaderParam multiply = new($"${nameof(multiply)}", ShaderParamType.Float, "0.6", "multiply");
	public static readonly ShaderParam sunsize = new($"${nameof(sunsize)}", ShaderParamType.Float, "0.06", "sunsize");
	public static readonly ShaderParam sunx = new($"${nameof(sunx)}", ShaderParamType.Float, "0.5", "sunx");
	public static readonly ShaderParam suny = new($"${nameof(suny)}", ShaderParamType.Float, "0.2", "suny");

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

	protected override void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		LoadTexture(FBTEXTURE);
	}

	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (IsSnapshotting()) {
			ShaderShadow.EnableTexture(Sampler.Sampler0, true);
			ShaderShadow.EnableDepthWrites(false);
			ShaderShadow.VertexShaderVertexFormat(VertexFormat.Position, 1, null, 0);
			ShaderShadow.SetVertexShader("g_screenquad", 0);
			ShaderShadow.SetPixelShader("g_sunbeams", 0);
			DefaultFog();
		}
		else {
			shaderAPI.SetPixelShaderConstant(0, [vars[darken].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(1, [vars[multiply].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(2, [vars[sunsize].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(3, [vars[sunx].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(4, [vars[suny].GetFloatValue(), 0, 0, 0], 1);

			BindTexture(Sampler.Sampler0, FBTEXTURE, -1);
			shaderAPI.SetVertexShaderIndex(0);
		}
		Draw();
	}
}
#endif
