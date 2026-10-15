// </3

using Microsoft.Extensions.DependencyInjection;

using SDL;

using Source.Common;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


#if WIN32
using static PInvoke.User32;
using MSG = PInvoke.User32.MSG;
#endif
using static Source.Common.AssertDialog;


namespace Source.SDLManager;

#if WIN32
public unsafe struct WNDCLASS
{
	public ClassStyles style;

	[MarshalAs(UnmanagedType.FunctionPtr)]
	public delegate* unmanaged[Stdcall]<nint, uint, void*, void*, nint> lpfnWndProc;

	public int cbClsExtra;

	public int cbWndExtra;

	public IntPtr hInstance;

	public IntPtr hIcon;

	public IntPtr hCursor;

	public IntPtr hbrBackground;

	public char* lpszMenuName;

	public char* lpszClassName;
}
#endif
[EngineComponent]
public static class SourceDllMain
{
	static bool initialized = false;
#if WIN32
	static IntPtr sansFont;
	static IntPtr monoFont;
#endif
	static unsafe byte* s_TextOK = (byte*)Marshal.StringToCoTaskMemUTF8("OK");
	static unsafe byte* s_TextCancel = (byte*)Marshal.StringToCoTaskMemUTF8("Cancel");
	internal static unsafe SDL_Window* g_SDLWindow;
	static unsafe bool SDLMessageBox(ReadOnlySpan<char> title, ReadOnlySpan<char> desc, bool showOkAndCancel) {
		int buttonid = 0;
		SDL_MessageBoxData messageboxdata = default;
		SDL_MessageBoxButtonData* buttondata = stackalloc SDL_MessageBoxButtonData[2];
		buttondata[0] = new() { buttonID = 1, flags = SDL_MessageBoxButtonFlags.SDL_MESSAGEBOX_BUTTON_RETURNKEY_DEFAULT, text = s_TextOK };
		buttondata[1] = new() { buttonID = 0, flags = SDL_MessageBoxButtonFlags.SDL_MESSAGEBOX_BUTTON_ESCAPEKEY_DEFAULT, text = s_TextCancel };

		messageboxdata.window = g_SDLWindow;
		messageboxdata.title = (byte*)Marshal.StringToCoTaskMemUTF8(new(title));
		messageboxdata.message = (byte*)Marshal.StringToCoTaskMemUTF8(new(desc));
		messageboxdata.numbuttons = showOkAndCancel ? 2 : 1;
		messageboxdata.buttons = buttondata;

		SDL3.SDL_ShowMessageBox(&messageboxdata, &buttonid);

		Marshal.ZeroFreeCoTaskMemUTF8((nint)messageboxdata.title);
		Marshal.ZeroFreeCoTaskMemUTF8((nint)messageboxdata.message);

		return (buttonid == 1);
	}

	public static unsafe void Link(IServiceCollection services) {
		if (initialized)
			return;

		// We only run this stuff once to plug into asserts!
		OnMainThreadAssert += AssertDialog_OnMainThreadAssert;
		OnSepThreadAssert += AssertDialog_OnSepThreadAssert;
		services.AddSingleton<MessageBoxFn>(SDLMessageBox);

#if WIN32
		sansFont = CreateFont(
			18, 0, 0, 0, 400, 0, 0, 0,
			0, 0, 0, 0, 0, "Segoe UI"
		);
		monoFont = CreateFont(
			14, 0, 0, 0, 400, 0, 0, 0,
			0, 0, 0, 0, 0, "Consolas"
		);


		const int COLOR_WINDOW = 5;

		WNDCLASS wc = new WNDCLASS();
		wc.lpfnWndProc = &WndProc; // or your custom WndProc delegate
		fixed (char* ptr = "Source.NET.WindowDialog") {
			wc.lpszClassName = ptr;
			wc.hCursor = LoadCursor(0, PInvoke.Kernel32.MAKEINTRESOURCE((int)Cursors.IDC_ARROW)).DangerousGetHandle();
			wc.hbrBackground = (IntPtr)(COLOR_WINDOW + 1);
			ushort atom = RegisterClass(&wc);
		}

		wc = new WNDCLASS();
		wc.lpfnWndProc = &StdProc; // or your custom WndProc delegate
		fixed (char* ptr = "Source.NET.SubDialog") {
			wc.lpszClassName = ptr;
			wc.hCursor = LoadCursor(0, PInvoke.Kernel32.MAKEINTRESOURCE((int)Cursors.IDC_ARROW)).DangerousGetHandle();
			wc.hbrBackground = (IntPtr)(COLOR_WINDOW + 1);
			ushort atom = RegisterClass(&wc);
		}
#endif

		initialized = true;
	}

#if WIN32
	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	public static unsafe extern ushort RegisterClass([In] WNDCLASS* cls);

