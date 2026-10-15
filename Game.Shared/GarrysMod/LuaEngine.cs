#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaEngine
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_engine = new("engine");

	[LuaFunction]
	static int TickInterval(ILuaInterface lua) {
		g_Lua!.PushNumber(gpGlobals.IntervalPerTick);
		return 1;
	}

	[LuaFunction]
	static int ActiveGamemode(ILuaInterface lua) {
		g_Lua!.PushString(filesystem.Gamemodes().Active().Name);
		return 1;
	}

#if CLIENT_DLL || MENU_DLL
	[LuaFunction]
	static int IsRecordingDemo(ILuaInterface lua) {
		g_Lua!.PushBool(engine.IsRecordingDemo());
		return 1;
	}

	[LuaFunction]
	static int IsPlayingDemo(ILuaInterface lua) {
		g_Lua!.PushBool(engine.IsPlayingDemo());
		return 1;
	}

	[LuaFunction]
	static int GetDemoPlaybackTick(ILuaInterface lua) {
		lua.PushNumber(engine.GetDemoPlaybackTick());
		return 1;
	}

	[LuaFunction]
	static int GetDemoPlaybackStartTick(ILuaInterface lua) {
		lua.PushNumber(engine.GetDemoPlaybackStartTick());
		return 1;
	}

	[LuaFunction]
	static int GetDemoPlaybackTimeScale(ILuaInterface lua) {
		lua.PushNumber(engine.GetDemoPlaybackTimeScale());
		return 1;
	}

	[LuaFunction]
	static int GetDemoPlaybackTotalTicks(ILuaInterface lua) {
		lua.PushNumber(engine.GetDemoPlaybackTotalTicks());
		return 1;
	}

	[LuaFunction]
	static int AbsoluteFrameTime(ILuaInterface lua) {
		lua.PushNumber(gpGlobals.AbsoluteFrameTime);
		return 1;
	}

	[LuaFunction]
	static int TickCount(ILuaInterface lua) {
		lua.PushNumber(gpGlobals.TickCount);
		return 1;
	}

	[LuaFunction]
	static int GetUserContent(ILuaInterface lua) {
		LinkedList<IAddonSystem.UGCInfo> list = filesystem.Addons().GetUGCList();
		LuaTable table = new(null, (uint)list.Count);
		int i = 1;
		foreach (IAddonSystem.UGCInfo info in list) {
			LuaTable entry = new(null, 0);
			entry.SetMember("title", info.Title);
			entry.SetMember("tags", info.Tags);
			entry.SetMember("wsid", info.WorkshopID.ToString());
			entry.SetMember("timeadded", (double)info.TimeAdded);
			entry.SetMember("type", info.Type switch {
				Addon.AddonType.Dupe => "dupe",
				Addon.AddonType.Save => "save",
				Addon.AddonType.Demo => "demo",
				_ => "unknown"
			});
			table.SetMember((float)i, entry);
			i++;
			entry.UnReference();
		}
		table.Push();
		table.UnReference();
		return 1;
	}
#endif

#if MENU_DLL
	[LuaFunction]
	static int SetMounted(ILuaInterface lua) {
		bool mounted = lua.GetBool(2);
		filesystem.Games().SetMount(new Steamworks.AppId_t((uint)(int)lua.CheckNumber(1)), mounted);
		filesystem.Games().Save();
		filesystem.Games().Refresh();
		filesystem.DoFilesystemRefresh();
		return 0;
	}

	[LuaFunction]
	static int CurrentServerAddress(ILuaInterface lua) {
		Source.Common.Networking.INetChannelInfo? channel = engine.GetNetChannelInfo();
		lua.PushString(channel == null ? "" : channel.GetAddress());
		return 1;
	}
#endif

	[LuaFunction]
	static int GetAddons(ILuaInterface lua) {
		LinkedList<IAddonSystem.Information> list = filesystem.Addons().GetList();
		LuaTable table = new(null, (uint)list.Count);
		int i = 1;
		foreach (IAddonSystem.Information info in list) {
			LuaTable entry = new(null, 0);
			entry.SetMember("title", info.Title);
			entry.SetMember("size", (double)info.Size);
			entry.SetMember("updated", (double)info.TimeUpdated);
			entry.SetMember("tags", info.Tags);
			entry.SetMember("models", (float)info.Models);
			entry.SetMember("wsid", info.WorkshopID);
			entry.SetMember("downloaded", info.Downloaded);
			entry.SetMember("timeadded", (double)info.TimeAdded);
			entry.SetMember("mounted", filesystem.Addons().ShouldMount(info.WorkshopID));

			string file = info.File.Replace("\\", "/");
			int workshop = file.IndexOf("steamapps/workshop/content/4000", StringComparison.Ordinal);
			if (workshop != -1)
				file = file[(workshop + 19)..];
			entry.SetMember("file", file);

			if (info.Failure.Length != 0)
				entry.SetMember("invalid_reason", info.Failure);
			else if (!info.Downloaded)
				entry.SetMember("invalid_reason", info.Failed ? "Failed to download" : "Download pending");

			table.SetMember((float)i, entry);
			i++;
			entry.UnReference();
		}
		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaFunction]
	static int GetGames(ILuaInterface lua) {
		List<IGameDepotSystem.Information> list = filesystem.Games().GetList();
		LuaTable table = new(null, (uint)list.Count);
		int i = 1;
		foreach (IGameDepotSystem.Information info in list) {
			LuaTable entry = new(null, 0);
			entry.SetMember("depot", (int)info.Depot);
			entry.SetMember("title", info.Title);
			entry.SetMember("mounted", info.Mounted && info.Owned && info.Installed);
			entry.SetMember("installed", info.Installed);
			entry.SetMember("owned", info.Owned);
			entry.SetMember("folder", info.Folder);
			table.SetMember((float)i, entry);
			i++;
			entry.UnReference();
		}
		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaFunction]
	static int GetGamemodes(ILuaInterface lua) {
		List<IGamemodeSystem.Information> list = filesystem.Gamemodes().GetList();
		LuaTable table = new(null, (uint)list.Count);
		int i = 1;
		foreach (IGamemodeSystem.Information info in list) {
			LuaTable entry = new(null, 0);
			entry.SetMember("title", info.Title);
			entry.SetMember("name", info.Name);
			entry.SetMember("maps", info.Maps);
			entry.SetMember("menusystem", info.MenuSystem);
			entry.SetMember("workshopid", info.WorkshopID.ToString());
			table.SetMember((float)i, entry);
			i++;
			entry.UnReference();
		}
		table.Push();
		table.UnReference();
		return 1;
	}
}
#endif
