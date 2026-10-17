using Microsoft.Extensions.DependencyInjection;

using SDL;

using Source.Common.GUI;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.Bitmap;

using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.SDLManager;

internal static class SDL3_State
{
	static bool ready = false;
	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	internal static unsafe void SDL_Log(nint _, int categoryRaw, SDL_LogPriority priority, byte* str) {
		SDL_LogCategory category = (SDL_LogCategory)categoryRaw;
		string msg = SDL3.PtrToStringUTF8(str) ?? "<NULL MESSAGE>";

		ReadOnlySpan<char> piece = category switch {
			SDL_LogCategory.SDL_LOG_CATEGORY_APPLICATION => "SDL/App: ",
			SDL_LogCategory.SDL_LOG_CATEGORY_ASSERT => "SDL/Assert: ",
			SDL_LogCategory.SDL_LOG_CATEGORY_AUDIO => "SDL/Audio: ",
			SDL_LogCategory.SDL_LOG_CATEGORY_CUSTOM => "SDL/Custom: ",
			SDL_LogCategory.SDL_LOG_CATEGORY_ERROR => "SDL/Error: ",
			SDL_LogCategory.SDL_LOG_CATEGORY_GPU => "SDL/GPU: ",
			SDL_LogCategory.SDL_LOG_CATEGORY_INPUT => "SDL/Input: ",
			SDL_LogCategory.SDL_LOG_CATEGORY_RENDER => "SDL/Render: ",
			SDL_LogCategory.SDL_LOG_CATEGORY_SYSTEM => "SDL/System: ",
			SDL_LogCategory.SDL_LOG_CATEGORY_TEST => "SDL/Test: ",
			SDL_LogCategory.SDL_LOG_CATEGORY_VIDEO => "SDL/Video: ",
			_ => "SDL/Unknown: "
		};

		Span<char> message = stackalloc char[piece.Length + msg.Length + 1];
		piece.CopyTo(message);
		msg?.CopyTo(message[piece.Length..]);
		message[^1] = '\n';
		switch (priority) {
			case SDL_LogPriority.SDL_LOG_PRIORITY_TRACE: Msg(message); break;
			case SDL_LogPriority.SDL_LOG_PRIORITY_VERBOSE: Msg(message); break;
			case SDL_LogPriority.SDL_LOG_PRIORITY_DEBUG: Msg(message); break;
			case SDL_LogPriority.SDL_LOG_PRIORITY_INFO: Msg(message); break;
			case SDL_LogPriority.SDL_LOG_PRIORITY_WARN: Warning(message); break;
			case SDL_LogPriority.SDL_LOG_PRIORITY_ERROR: Error(message); break;
			case SDL_LogPriority.SDL_LOG_PRIORITY_CRITICAL: Error(message); break;
			default: Msg(message); break;
		}
	}

	internal static unsafe void InitializeIfRequired() {
		if (ready) return;

		SDL3.SDL_SetLogOutputFunction(&SDL_Log, 0);
		SDL3.SDL_SetLogPriorities(SDL_LogPriority.SDL_LOG_PRIORITY_TRACE);
		SDL3.SDL_SetAppMetadata(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "N/A", "N/A");
		if (!SDL3.SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_VIDEO))
			throw new Exception($"Couldn't initialize SDL3's video subsystem: ${SDL3.SDL_GetError()}");

		ready = true;
	}
}

public unsafe class SDL3_OpenGL46_Context(nint window, nint ctx) : IGraphicsContext
{
	public nint HardwareHandle => ctx;

	public void MakeCurrent() {
		if (!SDL3.SDL_GL_MakeCurrent((SDL_Window*)window, (SDL_GLContextState*)ctx))
			Warning($"Could not make GL context current: {SDL3.SDL_GetError()}\n");
	}

	public void SetSwapInterval(float swapInterval) {
		SDL3.SDL_GL_MakeCurrent((SDL_Window*)window, (SDL_GLContextState*)ctx);
		if (!SDL3.SDL_GL_SetSwapInterval((int)swapInterval)) {
			throw new Exception($"Could not set vsync: {SDL3.SDL_GetError()}");
		}
	}

	public void SwapBuffers() {
		SDL3.SDL_GL_SwapWindow((SDL_Window*)window);
	}
}

