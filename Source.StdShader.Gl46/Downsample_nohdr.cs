using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class Downsample_nohdr : BaseVSShader
{
	public static string HelpString = "Help for Downsample_nohdr";
	public static int Flags = (int)ShaderParamFlags.NotEditable;
	public static List<ShaderParam> ShaderParams = [];

	public class ShaderParam
	{
		public readonly ShaderParamInfo Info;
		public readonly int Index;
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

	public static readonly ShaderParam BLOOMTINTENABLE = new($"${nameof(BLOOMTINTENABLE)}", ShaderParamType.Integer, "1", "");
	public static readonly ShaderParam CSTRIKE = new($"${nameof(CSTRIKE)}", ShaderParamType.Integer, "0", "");

	static readonly ConVar r_bloomtintr = new("r_bloomtintr", "0.3");
	static readonly ConVar r_bloomtintg = new("r_bloomtintg", "0.59");
	static readonly ConVar r_bloomtintb = new("r_bloomtintb", "0.11");
	static readonly ConVar r_bloomtintexponent = new("r_bloomtintexponent", "2.2");

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

	protected override void OnInitShaderParams(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		if (!vars[BLOOMTINTENABLE].IsDefined())
			vars[BLOOMTINTENABLE].SetIntValue(1);
	}

	protected override void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		LoadTexture((int)ShaderMaterialVars.BaseTexture);
	}

	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (IsSnapshotting()) {
			ShaderShadow.EnableDepthWrites(false);
			ShaderShadow.EnableAlphaWrites(true);
			ShaderShadow.EnableTexture(Sampler.Sampler0, true);

			ShaderShadow.EnableSRGBRead(Sampler.Sampler0, false);
			ShaderShadow.EnableSRGBWrite(false);

			ShaderShadow.VertexShaderVertexFormat(VertexFormat.Position, 1, null, 0);

			ShaderShadow.SetVertexShader("downsample", 0);

			StaticShaderIndex pshIndex = new(ShaderShadow, ShaderType.Pixel, "downsample_nohdr");
			pshIndex.Set("CSTRIKE", vars[CSTRIKE].GetIntValue() != 0 ? 1 : 0);
			ShaderShadow.SetPixelShader("downsample_nohdr", pshIndex.GetIndex());
		}
		else {
			BindTexture(Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, -1);

			shaderAPI.GetBackBufferDimensions(out int width, out int height);

			Span<float> v = stackalloc float[16];
			float dX = 1.0f / width;
			float dY = 1.0f / height;

			v[0] = .5f * dX;
			v[1] = .5f * dY;
			v[4] = 2.5f * dX;
			v[5] = .5f * dY;
			v[8] = .5f * dX;
			v[9] = 2.5f * dY;
			v[12] = 2.5f * dX;
			v[13] = 2.5f * dY;
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst0, v, 4);

			shaderAPI.SetVertexShaderIndex(0);

			Span<float> pixelShaderParams = [r_bloomtintr.GetFloat(),
											 r_bloomtintg.GetFloat(),
											 r_bloomtintb.GetFloat(),
											 r_bloomtintexponent.GetFloat()];
			if (vars[BLOOMTINTENABLE].GetIntValue() == 0) {
				pixelShaderParams[0] = 0.333f;
				pixelShaderParams[1] = 0.333f;
				pixelShaderParams[2] = 0.333f;
				pixelShaderParams[3] = 1.0f;
			}
			shaderAPI.SetPixelShaderConstant(0, pixelShaderParams, 1);

			DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
			shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
		}
		Draw();
	}
}
