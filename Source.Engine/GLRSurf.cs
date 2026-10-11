global using static Source.Engine.GLRSurfGlobals;
global using static Source.Engine.GLRSurf;

using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.MaterialSystem;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Source.Common.Commands;

namespace Source.Engine;

public struct FogState
{
	public MaterialFogMode FogMode;
	public float FogStart;
	public float FogEnd;
	public InlineArray3<float> FogColor;
	public bool FogEnabled;
}

public struct FogVolumeInfo
{
	public FogState State;
	public bool InFogVolume;
	public float FogSurfaceZ;
	public float FogMinZ;
	public int FogVolumeID;
}

public struct CachedConvars
{
	public int DrawLeaf;
	public bool DrawWorld;
	public bool DrawFuncDetail;
}

public struct VertexFormatList
{
	public ushort NumBatches;
	public ushort FirstBatch;
	public IMesh? Mesh;
	public int MeshOrder;
}

public struct BatchList
{
	public SurfaceHandle_t SurfID;
	public ushort FirstIndex;
	public ushort NumIndex;
}

public ref struct EnumLeafBoxInfo<T> where T : ISpatialLeafEnumerator
{
	public Vector3 BoxMax;
	public Vector3 BoxMin;
	public Vector3 BoxCenter;
	public Vector3 BoxHalfDiagonal;
	public ref T Iterator;
	public nint Context;
}

public struct ShaderDebug
{
	public bool Wireframe;
	public bool Normals;
	public bool Luxels;
	public bool BumpBasis;
	public bool SurfaceMaterials;
	public bool AnyDebug;
	public int SurfaceID;
	public void TestAnyDebug() => AnyDebug = Wireframe || Normals || Luxels || BumpBasis || SurfaceID != 0 || SurfaceMaterials;
}

public static class GLRSurfGlobals
{
	public const int FRUSTUM_CLIP_RIGHT = 1 << (int)FrustumPlane.Right;
	public const int FRUSTUM_CLIP_LEFT = 1 << (int)FrustumPlane.Left;
	public const int FRUSTUM_CLIP_TOP = 1 << (int)FrustumPlane.Top;
	public const int FRUSTUM_CLIP_BOTTOM = 1 << (int)FrustumPlane.Bottom;
	public const int FRUSTUM_CLIP_MASK = FRUSTUM_CLIP_RIGHT | FRUSTUM_CLIP_LEFT | FRUSTUM_CLIP_TOP | FRUSTUM_CLIP_BOTTOM;
	public const int FRUSTUM_CLIP_IN_AREA = unchecked((int)0x80000000);
	public const int FRUSTUM_CLIP_ALL = FRUSTUM_CLIP_MASK;
	public const int FRUSTUM_SUPPRESS_CLIPPING = FRUSTUM_CLIP_IN_AREA;
	public const int BRUSHMODEL_DECAL_SORT_GROUP = (int)MatSortGroup.Max;

	public static int r_surfacevisframe = 0;

	public static readonly EngineBSPTree g_ToolBSPTree = new();
	public static CachedConvars s_ShaderConvars;
	public static Frustum_t g_Frustum = new();
	public static ShaderDebug g_ShaderDebug = new();
	public static Vector3 ModelOrg;
	public static bool r_drawtopview = false;
	public static int g_MaxLeavesVisible = 512;

	public const float BACKFACE_EPSILON = -0.01f;
	public const int MAX_VERTEX_FORMAT_CHANGES = 256;

	public readonly static ConVar r_drawtranslucentworld = new("r_drawtranslucentworld", "1", FCvar.Cheat);
	public readonly static ConVar mat_forcedynamic = new("mat_forcedynamic", "0", FCvar.Cheat);
	public readonly static ConVar r_drawleaf = new("r_drawleaf", "-1", FCvar.Cheat, "Draw the specified leaf.");
	public readonly static ConVar r_drawworld = new("r_drawworld", "1", FCvar.Cheat, "Render the world.");
	public readonly static ConVar r_drawfuncdetail = new("r_drawfuncdetail", "1", FCvar.Cheat, "Render func_detail");
	public readonly static ConVar fog_enable_water_fog = new("fog_enable_water_fog", "1", FCvar.Cheat);
	public readonly static ConVar r_fastzreject = new("r_fastzreject", "0", 0, "Activate/deactivates a fast z-setting algorithm to take advantage of hardware with fast z reject. Use -1 to default to hardware settings");
	public readonly static ConVar r_fastzrejectdisp = new("r_fastzrejectdisp", "0", 0, "Activates/deactivates fast z rejection on displacements (360 only). Only active when r_fastzreject is on.");
	public readonly static ConVar r_frustumcullworld = new("r_frustumcullworld", "1", FCvar.Cheat);
	public readonly static ConVar r_spewleaf = new("r_spewleaf", "0", 0);

	public static int WireFrameMode() {
		if (Host.CanCheat())
			return mat_wireframe.GetInt();
		return 0;
	}

	public static bool ShouldDrawInWireFrameMode() {
		if (Host.CanCheat())
			return mat_wireframe.GetInt() != 0;
		return false;
	}
}

public class WorldRenderList : IWorldRenderList
{
	public MSurfaceSortList SortList = new();
	public MSurfaceSortList DispSortList = new();
	public MSurfaceSortList AlphaSortList = new();
	public MSurfaceSortList DispAlphaSortList = new();

	public readonly List<ShadowDecalHandle_t>[] ShadowHandles = new List<ShadowDecalHandle_t>[(int)MatSortGroup.Max].InstantiateArray();
	public readonly List<SurfaceHandle_t>[] DlightSurfaces = new List<SurfaceHandle_t>[(int)MatSortGroup.Max].InstantiateArray();

	public readonly List<LeafIndex_t> VisibleLeaves = [];
	public readonly List<LeafFogVolume_t> VisibleLeafFogVolumes = [];

	public VarBitSet VisitedSurfs = new();
	public bool SkyVisible;
	int Refs = 1;

	static readonly Stack<WorldRenderList> g_Pool = new();

	public static WorldRenderList FindOrCreateList(int numSurfaces) {
		WorldRenderList p = g_Pool.Count > 0 ? g_Pool.Pop() : new();
		if (p.VisitedSurfs.GetNumBits() == 0)
			p.Init(numSurfaces);
		else
			p.AddRef();

		AssertMsg(p.VisitedSurfs.GetNumBits() == numSurfaces, "World render list pool not cleared between maps");

		return p;
	}

	public static void PurgeAll() {
		while (g_Pool.Count > 0) {
			WorldRenderList p = g_Pool.Pop();
			p.Purge();
		}
	}

	public bool OnFinalRelease() {
		Reset();
		g_Pool.Push(this);
		return false;
	}

	public void Init(int numSurfaces) {
		SortList.Init(materials.GetNumSortIDs(), 512);
		AlphaSortList.Init(g_MaxLeavesVisible, 64);
		DispSortList.Init(materials.GetNumSortIDs(), 32);
		DispAlphaSortList.Init(g_MaxLeavesVisible, 32);
		VisitedSurfs.Resize(numSurfaces);
		SkyVisible = false;
	}

	public void Purge() {
		g_MaxLeavesVisible = Math.Max(g_MaxLeavesVisible, VisibleLeaves.Count);

		VisibleLeaves.Clear();
		VisibleLeafFogVolumes.Clear();
		for (int i = 0; i < (int)MatSortGroup.Max; i++) {
			ShadowHandles[i].Clear();
			DlightSurfaces[i].Clear();
		}
		SortList.Shutdown();
		AlphaSortList.Shutdown();
		DispSortList.Shutdown();
		DispAlphaSortList.Shutdown();
	}

	public void Reset() {
		g_MaxLeavesVisible = Math.Max(g_MaxLeavesVisible, VisibleLeaves.Count);
		SortList.Reset();
		AlphaSortList.Reset();
		DispSortList.Reset();
		DispAlphaSortList.Reset();

		SkyVisible = false;
		for (int j = 0; j < (int)MatSortGroup.Max; ++j) {
			ShadowHandles[j].Clear();
			DlightSurfaces[j].Clear();
		}

		VisibleLeaves.Clear();
		VisibleLeafFogVolumes.Clear();

		VisitedSurfs.ClearAll();
	}

	public int AddRef() => ++Refs;

	public int Release() {
		int result = --Refs;
		if (result != 0)
			return result;

		OnFinalRelease();
		return 0;
	}
}

public static class GLRSurf
{
#if !SWDS
	static readonly RenderView RenderView = (RenderView)Singleton<IRenderView>();
#endif

	public static void ModulateMaterial(IMaterial material, Span<float> oldColor) {
#if !SWDS
		if (RenderView.IsBlendingOrModulating) {
			oldColor[3] = material.GetAlphaModulation();
			material.GetColorModulation(out float r, out float g, out float b);
			oldColor[0] = r;
			oldColor[1] = g;
			oldColor[2] = b;
			material.AlphaModulate(RenderView.r_blend);
			material.ColorModulate(RenderView.r_colormod.X, RenderView.r_colormod.Y, RenderView.r_colormod.Z);
		}
#endif
	}

	public static void UnModulateMaterial(IMaterial material, Span<float> oldColor) {
#if !SWDS
		if (RenderView.IsBlendingOrModulating) {
			material.AlphaModulate(oldColor[3]);
			material.ColorModulate(oldColor[0], oldColor[1], oldColor[2]);
		}
#endif
	}

	public static void Shader_BrushBegin(Model? model, IClientEntity? baseEntity = null) {
		// todo
	}
	public static void Shader_BrushSurface(SurfaceHandle_t surfID, Model? model, IClientEntity? baseEntity = null) => throw new NotImplementedException();
	public static void Shader_BrushEnd(IMatRenderContext renderContext, in Matrix4x4? brushToWorld, Model? model, bool shadowDepth, IClientEntity? baseEntity = null) {
		if (shadowDepth)
			return;

#if !SWDS
		Render.DecalSurfaceDraw(renderContext, BRUSHMODEL_DECAL_SORT_GROUP);
#endif
		g_ShadowMgr.DrawFlashlightDecals(BRUSHMODEL_DECAL_SORT_GROUP, false);
		g_ShadowMgr.RenderProjectedTextures(brushToWorld);
	}
	public static void BuildMSurfaceVertexArrays(WorldBrushData brushData, SurfaceHandle_t surfID, float overbright, MeshBuilder builder) => throw new NotImplementedException();

	public static void BuildIndicesForSurface(ref MeshBuilder meshBuilder, SurfaceHandle_t surfID) {
		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
		int surfTriCount = ModelLoader.MSurf_VertCount(ref surface) - 2;
		ushort startVert = ModelLoader.MSurf_VertBufferIndex(ref surface);
		Assert(startVert != 0xFFFF);

		switch (surfTriCount) {
			case 1:
				meshBuilder.FastIndex(startVert);
				meshBuilder.FastIndex((ushort)(startVert + 1));
				meshBuilder.FastIndex((ushort)(startVert + 2));
				break;

			case 2:
				meshBuilder.FastIndex(startVert);
				meshBuilder.FastIndex((ushort)(startVert + 1));
				meshBuilder.FastIndex((ushort)(startVert + 2));
				meshBuilder.FastIndex(startVert);
				meshBuilder.FastIndex((ushort)(startVert + 2));
				meshBuilder.FastIndex((ushort)(startVert + 3));
				break;

			default: {
					for (ushort v = 0; v < surfTriCount; ++v) {
						meshBuilder.FastIndex(startVert);
						meshBuilder.FastIndex((ushort)(startVert + v + 1));
						meshBuilder.FastIndex((ushort)(startVert + v + 2));
					}
				}
				break;
		}
	}

