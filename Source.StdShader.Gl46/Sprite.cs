using Source.Common;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class Sprite : BaseVSShader
{
	public static string HelpString = "Help for Sprite";
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

	public static readonly ShaderParam SPRITEORIGIN = new($"${nameof(SPRITEORIGIN)}", ShaderParamType.Vec3, "[0 0 0]", "sprite origin");
	public static readonly ShaderParam SPRITEORIENTATION = new($"${nameof(SPRITEORIENTATION)}", ShaderParamType.Integer, "0", "sprite orientation");
	public static readonly ShaderParam SPRITERENDERMODE = new($"${nameof(SPRITERENDERMODE)}", ShaderParamType.Integer, "0", "sprite rendermode");
	public static readonly ShaderParam IGNOREVERTEXCOLORS = new($"${nameof(IGNOREVERTEXCOLORS)}", ShaderParamType.Bool, "1", "ignore vertex colors");
	public static readonly ShaderParam NOSRGB = new($"${nameof(NOSRGB)}", ShaderParamType.Bool, "0", "do not operate in srgb space");
	public static readonly ShaderParam HDRCOLORSCALE = new($"${nameof(HDRCOLORSCALE)}", ShaderParamType.Float, "1.0", "hdr color scale");

	const int SPR_VP_PARALLEL_UPRIGHT = 0;
	const int SPR_FACING_UPRIGHT = 1;
	const int SPR_VP_PARALLEL = 2;
	const int SPR_ORIENTED = 3;
	const int SPR_VP_PARALLEL_ORIENTED = 4;

	const uint SHADER_USE_VERTEX_COLOR = 1;
	const uint SHADER_USE_CONSTANT_COLOR = 2;

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

	protected override void OnInitShaderParams(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		if (!parms[(int)ShaderMaterialVars.Alpha].IsDefined())
			parms[(int)ShaderMaterialVars.Alpha].SetFloatValue(1.0f);

		if (!parms[HDRCOLORSCALE].IsDefined())
			parms[HDRCOLORSCALE].SetFloatValue(1.0f);

		if (!parms[NOSRGB].IsDefined())
			parms[NOSRGB].SetIntValue(1);

		SetFlags(parms, MaterialVarFlags.NoDebugOverride);
		SetFlags(parms, MaterialVarFlags.VertexColor);
		SetFlags(parms, MaterialVarFlags.VertexAlpha);

		if (parms[SPRITEORIENTATION].IsDefined()) {
			ReadOnlySpan<char> orientationString = parms[SPRITEORIENTATION].GetStringValue();
			if (orientationString.Equals("parallel_upright", StringComparison.OrdinalIgnoreCase))
				parms[SPRITEORIENTATION].SetIntValue(SPR_VP_PARALLEL_UPRIGHT);
			else if (orientationString.Equals("facing_upright", StringComparison.OrdinalIgnoreCase))
				parms[SPRITEORIENTATION].SetIntValue(SPR_FACING_UPRIGHT);
			else if (orientationString.Equals("vp_parallel", StringComparison.OrdinalIgnoreCase))
				parms[SPRITEORIENTATION].SetIntValue(SPR_VP_PARALLEL);
			else if (orientationString.Equals("oriented", StringComparison.OrdinalIgnoreCase))
				parms[SPRITEORIENTATION].SetIntValue(SPR_ORIENTED);
			else if (orientationString.Equals("vp_parallel_oriented", StringComparison.OrdinalIgnoreCase))
				parms[SPRITEORIENTATION].SetIntValue(SPR_VP_PARALLEL_ORIENTED);
			else {
				Warning("error with $spriteOrientation\n");
				parms[SPRITEORIENTATION].SetIntValue(SPR_VP_PARALLEL_UPRIGHT);
			}
		}
		else
			parms[SPRITEORIENTATION].SetIntValue(SPR_VP_PARALLEL_UPRIGHT);
	}

	protected override void OnInitShaderInstance(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		bool srgb = parms[NOSRGB].GetIntValue() == 0;
		LoadTexture((int)ShaderMaterialVars.BaseTexture, srgb ? (int)TextureFlags.SRGB : 0);
	}

	void SetSpriteCommonShadowState(uint shaderFlags) {
		IShaderShadow shaderShadow = ShaderShadow!;
		shaderShadow.EnableTexture(Sampler.Sampler0, true);
		bool srgb = Params![NOSRGB].GetIntValue() == 0;
		shaderShadow.EnableSRGBRead(Sampler.Sampler0, srgb);

		VertexFormat flags = VertexFormat.Position;
		if ((shaderFlags & SHADER_USE_VERTEX_COLOR) != 0)
			flags |= VertexFormat.Color;
		int numTexCoords = 1;
		shaderShadow.VertexShaderVertexFormat(flags, numTexCoords, null, 0);

		StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "sprite");
		vshIndex.Set("VERTEXCOLOR", (shaderFlags & SHADER_USE_VERTEX_COLOR) != 0);
		vshIndex.Set("SRGB", srgb);
		shaderShadow.SetVertexShader("sprite", vshIndex.GetIndex());

		StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "sprite");
		pshIndex.Set("VERTEXCOLOR", (shaderFlags & SHADER_USE_VERTEX_COLOR) != 0);
		pshIndex.Set("CONSTANTCOLOR", (shaderFlags & SHADER_USE_CONSTANT_COLOR) != 0);
		pshIndex.Set("SRGB", srgb);
		shaderShadow.SetPixelShader("sprite", pshIndex.GetIndex());

		shaderShadow.EnableSRGBWrite(srgb);
	}

	void SetSpriteDynamicShaderIndices(int pixelFogCombo) {
		IShaderDynamicAPI shaderAPI = ShaderAPI!;

		MaterialFogMode fogType = shaderAPI.GetSceneFogMode();
		int fogIndex = (fogType == MaterialFogMode.LinearBelowFogZ) ? 1 : 0;
		DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
		vshIndex.Set("DOWATERFOG", fogIndex);
		shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

		DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
		pshIndex.Set("HDRENABLED", IsHDREnabled());
		pshIndex.Set("PIXELFOGTYPE", pixelFogCombo);
		shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());

		shaderAPI.SetPixelShaderFogParams((int)PixelShaderConst.FogParams);

		Span<float> eyePos_SpecExponent = [0, 0, 0, 0];
		shaderAPI.GetWorldSpaceCameraPosition(eyePos_SpecExponent);
		eyePos_SpecExponent[3] = 0.0f;
		shaderAPI.SetPixelShaderConstant((int)PixelShaderConst.EyePosSpecExponent, eyePos_SpecExponent);
	}

	void SetSpriteCommonDynamicState(uint shaderFlags) {
		bool srgb = Params![NOSRGB].GetIntValue() == 0;

		BindTexture(Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, (int)ShaderMaterialVars.Frame);

		SetSpriteDynamicShaderIndices(ShaderAPI!.GetPixelFogCombo());

		if ((shaderFlags & SHADER_USE_CONSTANT_COLOR) != 0) {
			if (srgb)
				SetPixelShaderConstantGammaToLinear(0, (int)ShaderMaterialVars.Color, (int)ShaderMaterialVars.Alpha);
			else
				SetPixelShaderConstant(0, (int)ShaderMaterialVars.Color, (int)ShaderMaterialVars.Alpha);
		}

		SetHDRColorScaleConstant(srgb);
	}

	void SetHDRColorScaleConstant(bool srgb) {
		if (IsHDREnabled()) {
			if (srgb)
				SetPixelShaderConstantGammaToLinear(1, HDRCOLORSCALE);
			else
				SetPixelShaderConstant(1, HDRCOLORSCALE);
		}
	}

	void SetFrameBlendDynamicState(ITexture texture, int frame, float fade, int pixelFogCombo) {
		bool srgb = Params![NOSRGB].GetIntValue() == 0;

		ShaderSystem.BindTexture(Sampler.Sampler0, texture, frame);

		SetSpriteDynamicShaderIndices(pixelFogCombo);

		Span<float> color = [0, 0, 0, 1.0f];
		color[0] = color[1] = color[2] = srgb ? MathLib.GammaToLinear(fade) : fade;
		ShaderAPI!.SetPixelShaderConstant(0, color);

		SetHDRColorScaleConstant(srgb);
	}

	protected override void OnDrawElements(IMaterialVar[] parms, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (IsSnapshotting())
			ShaderShadow!.EnableCulling(false);

		switch ((RenderMode)parms[SPRITERENDERMODE].GetIntValue()) {
			case RenderMode.Normal:
				if (IsSnapshotting()) {
					FogToFogColor();
					SetSpriteCommonShadowState(0);
				}
				else
					SetSpriteCommonDynamicState(0);
				Draw();
				break;

			case RenderMode.TransColor:
			case RenderMode.TransTexture:
			case RenderMode.TransAlpha:
				if (IsSnapshotting()) {
					ShaderShadow!.EnableDepthWrites(false);
					ShaderShadow.EnableBlending(true);
					ShaderShadow.BlendFunc(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.OneMinusSrcAlpha);
					FogToFogColor();
					SetSpriteCommonShadowState(SHADER_USE_VERTEX_COLOR);
				}
				else
					SetSpriteCommonDynamicState(SHADER_USE_VERTEX_COLOR);
				Draw();
				break;

			case RenderMode.Glow:
			case RenderMode.WorldGlow:
				if (IsSnapshotting()) {
					ShaderShadow!.EnableDepthWrites(false);
					ShaderShadow.EnableDepthTest(false);
					ShaderShadow.EnableBlending(true);
					ShaderShadow.BlendFunc(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.One);
					FogToBlack();
					SetSpriteCommonShadowState(SHADER_USE_VERTEX_COLOR);
				}
				else
					SetSpriteCommonDynamicState(SHADER_USE_VERTEX_COLOR);
				Draw();
				break;

			case RenderMode.TransAlphaAdd:
				if (IsSnapshotting()) {
					ShaderShadow!.EnableDepthWrites(false);
					ShaderShadow.EnableBlending(true);
					ShaderShadow.BlendFunc(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.OneMinusSrcAlpha);
					FogToFogColor();
					SetSpriteCommonShadowState(SHADER_USE_VERTEX_COLOR);
				}
				else
					SetSpriteCommonDynamicState(SHADER_USE_VERTEX_COLOR);
				Draw();
				break;

			case RenderMode.TransAdd: {
					uint flags = SHADER_USE_CONSTANT_COLOR;
					if (parms[IGNOREVERTEXCOLORS].GetIntValue() == 0)
						flags |= SHADER_USE_VERTEX_COLOR;

					if (IsSnapshotting()) {
						ShaderShadow!.EnableDepthWrites(false);
						ShaderShadow.EnableBlending(true);
						ShaderShadow.BlendFunc(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.One);
						FogToBlack();
						SetSpriteCommonShadowState(flags);
					}
					else
						SetSpriteCommonDynamicState(flags);
					Draw();
				}
				break;

			case RenderMode.TransAddFrameBlend: {
					float frame = parms[(int)ShaderMaterialVars.Frame].GetFloatValue();
					float fade = parms[(int)ShaderMaterialVars.Alpha].GetFloatValue();
					uint flags = SHADER_USE_CONSTANT_COLOR;
					if (parms[IGNOREVERTEXCOLORS].GetIntValue() == 0)
						flags |= SHADER_USE_VERTEX_COLOR;

					if (IsSnapshotting()) {
						ShaderShadow!.EnableDepthWrites(false);
						ShaderShadow.EnableBlending(true);
						ShaderShadow.BlendFunc(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.One);
						FogToBlack();
						SetSpriteCommonShadowState(flags);
						Draw();
					}
					else {
						ITexture texture = parms[(int)ShaderMaterialVars.BaseTexture].GetTextureValue()!;
						float frameBlendAlpha = 1.0f - (frame - (int)frame);
						SetFrameBlendDynamicState(texture, (int)frame, fade * frameBlendAlpha, shaderAPI.GetPixelFogCombo1(true));
						Draw();

						frameBlendAlpha = frame - (int)frame;
						int numAnimationFrames = texture.GetNumAnimationFrames();
						SetFrameBlendDynamicState(texture, ((int)frame + 1) % numAnimationFrames, fade * frameBlendAlpha, shaderAPI.GetPixelFogCombo());
						Draw();
					}
				}
				break;

			default:
				Warning("shader Sprite: Unknown sprite render mode\n");
				break;
		}
	}
}
