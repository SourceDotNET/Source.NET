using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;

using System.Drawing;

namespace Source.Engine;


public class VideoMode_Common(Sys Sys, IServiceProvider services, IFileSystem fileSystem, IMaterialSystem materials, RenderUtils renderUtils, ICommandLine CommandLine) : IVideoMode
{
	public const int MAX_MODE_LIST = 512;
	const int VIDEO_MODE_DEFAULT = -1;
	const int VIDEO_MODE_REQUESTED_WINDOW_SIZE = -2;
	const int CUSTOM_VIDEO_MODES = 2;

	bool Borderless;
	bool Windowed;
	bool ClientViewRectDirty = true;
	int ModeWidth;
	int ModeHeight;
	int ModeBPP;
	int UIWidth;
	int UIHeight;
	int StereoWidth;
	int StereoHeight;

	bool Initialized;
	public bool PlayedStartupVideo;

	// View rects. Never shrinks
	readonly ViewRects ClientViewRect = new([new()]);

	public virtual bool Init() => true;

	public ViewRects GetClientViewRect() {
		RecomputeClientViewRect();
		return ClientViewRect;
	}

	private void RecomputeClientViewRect() {
		if (!InEditMode()) {
			if (!ClientViewRectDirty)
				return;
		}

		ClientViewRectDirty = false;

		MatRenderContextPtr renderContext = new(materials);

		renderContext.GetRenderTargetDimensions(out int width, out int height);
		ref ViewRect viewrect = ref ClientViewRect[0];
		viewrect.X = 0;
		viewrect.Y = 0;
		viewrect.Width = width;
		viewrect.Height = height;

		if (width == 0 || height == 0)
			ClientViewRectDirty = true;
	}

	public void ResetCurrentModeForNewResolution(int width, int height, bool windowed, bool borderless) {
		Borderless = borderless;
		Windowed = windowed;
		ModeWidth = width;
		ModeHeight = height;
		UIWidth = width;
		UIHeight = height;
		StereoWidth = width;
		StereoHeight = height;
	}

	public bool IsWindowedMode() => Windowed;
	public bool IsBorderlessMode() => Borderless;
	public int GetModeWidth() => ModeWidth;
	public int GetModeHeight() => ModeHeight;
	public int GetModeBPP() => ModeBPP;
	public int GetModeStereoWidth() => StereoWidth;
	public int GetModeStereoHeight() => StereoHeight;
	public int GetModeUIWidth() => UIWidth;
	public int GetModeUIHeight() => UIHeight;

	public void AdjustWindow(int width, int height, int bpp, bool windowed, bool borderless) {
		IGame game = services.GetRequiredService<IGame>();
		ILauncherManager launcherMgr = services.GetRequiredService<ILauncherManager>();

		Rectangle windowRect = Rectangle.FromLTRB(
			0,
			0,
			width,
			height
			);

		game.SetWindowSize(width, height);
		if (windowed || borderless) {
			launcherMgr.SizeWindow(width, height);
			launcherMgr.CenterWindow(windowRect.Right - windowRect.Left, windowRect.Bottom - windowRect.Top);
		}
		MarkClientViewRectDirty();
	}

	public void MarkClientViewRectDirty() => ClientViewRectDirty = true;

	public bool CreateGameWindow(in UserVideoMode videoMode) {
		if (!InEditMode()) {
			ResetCurrentModeForNewResolution(videoMode.Width, videoMode.Height, videoMode.Windowed, videoMode.Borderless);

			// Aggggghhh i hate this
			services.GetRequiredService<IGraphicsProvider>().PrepareContext(services.GetRequiredService<MaterialSystem_Config>().Driver);
			IGame game = services.GetRequiredService<IGame>();

			Common.TimestampedLog("CreateGameWindow - Start");
			if (!game.CreateGameWindow(videoMode.Width, videoMode.Height, videoMode.Windowed, videoMode.Borderless))
				return false;
			Common.TimestampedLog("CreateGameWindow - Finish");

			AdjustWindow(GetModeWidth(), GetModeHeight(), GetModeBPP(), IsWindowedMode(), IsBorderlessMode());

			Common.TimestampedLog("SetMode - Start");
			if (!SetMode(in videoMode))
				return false;
			Common.TimestampedLog("SetMode - Finish");

			Common.TimestampedLog("DrawStartupGraphic - Start");
			DrawStartupGraphic();
			Common.TimestampedLog("DrawStartupGraphic - Finish");
		}

		return true;
	}

