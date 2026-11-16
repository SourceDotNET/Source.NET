using SDL;

using Source.Common.Launcher;

using System.Runtime.InteropServices;

namespace Source.SDLManager;

public unsafe class SDL3_MesaEGL_Context : IGraphicsContext
{
	const int EGL_NONE = 0x3038;
	const int EGL_RED_SIZE = 0x3024, EGL_GREEN_SIZE = 0x3023, EGL_BLUE_SIZE = 0x3022, EGL_ALPHA_SIZE = 0x3021;
	const int EGL_DEPTH_SIZE = 0x3025, EGL_STENCIL_SIZE = 0x3026;
	const int EGL_SURFACE_TYPE = 0x3033, EGL_PBUFFER_BIT = 0x0001;
	const int EGL_RENDERABLE_TYPE = 0x3040, EGL_OPENGL_BIT = 0x0008;
	const int EGL_WIDTH = 0x3057, EGL_HEIGHT = 0x3056;
	const int EGL_OPENGL_API = 0x30A2;
	const int EGL_CONTEXT_MAJOR_VERSION = 0x3098, EGL_CONTEXT_MINOR_VERSION = 0x30FB;
	const int EGL_CONTEXT_OPENGL_PROFILE_MASK = 0x30FD, EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT = 0x1;
	const int EGL_PLATFORM_SURFACELESS_MESA = 0x31DD;

	const int GL_PIXEL_PACK_BUFFER = 0x88EB, GL_PIXEL_PACK_BUFFER_BINDING = 0x88ED;
	const int GL_READ_FRAMEBUFFER = 0x8CA8, GL_READ_FRAMEBUFFER_BINDING = 0x8CAA;
	const int GL_READ_BUFFER = 0x0C02, GL_BACK = 0x0405;
	const int GL_PACK_ALIGNMENT = 0x0D05, GL_PACK_ROW_LENGTH = 0x0D02;
	const int GL_RGBA = 0x1908, GL_UNSIGNED_BYTE = 0x1401;
	const int GL_STREAM_READ = 0x88E1, GL_MAP_READ_BIT = 0x0001;
	const int GL_SYNC_GPU_COMMANDS_COMPLETE = 0x9117;
	const ulong GL_TIMEOUT_IGNORED = ulong.MaxValue;

	static delegate* unmanaged[Cdecl]<byte*, void*> eglGetProcAddress;
	static delegate* unmanaged[Cdecl]<int, nint, int*, nint> eglGetPlatformDisplayEXT;
	static delegate* unmanaged[Cdecl]<nint, int*, int*, uint> eglInitialize;
	static delegate* unmanaged[Cdecl]<int, uint> eglBindAPI;
	static delegate* unmanaged[Cdecl]<nint, int*, nint*, int, int*, uint> eglChooseConfig;
	static delegate* unmanaged[Cdecl]<nint, nint, nint, int*, nint> eglCreateContext;
	static delegate* unmanaged[Cdecl]<nint, nint, int*, nint> eglCreatePbufferSurface;
	static delegate* unmanaged[Cdecl]<nint, nint, uint> eglDestroySurface;
	static delegate* unmanaged[Cdecl]<nint, nint, nint, nint, uint> eglMakeCurrent;
	static delegate* unmanaged[Cdecl]<int> eglGetError;

	static delegate* unmanaged[Cdecl]<int, int*, void> glGetIntegerv;
	static delegate* unmanaged[Cdecl]<int, uint, void> glBindFramebuffer;
	static delegate* unmanaged[Cdecl]<int, void> glReadBuffer;
	static delegate* unmanaged[Cdecl]<int, int, void> glPixelStorei;
	static delegate* unmanaged[Cdecl]<int, uint*, void> glGenBuffers;
	static delegate* unmanaged[Cdecl]<int, uint, void> glBindBuffer;
	static delegate* unmanaged[Cdecl]<int, nint, void*, int, void> glBufferData;
	static delegate* unmanaged[Cdecl]<int, int, int, int, int, int, void*, void> glReadPixels;
	static delegate* unmanaged[Cdecl]<int, int, nint> glFenceSync;
	static delegate* unmanaged[Cdecl]<nint, void> glDeleteSync;
	static delegate* unmanaged[Cdecl]<nint, int, ulong, int> glClientWaitSync;
	static delegate* unmanaged[Cdecl]<void> glFlush;
	static delegate* unmanaged[Cdecl]<int, nint, nint, int, void*> glMapBufferRange;
	static delegate* unmanaged[Cdecl]<int, byte> glUnmapBuffer;

