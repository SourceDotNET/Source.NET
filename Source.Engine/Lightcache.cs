global using AmbientCube = Source.InlineArray6<System.Numerics.Vector3>;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Source.Engine;

public struct LightingState
{
	public AmbientCube BoxColor;
	public int NumLights;

	public InlineArrayMaxLocalLights<BSPDWorldLightPtr> LocalLight;

	public void ZeroLightingState() {
		for (int i = 0; i < 6; i++)
			BoxColor[i].Init();
		NumLights = 0;
	}

	public bool HasAmbientColors() {
		for (int i = 0; i < 6; i++)
			if (!BoxColor[i].IsZero(1e-4f))
				return true;
		return false;
	}

	public void AddLocalLight(in BSPDWorldLightPtr localLight) {
		if (NumLights >= Render.MAXLOCALLIGHTS)
			return;

		for (int i = 0; i < NumLights; i++)
			if (LocalLight[i] == localLight)
				return;

		LocalLight[NumLights] = localLight;
		NumLights++;
	}

	public void AddAllLocalLights(in LightingState src) {
		for (int i = 0; i < src.NumLights; i++)
			AddLocalLight(src.LocalLight[i]);
	}

	public void CopyLocalLights(in LightingState src) {
		NumLights = src.NumLights;
		for (int i = 0; i < src.NumLights; i++)
			LocalLight[i] = src.LocalLight[i];
	}
}
[InlineArray(Render.MAXLOCALLIGHTS)] public struct InlineArrayMaxLocalLights<T> { public T item; }

[Flags]
public enum LightCacheFlags
{
	Static = 0x1,
	Dynamic = 0x2,
	LightStyle = 0x4,
	AllowFast = 0x8,
}

public struct LightcacheGetDynamic_Stats
{
	public bool HasNonSwitchableLightStyles;
	public bool HasSwitchableLightStyles;
	public bool HasDLights;
	public bool NeedsSwitchableLightStyleUpdate;
}

[Flags]
public enum LightIntensityFlags
{
	NoOcclusionCheck = 0x1,
	NoRadiusCheck = 0x2,
	OccludeVsProps = 0x4,
	IgnoreLightstyleValue = 0x8
}

public enum HackLightCacheFlags
{
	HasSwitchableLightStyle = 0x1,
	HasNonSwitchableLightStyle = 0x2,
	HasDoneStaticLighting = 0x4
}

public class LightingStateInfo
{
	public InlineArrayMaxLocalLights<float> Illum;
	public bool LightingStateHasSkylight;

	public LightingStateInfo() {
		LightingStateHasSkylight = false;
		Clear();
	}

	public void Clear() {
		for (int i = 0; i < Render.MAXLOCALLIGHTS; i++)
			Illum[i] = 0;
		LightingStateHasSkylight = false;
	}

	public void CopyFrom(LightingStateInfo src) {
		for (int i = 0; i < Render.MAXLOCALLIGHTS; i++)
			Illum[i] = src.Illum[i];
		LightingStateHasSkylight = src.LightingStateHasSkylight;
	}
}

[InlineArray(BaseLightCache.MAX_LIGHTSTYLE_BYTES)] public struct InlineArrayLightstyleBytes { public byte item; }

public class BaseLightCache : LightingStateInfo
{
	public const int MAX_LIGHTSTYLE_BITS = BSPFileCommon.MAX_LIGHTSTYLES;
	public const int MAX_LIGHTSTYLE_BYTES = (MAX_LIGHTSTYLE_BITS + 7) / 8;

	public BaseLightCache() {
		LightingOrigin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
		LastFrameUpdatedLightStyles = -1;
		LastFrameUpdatedDynamicLighting = -1;
		LightingFlags = 0;
		Leaf = -1;
		EnvCubemapTexture = null;
	}

	public bool HasLightStyle() => (LightingFlags & (int)(HackLightCacheFlags.HasSwitchableLightStyle | HackLightCacheFlags.HasNonSwitchableLightStyle)) != 0;
	public bool HasSwitchableLightStyle() => (LightingFlags & (int)HackLightCacheFlags.HasSwitchableLightStyle) != 0;
	public bool HasNonSwitchableLightStyle() => (LightingFlags & (int)HackLightCacheFlags.HasNonSwitchableLightStyle) != 0;

	public LightingState StaticLightingState;
	public LightingState LightStyleLightingState;
	public int LastFrameUpdatedLightStyles;
	public LightingState DynamicLightingState;
	public int LastFrameUpdatedDynamicLighting;
	public int LightingFlags;
	public int Leaf;
	public InlineArrayLightstyleBytes Lightstyles;
	public Vector3 LightingOrigin;
	public ITexture? EnvCubemapTexture;
}

public class LightCache : BaseLightCache
{
	public LightCache() {
		StaticPrecalcNumLocalLights = 0;
		Next = Bucket = 0xFFFF;
		LruPrev = LruNext = 0xFFFF;
		X = Y = Z = int.MinValue;
	}

	public InlineArrayMaxLocalLights<BSPDWorldLightPtr> StaticPrecalcLocalLight;
	public ushort StaticPrecalcNumLocalLights;
	public LightingStateInfo StaticPrecalcLightingStateInfo = new();
	public ushort Next;
	public ushort Bucket;
	public ushort LruPrev;
	public ushort LruNext;
	public int X, Y, Z;
	public int Index;
}

public class PropLightcache : BaseLightCache
{
	public uint Flags;
	public int DLightActive;
	public int DLightMarkFrame;
	public List<short> LightStyleWorldLights = [];
	public int SwitchableLightFrame;
	public Vector3 Mins;
	public Vector3 Maxs;

	public PropLightcache() {
		Flags = 0;
		DLightActive = 0;
		DLightMarkFrame = 0;
		SwitchableLightFrame = -1;
		Mins = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
		Maxs = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
	}

	public bool HasDlights() => DLightActive != 0;
}

public class LightVecState
{
	public Ray Ray;
	public float HitFrac;
	public float[]? TextureS;
	public float[]? TextureT;
	public float[]? LightmapS;
	public float[]? LightmapT;
	public SurfaceHandle_t SkySurfID;
	public bool UseLightStyles;
	public List<IDispInfo> LightTestDisps = [];
}

public partial class Render
{
	public const int MAXLOCALLIGHTS = 4;
	public const int SURFACE_HANDLE_INVALID = -1;
	private static bool IS_SURF_VALID(int surfID) => surfID != SURFACE_HANDLE_INVALID;
	public static readonly ConVar r_lightcache_zbuffercache = new("r_lightcache_zbuffercache", "0", 0);
	public static readonly ConVar r_radiosity = new("r_radiosity", "4", FCvar.Cheat, "0: no radiosity\n1: radiosity with ambient cube (6 samples)\n2: radiosity with 162 samples\n3: 162 samples for static props, 6 samples for everything else");
	public static readonly ConVar r_ambientlightingonly = new("r_ambientlightingonly", "0", FCvar.Cheat, "Set this to 1 to light models with only ambient lighting (and no static lighting).");
	public static readonly ConVar r_worldlights = new("r_worldlights", "4", 0, "number of world lights to use per vertex");
	public static readonly ConVar r_avglight = new("r_avglight", "1", FCvar.Cheat);
	public static readonly ConVar r_lightcachecenter = new("r_lightcachecenter", "1", FCvar.Cheat);
	public static readonly ConVar r_lightcache_numambientsamples = new("r_lightcache_numambientsamples", "162", FCvar.Cheat, "number of random directions to fire rays when computing ambient lighting");
	public static readonly ConVar lightcache_maxmiss = new("lightcache_maxmiss", "2", FCvar.Cheat);
	public static readonly ConVar r_oldlightselection = new("r_oldlightselection", "0", FCvar.Cheat, "Set this to revert to HL2's method of selecting lights");
	public static readonly ConVar r_worldlightmin = new("r_worldlightmin", "0.0002");

	private const int MAX_CACHE_ENTRY = 200;
	private const int MAX_CACHE_BUCKETS = MAX_CACHE_ENTRY;
	private const int HASH_GRID_SIZEX = 5;
	private const int HASH_GRID_SIZEY = 5;
	private const int HASH_GRID_SIZEZ = 7;
	private const float LIGHTCACHE_SNAP_EPSILON = 0.5f;
	private const int LIGHT_LRU_HEAD_INDEX = MAX_CACHE_ENTRY;
	private const int LIGHT_LRU_TAIL_INDEX = MAX_CACHE_ENTRY + 1;

	[InlineArray(MAX_CACHE_ENTRY + 2)] private struct LightCacheArray { public LightCache Item; }
	[InlineArray(MAX_CACHE_BUCKETS)] private struct LightBucketArray { public ushort Item; }

	private static LightCacheArray LightCaches;
	private static LightBucketArray LightBuckets;

	private static int CachedRWorldLights = -1;
	private static int CachedRRadiosity = -1;
	private static int CachedRAvgLight = -1;
	private static int CachedMatFullbright = -1;
	private static int CachedRLightcacheNumAmbientSamples = -1;

	private static byte FrameMissCount = 0;
	private static int FrameIndex = 0;
	public static int r_framecount = 0;

	private static ushort GetLightCacheIndex(LightCache cache) => (ushort)cache.Index;
	private static LightCache GetLightLRUHead() => LightCaches[LIGHT_LRU_HEAD_INDEX];
	private static LightCache GetLightLRUTail() => LightCaches[LIGHT_LRU_TAIL_INDEX];

