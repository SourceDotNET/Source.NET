using Source.Common;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Hashing;
using Source.Common.Networking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace Source.Engine;

[EngineComponent]
public class Log(Host Host, Net Net, ICommandLine CommandLine) : IGameEventListener2
{
	const char S2A_LOGSTRING = 'R';
	const char S2A_LOGSTRING2 = 'S';

#if GMOD_DLL
	static readonly ConVar sv_logsdir = new("sv_logsdir", "logs", FCvar.Archive, "Folder in the game directory where server logs will be stored. Must start with 'logs/'.");
#else
	static readonly ConVar sv_logsdir = new("sv_logsdir", "logs", FCvar.Archive, "Folder in the game directory where server logs will be stored.");
#endif
	static readonly ConVar sv_logfile = new("sv_logfile", "1", FCvar.Archive, "Log server information in the log file.");
	static readonly ConVar sv_logflush = new("sv_logflush", "0", FCvar.Archive, "Flush the log file to disk on each write (slow).");
	static readonly ConVar sv_logecho = new("sv_logecho", "1", FCvar.Archive, "Echo log information to the console.");
	static readonly ConVar sv_log_onefile = new("sv_log_onefile", "0", FCvar.Archive, "Log server information to only one file.");
	static readonly ConVar sv_logbans = new("sv_logbans", "0", FCvar.Archive, "Log server bans in the server logs.");
	static readonly ConVar sv_logsecret = new("sv_logsecret", "0", 0, "If set then include this secret when doing UDP logging (will use 0x53 as packet type, not usual 0x52)");
#if GMOD_DLL
	static readonly ConVar sv_log_storetime = new("sv_log_storetime", "0", 0, "If set, amount of hours to keep server logs before deleting them automatically. (8760 for a year, 730 for a month. 0=don't delete)");
#endif
	static readonly ConVar sv_logfilename_format = new("sv_logfilename_format", "", FCvar.Archive, "Log filename format. See strftime for formatting codes.");
	static readonly ConVar sv_logfilecompress = new("sv_logfilecompress", "0", FCvar.Archive, "Gzip compress logfile and rename to logfilename.log.gz on close.");

	readonly List<NetAddress> LogAddresses = [];
	IFileHandle? LogFile;
	string? LogFilename;
	bool Active;
	TimeUnit_t LastLogFlush;
	bool FlushLog;
#if GMOD_DLL
	TimeUnit_t LastCleanup;
#endif

	[ConCommand("log", "Enables logging to file, console, and udp < on | off >.")]
	void log(in TokenizedCommand args) {
		if (args.ArgC() != 2) {
			ConMsg("Usage:  log < on | off >\n");

			if (IsActive()) {
				bool haveFirst = false;

				ConMsg("currently logging to: ");

				if (sv_logfile.GetInt() != 0) {
					ConMsg("file");
					haveFirst = true;
				}

				if (sv_logecho.GetInt() != 0) {
					if (haveFirst)
						ConMsg(", console");
					else {
						ConMsg("console");
						haveFirst = true;
					}
				}

				if (UsingLogAddress()) {
					if (haveFirst)
						ConMsg(", udp");
					else {
						ConMsg("udp");
						haveFirst = true;
					}
				}

				if (!haveFirst) {
					ConMsg("no destinations! (file, console, or udp)\n");
					ConMsg("check \"sv_logfile\", \"sv_logecho\", and \"logaddress_list\"");
				}

				ConMsg("\n");
			}
			else
				ConMsg("not currently logging\n");

			return;
		}

		if (stricmp(args[1], "off") == 0 || stricmp(args[1], "0") == 0) {
			if (IsActive()) {
				Close();
				SetLoggingState(false);
				ConMsg("Server logging disabled.\n");
			}
		}
		else if (stricmp(args[1], "on") == 0 || stricmp(args[1], "1") == 0) {
			SetLoggingState(true);
			ConMsg("Server logging enabled.\n");
			Open();
		}
		else
			ConMsg($"log:  unknown parameter {args[1]}, 'on' and 'off' are valid\n");
	}

