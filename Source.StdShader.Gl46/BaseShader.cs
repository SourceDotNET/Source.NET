using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

using System.Diagnostics.CodeAnalysis;

namespace Source.StdShader.Gl46;

public abstract class BaseShader : IShader
{
	readonly public IMaterialSystemHardwareConfig HardwareConfig = Singleton<IMaterialSystemHardwareConfig>();
	readonly public IShaderSystem ShaderSystem = Singleton<IShaderSystem>();
	static readonly Lazy<IMaterialSystem> s_materials = new(Singleton<IMaterialSystem>); // HACK
	protected static IMaterialSystem Materials => s_materials.Value;
	public static MaterialSystem_Config Config => Materials.GetCurrentConfigForVideoCard();

	internal static IMaterialVar[]? Params;
	internal static IShaderInit? ShaderInit;
	internal IShaderShadow? ShaderShadow;
	internal static IShaderDynamicAPI? ShaderAPI;
	internal static string? TextureGroupName;

	internal static ShaderParamInfo[] StandardParams = [
		new(){ Name = "$flags",                    Help = "flags",            Type = ShaderParamType.Integer,DefaultValue =  "0", Flags = ShaderParamFlags.NotEditable },
		new(){ Name = "$flags_defined",            Help = "flags_defined",    Type = ShaderParamType.Integer, DefaultValue = "0", Flags =ShaderParamFlags.NotEditable },
		new(){ Name = "$flags2",                   Help = "flags2",           Type = ShaderParamType.Integer, DefaultValue = "0", Flags =ShaderParamFlags.NotEditable },
		new(){ Name = "$flags_defined2",           Help = "flags2_defined",   Type = ShaderParamType.Integer, DefaultValue = "0", Flags =ShaderParamFlags.NotEditable },
		new(){ Name = "$color",                    Help = "color",            Type = ShaderParamType.Color,   DefaultValue = "[1 1 1]", Flags =0 },
		new(){ Name = "$alpha",                    Help = "alpha",            Type = ShaderParamType.Float,  DefaultValue =  "1.0", Flags =0 },
		new(){ Name = "$basetexture",              Help = "Base Texture with lighting built in", Type = ShaderParamType.Texture, DefaultValue = "shadertest/BaseTexture", Flags = ShaderParamFlags.DoNotUpload },
		new(){ Name = "$frame",                    Help = "Animation Frame", Type =  ShaderParamType.Integer,  DefaultValue ="0", Flags =0 },
		new(){ Name = "$basetexturetransform",     Help = "Base Texture Texcoord Transform",Type = ShaderParamType.Matrix, DefaultValue = "center .5 .5 scale 1 1 rotate 0 translate 0 0", Flags =0 },
		new(){ Name = "$flashlighttexture",        Help = "flashlight spotlight shape texture", Type = ShaderParamType.Texture, DefaultValue ="effects/flashlight001", Flags =ShaderParamFlags.NotEditable | ShaderParamFlags.DoNotUpload },
		new(){ Name = "$flashlighttextureframe",   Help = "Animation Frame for $flashlight",  Type = ShaderParamType.Integer,DefaultValue = "0", Flags =ShaderParamFlags.NotEditable },
		new(){ Name = "$color2",                   Help = "color2",           Type = ShaderParamType.Color,  DefaultValue =  "[1 1 1]",Flags = 0 },
		new(){ Name = "$srgbtint",                 Help = "tint value to be applied when running on new-style srgb parts",            Type = ShaderParamType.Color,   DefaultValue = "[1 1 1]", Flags =0 },
	];

	public string GetName() => GetType().Name;
	public virtual int GetFlags() => 0;

	public int CurrentMaterialVarFlags() {
		return Params![(int)ShaderMaterialVars.Flags].GetIntValue();
	}

	public bool IsWhite(int colorVar) {
		if (colorVar < 0)
			return true;

		if (!Params[colorVar].IsDefined())
			return true;

		Span<float> color = stackalloc float[3];
		Params[colorVar].GetVecValue(color);
		return color[0] >= 1.0f && color[1] >= 1.0f && color[2] >= 1.0f;
	}

	public virtual int GetNumParams() {
		return (int)ShaderMaterialVars.Count;
	}

	public virtual ReadOnlySpan<char> GetParamName(int paramIndex) {
		Dbg.Assert(paramIndex < (int)ShaderMaterialVars.Count);
		return StandardParams[paramIndex].Name;
	}

	public virtual ReadOnlySpan<char> GetParamHelp(int paramIndex) {
		Dbg.Assert(paramIndex < (int)ShaderMaterialVars.Count);
		return StandardParams[paramIndex].Help;
	}

	public virtual ShaderParamType GetParamType(int paramIndex) {
		Dbg.Assert(paramIndex < (int)ShaderMaterialVars.Count);
		return StandardParams[paramIndex].Type;
	}