	public void R_StudioInitLightingCache() {
		int i;

		for (i = 0; i < MAX_CACHE_ENTRY + 2; i++)
			LightCaches[i] = new() { Index = i };

		for (i = 0; i < MAX_CACHE_ENTRY + 2; i++)
			LightCaches[i].Bucket = 0xFFFF;

		for (i = 0; i < MAX_CACHE_BUCKETS; i++)
			LightBuckets[i] = 0xFFFF;

		ushort last = LIGHT_LRU_HEAD_INDEX;
		for (i = 0; i < MAX_CACHE_ENTRY - 1; i++) {
			LightCaches[i].LruPrev = last;
			LightCaches[i].LruNext = (ushort)(i + 1);
			last = (ushort)i;
		}
		LightCaches[i].LruPrev = last;
		LightCaches[i].LruNext = LIGHT_LRU_TAIL_INDEX;

		LightCaches[LIGHT_LRU_HEAD_INDEX].LruNext = 0;
		LightCaches[LIGHT_LRU_TAIL_INDEX].LruPrev = (ushort)i;

		if (HardwareConfig.MaxNumLights() < r_worldlights.GetInt())
			r_worldlights.SetValue(HardwareConfig.MaxNumLights());

		CachedRWorldLights = r_worldlights.GetInt();
		CachedRRadiosity = r_radiosity.GetInt();
		CachedRAvgLight = r_avglight.GetInt();
		CachedMatFullbright = MatSysInterface.MaterialSystemConfig.Fullbright;
		CachedRLightcacheNumAmbientSamples = r_lightcache_numambientsamples.GetInt();

		InvalidateStaticLightingCache();
	}

	private static void LightcacheMark(LightCache cache) {
		if (cache.LruNext == 0 && cache.LruPrev == 0)
			return;

		if (GetLightCacheIndex(cache) == LightCaches[LIGHT_LRU_TAIL_INDEX].LruPrev)
			return;

		LightCaches[cache.LruPrev].LruNext = cache.LruNext;
		LightCaches[cache.LruNext].LruPrev = cache.LruPrev;

		LightCaches[GetLightLRUTail().LruPrev].LruNext = GetLightCacheIndex(cache);
		cache.LruPrev = GetLightLRUTail().LruPrev;

		cache.LruNext = LIGHT_LRU_TAIL_INDEX;
		GetLightLRUTail().LruPrev = GetLightCacheIndex(cache);
	}

	private static void LightcacheUnlink(LightCache cache) {
		ushort bucket = cache.Bucket;

		if (bucket == 0xFFFF)
			return;

		ushort cacheIndex = GetLightCacheIndex(cache);

		ushort list = LightBuckets[bucket];

		if (list == cacheIndex)
			LightBuckets[bucket] = cache.Next;
		else {
			bool found = false;
			while (list != 0xFFFF) {
				if (LightCaches[list].Next == cacheIndex) {
					LightCaches[list].Next = cache.Next;
					found = true;
					break;
				}
				list = LightCaches[list].Next;
			}
			Assert(found);
		}
	}

	private static LightCache LightcacheGetLRU() {
		LightCache cache = LightCaches[GetLightLRUHead().LruNext];

		LightcacheMark(cache);

		LightcacheUnlink(cache);

		cache.Leaf = -1;
		return cache;
	}

	private static int LightcacheHashKey(int x, int y, int z, int leaf) {
		uint key = (uint)(((x << 20) + (y << 8) + z) ^ leaf);
		key %= MAX_CACHE_BUCKETS;
		return (int)key;
	}

	private static LightCache? FindInCache(int bucket, int x, int y, int z, int leaf) {
		ushort cacheIndex;
		for (cacheIndex = LightBuckets[bucket]; cacheIndex != 0xFFFF; cacheIndex = LightCaches[cacheIndex].Next) {
			LightCache cache = LightCaches[cacheIndex];

			if (cache.X == x && cache.Y == y && cache.Z == z && cache.Leaf == leaf)
				return cache;
		}
		return null;
	}

	private static void LinkToBucket(int bucket, LightCache cache) {
		cache.Next = LightBuckets[bucket];
		LightBuckets[bucket] = GetLightCacheIndex(cache);

		cache.Bucket = (ushort)bucket;
	}

	private static LightCache NewLightcacheEntry(int bucket) {
		LightCache cache = LightcacheGetLRU();
		LinkToBucket(bucket, cache);
		return cache;
	}

	private static void ComputeLightcacheBounds(in Vector3 vecOrigin, out Vector3 mins, out Vector3 maxs) {
		bool bXPos = vecOrigin[0] >= 0;
		bool bYPos = vecOrigin[1] >= 0;
		bool bZPos = vecOrigin[2] >= 0;

		int ix = ((int)MathF.Abs(vecOrigin[0])) >> HASH_GRID_SIZEX;
		int iy = ((int)MathF.Abs(vecOrigin[1])) >> HASH_GRID_SIZEY;
		int iz = ((int)MathF.Abs(vecOrigin[2])) >> HASH_GRID_SIZEZ;

		mins = default;
		maxs = default;
		mins.X = (bXPos ? ix : -(ix + 1)) << HASH_GRID_SIZEX;
		mins.Y = (bYPos ? iy : -(iy + 1)) << HASH_GRID_SIZEY;
		mins.Z = (bZPos ? iz : -(iz + 1)) << HASH_GRID_SIZEZ;

		maxs.X = mins.X + (1 << HASH_GRID_SIZEX);
		maxs.Y = mins.Y + (1 << HASH_GRID_SIZEY);
		maxs.Z = mins.Z + (1 << HASH_GRID_SIZEZ);

		Assert((mins.X <= vecOrigin.X) && (mins.Y <= vecOrigin.Y) && (mins.Z <= vecOrigin.Z));
		Assert((maxs.X >= vecOrigin.X) && (maxs.Y >= vecOrigin.Y) && (maxs.Z >= vecOrigin.Z));
	}

	private static void OriginToCacheOrigin(in Vector3 origin, out int x, out int y, out int z) {
		x = ((int)origin[0] + 32768) >> HASH_GRID_SIZEX;
		y = ((int)origin[1] + 32768) >> HASH_GRID_SIZEY;
		z = ((int)origin[2] + 32768) >> HASH_GRID_SIZEZ;
	}

	private static bool AllowFullCacheMiss(LightCacheFlags flags) {
		if (r_framecount < 60 || r_framecount != FrameIndex) {
			FrameMissCount = 0;
			FrameIndex = r_framecount;
		}
		if (FrameMissCount < lightcache_maxmiss.GetInt()) {
			FrameMissCount++;
			return true;
		}

		if ((flags & LightCacheFlags.AllowFast) != 0)
			return false;

		return true;
	}

	private static LightCache? FindNearestCache(int x, int y, int z, int leafIndex) {
		int bestDist = int.MaxValue;
		LightCache? best = null;
		ushort current = GetLightLRUTail().LruPrev;
		while (current != LIGHT_LRU_HEAD_INDEX) {
			LightCache cache = LightCaches[current];
			int dist = 0;
			int dx = Math.Abs(cache.X - x);
			int dy = Math.Abs(cache.Y - y);
			int dz = Math.Abs(cache.Z - z);
			if (leafIndex != cache.Leaf)
				dist += 2;
			dist = Math.Max(dist, dx);
			dist = Math.Max(dist, dy);
			dist = Math.Max(dist, dz);
			if (dist < bestDist) {
				best = cache;
				bestDist = dist;
				if (dist <= 1)
					break;
			}
			current = cache.LruPrev;
		}
		return best;
	}

	private static void ComputeAmbientFromLeaf(in Vector3 start, int leafID, Span<Vector3> lightBoxColor, ref bool addedLeafAmbientCube) {
		if (leafID >= 0) {
			ModelLoader.Mod_LeafAmbientColorAtPos(lightBoxColor, start, leafID);
			addedLeafAmbientCube = true;
		}
		else {
			for (int i = 0; i < 6; i++)
				lightBoxColor[i].Init(0.0f, 0.0f, 0.0f);
		}
	}

	private void R_StudioGetAmbientLightForPoint(int leafID, in Vector3 start, Span<Vector3> lightBoxColor, bool isStaticProp, out bool addedLeafAmbientCube) {
		addedLeafAmbientCube = false;

		int i;
		if (MatSysInterface.MaterialSystemConfig.Fullbright == 1) {
#if !SWDS
			for (i = studiorender.GetNumAmbientLightSamples(); --i >= 0;)
				lightBoxColor[i].Init(1.0f, 1.0f, 1.0f);
#endif
			return;
		}

		switch (r_radiosity.GetInt()) {
			case 1:
				// todo
				break;
			case 2:
				ComputeAmbientFromSphericalSamples(start, lightBoxColor);
				break;
			case 3:
				if (isStaticProp)
					ComputeAmbientFromSphericalSamples(start, lightBoxColor);
				else {
					// todo
				}
				break;
			case 4:
				if (isStaticProp)
					ComputeAmbientFromSphericalSamples(start, lightBoxColor);
				else
					ComputeAmbientFromLeaf(start, leafID, lightBoxColor, ref addedLeafAmbientCube);
				break;
			default:
#if !SWDS
				for (i = studiorender.GetNumAmbientLightSamples(); --i >= 0;)
					lightBoxColor[i].Init(0.0f, 0.0f, 0.0f);
#endif
				break;
		}
	}

	private static void ComputeLightmapCoordsAtIntersection(ref BSPMSurfaceLighting lighting, float ds, float dt, float[]? lightmapS, float[]? lightmapT) {
		if (lightmapS != null && lightmapT != null) {
			if (lighting.LightmapExtents[0] != 0)
				lightmapS[0] = (ds + 0.5f) / lighting.LightmapExtents[0];
			else
				lightmapS[0] = 0.5f;

			if (lighting.LightmapExtents[1] != 0)
				lightmapT[0] = (dt + 0.5f) / lighting.LightmapExtents[1];
			else
				lightmapT[0] = 0.5f;
		}
	}