	public static void BuildIndicesForWorldSurface(ref MeshBuilder meshBuilder, SurfaceHandle_t surfID, WorldBrushData pData) {
		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID, pData);
		if (ModelLoader.SurfaceHasPrims(ref surface)) {
			ref BSPMPrimitive prim = ref pData.Primitives![ModelLoader.MSurf_FirstPrimID(ref surface, pData)];
			Assert(prim.VertCount == 0);
			ushort startVert = ModelLoader.MSurf_VertBufferIndex(ref surface);
			Assert(prim.IndexCount == (ModelLoader.MSurf_VertCount(ref surface) - 2) * 3);

			for (int primIndex = 0; primIndex < prim.IndexCount; primIndex++)
				meshBuilder.FastIndex((ushort)(pData.PrimIndices![prim.FirstIndex + primIndex] + startVert));
		}
		else
			BuildIndicesForSurface(ref meshBuilder, surfID);
	}

	public static int R_GetBrushModelPlaneCount(Model model) => throw new NotImplementedException();
	public static ref readonly CollisionPlane R_GetBrushModelPlane(Model model, int index, out Vector3 origin) => throw new NotImplementedException();
	public static void Surf_ComputeCentroid(SurfaceHandle_t surfID, out Vector3 vecCentroid) => throw new NotImplementedException();
	public static int SortInfoToLightmapPage(int sortID) => throw new NotImplementedException();
	public static IWorldRenderList AllocWorldRenderList() => WorldRenderList.FindOrCreateList(host_state!.WorldBrush!.NumSurfaces);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool VisitSurface(ref VarBitSet visitedSurfs, SurfaceHandle_t surfID) => !visitedSurfs.TestAndSet(surfID);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void MarkSurfaceVisited(ref VarBitSet visitedSurfs, SurfaceHandle_t surfID) => visitedSurfs.Set(surfID);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool VisitedSurface(ref VarBitSet visitedSurfs, int index) => visitedSurfs.IsBitSet(index);

	public static void R_DrawTopView(bool enable) => throw new NotImplementedException();
	public static void R_TopViewBounds(in Vector2 mins, in Vector2 maxs) => throw new NotImplementedException();
	public static bool DlightSurfaceSetQueuingFlag(SurfaceHandle_t surfID) => false;

	public static void Shader_TranslucentWorldSurface(WorldRenderList renderList, SurfaceHandle_t surfID) {
		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
		Assert(!ModelLoader.SurfaceHasDispInfo(ref surface) && (renderList.VisibleLeaves.Count > 0));

		// Hook into the chain of translucent objects for this leaf
		int sortGroup = ModelLoader.MSurf_SortGroup(ref surface);
		renderList.AlphaSortList.AddSurfaceToTail(ref surface, sortGroup, (short)(renderList.VisibleLeaves.Count - 1));
		if ((ModelLoader.MSurf_Flags(ref surface) & (SurfDraw.HasLightStyles | SurfDraw.HasDLight)) != 0) {
			renderList.DlightSurfaces[sortGroup].Add(surfID);
			DlightSurfaceSetQueuingFlag(surfID);
		}
	}
	static void Shader_WorldSurface(WorldRenderList renderList, SurfaceHandle_t surfID) {
		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
		Assert(!ModelLoader.SurfaceHasDispInfo(ref surface));

		int sortGroup = ModelLoader.MSurf_SortGroup(ref surface);

#if !SWDS
		if (ModelLoader.SurfaceHasDecals(ref surface))
			Render.DecalSurfaceAdd(surfID, sortGroup);
#endif

		int materialSortID = ModelLoader.MSurf_MaterialSortID(ref surface);

		if ((ModelLoader.MSurf_Flags(ref surface) & (SurfDraw.HasLightStyles | SurfDraw.HasDLight)) != 0) {
			renderList.DlightSurfaces[sortGroup].Add(surfID);
			if (!DlightSurfaceSetQueuingFlag(surfID))
				renderList.SortList.AddSurfaceToTail(ref surface, sortGroup, (short)materialSortID);
		}
		else
			renderList.SortList.AddSurfaceToTail(ref surface, sortGroup, (short)materialSortID);
	}

	public static void Shader_TranslucentDisplacementSurface(WorldRenderList renderList, SurfaceHandle_t surfID) {
		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
		Assert(ModelLoader.SurfaceHasDispInfo(ref surface) && (renderList.VisibleLeaves.Count > 0));

		int sortGroup = ModelLoader.MSurf_SortGroup(ref surface);
		if ((ModelLoader.MSurf_Flags(ref surface) & (SurfDraw.HasLightStyles | SurfDraw.HasDLight)) != 0) {
			renderList.DlightSurfaces[sortGroup].Add(surfID);
			if (!DlightSurfaceSetQueuingFlag(surfID))
				renderList.DispAlphaSortList.AddSurfaceToTail(ref surface, sortGroup, (short)(renderList.VisibleLeaves.Count - 1));
		}
		else
			renderList.DispAlphaSortList.AddSurfaceToTail(ref surface, sortGroup, (short)(renderList.VisibleLeaves.Count - 1));
	}

	public static void Shader_DisplacementSurface(WorldRenderList renderList, SurfaceHandle_t surfID) {
		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
		Assert(ModelLoader.SurfaceHasDispInfo(ref surface));

		int sortGroup = ModelLoader.MSurf_SortGroup(ref surface);
		int materialSortID = ModelLoader.MSurf_MaterialSortID(ref surface);
		if ((ModelLoader.MSurf_Flags(ref surface) & (SurfDraw.HasLightStyles | SurfDraw.HasDLight)) != 0) {
			renderList.DlightSurfaces[sortGroup].Add(surfID);
			if (!DlightSurfaceSetQueuingFlag(surfID))
				renderList.DispSortList.AddSurfaceToTail(ref surface, sortGroup, (short)materialSortID);
		}
		else
			renderList.DispSortList.AddSurfaceToTail(ref surface, sortGroup, (short)materialSortID);
	}
	public static void Shader_DrawSurfaceDynamic(IMatRenderContext renderContext, SurfaceHandle_t surfID, bool shadowDepth) {
		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);

		if (!ModelLoader.SurfaceHasPrims(ref surface)) {
			IMesh mesh = renderContext.GetDynamicMesh();
			MeshBuilder meshBuilder = new();
			meshBuilder.Begin(mesh, MaterialPrimitiveType.Polygon, ModelLoader.MSurf_VertCount(ref surface));
			MatSys.BuildMSurfaceVertexArrays(host_state!.WorldBrush!, ref surface, IMaterialSystem.OVERBRIGHT, ref meshBuilder);
			meshBuilder.End();
			mesh.Draw();
			return;
		}

		int firstPrim = ModelLoader.MSurf_FirstPrimID(ref surface, host_state!.WorldBrush!);
		ref BSPMPrimitive prim = ref host_state.WorldBrush!.Primitives![firstPrim];

		if (prim.VertCount != 0) {
			IMesh mesh = renderContext.GetDynamicMesh(false);
			MeshBuilder meshBuilder = new();
			for (int i = 0; i < ModelLoader.MSurf_NumPrims(ref surface, host_state.WorldBrush); i++) {
				ref BSPMPrimitive current = ref host_state.WorldBrush.Primitives![firstPrim + i];
				switch (current.Type) {
					case BSPPrimType.TriList:
						meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, current.VertCount, current.IndexCount);
						break;
					case BSPPrimType.TriStrip:
						meshBuilder.Begin(mesh, MaterialPrimitiveType.TriangleStrip, current.VertCount, current.IndexCount);
						break;
					default:
						Assert(false);
						return;
				}
				Assert(current.IndexCount != 0);
				MatSys.BuildMSurfacePrimVerts(current.Type, host_state.WorldBrush, ref current, ref meshBuilder, ref surface);
				MatSys.BuildMSurfacePrimIndices(current.Type, host_state.WorldBrush, ref current, ref meshBuilder);
				meshBuilder.End();
				mesh.Draw();
			}
		}
		else {
			IMesh mesh = renderContext.GetDynamicMesh();
			MeshBuilder meshBuilder = new();
			meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, ModelLoader.MSurf_VertCount(ref surface), prim.IndexCount);
			MatSys.BuildMSurfaceVertexArrays(host_state.WorldBrush, ref surface, IMaterialSystem.OVERBRIGHT, ref meshBuilder);
			for (int primIndex = 0; primIndex < prim.IndexCount; primIndex++)
				meshBuilder.FastIndex(host_state.WorldBrush.PrimIndices![prim.FirstIndex + primIndex]);
			meshBuilder.End();
			mesh.Draw();
		}
	}
	public static void Shader_DrawSurfaceStatic(SurfaceHandle_t surfID) => throw new NotImplementedException();
	static void Shader_SetChainLightmapState(IMatRenderContext renderContext, SurfaceHandle_t surfID) => throw new NotImplementedException();
	public static void Shader_SetChainTextureState(IMatRenderContext renderContext, SurfaceHandle_t surfID, IClientEntity? baseEntity, bool shadowDepth) => throw new NotImplementedException();
	public static void Shader_DrawDynamicChain(in MSurfaceSortList sortList, in SurfaceSortGroup group, bool shadowDepth) => throw new NotImplementedException();
	public static void Shader_DrawChainsDynamic(in MSurfaceSortList sortList, int sortGroup, bool shadowDepth) => throw new NotImplementedException();

	static InlineArray256<VertexFormatList> s_meshList;
	static InlineArray256<int> s_meshMap;
	static readonly List<BatchList> s_batchList = new(512);
	static readonly List<SurfaceSortGroup> s_dynamicGroups = new(8);

	public static void Shader_DrawChainsStatic(in MSurfaceSortList sortList, int sortGroup, bool shadowDepth) {
		int meshListCount = 0;
		Span<VertexFormatList> meshList = s_meshList;
		Span<int> meshMap = s_meshMap;

		List<BatchList> batchList = s_batchList;
		List<SurfaceSortGroup> dynamicGroups = s_dynamicGroups;

		batchList.Clear();
		dynamicGroups.Clear();

		bool bWarn = true;

		bool skipBind = false;
		if (MatSysInterface.MaterialSystemConfig.Fullbright == 1)
			skipBind = true;

		Span<SurfaceSortGroup> groupList = sortList.GetSortList(sortGroup).AsSpan();
		int count = groupList.Length;

		int listIndex = 0;

		using MatRenderContextPtr renderContext = new(materials);

		int nMaxIndices = renderContext.GetMaxIndicesToRender();
		while (listIndex < count) {
			ref SurfaceSortGroup groupBase = ref groupList[listIndex];
			ref BSPMSurface2 surfIDBase = ref sortList.GetSurfaceAtHead(in groupBase);
			int sortIDBase = ModelLoader.MSurf_MaterialSortID(ref surfIDBase);
			IMesh pBuildMesh = renderContext.GetDynamicMesh(false, MatSys.WorldStaticMeshes[sortIDBase]);
			MeshBuilder meshBuilder = new();
			meshBuilder.Begin(pBuildMesh, MaterialPrimitiveType.Triangles, 0, nMaxIndices);
			IMesh? lastMesh = null;
			int indexCount = 0;
			int meshIndex = -1;

			for (; listIndex < count; listIndex++) {
				ref SurfaceSortGroup group = ref groupList[listIndex];
				ref BSPMSurface2 surfID = ref sortList.GetSurfaceAtHead(in group);
				if ((ModelLoader.MSurf_Flags(ref surfID) & SurfDraw.Dynamic) != 0) {
					dynamicGroups.Add(group);
					continue;
				}

				Assert(group.TriangleCount > 0);
				int numIndex = group.TriangleCount * 3;
				if (indexCount + numIndex > nMaxIndices) {
					if (numIndex > nMaxIndices) {
						DevMsg($"Too many faces (max {nMaxIndices}) with the same material in scene!\n");
						break;
					}

					lastMesh = null;
					break;
				}

				int sortID = ModelLoader.MSurf_MaterialSortID(ref surfID);

				if (!ReferenceEquals(MatSys.WorldStaticMeshes[sortID], lastMesh)) {
					if (meshListCount < MAX_VERTEX_FORMAT_CHANGES - 1) {
						lastMesh = MatSys.WorldStaticMeshes[sortID];
						Assert(lastMesh != null);
						meshList[meshListCount++] = new VertexFormatList { NumBatches = 0, FirstBatch = (ushort)batchList.Count, Mesh = lastMesh, MeshOrder = RuntimeHelpers.GetHashCode(lastMesh) };
						meshIndex = meshListCount - 1;
					}
					else {
						if (bWarn) {
							Warning($"Too many (max {MAX_VERTEX_FORMAT_CHANGES - 1}) vertex format changes in frame, whole world not rendered\n");
							bWarn = false;
						}
						continue;
					}
				}

				batchList.Add(new BatchList { FirstIndex = (ushort)indexCount, SurfID = ModelLoader.MSurf_Index(ref surfID), NumIndex = (ushort)numIndex });
				Assert(indexCount + numIndex < nMaxIndices);
				indexCount += numIndex;

				meshList[meshIndex].NumBatches++;

				for (short blockIndex = group.ListHead; blockIndex != -1; blockIndex = sortList.GetSurfaceBlock(blockIndex).NextBlock) {
					ref MaterialList matList = ref sortList.GetSurfaceBlock(blockIndex);
					for (int idx = 0; idx < matList.Count; ++idx) {
						SurfaceHandle_t surfIDList = (SurfaceHandle_t)matList.Surfaces[idx];
						BuildIndicesForWorldSurface(ref meshBuilder, surfIDList, host_state.WorldBrush!);
					}
				}
			}

			meshBuilder.End(false, false);

			int meshTotal = meshListCount;

			for (int i = 0; i < meshTotal; i++) {
				meshMap[i] = i;
			}

			bool swapped = true;
			while (swapped) {
				swapped = false;
				for (int i = 1; i < meshTotal; i++) {
					if (meshList[meshMap[i]].MeshOrder < meshList[meshMap[i - 1]].MeshOrder) {
						(meshMap[i - 1], meshMap[i]) = (meshMap[i], meshMap[i - 1]);
						swapped = true;
					}
				}
			}

			renderContext.BeginBatch(pBuildMesh);
			Span<BatchList> batchL = batchList.AsSpan();
			for (int m = 0; m < meshTotal; m++) {
				ref VertexFormatList mesh = ref meshList[meshMap[m]];
				IMaterial? pBindMaterial = MatSys.MaterialSortInfoArray![ModelLoader.MSurf_MaterialSortID(ref ModelLoader.SurfaceHandleFromIndex(batchL[mesh.FirstBatch].SurfID))].Material;
				Assert(mesh.Mesh != null);
				renderContext.BindBatch(mesh.Mesh!, pBindMaterial);

				for (int b = 0; b < mesh.NumBatches; b++) {
					ref BatchList batch = ref batchL[b + mesh.FirstBatch];
					IMaterial pDrawMaterial = MatSys.MaterialSortInfoArray![ModelLoader.MSurf_MaterialSortID(ref ModelLoader.SurfaceHandleFromIndex(batch.SurfID))].Material!;

					if (shadowDepth) {
						// TODO!!!!!!!!!!
						throw new NotImplementedException();
					}
					else {
						renderContext.Bind(pDrawMaterial, null);

						if (skipBind) {
							if ((ModelLoader.MSurf_Flags(ref ModelLoader.SurfaceHandleFromIndex(batch.SurfID)) & SurfDraw.BumpLight) != 0)
								renderContext.BindLightmapPage(StandardLightmap.WhiteBump);
							else
								renderContext.BindLightmapPage(StandardLightmap.White);
						}
						else
							renderContext.BindLightmapPage(MatSys.MaterialSortInfoArray![ModelLoader.MSurf_MaterialSortID(ref ModelLoader.SurfaceHandleFromIndex(batch.SurfID))].LightmapPageID);
					}
					renderContext.DrawBatch(batch.FirstIndex, batch.NumIndex);
				}
			}
			renderContext.EndBatch();

			if (lastMesh != null || meshTotal == 0)
				break;

			meshListCount = 0;
			batchList.Clear();
		}

		for (int i = 0; i < dynamicGroups.Count; i++)
			Shader_DrawDynamicChain(sortList, dynamicGroups[i], shadowDepth);
	}

	public static void DrawSurfaceID(SurfaceHandle_t surfID, in Vector3 vecCentroid) => throw new NotImplementedException();
	public static void DrawSurfaceIDAsInt(SurfaceHandle_t surfID, in Vector3 vecCentroid) => throw new NotImplementedException();
	public static void DrawSurfaceMaterial(SurfaceHandle_t surfID, in Vector3 vecCentroid) => throw new NotImplementedException();
	public static void Shader_DrawSurfaceDebuggingInfo(List<SurfaceHandle_t> surfaceList, SurfaceDebugFunc func) => throw new NotImplementedException();
	public static void Shader_DrawWireframePolygons(List<SurfaceHandle_t> surfaceList) {
		int lineCount = 0;
		for (int i = 0; i < surfaceList.Count; i++) {
			int count = ModelLoader.MSurf_VertCount(ref ModelLoader.SurfaceHandleFromIndex(surfaceList[i]));
			if (count >= 3)
				lineCount += count;
		}

		if (lineCount == 0)
			return;

		using MatRenderContextPtr renderContext = new(materials);

#if !SWDS
		renderContext.Bind(MatSys.MaterialWorldWireframe!, null);
#endif
		IMesh mesh = renderContext.GetDynamicMesh(false);
		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Lines, lineCount);

		for (int i = 0; i < surfaceList.Count; i++) {
			SurfaceHandle_t surfID = surfaceList[i];
			ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
			Assert(!ModelLoader.SurfaceHasDispInfo(ref surface));

			int count = ModelLoader.MSurf_VertCount(ref surface);
			if (count >= 3) {
				int firstVertIndex = ModelLoader.MSurf_FirstVertIndex(ref surface);
				int vertIndex = host_state!.WorldBrush!.VertIndices![firstVertIndex + count - 1];
				Vector3 prevPos = host_state.WorldBrush.Vertexes![vertIndex].Position;
				for (int v = 0; v < count; ++v) {
					vertIndex = host_state.WorldBrush.VertIndices[firstVertIndex + v];
					ref Vector3 vec = ref host_state.WorldBrush.Vertexes![vertIndex].Position;

					meshBuilder.Position3fv(prevPos);
					meshBuilder.AdvanceVertex();
					meshBuilder.Position3fv(vec);
					meshBuilder.AdvanceVertex();

					prevPos = vec;
				}
			}
		}

		meshBuilder.End();
		mesh.Draw();
	}
	static void Shader_DrawChainsWireframe(List<SurfaceHandle_t> surfaceList) {
		int wireFrameMode = WireFrameMode();

		switch (wireFrameMode) {
			case 3:
				Shader_DrawWireframePolygons(surfaceList);
				break;

			default: {
					using MatRenderContextPtr renderContext = new(materials);
#if !SWDS
					if (wireFrameMode == 2)
						renderContext.Bind(MatSys.MaterialWorldWireframeZBuffer!, null);
					else
						renderContext.Bind(MatSys.MaterialWorldWireframe!, null);
#endif
					for (int i = 0; i < surfaceList.Count; i++) {
						SurfaceHandle_t surfID = surfaceList[i];
						Assert(!ModelLoader.SurfaceHasDispInfo(ref ModelLoader.SurfaceHandleFromIndex(surfID)));
						Shader_DrawSurfaceDynamic(renderContext, surfID, false);
					}
				}
				break;
		}
	}
	static void Shader_DrawChainNormals(List<SurfaceHandle_t> surfaceList) => throw new NotImplementedException();
	static void Shader_DrawChainBumpBasis(List<SurfaceHandle_t> surfaceList) => throw new NotImplementedException();
	static void Shader_DrawLuxels(List<SurfaceHandle_t> surfaceList) => throw new NotImplementedException();
	static void ComputeDebugSettings() {
		g_ShaderDebug.Wireframe = ShouldDrawInWireFrameMode() || (r_drawworld.GetInt() == 2);
		g_ShaderDebug.Normals = mat_normals.GetBool();
		g_ShaderDebug.Luxels = mat_luxels.GetBool();
		g_ShaderDebug.BumpBasis = mat_bumpbasis.GetBool();
		// g_ShaderDebug.SurfaceID = mat_surfaceid.GetInt();
		// g_ShaderDebug.SurfaceMaterials = mat_surfacemat.GetBool();
		g_ShaderDebug.TestAnyDebug();
	}
	internal static void DrawDebugInformation(List<SurfaceHandle_t> surfaceList) {
		if (g_ShaderDebug.Wireframe)
			Shader_DrawChainsWireframe(surfaceList);

		if (g_ShaderDebug.Normals)
			Shader_DrawChainNormals(surfaceList);

		if (g_ShaderDebug.BumpBasis)
			Shader_DrawChainBumpBasis(surfaceList);

		if (g_ShaderDebug.Luxels)
			Shader_DrawLuxels(surfaceList);

		if (g_ShaderDebug.SurfaceID != 0)
			Shader_DrawSurfaceDebuggingInfo(surfaceList, g_ShaderDebug.SurfaceID != 2 ? DrawSurfaceID : DrawSurfaceIDAsInt);
		else if (g_ShaderDebug.SurfaceMaterials)
			Shader_DrawSurfaceDebuggingInfo(surfaceList, DrawSurfaceMaterial);
	}

	public static void AddProjectedTextureDecalsToList(WorldRenderList renderList, int sortGroup) {
		MSurfaceSortList sortList = renderList.SortList;
		foreach (SurfaceSortGroup group in sortList.GetSortList(sortGroup)) {
			for (short blockIndex = group.ListHead; blockIndex != -1; blockIndex = sortList.GetSurfaceBlock(blockIndex).NextBlock) {
				ref MaterialList matList = ref sortList.GetSurfaceBlock(blockIndex);
				for (int idx = 0; idx < matList.Count; ++idx) {
					SurfaceHandle_t surfID = (SurfaceHandle_t)matList.Surfaces[idx];
					ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
					Assert(!ModelLoader.SurfaceHasDispInfo(ref surface));
					if (SHADOW_DECAL_HANDLE_INVALID != ModelLoader.MSurf_ShadowDecals(ref surface)) {
						if ((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.NoShadows) == 0)
							renderList.ShadowHandles[sortGroup].Add(ModelLoader.MSurf_ShadowDecals(ref surface));
					}
					// OverlayMgr().AddFragmentListToRenderList(sortGroup, MSurf_OverlayFragmentList(surface), false) // todo
				}
			}
		}
	}
	public static void Shader_DrawChains(WorldRenderList renderList, int sortGroup, bool shadowDepth) {
		Assert(!g_EngineRenderer.InLightmapUpdate());

		if (mat_forcedynamic.GetInt() == 0 && !MatSysInterface.MaterialSystemConfig.DrawFlat)
			Shader_DrawChainsStatic(renderList.SortList, sortGroup, shadowDepth);
		else
			Shader_DrawChainsDynamic(renderList.SortList, sortGroup, shadowDepth);

		if (shadowDepth)
			return;

		if (g_ShaderDebug.AnyDebug) {
			MSurfaceSortList sortList = renderList.SortList;
			foreach (SurfaceSortGroup group in sortList.GetSortList(sortGroup)) {
				List<SurfaceHandle_t> surfList = [];
				sortList.GetSurfaceListForGroup(surfList, group);
				DrawDebugInformation(surfList);
			}
		}
	}
	public static void Shader_DrawDispChain(int sortGroup, in MSurfaceSortList list, uint flags, RenderDepthMode depthMode) {
		int count = 0;
		List<SurfaceSortGroup> groupList = list.GetSortList(sortGroup);
		foreach (SurfaceSortGroup group in groupList)
			count += group.SurfaceCount;

		if (count != 0) {
			SurfaceHandle_t[] pList = new SurfaceHandle_t[count];
			int i = 0;
			foreach (SurfaceSortGroup group in groupList) {
				for (short blockIndex = group.ListHead; blockIndex != -1; blockIndex = list.GetSurfaceBlock(blockIndex).NextBlock) {
					ref MaterialList matList = ref list.GetSurfaceBlock(blockIndex);
					for (int index = 0; index < matList.Count; ++index) {
						pList[i] = (SurfaceHandle_t)matList.Surfaces[index];
						++i;
					}
				}
			}
			Assert(i == count);

			DispInfo.DispInfo_RenderList(sortGroup, pList, count, g_EngineRenderer.ViewGetCurrent().Ortho, flags, depthMode);
		}
	}
	static void Shader_BuildDynamicLightmaps(WorldRenderList renderList) {
		Render.DLightStartView();

		for (int sortGroup = 0; sortGroup < (int)MatSortGroup.Max; ++sortGroup) {
			for (int i = renderList.DlightSurfaces[sortGroup].Count - 1; i >= 0; --i) {
				Render.LightmapUpdateInfo tmp = default;
				tmp.SurfaceData = host_state.WorldBrush!.Surfaces2;
				tmp.SurfaceIndex = renderList.DlightSurfaces[sortGroup][i];
				tmp.TransformIndex = 0;
				Render.g_LightmapUpdateList.Add(tmp);
			}
		}

		Render.DLightEndView();
	}
	static void ComputeFogVolumeInfo(ref FogVolumeInfo fogVolume) {
		fogVolume.InFogVolume = false;
		int leafID = CM.PointLeafnum(CurrentViewOrigin());
		if (leafID < 0 || leafID >= host_state.WorldBrush!.NumLeafs)
			return;

		BSPMLeaf leaf = host_state.WorldBrush!.Leafs![leafID];
		fogVolume.FogVolumeID = leaf.LeafWaterDataID;
		if (fogVolume.FogVolumeID == -1)
			return;

		fogVolume.InFogVolume = true;

		ref BSPMLeafWaterData leafWaterData = ref host_state.WorldBrush!.LeafWaterData![leaf.LeafWaterDataID];
		if (leafWaterData.SurfaceTexInfoID == -1) {
			fogVolume.State.FogEnabled = false;
			return;
		}
		ref ModelTexInfo texInfo = ref host_state.WorldBrush!.TexInfo![leafWaterData.SurfaceTexInfoID];

		IMaterial? material = texInfo.Material;
		if (material != null) {
			IMaterialVar fogColorVar = material.FindVar("$fogcolor", out _);
			IMaterialVar fogEnableVar = material.FindVar("$fogenable", out _);
			IMaterialVar fogStartVar = material.FindVar("$fogstart", out _);
			IMaterialVar fogEndVar = material.FindVar("$fogend", out _);

			fogVolume.State.FogEnabled = fogEnableVar.GetIntValue() != 0;
			fogColorVar.GetVecValue(fogVolume.State.FogColor);
			fogVolume.State.FogStart = -fogStartVar.GetFloatValue();
			fogVolume.State.FogEnd = -fogEndVar.GetFloatValue();
			fogVolume.FogSurfaceZ = leafWaterData.SurfaceZ;
			fogVolume.FogMinZ = leafWaterData.MinZ;
			fogVolume.State.FogMode = MaterialFogMode.Linear;
		}
		else {
			Warning("***Water vmt missing . . check console for missing materials!***\n");
			fogVolume.State.FogEnabled = false;
		}
	}
	public static void ResetWorldRenderList(WorldRenderList renderList) => renderList?.Reset();
	public static void Shader_WorldBegin(WorldRenderList renderList) {
		s_ShaderConvars.DrawLeaf = r_drawleaf.GetInt();
		s_ShaderConvars.DrawWorld = r_drawworld.GetBool();
		s_ShaderConvars.DrawFuncDetail = r_drawfuncdetail.GetBool();

		ResetWorldRenderList(renderList);

#if !SWDS
		Render.DecalSurfacesInit(false);
#endif

		// TODO overlaymgr

		g_ShadowMgr.ClearShadowRenderList();
	}
	static void Shader_WorldZFillSurfChain(in MSurfaceSortList sortList, in SurfaceSortGroup group, MeshBuilder meshBuilder, ref nint startVertIn, uint includeFlags) => throw new NotImplementedException();
	static void Shader_WorldShadowDepthFill(WorldRenderList renderList, DrawWorldListFlags flags) => throw new NotImplementedException();
	static void Shader_WorldZFill(WorldRenderList renderList, DrawWorldListFlags flags) => throw new NotImplementedException();
	static readonly int[] s_DrawWorldListsToSortGroup = [
		(int)MatSortGroup.StrictlyAboveWater,
		(int)MatSortGroup.StrictlyUnderwater,
		(int)MatSortGroup.IntersectsWaterSurface,
		(int)MatSortGroup.WaterSurface,
	];

	static void Shader_WorldEnd(WorldRenderList renderList, DrawWorldListFlags flags, float waterZAdjust) {
		if ((flags & (DrawWorldListFlags.ShadowDepth | DrawWorldListFlags.SSAO)) != 0) {
			Shader_WorldShadowDepthFill(renderList, flags);
			return;
		}

		using MatRenderContextPtr renderCtx = new(materials);

		if ((flags & DrawWorldListFlags.Skybox) != 0) {
			if (renderList.SkyVisible || Map_VisForceFullSky()) {
				if ((flags & DrawWorldListFlags.ClipSkybox) != 0)
					g_EngineRenderer.DrawSkybox(g_EngineRenderer.GetZFar());
				else {
					MaterialHeightClipMode clipMode = renderCtx.GetHeightClipMode();
					renderCtx.SetHeightClipMode(MaterialHeightClipMode.Disable);
					g_EngineRenderer.DrawSkybox(g_EngineRenderer.GetZFar());
					renderCtx.SetHeightClipMode(clipMode);
				}
			}
		}

		bool fastZReject = r_fastzreject.GetInt() != 0;
		if (fastZReject)
			Shader_WorldZFill(renderList, flags);

		int i;
		for (i = (int)MatSortGroup.Max; --i >= 0;) {
			if ((flags & (DrawWorldListFlags)(1 << i)) == 0)
				continue;

			int sortGroup = s_DrawWorldListsToSortGroup[i];
			if (sortGroup == (int)MatSortGroup.WaterSurface) {
				if (waterZAdjust != 0.0f) {
					renderCtx.MatrixMode(MaterialMatrixMode.Model);
					renderCtx.PushMatrix();
					renderCtx.LoadIdentity();
					renderCtx.Translate(0.0f, 0.0f, waterZAdjust);
				}
			}

			Shader_DrawDispChain(sortGroup, renderList.DispSortList, (uint)flags, RenderDepthMode.Normal);

			Shader_DrawChains(renderList, sortGroup, false);
			AddProjectedTextureDecalsToList(renderList, sortGroup);

			for (int j = renderList.ShadowHandles[sortGroup].Count - 1; j >= 0; --j)
				g_ShadowMgr.AddShadowsOnSurfaceToRenderList(renderList.ShadowHandles[sortGroup].ElementAt(j));

			renderList.ShadowHandles[sortGroup].Clear();

			bool flashlightMask = !((flags & DrawWorldListFlags.Refraction) != 0 || (flags & DrawWorldListFlags.Reflection) != 0);

			g_ShadowMgr.SetFlashlightStencilMasks(flashlightMask);
			g_ShadowMgr.RenderFlashlights(flashlightMask);

			// overlaymgr todo
			g_ShadowMgr.DrawFlashlightOverlays(sortGroup, flashlightMask);
			// overlaymgr todo

#if !SWDS
			Render.DecalSurfaceDraw(renderCtx, sortGroup);
#endif

			g_ShadowMgr.DrawFlashlightDecals(sortGroup, flashlightMask);

			g_ShadowMgr.RenderShadows();
			g_ShadowMgr.ClearShadowRenderList();

			if (sortGroup == (int)MatSortGroup.WaterSurface && waterZAdjust != 0.0f) {
				renderCtx.MatrixMode(MaterialMatrixMode.Model);
				renderCtx.PopMatrix();
			}
		}
	}

	public static bool Shader_LeafContainsTranslucentSurfaces(IWorldRenderList renderListIn, int sortIndex, uint flags) {
		WorldRenderList renderList = (WorldRenderList)renderListIn;
		for (int i = 0; i < (int)MatSortGroup.Max; ++i) {
			if ((flags & (1u << i)) == 0)
				continue;

			int sortGroup = s_DrawWorldListsToSortGroup[i];

			ref SurfaceSortGroup group = ref renderList.AlphaSortList.GetGroupForSortID(sortGroup, sortIndex);
			if (group.SurfaceCount != 0)
				return true;
			ref SurfaceSortGroup dispGroup = ref renderList.DispAlphaSortList.GetGroupForSortID(sortGroup, sortIndex);
			if (dispGroup.SurfaceCount != 0)
				return true;
		}

		return false;
	}
	public static void Shader_DrawTranslucentSurfaces(IWorldRenderList renderListIn, int sortIndex, uint flags, bool shadowDepth) {
		if (!r_drawtranslucentworld.GetBool())
			return;

		WorldRenderList renderList = (WorldRenderList)renderListIn;

		using MatRenderContextPtr renderContext = new(materials);

		bool skipLight = false;
		if (MatSysInterface.MaterialSystemConfig.Fullbright == 1) {
			renderContext.BindLightmapPage(StandardLightmap.WhiteBump);
			skipLight = true;
		}

		List<SurfaceHandle_t> surfaceList = [];
		for (int i = 0; i < (int)MatSortGroup.Max; ++i) {
			if ((flags & (1u << i)) == 0)
				continue;

			int sortGroup = s_DrawWorldListsToSortGroup[i];

			surfaceList.Clear();
			ref SurfaceSortGroup group = ref renderList.AlphaSortList.GetGroupForSortID(sortGroup, sortIndex);
			ref SurfaceSortGroup dispGroup = ref renderList.DispAlphaSortList.GetGroupForSortID(sortGroup, sortIndex);
			if (group.SurfaceCount == 0 && dispGroup.SurfaceCount == 0)
				continue;

			renderList.AlphaSortList.GetSurfaceListForGroup(surfaceList, group);

			for (int listIndex = surfaceList.Count; --listIndex >= 0;) {
				SurfaceHandle_t surfID = surfaceList[listIndex];
				ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
				renderContext.Bind(ModelLoader.MSurf_TexInfo(ref surface).Material!, null);

				if (!skipLight)
					renderContext.BindLightmapPage(MatSys.MaterialSortInfoArray![ModelLoader.MSurf_MaterialSortID(ref surface)].LightmapPageID);

				Shader_DrawSurfaceDynamic(renderContext, surfID, false);

				// todo
			}

			DrawDebugInformation(surfaceList);

			surfaceList.Clear();
			renderList.DispAlphaSortList.GetSurfaceListForGroup(surfaceList, dispGroup);
			Span<SurfaceHandle_t> dispList = surfaceList.AsSpan();
			DispInfo.DispInfo_RenderList(i, dispList, dispList.Length, g_EngineRenderer.ViewGetCurrent().Ortho, flags, RenderDepthMode.Normal);
		}
	}

	public static void R_DrawSurface(WorldRenderList renderList, SurfaceHandle_t surfID) {
		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
		Assert(!ModelLoader.SurfaceHasDispInfo(ref surface));
		if ((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.Sky) != 0)
			renderList.SkyVisible = true;
		else if ((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.Trans) != 0)
			Shader_TranslucentWorldSurface(renderList, surfID);
		else
			Shader_WorldSurface(renderList, surfID);
	}
	public static void R_DrawSurfaceNoCull(WorldRenderList renderList, SurfaceHandle_t surfID) => throw new NotImplementedException();
	static void DrawDisplacementsInLeaf(WorldRenderList renderList, BSPMLeaf leaf) {
		if (leaf.DispCount == 0)
			return;

		ref VarBitSet visitedSurfs = ref renderList.VisitedSurfs;
		for (int i = 0; i < leaf.DispCount; i++) {
			IDispInfo dispInfo = DispInfo.MLeaf_Disaplcement(leaf, i)!;

			ref BSPMSurface2 parent = ref dispInfo.GetParent();
			SurfaceHandle_t parentSurfID = ModelLoader.MSurf_Index(ref parent);

			if (VisitSurface(ref visitedSurfs, parentSurfID)) {
				if ((ModelLoader.MSurf_Flags(ref parent) & SurfDraw.Trans) != 0)
					Shader_TranslucentDisplacementSurface(renderList, parentSurfID);
				else
					Shader_DisplacementSurface(renderList, parentSurfID);
			}
		}
	}

	static int LeafToIndex(BSPMLeaf leaf) => leaf.Index;

	static void UpdateVisibleLeafLists(WorldRenderList renderList, BSPMLeaf leaf) {
		int nLeafIndex = LeafToIndex(leaf);
		renderList.VisibleLeaves.Add((LeafIndex_t)nLeafIndex);
		int leafCount = renderList.VisibleLeaves.Count;
		renderList.VisibleLeafFogVolumes.Add(leaf.LeafWaterDataID);
		renderList.AlphaSortList.EnsureMaxSortIDs(leafCount);
		renderList.DispAlphaSortList.EnsureMaxSortIDs(leafCount);
	}

	static void R_DrawLeaf(WorldRenderList renderList, BSPMLeaf pleaf) {
		UpdateVisibleLeafLists(renderList, pleaf);

		if ((s_ShaderConvars.DrawLeaf >= 0) && (s_ShaderConvars.DrawLeaf != LeafToIndex(pleaf)))
			return;

		DrawDisplacementsInLeaf(renderList, pleaf);

		if (!s_ShaderConvars.DrawWorld)
			return;

		int i;
		int surfaceCount = pleaf.NumMarkNodeSurfaces;
		Span<SurfaceHandle_t> pSurfID = host_state.WorldBrush!.MarkSurfaces.AsSpan(pleaf.FirstMarkSurface);
		ref VarBitSet visitedSurfs = ref renderList.VisitedSurfs;
		for (i = 0; i < surfaceCount; ++i) {
			SurfaceHandle_t surfID = pSurfID[i];
			ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
			Assert((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.NoDraw) == 0);
			Assert((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.Node) != 0);
			Assert(!ModelLoader.SurfaceHasDispInfo(ref surface));
			MarkSurfaceVisited(ref visitedSurfs, surfID);
		}

		if (!s_ShaderConvars.DrawFuncDetail)
			return;

		for (; i < pleaf.NumMarkSurfaces; i++) {
			SurfaceHandle_t surfID = pSurfID[i];

			if (!VisitSurface(ref visitedSurfs, surfID))
				continue;

			ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
			Assert((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.Node) == 0);

			if ((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.NoCull) == 0) {
				ref CollisionPlane plane = ref ModelLoader.MSurf_Plane(ref surface);
				if ((Vector3.Dot(plane.Normal, ModelOrg) - plane.Dist) < BACKFACE_EPSILON)
					continue;
			}

			R_DrawSurface(renderList, surfID);
		}
	}
	static void R_DrawLeafNoCull(WorldRenderList renderList, BSPMLeaf leaf) => throw new NotImplementedException();
	static void R_RecursiveWorldNodeNoCull(WorldRenderList renderList, BSPMNode node, int cullMask) => throw new NotImplementedException();
	static void R_RecursiveWorldNode(WorldRenderList renderList, BSPMNode node, int cullMask) {
		int side;
		float dot;

		while (true) {
			if (node.Contents == (int)Contents.Solid)
				return;

			if (node.VisFrame != r_visframecount)
				return;

			if (cullMask != FRUSTUM_SUPPRESS_CLIPPING) {
				if (node.Contents >= -1) {
					if ((cullMask != 0) || (node.Area > 0)) {
						if (R_CullNode(g_Frustum, node, ref cullMask))
							return;
					}
				}
				else {
					if (node.Contents == -2)
						cullMask = FRUSTUM_SUPPRESS_CLIPPING;
				}
			}

			if (node.Contents >= 0) {
				R_DrawLeaf(renderList, (BSPMLeaf)node);
				return;
			}

			ref CollisionPlane plane = ref node.Plane;
			if ((byte)plane.Type <= 2)
				dot = ModelOrg[(byte)plane.Type] - plane.Dist;
			else
				dot = Vector3.Dot(ModelOrg, plane.Normal) - plane.Dist;

			side = dot >= 0 ? 0 : 1;

			R_RecursiveWorldNode(renderList, node.Children[side]!, cullMask);

			SurfaceHandle_t surfID = node.FirstSurface;
			int i = surfID;
			int lastSurface = i + node.NumSurfaces;
			ref VarBitSet visitedSurfs = ref renderList.VisitedSurfs;
			for (; i < lastSurface; ++i, ++surfID) {
				if (!VisitedSurface(ref visitedSurfs, i))
					continue;

				ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
				Assert(!ModelLoader.SurfaceHasDispInfo(ref surface));

				SurfDraw flags = ModelLoader.MSurf_Flags(ref surface);

				Assert((flags & SurfDraw.Node) != 0);

				Assert((flags & SurfDraw.NoDraw) == 0);

				if ((flags & SurfDraw.UnderWater) == 0 && (side ^ ((flags & SurfDraw.PlaneBack) != 0 ? 1 : 0)) != 0)
					continue;

				R_DrawSurface(renderList, surfID);
			}

			node = node.Children[side == 0 ? 1 : 0]!;
		}
	}
	public const float INVALID_WATER_HEIGHT = 1000000.0f;

	static float R_GetWaterHeight(int fogVolume) {
		if (fogVolume < 0 || fogVolume >= host_state.WorldBrush!.LeafWaterData!.Length)
			return INVALID_WATER_HEIGHT;

		return host_state.WorldBrush!.LeafWaterData![fogVolume].SurfaceZ;
	}

	public static IMaterial? R_GetFogVolumeMaterial(int fogVolume, bool eyeInFogVolume) {
		if (fogVolume < 0 || fogVolume >= host_state.WorldBrush!.LeafWaterData!.Length)
			return null;

		ref BSPMLeafWaterData leafWaterData = ref host_state.WorldBrush!.LeafWaterData![fogVolume];
		ref ModelTexInfo texInfo = ref host_state.WorldBrush!.TexInfo![leafWaterData.SurfaceTexInfoID];

		IMaterial? material = texInfo.Material;
		if (eyeInFogVolume) {
			IMaterialVar? var = material!.FindVar("$bottommaterial", out _);
			if (var != null) {
				ReadOnlySpan<char> materialName = var.GetStringValue();
				if (!materialName.IsEmpty)
					material = materials.FindMaterial(materialName, MaterialDefines.TEXTURE_GROUP_OTHER);
			}
		}
		return material;
	}
	public static void R_SetFogVolumeState(int fogVolume, bool useHeightFog) {
		IMaterial? material = R_GetFogVolumeMaterial(fogVolume, !useHeightFog);
		ref BSPMLeafWaterData leafWaterData = ref host_state.WorldBrush!.LeafWaterData![fogVolume];
		IMaterialVar fogColorVar = material!.FindVar("$fogcolor", out _);
		IMaterialVar fogEnableVar = material.FindVar("$fogenable", out _);
		IMaterialVar fogStartVar = material.FindVar("$fogstart", out _);
		IMaterialVar fogEndVar = material.FindVar("$fogend", out _);

		using MatRenderContextPtr renderContext = new(materials);

		if (material != null && fogEnableVar.GetIntValue() != 0 && fog_enable_water_fog.GetBool()) {
			renderContext.SetFogZ(leafWaterData.SurfaceZ);
			if (useHeightFog)
				renderContext.FogMode(MaterialFogMode.LinearBelowFogZ);
			else
				renderContext.FogMode(MaterialFogMode.Linear);

			Span<float> fogColor = stackalloc float[3];
			fogColorVar.GetVecValue(fogColor);

			renderContext.FogColor3fv(fogColor);
			renderContext.FogStart(fogStartVar.GetFloatValue());
			renderContext.FogEnd(fogEndVar.GetFloatValue());
			renderContext.FogMaxDensity(1.0f);
		}
		else
			renderContext.FogMode(MaterialFogMode.None);
	}
	static bool R_CullNodeTopView(BSPMNode node) => throw new NotImplementedException();
	static void R_DrawTopViewLeaf(WorldRenderList renderList, BSPMLeaf leaf) => throw new NotImplementedException();
	public static void R_RenderWorldTopView(WorldRenderList renderList, BSPMNode node) => throw new NotImplementedException();
	static void SpewLeaf() => throw new NotImplementedException();

	public static void R_BuildWorldLists(IWorldRenderList renderListIn, ref WorldListInfo info, int forceViewLeaf, ReadOnlySpan<VisOverrideData> visData, bool shadowDepth, Span<float> waterReflectionHeight) {
		WorldRenderList renderList = (WorldRenderList)renderListIn;
		// if (g_LostVideoMemory) {
		// 	info.ViewFogVolume = (int)MatSortGroup.StrictlyAboveWater;
		// 	info.LeafCount = 0;
		// 	info.LeafList = CollectionsMarshal.AsSpan(renderList.VisibleLeaves);
		// 	info.LeafFogVolume = CollectionsMarshal.AsSpan(renderList.VisibleLeafFogVolumes);
		// 	return;
		// }

		ModelOrg = g_EngineRenderer.ViewOrigin();

		if (r_spewleaf.GetInt() != 0)
			SpewLeaf();

		Shader_WorldBegin(renderList);

		if (!r_drawtopview) {
			R_SetupAreaBits(forceViewLeaf, visData, waterReflectionHeight);

			if (shadowDepth)
				R_RecursiveWorldNodeNoCull(renderList, host_state.WorldBrush!.Nodes![0], r_frustumcullworld.GetBool() ? FRUSTUM_CLIP_ALL : FRUSTUM_SUPPRESS_CLIPPING);
			else
				R_RecursiveWorldNode(renderList, host_state.WorldBrush!.Nodes![0], r_frustumcullworld.GetBool() ? FRUSTUM_CLIP_ALL : FRUSTUM_SUPPRESS_CLIPPING);
		}
		else
			R_RenderWorldTopView(renderList, host_state.WorldBrush!.Nodes![0]);

		if (!r_drawtopview && !shadowDepth)
			Shader_BuildDynamicLightmaps(renderList);

		if (!shadowDepth) {
			FogVolumeInfo fogInfo = default;
			ComputeFogVolumeInfo(ref fogInfo);
			if (fogInfo.InFogVolume)
				info.ViewFogVolume = (int)MatSortGroup.StrictlyUnderwater;
			else
				info.ViewFogVolume = (int)MatSortGroup.StrictlyAboveWater;
		}
		else
			info.ViewFogVolume = (int)MatSortGroup.StrictlyAboveWater;
		info.LeafCount = renderList.VisibleLeaves.Count;
		info.LeafList = renderList.VisibleLeaves;
		info.LeafFogVolume = renderList.VisibleLeafFogVolumes;
	}

	static void ClearFogInfo(ref VisibleFogVolumeInfo info) {
		info.EyeInFogVolume = false;
		info.VisibleFogVolume = -1;
		info.VisibleFogVolumeLeaf = -1;
		info.FogVolumeMaterial = null;
		info.WaterHeight = INVALID_WATER_HEIGHT;
	}

	static readonly ConVar fast_fogvolume = new("fast_fogvolume", "0");

	public static void R_GetVisibleFogVolume(in Vector3 eyePoint, ref VisibleFogVolumeInfo info) {
		WorldBrushData brush = host_state.WorldBrush!;
		if (brush.LeafWaterData == null || brush.LeafWaterData.Length == 0) {
			ClearFogInfo(ref info);
			return;
		}

		int leafID = CM.PointLeafnum(eyePoint);
		BSPMLeaf leaf = brush.Leafs![leafID];

		Contents leafContents = (Contents)leaf.Contents;
		if (leaf.LeafWaterDataID != -1) {
			Assert((leafContents & (Contents.Slime | Contents.Water)) != 0);
			info.EyeInFogVolume = true;
			info.VisibleFogVolume = leaf.LeafWaterDataID;
			info.VisibleFogVolumeLeaf = leafID;
			info.FogVolumeMaterial = R_GetFogVolumeMaterial(info.VisibleFogVolume, true);
			info.WaterHeight = R_GetWaterHeight(info.VisibleFogVolume);
		}
		else if ((leafContents & Contents.TestFogVolume) != 0) {
			Assert((leafContents & (Contents.Slime | Contents.Water)) == 0);
			if (fast_fogvolume.GetBool() && brush.LeafWaterData.Length == 1) {
				info.VisibleFogVolume = 0;
				info.VisibleFogVolumeLeaf = brush.LeafWaterData[0].FirstLeafIndex;
			}
			else {
				VisibleFogVolumeQuery query = new();
				query.FindVisibleFogVolume(eyePoint, out info.VisibleFogVolume, out info.VisibleFogVolumeLeaf);
			}

			info.EyeInFogVolume = false;
			info.FogVolumeMaterial = R_GetFogVolumeMaterial(info.VisibleFogVolume, false);
			info.WaterHeight = R_GetWaterHeight(info.VisibleFogVolume);
		}
		else
			ClearFogInfo(ref info);

		if (brush.LeafMinDistToWater != null)
			info.DistanceToWater = brush.LeafMinDistToWater[leafID];
		else
			info.DistanceToWater = 0.0f;
	}

	public static void R_DrawWorldLists(IWorldRenderList renderListIn, DrawWorldListFlags flags, float waterZAdjust) {
		WorldRenderList renderList = (WorldRenderList)renderListIn;
		// if (TextMode || LostVideoMemory)
		// return;

		Shader_WorldEnd(renderList, flags, waterZAdjust);
	}
	public static void R_SceneBegin() => ComputeDebugSettings();
	public static void R_SceneEnd() { }

	public static void Shader_DrawLightmapPageSurface(SurfaceHandle_t surfID, float red, float green, float blue) => throw new NotImplementedException();
	public static void Shader_DrawLightmapPageChains(IWorldRenderList renderListIn, int pageId) => throw new NotImplementedException();

	public static void R_FastZRejectDisplacements(bool enable) => throw new NotImplementedException();
	public static void R_InstallBrushRenderOverride(IBrushRenderer? brushRenderer) => throw new NotImplementedException();
	public static bool Shader_DrawBrushSurfaceOverride(IMatRenderContext renderContext, SurfaceHandle_t surfID, IClientEntity? baseEntity) => throw new NotImplementedException();

	public static void R_BrushBatchInit() => throw new NotImplementedException();
	public static void R_Surface_LevelInit() => throw new NotImplementedException();
	public static void R_Surface_LevelShutdown() => WorldRenderList.PurgeAll();
	static void R_DrawBrushModel_Override(IClientEntity? baseEntity, Model? model, in Vector3 origin) => throw new NotImplementedException();
	public static int R_MarkDlightsOnBrushModel(Model? model, IClientRenderable renderable) => throw new NotImplementedException();

	public static readonly BrushBatchRender g_BrushBatchRenderer = new();

	public static void R_DrawBrushModel(IClientEntity? baseEntity, Model? model, in Vector3 origin, in QAngle angles, RenderDepthMode depthMode, bool drawOpaque, bool drawTranslucent) {
		if (r_drawbrushmodels.GetInt() == 0)
			return;

		bool wireframe = false;
		if (r_drawbrushmodels.GetInt() == 2) {
			wireframe = g_ShaderDebug.Wireframe;
			g_ShaderDebug.Wireframe = true;
			g_ShaderDebug.AnyDebug = true;
		}

		using MatRenderContextPtr renderContext = new(materials);
		using BrushModelTransform brushTransform = new(origin, angles, renderContext);

		Assert(model!.Brush.FirstModelSurface != 0);

		Shader_BrushBegin(model, baseEntity);

		if ((model.Flags & ModelFlag.FramebufferTexture) != 0) {
			// todo
		}

		if ((model.Flags & ModelFlag.Translucent) != 0) {
			if (depthMode == RenderDepthMode.Normal)
				g_BrushBatchRenderer.DrawTranslucentBrushModel(baseEntity, model, origin, false, drawOpaque, drawTranslucent);
		}
		else if (drawOpaque)
			g_BrushBatchRenderer.DrawOpaqueBrushModel(baseEntity, model, origin, depthMode);

		Shader_BrushEnd(renderContext, brushTransform.GetNonIdentityMatrix(), model, depthMode != RenderDepthMode.Normal, baseEntity);

		if (r_drawbrushmodels.GetInt() == 2) {
			g_ShaderDebug.Wireframe = wireframe;
			g_ShaderDebug.TestAnyDebug();
		}
	}
	public static void R_DrawBrushModelShadow(IClientRenderable renderable) {
		if (r_drawbrushmodels.GetInt() == 0)
			return;

		Model? model = renderable.GetModel();
		Vector3 origin = renderable.GetRenderOrigin();
		QAngle angles = renderable.GetRenderAngles();

		using MatRenderContextPtr renderContext = new(materials);
		using BrushModelTransform brushTransform = new(origin, angles, renderContext);
		g_BrushBatchRenderer.DrawBrushModelShadow(model, renderable);
	}
	public static void R_DrawIdentityBrushModel(IWorldRenderList renderListIn, Model? model) => throw new NotImplementedException();
}

