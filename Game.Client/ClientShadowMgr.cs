global using static Game.Client.ClientShadowMgrGlobals;
using static Source.Engine.ShadowMgrGlobals;
using static Source.Engine.StaticPropMgrGlobals;

using CommunityToolkit.HighPerformance;

using Source;
using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;

using Game.Shared;

namespace Game.Client;

public static class ClientShadowMgrGlobals
{
	public static readonly ConVar r_flashlightdrawfrustum = new("r_flashlightdrawfrustum", "0");
	public static readonly ConVar r_flashlightmodels = new("r_flashlightmodels", "1");
	public static readonly ConVar r_shadowrendertotexture = new("r_shadowrendertotexture", "0");

	public static readonly ConVar r_flashlightdepthtexture = new("r_flashlightdepthtexture", "1");
	public static readonly ConVar r_flashlightdepthres = new("r_flashlightdepthres", "1024");

	public static readonly ConVar r_threaded_client_shadow_manager = new("r_threaded_client_shadow_manager", "0");

	public const TextureHandle_t INVALID_TEXTURE_HANDLE = unchecked((TextureHandle_t)~0);

	public const float TEXEL_SIZE_PER_CASTER_SIZE = 2.0f;
	public const int MAX_FALLOFF_AMOUNT = 240;
	public const int MAX_CLIP_PLANE_COUNT = 4;
	public const float SHADOW_CULL_TOLERANCE = 0.5f;

	public static readonly ConVar r_shadowmaxrendered = new("r_shadowmaxrendered", "32");

	public static readonly ClientShadowMgr s_ClientShadowMgr = new();
	public static readonly IClientShadowMgr g_ClientShadowMgr = s_ClientShadowMgr;

	public static readonly VisibleShadowList s_VisibleShadowList = new();

	public static void ShadowRestoreFunc(RestoreChangeFlags changeFlags) {
		s_ClientShadowMgr.RestoreRenderState();
	}

	public static readonly List<C_BaseAnimating> s_NPCShadowBoneSetups = [];
	public static readonly List<C_BaseAnimating> s_NonNPCShadowBoneSetups = [];

	[ConCommand("r_shadowangles", "Set shadow angles", FCvar.Cheat)]
	public static void r_shadowangles(in TokenizedCommand args) {
		if (args.ArgC() == 1) {
			Vector3 dir = s_ClientShadowMgr.GetShadowDirection();
			MathLib.VectorAngles(dir, out QAngle angles);
			Msg($"Shadow angles {angles.X} {angles.Y} {angles.Z}\n");
			return;
		}

		if (args.ArgC() == 4) {
			QAngle angles = default;
			_ = float.TryParse(args[1], out angles.X);
			_ = float.TryParse(args[2], out angles.Y);
			_ = float.TryParse(args[3], out angles.Z);
			MathLib.AngleVectors(angles, out Vector3 dir);
			s_ClientShadowMgr.SetShadowDirection(dir);
		}
	}

	[ConCommand("r_shadowcolor", "Set shadow color", FCvar.Cheat)]
	public static void r_shadowcolor(in TokenizedCommand args) {
		if (args.ArgC() == 1) {
			s_ClientShadowMgr.GetShadowColor(out byte r, out byte g, out byte b);
			Msg($"Shadow color {r} {g} {b}\n");
			return;
		}

		if (args.ArgC() == 4) {
			_ = int.TryParse(args[1], out int r);
			_ = int.TryParse(args[2], out int g);
			_ = int.TryParse(args[3], out int b);
			s_ClientShadowMgr.SetShadowColor((byte)r, (byte)g, (byte)b);
		}
	}

	[ConCommand("r_shadowdist", "Set shadow distance", FCvar.Cheat)]
	public static void r_shadowdist(in TokenizedCommand args) {
		if (args.ArgC() == 1) {
			float dist = s_ClientShadowMgr.GetShadowDistance();
			Msg($"Shadow distance {dist:F2}\n");
			return;
		}

		if (args.ArgC() == 2) {
			_ = float.TryParse(args[1], out float dist);
			s_ClientShadowMgr.SetShadowDistance(dist);
		}
	}

	[ConCommand("r_shadowblobbycutoff", "some shadow stuff", FCvar.Cheat)]
	public static void r_shadowblobbycutoff(in TokenizedCommand args) {
		if (args.ArgC() == 1) {
			float area = s_ClientShadowMgr.GetBlobbyCutoffArea();
			Msg($"Cutoff area {area:F2}\n");
			return;
		}

		if (args.ArgC() == 2)
			s_ClientShadowMgr.SetShadowBlobbyCutoffArea(float.Parse(args[1]));
	}
}

public class TextureAllocator
{
	public const FragmentHandle_t INVALID_FRAGMENT_HANDLE = unchecked((FragmentHandle_t)~0);
	public const int TEXTURE_PAGE_SIZE = 1024;
	public const int MAX_TEXTURE_POWER = 8;
	public const int MIN_TEXTURE_POWER = 4;
	public const int MAX_TEXTURE_SIZE = 1 << MAX_TEXTURE_POWER;
	public const int MIN_TEXTURE_SIZE = 1 << MIN_TEXTURE_POWER;
	public const int BLOCK_SIZE = MAX_TEXTURE_SIZE;
	public const int BLOCKS_PER_ROW = TEXTURE_PAGE_SIZE / MAX_TEXTURE_SIZE;
	public const int BLOCK_COUNT = BLOCKS_PER_ROW * BLOCKS_PER_ROW;

	public struct TextureInfo_t
	{
		public FragmentHandle_t Fragment;
		public ushort Size;
		public ushort Power;
	}

	public struct FragmentInfo_t
	{
		public ushort Block;
		public ushort Index;
		public TextureHandle_t Texture;

		public uint FrameUsed;

		public FragmentHandle_t Prev;
		public FragmentHandle_t Next;
	}

	public struct BlockInfo_t
	{
		public ushort FragmentPower;
	}

	public struct Cache_t
	{
		public FragmentHandle_t Head;
		public FragmentHandle_t Tail;
	}

	readonly TextureReference TexturePage = new();

	readonly PooledLinkedList<TextureInfo_t> Textures = new();
	readonly List<FragmentInfo_t> Fragments = new(256);

	Cache_t[] Cache = new Cache_t[MAX_TEXTURE_POWER + 1];
	BlockInfo_t[] Blocks = new BlockInfo_t[BLOCK_COUNT];
	uint CurrentFrame;

	Span<FragmentInfo_t> Frags => Fragments.AsSpan();

	public void Init() {
		for (int i = 0; i <= MAX_TEXTURE_POWER; ++i) {
			Cache[i].Head = INVALID_FRAGMENT_HANDLE;
			Cache[i].Tail = INVALID_FRAGMENT_HANDLE;
		}

		TexturePage.InitRenderTarget(TEXTURE_PAGE_SIZE, TEXTURE_PAGE_SIZE, RenderTargetSizeMode.NoChange, ImageFormat.ARGB8888, MaterialRenderTargetDepth.None, false, "_rt_Shadows");
	}

	public void Shutdown() => TexturePage.Shutdown();

	public void Reset() {
		DeallocateAllTextures();

		Fragments.EnsureCapacity(256);

		Blocks[0].FragmentPower = MAX_TEXTURE_POWER - 4;
		Blocks[1].FragmentPower = MAX_TEXTURE_POWER - 3;
		Blocks[2].FragmentPower = MAX_TEXTURE_POWER - 2;
		Blocks[3].FragmentPower = MAX_TEXTURE_POWER - 2;
		Blocks[4].FragmentPower = MAX_TEXTURE_POWER - 1;
		Blocks[5].FragmentPower = MAX_TEXTURE_POWER - 1;
		Blocks[6].FragmentPower = MAX_TEXTURE_POWER - 1;
		Blocks[7].FragmentPower = MAX_TEXTURE_POWER - 1;
		Blocks[8].FragmentPower = MAX_TEXTURE_POWER - 1;
		Blocks[9].FragmentPower = MAX_TEXTURE_POWER - 1;
		Blocks[10].FragmentPower = MAX_TEXTURE_POWER;
		Blocks[11].FragmentPower = MAX_TEXTURE_POWER;
		Blocks[12].FragmentPower = MAX_TEXTURE_POWER;
		Blocks[13].FragmentPower = MAX_TEXTURE_POWER;
		Blocks[14].FragmentPower = MAX_TEXTURE_POWER;
		Blocks[15].FragmentPower = MAX_TEXTURE_POWER;

		int i;
		for (i = 0; i <= MAX_TEXTURE_POWER; ++i) {
			Cache[i].Head = INVALID_FRAGMENT_HANDLE;
			Cache[i].Tail = INVALID_FRAGMENT_HANDLE;
		}

		for (i = 0; i < BLOCK_COUNT; ++i)
			AddBlockToLRU(i);

		CurrentFrame = 0;
	}

	public void DeallocateAllTextures() {
		Textures.Clear();
		Fragments.Clear();
		for (int i = 0; i <= MAX_TEXTURE_POWER; ++i) {
			Cache[i].Head = INVALID_FRAGMENT_HANDLE;
			Cache[i].Tail = INVALID_FRAGMENT_HANDLE;
		}
	}

	public void DebugPrintCache() {
		int numFragments = Fragments.Count;
		int numInvalidFragments = 0;

		Warning($"Fragments ({numFragments}):\n===============\n");

		Span<FragmentInfo_t> frags = Frags;
		for (int f = 0; f < numFragments; f++) {
			if (frags[f].FrameUsed != 0 && frags[f].Texture != INVALID_TEXTURE_HANDLE)
				Warning($"Fragment {f}, Block: {frags[f].Block}, Index: {frags[f].Index}, Texture: {frags[f].Texture} Frame Used: {frags[f].FrameUsed}\n");
			else
				numInvalidFragments++;
		}

		Warning($"Invalid Fragments: {numInvalidFragments}\n");
	}

	void AddBlockToLRU(int block) {
		int power = Blocks[block].FragmentPower;
		int size = 1 << power;

		int fragmentCount = MAX_TEXTURE_SIZE / size;
		fragmentCount *= fragmentCount;

		while (--fragmentCount >= 0) {
			FragmentHandle_t f = (FragmentHandle_t)Fragments.Count;
			Fragments.Add(new FragmentInfo_t() {
				Block = (ushort)block,
				Index = (ushort)fragmentCount,
				Texture = INVALID_TEXTURE_HANDLE,
				FrameUsed = 0xFFFFFFFF,
				Prev = INVALID_FRAGMENT_HANDLE,
				Next = INVALID_FRAGMENT_HANDLE
			});
			LinkToHead(ref Cache[power], f);
		}
	}

	void LinkToHead(ref Cache_t cache, FragmentHandle_t fragment) {
		Unlink(ref cache, fragment);

		Span<FragmentInfo_t> frags = Frags;
		frags[fragment].Next = cache.Head;
		if (cache.Head != INVALID_FRAGMENT_HANDLE)
			frags[cache.Head].Prev = fragment;
		else
			cache.Tail = fragment;
		cache.Head = fragment;
	}

	void LinkToTail(ref Cache_t cache, FragmentHandle_t fragment) {
		Unlink(ref cache, fragment);

		Span<FragmentInfo_t> frags = Frags;
		frags[fragment].Prev = cache.Tail;
		if (cache.Tail != INVALID_FRAGMENT_HANDLE)
			frags[cache.Tail].Next = fragment;
		else
			cache.Head = fragment;
		cache.Tail = fragment;
	}

	void Unlink(ref Cache_t cache, FragmentHandle_t fragment) {
		Span<FragmentInfo_t> frags = Frags;
		FragmentHandle_t prev = frags[fragment].Prev;
		FragmentHandle_t next = frags[fragment].Next;

		if (prev != INVALID_FRAGMENT_HANDLE)
			frags[prev].Next = next;
		else if (cache.Head == fragment)
			cache.Head = next;

		if (next != INVALID_FRAGMENT_HANDLE)
			frags[next].Prev = prev;
		else if (cache.Tail == fragment)
			cache.Tail = prev;

		frags[fragment].Prev = INVALID_FRAGMENT_HANDLE;
		frags[fragment].Next = INVALID_FRAGMENT_HANDLE;
	}

	void UnlinkFragmentFromCache(ref Cache_t cache, FragmentHandle_t fragment) => Unlink(ref cache, fragment);

	void MarkUsed(FragmentHandle_t fragment) {
		int block = Frags[fragment].Block;
		int power = Blocks[block].FragmentPower;

		LinkToTail(ref Cache[power], fragment);
		Frags[fragment].FrameUsed = CurrentFrame;
	}

