#if CLIENT_DLL || GAME_DLL
using Source;
using Source.Common;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

using static Game.Client.GarrysMod.GarrysMod;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public class LuaSENTManager
{
	public static LuaSENTManager? gSENTManager = new();

	public LuaSENTManager() => gSENTManager = this;

	static void GetSENTsFunction(ReadOnlySpan<char> name, LuaObject func) {
		LuaObject scriptedEnts = new();
		g_Lua!.Global().GetMember("scripted_ents", scriptedEnts);
		if (!scriptedEnts.isTable())
			Warning("'scripted_ents' library is not a table. Make sure your addons are not overriding the 'scripted_ents' global variable.\n");
		else {
			scriptedEnts.GetMember(name, func);
			if (!func.isFunction())
				Warning($"scripted_ents.{name} is not a function. Make sure your addons are not overriding it.\n");
		}
		scriptedEnts.UnReference();
	}

	public void LoadScript(ReadOnlySpan<char> scriptName) {
		if (g_LuaManager == null || g_Lua == null || g_Lua.Global() == null)
			return;

		Span<char> nameBuffer = stackalloc char[260];
		strcpy(nameBuffer, scriptName);
		string name = new string(((ReadOnlySpan<char>)nameBuffer).SliceNullTerminatedString()).ToLowerInvariant();

		string path;
		if (name.Contains(".lua", StringComparison.OrdinalIgnoreCase)) {
			path = $"entities/{name}";
			Span<char> stripped = stackalloc char[260];
			name.AsSpan().StripExtension(stripped);
			name = new string(((ReadOnlySpan<char>)stripped).SliceNullTerminatedString());
		}
		else {
#if CLIENT_DLL
			path = $"entities/{name}/cl_init.lua";
#else
			path = $"entities/{name}/init.lua";
#endif
			if (!g_LuaManager.ScriptExists(path, LuaPathID))
				path = $"entities/{name}/shared.lua";
		}

		if (!g_LuaManager.ScriptExists(path, LuaPathID))
			return;

		LuaTable ent = new("ENT", 0);
		ent.SetMember("Folder", $"entities/{name}");
		g_LuaManager.RunScript(path, LuaPathID, true, "!ENT");

		if (!ent.MemberIsNil("Base") || !ent.MemberIsNil("Type")) {
			LuaObject register = new();
			GetSENTsFunction("Register", register);
			if (register.isFunction()) {
				register.Push();
				ent.Push();
				g_Lua.PushString(name);
				g_Lua.CallInternalNoReturns(2);
				g_Lua.Global().SetMemberNil("ENT");
			}
			register.UnReference();
		}
		else
			g_Lua.ErrorNoHalt($"Refusing to load {name} because it is missing Type and Base keys!\n");

		ent.UnReference();
	}

	public void ReloadSpecific(ReadOnlySpan<char> name) => LoadScript(name);

	public void LoadScripts() {
		List<LuaFindResult> scripts = [];
		get.LuaShared()!.FindScripts("entities/*", LuaPathID, scripts);
		foreach (LuaFindResult script in scripts)
			LoadScript(script.FileName);

		LuaObject onLoaded = new();
		GetSENTsFunction("OnLoaded", onLoaded);
		if (onLoaded.isFunction()) {
			onLoaded.Push();
			g_Lua!.CallInternalNoReturns(0);
		}
		onLoaded.UnReference();
	}

#if CLIENT_DLL
	public C_BaseEntity? CreateEntity(ReadOnlySpan<char> name) {
#else
	public IServerNetworkable? CreateEntity(ReadOnlySpan<char> name) {
#endif
		if (IsSacredEntity(name) || g_Lua == null || g_Lua.Global() == null)
			return null;

		LuaObject get = new();
		GetSENTsFunction("Get", get);
		if (!get.isFunction()) {
			get.UnReference();
			return null;
		}

		get.Push();
		g_Lua.PushString(name);
#if CLIENT_DLL
		C_BaseEntity? ent = null;
#else
		IServerNetworkable? ent = null;
#endif
		LuaObject sent = new();
		if (g_Lua.CallInternalGet(1, sent) && sent.isTable()) {
			string? type = sent.GetMemberStr("Type", null);
			if (type == null)
				Warning($"Error: Tried to make a SENT ({name}) but it didn't have a 'Type' key!\n");
			else {
#if CLIENT_DLL
				if (stricmp(type, "anim") != 0 && stricmp(type, "vehicle") != 0)
					Warning($"Error: Tried to make a SENT ({name}) but the 'Type' ({type}) was invalid!\n");
				else {
					ent = GetClassMap().CreateEntity("sent_anim");
					if (ent == null)
						Warning($"Error: Couldn't make SENT ({name}) (?!?)\n");
					else {
						if (ent.HasLuaTable())
							Warning("CREATING ENTITY - ALREADY HAS A LUA TABLE! AND IT SHOULDN'T!\n");
						ent.InitializeAsClientEntity(null, Source.Common.Engine.RenderGroup.TwoPass);
						ent.UpdatePartitionListEntry();
						ent.CollisionProp().UpdatePartition();
						ent.SetBlocksLOS(false);
						ent.CreateShadow();
						ent.UpdateVisibility();
						ent.InitializeScriptedEntity(name);
					}
				}
#else
				ReadOnlySpan<char> className = null;
				if (stricmp(type, "anim") == 0 || stricmp(type, "vehicle") == 0)
					className = "sent_anim";
				else if (stricmp(type, "ai") == 0)
					className = "sent_ai";
				else if (stricmp(type, "point") == 0)
					className = "sent_point";
				else if (stricmp(type, "filter") == 0)
					className = "sent_filter";
				else if (stricmp(type, "brush") == 0)
					className = "sent_brush";
				else if (stricmp(type, "nextbot") == 0)
					className = "sent_nextbot";
				else
					Warning($"Error: Tried to make a SENT ({name}) but the 'Type' ({type}) was invalid!\n");

				if (!className.IsEmpty) {
					ent = EntityFactoryDictionary().Create(className);
					if (ent == null)
						Warning($"Error: Couldn't make SENT ({name}) (?!?)\n");
					else {
						if (((BaseEntity)ent.GetBaseEntity()!).HasLuaTable())
							Warning("CREATING ENTITY - ALREADY HAS A LUA TABLE! AND IT SHOULDN'T!\n");
						((BaseEntity)ent.GetBaseEntity()!).InitializeScriptedEntity(name);
					}
				}
#endif
			}
		}

		sent.UnReference();
		get.UnReference();
		return ent;
	}
}
#endif