	bool ParseLogAddress(in TokenizedCommand args, ReadOnlySpan<char> command, out NetAddress adr, out string address) {
		adr = new();
		address = "";

		if (args.ArgC() != 4 && args.ArgC() != 2) {
			ConMsg($"Usage:  {command} ip:port\n");
			return false;
		}

		ReadOnlySpan<char> ip = args[1];
		ReadOnlySpan<char> port;

		if (args.ArgC() == 4)
			port = args[3];
		else {
			int colon = ip.IndexOf(':');
			port = colon != -1 ? ip[(colon + 1)..] : "27015";
		}

		if (atoi(port) == 0) {
			ConMsg($"{command}:  must specify a valid port\n");
			return false;
		}

		if (ip.IsEmpty) {
			ConMsg($"{command}:  unparseable address\n");
			return false;
		}

		address = $"{ip}:{port}";
		if (address.Length > 31)
			address = address[..31];

		if (!Net.StringToAdr(address, out IPEndPoint? ep)) {
			ConMsg($"{command}:  unable to resolve {address}\n");
			return false;
		}

		adr.Endpoint = ep;
		adr.Type = NetAddressType.IP;
		return true;
	}

	[ConCommand("logaddress_add", "Set address and port for remote host <ip:port>.")]
	void logaddress_add(in TokenizedCommand args) {
		if (!ParseLogAddress(in args, "logaddress_add", out NetAddress adr, out _))
			return;

		if (AddLogAddress(adr))
			ConMsg($"logaddress_add:  {adr.Endpoint}\n");
		else
			ConMsg($"logaddress_add:  {adr.Endpoint} is already in the list\n");
	}

	[ConCommand("logaddress_delall", "Remove all udp addresses being logged to")]
	void logaddress_delall() => DelAllLogAddress();

	[ConCommand("logaddress_del", "Remove address and port for remote host <ip:port>.")]
	void logaddress_del(in TokenizedCommand args) {
		if (!ParseLogAddress(in args, "logaddress_del", out NetAddress adr, out _))
			return;

		if (DelLogAddress(adr))
			ConMsg($"logaddress_del:  {adr.Endpoint}\n");
		else
			ConMsg($"logaddress_del:  address {adr.Endpoint} not found in the list\n");
	}

	[ConCommand("logaddress_list", "List all addresses currently being used by logaddress.")]
	void logaddress_list() => ListLogAddress();

	public void Reset() {
		LogAddresses.Clear();

		LogFile = null;
		LogFilename = null;

		Active = false;
		LastLogFlush = Host.RealTime;
		FlushLog = CommandLine.CheckParm("-flushlog");
	}

	public void Init() {
		Reset();

		g_GameEventManager.AddListener(this, "server_spawn", true);
		g_GameEventManager.AddListener(this, "server_shutdown", true);
		g_GameEventManager.AddListener(this, "server_cvar", true);
		g_GameEventManager.AddListener(this, "server_message", true);
		g_GameEventManager.AddListener(this, "server_addban", true);
		g_GameEventManager.AddListener(this, "server_removeban", true);
	}

	public void Shutdown() {
		Close();
		Reset();
		g_GameEventManager.RemoveListener(this);
	}

	public void SetLoggingState(bool state) => Active = state;

