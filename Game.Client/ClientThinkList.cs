global using static Game.Client.CClientThinkList;

using Game.Shared;

using Source;
using Source.Common;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
namespace Game.Client;

public class CClientThinkList : IGameSystemPerFrame
{
	public const double CLIENT_THINK_ALWAYS = -1293;
	public const double CLIENT_THINK_NEVER	= -1;

	public static ClientThinkHandle_t INVALID_THINK_HANDLE => ClientThinkList().GetInvalidThinkHandle();

	public ReadOnlySpan<char> Name() => "CClientThinkList";
	public bool IsPerFrame() => true;

	public bool Init() {

		return true;
	}

	public void PostInit() { }
	public void Shutdown() { }
	public void LevelInitPreEntity() { }
	public void LevelShutdownPreClearSteamAPIContext() { }
	public void OnSave() { }
	public void OnRestore() { }
	public void SafeRemoveIfDesired() { }
	public void LevelInitPostEntity() {
		IterEnum = 0;
	}
	public void LevelShutdownPreEntity() { }
	public void LevelShutdownPostEntity() { }
	public void PreRender() { }
	public void PostRender() { }
	public void Update(TimeUnit_t frametime) { }

	// These are poolable objects for memory efficiency rather than a linked list esque system
	class ThinkEntry : IPoolableObject
	{
		public ClientEntityHandle Ent = new();
		public TimeUnit_t NextClientThink;
		public TimeUnit_t LastClientThink;
		public int IterEnum;

		public void Init() { }
		public void Reset() {
			Ent.Invalidate();
			NextClientThink = 0;
			LastClientThink = 0;
			IterEnum = 0;
		}
	}

	class ThinkListChanges : IPoolableObject
	{
		public ClientEntityHandle Ent = new();
		public ClientThinkHandle_t Think;
		public TimeUnit_t NextTime;

		public void Init() {}
		public void Reset() {
			Ent.Invalidate();
			Think = 0;
			NextTime = 0;
		}
	}

