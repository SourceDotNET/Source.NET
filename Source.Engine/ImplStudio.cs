using Source.Common;
using Source.Common.Commands;
using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;


namespace Source.Engine;

public enum ModelInstanceFlags
{
	HasStaticLighting = 0x1,
	HasDiskCompiledColor = 0x2,
	DiskCompiledColorBad = 0x4,
	HasColorDAta = 0x8
}
public class ModelInstance
{
	public IClientRenderable? Renderable;
	public Model? Model;
	public ModelInstanceFlags Flags;

	public LightingState CurrentLightingState;
	public LightingState AmbientLightingState;
	public InlineArray4<Vector3> LightIntensity;
	public float LightingTime = ModelRender.CURRENT_LIGHTING_UNINITIALIZED;
	public LightCacheHandle_t LightCacheHandle;
	public StudioDecalHandle_t DecalHandle = ModelRender.STUDIORENDER_DECAL_INVALID;
	public DataCacheHandle_t ColorMeshHandle;
	public uint FirstShadow;
}

public struct ColorMeshParams
{
	public int Meshes;
	public int TotalVertexes;
	public IPooledVBAllocator? PooledVBAllocator;
	public InlineArray256<int> Vertexes;
	public FileNameHandle_t FnHandle;
}

public class ColorMeshData
{
	public int NumMeshes;
	public ColorMeshInfo[]? MeshInfos;
	public nint[]? Targets;
	public uint TotalSize;
	// FSAsyncControl_t AsyncControlVertex; // todo
	// FSAsyncControl_t AsyncControlTexel; // todo
	public bool HasInvalidVB;
	public bool ColorMeshValid;
	public bool ColorTextureValid;
	public bool ColorTextureCreated;
	public bool NeedsRetry;
	public FileNameHandle_t FnHandle;

	public void DestroyResource() {
		// todo AsyncFinish/AsyncRelease

		using MatRenderContextPtr renderContext = new(Singleton<IMaterialSystem>());

		for (int i = 0; i < NumMeshes; i++) {
			if (MeshInfos![i].PooledVBAllocator != null)
				MeshInfos[i].PooledVBAllocator!.Deallocate(MeshInfos[i].VertOffsetInBytes, MeshInfos[i].NumVerts);
			else
				renderContext.DestroyStaticMesh(MeshInfos[i].Mesh!);

			if (MeshInfos[i].Lightmap != null) {
				// MeshInfos[i].Lightmap.Release();
				MeshInfos[i].Lightmap = null;
			}

			if (MeshInfos[i].LightmapData != null)
				MeshInfos[i].LightmapData = null;
		}
	}

	public ColorMeshData GetData() => this;

	public uint Size() => TotalSize;

	public static ColorMeshData? CreateResource(in ColorMeshParams createParams) {
		ColorMeshData data = new() {
			HasInvalidVB = false,
			ColorMeshValid = false,
			ColorTextureValid = false,
			ColorTextureCreated = false,
			NeedsRetry = false,
			// data.AsyncControlVertex = null;
			// data.AsyncControlTexel = null;
			FnHandle = createParams.FnHandle,
			TotalSize = (uint)(createParams.Meshes * IntPtr.Size + createParams.TotalVertexes * 4),
			NumMeshes = createParams.Meshes,
			MeshInfos = new ColorMeshInfo[createParams.Meshes],
			Targets = new nint[createParams.Meshes]
		};

		using MatRenderContextPtr renderContext = new(Singleton<IMaterialSystem>());

		for (int i = 0; i < createParams.Meshes; i++) {
			VertexFormat vertexFormat = VertexFormat.Specular;

			data.MeshInfos[i].Mesh = null;
			data.MeshInfos[i].PooledVBAllocator = createParams.PooledVBAllocator;
			data.MeshInfos[i].VertOffsetInBytes = 0;
			data.MeshInfos[i].NumVerts = createParams.Vertexes[i];
			data.MeshInfos[i].LightmapData = null;
			data.MeshInfos[i].Lightmap = null;

			if (createParams.PooledVBAllocator != null) {
				data.MeshInfos[i].VertOffsetInBytes = createParams.PooledVBAllocator.Allocate(createParams.Vertexes[i]);

				if (data.MeshInfos[i].VertOffsetInBytes == -1) {
					data.MeshInfos[i].PooledVBAllocator = null;
					data.MeshInfos[i].VertOffsetInBytes = 0;
				}
				else {
					data.MeshInfos[i].Mesh = createParams.PooledVBAllocator.GetSharedMesh();
					data.Targets[i] = createParams.PooledVBAllocator.GetVertexBufferBase() + data.MeshInfos[i].VertOffsetInBytes;
				}
			}

			if (data.MeshInfos[i].Mesh == null)
				data.MeshInfos[i].Mesh = renderContext.CreateStaticMesh(vertexFormat, MaterialDefines.TEXTURE_GROUP_STATIC_VERTEX_BUFFER_COLOR, null);

			Assert(data.MeshInfos[i].Mesh != null);
			if (data.MeshInfos[i].Mesh == null) {
				data.DestroyResource();
				return null;
			}
		}

		return data;
	}

	public static nuint EstimatedSize(in ColorMeshParams createParams) {
		return (nuint)(createParams.Meshes * IntPtr.Size + createParams.TotalVertexes * 4);
	}
}

public class ModelRender : IModelRender
{
	static readonly ConVar r_drawmodelstatsoverlay = new("r_drawmodelstatsoverlay", "0", FCvar.Cheat);
	static readonly ConVar r_drawmodelstatsoverlaydistance = new("r_drawmodelstatsoverlaydistance", "500", FCvar.Cheat);
	static readonly ConVar r_drawmodellightorigin = new("r_DrawModelLightOrigin", "0", FCvar.Cheat);
	static readonly ConVar r_lod = new("r_lod", "-1", 0, "");
	static readonly ConVar r_entity = new("r_entity", "-1", FCvar.Cheat | FCvar.DevelopmentOnly);
	static readonly ConVar r_lightaverage = new("r_lightaverage", "1", 0, "Activates/deactivate light averaging");
	static readonly ConVar r_lightinterp = new("r_lightinterp", "5", FCvar.Cheat, "Controls the speed of light interpolation, 0 turns off interpolation");
	static readonly ConVar r_eyeglintlodpixels = new("r_eyeglintlodpixels", "20.0", FCvar.Cheat, "The number of pixels wide an eyeball has to be before rendering an eyeglint.  Is a floating point value.");
	// static readonly ConVar r_rootlod = new( "r_rootlod", "0", FCvar.MaterialSystemThread | FCvar.Archive, "Root LOD", true, 0, true, Studio.MAX_NUM_LODS-1, SetRootLOD_f ); << todo
	static readonly ConVar r_decalstaticprops = new("r_decalstaticprops", "1", 0, "Decal static props test");
	// static readonly ConCommand r_flushlod = new( "r_flushlod", FlushLOD_f, "Flush and reload LODs." ); << todo
	static readonly ConVar r_debugrandomstaticlighting = new("r_debugrandomstaticlighting", "0", FCvar.Cheat, "Set to 1 to randomize static lighting for debugging.  Must restart for change to take affect.");
	public static readonly ConVar r_proplightingfromdisk = new("r_proplightingfromdisk", "1", FCvar.Cheat, "0=Off, 1=On, 2=Show Errors");
	static readonly ConVar r_itemblinkmax = new("r_itemblinkmax", ".3", FCvar.Cheat);
	static readonly ConVar r_itemblinkrate = new("r_itemblinkrate", "4.5", FCvar.Cheat);
	static readonly ConVar r_ambientboost = new("r_ambientboost", "1", 0, "Set to boost ambient term if it is totally swamped by local lights");
	static readonly ConVar r_ambientmin = new("r_ambientmin", "0.3", 0, "Threshold below which ambient cube will appear boosted");
	static readonly ConVar r_ambientfraction = new("r_ambientfraction", "0.1", FCvar.Cheat, "Fraction of direct lighting used to boost lighting when model requests");
	static readonly ConVar r_drawlightcache = new("r_drawlightcache", "0", FCvar.Cheat, "0: off\n1: draw light cache entries\n2: draw rays\n");
	static readonly ConVar r_lightcachemodel = new("r_lightcachemodel", "-1", FCvar.Cheat, "");
	static readonly ConVar r_proplightingpooling = new("r_proplightingpooling", "-1.0", FCvar.Cheat, "0 - off, 1 - static prop color meshes are allocated from a single shared vertex buffer (on hardware that supports stream offset)");

