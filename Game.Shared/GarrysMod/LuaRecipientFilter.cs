#if GAME_DLL
using Source;
using Source.Common.GarrysMod.Lua;

namespace Game.Server.GarrysMod;

public static partial class LuaRecipientFilter
{
	[LuaClass(typeof(RecipientFilter), NullError = "Tried to use a NULL CRecipientFilter!")]
	public static readonly LuaClass LC_RecipientFilter = new("CRecipientFilter", LuaType.RecipientFilter, null, null);

	public static void Push_RecipientFilter(RecipientFilter? filter) => LC_RecipientFilter.Push(filter);

	public static RecipientFilter Get_RecipientFilter(int stackPos) {
		RecipientFilter? filter = LC_RecipientFilter.Get(stackPos) as RecipientFilter;
		if (filter == null)
			g_Lua!.Error("Tried to use a NULL CRecipientFilter!");
		return filter!;
	}

	static void RemoveInvalidPlayers(RecipientFilter filter) {
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			if (Util.PlayerByIndex(i) == null)
				filter.RemoveRecipientByPlayerIndex(i);
		}
	}

	[LuaGlobal]
	static int RecipientFilter(ILuaInterface lua) {
		RecipientFilter filter = new();
		if (lua.GetType(1) == LuaType.Nil || !lua.GetBool(1))
			filter.MakeReliable();
		Push_RecipientFilter(filter);
		return 1;
	}

	[LuaMethod]
	static int CRecipientFilter____tostring(ILuaInterface lua) {
		RecipientFilter filter = Get_RecipientFilter(1);
		RemoveInvalidPlayers(filter);
		Span<char> str = stackalloc char[128];
		sprintf(str, "CRecipientFilter [%i]").I(filter.GetRecipientCount());
		lua.PushString(((ReadOnlySpan<char>)str).SliceNullTerminatedString());
		return 1;
	}

	[LuaMethod]
	static int CRecipientFilter__AddAllPlayers(ILuaInterface lua) {
		Get_RecipientFilter(1).AddAllPlayers();
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__RemoveAllPlayers(ILuaInterface lua) {
		Get_RecipientFilter(1).RemoveAllRecipients();
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__AddPAS(ILuaInterface lua) {
		Get_RecipientFilter(1).AddRecipientsByPAS(LuaVector.Get_Vector(2));
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__RemovePAS(ILuaInterface lua) {
		Get_RecipientFilter(1).GMOD_RemoveRecipientsByPAS(LuaVector.Get_Vector(2));
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__AddPVS(ILuaInterface lua) {
		Get_RecipientFilter(1).AddRecipientsByPVS(LuaVector.Get_Vector(2));
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__RemovePVS(ILuaInterface lua) {
		Get_RecipientFilter(1).RemoveRecipientsByPVS(LuaVector.Get_Vector(2));
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__AddPlayer(ILuaInterface lua) {
		RecipientFilter filter = Get_RecipientFilter(1);
		BaseEntity? ent = LuaEntity.Get_Entity(2, false);
		filter.AddRecipient(ent != null && ent.IsPlayer() ? (BasePlayer)ent : null!);
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__RemovePlayer(ILuaInterface lua) {
		RecipientFilter filter = Get_RecipientFilter(1);
		BaseEntity? ent = LuaEntity.Get_Entity(2, false);
		filter.RemoveRecipient(ent != null && ent.IsPlayer() ? (BasePlayer)ent : null!);
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__AddPlayers(ILuaInterface lua) {
		RecipientFilter filter = Get_RecipientFilter(1);
		if (lua.GetType(2) == LuaType.RecipientFilter) {
			RecipientFilter? other = LC_RecipientFilter.Get(2) as RecipientFilter;
			if (other == null)
				return 0;
			for (int i = 0; i < other.GetRecipientCount(); i++)
				filter.AddRecipient(Util.PlayerByIndex(other.GetRecipientIndex(i))!);
			return 0;
		}
		if (lua.GetType(2) == LuaType.Table) {
			LuaObject table = new(2, LuaType.None);
			for (int i = 1; ; i++) {
				BaseEntity? ent = table.GetMemberEntity(i, null) as BaseEntity;
				if (ent == null || !ent.IsPlayer())
					break;
				filter.AddRecipient((BasePlayer)ent);
			}
			table.UnReference();
		}
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__RemovePlayers(ILuaInterface lua) {
		RecipientFilter filter = Get_RecipientFilter(1);
		if (lua.GetType(2) == LuaType.RecipientFilter) {
			RecipientFilter? other = LC_RecipientFilter.Get(2) as RecipientFilter;
			if (other == null)
				return 0;
			for (int i = 0; i < other.GetRecipientCount(); i++)
				filter.RemoveRecipient(Util.PlayerByIndex(other.GetRecipientIndex(i))!);
			return 0;
		}
		if (lua.GetType(2) == LuaType.Table) {
			LuaObject table = new(2, LuaType.None);
			for (int i = 1; ; i++) {
				BaseEntity? ent = table.GetMemberEntity(i, null) as BaseEntity;
				if (ent == null || !ent.IsPlayer())
					break;
				filter.RemoveRecipient((BasePlayer)ent);
			}
			table.UnReference();
		}
		return 0;
	}
	[LuaMethod]
	static int CRecipientFilter__RemoveMismatchedPlayers(ILuaInterface lua) {
		RecipientFilter filter = Get_RecipientFilter(1);
		if (lua.GetType(2) == LuaType.RecipientFilter) {
			RecipientFilter? other = LC_RecipientFilter.Get(2) as RecipientFilter;
			if (other == null)
				return 0;
			for (int i = filter.GetRecipientCount() - 1; i >= 0; i--) {
				int index = filter.GetRecipientIndex(i);
				if (!other.GMOD_HasRecipient(index))
					filter.RemoveRecipientByPlayerIndex(index);
			}
			return 0;
		}
		if (lua.GetType(2) == LuaType.Table) {
			List<int> keep = [];
			LuaObject table = new(2, LuaType.None);
			for (int i = 1; ; i++) {
				BaseEntity? ent = table.GetMemberEntity(i, null) as BaseEntity;
				if (ent == null || !ent.IsPlayer())
					break;
				keep.Add(ent.Edict() == null ? 0 : ent.EntIndex());
			}
			for (int i = filter.GetRecipientCount() - 1; i >= 0; i--) {
				int index = filter.GetRecipientIndex(i);
				if (!keep.Contains(index))
					filter.RemoveRecipientByPlayerIndex(index);
			}
			table.UnReference();
		}
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__AddRecipientsByTeam(ILuaInterface lua) {
		RecipientFilter filter = Get_RecipientFilter(1);
		int team = (int)lua.CheckNumber(2);
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player != null && player.GetTeamNumber() == team)
				filter.AddRecipient(player);
		}
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__RemoveRecipientsByTeam(ILuaInterface lua) {
		RecipientFilter filter = Get_RecipientFilter(1);
		int team = (int)lua.CheckNumber(2);
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player != null && player.GetTeamNumber() == team)
				filter.RemoveRecipient(player);
		}
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__RemoveRecipientsNotOnTeam(ILuaInterface lua) {
		RecipientFilter filter = Get_RecipientFilter(1);
		int team = (int)lua.CheckNumber(2);
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player != null && player.GetTeamNumber() != team)
				filter.RemoveRecipient(player);
		}
		return 0;
	}

	[LuaMethod]
	static int CRecipientFilter__GetCount(ILuaInterface lua) {
		RecipientFilter filter = Get_RecipientFilter(1);
		RemoveInvalidPlayers(filter);
		lua.PushNumber(filter.GetRecipientCount());
		return 1;
	}

	[LuaMethod]
	static int CRecipientFilter__GetPlayers(ILuaInterface lua) {
		RecipientFilter filter = Get_RecipientFilter(1);
		RemoveInvalidPlayers(filter);
		LuaTable table = new(null, (uint)filter.GetRecipientCount());
		for (int i = 0; i < filter.GetRecipientCount(); i++) {
			int index = filter.GetRecipientIndex(i);
			if (table.isTable()) {
				LuaEntity.Push_Entity(index == 0 ? GetWorldEntity() : Util.EntityByIndex(index));
				table.SetMember(i + 1);
			}
		}
		table.Push();
		table.UnReference();
		return 1;
	}
}
#endif
