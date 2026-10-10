using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.Utilities;
using Source.GUI.Controls;

using System.Diagnostics.CodeAnalysis;
using System.Drawing;
namespace Source.MaterialSystem.Surface;

public struct ScreenOverride
{
	public bool Active;
	public int X;
	public int Y;
}
public struct PaintState
{
	public IPanel Panel;
	public int TranslateX;
	public int TranslateY;
	public int ScissorLeft;
	public int ScissorRight;
	public int ScissorTop;
	public int ScissorBottom;
}

public struct ScissorRectState
{
	public int Left;
	public int Top;
	public int Right;
	public int Bottom;

	public bool Scissor;
	public bool FullScreenScissor;
}

public class MatSystemSurface : IMatSystemSurface
{
	readonly IMaterialSystem materials;
	bool InDrawing;
	bool In3DPaintMode;
	float zPos;
	ScissorRectState scissorRect;
	public int TranslateX;
	public int TranslateY;
	public float alphaMultiplier = 1;
	public int[] SurfaceExtents = new int[4];
	public Color DrawColor = new(255, 255, 255, 255);
	public Color DrawTextColor = new(255, 255, 255, 255);
	public IFont? DrawTextFont = null;
	public int TextPosX;
	public int TextPosY;
	IPanel DefaultEmbeddedPanel;
	IPanel? EmbeddedPanel;
	bool IsDefault;

	IMesh? Mesh;
	MeshBuilder meshBuilder;
	MaterialReference White = new();

	float AlphaMultiplier;

	readonly ICommandLine CommandLine;
	readonly ISchemeManager SchemeManager;
	readonly IFileSystem FileSystem;
	readonly FontManager FontManager;
	readonly FontTextureCache FontTextureCache;
	IVGui VGui;
	IVGuiInput? VGuiInput;
	readonly IInputSystem InputSystem;
	readonly ClientGlobalVariables globals;
	readonly IServiceProvider services;
	readonly ILauncherManager launcherMgr;
	readonly TextureDictionary TextureDictionary;

	bool ProvidedInterfaces;

	[MemberNotNull(nameof(VGuiInput))]
	void LinkVGUI() {
		if (!ProvidedInterfaces) {
			VGui ??= services.GetRequiredService<IVGui>();
			VGuiInput ??= VGui.GetInput();
			ProvidedInterfaces = true;
		}
	}

	public MatSystemSurface(IMaterialSystem materials, IShaderAPI shaderAPI, ICommandLine commandLine,
							ISchemeManager schemeManager, IFileSystem fileSystem, ClientGlobalVariables globals,
							IServiceProvider services, IInputSystem inputSystem, ISystem system, ILauncherManager launcherMgr) {
		this.materials = materials;
		this.services = services;
		this.TextureDictionary = new(materials, this);
		this.FileSystem = fileSystem;
		this.FontManager = new FontManager(materials, fileSystem, system, this);
		this.FontTextureCache = new FontTextureCache(materials, fileSystem, system, this.FontManager, this);
		this.InputSystem = inputSystem;
		this.globals = globals;
		CommandLine = commandLine;
		SchemeManager = schemeManager;

		DrawColor[0] = DrawColor[1] = DrawColor[2] = DrawColor[3] = 25; ;
		TranslateX = TranslateY = 0;

		EnableScissor(false);
		SetScissorRect(0, 0, 100000, 100000);

		AlphaMultiplier = 1;

		KeyValues vmtKeyValues = new KeyValues("UnlitGeneric");
		vmtKeyValues.SetString("$basetexture", "white");
		vmtKeyValues.SetInt("$vertexcolor", 1);
		vmtKeyValues.SetInt("$vertexalpha", 1);
		vmtKeyValues.SetInt("$ignorez", 1);
		vmtKeyValues.SetInt("$no_fullbright", 1);

		White.Init("VGUI_White", TEXTURE_GROUP_OTHER, vmtKeyValues);

		InitInput();
		InitCursors();

		DefaultEmbeddedPanel = new MatEmbeddedPanel() {
			materials = materials,
		};
		SetEmbeddedPanel(DefaultEmbeddedPanel);
		this.launcherMgr = launcherMgr;
	}

	private void InitInput() {
		EnableInput(true);
	}

	public void InternalThinkTraverse(IPanel panel) {
		panel.TraverseLevel(1);
		panel.Think();

		for (int i = 0; i < panel.GetChildCount(); i++) {
			var child = panel.GetChild(i);
			if (child.IsVisible()) {
				InternalThinkTraverse(child);
			}
		}

		panel.TraverseLevel(-1);
	}


	private void EnableInput(bool v) {

	}

	private void InitCursors() {

	}

	public bool FullyTransparent => DrawColor.A <= 0;
	public bool TextFullyTransparent => DrawTextColor.A <= 0;

	public void InitVertex(ref SurfaceVertex vertex, int x, int y, float u, float v) {
		vertex.Position = new(x + TranslateX, y + TranslateY);
		vertex.TexCoord = new(u, v);
	}

	public void InternalSetMaterial(IMaterial? material = null) {
		if (material == null)
			material = White.Get();

		using MatRenderContextPtr renderContext = new(materials);
		Mesh = renderContext.GetDynamicMesh(true, null, null, material);
	}

	bool checkedCommandLine;

	float pixelOffsetX;
	float pixelOffsetY;

	public void StartDrawing() {
		if (!checkedCommandLine) {
			checkedCommandLine = true;

			ReadOnlySpan<char> pX = CommandLine.ParmValue("-pixel_offset_x", null);
			if (!pX.IsEmpty)
				pixelOffsetX = float.TryParse(pX, out pixelOffsetX) ? pixelOffsetX : 0;


			ReadOnlySpan<char> pY = CommandLine.ParmValue("-pixel_offset_y", null);
			if (!pY.IsEmpty)
				pixelOffsetY = float.TryParse(pY, out pixelOffsetY) ? pixelOffsetY : 0;
		}

		InDrawing = true;
		BoundTexture = TextureID.INVALID;

		using MatRenderContextPtr renderContext = new(materials);
		renderContext.GetViewport(out int x, out int y, out int width, out int height);

		SurfaceExtents[0] = 0;
		SurfaceExtents[1] = 0;
		SurfaceExtents[2] = width;
		SurfaceExtents[3] = height;

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
		renderContext.Scale(1, -1, 1);

		renderContext.Ortho(pixelOffsetX, pixelOffsetY, width + pixelOffsetX, height + pixelOffsetY, -1.0f, 1.0f);

		// make sure there is no translation and rotation laying around
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		// Always enable scissoring (translate to origin because of the glTranslatef call above..)
		EnableScissor(true);

		TranslateX = 0;
		TranslateY = 0;

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
	}

	private void EnableScissor(bool v) {
		scissorRect.Scissor = v;
	}

	public void DrawQuad(in SurfaceVertex ul, in SurfaceVertex lr, in Color color) {
		if (Mesh == null)
			return;

		meshBuilder.Begin(Mesh, MaterialPrimitiveType.Quads, 1);

		meshBuilder.Position3f(ul.Position.X, ul.Position.Y, zPos);
		meshBuilder.Color4ubv(color);
		meshBuilder.TexCoord2f(0, ul.TexCoord.X, ul.TexCoord.Y);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3f(lr.Position.X, ul.Position.Y, zPos);
		meshBuilder.Color4ubv(color);
		meshBuilder.TexCoord2f(0, lr.TexCoord.X, ul.TexCoord.Y);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3f(lr.Position.X, lr.Position.Y, zPos);
		meshBuilder.Color4ubv(color);
		meshBuilder.TexCoord2f(0, lr.TexCoord.X, lr.TexCoord.Y);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3f(ul.Position.X, lr.Position.Y, zPos);
		meshBuilder.Color4ubv(color);
		meshBuilder.TexCoord2f(0, ul.TexCoord.X, lr.TexCoord.Y);
		meshBuilder.AdvanceVertex();

		meshBuilder.End();
		Mesh.Draw();
	}


	public bool AddCustomFontFile(ReadOnlySpan<char> fontName, ReadOnlySpan<char> fontFileName) {
		return FontManager.AddCustomFontFile(fontName, fontFileName);
	}

	public void AddPanel(IPanel panel) {
		if (panel.IsPopup())
			CreatePopup(panel, false);
	}

	public void ApplyChanges() {

	}

	public void BringToFront(IPanel panel) {
		panel.MoveToFront();

		// move panel to top of popup list
		if (panel.IsPopup()) {
			MovePopupToFront(panel);
		}
	}
	bool NeedsMouse;
	bool NeedsKeyboard;

