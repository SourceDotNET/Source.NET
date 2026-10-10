using Source.Common;
using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.Physics;
using Source.Engine.Client;

using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;

namespace Source.Engine;

public abstract class ModelInfo(IFileSystem filesystem, IModelLoader modelloader, IMDLCache mdlCache) : IVModelInfoClient
{
	public abstract Model? GetModel(int modelIndex);

	protected abstract INetworkStringTable? GetDynamicModelStringTable();
	protected abstract int LookupPrecachedModelIndex(ReadOnlySpan<char> name);

	protected static int CLIENTSIDE_TO_MODEL(int i) => i >= 0 ? (-2 - (i * 2 + 1)) : -1;
	protected static int NETDYNAMIC_TO_MODEL(int i) => i >= 0 ? (-2 - (i * 2)) : -1;
	protected static int MODEL_TO_CLIENTSIDE(int i) => (i <= -2 && (i & 1) == 1) ? (-2 - i) >> 1 : -1;
	protected static int MODEL_TO_NETDYNAMIC(int i) => (i <= -2 && (i & 1) == 0) ? (-2 - i) >> 1 : -1;

	public ReadOnlySpan<char> GetModelName(Model? model) {
		if (model == null)
			return "?";

		return modelloader.GetName(model);
	}
	public bool IsTranslucentTwoPass(Model? model) {
		return model != null && (model.Flags & ModelFlag.TranslucentTwoPass) != 0;
	}
	public ModelType GetModelType(Model? model) {
		if (model == null)
			return (ModelType)(-1);

		if (model.Type == ModelType.Invalid) {
			if (ClientDynamicModels.ContainsKey(model.FileNameHandle))
				return ModelType.Studio;
			INetworkStringTable? table = GetDynamicModelStringTable();
			if (table != null && table.FindStringIndex(model.StrName) != INetworkStringTable.INVALID_STRING_INDEX)
				return ModelType.Studio;
		}

		return model.Type;
	}
	public int GetModelIndex(ReadOnlySpan<char> name) {
		if (name.IsEmpty)
			return -1;

		int index = LookupPrecachedModelIndex(name);
		if (index != -1)
			return index;

		INetworkStringTable? table = GetDynamicModelStringTable();
		if (table != null) {
			int netdyn = table.FindStringIndex(name);
			if (netdyn != INetworkStringTable.INVALID_STRING_INDEX) {
				Assert(!NetworkedDynamicModels.IsValidIndex(netdyn) || NetworkedDynamicModels[netdyn].StrName.Equals(name, StringComparison.OrdinalIgnoreCase));
				return NETDYNAMIC_TO_MODEL(netdyn);
			}
		}

		return GetModelClientSideIndex(name);
	}

	protected readonly Dictionary<FileNameHandle_t, Model> ClientDynamicModels = [];

	public int GetModelClientSideIndex(ReadOnlySpan<char> name) {
		if (ClientDynamicModels.Count != 0) {
			FileNameHandle_t file = filesystem.FindFileName(name);
			if (file != FILENAMEHANDLE_INVALID && ClientDynamicModels.TryGetValue(file, out var model)) {
				Assert(model.StrName.Equals(name, StringComparison.OrdinalIgnoreCase));
				return CLIENTSIDE_TO_MODEL((int)file); // evil cast
			}
		}

		return -1;
	}

	protected bool IsDynamicModelIndex(int modelIndex) => modelIndex < -1;
	protected Model? LookupDynamicModel(int modelIndex) {
		throw new NotImplementedException("LookupDynamicModel not yet implemented!");
	}

	public VirtualModel? GetVirtualModel(StudioHeader studioHdr) {
		return mdlCache.GetVirtualModelFast(studioHdr, studioHdr.VirtualModel);
	}

	public virtual MDLHandle_t GetCacheHandle(Model model) {
		return model.Type == ModelType.Studio ? model.Studio : MDLHANDLE_INVALID;
	}

	public Memory<byte> GetAnimBlock(StudioHeader studioHdr, int block) {
		return mdlCache.GetAnimBlock(studioHdr.VirtualModel, block);
	}
	public int GetAutoplayList(StudioHeader studioHdr, out Span<short> autoplayList) {
		MDLHandle_t handle = studioHdr.VirtualModel;
		autoplayList = mdlCache.GetAutoplayList(handle).Span;
		return autoplayList.Length;
	}

	int ModelFrameCount(Model? model) {
		int count = 1;

		if (model == null)
			return count;

		if (model.Type == ModelType.Sprite) {
			return model.Sprite.NumFrames;
		}
		else if (model.Type == ModelType.Studio) {
			count = R_StudioBodyVariations((StudioHeader?)modelloader.GetExtraData(model));
		}

		if (count < 1)
			count = 1;

		return count;
	}