public unsafe class SDL3_LauncherManager : ILauncherManager, IGraphicsProvider
{
	readonly IServiceProvider services;
	public SDL3_LauncherManager(IServiceProvider services) {
		this.services = services;
		SDL3_State.InitializeIfRequired();
		InitCursors();
		window = null!;
	}
	SDL3_Window window;
	public bool CreateGameWindow(string title, bool windowed, bool borderless, int width, int height) {
		IMaterialSystem materials = services.GetRequiredService<IMaterialSystem>();

		SDL_WindowFlags flags = default;
		GraphicsDriver driver = GraphicsDriver.OpenGL46; // todo, dont hardcode
		switch(driver.GetDriver()){
			case GraphicsDriver.OpenGL: flags |= SDL_WindowFlags.SDL_WINDOW_OPENGL; break;
			case GraphicsDriver.Metal: flags |= SDL_WindowFlags.SDL_WINDOW_METAL; break;
			case GraphicsDriver.Vulkan: flags |= SDL_WindowFlags.SDL_WINDOW_VULKAN; break;
			default: throw new Exception("Need graphics driver support");
		}

		if (borderless) flags |= SDL_WindowFlags.SDL_WINDOW_BORDERLESS;

		window = new SDL3_Window(services).Create(title, width, height, flags);

		if (!windowed) {
			SetExclusiveFullscreenMode(width, height);
			SDL3.SDL_SetWindowFullscreen(window.HardwareHandle, true);
		}

#if WIN32
		// Hack to fix SDL/GL promoting borderless windows to exclusive fullscreen
		// when the resolution matches desktop
		// See https://github.com/libsdl-org/SDL/issues/12791
		// & https://github.com/libsdl-org/SDL/issues/12791#issuecomment-2792498618
		if (windowed && borderless) {
			SDL_PropertiesID props = SDL3.SDL_GetWindowProperties(window.HardwareHandle);
			nint hwnd;
			fixed (byte* name = SDL3.SDL_PROP_WINDOW_WIN32_HWND_POINTER)
				hwnd = SDL3.SDL_GetPointerProperty(props, name, IntPtr.Zero);
			SetWindowLongPtrW(hwnd, -16, 0x06000000 /* WS_OVERLAPPED|WS_CLIPSIBLINGS|WS_CLIPCHILDREN */);

			int x = 0, y = 0;
			SDL_Rect bounds;
			if (SDL3.SDL_GetDisplayBounds(SDL3.SDL_GetDisplayForWindow(window.HardwareHandle), &bounds)) {
				x = bounds.x + (bounds.w - width) / 2;
				y = bounds.y + (bounds.h - height) / 2;
			}
			SetWindowPos(hwnd, 0, x, y, width, height, 0x0260 /* FRAMECHANGED|NOOWNERZORDER|SHOWWINDOW */);
		}
#endif
		Rectangle windowRect = Rectangle.FromLTRB(
			0,
			0,
			width,
			height
		);

		CenterWindow(windowRect.Right - windowRect.Left, windowRect.Bottom - windowRect.Top);

		return true;
	}

#if WIN32
	[DllImport("user32.dll")] static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);
	[DllImport("user32.dll")] static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
