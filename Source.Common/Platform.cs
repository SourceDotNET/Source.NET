using Source.Common;

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Source;

public delegate bool MessageBoxFn(ReadOnlySpan<char> title, ReadOnlySpan<char> info, bool showOkAndCancel);

public static class Platform
{
	public const bool IsWindows
#if WIN32
		= true;
#else
		= false;
#endif

	public const bool IsOSX
#if OSX
		= true;
#else
		= false;
#endif

	public const bool IsPosix
#if POSIX
		= true;
#else
		= false;
#endif

	public const int MAX_PATH = 260;
	public const int MAX_OSPATH = 260;

	readonly static Lazy<Stopwatch> __timer = new(() => {
		Stopwatch stopwatch = new();
		stopwatch.Start();
		return stopwatch;
	});

	public static ReadOnlySpan<char> GetCommandLine() => Environment.CommandLine;
	public static double Time => __timer.Value.Elapsed.TotalSeconds;
	public static double MSTime => __timer.Value.Elapsed.TotalMilliseconds;

	public static ulong ApproximateProcessMemoryUsage() {
#if POSIX
		try {
			ReadOnlySpan<char> statm = File.ReadAllText("/proc/self/statm").AsSpan().TrimStart();
			int end = statm.IndexOfAny(' ', '\t');
			if (ulong.TryParse(end < 0 ? statm : statm[..end], out ulong size))
				return 4096 * size;
		}
		catch (IOException) {
		}
		catch (UnauthorizedAccessException) {
		}
#endif
		return 0;
	}

#if WIN32
	[DllImport("kernel32.dll")]
	unsafe static extern void OutputDebugStringW(char* lpOutputString);
#endif

	public static unsafe void DebugString(ReadOnlySpan<char> buf) {
#if WIN32
		fixed (char* cbuf = buf)
			OutputDebugStringW(cbuf);
#endif
	}

	public static void Initialize() {
		CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
		CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
		ThreadUtils.SetMainThread();
	}
}