	static readonly ConVar r_showenvcubemap = new("r_showenvcubemap", "0", FCvar.Cheat);
	static readonly ConVar r_eyemove = new("r_eyemove", "1", FCvar.Archive); // look around
	static readonly ConVar r_eyeshift_x = new("r_eyeshift_x", "0", FCvar.Archive); // eye X position
	static readonly ConVar r_eyeshift_y = new("r_eyeshift_y", "0", FCvar.Archive); // eye Y position
	static readonly ConVar r_eyeshift_z = new("r_eyeshift_z", "0", FCvar.Archive); // eye Z position
	static readonly ConVar r_eyesize = new("r_eyesize", "0", FCvar.Archive); // adjustment to iris textures
	static readonly ConVar mat_softwareskin = new("mat_softwareskin", "0", FCvar.Cheat);
	static readonly ConVar r_nohw = new("r_nohw", "0", FCvar.Cheat);
	static readonly ConVar r_nosw = new("r_nosw", "0", FCvar.Cheat);
	static readonly ConVar r_teeth = new("r_teeth", "1", 0);
	static readonly ConVar r_drawentities = new("r_drawentities", "1", FCvar.Cheat);
	static readonly ConVar r_flex = new("r_flex", "1", 0);
	static readonly ConVar r_eyes = new("r_eyes", "1", 0);
	static readonly ConVar r_skin = new("r_skin", "0", FCvar.Cheat);
	static readonly ConVar r_modelwireframedecal = new("r_modelwireframedecal", "0", FCvar.Cheat);
	static readonly ConVar r_maxmodeldecal = new("r_maxmodeldecal", "50", 0);

	static StudioRenderConfig s_StudioRenderConfig;

	public static void UpdateStudioRenderConfig() {
		IStudioRender? studioRender = Singleton<IStudioRender>();
		if (studioRender == null)
			return;

		s_StudioRenderConfig = default;

		s_StudioRenderConfig.EyeMove = r_eyemove.GetInt() != 0;
		s_StudioRenderConfig.EyeShiftX = r_eyeshift_x.GetFloat();
		s_StudioRenderConfig.EyeShiftY = r_eyeshift_y.GetFloat();
		s_StudioRenderConfig.EyeShiftZ = r_eyeshift_z.GetFloat();
		s_StudioRenderConfig.EyeSize = r_eyesize.GetFloat();
		if (mat_softwareskin.GetInt() != 0 || ShouldDrawInWireFrameMode())
			s_StudioRenderConfig.SoftwareSkin = true;
		else
			s_StudioRenderConfig.SoftwareSkin = false;
		s_StudioRenderConfig.NoHardware = r_nohw.GetInt() != 0;
		s_StudioRenderConfig.NoSoftware = r_nosw.GetInt() != 0;
		s_StudioRenderConfig.Teeth = r_teeth.GetInt() != 0;
		s_StudioRenderConfig.DrawEntities = r_drawentities.GetInt();
		s_StudioRenderConfig.Flex = r_flex.GetInt() != 0;
		s_StudioRenderConfig.Eyes = r_eyes.GetInt() != 0;
		s_StudioRenderConfig.Wireframe = ShouldDrawInWireFrameMode();
		s_StudioRenderConfig.DrawNormals = mat_normals.GetBool();
		s_StudioRenderConfig.Skin = r_skin.GetInt();
		s_StudioRenderConfig.MaxDecalsPerModel = r_maxmodeldecal.GetInt();
		s_StudioRenderConfig.WireframeDecals = r_modelwireframedecal.GetInt() != 0;

		s_StudioRenderConfig.FullBright = MatSysInterface.MaterialSystemConfig.Fullbright;
		s_StudioRenderConfig.SoftwareLighting = MatSysInterface.MaterialSystemConfig.SoftwareLighting;

		s_StudioRenderConfig.ShowEnvCubemapOnly = r_showenvcubemap.GetInt() != 0;
		s_StudioRenderConfig.EyeGlintPixelWidthLODThreshold = r_eyeglintlodpixels.GetFloat();

		studioRender.UpdateConfig(in s_StudioRenderConfig);
	}

	ModelInstanceHandle_t curModelHandle;
	readonly Dictionary<ModelInstanceHandle_t, ModelInstance> ModelInstances = [];

	public ref uint FirstShadowOnModelInstance(ModelInstanceHandle_t handle) => ref ModelInstances[handle].FirstShadow;

	ModelInstanceHandle_t NewHandle() {
		ModelInstanceHandle_t handle = Interlocked.Increment(ref curModelHandle);
		ModelInstances[handle] = new();
		return handle;
	}
	void DeleteHandle(ModelInstanceHandle_t handle) {
		ModelInstances.Remove(handle);
	}

	public ModelInstanceHandle_t CreateInstance(IClientRenderable renderable, LightCacheHandle_t? cache = null) {
		Model? model = renderable.GetModel();

		// We're ok, allocate a new instance handle
		ModelInstanceHandle_t handle = NewHandle();
		ModelInstance instance = ModelInstances[handle];

		instance.Renderable = renderable;
		instance.Model = model;
		instance.Flags = 0;

		for (int i = 0; i < 6; ++i)
			instance.AmbientLightingState.BoxColor[i].X = 1.0f;

#if !SWDS
		instance.FirstShadow = unchecked((uint)g_ShadowMgr.InvalidShadowIndex());
#endif

		// Static props use baked lighting for performance reasons
		if (cache != null) {
			SetStaticLighting(handle, cache);

			// todo v

			// validate static color meshes once, now at load/create time
			ValidateStaticPropColorData(handle);

			// builds out color meshes or loads disk colors, now at load/create time
			RecomputeStaticLighting(handle);
		}

		return handle;
	}

	public void DestroyInstance(ModelInstanceHandle_t modelInstance) {
		// TODO

		// DestroyStaticPropColorData(modelInstance);
		ModelInstances.Remove(modelInstance);
	}

	public ref Matrix4x4 SetupModelState(IClientRenderable renderable) {
		throw new NotImplementedException(); // todo
	}

	public bool DrawModelSetup(ref ModelRenderInfo info, ref DrawModelState state, Span<Matrix3x4> customBoneToWorld, out Span<Matrix3x4> boneToWorldOut) {
		state.StudioHdr = MDLCache.GetStudioHdr(info.Model!.Studio);
		state.Renderable = info.Renderable;

		if ((r_entity.GetInt() != -1) && (r_entity.GetInt() != info.EntityIndex)) {
			boneToWorldOut = default;
			return false;
		}

		// quick exit
		if (state.StudioHdr!.NumBodyParts == 0) {
			boneToWorldOut = default;
			return false;
		}

		state.ModelToWorld = info.ModelToWorld;

		Assert(info.Renderable != null);

		state.StudioHWData = MDLCache.GetHardwareData(info.Model!.Studio)!;
		if (state.StudioHWData == null) {
			boneToWorldOut = default;
			return false;
		}

		state.LOD = ComputeLOD(ref info, state.StudioHWData);
		int boneMask = Studio.BONE_USED_BY_VERTEX_AT_LOD(state.LOD);
		// Why isn't this always set?!?

		bool ok;
		if ((info.Flags & StudioFlags.Render) == 0) {
			// no rendering, just force a bone setup.  Don't copy the bones
			ok = info.Renderable.SetupBones(default, Studio.MAXSTUDIOBONES, boneMask, cl.GetTime());
			boneToWorldOut = default;
			return ok;
		}

		int boneCount = state.StudioHdr.NumBones;
		Span<Matrix3x4> boneToWorld = customBoneToWorld;
		if (customBoneToWorld.IsEmpty)
			boneToWorld = StudioRender.LockBoneMatrices(boneCount);

		ok = info.Renderable.SetupBones(boneToWorld, boneCount, boneMask, cl.GetTime());
		if (customBoneToWorld.IsEmpty)
			StudioRender.UnlockBoneMatrices();

		if (!ok) {
			boneToWorldOut = default;
			return false;
		}

		boneToWorldOut = boneToWorld;

		state.DrawFlags = StudioRenderFlags.DrawEntireModel;
		if ((info.Flags & StudioFlags.TwoPass) != 0) {
			if ((info.Flags & StudioFlags.Transparency) != 0)
				state.DrawFlags = StudioRenderFlags.DrawTranslucentOnly;
			else
				state.DrawFlags = StudioRenderFlags.DrawOpaqueOnly;
		}

		if ((info.Flags & StudioFlags.StaticLighting) != 0)
			state.DrawFlags |= StudioRenderFlags.DrawStaticLighting;

		if ((info.Flags & StudioFlags.ItemBlink) != 0)
			state.DrawFlags |= StudioRenderFlags.DrawItemBlink;

		if ((info.Flags & StudioFlags.Wireframe) != 0)
			state.DrawFlags |= StudioRenderFlags.DrawWireframe;

		if ((info.Flags & StudioFlags.NoShadows) != 0)
			state.DrawFlags |= StudioRenderFlags.DrawNoShadows;

		if ((info.Flags & StudioFlags.ShadowDepthTexture) != 0)
			state.DrawFlags |= StudioRenderFlags.ShadowDepthTexture;

		return true;
	}

