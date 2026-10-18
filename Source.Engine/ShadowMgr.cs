global using static Source.Engine.ShadowMgrGlobals;

using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;

namespace Source.Engine;

public static class ShadowMgrGlobals
{
	public const int SHADOW_VERTEX_SMALL_CACHE_COUNT = 8;
	public const int SHADOW_VERTEX_LARGE_CACHE_COUNT = 32;
	public const int SHADOW_VERTEX_TEMP_COUNT = 48;
	public const int MAX_CLIP_PLANE_COUNT = 4;
	public const int SURFACE_BOUNDS_CACHE_COUNT = 1024;
	public const int SHADOW_DECAL_CACHE_COUNT = 16 * 1024;
	public const int MAX_SHADOW_DECAL_CACHE_COUNT = 64 * 1024;

	public static readonly ConVar r_shadows = new("r_shadows", "1");
	public static readonly ConVar r_shadows_gamecontrol = new("r_shadows_gamecontrol", "-1", FCvar.Cheat);
	public static readonly ConVar r_shadowwireframe = new("r_shadowwireframe", "0", FCvar.Cheat);
	public static readonly ConVar r_shadowids = new("r_shadowids", "0", FCvar.Cheat);
	public static readonly ConVar r_flashlightdrawsweptbbox = new("r_flashlightdrawsweptbbox", "0");
	public static readonly ConVar r_flashlightdrawfrustumbbox = new("r_flashlightdrawfrustumbbox", "0");
	public static readonly ConVar r_flashlightnodraw = new("r_flashlightnodraw", "0");
	public static readonly ConVar r_flashlightupdatedepth = new("r_flashlightupdatedepth", "1");
	public static readonly ConVar r_flashlightdrawdepth = new("r_flashlightdrawdepth", "0");
	public static readonly ConVar r_flashlightrenderworld = new("r_flashlightrenderworld", "1");
	public static readonly ConVar r_flashlightrendermodels = new("r_flashlightrendermodels", "1");
	public static readonly ConVar r_flashlightrender = new("r_flashlightrender", "1");
	public static readonly ConVar r_flashlightculldepth = new("r_flashlightculldepth", "1");
	public static readonly ConVar r_flashlight_version2 = new("r_flashlight_version2", "0", FCvar.Cheat | FCvar.DevelopmentOnly);

	public static readonly ShadowMgr g_ShadowMgr = new();

	public static bool ScreenSpaceRectFromPoints(MatRenderContextPtr renderContext, Vector3[][] clippedPolygons, Span<int> numPoints, int numPolygons, out int left, out int top, out int right, out int bottom) {
		left = top = right = bottom = 0;

		if (numPolygons == 0)
			return false;

		renderContext.GetMatrix(MaterialMatrixMode.View, out Matrix4x4 matView);
		renderContext.GetMatrix(MaterialMatrixMode.Projection, out Matrix4x4 matProj);
		Matrix4x4 matViewProj = Matrix4x4.Multiply(matProj, matView);

		float minX, maxX, minY, maxY;
		minX = minY = float.MaxValue;
		maxX = maxY = -float.MaxValue;

		for (int i = 0; i < numPolygons; i++) {
			for (int j = 0; j < numPoints[i]; j++) {
				matViewProj.V3Mul(in clippedPolygons[i][j], out Vector3 screenSpacePoint);

				minX = MathF.Min(minX, screenSpacePoint.X);
				maxX = MathF.Max(maxX, screenSpacePoint.X);
				minY = MathF.Min(minY, -screenSpacePoint.Y);
				maxY = MathF.Max(maxY, -screenSpacePoint.Y);
			}
		}

		materials.GetBackBufferDimensions(out int width, out int height);

		left = (int)((minX * 0.5f + 0.5f) * width) - 1;
		top = (int)((minY * 0.5f + 0.5f) * height) - 1;
		right = (int)((maxX * 0.5f + 0.5f) * width) + 1;
		bottom = (int)((maxY * 0.5f + 0.5f) * height) + 1;

		left = Math.Clamp(left, 0, width);
		top = Math.Clamp(top, 0, height);
		right = Math.Clamp(right, 0, width);
		bottom = Math.Clamp(bottom, 0, height);

		Assert((left <= right) && (top <= bottom));

		bool withinBounds = (left > 0) || (top > 0) || (right < width) || (bottom < height);

		width = right - left;
		height = bottom - top;
		int area = (width > 0) && (height > 0) ? width * height : 0;

		return withinBounds && (area > 0);
	}

	public static void DrawDebugPolygon(int numVerts, Span<Vector3> vecPoints, bool frontFacing, bool nearPlane) {
		int r = 0, g = 0, b = 0;
		if (frontFacing)
			b = 255;
		else
			r = 255;

		if (nearPlane) {
			r = b = 0;
			g = 255;
		}

		for (int i = 1; i < (numVerts - 1); i++) {
			Vector3 v0 = vecPoints[0];
			Vector3 v1 = vecPoints[frontFacing ? i : i + 1];
			Vector3 v2 = vecPoints[frontFacing ? i + 1 : i];

			debugoverlay.AddTriangleOverlay(v0, v1, v2, r, g, b, 20, true, 0);
		}

		for (int i = 0; i < numVerts; i++) {
			Vector3 v0 = vecPoints[i];
			Vector3 v1 = vecPoints[(i + 1) % numVerts];

			debugoverlay.AddLineOverlayAlpha(v0, v1, 255, 255, 255, 255, false, 0);
		}
	}

	public static void DrawPolygonToStencil(MatRenderContextPtr renderContext, int numVerts, Span<Vector3> vecPoints, bool frontFacing, bool nearPlane) {
		IMaterial? material = materials.FindMaterial("engine/writestencil", MaterialDefines.TEXTURE_GROUP_OTHER, true);

		renderContext.Bind(material!);
		IMesh mesh = renderContext.GetDynamicMesh(true);

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, numVerts - 2);

		for (int i = 1; i < (numVerts - 1); i++) {
			meshBuilder.Position3f(vecPoints[0].X, vecPoints[0].Y, vecPoints[0].Z);
			meshBuilder.AdvanceVertex();

			int index = frontFacing ? i : i + 1;
			meshBuilder.Position3f(vecPoints[index].X, vecPoints[index].Y, vecPoints[index].Z);
			meshBuilder.AdvanceVertex();

			index = frontFacing ? i + 1 : i;
			meshBuilder.Position3f(vecPoints[index].X, vecPoints[index].Y, vecPoints[index].Z);
			meshBuilder.AdvanceVertex();
		}

		meshBuilder.End(false, true);

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PopMatrix();
	}

	public static readonly ConVar r_flashlightclip = new("r_flashlightclip", "0", FCvar.Cheat);
	public static readonly ConVar r_flashlightdrawclip = new("r_flashlightdrawclip", "0", FCvar.Cheat);
	public static readonly ConVar r_flashlightscissor = new("r_flashlightscissor", "1", 0);

	public static void ExtractFrustumPlanes(out Frustum frustumPlanes, float planeEpsilon) {
		ref readonly ViewSetup view = ref g_EngineRenderer.ViewGetCurrent();

		float fovY = MathLib.CalcFovY(view.FOV, view.AspectRatio);

		Frustum_t frustum = new();
		MathLib.AngleVectors(view.Angles, out Vector3 forward, out Vector3 right, out Vector3 up);
		MathLib.GeneratePerspectiveFrustum(view.Origin, forward, right, up, view.ZNear + planeEpsilon, view.ZFar - planeEpsilon, view.FOV, fovY, frustum);

		frustumPlanes = default;
		for (int i = 0; i < (int)FrustumPlane.NumPlanes; i++)
			frustumPlanes.SetPlane(i, frustum.GetPlane(i).Normal, frustum.GetPlane(i).Dist);
	}

	public static void ConstructNearAndFarPolygons(Span<Vector3> vecNearPlane, Span<Vector3> vecFarPlane, float planeEpsilon) {
		ref readonly ViewSetup view = ref g_EngineRenderer.ViewGetCurrent();

		float fovY = MathLib.CalcFovY(view.FOV, view.AspectRatio);

		float tanHalfAngleRadians = MathF.Tan(MathLib.DEG2RAD(view.FOV * 0.5f));
		float halfNearWidth = tanHalfAngleRadians * (view.ZNear + planeEpsilon);
		float halfFarWidth = tanHalfAngleRadians * (view.ZFar - planeEpsilon);
		tanHalfAngleRadians = MathF.Tan(MathLib.DEG2RAD(fovY * 0.5f));
		float halfNearHeight = tanHalfAngleRadians * (view.ZNear + planeEpsilon);
		float halfFarHeight = tanHalfAngleRadians * (view.ZFar - planeEpsilon);

		MathLib.AngleVectors(view.Angles, out Vector3 forward, out Vector3 right, out Vector3 up);
		forward.NormalizeInPlace();
		right.NormalizeInPlace();
		up.NormalizeInPlace();

		Vector3 centerNear = view.Origin + forward * (view.ZNear + planeEpsilon);

		Vector3 rightHalfNearWidth = right * halfNearWidth;
		Vector3 upHalfNearHeight = up * halfNearHeight;

		vecNearPlane[0] = centerNear - rightHalfNearWidth - upHalfNearHeight;
		vecNearPlane[1] = centerNear - rightHalfNearWidth + upHalfNearHeight;
		vecNearPlane[2] = centerNear + rightHalfNearWidth + upHalfNearHeight;
		vecNearPlane[3] = centerNear + rightHalfNearWidth - upHalfNearHeight;

		Vector3 rightHalfFarWidth = right * halfFarWidth;
		Vector3 upHalfFarHeight = up * halfFarHeight;

		vecFarPlane[0] = centerNear - rightHalfFarWidth - upHalfFarHeight;
		vecFarPlane[1] = centerNear + rightHalfFarWidth - upHalfFarHeight;
		vecFarPlane[2] = centerNear + rightHalfFarWidth + upHalfFarHeight;
		vecFarPlane[3] = centerNear - rightHalfFarWidth + upHalfFarHeight;
	}

	public static bool SufficientlyClose(Vector3 v1, Vector3 v2, float epsilon) {
		if (MathF.Abs(v1.X - v2.X) > epsilon)
			return false;

		if (MathF.Abs(v1.Y - v2.Y) > epsilon)
			return false;

		if (MathF.Abs(v1.Z - v2.Z) > epsilon)
			return false;

		return true;
	}

	public static int ClipPlaneToFrustum(Span<Vector3> inPoints, Span<Vector3> outPoints, Span<Vector3> vecWorldFrustumPoints) {
		Span<Vector3> clipPing = stackalloc Vector3[10];
		Span<Vector3> clipPong = stackalloc Vector3[10];
		bool ping = true;

		clipPing[0] = inPoints[0];
		clipPing[1] = inPoints[1];
		clipPing[2] = inPoints[2];
		clipPing[3] = inPoints[3];

		int numPoints = 4;

		for (int i = 0; i < 6; i++) {
			if (numPoints < 3)
				break;

			Span<Vector3> clipPolygon = vecWorldFrustumPoints[(4 * i)..];
			MathLib.ComputeTrianglePlane(clipPolygon[0], clipPolygon[1], clipPolygon[2], out Vector3 normal, out float dist);

			if (ping)
				numPoints = MathLib.ClipPolyToPlane(clipPing, numPoints, clipPong, normal, dist);
			else
				numPoints = MathLib.ClipPolyToPlane(clipPong, numPoints, clipPing, normal, dist);

			ping = !ping;
		}

		if (numPoints < 3)
			return 0;

		if (ping)
			clipPing[..numPoints].CopyTo(outPoints);
		else
			clipPong[..numPoints].CopyTo(outPoints);

		return numPoints;
	}

#if !SWDS
	public static ref uint FirstShadowOnModel(ModelInstanceHandle_t h) => ref ((ModelRender)modelrender).FirstShadowOnModelInstance(h);
#endif

	public static ref uint FirstModelInShadow(ShadowHandle_t h) => ref g_ShadowMgr.FirstModelInShadow(h);
}

public interface IShadowClipper
{
	static abstract bool Inside(in ShadowVertex vert);
	static abstract float Clip(in Vector3 one, in Vector3 two);
	static abstract bool IsPlane();
	static abstract bool IsAbove();
}