	public void RunFrame() {
		if (FlushLog && LogFile != null && (Host.RealTime - LastLogFlush) > 1.0) {
			LastLogFlush = Host.RealTime;
			LogFile.Stream.Flush();
		}

#if GMOD_DLL
		if (Host.RealTime > 10.0 && (LastCleanup == 0.0 || Host.RealTime - LastCleanup > 3600.0)) {
			LastCleanup = Host.RealTime;
			CleanupStaleLogs();
		}
#endif
	}

#if GMOD_DLL
	static void CleanupStaleLogs() {
		int hours = sv_log_storetime.GetInt();
		if (hours <= 0)
			return;

		DateTime now = DateTime.UtcNow;

		ReadOnlySpan<char> file = g_pFileSystem.FindFirstEx("logs/*.log", "MOD", out FileFindHandle_t handle);
		while (!file.IsEmpty) {
			string path = $"logs/{file}";
			DateTime time = g_pFileSystem.GetFileTime(path, "MOD");
			if (time > DateTime.UnixEpoch && time.AddHours(hours) < now) {
				double age = (now - time).TotalSeconds / 3600.0;
				Warning($"Deleting log '{path}' because it exceeds sv_log_storetime ({age.ToString("F1", CultureInfo.InvariantCulture)} hours old)\n");
				g_pFileSystem.RemoveFile(path, "MOD");
			}

			file = g_pFileSystem.FindNext(handle);
		}

		g_pFileSystem.FindClose(handle);
	}
#endif

	public bool AddLogAddress(NetAddress addr) {
		for (int i = 0; i < LogAddresses.Count; i++) {
			if (LogAddresses[i].CompareAdr(addr, false))
				return false;
		}

		LogAddresses.Add(addr);
		return true;
	}

	public bool DelLogAddress(NetAddress addr) {
		for (int i = 0; i < LogAddresses.Count; i++) {
			if (LogAddresses[i].CompareAdr(addr, false)) {
				LogAddresses.RemoveAt(i);
				return true;
			}
		}

		return false;
	}

	public void ListLogAddress() {
		int count = LogAddresses.Count;

		if (count <= 0) {
			ConMsg("logaddress_list:  no addresses in the list\n");
			return;
		}

		if (count == 1)
			ConMsg($"logaddress_list: {count} entry\n");
		else
			ConMsg($"logaddress_list: {count} entries\n");

		for (int i = 0; i < count; i++)
			ConMsg($"{LogAddresses[i].Endpoint}\n");
	}

	public bool UsingLogAddress() => LogAddresses.Count > 0;

	public void DelAllLogAddress() {
		if (LogAddresses.Count > 0) {
			ConMsg("logaddress_delall:  all addresses cleared\n");
			LogAddresses.Clear();
		}
		else
			ConMsg("logaddress_delall:  no addresses in the list\n");
	}

	public void PrintServerVars() {
		if (!IsActive())
			return;

		Print("server cvars start\n");

		foreach (ConCommandBase var in cvar.GetCommands()) {
			if (var is not ConVar conVar)
				continue;

			if (!var.IsFlagSet(FCvar.Notify))
				continue;

			Print($"\"{var.GetName()}\" = \"{conVar.GetString()}\"\n");
		}

		Print("server cvars end\n");
	}

	public bool IsActive() => Active;

	public void Print(ReadOnlySpan<char> text) {
		if (!IsActive() || text.IsEmpty)
			return;

		DateTime today = DateTime.Now;

		if (text.Length > 1024)
			DevMsg(1, "CLog::Print: string too long (>1024 bytes).");

		string message = $"L {today.ToString("MM'/'dd'/'yyyy' - 'HH':'mm':'ss", CultureInfo.InvariantCulture)}: {text}";
		if (message.Length > 1099)
			message = message[..1099];

		if (sv_logecho.GetInt() != 0)
			ConMsg(message);

		if (sv_logfile.GetInt() != 0 && LogFile != null) {
			LogFile.Stream.Write(Encoding.UTF8.GetBytes(message));
			if (sv_logflush.GetBool())
				LogFile.Stream.Flush();
		}

		for (int i = 0; i < LogAddresses.Count; i++) {
			if (sv_logsecret.GetInt() != 0)
				Net.OutOfBandPrint(NetSocketType.Server, LogAddresses[i], $"{S2A_LOGSTRING2}{sv_logsecret.GetString()}{message}");
			else
				Net.OutOfBandPrint(NetSocketType.Server, LogAddresses[i], $"{S2A_LOGSTRING}{message}");
		}
	}

