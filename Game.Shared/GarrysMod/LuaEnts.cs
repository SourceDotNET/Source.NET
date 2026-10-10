#if CLIENT_DLL || GAME_DLL
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaEnts
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_ents = new("ents");

	public static BaseEntity? GMEntityByIndex(int index) {
#if CLIENT_DLL
		return cl_entitylist.GetBaseEntity(index);
#else
		if (index != 0)
			return Util.EntityByIndex(index);
		return GetWorldEntity();
#endif
	}

	static readonly string[] ProtectedClasses = [
		"worldspawn",
		"player",
		"gm_bot",
		"gmod_gamerules",
		"soundent",
		"ai_ally_speech_manager",
		"ai_network_build_helper",
		"scene_manager",
		"ai_network",
		"player_manager",
		"instanced_scripted_scene",
		"npc_barnacle_tongue_tip",
	];

	public static bool IsProtectedClass(ReadOnlySpan<char> className) {
		foreach (string name in ProtectedClasses) {
			if (stricmp(className, name) == 0)
				return true;
		}
		return false;
	}

	public static bool IsProtectedEntity(BaseEntity ent) {
#if CLIENT_DLL
		if (ent.IsPlayer() || ent == C_World.GetClientWorldEntity())
#else
		if (ent.IsPlayer() || ent == GetWorldEntity())
#endif
			return true;
		return IsProtectedClass(ent.GetClassname());
	}

#if CLIENT_DLL
	// todo: CreateClientProp
	// todo: CreateClientRope
	// todo: CreateClientside
#else
	// todo: render depth counter
	static bool IsRendering() => false;

	[LuaFunction]
	static int Create(ILuaInterface lua) {
		if (IsRendering()) {
			lua.ErrorNoHalt("ents.Create cannot be called while rendering\n");
			return 0;
		}

		string? className = lua.CheckString(1);
		if (className == null || IsProtectedClass(className))
			return 0;

		if (GetWorldEntity() == null)
			Warning($"Trying to create entities too early! ({className})\n");

		LuaEntity.Push_Entity(CreateEntityByName(className, -1));
		return 1;
	}

	[LuaFunction]
	static int GetEdictCount() => gEntList.NumberOfEdicts();
#endif

	[LuaFunction]
	static int GetCount(ILuaInterface lua) {
		if (lua.GetType(1) != LuaType.Nil && lua.GetBool(1)) {
#if CLIENT_DLL
			lua.PushNumber(cl_entitylist.NumberOfEntities(false));
#else
			lua.PushNumber(gEntList.NumberOfEntities());
#endif
			return 1;
		}

		int count = 0;
#if CLIENT_DLL
		for (BaseEntity? ent = cl_entitylist.FirstBaseEntity(); ent != null; ent = cl_entitylist.NextBaseEntity(ent)) {
#else
		for (BaseEntity? ent = gEntList.NextEnt(null); ent != null; ent = gEntList.NextEnt(ent)) {
#endif
			if (!ent.IsMarkedForDeletion())
				count++;
		}

		lua.PushNumber(count);
		return 1;
	}

	[LuaFunction]
	static int GetAll(ILuaInterface lua) {
		lua.CreateTable();
		int i = 1;
#if CLIENT_DLL
		for (BaseEntity? ent = cl_entitylist.FirstBaseEntity(); ent != null; ent = cl_entitylist.NextBaseEntity(ent)) {
#else
		for (BaseEntity? ent = gEntList.NextEnt(null); ent != null; ent = gEntList.NextEnt(ent)) {
#endif
			if (ent.IsMarkedForDeletion())
				continue;

			lua.PushNumber(i++);
			LuaEntity.Push_Entity(ent);
			lua.SetTable(-3);
		}
		return 1;
	}

	// todo: FindAlongRay

#if CLIENT_DLL
	static bool WildcardMatch(ReadOnlySpan<char> pattern, ReadOnlySpan<char> str) {
		static char Lower(char c) => (uint)(c - 'A') < 26 ? (char)(c + 32) : c;
		static char At(ReadOnlySpan<char> s, int i) => i < s.Length ? s[i] : '\0';

		int p = 0, s = 0, star = -1;
		while (true) {
			char c = At(str, s);
			if (c == '\0') {
				while (true) {
					char pc = At(pattern, p);
					if (pc == '\0')
						return true;
					if (pc != '*')
						return false;
					p++;
				}
			}

			char pl = Lower(At(pattern, p));
			char cl = Lower(c);
			if (pl == cl || pl == '?') {
				p++;
				s++;
				continue;
			}

			if (pl == '*') {
				p++;
				star = p;
				continue;
			}

			if (star == -1)
				return false;

			char sc = Lower(At(pattern, star));
			if (sc == '\0')
				return true;
			p = sc == cl || sc == '?' ? star + 1 : star;
			s++;
		}
	}
#endif

	[LuaFunction]
	static int FindByClass(ILuaInterface lua) {
		string? className = lua.GetString(1);
		lua.CreateTable();
		int i = 1;
#if CLIENT_DLL
		for (BaseEntity? ent = cl_entitylist.FirstBaseEntity(); ent != null; ent = cl_entitylist.NextBaseEntity(ent)) {
			if (!WildcardMatch(className, ent.GetClassname()) || ent.IsMarkedForDeletion())
				continue;
#else
		for (BaseEntity? ent = gEntList.FindEntityByClassname(null, className); ent != null; ent = gEntList.FindEntityByClassname(ent, className)) {
			if (ent.IsMarkedForDeletion())
				continue;
#endif
			lua.PushNumber(i++);
			LuaEntity.Push_Entity(ent);
			lua.SetTable(-3);
		}
		return 1;
	}

	// todo: FindInSphere
	// todo: FindInBox

	[LuaFunction]
	static int FindByName(ILuaInterface lua) {
		lua.CreateTable();
#if GAME_DLL
		string? name = lua.GetString(1);
		int i = 1;
		for (BaseEntity? ent = gEntList.FindEntityByName(null, name); ent != null; ent = gEntList.FindEntityByName(ent, name)) {
			lua.PushNumber(i++);
			LuaEntity.Push_Entity(ent);
			lua.SetTable(-3);
		}
#endif
		return 1;
	}

	// todo: FindByModel

	[LuaFunction]
	static int GetByIndex(ILuaInterface lua) {
		LuaEntity.Push_Entity(GMEntityByIndex((int)lua.GetNumber(1)));
		return 1;
	}

	// todo: FindInCone
	[LuaFunction]
	static int GetMapCreatedEntity(ILuaInterface lua) {
		int id = (int)lua.GetNumber(1);
#if CLIENT_DLL
		for (BaseEntity? ent = cl_entitylist.FirstBaseEntity(); ent != null; ent = cl_entitylist.NextBaseEntity(ent)) {
#else
		for (BaseEntity? ent = gEntList.NextEnt(null); ent != null; ent = gEntList.NextEnt(ent)) {
#endif
			if (ent.MapCreatedID != -1 && ent.MapCreatedID == id && !ent.IsMarkedForDeletion()) {
				LuaEntity.Push_Entity(ent);
				return 1;
			}
		}
		return 0;
	}
#if GAME_DLL
	// todo: FindInPVS
	// todo: FireTargets
#endif
}
#endif