	public virtual ReadOnlySpan<char> GetParamDefault(int paramIndex) {
		Dbg.Assert(paramIndex < (int)ShaderMaterialVars.Count);
		return StandardParams[paramIndex].DefaultValue;
	}

	public virtual string? GetFallbackShader(IMaterialVar[] vars) => null;

	public void InitShaderParams(IMaterialVar[] vars, IShaderAPI shaderAPI, ReadOnlySpan<char> materialName) {
		Assert(Params == null);
		Params = vars;
		ShaderAPI = shaderAPI;
		OnInitShaderParams(vars, materialName);
		ShaderAPI = null;
		Params = null;
	}

	protected virtual void OnInitShaderParams(IMaterialVar[] vars, ReadOnlySpan<char> materialName) { }
	protected virtual void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName) { }
	protected virtual void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression, ref BasePerMaterialContextData? contextData) => OnDrawElements(vars, shaderAPI, vertexCompression);
	protected virtual void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) { }

	public virtual bool IsTranslucent(IMaterialVar[]? parms) => IsFlagSet(parms, (int)MaterialVarFlags.Translucent);

	public void DrawElements(IMaterialVar[] vars, IShaderShadow? shadow, IShaderDynamicAPI? shaderAPI, VertexCompressionType vertexCompression, ref BasePerMaterialContextData? contextData) {
		Assert(Params == null);
		Params = vars;
		ShaderShadow = shadow;
		ShaderAPI = shaderAPI;

		if (IsSnapshotting())
			SetInitialShadowState();

		OnDrawElements(vars, shaderAPI, vertexCompression, ref contextData);

		Params = null;
		ShaderShadow = null;
		ShaderAPI = null;
		// MeshBuilder = null
	}

	internal void SetInitialShadowState() {
		ShaderShadow!.SetDefaultState();
		MaterialVarFlags flags = (MaterialVarFlags)Params![(int)ShaderMaterialVars.Flags].GetIntValue();

		if ((flags & MaterialVarFlags.IgnoreZ) != 0) {
			ShaderShadow.EnableDepthTest(false);
			ShaderShadow.EnableDepthWrites(false);
		}

		if ((flags & MaterialVarFlags.Decal) != 0) {
			ShaderShadow.EnablePolyOffset(PolygonOffsetMode.Decal);
			ShaderShadow.EnableDepthWrites(false);
		}

		if ((flags & MaterialVarFlags.NoCull) != 0)
			ShaderShadow.EnableCulling(false);

		if ((flags & MaterialVarFlags.ZNearer) != 0) {
			ShaderShadow.DepthFunc(ShaderDepthFunc.Nearer);
		}

		if ((flags & MaterialVarFlags.Wireframe) != 0)
			ShaderShadow.PolyMode(ShaderPolyModeFace.FrontAndBack, ShaderPolyMode.Line);

		if ((flags & MaterialVarFlags.AllowAlphaToCoverage) != 0)
			ShaderShadow.EnableAlphaToCoverage(true);
	}

	[MemberNotNullWhen(true, nameof(ShaderShadow))]
	internal bool IsSnapshotting() => ShaderShadow != null;

	public bool TextureIsTranslucent(int textureVar = -1, bool isBaseTexture = true) {
		if (textureVar < 0)
			return false;

		IMaterialVar[] shaderParams = Params!;
		if (shaderParams[textureVar].GetVarType() == MaterialVarType.Texture) {
			if (!isBaseTexture) {
				return shaderParams[textureVar].GetTextureValue()!.IsTranslucent();
			}
			else {
				// Override translucency settings if this flag is set.
				if (IsFlagSet(shaderParams, MaterialVarFlags.OpaqueTexture))
					return false;

				if ((CurrentMaterialVarFlags() & (int)(MaterialVarFlags.SelfIllum | MaterialVarFlags.BaseAlphaEnvMapMask)) == 0) {
					if ((CurrentMaterialVarFlags() & (int)MaterialVarFlags.Translucent) != 0 ||
						(CurrentMaterialVarFlags() & (int)MaterialVarFlags.AlphaTest) != 0) {
						return shaderParams[textureVar].GetTextureValue()!.IsTranslucent();
					}
				}
			}
		}

		return false;
	}

	public virtual void InitShaderInstance(IMaterialVar[] shaderParams, IShaderAPI shaderAPI, IShaderInit shaderInit, ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName) {
		Assert(Params == null);

		Params = shaderParams;
		ShaderAPI = shaderAPI;
		ShaderInit = shaderInit;
		TextureGroupName = new(textureGroupName);

		OnInitShaderInstance(shaderParams, materialName);

		Params = null;
		ShaderAPI = null;
		ShaderInit = null;
		TextureGroupName = null;
	}

	protected void LoadCubeMap(int envmapVar, int additionalCreationFlags = 0) {
		if (Params == null || envmapVar == -1)
			return;

		IMaterialVar nameVar = Params[envmapVar];
		if (nameVar != null && nameVar.IsDefined()) {
			ShaderInit!.LoadCubeMap(Params, nameVar, additionalCreationFlags);
		}
	}

	protected void LoadTexture(int textureVar, int additionalCreationFlags = 0) {
		if (Params == null || textureVar == -1)
			return;

		IMaterialVar nameVar = Params[textureVar];
		if (nameVar != null && nameVar.IsDefined()) {
			ShaderInit!.LoadTexture(nameVar, TextureGroupName, additionalCreationFlags);
		}
	}

	private void GetColorParameter(Span<IMaterialVar> parms, Span<float> colorOut) {
		Span<float> color2 = stackalloc float[3];
		parms[(int)ShaderMaterialVars.Color].GetVecValue(colorOut[..3]);
		parms[(int)ShaderMaterialVars.Color2].GetVecValue(color2[..3]);

		colorOut[0] *= color2[0];
		colorOut[1] *= color2[1];
		colorOut[2] *= color2[2];

		if (HardwareConfig.UsesSRGBCorrectBlending()) {
			Span<float> SRGBTint = stackalloc float[3];
			parms[(int)ShaderMaterialVars.SRGBTint].GetVecValue(SRGBTint[..3]);

			colorOut[0] *= SRGBTint[0];
			colorOut[1] *= SRGBTint[1];
			colorOut[2] *= SRGBTint[2];
		}

	}

	public ShaderUsingFlags ComputeModulationFlags(IMaterialVar[] shaderParams, IShaderDynamicAPI shaderAPI) {
		ShaderUsingFlags mod = 0;
		if (GetAlpha(shaderParams) < 1.0f)
			mod |= ShaderUsingFlags.AlphaModulation;

		Span<float> color = stackalloc float[3];
		GetColorParameter(shaderParams, color);

		if ((color[0] != 1.0f) || (color[1] != 1.0f) || (color[2] != 1.0f))
			mod |= ShaderUsingFlags.ColorModulation;

		return mod;
	}

	private float GetAlpha(Span<IMaterialVar> parms) {
		if (parms.IsEmpty)
			parms = Params;

		if (parms.IsEmpty)
			return 1.0f;

		if ((parms[(int)ShaderMaterialVars.Flags].GetIntValue() & (int)MaterialVarFlags.NoAlphaMod) != 0)
			return 1.0f;

		float alpha = parms[(int)ShaderMaterialVars.Alpha].GetFloatValue();
		return Math.Clamp(alpha, 0, 1);
	}

	protected float GetAlpha() => GetAlpha(Params);

	protected bool IsAlphaModulating() => GetAlpha() < 1.0f;

	protected void ApplyColor2Factor(Span<float> colorOut) {
		IMaterialVar[] shaderParams = Params!;

		IMaterialVar color2Var = shaderParams[(int)ShaderMaterialVars.Color2];
		if (color2Var.GetVarType() == MaterialVarType.Vector) {
			Span<float> color2 = stackalloc float[3];
			color2Var.GetVecValue(color2);
			colorOut[0] *= color2[0];
			colorOut[1] *= color2[1];
			colorOut[2] *= color2[2];
		}

		if (HardwareConfig.UsesSRGBCorrectBlending()) {
			IMaterialVar pSRGBVar = shaderParams[(int)ShaderMaterialVars.SRGBTint];
			if (pSRGBVar.GetVarType() == MaterialVarType.Vector) {
				Span<float> SRGB = stackalloc float[3];
				pSRGBVar.GetVecValue(SRGB);
				colorOut[0] *= SRGB[0];
				colorOut[1] *= SRGB[1];
				colorOut[2] *= SRGB[2];
			}
		}
	}

	protected void ComputeModulationColor(Span<float> color) {
		Assert(!IsSnapshotting());
		IMaterialVar[] shaderParams = Params!;
		if (shaderParams == null)
			return;

		IMaterialVar colorVar = shaderParams[(int)ShaderMaterialVars.Color];
		if (colorVar.GetVarType() == MaterialVarType.Vector)
			colorVar.GetVecValue(color[..3]);
		else
			color[0] = color[1] = color[2] = colorVar.GetFloatValue();

		ApplyColor2Factor(color);

		MaterialSystem_Config config = Materials.GetCurrentConfigForVideoCard();
		if (!config.ShowDiffuse)
			color[0] = color[1] = color[2] = 0.0f;
		if (config.Fullbright == 2)
			color[0] = color[1] = color[2] = 1.0f;

		color[3] = GetAlpha();
	}

	public bool NeedsPowerOfTwoFrameBufferTexture(IMaterialVar[]? shaderParams, bool checkSpecificToThisFrame)
		=> IsFlag2Set(shaderParams, MaterialVarFlags2.NeedsPowerOfTwoFrameBufferTexture);

	public bool NeedsFullFrameBufferTexture(IMaterialVar[]? shaderParams, bool checkSpecificToThisFrame)
		=> IsFlag2Set(shaderParams, MaterialVarFlags2.NeedsFullFrameBufferTexture);

	internal bool UsingFlashlight(IMaterialVar[] shaderParams) {
		if (IsSnapshotting())
			return IsFlag2Set(shaderParams, MaterialVarFlags2.UseFlashlight);
		else
			return ShaderAPI!.InFlashlightMode();
	}

	internal bool UsingEditor(IMaterialVar[] shaderParams) {
		if (IsSnapshotting())
			return IsFlag2Set(shaderParams, MaterialVarFlags2.UseEditor);
		else
			return ShaderAPI!.InEditorMode();
	}

	protected void EnableAlphaBlending(ShaderBlendFactor srcFactor, ShaderBlendFactor dstFactor) {
		ShaderShadow!.EnableBlending(true);
		ShaderShadow!.BlendFunc(srcFactor, dstFactor);
		ShaderShadow!.EnableDepthWrites(false);
	}

	protected void DisableAlphaBlending() {
		ShaderShadow!.EnableBlending(false);
	}

	public void FogToOOOverbright() {
		Assert(IsSnapshotting());
		if ((CurrentMaterialVarFlags() & (int)MaterialVarFlags.NoFog) == 0)
			ShaderShadow!.FogMode(ShaderFogMode.Overbright);
		else
			ShaderShadow!.FogMode(ShaderFogMode.Disabled);
	}

	public void FogToWhite() {
		Assert(IsSnapshotting());
		if ((CurrentMaterialVarFlags() & (int)MaterialVarFlags.NoFog) == 0)
			ShaderShadow!.FogMode(ShaderFogMode.White);
		else
			ShaderShadow!.FogMode(ShaderFogMode.Disabled);
	}

	public void FogToBlack() {
		Assert(IsSnapshotting());
		if ((CurrentMaterialVarFlags() & (int)MaterialVarFlags.NoFog) == 0)
			ShaderShadow!.FogMode(ShaderFogMode.Black);
		else
			ShaderShadow!.FogMode(ShaderFogMode.Disabled);
	}

	public void FogToGrey() {
		Assert(IsSnapshotting());
		if ((CurrentMaterialVarFlags() & (int)MaterialVarFlags.NoFog) == 0)
			ShaderShadow!.FogMode(ShaderFogMode.Grey);
		else
			ShaderShadow!.FogMode(ShaderFogMode.Disabled);
	}

	public void FogToFogColor() {
		Assert(IsSnapshotting());
		if ((CurrentMaterialVarFlags() & (int)MaterialVarFlags.NoFog) == 0)
			ShaderShadow!.FogMode(ShaderFogMode.FogColor);
		else
			ShaderShadow!.FogMode(ShaderFogMode.Disabled);
	}

	public void DisableFog() {
		Assert(IsSnapshotting());
		ShaderShadow!.FogMode(ShaderFogMode.Disabled);
	}

	public void DefaultFog() {
		if ((CurrentMaterialVarFlags() & (int)MaterialVarFlags.Additive) != 0)
			FogToBlack();
		else
			FogToFogColor();
	}

	internal void SetBlendingShadowState(BlendType blendType) {
		switch (blendType) {
			case BlendType.None:
				DisableAlphaBlending();
				break;
			case BlendType.Blend:
				EnableAlphaBlending(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.OneMinusSrcAlpha);
				break;
			case BlendType.Add:
				EnableAlphaBlending(ShaderBlendFactor.One, ShaderBlendFactor.One);
				break;
			case BlendType.BlendAdd:
				EnableAlphaBlending(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.One);
				break;
		}
	}

	internal void LoadBumpMap(int textureVar) {
		if (Params == null || textureVar == -1)
			return;

		IMaterialVar? nameVar = Params[textureVar];
		if (nameVar != null && nameVar.IsDefined())
			ShaderInit!.LoadBumpMap(nameVar, TextureGroupName);
	}

	internal void HashShadow2DJitter(float shadowJitterSeed, out float v1, out float v2) {
		const int texRes = 32;
		const int texResx2 = texRes * texRes;
		int seed = (int)(MathLib.Fmod(shadowJitterSeed, 1.0f) * texResx2);
		int row = seed / texRes;
		int col = seed % texRes;
		v1 = row / (float)texRes;
		v2 = col / (float)texRes;
	}
}
