using Game.Client.HUD;
using Game.Shared;

using Source;
using Source.Common.Commands;
using Source.Common.GUI;
using Source.GUI.Controls;

namespace Game.Client.HL2;

// [DeclareHudElement(Name = "CHudSuitPower")] // FIXME: Animations are not working here? just sits at the topleft of the screen :(
public class HudSuitPower : EditableHudElement, IHudElement
{
	[PanelAnimationVar("AuxPowerColor", "255 0 0 255", "Color")] protected Color AuxPowerColor;
	[PanelAnimationVar("AuxPowerDisabledAlpha", "70")] protected int AuxPowerDisabledAlpha;
	[PanelAnimationVarAliasType("BarInsetX", "8", "proportional_float")] protected float BarInsetX;
	[PanelAnimationVarAliasType("BarInsetY", "8", "proportional_float")] protected float BarInsetY;
	[PanelAnimationVarAliasType("BarWidth", "80", "proportional_float")] protected float BarWidth;
	[PanelAnimationVarAliasType("BarHeight", "10", "proportional_float")] protected float BarHeight;
	[PanelAnimationVarAliasType("BarChunkWidth", "10", "proportional_float")] protected float BarChunkWidth;
	[PanelAnimationVarAliasType("BarChunkGap", "2", "proportional_float")] protected float BarChunkGap;
	[PanelAnimationVar("TextFont", "Default", "HFont")] protected IFont TextFont;
	[PanelAnimationVarAliasType("text_xpos", "8", "proportional_float")] protected float text_xpos;
	[PanelAnimationVarAliasType("text_ypos", "20", "proportional_float")] protected float text_ypos;
	[PanelAnimationVarAliasType("text2_xpos", "8", "proportional_float")] protected float text2_xpos;
	[PanelAnimationVarAliasType("text2_ypos", "40", "proportional_float")] protected float text2_ypos;
	[PanelAnimationVarAliasType("text2_gap", "10", "proportional_float")] protected float text2_gap;
	float SuitPower;
	int SuitPowerLow;
	int ActiveSuitDevices;

	const int SUIT_POWER_INIT = -1;

	public HudSuitPower(string? panelName) : base("HudSuitPower", panelName) {
		SetParent(clientMode.GetViewport());
		((IHudElement)this).SetHiddenBits(HideHudBits.Health | HideHudBits.NeedSuit | HideHudBits.PlayerDead);
	}

	public override void Init() {
		SuitPower = SUIT_POWER_INIT;
		SuitPowerLow = -1;
		ActiveSuitDevices = 0;
	}

	public void Reset() => Init();

#if GMOD_DLL
	static ConVarRef? gmod_suit;
#endif

	public bool ShouldDraw() {
		C_BaseHLPlayer? player = (C_BaseHLPlayer?)BasePlayer.GetLocalPlayer();
		if (player == null)
			return false;

#if GMOD_DLL
		gmod_suit ??= new("gmod_suit");
		if (gmod_suit.Value.GetInt() == 0)
			return false;
#endif

		bool needsDraw = (player.HL2Local.SuitPower != SuitPower) || AuxPowerColor.A > 0;
		return needsDraw && IHudElement.DefaultShouldDraw(this);
	}

	public override void OnThink() {
		C_BaseHLPlayer? player = (C_BaseHLPlayer?)BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		float currentPower = player.HL2Local.SuitPower;
		if (currentPower == SuitPower)
			return;

		if (currentPower >= 100.0f && SuitPower < 100.0f)
			clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitAuxPowerMax");
		else if (currentPower < 100.0f && (SuitPower >= 100.0f || SuitPower == SUIT_POWER_INIT))
			clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitAuxPowerNotMax");

		bool flashlightActive = player.IsFlashlightActive();
		bool sprintActive = player.IsSprintingBits();
		bool breatherActive = player.IsBreatherActive();
		int activeDevices = (flashlightActive ? 1 : 0) + (sprintActive ? 1 : 0) + (breatherActive ? 1 : 0);

		if (activeDevices != ActiveSuitDevices) {
			ActiveSuitDevices = activeDevices;

			switch (activeDevices) {
				case 3:
					clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitAuxPowerThreeItemsActive");
					break;
				case 2:
					clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitAuxPowerTwoItemsActive");
					break;
				case 1:
					clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitAuxPowerOneItemActive");
					break;
				case 0:
					clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitAuxPowerNoItemsActive");
					break;
			}
		}

		SuitPower = currentPower;
	}

	public override void Paint() {
		C_BaseHLPlayer? player = (C_BaseHLPlayer?)BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		int chunkCount = (int)(BarWidth / (BarChunkWidth + BarChunkGap));
		int enabledChunks = (int)(chunkCount * (SuitPower * 1.0f / 100.0f) + 0.5f);

		int lowPower = 0;
		if (enabledChunks <= (chunkCount / 4))
			lowPower = 1;

		if (SuitPowerLow != lowPower) {
			if (ActiveSuitDevices != 0 || SuitPower < 100.0f) {
				if (lowPower != 0)
					clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitAuxPowerDecreasedBelow25");
				else
					clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitAuxPowerIncreasedAbove25");

				SuitPowerLow = lowPower;
			}
		}

		surface.DrawSetColor(AuxPowerColor);

		int xpos = (int)BarInsetX, ypos = (int)BarInsetY;
		for (int i = 0; i < enabledChunks; i++) {
			surface.DrawFilledRect(xpos, ypos, (int)(xpos + BarChunkWidth), (int)(ypos + BarHeight));
			xpos += (int)(BarChunkWidth + BarChunkGap);
		}

		surface.DrawSetColor(AuxPowerColor with { A = (byte)AuxPowerDisabledAlpha });

		for (int i = enabledChunks; i < chunkCount; i++) {
			surface.DrawFilledRect(xpos, ypos, (int)(xpos + BarChunkWidth), (int)(ypos + BarHeight));
			xpos += (int)(BarChunkWidth + BarChunkGap);
		}

		surface.DrawSetTextFont(TextFont);
		surface.DrawSetTextColor(AuxPowerColor);
		surface.DrawSetTextPos((int)text_xpos, (int)text_ypos);

		ReadOnlySpan<char> tempString = localize.Find("#Valve_Hud_AUX_POWER");

		if (!tempString.IsEmpty)
			surface.DrawPrintText(tempString);
		else
			surface.DrawPrintText("AUX POWER");

		if (ActiveSuitDevices != 0) {
			ypos = (int)text2_ypos;

			if (player.IsBreatherActive()) {
				tempString = localize.Find("#Valve_Hud_OXYGEN");

				surface.DrawSetTextPos((int)text2_xpos, ypos);

				if (!tempString.IsEmpty)
					surface.DrawPrintText(tempString);
				else
					surface.DrawPrintText("OXYGEN");

				ypos += (int)text2_gap;
			}

			if (player.IsFlashlightActive()) {
				tempString = localize.Find("#Valve_Hud_FLASHLIGHT");

				surface.DrawSetTextPos((int)text2_xpos, ypos);

				if (!tempString.IsEmpty)
					surface.DrawPrintText(tempString);
				else
					surface.DrawPrintText("FLASHLIGHT");

				ypos += (int)text2_gap;
			}

			if (player.IsSprintingBits()) {
				tempString = localize.Find("#Valve_Hud_SPRINT");

				surface.DrawSetTextPos((int)text2_xpos, ypos);

				if (!tempString.IsEmpty)
					surface.DrawPrintText(tempString);
				else
					surface.DrawPrintText("SPRINT");
				ypos += (int)text2_gap;
			}
		}
	}
}