	private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
	[DllImport("gdi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	public static extern IntPtr CreateFont(
		int nHeight,             // Height of font
		int nWidth,              // Average character width (0 = default)
		int nEscapement,         // Angle of escapement
		int nOrientation,        // Orientation angle
		int fnWeight,            // Font weight (FW_NORMAL, FW_BOLD, etc.)
		uint fdwItalic,          // Italic (0 or 1)
		uint fdwUnderline,       // Underline (0 or 1)
		uint fdwStrikeOut,       // Strikeout (0 or 1)
		uint fdwCharSet,         // Character set
		uint fdwOutputPrecision, // Output precision
		uint fdwClipPrecision,   // Clipping precision
		uint fdwQuality,         // Output quality
		uint fdwPitchAndFamily,  // Pitch and family
		string lpszFace          // Typeface name
	);
	[DllImport("user32.dll", SetLastError = true)]
	static unsafe extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, delegate* unmanaged[Stdcall]<nint, nint, uint, nint, nint, nint> newProc);

	[DllImport("user32.dll")]
	static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	static extern uint GetDlgItemInt(IntPtr hDlg, int nIDDlgItem, IntPtr lpTranslated, int bSigned);

	[DllImport("user32.dll")]
	static extern int SetBkMode(IntPtr hdc, int mode);
	const nint HTCLIENT = 0x0001;
	const nint HTCAPTION = 0x0002;
	static AssertDialogOutgoing outgoing;