	public void CalculateMouseVisible() {
		nint i;
		NeedsMouse = false;
		NeedsKeyboard = false;

		LinkVGUI();

		if (VGuiInput.GetMouseCapture() != null)
			return;

		nint c = GetPopupCount();
		IPanel? modalSubTree = VGuiInput.GetModalSubTree();
		if (modalSubTree != null) {
			for (i = 0; i < c; i++) {
				IPanel? pop = GetPopup((int)i);
				bool isChildOfModalSubPanel = IsChildOfModalSubTree(pop);
				if (!isChildOfModalSubPanel)
					continue;

				bool isVisible = pop.IsVisible();
				IPanel? p = pop.GetParent();

				while (p != null && isVisible) {
					if (p.IsVisible() == false) {
						isVisible = false;
						break;
					}
					p = p.GetParent();
				}

				if (isVisible) {
					NeedsMouse = NeedsMouse || pop.IsMouseInputEnabled();
					NeedsKeyboard = NeedsKeyboard || pop.IsKeyboardInputEnabled();

					if (NeedsMouse && NeedsKeyboard)
						break;
				}
			}
		}
		else {
			for (i = 0; i < c; i++) {
				IPanel? pop = GetPopup((int)i);

				bool isVisible = pop.IsVisible();
				IPanel? p = pop.GetParent();

				while (p != null && isVisible) {
					if (p.IsVisible() == false) {
						isVisible = false;
						break;
					}
					p = p.GetParent();
				}

				if (isVisible) {
					NeedsMouse = NeedsMouse || pop.IsMouseInputEnabled();
					NeedsKeyboard = NeedsKeyboard || pop.IsKeyboardInputEnabled();

					if (NeedsMouse && NeedsKeyboard)
						break;
				}
			}
		}

		if (NeedsMouse) {
			UnlockCursor();
			if (CurrentHCursor == (nint)CursorCode.None) {
				SetCursor(CursorCode.Arrow);
			}
		}
		else {
			SetCursor(CursorCode.None);
			LockCursor();
		}
	}

	private bool IsChildOfModalSubTree(IPanel pop) {
		throw new NotImplementedException();
	}

	public IFont CreateFont() {
		return FontManager.CreateFont();
	}

	public TextureID CreateNewTextureID(bool procedural = false) {
		return TextureDictionary.CreateTexture(procedural);
	}

	List<IPanel> PopupList = [];

	public void CreatePopup(IPanel panel, bool minimised, bool showTaskbarIcon = true, bool disabled = false, bool mouseInput = true, bool kbInput = true) {
		if (panel.GetParent() == null)
			panel.SetParent(GetEmbeddedPanel());

		panel.SetPopup(true);
		panel.SetKeyboardInputEnabled(kbInput);
		panel.SetMouseInputEnabled(mouseInput);

		if (!PopupList.Contains(panel)) {
			PopupList.Add(panel);
		}
		else {
			MovePopupToFront(panel);
		}
	}

	public void DestroyTextureID(in TextureID id) {
		TextureDictionary.DestroyTexture(id);
	}

	public bool DeleteTextureByID(in TextureID id) {
		TextureDictionary.DestroyTexture(id);
		return false;
	}

	public void DrawFilledRect(int x0, int y0, int x1, int y1) {
#if !GMOD_DLL
		Assert(InDrawing);
#endif

		if (FullyTransparent)
			return;

		Span<SurfaceVertex> rect = stackalloc SurfaceVertex[2];
		Span<SurfaceVertex> clippedRect = stackalloc SurfaceVertex[2];
		InitVertex(ref rect[0], x0, y0, 0, 0);
		InitVertex(ref rect[1], x1, y1, 1, 1);

		if (!Clip2D.ClipRect(in scissorRect, in rect[0], in rect[1], out clippedRect[0], out clippedRect[1]))
			return;

		InternalSetMaterial();
		DrawQuad(in clippedRect[0], in clippedRect[1], in DrawColor);
	}

	public void DrawFilledRectArray(Span<Rectangle> rects, int numRects) {
#if !GMOD_DLL
		Assert(InDrawing);
#endif

		if (FullyTransparent)
			return;

		if (Mesh == null)
			return;

		InternalSetMaterial();

		meshBuilder.Begin(Mesh, MaterialPrimitiveType.Quads, numRects);

		Span<SurfaceVertex> rect = stackalloc SurfaceVertex[2];
		Span<SurfaceVertex> clippedRect = stackalloc SurfaceVertex[2];
		for (int i = 0; i < numRects; ++i) {
			InitVertex(ref rect[0], rects[i].X0, rects[i].Y0, 0, 0);
			InitVertex(ref rect[1], rects[i].X1, rects[i].Y1, 1, 1);

			if (!Clip2D.ClipRect(in scissorRect, in rect[0], in rect[1], out clippedRect[0], out clippedRect[1]))
				continue;

			ref SurfaceVertex ul = ref clippedRect[0];
			ref SurfaceVertex lr = ref clippedRect[1];

			meshBuilder.Position3f(ul.Position.X, ul.Position.Y, zPos);
			meshBuilder.Color4ubv(DrawColor);
			meshBuilder.TexCoord2f(0, ul.TexCoord.X, ul.TexCoord.Y);
			meshBuilder.AdvanceVertex();

			meshBuilder.Position3f(lr.Position.X, ul.Position.Y, zPos);
			meshBuilder.Color4ubv(DrawColor);
			meshBuilder.TexCoord2f(0, lr.TexCoord.X, ul.TexCoord.Y);
			meshBuilder.AdvanceVertex();

			meshBuilder.Position3f(lr.Position.X, lr.Position.Y, zPos);
			meshBuilder.Color4ubv(DrawColor);
			meshBuilder.TexCoord2f(0, lr.TexCoord.X, lr.TexCoord.Y);
			meshBuilder.AdvanceVertex();

			meshBuilder.Position3f(ul.Position.X, lr.Position.Y, zPos);
			meshBuilder.Color4ubv(DrawColor);
			meshBuilder.TexCoord2f(0, ul.TexCoord.X, lr.TexCoord.Y);
			meshBuilder.AdvanceVertex();
		}

		meshBuilder.End();
		Mesh.Draw();
	}

	public void DrawFlushText() {
		if (BatchedCharVertCount <= 0)
			return;

		IMaterial? pMaterial = TextureDictionary.GetTextureMaterial(BoundTexture);
		InternalSetMaterial(pMaterial);
		DrawQuadArray(BatchedCharVertCount / 2, BatchedCharVerts, DrawTextColor);
		BatchedCharVertCount = 0;
	}

	public void DrawGetTextPos(out int x, out int y) {
		x = TextPosX;
		y = TextPosY;
	}

	public bool DrawGetTextureFile(in TextureID id, out ReadOnlySpan<char> filename) {
		filename = default;

		if (!TextureDictionary.IsValidId(in id, out _))
			return false;

		IMaterial? material = TextureDictionary.GetTextureMaterial(in id);
		if (material == null)
			return false;

		filename = material.GetName();
		return true;
	}

	public int DrawGetTextureId(ReadOnlySpan<char> filename) {
		return TextureDictionary.FindTextureIdForTextureId(filename);
	}

	public void DrawGetTextureSize(in TextureID id, out int wide, out int tall) {
		TextureDictionary.GetTextureSize(id, out wide, out tall);
	}

	public void DrawLine(int x0, int y0, int x1, int y1) {
#if !GMOD_DLL
		Assert(InDrawing);
#endif

		if (FullyTransparent)
			return;

		Span<SurfaceVertex> verts = [new(new(x0, y0), new(0, 0)), new(new(x1, y1), new(1, 1))];
		InternalSetMaterial();
		DrawTexturedLineInternal(in verts[0], in verts[1]);
	}

	const int MAX_BATCHED_LINES = 4096;
	public struct Line
	{
		public SurfaceVertex A;
		public SurfaceVertex B;
		public Color Color;
	}
	Line[] BatchedLines = new Line[MAX_BATCHED_LINES];
	int BatchedLineCount;
	public void DrawFlushLines() {
		if (BatchedLineCount <= 0)
			return;

		IMaterial? pMaterial = TextureDictionary.GetTextureMaterial(BoundTexture);
		InternalSetMaterial(pMaterial);
		DrawLineArray(BatchedLines.AsSpan()[..BatchedLineCount]);
		BatchedLineCount = 0;
	}

	public void DrawLineArray(ReadOnlySpan<Line> lines) {
		meshBuilder.Begin(Mesh!, MaterialPrimitiveType.Lines, lines.Length);
		for (int i = 0, len = lines.Length; i < len; i++) {
			ref readonly Line line = ref lines[i];
			ref readonly SurfaceVertex a = ref line.A;
			ref readonly SurfaceVertex b = ref line.B;

			meshBuilder.Position3f(a.Position.X, a.Position.Y, zPos);
			meshBuilder.Color4ubv(line.Color);
			meshBuilder.TexCoord2fv(0, a.TexCoord);
			meshBuilder.AdvanceVertex();

			meshBuilder.Position3f(b.Position.X, b.Position.Y, zPos);
			meshBuilder.Color4ubv(line.Color);
			meshBuilder.TexCoord2fv(0, b.TexCoord);
			meshBuilder.AdvanceVertex();
		}
		meshBuilder.End();
		Mesh!.Draw();
	}