	static int messagecount = 0;
	private void ComputeLightmapColor(ref BSPMSurface2 surf, ref BSPMSurfaceLighting lighting, int ds, int dt, bool useLightStyles, ref Vector3 c) {
		Span<ColorRGBExp32> lightmap = lighting.Samples.Span;
		if (lightmap.IsEmpty) {
			if (++messagecount < 10)
				ConMsg("hit surface has no samples\n");
			return;
		}

		int smax = lighting.LightmapExtents[0] + 1;
		int tmax = lighting.LightmapExtents[1] + 1;
		int offset = smax * tmax;
		if (ModelLoader.SurfHasBumpedLightmaps(ref surf))
			offset *= Constants.NUM_BUMP_VECTS + 1;

		int index = dt * smax + ds;
		int maxMaps = useLightStyles ? BSPFileCommon.MAXLIGHTMAPS : 1;
		for (int maps = 0; maps < maxMaps && lighting.Styles[maps] != 255; ++maps) {
			float scale = LightStyleValue(lighting.Styles[maps]);

			ColorRGBExp32 sample = lightmap[index];
			c[0] += MathLib.TexLightToLinear(sample.R, sample.Exponent) * scale;
			c[1] += MathLib.TexLightToLinear(sample.G, sample.Exponent) * scale;
			c[2] += MathLib.TexLightToLinear(sample.B, sample.Exponent) * scale;

			index += offset;
		}
	}

	private void ComputeLightmapColorFromAverage(ref BSPMSurfaceLighting lighting, bool useLightStyles, ref Vector3 c) {
		int maxMaps = useLightStyles ? BSPFileCommon.MAXLIGHTMAPS : 1;
		for (int maps = 0; maps < maxMaps && lighting.Styles[maps] != 255; ++maps) {
			float scale = LightStyleValue(lighting.Styles[maps]);

			ColorRGBExp32 avgColor = lighting.AvgLightColor(maps)[0];
			c[0] += MathLib.TexLightToLinear(avgColor.R, avgColor.Exponent) * scale;
			c[1] += MathLib.TexLightToLinear(avgColor.G, avgColor.Exponent) * scale;
			c[2] += MathLib.TexLightToLinear(avgColor.B, avgColor.Exponent) * scale;
		}
	}

	private bool FindIntersectionAtSurface(int surfID, float f, ref Vector3 c, LightVecState state) {
		ref BSPMSurface2 surf = ref ModelLoader.SurfaceHandleFromIndex(surfID);

		if ((ModelLoader.MSurf_Flags(ref surf) & SurfDraw.NoLight) != 0)
			return false;

		MathLib.VectorMA(state.Ray.Start, f, state.Ray.Delta, out Vector3 pt);

		ref ModelTexInfo tex = ref ModelLoader.MSurf_TexInfo(ref surf);

		float s = MathLib.DotProduct(pt, tex.LightmapVecsLuxelsPerWorldUnits[0].AsVector3D()) + tex.LightmapVecsLuxelsPerWorldUnits[0][3];
		float t = MathLib.DotProduct(pt, tex.LightmapVecsLuxelsPerWorldUnits[1].AsVector3D()) + tex.LightmapVecsLuxelsPerWorldUnits[1][3];

		ref BSPMSurfaceLighting lighting = ref ModelLoader.SurfaceLighting(ref surf, host_state.WorldBrush!);
		if (s < lighting.LightmapMins[0] || t < lighting.LightmapMins[1])
			return false;

		float ds = s - lighting.LightmapMins[0];
		float dt = t - lighting.LightmapMins[1];
		if (lighting.LightmapExtents[0] == 0 && lighting.LightmapExtents[1] == 0) {
			WorldBrushData brushData = host_state.WorldBrush!;

			Span<float> lightMaxs = stackalloc float[2];
			lightMaxs[0] = lighting.LightmapMins[0];
			lightMaxs[1] = lighting.LightmapMins[1];
			int i;
			for (i = 0; i < ModelLoader.MSurf_VertCount(ref surf); i++) {
				int e = brushData.VertIndices![ModelLoader.MSurf_FirstVertIndex(ref surf) + i];
				ref BSPDertex v = ref brushData.Vertexes![e];

				int j;
				for (j = 0; j < 2; j++) {
					float sextent = MathLib.DotProduct(v.Position, tex.LightmapVecsLuxelsPerWorldUnits[0].AsVector3D()) + tex.LightmapVecsLuxelsPerWorldUnits[0][3] - lighting.LightmapMins[0];
					float textent = MathLib.DotProduct(v.Position, tex.LightmapVecsLuxelsPerWorldUnits[1].AsVector3D()) + tex.LightmapVecsLuxelsPerWorldUnits[1][3] - lighting.LightmapMins[1];

					if (sextent > lightMaxs[0])
						lightMaxs[0] = sextent;
					if (textent > lightMaxs[1])
						lightMaxs[1] = textent;
				}
			}
			if (ds > lightMaxs[0] || dt > lightMaxs[1])
				return false;
		}
		else {
			if (ds > lighting.LightmapExtents[0] || dt > lighting.LightmapExtents[1])
				return false;
		}

		state.HitFrac = f;

		ComputeTextureCoordsAtIntersection(tex, pt, state.TextureS, state.TextureT);

		if (r_avglight.GetInt() != 0)
			ComputeLightmapColorFromAverage(ref lighting, state.UseLightStyles, ref c);
		else {
			ComputeLightmapCoordsAtIntersection(ref lighting, ds, dt, state.LightmapS, state.LightmapT);
			ComputeLightmapColor(ref surf, ref lighting, (int)ds, (int)dt, state.UseLightStyles, ref c);
		}

		return true;
	}

	private void ComputeTextureCoordsAtIntersection(ModelTexInfo tex, Vector3 pt, float[]? textureS, float[]? textureT) {
		// TODO!
		// TODO!
	}

	private int FindIntersectionSurfaceAtNode(BSPMNode node, float t, ref Vector3 c, LightVecState state) {
		int surfID = node.FirstSurface;
		for (int i = 0; i < node.NumSurfaces; ++i, ++surfID) {
			ref BSPMSurface2 surf = ref ModelLoader.SurfaceHandleFromIndex(surfID);

			if ((ModelLoader.MSurf_Flags(ref surf) & SurfDraw.Sky) != 0) {
				state.SkySurfID = surfID;
				continue;
			}

			if ((ModelLoader.MSurf_Flags(ref surf) & SurfDraw.WaterSurface) != 0)
				continue;

			if (FindIntersectionAtSurface(surfID, t, ref c, state))
				return surfID;
		}

		return SURFACE_HANDLE_INVALID;
	}

	private int R_LightVecDisplacementChain(LightVecState state, bool useLightStyles, ref Vector3 c) {
		SurfaceHandle_t surfID = SURFACE_HANDLE_INVALID;

		// TODO!

		return surfID;
	}

	private void AddDisplacementsInLeafToTestList(BSPMLeaf leaf, LightVecState state) {
		for (int i = 0; i < leaf.DispCount; i++) {
			IDispInfo dispInfo = DispInfo.MLeaf_Disaplcement(leaf, i)!;
			BSPMSurface2 parentSurfID = dispInfo.GetParent();

			if (ModelLoader.MSurf_VisFrame(ref parentSurfID) != r_surfacevisframe) {
				ModelLoader.MSurf_VisFrame(ref parentSurfID) = r_surfacevisframe;
				state.LightTestDisps.Add(dispInfo);
			}
		}
	}

	private int FindIntersectionSurfaceAtLeaf(BSPMLeaf leaf, float start, float end, ref Vector3 c, LightVecState state) {
		SurfaceHandle_t closestSurfID = SURFACE_HANDLE_INVALID;

		AddDisplacementsInLeafToTestList(leaf, state);

		WorldBrushData brushData = host_state.WorldBrush!;
		for (int i = leaf.NumMarkNodeSurfaces; i < leaf.NumMarkSurfaces; i++) {
			SurfaceHandle_t surfID = brushData.MarkSurfaces![leaf.FirstMarkSurface + i];
			ref BSPMSurface2 surf = ref ModelLoader.SurfaceHandleFromIndex(surfID);

			if (ModelLoader.SurfaceHasDispInfo(ref surf))
				continue;
			Assert((ModelLoader.MSurf_Flags(ref surf) & SurfDraw.Node) == 0);

			if ((ModelLoader.MSurf_Flags(ref surf) & (SurfDraw.Node | SurfDraw.NoDraw | SurfDraw.WaterSurface)) != 0)
				continue;

			ref CollisionPlane plane = ref ModelLoader.MSurf_Plane(ref surf);

			if (MathLib.DotProduct(plane.Normal, state.Ray.Delta) > 0.0f)
				continue;

			float startDotN = MathLib.DotProduct(state.Ray.Start, plane.Normal);
			float deltaDotN = MathLib.DotProduct(state.Ray.Delta, plane.Normal);

			float front = startDotN + start * deltaDotN - plane.Dist;
			float back = startDotN + end * deltaDotN - plane.Dist;

			int side = front < 0.0f ? 1 : 0;

			if ((back < 0.0f ? 1 : 0) == side)
				continue;

			float frac = front / (front - back);
			if (frac >= state.HitFrac)
				continue;

			float mid = start * (1.0f - frac) + end * frac;

			if (FindIntersectionAtSurface(surfID, mid, ref c, state))
				closestSurfID = surfID;
		}

		return closestSurfID;
	}

	private int RecursiveLightPoint(BSPMNode node, float start, float end, ref Vector3 c, LightVecState state) {
		if (node.Contents >= 0)
			return FindIntersectionSurfaceAtLeaf((BSPMLeaf)node, start, end, ref c, state);

		ref CollisionPlane plane = ref node.Plane;

		float startDotN = MathLib.DotProduct(state.Ray.Start, plane.Normal);
		float deltaDotN = MathLib.DotProduct(state.Ray.Delta, plane.Normal);

		float front = startDotN + start * deltaDotN - plane.Dist;
		float back = startDotN + end * deltaDotN - plane.Dist;
		int side = front < 0 ? 1 : 0;

		int surfID;
		if ((back < 0 ? 1 : 0) == side) {
			surfID = RecursiveLightPoint(node.Children[side]!, start, end, ref c, state);
			return surfID;
		}

		float frac = front / (front - back);
		float mid = start * (1.0f - frac) + end * frac;

		surfID = RecursiveLightPoint(node.Children[side]!, start, mid, ref c, state);
		if (IS_SURF_VALID(surfID))
			return surfID;

		surfID = FindIntersectionSurfaceAtNode(node, mid, ref c, state);
		if (IS_SURF_VALID(surfID))
			return surfID;

		surfID = RecursiveLightPoint(node.Children[side == 0 ? 1 : 0]!, mid, end, ref c, state);
		return surfID;
	}

