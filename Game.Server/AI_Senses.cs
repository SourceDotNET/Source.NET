global using static Game.Server.AI_SensesGlobals;

using Game.Shared;

using Source;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

public static class AI_SensesGlobals
{
	public static readonly AI_SensedObjectsManager g_AI_SensedObjectsManager = new();
}

public enum SeenType
{
	All = -1,
	HighPriority = 0,
	NPCs,
	Misc
}

[Flags]
public enum SensingFlags
{
	None = 0x00000000,
	DontLook = 0x00000001,
	DontListen = 0x00000002,
}

public struct AISightIter
{
	public int Array;
	public int Next;
	public SeenType SeenArray;
	public bool Done;
}

public class AI_Senses : AI_Component
{
	const float AI_STANDARD_NPC_SEARCH_TIME = .25f;
	const float AI_EFFICIENT_NPC_SEARCH_TIME = .35f;
	const float AI_HIGH_PRIORITY_SEARCH_TIME = 0.15f;
	const float AI_MISC_SEARCH_TIME = 0.45f;

	public AI_Senses() {
		LookDist = 2048;
		LastLookDist = -1;
		TimeLastLook = -1;
		AudibleList = 0;
		TimeLastLookHighPriority = -1;
		TimeLastLookNPCs = -1;
		TimeLastLookMisc = -1;

		SeenArrays = [SeenHighPriority, SeenNPCs, SeenMisc];
		SensingFlagsValue = SensingFlags.None;
	}

	public float GetDistLook() => LookDist;
	public void SetDistLook(float distLook) => LookDist = distLook;

	public bool CanHearSound(ref WorldSoundInstance sound) {
		if (sound.Owner.Get() == GetOuter())
			return false;

		if (sound.Owner.Get() != null && sound.Owner.Get()!.IsPlayer() && g_IgnorePlayers.GetBool())
			return false;

		if (GetOuter()!.GetState() == NPCState.Script && sound.IsSoundType(SoundInstanceType.Danger))
			return false;

		if (GetOuter()!.IsInAScript())
			return false;

		float hearDistanceSq = sound.Volume() * GetOuter()!.HearingSensitivity();
		hearDistanceSq *= hearDistanceSq;
		if (Vector3.DistanceSquared(sound.GetSoundOrigin(), GetOuter()!.EarPosition()) <= hearDistanceSq)
			return GetOuter()!.QueryHearSound(ref sound);

		return false;
	}

	public void Listen() {
		AudibleList = SOUNDLIST_EMPTY;

		int soundMask = GetOuter()!.GetSoundInterests();

		if (soundMask != (int)SoundInstanceType.None && !GetOuter()!.HasSpawnFlags(SF_NPC_WAIT_TILL_SEEN)) {
			int iSound = SoundEnt.ActiveList();

			while (iSound != SOUNDLIST_EMPTY) {
				ref WorldSoundInstance currentSound = ref SoundEnt.SoundPointerForIndex(iSound);

				if (!Unsafe.IsNullRef(ref currentSound) && (soundMask & (int)currentSound.SoundType()) != 0 && CanHearSound(ref currentSound)) {
					currentSound.NextAudible = AudibleList;
					AudibleList = iSound;
				}

				iSound = currentSound.NextSound();
			}
		}

		GetOuter()!.OnListened();
	}

	public bool ShouldSeeEntity(BaseEntity sightEnt) {
		if (sightEnt == GetOuter() || !sightEnt.IsAlive())
			return false;

		if (sightEnt.IsPlayer() && (sightEnt.GetFlags() & EntityFlags.NoTarget) != 0)
			return false;

		if (sightEnt.IsPlayer() && g_IgnorePlayers.GetBool())
			return false;

		if ((sightEnt.SpawnFlags & SF_NPC_WAIT_TILL_SEEN) != 0)
			return false;

		if (!sightEnt.CanBeSeenBy(GetOuter()!))
			return false;

		if (!GetOuter()!.QuerySeeEntity(sightEnt, true))
			return false;

		return true;
	}

	public bool CanSeeEntity(BaseEntity? sightEnt) {
		if (sightEnt != null && sightEnt.IsPlayer() && g_IgnorePlayers.GetBool())
			return false;

		return GetOuter()!.FInViewCone(sightEnt!) && GetOuter()!.FVisible(sightEnt!);
	}

