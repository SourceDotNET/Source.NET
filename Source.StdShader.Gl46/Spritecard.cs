using Source.Common;
using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class Spritecard : BaseVSShader
{
	public static string HelpString = "Help for Spritecard";
	public static ShaderFlags Flags = ShaderFlags.NotEditable;
	public static List<ShaderParam> ShaderParams = [];
	public static ShaderParam[] ShaderParamOverrides = new ShaderParam[(int)ShaderMaterialVars.Count];

	public class ShaderParam
	{
		public readonly ShaderParamInfo Info;
		public readonly int Index;
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

	public static readonly ShaderParam DEPTHBLEND = new($"${nameof(DEPTHBLEND)}", ShaderParamType.Integer, "0", "fade at intersection boundaries");
	public static readonly ShaderParam DEPTHBLENDSCALE = new($"${nameof(DEPTHBLENDSCALE)}", ShaderParamType.Float, "50.0", "Amplify or reduce DEPTHBLEND fading. Lower values make harder edges.");
	public static readonly ShaderParam ORIENTATION = new($"${nameof(ORIENTATION)}", ShaderParamType.Integer, "0", "0 = always face camera, 1 = rotate around z, 2= parallel to ground");
	public static readonly ShaderParam ADDBASETEXTURE2 = new($"${nameof(ADDBASETEXTURE2)}", ShaderParamType.Float, "0.0", "amount to blend second texture into frame by");
	public static readonly ShaderParam OVERBRIGHTFACTOR = new($"${nameof(OVERBRIGHTFACTOR)}", ShaderParamType.Float, "1.0", "overbright factor for texture. For HDR effects.");
	public static readonly ShaderParam DUALSEQUENCE = new($"${nameof(DUALSEQUENCE)}", ShaderParamType.Integer, "0", "blend two separate animated sequences.");
	public static readonly ShaderParam SEQUENCE_BLEND_MODE = new($"${nameof(SEQUENCE_BLEND_MODE)}", ShaderParamType.Integer, "0", "defines the blend mode between the images un dual sequence particles. 0 = avg, 1=alpha from first, rgb from 2nd, 2= first over second");
	public static readonly ShaderParam MAXLUMFRAMEBLEND1 = new($"${nameof(MAXLUMFRAMEBLEND1)}", ShaderParamType.Integer, "0", "instead of blending between animation frames for the first sequence, select pixels based upon max luminance");
	public static readonly ShaderParam MAXLUMFRAMEBLEND2 = new($"${nameof(MAXLUMFRAMEBLEND2)}", ShaderParamType.Integer, "0", "instead of blending between animation frames for the 2nd sequence, select pixels based upon max luminance");
	public static readonly ShaderParam RAMPTEXTURE = new($"${nameof(RAMPTEXTURE)}", ShaderParamType.Texture, "", "if specified, then the red value of the image is used to index this ramp to produce the output color");
	public static readonly ShaderParam ZOOMANIMATESEQ2 = new($"${nameof(ZOOMANIMATESEQ2)}", ShaderParamType.Float, "1.0", "amount to gradually zoom between frames on the second sequence. 2.0 will double the size of a frame over its lifetime.");
	public static readonly ShaderParam EXTRACTGREENALPHA = new($"${nameof(EXTRACTGREENALPHA)}", ShaderParamType.Integer, "0", "grayscale data sitting in green/alpha channels");
	public static readonly ShaderParam ADDOVERBLEND = new($"${nameof(ADDOVERBLEND)}", ShaderParamType.Integer, "0", "use ONE:INVSRCALPHA blending");
	public static readonly ShaderParam ADDSELF = new($"${nameof(ADDSELF)}", ShaderParamType.Float, "0.0", "amount of base texture to additively blend in");
	public static readonly ShaderParam BLENDFRAMES = new($"${nameof(BLENDFRAMES)}", ShaderParamType.Bool, "1", "whether or not to smoothly blend between animated frames");
	public static readonly ShaderParam MINSIZE = new($"${nameof(MINSIZE)}", ShaderParamType.Float, "0.0", "minimum screen fractional size of particle");
	public static readonly ShaderParam STARTFADESIZE = new($"${nameof(STARTFADESIZE)}", ShaderParamType.Float, "10.0", "screen fractional size to start fading particle out");
	public static readonly ShaderParam ENDFADESIZE = new($"${nameof(ENDFADESIZE)}", ShaderParamType.Float, "20.0", "screen fractional size to finish fading particle out");
	public static readonly ShaderParam MAXSIZE = new($"${nameof(MAXSIZE)}", ShaderParamType.Float, "20.0", "maximum screen fractional size of particle");
	public static readonly ShaderParam USEINSTANCING = new($"${nameof(USEINSTANCING)}", ShaderParamType.Bool, "1", "whether to use GPU vertex instancing (submit 1 vert per particle quad)");
	public static readonly ShaderParam SPLINETYPE = new($"${nameof(SPLINETYPE)}", ShaderParamType.Integer, "0", "spline type 0 = none,  1=ctamull rom");
	public static readonly ShaderParam MAXDISTANCE = new($"${nameof(MAXDISTANCE)}", ShaderParamType.Float, "100000.0", "maximum distance to draw particles at");
	public static readonly ShaderParam FARFADEINTERVAL = new($"${nameof(FARFADEINTERVAL)}", ShaderParamType.Float, "400.0", "interval over which to fade out far away particles");

	static readonly ConVarRef mat_reduceparticles = new("mat_reduceparticles");

	static readonly int[] TexCoordSize = [4, 4, 4, 2, 4, 4, 4, 4];

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

	static int GetDefaultDepthFeatheringValue() => mat_reduceparticles.GetBool() ? 0 : 1;

	protected override void OnInitShaderParams(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		InitFloatParam(MAXDISTANCE, parms, 100000.0f);
		InitFloatParam(FARFADEINTERVAL, parms, 400.0f);
		InitFloatParam(MAXSIZE, parms, 20.0f);
		InitFloatParam(ENDFADESIZE, parms, 20.0f);
		InitFloatParam(STARTFADESIZE, parms, 10.0f);
		InitFloatParam(DEPTHBLENDSCALE, parms, 50.0f);
		InitFloatParam(OVERBRIGHTFACTOR, parms, 1.0f);
		InitFloatParam(ADDBASETEXTURE2, parms, 0.0f);
		InitFloatParam(ADDSELF, parms, 0.0f);
		InitFloatParam(ZOOMANIMATESEQ2, parms, 0.0f);

		if (!parms[DEPTHBLEND].IsDefined())
			parms[DEPTHBLEND].SetIntValue(GetDefaultDepthFeatheringValue());

		InitIntParam(DUALSEQUENCE, parms, 0);
		InitIntParam(MAXLUMFRAMEBLEND1, parms, 0);
		InitIntParam(MAXLUMFRAMEBLEND2, parms, 0);
		InitIntParam(EXTRACTGREENALPHA, parms, 0);
		InitIntParam(ADDOVERBLEND, parms, 0);
		InitIntParam(BLENDFRAMES, parms, 1);
		InitIntParam(USEINSTANCING, parms, 0);

		SetFlags2(parms, MaterialVarFlags2.IsSpritecard);
	}

	protected override void OnInitShaderInstance(IMaterialVar[] parms, ReadOnlySpan<char> materialName) {
		SetFlags2(parms, MaterialVarFlags2.LightingVertexLit);

		if (parms[(int)ShaderMaterialVars.BaseTexture].IsDefined()) {
			bool extractGreenAlpha = false;
			if (parms[EXTRACTGREENALPHA].IsDefined())
				extractGreenAlpha = parms[EXTRACTGREENALPHA].GetIntValue() != 0;

			LoadTexture((int)ShaderMaterialVars.BaseTexture, !extractGreenAlpha ? (int)TextureFlags.SRGB : 0);
		}
		if (parms[RAMPTEXTURE].IsDefined())
			LoadTexture(RAMPTEXTURE, (int)TextureFlags.SRGB);
	}

	protected override void OnDrawElements(IMaterialVar[] parms, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		bool useRampTexture = parms[RAMPTEXTURE].IsDefined();
		bool zoomSeq2 = parms[ZOOMANIMATESEQ2].GetFloatValue() > 1.0f;
		bool additive2ndTexture = parms[ADDBASETEXTURE2].GetFloatValue() != 0.0f;
		int splineType = parms[SPLINETYPE].GetIntValue();

		if (IsSnapshotting()) {
			IShaderShadow shaderShadow = ShaderShadow!;

			bool secondSequence = parms[DUALSEQUENCE].GetIntValue() != 0;
			bool addOverBlend = parms[ADDOVERBLEND].GetIntValue() != 0;
			bool extractGreenAlpha = parms[EXTRACTGREENALPHA].GetIntValue() != 0;
			bool blendFrames = parms[BLENDFRAMES].GetIntValue() != 0;
			if (splineType != 0)
				blendFrames = false;
			bool addSelf = parms[ADDSELF].GetFloatValue() != 0.0f;

			shaderShadow.EnableCulling(false);
			shaderShadow.EnableAlphaWrites(false);
			shaderShadow.EnableTexture(Sampler.Sampler0, true);

			if (useRampTexture) {
				shaderShadow.EnableTexture(Sampler.Sampler1, true);
				shaderShadow.EnableSRGBRead(Sampler.Sampler1, true);
			}

			shaderShadow.EnableAlphaTest(!(additive2ndTexture || addSelf));
			shaderShadow.AlphaFunc(ShaderAlphaFunc.Greater, 0.01f);

			if (additive2ndTexture || addOverBlend || addSelf)
				EnableAlphaBlending(ShaderBlendFactor.One, ShaderBlendFactor.OneMinusSrcAlpha);
			else if (IsFlagSet(parms, MaterialVarFlags.Additive))
				EnableAlphaBlending(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.One);
			else
				EnableAlphaBlending(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.OneMinusSrcAlpha);

			int numTexCoords = secondSequence ? 8 : 5;
			shaderShadow.VertexShaderVertexFormat(VertexFormat.Position | VertexFormat.Color, numTexCoords, TexCoordSize, 0);

			StaticShaderIndex vshIndex = new(shaderShadow, ShaderType.Vertex, "spritecard");
			vshIndex.Set("DUALSEQUENCE", secondSequence);
			vshIndex.Set("ZOOM_ANIMATE_SEQ2", zoomSeq2);
			vshIndex.Set("EXTRACTGREENALPHA", extractGreenAlpha);
			shaderShadow.SetVertexShader("spritecard", vshIndex.GetIndex());

			StaticShaderIndex pshIndex = new(shaderShadow, ShaderType.Pixel, "spritecard");
			pshIndex.Set("ADDBASETEXTURE2", additive2ndTexture);
			pshIndex.Set("ADDSELF", addSelf);
			pshIndex.Set("ANIMBLEND", blendFrames);
			pshIndex.Set("DUALSEQUENCE", secondSequence);
			pshIndex.Set("SEQUENCE_BLEND_MODE", secondSequence ? parms[SEQUENCE_BLEND_MODE].GetIntValue() : 0);
			pshIndex.Set("MAXLUMFRAMEBLEND1", parms[MAXLUMFRAMEBLEND1].GetIntValue());
			pshIndex.Set("MAXLUMFRAMEBLEND2", secondSequence ? parms[MAXLUMFRAMEBLEND1].GetIntValue() : 0);
			pshIndex.Set("COLORRAMP", useRampTexture);
			pshIndex.Set("EXTRACTGREENALPHA", extractGreenAlpha);
			shaderShadow.SetPixelShader("spritecard", pshIndex.GetIndex());

			shaderShadow.EnableSRGBWrite(true);

			if (!extractGreenAlpha)
				shaderShadow.EnableSRGBRead(Sampler.Sampler0, true);
		}
		else {
			BindTexture(Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, (int)ShaderMaterialVars.Frame);

			if (useRampTexture)
				BindTexture(Sampler.Sampler1, RAMPTEXTURE, (int)ShaderMaterialVars.Frame);

			int orientation = Math.Clamp(parms[ORIENTATION].GetIntValue(), 0, 2);

			if (zoomSeq2) {
				float zScale = 1.0f / parms[ZOOMANIMATESEQ2].GetFloatValue();
				Span<float> c0 = [0.5f * (1.0f + zScale), zScale, 0, 0];
				shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst7, c0);
			}

			float maxDistance = parms[MAXDISTANCE].GetFloatValue();
			float startFade = MathF.Max(1.0f, maxDistance - parms[FARFADEINTERVAL].GetFloatValue());

			Span<float> vc0 = [
				parms[MINSIZE].GetFloatValue(), parms[MAXSIZE].GetFloatValue(),
				parms[STARTFADESIZE].GetFloatValue(), parms[ENDFADESIZE].GetFloatValue(),
				startFade, 1.0f / (maxDistance - startFade),
				0, 0
			];
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst8, vc0, 2);

			Span<float> pc0 = [
				parms[ADDBASETEXTURE2].GetFloatValue(),
				parms[OVERBRIGHTFACTOR].GetFloatValue(),
				parms[ADDSELF].GetFloatValue(),
				0.0f
			];
			shaderAPI.SetPixelShaderConstant(0, pc0);

			DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
			vshIndex.Set("ORIENTATION", orientation);
			shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

			DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
			shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
		}
		Draw();
	}
}
