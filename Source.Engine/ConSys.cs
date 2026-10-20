using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.GUI;
using Source.Common.Networking;
using Source.Engine.Client;
using Source.GUI.Controls;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

using MemoryExtensions = System.MemoryExtensions;

namespace Source.Engine;


public static class ConsoleCVars
{
	internal static ConVar con_trace = new("con_trace", "0", FCvar.MaterialSystemThread, "Print console text to low level printout.");
	internal static ConVar con_notifytime = new("con_notifytime", "8", FCvar.MaterialSystemThread, "How long to display recent console text to the upper part of the game window");
	internal static ConVar con_times = new("contimes", "8", FCvar.MaterialSystemThread, "Number of console lines to overlay for debugging.");
#if !SWDS
	internal static ConVar con_drawnotify = new("con_drawnotify", "1", 0, "Disables drawing of notification area (for taking screenshots).");
	internal static ConVar con_enable = new("con_enable", "1", FCvar.Archive, "Allows the console to be activated.");
#endif
	internal static ConVar con_filter_enable = new("con_filter_enable", "0", FCvar.MaterialSystemThread, "Filters console output based on the setting of con_filter_text. 1 filters completely, 2 displays filtered text brighter than other text.");
	internal static ConVar con_filter_text = new("con_filter_text", "", FCvar.MaterialSystemThread, "Text with which to filter console spew. Set con_filter_enable 1 or 2 to activate.");
	internal static ConVar con_filter_text_out = new("con_filter_text_out", "", FCvar.MaterialSystemThread, "Text with which to filter OUT of console spew. Set con_filter_enable 1 or 2 to activate.");
#if GMOD_DLL && !SWDS
	internal static ConVar con_bgalpha = new("con_bgalpha", "50", FCvar.Archive, "Background alpha for console notify (contimes + developer 1).");
	internal static ConVar con_border = new("con_border", "6", FCvar.Archive, "Border size for console notify (contimes + developer 1)..");
#endif
}

#if !SWDS
public class ConPanel : BasePanel
{
	public ConPanel(Panel panel) : base(panel) {
		SetSize(videomode.GetModeStereoWidth(), videomode.GetModeStereoHeight());
		SetPos(0, 0);
		SetVisible(true);
		SetMouseInputEnabled(false);
		SetKeyboardInputEnabled(false);

		DefaultColor[0] = 1.0f;
		DefaultColor[1] = 1.0f;
		DefaultColor[2] = 1.0f;
		SetName("ConPanel");
		drawDebugAreas = false;
#if GMOD_DLL
		Instance = this;
		NotifyOverlayPanel = new NotifyOverlayPanel();
		NotifyOverlayPanel.SetParent(Surface.GetEmbeddedPanel());
		NotifyOverlayPanel.MakePopup(true, false);
		NotifyOverlayPanel.SetKeyboardInputEnabled(false);
		NotifyOverlayPanel.SetMouseInputEnabled(false);
#endif
	}

#if GMOD_DLL
	internal static ConPanel? Instance;
	Panel? NotifyOverlayPanel;

	public override void Dispose() {
		if (NotifyOverlayPanel != null) {
			NotifyOverlayPanel.SetParent(null);
			NotifyOverlayPanel.MarkForDeletion();
			NotifyOverlayPanel = null;
		}
		Instance = null;
		base.Dispose();
	}
#endif

