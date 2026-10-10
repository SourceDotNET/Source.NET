#if CLIENT_DLL || GAME_DLL
#if CLIENT_DLL
global using static Game.Client.GarrysMod.LuaGamemodeGlobals;
#else
global using static Game.Server.GarrysMod.LuaGamemodeGlobals;
#endif

using Source.Common;
using Source.Common.Engine;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static class LuaGamemodeGlobals
{
	public static LuaGamemode? gGM;
}

public class LuaGamemode : LuaObject, IDisposable
{
	public readonly LuaObject Library = new();
	public readonly LuaObject Hook = new();
	public readonly LuaObject HookCall = new();
	public string Name = "base";

	public void Dispose() {
		gGM = null;
		HookCall.UnReference();
		Hook.UnReference();
		Library.UnReference();
		UnReference();
	}

	public void LoadGamemode(string name, bool reload) {
		if (Game.Client.GarrysMod.GarrysMod.g_LuaManager == null)
			Error("Tried to LoadGamemode with NULL g_LuaManager");
		if (g_Lua == null)
			Error("Tried to LoadGamemode with NULL g_Lua");
		if (g_Lua.Global() == null)
			Error("Tried to LoadGamemode with NULL g_Lua->Global");

		string folder = "gamemodes/" + name;
#if CLIENT_DLL
		string init = name + "/gamemode/" + "cl_init.lua";
#else
		string init = name + "/gamemode/" + "init.lua";
#endif

		if (!reload) {
#if GAME_DLL
			FileServ.Add(folder + "/send.txt");
			FileServ.AddCSLuaFile(name + "/gamemode/" + "cl_init.lua", "!GM");
#endif
			foreach (ILegacyAddons.Information addon in filesystem.LegacyAddons().GetList()) {
				string path = addon.GamemodesPath + "/" + folder + "/entities";
				if (!string.IsNullOrEmpty(addon.GamemodesPath) && filesystem.IsDirectory(path, "MOD"))
					get.LuaShared()!.MountLuaAdd(path, Game.Client.GarrysMod.GarrysMod.LuaPathID);
			}
			get.LuaShared()!.MountLuaAdd("workshop/" + folder + "/entities", Game.Client.GarrysMod.GarrysMod.LuaPathID);
			get.LuaShared()!.MountLuaAdd(folder + "/entities", g_Lua.GetPathID());
		}

		LuaTable GM = new("GM", 0);
		GM.SetMember("Folder", folder);
		GM.SetMember("FolderName", name);
		if (!Game.Client.GarrysMod.GarrysMod.g_LuaManager.RunScript(init, Game.Client.GarrysMod.GarrysMod.LuaPathID, true, "!GM"))
			g_Lua.ErrorFromLua($"Couldn't Load Init Script: '{init}'\n");

		string derived = GM.GetMemberStr("DerivedFrom", "base") ?? "";

		Library.SetFromGlobal("gamemode");
		if (!Library.isTable()) {
			g_Lua.ErrorFromLua($"LoadGamemode: 'gamemode' library is not a table (it's {g_Lua.GetTypeName(Library.GetType())}, '{Library.GetString()}'), make sure addon is not overwriting it and there are no prior Lua errors!\n");
			GM.UnReference();
			return;
		}

		LuaObject register = new();
		Library.GetMember("Register", register);
		if (!register.isFunction()) {
			g_Lua.ErrorFromLua("gamemode.Register is not a function, make sure addon is not overwriting it!\n");
			register.UnReference();
			GM.UnReference();
			return;
		}

		register.Push();
		g_Lua.PushLuaObject(GM);
		g_Lua.PushString(name);
		g_Lua.PushString(derived);
		g_Lua.PushBool(reload);
		g_Lua.CallInternalNoReturns(4);
		register.UnReference();
		g_Lua.Global().SetMemberNil("GM");
		GM.UnReference();
	}

	public void ReloadGamemode() => LoadGamemode(Name, true);

	public bool IsValidGamemode(string name) {
		string init = name + "/gamemode/" + "init.lua";
		return Game.Client.GarrysMod.GarrysMod.g_LuaManager!.ScriptExists(init, Game.Client.GarrysMod.GarrysMod.LuaPathID);
	}

	public void DeriveGamemode(string name) {
		if (g_Lua == null || g_Lua.Global() == null) {
			Error("DeriveGamemode: No Lua?!\n");
			return;
		}

		LuaObject GM = new();
		g_Lua.Global().GetMember("GM", GM);
		GM.SetMember("DerivedFrom", name);
		LoadGamemode(name, false);
		g_Lua.Global().SetMember("GM", GM);
		GM.UnReference();
	}

