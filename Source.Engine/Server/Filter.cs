using Source.Common;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Networking;
using Source.Common.Server;

using Steamworks;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Source.Engine.Server;

public struct IPFilter {
	public uint Mask;
	public uint Compare;
	public float BanEndTime;
	public float BanTime;
}

public struct UserFilter {
	public USERID UserID;
	public float BanEndTime;
	public float BanTime;
}

[EngineComponent]
public class Filter(Host Host)
{
	public const int MAX_IPFILTERS = 32768;
	public const int MAX_USERFILTERS	    = 32768;

	public const string BANNED_IP_FILENAME = "banned_ip.cfg";
	public const string BANNED_USER_FILENAME = "banned_user.cfg";
	public const string CONFIG_DIR = "cfg/";
	public const string STEAM_PREFIX = "STEAM_";

	static ConVar sv_filterban = new("sv_filterban", "1", 0, "Set packet filtering by IP mode");
	readonly List<IPFilter> IPFilters = [];
	readonly List<UserFilter> UserFilters = [];

	Net? _net = null; Net Net => _net ??= Singleton<Net>();

	public void SendBan(NetAddress adr) {
		Net.OutOfBandPrint(NetSocketType.Server, adr, $"{(char)A2A.Print}Banned by server\n");
	}

	static uint GetIP(NetAddress adr) {
		Span<byte> ip = stackalloc byte[4];
		ip.Clear();
		IPAddress? address = adr.Endpoint?.Address;
		if (address != null && address.AddressFamily == AddressFamily.InterNetwork)
			address.TryWriteBytes(ip, out _);
		return MemoryMarshal.Read<uint>(ip);
	}

	public bool ShouldDiscard(NetAddress adr) {
		if (sv_filterban.GetInt() == 0)
			return false;

		bool negativeFilter = sv_filterban.GetInt() == 1;

		if (IPFilters.Count == 0)
			return !negativeFilter;

		uint ip = GetIP(adr);

		for (int i = IPFilters.Count - 1; i >= 0; i--) {
			if ((IPFilters[i].Compare != 0xffffffff) &&
				 (IPFilters[i].BanEndTime != 0.0f) &&
				 (IPFilters[i].BanEndTime <= Host.RealTime)) {
				IPFilters.RemoveAt(i);
				continue;
			}

			if ((ip & IPFilters[i].Mask) == IPFilters[i].Compare)
				return negativeFilter;
		}

		return !negativeFilter;
	}

	static bool ConvertString(ReadOnlySpan<char> s, out IPFilter f) {
		Span<char> num = stackalloc char[128];
		Span<byte> b = stackalloc byte[4];
		Span<byte> m = stackalloc byte[4];
		b.Clear();
		m.Clear();
		f = default;

		int pos = 0;
		for (int i = 0; i < 4; i++) {
			if (pos >= s.Length || s[pos] < '0' || s[pos] > '9') {
				ConMsg($"Bad filter address: {s[Math.Min(pos, s.Length)..]}\n");
				return false;
			}

			int j = 0;
			while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9')
				num[j++] = s[pos++];

			b[i] = (byte)atoi(num[..j]);
			if (b[i] != 0)
				m[i] = 255;

			if (pos >= s.Length)
				break;

			pos++;
		}

		f.Mask = MemoryMarshal.Read<uint>(m);
		f.Compare = MemoryMarshal.Read<uint>(b);

		return true;
	}

	static ReadOnlySpan<char> CommandIssuer(CommandSource source, int clientSlot)
		=> source == CommandSource.Command ? "Console" : sv.GetClient(clientSlot)!.GetClientName();

	static string BanDuration(float banTime)
		=> banTime == 0.0f ? "permanently" : $"for {banTime.ToString("F2", CultureInfo.InvariantCulture)} minutes";

