global using static Game.Client.HUD.HudExtensions;

global using HudTextureDict = System.Collections.Generic.Dictionary<ulong, Game.Client.HUD.HudTexture>;

using CommunityToolkit.HighPerformance;

using Game.Shared;

using Source;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.Utilities;
using Source.GUI.Controls;

using System.Drawing;

using Color = Source.Color;

namespace Game.Client.HUD;

public static class HudExtensions
{
	public static T? GET_HUDELEMENT<T>() where T : IHudElement => (T?)gHUD.FindElement(typeof(T).Name);
}

struct HudTextureFileRef
{
	const int fileKeyLength = 64;
	const int hudTexturePrefix = 16;

	public InlineArray64<char> FileKey;
	public InlineArray16<char> HudTexturePrefix;
	public uint PrefixLength;
	public UtlSymbol FileKeySymbol;

	public HudTextureFileRef(ReadOnlySpan<char> fileKey, ReadOnlySpan<char> texturePrefix) {
		strcpy(FileKey, fileKey);
		strcpy(HudTexturePrefix, texturePrefix);
		PrefixLength = (uint)strlen(texturePrefix);
		FileKeySymbol = new UtlSymbol(fileKey);
	}
}

public struct WRect
{
	public int Left;
	public int Right;
	public int Top;
	public int Bottom;
}

public class HudTexture
{
	public InlineArray64<char> ShortName;
	public InlineArray64<char> TextureFile;

	public int Width() => RC.Right - RC.Left;
	public int Height() => RC.Bottom - RC.Top;

	public void Precache() {

	}

	public void DrawSelf(int x, int y, in Color clr) {
		DrawSelf(x, y, Width(), Height(), clr);
	}

	public void DrawSelf(int x, int y, int w, int h, in Color clr) {
		if (RenderUsingFont) {
			surface.DrawSetTextFont(Font);
			surface.DrawSetTextColor(clr);
			surface.DrawSetTextPos(x, y);
			surface.DrawChar(CharacterInFont);
		}
		else {
			if (TextureID == TextureID.INVALID)
				return;

			surface.DrawSetTexture(TextureID);
			surface.DrawSetColor(clr);
			surface.DrawTexturedSubRect(x, y, x + w, y + h, TexCoords[0], TexCoords[1], TexCoords[2], TexCoords[3]);
		}
	}

	public void DrawSelfCropped(int x, int y, int cropx, int cropy, int cropw, int croph, in Color clr) => DrawSelfCropped(x, y, cropx, cropy, cropw, croph, cropw, croph, clr);
	public void DrawSelfCropped(int x, int y, int cropx, int cropy, int cropw, int croph, int finalWidth, int finalHeight, in Color clr) {
		if (RenderUsingFont) {
			int height = surface.GetFontTall(Font);
			float frac = (height - croph) / height;
			y -= cropy;

			surface.DrawSetTextFont(Font);
			surface.DrawSetTextColor(clr);
			surface.DrawSetTextPos(x, y);

			CharRenderInfo info = new();
			if (surface.DrawGetCharRenderInfo(CharacterInFont, ref info)) {
				if (cropy != 0) {
					info.Verts[0].Position.Y = MathLib.Lerp(frac, info.Verts[0].Position.Y, info.Verts[1].Position.Y);
					info.Verts[0].TexCoord.Y = MathLib.Lerp(frac, info.Verts[0].TexCoord.Y, info.Verts[1].TexCoord.Y);
				}
				else if (croph != height) {
					info.Verts[1].Position.Y = MathLib.Lerp(1.0f - frac, info.Verts[0].Position.Y, info.Verts[1].Position.Y);
					info.Verts[1].TexCoord.Y = MathLib.Lerp(1.0f - frac, info.Verts[0].TexCoord.Y, info.Verts[1].TexCoord.Y);
				}
				surface.DrawRenderCharFromInfo(info);
			}
		}
		else {
			if (TextureID == TextureID.INVALID)
				return;

			float fw = Width();
			float fh = Height();

			float twidth = TexCoords[2] - TexCoords[0];
			float theight = TexCoords[3] - TexCoords[1];

			float[] tCoords = [
				TexCoords[0] + cropx / fw * twidth,
				TexCoords[1] + cropy / fh * theight,
				TexCoords[0] + (cropx + cropw) / fw * twidth,
				TexCoords[1] + (cropy + croph) / fh * theight
			];
			surface.DrawSetTexture(TextureID);
			surface.DrawSetColor(clr);
			surface.DrawTexturedSubRect(
				x, y,
				x + finalWidth, y + finalHeight,
				tCoords[0], tCoords[1],
				tCoords[2], tCoords[3]);
		}
	}

