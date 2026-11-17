using Game.Client.HUD;
using Game.Shared;

using Source;
using Source.Common.Bitbuffers;
using Source.Common.GUI;
using Source.GUI.Controls;

using System.Numerics;

namespace Game.Client.HL2;

[DeclareHudElement(Name = "CHudBattery")]
public class HudBattery : HudNumericDisplay, IHudElement
{
	int Bat;
	int NewBat;

	public HudBattery(string? panelName) : base(null, "HudSuit") {
		ElementName = panelName;
#if GMOD_DLL
		((IHudElement)this).SetHiddenBits(HideHudBits.Health | HideHudBits.PlayerDead | HideHudBits.NeedSuit);
#else
		((IHudElement)this).SetHiddenBits(HideHudBits.Health | HideHudBits.NeedSuit);
#endif
	}

	public void Init() {
		IHudElement.HookMessage("Battery", Battery);
		Reset();
		Bat = -1;
		NewBat = 0;
	}

	public bool ShouldDraw() {
#if GMOD_DLL
		if (NewBat == 0)
			return false;
#endif
		bool needsDraw = Bat != NewBat || (GetAlpha() > 0);
		return needsDraw && IHudElement.DefaultShouldDraw(this);
	}

	public void VidInit() {
		Reset();
	}

	public override void Reset() {
		ReadOnlySpan<char> tempString = Localize.Find("#Valve_Hud_SUIT");

		if (!tempString.IsEmpty)
			SetLabelText(tempString);
		else
			SetLabelText("SUIT");

		SetDisplayValue(Bat);
	}

	public override void OnThink() {
		if (Bat == NewBat)
			return;

		if (NewBat == 0) {
			clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitPowerZero");
		}
		else if (NewBat < Bat) {
			clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitDamageTaken");

			if (NewBat < 20)
				clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitArmorLow");
		}
		else {
			if (Bat == -1 || Bat == 0 || NewBat >= 20)
				clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitPowerIncreasedAbove20");
			else
				clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitPowerIncreasedBelow20");
		}

#if GMOD_DLL
		if (NewBat < 1000)
			clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitPowerBelow999");
		else
			clientMode.GetViewportAnimationController()!.StartAnimationSequence("SuitPowerOver999");
#endif

		Bat = NewBat;

		SetDisplayValue(Bat);
	}

	private void Battery(bf_read msg) {
		NewBat = msg.ReadShort();
	}
}