	private int R_StudioBodyVariations(StudioHeader? studiohdr) {
		Span<MStudioBodyParts> pbodypart;
		int i, count;

		if (studiohdr == null)
			return 0;

		count = 1;
		pbodypart = studiohdr.BodyParts(0);

		// Each body part has nummodels variations so there are as many total variations as there
		// are in a matrix of each part by each other part
		for (i = 0; i < studiohdr.NumBodyParts; i++)
			count = count * pbodypart[i].NumModels;

		return count;
	}

	public int GetModelFrameCount(Model? model) {
		return ModelFrameCount(model);
	}

	public StudioHeader? GetStudiomodel(Model? model) {
		if (model!.Type == ModelType.Studio)
			return mdlCache.GetStudioHdr(model.Studio);

		return null;
	}

	public int GetModelSpriteWidth(Model model) {
		if (model.Type != ModelType.Sprite)
			return 0;

		return model.Sprite.Width;
	}

	public int GetModelSpriteHeight(Model model) {
		if (model.Type != ModelType.Sprite)
			return 0;

		return model.Sprite.Height;
	}

	public object? GetModelExtraData(Model model) {
		return modelloader.GetExtraData(model);
	}

	public void GetModelRenderBounds(Model? model, out Vector3 mins, out Vector3 maxs) {
		if (model == null) {
			mins = default;
			maxs = default;
			return;
		}

		switch (model.Type) {
			case ModelType.Studio: {
					StudioHeader? pStudioHdr = (StudioHeader?)modelloader.GetExtraData(model);
					Assert(pStudioHdr != null);

					if (!MathLib.VectorCompare(vec3_origin, pStudioHdr.ViewBoundingBoxMin) || !MathLib.VectorCompare(in vec3_origin, in pStudioHdr.ViewBoundingBoxMax)) {
						// clipping bounding box
						mins = pStudioHdr.ViewBoundingBoxMin;
						maxs = pStudioHdr.ViewBoundingBoxMax;
					}
					else {
						// movement bounding box
						mins = pStudioHdr.HullMin;
						maxs = pStudioHdr.HullMax;
					}
				}
				break;

			case ModelType.Brush:
				mins = model.Mins;
				maxs = model.Maxs;
				break;

			default:
				mins = default;
				maxs = default;
				break;
		}
	}

	public void OnDynamicModelsStringTableChange(int stringIndex, ReadOnlySpan<char> str, object? data) {
		throw new NotImplementedException();
	}

	public Model? FindOrLoadModel(ReadOnlySpan<char> name) {
		Model? model = GetModel(GetModelIndex(name));
		if (model != null)
			return model;

		return modelloader.GetModelForName(name, ModelLoaderFlags.ClientDLL);
	}

	public VCollide? GetVCollide(Model? model) {
		if (model == null)
			return null;

		if (model.Type == ModelType.Studio)
			return mdlcache.GetVCollide(model.Studio);

		int i = GetModelIndex(GetModelName(model));
		if (i >= 0)
			return GetVCollide(i);

		return null;
	}

	public VCollide? GetVCollide(int modelIndex) {
		// First model (index 0 )is is empty
		// Second model( index 1 ) is the world, then brushes/submodels, then players, etc.
		// So, we must subtract 1 from the model index to map modelindex to CM_ index
		// in cmodels, 0 is the world, then brushes, etc.
		if (modelIndex < g_MaxModels) {
			Model? model = GetModel(modelIndex);
			if (model != null) {
				switch (model.Type) {
					case ModelType.Brush:
						return CM.GetVCollide(modelIndex - 1);
					case ModelType.Studio: {
							VCollide? col = mdlcache.GetVCollide(model.Studio);
							return col;
						}
				}
			}
			else {
				// we may have the cmodels loaded and not know the model/mod->type yet
				return CM.GetVCollide(modelIndex - 1);
			}
		}
		return null;
	}

	public void GetModelBounds(Model? model, out Vector3 mins, out Vector3 maxs) {
		mins = model!.Mins;
		maxs = model!.Maxs;
	}

	public bool ModelHasMaterialProxy(Model? model) => model != null && (model.Flags & ModelFlag.MaterialProxy) != 0;

	public bool IsTranslucent(Model? model) => model != null && (model.Flags & ModelFlag.Translucent) != 0;

	public void RecomputeTranslucency(Model? model, int skin, int nBody, object? clientRenderable, float instanceAlphaModulate = 1) {
		throw new NotImplementedException();
	}

	public int GetModelMaterialCount(Model? model) {
		throw new NotImplementedException();
	}

	public void GetModelMaterials(Model? model, Span<IMaterial> material) {
		throw new NotImplementedException();
	}

