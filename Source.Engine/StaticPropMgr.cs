global using static Source.Engine.StaticPropMgrGlobals;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Source.Engine;


public static class StaticPropMgrGlobals
{
	public static readonly StaticPropMgrImpl g_StaticPropMgr = new();
	public static IStaticPropMgrEngine StaticPropMgr() => g_StaticPropMgr;

	public static readonly ConVar r_DrawSpecificStaticProp = new("r_DrawSpecificStaticProp", "-1", 0);
	public static readonly ConVar r_drawstaticprops = new("r_drawstaticprops", "1", FCvar.Cheat, "0=Off, 1=Normal, 2=Wireframe");
	public static readonly ConVar r_colorstaticprops = new("r_colorstaticprops", "0", FCvar.Cheat);
	public static readonly ConVar r_staticpropinfo = new("r_staticpropinfo", "0", 0);
	public static readonly ConVar r_drawmodeldecals = new("r_drawmodeldecals", "1", 0);

	public const int STATICPROP_EHANDLE_MASK = 0x40000000;

	public static Vector3 r_colormod = new(1, 1, 1);
	public static float r_blend = 1;

	public static bool IsUsingStaticPropDebugModes() {
		if (r_drawstaticprops.GetInt() != 1 ||
			r_DrawSpecificStaticProp.GetInt() >= 0 ||
			r_colorstaticprops.GetBool() ||
			r_staticpropinfo.GetInt() != 0 ||
			mat_fullbright.GetBool())
			// todo: || r_drawmodellightorigin || r_drawmodelstatsoverlay
			return true;
		return false;
	}
}


public class StaticPropMgrImpl : IStaticPropMgrEngine, IStaticPropMgrClient, IStaticPropMgrServer
{
	struct StaticPropDict
	{
		public Model? Model;
		public MDLHandle_t MDL;
	}

	[StructLayout(LayoutKind.Explicit)]
	struct StaticPropFade
	{
		[FieldOffset(0)] public int Model;
		[FieldOffset(4)] public float MinDistSq;
		[FieldOffset(4)] public float MaxScreenWidth;
		[FieldOffset(8)] public float MaxDistSq;
		[FieldOffset(8)] public float MinScreenWidth;
		[FieldOffset(12)] public float FalloffFactor;
	}

	readonly List<StaticPropDict> StaticPropDictList = [];
	readonly List<StaticProp> StaticProps = [];
	readonly List<StaticPropLeafLump> StaticPropLeaves = [];

	readonly List<StaticPropFade> StaticPropFadeList = [];

	bool LevelInitialized;
	bool ClientInitialized;
	Vector3 LastViewOrigin;
	float LastViewFactor;

	public bool Init() {
		return true;
	}

	public void Shutdown() {
		if (!LevelInitialized)
			return;

		LevelShutdown();
	}

	public void LevelInit() {
		if (LevelInitialized)
			return;

		Assert(!ClientInitialized);
		LevelInitialized = true;

		UnserializeStaticProps();
	}

	public void LevelInitClient() {
#if !SWDS
		if (sv.IsDedicated())
			return;

		bool needsMapAccess = ModelRender.r_proplightingfromdisk.GetBool();
		if (needsMapAccess)
			g_pFileSystem.BeginMapAccess();

		Assert(LevelInitialized);
		Assert(!ClientInitialized);

		foreach (StaticProp prop in StaticProps) {
			clientleafsystem.CreateRenderableHandle(prop, true);
			if (!prop.ShouldDraw())
				continue;

			ClientRenderHandle_t handle = prop.RenderHandle();
			if (prop.LeafCount() > 0) {
				Span<ushort> leaves = MemoryMarshal.Cast<StaticPropLeafLump, ushort>(CollectionsMarshal.AsSpan(StaticPropLeaves)).Slice(prop.FirstLeaf(), prop.LeafCount());
				clientleafsystem.AddRenderableToLeaves(handle, leaves);
			}
			else {
				Vector3 origin = prop.GetCollisionOrigin();
				DevMsg($"Static prop in 0 leaves! @ {origin.X:F1}, {origin.Y:F1}, {origin.Z:F1}\n");
			}
		}

		PrecacheLighting();

		ClientInitialized = true;

		if (needsMapAccess)
			g_pFileSystem.EndMapAccess();
#endif
	}

	public void LevelShutdown() {
		if (!LevelInitialized)
			return;

		if (ClientInitialized)
			LevelShutdownClient();

		LevelInitialized = false;

		for (int i = 0; i < StaticPropDictList.Count; i++)
			mdlcache.UnlockStudioHdr(StaticPropDictList[i].MDL);

		StaticProps.Clear();
		StaticPropDictList.Clear();
		StaticPropFadeList.Clear();
	}