	[ConCommand("addip", "Add an IP address to the ban list.")]
	void addip(in TokenizedCommand args, CommandSource source, int clientSlot) {
		bool kick = true;
		bool found = false;
		BaseClient? client = null;

		if (stricmp(args[0], "banip") == 0)
			ConMsg("Note: should use \"addip\" instead of \"banip\".\n");

		if (args.ArgC() != 3) {
			ConMsg("Usage:  addip < minutes > < ipaddress >\nUse 0 minutes for permanent\n");
			return;
		}

		if (!ConvertString(args[2], out IPFilter f))
			return;

		int i;
		for (i = 0; i < IPFilters.Count; i++) {
			if (IPFilters[i].Compare == 0xffffffff || (IPFilters[i].Compare == f.Compare && IPFilters[i].Mask == f.Mask))
				break;
		}

		if (i == IPFilters.Count) {
			if (IPFilters.Count == MAX_IPFILTERS) {
				ConMsg("addip:  IP filter list is full\n");
				return;
			}

			IPFilters.Add(default);
		}
		else
			kick = false;

		float banTime = (float)atof(args[1]);
		if (banTime < 0.01f)
			banTime = 0.0f;

		if (!ConvertString(args[2], out IPFilter filter))
			filter.Compare = 0xffffffff;

		filter.BanTime = banTime;
		filter.BanEndTime = (banTime != 0.0f) ? (float)(Host.RealTime + 60.0 * banTime) : 0.0f;
		IPFilters[i] = filter;

		if (kick) {
			for (i = 0; i < sv.GetClientCount(); i++) {
				client = (BaseClient?)sv.GetClient(i);

				if (client == null || !client.IsActive() || !client.IsConnected() || !client.IsSpawned())
					continue;

				if (client.IsFakeClient())
					continue;

				if (!ShouldDiscard(client.GetNetChannel()!.GetRemoteAddress()!))
					continue;

				found = true;
				break;
			}
		}

		string duration = BanDuration(banTime);

		IGameEvent? ev = g_GameEventManager.CreateEvent("server_addban");
		if (ev != null) {
			if (found && client != null) {
				ev.SetString("name", client.GetClientName());
				ev.SetInt("userid", client.GetUserID());
				ev.SetString("networkid", client.GetNetworkIDString());
			}
			else {
				ev.SetString("name", "");
				ev.SetInt("userid", 0);
				ev.SetString("networkid", "");
			}

			ev.SetString("ip", args[2]);
			ev.SetString("duration", duration);
			ev.SetString("by", CommandIssuer(source, clientSlot));
			ev.SetBool("kicked", kick && found && client != null);

			g_GameEventManager.FireEvent(ev);
		}

		if (kick && found && client != null) {
			client.ClientPrintf("The server operator has added you to the banned list.\n");
			client.Disconnect("Added to banned list");
		}
	}

	[ConCommand("banip", "Add an IP address to the ban list.")]
	void banip(in TokenizedCommand args, CommandSource source, int clientSlot) => addip(in args, source, clientSlot);

	[ConCommand("removeip", "Remove an IP address from the ban list.")]
	void removeip(in TokenizedCommand args, CommandSource source, int clientSlot) {
		if (args.ArgC() < 1) {
			ConMsg("Usage:  removeip < slot | ipaddress >\n");
			return;
		}

		if (!args[1].Contains('.')) {
			int slot = atoi(args[1]);
			if (slot > 0 && slot <= IPFilters.Count) {
				slot--;

				Span<byte> b = stackalloc byte[4];
				MemoryMarshal.Write(b, IPFilters[slot].Compare);
				string ip = $"{b[0],3}.{b[1],3}.{b[2],3}.{b[3],3}";

				IPFilters.RemoveAt(slot);

				ConMsg($"removeip:  filter removed for {args[1]}, IP {ip}\n");

				IGameEvent? slotEvent = g_GameEventManager.CreateEvent("server_removeban");
				if (slotEvent != null) {
					slotEvent.SetString("networkid", "");
					slotEvent.SetString("ip", ip);
					slotEvent.SetString("by", CommandIssuer(source, clientSlot));
					g_GameEventManager.FireEvent(slotEvent);
				}

				return;
			}

			ConMsg($"removeip:  invalid slot {slot}\n");
			return;
		}

		if (!ConvertString(args[1], out IPFilter f))
			return;

		for (int i = 0; i < IPFilters.Count; i++) {
			if ((IPFilters[i].Mask == f.Mask) && (IPFilters[i].Compare == f.Compare)) {
				IPFilters.RemoveAt(i);
				ConMsg($"removeip:  filter removed for {args[1]}\n");

				IGameEvent? ev = g_GameEventManager.CreateEvent("server_removeban");
				if (ev != null) {
					ev.SetString("networkid", "");
					ev.SetString("ip", args[1]);
					ev.SetString("by", CommandIssuer(source, clientSlot));
					g_GameEventManager.FireEvent(ev);
				}

				return;
			}
		}

		ConMsg($"removeip:  couldn't find {args[1]}\n");
	}

