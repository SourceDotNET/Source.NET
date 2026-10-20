using Source.Common;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source;

public static class PlatformMacros
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsX360() => false;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsRetail() => false;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsOSX()
#if OSX
		=> true;
#else
		=> false;
#endif
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsPlatform64Bits() => IntPtr.Size == 8;

	public const int MAX_PATH = 260;
	public const int MAX_OSPATH = 260;
}

public delegate bool MessageBoxFn(ReadOnlySpan<char> title, ReadOnlySpan<char> info, bool showOkAndCancel);

public static class Platform
{
	readonly static Lazy<Stopwatch> __timer = new(() => {
		Stopwatch stopwatch = new();
		stopwatch.Start();
		return stopwatch;
	});

	public static ReadOnlySpan<char> GetCommandLine() => Environment.CommandLine;
	public static double Time => __timer.Value.Elapsed.TotalSeconds;
	public static double MSTime => __timer.Value.Elapsed.TotalMilliseconds;

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
		ThreadUtils.SetMainThread();
	}
}