public class VisibleFogVolumeQuery
{
	Vector3 SearchPoint;
	int VisibleFogVolume;
	int VisibleFogVolumeLeaf;

	public void FindVisibleFogVolume(in Vector3 viewPoint, out int visibleFogVolume, out int visibleFogVolumeLeaf) {
		R_SetupAreaBits();

		SearchPoint = viewPoint;
		VisibleFogVolume = -1;
		VisibleFogVolumeLeaf = -1;

		RecursiveGetVisibleFogVolume(host_state.WorldBrush!.Nodes![0]);

		visibleFogVolume = VisibleFogVolume;
		visibleFogVolumeLeaf = VisibleFogVolumeLeaf;
	}

	bool RecursiveGetVisibleFogVolume(BSPMNode node) {
		if (node.Contents == (int)Contents.Solid)
			return true;

		if (node.VisFrame != r_visframecount)
			return true;

		int fixmeTempRemove = FRUSTUM_CLIP_ALL;
		if (R_CullNode(g_Frustum, node, ref fixmeTempRemove))
			return true;

		if (node.Contents >= 0) {
			BSPMLeaf leaf = (BSPMLeaf)node;

			if (leaf.LeafWaterDataID == -1)
				return true;

			if ((leaf.Contents & (int)Contents.Slime) != 0)
				return true;

			VisibleFogVolume = leaf.LeafWaterDataID;
			VisibleFogVolumeLeaf = leaf.Index;
			return false;
		}

		ref CollisionPlane plane = ref node.Plane;
		float dot;
		if ((byte)plane.Type <= 2)
			dot = SearchPoint[(byte)plane.Type] - plane.Dist;
		else
			dot = Vector3.Dot(SearchPoint, plane.Normal) - plane.Dist;

		int side = (dot >= 0) ? 0 : 1;

		if (!RecursiveGetVisibleFogVolume(node.Children[side]!))
			return false;

		return RecursiveGetVisibleFogVolume(node.Children[side == 0 ? 1 : 0]!);
	}
}