	[ConCommand("listip", "List IP addresses on the ban list.")]
	void listip() {
		int count = IPFilters.Count;
		if (count == 0) {
			ConMsg("IP filter list: empty\n");
			return;
		}

		if (count == 1)
			ConMsg($"IP filter list: {count} entry\n");
		else
			ConMsg($"IP filter list: {count} entries\n");

		Span<byte> b = stackalloc byte[4];
		for (int i = 0; i < count; i++) {
			MemoryMarshal.Write(b, IPFilters[i].Compare);

			if (IPFilters[i].BanTime != 0.0f)
				ConMsg($"{i + 1} {b[0],3}.{b[1],3}.{b[2],3}.{b[3],3} : {IPFilters[i].BanTime.ToString("F3", CultureInfo.InvariantCulture)} min\n");
			else
				ConMsg($"{i + 1} {b[0],3}.{b[1],3}.{b[2],3}.{b[3],3} : permanent\n");
		}
	}

	[ConCommand("writeip", "Save the ban list to " + BANNED_IP_FILENAME + ".")]
	void writeip() {
		const string name = CONFIG_DIR + BANNED_IP_FILENAME;

		ConMsg($"Writing {name}.\n");

		using IFileHandle? f = g_pFileSystem.Open(name, FileOpenOptions.Write | FileOpenOptions.Binary, null);
		if (f == null) {
			ConMsg($"Couldn't open {name}\n");
			return;
		}

		Span<byte> b = stackalloc byte[4];
		for (int i = 0; i < IPFilters.Count; i++) {
			MemoryMarshal.Write(b, IPFilters[i].Compare);

			if (IPFilters[i].BanTime != 0.0f)
				continue;

			f.Stream.Write(Encoding.ASCII.GetBytes($"addip 0 {b[0]}.{b[1]}.{b[2]}.{b[3]}\r\n"));
		}
	}

	public bool IsUserBanned(USERID userid) {
		if (sv_filterban.GetInt() == 0)
			return false;

		bool negativeFilter = sv_filterban.GetInt() == 1;

		for (int i = UserFilters.Count - 1; i >= 0; i--) {
			if ((UserFilters[i].BanEndTime != 0.0f) &&
				 (UserFilters[i].BanEndTime <= Host.RealTime)) {
				UserFilters.RemoveAt(i);
				continue;
			}

			if (Steam3Server().CompareUserID(userid, UserFilters[i].UserID))
				return negativeFilter;
		}

		return !negativeFilter;
	}

	static USERID StringToUserID(ReadOnlySpan<char> str) {
		USERID id = default;

		if (!str.IsEmpty) {
			ReadOnlySpan<char> temp = default;
			if (str.StartsWith(STEAM_PREFIX, StringComparison.OrdinalIgnoreCase)) {
				temp = str[STEAM_PREFIX.Length..];
				if (temp.Length > 126)
					temp = temp[..126];
				id.IDType = IDType.Steam;
			}

			TokenizedCommand args = new();
			args.Tokenize(temp);
			if (args.ArgC() >= 5) {
				uint high = (uint)atoi(args[2]);
				uint low = (uint)atoi(args[4]);
				id.SteamID = new CSteamID(new AccountID_t(low * 2 + high), EUniverse.k_EUniversePublic, EAccountType.k_EAccountTypeIndividual);
			}
		}

		return id;
	}

