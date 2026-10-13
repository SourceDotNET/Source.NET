global using static Game.Server.EntityListGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

public static class EntityListGlobals
{
	[ConCommand("report_entities", "List all entities")]
	static void report_entities() {
		if (!Util.IsCommandIssuedByServerAdmin())
			return;

		SortedEntityList list = new();
		BaseEntity? ent = gEntList.FirstEnt();
		while (ent != null) {
			list.AddEntityToList(ent);
			ent = gEntList.NextEnt(ent);
		}

		list.ReportEntityList();
	}
}

internal class EntItem
{
	public EHANDLE Ent;
	public EntItem? Next;
}
public class EntityList
{
	int NumItems;
	EntItem? ItemList;

	public void AddEntity(BaseEntity ent) {
		// check if it's already in the list; if not, add it
		EntItem e = ItemList;
		while (e != null) {
			if (e.Ent.Get<BaseEntity>() == ent) {
				// it's already in the list
				return;
			}

			if (e.Next == null) {
				// we've hit the end of the list, so tack it on
				e.Next = new EntItem();
				e.Next.Ent.Set(ent);
				e.Next.Next = null;
				NumItems++;
				return;
			}

			e = e.Next;
		}

		// empty list
		ItemList = new EntItem();
		ItemList.Ent.Set(ent);
		ItemList.Next = null;
		NumItems = 1;
	}

	public void DeleteEntity(BaseEntity ent) {
		// find the entry in the list and delete it
		EntItem? prev = null, e = ItemList;
		while (e != null) {
			// delete the link if it's the matching entity OR if the link is NULL
			if (e.Ent.Get<BaseEntity>() == ent || e.Ent.Get<BaseEntity>() == null) {
				if (prev != null)
					prev.Next = e.Next;
				else
					ItemList = e.Next;

				NumItems--;

				// REVISIT: Is this correct?  Is this just here to clean out dead EHANDLEs?
				// restart the loop
				e = ItemList;
				prev = null;
				continue;
			}

			prev = e;
			e = e.Next;
		}
	}
}

class SortedEntityList
{
	private readonly List<BaseEntity?> SortedList = new();
	private readonly EntityReportLess Comparer = new();
	private int EmptyCount;

	private sealed class EntityReportLess : IComparer<BaseEntity?>
	{
		public int Compare(BaseEntity? src1, BaseEntity? src2) {
			if (src1 == null && src2 == null)
				return 0;
			if (src1 == null)
				return -1;
			if (src2 == null)
				return 1;

			return src1.GetClassname().CompareTo(src2.GetClassname(), StringComparison.Ordinal);
		}
	}

	public void AddEntityToList(BaseEntity? entity) {
		if (entity == null) {
			EmptyCount++;
			return;
		}

		int index = SortedList.BinarySearch(entity, Comparer);
		if (index < 0)
			index = ~index;

		SortedList.Insert(index, entity);
	}

	public void ReportEntityList() {
		ReadOnlySpan<char> lastClass = default;
		int count = 0;
		int edicts = 0;

		for (int i = 0; i < SortedList.Count; i++) {
			var entity = SortedList[i];
			if (entity == null)
				continue;

			if (entity.Edict() != null)
				edicts++;

			ReadOnlySpan<char> className = entity.GetClassname();

			if (!className.Equals(lastClass, StringComparison.Ordinal)) {
				if (count > 0)
					Msg($"Class: {lastClass} ({count})\n");

				lastClass = className;
				count = 1;
			}
			else {
				count++;
			}
		}

		if (!lastClass.IsEmpty && count > 0)
			Msg($"Class: {lastClass} ({count})\n");

		if (SortedList.Count > 0)
			Msg($"Total {SortedList.Count} entities ({EmptyCount} empty, {edicts} edicts)\n");

		for (int i = 0; i < SortedList.Count; i++) {
			BaseEntity ent = SortedList[i];
			Msg($"  -[{ent.EntIndex()}]: {ent.GetClassname()} at {ent.GetAbsOrigin()}\n");
		}
	}
}

public class GlobalEntityList : BaseEntityList
{
	int HighestEnt;
	int NumEnts;
	int NumEdicts;

	bool ClearingEntities;
	readonly List<IEntityListener> EntityListeners = [];

	public GlobalEntityList() {
		HighestEnt = NumEnts = NumEdicts = 0;
		ClearingEntities = false;
	}

	readonly static List<IServerNetworkable> g_DeleteList = [];

