using Source.Common;
using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class Water_DX90 : BaseVSShader
{
	public static string HelpString = "Help for Water";
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

	public static readonly ShaderParam REFRACTTEXTURE = new($"${nameof(REFRACTTEXTURE)}", ShaderParamType.Texture, "_rt_WaterRefraction", "");
	public static readonly ShaderParam REFLECTTEXTURE = new($"${nameof(REFLECTTEXTURE)}", ShaderParamType.Texture, "_rt_WaterReflection", "");
	public static readonly ShaderParam REFRACTAMOUNT = new($"${nameof(REFRACTAMOUNT)}", ShaderParamType.Float, "0", "");
	public static readonly ShaderParam REFRACTTINT = new($"${nameof(REFRACTTINT)}", ShaderParamType.Color, "[1 1 1]", "refraction tint");
	public static readonly ShaderParam REFLECTAMOUNT = new($"${nameof(REFLECTAMOUNT)}", ShaderParamType.Float, "0.8", "");
	public static readonly ShaderParam REFLECTTINT = new($"${nameof(REFLECTTINT)}", ShaderParamType.Color, "[1 1 1]", "reflection tint");
	public static readonly ShaderParam NORMALMAP = new($"${nameof(NORMALMAP)}", ShaderParamType.Texture, "dev/water_normal", "normal map");
	public static readonly ShaderParam BUMPFRAME = new($"${nameof(BUMPFRAME)}", ShaderParamType.Integer, "0", "frame number for $bumpmap");
	public static readonly ShaderParam BUMPTRANSFORM = new($"${nameof(BUMPTRANSFORM)}", ShaderParamType.Matrix, "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$bumpmap texcoord transform");
	public static readonly ShaderParam SCALE = new($"${nameof(SCALE)}", ShaderParamType.Vec2, "[1 1]", "");
	public static readonly ShaderParam TIME = new($"${nameof(TIME)}", ShaderParamType.Float, "", "");
	public static readonly ShaderParam WATERDEPTH = new($"${nameof(WATERDEPTH)}", ShaderParamType.Float, "", "");
	public static readonly ShaderParam CHEAPWATERSTARTDISTANCE = new($"${nameof(CHEAPWATERSTARTDISTANCE)}", ShaderParamType.Float, "", "This is the distance from the eye in inches that the shader should start transitioning to a cheaper water shader.");
	public static readonly ShaderParam CHEAPWATERENDDISTANCE = new($"${nameof(CHEAPWATERENDDISTANCE)}", ShaderParamType.Float, "", "This is the distance from the eye in inches that the shader should finish transitioning to a cheaper water shader.");
	public static readonly ShaderParam ENVMAP = new($"${nameof(ENVMAP)}", ShaderParamType.Texture, "env_cubemap", "envmap");
	public static readonly ShaderParam ENVMAPFRAME = new($"${nameof(ENVMAPFRAME)}", ShaderParamType.Integer, "0", "");
	public static readonly ShaderParam FOGCOLOR = new($"${nameof(FOGCOLOR)}", ShaderParamType.Color, "", "");
	public static readonly ShaderParam FORCECHEAP = new($"${nameof(FORCECHEAP)}", ShaderParamType.Bool, "", "");
	public static readonly ShaderParam FORCEEXPENSIVE = new($"${nameof(FORCEEXPENSIVE)}", ShaderParamType.Bool, "", "");
	public static readonly ShaderParam REFLECTENTITIES = new($"${nameof(REFLECTENTITIES)}", ShaderParamType.Bool, "", "");
	public static readonly ShaderParam FOGSTART = new($"${nameof(FOGSTART)}", ShaderParamType.Float, "", "");
	public static readonly ShaderParam FOGEND = new($"${nameof(FOGEND)}", ShaderParamType.Float, "", "");
	public static readonly ShaderParam ABOVEWATER = new($"${nameof(ABOVEWATER)}", ShaderParamType.Bool, "", "");
	public static readonly ShaderParam REFLECTBLENDFACTOR = new($"${nameof(REFLECTBLENDFACTOR)}", ShaderParamType.Float, "1.0", "");
	public static readonly ShaderParam NOFRESNEL = new($"${nameof(NOFRESNEL)}", ShaderParamType.Bool, "0", "");
	public static readonly ShaderParam NOLOWENDLIGHTMAP = new($"${nameof(NOLOWENDLIGHTMAP)}", ShaderParamType.Bool, "0", "");
	public static readonly ShaderParam SCROLL1 = new($"${nameof(SCROLL1)}", ShaderParamType.Color, "", "");
	public static readonly ShaderParam SCROLL2 = new($"${nameof(SCROLL2)}", ShaderParamType.Color, "", "");
	public static readonly ShaderParam BLURREFRACT = new($"${nameof(BLURREFRACT)}", ShaderParamType.Bool, "0", "Cause the refraction to be blurry on ps2b hardware");

	static readonly ConVar r_waterforceexpensive = new("r_waterforceexpensive", "0", FCvar.Archive);

	const float PSHADER_VECT_SCALE = 1.0f;
	const float VSHADER_VECT_SCALE = 1.0f;

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
		if (!vars[ABOVEWATER].IsDefined()) {
			Warning($"***need to set $abovewater for material {materialName}\n");
			vars[ABOVEWATER].SetIntValue(1);
		}
		SetFlags2(vars, MaterialVarFlags2.NeedsTangentSpaces);
		if (!vars[CHEAPWATERSTARTDISTANCE].IsDefined())
			vars[CHEAPWATERSTARTDISTANCE].SetFloatValue(500.0f);
		if (!vars[CHEAPWATERENDDISTANCE].IsDefined())
			vars[CHEAPWATERENDDISTANCE].SetFloatValue(1000.0f);
		if (!vars[SCALE].IsDefined())
			vars[SCALE].SetVecValue(1.0f, 1.0f);
		if (!vars[SCROLL1].IsDefined())
			vars[SCROLL1].SetVecValue(0.0f, 0.0f, 0.0f);
		if (!vars[SCROLL2].IsDefined())
			vars[SCROLL2].SetVecValue(0.0f, 0.0f, 0.0f);
		if (!vars[FOGCOLOR].IsDefined()) {
			vars[FOGCOLOR].SetVecValue(1.0f, 0.0f, 0.0f);
			Warning($"material {materialName} needs to have a $fogcolor.\n");
		}
		if (!vars[REFLECTENTITIES].IsDefined())
			vars[REFLECTENTITIES].SetIntValue(0);
		if (!vars[REFLECTBLENDFACTOR].IsDefined())
			vars[REFLECTBLENDFACTOR].SetFloatValue(1.0f);

		if (!vars[FORCEEXPENSIVE].IsDefined())
			vars[FORCEEXPENSIVE].SetIntValue(1);
		if (vars[FORCEEXPENSIVE].GetIntValue() != 0 && vars[FORCECHEAP].GetIntValue() != 0)
			vars[FORCEEXPENSIVE].SetIntValue(0);

		if (vars[NOLOWENDLIGHTMAP].GetIntValue() == 0)
			SetFlags2(vars, MaterialVarFlags2.LightingLightmap);

		SetFlags2(vars, MaterialVarFlags2.LightingLightmap);
		if (Config.UseBumpmapping() && vars[NORMALMAP].IsDefined())
			SetFlags2(vars, MaterialVarFlags2.LightingBumpedLightmap);
	}

	protected override void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		Assert(vars[WATERDEPTH].IsDefined());

		if (vars[REFRACTTEXTURE].IsDefined())
			LoadTexture(REFRACTTEXTURE, (int)TextureFlags.SRGB);
		if (vars[REFLECTTEXTURE].IsDefined())
			LoadTexture(REFLECTTEXTURE, (int)TextureFlags.SRGB);
		if (vars[ENVMAP].IsDefined())
			LoadCubeMap(ENVMAP, (int)TextureFlags.SRGB);
		if (vars[NORMALMAP].IsDefined())
			LoadBumpMap(NORMALMAP);
		if (vars[(int)ShaderMaterialVars.BaseTexture].IsDefined())
			LoadTexture((int)ShaderMaterialVars.BaseTexture, (int)TextureFlags.SRGB);
	}

	void DrawReflectionRefraction(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, bool reflection, bool refraction) {
		if (IsSnapshotting()) {
			SetInitialShadowState();
			if (refraction) {
				ShaderShadow.EnableTexture(Sampler.Sampler0, true);
				ShaderShadow.EnableTexture(Sampler.Sampler1, true);
				ShaderShadow.EnableSRGBRead(Sampler.Sampler0, true);
			}
			if (reflection) {
				ShaderShadow.EnableTexture(Sampler.Sampler2, true);
				ShaderShadow.EnableTexture(Sampler.Sampler3, true);
				ShaderShadow.EnableSRGBRead(Sampler.Sampler2, true);
				if (vars[(int)ShaderMaterialVars.BaseTexture].IsTexture()) {
					ShaderShadow.EnableTexture(Sampler.Sampler1, true);
					ShaderShadow.EnableSRGBRead(Sampler.Sampler1, true);
					ShaderShadow.EnableTexture(Sampler.Sampler3, true);
					ShaderShadow.EnableSRGBRead(Sampler.Sampler3, true);
				}
			}
			ShaderShadow.EnableTexture(Sampler.Sampler4, true);
			ShaderShadow.EnableTexture(Sampler.Sampler5, true);

			VertexFormat fmt = VertexFormat.Position | VertexFormat.Normal | VertexFormat.TangentS | VertexFormat.TangentT;

			int numTexCoords = 1;
			if (vars[(int)ShaderMaterialVars.BaseTexture].IsTexture())
				numTexCoords = 3;
			ShaderShadow.VertexShaderVertexFormat(fmt, numTexCoords, null, 0);

			Span<float> scroll1 = stackalloc float[4];
			vars[SCROLL1].GetVecValue(scroll1);

			StaticShaderIndex vshIndex = new(ShaderShadow, ShaderType.Vertex, "water");
			vshIndex.Set("MULTITEXTURE", MathF.Abs(scroll1[0]) > 0.0f);
			vshIndex.Set("BASETEXTURE", vars[(int)ShaderMaterialVars.BaseTexture].IsTexture());
			ShaderShadow.SetVertexShader("water", vshIndex.GetIndex());

			StaticShaderIndex pshIndex = new(ShaderShadow, ShaderType.Pixel, "water");
			pshIndex.Set("REFLECT", reflection);
			pshIndex.Set("REFRACT", refraction);
			pshIndex.Set("ABOVEWATER", vars[ABOVEWATER].GetIntValue());
			pshIndex.Set("MULTITEXTURE", MathF.Abs(scroll1[0]) > 0.0f);
			pshIndex.Set("BASETEXTURE", vars[(int)ShaderMaterialVars.BaseTexture].IsTexture());
			pshIndex.Set("BLURRY_REFRACT", vars[BLURREFRACT].GetIntValue());
			ShaderShadow.SetPixelShader("water", pshIndex.GetIndex());

			FogToFogColor();

			ShaderShadow.EnableSRGBWrite(true);

			ShaderShadow.EnableAlphaWrites(true);
		}
		else {
			shaderAPI.SetDefaultState();
			if (refraction)
				BindTexture(Sampler.Sampler0, REFRACTTEXTURE, -1);
			if (reflection)
				BindTexture(Sampler.Sampler2, REFLECTTEXTURE, -1);
			BindTexture(Sampler.Sampler4, NORMALMAP, BUMPFRAME);
			if (vars[(int)ShaderMaterialVars.BaseTexture].IsTexture()) {
				BindTexture(Sampler.Sampler1, (int)ShaderMaterialVars.BaseTexture, (int)ShaderMaterialVars.Frame);
				shaderAPI.BindStandardTexture(Sampler.Sampler3, StandardTextureId.Lightmap);
			}

			shaderAPI.BindStandardTexture(Sampler.Sampler5, StandardTextureId.NormalizationCubemapSigned);

			if (refraction)
				SetPixelShaderConstantGammaToLinear(1, REFRACTTINT);
			if (reflection) {
				if (HardwareConfig.GetHDRType() == HDRType.Integer) {
					Span<float> gammaReflectTint = stackalloc float[3];
					vars[REFLECTTINT].GetVecValue(gammaReflectTint);
					Span<float> linearReflectTint = [
						MathLib.GammaToLinear(gammaReflectTint[0]) * 4.0f,
						MathLib.GammaToLinear(gammaReflectTint[1]) * 4.0f,
						MathLib.GammaToLinear(gammaReflectTint[2]) * 4.0f,
						1.0f
					];
					shaderAPI.SetPixelShaderConstant(4, linearReflectTint, 1);
				}
				else
					SetPixelShaderConstantGammaToLinear(4, REFLECTTINT);
			}

			SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst1, BUMPTRANSFORM);

			float curtime = (float)shaderAPI.CurrentTime();
			Span<float> vc0 = stackalloc float[4];
			Span<float> v0 = stackalloc float[4];
			vars[SCROLL1].GetVecValue(v0);
			vc0[0] = curtime * v0[0];
			vc0[1] = curtime * v0[1];
			vars[SCROLL2].GetVecValue(v0);
			vc0[2] = curtime * v0[0];
			vc0[3] = curtime * v0[1];
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst3, vc0, 1);

			Span<float> c0 = [1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f, 0.0f];
			shaderAPI.SetPixelShaderConstant(0, c0, 1);

			Span<float> c2 = [0.5f, 0.5f, 0.5f, 0.5f];
			shaderAPI.SetPixelShaderConstant(2, c2, 1);

			Span<float> c3 = [1.0f, 0.0f, 0.0f, 0.0f];
			shaderAPI.SetPixelShaderConstant(3, c3, 1);

			Span<float> c5 = [vars[REFLECTAMOUNT].GetFloatValue(), vars[REFLECTAMOUNT].GetFloatValue(),
				vars[REFRACTAMOUNT].GetFloatValue(), vars[REFRACTAMOUNT].GetFloatValue()];
			shaderAPI.SetPixelShaderConstant(5, c5, 1);

			SetPixelShaderConstantGammaToLinear(6, FOGCOLOR);

			Span<float> c7 = [
				vars[FOGSTART].GetFloatValue(),
				vars[FOGEND].GetFloatValue() - vars[FOGSTART].GetFloatValue(),
				1.0f,
				0.0f
			];
			if (HardwareConfig.GetHDRType() == HDRType.Integer)
				c7[2] = 4.0f;
			shaderAPI.SetPixelShaderConstant(7, c7, 1);

			shaderAPI.SetPixelShaderFogParams(8);

			DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
			shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

			DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
			pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo());
			pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", shaderAPI.ShouldWriteDepthToDestAlpha());
			shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
		}
		Draw();
	}

	void DrawCheapWater(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, bool blend, bool refraction) {
		if (IsSnapshotting()) {
			SetInitialShadowState();

			if (UsingEditor(vars))
				ShaderShadow.EnableCulling(false);

			if (blend)
				EnableAlphaBlending(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.OneMinusSrcAlpha);
			ShaderShadow.EnableTexture(Sampler.Sampler0, true);
			ShaderShadow.EnableTexture(Sampler.Sampler1, true);
			if (refraction && blend)
				ShaderShadow.EnableTexture(Sampler.Sampler2, true);
			ShaderShadow.EnableTexture(Sampler.Sampler6, true);
			VertexFormat fmt = VertexFormat.Position | VertexFormat.Normal | VertexFormat.TangentS | VertexFormat.TangentT;
			ShaderShadow.VertexShaderVertexFormat(fmt, 1, null, 0);

			StaticShaderIndex vshIndex = new(ShaderShadow, ShaderType.Vertex, "watercheap");
			vshIndex.Set("BLEND", blend && refraction);
			ShaderShadow.SetVertexShader("watercheap", vshIndex.GetIndex());

			Span<float> scroll1 = stackalloc float[4];
			vars[SCROLL1].GetVecValue(scroll1);

			StaticShaderIndex pshIndex = new(ShaderShadow, ShaderType.Pixel, "watercheap");
			pshIndex.Set("FRESNEL", vars[NOFRESNEL].GetIntValue() == 0);
			pshIndex.Set("BLEND", blend);
			pshIndex.Set("REFRACTALPHA", refraction);
			pshIndex.Set("HDRTYPE", (int)HardwareConfig.GetHDRType());
			pshIndex.Set("MULTITEXTURE", MathF.Abs(scroll1[0]) > 0.0f);
			ShaderShadow.SetPixelShader("watercheap", pshIndex.GetIndex());

			if (HardwareConfig.GetHDRType() != HDRType.None)
				ShaderShadow.EnableSRGBWrite(true);

			FogToFogColor();
		}
		else {
			shaderAPI.SetDefaultState();

			BindTexture(Sampler.Sampler0, ENVMAP, ENVMAPFRAME);
			BindTexture(Sampler.Sampler1, NORMALMAP, BUMPFRAME);
			if (refraction && blend)
				BindTexture(Sampler.Sampler2, REFRACTTEXTURE, -1);
			shaderAPI.BindStandardTexture(Sampler.Sampler6, StandardTextureId.NormalizationCubemapSigned);

			SetPixelShaderConstant(0, FOGCOLOR);

			float cheapWaterStartDistance = vars[CHEAPWATERSTARTDISTANCE].GetFloatValue();
			float cheapWaterEndDistance = vars[CHEAPWATERENDDISTANCE].GetFloatValue();
			Span<float> cheapWaterParams = [
				cheapWaterStartDistance * VSHADER_VECT_SCALE,
				cheapWaterEndDistance * VSHADER_VECT_SCALE,
				PSHADER_VECT_SCALE / (cheapWaterEndDistance - cheapWaterStartDistance),
				cheapWaterStartDistance / (cheapWaterEndDistance - cheapWaterStartDistance),
			];
			shaderAPI.SetPixelShaderConstant(1, cheapWaterParams);

			if (Config.ShowSpecular)
				SetPixelShaderConstant(2, REFLECTTINT, REFLECTBLENDFACTOR);
			else {
				Span<float> zero = [0.0f, 0.0f, 0.0f, vars[REFLECTBLENDFACTOR].GetFloatValue()];
				shaderAPI.SetPixelShaderConstant(2, zero);
			}

			shaderAPI.SetPixelShaderFogParams(3);

			if (vars[SCROLL1].IsDefined()) {
				float curtime = (float)shaderAPI.CurrentTime();
				Span<float> vc0 = stackalloc float[4];
				Span<float> v0 = stackalloc float[4];
				vars[SCROLL1].GetVecValue(v0);
				vc0[0] = curtime * v0[0];
				vc0[1] = curtime * v0[1];
				vars[SCROLL2].GetVecValue(v0);
				vc0[2] = curtime * v0[0];
				vc0[3] = curtime * v0[1];
				shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst3, vc0, 1);
			}

			DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
			shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

			DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
			pshIndex.Set("HDRENABLED", IsHDREnabled());
			pshIndex.Set("PIXELFOGTYPE", shaderAPI.GetPixelFogCombo());
			shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
		}
		Draw();
	}

	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		bool forceExpensive = r_waterforceexpensive.GetBool();
		bool forceCheap = (vars[FORCECHEAP].GetIntValue() != 0) || UsingEditor(vars);
		if (forceCheap)
			forceExpensive = false;
		else
			forceExpensive = forceExpensive || (vars[FORCEEXPENSIVE].GetIntValue() != 0);
		Assert(!(forceCheap && forceExpensive));

		bool refraction = vars[REFRACTTEXTURE].IsTexture();
		bool reflection = forceExpensive && vars[REFLECTTEXTURE].IsTexture();
		bool drewSomething = false;
		if (!forceCheap && (reflection || refraction)) {
			drewSomething = true;
			DrawReflectionRefraction(vars, shaderAPI, reflection, refraction);
		}

		if (!reflection && vars[ENVMAP].IsTexture() && !IsFlagSet(vars, MaterialVarFlags.Decal)) {
			drewSomething = true;
			DrawCheapWater(vars, shaderAPI, !forceCheap, refraction);
		}

		if (!drewSomething)
			Draw();
	}
}