#endif

	[UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
	static void* GL_ProcAddress(byte* proc) {
		return (void*)SDL3.SDL_GL_GetProcAddress(proc);
	}

	public void DestroyGameWindow() {
		SDL3.SDL_DestroyWindow(window.HardwareHandle);
	}

	public void DisplayedSize(out int width, out int height) {
		int w, h;
		SDL3.SDL_GetWindowSize(window.HardwareHandle, &w, &h);
		width = w;
		height = h;
	}

	public void GetMouseDelta(out int x, out int y, bool ignoreNextMouseDelta = false) {
		x = window.MouseXDelta;
		y = window.MouseYDelta;
		window.MouseXDelta = 0;
		window.MouseYDelta = 0;
	}

	public void GetNativeDisplayInfo(int nDisplay, out uint width, out uint height, out uint refreshHz) {
		width = 0;
		height = 0;
		refreshHz = 0;

		if (SDL3.SDL_WasInit((SDL_InitFlags)SDL3.SDL_INIT_VIDEO) == 0)
			return;

		int count;
		SDL_DisplayID* displays = SDL3.SDL_GetDisplays(&count);
		if (displays == null || nDisplay >= count)
			return;

		SDL_DisplayID display = nDisplay < 0 ? SDL3.SDL_GetPrimaryDisplay() : displays[nDisplay];
		SDL_DisplayMode* modePtr = SDL3.SDL_GetCurrentDisplayMode(display);

		if (modePtr == null)
			return;

		SDL_DisplayMode mode = *modePtr;

		width = (uint)mode.w;
		height = (uint)mode.h;
		refreshHz = (uint)mode.refresh_rate;
	}

	public bool IsWindowFullScreen() => (SDL3.SDL_GetWindowFlags(window.HardwareHandle) & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0;

	public bool MakeContextCurrent(nint context) {
		return false;
	}

	public void MoveWindow(int x, int y) {
		SDL3.SDL_SetWindowPosition(window.HardwareHandle, x, y);
	}

	int renderedWidth, renderedHeight;
	public void RenderedSize(bool set, ref int width, ref int height) {
		if (set) {
			renderedWidth = width;
			renderedHeight = height;
		}
		else {
			width = renderedWidth;
			height = renderedHeight;
		}
	}

	public void SetApplicationIcon(ReadOnlySpan<char> appIconFile) {
		throw new NotImplementedException();
	}

	public void SetWindowFullScreen(bool fullscreen, int width, int height) {
		if (fullscreen)
			SetExclusiveFullscreenMode(width, height);
		else
			SDL3.SDL_SetWindowFullscreenMode(window.HardwareHandle, null);

		SDL3.SDL_SetWindowFullscreen(window.HardwareHandle, fullscreen);
		SizeWindow(width, height);
	}

	private void SetExclusiveFullscreenMode(int width, int height) {
		SDL_DisplayID display = SDL3.SDL_GetDisplayForWindow(window.HardwareHandle);
		SDL_DisplayMode closest;
		if (SDL3.SDL_GetClosestFullscreenDisplayMode(display, width, height, 0, false, &closest))
			SDL3.SDL_SetWindowFullscreenMode(window.HardwareHandle, &closest);
	}

	public void SetWindowBordered(bool bordered) {
		SDL3.SDL_SetWindowBordered(window.HardwareHandle, bordered);
	}

	public void SizeWindow(int width, int tall) => SDL3.SDL_SetWindowSize(window.HardwareHandle, width, tall);

	public int GetDisplayModeCount(int displayIndex) {
		int count = 0;
		SDL_DisplayID* displaysPtr = SDL3.SDL_GetDisplays(&count);
		if (displaysPtr == null) return 0;

		if (displayIndex < 0 || displayIndex >= count)
			return 0;

		int modeCount = 0;
		SDL3.SDL_GetFullscreenDisplayModes(displaysPtr[displayIndex], &modeCount);
		return modeCount;
	}

	public void GetDisplayMode(int displayIndex, int modeIndex, out ShaderDisplayMode info) {
		info = default;
		int count = 0;
		SDL_DisplayID* displaysPtr = SDL3.SDL_GetDisplays(&count);
		if (displaysPtr == null) return;

		if (displayIndex < 0 || displayIndex >= count)
			return;

		int modeCount = 0;
		SDL_DisplayMode** modesPtr = SDL3.SDL_GetFullscreenDisplayModes(displaysPtr[displayIndex], &modeCount);
		if (modesPtr == null) return;

		if (modeIndex < 0 || modeIndex >= modeCount)
			return;

		SDL_DisplayMode* mode = modesPtr[modeIndex];
		info.Width = mode->w;
		info.Height = mode->h;
		info.Format = ImageFormat.RGBA8888;
		info.RefreshRateNumerator = (int)(mode->refresh_rate * 1000);
		info.RefreshRateDenominator = 1000;
	}

	public int GetCurrentDisplayIndex() {
		if (window == null) return 0;
		SDL_DisplayID displayID = SDL3.SDL_GetDisplayForWindow(window.HardwareHandle);
		int count = 0;
		SDL_DisplayID* displaysPtr = SDL3.SDL_GetDisplays(&count);
		if (displaysPtr == null) return 0;
		for (int i = 0; i < count; i++) {
			if (displaysPtr[i] == displayID)
				return i;
		}
		return 0;
	}

	public IWindow GetWindow() => window;
	public void PumpWindowsMessageLoop() => window?.PumpMessages();
	public int GetEvents(WindowEvent[] eventBuffer, int length) => window.GetEvents(eventBuffer, length);

	public void CenterWindow(int width, int height) {
		SDL3.SDL_SetWindowPosition(window.HardwareHandle, (int)SDL3.SDL_WINDOWPOS_CENTERED, (int)SDL3.SDL_WINDOWPOS_CENTERED);
	}

	public bool PrepareContext(GraphicsDriver driver) {
		if (0 != (driver & GraphicsDriver.OpenGL)) {
			switch ((driver & ~GraphicsDriver.OpenGL)) {
				default:
					Error("Cannot support this OpenGL version");
					return false;
				case (GraphicsDriver)460: {
						if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 4))
							Error($"GL Context: Bad major version... {SDL3.SDL_GetError()}");
						if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 6))
							Error($"GL Context: Bad minor version... {SDL3.SDL_GetError()}");
					}
					break;
			}

			if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_DEPTH_SIZE, 24))
				Error("GL Context: Bad depth request...");
			if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_STENCIL_SIZE, 8))
				Error("GL Context: Bad stencil request...");
			if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_DOUBLEBUFFER, 1))
				Error("GL Context: Can't double buffer?");
			if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_MULTISAMPLEBUFFERS, 1))
				Error("GL Context: Can't double buffer?");
			if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_MULTISAMPLESAMPLES, 4))
				Error("GL Context: Can't double buffer?");

			return true;
		}
		return false;
	}

	public IGraphicsContext? CreateContext(in ShaderDeviceInfo deviceInfo, IWindow? window = null) {
		IGraphicsContext? gfx;

		window = window == null ? this.window : window;
		if (0 != (deviceInfo.Driver & GraphicsDriver.OpenGL)) {
			nint handle = (nint)((SDL3_Window)window).HardwareHandle;
			nint ctx = (nint)SDL3.SDL_GL_CreateContext((SDL_Window*)handle);
			SDL3.SDL_GL_SetSwapInterval(0);
			gfx = new SDL3_OpenGL46_Context(handle, ctx);
			return gfx;
		}

		Warning("Cannot support this graphics API\n");
		return null;
	}

	public delegate* unmanaged[Cdecl]<byte*, void*> GL_LoadExtensionsPtr() => &GL_ProcAddress;

	ICursor[] DefaultCursors;

	public ICursor? GetHardwareCursor(HCursor cursor) {
		if (cursor <= 0) return null;
		if (cursor >= (int)CursorCode.Last) return null;
		return DefaultCursors[cursor];
	}

	public ICursor? GetSoftwareCursor(HCursor cursor, out float x, out float y) {
		x = y = 0;
		return null;
	}

	[MemberNotNull(nameof(DefaultCursors))]
	void InitCursors() {
		DefaultCursors = new ICursor[(int)CursorCode.Last];

		DefaultCursors[(nint)CursorCode.Arrow] = new SDL3_Cursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_DEFAULT);
		DefaultCursors[(nint)CursorCode.IBeam] = new SDL3_Cursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_TEXT);
		DefaultCursors[(nint)CursorCode.Hourglass] = new SDL3_Cursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_WAIT);
		DefaultCursors[(nint)CursorCode.Crosshair] = new SDL3_Cursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_CROSSHAIR);
		DefaultCursors[(nint)CursorCode.WaitArrow] = new SDL3_Cursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_PROGRESS);
		DefaultCursors[(nint)CursorCode.SizeNWSE] = new SDL3_Cursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_NWSE_RESIZE);
		DefaultCursors[(nint)CursorCode.SizeNESW] = new SDL3_Cursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_NESW_RESIZE);
		DefaultCursors[(nint)CursorCode.SizeWE] = new SDL3_Cursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_EW_RESIZE);
		DefaultCursors[(nint)CursorCode.SizeNS] = new SDL3_Cursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_NS_RESIZE);
		DefaultCursors[(nint)CursorCode.SizeAll] = new SDL3_Cursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_MOVE);
		DefaultCursors[(nint)CursorCode.No] = new SDL3_Cursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_NOT_ALLOWED);
		DefaultCursors[(nint)CursorCode.Hand] = new SDL3_Cursor(SDL_SystemCursor.SDL_SYSTEM_CURSOR_POINTER);

		DefaultCursors[(nint)CursorCode.Arrow].Activate();
	}
	ICursor? lastCursor;
	public void SetMouseCursor(ICursor? currentlySetCursor) {
		lastCursor = currentlySetCursor;
		currentlySetCursor?.Activate();
	}

	public void SetMouseVisible(bool state) {
		if (state) {
			SDL3.SDL_ShowCursor();
			lastCursor?.Activate();
		}
		else {
			SDL3.SDL_HideCursor();
		}
	}

	public void SetWindowRelativeMouseMode(bool cursorLocked) {
		SDL3.SDL_SetWindowRelativeMouseMode(window.HardwareHandle, cursorLocked);
	}

	public void FlashWindow(bool state) {
		SDL3.SDL_FlashWindow(window.HardwareHandle, state ? SDL_FlashOperation.SDL_FLASH_UNTIL_FOCUSED : SDL_FlashOperation.SDL_FLASH_CANCEL);
	}
}

public unsafe class SDL3_Cursor : ICursor
{
	SDL_Cursor* cursorPtr;
	public SDL3_Cursor(SDL_SystemCursor cursorID) {
		cursorPtr = SDL3.SDL_CreateSystemCursor(cursorID);
	}
	public void Activate() {
		SDL3.SDL_SetCursor(cursorPtr);
	}
}
