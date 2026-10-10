using Source.Common.Bitmap;
using Source.Common.Engine;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;

namespace Source.Common;

public static class MaterialVertexFormat {
	public static readonly VertexFormat SkinnedModel = VertexFormat.Position | VertexFormat.Color | VertexFormat.Normal | VertexFormat.TexCoord2D_0 | VertexExts.GetBoneWeight(2) | VertexFormat.BoneIndex | VertexExts.GetUserDataSize(4);
	public static readonly VertexFormat Model = VertexFormat.Position | VertexFormat.Color | VertexFormat.Normal | VertexFormat.TexCoord2D_0 | VertexExts.GetUserDataSize(4);
	public static readonly VertexFormat Color = VertexFormat.Specular;
}

public struct ColorTexelsInfo {
	public int Width;
	public int Height;
	public int MipmapCount;
	public ImageFormat ImageFormat;
	public int ByteCount;
	public byte[]? TexelData;
}

public struct ColorMeshInfo {
	public IMesh? Mesh;
	public IPooledVBAllocator? PooledVBAllocator;
	public int VertOffsetInBytes;
	public int NumVerts;
	public ITexture? Lightmap;
	public ColorTexelsInfo? LightmapData;
}

public struct DrawModelInfo {
	public StudioHeader StudioHdr;
	public StudioHWData HardwareData;
	public int Skin;
	public int Body;
	public int HitboxSet;
	public object? ClientEntity;
	public int Lod;
	public ColorMeshInfo[]? ColorMeshes;
	public bool StaticLighting;
	public InlineArray6<Vector3> AmbientCube;
	public int NumLocalLights;
	public InlineArray4<LightDesc> LocalLightDescs;
}

public struct StudioRenderConfig {
	public float EyeShiftX;
	public float EyeShiftY;
	public float EyeShiftZ;
	public float EyeSize;
	public float EyeGlintPixelWidthLODThreshold;
	public int MaxDecalsPerModel;
	public int DrawEntities;
	public int Skin;
	public int FullBright;
	public bool EyeMove;
	public bool SoftwareSkin;
	public bool NoHardware;
	public bool NoSoftware;
	public bool Teeth;
	public bool Eyes;
	public bool Flex;
	public bool Wireframe;
	public bool DrawNormals;
	public bool DrawTangentFrame;
	public bool DrawZBufferedWireframe;
	public bool SoftwareLighting;
	public bool SupportsVertexAndPixelShaders;
	public bool ShowEnvCubemapOnly;
	public bool WireframeDecals;
	public bool EnableHWMorph;
	public bool StatsMode;
}

public enum OverrideType
{
	Normal,
	BuildShadows,
	DepthWrite,
	SSAODepthWrite
}

public struct DrawModelResults {
	public int ActualTriCount;
	public int TextureMemoryBytes;
	public int NumHardwareBones;
	public int NumBatches;
	public int NumMaterials;
	public int LODUsed;
	public float LODMetric;
}

public interface IStudioRender {
	void BeginFrame();
	void EndFrame();
	void UpdateConfig(in StudioRenderConfig config);
	bool LoadModel(StudioHeader studioHDR, Memory<byte> vtxData, StudioHWData hardwareData);
	void UnloadModel(StudioHWData hardwareData);

	int GetMaterialList(StudioHeader studioHDR, Span<IMaterial> materials);
	int GetMaterialListFromBodyAndSkin(MDLHandle_t studio, int skin, int body, Span<IMaterial> outputMaterials);
	Span<Matrix3x4> LockBoneMatrices(int boneCount);
	void UnlockBoneMatrices();
	void LockFlexWeights(int weightCount, out Span<float> flexWeights);
	void LockFlexWeights(int weightCount, out Span<float> flexWeights, out Span<float> flexDelayedWeights);
	void UnlockFlexWeights();
	void DrawModel(ref DrawModelResults results, ref DrawModelInfo info, Span<Matrix3x4> boneToWorld, Span<float> flexWeights, Span<float> flexDelayedWeights, in Vector3 modelOrigin, StudioRenderFlags flags = StudioRenderFlags.DrawEntireModel);
	void SetViewState(in Vector3 currentViewOrigin, in Vector3 currentViewRight, in Vector3 currentViewUp, in Vector3 currentViewForward);
	void SetColorModulation(Vector3 r_colormod);
	void SetAlphaModulation(float r_blend);
	void SetEyeViewTarget(StudioHeader? studioHdr, int bodyPart, in Vector3 viewtarget);
	void ForcedMaterialOverride(IMaterial? newMaterial, OverrideType overrideType = OverrideType.Normal);
	int ComputeModelLod(StudioHWData hardwareData, float unitSphereSize);

	int GetNumAmbientLightSamples();
	ReadOnlySpan<Vector3> GetAmbientLightDirections();
	void SetAmbientLightColors(ReadOnlySpan<Vector3> ambientOnlyColors);
	void SetLocalLights(int lightCount, ReadOnlySpan<LightDesc> lights);
	void ComputeLighting(ReadOnlySpan<Vector3> ambient, int lightCount, Span<LightDesc> lights, in Vector3 pt, in Vector3 normal, out Vector3 lighting);
	void ComputeLightingConstDirectional(ReadOnlySpan<Vector3> ambient, int lightCount, Span<LightDesc> lights, in Vector3 pt, in Vector3 normal, out Vector3 lighting, float directionalAmount);
}