	public void LevelShutdownClient() {
		if (!ClientInitialized)
			return;

		Assert(LevelInitialized);

		for (int i = StaticProps.Count; --i >= 0;) {
			StaticProps[i].CleanUpRenderHandle();
#if !SWDS
			modelrender.SetStaticLighting(StaticProps[i].GetModelInstance(), null);
#endif
		}

#if !SWDS
		R.ClearStaticLightingCache();
#endif

		ClientInitialized = false;
	}

	public bool IsPropInPVS(IHandleEntity? handleEntity, ReadOnlySpan<byte> vis) {
		throw new NotImplementedException();
	}

	public ICollideable? GetStaticProp(IHandleEntity? handleEntity) {
		if (!IsStaticProp(handleEntity))
			return null;

		int index = handleEntity != null ? handleEntity.GetRefEHandle().GetEntryIndex() : -1;
		if (index < 0 || index > StaticProps.Count)
			return null;

		return StaticProps[index];
	}

	public void RecomputeStaticLighting() {
		throw new NotImplementedException();
	}

	public nint GetLightCacheHandleForStaticProp(IHandleEntity? handleEntity) {
		throw new NotImplementedException();
	}

	public bool IsStaticProp(IHandleEntity? handleEntity) => handleEntity == null || handleEntity.GetRefEHandle().GetSerialNumber() == (STATICPROP_EHANDLE_MASK >> Constants.NUM_ENT_ENTRY_BITS);
	public bool IsStaticProp(ClientEntityHandle handle) => handle.GetSerialNumber() == (STATICPROP_EHANDLE_MASK >> Constants.NUM_ENT_ENTRY_BITS);
	public bool IsStaticProp(in ClientEntityHandle handle) => handle.GetSerialNumber() == (STATICPROP_EHANDLE_MASK >> Constants.NUM_ENT_ENTRY_BITS);

	public int GetStaticPropIndex(IHandleEntity? handleEntity) => HandleEntityToIndex(handleEntity);

	public ICollideable GetStaticPropByIndex(int propIndex) {
		if (propIndex < StaticProps.Count)
			return StaticProps[propIndex];

		Assert(false);
		return null;
	}

	public void ComputePropOpacity(Vector3 viewOrigin, float factor) {
		LastViewOrigin = viewOrigin;
		LastViewFactor = factor;
	}

	public void TraceRayAgainstStaticProp(Ray ray, int staticPropIndex, Trace tr) {
		throw new NotImplementedException();
	}

	public void AddDecalToStaticProp(Vector3 rayStart, Vector3 rayEnd, int staticPropIndex, int decalIndex, bool doTrace, Trace tr) {
		// throw new NotImplementedException();
		DevWarning("AddDecalToStaticProp not implemented\n");
	}

	public void AddColorDecalToStaticProp(Vector3 rayStart, Vector3 rayEnd, int staticPropIndex, int decalIndex, bool doTrace, Trace tr, bool useColor, Color color) {
		throw new NotImplementedException();
	}

	public void AddShadowToStaticProp(ushort shadowHandle, IClientRenderable renderable) {
		Assert(renderable as StaticProp != null);

		StaticProp prop = (StaticProp)renderable;

		g_ShadowMgr.AddShadowToModel(shadowHandle, prop.GetModelInstance());
	}

	public void RemoveAllShadowsFromStaticProp(IClientRenderable renderable) {
		Assert(renderable as StaticProp != null);
		StaticProp prop = (StaticProp)renderable;
		if (prop.GetModelInstance() != MODEL_INSTANCE_INVALID)
			g_ShadowMgr.RemoveAllShadowsFromModel(prop.GetModelInstance());
	}

	public void GetStaticPropMaterialColorAndLighting(Trace trace, int staticPropIndex, out Vector3 lighting, out Vector3 matColor) {
		throw new NotImplementedException();
	}

	public void CreateVPhysicsRepresentations(IPhysicsEnvironment physenv, IVPhysicsKeyHandler defaults, object gameData) {
		throw new NotImplementedException();
	}

	public void GetAllStaticProps(List<ICollideable> output) {
		throw new NotImplementedException();
	}

	public void GetAllStaticPropsInAABB(Vector3 mins, Vector3 maxs, List<ICollideable> output) {
		throw new NotImplementedException();
	}

	public void GetAllStaticPropsInOBB(Vector3 origin, Vector3 extent1, Vector3 extent2, Vector3 extent3, List<ICollideable> output) {
		throw new NotImplementedException();
	}