	[ConCommand("writeid", "Writes a list of permanently-banned user IDs to " + BANNED_USER_FILENAME + ".")]
	void writeid() {
		const string name = CONFIG_DIR + BANNED_USER_FILENAME;

		ConMsg($"Writing {name}.\n");

		using IFileHandle? f = g_pFileSystem.Open(name, FileOpenOptions.Write | FileOpenOptions.Binary, null);
		if (f == null) {
			ConMsg($"Couldn't open {name}\n");
			return;
		}

		for (int i = 0; i < UserFilters.Count; i++) {
			if (UserFilters[i].BanTime != 0.0f)
				continue;

			f.Stream.Write(Encoding.ASCII.GetBytes($"banid 0 {BaseClient.GetUserIDString(UserFilters[i].UserID)}\r\n"));
		}
	}

	[ConCommand("removeid", "Remove a user ID from the ban list.")]
	void removeid(in TokenizedCommand args, CommandSource source, int clientSlot) {
		if (args.ArgC() != 2 && args.ArgC() != 6) {
			ConMsg("Usage:  removeid < slot | uniqueid >\n");
			return;
		}

		ReadOnlySpan<char> arg1 = args[1];

		if (arg1.StartsWith('#')) {
			ConMsg("Usage:  removeid < userid | uniqueid >\n");
			ConMsg("No # necessary\n");
			return;
		}

		if (arg1.IsEmpty || arg1[0] < '0' || arg1[0] > '9') {
			string searchString;
			if (arg1.StartsWith(STEAM_PREFIX, StringComparison.OrdinalIgnoreCase) && args[2].Contains(':'))
				searchString = $"{arg1}:{args[3]}:{args[5]}";
			else if (arg1.StartsWith(STEAM_PREFIX, StringComparison.OrdinalIgnoreCase) && arg1.Contains(':'))
				searchString = new(arg1);
			else {
				ConMsg($"removeid:  invalid ban ID \"{arg1}\"\n");
				return;
			}

			if (searchString.Length > 63)
				searchString = searchString[..63];

			for (int i = 0; i < UserFilters.Count; i++) {
				if (stricmp(BaseClient.GetUserIDString(UserFilters[i].UserID), searchString) != 0)
					continue;

				UserFilters.RemoveAt(i);
				ConMsg($"removeid:  filter removed for {searchString}\n");

				IGameEvent? ev = g_GameEventManager.CreateEvent("server_removeban");
				if (ev != null) {
					ev.SetString("networkid", searchString);
					ev.SetString("ip", "");
					ev.SetString("by", CommandIssuer(source, clientSlot));
					g_GameEventManager.FireEvent(ev);
				}

				return;
			}

			ConMsg($"removeid:  couldn't find {searchString}\n");
			return;
		}

		int slot = atoi(arg1);
		if (slot <= 0 || slot > UserFilters.Count) {
			ConMsg($"removeid:  invalid slot {slot}\n");
			return;
		}

		USERID id = UserFilters[--slot].UserID;
		UserFilters.RemoveAt(slot);

		string idString = new(BaseClient.GetUserIDString(id));
		ConMsg($"removeid:  filter removed for {arg1}, ID {idString}\n");

		IGameEvent? slotEvent = g_GameEventManager.CreateEvent("server_removeban");
		if (slotEvent != null) {
			slotEvent.SetString("networkid", idString);
			slotEvent.SetString("ip", "");
			slotEvent.SetString("by", CommandIssuer(source, clientSlot));
			g_GameEventManager.FireEvent(slotEvent);
		}
	}

	[ConCommand("listid", "Lists banned users.")]
	void listid() {
		int count = UserFilters.Count;
		if (count == 0) {
			ConMsg("ID filter list: empty\n");
			return;
		}

		if (count == 1)
			ConMsg($"ID filter list: {count} entry\n");
		else
			ConMsg($"ID filter list: {count} entries\n");

		for (int i = 0; i < count; i++) {
			string uid = new(BaseClient.GetUserIDString(UserFilters[i].UserID));

			if (UserFilters[i].BanTime != 0.0f)
				ConMsg($"{i + 1} {uid} : {UserFilters[i].BanTime.ToString("F3", CultureInfo.InvariantCulture)} min\n");
			else
				ConMsg($"{i + 1} {uid} : permanent\n");
		}
	}

