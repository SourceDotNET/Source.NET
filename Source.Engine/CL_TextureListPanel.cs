using Source;
using Source.Bitmap;
using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.MaterialSystem;
using Source.Engine;
using Source.GUI.Controls;

using System.Text;
#if !SWDS
class TileViewPanelEx : Panel
{
	public enum HitTest_t
	{
		Nothing = 0,
		Tile
	}

	int NumTiles;
	int NumVisibleTiles;
	public int Wide;
	public int Tall;
	int WideItem;
	int TallItem;
	int ColVisible;
	int RowVisible;
	int RowNeeded;
	int StartTile;
	int EndTile;

	ScrollBar Hbar;
	IFont Font;

	protected RenderTexturesListViewPanel? Derived => this as RenderTexturesListViewPanel;

	public TileViewPanelEx(Panel parent, ReadOnlySpan<char> name) : base(parent, name) {
		Hbar = new(this, "VerticalScrollBar", true);
		Hbar.AddActionSignalTarget(this);
		Hbar.SetVisible(true);
	}

	public void SetFont(IFont font) {
		Font = font;
		Repaint();
	}

	public IFont GetFont() => Font;

	public HitTest_t HitTest(int x, int y, out int tile) {
		tile = -1;

		if (!ComputeLayoutInfo())
			return HitTest_t.Nothing;

		int hitCol = x / WideItem;
		int hitRow = y / TallItem;

		if (hitCol >= ColVisible)
			return HitTest_t.Nothing;
		if (hitRow > RowVisible)
			return HitTest_t.Nothing;

		int hitTile = StartTile + hitCol + hitRow * ColVisible;
		if (hitTile >= EndTile)
			return HitTest_t.Nothing;

		tile = hitTile;
		return HitTest_t.Tile;
	}

	public bool GetTileOrg(int tile, out int x, out int y) {
		x = 0;
		y = 0;
		if (ColVisible <= 0)
			return false;
		if (tile < StartTile || tile >= EndTile)
			return false;

		x = (tile - StartTile) % ColVisible * WideItem;
		y = (tile - StartTile) / ColVisible * TallItem;

		return true;
	}

	public override void OnMouseWheeled(int delta) {
		if (Hbar.IsVisible()) {
			int val = Hbar.GetValue();
			val -= delta;
			Hbar.SetValue(val);
		}
	}

	public override void OnSizeChanged(int newWide, int newTall) {
		base.OnSizeChanged(newWide, newTall);
		InvalidateLayout();
		Repaint();
	}

	public override void PerformLayout() {
		int numTiles = Derived != null ? Derived.GetNumTiles() : 0;
		Hbar.SetVisible(false);

		GetSize(out int wide, out int tall);
		wide -= Hbar.GetWide();

		Hbar.SetPos(wide - 2, 0);
		Hbar.SetTall(tall);

		if (numTiles == 0)
			return;

		int wideItem = 0;
		int tallItem = 0;
		Derived?.GetTileSize(out wideItem, out tallItem);
		if (wideItem <= 0 || tallItem <= 0)
			return;

		int colVisible = wide / wideItem;
		int rowvisible = tall / tallItem;
		if (colVisible <= 0 || rowvisible <= 0)
			return;

		int rowNeeded = (numTiles + colVisible - 1) / colVisible;
		// int startTile = 0;
		if (rowNeeded > rowvisible) {
			Hbar.SetRange(0, rowNeeded);
			Hbar.SetRangeWindow(rowvisible);
			Hbar.SetButtonPressedScrollValue(1);
			Hbar.SetVisible(true);
			Hbar.InvalidateLayout();

			// int val = Hbar.GetValue();
			// startTile = val * colisible;
		}
	}

	public bool ComputeLayoutInfo() {
		NumTiles = Derived != null ? Derived.GetNumTiles() : 0;
		if (NumTiles == 0)
			return false;

		GetSize(out Wide, out Tall);
		Wide -= Hbar.GetWide();

		WideItem = 1;
		TallItem = 1;
		Derived?.GetTileSize(out WideItem, out TallItem);
		if (WideItem <= 0 || TallItem <= 0)
			return false;

		ColVisible = Wide / WideItem;
		RowVisible = Tall / TallItem;
		if (RowVisible <= 0 || ColVisible <= 0)
			return false;

		RowNeeded = (NumTiles + ColVisible - 1) / ColVisible;
		NumVisibleTiles = ColVisible * RowVisible;

		StartTile = 0;
		if (RowNeeded > RowVisible) {
			int val = Hbar.GetValue();
			StartTile = val * ColVisible;
		}

		if (StartTile >= NumTiles)
			StartTile = NumTiles - NumVisibleTiles;
		if (StartTile < 0)
			StartTile = 0;

		EndTile = StartTile + NumVisibleTiles + ColVisible;
		if (EndTile > NumTiles)
			EndTile = NumTiles;

		return true;
	}

	public override void Paint() {
		base.Paint();

		GetSize(out int w, out int h);
		surface.DrawSetColor(0, 0, 0, 255);
		surface.DrawFilledRect(1, 1, w - 2, h - 2);

		if (!ComputeLayoutInfo())
			return;

		for (int renderTile = StartTile; renderTile < EndTile; ++renderTile) {
			int x = (renderTile - StartTile) % ColVisible * WideItem;
			int y = (renderTile - StartTile) / ColVisible * TallItem;
			Derived?.RenderTile(renderTile, x, y);
		}
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		SetBgColor(GetSchemeColor("ListPanel.BgColor", scheme));
		SetBorder(scheme.GetBorder("ButtonDepressedBorder"));
		SetFont(scheme.GetFont("Default", IsProportional())!);
	}
}

class AutoMatSysDebugMode
{
	bool OldDebugMode;
	List<IMaterialVar?> ArrCleanupVars;

	public AutoMatSysDebugMode() {
		ArrCleanupVars = [];

		// materialSystem.Flush();
		OldDebugMode = materialSystemDebugTextureInfo.SetDebugTextureRendering(true);
	}

	public void ScheduleCleanupTextureVar(IMaterialVar? Var) {
		if (Var == null)
			return;

		if (!ArrCleanupVars.Contains(Var))
			ArrCleanupVars.Add(Var);
	}
}

class VmtTextEntry : TextEntry
{
	public VmtTextEntry(Panel parent, ReadOnlySpan<char> name) : base(parent, name) {

	}
}

class P4Requirement
{

}

class RenderTextureEditor : Frame
{
	const int TileBorder = 10;
	const int TileSize = 550;
	const int TileTextureSize = 256;
	const int TileText = 70;

	IFont Font;
	VmtTextEntry Materials;
	Button Explore;
	Button Reload;
	Button Rebuild;
	Button ToggleNoMip;
	Button CopyTxt;
#if !POSIX
	Button CopyImg;
#endif
	Button SaveImg;
	Button[] SizeControls;
	Button FlashBtn;
	KeyValues? Info;
	StringBuilder BufInfoText;
	List<byte> LstMaterials = [];
	int InfoHint;

	public RenderTextureEditor(Panel parent, ReadOnlySpan<char> name) : base(parent, name) {
		InfoHint = 0;
		BufInfoText = new();

		Materials = new(this, "Materials");
		Materials.MakeReadyForUse();
		Materials.SetMultiline(true);
		Materials.SetEditable(false);
		Materials.SetEnabled(false);
		Materials.SetVerticalScrollbar(true);
		Materials.SetVisible(true);

		Explore = new(this, "Explore", "Open", this, "Explore");
		Explore.MakeReadyForUse();
		Explore.SetVisible(true);

		Reload = new(this, "Reload", "Reload", this, "Reload");
		Reload.MakeReadyForUse();
		Reload.SetVisible(true);

		Rebuild = new(this, "RebuildVTF", "Rebuild VTF", this, "RebuildVTF");
		Rebuild.MakeReadyForUse();
		Rebuild.SetVisible(true);

		ToggleNoMip = new(this, "ToggleNoMip", "ToggleNoMip", this, "ToggleNoMip");
		ToggleNoMip.MakeReadyForUse();
		ToggleNoMip.SetVisible(true);

		CopyTxt = new(this, "CopyTxt", "Copy Text", this, "CopyTxt");
		CopyTxt.MakeReadyForUse();
		CopyTxt.SetVisible(true);

#if !POSIX
		CopyImg = new(this, "CopyImg", "Copy Image", this, "CopyImg");
		CopyImg.MakeReadyForUse();
		CopyImg.SetVisible(true);
#endif

		SaveImg = new(this, "SaveImg", "Save Image", this, "SaveImg");
		SaveImg.MakeReadyForUse();
		SaveImg.SetVisible(true);

		FlashBtn = new(this, "FlashBtn", "Flash in Game", this, "FlashBtn");
		FlashBtn.MakeReadyForUse();
		FlashBtn.SetVisible(true);

		SizeControls = new Button[2];
		SizeControls[0] = new(this, "--", "--", this, "size-");
		SizeControls[0].MakeReadyForUse();
		SizeControls[1] = new(this, "+", "+", this, "size+");
		SizeControls[1].MakeReadyForUse();
	}

	// FIXME #37
	public override void Dispose() {
		base.Dispose();
		SetDispInfo(null, 0);
	}

	public void GetDispInfo(out KeyValues? kv, out int hint) {
		kv = Info;
		hint = InfoHint;
	}