	private void DrawTexturedLineInternal(in SurfaceVertex a, in SurfaceVertex b) {
		Assert(!In3DPaintMode);

		if (FullyTransparent)
			return;

		Span<SurfaceVertex> verts = [a, b];

		verts[0].Position.X += TranslateX + pixelOffsetX;
		verts[0].Position.Y += TranslateY + pixelOffsetY;

		verts[1].Position.X += TranslateX + pixelOffsetX;
		verts[1].Position.Y += TranslateY + pixelOffsetY;

		Span<SurfaceVertex> clippedVerts = [a, b];

		if (!Clip2D.ClipLine(in scissorRect, verts, clippedVerts))
			return;

		ref Line line = ref BatchedLines[BatchedLineCount];
		line.A = verts[0];
		line.B = verts[1];
		line.Color = DrawColor;
		BatchedLineCount += 1;

		if (BatchedLineCount >= MAX_BATCHED_LINES - 1)
			DrawFlushLines();
	}

	public void DrawOutlinedRect(int x0, int y0, int x1, int y1) {
		DrawFilledRect(x0, y0, x1, y0 + 1);
		DrawFilledRect(x0, y1 - 1, x1, y1);
		DrawFilledRect(x0, y0 + 1, x0 + 1, y1 - 1);
		DrawFilledRect(x1 - 1, y0 + 1, x1, y1 - 1);
	}

	public void DrawOutlinedCircle(int x, int y, int radius, int segments) {
#if !GMOD_DLL
		Assert(InDrawing);
#endif
		Assert(!In3DPaintMode);

		if (FullyTransparent)
			return;

		InternalSetMaterial();
		meshBuilder.Begin(Mesh!, MaterialPrimitiveType.Lines, segments);
		SurfaceVertex[] vertex = new SurfaceVertex[2];
		vertex[0].Position = new(x + radius + TranslateX, y + TranslateY);
		vertex[0].TexCoord = new(1.0f, 0.5f);

		float invDelta = 2.0f * MathF.PI / segments;
		for (int i = 1; i <= segments; ++i) {
			float flRadians = i * invDelta;
			float ca = MathF.Cos(flRadians);
			float sa = MathF.Sin(flRadians);

			vertex[1].Position.X = x + TranslateX + (radius * ca);
			vertex[1].Position.Y = y + TranslateY + (radius * sa);
			vertex[1].TexCoord.X = 0.5f * (ca + 1.0f);
			vertex[1].TexCoord.Y = 0.5f * (sa + 1.0f);

			Span<SurfaceVertex> verts = [vertex[0], vertex[1]];
			Span<SurfaceVertex> clippedVerts = [vertex[0], vertex[1]];

			if (Clip2D.ClipLine(in scissorRect, verts, clippedVerts)) {
				ref SurfaceVertex a = ref clippedVerts[0];
				ref SurfaceVertex b = ref clippedVerts[1];

				meshBuilder.Position3f(a.Position.X, a.Position.Y, zPos);
				meshBuilder.Color4ubv(DrawColor);
				meshBuilder.TexCoord2fv(0, a.TexCoord);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(b.Position.X, b.Position.Y, zPos);
				meshBuilder.Color4ubv(DrawColor);
				meshBuilder.TexCoord2fv(0, b.TexCoord);
				meshBuilder.AdvanceVertex();
			}

			vertex[0].Position = vertex[1].Position;
			vertex[0].TexCoord = vertex[1].TexCoord;
		}

		meshBuilder.End();
		Mesh!.Draw();
	}

	public void DrawPolyLine(Span<Point> points) {
		throw new NotImplementedException();
	}

	IFont? CurrentFont;

	public void DrawPrintText(ReadOnlySpan<char> text, FontDrawType drawType = FontDrawType.Default) {
#if !GMOD_DLL
		Assert(InDrawing);
#endif

		if (text.IsEmpty)
			return;

		if (CurrentFont == null)
			return;
		text = text.SliceNullTerminatedString();

		int x = TextPosX + TranslateX;
		int y = TextPosY + TranslateY;

		int tall = GetFontTall(CurrentFont);
		TextureID lastTexId = TextureID.INVALID;

		int count = 0;
		Span<SurfaceVertex> quads = stackalloc SurfaceVertex[2 * text.Length];
		bool underlined = FontManager.GetFontUnderlined(CurrentFont);
		if (drawType == FontDrawType.Default)
			drawType = IsFontAdditive(CurrentFont) > 0 ? FontDrawType.Additive : FontDrawType.NonAdditive;

		int totalWidth = 0;
		Span<TextureID> texID = stackalloc TextureID[1];
		Span<CharTexCoord> texCoords = stackalloc CharTexCoord[1];

		for (int i = 0; i < text.Length; ++i) {
			char ch = text[i];
			GetCharABCwide(CurrentFont, ch, out int abcA, out int abcB, out int abcC);
			int textureWide = abcB;
			if (underlined) {
				textureWide += (abcA + abcC);
				x -= abcA;
			}

			float flA = abcA;
			float flWide = abcA + abcB + abcC;

			if (!char.IsWhiteSpace(ch) || underlined) {
				if (!FontTextureCache.GetTextureForChar(CurrentFont, drawType, ch, texID, texCoords))
					continue;

				if (texID[0] != lastTexId) {
					if (count != 0) {
						IMaterial material = TextureDictionary.GetTextureMaterial(lastTexId)!;
						InternalSetMaterial(material);
						DrawQuadArray(count, quads, DrawTextColor);
						count = 0;
					}

					lastTexId = texID[0];
				}

				ref SurfaceVertex ul = ref quads[2 * count];
				ref SurfaceVertex lr = ref quads[2 * count + 1];
				++count;

				ul.Position.X = x + totalWidth + MathF.Floor(abcA + 0.6f);
				ul.Position.Y = y;
				lr.Position.X = ul.Position.X + textureWide;
				lr.Position.Y = ul.Position.Y + tall;

				ul.TexCoord.X = texCoords[0].X0;
				ul.TexCoord.Y = texCoords[0].Y0;
				lr.TexCoord.X = texCoords[0].X1;
				lr.TexCoord.Y = texCoords[0].Y1;
			}
			totalWidth += (int)MathF.Floor(flWide + 0.6f);
		}

		if (count != 0) {
			IMaterial material = TextureDictionary.GetTextureMaterial(lastTexId)!;
			InternalSetMaterial(material);
			DrawQuadArray(count, quads, DrawTextColor);
			count = 0;
		}

		TextPosX += totalWidth;
	}

	private void DrawQuadArray(int quadCount, Span<SurfaceVertex> verts, in Color color, bool shouldClip = true) {
		Assert(!In3DPaintMode);

		if (Mesh == null)
			return;

		meshBuilder.Begin(Mesh, MaterialPrimitiveType.Quads, quadCount);

		SurfaceVertex ulc;
		SurfaceVertex lrc;

		if (shouldClip) {
			for (int i = 0; i < quadCount; ++i) {
				if (!Clip2D.ClipRect(in scissorRect, verts[2 * i], verts[2 * i + 1], out ulc, out lrc)) {
					continue;
				}
				ref SurfaceVertex pulc = ref ulc;
				ref SurfaceVertex plrc = ref lrc;

				meshBuilder.Position3f(pulc.Position.X, pulc.Position.Y, zPos);
				meshBuilder.Color4ubv(color);
				meshBuilder.TexCoord2f(0, pulc.TexCoord.X, pulc.TexCoord.Y);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(plrc.Position.X, pulc.Position.Y, zPos);
				meshBuilder.Color4ubv(color);
				meshBuilder.TexCoord2f(0, plrc.TexCoord.X, pulc.TexCoord.Y);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(plrc.Position.X, plrc.Position.Y, zPos);
				meshBuilder.Color4ubv(color);
				meshBuilder.TexCoord2f(0, plrc.TexCoord.X, plrc.TexCoord.Y);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(pulc.Position.X, plrc.Position.Y, zPos);
				meshBuilder.Color4ubv(color);
				meshBuilder.TexCoord2f(0, pulc.TexCoord.X, plrc.TexCoord.Y);
				meshBuilder.AdvanceVertex();
			}
		}
		else {
			for (int i = 0; i < quadCount; ++i) {
				ref SurfaceVertex pulc = ref verts[2 * i];
				ref SurfaceVertex plrc = ref verts[2 * i + 1];

				meshBuilder.Position3f(pulc.Position.X, pulc.Position.Y, zPos);
				meshBuilder.Color4ubv(color);
				meshBuilder.TexCoord2f(0, pulc.TexCoord.X, pulc.TexCoord.Y);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(plrc.Position.X, pulc.Position.Y, zPos);
				meshBuilder.Color4ubv(color);
				meshBuilder.TexCoord2f(0, plrc.TexCoord.X, pulc.TexCoord.Y);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(plrc.Position.X, plrc.Position.Y, zPos);
				meshBuilder.Color4ubv(color);
				meshBuilder.TexCoord2f(0, plrc.TexCoord.X, plrc.TexCoord.Y);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(pulc.Position.X, plrc.Position.Y, zPos);
				meshBuilder.Color4ubv(color);
				meshBuilder.TexCoord2f(0, pulc.TexCoord.X, plrc.TexCoord.Y);
				meshBuilder.AdvanceVertex();
			}
		}

		meshBuilder.End();
		Mesh.Draw();
	}