public struct ClipTop : IShadowClipper
{
	public static bool Inside(in ShadowVertex vert) => vert.ShadowSpaceTexCoord.Y < 1;
	public static float Clip(in Vector3 one, in Vector3 two) => (1 - one.Y) / (two.Y - one.Y);
	public static bool IsPlane() => false;
	public static bool IsAbove() => false;
}

public struct ClipLeft : IShadowClipper
{
	public static bool Inside(in ShadowVertex vert) => vert.ShadowSpaceTexCoord.X > 0;
	public static float Clip(in Vector3 one, in Vector3 two) => one.X / (one.X - two.X);
	public static bool IsPlane() => false;
	public static bool IsAbove() => false;
}

public struct ClipRight : IShadowClipper
{
	public static bool Inside(in ShadowVertex vert) => vert.ShadowSpaceTexCoord.X < 1;
	public static float Clip(in Vector3 one, in Vector3 two) => (1 - one.X) / (two.X - one.X);
	public static bool IsPlane() => false;
	public static bool IsAbove() => false;
}

public struct ClipBottom : IShadowClipper
{
	public static bool Inside(in ShadowVertex vert) => vert.ShadowSpaceTexCoord.Y > 0;
	public static float Clip(in Vector3 one, in Vector3 two) => one.Y / (one.Y - two.Y);
	public static bool IsPlane() => false;
	public static bool IsAbove() => false;
}

public struct ClipAbove : IShadowClipper
{
	public static bool Inside(in ShadowVertex vert) => vert.ShadowSpaceTexCoord.Z > 0;
	public static float Clip(in Vector3 one, in Vector3 two) => one.Z / (one.Z - two.Z);
	public static bool IsPlane() => false;
	public static bool IsAbove() => true;
}

public struct ClipPlane : IShadowClipper
{
	static Vector3 Normal;
	static float Dist;

	public static bool Inside(in ShadowVertex vert) => MathLib.DotProduct(vert.Position, Normal) < Dist;

	public static float Clip(in Vector3 one, in Vector3 two) {
		MathLib.VectorSubtract(two, one, out Vector3 dir);
		return CollisionUtils.IntersectRayWithPlane(one, dir, Normal, Dist);
	}

	public static bool IsAbove() => false;
	public static bool IsPlane() => true;

	public static void SetPlane(in Vector3 normal, float dist) {
		Normal = normal;
		Dist = dist;
	}
}

public struct ShadowClipState
{
	public int CurrVert;
	public int TempCount;
	public int ClipCount;
	public ShadowVertex[] TempVertices = new ShadowVertex[SHADOW_VERTEX_TEMP_COUNT];
	public int[,] ClipVertices = new int[2, SHADOW_VERTEX_TEMP_COUNT];

	public ShadowClipState() { }

	static void ClampTexCoord(ref ShadowVertex inVertex, ref ShadowVertex outVertex) {
		if (MathF.Abs(inVertex.ShadowSpaceTexCoord.X) < 1e-3)
			outVertex.ShadowSpaceTexCoord.X = 0.0f;
		else if (MathF.Abs(inVertex.ShadowSpaceTexCoord.X - 1.0f) < 1e-3)
			outVertex.ShadowSpaceTexCoord.X = 1.0f;

		if (MathF.Abs(inVertex.ShadowSpaceTexCoord.Y) < 1e-3)
			outVertex.ShadowSpaceTexCoord.Y = 0.0f;
		else if (MathF.Abs(inVertex.ShadowSpaceTexCoord.Y - 1.0f) < 1e-3)
			outVertex.ShadowSpaceTexCoord.Y = 1.0f;
	}

	static void Intersect<Clipper>(ref ShadowVertex start, ref ShadowVertex end, ref ShadowVertex outVertex, bool startInside) where Clipper : IShadowClipper {
		float t;
		if (!Clipper.IsPlane()) {
			if (!Clipper.IsAbove()) {
				t = Clipper.Clip(start.ShadowSpaceTexCoord, end.ShadowSpaceTexCoord);

				MathLib.VectorLerp(start.ShadowSpaceTexCoord, end.ShadowSpaceTexCoord, t, out outVertex.ShadowSpaceTexCoord);
			}
			else {
				t = Clipper.Clip(start.ShadowSpaceTexCoord, end.ShadowSpaceTexCoord);
				MathLib.VectorLerp(start.ShadowSpaceTexCoord, end.ShadowSpaceTexCoord, t, out outVertex.ShadowSpaceTexCoord);

				if (startInside)
					ClampTexCoord(ref end, ref outVertex);
				else
					ClampTexCoord(ref start, ref outVertex);
			}
		}
		else {
			t = Clipper.Clip(start.Position, end.Position);
			MathLib.VectorLerp(start.ShadowSpaceTexCoord, end.ShadowSpaceTexCoord, t, out outVertex.ShadowSpaceTexCoord);
		}

		MathLib.VectorLerp(start.Position, end.Position, t, out outVertex.Position);
	}

	public static void ShadowClip<Clipper>(ref ShadowClipState clip) where Clipper : IShadowClipper {
		if (clip.ClipCount == 0)
			return;

		int numOutVerts = 0;
		int srcVert = clip.CurrVert;
		int destVert = clip.CurrVert == 0 ? 1 : 0;

		int numVerts = clip.ClipCount;
		int start = clip.ClipVertices[srcVert, numVerts - 1];
		bool startInside = Clipper.Inside(clip.TempVertices[start]);
		for (int i = 0; i < numVerts; ++i) {
			int end = clip.ClipVertices[srcVert, i];
			bool endInside = Clipper.Inside(clip.TempVertices[end]);
			if (endInside) {
				if (!startInside) {
					if (clip.TempCount >= SHADOW_VERTEX_TEMP_COUNT)
						return;

					clip.ClipVertices[destVert, numOutVerts] = clip.TempCount++;

					Intersect<Clipper>(ref clip.TempVertices[start], ref clip.TempVertices[end], ref clip.TempVertices[clip.ClipVertices[destVert, numOutVerts]], startInside);
					++numOutVerts;
				}
				clip.ClipVertices[destVert, numOutVerts++] = end;
			}
			else {
				if (startInside) {
					if (clip.TempCount >= SHADOW_VERTEX_TEMP_COUNT)
						return;

					clip.ClipVertices[destVert, numOutVerts] = clip.TempCount++;

					Intersect<Clipper>(ref clip.TempVertices[start], ref clip.TempVertices[end], ref clip.TempVertices[clip.ClipVertices[destVert, numOutVerts]], startInside);
					++numOutVerts;
				}
			}
			start = end;
			startInside = endInside;
		}

		clip.CurrVert = 1 - clip.CurrVert;
		clip.ClipCount = numOutVerts;
		Assert(clip.ClipCount <= SHADOW_VERTEX_TEMP_COUNT);
	}
}

class FlashlightInfoBox
{
	public ShadowMgr.FlashlightInfo Info = new();
}

public class ShadowMgr : IShadowMgrInternal, ISpatialLeafEnumerator
{
	public const float BACKFACE_EPSILON = 0.01f;

	public const ShadowCreateFlags SHADOW_DISABLED = (ShadowCreateFlags)((int)ShadowCreateFlags.LastFlag << 1);

	struct SurfaceBounds_t
	{
		public fltx4 Mins;
		public fltx4 Maxs;
		public Vector3 Center;
		public float Radius;
		public int SurfaceIndex;
	}

	struct ShadowVertexSmallList
	{
		public InlineArray8<ShadowVertex> Verts;
	}

	struct ShadowVertexLargeList
	{
		public InlineArray32<ShadowVertex> Verts;
	}

	struct ShadowVertexCache
	{
		public ushort Count;
		public ShadowHandle_t Shadow;
		public ushort CachedVerts;
		public ShadowVertex[]? Verts;
	}

	struct Shadow
	{
		public ShadowInfo_t Info;
		public Vector3 ProjectionDir;
		public IMaterial? Material;
		public IMaterial? ModelMaterial;
		public object? BindProxy;
		public ShadowCreateFlags Flags;
		public ushort SortOrder;
		public float SphereRadius;
		public Ray Ray;
		public Vector3 SphereCenter;

		public FlashlightHandle_t FlashlightHandle;
		public ITexture? FlashlightDepthTexture;

		public ushort ClipPlaneCount;
		public InlineArray4<Vector3> ClipPlane;
		public InlineArray4<float> ClipDist;

		public ShadowSurfaceIndex_t FirstDecal;

		public uint FirstModel;

		public byte ShadowStencilBit;
	}

	struct ShadowDecal
	{
		public SurfaceHandle_t SurfID;
		public ShadowSurfaceIndex_t ShadowListIndex;
		public ShadowHandle_t Shadow;
		public DispShadowHandle DispShadow;
		public ushort ShadowVerts;

		public ShadowDecalHandle_t NextRender;
	}

	struct ShadowBuildInfo
	{
		public ShadowHandle_t Shadow;
		public Vector3 RayStart;
		public Vector3 ProjectionDirection;
		public Vector3 SphereCenter;
		public float SphereRadius;
		public byte[]? Vis;
	}

	struct ShadowRenderInfo
	{
		public int VertexCount;
		public int IndexCount;
		public int MaxVertices;
		public int MaxIndices;
		public int Count;
		public nint[]? Cache;
		public int DispCount;
		public Matrix4x4? ModelToWorld;
		public Matrix4x4 WorldToModel;
		public DispShadowHandle[]? DispCache;
	}

	struct SortOrderInfo
	{
		public IMaterial? MaterialEnum;
		public int RefCount;
	}

	delegate void ShadowDebugFunc(ShadowHandle_t shadowHandle, in Vector3 centroid);

	internal struct FlashlightInfo
	{
		public FlashlightInfo() {
			FlashlightState = new();
			Frustum = new();
			MaterialBuckets = new();
			OccluderBuckets = new();
			Renderables = [];
		}

		public FlashlightState FlashlightState;
		public ShadowHandle_t Shadow;
		public Frustum_t Frustum;
		public MaterialsBuckets<SurfaceHandle_t> MaterialBuckets;
		public MaterialsBuckets<SurfaceHandle_t> OccluderBuckets;

		public List<IClientRenderable?> Renderables;
	}

	readonly PooledLinkedList<Shadow> Shadows = new();

	readonly PooledLinkedList<ShadowDecal> ShadowDecals = new();

	readonly PooledLinkedList<ShadowDecalHandle_t> ShadowSurfaces = new();

	readonly List<ShadowDecalHandle_t> RenderQueue = [];

	readonly List<SortOrderInfo> SortOrderIds = [];

	readonly PooledLinkedList<ShadowVertexCache> VertexCache = new();

	readonly List<ShadowVertexCache> TempVertexCache = [];

	readonly PooledLinkedList<ShadowVertexSmallList> SmallVertexList = new();
	readonly PooledLinkedList<ShadowVertexLargeList> LargeVertexList = new();

	readonly BidirectionalSet<ModelInstanceHandle_t, ShadowHandle_t> ShadowsOnModels = new();

	readonly LinkedList<SurfaceBounds_t> SurfaceBoundsCache = [];
	LinkedListNode<SurfaceBounds_t>?[]? SurfaceBounds;

	int DecalsToRender;

	readonly Dictionary<FlashlightHandle_t, FlashlightInfoBox> FlashlightStates = [];
	readonly List<FlashlightHandle_t> ValidFlashlightHandles = [];
	FlashlightHandle_t curFlashlightHandleIdx;
	int NumWorldMaterialBuckets;
	bool Initialized;

	nint[] ShadowDecalCache = new nint[SHADOW_DECAL_CACHE_COUNT];
	DispShadowHandle[] DispShadowDecalCache = new DispShadowHandle[SHADOW_DECAL_CACHE_COUNT];

	public ShadowMgr() {
#if !SWDS
		ShadowsOnModels.Init(FirstShadowOnModel, FirstModelInShadow);
#endif
		NumWorldMaterialBuckets = 0;
		SurfaceBounds = null;
		Initialized = false;
		ClearShadowRenderList();
	}

	public void LevelInit(int surfCount) {
		if (Initialized)
			return;
		Initialized = true;

		SurfaceBounds = new LinkedListNode<SurfaceBounds_t>?[surfCount];
	}

