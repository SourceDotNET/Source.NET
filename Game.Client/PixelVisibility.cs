using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Client;

public struct PixelVisQueryParams
{
	public const float PIXELVIS_DEFAULT_PROXY_SIZE = 2.0f;
	public const float PIXELVIS_DEFAULT_FADE_TIME = 0.0625f;

	public Vector3 Position;
	public float ProxySize;
	public float ProxyAspect;
	public float FadeTime;
	public bool Setup;
	public bool SizeInScreenspace;

	public PixelVisQueryParams() => Setup = false;

	public void Init(in Vector3 origin, float proxySizeIn = PIXELVIS_DEFAULT_PROXY_SIZE, float proxyAspectIn = 1.0f, float fadeTimeIn = PIXELVIS_DEFAULT_FADE_TIME) {
		Position = origin;
		ProxySize = proxySizeIn;
		ProxyAspect = proxyAspectIn;
		FadeTime = fadeTimeIn;
		Setup = true;
		SizeInScreenspace = false;
	}
}

public class PixelVisSet
{
	public float ProxySize;
	public float ProxyAspect;
	public float FadeTimeInv;
	public ushort QueryList;
	public ushort Serial;
	public bool SizeIsScreenSpace;
	int FrameIssued;

	public PixelVisSet() {
		ProxySize = 0;
		ProxyAspect = 0;
		FadeTimeInv = 0;
		FrameIssued = 0;
		Serial = 0;
		QueryList = 0xFFFF;
		SizeIsScreenSpace = false;
	}

	public void Init(in PixelVisQueryParams parms) {
		Assert(parms.Setup);
		ProxySize = parms.ProxySize;
		ProxyAspect = parms.ProxyAspect;
		if (parms.FadeTime > 0.0f)
			FadeTimeInv = 1.0f / parms.FadeTime;
		else
			FadeTimeInv = 1.0f / 0.125f;
		FrameIssued = 0;
		SizeIsScreenSpace = parms.SizeInScreenspace;
	}

	public void MarkActive() => FrameIssued = (int)gpGlobals.FrameCount;

	public bool IsActive() => throw new NotImplementedException();
}

public class PixelVisibilityQuery
{
	public Vector3 Origin;
	public int FrameIssued;
	float BrightnessTarget;
	float ClipFraction;
	nint QueryHandle;
	nint QueryHandleCount;
	bool WasQueriedThisFrame;
	bool Failed;
	bool HasValidQueryResults;
	ushort ViewID;

	public PixelVisibilityQuery() {
		using MatRenderContextPtr renderContext = new(materials);
		SetView(0xFFFF);
		QueryHandle = renderContext.CreateOcclusionQueryObject();
		QueryHandleCount = renderContext.CreateOcclusionQueryObject();
	}

	public void ResetOcclusionQueries() {
		using MatRenderContextPtr renderContext = new(materials);
		if (QueryHandle != MaterialSystemGlobals.INVALID_OCCLUSION_QUERY_OBJECT_HANDLE)
			renderContext.ResetOcclusionQueryObject(QueryHandle);
		if (QueryHandleCount != MaterialSystemGlobals.INVALID_OCCLUSION_QUERY_OBJECT_HANDLE)
			renderContext.ResetOcclusionQueryObject(QueryHandleCount);
	}

	public bool IsValid() => QueryHandle != MaterialSystemGlobals.INVALID_OCCLUSION_QUERY_OBJECT_HANDLE;

	public bool IsForView(int viewID) => ViewID == viewID;

	public bool IsActive() => throw new NotImplementedException();