	public bool PropHasBakedLightingDisabled(IHandleEntity? handleEntity) {
		int index = HandleEntityToIndex(handleEntity);

		StaticProp prop = StaticProps[index];

		return (prop.Flags() & (int)StaticPropFlags.NoPerVertexLighting) != 0;
	}

	public ref readonly Vector3 ViewOrigin() => ref LastViewOrigin;

	static ConVarRef? localplayer_visionflags;

	public void ComputePropOpacity(StaticProp prop) {
#if !SWDS
		if (modelInfo.ModelHasMaterialProxy(prop.GetModel()))
			modelInfo.RecomputeTranslucency(prop.GetModel(), prop.GetSkin(), prop.GetBody(), prop.GetClientRenderable(), (float)prop.GetFxBlend() / 255.0f);
#endif

		ConVarRef visionFlags = localplayer_visionflags ??= new("localplayer_visionflags");
		bool visionOverride = visionFlags.IsValid() && (visionFlags.GetInt() & 0x01) != 0;
		if (!HardwareConfig.SupportsPixelShaders_2_0())
			visionOverride = false;

		if (LastViewFactor < 0 || visionOverride) {
			prop.SetAlpha(255);
			ChangeRenderGroup(prop);
			return;
		}

		if ((prop.Flags() & (int)StaticPropFlags.Fades) != 0) {
			Assert(prop.FadeIndex() != -1);

			StaticPropFade fade = StaticPropFadeList[(int)prop.FadeIndex()];

			byte alpha;

			if ((prop.Flags() & (int)StaticPropFlags.ScreenSpaceFade) == 0) {
				MathLib.VectorSubtract(prop.GetRenderOrigin(), LastViewOrigin, out Vector3 v);
				MathLib.VectorScale(v, LastViewFactor, out v);

				alpha = 0;
				float sqDist = v.LengthSqr();
				if (sqDist < fade.MaxDistSq) {
					if (fade.MinDistSq >= 0 && sqDist > fade.MinDistSq) {
						int nAlpha = (int)(fade.FalloffFactor * (fade.MaxDistSq - sqDist));
						alpha = (byte)Math.Clamp(nAlpha, 0, 255);
					}
					else
						alpha = 255;
				}
			}
			else
				alpha = ComputeScreenFade(prop, fade.MinScreenWidth, fade.MaxScreenWidth, fade.FalloffFactor);

			prop.SetAlpha(alpha);
			ChangeRenderGroup(prop);
		}
		else {
			prop.SetAlpha(255);
			ChangeRenderGroup(prop);
		}

#if !SWDS
		{
			byte alpha = modelInfo.ComputeLevelScreenFade(prop.GetRenderOrigin(), prop.Radius(), prop.ForcedFadeScale());
			byte viewAlpha = modelInfo.ComputeViewScreenFade(prop.GetRenderOrigin(), prop.Radius(), prop.ForcedFadeScale());
			if (viewAlpha < alpha)
				alpha = viewAlpha;

			if (alpha < prop.GetFxBlend()) {
				prop.SetAlpha(alpha);
				ChangeRenderGroup(prop);
			}
		}
#endif
	}

	public void DrawStaticProps(ref InlineArray512<IClientRenderable> props, int count, bool shadowDepth, bool drawVCollideWireframe) {
		if (!r_drawstaticprops.GetBool())
			return;

		// todo: fast pipeline
		DrawStaticProps_Slow(ref props, count, shadowDepth, drawVCollideWireframe);
	}

	void DrawStaticProps_Slow(ref InlineArray512<IClientRenderable> props, int count, bool shadowDepth, bool drawVCollideWireframe) {
		StudioFlags flags = StudioFlags.Render;
		if (shadowDepth)
			flags |= StudioFlags.ShadowDepthTexture;
		if (drawVCollideWireframe)
			flags |= StudioFlags.WireframeVCollide;

		for (int i = 0; i < count; i++) {
			StaticProp prop = (StaticProp)props[i];
			prop.DrawModelSlow(flags);
		}
	}

	void DrawStaticProps_Fast(ref InlineArray512<IClientRenderable> props, int count, bool shadowDepth) => throw new NotImplementedException();
	void DrawStaticProps_FastPipeline(ref InlineArray512<IClientRenderable> props, int count, bool shadowDepth) => throw new NotImplementedException();