	[ConCommand("banid", "Add a user ID to the ban list.")]
	void banid(in TokenizedCommand args, CommandSource source, int clientSlot) {
		if (Steam3Server().BLanOnly()) {
			ConMsg("Can't ban users on a LAN\n");
			return;
		}

		if (args.ArgC() < 3 || args.ArgC() > 8) {
			ConMsg("Usage:  banid < minutes > < userid | uniqueid > { kick }\n");
			ConMsg("Use 0 minutes for permanent\n");
			return;
		}

		float banTime = (float)atof(args[1]);
		if (banTime < 0.01f)
			banTime = 0.0f;

		ReadOnlySpan<char> arg2 = args[2];
		if (arg2.StartsWith('#')) {
			ConMsg("Usage:  banid < minutes > < userid | uniqueid > { kick }\n");
			ConMsg("No # necessary\n");
			return;
		}

		bool kick = args.ArgC() >= 4 && stricmp(args[args.ArgC() - 1], "kick") == 0;

		string searchString = "";
		int searchIndex;
		if (arg2.IsEmpty || arg2[0] < '0' || arg2[0] > '9') {
			searchIndex = -1;

			if (arg2.StartsWith(STEAM_PREFIX, StringComparison.OrdinalIgnoreCase) && args[3].Contains(':')) {
				searchString = $"{arg2}:{args[4]}:{args[6]}";
				if (searchString.Length > 63)
					searchString = searchString[..63];
			}
			else {
				ConMsg($"Can't ban users with ID \"{arg2}\"\n");
				return;
			}
		}
		else
			searchIndex = atoi(arg2);

		bool playing = false;
		USERID id = default;
		BaseClient? client = null;
		for (int c = 0; c < sv.GetClientCount(); c++) {
			client = (BaseClient?)sv.GetClient(c);
			if (client == null || !client.IsActive() || !client.IsConnected() || !client.IsSpawned())
				continue;

			if (client.IsFakeClient())
				continue;

			if (searchIndex != -1) {
				if (client.GetUserID() == searchIndex) {
					id = client.GetNetworkID();
					playing = true;
					break;
				}
			}
			else {
				if (stricmp(client.GetNetworkIDString(), searchString) == 0) {
					id = client.GetNetworkID();
					playing = true;
					break;
				}
			}
		}

		if (!playing) {
			if (searchIndex != -1) {
				ConMsg($"banid:  couldn't find userid {searchIndex}\n");
				return;
			}

			id = StringToUserID(searchString);
		}

		int i;
		for (i = 0; i < UserFilters.Count; i++) {
			if (Steam3Server().CompareUserID(UserFilters[i].UserID, id))
				break;
		}

		if (!sv.IsDedicated() && playing && client!.GetPlayerSlot() == 0) {
			Msg("Preventing ban of local server host!\n");
			return;
		}

		if (i >= UserFilters.Count) {
			if (UserFilters.Count >= MAX_USERFILTERS) {
				ConMsg("banid:  user filter list is full\n");
				return;
			}

			i = UserFilters.Count;
			UserFilters.Add(default);
		}

		UserFilters[i] = new() {
			BanTime = banTime,
			BanEndTime = (banTime != 0.0f) ? (float)(Host.RealTime + 60.0 * banTime) : 0.0f,
			UserID = id
		};

		string duration = BanDuration(banTime);

		IGameEvent? ev = g_GameEventManager.CreateEvent("server_addban");
		if (ev != null) {
			if (playing) {
				ev.SetString("name", client!.GetClientName());
				ev.SetInt("userid", client.GetUserID());
				ev.SetString("networkid", client.GetNetworkIDString());
			}
			else {
				ev.SetString("name", "");
				ev.SetInt("userid", 0);
				ev.SetString("networkid", BaseClient.GetUserIDString(id));
			}

			ev.SetString("ip", "");
			ev.SetString("duration", duration);
			ev.SetString("by", CommandIssuer(source, clientSlot));
			ev.SetInt("kicked", (kick && playing && client != null) ? 1 : 0);

			g_GameEventManager.FireEvent(ev);
		}

		if (kick && playing && client != null) {
			client.ClientPrintf($"You have been kicked and banned {duration} by the server.\n");
			client.Disconnect("Kicked and banned");
		}
	}
}