	private int R_LightVec(in Vector3 start, in Vector3 end, bool useLightStyles, out Vector3 c) {
		int retSurfID;
		int dispSurfID;

		++r_surfacevisframe;

		LightVecState state = new();
		state.HitFrac = 1.0f;
		state.Ray.Init(start, end);
		state.SkySurfID = SURFACE_HANDLE_INVALID;
		state.UseLightStyles = useLightStyles;

		c = new(0.0f, 0.0f, 0.0f);

		Model model = host_state.WorldModel!; // toto LightVecModel
		retSurfID = RecursiveLightPoint(host_state.WorldBrush!.Nodes![model.Brush.FirstNode], 0.0f, 1.0f, ref c, state);

		dispSurfID = R_LightVecDisplacementChain(state, useLightStyles, ref c);

		//r_visualizelighttraces

		if (IS_SURF_VALID(dispSurfID))
			retSurfID = dispSurfID;

		if (retSurfID == SURFACE_HANDLE_INVALID && state.SkySurfID != SURFACE_HANDLE_INVALID)
			return state.SkySurfID;

		return retSurfID;
	}

	private void ComputeAmbientFromSurface(int surfID, BSPDWorldLight? skylight, ref Vector3 radcolor) {
		if (IS_SURF_VALID(surfID)) {
			ref BSPMSurface2 surf = ref ModelLoader.SurfaceHandleFromIndex(surfID);
			if ((ModelLoader.MSurf_Flags(ref surf) & SurfDraw.Sky) != 0) {
				if (skylight != null)
					radcolor = skylight.Value.Intensity;
			}
			else {
				ModelLoader.MSurf_TexInfo(ref surf).Material!.GetReflectivity(out Vector3 reflectivity);
				MathLib.VectorMultiply(radcolor, reflectivity, out radcolor);
			}
		}
	}

	private void ComputeAmbientFromSphericalSamples(in Vector3 start, Span<Vector3> lightBoxColor) {
		BSPDWorldLight? skylight = FindAmbientLight();

		Span<Vector3> radcolor = stackalloc Vector3[MathLib.Anorms.Length];
		Assert(CachedRLightcacheNumAmbientSamples <= radcolor.Length);

		int i;
		for (i = 0; i < CachedRLightcacheNumAmbientSamples; i++) {
			MathLib.VectorMA(start, COORD_EXTENT * 1.74f, MathLib.Anorms[i], out Vector3 upend);

			int surfID = R_LightVec(start, upend, false, out radcolor[i]);
			if (!IS_SURF_VALID(surfID))
				continue;

			ComputeAmbientFromSurface(surfID, skylight, ref radcolor[i]);
		}

#if !SWDS
		ReadOnlySpan<Vector3> boxDirs = studiorender.GetAmbientLightDirections();
		for (int j = studiorender.GetNumAmbientLightSamples(); --j >= 0;) {
			float c, t = 0;

			lightBoxColor[j][0] = 0;
			lightBoxColor[j][1] = 0;
			lightBoxColor[j][2] = 0;

			for (i = 0; i < CachedRLightcacheNumAmbientSamples; i++) {
				c = MathLib.DotProduct(MathLib.Anorms[i], boxDirs[j]);
				if (c > 0) {
					t += c;
					MathLib.VectorMA(lightBoxColor[j], c, radcolor[i], out lightBoxColor[j]);
				}
			}

			MathLib.VectorMultiply(lightBoxColor[j], 1 / t, out lightBoxColor[j]);
		}
#endif
	}

	private static bool IsCachedLightStylesValid(BaseLightCache cache) {
		if (!cache.HasLightStyle())
			return true;

		for (int i = 1; i < BSPFileCommon.MAX_LIGHTSTYLES; i++) {
			int byt = i >> 3;
			int bit = i & 0x7;
			if ((cache.Lightstyles[byt] & (1 << bit)) != 0)
				return false;
		}

		return true;
	}

	private void AdjustLightCacheOrigin(LightCache cache, in Vector3 origin, int originLeaf) {
		Trace tr;
		Ray ray = default;
		TraceFilterWorldOnly worldTraceFilter = new();

		tr.StartSolid = false;
		tr.Fraction = 0;

		ComputeLightcacheBounds(origin, out Vector3 cacheMins, out Vector3 cacheMaxs);
		MathLib.VectorAdd(cacheMins, cacheMaxs, out Vector3 center);
		MathLib.VectorScale(center, 0.5f, out center);

		bool traceToCenter = true;
		int centerLeaf = CM.PointLeafnum(center);
		if (centerLeaf != originLeaf) {
			if (((uint)CM.LeafContents(centerLeaf) & (uint)Mask.Opaque) != 0)
				traceToCenter = false;
			else
				CM.SnapPointToReferenceLeaf(origin, LIGHTCACHE_SNAP_EPSILON, ref center);
		}

		if (traceToCenter) {
			ray.Init(origin, center);
			engineTrace.TraceRay(in ray, Mask.Opaque, ref worldTraceFilter, out tr);
		}

		if (traceToCenter && tr.StartSolid)
			MathLib.VectorCopy(origin, out cache.LightingOrigin);
		else if (!traceToCenter || tr.Fraction < 1) {
			center.X = (cacheMins.X + cacheMaxs.X) * 0.5f;
			center.Y = (cacheMins.Y + cacheMaxs.Y) * 0.5f;
			center.Z = origin.Z;
			CM.SnapPointToReferenceLeaf(origin, LIGHTCACHE_SNAP_EPSILON, ref center);

			ray.Init(origin, center);
			engineTrace.TraceRay(in ray, Mask.Opaque, ref worldTraceFilter, out tr);
			if (tr.Fraction < 1)
				MathLib.VectorCopy(origin, out cache.LightingOrigin);
			else
				MathLib.VectorCopy(center, out cache.LightingOrigin);
		}
		else {
			MathLib.VectorCopy(center, out cache.LightingOrigin);
		}
	}

	private ITexture? FindEnvCubemapForPoint(in Vector3 origin) {
		WorldBrushData? brushData = host_state.WorldBrush;
		if (brushData != null && brushData.NumCubemapSamples > 0) {
			int smallestIndex = 0;
			Vector3 delta = origin - brushData.CubemapSamples![0].Origin;
			float smallestDist = MathLib.DotProduct(delta, delta);
			for (int i = 1; i < brushData.NumCubemapSamples; i++) {
				delta = origin - brushData.CubemapSamples![i].Origin;
				float dist = MathLib.DotProduct(delta, delta);
				if (dist < smallestDist) {
					smallestDist = dist;
					smallestIndex = i;
				}
			}

			return brushData.CubemapSamples![smallestIndex].Texture;
		}
		else
			return null;
	}

	private ReadOnlySpan<byte> ComputeStaticLightingForCacheEntry(BaseLightCache cache, scoped in Vector3 origin, int leaf, bool staticProp = false) {
		ReadOnlySpan<byte> vis = CM.ClusterPVS(CM.LeafCluster(leaf));

		R_StudioGetAmbientLightForPoint(leaf, origin, cache.StaticLightingState.BoxColor, staticProp, out bool addedLeafAmbientCube);

		if (r_ambientlightingonly.GetInt() == 0)
			AddStaticLighting(cache, origin, vis, staticProp, addedLeafAmbientCube);

		return vis;
	}

	private ReadOnlySpan<byte> PrecalcLightingState(LightCache cache, ReadOnlySpan<byte> vis) {
		LightingState lightingState = default;
		lightingState.ZeroLightingState();

		cache.StaticPrecalcLightingStateInfo.Clear();

		int i;
		for (i = 0; i < cache.StaticLightingState.NumLights; i++)
			vis = AddWorldLightToLightingState(cache.StaticLightingState.LocalLight[i], null, ref lightingState, cache.StaticPrecalcLightingStateInfo, cache.LightingOrigin, vis, true, false);

		for (i = 0; i < 6; i++)
			cache.StaticLightingState.BoxColor[i] += lightingState.BoxColor[i];

		cache.StaticPrecalcNumLocalLights = (ushort)lightingState.NumLights;
		for (i = 0; i < cache.StaticPrecalcNumLocalLights; i++)
			cache.StaticPrecalcLocalLight[i] = lightingState.LocalLight[i];

		return vis;
	}

	private static void CopyPrecalcedLightingState(LightCache cache, ref LightingState lightingState, LightingStateInfo info) {
		info.CopyFrom(cache.StaticPrecalcLightingStateInfo);

		int i;
		for (i = 0; i < 6; i++)
			lightingState.BoxColor[i] = cache.StaticLightingState.BoxColor[i];

		lightingState.NumLights = cache.StaticPrecalcNumLocalLights;
		for (i = 0; i < lightingState.NumLights; i++)
			lightingState.LocalLight[i] = cache.StaticPrecalcLocalLight[i];
	}

	private ReadOnlySpan<byte> AddLightingState(scoped ref LightingState dst, scoped in LightingState src, LightingStateInfo info, scoped in Vector3 bucketOrigin, ReadOnlySpan<byte> vis, bool dynamic, bool ignoreVis) {
		int i;
		for (i = 0; i < src.NumLights; i++)
			vis = AddWorldLightToLightingState(src.LocalLight[i], null, ref dst, info, bucketOrigin, vis, dynamic, ignoreVis);

		for (i = 0; i < 6; i++)
			dst.BoxColor[i] += src.BoxColor[i];

		return vis;
	}

	private static readonly Vector3 s_Grayscale = new(0.299f, 0.587f, 0.114f);

