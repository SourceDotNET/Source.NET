using Source.Common;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class Screenspace_General : BaseVSShader
{
	public static string HelpString = "Help for screenspace_general";
	public static int Flags = (int)ShaderParamFlags.NotEditable;
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

	public static readonly ShaderParam ALPHA_BLEND = new($"${nameof(ALPHA_BLEND)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam ALPHA_BLEND_COLOR_OVERLAY = new($"${nameof(ALPHA_BLEND_COLOR_OVERLAY)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam ALPHABLEND = new($"${nameof(ALPHABLEND)}", ShaderParamType.Integer, "0", "whether or not to enable alpha blend");
	public static readonly ShaderParam ALPHATESTED = new($"${nameof(ALPHATESTED)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam BLENDOPMIN = new($"${nameof(BLENDOPMIN)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam C0_W = new($"${nameof(C0_W)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C0_X = new($"${nameof(C0_X)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C0_Y = new($"${nameof(C0_Y)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C0_Z = new($"${nameof(C0_Z)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C1_W = new($"${nameof(C1_W)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C1_X = new($"${nameof(C1_X)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C1_Y = new($"${nameof(C1_Y)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C1_Z = new($"${nameof(C1_Z)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C2_W = new($"${nameof(C2_W)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C2_X = new($"${nameof(C2_X)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C2_Y = new($"${nameof(C2_Y)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C2_Z = new($"${nameof(C2_Z)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C3_W = new($"${nameof(C3_W)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C3_X = new($"${nameof(C3_X)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C3_Y = new($"${nameof(C3_Y)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam C3_Z = new($"${nameof(C3_Z)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam COPYALPHA = new($"${nameof(COPYALPHA)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam CULL = new($"${nameof(CULL)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam DEPTHTEST = new($"${nameof(DEPTHTEST)}", ShaderParamType.Integer, "0", "Enable Depthtest");
	public static readonly ShaderParam DISABLE_COLOR_WRITES = new($"${nameof(DISABLE_COLOR_WRITES)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam INVVIEWPROJMAT = new($"${nameof(INVVIEWPROJMAT)}", ShaderParamType.Matrix, "0", "");
	public static readonly ShaderParam LINEARREAD_BASETEXTURE = new($"${nameof(LINEARREAD_BASETEXTURE)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam LINEARREAD_TEXTURE1 = new($"${nameof(LINEARREAD_TEXTURE1)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam LINEARREAD_TEXTURE2 = new($"${nameof(LINEARREAD_TEXTURE2)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam LINEARREAD_TEXTURE3 = new($"${nameof(LINEARREAD_TEXTURE3)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam LINEARWRITE = new($"${nameof(LINEARWRITE)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam MULTIPLYCOLOR = new($"${nameof(MULTIPLYCOLOR)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam PIXSHADER = new($"${nameof(PIXSHADER)}", ShaderParamType.String, "", "");
	public static readonly ShaderParam POINTSAMPLE_BASETEXTURE = new($"${nameof(POINTSAMPLE_BASETEXTURE)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam POINTSAMPLE_TEXTURE1 = new($"${nameof(POINTSAMPLE_TEXTURE1)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam POINTSAMPLE_TEXTURE2 = new($"${nameof(POINTSAMPLE_TEXTURE2)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam POINTSAMPLE_TEXTURE3 = new($"${nameof(POINTSAMPLE_TEXTURE3)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam TCSIZE0 = new($"${nameof(TCSIZE0)}", ShaderParamType.Integer, "2", "");
	public static readonly ShaderParam TCSIZE1 = new($"${nameof(TCSIZE1)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam TCSIZE2 = new($"${nameof(TCSIZE2)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam TCSIZE3 = new($"${nameof(TCSIZE3)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam TCSIZE4 = new($"${nameof(TCSIZE4)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam TCSIZE5 = new($"${nameof(TCSIZE5)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam TCSIZE6 = new($"${nameof(TCSIZE6)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam TCSIZE7 = new($"${nameof(TCSIZE7)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam TEXTURE1 = new($"${nameof(TEXTURE1)}", ShaderParamType.Texture, "", "");
	public static readonly ShaderParam TEXTURE2 = new($"${nameof(TEXTURE2)}", ShaderParamType.Texture, "", "");
	public static readonly ShaderParam TEXTURE3 = new($"${nameof(TEXTURE3)}", ShaderParamType.Texture, "", "");
	public static readonly ShaderParam VERTEXNORMAL = new($"${nameof(VERTEXNORMAL)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam VERTEXSHADER = new($"${nameof(VERTEXSHADER)}", ShaderParamType.String, "", "");
	public static readonly ShaderParam VERTEXTRANSFORM = new($"${nameof(VERTEXTRANSFORM)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam VIEWPROJMAT = new($"${nameof(VIEWPROJMAT)}", ShaderParamType.Matrix, "0", "");
	public static readonly ShaderParam WRITEALPHA = new($"${nameof(WRITEALPHA)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam WRITEDEPTH = new($"${nameof(WRITEDEPTH)}", ShaderParamType.Integer, "0", "whether or not to enable depth write");

	static readonly ShaderParam[] TCSizes = [TCSIZE0, TCSIZE1, TCSIZE2, TCSIZE3, TCSIZE4, TCSIZE5, TCSIZE6, TCSIZE7];

	protected override void OnInitShaderParams(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		if (!parms[TCSIZE0].IsDefined())
			parms[TCSIZE0].SetIntValue(2);
	}

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

	protected override void OnInitShaderInstance(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		if (parms[(int)ShaderMaterialVars.BaseTexture].IsDefined())
			LoadTexture((int)ShaderMaterialVars.BaseTexture);
		if (parms[TEXTURE1].IsDefined())
			LoadTexture(TEXTURE1);
		if (parms[TEXTURE2].IsDefined())
			LoadTexture(TEXTURE2);
		if (parms[TEXTURE3].IsDefined())
			LoadTexture(TEXTURE3);
	}

	void EnableTextureSRGBRead(IMaterialVar[] parms, Sampler sampler, int textureVar, int linearReadVar) {
		if (!parms[textureVar].IsDefined())
			return;
		ShaderShadow!.EnableTexture(sampler, true);
		ShaderShadow.EnableSRGBRead(sampler, !parms[linearReadVar].IsDefined() || parms[linearReadVar].GetIntValue() == 0);
	}

	void SetPixelSize(IMaterialVar[] parms, Sampler sampler, int textureVar, int register) {
		if (!parms[textureVar].IsDefined())
			return;
		BindTexture(sampler, textureVar, -1);

		ITexture target = parms[textureVar].GetTextureValue()!;
		Span<float> pixelSize = [1.0f / target.GetActualWidth(), 1.0f / target.GetActualHeight(), 0.0f, 0.0f];
		ShaderAPI!.SetPixelShaderConstant(register, pixelSize, 1);
	}

	protected override void OnDrawElements(IMaterialVar[] parms, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		bool customVertexShader = parms[VERTEXSHADER].IsDefined();
		if (ShaderShadow != null) {
			ShaderShadow.EnableDepthWrites(parms[WRITEDEPTH].GetIntValue() != 0);
			if (parms[WRITEDEPTH].GetIntValue() != 0) {
				ShaderShadow.EnableDepthTest(true);
				ShaderShadow.DepthFunc(ShaderDepthFunc.Always);
			}
			ShaderShadow.EnableAlphaWrites(parms[WRITEALPHA].GetIntValue() != 0);
			ShaderShadow.EnableDepthTest(parms[DEPTHTEST].GetIntValue() != 0);
			ShaderShadow.EnableCulling(parms[CULL].GetIntValue() != 0);

			EnableTextureSRGBRead(parms, Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, LINEARREAD_BASETEXTURE);
			EnableTextureSRGBRead(parms, Sampler.Sampler1, TEXTURE1, LINEARREAD_TEXTURE1);
			EnableTextureSRGBRead(parms, Sampler.Sampler2, TEXTURE2, LINEARREAD_TEXTURE2);
			EnableTextureSRGBRead(parms, Sampler.Sampler3, TEXTURE3, LINEARREAD_TEXTURE3);

			int vertexColor = IsFlagSet(parms, MaterialVarFlags.VertexColor) ? 1 : 0;
			VertexFormat fmt = VertexFormat.Position;
			if (vertexColor != 0)
				fmt |= VertexFormat.Color;
			if (parms[VERTEXNORMAL].GetIntValue() != 0)
				fmt |= VertexFormat.Normal;

			Span<int> texCoordDimensions = stackalloc int[8];
			int texCoordCount = 0;
			while (texCoordCount < 8 && parms[TCSizes[texCoordCount]].GetIntValue() != 0) {
				texCoordDimensions[texCoordCount] = parms[TCSizes[texCoordCount]].GetIntValue();
				texCoordCount++;
			}
			ShaderShadow.VertexShaderVertexFormat(fmt, texCoordCount, texCoordDimensions[..texCoordCount], 0);

			if (IsFlagSet(parms, MaterialVarFlags.Additive))
				EnableAlphaBlending(ShaderBlendFactor.One, ShaderBlendFactor.One);
			else if (parms[MULTIPLYCOLOR].GetIntValue() != 0)
				EnableAlphaBlending(ShaderBlendFactor.Zero, ShaderBlendFactor.SrcColor);
			else if (parms[ALPHABLEND].GetIntValue() != 0)
				EnableAlphaBlending(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.OneMinusSrcAlpha);
			else
				ShaderShadow.EnableBlending(false);

			if (parms[BLENDOPMIN].GetIntValue() != 0) {
				ShaderShadow.BlendOp(ShaderBlendOp.Min);
				ShaderShadow.EnableBlending(true);
			}

			ShaderShadow.EnableSRGBWrite(parms[LINEARWRITE].GetIntValue() == 0);

			if (!customVertexShader)
				ShaderShadow.SetVertexShader("screenspaceeffect", vertexColor + parms[VERTEXTRANSFORM].GetIntValue() * 2);
			else
				ShaderShadow.SetVertexShader(parms[VERTEXSHADER].GetStringValue(), 0);

			if (parms[DISABLE_COLOR_WRITES].GetIntValue() != 0)
				ShaderShadow.EnableColorWrites(false);

			ShaderShadow.EnableAlphaTest(true);
			ShaderShadow.AlphaFunc(ShaderAlphaFunc.Greater, 0.0f);

			string pixelShader = parms[PIXSHADER].GetStringValue();
			if (HardwareConfig.SupportsPixelShaders_2_b() && pixelShader.Length > 6 && (pixelShader.EndsWith("_ps20", StringComparison.OrdinalIgnoreCase) || pixelShader.EndsWith("_vs20", StringComparison.OrdinalIgnoreCase)))
				pixelShader += "b";
			ShaderShadow.SetPixelShader(pixelShader, 0);

			if (parms[ALPHA_BLEND_COLOR_OVERLAY].GetIntValue() != 0)
				EnableAlphaBlending(ShaderBlendFactor.One, ShaderBlendFactor.OneMinusSrcAlpha);
			if (parms[ALPHA_BLEND].GetIntValue() != 0)
				EnableAlphaBlending(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.OneMinusSrcAlpha);
			if (parms[COPYALPHA].GetIntValue() != 0) {
				ShaderShadow.EnableBlending(false);
				ShaderShadow.AlphaFunc(ShaderAlphaFunc.Always, 0.0f);
			}
		}
		if (ShaderAPI != null) {
			SetPixelSize(parms, Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, 4);
			SetPixelSize(parms, Sampler.Sampler1, TEXTURE1, 5);
			SetPixelSize(parms, Sampler.Sampler2, TEXTURE2, 6);
			SetPixelSize(parms, Sampler.Sampler3, TEXTURE3, 7);

			Span<float> c0 = [
				parms[C0_X].GetFloatValue(), parms[C0_Y].GetFloatValue(), parms[C0_Z].GetFloatValue(), parms[C0_W].GetFloatValue(),
				parms[C1_X].GetFloatValue(), parms[C1_Y].GetFloatValue(), parms[C1_Z].GetFloatValue(), parms[C1_W].GetFloatValue(),
				parms[C2_X].GetFloatValue(), parms[C2_Y].GetFloatValue(), parms[C2_Z].GetFloatValue(), parms[C2_W].GetFloatValue(),
				parms[C3_X].GetFloatValue(), parms[C3_Y].GetFloatValue(), parms[C3_Z].GetFloatValue(), parms[C3_W].GetFloatValue()
			];
			ShaderAPI.SetPixelShaderConstant(0, c0, 4);

			Span<float> eyePos = stackalloc float[4];
			ShaderAPI.GetWorldSpaceCameraPosition(eyePos);
			ShaderAPI.SetPixelShaderConstant(10, eyePos, 1);

			if (parms[VIEWPROJMAT].IsDefined()) {
				System.Numerics.Matrix4x4 viewProj = parms[VIEWPROJMAT].GetMatrixValue();
				ShaderAPI.SetPixelShaderConstant(11, System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref viewProj.M11, 16), 4);
			}
			if (parms[INVVIEWPROJMAT].IsDefined()) {
				System.Numerics.Matrix4x4 invViewProj = parms[INVVIEWPROJMAT].GetMatrixValue();
				ShaderAPI.SetPixelShaderConstant(15, System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref invViewProj.M11, 16), 4);
			}

			ShaderAPI.SetVertexShaderIndex(0);
			ShaderAPI.SetPixelShaderIndex(0);
			if (!customVertexShader)
				ShaderAPI.SetVertexShaderIndex(0);
		}
		Draw();
	}
}
