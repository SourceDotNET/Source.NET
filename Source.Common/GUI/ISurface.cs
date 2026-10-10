using Source.Common.Bitmap;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;

using System.Drawing;
using System.Numerics;

namespace Source.Common.GUI;

public struct SurfaceVertex
{
	public Vector2 Position;
	public Vector2 TexCoord;

	public SurfaceVertex() { }
	public SurfaceVertex(Vector2 pos, Vector2 tex) {
		Position = pos;
		TexCoord = tex;
	}
}

public record struct TextureID
{
	public long ID;
	public static readonly TextureID INVALID = new(-1);
	public TextureID(long id) => ID = id;
	public static implicit operator long(TextureID id) => id.ID;
	public static implicit operator TextureID(long id) => new(id);
}

public enum FontDrawType
{
	Default,
	NonAdditive,
	Additive,
	Count
}

public enum SurfaceFeature
{
	AntialiasedFonts = 1,
	DropShadowFonts = 2,
	EscapeKey = 3,
	OpeningNewHTMLWindows = 4,
	FrameMinimizeMaximize = 5,
	OutlineFonts = 6,
	DirectHwndRender = 7
}

public enum SurfaceFontFlags
{
	None,
	Italic = 0x001,
	Underline = 0x002,
	Strikeout = 0x004,
	Symbol = 0x008,
	Antialias = 0x010,
	GaussianBlur = 0x020,
	Rotary = 0x040,
	DropShadow = 0x080,
	Additive = 0x100,
	Outline = 0x200,
	Custom = 0x400,
	Bitmap = 0x800,
}

public ref struct CharRenderInfo
{
	public int X, Y;
	public Span<SurfaceVertex> Verts;
	public TextureID TextureId;
	public int A;
	public int B;
	public int C;
	public int FontTall;
	public IFont? CurrentFont;

	public FontDrawType DrawType;
	public char Character;

	public bool Valid;
	public bool ShouldClip;
};

public delegate void VGuiPlayFunc(ReadOnlySpan<char> fileName);
public interface ISurface
{
	void RunFrame();

	IPanel GetEmbeddedPanel();
	void SetEmbeddedPanel(IPanel panel);

	void PushMakeCurrent(IPanel panel, bool useInsets);
	void PopMakeCurrent(IPanel panel);

	void DrawSetColor(int r, int g, int b, int a);
	void DrawSetColor(in Color color);

	void DrawFilledRect(int x0, int y0, int x1, int y1);
	void DrawFilledRectArray(Span<Rectangle> rects, int numRects);
	void DrawOutlinedRect(int x0, int y0, int x1, int y1);
	void DrawOutlinedCircle(int x, int y, int radius, int segments);

	void DrawLine(int x0, int y0, int x1, int y1);
	void DrawPolyLine(Span<Point> points);

	void DrawSetTextFont(IFont? font);
	void DrawSetTextColor(int r, int g, int b, int a);
	void DrawSetTextColor(in Color color);
	void DrawSetTextPos(int x, int y);
	void DrawGetTextPos(out int x, out int y);
	void DrawPrintText(ReadOnlySpan<char> text, FontDrawType drawType = FontDrawType.Default);
	void DrawFlushText();

	int GetFontTall(IFont? font);
	int GetFontTallRequested(IFont? font);
	int GetFontAscent(IFont? font, char ch);
	int IsFontAdditive(IFont? font);
	void GetCharABCwide(IFont? font, char ch, out int a, out int b, out int c);
	int GetCharacterWidth(IFont? font, char ch);
	void GetTextSize(IFont? font, ReadOnlySpan<char> text, out int wide, out int tall);


	int DrawGetTextureId(ReadOnlySpan<char> filename);
	bool DrawGetTextureFile(in TextureID id, out ReadOnlySpan<char> filename);
	void DrawSetTextureFile(in TextureID id, in ReadOnlySpan<char> filename, int hardwareFilter, bool forceReload);
	void DrawSetTextureRGBA(in TextureID id, Span<byte> rgba, int wide, int tall, int hardwareFilter, bool forceReload);
	void DrawSetTextureRGBAEx(in TextureID id, IntPtr rgba, int wide, int tall, ImageFormat format);
	void DrawUpdateRegionTextureRGBA(in TextureID id, int x, int y, IntPtr rgba, int wide, int tall, ImageFormat format);
	void DrawSetTexture(in TextureID id);
	void DrawGetTextureSize(in TextureID id, out int wide, out int tall);
	void DrawTexturedRect(int x0, int y0, int x1, int y1);
	void DrawTexturedSubRect(int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1);
	bool IsTextureIDValid(in TextureID id);
	bool DeleteTextureByID(in TextureID id);
	void DestroyTextureID(in TextureID id);
	TextureID CreateNewTextureID(bool procedural = false);

	void GetScreenSize(out int wide, out int tall);
	void SetAsTopMost(IPanel panel, bool state);
	void BringToFront(IPanel panel);
	void SetForegroundWindow(IPanel panel);
	void SetPanelVisible(IPanel panel, bool state);
	void SetMinimized(IPanel panel, bool state);
	bool IsMinimized(IPanel panel);
	void FlashWindow(IPanel panel, bool state);
	void SetTitle(IPanel panel, ReadOnlySpan<char> title);
	void SetAsToolBar(IPanel panel, bool state);