	public void LevelShutdown() {
		if (!Initialized)
			return;

		if (SurfaceBounds != null)
			SurfaceBounds = null;

		SurfaceBoundsCache.Clear();
		Initialized = false;
	}

	void SetMaterial(ref Shadow shadow, IMaterial? material, IMaterial? modelMaterial, object? bindProxy) {
		shadow.Material = material;
		shadow.ModelMaterial = modelMaterial;
		shadow.BindProxy = bindProxy;

		material?.IncrementReferenceCount();
		modelMaterial?.IncrementReferenceCount();

		for (int i = 0; i < SortOrderIds.Count; i++) {
			if (SortOrderIds[i].MaterialEnum == material) {
				SortOrderInfo used = SortOrderIds[i];
				++used.RefCount;
				SortOrderIds[i] = used;
				shadow.SortOrder = (ushort)i;
				return;
			}
		}

		shadow.SortOrder = (ushort)SortOrderIds.Count;
		SortOrderIds.Add(new SortOrderInfo { MaterialEnum = material, RefCount = 1 });

		int count = RenderQueue.Count;
		while (count < SortOrderIds.Count) {
			RenderQueue.Add(SHADOW_DECAL_HANDLE_INVALID);
			++count;
		}
	}

	void CleanupMaterial(ref Shadow shadow) {
		SortOrderInfo sortOrder = SortOrderIds[shadow.SortOrder];
		--sortOrder.RefCount;
		SortOrderIds[shadow.SortOrder] = sortOrder;

		shadow.Material?.DecrementReferenceCount();
		shadow.ModelMaterial?.DecrementReferenceCount();
	}

	public int InvalidShadowIndex() => BidirectionalSet<ModelInstanceHandle_t, ShadowHandle_t>.InvalidIndex;

	public ShadowHandle_t CreateShadow(IMaterial? material, IMaterial? modelMaterial, object? bindProxy, int creationFlags) => CreateShadowEx(material, modelMaterial, bindProxy, creationFlags);

	public ShadowHandle_t CreateShadowEx(IMaterial? material, IMaterial? modelMaterial, object? bindProxy, int creationFlags) {
		ShadowHandle_t h = unchecked((ShadowHandle_t)Shadows.Alloc());

		ref Shadow shadow = ref Shadows[h];
		SetMaterial(ref shadow, material, modelMaterial, bindProxy);
		shadow.Flags = (ShadowCreateFlags)creationFlags;
		shadow.FirstDecal = PooledLinkedList<ShadowDecalHandle_t>.INVALID_INDEX;
		shadow.FirstModel = unchecked((uint)BidirectionalSet<ModelInstanceHandle_t, ShadowHandle_t>.InvalidIndex);
		shadow.ProjectionDir = new(0, 0, 1);
		shadow.Info.TexOrigin = new(0, 0);
		shadow.Info.TexSize = new(1, 1);
		shadow.ClipPlaneCount = 0;
		shadow.Info.FalloffBias = 0;
		shadow.FlashlightDepthTexture = null;
		shadow.FlashlightHandle = SHADOW_HANDLE_INVALID;

		if (((ShadowCreateFlags)creationFlags & ShadowCreateFlags.Flashlight) != 0) {
			shadow.FlashlightHandle = AllocFlashlightHandle();
			FlashlightStates[shadow.FlashlightHandle] = new();
			ValidFlashlightHandles.Add(shadow.FlashlightHandle);
			FlashlightStates[shadow.FlashlightHandle].Info.Shadow = h;
			if (r_flashlight_version2.GetInt() == 0)
				AllocFlashlightMaterialBuckets(shadow.FlashlightHandle);
		}

		shadow.Info.WorldToShadow = Matrix4x4.Identity;
		return h;
	}

	FlashlightHandle_t AllocFlashlightHandle() => ++curFlashlightHandleIdx;

	public void DestroyShadow(ShadowHandle_t handle) {
		CleanupMaterial(ref Shadows[handle]);
		RemoveAllSurfacesFromShadow(handle);
		RemoveAllModelsFromShadow(handle);
		if (Shadows[handle].FlashlightHandle != SHADOW_HANDLE_INVALID) {
			FlashlightStates.Remove(Shadows[handle].FlashlightHandle);
			ValidFlashlightHandles.Remove(Shadows[handle].FlashlightHandle);
		}

		Shadows.Remove(handle);
	}

	public void SetShadowMaterial(ShadowHandle_t handle, IMaterial? material, IMaterial? modelMaterial, object? bindProxy) {
		ref Shadow shadow = ref Shadows[handle];
		if ((shadow.Material != material) || (shadow.ModelMaterial != modelMaterial) || (shadow.BindProxy != bindProxy)) {
			CleanupMaterial(ref shadow);
			SetMaterial(ref shadow, material, modelMaterial, bindProxy);
		}
	}

	public void SetShadowTexCoord(ShadowHandle_t handle, float x, float y, float w, float h) {
		ref Shadow shadow = ref Shadows[handle];
		shadow.Info.TexOrigin = new(x, y);
		shadow.Info.TexSize = new(w, h);
	}

	public void ClearExtraClipPlanes(ShadowHandle_t h) => Shadows[h].ClipPlaneCount = 0;

	public void AddExtraClipPlane(ShadowHandle_t h, in Vector3 normal, float dist) {
		ref Shadow shadow = ref Shadows[h];
		Assert(shadow.ClipPlaneCount < MAX_CLIP_PLANE_COUNT);

		shadow.ClipPlane[shadow.ClipPlaneCount] = normal;
		shadow.ClipDist[shadow.ClipPlaneCount] = dist;
		++shadow.ClipPlaneCount;
	}

	public ref readonly ShadowInfo_t GetInfo(ShadowHandle_t handle) => ref Shadows[handle].Info;

	Span<ShadowVertex> GetCachedVerts(in ShadowVertexCache cache) {
		if (cache.Count == 0)
			return default;

		if (cache.Verts != null)
			return cache.Verts;

		if (cache.Count <= SHADOW_VERTEX_SMALL_CACHE_COUNT)
			return SmallVertexList[cache.CachedVerts].Verts;

		return LargeVertexList[cache.CachedVerts].Verts;
	}

	Span<ShadowVertex> AllocateVertices(ref ShadowVertexCache cache, int count) {
		cache.Verts = null;
		if (count <= SHADOW_VERTEX_SMALL_CACHE_COUNT) {
			cache.Count = (ushort)count;
			cache.CachedVerts = (ushort)SmallVertexList.Alloc();
			return SmallVertexList[cache.CachedVerts].Verts;
		}
		else if (count <= SHADOW_VERTEX_LARGE_CACHE_COUNT) {
			cache.Count = (ushort)count;
			cache.CachedVerts = (ushort)LargeVertexList.Alloc();
			return LargeVertexList[cache.CachedVerts].Verts;
		}

		cache.Count = (ushort)count;
		if (count > 0)
			cache.Verts = new ShadowVertex[count];

		cache.CachedVerts = unchecked((ushort)PooledLinkedList<ShadowVertexLargeList>.INVALID_INDEX);
		return cache.Verts;
	}

	void FreeVertices(ref ShadowVertexCache cache) {
		if (cache.Count == 0)
			return;

		if (cache.Verts != null)
			cache.Verts = null;
		else if (cache.Count <= SHADOW_VERTEX_SMALL_CACHE_COUNT)
			SmallVertexList.Remove(cache.CachedVerts);
		else
			LargeVertexList.Remove(cache.CachedVerts);
	}

	void ClearTempCache() {
		for (int i = TempVertexCache.Count; --i >= 0;)
			FreeVertices(ref TempVertexCache.AsSpan()[i]);

		TempVertexCache.Clear();
	}

	bool AddDecalToShadowList(ShadowHandle_t handle, ShadowDecalHandle_t decalHandle) {
		ShadowSurfaceIndex_t idx = ShadowSurfaces.Alloc();
		if (idx == PooledLinkedList<ShadowDecalHandle_t>.INVALID_INDEX) {
			Warning("CShadowMgr::AddDecalToShadowList - overflowed m_ShadowSurfaces linked list!\n");
			return false;
		}

		ShadowSurfaces[idx] = decalHandle;
		if (Shadows[handle].FirstDecal != PooledLinkedList<ShadowDecalHandle_t>.INVALID_INDEX)
			ShadowSurfaces.LinkBefore(Shadows[handle].FirstDecal, idx);

		Shadows[handle].FirstDecal = idx;
		ShadowDecals[decalHandle].ShadowListIndex = idx;

		return true;
	}

	ShadowDecalHandle_t AddShadowDecalToSurface(SurfaceHandle_t surfID, ShadowHandle_t handle) {
		ShadowDecalHandle_t decalHandle = unchecked((ShadowDecalHandle_t)ShadowDecals.Alloc());
		if (decalHandle == SHADOW_DECAL_HANDLE_INVALID) {
			Warning("CShadowMgr::AddShadowDecalToSurface - overflowed m_ShadowDecals linked list!\n");
			return decalHandle;
		}

		ref ShadowDecal decal = ref ShadowDecals[decalHandle];
		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);

		decal.SurfID = surfID;
		if (ModelLoader.MSurf_ShadowDecals(ref surface) != SHADOW_DECAL_HANDLE_INVALID)
			ShadowDecals.LinkBefore(ModelLoader.MSurf_ShadowDecals(ref surface), decalHandle);
		ModelLoader.MSurf_ShadowDecals(ref surface) = decalHandle;

		if (!ModelLoader.SurfaceHasDispInfo(ref surface))
			decal.DispShadow = DISP_SHADOW_HANDLE_INVALID;
		else
			decal.DispShadow = surface.DispInfo!.AddShadowDecal(handle);

		decal.Shadow = handle;
		decal.ShadowVerts = unchecked((ushort)PooledLinkedList<ShadowVertexCache>.INVALID_INDEX);
		decal.NextRender = SHADOW_DECAL_HANDLE_INVALID;
		decal.ShadowListIndex = PooledLinkedList<ShadowDecalHandle_t>.INVALID_INDEX;

		if (!AddDecalToShadowList(handle, decalHandle)) {
			ShadowDecals.Remove(decalHandle);
			decalHandle = SHADOW_DECAL_HANDLE_INVALID;
		}