	public bool IsModelVertexLit(Model? model) {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetModelKeyValueText(Model? model) {
		throw new NotImplementedException();
	}

	public float GetModelRadius(Model? model) => model?.Radius ?? 0;

	public StudioHeader? FindModel(StudioHeader? studioHdr, ref object? cache, ReadOnlySpan<char> modelname) {
		throw new NotImplementedException();
	}

	public StudioHeader? FindModel(object? cache) {
		throw new NotImplementedException();
	}

	public void GetModelMaterialColorAndLighting(Model? model, in Vector3 origin, in QAngle angles, out Trace trace, out Vector3 lighting, out Vector3 matColor) {
		throw new NotImplementedException();
	}

	private static void R_StudioGetLightingCenter(IClientRenderable? renderable, StudioHeader studioHdr, in Vector3 origin, in QAngle angles, out Vector3 lightingOrigin) {
		MathLib.AngleMatrix(angles, origin, out Matrix3x4 matrix);
		ModelRender.R_ComputeLightingOrigin(renderable, studioHdr, matrix, out lightingOrigin);
	}

	public void GetIlluminationPoint(Model? model, IClientRenderable? renderable, in Vector3 origin, in QAngle angles, out Vector3 lightingCenter) {
		Assert(model!.Type == ModelType.Studio);
		StudioHeader? studioHdr = (StudioHeader?)GetModelExtraData(model);
		if (studioHdr != null)
			R_StudioGetLightingCenter(renderable, studioHdr, origin, angles, out lightingCenter);
		else
			lightingCenter = origin;
	}

	public int GetModelContents(int modelIndex) {
		throw new NotImplementedException();
	}

	public void SetLevelScreenFadeRange(float minSize, float maxSize) {
		throw new NotImplementedException();
	}

	public void GetLevelScreenFadeRange(out float minArea, out float maxArea) {
		throw new NotImplementedException();
	}

	public void SetViewScreenFadeRange(float flMinSize, float flMaxSize) {
		throw new NotImplementedException();
	}

	public byte ComputeLevelScreenFade(in Vector3 absOrigin, float radius, float fadeScale) => 255; // TODO!!!

	public byte ComputeViewScreenFade(in Vector3 absOrigin, float radius, float fadeScale) => 255;// TODO!!

	public PhysCollide? GetCollideForVirtualTerrain(int index) {
		throw new NotImplementedException();
	}

	public bool IsUsingFBTexture(Model? model, int nSkin, int nBody, object? pClientRenderable) {
		throw new NotImplementedException();
	}

	public int GetBrushModelPlaneCount(Model? model) {
		throw new NotImplementedException();
	}

	public void GetBrushModelPlane(Model? model, int nIndex, out CollisionPlane plane, out Vector3 origin) {
		throw new NotImplementedException();
	}

	public int GetSurfacepropsForTerrain(int index) {
		throw new NotImplementedException();
	}

	public void OnLevelChange() {
		NetworkedDynamicModels.Clear();

		// TODO
		// modelloader.ForceUnloadNonClientDynamicModels();
	}

	public int RegisterDynamicModel(ReadOnlySpan<char> name, bool bClientSide) {
		throw new NotImplementedException();
	}

	public bool IsDynamicModelLoading(int modelIndex) {
		return false;
	}

	public void AddRefDynamicModel(int modelIndex) {

	}

	public void ReleaseDynamicModel(int modelIndex) {

	}

	public bool RegisterModelLoadCallback(int modelindex, IModelLoadCallback callback, bool callImmediatelyIfLoaded = true) {
		throw new NotImplementedException();
	}

	public void UnregisterModelLoadCallback(int modelindex, IModelLoadCallback callback) {
		throw new NotImplementedException();
	}

	readonly List<Model> NetworkedDynamicModels = [];
}

public class ModelInfoClient(IFileSystem filesystem, IModelLoader modelloader, IMDLCache mdlCache) : ModelInfo(filesystem, modelloader, mdlCache)
{
	public override Model? GetModel(int modelIndex) {
		if (IsDynamicModelIndex(modelIndex))
			return LookupDynamicModel(modelIndex);

		return cl.GetModel(modelIndex);
	}

	protected override INetworkStringTable? GetDynamicModelStringTable() {
		return cl.DynamicModelsTable;
	}

	protected override int LookupPrecachedModelIndex(ReadOnlySpan<char> name) {
		return cl.LookupModelIndex(name);
	}
}


public class ModelInfoServer(IFileSystem filesystem, IModelLoader modelloader, IMDLCache mdlCache) : ModelInfo(filesystem, modelloader, mdlCache)
{
	public override Model? GetModel(int modelIndex) {
		if (IsDynamicModelIndex(modelIndex))
			return LookupDynamicModel(modelIndex);

		return sv.GetModel(modelIndex);
	}

	protected override INetworkStringTable? GetDynamicModelStringTable() {
		return sv.GetDynamicModelsTable();
	}

	protected override int LookupPrecachedModelIndex(ReadOnlySpan<char> name) {
		return sv.LookupModelIndex(name);
	}
}