	public float GetFractionVisible(float fadeTimeInv) {
		if (!IsValid())
			return 0.0f;

		if (!WasQueriedThisFrame) {
			using MatRenderContextPtr renderContext = new(materials);
			WasQueriedThisFrame = true;
			int pixels = -1;
			int pixelsPossible = -1;
			if (PixelVisibility.r_pixelvisibility_partial.GetBool()) {
				if (FrameIssued != -1) {
					pixelsPossible = renderContext.OcclusionQuery_GetNumPixelsRendered(QueryHandleCount);
					pixels = renderContext.OcclusionQuery_GetNumPixelsRendered(QueryHandle);
				}

				if (PixelVisibility.r_pixelvisibility_spew.GetBool() && CurrentViewID() == 0)
					DevMsg(1, $"Pixels visible: {pixels} (qh:{QueryHandle}) Pixels possible: {pixelsPossible} (qh:{QueryHandleCount}) (frame:{gpGlobals.FrameCount})\n");

				if (pixels < 0 || pixelsPossible < 0) {
					Failed = FrameIssued >= 0;
					return BrightnessTarget * ClipFraction;
				}
				HasValidQueryResults = true;

				if (pixelsPossible > 0) {
					float target = (float)pixels / (float)pixelsPossible;
					target = (target >= 0.95f) ? 1.0f : (target < 0.0f) ? 0.0f : target;
					float rate = (float)gpGlobals.FrameTime * fadeTimeInv;
					BrightnessTarget = MathLib.Approach(target, BrightnessTarget, rate);
				}
				else
					BrightnessTarget = 0.0f;
			}
			else {
				if (FrameIssued != -1)
					pixels = renderContext.OcclusionQuery_GetNumPixelsRendered(QueryHandle);

				if (PixelVisibility.r_pixelvisibility_spew.GetBool() && CurrentViewID() == 0)
					DevMsg(1, $"Pixels visible: {pixels} (qh:{QueryHandle}) (frame:{gpGlobals.FrameCount})\n");

				if (pixels < 0) {
					Failed = FrameIssued >= 0;
					return BrightnessTarget * ClipFraction;
				}
				HasValidQueryResults = true;
				if (FrameIssued == gpGlobals.FrameCount - 1) {
					float rate = (float)gpGlobals.FrameTime * fadeTimeInv;
					float target = 0.0f;
					if (pixels > 0) {
						rate *= 0.5f;
						target = 1.0f;
					}
					BrightnessTarget = MathLib.Approach(target, BrightnessTarget, rate);
				}
				else
					BrightnessTarget = 0.0f;
			}
		}

		return BrightnessTarget * ClipFraction;
	}

	public void IssueQuery(IMatRenderContext renderContext, float proxySize, float proxyAspect, IMaterial material, bool sizeIsScreenSpace) => throw new NotImplementedException();

	public void IssueCountingQuery(IMatRenderContext renderContext, float proxySize, float proxyAspect, IMaterial material, bool sizeIsScreenSpace) => throw new NotImplementedException();

	public void SetView(int viewID) {
		ViewID = (ushort)viewID;
		BrightnessTarget = 0.0f;
		ClipFraction = 1.0f;
		FrameIssued = -1;
		Failed = false;
		WasQueriedThisFrame = false;
		HasValidQueryResults = false;
	}
}

public class PixelVisibilitySystem : AutoGameSystem
{
	readonly UtlMultiList<PixelVisSet> SetList = new();
	readonly UtlMultiList<PixelVisibilityQuery> QueryList = new();
	ushort FreeQueriesList;
	ushort ActiveSetsList;
	ushort FreeSetsList;
	IMaterial? ProxyMaterial;
	IMaterial? DrawMaterial;
	bool HwCanTestGlows;
	bool DrawQueries;

	public PixelVisibilitySystem() : base("CPixelVisibilitySystem") {
		HwCanTestGlows = true;
		DrawQueries = false;
	}

	public override void LevelInitPreEntity() {
		bool fastqueries = PixelVisibility.HasFastQueries();

		HwCanTestGlows = PixelVisibility.r_dopixelvisibility.GetBool() && fastqueries && engine.GetDXSupportLevel() >= 80;
		if (HwCanTestGlows) {
			using MatRenderContextPtr renderContext = new(materials);

			nint query = renderContext.CreateOcclusionQueryObject();
			if (query != MaterialSystemGlobals.INVALID_OCCLUSION_QUERY_OBJECT_HANDLE)
				renderContext.DestroyOcclusionQueryObject(query);
			else
				HwCanTestGlows = false;
		}

		ProxyMaterial = materials.FindMaterial("engine/occlusionproxy", MaterialDefines.TEXTURE_GROUP_CLIENT_EFFECTS);
		ProxyMaterial!.IncrementReferenceCount();
		DrawMaterial = materials.FindMaterial("engine/occlusionproxy_countdraw", MaterialDefines.TEXTURE_GROUP_CLIENT_EFFECTS);
		DrawMaterial!.IncrementReferenceCount();
		FreeQueriesList = QueryList.CreateList();
		ActiveSetsList = SetList.CreateList();
		FreeSetsList = SetList.CreateList();
	}

