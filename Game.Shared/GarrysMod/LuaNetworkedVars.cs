#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;

using System.Numerics;
using System.Text;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public class LuaNetworkedVars
{
	public class LuaNetworkedVar
	{
		public readonly LuaObject Value = new();
		public readonly LuaObject Proxy = new();
#if CLIENT_DLL
		public float Time = -1.0f;
#else
		public float NextUpdate = -1.0f;
		public int NameID = -1;
#endif
	}

	class CaselessStringLessThan : IComparer<string>
	{
		public static readonly CaselessStringLessThan Instance = new();
		public int Compare(string? x, string? y) => stricmp(x, y);
	}

	public class LuaNetworkedEntity
	{
		public BaseHandle Handle = new(0xFFFFFFFF);
		public readonly SortedDictionary<string, LuaNetworkedVar> Vars = new(CaselessStringLessThan.Instance);
	}

	public static LuaNetworkedVars? g_LuaNetworkedVars;

	public const int MAX_ENTITIES = 0x4000;

	readonly LuaNetworkedEntity[] Entities = new LuaNetworkedEntity[MAX_ENTITIES];

#if GAME_DLL
	public static readonly ConVar lua_networkvar_bytespertick = new("lua_networkvar_bytespertick", "256", 0);

	int BytesSent;
	readonly uint[] PlayerCursors = new uint[0x80];
	static float fNextTime;
#endif

	public LuaNetworkedVars() {
		for (int i = 0; i < MAX_ENTITIES; i++)
			Entities[i] = new();
#if GAME_DLL
		Array.Fill(PlayerCursors, 0xFFFFFFFF);
#endif
	}

	static int stricmp(string? a, string? b) {
		a ??= "";
		b ??= "";
		int len = Math.Min(a.Length, b.Length);
		for (int i = 0; i < len; i++) {
			int ca = char.ToLowerInvariant(a[i]);
			int cb = char.ToLowerInvariant(b[i]);
			if (ca != cb)
				return ca - cb;
		}
		return a.Length - b.Length;
	}

	static BaseEntity? EntityFromHandle(uint handle) => LuaEntity.GetEntityFromHandle(handle);

	public SortedDictionary<string, LuaNetworkedVar> GetEntityVars(uint handle) {
		if (handle == 0xFFFFFFFF || (handle & 0x3FFF) == 0)
			return Entities[0].Vars;

		LuaNetworkedEntity slot = Entities[handle & 0x3FFF];
		if ((slot.Handle.Index >> 14) != (handle >> 14)) {
			slot.Vars.Clear();
			slot.Handle = new(handle);
		}
		return slot.Vars;
	}

	public void ClearEntity(uint handle) {
		LuaNetworkedEntity slot = Entities[handle & 0x3FFF];
		slot.Vars.Clear();
		slot.Handle = new(handle);
	}

	public LuaNetworkedVar? FindEntityVar(uint handle, string? name, bool create) {
		SortedDictionary<string, LuaNetworkedVar> vars = GetEntityVars(handle);
		if (vars.Count == 0 && !create)
			return null;

		name ??= "";
		if (vars.TryGetValue(name, out LuaNetworkedVar? var))
			return var;

		if (!create)
			return null;

		var = new();
		vars.Add(new string(name), var);
		return var;
	}

	public bool GetNetworkedVarBool(BaseEntity? ent, string name, bool def) {
		LuaNetworkedVar? var = FindEntityVar(ent != null ? ent.GetRefEHandle().Index : 0xFFFFFFFF, name, false);
		if (var == null || var.Value.isNil())
			return def;
		return var.Value.isBool() ? var.Value.GetBool() : def;
	}

	public Vector3 GetNetworkedVarVector(BaseEntity? ent, string name, in Vector3 def) {
		LuaNetworkedVar? var = FindEntityVar(ent != null ? ent.GetRefEHandle().Index : 0xFFFFFFFF, name, false);
		if (var == null || var.Value.isNil())
			return def;
		return var.Value.GetType() == LuaType.Vector ? var.Value.GetVector() : def;
	}

	public float GetNetworkedVarFloat(BaseEntity? ent, string name, float def) {
		LuaNetworkedVar? var = FindEntityVar(ent != null ? ent.GetRefEHandle().Index : 0xFFFFFFFF, name, false);
		if (var == null || var.Value.isNil())
			return def;
		return var.Value.isNumber() ? var.Value.GetFloat() : def;
	}

	public int PushNetworkedVar(BaseEntity? ent, string name) {
		LuaNetworkedVar? var = FindEntityVar(ent != null ? ent.GetRefEHandle().Index : 0xFFFFFFFF, name, false);
		if (var == null || var.Value.isNil())
			return 0;
		var.Value.Push();
		return 1;
	}

	static void TruncateString(ILuaObject value) {
		if (!value.isString())
			return;
		string? str = value.GetString();
		if (str == null || str.Length <= 0xC8)
			return;
		g_Lua!.PushString(str.AsSpan(0, 0xC7));
		value.SetFromStack(-1);
		g_Lua.Pop(1);
	}

	void CallProxy(LuaNetworkedVar var, uint handle, string name, ILuaObject oldValue) {
		if (g_Lua == null || !var.Proxy.isFunction())
			return;

		var.Proxy.Push();
		LuaEntity.Push_Entity(EntityFromHandle(handle));
		g_Lua.PushString(name);
		oldValue.Push();
		var.Value.Push();
		g_Lua.CallInternalNoReturns(4);
	}

	public void SetNetworkedVar(uint handle, string name, ILuaObject value) {
		LuaNetworkedVar? var = FindEntityVar(handle, name, true);
		if (var == null) {
			Warning($"Setting Networked Var {name} Failed!!\n");
			return;
		}

		LuaObject oldValue = new();
		oldValue.Set(var.Value);

		TruncateString(value);

#if GAME_DLL
		if (g_Lua!.IsEqual(var.Value, value)) {
			oldValue.UnReference();
			return;
		}

		var.Value.Set(value);
		var.NextUpdate = 0;
		if (var.NameID == -1)
			var.NameID = NetworkString.Add(name);
#else
		var.Value.Set(value);
		var.Time = (float)gpGlobals.CurTime;
#endif

		CallProxy(var, handle, name, oldValue);
		oldValue.UnReference();
	}

	public void SetNetworkedVarProxy(uint handle, string name, ILuaObject func) {
		LuaNetworkedVar? var = FindEntityVar(handle, name, true);
		if (var == null) {
			Warning("Setting Networked Var Proxy Failed!!\n");
			return;
		}
		var.Proxy.Set(func);
	}

	static void FillVarTable(LuaTable table, SortedDictionary<string, LuaNetworkedVar> vars) {
		foreach (KeyValuePair<string, LuaNetworkedVar> pair in vars)
			table.SetMember(pair.Key, pair.Value.Value);
	}

	static ILuaObject PopTable(LuaTable table) {
		table.Push();
		ILuaObject ret = g_Lua!.GetObject(-1);
		g_Lua.Pop(1);
		table.UnReference();
		return ret;
	}

	public ILuaObject BuildEntityNetworkVarTable(BaseEntity? ent) {
		LuaTable result = new();
		SortedDictionary<string, LuaNetworkedVar> vars = GetEntityVars(ent != null ? ent.GetRefEHandle().Index : 0xFFFFFFFF);
		if (vars.Count != 0)
			FillVarTable(result, vars);
		return PopTable(result);
	}

	public ILuaObject BuildNetworkVarTables() {
		LuaTable result = new();

		SortedDictionary<string, LuaNetworkedVar> globals = GetEntityVars(0xFFFFFFFF);
		if (globals.Count != 0) {
			LuaTable table = new();
			FillVarTable(table, globals);
			result.SetMember(0, table);
			table.UnReference();
		}

#if GAME_DLL
		for (BaseEntity? ent = gEntList.FirstEnt(); ent != null; ent = gEntList.NextEnt(ent)) {
			if (ent.Edict() == null || (uint)(ent.EntIndex() - 1) > 0x3FFE)
				continue;
#else
		for (int i = 1; i < MAX_ENTITIES; i++) {
			C_BaseEntity? ent = cl_entitylist.GetBaseEntity(i);
			if (ent == null)
				continue;
#endif
			SortedDictionary<string, LuaNetworkedVar> vars = GetEntityVars(ent.GetRefEHandle().Index);
			if (vars.Count == 0)
				continue;

			LuaTable table = new();
			FillVarTable(table, vars);
			LuaEntity.Push_Entity(ent);
			ILuaObject key = g_Lua!.GetObject(-1);
			g_Lua.Pop(1);
			result.SetMember(key, table);
			table.UnReference();
		}

		return PopTable(result);
	}

#if GAME_DLL
	public static void RegisterUserMessages(UserMessages messages) => messages.Register("NetworkedVar", -1);

	void UpdateEntityVar(LuaNetworkedEntity slot, LuaNetworkedVar var, float interval, RecipientFilter filter, bool force) {
		if (!force && var.NextUpdate != 0.0f && var.NextUpdate > gpGlobals.CurTime - interval)
			return;

		if (var.Value.isNil()) {
			var.NextUpdate = (float)gpGlobals.RealTime;
			return;
		}

		if ((BytesSent > lua_networkvar_bytespertick.GetInt() || BytesSent == -1) && !force) {
			BytesSent = -1;
			return;
		}

		UserMessageBegin(filter, "NetworkedVar");
		MessageWriteEHandle(EntityFromHandle(slot.Handle.Index));
		LuaType type = var.Value.GetType();
		MessageWriteChar((sbyte)type);
		MessageWriteString(NetworkString.Convert(var.NameID));
		switch (type) {
			case LuaType.Bool:
				MessageWriteBool(var.Value.GetBool());
				BytesSent += 1;
				break;
			case LuaType.Number:
				MessageWriteFloat(var.Value.GetFloat());
				BytesSent += 4;
				break;
			case LuaType.String:
				string str = var.Value.GetString() ?? "";
				MessageWriteString(str);
				BytesSent += Encoding.UTF8.GetByteCount(str);
				break;
			case LuaType.Entity:
				BytesSent += 4;
				MessageWriteEHandle(var.Value.GetUserData() != 0 ? (BaseEntity?)var.Value.GetEntity() : null);
				break;
			case LuaType.Vector:
				MessageWriteVec3Coord(var.Value.GetVector());
				BytesSent += 12;
				break;
			case LuaType.Angle:
				MessageWriteAngles(var.Value.GetAngle());
				BytesSent += 12;
				break;
			default:
				Msg($"Error: Trying to network unacceptable type ({g_Lua!.GetTypeName(type)})\n");
				break;
		}
		MessageEnd();

		if (!force)
			var.NextUpdate = (float)(gpGlobals.CurTime + RandomFloat(-0.05f, 0.05f));
	}

	void UpdateEntityVars(LuaNetworkedEntity slot, RecipientFilter filter, bool force) {
		if (slot.Vars.Count == 0)
			return;

		float interval = RandomFloat(0.5f, 1.0f) * 360.0f;
		uint handle = slot.Handle.Index;
		if (handle == 0xFFFFFFFF || (handle & 0x3FFF) == 0)
			interval = 30.0f;
		else {
			BaseEntity? ent = EntityFromHandle(handle);
			if (ent == null)
				return;

			if (ent.IsWeapon()) {
				BaseCombatWeapon weapon = (BaseCombatWeapon)ent;
				if (weapon.GetOwner() != null && weapon.GetOwner()!.GetActiveWeapon() == weapon)
					interval = 0.5f;
			}

			if (ent.IsPlayer())
				interval = 10.0f;
		}

		foreach (LuaNetworkedVar var in slot.Vars.Values)
			UpdateEntityVar(slot, var, interval, filter, force);
	}

	LuaNetworkedEntity SlotOf(BaseEntity ent) => Entities[ent.Edict() != null ? ent.EntIndex() : 0];

	public void Cycle() {
		if (g_Lua == null || fNextTime > (float)gpGlobals.RealTime)
			return;

		BytesSent = 0;
		fNextTime = (float)(gpGlobals.RealTime + 0.1);

		BroadcastRecipientFilter broadcast = new();
		if (Entities[0].Vars.Count != 0)
			UpdateEntityVars(Entities[0], broadcast, false);

		BytesSent = 0;
		for (BaseEntity? ent = gEntList.FirstEnt(); ent != null; ent = gEntList.NextEnt(ent)) {
			if (ent.Edict() == null || (uint)(ent.EntIndex() - 1) > 0x3FFE)
				continue;
			LuaNetworkedEntity slot = Entities[ent.EntIndex()];
			if (slot.Vars.Count != 0)
				UpdateEntityVars(slot, broadcast, false);
		}

		BytesSent = 0;
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			if (EntityFromHandle(PlayerCursors[i - 1]) == null)
				continue;

			BasePlayer? player = Util.PlayerByIndex(i);
			if (player == null) {
				PlayerCursors[i - 1] = 0xFFFFFFFF;
				continue;
			}

			BytesSent = 0;
			SingleUserRecipientFilter filter = new(player);
			filter.MakeReliable();

			do {
				BaseEntity? ent = EntityFromHandle(PlayerCursors[i - 1]);
				if (ent == null)
					break;

				if (ent.Edict() != null && (uint)(ent.EntIndex() - 1) <= 0x3FFE) {
					LuaNetworkedEntity slot = Entities[ent.EntIndex()];
					if (slot.Vars.Count != 0)
						UpdateEntityVars(slot, filter, true);
				}

				BaseEntity? next = gEntList.NextEnt(EntityFromHandle(PlayerCursors[i - 1]));
				PlayerCursors[i - 1] = next != null ? next.GetRefEHandle().Index : 0xFFFFFFFF;
			}
			while (BytesSent <= 0x200);
		}
	}

	public void PlayerInsert(BasePlayer player) {
		BaseEntity? first = gEntList.FirstEnt();
		int slot = player.Edict() != null ? player.EntIndex() - 1 : -1;
		PlayerCursors[slot] = first != null ? first.GetRefEHandle().Index : 0xFFFFFFFF;

		SingleUserRecipientFilter filter = new(player);
		filter.MakeReliable();

		if (Entities[0].Vars.Count != 0)
			UpdateEntityVars(Entities[0], filter, true);

		LuaNetworkedEntity own = SlotOf(player);
		if (own.Vars.Count != 0)
			UpdateEntityVars(own, filter, true);
	}
#else
	public static void RegisterUserMessages(UserMessages messages) {
		messages.Register("NetworkedVar", -1);
		messages.HookMessage("NetworkedVar", MsgFunc_NetworkedVar);
	}

	static uint ReadEHandle(bf_read msg) {
		uint value = (uint)msg.ReadLong();
		if (value == Constants.INVALID_NETWORKED_EHANDLE_VALUE)
			return 0xFFFFFFFF;
		uint index = value & ((1 << Constants.MAX_EDICT_BITS) - 1);
		uint serial = (uint)((int)value >> Constants.MAX_EDICT_BITS);
		uint handle = (serial << 14) | index;
		if (index != (handle & 0x3FFF) || serial != (handle >> 14))
			Warning($"CBaseHandle::Init got a bad handle! {index} {serial} => {handle & 0x3FFF} {handle >> 14}\n");
		return handle;
	}

	static void MsgFunc_NetworkedVar(bf_read msg) {
		if (g_Lua == null || g_LuaNetworkedVars == null)
			return;

		uint handle = ReadEHandle(msg);
		LuaType type = (LuaType)msg.ReadChar();
		string name = msg.ReadString(0x80) ?? "";

		switch (type) {
			case LuaType.String:
				g_Lua.PushString(msg.ReadString(0x100) ?? "");
				break;
			case LuaType.Number:
				g_Lua.PushNumber(msg.ReadFloat());
				break;
			case LuaType.Vector:
				LuaVector.Push_Vector(msg.ReadBitVec3Coord());
				break;
			case LuaType.Angle:
				LuaAngle.Push_Angle(msg.ReadBitAngles());
				break;
			case LuaType.Bool:
				g_Lua.PushBool(msg.ReadOneBit() != 0);
				break;
			case LuaType.Entity:
				uint entHandle = ReadEHandle(msg);
				if (g_Lua == null)
					Error("LuaNetworkVars: !g_Lua");
				LuaEntity.Push_Entity(EntityFromHandle(entHandle));
				break;
			default:
				Msg($"Error: Trying to network (receive) unacceptable type ({g_Lua.GetTypeName(type)})\n");
				return;
		}

		LuaObject value = new();
		value.SetFromStack(-1);
		g_Lua.Pop(1);

		g_LuaNetworkedVars.SetNetworkedVar(handle, name, value);
		value.UnReference();
	}
#endif
}
#endif