	public bool RenderUsingFont;
	public bool Precached;
	public char CharacterInFont;
	public IFont? Font;
	public TextureID TextureID;
	public InlineArray4<float> TexCoords;
	public WRect RC;
}

public enum ProgressBarType
{
	Horizontal = 0,
	Vertical,
	HorizontalInv
}

[EngineComponent]
public class Hud(HudElementHelper HudElementHelper)
{
	readonly HudTextureDict Icons = [];
	public readonly List<IHudElement> HudList = [];
	internal InButtons KeyBits;
	bool HudTexturesLoaded;

	public Color ClrNormal;
	public Color ClrCaution;
	public Color ClrYellowish;

	readonly List<string> RenderGroupNames = [];
	readonly Dictionary<int, HudRenderGroup> RenderGroups = [];

	public void Init() {
		HudElementHelper.CreateAllElements(this);
		foreach (var element in HudList)
			element.Init();

		KeyValues kv = new KeyValues("layout");
		if (kv.LoadFromFile(filesystem, "scripts/HudLayout.res")) {
			int numelements = HudList.Count;

			for (int i = 0; i < numelements; i++) {
				IHudElement element = HudList[i];

				if (element is not Panel panel) {
					Msg($"Non-vgui hud element {HudList[i].ElementName}\n");
					continue;
				}

				KeyValues? key = kv.FindKey(panel.GetName(), false);
				if (key == null)
					Msg($"Hud element '{element.ElementName}' doesn't have an entry '{panel.GetName()}' in scripts/HudLayout.res\n");

				if (!element.IsParentedToClientDLLRootPanel && panel.GetParent() == null)
					DevMsg($"Hud element '{element.ElementName}'/'{panel.GetName()}' doesn't have a parent\n");
			}
		}

		if (HudTexturesLoaded)
			return;

		HudTexturesLoaded = true;

		HudTextureDict textureList = [];

		Span<char> sz = stackalloc char[128];
		strcpy(sz, "scripts/hud_textures.txt");
		LoadHudTextures(textureList, sz.SliceNullTerminatedString());
		strcpy(sz, "scripts/mod_textures.txt");
		LoadHudTextures(textureList, sz.SliceNullTerminatedString());

		foreach (var t in textureList)
			AddSearchableHudIconToList(t.Value);
	}

	public void VidInit() {
		foreach (var element in HudList)
			element.VidInit();

		ResetHUD();
	}

	public void LevelShutdown() {
		foreach (var element in HudList)
			element.LevelShutdown();
	}

	public void InitColors(IScheme scheme) {
		ClrNormal = scheme.GetColor("Normal", new(255, 208, 64, 255));
		ClrCaution = scheme.GetColor("Caution", new(255, 48, 0, 255));
		ClrYellowish = scheme.GetColor("Yellowish", new(255, 160, 0, 255));
	}

	public void InitFonts() {

	}

	internal void AddHudElement(IHudElement element) {
		HudList.Add(element);
		element.NeedsRemove = true;
	}

	public IHudElement? FindElement(ReadOnlySpan<char> name) {
		foreach (var hudElement in HudList) {
			if (name.Equals(hudElement.ElementName, StringComparison.OrdinalIgnoreCase))
				return hudElement;
		}

		DevWarning(1, $"Could not find HUD element: {name}\n");
		Assert(false);
		return null;
	}

	public int GetSensitivity() {
		return 0;
	}

	internal void ResetHUD() {
		clientMode.GetViewportAnimationController()!.CancelAllAnimations();
	}

	internal void RefreshHudTextures() {
	}

	internal bool IsHidden(HideHudBits hudFlags) {
		if (!engine.IsInGame())
			return true;

		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null)
			return true;

		HideHudBits hideHud = (HideHudBits)player.Local.HideHUD;
		// todo: hidehud convar