	public override void LevelShutdownPostEntity() {
		DrawMaterial!.DecrementReferenceCount();
		DrawMaterial = null;
		ProxyMaterial!.DecrementReferenceCount();
		ProxyMaterial = null;
		DeleteUnusedSets(true);
		SetList.Purge();
		QueryList.Purge();
		FreeQueriesList = UtlMultiList<PixelVisibilityQuery>.InvalidIndex();
		ActiveSetsList = UtlMultiList<PixelVisSet>.InvalidIndex();
		FreeSetsList = UtlMultiList<PixelVisSet>.InvalidIndex();
	}

	public float GetFractionVisible(in PixelVisQueryParams parms, ref int queryHandle) {
		if (!HwCanTestGlows || PixelVisibility.building_cubemaps.GetBool())
			return PixelVisibility.GlowSightDistance(parms.Position, true) > 0 ? 1.0f : 0.0f;
		if (CurrentViewID() < 0)
			return 0.0f;

		PixelVisSet set = FindOrCreatePixelVisSet(parms, ref queryHandle);
		ushort node = FindOrCreateQueryForView(set, (int)CurrentViewID());
		QueryList[node].Origin = parms.Position;
		float fraction = QueryList[node].GetFractionVisible(set.FadeTimeInv);
		set.MarkActive();
		return fraction;
	}

	public void EndView() => throw new NotImplementedException();

	public void EndScene() => throw new NotImplementedException();

	public ushort FindQueryForView(PixelVisSet set, int viewID) {
		ushort node = QueryList.Head(set.QueryList);
		while (node != UtlMultiList<PixelVisibilityQuery>.InvalidIndex()) {
			if (QueryList[node].IsForView(viewID))
				return node;
			node = QueryList.Next(node);
		}
		return UtlMultiList<PixelVisibilityQuery>.InvalidIndex();
	}

	public ushort FindOrCreateQueryForView(PixelVisSet set, int viewID) {
		ushort node = FindQueryForView(set, viewID);
		if (node != UtlMultiList<PixelVisibilityQuery>.InvalidIndex())
			return node;

		node = AllocQuery();
		QueryList.LinkToHead(set.QueryList, node);
		QueryList[node].SetView(viewID);
		return node;
	}

	public void DeleteUnusedQueries(PixelVisSet set, bool deleteAll) {
		ushort node = QueryList.Head(set.QueryList);
		while (node != UtlMultiList<PixelVisibilityQuery>.InvalidIndex()) {
			ushort next = QueryList.Next(node);
			if (deleteAll || !QueryList[node].IsActive()) {
				QueryList.Unlink(set.QueryList, node);
				QueryList.LinkToHead(FreeQueriesList, node);
			}
			node = next;
		}
	}

	public void DeleteUnusedSets(bool deleteAll) {
		ushort node = SetList.Head(ActiveSetsList);
		while (node != UtlMultiList<PixelVisSet>.InvalidIndex()) {
			ushort next = SetList.Next(node);
			PixelVisSet set = SetList[node];
			if (deleteAll || !SetList[node].IsActive())
				DeleteUnusedQueries(set, true);
			else
				DeleteUnusedQueries(set, false);
			if (QueryList.Head(set.QueryList) == UtlMultiList<PixelVisibilityQuery>.InvalidIndex())
				FreeSet(node);
			node = next;
		}
	}

	public void ShowQueries(bool show) => throw new NotImplementedException();

	public ushort AllocQuery() {
		ushort node = QueryList.Head(FreeQueriesList);
		if (node != UtlMultiList<PixelVisibilityQuery>.InvalidIndex()) {
			QueryList.Unlink(FreeQueriesList, node);
			QueryList[node].ResetOcclusionQueries();
		}
		else
			node = QueryList.Alloc();
		return node;
	}

	public ushort AllocSet() {
		ushort node = SetList.Head(FreeSetsList);
		if (node != UtlMultiList<PixelVisSet>.InvalidIndex())
			SetList.Unlink(FreeSetsList, node);
		else {
			node = SetList.Alloc();
			SetList[node].QueryList = QueryList.CreateList();
		}
		SetList.LinkToHead(ActiveSetsList, node);
		return node;
	}

