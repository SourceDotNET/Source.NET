#if CLIENT_DLL || GAME_DLL
using Source;
using Source.Common;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
using static Game.Client.GarrysMod.GarrysMod;
#else
using static Game.Server.GarrysMod.GarrysMod;
#endif

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public class LuaSWEPManager
{
	public static LuaSWEPManager? gSWEPManager = new();

	public LuaSWEPManager() => gSWEPManager = this;

	static void GetWeaponFunction(ReadOnlySpan<char> name, LuaObject func) {
		LuaObject weapons = new();
		g_Lua!.Global().GetMember("weapons", weapons);
		if (!weapons.isTable())
			Warning("'weapons' librbary is not a table. Make sure your addons are not overriding the 'weapons' global variable.\n");
		else {
			weapons.GetMember(name, func);
			if (!func.isFunction())
				Warning($"weapons.{name} is not a function. Make sure your addons are not overriding it.\n");
		}
		weapons.UnReference();
	}

	public void LoadScript(ReadOnlySpan<char> scriptName) {
		if (g_LuaManager == null || g_Lua == null || g_Lua.Global() == null)
			return;

		Span<char> nameBuffer = stackalloc char[260];
		strcpy(nameBuffer, scriptName);
		string name = new string(((ReadOnlySpan<char>)nameBuffer).SliceNullTerminatedString()).ToLowerInvariant();

		string path;
		if (name.Contains(".lua", StringComparison.OrdinalIgnoreCase)) {
			path = $"weapons/{name}";
			Span<char> stripped = stackalloc char[260];
			name.AsSpan().StripExtension(stripped);
			name = new string(((ReadOnlySpan<char>)stripped).SliceNullTerminatedString());
#if GAME_DLL
			FileServ.AddCSLuaFile("weapons/" + name + ".lua", "!WEP");
#endif
		}
		else {
#if CLIENT_DLL
			path = $"weapons/{name}/cl_init.lua";
			if (!g_LuaManager.ScriptExists(path, LuaPathID))
				path = $"weapons/{name}/shared.lua";
#else
			path = $"weapons/{name}/init.lua";
			if (!g_LuaManager.ScriptExists(path, LuaPathID)) {
				path = $"weapons/{name}/shared.lua";
				FileServ.AddCSLuaFile("weapons/" + name + "/shared.lua", "!WEP");
			}
			else
				FileServ.AddCSLuaFile("weapons/" + name + "/cl_init.lua", "!WEP");
#endif
		}

		if (!g_LuaManager.ScriptExists(path, LuaPathID))
			return;

		LuaTable swep = new("SWEP", 0);
		swep.SetMember("Folder", $"weapons/{name}");
		swep.SetMember("Base", "weapon_base");
		LuaTable primary = new(null, 0);
		LuaTable secondary = new(null, 0);
		swep.SetMember("Primary", primary);
		swep.SetMember("Secondary", secondary);

		if (g_LuaManager.RunScript(path, LuaPathID, true, "!WEP")) {
			LuaObject register = new();
			GetWeaponFunction("Register", register);
			if (register.isFunction()) {
				register.Push();
				swep.Push();
				g_Lua.PushString(name);
				g_Lua.CallInternalNoReturns(2);
			}
			register.UnReference();
		}

		g_Lua.Global().SetMemberNil("SWEP");
		g_Lua.Global().SetMemberNil("ENT");

		secondary.UnReference();
		primary.UnReference();
		swep.UnReference();
	}

#if GAME_DLL
	public IServerNetworkable? CreateEntity(ReadOnlySpan<char> name) {
		if (IsSacredEntity(name) || g_Lua == null)
			return null;

		LuaObject get = new();
		GetWeaponFunction("Get", get);
		if (!get.isFunction()) {
			get.UnReference();
			return null;
		}

		IServerNetworkable? ent = null;
		LuaObject swep = new();
		get.Push();
		g_Lua.PushString(name);
		if (!g_Lua.CallInternalGet(1, swep))
			Warning($"Error creating scripted weapon {name} (errored?)\n");
		else if (swep.isTable()) {
			ent = EntityFactoryDictionary().Create("weapon_swep");
			if (ent != null)
				((BaseEntity)ent.GetBaseEntity()!).InitializeScriptedEntity(name);
		}

		swep.UnReference();
		get.UnReference();
		return ent;
	}
#endif

	public void ReloadSpecific(ReadOnlySpan<char> name) => LoadScript(name);

	public void LoadScripts() {
		List<LuaFindResult> scripts = [];
		get.LuaShared()!.FindScripts("weapons/*", LuaPathID, scripts);
		foreach (LuaFindResult script in scripts)
			LoadScript(script.FileName);

		LuaObject onLoaded = new();
		GetWeaponFunction("OnLoaded", onLoaded);
		if (onLoaded.isFunction()) {
			onLoaded.Push();
			g_Lua!.CallInternalNoReturns(0);
		}
		onLoaded.UnReference();
	}
}
#endif