	public int GetFontTall(IFont? font) {
		return FontManager.GetFontTall(font);
	}

	public void DrawSetColor(int r, int g, int b, int a) {
		DrawColor.SetColor(r, g, b, (int)(a * AlphaMultiplier));
	}

	public void DrawSetColor(in Color color) {
		DrawColor = color with { A = (byte)Math.Clamp(color.A * AlphaMultiplier, 0, 255) };
	}

	public void DrawSetTextColor(int r, int g, int b, int a) {
		int adjustedAlpha = (int)(a * AlphaMultiplier);

		if (r != DrawTextColor[0] || g != DrawTextColor[1] || b != DrawTextColor[2] || adjustedAlpha != DrawTextColor[3]) {
			DrawFlushText();

			DrawTextColor[0] = (byte)r;
			DrawTextColor[1] = (byte)g;
			DrawTextColor[2] = (byte)b;
			DrawTextColor[3] = (byte)adjustedAlpha;
		}
	}

	public void DrawSetTextColor(in Color color) {
		DrawSetTextColor(color.R, color.G, color.B, color.A);
	}

	public void DrawSetTextFont(IFont? font) {
		CurrentFont = font;
	}

	public void DrawSetTextPos(int x, int y) {
		TextPosX = x;
		TextPosY = y;
	}

	TextureID BoundTexture;

	public void DrawSetTexture(in TextureID id) {
		if (id != BoundTexture) {
			DrawFlushText();
			DrawFlushLines();
			BoundTexture = id;
		}
	}

	public void DrawSetTextureFile(in TextureID id, in ReadOnlySpan<char> filename, int hardwareFilter, bool forceReload) {
		TextureDictionary.BindTextureToFile(id, filename);
		DrawSetTexture(id);
	}

	public void DrawSetTextureRGBA(in TextureID id, Span<byte> rgba, int wide, int tall, int a, bool b) {
		TextureDictionary.SetTextureRGBAEx(in id, rgba, wide, tall, ImageFormat.RGBA8888, false);
	}

	public unsafe void DrawSetTextureRGBAEx(in TextureID id, nint rgba, int wide, int tall, ImageFormat format) {
		Span<byte> data = new((void*)rgba, wide * tall * 4);
		TextureDictionary.SetTextureRGBAEx(in id, data, wide, tall, format, true);
	}

	public void DrawUpdateRegionTextureRGBA(in TextureID id, int x, int y, nint rgba, int wide, int tall, ImageFormat format) {
		TextureDictionary.UpdateSubTextureRGBA(in id, x, y, rgba, wide, tall, format);
	}

	public void DrawTexturedRect(int x0, int y0, int x1, int y1) {
#if !GMOD_DLL
		Assert(InDrawing);
#endif

		if (DrawColor.A == 0)
			return;

		TextureDictionary.GetTextureTexCoords(in BoundTexture, out float s0, out float t0, out float s1, out float t1);

		Span<SurfaceVertex> rect = stackalloc SurfaceVertex[2];
		Span<SurfaceVertex> clippedRect = stackalloc SurfaceVertex[2];
		InitVertex(ref rect[0], x0, y0, s0, t0);
		InitVertex(ref rect[1], x1, y1, s1, t1);

		// Fully clipped?
		if (!Clip2D.ClipRect(in scissorRect, rect[0], rect[1], out clippedRect[0], out clippedRect[1]))
			return;

		IMaterial? material = TextureDictionary.GetTextureMaterial(in BoundTexture);
		InternalSetMaterial(material);
		DrawQuad(in clippedRect[0], in clippedRect[1], in DrawColor);
	}

	public void DrawTexturedSubRect(int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1) {
#if !GMOD_DLL
		Assert(InDrawing);
#endif

		if (DrawColor.A == 0)
			return;

		TextureDictionary.GetTextureTexCoords(in BoundTexture, out float ts0, out float tt0, out float ts1, out float tt1);

		float ssize = ts1 - ts0;
		float tsize = tt1 - tt0;

		s0 = ts0 + s0 * (ssize);
		s1 = ts0 + s1 * (ssize);
		t0 = tt0 + t0 * (tsize);
		t1 = tt0 + t1 * (tsize);

		Span<SurfaceVertex> rect = stackalloc SurfaceVertex[2];
		Span<SurfaceVertex> clippedRect = stackalloc SurfaceVertex[2];
		InitVertex(ref rect[0], x0, y0, s0, t0);
		InitVertex(ref rect[1], x1, y1, s1, t1);

		if (!Clip2D.ClipRect(in scissorRect, rect[0], rect[1], out clippedRect[0], out clippedRect[1]))
			return;

		IMaterial? material = TextureDictionary.GetTextureMaterial(in BoundTexture);
		InternalSetMaterial(material);
		DrawQuad(in clippedRect[0], in clippedRect[1], in DrawColor);
	}

	public void EnableMouseCapture(IPanel panel, bool state) {

	}

	public void FlashWindow(IPanel panel, bool state) => launcherMgr.FlashWindow(state);

	public void GetAbsoluteWindowBounds(out int x, out int y, out int wide, out int tall) {
		throw new NotImplementedException();
	}

	public int GetCharacterWidth(IFont font, int ch) {
		throw new NotImplementedException();
	}

	public IPanel GetEmbeddedPanel() {
		return EmbeddedPanel!;
	}

	public IPanel? GetPopup(int index) {
		if (index < 0)
			return null;
		if (index >= PopupList.Count)
			return null;

		return PopupList[index];
	}

	public int GetPopupCount() {
		return PopupList.Count;
	}

	const int BASE_WIDTH = 640;
	const int BASE_HEIGHT = 480;

	public void GetProportionalBase(out int width, out int height) {
		width = BASE_WIDTH;
		height = BASE_HEIGHT;
	}

	public void GetScreenSize(out int wide, out int tall) {
		if (ScreenSizeOverride.Active) {
			wide = ScreenSizeOverride.X;
			tall = ScreenSizeOverride.Y;
			return;
		}

		IMatRenderContext renderContext = materials.GetRenderContext();
		renderContext.GetViewport(out _, out _, out wide, out tall);
	}

	public void GetTextSize(IFont? font, ReadOnlySpan<char> text, out int wide, out int tall) {
		FontManager.GetTextSize(font, text, out wide, out tall);
	}

	public IPanel? GetTopmostPopup() {
		return null;
	}

	ScreenOverride ScreenPosOverride;
	ScreenOverride ScreenSizeOverride;

	int[] WorkSpaceInsets = new int[4];

	public void GetWorkspaceBounds(out int x, out int y, out int wide, out int tall) {
		if (ScreenSizeOverride.Active) {
			x = y = 0;
			wide = ScreenSizeOverride.X;
			tall = ScreenSizeOverride.Y;
			return;
		}

		x = WorkSpaceInsets[0];
		y = WorkSpaceInsets[1];
		EmbeddedPanel!.GetSize(out wide, out tall);

		wide -= WorkSpaceInsets[2];
		tall -= WorkSpaceInsets[3];
	}

	public void SetWorkspaceInsets(int left, int top, int right, int bottom) {
		WorkSpaceInsets[0] = left;
		WorkSpaceInsets[1] = top;
		WorkSpaceInsets[2] = right;
		WorkSpaceInsets[3] = bottom;
	}

	public bool HasCursorPosFunctions() => false;

	public bool HasFocus() {
		return true;
	}

	public void Invalidate(IPanel panel) {

	}

	public bool IsCursorVisible() => CursorAlwaysVisible || CurrentHCursor != (nint)CursorCode.None;

	public bool IsMinimized(IPanel panel) {
		return false;
	}

	public bool IsTextureIDValid(in TextureID id) {
		return true;
	}

	public bool IsWithin(int x, int y) {
		return true;
	}

	public void LockCursor() {
		LockCursor(true);
	}

	public void LockCursor(bool state) {
		CursorLocked = state;
		ActivateCurrentCursor();
	}

	static KeyValues OnMovedPopupToFront = new("OnMovedPopupToFront");

	public void MovePopupToBack(IPanel panel) {
		LinkVGUI();

		int index = PopupList.IndexOf(panel);
		if (index == -1)
			return;

		PopupList.RemoveAt(index);
		PopupList.Insert(0, panel);
	}