	private static int FindDarkestWorldLight(int numLights, InlineArrayMaxLocalLights<float> lightIllum, float newIllum) {
		int minLightIndex = -1;
		float minillum = newIllum;
		for (int j = 0; j < numLights; ++j) {
			if (lightIllum[j] < minillum) {
				minillum = lightIllum[j];
				minLightIndex = j;
			}
		}

		return minLightIndex;
	}

	private static void AddWorldLightToLightCube(in BSPDWorldLight worldLight, Span<Vector3> boxColor, in Vector3 direction, float ratio) {
		if (ratio == 0.0f)
			return;

#if !SWDS
		ReadOnlySpan<Vector3> boxDir = studiorender.GetAmbientLightDirections();

		for (int j = studiorender.GetNumAmbientLightSamples(); --j >= 0;) {
			float t = MathLib.DotProduct(boxDir[j], direction);
			if (t > 0)
				MathLib.VectorMA(boxColor[j], ratio * t, worldLight.Intensity, out boxColor[j]);
		}
#endif
	}

	private static void WorldLightFromDynamicLight(DLight dynamicLight, ref BSPDWorldLight worldLight) {
		MathLib.VectorCopy(dynamicLight.Origin, out worldLight.Origin);
		worldLight.Type = EmitType.Point;

		worldLight.Intensity[0] = MathLib.TexLightToLinear(dynamicLight.Color.R, dynamicLight.Color.Exponent);
		worldLight.Intensity[1] = MathLib.TexLightToLinear(dynamicLight.Color.G, dynamicLight.Color.Exponent);
		worldLight.Intensity[2] = MathLib.TexLightToLinear(dynamicLight.Color.B, dynamicLight.Color.Exponent);
		worldLight.Style = dynamicLight.Style;

		worldLight.Cluster = CM.LeafCluster(CM.PointLeafnum(worldLight.Origin));

		float minlight = MathF.Max(dynamicLight.MinLight, MinLightingValue);
		float radius = dynamicLight.GetRadius();
		if (radius < 0.1f)
			radius = 0.1f;

		worldLight.ConstantAttn = 0;
		worldLight.LinearAttn = 0;
		worldLight.QuadraticAttn = 1.0f / (minlight * radius * radius);

		worldLight.Radius = radius;

		if (dynamicLight.OuterAngle > 0.0f) {
			worldLight.Type = EmitType.SpotLight;
			MathLib.VectorCopy(dynamicLight.Direction, out worldLight.Normal);
			worldLight.StopDot = MathF.Cos(dynamicLight.InnerAngle * MathF.PI / 180.0f);
			worldLight.StopDot2 = MathF.Cos(dynamicLight.OuterAngle * MathF.PI / 180.0f);
		}
	}

	static readonly BSPDWorldLight[] s_pDynamicLight = new BSPDWorldLight[Constants.MAX_DLIGHTS + Constants.MAX_ELIGHTS];

	private ReadOnlySpan<byte> AddDLights(LightingStateInfo info, scoped ref LightingState lightingState, scoped in Vector3 origin, int leaf, ReadOnlySpan<byte> vis) {
		if (!CL.ActiveDlights)
			return vis;

		const bool ignoreVis = false;
		const bool ignoreVisTest = true;

		for (int i = 0; i < Constants.MAX_DLIGHTS; ++i) {
			DLight dl = CL.DLights[i];

			if ((Render.DLightActive & (1 << i)) == 0)
				continue;

			if ((dl.Flags & (DLightFlags.NoModelIllumination | DLightFlags.DisplacementMask)) != 0)
				continue;

			int lightCluster = CM.LeafCluster(CM.PointLeafnum(dl.Origin));
			vis = FastRejectLightSource(ignoreVis, vis, origin, EmitType.Point, lightCluster, out bool reject);
			if (reject)
				continue;

			WorldLightFromDynamicLight(dl, ref s_pDynamicLight[i]);

			vis = AddWorldLightToLightingState(new BSPDWorldLightPtr(s_pDynamicLight, i), null, ref lightingState, info, origin, vis, true, ignoreVis, ignoreVisTest);
		}

		return vis;
	}

	private ReadOnlySpan<byte> AddELights(LightingStateInfo info, scoped ref LightingState lightingState, scoped in Vector3 origin, int leaf, ReadOnlySpan<byte> vis) {
		if (!CL.ActiveElights)
			return vis;

		const bool ignoreVis = false;
		const bool ignoreVisTest = true;

		for (int i = 0; i < Constants.MAX_ELIGHTS; ++i) {
			DLight dl = CL.ELights[i];

			if (!dl.IsRadiusGreaterThanZero())
				continue;

			if ((dl.Flags & (DLightFlags.NoModelIllumination | DLightFlags.DisplacementMask)) != 0)
				continue;

			int lightCluster = CM.LeafCluster(CM.PointLeafnum(dl.Origin));
			vis = FastRejectLightSource(ignoreVis, vis, origin, EmitType.Point, lightCluster, out bool reject);
			if (reject)
				continue;

			WorldLightFromDynamicLight(dl, ref s_pDynamicLight[Constants.MAX_DLIGHTS + i]);

			vis = AddWorldLightToLightingState(new BSPDWorldLightPtr(s_pDynamicLight, Constants.MAX_DLIGHTS + i), null, ref lightingState, info, origin, vis, true, ignoreVis, ignoreVisTest);
		}

		return vis;
	}

	private ReadOnlySpan<byte> FastRejectLightSource(bool ignoreVis, ReadOnlySpan<byte> vis, scoped in Vector3 bucketOrigin, EmitType lightType, int lightCluster, out bool reject) {
		reject = false;
		if (!ignoreVis) {
			if (vis.IsEmpty) {
				int bucketOriginLeaf = CM.PointLeafnum(bucketOrigin);
				vis = CM.ClusterPVS(CM.LeafCluster(bucketOriginLeaf));
			}
			if (lightType == EmitType.SkyLight) {
				int bucketOriginLeaf = CM.PointLeafnum(bucketOrigin);
				int flags = CM.LeafFlags(bucketOriginLeaf);
				if ((flags & (BSPFileCommon.LEAF_FLAGS_SKY | BSPFileCommon.LEAF_FLAGS_SKY2D)) == 0)
					reject = true;
			}
			else {
				if (lightCluster < 0 || (vis[lightCluster >> 3] & (1 << (lightCluster & 7))) == 0)
					reject = true;
			}
		}
		return vis;
	}

	private ReadOnlySpan<byte> AddWorldLightToLightingState(scoped in BSPDWorldLightPtr light, LightZBuffer[]? zBuf, scoped ref LightingState lightingState, LightingStateInfo info, scoped in Vector3 bucketOrigin, ReadOnlySpan<byte> vis, bool dynamic = false, bool ignoreVis = false, bool ignoreVisTest = false) {
		Assert(lightingState.NumLights >= 0 && lightingState.NumLights <= MAXLOCALLIGHTS);

		BSPDWorldLightPtr worldLightPtr = light;
		ref BSPDWorldLight worldLight = ref worldLightPtr.Dereference();

		if (worldLight.Type == EmitType.SkyLight && info.LightingStateHasSkylight)
			return vis;

		if (!ignoreVisTest) {
			vis = FastRejectLightSource(ignoreVis, vis, bucketOrigin, worldLight.Type, worldLight.Cluster, out bool reject);
			if (reject)
				return vis;
		}

		Vector3 direction;

		float ratio;

		if (!dynamic && r_oldlightselection.GetBool())
			ratio = LightIntensityAndDirectionAtPoint(ref worldLight, zBuf, bucketOrigin, 0, null, out direction);
		else {
			ComputeLightcacheBounds(bucketOrigin, out Vector3 mins, out Vector3 maxs);
			ratio = LightIntensityAndDirectionInBox(ref worldLight, zBuf, bucketOrigin, mins, maxs, dynamic ? LightIntensityFlags.NoOcclusionCheck : 0, out direction);
		}

		if (ratio <= 0.0f)
			return vis;

		if (worldLight.Type == EmitType.SkyLight)
			info.LightingStateHasSkylight = true;

		float angularRatio = Engine_WorldLightAngle(in worldLight, worldLight.Normal, direction, direction);

		float illum = ratio * MathLib.DotProduct(worldLight.Intensity, s_Grayscale);

		if (worldLight.Type == EmitType.Surface || illum >= r_worldlightmin.GetFloat()) {
			int worldLights = Math.Min(HardwareConfig.MaxNumLights(), r_worldlights.GetInt());

			if (lightingState.NumLights < worldLights) {
				lightingState.LocalLight[lightingState.NumLights] = light;
				info.Illum[lightingState.NumLights] = illum;
				++lightingState.NumLights;
				return vis;
			}

			int minLightIndex = FindDarkestWorldLight(lightingState.NumLights, info.Illum, dynamic ? 100000 : illum);
			if (minLightIndex != -1) {
				(lightingState.LocalLight[minLightIndex], worldLightPtr) = (worldLightPtr, lightingState.LocalLight[minLightIndex]);
				worldLight = ref worldLightPtr.Dereference();
				(info.Illum[minLightIndex], illum) = (illum, info.Illum[minLightIndex]);
				ratio = illum / MathLib.DotProduct(worldLight.Intensity, s_Grayscale);

				if (worldLight.Type == EmitType.SkyLight) {
					MathLib.VectorClear(out direction);
					angularRatio = 1.0f;
				}
				else {
					MathLib.VectorSubtract(worldLight.Origin, bucketOrigin, out direction);
					MathLib.VectorNormalize(ref direction);

					angularRatio = Engine_WorldLightAngle(in worldLight, worldLight.Normal, direction, direction);
				}
			}
		}

		AddWorldLightToLightCube(in worldLight, lightingState.BoxColor, direction, ratio * angularRatio);
		return vis;
	}