	public void SetNextClientThink(ClientEntityHandle hEnt, TimeUnit_t nextTime) {
		if(nextTime == CLIENT_THINK_NEVER) {
			RemoveThinkable(hEnt);
			return;
		}

		IClientThinkable? think = cl_entitylist.GetClientThinkableFromHandle(hEnt);
		if (think == null)
			return;

		ClientThinkHandle_t hThink = think.GetThinkHandle();
		if (InThinkLoop) {
			int i = ChangeList.AddToTail();
			ChangeList[i].Ent.Init(hEnt);
			ChangeList[i].Think = hThink;
			ChangeList[i].NextTime = nextTime;
			return;
		}

		if(hThink == INVALID_THINK_HANDLE) {
			hThink = ThinkEntries.AddToTail();
			think.SetThinkHandle(hThink);
			ThinkEntry entry = ThinkEntries[hThink];
			entry.Ent.Init(hEnt);
			entry.IterEnum = -1;
			entry.LastClientThink = 0.0;
		}

		GetThinkEntry(hThink).NextClientThink = nextTime;
	}
	public void RemoveThinkable(ClientEntityHandle hEnt) {
		IClientThinkable? pThink = cl_entitylist.GetClientThinkableFromHandle(hEnt);
		if (pThink != null) {
			ClientThinkHandle_t hThink = pThink.GetThinkHandle();
			if (hThink != INVALID_THINK_HANDLE) {
				Assert(GetThinkEntry(hThink).Ent == hEnt);
				RemoveThinkable(hThink);
			}
		}
	}
	public ClientThinkHandle_t GetInvalidThinkHandle() {
		return unchecked((ClientThinkHandle_t)~0);
	}
	public void PerformThinkFunctions() {
		int nMaxList = ThinkEntries.Count();
		if (nMaxList == 0)
			return;

		++IterEnum;

		// Build a list of entities to think this frame, in order of hierarchy.
		// Do this because the list may be modified during the thinking and also to
		// prevent bad situations where an entity can think more than once in a frame.
		ThinkEntry[] ppThinkEntryList = ArrayPool<ThinkEntry>.Shared.Rent(nMaxList);
		int nThinkCount = 0;
		foreach(var entry in ThinkEntries) { 
			AddEntityToFrameThinkList(entry, false, ref nThinkCount, ppThinkEntryList);
			Assert(nThinkCount <= nMaxList);
		}

		// While we're in the loop, no changes to the think list are allowed
		InThinkLoop = true;

		// Perform thinks on all entities that need it
		int i;
		for (i = 0; i < nThinkCount; ++i) {
			PerformThinkFunction(ppThinkEntryList[i], gpGlobals.CurTime);
		}

		InThinkLoop = false;

		// Apply changes to the think list
		int nCount = ChangeList.Count();
		for (i = 0; i < nCount; ++i) {
			ClientThinkHandle_t hThink = ChangeList[i].Think;
			if (hThink != INVALID_THINK_HANDLE) {
				// This can happen if the same think handle was removed twice
				if (!ThinkEntries.IsInList(hThink))
					continue;

				// NOTE: This is necessary for the case where the client entity handle
				// is slammed to NULL during a think interval; the hThink will get stuck
				// in the list and can never leave.
				SetNextClientThink(hThink, ChangeList[i].NextTime);
			}
			else {
				SetNextClientThink(ChangeList[i].Ent, ChangeList[i].NextTime);
			}
		}
		ChangeList.RemoveAll();

		// Clear out the client-side entity deletion list.
		CleanUpDeleteList();
		ArrayPool<ThinkEntry>.Shared.Return(ppThinkEntryList, true);
	}
	public void AddToDeleteList(ClientEntityHandle hEnt) {
		Assert(hEnt != cl_entitylist.InvalidHandle());
		if (hEnt == cl_entitylist.InvalidHandle())
			return;

		// Check to see if entity is networkable -- don't let it release!
		C_BaseEntity? pEntity = cl_entitylist.GetBaseEntityFromHandle(hEnt);
		if (pEntity != null) {
			// Check to see if the entity is already being removed!
			if (pEntity.IsMarkedForDeletion())
				return;

			// Don't add networkable entities to delete list -- the server should
			// take care of this.  The delete list is for client-side only entities.
			if (pEntity.GetClientNetworkable() == null) {
				DeleteList.Add(hEnt);
				pEntity.SetRemovalFlag(true);
			}
		}
	}
	public void RemoveFromDeleteList(ClientEntityHandle hEnt) {
		Assert(hEnt != cl_entitylist.InvalidHandle());
		if (hEnt == cl_entitylist.InvalidHandle())
			return;

		int nSize = DeleteList.Count;
		for (int iHandle = 0; iHandle < nSize; ++iHandle) {
			if (DeleteList[iHandle] == hEnt) {
				DeleteList[iHandle] = cl_entitylist.InvalidHandle();

				C_BaseEntity? pEntity = cl_entitylist.GetBaseEntityFromHandle(hEnt);
				if (pEntity != null)
					pEntity.SetRemovalFlag(false);
			}
		}
	}

	// Internal stuff
	private void SetNextClientThink(ClientThinkHandle_t hThink, TimeUnit_t nextTime) {
		if (hThink == INVALID_THINK_HANDLE)
			return;

		if (InThinkLoop) {
			int i = ChangeList.AddToTail();
			ChangeList[i].Ent.Init(Constants.INVALID_EHANDLE_INDEX);
			ChangeList[i].Think = hThink;
			ChangeList[i].NextTime = nextTime;
			return;
		}

		if(nextTime == CLIENT_THINK_NEVER) {
			RemoveThinkable(hThink);
		}
		else {
			GetThinkEntry(hThink).NextClientThink = nextTime;
		}
	}