	public void SetDispInfo(KeyValues? kv, int hint) {
		InfoHint = hint;
		Info = kv?.MakeCopy();

		HashSet<string> arrMaterials = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> arrMaterialsFullNames = new(StringComparer.OrdinalIgnoreCase);

		// if (kv != null) {
		// 	ReadOnlySpan<char> textureName = kv.GetString(TextureListPanel.KeyName_Name);

		// 	for (short hm = 0; hm != -1; ++hm) {
		// 		IMaterial? mat = materials.GetMaterial(hm); // todo!!! this is all we need!
		// 		if (mat == null) continue;

		// 		IMaterialVar[] arrVars = mat.GetShaderParams();
		// 		int numParams = mat.ShaderParamCount();

		// 		for (int idxParam = 0; idxParam < numParams; ++idxParam) {
		// 			if (!arrVars[idxParam].IsTexture()) continue;

		// 			ITexture? tex = arrVars[idxParam].GetTextureValue();
		// 			if (tex == null || tex.IsError()) continue;

		// 			ReadOnlySpan<char> texName = tex.GetName();
		// 			if (!texName.Equals(textureName, StringComparison.OrdinalIgnoreCase)) continue;

		// 			bool realMaterial = true;
		// 			ReadOnlySpan<char> matNameSpan = mat.GetName();

		// 			if (matNameSpan.StartsWith("debug/debugtexture"))
		// 				realMaterial = false;

		// 			if (matNameSpan.StartsWith("maps/")) {
		// 				realMaterial = false;

		// 				ReadOnlySpan<char> chName = matNameSpan[5..];
		// 				int slashIndex = chName.IndexOf('/');
		// 				if (slashIndex != -1) {
		// 					Span<char> matName = chName[(slashIndex + 1)..].ToArray();

		// 					for (int k = 0; k < 3; ++k) {
		// 						int underscore = matName.LastIndexOf('_');
		// 						if (underscore != -1)
		// 							matName = matName[..underscore];
		// 					}

		// 					string cubemapName = string.Concat(matName.ToString(), " (from map)");
		// 					arrMaterials.Add(cubemapName);
		// 				}

		// 				arrMaterialsFullNames.Add(matNameSpan.ToString());
		// 			}

		// 			if (realMaterial) {
		// 				string nameStr = matNameSpan.ToString();
		// 				arrMaterials.Add(nameStr);
		// 				arrMaterialsFullNames.Add(nameStr);
		// 			}

		// 			break;
		// 		}
		// 	}
		// }

		StringBuilder bufText = new();
		if (arrMaterials.Count == 0)
			bufText.Append("-- no materials --");
		else {
			int c = arrMaterials.Count;
			bufText.AppendFormat("  {0} material{1}:", c, (c % 10 == 1 && c != 11) ? "" : "s");
		}

		foreach (string s in arrMaterials)
			bufText.AppendLine().Append(s);

		SaveImg.SetVisible(false);

		if (!(Info != null && arrMaterials.Count == 0)) {
			if (kv != null) {
				int txWidth = kv.GetInt(TextureListPanel.KeyName_Width);
				int txHeight = kv.GetInt(TextureListPanel.KeyName_Height);
				ReadOnlySpan<char> txFormatSpan = kv.GetString(TextureListPanel.KeyName_Format);
				string txFormat = txFormatSpan.ToString();

				bufText.AppendFormat("\n{0}x{1} Format:{2}", txWidth, txHeight, txFormat);

				if (txFormat.Contains("8888"))
					SaveImg.SetVisible(true);
			}

			Materials.SetText(bufText.ToString());

			LstMaterials.Clear();
			LstMaterials.EnsureCapacity(arrMaterialsFullNames.Count);
			foreach (string s in arrMaterialsFullNames)
				LstMaterials.AddRange(Encoding.UTF8.GetBytes(s));
		}

		BufInfoText.Clear();
		InvalidateLayout();
	}

	public override void Close() {
		base.Close();
		SetDispInfo(null, 0);
	}

	private static bool CanAdjustTextureSize(ReadOnlySpan<char> textureName, bool moveSizeUp) {
		ITexture? tex = materials.FindTexture(textureName, "", false);
		if (tex == null)
			return false;

		if (!moveSizeUp)
			return tex.GetActualWidth() > 4 || tex.GetActualHeight() > 4;
		else
			return tex.GetActualWidth() < tex.GetMappingWidth() || tex.GetActualHeight() < tex.GetMappingHeight();
	}

	public override void PerformLayout() {
		base.PerformLayout();

		int renderedHeight = 4 * TileBorder + TileText + TileSize;

		SetSize(4 * TileBorder + TileSize, renderedHeight + 90 + TileBorder);

		Materials.SetPos(TileBorder, renderedHeight + 2);
		Materials.SetSize(2 * TileBorder + TileSize, 90);

		Explore.SetPos(2 * TileBorder + TileSize - 50, 2 * TileBorder);
		Explore.SetWide(50);

		Reload.SetPos(2 * TileBorder + TileSize - 50 - 65, 2 * TileBorder);
		Reload.SetWide(60);
		Reload.SetVisible(LstMaterials.Count > 0);

		Rebuild.SetPos(2 * TileBorder + TileSize - 50 - 65 - 95, 2 * TileBorder);
		Rebuild.SetWide(90);
		Rebuild.SetVisible(LstMaterials.Count > 0);

		ToggleNoMip.SetPos(2 * TileBorder + TileSize - 50 - 95, (2 * TileBorder) + Reload.GetTall() + 1);
		ToggleNoMip.SetWide(90);
		ToggleNoMip.SetVisible(LstMaterials.Count > 0);

		Explore.SetVisible(false);
		SizeControls[0].SetVisible(false);
		SizeControls[1].SetVisible(false);

		if (Info != null) {
			Span<char> resolveName = stackalloc char[256];
			Span<char> resolveNameArg = stackalloc char[256];
			sprintf(resolveNameArg, "materials/%s.vtf").S(Info.GetString(TextureListPanel.KeyName_Name));
			ReadOnlySpan<char> resolvedName = g_pFileSystem.RelativePathToFullPath(resolveNameArg, "game", resolveName);
			if (!resolvedName.IsEmpty)
				Explore.SetVisible(true);

			if (Info.GetInt("SpecialTx") == 0) {
				SizeControls[0].SetVisible(true);
				SizeControls[1].SetVisible(true);

				SizeControls[0].SetEnabled(CanAdjustTextureSize(Info.GetString(TextureListPanel.KeyName_Name), false));
				SizeControls[1].SetEnabled(CanAdjustTextureSize(Info.GetString(TextureListPanel.KeyName_Name), true));

				Explore.GetPos(out int posx, out int posy);
				SizeControls[0].SetPos(posx, posy + Explore.GetTall() + 1);
				SizeControls[0].SetWide(Explore.GetWide() / 2);
				SizeControls[1].SetPos(posx + SizeControls[0].GetWide() + 1, posy + Explore.GetTall() + 1);
				SizeControls[1].SetWide(Explore.GetWide() - (SizeControls[0].GetWide() + 1));
			}
		}

		Explore.GetPos(out int posX, out int posY);
		posY += Explore.GetTall() * 2 + 2;
		posX += Explore.GetWide();

		posX -= 80;
		SaveImg.SetPos(posX, posY);
		SaveImg.SetWide(80);

#if !POSIX
		posX -= 80 + 5;
		CopyImg.SetPos(posX, posY);
		CopyImg.SetWide(80);
#endif

		posX -= 80 + 5;
		CopyTxt.SetPos(posX, posY);
		CopyTxt.SetWide(80);

		posX -= 95 + 5;
		FlashBtn.SetPos(posX, posY);
		FlashBtn.SetWide(95);
	}

	IFont GetFont() => Font;
	void SetFont(IFont font) {
		Font = font;
		Repaint();
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetFont(scheme.GetFont("Default", IsProportional())!);
	}

	public override void OnMousePressed(ButtonCode code) => Close();