	void OutputLevelStats() => throw new NotImplementedException();
	void PrecacheLighting() { // FIXME: This is very slow... 
		Common.TimestampedLog("CStaticPropMgr::PrecacheLighting - start");

		for (int i = StaticProps.Count; --i >= 0;) {
			if (!StaticProps[i].ShouldDraw())
				continue;
			StaticProps[i].PrecacheLighting();
		}

		Common.TimestampedLog("CStaticPropMgr::PrecacheLighting - end");
	}

	void UnserializeModelDict(Stream buf) {
		int count = 0;
		buf.ReadToStruct(ref count);

		for (int i = 0; i < count; i++) {
			StaticPropDictLump lump = default;
			buf.ReadToStruct(ref lump);

			Span<byte> nameBytes = lump.Name;
			int len = nameBytes.IndexOf((byte)0);
			if (len < 0)
				len = nameBytes.Length;
			string name = System.Text.Encoding.ASCII.GetString(nameBytes[..len]);

			StaticPropDict dict = default;
			dict.Model = modelLoader.GetModelForName(name, ModelLoaderFlags.StaticProp);
			dict.MDL = modelInfo.GetCacheHandle(dict.Model);
			mdlcache.LockStudioHdr(dict.MDL);

			StaticPropDictList.Add(dict);
		}

#if DEBUG
		DevMsg($"UnserializeModelDict: {StaticPropDictList.Count} static prop models\n");
#endif
	}

	void UnserializeLeafList(Stream buf) {
		int count = 0;
		buf.ReadToStruct(ref count);

		StaticPropLeaves.Clear();
		for (int i = 0; i < count; i++) {
			StaticPropLeafLump leaf = default;
			buf.ReadToStruct(ref leaf);
			StaticPropLeaves.Add(leaf);
		}

#if DEBUG
		DevMsg($"UnserializeLeafList: {StaticPropLeaves.Count} static prop leaves\n");
#endif
	}

	void UnserializeModels(Stream buf) {
		int lumpVersion = ModelLoader.GameLumpVersion((int)GameLump.StaticProps);
		if (lumpVersion < 4) {
			Warning("Really old map format! Static props can't be loaded...\n");
			return;
		}

		int count = 0;
		buf.ReadToStruct(ref count);

		for (int i = 0; i < count; ++i) {
			StaticPropLump lump = default;
			switch (lumpVersion) {
				case 4: {
						StaticPropLumpV4 v = default;
						buf.ReadToStruct(ref v);
						lump = v;
						break;
					}
				case 5: {
						StaticPropLumpV5 v = default;
						buf.ReadToStruct(ref v);
						lump = v;
						break;
					}
				case 6: {
						StaticPropLumpV6 v = default;
						buf.ReadToStruct(ref v);
						lump = v;
						break;
					}
				case 7:
				case 10:
					if (MapLoadHelper.MapHeader.Version == 21) {
						StaticPropLumpV10_21 v = default;
						buf.ReadToStruct(ref v);
						lump = v;
					}
					else {
						StaticPropLumpV10 v = default;
						buf.ReadToStruct(ref v);
						lump = v;
					}
					break;
				case 9: {
						StaticPropLumpV9 v = default;
						buf.ReadToStruct(ref v);
						lump = v;
						break;
					}
				case 11: {
						StaticPropLumpV11 v = default;
						buf.ReadToStruct(ref v);
						lump = v;
						break;
					}
				default:
					Sys.Error($"Unexpected lump version {lumpVersion} while deserializing lumps.");
					break;
			}

			StaticProp prop = new();
			StaticProps.Add(prop);
			prop.Init(i, ref lump, StaticPropDictList[lump.PropType].Model!);

			if ((lump.Flags & (uint)StaticPropFlags.Fades) != 0) {
				prop.SetFadeIndex(StaticPropFadeList.Count);
				StaticPropFade fade = default;
				fade.Model = i;
				fade.MinDistSq = lump.FadeMinDist;
				fade.MaxDistSq = lump.FadeMaxDist;

				if ((lump.Flags & (uint)StaticPropFlags.ScreenSpaceFade) == 0) {
					fade.MinDistSq *= fade.MinDistSq;
					fade.MaxDistSq *= fade.MaxDistSq;
				}

				if (fade.MaxDistSq != fade.MinDistSq) {
					if ((lump.Flags & (uint)StaticPropFlags.ScreenSpaceFade) != 0)
						fade.FalloffFactor = 255.0f / (fade.MaxScreenWidth - fade.MinScreenWidth);
					else
						fade.FalloffFactor = 255.0f / (fade.MaxDistSq - fade.MinDistSq);
				}
				else
					fade.FalloffFactor = 255.0f;

				StaticPropFadeList.Add(fade);
			}

			prop.InsertPropIntoKDTree();
		}

#if DEBUG
		DevMsg($"UnserializeModels: {StaticProps.Count} static props\n");
#endif
	}
	void UnserializeStaticProps() {
		int size = ModelLoader.GameLumpSize((int)GameLump.StaticProps);
		if (size == 0)
			return;

		Common.TimestampedLog("UnserializeStaticProps - start");

		byte[] buf = new byte[size];
		if (ModelLoader.LoadGameLump((int)GameLump.StaticProps, buf)) {
			using MemoryStream stream = new(buf);
			Common.TimestampedLog("UnserializeModelDict");
			UnserializeModelDict(stream);
			Common.TimestampedLog("UnserializeLeafList");
			UnserializeLeafList(stream);
			Common.TimestampedLog("UnserializeModels");
			UnserializeModels(stream);
		}

		Common.TimestampedLog("UnserializeStaticProps - end");
	}

