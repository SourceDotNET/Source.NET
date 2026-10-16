#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source.Common.Filesystem;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaFileLibrary
{
	public class File
	{
		public IFileHandle? Handle;
		public bool Open;
	}

	struct FileInfo
	{
		public string Name;
		public long Time;
	}

	class FileAsyncCallback
	{
		public int LuaID;
		public int Reference;
		public string? FileName;
		public string? PathID;
		public FSAsyncStatus Status;
		public byte[]? Data;
		public int BytesRead;
	}

	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_file = new("file");

	[LuaClass(typeof(File), NullError = "Tried to use a NULL File!")]
	public static readonly LuaClass LC_File = new("File", LuaType.File, null, null);

#if CLIENT_DLL
	const string LuaPathID = "lcl";
#else
	const string LuaPathID = "lsv";
#endif

	static readonly List<FileAsyncCallback> FileAsyncCallbackList = [];
	static string g_SortMethod = "";

	static readonly string[] AllowedWriteExtensions = [
		"txt", "dat", "json", "xml", "csv", "dem", "vcd", "gma", "mdl", "phy", "vvd",
		"vtx", "ani", "vtf", "vmt", "png", "jpg", "jpeg", "mp3", "wav", "ogg",
	];

	static void FileAsyncReadCallback(in FileAsyncRequest request, int bytesRead, FSAsyncStatus err) {
		lock (FileAsyncCallbackList) {
			FileAsyncCallbackList.Add(new FileAsyncCallback {
				LuaID = g_LuaID,
				Reference = (int)request.Context!,
				FileName = request.FileName,
				PathID = request.PathID,
				Status = err,
				Data = request.Data,
				BytesRead = bytesRead,
			});
		}
	}

	static bool SortFindFiles(in FileInfo a, in FileInfo b) {
		if (g_SortMethod == "datedesc")
			return a.Time > b.Time;
		if (g_SortMethod == "dateasc")
			return a.Time < b.Time;
		if (g_SortMethod == "namedesc")
			return string.CompareOrdinal(a.Name, b.Name) > 0;
		return string.CompareOrdinal(a.Name, b.Name) < 0;
	}

	static string TranslateSearchPath(string path) => stricmp(path, "LUA") == 0 ? LuaPathID : path;

	static void ReplaceAllCharacters(ref string str, string find, string replace) {
		int pos;
		while ((pos = str.IndexOf(find, StringComparison.Ordinal)) != -1)
			str = str[..pos] + replace + str[(pos + find.Length)..];
	}

	public static bool IsFileAccessAllowed(string file, string path, bool write, bool anyExtension) {
		string str = file;
		Bootil.String.Util.Trim(ref str, " \t\n\r.");

		string ext = Bootil.String.File.GetFileExtension(str);
		Bootil.String.Lower(ref ext);

		if (str.Length > 0x103)
			return false;

		if (write) {
			if (path != "DATA")
				return false;

			if (!anyExtension && Array.IndexOf(AllowedWriteExtensions, ext) == -1)
				return false;
		}

		if (Bootil.String.Test.Contains(str, "cache/chromium", true))
			return false;
		if (Bootil.String.Test.Contains(str, "ChromiumCache", true))
			return false;
		if (Bootil.String.Test.Contains(str, "GameConfig.txt", true))
			return false;
		if (Bootil.String.Test.Contains(str, "srcds_addons.txt", true))
			return false;
		if (Bootil.String.Test.Contains(str, "CmdSeq.wc", true))
			return false;
		if (Bootil.String.Test.Contains(str, "debug_dump.txt", true))
			return false;

		if (ext == "vdf"
			|| Bootil.String.Test.Contains(str, "mount.cfg", true)
			|| Bootil.String.Test.Contains(str, "server.cfg", true)
			|| Bootil.String.Test.Contains(str, "sourcetv.cfg", true)
			|| Bootil.String.Test.Contains(str, "listenserver.cfg", true)
			|| Bootil.String.Test.Contains(str, "autoexec.cfg", true)
			|| Bootil.String.Test.Contains(str, "config.cfg", true))
			return false;

		if (ext is "dmp" or "mdmp" or "db" or "log")
			return false;

		if (ext == "txt" && Bootil.String.Test.Contains(str, "crashes/", true))
			return false;

		if (!Bootil.String.Test.ContainsOnly(str, "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ01234567890-_ ./*!@#$%^&()=+;',~`[]{}"))
			return false;

		if (Bootil.String.Test.StartsWith(str, "/"))
			return false;

		if (Bootil.String.Test.Contains(str, ".."))
			return false;

		return !Bootil.String.Test.Contains(str, "./");
	}

	static File? Get_File(int stackPos) => (File?)LC_File.Get(stackPos);

	static File GetFileOrError(ILuaInterface lua) {
		File? file = Get_File(1);
		if (file == null)
			lua.Error("Tried to use a NULL File!");
		return file!;
	}

	static int ReadFileBytes(File file, Span<byte> buffer) {
		Stream stream = file.Handle!.Stream;
		if (!stream.CanRead)
			return 0;

		int bytesRead = 0;
		while (bytesRead < buffer.Length) {
			int read = stream.Read(buffer[bytesRead..]);
			if (read <= 0)
				break;
			bytesRead += read;
		}
		return bytesRead;
	}

	static void WriteFileBytes(File file, ReadOnlySpan<byte> data) {
		Stream stream = file.Handle!.Stream;
		if (stream.CanWrite)
			stream.Write(data);
	}

	static void SeekFile(File file, long pos, SeekOrigin origin) {
		Stream stream = file.Handle!.Stream;
		long target = origin == SeekOrigin.Current ? stream.Position + pos : pos;
		if (target >= 0)
			stream.Seek(target, SeekOrigin.Begin);
	}

	public static void AsyncCycle() {
		lock (FileAsyncCallbackList) {
			for (int i = 0; i < FileAsyncCallbackList.Count; i++) {
				FileAsyncCallback callback = FileAsyncCallbackList[i];
				if (callback.LuaID == g_LuaID) {
					g_Lua!.ReferencePush(callback.Reference);
					g_Lua.PushString(callback.FileName);
					g_Lua.PushString(callback.PathID);
					g_Lua.PushNumber((double)callback.Status);
					if (callback.Status == FSAsyncStatus.OK && callback.BytesRead > 0)
						g_Lua.PushString(callback.Data.AsSpan(0, callback.BytesRead));
					else
						g_Lua.PushString("");
					g_Lua.CallFunctionProtected(4, 0, true);
				}
				g_Lua!.ReferenceFree(callback.Reference);
			}
			FileAsyncCallbackList.Clear();
		}
	}

	[LuaMethod]
	static int File____tostring(ILuaInterface lua) {
		File? file = Get_File(1);
		if (file == null || !file.Open)
			lua.PushString("File [NULL]");
		else
			lua.PushString("File");
		return 1;
	}

	[LuaMethod]
	static int File__Close(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (file.Open) {
			file.Handle!.Dispose();
			file.Open = false;
		}
		return 0;
	}

	[LuaMethod]
	static int File__Size(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		lua.PushNumber((uint)file.Handle!.Stream.Length);
		return 1;
	}

	[LuaMethod]
	static int File__Read(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Stream stream = file.Handle!.Stream;
		long size = (uint)stream.Length;
		long pos = (uint)stream.Position;

		long length;
		if (lua.GetType(2) != LuaType.Nil)
			length = (int)lua.CheckNumber(2);
		else
			length = size - pos;

		bool pastEnd = length + pos > size;
		long toRead;
		if (!pastEnd && length > 0)
			toRead = length;
		else {
#if !MENU_DLL
			if (Game.Client.GarrysMod.GarrysMod.lua_strict.GetInt() != 0) {
				lua.ErrorFromLua($"File:Read() - Invalid requested size {(int)length}\n");
				return 0;
			}
#endif

			toRead = pastEnd && size > pos ? size - pos : 0;
		}

		byte[] buffer = new byte[toRead];
		int bytesRead = ReadFileBytes(file, buffer);
		if (bytesRead == 0)
			return 0;

		lua.PushString(buffer.AsSpan(0, bytesRead));
		return 1;
	}

	[LuaMethod]
	static int File__ReadLine(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<char> buffer = stackalloc char[0x2000];
		ReadOnlySpan<char> line = filesystem.ReadLine(buffer, file.Handle!);
		if (line.IsEmpty)
			return 0;

		int length = line.IndexOf('\0');
		if (length == -1)
			length = line.Length;

		Span<byte> bytes = stackalloc byte[length];
		for (int i = 0; i < length; i++)
			bytes[i] = (byte)line[i];

		lua.PushString(bytes);
		return 1;
	}

	[LuaMethod]
	static int File__Write(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		ReadOnlySpan<byte> data = lua.GetDataString(2);
		if (data.Length > 0) {
			WriteFileBytes(file, data);
			return 0;
		}

#if !MENU_DLL
		if (Game.Client.GarrysMod.GarrysMod.lua_strict.GetInt() != 0)
#endif
			lua.ErrorFromLua($"File:Write() - Invalid write size {data.Length}\n");
		return 0;
	}

	[LuaMethod]
	static int File__Seek(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (file.Open)
			SeekFile(file, (int)lua.CheckNumber(2), SeekOrigin.Begin);
		return 0;
	}

	[LuaMethod]
	static int File__Skip(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (file.Open)
			SeekFile(file, (int)lua.CheckNumber(2), SeekOrigin.Current);
		return 0;
	}

	[LuaMethod]
	static int File__Tell(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		lua.PushNumber((uint)file.Handle!.Stream.Position);
		return 1;
	}

	[LuaMethod]
	static int File__ReadByte(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[1];
		if (ReadFileBytes(file, value) == 0)
			return 0;

		lua.PushNumber(value[0]);
		return 1;
	}

	[LuaMethod]
	static int File__ReadBool(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[1];
		if (ReadFileBytes(file, value) == 0)
			return 0;

		lua.PushBool(value[0] != 0);
		return 1;
	}

	[LuaMethod]
	static int File__ReadShort(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[2];
		if (ReadFileBytes(file, value) == 0)
			return 0;

		lua.PushNumber(BitConverter.ToInt16(value));
		return 1;
	}

	[LuaMethod]
	static int File__ReadUShort(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[2];
		if (ReadFileBytes(file, value) == 0)
			return 0;

		lua.PushNumber(BitConverter.ToUInt16(value));
		return 1;
	}

	[LuaMethod]
	static int File__ReadLong(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[4];
		if (ReadFileBytes(file, value) == 0)
			return 0;

		lua.PushLong(BitConverter.ToInt32(value));
		return 1;
	}

	[LuaMethod]
	static int File__ReadULong(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[4];
		if (ReadFileBytes(file, value) == 0)
			return 0;

		lua.PushNumber(BitConverter.ToUInt32(value));
		return 1;
	}

	[LuaMethod]
	static int File__ReadUInt64(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[8];
		if (ReadFileBytes(file, value) == 0)
			return 0;

		lua.PushString(Bootil.String.Format.UInt64(BitConverter.ToUInt64(value)));
		return 1;
	}

	[LuaMethod]
	static int File__ReadFloat(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[4];
		if (ReadFileBytes(file, value) == 0)
			return 0;

		lua.PushNumber(BitConverter.ToSingle(value));
		return 1;
	}

	[LuaMethod]
	static int File__ReadDouble(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[8];
		if (ReadFileBytes(file, value) == 0)
			return 0;

		lua.PushNumber(BitConverter.ToDouble(value));
		return 1;
	}

	[LuaMethod]
	static int File__WriteByte(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		ReadOnlySpan<byte> value = [(byte)(int)lua.CheckNumber(2)];
		WriteFileBytes(file, value);
		return 0;
	}

	[LuaMethod]
	static int File__WriteBool(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		ReadOnlySpan<byte> value = [(byte)(lua.GetBool(2) ? 1 : 0)];
		WriteFileBytes(file, value);
		return 0;
	}

	[LuaMethod]
	static int File__WriteShort(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[2];
		BitConverter.TryWriteBytes(value, (short)(int)lua.GetNumber(2));
		WriteFileBytes(file, value);
		return 0;
	}

	[LuaMethod]
	static int File__WriteUShort(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[2];
		BitConverter.TryWriteBytes(value, (ushort)(int)lua.GetNumber(2));
		WriteFileBytes(file, value);
		return 0;
	}

	[LuaMethod]
	static int File__WriteLong(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[4];
		BitConverter.TryWriteBytes(value, (int)lua.GetNumber(2));
		WriteFileBytes(file, value);
		return 0;
	}

	[LuaMethod]
	static int File__WriteULong(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		double number = lua.GetNumber(2);
		uint unsigned = number >= 2147483648.0 ? (uint)(int)(number - 2147483648.0) ^ 0x80000000u : (uint)(int)number;
		Span<byte> value = stackalloc byte[4];
		BitConverter.TryWriteBytes(value, unsigned);
		WriteFileBytes(file, value);
		return 0;
	}

	[LuaMethod]
	static int File__WriteFloat(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[4];
		BitConverter.TryWriteBytes(value, (float)lua.GetNumber(2));
		WriteFileBytes(file, value);
		return 0;
	}

	[LuaMethod]
	static int File__WriteDouble(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[8];
		BitConverter.TryWriteBytes(value, lua.GetNumber(2));
		WriteFileBytes(file, value);
		return 0;
	}

	[LuaMethod]
	static int File__WriteUInt64(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Span<byte> value = stackalloc byte[8];
		BitConverter.TryWriteBytes(value, Bootil.String.To.UInt64(lua.CheckString(2)));
		WriteFileBytes(file, value);
		return 0;
	}

	[LuaMethod]
	static int File__EndOfFile(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (!file.Open)
			return 0;

		Stream stream = file.Handle!.Stream;
		lua.PushBool(stream.Position >= stream.Length);
		return 1;
	}

	[LuaMethod]
	static int File____gc(ILuaInterface lua) {
		File? file = Get_File(1);
		if (file != null && file.Open) {
			file.Handle!.Dispose();
			file.Open = false;
		}
		if (file != null)
			lua.ReleaseUserTypeObject(file);
		lua.SetUserType(1, 0);
		return 0;
	}

	[LuaMethod]
	static int File__Flush(ILuaInterface lua) {
		File file = GetFileOrError(lua);
		if (file.Open)
			file.Handle!.Stream.Flush();
		return 0;
	}

	[LuaFunction]
	static int Exists(ILuaInterface lua) {
		string file = lua.CheckString(1);
		string path = TranslateSearchPath(lua.CheckString(2));
		Bootil.String.File.FixSlashes(ref file, "\\", "/");
		ReplaceAllCharacters(ref file, "//", "/");

		if (!IsFileAccessAllowed(file, path, false, false)) {
			lua.PushBool(false);
			return 1;
		}

		if (path == "lcl" || path == "lsv")
			lua.PushBool(get.LuaShared()!.ScriptExists(file, path, false));
		else
			lua.PushBool(filesystem.FileExists(file, path) || filesystem.IsDirectory(file, path));
		return 1;
	}

	[LuaFunction]
	static int IsDir(ILuaInterface lua) {
		string file = lua.CheckString(1);
		string path = TranslateSearchPath(lua.CheckString(2));
		Bootil.String.File.FixSlashes(ref file, "\\", "/");
		ReplaceAllCharacters(ref file, "//", "/");

		if (!IsFileAccessAllowed(file, path, false, true)) {
			lua.PushBool(false);
			return 1;
		}

		if (path == "lcl" || path == "lsv") {
			if (!Bootil.String.Test.EndsWith(file, "/"))
				file += "/";
			lua.PushBool(get.LuaShared()!.ScriptExists(file, path, true));
		}
		else
			lua.PushBool(filesystem.IsDirectory(file, path));
		return 1;
	}

	[LuaFunction]
	static int Rename(ILuaInterface lua) {
		string oldName = lua.CheckString(1);
		string newName = lua.CheckString(2);
		string path = "DATA";
		Bootil.String.File.FixSlashes(ref oldName, "\\", "/");
		Bootil.String.File.FixSlashes(ref newName, "\\", "/");
		ReplaceAllCharacters(ref oldName, "//", "/");
		ReplaceAllCharacters(ref newName, "//", "/");
		Bootil.String.Lower(ref oldName);
		Bootil.String.Lower(ref newName);

		bool directory = filesystem.IsDirectory(oldName, path);
		if (!IsFileAccessAllowed(oldName, path, true, directory) || !IsFileAccessAllowed(newName, path, true, directory)) {
			lua.PushBool(false);
			return 1;
		}

		lua.PushBool(filesystem.RenameFile(oldName, newName, path));
		return 1;
	}

	[LuaFunction]
	static int Delete(ILuaInterface lua) {
		string file = lua.CheckString(1);
		Bootil.String.File.FixSlashes(ref file, "\\", "/");
		ReplaceAllCharacters(ref file, "//", "/");
		string path = "DATA";

		if (!IsFileAccessAllowed(file, path, true, true)) {
			lua.PushBool(false);
			return 1;
		}

		lua.PushBool(filesystem.RemoveFile(file, path));
		return 1;
	}

	[LuaFunction]
	static int CreateDir(ILuaInterface lua) {
		string name = lua.CheckString(1);
		string path = "DATA";
		Bootil.String.File.FixSlashes(ref name, "\\", "/");
		ReplaceAllCharacters(ref name, "//", "/");

		if (IsFileAccessAllowed(name, path, true, true))
			filesystem.CreateDirHierarchy(name, path);
		return 0;
	}

	[LuaFunction]
	static int Time(ILuaInterface lua) {
		string file = lua.CheckString(1);
		string path = TranslateSearchPath(lua.CheckString(2));
		Bootil.String.File.FixSlashes(ref file, "\\", "/");
		ReplaceAllCharacters(ref file, "//", "/");

		if (!IsFileAccessAllowed(file, path, false, false)) {
			lua.PushLong(0);
			return 1;
		}

		lua.PushLong((long)(filesystem.GetFileTime(file, path) - DateTime.UnixEpoch).TotalSeconds);
		return 1;
	}

	[LuaFunction]
	static int Size(ILuaInterface lua) {
		string file = lua.CheckString(1);
		string path = TranslateSearchPath(lua.CheckString(2));
		Bootil.String.File.FixSlashes(ref file, "\\", "/");
		ReplaceAllCharacters(ref file, "//", "/");

		if (!IsFileAccessAllowed(file, path, false, false))
			return 0;

		long size = filesystem.Size(file, path);
		lua.PushLong(size < 0 ? 0 : size);
		return 1;
	}

	[LuaFunction]
	static int Open(ILuaInterface lua) {
		string fileName = lua.CheckString(1);
		string mode = lua.CheckString(2);

		bool write = mode is "w" or "wb" or "a" or "ab";
		if (mode != "r" && mode != "rb" && !write) {
			lua.ErrorFromLua($"file.Open: Invalid file open mode '{mode}'!");
			return 0;
		}

		string path = TranslateSearchPath(lua.CheckString(3));
		Bootil.String.File.FixSlashes(ref fileName, "\\", "/");
		ReplaceAllCharacters(ref fileName, "//", "/");

		if (write || stricmp(path, "data") == 0)
			Bootil.String.Lower(ref fileName);

		if (!IsFileAccessAllowed(fileName, path, write, false))
			return 0;

		FileOpenOptions options = mode switch {
			"r" => FileOpenOptions.Read | FileOpenOptions.Text,
			"rb" => FileOpenOptions.Read | FileOpenOptions.Binary,
			"w" => FileOpenOptions.Write | FileOpenOptions.Text,
			"wb" => FileOpenOptions.Write | FileOpenOptions.Binary,
			"a" => FileOpenOptions.Append | FileOpenOptions.Text,
			_ => FileOpenOptions.Append | FileOpenOptions.Binary,
		};

		IFileHandle? handle = filesystem.Open(fileName, options, path);
		if (handle == null)
			return 0;

		LC_File.Push(new File { Handle = handle, Open = true });
		return 1;
	}

#if !MENU_DLL
	[LuaFunction]
	static int AsyncRead(ILuaInterface lua) {
		string fileName = lua.CheckString(1);
		string path = TranslateSearchPath(lua.CheckString(2));
		lua.CheckType(3, LuaType.Function);

		bool sync;
		if (lua.IsType(4, LuaType.None))
			sync = false;
		else {
			lua.CheckType(4, LuaType.Bool);
			sync = lua.GetBool(4);
		}

		Bootil.String.File.FixSlashes(ref fileName, "\\", "/");
		ReplaceAllCharacters(ref fileName, "//", "/");

		if (stricmp(path, "data") == 0)
			Bootil.String.Lower(ref fileName);

		if (!IsFileAccessAllowed(fileName, path, false, false)) {
			lua.PushNumber(-1);
			return 1;
		}

		long size = filesystem.Size(fileName, path);
		int bytes = size < 0 ? 0 : (int)size;

		FileAsyncRequest request = new() {
			FileName = fileName,
			Data = new byte[bytes],
			Bytes = bytes,
			Callback = FileAsyncReadCallback,
			Priority = -1,
			Flags = sync ? FSAsyncFlags.Sync : 0,
			PathID = path,
		};

		lua.Push(3);
		request.Context = lua.ReferenceCreate();

		lua.PushNumber((double)filesystem.AsyncRead(request));
		return 1;
	}
#endif

	[LuaFunction]
	static int Find(ILuaInterface lua) {
		string wildcard = lua.CheckString(1);
		string path = TranslateSearchPath(lua.CheckString(2));
		g_SortMethod = lua.CheckStringOpt(3, "nameasc");
		Bootil.String.File.FixSlashes(ref wildcard, "\\", "/");
		ReplaceAllCharacters(ref wildcard, "//", "/");

		string basePath = wildcard;
		Bootil.String.File.StripFilename(ref basePath);

		if (!IsFileAccessAllowed(wildcard, path, false, false))
			return 0;

		List<FileInfo> files = [];
		List<FileInfo> dirs = [];

		if (path == LuaPathID) {
			if (g_SortMethod == "datedesc" || g_SortMethod == "dateasc")
				g_SortMethod = "nameasc";

			List<LuaFindResult> results = [];
			get.LuaShared()!.FindScripts(wildcard, path, results);
			foreach (LuaFindResult result in results) {
				FileInfo info = new() { Name = result.FileName, Time = 0 };
				if (result.IsFolder)
					dirs.Add(info);
				else
					files.Add(info);
			}
		}
		else {
			ReadOnlySpan<char> name = filesystem.FindFirstEx(wildcard, path, out ulong handle);
			while (!name.IsEmpty) {
				if (name[0] != '.') {
					FileInfo info = new() { Name = name.ToString() };
					if (g_SortMethod == "datedesc" || g_SortMethod == "dateasc")
						info.Time = (long)(filesystem.GetFileTime(basePath + info.Name, path) - DateTime.UnixEpoch).TotalSeconds;

					if (filesystem.FindIsDirectory(handle))
						dirs.Add(info);
					else
						files.Add(info);
				}
				name = filesystem.FindNext(handle);
			}
			filesystem.FindClose(handle);
		}

		files.Sort((a, b) => SortFindFiles(a, b) ? -1 : SortFindFiles(b, a) ? 1 : 0);
		LuaTable fileTable = new(null, (uint)files.Count);
		for (int i = 0; i < files.Count; i++)
			fileTable.SetMember(i + 1, files[i].Name);
		fileTable.Push();
		fileTable.UnReference();

		dirs.Sort((a, b) => SortFindFiles(a, b) ? -1 : SortFindFiles(b, a) ? 1 : 0);
		LuaTable dirTable = new(null, (uint)dirs.Count);
		for (int i = 0; i < dirs.Count; i++)
			dirTable.SetMember(i + 1, dirs[i].Name);
		dirTable.Push();
		dirTable.UnReference();

		return 2;
	}
}
#endif