	public override void Paint() {
		AutoMatSysDebugMode auto_matsysdebugmode = new();

		DisableFadeEffects();
		SetBgColor(new(10, 50, 10, 255));
		base.Paint();

		KeyValues? kv = Info;
		if (kv == null)
			return;

		ReadOnlySpan<char> textureFile = kv.GetString(TextureListPanel.KeyName_Name);
		// LastViewedTextureBuffer todo
		ReadOnlySpan<char> textureGroup = kv.GetString(TextureListPanel.KeyName_Texture_Group);

		ITexture? matTexture = null;
		if (!textureFile.IsEmpty)
			matTexture = materials.FindTexture(textureFile, textureGroup, false);
		matTexture ??= materials.FindTexture("debugempty", "", false);

		int txWidth = kv.GetInt(TextureListPanel.KeyName_Width);
		int txHeight = kv.GetInt(TextureListPanel.KeyName_Height);
		int txSize = kv.GetInt(TextureListPanel.KeyName_Size);
		ReadOnlySpan<char> txFormat = kv.GetString(TextureListPanel.KeyName_Format);

		int x = TileBorder;
		int y = TileBorder;
		int drawWidth = txWidth;
		int drawHeight = txHeight;

		if (matTexture != null && matTexture.IsCubeMap()) {
			drawWidth = 1024;
			drawHeight = 1024;
		}

		if (drawHeight >= drawWidth) {
			if (drawHeight > TileTextureSize) {
				drawWidth *= TileTextureSize / drawHeight;
				drawHeight = TileTextureSize;
			}

			if (drawHeight < 64) {
				drawWidth *= 64 / drawHeight;
				drawHeight = 64;
			}
		}
		else {
			if (drawWidth > TileTextureSize) {
				drawHeight *= TileTextureSize / drawWidth;
				drawWidth = TileTextureSize;
			}

			if (drawWidth < 64) {
				drawHeight *= 64 / drawWidth;
				drawWidth = 64;
			}
		}

		drawHeight = (int)(drawHeight * ((float)TileSize / TileTextureSize));
		drawWidth = (int)(drawWidth * ((float)TileSize / TileTextureSize));
		drawHeight = Math.Max(drawHeight, 4);
		drawWidth = Math.Max(drawWidth, 4);

		int tileWidth = 2 * TileBorder + TileSize;
		int tileHeight = 3 * TileBorder + TileSize + TileText;

		Surface.DrawSetColor(255, 255, 255, 255);
		Surface.DrawOutlinedRect(x + 1, y + 1, x + tileWidth - 2, y + tileHeight - 2);

		y += TileBorder;
		x += TileBorder;

		Span<char> resolveName = stackalloc char[256];
		Span<char> resolveNameArg = stackalloc char[256];
		sprintf(resolveNameArg, "materials/%s.vtf").S(textureFile);
		ReadOnlySpan<char> resolvedName = g_pFileSystem.RelativePathToFullPath(resolveNameArg, "game", resolveName);

		ReadOnlySpan<char> printFilePrefix = !resolvedName.IsEmpty ? "" : "[?]/";
		ReadOnlySpan<char> printFileName = !resolvedName.IsEmpty ? resolvedName : textureFile;

		Span<char> sizeBuf = stackalloc char[20];
		if (txSize >= 0)
			RenderTexturesListViewPanel.FmtCommaNumber(sizeBuf, (uint)txSize);
		else
			sizeBuf[0] = '-';

		Span<char> fullText = stackalloc char[512];
		int written = sprintf(fullText, "%s%s\n%s Kb    %dx%d    %s")
			.S(printFilePrefix)
			.S(printFileName)
			.S(sizeBuf)
			.I(txWidth)
			.I(txHeight)
			.S(txFormat);
		Surface.DrawColoredTextRect(GetFont(), x, y, TileSize, TileText / 2, 255, 255, 255, 255, fullText);

		if (BufInfoText.Length == 0) {
			BufInfoText.AppendFormat(
				"{0}{1}\r\n{2} Kb    {3}x{4}    {5}",
				printFilePrefix.ToString(),
				printFileName.ToString(),
				sizeBuf.ToString(),
				txWidth,
				txHeight,
				txFormat.ToString());
		}

		if (kv.GetInt("SpecialTx") == 0) {
			Span<char> line1 = stackalloc char[256];
			Span<char> line2 = stackalloc char[256];

			// Line 1
			if (txSize > TextureListPanel.WarnTxListSize)
				sprintf(line1, "  Size(%s Kb)").S(sizeBuf);
			if (txWidth > TextureListPanel.WarnTextDimensions || txHeight > TextureListPanel.WarnTextDimensions)
				sprintf(line1, "%s  Dimensions(%dx%d)").S(line1).I(txWidth).I(txHeight);
			if (!txFormat.Equals("DXT1", StringComparison.OrdinalIgnoreCase) &&
				!txFormat.Equals("DXT5", StringComparison.OrdinalIgnoreCase))
				sprintf(line1, "%s  Format(%s)").S(line1).S(txFormat);
			if (((TextureFlags)matTexture!.GetFlags() & TextureFlags.NoLOD) != 0)
				sprintf(line1, "%s  NoLod").S(line1);
			if (((TextureFlags)matTexture.GetFlags() & TextureFlags.NoMip) != 0)
				sprintf(line1, "%s  NoMip").S(line1);
			if (((TextureFlags)matTexture.GetFlags() & TextureFlags.OneBitAlpha) != 0)
				sprintf(line1, "%s  OneBitAlpha").S(line1);

			// Line 2
			int wmap = matTexture.GetMappingWidth(), hmap = matTexture.GetMappingHeight(), dmap = matTexture.GetMappingDepth();
			int wact = matTexture.GetActualWidth(), hact = matTexture.GetActualHeight(), dact = matTexture.GetActualDepth();
			ImageFormat fmt = matTexture.GetImageFormat();

			if (wact > 4 || hact > 4) {
				Span<char> buf = stackalloc char[50];
				int mem = ImageLoader.GetMemRequired(Math.Max(Math.Min(wact, 4), wact / 2), Math.Max(Math.Min(hact, 4), hact / 2), Math.Max(1, dact / 2), fmt, true);
				mem = (mem + 511) / 1024;
				RenderTexturesListViewPanel.FmtCommaNumber(buf, (uint)mem);
				sprintf(line2, "  %s Kb @ lower mip").S(buf);
			}

			if (wmap > wact || hmap > hact || dmap > dact) {
				Span<char> buf = stackalloc char[50];
				int mem = ImageLoader.GetMemRequired(Math.Min(wmap, wact * 2), Math.Min(hmap, hact * 2), Math.Min(dmap, dact * 2), fmt, true);
				mem = (mem + 511) / 1024;
				RenderTexturesListViewPanel.FmtCommaNumber(buf, (uint)mem);
				sprintf(line2, "%s      %s Kb @ higher mip").S(line2).S(buf);
			}

			if (!line1.IsEmpty) {
				Surface.DrawSetColor(200, 0, 0, 255);
				Surface.DrawFilledRect(x - TileBorder / 2, y + TileText / 2, (int)(x + TileBorder / 2 + (TileSize * 0.5)), y + TileText / 2 + TileText / 4);
				Surface.DrawColoredTextRect(GetFont(), x, y + TileText / 2, TileSize, TileText / 4, 255, 255, 255, 255, line1.SliceNullTerminatedString());
			}

			if (!line2.IsEmpty)
				Surface.DrawColoredTextRect(GetFont(), x, y + TileText / 2 + TileText / 4, TileSize, TileText / 4, 255, 255, 255, 255, line2.SliceNullTerminatedString());
		}

		y += TileText;

		bool hasAlpha = !txFormat.Equals("DXT1", StringComparison.OrdinalIgnoreCase);

		int extTxWidth = TileSize;
		int extTxHeight = TileSize;
		int orgTxX = 0, orgTxXA = 0;
		int orgTxY = 0, orgTxYA = 0;

		if (hasAlpha) {
			if (txWidth >= txHeight * 2) {
				extTxHeight /= 2;
				orgTxYA = extTxHeight + TileBorder / 2;
				extTxHeight -= 1;
			}
			else if (txHeight >= txWidth * 2) {
				extTxWidth /= 2;
				orgTxXA = extTxWidth + TileBorder / 2;
				extTxWidth -= 3;
			}
			else {
				extTxHeight /= 2;
				orgTxYA = extTxHeight + TileBorder / 2;
				orgTxX = extTxWidth / 4;
				extTxWidth /= 2;

				if (drawWidth > extTxWidth) {
					drawWidth /= 2;
					drawHeight /= 2;
				}
				extTxWidth -= 1;
				extTxHeight -= 1;
			}
		}

		const int imgFrameOff = 2;
		IMaterial? mat = RenderTexturesListViewPanel.UseDebugMaterial("debug/debugtexturecolor", matTexture, auto_matsysdebugmode);
		if (mat != null) {
			Surface.DrawSetColor(255, 255, 255, 255);
			Surface.DrawOutlinedRect(x + orgTxX + (extTxWidth - drawWidth) / 2 - imgFrameOff, y + orgTxY + (extTxHeight - drawHeight) / 2 - imgFrameOff,
				x + orgTxX + (extTxWidth + drawWidth) / 2 + imgFrameOff, y + orgTxY + (extTxHeight + drawHeight) / 2 + imgFrameOff);
			RenderTexturesListViewPanel.RenderTexturedRect(this, mat,
				x + orgTxX + (extTxWidth - drawWidth) / 2, y + orgTxY + (extTxHeight - drawHeight) / 2,
				x + orgTxX + (extTxWidth + drawWidth) / 2, y + orgTxY + (extTxHeight + drawHeight) / 2);

			if (hasAlpha) {
				orgTxX += orgTxXA;
				orgTxY += orgTxYA;
				IMaterial? matDebug = RenderTexturesListViewPanel.UseDebugMaterial("debug/debugtexturealpha", matTexture, auto_matsysdebugmode);
				if (matDebug != null) {
					Surface.DrawOutlinedRect(x + orgTxX + (extTxWidth - drawWidth) / 2 - imgFrameOff, y + orgTxY + (extTxHeight - drawHeight) / 2 - imgFrameOff,
						x + orgTxX + (extTxWidth + drawWidth) / 2 + imgFrameOff, y + orgTxY + (extTxHeight + drawHeight) / 2 + imgFrameOff);
					RenderTexturesListViewPanel.RenderTexturedRect(this, matDebug,
						x + orgTxX + (extTxWidth - drawWidth) / 2, y + orgTxY + (extTxHeight - drawHeight) / 2,
						x + orgTxX + (extTxWidth + drawWidth) / 2, y + orgTxY + (extTxHeight + drawHeight) / 2);
				}
			}
		}
		else {
			Surface.DrawSetColor(255, 0, 255, 100);
			Surface.DrawFilledRect(
				x + orgTxX + (extTxWidth - drawWidth) / 2, y + orgTxY + (extTxWidth - drawHeight) / 2,
				x + orgTxX + (extTxWidth + drawWidth) / 2, y + orgTxY + (extTxWidth + drawHeight) / 2);
		}
	}
}

class RenderTexturesListViewPanel : TileViewPanelEx
{
	public const int TileBorder = 20;
	public const int TileSize = 192;
	public const int TileTextureSize = 256;
	public const int TileText = 35;

	public ListPanel? ListPanel;
	RenderTextureEditor RenderTxEditor;
	bool PaintAlpha;
	public RenderTexturesListViewPanel(Panel parent, ReadOnlySpan<char> name) : base(parent, name) {
		ListPanel = null;
		PaintAlpha = false;

		RenderTxEditor = new(this, "TxEdit");
		RenderTxEditor.SetPos(10, 10);
		RenderTxEditor.PerformLayout();
		RenderTxEditor.SetMoveable(true);
		RenderTxEditor.SetSizeable(false);
		RenderTxEditor.SetClipToParent(true);
		RenderTxEditor.SetTitle("", true);
		RenderTxEditor.SetCloseButtonVisible(false);
		RenderTxEditor.SetVisible(false);
	}