	int HandleEntityToIndex(IHandleEntity? handleEntity) {
		Assert(IsStaticProp(handleEntity));
		return handleEntity!.GetRefEHandle().GetEntryIndex();
	}

	byte ComputeScreenFade(StaticProp prop, float minSize, float maxSize, float falloffFactor) {
		MatRenderContextPtr renderContext = new(materials);

		float pixelWidth = renderContext.ComputePixelWidthOfSphere(prop.GetRenderOrigin(), prop.Radius());

		byte alpha = 0;
		if (pixelWidth > minSize) {
			if (maxSize >= 0 && pixelWidth < maxSize) {
				int nAlpha = (int)(falloffFactor * (pixelWidth - minSize));
				alpha = (byte)Math.Clamp(nAlpha, 0, 255);
			}
			else
				alpha = 255;
		}

		return alpha;
	}

	void ChangeRenderGroup(StaticProp prop) {
#if !SWDS
		RenderGroup opaqueRenderGroup = RenderGroup.OpaqueStatic;
		ClientRenderHandle_t renderHandle = prop.GetRenderHandle();
		Assert(renderHandle != INVALID_CLIENT_RENDER_HANDLE);
		if (prop.GetFxBlend() == 0)
			clientleafsystem.ChangeRenderableRenderGroup(renderHandle, opaqueRenderGroup);
		else if (prop.GetFxBlend() == 255) {
			RenderGroup nRenderGroup = prop.IsTransparent() ? RenderGroup.TranslucentEntity : opaqueRenderGroup;
			clientleafsystem.ChangeRenderableRenderGroup(renderHandle, nRenderGroup);
		}
		else
			clientleafsystem.ChangeRenderableRenderGroup(renderHandle, RenderGroup.TranslucentEntity);
#endif
	}
}


public class StaticProp : IClientUnknown, IClientRenderable, ICollideable
{
	Vector3 Origin;
	QAngle Angles;
	Model? Model;
	SpatialPartitionHandle_t Partition;
	ModelInstanceHandle_t ModelInstance;
	byte Alpha;
	byte Solid;
	byte Skin;
	byte flags;
	ushort firstLeaf;
	ushort leafCount;
	BaseHandle EntHandle;
	ClientRenderHandle_t renderHandle;
	nint fadeIndex;
	float forcedFadeScale;

	Vector3 RenderBBoxMin;
	Vector3 RenderBBoxMax;
	Matrix3x4 ModelToWorld;
	float radius;

	Vector3 WorldRenderBBoxMin;
	Vector3 WorldRenderBBoxMax;

	Vector3 LightingOrigin;

	public StaticProp() {
		ModelInstance = MODEL_INSTANCE_INVALID;
		Partition = PARTITION_INVALID_HANDLE;
		EntHandle = default;
		renderHandle = INVALID_CLIENT_RENDER_HANDLE;
		Alpha = 255;
	}

	public void SetRefEHandle(in BaseHandle handle) {
		// Only the static prop mgr should be setting this...
		Assert(false);
	}
	public ref readonly BaseHandle GetRefEHandle() => ref EntHandle;

	public ICollideable? GetCollideable() => this;
	public IClientNetworkable? GetClientNetworkable() => null;
	public IClientRenderable? GetClientRenderable() => this;
	public IClientEntity? GetIClientEntity() => null;
	public IClientThinkable? GetClientThinkable() => null;

	public ref readonly Vector3 OBBMinsPreScaled() => ref OBBMins();
	public ref readonly Vector3 OBBMaxsPreScaled() => ref OBBMaxs();
	public ref readonly Vector3 OBBMins() => throw new NotImplementedException();
	public ref readonly Vector3 OBBMaxs() => throw new NotImplementedException();