	private void AddStaticLighting(BaseLightCache cache, in Vector3 origin, ReadOnlySpan<byte> vis, bool staticProp, bool addedLeafAmbientCube) {
		int i;
		cache.StaticLightingState.NumLights = 0;
		cache.LightingStateHasSkylight = false;

		if (!staticProp) {
			cache.LightingFlags &= ~(int)(HackLightCacheFlags.HasSwitchableLightStyle | HackLightCacheFlags.HasNonSwitchableLightStyle);
			for (i = 0; i < BaseLightCache.MAX_LIGHTSTYLE_BYTES; i++)
				cache.Lightstyles[i] = 0;
		}

		WorldBrushData worldBrush = host_state.WorldBrush!;
		for (i = 0; i < worldBrush.NumWorldLights; ++i) {
			BSPDWorldLightPtr wl = new(worldBrush.WorldLights!, i);
			ref BSPDWorldLight worldLight = ref wl.Dereference();
			LightZBuffer[]? zBuf;
			if (r_lightcache_zbuffercache.GetInt() != 0)
				zBuf = worldBrush.ShadowZBuffers;
			else
				zBuf = null;

			if (addedLeafAmbientCube && (worldLight.Flags & DWorldLightFlags.InAmbientCube) != 0) {
				Assert(worldLight.Type == EmitType.Surface);
				continue;
			}

			if (worldLight.Style == 0)
				AddWorldLightToLightingState(wl, zBuf, ref cache.StaticLightingState, cache, origin, vis, false, false);
			else {
				if (!staticProp) {
					int b = worldLight.Style >> 3;
					int bit = worldLight.Style & 0x7;
					if ((cache.Lightstyles[b] & (1 << bit)) == 0) {
						ComputeLightcacheBounds(origin, out Vector3 mins, out Vector3 maxs);
						float ratio = LightIntensityAndDirectionInBox(ref worldLight, null, origin, mins, maxs,
							LightIntensityFlags.NoOcclusionCheck | LightIntensityFlags.IgnoreLightstyleValue, out _);
						if (ratio > 0.0f) {
							if (MatSysInterface.LightStyleNumFrames[worldLight.Style] <= 1)
								cache.LightingFlags |= (int)HackLightCacheFlags.HasSwitchableLightStyle;
							else
								cache.LightingFlags |= (int)HackLightCacheFlags.HasNonSwitchableLightStyle;
							cache.Lightstyles[b] |= (byte)(1 << bit);
						}
					}
				}
			}
		}
	}

	private ReadOnlySpan<byte> ComputeLightStyles(LightCache cache, scoped ref LightingState lightingState, scoped in Vector3 origin, int leaf, ReadOnlySpan<byte> vis) {
		LightingStateInfo info = new();

		lightingState.ZeroLightingState();
		for (int i = 0; i < host_state.WorldBrush!.NumWorldLights; ++i) {
			BSPDWorldLightPtr wl = new(host_state.WorldBrush!.WorldLights!, i);
			ref BSPDWorldLight worldLight = ref wl.Dereference();
			if (worldLight.Style == 0)
				continue;

			int b = worldLight.Style >> 3;
			int bit = worldLight.Style & 0x7;
			if ((cache.Lightstyles[b] & (1 << bit)) == 0)
				continue;

			if (vis.IsEmpty)
				vis = CM.ClusterPVS(CM.LeafCluster(leaf));

			AddWorldLightToLightingState(wl, null, ref lightingState, info, origin, vis);
		}
		cache.LastFrameUpdatedLightStyles = r_framecount;
		return vis;
	}

	private ReadOnlySpan<byte> ComputeDynamicLighting(LightCache cache, scoped ref LightingState lightingState, scoped in Vector3 lightingOrigin, int leaf, ReadOnlySpan<byte> vis) {
		if (cache.LastFrameUpdatedDynamicLighting != r_framecount) {
			LightingStateInfo info = new();

			cache.DynamicLightingState.ZeroLightingState();

			vis = AddDLights(info, ref cache.DynamicLightingState, lightingOrigin, leaf, vis);
			vis = AddELights(info, ref cache.DynamicLightingState, lightingOrigin, leaf, vis);

			cache.LastFrameUpdatedDynamicLighting = r_framecount;
		}

		Assert(cache.DynamicLightingState.NumLights >= 0 && cache.DynamicLightingState.NumLights <= MAXLOCALLIGHTS);
		lightingState = cache.DynamicLightingState;
		return vis;
	}

	public void StudioCheckReinitLightingCache() {
		if (HardwareConfig.MaxNumLights() < r_worldlights.GetInt())
			r_worldlights.SetValue(HardwareConfig.MaxNumLights());

		if (CachedRWorldLights != r_worldlights.GetInt() ||
			CachedRRadiosity != r_radiosity.GetInt() ||
			CachedRAvgLight != r_avglight.GetInt() ||
			CachedMatFullbright != MatSysInterface.MaterialSystemConfig.Fullbright ||
			CachedRLightcacheNumAmbientSamples != r_lightcache_numambientsamples.GetInt()) {
			R_StudioInitLightingCache();
		}
	}
	private readonly List<PropLightcache> PropCaches = [];

	public ref LightingState LightcacheGetStatic(LightCacheHandle_t cache, out ITexture? envCubemap, LightCacheFlags flags = LightCacheFlags.Static | LightCacheFlags.Dynamic | LightCacheFlags.LightStyle) {
		PropLightcache pcache = PropCaches[(int)cache - 1];

		envCubemap = pcache.EnvCubemapTexture;

		bool recalcStaticLighting = false;
		bool recalcLightStyles = pcache.HasLightStyle() && pcache.LastFrameUpdatedLightStyles != r_framecount && !IsCachedLightStylesValid(pcache);
		bool recalcDLights = pcache.HasDlights() && pcache.LastFrameUpdatedDynamicLighting != r_framecount;

		if (flags != (LightCacheFlags)pcache.Flags) {
			recalcStaticLighting = true;
			recalcLightStyles = true;
			recalcDLights = true;

			pcache.Flags = (uint)flags;
		}
		else if (!recalcDLights && !recalcLightStyles)
			return ref pcache.DynamicLightingState;
		else {
			if (recalcLightStyles)
				recalcDLights = true;
		}

		LightingState accumulatedState = default;

		if ((flags & LightCacheFlags.Static) != 0) {
			if (recalcStaticLighting && (pcache.LightingFlags & (int)HackLightCacheFlags.HasDoneStaticLighting) == 0) {
				ComputeStaticLightingForCacheEntry(pcache, pcache.LightingOrigin, pcache.Leaf, true);
				pcache.LightingFlags |= (int)HackLightCacheFlags.HasDoneStaticLighting;
			}

			accumulatedState = pcache.StaticLightingState;
		}
		else
			accumulatedState.ZeroLightingState();

		if ((flags & LightCacheFlags.LightStyle) != 0) {
			if (recalcLightStyles) {
				AddLightStylesForStaticProp(pcache, ref accumulatedState);
				pcache.LightStyleLightingState = accumulatedState;
				pcache.LastFrameUpdatedLightStyles = r_framecount;
			}
			else
				accumulatedState = pcache.LightStyleLightingState;
		}

		if ((flags & LightCacheFlags.Dynamic) != 0) {
			if (recalcDLights) {
				AddDLightsForStaticProps(pcache, ref accumulatedState, pcache);
				pcache.DynamicLightingState = accumulatedState;
				pcache.LastFrameUpdatedDynamicLighting = r_framecount;
			}
			else
				accumulatedState = pcache.DynamicLightingState;
		}
		else
			pcache.DynamicLightingState = accumulatedState;

		return ref pcache.DynamicLightingState;
	}

	private void AddLightStylesForStaticProp(PropLightcache pcache, ref LightingState lightingState) {
		for (int i = 0; i < pcache.LightStyleWorldLights.Count; ++i) {
			Assert(pcache.LightStyleWorldLights[i] >= 0);
			Assert(pcache.LightStyleWorldLights[i] < host_state.WorldBrush!.NumWorldLights);
			BSPDWorldLightPtr wl = new(host_state.WorldBrush!.WorldLights!, pcache.LightStyleWorldLights[i]);
			Assert(wl.Dereference().Style != 0);

			AddWorldLightToLightingState(wl, null, ref lightingState, pcache, pcache.LightingOrigin, default, false, true);
		}
	}

	private void AddDLightsForStaticProps(LightingStateInfo info, ref LightingState lightingState, PropLightcache pcache) {
		// todo
	}

	public ITexture? LightcacheGetDynamic(in Vector3 origin, ref LightingState lightingState, ref LightcacheGetDynamic_Stats stats, LightCacheFlags flags = (LightCacheFlags.Static | LightCacheFlags.Dynamic | LightCacheFlags.LightStyle), bool debugModel = false) {
		LightingStateInfo info = new();

		int originLeaf = CM.PointLeafnum(origin);

		OriginToCacheOrigin(origin, out int x, out int y, out int z);

		int bucket = LightcacheHashKey(x, y, z, originLeaf);

		ReadOnlySpan<byte> vis = default;
		bool computeLightStyles = (flags & LightCacheFlags.LightStyle) != 0;

		LightCache? cache = FindInCache(bucket, x, y, z, originLeaf);

		if (cache != null) {
			LightcacheMark(cache);

			if (computeLightStyles && IsCachedLightStylesValid(cache))
				computeLightStyles = false;
		}
		else if (!AllowFullCacheMiss(flags)) {
			cache = FindNearestCache(x, y, z, originLeaf);
			originLeaf = cache!.Leaf;

			x = cache.X;
			y = cache.Y;
			z = cache.Z;
		}
		if (cache == null) {
			cache = NewLightcacheEntry(bucket);

			cache.X = x;
			cache.Y = y;
			cache.Z = z;
			cache.Leaf = originLeaf;

			if (r_lightcachecenter.GetBool())
				AdjustLightCacheOrigin(cache, origin, originLeaf);
			else
				cache.LightingOrigin = origin;

			cache.EnvCubemapTexture = FindEnvCubemapForPoint(cache.LightingOrigin);

			vis = ComputeStaticLightingForCacheEntry(cache, cache.LightingOrigin, originLeaf);
			vis = PrecalcLightingState(cache, vis);
		}

		stats.HasNonSwitchableLightStyles = cache.HasNonSwitchableLightStyle();
		stats.HasSwitchableLightStyles = cache.HasSwitchableLightStyle();

		if (computeLightStyles) {
			vis = ComputeLightStyles(cache, ref cache.LightStyleLightingState, cache.LightingOrigin, originLeaf, vis);
			stats.NeedsSwitchableLightStyleUpdate = true;
		}
		else
			stats.NeedsSwitchableLightStyleUpdate = false;

		stats.HasDLights = false;
		if ((flags & LightCacheFlags.Dynamic) != 0) {
			vis = ComputeDynamicLighting(cache, ref cache.DynamicLightingState, cache.LightingOrigin, originLeaf, vis);
			if (cache.DynamicLightingState.NumLights > 0)
				stats.HasDLights = true;
		}

		if ((flags & LightCacheFlags.Static) != 0)
			CopyPrecalcedLightingState(cache, ref lightingState, info);
		else
			lightingState.ZeroLightingState();

		if ((flags & LightCacheFlags.LightStyle) != 0)
			vis = AddLightingState(ref lightingState, cache.LightStyleLightingState, info, cache.LightingOrigin, vis, true, false);

		if ((flags & LightCacheFlags.Dynamic) != 0)
			vis = AddLightingState(ref lightingState, cache.DynamicLightingState, info, cache.LightingOrigin, vis, true, false);

		// todo r_drawlightcache

		return cache.EnvCubemapTexture;
	}