	void MarkUnused(FragmentHandle_t fragment) {
		int block = Frags[fragment].Block;
		int power = Blocks[block].FragmentPower;

		LinkToHead(ref Cache[power], fragment);
	}

	public TextureHandle_t AllocateTexture(int w, int h) {
		Assert(w == h);

		if (w < MIN_TEXTURE_SIZE)
			w = MIN_TEXTURE_SIZE;
		else if (w > MAX_TEXTURE_SIZE)
			w = MAX_TEXTURE_SIZE;

		TextureHandle_t handle = (TextureHandle_t)Textures.Alloc();
		Textures[handle].Fragment = INVALID_FRAGMENT_HANDLE;
		Textures[handle].Size = (ushort)w;

		int power = 0;
		int size = 1;
		while (size < w) {
			size <<= 1;
			++power;
		}
		Assert(size == w);

		Textures[handle].Power = (ushort)power;

		return handle;
	}

	public void DeallocateTexture(TextureHandle_t h) {
		if (Textures[h].Fragment != INVALID_FRAGMENT_HANDLE) {
			MarkUnused(Textures[h].Fragment);
			Frags[Textures[h].Fragment].FrameUsed = 0xFFFFFFFF;
			DisconnectTextureFromFragment(Textures[h].Fragment);
		}
		Textures.Remove(h);
	}

	void DisconnectTextureFromFragment(FragmentHandle_t f) {
		ref FragmentInfo_t info = ref Frags[f];
		if (info.Texture != INVALID_TEXTURE_HANDLE) {
			Textures[info.Texture].Fragment = INVALID_FRAGMENT_HANDLE;
			info.Texture = INVALID_TEXTURE_HANDLE;
		}
	}

	public bool HasValidTexture(TextureHandle_t h) {
		ref TextureInfo_t info = ref Textures[h];
		FragmentHandle_t currentFragment = info.Fragment;
		return currentFragment != INVALID_FRAGMENT_HANDLE;
	}

	public bool UseTexture(TextureHandle_t h, bool willRedraw, float area) {
		ref TextureInfo_t info = ref Textures[h];

		int desiredPower = MIN_TEXTURE_POWER;
		int desiredWidth = MIN_TEXTURE_SIZE;
		while (desiredWidth * desiredWidth < area) {
			if (desiredPower >= info.Power) {
				desiredPower = info.Power;
				break;
			}

			++desiredPower;
			desiredWidth <<= 1;
		}

		int currentPower = -1;
		FragmentHandle_t currentFragment = info.Fragment;
		if (currentFragment != INVALID_FRAGMENT_HANDLE) {
			currentPower = GetFragmentPower(info.Fragment);
			Assert(currentPower <= info.Power);
			bool shouldKeepTexture = !willRedraw && desiredPower < 8 && desiredPower - currentPower <= 1;
			if (currentPower == desiredPower || shouldKeepTexture) {
				MarkUsed(currentFragment);
				return false;
			}
		}

		int power = desiredPower;

		FragmentHandle_t f = INVALID_FRAGMENT_HANDLE;
		bool done = false;
		while (!done && power >= 0) {
			f = Cache[power].Head;

			if (f != INVALID_FRAGMENT_HANDLE && Frags[f].FrameUsed != CurrentFrame)
				done = true;
			else
				--power;
		}

		if (currentFragment != INVALID_FRAGMENT_HANDLE) {
			if (power <= currentPower) {
				MarkUsed(currentFragment);
				return false;
			}
			else {
				DisconnectTextureFromFragment(currentFragment);
			}
		}

		if (f == INVALID_FRAGMENT_HANDLE)
			return false;

		DisconnectTextureFromFragment(f);

		info.Fragment = f;
		Frags[f].Texture = h;

		MarkUsed(f);

		return true;
	}

	int GetFragmentPower(FragmentHandle_t f) => Blocks[Frags[f].Block].FragmentPower;

	public void AdvanceFrame() => CurrentFrame++;

	public ITexture? GetTexture() => TexturePage.Get();

	public void GetTotalTextureSize(out int w, out int h) => w = h = TEXTURE_PAGE_SIZE;

	public void GetTextureRect(TextureHandle_t handle, out int x, out int y, out int w, out int h) {
		ref TextureInfo_t info = ref Textures[handle];
		Assert(info.Fragment != INVALID_FRAGMENT_HANDLE);

		ref FragmentInfo_t fragment = ref Frags[info.Fragment];
		int blockY = fragment.Block / BLOCKS_PER_ROW;
		int blockX = fragment.Block - blockY * BLOCKS_PER_ROW;

		int fragmentSize = 1 << Blocks[fragment.Block].FragmentPower;
		int fragmentsPerRow = BLOCK_SIZE / fragmentSize;
		int fragmentY = fragment.Index / fragmentsPerRow;
		int fragmentX = fragment.Index - fragmentY * fragmentsPerRow;

		x = blockX * BLOCK_SIZE + fragmentX * fragmentSize;
		y = blockY * BLOCK_SIZE + fragmentY * fragmentSize;
		w = fragmentSize;
		h = fragmentSize;
	}
}

public struct VisibleShadowInfo_t
{
	public ClientShadowHandle_t Shadow;
	public float Area;
	public Vector3 AbsCenter;
}

public class VisibleShadowList : IClientLeafShadowEnum
{
	readonly List<VisibleShadowInfo_t> ShadowsInView = [];
	readonly List<int> PriorityIndex = [];

	public int GetVisibleShadowCount() => ShadowsInView.Count;

	public ref readonly VisibleShadowInfo_t GetVisibleShadow(int i) => ref ShadowsInView.AsSpan()[PriorityIndex[i]];

	float ComputeScreenArea(in Vector3 center, float r) {
		IMatRenderContext renderContext = materials.GetRenderContext();
		float screenDiameter = renderContext.ComputePixelDiameterOfSphere(center, r);
		return screenDiameter * screenDiameter;
	}

	public void EnumShadow(ClientShadowHandle_t clientShadowHandle) {
		ref ClientShadowMgr.ClientShadow_t shadow = ref s_ClientShadowMgr.Shadows[clientShadowHandle].Shadow;

		if (shadow.RenderFrame == gpGlobals.FrameCount)
			return;

		if (s_ClientShadowMgr.GetActualShadowCastType(clientShadowHandle) != ShadowType.RenderToTexture)
			return;

		ref readonly Source.Common.Engine.ShadowInfo_t shadowInfo = ref g_ShadowMgr.GetInfo(shadow.ShadowHandle);
		if (shadowInfo.FalloffBias == 255)
			return;

		IClientRenderable? renderable = cl_entitylist.GetClientRenderableFromHandle(shadow.Entity);
		Assert(renderable != null);

		if (s_ClientShadowMgr.ShouldUseParentShadow(renderable) || s_ClientShadowMgr.WillParentRenderBlobbyShadow(renderable))
			return;

		s_ClientShadowMgr.ComputeBoundingSphere(renderable, out Vector3 absCenter, out float radius);

		s_ClientShadowMgr.ComputeShadowBBox(renderable, in absCenter, radius, out Vector3 absMins, out Vector3 absMaxs);

		if (engine.CullBox(in absMins, in absMaxs))
			return;

		VisibleShadowInfo_t info = default;
		info.Shadow = clientShadowHandle;
		info.Area = ComputeScreenArea(in absCenter, radius);
		ShadowsInView.Add(info);

		shadow.RenderFrame = gpGlobals.FrameCount;
	}

	void PrioritySort() {
		int count = ShadowsInView.Count;
		PriorityIndex.EnsureCapacity(count);

		PriorityIndex.Clear();

		int i, j;
		for (i = 0; i < count; ++i)
			PriorityIndex.Add(i);

		for (i = 0; i < count - 1; ++i) {
			int largestInd = i;
			float largestArea = ShadowsInView[PriorityIndex[i]].Area;
			for (j = i + 1; j < count; ++j) {
				int index = PriorityIndex[j];
				if (largestArea < ShadowsInView[index].Area) {
					largestInd = j;
					largestArea = ShadowsInView[index].Area;
				}
			}
			(PriorityIndex[i], PriorityIndex[largestInd]) = (PriorityIndex[largestInd], PriorityIndex[i]);
		}
	}

	public int FindShadows(in ViewSetup view, int leafCount, List<LeafIndex_t> leafList) {
		ShadowsInView.Clear();
		clientLeafSystem.EnumerateShadowsInLeaves(leafCount, leafList, this);
		int count = ShadowsInView.Count;
		if (count != 0)
			PrioritySort();

		return count;
	}
}

public class ShadowLeafEnum : ISpatialLeafEnumerator
{
	public readonly List<int> LeafList = [];

	public bool EnumerateLeaf(int leaf, nint context) {
		LeafList.Add(leaf);
		return true;
	}
}

public class ClientShadowBox
{
	public ClientShadowMgr.ClientShadow_t Shadow = new();
}

public class ClientShadowMgr : IClientShadowMgr
{
	public enum ShadowFlags_t
	{
		TextureDirty = ClientShadowFlags.LastFlag << 1,
		BrushModel = ClientShadowFlags.LastFlag << 2,
		UsingLodShadow = ClientShadowFlags.LastFlag << 3,
		LightWorld = ClientShadowFlags.LastFlag << 4,
	}

	public struct ClientShadow_t
	{
		public ClientShadow_t() => TargetEntity = new();
		public ClientEntityHandle Entity;
		public ShadowHandle_t ShadowHandle;
		public ClientLeafShadowHandle_t ClientLeafShadowHandle;
		public ushort Flags;
		public Matrix4x4 WorldToShadow;
		public Vector2 WorldSize;
		public Vector3 LastOrigin;
		public QAngle LastAngles;
		public TextureHandle_t ShadowTexture;
		public ITexture? ShadowDepthTexture;
		public long RenderFrame;
		public EHANDLE TargetEntity;
	}

	Vector3 SimpleShadowDir;
	Color AmbientLightColor;
	IMaterial? SimpleShadow;
	IMaterial? RenderShadow;
	IMaterial? RenderModelShadow;
	ITexture? DummyColorTexture;
	internal readonly Dictionary<ClientShadowHandle_t, ClientShadowBox> Shadows = [];
	readonly List<ClientShadowHandle_t> ValidShadowHandles = [];
	ClientShadowHandle_t curShadowHandleIdx;
	readonly TextureAllocator ShadowAllocator = new();

	bool RenderToTextureActive;
	bool RenderTargetNeedsClear;
	bool UpdatingDirtyShadows;
	bool Threaded;
	float ShadowCastDist;
	float MinShadowArea;
	readonly SortedSet<ClientShadowHandle_t> DirtyShadows = [];
	readonly List<ClientShadowHandle_t> TransparentShadows = [];

	bool DepthTextureActive;
	int DepthTextureResolution;

	readonly List<ITexture?> DepthTextureCache = [];
	readonly List<bool> DepthTextureCacheLocks = [];
	int MaxDepthTextureShadows;

	public ClientShadowMgr() {
		RenderToTextureActive = false;
		DepthTextureActive = false;

		DepthTextureResolution = r_flashlightdepthres.GetInt();
		Threaded = false;
	}

	public ReadOnlySpan<char> Name() => "CCLientShadowMgr";

	public bool Init() {
		RenderTargetNeedsClear = false;
		SimpleShadow = materials.FindMaterial("decals/simpleshadow", MaterialDefines.TEXTURE_GROUP_DECAL);

		Vector3 dir = new(0.1f, 0.1f, -1);
		SetShadowDirection(dir);
		SetShadowDistance(50);

		SetShadowBlobbyCutoffArea(0.005f);

		bool tools = commandLine.CheckParm("-tools");
		MaxDepthTextureShadows = tools ? 4 : 2;

		if (r_shadowrendertotexture.GetBool())
			InitRenderToTextureShadows();

		if (r_flashlightdepthtexture.GetBool() && !materials.SupportsShadowDepthTextures()) {
			r_flashlightdepthtexture.SetValue(0);
			ShutdownDepthTextureShadows();
		}

		if (r_flashlightdepthtexture.GetBool())
			InitDepthTextureShadows();

		materials.AddRestoreFunc(ShadowRestoreFunc);

		return true;
	}
	public void PostInit() { }
	public void Shutdown() {
		SimpleShadow = null;
		Shadows.Clear();
		ValidShadowHandles.Clear();
		ShutdownRenderToTextureShadows();

		ShutdownDepthTextureShadows();

		materials.RemoveRestoreFunc(ShadowRestoreFunc);
	}