	public bool TestCollision(in Ray ray, Contents contentsMask, ref Trace tr) {
		Assert(false);
		return false;
	}
	public bool TestHitboxes(in Ray ray, Contents contentsMask, ref Trace tr) => false;

	public int GetCollisionModelIndex() => -1;
	public Model? GetCollisionModel() => Model;

	public ref readonly Vector3 GetCollisionOrigin() => ref Origin;
	public ref readonly QAngle GetCollisionAngles() => ref Angles;
	public ref readonly Matrix3x4 CollisionToWorldTransform() => ref ModelToWorld;

	public SolidType GetSolid() => (SolidType)Solid;
	public int GetSolidFlags() => 0;

	public IHandleEntity? GetEntityHandle() => this;

	public CollisionGroup GetCollisionGroup() => CollisionGroup.None;

	public void WorldSpaceTriggerBounds(out Vector3 vecWorldMins, out Vector3 vecWorldMaxs) {
		// This should never be called..
		Assert(false);
		vecWorldMins = default;
		vecWorldMaxs = default;
	}
	public void WorldSpaceSurroundingBounds(out Vector3 vecMins, out Vector3 vecMaxs) {
		vecMins = WorldRenderBBoxMin;
		vecMaxs = WorldRenderBBoxMax;
	}
	public bool ShouldTouchTrigger(int triggerSolidFlags) => false;
	public ref readonly Matrix3x4 GetRootParentToWorldTransform() => throw new NotImplementedException();

	public int GetBody() => 0;
	public int GetSkin() => 0;
	public ref readonly Vector3 GetRenderOrigin() => ref Origin;
	public ref readonly QAngle GetRenderAngles() => ref Angles;
	public bool ShouldDraw() => (flags & (byte)StaticPropFlags.NoDraw) == 0;
	public bool IsTransparent() => Alpha < 255 || modelInfo.IsTranslucent(Model);
	public bool IsTwoPass() => modelInfo.IsTranslucentTwoPass(Model);
	public void OnThreadedDrawSetup() { }
	public Model? GetModel() => Model;
	public int DrawModel(StudioFlags flags) {
#if !SWDS
		if (Alpha == 0 || Model == null)
			return 0;

		if (IsUsingStaticPropDebugModes() || (flags & StudioFlags.WireframeVCollide) != 0)
			return DrawModelSlow(flags);

		flags |= StudioFlags.StaticLighting;

		ModelRenderInfo info = default;
		InitModelRenderInfo(ref info, flags);
		studiorender.SetColorModulation(r_colormod);
		studiorender.SetAlphaModulation(r_blend);

		MatRenderContextPtr renderContext = new(materials);
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
		int drawn = modelrender.DrawModelEx(ref info);
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PopMatrix();

		return drawn;
#else
		return 0;
#endif
	}
	public void ComputeFxBlend() => g_StaticPropMgr.ComputePropOpacity(this);
	public int GetFxBlend() => Alpha;
	public void GetColorModulation(Span<float> color) => color[0] = color[1] = color[2] = 1;
	public bool LODTest() => true;
	public bool SetupBones(Span<Matrix3x4> boneToWorldOut, int maxBones, int boneMask, TimeUnit_t currentTime) {
		if (Model == null)
			return false;

		boneToWorldOut[0] = ModelToWorld;
		return true;
	}
	public void SetupWeights(Span<Matrix3x4> boneToWorld, Span<float> flexWeights, Span<float> flexDelayedWeights) { }
	public bool UsesFlexDelayedWeights() => false;
	public void DoAnimationEvents() => throw new NotImplementedException();
	public IPVSNotify? GetPVSNotifyInterface() => null;
	public void GetRenderBounds(out Vector3 mins, out Vector3 maxs) {
		mins = RenderBBoxMin;
		maxs = RenderBBoxMax;
	}
	public void GetRenderBoundsWorldspace(out Vector3 mins, out Vector3 maxs) {
		mins = WorldRenderBBoxMin;
		maxs = WorldRenderBBoxMax;
	}
	public bool ShouldReceiveProjectedTextures(ShadowFlags flags) {
		if ((flags & ShadowFlags.Flashlight) != 0)
			return true;
		else
			return false;
	}
	public bool GetShadowCastDistance(ref float dist, ShadowType shadowType) { return false; }
	public bool GetShadowCastDirection(ref Vector3 direction, ShadowType shadowType) { return false; }
	public bool UsesPowerOfTwoFrameBufferTexture() => throw new NotImplementedException();
	public bool UsesFullFrameBufferTexture() => throw new NotImplementedException();
	public ClientShadowHandle_t GetShadowHandle() => unchecked((ClientShadowHandle_t)~0);
	public ref ClientRenderHandle_t RenderHandle() => ref renderHandle;
	public void RecordToolMessage() { }
	public void GetShadowRenderBounds(out Vector3 mins, out Vector3 maxs, ShadowType shadowType) => GetRenderBounds(out mins, out maxs);
	public bool IsShadowDirty() => false;
	public void MarkShadowDirty(bool dirty) { }
	public IClientRenderable? GetShadowParent() => null;
	public IClientRenderable? FirstShadowChild() => null;
	public IClientRenderable? NextShadowPeer() => null;
	public ShadowType ShadowCastType() => ShadowType.None;
	public void CreateModelInstance() => Assert(false);
	public ModelInstanceHandle_t GetModelInstance() => ModelInstance;
	public int LookupAttachment(ReadOnlySpan<char> attachmentName) => -1;
	public bool GetAttachment(int number, out Vector3 origin, out QAngle angles) {
		origin = Origin;
		angles = Angles;
		return true;
	}
	public bool GetAttachment(int number, out Matrix3x4 matrix) => throw new NotImplementedException();
	public bool IgnoresZBuffer() => false;
	public Span<float> GetRenderClipPlane() => null;
	public ref readonly Matrix3x4 RenderableToWorldTransform() => ref ModelToWorld;
	public IClientUnknown GetIClientUnknown() => this;