	public void InvalidateStaticLightingCache() {
		// todo
	}

	public void ComputeDynamicLighting(in Vector3 pt, in Vector3 normal, out Vector3 color) {
		throw new NotImplementedException();
	}

	public void ComputeLighting(in Vector3 pt, in Vector3 normal, bool clamp, out Vector3 color, Span<Vector3> boxColors) {
		throw new NotImplementedException();
	}

	// Finds ambient lights
	public BSPDWorldLight? FindAmbientLight() {
		WorldBrushData worldBrush = host_state.WorldBrush!;
		for (int i = 0; i < worldBrush.NumWorldLights; i++)
			if (worldBrush.WorldLights![i].Type == EmitType.SkyAmbient)
				return host_state.WorldBrush!.WorldLights![i];

		return null;
	}

	public static float Engine_WorldLightDistanceFalloff(in BSPDWorldLight wl, in Vector3 delta, bool noRadiusCheck = false) {
		float falloff;

		switch (wl.Type) {
			case EmitType.Surface:
				if (wl.Radius != 0) {
					if (MathLib.DotProduct(delta, delta) > wl.Radius * wl.Radius)
						return 0.0f;
				}
				return 1.0f / MathF.Max(1.0f, MathLib.DotProduct(delta, delta));
			case EmitType.SkyLight:
				return 1.0f;
			case EmitType.QuakeLight:
				falloff = wl.LinearAttn - MathF.Sqrt(MathLib.DotProduct(delta, delta));
				if (falloff < 0)
					return 0.0f;

				return falloff;
			case EmitType.SkyAmbient:
				return 1.0f;
			case EmitType.Point:
			case EmitType.SpotLight: {
					float dist2 = MathLib.DotProduct(delta, delta);
					float dist = MathF.Sqrt(dist2);

					if (!noRadiusCheck && wl.Radius != 0 && dist > wl.Radius)
						return 0.0f;

					return 1.0f / (wl.ConstantAttn + wl.LinearAttn * dist + wl.QuadraticAttn * dist2);
				}
		}
		return 1.0f;
	}

	public float Engine_WorldLightAngle(in BSPDWorldLight wl, in Vector3 lnormal, in Vector3 snormal, in Vector3 delta) {
		float dot, dot2, ratio;

		switch (wl.Type) {
			case EmitType.Surface:
				dot = MathLib.DotProduct(snormal, delta);
				if (dot < 0)
					return 0;

				dot2 = -MathLib.DotProduct(delta, lnormal);
				if (dot2 <= 0.1 / 10)
					return 0;

				return dot * dot2;

			case EmitType.Point:
				dot = MathLib.DotProduct(snormal, delta);
				if (dot < 0)
					return 0;
				return dot;

			case EmitType.SpotLight:
				dot = MathLib.DotProduct(snormal, delta);
				if (dot < 0)
					return 0;

				dot2 = -MathLib.DotProduct(delta, lnormal);
				if (dot2 <= wl.StopDot2)
					return 0;

				ratio = dot;
				if (dot2 >= wl.StopDot)
					return ratio;

				if (wl.Exponent == 1 || wl.Exponent == 0)
					ratio *= (dot2 - wl.StopDot2) / (wl.StopDot - wl.StopDot2);
				else
					ratio *= MathF.Pow((dot2 - wl.StopDot2) / (wl.StopDot - wl.StopDot2), wl.Exponent);
				return ratio;

			case EmitType.SkyLight:
				dot2 = -MathLib.DotProduct(snormal, lnormal);
				if (dot2 < 0)
					return 0;
				return dot2;

			case EmitType.QuakeLight:
				dot = MathLib.DotProduct(snormal, delta);
				if (dot < 0)
					return 0;
				return dot;

			case EmitType.SkyAmbient:
				return 1;

			default:
				break;
		}
		return 0;
	}

	private float LightIntensityAndDirectionAtPointOld(ref BSPDWorldLight light, in Vector3 mid, LightIntensityFlags flags, IHandleEntity? ignoreEnt, out Vector3 direction) {
		TraceFilterWorldOnly worldTraceFilter = new();
		TraceFilterWorldAndPropsOnly propTraceFilter = new();
		bool occludeVsProps = (flags & LightIntensityFlags.OccludeVsProps) != 0;

		switch (light.Type) {
			case EmitType.SkyLight:
				MathLib.VectorClear(out direction);
				MathLib.VectorMA(mid, -COORD_EXTENT * 1.74f, light.Normal, out Vector3 end);

				Trace tr;
				Ray ray = default;
				ray.Init(mid, end);
				if (occludeVsProps)
					engineTrace.TraceRay(in ray, Mask.Opaque, ref propTraceFilter, out tr);
				else
					engineTrace.TraceRay(in ray, Mask.Opaque, ref worldTraceFilter, out tr);

				if ((tr.Surface.Flags & (ushort)Surf.Sky) == 0)
					return 0.0f;

				direction = -light.Normal;
				return 1.0f;
			case EmitType.SkyAmbient:
				MathLib.VectorClear(out direction);
				return 0.0f;
		}

		MathLib.VectorSubtract(light.Origin, mid, out direction);
		float ratio = Engine_WorldLightDistanceFalloff(light, direction, (flags & LightIntensityFlags.NoRadiusCheck) != 0);

		if ((flags & LightIntensityFlags.IgnoreLightstyleValue) == 0)
			ratio *= LightStyleValue((byte)light.Style);

		float intensity = MathF.Max(light.Intensity.X, light.Intensity.Y);
		intensity = MathF.Max(intensity, light.Intensity.Z);

		if (light.Type != EmitType.Surface) {
			if (intensity * ratio < r_worldlightmin.GetFloat())
				return 0.0f;
		}

		float dist = MathLib.VectorNormalize(ref direction);

		if ((flags & LightIntensityFlags.NoOcclusionCheck) != 0)
			return ratio;

		Trace pm;
		Ray occludeRay = default;
		occludeRay.Init(mid, light.Origin);
		if (occludeVsProps)
			engineTrace.TraceRay(in occludeRay, Mask.Opaque, ref propTraceFilter, out pm);
		else
			engineTrace.TraceRay(in occludeRay, Mask.Opaque, ref worldTraceFilter, out pm);

		if ((1.0f - pm.Fraction) * dist > 8) // r_drawlightcache
			return 0.0f;

		return ratio;
	}

	private float LightIntensityAndDirectionAtPointNew(ref BSPDWorldLight light, LightZBuffer[]? zBuf, in Vector3 mid, LightIntensityFlags flags, IHandleEntity? ignoreEnt, out Vector3 direction) {
		const int SHADOW_ZBUF_RES = 8;

		TraceFilterWorldOnly worldTraceFilter = new();
		TraceFilterWorldAndPropsOnly propTraceFilter = new();
		bool occludeVsProps = (flags & LightIntensityFlags.OccludeVsProps) != 0;

		switch (light.Type) {
			case EmitType.SkyLight:
				MathLib.VectorClear(out direction);
				MathLib.VectorMA(mid, -COORD_EXTENT * 1.74f, light.Normal, out Vector3 end);

				Trace tr;
				Ray ray = default;
				ray.Init(mid, end);
				if (occludeVsProps)
					engineTrace.TraceRay(in ray, Mask.Opaque, ref propTraceFilter, out tr);
				else
					engineTrace.TraceRay(in ray, Mask.Opaque, ref worldTraceFilter, out tr);

				if ((tr.Surface.Flags & (ushort)Surf.Sky) == 0)
					return 0.0f;

				direction = -light.Normal;
				return 1.0f;
			case EmitType.SkyAmbient:
				MathLib.VectorClear(out direction);
				return 0.0f;
		}

		MathLib.VectorSubtract(light.Origin, mid, out direction);
		float ratio = Engine_WorldLightDistanceFalloff(light, direction, (flags & LightIntensityFlags.NoRadiusCheck) != 0);

		if ((flags & LightIntensityFlags.IgnoreLightstyleValue) == 0)
			ratio *= LightStyleValue((byte)light.Style);

		float intensity = MathF.Max(light.Intensity.X, light.Intensity.Y);
		intensity = MathF.Max(intensity, light.Intensity.Z);

		if (light.Type != EmitType.Surface) {
			if (intensity * ratio < r_worldlightmin.GetFloat())
				return 0.0f;
		}

		float dist = MathLib.VectorNormalize(ref direction);

		if ((flags & LightIntensityFlags.NoOcclusionCheck) != 0)
			return ratio;

		float traceDistance = dist;
		if (dist - (1 << HASH_GRID_SIZEZ) < 8 * SHADOW_ZBUF_RES)
			zBuf = null;

		Vector3 epnt = mid;

		bool hasSample = false;
		int sampleFace = 0, sampleX = 0, sampleY = 0;
		if (zBuf != null) {
			zBuf[0].GetCoords(direction, out sampleX, out sampleY, out sampleFace);
			ref LightShadowZBufferSample sample = ref zBuf[0][sampleFace, sampleX, sampleY];
			hasSample = true;
			if (sample.HitDistance < sample.TraceDistance || sample.TraceDistance >= dist) {
				if (dist > sample.HitDistance + 8)
					return 0;
				else
					return ratio;
			}

			traceDistance = MathF.Max(100.0f, 2.0f * dist);
			MathLib.VectorMA(epnt, dist - traceDistance, direction, out epnt);
		}

		Trace pm;
		Ray occludeRay = default;
		occludeRay.Init(light.Origin, epnt);
		if (occludeVsProps)
			engineTrace.TraceRay(in occludeRay, Mask.Opaque, ref propTraceFilter, out pm);
		else
			engineTrace.TraceRay(in occludeRay, Mask.Opaque, ref worldTraceFilter, out pm);

		float hitDistance = pm.StartSolid ? 1.192092896e-07f : pm.Fraction * traceDistance;

		if (hasSample) {
			ref LightShadowZBufferSample sample = ref zBuf![0][sampleFace, sampleX, sampleY];
			sample.TraceDistance = traceDistance;
			sample.HitDistance = pm.Fraction >= 1.0f ? 1.0e23f : hitDistance;
		}
		if (dist > hitDistance + 8)
			return 0.0f;
		return ratio;
	}

