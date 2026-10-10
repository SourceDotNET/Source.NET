using Source;
using Source.Common;
using Source.Common.Filesystem;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

using System;
using System.Collections.Generic;
using System.Text;

using static Source.StrTools;

namespace Game.Lua;

public class LuaSharedImpl : ILuaShared
{
	const int MaxPath = 260;
	const int StackTraceBufferSize = 4096;

	readonly ILuaInterface?[] LuaInterfaces = new ILuaInterface?[3];
	readonly List<LuaFile> Cache = [];
	ILuaClientDatatableHook? LuaFindHook;
	IGet? Get;
	bool Unk1;

	int FailedLoads;
	int SuccessfulLoads;
	int CacheHits;
	int DiskLoads;
	long LoadingTime;
	long CRCTime;
	long FilestampTime;

	public void Init(IServiceProvider services, bool unk1, IGet get) {
		Get = get;
		Unk1 = unk1;
		Cache.Clear();
	}

	public void Shutdown() => EmptyCache();

	public void DumpStats() {
		Msg("Lua File Stats ------\n");
		Msg($"Files In Cache: {Cache.Count}\n");
		Msg($"m_iCacheHits: {CacheHits}\n");
		Msg($"m_iFailedLoads: {FailedLoads}\n");
		Msg($"m_iSuccessfulLoads: {SuccessfulLoads}\n");
		Msg($"m_iDiskLoads: {DiskLoads}\n");
		Msg($"Time Spent Loading Files: {LoadingTime / (double)TimeSpan.TicksPerSecond:F6} seconds ({LoadingTime / (double)TimeSpan.TicksPerMillisecond:F6} milliseconds)\n");
		Msg($"Time Spent Working Out CRCs: {CRCTime / (double)TimeSpan.TicksPerSecond:F6} seconds ({CRCTime / (double)TimeSpan.TicksPerMillisecond:F6} milliseconds)\n");
		Msg($"Time Spent Checking Filestamps: {FilestampTime / (double)TimeSpan.TicksPerSecond:F6} seconds ({FilestampTime / (double)TimeSpan.TicksPerMillisecond:F6} milliseconds)\n");
	}

	public ILuaInterface CreateLuaInterface(Realm realm, bool renew) {
		if (!commandLine.CheckParm("-debuglua"))
			return (LuaInterfaces[(int)realm] = new LuaInterfaceImpl())!;

		// todo: CLuaInterface_Debug
		return (LuaInterfaces[(int)realm] = new LuaInterfaceImpl())!;
	}

	public void CloseLuaInterface(ILuaInterface iface) {
		iface.Shutdown();

		for (int i = 0; i < LuaInterfaces.Length; i++) {
			if (LuaInterfaces[i] == iface)
				LuaInterfaces[i] = null;
		}

		if (LuaInterfaces[(int)Realm.Client] == null && LuaInterfaces[(int)Realm.Server] == null)
			luaconvars.DestroyManaged();
	}

	public ILuaInterface? GetLuaInterface(byte realm) => LuaInterfaces[realm];

	public LuaFile? LoadFile(ReadOnlySpan<char> path, ReadOnlySpan<char> pathId, bool fromDatatable, bool fromFile) {
		if (!fromDatatable)
			return LoadFile_FromFile(path, pathId, fromDatatable, fromFile);

		LuaFile? file = LoadFile_FromDataTable(path, pathId, true);
		if (file != null)
			return file;

		if (LuaFindHook != null && !LuaFindHook.IsLocalLuaBlocked())
			return LoadFile_FromFile(path, pathId, fromDatatable, fromFile);

		return null;
	}