	public void DrawStartupGraphic() {
		bool debugstartup = CommandLine.FindParm("-debugstartupscreen") > 0;
		SetupStartupGraphic();

		if (backgroundTexture == null)
			return;

		using MatRenderContextPtr renderContext = new(materials);

		Span<char> startupGraphicName = stackalloc char[MAX_PATH];
		// ComputeStartupGraphicName(startupGraphicName);

		if (debugstartup || true)
			"console/background01.vtf".CopyTo(startupGraphicName);

		KeyValues keyValues = new KeyValues("UnlitGeneric");
		keyValues.SetString("$basetexture", startupGraphicName);
		keyValues.SetInt("$ignorez", 1);
		keyValues.SetInt("$nofog", 1);
		keyValues.SetInt("$no_fullbright", 1);
		keyValues.SetInt("$nocull", 1);
		IMaterial material = materials.CreateMaterial("__background", keyValues);

		keyValues = new KeyValues("UnlitGeneric");
		keyValues.SetString("$basetexture", "console/startup_loading.vtf");
		keyValues.SetInt("$translucent", 1);
		keyValues.SetInt("$ignorez", 1);
		keyValues.SetInt("$nofog", 1);
		keyValues.SetInt("$no_fullbright", 1);
		keyValues.SetInt("$nocull", 1);
		IMaterial loadingMaterial = materials.CreateMaterial("__loading", keyValues);

		int w = GetModeStereoWidth();
		int h = GetModeStereoHeight();
		int tw = backgroundTexture!.Width();
		int th = backgroundTexture!.Height();
		int lw = loadingTexture!.Width();
		int lh = loadingTexture!.Height();


		if (false && debugstartup) {
			for (int repeat = 0; repeat < 100000; repeat++) {
				renderContext.Viewport(0, 0, w, h);
				renderContext.DepthRange(0, 1);
				renderContext.ClearColor3ub(0, (byte)((repeat & 0x7) << 3), 0);
				renderContext.ClearBuffers(true, true, true);

				if (true)  // draw normal BK
				{
					float depth = 0.55f;
					int slide = (repeat) % 200; // 100 down and 100 up
					if (slide > 100) {
						slide = 200 - slide;        // aka 100-(slide-100).
					}

					// stop sliding about
					slide = 0;

					renderUtils.DrawScreenSpaceRectangle(material, 0, 0 + slide, w, h - 50, 0, 0, tw - 1, th - 1, tw, th, null, 1, 1, depth);
					renderUtils.DrawScreenSpaceRectangle(loadingMaterial, w - lw, h - lh + slide / 2, lw, lh, 0, 0, lw - 1, lh - 1, lw, lh, null, 1, 1, depth - 0.1f);
				}

				if (true) {
					// draw a grid too
					int grid_size = 8;
					float depthacc = 0.0f;
					float depthinc = 1.0f / ((grid_size * grid_size) + 1);

					for (int x = 0; x < grid_size; x++) {
						float cornerx = x * 20.0f;

						for (int y = 0; y < grid_size; y++) {
							float cornery = ((float)y) * 20.0f;

							renderUtils.DrawScreenSpaceRectangle(material, 10 + (int)cornerx, 10 + (int)cornery, 15, 15, 0, 0, tw - 1, th - 1, tw, th, null, 1, 1, depthacc);

							depthacc += depthinc;
						}
					}
				}

				materials.SwapBuffers();
			}
		}
		else {
			renderContext.Viewport(0, 0, w, h);
			renderContext.DepthRange(0, 1);
			// SetToneMappingScaleLinear - what does it do... (in this context)
			// I guess it just sets it to 1, 1, 1 but still, need to review how we'd even replicate tone mapping 
			float depth = 0.5f;

#if !SWDS
			renderUtils.DrawScreenSpaceRectangle(material, 0, 0, w, h, 0, 0, tw - 1, th - 1, tw, th, null, 1, 1, depth);
			renderUtils.DrawScreenSpaceRectangle(loadingMaterial, w - lw, h - lh, lw, lh, 0, 0, lw - 1, lh - 1, lw, lh, null, 1, 1, depth);
			Singleton<IShaderSystem>().WaitForPendingCompiles(null);
#endif

			for (int i = 0; i < 2; i++) {
				renderContext.ClearColor3ub(0, 0, 0);
				renderContext.ClearBuffers(true, true, true);
				renderUtils.DrawScreenSpaceRectangle(material, 0, 0, w, h, 0, 0, tw - 1, th - 1, tw, th, null, 1, 1, depth);
				renderUtils.DrawScreenSpaceRectangle(loadingMaterial, w - lw, h - lh, lw, lh, 0, 0, lw - 1, lh - 1, lw, lh, null, 1, 1, depth);
				materials.SwapBuffers();
			}
		}
	}

