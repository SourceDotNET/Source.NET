using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Game.Client;


public class ClientRenderablesList : IPoolableObject
{

	public static readonly ObjectPool<ClientRenderablesList> Shared = new();

	public ref Entry this[RenderGroup renderGroup, int index] => ref RenderGroups[(int)renderGroup, index];
	public ref int Count(RenderGroup group) => ref RenderGroupCounts[(int)group];

	public struct Entry
	{
		public IClientRenderable? Renderable;
		public ushort WorldListInfoLeaf;
		public bool TwoPass;
		public ClientRenderHandle_t RenderHandle;
	}

	public const int RENDER_GROUP_COUNT = (int)RenderGroup.Count;
	public const int MAX_GROUP_ENTITIES = 4096;

	public readonly Entry[,] RenderGroups = new Entry[RENDER_GROUP_COUNT, MAX_GROUP_ENTITIES];
	public readonly int[] RenderGroupCounts = new int[RENDER_GROUP_COUNT];

	public void Init() {
		for (int i = 0; i < RENDER_GROUP_COUNT; i++) {
			int count = RenderGroupCounts[i];
			if (count > 0)
				MemoryMarshal.CreateSpan(ref RenderGroups[i, 0], count).Clear();
		}
		Array.Clear(RenderGroupCounts);
	}

	public void Reset() { }
}

public struct SetupRenderInfo
{
	public WorldListInfo WorldListInfo;
	public ClientRenderablesList? RenderList;
	public Vector3 RenderOrigin;
	public Vector3 RenderForward;
	public long RenderFrame;
	public long DetailBuildFrame;
	public float RenderDistSq;
	public bool DrawDetailObjects;
	public bool DrawTranslucentObjects;

	public SetupRenderInfo() {
		DrawDetailObjects = true;
		DrawTranslucentObjects = true;
	}
}

public interface IClientLeafSystem : IClientLeafSystemEngine, IGameSystemPerFrame
{
	void AddRenderable(IClientRenderable renderable, RenderGroup group);
	void BuildRenderablesList(in SetupRenderInfo setupInfo);
	void ComputeTranslucentRenderLeaf(int count, List<LeafIndex_t> leafList, List<LeafFogVolume_t> leafFogVolumeList, long frameNumber, int viewID);
	void CollateViewModelRenderables(List<IClientRenderable> opaqueViewModelList, List<IClientRenderable> translucentViewModelList);
	void EnableAlternateSorting(ClientRenderHandle_t renderHandle, bool alternateSorting);
	bool IsRenderableInPVS(IClientRenderable renderable);

	ClientLeafShadowHandle_t AddShadow(ClientShadowHandle_t userId, ushort flags);
	void RemoveShadow(ClientLeafShadowHandle_t h);

	void ProjectShadow(ClientLeafShadowHandle_t handle, int leafCount, ReadOnlySpan<int> leafList);
	void ProjectFlashlight(ClientLeafShadowHandle_t handle, int leafCount, ReadOnlySpan<int> leafList);

	void EnumerateShadowsInLeaves(int leafCount, List<LeafIndex_t> leaves, IClientLeafShadowEnum enumerator);

	void RenderableChanged(ClientRenderHandle_t handle);
	void SetRenderGroup(ClientRenderHandle_t handle, RenderGroup group);

	void SetSubSystemDataInLeaf(int leaf, int subSystemIdx, ClientLeafSubSystemData? data);
	ClientLeafSubSystemData? GetSubSystemDataInLeaf(int leaf, int subSystemIdx);
	void SetDetailObjectsInLeaf(int leaf, int firstDetailObject, int detailObjectCount);
	void GetDetailObjectsInLeaf(int leaf, out int firstDetailObject, out int detailObjectCount);
	void DrawDetailObjectsInLeaf(int leaf, int frameNumber, out int firstDetailObject, out int detailObjectCount);
}
