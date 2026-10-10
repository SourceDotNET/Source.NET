using Game.Client;

using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

namespace Source.Common.Client;

[Flags]
public enum RenderViewInfo
{
	Unspecified = 0,
	DrawViewmodel = 1 << 0,
	DrawHUD = 1 << 1,
	SuppressMonitorRendering = 1 << 2,
	NoPostProcess = 1 << 3
}
[Flags]
public enum DrawFlags
{
	RenderRefraction = 0x1,
	RenderReflection = 0x2,
	ClipZ = 0x4,
	ClipBelow = 0x8,
	RenderUnderwater = 0x10,
	RenderAbovewater = 0x20,
	RenderWater = 0x40,
	SSAODepthPass = 0x100,
	WaterHeight = 0x200,
	DrawSSAO = 0x400,
	DrawSkybox = 0x800,
	FudgeUp = 0x1000,
	DrawEntities = 0x2000,
	UNUSED3 = 0x4000,
	UNUSED4 = 0x8000,
	UNUSED5 = 0x10000,
	SaveGameScreenshot = 0x20000,
	ClipSkybox = 0x40000,
	ShadowDepthMap = 0x100000
}

/// <summary>
/// View setup and rendering.
/// </summary>
public interface IViewRender
{
	void Init();
	void LevelInit();
	void LevelShutdown();
	void Shutdown();
	void OnRenderStart();
	void Render(ViewRects rect);
	void RenderView(in ViewSetup view, ClearFlags clearFlags, RenderViewInfo whatToDraw);
	DrawFlags GetDrawFlags();
	void StartPitchDrift();
	void StopPitchDrift();
	Frustum GetFrustum();
	bool ShouldDrawBrushModels();
	ref ViewSetup GetPlayerViewSetup();
	ref ViewSetup GetViewSetup();
	void DisableVis();
	void SetCheapWaterStartDistance(float cheapWaterStartDistance);
	void SetCheapWaterEndDistance(float cheapWaterEndDistance);
	void GetWaterLODParams(ref float cheapWaterStartDistance, ref float cheapWaterEndDistance);
	void DriftPitch();
	void SetScreenOverlayMaterial(IMaterial? pMaterial);
	IMaterial? GetScreenOverlayMaterial();
	void WriteSaveGameScreenshot(ReadOnlySpan<char> pFilename);
	void WriteSaveGameScreenshotOfSize(ReadOnlySpan<char> pFilename, int width, int height, bool bCreatePowerOf2Padded = false, bool bWriteVTF = false);
	void QueueOverlayRenderView(in ViewSetup view, ClearFlags clearFlags, DrawFlags whatToDraw);
	float GetZNear();
	float GetZFar();
	void GetScreenFadeDistances(out float min, out float max);
	// C_BaseEntity? GetCurrentlyDrawingEntity();
	// void SetCurrentlyDrawingEntity(C_BaseEntity ent);
	bool UpdateShadowDepthTexture(ITexture? pRenderTarget, ITexture? pDepthTexture, in ViewSetup shadowView);
	void FreezeFrame(float flFreezeTime);
	C_BaseEntity? GetCurrentlyDrawingEntity();
	void SetCurrentlyDrawingEntity(C_BaseEntity? ent);
	int BuildWorldListsNumber();
}
