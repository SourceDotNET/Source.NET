#define DBGFLAG_WRITE_TO_STDOUT
global using static Source.Engine.SysGlobals;

using Pastel;

using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Engine.Server;
using Source.GUI.Controls;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Source.Engine;

public static class SysGlobals
{
	public static bool InEditMode() => false;
}

// Kind of custom here, we can figure out what to properly do here later.
// What would be nice if we continue to go custom is something like "date (git branch)",
// but I don't know how to extract the git branch into the assembly (yet)...
public struct EngineVersion
{
	public static DateTime? GetLinkerTime(Assembly assembly) {
		const string BuildVersionMetadataPrefix = "+build";

		var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
		if (attribute?.InformationalVersion != null) {
			var value = attribute.InformationalVersion.Replace("\r", "").Replace("\n", "").Replace("\t", "");
			var index = value.IndexOf(BuildVersionMetadataPrefix);
			if (index > 0) {
				value = value[(index + BuildVersionMetadataPrefix.Length)..];
				return DateTime.ParseExact(value, "yyyy-MM-ddTHH:mm:ss:fffZ", CultureInfo.InvariantCulture);
			}
		}

		return null;
	}

	public static bool TryGetLinkerTime(Assembly assembly, [NotNullWhen(true)] out DateTime dateTime) {
		var dt = GetLinkerTime(assembly);
		dateTime = dt ?? default;
		return dt.HasValue;
	}

	public static EngineVersion FromAssembly(Assembly assembly, string? extra = null) {
		if (TryGetLinkerTime(assembly, out var dt))
			return new(dt, extra);

		return default;
	}

	public static readonly EngineVersion Current = FromAssembly(Assembly.GetExecutingAssembly(), "alpha");

	public DateTime Date;
	public string? Extra;

	public EngineVersion(DateTime time, string? extra = null) {
		Date = time;
		Extra = extra;
	}
}

public class Sys(Host host, ICommandLine CommandLine)
{
	public static double Time => Platform.Time;
	public bool Dedicated;
	public bool TextMode;

	public string? DisconnectReason = null;
	public string? ExtendedDisconnectReason = null;
	public bool ExtendedError = false;

	public Thread? MainThread;


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool InMainThread() => Thread.CurrentThread == MainThread;

	public bool InitGame(bool dedicated, string rootDirectory) {
		MainThread = Thread.CurrentThread;

		MathLib.Init(IMaterialSystem.GAMMA, IMaterialSystem.TEXGAMMA, 0.0f, (int)IMaterialSystem.OVERBRIGHT);

		Dbg.SpewActivate("console", 1);
		Host.developer.Changed += DeveloperChangeCallback;
		Dbg.SpewOutputFunc(SpewFunc);
		host.Initialized = false;
		Dedicated = dedicated;

		RunDataTableTest();

		host.Init(Dedicated);
		if (!host.Initialized)
			return false;

		return true;
	}

	private void DeveloperChangeCallback(IConVar cvar, in ConVarChangeContext ctx) {
		ConVarRef var = new(cvar);
		int val = var.GetInt();
		SpewActivate("developer", val);
		SpewActivate("console", val != 0 ? 2 : 1);
	}

	private void RunDataTableTest() {
		// later
	}

	public void ShutdownGame() {
		host.Shutdown();
		Shutdown();
		Host.developer.Changed -= DeveloperChangeCallback;
		Dbg.SpewOutputFunc(null);
	}

	private void Shutdown() {

	}

	public static bool InSpew => inSpew.Value;

	static ThreadLocal<bool> inSpew = new();
	static ThreadLocal<string> groupWrite = new();