	public bool DidSeeEntity(BaseEntity? sightEnt) {
		BaseEntity? testEnt = GetFirstSeenEntity(out AISightIter iter);

		while (testEnt != null) {
			if (sightEnt == testEnt)
				return true;
			testEnt = GetNextSeenEntity(ref iter);
		}
		return false;
	}

	void NoteSeenEntity(BaseEntity sightEnt) {
		sightEnt.Link = GetOuter()!.Link;
		GetOuter()!.Link = sightEnt;
	}

	bool WaitingUntilSeen(BaseEntity sightEnt) {
		if ((GetOuter()!.SpawnFlags & SF_NPC_WAIT_TILL_SEEN) != 0) {
			if (sightEnt.IsPlayer()) {
				BasePlayer? player = ToBasePlayer(sightEnt);
				if (player != null
					&& player.FInViewCone(GetOuter()!)
					&& FBoxVisible(sightEnt, GetOuter()!, out _)) {
					GetOuter()!.SpawnFlags &= ~SF_NPC_WAIT_TILL_SEEN;
					return false;
				}
			}
			return true;
		}

		return false;
	}

	bool SeeEntity(BaseEntity sightEnt) {
		GetOuter()!.OnSeeEntity(sightEnt);

		NoteSeenEntity(sightEnt);

		return true;
	}

	public BaseEntity? GetFirstSeenEntity(out AISightIter iter, SeenType seenType = SeenType.All) {
		iter = default;
		iter.SeenArray = seenType;
		int firstArray = (seenType == SeenType.All) ? 0 : (int)seenType;

		for (int i = firstArray; i < SeenArrays.Length; i++) {
			if (SeenArrays[i].Count != 0) {
				iter.Array = i;
				iter.Next = 1;
				return SeenArrays[i][0].Get();
			}
		}

		iter.Done = true;
		return null;
	}

	public BaseEntity? GetNextSeenEntity(ref AISightIter iter) {
		if (!iter.Done) {
			for (int i = iter.Array; i < SeenArrays.Length; i++) {
				for (int j = iter.Next; j < SeenArrays[i].Count; j++) {
					if (SeenArrays[i][j].Get() != null) {
						iter.Array = i;
						iter.Next = j + 1;
						return SeenArrays[i][j].Get();
					}
				}
				iter.Next = 0;

				if (iter.SeenArray != SeenType.All)
					break;
			}
			iter.Done = true;
		}
		return null;
	}

	void BeginGather() => GetOuter()!.Link = null;

	void EndGather(int nSeen, List<EHANDLE> result) {
		result.Clear();
		if (nSeen != 0) {
			BaseEntity? current = GetOuter()!.Link;
			for (int i = 0; i < nSeen; i++) {
				Assert(current != null);
				result.Add(current!.GetRefEHandle());
				current = current!.Link;
			}
			GetOuter()!.Link = null;
		}
	}

	public void Look(int distance) {
		if (TimeLastLook != gpGlobals.CurTime || LastLookDist != distance) {
			LookForHighPriorityEntities(distance);
			LookForNPCs(distance);
			LookForObjects(distance);

			LastLookDist = distance;
			TimeLastLook = gpGlobals.CurTime;
		}

		GetOuter()!.OnLooked(distance);
	}

	bool Look(BaseEntity sightEnt) {
		if (WaitingUntilSeen(sightEnt))
			return false;

		if (ShouldSeeEntity(sightEnt) && CanSeeEntity(sightEnt))
			return SeeEntity(sightEnt);
		return false;
	}

	int LookForHighPriorityEntities(int distance) {
		int nSeen = 0;
		if (gpGlobals.CurTime - TimeLastLookHighPriority > AI_HIGH_PRIORITY_SEARCH_TIME) {
			TimeLastLookHighPriority = gpGlobals.CurTime;

			BeginGather();

			float distSq = distance * distance;
			Vector3 origin = GetOuter()!.GetAbsOrigin();

			for (int i = 1; i <= gpGlobals.MaxClients; i++) {
				BaseEntity? player = Util.PlayerByIndex(i);

				if (player != null) {
					if (Vector3.DistanceSquared(origin, player.GetAbsOrigin()) < distSq && Look(player))
						nSeen++;
				}
			}

			EndGather(nSeen, SeenHighPriority);
		}
		else {
			for (int i = SeenHighPriority.Count - 1; i >= 0; --i) {
				if (SeenHighPriority[i].Get() == null)
					FastRemove(SeenHighPriority, i);
			}
			nSeen = SeenHighPriority.Count;
		}

		return nSeen;
	}