	public void AddToDeleteList(IServerNetworkable? ent) {
		if (ent != null && ent.GetEntityHandle().GetRefEHandle().Index != Constants.INVALID_EHANDLE_INDEX)
			g_DeleteList.Add(ent);
	}

	public IServerNetworkable? GetServerNetworkable(BaseHandle hEnt) {
		IServerUnknown? unk = (IServerUnknown?)LookupEntity(hEnt);
		return unk?.GetNetworkable();
	}

	public BaseEntity? GetBaseEntity(BaseHandle ent) {
		IServerUnknown? unk = (IServerUnknown?)LookupEntity(ent);
		return unk == null ? null : (BaseEntity?)unk.GetBaseEntity();
	}

	public int NumberOfEntities() => NumEnts;
	public int NumberOfEdicts() => NumEdicts;
	public bool IsClearingEntities() => ClearingEntities;

	public BaseEntity? FirstEnt() => NextEnt(null);

	public BaseEntity? NextEnt(BaseEntity? currentEnt) {
		if (currentEnt == null) {
			EntInfo? info = FirstEntInfo();
			if (info == null)
				return null;

			return (BaseEntity?)info.Entity;
		}

		EntInfo? list = GetEntInfoPtr(currentEnt.GetRefEHandle());
		if (list != null)
			list = NextEntInfo(list);

		while (list != null) {
			return (BaseEntity?)list.Entity;
			list = NextEntInfo(list); //??
		}

		return null;
	}

	public T? NextEntByClass<T>(T? start) where T : BaseEntity {
		for (BaseEntity? x = NextEnt(start); x != null; x = NextEnt(x)) {
			if (x is T found)
				return found;
		}
		return null;
	}

	public void AddListenerEntity(IEntityListener listener) {
		if (EntityListeners.Contains(listener)) {
			Assert(false, "Can't add listeners multiple times\n");
			return;
		}
		EntityListeners.Add(listener);
	}

	public void RemoveListenerEntity(IEntityListener listener) => EntityListeners.Remove(listener);

	public void CleanupDeleteList() {
		PhysOnCleanupDeleteList();

		Util.g_bDisableEhandleAccess = true;
		for (int i = 0; i < g_DeleteList.Count; i++)
			g_DeleteList[i].Release();
		Util.g_bDisableEhandleAccess = false;
		g_DeleteList.Clear();
	}

	public int ResetDeleteList() {
		int result = g_DeleteList.Count;
		g_DeleteList.Clear();
		return result;
	}

	public BaseEntity? FindEntityByClassname(BaseEntity? startEntity, ReadOnlySpan<char> className) {
		EntInfo? info = startEntity != null ? GetEntInfoPtr(startEntity.GetRefEHandle()).Next : FirstEntInfo();

		for (; info != null; info = info.Next) {
			BaseEntity? ent = (BaseEntity?)info.Entity;
			if (ent == null) {
				DevWarning("NULL entity in global entity list!\n");
				continue;
			}

			if (ent.ClassMatches(className))
				return ent;
		}

		return null;
	}

	public BaseEntity? FindEntityByTarget(BaseEntity? startEntity, ReadOnlySpan<char> name) {
		EntInfo? info = startEntity != null ? GetEntInfoPtr(startEntity.GetRefEHandle()).Next : FirstEntInfo();

		for (; info != null; info = info.Next) {
			BaseEntity? ent = (BaseEntity?)info.Entity;
			if (ent == null) {
				DevWarning("NULL entity in global entity list!");
				continue;
			}

			if (ent.Target == null)
				continue;

			if (FStrEq(ent.Target, name))
				return ent;
		}

		return null;
	}

	public BaseEntity? FindEntityClassNearestFacing(in Vector3 origin, in Vector3 facing, float threshold, ReadOnlySpan<char> classname) {
		float bestDot = threshold;
		BaseEntity? bestEnt = null;

		EntInfo? info = FirstEntInfo();

		for (; info != null; info = info.Next) {
			BaseEntity? ent = (BaseEntity?)info.Entity;
			if (ent == null) {
				DevWarning("NULL entity in global entity list!\n");
				continue;
			}

			if (ent.IsPointSized())
				continue;

			Vector3 toEnt = ent.GetAbsOrigin() - origin;

			MathLib.VectorNormalize(ref toEnt);
			float dot = MathLib.DotProduct(facing, toEnt);
			if (dot > bestDot)
				if (BaseEntity.FClassnameIs(ent, classname))
					if (!BaseEntity.FClassnameIs(ent, "worldspawn") && !BaseEntity.FClassnameIs(ent, "soundent")) {
						bestDot = dot;
						bestEnt = ent;
					}
		}

		return bestEnt;
	}