	public void LevelInitPreEntity() {
		UpdatingDirtyShadows = false;

		engine.GetAmbientLightColor(out Vector3 ambientColor);
		ambientColor *= 3;
		ambientColor += new Vector3(0.3f, 0.3f, 0.3f);

		byte r = ambientColor[0] > 1.0 ? (byte)255 : (byte)(255 * ambientColor[0]);
		byte g = ambientColor[1] > 1.0 ? (byte)255 : (byte)(255 * ambientColor[1]);
		byte b = ambientColor[2] > 1.0 ? (byte)255 : (byte)(255 * ambientColor[2]);

		SetShadowColor(r, g, b);

		if (RenderToTextureActive) {
			ShadowAllocator.Reset();
			RenderTargetNeedsClear = true;
		}
	}
	public void LevelInitPostEntity() { }
	public void LevelShutdownPreClearSteamAPIContext() { }
	public void LevelShutdownPreEntity() { }
	public void LevelShutdownPostEntity() {
		Assert(Shadows.Count == 0);

		for (int i = ValidShadowHandles.Count - 1; i >= 0; i--)
			DestroyShadow(ValidShadowHandles[i]);

		if (RenderToTextureActive)
			ShadowAllocator.DeallocateAllTextures();

		r_shadows_gamecontrol.SetValue(-1);
	}

	public bool IsPerFrame() => true;

	public void PreRender() {
		if (r_flashlightdepthtexture.GetBool() && !materials.SupportsShadowDepthTextures()) {
			r_flashlightdepthtexture.SetValue(0);
			ShutdownDepthTextureShadows();
		}

		bool depthTextureActive = r_flashlightdepthtexture.GetBool();
		int depthTextureResolution = r_flashlightdepthres.GetInt();

		if ((depthTextureActive != DepthTextureActive) || (depthTextureResolution != DepthTextureResolution)) {
			if ((depthTextureActive == true) && (DepthTextureActive == true) &&
				(depthTextureResolution != DepthTextureResolution)) {
				ShutdownDepthTextureShadows();
				InitDepthTextureShadows();
			}
			else {
				if (DepthTextureActive && !depthTextureActive)
					ShutdownDepthTextureShadows();
				else if (depthTextureActive && !DepthTextureActive)
					InitDepthTextureShadows();
			}
		}

		bool renderToTextureActive = r_shadowrendertotexture.GetBool();
		if (renderToTextureActive != RenderToTextureActive) {
			if (RenderToTextureActive)
				ShutdownRenderToTextureShadows();
			else
				InitRenderToTextureShadows();

			UpdateAllShadows();
			return;
		}

		UpdatingDirtyShadows = true;

		foreach (ClientShadowHandle_t handle in DirtyShadows) {
			Assert(Shadows.ContainsKey(handle));
			UpdateProjectedTextureInternal(handle, false);
		}
		DirtyShadows.Clear();

		int count = TransparentShadows.Count;
		for (int j = 0; j < count; ++j)
			DirtyShadows.Add(TransparentShadows[j]);
		TransparentShadows.Clear();

		UpdatingDirtyShadows = false;
	}
	public void Update(double frametime) { }
	public void PostRender() { }

	public void OnSave() { }
	public void OnRestore() { }
	public void SafeRemoveIfDesired() { }

	public ClientShadowHandle_t CreateShadow(ClientEntityHandle entity, int flags) {
		flags &= ~(int)ShadowFlags.ProjectedTextureTypeMask;
		flags |= (int)ShadowFlags.Shadow | (int)ShadowFlags_t.TextureDirty;
		ClientShadowHandle_t shadowHandle = CreateProjectedTexture(entity, flags);

		IClientRenderable? renderable = cl_entitylist.GetClientRenderableFromHandle(entity);
		if (renderable != null) {
			Assert(!renderable.IsShadowDirty());
			renderable.MarkShadowDirty(true);
		}

		AddToDirtyShadowList(shadowHandle, true);
		return shadowHandle;
	}

	public void DestroyShadow(ClientShadowHandle_t handle) {
		Assert(Shadows.ContainsKey(handle));
		RemoveShadowFromDirtyList(handle);
		g_ShadowMgr.DestroyShadow(Shadows[handle].Shadow.ShadowHandle);
		clientLeafSystem.RemoveShadow(Shadows[handle].Shadow.ClientLeafShadowHandle);
		CleanUpRenderToTextureShadow(handle);
		Shadows.Remove(handle);
		ValidShadowHandles.Remove(handle);
	}

	public ClientShadowHandle_t CreateFlashlight(in FlashlightState lightState) {
		ClientEntityHandle invalidHandle = INVALID_CLIENTENTITY_HANDLE;

		int shadowFlags = (int)ShadowFlags.Flashlight | (int)ShadowFlags_t.LightWorld;
		if (lightState.EnableShadows && r_flashlightdepthtexture.GetBool()) {
			shadowFlags |= (int)ClientShadowFlags.UseDepthTexture;
		}

		ClientShadowHandle_t shadowHandle = CreateProjectedTexture(invalidHandle, shadowFlags);

		UpdateFlashlightState(shadowHandle, in lightState);
		UpdateProjectedTexture(shadowHandle, true);
		return shadowHandle;
	}

	public void UpdateFlashlightState(ClientShadowHandle_t shadowHandle, in FlashlightState lightState) {
		BuildPerspectiveWorldToFlashlightMatrix(out Shadows[shadowHandle].Shadow.WorldToShadow, in lightState);

		g_ShadowMgr.UpdateFlashlightState(Shadows[shadowHandle].Shadow.ShadowHandle, in lightState);
	}

	public void DestroyFlashlight(ClientShadowHandle_t shadowHandle) => DestroyShadow(shadowHandle);

	public void UpdateProjectedTexture(ClientShadowHandle_t handle, bool force = false) {
		if (handle == CLIENTSHADOW_INVALID_HANDLE)
			return;

		ref ClientShadow_t shadow = ref Shadows[handle].Shadow;
		if ((shadow.Flags & (int)ShadowFlags.Flashlight) == 0) {
			Warning("CClientShadowMgr::UpdateProjectedTexture can only be used with flashlights!\n");
			return;
		}

		UpdateProjectedTextureInternal(handle, force);
		RemoveShadowFromDirtyList(handle);
	}

	public void ComputeBoundingSphere(IClientRenderable? renderable, out Vector3 origin, out float radius) {
		Assert(renderable != null);
		renderable!.GetShadowRenderBounds(out Vector3 mins, out Vector3 maxs, GetActualShadowCastType(renderable));
		MathLib.VectorSubtract(maxs, mins, out Vector3 size);
		radius = size.Length() * 0.5f;

		MathLib.VectorAdd(mins, maxs, out Vector3 centroid);
		centroid *= 0.5f;

		Span<Vector3> vec = stackalloc Vector3[3];
		MathLib.AngleVectors(renderable.GetRenderAngles(), out vec[0], out vec[1], out vec[2]);
		vec[1] *= -1.0f;

		MathLib.VectorCopy(renderable.GetRenderOrigin(), out origin);
		MathLib.VectorMA(origin, centroid.X, vec[0], out origin);
		MathLib.VectorMA(origin, centroid.Y, vec[1], out origin);
		MathLib.VectorMA(origin, centroid.Z, vec[2], out origin);
	}

	public void AddToDirtyShadowList(ClientShadowHandle_t handle, bool force = false) {
		if (UpdatingDirtyShadows)
			return;

		if (handle == CLIENTSHADOW_INVALID_HANDLE)
			return;

		Assert(!DirtyShadows.Contains(handle));
		DirtyShadows.Add(handle);

		if (force)
			Shadows[handle].Shadow.LastAngles = new(float.MaxValue, float.MaxValue, float.MaxValue);

		IClientRenderable? parent = GetParentShadowEntity(handle);
		if (parent != null)
			AddToDirtyShadowList(parent, force);
	}
	public void AddToDirtyShadowList(IClientRenderable? renderable, bool force = false) {
		if (UpdatingDirtyShadows)
			return;

		if (renderable!.IsShadowDirty())
			return;

		ClientShadowHandle_t handle = renderable.GetShadowHandle();
		if (handle == CLIENTSHADOW_INVALID_HANDLE)
			return;

#if DEBUG
		if (handle != CLIENTSHADOW_INVALID_HANDLE) {
			IClientRenderable? shadowRenderable = cl_entitylist.GetClientRenderableFromHandle(Shadows[handle].Shadow.Entity);
			Assert(renderable == shadowRenderable);
		}
#endif

		renderable.MarkShadowDirty(true);
		AddToDirtyShadowList(handle, force);
	}

	public void MarkRenderToTextureShadowDirty(ClientShadowHandle_t handle) {
		if (handle != CLIENTSHADOW_INVALID_HANDLE) {
			ref ClientShadow_t shadow = ref Shadows[handle].Shadow;
			shadow.Flags |= (ushort)ShadowFlags_t.TextureDirty;

			IClientRenderable? parent = GetParentShadowEntity(handle);
			if (parent != null) {
				ClientShadowHandle_t parentHandle = parent.GetShadowHandle();
				if (parentHandle != CLIENTSHADOW_INVALID_HANDLE)
					Shadows[parentHandle].Shadow.Flags |= (ushort)ShadowFlags_t.TextureDirty;
			}
		}
	}

	public void AddShadowToReceiver(ClientShadowHandle_t handle, IClientRenderable? renderable, ShadowReceiver type) {
		ref ClientShadow_t shadow = ref Shadows[handle].Shadow;

		IClientRenderable? sourceRenderable = cl_entitylist.GetClientRenderableFromHandle(shadow.Entity);

		if (sourceRenderable == renderable)
			return;

		if (!renderable!.ShouldReceiveProjectedTextures(ShadowFlags.ProjectedTextureTypeMask))
			return;

		if (CullReceiver(handle, renderable, sourceRenderable))
			return;

		switch (type) {
			case ShadowReceiver.BrushModel:
				if ((shadow.Flags & (int)ShadowFlags.Flashlight) != 0) {
					if (!shadow.TargetEntity.IsValid() || IsFlashlightTarget(handle, renderable)) {
						g_ShadowMgr.AddShadowToBrushModel(shadow.ShadowHandle, renderable.GetModel(), renderable.GetRenderOrigin(), renderable.GetRenderAngles());
						g_ShadowMgr.AddFlashlightRenderable(shadow.ShadowHandle, renderable);
					}
				}
				else
					g_ShadowMgr.AddShadowToBrushModel(shadow.ShadowHandle, renderable.GetModel(), renderable.GetRenderOrigin(), renderable.GetRenderAngles());
				break;

			case ShadowReceiver.StaticProp:
				if (GetActualShadowCastType(handle) == ShadowType.RenderToTexture) {
					C_BaseEntity? ent = sourceRenderable!.GetIClientUnknown()!.GetBaseEntity();
					if (ent != null && (ent.GetFlags() & (EntityFlags.NPC | EntityFlags.Client)) != 0)
						g_StaticPropMgr.AddShadowToStaticProp(shadow.ShadowHandle, renderable);
				}
				else if ((shadow.Flags & (int)ShadowFlags.Flashlight) != 0) {
					if (!shadow.TargetEntity.IsValid() || IsFlashlightTarget(handle, renderable)) {
						g_StaticPropMgr.AddShadowToStaticProp(shadow.ShadowHandle, renderable);
						g_ShadowMgr.AddFlashlightRenderable(shadow.ShadowHandle, renderable);
					}
				}
				break;

			case ShadowReceiver.StudioModel:
				if ((shadow.Flags & (int)ShadowFlags.Flashlight) != 0) {
					if (!shadow.TargetEntity.IsValid() || IsFlashlightTarget(handle, renderable)) {
						renderable.CreateModelInstance();
						g_ShadowMgr.AddShadowToModel(shadow.ShadowHandle, renderable.GetModelInstance());
						g_ShadowMgr.AddFlashlightRenderable(shadow.ShadowHandle, renderable);
					}
				}
				break;
		}
	}

	public void RemoveAllShadowsFromReceiver(IClientRenderable? renderable, ShadowReceiver type) {
		if (!renderable!.ShouldReceiveProjectedTextures(ShadowFlags.ProjectedTextureTypeMask))
			return;

		switch (type) {
			case ShadowReceiver.BrushModel:
				Model? model = renderable.GetModel();
				g_ShadowMgr.RemoveAllShadowsFromBrushModel(model);
				break;
			case ShadowReceiver.StaticProp:
				g_StaticPropMgr.RemoveAllShadowsFromStaticProp(renderable);
				break;
			case ShadowReceiver.StudioModel:
				if (renderable.GetModelInstance() != MODEL_INSTANCE_INVALID)
					g_ShadowMgr.RemoveAllShadowsFromModel(renderable.GetModelInstance());
				break;
		}
	}

