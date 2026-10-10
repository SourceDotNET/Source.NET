using K4os.Hash.xxHash;

using Microsoft.Extensions.DependencyInjection;

using OpenGL;

using Source.Bitmap;
using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;
using Source.Common.Utilities;

using System.Numerics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Source.ShaderAPI.Gl46;

using OpenGL_ShaderInputAttribute = VertexBufferGl46.OpenGL_ShaderInputAttribute;

/// <summary>
/// Standardized OpenGL UBO binding locations. All uniform buffers are loaded with layout std140
/// </summary>
public enum UniformBufferBindingLocation
{
	/// <summary><b>source_matrices</b>: The model, view, and projection matrices</summary>
	SharedMatrices = 0,
	/// <summary><b>source_base_sharedUBO</b>: Shared uniforms that every shader can use.</summary>
	SharedBaseShader = 1,
	/// <summary><b>source_vertex_sharedUBO</b>: Shared uniforms that every vertex shader can use.</summary>
	SharedVertexShader = 2,
	/// <summary><b>source_pixel_sharedUBO</b>: Shared uniforms that every pixel shader can use.</summary>
	SharedPixelShader = 3,
	/// <summary><b>source_bone_matrices</b>: A <see cref="Matrix4x4"/>[<see cref="Studio.MAXSTUDIOBONES"/>] array.</summary>
	SharedBoneMatrices = 4,
	/// <summary><b>source_vs_constants</b>: Vertex shader float constants.</summary>
	VertexShaderConstants = 5,
	/// <summary><b>source_ps_constants</b>: Pixel shader float constants.</summary>
	PixelShaderConstants = 6,
	Count = 7
}

public static class UniformBufferBindings
{
	static readonly uint[] LastBound = new uint[(int)UniformBufferBindingLocation.Count];

	public static void Bind(UniformBufferBindingLocation location, uint buffer) {
		if (LastBound[(int)location] == buffer)
			return;

		LastBound[(int)location] = buffer;
		glBindBufferBase(GL_UNIFORM_BUFFER, (uint)location, buffer);
	}
}

public struct GfxViewport
{
	public int X;
	public int Y;
	public int Width;
	public int Height;
	public float MinZ;
	public float MaxZ;
}

public enum CommitFuncType
{
	PerDraw,
	PerPass
}

public enum TransformDirtyBits
{
	StateChangedVertexShader = 0x1,
	StateChangedFixedFunction = 0x2,
	StateChanged = 0x3
}

public class ShaderAPIGl46 : IShaderAPI, IShaderDevice, IDebugTextureInfo
{
	public MeshMgr MeshMgr;
	public IShaderSystem ShaderManager;

	public static void DLLInit(IServiceCollection services) {
		services.AddSingleton(x => (IDebugTextureInfo)(ShaderAPIGl46)x.GetRequiredService<IShaderAPI>());
		services.AddSingleton(x => ((ShaderAPIGl46)x.GetRequiredService<IShaderAPI>()).GetShaderDevice());
		services.AddSingleton<IMeshMgr, MeshMgr>();
		services.AddSingleton<IMaterialSystemHardwareConfig, HardwareConfig>();
		services.AddSingleton<IShaderSystem, ShaderSystem>();
		services.AddSingleton<MeshMgr>();
	}

	public GraphicsDriver GetDriver() => Driver;
	private bool ready;

	uint renderFBO;
	uint copyFBO;

	public bool OnDeviceInit() {
		AcquireInternalRenderTargets();

		CreateMatrixStacks();

		MeshMgr.MaterialSystem = Singleton<IMaterialSystem>();
		MeshMgr.ShaderAPI = this;
		MeshMgr.Init();
		Device!.SetSwapInterval(0);

		InitRenderState();

		ClearColor4ub(0, 0, 0, 1);
		ClearBuffers(true, true, true, -1, -1);

		return true;
	}

	DeviceState DeviceState = DeviceState.OK;

	public void ClearBuffers(bool clearColor, bool clearDepth, bool clearStencil, int renderTargetWidth = -1, int renderTargetHeight = -1) {
		if (IsDeactivated())
			return;

		FlushBufferedPrimitives();
		uint flags = 0;

		if (clearColor)
			flags |= GL_COLOR_BUFFER_BIT;
		if (clearDepth)
			flags |= GL_DEPTH_BUFFER_BIT;
		if (clearStencil)
			flags |= GL_STENCIL_BUFFER_BIT;

		if (clearDepth)
			glDepthMask(true);
		if (clearStencil)
			glStencilMask(0xFF);

		if (flags != 0) {
			bool renderTargetMatchesViewport =
				(renderTargetWidth == -1 && renderTargetHeight == -1) ||
				(renderTargetWidth == Viewport.Width && renderTargetHeight == Viewport.Height);

			if (renderTargetMatchesViewport)
				glClear(flags);
			else {
				glEnable(GL_SCISSOR_TEST);
				glScissor(Viewport.X, renderTargetHeight - (Viewport.Y + Viewport.Height), Viewport.Width, Viewport.Height);
				glClear(flags);
				glDisable(GL_SCISSOR_TEST);
			}
		}
	}

	public void ClearColor3ub(byte r, byte g, byte b) => glClearColor(r / 255f, g / 255f, b / 255f, 1);
	public void ClearColor4ub(byte r, byte g, byte b, byte a) => glClearColor(r / 255f, g / 255f, b / 255f, a / 255f);

	VertexShaderHandle activeVertexShader = VertexShaderHandle.INVALID;
	PixelShaderHandle activePixelShader = PixelShaderHandle.INVALID;
	bool pipelineChanged = false;
	uint lastShader;
	ShadowStateGl46? currentShadow;
	internal void SetCurrentShadow(ShadowStateGl46 shadow) {
		currentShadow = shadow;
	}

	public void SetVertexShaderIndex(int index) {
		if (currentShadow != null)
			BindVertexShader(currentShadow.GetVertexShaderVariant(index));
	}

	public void SetPixelShaderIndex(int index) {
		if (currentShadow != null)
			BindPixelShader(currentShadow.GetPixelShaderVariant(index));
	}

	public int GetDynamicComboScale(ShaderType type, ReadOnlySpan<char> name) => currentShadow?.GetDynamicComboScale(type, name) ?? 0;

	public void BindVertexShader(VertexShaderHandle vertexShader) {
		activeVertexShader = vertexShader;
		pipelineChanged = true;
	}

	public void BindPixelShader(PixelShaderHandle pixelShader) {
		activePixelShader = pixelShader;
		pipelineChanged = true;
	}

	public void CallCommitFuncs(CommitFuncType func, bool usingFixedFunction, bool force = false) {

	}

	const int NUM_VERTEX_SHADER_CONSTANTS = 256;
	readonly float[] desiredVertexShaderConstants = new float[NUM_VERTEX_SHADER_CONSTANTS * 4];
	readonly float[] dynamicVertexShaderConstants = new float[NUM_VERTEX_SHADER_CONSTANTS * 4];

	const int NUM_PIXEL_SHADER_CONSTANTS = 256;
	readonly float[] desiredPixelShaderConstants = new float[NUM_PIXEL_SHADER_CONSTANTS * 4];
	readonly float[] dynamicPixelShaderConstants = new float[NUM_PIXEL_SHADER_CONSTANTS * 4];

	uint uboMatrices;
	uint uboBones;
	uint uboVertexConstants;
	uint uboPixelConstants;

	SourceVertexSharedShadowState vertexShared;

	internal SourceVertexSharedShadowState GetVertexSharedState() => vertexShared;

	private unsafe void CreateMatrixStacks() {
		uboMatrices = glCreateBuffer();
		glObjectLabel(GL_BUFFER, uboMatrices, "ShaderAPI Shared Matrix UBO");
		glNamedBufferData(uboMatrices, sizeof(Matrix4x4) * 3, null, GL_DYNAMIC_DRAW);
		UniformBufferBindings.Bind(UniformBufferBindingLocation.SharedMatrices, uboMatrices);

		uboBones = glCreateBuffer();
		glObjectLabel(GL_BUFFER, uboBones, "ShaderAPI Shared Bone UBO");

		Matrix4x4* identityMatrices = stackalloc Matrix4x4[Studio.MAXSTUDIOBONES];
		for (int i = 0; i < Studio.MAXSTUDIOBONES; i++)
			identityMatrices[i] = Matrix4x4.Identity;

		glNamedBufferData(uboBones, sizeof(Matrix4x4) * Studio.MAXSTUDIOBONES, identityMatrices, GL_DYNAMIC_DRAW);
		UniformBufferBindings.Bind(UniformBufferBindingLocation.SharedBoneMatrices, uboBones);

		uboVertexConstants = glCreateBuffer();
		glObjectLabel(GL_BUFFER, uboVertexConstants, "ShaderAPI Vertex Shader Constants UBO");
		glNamedBufferData(uboVertexConstants, sizeof(float) * NUM_VERTEX_SHADER_CONSTANTS * 4, null, GL_DYNAMIC_DRAW);
		UniformBufferBindings.Bind(UniformBufferBindingLocation.VertexShaderConstants, uboVertexConstants);

		uboPixelConstants = glCreateBuffer();
		glObjectLabel(GL_BUFFER, uboPixelConstants, "ShaderAPI Pixel Shader Constants UBO");
		glNamedBufferData(uboPixelConstants, sizeof(float) * NUM_PIXEL_SHADER_CONSTANTS * 4, null, GL_DYNAMIC_DRAW);
		UniformBufferBindings.Bind(UniformBufferBindingLocation.PixelShaderConstants, uboPixelConstants);
	}

	private void AcquireInternalRenderTargets() {
		renderFBO = glCreateFramebuffer();
		copyFBO = glCreateFramebuffer();
	}

	public void InitRenderState() {
		glDisable(GL_DEPTH_TEST);
		glFrontFace(GL_CW);
		glEnable(GL_TEXTURE_CUBE_MAP_SEAMLESS);

		if (!IsDeactivated())
			ResetRenderState();

		SetToneMappingScaleLinear(new Vector3(1.0f, 1.0f, 1.0f));
	}

	public void SetPresentParameters(in ShaderDeviceInfo info) {
		PresentParameters = info;
	}

	public void ResetRenderState(bool fullReset = true) {
		if (fullReset) {
			InitVertexAndPixelShaders();
		}

		int width = 0, height = 0;
		Singleton<ILauncherManager>().DisplayedSize(out width, out height);
		ShaderViewport viewport = new ShaderViewport(0, 0, width, height);
		SetViewports(new(ref viewport));
	}

	readonly ShaderAPITextureHandle_t[] StdTextureHandles = new ShaderAPITextureHandle_t[(int)StandardTextureId.Max];

	public void SetStandardTextureHandle(StandardTextureId id, ShaderAPITextureHandle_t handle) {
		StdTextureHandles[(int)id] = handle;
	}

	public void SetLinearToGammaConversionTextures(ShaderAPITextureHandle_t srgbWriteEnabledTexture, ShaderAPITextureHandle_t identityTexture) {
		LinearToGammaTableTexture = srgbWriteEnabledTexture;
		LinearToGammaTableIdentityTexture = identityTexture;
	}

	ShaderAPITextureHandle_t LinearToGammaTableTexture;
	ShaderAPITextureHandle_t LinearToGammaTableIdentityTexture;

	public float LinearToGamma_HardwareSpecific(float linear) {
		if (IsPC())
			return MathLib.SrgbLinearToGamma(linear);
		else
			throw new NotImplementedException();
	}

	public void SetDefaultState() {

	}

	public IShaderShadow NewShaderShadow(ReadOnlySpan<char> materialName) => new ShadowStateGl46(this, (IShaderSystemInternal)ShaderManager, materialName);
	public bool IsTranslucent(IShaderShadow renderState) => ((ShadowStateGl46)renderState).State.Blending;
	public bool IsAlphaTested(IShaderShadow renderState) => ((ShadowStateGl46)renderState).Pixel.IsAlphaTesting != 0;

	private InlineArray6<Vector4> AmbientLightCube;
	private int CachedAmbientLightCube = (int)TransformDirtyBits.StateChanged;
	public void SetAmbientLightCube(Span<Vector4> cube) {
		Span<Vector4> dst = AmbientLightCube;
		if (!memcmpb(dst, cube)) {
			memcpy(dst, cube);
			CachedAmbientLightCube = (int)TransformDirtyBits.StateChanged;
		}
	}

	private Vector3 LightingOrigin;
	public void SetLightingOrigin(Vector3 lightingOrigin) {
		if (lightingOrigin != LightingOrigin) {
			FlushBufferedPrimitives();
			LightingOrigin = lightingOrigin;
		}
	}

	public void SetAmbientLight(float r, float g, float b) {
		// todo todo
	}

	public const int MAX_NUM_LIGHTS = 4;

	enum VertexShaderLightTypes
	{
		None = -1,
		Spot = 0,
		Point = 1,
		Directional = 2,
		Static = 3
	}

	struct Light
	{
		public LightType Type;
		public Vector4 Diffuse;
		public Vector4 Specular;
		public Vector4 Ambient;
		public Vector3 Position;
		public Vector3 Direction;
		public float Range;
		public float Falloff;
		public float Attenuation0;
		public float Attenuation1;
		public float Attenuation2;
		public float Theta;
		public float Phi;
	}

	readonly LightDesc[] LightDescs = new LightDesc[MAX_NUM_LIGHTS];
	readonly Light[] Lights = new Light[MAX_NUM_LIGHTS];
	readonly bool[] LightEnable = new bool[MAX_NUM_LIGHTS];
	readonly TransformDirtyBits[] LightEnableChanged = new TransformDirtyBits[MAX_NUM_LIGHTS];
	readonly TransformDirtyBits[] LightChanged = new TransformDirtyBits[MAX_NUM_LIGHTS];
	readonly VertexShaderLightTypes[] LightTypes = new VertexShaderLightTypes[MAX_NUM_LIGHTS];
	int NumLights;

	public int GetMaxLights() => MAX_NUM_LIGHTS;

	public void SetLight(int lightNum, in LightDesc desc) {
		if (lightNum >= MAX_NUM_LIGHTS || lightNum < 0)
			return;

		LightDescs[lightNum] = desc;

		FlushBufferedPrimitives();

		if (desc.Type == LightType.Disable) {
			if (LightEnable[lightNum]) {
				LightEnableChanged[lightNum] = TransformDirtyBits.StateChanged;
				LightEnable[lightNum] = false;
			}
			return;
		}

		if (!LightEnable[lightNum]) {
			LightEnableChanged[lightNum] = TransformDirtyBits.StateChanged;
			LightEnable[lightNum] = true;
		}

		Light light = default;
		switch (desc.Type) {
			case LightType.Point:
				light.Type = LightType.Point;
				light.Range = desc.Range;
				break;

			case LightType.Directional:
				light.Type = LightType.Directional;
				light.Range = 1e12f;
				break;

			case LightType.Spot:
				light.Type = LightType.Spot;
				light.Range = desc.Range;
				break;

			default:
				LightEnable[lightNum] = false;
				return;
		}

		light.Diffuse = new Vector4(desc.Color, 1.0f);
		light.Specular = new Vector4(desc.Color, 1.0f);
		light.Ambient = new Vector4(0, 0, 0, 0);
		light.Position = desc.Position;
		light.Direction = desc.Direction;
		light.Falloff = desc.Falloff;
		light.Attenuation0 = desc.Attenuation0;
		light.Attenuation1 = desc.Attenuation1;
		light.Attenuation2 = desc.Attenuation2;

		light.Theta = desc.Theta;
		light.Phi = desc.Phi;
		if (light.Phi > MathF.PI)
			light.Phi = MathF.PI;

		if (light.Theta - light.Phi > -1e-3f)
			light.Theta = light.Phi - 1e-3f;

		LightChanged[lightNum] = TransformDirtyBits.StateChanged;
		Lights[lightNum] = light;
	}

	FlashlightState FlashlightState;
	Matrix4x4 FlashlightWorldToTexture;
	ITexture? FlashlightDepthTexture;