	static readonly ConVar r_shadowlod = new("r_shadowlod", "-1");
	static readonly ConVar r_shadowlodbias = new("r_shadowlodbias", "2");

	public bool DrawModelShadowSetup(IClientRenderable renderable, int body, int skin, ref DrawModelInfo info, Span<Matrix3x4> customBoneToWorld, out Span<Matrix3x4> boneToWorldOut) {
		boneToWorldOut = default;

		Model? model = renderable.GetModel();
		if (model == null)
			return false;

		if (model.Type != ModelType.Studio)
			return false;

		// Assert(modelloader.IsLoaded(model) && model.Type == ModelType.Studio);//2do

		info.StudioHdr = MDLCache.GetStudioHdr(model.Studio)!;
		info.ColorMeshes = null;

		if (info.StudioHdr.NumBodyParts == 0)
			return false;

		Assert(renderable != null);
		info.HardwareData = MDLCache.GetHardwareData(model.Studio)!;
		if (info.HardwareData == null)
			return false;

		info.Skin = skin;
		info.Body = body;
		info.ClientEntity = renderable;
		info.HitboxSet = 0;

		info.Lod = r_shadowlod.GetInt();
		if ((info.StudioHdr.Flags & StudioHdrFlags.HasShadowLod) != 0)
			info.Lod = info.HardwareData.NumLODs - 1;
		else if (info.Lod == Studio.USESHADOWLOD) {
			int lastlod = info.HardwareData.NumLODs - 1;
			info.Lod = lastlod;
		}
		else if (info.Lod < 0) {
			using MatRenderContextPtr renderContext = new(materialSystem);
			float factor = r_shadowlodbias.GetFloat() > 0.0f ? 1.0f / r_shadowlodbias.GetFloat() : 1.0f;
			float screenSize = factor * renderContext.ComputePixelWidthOfSphere(renderable!.GetRenderOrigin(), 0.5f);
			info.Lod = StudioRender.ComputeModelLod(info.HardwareData, screenSize);
			info.Lod = info.HardwareData.NumLODs - 2;
			if (info.Lod < 0) {
				info.Lod = 0;
			}
		}

		if (info.Lod < info.HardwareData.RootLOD)
			info.Lod = info.HardwareData.RootLOD;

		Span<Matrix3x4> boneToWorld = customBoneToWorld;
		if (boneToWorld.IsEmpty)
			boneToWorld = StudioRender.LockBoneMatrices(info.StudioHdr.NumBones);
		bool ok = renderable!.SetupBones(boneToWorld, info.StudioHdr.NumBones, Studio.BONE_USED_BY_VERTEX_AT_LOD(info.Lod), cl.GetTime());
		StudioRender.UnlockBoneMatrices();
		if (!ok)
			return false;

		boneToWorldOut = boneToWorld;
		return true;
	}

	public void DrawModelShadow(IClientRenderable renderable, in DrawModelInfo info, Span<Matrix3x4> boneToWorld) {
		StudioRender.SetEyeViewTarget(info.StudioHdr, info.Body, in vec3_origin);

		Vector3 white = new(1, 1, 1);
		StudioRender.SetColorModulation(white);
		StudioRender.SetAlphaModulation(1.0f);

		if ((info.StudioHdr.Flags & StudioHdrFlags.UseShadowLodMaterials) == 0) {
#if !SWDS
			StudioRender.ForcedMaterialOverride(MatSys.MaterialShadowBuild, OverrideType.BuildShadows);
#endif
		}

		DrawModelInfo drawInfo = info;
		StudioRender.DrawModel(ref Unsafe.NullRef<DrawModelResults>(), ref drawInfo, boneToWorld, null, null, renderable.GetRenderOrigin(),
			StudioRenderFlags.DrawNoShadows | StudioRenderFlags.DrawEntireModel | StudioRenderFlags.DrawNoFlexes);
		StudioRender.ForcedMaterialOverride(null);
	}

	public void SetViewTarget(StudioHdr studioHdr, int bodyIndex, in Vector3 target) {
		StudioRender.SetEyeViewTarget(studioHdr.GetRenderHdr(), bodyIndex, in target);
	}

	readonly IMDLCache MDLCache;
	readonly IStudioRender StudioRender;
	readonly IMaterialSystem materials;
#if !SWDS
	readonly RenderView RenderView;
#endif
	readonly Render Render;
	readonly Host Host;
#if !SWDS
	public ModelRender(IMDLCache MDLCache, IStudioRender StudioRender, Host Host, Render Render, IRenderView renderView, IMaterialSystem materialSystem) {
		this.MDLCache = MDLCache;
		this.StudioRender = StudioRender;
		this.Host = Host;
		this.materials = materialSystem;
		this.Render = Render;
		this.RenderView = (RenderView)renderView;

		r_lod.Changed += r_lod_f;
	}
#endif

	protected void r_lod_f(IConVar var, in ConVarChangeContext ctx) {
		CheckVarRange_r_lod();
	}

	private bool CheckVarRange_r_lod() {
		return CheckVarRange_Generic(r_lod, -1, 2);
	}

	private bool CheckVarRange_Generic(ConVar var, int minVal, int maxVal) {
		if (!Host.CanCheat() && !Host.IsSinglePlayerGame()) {
			int clampedValue = Math.Clamp(var.GetInt(), minVal, maxVal);
			if (clampedValue != var.GetInt()) {
				Warning($"sv_cheats=0 prevented changing {var.GetName()} outside of the range [{minVal},{maxVal}] (was {var.GetInt()}).\n");
				var.SetValue(clampedValue);
			}
		}

		return false;
	}

	private int ComputeLOD(ref ModelRenderInfo info, StudioHWData studioHWData) {
		int lod = r_lod.GetInt();
		float screenSize = -1.0f;
		float metric = -1.0f;
		// FIXME!!!  This calc should be in studiorender, not here!!!!!  But since the bone setup
		// is done here, and we need the bone mask, we'll do it here for now.
		if (lod == -1) {
			using MatRenderContextPtr renderContext = new(materials);
			screenSize = renderContext.ComputePixelWidthOfSphere(info.Renderable!.GetRenderOrigin(), 0.5f);
			metric = studioHWData.LODMetric(screenSize);
			lod = studioHWData.GetLODForMetric(metric);
		}
		else {
			if ((info.Flags & (StudioFlags)StudioHdrFlags.HasShadowLod) != 0 && (lod > studioHWData.NumLODs - 2))
				lod = studioHWData.NumLODs - 2;
			else if (lod > studioHWData.NumLODs - 1)
				lod = studioHWData.NumLODs - 1;
			else if (lod < 0)
				lod = 0;
		}

		if (lod < 0)
			lod = 0;
		else if (lod >= studioHWData.NumLODs)
			lod = studioHWData.NumLODs - 1;

		if (lod < studioHWData.RootLOD)
			lod = studioHWData.RootLOD;

		Assert(lod >= 0 && lod < studioHWData.NumLODs);
		return lod;
	}

	public void SetStaticLighting(ModelInstanceHandle_t handle, LightCacheHandle_t? cache) {
		if (handle != MODEL_INSTANCE_INVALID) {
			ModelInstance instance = ModelInstances[handle];
			if (cache != null) {
				instance.LightCacheHandle = cache.Value;
				instance.Flags |= ModelInstanceFlags.HasStaticLighting;
			}
			else {
				instance.LightCacheHandle = 0;
				instance.Flags &= ~ModelInstanceFlags.HasStaticLighting;
			}
		}
	}

	public int DrawModel(StudioFlags flags, IClientRenderable? renderable, ModelInstanceHandle_t instance, int entityIndex, Model? model, in Vector3 origin, in QAngle angles, int skin, int body, int hitboxset, Matrix3x4? modelToWorld = null, Matrix3x4? lightingOffset = null) {
		ModelRenderInfo info = default;
		info.Flags = flags;
		info.Renderable = renderable;
		info.Instance = instance;
		info.EntityIndex = entityIndex;
		info.Model = model;
		info.Origin = origin;
		info.Angles = angles;
		info.Skin = skin;
		info.Body = body;
		info.HitboxSet = hitboxset;
		if (modelToWorld.HasValue) info.ModelToWorld = modelToWorld.Value;
		if (lightingOffset.HasValue) info.LightingOffset = lightingOffset.Value;

		if (r_entity.GetInt() == -1 || r_entity.GetInt() == entityIndex)
			return DrawModelEx(ref info);

		return 0;
	}