	public void ComputeShadowTextures(in ViewSetup view, int leafCount, List<LeafIndex_t> leafList) {
		if (!RenderToTextureActive || r_shadows.GetInt() == 0 || r_shadows_gamecontrol.GetInt() == 0)
			return;

		Threaded = false;

		int count = s_VisibleShadowList.FindShadows(in view, leafCount, leafList);
		if (count == 0)
			return;

		using MatRenderContextPtr renderContext = new(materials);

		renderContext.ClearColor4ub(255, 255, 255, 0);

		MaterialHeightClipMode oldHeightClipMode = renderContext.GetHeightClipMode();
		renderContext.SetHeightClipMode(MaterialHeightClipMode.Disable);

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
		renderContext.Scale(1, -1, 1);
		renderContext.Ortho(0, 0, 1, 1, -9999, 0);

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PushMatrix();

		renderContext.PushRenderTargetAndViewport(ShadowAllocator.GetTexture());

		if (RenderTargetNeedsClear) {
			renderContext.ClearBuffers(true, false);
			RenderTargetNeedsClear = false;
		}

		int maxShadows = r_shadowmaxrendered.GetInt();
		int modelsRendered = 0;
		int i;

		for (i = 0; i < count; ++i) {
			ref readonly VisibleShadowInfo_t info = ref s_VisibleShadowList.GetVisibleShadow(i);
			if (modelsRendered < maxShadows) {
				if (DrawRenderToTextureShadow(info.Shadow, info.Area))
					++modelsRendered;
			}
			else
				DrawRenderToTextureShadowLOD(info.Shadow);
		}

		renderContext.PopRenderTargetAndViewport();

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PopMatrix();

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PopMatrix();

		renderContext.SetHeightClipMode(oldHeightClipMode);

		renderContext.SetHeightClipMode(oldHeightClipMode);

		renderContext.ClearColor3ub(0, 0, 0);
	}

	public void ComputeShadowDepthTextures(in ViewSetup view) => throw new NotImplementedException();

	public void FreeShadowDepthTextures() => throw new NotImplementedException();

	public ITexture? GetShadowTexture(ushort h) => ShadowAllocator.GetTexture();

	public ref readonly Source.Common.Engine.ShadowInfo_t GetShadowInfo(ClientShadowHandle_t h) => ref g_ShadowMgr.GetInfo(Shadows[h].Shadow.ShadowHandle);

	public void RenderShadowTexture(int w, int h) => throw new NotImplementedException();

	public void SetShadowDirection(in Vector3 dir) {
		MathLib.VectorCopy(dir, out SimpleShadowDir);
		MathLib.VectorNormalize(ref SimpleShadowDir);

		if (RenderToTextureActive)
			UpdateAllShadows();
	}

	static Vector3 s_vecDown = new(0, 0, -1);
	public ref readonly Vector3 GetShadowDirection() {
		if (!RenderToTextureActive)
			return ref s_vecDown;

		return ref SimpleShadowDir;
	}

	public void SetShadowColor(byte r, byte g, byte b) {
		float fr = r / 255.0f;
		float fg = g / 255.0f;
		float fb = b / 255.0f;

		SimpleShadow!.ColorModulate(fr, fg, fb);

		if (RenderToTextureActive) {
			RenderShadow!.ColorModulate(fr, fg, fb);
			RenderModelShadow!.ColorModulate(fr, fg, fb);
		}

		AmbientLightColor.R = r;
		AmbientLightColor.G = g;
		AmbientLightColor.B = b;
	}
	public void GetShadowColor(out byte r, out byte g, out byte b) {
		r = AmbientLightColor.R;
		g = AmbientLightColor.G;
		b = AmbientLightColor.B;
	}

	public void SetShadowDistance(float maxDistance) {
		ShadowCastDist = maxDistance;
		UpdateAllShadows();
	}
	public float GetShadowDistance() => ShadowCastDist;

	public void SetShadowBlobbyCutoffArea(float minArea) => MinShadowArea = minArea;
	public float GetBlobbyCutoffArea() => MinShadowArea;

	public void SetFalloffBias(ClientShadowHandle_t handle, byte bias) => g_ShadowMgr.SetFalloffBias(Shadows[handle].Shadow.ShadowHandle, bias);

	public void RestoreRenderState() {
		foreach (ClientShadowHandle_t h in ValidShadowHandles)
			Shadows[h].Shadow.Flags |= (ushort)ShadowFlags_t.TextureDirty;

		SetShadowColor(AmbientLightColor.R, AmbientLightColor.G, AmbientLightColor.B);
		RenderTargetNeedsClear = true;
	}

	public void ComputeShadowBBox(IClientRenderable? renderable, in Vector3 absCenter, float radius, out Vector3 absMins, out Vector3 absMaxs) {
		absMins = default;
		absMaxs = default;

		Vector3 shadowDir = GetShadowDirection(renderable);
		for (int i = 0; i < 3; ++i) {
			float shadowCastDistance = GetShadowDistance(renderable);
			float dist = shadowCastDistance * shadowDir[i];

			if (shadowDir[i] < 0) {
				absMins[i] = absCenter[i] - radius + dist;
				absMaxs[i] = absCenter[i] + radius;
			}
			else {
				absMins[i] = absCenter[i] - radius;
				absMaxs[i] = absCenter[i] + radius + dist;
			}
		}
	}

	public bool WillParentRenderBlobbyShadow(IClientRenderable? renderable) {
		if (renderable == null)
			return false;

		IClientRenderable? shadowParent = renderable.GetShadowParent();
		if (shadowParent == null)
			return false;

		ShadowType shadowType = GetActualShadowCastType(shadowParent);
		if (shadowType == ShadowType.None)
			return WillParentRenderBlobbyShadow(shadowParent);

		return shadowType == ShadowType.Simple;
	}

	public bool ShouldUseParentShadow(IClientRenderable? renderable) {
		if (renderable == null)
			return false;

		IClientRenderable? shadowParent = renderable.GetShadowParent();
		if (shadowParent == null)
			return false;

		ShadowType shadowType = GetActualShadowCastType(shadowParent);
		if (shadowType == ShadowType.Simple)
			return false;

		if (shadowType == ShadowType.None)
			return ShouldUseParentShadow(shadowParent);

		return true;
	}

	public void SetShadowsDisabled(bool disabled) => r_shadows_gamecontrol.SetValue(disabled != true ? 1 : 0);

	void UpdateStudioShadow(IClientRenderable? renderable, ClientShadowHandle_t handle) {
		if ((Shadows[handle].Shadow.Flags & (int)ShadowFlags.Flashlight) == 0) {
			ComputeHierarchicalBounds(renderable, out Vector3 mins, out Vector3 maxs);

			ShadowType shadowType = GetActualShadowCastType(handle);
			if (shadowType != ShadowType.RenderToTexture)
				BuildOrthoShadow(renderable, handle, mins, maxs);
			else
				BuildRenderToTextureShadow(renderable, handle, mins, maxs);
		}
		else
			BuildFlashlight(handle);
	}

	void UpdateBrushShadow(IClientRenderable? renderable, ClientShadowHandle_t handle) {
		if ((Shadows[handle].Shadow.Flags & (int)ShadowFlags.Flashlight) == 0) {
			ComputeHierarchicalBounds(renderable, out Vector3 mins, out Vector3 maxs);

			ShadowType shadowType = GetActualShadowCastType(handle);
			if (shadowType != ShadowType.RenderToTexture)
				BuildOrthoShadow(renderable, handle, mins, maxs);
			else
				BuildRenderToTextureShadow(renderable, handle, mins, maxs);
		}
		else
			BuildFlashlight(handle);
	}
	void UpdateShadow(ClientShadowHandle_t handle, bool force) {
		ref ClientShadow_t shadow = ref Shadows[handle].Shadow;

		IClientRenderable? renderable = cl_entitylist.GetClientRenderableFromHandle(shadow.Entity);
		if (renderable == null) {
			DestroyShadow(handle);
			return;
		}

		if (renderable.GetModel() == null) {
			renderable.MarkShadowDirty(false);
			return;
		}

		ref readonly Source.Common.Engine.ShadowInfo_t shadowInfo = ref g_ShadowMgr.GetInfo(shadow.ShadowHandle);
		if (shadowInfo.FalloffBias == 255) {
			g_ShadowMgr.EnableShadow(shadow.ShadowHandle, false);
			TransparentShadows.Add(handle);
			return;
		}

		if (ShouldUseParentShadow(renderable) || WillParentRenderBlobbyShadow(renderable)) {
			g_ShadowMgr.EnableShadow(shadow.ShadowHandle, false);
			renderable.MarkShadowDirty(false);
			return;
		}

		g_ShadowMgr.EnableShadow(shadow.ShadowHandle, true);

		ref readonly Vector3 origin = ref renderable.GetRenderOrigin();
		ref readonly QAngle angles = ref renderable.GetRenderAngles();

		if (force || (origin != shadow.LastOrigin) || (angles != shadow.LastAngles)) {
			MathLib.VectorCopy(origin, out shadow.LastOrigin);
			MathLib.VectorCopy(angles, out shadow.LastAngles);

			using MatRenderContextPtr renderContext = new(materials);
			Model? model = renderable.GetModel();
			// MaterialFogMode fogMode = renderContext.GetFogMode();
			// renderContext.FogMode(MaterialFogMode.None);
			switch (modelinfo.GetModelType(model)) {
				case ModelType.Brush:
					UpdateBrushShadow(renderable, handle);
					break;
				case ModelType.Studio:
					UpdateStudioShadow(renderable, handle);
					break;
				default:
					Assert(false);
					break;
			}
			// renderContext.FogMode(fogMode);
		}

		renderable.MarkShadowDirty(false);
	}

	IClientRenderable? GetParentShadowEntity(ClientShadowHandle_t handle) {
		ref ClientShadow_t shadow = ref Shadows[handle].Shadow;
		IClientRenderable? renderable = cl_entitylist.GetClientRenderableFromHandle(shadow.Entity);
		if (renderable != null) {
			if (ShouldUseParentShadow(renderable)) {
				IClientRenderable? parent = renderable.GetShadowParent();
				while (GetActualShadowCastType(parent) == ShadowType.None) {
					parent = parent!.GetShadowParent();
					Assert(parent != null);
				}
				return parent;
			}
		}
		return null;
	}

	void AddChildBounds(in Matrix3x4 matWorldToBBox, IClientRenderable? parent, ref Vector3 mins, ref Vector3 maxs) {
		IClientRenderable? child = parent!.FirstShadowChild();
		while (child != null) {
			if (GetActualShadowCastType(child) != ShadowType.None) {
				child.GetShadowRenderBounds(out Vector3 childMins, out Vector3 childMaxs, ShadowType.RenderToTexture);
				MathLib.ConcatTransforms(in matWorldToBBox, in child.RenderableToWorldTransform(), out Matrix3x4 childToBBox);
				MathLib.TransformAABB(in childToBBox, in childMins, in childMaxs, out Vector3 newChildMins, out Vector3 newChildMaxs);
				MathLib.VectorMin(mins, newChildMins, out mins);
				MathLib.VectorMax(maxs, newChildMaxs, out maxs);
			}

			AddChildBounds(in matWorldToBBox, child, ref mins, ref maxs);
			child = child.NextShadowPeer();
		}
	}

	void ComputeHierarchicalBounds(IClientRenderable? renderable, out Vector3 mins, out Vector3 maxs) {
		ShadowType shadowType = GetActualShadowCastType(renderable);

		renderable!.GetShadowRenderBounds(out mins, out maxs, shadowType);

		IClientRenderable? child = renderable.FirstShadowChild();

		if (child != null && shadowType != ShadowType.Simple) {
			MathLib.MatrixInvert(in renderable.RenderableToWorldTransform(), out Matrix3x4 matWorldToBBox);
			AddChildBounds(in matWorldToBBox, renderable, ref mins, ref maxs);
		}
	}

	void BuildGeneralWorldToShadowMatrix(out Matrix4x4 matWorldToShadow, in Vector3 origin, in Vector3 dir, in Vector3 xvec, in Vector3 yvec) {
		matWorldToShadow = default;
		MathLib.MatrixSetColumn(ref matWorldToShadow, 0, in xvec);
		MathLib.MatrixSetColumn(ref matWorldToShadow, 1, in yvec);
		MathLib.MatrixSetColumn(ref matWorldToShadow, 2, in dir);
		MathLib.MatrixSetColumn(ref matWorldToShadow, 3, in origin);
		matWorldToShadow[3, 0] = matWorldToShadow[3, 1] = matWorldToShadow[3, 2] = 0.0f;
		matWorldToShadow[3, 3] = 1.0f;

		MathLib.MatrixInverseGeneral(in matWorldToShadow, out matWorldToShadow);
	}