	StencilOperation StencilFailOperation = StencilOperation.Keep;
	StencilOperation StencilZFailOperation = StencilOperation.Keep;
	StencilOperation StencilPassOperation = StencilOperation.Keep;
	StencilComparisonFunction StencilCompareFunction = StencilComparisonFunction.Always;
	int StencilReferenceValue;
	uint StencilTestMask = 0xFFFFFFFF;

	public void SetFlashlightStateEx(in FlashlightState state, in Matrix4x4 worldToTexture, ITexture? flashlightDepthTexture) {
		FlushBufferedPrimitives();
		FlashlightState = state;
		FlashlightWorldToTexture = worldToTexture;
		FlashlightDepthTexture = flashlightDepthTexture;
	}

	public void SetStencilEnable(bool onoff) {
		FlushBufferedPrimitives();
		if (onoff)
			glEnable(GL_STENCIL_TEST);
		else
			glDisable(GL_STENCIL_TEST);
	}

	public void SetStencilFailOperation(StencilOperation op) {
		FlushBufferedPrimitives();
		StencilFailOperation = op;
		ApplyStencilOperations();
	}

	public void SetStencilZFailOperation(StencilOperation op) {
		FlushBufferedPrimitives();
		StencilZFailOperation = op;
		ApplyStencilOperations();
	}

	public void SetStencilPassOperation(StencilOperation op) {
		FlushBufferedPrimitives();
		StencilPassOperation = op;
		ApplyStencilOperations();
	}

	public void SetStencilCompareFunction(StencilComparisonFunction cmpfn) {
		if (StencilCompareFunction == cmpfn)
			return;

		FlushBufferedPrimitives();
		StencilCompareFunction = cmpfn;
		ApplyStencilFunc();
	}

	public void SetStencilReferenceValue(int reference) {
		if (StencilReferenceValue == reference)
			return;

		FlushBufferedPrimitives();
		StencilReferenceValue = reference;
		ApplyStencilFunc();
	}

	public void SetStencilTestMask(uint msk) {
		if (StencilTestMask == msk)
			return;

		FlushBufferedPrimitives();
		StencilTestMask = msk;
		ApplyStencilFunc();
	}

	uint StencilWriteMask = 0xFFFFFFFF;
	public void SetStencilWriteMask(uint msk) {
		if (StencilWriteMask == msk)
			return;

		FlushBufferedPrimitives();
		StencilWriteMask = msk;
		glStencilMask(msk);
	}

	void ApplyStencilOperations() => glStencilOp(StencilFailOperation.GLEnum(), StencilZFailOperation.GLEnum(), StencilPassOperation.GLEnum());

	void ApplyStencilFunc() => glStencilFunc(StencilCompareFunction.GLEnum(), StencilReferenceValue, StencilTestMask);

	public void SetScissorRect(int left, int top, int right, int bottom, bool enableScissor) {
		FlushBufferedPrimitives();
		if (!enableScissor) {
			glDisable(GL_SCISSOR_TEST);
			return;
		}

		GetBackBufferDimensions(out _, out int height);
		glEnable(GL_SCISSOR_TEST);
		glScissor(left, height - bottom, right - left, bottom - top);
	}

	public void DisableAllLocalLights() {
		bool flushed = false;
		for (int lightNum = 0; lightNum < MAX_NUM_LIGHTS; lightNum++) {
			if (LightEnable[lightNum]) {
				if (!flushed) {
					FlushBufferedPrimitives();
					flushed = true;
				}
				LightDescs[lightNum].Type = LightType.Disable;
				LightEnableChanged[lightNum] = TransformDirtyBits.StateChanged;
				LightEnable[lightNum] = false;
			}
		}
	}

	private VertexShaderLightTypes ComputeLightType(int i) {
		if (!LightEnable[i])
			return VertexShaderLightTypes.None;

		return Lights[i].Type switch {
			LightType.Point => VertexShaderLightTypes.Point,
			LightType.Directional => VertexShaderLightTypes.Directional,
			LightType.Spot => VertexShaderLightTypes.Spot,
			_ => VertexShaderLightTypes.None,
		};
	}

	private void SortLights(Span<int> index) {
		NumLights = 0;

		for (int i = 0; i < MAX_NUM_LIGHTS; ++i) {
			VertexShaderLightTypes type = ComputeLightType(i);
			int j = NumLights;
			if (type != VertexShaderLightTypes.None) {
				while (--j >= 0) {
					if (LightTypes[j] <= type)
						break;

					LightTypes[j + 1] = LightTypes[j];
					index[j + 1] = index[j];
				}
				++j;

				LightTypes[j] = type;
				index[j] = i;

				++NumLights;
			}
		}
	}

	public const int PSREG_LIGHT_INFO_ARRAY = 20;

	public void CommitPixelShaderLighting(int pshReg) {
		Span<int> lightIndex = stackalloc int[MAX_NUM_LIGHTS];
		SortLights(lightIndex);

		const float farAway = 10000.0f;

		Span<Vector4> lightState = stackalloc Vector4[6];
		for (int i = 0; i < 6; i++)
			lightState[i] = default;

		int numLights = NumLights;
		if (numLights > 0) {
			ref Light light = ref Lights[lightIndex[0]];
			lightState[0] = new Vector4(light.Diffuse.X, light.Diffuse.Y, light.Diffuse.Z, 0.0f);

			if (light.Type == LightType.Directional) {
				Vector3 dir = light.Direction;
				Vector3 pos = LightingOrigin - dir * farAway;
				lightState[1] = new Vector4(pos.X, pos.Y, pos.Z, 0.0f);
			}
			else {
				lightState[1] = new Vector4(light.Position.X, light.Position.Y, light.Position.Z, 0.0f);
			}

			if (numLights > 1) {
				light = ref Lights[lightIndex[1]];
				lightState[2] = new Vector4(light.Diffuse.X, light.Diffuse.Y, light.Diffuse.Z, 0.0f);

				if (light.Type == LightType.Directional) {
					Vector3 dir = light.Direction;
					Vector3 pos = LightingOrigin - dir * farAway;
					lightState[3] = new Vector4(pos.X, pos.Y, pos.Z, 0.0f);
				}
				else
					lightState[3] = new Vector4(light.Position.X, light.Position.Y, light.Position.Z, 0.0f);

				if (numLights > 2) {
					light = ref Lights[lightIndex[2]];
					lightState[4] = new Vector4(light.Diffuse.X, light.Diffuse.Y, light.Diffuse.Z, 0.0f);

					if (light.Type == LightType.Directional) {
						Vector3 dir = light.Direction;
						Vector3 pos = LightingOrigin - dir * farAway;
						lightState[5] = new Vector4(pos.X, pos.Y, pos.Z, 0.0f);
					}
					else
						lightState[5] = new Vector4(light.Position.X, light.Position.Y, light.Position.Z, 0.0f);

					if (numLights > 3) {
						light = ref Lights[lightIndex[3]];
						lightState[0].W = light.Diffuse.X;
						lightState[1].W = light.Diffuse.Y;
						lightState[2].W = light.Diffuse.Z;

						if (light.Type == LightType.Directional) {
							Vector3 dir = light.Direction;
							Vector3 pos = LightingOrigin - dir * farAway;
							lightState[3].W = pos.X;
							lightState[4].W = pos.Y;
							lightState[5].W = pos.Z;
						}
						else {
							lightState[3].W = light.Position.X;
							lightState[4].W = light.Position.Y;
							lightState[5].W = light.Position.Z;
						}
					}
				}
			}
		}

		SetPixelShaderConstant(pshReg, MemoryMarshal.Cast<Vector4, float>(lightState), 6);
	}

	private bool VertexShaderLightingChanged(int i) => (LightChanged[i] & TransformDirtyBits.StateChangedVertexShader) != 0;
	private bool VertexShaderLightingEnableChanged(int i) => (LightEnableChanged[i] & TransformDirtyBits.StateChangedVertexShader) != 0;

	public void CommitVertexShaderLighting() {
		int i;
		for (i = 0; i < MAX_NUM_LIGHTS; ++i) {
			if (VertexShaderLightingChanged(i) || VertexShaderLightingEnableChanged(i))
				break;
		}

		if (i == MAX_NUM_LIGHTS)
			return;

		Span<int> lightIndex = stackalloc int[MAX_NUM_LIGHTS];
		lightIndex.Clear();
		SortLights(lightIndex);

		for (i = 0; i < MAX_NUM_LIGHTS; ++i) {
			LightEnableChanged[i] &= ~TransformDirtyBits.StateChangedVertexShader;
			LightChanged[i] &= ~TransformDirtyBits.StateChangedVertexShader;
		}

		Span<Vector4> lightState = stackalloc Vector4[5];
		for (i = 0; i < NumLights; ++i) {
			ref Light light = ref Lights[lightIndex[i]];

			float w = (light.Type == LightType.Directional) ? 1.0f : 0.0f;
			lightState[0] = new Vector4(light.Diffuse.X, light.Diffuse.Y, light.Diffuse.Z, w);

			w = (light.Type == LightType.Spot) ? 1.0f : 0.0f;
			lightState[1] = new Vector4(light.Direction.X, light.Direction.Y, light.Direction.Z, w);

			lightState[2] = new Vector4(light.Position.X, light.Position.Y, light.Position.Z, 1.0f);

			if (light.Type == LightType.Spot) {
				float stopDot = MathF.Cos(light.Theta * 0.5f);
				float stopDot2 = MathF.Cos(light.Phi * 0.5f);
				float ooDot = (stopDot > stopDot2) ? 1.0f / (stopDot - stopDot2) : 0.0f;
				lightState[3] = new Vector4(light.Falloff, stopDot, stopDot2, ooDot);
			}
			else {
				lightState[3] = new Vector4(0, 1, 1, 1);
			}

			lightState[4] = new Vector4(light.Attenuation0, light.Attenuation1, light.Attenuation2, 0.0f);

			SetVertexShaderConstant(VertexShaderConst.Lights + i * 5, MemoryMarshal.Cast<Vector4, float>(lightState), 5);
		}

		vertexShared.LightCount = NumLights;

		Span<float> lightEnable = stackalloc float[MAX_NUM_LIGHTS];
		lightEnable.Clear();
		for (i = 0; i < NumLights; ++i)
			lightEnable[i] = 1.0f;

		vertexShared.LightEnabled = new(lightEnable[0], lightEnable[1], lightEnable[2], lightEnable[3]);
	}

	public void GetDX9LightState(out LightState state) {
		state = default;

		Span<Vector4> cube = AmbientLightCube;
		bool ambientIsZero = true;
		for (int i = 0; i < 6; ++i) {
			if (cube[i].X != 0.0f || cube[i].Y != 0.0f || cube[i].Z != 0.0f) {
				ambientIsZero = false;
				break;
			}
		}
		state.AmbientLight = !ambientIsZero;

		Assert(RenderMesh != null);
		Assert(NumLights <= 4);

		if (HardwareConfig.SupportsPixelShaders_2_b())
			Assert(NumLights <= MAX_NUM_LIGHTS);
		else
			Assert(NumLights <= MAX_NUM_LIGHTS - 2);

		state.NumLights = NumLights;
		state.StaticLightVertex = RenderMesh?.HasColorMesh() ?? false;
		state.StaticLightTexel = false;
	}

	public void SetVertexShaderStateAmbientLightCube() {
		if ((CachedAmbientLightCube & (int)TransformDirtyBits.StateChangedVertexShader) != 0) {
			Span<Vector4> cube = AmbientLightCube;
			SetVertexShaderConstant(VertexShaderConst.AmbientLight, MemoryMarshal.Cast<Vector4, float>(cube), 6);
			CachedAmbientLightCube &= ~(int)TransformDirtyBits.StateChangedVertexShader;
		}
	}

	Vector4 WorldSpaceCameraPosition = new(0, 0, 0.01f, 0); // Don't let z be zero, as some pixel shaders will divide by this

	private void CacheWorldSpaceCameraPosition() {
		ref Matrix4x4 view = ref Matrices[(int)MaterialMatrixMode.View];

		WorldSpaceCameraPosition.X = -(view.M41 * view.M11 + view.M42 * view.M12 + view.M43 * view.M13);
		WorldSpaceCameraPosition.Y = -(view.M41 * view.M21 + view.M42 * view.M22 + view.M43 * view.M23);
		WorldSpaceCameraPosition.Z = -(view.M41 * view.M31 + view.M42 * view.M32 + view.M43 * view.M33);
		WorldSpaceCameraPosition.W = 1.0f;

		if (MathF.Abs(WorldSpaceCameraPosition.Z) <= 0.00001f)
			WorldSpaceCameraPosition.Z = 0.01f;
	}

	private void UpdateVertexShaderFogParams() {
		// todo: fog params

		Span<float> vertexShaderCameraPos = [
			WorldSpaceCameraPosition.X,
			WorldSpaceCameraPosition.Y,
			WorldSpaceCameraPosition.Z,
			0.0f, // todo: waterheight
		];

		SetVertexShaderConstant(VertexShaderConst.CameraPos, vertexShaderCameraPos);
	}

	private void InitVertexAndPixelShaders() {
		// TODO; everything before this call
		ShaderManager.ResetShaderState();
	}

	public MaterialFogMode GetSceneFogMode() {
		return SceneFogMode;
	}

	MaterialFogMode SceneFogMode = MaterialFogMode.None;

	internal IShaderUtil ShaderUtil;

	public bool InFlashlightMode() {
		return ShaderUtil.InFlashlightMode();
	}


	MeshGl46? RenderMesh;
	IMaterialInternal? Material;
	uint CombobulateShadersIfChanged() {
		uint program;
		if (pipelineChanged) {
			program = ShaderCombobulator();
			if (lastShader != program) {
				lastShader = program;
				glUseProgram(program);
			}
		}

		pipelineChanged = false;
		return lastShader;
	}

	public void RenderPass() {
		if (IsDeactivated())
			return;

		CombobulateShadersIfChanged();

		if (RenderMesh != null)
			RenderMesh.RenderPass();
		else
			MeshMgr.RenderPassWithVertexAndIndexBuffers();
	}

	Dictionary<ulong, uint> shaderCombinations = [];
	private unsafe uint ShaderCombobulator() {
		// Determines the shader program used given the current shader handles.
		// If one does not exist, it is created.
		Span<nint> hashedData = [activeVertexShader.Handle, activePixelShader.Handle];
		ulong hash;
		fixed (nint* data = hashedData)
			hash = XXH64.DigestOf(data, hashedData.Length * sizeof(nint), 0);

		if (shaderCombinations.TryGetValue(hash, out var program))
			return program; // We have already linked a program for this shader combination

		// We need to create a program then
		program = glCreateProgram();
		glAttachShader(program, (uint)activeVertexShader.Handle);
		glAttachShader(program, (uint)activePixelShader.Handle);
		glLinkProgram(program);
		// Even invalid program states should be hashed... for now.
		// Maybe a time based thing for invalid programs, to try allowing for the shader developer to recover, etc...
		shaderCombinations[hash] = program;

		if (!ShaderSystem.IsValidProgram(program, out string? error)) {
			Warning("WARNING: Shader combobulation linker error.\n");
			Warning(error);
			Warning("\n");
			return 0;
		}

		return program;
	}

	private bool IsDeactivated() {
		return DeviceState != DeviceState.OK;
	}

	public void InvalidateDelayedShaderConstants() {
		// TODO FIXME
	}

	public enum TransformType
	{
		IsIdentity = 0,
		IsCameraToWorld,
		IsGeneral
	}


	public void PushMatrix() {
		if (MatrixIsChanging()) {

		}
	}

