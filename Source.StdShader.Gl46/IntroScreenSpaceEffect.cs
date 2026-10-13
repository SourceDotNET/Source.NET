using Source.Common;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class IntroScreenSpaceEffect : BaseVSShader
{
	public static string HelpString = "Help for IntroScreenSpaceEffect";
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

	public static readonly ShaderParam MODE = new($"${nameof(MODE)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam ENABLESRGB = new($"${nameof(ENABLESRGB)}", ShaderParamType.Bool, "0", "");

	protected override void OnInitShaderParams(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		SetFlags2(parms, MaterialVarFlags2.NeedsFullFrameBufferTexture);

		if (!parms[ENABLESRGB].IsDefined())
			parms[ENABLESRGB].SetIntValue(0);
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

	protected override void OnInitShaderInstance(IMaterialVar[] parms, ReadOnlySpan<char> materialName) { }

	protected override void OnDrawElements(IMaterialVar[] parms, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (ShaderShadow != null) {
			ShaderShadow.EnableTexture(Sampler.Sampler0, true);
			ShaderShadow.EnableTexture(Sampler.Sampler1, true);

			if (parms[ENABLESRGB].GetIntValue() != 0) {
				ShaderShadow.EnableSRGBRead(Sampler.Sampler0, true);
				ShaderShadow.EnableSRGBRead(Sampler.Sampler1, true);
				ShaderShadow.EnableSRGBWrite(true);
			}

			ShaderShadow.VertexShaderVertexFormat(VertexFormat.Position, 1, null, 0);

			StaticShaderIndex vshIndex = new(ShaderShadow, ShaderType.Vertex, "screenspaceeffect");
			ShaderShadow.SetVertexShader("screenspaceeffect", vshIndex.GetIndex());

			StaticShaderIndex pshIndex = new(ShaderShadow, ShaderType.Pixel, "introscreenspaceeffect");
			ShaderShadow.SetPixelShader("introscreenspaceeffect", pshIndex.GetIndex());

			ShaderShadow.EnableBlending(true);
			ShaderShadow.BlendFunc(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.One);
		}
		else if (ShaderAPI != null) {
			ShaderAPI.BindStandardTexture(Sampler.Sampler0, StandardTextureId.FrameBufferFullTexture0);
			ShaderAPI.BindStandardTexture(Sampler.Sampler1, StandardTextureId.FrameBufferFullTexture1);

			DynamicShaderIndex vshIndex = new(ShaderAPI, ShaderType.Vertex);
			ShaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

			DynamicShaderIndex pshIndex = new(ShaderAPI, ShaderType.Pixel);
			pshIndex.Set("MODE", parms[MODE].GetIntValue());
			ShaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());

			SetPixelShaderConstant(0, (int)ShaderMaterialVars.Alpha);
		}
		Draw();
	}
}