	public void MovePopupToFront(IPanel panel) {
		int index = PopupList.IndexOf(panel);
		if (index == -1)
			return;

		PopupList.RemoveAt(index);
		PopupList.Add(panel);

		// If the modal panel isn't a parent, restore it to the top, to prevent a hard lock
		LinkVGUI();
		if (VGuiInput.GetAppModalSurface() != null) {
			if (!panel.HasParent(VGuiInput.GetAppModalSurface()!)) {
				index = PopupList.IndexOf(panel);
				if (index != -1) {
					PopupList.RemoveAt(index);
					PopupList.Add(panel);
				}
			}
		}

		VGui.PostMessage(panel, OnMovedPopupToFront, null);
	}

	public bool NeedKBInput() {
		throw new NotImplementedException();
	}

	public void PaintTraverse(IPanel panel) {
		PaintTraverseEx(panel, false);
	}

	IPanel? RestrictedPanel;

	public void PaintTraverseEx(IPanel panel, bool paintPopups) {
		if (!panel.IsVisible())
			return;

		using MatRenderContextPtr renderContext = new(materials);
		bool topLevelDraw = false;
		if (InDrawing == false) {
			topLevelDraw = true;
			StartDrawing();

			renderContext.ClearBuffers(false, true, true);
			renderContext.SetStencilEnable(true);
			renderContext.SetStencilFailOperation(StencilOperation.Keep);
			renderContext.SetStencilZFailOperation(StencilOperation.Keep);
			renderContext.SetStencilPassOperation(StencilOperation.Replace);
			renderContext.SetStencilCompareFunction(StencilComparisonFunction.GreaterEqual);
			renderContext.SetStencilReferenceValue(0);
			renderContext.SetStencilTestMask(0xFFFFFFFF);
			renderContext.SetStencilWriteMask(0xFFFFFFFF);
		}

		float oldZPos = zPos;
		zPos = 0;
		if (panel == GetEmbeddedPanel()) {
			if (RestrictedPanel != null) {
				RestrictedPanel.GetParent()?.PaintTraverse(true);
			}
			else {
				panel.PaintTraverse(forceRepaint: true);
			}
		}
		else {
			if (!paintPopups || !panel.IsPopup())
				panel.PaintTraverse(true);
		}

		if (paintPopups) {
			int popups = GetPopupCount();
			if (popups > 254)
				Warning("Too many popups! Rendering will be bad!\n");

			int stencilRef = 254;
			for (int i = popups - 1; i >= 0; --i) {
				IPanel? popupPanel = GetPopup(i);

				if (popupPanel == null)
					continue;

				if (!popupPanel.IsFullyVisible())
					continue;

				if (!IsPanelUnderRestrictedPanel(popupPanel))
					continue;

				bool isTopmostPopup = popupPanel.IsTopmostPopup();

				renderContext.SetStencilReferenceValue(isTopmostPopup ? 255 : stencilRef);
				--stencilRef;

				zPos = i / (float)popups;
				popupPanel.PaintTraverse(true);
			}
		}

		zPos = oldZPos;

		if (topLevelDraw) {
			renderContext.SetStencilEnable(false);
			FinishDrawing();
		}
	}

	private bool IsPanelUnderRestrictedPanel(IPanel? panel) {
		if (RestrictedPanel == null)
			return true;

		while (panel != null) {
			if (panel == RestrictedPanel)
				return true;
			panel = panel.GetParent();
		}

		return false;
	}

	private void FinishDrawing() {
		EnableScissor(false);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PopMatrix();

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PopMatrix();

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PopMatrix();

#if !GMOD_DLL
		Assert(InDrawing);
#endif
		InDrawing = false;
	}

	int BatchedCharVertCount;
	public void PopMakeCurrent(IPanel panel) {
		if (BatchedCharVertCount > 0)
			DrawFlushText();
		if (BatchedLineCount > 0)
			DrawFlushLines();

		int top = PaintStateStack.Count - 1;
		Assert(top >= 0);
		Assert(PaintStateStack[top].Panel == panel);
		PaintStateStack.Pop();
		if (top > 0)
			SetupPaintState(PaintStateStack[top - 1]);
	}

	readonly RefStack<PaintState> PaintStateStack = new();

	public void PushMakeCurrent(IPanel panel, bool useInsets) {
		Span<int> inSets = [0, 0, 0, 0];
		Span<int> absExtents = stackalloc int[4];
		Span<int> clipRect = stackalloc int[4];

		if (useInsets)
			panel.GetInset(out inSets[0], out inSets[1], out inSets[2], out inSets[3]);

		panel.GetAbsPos(out absExtents[0], out absExtents[1]);
		int wide, tall;
		panel.GetSize(out wide, out tall);
		absExtents[2] = absExtents[0] + wide;
		absExtents[3] = absExtents[1] + tall;

		panel.GetClipRect(out clipRect[0], out clipRect[1], out clipRect[2], out clipRect[3]);


		ref PaintState paintState = ref PaintStateStack.Push();
		paintState.Panel = panel;

		// Determine corrected top left origin
		paintState.TranslateX = inSets[0] + absExtents[0] - SurfaceExtents[0];
		paintState.TranslateY = inSets[1] + absExtents[1] - SurfaceExtents[1];

		// Setup clipping rectangle for scissoring
		paintState.ScissorLeft = clipRect[0] - SurfaceExtents[0];
		paintState.ScissorTop = clipRect[1] - SurfaceExtents[1];
		paintState.ScissorRight = clipRect[2] - SurfaceExtents[0];
		paintState.ScissorBottom = clipRect[3] - SurfaceExtents[1];

		SetupPaintState(in paintState);
	}

	private void SetupPaintState(in PaintState paintState) {
		TranslateX = paintState.TranslateX;
		TranslateY = paintState.TranslateY;
		SetScissorRect(paintState.ScissorLeft, paintState.ScissorTop, paintState.ScissorRight, paintState.ScissorBottom);
	}

	private void SetScissorRect(int left, int top, int right, int bottom) {
		Assert(left <= right);
		Assert(top <= bottom);

		if (scissorRect.Left == left && scissorRect.Right == right &&
			 scissorRect.Top == top && scissorRect.Bottom == bottom)
			return;

		scissorRect.Left = left;
		scissorRect.Top = top;
		scissorRect.Right = right;
		scissorRect.Bottom = bottom;
	}

	public bool RecreateContext(IPanel panel) {
		throw new NotImplementedException();
	}

	public void ReleasePanel(IPanel panel) {
		RemovePopup(panel);
		// todo: m_Titles remove
	}

	void RemovePopup(IPanel panel) {
		for (int i = PopupList.Count - 1; i >= 0; i--) {
			IPanel? popup = PopupList[i];
			if (popup != null && popup != panel)
				continue;

			PopupList.RemoveAt(i);
			break;
		}
	}

	public void DrawTexturedPolygon(Span<SurfaceVertex> vertices, bool clipVertices = true) {
		Assert(!In3DPaintMode);
#if !GMOD_DLL
		Assert(InDrawing);
#endif

		// Don't even bother drawing fully transparent junk
		if (vertices.IsEmpty || vertices.Length == 0 || DrawColor[3] == 0)
			return;

		if (clipVertices) {
			int iCount;
			Span<SurfaceVertex> clippedVerts;
			iCount = Clip2D.ClipPolygon(in scissorRect, vertices, TranslateX, TranslateY, out clippedVerts);
			if (iCount <= 0)
				return;

			IMaterial? material = TextureDictionary.GetTextureMaterial(BoundTexture);
			InternalSetMaterial(material);

			meshBuilder.Begin(Mesh!, MaterialPrimitiveType.Polygon, iCount);

			for (int i = 0; i < iCount; ++i) {
				meshBuilder.Position3f(clippedVerts[i].Position.X, clippedVerts[i].Position.Y, zPos);
				meshBuilder.Color4ubv(DrawColor);
				meshBuilder.TexCoord2fv(0, [clippedVerts[i].TexCoord.X, clippedVerts[i].TexCoord.Y]);
				meshBuilder.AdvanceVertex();
			}

			meshBuilder.End();
			Mesh!.Draw();
		}
		else {
			IMaterial? material = TextureDictionary.GetTextureMaterial(BoundTexture);
			InternalSetMaterial(material);

			meshBuilder.Begin(Mesh!, MaterialPrimitiveType.Polygon, vertices.Length);

			for (int i = 0; i < vertices.Length; ++i) {
				meshBuilder.Position3f(vertices[i].Position.X + TranslateX, vertices[i].Position.Y + TranslateY, zPos);
				meshBuilder.Color4ubv(DrawColor);
				meshBuilder.TexCoord2fv(0, [vertices[i].TexCoord.X, vertices[i].TexCoord.Y]);
				meshBuilder.AdvanceVertex();
			}

			meshBuilder.End();
			Mesh!.Draw();
		}
	}

	public IPanel? GetRestrictedPanel() => RestrictedPanel;
	public void RestrictPaintToSinglePanel(IPanel? panel) {
		LinkVGUI();
		if (panel != null && RestrictedPanel != null && RestrictedPanel == VGuiInput.GetAppModalSurface())
			return;

		RestrictedPanel = panel;

		if (VGuiInput.GetAppModalSurface() == null)
			VGuiInput.SetAppModalSurface(panel);
	}