	const nint INVALID_FADE_INDEX = ~0;

	static IMaterialSystemHardwareConfig hardwareConfig = Singleton<IMaterialSystemHardwareConfig>();
	public bool Init(int index, ref StaticPropLump lump, Model model) {
		EntHandle.Init(index, STATICPROP_EHANDLE_MASK >> Constants.NUM_ENT_ENTRY_BITS);
		Partition = PARTITION_INVALID_HANDLE;
		forcedFadeScale = lump.ForcedFadeScale;
		Origin = lump.Origin;
		Angles = lump.Angles;
		Model = model;
		firstLeaf = lump.FirstLeaf;
		leafCount = lump.LeafCount;
		Solid = lump.Solid;
		fadeIndex = INVALID_FADE_INDEX;

		StudioHeader? studioHdr = modelInfo.GetStudiomodel(Model);
		if (studioHdr != null) {
			if ((studioHdr.Flags & StudioHdrFlags.StaticProp) == 0)
				Warning("model used as a static prop, but not compiled as a static prop\n");

			if ((studioHdr.Flags & StudioHdrFlags.NoForcedFade) != 0)
				forcedFadeScale = 0.0f;
		}

		switch ((SolidType)Solid) {
			case SolidType.VPhysics:
			case SolidType.BBox:
			case SolidType.None:
				break;

			default:
				Warning($"CStaticProp::Init:  Map error, static_prop with bogus SOLID_ flag ({Solid})!\n");
				Solid = (byte)SolidType.None;
				break;
		}

		Alpha = 255;
		Skin = (byte)lump.Skin;
		flags = (byte)(lump.Flags & (uint)(StaticPropFlags.ScreenSpaceFade | StaticPropFlags.Fades | StaticPropFlags.NoPerVertexLighting));

		int currentDXLevel = hardwareConfig.GetDXSupportLevel();
		bool noDraw = lump.MinDXLevel != 0 && lump.MinDXLevel > currentDXLevel;
		noDraw = noDraw || (lump.MaxDXLevel != 0 && lump.MaxDXLevel < currentDXLevel);
		if (noDraw)
			flags |= (byte)StaticPropFlags.NoDraw;

		MathLib.AngleMatrix(lump.Angles, lump.Origin, out ModelToWorld);

		modelInfo.GetModelRenderBounds(Model, out RenderBBoxMin, out RenderBBoxMax);
		radius = RenderBBoxMin.DistTo(RenderBBoxMax) * 0.5f;
		MathLib.TransformAABB(ModelToWorld, RenderBBoxMin, RenderBBoxMax, out WorldRenderBBoxMin, out WorldRenderBBoxMax);

		if ((lump.Flags & (uint)StaticPropFlags.UseLightingOrigin) != 0)
			LightingOrigin = lump.LightingOrigin;
		else
			modelInfo.GetIlluminationPoint(Model, this, Origin, Angles, out LightingOrigin);

		if (!sv.IsDedicated() && Model != null) {
			// Mod_SetMaterialVarFlag(Model, MATERIAL_VAR_IGNORE_ALPHA_MODULATION, true); // todo
		}

		return true;
	}

