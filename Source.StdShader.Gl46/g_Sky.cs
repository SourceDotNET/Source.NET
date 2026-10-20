#if GMOD_DLL
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class g_sky : BaseVSShader
{
	public static string HelpString = "Help for g_sky";
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

	public static readonly ShaderParam TOPCOLOR = new($"${nameof(TOPCOLOR)}", ShaderParamType.Vec3, "[0 0 1]", "");
	public static readonly ShaderParam BOTTOMCOLOR = new($"${nameof(BOTTOMCOLOR)}", ShaderParamType.Vec3, "[0 1 1]", "");
	public static readonly ShaderParam FADEBIAS = new($"${nameof(FADEBIAS)}", ShaderParamType.Float, "1.0", "");
	public static readonly ShaderParam HDRSCALE = new($"${nameof(HDRSCALE)}", ShaderParamType.Float, "1.0", "");
	public static readonly ShaderParam SUNNORMAL = new($"${nameof(SUNNORMAL)}", ShaderParamType.Vec3, "[0 1 0.5]", "");
	public static readonly ShaderParam DUSKCOLOR = new($"${nameof(DUSKCOLOR)}", ShaderParamType.Vec3, "[1 0.4 0]", "");
	public static readonly ShaderParam DUSKSCALE = new($"${nameof(DUSKSCALE)}", ShaderParamType.Float, "1.0", "");
	public static readonly ShaderParam DUSKINTENSITY = new($"${nameof(DUSKINTENSITY)}", ShaderParamType.Float, "1.0", "");
	public static readonly ShaderParam SUNCOLOR = new($"${nameof(SUNCOLOR)}", ShaderParamType.Vec3, "[1 1 1]", "");
	public static readonly ShaderParam SUNSIZE = new($"${nameof(SUNSIZE)}", ShaderParamType.Float, "1", "");
	public static readonly ShaderParam STARTEXTURE = new($"${nameof(STARTEXTURE)}", ShaderParamType.Texture, "skybox/stars", "");
	public static readonly ShaderParam STARFADE = new($"${nameof(STARFADE)}", ShaderParamType.Float, "1", "");
	public static readonly ShaderParam STARSCALE = new($"${nameof(STARSCALE)}", ShaderParamType.Float, "1", "");
	public static readonly ShaderParam STARPOS = new($"${nameof(STARPOS)}", ShaderParamType.Float, "1", "");
	public static readonly ShaderParam STARLAYERS = new($"${nameof(STARLAYERS)}", ShaderParamType.Integer, "0", "");

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
		LoadTexture(STARTEXTURE);
	}

	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (IsSnapshotting()) {
			ShaderShadow.EnableDepthWrites(false);
			ShaderShadow.VertexShaderVertexFormat(VertexFormat.Position, 1, null, 0);
			ShaderShadow.EnableBlending(false);
			ShaderShadow.BlendFunc(ShaderBlendFactor.One, ShaderBlendFactor.One);
			if (!HardwareConfig.SupportsPixelShaders_2_b()) {
				ShaderShadow.SetPixelShader("g_sky", 0);
				ShaderShadow.SetVertexShader("g_sky", 0);
				ShaderShadow.EnableTexture(Sampler.Sampler0, false);
			}
			else {
				ShaderShadow.SetPixelShader("g_sky", vars[STARLAYERS].GetIntValue());
				ShaderShadow.SetVertexShader("g_sky", 0);
				ShaderShadow.EnableTexture(Sampler.Sampler0, true);
				ShaderShadow.EnableSRGBRead(Sampler.Sampler0, false);
			}
			ShaderShadow.EnableSRGBWrite(true);
			ShaderShadow.EnableAlphaWrites(true);
		}
		else {
			shaderAPI.SetVertexShaderIndex(0);
			shaderAPI.SetPixelShaderIndex(0);

			Span<float> v = [0, 0, 0, 0];
			shaderAPI.GetWorldSpaceCameraPosition(v);
			shaderAPI.SetPixelShaderConstant(0, v, 1);

			vars[TOPCOLOR].GetVecValue(v);
			shaderAPI.SetPixelShaderConstant(1, v, 1);
			vars[BOTTOMCOLOR].GetVecValue(v);
			shaderAPI.SetPixelShaderConstant(2, v, 1);
			shaderAPI.SetPixelShaderConstant(3, [vars[FADEBIAS].GetFloatValue(), 0, 0, 0], 1);

			if (HardwareConfig.SupportsPixelShaders_2_0()) {
				float hdrScale = vars[HDRSCALE].GetFloatValue();
				if (HardwareConfig.GetHDRType() == HDRType.None)
					hdrScale = 1.0f;
				shaderAPI.SetPixelShaderConstant(4, [hdrScale, 0, 0, 0], 1);

				vars[SUNNORMAL].GetVecValue(v);
				shaderAPI.SetPixelShaderConstant(5, v, 1);
				vars[DUSKCOLOR].GetVecValue(v);
				shaderAPI.SetPixelShaderConstant(6, v, 1);
				shaderAPI.SetPixelShaderConstant(7, [vars[DUSKSCALE].GetFloatValue(), 0, 0, 0], 1);
				shaderAPI.SetPixelShaderConstant(8, [vars[DUSKINTENSITY].GetFloatValue(), 0, 0, 0], 1);
				vars[SUNCOLOR].GetVecValue(v);
				shaderAPI.SetPixelShaderConstant(9, v, 1);
				shaderAPI.SetPixelShaderConstant(10, [vars[SUNSIZE].GetFloatValue(), 0, 0, 0], 1);
				shaderAPI.SetPixelShaderConstant(11, [vars[STARFADE].GetFloatValue(), 0, 0, 0], 1);
			}

			if (HardwareConfig.SupportsPixelShaders_2_b()) {
				float starScale = vars[STARSCALE].GetFloatValue();
				if (starScale != 0.0f)
					shaderAPI.SetPixelShaderConstant(12, [1.0f / starScale, 0, 0, 0], 1);
				shaderAPI.SetPixelShaderConstant(13, [vars[STARPOS].GetFloatValue(), 0, 0, 0], 1);
				BindTexture(Sampler.Sampler0, STARTEXTURE, -1);
			}
		}
		Draw();
	}
}
#endif