	private bool MatrixIsChanging(TransformType type = TransformType.IsGeneral) {
		if (IsDeactivated())
			return false;

		if (type != TransformType.IsGeneral)
			return false;

		FlushBufferedPrimitivesInternal();

		return true;
	}
	public void FlushBufferedPrimitives() => FlushBufferedPrimitivesInternal();
	private void FlushBufferedPrimitivesInternal() {
		Assert(RenderMesh == null);
#if DEBUG
		//glDebugMessageInsert(GL_DEBUG_SOURCE_APPLICATION, GL_DEBUG_TYPE_MARKER, 0, GL_DEBUG_SEVERITY_LOW, "FlushBufferedPrimitivesInternal: executing...");
#endif
		MeshMgr.Flush();
#if DEBUG
		//glDebugMessageInsert(GL_DEBUG_SOURCE_APPLICATION, GL_DEBUG_TYPE_MARKER, 0, GL_DEBUG_SEVERITY_LOW, "FlushBufferedPrimitivesInternal: complete");
#endif
	}

	public void PopMatrix() {
		if (MatrixIsChanging()) {
			UpdateMatrixTransform();
		}
	}

	private void UpdateMatrixTransform() {

	}

	public void DrawMesh(IMesh imesh) {
		MeshGl46 mesh = (MeshGl46)imesh!;
		RenderMesh = mesh;
		VertexFormat vertexFormat = RenderMesh.GetVertexFormat();
		SetVertexDecl(vertexFormat, RenderMesh.HasColorMesh(), RenderMesh.HasFlexMesh(), Material!.IsUsingVertexID());
		CommitStateChanges();
		Material!.DrawMesh(VertexCompressionType.None);
		RenderMesh = null;
	}

	private void CommitStateChanges() {
		// todo

		CommitVertexShaderLighting();

	}

	[Flags]
	enum VertexDeclLookupFlags
	{
		StaticLit = 0x1,
		UsingMorph = 0x2,
		UsingFlex = 0x4
	}

	readonly record struct VertexDeclLookup(VertexFormat VertexFormat, VertexDeclLookupFlags Flags);

	readonly Dictionary<VertexDeclLookup, int> VertexDeclDict = [];
	int currentVertexDecl = -1;

	internal uint CurrentVertexDecl => currentVertexDecl > 0 ? (uint)currentVertexDecl : throw new NullReferenceException("Vertex Array Object was null");

	int FindOrCreateVertexDecl(VertexFormat fmt, bool staticLit, bool usingFlex, bool usingMorph) {
		VertexDeclLookupFlags flags = 0;
		if (staticLit)
			flags |= VertexDeclLookupFlags.StaticLit;
		if (usingMorph)
			flags |= VertexDeclLookupFlags.UsingMorph;
		if (usingFlex)
			flags |= VertexDeclLookupFlags.UsingFlex;

		VertexDeclLookup lookup = new(fmt, flags);
		if (VertexDeclDict.TryGetValue(lookup, out int found))
			return found;

		int vao = (int)glCreateVertexArray();
		glObjectLabel(GL_VERTEX_ARRAY, (uint)vao, "MaterialSystem VertexDecl");
		ConfigureVertexDecl((uint)vao, fmt, staticLit);
		VertexDeclDict[lookup] = vao;
		return vao;
	}

	private void SetVertexDecl(VertexFormat vertexFormat, bool hasColorMesh, bool hasFlexMesh, bool usingMorph) {
		int decl = FindOrCreateVertexDecl(vertexFormat, hasColorMesh, hasFlexMesh, usingMorph);

		if (decl != currentVertexDecl) {
			glBindVertexArray((uint)decl);
			currentVertexDecl = decl;
		}
	}