	int LastInputPollCount;
	bool AppDrivesInput;

	public void RunFrame() {
		int pollCount = InputSystem.GetPollCount();
		if (LastInputPollCount == pollCount)
			return;

		if (!AppDrivesInput && LastInputPollCount != pollCount - 1) {
			Assert(false);
			Warning("Vgui is losing input messages!\n");
		}

		LastInputPollCount = pollCount;

		if (AppDrivesInput)
			return;

		// Generate all input messages
		long eventCount = InputSystem.GetEventCount();
		foreach (var ev in InputSystem.GetEventData())
			HandleInputEvent(in ev);
	}

	public void SetAsToolBar(IPanel panel, bool state) {
		throw new NotImplementedException();
	}

	public void SetAsTopMost(IPanel panel, bool state) {
		throw new NotImplementedException();
	}

	public void SetCursor(CursorCode cursor) => SetCursor((HCursor)cursor);
	public void SetCursor(HCursor cursor) {
		if (CurrentHCursor != cursor) {
			CurrentHCursor = cursor;
			CursorSelect(cursor);
		}
	}

	float SoftwareCursorOffsetX, SoftwareCursorOffsetY;

	private void CursorSelect(HCursor cursor) {
		if ((cursor == (nint)CursorCode.AlwaysVisiblePush) || (cursor == (nint)CursorCode.AlwaysVisiblePop)) {
			ForceCursorVisibleCount += (cursor == (nint)CursorCode.AlwaysVisiblePush ? 1 : -1);
			Assert(ForceCursorVisibleCount >= 0);

			if ((ForceCursorVisibleCount != 0) != CursorVisible)
				ActivateCurrentCursor();

			return;
		}

		if (CursorLocked)
			return;

		switch ((CursorCode)cursor) {
			case CursorCode.User:
			case CursorCode.None:
			case CursorCode.Blank:
				CursorVisible = false;
				break;

			default:
				Assert(false);
				cursor = (nint)CursorCode.Arrow;
				goto case CursorCode.Arrow;

			case CursorCode.Arrow:
			case CursorCode.WaitArrow:
			case CursorCode.IBeam:
			case CursorCode.Hourglass:
			case CursorCode.Crosshair:
			case CursorCode.Up:
			case CursorCode.SizeNWSE:
			case CursorCode.SizeNESW:
			case CursorCode.SizeWE:
			case CursorCode.SizeNS:
			case CursorCode.SizeAll:
			case CursorCode.No:
			case CursorCode.Hand:
				CursorVisible = true;
				if (!SoftwareCursorActive)
					CurrentCursor = launcherMgr.GetHardwareCursor(cursor);
				else
					CurrentCursor = launcherMgr.GetSoftwareCursor(cursor, out SoftwareCursorOffsetX, out SoftwareCursorOffsetY);

				break;
		}
		ActivateCurrentCursor();
	}

	public void SetCursorAlwaysVisible(bool visible) {
		CursorAlwaysVisible = visible;
	}

	public void SetEmbeddedPanel(IPanel panel) {
		EmbeddedPanel = panel;
		IsDefault = EmbeddedPanel == DefaultEmbeddedPanel;
		EmbeddedPanel.RequestFocus();
	}

	public bool HandleInputEvent(in InputEvent ev) {
		LinkVGUI();
		if (AppDrivesInput)
			VGuiInput.UpdateButtonState(in ev);

		return InputHandleInputEvent(in ev);
	}
	bool InputHandleInputEvent(in InputEvent ev) {
		LinkVGUI();
		switch (ev.Type) {
			case InputEventType.IE_ButtonPressed: {
					ButtonCode code = (ButtonCode)ev.Data2;
					if (code.IsKeyCode())
						return VGuiInput.InternalKeyCodePressed(code);

					if (code.IsMouseCode())
						return VGuiInput.InternalMousePressed(code);

				}
				break;
			case InputEventType.IE_ButtonReleased: {
					ButtonCode code = (ButtonCode)ev.Data2;
					if (code.IsKeyCode())
						return VGuiInput.InternalKeyCodeReleased(code);

					if (code.IsMouseCode())
						return VGuiInput.InternalMouseReleased(code);
				}
				break;
			case InputEventType.IE_ButtonDoubleClicked: {
					ButtonCode code = (ButtonCode)ev.Data2;
					if (code.IsMouseCode())
						return VGuiInput.InternalMouseDoublePressed(code);
				}
				break;

			case InputEventType.IE_AnalogValueChanged: {
					if ((AnalogCode)ev.Data == AnalogCode.MouseWheel)
						return VGuiInput.InternalMouseWheeled(ev.Data3);
					if ((AnalogCode)ev.Data == AnalogCode.MouseXY)
						return VGuiInput.InternalCursorMoved(ev.Data2, ev.Data3);
				}
				break;

			case InputEventType.Gui_KeyCodeTyped: {
					VGuiInput.InternalKeyCodeTyped((ButtonCode)ev.Data);
				}
				return true;

			case InputEventType.Gui_KeyTyped: {
					VGuiInput.InternalKeyTyped((char)ev.Data);
				}
				return true;

			case InputEventType.System_Quit:
				// Referencing VGUI here will result in circular dependency...
				// so im gonna resolve it this way since System_Quit doesnt run frequently...
				services.GetRequiredService<IVGui>().Quit();
				return false;

			case InputEventType.Gui_Close:
				// FIXME: Change this so we don't stop until 'save' occurs, etc.
				services.GetRequiredService<IVGui>().Stop();
				return true;

			case InputEventType.Gui_SetCursor:
				ActivateCurrentCursor(); // todo
				return true;

			// All of IME is todo
			case InputEventType.Gui_IMESetWindow: {
					return true;
				}

			case InputEventType.Gui_LocateMouseClick:
				VGuiInput.InternalCursorMoved(ev.Data, ev.Data2);
				return true;

			case InputEventType.Gui_InputLanguageChanged:
				VGuiInput.OnInputLanguageChanged();
				return true;

			case InputEventType.Gui_IMEStartComposition:
				return true;

			case InputEventType.Gui_IMEComposition:
				return true;

			case InputEventType.Gui_IMEEndComposition:
				return true;

			case InputEventType.Gui_IMEShowCandidates:
				return true;

			case InputEventType.Gui_IMEChangeCandidates:
				return true;

			case InputEventType.Gui_IMECloseCandidates:
				return true;

			case InputEventType.Gui_IMERecomputeModes:
				return true;
		}

		return false;
	}

	bool SoftwareCursorActive;
	bool CursorVisible;
	bool CursorAlwaysVisible;
	bool CursorLocked;
	int ForceCursorVisibleCount;

	HCursor CurrentHCursor;
	ICursor? CurrentCursor;
	ICursor? CurrentlySetCursor;

	private void ActivateCurrentCursor() {
		if (SoftwareCursorActive) {
			HideHardwareCursor();
			return;
		}

		if (CursorVisible || ForceCursorVisibleCount > 0) {
			if (CurrentCursor != CurrentlySetCursor) {
				CurrentlySetCursor = CurrentCursor;
				launcherMgr.SetMouseCursor(CurrentlySetCursor);
				launcherMgr.SetMouseVisible(true);
			}
		}
	}

	private void HideHardwareCursor() {
		throw new NotImplementedException();
	}

	public bool SetFontGlyphSet(IFont font, ReadOnlySpan<char> windowsFontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags, int rangeMin = 0, int rangeMax = 0) {
		return FontManager.SetFontGlyphSet(font, windowsFontName, tall, weight, blur, scanlines, flags, rangeMin, rangeMax);
	}

	public void ClearTemporaryFontCache() => FontManager.ClearTemporaryFontCache();

	public void SetForegroundWindow(IPanel panel) {
		throw new NotImplementedException();
	}

	public void SetMinimized(IPanel panel, bool state) {
		if (state) {
			panel.SetVisible(false);
		}
	}

	public void PlaySound(ReadOnlySpan<char> fileName) {
		play?.Invoke(fileName);
	}

	public void SetPanelVisible(IPanel panel, bool state) {
		// throw new NotImplementedException();
	}

	public void SetTitle(IPanel panel, ReadOnlySpan<char> title) {
	}

	public void SetTopLevelFocus(IPanel? panel) {
		while (panel != null) {
			if (panel.IsPopup() && panel.IsMouseInputEnabled()) {
				BringToFront(panel);
				break;
			}

			panel = panel.GetParent();
		}
	}

	public void SetTranslateExtendedKeys(bool state) {
		throw new NotImplementedException();
	}

	public bool ShouldPaintChildPanel(IPanel childPanel) {
		if (RestrictedPanel != null && RestrictedPanel != childPanel && !childPanel.HasParent(RestrictedPanel))
			return false;

		return !childPanel.IsPopup();
	}

