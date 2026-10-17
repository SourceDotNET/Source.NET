global using static Source.Engine.SpatialPartitionGlobals;

using Source.Common;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static Source.Common.PartitionConstants;
using static Source.Engine.SpatialPartitionConstants;

namespace Source.Engine;

public static class SpatialPartitionConstants
{
	public const int SPHASH_LEVEL_SKIP = 2;
	public const int SPHASH_VOXEL_SIZE = 256;
	public const int SPHASH_VOXEL_SHIFT = 8;
	public const float SPHASH_VOXEL_LARGE = 65536.0f;
	public const int SPHASH_HANDLELIST_BLOCK = 256;
	public const int SPHASH_LEAFLIST_BLOCK = 512;
	public const int SPHASH_ENTITYLIST_BLOCK = 256;
	public const int SPHASH_BUCKET_COUNT = 512;
	public const float SPHASH_EPS = 0.03125f;
}

public static class SpatialPartitionGlobals
{
	public static readonly SpatialPartitionImpl g_SpatialPartition = new();
	public static ISpatialPartitionInternal SpatialPartition() => g_SpatialPartition;
}

public enum PartitionTrees
{
	ClientTree = 0,
	ServerTree = 1,
	NumTrees = 2,
}

[InlineArray((int)PartitionTrees.NumTrees)]
public struct TreeArray<T>
{
	T _first;
}

[Flags]
public enum EntityInfoFlags : byte
{
	Hidden = 1 << 0,
	InClientTree = 1 << 1,
	InServerTree = 1 << 2,
}

[StructLayout(LayoutKind.Explicit)]
public struct Voxel
{
	[FieldOffset(0)] public uint Raw;

	public int X {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => (int)(Raw & 0x7FF);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => Raw = (Raw & 0xFFFFF800u) | ((uint)value & 0x7FF);
	}

	public int Y {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => (int)((Raw >> 11) & 0x7FF);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => Raw = (Raw & 0xFFC007FFu) | (((uint)value & 0x7FF) << 11);
	}

	public int Z {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => (int)((Raw >> 22) & 0x3FF);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => Raw = (Raw & 0x003FFFFFu) | (((uint)value & 0x3FF) << 22);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Voxel ConvertToNextLevel(Voxel v)
		=> new Voxel { Raw = (v.Raw >> SPHASH_LEVEL_SKIP) & 0xFFCFF9FFu };
}

public sealed class SpatialEntityInfo
{
	public Vector3 Min;
	public Vector3 Max;
	public IHandleEntity? HandleEntity;
	public ushort ListMask;
	public EntityInfoFlags Flags;
	public TreeArray<sbyte> Level;
	public TreeArray<ushort> VisitBit;
	public TreeArray<int> LeafListHead;
}

public struct LeafListData
{
	public int VoxelHashHandle;
	public int EntityListIndex;
}

public sealed class HandleList
{
	SpatialEntityInfo?[] _items = new SpatialEntityInfo[SPHASH_HANDLELIST_BLOCK];
	int[] _next;
	int _freeHead = -1;
	int _count;
	int _capacity;
	readonly object _lock = new();

	public HandleList() {
		for (int i = 0; i < _items.Length; i++)
			_items[i] = new();

		_capacity = SPHASH_HANDLELIST_BLOCK;
		_next = new int[_capacity];
		for (int i = _capacity - 1; i >= 0; --i) {
			_next[i] = _freeHead;
			_freeHead = i;
		}
	}

	public SpatialEntityInfo this[ushort handle] {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _items[handle]!;
	}

	public bool IsValidIndex(ushort handle) => handle < _capacity && _items[handle] != null;

	public ushort Alloc() {
		lock (_lock) {
			if (_freeHead == -1) Grow();
			int idx = _freeHead;
			_freeHead = _next[idx];
			_items[idx] ??= new();
			_count++;
			return (ushort)idx;
		}
	}

	public void Free(ushort handle) {
		lock (_lock) {
			_items[handle].ClearInstantiatedReference();
			_next[handle] = _freeHead;
			_freeHead = handle;
			_count--;
		}
	}

	public int Count => _count;

	void Grow() {
		int newCap = _capacity * 2;
		Array.Resize(ref _items, newCap);
		Array.Resize(ref _next, newCap);
		for (int i = newCap - 1; i >= _capacity; --i) {
			_next[i] = _freeHead;
			_freeHead = i;
		}
		_capacity = newCap;
	}
}

public sealed class PartitionVisits
{
	ulong[] _bits = Array.Empty<ulong>();
	int _bitCount;

	public void Resize(int bitCount) {
		_bitCount = bitCount;
		int words = (bitCount + 63) >> 6;
		if (_bits.Length < words)
			_bits = new ulong[words];
	}

	public void ClearAll() => Array.Clear(_bits, 0, (_bitCount + 63) >> 6);

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsBitSet(int bit) => (_bits[bit >> 6] & (1UL << (bit & 63))) != 0;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void Set(int bit) => _bits[bit >> 6] |= 1UL << (bit & 63);
}

public sealed class VoxelHash
{
	Vector3 _voxelOrigin;
	readonly Dictionary<uint, int> _voxelHash = new();
	readonly int[] _voxelDelta = new int[3];
	readonly PooledLinkedList<ushort> _entityList = new(SPHASH_ENTITYLIST_BLOCK);
	VoxelTree _tree = null!;
	int _level;

