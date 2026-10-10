global using static Game.Client.ClientEntityGlobals;

using Game.Shared;

using Source;
using Source.Common;

namespace Game.Client;

public static class ClientEntityGlobals
{
	public static readonly BaseHandle INVALID_CLIENTENTITY_HANDLE = new(Constants.INVALID_EHANDLE_INDEX);
}
public static class ClientEntityExts
{
	public static C_BaseEntity? GetBaseEntity(this IClientUnknown? unk) => unk == null ? null : unk is C_BaseEntity cbe ? cbe : null;
}

public class ClientEntityList : BaseEntityList, IClientEntityList
{
	public IClientEntity? GetClientEntity(int entNum) {
		IClientUnknown? ent = GetListedEntity(entNum);
		return ent?.GetIClientEntity();
	}

	public C_BaseEntity? GetEnt(int entNum) => GetBaseEntity(entNum);

	public C_BaseEntity? GetBaseEntity(int entNum) {
		IClientUnknown? pEnt = GetListedEntity(entNum);
		return pEnt?.GetBaseEntity();
	}

	public IClientUnknown? GetListedEntity(int entNum) => (IClientUnknown?)LookupEntityByNetworkIndex(entNum);

	public IClientEntity? GetClientEntityFromHandle(in BaseHandle ent) {
		IClientUnknown? pEnt = GetClientUnknownFromHandle(ent);
		return pEnt == null ? null : pEnt.GetIClientEntity();
	}

	public IClientNetworkable? GetClientNetworkable(int entnNum) {
		return EntityCacheInfo[entnNum].Networkable;
	}

	public IClientNetworkable? GetClientNetworkableFromHandle(in BaseHandle ent) {
		IClientUnknown? pEnt = GetClientUnknownFromHandle(ent);
		return pEnt == null ? null : pEnt.GetClientNetworkable();
	}

	public void Release() {
		BaseHandle iter = FirstHandle();
		while (iter != InvalidHandle()) {
			IClientNetworkable? net = GetClientNetworkableFromHandle(iter);
			if (net != null)
				net.Release();
			else {
				IClientThinkable? thinkable = GetClientThinkableFromHandle(iter);
				thinkable?.Release();
			}
			RemoveEntity(iter);

			iter = FirstHandle();
		}

		NumServerEnts = 0;
		MaxServerEnts = 0;
		NumClientNonNetworkable = 0;
		MaxUsedServerIndex = -1;
	}

	public IClientUnknown? GetClientUnknownFromHandle(in BaseHandle ent) {
		return (IClientUnknown?)LookupEntity(ent);
	}

	public IClientRenderable? GetClientRenderableFromHandle(in BaseHandle ent) {
		IClientUnknown? pEnt = GetClientUnknownFromHandle(ent);
		return pEnt == null ? null : pEnt.GetClientRenderable();
	}

	public int GetHighestEntityIndex() {
		return MaxUsedServerIndex;
	}

	public int GetMaxEntities() {
		throw new NotImplementedException();
	}

	public int NumberOfEntities(bool includeNonNetworkable) {
		throw new NotImplementedException();
	}

	public void SetMaxEntities(int maxEnts) {
		MaxServerEnts = maxEnts;
	}

	readonly Dictionary<IClientUnknown, LinkedListNode<PVSNotifyInfo>> PVSNotifierMap = [];
	readonly LinkedList<PVSNotifyInfo> PVSNotifyInfos = [];

	private void AddPVSNotifier(IClientUnknown? unknown) {
		if (unknown == null)
			return;

		IClientRenderable? renderable = unknown.GetClientRenderable();
		if (renderable != null) {
			IPVSNotify? notify = renderable.GetPVSNotifyInterface();
			if (notify != null) {
				LinkedListNode<PVSNotifyInfo> node = PVSNotifyInfos.AddLast(new PVSNotifyInfo());
				PVSNotifyInfo info = node.Value;
				info.Notify = notify;
				info.Renderable = renderable;
				info.InPVSStatus = 0;
				info.Node = node;

				PVSNotifierMap[unknown] = node;
			}
		}
	}