public class BrushSurface : IBrushSurface
{
	SurfaceHandle_t SurfaceID;
	MatSysInterface.SurfaceCtx Ctx;

	public BrushSurface(SurfaceHandle_t surfID) => throw new NotImplementedException();

	public void ComputeTextureCoordinate(in Vector3 worldPos, out Vector2 texCoord) => throw new NotImplementedException();
	public void ComputeLightmapCoordinate(in Vector3 worldPos, out Vector2 lightmapCoord) => throw new NotImplementedException();
	public int GetVertexCount() => throw new NotImplementedException();
	public void GetVertexData(Span<BrushVertex> verts) => throw new NotImplementedException();
	public IMaterial? GetMaterial() => throw new NotImplementedException();
}

public class BrushBatchRender
{
	public struct BrushRenderSurface
	{
		public short SurfaceIndex;
		public nint PlaneIndex;
	}

	public struct BrushRenderBatch
	{
		public short FirstSurface;
		public short SurfaceCount;
		public IMaterial? Material;
		public int SortID;
		public int IndexCount;
	}

	public struct BrushRenderMesh
	{
		public short FirstBatch;
		public short BatchCount;
	}

	public class BrushRender
	{
		public CollisionPlane[]? Planes;
		public BrushRenderMesh[]? Meshes;
		public BrushRenderBatch[]? Batches;
		public BrushRenderSurface[]? Surfaces;
		public short PlaneCount;
		public short MeshCount;
		public short BatchCount;
		public short SurfaceCount;
		public short TotalIndexCount;
		public short TotalVertexCount;