	const int SS_PATHELLIPSIS = 32768;
	const nint IGNORE_TIMES_INPUT = 280;
	const nint IGNORE_NEARBY_INPUT = 281;
	const nint VIEW_STACK_TRACE = 282;

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	static unsafe IntPtr StdProc(IntPtr hWnd, uint message, void* wParam, void* lParam) {
		return DefWindowProc(hWnd, (WindowMessage)message, (nint)wParam, (nint)lParam);
	}
	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	static unsafe IntPtr WndProc(IntPtr hWnd, uint message, void* wParam, void* lParam) {
		switch ((WindowMessage)message) {
			case WindowMessage.WM_COMMAND:
				AssertDialogResultType result = (AssertDialogResultType)(int)(nint)wParam;
				switch (result) {
					case AssertDialogResultType.Ignore:
						goto writeHandledType;
					case AssertDialogResultType.IgnoreFile:
						goto writeHandledType;
					case AssertDialogResultType.IgnoreAlways:
						goto writeHandledType;
					case AssertDialogResultType.IgnoreNearby:
						goto writeHandledType;
					case AssertDialogResultType.IgnoreAll:
						goto writeHandledType;
					case AssertDialogResultType.Break:
						goto writeHandledType;
					case (AssertDialogResultType)VIEW_STACK_TRACE:
						// Special handler; just makes a new window to show the stack trace
						nint hwnd = CreateWindowEx(0, "Source.NET.SubDialog", "Assertion Stack Traceback", WindowStyles.WS_OVERLAPPEDWINDOW | WindowStyles.WS_CAPTION | WindowStyles.WS_POPUPWINDOW, 0, 0, 800, 600, hWnd, 0, 0, 0);
						if (hwnd == 0) {
							Warning("Failed to get window handle\n");
							break;
						}
						// Oh man this is heartbreaking but it's 4 AM and I am not redoing this
						// in a more efficient way right now
						nint fileLabel = CreateWindowEx(0, "EDIT", string.Join('\n', Environment.StackTrace.Split('\n')[7..]),
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD | (WindowStyles)EditControlWindowStyles.ES_MULTILINE,
										0, 0, 800, 600, hwnd, 0, 0, 0);
						CenterWindow(800, 600, hwnd);
						HookControl(fileLabel, monoFont);
						ShowWindow(hwnd, WindowShowStyle.SW_SHOW);
						break;
				}
				break;
			writeHandledType:
				outgoing.Type = result;
				outgoing.IgnoreNumTimes = (int)GetDlgItemInt(hWnd, (int)IGNORE_TIMES_INPUT, 0, 0);
				outgoing.LineRange = (int)GetDlgItemInt(hWnd, (int)IGNORE_NEARBY_INPUT, 0, 0);
				handled = true;
				DestroyWindow(hWnd);
				return 1;

			case WindowMessage.WM_CLOSE:
			case WindowMessage.WM_DESTROY:
				handled = true;
				break;
		}
		return DefWindowProc(hWnd, (WindowMessage)message, (nint)wParam, (nint)lParam);
	}
	static void HookControl(IntPtr controlHandle, IntPtr font) {
		SendMessage(controlHandle, WindowMessage.WM_SETFONT, font, 1);
	}
#endif
	static bool handled = false;
	private static unsafe void AssertDialog_OnMainThreadAssert(ref AssertDialog.AssertDialogResult result) {
		int w = 400, h = 338;
#if WIN32
		// This is horrible but it works!
		nint hwnd = CreateWindowEx(0, "Source.NET.WindowDialog", "Source - Assertion Failed", WindowStyles.WS_OVERLAPPEDWINDOW | WindowStyles.WS_CAPTION | WindowStyles.WS_POPUPWINDOW, 0, 0, w, h, 0, 0, 0, 0);
		if (hwnd == 0) {
			Warning("Failed to get window handle\n");
			return;
		}

		outgoing = result.UserInfo;

		handled = false;

		int style = GetWindowLong(hwnd, WindowLongIndexFlags.GWL_STYLE);
		style &= ~(int)(WindowStyles.WS_MINIMIZEBOX | WindowStyles.WS_MAXIMIZEBOX);
		SetWindowLong(hwnd, WindowLongIndexFlags.GWL_STYLE, (SetWindowLongFlags)style);

		int padding = 12;
		int lineHeight = 18;
		int linePadding = 4;
		int curLine = 0;
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		nint fileLabel = CreateWindowEx(0, "STATIC", $"File: {result.AssertInfo.FileName}",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD | (WindowStyles)SS_PATHELLIPSIS,
										padding, padding + (curLine * (lineHeight + linePadding)), w - (padding * 2), lineHeight, hwnd, 0, 0, 0);
		curLine++;
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		nint lineLabel = CreateWindowEx(0, "STATIC", $"Line: {result.AssertInfo.Line}",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD,
										padding, padding + (curLine * (lineHeight + linePadding)), w - (padding * 2), lineHeight, hwnd, 0, 0, 0);
		curLine++;
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		nint expLabel = CreateWindowEx(0, "STATIC", $"Assert: {result.AssertInfo.Expression}",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD,
										padding, padding + (curLine * (lineHeight + linePadding)), w - (padding * 2), lineHeight, hwnd, 0, 0, 0); curLine++;
		// Extend line height for buttons.
		lineHeight = 24;
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

		nint breakBtn = CreateWindowEx(0, "BUTTON", $"Break in Debugger",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD | WindowStyles.WS_BORDER | (WindowStyles)ButtonWindowStyles.BS_DEFPUSHBUTTON,
										padding, padding + (curLine * (lineHeight + linePadding)), (w / 2) - (padding * 2), lineHeight, hwnd, (nint)AssertDialogResultType.Break, 0, 0);
		curLine++;
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		nint ignoreBtn = CreateWindowEx(0, "BUTTON", $"Ignore This Assert",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD | WindowStyles.WS_BORDER | (WindowStyles)ButtonWindowStyles.BS_PUSHBUTTON,
										padding, padding + (curLine * (lineHeight + linePadding)), (w / 2) - (padding * 2), lineHeight, hwnd, (nint)AssertDialogResultType.Ignore, 0, 0);
		nint ignoreTimesInput = CreateWindowEx(0, "EDIT", $"{Math.Clamp(result.UserInfo.IgnoreNumTimes, 1, 100000000)}",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD | WindowStyles.WS_BORDER | (WindowStyles)EditControlWindowStyles.ES_NUMBER,
										(w / 2), padding + (curLine * (lineHeight + linePadding)), 48, lineHeight, hwnd, IGNORE_TIMES_INPUT, 0, 0);
		nint ignoreTimesLabel = CreateWindowEx(0, "STATIC", $"time(s)",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD,
										(w / 2) + 48 + padding, padding + (curLine * (lineHeight + linePadding)), 48, lineHeight, hwnd, 0, 0, 0);
		curLine++;
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		nint alwaysIgnoreBtn = CreateWindowEx(0, "BUTTON", $"Always Ignore This Assert",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD | WindowStyles.WS_BORDER | (WindowStyles)ButtonWindowStyles.BS_PUSHBUTTON,
										padding, padding + (curLine * (lineHeight + linePadding)), (w / 2) - (padding * 2), lineHeight, hwnd, (nint)AssertDialogResultType.IgnoreAlways, 0, 0);
		curLine++;
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		nint ignoreNearbyBtn = CreateWindowEx(0, "BUTTON", $"Ignore Nearby Asserts",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD | WindowStyles.WS_BORDER | (WindowStyles)ButtonWindowStyles.BS_PUSHBUTTON,
										padding, padding + (curLine * (lineHeight + linePadding)), (w / 2) - (padding * 2), lineHeight, hwnd, (nint)AssertDialogResultType.IgnoreNearby, 0, 0);
		nint ignoreNearbyWithinLabel = CreateWindowEx(0, "STATIC", $"within",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD,
										(w / 2), padding + (curLine * (lineHeight + linePadding)), 48, lineHeight, hwnd, 0, 0, 0);
		nint ignoreNearbyInput = CreateWindowEx(0, "EDIT", $"{result.UserInfo.LineRange}",
								WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD | WindowStyles.WS_BORDER | (WindowStyles)EditControlWindowStyles.ES_NUMBER,
								(w / 2) + 40, padding + (curLine * (lineHeight + linePadding)), 48, lineHeight, hwnd, IGNORE_NEARBY_INPUT, 0, 0);
		nint ignoreNearbyTimesLabel = CreateWindowEx(0, "STATIC", $"lines",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD,
										(w / 2) + (48 * 2), padding + (curLine * (lineHeight + linePadding)), 48, lineHeight, hwnd, 0, 0, 0);
		curLine++;
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		nint ignoreFileBtn = CreateWindowEx(0, "BUTTON", $"Ignore Asserts in This File",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD | WindowStyles.WS_BORDER | (WindowStyles)ButtonWindowStyles.BS_PUSHBUTTON,
										padding, padding + (curLine * (lineHeight + linePadding)), (w / 2) - (padding * 2), lineHeight, hwnd, (nint)AssertDialogResultType.IgnoreFile, 0, 0);
		curLine++;
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		nint ignoreAll = CreateWindowEx(0, "BUTTON", $"Ignore All Asserts",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD | WindowStyles.WS_BORDER | (WindowStyles)ButtonWindowStyles.BS_PUSHBUTTON,
										padding, padding + (curLine * (lineHeight + linePadding)), (w / 2) - (padding * 2), lineHeight, hwnd, (nint)AssertDialogResultType.IgnoreAll, 0, 0);
		curLine++;
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		nint viewStackTrace = CreateWindowEx(0, "BUTTON", $"View Stack Traceback",
										WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD | WindowStyles.WS_BORDER | (WindowStyles)ButtonWindowStyles.BS_PUSHBUTTON,
										padding, padding + (curLine * (lineHeight + linePadding)), (w / 2) - (padding * 2), lineHeight, hwnd, VIEW_STACK_TRACE, 0, 0);
		curLine++;
		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


		HookControl(fileLabel, sansFont);
		HookControl(lineLabel, sansFont);
		HookControl(expLabel, sansFont);
		HookControl(breakBtn, sansFont);
		HookControl(ignoreBtn, sansFont);
		HookControl(ignoreTimesInput, sansFont);
		HookControl(ignoreTimesLabel, sansFont);
		HookControl(alwaysIgnoreBtn, sansFont);
		HookControl(ignoreNearbyBtn, sansFont);
		HookControl(ignoreNearbyWithinLabel, sansFont);
		HookControl(ignoreNearbyInput, sansFont);
		HookControl(ignoreNearbyTimesLabel, sansFont);
		HookControl(ignoreFileBtn, sansFont);
		HookControl(ignoreAll, sansFont);
		HookControl(viewStackTrace, sansFont);
		CenterWindow(w, h, hwnd);

		ShowWindow(hwnd, WindowShowStyle.SW_SHOW);

		// Main event loop
		MSG msg;
		while (!handled) {
			while (PeekMessage(&msg, 0, 0, 0, PeekMessageRemoveFlags.PM_REMOVE)) {
				TranslateMessage(ref msg);
				DispatchMessage(ref msg);
			}
			Thread.Sleep(15);
		}
		// copy back to the ref
		result.UserInfo = outgoing;
		return;
#else
		Warning($"ASSERT '{result.AssertInfo.Expression}': {result.AssertInfo.FileName} at line {result.AssertInfo.Line}\n");
		Warning(" - Cannot create assert window on this unsupported platform, ignoring.\n");
		result.UserInfo.Type = AssertDialog.AssertDialogResultType.Ignore;
		return;
#endif
	}

#if WIN32
	private static void CenterWindow(int w, int h, nint hwnd) {
		int screenWidth = GetSystemMetrics(SystemMetric.SM_CXSCREEN);
		int screenHeight = GetSystemMetrics(SystemMetric.SM_CYSCREEN);

		int posX = (screenWidth - w) / 2;
		int posY = (screenHeight - h) / 2;

		SetWindowPos(hwnd, IntPtr.Zero, posX, posY, 0, 0, SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOSIZE);
	}
#endif

	private static void AssertDialog_OnSepThreadAssert(ref AssertDialog.AssertDialogResult result) {
		throw new NotImplementedException();
	}
}