	int LookForNPCs(int distance) {
		bool removeStaleFromCache = false;
		float distSq = distance * distance;
		Vector3 origin = GetOuter()!.GetAbsOrigin();
		AI_Efficiency efficiency = GetOuter()!.GetEfficiency();
		float timeNPCs = (efficiency < AI_Efficiency.VeryEfficient) ? AI_STANDARD_NPC_SEARCH_TIME : AI_EFFICIENT_NPC_SEARCH_TIME;
		if (gpGlobals.CurTime - TimeLastLookNPCs > timeNPCs) {
			TimeLastLookNPCs = gpGlobals.CurTime;

			if (efficiency < AI_Efficiency.SuperEfficient) {
				int i, nSeen = 0;

				BeginGather();

				List<AI_BaseNPC> ais = g_AI_Manager.AccessAIs();

				for (i = 0; i < g_AI_Manager.NumAIs(); i++) {
					if (ais[i] != GetOuter() && (ais[i].ShouldNotDistanceCull() || Vector3.DistanceSquared(origin, ais[i].GetAbsOrigin()) < distSq)) {
						if (Look(ais[i]))
							nSeen++;
					}
				}

				EndGather(nSeen, SeenNPCs);

				return nSeen;
			}

			removeStaleFromCache = true;
		}

		for (int i = SeenNPCs.Count - 1; i >= 0; --i) {
			BaseEntity? seen = SeenNPCs[i].Get();
			if (seen == null)
				FastRemove(SeenNPCs, i);
			else if (removeStaleFromCache) {
				if ((!((AI_BaseNPC)seen).ShouldNotDistanceCull() &&
					   Vector3.DistanceSquared(origin, seen.GetAbsOrigin()) > distSq) ||
					 !Look(seen))
					FastRemove(SeenNPCs, i);
			}
		}

		return SeenNPCs.Count;
	}

	int LookForObjects(int distance) {
		const EntityFlags BOX_QUERY_MASK = EntityFlags.Object;
		int nSeen = 0;

		if (gpGlobals.CurTime - TimeLastLookMisc > AI_MISC_SEARCH_TIME) {
			TimeLastLookMisc = gpGlobals.CurTime;

			BeginGather();

			float distSq = distance * distance;
			Vector3 origin = GetOuter()!.GetAbsOrigin();
			BaseEntity? ent = g_AI_SensedObjectsManager.GetFirst(out int iter);
			while (ent != null) {
				if ((ent.GetFlags() & BOX_QUERY_MASK) != 0) {
					if (Vector3.DistanceSquared(origin, ent.GetAbsOrigin()) < distSq && Look(ent))
						nSeen++;
				}
				ent = g_AI_SensedObjectsManager.GetNext(ref iter);
			}

			EndGather(nSeen, SeenMisc);
		}
		else {
			for (int i = SeenMisc.Count - 1; i >= 0; --i) {
				if (SeenMisc[i].Get() == null)
					FastRemove(SeenMisc, i);
			}
			nSeen = SeenMisc.Count;
		}

		return nSeen;
	}

	static void FastRemove(List<EHANDLE> list, int i) {
		list[i] = list[^1];
		list.RemoveAt(list.Count - 1);
	}

	public TimeUnit_t GetTimeLastUpdate(BaseEntity? entity) {
		if (entity == null)
			return 0;
		if (entity.IsPlayer())
			return TimeLastLookHighPriority;
		if (entity.IsNPC())
			return TimeLastLookNPCs;
		return TimeLastLookMisc;
	}

	public ref WorldSoundInstance GetFirstHeardSound(out int iter) {
		int first = AudibleList;

		if (first == SOUNDLIST_EMPTY) {
			iter = SOUNDLIST_EMPTY;
			return ref Unsafe.NullRef<WorldSoundInstance>();
		}

		iter = first;
		return ref SoundEnt.SoundPointerForIndex(first);
	}

	public ref WorldSoundInstance GetNextHeardSound(scoped ref int iter) {
		if (iter == SOUNDLIST_EMPTY)
			return ref Unsafe.NullRef<WorldSoundInstance>();

		int current = SoundEnt.SoundPointerForIndex(iter).NextAudible;
		if (current == SOUNDLIST_EMPTY) {
			iter = SOUNDLIST_EMPTY;
			return ref Unsafe.NullRef<WorldSoundInstance>();
		}

		iter = current;
		return ref SoundEnt.SoundPointerForIndex(current);
	}