		public void Free() {
			Planes = null;
			Meshes = null;
			Batches = null;
			Surfaces = null;
		}
	}

	public struct SurfaceList
	{
		public SurfaceHandle_t SurfID;
		public short SurfaceIndex;
		public nint PlaneIndex;
	}

	public struct TransBatch
	{
		public short FirstSurface;
		public short SurfaceCount;
		public IMaterial? Material;
		public int SortID;
		public int IndexCount;
	}

	public struct TransDecal
	{
		public short FirstSurface;
		public short SurfaceCount;
	}

	public struct TransNode
	{
		public short FirstBatch;
		public short BatchCount;
		public short FirstDecalSurface;
		public short DecalSurfaceCount;
	}

	// const int MAX_TRANS_NODES = 256;
	// const int MAX_TRANS_SURFACES = 1024;
	// const int MAX_TRANS_DECALS = 256;
	// const int MAX_TRANS_BATCHES = 1024;
	public struct TransRender
	{
		public InlineArray256<TransNode> Nodes;
		public InlineArray1024<SurfaceHandle_t> Surfaces;
		public InlineArray256<SurfaceHandle_t> DecalSurfaces;
		public InlineArray1024<TransBatch> Batches;
		public short LastBatch;
		public short LastNode;
		public short NodeCount;
		public short BatchCount;
		public short SurfaceCount;
		public short DecalSurfaceCount;
	}