	public BaseEntity? FindEntityNearestFacing(in Vector3 origin, in Vector3 facing, float threshold) {
		float bestDot = threshold;
		BaseEntity? bestEnt = null;

		EntInfo? info = FirstEntInfo();

		for (; info != null; info = info.Next) {
			BaseEntity? ent = (BaseEntity?)info.Entity;
			if (ent == null) {
				DevWarning("NULL entity in global entity list!\n");
				continue;
			}

			if (ent.Edict() == null)
				continue;

			Vector3 toEnt = ent.WorldSpaceCenter() - origin;
			MathLib.VectorNormalize(ref toEnt);

			float dot = MathLib.DotProduct(facing, toEnt);
			if (dot <= bestDot)
				continue;

			if (!FStrEq(ent.Classname, "worldspawn") && !FStrEq(ent.Classname, "soundent")) {
				bestDot = dot;
				bestEnt = ent;
			}
		}

		return bestEnt;
	}

	public BaseEntity? FindEntityByName(BaseEntity? startEntity, ReadOnlySpan<char> name, BaseEntity? searchingEntity = null, BaseEntity? activator = null, BaseEntity? caller = null, IEntityFindFilter filter = null) {
		if (name.IsEmpty)
			return null;

		if (name[0] == '!') {
			if (startEntity == null)
				return FindEntityProcedural(name, searchingEntity, activator, caller);

			return null;
		}

		EntInfo? info = startEntity != null ? GetEntInfoPtr(startEntity.GetRefEHandle()).Next : FirstEntInfo();

		for (; info != null; info = info.Next) {
			BaseEntity? ent = (BaseEntity?)info.Entity;
			if (ent == null) {
				DevWarning("NULL entity in global entity list!\n");
				continue;
			}

			if (ent.Name == null)
				continue;

			if (ent.NameMatches(name)) {
				if (filter != null && !filter.ShouldFindEntity(ent))
					continue;

				return ent;
			}
		}

		return null;
	}

	public BaseEntity? FindEntityProcedural(ReadOnlySpan<char> name, BaseEntity? searchingEntity = null, BaseEntity? activator = null, BaseEntity? caller = null) {
		if (name[0] == '!') {
			ReadOnlySpan<char> pName = name[1..];

			if (pName.SequenceEqual("player"))
				return (BaseEntity?)Util.PlayerByIndex(1);
			else if (pName.SequenceEqual("activator"))
				return activator;
			else if (pName.SequenceEqual("caller"))
				return caller;
			else if (pName.SequenceEqual("self"))
				return searchingEntity;
			else {
				Warning($"Invalid entity search name {name}\n");
				Assert(false);
			}
		}

		return null;
	}

	public BaseEntity? FindEntityByNameWithin(BaseEntity? startEntity, ReadOnlySpan<char> name, in Vector3 src, float radius, BaseEntity? searchingEntity = null, BaseEntity? activator = null, BaseEntity? caller = null) {
		BaseEntity? entity = startEntity;
		float maxDist2 = radius * radius;
		if (maxDist2 == 0)
			return gEntList.FindEntityByName(entity, name, searchingEntity, activator, caller);

		while ((entity = gEntList.FindEntityByName(entity, name, searchingEntity, activator, caller)) != null) {
			if (entity.Edict() == null)
				continue;

			float dist2 = (entity.GetAbsOrigin() - src).LengthSquared();

			if (maxDist2 > dist2)
				return entity;
		}

		return null;
	}

	public BaseEntity? FindEntityByClassnameWithin(BaseEntity? startEntity, ReadOnlySpan<char> name, in Vector3 src, float radius) {
		BaseEntity? entity = startEntity;
		float maxDist2 = radius * radius;
		if (maxDist2 == 0)
			return gEntList.FindEntityByClassname(entity, name);

		while ((entity = gEntList.FindEntityByClassname(entity, name)) != null) {
			if (entity.Edict() == null)
				continue;

			float dist2 = (entity.GetAbsOrigin() - src).LengthSquared();

			if (maxDist2 > dist2)
				return entity;
		}

		return null;
	}