	[DllImport("libc", EntryPoint = "setenv")]
	static extern int setenv([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int overwrite);

	public static string MesaLibDirectory => Path.Combine(AppContext.BaseDirectory, "mesa", "lib");

	readonly SDL_Window* window;
	readonly Func<(int Width, int Height)> targetSize;
	nint display, config, context, surface;
	int surfaceWidth, surfaceHeight;

	SDL_Renderer* renderer;
	SDL_Texture* texture;
	int textureWidth, textureHeight;

	readonly uint[] pbos = new uint[2];
	readonly nint[] fences = new nint[2];
	readonly (int Width, int Height)[] pboSizes = new (int, int)[2];
	uint frameIndex;

	SDL3_MesaEGL_Context(SDL_Window* window, Func<(int, int)> targetSize) {
		this.window = window;
		this.targetSize = targetSize;
	}

	public nint HardwareHandle => context;

	public static SDL3_MesaEGL_Context? Create(SDL_Window* window, int major, int minor, Func<(int Width, int Height)> targetSize) {
		if (!LoadEGL())
			return null;

		SDL3_MesaEGL_Context ctx = new(window, targetSize);
		return ctx.Init(major, minor) ? ctx : null;
	}

	static bool LoadEGL() {
		if (eglGetProcAddress != null)
			return true;

		string dir = MesaLibDirectory;
		setenv("GALLIUM_DRIVER", "zink", 0);
		setenv("VK_DRIVER_FILES", Path.Combine(dir, "kosmickrisp_icd.json"), 0);
		setenv("MESA_GL_VERSION_OVERRIDE", "4.6", 0);
		setenv("MESA_GLSL_VERSION_OVERRIDE", "460", 0);

		if (!NativeLibrary.TryLoad(Path.Combine(dir, "libEGL.1.dylib"), out nint lib)) {
			Warning($"Could not load Mesa's libEGL from {dir}. Install it with 'brew install mesa vulkan-loader' and rebuild.\n");
			return false;
		}

		eglGetProcAddress = (delegate* unmanaged[Cdecl]<byte*, void*>)NativeLibrary.GetExport(lib, "eglGetProcAddress");
		eglGetPlatformDisplayEXT = (delegate* unmanaged[Cdecl]<int, nint, int*, nint>)Proc("eglGetPlatformDisplayEXT");
		eglInitialize = (delegate* unmanaged[Cdecl]<nint, int*, int*, uint>)Proc("eglInitialize");
		eglBindAPI = (delegate* unmanaged[Cdecl]<int, uint>)Proc("eglBindAPI");
		eglChooseConfig = (delegate* unmanaged[Cdecl]<nint, int*, nint*, int, int*, uint>)Proc("eglChooseConfig");
		eglCreateContext = (delegate* unmanaged[Cdecl]<nint, nint, nint, int*, nint>)Proc("eglCreateContext");
		eglCreatePbufferSurface = (delegate* unmanaged[Cdecl]<nint, nint, int*, nint>)Proc("eglCreatePbufferSurface");
		eglDestroySurface = (delegate* unmanaged[Cdecl]<nint, nint, uint>)Proc("eglDestroySurface");
		eglMakeCurrent = (delegate* unmanaged[Cdecl]<nint, nint, nint, nint, uint>)Proc("eglMakeCurrent");
		eglGetError = (delegate* unmanaged[Cdecl]<int>)Proc("eglGetError");
		return true;
	}

	static void* Proc(string name) {
		byte* str = (byte*)Marshal.StringToCoTaskMemUTF8(name);
		void* ptr = eglGetProcAddress(str);
		Marshal.FreeCoTaskMem((nint)str);
		if (ptr == null)
			throw new EntryPointNotFoundException($"Mesa EGL is missing {name}");
		return ptr;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
	internal static void* GetProcAddress(byte* name) => eglGetProcAddress(name);

	bool Init(int major, int minor) {
		display = eglGetPlatformDisplayEXT(EGL_PLATFORM_SURFACELESS_MESA, 0, null);
		int vmaj, vmin;
		if (display == 0 || eglInitialize(display, &vmaj, &vmin) == 0) {
			Warning($"Mesa EGL: could not initialize the surfaceless display (0x{eglGetError():X})\n");
			return false;
		}
		eglBindAPI(EGL_OPENGL_API);

		int* cfgAttrs = stackalloc int[] {
			EGL_SURFACE_TYPE, EGL_PBUFFER_BIT,
			EGL_RENDERABLE_TYPE, EGL_OPENGL_BIT,
			EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8, EGL_BLUE_SIZE, 8, EGL_ALPHA_SIZE, 8,
			EGL_DEPTH_SIZE, 24, EGL_STENCIL_SIZE, 8,
			EGL_NONE
		};
		nint cfg;
		int numConfigs;
		if (eglChooseConfig(display, cfgAttrs, &cfg, 1, &numConfigs) == 0 || numConfigs < 1) {
			Warning($"Mesa EGL: no matching config (0x{eglGetError():X})\n");
			return false;
		}
		config = cfg;

		int* ctxAttrs = stackalloc int[] {
			EGL_CONTEXT_MAJOR_VERSION, major,
			EGL_CONTEXT_MINOR_VERSION, minor,
			EGL_CONTEXT_OPENGL_PROFILE_MASK, EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT,
			EGL_NONE
		};
		context = eglCreateContext(display, config, 0, ctxAttrs);
		if (context == 0) {
			Warning($"Mesa EGL: could not create a GL {major}.{minor} core context (0x{eglGetError():X})\n");
			return false;
		}

		(int w, int h) = targetSize();
		if (!CreateSurface(w, h))
			return false;

		renderer = SDL3.SDL_CreateRenderer(window, "metal");
		if (renderer == null) {
			Warning($"Mesa EGL: could not create the SDL Metal renderer: {SDL3.SDL_GetError()}\n");
			return false;
		}

		glGetIntegerv = (delegate* unmanaged[Cdecl]<int, int*, void>)Proc("glGetIntegerv");
		glBindFramebuffer = (delegate* unmanaged[Cdecl]<int, uint, void>)Proc("glBindFramebuffer");
		glReadBuffer = (delegate* unmanaged[Cdecl]<int, void>)Proc("glReadBuffer");
		glPixelStorei = (delegate* unmanaged[Cdecl]<int, int, void>)Proc("glPixelStorei");
		glGenBuffers = (delegate* unmanaged[Cdecl]<int, uint*, void>)Proc("glGenBuffers");
		glBindBuffer = (delegate* unmanaged[Cdecl]<int, uint, void>)Proc("glBindBuffer");
		glBufferData = (delegate* unmanaged[Cdecl]<int, nint, void*, int, void>)Proc("glBufferData");
		glReadPixels = (delegate* unmanaged[Cdecl]<int, int, int, int, int, int, void*, void>)Proc("glReadPixels");
		glFenceSync = (delegate* unmanaged[Cdecl]<int, int, nint>)Proc("glFenceSync");
		glDeleteSync = (delegate* unmanaged[Cdecl]<nint, void>)Proc("glDeleteSync");
		glClientWaitSync = (delegate* unmanaged[Cdecl]<nint, int, ulong, int>)Proc("glClientWaitSync");
		glFlush = (delegate* unmanaged[Cdecl]<void>)Proc("glFlush");
		glMapBufferRange = (delegate* unmanaged[Cdecl]<int, nint, nint, int, void*>)Proc("glMapBufferRange");
		glUnmapBuffer = (delegate* unmanaged[Cdecl]<int, byte>)Proc("glUnmapBuffer");
		return true;
	}

	bool CreateSurface(int width, int height) {
		width = Math.Max(width, 1);
		height = Math.Max(height, 1);

		int* attrs = stackalloc int[] { EGL_WIDTH, width, EGL_HEIGHT, height, EGL_NONE };
		nint newSurface = eglCreatePbufferSurface(display, config, attrs);
		if (newSurface == 0) {
			Warning($"Mesa EGL: could not create a {width}x{height} pbuffer (0x{eglGetError():X})\n");
			return false;
		}

		DevMsg($"Mesa EGL: framebuffer {width}x{height}\n");
		nint oldSurface = surface;
		surface = newSurface;
		surfaceWidth = width;
		surfaceHeight = height;

		eglMakeCurrent(display, surface, surface, context);
		if (oldSurface != 0)
			eglDestroySurface(display, oldSurface);
		return true;
	}

	public void MakeCurrent() {
		if (eglMakeCurrent(display, surface, surface, context) == 0)
			Warning($"Could not make GL context current (0x{eglGetError():X})\n");
	}

	public void ReleaseCurrent() {
		if (eglMakeCurrent(display, 0, 0, 0) == 0)
			Warning($"Could not release GL context (0x{eglGetError():X})\n");
	}

	public void SetSwapInterval(float swapInterval) {
		if (!SDL3.SDL_SetRenderVSync(renderer, swapInterval != 0 ? 1 : 0))
			Warning($"Could not set vsync: {SDL3.SDL_GetError()}\n");
	}

	public void SwapBuffers() {
		int prevPackBuffer, prevReadFramebuffer, prevReadBuffer, prevAlignment, prevRowLength;
		glGetIntegerv(GL_PIXEL_PACK_BUFFER_BINDING, &prevPackBuffer);
		glGetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &prevReadFramebuffer);
		glGetIntegerv(GL_PACK_ALIGNMENT, &prevAlignment);
		glGetIntegerv(GL_PACK_ROW_LENGTH, &prevRowLength);

		glBindFramebuffer(GL_READ_FRAMEBUFFER, 0);
		glGetIntegerv(GL_READ_BUFFER, &prevReadBuffer);
		glReadBuffer(GL_BACK);
		glPixelStorei(GL_PACK_ALIGNMENT, 4);
		glPixelStorei(GL_PACK_ROW_LENGTH, 0);

		if (pbos[0] == 0)
			fixed (uint* p = pbos)
				glGenBuffers(pbos.Length, p);

		int cur = (int)(frameIndex % 2);
		int prev = (int)((frameIndex + 1) % 2);

		glBindBuffer(GL_PIXEL_PACK_BUFFER, pbos[cur]);
		if (pboSizes[cur] != (surfaceWidth, surfaceHeight)) {
			glBufferData(GL_PIXEL_PACK_BUFFER, (nint)surfaceWidth * surfaceHeight * 4, null, GL_STREAM_READ);
			pboSizes[cur] = (surfaceWidth, surfaceHeight);
		}
		glReadPixels(0, 0, surfaceWidth, surfaceHeight, GL_RGBA, GL_UNSIGNED_BYTE, null);

		if (fences[cur] != 0)
			glDeleteSync(fences[cur]);
		fences[cur] = glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
		glFlush();

		(int w, int h) = pboSizes[prev];
		if (fences[prev] != 0 && w > 0 && h > 0) {
			glClientWaitSync(fences[prev], 0, GL_TIMEOUT_IGNORED);
			glDeleteSync(fences[prev]);
			fences[prev] = 0;

			glBindBuffer(GL_PIXEL_PACK_BUFFER, pbos[prev]);
			void* data = glMapBufferRange(GL_PIXEL_PACK_BUFFER, 0, (nint)w * h * 4, GL_MAP_READ_BIT);
			if (data != null) {
				UploadFrame(data, w, h);
				glUnmapBuffer(GL_PIXEL_PACK_BUFFER);
			}
		}

		glBindBuffer(GL_PIXEL_PACK_BUFFER, (uint)prevPackBuffer);
		glReadBuffer(prevReadBuffer);
		glPixelStorei(GL_PACK_ALIGNMENT, prevAlignment);
		glPixelStorei(GL_PACK_ROW_LENGTH, prevRowLength);
		glBindFramebuffer(GL_READ_FRAMEBUFFER, (uint)prevReadFramebuffer);

		if (texture != null) {
			SDL3.SDL_RenderTextureRotated(renderer, texture, null, null, 0, null, SDL_FlipMode.SDL_FLIP_VERTICAL);
			SDL3.SDL_RenderPresent(renderer);
		}

		frameIndex++;

		(int tw, int th) = targetSize();
		if (tw > 0 && th > 0 && (tw != surfaceWidth || th != surfaceHeight))
			CreateSurface(tw, th);
	}

	void UploadFrame(void* data, int width, int height) {

		if (texture == null || textureWidth != width || textureHeight != height) {
			if (texture != null)
				SDL3.SDL_DestroyTexture(texture);
			texture = SDL3.SDL_CreateTexture(renderer, SDL_PixelFormat.SDL_PIXELFORMAT_XBGR8888, SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, width, height);
			if (texture == null)
				return;
			SDL3.SDL_SetTextureBlendMode(texture, SDL3.SDL_BLENDMODE_NONE);
			textureWidth = width;
			textureHeight = height;
		}

		nint pixels;
		int pitch;
		if (!SDL3.SDL_LockTexture(texture, null, &pixels, &pitch))
			return;

		int rowBytes = width * 4;
		if (pitch == rowBytes)
			Buffer.MemoryCopy(data, (void*)pixels, (long)rowBytes * height, (long)rowBytes * height);
		else
			for (int y = 0; y < height; y++)
				Buffer.MemoryCopy((byte*)data + (long)y * rowBytes, (byte*)pixels + (long)y * pitch, pitch, rowBytes);

		SDL3.SDL_UnlockTexture(texture);
	}
}