	private void RemoveThinkable(ClientThinkHandle_t hThink) {
		if (hThink == INVALID_THINK_HANDLE)
			return;

		if (InThinkLoop) {
			int i = ChangeList.AddToTail();
			ChangeList[i].Ent.Init(Constants.INVALID_EHANDLE_INDEX);
			ChangeList[i].Think = hThink;
			ChangeList[i].NextTime = CLIENT_THINK_NEVER;
			return;
		}

		ThinkEntry entry = GetThinkEntry(hThink);
		IClientThinkable? pThink = cl_entitylist.GetClientThinkableFromHandle(entry.Ent);
		if (pThink != null)
			pThink.SetThinkHandle(INVALID_THINK_HANDLE);
		ThinkEntries.Remove(hThink);
	}

	private void PerformThinkFunction(ThinkEntry pEntry, TimeUnit_t curtime) {
		IClientThinkable? pThink = cl_entitylist.GetClientThinkableFromHandle(pEntry.Ent);
		if (pThink == null) {
			RemoveThinkable(pEntry.Ent);
			return;
		}

		if (pEntry.NextClientThink == CLIENT_THINK_ALWAYS) {
			pThink.ClientThink();
		}
		else if (pEntry.NextClientThink == float.MaxValue) {
			RemoveThinkable(pEntry.Ent);
		}
		else {
			Assert(pEntry.NextClientThink <= curtime);

			// Indicate we're not going to think again
			pEntry.NextClientThink = float.MaxValue;

			// NOTE: The Think function here could call SetNextClientThink
			// which would cause it to be readded into the list
			pThink.ClientThink();
		}

		// Set this after the Think calls in case they look at LastClientThink
		pEntry.LastClientThink = curtime;
	}

	private ThinkEntry GetThinkEntry(ClientThinkHandle_t hThink) {
		return ThinkEntries[hThink];
	}

	private void CleanUpDeleteList() {
		int nThinkCount = DeleteList.Count;
		for (int iThink = 0; iThink < nThinkCount; ++iThink) {
			ClientEntityHandle handle = DeleteList[iThink];
			if (handle != cl_entitylist.InvalidHandle()) {
				C_BaseEntity? pEntity = cl_entitylist.GetBaseEntityFromHandle(handle);
				if (pEntity != null)
					pEntity.SetRemovalFlag(false);

				IClientThinkable? pThink = cl_entitylist.GetClientThinkableFromHandle(handle);
				if (pThink != null)
					pThink.Release();
			}
		}

		DeleteList.Clear();
	}

	private void AddEntityToFrameThinkList(ThinkEntry pEntry, bool bAlwaysChain, ref int count, Span<ThinkEntry> ppFrameThinkList) {
		if (pEntry.IterEnum == IterEnum)
			return;

		bool bThinkThisInterval = (pEntry.NextClientThink == CLIENT_THINK_ALWAYS) ||
									(pEntry.NextClientThink <= gpGlobals.CurTime);

		if (!bThinkThisInterval && !bAlwaysChain)
			return;

		C_BaseEntity? pEntity = cl_entitylist.GetBaseEntityFromHandle(pEntry.Ent);
		if (pEntity != null) {
			C_BaseEntity? pParent = pEntity.GetMoveParent();
			if (pParent != null && (pParent.GetThinkHandle() != INVALID_THINK_HANDLE)) {
				ThinkEntry pParentEntry = GetThinkEntry(pParent.GetThinkHandle());
				AddEntityToFrameThinkList(pParentEntry, true, ref count, ppFrameThinkList);
			}
		}

		if (!bThinkThisInterval)
			return;

		// Add the entry into the list
		pEntry.IterEnum = IterEnum;
		ppFrameThinkList[count++] = pEntry;
	}

	private readonly PooledValueDictionary<ThinkEntry> ThinkEntries = new();
	private readonly List<ClientEntityHandle> DeleteList = new();
	private readonly PooledValueList<ThinkListChanges> ChangeList = new();

	private int IterEnum;
	private bool InThinkLoop;

	// Static members
	public static readonly CClientThinkList g_ClientThinkList = new();
	public static CClientThinkList ClientThinkList() => g_ClientThinkList;
}