	public Host Host = Singleton<Host>();
	public IBaseClientDLL ClientDLL = Singleton<IBaseClientDLL>();
	public Con Con = Singleton<Con>();
	public VideoMode_Common videomode = (VideoMode_Common)Singleton<IVideoMode>();

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		Font = scheme.GetFont("DefaultSmallDropShadow", false);
		FontFixed = scheme.GetFont("DefaultFixedDropShadow", false);
	}

	public override void Paint() {
#if GMOD_DLL
		if (ClientDLL.ShouldDrawDropdownConsole())
			DrawDebugAreas();
#else
		// Client DLL shoulddrawdropdownconsole?

		DrawDebugAreas();
		DrawNotify();
#endif
	}

	public override void PaintBackground() {
#if !GMOD_DLL
		DrawVersion();
#endif
	}

	public void DrawVersion() {
		if (!Con.IsVisible())
			return;

		Span<char> ver = stackalloc char[100];
		Span<char> text = ver; // Fixes an unsafe complaint in the compiler for whatever reason
		int wide = GetWide();
		text = new PrintF(ver, "Source.NET Engine %i (build %d)").I(Protocol.VERSION).D(Sys.BuildNumber());

		Surface.DrawSetTextColor(new Color(255, 255, 255, 255));
		int x = wide - DrawTextLen(Font, text) - 2;
#if GMOD_DLL
		DrawText(Font, x, 30, text);
#else
		DrawText(Font, x, 0, text);
#endif

		if (cl.IsActive()) {
			if (cl.NetChannel!.IsLoopback())
				text = new PrintF(ver, "Map '%s'").S(cl.LevelBaseName).ToSpan();
			else
				text = new PrintF(ver, "Server '%s' Map '%s'").S(cl.NetChannel!.GetRemoteAddress()!.ToString()).S(cl.LevelBaseName).ToSpan();

			int tall = Surface.GetFontTall(Font);

			x = wide - DrawTextLen(Font, text) - 2;
#if GMOD_DLL
			DrawText(Font, x, tall + 31, text);
#else
			DrawText(Font, x, tall + 1, text);
#endif
		}
	}

	readonly List<NotifyText> TextToDraw = [];
	public virtual void DrawNotify() {
		int x = 8;
		int y = 5;

		if (FontFixed == null)
			return;

		// notify area only draws in developer mode
		if (!Host.developer.GetBool())
			return;

#if GMOD_DLL
		// todo: return if cl_movieinfo.IsRecording()
#endif

		Surface.DrawSetTextFont(FontFixed);

		int fontTall = Surface.GetFontTall(FontFixed) + 1;

		TextToDraw.Clear();
		lock (NotifyTexts) {
			TextToDraw.EnsureCountDefault(NotifyTexts.Count);
			NotifyTexts.CopyTo(TextToDraw.AsSpan());
		}

		Span<NotifyText> textToDraw = TextToDraw.AsSpan();
		int c = textToDraw.Length;
#if GMOD_DLL
		int border = ConsoleCVars.con_border.GetInt();
		int bgAlpha = ConsoleCVars.con_bgalpha.GetInt();
		int width = 0;
		int height = 0;
		for (int i = 0; i < c; i++) {
			ref NotifyText notify = ref textToDraw[i];
			float timeleft = (float)notify.LifeRemaining;

			if (timeleft < .5f) {
				float f = Math.Clamp(timeleft, 0.0f, .5f) / .5f;
				if (i == 0 && f < 0.2f)
					height = (int)(height - (1.0f - f * 5.0f) * fontTall);
			}

			height += fontTall;
			ReadOnlySpan<char> text = ((ReadOnlySpan<char>)notify.Text).SliceNullTerminatedString();
			int len = DrawTextLen(FontFixed, text);
			if (width < len)
				width = DrawTextLen(FontFixed, text);
		}

		int charWide = DrawTextLen(FontFixed, "c");
		if (border >= 5) {
			x = border + 4;
			y = border + 1;
		}

		Surface.DrawSetColor(0, 0, 0, bgAlpha);
		Surface.DrawFilledRect(x - border, y - border, border + (width - charWide) + x, border + (height - fontTall) + y);
#endif
		for (int i = 0; i < c; i++) {
			ref NotifyText notify = ref textToDraw[i];
			TimeUnit_t timeleft = notify.LifeRemaining;
			Color clr = notify.Color;

			if (timeleft < .5f) {
				TimeUnit_t f = Math.Clamp(timeleft, 0.0, .5) / .5;

				clr[3] = (byte)(int)(float)(f * 255.0);

				if (i == 0 && f < 0.2f)
					y -= (int)(float)(fontTall * (1.0 - f / 0.2));
			}
			else {
				clr[3] = 255;
			}

			DrawColoredText(FontFixed, x, y, clr[0], clr[1], clr[2], clr[3], notify.Text);

			y += fontTall;
		}
	}
	static ConVar con_nprint_bgalpha = new("50", 0, "Con_NPrint background alpha.");
	static ConVar con_nprint_bgborder = new("5", 0, "Con_NPrint border size.");

	public virtual void DrawDebugAreas() {
		if (!drawDebugAreas)
			return;

		// Find the top and bottom of all the nprint text so we can draw a box behind it.
		int left = 99999, top = 99999, right = -99999, bottom = -99999;
		if (con_nprint_bgalpha.GetInt() != 0) {
			// First, figure out the bounds of all the con_nprint text.
			if (ProcessNotifyLines(ref left, ref top, ref right, ref bottom, false) != 0) {
				int b = con_nprint_bgborder.GetInt();

				// Now draw a box behind it.
				Surface.DrawSetColor(0, 0, 0, con_nprint_bgalpha.GetInt());
				Surface.DrawFilledRect(left - b, top - b, right + b, bottom + b);
			}
		}

		if (ProcessNotifyLines(ref left, ref top, ref right, ref bottom, true) == 0)
			drawDebugAreas = false;
	}

	public virtual int ProcessNotifyLines(ref int left, ref int top, ref int right, ref int bottom, bool draw) {
		int count = 0;
		int y = 20;

		for (int i = 0; i < MAX_DBG_NOTIFY; i++) {
			da_notify_t Notify = da_notify[i];

			if (Host.RealTime < Notify.Expire || Notify.Expire == -1) {
				if (Notify.Expire == -1 && draw)
					Notify.Expire = Host.RealTime - 1;

				int len;
				int x;

				IFont? font = Notify.FixedWidthFont ? FontFixed : Font;

				int fontTall = Surface.GetFontTall(FontFixed) + 1;

				ReadOnlySpan<char> NotifyText = Notify.Notify;
				NotifyText = NotifyText.SliceNullTerminatedString();

				len = DrawTextLen(font, NotifyText);
				x = videomode.GetModeStereoWidth() - 10 - len;

				if (y + fontTall > videomode.GetModeStereoHeight() - 20)
					return count;

				count++;
				y = 20 + 10 * i;

				if (draw) {
					DrawColoredText(font, x, y,
						(byte)(int)(Notify.Color[0] * 255),
						(byte)(int)(Notify.Color[1] * 255),
						(byte)(int)(Notify.Color[2] * 255),
						255,
						NotifyText);
				}

				if (!NotifyText.IsEmpty && NotifyText[0] != '\0') {
					left = Math.Min(left, x);
					top = Math.Min(top, y);
					right = Math.Max(right, x + len);
					bottom = Math.Max(bottom, y + fontTall);
				}

				y += fontTall;
			}
		}

		return count;
	}

	IFont? Font;
	IFont? FontFixed;

	struct NotifyText
	{
		public Color Color;
		public TimeUnit_t LifeRemaining;
		public InlineArray256<char> Text;
	}
	readonly List<NotifyText> NotifyTexts = [];

	InlineArray3<float> DefaultColor;
	struct da_notify_t
	{
		public InlineArray256<char> Notify;
		public TimeUnit_t Expire;
		public InlineArray3<float> Color;
		public bool FixedWidthFont;
	}

	const int MAX_DBG_NOTIFY = 128;
	InlineArray128<da_notify_t> da_notify;
	bool drawDebugAreas;

	public override bool ShouldDraw() {
		bool visible = false;

		if (drawDebugAreas) {
			visible = true;
		}

		if (!Con.IsVisible()) {
			int i;
			int c = NotifyTexts.Count;
			Span<NotifyText> notifyTexts = NotifyTexts.AsSpan();
			for (i = c - 1; i >= 0; i--) {
				ref NotifyText notify = ref notifyTexts[i];

				notify.LifeRemaining -= Host.FrameTime;

				if (notify.LifeRemaining <= 0.0f) {
					NotifyTexts.RemoveAt(i);
					notifyTexts = NotifyTexts.AsSpan();
					continue;
				}

				visible = true;
			}
		}
		else {
			visible = true;
		}

		return visible;
	}

	const int DBG_NOTIFY_TIMEOUT = 4;

	public void Con_NPrintf(int idx, ReadOnlySpan<char> msg) {
		if (idx < 0 || idx >= MAX_DBG_NOTIFY)
			return;

		msg.ClampedCopyTo(da_notify[idx].Notify);

		// Reset values
		da_notify[idx].Expire = Host.RealTime + DBG_NOTIFY_TIMEOUT;
		da_notify[idx].Color = DefaultColor;
		da_notify[idx].FixedWidthFont = false;
		drawDebugAreas = true;
	}

	public void Con_NXPrintf(in Con_NPrint_s info, ReadOnlySpan<char> msg) {
		if (info.Index < 0 || info.Index >= MAX_DBG_NOTIFY)
			return;

		strcpy(da_notify[info.Index].Notify, msg);
		if (info.TimeToLive == -1)
			da_notify[info.Index].Expire = -1;
		else
			da_notify[info.Index].Expire = Host.RealTime + info.TimeToLive;

		MemoryMarshal.Cast<Vector3, float>(new(in info.Color)).CopyTo(da_notify[info.Index].Color);
		da_notify[info.Index].FixedWidthFont = info.FixedWidthFont;
		drawDebugAreas = true;
	}

	private static void AppendToText(ref NotifyText nt, ReadOnlySpan<char> src) {
		Span<char> dest = nt.Text;
		int len = MemoryExtensions.IndexOf(dest, '\0');
		if (len < 0) len = dest.Length;

		int copyLen = Math.Min(src.Length, dest.Length - len - 1);
		src[..copyLen].ClampedCopyTo(dest[len..]);
		dest[len + copyLen] = '\0';
	}

	public void AddToNotify(in Color clr, ReadOnlySpan<char> msg) {
		if (!Host.Initialized)
			return;

		if (!Host.developer.GetBool())
			return;

		if (msg[0] == 1 || msg[0] == 2)
			msg = msg[1..];

		if (msg.IsEmpty || msg[0] == '\0')
			return;

		lock (NotifyTexts) {
			ref NotifyText current = ref Unsafe.NullRef<NotifyText>();

			int slot = NotifyTexts.Count - 1;
			if (slot < 0) {
				NotifyTexts.Add(default);
				slot = NotifyTexts.Count - 1;
				current = ref NotifyTexts.AsSpan()[slot];
				current.Color = clr;
				current.Text[0] = '\0';
				current.LifeRemaining = ConsoleCVars.con_notifytime.GetFloat();
			}
			else {
				current = ref NotifyTexts.AsSpan()[slot];
				current.Color = clr;
			}

			Assert(current);
			// TODO: Localization

			ReadOnlySpan<char> p = msg;
			while (!p.IsEmpty && p[0] != '\0') {
				int nextreturn = p.IndexOf('\n');
				if (nextreturn != -1) {
					int copysize = nextreturn + 1;
					AppendToText(ref current, p[..copysize]);

					if (current.Text[0] != '\0' && current.Text[0] != '\n') {
						NotifyTexts.Add(default);
						slot = NotifyTexts.Count - 1;
						current = ref NotifyTexts.AsSpan()[slot];
					}
					current.Color = clr;
					current.LifeRemaining = ConsoleCVars.con_notifytime.GetFloat();
					p = p[copysize..];
					continue;
				}

				AppendToText(ref current, p);
				current.Color = clr;
				current.LifeRemaining = ConsoleCVars.con_notifytime.GetFloat();
				break;
			}

			while (NotifyTexts.Count > 0 && (NotifyTexts.Count >= ConsoleCVars.con_times.GetInt())) {
				NotifyTexts.RemoveAt(0);
			}
		}
	}

	public void ClearNotify() {
		lock (NotifyTexts) {
			NotifyTexts.Clear();
		}
	}
}