	public void InsertPropIntoKDTree() {
		Assert(Partition == PARTITION_INVALID_HANDLE);
		if (Solid == (byte)SolidType.None)
			return;

		MathLib.AngleMatrix(Angles, Origin, out Matrix3x4 propToWorld);
		MathLib.TransformAABB(propToWorld, Model!.Mins, Model.Maxs, out Vector3 mins, out Vector3 maxs);

		if (Solid == (byte)SolidType.VPhysics) {
			VCollide? collide = modelInfo.GetVCollide(Model);
			if (collide != null && collide.SolidCount != 0) {
				physcollision.CollideGetAABB(out mins, out maxs, collide.Solids![0]!, Origin, Angles);
			}
			else {
				Warning("SOLID_VPHYSICS static prop with no vphysics model!\n");
				Solid = (byte)SolidType.None;
				return;
			}
		}

		Partition = SpatialPartition().CreateHandle(this,
			(SpatialPartitionListMask_t)(PartitionListMask.ClientSolidEdicts | PartitionListMask.ClientStaticProps |
				PartitionListMask.EngineSolidEdicts | PartitionListMask.EngineStaticProps),
			mins, maxs);

		Assert(Partition != PARTITION_INVALID_HANDLE);
	}
	public void RemovePropFromKDTree() => throw new NotImplementedException();

	public void PrecacheLighting() {
#if !SWDS
		if (ModelInstance == MODEL_INSTANCE_INVALID) {
			LightCacheHandle_t lightCacheHandle = R.CreateStaticLightingCache(LightingOrigin, WorldRenderBBoxMin, WorldRenderBBoxMax);
			ModelInstance = modelrender.CreateInstance(this, lightCacheHandle);
		}
#endif
	}
	public void RecomputeStaticLighting() => throw new NotImplementedException();

	public int LeafCount() => leafCount;
	public int FirstLeaf() => firstLeaf;
	public LightCacheHandle_t GetLightCacheHandle() => throw new NotImplementedException();
	public void SetModelInstance(ModelInstanceHandle_t handle) => ModelInstance = handle;
	public void SetRenderHandle(ClientRenderHandle_t handle) => renderHandle = handle;
	public void CleanUpRenderHandle() {
		if (renderHandle != INVALID_CLIENT_RENDER_HANDLE) {
#if !SWDS
			clientleafsystem.RemoveRenderable(renderHandle);
#endif
			renderHandle = INVALID_CLIENT_RENDER_HANDLE;
		}
	}
	public ClientRenderHandle_t GetRenderHandle() => renderHandle;
	public void SetAlpha(byte alpha) => Alpha = alpha;

	public void CreateVPhysics(IPhysicsEnvironment physenv, IVPhysicsKeyHandler defaults, object gameData) => throw new NotImplementedException();

	public float Radius() => radius;
	public int Flags() => flags;

	public void SetFadeIndex(nint index) => fadeIndex = index;
	public nint FadeIndex() => fadeIndex;
	public float ForcedFadeScale() => forcedFadeScale;
	public int DrawModelSlow(StudioFlags flags) {
#if !SWDS
		if (!r_drawstaticprops.GetBool())
			return 0;

		if (r_drawstaticprops.GetInt() == 2)
			flags |= StudioFlags.Wireframe;

		if (Alpha == 0 || Model == null)
			return 0;

		// todo: r_colorstaticprops, DisplayStaticPropInfo, debug overlays, vcollide wireframe

		flags |= StudioFlags.StaticLighting;

		ModelRenderInfo info = default;
		InitModelRenderInfo(ref info, flags);
		studiorender.SetColorModulation(r_colormod);
		studiorender.SetAlphaModulation(r_blend);

		MatRenderContextPtr renderContext = new(materials);
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
		int drawn = modelrender.DrawModelEx(ref info);
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PopMatrix();

		return drawn;
#else
		return 0;
#endif
	}

	void DisplayStaticPropInfo(int infoType) => throw new NotImplementedException();
	void InitModelRenderInfo(ref ModelRenderInfo info, StudioFlags flags) {
		info.Origin = Origin;
		info.Angles = Angles;
		info.Renderable = this;
		info.Model = Model;
		info.ModelToWorld = ModelToWorld;
		info.LightingOrigin = LightingOrigin;
		info.Flags = flags;
		info.EntityIndex = -1;
		info.Skin = Skin;
		info.Body = 0;
		info.HitboxSet = 0;
		info.Instance = ModelInstance;
	}
}