	public int DrawModelEx(ref ModelRenderInfo info) {
#if !SWDS
		DrawModelState state = default;

		if (info.ModelToWorld == default)
			MathLib.AngleMatrix(info.Angles, info.Origin, out info.ModelToWorld);

		if (!DrawModelSetup(ref info, ref state, default, out Span<Matrix3x4> boneToWorld))
			return 0;

		if ((info.Flags & StudioFlags.Render) != 0)
			DrawModelExecute(ref state, ref info, boneToWorld);

		return 1;
#else
		return 0;
#endif
	}

	public void DrawModelExecute(ref DrawModelState state, ref ModelRenderInfo pInfo, Span<Matrix3x4> boneToWorldArray) {
#if !SWDS
		bool bShadowDepth = (pInfo.Flags & StudioFlags.ShadowDepthTexture) != 0;
		bool bSSAODepth = (pInfo.Flags & StudioFlags.SSAODepthTexture) != 0;

		if (bShadowDepth && ((pInfo.Model!.Flags & ModelFlag.DoNotCastShadows) != 0))
			return;

		// Shadow state...
		// ShadowMgr.SetModelShadowState(pInfo.Instance);

		//  if (textMode)
		//  	return;

		Span<float> flexWeights = default;
		Span<float> flexDelayedWeights = default;
		int flexCount = state.StudioHdr!.NumFlexDesc;
		if (flexCount > 0) {
			Assert(!boneToWorldArray.IsEmpty);
			bool usesDelayedWeights = state.Renderable!.UsesFlexDelayedWeights();
			if (usesDelayedWeights)
				StudioRender.LockFlexWeights(flexCount, out flexWeights, out flexDelayedWeights);
			else
				StudioRender.LockFlexWeights(flexCount, out flexWeights);
			state.Renderable.SetupWeights(boneToWorldArray, flexWeights, flexDelayedWeights);
			StudioRender.UnlockFlexWeights();
		}

		// OPTIMIZE: Try to precompute part of this mess once a frame at the very least.
		bool bUsesBumpmapping = (pInfo.Model!.Flags & ModelFlag.UsesBumpMapping) != 0;

		bool bStaticLighting = (state.DrawFlags & StudioRenderFlags.DrawStaticLighting) != 0 &&
									(state.StudioHdr!.Flags & StudioHdrFlags.StaticProp) != 0 &&
									(!bUsesBumpmapping) &&
									(pInfo.Instance != MODEL_INSTANCE_INVALID) &&
									HardwareConfig.SupportsColorOnSecondStream();

		bool bVertexLit = (pInfo.Model.Flags & ModelFlag.VertexLit) != 0;

		bool bNeedsEnvCubemap = r_showenvcubemap.GetInt() != 0 || (pInfo.Model.Flags & ModelFlag.UsesEnvCubemap) != 0;

		// todo: r_drawmodellightorigin

		ColorMeshInfo[]? colorMeshes = null;
		DataCacheHandle_t colorMeshData = 0;
		if (bStaticLighting) {
			colorMeshData = ModelInstances[pInfo.Instance].ColorMeshHandle;
			ColorMeshData? pColorMeshData = CacheGet(colorMeshData);
			if (pColorMeshData == null || pColorMeshData.NeedsRetry) {
				if (RecomputeStaticLighting(pInfo.Instance))
					pColorMeshData = CacheGet(colorMeshData);
				else if (pColorMeshData == null || !pColorMeshData.NeedsRetry)
					return;
			}

			if (pColorMeshData != null && (pColorMeshData.ColorMeshValid || pColorMeshData.ColorTextureValid)) {
				colorMeshes = pColorMeshData.MeshInfos;
				if (pColorMeshData.ColorTextureValid && !pColorMeshData.ColorTextureCreated)
					CreateLightmapsFromData(pColorMeshData);
			}
			else
				bStaticLighting = false;
		}

		DrawModelInfo info = default;
		info.StaticLighting = false;

		if ((bVertexLit || bNeedsEnvCubemap) && !bShadowDepth && !bSSAODepth) {
			ref LightCacheHandle_t lightCache = ref Unsafe.NullRef<LightCacheHandle_t>();
			if (pInfo.Instance != MODEL_INSTANCE_INVALID) {
				if ((ModelInstances[pInfo.Instance].Flags & ModelInstanceFlags.HasStaticLighting) != 0 && ModelInstances[pInfo.Instance].LightCacheHandle != 0) {
					lightCache = ref ModelInstances[pInfo.Instance].LightCacheHandle;
				}
			}

			R_ComputeLightingOrigin(state.Renderable, state.StudioHdr, in state.ModelToWorld, out Vector3 entOrigin);

			// Set up lighting based on the lighting origin
			StudioSetupLighting(state, entOrigin, ref lightCache, bVertexLit, bNeedsEnvCubemap, ref bStaticLighting, ref info, pInfo, state.DrawFlags);
		}

		// Set up the camera state
		StudioRender.SetViewState(in Render.CurrentViewOrigin, in Render.CurrentViewRight, in Render.CurrentViewUp, in Render.CurrentViewForward);

		// Color + alpha modulation
		StudioRender.SetColorModulation(RenderView.r_colormod);
		StudioRender.SetAlphaModulation(RenderView.r_blend);

		info.StudioHdr = state.StudioHdr!;
		info.HardwareData = state.StudioHWData;
		info.Skin = pInfo.Skin;
		info.Body = pInfo.Body;
		info.HitboxSet = pInfo.HitboxSet;
		info.ClientEntity = state.Renderable;
		info.Lod = state.LOD;
		info.ColorMeshes = colorMeshes;

		// TODO: decals

		// TODO: perf stats
		StudioRenderFlags drawFlags = state.DrawFlags;

		if (bShadowDepth) {
			drawFlags |= StudioRenderFlags.DrawOpaqueOnly;
			drawFlags |= StudioRenderFlags.ShadowDepthTexture;
		}

		if (bSSAODepth == true) {
			drawFlags |= StudioRenderFlags.DrawOpaqueOnly;
			drawFlags |= StudioRenderFlags.SSAODepthTexture;
		}

		// TODO: perf stats
		DrawModelResults results = default;
		StudioRender.DrawModel(ref results, ref info, boneToWorldArray, flexWeights,
			flexDelayedWeights, in pInfo.Origin, drawFlags);

		// TODO: debug overlay

#endif
	}

	bool EngineLightingSuppressed = false;

	public void SuppressEngineLighting(bool suppress) => EngineLightingSuppressed = suppress;

	LightingState ActualLightingState;
	readonly BSPDWorldLight[] WorldLights = new BSPDWorldLight[Render.MAXLOCALLIGHTS];
	readonly BSPDWorldLightPtr[] SourceLight = new BSPDWorldLightPtr[Render.MAXLOCALLIGHTS];