	LuaFile? LoadFile_FromFile(ReadOnlySpan<char> path, ReadOnlySpan<char> pathId, bool fromDatatable, bool fromFile) {
		DateTime fileTime = filesystem.GetFileTime(path, pathId);
		int time = fileTime <= DateTime.UnixEpoch ? 0 : (int)(fileTime.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;
		if (time == 0)
			return null;

		Span<char> fixedPath = stackalloc char[MaxPath];
		strcpy(fixedPath, path);
		FixSlashes(fixedPath, '/');
		if (!RemoveDotSlashes(fixedPath, '/'))
			return null;

		Span<char> fullPath = stackalloc char[MaxPath];
		if (filesystem.RelativePathToFullPath(fixedPath.SliceNullTerminatedString(), pathId, fullPath).IsEmpty)
			return null;

		Span<char> relativePath = stackalloc char[MaxPath];
		if (!filesystem.FullPathToRelativePathEx(fullPath, "MOD", relativePath))
			return null;

		FixSlashes(relativePath, '/');
		ReadOnlySpan<char> name = relativePath.SliceNullTerminatedString();
		if (name.StartsWith("workshop/", StringComparison.OrdinalIgnoreCase))
			name = name["workshop/".Length..];

		LuaFile? cache = GetCache(name);
		if (!fromFile)
			return cache;

		if (cache != null && time == cache.Time)
			return cache;

		byte[]? data = ReadFile(fixedPath.SliceNullTerminatedString(), pathId);
		if (data == null || data.Length == 0)
			return null;

		int nul = Array.IndexOf(data, (byte)0);
		byte[] contents = nul < 0 ? data : data[..nul];
		return CreateCache(name, contents, time);
	}

	static byte[]? ReadFile(ReadOnlySpan<char> path, ReadOnlySpan<char> pathId) {
		using IFileHandle? handle = filesystem.Open(path, FileOpenOptions.Read | FileOpenOptions.Binary, pathId);
		if (handle == null)
			return null;

		using MemoryStream buffer = new();
		handle.Stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	LuaFile? LoadFile_FromDataTable(ReadOnlySpan<char> path, ReadOnlySpan<char> pathId, bool unk) {
		if (LuaFindHook == null)
			return null;

		string? name = LuaFindHook.FindFileInDatatable(path, true, pathId.SequenceEqual("GAME"));
		if (name == null)
			return null;

		LuaFile? cache = GetCache("!" + name);
		if (cache != null && cache.Time != 0)
			return cache;

		string? contents = LuaFindHook.GetFromDatatable(name);
		if (contents == null)
			return null;

		if (contents.Length > 0 && contents[0] != '\0')
			return CreateCache("!" + name, Encoding.UTF8.GetBytes(contents), 1);

		LuaFile? file = LoadFile_FromFile(path, pathId, true, true);
		if (file == null) {
			Warning($"File in nosend - but we don't have it! ({path})\n");
			return null;
		}

		byte[] expected = LuaFindHook.GetHashFromDatatable(name);
		byte[] actual = LuaFindHook.GetHashFromString(file.Contents);
		if (!expected.AsSpan().SequenceEqual(actual)) {
			Warning($"hash mismatch: {name}\n");
			return null;
		}

		return file;
	}

	LuaFile CreateCache(ReadOnlySpan<char> name, byte[] contents, int time) {
		LuaFile? file = GetCache(name);
		if (file == null) {
			file = new LuaFile();
			Cache.Insert(0, file);
			string lowered = name.ToString();
			Bootil.String.Lower(ref lowered);
			file.Name = lowered;
			file.TimesLoadedServer = 0;
			file.TimesLoadedClient = 0;
		}

		file.Contents = contents;
		file.Time = time;
		file.Compressed = [];
		return file;
	}

	public LuaFile? GetCache(ReadOnlySpan<char> name) {
		string lowered = name.ToString();
		Bootil.String.Lower(ref lowered);

		foreach (LuaFile file in Cache) {
			if (file.Name == lowered)
				return file;
		}

		return null;
	}

	public void MountLua(ReadOnlySpan<char> pathId) {
		MountLuaAdd("lua", pathId);
		if (stricmp(pathId, "LuaMenu") != 0)
			MountLuaAdd("gamemodes", pathId);
	}

	public void MountLuaAdd(ReadOnlySpan<char> path, ReadOnlySpan<char> pathId) {
		AddSearchPath(path, pathId);
		filesystem.MarkPathIDByRequestOnly(pathId, true);
	}

	void AddSearchPath(ReadOnlySpan<char> path, ReadOnlySpan<char> pathId) {
		bool workshop = strnicmp(path, "workshop", 8) == 0;
		Span<char> fullPath = stackalloc char[MaxPath];
		ReadOnlySpan<char> full = filesystem.RelativePathToFullPath(path, "MOD", fullPath);
		if (!full.IsEmpty) {
			if (filesystem.IsDirectory(full, null))
				filesystem.AddSearchPath(full, pathId, SearchPathAdd.ToTail, PathGroupName.Lua, workshop);
			else
				Warning($"Tried to add search path, but path isn't path(!?) ({path})\n");
			return;
		}

		filesystem.AddSearchPath($"{Get!.GameDir()}{CORRECT_PATH_SEPARATOR}{path}", pathId, SearchPathAdd.ToTail, PathGroupName.Lua, workshop);
	}

	public void UnMountLua(ReadOnlySpan<char> pathId) => filesystem.RemoveSearchPaths(pathId);

	public void SetFileContents(ReadOnlySpan<char> unk1, ReadOnlySpan<char> unk2) { }

	public void SetLuaFindHook(ILuaClientDatatableHook? hook) => LuaFindHook = hook;

	public void FindScripts(ReadOnlySpan<char> wildcard, ReadOnlySpan<char> pathId, List<LuaFindResult> output) {
		if (pathId.SequenceEqual("lcl")) {
			if (LuaFindHook == null) {
				SortFindResults(output);
				return;
			}

			LuaFindHook.FindInDatatable(wildcard, output, false);
			if (output.Count != 0) {
				SortFindResults(output);
				return;
			}

			if (LuaFindHook == null || !LuaFindHook.IsSingleplayer()) {
				SortFindResults(output);
				return;
			}
		}

		HashSet<string> found = [];
		ReadOnlySpan<char> name = filesystem.FindFirstEx(wildcard, pathId, out ulong handle);
		while (!name.IsEmpty) {
			if (name[0] != '.') {
				string fileName = name.ToString();
				if (!found.Contains(fileName)) {
					output.Add(new LuaFindResult { FileName = fileName, IsFolder = filesystem.FindIsDirectory(handle) });
					found.Add(fileName);
				}
			}
			name = filesystem.FindNext(handle);
		}
		filesystem.FindClose(handle);

		SortFindResults(output);
	}

	static void SortFindResults(List<LuaFindResult> output)
		=> output.Sort((a, b) => CompareBytes(a.FileName, b.FileName));

	static int CompareBytes(string a, string b) {
		ReadOnlySpan<byte> left = Encoding.UTF8.GetBytes(a);
		ReadOnlySpan<byte> right = Encoding.UTF8.GetBytes(b);
		return left.SequenceCompareTo(right);
	}

	public ReadOnlySpan<char> GetStackTraces() {
		StringBuilder buffer = new();
		string[] names = ["  Client\n", "  Server\n", "  MenuSystem\n"];
		for (int i = 0; i < 3; i++) {
			buffer.Append(names[i]);
			ILuaInterface? iface = LuaInterfaces[i];
			if (iface != null)
				iface.AppendStackTrace(buffer);
			else
				buffer.Append("    Lua Interface = NULL\n\n");
		}

		string traces = buffer.ToString();
		return traces.Length < StackTraceBufferSize ? traces : traces[..(StackTraceBufferSize - 1)];
	}

	public void InvalidateCache(ReadOnlySpan<char> name) {
		string lowered = name.ToString();
		Bootil.String.Lower(ref lowered);

		foreach (LuaFile file in Cache) {
			if (file.Name == lowered)
				file.Time = 0;
		}
	}

	public void EmptyCache() => Cache.Clear();

	public bool ScriptExists(ReadOnlySpan<char> file, ReadOnlySpan<char> pathId, bool directoryOnly) {
		if (pathId.SequenceEqual("lcl")) {
			if (LuaFindHook == null)
				return false;

			if (!directoryOnly && LuaFindHook.FindFileInDatatable(file, false, false) != null)
				return true;

			if (LuaFindHook.IsValidDirectory(file))
				return true;

			if (LuaFindHook == null || !LuaFindHook.IsSingleplayer())
				return false;
		}

		if (!directoryOnly && filesystem.FileExists(file, pathId))
			return true;

		return filesystem.IsDirectory(file, pathId);
	}
}