	public void SetGamemode(string name, bool initialize) {
		if (g_Lua == null || g_Lua.Global() == null) {
			Error("LoadGamemode: No Lua?!\n");
			return;
		}

		if (!Library.isTable()) {
			g_Lua.ErrorFromLua($"SetGamemode: 'gamemode' library is not a table (it's {g_Lua.GetTypeName(Library.GetType())}, '{Library.GetString()}'), make sure addon is not overwriting it and there are no prior Lua errors!\n");
			return;
		}

		LuaObject get = new();
		Library.GetMember("Get", get);
		if (!get.isFunction()) {
			g_Lua.ErrorFromLua("gamemode.Get is not a function, make sure addon is not overwriting it!\n");
			get.UnReference();
			return;
		}

		get.Push();
		g_Lua.PushString(name);
		if (!g_Lua.CallInternalGet(1, this)) {
			g_Lua.ErrorFromLua($"Error when retrieving Gamemode '{name}'!\n");
			UnReference();
		}

		if (!isTable()) {
			g_Lua.ErrorFromLua($"gamemode.Get() - Gamemode table for gamemode '{name}' not found!\n");
			UnReference();
			get.UnReference();
			return;
		}

		Name = name;
		g_Lua.Global().SetMember("GAMEMODE", this);
		g_Lua.Global().SetMember("GAMEMODE_NAME", Name);

		Hook.SetFromGlobal("hook");
		Hook.GetMember("Call", HookCall);
		if (!HookCall.isFunction()) {
			g_Lua.ErrorFromLua("hook.Call is not a function!\n");
			HookCall.UnReference();
			get.UnReference();
			return;
		}

		if (initialize) {
			LuaSWEPManager.gSWEPManager!.LoadScripts();
			// todo: LuaSENTManager.LoadScripts();
			// todo: gEffectManager.LoadScripts();
			Call("CreateTeams");
			Call("PreGamemodeLoaded");
			Call("OnGamemodeLoaded");
			Call("PostGamemodeLoaded");
			Call("Initialize");
		}
		get.UnReference();
	}

	public bool Call(string name) {
		if (!ThreadInMainThread())
			Error($"[GM:Call - !ThreadInMainThread] {name}\n");

		if (!isTable() || !HookCall.isFunction())
			return false;

		HookCall.Push();
		g_Lua!.PushString(name);
		Push();
		g_Lua.CallInternalNoReturns(2);
		return true;
	}

	public bool Call(int pooledName) {
		if (!ThreadInMainThread())
			Error($"[GM:Call - !ThreadInMainThread] {g_Lua!.GetPooledString(pooledName)}\n");

		if (!isTable() || !HookCall.isFunction())
			return false;

		HookCall.Push();
		g_Lua!.PushPooledString(pooledName);
		Push();
		g_Lua.CallInternalNoReturns(2);
		return true;
	}

	public bool CallWithArgs(string name) {
		if (!ThreadInMainThread())
			Error($"[GM:CallWithArgs - !ThreadInMainThread] {name}\n");

		if (!isTable() || !HookCall.isFunction())
			return false;

		HookCall.Push();
		g_Lua!.PushString(name);
		Push();
		return true;
	}

	public bool CallWithArgs(int pooledName) {
		if (!ThreadInMainThread())
			Error($"[GM:CallWithArgs - !ThreadInMainThread] {g_Lua!.GetPooledString(pooledName)}\n");

		if (!isTable() || !HookCall.isFunction())
			return false;

		HookCall.Push();
		g_Lua!.PushPooledString(pooledName);
		Push();
		return true;
	}

	public bool CallFinish(int args) => g_Lua!.CallInternalGetBool(args + 2);

	public bool CallFinishBool(int args, bool def) {
		if (g_Lua!.CallInternal(args + 2, 1)) {
			if (g_Lua.GetReturn(0).isBool())
				def = g_Lua.GetReturn(0).GetBool();
		}
		return def;
	}

	public bool CallReturns(int args, int returns) => g_Lua!.CallInternal(args + 2, returns);

	public void CallNoReturns(int args) => g_Lua!.CallInternalNoReturns(args + 2);

#if GAME_DLL
	public void LoadCurrentlyActiveGamemode() {
		ref IGamemodeSystem.Information active = ref filesystem.Gamemodes().Active();
		if (!active.Exists)
			Error($"Error loading gamemode!\nGamemode \"{active.Name}\" doesn't exist!\n");

		if (!IsValidGamemode(active.Name))
			Error($"Error loading gamemode!\nCannot find/load \"gamemodes/{active.Name}/gamemode/{"init.lua"}\"!\n");

		LoadGamemode(active.Name, false);
		SetGamemode(active.Name, true);

		INetworkStringTable? downloadables = networkstringtable.FindTable("downloadables");
		ref IGamemodeSystem.Information info = ref filesystem.Gamemodes().Active();
		if (downloadables != null && info.WorkshopID != 0) {
			Msg("Making workshop gamemode available for client download\n");
			string gma = Bootil.String.Format.UInt64(info.WorkshopID) + ".gma";
			if (downloadables.FindStringIndex(gma) == INetworkStringTable.INVALID_STRING_INDEX)
				downloadables.AddString(true, gma);
		}

		string map = "maps/" + gpGlobals.MapName + ".bsp";
		if (filesystem.Addons().FindFileOwner(map, out IAddonSystem.Information owner) && downloadables != null) {
			string gma = Bootil.String.Format.UInt64(owner.WorkshopID) + ".gma";
			Msg("Making workshop map available for client download\n");
			if (downloadables.FindStringIndex(gma) == INetworkStringTable.INVALID_STRING_INDEX)
				downloadables.AddString(true, gma);
		}
	}
#endif
}
#endif