	public void SolveTraverse(IPanel panel, bool forceApplySchemeSettings = false) {
		InternalSchemeSettingsTraverse(panel, forceApplySchemeSettings);
		InternalThinkTraverse(panel);
		InternalSolveTraverse(panel);
	}

	private void InternalSchemeSettingsTraverse(IPanel panel, bool forceApplySchemeSettings) {
		panel.TraverseLevel(1);

		for (int i = 0; i < panel.GetChildCount(); ++i) {
			IPanel child = panel.GetChild(i);
			if (forceApplySchemeSettings || child.IsVisible()) {
				InternalSchemeSettingsTraverse(child, forceApplySchemeSettings);
			}
		}

		panel.PerformApplySchemeSettings();
		panel.TraverseLevel(-1);
	}

	private void InternalSolveTraverse(IPanel panel) {
		panel.TraverseLevel(1);
		panel.Solve();

		for (int i = 0; i < panel.GetChildCount(); ++i) {
			IPanel child = panel.GetChild(i);
			if (child.IsVisible()) {
				InternalSolveTraverse(child);
			}
		}

		panel.TraverseLevel(-1);
	}

	public bool SupportsFeature(SurfaceFeature feature) {
		switch (feature) {
			case SurfaceFeature.AntialiasedFonts:
			case SurfaceFeature.OutlineFonts:
				return true;
			default:
				return false; // Most things arent implemented yet...
		}
	}


	public void SwapBuffers(IPanel panel) {

	}

	public void UnlockCursor() {
		LockCursor(false);
	}

	public void PaintSoftwareCursor() {

	}

	public int GetTextureNumFrames(in TextureID id) {
		IMaterial? material = TextureDictionary.GetTextureMaterial(in id);

		if (material == null)
			return 0;

		return material.GetNumAnimationFrames();
	}

	public void DrawSetTextureFrame(in TextureID id, int frame, ref TokenCache frameCache) {
		IMaterial? material = TextureDictionary.GetTextureMaterial(in id);
		if (material == null)
			return;

		int totalFrames = material.GetNumAnimationFrames();
		if (totalFrames == 0)
			return;

		IMaterialVar? frameVar = material.FindVarFast("$frame", ref frameCache);
		frameVar?.SetIntValue(frame % totalFrames);
	}

	event VGuiPlayFunc? play;

	public void InstallPlaySoundFunc(VGuiPlayFunc func) => play += func;

	List<UtlSymbol> BitmapFontFileNames = [];
	Dictionary<UtlSymId_t, UtlSymbol> BitmapFontFileMapping = [];
	public bool AddBitmapFontFile(ReadOnlySpan<char> fontFileName) {
		bool found = false;
		found = FileSystem.FileExists(fontFileName, null);
		if (!found) {
			Msg($"Couldn't find bitmap font file '{fontFileName}'\n");
			return false;
		}
		Span<char> variantPath = stackalloc char[MAX_PATH];
		Span<char> path = stackalloc char[MAX_PATH];
		fontFileName.CopyTo(variantPath);

		((ReadOnlySpan<char>)variantPath[..fontFileName.Length]).ToLowerInvariant(path);
		path = path[..fontFileName.Length];
		ulong sym = path.Hash();

		int i;
		for (i = 0; i < BitmapFontFileNames.Count; i++) {
			if (BitmapFontFileNames[i].Hash() == sym)
				break;
		}
		if (i < BitmapFontFileNames.Count) {
			BitmapFontFileNames.Add(new(path));
			FileSystem.GetLocalCopy(path);
		}

		return true;
	}

	public void SetBitmapFontName(string name, ReadOnlySpan<char> fontFile) {
		Span<char> fontPath = stackalloc char[MAX_PATH];
		fontFile.ToLowerInvariant(fontPath);
		BitmapFontFileMapping[new UtlSymbol(name)] = new(fontPath);
	}

	public float DrawGetAlphaMultiplier() {
		return AlphaMultiplier;
	}

	public void DrawSetAlphaMultiplier(float alpha) {
		AlphaMultiplier = alpha;
	}

	public void OffsetAbsPos(ref int x, ref int y) {
		if (!ScreenPosOverride.Active)
			return;
		x += ScreenPosOverride.X;
		y += ScreenPosOverride.Y;
	}

	IWindow? window;
	public void AttachToWindow(IWindow? window, bool appDrivesInput) {
		InputDetachFromWindow(this.window);
		this.window = window;
		if (window != null) {
			InputAttachToWindow(this.window);
			AppDrivesInput = appDrivesInput;
		}
		else {
			AppDrivesInput = false;
		}
	}

	private void InputDetachFromWindow(IWindow? window) {

	}

	private void InputAttachToWindow(IWindow? window) {

	}

	public void EnableWindowsMessages(bool enabled) {

	}

	public ReadOnlySpan<char> GetBitmapFontName(ReadOnlySpan<char> name) {
		return BitmapFontFileMapping.TryGetValue(new UtlSymbol(name), out UtlSymbol fontPath)
			? fontPath.String() : "";
	}

	public void SetBitmapFontGlyphSet(IFont font, ReadOnlySpan<char> name, float scalex, float scaley, SurfaceFontFlags flags) {
		throw new NotImplementedException();
	}

	public int GetFontTallRequested(IFont? font) {
		return FontManager.GetFontTallRequested(font);
	}

	public int GetFontAscent(IFont? font, char ch) {
		throw new NotImplementedException();
	}

	public int IsFontAdditive(IFont? font) {
		return FontManager.IsFontAdditive(font);
	}

	public void GetCharABCwide(IFont? font, char ch, out int a, out int b, out int c) {
		FontManager.GetCharABCwide(font, ch, out a, out b, out c);
	}

	public int GetCharacterWidth(IFont? font, char ch) {
		return FontManager.GetCharacterWidth(font, ch);
	}


	internal void DrawSetSubTextureRGBA(in TextureID id, int drawX, int drawY, Span<byte> rgba, int subTextureWide, int subTextureTall) {
		TextureDictionary.SetSubTextureRGBA(in id, drawX, drawY, rgba, subTextureWide, subTextureTall, ImageFormat.RGBA8888);
	}

	public void DrawSetTextureMaterial(TextureID textureID, IMaterial material) {
		TextureDictionary.BindTextureToMaterial(in textureID, material);
		DrawSetTexture(in textureID);
	}

	internal void ReferenceProceduralMaterial(in TextureID id, in TextureID refID, IMaterial material) {
		TextureDictionary.BindTextureToMaterialReference(in id, in refID, material);
	}

	public void DrawString(ReadOnlySpan<char> str, FontDrawType drawType = FontDrawType.Default) {
		if (TextFullyTransparent)
			return;
		for (int i = 0; i < str.Length; i++) {
			char c = str[i];
			if (c == '\0')
				break;

			DrawChar(c);
		}
	}

	public void DrawChar(char ch, FontDrawType drawType = FontDrawType.Default) {
		if (DrawTextColor.A == 0)
			return;

		CharRenderInfo info = new();
		info.DrawType = drawType;
		if (DrawGetCharRenderInfo(ch, ref info)) {
			DrawRenderCharFromInfo(info);
		}
	}

	public void DrawRenderCharFromInfo(CharRenderInfo info) {
		if (!info.Valid)
			return;

		int x = info.X;
		DrawSetTexture(info.TextureId);
		DrawRenderCharInternal(info);
		x += (info.B + info.C);
		DrawSetTextPos(x, info.Y);
	}

	private void DrawRenderCharInternal(CharRenderInfo info) {
#if !GMOD_DLL
		Assert(InDrawing);
#endif

		if (info.ShouldClip) {
			Span<SurfaceVertex> clip = [info.Verts[0], info.Verts[1]];
			if (!Clip2D.ClipRect(in scissorRect, clip[0], clip[1], out info.Verts[0], out info.Verts[1]))
				return;
		}

		BatchedCharVertCount += 2;

		if (BatchedCharVertCount >= MAX_BATCHED_CHAR_VERTS - 2)
			DrawFlushText();
	}

	const int MAX_BATCHED_CHAR_VERTS = 4096;
	SurfaceVertex[] BatchedCharVerts = new SurfaceVertex[MAX_BATCHED_CHAR_VERTS];