	public void FreeSet(ushort node) {
		SetList.Unlink(ActiveSetsList, node);
		SetList.LinkToHead(FreeSetsList, node);
		SetList[node].Serial++;
	}

	public PixelVisSet FindOrCreatePixelVisSet(in PixelVisQueryParams parms, ref int queryHandle) {
		if (queryHandle != 0) {
			ushort handle = (ushort)(queryHandle & 0xFFFF);
			handle--;
			ushort serial = (ushort)(queryHandle >> 16);
			if (SetList.IsValidIndex(handle) && SetList[handle].Serial == serial)
				return SetList[handle];
		}

		ushort node = AllocSet();
		SetList[node].Init(parms);
		uint outHandle = SetList[node].Serial;
		ushort nodeHandle = (ushort)(node + 1);
		outHandle <<= 16;
		outHandle |= nodeHandle;
		queryHandle = (int)outHandle;
		return SetList[node];
	}

	public bool SupportsOcclusion() => HwCanTestGlows;

	public void DebugInfo() => throw new NotImplementedException();
}

public struct TraceFilterGlow(IHandleEntity? passentity, CollisionGroup collisionGroup) : ITraceFilter
{
	public TraceFilterSimple Simple = new(passentity, collisionGroup);

	public bool ShouldHitEntity(IHandleEntity handleEntity, Contents contentsMask) {
		IClientUnknown unk = (IClientUnknown)handleEntity;
		ICollideable collide = unk.GetCollideable()!;
		if (collide.GetSolid() != SolidType.VPhysics && collide.GetSolid() != SolidType.BSP)
			return false;
		return Simple.ShouldHitEntity(handleEntity, contentsMask);
	}
}

public static class PixelVisibility
{
	public static readonly ConVar r_pixelvisibility_partial = new("r_pixelvisibility_partial", "1");
	public static readonly ConVar r_dopixelvisibility = new("r_dopixelvisibility", "1");
	public static readonly ConVar r_drawpixelvisibility = new("r_drawpixelvisibility", "0", 0, "Show the occlusion proxies", callback: PixelvisDrawChanged);
	public static readonly ConVar r_pixelvisibility_spew = new("r_pixelvisibility_spew", "0");
	public static readonly ConVarRef building_cubemaps = new("building_cubemaps");

	const float MIN_PROXY_PIXELS = 5.0f;

	static readonly PixelVisibilitySystem g_PixelVisibilitySystem = new();

	public static bool HasFastQueries() => true;

	public static float DrawProxy(IMatRenderContext renderContext, nint queryHandle, Vector3 origin, float scale, float proxyAspect, IMaterial material, bool screenspace) => throw new NotImplementedException();

	static void PixelvisDrawChanged(IConVar var, in ConVarChangeContext ctx) => throw new NotImplementedException();

	public static float GlowSightDistance(in Vector3 glowOrigin, bool shouldTrace) {
		float dist = (glowOrigin - CurrentViewOrigin()).Length();
		C_BasePlayer? local = C_BasePlayer.GetLocalPlayer();
		if (local != null)
			dist *= local.GetFOVDistanceAdjustFactor();

		if (shouldTrace) {
			Vector3 end = glowOrigin;
			if (dist > 4)
				end -= CurrentViewForward() * 4;
			Mask traceFlags = Mask.Opaque | (Mask)Contents.Monster | (Mask)Contents.Debris;

			TraceFilterGlow filter = new(null, CollisionGroup.None);
			Util.TraceLine(CurrentViewOrigin(), end, traceFlags, null, ref filter, out Trace tr);
			if (tr.Fraction != 1.0f)
				return -1;
		}

		return dist;
	}

	public static void EndCurrentView() => throw new NotImplementedException();

	public static void EndScene() => throw new NotImplementedException();

	public static float FractionVisible(in PixelVisQueryParams parms, ref int queryHandle) => g_PixelVisibilitySystem.GetFractionVisible(parms, ref queryHandle);

	public static bool IsAvailable() => throw new NotImplementedException();

	public static void ShiftVisibilityViews(int sourceViewID, int destViewID) => throw new NotImplementedException();
}
