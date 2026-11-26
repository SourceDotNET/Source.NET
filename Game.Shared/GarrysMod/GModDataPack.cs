#if CLIENT_DLL || GAME_DLL
using Source;
using Source.Common.Engine;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;
using Source.Common.Hashing;
using Source.Common;
using Source.Common.Filesystem;
using Source.Common.Bitbuffers;

using System.Security.Cryptography;
using System.Text;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public class GModDataPack : IGModDataPack
{
	public static readonly GModDataPack pDataPack = new();
	public static GModDataPack DataPack() => pDataPack;

	INetworkStringTable? Table;
	bool OverflowWarned;
	readonly SortedSet<string> SingleplayerFiles = new(StringComparer.Ordinal);

#if CLIENT_DLL
	string ClientSearchPaths = "";

	static int FilesRequesting_Total;
	static int FilesRequesting_Recv;

	public void Initialize() {
		Table = networkstringtable.FindTable("client_lua_files");
		get.LuaShared()!.SetLuaFindHook(this);
		OverflowWarned = false;
	}

	int FindInTable(ReadOnlySpan<char> name) {
		Span<char> fixedName = stackalloc char[0x104];
		strcpy(fixedName, name);
		for (int i = 0; i < fixedName.Length && fixedName[i] != '\0'; i++) {
			if (fixedName[i] == '\\')
				fixedName[i] = '/';
		}

		string path = new(fixedName.SliceNullTerminatedString());
		int index = Table!.FindStringIndex(path);
		if (index != INetworkStringTable.INVALID_STRING_INDEX)
			return index;

		index = Table.FindStringIndex("lua\\" + path);
		if (index != INetworkStringTable.INVALID_STRING_INDEX)
			return index;

		return Table.FindStringIndex("gamemodes\\" + path);
	}

	ReadOnlySpan<char> GetClientSearchPaths() {
		byte[]? data = Table!.GetStringUserData(0);
		ClientSearchPaths = data == null ? "" : Encoding.UTF8.GetString(data);
		return ClientSearchPaths;
	}

	byte[] GetHashFromDatatable(int index) {
		if (index == INetworkStringTable.INVALID_STRING_INDEX)
			return [];

		byte[]? data = Table!.GetStringUserData(index);
		if (data == null || data.Length < 0x20)
			return [];

		return data[..0x20];
	}

	public void RequestFiles() {
		FilesRequesting_Total = 0;
		FilesRequesting_Recv = 0;
		if (IsSingleplayer())
			return;

		if (Table == null) {
			Warning("RequestFiles with no table!\n");
			return;
		}

		List<byte> buffer = new(0x20) { (byte)GModMessageType.LuaFile };
		int requesting = 0;
		for (int i = 1; i < Table.GetNumStrings(); i++) {
			byte[] hash = GetHashFromDatatable(i);
			if (ReadCache(hash, out byte[]? cached)) {
				SetFileContents(i, cached!, false);
				continue;
			}

			string name = new(Table.GetString(i));
			if (LoadFromDisk(i, name, "MOD", hash))
				continue;
			if (LoadFromDisk(i, name, "workshop", hash))
				continue;

			buffer.Add((byte)i);
			buffer.Add((byte)(i >> 8));
			requesting++;
		}
		buffer.Add(0);
		buffer.Add(0);

		engine.GMOD_SendToServer(buffer.ToArray(), buffer.Count << 3, true);

		if (requesting < 1)
			Msg("Lua cache is up to date\n");
		else {
			FilesRequesting_Total = requesting;
			Msg($"Requesting {requesting} Lua files from the server...\n");
			enginevgui.UpdateCustomProgressBar(0.9f, $"Requesting {requesting} Lua files from the server...");
		}
	}

	bool LoadFromDisk(int index, string fileName, ReadOnlySpan<char> pathID, byte[] hash) {
		if (fileName.Contains("..", StringComparison.Ordinal))
			return false;

		using IFileHandle? handle = filesystem.Open(fileName, FileOpenOptions.Read | FileOpenOptions.Binary, pathID);
		if (handle == null)
			return false;

		int size = (int)handle.Stream.Length;
		byte[] contents = new byte[size + 1];
		handle.Stream.ReadExactly(contents, 0, size);
		contents[size] = 0;

		if (!GetHashFromString(contents).AsSpan().SequenceEqual(hash))
			return false;

		using MemoryStream output = new();
		output.Write(hash, 0, 0x20);
		bool compressed = Bootil.Compression.LZMA.Compress(contents, output, 5, 0x10000);
		if (compressed)
			SetFileContents(index, output.ToArray(), false);

		return compressed;
	}

	static string GetCachePath(ReadOnlySpan<byte> hash) => "cache/lua/" + SHA256Value.FromBytes(hash).ToString()[..0x28] + ".lua";

	static bool ReadCache(byte[] hash, out byte[]? data) {
		data = null;
		if (hash.Length != 0x20)
			return false;

		string path = GetCachePath(hash);
		if (!filesystem.FileExists(path, "DEFAULT_WRITE_PATH"))
			return false;

		using IFileHandle? handle = filesystem.Open(path, FileOpenOptions.Read | FileOpenOptions.Binary, "DEFAULT_WRITE_PATH");
		if (handle == null)
			return false;

		using MemoryStream file = new();
		handle.Stream.CopyTo(file);
		byte[] contents = file.ToArray();
		if (contents.Length >= 0x20 && contents.AsSpan(0, 0x20).SequenceEqual(hash)) {
			data = contents;
			return true;
		}

		Msg("SHA256 in cache file doesn't match!!\n");
		return false;
	}

	static void DeleteCache(ReadOnlySpan<byte> hash) {
		string path = GetCachePath(hash);
		if (filesystem.FileExists(path, "DEFAULT_WRITE_PATH"))
			filesystem.RemoveFile(path, "DEFAULT_WRITE_PATH");
	}

	static bool WriteCache(ReadOnlySpan<byte> hash, ReadOnlySpan<byte> data) {
		filesystem.CreateDirHierarchy("cache/lua", "DEFAULT_WRITE_PATH");
		string path = GetCachePath(hash);
		if (filesystem.FileExists(path, "DEFAULT_WRITE_PATH"))
			return false;

		using IFileHandle? handle = filesystem.Open(path, FileOpenOptions.Write | FileOpenOptions.Binary, "DEFAULT_WRITE_PATH");
		handle?.Stream.Write(data);
		return true;
	}

	public void SetFileContents(int index, ReadOnlySpan<byte> data, bool save) {
		if (Table == null) {
			DevWarning("SetFileContents with no table!\n");
			return;
		}

		if (Table.GetNumStrings() <= index) {
			DevWarning($"Client requesting crazy file update number ({index})\n");
			return;
		}

		Table.SetStringUserData(index, data.Length, data);
		if (save) {
			if (data.Length < 0x21)
				Warning($"Downlaoded .lua file '{Table.GetString(index)}', but it has no content?\n");
			else
				WriteCache(data[..0x20], data);
		}

		get.LuaShared()!.InvalidateCache("!" + new string(Table.GetString(index)));

		if (FilesRequesting_Total != 0) {
			FilesRequesting_Recv++;
			enginevgui.UpdateCustomProgressBar(0.91f, $"Downloaded {FilesRequesting_Recv} of {FilesRequesting_Total} Lua files");
		}

		if (FilesRequesting_Total <= FilesRequesting_Recv && FilesRequesting_Total != 0) {
			FilesRequesting_Recv = 0;
			FilesRequesting_Total = 0;
			enginevgui.UpdateCustomProgressBar(0.91f, "Received all Lua files we needed!");
		}
	}
#else
	readonly int[] RequestCount = new int[0x80];

	public void Initialize() {
		Table = networkstringtable.CreateStringTable("client_lua_files", IsSingleplayer() ? 0x8000 : 0x2000, 0, 0);
		Table.AddString(true, "paths");
	}

	public void OnClientConnected(int client) => RequestCount[client] = 0;

	public void SendFileRequestRequest(int client) {
		RequestCount[client]++;
		ReadOnlySpan<byte> data = [(byte)GModMessageType.RequestLuaFiles];
		engine.GMOD_SendToClient(client, data, data.Length << 3);
	}

	public void OnFilesRequested(int client, bf_read msg, int bits) {
		if (RequestCount[client] <= 0)
			return;

		RequestCount[client]--;
		if ((bits & 0xF) != 0)
			return;

		int count = bits / 16;
		if (count > Table!.GetNumStrings())
			return;

		List<int> requested = [];
		HashSet<int> seen = [];
		for (int i = 0; i < count; i++) {
			int index = (int)msg.ReadUBitLong(16);
			if (index == 0 || index >= Table.GetNumStrings())
				continue;
			if (seen.Add(index))
				requested.Add(index);
		}

		foreach (int index in requested)
			SendFileToClient(client, index);
	}

	public void SendFileToClient(int client, int index) {
		if (index < 0 || index >= Table!.GetNumStrings()) {
			DevWarning($"Client requesting crazy file number ({index})\n");
			return;
		}

		string name = new(Table.GetString(index));
		LuaFile? file = get.LuaShared()!.GetCache(name);
		if (file == null) {
			DevWarning($"Client requested file but doesn't exist! {name}\n");
			return;
		}

		if (file.Compressed.Length == 0) {
			byte[] contents = new byte[file.Contents.Length + 1];
			file.Contents.CopyTo(contents, 0);

			using MemoryStream compressed = new();
			compressed.Write(GetHashFromString(contents), 0, 0x20);
			bool success = Bootil.Compression.LZMA.Compress(contents, compressed, 5, 0x10000);
			file.Compressed = compressed.ToArray();
			if (!success) {
				DevWarning($"Couldn't compress file '{name}' before sending to client\n");
				return;
			}
		}

		if (file.Compressed.Length > 0x10000) {
			Warning($"Refusing to send file '{name}', its too large! ({Bootil.String.Format.Memory((ulong)file.Compressed.Length)}, 64KB max)\n");
			return;
		}

		byte[] data = new byte[3 + file.Compressed.Length];
		data[0] = (byte)GModMessageType.LuaFile;
		data[1] = (byte)index;
		data[2] = (byte)(index >> 8);
		file.Compressed.CopyTo(data, 3);
		engine.GMOD_SendToClient(client, data, data.Length << 3);
	}
#endif

	public void BuildSearchPaths() {
		string gameDir = new(get.GameDir());
		Bootil.String.File.FixSlashes(ref gameDir, "\\", "/");
		Bootil.String.Lower(ref gameDir);

		char[] buffer = new char[filesystem.GetSearchPath("lsv", false, default)];
		filesystem.GetSearchPath("lsv", false, buffer);
		string paths = new(((ReadOnlySpan<char>)buffer).SliceNullTerminatedString());
		Bootil.String.File.FixSlashes(ref paths, "\\", "/");
		Bootil.String.Lower(ref paths);
		Bootil.String.Util.FindAndReplace(ref paths, gameDir, "");

		byte[] userData = Encoding.UTF8.GetBytes(paths + '\0');
		Table!.SetStringUserData(0, userData.Length, userData);
	}

	public void Reset() {
		Table = null;
		SingleplayerFiles.Clear();
	}

	public bool Contains(ReadOnlySpan<char> name) {
		Span<char> fixedName = stackalloc char[0x104];
		strcpy(fixedName, name);
		for (int i = 0; i < fixedName.Length && fixedName[i] != '\0'; i++) {
			if (fixedName[i] == '\\')
				fixedName[i] = '/';
		}

		return Table!.FindStringIndex(fixedName.SliceNullTerminatedString()) != INetworkStringTable.INVALID_STRING_INDEX;
	}

	public void AddOrUpdateFile(LuaFile file, bool refresh) {
		if (IsSingleplayer()) {
			string fileName = new(((ReadOnlySpan<char>)file.Name).UnqualifiedFileName());
			if (!SingleplayerFiles.Add(fileName))
				return;

			StringBuilder builder = new(":");
			foreach (string name in SingleplayerFiles)
				builder.Append(name).Append(':');
			string files = builder.ToString();

			for (int i = 0, offset = 0; i < 0x400; i++, offset += 0x19000) {
				string key = $"singleplayer_files{i}";
				int index = Table!.FindStringIndex(key);
				if (index == INetworkStringTable.INVALID_STRING_INDEX)
					index = Table.AddString(true, key);
				if (index == INetworkStringTable.INVALID_STRING_INDEX) {
					Warning($"Couldn't add network file ({file.Name}) - overflow?\n");
					return;
				}

				string chunk = files.Substring(offset, Math.Min(files.Length - offset, 0x19000));
				Bootil.String.Lower(ref chunk);
				byte[] userData = Encoding.UTF8.GetBytes(chunk + '\0');
				Table.SetStringUserData(index, userData.Length, userData);

				if (offset + 0x19000 > files.Length)
					return;
			}
			return;
		}

		int stringIndex = Table!.FindStringIndex(file.Name);
		if (stringIndex == INetworkStringTable.INVALID_STRING_INDEX) {
			stringIndex = Table.AddString(true, file.Name);
			if (stringIndex == INetworkStringTable.INVALID_STRING_INDEX) {
				if (!OverflowWarned) {
					OverflowWarned = true;
					g_Lua!.ErrorFromLua("Too many clientside Lua files (AddCSLuaFile), you have hit the limit!\n");
				}
				Warning($"Couldn't add network string [{file.Name}] - overflow?\n");
				return;
			}
		}

		byte[] contents = new byte[file.Contents.Length + 1];
		file.Contents.CopyTo(contents, 0);

		List<byte> buffer = new(0x20);
		buffer.AddRange(GetHashFromString(contents));
		if (refresh) {
			// todo: file.Compressed.Clear(); Bootil::Compression::LZMA::Compress(contents, len + 1, buffer, 5, 0x10000), "GModDataPack::AddOrUpdateFile: Couldn't compress file\n", "AUTOREFRESH: Not adding %s to datatable, its too large! (%i vs 65536)\n"
		}

		Table.SetStringUserData(stringIndex, buffer.Count, buffer.ToArray());
	}

	public byte[] GetHashFromString(ReadOnlySpan<byte> data) => SHA256.HashData(data);

	public bool IsSingleplayer() => gpGlobals.MaxClients == 1;

#if CLIENT_DLL
	public string? GetFromDatatable(ReadOnlySpan<char> name) {
		if (Table == null)
			return null;

		int index = FindInTable(name);
		if (index == INetworkStringTable.INVALID_STRING_INDEX)
			return null;

		byte[]? data = Table.GetStringUserData(index);
		if (data == null)
			return null;

		if (data.Length < 0x20) {
			Warning($"This should never happen - datapack file entry has no hash! ({name})\n");
			return null;
		}

		if (data.Length == 0x20) {
			if (engine.IsPlayingDemo())
				return null;

			Warning($"This should never happen - datapack file entry has no data! ({name})\n");
			return null;
		}

		ReadOnlySpan<byte> hash = data.AsSpan(0, 0x20);
		using MemoryStream output = new();
		if (!Bootil.Compression.LZMA.Extract(data.AsSpan(0x20), output)) {
			Warning($"Couldn't extract from datatable ({name}) ({data.Length})\n");
			return null;
		}

		byte[] contents = output.ToArray();
		byte[] computed = GetHashFromString(contents);
		if (hash.SequenceEqual(computed)) {
			int length = Array.IndexOf(contents, (byte)0);
			return Encoding.UTF8.GetString(contents, 0, length < 0 ? contents.Length : length);
		}

		DeleteCache(computed);
		DeleteCache(hash);
		Warning($"Hash mismatch for file {name}! ({SHA256Value.FromBytes(computed)} {SHA256Value.FromBytes(hash)})\n");
		return null;
	}

	public byte[] GetHashFromDatatable(ReadOnlySpan<char> name) {
		if (Table == null)
			return [];

		return GetHashFromDatatable(FindInTable(name));
	}

	public string? FindFileInDatatable(ReadOnlySpan<char> path, bool unk, bool isGamePath) {
		if (Table == null)
			return null;

		string fullPath = new(path);
		Bootil.String.Lower(ref fullPath);
		Bootil.String.Util.FindAndReplace(ref fullPath, "\\", "/");

		string paths = new(GetClientSearchPaths().SliceNullTerminatedString());
		Bootil.String.Lower(ref paths);

		if (isGamePath)
			paths = "/";

		foreach (string tok in paths.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
			string searchPath = tok;
			Bootil.String.Util.FindAndReplace(ref searchPath, "\\", "/");
			Bootil.String.Util.TrimLeft(ref searchPath, "/\\");

			string check = searchPath + fullPath;
			for (int i = 0; i < Table.GetNumStrings(); i++) {
				string name = new(Table.GetString(i));
				if (name != check)
					continue;

				if (!unk)
					Bootil.String.File.ExtractFilename(ref name);
				return name;
			}
		}

		return null;
	}

	public bool IsLocalLuaBlocked() {
		if (IsSingleplayer())
			return false;

		return cvar.FindVar("sv_cheats")!.GetInt() == 0 && GarrysMod.sv_allowcslua.GetInt() == 0;
	}

	static void CleanUpPath(ref string path) {
		Bootil.String.Util.TrimLeft(ref path, "/\\");
		if (Bootil.String.Test.StartsWith(path, "addons/")) {
			int pos = path[7..].IndexOf('/');
			if (pos != -1)
				path = path[(pos + 8)..];
		}

		if (Bootil.String.Test.StartsWith(path, "workshop/"))
			path = path[9..];

		if (Bootil.String.Test.StartsWith(path, "lua/"))
			path = path[4..];

		if (Bootil.String.Test.StartsWith(path, "gamemodes/"))
			path = path[10..];
	}

	public bool IsValidDirectory(ReadOnlySpan<char> name) {
		string dir = new(name);
		string paths = new(GetClientSearchPaths().SliceNullTerminatedString());
		Bootil.String.Lower(ref paths);

		foreach (string tok in paths.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
			string path = tok;
			Bootil.String.File.FixSlashes(ref path, "\\", "/");
			CleanUpPath(ref path);
			if (Bootil.String.Test.StartsWith(path, dir))
				return true;
		}

		for (int i = 0; i < Table!.GetNumStrings(); i++) {
			string path = new(Table.GetString(i));
			Bootil.String.File.StripFilename(ref path);
			CleanUpPath(ref path);
			if (Bootil.String.Test.StartsWith(path, dir))
				return true;
		}

		return false;
	}

	static bool ContainsResult(List<LuaFindResult> output, string name) {
		foreach (LuaFindResult result in output) {
			if (result.FileName == name)
				return true;
		}
		return false;
	}

	public void FindInDatatable(ReadOnlySpan<char> wildcard, List<LuaFindResult> output, bool fullPath) {
		if (Table == null)
			return;

		if (IsSingleplayer()) {
			string list = "";
			int count = 0;
			for (; count < 0x400; count++) {
				int index = Table.FindStringIndex($"singleplayer_files{count}");
				if (index == INetworkStringTable.INVALID_STRING_INDEX)
					break;

				byte[]? data = Table.GetStringUserData(index);
				list += data == null ? "" : Encoding.UTF8.GetString(data);
			}

			if (count == 0) {
				Warning("Couldn't find singleplayer file list?\n");
				return;
			}

			HashSet<string> seen = [];
			ReadOnlySpan<char> found = filesystem.FindFirstEx(wildcard, "lcl", out FileFindHandle_t handle);
			while (!found.IsEmpty) {
				string fileName = new(found);
				string key = ":" + fileName + ":";
				Bootil.String.Lower(ref key);
				if (fileName[0] != '.' && !seen.Contains(fileName) && (list.Contains(key, StringComparison.Ordinal) || filesystem.FindIsDirectory(handle))) {
					output.Add(new() { FileName = fileName, IsFolder = filesystem.FindIsDirectory(handle) });
					seen.Add(fileName);
				}
				found = filesystem.FindNext(handle);
			}
			filesystem.FindClose(handle);
			return;
		}

		string search = new(wildcard);
		Bootil.String.Lower(ref search);
		Bootil.String.File.FixSlashes(ref search, "\\", "/");

		string paths = new(GetClientSearchPaths().SliceNullTerminatedString());
		Bootil.String.Lower(ref paths);

		string dir = search;
		Bootil.String.File.StripFilename(ref dir);
		string pattern = search[dir.Length..];

		foreach (string tok in paths.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
			string path = tok;
			Bootil.String.File.FixSlashes(ref path, "\\", "/");
			Bootil.String.Util.TrimLeft(ref path, "/\\");

			string prefix = path + dir;
			string full = path + search;
			for (int i = 0; i < Table.GetNumStrings(); i++) {
				string entry = new(Table.GetString(i));
				if (entry.Length < search.Length || !Bootil.String.Test.Wildcard(full, entry) || !Bootil.String.Test.StartsWith(entry, prefix))
					continue;

				entry = entry[prefix.Length..];
				int pos = entry.IndexOf('/');
				if (pos == -1)
					pos = entry.IndexOf('\\');

				if (pos != -1) {
					entry = entry[..pos];
					if (!Bootil.String.Test.Wildcard(pattern, entry) || ContainsResult(output, entry))
						continue;

					output.Add(new() { FileName = entry, IsFolder = true });
					continue;
				}

				if (!Bootil.String.Test.Wildcard(pattern, entry) || ContainsResult(output, entry))
					continue;

				if (fullPath) {
					entry = new(Table.GetString(i));
					Bootil.String.File.FixSlashes(ref entry, "\\", "/");
				}

				output.Add(new() { FileName = entry, IsFolder = false });
			}
		}
	}
#else
	public string? GetFromDatatable(ReadOnlySpan<char> name) => throw new NotImplementedException();
	public byte[] GetHashFromDatatable(ReadOnlySpan<char> name) => throw new NotImplementedException();
	public string? FindFileInDatatable(ReadOnlySpan<char> path, bool unk, bool isGamePath) => throw new NotImplementedException();
	public bool IsLocalLuaBlocked() => throw new NotImplementedException();
	public void FindInDatatable(ReadOnlySpan<char> wildcard, List<LuaFindResult> output, bool unk) => throw new NotImplementedException();
	public bool IsValidDirectory(ReadOnlySpan<char> name) => throw new NotImplementedException();
#endif
}
#endif