	public bool DrawGetCharRenderInfo(char ch, ref CharRenderInfo info) {
#if !GMOD_DLL
		Assert(InDrawing);
#endif
		info.Valid = false;

		if (CurrentFont == null)
			return info.Valid;

		info.Valid = true;
		info.Character = ch;
		DrawGetTextPos(out info.X, out info.Y);

		info.CurrentFont = CurrentFont;
		info.FontTall = GetFontTall(CurrentFont);

		GetCharABCwide(CurrentFont, ch, out info.A, out info.B, out info.C);
		bool bUnderlined = FontManager.GetFontUnderlined(CurrentFont);

		if (!bUnderlined)
			info.X += info.A;

		info.TextureId = 0;
		Span<CharTexCoord> texCoords = stackalloc CharTexCoord[1];
		if (!FontTextureCache.GetTextureForChar(CurrentFont, info.DrawType, ch, new Span<TextureID>(ref info.TextureId), texCoords)) {
			info.Valid = false;
			return info.Valid;
		}

		int fontWide = info.B;
		if (bUnderlined) {
			fontWide += (info.A + info.C);
			info.X -= info.A;
		}

		if (info.TextureId != BoundTexture) {
			DrawFlushText();
			DrawFlushLines();
		}

		info.Verts = BatchedCharVerts.AsSpan()[BatchedCharVertCount..(BatchedCharVertCount + 2)];
		InitVertex(ref info.Verts[0], info.X, info.Y, texCoords[0].X0, texCoords[0].Y0);
		InitVertex(ref info.Verts[1], info.X + fontWide, info.Y + info.FontTall, texCoords[0].X1, texCoords[0].Y1);

		info.ShouldClip = true;

		return info.Valid;
	}

	public bool IsCursorLocked() => CursorLocked;

	public void GetKernedCharWidth(IFont? font, char ch, char before, char after, out float w, out float abcA) {
		FontManager.GetKernedCharWidth(font, ch, before, after, out w, out abcA, out _);
	}

	public bool IsScreenPosOverrideActive() => ScreenSizeOverride.Active;
	public bool IsScreenSizeOverrideActive() => ScreenSizeOverride.Active;

	public void ForceScreenPosOffset(bool active, int x, int y) {
		ScreenPosOverride.Active = active;
		ScreenPosOverride.X = x;
		ScreenPosOverride.Y = y;
	}
	public void ForceScreenSizeOverride(bool active, int w, int h) {
		ScreenSizeOverride.Active = active;
		ScreenSizeOverride.X = w;
		ScreenSizeOverride.Y = h;
	}

	public void OnScreenSizeChanged(int oldWidth, int oldHeight) {
		GetScreenSize(out int newWidth, out int newHeight);

		Msg($"Changing resolutions from ({oldWidth}, {oldHeight}) -> ({newWidth}, {newHeight})\n");

		IPanel panel = GetEmbeddedPanel();
		panel.SetSize(newWidth, newHeight);

		VGui.PostMessage(panel, new("OnScreenSizeChanged", "oldwide", oldWidth, "oldtall", oldHeight), null);
		VGui.RunFrame();

		ResetFontCaches();
	}

	private void ResetFontCaches() {
		FontTextureCache.Clear();

		BoundTexture = -1;

		FontManager.ClearAllFonts();
		SchemeManager.ReloadFonts();
		VGui.RunFrame();
	}

	readonly ILocalize localize = Singleton<ILocalize>();

	public int DrawTextLen(IFont font, ReadOnlySpan<char> text) {
		int i;
		int x = 0;
		int len = text.Length;

		for (i = 0; i < len; i++) {
			GetCharABCwide(font, text[i], out int a, out int b, out int c);

			x += b;
			x += c;
		}

		return x;
	}
	public int DrawColoredText(IFont? font, int x, int y, byte r, byte g, byte b, byte a, ReadOnlySpan<char> text) {
		DrawSetTextPos(x, y);
		DrawSetTextColor(r, g, b, a);
		DrawSetTextFont(font);
		ReadOnlySpan<char> input = localize.Find(text);
		DrawPrintText(input.IsEmpty ? text : input);

		int totalLength = DrawTextLen(font, text);

		return x + totalLength;
	}

	public void DrawColoredTextRect(IFont? font, int x, int y, int w, int h, byte r, byte g, byte b, byte a, ReadOnlySpan<char> text) {
		DrawSetTextPos(x, y);
		DrawSetTextColor(r, g, b, a);
		DrawSetTextFont(font);

		int yStep = GetFontTall(font);
		int startX = x;
		int endX = x + w;
		int endY = y + h;

		int chars = 0;

		Span<char> word = stackalloc char[512];
		for (int i = 0; i < text.Length; i += chars) {
			SearchForWordBreak(font, text[i..], out chars, out int pixels);

			if (text[i] == '\n') {
				x = startX;
				y += yStep;
				chars = 1;
				continue;
			}

			if (x + pixels >= endX) {
				x = startX;
				if (x + pixels >= endX)
					break;

				y += yStep;
			}

			if (y + yStep >= endY)
				break;

			if (chars <= 0) {
				chars = 1;
				continue;
			}

			text.Slice(i, chars).CopyTo(word);

			DrawSetTextPos(x, y);

			ReadOnlySpan<char> input = localize.Find(word[..chars]);
			DrawPrintText(input.IsEmpty ? word[..chars] : input);

			x += DrawTextLen(font, word[..chars]);
		}
	}

	private void SearchForWordBreak(IFont? font, ReadOnlySpan<char> text, out int chars, out int pixels) {
		chars = pixels = 0;
		while (true) {
			if (chars >= text.Length)
				break;

			char ch = text[chars];
			GetCharABCwide(font, ch, out int a, out int b, out int c);

			if (ch == 0 || ch <= 32) {
				if (ch == 32 && chars == 0) {
					pixels += b + c;
					chars++;
				}
				break;
			}

			pixels += b + c;
			chars++;
		}
	}

	public void DisableClipping(bool disable) {
		DrawFlushText();
		EnableScissor(!disable);
	}

	public void GetClippingRect(out int left, out int top, out int right, out int bottom, out bool clippingDisabled) {
		left = scissorRect.Left;
		top = scissorRect.Top;
		right = scissorRect.Right;
		bottom = scissorRect.Bottom;
		clippingDisabled = !scissorRect.Scissor;
	}

#if GMOD_DLL
	public void DrawGetColor(out Color color) => color = DrawColor;
	public void DrawGetTextColor(out Color color) => color = DrawTextColor;

	public void DrawGetTranslate(out int x, out int y) {
		x = TranslateX;
		y = TranslateY;
	}
#endif

	public void PushFullscreenViewport() {
		using MatRenderContextPtr renderContext = new(materials);

		// the viewport x/y will be wrong because the render target is only for one eye. 
		// Ask the surface for that information instead.
		GetFullscreenViewportAndRenderTarget(out int vx, out int vy, out int vw, out int vh, out ITexture? renderTarget);
		renderContext.PushRenderTargetAndViewport(renderTarget, null, vx, vy, vw, vh);

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
		renderContext.Scale(1, -1, 1);

		renderContext.Ortho(pixelOffsetX, pixelOffsetY, vw + pixelOffsetX, vh + pixelOffsetY, -1.0f, 1.0f);

		DisableClipping(true);
	}


	int FullscreenViewportX;
	int FullscreenViewportY;
	int FullscreenViewportWidth;
	int FullscreenViewportHeight;
	ITexture? FullscreenRenderTarget;
	public void SetFullscreenViewportAndRenderTarget(int x, int y, int w, int h, ITexture? renderTarget) {
		FullscreenViewportX = x;
		FullscreenViewportY = y;
		FullscreenViewportWidth = w;
		FullscreenViewportHeight = h;
		FullscreenRenderTarget = renderTarget;
	}
	public void GetFullscreenViewportAndRenderTarget(out int x, out int y, out int w, out int h, out ITexture? renderTarget) {
		if (FullscreenViewportHeight == 0) {
			// this can't actually be zero. If it is, use the height of the screen instead
			x = y = 0;
			GetScreenSize(out w, out h);
			renderTarget = null;
		}
		else {
			x = FullscreenViewportX;
			y = FullscreenViewportY;
			w = FullscreenViewportWidth;
			h = FullscreenViewportHeight;
			renderTarget = FullscreenRenderTarget;
		}
	}

	public void GetFullscreenViewport(out int x, out int y, out int w, out int h) => GetFullscreenViewportAndRenderTarget(out x, out y, out w, out h, out _);

	public void PopFullscreenViewport() {
		using MatRenderContextPtr renderContext = new(materials);

		renderContext.PopRenderTargetAndViewport();

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PopMatrix();

		DisableClipping(false);
	}
	public void PrecacheFontCharacters(IFont font, ReadOnlySpan<char> characterString){
		const string pCommonChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789,.!:-/%";

		if (characterString.IsStringEmpty) 
			// use the common chars, alternate languages are not handled
			characterString = pCommonChars;

		StartDrawing();
		DrawSetTextFont(font);

		int numChars = 0;
		while (!characterString[numChars..].IsStringEmpty) 
			numChars++;

		Span<TextureID> pTextureIDs_ignored = stackalloc TextureID[numChars];
		Span<CharTexCoord> pTexCoords_ignored = stackalloc CharTexCoord[numChars];
		FontTextureCache.GetTextureForChars(CurrentFont, FontDrawType.Default, characterString[..numChars], pTextureIDs_ignored, pTexCoords_ignored);

		FinishDrawing();
	}
	public ReadOnlySpan<char> GetFontName(IFont font) => FontManager.GetFontName(font);
	public ReadOnlySpan<char> GetFontFamilyName(IFont font) => FontManager.GetFontFamilyName(font);
}