	private void StudioSetupLighting(DrawModelState state, in Vector3 absEntCenter, ref LightCacheHandle_t lightcache, bool vertexLit, bool needsEnvCubemap, ref bool staticLighting, ref DrawModelInfo drawInfo, ModelRenderInfo renderInfo, StudioRenderFlags drawFlags) {
		if (EngineLightingSuppressed)
			return;

#if !SWDS
		ITexture? envCubemapTexture = null;
		LightingState lightingState = default;

		Span<Vector3> saveLightPos = stackalloc Vector3[Render.MAXLOCALLIGHTS];
		Vector3? debugLightingOrigin = null;
		LightingState lightingDecalState = default;

		drawInfo.StaticLighting = staticLighting && HardwareConfig.SupportsStaticPlusDynamicLighting();
		drawInfo.NumLocalLights = 0;

		Vector3 lightingOrigin = new(0.0f, 0.0f, 0.0f);
		using MatRenderContextPtr renderContext = new(materialSystem);
		if (renderInfo.LightingOrigin.HasValue)
			lightingOrigin = renderInfo.LightingOrigin.Value;
		else {
			lightingOrigin = absEntCenter;
			if (renderInfo.LightingOffset.HasValue)
				MathLib.VectorTransform(absEntCenter, renderInfo.LightingOffset.Value, out lightingOrigin);
		}

		renderContext.SetLightingOrigin(lightingOrigin);

		ModelInstance? modelInst = null;
		bool hasDecals = false;
		if (renderInfo.Instance != MODEL_INSTANCE_INVALID) {
			modelInst = ModelInstances[renderInfo.Instance];
			hasDecals = modelInst.DecalHandle != STUDIORENDER_DECAL_INVALID;
		}

		if (!Unsafe.IsNullRef(ref lightcache)) {
			if (staticLighting) {
				if (HardwareConfig.SupportsStaticPlusDynamicLighting()) {
					if (Render.StaticLightCacheAffectedByDynamicLight(lightcache))
						lightingState = Render.LightcacheGetStatic(lightcache, out envCubemapTexture);
					else
						lightingState = Render.LightcacheGetStatic(lightcache, out envCubemapTexture, LightCacheFlags.Dynamic | LightCacheFlags.LightStyle);
				}
				else {
					if (Render.StaticLightCacheAffectedByDynamicLight(lightcache) ||
						Render.StaticLightCacheAffectedByAnimatedLightStyle(lightcache))
						staticLighting = false;
					else if (Render.StaticLightCacheNeedsSwitchableLightUpdate(lightcache))
						UpdateStaticPropColorData(state.Renderable!.GetIClientUnknown(), renderInfo.Instance);
				}
			}

			if (!staticLighting)
				lightingState = Render.LightcacheGetStatic(lightcache, out envCubemapTexture);

			if (r_decalstaticprops.GetBool() && modelInst != null && drawInfo.StaticLighting && hasDecals) {
				for (int iCube = 0; iCube < 6; ++iCube)
					drawInfo.AmbientCube[iCube] = modelInst.AmbientLightingState.BoxColor[iCube] + lightingState.BoxColor[iCube];

				lightingDecalState.CopyLocalLights(modelInst.AmbientLightingState);
				lightingDecalState.AddAllLocalLights(lightingState);
			}
		}
		else {
			debugLightingOrigin = lightingOrigin;

			if (staticLighting) {
				LightcacheGetDynamic_Stats stats = default;
				envCubemapTexture = Render.LightcacheGetDynamic(lightingOrigin, ref lightingState,
					ref stats, LightCacheFlags.Dynamic | LightCacheFlags.LightStyle);

				if (!HardwareConfig.SupportsStaticPlusDynamicLighting()) {
					if (stats.HasDLights || stats.HasNonSwitchableLightStyles)
						staticLighting = false;
					else if (stats.NeedsSwitchableLightStyleUpdate)
						UpdateStaticPropColorData(state.Renderable!.GetIClientUnknown(), renderInfo.Instance);
				}
			}

			if (!staticLighting) {
				LightcacheGetDynamic_Stats stats = default;

				bool debugModel = false;
				if (r_drawlightcache.GetInt() == 5) {
					if (modelInst != null && modelInst.Model != null && !string.IsNullOrEmpty(modelInst.Model.StrName.String())) {
						string modelName = r_lightcachemodel.GetString();
						debugModel = modelInst.Model.StrName.String()!.Contains(modelName, StringComparison.OrdinalIgnoreCase);
					}
				}

				envCubemapTexture = Render.LightcacheGetDynamic(lightingOrigin, ref lightingState, ref stats,
					LightCacheFlags.Static | LightCacheFlags.Dynamic | LightCacheFlags.LightStyle | LightCacheFlags.AllowFast, debugModel);
			}

			if (renderInfo.LightingOffset.HasValue && !renderInfo.LightingOrigin.HasValue) {
				for (int i = 0; i < lightingState.NumLights; ++i) {
					saveLightPos[i] = lightingState.LocalLight[i].Dereference().Origin;
					MathLib.VectorITransform(saveLightPos[i], renderInfo.LightingOffset.Value, out lightingState.LocalLight[i].Dereference().Origin);
				}
			}

			if (modelInst != null && drawInfo.StaticLighting && hasDecals) {
				LightcacheGetDynamic_Stats stats = default;
				Render.LightcacheGetDynamic(lightingOrigin, ref lightingDecalState, ref stats,
					LightCacheFlags.Static | LightCacheFlags.Dynamic | LightCacheFlags.LightStyle | LightCacheFlags.AllowFast);

				for (int iCube = 0; iCube < 6; ++iCube)
					MathLib.VectorCopy(lightingDecalState.BoxColor[iCube], out drawInfo.AmbientCube[iCube]);

				if (renderInfo.LightingOffset.HasValue && !renderInfo.LightingOrigin.HasValue) {
					for (int i = 0; i < lightingDecalState.NumLights; ++i) {
						saveLightPos[i] = lightingDecalState.LocalLight[i].Dereference().Origin;
						MathLib.VectorITransform(saveLightPos[i], renderInfo.LightingOffset.Value, out lightingDecalState.LocalLight[i].Dereference().Origin);
					}
				}
			}
		}

		ref LightingState stateRef = ref lightingState;
		if (!staticLighting && Unsafe.IsNullRef(ref lightcache))
			stateRef = ref TimeAverageLightingState(renderInfo.Instance, ref lightingState, renderInfo.EntityIndex, debugLightingOrigin);

		if (needsEnvCubemap && envCubemapTexture != null)
			renderContext.BindLocalCubemap(envCubemapTexture);

		if (MatSysInterface.MaterialSystemConfig.Fullbright == 1) {
			renderContext.SetAmbientLight(1.0f, 1.0f, 1.0f);

			ReadOnlySpan<Vector3> white = [new(1, 1, 1), new(1, 1, 1), new(1, 1, 1), new(1, 1, 1), new(1, 1, 1), new(1, 1, 1)];
			StudioRender.SetAmbientLightColors(white);

			renderContext.DisableAllLocalLights();
		}
		else if (vertexLit) {
			if ((drawFlags & StudioRenderFlags.DrawItemBlink) != 0) {
				float add = r_itemblinkmax.GetFloat() * (MathF.Cos(r_itemblinkrate.GetFloat() * (float)Sys.Time) + 1.0f);
				Vector3 additiveColor = new(add, add, add);
				Span<Vector3> temp = stackalloc Vector3[6];
				for (int i = 0; i < 6; i++)
					temp[i] = stateRef.BoxColor[i] + additiveColor;
				StudioRender.SetAmbientLightColors(temp);
			}
			else {
				if (stateRef.NumLights > 0 && (renderInfo.Model!.Flags & ModelFlag.AmbientBoost) != 0 && r_ambientboost.GetBool()) {
					Vector3 lumCoeff = new(0.3f, 0.59f, 0.11f);
					float avgCubeLuminance = 0.0f;
					float minCubeLuminance = float.MaxValue;
					float maxCubeLuminance = 0.0f;

					for (int i = 0; i < 6; i++) {
						float luminance = MathLib.DotProduct(stateRef.BoxColor[i], lumCoeff);
						minCubeLuminance = MathF.Min(minCubeLuminance, luminance);
						maxCubeLuminance = MathF.Max(maxCubeLuminance, luminance);
						avgCubeLuminance += luminance;
					}
					avgCubeLuminance /= 6.0f;

					float directLight = 0.0f;
					for (int i = 0; i < stateRef.NumLights; i++) {
						Vector3 light = stateRef.LocalLight[i].Dereference().Origin - lightingOrigin;
						float d2 = MathLib.DotProduct(light, light);
						float d = MathF.Sqrt(d2);
						float atten = 1.0f;

						float denom = stateRef.LocalLight[i].Dereference().ConstantAttn +
									stateRef.LocalLight[i].Dereference().LinearAttn * d +
									stateRef.LocalLight[i].Dereference().QuadraticAttn * d2;

						if (denom > 0.00001f)
							atten = 1.0f / denom;

						Vector3 lit = stateRef.LocalLight[i].Dereference().Intensity * atten;
						directLight += MathLib.DotProduct(lit, lumCoeff);
					}

					if (avgCubeLuminance < r_ambientmin.GetFloat() && (avgCubeLuminance < (directLight * r_ambientfraction.GetFloat()))) {
						Span<Vector3> finalAmbientCube = stackalloc Vector3[6];
						float boostFactor = MathF.Min((directLight * r_ambientfraction.GetFloat()) / maxCubeLuminance, 5.0f);
						for (int i = 0; i < 6; i++)
							finalAmbientCube[i] = stateRef.BoxColor[i] * boostFactor;
						StudioRender.SetAmbientLightColors(finalAmbientCube);
					}
					else
						StudioRender.SetAmbientLightColors(stateRef.BoxColor);
				}
				else
					StudioRender.SetAmbientLightColors(stateRef.BoxColor);
			}

			renderContext.SetAmbientLight(0.0f, 0.0f, 0.0f);
			R_SetNonAmbientLightingState(stateRef.NumLights, stateRef.LocalLight, out drawInfo.NumLocalLights, drawInfo.LocalLightDescs, true);

			if (modelInst != null && drawInfo.StaticLighting && hasDecals)
				R_SetNonAmbientLightingState(lightingDecalState.NumLights, lightingDecalState.LocalLight, out drawInfo.NumLocalLights, drawInfo.LocalLightDescs, false);
		}

		if (renderInfo.LightingOffset.HasValue && !renderInfo.LightingOrigin.HasValue) {
			for (int i = 0; i < lightingState.NumLights; ++i)
				lightingState.LocalLight[i].Dereference().Origin = saveLightPos[i];
		}
#endif
	}

