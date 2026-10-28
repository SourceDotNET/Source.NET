#if GMOD_DLL
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class g_colourmodify : BaseVSShader
{
	public static string HelpString = "Help for g_colourmodify";
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
	public static readonly ShaderParam pp_colour_addr = new($"${nameof(pp_colour_addr)}", ShaderParamType.Float, "0.0", "pp_colour_addr");
	public static readonly ShaderParam pp_colour_addg = new($"${nameof(pp_colour_addg)}", ShaderParamType.Float, "0.0", "pp_colour_addg");
	public static readonly ShaderParam pp_colour_addb = new($"${nameof(pp_colour_addb)}", ShaderParamType.Float, "0.0", "pp_colour_addb");
	public static readonly ShaderParam pp_colour_brightness = new($"${nameof(pp_colour_brightness)}", ShaderParamType.Float, "0.0", "pp_colour_brightness");
	public static readonly ShaderParam pp_colour_contrast = new($"${nameof(pp_colour_contrast)}", ShaderParamType.Float, "1", "pp_colour_contrast");
	public static readonly ShaderParam pp_colour_colour = new($"${nameof(pp_colour_colour)}", ShaderParamType.Float, "1", "pp_colour_colour");
	public static readonly ShaderParam pp_colour_mulr = new($"${nameof(pp_colour_mulr)}", ShaderParamType.Float, "0", "pp_colour_mulr");
	public static readonly ShaderParam pp_colour_mulg = new($"${nameof(pp_colour_mulg)}", ShaderParamType.Float, "0", "pp_colour_mulg");
	public static readonly ShaderParam pp_colour_mulb = new($"${nameof(pp_colour_mulb)}", ShaderParamType.Float, "0", "pp_colour_mulb");
	public static readonly ShaderParam pp_colour_inv = new($"${nameof(pp_colour_inv)}", ShaderParamType.Float, "0", "pp_colour_inv");

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
			ShaderShadow.SetPixelShader("g_colourmodify", 0);
			ShaderShadow.EnableSRGBRead(Sampler.Sampler0, false);
			ShaderShadow.EnableSRGBWrite(false);
			DefaultFog();
		}
		else {
			shaderAPI.SetPixelShaderConstant(0, [vars[pp_colour_addr].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(1, [vars[pp_colour_addg].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(2, [vars[pp_colour_addb].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(3, [vars[pp_colour_brightness].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(4, [vars[pp_colour_contrast].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(5, [vars[pp_colour_colour].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(6, [vars[pp_colour_mulr].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(7, [vars[pp_colour_mulg].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(8, [vars[pp_colour_mulb].GetFloatValue(), 0, 0, 0], 1);
			shaderAPI.SetPixelShaderConstant(9, [vars[pp_colour_inv].GetFloatValue(), 0, 0, 0], 1);

			BindTexture(Sampler.Sampler0, FBTEXTURE, -1);
			shaderAPI.SetVertexShaderIndex(0);
		}
		Draw();
	}
}
#endif