	public ref WorldSoundInstance GetClosestSound(bool scent = false, SoundInstanceType validTypes = SoundInstanceType.AllSounds | SoundInstanceType.AllScents, bool usePriority = true) {
		float bestDist = MAX_COORD_RANGE * MAX_COORD_RANGE;
		float dist;
		SoundPriority bestPriority = SoundPriority.VeryLow;

		ref WorldSoundInstance result = ref Unsafe.NullRef<WorldSoundInstance>();
		ref WorldSoundInstance current = ref GetFirstHeardSound(out int iter);

		Vector3 earPosition = GetOuter()!.EarPosition();

		while (!Unsafe.IsNullRef(ref current)) {
			if ((!scent && current.FIsSound()) ||
				 (scent && current.FIsScent())) {
				if (current.IsSoundType(validTypes) && !GetOuter()!.ShouldIgnoreSound(ref current)) {
					if (!usePriority || GetOuter()!.GetSoundPriority(ref current) >= bestPriority) {
						dist = (current.GetSoundOrigin() - earPosition).LengthSquared();

						if (dist < bestDist) {
							result = ref current;
							bestDist = dist;

							bestPriority = GetOuter()!.GetSoundPriority(ref current);
						}
					}
				}
			}

			current = ref GetNextHeardSound(ref iter);
		}

		return ref result;
	}

	public void PerformSensing() {
		if (!HasSensingFlags(SensingFlags.DontLook))
			Look((int)LookDist);

		if (!HasSensingFlags(SensingFlags.DontListen))
			Listen();
	}

	public void AddSensingFlags(SensingFlags flags) => SensingFlagsValue |= flags;
	public void RemoveSensingFlags(SensingFlags flags) => SensingFlagsValue &= ~flags;
	public bool HasSensingFlags(SensingFlags flags) => (SensingFlagsValue & flags) == flags;

	public float LookDist;
	float LastLookDist;
	TimeUnit_t TimeLastLook;

	int AudibleList;

	readonly List<EHANDLE> SeenHighPriority = [];
	readonly List<EHANDLE> SeenNPCs = [];
	readonly List<EHANDLE> SeenMisc = [];

	readonly List<EHANDLE>[] SeenArrays;

	TimeUnit_t TimeLastLookHighPriority;
	TimeUnit_t TimeLastLookNPCs;
	TimeUnit_t TimeLastLookMisc;

	SensingFlags SensingFlagsValue;
}

public class AI_SensedObjectsManager : IEntityListener
{
	public void Init() {
		BaseEntity? ent = null;
		while ((ent = gEntList.NextEnt(ent)) != null)
			OnEntitySpawned(ent);

		gEntList.AddListenerEntity(this);
	}

	public void Term() {
		gEntList.RemoveListenerEntity(this);
		SensedObjects.Clear();
	}

	public BaseEntity? GetFirst(out int iter) {
		if (SensedObjects.Count != 0) {
			iter = 1;
			return SensedObjects[0].Get();
		}

		iter = 0;
		return null;
	}

	public BaseEntity? GetNext(ref int iter) {
		int i = iter;
		if (i != 0 && i < SensedObjects.Count) {
			iter++;
			return SensedObjects[i].Get();
		}

		iter = 0;
		return null;
	}

	public virtual void AddEntity(BaseEntity entity) {
		if (Find(entity) != -1)
			return;

		Assert(!entity.IsPlayer() && !entity.IsNPC());

		entity.AddFlag(EntityFlags.Object);
		SensedObjects.Add(entity.GetRefEHandle());
	}

	public void OnEntitySpawned(BaseEntity entity) {
		if ((entity.GetFlags() & EntityFlags.Object) != 0 && !entity.IsPlayer() && !entity.IsNPC())
			SensedObjects.Add(entity.GetRefEHandle());
	}

	public void OnEntityDeleted(BaseEntity entity) {
		if ((entity.GetFlags() & EntityFlags.Object) != 0 && !entity.IsPlayer() && !entity.IsNPC()) {
			int i = Find(entity);
			if (i != -1) {
				SensedObjects[i] = SensedObjects[^1];
				SensedObjects.RemoveAt(SensedObjects.Count - 1);
			}
		}
	}

	int Find(BaseEntity entity) {
		for (int i = 0; i < SensedObjects.Count; i++) {
			if (SensedObjects[i].Get() == entity)
				return i;
		}
		return -1;
	}

	readonly List<EHANDLE> SensedObjects = [];
}