	private void Write(string group, ReadOnlySpan<char> str, in Color color, bool routeInGame = false) {
		if (!groupWrite.IsValueCreated)
			groupWrite.Value = "";

		Span<char> buffer = stackalloc char[256];
		int bufferIdx = 0;
		void writeTxt(ReadOnlySpan<char> sub, in Color color) {
#if DBGFLAG_WRITE_TO_STDOUT
			if (dedicated != null)
				dedicated.Sys_Printf(sub.Pastel(color));
			else
				Console.Write(sub.Pastel(color));
#endif
			if (routeInGame) {
				host.Con?.ColorPrintf(in color, sub);
			}
		}
		void flushTxt(Span<char> buffer, in Color color) {
			if (bufferIdx > 0) {
				ReadOnlySpan<char> sub = buffer[..bufferIdx];
				writeTxt(sub, in color);
			}
			bufferIdx = 0;
		}
		void writeGroup(ReadOnlySpan<char> group, in Color color) {
			writeTxt("[", in color);
			writeTxt(group, in color);
			writeTxt("] ", in color);
		}
		void writeNewLine() {
			writeTxt(Environment.NewLine, new(255, 255, 255));
		}
		void writeBuffer(Span<char> buffer, in Color color, char c) {
			if (bufferIdx >= buffer.Length)
				flushTxt(buffer, in color);
			buffer[bufferIdx++] = c;
		}
		for (int i = 0; i < str.Length; i++) {
			char c = str[i];
			if (c == '\n') {
				groupWrite.Value = "";

				flushTxt(buffer, in color);
				writeNewLine();
			}
			else {
				if (groupWrite.Value != group) {
					flushTxt(buffer, in color);
					writeGroup(group, in color);
					groupWrite.Value = group;
				}
				writeBuffer(buffer, in color, c);
			}
		}
		flushTxt(buffer, in color);
	}
	public SpewRetval SpewFunc(SpewType spewType, ReadOnlySpan<char> msg) {
		if (!inSpew.IsValueCreated)
			inSpew.Value = false;

		bool suppress = inSpew.Value;
		inSpew.Value = true;

		const string engineGroup = "engine";
		string? group = Dbg.GetSpewOutputGroup();
		group = string.IsNullOrEmpty(group) ? engineGroup : group;

		if (!suppress) {
			/*if (TextMode) {
				if(spewType == SpewType.Message || spewType == SpewType.Log) {
					Console.Write($"[{group}] {msg}");
				}
				else {
					Console.Write($"[{group}] {msg}");
				}
			}*/

			if ((spewType != SpewType.Log) || sv.GetMaxClients() == 1) {
				Color color = new();
				switch (spewType) {
					case SpewType.Warning: color.SetColor(255, 90, 90, 255); break;
					case SpewType.Assert: color.SetColor(255, 20, 20, 255); break;
					case SpewType.Error: color.SetColor(20, 70, 255, 255); break;
					default: color = Dbg.GetSpewOutputColor(); break;
				}
				Write(group, msg, color, true);
			}
			else {
				Color color = new Color(255, 255, 255);
				Write(group, msg, in color);
			}
		}

		inSpew.Value = false;
		if (spewType == SpewType.Error) {
			Error($"[{group}] {msg}");
			return SpewRetval.Abort;
		}

		if (spewType == SpewType.Assert) {
			if (CommandLine.FindParm("-noassert") == 0)
				return SpewRetval.Debugger;
			else
				return SpewRetval.Continue;
		}

		return SpewRetval.Continue;
	}

	[DoesNotReturn]
	public static void Error(ReadOnlySpan<char> msg) {
		Singleton<MessageBoxFn>().Invoke("Engine Error", msg, false);
		Environment.Exit(100);
	}

	// public static readonly DateTime SourceEpoch = new(2003, 9, 30);
	public static readonly DateTime SourceEpoch = new(1998, 11, 7);
	/// <summary>
	/// Day counter from Nov 7th, 1998
	/// </summary>
	/// <returns>How many days since Nov 7th, 1998</returns>
	public static long BuildNumber() {
		long days = (long)(EngineVersion.Current.Date - SourceEpoch).TotalDays;
		return days;
	}

	internal static void OutputDebugString(ReadOnlySpan<char> msg) {
		// Platform.DebugString(msg);
	}

	public static void Sleep(int msec) {
		Thread.Sleep(msec);
	}
}