	public const float CURRENT_LIGHTING_UNINITIALIZED = -999999.0f;
	public const StudioDecalHandle_t STUDIORENDER_DECAL_INVALID = unchecked((StudioDecalHandle_t)~0);
	const float AMBIENT_MAX = 8.0f;

	private void InitColormeshParams(ModelInstance instance, StudioHWData studioHWData, ref ColorMeshParams colorMeshParams) {
		colorMeshParams.Meshes = 0;
		colorMeshParams.TotalVertexes = 0;
		colorMeshParams.PooledVBAllocator = null;

		if ((instance.Flags & ModelInstanceFlags.HasDiskCompiledColor) != 0 &&
			HardwareConfig.SupportsStreamOffset() &&
			r_proplightingpooling.GetInt() == 1) {
			// colorMeshParams.PooledVBAllocator = ColorMeshVBAllocator;
		}

		for (int lodID = studioHWData.RootLOD; lodID < studioHWData.NumLODs; lodID++) {
			StudioLODData lod = studioHWData.LODs![lodID];
			for (int meshID = 0; meshID < studioHWData.NumStudioMeshes; meshID++) {
				StudioMeshData mesh = lod.MeshData![meshID];
				for (int groupID = 0; groupID < mesh.NumGroup; groupID++) {
					colorMeshParams.Vertexes[colorMeshParams.Meshes++] = mesh.MeshGroup![groupID].NumVertices;
					Assert(colorMeshParams.Meshes <= 256);

					colorMeshParams.TotalVertexes += mesh.MeshGroup[groupID].NumVertices;
				}
			}
		}
	}

	private ColorMeshData? FindOrCreateStaticPropColorData(ModelInstanceHandle_t handle) { // datacache TODO
		if (handle == MODEL_INSTANCE_INVALID || !HardwareConfig.SupportsColorOnSecondStream())
			return null;

		ModelInstance instance = ModelInstances[handle];
		ColorMeshData? colorMeshData = CacheGet(instance.ColorMeshHandle);
		if (colorMeshData != null)
			return colorMeshData;

		if (instance.Model == null)
			return null;

		StudioHWData? studioHWData = MDLCache.GetHardwareData(instance.Model.Studio);
		Assert(studioHWData != null);
		if (studioHWData == null)
			return null;

		ColorMeshParams parms = default;
		InitColormeshParams(instance, studioHWData, ref parms);
		if (parms.Meshes <= 0)
			return null;

		parms.FnHandle = instance.Model.FileNameHandle;
		instance.ColorMeshHandle = CacheCreate(parms);
		ProtectColorDataIfQueued(instance.ColorMeshHandle);
		colorMeshData = CacheGet(instance.ColorMeshHandle);

		return colorMeshData;
	}

#if true // until datacache done
	private DataCacheHandle_t CacheNextHandle = 1;
	private readonly Dictionary<DataCacheHandle_t, ColorMeshData> Cache = [];

	private ColorMeshData? CacheGet(DataCacheHandle_t handle) {
		Cache.TryGetValue(handle, out ColorMeshData? colorMeshData);
		return colorMeshData;
	}

	private DataCacheHandle_t CacheCreate(in ColorMeshParams parms) {
		ColorMeshData? colorMeshData = ColorMeshData.CreateResource(parms);
		if (colorMeshData == null)
			return 0;

		DataCacheHandle_t handle = CacheNextHandle++;
		Cache[handle] = colorMeshData;
		return handle;
	}

	private void ProtectColorDataIfQueued(DataCacheHandle_t handle) {
	}
#endif

	private static void CreateLightmapsFromData(ColorMeshData colorMeshData) {
		Assert(colorMeshData.ColorTextureValid);
		Assert(!colorMeshData.ColorTextureCreated);

		for (int mesh = 0; mesh < colorMeshData.NumMeshes; ++mesh) {
			ref ColorMeshInfo meshInfo = ref colorMeshData.MeshInfos![mesh];

			Assert(meshInfo.LightmapData != null);
			Assert(meshInfo.Lightmap == null);

			ColorTexelsInfo cti = meshInfo.LightmapData!.Value;

			Assert(cti.TexelData != null);

			// meshInfo.Lightmap = Singleton<IMaterialSystem>().CreateTextureFromBits(cti.Width, cti.Height, cti.MipmapCount, cti.ImageFormat, cti.ByteCount, cti.TexelData); // todo

			meshInfo.LightmapData = null;
		}

		colorMeshData.ColorTextureCreated = true;
	}

	private void ComputeModelVertexLightingOld(StudioHeader studioHdr, MStudioModel pModel, in Matrix3x4 matrix, in LightingState lightingState, Span<Color24> pLighting, bool useConstDirLighting, float constDirLightAmount) {
		Vector3 destColor;
		Span<LightDesc> lightDesc = stackalloc LightDesc[Render.MAXLOCALLIGHTS];

		ref LightingState pLightingState = ref Unsafe.AsRef(in lightingState);

		R_SetNonAmbientLightingState(pLightingState.NumLights, pLightingState.LocalLight, out int numLightDesc, lightDesc, false);

		MStudioModelVertexData? vertData = pModel.GetVertexData((IStudioDataCache)MDLCache, studioHdr);
		if (vertData == null)
			return;

		for (int i = 0; i < pModel.NumVertices; ++i) {
			ref MStudioVertex vert = ref vertData.Vertex(i);
			MathLib.VectorTransform(vert.Position, matrix, out Vector3 worldPos);
			MathLib.VectorRotate(vert.Normal, matrix, out Vector3 worldNormal);

			if (useConstDirLighting)
				StudioRender.ComputeLightingConstDirectional(pLightingState.BoxColor, numLightDesc, lightDesc, worldPos, worldNormal, out destColor, constDirLightAmount);
			else
				StudioRender.ComputeLighting(pLightingState.BoxColor, numLightDesc, lightDesc, worldPos, worldNormal, out destColor);

			destColor.X = MathLib.LinearToVertexLight(destColor.X);
			destColor.Y = MathLib.LinearToVertexLight(destColor.Y);
			destColor.Z = MathLib.LinearToVertexLight(destColor.Z);

			Assert((destColor.X >= 0.0f) && (destColor.X <= 1.0f));
			Assert((destColor.Y >= 0.0f) && (destColor.Y <= 1.0f));
			Assert((destColor.Z >= 0.0f) && (destColor.Z <= 1.0f));

			pLighting[i].R = MathLib.FastFToC(destColor.X);
			pLighting[i].G = MathLib.FastFToC(destColor.Y);
			pLighting[i].B = MathLib.FastFToC(destColor.Z);
		}
	}