	readonly List<BrushRender> RenderList = [];

	public static int SurfaceCmp(in SurfaceList s0, in SurfaceList s1) {
		int sortID0 = ModelLoader.MSurf_MaterialSortID(ref ModelLoader.SurfaceHandleFromIndex(s0.SurfID));
		int sortID1 = ModelLoader.MSurf_MaterialSortID(ref ModelLoader.SurfaceHandleFromIndex(s1.SurfID));

		return sortID0 - sortID1;
	}

	public void LevelInit() {
		foreach (BrushRender render in RenderList)
			render.Free();

		RenderList.Clear();

		ClearRenderHandles();
	}

	public void ClearRenderHandles() {
		for (int brush = 1; brush < host_state.WorldBrush!.NumSubModels; ++brush) {
			Span<char> brushModel = stackalloc char[5];
			sprintf(brushModel, $"*{brush}");
			Model? model = modelloader.GetModelForName(brushModel.SliceNullTerminatedString(), ModelLoaderFlags.Server);
			model?.Brush.RenderHandle = 0;
		}
	}

	public BrushRender? FindOrCreateRenderBatch(Model model) {
		if (model.Brush.NumModelSurfaces == 0)
			return null;

		int index = model.Brush.RenderHandle - 1;

		if (RenderList.IsValidIndex(index))
			return RenderList[index];

		index = RenderList.Count;
		BrushRender renderT = new();
		RenderList.Add(renderT);
		model.Brush.RenderHandle = (ushort)(index + 1);
		renderT.Planes = null;
		renderT.Meshes = null;
		renderT.PlaneCount = 0;
		renderT.MeshCount = 0;
		renderT.TotalIndexCount = 0;
		renderT.TotalVertexCount = 0;

		List<CollisionPlane> planeList = [];
		List<SurfaceList> surfaceList = [];

		int i;

		for (i = 0; i < model.Brush.NumModelSurfaces; i++) {
			SurfaceHandle_t surfID = model.Brush.FirstModelSurface + i;
			ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID, model.Brush.Shared);
			if ((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.Trans) != 0)
				continue;

			ref CollisionPlane plane = ref ModelLoader.MSurf_Plane(ref surface);
			int planeIndex = -1;
			for (int p = 0; p < planeList.Count; p++) {
				if (planeList[p].Normal == plane.Normal && planeList[p].Dist == plane.Dist) {
					planeIndex = p;
					break;
				}
			}
			if (planeIndex == -1) {
				planeIndex = planeList.Count;
				planeList.Add(plane);
			}
			SurfaceList tmp;
			tmp.SurfID = surfID;
			tmp.SurfaceIndex = (short)i;
			tmp.PlaneIndex = planeIndex;
			surfaceList.Add(tmp);
		}
		surfaceList.Sort((a, b) => SurfaceCmp(a, b));
		renderT.Planes = new CollisionPlane[planeList.Count];
		renderT.PlaneCount = (short)planeList.Count;
		for (i = 0; i < planeList.Count; i++)
			renderT.Planes[i] = planeList[i];
		renderT.Surfaces = new BrushRenderSurface[surfaceList.Count];
		renderT.SurfaceCount = (short)surfaceList.Count;

		int meshCount = 0;
		int batchCount = 0;
		int lastSortID = -1;
		IMesh? lastMesh = null;
		BrushRenderMesh[] tmpMesh = new BrushRenderMesh[MAX_VERTEX_FORMAT_CHANGES];
		BrushRenderBatch[] tmpBatch = new BrushRenderBatch[128];

