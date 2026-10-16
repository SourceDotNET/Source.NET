#if CLIENT_DLL || MENU_DLL
using Source;
using Source.Common.GarrysMod.Lua;
using Source.Common.Networking;
using Source.Common.Utilities;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.MenuSystem.GarrysMod;
#endif

public static partial class LuaPermissions
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_permissions = new("permissions");

	const string PermissionsFile = "settings/permissions.bin";
	const int InvalidPermission = 8;

	static readonly string[] PermissionNames = ["connect", "voicerecord", "workshopdownload", "javascript", "escapemenu", "filewrite", "motionsensor"];

	static readonly SortedDictionary<string, uint> Permanent = new(StringComparer.Ordinal);
	static readonly SortedDictionary<string, uint> Temporary = new(StringComparer.Ordinal);
	static bool Loaded;

	static int PermissionFromName(ReadOnlySpan<char> name) {
		if (name.SequenceEqual("connect"))
			return 0;
		if (name.SequenceEqual("voicerecord"))
			return 1;
		if (name.SequenceEqual("motionsensor"))
			return 6;
		return InvalidPermission;
	}

	static uint Index(SortedDictionary<string, uint> map, string address) {
		if (!map.TryGetValue(address, out uint mask))
			map[address] = mask = 0;
		return mask;
	}

	static string? GetServerAddress() {
		INetChannelInfo? nci = engine.GetNetChannelInfo();
		if (nci == null)
			return null;
		ReadOnlySpan<char> address = nci.GetAddress();
		return address.IsEmpty ? null : new string(address);
	}

	static void Load() {
		if (Loaded)
			return;
		Loaded = true;

		long size = filesystem.Size(PermissionsFile, "MOD");
		if (size <= 0)
			return;

		byte[] data = new byte[size];
		if (!filesystem.ReadFile(PermissionsFile, "MOD", data, 0))
			return;

		UtlBuffer buf = new(data, UtlBuffer.BufferFlags.ReadOnly);
		uint count = buf.GetUnsignedInt();
		Span<char> address = stackalloc char[256];
		for (uint i = 0; i < count; i++) {
			buf.GetString(address);
			uint mask = buf.GetUnsignedInt();
			Permanent.TryAdd(new string(((ReadOnlySpan<char>)address).SliceNullTerminatedString()), mask);
		}
	}

#if MENU_DLL
	static void Save() {
		UtlBuffer buf = new();
		buf.PutUnsignedInt((uint)Permanent.Count);
		foreach (KeyValuePair<string, uint> entry in Permanent) {
			if (entry.Value == 0 || entry.Key == "loopback")
				continue;
			buf.PutString(entry.Key);
			buf.PutUnsignedInt(entry.Value);
		}
		filesystem.WriteFile(PermissionsFile, "MOD", buf.Base().AsSpan(0, buf.TellPut()));
	}

	static void CallPermissionsChanged() => MenuSystem.CallHook("OnPermissionsChanged");

	static void Connect(ReadOnlySpan<char> address) {
		string trimmed = new string(address).Trim(" \t;".ToCharArray());
		if (trimmed.Contains(';'))
			return;

		string command = $"connect {trimmed} serverbrowser\n";
		if (command.Length > 255)
			command = command[..255];
		engine.ClientCmd(command);

		Temporary[trimmed] = Index(Temporary, trimmed) | 1;
	}

#endif

	static bool IsGranted(string name) {
		int permission = PermissionFromName(name);
		if (permission == InvalidPermission) {
			g_Lua!.ErrorFromLua($"Invalid permission '{name}'!");
			return false;
		}

		Load();

		string? address = GetServerAddress();
		if (address == null)
			return false;

		uint mask = Index(Permanent, address) | Index(Temporary, address);
		return (mask & (1u << permission)) != 0;
	}

#if MENU_DLL
	[LuaFunction]
	static int Connect(ILuaInterface lua) {
		string address = lua.CheckString(1);
		lua.CheckStringOpt(2, null);
		Connect(address);
		return 0;
	}

#endif

	[LuaFunction]
	static int IsGranted(ILuaInterface lua) {
		lua.PushBool(IsGranted(lua.CheckString(1)));
		return 1;
	}

#if MENU_DLL
	[LuaFunction]
	static int Grant(ILuaInterface lua) {
		Load();

		string name = g_Lua!.CheckString(1);
		bool temporary = g_Lua.GetType(2) != LuaType.Nil && g_Lua.GetBool(2);

		string? address = GetServerAddress();
		if (address == null)
			return 0;

		int permission = PermissionFromName(name);
		if (permission == InvalidPermission) {
			g_Lua.ErrorFromLua($"Invalid permission '{name}'!");
			return 0;
		}

		if (temporary)
			Temporary[address] = Index(Temporary, address) | (1u << permission);
		else {
			Permanent[address] = Index(Permanent, address) | (1u << permission);
			Save();
		}

		CallPermissionsChanged();
		return 0;
	}

	[LuaFunction]
	static int Revoke(ILuaInterface lua) {
		Load();

		string name = g_Lua!.CheckString(1);
		string? address = g_Lua.CheckString(2);
		if (address == null)
			return 0;

		int permission = PermissionFromName(name);
		if (permission == InvalidPermission) {
			g_Lua.ErrorFromLua($"Invalid permission '{name}'!");
			return 0;
		}

		uint clear = ~(1u << permission);
		Temporary[address] = Index(Temporary, address) & clear;
		Permanent[address] = Index(Permanent, address) & clear;
		Save();

		CallPermissionsChanged();
		return 0;
	}

	static void PushPermissionTable(ILuaInterface lua, SortedDictionary<string, uint> map) {
		lua.CreateTable();
		foreach (KeyValuePair<string, uint> entry in map) {
			if (entry.Value == 0)
				continue;

			List<string> names = [];
			for (int i = 0; i < PermissionNames.Length; i++) {
				if ((entry.Value & (1u << i)) != 0)
					names.Add(PermissionNames[i]);
			}

			lua.PushString(string.Join(',', names));
			lua.SetField(-2, entry.Key);
		}
	}

	[LuaFunction]
	static int GetAll(ILuaInterface lua) {
		Load();

		lua.CreateTable();
		PushPermissionTable(lua, Permanent);
		lua.SetField(-2, "permanent");
		PushPermissionTable(lua, Temporary);
		lua.SetField(-2, "temporary");
		return 1;
	}
#endif
}
#endif