	public override void OnMousePressed(ButtonCode code) {
		base.OnMousePressed(code);

		RenderTxEditor.Close();

		if (ListPanel == null)
			return;

		Input.GetCursorPos(out int x, out int y);
		ScreenToLocal(ref x, ref y);

		HitTest_t htResult = HitTest(x, y, out int tile);

		if (htResult == HitTest_t.Nothing)
			return;

		if (!GetTileOrg(tile, out int tileX, out int tileY))
			return;

		int itemId = ListPanel.GetItemIDFromRow(tile);
		if (itemId < 0)
			return;

		KeyValues? kv = ListPanel.GetItem(itemId);
		if (kv == null)
			return;

		RenderTxEditor.SetDispInfo(kv, itemId);

		if (tileX + RenderTxEditor.GetWide() > Wide - 2)
			tileX -= tileX + RenderTxEditor.GetWide() - (Wide - 2);
		if (tileY + RenderTxEditor.GetTall() > Tall - 2)
			tileY -= tileY + RenderTxEditor.GetTall() - (Tall - 2);

		int topLeftX = 0;
		int topLeftY = 0;
		for (Panel? p = this; p != null; p = p.GetParent())
			p.LocalToScreen(ref tileX, ref tileY);

		LocalToScreen(ref tileX, ref tileY);
		if (tileX < topLeftX) tileX = topLeftX;
		if (tileY < topLeftY) tileY = topLeftY;

		RenderTxEditor.SetPos(tileX, tileY);
		RenderTxEditor.Activate();
	}

	public int GetNumTiles() => ListPanel != null ? ListPanel.GetItemCount() : 0;

	public void GetTileSize(out int wide, out int tall) {
		wide = 2 * TileBorder + TileSize;
		tall = 2 * TileBorder + TileSize + TileText;
	}

	public KeyValues? GetTileData(int tile) {
		int data = ListPanel!.GetItemIDFromRow(tile);
		if (data < 0)
			return null;
		return ListPanel.GetItem(data);
	}

	public void RenderTile(int tile, int x, int y) {
		AutoMatSysDebugMode auto_matsysdebugmode = new();

		KeyValues? kv = GetTileData(tile);
		if (kv == null) return;

		ReadOnlySpan<char> TextureFile = kv.GetString(TextureListPanel.KeyName_Name);
		ReadOnlySpan<char> TextureGroup = kv.GetString(TextureListPanel.KeyName_Texture_Group);

		ITexture? MatTexture = null;
		if (!TextureFile.IsEmpty)
			MatTexture = materials.FindTexture(TextureFile, TextureGroup, false);
		MatTexture ??= materials.FindTexture("debugempty", "", false);

		int TxWidth = kv.GetInt(TextureListPanel.KeyName_Width);
		int TxHeight = kv.GetInt(TextureListPanel.KeyName_Height);
		int TxSize = kv.GetInt(TextureListPanel.KeyName_Size);
		ReadOnlySpan<char> TxFormat = kv.GetString(TextureListPanel.KeyName_Format);

		int TxFormatLen = TxFormat.Length;
		ReadOnlySpan<char> TxFormatSuffix = "";
		if (TxFormatLen > 4) {
		fmtlenreduce:
			switch (TxFormat[TxFormatLen - 1]) {
				case '8':
					while (TxFormatLen > 4 && TxFormat[TxFormatLen - 2] == '8') TxFormatLen--;
					break;
				case '6':
					while (TxFormatLen > 4 && TxFormat[TxFormatLen - 2] == '1' && TxFormat[TxFormatLen - 3] == '6') TxFormatLen -= 2;
					break;
				case 'F':
					if (TxFormatSuffix.IsEmpty) {
						TxFormatLen--;
						TxFormatSuffix = "F";
						goto fmtlenreduce;
					}
					break;
			}
		}

		int DrawWidth = TxWidth;
		int DrawHeight = TxHeight;

		if (MatTexture != null && MatTexture.IsCubeMap()) {
			DrawWidth = 1024;
			DrawHeight = 1024;
		}

		if (DrawHeight >= DrawWidth) {
			if (DrawHeight > TileTextureSize) {
				DrawWidth = (int)(DrawWidth * (float)TileTextureSize / DrawHeight);
				DrawHeight = TileTextureSize;
			}
			if (DrawHeight < 64) {
				DrawWidth = (int)(DrawWidth * (float)64 / DrawHeight);
				DrawHeight = 64;
			}
		}
		else {
			if (DrawWidth > TileTextureSize) {
				DrawHeight = (int)(DrawHeight * (float)TileTextureSize / DrawWidth);
				DrawWidth = TileTextureSize;
			}
			if (DrawWidth < 64) {
				DrawHeight = (int)(DrawHeight * (float)64 / DrawWidth);
				DrawWidth = 64;
			}
		}

		DrawHeight = (int)(DrawHeight / ((float)TileTextureSize / TileSize));
		DrawWidth = (int)(DrawWidth / ((float)TileTextureSize / TileSize));
		DrawHeight = Math.Max(DrawHeight, 4);
		DrawWidth = Math.Max(DrawWidth, 4);

		GetTileSize(out int tileWidth, out int tileHeight);
		Surface.DrawSetColor(255, 255, 255, 255);
		Surface.DrawOutlinedRect(x + 1, y + 1, x + tileWidth - 2, y + tileHeight - 2);

		x += TileBorder;
		y += TileBorder / 2;

		int iLenFile = TextureFile.Length;
		ReadOnlySpan<char> PrintFilePrefix = (iLenFile > 22) ? "..." : "";
		ReadOnlySpan<char> PrintFileName = iLenFile > 22 ? TextureFile[(iLenFile - 22)..] : TextureFile;

		Span<char> sizeBuf = stackalloc char[20];
		if (TxSize >= 0)
			FmtCommaNumber(sizeBuf, (uint)TxSize);
		else
			sizeBuf[0] = '-';

		Color clrLblNormal = new(25, 50, 25, 255);
		Color clrLblWarn = new(75, 75, 0, 255);
		Color clrLblError = new(200, 0, 0, 255);

		bool warnTile = (kv.GetInt("SpecialTx") == 0) && TextureListPanel.WarnEnable && ShallWarnTx(kv, MatTexture);

		Surface.DrawSetColor(warnTile ? clrLblWarn : clrLblNormal);
		Surface.DrawFilledRect(x - TileBorder / 2, y, x + TileBorder / 2 + TileSize, y + TileText);

		Span<char> infoText = stackalloc char[256];
		sprintf(infoText, "%s Kb  %dx%d  %s%s  %s") // fixme, probably wrong
			.S(sizeBuf)
			.D(TxWidth)
			.D(TxHeight)
			// .D(TxFormatLen)
			.S(TxFormat)
			.S(TxFormatSuffix)
			.S(((TextureFlags)MatTexture!.GetFlags() & (TextureFlags.NoLOD | TextureFlags.NoMip | TextureFlags.OneBitAlpha)) != 0 ? "***" : "");

		int[] textMargins = new int[4];
		int textHeight = Surface.GetFontTall(GetFont());
		int[] textLen = new int[4];
		textLen[0] = sizeBuf.Length + 5;
		textLen[1] = infoText.IndexOf('x') + 1;
		while (infoText[textLen[1]] != ' ')
			textLen[1]++;
		++textLen[1];
		textLen[2] = 2 + textLen[1] + TxFormatLen + TxFormatSuffix.Length;
		textLen[3] = infoText.IndexOf("***") + 3; // fixme this is wrong

		for (int k = 0; k < 4; ++k)
			textMargins[k] = ((IMatSystemSurface)Surface).DrawTextLen(GetFont(), infoText[..textLen[k]]);

		if (warnTile) {
			Surface.DrawSetColor(clrLblError);
			if (TxSize > TextureListPanel.WarnTxListSize)
				Surface.DrawFilledRect(x - 2, y + textHeight + 1, x + textMargins[0] - 5, y + TileText);
			if (TxWidth > TextureListPanel.WarnTextDimensions || TxHeight > TextureListPanel.WarnTextDimensions)
				Surface.DrawFilledRect(x + textMargins[0] - 2, y + textHeight + 1, x + textMargins[1] - 1, y + TileText);
			if (!TxFormat.Equals("DXT1", StringComparison.OrdinalIgnoreCase) && !TxFormat.Equals("DXT5", StringComparison.OrdinalIgnoreCase))
				Surface.DrawFilledRect(x + textMargins[1] + 2, y + textHeight + 1, x + textMargins[2] - 1, y + TileText);
			if (((TextureFlags)MatTexture.GetFlags() & (TextureFlags.NoLOD | TextureFlags.NoMip | TextureFlags.OneBitAlpha)) != 0)
				Surface.DrawFilledRect(x + textMargins[2] + 3, y + textHeight + 1, x + textMargins[3] + 2, y + TileText);
		}

		Span<char> fullText = stackalloc char[256];
		sprintf(fullText, "%s%s\n%s")
			.S(PrintFilePrefix)
			.S(PrintFileName)
			.S(infoText);
		Surface.DrawColoredTextRect(GetFont(), x, y, TileSize, TileText, 255, 255, 255, 255, fullText);

		y += TileText + TileBorder / 2;

		bool bHasAlpha = PaintAlpha && !TxFormat.Equals("DXT1", StringComparison.OrdinalIgnoreCase);

		int extTxWidth = TileSize;
		int extTxHeight = TileSize;

		int orgTxX = 0, orgTxXA = 0;
		int orgTxY = 0, orgTxYA = 0;

		if (bHasAlpha) {
			if (TxWidth >= TxHeight * 2) {
				extTxHeight /= 2;
				orgTxYA = extTxHeight + TileBorder / 2;
			}
			else if (TxHeight >= TxWidth * 2) {
				extTxWidth /= 2;
				orgTxXA = extTxWidth + TileBorder / 2;
				x -= TileBorder / 4 + 1;
			}
			else {
				extTxHeight /= 2;
				orgTxYA = extTxHeight + TileBorder / 2;
				orgTxX = extTxWidth / 4;
				extTxWidth /= 2;
				x -= TileBorder / 4 + 1;

				if (DrawWidth > extTxWidth) {
					DrawWidth /= 2;
					DrawHeight /= 2;
				}
			}
		}

		const int ImgFrameOff = 2;
		IMaterial? Material = UseDebugMaterial("debug/debugtexturecolor", MatTexture, auto_matsysdebugmode);
		if (Material != null) {
			Surface.DrawSetColor(255, 255, 255, 255);
			Surface.DrawOutlinedRect(
				x + orgTxX + (extTxWidth - DrawWidth) / 2 - ImgFrameOff,
				y + orgTxY + (extTxHeight - DrawHeight) / 2 - ImgFrameOff,
				x + orgTxX + (extTxWidth + DrawWidth) / 2 + ImgFrameOff,
				y + orgTxY + (extTxHeight + DrawHeight) / 2 + ImgFrameOff
			);
			RenderTexturedRect(this, Material,
				x + orgTxX + (extTxWidth - DrawWidth) / 2, y + orgTxY + (extTxHeight - DrawHeight) / 2,
				x + orgTxX + (extTxWidth + DrawWidth) / 2, y + orgTxY + (extTxHeight + DrawHeight) / 2,
				2, 1
			);

			if (bHasAlpha) {
				orgTxX += orgTxXA;
				orgTxY += orgTxYA;
				IMaterial? MaterialDebug = UseDebugMaterial("debug/debugtexturealpha", MatTexture, auto_matsysdebugmode);
				if (MaterialDebug != null) {
					Surface.DrawOutlinedRect(
						x + orgTxX + (extTxWidth - DrawWidth) / 2 - ImgFrameOff,
						y + orgTxY + (extTxHeight - DrawHeight) / 2 - ImgFrameOff,
						x + orgTxX + (extTxWidth + DrawWidth) / 2 + ImgFrameOff,
						y + orgTxY + (extTxHeight + DrawHeight) / 2 + ImgFrameOff
					);
					RenderTexturedRect(this, MaterialDebug,
						x + orgTxX + (extTxWidth - DrawWidth) / 2, y + orgTxY + (extTxHeight - DrawHeight) / 2,
						x + orgTxX + (extTxWidth + DrawWidth) / 2, y + orgTxY + (extTxHeight + DrawHeight) / 2,
						2, 1
					);
				}
			}
		}
		else {
			Surface.DrawSetColor(255, 0, 255, 100);
			Surface.DrawFilledRect(
				x + orgTxX + (extTxWidth - DrawWidth) / 2, y + orgTxY + (extTxWidth - DrawHeight) / 2,
				x + orgTxX + (extTxWidth + DrawWidth) / 2, y + orgTxY + (extTxWidth + DrawHeight) / 2
			);
		}

		y += TileSize + TileBorder;
	}