	public void FireGameEvent(IGameEvent ev) {
		if (!IsActive())
			return;

		ReadOnlySpan<char> name = ev.GetName();
		if (name.IsEmpty)
			return;

		if (name.SequenceEqual("server_spawn")) {
			Span<byte> md5 = stackalloc byte[MD5Value.SIZE_BYTES];
			sv.WorldmapMD5.ToBytes(md5);
			Print($"Started map \"{sv.GetMapName()}\" (CRC \"{Convert.ToHexStringLower(md5)}\")\n");
		}
		else if (name.SequenceEqual("server_shutdown"))
			Print($"server_message: \"{ev.GetString("reason")}\"\n");
		else if (name.SequenceEqual("server_cvar"))
			Print($"server_cvar: \"{ev.GetString("cvarname")}\" \"{ev.GetString("cvarvalue")}\"\n");
		else if (name.SequenceEqual("server_message"))
			Print($"server_message: \"{ev.GetString("text")}\"\n");
		else if (name.SequenceEqual("server_addban")) {
			if (sv_logbans.GetInt() > 0) {
				int userid = ev.GetInt("userid");
				ReadOnlySpan<char> playerName = ev.GetString("name");
				ReadOnlySpan<char> networkid = ev.GetString("networkid");
				ReadOnlySpan<char> ip = ev.GetString("ip");
				ReadOnlySpan<char> duration = ev.GetString("duration");
				ReadOnlySpan<char> cmdGiver = ev.GetString("by");

				if (!ip.IsEmpty) {
					string result = ev.GetInt("kicked") > 0 ? "was kicked and banned by IP" : "was banned by IP";

					if (userid > 0)
						Print($"Addip: \"{playerName}<{userid}><{networkid}><>\" {result} \"{duration}\" by \"{cmdGiver}\" (IP \"{ip}\")\n");
					else
						Print($"Addip: \"<><><>\" {result} \"{duration}\" by \"{cmdGiver}\" (IP \"{ip}\")\n");
				}
				else {
					string result = ev.GetInt("kicked") > 0 ? "was kicked and banned" : "was banned";

					if (userid > 0)
						Print($"Banid: \"{playerName}<{userid}><{networkid}><>\" {result} \"{duration}\" by \"{cmdGiver}\"\n");
					else
						Print($"Banid: \"<><{networkid}><>\" {result} \"{duration}\" by \"{cmdGiver}\"\n");
				}
			}
		}
		else if (name.SequenceEqual("server_removeban")) {
			if (sv_logbans.GetInt() > 0) {
				ReadOnlySpan<char> networkid = ev.GetString("networkid");
				ReadOnlySpan<char> ip = ev.GetString("ip");
				ReadOnlySpan<char> cmdGiver = ev.GetString("by");

				if (!ip.IsEmpty)
					Print($"Removeip: \"<><><>\" was unbanned by \"{cmdGiver}\" (IP \"{ip}\")\n");
				else
					Print($"Removeid: \"<><{networkid}><>\" was unbanned by \"{cmdGiver}\"\n");
			}
		}
	}

	static string? CreateTempFilename(ReadOnlySpan<char> filenameBase, ReadOnlySpan<char> ext) {
		bool haveLogfilenameFormat = sv_logfilename_format.GetString().Length != 0;

		string fname = new(filenameBase);
		int dot = fname.LastIndexOf('.');
		if (dot != -1 && fname.IndexOfAny(['/', '\\'], dot) == -1)
			fname = fname[..dot];

		for (int i = 0; i < 1000; i++) {
			string filename;
			if (haveLogfilenameFormat)
				filename = i == 0 ? $"{fname}.{ext}" : $"{fname}_{i - 1:D3}.{ext}";
			else
				filename = $"{fname}{i:D3}.{ext}";

			filename = filename.Replace('\\', '/');
			Common.CreatePath(filename);

			if (!g_pFileSystem.FileExists(filename, "LOGDIR"))
				return filename;
		}

		return null;
	}