	public BaseEntity? FindEntityGeneric(BaseEntity? startEntity, ReadOnlySpan<char> name, BaseEntity? searchingEntity = null, BaseEntity? activator = null, BaseEntity? caller = null) {
		BaseEntity? entity = FindEntityByName(startEntity, name, searchingEntity, activator, caller);
		entity ??= FindEntityByClassname(startEntity, name);

		return entity;
	}

	public void NotifyCreateEntity(BaseEntity? ent) {
		if (ent == null)
			return;

		for (int i = EntityListeners.Count - 1; i >= 0; i--)
			EntityListeners[i].OnEntityCreated(ent);
	}

	public void NotifySpawn(BaseEntity? ent) {
		if (ent == null)
			return;

		for (int i = EntityListeners.Count - 1; i >= 0; i--)
			EntityListeners[i].OnEntitySpawned(ent);
	}

	public void NotifyRemoveEntity(BaseHandle hEnt) {
		BaseEntity? ent = GetBaseEntity(hEnt);
		if (ent == null)
			return;

		for (int i = EntityListeners.Count - 1; i >= 0; i--)
			EntityListeners[i].OnEntityDeleted(ent);
	}

	protected override void OnAddEntity(IHandleEntity? pEnt, BaseHandle handle) {
		int i = handle.GetEntryIndex();

		NumEnts++;
		if (i > HighestEnt)
			HighestEnt = i;

		BaseEntity? ent = (BaseEntity?)((IServerUnknown?)pEnt)?.GetBaseEntity();
		if (ent?.Edict() != null)
			NumEdicts++;

		Assert(ent != null);
		for (i = EntityListeners.Count - 1; i >= 0; i--)
			EntityListeners[i].OnEntityCreated(ent!);
	}

	protected override void OnRemoveEntity(IHandleEntity? pEnt, BaseHandle handle) {
		BaseEntity? ent = (BaseEntity?)((IServerUnknown?)pEnt)?.GetBaseEntity();
		if (ent?.Edict() != null)
			NumEdicts--;

		NumEnts--;
	}

	public void Clear() {
		ClearingEntities = true;

		BaseHandle cur = FirstHandle();
		while (cur.IsValid()) {
			IServerNetworkable? ent = GetServerNetworkable(cur);
			if (ent != null)
				Util.Remove(ent);
			cur = NextHandle(cur);
		}

		CleanupDeleteList();
		// DeleteList.Clear();

		HighestEnt = 0;
		NumEnts = 0;
		ClearingEntities = false;
	}

	public Edict? GetEdict(EntityHandle_t ent) {
		IServerUnknown? unk = (IServerUnknown?)LookupEntity(ent);
		if (unk != null)
			return unk.GetNetworkable()!.GetEdict();
		else
			return null;
	}
}

struct SimThinkEntry
{
	public ushort EntEntry;
	public ushort Unused0;
	public int NextThinkTick;
}

public class SimThinkManager : IEntityListener
{
	public static SimThinkManager g_SimThinkManager = new();
	readonly List<SimThinkEntry> SimThinkList = [];
	ushort[] EntInfoIndex = new ushort[Constants.NUM_ENT_ENTRIES];

	public SimThinkManager() => Clear();

	void Clear() {
		SimThinkList.Clear();

		for (int i = 0; i < EntInfoIndex.Length; i++)
			EntInfoIndex[i] = 0xFFFF;
	}

	public void LevelInitPreEntity() => gEntList.AddListenerEntity(this);

	public void LevelShutdownPostEntity() {
		gEntList.RemoveListenerEntity(this);
		Clear();
	}

	public void OnEntityCreated(BaseEntity ent) => Assert(EntInfoIndex[ent.GetRefEHandle().GetEntryIndex()] == 0xFFFF);

	public void OnEntityDeleted(BaseEntity ent) => RemoveEntinfoIndex(ent.GetRefEHandle().GetEntryIndex());

	void RemoveEntinfoIndex(int index) {
		int listHandle = EntInfoIndex[index];
		// If this guy is in the active list, remove him
		if (listHandle != 0xFFFF) {
			Assert(SimThinkList[listHandle].EntEntry == index);
			int last = SimThinkList.Count - 1;
			SimThinkList[listHandle] = SimThinkList[last];
			SimThinkList.RemoveAt(last);
			EntInfoIndex[index] = 0xFFFF;

			// fast remove shifted someone, update that someone
			if (listHandle < SimThinkList.Count)
				EntInfoIndex[SimThinkList[listHandle].EntEntry] = (ushort)listHandle;
		}
	}

	public int ListCount() => SimThinkList.Count;