	static bool ShallWarnTx(KeyValues kv, ITexture? tx) {
		if (tx == null)
			return false;

		if (((TextureFlags)tx.GetFlags() & (TextureFlags.NoLOD | TextureFlags.NoMip | TextureFlags.OneBitAlpha)) != 0)
			return true;

		ReadOnlySpan<char> fmt = kv.GetString(TextureListPanel.KeyName_Format);
		if (!fmt.Equals("DXT1", StringComparison.OrdinalIgnoreCase) &&
				!fmt.Equals("DXT5", StringComparison.OrdinalIgnoreCase) &&
				!fmt.Equals("ATI1N", StringComparison.OrdinalIgnoreCase) &&
				!fmt.Equals("ATI2N", StringComparison.OrdinalIgnoreCase) &&
				!fmt.Equals("DXT5_RUNTIME", StringComparison.OrdinalIgnoreCase))
			return true;

		if (kv.GetInt(TextureListPanel.KeyName_Size) > TextureListPanel.WarnTxListSize)
			return true;

		if (kv.GetInt(TextureListPanel.KeyName_Width) > TextureListPanel.WarnTextDimensions)
			return true;

		if (kv.GetInt(TextureListPanel.KeyName_Height) > TextureListPanel.WarnTextDimensions)
			return true;

		return false;
	}

	public static void FmtCommaNumber(Span<char> buffer, uint number) {
		buffer.Clear();
		int offset = 0;
		for (uint divisor = 1_000_000_000; divisor > 0; divisor /= 1000) {
			if (number >= divisor) {
				uint print = number / divisor % 1000;
				int written;
				if (number / divisor < 1000)
					print.TryFormat(buffer[offset..], out written);
				else
					print.TryFormat(buffer[offset..], out written, "D3");
				offset += written;
				buffer[offset++] = ',';
			}
		}

		if (offset == 0)
			"0".AsSpan().CopyTo(buffer);
		else if (buffer[offset - 1] == ',')
			buffer[offset - 1] = '\0';
	}


	public static void RenderTexturedRect(Panel pPanel, IMaterial Material, int x, int y, int x1, int y1, int xoff = 0, int yoff = 0) {
		int tall = pPanel.GetTall();
		float fHeightUV = 1.0f;
		if (y1 > tall) {
			fHeightUV = (tall - y) / (y1 - y);
			y1 = tall;
		}
		if (y1 <= y)
			return;

		pPanel.LocalToScreen(ref x, ref y);
		pPanel.LocalToScreen(ref x1, ref y1);

		x += xoff; x1 += xoff; y += yoff; y1 += yoff;

		using MatRenderContextPtr pRenderContext = new(materials);
		pRenderContext.Bind(Material);
		IMesh Mesh = pRenderContext.GetDynamicMesh(true);

		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(Mesh, MaterialPrimitiveType.Quads, 1);

		meshBuilder.Position3f(x, y, 0.0f);
		meshBuilder.TexCoord2f(0, 0.0f, 0.0f);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3f(x1, y, 0.0f);
		meshBuilder.TexCoord2f(0, 1.0f, 0.0f);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3f(x1, y1, 0.0f);
		meshBuilder.TexCoord2f(0, 1.0f, fHeightUV);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3f(x, y1, 0.0f);
		meshBuilder.TexCoord2f(0, 0.0f, fHeightUV);
		meshBuilder.AdvanceVertex();

		meshBuilder.End();
		Mesh.Draw();
	}


	public static IMaterial? UseDebugMaterial(ReadOnlySpan<char> material, ITexture? matTexture, AutoMatSysDebugMode restoreVars) {
		if (material.IsEmpty || matTexture == null)
			return null;

		IMaterial Material = materials.FindMaterial(material, "Other textures", false);//TEXTURE_GROUP_OTHER
		if (Material == null)
			return null;

		IMaterialVar BaseTextureVar = Material.FindVar("$basetexture", out bool foundVar, false);
		if (!foundVar || BaseTextureVar == null)
			return null;

		IMaterialVar FrameVar = Material.FindVar("$frame", out foundVar, false);
		if (foundVar && FrameVar != null) {
			int numAnimFrames = matTexture.GetNumAnimationFrames();

			if (matTexture.IsRenderTarget() || matTexture.IsProcedural())
				numAnimFrames = 0;

			FrameVar.SetIntValue((int)((numAnimFrames > 0) ? (clientGlobalVariables.TickCount % numAnimFrames) : 0));
		}

		BaseTextureVar.SetTextureValue(matTexture);

		restoreVars?.ScheduleCleanupTextureVar(BaseTextureVar);

		return Material;
	}

	public void SetDataListPanel(ListPanel panel) {
		ListPanel = panel;
		InvalidateLayout();
	}

	public void SetPaintAlpha(bool alpha) {
		PaintAlpha = alpha;
		Repaint();
	}

	public RenderTextureEditor GetRenderTxEditor() => RenderTxEditor;
}

class TextureListPanel : Frame
{
	public static TextureListPanel? g_TextureListPanel;
	static int SaveQueueState = int.MinValue;
	public static int WarnTxListSize = 1499;
	public static int WarnTextDimensions = 1024;
	public static bool WarnEnable = true;
	static bool CursorSet = false;

	static ConVar mat_texture_list = new("mat_texture_list", "0", FCvar.Cheat, "For debugging, show a list of used textures per frame");
	static ConVar mat_texture_list_all = new("mat_texture_list_all", "0", FCvar.NeverAsString | FCvar.Cheat, "If this is nonzero, then the texture list panel will show all currently-loaded textures.");
	static ConVar mat_texture_list_view = new("mat_texture_list_view", "1", FCvar.NeverAsString | FCvar.Cheat, "If this is nonzero, then the texture list panel will render thumbnails of currently-loaded textures.");
	static ConVar mat_show_texture_memory_usage = new("mat_show_texture_memory_usage", "0", FCvar.NeverAsString | FCvar.Cheat, "Display the texture memory usage on the HUD.");

	public const string KeyName_Name = "Name";
	public const string KeyName_Path = "Path";
	public const string KeyName_Binds_Max = "BindsMax";
	public const string KeyName_Binds_Frame = "BindsFrame";
	public const string KeyName_Size = "Size";
	public const string KeyName_Format = "Format";
	public const string KeyName_Width = "Width";
	public const string KeyName_Height = "Height";
	public const string KeyName_Texture_Group = "TexGroup";

	enum TxListPanelRequest
	{
		TxrNone,
		TxrShow,
		TxrRunning,
		TxrHide
	}
	static TxListPanelRequest TxListPanelReq = TxListPanelRequest.TxrNone;