	private float LightIntensityAndDirectionAtPoint(ref BSPDWorldLight light, LightZBuffer[]? zBuf, in Vector3 mid, LightIntensityFlags flags, IHandleEntity? ignoreEnt, out Vector3 direction) {
		if (zBuf != null)
			return LightIntensityAndDirectionAtPointNew(ref light, zBuf, mid, flags, ignoreEnt, out direction);
		else
			return LightIntensityAndDirectionAtPointOld(ref light, mid, flags, ignoreEnt, out direction);
	}

	private float LightIntensityAndDirectionInBox(ref BSPDWorldLight light, LightZBuffer[]? zBuf, in Vector3 mid, in Vector3 mins, in Vector3 maxs, LightIntensityFlags flags, out Vector3 direction) {
		if (!r_oldlightselection.GetBool()) {
			switch (light.Type) {
				case EmitType.SpotLight: {
						float sphereRadius = (maxs - mid).Length();
						float dist = (light.Origin - mid).Length();
						if (dist > sphereRadius + light.Radius) {
							direction = new(0, 0, 0);
							return 0;
						}
						float angle = MathF.Acos(light.StopDot2);
						float sinAngle = MathF.Sin(angle);
						if (!CollisionUtils.IsSphereIntersectingCone(mid, sphereRadius, light.Origin, light.Normal, sinAngle, light.StopDot2)) {
							direction = new(0, 0, 0);
							return 0;
						}
						goto case EmitType.Point;
					}
				case EmitType.Point: {
						float distSqr = MathLib.CalcSqrDistanceToAABB(mins, maxs, light.Origin);
						if (distSqr > light.Radius * light.Radius) {
							direction = new(0, 0, 0);
							return 0;
						}
					}
					break;
				case EmitType.Surface: {
						float sphereRadius = (maxs - mid).Length();
						float dist = (light.Origin - mid).Length();
						if (dist > sphereRadius + light.Radius) {
							direction = new(0, 0, 0);
							return 0;
						}
						if (!CollisionUtils.IsSphereIntersectingCone(mid, sphereRadius, light.Origin, light.Normal, 1.0f, 0.0f)) {
							direction = new(0, 0, 0);
							return 0;
						}
					}
					break;
			}
		}
		else {
			switch (light.Type) {
				case EmitType.Point:
				case EmitType.SpotLight: {
						Vector3 closestPoint = new();
						for (int i = 0; i < 3; ++i)
							closestPoint[i] = Math.Clamp(light.Origin[i], mins[i], maxs[i]);

						closestPoint -= light.Origin;
						if (closestPoint.LengthSquared() > light.Radius * light.Radius) {
							direction = new(0, 0, 0);
							return 0;
						}
					}
					break;
			}
		}

		return LightIntensityAndDirectionAtPoint(ref light, zBuf, mid, flags | LightIntensityFlags.NoRadiusCheck, null, out direction);
	}

	private void BuildStaticLightingCacheLightStyleInfo(PropLightcache cache, in Vector3 mins, in Vector3 maxs) {
		Span<byte> vis = default;
		bool haveVis = false;
		Assert(cache.LightStyleWorldLights.Count == 0);
		cache.LightingFlags &= ~(int)(HackLightCacheFlags.HasSwitchableLightStyle | HackLightCacheFlags.HasNonSwitchableLightStyle);
		for (int i = 0; i < BaseLightCache.MAX_LIGHTSTYLE_BYTES; i++)
			cache.Lightstyles[i] = 0;

		WorldBrushData worldBrush = host_state.WorldBrush!;
		for (short i = 0; i < worldBrush.NumWorldLights; ++i) {
			ref BSPDWorldLight wl = ref worldBrush.WorldLights![i];
			if (wl.Style == 0)
				continue;

			if (!haveVis) {
				vis = CM.ClusterPVS(CM.LeafCluster(cache.Leaf));
				haveVis = true;
			}
			if ((vis[wl.Cluster >> 3] & (1 << (wl.Cluster & 7))) != 0) {
				BSPDWorldLight tmpLight = wl;
				tmpLight.Style = 0;
				float ratio = LightIntensityAndDirectionInBox(ref tmpLight, null, cache.LightingOrigin, mins, maxs,
					LightIntensityFlags.NoOcclusionCheck | LightIntensityFlags.IgnoreLightstyleValue, out _);
				if (ratio <= 0.0f)
					continue;

				cache.LightStyleWorldLights.Add(i);

				int b = wl.Style >> 3;
				int bit = wl.Style & 0x7;
				cache.Lightstyles[b] |= (byte)(1 << bit);
				if (MatSysInterface.LightStyleNumFrames[wl.Style] <= 1)
					cache.LightingFlags |= (int)HackLightCacheFlags.HasSwitchableLightStyle;
				else
					cache.LightingFlags |= (int)HackLightCacheFlags.HasNonSwitchableLightStyle;
			}
		}
	}

	// Precache lighting
	public LightCacheHandle_t CreateStaticLightingCache(in Vector3 origin, in Vector3 mins, in Vector3 maxs) {
		PropLightcache pcache = new() {
			LightingOrigin = origin,
			Flags = 0,
			Mins = mins,
			Maxs = maxs,
			Leaf = CM.PointLeafnum(origin),
			EnvCubemapTexture = FindEnvCubemapForPoint(origin)
		};

		BuildStaticLightingCacheLightStyleInfo(pcache, mins, maxs);

		PropCaches.Add(pcache);
		return PropCaches.Count;
	}

	public void ClearStaticLightingCache() {
		PropCaches.Clear();
		// AllStaticProps = null;
	}

	public bool ComputeVertexLightingFromSphericalSamples(in Vector3 vertex, in Vector3 normal, IHandleEntity? ignoreEnt, out Vector3 linearColor) {
		throw new NotImplementedException();
	}

	public bool StaticLightCacheAffectedByDynamicLight(LightCacheHandle_t handle) {
		PropLightcache pcache = PropCaches[(int)handle - 1];
		return pcache.HasDlights();
	}

	public bool StaticLightCacheAffectedByAnimatedLightStyle(LightCacheHandle_t handle) {
		PropLightcache pcache = PropCaches[(int)handle - 1];
		if (!pcache.HasLightStyle())
			return false;
		else {
			for (int i = 0; i < pcache.LightStyleWorldLights.Count; ++i) {
				Assert(pcache.LightStyleWorldLights[i] >= 0);
				Assert(pcache.LightStyleWorldLights[i] < host_state.WorldBrush!.NumWorldLights);
				ref BSPDWorldLight wl = ref host_state.WorldBrush!.WorldLights![pcache.LightStyleWorldLights[i]];
				Assert(wl.Style != 0);
				if (MatSysInterface.LightStyleNumFrames[wl.Style] > 1)
					return true;
			}
			return false;
		}
	}

	public bool StaticLightCacheNeedsSwitchableLightUpdate(LightCacheHandle_t handle) {
		PropLightcache pcache = PropCaches[(int)handle - 1];
		if (!pcache.HasSwitchableLightStyle())
			return false;
		else {
			for (int i = 0; i < pcache.LightStyleWorldLights.Count; ++i) {
				Assert(pcache.LightStyleWorldLights[i] >= 0);
				Assert(pcache.LightStyleWorldLights[i] < host_state.WorldBrush!.NumWorldLights);
				ref BSPDWorldLight wl = ref host_state.WorldBrush!.WorldLights![pcache.LightStyleWorldLights[i]];
				Assert(wl.Style != 0);
				if (MatSysInterface.LightStyleNumFrames[wl.Style] <= 1) {
					if (pcache.SwitchableLightFrame < MatSysInterface.LightStyleFrame[wl.Style]) {
						pcache.SwitchableLightFrame = r_framecount;
						return true;
					}
				}
			}
			return false;
		}
	}

	public void AddWorldLightToAmbientCube(BSPDWorldLightPtr worldLight, in Vector3 lightingOrigin, ref AmbientCube ambientCube) {

	}

	public static float MinLightingValue = 1.0f;
	public static void InitDLightGlobals(int mapVersion) {
		if (mapVersion >= 20)
			MinLightingValue = 1.0f / 256.0f;
		else
			MinLightingValue = 20.0f / 256.0f;
	}
}
