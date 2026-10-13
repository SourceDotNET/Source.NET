using CommunityToolkit.HighPerformance;


namespace Game.Shared;

[Flags]
public enum AnimEventType
{
	Server = 1 << 0,
	Scripted = 1 << 1,
	Shared = 1 << 2,
	Weapon = 1 << 3,
	Client = 1 << 4,
	FacePoser = 1 << 5,
	NewEventSystem = 1 << 10
}

public enum AnimEventId
{
	AE_INVALID = -1,
	AE_EMPTY,
	AE_NPC_LEFTFOOT,
	AE_NPC_RIGHTFOOT,
	AE_NPC_BODYDROP_LIGHT,
	AE_NPC_BODYDROP_HEAVY,
	AE_NPC_SWISHSOUND,
	AE_NPC_180TURN,
	AE_NPC_ITEM_PICKUP,
	AE_NPC_WEAPON_DROP,
	AE_NPC_WEAPON_SET_SEQUENCE_NAME,
	AE_NPC_WEAPON_SET_SEQUENCE_NUMBER,
	AE_NPC_WEAPON_SET_ACTIVITY,
	AE_NPC_HOLSTER,
	AE_NPC_DRAW,
	AE_NPC_WEAPON_FIRE,

	AE_CL_PLAYSOUND,
	AE_SV_PLAYSOUND,
	AE_CL_STOPSOUND,

	AE_START_SCRIPTED_EFFECT,
	AE_STOP_SCRIPTED_EFFECT,

	AE_CLIENT_EFFECT_ATTACH,

	AE_MUZZLEFLASH,
	AE_NPC_MUZZLEFLASH,

	AE_THUMPER_THUMP,
	AE_AMMOCRATE_PICKUP_AMMO,

	AE_NPC_RAGDOLL,

	AE_NPC_ADDGESTURE,
	AE_NPC_RESTARTGESTURE,

	AE_NPC_ATTACK_BROADCAST,

	AE_NPC_HURT_INTERACTION_PARTNER,
	AE_NPC_SET_INTERACTION_CANTDIE,

	AE_SV_DUSTTRAIL,

	AE_CL_CREATE_PARTICLE_EFFECT,

	AE_RAGDOLL,

	AE_CL_ENABLE_BODYGROUP,
	AE_CL_DISABLE_BODYGROUP,
	AE_CL_BODYGROUP_SET_VALUE,
	AE_CL_BODYGROUP_SET_VALUE_CMODEL_WPN,

	AE_WPN_PRIMARYATTACK,
	AE_WPN_INCREMENTAMMO,

	AE_WPN_HIDE,
	AE_WPN_UNHIDE,

	AE_WPN_PLAYWPNSOUND,

	LAST_SHARED_ANIMEVENT,
}

public struct EventListEntry
{
	public int EventIndex;
	public AnimEventType Type;
	public ushort StringKey;
	public bool IsPrivate;
}

public static class EventList
{
	public const int AE_NOT_AVAILABLE = -1;

	public static readonly List<EventListEntry> g_EventList = [];
	static readonly List<string> g_EventStrings = [];
	static readonly Dictionary<string, int> g_EventStringIDs = new(StringComparer.OrdinalIgnoreCase);
	static readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> g_EventStringLookup = g_EventStringIDs.GetAlternateLookup<ReadOnlySpan<char>>();
	public static int g_HighestEvent = 0;
	public static int g_EventListVersion = 1;

	public static void Init() {
		g_HighestEvent = 0;
	}

	public static void Free() {
		g_EventStrings.Clear();
		g_EventStringIDs.Clear();
		g_EventList.Clear();

		++g_EventListVersion;
	}

	public static EventListEntry AddEventEntry(ReadOnlySpan<char> name, int eventIndex, bool isPrivate, AnimEventType type) {
		int index = g_EventList.Count; g_EventList.Add(default);
		ref EventListEntry pList = ref g_EventList.AsSpan()[index];
		pList.EventIndex = eventIndex;
		string nameString = new(name);
		pList.StringKey = (ushort)g_EventStrings.Count;
		g_EventStrings.Add(nameString);
		g_EventStringIDs.TryAdd(nameString, index);
		pList.IsPrivate = isPrivate;
		pList.Type = type;

		// UNDONE: This implies that ALL shared activities are added before ANY custom activities
		// UNDONE: Segment these instead?  It's a 32-bit int, how many activities do we need?
		if (eventIndex > g_HighestEvent) {
			g_HighestEvent = eventIndex;
		}

		return pList;
	}

	static int ListFromString(ReadOnlySpan<char> str) {
		if (!g_EventStringLookup.TryGetValue(str, out int stringID))
			return -1;

		return stringID;
	}