		for (i = 0; i < surfaceList.Count; i++) {
			renderT.Surfaces[i].SurfaceIndex = surfaceList[i].SurfaceIndex;
			renderT.Surfaces[i].PlaneIndex = surfaceList[i].PlaneIndex;

			SurfaceHandle_t surfID = surfaceList[i].SurfID;
			ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID, model.Brush.Shared);
			int sortID = ModelLoader.MSurf_MaterialSortID(ref surface);
			if (!ReferenceEquals(MatSys.WorldStaticMeshes[sortID], lastMesh)) {
				tmpMesh[meshCount].FirstBatch = (short)batchCount;
				tmpMesh[meshCount].BatchCount = 0;
				lastSortID = -1;
				meshCount++;
			}
			if (sortID != lastSortID) {
				tmpBatch[batchCount].FirstSurface = (short)i;
				tmpBatch[batchCount].SurfaceCount = 0;
				tmpBatch[batchCount].SortID = sortID;
				tmpBatch[batchCount].Material = ModelLoader.MSurf_TexInfo(ref surface, model.Brush.Shared).Material;
				tmpBatch[batchCount].IndexCount = 0;
				tmpMesh[meshCount - 1].BatchCount++;
				batchCount++;
			}
			lastMesh = MatSys.WorldStaticMeshes[sortID];
			lastSortID = sortID;
			tmpBatch[batchCount - 1].SurfaceCount++;
			int vertCount = ModelLoader.MSurf_VertCount(ref surface);
			int indexCount = (vertCount - 2) * 3;
			tmpBatch[batchCount - 1].IndexCount += indexCount;
			renderT.TotalIndexCount += (short)indexCount;
			renderT.TotalVertexCount += (short)vertCount;
		}

		renderT.Meshes = new BrushRenderMesh[meshCount];
		Array.Copy(tmpMesh, renderT.Meshes, meshCount);
		renderT.MeshCount = (short)meshCount;
		renderT.Batches = new BrushRenderBatch[batchCount];
		Array.Copy(tmpBatch, renderT.Batches, batchCount);
		renderT.BatchCount = (short)batchCount;
		return renderT;
	}

	public void DrawOpaqueBrushModel(IClientEntity? baseEntity, Model? model, in Vector3 origin, RenderDepthMode depthMode) {
		SurfaceHandle_t firstSurfID = model!.Brush.FirstModelSurface;

		BrushRender? render = FindOrCreateRenderBatch(model);
		int i;
		if (render == null)
			return;

		bool skipLight = false;
		using MatRenderContextPtr renderContext = new(materials);

		if (MatSysInterface.MaterialSystemConfig.Fullbright == 1 || depthMode == RenderDepthMode.Shadow) {
			renderContext.BindLightmapPage(StandardLightmap.WhiteBump);
			skipLight = true;
		}

		object? proxyData = baseEntity?.GetClientRenderable();
		Span<bool> backface = stackalloc bool[1024];
		Assert(render.PlaneCount < 1024);

		for (i = 0; i < render.PlaneCount; i++) {
			float dot = MathLib.DotProduct(ModelOrg, render.Planes![i].Normal) - render.Planes[i].Dist;
			backface[i] = depthMode == RenderDepthMode.Normal && dot < BACKFACE_EPSILON;
		}

		Span<float> oldColor = stackalloc float[4];

		for (i = 0; i < render.MeshCount; i++) {
			ref BrushRenderMesh mesh = ref render.Meshes![i];
			for (int j = 0; j < mesh.BatchCount; j++) {
				ref BrushRenderBatch batch = ref render.Batches![mesh.FirstBatch + j];

				int k;
				for (k = 0; k < batch.SurfaceCount; k++) {
					ref BrushRenderSurface surface = ref render.Surfaces![batch.FirstSurface + k];
					if (!backface[(int)surface.PlaneIndex])
						break;
				}

				if (k == batch.SurfaceCount)
					continue;

				MeshBuilder meshBuilder = new();
				IMaterial? material;

				if (depthMode != RenderDepthMode.Normal) {
					throw new NotImplementedException();
				}
				else {
					material = batch.Material;

					ModulateMaterial(material!, oldColor);
					if (!skipLight)
						renderContext.BindLightmapPage(MatSys.MaterialSortInfoArray![batch.SortID].LightmapPageID);
				}

				renderContext.Bind(material!, proxyData);
				IMesh pBuildMesh = renderContext.GetDynamicMesh(false, MatSys.WorldStaticMeshes[batch.SortID]);
				meshBuilder.Begin(pBuildMesh, MaterialPrimitiveType.Triangles, 0, batch.IndexCount);

				for (; k < batch.SurfaceCount; k++) {
					ref BrushRenderSurface surface = ref render.Surfaces![batch.FirstSurface + k];
					if (backface[(int)surface.PlaneIndex])
						continue;

					SurfaceHandle_t surfID = firstSurfID + surface.SurfaceIndex;
					ref BSPMSurface2 surface2 = ref ModelLoader.SurfaceHandleFromIndex(surfID);

					BuildIndicesForSurface(ref meshBuilder, surfID);

#if !SWDS
					if (ModelLoader.SurfaceHasDecals(ref surface2) && depthMode == RenderDepthMode.Normal)
						Render.DecalSurfaceAdd(surfID, BRUSHMODEL_DECAL_SORT_GROUP);
#endif

					if (depthMode == RenderDepthMode.Normal) {
						ShadowDecalHandle_t decalHandle = ModelLoader.MSurf_ShadowDecals(ref surface2);
						if (decalHandle != SHADOW_DECAL_HANDLE_INVALID)
							g_ShadowMgr.AddShadowsOnSurfaceToRenderList(decalHandle);
					}
				}

				meshBuilder.End(false, true);

				if (depthMode == RenderDepthMode.Normal)
					UnModulateMaterial(material!, oldColor);
			}
		}

		if (depthMode != RenderDepthMode.Normal)
			return;

		if (g_ShaderDebug.AnyDebug) {
			for (int i2 = 0; i2 < render.MeshCount; i2++) {
				ref BrushRenderMesh mesh = ref render.Meshes![i2];
				List<SurfaceHandle_t> brushList = [];
				for (int j = 0; j < mesh.BatchCount; j++) {
					ref BrushRenderBatch batch = ref render.Batches![mesh.FirstBatch + j];
					for (int k = 0; k < batch.SurfaceCount; k++) {
						ref BrushRenderSurface surface = ref render.Surfaces![batch.FirstSurface + k];
						if (backface[(int)surface.PlaneIndex])
							continue;

						SurfaceHandle_t surfID = firstSurfID + surface.SurfaceIndex;
						brushList.Add(surfID);
					}
				}
				DrawDebugInformation(brushList);
			}
		}
	}

	public void DrawTranslucentBrushModel(IClientEntity? baseEntity, Model? model, in Vector3 origin, bool shadowDepth, bool drawOpaque, bool drawTranslucent) {
		if (drawOpaque)
			DrawOpaqueBrushModel(baseEntity, model, origin, shadowDepth ? RenderDepthMode.Shadow : RenderDepthMode.Normal);

		if (drawTranslucent)
			DrawTranslucentBrushModel(model, baseEntity);
	}

	private void DrawTranslucentBrushModel(Model? model, IClientEntity? baseEntity) {
		TransRender renderT = new() {
			LastBatch = -1,
			LastNode = -1,
			NodeCount = 0,
			SurfaceCount = 0,
			BatchCount = 0,
			DecalSurfaceCount = 0
		};

		BuildTransLists_r(ref renderT, model!, model!.Brush.Shared!.Nodes![model.Brush.FirstNode]!);
		object? proxyData = baseEntity?.GetClientRenderable();
		DrawTransLists(ref renderT, proxyData);
	}

	private void AddSurfaceToBatch(ref TransRender renderT, int nodeIndex, int batchIndex, SurfaceHandle_t surfID) {
		renderT.Batches[batchIndex].SurfaceCount++;
		Assert(renderT.SurfaceCount < 1024);

		renderT.Batches[batchIndex].IndexCount += (ModelLoader.MSurf_VertCount(ref ModelLoader.SurfaceHandleFromIndex(surfID)) - 2) * 3;
		renderT.Surfaces[renderT.SurfaceCount] = surfID;
		renderT.SurfaceCount++;
		if (ModelLoader.SurfaceHasDecals(ref ModelLoader.SurfaceHandleFromIndex(surfID))) {
			Assert(renderT.DecalSurfaceCount < 256);
			renderT.Nodes[nodeIndex].DecalSurfaceCount++;
			renderT.DecalSurfaces[renderT.DecalSurfaceCount] = surfID;
			renderT.DecalSurfaceCount++;
		}
	}

	private void AddTransNode(ref TransRender renderT) {
		renderT.LastNode = renderT.NodeCount;
		renderT.NodeCount++;
		Assert(renderT.NodeCount < 256);
		renderT.LastBatch = -1;
		renderT.Nodes[renderT.LastNode].FirstBatch = renderT.BatchCount;
		renderT.Nodes[renderT.LastNode].FirstDecalSurface = renderT.DecalSurfaceCount;
		renderT.Nodes[renderT.LastNode].BatchCount = 0;
		renderT.Nodes[renderT.LastNode].DecalSurfaceCount = 0;
	}

	private void AddTransBatch(ref TransRender renderT, SurfaceHandle_t surfID) {
		int batchIndex = renderT.Nodes[renderT.LastNode].FirstBatch + renderT.Nodes[renderT.LastNode].BatchCount;
		Assert(renderT.BatchCount < 256);
		renderT.Nodes[renderT.LastNode].BatchCount++;
		renderT.BatchCount++;
		ref TransBatch batch = ref renderT.Batches[batchIndex];
		batch.FirstSurface = renderT.SurfaceCount;
		batch.SurfaceCount = 0;
		batch.Material = ModelLoader.MSurf_TexInfo(ref ModelLoader.SurfaceHandleFromIndex(surfID)).Material;
		batch.SortID = ModelLoader.MSurf_MaterialSortID(ref ModelLoader.SurfaceHandleFromIndex(surfID));
		batch.IndexCount = 0;
		renderT.LastBatch = (short)batchIndex;
		AddSurfaceToBatch(ref renderT, renderT.LastNode, batchIndex, surfID);
	}

	private void BuildTransLists_r(ref TransRender renderT, Model model, BSPMNode node) {
		float dot;

		if (node.Contents >= 0)
			return;

		ref CollisionPlane plane = ref node.Plane;
		if ((byte)plane.Type <= 2)
			dot = ModelOrg[(byte)plane.Type] - plane.Dist;
		else
			dot = Vector3.Dot(ModelOrg, plane.Normal) - plane.Dist;

		int side = dot >= 0 ? 0 : 1;

		BuildTransLists_r(ref renderT, model, node.Children[side == 0 ? 1 : 0]!);

		List<SurfaceList> sortList = [];
		SurfaceHandle_t surfID = node.FirstSurface;
		for (int i = 0; i < node.NumSurfaces; i++, surfID++) {
			ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID, model.Brush.Shared);

			if ((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.Trans) != 0) {
				if ((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.NoCull) == 0) {
					if ((side ^ ((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.PlaneBack) != 0 ? 1 : 0)) != 0)
						continue;
				}

				int sortID = ModelLoader.MSurf_MaterialSortID(ref surface);
				if (renderT.LastBatch != -1 && renderT.Batches[renderT.LastBatch].SortID == sortID) {
					AddSurfaceToBatch(ref renderT, renderT.LastNode, renderT.LastBatch, surfID);
				}
				else {
					SurfaceList tmp = default;
					tmp.SurfID = surfID;
					sortList.Add(tmp);
				}
			}
		}

		if (sortList.Count != 0) {
			sortList.Sort((a, b) => SurfaceCmp(a, b));

			AddTransNode(ref renderT);
			int lastSortID = -1;
			for (int i = 0; i < sortList.Count; i++) {
				surfID = sortList[i].SurfID;
				int sortID = ModelLoader.MSurf_MaterialSortID(ref ModelLoader.SurfaceHandleFromIndex(surfID, model.Brush.Shared));
				if (lastSortID == sortID) {
					AddSurfaceToBatch(ref renderT, renderT.LastNode, renderT.LastBatch, surfID);
				}
				else {
					AddTransBatch(ref renderT, surfID);
					lastSortID = sortID;
				}
			}

			if (renderT.Nodes[renderT.LastNode].DecalSurfaceCount != 0) {
				renderT.LastNode = -1;
				renderT.LastBatch = -1;
			}
		}

		BuildTransLists_r(ref renderT, model, node.Children[side]!);
	}

	private void DrawTransLists(ref TransRender renderT, object? proxyData) {
		using MatRenderContextPtr renderContext = new(materials);

		bool skipLight = false;
		if (MatSysInterface.MaterialSystemConfig.Fullbright == 1) {
			renderContext.BindLightmapPage(StandardLightmap.WhiteBump);
			skipLight = true;
		}

		Span<float> oldColor = stackalloc float[4];

		for (int i = 0; i < renderT.NodeCount; i++) {
			int j;
			ref TransNode node = ref renderT.Nodes[i];
			for (j = 0; j < node.BatchCount; j++) {
				ref TransBatch batch = ref renderT.Batches[node.FirstBatch + j];

				MeshBuilder meshBuilder = new();
				IMaterial material = batch.Material!;

				ModulateMaterial(material, oldColor);

				if (!skipLight)
					renderContext.BindLightmapPage(MatSys.MaterialSortInfoArray![batch.SortID].LightmapPageID);
				renderContext.Bind(material, proxyData);

				IMesh buildMesh = renderContext.GetDynamicMesh(false, MatSys.WorldStaticMeshes[batch.SortID]);
				meshBuilder.Begin(buildMesh, MaterialPrimitiveType.Triangles, 0, batch.IndexCount);

				for (int k = 0; k < batch.SurfaceCount; k++) {
					SurfaceHandle_t surfID = renderT.Surfaces[batch.FirstSurface + k];
					BuildIndicesForSurface(ref meshBuilder, surfID);
				}

				meshBuilder.End(false, true);

				UnModulateMaterial(material, oldColor);
			}

			if (node.DecalSurfaceCount != 0) {
				for (j = 0; j < node.DecalSurfaceCount; j++) {
					SurfaceHandle_t surfID = renderT.DecalSurfaces[node.FirstDecalSurface + j];
					ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);

					Assert((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.NoDraw) == 0);

#if !SWDS
					if (ModelLoader.SurfaceHasDecals(ref surface))
						Render.DecalSurfaceAdd(surfID, BRUSHMODEL_DECAL_SORT_GROUP);
#endif

					ShadowDecalHandle_t decalHandle = ModelLoader.MSurf_ShadowDecals(ref surface);
					if (decalHandle != SHADOW_DECAL_HANDLE_INVALID)
						g_ShadowMgr.AddShadowsOnSurfaceToRenderList(decalHandle);
				}

#if !SWDS
				Render.DecalSurfaceDraw(renderContext, BRUSHMODEL_DECAL_SORT_GROUP);
#endif

#if !SWDS
				Render.DecalSurfacesInit(true);
#endif

				g_ShadowMgr.RenderProjectedTextures();
			}

			if (g_ShaderDebug.AnyDebug) {
				List<SurfaceHandle_t> brushList = [];
				for (j = 0; j < node.BatchCount; j++) {
					ref TransBatch batch = ref renderT.Batches[node.FirstBatch + j];
					for (int k = 0; k < batch.SurfaceCount; k++)
						brushList.Add(renderT.Surfaces[batch.FirstSurface + k]);
				}
				DrawDebugInformation(brushList);
			}
		}
	}

	public void DrawBrushModelShadow(Model? model, IClientRenderable renderable) {
		BrushRender? render = FindOrCreateRenderBatch(model!);
		if (render == null)
			return;

		using MatRenderContextPtr renderContext = new(materials);

#if !SWDS
		renderContext.Bind(MatSys.MaterialShadowBuild!, renderable);
#endif

		SurfaceHandle_t surfID = model!.Brush.FirstModelSurface;
		IMesh mesh = renderContext.GetDynamicMesh();
		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, render.TotalVertexCount, render.TotalIndexCount);

		for (int i = 0; i < model.Brush.NumModelSurfaces; i++, surfID++) {
			ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID, model.Brush.Shared);
			Assert((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.NoDraw) == 0);

			if ((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.Trans) != 0)
				continue;

			int startVert = ModelLoader.MSurf_FirstVertIndex(ref surface);
			int vertCount = ModelLoader.MSurf_VertCount(ref surface);
			int startIndex = meshBuilder.GetCurrentVertex();
			int j;
			for (j = 0; j < vertCount; j++) {
				int vertIndex = model.Brush.Shared!.VertIndices![startVert + j];

				meshBuilder.Position3fv(model.Brush.Shared.Vertexes![vertIndex].Position);
				meshBuilder.TexCoord2f(0, 0.0f, 0.0f);
				meshBuilder.AdvanceVertex();
			}

			for (j = 0; j < vertCount - 2; j++) {
				meshBuilder.FastIndex((ushort)startIndex);
				meshBuilder.FastIndex((ushort)(startIndex + j + 1));
				meshBuilder.FastIndex((ushort)(startIndex + j + 2));
			}
		}
		meshBuilder.End();
		mesh.Draw();
	}
}

public class BrushModelTransform : IDisposable
{
	public Vector3 SavedModelOrg;
	public bool Identity;
	Matrix3x4 BrushToWorldMatrix;

	public BrushModelTransform(in Vector3 origin, in QAngle angles, IMatRenderContext renderContext) {
		bool rotated = angles[0] != 0 || angles[1] != 0 || angles[2] != 0;
		Identity = origin == Vector3.Zero && !rotated;

		if (!Identity) {
			SavedModelOrg = ModelOrg;
			renderContext.MatrixMode(MaterialMatrixMode.Model);
			renderContext.PushMatrix();
			MathLib.AngleMatrix(angles, origin, out BrushToWorldMatrix);
			renderContext.LoadMatrix(BrushToWorldMatrix);

			Vector3 delta = g_EngineRenderer.ViewOrigin() - origin;
			ModelOrg = new Vector3(
				delta.X * BrushToWorldMatrix.M00 + delta.Y * BrushToWorldMatrix.M10 + delta.Z * BrushToWorldMatrix.M20,
				delta.X * BrushToWorldMatrix.M01 + delta.Y * BrushToWorldMatrix.M11 + delta.Z * BrushToWorldMatrix.M21,
				delta.X * BrushToWorldMatrix.M02 + delta.Y * BrushToWorldMatrix.M12 + delta.Z * BrushToWorldMatrix.M22
			);
		}
	}