	IFont Font;
	ListPanel ListPanel;
	RenderTexturesListViewPanel ViewPanel;
	CheckButton SpecialTexs;
	CheckButton ResolveTexturePath;
	ConVarCheckbutton ShowTextureMemoryUsageOption;
	ConVarCheckbutton AllTextures;
	ConVarCheckbutton ViewTextures;
	Button CopyToClipboardButton;
	ToggleButton Collapse;
	CheckButton Alpha;
	CheckButton ThumbWarnings;
	CheckButton HideMipped;
	CheckButton FilteringChk;
	TextEntry FilteringText;
	int NumDisplayedSizeKB;
	Button ReloadAllMaterialsButton;
	Button CommitChangesButton;
	Button DiscardChangesButton;
	Label CVarListLabel;
	Label TotalUsageLabel;

	public TextureListPanel(Panel parent) : base(parent, "TextureListPanel") {
		SetSize(((VideoMode_Common)videoMode).GetModeStereoWidth() - 20, ((VideoMode_Common)videoMode).GetModeStereoHeight() - 20);
		SetPos(10, 10);
		SetVisible(true);

		SetTitle("Texture list", false);
		SetMenuButtonVisible(false);

		SetFgColor(new Color(0, 0, 0, 255));
		SetPaintBackgroundEnabled(true);

		CVarListLabel = new Label(this, "CVarListLabel", "cvars: mat_texture_limit, mat_texture_list, mat_picmip, mat_texture_list_txlod, mat_texture_list_txlod_sync");
		CVarListLabel.SetVisible(false); // CVarListLabel.SetVisible(true);

		TotalUsageLabel = new Label(this, "TotalUsageLabel", "");
		TotalUsageLabel.SetVisible(true);

		SpecialTexs = new CheckButton(this, "service", "Render Targets and Special Textures");
		SpecialTexs.SetVisible(true);
		SpecialTexs.AddActionSignalTarget(this);
		SpecialTexs.SetCommand("service");

		ResolveTexturePath = new CheckButton(this, "resolvepath", "Resolve Full Texture Path");
		ResolveTexturePath.SetVisible(true);
		ResolveTexturePath.AddActionSignalTarget(this);
		ResolveTexturePath.SetCommand("resolvepath");

		ShowTextureMemoryUsageOption = new ConVarCheckbutton(this, "ShowTextureMemoryUsageOption", "Show Memory Usage on HUD");
		ShowTextureMemoryUsageOption.SetVisible(true);
		ShowTextureMemoryUsageOption.SetConVar(mat_show_texture_memory_usage);

		AllTextures = new ConVarCheckbutton(this, "AllTextures", "Show ALL textures");
		AllTextures.SetVisible(true);
		AllTextures.SetConVar(mat_texture_list_all);
		AllTextures.AddActionSignalTarget(this);
		AllTextures.SetCommand("AllTextures");

		ViewTextures = new ConVarCheckbutton(this, "ViewTextures", "View textures thumbnails");
		ViewTextures.SetVisible(true);
		ViewTextures.SetConVar(mat_texture_list_view);
		ViewTextures.AddActionSignalTarget(this);
		ViewTextures.SetCommand("ViewThumbnails");

		CopyToClipboardButton = new Button(this, "CopyToClipboard", "Copy to Clipboard");
		CopyToClipboardButton.AddActionSignalTarget(this);
		CopyToClipboardButton.SetCommand("CopyToClipboard");

		Collapse = new ToggleButton(this, "Collapse", " ");
		Collapse.AddActionSignalTarget(this);
		Collapse.SetCommand("Collapse");
		Collapse.SetSelected(true);

		Alpha = new CheckButton(this, "ShowAlpha", "Alpha");
		Alpha.AddActionSignalTarget(this);
		Alpha.SetCommand("ShowAlpha");
		bool DefaultTxAlphaOn = true;
		Alpha.SetSelected(DefaultTxAlphaOn);

		ThumbWarnings = new CheckButton(this, "ThumbWarnings", "Warns");
		ThumbWarnings.AddActionSignalTarget(this);
		ThumbWarnings.SetCommand("ThumbWarnings");
		ThumbWarnings.SetSelected(WarnEnable);

		HideMipped = new CheckButton(this, "HideMipped", "Hide Mipped");
		HideMipped.AddActionSignalTarget(this);
		HideMipped.SetCommand("HideMipped");
		HideMipped.SetSelected(false);

		FilteringChk = new CheckButton(this, "FilteringChk", "Filter: ");
		FilteringChk.AddActionSignalTarget(this);
		FilteringChk.SetCommand("FilteringChk");
		FilteringChk.SetSelected(true);

		FilteringText = new TextEntry(this, "FilteringTxt");
		FilteringText.AddActionSignalTarget(this);

		ReloadAllMaterialsButton = new Button(this, "ReloadAllMaterials", "Reload All Materials");
		ReloadAllMaterialsButton.AddActionSignalTarget(this);
		ReloadAllMaterialsButton.SetCommand("ReloadAllMaterials");

		CommitChangesButton = new Button(this, "CommitChanges", "Commit Changes");
		CommitChangesButton.AddActionSignalTarget(this);
		CommitChangesButton.SetCommand("CommitChanges");

		DiscardChangesButton = new Button(this, "DiscardChanges", "Discard Changes");
		DiscardChangesButton.AddActionSignalTarget(this);
		DiscardChangesButton.SetCommand("DiscardChanges");

		ListPanel = new(this, "List Panel");
		ListPanel.SetVisible(!mat_texture_list_view.GetBool());

		int col = -1;
		ListPanel.AddColumnHeader(++col, KeyName_Name, "Texture Name", 200, 100, 700, ListPanel.ColumnFlags.ResizeWithWindow);
		ListPanel.AddColumnHeader(++col, KeyName_Path, "Path", 50, 50, 300, 0);
		ListPanel.AddColumnHeader(++col, KeyName_Size, "Kilobytes", 50, 50, 50, 0);
		ListPanel.SetSortFunc(col, KilobytesSortFunc);
		ListPanel.SetSortColumnEx(col, 0, true);
		ListPanel.AddColumnHeader(++col, KeyName_Texture_Group, "Group", 100, 100, 300, 0);
		ListPanel.AddColumnHeader(++col, KeyName_Format, "Format", 250, 50, 300, 0);
		ListPanel.AddColumnHeader(++col, KeyName_Width, "Width", 50, 50, 50, 0);
		ListPanel.AddColumnHeader(++col, KeyName_Height, "Height", 50, 50, 50, 0);
		ListPanel.AddColumnHeader(++col, KeyName_Binds_Frame, "# Binds", 50, 50, 50, 0);
		ListPanel.AddColumnHeader(++col, KeyName_Binds_Max, "BindsMax", 50, 50, 50, 0);

		SetBgColor(new Color(0, 0, 0, 100));

		ListPanel.SetBgColor(new Color(0, 0, 0, 100));

		ViewPanel = new RenderTexturesListViewPanel(this, "View Panel");
		ViewPanel.SetVisible(mat_texture_list_view.GetBool());
		ViewPanel.SetBgColor(new Color(0, 0, 0, 255));
		ViewPanel.SetDragEnabled(false);
		ViewPanel.SetDropEnabled(false);
		ViewPanel.SetPaintAlpha(DefaultTxAlphaOn);
		ViewPanel.SetDataListPanel(ListPanel);
	}

	// FIXME #37
	public override void Dispose() {
		base.Dispose();
		g_TextureListPanel = null;
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		Font = scheme.GetFont("DefaultVerySmall", false)!;
		Assert(Font != null);
	}

	private bool ShouldDraw() {
		if (mat_texture_list.GetBool())
			return true;

		if (TxListPanelReq == TxListPanelRequest.TxrShow || TxListPanelReq == TxListPanelRequest.TxrRunning)
			return true;

		return false;
	}

	public void UpdateTotalUsageLabel() {
		Span<char> data = stackalloc char[1024];
		Span<char> kb1 = stackalloc char[20];
		Span<char> kb2 = stackalloc char[20];
		Span<char> kb3 = stackalloc char[20];

		FmtCommaNumber(kb1, (uint)materialSystemDebugTextureInfo.GetTextureMemoryUsed(TextureMemoryType.BoundLastFrame + 511) / 1024);
		FmtCommaNumber(kb2, (uint)materialSystemDebugTextureInfo.GetTextureMemoryUsed(TextureMemoryType.TotalLoaded + 511) / 1024);
		FmtCommaNumber(kb3, (uint)NumDisplayedSizeKB);

		if (Collapse.IsSelected()) {
			ReadOnlySpan<char> title = "";
			sprintf(data, "%s[F %s Kb] / [T %s Kb] / [S %s Kb]").S(title).S(kb1).S(kb2).S(kb3);
		}
		else {
			ReadOnlySpan<char> title = "Texture Memory Usage";
			Span<char> kbMip1 = stackalloc char[20];
			Span<char> kbMip2 = stackalloc char[20];
			FmtCommaNumber(kbMip1, (uint)materialSystemDebugTextureInfo.GetTextureMemoryUsed(TextureMemoryType.EstimatePicmip1 + 511) / 1024);
			FmtCommaNumber(kbMip2, (uint)materialSystemDebugTextureInfo.GetTextureMemoryUsed(TextureMemoryType.EstimatePicmip2 + 511) / 1024);
			sprintf(data, "%s:  frame %s Kb  /  total %s Kb ( picmip1 = %s Kb, picmip2 = %s Kb )  /  shown %s Kb")
				.S(title).S(kb1).S(kb2).S(kbMip1).S(kbMip2).S(kb3);
		}

		TotalUsageLabel.SetText(data);
	}

	public override void OnTextChanged(Panel sender) => DoCommand("FilteringTxt");
	[UiCommand("Collapse")]
	[UiCommand("ViewThumbnails")]
	private void OnInvalidateLayoutCommand() => InvalidateLayout();

	[UiCommand("ShowAlpha")]
	private void OnShowAlpha() => ViewPanel.SetPaintAlpha(Alpha.IsSelected());