	static int ListFromEvent(int eventIndex) {
		for (int i = 0; i < g_EventList.Count; i++) {
			if (g_EventList[i].EventIndex == eventIndex)
				return i;
		}

		return -1;
	}

	public static AnimEventType GetEventType(int eventIndex) {
		int list = ListFromEvent(eventIndex);

		if (list != -1)
			return g_EventList[list].Type;

		return (AnimEventType)(-1);
	}

	public static bool RegisterSharedEvent(ReadOnlySpan<char> eventName, int eventIndex, AnimEventType type = 0) {
		int list = ListFromString(eventName);
		if (list == -1)
			list = ListFromEvent(eventIndex);

		if (list != -1)
			return false;

		AddEventEntry(eventName, eventIndex, false, type);
		return true;
	}

	public static AnimEventId RegisterPrivateEvent(ReadOnlySpan<char> eventName) {
		int list = ListFromString(eventName);
		if (list != -1) {
			if (g_EventList[list].IsPrivate)
				return (AnimEventId)g_EventList[list].EventIndex;
			else {
				Warning("***\nShared<->Private Event collision!\n***\n");
				Assert(false);
				return AnimEventId.AE_INVALID;
			}
		}

		EventListEntry entry = AddEventEntry(eventName, g_HighestEvent + 1, true, AnimEventType.Server);
		return (AnimEventId)entry.EventIndex;
	}

	public static int IndexForName(ReadOnlySpan<char> eventName) {
		int list = ListFromString(eventName);

		if (list != -1)
			return g_EventList[list].EventIndex;

		return -1;
	}

	public static string? NameForIndex(int eventIndex) {
		int list = ListFromEvent(eventIndex);
		if (list != -1)
			return g_EventStrings[g_EventList[list].StringKey];

		return null;
	}

	static void REGISTER_SHARED_ANIMEVENT(AnimEventId n, AnimEventType b) => RegisterSharedEvent(Enum.GetName(n), (int)n, b);

	public static void RegisterSharedEvents() {
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_EMPTY, AnimEventType.Server);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_LEFTFOOT, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_RIGHTFOOT, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_BODYDROP_LIGHT, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_BODYDROP_HEAVY, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_SWISHSOUND, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_180TURN, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_ITEM_PICKUP, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_WEAPON_DROP, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_WEAPON_SET_SEQUENCE_NAME, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_WEAPON_SET_SEQUENCE_NUMBER, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_WEAPON_SET_ACTIVITY, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_HOLSTER, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_DRAW, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_WEAPON_FIRE, AnimEventType.Server | AnimEventType.Weapon);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_CL_PLAYSOUND, AnimEventType.Client);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_SV_PLAYSOUND, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_CL_STOPSOUND, AnimEventType.Client);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_START_SCRIPTED_EFFECT, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_STOP_SCRIPTED_EFFECT, AnimEventType.Server);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_CLIENT_EFFECT_ATTACH, AnimEventType.Client);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_MUZZLEFLASH, AnimEventType.Client);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_MUZZLEFLASH, AnimEventType.Client);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_THUMPER_THUMP, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_AMMOCRATE_PICKUP_AMMO, AnimEventType.Server);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_RAGDOLL, AnimEventType.Server);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_ADDGESTURE, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_RESTARTGESTURE, AnimEventType.Server);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_ATTACK_BROADCAST, AnimEventType.Server);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_HURT_INTERACTION_PARTNER, AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_NPC_SET_INTERACTION_CANTDIE, AnimEventType.Server);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_SV_DUSTTRAIL, AnimEventType.Server);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_CL_CREATE_PARTICLE_EFFECT, AnimEventType.Client);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_RAGDOLL, AnimEventType.Server);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_CL_ENABLE_BODYGROUP, AnimEventType.Client);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_CL_DISABLE_BODYGROUP, AnimEventType.Client);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_CL_BODYGROUP_SET_VALUE, AnimEventType.Client);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_CL_BODYGROUP_SET_VALUE_CMODEL_WPN, AnimEventType.Client);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_WPN_PRIMARYATTACK, AnimEventType.Client | AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_WPN_INCREMENTAMMO, AnimEventType.Client | AnimEventType.Server);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_WPN_HIDE, AnimEventType.Client | AnimEventType.Server);
		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_WPN_UNHIDE, AnimEventType.Client | AnimEventType.Server);

		REGISTER_SHARED_ANIMEVENT(AnimEventId.AE_WPN_PLAYWPNSOUND, AnimEventType.Client | AnimEventType.Server);
	}
}