	public void Dispose() {
		if (!Identity) {
			using MatRenderContextPtr renderContext = new(materials);
			renderContext.MatrixMode(MaterialMatrixMode.Model);
			renderContext.PopMatrix();
			ModelOrg = SavedModelOrg;
		}
	}

	public Matrix4x4? GetNonIdentityMatrix() => Identity ? null : BrushToWorldMatrix;
	public bool IsIdentity() => Identity;
}

public class EngineBSPTree : ISpatialQuery
{
	const int ENUM_SPHERE_TEST_X = 0x1;
	const int ENUM_SPHERE_TEST_Y = 0x2;
	const int ENUM_SPHERE_TEST_Z = 0x4;
	const int ENUM_SPHERE_TEST_ALL = 0x7;

	ref struct EnumLeafSphereInfo<T> where T : ISpatialLeafEnumerator
	{
		public Vector3 Center;
		public float Radius;
		public Vector3 BoxCenter;
		public Vector3 BoxHalfDiagonal;
		public ref T Iterator;
		public nint Context;
	}

	static CommonHostState host_state => field ??= Singleton<CommonHostState>();

	public int LeafCount() => host_state.WorldBrush!.NumLeafs;

	public bool EnumerateLeavesAtPoint<T>(in Vector3 pt, ref T pEnum, nint context) where T : ISpatialLeafEnumerator => pEnum.EnumerateLeaf(CM.PointLeafnum(pt), context);

	public bool EnumerateLeavesInBox<T>(in Vector3 mins, in Vector3 maxs, ref T pEnum, nint context) where T : ISpatialLeafEnumerator {
		if (host_state.WorldModel == null)
			return false;

		unsafe {
			EnumLeafBoxInfo<T> info = default;
			info.BoxCenter = (mins + maxs) * 0.5f;
			info.BoxHalfDiagonal = maxs - info.BoxCenter;
			info.Iterator = ref pEnum;
			info.Context = context;
			info.BoxMax = maxs;
			info.BoxMin = mins;
			return EnumerateLeafInBox_R(host_state.WorldBrush!.Nodes![0], ref info);
		}
	}

	public bool EnumerateLeavesInSphere<T>(in Vector3 center, float radius, ref T pEnum, nint context) where T : ISpatialLeafEnumerator {
		EnumLeafSphereInfo<T> info = default;
		unsafe {
			info.Center = center;
			info.Radius = radius;
			info.Iterator = ref pEnum;
			info.Context = context;
			info.BoxCenter = center;
			info.BoxHalfDiagonal = new(radius, radius, radius);
			return EnumerateLeafInSphere_R(host_state.WorldBrush!.Nodes![0], ref info, ENUM_SPHERE_TEST_ALL);
		}
	}

	public bool EnumerateLeavesAlongRay<T>(in Ray ray, ref T pEnum, nint context) where T : ISpatialLeafEnumerator {
		if (!ray.IsSwept) {
			MathLib.VectorAdd(in ray.Start, in ray.Extents, out Vector3 maxs);
			MathLib.VectorSubtract(in ray.Start, in ray.Extents, out Vector3 mins);

			return EnumerateLeavesInBox(in mins, in maxs, ref pEnum, context);
		}

		if (ray.IsRay)
			return EnumerateLeavesAlongRay_R(host_state.WorldBrush!.Nodes![0], in ray, 0.0f, 1.0f, pEnum, context);
		else
			return EnumerateLeavesAlongExtrudedRay_R(host_state.WorldBrush!.Nodes![0], in ray, 0.0f, 1.0f, pEnum, context);
	}

	static bool EnumerateLeafInBox_R<T>(BSPMNode node, ref EnumLeafBoxInfo<T> info) where T : ISpatialLeafEnumerator {
		if (node.Contents == (int)Contents.Solid)
			return true;

		if (!CollisionUtils.IsBoxIntersectingBoxExtents(node.Center, node.HalfDiagonal, info.BoxCenter, info.BoxHalfDiagonal))
			return true;

		if (node.Contents >= 0)
			return info.Iterator!.EnumerateLeaf(((BSPMLeaf)node).Index, info.Context);

		ref CollisionPlane plane = ref node.Plane;
		if ((byte)plane.Type <= 2) {
			if (info.BoxMax[(byte)plane.Type] <= plane.Dist) {
				return EnumerateLeafInBox_R(node.Children[1]!, ref info);
			}
			else if (info.BoxMin[(byte)plane.Type] >= plane.Dist) {
				return EnumerateLeafInBox_R(node.Children[0]!, ref info);
			}
			else {
				bool ret = EnumerateLeafInBox_R(node.Children[0]!, ref info);
				if (!ret)
					return false;

				return EnumerateLeafInBox_R(node.Children[1]!, ref info);
			}
		}

		Vector3 normal = plane.Normal;
		Vector3 cornermin = new(
			normal.X >= 0 ? info.BoxMin.X : info.BoxMax.X,
			normal.Y >= 0 ? info.BoxMin.Y : info.BoxMax.Y,
			normal.Z >= 0 ? info.BoxMin.Z : info.BoxMax.Z);
		Vector3 cornermax = new(
			normal.X >= 0 ? info.BoxMax.X : info.BoxMin.X,
			normal.Y >= 0 ? info.BoxMax.Y : info.BoxMin.Y,
			normal.Z >= 0 ? info.BoxMax.Z : info.BoxMin.Z);

		if (MathLib.DotProduct(plane.Normal, cornermax) <= plane.Dist) {
			return EnumerateLeafInBox_R(node.Children[1]!, ref info);
		}
		else if (MathLib.DotProduct(plane.Normal, cornermin) >= plane.Dist) {
			return EnumerateLeafInBox_R(node.Children[0]!, ref info);
		}
		else {
			bool ret = EnumerateLeafInBox_R(node.Children[0]!, ref info);
			if (!ret)
				return false;

			return EnumerateLeafInBox_R(node.Children[1]!, ref info);
		}
	}

	static bool EnumerateLeavesAlongRay_R(BSPMNode node, in Ray ray, float start, float end, ISpatialLeafEnumerator pEnum, nint context) {
		if (node.Contents == (int)Contents.Solid)
			return true;

		if (node.Contents >= 0)
			return pEnum.EnumerateLeaf(((BSPMLeaf)node).Index, context);

		ref CollisionPlane plane = ref node.Plane;

		float startDotN, deltaDotN;
		if ((byte)plane.Type <= 2) {
			startDotN = ray.Start[(byte)plane.Type];
			deltaDotN = ray.Delta[(byte)plane.Type];
		}
		else {
			startDotN = MathLib.DotProduct(ray.Start, plane.Normal);
			deltaDotN = MathLib.DotProduct(ray.Delta, plane.Normal);
		}

		float front = startDotN + start * deltaDotN - plane.Dist;
		float back = startDotN + end * deltaDotN - plane.Dist;

		int side = front < 0 ? 1 : 0;

		if ((back < 0 ? 1 : 0) == side)
			return EnumerateLeavesAlongRay_R(node.Children[side]!, in ray, start, end, pEnum, context);

		float frac = front / (front - back);
		float mid = start * (1.0f - frac) + end * frac;

		bool ok = EnumerateLeavesAlongRay_R(node.Children[side]!, in ray, start, mid, pEnum, context);
		if (!ok)
			return ok;

		return EnumerateLeavesAlongRay_R(node.Children[side != 0 ? 0 : 1]!, in ray, mid, end, pEnum, context);
	}

	static bool EnumerateLeavesAlongExtrudedRay_R(BSPMNode node, in Ray ray, float start, float end, ISpatialLeafEnumerator pEnum, nint context) {
		if (node.Contents == (int)Contents.Solid)
			return true;

		if (node.Contents >= 0)
			return pEnum.EnumerateLeaf(((BSPMLeaf)node).Index, context);

		ref CollisionPlane plane = ref node.Plane;

		float t1, t2, offset;
		float startDotN, deltaDotN;
		if ((byte)plane.Type <= 2) {
			startDotN = ray.Start[(byte)plane.Type];
			deltaDotN = ray.Delta[(byte)plane.Type];
			offset = ray.Extents[(byte)plane.Type] + DIST_EPSILON;
		}
		else {
			startDotN = MathLib.DotProduct(ray.Start, plane.Normal);
			deltaDotN = MathLib.DotProduct(ray.Delta, plane.Normal);
			offset = MathF.Abs(ray.Extents[0] * plane.Normal[0]) +
					MathF.Abs(ray.Extents[1] * plane.Normal[1]) +
					MathF.Abs(ray.Extents[2] * plane.Normal[2]) + DIST_EPSILON;
		}
		t1 = startDotN + start * deltaDotN - plane.Dist;
		t2 = startDotN + end * deltaDotN - plane.Dist;

		if (t1 > offset && t2 > offset)
			return EnumerateLeavesAlongExtrudedRay_R(node.Children[0]!, in ray, start, end, pEnum, context);

		if (t1 < -offset && t2 < -offset)
			return EnumerateLeavesAlongExtrudedRay_R(node.Children[1]!, in ray, start, end, pEnum, context);

		if (MathF.Abs(t1 - t2) < DIST_EPSILON) {
			bool parallelRet = EnumerateLeavesAlongExtrudedRay_R(node.Children[0]!, in ray, start, end, pEnum, context);
			if (!parallelRet)
				return false;
			return EnumerateLeavesAlongExtrudedRay_R(node.Children[1]!, in ray, start, end, pEnum, context);
		}

		float idist, frac2, frac;
		int side;
		if (t1 < t2) {
			idist = 1.0f / (t1 - t2);
			side = 1;
			frac2 = (t1 + offset) * idist;
			frac = (t1 - offset) * idist;
		}
		else if (t1 > t2) {
			idist = 1.0f / (t1 - t2);
			side = 0;
			frac2 = (t1 - offset) * idist;
			frac = (t1 + offset) * idist;
		}
		else {
			side = 0;
			frac = 1;
			frac2 = 0;
		}

		frac = Math.Clamp(frac, 0f, 1f);
		float midf = start + (end - start) * frac;
		bool ret = EnumerateLeavesAlongExtrudedRay_R(node.Children[side]!, in ray, start, midf, pEnum, context);
		if (!ret)
			return ret;

		frac2 = Math.Clamp(frac2, 0f, 1f);
		midf = start + (end - start) * frac2;
		return EnumerateLeavesAlongExtrudedRay_R(node.Children[side != 0 ? 0 : 1]!, in ray, midf, end, pEnum, context);
	}

	static bool EnumerateLeafInSphere_R<T>(BSPMNode node, ref EnumLeafSphereInfo<T> info, int testFlags) where T : ISpatialLeafEnumerator {
		while (true) {
			if (node.Contents == (int)Contents.Solid)
				return true;

			if (node.Contents >= 0) {
				if (testFlags != 0) {
					if (!CollisionUtils.IsBoxIntersectingSphereExtents(node.Center, node.HalfDiagonal, info.Center, info.Radius))
						return true;
				}

				return info.Iterator.EnumerateLeaf(((BSPMLeaf)node).Index, info.Context);
			}
			else if (testFlags != 0) {
				if (node.Contents == -1) {
					if ((testFlags & ENUM_SPHERE_TEST_X) != 0) {
						float delta = MathF.Abs(node.Center.X - info.BoxCenter.X);
						float size = node.HalfDiagonal.X + info.BoxHalfDiagonal.X;
						if (delta > size)
							return true;

						if (delta + node.HalfDiagonal.X < info.BoxHalfDiagonal.X)
							testFlags &= ~ENUM_SPHERE_TEST_X;
					}

					if ((testFlags & ENUM_SPHERE_TEST_Y) != 0) {
						float delta = MathF.Abs(node.Center.Y - info.BoxCenter.Y);
						float size = node.HalfDiagonal.Y + info.BoxHalfDiagonal.Y;
						if (delta > size)
							return true;

						if (delta + node.HalfDiagonal.Y < info.BoxHalfDiagonal.Y)
							testFlags &= ~ENUM_SPHERE_TEST_Y;
					}

					if ((testFlags & ENUM_SPHERE_TEST_Z) != 0) {
						float delta = MathF.Abs(node.Center.Z - info.BoxCenter.Z);
						float size = node.HalfDiagonal.Z + info.BoxHalfDiagonal.Z;
						if (delta > size)
							return true;

						if (delta + node.HalfDiagonal.Z < info.BoxHalfDiagonal.Z)
							testFlags &= ~ENUM_SPHERE_TEST_Z;
					}
				}
				else if (node.Contents == -2)
					testFlags = 0;
			}

			float normalDotCenter = Vector3.Dot(node.Plane.Normal, info.Center);

			if (normalDotCenter + info.Radius <= node.Plane.Dist)
				node = node.Children[1]!;
			else if (normalDotCenter - info.Radius >= node.Plane.Dist)
				node = node.Children[0]!;
			else {
				if (!EnumerateLeafInSphere_R(node.Children[0]!, ref info, testFlags))
					return false;

				node = node.Children[1]!;
			}
		}
	}
}

public delegate void SurfaceDebugFunc(SurfaceHandle_t surfID, in Vector3 vecCentroid);