	static bool GzipFileCompress(string filename) {
		string? compressed = CreateTempFilename(filename, "log.gz");
		if (compressed == null)
			return false;

		Msg($"Compressing {filename} to {compressed}...\n");

		using IFileHandle? input = g_pFileSystem.Open(filename, FileOpenOptions.Read | FileOpenOptions.Binary, "LOGDIR");
		using IFileHandle? output = g_pFileSystem.Open(compressed, FileOpenOptions.Write | FileOpenOptions.Binary, "LOGDIR");
		if (input == null || output == null)
			return false;

		try {
			using GZipStream gzip = new(output.Stream, CompressionLevel.Optimal, true);
			input.Stream.CopyTo(gzip, 16384);
		}
		catch (IOException) {
			Msg("gzip_file_compress: gzwrite failed.\n");
			return false;
		}

		return true;
	}

	static void FixupInvalidPathChars(Span<char> filename) {
		for (int i = 0; i < filename.Length; i++) {
			switch (filename[i]) {
				case ':':
				case '\n':
				case '\r':
				case '\t':
				case '.':
				case '\\':
				case '/':
					filename[i] = '_';
					break;
			}
		}
	}

#if GMOD_DLL
	static bool GMOD_IsValidPath(ReadOnlySpan<char> path) {
		return !path.IsEmpty
			&& path[0] != '/'
			&& path[0] != '\\'
			&& !path.Contains("\\\\", StringComparison.Ordinal)
			&& !path.Contains(':')
			&& !path.Contains("..", StringComparison.Ordinal)
			&& !path.Contains('\n')
			&& !path.Contains('\r')
			&& !path.Contains('\t')
			&& !path.Contains('?')
			&& !path.Contains('|')
			&& !path.Contains('>')
			&& !path.Contains('<')
			&& !path.Contains('"');
	}
#endif

	public void Close() {
		if (LogFile != null) {
			Print("Log file closed.\n");
			LogFile.Dispose();

			if (sv_logfilecompress.GetBool() && LogFilename != null) {
				if (GzipFileCompress(LogFilename)) {
					Msg($"  Success. Removing {LogFilename}.\n");
					g_pFileSystem.RemoveFile(LogFilename, "LOGDIR");
				}
			}
		}

		LogFile = null;
		LogFilename = null;
	}

	public void Flush() => LogFile?.Stream.Flush();