	public static int ComputeVoxelCountAtLevel(int level) {
		int c = (int)COORD_EXTENT >> SPHASH_VOXEL_SHIFT;
		c >>= SPHASH_LEVEL_SKIP * level;
		return c > 0 ? c : 1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int VoxelSize() => SPHASH_VOXEL_SIZE << (SPHASH_LEVEL_SKIP * _level);

	public void Init(VoxelTree tree, in Vector3 worldMin, in Vector3 worldMax, int level) {
		_tree = tree;
		_level = level;
		int nVoxelCount = ComputeVoxelCountAtLevel(level);
		_voxelOrigin = new Vector3(MIN_COORD_FLOAT, MIN_COORD_FLOAT, MIN_COORD_FLOAT);
		_voxelDelta[0] = nVoxelCount;
		_voxelDelta[1] = nVoxelCount;
		_voxelDelta[2] = nVoxelCount;

		Debug.Assert(_voxelDelta[0] >= 0 && _voxelDelta[0] <= (1 << 10));
		Debug.Assert(_voxelDelta[1] >= 0 && _voxelDelta[1] <= (1 << 10));
		Debug.Assert(_voxelDelta[2] >= 0 && _voxelDelta[2] <= (1 << 9));

		_voxelHash.Clear();
		_entityList.Clear();
	}

	public void Shutdown() {
		_entityList.Clear();
		_voxelHash.Clear();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Voxel VoxelIndexFromPoint(in Vector3 pt) {
		int shift = SPHASH_VOXEL_SHIFT + SPHASH_LEVEL_SKIP * _level;
		Voxel v = default;
		v.X = (int)(pt.X - _voxelOrigin.X) >> shift;
		v.Y = (int)(pt.Y - _voxelOrigin.Y) >> shift;
		v.Z = (int)(pt.Z - _voxelOrigin.Z) >> shift;
		return v;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void VoxelIndexFromPoint(in Vector3 pt, Span<int> result) {
		int shift = SPHASH_VOXEL_SHIFT + SPHASH_LEVEL_SKIP * _level;
		result[0] = (int)(pt.X - _voxelOrigin.X) >> shift;
		result[1] = (int)(pt.Y - _voxelOrigin.Y) >> shift;
		result[2] = (int)(pt.Z - _voxelOrigin.Z) >> shift;
	}

	public int EntityCount() {
		int count = 0;
		foreach (int head in _voxelHash.Values) {
			int i = head;
			while (i != PooledLinkedList<ushort>.INVALID_INDEX) {
				count++;
				i = _entityList.Next(i);
			}
		}
		return count;
	}

	public void InsertIntoTree(ushort hPartition, in Vector3 vecMin, in Vector3 vecMax) {
		SpatialEntityInfo info = _tree.EntityInfo(hPartition);
		PooledLinkedList<LeafListData> leafList = _tree.LeafList;
		int treeId = _tree.TreeId;

		info.Min = vecMin;
		info.Max = vecMax;
		Unsafe.Add(ref Unsafe.As<TreeArray<sbyte>, sbyte>(ref info.Level), treeId) = (sbyte)_level;

		Voxel voxelMin = VoxelIndexFromPoint(vecMin);
		Voxel voxelMax = VoxelIndexFromPoint(vecMax);

		Voxel voxel = default;
		for (int iX = voxelMin.X; iX <= voxelMax.X; ++iX) {
			voxel.X = iX;
			for (int iY = voxelMin.Y; iY <= voxelMax.Y; ++iY) {
				voxel.Y = iY;
				for (int iZ = voxelMin.Z; iZ <= voxelMax.Z; ++iZ) {
					voxel.Z = iZ;

					int iEntity = _entityList.Alloc();
					_entityList[iEntity] = hPartition;

					if (_voxelHash.TryGetValue(voxel.Raw, out int iHead)) {
						_entityList.LinkBefore(iHead, iEntity);
						_voxelHash[voxel.Raw] = iEntity;
					}
					else {
						_voxelHash[voxel.Raw] = iEntity;
					}

					int iLeaf = leafList.Alloc();
					leafList[iLeaf] = new LeafListData {
						VoxelHashHandle = (int)voxel.Raw,
						EntityListIndex = iEntity,
					};

					ref int leafHead = ref Unsafe.Add(ref Unsafe.As<TreeArray<int>, int>(ref info.LeafListHead), treeId);
					if (leafHead == PooledLinkedList<LeafListData>.INVALID_INDEX) {
						leafHead = iLeaf;
					}
					else {
						leafList.LinkBefore(leafHead, iLeaf);
						leafHead = iLeaf;
					}
				}
			}
		}
	}

	public void RemoveFromTree(ushort hPartition) {
		SpatialEntityInfo info = _tree.EntityInfo(hPartition);
		PooledLinkedList<LeafListData> leafList = _tree.LeafList;
		int treeId = _tree.TreeId;

		ref int leafHead = ref Unsafe.Add(ref Unsafe.As<TreeArray<int>, int>(ref info.LeafListHead), treeId);
		int iLeaf = leafHead;
		while (iLeaf != PooledLinkedList<LeafListData>.INVALID_INDEX) {
			int iNext = leafList.Next(iLeaf);
			ref LeafListData ld = ref leafList[iLeaf];
			uint voxelKey = (uint)ld.VoxelHashHandle;

			if (!_voxelHash.TryGetValue(voxelKey, out int iEntityHead)) {
				iLeaf = iNext;
				continue;
			}

			int iEntity = ld.EntityListIndex;
			if (iEntityHead == iEntity) {
				int iEntityNext = _entityList.Next(iEntityHead);
				if (iEntityNext == PooledLinkedList<ushort>.INVALID_INDEX)
					_voxelHash.Remove(voxelKey);
				else
					_voxelHash[voxelKey] = iEntityNext;
			}

			_entityList.Remove(iEntity);
			leafList.Remove(iLeaf);
			iLeaf = iNext;
		}

		leafHead = PooledLinkedList<LeafListData>.INVALID_INDEX;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool Visit(PartitionVisits? visits, int treeId, ushort hPartition, SpatialEntityInfo info) {
		if (visits == null) return true;
		ushort bit = Unsafe.Add(ref Unsafe.As<TreeArray<ushort>, ushort>(ref info.VisitBit), treeId);
		if (visits.IsBitSet(bit)) return false;
		visits.Set(bit);
		return true;
	}

	public bool EnumerateElementsInVoxel<T, IPE>(Voxel voxel, in T intersectTest, SpatialPartitionListMask_t listMask,
		 scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct where T : PartitionVisitor {
		if (!_voxelHash.TryGetValue(voxel.Raw, out int head))
			return true;

		int treeId = _tree.TreeId;
		PartitionVisits? visits = _tree.GetVisits();

		for (int i = head; i != PooledLinkedList<ushort>.INVALID_INDEX; i = _entityList.Next(i)) {
			ushort hPartition = _entityList[i];
			if (hPartition == PARTITION_INVALID_HANDLE) continue;

			SpatialEntityInfo info = _tree.EntityInfo(hPartition);
			if ((listMask & info.ListMask) == 0) continue;
			if ((info.Flags & EntityInfoFlags.Hidden) != 0) continue;
			if (!Visit(visits, treeId, hPartition, info)) continue;
			if (!intersectTest.Intersects(info.Min, info.Max)) continue;

			if (iterator.EnumElement(info.HandleEntity) == IterationRetval.Stop)
				return false;
		}
		return true;
	}

	public bool EnumerateElementsInSingleVoxel<T, IPE>(Voxel voxel, in T intersectTest, SpatialPartitionListMask_t listMask,
		 scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct where T : PartitionVisitor {
		if (!_voxelHash.TryGetValue(voxel.Raw, out int head))
			return true;

		for (int i = head; i != PooledLinkedList<ushort>.INVALID_INDEX; i = _entityList.Next(i)) {
			ushort hPartition = _entityList[i];
			if (hPartition == PARTITION_INVALID_HANDLE) continue;

			SpatialEntityInfo info = _tree.EntityInfo(hPartition);
			if ((listMask & info.ListMask) == 0) continue;
			if ((info.Flags & EntityInfoFlags.Hidden) != 0) continue;
			if (!intersectTest.Intersects(info.Min, info.Max)) continue;

			if (iterator.EnumElement(info.HandleEntity) == IterationRetval.Stop)
				return false;
		}
		return true;
	}

	public bool EnumerateElementsInBox<IPE>(SpatialPartitionListMask_t listMask, Voxel vmin, Voxel vmax,
		in Vector3 mins, in Vector3 maxs, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		var rect = new IntersectBox(_tree, mins, maxs);
		if (vmin.Raw == vmax.Raw)
			return EnumerateElementsInSingleVoxel(vmin, rect, listMask, ref iterator);

		Voxel voxel = default;
		for (int iX = vmin.X; iX <= vmax.X; ++iX) {
			voxel.X = iX;
			for (int iY = vmin.Y; iY <= vmax.Y; ++iY) {
				voxel.Y = iY;
				for (int iZ = vmin.Z; iZ <= vmax.Z; ++iZ) {
					voxel.Z = iZ;
					if (!EnumerateElementsInVoxel(voxel, rect, listMask, ref iterator))
						return false;
				}
			}
		}
		return true;
	}

	public bool EnumerateElementsAtPoint<IPE>(SpatialPartitionListMask_t listMask, Voxel v,
		in Vector3 pt, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		if (!_voxelHash.TryGetValue(v.Raw, out int head))
			return true;

		for (int i = head; i != PooledLinkedList<ushort>.INVALID_INDEX; i = _entityList.Next(i)) {
			ushort hPartition = _entityList[i];
			if (hPartition == PARTITION_INVALID_HANDLE) continue;

			SpatialEntityInfo info = _tree.EntityInfo(hPartition);
			if ((listMask & info.ListMask) == 0) continue;
			if ((info.Flags & EntityInfoFlags.Hidden) != 0) continue;
			if (!IsPointInBox(pt, info.Min, info.Max)) continue;

			if (iterator.EnumElement(info.HandleEntity) == IterationRetval.Stop)
				return false;
		}
		return true;
	}

	public bool EnumerateElementsAlongRay<IPE>(SpatialPartitionListMask_t listMask,
		in Ray ray, in Vector3 vecInvDelta, in Vector3 vecEnd, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		if (ray.IsRay)
			return EnumerateElementsAlongRay_Ray(listMask, ray, vecInvDelta, vecEnd, ref iterator);
		return EnumerateElementsAlongRay_ExtrudedRay(listMask, ray, vecInvDelta, vecEnd, ref iterator);
	}

	bool EnumerateElementsAlongRay_Ray<IPE>(SpatialPartitionListMask_t listMask,
		in Ray ray, in Vector3 vecInvDelta, in Vector3 vecEnd, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		Voxel voxelStart = VoxelIndexFromPoint(ray.Start);
		Voxel voxelEnd = VoxelIndexFromPoint(vecEnd);

		var intersectRay = new IntersectRay(_tree, ray, vecInvDelta);

		if (voxelStart.Raw == voxelEnd.Raw)
			return EnumerateElementsInSingleVoxel(voxelStart, intersectRay, listMask, ref iterator);

		Voxel voxelCurrent = voxelStart;
		Span<int> nStep = stackalloc int[3];
		Span<float> tMax = stackalloc float[3];
		Span<float> tDelta = stackalloc float[3];
		LeafListRaySetup(ray, vecEnd, vecInvDelta, voxelStart, nStep, tMax, tDelta);

		while (true) {
			if (!EnumerateElementsInVoxel(voxelCurrent, intersectRay, listMask, ref iterator))
				return false;

			if (tMax[0] >= 1.0f && tMax[1] >= 1.0f && tMax[2] >= 1.0f)
				break;

			if (tMax[0] < tMax[1]) {
				if (tMax[0] < tMax[2]) { voxelCurrent.X += nStep[0]; tMax[0] += tDelta[0]; }
				else { voxelCurrent.Z += nStep[2]; tMax[2] += tDelta[2]; }
			}
			else {
				if (tMax[1] < tMax[2]) { voxelCurrent.Y += nStep[1]; tMax[1] += tDelta[1]; }
				else { voxelCurrent.Z += nStep[2]; tMax[2] += tDelta[2]; }
			}
		}
		return true;
	}

	bool EnumerateElementsAlongRay_ExtrudedRay<IPE>(SpatialPartitionListMask_t listMask,
		in Ray ray, in Vector3 vecInvDelta, in Vector3 vecEnd, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		Vector3 vecMin = ray.Start - ray.Extents;
		Vector3 vecMax = ray.Start + ray.Extents;

		Span<int> voxelMin = stackalloc int[3];
		Span<int> voxelMax = stackalloc int[3];
		VoxelIndexFromPoint(vecMin, voxelMin);
		VoxelIndexFromPoint(vecMax, voxelMax);

		var intersectSweptBox = new IntersectSweptBox(_tree, ray, vecInvDelta);

		Voxel voxel = default;
		for (int iX = voxelMin[0]; iX <= voxelMax[0]; ++iX) {
			voxel.X = iX;
			for (int iY = voxelMin[1]; iY <= voxelMax[1]; ++iY) {
				voxel.Y = iY;
				for (int iZ = voxelMin[2]; iZ <= voxelMax[2]; ++iZ) {
					voxel.Z = iZ;
					if (!EnumerateElementsInVoxel(voxel, intersectSweptBox, listMask, ref iterator))
						return false;
				}
			}
		}

		Vector3 vecEndMin = vecEnd - ray.Extents;
		Vector3 vecEndMax = vecEnd + ray.Extents;
		Span<int> endVoxelMin = stackalloc int[3];
		Span<int> endVoxelMax = stackalloc int[3];
		VoxelIndexFromPoint(vecEndMin, endVoxelMin);
		VoxelIndexFromPoint(vecEndMax, endVoxelMax);

		if (endVoxelMin[0] >= voxelMin[0] && endVoxelMin[1] >= voxelMin[1] && endVoxelMin[2] >= voxelMin[2] &&
			endVoxelMax[0] <= voxelMax[0] && endVoxelMax[1] <= voxelMax[1] && endVoxelMax[2] <= voxelMax[2])
			return true;

		Span<int> nStep = stackalloc int[3];
		Span<float> tMin2 = stackalloc float[3];
		Span<float> tMax2 = stackalloc float[3];
		Span<float> tDelta = stackalloc float[3];
		LeafListExtrudedRaySetup(ray, vecInvDelta, vecMin, vecMax, voxelMin, voxelMax, nStep, tMin2, tMax2, tDelta);

		while (tMax2[0] < 1.0f || tMax2[1] < 1.0f || tMax2[2] < 1.0f) {
			int iAxis = MinIndex(tMax2[0], tMax2[1], tMax2[2]);
			int iMinAxis = MinIndex(tMin2[0], tMin2[1], tMin2[2]);

			if (tMin2[iMinAxis] < tMax2[iAxis]) {
				tMin2[iMinAxis] += tDelta[iMinAxis];
				if (nStep[iMinAxis] > 0)
					voxelMin[iMinAxis] += nStep[iMinAxis];
				else
					voxelMax[iMinAxis] += nStep[iMinAxis];
			}
			else {
				tMax2[iAxis] += tDelta[iAxis];
				if (nStep[iAxis] > 0)
					voxelMax[iAxis] += nStep[iAxis];
				else
					voxelMin[iAxis] += nStep[iAxis];

				unsafe {
					if (!EnumerateElementsAlongRay_ExtrudedRaySlice(listMask, ref iterator, intersectSweptBox, voxelMin, voxelMax, iAxis, nStep))
						return false;
				}
			}
		}
		return true;
	}

	public bool EnumerateElementsAlongRay_ExtrudedRaySlice<IPE>(SpatialPartitionListMask_t listMask,
		 scoped ref IPE iterator, in IntersectSweptBox intersectSweptBox,
		Span<int> voxelMin, Span<int> voxelMax, int iAxis, Span<int> pStep) where IPE : struct, IPartitionEnumerator, allows ref struct {
		Span<int> mins = stackalloc int[3] { voxelMin[0], voxelMin[1], voxelMin[2] };
		Span<int> maxs = stackalloc int[3] { voxelMax[0], voxelMax[1], voxelMax[2] };

		if (pStep[iAxis] < 0) maxs[iAxis] = mins[iAxis];
		else mins[iAxis] = maxs[iAxis];

		Voxel voxel = default;
		for (int iX = mins[0]; iX <= maxs[0]; ++iX) {
			voxel.X = iX;
			for (int iY = mins[1]; iY <= maxs[1]; ++iY) {
				voxel.Y = iY;
				for (int iZ = mins[2]; iZ <= maxs[2]; ++iZ) {
					voxel.Z = iZ;
					if (!EnumerateElementsInVoxel(voxel, intersectSweptBox, listMask, ref iterator))
						return false;
				}
			}
		}
		return true;
	}

	public void LeafListRaySetup(in Ray ray, in Vector3 vecEnd, in Vector3 vecInvDelta,
		Voxel voxel, Span<int> pStep, Span<float> pMax, Span<float> pDelta) {
		Span<int> iVoxel = stackalloc int[3] { voxel.X, voxel.Y, voxel.Z };
		Vector3 vecVoxelStart = ray.Start - _voxelOrigin;
		Vector3 vecVoxelEnd = vecEnd - _voxelOrigin;

		for (int axis = 0; axis < 3; ++axis) {
			float startComp = axis == 0 ? vecVoxelStart.X : axis == 1 ? vecVoxelStart.Y : vecVoxelStart.Z;
			float endComp = axis == 0 ? vecVoxelEnd.X : axis == 1 ? vecVoxelEnd.Y : vecVoxelEnd.Z;
			float deltaComp = axis == 0 ? ray.Delta.X : axis == 1 ? ray.Delta.Y : ray.Delta.Z;
			float invComp = axis == 0 ? vecInvDelta.X : axis == 1 ? vecInvDelta.Y : vecInvDelta.Z;

			if (startComp == endComp) {
				pStep[axis] = 0;
				pMax[axis] = SPHASH_VOXEL_LARGE;
				pDelta[axis] = SPHASH_VOXEL_LARGE;
				continue;
			}

			float flDistStart, flRecipDist;
			float flDistEnd;
			int vs = VoxelSize();

			if (deltaComp < 0.0f) {
				pStep[axis] = -1;
				float flDist = iVoxel[axis] * vs;
				flDistStart = startComp - flDist;
				flDistEnd = endComp - flDist;
				flRecipDist = -invComp;
			}
			else {
				pStep[axis] = 1;
				float flDist = (iVoxel[axis] + 1) * -vs;
				flDistStart = -startComp - flDist;
				flDistEnd = -endComp - flDist;
				flRecipDist = invComp;
			}

			if (flDistStart > 0.0f && flDistEnd > 0.0f) {
				pMax[axis] = SPHASH_VOXEL_LARGE;
				pDelta[axis] = SPHASH_VOXEL_LARGE;
			}
			else {
				pMax[axis] = flDistStart * flRecipDist;
				pDelta[axis] = vs * flRecipDist;
			}
		}
	}

	public void LeafListExtrudedRaySetup(in Ray ray, in Vector3 vecInvDelta,
		in Vector3 vecMin, in Vector3 vecMax,
		Span<int> iVoxelMin, Span<int> iVoxelMax,
		Span<int> pStep, Span<float> pMin, Span<float> pMax, Span<float> pDelta) {
		Vector3 vecVoxelMin = vecMin - _voxelOrigin;
		Vector3 vecVoxelMax = vecMax - _voxelOrigin;

		for (int axis = 0; axis < 3; ++axis) {
			float deltaComp = axis == 0 ? ray.Delta.X : axis == 1 ? ray.Delta.Y : ray.Delta.Z;
			float invComp = axis == 0 ? vecInvDelta.X : axis == 1 ? vecInvDelta.Y : vecInvDelta.Z;
			float voxMinComp = axis == 0 ? vecVoxelMin.X : axis == 1 ? vecVoxelMin.Y : vecVoxelMin.Z;
			float voxMaxComp = axis == 0 ? vecVoxelMax.X : axis == 1 ? vecVoxelMax.Y : vecVoxelMax.Z;

			if (deltaComp == 0.0f) {
				pMax[axis] = SPHASH_VOXEL_LARGE;
				pMin[axis] = SPHASH_VOXEL_LARGE;
				pDelta[axis] = SPHASH_VOXEL_LARGE;
				continue;
			}

			float flDistStart, flDistMinStart, flDist, flRecipDist;
			int vs = VoxelSize();

			if (deltaComp < 0.0f) {
				pStep[axis] = -1;
				flDistStart = voxMinComp - (iVoxelMin[axis] * vs);
				flDistMinStart = voxMaxComp - (iVoxelMax[axis] * vs);
				flDist = -deltaComp;
				flRecipDist = -invComp;
			}
			else {
				pStep[axis] = 1;
				flDistStart = -voxMaxComp - ((iVoxelMax[axis] + 1) * -vs);
				flDistMinStart = -voxMinComp - ((iVoxelMin[axis] + 1) * -vs);
				flDist = deltaComp;
				flRecipDist = invComp;
			}

			pMax[axis] = flDistStart > flDist ? SPHASH_VOXEL_LARGE : flDistStart * flRecipDist;
			pDelta[axis] = flDistStart > flDist ? SPHASH_VOXEL_LARGE : vs * flRecipDist;
			pMin[axis] = flDistMinStart > flDist ? SPHASH_VOXEL_LARGE : flDistMinStart * flRecipDist;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool IsPointInBox(in Vector3 pt, in Vector3 mins, in Vector3 maxs) =>
		pt.X >= mins.X && pt.X <= maxs.X &&
		pt.Y >= mins.Y && pt.Y <= maxs.Y &&
		pt.Z >= mins.Z && pt.Z <= maxs.Z;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static int MinIndex(float a, float b, float c) =>
		a < b ? (a < c ? 0 : 2) : (b < c ? 1 : 2);
}

public interface PartitionVisitor
{
	PartitionVisits? Visits { get; }
	int Tree { get; }
	public bool Visit(SpatialPartitionHandle_t partition, SpatialEntityInfo info) {
		int visitBit = info.VisitBit[Tree];
		if (Visits.IsBitSet(visitBit))
			return false;

		Visits.Set(visitBit);
		return true;
	}
	bool Intersects(in Vector3 mins, in Vector3 maxs);
}

public readonly struct IntersectBox(VoxelTree partition, in Vector3 mins, in Vector3 maxs) : PartitionVisitor
{
	public PartitionVisits? Visits { get; } = partition.GetVisits();
	public int Tree { get; } = partition.GetTreeId();

	readonly Vector3 _mins = mins, _maxs = maxs;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Intersects(in Vector3 mins, in Vector3 maxs) =>
		mins.X <= _maxs.X && maxs.X >= _mins.X &&
		mins.Y <= _maxs.Y && maxs.Y >= _mins.Y &&
		mins.Z <= _maxs.Z && maxs.Z >= _mins.Z;
}

public readonly struct IntersectPoint(VoxelTree partition, in Vector3 pt) : PartitionVisitor
{
	public PartitionVisits? Visits { get; } = partition.GetVisits();
	public int Tree { get; } = partition.GetTreeId();

	readonly Vector3 _pt = pt;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Intersects(in Vector3 mins, in Vector3 maxs) =>
		_pt.X >= mins.X && _pt.X <= maxs.X &&
		_pt.Y >= mins.Y && _pt.Y <= maxs.Y &&
		_pt.Z >= mins.Z && _pt.Z <= maxs.Z;
}

public readonly struct IntersectRay(VoxelTree partition, in Ray ray, in Vector3 invDelta) : PartitionVisitor
{
	public PartitionVisits? Visits { get; } = partition.GetVisits();
	public int Tree { get; } = partition.GetTreeId();
	readonly Ray _ray = ray;
	readonly Vector3 _invDelta = invDelta;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool Intersects(in Vector3 mins, in Vector3 maxs) => CollisionUtils.IsBoxIntersectingRay(mins, maxs, _ray.Start, _ray.Delta, _invDelta);
}

public readonly struct IntersectSweptBox(VoxelTree partition, in Ray ray, in Vector3 invDelta) : PartitionVisitor
{
	public PartitionVisits? Visits { get; } = partition.GetVisits();
	public int Tree { get; } = partition.GetTreeId();
	readonly Ray _ray = ray;
	readonly Vector3 _invDelta = invDelta;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Intersects(in Vector3 mins, in Vector3 maxs) {
		Vector3 testMin = mins - _ray.Extents;
		Vector3 testMax = maxs + _ray.Extents;
		return CollisionUtils.IsBoxIntersectingRay(testMin, testMax, _ray.Start, _ray.Delta, _invDelta);
	}
}

public sealed class VoxelTree
{
	int _levelCount;
	VoxelHash[] _voxelHash = null!;
	readonly PooledLinkedList<LeafListData> _leafList = new(SPHASH_LEAFLIST_BLOCK);
	int _treeId;
	SpatialPartitionImpl? _owner;
	readonly List<ushort> _availableVisitBits = new(2048);
	ushort _nextVisitBit;
	readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);

	[ThreadStatic] static PartitionVisits? t_visits;

	public int TreeId => _treeId;
	public PooledLinkedList<LeafListData> LeafList => _leafList;

	public SpatialEntityInfo EntityInfo(ushort hPartition) => _owner!.EntityInfo(hPartition);

	public PartitionVisits? GetVisits() => t_visits;
	public int GetTreeId() => TreeId;

	[ThreadStatic] static Stack<PartitionVisits>? t_freeVisits;

	public PartitionVisits? BeginVisit() {
		var prev = t_visits;
		Stack<PartitionVisits> free = t_freeVisits ??= new();
		var visits = free.Count > 0 ? free.Pop() : new PartitionVisits();
		visits.Resize(_nextVisitBit);
		visits.ClearAll();
		t_visits = visits;
		return prev;
	}

	public void EndVisit(PartitionVisits? prev) {
		if (t_visits != null)
			(t_freeVisits ??= new()).Push(t_visits);
		t_visits = prev;
	}

	public VoxelTree() {
		_levelCount = 0;
		while (VoxelHash.ComputeVoxelCountAtLevel(_levelCount) > 2)
			++_levelCount;
		++_levelCount;

		Debug.Assert(_levelCount == 4);
		_voxelHash = new VoxelHash[_levelCount];
		for (int i = 0; i < _levelCount; i++)
			_voxelHash[i] = new VoxelHash();

	}

	public void Init(SpatialPartitionImpl owner, int treeId, in Vector3 worldMin, in Vector3 worldMax) {
		_owner = owner;
		_treeId = treeId;
		t_visits = null;

		for (int i = 0; i < _levelCount; ++i)
			_voxelHash[i].Init(this, worldMin, worldMax, i);

		_leafList.Clear();
	}

	public void Shutdown() {
		_leafList.Clear();
		for (int i = 0; i < _levelCount; ++i)
			_voxelHash[i].Shutdown();
	}

	public void InsertIntoTree(ushort hPartition, in Vector3 mins, in Vector3 maxs) {
		Debug.Assert(hPartition != PARTITION_INVALID_HANDLE);

		int readCount = _lock.RecursiveReadCount;
		for (int i = 0; i < readCount; i++) _lock.ExitReadLock();

		_lock.EnterWriteLock();
		try {
			SpatialEntityInfo info = EntityInfo(hPartition);
			ref ushort visitBit = ref Unsafe.Add(ref Unsafe.As<TreeArray<ushort>, ushort>(ref info.VisitBit), _treeId);
			if (_availableVisitBits.Count > 0) {
				visitBit = _availableVisitBits[^1];
				_availableVisitBits.RemoveAt(_availableVisitBits.Count - 1);
			}
			else {
				visitBit = _nextVisitBit++;
			}

			Vector3 vecMin = new(mins.X - SPHASH_EPS, mins.Y - SPHASH_EPS, mins.Z - SPHASH_EPS);
			Vector3 vecMax = new(maxs.X + SPHASH_EPS, maxs.Y + SPHASH_EPS, maxs.Z + SPHASH_EPS);
			vecMin = Vector3.Clamp(vecMin, new(MIN_COORD_FLOAT), new(MAX_COORD_FLOAT));
			vecMax = Vector3.Clamp(vecMax, new(MIN_COORD_FLOAT), new(MAX_COORD_FLOAT));

			Vector3 vecSize = vecMax - vecMin;
			int nLevel;
			for (nLevel = 0; nLevel < _levelCount - 1; ++nLevel) {
				int vs = _voxelHash[nLevel].VoxelSize();
				if (vs > vecSize.X && vs > vecSize.Y && vs > vecSize.Z)
					break;
			}
			_voxelHash[nLevel].InsertIntoTree(hPartition, vecMin, vecMax);
		}
		finally {
			_lock.ExitWriteLock();
			for (int i = 0; i < readCount; i++) _lock.EnterReadLock();
		}
	}

	public void RemoveFromTree(ushort hPartition) {
		Debug.Assert(hPartition != PARTITION_INVALID_HANDLE);
		SpatialEntityInfo info = EntityInfo(hPartition);
		int nLevel = Unsafe.Add(ref Unsafe.As<TreeArray<sbyte>, sbyte>(ref info.Level), _treeId);
		if (nLevel < 0) return;

		int readCount = _lock.RecursiveReadCount;
		for (int i = 0; i < readCount; i++) _lock.ExitReadLock();

		_lock.EnterWriteLock();
		try {
			_voxelHash[nLevel].RemoveFromTree(hPartition);
			_availableVisitBits.Add(Unsafe.Add(ref Unsafe.As<TreeArray<ushort>, ushort>(ref info.VisitBit), _treeId));
			Unsafe.Add(ref Unsafe.As<TreeArray<ushort>, ushort>(ref info.VisitBit), _treeId) = unchecked((ushort)-1);
		}
		finally {
			_lock.ExitWriteLock();
			for (int i = 0; i < readCount; i++) _lock.EnterReadLock();
		}
	}

	public void ElementMoved(ushort hPartition, in Vector3 mins, in Vector3 maxs) {
		if (hPartition == PARTITION_INVALID_HANDLE) return;

		SpatialEntityInfo info = EntityInfo(hPartition);
		ref int leafHead = ref Unsafe.Add(ref Unsafe.As<TreeArray<int>, int>(ref info.LeafListHead), _treeId);
		if (leafHead == PooledLinkedList<LeafListData>.INVALID_INDEX) {
			InsertIntoTree(hPartition, mins, maxs);
			return;
		}

		Vector3 vecEpsMin = new(mins.X - SPHASH_EPS, mins.Y - SPHASH_EPS, mins.Z - SPHASH_EPS);
		Vector3 vecEpsMax = new(maxs.X + SPHASH_EPS, maxs.Y + SPHASH_EPS, maxs.Z + SPHASH_EPS);
		if (info.Min == vecEpsMin && info.Max == vecEpsMax) return;

		RemoveFromTree(hPartition);
		InsertIntoTree(hPartition, mins, maxs);
	}

	public void EnumerateElementsInBox<IPE>(SpatialPartitionListMask_t listMask,
		in Vector3 vecMins, in Vector3 vecMaxs, bool coarseTest, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		if (listMask == 0) return;

		Vector3 mins = Vector3.Clamp(vecMins, new(MIN_COORD_FLOAT), new(MAX_COORD_FLOAT));
		Vector3 maxs = Vector3.Clamp(vecMaxs, new(MIN_COORD_FLOAT), new(MAX_COORD_FLOAT));

		var prevVisits = BeginVisit();
		_lock.EnterReadLock();
		try {
			Voxel vs = _voxelHash[0].VoxelIndexFromPoint(mins);
			Voxel ve = _voxelHash[0].VoxelIndexFromPoint(maxs);
			if (!_voxelHash[0].EnumerateElementsInBox(listMask, vs, ve, mins, maxs, ref iterator)) return;

			vs = Voxel.ConvertToNextLevel(vs); ve = Voxel.ConvertToNextLevel(ve);
			if (!_voxelHash[1].EnumerateElementsInBox(listMask, vs, ve, mins, maxs, ref iterator)) return;

			vs = Voxel.ConvertToNextLevel(vs); ve = Voxel.ConvertToNextLevel(ve);
			if (!_voxelHash[2].EnumerateElementsInBox(listMask, vs, ve, mins, maxs, ref iterator)) return;

			vs = Voxel.ConvertToNextLevel(vs); ve = Voxel.ConvertToNextLevel(ve);
			_voxelHash[3].EnumerateElementsInBox(listMask, vs, ve, mins, maxs, ref iterator);
		}
		finally {
			_lock.ExitReadLock();
			EndVisit(prevVisits);
		}
	}

	public void EnumerateElementsInSphere<IPE>(SpatialPartitionListMask_t listMask,
		in Vector3 origin, float radius, bool coarseTest, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		Vector3 vecMin = new(origin.X - radius, origin.Y - radius, origin.Z - radius);
		Vector3 vecMax = new(origin.X + radius, origin.Y + radius, origin.Z + radius);
		EnumerateElementsInBox(listMask, vecMin, vecMax, coarseTest, ref iterator);
	}

	public void EnumerateElementsAlongRay<IPE>(SpatialPartitionListMask_t listMask,
		in Ray ray, bool coarseTest, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		if (!ray.IsSwept) {
			Vector3 vecMin = ray.Start - ray.Extents;
			Vector3 vecMax = ray.Start + ray.Extents;
			EnumerateElementsInBox(listMask, vecMin, vecMax, coarseTest, ref iterator);
			return;
		}

		if (listMask == 0) return;

		Vector3 vecEnd = ray.Start + ray.Delta;
		Ray clippedRay = ray;

		Vector3 pMin = new(MIN_COORD_FLOAT);
		Vector3 pMax = new(MAX_COORD_FLOAT);
		bool bStartIn = IsPointInBox(ray.Start, pMin, pMax);
		bool bEndIn = IsPointInBox(vecEnd, pMin, pMax);
		if (!bStartIn && !bEndIn) return;

		if (!bStartIn) ClampStartPoint(ref clippedRay, vecEnd);
		else if (!bEndIn) ClampEndPoint(ref clippedRay, ref vecEnd);

		Vector3 vecInvDelta = new(
			clippedRay.Delta.X != 0f ? 1f / clippedRay.Delta.X : float.MaxValue,
			clippedRay.Delta.Y != 0f ? 1f / clippedRay.Delta.Y : float.MaxValue,
			clippedRay.Delta.Z != 0f ? 1f / clippedRay.Delta.Z : float.MaxValue
		);

		var prevVisits = BeginVisit();
		_lock.EnterReadLock();
		if (ray.IsRay)
			EnumerateElementsAlongRay_Ray(listMask, clippedRay, vecInvDelta, vecEnd, ref iterator);
		else
			EnumerateElementsAlongRay_ExtrudedRay(listMask, clippedRay, vecInvDelta, vecEnd, ref iterator);
		_lock.ExitReadLock();
		EndVisit(prevVisits);
	}

	bool EnumerateElementsAlongRay_Ray<IPE>(SpatialPartitionListMask_t listMask,
		in Ray ray, in Vector3 vecInvDelta, in Vector3 vecEnd, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		Voxel voxelStart = _voxelHash[0].VoxelIndexFromPoint(ray.Start);
		Voxel voxelEnd = _voxelHash[0].VoxelIndexFromPoint(vecEnd);

		var intersectRay = new IntersectRay(this, ray, vecInvDelta);

		if (voxelStart.Raw == voxelEnd.Raw) {
			Voxel v = voxelStart;
			if (!_voxelHash[0].EnumerateElementsInSingleVoxel(v, intersectRay, listMask, ref iterator)) return false;
			v = Voxel.ConvertToNextLevel(v);
			if (!_voxelHash[1].EnumerateElementsInSingleVoxel(v, intersectRay, listMask, ref iterator)) return false;
			v = Voxel.ConvertToNextLevel(v);
			if (!_voxelHash[2].EnumerateElementsInSingleVoxel(v, intersectRay, listMask, ref iterator)) return false;
			v = Voxel.ConvertToNextLevel(v);
			return _voxelHash[3].EnumerateElementsInSingleVoxel(v, intersectRay, listMask, ref iterator);
		}

		Voxel voxelCurrent = voxelStart;
		Span<int> nStep = stackalloc int[3];
		Span<float> tMax = stackalloc float[3];
		Span<float> tDelta = stackalloc float[3];
		_voxelHash[0].LeafListRaySetup(ray, vecEnd, vecInvDelta, voxelStart, nStep, tMax, tDelta);

		Voxel ov1 = default, ov2 = default, ov3 = default;
		ov1.Raw = ov2.Raw = ov3.Raw = 0xFFFFFFFF;

		Voxel v1 = Voxel.ConvertToNextLevel(voxelCurrent);
		Voxel v2 = Voxel.ConvertToNextLevel(v1);
		Voxel v3 = Voxel.ConvertToNextLevel(v2);

		while (true) {
			if (!_voxelHash[0].EnumerateElementsInVoxel(voxelCurrent, intersectRay, listMask, ref iterator)) return false;
			if (v1.Raw != ov1.Raw) { if (!_voxelHash[1].EnumerateElementsInVoxel(v1, intersectRay, listMask, ref iterator)) return false; }
			if (v2.Raw != ov2.Raw) { if (!_voxelHash[2].EnumerateElementsInVoxel(v2, intersectRay, listMask, ref iterator)) return false; }
			if (v3.Raw != ov3.Raw) { if (!_voxelHash[3].EnumerateElementsInVoxel(v3, intersectRay, listMask, ref iterator)) return false; }

			if (tMax[0] >= 1f && tMax[1] >= 1f && tMax[2] >= 1f) break;

			if (tMax[0] < tMax[1]) {
				if (tMax[0] < tMax[2]) { voxelCurrent.X += nStep[0]; tMax[0] += tDelta[0]; }
				else { voxelCurrent.Z += nStep[2]; tMax[2] += tDelta[2]; }
			}
			else {
				if (tMax[1] < tMax[2]) { voxelCurrent.Y += nStep[1]; tMax[1] += tDelta[1]; }
				else { voxelCurrent.Z += nStep[2]; tMax[2] += tDelta[2]; }
			}

			ov1 = v1; ov2 = v2; ov3 = v3;
			v1 = Voxel.ConvertToNextLevel(voxelCurrent);
			v2 = Voxel.ConvertToNextLevel(v1);
			v3 = Voxel.ConvertToNextLevel(v2);
		}
		return true;
	}

	unsafe bool EnumerateElementsAlongRay_ExtrudedRay<IPE>(SpatialPartitionListMask_t listMask,
		in Ray ray, in Vector3 vecInvDelta, in Vector3 vecEnd, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		Vector3 vecMin = ray.Start - ray.Extents;
		Vector3 vecMax = ray.Start + ray.Extents;

		Span<int> vb = stackalloc int[4 * 2 * 3];

		Span<int> vb00 = vb.Slice(0, 3);
		Span<int> vb01 = vb.Slice(3, 3);
		_voxelHash[0].VoxelIndexFromPoint(vecMin, vb00);
		_voxelHash[0].VoxelIndexFromPoint(vecMax, vb01);

		var intersectSweptBox = new IntersectSweptBox(this, ray, vecInvDelta);

		for (int lvl = 0; lvl < _levelCount; ++lvl) {
			int shift = lvl * SPHASH_LEVEL_SKIP;
			Span<int> lvlMin = vb.Slice(lvl * 6, 3);
			Span<int> lvlMax = vb.Slice(lvl * 6 + 3, 3);
			for (int a = 0; a < 3; a++) {
				lvlMin[a] = vb00[a] >> shift;
				lvlMax[a] = vb01[a] >> shift;
			}

			Voxel voxel = default;
			for (int iX = lvlMin[0]; iX <= lvlMax[0]; ++iX) {
				voxel.X = iX;
				for (int iY = lvlMin[1]; iY <= lvlMax[1]; ++iY) {
					voxel.Y = iY;
					for (int iZ = lvlMin[2]; iZ <= lvlMax[2]; ++iZ) {
						voxel.Z = iZ;
						if (!_voxelHash[lvl].EnumerateElementsInVoxel(voxel, intersectSweptBox, listMask, ref iterator))
							return false;
					}
				}
			}
		}

		Vector3 vecEndMin = vecEnd - ray.Extents;
		Vector3 vecEndMax = vecEnd + ray.Extents;
		Span<int> endMin = stackalloc int[3];
		Span<int> endMax = stackalloc int[3];
		_voxelHash[0].VoxelIndexFromPoint(vecEndMin, endMin);
		_voxelHash[0].VoxelIndexFromPoint(vecEndMax, endMax);
		if (endMin[0] >= vb00[0] && endMin[1] >= vb00[1] && endMin[2] >= vb00[2] &&
			endMax[0] <= vb01[0] && endMax[1] <= vb01[1] && endMax[2] <= vb01[2])
			return true;

		Span<int> nStep = stackalloc int[3];
		Span<float> tMin2 = stackalloc float[3];
		Span<float> tMax2 = stackalloc float[3];
		Span<float> tDelta = stackalloc float[3];
		_voxelHash[0].LeafListExtrudedRaySetup(ray, vecInvDelta, vecMin, vecMax, vb00, vb01, nStep, tMin2, tMax2, tDelta);

		Span<int> lastVoxel1 = stackalloc int[3];
		Span<int> lastVoxel2 = stackalloc int[3];
		Span<int> lastVoxel3 = stackalloc int[3];
		for (int a = 0; a < 3; a++) {
			int idx = nStep[a] > 0 ? 1 : 0;
			lastVoxel1[a] = vb[(1 * 6) + idx * 3 + a];
			lastVoxel2[a] = vb[(2 * 6) + idx * 3 + a];
			lastVoxel3[a] = vb[(3 * 6) + idx * 3 + a];
		}

		while (tMax2[0] < 1f || tMax2[1] < 1f || tMax2[2] < 1f) {
			int iAxis = MinIndex(tMax2[0], tMax2[1], tMax2[2]);
			int iMinAxis = MinIndex(tMin2[0], tMin2[1], tMin2[2]);

			if (tMin2[iMinAxis] < tMax2[iAxis]) {
				tMin2[iMinAxis] += tDelta[iMinAxis];
				int ni = nStep[iMinAxis] > 0 ? 0 : 1;
				vb[ni * 3 + iMinAxis] += nStep[iMinAxis];
				vb[1 * 6 + ni * 3 + iMinAxis] = vb[ni * 3 + iMinAxis] >> SPHASH_LEVEL_SKIP;
				vb[2 * 6 + ni * 3 + iMinAxis] = vb[ni * 3 + iMinAxis] >> (2 * SPHASH_LEVEL_SKIP);
				vb[3 * 6 + ni * 3 + iMinAxis] = vb[ni * 3 + iMinAxis] >> (3 * SPHASH_LEVEL_SKIP);
			}
			else {
				tMax2[iAxis] += tDelta[iAxis];
				int ni = nStep[iAxis] > 0 ? 1 : 0;
				vb[ni * 3 + iAxis] += nStep[iAxis];
				vb[1 * 6 + ni * 3 + iAxis] = vb[ni * 3 + iAxis] >> SPHASH_LEVEL_SKIP;
				vb[2 * 6 + ni * 3 + iAxis] = vb[ni * 3 + iAxis] >> (2 * SPHASH_LEVEL_SKIP);
				vb[3 * 6 + ni * 3 + iAxis] = vb[ni * 3 + iAxis] >> (3 * SPHASH_LEVEL_SKIP);

				Span<int> s0 = vb.Slice(0, 3);
				Span<int> s1 = vb.Slice(3, 3);
				if (!_voxelHash[0].EnumerateElementsAlongRay_ExtrudedRaySlice(listMask, ref iterator, intersectSweptBox, s0, s1, iAxis, nStep))
					return false;

				if (lastVoxel1[iAxis] != vb[1 * 6 + ni * 3 + iAxis]) {
					lastVoxel1[iAxis] = vb[1 * 6 + ni * 3 + iAxis];
					Span<int> l1m = vb.Slice(1 * 6, 3);
					Span<int> l1x = vb.Slice(1 * 6 + 3, 3);
					if (!_voxelHash[1].EnumerateElementsAlongRay_ExtrudedRaySlice(listMask, ref iterator, intersectSweptBox, l1m, l1x, iAxis, nStep))
						return false;
				}

				if (lastVoxel2[iAxis] != vb[2 * 6 + ni * 3 + iAxis]) {
					lastVoxel2[iAxis] = vb[2 * 6 + ni * 3 + iAxis];
					Span<int> l2m = vb.Slice(2 * 6, 3);
					Span<int> l2x = vb.Slice(2 * 6 + 3, 3);
					if (!_voxelHash[2].EnumerateElementsAlongRay_ExtrudedRaySlice(listMask, ref iterator, intersectSweptBox, l2m, l2x, iAxis, nStep))
						return false;
				}

				if (lastVoxel3[iAxis] != vb[3 * 6 + ni * 3 + iAxis]) {
					lastVoxel3[iAxis] = vb[3 * 6 + ni * 3 + iAxis];
					Span<int> l3m = vb.Slice(3 * 6, 3);
					Span<int> l3x = vb.Slice(3 * 6 + 3, 3);
					if (!_voxelHash[3].EnumerateElementsAlongRay_ExtrudedRaySlice(listMask, ref iterator, intersectSweptBox, l3m, l3x, iAxis, nStep))
						return false;
				}
			}
		}
		return true;
	}

	public void EnumerateElementsAtPoint<IPE>(SpatialPartitionListMask_t listMask,
		in Vector3 pt, bool coarseTest, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		if (listMask == 0) return;

		_lock.EnterReadLock();
		try {
			Voxel v = _voxelHash[0].VoxelIndexFromPoint(pt);
			if (!_voxelHash[0].EnumerateElementsAtPoint(listMask, v, pt, ref iterator)) return;
			v = Voxel.ConvertToNextLevel(v);
			if (!_voxelHash[1].EnumerateElementsAtPoint(listMask, v, pt, ref iterator)) return;
			v = Voxel.ConvertToNextLevel(v);
			if (!_voxelHash[2].EnumerateElementsAtPoint(listMask, v, pt, ref iterator)) return;
			v = Voxel.ConvertToNextLevel(v);
			_voxelHash[3].EnumerateElementsAtPoint(listMask, v, pt, ref iterator);
		}
		finally { _lock.ExitReadLock(); }
	}

	public void ReportStats(ReadOnlySpan<char> fileName) {
		Msg("Histogram : Entities per level\n");
		for (int i = 0; i < _levelCount; ++i)
			Msg($"\t{i} - {_voxelHash[i].EntityCount()}\n");
	}

	static bool IsPointInBox(in Vector3 pt, in Vector3 mins, in Vector3 maxs) =>
		pt.X >= mins.X && pt.X <= maxs.X &&
		pt.Y >= mins.Y && pt.Y <= maxs.Y &&
		pt.Z >= mins.Z && pt.Z <= maxs.Z;

	static void ClampStartPoint(ref Ray ray, in Vector3 vecEnd) {
		for (int axis = 0; axis < 3; ++axis) {
			float d = GetAxis(ray.Delta, axis);
			if (MathF.Abs(d) < 1e-10f) continue;
			float s = GetAxis(ray.Start, axis);

			if (d > 0f && s < MIN_COORD_FLOAT) {
				float t = (MIN_COORD_FLOAT + 5f - s) / d;
				ray.Start += t * ray.Delta;
			}
			else if (d < 0f && s > MAX_COORD_FLOAT) {
				float t = (s - (MAX_COORD_FLOAT - 5f)) / -d;
				ray.Start += t * ray.Delta;
			}
		}
		ray.Delta = vecEnd - ray.Start;
	}

	static void ClampEndPoint(ref Ray ray, ref Vector3 vecEnd) {
		for (int axis = 0; axis < 3; ++axis) {
			float d = GetAxis(ray.Delta, axis);
			if (MathF.Abs(d) < 1e-10f) continue;
			float e = GetAxis(vecEnd, axis);

			if (d < 0f && e < MIN_COORD_FLOAT) {
				float t = (GetAxis(ray.Start, axis) - (MIN_COORD_FLOAT + 5f)) / -d;
				vecEnd = ray.Start + t * ray.Delta;
			}
			else if (d > 0f && e > MAX_COORD_FLOAT) {
				float t = (GetAxis(ray.Start, axis) - (-MAX_COORD_FLOAT + 5f)) / d;
				vecEnd = ray.Start + t * ray.Delta;
			}
		}
		ray.Delta = vecEnd - ray.Start;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static float GetAxis(in Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static int MinIndex(float a, float b, float c) =>
		a < b ? (a < c ? 0 : 2) : (b < c ? 1 : 2);
}


public sealed class SpatialPartitionImpl : ISpatialPartitionInternal
{
	const int MAX_QUERY_CALLBACK = 3;
	const SpatialPartitionListMask_t PARTITION_ALL_CLIENT_EDICTS = (int)PartitionListMask.AllClientEdicts;

	readonly HandleList _handles = new();
	readonly VoxelTree[] _voxelTrees = new VoxelTree[(int)PartitionTrees.NumTrees];
	readonly IPartitionQueryCallback?[] _queryCallback = new IPartitionQueryCallback[MAX_QUERY_CALLBACK];
	readonly bool[] _useOldQueryCallback = new bool[MAX_QUERY_CALLBACK];
	int _queryCallbackCount;
	SpatialPartitionListMask_t _suppressedListMask;

	public SpatialPartitionImpl() {
		for (int i = 0; i < _voxelTrees.Length; i++)
			_voxelTrees[i] = new VoxelTree();
	}

	internal SpatialEntityInfo EntityInfo(ushort h) => _handles[h];

	VoxelTree VoxelTree(SpatialPartitionListMask_t listMask) {
		int tree = (listMask & PARTITION_ALL_CLIENT_EDICTS) == 0
			? (int)PartitionTrees.ServerTree
			: (int)PartitionTrees.ClientTree;
		return _voxelTrees[tree];
	}

	public void Init(in Vector3 worldmin, in Vector3 worldmax) {
		for (int i = 0; i < (int)PartitionTrees.NumTrees; i++)
			_voxelTrees[i].Init(this, i, worldmin, worldmax);
	}

	void Shutdown() {
		for (int i = 0; i < (int)PartitionTrees.NumTrees; i++)
			_voxelTrees[i].Shutdown();
	}

	public ushort CreateHandle(IHandleEntity? handleEntity) {
		ushort h = _handles.Alloc();
		SpatialEntityInfo info = _handles[h];
		info.HandleEntity = handleEntity;
		info.Min = new Vector3(float.MaxValue);
		info.Max = new Vector3(float.MinValue);
		info.ListMask = 0;
		info.Flags = 0;
		for (int i = 0; i < (int)PartitionTrees.NumTrees; i++) {
			Unsafe.Add(ref Unsafe.As<TreeArray<ushort>, ushort>(ref info.VisitBit), i) = 0xFFFF;
			Unsafe.Add(ref Unsafe.As<TreeArray<sbyte>, sbyte>(ref info.Level), i) = unchecked((sbyte)-1);
			Unsafe.Add(ref Unsafe.As<TreeArray<int>, int>(ref info.LeafListHead), i) = PooledLinkedList<LeafListData>.INVALID_INDEX;
		}
		return h;
	}

	public ushort CreateHandle(IHandleEntity handleEntity, SpatialPartitionListMask_t listMask, in Vector3 mins, in Vector3 maxs) {
		ushort h = CreateHandle(handleEntity);
		Insert(listMask, h);
		InsertIntoTree(h, mins, maxs);
		return h;
	}

	public void DestroyHandle(ushort handle) {
		if (handle == PARTITION_INVALID_HANDLE) return;
		RemoveFromTree(handle);
		_handles.Free(handle);
	}

	public void Insert(SpatialPartitionListMask_t listMask, ushort handle) {
		Debug.Assert(_handles.IsValidIndex(handle));
		_handles[handle].ListMask |= (ushort)listMask;
	}

	public void Remove(SpatialPartitionListMask_t listMask, ushort handle) {
		Debug.Assert(_handles.IsValidIndex(handle));
		_handles[handle].ListMask &= (ushort)~listMask;
	}

	public void RemoveAndInsert(SpatialPartitionListMask_t removeMask, SpatialPartitionListMask_t insertMask, ushort handle) {
		Debug.Assert(_handles.IsValidIndex(handle));
		_handles[handle].ListMask = (ushort)((_handles[handle].ListMask & ~removeMask) | insertMask);
	}

	public void Remove(ushort handle) {
		Debug.Assert(_handles.IsValidIndex(handle));
		_handles[handle].ListMask = 0;
	}

	public SpatialTempHandle_t HideElement(ushort handle) {
		Debug.Assert(_handles.IsValidIndex(handle));
		_handles[handle].Flags |= EntityInfoFlags.Hidden;
		return 1;
	}

	public void UnhideElement(ushort handle, SpatialTempHandle_t tempHandle) {
		Debug.Assert(_handles.IsValidIndex(handle));
		_handles[handle].Flags &= ~EntityInfoFlags.Hidden;
	}

	void InsertIntoTree(ushort hPartition, in Vector3 mins, in Vector3 maxs) {
		SpatialEntityInfo info = _handles[hPartition];
		SpatialPartitionListMask_t listMask = info.ListMask;

		if ((int)PartitionTrees.ClientTree != (int)PartitionTrees.ServerTree) {
			if ((listMask & PARTITION_ALL_CLIENT_EDICTS) != 0 && (info.Flags & EntityInfoFlags.InClientTree) == 0) {
				_voxelTrees[(int)PartitionTrees.ClientTree].InsertIntoTree(hPartition, mins, maxs);
				info.Flags |= EntityInfoFlags.InClientTree;
			}
			if ((listMask & ~PARTITION_ALL_CLIENT_EDICTS) != 0 && (info.Flags & EntityInfoFlags.InServerTree) == 0) {
				_voxelTrees[(int)PartitionTrees.ServerTree].InsertIntoTree(hPartition, mins, maxs);
				info.Flags |= EntityInfoFlags.InServerTree;
			}
		}
		else if ((info.Flags & EntityInfoFlags.InClientTree) == 0) {
			_voxelTrees[(int)PartitionTrees.ClientTree].InsertIntoTree(hPartition, mins, maxs);
			info.Flags |= EntityInfoFlags.InClientTree;
		}
	}

	void RemoveFromTree(ushort hPartition) {
		SpatialEntityInfo info = EntityInfo(hPartition);

		if ((info.Flags & EntityInfoFlags.InClientTree) != 0) {
			_voxelTrees[(int)PartitionTrees.ClientTree].RemoveFromTree(hPartition);
			info.Flags &= ~EntityInfoFlags.InClientTree;
		}
		if ((info.Flags & EntityInfoFlags.InServerTree) != 0) {
			_voxelTrees[(int)PartitionTrees.ServerTree].RemoveFromTree(hPartition);
			info.Flags &= ~EntityInfoFlags.InServerTree;
		}
	}

	public void ElementMoved(ushort handle, in Vector3 mins, in Vector3 maxs) {
		SpatialEntityInfo info = _handles[handle];
		SpatialPartitionListMask_t listMask = info.ListMask;

		if ((int)PartitionTrees.ClientTree != (int)PartitionTrees.ServerTree) {
			if ((listMask & PARTITION_ALL_CLIENT_EDICTS) != 0) {
				_voxelTrees[(int)PartitionTrees.ClientTree].ElementMoved(handle, mins, maxs);
				info.Flags |= EntityInfoFlags.InClientTree;
			}
			if ((listMask & ~PARTITION_ALL_CLIENT_EDICTS) != 0) {
				_voxelTrees[(int)PartitionTrees.ServerTree].ElementMoved(handle, mins, maxs);
				info.Flags |= EntityInfoFlags.InServerTree;
			}
		}
		else {
			_voxelTrees[(int)PartitionTrees.ClientTree].ElementMoved(handle, mins, maxs);
			info.Flags |= EntityInfoFlags.InClientTree;
		}
	}

	public void InstallQueryCallback(IPartitionQueryCallback? callback) {
		if (callback == null || _queryCallbackCount >= MAX_QUERY_CALLBACK) return;
		_queryCallback[_queryCallbackCount] = callback;
		_useOldQueryCallback[_queryCallbackCount] = false;
		++_queryCallbackCount;
	}

	public void InstallQueryCallback_V1(IPartitionQueryCallback? callback) {
		if (callback == null || _queryCallbackCount >= MAX_QUERY_CALLBACK) return;
		_queryCallback[_queryCallbackCount] = callback;
		_useOldQueryCallback[_queryCallbackCount] = true;
		++_queryCallbackCount;
	}

	public void RemoveQueryCallback(IPartitionQueryCallback? callback) {
		if (callback == null) return;
		for (int i = _queryCallbackCount - 1; i >= 0; --i) {
			if (_queryCallback[i] == callback) {
				--_queryCallbackCount;
				_queryCallback[i] = _queryCallback[_queryCallbackCount];
				_useOldQueryCallback[i] = _useOldQueryCallback[_queryCallbackCount];
				return;
			}
		}
	}

	void InvokeQueryCallbacks(SpatialPartitionListMask_t listMask, bool done = false) {
		for (int i = 0; i < _queryCallbackCount; ++i) {
			if (!done) {
				if (_useOldQueryCallback[i])
					_queryCallback[i]!.OnPreQuery_V1();
				else
					_queryCallback[i]!.OnPreQuery(listMask);
			}
			else {
				if (!_useOldQueryCallback[i])
					_queryCallback[i]!.OnPostQuery(listMask);
			}
		}
	}

	public void SuppressLists(SpatialPartitionListMask_t nListMask, bool suppress) {
		if (suppress) _suppressedListMask |= nListMask;
		else _suppressedListMask &= ~nListMask;
	}

	public SpatialPartitionListMask_t GetSuppressedLists() => _suppressedListMask;

	public void EnumerateElementsInBox<IPE>(SpatialPartitionListMask_t listMask, in Vector3 mins, in Vector3 maxs,
		bool coarseTest, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		VoxelTree tree = VoxelTree(listMask);
		InvokeQueryCallbacks(listMask);
		tree.EnumerateElementsInBox(listMask, mins, maxs, coarseTest, ref iterator);
		InvokeQueryCallbacks(listMask, true);
	}

	public void EnumerateElementsInSphere<IPE>(SpatialPartitionListMask_t listMask, in Vector3 origin, float radius,
		bool coarseTest, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		VoxelTree tree = VoxelTree(listMask);
		InvokeQueryCallbacks(listMask);
		tree.EnumerateElementsInSphere(listMask, origin, radius, coarseTest, ref iterator);
		InvokeQueryCallbacks(listMask, true);
	}

	public void EnumerateElementsAlongRay<IPE>(SpatialPartitionListMask_t listMask, in Ray ray,
		bool coarseTest, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		VoxelTree tree = VoxelTree(listMask);
		InvokeQueryCallbacks(listMask);
		tree.EnumerateElementsAlongRay(listMask, ray, coarseTest, ref iterator);
		InvokeQueryCallbacks(listMask, true);
	}

	public void EnumerateElementsAtPoint<IPE>(SpatialPartitionListMask_t listMask, in Vector3 pt,
		bool coarseTest, scoped ref IPE iterator) where IPE : struct, IPartitionEnumerator, allows ref struct {
		VoxelTree tree = VoxelTree(listMask);
		InvokeQueryCallbacks(listMask);
		tree.EnumerateElementsAtPoint(listMask, pt, coarseTest, ref iterator);
		InvokeQueryCallbacks(listMask, true);
	}

	public void RenderLeafsForRayTraceStart(TimeUnit_t time) { }
	public void RenderLeafsForRayTraceEnd() { }
	public void RenderLeafsForHullTraceStart(TimeUnit_t time) { }
	public void RenderLeafsForHullTraceEnd() { }
	public void RenderLeafsForBoxStart(TimeUnit_t time) { }
	public void RenderLeafsForBoxEnd() { }
	public void RenderLeafsForSphereStart(TimeUnit_t time) { }
	public void RenderLeafsForSphereEnd() { }
	public void RenderObjectsInBox(in Vector3 min, in Vector3 max, TimeUnit_t time) { }
	public void RenderObjectsInSphere(in Vector3 center, float radius, TimeUnit_t time) { }
	public void RenderObjectsAlongRay(in Ray ray, TimeUnit_t time) { }

	public void RenderAllObjectsInTree(TimeUnit_t time) {

	}

	public void RenderObjectsInPlayerLeafs(in Vector3 playerMin, in Vector3 layerMax, TimeUnit_t time) {

	}

	public void ReportStats(ReadOnlySpan<char> fileName) {
		Msg($"Handle Count {_handles.Count}\n");
		for (int i = 0; i < (int)PartitionTrees.NumTrees; i++)
			_voxelTrees[i].ReportStats(fileName);
	}

	public void DrawDebugOverlays() { }
}