		if ((hideHud & HideHudBits.All) != 0)
			return true;

		if ((hideHud & HideHudBits.PlayerDead) != 0 && player.GetHealth() <= 0 && !player.IsAlive())
			return true;

		if ((hideHud & HideHudBits.NeedSuit) != 0 && !player.IsSuitEquipped())
			return true;

		return (hudFlags & hideHud) != 0;
	}

	internal void ProcessInput(bool active) {
		if (active) {
			KeyBits = input.GetButtonBits(0);
			gHUD.Think();
		}
	}

	internal static void LoadHudTextures(HudTextureDict list, Span<char> filenameWithExtension) {
		KeyValues? temp, textureSection;

		KeyValues keyValuesData = new();
		if (keyValuesData.LoadFromFile(filesystem, filenameWithExtension)) {
			List<HudTextureFileRef> hudTextureFileRefs = [];
			hudTextureFileRefs.Add(new HudTextureFileRef("file", ""));

			KeyValues? textureFileRefs = keyValuesData.FindKey("TextureFileRefs");
			if (textureFileRefs != null) {
				temp = textureFileRefs.GetFirstSubKey();
				while (temp != null) {
					hudTextureFileRefs.Add(new HudTextureFileRef(temp.Name, temp.GetString("prefix", "")));
					temp = temp.GetNextKey();
				}
			}

			textureSection = keyValuesData.FindKey("TextureData");
			if (textureSection != null) {
				temp = textureSection.GetFirstSubKey();
				while (temp != null) {
					if (!temp.GetString("font", null).IsEmpty) {
						HudTexture tex = new HudTexture();

						// Key Name is the sprite name
						strcpy(tex.ShortName, temp.Name);

						// it's a font-based icon
						tex.RenderUsingFont = true;
						tex.CharacterInFont = temp.GetString("character", "\0")[0];
						strcpy(tex.TextureFile, temp.GetString("font"));

						list.TryAdd(((ReadOnlySpan<char>)tex.ShortName).Hash(false), tex);
					}
					else {
						int iTexLeft = temp.GetInt("x", 0),
							iTexTop = temp.GetInt("y", 0),
							iTexRight = temp.GetInt("width", 0) + iTexLeft,
							iTexBottom = temp.GetInt("height", 0) + iTexTop;

						for (int i = 0; i < hudTextureFileRefs.Count(); i++) {
							ReadOnlySpan<char> fileName = temp.GetString(hudTextureFileRefs[i].FileKeySymbol, null);
							if (!fileName.IsEmpty) {
								HudTexture tex = new HudTexture();

								tex.RenderUsingFont = false;
								tex.RC.Left = iTexLeft;
								tex.RC.Top = iTexTop;
								tex.RC.Right = iTexRight;
								tex.RC.Bottom = iTexBottom;

								strcpy(tex.ShortName, hudTextureFileRefs.AsSpan()[i].HudTexturePrefix);
								strcpy(tex.ShortName[(int)hudTextureFileRefs[i].PrefixLength..], temp.Name.SliceNullTerminatedString());
								strcpy(tex.TextureFile, fileName);

								list.TryAdd(((ReadOnlySpan<char>)tex.ShortName).Hash(false), tex);
							}
						}
					}

					temp = temp.GetNextKey();
				}
			}
		}
	}

	internal HudTexture? GetIcon(ReadOnlySpan<char> icon) {
		if (Icons.TryGetValue(icon.Hash(false), out HudTexture? tex))
			return tex;

		return null;
	}

	internal HudTexture AddUnsearchableHudIconToList(HudTexture texture) {
		Span<char> composedName = stackalloc char[512];

		if (texture.RenderUsingFont)
			sprintf(composedName, "%s_c%i").S(texture.TextureFile).I(texture.CharacterInFont);
		else
			sprintf(composedName, "%s_%i_%i_%i_%i").S(texture.TextureFile).I(texture.RC.Left).I(texture.RC.Top).I(texture.RC.Right).I(texture.RC.Bottom);


		HudTexture? icon = GetIcon(composedName);
		if (icon != null)
			return icon;

		HudTexture newTexture = new();
		texture.CopyInstantiatedReferenceTo(newTexture);

		SetupNewHudTexture(newTexture);

		Icons.Add(composedName.Hash(false), newTexture);
		return newTexture;
	}

	internal HudTexture AddSearchableHudIconToList(HudTexture texture) {
		HudTexture? icon = GetIcon(texture.ShortName);
		if (icon != null)
			return icon;

		HudTexture newTexture = new();
		texture.CopyInstantiatedReferenceTo(newTexture);

		SetupNewHudTexture(newTexture);

		Icons.Add(((ReadOnlySpan<char>)newTexture.ShortName).Hash(false), newTexture);
		return newTexture;
	}

	private void SetupNewHudTexture(HudTexture t) {
		if (t.RenderUsingFont) {
			IScheme scheme = vguiSchemeManager.GetScheme("ClientScheme")!;
			t.Font = scheme.GetFont(t.TextureFile, true);
			t.RC.Top = 0;
			t.RC.Left = 0;
			t.RC.Right = surface.GetCharacterWidth(t.Font, t.CharacterInFont);
			t.RC.Bottom = surface.GetFontTall(t.Font);
		}
		else {
			// Set up texture id and texture coordinates
			t.TextureID = surface.CreateNewTextureID();
			surface.DrawSetTextureFile(t.TextureID, t.TextureFile, 0, false);

			surface.DrawGetTextureSize(t.TextureID, out int wide, out int tall);

			t.TexCoords[0] = (float)(t.RC.Left + 0.5f) / wide;
			t.TexCoords[1] = (float)(t.RC.Top + 0.5f) / tall;
			t.TexCoords[2] = (float)(t.RC.Right - 0.5f) / wide;
			t.TexCoords[3] = (float)(t.RC.Bottom - 0.5f) / tall;
		}
	}

	public void UpdateHud(bool active) {
		KeyBits &= ~(InButtons.Weapon1 | InButtons.Weapon2);
		// clientMode.Update();
		// LCD.Update();
	}

	public void Think() {
		foreach (IHudElement element in HudList) {
			bool visible = element.ShouldDraw();
			element.SetActive(visible);
			Panel? panel = (Panel?)element;
			if (panel != null && panel.Visible != visible)
				panel.SetVisible(visible);
			else if (panel == null)
				Assert(false, "All HUD elements should derive from vgui");

			if (visible)
				element.ProcessInput();
		}

		// BaseCombatWeapon? weapon = BaseCombatWeapon.GetActiveWeapon();
		// weapon?.HandleInput();

		// screenshottime
	}

	public void DrawProgressBar(int x, int y, int width, int height, float percentage, Color clr, ProgressBarType type) {
		percentage = Math.Clamp(percentage, 0.0f, 1.0f);

		Color lowColor = clr;
		lowColor[0] /= 2;
		lowColor[1] /= 2;
		lowColor[2] /= 2;

		if (type == ProgressBarType.Vertical) {
			int barOfs = (int)(height * percentage);
			surface.DrawSetColor(clr);
			surface.DrawFilledRect(x, y, x + width, y + barOfs);
			surface.DrawSetColor(lowColor);
			surface.DrawFilledRect(x, y + barOfs, x + width, y + height);
		}
		else if (type == ProgressBarType.Horizontal) {
			int barOfs = (int)(width * percentage);
			surface.DrawSetColor(clr);
			surface.DrawFilledRect(x, y, x + barOfs, y + height);
			surface.DrawSetColor(lowColor);
			surface.DrawFilledRect(x + barOfs, y, x + width, y + height);
		}
		else if (type == ProgressBarType.HorizontalInv) {
			int barOfs = (int)(width * percentage);
			surface.DrawSetColor(lowColor);
			surface.DrawFilledRect(x, y, x + barOfs, y + height);
			surface.DrawSetColor(clr);
			surface.DrawFilledRect(x + barOfs, y, x + width, y + height);
		}
	}

	public void DrawIconProgressBar(int x, int y, HudTexture icon, HudTexture icon2, float percentage, Color clr, ProgressBarType type) {
		if (icon == null)
			return;

		percentage = Math.Clamp(percentage, 0.0f, 1.0f);

		int height = icon.Height();
		int width = icon.Width();

		if (type == ProgressBarType.Vertical) {
			int barOfs = (int)(height * percentage);
			icon2.DrawSelfCropped(x, y, 0, 0, width, barOfs, clr);
			icon.DrawSelfCropped(x, y + barOfs, 0, barOfs, width, height - barOfs, clr);
		}
		else if (type == ProgressBarType.Horizontal) {
			int barOfs = (int)(width * percentage);
			icon2.DrawSelfCropped(x, y, 0, 0, barOfs, height, clr);
			icon.DrawSelfCropped(x + barOfs, y, barOfs, 0, width - barOfs, height, clr);
		}
	}

	bool DoesRenderGoupExist(int groupIndex) => groupIndex >= 0 && groupIndex < RenderGroupNames.Count;

	int LookupRenderGroupIndexByName(ReadOnlySpan<char> groupName) {
		for (int i = 0; i < RenderGroupNames.Count; i++) {
			if (groupName.Equals(RenderGroupNames[i], StringComparison.OrdinalIgnoreCase))
				return i;
		}

		return -1;
	}

	bool LockRenderGroup(int groupIndex, IHudElement? locker = null) {
		if (!DoesRenderGroupExist(groupIndex))
			return false;

		if (!RenderGroups.TryGetValue(groupIndex, out var group) || group == null)
			return false;

		if (locker == null)
			group.Hidden = true;
		else {
			bool found = false;
			for (int i = 0; i < group.LockingElements.Count; i++) {
				if (ReferenceEquals(group.LockingElements[i], locker)) {
					found = true;
					break;
				}
			}

			if (!found) {
				group.LockingElements.Add(locker);
				group.LockingElements.Sort((a, b) => a.GetRenderGroupPriority().CompareTo(b.GetRenderGroupPriority()));
			}
		}

		return true;
	}


	bool UnlockRenderGroup(int groupIndex, IHudElement? locker = null) {
		if (!DoesRenderGroupExist(groupIndex))
			return false;

		if (!RenderGroups.TryGetValue(groupIndex, out var group))
			return false;

		if (group.Hidden && locker == null) {
			group.Hidden = false;
			return true;
		}

		for (int i = 0; i < group.LockingElements.Count; i++) {
			if (locker == group.LockingElements[i]) {
				group.LockingElements.RemoveAt(i);
				return true;
			}
		}

		return false;
	}

	public bool IsRenderGroupLockedFor(IHudElement? hudElement, int groupIndex) {
		if (!DoesRenderGroupExist(groupIndex))
			return false;

		if (!RenderGroups.TryGetValue(groupIndex, out var group) || group == null)
			return false;

		if (group.Hidden)
			return true;

		if (group.LockingElements.Count == 0)
			return false;

		if (hudElement == null)
			return true;

		var locker = group.LockingElements[0];
		return locker != hudElement && locker.GetRenderGroupPriority() <= hudElement.GetRenderGroupPriority();
	}

	int RegisterForRenderGroup(ReadOnlySpan<char> groupName) {
		int index = LookupRenderGroupIndexByName(groupName);
		if (index != -1)
			return index;

		return AddHudRenderGroup(groupName);
	}

	int AddHudRenderGroup(ReadOnlySpan<char> groupName) {
		RenderGroupNames.Add(groupName.ToString());
		int idx = RenderGroupNames.Count - 1;
		RenderGroups.Add(idx, new());
		return idx;
	}

	bool DoesRenderGroupExist(int groupIndex) => RenderGroups.ContainsKey(groupIndex);
}

public class HudElementHelper
{
	Hud HUD;
	public void CreateAllElements(Hud HUD) {
		this.HUD = HUD;
		var declaredHudElements = ReflectionUtils.GetLoadedTypesWithAttribute<DeclareHudElementAttribute>();
		foreach (var kvp in declaredHudElements) {
			Type type = kvp.Key;
			DeclareHudElementAttribute hudElement = kvp.Value;
			string name = hudElement.Name ?? type.Name;

			IHudElement? element = Activator.CreateInstance(type, [name]) as IHudElement;
			if (element != null)
				HUD.AddHudElement(element);
		}
	}
}

class HudRenderGroup
{
	public bool Hidden;
	public List<IHudElement> LockingElements = [];
	public HudRenderGroup() => Hidden = false;
}