	public void Open() {
		if (!Active || !sv_logfile.GetBool())
			return;

		if (LogFile != null && sv_log_onefile.GetBool())
			return;

		Close();

		DateTime today = DateTime.Now;

		ReadOnlySpan<char> logsDir = sv_logsdir.GetString();
		if (!Common.IsValidPath(logsDir))
			logsDir = "logs";

#if GMOD_DLL
		if (logsDir[0] == ' ' || logsDir[0] == '\\' || logsDir[0] == '/')
			logsDir = "logs";

		if (logsDir.Contains('.') || !logsDir.StartsWith("logs", StringComparison.OrdinalIgnoreCase))
			logsDir = "logs";
#endif

		Span<char> logFilename = stackalloc char[MAX_OSPATH];
		logFilename.Clear();

		string format = sv_logfilename_format.GetString();
		if (format.Length != 0) {
			strcpy(logFilename, strftime(format, today).Trim());
#if GMOD_DLL
			if (!GMOD_IsValidPath(logFilename.SliceNullTerminatedString())) {
				Warning("Got weird symbols in 'sv_logfilename_format'!\n");
				logFilename[0] = '\0';
			}
#endif
		}

		if (logFilename[0] == '\0') {
#if GMOD_DLL
			strcpy(logFilename, $"L-{today.Year:D4}-{today.Month:D2}-{today.Day:D2}-");
#else
			strcpy(logFilename, $"L{today.Month:D2}{today.Day:D2}");
#endif
		}

		Span<char> name = logFilename.SliceNullTerminatedString();
		FixupInvalidPathChars(name);

		string fileBase = $"{logsDir}/{name}";

		string? filename = CreateTempFilename(fileBase, "log");
		IFileHandle? file = filename == null ? null : g_pFileSystem.Open(filename, FileOpenOptions.Write | FileOpenOptions.Text, "LOGDIR");
		if (file == null) {
			ConMsg($"Unable to open logfiles under {fileBase}\nLogging disabled\n");
			return;
		}

		LogFile = file;
		LogFilename = filename;

		ConMsg($"Server logging data to file {LogFilename}\n");
#if GMOD_DLL
		Print($"Log file started (file \"{LogFilename}\") (game \"garrysmod\") (version \"{build_number()}\")\n");
#else
		Print($"Log file started (file \"{LogFilename}\") (game \"{Common.Gamedir}\") (version \"{build_number()}\")\n");
#endif
	}

	static string strftime(string format, DateTime time) {
		StringBuilder result = new();
		for (int i = 0; i < format.Length; i++) {
			if (format[i] != '%' || i + 1 >= format.Length) {
				result.Append(format[i]);
				continue;
			}

			char spec = format[++i];
			switch (spec) {
				case 'Y': result.Append(time.Year.ToString("D4", CultureInfo.InvariantCulture)); break;
				case 'y': result.Append((time.Year % 100).ToString("D2", CultureInfo.InvariantCulture)); break;
				case 'm': result.Append(time.Month.ToString("D2", CultureInfo.InvariantCulture)); break;
				case 'd': result.Append(time.Day.ToString("D2", CultureInfo.InvariantCulture)); break;
				case 'H': result.Append(time.Hour.ToString("D2", CultureInfo.InvariantCulture)); break;
				case 'I': result.Append((time.Hour % 12 == 0 ? 12 : time.Hour % 12).ToString("D2", CultureInfo.InvariantCulture)); break;
				case 'M': result.Append(time.Minute.ToString("D2", CultureInfo.InvariantCulture)); break;
				case 'S': result.Append(time.Second.ToString("D2", CultureInfo.InvariantCulture)); break;
				case 'j': result.Append(time.DayOfYear.ToString("D3", CultureInfo.InvariantCulture)); break;
				case 'p': result.Append(time.Hour < 12 ? "AM" : "PM"); break;
				case 'a': result.Append(time.ToString("ddd", CultureInfo.InvariantCulture)); break;
				case 'A': result.Append(time.ToString("dddd", CultureInfo.InvariantCulture)); break;
				case 'b': result.Append(time.ToString("MMM", CultureInfo.InvariantCulture)); break;
				case 'B': result.Append(time.ToString("MMMM", CultureInfo.InvariantCulture)); break;
				case 'w': result.Append((int)time.DayOfWeek); break;
				case 'F': result.Append(time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); break;
				case 'T': result.Append(time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)); break;
				case 'D': result.Append(time.ToString("MM/dd/yy", CultureInfo.InvariantCulture)); break;
				case 'R': result.Append(time.ToString("HH:mm", CultureInfo.InvariantCulture)); break;
				case 'c': result.Append(time.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture)); break;
				case 'x': result.Append(time.ToString("MM/dd/yy", CultureInfo.InvariantCulture)); break;
				case 'X': result.Append(time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)); break;
				case 'n': result.Append('\n'); break;
				case 't': result.Append('\t'); break;
				case '%': result.Append('%'); break;
				default: result.Append('%').Append(spec); break;
			}
		}

		return result.ToString();
	}
}