	[UiCommand("ThumbWarnings")]
	private void OnThumbWarnings() => WarnEnable = ThumbWarnings.IsSelected();

	[UiCommand("CopyToClipboard")]
	private void OnCopyToClipboard() {
		// CopyListPanelToClipboard(ListPanel);
	}

	[UiCommand("ReloadAllMaterials")]
	private void OnReloadAllMaterials() {
		cbuf.AddText("mat_reloadallmaterials");
		cbuf.Execute();
	}

	[UiCommand("CommitChanges")]
	private void OnCommitChanges() {
		cbuf.AddText("mat_texture_list_txlod_sync save");
		cbuf.Execute();
	}

	[UiCommand("DiscardChanges")]
	private void OnDiscardChanges() {
		cbuf.AddText("mat_texture_list_txlod_sync reset");
		cbuf.Execute();
	}

	public override void OnCommand(ReadOnlySpan<char> command) {
		mat_texture_list_on_f();
		InvalidateLayout();
	}

	private bool UpdateDisplayedItem(KeyValues dispData, KeyValues kv) {
		bool update = false;

		if (dispData.GetInt(KeyName_Binds_Frame) != kv.GetInt(KeyName_Binds_Frame)) {
			dispData.SetInt(KeyName_Binds_Frame, kv.GetInt(KeyName_Binds_Frame));
			update = true;
		}

		if (dispData.GetInt(KeyName_Binds_Max) != kv.GetInt(KeyName_Binds_Max)) {
			dispData.SetInt(KeyName_Binds_Max, kv.GetInt(KeyName_Binds_Max));
			update = true;
		}

		if (dispData.GetInt(KeyName_Size) != kv.GetInt(KeyName_Size) ||
			dispData.GetInt(KeyName_Width) != kv.GetInt(KeyName_Width) ||
			dispData.GetInt(KeyName_Height) != kv.GetInt(KeyName_Height) ||
			!dispData.GetString(KeyName_Format).Equals(kv.GetString(KeyName_Format), StringComparison.OrdinalIgnoreCase) ||
			!dispData.GetString(KeyName_Path).Equals(kv.GetString(KeyName_Path), StringComparison.OrdinalIgnoreCase) ||
			!dispData.GetString(KeyName_Texture_Group).Equals(kv.GetString(KeyName_Texture_Group), StringComparison.OrdinalIgnoreCase)
		) {
			dispData.SetInt(KeyName_Size, kv.GetInt(KeyName_Size));
			dispData.SetInt(KeyName_Width, kv.GetInt(KeyName_Width));
			dispData.SetInt(KeyName_Height, kv.GetInt(KeyName_Height));
			dispData.SetString(KeyName_Format, kv.GetString(KeyName_Format));
			dispData.SetString(KeyName_Path, kv.GetString(KeyName_Path));
			dispData.SetString(KeyName_Texture_Group, kv.GetString(KeyName_Texture_Group));
			update = true;
		}

		return update;
	}

	private int AddListItem(KeyValues kv) {
		int item = ListPanel.GetItem(kv.GetString(KeyName_Name))!;
		if (item == -1) {
			kv.SetName(kv.GetString(KeyName_Name));

			item = ListPanel.AddItem(kv, 0, false, false);
			ViewPanel.InvalidateLayout();
		}
		else {
			KeyValues values = ListPanel.GetItem(item)!;
			bool needsUpdate = UpdateDisplayedItem(values, kv);

			if (needsUpdate) {
				ListPanel.ApplyItemChanges(item);
				ViewPanel.Repaint();
			}
		}

		return item;
	}

	struct LayoutHorz
	{
		public Panel Panel;
		public int Width;
	}

	public override void PerformLayout() {
		base.PerformLayout();

		Collapse.SetPos(2, 10);
		Collapse.SetSize(10, 10);
		Collapse.SetVisible(true);

		bool Collapsed = Collapse.IsSelected();

		GetClientArea(out int x, out int y, out int w, out int t);

		int yOffset = y;

		CVarListLabel.SetPos(x, yOffset);
		CVarListLabel.SetWide(w);
		// yOffset += CVarListLabel.GetTall();
		CVarListLabel.SetVisible(false); // CVarListLabel.SetVisible(!Collapsed);

		TotalUsageLabel.SetPos(x, yOffset);
		TotalUsageLabel.SetWide(w);
		yOffset += TotalUsageLabel.GetTall();
		TotalUsageLabel.SetVisible(!Collapsed);

		Panel[] buttons = [
			SpecialTexs,
			ShowTextureMemoryUsageOption,
			AllTextures,
			ViewTextures,
			FilteringChk,
			HideMipped,
			ResolveTexturePath,
			CopyToClipboardButton
		];

		for (int i = 0; i < buttons.Length; i++) {
			buttons[i].SetPos(x, yOffset);
			buttons[i].SetWide(w / 2);
			yOffset += buttons[i].GetTall();
			buttons[i].SetVisible(!Collapsed);

			if (buttons[i] == ViewTextures) {
				ViewTextures.SetWide(170);
				int accumw = 170;

				Alpha.SetPos(x + accumw + 5, yOffset - ViewTextures.GetTall());
				Alpha.SetWide(85);
				accumw += 85;

				ThumbWarnings.SetPos(x + accumw + 5, yOffset - ViewTextures.GetTall());
				ThumbWarnings.SetWide(85);
				// accumw += 85;
			}

			if (buttons[i] == FilteringChk) {
				FilteringChk.SetWide(60);
				int accumw = 60;

				FilteringText.SetPos(x + accumw + 5, yOffset - FilteringChk.GetTall());
				FilteringText.SetWide(170);
				FilteringText.SetTall(FilteringChk.GetTall());
				FilteringText.SetVisible(!Collapsed);
				// accumw += 170;
			}
		}

		if (Collapsed) {
			int xOffset = 85;
			int Width;

			LayoutHorz[] layout = [
				new () { Panel = TotalUsageLabel, Width = 290 },
				new () { Panel = ViewTextures, Width = 170 },
				new () { Panel = Alpha, Width = 60 },
				new () { Panel = AllTextures, Width = 135 },
				new () { Panel = HideMipped, Width = 100 },
				new () { Panel = FilteringChk, Width = 60 },
				new () { Panel = FilteringText, Width = 130 },
				new () { Panel = ReloadAllMaterialsButton, Width = 130 },
				new () { Panel = CommitChangesButton, Width = 130 },
				new () { Panel = DiscardChangesButton, Width = 130 }
			];

			for (int k = 0; k < layout.Length; k++) {
				layout[k].Panel.SetPos(xOffset, 2);
				Width = layout[k].Width;
				Width = Math.Min(w - xOffset - 30, Width);
				layout[k].Panel.SetWide(Width);
				layout[k].Panel.SetVisible(Width > 50);

				if (Width > 50)
					xOffset += Width + 5;
			}

			yOffset = y;
		}

		Alpha.SetVisible(ViewTextures.IsSelected());
		ThumbWarnings.SetVisible(!Collapsed && ViewTextures.IsSelected());

		ListPanel.SetBounds(x, yOffset, w, t - (yOffset - y));
		ViewPanel.SetBounds(x, yOffset, w, t - (yOffset - y));

		ListPanel.SetVisible(!mat_texture_list_view.GetBool());
		ViewPanel.SetVisible(mat_texture_list_view.GetBool());
	}

	public void OnTurnedOn() {
		// RecursiveRequestToShowTextureList

		ListPanel?.DeleteAllItems();
		ViewPanel.GetRenderTxEditor()?.Close();

		MakePopup(false, false);
		MoveToFront();
	}

	private void EndPaint() => UpdateTotalUsageLabel();