	public int ListCopy(BaseEntity[] list, int listMax) {
		int count = Math.Min(listMax, SimThinkList.Count);
		int outCount = 0;
		for (int i = 0; i < count; i++) {
			if (SimThinkList[i].NextThinkTick <= gpGlobals.TickCount) {
				Assert(SimThinkList[i].NextThinkTick >= 0);
				int entinfoIndex = SimThinkList[i].EntEntry;
				EntInfo? info = gEntList.GetEntInfoPtrByIndex(entinfoIndex);
				BaseEntity? ent = (BaseEntity?)info?.Entity;
				Assert(ent != null);
				list[outCount++] = ent!;
			}
		}

		return outCount;
	}

	public void EntityChanged(BaseEntity ent) {
		if (ent.IsMarkedForDeletion())
			return;

		BaseHandle eh = ent.GetRefEHandle();
		if (!eh.IsValid())
			return;

		int index = eh.GetEntryIndex();
		if (ent.IsEFlagSet(EFL.NoThinkFunction) && ent.IsEFlagSet(EFL.NoGamePhysicsSimulation))
			RemoveEntinfoIndex(index);
		else {
			if (EntInfoIndex[index] == 0xFFFF) {
				EntInfoIndex[index] = (ushort)SimThinkList.Count;
				SimThinkList.Add(new SimThinkEntry() {
					EntEntry = (ushort)index,
					NextThinkTick = 0
				});
				if (ent.IsEFlagSet(EFL.NoGamePhysicsSimulation)) {
					SimThinkList[EntInfoIndex[index]] = new SimThinkEntry() {
						EntEntry = (ushort)index,
						NextThinkTick = ent.GetFirstThinkTick()
					};
					Assert(SimThinkList[EntInfoIndex[index]].NextThinkTick >= 0);
				}
			}
			else {
				if (ent.IsEFlagSet(EFL.NoGamePhysicsSimulation)) {
					SimThinkList[EntInfoIndex[index]] = new SimThinkEntry() {
						EntEntry = (ushort)index,
						NextThinkTick = ent.GetFirstThinkTick()
					};
					Assert(SimThinkList[EntInfoIndex[index]].NextThinkTick >= 0);
				}
				else
					SimThinkList[EntInfoIndex[index]] = new SimThinkEntry() {
						EntEntry = (ushort)index,
						NextThinkTick = 0
					};
			}
		}
	}
}

public struct RespawnEntitiesFilter : IMapEntityFilter
{
	public BaseEntity? CreateNextEntity(ReadOnlySpan<char> className) => CreateEntityByName(className);
	public bool ShouldCreateEntity(ReadOnlySpan<char> className) => stricmp(className, "worldspawn") != 0;
}

public class EntityTouchManager : IEntityListener
{
	public static readonly EntityTouchManager g_TouchManager = new();

	readonly List<BaseEntity> UpdateList = [];

	public static void EntityTouch_Add(BaseEntity entity) => g_TouchManager.AddEntity(entity);

	// called by EntityListSystem
	public void LevelInitPreEntity() {
		gEntList.AddListenerEntity(this);
		Clear();
	}

	public void LevelShutdownPostEntity() {
		gEntList.RemoveListenerEntity(this);
		Clear();
	}

	public void Clear() {
		UpdateList.Clear();
	}

	// IEntityListener
	public void OnEntityCreated(BaseEntity entity) { }
	public void OnEntityDeleted(BaseEntity entity) {
		if (!entity.GetCheckUntouch())
			return;
		int index = UpdateList.IndexOf(entity);
		if (index >= 0) {
			int last = UpdateList.Count - 1;
			UpdateList[index] = UpdateList[last];
			UpdateList.RemoveAt(last);
		}
	}

	public void AddEntity(BaseEntity entity) {
		if (entity.IsMarkedForDeletion())
			return;
		UpdateList.Add(entity);
	}

	public void FrameUpdatePostEntityThink() {
		// Loop through all entities again, checking their untouch if flagged to do so

		int count = UpdateList.Count;
		if (count != 0) {
			// copy off the list
			BaseEntity[] ents = UpdateList.ToArray();
			// clear it
			UpdateList.Clear();

			// now update those ents
			for (int i = 0; i < count; i++) {
				//Assert( ents[i]->GetCheckUntouch() );
				if (ents[i].GetCheckUntouch())
					ents[i].PhysicsCheckForEntityUntouch();
			}
		}
	}
}