	private bool UpdateStaticPropColorData(IHandleEntity? pProp, ModelInstanceHandle_t handle) {
#if !SWDS
		ColorMeshData? colorMeshData = FindOrCreateStaticPropColorData(handle);
		if (colorMeshData == null)
			return false;

		if (colorMeshData.HasInvalidVB) {
			colorMeshData.ColorMeshValid = false;
			colorMeshData.NeedsRetry = false;
			return false;
		}

		Span<byte> debugColor = stackalloc byte[3];
		debugColor.Clear();
		bool debugColorSet = false;
		if (r_debugrandomstaticlighting.GetBool()) {
			Vector3 randomColor;
			int color = RandomInt(1, 6);
			randomColor.X = (color >> 2) & 1;
			randomColor.Y = (color >> 1) & 1;
			randomColor.Z = color & 1;
			MathLib.VectorNormalize(ref randomColor);
			debugColor[0] = (byte)(randomColor.X * 255.0f);
			debugColor[1] = (byte)(randomColor.Y * 255.0f);
			debugColor[2] = (byte)(randomColor.Z * 255.0f);
			debugColorSet = true;
		}

		ModelInstance inst = ModelInstances[handle];
		Assert(inst.Model != null);

		if (r_proplightingfromdisk.GetInt() == 2) {
			if ((inst.Flags & ModelInstanceFlags.DiskCompiledColorBad) != 0) {
				debugColor[0] = 255;
				debugColor[1] = 0;
				debugColor[2] = 0;
			}
			else if ((inst.Flags & ModelInstanceFlags.HasDiskCompiledColor) != 0) {
				debugColor[0] = 0;
				debugColor[1] = 255;
				debugColor[2] = 0;
			}
			else {
				debugColor[0] = 255;
				debugColor[1] = 255;
				debugColor[2] = 0;
			}
			debugColorSet = true;
		}

		StudioHeader? studioHdr = MDLCache.GetStudioHdr(inst.Model!.Studio);
		StudioHWData? studioHWData = MDLCache.GetHardwareData(inst.Model.Studio);
		Assert(studioHdr != null && studioHWData != null);

		if (!debugColorSet && (inst.Flags & ModelInstanceFlags.HasDiskCompiledColor) != 0) {
			// if (LoadStaticPropColorData(pProp, inst.ColorMeshHandle, studioHWData)) // todo
			// 	return true;
		}

		colorMeshData.ColorMeshValid = false;
		colorMeshData.ColorTextureValid = false;
		colorMeshData.ColorTextureCreated = false;
		colorMeshData.NeedsRetry = true;

		if (!debugColorSet) {
			VertexFileHeader? vertexHdr = ((IStudioDataCache)MDLCache).CacheVertexData(studioHdr!);
			if (vertexHdr == null)
				return false;
		}

		inst.Flags |= ModelInstanceFlags.HasColorDAta;

		MathLib.AngleMatrix(inst.Renderable!.GetRenderAngles(), inst.Renderable.GetRenderOrigin(), out Matrix3x4 matrix);

		LightCacheFlags lightCacheFlags = LightCacheFlags.Static;
		if (!HardwareConfig.SupportsStaticPlusDynamicLighting())
			lightCacheFlags |= LightCacheFlags.LightStyle;

		LightingState lightingState = default;
		if ((inst.Flags & ModelInstanceFlags.HasStaticLighting) != 0 && inst.LightCacheHandle != 0)
			lightingState = Render.LightcacheGetStatic(inst.LightCacheHandle, out _, lightCacheFlags);
		else {
			R_ComputeLightingOrigin(inst.Renderable, studioHdr, matrix, out Vector3 entOrigin);
			LightcacheGetDynamic_Stats stats = default;
			Render.LightcacheGetDynamic(entOrigin, ref lightingState, ref stats, lightCacheFlags);
		}

		bool useConstDirLighting = false;
		float constDirLightingAmount = 0.0f;
		if ((studioHdr!.Flags & StudioHdrFlags.ConstantDirectionalLightDot) != 0) {
			useConstDirLighting = true;
			constDirLightingAmount = studioHdr.ConstDirectionalLightDot / 255.0f;
		}

		Color24[]? tmpLightingMem = null;

		for (int bodyPartID = 0; bodyPartID < studioHdr.NumBodyParts; ++bodyPartID) {
			MStudioBodyParts pBodyPart = studioHdr.BodyPart(bodyPartID);

			for (int modelID = 0; modelID < pBodyPart.NumModels; ++modelID) {
				MStudioModel pModel = pBodyPart.Model(modelID);

				if (pModel.NumVertices == 0)
					continue;

				if (tmpLightingMem == null || tmpLightingMem.Length < pModel.NumVertices)
					tmpLightingMem = new Color24[pModel.NumVertices];

				if (!debugColorSet)
					ComputeModelVertexLightingOld(studioHdr, pModel, matrix, lightingState, tmpLightingMem, useConstDirLighting, constDirLightingAmount);
				else {
					for (int i = 0; i < pModel.NumVertices; i++) {
						tmpLightingMem[i].R = debugColor[0];
						tmpLightingMem[i].G = debugColor[1];
						tmpLightingMem[i].B = debugColor[2];
					}
				}

				for (int lodID = studioHWData!.RootLOD; lodID < studioHWData.NumLODs; ++lodID) {
					StudioLODData pStudioLODData = studioHWData.LODs![lodID];
					StudioMeshData[] pStudioMeshData = pStudioLODData.MeshData!;

					for (int meshID = 0; meshID < pModel.NumMeshes; ++meshID) {
						MStudioMesh pMesh = pModel.Mesh(meshID);

						for (int stripGroupID = 0; stripGroupID < pStudioMeshData[pMesh.MeshID].NumGroup; ++stripGroupID) {
							StudioMeshGroup pMeshGroup = pStudioMeshData[pMesh.MeshID].MeshGroup![stripGroupID];
							ref ColorMeshInfo pColorMeshInfo = ref colorMeshData.MeshInfos![pMeshGroup.ColorMeshID];

							MeshBuilder meshBuilder = new();
							meshBuilder.Begin(pColorMeshInfo.Mesh!, MaterialPrimitiveType.Heterogenous, pMeshGroup.NumVertices, 0);

							if (meshBuilder.VertexSize() == 0) {
								meshBuilder.End();
								return false;
							}

							int streamOffset = pColorMeshInfo.VertOffsetInBytes / meshBuilder.VertexSize();
							meshBuilder.AdvanceVertices(streamOffset);

							for (int i = 0; i < pMeshGroup.NumVertices; ++i) {
								int nVertIndex = pMesh.VertexOffset + pMeshGroup.GroupIndexToMeshIndex![i];
								Assert(nVertIndex < pModel.NumVertices);
								meshBuilder.Specular3ub(tmpLightingMem[nVertIndex].R, tmpLightingMem[nVertIndex].G, tmpLightingMem[nVertIndex].B);
								meshBuilder.AdvanceVertex();
							}

							meshBuilder.End();
						}
					}
				}
			}
		}

		colorMeshData.ColorMeshValid = true;
		colorMeshData.NeedsRetry = false;
#endif
		return true;
	}

	public bool RecomputeStaticLighting(ModelInstanceHandle_t handle) {
#if !SWDS
		if (handle == MODEL_INSTANCE_INVALID)
			return false;

		if (!HardwareConfig.SupportsColorOnSecondStream())
			return true;

		ModelInstance instance = ModelInstances[handle];

		StudioHeader? studioHdr = MDLCache.GetStudioHdr(instance.Model!.Studio);
		if (studioHdr == null)
			return false;

		if ((studioHdr.Flags & StudioHdrFlags.StaticProp) != 0) {
			StudioHWData? studioHWData = MDLCache.GetHardwareData(instance.Model.Studio);
			if (studioHWData == null)
				return false;

			if (r_decalstaticprops.GetBool() && instance.LightCacheHandle != 0)
				instance.AmbientLightingState = Render.LightcacheGetStatic(instance.LightCacheHandle, out _, LightCacheFlags.Static);

			return UpdateStaticPropColorData(instance.Renderable!.GetIClientUnknown(), handle);
		}
#endif
		return true;
	}

	private void ValidateStaticPropColorData(uint handle) {
		if (!r_proplightingfromdisk.GetBool())
			return;

		ModelInstance instance = ModelInstances[handle];
		IHandleEntity prop = instance.Renderable!.GetIClientUnknown();


		if (!HardwareConfig.SupportsColorOnSecondStream() || !StaticPropMgr().IsStaticProp(prop))
			return;

		// todo

	}


	private ref LightingState TimeAverageLightingState(ModelInstanceHandle_t handle, ref LightingState lightingState, int entIndex, Vector3? lightingOrigin) {
		if (r_lightaverage.GetInt() == 0)
			return ref lightingState;

		float interpFactor = r_lightinterp.GetFloat();
		if (interpFactor == 0)
			return ref lightingState;

		if (handle == MODEL_INSTANCE_INVALID)
			return ref lightingState;

		ModelInstance inst = ModelInstances[handle];
		if (inst.LightingTime == CURRENT_LIGHTING_UNINITIALIZED) {
			SnapCurrentLightingState(inst, ref lightingState);
			return ref lightingState;
		}

		float dt = (float)(cl.GetTime() - inst.LightingTime);
		if (dt <= 0.0f)
			dt = 0.0f;
		else
			inst.LightingTime = (float)cl.GetTime();

		int i;
		Vector3 vecDelta;
		float attenFactor = MathF.Exp(-interpFactor * dt);
		TimeAverageAmbientLight(ref ActualLightingState, inst, attenFactor, ref lightingState, lightingOrigin);

		int worldLights;
		if (!MatSysInterface.MaterialSystemConfig.SoftwareLighting)
			worldLights = Math.Min(HardwareConfig.MaxNumLights(), Render.r_worldlights.GetInt());
		else
			worldLights = Render.r_worldlights.GetInt();

		int matchCount = 0;
		Span<bool> match = stackalloc bool[Render.MAXLOCALLIGHTS];
		Span<Vector3> lightIntensity = stackalloc Vector3[Render.MAXLOCALLIGHTS];

		match.Clear();
		for (i = 0; i < lightingState.NumLights; ++i) {
			lightIntensity[i].Init(0.0f, 0.0f, 0.0f);
			int j;
			for (j = 0; j < inst.CurrentLightingState.NumLights; ++j) {
				if (lightingState.LocalLight[i] == inst.CurrentLightingState.LocalLight[j]) {
					++matchCount;
					match[j] = true;
					lightIntensity[i] = inst.LightIntensity[j];
					break;
				}
			}
		}

		for (i = 0; i < lightingState.NumLights; ++i) {
			ActualLightingState.LocalLight[i] = new BSPDWorldLightPtr(WorldLights, i);
			WorldLights[i] = lightingState.LocalLight[i].Dereference();

			MathLib.VectorSubtract(lightingState.LocalLight[i].Dereference().Intensity, lightIntensity[i], out vecDelta);
			vecDelta *= attenFactor;

			WorldLights[i].Intensity = lightingState.LocalLight[i].Dereference().Intensity - vecDelta;
			SourceLight[i] = lightingState.LocalLight[i];
		}

		int currLight = lightingState.NumLights;
		for (i = 0; i < inst.CurrentLightingState.NumLights; ++i) {
			if (match[i])
				continue;

			if (inst.LightIntensity[i].LengthSqr() < 1)
				continue;

			if (currLight >= Render.MAXLOCALLIGHTS)
				break;

			ActualLightingState.LocalLight[currLight] = new BSPDWorldLightPtr(WorldLights, currLight);
			WorldLights[currLight] = inst.CurrentLightingState.LocalLight[i].Dereference();

			MathLib.VectorMultiply(inst.LightIntensity[i], attenFactor, out vecDelta);

			WorldLights[currLight].Intensity = vecDelta;
			SourceLight[currLight] = inst.CurrentLightingState.LocalLight[i];

			if ((currLight >= worldLights) && lightingOrigin.HasValue)
				Render.AddWorldLightToAmbientCube(ActualLightingState.LocalLight[currLight], lightingOrigin.Value, ref ActualLightingState.BoxColor);

			++currLight;
		}

		ActualLightingState.NumLights = Math.Min(currLight, worldLights);
		inst.CurrentLightingState.NumLights = currLight;

		for (i = 0; i < currLight; ++i) {
			inst.CurrentLightingState.LocalLight[i] = SourceLight[i];
			inst.LightIntensity[i] = WorldLights[i].Intensity;
		}

		return ref ActualLightingState;
	}