	static void BuildOrthoWorldToShadowMatrix(out Matrix4x4 worldToShadow, in Vector3 origin, in Vector3 dir, in Vector3 xvec, in Vector3 yvec) {
		Assert(MathF.Abs(MathLib.DotProduct(dir, xvec)) < 1e-3f);
		Assert(MathF.Abs(MathLib.DotProduct(dir, yvec)) < 1e-3f);
		Assert(MathF.Abs(MathLib.DotProduct(xvec, yvec)) < 1e-3f);

		worldToShadow = default;
		worldToShadow.SetBasisVectors(in xvec, in yvec, in dir);
		MathLib.MatrixTranspose(in worldToShadow, out worldToShadow);

		MathLib.Vector3DMultiply(in worldToShadow, in origin, out Vector3 translation);

		translation *= -1.0f;
		worldToShadow.SetTranslation(in translation);

		worldToShadow[3, 0] = worldToShadow[3, 1] = worldToShadow[3, 2] = 0.0f;
		worldToShadow[3, 3] = 1.0f;
	}

	static void BuildWorldToTextureMatrix(in Matrix4x4 matWorldToShadow, in Vector2 size, out Matrix4x4 matWorldToTexture) {
		MathLib.MatrixBuildScale(out Matrix4x4 shadowToUnit, 1.0f / size.X, 1.0f / size.Y, 1.0f);
		shadowToUnit[0, 3] = shadowToUnit[1, 3] = 0.5f;

		MathLib.MatrixMultiply(in shadowToUnit, in matWorldToShadow, out matWorldToTexture);
	}

	static void SortAbsVectorComponents(in Vector3 src, Span<int> vecIdx) {
		Vector3 absVec = new(MathF.Abs(src[0]), MathF.Abs(src[1]), MathF.Abs(src[2]));

		int maxIdx = (absVec[0] > absVec[1]) ? 0 : 1;
		if (absVec[2] > absVec[maxIdx])
			maxIdx = 2;

		switch (maxIdx) {
			case 0:
				vecIdx[0] = 1;
				vecIdx[1] = 2;
				vecIdx[2] = 0;
				break;
			case 1:
				vecIdx[0] = 2;
				vecIdx[1] = 0;
				vecIdx[2] = 1;
				break;
			case 2:
				vecIdx[0] = 0;
				vecIdx[1] = 1;
				vecIdx[2] = 2;
				break;
		}
	}

	void BuildWorldToShadowMatrix(out Matrix4x4 matWorldToShadow, in Vector3 origin, in Quaternion quatOrientation) {
		MathLib.QuaternionMatrix(in quatOrientation, out Matrix3x4 matOrientation);
		MathLib.PositionMatrix(in vec3_origin, ref matOrientation);

		Matrix4x4 matBasis = matOrientation;

		matBasis.GetBasisVectors(out Vector3 forward, out Vector3 left, out Vector3 up);
		matBasis.SetForward(in left);
		matBasis.SetLeft(in up);
		matBasis.SetUp(in forward);
		MathLib.MatrixTranspose(in matBasis, out matWorldToShadow);

		MathLib.Vector3DMultiply(in matWorldToShadow, in origin, out Vector3 translation);

		translation *= -1.0f;
		matWorldToShadow.SetTranslation(in translation);

		matWorldToShadow[3, 0] = matWorldToShadow[3, 1] = matWorldToShadow[3, 2] = 0.0f;
		matWorldToShadow[3, 3] = 1.0f;
	}

	void BuildPerspectiveWorldToFlashlightMatrix(out Matrix4x4 matWorldToShadow, in FlashlightState flashlightState) {
		BuildWorldToShadowMatrix(out Matrix4x4 matWorldToShadowView, in flashlightState.LightOrigin, in flashlightState.Orientation);

		MathLib.MatrixBuildPerspective(out Matrix4x4 matPerspective, flashlightState.HorizontalFOVDegrees,
			flashlightState.VerticalFOVDegrees,
			flashlightState.NearZ, flashlightState.FarZ);

		MathLib.MatrixMultiply(in matPerspective, in matWorldToShadowView, out matWorldToShadow);
	}

	void UpdateProjectedTextureInternal(ClientShadowHandle_t handle, bool force) {
		ref ClientShadow_t shadow = ref Shadows[handle].Shadow;

		if ((shadow.Flags & (int)ShadowFlags.Flashlight) != 0) {
			Assert((shadow.Flags & (int)ShadowFlags.Shadow) == 0);
			ref ClientShadow_t shadowClient = ref Shadows[handle].Shadow;

			g_ShadowMgr.EnableShadow(shadowClient.ShadowHandle, true);

			UpdateBrushShadow(null, handle);
		}
		else {
			Assert((shadow.Flags & (int)ShadowFlags.Shadow) != 0);
			Assert((shadow.Flags & (int)ShadowFlags.Flashlight) == 0);
			UpdateShadow(handle, force);
		}
	}

	float ComputeLocalShadowOrigin(IClientRenderable? renderable, in Vector3 mins, in Vector3 maxs, in Vector3 localShadowDir, float backupFactor, out Vector3 origin) {
		MathLib.VectorAdd(in mins, in maxs, out Vector3 centroid);
		centroid *= 0.5f;

		MathLib.VectorSubtract(in maxs, in mins, out Vector3 size);
		float radius = size.Length() * 0.5f;

		float centroidProjection = MathLib.DotProduct(centroid, localShadowDir);
		float minDist = -centroidProjection;
		for (int i = 0; i < 3; ++i) {
			if (localShadowDir[i] > 0.0f)
				minDist += localShadowDir[i] * mins[i];
			else
				minDist += localShadowDir[i] * maxs[i];
		}

		minDist *= backupFactor;

		MathLib.VectorMA(in centroid, minDist, in localShadowDir, out origin);

		return radius - minDist;
	}

	void RemoveShadowFromDirtyList(ClientShadowHandle_t handle) {
		if (DirtyShadows.Contains(handle)) {
			IClientRenderable? renderable = cl_entitylist.GetClientRenderableFromHandle(Shadows[handle].Shadow.Entity);
			renderable?.MarkShadowDirty(false);
			DirtyShadows.Remove(handle);
		}
	}

	internal ShadowType GetActualShadowCastType(ClientShadowHandle_t handle) {
		if (handle == CLIENTSHADOW_INVALID_HANDLE)
			return ShadowType.None;

		if ((Shadows[handle].Shadow.Flags & (int)ClientShadowFlags.UseRenderToTexture) != 0)
			return RenderToTextureActive ? ShadowType.RenderToTexture : ShadowType.Simple;
		else if ((Shadows[handle].Shadow.Flags & (int)ClientShadowFlags.UseDepthTexture) != 0)
			return ShadowType.RenderToDepthTexture;
		else
			return ShadowType.Simple;
	}
	ShadowType GetActualShadowCastType(IClientRenderable? renderable) => GetActualShadowCastType(renderable != null ? renderable.GetShadowHandle() : CLIENTSHADOW_INVALID_HANDLE);

	static void BuildShadowLeafList(ShadowLeafEnum shadowEnum, in Vector3 origin, in Vector3 dir, in Vector2 size, float maxDist) {
		Ray ray = default;
		MathLib.VectorCopy(origin, out ray.Start);
		MathLib.VectorMultiply(dir, maxDist, out ray.Delta);
		ray.StartOffset = new(0, 0, 0);

		float radius = MathF.Sqrt(size.X * size.X + size.Y * size.Y) * 0.5f;
		ray.Extents = new(radius, radius, radius);
		ray.IsRay = false;
		ray.IsSwept = true;

		ISpatialQuery query = engine.GetBSPTreeQuery()!;
		ISpatialLeafEnumerator queryRef = shadowEnum;
		query.EnumerateLeavesAlongRay(in ray, ref queryRef, 0);
	}

	void BuildOrthoShadow(IClientRenderable? renderable, ClientShadowHandle_t handle, in Vector3 mins, in Vector3 maxs) {
		Span<Vector3> vec = stackalloc Vector3[3];
		MathLib.AngleVectors(renderable!.GetRenderAngles(), out vec[0], out vec[1], out vec[2]);
		vec[1] *= -1.0f;

		Vector3 shadowDir = GetShadowDirection(renderable);

		Vector3 localShadowDir = default;
		localShadowDir[0] = MathLib.DotProduct(vec[0], shadowDir);
		localShadowDir[1] = MathLib.DotProduct(vec[1], shadowDir);
		localShadowDir[2] = MathLib.DotProduct(vec[2], shadowDir);

		Span<int> vecIdx = stackalloc int[3];
		SortAbsVectorComponents(in localShadowDir, vecIdx);

		Vector3 xvec = vec[vecIdx[0]];
		Vector3 yvec = vec[vecIdx[1]];

		xvec -= shadowDir * MathLib.DotProduct(shadowDir, xvec);
		yvec -= shadowDir * MathLib.DotProduct(shadowDir, yvec);
		MathLib.VectorNormalize(ref xvec);
		MathLib.VectorNormalize(ref yvec);

		MathLib.VectorSubtract(in maxs, in mins, out Vector3 boxSize);

		Vector2 size = new(boxSize[vecIdx[0]], boxSize[vecIdx[1]]);
		size.X *= MathF.Abs(MathLib.DotProduct(vec[vecIdx[0]], xvec));
		size.Y *= MathF.Abs(MathLib.DotProduct(vec[vecIdx[1]], yvec));

		size.X += boxSize[vecIdx[2]] * MathF.Abs(MathLib.DotProduct(vec[vecIdx[2]], xvec));
		size.Y += boxSize[vecIdx[2]] * MathF.Abs(MathLib.DotProduct(vec[vecIdx[2]], yvec));

		size.X += 10.0f;
		size.Y += 10.0f;

		MathLib.Vector2DMax(in size, new Vector2(10.0f, 10.0f), out size);

		float falloffStart = ComputeLocalShadowOrigin(renderable, in mins, in maxs, in localShadowDir, 2.0f, out Vector3 org);

		Vector3 worldOrigin = renderable.GetRenderOrigin();
		MathLib.VectorMA(in worldOrigin, org.X, vec[0], out worldOrigin);
		MathLib.VectorMA(in worldOrigin, org.Y, vec[1], out worldOrigin);
		MathLib.VectorMA(in worldOrigin, org.Z, vec[2], out worldOrigin);

		float dx = 1.0f / TEXEL_SIZE_PER_CASTER_SIZE;
		worldOrigin.X = (int)(worldOrigin.X / dx) * dx;
		worldOrigin.Y = (int)(worldOrigin.Y / dx) * dx;
		worldOrigin.Z = (int)(worldOrigin.Z / dx) * dx;

		BuildGeneralWorldToShadowMatrix(out Shadows[handle].Shadow.WorldToShadow, in worldOrigin, in shadowDir, in xvec, in yvec);
		BuildWorldToTextureMatrix(in Shadows[handle].Shadow.WorldToShadow, in size, out Matrix4x4 matWorldToTexture);
		MathLib.Vector2DCopy(in size, out Shadows[handle].Shadow.WorldSize);

		float shadowCastDistance = GetShadowDistance(renderable);
		float maxHeight = shadowCastDistance + falloffStart;

		ShadowLeafEnum leafList = new();
		BuildShadowLeafList(leafList, in worldOrigin, in shadowDir, in size, maxHeight);
		Span<int> pLeafList = leafList.LeafList.AsSpan();

		g_ShadowMgr.ProjectShadow(Shadows[handle].Shadow.ShadowHandle, in worldOrigin,
			in shadowDir, in matWorldToTexture, in size, pLeafList, maxHeight, falloffStart, MAX_FALLOFF_AMOUNT, renderable.GetRenderOrigin());

		clientLeafSystem.ProjectShadow(Shadows[handle].Shadow.ClientLeafShadowHandle, pLeafList.Length, pLeafList);
	}