	void CreatePopup(IPanel panel, bool minimised, bool showTaskbarIcon = true, bool disabled = false, bool mouseInput = true, bool kbInput = true);
	void SwapBuffers(IPanel panel);
	void Invalidate(IPanel panel);
	void SetCursor(CursorCode cursor);
	void SetCursor(HCursor cursor);
	void SetCursorAlwaysVisible(bool visible);
	bool IsCursorVisible();
	void ApplyChanges();
	bool IsWithin(int x, int y);
	bool HasFocus();

	bool SupportsFeature(SurfaceFeature feature);

	IPanel? GetRestrictedPanel();
	void RestrictPaintToSinglePanel(IPanel? panel);


	void UnlockCursor();
	void LockCursor();
	void SetTranslateExtendedKeys(bool state);
	IPanel? GetTopmostPopup();
	void SetTopLevelFocus(IPanel? panel);

	IFont CreateFont();

	ReadOnlySpan<char> GetBitmapFontName(ReadOnlySpan<char> name);
	bool SetFontGlyphSet(IFont font, ReadOnlySpan<char> windowsFontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags, int rangeMin = 0, int rangeMax = 0);
	void ClearTemporaryFontCache();
	bool AddCustomFontFile(ReadOnlySpan<char> fontName, ReadOnlySpan<char> fontFileName);

	int GetCharacterWidth(IFont font, int ch);

	int GetPopupCount();
	IPanel? GetPopup(int index);
	bool ShouldPaintChildPanel(IPanel childPanel);
	bool RecreateContext(IPanel panel);
	void AddPanel(IPanel panel);
	void ReleasePanel(IPanel panel);
	void MovePopupToFront(IPanel panel);
	void MovePopupToBack(IPanel panel);

	void SolveTraverse(IPanel panel, bool forceApplySchemeSettings = false);
	void PaintTraverse(IPanel panel);

	void EnableMouseCapture(IPanel panel, bool state);

	void GetWorkspaceBounds(out int x, out int y, out int wide, out int tall);
	void GetAbsoluteWindowBounds(out int x, out int y, out int wide, out int tall);
	void GetProportionalBase(out int width, out int height);

	void CalculateMouseVisible();
	bool NeedKBInput();

	bool HasCursorPosFunctions();
	void PaintTraverseEx(IPanel embedded, bool v);
	void PaintSoftwareCursor();
	bool HandleInputEvent(in InputEvent ev);
	void InstallPlaySoundFunc(VGuiPlayFunc func);
	bool AddBitmapFontFile(ReadOnlySpan<char> fontFile);
	void SetBitmapFontName(string name, ReadOnlySpan<char> fontFile);
	float DrawGetAlphaMultiplier();
	void DrawSetAlphaMultiplier(float newAlphaMultiplier);
	void OffsetAbsPos(ref int x, ref int y);
	void SetBitmapFontGlyphSet(IFont font, ReadOnlySpan<char> fontName, float scalex, float scaley, SurfaceFontFlags flags);
	void DrawChar(char c, FontDrawType drawType = FontDrawType.Default);
	bool DrawGetCharRenderInfo(char ch, ref CharRenderInfo info);
	void DrawRenderCharFromInfo(CharRenderInfo info);
	void PlaySound(ReadOnlySpan<char> fileName);
	void DrawTexturedPolygon(Span<SurfaceVertex> verts, bool clipVertices = true);
	bool IsCursorLocked();
	void GetKernedCharWidth(IFont? font, char ch, char before, char after, out float w, out float flabcA);
	bool IsScreenPosOverrideActive();
	bool IsScreenSizeOverrideActive();
	void ForceScreenPosOffset(bool active, int x, int y);
	void ForceScreenSizeOverride(bool active, int w, int h);
	int DrawColoredText(IFont? font, int x, int y, byte r, byte g, byte b, byte a, ReadOnlySpan<char> text);
	void DrawColoredTextRect(IFont? font, int x, int y, int w, int h, byte r, byte g, byte b, byte a, ReadOnlySpan<char> text);
	void DrawString(ReadOnlySpan<char> str, FontDrawType drawType = FontDrawType.Default);
	void PrecacheFontCharacters(IFont font, ReadOnlySpan<char> str);
	void PopFullscreenViewport();
	void PushFullscreenViewport();
	ReadOnlySpan<char> GetFontName(IFont font);
	ReadOnlySpan<char> GetFontFamilyName(IFont font);
#if GMOD_DLL
	void DrawGetColor(out Color color);
	void DrawGetTextColor(out Color color);
	void DrawGetTranslate(out int x, out int y);
#endif
}

public interface IMatSystemSurface : ISurface
{
	void AttachToWindow(IWindow? window, bool appDrivesInput);
	void EnableWindowsMessages(bool enabled);
	int DrawTextLen(IFont font, ReadOnlySpan<char> text);
	int GetTextureNumFrames(in TextureID id);
	void DrawSetTextureFrame(in TextureID id, int frame, ref TokenCache frameCache);
	void DrawSetTextureMaterial(TextureID textureID, IMaterial material);
	void SetFullscreenViewportAndRenderTarget(int x, int y, int w, int h, ITexture? renderTarget);
	void GetFullscreenViewport(out int x, out int y, out int w, out int h);
	void OnScreenSizeChanged(int oldWidth, int oldHeight);
	void DisableClipping(bool disable);
	void GetClippingRect(out int left, out int top, out int right, out int bottom, out bool clippingDisabled);
}
