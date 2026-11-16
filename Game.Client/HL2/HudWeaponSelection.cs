using Game.Client.HUD;
using Game.Shared;

using Source;
using Source.Common.Commands;
using Source.Common.GUI;
using Source.Common.MaterialSystem;
using Source.GUI.Controls;

namespace Game.Client.HL2;

[DeclareHudElement(Name = "CHudWeaponSelection")]
class HudWeaponSelection : BaseHudWeaponSelection, IHudElement
{
	public static ConVar hud_showemptyweaponslots = new("hud_showemptyweaponslots", "1", FCvar.Archive, "Shows slots for missing weapons when recieving weapons out of order");

	const float SELECTION_TIMEOUT_THRESHOLD = 0.5f;  // Seconds
	const float SELECTION_FADEOUT_TIME = 0.75f;
	const float PLUS_DISPLAY_TIMEOUT = 0.5f; // Seconds
	const float PLUS_FADEOUT_TIME = 0.75f;
	const float FASTSWITCH_DISPLAY_TIMEOUT = 1.5f;
	const float FASTSWITCH_FADEOUT_TIME = 1.5f;
	const float CAROUSEL_SMALL_DISPLAY_ALPHA = 200.0f;
	const float FASTSWITCH_SMALL_DISPLAY_ALPHA = 160.0f;
	const float MAX_CAROUSEL_SLOTS = 5;

	[PanelAnimationVar("NumberFont", "HudSelectionNumbers")] protected IFont NumberFont;
	[PanelAnimationVar("TextFont", "HudSelectionText")] protected IFont TextFont;
	[PanelAnimationVar("Blur", "0")] protected float Blur;
#if GMOD_DLL
	[PanelAnimationVarAliasType("LargeBoxUnselectedTall", "24", "proportional_float")] protected float UnselectedBoxSize;
#endif
	[PanelAnimationVarAliasType("SmallBoxSize", "32", "proportional_float")] protected float SmallBoxSize;
	[PanelAnimationVarAliasType("LargeBoxWide", "108", "proportional_float")] protected float LargeBoxWide;
	[PanelAnimationVarAliasType("LargeBoxTall", "72", "proportional_float")] protected float LargeBoxTall;
	[PanelAnimationVarAliasType("MediumBoxWide", "75", "proportional_float")] protected float MediumBoxWide;
	[PanelAnimationVarAliasType("MediumBoxTall", "50", "proportional_float")] protected float MediumBoxTall;
	[PanelAnimationVarAliasType("BoxGap", "12", "proportional_float")] protected float BoxGap;
	[PanelAnimationVarAliasType("SelectionNumberXPos", "4", "proportional_float")] protected float SelectionNumberXPos;
	[PanelAnimationVarAliasType("SelectionNumberYPos", "4", "proportional_float")] protected float SelectionNumberYPos;
	[PanelAnimationVarAliasType("TextYPos", "54", "proportional_float")] protected float TextYPos;
	[PanelAnimationVar("Alpha", "0")] protected float AlphaOverride;
	[PanelAnimationVar("SelectionAlpha", "0")] protected float SelectionAlphaOverride;
	[PanelAnimationVar("TextColor", "SelectionTextFg")] protected Color TextColor;
	[PanelAnimationVar("NumberColor", "SelectionNumberFg")] protected Color NumberColor;
	[PanelAnimationVar("EmptyBoxColor", "SelectionEmptyBoxBg")] protected Color EmptyBoxColor;
	[PanelAnimationVar("BoxColor", "SelectionBoxBg")] protected Color BoxColor;
	[PanelAnimationVar("SelectedBoxColor", "SelectionSelectedBoxBg")] protected Color SelectedBoxColor;
	[PanelAnimationVar("SelectedFgColor", "FgColor")] protected Color SelectedFgColor;
	[PanelAnimationVar("SelectedFgColor", "BgColor")] protected Color BrightBoxColor;
	[PanelAnimationVar("SelectionGrowTime", "0.1")] protected float WeaponPickupGrowTime;
	[PanelAnimationVar("TextScan", "1.0")] protected float TextScan;
	[PanelAnimationVar("WeaponBoxOffset", "0", "float")] protected float HorizWeaponSelectOffsetPoint;
	bool FadingOut;
	struct WeaponBox
	{
		public int Slot;
		public int SlotPos;
	}
	List<WeaponBox> WeaponBoxes = [];
	int SelectedWeaponBox;
	int SelectedSlideDir;
	int SelectedBoxPosition;
	int SelectedSlot;
	BaseCombatWeapon? LastWeapon;
	[PanelAnimationVar(nameof(WeaponBoxOffset), "WeaponBoxOffset", "0")] protected float WeaponBoxOffset;

	public HudWeaponSelection(string elementName) : base(elementName, null, "HudWeaponSelection") {
		Panel Parent = clientMode.GetViewport();
		SetParent(Parent);
		FadingOut = false;
	}

	bool IsWeaponSelectable() => IsInSelectionMode();
	void SetSelectedWeapon(BaseCombatWeapon? weapon) => SelectedWeapon = weapon;
	void SetSelectedSlot(int slot) => SelectedSlot = slot;
	void SetSelectedSlideDir(int dir) => SelectedSlideDir = dir;

#if !GMOD_DLL
	public override void SetWeaponSelected() {
		base.SetWeaponSelected();
		switch (hud_fastswitch.GetInt()) {
			case HUDTYPE_FASTSWITCH:
			case HUDTYPE_CAROUSEL:
				ActivateFastswitchWeaponDisplay(GetSelectedWeapon());
				break;
			case HUDTYPE_PLUS:
				ActivateWeaponHighlight(GetSelectedWeapon());
				break;
		}
	}
#endif

	public override void OnWeaponPickup(BaseCombatWeapon weapon) {
		HudHistoryResource? hr = gHUD.FindElement("CHudHistoryResource") as HudHistoryResource;
		hr?.AddToHistory(weapon);
	}

