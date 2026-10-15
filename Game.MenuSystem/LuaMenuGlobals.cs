#if MENU_DLL
using Source;
using Source.Common.Engine;
using Source.Common.GarrysMod.Lua;
using Source.Common.Launcher;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Networking;
using Source.Engine;
using Source.GUI.Controls;

using System.Net;
using System.Text;

namespace Game.MenuSystem.GarrysMod;

public static partial class LuaMenuGlobals
{
	[LuaGlobal]
	static int GetAPIManifest(ILuaInterface lua) {
		ILuaObject? callback = lua.GetObject(1);
		if (callback != null && callback.isFunction())
			GMODGetAPIManifest.Request(callback);
		return 0;
	}

	[LuaGlobal]
	static int GetAddonStatus(ILuaInterface lua) {
		lua.PushBool(commandLine.FindParm("-noaddons") == 0);
		lua.PushBool(commandLine.FindParm("-noworkshop") == 0);
		return 2;
	}

	[LuaGlobal]
	static int GetDefaultLoadingHTML(ILuaInterface lua) {
		lua.PushString("asset://garrysmod/html/loading.html");
		return 1;
	}

	[LuaGlobal]
	static int GetDownloadables(ILuaInterface lua) {
		INetworkStringTable? downloadables = networkstringtable!.FindTable(Protocol.DOWNLOADABLE_FILE_TABLENAME);
		if (downloadables == null)
			return 0;

		int count = downloadables.GetNumStrings();
		lua.PreCreateTable(count, 0);
		for (int i = 0; i < count; i++) {
			lua.PushNumber(i + 1);
			lua.PushString(downloadables.GetString(i));
			lua.SetTable(-3);
		}

		return 1;
	}

	[LuaGlobal]
	static int GetOverlayPanel(ILuaInterface lua) {
		LuaVGUI.Push_Panel(MenuPanels.GetOverlayPanel());
		return 1;
	}

	[LuaGlobal]
	static int IsHostingGame(ILuaInterface lua) {
		lua.PushBool(engine.IsHostingGame());
		return 1;
	}

	static IPanel? FindChildByName(IPanel? parent, ReadOnlySpan<char> name) {
		if (parent == null)
			return null;

		int count = parent.GetChildCount();
		for (int i = 0; i < count; i++) {
			IPanel child = parent.GetChild(i);
			if (child.GetName().SequenceEqual(name))
				return child;

			IPanel? found = FindChildByName(child, name);
			if (found != null)
				return found;
		}

		return null;
	}

	[LuaGlobal]
	static int CancelLoading(ILuaInterface lua) {
		IPanel? root = enginevgui.GetPanel(VGuiPanelType.Root);
		if (root == null)
			return 0;

		IPanel? dialog = FindChildByName(root, "LoadingDialog");
		if (dialog != null)
			dialog.SendMessage(new KeyValues("Command", "command", "Cancel"), dialog);
		return 0;
	}

	[LuaGlobal]
	static int GetLoadStatus(ILuaInterface lua) {
		IPanel? root = enginevgui.GetPanel(VGuiPanelType.Root);
		if (root == null)
			return 0;

		IPanel? dialog = FindChildByName(root, "LoadingDialog");
		if (dialog == null)
			return 0;

		IPanel? label = FindChildByName(dialog, "InfoLabel");
		if (label is not Panel infoLabel)
			return 0;

		KeyValues data = new("GetText");
		if (!infoLabel.RequestInfo(data))
			return 0;

		KeyValues? text = data.FindKey("text");
		if (text == null)
			return 0;

		lua.PushString(text.GetString());
		return 1;
	}

	[LuaGlobal]
	static int NumDownloadables(ILuaInterface lua) {
		if (networkstringtable == null)
			return 0;

		INetworkStringTable? downloadables = networkstringtable.FindTable(Protocol.DOWNLOADABLE_FILE_TABLENAME);
		if (downloadables == null) {
			lua.PushNumber(0);
			return 0;
		}

		lua.PushNumber(downloadables.GetNumStrings());
		return 1;
	}

	[LuaGlobal]
	static int TranslateDownloadableName(ILuaInterface lua) {
		if (networkstringtable == null)
			return 0;

		INetworkStringTable? luaFiles = networkstringtable.FindTable("client_lua_files");
		if (luaFiles == null)
			return 0;

		Span<char> fileBase = stackalloc char[64];
		lua.CheckString(1).FileBase(fileBase);

		for (int i = 0; i < luaFiles.GetNumStrings(); i++) {
			ReadOnlySpan<char> entry = luaFiles.GetString(i);
			if (strncmp(fileBase, entry, entry.SliceNullTerminatedString().Length) == 0) {
				lua.PushString("Lua Cache File");
				return 1;
			}
		}

		return 0;
	}

	static byte[]? ReadWholeFile(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID) {
		long size = filesystem.Size(fileName, pathID);
		if (size <= 0)
			return null;

		byte[] data = new byte[size];
		return filesystem.ReadFile(fileName, pathID, data, 0) ? data : null;
	}

	static string ReadFixedString(ReadOnlySpan<byte> data) {
		int end = data.IndexOf((byte)0);
		return Encoding.UTF8.GetString(end < 0 ? data : data[..end]);
	}