	public override void Paint() {
		if (Font == null)
			return;

		if (!mat_texture_list.GetBool() || !materialSystemDebugTextureInfo.IsDebugTextureListFresh()) {
			EndPaint();
			return;
		}

		using SmartTextureKeyValues textureList = new();
		if (textureList.Get() == null)
			return;

		RenderTextureEditor rte = ViewPanel.GetRenderTxEditor();

		if (TxListPanelReq == TxListPanelRequest.TxrRunning && rte.IsVisible()) {
			rte.GetDispInfo(out KeyValues? kv, out int hint);
			if (kv != null && hint != 0) {
				KeyValues plv = ListPanel.IsValidItemID(hint) ? ListPanel.GetItem(hint)! : null!;
				if (plv != null && plv.GetString(KeyName_Name).Equals(kv.GetString(KeyName_Name), StringComparison.OrdinalIgnoreCase)) {
					KeyValues? valData = plv.GetFirstValue();
					KeyValues? valRendered = kv.GetFirstValue();
					for (; valData != null; valData = valData.GetNextValue(), valRendered = valRendered.GetNextValue()) {
						if (!valData.GetString().Equals(valRendered.GetString(), StringComparison.OrdinalIgnoreCase))
							break;
					}
					if (valData != null || valRendered != null)
						rte.SetDispInfo(plv, hint);
				}
				else
					kv = null;
			}
		}

		if (mat_texture_list_all.GetBool()) {
			if (TxListPanelReq != TxListPanelRequest.TxrRunning) {
				mat_texture_list.SetValue(0);
				TxListPanelReq = TxListPanelRequest.TxrShow;
			}
			else
				TxListPanelReq = TxListPanelRequest.TxrRunning;
		}
		else if (TxListPanelReq == TxListPanelRequest.TxrShow) {
			ListPanel.RemoveAll();
			ViewPanel.InvalidateLayout();
			TxListPanelReq = TxListPanelRequest.TxrRunning;
			EndPaint();
			return;
		}

		HashSet<int> itemsTouched = new();

		KeepSpecialKeys(textureList.Get()!, SpecialTexs.IsSelected());

		if (FilteringChk.IsSelected() && FilteringText.GetTextLength() > 0) {
			Span<char> filter = stackalloc char[260];
			FilteringText.GetText(filter);
			KeepKeysMatchingFilter(textureList.Get()!, filter);
		}

		if (HideMipped.IsSelected())
			KeepKeysMarkedNoMip(textureList.Get()!);

		int totalDisplayedSizeInBytes = 0;
		Span<char> resolveName = stackalloc char[256];
		Span<char> resolveNameArg = stackalloc char[256];
		for (KeyValues? cur = textureList.Get()!.GetFirstSubKey(); cur != null; cur = cur.GetNextKey()) {
			int sizeInBytes = cur.GetInt(KeyName_Size);
			totalDisplayedSizeInBytes += sizeInBytes;

			int numCount = cur.GetInt(KeyName_Size);
			if (numCount > 1)
				sizeInBytes *= numCount;

			int sizeInKilo = (sizeInBytes + 511) / 1024;
			cur.SetInt(KeyName_Size, sizeInKilo);

			if (ResolveTexturePath.IsSelected()) {
				resolveName.Clear();
				resolveNameArg.Clear();
				sprintf(resolveNameArg, "materials/%s.vtf").S(cur.GetString(KeyName_Name));
				ReadOnlySpan<char> resolvedName = g_pFileSystem.RelativePathToFullPath(resolveNameArg, "game", resolveName);
				if (resolveName.Length > 0)
					cur.SetString(KeyName_Path, resolvedName);
			}

			int item = AddListItem(cur);
			itemsTouched.Add(item);
		}

		NumDisplayedSizeKB = (totalDisplayedSizeInBytes + 511) / 1024;
		List<int> itemsToRemove = [];
		for (int i = 0; i < ListPanel.GetItemCount(); i++) {
			int itemID = ListPanel.GetItemIDFromRow(i);
			if (!itemsTouched.Contains(itemID))
				itemsToRemove.Add(itemID);
		}

		itemsToRemove.Sort((a, b) => b.CompareTo(a));

		int numRemoved = itemsToRemove.Count;
		foreach (int itemID in itemsToRemove)
			ListPanel.RemoveItem(itemID);

		// todo sort

		if (numRemoved > 0)
			ViewPanel.InvalidateLayout();

		EndPaint();
	}

	public static void CreateTextureListPanel(Panel parent) => g_TextureListPanel = new(parent);

	[ConCommand("+mat_texture_list")]
	static private void mat_texture_list_on_f() {
		ConVarRef sv_cheats = new("sv_cheats");
		if (sv_cheats.IsValid() && sv_cheats.GetBool() == false)
			return;

		ConVarRef mat_queue_mode = new("mat_queue_mode");
		if (mat_queue_mode.IsValid() && SaveQueueState == int.MinValue) {
			SaveQueueState = mat_queue_mode.GetInt();
			mat_queue_mode.SetValue(0);
		}

		mat_texture_list.SetValue(1);

		g_TextureListPanel?.OnTurnedOn();
	}

	[ConCommand("-mat_texture_list")]
	private static void mat_texture_list_off_f() {
		mat_texture_list.SetValue(0);
		TxListPanelReq = TxListPanelRequest.TxrHide;

		if (CursorSet) {
			// surface.SetCursorAlwaysVisible(false);
			CursorSet = false;
		}

		if (SaveQueueState != int.MinValue) {
			ConVarRef mat_queue_mode = new("mat_queue_mode");
			mat_queue_mode.SetValue(SaveQueueState);
			SaveQueueState = int.MinValue;
		}
	}

	static int KilobytesSortFunc(Panel _, ListPanelItem item1, ListPanelItem item2) {
		var a = int.Parse(item1.kv!.GetString(KeyName_Size));
		var b = int.Parse(item2.kv!.GetString(KeyName_Size));

		if (a < b) return 1;
		if (a > b) return -1;
		return 0;
	}

	private static bool StripDirName(Span<char> filename) {
		if (filename.Length == 0 || filename[0] == '\0')
			return false;

		Span<char> lastSlash = filename;
		while (true) {
			Span<char> testSlash = lastSlash.Slice(0, lastSlash.IndexOf('/'));
			if (testSlash.Length == 0) {
				testSlash = lastSlash[..lastSlash.IndexOf('\\')];
				if (testSlash.Length == 0)
					break;
			}

			testSlash = testSlash[1..];
			lastSlash = testSlash;
		}

		if (lastSlash == filename)
			return false;
		else {
			Assert(lastSlash[^1] == '/' || lastSlash[^1] == '\\');
			lastSlash[^1] = '\0';
			return true;
		}
	}

	private static void ToLowerInplace(Span<char> str) {
		for (int i = 0; i < str.Length; i++) {
			if (char.IsUpper(str[i]))
				str[i] = char.ToLower(str[i]);
		}
	}
	private static void KeepSpecialKeys(KeyValues textureList, bool serviceKeys) {
		KeyValues? pNext;
		for (KeyValues? pCur = textureList.GetFirstSubKey(); pCur != null; pCur = pNext) {
			pNext = pCur.GetNextKey();

			bool isServiceKey = false;

			ReadOnlySpan<char> name = pCur.GetString(KeyName_Name);
			if (name.StartsWith("_") ||
				name.StartsWith("[") ||
				name.Equals("backbuffer", StringComparison.OrdinalIgnoreCase) ||
				name.StartsWith("colorcorrection", StringComparison.OrdinalIgnoreCase) ||
				name.Equals("depthbuffer", StringComparison.OrdinalIgnoreCase) ||
				name.Equals("frontbuffer", StringComparison.OrdinalIgnoreCase) ||
				name.Equals("normalize", StringComparison.OrdinalIgnoreCase) ||
				name.Length == 0) {
				isServiceKey = true;
			}

			if (isServiceKey != serviceKeys)
				textureList.RemoveSubKey(pCur);
			else if (isServiceKey)
				pCur.SetInt("SpecialTx", 1);
		}
	}

	public static void UpdateTextureListPanel() {
		if (mat_show_texture_memory_usage.GetInt() != 0) {
			Con_NPrint_s info = new() {
				Index = 4,
				TimeToLive = 0.2,
				FixedWidthFont = true,
				Color = new(1, 0.5f, 0)
			};

			Span<char> kb1 = stackalloc char[20];
			Span<char> kb2 = stackalloc char[20];

			FmtCommaNumber(kb1, (uint)materialSystemDebugTextureInfo.GetTextureMemoryUsed(TextureMemoryType.BoundLastFrame + 511) / 1024);
			FmtCommaNumber(kb2, (uint)materialSystemDebugTextureInfo.GetTextureMemoryUsed(TextureMemoryType.TotalLoaded + 511) / 1024);

			// todo Con_NXPrintf
		}

		// MatViewOverride::DisplaySelectedTextures();

		materialSystemDebugTextureInfo.EnableGetAllTextures(true || mat_texture_list_all.GetBool());
		materialSystemDebugTextureInfo.EnableDebugTextureList(mat_texture_list.GetInt() > 0);

		bool shouldDrawTxListPanel = g_TextureListPanel!.ShouldDraw();
		if (g_TextureListPanel.IsVisible() != shouldDrawTxListPanel) {
			g_TextureListPanel.SetVisible(shouldDrawTxListPanel);

			if (shouldDrawTxListPanel)
				mat_texture_list_on_f();
			else
				mat_texture_list_off_f();
		}
	}

	private void KeepKeysMatchingFilter(KeyValues textureList, ReadOnlySpan<char> filter) {
		if (filter.Length == 0)
			return;

		Span<char> chFilter = stackalloc char[260];
		Span<char> chName = stackalloc char[260];

		filter.CopyTo(chFilter);
		ToLowerInplace(chFilter);

		KeyValues? next;
		for (KeyValues? cur = textureList.GetFirstSubKey(); cur != null; cur = next) {
			next = cur.GetNextKey();

			ReadOnlySpan<char> name = cur.GetString(KeyName_Name);
			name.CopyTo(chName);
			ToLowerInplace(chName);

			if (!chName.Contains(chFilter, StringComparison.OrdinalIgnoreCase))
				textureList.RemoveSubKey(cur);
		}
	}

	private void KeepKeysMarkedNoMip(KeyValues textureList) {
		KeyValues? next;
		for (KeyValues? cur = textureList.GetFirstSubKey(); cur != null; cur = next) {
			next = cur.GetNextKey();

			ReadOnlySpan<char> textureFile = cur.GetString(KeyName_Name);
			ReadOnlySpan<char> textureGroup = cur.GetString(KeyName_Texture_Group);
			if (!textureFile.IsEmpty) {
				ITexture? matTexture = materialSystem.FindTexture(textureFile, textureGroup, false);
				if (matTexture != null && (matTexture.GetFlags() & (int)TextureFlags.NoMip) == 0)
					textureList.RemoveSubKey(cur);
			}
		}
	}

	public static void FmtCommaNumber(Span<char> buffer, uint number) {
		buffer[0] = '\0';
		for (uint divisor = 1000 * 1000 * 1000; divisor > 0; divisor /= 1000) {
			if (number >= divisor) {
				uint print = number / divisor % 1000;
				sprintf(buffer, (number / divisor < 1000) ? "%d," : "%03d,").D(print);
			}
		}

		int len = buffer.IndexOf('\0');
		if (len == 0)
			sprintf(buffer, "0");
		else if (buffer[len - 1] == ',')
			buffer[len - 1] = '\0';
	}
}


class SmartTextureKeyValues : IDisposable
{
	private KeyValues? _p;

	public SmartTextureKeyValues() {
		var p = materialSystemDebugTextureInfo.GetDebugTextureList();
		if (p != null)
			_p = p.MakeCopy();
	}

	public KeyValues? Get() => _p;

	public void Dispose() {
		_p = null;
	}
}
#endif
