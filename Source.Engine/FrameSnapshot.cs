
using CommunityToolkit.HighPerformance;

using System.Buffers;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;

namespace Source.Engine;

public struct FrameSnapshotEntry
{
	public ServerClass? Class;
	public int SerialNumber;
	public PackedEntityHandle_t PackedData;
}

public class FrameSnapshot(FrameSnapshotManager frameSnapshotManager) : IDisposable
{
	public void AddReference() {
		Assert(References < 0xFFFF);
		Interlocked.Increment(ref References);
	}

	public void ReleaseReference() {
		Assert(References > 0);
		Interlocked.Decrement(ref References);
		if (References == 0)
			frameSnapshotManager.DeleteFrameSnapshot(this);
	}

	public FrameSnapshot? NextSnapshot() {
		return frameSnapshotManager.NextSnapshot(this);
	}

	public volatile int ListIndex;
	public int TickCount;
	public FrameSnapshotEntry[]? Entities;
	public int NumEntities;
	public ushort[]? ValidEntities;
	public int NumValidEntities;
	public EventInfo[]? TempEntities;
	public int NumTempEntities;
	public readonly List<int> ExplicitDeleteSlots = [];

	volatile int References;

	public void Dispose() {
		if (ValidEntities != null)
			ArrayPool<ushort>.Shared.Return(ValidEntities);
		if (Entities != null)
			ArrayPool<FrameSnapshotEntry>.Shared.Return(Entities);
		ValidEntities = null;
		Entities = null;
		TempEntities = null;
		Assert(References == 0);

		GC.SuppressFinalize(this);
	}
}

public struct UnpackedDataCache
{
	public PackedEntity? Entity;
	public int Counter;
	public int Bits;
	public InlineArrayMaxPackedEntityData<byte> Data;
}

[EngineComponent]
public class FrameSnapshotManager
{
	public const PackedEntityHandle_t INVALID_PACKED_ENTITY_HANDLE = 0;

	static readonly ConVar sv_creationtickcheck = new("sv_creationtickcheck", "1", FCvar.Cheat | FCvar.DevelopmentOnly, "Do extended check for encoding of timestamps against tickcount");

	PackedEntityHandle_t _nextHandle = 1;
	readonly Dictionary<PackedEntityHandle_t, PackedEntity> HandleMap = [];
	PackedEntityHandle_t AllocHandle(PackedEntity entity) {
		var h = _nextHandle++;
		HandleMap[h] = entity;
		return h;
	}
	PackedEntity HandleToEntity(PackedEntityHandle_t handle) => HandleMap[handle];
	void FreeHandle(PackedEntityHandle_t handle) => HandleMap.Remove(handle);

	public virtual void LevelChanged() {
		Assert(FrameSnapshots.Count == 0);

		PackedEntityCache.Clear();
		HandleMap.Clear();
		_nextHandle = 1;
		((Span<PackedEntityHandle_t>)PackedData).Clear();
	}

	public FrameSnapshot CreateEmptySnapshot(int tickcount, int maxEntities) {
		FrameSnapshot snap = new(this);
		snap.AddReference();
		snap.TickCount = tickcount;
		snap.NumEntities = maxEntities;
		snap.NumValidEntities = 0;
		snap.ValidEntities = null;
		snap.Entities = ArrayPool<FrameSnapshotEntry>.Shared.Rent(maxEntities);
		snap.Entities.AsSpan(0, maxEntities).Fill(new FrameSnapshotEntry {
			Class = null,
			SerialNumber = -1,
			PackedData = INVALID_PACKED_ENTITY_HANDLE
		});

		FrameSnapshots.AddLast(snap);
		snap.ListIndex = FrameSnapshots.Count - 1;
		return snap;
	}

	public FrameSnapshot TakeTickSnapshot(int tickcount) {
		FrameSnapshot snap = CreateEmptySnapshot(tickcount, sv.NumEdicts);
		ushort[] validEntities = ArrayPool<ushort>.Shared.Rent(sv.NumEdicts);

		int maxclients = sv.GetClientCount();
		int numValid = 0;

		for (int i = 0; i < sv.NumEdicts; i++) {
			Edict edict = sv.Edicts![i];
			ref FrameSnapshotEntry entry = ref snap.Entities![i];

			IServerUnknown? unk = edict.GetUnknown();

			if (unk == null)
				continue;

			if (edict.IsFree())
				continue;

			if (i > 0 && i <= maxclients) {
				if (!sv.GetClient(i - 1)!.IsActive())
					continue;
			}

			Assert(edict.NetworkSerialNumber != -1);
			Assert(edict.GetNetworkable() != null);
			Assert(edict.GetNetworkable()!.GetServerClass() != null);

			entry.SerialNumber = edict.NetworkSerialNumber;
			entry.Class = edict.GetNetworkable()!.GetServerClass();
			validEntities[numValid++] = (ushort)i;
		}

		snap.NumValidEntities = numValid;
		snap.ValidEntities = validEntities;

		snap.ExplicitDeleteSlots.AddRange(ExplicitDeleteSlots);
		ExplicitDeleteSlots.Clear();

		return snap;
	}

	public FrameSnapshot? NextSnapshot(FrameSnapshot? snapshot) {
		if (snapshot == null)
			return null;

		LinkedListNode<FrameSnapshot>? node = FrameSnapshots.Find(snapshot);
		if (node == null)
			return null;

		return node.Next?.Value;
	}

