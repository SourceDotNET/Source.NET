using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class Sky_HDR_DX9 : BaseVSShader
{
	public static string HelpString = "Help for Sky_HDR_DX9 shader";
	public static int Flags = 0;
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

	public static readonly ShaderParam HDRBASETEXTURE = new($"${nameof(HDRBASETEXTURE)}", ShaderParamType.Texture, "", "base texture when running with HDR enabled");
	public static readonly ShaderParam HDRCOMPRESSEDTEXTURE = new($"${nameof(HDRCOMPRESSEDTEXTURE)}", ShaderParamType.Texture, "", "base texture (compressed) for hdr compression method A");
	public static readonly ShaderParam HDRCOMPRESSEDTEXTURE0 = new($"${nameof(HDRCOMPRESSEDTEXTURE0)}", ShaderParamType.Texture, "", "compressed base texture0 for hdr compression method B");
	public static readonly ShaderParam HDRCOMPRESSEDTEXTURE1 = new($"${nameof(HDRCOMPRESSEDTEXTURE1)}", ShaderParamType.Texture, "", "compressed base texture1 for hdr compression method B");
	public static readonly ShaderParam HDRCOMPRESSEDTEXTURE2 = new($"${nameof(HDRCOMPRESSEDTEXTURE2)}", ShaderParamType.Texture, "", "compressed base texture2 for hdr compression method B");

	static readonly ConVar mat_use_compressed_hdr_textures = new("mat_use_compressed_hdr_textures", "1");

	public override string? GetFallbackShader(IMaterialVar[] vars) {
		if (HardwareConfig.GetHDRType() == HDRType.None)
			return "Sky_DX9";
		return null;
	}
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
		SetFlags(vars, MaterialVarFlags.NoFog);
		SetFlags(vars, MaterialVarFlags.IgnoreZ);
	}

	protected override void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		int samplerZeroFlags = 0;
		if (vars[HDRCOMPRESSEDTEXTURE].IsDefined() && mat_use_compressed_hdr_textures.GetBool()) {
			samplerZeroFlags = 0;
		}
		else {
			if (vars[HDRCOMPRESSEDTEXTURE0].IsDefined()) {
				samplerZeroFlags = 0;
			}
			else {
				samplerZeroFlags = (int)TextureFlags.SRGB;

				if (vars[HDRBASETEXTURE].IsDefined() && vars[HDRBASETEXTURE].IsTexture()) {
					ITexture txtr = vars[HDRBASETEXTURE].GetTextureValue()!;
					ImageFormat fmt = txtr.GetImageFormat();
					if ((fmt == ImageFormat.RGBA16161616F) || (fmt == ImageFormat.RGBA16161616))
						samplerZeroFlags = 0;
				}
			}
		}

		int sampler0 = HDRCOMPRESSEDTEXTURE;
		if (vars[HDRCOMPRESSEDTEXTURE].IsDefined() && mat_use_compressed_hdr_textures.GetBool()) {
			sampler0 = HDRCOMPRESSEDTEXTURE;
		}
		else {
			if (vars[HDRCOMPRESSEDTEXTURE0].IsDefined())
				sampler0 = HDRCOMPRESSEDTEXTURE0;
			else
				sampler0 = HDRBASETEXTURE;
		}

		if (vars[HDRCOMPRESSEDTEXTURE].IsDefined() && mat_use_compressed_hdr_textures.GetBool()) {
			LoadTexture(HDRCOMPRESSEDTEXTURE, HDRCOMPRESSEDTEXTURE == sampler0 ? samplerZeroFlags : 0);
		}
		else {
			if (vars[HDRCOMPRESSEDTEXTURE0].IsDefined()) {
				LoadTexture(HDRCOMPRESSEDTEXTURE0, HDRCOMPRESSEDTEXTURE0 == sampler0 ? samplerZeroFlags : 0);
				if (vars[HDRCOMPRESSEDTEXTURE1].IsDefined())
					LoadTexture(HDRCOMPRESSEDTEXTURE1, HDRCOMPRESSEDTEXTURE1 == sampler0 ? samplerZeroFlags : 0);
				if (vars[HDRCOMPRESSEDTEXTURE2].IsDefined())
					LoadTexture(HDRCOMPRESSEDTEXTURE2, HDRCOMPRESSEDTEXTURE2 == sampler0 ? samplerZeroFlags : 0);
			}
			else {
				if (vars[HDRBASETEXTURE].IsDefined())
					LoadTexture(HDRBASETEXTURE, HDRBASETEXTURE == sampler0 ? samplerZeroFlags : 0);
			}
		}
	}

	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (IsSnapshotting()) {
			SetInitialShadowState();

			ShaderShadow.EnableTexture(Sampler.Sampler0, true);
			ShaderShadow.VertexShaderVertexFormat(VertexFormat.Position, 1, null, 0);

			StaticShaderIndex vshIndex = new(ShaderShadow, ShaderType.Vertex, "sky");
			ShaderShadow.SetVertexShader("sky", vshIndex.GetIndex());

			if (vars[HDRCOMPRESSEDTEXTURE].IsDefined() && mat_use_compressed_hdr_textures.GetBool()) {
				ShaderShadow.EnableSRGBRead(Sampler.Sampler0, false);
				StaticShaderIndex pshIndex = new(ShaderShadow, ShaderType.Pixel, "sky_hdr_compressed_rgbs");
				ShaderShadow.SetPixelShader("sky_hdr_compressed_rgbs", pshIndex.GetIndex());
			}
			else {
				if (vars[HDRCOMPRESSEDTEXTURE0].IsDefined()) {
					ShaderShadow.EnableTexture(Sampler.Sampler1, true);
					ShaderShadow.EnableTexture(Sampler.Sampler2, true);

					ShaderShadow.EnableSRGBRead(Sampler.Sampler0, false);
					ShaderShadow.EnableSRGBRead(Sampler.Sampler1, false);
					ShaderShadow.EnableSRGBRead(Sampler.Sampler2, false);
					StaticShaderIndex pshIndex = new(ShaderShadow, ShaderType.Pixel, "sky_hdr_compressed");
					ShaderShadow.SetPixelShader("sky_hdr_compressed", pshIndex.GetIndex());
				}
				else {
					ITexture txtr = vars[HDRBASETEXTURE].GetTextureValue()!;
					ImageFormat fmt = txtr.GetImageFormat();
					if ((fmt == ImageFormat.RGBA16161616F) || (fmt == ImageFormat.RGBA16161616))
						ShaderShadow.EnableSRGBRead(Sampler.Sampler0, false);
					else
						ShaderShadow.EnableSRGBRead(Sampler.Sampler0, true);

					StaticShaderIndex pshIndex = new(ShaderShadow, ShaderType.Pixel, "sky");
					ShaderShadow.SetPixelShader("sky", pshIndex.GetIndex());
				}
			}
			ShaderShadow.EnableSRGBWrite(true);

			ShaderShadow.EnableAlphaWrites(true);
		}
		else {
			DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
			shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

			SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst1, (int)ShaderMaterialVars.BaseTextureTransform);

			Span<float> c0 = [1, 1, 1, 1];
			if (vars[(int)ShaderMaterialVars.Color].IsDefined())
				vars[(int)ShaderMaterialVars.Color].GetVecValue(c0[..3]);
			if (vars[HDRCOMPRESSEDTEXTURE].IsDefined() && mat_use_compressed_hdr_textures.GetBool()) {
				ITexture txtr = vars[HDRCOMPRESSEDTEXTURE].GetTextureValue()!;
				float w = txtr.GetActualWidth();
				float h = txtr.GetActualHeight();
				float FUDGE = 0.01f / Math.Max(w, h);
				Span<float> c1 = [0.5f / w - FUDGE, 0.5f / h - FUDGE, w, h];
				shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst0, c1);

				BindTexture(Sampler.Sampler0, HDRCOMPRESSEDTEXTURE, (int)ShaderMaterialVars.Frame);
				c0[0] *= 8.0f;
				c0[1] *= 8.0f;
				c0[2] *= 8.0f;
				DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
				pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", shaderAPI.ShouldWriteDepthToDestAlpha());
				shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
			}
			else {
				Span<float> c1 = [0, 0, 0, 0];
				shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst0, c1);

				if (vars[HDRCOMPRESSEDTEXTURE0].IsDefined()) {
					BindTexture(Sampler.Sampler0, HDRCOMPRESSEDTEXTURE0, (int)ShaderMaterialVars.Frame);
					BindTexture(Sampler.Sampler1, HDRCOMPRESSEDTEXTURE1, (int)ShaderMaterialVars.Frame);
					BindTexture(Sampler.Sampler2, HDRCOMPRESSEDTEXTURE2, (int)ShaderMaterialVars.Frame);
					DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
					pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", shaderAPI.ShouldWriteDepthToDestAlpha());
					shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
				}
				else {
					BindTexture(Sampler.Sampler0, HDRBASETEXTURE, (int)ShaderMaterialVars.Frame);
					ITexture txtr = vars[HDRBASETEXTURE].GetTextureValue()!;
					ImageFormat fmt = txtr.GetImageFormat();
					if ((fmt == ImageFormat.RGBA16161616) ||
						((fmt == ImageFormat.RGBA16161616F) &&
						 (HardwareConfig.GetHDRType() == HDRType.Integer))) {
						c0[0] *= 16.0f;
						c0[1] *= 16.0f;
						c0[2] *= 16.0f;
					}
					DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
					pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", shaderAPI.ShouldWriteDepthToDestAlpha());
					shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
				}
			}
			shaderAPI.SetPixelShaderConstant(0, c0, 1);
		}
		Draw();
	}
}