		return decalHandle;
	}

	void RemoveDecalFromShadowList(ShadowHandle_t handle, ShadowDecalHandle_t decalHandle) {
		ShadowSurfaceIndex_t idx = ShadowDecals[decalHandle].ShadowListIndex;

		ref ShadowSurfaceIndex_t decal = ref Shadows[handle].FirstDecal;
		if (decal == idx)
			decal = ShadowSurfaces.Next(idx);

		ShadowSurfaces.Remove(idx);

		ShadowDecals[decalHandle].ShadowListIndex = PooledLinkedList<ShadowDecalHandle_t>.INVALID_INDEX;
	}

	void RemoveShadowDecalFromSurface(SurfaceHandle_t surfID, ShadowDecalHandle_t decalHandle) {
		ref ShadowDecal decal = ref ShadowDecals[decalHandle];
		if (decal.ShadowVerts != unchecked((ushort)PooledLinkedList<ShadowVertexCache>.INVALID_INDEX)) {
			FreeVertices(ref VertexCache[decal.ShadowVerts]);
			VertexCache.Remove(decal.ShadowVerts);
			decal.ShadowVerts = unchecked((ushort)PooledLinkedList<ShadowVertexCache>.INVALID_INDEX);
		}

		if (decal.DispShadow != DISP_SHADOW_HANDLE_INVALID)
			ModelLoader.SurfaceHandleFromIndex(decal.SurfID).DispInfo!.RemoveShadowDecal(decal.DispShadow);

		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
		if (ModelLoader.MSurf_ShadowDecals(ref surface) == decalHandle)
			ModelLoader.MSurf_ShadowDecals(ref surface) = unchecked((ShadowDecalHandle_t)ShadowDecals.Next(decalHandle));

		RemoveDecalFromShadowList(decal.Shadow, decalHandle);

		ShadowDecals.Remove(decalHandle);
	}

	void ComputeSurfaceBounds(ref SurfaceBounds_t bounds, SurfaceHandle_t surfID) {
		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);

		bounds.Center = new();
		bounds.Mins = MathLib.ReplicateX4(float.MaxValue);
		bounds.Maxs = MathLib.ReplicateX4(-float.MaxValue);
		int count = ModelLoader.MSurf_VertCount(ref surface);
		for (int i = 0; i < count; ++i) {
			int vertIndex = host_state.WorldBrush!.VertIndices![ModelLoader.MSurf_FirstVertIndex(ref surface) + i];
			ref Vector3 position = ref host_state.WorldBrush.Vertexes![vertIndex].Position;
			bounds.Center += position;

			fltx4 pos4 = MathLib.LoadFloat3(in position);
			bounds.Mins = MathLib.MinSIMD(pos4, bounds.Mins);
			bounds.Maxs = MathLib.MaxSIMD(pos4, bounds.Maxs);
		}

		fltx4 eps = MathLib.ReplicateX4(1e-3f);
		bounds.Mins = MathLib.SetWToZeroSIMD(MathLib.SubSIMD(bounds.Mins, eps));
		bounds.Maxs = MathLib.SetWToZeroSIMD(MathLib.AddSIMD(bounds.Maxs, eps));
		bounds.Center /= count;

		bounds.Radius = 0.0f;
		for (int i = 0; i < count; ++i) {
			int vertIndex = host_state.WorldBrush!.VertIndices![ModelLoader.MSurf_FirstVertIndex(ref surface) + i];
			ref Vector3 position = ref host_state.WorldBrush.Vertexes![vertIndex].Position;
			float distSq = position.DistToSqr(bounds.Center);
			if (distSq > bounds.Radius)
				bounds.Radius = distSq;
		}
		bounds.Radius = MathF.Sqrt(bounds.Radius);
	}

	ref readonly SurfaceBounds_t GetSurfaceBounds(SurfaceHandle_t surfID) {
		int surfaceIndex = ModelLoader.MSurf_Index(ref ModelLoader.SurfaceHandleFromIndex(surfID));

		if (SurfaceBounds![surfaceIndex] != null)
			return ref SurfaceBounds[surfaceIndex]!.ValueRef;

		LinkedListNode<SurfaceBounds_t> node;
		if (SurfaceBoundsCache.Count >= SURFACE_BOUNDS_CACHE_COUNT) {
			node = SurfaceBoundsCache.Last!;
			SurfaceBoundsCache.Remove(node);
			SurfaceBoundsCache.AddFirst(node);
			SurfaceBounds[node.ValueRef.SurfaceIndex] = null;
		}
		else
			node = SurfaceBoundsCache.AddFirst(default(SurfaceBounds_t));
		SurfaceBounds[surfaceIndex] = node;

		ref SurfaceBounds_t bounds = ref node.ValueRef;
		bounds.SurfaceIndex = surfaceIndex;
		ComputeSurfaceBounds(ref bounds, surfID);
		return ref bounds;
	}

	bool IsShadowNearSurface(ShadowHandle_t h, SurfaceHandle_t surfID, Matrix4x4? modelToWorld, Matrix4x4? worldToModel) {
		ref readonly Shadow shadow = ref Shadows[h];
		ref readonly SurfaceBounds_t bounds = ref GetSurfaceBounds(surfID);
		Vector3 surfCenter;
		if (modelToWorld == null)
			surfCenter = bounds.Center;
		else
			MathLib.Vector3DMultiplyPosition(modelToWorld.Value, bounds.Center, out surfCenter);

		MathLib.VectorSubtract(shadow.SphereCenter, surfCenter, out Vector3 delta);
		float distSqr = delta.LengthSquared();
		float minDistSqr = bounds.Radius + shadow.SphereRadius;
		minDistSqr *= minDistSqr;
		if (distSqr >= minDistSqr)
			return false;

		Vector3 boundsMins = new(bounds.Mins[0], bounds.Mins[1], bounds.Mins[2]);
		Vector3 boundsMaxs = new(bounds.Maxs[0], bounds.Maxs[1], bounds.Maxs[2]);

		if (modelToWorld == null)
			return CollisionUtils.IsBoxIntersectingRay(boundsMins, boundsMaxs, shadow.Ray);

		Ray transformedRay = default;
		MathLib.Vector3DMultiplyPosition(worldToModel!.Value, shadow.Ray.Start, out transformedRay.Start);
		MathLib.Vector3DMultiply(worldToModel.Value, shadow.Ray.Delta, out transformedRay.Delta);
		transformedRay.StartOffset = shadow.Ray.StartOffset;
		transformedRay.Extents = shadow.Ray.Extents;
		transformedRay.IsRay = shadow.Ray.IsRay;
		transformedRay.IsSwept = shadow.Ray.IsSwept;
		return CollisionUtils.IsBoxIntersectingRay(boundsMins, boundsMaxs, transformedRay);
	}

	void AddSurfaceToFlashlightMaterialBuckets(ShadowHandle_t handle, SurfaceHandle_t surfID) {
		Assert((Shadows[handle].Flags & ShadowCreateFlags.Flashlight) != 0);

		FlashlightHandle_t flashlightID = Shadows[handle].FlashlightHandle;
		Assert(flashlightID != SHADOW_HANDLE_INVALID);

		FlashlightStates[flashlightID].Info.MaterialBuckets.AddElement(ModelLoader.MSurf_MaterialSortID(ref ModelLoader.SurfaceHandleFromIndex(surfID)), surfID);
	}

	void AddSurfaceToShadow(ShadowHandle_t handle, SurfaceHandle_t surfID) {
		bool isFlashlight = (Shadows[handle].Flags & ShadowCreateFlags.Flashlight) != 0;
		if (!isFlashlight && (ModelLoader.MSurf_Flags(ref ModelLoader.SurfaceHandleFromIndex(surfID)) & (SurfDraw.Trans | SurfDraw.AlphaTest | SurfDraw.NoShadows)) != 0)
			return;

		AddShadowDecalToSurface(surfID, handle);
	}

	void RemoveSurfaceFromShadow(ShadowHandle_t handle, SurfaceHandle_t surfID) => throw new NotImplementedException();

	void RemoveAllSurfacesFromShadow(ShadowHandle_t handle) {
		ShadowSurfaceIndex_t i = Shadows[handle].FirstDecal;
		while (i != PooledLinkedList<ShadowDecalHandle_t>.INVALID_INDEX) {
			ShadowDecalHandle_t decalHandle = ShadowSurfaces[i];
			ShadowSurfaceIndex_t next = ShadowSurfaces.Next(i);

			RemoveShadowDecalFromSurface(ShadowDecals[decalHandle].SurfID, decalHandle);

			i = next;
		}

		Shadows[handle].FirstDecal = PooledLinkedList<ShadowDecalHandle_t>.INVALID_INDEX;
	}

	void RemoveAllShadowsFromSurface(SurfaceHandle_t surfID) {
		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);
		ShadowDecalHandle_t dh = ModelLoader.MSurf_ShadowDecals(ref surface);
		while (dh != SHADOW_DECAL_HANDLE_INVALID) {
			ShadowDecalHandle_t next = unchecked((ShadowDecalHandle_t)ShadowDecals.Next(dh));

			RemoveShadowDecalFromSurface(ShadowDecals[dh].SurfID, dh);

			dh = next;
		}

		ModelLoader.MSurf_ShadowDecals(ref surface) = SHADOW_DECAL_HANDLE_INVALID;
	}

	public void AddShadowToModel(ShadowHandle_t handle, ModelInstanceHandle_t model) {
		if (model == MODEL_INSTANCE_INVALID)
			return;

		if (r_flashlightrender.GetBool() == false)
			return;

		ShadowsOnModels.AddElementToBucket(model, handle);
	}

	public void RemoveAllShadowsFromModel(ModelInstanceHandle_t model) {
		if (model != MODEL_INSTANCE_INVALID) {
			ShadowsOnModels.RemoveBucket(model);

			foreach (FlashlightHandle_t i in ValidFlashlightHandles) {
				ref FlashlightInfo info = ref FlashlightStates[i].Info;

				for (int j = 0; j < info.Renderables.Count; j++) {
					if (info.Renderables[j]!.GetModelInstance() == model) {
						info.Renderables.RemoveAt(j);
						break;
					}
				}
			}
		}
	}

	void RemoveAllModelsFromShadow(ShadowHandle_t handle) {
		ShadowsOnModels.RemoveElement(handle);

		foreach (FlashlightHandle_t i in ValidFlashlightHandles) {
			ref FlashlightInfo info = ref FlashlightStates[i].Info;

			if (info.Shadow == handle)
				info.Renderables.Clear();
		}
	}

	public void SetModelShadowState(ModelInstanceHandle_t instance) => throw new NotImplementedException();

	public bool ModelHasShadows(ModelInstanceHandle_t instance) => throw new NotImplementedException();

	void ApplyShadowToSurface(ref ShadowBuildInfo build, SurfaceHandle_t surfID) {
		AddSurfaceToShadow(build.Shadow, surfID);
	}

	void ApplyShadowToDisplacement(ref ShadowBuildInfo build, IDispInfo? dispInfo, bool isFlashlight) {
		if (!isFlashlight && (ModelLoader.MSurf_Flags(ref dispInfo!.GetParent()) & SurfDraw.NoShadows) != 0)
			return;

		dispInfo!.GetBoundingBox(out Vector3 bbMin, out Vector3 bbMax);
		if (!isFlashlight) {
			if (!CollisionUtils.IsBoxIntersectingSphere(bbMin, bbMax, build.SphereCenter, build.SphereRadius))
				return;
		}
		else {
			if (MathLib.R_CullBox(bbMin, bbMax, GetFlashlightFrustum(build.Shadow)))
				return;
		}

		SurfaceHandle_t surfID = ModelLoader.MSurf_Index(ref dispInfo.GetParent());

		if (dispInfo.GetParent().DynamicShadowsEnabled == false && !isFlashlight)
			return;

		AddSurfaceToShadow(build.Shadow, surfID);
	}

	public void EnableShadow(ShadowHandle_t handle, bool enable) {
		if (!enable) {
			RemoveAllSurfacesFromShadow(handle);
			RemoveAllModelsFromShadow(handle);

			Shadows[handle].Flags |= SHADOW_DISABLED;
		}
		else
			Shadows[handle].Flags &= ~SHADOW_DISABLED;
	}

	public void SetFalloffBias(ShadowHandle_t shadow, byte bias) => Shadows[shadow].Info.FalloffBias = bias;

	public void ProjectShadow(ShadowHandle_t handle, in Vector3 origin, in Vector3 projectionDir, in Matrix4x4 worldToShadow, in Vector2 size, ReadOnlySpan<int> leafList, float maxHeight, float falloffOffset, float falloffAmount, in Vector3 casterOrigin) {
		RemoveAllSurfacesFromShadow(handle);
		RemoveAllModelsFromShadow(handle);

		ref Shadow shadow = ref Shadows[handle];
		if ((shadow.Flags & SHADOW_DISABLED) != 0)
			return;

		if (r_shadows.GetInt() == 0)
			return;

		shadow.Info.FalloffOffset = falloffOffset;
		shadow.ProjectionDir = projectionDir;

		shadow.Info.MaxDist = maxHeight;
		shadow.Info.FalloffAmount = falloffAmount;
		shadow.Info.WorldToShadow = worldToShadow;

		float radius = MathF.Sqrt(size.X * size.X + size.Y * size.Y) * 0.5f;
		MathLib.VectorMA(origin, 0.5f * maxHeight, projectionDir, out shadow.SphereCenter);
		shadow.SphereRadius = 0.5f * maxHeight + radius;

		Vector3 mins = new(-radius, -radius, -radius);
		Vector3 maxs = new(radius, radius, radius);
		MathLib.VectorMA(origin, maxHeight, projectionDir, out Vector3 endPoint);
		shadow.Ray.Init(origin, endPoint, mins, maxs);

		if (leafList.Length == 0)
			return;

		++r_surfacevisframe;

		DispInfo.DispInfo_ClearAllTags(host_state.WorldBrush!.DispInfos);

		EnumerateBuild = default;
		EnumerateBuild.Shadow = handle;
		EnumerateBuild.RayStart = origin;
		EnumerateBuild.Vis = null;
		EnumerateBuild.SphereCenter = shadow.SphereCenter;
		EnumerateBuild.SphereRadius = shadow.SphereRadius;
		EnumerateBuild.ProjectionDirection = projectionDir;

		for (int i = 0; i < leafList.Length; ++i)
			EnumerateLeaf(leafList[i], 0);
	}

	public void ProjectFlashlight(ShadowHandle_t handle, in Matrix4x4 worldToShadow, ReadOnlySpan<int> leafList) {
		ref Shadow shadow = ref Shadows[handle];

		if (r_flashlight_version2.GetInt() == 0) {
			RemoveAllSurfacesFromShadow(handle);
			RemoveAllModelsFromShadow(handle);

			FlashlightStates[shadow.FlashlightHandle].Info.OccluderBuckets.Flush();
		}

		if ((Shadows[handle].Flags & SHADOW_DISABLED) != 0)
			return;

		if (r_shadows.GetInt() == 0)
			return;

		shadow.Info.WorldToShadow = worldToShadow;

		MathLib.MatrixInverseGeneral(in shadow.Info.WorldToShadow, out Matrix4x4 shadowToWorld);

		Assert((shadow.Flags & (ShadowCreateFlags)ShadowFlags.Flashlight) != 0);
		Frustum_t frustum = FlashlightStates[shadow.FlashlightHandle].Info.Frustum;
		MathLib.FrustumPlanesFromMatrix(in shadowToWorld, frustum);
		MathLib.CalculateSphereFromProjectionMatrixInverse(in shadowToWorld, out shadow.SphereCenter, out shadow.SphereRadius);

		if (leafList.Length == 0)
			return;

		++r_surfacevisframe;

		DispInfo.DispInfo_ClearAllTags(host_state.WorldBrush!.DispInfos);

		EnumerateBuild = default;
		EnumerateBuild.Shadow = handle;
		EnumerateBuild.RayStart = FlashlightStates[shadow.FlashlightHandle].Info.FlashlightState.LightOrigin;
		EnumerateBuild.Vis = null;
		EnumerateBuild.SphereCenter = shadow.SphereCenter;
		EnumerateBuild.SphereRadius = shadow.SphereRadius;

		if (r_flashlightdrawfrustumbbox.GetBool()) {
			MathLib.CalculateAABBFromProjectionMatrixInverse(in shadowToWorld, out Vector3 mins, out Vector3 maxs);
			debugoverlay?.AddBoxOverlay(new Vector3(0.0f, 0.0f, 0.0f), in mins, in maxs, new QAngle(0, 0, 0),
				0, 0, 255, 100, 0.0f);
		}

		for (int i = 0; i < leafList.Length; ++i)
			EnumerateLeaf(leafList[i], 0);
	}

	void ApplyFlashlightToLeaf(in Shadow shadow, BSPMLeaf? leaf, ref ShadowBuildInfo build) {
		MathLib.VectorAdd(leaf!.Center, leaf.HalfDiagonal, out Vector3 leafMaxs);
		MathLib.VectorSubtract(leaf.Center, leaf.HalfDiagonal, out Vector3 leafMins);

		if (MathLib.R_CullBox(in leafMins, in leafMaxs, GetFlashlightFrustum(build.Shadow)))
			return;

		bool cullDepth = r_flashlightculldepth.GetBool();

		for (int i = 0; i < leaf.NumMarkSurfaces; i++) {
			SurfaceHandle_t surfID = host_state.WorldBrush!.MarkSurfaces![leaf.FirstMarkSurface + i];

			ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);

			if (ModelLoader.MSurf_VisFrame(ref surface) == r_surfacevisframe)
				continue;

			ModelLoader.MSurf_VisFrame(ref surface) = r_surfacevisframe;
			Assert(surface.DispInfo == null);

			int vertIndex = host_state.WorldBrush.VertIndices![ModelLoader.MSurf_FirstVertIndex(ref surface)];
			ref Vector3 worldPos = ref host_state.WorldBrush.Vertexes![vertIndex].Position;

			MathLib.VectorSubtract(worldPos, build.RayStart, out Vector3 lookdir);
			MathLib.VectorNormalize(ref lookdir);

			ref CollisionPlane surfPlane = ref ModelLoader.MSurf_Plane(ref surface);

			float dist = MathLib.DotProduct(surfPlane.Normal, build.SphereCenter) - surfPlane.Dist;
			if (MathF.Abs(dist) >= build.SphereRadius)
				continue;

			ApplyShadowToSurface(ref build, surfID);

			if (cullDepth) {
				if ((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.NoCull) == 0) {
					if (MathLib.DotProduct(surfPlane.Normal, lookdir) < BACKFACE_EPSILON)
						continue;
				}
				else {
					float dot = MathLib.DotProduct(surfPlane.Normal, lookdir);
					if (MathF.Abs(dot) < BACKFACE_EPSILON)
						continue;
				}
			}

			FlashlightInfoBox flashlightInfo = FlashlightStates[shadow.FlashlightHandle];
			flashlightInfo.Info.OccluderBuckets.AddElement(ModelLoader.MSurf_MaterialSortID(ref surface), surfID);
		}
	}

	void ApplyShadowToLeaf(in Shadow shadow, BSPMLeaf leaf, ref ShadowBuildInfo build) {
		for (int i = 0; i < leaf.NumMarkSurfaces; i++) {
			SurfaceHandle_t surfID = host_state.WorldBrush!.MarkSurfaces![leaf.FirstMarkSurface + i];

			ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(surfID);

			if (ModelLoader.MSurf_VisFrame(ref surface) == r_surfacevisframe)
				continue;

			ModelLoader.MSurf_VisFrame(ref surface) = r_surfacevisframe;
			Assert(surface.DispInfo == null);

			if (!surface.DynamicShadowsEnabled)
				continue;

			ref CollisionPlane surfPlane = ref ModelLoader.MSurf_Plane(ref surface);
			bool inFront;
			if ((ModelLoader.MSurf_Flags(ref surface) & SurfDraw.NoCull) == 0) {
				if (MathLib.DotProduct(surfPlane.Normal, build.ProjectionDirection) > -BACKFACE_EPSILON)
					continue;

				inFront = true;
			}
			else {
				float dot = MathLib.DotProduct(surfPlane.Normal, build.ProjectionDirection);
				if (MathF.Abs(dot) < BACKFACE_EPSILON)
					continue;

				inFront = dot < 0;
			}

			if (inFront) {
				if (MathLib.DotProduct(surfPlane.Normal, build.RayStart) < surfPlane.Dist)
					continue;
			}
			else {
				if (MathLib.DotProduct(surfPlane.Normal, build.RayStart) > surfPlane.Dist)
					continue;
			}

			float dist = MathLib.DotProduct(surfPlane.Normal, build.SphereCenter) - surfPlane.Dist;
			if (MathF.Abs(dist) >= build.SphereRadius)
				continue;

			ApplyShadowToSurface(ref build, surfID);
		}
	}

	ShadowBuildInfo EnumerateBuild;

	public bool EnumerateLeaf(int leaf, nint context) {
		ref ShadowBuildInfo build = ref EnumerateBuild;

		if (build.Vis != null) {
			int cluster = CM.LeafCluster(leaf);
			if ((build.Vis[cluster >> 3] & (1 << (cluster & 7))) == 0)
				return true;
		}

		ref readonly Shadow shadow = ref Shadows[build.Shadow];

		BSPMLeaf leafData = host_state.WorldBrush!.Leafs![leaf];

		bool isFlashlight;
		if ((shadow.Flags & ShadowCreateFlags.Flashlight) != 0) {
			isFlashlight = true;
			ApplyFlashlightToLeaf(in shadow, leafData, ref build);
		}
		else {
			isFlashlight = false;
			ApplyShadowToLeaf(in shadow, leafData, ref build);
		}

		for (int i = 0; i < leafData.DispCount; i++) {
			IDispInfo? dispInfo = DispInfo.MLeaf_Disaplcement(leafData, i);

			if (dispInfo!.GetTag())
				continue;

			dispInfo.SetTag();

			ApplyShadowToDisplacement(ref build, dispInfo, isFlashlight);
		}

		return true;
	}

	public void AddShadowToBrushModel(ShadowHandle_t handle, Model? model, in Vector3 origin, in QAngle angles) {
		if (r_shadows.GetInt() == 0)
			return;

		ref Shadow shadow = ref Shadows[handle];

		Vector3 shadowDirInModelSpace = default;
		bool isFlashlight = (shadow.Flags & ShadowCreateFlags.Flashlight) != 0;
		if (!isFlashlight) {
			MathLib.AngleIMatrix(angles, out Matrix3x4 worldToModel);
			MathLib.VectorRotate(shadow.ProjectionDir, worldToModel, out shadowDirInModelSpace);
		}

		for (int i = 0; i < model!.Brush.NumModelSurfaces; ++i) {
			SurfaceHandle_t surfID = model.Brush.FirstModelSurface + i;
			ref BSPMSurface2 surf = ref ModelLoader.SurfaceHandleFromIndex(surfID, model.Brush.Shared);

			SurfDraw flags = ModelLoader.MSurf_Flags(ref surf);
			if ((flags & SurfDraw.NoDraw) != 0)
				continue;

			if (!isFlashlight) {
				if ((flags & SurfDraw.NoCull) == 0) {
					ref CollisionPlane surfPlane = ref ModelLoader.MSurf_Plane(ref surf);
					float dot = MathLib.DotProduct(shadowDirInModelSpace, surfPlane.Normal);
					if (dot > 0)
						continue;
				}
			}

			AddSurfaceToShadow(handle, surfID);
		}
	}

	public void RemoveAllShadowsFromBrushModel(Model? model) {
		for (int i = 0; i < model!.Brush.NumModelSurfaces; ++i)
			RemoveAllShadowsFromSurface(model.Brush.FirstModelSurface + i);
	}

	public void AddShadowsOnSurfaceToRenderList(ShadowDecalHandle_t decalHandle) {
		if (r_shadows.GetInt() == 0)
			return;

		while (decalHandle != SHADOW_DECAL_HANDLE_INVALID) {
			ref ShadowDecal shadowDecal = ref ShadowDecals[decalHandle];
			if ((Shadows[shadowDecal.Shadow].Flags & ShadowCreateFlags.Flashlight) != 0) {
				AddSurfaceToFlashlightMaterialBuckets(shadowDecal.Shadow, shadowDecal.SurfID);

				++DecalsToRender;
			}
			else if (r_shadows_gamecontrol.GetInt() != 0) {
				int sortOrder = Shadows[shadowDecal.Shadow].SortOrder;
				ShadowDecals[decalHandle].NextRender = RenderQueue[sortOrder];
				RenderQueue[sortOrder] = decalHandle;

				++DecalsToRender;
			}
			decalHandle = unchecked((ShadowDecalHandle_t)ShadowDecals.Next(decalHandle));
		}
	}

	public void ClearShadowRenderList() {
		if (RenderQueue.Count > 0)
			memset(RenderQueue.AsSpan(), SHADOW_DECAL_HANDLE_INVALID);

		DecalsToRender = 0;
		ClearAllFlashlightMaterialBuckets();
	}

	public void RenderShadows(Matrix4x4? modelToWorld = null) {
		using MatRenderContextPtr renderContext = new(materials);
		int i;
		for (i = 0; i < RenderQueue.Count; ++i) {
			if (RenderQueue[i] != SHADOW_DECAL_HANDLE_INVALID)
				RenderShadowList(renderContext, RenderQueue[i], modelToWorld);
		}
	}

	public void RenderProjectedTextures(Matrix4x4? modelToWorld = null) {
		RenderFlashlights(true, modelToWorld);
		RenderShadows(modelToWorld);
		ClearShadowRenderList();
	}

	bool ProjectVerticesIntoShadowSpace(in Matrix4x4 modelToShadow, float maxDist, ReadOnlySpan<Vector3> position, ref ShadowClipState clip) {
		bool insideVolume = false;

		for (int i = 0; i < position.Length; ++i) {
			clip.TempVertices[i].Position = position[i];

			MathLib.Vector3DMultiplyPosition(in modelToShadow, in position[i], out clip.TempVertices[i].ShadowSpaceTexCoord);

			clip.ClipVertices[0, i] = i;

			if (clip.TempVertices[i].ShadowSpaceTexCoord.Z < maxDist)
				insideVolume = true;
		}

		clip.TempCount = clip.ClipCount = position.Length;
		clip.CurrVert = 0;

		return insideVolume;
	}

	static ShadowClipState clip = new();

	int ProjectAndClipVertices(in Shadow shadow, in Matrix4x4 worldToShadow, Matrix4x4? worldToModel, ReadOnlySpan<Vector3> position, out ShadowVertex[]? outVertex) {
		outVertex = null;
		if (!ProjectVerticesIntoShadowSpace(in worldToShadow, shadow.Info.MaxDist, position, ref clip))
			return 0;

		ShadowClipState.ShadowClip<ClipTop>(ref clip);
		ShadowClipState.ShadowClip<ClipBottom>(ref clip);
		ShadowClipState.ShadowClip<ClipLeft>(ref clip);
		ShadowClipState.ShadowClip<ClipRight>(ref clip);
		ShadowClipState.ShadowClip<ClipAbove>(ref clip);

		for (int i = 0; i < shadow.ClipPlaneCount; ++i) {
			if (worldToModel != null) {
				CollisionPlane worldPlane = default;
				worldPlane.Normal = shadow.ClipPlane[i];
				worldPlane.Dist = shadow.ClipDist[i];
				MathLib.MatrixTransformPlane(worldToModel.Value, in worldPlane, out CollisionPlane modelPlane);
				ClipPlane.SetPlane(modelPlane.Normal, modelPlane.Dist);
			}
			else
				ClipPlane.SetPlane(shadow.ClipPlane[i], shadow.ClipDist[i]);

			ShadowClipState.ShadowClip<ClipPlane>(ref clip);
		}

		if (clip.ClipCount < 3)
			return 0;

		outVertex = new ShadowVertex[clip.ClipCount];
		for (int i = 0; i < clip.ClipCount; ++i)
			outVertex[i] = clip.TempVertices[clip.ClipVertices[clip.CurrVert, i]];

		return clip.ClipCount;
	}

	public int ProjectAndClipVertices(ShadowHandle_t handle, ReadOnlySpan<Vector3> position, out ShadowVertex[]? outVertex) =>
		ProjectAndClipVertices(in Shadows[handle], in Shadows[handle].Info.WorldToShadow, null, position, out outVertex);

	void CopyClippedVertices(int count, ReadOnlySpan<ShadowVertex> srcVert, Span<ShadowVertex> dstVert, in Vector3 toAdd) {
		for (int i = 0; i < count; ++i) {
			dstVert[i].Position = srcVert[i].Position + toAdd;
			dstVert[i].ShadowSpaceTexCoord = srcVert[i].ShadowSpaceTexCoord;

			Assert(srcVert[i].ShadowSpaceTexCoord.X >= -1e-3f);
			Assert(srcVert[i].ShadowSpaceTexCoord.X - 1.0f <= 1e-3f);
			Assert(srcVert[i].ShadowSpaceTexCoord.Y >= -1e-3f);
			Assert(srcVert[i].ShadowSpaceTexCoord.Y - 1.0f <= 1e-3f);
		}
	}

	bool ShouldCacheVertices(in ShadowDecal decal) => (Shadows[decal.Shadow].Flags & ShadowCreateFlags.CacheVerts) != 0;

	bool GenerateDispShadowRenderInfo(MatRenderContextPtr renderContext, ref ShadowDecal decal, ref ShadowRenderInfo info) {
		if (info.DispCount >= MAX_SHADOW_DECAL_CACHE_COUNT) {
			info.DispCount = MAX_SHADOW_DECAL_CACHE_COUNT;
			return true;
		}

		if (!ModelLoader.SurfaceHandleFromIndex(decal.SurfID).DispInfo!.ComputeShadowFragments(decal.DispShadow, out int v, out int i))
			return false;

		if ((info.VertexCount + v >= info.MaxVertices) || (info.IndexCount + i >= info.MaxIndices))
			return true;

		info.VertexCount += v;
		info.IndexCount += i;
		info.DispCache![info.DispCount++] = decal.DispShadow;
		return true;
	}

	bool GenerateNormalShadowRenderInfo(MatRenderContextPtr renderContext, ref ShadowDecal decal, ref ShadowRenderInfo info) {
		if (info.Count >= MAX_SHADOW_DECAL_CACHE_COUNT) {
			info.Count = MAX_SHADOW_DECAL_CACHE_COUNT;
			return true;
		}

		int vertexCacheIndex;
		bool temp = false;
		if (decal.ShadowVerts != unchecked((ushort)PooledLinkedList<ShadowVertexCache>.INVALID_INDEX)) {
			info.Cache![info.Count] = decal.ShadowVerts;
			vertexCacheIndex = decal.ShadowVerts;
		}
		else {
			bool isNear = IsShadowNearSurface(decal.Shadow, decal.SurfID, info.ModelToWorld, info.WorldToModel);
			if (!isNear)
				return false;

			bool shouldCacheVerts = ShouldCacheVertices(in decal);
			if (shouldCacheVerts) {
				decal.ShadowVerts = (ushort)VertexCache.Alloc();
				info.Cache![info.Count] = decal.ShadowVerts;
				vertexCacheIndex = decal.ShadowVerts;
			}
			else {
				TempVertexCache.Add(default);
				vertexCacheIndex = TempVertexCache.Count - 1;
				info.Cache![info.Count] = -vertexCacheIndex - 1;
				temp = true;
				Assert(info.Cache[info.Count] < 0);
			}

			if (!ComputeShadowVertices(ref decal, info.ModelToWorld, info.WorldToModel, ref temp ? ref TempVertexCache.AsSpan()[vertexCacheIndex] : ref VertexCache[vertexCacheIndex]))
				return false;
		}

		ref ShadowVertexCache vertexCache = ref temp ? ref TempVertexCache.AsSpan()[vertexCacheIndex] : ref VertexCache[vertexCacheIndex];

		int additionalIndices = 3 * (vertexCache.Count - 2);
		if ((info.VertexCount + vertexCache.Count >= info.MaxVertices) ||
			(info.IndexCount + additionalIndices >= info.MaxIndices))
			return true;

		info.VertexCount += vertexCache.Count;
		info.IndexCount += additionalIndices;
		++info.Count;

		return true;
	}

	bool ComputeShadowVertices(ref ShadowDecal decal, Matrix4x4? modelToWorld, Matrix4x4? worldToModel, ref ShadowVertexCache vertexCache) {
		ref BSPMSurface2 surface = ref ModelLoader.SurfaceHandleFromIndex(decal.SurfID);
		int vertCount = ModelLoader.MSurf_VertCount(ref surface);
		Span<Vector3> vecs = stackalloc Vector3[vertCount];
		for (int i = 0; i < vertCount; ++i) {
			int vertIndex = host_state.WorldBrush!.VertIndices![ModelLoader.MSurf_FirstVertIndex(ref surface) + i];
			vecs[i] = host_state.WorldBrush.Vertexes![vertIndex].Position;
		}

		Matrix4x4 modelToShadow = Shadows[decal.Shadow].Info.WorldToShadow;

		if (modelToWorld != null) {
			Matrix4x4 modelToWorldValue = modelToWorld.Value;
			MathLib.MatrixMultiply(in modelToShadow, in modelToWorldValue, out modelToShadow);
		}
		else
			worldToModel = null;

		int clipCount = ProjectAndClipVertices(in Shadows[decal.Shadow], in modelToShadow, worldToModel, vecs, out ShadowVertex[]? srcVert);
		if (clipCount == 0) {
			vertexCache.Count = 0;
			return false;
		}

		Span<ShadowVertex> dstVert = AllocateVertices(ref vertexCache, clipCount);
		Assert(!dstVert.IsEmpty);

		ref Vector3 normal = ref ModelLoader.MSurf_Plane(ref surface).Normal;
		CopyClippedVertices(clipCount, srcVert!, dstVert, normal * OVERLAY_AVOID_FLICKER_NORMAL_OFFSET);

		vertexCache.Shadow = decal.Shadow;

		return true;
	}

	void GenerateShadowRenderInfo(MatRenderContextPtr renderContext, ShadowDecalHandle_t decalHandle, ref ShadowRenderInfo info) {
		info.VertexCount = 0;
		info.IndexCount = 0;
		info.Count = 0;
		info.DispCount = 0;

		ShadowDecalHandle_t next;
		for (; decalHandle != SHADOW_DECAL_HANDLE_INVALID; decalHandle = next) {
			ref ShadowDecal decal = ref ShadowDecals[decalHandle];
			next = ShadowDecals[decalHandle].NextRender;

			ref Shadow shadow = ref Shadows[decal.Shadow];
			if (shadow.Info.FalloffBias == 255)
				continue;

			bool keepShadow;
			if (decal.DispShadow != DISP_SHADOW_HANDLE_INVALID)
				keepShadow = GenerateDispShadowRenderInfo(renderContext, ref decal, ref info);
			else
				keepShadow = GenerateNormalShadowRenderInfo(renderContext, ref decal, ref info);

			if (!keepShadow && ShouldCacheVertices(in decal))
				RemoveShadowDecalFromSurface(decal.SurfID, decalHandle);
		}
	}

	public void ComputeRenderInfo(ref ShadowDecalRenderInfo info, ShadowHandle_t handle) {
		ref ShadowInfo_t i = ref Shadows[handle].Info;
		info.TexOrigin = i.TexOrigin;
		info.TexSize = i.TexSize;
		info.FalloffOffset = i.FalloffOffset;
		info.FalloffAmount = i.FalloffAmount;
		info.FalloffBias = i.FalloffBias;

		float falloffDist = i.MaxDist - i.FalloffOffset;
		info.OOZFalloffDist = (falloffDist > 0.0f) ? 1.0f / falloffDist : 1.0f;
	}

	int AddNormalShadowsToMeshBuilder(ref MeshBuilder meshBuilder, ref ShadowRenderInfo info) {
		ShadowDecalRenderInfo shadow = default;
		int baseIndex = 0;
		for (int i = 0; i < info.Count; ++i) {
			ref ShadowVertexCache vertexCache = ref (info.Cache![i] < 0
				? ref TempVertexCache.AsSpan()[(int)(-info.Cache[i] - 1)]
				: ref VertexCache[(int)info.Cache[i]]);

			Span<ShadowVertex> verts = GetCachedVerts(in vertexCache);
			g_ShadowMgr.ComputeRenderInfo(ref shadow, vertexCache.Shadow);

			int j;
			byte c;
			Vector2 texCoord;
			int vCount = vertexCache.Count - 2;
			if (vCount <= 0)
				continue;

			int vert = 0;
			for (j = 0; j < vCount; ++j, ++vert) {
				MathLib.Vector2DMultiply(new Vector2(verts[vert].ShadowSpaceTexCoord.X, verts[vert].ShadowSpaceTexCoord.Y), shadow.TexSize, out texCoord);
				texCoord += shadow.TexOrigin;
				c = ((IShadowMgrInternal)this).ComputeDarkness(verts[vert].ShadowSpaceTexCoord.Z, in shadow);

				meshBuilder.Position3fv(verts[vert].Position);
				meshBuilder.Color4ub(c, c, c, c);
				meshBuilder.TexCoord2fv(0, texCoord);
				meshBuilder.AdvanceVertex();

				meshBuilder.FastIndex((ushort)baseIndex);
				meshBuilder.FastIndex((ushort)(j + baseIndex + 1));
				meshBuilder.FastIndex((ushort)(j + baseIndex + 2));
			}

			MathLib.Vector2DMultiply(new Vector2(verts[vert].ShadowSpaceTexCoord.X, verts[vert].ShadowSpaceTexCoord.Y), shadow.TexSize, out texCoord);
			texCoord += shadow.TexOrigin;
			c = ((IShadowMgrInternal)this).ComputeDarkness(verts[vert].ShadowSpaceTexCoord.Z, in shadow);
			meshBuilder.Position3fv(verts[vert].Position);
			meshBuilder.Color4ub(c, c, c, c);
			meshBuilder.TexCoord2fv(0, texCoord);
			meshBuilder.AdvanceVertex();
			++vert;

			MathLib.Vector2DMultiply(new Vector2(verts[vert].ShadowSpaceTexCoord.X, verts[vert].ShadowSpaceTexCoord.Y), shadow.TexSize, out texCoord);
			texCoord += shadow.TexOrigin;
			c = ((IShadowMgrInternal)this).ComputeDarkness(verts[vert].ShadowSpaceTexCoord.Z, in shadow);
			meshBuilder.Position3fv(verts[vert].Position);
			meshBuilder.Color4ub(c, c, c, c);
			meshBuilder.TexCoord2fv(0, texCoord);
			meshBuilder.AdvanceVertex();

			baseIndex += vCount + 2;
		}

		return baseIndex;
	}

	int AddDisplacementShadowsToMeshBuilder(ref MeshBuilder meshBuilder, ref ShadowRenderInfo info, int baseIndex) {
		if (!DispInfo.r_DrawDisp.GetBool())
			return baseIndex;

		for (int i = 0; i < info.DispCount; ++i)
			baseIndex = DispInfo.DispInfo_AddShadowsToMeshBuilder(ref meshBuilder, info.DispCache![i], baseIndex);

		return baseIndex;
	}

	void RenderDebuggingInfo(in ShadowRenderInfo info, ShadowDebugFunc func) {
		for (int i = 0; i < info.Count; ++i) {
			ref ShadowVertexCache vertexCache = ref (info.Cache![i] < 0 ? ref TempVertexCache.AsSpan()[(int)(-info.Cache[i] - 1)] : ref VertexCache[(int)info.Cache[i]]);

			Span<ShadowVertex> verts = GetCachedVerts(vertexCache);

			float totalArea = 0.0f;
			Vector3 centroid = new(0, 0, 0);
			Vector3 apex = verts[0].Position;
			int count = vertexCache.Count;

			for (int j = 0; j < count - 2; ++j) {
				Vector3 v1 = verts[j + 1].Position;
				Vector3 v2 = verts[j + 2].Position;
				MathLib.CrossProduct(v2 - v1, v1 - apex, out Vector3 normal);
				float area = normal.Length();
				totalArea += area;
				centroid += (apex + v1 + v2) * area / 3.0f;
			}

			if (totalArea != 0)
				centroid /= totalArea;

			func(vertexCache.Shadow, centroid);
		}
	}

	static void DrawShadowID(ShadowHandle_t shadowHandle, in Vector3 centroid) {
#if !SWDS
		Span<char> buf = stackalloc char[16];
		shadowHandle.TryFormat(buf, out int written);
		debugoverlay.AddTextOverlay(centroid, 0, buf[..written]);
#endif
	}

	void RenderShadowList(MatRenderContextPtr renderContext, ShadowDecalHandle_t decalHandle, Matrix4x4? modelToWorld) {
		if (DecalsToRender > ShadowDecalCache.Length) {
			int diff = Math.Min(DecalsToRender, MAX_SHADOW_DECAL_CACHE_COUNT) - ShadowDecalCache.Length;
			if (diff > 0) {
				Array.Resize(ref ShadowDecalCache, ShadowDecalCache.Length + diff);
				DevMsg($"[CShadowMgr::RenderShadowList] growing shadow decal cache (decals: {DecalsToRender}, cache: {ShadowDecalCache.Length}, diff: {diff}).\n");
			}
		}

		if (DecalsToRender > DispShadowDecalCache.Length) {
			int diff = Math.Min(DecalsToRender, MAX_SHADOW_DECAL_CACHE_COUNT) - DispShadowDecalCache.Length;
			if (diff > 0) {
				Array.Resize(ref DispShadowDecalCache, DispShadowDecalCache.Length + diff);
				DevMsg($"[CShadowMgr::RenderShadowList] growing disp shadow decal cache (decals: {DecalsToRender}, cache: {DispShadowDecalCache.Length}, diff: {diff}).\n");
			}
		}

		ref Shadow shadow = ref Shadows[ShadowDecals[decalHandle].Shadow];

#if !SWDS
		if (r_shadowwireframe.GetInt() == 0)
			renderContext.Bind(shadow.Material!, shadow.BindProxy);
		else
			renderContext.Bind(MatSys.MaterialWorldWireframe!, null);
#else
		renderContext.Bind(shadow.Material!, shadow.BindProxy);
#endif

		ClearTempCache();

		ShadowRenderInfo info = default;

		info.Cache = ShadowDecalCache;
		info.DispCache = DispShadowDecalCache;

		info.ModelToWorld = modelToWorld;
		if (modelToWorld != null)
			MathLib.MatrixInverseTR(modelToWorld.Value, out info.WorldToModel);

		info.MaxIndices = renderContext.GetMaxIndicesToRender();
		info.MaxVertices = renderContext.GetMaxVerticesToRender(shadow.Material!);

		GenerateShadowRenderInfo(renderContext, decalHandle, ref info);
		Assert(info.Count <= DecalsToRender);
		Assert(info.DispCount <= DecalsToRender);
		Assert(info.Count <= ShadowDecalCache.Length && info.Count <= MAX_SHADOW_DECAL_CACHE_COUNT);
		Assert(info.DispCount <= DispShadowDecalCache.Length && info.DispCount <= MAX_SHADOW_DECAL_CACHE_COUNT);

		IMesh mesh = renderContext.GetDynamicMesh();
		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, info.VertexCount, info.IndexCount);

		int baseIndex = AddNormalShadowsToMeshBuilder(ref meshBuilder, ref info);
		AddDisplacementShadowsToMeshBuilder(ref meshBuilder, ref info, baseIndex);

		meshBuilder.End();
		mesh.Draw();

		if (r_shadowids.GetInt() != 0)
			RenderDebuggingInfo(in info, DrawShadowID);
	}

	public void SetNumWorldMaterialBuckets(int numMaterialSortBins) {
		NumWorldMaterialBuckets = numMaterialSortBins;
		foreach (FlashlightHandle_t flashlightID in ValidFlashlightHandles) {
			FlashlightStates[flashlightID].Info.MaterialBuckets.SetNumMaterialSortIDs(numMaterialSortBins);
			FlashlightStates[flashlightID].Info.OccluderBuckets.SetNumMaterialSortIDs(numMaterialSortBins);
		}
		ClearAllFlashlightMaterialBuckets();
	}

	void ClearAllFlashlightMaterialBuckets() {
		if (r_flashlight_version2.GetInt() != 0)
			return;

		foreach (FlashlightHandle_t flashlightID in ValidFlashlightHandles)
			FlashlightStates[flashlightID].Info.MaterialBuckets.Flush();
	}

	void AllocFlashlightMaterialBuckets(FlashlightHandle_t flashlightID) {
		Assert(FlashlightStates.Count >= flashlightID);
		FlashlightStates[flashlightID].Info.MaterialBuckets.SetNumMaterialSortIDs(NumWorldMaterialBuckets);
		FlashlightStates[flashlightID].Info.OccluderBuckets.SetNumMaterialSortIDs(NumWorldMaterialBuckets);
	}

	public void UpdateFlashlightState(ShadowHandle_t shadowHandle, in FlashlightState lightState) {
		FlashlightStates[Shadows[shadowHandle].FlashlightHandle].Info.FlashlightState = lightState;
	}

	public void SetFlashlightDepthTexture(ShadowHandle_t shadowHandle, ITexture? flashlightDepthTexture, byte shadowStencilBit) {
		Shadows[shadowHandle].FlashlightDepthTexture = flashlightDepthTexture;
		Shadows[shadowHandle].ShadowStencilBit = shadowStencilBit;
	}

	void SetStencilAndScissor(MatRenderContextPtr renderContext, ref FlashlightInfo flashlightInfo, bool useStencil) {
		MathLib.MatrixInverseGeneral(Shadows[flashlightInfo.Shadow].Info.WorldToShadow, out Matrix4x4 matFlashlightToWorld);

		Span<Vector3> frustumPoints = [ new(0.0f, 0.0f, 0.0f), new(1.0f, 0.0f, 0.0f), new(1.0f, 1.0f, 0.0f), new(0.0f, 1.0f, 0.0f),
											new(0.0f, 0.0f, 1.0f), new(0.0f, 1.0f, 1.0f), new(1.0f, 1.0f, 1.0f), new(1.0f, 0.0f, 1.0f),
											new(1.0f, 0.0f, 0.0f), new(1.0f, 0.0f, 1.0f), new(1.0f, 1.0f, 1.0f), new(1.0f, 1.0f, 0.0f),
											new(0.0f, 0.0f, 0.0f), new(0.0f, 1.0f, 0.0f), new(0.0f, 1.0f, 1.0f), new(0.0f, 0.0f, 1.0f),
											new(0.0f, 1.0f, 0.0f), new(1.0f, 1.0f, 0.0f), new(1.0f, 1.0f, 1.0f), new(0.0f, 1.0f, 1.0f),
											new(0.0f, 0.0f, 0.0f), new(0.0f, 0.0f, 1.0f), new(1.0f, 0.0f, 1.0f), new(1.0f, 0.0f, 0.0f)];

		Span<Vector3> worldFrustumPoints = stackalloc Vector3[24];
		for (int i = 0; i < 24; i++)
			matFlashlightToWorld.V3Mul(frustumPoints[i], out worldFrustumPoints[i]);

		const float planeEpsilon = 0.4f;
		ExtractFrustumPlanes(out Frustum frustumPlanes, planeEpsilon);
		Vector3 nearNormal = frustumPlanes[FrustumPlane.NearZ].Normal;
		Vector3 farNormal = frustumPlanes[FrustumPlane.FarZ].Normal;
		float nearDist = frustumPlanes[FrustumPlane.NearZ].Dist;
		float farDist = frustumPlanes[FrustumPlane.FarZ].Dist;

		Span<Vector3> tempFace = stackalloc Vector3[5];
		Span<Vector3> clippedFace = stackalloc Vector3[6];
		Vector3[][] clippedPolygons = new Vector3[8][];
		for (int i = 0; i < 8; i++)
			clippedPolygons[i] = new Vector3[10];
		Span<int> numVertices = stackalloc int[8];
		int numPolygons = 0;

		for (int i = 0; i < 6; i++) {
			Span<Vector3> inVerts = worldFrustumPoints[(4 * i)..];

			int clipCount = MathLib.ClipPolyToPlane(inVerts, 4, tempFace, nearNormal, nearDist);

			if (clipCount > 2) {
				clipCount = MathLib.ClipPolyToPlane(tempFace, clipCount, clippedFace, farNormal, farDist);

				if (clipCount > 2) {
					clippedFace[..clipCount].CopyTo(clippedPolygons[numPolygons]);
					numVertices[numPolygons] = clipCount;
					numPolygons++;
				}
			}
		}

		Span<Vector3> nearPlane = stackalloc Vector3[4];
		Span<Vector3> farPlane = stackalloc Vector3[4];
		ConstructNearAndFarPolygons(nearPlane, farPlane, planeEpsilon);

		int nearClipCount = ClipPlaneToFrustum(nearPlane, clippedPolygons[numPolygons], worldFrustumPoints);
		if (nearClipCount > 2) {
			numVertices[numPolygons] = nearClipCount;
			numPolygons++;
		}

		for (int i = 0; i < numPolygons; i++) {
			for (int j = 0; j < numVertices[i]; j++) {
				for (int k = i + 1; k < numPolygons; k++) {
					for (int m = 0; m < numVertices[k]; m++) {
						if (SufficientlyClose(clippedPolygons[i][j], clippedPolygons[k][m], 0.1f))
							clippedPolygons[k][m] = clippedPolygons[i][j];
					}
				}
			}
		}

		flashlightInfo.FlashlightState.Scissor = false;
		if (r_flashlightscissor.GetBool() && (numPolygons > 0)) {
			flashlightInfo.FlashlightState.Scissor = ScreenSpaceRectFromPoints(renderContext, clippedPolygons, numVertices, numPolygons, out int left, out int top, out int right, out int bottom);
			if (flashlightInfo.FlashlightState.Scissor) {
				flashlightInfo.FlashlightState.Left = left;
				flashlightInfo.FlashlightState.Top = top;
				flashlightInfo.FlashlightState.Right = right;
				flashlightInfo.FlashlightState.Bottom = bottom;
			}
		}

		if (r_flashlightdrawclip.GetBool() && r_flashlightclip.GetBool() && useStencil) {
			for (int i = 0; i < numPolygons; i++)
				DrawDebugPolygon(numVertices[i], clippedPolygons[i], false, false);
		}

		if (r_flashlightclip.GetBool() && useStencil) {
			renderContext.SetStencilEnable(true);
			renderContext.SetStencilFailOperation(StencilOperation.Replace);
			renderContext.SetStencilZFailOperation(StencilOperation.Replace);
			renderContext.SetStencilPassOperation(StencilOperation.Replace);
			renderContext.SetStencilCompareFunction(StencilComparisonFunction.Always);
			renderContext.SetStencilReferenceValue(Shadows[flashlightInfo.Shadow].ShadowStencilBit);
			renderContext.SetStencilTestMask(Shadows[flashlightInfo.Shadow].ShadowStencilBit);
			renderContext.SetStencilWriteMask(Shadows[flashlightInfo.Shadow].ShadowStencilBit);

			for (int i = 0; i < numPolygons; i++)
				DrawPolygonToStencil(renderContext, numVertices[i], clippedPolygons[i], true, false);

			renderContext.SetStencilEnable(false);
		}
	}

	public void SetFlashlightStencilMasks(bool doMasking) {
		if (r_flashlight_version2.GetInt() != 0)
			return;

		if (!(r_flashlightclip.GetBool() || r_flashlightscissor.GetBool()))
			return;

		if (FlashlightStates.Count == 0)
			return;

		using MatRenderContextPtr renderContext = new(materials);

		foreach (FlashlightHandle_t flashlightID in ValidFlashlightHandles) {
			ref FlashlightInfo flashlightInfo = ref FlashlightStates[flashlightID].Info;

			SetStencilAndScissor(renderContext, ref flashlightInfo, Shadows[flashlightInfo.Shadow].FlashlightDepthTexture != null);

		}
	}

	void DisableStencilAndScissorMasking(MatRenderContextPtr renderContext) {
		if (r_flashlightclip.GetBool())
			renderContext.SetStencilEnable(false);

		if (r_flashlightscissor.GetBool())
			renderContext.SetScissorRect(-1, -1, -1, -1, false);
	}

	void EnableStencilAndScissorMasking(MatRenderContextPtr renderContext, in FlashlightInfo flashlightInfo, bool doMasking) {
		if (!(r_flashlightclip.GetBool() || r_flashlightscissor.GetBool()) || !doMasking)
			return;

		if (renderContext.GetRenderTarget() == null) {
			if (r_flashlightclip.GetBool() && Shadows[flashlightInfo.Shadow].FlashlightDepthTexture != null) {
				byte shadowStencilBit = Shadows[flashlightInfo.Shadow].ShadowStencilBit;

				renderContext.SetStencilEnable(true);
				renderContext.SetStencilFailOperation(StencilOperation.Keep);
				renderContext.SetStencilZFailOperation(StencilOperation.Keep);
				renderContext.SetStencilPassOperation(StencilOperation.Keep);

				renderContext.SetStencilCompareFunction(StencilComparisonFunction.Equal);
				renderContext.SetStencilReferenceValue(shadowStencilBit);
				renderContext.SetStencilTestMask(shadowStencilBit);
				renderContext.SetStencilWriteMask(0x00000000);
			}

			if (r_flashlightscissor.GetBool() && flashlightInfo.FlashlightState.Scissor)
				renderContext.SetScissorRect(flashlightInfo.FlashlightState.Left, flashlightInfo.FlashlightState.Top, flashlightInfo.FlashlightState.Right, flashlightInfo.FlashlightState.Bottom, true);
		}
		else
			DisableStencilAndScissorMasking(renderContext);
	}

	public void SetFlashlightRenderState(ShadowHandle_t handle) => throw new NotImplementedException();

	public void RenderFlashlights(bool doMasking, Matrix4x4? modelToWorld = null) {
#if !SWDS
		if (r_flashlight_version2.GetInt() != 0)
			return;

		if (r_flashlightrender.GetBool() == false)
			return;

		if (FlashlightStates.Count == 0)
			return;

		bool wireframe = r_shadowwireframe.GetBool();

		using MatRenderContextPtr renderContext = new(materials);

		renderContext.SetFlashlightMode(true);

		foreach (FlashlightHandle_t flashlightID in ValidFlashlightHandles) {
			ref FlashlightInfo flashlightInfo = ref FlashlightStates[flashlightID].Info;
			MaterialsBuckets<SurfaceHandle_t> materialBuckets = flashlightInfo.MaterialBuckets;
			int sortIDHandle = materialBuckets.GetFirstUsedSortID();
			if (sortIDHandle == materialBuckets.InvalidSortIDHandle())
				continue;

			renderContext.SetFlashlightStateEx(flashlightInfo.FlashlightState, Shadows[flashlightInfo.Shadow].Info.WorldToShadow, Shadows[flashlightInfo.Shadow].FlashlightDepthTexture);
			EnableStencilAndScissorMasking(renderContext, flashlightInfo, doMasking);

			for (; sortIDHandle != materialBuckets.InvalidSortIDHandle(); sortIDHandle = materialBuckets.GetNextUsedSortID(sortIDHandle)) {
				int sortID = materialBuckets.GetSortID(sortIDHandle);

				if (wireframe)
					renderContext.Bind(MatSys.MaterialWorldWireframe!, null);
				else {
					renderContext.Bind(MatSys.MaterialSortInfoArray![sortID].Material!, null);
					renderContext.BindLightmapPage(MatSys.MaterialSortInfoArray![sortID].LightmapPageID);
				}

				int elemHandle;
				int numIndices = 0;
				for (elemHandle = materialBuckets.GetElementListHead(sortID); elemHandle != materialBuckets.InvalidElementHandle(); elemHandle = materialBuckets.GetElementListNext(elemHandle)) {
					SurfaceHandle_t surfID = materialBuckets.GetElement(elemHandle);
					if (!ModelLoader.SurfaceHasDispInfo(ref ModelLoader.SurfaceHandleFromIndex(surfID)))
						numIndices += 3 * (ModelLoader.MSurf_VertCount(ref ModelLoader.SurfaceHandleFromIndex(surfID)) - 2);
				}

				if (numIndices > 0) {
					IMesh mesh = renderContext.GetDynamicMesh(false, MatSys.WorldStaticMeshes[sortID]);
					MeshBuilder meshBuilder = new();
					meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, 0, numIndices);

					for (elemHandle = materialBuckets.GetElementListHead(sortID); elemHandle != materialBuckets.InvalidElementHandle(); elemHandle = materialBuckets.GetElementListNext(elemHandle)) {
						SurfaceHandle_t surfID = materialBuckets.GetElement(elemHandle);
						if (!ModelLoader.SurfaceHasDispInfo(ref ModelLoader.SurfaceHandleFromIndex(surfID)))
							BuildIndicesForWorldSurface(ref meshBuilder, surfID, host_state.WorldBrush!);
					}

					meshBuilder.End(false, true);
				}

				for (elemHandle = materialBuckets.GetElementListHead(sortID); elemHandle != materialBuckets.InvalidElementHandle(); elemHandle = materialBuckets.GetElementListNext(elemHandle)) {
					SurfaceHandle_t surfID = materialBuckets.GetElement(elemHandle);
					if (ModelLoader.SurfaceHasDispInfo(ref ModelLoader.SurfaceHandleFromIndex(surfID))) {
						DispInfo? disp = (DispInfo?)ModelLoader.SurfaceHandleFromIndex(surfID).DispInfo;
						Assert(disp != null);
						if (wireframe)
							disp!.SpecifyDynamicMesh();
						else {
							Assert(disp != null && disp.Mesh != null && disp.Mesh.Mesh != null);
							disp!.Mesh!.Mesh!.Draw(disp.IndexOffset, disp.NumIndices);
						}
					}
				}
			}
		}

		renderContext.SetFlashlightMode(false);

		DisableStencilAndScissorMasking(renderContext);