	public PackedEntity CreatePackedEntity(FrameSnapshot snapshot, int entity) {
		PackedEntity packedEntity = PackedEntitiesPool.Alloc();
		PackedEntityHandle_t handle = AllocHandle(packedEntity);

		Assert(entity < snapshot.NumEntities);

		packedEntity.ReferenceCount = 2;
		packedEntity.EntityIndex = entity;
		snapshot.Entities![entity].PackedData = handle;

		if (PackedData[entity] != INVALID_PACKED_ENTITY_HANDLE)
			RemoveEntityReference(PackedData[entity]);

		PackedData[entity] = handle;
		SerialNumber[entity] = snapshot.Entities[entity].SerialNumber;

		packedEntity.SetSnapshotCreationTick(snapshot.TickCount);

		return packedEntity;
	}

	public PackedEntity? GetPackedEntity(FrameSnapshot? snapshot, int entity) {
		if (snapshot == null)
			return null;

		Assert(entity < snapshot.NumEntities);

		PackedEntityHandle_t index = snapshot.Entities![entity].PackedData;
		if (index == INVALID_PACKED_ENTITY_HANDLE)
			return null;

		PackedEntity packedEntity = HandleToEntity(index);
		Assert(packedEntity.EntityIndex == entity);
		return packedEntity;
	}

	public void AddEntityReference(PackedEntityHandle_t handle) {
		Assert(handle != INVALID_PACKED_ENTITY_HANDLE);
		HandleToEntity(handle).ReferenceCount++;
	}

	public void RemoveEntityReference(PackedEntityHandle_t handle) {
		Assert(handle != INVALID_PACKED_ENTITY_HANDLE);

		PackedEntity packedEntity = HandleToEntity(handle);

		if (--packedEntity.ReferenceCount <= 0) {
			FreeHandle(handle);
			PackedEntitiesPool.Free(packedEntity);

			for (int i = 0; i < PackedEntityCache.Count; i++) {
				UnpackedDataCache pdc = PackedEntityCache[i];
				if (pdc.Entity == packedEntity) {
					pdc.Entity = null;
					pdc.Counter = 0;
					break;
				}
			}
		}
	}

	public bool UsePreviouslySentPacket(FrameSnapshot snapshot, int entity, int entSerialNumber) {
		PackedEntityHandle_t handle = PackedData[entity];
		if (handle != INVALID_PACKED_ENTITY_HANDLE) {
			if (SerialNumber[entity] == entSerialNumber) {
				if (ShouldForceRepack(snapshot, entity, handle))
					return false;

				Assert(entity < snapshot.NumEntities);
				snapshot.Entities![entity].PackedData = handle;
				HandleToEntity(handle).ReferenceCount++;
				return true;
			}

			return false;
		}

		return false;
	}

	public bool ShouldForceRepack(FrameSnapshot snapshot, int entity, PackedEntityHandle_t handle) {
		if (sv_creationtickcheck.GetBool()) {
			PackedEntity pe = HandleToEntity(handle);
			Assert(pe != null);
			if (pe.ShouldCheckCreationTick()) {
				long nCurrentNetworkBase = serverGlobalVariables.GetNetworkBase(snapshot.TickCount, entity);
				long nPackedEntityNetworkBase = serverGlobalVariables.GetNetworkBase(pe.GetSnapshotCreationTick(), entity);
				if (nCurrentNetworkBase != nPackedEntityNetworkBase)
					return true;
			}
		}

		return false;
	}

	public PackedEntity? GetPreviouslySentPacket(int entity, int serialNumber) {
		PackedEntityHandle_t handle = PackedData[entity];
		if (handle != INVALID_PACKED_ENTITY_HANDLE) {
			if (SerialNumber[entity] == serialNumber)
				return HandleToEntity(handle);
		}

		return null;
	}

	UnpackedDataCache oldest = default;

	public ref UnpackedDataCache GetCachedUncompressedEntity(PackedEntity packedEntity) {
		if (PackedEntityCache.Count == 0) {
			PackedEntityCacheCounter = 0;
			for (int i = 0; i < 128; i++) {
				PackedEntityCache.Add(new UnpackedDataCache {
					Entity = null,
					Counter = 0
				});
			}
		}

		PackedEntityCacheCounter++;

		int oldestValue = PackedEntityCacheCounter;

		for (int i = 0; i < PackedEntityCache.Count; i++) {
			ref UnpackedDataCache pdc = ref PackedEntityCache.AsSpan()[i];

			if (pdc.Entity == packedEntity) {
				pdc.Counter = PackedEntityCacheCounter;
				return ref pdc;
			}

			if (pdc.Counter < oldestValue) {
				oldestValue = pdc.Counter;
				oldest = pdc;
			}
		}

		oldest!.Counter = PackedEntityCacheCounter;
		oldest.Bits = -1;
		oldest.Entity = packedEntity;
		return ref oldest;
	}

	public Mutex GetMutex() => WriteMutex;

	public void AddExplicitDelete(int slot) {
		if (!ExplicitDeleteSlots.Contains(slot))
			ExplicitDeleteSlots.Add(slot);
	}

	public void DeleteFrameSnapshot(FrameSnapshot snapshot) {
		for (int i = 0; i < snapshot.NumEntities; i++) {
			if (snapshot.Entities![i].PackedData != INVALID_PACKED_ENTITY_HANDLE) {
				RemoveEntityReference(snapshot.Entities[i].PackedData);
			}
		}

		FrameSnapshots.Remove(snapshot);
		snapshot.Dispose();
	}

	readonly LinkedList<FrameSnapshot> FrameSnapshots = [];
	readonly ObjectPool<PackedEntity> PackedEntitiesPool = new();

	int PackedEntityCacheCounter;
	readonly List<UnpackedDataCache> PackedEntityCache = [];

	InlineArrayMaxEdicts<PackedEntityHandle_t> PackedData;
	InlineArrayMaxEdicts<int> SerialNumber;

	readonly Mutex WriteMutex = new();

	readonly List<int> ExplicitDeleteSlots = [];
}