	void BuildRenderToTextureShadow(IClientRenderable? renderable, ClientShadowHandle_t handle, in Vector3 mins, in Vector3 maxs) {
		if (DebugViewRender.cl_drawshadowtexture.GetInt() != 0)
			DrawRenderToTextureDebugInfo(renderable, in mins, in maxs);

		Span<Vector3> vec = stackalloc Vector3[3];
		MathLib.AngleVectors(renderable!.GetRenderAngles(), out vec[0], out vec[1], out vec[2]);
		vec[1] *= -1.0f;

		Vector3 shadowDir = GetShadowDirection(renderable);

		Vector3 localShadowDir = default;
		localShadowDir[0] = MathLib.DotProduct(vec[0], shadowDir);
		localShadowDir[1] = MathLib.DotProduct(vec[1], shadowDir);
		localShadowDir[2] = MathLib.DotProduct(vec[2], shadowDir);

		MathLib.VectorSubtract(in maxs, in mins, out Vector3 boxSize);

		Vector3 yvec = vec3_origin;
		float projMax = 0.0f;
		for (int i = 0; i < 3; ++i) {
			Vector3 test = vec[i] - shadowDir * MathLib.DotProduct(shadowDir, vec[i]);
			test *= boxSize[i];
			float lengthSqr = test.LengthSquared();
			if (lengthSqr > projMax) {
				projMax = lengthSqr;
				yvec = test;
			}
		}

		MathLib.VectorNormalize(ref yvec);

		MathLib.CrossProduct(in yvec, in shadowDir, out Vector3 xvec);

		Vector2 size;
		size.X = boxSize.X * MathF.Abs(MathLib.DotProduct(vec[0], xvec)) + boxSize.Y * MathF.Abs(MathLib.DotProduct(vec[1], xvec)) + boxSize.Z * MathF.Abs(MathLib.DotProduct(vec[2], xvec));
		size.Y = boxSize.X * MathF.Abs(MathLib.DotProduct(vec[0], yvec)) + boxSize.Y * MathF.Abs(MathLib.DotProduct(vec[1], yvec)) + boxSize.Z * MathF.Abs(MathLib.DotProduct(vec[2], yvec));

		size.X += 2.0f * TEXEL_SIZE_PER_CASTER_SIZE;
		size.Y += 2.0f * TEXEL_SIZE_PER_CASTER_SIZE;

		float falloffStart = ComputeLocalShadowOrigin(renderable, in mins, in maxs, in localShadowDir, 1.0f, out Vector3 org);

		Vector3 worldOrigin = renderable.GetRenderOrigin();
		MathLib.VectorMA(in worldOrigin, org.X, vec[0], out worldOrigin);
		MathLib.VectorMA(in worldOrigin, org.Y, vec[1], out worldOrigin);
		MathLib.VectorMA(in worldOrigin, org.Z, vec[2], out worldOrigin);

		BuildOrthoWorldToShadowMatrix(out Shadows[handle].Shadow.WorldToShadow, in worldOrigin, in shadowDir, in xvec, in yvec);
		BuildWorldToTextureMatrix(in Shadows[handle].Shadow.WorldToShadow, in size, out Matrix4x4 matWorldToTexture);
		MathLib.Vector2DCopy(in size, out Shadows[handle].Shadow.WorldSize);

		float shadowCastDistance = GetShadowDistance(renderable);
		float maxHeight = shadowCastDistance + falloffStart;

		ShadowLeafEnum leafList = new();
		BuildShadowLeafList(leafList, in worldOrigin, in shadowDir, in size, maxHeight);
		Span<int> pLeafList = leafList.LeafList.AsSpan();

		g_ShadowMgr.ProjectShadow(Shadows[handle].Shadow.ShadowHandle, in worldOrigin, in shadowDir, in matWorldToTexture, in size, pLeafList, maxHeight, falloffStart, MAX_FALLOFF_AMOUNT, renderable.GetRenderOrigin());

		ComputeExtraClipPlanes(renderable, handle, vec, in mins, in maxs, in localShadowDir);

		clientLeafSystem.ProjectShadow(Shadows[handle].Shadow.ClientLeafShadowHandle, pLeafList.Length, pLeafList);
	}

	static void BuildFlashlightLeafList(ShadowLeafEnum shadowEnum, in Matrix4x4 worldToShadow) {
		MathLib.CalculateAABBFromProjectionMatrix(in worldToShadow, out Vector3 mins, out Vector3 maxs);
		ISpatialQuery query = engine.GetBSPTreeQuery()!;
		ISpatialLeafEnumerator queryRef = shadowEnum;
		query.EnumerateLeavesInBox(in mins, in maxs, ref queryRef, 0);
	}

	void BuildFlashlight(ClientShadowHandle_t handle) {
		ref ClientShadow_t shadow = ref Shadows[handle].Shadow;
		if (r_flashlight_version2.GetInt() != 0) {
			g_ShadowMgr.ProjectFlashlight(shadow.ShadowHandle, in shadow.WorldToShadow, default);
			return;
		}

		bool lightModels = r_flashlightmodels.GetBool();
		bool lightSpecificEntity = shadow.TargetEntity.Get() != null;
		bool lightWorld = (shadow.Flags & (int)ShadowFlags_t.LightWorld) != 0;
		int count = 0;
		ReadOnlySpan<int> leafList = default;

		ShadowLeafEnum leafEnum = new();
		if (lightWorld || (lightModels && !lightSpecificEntity)) {
			BuildFlashlightLeafList(leafEnum, in shadow.WorldToShadow);
			count = leafEnum.LeafList.Count;
			leafList = leafEnum.LeafList.AsSpan();
		}

		if (lightWorld)
			g_ShadowMgr.ProjectFlashlight(shadow.ShadowHandle, in shadow.WorldToShadow, leafList);
		else {
			g_ShadowMgr.EnableShadow(shadow.ShadowHandle, false);
			g_ShadowMgr.EnableShadow(shadow.ShadowHandle, true);
		}

		if (!lightModels)
			return;

		if (!lightSpecificEntity) {
			clientLeafSystem.ProjectFlashlight(shadow.ClientLeafShadowHandle, count, leafList);
			return;
		}

		Assert(shadow.TargetEntity.Get()!.GetModel() != null);

		C_BaseEntity? child = shadow.TargetEntity.Get()!.FirstMoveChild();
		while (child != null) {
			ModelType modelType = modelinfo.GetModelType(child.GetModel());
			if (modelType == ModelType.Brush)
				AddShadowToReceiver(handle, child, ShadowReceiver.BrushModel);
			else if (modelType == ModelType.Studio)
				AddShadowToReceiver(handle, child, ShadowReceiver.StudioModel);

			child = child.NextMovePeer();
		}

		ModelType targetModelType = modelinfo.GetModelType(shadow.TargetEntity.Get()!.GetModel());
		if (targetModelType == ModelType.Brush)
			AddShadowToReceiver(handle, shadow.TargetEntity.Get(), ShadowReceiver.BrushModel);
		else if (targetModelType == ModelType.Studio)
			AddShadowToReceiver(handle, shadow.TargetEntity.Get(), ShadowReceiver.StudioModel);
	}

	void SetupRenderToTextureShadow(ClientShadowHandle_t h) {
		ref ClientShadow_t shadow = ref Shadows[h].Shadow;

		IClientRenderable? renderable = cl_entitylist.GetClientRenderableFromHandle(shadow.Entity);
		if (renderable == null)
			return;

		renderable.GetShadowRenderBounds(out Vector3 mins, out Vector3 maxs, GetActualShadowCastType(h));

		MathLib.VectorSubtract(maxs, mins, out Vector3 size);
		float maxSize = Math.Max(size.X, size.Y);
		maxSize = Math.Max(maxSize, size.Z);

		float texelCount = TEXEL_SIZE_PER_CASTER_SIZE * maxSize;

		int textureSize = 1;
		while (textureSize < texelCount)
			textureSize <<= 1;

		shadow.ShadowTexture = ShadowAllocator.AllocateTexture(textureSize, textureSize);
	}
	void CleanUpRenderToTextureShadow(ClientShadowHandle_t h) {
		ref ClientShadow_t shadow = ref Shadows[h].Shadow;
		if (RenderToTextureActive && (shadow.Flags & (int)ClientShadowFlags.UseRenderToTexture) != 0) {
			ShadowAllocator.DeallocateTexture(shadow.ShadowTexture);
			shadow.ShadowTexture = INVALID_TEXTURE_HANDLE;
		}
	}

	void ComputeExtraClipPlanes(IClientRenderable? renderable, ClientShadowHandle_t handle, ReadOnlySpan<Vector3> vec, in Vector3 mins, in Vector3 maxs, in Vector3 localShadowDir) {
		Vector3 origin = renderable!.GetRenderOrigin();
		Span<float> dir = stackalloc float[3];

		int i;
		for (i = 0; i < 3; ++i) {
			if (localShadowDir[i] < 0.0f) {
				MathLib.VectorMA(in origin, maxs[i], vec[i], out origin);
				dir[i] = 1;
			}
			else {
				MathLib.VectorMA(in origin, mins[i], vec[i], out origin);
				dir[i] = -1;
			}
		}

		Vector3 normal = default;
		ClearExtraClipPlanes(handle);
		for (i = 0; i < 3; ++i) {
			MathLib.VectorMultiply(vec[i], dir[i], out normal);
			float dist = MathLib.DotProduct(normal, origin);
			AddExtraClipPlane(handle, in normal, dist);
		}

		ref ClientShadow_t shadow = ref Shadows[handle].Shadow;
		C_BaseEntity? entity = cl_entitylist.GetBaseEntityFromHandle(shadow.Entity);
		if (entity != null && entity.EnableRenderingClipPlane) {
			normal[0] = -entity.RenderingClipPlane[0];
			normal[1] = -entity.RenderingClipPlane[1];
			normal[2] = -entity.RenderingClipPlane[2];
			AddExtraClipPlane(handle, in normal, -entity.RenderingClipPlane[3] - 0.5f);
		}
	}

	void ClearExtraClipPlanes(ClientShadowHandle_t h) => g_ShadowMgr.ClearExtraClipPlanes(Shadows[h].Shadow.ShadowHandle);
	void AddExtraClipPlane(ClientShadowHandle_t h, in Vector3 normal, float dist) => g_ShadowMgr.AddExtraClipPlane(Shadows[h].Shadow.ShadowHandle, in normal, dist);

	bool CullReceiver(ClientShadowHandle_t handle, IClientRenderable? renderable, IClientRenderable? sourceRenderable) {
		if ((Shadows[handle].Shadow.Flags & (int)ShadowFlags.Flashlight) != 0) {
			Assert(sourceRenderable == null);
			Frustum_t frustum = g_ShadowMgr.GetFlashlightFrustum(Shadows[handle].Shadow.ShadowHandle);

			renderable!.GetRenderBoundsWorldspace(out Vector3 mins, out Vector3 maxs);

			return MathLib.R_CullBox(mins, maxs, frustum);
		}

		Assert(sourceRenderable != null);
		ComputeBoundingSphere(renderable, out Vector3 origin, out float radius);

		ref ClientShadow_t shadow = ref Shadows[handle].Shadow;
		ref readonly Source.Common.Engine.ShadowInfo_t info = ref g_ShadowMgr.GetInfo(shadow.ShadowHandle);
		MathLib.Vector3DMultiplyPosition(shadow.WorldToShadow, origin, out Vector3 localOrigin);

		Vector3 shadowMin = new(-shadow.WorldSize.X * 0.5f, -shadow.WorldSize.Y * 0.5f, 0);
		Vector3 shadowMax = new(shadow.WorldSize.X * 0.5f, shadow.WorldSize.Y * 0.5f, info.MaxDist);

		if (!CollisionUtils.IsBoxIntersectingSphere(shadowMin, shadowMax, localOrigin, radius))
			return true;

		ComputeBoundingSphere(sourceRenderable, out Vector3 originSource, out float radiusSource);

		bool foundSeparatingPlane;
		CollisionPlane plane;
		if (!CollisionUtils.IsSphereIntersectingSphere(originSource, radiusSource, origin, radius)) {
			foundSeparatingPlane = true;
			plane = default;

			MathLib.VectorSubtract(origin, originSource, out plane.Normal);
		}
		else
			foundSeparatingPlane = ComputeSeparatingPlane(renderable, sourceRenderable, out plane);

		if (foundSeparatingPlane) {
			Vector3 shadowDir = GetShadowDirection(sourceRenderable);
			float shadowDot = MathLib.DotProduct(shadowDir, plane.Normal);
			float receiverDot = MathLib.DotProduct(plane.Normal, origin);
			float sourceDot = MathLib.DotProduct(plane.Normal, originSource);

			if (shadowDot > 0.0f) {
				if (receiverDot <= sourceDot)
					return true;
			}
			else {
				if (receiverDot >= sourceDot)
					return true;
			}
		}

		return false;
	}

	bool ComputeSeparatingPlane(IClientRenderable? rend1, IClientRenderable? rend2, out CollisionPlane plane) {
		rend1!.GetShadowRenderBounds(out Vector3 min1, out Vector3 max1, GetActualShadowCastType(rend1));
		rend2!.GetShadowRenderBounds(out Vector3 min2, out Vector3 max2, GetActualShadowCastType(rend2));
		return CollisionUtils.ComputeSeparatingPlane(rend1.GetRenderOrigin(), rend1.GetRenderAngles(), min1, max1, rend2.GetRenderOrigin(), rend2.GetRenderAngles(), min2, max2, 3.0f, out plane);
	}

