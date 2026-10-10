using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Source.Common.Engine;

public enum StudioRenderFlags
{
	DrawEntireModel = 0,
	DrawOpaqueOnly = 0x01,
	DrawTranslucentOnly = 0x02,
	DrawGroupMask = 0x03,
	DrawNoFlexes = 0x04,
	DrawStaticLighting = 0x08,
	DrawAccurateTime = 0x10,
	DrawNoShadows = 0x20,
	DrawGetPerfStats = 0x40,
	DrawWireframe = 0x80,
	DrawItemBlink = 0x100,
	ShadowDepthTexture = 0x200,
	SSAODepthTexture = 0x1000,
	GenerateStats = 0x8000,
}

public struct DrawModelState
{
	public StudioHeader? StudioHdr;
	public StudioHWData StudioHWData;
	public IClientRenderable? Renderable;
	public Matrix3x4 ModelToWorld;
	public StudioRenderFlags DrawFlags;
	public int LOD;
}

public struct ModelRenderInfo
{
	public Vector3 Origin;
	public QAngle Angles;
	public IClientRenderable? Renderable;
	public Model? Model;
	public Matrix3x4 ModelToWorld;
	public Matrix3x4? LightingOffset;
	public Vector3? LightingOrigin;
	public StudioFlags Flags;
	public int EntityIndex;
	public int Skin;
	public int Body;
	public int HitboxSet;
	public ModelInstanceHandle_t Instance;
}

public interface IModelRender
{
	ModelInstanceHandle_t CreateInstance(IClientRenderable renderable, LightCacheHandle_t? cache = null);
	void DestroyInstance(ModelInstanceHandle_t modelInstance);
	int DrawModel(StudioFlags flags, IClientRenderable? renderable, ModelInstanceHandle_t instance, int entityIndex, Model? model, in Vector3 origin, in QAngle angles, int skin, int body, int hitboxset, Matrix3x4? modelToWorld = null, Matrix3x4? lightingOffset = null);
	int DrawModelEx(ref ModelRenderInfo info);
	void SetStaticLighting(ModelInstanceHandle_t handle, LightCacheHandle_t? cache);
	void SetViewTarget(StudioHdr studioHdr, int bodyIndex, in Vector3 target);
	void DrawModelExecute(ref DrawModelState state, ref ModelRenderInfo info, Span<Matrix3x4> boneToWorldArray);
	bool DrawModelSetup(ref ModelRenderInfo info, ref DrawModelState state, Span<Matrix3x4> customBoneToWorld, out Span<Matrix3x4> boneToWorldArray);
	ref Matrix4x4 SetupModelState(IClientRenderable renderable);
	bool DrawModelShadowSetup(IClientRenderable renderable, int body, int skin, ref Source.Common.DrawModelInfo info, Span<Matrix3x4> customBoneToWorld, out Span<Matrix3x4> boneToWorldOut);
	void DrawModelShadow(IClientRenderable renderable, in Source.Common.DrawModelInfo info, Span<Matrix3x4> boneToWorld);
	void SuppressEngineLighting(bool suppress);
}