	private void RemovePVSNotifier(IClientUnknown? unknown) {
		if (unknown == null)
			return;

		IClientRenderable? renderable = unknown.GetClientRenderable();
		if (renderable != null) {
			IPVSNotify? notify = renderable.GetPVSNotifyInterface();
			if (notify != null) {
				if (!PVSNotifierMap.TryGetValue(unknown, out LinkedListNode<PVSNotifyInfo>? notifyInfo)) {
					Warning("PVS notifier not in m_PVSNotifierMap\n");
					Assert(false);
					return;
				}

				Assert(notifyInfo.Value.Notify == notify);
				Assert(notifyInfo.Value.Renderable == renderable);

				PVSNotifyInfos.Remove(notifyInfo);
				PVSNotifierMap.Remove(unknown);
				return;
			}
		}
	}

	protected override void OnAddEntity(IHandleEntity? ent, BaseHandle handle) {
		int entnum = handle.GetEntryIndex();
		ref EntityCacheInfo_t cache = ref EntityCacheInfo[entnum];

		if (entnum >= 0 && entnum < Constants.MAX_EDICTS) {
			NumServerEnts++;
			if (entnum > MaxUsedServerIndex)
				MaxUsedServerIndex = entnum;

			Assert(ent is IClientUnknown unk && unk.GetClientNetworkable() != null);
			cache.Networkable = ((IClientUnknown)ent).GetClientNetworkable();
		}

		IClientUnknown? unknown = (IClientUnknown?)ent;

		AddPVSNotifier(unknown);

		C_BaseEntity? baseEntity = unknown.GetBaseEntity();
		if (baseEntity != null) {
			cache.LinkedListNode = BaseEntities.AddLast(baseEntity);

			if ((baseEntity.ObjectCaps() & EntityCapabilities.SaveNonNetworkable) != 0)
				NumClientNonNetworkable++;

			for (int i = EntityListeners.Count() - 1; i >= 0; i--)
				EntityListeners[i].OnEntityCreated(baseEntity);
		}
		else
			cache.LinkedListNode = null;
	}

	int NumServerEnts;
	int MaxServerEnts;
	int NumClientNonNetworkable;
	int MaxUsedServerIndex;

	public List<IClientEntityListener> EntityListeners = [];
	public void AddListenerEntity(IClientEntityListener listener) {

	}
	public void RemoveListenerEntity(IClientEntityListener listener) {

	}

	public BaseHandle InvalidHandle() => new(Constants.INVALID_EHANDLE_INDEX);

	public IClientThinkable? GetClientThinkableFromHandle(in BaseHandle hEnt) {
		IClientUnknown? pEnt = GetClientUnknownFromHandle(hEnt);
		return pEnt == null ? null : pEnt.GetClientThinkable();
	}

	public BaseEntity? GetBaseEntityFromHandle(ClientEntityHandle hEnt) {
		IClientUnknown? pEnt = GetClientUnknownFromHandle(hEnt);
		return pEnt != null ? pEnt.GetBaseEntity() : null;
	}

	internal BaseEntity? FirstBaseEntity() {
		EntInfo? list = FirstEntInfo();
		while (list != null) {
			if (list.Entity != null) {
				IClientUnknown unk = (IClientUnknown)list.Entity;
				C_BaseEntity? ret = unk.GetBaseEntity();
				if (ret != null)
					return ret;
			}
			list = list.Next;
		}

		return null;
	}

	internal BaseEntity? NextBaseEntity(BaseEntity ?ent) {
		if (ent == null)
			return FirstBaseEntity();

		// Run through the list until we get a C_BaseEntity.
		EntInfo? list = GetEntInfoPtr(ent.GetRefEHandle());
		if (list != null) 
			list = NextEntInfo(list);

		while (list != null) {
			if (list.Entity != null) {
				IClientUnknown unk = (IClientUnknown)list.Entity;
				C_BaseEntity? ret = unk.GetBaseEntity();
				if (ret != null)
					return ret;
			}
			list = list.Next;
		}

		return null;
	}

	class PVSNotifyInfo
	{
		public IPVSNotify? Notify;
		public IClientRenderable? Renderable;
		public InPVS InPVSStatus;
		public LinkedListNode<PVSNotifyInfo>? Node;
	}

	struct EntityCacheInfo_t
	{
		public IClientNetworkable Networkable;
		public LinkedListNode<C_BaseEntity>? LinkedListNode;
	}

	InlineArrayNumEntEntries<EntityCacheInfo_t> EntityCacheInfo;
	public readonly LinkedList<C_BaseEntity> BaseEntities = [];
}

public interface IClientEntityListener
{
	void OnEntityCreated(C_BaseEntity ent);
	void OnEntityDeleted(C_BaseEntity ent);
}