	void UpdateAllShadows() {
		foreach (ClientShadowHandle_t i in ValidShadowHandles) {
			ref ClientShadow_t shadow = ref Shadows[i].Shadow;

			if ((shadow.Flags & (int)ShadowFlags.Flashlight) != 0)
				continue;

			IClientRenderable? renderable = cl_entitylist.GetClientRenderableFromHandle(shadow.Entity);
			if (renderable == null)
				continue;

			Assert(renderable.GetShadowHandle() == i);
			UpdateProjectedTextureInternal(i, false);
		}
	}

	bool DrawRenderToTextureShadow(ushort clientShadowHandle, float area) {
		ref ClientShadow_t shadow = ref Shadows[clientShadowHandle].Shadow;

		bool previouslyUsingLODShadow = (shadow.Flags & (int)ShadowFlags_t.UsingLodShadow) != 0;
		shadow.Flags &= unchecked((ushort)~(int)ShadowFlags_t.UsingLodShadow);
		if (previouslyUsingLODShadow)
			g_ShadowMgr.SetShadowMaterial(shadow.ShadowHandle, RenderShadow, RenderModelShadow, clientShadowHandle);

		bool dirtyTexture = (shadow.Flags & (int)ShadowFlags_t.TextureDirty) != 0;
		bool drewTexture = false;
		bool needsRedraw = !Threaded && ShadowAllocator.UseTexture(shadow.ShadowTexture, dirtyTexture, area);

		if (!ShadowAllocator.HasValidTexture(shadow.ShadowTexture)) {
			DrawRenderToTextureShadowLOD(clientShadowHandle);
			return false;
		}

		if (needsRedraw || dirtyTexture) {
			IClientRenderable? renderable = cl_entitylist.GetClientRenderableFromHandle(shadow.Entity);

			using MatRenderContextPtr renderContext = new(materials);

			ShadowAllocator.GetTextureRect(shadow.ShadowTexture, out int x, out int y, out int w, out int h);
			renderContext.Viewport(x, y, w, h);

			renderContext.ClearBuffers(true, false);

			renderContext.MatrixMode(MaterialMatrixMode.View);
			renderContext.LoadMatrix(g_ShadowMgr.GetInfo(shadow.ShadowHandle).WorldToShadow);

			if (DrawShadowHierarchy(renderable, in shadow))
				drewTexture = true;
			else
				DevMsg("Didn't draw shadow hierarchy.. bad shadow texcoords probably going to happen..grab Brian!\n");

			if ((shadow.Flags & (int)ClientShadowFlags.AnimatingSource) == 0)
				shadow.Flags &= unchecked((ushort)~(int)ShadowFlags_t.TextureDirty);

			SetRenderToTextureShadowTexCoords(shadow.ShadowHandle, x, y, w, h);
		}
		else if (previouslyUsingLODShadow) {
			ShadowAllocator.GetTextureRect(shadow.ShadowTexture, out int x, out int y, out int w, out int h);
			SetRenderToTextureShadowTexCoords(shadow.ShadowHandle, x, y, w, h);
		}

		return drewTexture;
	}
	void DrawRenderToTextureShadowLOD(ushort clientShadowHandle) {
		ref ClientShadow_t shadow = ref Shadows[clientShadowHandle].Shadow;
		if ((shadow.Flags & (int)ShadowFlags_t.UsingLodShadow) == 0) {
			g_ShadowMgr.SetShadowMaterial(shadow.ShadowHandle, SimpleShadow, SimpleShadow, CLIENTSHADOW_INVALID_HANDLE);
			g_ShadowMgr.SetShadowTexCoord(shadow.ShadowHandle, 0, 0, 1, 1);
			ClearExtraClipPlanes(clientShadowHandle);
			shadow.Flags |= (ushort)ShadowFlags_t.UsingLodShadow;
		}
	}

	bool DrawShadowHierarchy(IClientRenderable? renderable, in ClientShadow_t shadow, bool child = false) {
		bool drewTexture = false;

		ShadowType shadowType = GetActualShadowCastType(renderable);
		if (renderable != null && shadowType == ShadowType.Simple)
			return false;

		if (renderable == null || shadowType != ShadowType.None) {
			bool drawModelShadow;
			bool drawBrushShadow;
			if (!child) {
				drawModelShadow = (shadow.Flags & (int)ShadowFlags_t.BrushModel) == 0;
				drawBrushShadow = !drawModelShadow;
			}
			else {
				ModelType modelType = modelinfo.GetModelType(renderable!.GetModel());
				drawModelShadow = modelType == ModelType.Studio;
				drawBrushShadow = modelType == ModelType.Brush;
			}

			if (drawModelShadow) {
				DrawModelInfo info = default;
				if (modelrender.DrawModelShadowSetup(renderable!, renderable!.GetBody(), renderable.GetSkin(), ref info, default, out Span<Matrix3x4> boneToWorld))
					modelrender.DrawModelShadow(renderable, in info, boneToWorld);
				drewTexture = true;
			}
			else if (drawBrushShadow) {
				render.DrawBrushModelShadow(renderable!);
				drewTexture = true;
			}
		}

		if (renderable == null)
			return drewTexture;

		for (IClientRenderable? pChild = renderable.FirstShadowChild(); pChild != null; pChild = pChild.NextShadowPeer()) {
			if (DrawShadowHierarchy(pChild, in shadow, true))
				drewTexture = true;
		}
		return drewTexture;
	}

	bool BuildSetupListForRenderToTextureShadow(ushort clientShadowHandle, float area) {
		ref ClientShadow_t shadow = ref Shadows[clientShadowHandle].Shadow;
		bool dirtyTexture = (shadow.Flags & (int)ShadowFlags_t.TextureDirty) != 0;
		bool needsRedraw = ShadowAllocator.UseTexture(shadow.ShadowTexture, dirtyTexture, area);
		if (needsRedraw || dirtyTexture) {
			shadow.Flags |= (ushort)ShadowFlags_t.TextureDirty;

			if (!ShadowAllocator.HasValidTexture(shadow.ShadowTexture))
				return false;

			IClientRenderable? renderable = cl_entitylist.GetClientRenderableFromHandle(shadow.Entity);

			if (BuildSetupShadowHierarchy(renderable, in shadow))
				return true;
		}
		return false;
	}

	bool BuildSetupShadowHierarchy(IClientRenderable? renderable, in ClientShadow_t shadow, bool child = false) {
		bool drewTexture = false;

		ShadowType shadowType = GetActualShadowCastType(renderable);
		if (renderable != null && shadowType == ShadowType.Simple)
			return false;

		if (renderable == null || shadowType != ShadowType.None) {
			bool drawModelShadow;
			if (!child) {
				drawModelShadow = (shadow.Flags & (int)ShadowFlags_t.BrushModel) == 0;
			}
			else {
				ModelType modelType = modelinfo.GetModelType(renderable!.GetModel());
				drawModelShadow = modelType == ModelType.Studio;
			}

			if (drawModelShadow) {
				C_BaseEntity? entity = renderable?.GetIClientUnknown()?.GetBaseEntity();
				if (entity != null) {
					if (entity.IsNPC())
						s_NPCShadowBoneSetups.Add((C_BaseAnimating)entity);
					else if (entity.GetBaseAnimating() != null)
						s_NonNPCShadowBoneSetups.Add((C_BaseAnimating)entity);
				}
				drewTexture = true;
			}
		}

		if (renderable == null)
			return drewTexture;

		for (IClientRenderable? pChild = renderable.FirstShadowChild(); pChild != null; pChild = pChild.NextShadowPeer()) {
			if (BuildSetupShadowHierarchy(pChild, in shadow, true))
				drewTexture = true;
		}
		return drewTexture;
	}

	void SetRenderToTextureShadowTexCoords(ShadowHandle_t handle, int x, int y, int w, int h) {
		ShadowAllocator.GetTotalTextureSize(out int textureW, out int textureH);

		float u, v, du, dv;

		u = ((float)x + 0.5f) / (float)textureW;
		v = ((float)y + 0.5f) / (float)textureH;
		du = ((float)w - 1) / (float)textureW;
		dv = ((float)h - 1) / (float)textureH;

		g_ShadowMgr.SetShadowTexCoord(handle, u, v, du, dv);
	}

	void DrawRenderToTextureDebugInfo(IClientRenderable? renderable, in Vector3 mins, in Vector3 maxs) {
		if (debugoverlay == null)
			return;

		Span<Vector3> vec = stackalloc Vector3[3];
		MathLib.AngleVectors(renderable!.GetRenderAngles(), out vec[0], out vec[1], out vec[2]);
		vec[1] *= -1.0f;

		MathLib.VectorSubtract(in maxs, in mins, out Vector3 size);

		Vector3 origin = renderable.GetRenderOrigin();

		MathLib.VectorMA(in origin, mins.X, vec[0], out Vector3 start);
		MathLib.VectorMA(in start, mins.Y, vec[1], out start);
		MathLib.VectorMA(in start, mins.Z, vec[2], out start);

		MathLib.VectorMA(in start, size.X, vec[0], out Vector3 end);
		MathLib.VectorMA(in end, size.Z, vec[2], out Vector3 end2);
		debugoverlay.AddLineOverlay(in start, in end, 255, 0, 0, true, 0.01f);
		debugoverlay.AddLineOverlay(in end2, in end, 255, 0, 0, true, 0.01f);

		MathLib.VectorMA(in start, size.Y, vec[1], out end);
		MathLib.VectorMA(in end, size.Z, vec[2], out end2);
		debugoverlay.AddLineOverlay(in start, in end, 255, 0, 0, true, 0.01f);
		debugoverlay.AddLineOverlay(in end2, in end, 255, 0, 0, true, 0.01f);

		MathLib.VectorMA(in start, size.Z, vec[2], out end);
		debugoverlay.AddLineOverlay(in start, in end, 255, 0, 0, true, 0.01f);

		start = end;
		MathLib.VectorMA(in start, size.X, vec[0], out end);
		debugoverlay.AddLineOverlay(in start, in end, 255, 0, 0, true, 0.01f);

		MathLib.VectorMA(in start, size.Y, vec[1], out end);
		debugoverlay.AddLineOverlay(in start, in end, 255, 0, 0, true, 0.01f);

		MathLib.VectorMA(in end, size.X, vec[0], out start);
		MathLib.VectorMA(in start, -size.X, vec[0], out end);
		debugoverlay.AddLineOverlay(in start, in end, 255, 0, 0, true, 0.01f);

		MathLib.VectorMA(in start, -size.Y, vec[1], out end);
		debugoverlay.AddLineOverlay(in start, in end, 255, 0, 0, true, 0.01f);

		MathLib.VectorMA(in start, -size.Z, vec[2], out end);
		debugoverlay.AddLineOverlay(in start, in end, 255, 0, 0, true, 0.01f);

		start = end;
		MathLib.VectorMA(in start, -size.X, vec[0], out end);
		debugoverlay.AddLineOverlay(in start, in end, 255, 0, 0, true, 0.01f);

		MathLib.VectorMA(in start, -size.Y, vec[1], out end);
		debugoverlay.AddLineOverlay(in start, in end, 255, 0, 0, true, 0.01f);

		C_BaseEntity? ent = renderable.GetIClientUnknown()?.GetBaseEntity();
		if (ent != null)
			debugoverlay.AddTextOverlay(in origin, 0, $"{ent.EntIndex()}");
		else
			debugoverlay.AddTextOverlay(in origin, 0, $"{renderable}");
	}

	public void AdvanceFrame() => ShadowAllocator.AdvanceFrame();

	float GetShadowDistance(IClientRenderable? renderable) {
		float dist = ShadowCastDist;

		renderable!.GetShadowCastDistance(ref dist, GetActualShadowCastType(renderable));

		return dist;
	}

	Vector3 GetShadowDirection(IClientRenderable? renderable) {
		Vector3 result = GetShadowDirection();

		renderable!.GetShadowCastDirection(ref result, GetActualShadowCastType(renderable));

		return result;
	}

