using Source.Common.Client;
using Source.Common.Engine;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Diagnostics;
using System.Numerics;

namespace Source.Common.Engine;

public interface IVModelInfo
{
	public static bool IsDynamicModelIndex(int modelindex) => modelindex < -1;
	public static bool IsClientOnlyModelIndex(int modelindex) => modelindex < -1 && (modelindex & 1) != 0;
	Model? GetModel(int modelindex);
	int GetModelIndex(ReadOnlySpan<char> name);
	ReadOnlySpan<char> GetModelName(Model? model);
	VCollide? GetVCollide(Model? model);
	VCollide? GetVCollide(int modelindex);
	void GetModelBounds(Model? model, out Vector3 mins, out Vector3 maxs);
	void GetModelRenderBounds(Model? model, out Vector3 mins, out Vector3 maxs);
	int GetModelFrameCount(Model? model);
	ModelType GetModelType(Model? model);
	object? GetModelExtraData(Model? model);
	bool ModelHasMaterialProxy(Model? model);
	bool IsTranslucent(Model? model);
	bool IsTranslucentTwoPass(Model? model);
	void RecomputeTranslucency(Model? model, int skin, int nBody, object? clientRenderable, float instanceAlphaModulate = 1.0f);
	int GetModelMaterialCount(Model? model);
	void GetModelMaterials(Model? model, Span<IMaterial> material);
	bool IsModelVertexLit(Model? model);
	ReadOnlySpan<char> GetModelKeyValueText(Model? model);
	// bool GetModelKeyValue(Model? model, CUtlBuffer? buf ); // supports keyvalue blocks in submodels
	// TODO: How to implement this.
	float GetModelRadius(Model? model);

	StudioHeader? FindModel(StudioHeader? studioHdr, ref object? cache, ReadOnlySpan<char> modelname);
	StudioHeader? FindModel(object? cache);
	VirtualModel? GetVirtualModel(StudioHeader? studioHdr);
	Memory<byte> GetAnimBlock(StudioHeader? studioHdr, int block);

	// Available on client only!!!
	void GetModelMaterialColorAndLighting(Model? model, in Vector3 origin, in QAngle angles, out Trace trace, out Vector3 lighting, out Vector3 matColor);
	void GetIlluminationPoint(Model? model, IClientRenderable? renderable, in Vector3 origin, in QAngle angles, out Vector3 lightingCenter);

	int GetModelContents(int modelIndex);
	StudioHeader? GetStudiomodel(Model? mod);
	int GetModelSpriteWidth(Model? model);
	int GetModelSpriteHeight(Model? model);

	// Sets/gets a map-specified fade range (client only)
	void SetLevelScreenFadeRange(float minSize, float maxSize);
	void GetLevelScreenFadeRange(out float minArea, out float maxArea);

	// Sets/gets a map-specified per-view fade range (client only)
	void SetViewScreenFadeRange(float flMinSize, float flMaxSize);

	// Computes fade alpha based on distance fade + screen fade (client only)
	byte ComputeLevelScreenFade(in Vector3 absOrigin, float radius, float fadeScale);
	byte ComputeViewScreenFade(in Vector3 absOrigin, float radius, float fadeScale);

	// both client and server
	int GetAutoplayList(StudioHeader studioHdr, out ReadOnlySpan<short> autoplayList);

	// Gets a  terrain collision model (creates if necessary)
	// NOTE: This may return NULL if the terrain model cannot be ized
	PhysCollide? GetCollideForVirtualTerrain(int index);

	bool IsUsingFBTexture(Model? model, int nSkin, int nBody, object? /*IClientRenderable*/  pClientRenderable);

	MDLHandle_t GetCacheHandle(Model? model);

	// Returns planes of non-nodraw brush model surfaces
	int GetBrushModelPlaneCount(Model? model);
	void GetBrushModelPlane(Model? model, int nIndex, out CollisionPlane plane, out Vector3 origin);
	int GetSurfacepropsForTerrain(int index);

	// Poked by engine host system
	void OnLevelChange();

	int GetModelClientSideIndex(ReadOnlySpan<char> name);

	// Returns index of model by name, dynamically registered if not already known.
	int RegisterDynamicModel(ReadOnlySpan<char> name, bool bClientSide);

	bool IsDynamicModelLoading(int modelIndex);

	void AddRefDynamicModel(int modelIndex);
	void ReleaseDynamicModel(int modelIndex);

	// Registers callback for when dynamic model has finished loading.
	// Automatically adds reference, pair with ReleaseDynamicModel.
	bool RegisterModelLoadCallback(int modelindex, IModelLoadCallback callback, bool callImmediatelyIfLoaded = true);
	void UnregisterModelLoadCallback(int modelindex, IModelLoadCallback callback);
}

public interface IVModelInfoClient : IVModelInfo
{
	void OnDynamicModelsStringTableChange(int stringIndex, ReadOnlySpan<char> str, object? data);
	// For tools only!
	Model? FindOrLoadModel(ReadOnlySpan<char> name);
}