	IVTFTexture? backgroundTexture;
	IVTFTexture? loadingTexture;

	private void SetupStartupGraphic() {
		string material = "materials/console/background01.vtf";
		backgroundTexture = LoadVTF(material);
		if (backgroundTexture == null) {
			Error($"Can't find background image '{material}'\n");
			return;
		}

		loadingTexture = LoadVTF("materials/console/startup_loading.vtf");
		if (loadingTexture == null) {
			Error($"Can't find background image materials/console/startup_loading.vtf\n");
			return;
		}
	}

	private IVTFTexture? LoadVTF(string material) {
		using IFileHandle? handle = fileSystem.Open(material, FileOpenOptions.Read);
		if (handle != null) {
			IVTFTexture texture = IVTFTexture.Create();
			if (!texture.Unserialize(handle)) {
				Error($"Invalid or corrupt texture {material}\n");
			}
			// FIXME texture.ConvertImageFormat(ImageFormat.RGBA8888, false);
			return texture;
		}

		return null;
	}

	public void SetGameWindow(nint window) {
		throw new NotImplementedException();
	}

	public virtual bool SetMode(in UserVideoMode videoMode) => false;

	public void SetInitialized(bool initialized) => Initialized = initialized;
	public bool GetInitialized() => Initialized;
}
public class VideoMode_MaterialSystem(Sys Sys, IMaterialSystem materials, IGame game, IServiceProvider services, IFileSystem fileSystem, RenderUtils renderUtils, ICommandLine commandLine, MatSysInterface matSys)
	: VideoMode_Common(Sys, services, fileSystem, materials, renderUtils, commandLine)
{
	MaterialSystem_Config config = services.GetRequiredService<MaterialSystem_Config>();

	bool SetModeOnce;

#if WIN32
	int LastCDSWidth = 0;
	int LastCDSHeight = 0;
	int LastCDSBPP = 0;
	int LastCDSFreq = 0;
#endif

	public override bool Init() {
		SetModeOnce = false;
		PlayedStartupVideo = false;

		int bitsPerPixel = 32;
		int adapter = (int)materials.GetCurrentAdapter();

		game.GetDesktopInfo(out uint desktopWidth, out uint desktopHeight, out uint desktopRefresh);

		materials.AddModeChangeCallBack(AdjustForModeChange);
		SetInitialized(true);

		return true;
	}

	public override bool SetMode(in UserVideoMode mode) {
		MaterialSystem_Config newConfig = new();
		config.CopyInstantiatedReferenceTo(newConfig);

		newConfig.VideoMode.Width = mode.Width;
		newConfig.VideoMode.Height = mode.Height;

#if SWDS
		newConfig.VideoMode.RefreshRate = 60;
#else
		newConfig.VideoMode.RefreshRate = mode.RefreshRate;
#endif

		newConfig.SetFlag(MaterialSystem_Config_Flags.Windowed, mode.Windowed);
		newConfig.SetFlag(MaterialSystem_Config_Flags.NoWindowBorder, mode.Borderless);

		if (!SetModeOnce) {
			if (!materials.SetMode(game.GetMainDeviceWindow(), newConfig))
				return false;

			newConfig.CopyInstantiatedReferenceTo(config);
			SetModeOnce = true;

			return true;
		}

		matSys.OverrideMaterialSystemConfig(newConfig);

		return true;
	}

	void AdjustForModeChange() {
		int oldUIWidth = GetModeUIWidth();
		int oldUIHeight = GetModeUIHeight();

		int newWidth = config.VideoMode.Width;
		int newHeight = config.VideoMode.Height;
		bool windowed = config.Windowed();
		bool borderless = config.NoWindowBorder();

		using MatRenderContextPtr renderContext = new(materials);

		ResetCurrentModeForNewResolution(newWidth, newHeight, windowed, borderless);
		AdjustWindow(GetModeWidth(), GetModeHeight(), GetModeBPP(), IsWindowedMode(), IsBorderlessMode());
		MarkClientViewRectDirty();
		renderContext.Viewport(0, 0, GetModeStereoWidth(), GetModeStereoHeight());
#if !SWDS
		surface.OnScreenSizeChanged(oldUIWidth, oldUIHeight);
#endif
		g_ClientDLL!.HudVidInit();
	}

	void SetGameWindow() {

	}

	void ReleaseVideo() {
		if (IsWindowedMode())
			return;

		ReleaseFullScreen();
	}

	void RestoreVideo() {

	}

	void ReleaseFullScreen() {

	}

	void ChangeDisplaySettingsToFullscreen() {

	}

	void ReadScreenPixels() {

	}
}