#if GMOD_DLL
public class NotifyOverlayPanel : Panel
{
	readonly IBaseClientDLL ClientDLL = Singleton<IBaseClientDLL>();

	public NotifyOverlayPanel() : base(null, "GModConsoleOverlayPanel") {
		Surface.GetScreenSize(out int wide, out int tall);
		SetSize(wide, tall);
		SetPos(0, 0);
	}

	public override void OnThink() => Surface.MovePopupToFront(this);

	public override void Paint() {
		if (ConPanel.Instance == null)
			return;

		ConPanel.Instance.DrawVersion();

		if (ClientDLL.ShouldDrawDropdownConsole())
			ConPanel.Instance.DrawNotify();
	}

	public override void OnScreenSizeChanged(int oldWide, int oldTall) {
		Surface.GetScreenSize(out int wide, out int tall);
		SetSize(wide, tall);
		SetPos(0, 0);
	}
}
#endif
#endif


public class Con(
#if !SWDS
 IEngineVGuiInternal EngineVGui, IVGuiInput Input, IBaseClientDLL ClientDLL
#endif
)
{
#if !SWDS
	static ConPanel? conPanel = null;
#endif
	static ConVar con_enable = new("1", FCvar.Archive, "Allows the console to be activated.");

	[ConCommand]
	void toggleconsole() => ToggleConsole();

	public void ShowConsole() {
#if !SWDS
		if (Input.GetAppModalSurface() != null)
			return;

		if (!ClientDLL.ShouldAllowConsole())
			return;

		if (con_enable.GetBool()) {
			EngineVGui.ShowConsole();
			Singleton<Scr>().EndLoadingPlaque();
		}
#endif
	}

	public void HideConsole() {
#if !SWDS
		if (EngineVGui.IsConsoleVisible())
			EngineVGui.HideConsole();
#endif
	}

	public void ToggleConsole() {
#if !SWDS
		if (EngineVGui.IsConsoleVisible()) {
			HideConsole();
			EngineVGui.HideGameUI();
		}
		else
			ShowConsole();
#endif
	}

	public void Init() {
		con_initialized = true;
	}
	public void Shutdown() { }
	public void Execute() { }

	// TODO: ConPanel

	internal void ClearNotify() {
#if !SWDS
		conPanel?.ClearNotify();
#endif
	}

	public void Clear() {
		Singleton<IEngineVGui>().ClearConsole();
		ClearNotify();
	}

	[ConCommand] void clear() => Clear();

	static bool g_fColorPrintf;
	static bool g_fIsDebugPrint;
	static bool g_bInColorPrint;

	public void ColorPrintf(in Color clr, ReadOnlySpan<char> fmt) {
#if !SWDS
		g_fColorPrintf = true;
		ColorPrint(clr, fmt);
		g_fColorPrintf = false;
#endif
	}
	static ConVar spew_consolelog_to_debugstring = new("0", 0, "Send console log to PLAT_DebugString()");

	public static void ColorPrint(in Color clr, ReadOnlySpan<char> msg) {
#if !SWDS
		if (g_bInColorPrint)
			return;

		int nCon_Filter_Enable = ConsoleCVars.con_filter_enable.GetInt();
		if (nCon_Filter_Enable > 0) {
			ReadOnlySpan<char> pszText = ConsoleCVars.con_filter_text.GetString();
			ReadOnlySpan<char> pszIgnoreText = ConsoleCVars.con_filter_text_out.GetString();

			switch (nCon_Filter_Enable) {
				case 1:
					if (!pszText.IsEmpty && (pszText[0] != '\0') && !msg.Contains(pszText, StringComparison.OrdinalIgnoreCase))
						return;
					if (!pszIgnoreText.IsEmpty && pszIgnoreText[0] != '\0' && msg.Contains(pszIgnoreText, StringComparison.OrdinalIgnoreCase))
						return;
					break;

				case 2:
					if (!pszIgnoreText.IsEmpty && pszIgnoreText[0] != '\0' && msg.Contains(pszIgnoreText, StringComparison.OrdinalIgnoreCase))
						return;
					if (!pszText.IsEmpty && (pszText[0] != '\0') && !msg.Contains(pszText, StringComparison.OrdinalIgnoreCase)) {
						Color mycolor = new(200, 200, 200, 150);
						cvar.ConsoleColorPrintf(mycolor, msg);
						return;
					}
					break;

				default:
					// by default do no filtering
					break;
			}
		}

		g_bInColorPrint = true;

		// also echo to debugging console
		if (Debugger.IsAttached && ConsoleCVars.con_trace.GetInt() == 0 && !spew_consolelog_to_debugstring.GetBool())
			Sys.OutputDebugString(msg);

		if (sv.IsDedicated()) {
			g_bInColorPrint = false;
			return;     // no graphics mode
		}

		bool convisible = IsVisible();
		bool indeveloper = (Host.developer.GetInt() > 0);
		bool debugprint = g_fIsDebugPrint;

		if (g_fColorPrintf)
			cvar.ConsoleColorPrintf(clr, msg);
		else {
			if (g_fIsDebugPrint) {
				if (!cl.IsActive() || !convisible)
					cvar.ConsoleDPrintf(msg);
			}
			else
				cvar.ConsolePrintf(msg);
		}

		if (Sys.InSpew)
			Msg(msg);

#if !SWDS
		if ((!debugprint || indeveloper) && !(debugprint && convisible))
			conPanel?.AddToNotify(clr, msg);
#endif

		g_bInColorPrint = false;
#endif
	}

#if !SWDS
	public static bool IsVisible() => __EngineVGui.IsConsoleVisible();
#endif

	internal void CreateConsolePanel(Panel parent) {
#if !SWDS
		conPanel = new(parent);
		conPanel.SetVisible(false);
#endif
	}

	public static void DebugLog(ReadOnlySpan<char> text) {
		// TODO
	}

	static bool con_debuglog = false;
	static bool con_initialized = false;
	static bool con_debuglogmapprefixed = false;

	public static bool HandleRedirectAndDebugLog(ReadOnlySpan<char> msg) {
		// Add to redirected message
		if (SV.RedirectActive()) {
			SV.RedirectAddText(msg);
			return false;
		}

		// log all messages to file
		if (con_debuglog)
			DebugLog(msg);

		if (!con_initialized)
			return false;

		return true;
	}

	static bool inupdate;
	public static void PrintF(ReadOnlySpan<char> msg) {

		if (!HandleRedirectAndDebugLog(msg)) {
			return;
		}

#if SWDS
	Msg(msg);
#else
		if (sv.IsDedicated()) {
			Msg(msg);
		}
		else {
			Color clr = new(0, 0, 0, 255);
			ColorPrint(clr, msg);
		}
#endif
	}
	public static void NPrintF(int idx, ReadOnlySpan<char> text) {
#if !SWDS
		conPanel!.Con_NPrintf(idx, text);
#endif
	}
	public static void NXPrintF(in Con_NPrint_s info, ReadOnlySpan<char> text) {
#if !SWDS
		conPanel!.Con_NXPrintf(in info, text);
#endif
	}

#if !SWDS
	public ConPanel? GetConsolePanel() => conPanel;
#endif
}