	[LuaGlobal]
	static int GetDemoFileDetails(ILuaInterface lua) {
		byte[]? data = ReadWholeFile(lua.CheckString(1), "GAME");
		if (data == null || data.Length < 0x430)
			return 0;

		ReadOnlySpan<byte> header = data;
		if (header[0] != 'G' || header[1] != 'M' || header[2] != 'O' || header[3] != 'D')
			return 0;

		lua.CreateTable();
		lua.PushNumber(BitConverter.ToInt32(header[0x8..]));
		lua.SetField(-2, "demoprotocol");
		lua.PushNumber(BitConverter.ToInt32(header[0xc..]));
		lua.SetField(-2, "networkprotocol");
		lua.PushString(ReadFixedString(header.Slice(0x10, 260)));
		lua.SetField(-2, "servername");
		lua.PushString(ReadFixedString(header.Slice(0x114, 260)));
		lua.SetField(-2, "clientname");
		lua.PushString(ReadFixedString(header.Slice(0x218, 260)));
		lua.SetField(-2, "mapname");
		lua.PushString(ReadFixedString(header.Slice(0x31c, 260)));
		lua.SetField(-2, "gamedirectory");
		lua.PushNumber(BitConverter.ToSingle(header[0x420..]));
		lua.SetField(-2, "playback_time");
		lua.PushNumber(BitConverter.ToInt32(header[0x424..]));
		lua.SetField(-2, "playback_ticks");
		lua.PushNumber(BitConverter.ToInt32(header[0x428..]));
		lua.SetField(-2, "playback_frames");
		lua.PushNumber(BitConverter.ToInt32(header[0x42c..]));
		lua.SetField(-2, "signonlength");
		return 1;
	}

	static string ReadBufferString(ReadOnlySpan<byte> data, ref int get, int maxChars) {
		int end = data[get..].IndexOf((byte)0);
		int length = end < 0 ? data.Length - get : end;
		string str = Encoding.UTF8.GetString(data.Slice(get, Math.Min(length, maxChars - 1)));
		get = Math.Min(data.Length, get + length + 1);
		return str;
	}

	[LuaGlobal]
	static int GetSaveFileDetails(ILuaInterface lua) {
		byte[]? data = ReadWholeFile(lua.CheckString(1), "GAME");
		if (data == null) {
			Msg("GetSaveFileDetails: Couldn't open file");
			return 0;
		}

		if (data.Length < 4 || data[0] != 'G' || data[1] != 'M' || data[2] != 'S' || data[3] != '3') {
			Msg("GetSaveFileDetails/ReadFromBuffer: Invalid save file");
			return 0;
		}

		int get = 4;
		string map = ReadBufferString(data, ref get, 512);
		if (data.AsSpan(get).StartsWith("WSID:"u8)) {
			get += 5;
			ReadBufferString(data, ref get, 64);
		}

		lua.CreateTable();
		lua.PushString(map);
		lua.SetField(-2, "map");
		lua.PushNumber(0);
		lua.SetField(-2, "time");
		lua.PushNumber(data.Length - get);
		lua.SetField(-2, "size");
		return 1;
	}

	const string AddonPresetsFile = "settings/addonpresets.txt";

	[LuaGlobal]
	static int LoadAddonPresets(ILuaInterface lua) {
		byte[]? data = ReadWholeFile(AddonPresetsFile, "MOD");
		if (data == null) {
			Msg("GetSaveFileDetails: Couldn't open file");
			return 0;
		}

		int end = Array.IndexOf(data, (byte)0);
		lua.PushString(end < 0 ? data : data.AsSpan(0, end));
		return 1;
	}

	[LuaGlobal]
	static int SaveAddonPresets(ILuaInterface lua) {
		string presets = lua.CheckString(1);
		filesystem.WriteFile(AddonPresetsFile, "MOD", Encoding.UTF8.GetBytes(presets));
		return 0;
	}

	[LuaGlobal]
	static int CanAddServerToFavorites(ILuaInterface lua) {
		INetChannelInfo? nci = engine.GetNetChannelInfo();
		if (nci == null || nci.IsLoopback()) {
			lua.PushBool(false);
			return 1;
		}

		bool canAdd = false;
		ReadOnlySpan<char> address = nci.GetAddress();
		if (!address.SequenceEqual("loopback") && IPEndPoint.TryParse(address, out IPEndPoint? endpoint)) {
			byte[] ip = endpoint.Address.MapToIPv4().GetAddressBytes();
			bool localhost = ip[0] == 127 && ip[1] == 0 && ip[2] == 0 && ip[3] == 1;
			canAdd = !localhost && endpoint.Port != 0 && !endpoint.Address.Equals(IPAddress.Any);
		}

		lua.PushBool(canAdd);
		return 1;
	}

	[LuaGlobal]
	static int IsInGame(ILuaInterface lua) {
		lua.PushBool(engine.IsInGame());
		return 1;
	}

	[LuaGlobal]
	static int JoinServer(ILuaInterface lua) {
		string? address = lua.CheckString(1);
		if (address == null || address.Contains(';'))
			return 0;
		string command = $"connect {address} serverbrowser\n";
		if (command.Length > 255)
			command = command[..255];
		engine.ClientCmd(command);
		return 0;
	}

	[LuaGlobal]
	static int OpenFolder(ILuaInterface lua) {
		string name = lua.CheckString(1);
		if (name != "saves" && name != "demos" && name != "dupes")
			return 0;

		Span<char> path = stackalloc char[512];
		ReadOnlySpan<char> fullPath = filesystem.RelativePathToFullPath(name, "MOD", path);
		if (!fullPath.IsEmpty)
			Singleton<ISystem>().ShellExecute("open", fullPath);
		return 0;
	}

	[LuaGlobal]
	static int RunGameUICommand(ILuaInterface lua) {
		gameUI.SendMainMenuCommand(lua.CheckString(1));
		return 0;
	}
}
#endif
