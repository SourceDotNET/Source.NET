using Source.Common.MaterialSystem;
using Source.Common.ShaderLib;

using System.Runtime.InteropServices;

namespace Source.StdShader.Gl46;

public class Eyes : BaseVSShader
{
	public static string HelpString = "Help for Eyes";
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

	public static readonly ShaderParam IRIS = new($"${nameof(IRIS)}", ShaderParamType.Texture, "shadertest/BaseTexture", "iris texture");
	public static readonly ShaderParam IRISFRAME = new($"${nameof(IRISFRAME)}", ShaderParamType.Integer, "0", "frame for the iris texture");
	public static readonly ShaderParam GLINT = new($"${nameof(GLINT)}", ShaderParamType.Texture, "shadertest/BaseTexture", "glint texture");
	public static readonly ShaderParam EYEORIGIN = new($"${nameof(EYEORIGIN)}", ShaderParamType.Vec3, "[0 0 0]", "origin for the eyes");
	public static readonly ShaderParam EYEUP = new($"${nameof(EYEUP)}", ShaderParamType.Vec3, "[0 0 1]", "up vector for the eyes");
	public static readonly ShaderParam IRISU = new($"${nameof(IRISU)}", ShaderParamType.Vec4, "[0 1 0 0 ]", "U projection vector for the iris");
	public static readonly ShaderParam IRISV = new($"${nameof(IRISV)}", ShaderParamType.Vec4, "[0 0 1 0]", "V projection vector for the iris");
	public static readonly ShaderParam GLINTU = new($"${nameof(GLINTU)}", ShaderParamType.Vec4, "[0 1 0 0]", "U projection vector for the glint");
	public static readonly ShaderParam GLINTV = new($"${nameof(GLINTV)}", ShaderParamType.Vec4, "[0 0 1 0]", "V projection vector for the glint");
	public static readonly ShaderParam DILATION = new($"${nameof(DILATION)}", ShaderParamType.Float, "0", "Pupil dilation (0 is none, 1 is maximal)");
	public static readonly ShaderParam INTRO = new($"${nameof(INTRO)}", ShaderParamType.Bool, "0", "is eyes in the ep1 intro");
	public static readonly ShaderParam ENTITYORIGIN = new($"${nameof(ENTITYORIGIN)}", ShaderParamType.Vec3, "0.0", "center if the model in world space");
	public static readonly ShaderParam WARPPARAM = new($"${nameof(WARPPARAM)}", ShaderParamType.Float, "0.0", "animation param between 0 and 1");

	private void SetupVars(ref Eyes_Vars info) {
		info.BaseTexture = (int)ShaderMaterialVars.BaseTexture;
		info.Frame = (int)ShaderMaterialVars.Frame;
		info.Iris = IRIS;
		info.IrisFrame = IRISFRAME;
		info.Glint = GLINT;
		info.EyeOrigin = EYEORIGIN;
		info.EyeUp = EYEUP;
		info.IrisU = IRISU;
		info.IrisV = IRISV;
		info.GlintU = GLINTU;
		info.GlintV = GLINTV;
		info.Dilation = DILATION;
		info.Intro = INTRO;
		info.EntityOrigin = ENTITYORIGIN;
		info.WarpParam = WARPPARAM;
	}

	protected override void OnInitShaderParams(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		Eyes_Vars info = new();
		SetupVars(ref info);
		InitParamsEyes(this, parms, materialName, ref info);
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
		Eyes_Vars info = new();
		SetupVars(ref info);
		InitEyes(this, parms, ref info);
	}

	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		Eyes_Vars info = new();
		SetupVars(ref info);
		DrawEyes(this, vars, ShaderAPI, ShaderShadow, ref info, vertexCompression);
	}
}

struct Eyes_Vars
{
	public Eyes_Vars() => memset(MemoryMarshal.AsBytes(new Span<Eyes_Vars>(ref this)), (byte)0xFF);

	public int BaseTexture;
	public int Frame;
	public int Iris;
	public int IrisFrame;
	public int Glint;
	public int EyeOrigin;
	public int EyeUp;
	public int IrisU;
	public int IrisV;
	public int GlintU;
	public int GlintV;
	public int Dilation;
	public int Intro;
	public int EntityOrigin;
	public int WarpParam;
}
