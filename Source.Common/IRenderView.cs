using Source.Common.Engine;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Engine;

using System.Numerics;

namespace Source.Common;

public interface IWorldRenderList : IRefCounted
{

}

public struct WorldListInfo
{
	public int ViewFogVolume;
	public int LeafCount;
	public List<LeafIndex_t> LeafList;
	public List<LeafFogVolume_t> LeafFogVolume;
}

public struct VisibleFogVolumeInfo
{
	public int VisibleFogVolume;
	public int VisibleFogVolumeLeaf;
	public bool EyeInFogVolume;
	public float DistanceToWater;
	public float WaterHeight;
	public IMaterial? FogVolumeMaterial;
}

public enum DrawBrushModelMode
{
	DrawAll = 0,
	DrawOpaqueOnly,
	DrawTranslucentOnly,
}

public struct VisOverrideData
{
	public Vector3 VisOrigin;
	public float DistToAreaPortalTolerance;
}

[Flags]
public enum DrawWorldListFlags : ulong
{
	StrictlyAboveWater = 0x001,
	StrictlyUnderWater = 0x002,
	IntersectsWater = 0x004,
	WaterSurface = 0x008,
	Skybox = 0x010,
	ClipSkybox = 0x020,
	ShadowDepth = 0x040,
	Refraction = 0x080,
	Reflection = 0x100,
	SSAO = 0x800,
}

public enum MatSortGroup
{
	StrictlyAboveWater = 0,
	StrictlyUnderwater,
	IntersectsWaterSurface,
	WaterSurface,

	Max
}

public enum RenderDepthMode
{
	Normal,
	Shadow,
	SSAO,
	Override,
	Max
}

/// <summary>
/// Analog of IVRenderView
/// </summary>
public interface IRenderView
{
	public const uint VIEW_SETUP_VIS_EX_RETURN_FLAGS_USES_RADIAL_VIS = 1;
	void TouchLight(DLight light);
	void DrawBrushModel(IClientEntity baseentity, Model model, in Vector3 origin, in QAngle angles);
	void DrawBrushModelEx(IClientEntity baseentity, Model model, in Vector3 origin, in QAngle angles, DrawBrushModelMode mode);
	void DrawIdentityBrushModel(IWorldRenderList list, Model model);
	void DrawBrushModelShadow(IClientRenderable renderable);
	void VGui_Paint(PaintMode mode);
	void Push2DView(ViewSetup view, ClearFlags flags, ITexture? renderTarget, Frustum frustumPlanes);
	void PopView(Frustum frustumPlanes);
	void SetMainView(in Vector3 origin, in QAngle angles);
	void SceneBegin();
	void SceneEnd();
	void ViewDrawFade(Span<byte> color, IMaterial? fadeMaterial);
	void Draw3DDebugOverlays();
	void ViewSetupVisEx(bool novis, ReadOnlySpan<Vector3> origins, out uint visFlags);
	Span<byte> GetAreaBits();
	void SetAreaState(ReadOnlySpan<byte> areaBits, ReadOnlySpan<byte> areaPortalBits);
	IWorldRenderList? CreateWorldList();
	void BuildWorldLists(IWorldRenderList? list, ref WorldListInfo info, int forceViewLeaf, ReadOnlySpan<VisOverrideData> visData, bool shadowDepth, Span<float> reflectionWaterHeight);
	void DrawWorldLists(IWorldRenderList? list, uint flags, float waterZAdjust);
	void DrawTranslucentSurfaces(IWorldRenderList? list, int sortIndex, uint flags, bool shadowDepth);
	bool LeafContainsTranslucentSurfaces(IWorldRenderList? list, int sortIndex, uint flags);
	void GetVisibleFogVolume(in Vector3 eyePoint, ref VisibleFogVolumeInfo info);
	void SetFogVolumeState(int fogVolume, bool useHeightFog);
	bool DoesBoxIntersectWaterVolume(in Vector3 mins, in Vector3 maxs, int leafWaterDataID);
	void BeginUpdateLightmaps();
	void EndUpdateLightmaps();
	void Push3DView(in ViewSetup viewRender, ClearFlags clearFlags, ITexture? rtColor, Frustum frustum, ITexture? rtDepth);
	int GetViewEntity();
	void SetBlend(float blend);
	float GetBlend();
	Vector3 GetColorModulation();
	void SetColorModulation(ReadOnlySpan<float> mod);
	void SetColorModulation(Vector3 mod);
}