	void InitDepthTextureShadows() {
		if (!DepthTextureActive) {
			DepthTextureActive = true;

			ImageFormat dstFormat = materials.GetShadowDepthTextureFormat();
			ImageFormat nullFormat = materials.GetNullTextureFormat();

			materials.BeginRenderTargetAllocation();

			DummyColorTexture = InitRenderTarget(r_flashlightdepthres.GetInt(), r_flashlightdepthres.GetInt(), RenderTargetSizeMode.Offscreen, nullFormat, MaterialRenderTargetDepth.None, false, "_rt_ShadowDummy");

			DepthTextureCache.Clear();
			DepthTextureCacheLocks.Clear();
			Span<char> strRTName = stackalloc char[64];
			for (int i = 0; i < MaxDepthTextureShadows; i++) {
				sprintf(strRTName, "_rt_ShadowDepthTexture_%d").D(i);

				ITexture? depthTex = InitRenderTarget(DepthTextureResolution, DepthTextureResolution, RenderTargetSizeMode.Offscreen, dstFormat, MaterialRenderTargetDepth.None, false, strRTName.SliceNullTerminatedString());

				if (i == 0) {
					DepthTextureResolution = depthTex!.GetActualWidth();
					r_flashlightdepthres.SetValue(DepthTextureResolution);
				}

				DepthTextureCache.Add(depthTex);
				DepthTextureCacheLocks.Add(false);
			}

			materials.EndRenderTargetAllocation();
		}
	}

	static ITexture? InitRenderTarget(int w, int h, RenderTargetSizeMode sizeMode, ImageFormat fmt, MaterialRenderTargetDepth depth, bool hdr, ReadOnlySpan<char> strOptionalName) {
		TextureFlags textureFlags = TextureFlags.ClampS | TextureFlags.ClampT;
		if (depth == MaterialRenderTargetDepth.Only)
			textureFlags |= TextureFlags.PointSample;

		CreateRenderTargetFlags renderTargetFlags = hdr ? CreateRenderTargetFlags.HDR : 0;

		ITexture? texture = materials.CreateNamedRenderTargetTextureEx(strOptionalName, w, h, sizeMode, fmt, depth, textureFlags, renderTargetFlags);

		Assert(texture != null);
		return texture;
	}

	void ShutdownDepthTextureShadows() {
		if (DepthTextureActive) {
			DummyColorTexture = null;

			while (DepthTextureCache.Count != 0) {
				DepthTextureCacheLocks.RemoveAt(DepthTextureCache.Count - 1);
				DepthTextureCache.RemoveAt(DepthTextureCache.Count - 1);
			}

			DepthTextureActive = false;
		}
	}

	void InitRenderToTextureShadows() {
		if (!RenderToTextureActive) {
			RenderToTextureActive = true;
			RenderShadow = materials.FindMaterial("decals/rendershadow", MaterialDefines.TEXTURE_GROUP_DECAL);
			RenderModelShadow = materials.FindMaterial("decals/rendermodelshadow", MaterialDefines.TEXTURE_GROUP_DECAL);
			ShadowAllocator.Init();

			ShadowAllocator.Reset();
			RenderTargetNeedsClear = true;

			float fr = AmbientLightColor.R / 255.0f;
			float fg = AmbientLightColor.G / 255.0f;
			float fb = AmbientLightColor.B / 255.0f;
			RenderShadow!.ColorModulate(fr, fg, fb);
			RenderModelShadow!.ColorModulate(fr, fg, fb);

			foreach (ClientShadowHandle_t i in ValidShadowHandles) {
				ref ClientShadow_t shadow = ref Shadows[i].Shadow;
				if ((shadow.Flags & (int)ClientShadowFlags.UseRenderToTexture) != 0) {
					SetupRenderToTextureShadow(i);
					MarkRenderToTextureShadowDirty(i);

					g_ShadowMgr.SetShadowMaterial(shadow.ShadowHandle, RenderShadow, RenderModelShadow, i);
				}
			}
		}
	}

	void ShutdownRenderToTextureShadows() {
		if (RenderToTextureActive) {
			foreach (ClientShadowHandle_t i in ValidShadowHandles) {
				CleanUpRenderToTextureShadow(i);

				ref ClientShadow_t shadow = ref Shadows[i].Shadow;
				g_ShadowMgr.SetShadowMaterial(shadow.ShadowHandle, SimpleShadow, SimpleShadow, CLIENTSHADOW_INVALID_HANDLE);
				g_ShadowMgr.SetShadowTexCoord(shadow.ShadowHandle, 0, 0, 1, 1);
				ClearExtraClipPlanes(i);
			}

			RenderShadow = null;
			RenderModelShadow = null;

			ShadowAllocator.DeallocateAllTextures();
			ShadowAllocator.Shutdown();

			// materials.UncacheUnusedMaterials();

			RenderToTextureActive = false;
		}
	}

	static bool ShadowHandleCompareFunc(ClientShadowHandle_t lhs, ClientShadowHandle_t rhs) => lhs < rhs;

	ClientShadowHandle_t CreateProjectedTexture(ClientEntityHandle entity, int flags) {
		if ((flags & (int)ShadowFlags.Flashlight) == 0) {
			IClientRenderable? renderable = cl_entitylist.GetClientRenderableFromHandle(entity);
			if (renderable == null)
				return CLIENTSHADOW_INVALID_HANDLE;

			ModelType modelType = modelinfo.GetModelType(renderable.GetModel());
			if (modelType == ModelType.Brush)
				flags |= (int)ShadowFlags_t.BrushModel;
		}

		while (curShadowHandleIdx == CLIENTSHADOW_INVALID_HANDLE || Shadows.ContainsKey(curShadowHandleIdx))
			curShadowHandleIdx++;

		ClientShadowHandle_t h = curShadowHandleIdx++;
		Shadows[h] = new();
		ValidShadowHandles.Add(h);
		ref ClientShadow_t shadow = ref Shadows[h].Shadow;
		shadow.Entity = entity;
		shadow.ClientLeafShadowHandle = clientLeafSystem.AddShadow(h, (ushort)flags);
		shadow.Flags = (ushort)flags;
		shadow.RenderFrame = -1;
		shadow.LastOrigin = new(float.MaxValue, float.MaxValue, float.MaxValue);
		shadow.LastAngles = new(float.MaxValue, float.MaxValue, float.MaxValue);
		Assert((shadow.Flags & (int)ShadowFlags.Flashlight) == 0 != ((shadow.Flags & (int)ShadowFlags.Shadow) == 0));

		IMaterial? shadowMaterial = SimpleShadow;
		IMaterial? shadowModelMaterial = SimpleShadow;
		object? shadowProxyData = CLIENTSHADOW_INVALID_HANDLE;

		if (RenderToTextureActive && (flags & (int)ClientShadowFlags.UseRenderToTexture) != 0) {
			SetupRenderToTextureShadow(h);

			shadowMaterial = RenderShadow;
			shadowModelMaterial = RenderModelShadow;
			shadowProxyData = h;
		}

		if ((flags & (int)ClientShadowFlags.UseDepthTexture) != 0) {
			shadowMaterial = RenderShadow;
			shadowModelMaterial = RenderModelShadow;
			shadowProxyData = h;
		}

		ShadowCreateFlags createShadowFlags;
		if ((flags & (int)ShadowFlags.Flashlight) != 0)
			createShadowFlags = ShadowCreateFlags.Flashlight;
		else
			createShadowFlags = ShadowCreateFlags.CacheVerts;

		shadow.ShadowHandle = g_ShadowMgr.CreateShadowEx(shadowMaterial, shadowModelMaterial, shadowProxyData, (int)createShadowFlags);
		return h;
	}

	bool LockShadowDepthTexture(ref ITexture? shadowDepthTexture) => throw new NotImplementedException();
	public void UnlockAllShadowDepthTextures() => throw new NotImplementedException();

	public void SetFlashlightTarget(ClientShadowHandle_t shadowHandle, EHANDLE targetEntity) {
		Assert(Shadows.ContainsKey(shadowHandle));

		ref ClientShadow_t shadow = ref Shadows[shadowHandle].Shadow;
		if ((shadow.Flags & (int)ShadowFlags.Flashlight) == 0)
			return;

		shadow.TargetEntity = targetEntity;
	}

	public void SetFlashlightLightWorld(ClientShadowHandle_t shadowHandle, bool lightWorld) {
		Assert(Shadows.ContainsKey(shadowHandle));

		ref ClientShadow_t shadow = ref Shadows[shadowHandle].Shadow;
		if ((shadow.Flags & (int)ShadowFlags.Flashlight) == 0)
			return;

		if (lightWorld)
			shadow.Flags |= (ushort)ShadowFlags_t.LightWorld;
		else
			shadow.Flags &= unchecked((ushort)~(int)ShadowFlags_t.LightWorld);
	}

	bool IsFlashlightTarget(ClientShadowHandle_t shadowHandle, IClientRenderable? renderable) {
		ref ClientShadow_t shadow = ref Shadows[shadowHandle].Shadow;

		if (shadow.TargetEntity.Get()!.GetClientRenderable() == renderable)
			return true;

		C_BaseEntity? child = shadow.TargetEntity.Get()!.FirstMoveChild();
		while (child != null) {
			if (child.GetClientRenderable() == renderable)
				return true;

			child = child.NextMovePeer();
		}

		return false;
	}

	int BuildActiveShadowDepthList(in ViewSetup viewSetup, int maxDepthShadows, Span<ClientShadowHandle_t> activeDepthShadows) => throw new NotImplementedException();

	void SetViewFlashlightState(int activeFlashlightCount, ReadOnlySpan<ClientShadowHandle_t> activeFlashlights) => throw new NotImplementedException();
}

[ExposeMaterialProxy(Name = "Shadow")]
public class ShadowProxy : IMaterialProxy
{
	IMaterialVar? BaseTextureVar;

	public bool Init(IMaterial material, KeyValues keyValues) {
		BaseTextureVar = material.FindVar("$basetexture", out bool foundVar, false);
		return foundVar;
	}

	public void OnBind(object? proxyData) {
		ClientShadowHandle_t clientShadowHandle = (ClientShadowHandle_t)(proxyData ?? CLIENTSHADOW_INVALID_HANDLE);
		ITexture? tex = s_ClientShadowMgr.GetShadowTexture(clientShadowHandle);
		BaseTextureVar!.SetTextureValue(tex);
	}

	public void Release() { }

	public IMaterial GetMaterial() => BaseTextureVar!.GetOwningMaterial();
}

[ExposeMaterialProxy(Name = "ShadowModel")]
public class ShadowModelProxy : IMaterialProxy
{
	IMaterialVar? BaseTextureVar;
	IMaterialVar? BaseTextureOffsetVar;
	IMaterialVar? BaseTextureScaleVar;
	IMaterialVar? BaseTextureMatrixVar;
	IMaterialVar? FalloffOffsetVar;
	IMaterialVar? FalloffDistanceVar;
	IMaterialVar? FalloffAmountVar;

	public bool Init(IMaterial material, KeyValues keyValues) {
		BaseTextureVar = material.FindVar("$basetexture", out bool foundVar, false);
		if (!foundVar) return false;
		BaseTextureOffsetVar = material.FindVar("$basetextureoffset", out foundVar, false);
		if (!foundVar) return false;
		BaseTextureScaleVar = material.FindVar("$basetexturescale", out foundVar, false);
		if (!foundVar) return false;
		BaseTextureMatrixVar = material.FindVar("$basetexturetransform", out foundVar, false);
		if (!foundVar) return false;
		FalloffOffsetVar = material.FindVar("$falloffoffset", out foundVar, false);
		if (!foundVar) return false;
		FalloffDistanceVar = material.FindVar("$falloffdistance", out foundVar, false);
		if (!foundVar) return false;
		FalloffAmountVar = material.FindVar("$falloffamount", out foundVar, false);
		return foundVar;
	}

	public void OnBind(object? proxyData) {
		ClientShadowHandle_t clientShadowHandle = (ClientShadowHandle_t)(proxyData ?? CLIENTSHADOW_INVALID_HANDLE);
		ITexture? tex = s_ClientShadowMgr.GetShadowTexture(clientShadowHandle);
		BaseTextureVar!.SetTextureValue(tex);

		ref readonly Source.Common.Engine.ShadowInfo_t info = ref s_ClientShadowMgr.GetShadowInfo(clientShadowHandle);
		BaseTextureMatrixVar!.SetMatrixValue(in info.WorldToShadow);
		BaseTextureOffsetVar!.SetVecValue(in info.TexOrigin);
		BaseTextureScaleVar!.SetVecValue(in info.TexSize);
		FalloffOffsetVar!.SetFloatValue(info.FalloffOffset);
		FalloffDistanceVar!.SetFloatValue(info.MaxDist);
		FalloffAmountVar!.SetFloatValue(info.FalloffAmount);
	}

	public void Release() { }

	public IMaterial GetMaterial() => BaseTextureVar!.GetOwningMaterial();
}