#endif
	}

	public Frustum_t GetFlashlightFrustum(ShadowHandle_t handle) {
		Assert((Shadows[handle].Flags & ShadowCreateFlags.Flashlight) != 0);
		Assert(Shadows[handle].FlashlightHandle != SHADOW_HANDLE_INVALID);
		return FlashlightStates[Shadows[handle].FlashlightHandle].Info.Frustum;
	}

	public ref readonly FlashlightState GetFlashlightState(ShadowHandle_t handle) {
		Assert((Shadows[handle].Flags & ShadowCreateFlags.Flashlight) != 0);
		Assert(Shadows[handle].FlashlightHandle != SHADOW_HANDLE_INVALID);
		return ref FlashlightStates[Shadows[handle].FlashlightHandle].Info.FlashlightState;
	}

	public void DrawFlashlightDecals(int sortGroup, bool doMasking) {
		if (r_flashlight_version2.GetInt() != 0)
			return;

		if (FlashlightStates.Count == 0)
			return;

		using MatRenderContextPtr renderContext = new(materials);

		renderContext.SetFlashlightMode(true);

		foreach (FlashlightHandle_t flashlightID in ValidFlashlightHandles) {
			ref FlashlightInfo flashlightInfo = ref FlashlightStates[flashlightID].Info;
			renderContext.SetFlashlightState(flashlightInfo.FlashlightState, Shadows[flashlightInfo.Shadow].Info.WorldToShadow);

			EnableStencilAndScissorMasking(renderContext, flashlightInfo, doMasking);

#if !SWDS
			Render.DecalSurfaceDraw(renderContext.Context, sortGroup);
#endif
		}

		renderContext.SetFlashlightMode(false);

		DisableStencilAndScissorMasking(renderContext);
	}

	public void DrawFlashlightDecalsOnDisplacements(int sortGroup, ReadOnlySpan<DispInfo?> visibleDisps, int visibleDispCount, bool doMasking) {
		if (r_flashlight_version2.GetInt() != 0)
			return;

		if (FlashlightStates.Count == 0)
			return;

		using MatRenderContextPtr renderContext = new(materials);

		renderContext.SetFlashlightMode(true);

		// DispInfo_BatchDecals(visibleDisps, visibleDispCount);

		foreach (FlashlightHandle_t flashlightID in ValidFlashlightHandles) {
			ref FlashlightInfo flashlightInfo = ref FlashlightStates[flashlightID].Info;
			renderContext.SetFlashlightState(flashlightInfo.FlashlightState, Shadows[flashlightInfo.Shadow].Info.WorldToShadow);

			EnableStencilAndScissorMasking(renderContext, flashlightInfo, doMasking);

			// DispInfo_DrawDecals(visibleDisps, visibleDispCount);
		}

		renderContext.SetFlashlightMode(false);

		DisableStencilAndScissorMasking(renderContext);
	}

	public void DrawFlashlightDecalsOnSingleSurface(SurfaceHandle_t surfID, bool doMasking) => throw new NotImplementedException();

	public void DrawFlashlightOverlays(int sortGroup, bool doMasking) {
		if (r_flashlight_version2.GetInt() != 0)
			return;

		if (FlashlightStates.Count == 0)
			return;

		if (r_flashlightrender.GetBool() == false)
			return;

		using MatRenderContextPtr renderContext = new(materials);

		renderContext.SetFlashlightMode(true);

		foreach (FlashlightHandle_t flashlightID in ValidFlashlightHandles) {
			ref FlashlightInfo flashlightInfo = ref FlashlightStates[flashlightID].Info;
			renderContext.SetFlashlightState(flashlightInfo.FlashlightState, Shadows[flashlightInfo.Shadow].Info.WorldToShadow);

			EnableStencilAndScissorMasking(renderContext, flashlightInfo, doMasking);

			// OverlayMgr().RenderOverlays(sortGroup);
		}

		renderContext.SetFlashlightMode(false);

		DisableStencilAndScissorMasking(renderContext);
	}

	public void DrawFlashlightDepthTexture() => throw new NotImplementedException();

	public void AddFlashlightRenderable(ShadowHandle_t shadowHandle, IClientRenderable? renderable) {
		ref Shadow shadow = ref Shadows[shadowHandle];
		FlashlightInfoBox flashlightInfo = FlashlightStates[shadow.FlashlightHandle];

		if (renderable!.GetModelInstance() != MODEL_INSTANCE_INVALID)
			flashlightInfo.Info.Renderables.Add(renderable);
	}

	public ref uint FirstModelInShadow(ShadowHandle_t h) => ref Shadows[h].FirstModel;
}