public class EntityListSystem : AutoGameSystemPerFrame
{
	public static EntityListSystem g_EntityListSystem = new();
	public static void RespawnEntities() => g_EntityListSystem.RespawnAllEntities = true;
	bool RespawnAllEntities;

	public override void LevelInitPreEntity() {
		SimThinkManager.g_SimThinkManager.LevelInitPreEntity();
		EntityTouchManager.g_TouchManager.LevelInitPreEntity();
	}
	public override void LevelShutdownPostEntity() {
		SimThinkManager.g_SimThinkManager.LevelShutdownPostEntity();
		EntityTouchManager.g_TouchManager.LevelShutdownPostEntity();
	}

	public override void FrameUpdatePostEntityThink() {
		EntityTouchManager.g_TouchManager.FrameUpdatePostEntityThink();

		if (RespawnAllEntities) {
			RespawnAllEntities = false;

			// Don't change globalstate owing to deletion here
			GlobalEntity.EnableStateUpdates(false);

			// Remove all entities
			int nPlayerIndex = -1;
			BaseEntity? ent = gEntList.FirstEnt();
			while (ent != null) {
				BaseEntity? nextEnt = gEntList.NextEnt(ent);
				if (ent.IsPlayer())
					nPlayerIndex = ent.EntIndex();

				if (!ent.IsEFlagSet(EFL.KeepOnRecreateEntities))
					Util.Remove(ent);

				ent = nextEnt;
			}

			gEntList.CleanupDeleteList();

			GlobalEntity.EnableStateUpdates(true);

			// Allows us to immediately re-use the edict indices we just freed to avoid edict overflow
			engine.AllowImmediateEdictReuse();

			// Reset node counter used during load
			NodeEnt.NodeCount = 0;

			RespawnEntitiesFilter filter = new();
			MapEntity_ParseAllEntities(engine.GetMapEntitiesString(), filter, true);

			// Allocate a CBasePlayer for pev, and call spawn
			if (nPlayerIndex >= 0) {
				Edict? edict = engine.PEntityOfEntIndex(nPlayerIndex);
				ClientPutInServer(edict, "unnamed");
				ClientActive(edict, false);

				BasePlayer? player = (BasePlayer?)BaseEntity.Instance(edict);
				// SceneManager_ClientActive(player);
			}
		}
	}
}

public enum NotifySystemEvent
{
	Teleport,
	Destroy
}
public struct NotifyTeleportParams
{
	public Vector3 PrevOrigin;
	public QAngle PrevAngles;
	public bool PhysicsRotate;
}

public struct NotifyDestroyParams;

public ref struct NotifySystemEventParams
{
	public ref readonly NotifyTeleportParams Teleport;
	public ref readonly NotifyDestroyParams Destroy;

	public NotifySystemEventParams(in NotifyTeleportParams parms) => Teleport = ref parms;
	public NotifySystemEventParams(in NotifyDestroyParams parms) => Destroy = ref parms;
}

public interface INotify
{
	void AddEntity(BaseEntity notify, BaseEntity watched);

	// Remove notification for an entity
	void RemoveEntity(BaseEntity notify, BaseEntity watched);

	// Call the named input in each entity who is watching pEvent's status
	void ReportNamedEvent(BaseEntity entity, ReadOnlySpan<char> eventName);

	// System events don't make sense as inputs, so are handled through a generic notify function
	void ReportSystemEvent(BaseEntity entity, NotifySystemEvent eventType, in NotifySystemEventParams parms);

	public void ReportDestroyEvent(BaseEntity entity) {
		NotifyDestroyParams destroy = default;
		ReportSystemEvent(entity, NotifySystemEvent.Destroy, new(in destroy));
	}

	public void ReportTeleportEvent(BaseEntity entity, in Vector3 prevOrigin, in QAngle prevAngles, bool physicsRotate) {
		NotifyTeleportParams teleport = default;
		teleport.PrevOrigin = prevOrigin;
		teleport.PrevAngles = prevAngles;
		teleport.PhysicsRotate = physicsRotate;
		ReportSystemEvent(entity, NotifySystemEvent.Teleport, new(in teleport));
	}

	// Remove this entity from the notify list
	void ClearEntity(BaseEntity notify);
}

public interface IEntityListener
{
	void OnEntityCreated(BaseEntity ent) { }
	void OnEntitySpawned(BaseEntity ent) { }
	void OnEntityDeleted(BaseEntity ent) { }
}

public interface IEntityFindFilter
{
	bool ShouldFindEntity(BaseEntity ent);
	BaseEntity? GetFilterResult();
}