	static void ConfigureVertexDecl(uint vao, VertexFormat format, bool staticLit) {
		int offset = 0;

		Span<uint> bindings = stackalloc uint[64];
		int bindingsPtr = 0;

		void ConfigureAttribute(Span<uint> bindings, OpenGL_ShaderInputAttribute attr, VertexElement element, int size) {
			element.GetInformation(out int count, out VertexAttributeType type);
			int elementSize = count * (int)type.SizeOf();

			glEnableVertexArrayAttrib(vao, (uint)attr);
			// These specific checks are annoying...
			bool normalize = attr == OpenGL_ShaderInputAttribute.Color;
			if (attr == OpenGL_ShaderInputAttribute.BoneIndex)
				glVertexArrayAttribIFormat(vao, (uint)attr, count, (int)type, (uint)offset);
			else
				glVertexArrayAttribFormat(vao, (uint)attr, count, (int)type, normalize, (uint)offset);
			bindings[bindingsPtr++] = (uint)attr;
			offset += elementSize;
		}

		if ((format & VertexFormat.Position) != 0) {
			ConfigureAttribute(bindings, OpenGL_ShaderInputAttribute.Position, VertexElement.Position, 1);
			if ((format & VertexFormat.Wrinkle) != 0)
				ConfigureAttribute(bindings, OpenGL_ShaderInputAttribute.Wrinkle, VertexElement.Wrinkle, 1);
			else
				glDisableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.Wrinkle);
		}
		else {
			glDisableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.Position);
			glDisableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.Wrinkle);
		}

		if ((format & VertexFormat.BoneIndex) != 0) {
			int numBoneWeights = format.GetBoneWeightsSize();
			if (numBoneWeights > 0) {
				VertexElement boneWeightElement = VertexElement.BoneWeights1 + (numBoneWeights - 1);
				ConfigureAttribute(bindings, OpenGL_ShaderInputAttribute.BoneWeights, boneWeightElement, numBoneWeights);
			}
			else
				glDisableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.BoneWeights);

			ConfigureAttribute(bindings, OpenGL_ShaderInputAttribute.BoneIndex, VertexElement.BoneIndex, 1);
		}
		else {
			glDisableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.BoneIndex);
			glDisableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.BoneWeights);
		}

		if ((format & VertexFormat.Normal) != 0)
			ConfigureAttribute(bindings, OpenGL_ShaderInputAttribute.Normal, VertexElement.Normal, 1);
		else
			glDisableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.Normal);

		if ((format & VertexFormat.Color) != 0)
			ConfigureAttribute(bindings, OpenGL_ShaderInputAttribute.Color, VertexElement.Color, 1);
		else
			glDisableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.Color);

		if (staticLit) {
			VertexElement.Specular.GetInformation(out int specularCount, out VertexAttributeType specularType);
			glEnableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.Specular);
			glVertexArrayAttribFormat(vao, (uint)OpenGL_ShaderInputAttribute.Specular, specularCount, (int)specularType, true, 0);
			glVertexArrayAttribBinding(vao, (uint)OpenGL_ShaderInputAttribute.Specular, 1);
		}
		else if ((format & VertexFormat.Specular) != 0)
			ConfigureAttribute(bindings, OpenGL_ShaderInputAttribute.Specular, VertexElement.Specular, 1);
		else
			glDisableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.Specular);

		Span<VertexElement> texCoordElements = [VertexElement.TexCoord1D_0, VertexElement.TexCoord2D_0, VertexElement.TexCoord3D_0, VertexElement.TexCoord4D_0];
		for (int i = 0; i < IMesh.VERTEX_MAX_TEXTURE_COORDINATES; i++) {
			int texCoordSize = format.GetTexCoordDimensionSize(i);
			if (texCoordSize > 0) {
				VertexElement element = (VertexElement)((int)texCoordElements[texCoordSize - 1] + i);
				ConfigureAttribute(bindings, (OpenGL_ShaderInputAttribute)((int)OpenGL_ShaderInputAttribute.TexCoord0 + i), element, texCoordSize);
			}
			else
				glDisableVertexArrayAttrib(vao, (uint)((int)OpenGL_ShaderInputAttribute.TexCoord0 + i));
		}

		if ((format & VertexFormat.TangentS) != 0)
			ConfigureAttribute(bindings, OpenGL_ShaderInputAttribute.TangentS, VertexElement.TangentS, 1);
		else
			glDisableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.TangentS);

		if ((format & VertexFormat.TangentT) != 0)
			ConfigureAttribute(bindings, OpenGL_ShaderInputAttribute.TangentT, VertexElement.TangentT, 1);
		else
			glDisableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.TangentT);

		int userDataSize = format.GetUserDataSize();
		if (userDataSize > 0) {
			VertexElement element = VertexElement.UserData1 + (userDataSize - 1);
			ConfigureAttribute(bindings, OpenGL_ShaderInputAttribute.UserData, element, userDataSize);
		}
		else
			glDisableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.UserData);

		Assert(bindingsPtr < bindings.Length);
		for (int i = 0; i < bindingsPtr; i++)
			glVertexArrayAttribBinding(vao, bindings[i], 0);

		int normalOffset = (int)(VertexElement.Position.GetSize() + VertexElement.Wrinkle.GetSize());

		glEnableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.FlexPosition);
		glVertexArrayAttribFormat(vao, (uint)OpenGL_ShaderInputAttribute.FlexPosition, 4, GL_FLOAT, false, 0);
		glVertexArrayAttribBinding(vao, (uint)OpenGL_ShaderInputAttribute.FlexPosition, 2);

		glEnableVertexArrayAttrib(vao, (uint)OpenGL_ShaderInputAttribute.FlexNormal);
		glVertexArrayAttribFormat(vao, (uint)OpenGL_ShaderInputAttribute.FlexNormal, 3, GL_FLOAT, false, (uint)normalOffset);
		glVertexArrayAttribBinding(vao, (uint)OpenGL_ShaderInputAttribute.FlexNormal, 2);
	}

	public IMesh GetDynamicMesh(IMaterial material, int hwSkinBoneCount, bool buffered, IMesh? vertexOverride, IMesh? indexOverride) {
		Assert(material == null || material.IsRealTimeVersion());
		return MeshMgr.GetDynamicMesh(material, 0, hwSkinBoneCount, buffered, vertexOverride, indexOverride);
	}

	public IMesh GetDynamicMeshEx(IMaterial material, VertexFormat vertexFormat, int hwSkinBoneCount, bool buffered, IMesh? vertexOverride, IMesh? indexOverride) {
		Assert(material == null || material.IsRealTimeVersion());
		return MeshMgr.GetDynamicMesh(material, vertexFormat, hwSkinBoneCount, buffered, vertexOverride, indexOverride);
	}

	public IMesh GetFlexMesh() => MeshMgr.GetFlexMesh();

	public void Bind(IMaterial? material) {
		IMaterialInternal? matInt = (IMaterialInternal?)material;

		bool materialChanged;
		if (Material != null && matInt != null && Material.InMaterialPage() && matInt.InMaterialPage()) {
			materialChanged = (Material.GetMaterialPage() != matInt.GetMaterialPage());
		}
		else {
			materialChanged = (Material != matInt) || (Material != null && Material.InMaterialPage()) || (matInt != null && matInt.InMaterialPage());
		}

		if (materialChanged) {
			FlushBufferedPrimitives();
			Material = matInt;
		}
	}

	public void SetSkinningMatrices() {
		Assert(Material != null);

		if (numBones == 0)
			return;

		SetVertexShaderStateSkinningMatrices();
	}

	private unsafe void SetVertexShaderStateSkinningMatrices() {
		GetMatrix(MaterialMatrixMode.Model, out Matrix4x4 modelMatrix);

		Matrix4x4 transposed = Matrix4x4.Transpose(modelMatrix);
		glNamedBufferSubData(uboBones, 0, sizeof(Matrix4x4), &transposed);

		MaxBoneLoaded = 0;
	}

	public void ShadeMode(ShadeMode shadeMode) {
		// throw new NotImplementedException(); todo
	}

	public bool InEditorMode() {
		return false; // todo...?
	}

	Vector4 ToneMappingScale = new(1.0f, 1.0f, 1.0f, 1.0f);

	private float GetLightMapScaleFactor() {
		switch (HardwareConfig.GetHDRType()) {
			case HDRType.Float:
				return 1.0f;

			case HDRType.Integer:
				return 16.0f;

			case HDRType.None:
			default:
				return MathLib.GammaToLinearFullRange(2.0f);
		}
	}

	public void SetToneMappingScaleLinear(in Vector3 scale) {
		if (HardwareConfig.SupportsPixelShaders_2_0()) {
			FlushBufferedPrimitives();

			Vector3 scaleToUse = scale;
			ToneMappingScale = new(scaleToUse, ToneMappingScale.W);

			switch (HardwareConfig.GetHDRType()) {
				case HDRType.None:
					ToneMappingScale.X = 1.0f;
					ToneMappingScale.Z = 1.0f;
					break;

				case HDRType.Float:
					ToneMappingScale.X = scaleToUse.X;
					ToneMappingScale.Z = 1.0f;
					break;

				case HDRType.Integer:
					ToneMappingScale.X = scaleToUse.X;
					ToneMappingScale.Z = 16.0f;
					break;
			}

			ToneMappingScale.Y = GetLightMapScaleFactor();

			ToneMappingScale.W = MathLib.LinearToGammaFullRange(ToneMappingScale.X);
			SetPixelShaderConstant((int)PixelShaderConst.LightScale, MemoryMarshal.CreateSpan(ref ToneMappingScale.X, 4));
		}
	}

	public ref readonly Vector3 GetToneMappingScaleLinear() => ref Unsafe.As<Vector4, Vector3>(ref ToneMappingScale);

	public void SetVertexShaderConstant(int var, ReadOnlySpan<float> vec, int numConst = 1, bool force = false) {
		SetVertexShaderConstantInternal(var, vec[..Math.Min(vec.Length, numConst * 4)]);
	}

	public void SetPixelShaderConstant(int var, ReadOnlySpan<float> vec, int numConst = 1, bool force = false) {
		SetPixelShaderConstantInternal(var, vec[..Math.Min(vec.Length, numConst * 4)]);
	}

	private unsafe void SetPixelShaderConstantInternal(int var, ReadOnlySpan<float> vec) {
		int numVecs = vec.Length / 4;
		Assert(var + numVecs <= NUM_PIXEL_SHADER_CONSTANTS);

		numVecs = AdjustUpdateRange(vec, desiredPixelShaderConstants.AsSpan(var * 4), numVecs, out int skip);
		if (numVecs == 0)
			return;

		var += skip;
		ReadOnlySpan<float> src = vec.Slice(skip * 4, numVecs * 4);

		src.CopyTo(desiredPixelShaderConstants.AsSpan(var * 4));
		src.CopyTo(dynamicPixelShaderConstants.AsSpan(var * 4));

		fixed (float* pSrc = src)
			glNamedBufferSubData(uboPixelConstants, var * sizeof(Vector4), numVecs * sizeof(Vector4), pSrc);
	}

	private static int AdjustUpdateRange(ReadOnlySpan<float> src, ReadOnlySpan<float> dst, int numVecs, out int skip) {
		ReadOnlySpan<uint> srcU = MemoryMarshal.Cast<float, uint>(src);
		ReadOnlySpan<uint> dstU = MemoryMarshal.Cast<float, uint>(dst);
		skip = 0;
		int i = 0;

		while (numVecs > 0 && ((srcU[i] ^ dstU[i]) | (srcU[i + 1] ^ dstU[i + 1]) | (srcU[i + 2] ^ dstU[i + 2]) | (srcU[i + 3] ^ dstU[i + 3])) == 0) {
			i += 4; numVecs--; skip++;
		}

		if (numVecs == 0) return 0;

		int tail = i + numVecs * 4 - 4;
		while (numVecs > 1 && ((srcU[tail] ^ dstU[tail]) | (srcU[tail + 1] ^ dstU[tail + 1]) | (srcU[tail + 2] ^ dstU[tail + 2]) | (srcU[tail + 3] ^ dstU[tail + 3])) == 0) {
			tail -= 4; numVecs--;
		}

		return numVecs;
	}

	private unsafe void SetVertexShaderConstantInternal(int var, ReadOnlySpan<float> vec) {
		int numVecs = vec.Length / 4;
		Assert(var + numVecs <= NUM_VERTEX_SHADER_CONSTANTS);

		numVecs = AdjustUpdateRange(vec, desiredVertexShaderConstants.AsSpan(var * 4), numVecs, out int skip);
		if (numVecs == 0)
			return;

		var += skip;
		ReadOnlySpan<float> src = vec.Slice(skip * 4, numVecs * 4);

		src.CopyTo(desiredVertexShaderConstants.AsSpan(var * 4));
		src.CopyTo(dynamicVertexShaderConstants.AsSpan(var * 4));

		fixed (float* pSrc = src)
			glNamedBufferSubData(uboVertexConstants, var * sizeof(Vector4), numVecs * sizeof(Vector4), pSrc);
	}

	bool UsingTextureRenderTarget;
	int ViewportMaxWidth;
	int ViewportMaxHeight;
	GfxViewport Viewport;

	public void SetViewports(ReadOnlySpan<ShaderViewport> viewports) {
		Assert(viewports.Length == 1);
		if (viewports.Length != 1)
			return;

		ref GfxViewport viewport = ref Viewport;
		viewport = new();
		viewport.X = viewports[0].TopLeftX;
		viewport.Y = viewports[0].TopLeftY;
		viewport.Width = viewports[0].Width;
		viewport.Height = viewports[0].Height;
		viewport.MinZ = viewports[0].MinZ;
		viewport.MaxZ = viewports[0].MaxZ;

		int targetHeight;
		if (!UsingTextureRenderTarget) {
			GetBackBufferDimensions(out int maxWidth, out int maxHeight);

			if (viewport.Width > maxWidth && maxWidth > 0)
				viewport.Width = maxWidth;

			if (viewport.Height > maxHeight && maxHeight > 0)
				viewport.Height = maxHeight;

			targetHeight = maxHeight;
		}
		else {
			if (viewport.Width > ViewportMaxWidth)
				viewport.Width = ViewportMaxWidth;
			if (viewport.Height > ViewportMaxHeight)
				viewport.Height = ViewportMaxHeight;

			targetHeight = ViewportMaxHeight;
		}

		FlushBufferedPrimitives();
		// HACK BECAUSE SOMETHING IS REALLY WRONG: We report the right viewport width/height to OpenGL, but regardless the first couple of frames it decides that we didn't. So this hack
		// skips the first couple of loading screen frames.
		// TODO: Is this hack still needed?
		if (frame >= 2) {
			glViewport(viewport.X, targetHeight - (viewport.Y + viewport.Height), viewport.Width, viewport.Height);
			glDepthRangef(viewport.MinZ, viewport.MaxZ);
		}
		frame++;
	}
	int frame;

	public int GetViewports(Span<ShaderViewport> viewports) {
		throw new NotImplementedException();
	}

	public void PreInit(IShaderUtil shaderUtil, IServiceProvider services) {
		MeshMgr = (MeshMgr)services.GetRequiredService<IMeshMgr>()!;
		ShaderManager = services.GetRequiredService<IShaderSystem>();

		this.services = services;
		ShaderUtil = shaderUtil;
	}

	public ImageFormat GetBackBufferFormat() {
		// MaterialSystem is a prison of an architecture and I don't know how to reliably pass this information thru at the moment.
		// If other formats are used this will need to be changed. For now this will work fine.
		// It seems like IMaterialSystem::GetBackBufferFormat -> ShaderDevice::GetBackBufferFormat -> retrieve
		// PresentParameters.BackBufferFormat but what actually sets that I'm not sure yet
		return ImageFormat.RGBA8888;
	}

	public bool SupportsShadowDepthTextures() => ((HardwareConfig)HardwareConfig).SupportsShadowDepthTexturesCap;
	public ImageFormat GetShadowDepthTextureFormat() => ((HardwareConfig)HardwareConfig).ShadowDepthTextureFormat;
	public ImageFormat GetNullTextureFormat() => ((HardwareConfig)HardwareConfig).NullTextureFormat;

	public void GetBackBufferDimensions(out int width, out int height) {
		width = PresentParameters.DisplayMode.Width;
		height = PresentParameters.DisplayMode.Height;
	}

	ShaderDeviceInfo PresentParameters;
	bool ResetRenderStateNeeded = false;
	ulong CurrentFrame;
	nint TextureMemoryUsedLastFrame;

	public void BeginFrame() {
		if (ResetRenderStateNeeded) {
			ResetRenderState(false);
			ResetRenderStateNeeded = false;
		}

		++CurrentFrame;
		TextureMemoryUsedLastFrame = 0;
	}

	public void EndFrame() {
		ExportTextureList();
	}

	public bool IsDebugTextureListFresh(int numFramesAllowed = 1) {
		return DebugDataExportFrame <= CurrentFrame && (DebugDataExportFrame >= CurrentFrame - (nuint)numFramesAllowed);
	}

	public bool SetDebugTextureRendering(bool enable) {
		bool prev = DebugTexturesRendering;
		DebugTexturesRendering = enable;
		return prev;
	}

	public void EnableDebugTextureList(bool enable) {
		bEnableDebugTextureList = enable;
	}

	public void EnableGetAllTextures(bool enable) {
		DebugGetAllTextures = enable;
	}

	public KeyValues? GetDebugTextureList() {
		return DebugTextureList;
	}

	public int GetTextureMemoryUsed(TextureMemoryType type) {
		return type switch {
			TextureMemoryType.BoundLastFrame => (int)TextureMemoryUsedLastFrame,
			TextureMemoryType.TotalLoaded => (int)TextureMemoryUsedTotal,
			TextureMemoryType.EstimatePicmip1 => (int)TextureMemoryUsedPicMip1,
			TextureMemoryType.EstimatePicmip2 => (int)TextureMemoryUsedPicMip2,
			_ => 0,
		};
	}

	bool bEnableDebugTextureList;
	bool DebugGetAllTextures;
	bool DebugTexturesRendering;
	ulong DebugDataExportFrame;
	KeyValues? DebugTextureList;
	nuint TextureMemoryUsedTotal;
	nuint TextureMemoryUsedPicMip1;
	nuint TextureMemoryUsedPicMip2;
	private void ExportTextureList() {
		if (!bEnableDebugTextureList)
			return;

		DebugDataExportFrame = CurrentFrame;

		if (IsPC()) {
			if (DebugTextureList != null)
				DebugTextureList = null;

			DebugTextureList = new KeyValues("TextureList");

			TextureMemoryUsedTotal = 0;
			TextureMemoryUsedPicMip1 = 0;
			TextureMemoryUsedPicMip2 = 0;
			foreach (var texkvp in Textures) {
				var tex = texkvp.Value;
				TextureMemoryUsedTotal += tex.GetMemUsage();

				// Compute picmip memory usage
				{
					nuint numBytes = tex.GetMemUsage();

					if (tex.Levels > 1) {
						if (tex.GetWidth() > 4 || tex.GetHeight() > 4 || tex.GetDepth() > 4) {
							nuint topmipsize = (nuint)ImageLoader.GetMemRequired(tex.GetWidth(), tex.GetHeight(), tex.GetDepth(), tex.GetImageFormat(), false);
							numBytes -= topmipsize;

							TextureMemoryUsedPicMip1 += numBytes;

							if (tex.GetWidth() > 8 || tex.GetHeight() > 8 || tex.GetDepth() > 8) {
								nuint othermipsizeRatio = (nuint)(((tex.GetWidth() > 8) ? 2 : 1) * ((tex.GetHeight() > 8) ? 2 : 1) * ((tex.GetDepth() > 8) ? 2 : 1));
								nuint othermipsize = topmipsize / othermipsizeRatio;
								numBytes -= othermipsize;
							}

							TextureMemoryUsedPicMip1 += numBytes;
						}
						else {
							TextureMemoryUsedPicMip1 += numBytes;
							TextureMemoryUsedPicMip2 += numBytes;
						}
					}
					else {
						TextureMemoryUsedPicMip1 += numBytes;
						TextureMemoryUsedPicMip2 += numBytes;
					}
				}

				if (!DebugGetAllTextures) // && tex.LastBoundFrame != CurrentFrame, todo SetTextureState
					continue;

				if (tex.LastBoundFrame != CurrentFrame)
					tex.TimesBoundThisFrame = 0;

				KeyValues pSubKey = DebugTextureList.CreateNewKey();
				pSubKey.SetString("Name", tex.DebugName.String());
				pSubKey.SetString("TexGroup", tex.TextureGroupName.String());
				pSubKey.SetInt("Size", (int)tex.GetMemUsage());
				if (tex.GetCount() > 1)
					pSubKey.SetInt("Count", tex.GetCount());
				pSubKey.SetString("Format", ImageLoader.GetName(tex.GetImageFormat()));
				pSubKey.SetInt("Width", tex.GetWidth());
				pSubKey.SetInt("Height", tex.GetHeight());

				pSubKey.SetInt("BindsMax", tex.TimesBoundMax);
				pSubKey.SetInt("BindsFrame", tex.TimesBoundThisFrame);
			}
		}
	}

	public bool SetMode(IWindow window, uint adapter, in ShaderDeviceInfo info) {
		bool restoreNeeded = false;

		if (IsActive()) {
			ReleaseResources();
			// DeviceShutdown
			restoreNeeded = true;
		}

		ShaderDeviceInfo actualInfo = info;
		if (!InitDevice(window, in actualInfo))
			return false;

		if (!OnDeviceInit())
			return false;

		if (restoreNeeded)
			ReacquireResources();

		return true;
	}

	public void ChangeVideoMode(in ShaderDeviceInfo info) {
		if (!info.Windowed) {
			LauncherManager.SetWindowFullScreen(true, info.DisplayMode.Width, info.DisplayMode.Height);
		}
		else {
			if (LauncherManager.IsWindowFullScreen())
				LauncherManager.SetWindowFullScreen(false, info.DisplayMode.Width, info.DisplayMode.Height);
			else
				LauncherManager.SizeWindow(info.DisplayMode.Width, info.DisplayMode.Height);

			LauncherManager.SetWindowBordered(!info.Borderless);
		}

		SetPresentParameters(in info);
		InvokeModeChangeCallbacks();
	}

	internal IServiceProvider services;
	internal IGraphicsContext? Device;
	internal GraphicsDriver Driver;

	public bool InitDevice(IWindow window, in ShaderDeviceInfo deviceInfo) {
		IGraphicsProvider graphics = services.GetRequiredService<IGraphicsProvider>();
		SetPresentParameters(in deviceInfo);
		Device = graphics.CreateContext(in deviceInfo, window);
		if (Device == null)
			return false;

		Driver = deviceInfo.Driver;
		Device!.MakeCurrent();
		unsafe {
			if (0 != (deviceInfo.Driver & GraphicsDriver.OpenGL))
				GL_LoadExtensions(graphics.GL_LoadExtensionsPtr());
		}

		return true;
	}

	internal unsafe void GL_LoadExtensions(delegate* unmanaged[Cdecl]<byte*, void*> loadExts) {
		Import((name) => {
			byte[] data = Encoding.UTF8.GetBytes(name);
			fixed (byte* ptr = data)
				return (nint)loadExts(ptr);
		});
	}

	public bool IsActive() => Device != null;
	public bool IsUsingGraphics() => IsActive();

	private ILauncherManager LauncherManager => services.GetRequiredService<ILauncherManager>();
	public int GetCurrentAdapter() => LauncherManager.GetCurrentDisplayIndex();
	public int GetModeCount(int adapter) => LauncherManager.GetDisplayModeCount(adapter);
	public void GetModeInfo(int adapter, int mode, out ShaderDisplayMode info) => LauncherManager.GetDisplayMode(adapter, mode, out info);

	private List<ModeChangeCallbackFunc> ModeChangeCallbacks = [];
	public void AddModeChangeCallBack(ModeChangeCallbackFunc func) {
		if (!ModeChangeCallbacks.Contains(func))
			ModeChangeCallbacks.Add(func);
	}

	public void InvokeModeChangeCallbacks() {
		foreach (ModeChangeCallbackFunc func in ModeChangeCallbacks)
			func();
	}

	public void Present() {
		FlushBufferedPrimitives();
		bool validPresent = true;
		bool inMainThread = ThreadInMainThread();

		if (!inMainThread) {
			throw new Exception();
		}

		Device!.SwapBuffers();
		MeshMgr.DiscardVertexBuffers();
	}

	bool IShaderDevice.IsDeactivated() => IsDeactivated();


	MaterialMatrixMode currentMode;
	readonly Matrix4x4[] Matrices = new Matrix4x4[3];

	public void MatrixMode(MaterialMatrixMode mode) {
		currentMode = mode;
	}

	public unsafe void LoadMatrix(in Matrix4x4 m4x4) {
		if (Matrices[(int)currentMode] == m4x4)
			return;

		int szm4x4 = sizeof(Matrix4x4);
		int loc = (int)currentMode * szm4x4;
		Matrices[(int)currentMode] = m4x4;
		fixed (Matrix4x4* pMatrix = &Matrices[(int)currentMode])
			glNamedBufferSubData(uboMatrices, loc, szm4x4, pMatrix);

		if (currentMode == MaterialMatrixMode.View) {
			CacheWorldSpaceCameraPosition();
			UpdateVertexShaderFogParams();
		}
	}

	public void GetMatrix(MaterialMatrixMode matrixMode, out Matrix4x4 dst) {
		dst = Matrices[(int)matrixMode];
	}

	public void LoadIdentity() {
		LoadMatrix(Matrix4x4.Identity);
	}

	public int GetCurrentNumBones() {
		return numBones;
	}

	Dictionary<uint, Dictionary<ulong, int>> locs = [];

	public unsafe int LocateShaderUniform(ReadOnlySpan<char> name) {
		if (!activeVertexShader.IsValid()) {
			Warning("WARNING: Attempted to locate uniform on an invalid vertex shader!\n");
			return -1;
		}

		if (!activePixelShader.IsValid()) {
			Warning("WARNING: Attempted to locate uniform on an invalid pixel shader!\n");
			return -1;
		}

		// If the name starts with $, go up one
		if (name.Length > 0 && name[0] == '$')
			name = name[1..];

		// Combobulate
		uint shader = CombobulateShadersIfChanged();

		// Then get shader ID -> shader uniform lookup table
		if (!locs.TryGetValue(shader, out var lookup))
			lookup = locs[shader] = [];

		// Then compute uniform name -> hash symbol
		// and look up if we've queried for this parameter yet
		ulong hash = name.Hash();
		if (lookup.TryGetValue(hash, out int loc))
			return loc;

		Span<byte> bytes = stackalloc byte[name.Length * 2];
		int byteLen = Encoding.ASCII.GetBytes(name, bytes);
		fixed (byte* uniformName = bytes)
			lookup[hash] = loc = glGetUniformLocation(shader, uniformName);

		return loc;
	}

	public nint GetCurrentProgram() => (nint)CombobulateShadersIfChanged();
	uint GetCurrentProgramInternal() => CombobulateShadersIfChanged();

	readonly Dictionary<uint, Dictionary<int, float[]>> uniformValues = [];
	bool UniformChanged(int uniform, ReadOnlySpan<float> values, out uint program) {
		program = GetCurrentProgramInternal();

		if (!uniformValues.TryGetValue(program, out var cache))
			cache = uniformValues[program] = [];

		if (cache.TryGetValue(uniform, out float[]? last) && values.SequenceEqual(last))
			return false;

		if (last == null || last.Length != values.Length)
			cache[uniform] = last = new float[values.Length];

		values.CopyTo(last);
		return true;
	}

	public void SetShaderUniform(int uniform, int integer) {
		if (!UniformChanged(uniform, [BitConverter.Int32BitsToSingle(integer)], out uint program))
			return;
#if GL_DEBUG
		int i = glGetError();
#endif
		glProgramUniform1i(program, uniform, integer);
#if GL_DEBUG
		if ((i = glGetError()) != 0)
			AssertMsg(false, $"GL error {i}");
#endif
	}

	public void SetShaderUniform(int uniform, uint integer) {
		if (!UniformChanged(uniform, [BitConverter.UInt32BitsToSingle(integer)], out uint program))
			return;

		glProgramUniform1ui(program, uniform, integer);
	}

	public void SetShaderUniform(int uniform, float fl) {
		if (!UniformChanged(uniform, [fl], out uint program))
			return;

		glProgramUniform1f(program, uniform, fl);
	}

	public void SetShaderUniform(int uniform, ReadOnlySpan<float> flConsts) {
		if (!UniformChanged(uniform, flConsts, out uint program))
			return;

		glProgramUniform1fv(program, uniform, flConsts);
	}

	const int GL_TEXTURE_SRGB_DECODE_EXT = 0x8A48;
	const int GL_DECODE_EXT = 0x8A49;
	const int GL_SKIP_DECODE_EXT = 0x8A4A;

	bool? supportsSRGBDecode;
	private unsafe bool SupportsSRGBDecode() {
		if (supportsSRGBDecode == null) {
			supportsSRGBDecode = false;
			int count;
			glGetIntegerv(GL_NUM_EXTENSIONS, &count);
			for (uint i = 0; i < count; i++) {
				if (MemoryMarshal.CreateReadOnlySpanFromNullTerminated(glGetStringi(GL_EXTENSIONS, i)).SequenceEqual("GL_EXT_texture_sRGB_decode"u8)) {
					supportsSRGBDecode = true;
					break;
				}
			}

			if (supportsSRGBDecode == false)
				Warning("GL_EXT_texture_sRGB_decode unsupported\n");
		}

		return supportsSRGBDecode.Value;
	}

	int ForcedMinFilter;
	int ForcedMagFilter;
	readonly uint[] ForcedFilterSamplers = new uint[(int)Sampler.MaxSamplers];
	readonly bool[] ForcedFilterSamplerBound = new bool[(int)Sampler.MaxSamplers];

	public void GMOD_ForceFilterMode(bool min, int mode) {
		if (min)
			ForcedMinFilter = mode;
		else
			ForcedMagFilter = mode;
	}

	unsafe void ApplyForcedFilter(Sampler sampler, ShaderAPITextureHandle_t textureHandle) {
		int unit = (int)sampler;
		if (ForcedMinFilter == 0 && ForcedMagFilter == 0) {
			if (ForcedFilterSamplerBound[unit]) {
				glBindSampler((uint)unit, 0);
				ForcedFilterSamplerBound[unit] = false;
			}
			return;
		}

		if (ForcedFilterSamplers[unit] == 0)
			ForcedFilterSamplers[unit] = glCreateSamplers();
		uint samplerObject = ForcedFilterSamplers[unit];
		uint texture = GetGL46Texture(textureHandle);

		int value;
		foreach (int pname in (ReadOnlySpan<int>)[GL_TEXTURE_WRAP_S, GL_TEXTURE_WRAP_T, GL_TEXTURE_WRAP_R, GL_TEXTURE_MIN_FILTER, GL_TEXTURE_MAG_FILTER]) {
			glGetTextureParameteriv(texture, pname, &value);
			glSamplerParameteri(samplerObject, pname, value);
		}
		if (SupportsSRGBDecode()) {
			glGetTextureParameteriv(texture, GL_TEXTURE_SRGB_DECODE_EXT, &value);
			glSamplerParameteri(samplerObject, GL_TEXTURE_SRGB_DECODE_EXT, value);
		}
		glSamplerParameterf(samplerObject, GL_TEXTURE_MAX_ANISOTROPY, 1.0f);

		bool mipped = GetTexture(textureHandle).Levels > 1;
		switch (ForcedMinFilter) {
			case 0:
				break;
			case 1:
				glSamplerParameteri(samplerObject, GL_TEXTURE_MIN_FILTER, mipped ? GL_NEAREST_MIPMAP_LINEAR : GL_NEAREST);
				break;
			case 3:
				glSamplerParameterf(samplerObject, GL_TEXTURE_MAX_ANISOTROPY, AnisotropicLevel);
				glSamplerParameteri(samplerObject, GL_TEXTURE_MIN_FILTER, mipped ? GL_LINEAR_MIPMAP_LINEAR : GL_LINEAR);
				break;
			default:
				glSamplerParameteri(samplerObject, GL_TEXTURE_MIN_FILTER, mipped ? GL_LINEAR_MIPMAP_LINEAR : GL_LINEAR);
				break;
		}

		switch (ForcedMagFilter) {
			case 0:
				break;
			case 1:
				glSamplerParameteri(samplerObject, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
				break;
			default:
				glSamplerParameteri(samplerObject, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
				break;
		}

		glBindSampler((uint)unit, samplerObject);
		ForcedFilterSamplerBound[unit] = true;
	}

	readonly int[] LastBoundTextures = new int[(int)Sampler.MaxSamplers];
	public void BindTexture(Sampler sampler, ShaderAPITextureHandle_t textureHandle) {
		if (textureHandle == INVALID_SHADERAPI_TEXTURE_HANDLE)
			return; // TODO: can we UNSET the sampler???

		if (LastBoundTextures[(int)sampler] != textureHandle) {
			glBindTextureUnit((uint)sampler, GetGL46Texture(textureHandle));
			LastBoundTextures[(int)sampler] = textureHandle;
		}

		// D3D9 takes sRGB read from sampler state, but GL bakes it into the internal format. The same
		// texture is shared by shaders that disagree, so skip the decode when the shadow state wants linear.
		if (SupportsSRGBDecode()) {
			bool srgbRead = currentShadow != null && currentShadow.State.SamplerState[(int)sampler].SRGBReadEnable;
			int decode = srgbRead ? GL_DECODE_EXT : GL_SKIP_DECODE_EXT;
			InternalTextureInfo info = GetTexture(textureHandle);
			if (info.LastSRGBDecode != decode) {
				glTextureParameteri(GetGL46Texture(textureHandle), GL_TEXTURE_SRGB_DECODE_EXT, decode);
				info.LastSRGBDecode = decode;
			}
		}

		ApplyForcedFilter(sampler, textureHandle);
	}

	public bool CanDownloadTextures() {
		if (IsDeactivated())
			return false;

		return IsActive();
	}

	ShaderAPITextureHandle_t ModifyTextureHandle;

	public void ModifyTexture(ShaderAPITextureHandle_t textureHandle) {
		ModifyTextureHandle = textureHandle;
	}

	public ref struct TextureLoadInfo
	{
		public ShaderAPITextureHandle_t TextureHandle;
		public uint Texture;
		public int Width;
		public int Height;
		public int Copy;
		public int ZOffset;
		public int Level;
		public int CubeFaceID;
		public ImageFormat SrcFormat;
		public Span<byte> SrcData;
		public bool TextureIsLockable;
		public bool SRGB;
	}

	public void TexImageFromVTF(IVTFTexture? vtf, int vtfFrame) {
		Assert(vtf != null);
		Assert(ModifyTextureHandle != INVALID_SHADERAPI_TEXTURE_HANDLE);

		InternalTextureInfo tex = GetTexture(GetModifyTextureHandle());
		if (tex.SwitchNeeded) {
			AdvanceCurrentCopy(GetModifyTextureHandle());
			tex.SwitchNeeded = false;
		}

		TextureLoadInfo info = new();
		info.TextureHandle = GetModifyTextureHandle();
		info.Texture = GetModifyTexture();
		info.Level = 0;
		info.Copy = tex.CurrentCopy;
		info.CubeFaceID = 0;
		info.Width = 0;
		info.Height = 0;
		info.ZOffset = 0;
		info.SrcFormat = vtf.Format();
		info.SrcData = null;
		info.TextureIsLockable = (tex.Flags & InternalTextureFlags.IsLockable) != 0;
		info.SRGB = (tex.CreationFlags & CreateTextureFlags.SRGB) != 0;
		if (vtf.Depth() > 1) {
			throw new NotImplementedException("Multidepth textures not supported yet");
		}
		else if (vtf.IsCubeMap()) {
			if (HardwareConfig.SupportsCubeMaps())
				LoadCubeTextureFromVTF(in info, vtf, vtfFrame);
			else {
				info.CubeFaceID = (int)CubeMapFaceIndex.Spheremap;
				LoadTextureFromVTF(in info, vtf, vtfFrame);
			}
		}
		else {
			for (int i = 0; i < vtf.MipCount(); i++) {
				info.Level = i;
				LoadTextureFromVTF(in info, vtf, vtfFrame);
			}
		}
		SetModifyTexture(info.Texture);
	}

	private void AdvanceCurrentCopy(int textureHandle) {
		InternalTextureInfo tex = GetTexture(textureHandle);
		if (tex.NumCopies > 1) {
			if (++tex.CurrentCopy >= tex.NumCopies)
				tex.CurrentCopy = 0;

			UnbindTexture(textureHandle);
		}
	}

	private unsafe void LoadCubeTextureFromVTF(in TextureLoadInfo info, IVTFTexture vtf, int vtfFrame) {
		if (vtf.FaceCount() < 6) {
			Warning("LoadCubeTextureFromVTF: VTF dimensions do not match texture\n");
			return;
		}

		glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
		glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
		int mipCount = vtf.MipCount();
		for (int mip = 0; mip < mipCount; ++mip) {
			vtf.ComputeMipLevelDimensions(mip, out int w, out int h, out _);
			for (int face = 0; face < 6; ++face) {
				Span<byte> data = vtf.ImageData(vtfFrame, face, mip);
				if (info.SrcFormat.IsCompressed()) {
					fixed (byte* bytes = data)
						glCompressedTextureSubImage3D((uint)info.Texture, mip, 0, 0, face, w, h, 1, ImageLoader.GetGLImageInternalFormat(info.SrcFormat, info.SRGB), data.Length, bytes);
				}
				else {
					ConvertDataToAcceptableGLFormat(info.SrcFormat, data, out ImageFormat uploadFormat, out Span<byte> convertedData);
					fixed (byte* bytes = convertedData)
						glTextureSubImage3D((uint)info.Texture, mip, 0, 0, face, w, h, 1, ImageLoader.GetGLImageUploadFormat(uploadFormat), GL_UNSIGNED_BYTE, bytes);
				}
			}
		}
		glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
	}

	private unsafe void LoadTextureFromVTF(in TextureLoadInfo info, IVTFTexture vtf, int vtfFrame) {
		vtf.ComputeMipLevelDimensions(info.Level, out int w, out int h, out _);

		if (info.SrcFormat.IsCompressed()) {
			Span<byte> data = vtf.ImageData(vtfFrame, 0, info.Level);
			fixed (byte* bytes = data)
				glCompressedTextureSubImage2D((uint)info.Texture, info.Level, 0, 0, w, h, ImageLoader.GetGLImageInternalFormat(info.SrcFormat, info.SRGB), data.Length, bytes);
			// Msg("err: " + glGetErrorName() + "\n");
		}
		else {
			Span<byte> data = vtf.ImageData(vtfFrame, 0, info.Level);
			TexSubImage2D(info.Level, 0, 0, 0, 0, vtf.Width(), vtf.Height(), info.SrcFormat, 0, false, data);
		}
	}



	public unsafe ShaderAPITextureHandle_t CreateTexture(
		int width,
		int height,
		int depth,
		ImageFormat imageFormat,
		int mipCount,
		int copies,
		int flags,
		ReadOnlySpan<char> debugName,
		ReadOnlySpan<char> textureGroup) {
		ShaderAPITextureHandle_t handle = default;
		CreateTextures(new Span<int>(ref handle), width, height, depth, imageFormat, mipCount, copies, flags, debugName, textureGroup);
		return handle;
	}
	public unsafe void CreateTextures(
		Span<ShaderAPITextureHandle_t> textureHandles,
		int width,
		int height,
		int depth,
		ImageFormat imageFormat,
		int mipCount,
		int copies,
		int flags,
		ReadOnlySpan<char> debugName,
		ReadOnlySpan<char> textureGroup) {
		int count = textureHandles.Length;
		CreateTextureFlags creationFlags = (CreateTextureFlags)flags;
		if (depth == 0)
			depth = 1;

		bool isCubeMap = (creationFlags & CreateTextureFlags.Cubemap) != 0;
		bool isRenderTarget = (creationFlags & CreateTextureFlags.RenderTarget) != 0;
		bool managed = (creationFlags & CreateTextureFlags.Managed) != 0;
		bool isDepthBuffer = (creationFlags & CreateTextureFlags.DepthBuffer) != 0;
		bool isDynamic = (creationFlags & CreateTextureFlags.Dynamic) != 0;
		bool isSRGB = (creationFlags & CreateTextureFlags.SRGB) != 0;

		InternalTextureFlags setFlags = 0;
		// setFlags |= (IsPosix() || (creationFlags & (TEXTURE_CREATE_DYNAMIC | TEXTURE_CREATE_MANAGED))) ? Texture_t::IS_LOCKABLE : 0;
		setFlags |= (false || (creationFlags & (CreateTextureFlags.Dynamic | CreateTextureFlags.Managed)) != 0) ? InternalTextureFlags.IsLockable : 0;
		setFlags |= (creationFlags & CreateTextureFlags.VertexTexture) != 0 ? InternalTextureFlags.IsVertexTexture : 0;

		CreateTextureHandles(textureHandles);
		for (int i = 0; i < count; i++) {
			ShaderAPITextureHandle_t handle = textureHandles[i];
			InternalTextureInfo texture = GetTexture(textureHandles[i]);
			texture.Flags = InternalTextureFlags.IsAllocated;
			texture.DebugName = debugName;
			texture.Width = width;
			texture.Height = height;
			texture.Depth = depth;
			texture.Count = count;
			texture.CountIndex = i;

			ConvertDataToAcceptableGLFormat(imageFormat, null, out ImageFormat dstFormat, out _);
			texture.Format = dstFormat;

			texture.CreationFlags = creationFlags;
			texture.Flags |= setFlags;
			if (copies <= 1) {
				texture.NumCopies = 1;
				uint glTex = glCreateTexture(texture.DetermineGLObjectType());
				glTextureStorage2D(glTex, mipCount, ImageLoader.GetGLImageInternalFormat(dstFormat, isSRGB), width, height);
				SetLuminanceSwizzle(glTex, dstFormat);
				glObjectLabel(GL_TEXTURE, glTex, $"ShaderAPI Texture '{debugName.SliceNullTerminatedString()}' [frame {i}]");
				texture.SetTexture(glTex);
			}
			else {
				texture.NumCopies = (byte)copies;
				texture.TextureCopies = new uint[copies];
				for (int k = 0; k < copies; k++) {
					uint glTex = glCreateTexture(texture.DetermineGLObjectType());
					glTextureStorage2D(glTex, mipCount, ImageLoader.GetGLImageInternalFormat(dstFormat, isSRGB), width, height);
					SetLuminanceSwizzle(glTex, dstFormat);
					glObjectLabel(GL_TEXTURE, glTex, $"ShaderAPI Texture '{debugName.SliceNullTerminatedString()}' [frame {i} copy {k}]");
					texture.SetTexture(k, glTex);
				}
			}

			texture.CurrentCopy = 0;

			ComputeStatsInfo(textureHandles[i], isCubeMap, depth > 1);
		}
	}

	private static void SetLuminanceSwizzle(uint glTex, ImageFormat format) {
		switch (format) {
			case ImageFormat.I8:
				glTextureParameteri(glTex, GL_TEXTURE_SWIZZLE_R, GL_RED);
				glTextureParameteri(glTex, GL_TEXTURE_SWIZZLE_G, GL_RED);
				glTextureParameteri(glTex, GL_TEXTURE_SWIZZLE_B, GL_RED);
				glTextureParameteri(glTex, GL_TEXTURE_SWIZZLE_A, GL_ONE);
				break;
			case ImageFormat.IA88:
				glTextureParameteri(glTex, GL_TEXTURE_SWIZZLE_R, GL_RED);
				glTextureParameteri(glTex, GL_TEXTURE_SWIZZLE_G, GL_RED);
				glTextureParameteri(glTex, GL_TEXTURE_SWIZZLE_B, GL_RED);
				glTextureParameteri(glTex, GL_TEXTURE_SWIZZLE_A, GL_GREEN);
				break;
		}
	}

	InternalTextureInfo GetTexture(ShaderAPITextureHandle_t handle) => Textures[handle];

	private void ComputeStatsInfo(ShaderAPITextureHandle_t hTexture, bool isCubeMap, bool isVolumeTexture) {
		InternalTextureInfo textureData = GetTexture(hTexture);

		textureData.SizeBytes = 0;
		textureData.SizeTexels = 0;

		if (isCubeMap) {
			// todo: cubemap, info
		}
		else if (isVolumeTexture) {
			// todo
		}
		else {
			int numLevels = textureData.GetLevelCount();
			for (int i = 0; i < numLevels; ++i) {
				textureData.SizeBytes += (nuint)ImageLoader.GetMemRequired(textureData.Width >> i, textureData.Height >> i, 1, textureData.GetImageFormat(), false);
				textureData.SizeTexels += (textureData.Width >> i) * (textureData.Height >> i);
			}
		}
	}

	public ShaderAPITextureHandle_t CreateTextureHandle() {
		ShaderAPITextureHandle_t handle = 0;
		CreateTextureHandles(new Span<ShaderAPITextureHandle_t>(ref handle));
		return handle;
	}

	public class InternalTextureInfo
	{
		internal uint[] TextureCopies = [0];
		internal readonly uint DepthStencilSurface = 0;
		internal readonly InlineArray2<int> RenderTargetSurface;


		internal int Width;
		internal int Height;
		internal int Depth;
		internal int Levels;
		internal int Count;
		internal int CountIndex;
		internal ImageFormat Format;
		internal UtlSymbol DebugName;
		internal UtlSymbol TextureGroupName;

		public nuint SizeBytes;
		public int SizeTexels;
		internal int LastSRGBDecode = -1;
		internal ulong LastBoundFrame;
		internal int TimesBoundMax;
		internal int TimesBoundThisFrame;

		public byte NumLevels;
		public bool SwitchNeeded;
		public byte NumCopies;
		public byte CurrentCopy;

		public InternalTextureFlags Flags;
		internal CreateTextureFlags CreationFlags;

		public uint GetTexture() {
			Assert(NumCopies == 1);
			Assert(0 == (Flags & InternalTextureFlags.IsDepthStencil));
			return TextureCopies[0];
		}

		public uint GetTexture(int copy) {
			Assert(NumCopies > 1);
			Assert(0 == (Flags & InternalTextureFlags.IsDepthStencil));
			return TextureCopies[copy];
		}

		public uint[] GetTextureArray() {
			Assert(NumCopies > 1);
			Assert(0 == (Flags & InternalTextureFlags.IsDepthStencil));
			return TextureCopies;
		}

		public void SetTexture(uint tex) {
			TextureCopies[0] = tex;
		}

		public void SetTexture(int copy, uint tex) {
			TextureCopies[copy] = tex;
		}

		internal nuint GetMemUsage() {
			return SizeBytes;
		}

		internal virtual int GetWidth() => Width;
		internal virtual int GetHeight() => Height;
		internal virtual int GetDepth() => Depth;
		internal virtual int GetLevelCount() => Levels;
		internal virtual int GetCount() => Count;
		internal virtual ImageFormat GetImageFormat() => Format;

		internal int DetermineGLObjectType() {
			if ((CreationFlags & CreateTextureFlags.Cubemap) != 0)
				return GL_TEXTURE_CUBE_MAP;

			if (Depth > 1)
				return GL_TEXTURE_3D;

			return GL_TEXTURE_2D;
		}
	}

	public enum InternalTextureFlags
	{
		IsAllocated = 0x0001,
		IsDepthStencil = 0x0002,
		IsDepthStencilTexture = 0x0004,
		IsRenderable = (IsDepthStencil | IsAllocated),
		IsLockable = 0x0008,
		IsFinalized = 0x0010,
		IsFailed = 0x0020,
		CanConvertFormat = 0x0040,
		IsLinear = 0x0080,
		IsRenderTarget = 0x0100,
		IsRenderTargetSurface = 0x0200,
		IsVertexTexture = 0x0800
	}

	readonly Dictionary<ShaderAPITextureHandle_t, InternalTextureInfo> Textures = [];
	int currentTextureID = 1;
	public int AllocNewTextureID() => Interlocked.Increment(ref currentTextureID);
	public unsafe void CreateTextureHandles(Span<int> textureHandles) {
		int idxCreating = 0;
		ShaderAPITextureHandle_t hTexture;
		foreach (var texture in Textures) {
			if ((texture.Value.Flags & InternalTextureFlags.IsAllocated) == 0) {
				textureHandles[idxCreating++] = texture.Key;
				if (idxCreating >= textureHandles.Length)
					return;
			}
		}

		while (idxCreating < textureHandles.Length) {
			int id = AllocNewTextureID();
			textureHandles[idxCreating++] = id;
			Textures.Add(id, new());
		}
	}

	public ShaderAPITextureHandle_t CreateDepthTexture(ImageFormat imageFormat, int width, int height, ReadOnlySpan<char> debugName, bool texture) {
		ShaderAPITextureHandle_t handle = CreateTextureHandle();
		InternalTextureInfo tex = GetTexture(handle);
		tex.Flags = InternalTextureFlags.IsAllocated;
		tex.DebugName = debugName;
		tex.Width = width;
		tex.Height = height;
		tex.Depth = 1;
		tex.Count = 1;
		tex.Format = imageFormat;
		tex.NumCopies = 1;
		tex.CurrentCopy = 0;

		uint glTex = glCreateTexture(GL_TEXTURE_2D);
		glObjectLabel(GL_TEXTURE, glTex, $"ShaderAPI Depth Texture '{debugName}'");
		glTextureStorage2D(glTex, 1, GL_DEPTH24_STENCIL8, width, height);
		tex.SetTexture(glTex);
		return handle;
	}

	public bool IsTexture(ShaderAPITextureHandle_t handle) {
		return true; // TODO
	}

	bool TextureIsAllocated(ShaderAPITextureHandle_t texture) => Textures.ContainsKey(texture);

	public unsafe void DeleteTexture(ShaderAPITextureHandle_t handle) {
		if (!TextureIsAllocated(handle))
			return;

		UnbindTexture(handle);

		InternalTextureInfo tex = GetTexture(handle);
		uint[] copies = tex.GetTextureArray();
		fixed (uint* h = copies)
			glDeleteTextures(tex.NumCopies, h);

		Textures.Remove(handle);
	}

	public void UnbindTexture(int handle) {
		// todo
	}

	public ImageFormat GetNearestSupportedFormat(ImageFormat fmt, bool filteringRequired = true) {
		return FindNearestSupportedFormat(fmt, false, false, filteringRequired);
	}

	public ImageFormat FindNearestSupportedFormat(ImageFormat format, bool isVertexTexture, bool isRenderTarget, bool filterableRequired) {
		return format;
	}

	public int GetCurrentDynamicVBSize() {
		return (1024 + 512) * 1024; // See if it's still needed to use smaller sizes at certain points... how would this even work, I wonder
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void AssertValidTextureHandle(int textureHandle) {
		Assert(TextureIsAllocated(textureHandle));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ShaderAPITextureHandle_t GetModifyTextureHandle() => ModifyTextureHandle;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public uint GetModifyTexture() => GetGL46Texture(ModifyTextureHandle);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private uint GetGL46Texture(ShaderAPITextureHandle_t textureHandle) {
		AssertValidTextureHandle(textureHandle);
		InternalTextureInfo tex = GetTexture(textureHandle);
		if (tex.NumCopies == 1)
			return tex.GetTexture();
		else
			return tex.GetTexture(tex.CurrentCopy);
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)] public InternalTextureInfo? GetTexture() => Textures.TryGetValue(ModifyTextureHandle, out InternalTextureInfo? info) ? info : null;

	public unsafe void TexImage2D(int mip, int face, ImageFormat dstFormat, int zOffset, int width, int height, ImageFormat srcFormat, bool srcIsTiled, Span<byte> imageData) {
		var tex = GetTexture(GetModifyTextureHandle());
		TextureLoadInfo info = new();
		info.TextureHandle = GetModifyTextureHandle();
		info.Texture = GetModifyTexture();
		info.Level = mip;
		info.Copy = tex.CurrentCopy;
		info.CubeFaceID = face;
		info.Width = width;
		info.Height = height;
		info.ZOffset = zOffset;
		info.SrcFormat = srcFormat;
		info.SrcData = imageData;
		info.TextureIsLockable = (tex.Flags & InternalTextureFlags.IsLockable) != 0;
		info.SRGB = (tex.CreationFlags & CreateTextureFlags.SRGB) != 0;
		LoadTexture(ref info);
		SetModifyTexture(info.Texture);
	}

	private void LoadTexture(ref TextureLoadInfo info) {
		BlitTextureBits(ref info, 0, 0, 0);
	}

	private void BlitTextureBits(ref TextureLoadInfo info, int xOffset, int yOffset, int srcStride) {
		BlitSurfaceBits(ref info, xOffset, yOffset, srcStride);
	}

	private unsafe void BlitSurfaceBits(ref TextureLoadInfo info, int xOffset, int yOffset, int srcStride) {
		ImageFormat srcFormat = info.SrcFormat;
		Span<byte> imageData = info.SrcData;
		int width = info.Width;
		int height = info.Height;
		int mip = info.Level;

		glGetError();
		glPixelStorei(GL_UNPACK_ROW_LENGTH, srcStride / srcFormat.SizeInBytes());
		ConvertDataToAcceptableGLFormat(srcFormat, imageData, out srcFormat, out Span<byte> convertedData);
		fixed (byte* data = convertedData)
			glTextureSubImage2D((uint)info.Texture, mip, xOffset, yOffset, width >> mip, height >> mip, ImageLoader.GetGLImageUploadFormat(srcFormat), GL_UNSIGNED_BYTE, data);
		glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
		var err = glGetError();
		// System.Diagnostics.Debug.Assert(err == 0);
		if (err != 0)
			Warning($"glTextureSubImage2D error: {err}\n");
	}

	public void SetModifyTexture(uint textureID) {
		if (ModifyTextureHandle == INVALID_SHADERAPI_TEXTURE_HANDLE)
			return;

		InternalTextureInfo tex = GetTexture(ModifyTextureHandle);
		if (tex.NumCopies == 1)
			tex.SetTexture(textureID);
		else
			tex.SetTexture(tex.CurrentCopy, textureID);
	}

	public unsafe void TexSubImage2D(int mip, int face, int x, int y, int z, int width, int height, ImageFormat srcFormat, int srcStride, bool srcIsTiled, Span<byte> imageData) {
		glGetError();
		glPixelStorei(GL_UNPACK_ROW_LENGTH, srcStride / srcFormat.SizeInBytes());
		ConvertDataToAcceptableGLFormat(srcFormat, imageData, out srcFormat, out Span<byte> convertedData);
		fixed (byte* data = convertedData)
			glTextureSubImage2D(GetModifyTexture(), mip, x, y, width >> mip, height >> mip, ImageLoader.GetGLImageUploadFormat(srcFormat), GL_UNSIGNED_BYTE, data);
		glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
		var err = glGetError();
		// System.Diagnostics.Debug.Assert(err == 0);
		if (err != 0)
			Warning($"glTextureSubImage2D error: {err}\n");
	}

	readonly ThreadLocal<byte[]> tempTransformBuffers = new ThreadLocal<byte[]>(() => new byte[1024 * 1024]);
	Span<byte> GetTempTransformBuffer(ImageFormat inFormat, ImageFormat outFormat, Span<byte> inData) {
		int desiredLength = ImageLoader.SizeInBytes(outFormat) * (inData.Length / ImageLoader.SizeInBytes(inFormat));

		if (desiredLength > tempTransformBuffers.Value!.Length)
			tempTransformBuffers.Value = new byte[MathLib.CeilPow2(desiredLength)];

		return tempTransformBuffers.Value!.AsSpan()[..desiredLength];
	}
	private void ConvertDataToAcceptableGLFormat(ImageFormat inFormat, Span<byte> inData, out ImageFormat outFormat, out Span<byte> outData) {
		switch (inFormat) {
			case ImageFormat.BGR888:
				outFormat = ImageFormat.RGB888;
				break;
			default:
				outFormat = inFormat;
				outData = inData;
				return;
		}

		switch (inFormat) {
			case ImageFormat.BGR888:
				outData = GetTempTransformBuffer(inFormat, outFormat, inData);
				for (int i = 0; i < inData.Length; i += 3) {
					outData[i + 2] = inData[i + 0];
					outData[i + 1] = inData[i + 1];
					outData[i + 0] = inData[i + 2];
				}
				return;
			default:
				outFormat = inFormat;
				outData = inData;
				return;
		}
	}

	public void ReacquireResources() {
		ReacquireResourcesInternal();
	}

	int releaseResourcesCount = 0;

	private void ReacquireResourcesInternal(bool resetState = false, bool forceReacquire = false, ReadOnlySpan<char> forceReason = default) {
		if (--releaseResourcesCount != 0) {
			Warning($"ReacquireResources has no effect, now at level {releaseResourcesCount}.\n");
			DevWarning("ReacquireResources being discarded is a bug: use IsDeactivated to check for a valid device.\n");
			Assert(false);
			if (releaseResourcesCount < 0)
				releaseResourcesCount = 0;
			return;
		}

		if (resetState) {
			ResetRenderState();
		}

		RestoreShaderObjects();
		MeshMgr.RestoreBuffers();
		ShaderUtil.RestoreShaderObjects(services);
	}

	private void RestoreShaderObjects() {

	}

	public void ReleaseResources() {
		if (releaseResourcesCount++ != 0) {
			Warning($"ReleaseResources has no effect, now at level {releaseResourcesCount}.\n");
			DevWarning("ReleaseResources being discarded is a bug: use IsDeactivated to check for a valid device.\n");
			Assert(false);
			return;
		}

		// ShaderUtil.ReleaseShaderObjects();
		// MeshMgr.ReleaseBuffers();
		// ReleaseShaderObjects();
	}

	public void SetShaderUniform(IMaterialVar textureVar) {
		int uniform = LocateShaderUniform(textureVar.GetName());
		if (uniform == -1)
			return;
		switch (textureVar.GetVarType()) {
			case MaterialVarType.Float: SetShaderUniform(uniform, textureVar.GetFloatValue()); break;
			case MaterialVarType.Int: SetShaderUniform(uniform, textureVar.GetIntValue()); break;
		}
	}

	ulong lastBoardUploadHash;
	GraphicsBoardState lastBoardState;
	bool boardStateValid;
	public bool SetBoardState(in GraphicsBoardState state) {
		ulong currHash = state.Hash();
		if (currHash != lastBoardUploadHash) {
			ref GraphicsBoardState last = ref lastBoardState;
			bool force = !boardStateValid;

			if (force || state.Blending != last.Blending)
				glToggle(GL_BLEND, state.Blending);

			if (state.AlphaSeparateBlend) {
				if (force || state.SourceBlend != last.SourceBlend || state.DestinationBlend != last.DestinationBlend || state.AlphaSourceBlend != last.AlphaSourceBlend || state.AlphaDestinationBlend != last.AlphaDestinationBlend || !last.AlphaSeparateBlend)
					glBlendFuncSeparate(state.SourceBlend.GLEnum(), state.DestinationBlend.GLEnum(), state.AlphaSourceBlend.GLEnum(), state.AlphaDestinationBlend.GLEnum());

				if (force || state.BlendOperation != last.BlendOperation || state.AlphaBlendOperation != last.AlphaBlendOperation || !last.AlphaSeparateBlend)
					glBlendEquationSeparate(state.BlendOperation.GLEnum(), state.AlphaBlendOperation.GLEnum());
			}
			else {
				if (force || state.SourceBlend != last.SourceBlend || state.DestinationBlend != last.DestinationBlend || last.AlphaSeparateBlend)
					glBlendFunc(state.SourceBlend.GLEnum(), state.DestinationBlend.GLEnum());

				if (force || state.BlendOperation != last.BlendOperation || last.AlphaSeparateBlend)
					glBlendEquation(state.BlendOperation.GLEnum());
			}

			if (force || state.ColorWrite != last.ColorWrite || state.AlphaWrite != last.AlphaWrite)
				glColorMask(state.ColorWrite, state.ColorWrite, state.ColorWrite, state.AlphaWrite);

			if (force || state.DepthTest != last.DepthTest)
				glToggle(GL_DEPTH_TEST, state.DepthTest);

			if (force || state.DepthWrite != last.DepthWrite)
				glDepthMask(state.DepthWrite);

			if (force || state.DepthFunc != last.DepthFunc)
				glDepthFunc(state.DepthFunc.GLEnum());

			if (force || state.FillMode != last.FillMode)
				glPolygonMode(GL_FRONT_AND_BACK, state.FillMode.GLEnum());

			if (force || state.CullEnable != last.CullEnable)
				glToggle(GL_CULL_FACE, state.CullEnable);

			if (force || state.AlphaToCoverage != last.AlphaToCoverage)
				glToggle(GL_SAMPLE_ALPHA_TO_COVERAGE, state.AlphaToCoverage);

			if (force || state.SRGBWriteEnable != last.SRGBWriteEnable)
				glToggle(GL_FRAMEBUFFER_SRGB, state.SRGBWriteEnable);

			bool polyOffsetEnabled = state.ZBias != PolygonOffsetMode.Disable;
			bool lastPolyOffsetEnabled = last.ZBias != PolygonOffsetMode.Disable;
			if (force || polyOffsetEnabled != lastPolyOffsetEnabled || state.FillMode != last.FillMode) {
				glToggle(GL_POLYGON_OFFSET_FILL, polyOffsetEnabled && state.FillMode == ShaderPolyMode.Fill);
				glToggle(GL_POLYGON_OFFSET_LINE, polyOffsetEnabled && state.FillMode == ShaderPolyMode.Line);
				glToggle(GL_POLYGON_OFFSET_POINT, polyOffsetEnabled && state.FillMode == ShaderPolyMode.Point);
			}

			if (polyOffsetEnabled && (force || state.ZBias != last.ZBias)) {
				float factor = 0.0f;
				float units = 0.0f;

				if (state.ZBias == PolygonOffsetMode.ShadowBias) {
					factor = -1.0f;
					units = -1.0f;
				}

				glPolygonOffset(factor, units);
			}

#if DEBUG
			glDebugMessageInsert(GL_DEBUG_SOURCE_APPLICATION, GL_DEBUG_TYPE_MARKER, 0, GL_DEBUG_SEVERITY_LOW, "A board state write occured.");
#endif
			lastBoardUploadHash = currHash;
			lastBoardState = state;
			boardStateValid = true;
			return true;
		}
		return false;
	}

	public bool DoRenderTargetsNeedSeparateDepthBuffer() {
		return true;
	}

	public void EnableLinearColorSpaceFrameBuffer(bool v) {
		// I'm dealing with this later
	}

	public IShaderDevice GetShaderDevice() => this;

	public void SetRenderTargetEx(int renderTargetID, ShaderAPITextureHandle_t colorTextureHandle = -1, ShaderAPITextureHandle_t depthTextureHandle = -1) {
		FlushBufferedPrimitives();

		bool usingTextureTarget = false;

		if (colorTextureHandle == -1 && depthTextureHandle == -1) {
			if (renderTargetID == 0) {
				glBindFramebuffer(GL_FRAMEBUFFER, 0);
				UsingTextureRenderTarget = usingTextureTarget;
			}
			return;
		}

		if (colorTextureHandle >= 0)
			usingTextureTarget = true;

		if (renderTargetID == 0)
			UsingTextureRenderTarget = usingTextureTarget;

		glBindFramebuffer(GL_FRAMEBUFFER, renderFBO);

		if (UsingTextureRenderTarget && renderTargetID == 0) {
			InternalTextureInfo tex = GetTexture(depthTextureHandle < 0 ? colorTextureHandle : depthTextureHandle);
			ViewportMaxWidth = tex.Width;
			ViewportMaxHeight = tex.Height;
		}

		if (colorTextureHandle == -2)
			glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);
		else if (colorTextureHandle >= 0)
			glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, GetGL46Texture(colorTextureHandle), 0);

		if (depthTextureHandle == -2)
			glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_TEXTURE_2D, 0, 0);
		else if (depthTextureHandle >= 0)
			glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_TEXTURE_2D, GetGL46Texture(depthTextureHandle), 0);

		var status = glCheckFramebufferStatus(GL_FRAMEBUFFER);
		Assert(status == GL_FRAMEBUFFER_COMPLETE, "Framebuffer incomplete");
		// glBindFramebuffer(GL_FRAMEBUFFER, 0);
	}

	public void CopyRenderTargetToTextureEx(ShaderAPITextureHandle_t textureHandle, int renderTargetID, ref System.Drawing.Rectangle srcRect, ref System.Drawing.Rectangle dstRect) {
		if (!TextureIsAllocated(textureHandle))
			return;

		FlushBufferedPrimitives();

		InternalTextureInfo tex = GetTexture(textureHandle);

		int srcWidth, srcHeight;
		if (UsingTextureRenderTarget) {
			srcWidth = ViewportMaxWidth;
			srcHeight = ViewportMaxHeight;
		}
		else
			GetBackBufferDimensions(out srcWidth, out srcHeight);

		System.Drawing.Rectangle src = Unsafe.IsNullRef(ref srcRect) ? new(0, 0, srcWidth, srcHeight) : srcRect;
		System.Drawing.Rectangle dst = Unsafe.IsNullRef(ref dstRect) ? new(0, 0, tex.Width, tex.Height) : dstRect;

		int srcY0 = srcHeight - (src.Y + src.Height);
		int dstY0 = tex.Height - (dst.Y + dst.Height);

		glNamedFramebufferTexture(copyFBO, GL_COLOR_ATTACHMENT0, GetGL46Texture(textureHandle), 0);
		glBlitNamedFramebuffer(UsingTextureRenderTarget ? renderFBO : 0, copyFBO,
			src.X, srcY0, src.X + src.Width, srcY0 + src.Height,
			dst.X, dstY0, dst.X + dst.Width, dstY0 + dst.Height,
			GL_COLOR_BUFFER_BIT, GL_LINEAR);
		glNamedFramebufferTexture(copyFBO, GL_COLOR_ATTACHMENT0, 0, 0);
	}

	public IMesh CreateStaticMesh(VertexFormat format, ReadOnlySpan<char> textureGroup, IMaterial? material) {
		return MeshMgr.CreateStaticMesh(format, textureGroup, material);
	}

	public void DestroyStaticMesh(IMesh mesh) {
		MeshMgr.DestroyStaticMesh(mesh);
	}

	public int GetMaxVerticesToRender(IMaterial material) => MeshMgr.GetMaxVerticesToRender(material);
	public int GetMaxIndicesToRender() => MeshMgr.GetMaxIndicesToRender();

	public void TexWrap(TexCoordComponent coord, TexWrapMode wrapMode) {
		int coordinate = coord switch {
			TexCoordComponent.S => GL_TEXTURE_WRAP_S,
			TexCoordComponent.T => GL_TEXTURE_WRAP_T,
			TexCoordComponent.U => GL_TEXTURE_WRAP_R,
			_ => -1
		};

		if (coordinate == -1) {
			Warning("ShaderAPIGl46.TexWrap: unknown coord\n");
			return;
		}

		switch (wrapMode) {
			case TexWrapMode.Clamp:
				glTextureParameteri(GetModifyTexture(), coordinate, GL_CLAMP_TO_EDGE);
				break;
			case TexWrapMode.Repeat:
				glTextureParameteri(GetModifyTexture(), coordinate, GL_REPEAT);
				break;
			case TexWrapMode.Border:
				glTextureParameteri(GetModifyTexture(), coordinate, GL_CLAMP_TO_BORDER);
				break;
			default:
				Warning("ShaderAPIGl46.TexWrap: unknown wrapMode\n");
				break;
		}
	}
	IMaterialSystemHardwareConfig HardwareConfig = Singleton<IMaterialSystemHardwareConfig>();
	public void TexMinFilter(TexFilterMode mode) {
		switch (mode) {
			case TexFilterMode.Nearest:
				glTextureParameteri(GetModifyTexture(), GL_TEXTURE_MIN_FILTER, GL_NEAREST);
				break;
			case TexFilterMode.Linear:
				glTextureParameteri(GetModifyTexture(), GL_TEXTURE_MIN_FILTER, GL_LINEAR);
				break;
			case TexFilterMode.NearestMipmapNearest:
				glTextureParameteri(GetModifyTexture(), GL_TEXTURE_MIN_FILTER, GL_NEAREST_MIPMAP_NEAREST);
				break;
			case TexFilterMode.LinearMipmapNearest:
				glTextureParameteri(GetModifyTexture(), GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_NEAREST);
				break;
			case TexFilterMode.NearestMipmapLinear:
				glTextureParameteri(GetModifyTexture(), GL_TEXTURE_MIN_FILTER, GL_NEAREST_MIPMAP_LINEAR);
				break;
			case TexFilterMode.LinearMipmapLinear:
				glTextureParameteri(GetModifyTexture(), GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
				break;
			case TexFilterMode.Anisotropic:
				glTextureParameterf(GetModifyTexture(), GL_TEXTURE_MAX_ANISOTROPY, AnisotropicLevel);
				glTextureParameteri(GetModifyTexture(), GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
				break;
			default:
				break;
		}
	}

	public void TexMagFilter(TexFilterMode mode) {
		switch (mode) {
			case TexFilterMode.Nearest:
				glTextureParameteri(GetModifyTexture(), GL_TEXTURE_MAG_FILTER, GL_NEAREST);
				break;
			case TexFilterMode.Linear:
				glTextureParameteri(GetModifyTexture(), GL_TEXTURE_MAG_FILTER, GL_LINEAR);
				break;
			case TexFilterMode.NearestMipmapNearest:
				Warning("ShaderAPIGl46.TexMagFilter: TexFilterMode.NearestMipmapNearest is invalid\n");
				break;
			case TexFilterMode.LinearMipmapNearest:
				Warning("ShaderAPIGl46.TexMagFilter: TexFilterMode.LinearMipmapNearest is invalid\n");
				break;
			case TexFilterMode.NearestMipmapLinear:
				Warning("ShaderAPIGl46.TexMagFilter: TexFilterMode.NearestMipmapLinear is invalid\n");
				break;
			case TexFilterMode.LinearMipmapLinear:
				Warning("ShaderAPIGl46.TexMagFilter: TexFilterMode.LinearMipmapLinear is invalid\n");
				break;
			case TexFilterMode.Anisotropic:
				glTextureParameteri(GetModifyTexture(), GL_TEXTURE_MAG_FILTER, GL_LINEAR);
				glTextureParameterf(GetModifyTexture(), GL_TEXTURE_MAX_ANISOTROPY_EXT, AnisotropicLevel);
				break;
			default:
				break;
		}
	}

	int MaxBoneLoaded;
	public unsafe void LoadBoneMatrix(int boneIndex, in Matrix3x4 matrix) {
		if (IsDeactivated())
			return;
		int szm4x4 = sizeof(Matrix4x4);
		int loc = (int)boneIndex * szm4x4;
		Matrix4x4 transposed = Matrix4x4.Transpose(matrix);
		glNamedBufferSubData(uboBones, loc, szm4x4, &transposed);
		if (boneIndex > MaxBoneLoaded)
			MaxBoneLoaded = boneIndex;

		if (boneIndex == 0) {
			MatrixMode(MaterialMatrixMode.Model);
			LoadMatrix(matrix);
		}
	}

	int numBones;
	public void SetNumBoneWeights(int numBones) {
		if (this.numBones != numBones) {
			FlushBufferedPrimitives();
			this.numBones = numBones;
			vertexShared.NumBones = numBones;
		}
	}

	struct LockInfo
	{
		public bool Locked;
		public int Mip;
		public int CubeID;
		public int X;
		public int Y;
		public int W;
		public int H;
		public ImageFormat Format;
		public ShaderAPITextureHandle_t Handle;
	}
	LockInfo Lock;
	// This is a rushed implementation

	byte[] lockdata = new byte[2048 * 2048];
	Memory<byte> GetTempLockBuffer(ImageFormat format, int width, int height) {
		int desiredLength = ImageLoader.SizeInBytes(format) * (width) * (height);

		if (desiredLength > lockdata.Length)
			lockdata = new byte[MathLib.CeilPow2(desiredLength)];

		Array.Clear(lockdata);
		return lockdata.AsMemory()[..desiredLength];
	}

	public unsafe bool TexLock(int level, int cubeFaceID, int xOffset, int yOffset, int width, int height, ref PixelWriter writer) {
		if (!Textures.TryGetValue(ModifyTextureHandle, out InternalTextureInfo? info))
			return false;
		if (Lock.Locked)
			return false;

		Lock.Mip = level;
		Lock.CubeID = cubeFaceID;
		Lock.X = xOffset;
		Lock.Y = yOffset;
		Lock.W = width;
		Lock.H = height;
		Lock.Handle = ModifyTextureHandle;
		Lock.Format = info.Format;

		Memory<byte> buffer = GetTempLockBuffer(info.Format, width, height);
		fixed (byte* data = buffer.Span) {
			glGetTextureSubImage(GetModifyTexture(), level, xOffset, yOffset, 0, width, height, 1, GL_RGBA, GL_UNSIGNED_BYTE, buffer.Span.Length, data);
		}
		writer.SetPixelMemory(info.Format, buffer.Span, width * ImageLoader.SizeInBytes(info.Format));
		return true;
	}

	public unsafe bool TexLock(int level, int cubeFaceID, int xOffset, int yOffset, int width, int height, ref PixelWriterMem writer) {
		if (!Textures.TryGetValue(ModifyTextureHandle, out InternalTextureInfo? info))
			return false;
		if (Lock.Locked)
			return false;

		Lock.Mip = level;
		Lock.CubeID = cubeFaceID;
		Lock.X = xOffset;
		Lock.Y = yOffset;
		Lock.W = width;
		Lock.H = height;
		Lock.Handle = ModifyTextureHandle;
		Lock.Format = info.Format;

		Memory<byte> buffer = GetTempLockBuffer(info.Format, width, height);
		fixed (byte* data = buffer.Span) {
			glGetTextureSubImage(GetModifyTexture(), level, xOffset, yOffset, 0, width, height, 1, GL_RGBA, GL_UNSIGNED_BYTE, buffer.Span.Length, data);
		}
		writer.SetPixelMemory(info.Format, buffer, width * ImageLoader.SizeInBytes(info.Format));
		return true;
	}

	public unsafe void TexUnlock() {
		glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
		fixed (byte* data = lockdata)
			glTextureSubImage2D(GetGL46Texture(Lock.Handle), Lock.Mip, Lock.X, Lock.Y, Lock.W, Lock.H, ImageLoader.GetGLImageUploadFormat(Lock.Format), GL_UNSIGNED_BYTE, data);
		Lock = default;
	}

	public void BindStandardTexture(Sampler sampler, StandardTextureId id) {
		ShaderUtil.BindStandardTexture(sampler, id);
	}

	public ref readonly FlashlightState GetFlashlightState(out Matrix4x4 worldToTexture) {
		worldToTexture = FlashlightWorldToTexture;
		return ref FlashlightState;
	}

	public ref readonly FlashlightState GetFlashlightStateEx(out Matrix4x4 worldToTexture, out ITexture? flashlightDepthTexture) {
		worldToTexture = FlashlightWorldToTexture;
		flashlightDepthTexture = FlashlightDepthTexture;
		return ref FlashlightState;
	}

	public bool IsHWMorphingEnabled() {
		throw new NotImplementedException();
	}

	public void GetWorldSpaceCameraPosition(Span<float> eyePos) {
		eyePos[0] = WorldSpaceCameraPosition.X;
		eyePos[1] = WorldSpaceCameraPosition.Y;
		eyePos[2] = WorldSpaceCameraPosition.Z;
		eyePos[3] = WorldSpaceCameraPosition.W;
	}

	public int GetPixelFogCombo() {
		// throw new NotImplementedException();
		return (int)MaterialFogMode.None; // TODO!
	}

	public bool ShouldWriteDepthToDestAlpha() =>
		HardwareConfig.SupportsPixelShaders_2_b() &&
		(SceneFogMode != MaterialFogMode.LinearBelowFogZ) &&
		(GetIntRenderingParameter(RenderParamInt.WriteDepthToDestAlpha) != 0);

	public void MarkUnusedVertexFields(uint flags, Span<bool> unusedTexCoords) {
		throw new NotImplementedException();
	}

	public void ExecuteCommandBuffer(ICommandStorageBuffer storage) {
		ExecuteCommandBuffer(storage, storage.Base());
	}

	private void ExecuteCommandBuffer(ICommandStorageBuffer storage, Span<byte> cmdBuf) {
		while (true) {
			int offset = 0;

			while (true) {
				CommandBufferCommand cmd = (CommandBufferCommand)MemoryMarshal.Read<int>(cmdBuf[offset..]);
				switch (cmd) {
					case CommandBufferCommand.End:
						return;

					case CommandBufferCommand.Jump: {
							int reference = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
							storage = storage.Reference(reference);
							cmdBuf = storage.Base();
							offset = 0;
							continue;
						}

					case CommandBufferCommand.Jsr: {
							int reference = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
							ExecuteCommandBuffer(storage.Reference(reference));
							offset += 8;
							break;
						}

					case CommandBufferCommand.SetPixelShaderFloatConst: {
							int firstReg = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
							int numRegs = MemoryMarshal.Read<int>(cmdBuf[(offset + 8)..]);

#if DEBUG
							Assert(numRegs > 0);
							Assert(offset + 12 + numRegs * 16 <= cmdBuf.Length);
#endif

							Span<float> values = MemoryMarshal.Cast<byte, float>(cmdBuf.Slice(offset + 12, numRegs * 16));

							SetPixelShaderConstantInternal(firstReg, values);

							offset += 12 + numRegs * 16;
							break;
						}

					case CommandBufferCommand.SetVertexShaderFloatConst: {
							int firstReg = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
							int numRegs = MemoryMarshal.Read<int>(cmdBuf[(offset + 8)..]);

#if DEBUG
							Assert(numRegs > 0);
							Assert(offset + 12 + numRegs * 16 <= cmdBuf.Length);
#endif

							Span<float> values = MemoryMarshal.Cast<byte, float>(cmdBuf.Slice(offset + 12, numRegs * 16));

							SetVertexShaderConstantInternal(firstReg, values);

							offset += 12 + numRegs * 16;
							break;
						}

					case CommandBufferCommand.SetPixelShaderFogParams: {
							int reg = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
							// SetPixelShaderFogParams(reg);
							offset += 8;
							break;
						}

					case CommandBufferCommand.StoreEyePosInPsConst: {
							int reg = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
							SetPixelShaderConstantInternal(reg, MemoryMarshal.CreateSpan(ref WorldSpaceCameraPosition.X, 4));
							offset += 8;
							break;
						}

					case CommandBufferCommand.CommitPixelShaderLighting: {
							int reg = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
							CommitPixelShaderLighting(reg);
							offset += 8;
							break;
						}

					case CommandBufferCommand.SetPixelShaderStateAmbientLightCube: {
							int reg = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
							SetPixelShaderConstantInternal(reg, MemoryMarshal.CreateSpan(ref AmbientLightCube[0].X, 24));
							offset += 8;
							break;
						}

					case CommandBufferCommand.SetAmbientCubeDynamicStateVertexShader: {
							SetVertexShaderStateAmbientLightCube();
							offset += 4;
							break;
						}

					case CommandBufferCommand.SetDepthFeatheringConst: {
							int reg = MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
							float scale = MemoryMarshal.Read<float>(cmdBuf[(offset + 8)..]);
							SetDepthFeatheringPixelShaderConstant(reg, scale);
							offset += 12;
							break;
						}

					case CommandBufferCommand.BindStandardTexture: {
							Sampler sampler = (Sampler)MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
							StandardTextureId texture = (StandardTextureId)MemoryMarshal.Read<int>(cmdBuf[(offset + 8)..]);
							BindStandardTexture(sampler, texture);
							offset += 12;
							break;
						}

					case CommandBufferCommand.BindShaderApiTextureHandle: {
							Sampler sampler = (Sampler)MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]);
							ShaderAPITextureHandle_t texture = (ShaderAPITextureHandle_t)MemoryMarshal.Read<nint>(cmdBuf[(offset + 8)..]);
							BindTexture(sampler, texture);
							offset += 8 + IntPtr.Size;
							break;
						}

					case CommandBufferCommand.SetPsHIndex: {
							SetPixelShaderIndex(MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]));
							offset += 8;
							break;
						}

					case CommandBufferCommand.SetVsHIndex: {
							SetVertexShaderIndex(MemoryMarshal.Read<int>(cmdBuf[(offset + 4)..]));
							offset += 8;
							break;
						}

					default:
						throw new InvalidOperationException($"Unknown command {(int)cmd}");
				}
			}
		}
	}

	public void SetDepthFeatheringPixelShaderConstant(int reg, float scale) {
		// TODO!
		// Span<float> consts = [0, 0, 0, 0];

		// consts[0] = DestAlphaDepthRange / scale;
		// consts[1] = consts[2] = consts[3] = 0.0f;

		// SetPixelShaderConstant(reg, consts);
	}

	public int GetIntRenderingParameter(RenderParamInt parm) => GetIntRenderingParameter((int)parm);

	public int GetIntRenderingParameter(int parmNumber) {
		// throw new NotImplementedException();
		return 0;// todo
	}

	public void SetPixelShaderStateAmbientLightCube(int reg, bool forceToBlack) {
		if (forceToBlack) {
			Span<Vector4> tempCube = stackalloc Vector4[6];
			SetPixelShaderConstant(reg, MemoryMarshal.Cast<Vector4, float>(tempCube), 6);
		}
		else
			SetPixelShaderConstant(reg, MemoryMarshal.Cast<Vector4, float>(MemoryMarshal.CreateSpan(ref AmbientLightCube[0], 6)), 6);
	}

	float IShaderDynamicAPI.GetLightMapScaleFactor() {
		return HardwareConfig.GetHDRType() switch {
			HDRType.Float => 1.0f,
			HDRType.Integer => 16.0f,
			_ => MathLib.GammaToLinearFullRange(2.0f),
		};
	}

	public float GetAmbientLightCubeLuminance() {
		Vector4 luminance = new(0.3f, 0.59f, 0.11f, 0.0f);
		float fLuminance = 0.0f;

		Span<Vector4> cube = AmbientLightCube;
		for (int i = 0; i < 6; i++)
			fLuminance += Vector4.Dot(luminance, cube[i]);

		return fLuminance / 6.0f;
	}

	public void AcquireThreadOwnership() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void BeginOcclusionQueryDrawing(ShaderAPIOcclusionQuery_t query) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void BeginPIXEvent(Color color, ReadOnlySpan<char> name) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void BindGeometryShader(GeometryShaderHandle geometryShader) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void BindVertexTexture(VertexTextureSampler sampler, ShaderAPITextureHandle_t textureHandle) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ClearBuffersObeyStencil(bool clearColor, bool clearDepth) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ClearBuffersObeyStencilEx(bool clearColor, bool clearAlpha, bool clearDepth) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ClearSelectionNames() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ClearSnapshots() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ClearVertexAndPixelShaderRefCounts() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ComputeVertexDescription(Span<byte> buffer, VertexFormat vertexFormat, ref MeshDesc desc) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void CopyRenderTargetToTexture(ShaderAPITextureHandle_t textureHandle) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void CopyTextureToRenderTargetEx(int renderTargetID, ShaderAPITextureHandle_t textureHandle, ref System.Drawing.Rectangle srcRect, ref System.Drawing.Rectangle dstRect) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public ShaderAPIOcclusionQuery_t CreateOcclusionQueryObject() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void CullMode(MaterialCullMode cullMode) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void DXSupportLevelChanged() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void DestroyOcclusionQueryObject(ShaderAPIOcclusionQuery_t query) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void DestroyVertexBuffers(bool exitingLevel = false) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void DisableAlphaToCoverage() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void EnableAlphaToCoverage() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void EnableClipPlane(int index, bool bEnable) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void EnableFastClip(bool enable) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void EnableHWMorphing(bool enable) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void EnableShaderShaderMutex(bool enable) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void EnableUserClipTransformOverride(bool enable) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void EndOcclusionQueryDrawing(ShaderAPIOcclusionQuery_t query) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void EndPIXEvent() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void EvictManagedResources() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void FlushHardware() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void FogEnd(float end) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void FogMaxDensity(float maxDensity) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void FogStart(float start) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ForceDepthFuncEquals(bool bEnable) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ForceHardwareSync() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public float GammaToLinear_HardwareSpecific(float gamma) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void GetFogDistances(out float start, out float end, out float fogZ) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void GetMaxToRender(IMesh mesh, bool maxUntilFlush, out int maxVerts, out int maxIndices) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public ImageFormat GetNearestRenderTargetFormat(ImageFormat fmt) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void GetSceneFogColor(out Color rgb) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void HandleDeviceLost() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public bool IsTextureResident(ShaderAPITextureHandle_t textureHandle) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public float Knob(Span<char> knobname, Span<float> setvalue = default) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void LoadSelectionName(int name) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public int OcclusionQuery_GetNumPixelsRendered(ShaderAPIOcclusionQuery_t query, bool flush = false) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void OverrideAlphaWriteEnable(bool enable, bool alphaWriteEnable) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void OverrideBlend(bool overrideEnable, bool useSeparateAlpha, int srcBlend, int destBlend, int blendFunc) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void OverrideBlendSeparateAlpha(bool overrideEnable, bool useSeparateAlpha, int srcBlend, int destBlend, int blendFunc) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void OverrideColorWriteEnable(bool overrideEnable, bool colorWriteEnable) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void OverrideDepthEnable(bool bEnable, bool bDepthEnable) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public bool OwnGPUResources(bool bEnable) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void PerformFullScreenStencilOperation() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void PopSelectionName() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void PurgeUnusedVertexAndPixelShaders() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void PushSelectionName(int name) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ReadPixels(int x, int y, int width, int height, Span<byte> data, ImageFormat dstFormat) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ReadPixels(ref System.Drawing.Rectangle srcRect, ref System.Drawing.Rectangle dstRect, Span<byte> data, ImageFormat dstFormat, int dstStride) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ReleaseThreadOwnership() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SceneFogColor3ub(byte r, byte g, byte b) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	void IShaderAPI.SceneFogMode(MaterialFogMode fogMode) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SelectionBuffer(Span<uint> buffer) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public int SelectionMode(bool selectionMode) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	int AnisotropicLevel = 1;
	public void SetAnisotropicLevel(int anisotropyLevel) {
		int maxAnisotropy = HardwareConfig.MaximumAnisotropicLevel();
		if (anisotropyLevel > maxAnisotropy || anisotropyLevel <= 1)
			anisotropyLevel = Math.Max(2, Math.Min(8, maxAnisotropy / 4));

		AnisotropicLevel = anisotropyLevel;
	}

	public void SetClipPlane(int index, ReadOnlySpan<float> plane) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetDisallowAccess(bool access) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetFastClipPlane(ReadOnlySpan<float> plane) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetFlashlightState(in FlashlightState state, in Matrix4x4 worldToTexture) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetFlexWeights(int firstWeight, ReadOnlySpan<MorphWeight> weights) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetFogZ(float fogZ) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetFullScreenTextureHandle(ShaderAPITextureHandle_t h) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetHeightClipMode(MaterialHeightClipMode heightClipMode) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetHeightClipZ(float z) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetPIXMarker(Color color, ReadOnlySpan<char> name) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetRasterState(in RasterState state) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetRenderTarget(ShaderAPITextureHandle_t colorTextureHandle = (ShaderAPITextureHandle_t)ShaderRenderTarget.Backbuffer, ShaderAPITextureHandle_t depthTextureHandle = (ShaderAPITextureHandle_t)ShaderRenderTarget.Depthbuffer) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetShadowDepthBiasFactors(float shadowSlopeScaleDepthBias, float shadowDepthBias) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SetStandardVertexShaderConstants(float fOverbright) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ShaderLock() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void ShaderUnlock() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public bool SupportsCSAAMode(int numSamples, int qualityLevel) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public bool SupportsFetch4() {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public bool SupportsMSAAMode(int nMSAAMode) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void SyncToken(ReadOnlySpan<char> pToken) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void TexSetPriority(int priority) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void UserClipTransform(in Matrix4x4 worldToView) {
		throw new NotImplementedException("Incomplete port of IShaderAPI");
	}

	public void BindStandardVertexTexture(VertexTextureSampler sampler, StandardTextureId id) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void ClearStencilBufferRectangle(int xmin, int ymin, int xmax, int ymax, int value) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void Color3f(float r, float g, float b) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void Color3fv(ReadOnlySpan<float> color) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void Color3ub(byte r, byte g, byte b) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void Color3ubv(ReadOnlySpan<byte> color) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void Color4f(float r, float g, float b, float a) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void Color4fv(ReadOnlySpan<float> color) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void Color4ub(byte r, byte g, byte b, byte a) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void Color4ubv(ReadOnlySpan<byte> color) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public double CurrentTime() {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void DisableTextureTransform(TextureStage textureStage) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void ExecuteCommandBuffer(Span<byte> cmdBuffer) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void GMOD_SamplerBorderClamp(Sampler sampler) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public MorphFormatFlags GetBoundMorphFormat() {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void GetCurrentColorCorrection(out ShaderColorCorrectionInfo info) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public MaterialFogMode GetCurrentFogType() {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public int GetCurrentLightCombo() {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void GetDXLevelDefaults(out GraphicsDriver max, out GraphicsDriver recommended) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public float GetFloatRenderingParameter(int parmNumber) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public ref readonly LightDesc GetLight(int lightNum) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void GetLightmapDimensions(out int w, out int h) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public int GetNumActiveDeformations() {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public int GetPackedDeformationInformation(int maskOfUnderstoodDeformations, Span<float> constantValuesOut, int bufferSize, int maximumDeformations, out int numDefsOut) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public ITexture? GetRenderTargetEx(int renderTargetID) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void GetSceneFogColor(Span<byte> rgb) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void GetStandardTextureDimensions(out int width, out int height, StandardTextureId id) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public Vector3 GetVectorRenderingParameter(int parmNumber) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public ref MeshBuilder GetVertexModifyBuilder() {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void LoadCameraToWorld() {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void MultMatrix(in Matrix4x4 m) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void MultMatrixLocal(in Matrix4x4 m) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void Ortho(double left, double right, double bottom, double top, double zNear, double zFar) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void PerspectiveOffCenterX(double fovx, double aspect, double zNear, double zFar, double bottom, double top, double left, double right) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void PerspectiveX(double fovx, double aspect, double zNear, double zFar) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void PickMatrix(int x, int y, int width, int height) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void PopDeformation() {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void PushDeformation(in DeformationBase deformation ) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void Rotate(float angle, float x, float y, float z) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void Scale(float x, float y, float z) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void ScaleXY(float x, float y) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void SetBooleanPixelShaderConstant(int var, ReadOnlySpan<bool> vec, bool force = false ) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void SetBooleanVertexShaderConstant(int var, ReadOnlySpan<bool> vec, bool force = false ) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void SetBumpEnvMatrix(TextureStage textureStage, float m00, float m01, float m10, float m11) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void SetFloatRenderingParameter(int parmNumber, float value) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void SetIntRenderingParameter(int parmNumber, int value) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void SetIntegerPixelShaderConstant(int var, ReadOnlySpan<int> vec, bool force = false ) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void SetIntegerVertexShaderConstant(int var, ReadOnlySpan<int> vec, bool force = false ) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void SetPSNearAndFarZ(int reg) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void SetPixelShaderFogParams(int reg) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void SetTextureTransformDimension(TextureStage textureStage, int dimension, bool projected) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void SetVectorRenderingParameter(int parmNumber, in Vector3 value) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}

	public void Translate(float x, float y, float z) {
		throw new NotImplementedException("Incomplete port of IShaderDynamicAPI");
	}
}