	private static void TimeAverageAmbientLight(ref LightingState actualLightingState, ModelInstance inst, float attenFactor, ref LightingState lightingState, Vector3? lightingOrigin) {
		attenFactor = Math.Clamp(attenFactor, 0.0f, 1.0f);
		for (int i = 0; i < 6; ++i) {
			MathLib.VectorSubtract(lightingState.BoxColor[i], inst.CurrentLightingState.BoxColor[i], out Vector3 vecDelta);
			vecDelta *= attenFactor;
			inst.CurrentLightingState.BoxColor[i] = lightingState.BoxColor[i] - vecDelta;

			inst.CurrentLightingState.BoxColor[i].X = Math.Clamp(inst.CurrentLightingState.BoxColor[i].X, 0.0f, AMBIENT_MAX);
			inst.CurrentLightingState.BoxColor[i].Y = Math.Clamp(inst.CurrentLightingState.BoxColor[i].Y, 0.0f, AMBIENT_MAX);
			inst.CurrentLightingState.BoxColor[i].Z = Math.Clamp(inst.CurrentLightingState.BoxColor[i].Z, 0.0f, AMBIENT_MAX);
		}

		for (int i = 0; i < 6; ++i)
			actualLightingState.BoxColor[i] = inst.CurrentLightingState.BoxColor[i];
	}

	private void SnapCurrentLightingState(ModelInstance inst, ref LightingState lightingState) {
		inst.CurrentLightingState = lightingState;
		for (int i = 0; i < Render.MAXLOCALLIGHTS; ++i) {
			if (i < lightingState.NumLights)
				inst.LightIntensity[i] = lightingState.LocalLight[i].Dereference().Intensity;
			else
				inst.LightIntensity[i].Init(0.0f, 0.0f, 0.0f);
		}

		inst.LightingTime = (float)cl.GetTime();
	}

	const float MIN_LIGHT_VALUE = 0.03f;

	private static bool WorldLightToMaterialLight(ref BSPDWorldLight worldLight, out LightDesc light) {
		light = default;
		light.Attenuation0 = 0.0f;
		light.Attenuation1 = 0.0f;
		light.Attenuation2 = 0.0f;

		switch (worldLight.Type) {
			case EmitType.SpotLight:
				light.Type = LightType.Spot;
				light.Attenuation0 = worldLight.ConstantAttn;
				light.Attenuation1 = worldLight.LinearAttn;
				light.Attenuation2 = worldLight.QuadraticAttn;
				light.Theta = 2.0f * MathF.Acos(worldLight.StopDot);
				light.Phi = 2.0f * MathF.Acos(worldLight.StopDot2);
				light.ThetaDot = worldLight.StopDot;
				light.PhiDot = worldLight.StopDot2;
				light.Falloff = worldLight.Exponent != 0.0f ? worldLight.Exponent : 1.0f;
				break;
			case EmitType.Surface:
				light.Type = LightType.Spot;
				light.Attenuation2 = 1.0f;
				light.Theta = MathF.PI;
				light.Phi = MathF.PI;
				light.ThetaDot = 0.0f;
				light.PhiDot = 0.0f;
				light.Falloff = 1.0f;
				break;
			case EmitType.Point:
				light.Type = LightType.Point;
				light.Attenuation0 = worldLight.ConstantAttn;
				light.Attenuation1 = worldLight.LinearAttn;
				light.Attenuation2 = worldLight.QuadraticAttn;
				break;
			case EmitType.SkyLight:
				light.Type = LightType.Directional;
				break;
			case EmitType.QuakeLight:
			case EmitType.SkyAmbient:
				return false;
		}

		if ((light.Attenuation0 == 0.0f) && (light.Attenuation1 == 0.0f) && (light.Attenuation2 == 0.0f))
			light.Attenuation0 = 1.0f;

		light.Position = worldLight.Origin;
		light.Direction = worldLight.Normal;
		light.Color = worldLight.Intensity;

		float intensity = MathF.Sqrt(MathLib.DotProduct(light.Color, light.Color));

		if (worldLight.Radius != 0)
			light.Range = worldLight.Radius;
		else {
			if (light.Attenuation2 == 0.0f) {
				if (light.Attenuation1 == 0.0f)
					light.Range = MathF.Sqrt(float.MaxValue);
				else
					light.Range = (intensity / MIN_LIGHT_VALUE - light.Attenuation0) / light.Attenuation1;
			}
			else {
				float a = light.Attenuation2;
				float b = light.Attenuation1;
				float c = light.Attenuation0 - intensity / MIN_LIGHT_VALUE;
				float discrim = b * b - 4 * a * c;
				if (discrim < 0.0f)
					light.Range = MathF.Sqrt(float.MaxValue);
				else {
					light.Range = (-b + MathF.Sqrt(discrim)) / (2.0f * a);
					if (light.Range < 0)
						light.Range = 0;
				}
			}
		}

		LightTypeOptimizationFlags flags = LightTypeOptimizationFlags.DerivedValuesCalced;
		if (light.Attenuation0 != 0.0f)
			flags |= LightTypeOptimizationFlags.HasAttenuation0;
		if (light.Attenuation1 != 0.0f)
			flags |= LightTypeOptimizationFlags.HasAttenuation1;
		if (light.Attenuation2 != 0.0f)
			flags |= LightTypeOptimizationFlags.HasAttenuation2;
		light.Flags = (uint)flags;

		return true;
	}

	private void R_SetNonAmbientLightingState(int numLights, Span<BSPDWorldLightPtr> localLight, out int numLightDescs, Span<LightDesc> lightDescs, bool updateStudioRenderLights) {
		Assert(numLights >= 0 && numLights <= Render.MAXLOCALLIGHTS);

		numLightDescs = 0;

		for (int i = 0; i < numLights; i++) {
			if (!WorldLightToMaterialLight(ref localLight[i].Dereference(), out LightDesc lightDesc))
				continue;

			float bias = Render.LightStyleValue((byte)localLight[i].Dereference().Style);

			lightDesc.Color *= bias;

			lightDescs[numLightDescs] = lightDesc;
			numLightDescs += 1;
			Assert(numLightDescs <= Render.MAXLOCALLIGHTS);
		}

		if (updateStudioRenderLights)
			StudioRender.SetLocalLights(numLightDescs, lightDescs[..numLightDescs]);
	}

	internal static void R_ComputeLightingOrigin(IClientRenderable? renderable, StudioHeader? studioHdr, in Matrix3x4 matrix, out Vector3 center) {
		int nAttachmentIndex = studioHdr!.IllumPositionAttachmentIndex();
		if (nAttachmentIndex <= 0)
			MathLib.VectorTransform(studioHdr!.IllumPosition, matrix, out center);
		else {
			renderable!.GetAttachment(nAttachmentIndex, out Matrix3x4 attachment);
			MathLib.VectorTransform(studioHdr!.IllumPosition, attachment, out center);
		}
	}
}