	public override void OnThink() {
		float selectionTimeout = SELECTION_TIMEOUT_THRESHOLD;
		float selectionFadeoutTime = SELECTION_FADEOUT_TIME;

		if (hud_fastswitch.GetBool()) {
			selectionTimeout = FASTSWITCH_DISPLAY_TIMEOUT;
			selectionFadeoutTime = FASTSWITCH_FADEOUT_TIME;
		}

		if (gpGlobals.CurTime - SelectionTime > selectionTimeout) {
			if (!FadingOut) {
				clientMode.GetViewportAnimationController()?.StartAnimationSequence("FadeOutWeaponSelectionMenu");
				FadingOut = true;
			}
			else if (gpGlobals.CurTime - SelectionTime > selectionTimeout + selectionFadeoutTime)
				HideSelection();
		}
		else if (FadingOut) {
			clientMode.GetViewportAnimationController()?.StartAnimationSequence("OpenWeaponSelectionMenu");
			FadingOut = false;
		}
	}

	public bool ShouldDraw() {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
#if GMOD_DLL
		if (player == null || player.IsInAVehicle()) {
#else
		if (player == null) {
#endif
			if (IsInSelectionMode())
				HideSelection();
			return false;
		}

		bool bret = IHudElement.DefaultShouldDraw(this);
		if (!bret)
			return false;

		if (hud_fastswitch.GetBool() && gpGlobals.CurTime - SelectionTime < (FASTSWITCH_DISPLAY_TIMEOUT + FASTSWITCH_FADEOUT_TIME))
			return true;

		return SelectionVisible;
	}

	public void LevelInit() {
		SelectedWeaponBox = -1;
		SelectedSlideDir = 0;
		LastWeapon = null;
	}

	void ActivateFastswitchWeaponDisplay(BaseCombatWeapon selectedWeapon) {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		MakeReadyForUse();

		WeaponBoxes.Clear();
		SelectedWeaponBox = 0;

		int cWeapons = 0;
		int lastSelectedWeaponBox = -1;
		for (int i = 0; i < MAX_SELECTABLE_SLOTS; i++) {
			for (int slotPos = 0; slotPos < MAX_WEAPON_POSITIONS; slotPos++) {
				BaseCombatWeapon? weapon = GetWeaponInSlot(i, slotPos);
				if (weapon == null)
					continue;

				WeaponBox box = new() {
					Slot = i,
					SlotPos = slotPos
				};
				WeaponBoxes.Add(box);

				if (weapon == selectedWeapon)
					lastSelectedWeaponBox = cWeapons;

				if (weapon == LastWeapon)
					lastSelectedWeaponBox = cWeapons;

				cWeapons++;
			}
		}

		if (lastSelectedWeaponBox == -1)
			LastWeapon = null;

		float fstart, stop, time;
		if (LastWeapon == null || SelectedSlideDir == 0 || HorizWeaponSelectOffsetPoint != 0)
			LastWeapon = selectedWeapon;
		else {
			int numIcons = 0;
			int start = lastSelectedWeaponBox;

			for (int i = 0; i < cWeapons; i++) {
				if (start == SelectedWeaponBox)
					break;

				if (SelectedSlideDir < 0)
					start--;
				else
					start++;

				start = (start + cWeapons) % cWeapons;
				numIcons++;
			}

			fstart = numIcons * (LargeBoxWide + BoxGap);
			if (SelectedSlideDir < 0)
				fstart *= -1;
			stop = 0;

			time = numIcons * 0.20f;
			if (numIcons > 1)
				time *= 0.5f;

			HorizWeaponSelectOffsetPoint = fstart;
			clientMode.GetViewportAnimationController()?.RunAnimationCommand(this, "WeaponBoxOffset", stop, 0, time, Interpolators.Linear);

			Blur = 7.0f;
			clientMode.GetViewportAnimationController()?.RunAnimationCommand(this, "Blur", 0, time, 0.75f, Interpolators.Deaccel);
		}
	}

	void ActivateWeaponHighlight(BaseCombatWeapon selectedWeapon) {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		MakeReadyForUse();

		BaseCombatWeapon? weapon = GetWeaponInSlot(SelectedSlot, SelectedBoxPosition);
		if (weapon == null)
			return;

		clientMode.GetViewportAnimationController()?.StartAnimationSequence("WeaponHighlight");
	}

	float GetWeaponBoxAlpha(bool selected) {
		if (selected)
			return SelectionAlphaOverride;
		return SelectionAlphaOverride * (AlphaOverride / 255.0f);
	}

#if GMOD_DLL
	public override void Paint() {
		if (!ShouldDraw())
			return;

		BasePlayer? localPlayer = BasePlayer.GetLocalPlayer();
		if (localPlayer == null)
			return;

		BaseCombatWeapon? selectedWeapon = hud_fastswitch.GetInt() switch {
			HUDTYPE_FASTSWITCH or HUDTYPE_CAROUSEL => localPlayer.GetActiveWeapon(),
			_ => GetSelectedWeapon(),
		};

		if (selectedWeapon == null)
			return;

		int largeBoxWide = (int)LargeBoxWide;
		float largeBoxTall = LargeBoxTall;
		float percentageDone = 1.0f;
		Color selectedColor = new();
		for (int i = 0; i < 4; i++)
			selectedColor[i] = (byte)((SelectedBoxColor[i] - BoxColor[i]) * percentageDone + BoxColor[i]);

		if (hud_fastswitch.GetInt() != HUDTYPE_BUCKETS)
			return;

		int numSlots = MAX_WEAPON_SLOTS;
		for (int i = 0; i < MAX_WEAPONS; i++) {
			BaseCombatWeapon? weapon = localPlayer.GetWeapon(i);
			if (weapon != null && weapon.IsBaseCombatWeapon() && weapon.GetSlot() > numSlots - 1)
				numSlots = weapon.GetSlot() + 1;
		}
		if (numSlots > MAX_SELECTABLE_SLOTS)
			numSlots = MAX_SELECTABLE_SLOTS;

		int xpos = (GetWide() - (int)((BoxGap + SmallBoxSize) * (numSlots - 1) + largeBoxWide)) / 2;
		int activeSlot = selectedWeapon.GetSlot();

		for (int i = 0; i < numSlots; i++) {
			if (i == activeSlot) {
				bool drawBucketNumber = true;
				List<BaseCombatWeapon> weapons = GetWeaponsInSlot(i);

				int ypos = 0;
				for (int slotPos = 0; slotPos < weapons.Count; slotPos++) {
					if (GetWeaponInSlot(i, slotPos) == selectedWeapon) {
						ypos = (int)((UnselectedBoxSize + BoxGap) * slotPos);
						ypos = ypos <= GetTall() / 2 ? 0 : GetTall() / 2 - ypos;
						break;
					}
				}

				for (int slotPos = 0; slotPos < weapons.Count; slotPos++) {
					BaseCombatWeapon? weapon = GetWeaponInSlot(i, slotPos);
					if (weapon == null)
						continue;

					bool selected = weapon == selectedWeapon;
					float boxTall = selected ? largeBoxTall : UnselectedBoxSize;
					if (selected)
						DrawLargeWeaponBox(weapon, true, xpos, ypos, largeBoxWide, (int)boxTall, selectedColor, SelectionAlphaOverride, drawBucketNumber ? i + 1 : -1);
					else
						DrawLargeWeaponBox(weapon, false, xpos, ypos, largeBoxWide, (int)boxTall, BoxColor, AlphaOverride / 255.0f * SelectionAlphaOverride, drawBucketNumber ? i + 1 : -1);

					drawBucketNumber = false;
					ypos = (int)((int)boxTall + BoxGap + ypos);
				}

				xpos += largeBoxWide;
			}
			else {
				int smallBoxSize = (int)SmallBoxSize;
				if (GetFirstPos(i) == null)
					base.DrawBox(xpos, 0, smallBoxSize, smallBoxSize, EmptyBoxColor, AlphaOverride / 255.0f);
				else
					DrawBox(xpos, 0, smallBoxSize, smallBoxSize, BoxColor, AlphaOverride, i + 1);

				xpos = (int)(xpos + SmallBoxSize);
			}

			xpos = (int)(xpos + BoxGap);
		}
	}

	void DrawLargeWeaponBox(BaseCombatWeapon weapon, bool selected, int xpos, int ypos, int boxWide, int boxTall, Color selectedColor, float alpha, int number) {
		Color col = selected ? SelectedFgColor : GetFgColor();

		if (hud_fastswitch.GetInt() == HUDTYPE_BUCKETS) {
			DrawBox(xpos, ypos, boxWide, boxTall, selectedColor, alpha, number);

			// todo: lua hook (DrawWeaponSelection)
			if (selected) {
				col[3] = (byte)(alpha * (1.0f / 255.0f) * col[3]);
				HudTexture? spriteInactive = weapon.GetSpriteInactive();
				if (spriteInactive != null) {
					int x_offs = (boxWide - spriteInactive.Width()) / 2;
					int y_offs = (boxTall - spriteInactive.Height()) / 2;
					int x = xpos + x_offs;

					if (!weapon.CanBeSelected())
						col = new(255, 0, 0, col[3]);
					else {
						col[3] = (byte)alpha;
						weapon.GetSpriteActive()?.DrawSelf(x, ypos + y_offs, col);
					}

					spriteInactive.DrawSelf(x, ypos + y_offs, col);
				}
			}
		}

		if (hud_fastswitch.GetInt() == HUDTYPE_PLUS)
			return;

		col = TextColor;

		// todo: lua hook (language.GetPhrase)
		Span<char> text = stackalloc char[128];
		ReadOnlySpan<char> printName = weapon.GetPrintName();
		ReadOnlySpan<char> localized = localize.Find(printName);
		if (!localized.IsEmpty)
			localized.ClampedCopyTo(text);
		else
			printName.ClampedCopyTo(text);
		ReadOnlySpan<char> remaining = text.SliceNullTerminatedString();

		surface.DrawSetTextColor(col);
		surface.DrawSetTextFont(TextFont);

		int centerX = (int)(boxWide * 0.5f + xpos);
		surface.GetTextSize(TextFont, remaining, out _, out int textTall);
		int ty = boxTall - 8 + (ypos - textTall);

		while (true) {
			int newline = remaining.IndexOf('\n');
			ReadOnlySpan<char> line = newline < 0 ? remaining : remaining[..newline];

			surface.GetTextSize(TextFont, line, out int lineWide, out int lineTall);
			surface.DrawSetTextPos((int)(centerX - lineWide * 0.5f), ty);
			surface.DrawString(line);

			if (newline < 0)
				return;

			ty += lineTall;
			remaining = remaining[(newline + 1)..];
		}
	}
#else
	public override void Paint() {
		int width;
		int xpos;
		int ypos;

		if (!ShouldDraw())
			return;

		BasePlayer? localPlayer = BasePlayer.GetLocalPlayer();
		if (localPlayer == null)
			return;

		BaseCombatWeapon? selectedWeapon;
		selectedWeapon = hud_fastswitch.GetInt() switch {
			HUDTYPE_FASTSWITCH or HUDTYPE_CAROUSEL => localPlayer.GetActiveWeapon(),
			_ => GetSelectedWeapon(),
		};

		if (selectedWeapon == null)
			return;

		bool bPushedViewport = false;
		if (hud_fastswitch.GetInt() == HUDTYPE_FASTSWITCH || hud_fastswitch.GetInt() == HUDTYPE_PLUS) {
			using MatRenderContextPtr renderContext = new(materials);
			if (renderContext.GetRenderTarget() != null) {
				surface.PushFullscreenViewport();
				bPushedViewport = true;
			}
		}

		float percentageDone = 1.0f;
		int largeBoxWide = (int)(SmallBoxSize + ((LargeBoxWide - SmallBoxSize) * percentageDone));
		int largeBoxTall = (int)(SmallBoxSize + ((LargeBoxTall - SmallBoxSize) * percentageDone));
		Color selectedColor = new();
		for (int i = 0; i < 4; i++)
			selectedColor[i] = (byte)(BoxColor[i] + ((SelectedBoxColor[i] - BoxColor[i]) * percentageDone));

		switch (hud_fastswitch.GetInt()) {
			case HUDTYPE_CAROUSEL: {
					ypos = 0;
					if (SelectedWeaponBox == -1 || WeaponBoxes.Count <= 1)
						return;
					else if (WeaponBoxes.Count < MAX_CAROUSEL_SLOTS) {
						width = (int)((WeaponBoxes.Count - 1) * (LargeBoxWide + BoxGap) + LargeBoxWide);
						xpos = (GetWide() - width) / 2;
						for (int i = 0; i < WeaponBoxes.Count; i++) {
							BaseCombatWeapon? weapon = GetWeaponInSlot(WeaponBoxes[i].Slot, WeaponBoxes[i].SlotPos);
							if (weapon == null)
								break;

							byte alpha = (byte)GetWeaponBoxAlpha(i == SelectedWeaponBox);
							if (i == SelectedWeaponBox)
								DrawLargeWeaponBox(weapon, true, xpos, ypos, (int)LargeBoxWide, (int)LargeBoxTall, selectedColor, alpha, -1);
							else
								DrawLargeWeaponBox(weapon, false, xpos, ypos, (int)LargeBoxWide, (int)(LargeBoxTall / 1.5f), BoxColor, alpha, -1);

							xpos += (int)(LargeBoxWide + BoxGap);
						}
					}
					else {
						xpos = GetWide() / 2 + (int)HorizWeaponSelectOffsetPoint - largeBoxWide / 2;
						int i = SelectedWeaponBox;
						while (true) {
							BaseCombatWeapon? weapon = GetWeaponInSlot(WeaponBoxes[i].Slot, WeaponBoxes[i].SlotPos);
							if (weapon == null)
								break;

							byte alpha;
							if (i == SelectedWeaponBox && HorizWeaponSelectOffsetPoint == 0) {
								alpha = (byte)GetWeaponBoxAlpha(true);
								DrawLargeWeaponBox(weapon, true, xpos, ypos, largeBoxWide, largeBoxTall, selectedColor, alpha, -1);
							}
							else {
								alpha = (byte)GetWeaponBoxAlpha(false);
								DrawLargeWeaponBox(weapon, false, xpos, ypos, largeBoxWide, (int)(largeBoxTall / 1.5f), BoxColor, alpha, -1);
							}

							xpos += (int)(largeBoxWide + BoxGap);
							if (xpos >= GetWide())
								break;

							++i;
							if (i >= WeaponBoxes.Count)
								i = 0;
						}

						xpos = (int)(GetWide() / 2 + HorizWeaponSelectOffsetPoint - (3 * largeBoxWide / 2 + BoxGap));
						i = SelectedWeaponBox - 1;
						while (true) {
							if (i < 0)
								i = WeaponBoxes.Count - 1;

							BaseCombatWeapon? weapon = GetWeaponInSlot(WeaponBoxes[i].Slot, WeaponBoxes[i].SlotPos);
							if (weapon == null)
								break;

							byte alpha;
							if (i == SelectedWeaponBox && HorizWeaponSelectOffsetPoint == 0) {
								alpha = (byte)GetWeaponBoxAlpha(true);
								DrawLargeWeaponBox(weapon, true, xpos, ypos, largeBoxWide, largeBoxTall, selectedColor, alpha, -1);
							}
							else {
								alpha = (byte)GetWeaponBoxAlpha(false);
								DrawLargeWeaponBox(weapon, false, xpos, ypos, largeBoxWide, (int)(largeBoxTall / 1.5f), BoxColor, alpha, -1);
							}

							xpos -= (int)(largeBoxWide + BoxGap);
							if (xpos + largeBoxWide <= 0)
								break;

							--i;
						}
					}
				}
				break;
			case HUDTYPE_PLUS: {
					HudCrosshair.GetDrawPosition(out float fCenterX, out float fCenterY, out bool bBehindCamera);

					if (bBehindCamera)
						return;

					int screenCenterX = (int)fCenterX;
					int screenCenterY = (int)fCenterY - 15;

					int[] xModifiers = [0, 1, 0, -1, -1, 1];
					int[] yModifiers = [-1, 0, 1, 0, 1, 1];

					for (int i = 0; i < MAX_WEAPON_SLOTS; ++i) {
						int xPos = screenCenterX - (int)(MediumBoxWide / 2);
						int yPos = screenCenterY - (int)(MediumBoxTall / 2);

						int lastSlotPos = -1;
						for (int slotPos = 0; slotPos < MAX_WEAPON_POSITIONS; ++slotPos) {
							BaseCombatWeapon? weapon = GetWeaponInSlot(i, slotPos);
							if (weapon != null)
								lastSlotPos = slotPos;
						}

						for (int slotPos = 0; slotPos <= lastSlotPos; ++slotPos) {
							xPos += (int)(MediumBoxWide + 5) * xModifiers[i];
							yPos += (int)(MediumBoxTall + 5) * yModifiers[i];

							int boxWide = (int)MediumBoxWide;
							int boxTall = (int)MediumBoxTall;
							int x = xPos;
							int y = yPos;

							BaseCombatWeapon? weapon = GetWeaponInSlot(i, slotPos);
							bool bSelectedWeapon = false;
							if (i == SelectedSlot && slotPos == SelectedBoxPosition)
								bSelectedWeapon = true;

							DrawLargeWeaponBox(weapon, bSelectedWeapon, x, y, boxWide, boxTall, bSelectedWeapon ? selectedColor : BoxColor, (byte)GetWeaponBoxAlpha(bSelectedWeapon), -1);
						}
					}
				}
				break;
			case HUDTYPE_BUCKETS: {
					int numSlots = MAX_WEAPON_SLOTS;
					for (int i = MAX_WEAPON_SLOTS; i < MAX_SELECTABLE_SLOTS; i++)
						if (GetFirstPos(i) != null)
							numSlots = i + 1;

					width = (numSlots - 1) * (int)(SmallBoxSize + BoxGap) + largeBoxWide;
					xpos = (GetWide() - width) / 2;
					ypos = 0;

					int activeSlot = SelectedWeapon != null ? SelectedWeapon.GetSlot() : -1;

					for (int i = 0; i < numSlots; i++) {
						if (i == activeSlot) {
							bool drawBucketNumber = true;
							int lastPos = GetLastPosInSlot(i);

							for (int slotpos = 0; slotpos <= lastPos; slotpos++) {
								BaseCombatWeapon? weapon = GetWeaponInSlot(i, slotpos);
								if (weapon == null) {
									if (!hud_showemptyweaponslots.GetBool())
										continue;
									DrawBox(xpos, ypos, largeBoxWide, largeBoxTall, EmptyBoxColor, AlphaOverride, drawBucketNumber ? (i + 1) % 10 : -1);
								}
								else {
									bool bSelected = weapon == SelectedWeapon;
									DrawLargeWeaponBox(weapon, bSelected, xpos, ypos, largeBoxWide, largeBoxTall, bSelected ? selectedColor : BoxColor, (byte)GetWeaponBoxAlpha(bSelected), drawBucketNumber ? (i + 1) % 10 : -1);
								}

								ypos += (int)(largeBoxTall + BoxGap);
								drawBucketNumber = false;
							}

							xpos += largeBoxWide;
						}
						else {
							if (GetFirstPos(i) != null)
								DrawBox(xpos, ypos, (int)SmallBoxSize, (int)SmallBoxSize, BoxColor, AlphaOverride, (i + 1) % 10);
							else
								DrawBox(xpos, ypos, (int)SmallBoxSize, (int)SmallBoxSize, EmptyBoxColor, AlphaOverride, -1);

							xpos += (int)SmallBoxSize;
						}

						ypos = 0;
						xpos += (int)BoxGap;
					}
				}
				break;
			default:
				break;
		}

		if (bPushedViewport) {
			surface.PopFullscreenViewport();
		}
	}

	void DrawLargeWeaponBox(BaseCombatWeapon? weapon, bool bSelected, int xpos, int ypos, int boxWide, int boxTall, Color selectedColor, byte alpha, int number) {
		Color col = bSelected ? SelectedFgColor : GetFgColor();

		switch (hud_fastswitch.GetInt()) {
			case HUDTYPE_BUCKETS: {
					DrawBox(xpos, ypos, boxWide, boxTall, selectedColor, alpha, number);

					col[3] *= (byte)(alpha / 255.0f);
					if (weapon!.GetSpriteActive() != null) {
						int iconWidth = weapon.GetSpriteActive().Width();
						int iconHeight = weapon.GetSpriteActive().Height();
						int x_offs = (boxWide - iconWidth) / 2;
						int y_offs;

						if (bSelected && hud_fastswitch.GetInt() != 0)
							y_offs = (int)(boxTall / 1.5f - iconHeight) / 2;
						else
							y_offs = (boxTall - iconHeight) / 2;

						if (!weapon.CanBeSelected())
							col = new(255, 0, 0, col[3]);
						else if (bSelected) {
							col[3] = alpha;
							weapon.GetSpriteActive().DrawSelf(xpos + x_offs, ypos + y_offs, col);
						}

						weapon.GetSpriteInactive().DrawSelf(xpos + x_offs, ypos + y_offs, col);
					}
				}
				break;
			case HUDTYPE_PLUS:
			case HUDTYPE_CAROUSEL: {
					if (weapon == null) {
						if (bSelected)
							selectedColor.SetColor(255, 0, 0, 40);

						DrawBox(xpos, ypos, boxWide, boxTall, selectedColor, alpha, number);
						return;
					}
					else
						DrawBox(xpos, ypos, boxWide, boxTall, selectedColor, alpha, number);

					int iconWidth;
					int iconHeight;
					int x_offs;
					int y_offs;

					col[3] *= (byte)(alpha / 255.0f);

					if (weapon.GetSpriteInactive() != null) {
						iconWidth = weapon.GetSpriteInactive().Width();
						iconHeight = weapon.GetSpriteInactive().Height();

						x_offs = (boxWide - iconWidth) / 2;
						if (bSelected && HUDTYPE_CAROUSEL == hud_fastswitch.GetInt())
							y_offs = (int)(boxTall / 1.5f - iconHeight) / 2;
						else
							y_offs = (boxTall - iconHeight) / 2;

						if (!weapon.CanBeSelected())
							col = new(255, 0, 0, col[3]);

						weapon.GetSpriteInactive().DrawSelf(xpos + x_offs, ypos + y_offs, iconWidth, iconHeight, col);
					}

					if (bSelected && weapon.GetSpriteActive() != null) {
						iconWidth = weapon.GetSpriteActive().Width();
						iconHeight = weapon.GetSpriteActive().Height();

						x_offs = (boxWide - iconWidth) / 2;
						if (HUDTYPE_CAROUSEL == hud_fastswitch.GetInt())
							y_offs = (int)(boxTall / 1.5f - iconHeight) / 2;
						else
							y_offs = (boxTall - iconHeight) / 2;

						col[3] = 255;
						for (float fl = Blur; fl > 0.0f; fl -= 1.0f) {
							if (fl >= 1.0f)
								weapon.GetSpriteActive().DrawSelf(xpos + x_offs, ypos + y_offs, col);
							else {
								col[3] *= (byte)fl;
								weapon.GetSpriteActive().DrawSelf(xpos + x_offs, ypos + y_offs, col);
							}
						}
					}
				}
				break;
			default:
				break;
		}

		if (HUDTYPE_PLUS == hud_fastswitch.GetInt())
			return;

		col = TextColor;
		FileWeaponInfo weaponInfo = weapon!.GetWpnData();

		if (bSelected) {
			Span<char> text = stackalloc char[128];
			ReadOnlySpan<char> tempString = localize.Find(weaponInfo.PrintName);

			if (!tempString.IsEmpty)
				tempString.ClampedCopyTo(text);
			else
				strcpy(text, weaponInfo.PrintName);

			surface.DrawSetTextColor(col);
			surface.DrawSetTextFont(TextFont);

			int slen = 0, charCount = 0, maxslen = 0;
			int firstslen = 0;
			for (ReadOnlySpan<char> pch = text; pch[0] != '\0'; pch = pch[1..]) {
				if (pch[0] == '\n') {
					if (slen > maxslen)
						maxslen = slen;

					if (firstslen == 0)
						firstslen = slen;

					slen = 0;
				}
				else if (pch[0] != '\r') {
					slen += surface.GetCharacterWidth(TextFont, pch[0]);
					charCount++;
				}
			}

			if (slen > maxslen)
				maxslen = slen;

			if (firstslen == 0)
				firstslen = maxslen;

			int tx = xpos + (int)((LargeBoxWide - firstslen) / 2);
			int ty = ypos + (int)TextYPos;
			surface.DrawSetTextPos(tx, ty);
			charCount *= (int)TextScan;
			for (ReadOnlySpan<char> pch = text; charCount > 0; pch = pch[1..]) {
				if (pch[0] == '\n')
					surface.DrawSetTextPos(xpos + ((boxWide - slen) / 2), ty + (int)(surface.GetFontTall(TextFont) * 1.1f));
				else if (pch[0] != '\r') {
					surface.DrawChar(pch[0]);
					charCount--;
				}
			}
		}
	}

#endif

	void DrawBox(int x, int y, int wide, int tall, Color color, float normalizedAlpha, int number) {
		base.DrawBox(x, y, wide, tall, color, normalizedAlpha / 255.0f);

		if (number >= 0) {
			Color numberColor = NumberColor;
			numberColor[3] = (byte)(numberColor[3] * normalizedAlpha / 255.0f);
			Surface.DrawSetTextColor(numberColor);
			Surface.DrawSetTextFont(NumberFont);
#if GMOD_DLL
			Surface.DrawSetTextPos((int)(x + SelectionNumberXPos), (int)(y + SelectionNumberYPos));
			if (number < 10)
				Surface.DrawChar((char)('0' + number));
			else {
				Span<char> unicode = stackalloc char[3];
				sprintf(unicode, "%d").D(number);
				Surface.DrawString(unicode);
			}
#else
			Span<char> unicode = stackalloc char[2];
			sprintf(unicode, "%d").D(number);
			Surface.DrawSetTextPos(x + (int)SelectionNumberXPos, y + (int)SelectionNumberYPos);
			Surface.DrawString(unicode);
#endif
		}
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetPaintBackgroundEnabled(false);

		GetPos(out int x, out int y);
		GetHudSize(out int screenWide, out int screenTall);

		if (hud_fastswitch.GetInt() == HUDTYPE_CAROUSEL) {
			int width = (int)(MAX_CAROUSEL_SLOTS * LargeBoxWide + (MAX_CAROUSEL_SLOTS - 1) * BoxGap);
			SetBounds((screenWide - width) / 2, y, width, screenTall - y);
		}
		else
			SetBounds(x, y, screenWide - x, screenTall - y);
	}

	public override void OpenSelection() {
		Assert(!IsInSelectionMode());

		base.OpenSelection();
		clientMode.GetViewportAnimationController()?.StartAnimationSequence("OpenWeaponSelectionMenu");
		SelectedBoxPosition = 0;
		SelectedSlot = -1;
	}

	public override void HideSelection() {
		base.HideSelection();
		clientMode.GetViewportAnimationController()?.StartAnimationSequence("CloseWeaponSelectionMenu");
		FadingOut = false;
	}

#if GMOD_DLL
	BaseCombatWeapon? FindNextWeaponInWeaponSelection(int currentSlot, int currentPosition) {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return null;

		List<BaseCombatWeapon> weapons = GetWeaponsInSlot(currentSlot);
		if (currentPosition + 1 >= 0 && currentPosition + 1 < weapons.Count && weapons[currentPosition + 1] != null)
			return weapons[currentPosition + 1];

		while (++currentSlot < MAX_SELECTABLE_SLOTS) {
			weapons = GetWeaponsInSlot(currentSlot);
			if (weapons.Count > 0)
				return weapons[0];
		}

		return null;
	}

	BaseCombatWeapon? FindPrevWeaponInWeaponSelection(int currentSlot, int currentPosition) {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return null;

		List<BaseCombatWeapon> weapons = GetWeaponsInSlot(currentSlot);
		if (weapons.Count < currentPosition)
			currentPosition = weapons.Count;

		if (currentPosition - 1 >= 0 && currentPosition - 1 < weapons.Count && weapons[currentPosition - 1] != null)
			return weapons[currentPosition - 1];

		while (--currentSlot >= 0) {
			weapons = GetWeaponsInSlot(currentSlot);
			if (weapons.Count > 0)
				return weapons[^1];
		}

		return null;
	}
#else
	BaseCombatWeapon? FindNextWeaponInWeaponSelection(int currentSlot, int currentPosition) {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return null;

		BaseCombatWeapon? nextWeapon = null;
		int lowestNextSlot = MAX_SELECTABLE_SLOTS;
		int lowestNextPosition = MAX_WEAPON_POSITIONS;

		for (int i = 0; i < MAX_WEAPONS; i++) {
			BaseCombatWeapon? weapon = player.GetWeapon(i);
			if (weapon == null)
				continue;

			if (CanBeSelectedInHUD(weapon)) {
				int weaponSlot = weapon.GetSlot();
				int weaponPosition = GetWeaponPosition(weapon);

				if (weaponSlot > currentSlot || (weaponSlot == currentSlot && weaponPosition > currentPosition)) {
					if (weaponSlot < lowestNextSlot || (weaponSlot == lowestNextSlot && weaponPosition < lowestNextPosition)) {
						lowestNextSlot = weaponSlot;
						lowestNextPosition = weaponPosition;
						nextWeapon = weapon;
					}
				}
			}
		}

		return nextWeapon;
	}

	BaseCombatWeapon? FindPrevWeaponInWeaponSelection(int currentSlot, int currentPosition) {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return null;

		BaseCombatWeapon? prevWeapon = null;
		int highestPrevSlot = -1;
		int highestPrevPosition = -1;

		for (int i = 0; i < MAX_WEAPONS; i++) {
			BaseCombatWeapon? weapon = player.GetWeapon(i);
			if (weapon == null)
				continue;

			if (CanBeSelectedInHUD(weapon)) {
				int weaponSlot = weapon.GetSlot();
				int weaponPosition = GetWeaponPosition(weapon);

				if (weaponSlot < currentSlot || (weaponSlot == currentSlot && weaponPosition < currentPosition)) {
					if (weaponSlot > highestPrevSlot || (weaponSlot == highestPrevSlot && weaponPosition > highestPrevPosition)) {
						highestPrevSlot = weaponSlot;
						highestPrevPosition = weaponPosition;
						prevWeapon = weapon;
					}
				}
			}
		}

		return prevWeapon;
	}
#endif

	public override void CycleToNextWeapon() {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
#if GMOD_DLL
		if (player != null && player.IsInAVehicle())
			return;
#endif
		if (player == null)
			return;

		LastWeapon = player.GetActiveWeapon();

		BaseCombatWeapon? nextWeapon;
		if (IsInSelectionMode()) {
			BaseCombatWeapon? weapon = GetSelectedWeapon();
			if (weapon == null)
				return;
#if GMOD_DLL
			if (!weapon.IsBaseCombatWeapon())
				return;
#endif

			nextWeapon = FindNextWeaponInWeaponSelection(weapon.GetSlot(), GetWeaponPosition(weapon));
		}
		else {
			nextWeapon = player.GetActiveWeapon();
#if GMOD_DLL
			if (nextWeapon != null && nextWeapon.IsBaseCombatWeapon())
#else
			if (nextWeapon != null)
#endif
				nextWeapon = FindNextWeaponInWeaponSelection(nextWeapon.GetSlot(), GetWeaponPosition(nextWeapon));
		}

#if GMOD_DLL
		nextWeapon ??= FindNextWeaponInWeaponSelection(0, -1);
#else
		nextWeapon ??= FindNextWeaponInWeaponSelection(-1, -1);
#endif
		if (nextWeapon != null) {
			SetSelectedWeapon(nextWeapon);
			SetSelectedSlideDir(1);

#if GMOD_DLL
			if (!IsInSelectionMode())
#else
			if (hud_fastswitch.GetInt() != 0)
				SelectWeapon();
			else if (!IsInSelectionMode())
#endif
				OpenSelection();

			player.EmitSound("Player.WeaponSelectionMoveSlot");
		}
	}

	public override void CycleToPrevWeapon() {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
#if GMOD_DLL
		if (player != null && player.IsInAVehicle())
			return;
#endif
		if (player == null)
			return;

		LastWeapon = player.GetActiveWeapon();

		BaseCombatWeapon? prevWeapon;
		if (IsInSelectionMode()) {
			BaseCombatWeapon? weapon = GetSelectedWeapon();
			if (weapon == null)
				return;
#if GMOD_DLL
			if (!weapon.IsBaseCombatWeapon())
				return;
#endif

			prevWeapon = FindPrevWeaponInWeaponSelection(weapon.GetSlot(), GetWeaponPosition(weapon));
		}
		else {
			prevWeapon = player.GetActiveWeapon();
#if GMOD_DLL
			if (prevWeapon != null && prevWeapon.IsBaseCombatWeapon())
#else
			if (prevWeapon != null)
#endif
				prevWeapon = FindPrevWeaponInWeaponSelection(prevWeapon.GetSlot(), GetWeaponPosition(prevWeapon));
		}

#if GMOD_DLL
		if (prevWeapon == null) {
			int highestSlot = -9999;
			for (int i = 0; i < MAX_WEAPONS; i++) {
				BaseCombatWeapon? weapon = player.GetWeapon(i);
				if (weapon != null && weapon.GetSlot() > highestSlot)
					highestSlot = weapon.GetSlot();
				if (highestSlot > 9)
					break;
			}

			prevWeapon = FindPrevWeaponInWeaponSelection(highestSlot, 9999);
		}
#else
		prevWeapon ??= FindPrevWeaponInWeaponSelection(MAX_SELECTABLE_SLOTS, MAX_WEAPON_POSITIONS);
#endif

		if (prevWeapon != null) {
			SetSelectedWeapon(prevWeapon);
			SetSelectedSlideDir(-1);

#if GMOD_DLL
			if (!IsInSelectionMode())
#else
			if (hud_fastswitch.GetInt() != 0)
				SelectWeapon();
			else if (!IsInSelectionMode())
#endif
				OpenSelection();

			player.EmitSound("Player.WeaponSelectionMoveSlot");
		}
	}

	int GetLastPosInSlot(int slot) => GetWeaponsInSlot(slot).Count - 1;

	BaseCombatWeapon? GetWeaponInSlot(int slot, int slotPos) {
		List<BaseCombatWeapon> weapons = GetWeaponsInSlot(slot);
		return slotPos >= 0 && slotPos < weapons.Count ? weapons[slotPos] : null;
	}

	void FastWeaponSwitch(int weaponSlot) {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
#if GMOD_DLL
		if (player != null && player.IsInAVehicle())
			return;
#endif
		if (player == null)
			return;

		LastWeapon = null;

		int position = -1;
		BaseCombatWeapon? activeWeapon = player.GetActiveWeapon();
#if GMOD_DLL
		if (activeWeapon != null && activeWeapon.IsBaseCombatWeapon() && activeWeapon.GetSlot() == weaponSlot)
#else
		if (activeWeapon != null && activeWeapon.GetSlot() == weaponSlot)
#endif
			position = GetWeaponPosition(activeWeapon);

		BaseCombatWeapon? nextWeapon = FindNextWeaponInWeaponSelection(weaponSlot, position);

#if GMOD_DLL
		if (nextWeapon == null || !nextWeapon.IsBaseCombatWeapon() || nextWeapon.GetSlot() != weaponSlot)
#else
		if (nextWeapon == null || nextWeapon.GetSlot() != weaponSlot)
#endif
			nextWeapon = FindNextWeaponInWeaponSelection(weaponSlot, -1);

#if GMOD_DLL
		if (nextWeapon != null && nextWeapon != activeWeapon && nextWeapon.IsBaseCombatWeapon() && nextWeapon.GetSlot() == weaponSlot)
#else
		if (nextWeapon != null && nextWeapon != activeWeapon && nextWeapon.GetSlot() == weaponSlot)
#endif
			input.MakeWeaponSelection(nextWeapon);
		else if (nextWeapon != activeWeapon) {
			player.EmitSound("Player.DenyWeaponSelection");
		}

		if (HUDTYPE_CAROUSEL != hud_fastswitch.GetInt())
			SelectionTime = 0.0f;
	}

	void PlusTypeFastWeaponSwitch(int weaponSlot) {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		LastWeapon = null;
		int newSlot = SelectedSlot;

		if (-1 == SelectedSlot || ((SelectedSlot ^ weaponSlot) & 1) != 0) {
			SelectedBoxPosition = 0;
			SelectedSlot = weaponSlot;
		}
		else {
			int inc = 1;
			if (SelectedSlot != weaponSlot) {
				inc = -1;
				if (0 == SelectedBoxPosition) {
					newSlot = (SelectedSlot + 2) % 4;
					inc = 0;
				}
			}

			int lastSlotPos = -1;
			for (int slotPos = 0; slotPos < MAX_WEAPON_POSITIONS; ++slotPos) {
				BaseCombatWeapon? weapon = GetWeaponInSlot(newSlot, slotPos);
				if (weapon != null)
					lastSlotPos = slotPos;
			}

			if (SelectedBoxPosition + inc <= lastSlotPos) {
				SelectedBoxPosition += inc;
				SelectedSlot = newSlot;
			}
			else {
				player.EmitSound("Player.DenyWeaponSelection");
				return;
			}
		}

		bool weaponSelected = false;
		BaseCombatWeapon? activeWeapon = player.GetActiveWeapon();
		BaseCombatWeapon? pweapon = GetWeaponInSlot(SelectedSlot, SelectedBoxPosition);

		if (pweapon != null && pweapon != activeWeapon) {
			input.MakeWeaponSelection(pweapon);
			SetSelectedWeapon(pweapon);
			weaponSelected = true;
		}

		if (!weaponSelected)
			SetSelectedWeapon(activeWeapon);
	}

	public override void SelectWeaponSlot(int slot) {
#if GMOD_DLL
		BasePlayer? vehiclePlayer = BasePlayer.GetLocalPlayer();
		if (vehiclePlayer != null && vehiclePlayer.IsInAVehicle())
			return;
#endif
		--slot;

		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

#if GMOD_DLL
		if ((uint)slot >= MAX_SELECTABLE_SLOTS)
			return;

		if (!player.IsAllowedToSwitchWeapons())
			return;
#else
		if (slot >= MAX_SELECTABLE_SLOTS)
			return;

		// if (!player.IsAllowToSwitchWeapons()) todo
		// 	return;
#endif

		switch (hud_fastswitch.GetInt()) {
			case HUDTYPE_FASTSWITCH:
			case HUDTYPE_CAROUSEL: {
					FastWeaponSwitch(slot);
					return;
				}
#if !GMOD_DLL
			case HUDTYPE_PLUS: {
					if (!IsInSelectionMode())
						OpenSelection();

					PlusTypeFastWeaponSwitch(slot);
					ActivateWeaponHighlight(GetSelectedWeapon()!);
				}
				break;
#endif
			case HUDTYPE_BUCKETS: {
#if GMOD_DLL
					BaseCombatWeapon? activeWeapon = GetSelectedWeapon();
					if (IsInSelectionMode() && activeWeapon != null)
						activeWeapon = FindNextWeaponInWeaponSelection(activeWeapon.GetSlot(), GetWeaponPosition(activeWeapon));

					if (activeWeapon == null || activeWeapon.GetSlot() != slot)
						activeWeapon = GetNextActivePos(slot, 0);
#else
					int slotPos = 0;
					BaseCombatWeapon? activeWeapon = GetSelectedWeapon();

					if (IsInSelectionMode() && activeWeapon != null && activeWeapon.GetSlot() == slot)
						slotPos = GetWeaponPosition(activeWeapon) + 1;

					activeWeapon = GetNextActivePos(slot, slotPos);
					activeWeapon ??= GetNextActivePos(slot, 0);
#endif

					if (activeWeapon != null) {
						if (!IsInSelectionMode())
							OpenSelection();

						SetSelectedWeapon(activeWeapon);
						SetSelectedSlideDir(0);
					}
				}
				break;
			default:
				break;
		}

		player.EmitSound("Player.WeaponSelectionMoveSlot");
	}
}
