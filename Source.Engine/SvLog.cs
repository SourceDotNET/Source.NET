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

	static FileStream? OpenDirect(string filename, FileMode mode, FileAccess access) {
		try {
			string? directory = Path.GetDirectoryName(Path.GetFullPath(filename));
			if (directory != null && !Directory.Exists(directory))
				return null;

			return new FileStream(filename, mode, access, FileShare.ReadWrite);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
			return null;
		}
	}

	static bool CreateTempFilename(ReadOnlySpan<char> filenameBase, ReadOnlySpan<char> ext, bool isGzip, out string? filename, out IFileHandle? file, out FileStream? gzfile) {
		bool haveLogfilenameFormat = sv_logfilename_format.GetString().Length != 0;

		filename = null;
		file = null;
		gzfile = null;

		string fname = new(filenameBase);
		int dot = fname.LastIndexOf('.');
		if (dot != -1 && fname.IndexOfAny(['/', '\\'], dot) == -1)
			fname = fname[..dot];

		for (int i = 0; i < 1000; i++) {
			string name;
			if (haveLogfilenameFormat)
				name = i == 0 ? $"{fname}.{ext}" : $"{fname}_{i - 1:D3}.{ext}";
			else
				name = $"{fname}{i:D3}.{ext}";

#if WIN32
			name = name.Replace('/', '\\');
#else
			name = name.Replace('\\', '/');
#endif
			Common.CreatePath(name);

			if (!g_pFileSystem.FileExists(name, "LOGDIR")) {
				if (!isGzip) {
					file = g_pFileSystem.Open(name, FileOpenOptions.Write | FileOpenOptions.Text, "LOGDIR");
					if (file != null) {
						filename = name;
						return true;
					}
				}
				else {
					gzfile = OpenDirect(name, FileMode.Create, FileAccess.Write);
					if (gzfile != null) {
						filename = name;
						return true;
					}
				}
			}
		}

		return false;
	}

	static bool GzipFileCompress(string filename) {
		if (!CreateTempFilename(filename, "log.gz", true, out string? compressed, out _, out FileStream? gzfile))
			return false;

		Msg($"Compressing {filename} to {compressed}...\n");

		bool ret = true;

		FileStream? input = OpenDirect(filename, FileMode.Open, FileAccess.Read);
		if (input != null) {
			GZipStream gzip = new(gzfile!, CompressionLevel.Optimal);
			byte[] buf = new byte[16384];
			for (; ; ) {
				int len;
				try {
					len = input.Read(buf, 0, buf.Length);
				}
				catch (IOException) {
					Msg("gzip_file_compress: fread failed.\n");
					break;
				}

				if (len == 0)
					break;

				try {
					gzip.Write(buf, 0, len);
				}
				catch (IOException) {
					Msg("gzip_file_compress: gzwrite failed.\n");
					break;
				}
			}

			try {
				gzip.Dispose();
			}
			catch (IOException) {
				Msg("gzip_file_compress: gzclose failed.\n");
				ret = false;
			}

			input.Dispose();
		}
		else
			gzfile!.Dispose(); // note: GMod leaves it open

		return ret;
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

		if (!CreateTempFilename(fileBase, "log", false, out string? filename, out IFileHandle? file, out _)) {
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

	static string strftime(ReadOnlySpan<char> format, DateTime time) {
		CultureInfo culture = CultureInfo.InvariantCulture;
		StringBuilder result = new();
		int yday = time.DayOfYear - 1;
		int wday = (int)time.DayOfWeek;

		for (int i = 0; i < format.Length; i++) {
			if (format[i] != '%') {
				result.Append(format[i]);
				continue;
			}

			int start = i;
			char flag = '\0';
#if WIN32
			if (i + 1 < format.Length && format[i + 1] == '#')
				flag = format[++i];
#else
			if (i + 1 < format.Length && (format[i + 1] is '-' or '_' or '0' or '^' or '#'))
				flag = format[++i];
#endif
			if (i + 1 < format.Length && (format[i + 1] is 'E' or 'O'))
				i++;

			char spec = i + 1 < format.Length ? format[++i] : '\0';

			void Text(string text) => result.Append(flag == '^' ? text.ToUpperInvariant() : text);

			void Num(int value, int width, char pad = '0') {
#if WIN32
				if (flag == '#')
					width = 0;
#else
				if (flag == '-')
					width = 0;
				else if (flag == '_')
					pad = ' ';
				else if (flag == '0')
					pad = '0';
#endif
				string digits = value.ToString(culture);
				result.Append(pad, Math.Max(0, width - digits.Length)).Append(digits);
			}

			switch (spec) {
				case 'a': Text(time.ToString("ddd", culture)); break;
				case 'A': Text(time.ToString("dddd", culture)); break;
				case 'b':
				case 'h': Text(time.ToString("MMM", culture)); break;
				case 'B': Text(time.ToString("MMMM", culture)); break;
				case 'c':
#if WIN32
					result.Append(flag == '#' ? time.ToString("dddd, MMMM dd, yyyy HH:mm:ss", culture) : strftime("%m/%d/%y %H:%M:%S", time));
#else
					result.Append(strftime("%a %b %e %H:%M:%S %Y", time));
#endif
					break;
				case 'C': Num(time.Year / 100, 2); break;
				case 'd': Num(time.Day, 2); break;
				case 'D': result.Append(strftime("%m/%d/%y", time)); break;
				case 'e': Num(time.Day, 2, ' '); break;
				case 'F': result.Append(strftime("%Y-%m-%d", time)); break;
				case 'g': Num(ISOWeek.GetYear(time) % 100, 2); break;
				case 'G': Num(ISOWeek.GetYear(time), 4); break;
				case 'H': Num(time.Hour, 2); break;
				case 'I': Num(time.Hour % 12 == 0 ? 12 : time.Hour % 12, 2); break;
				case 'j': Num(time.DayOfYear, 3); break;
				case 'm': Num(time.Month, 2); break;
				case 'M': Num(time.Minute, 2); break;
				case 'n': result.Append('\n'); break;
				case 'p': Text(time.Hour < 12 ? "AM" : "PM"); break;
				case 'r': result.Append(strftime("%I:%M:%S %p", time)); break;
				case 'R': result.Append(strftime("%H:%M", time)); break;
				case 'S': Num(time.Second, 2); break;
				case 't': result.Append('\t'); break;
				case 'T': result.Append(strftime("%H:%M:%S", time)); break;
				case 'u': Num(wday == 0 ? 7 : wday, 1); break;
				case 'U': Num((yday + 7 - wday) / 7, 2); break;
				case 'V': Num(ISOWeek.GetWeekOfYear(time), 2); break;
				case 'w': Num(wday, 1); break;
				case 'W': Num((yday + 7 - (wday + 6) % 7) / 7, 2); break;
				case 'x':
#if WIN32
					result.Append(flag == '#' ? time.ToString("dddd, MMMM dd, yyyy", culture) : strftime("%m/%d/%y", time));
#else
					result.Append(strftime("%m/%d/%y", time));
#endif
					break;
				case 'X': result.Append(strftime("%H:%M:%S", time)); break;
				case 'y': Num(time.Year % 100, 2); break;
				case 'Y': Num(time.Year, 4); break;
				case 'z': {
					TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(time);
					result.Append(offset < TimeSpan.Zero ? '-' : '+');
					offset = offset.Duration();
					result.Append(offset.Hours.ToString("D2", culture)).Append(offset.Minutes.ToString("D2", culture));
					break;
				}
				case 'Z': Text(TimeZoneInfo.Local.IsDaylightSavingTime(time) ? TimeZoneInfo.Local.DaylightName : TimeZoneInfo.Local.StandardName); break;
				case '%': result.Append('%'); break;
#if !WIN32
				case 'k': Num(time.Hour, 2, ' '); break;
				case 'l': Num(time.Hour % 12 == 0 ? 12 : time.Hour % 12, 2, ' '); break;
				case 's': result.Append(new DateTimeOffset(time).ToUnixTimeSeconds().ToString(culture)); break;
#endif
				default:
#if WIN32
					return "";
#else
					result.Append(format[start..Math.Min(i + 1, format.Length)]);
					break;
#endif
			}
		}

		return result.ToString();
	}
}
