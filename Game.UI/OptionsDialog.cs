using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.GUI.Controls;

namespace Game.UI;

public class OptionsDialog : PropertyDialog
{
	readonly ModInfo ModInfo = Singleton<ModInfo>();

	OptionsSubAudio? OptionsSubAudio;
	OptionsSubVideo? OptionsSubVideo;
	PropertyPage? OptionsSubMultiplayer;

	public OptionsDialog(Panel? parent) : base(parent, "OptionsDialog") {
		SetDeleteSelfOnClose(true);
		SetBounds(0, 0, 512, 406);
		SetSizeable(false);

		SetTitle("#GameUI_Options", true);

#if GMOD_DLL // gmod's page order is different
		OptionsSubMultiplayer = new OptionsSubMultiplayer(this, "OptionsSubMultiplayer");
		AddPage(OptionsSubMultiplayer, "#GameUI_Game");
		AddPage(new OptionsSubKeyboard(this, null), "#GameUI_Keyboard");
		AddPage(new OptionsSubMouse(this, null), "#GameUI_Mouse");
		OptionsSubAudio = new(this, null);
		AddPage(OptionsSubAudio, "#GameUI_Audio");
		OptionsSubVideo = new(this, null);
		AddPage(OptionsSubVideo, "#GameUI_Video");
		AddPage(new OptionsSubVoice(this, null), "#GameUI_Voice");
		// AddPage(new OptionsSubLegal(this, null), "#GameUI_Legal");
#else
		if (IsWindows) {
			ConVarRef hap_HasDevice = new("hap_HasDevice");
			hap_HasDevice.Init("hap_HasDevice", true);
			// if (hap_HasDevice.GetBool())
				// AddPage(new OptionsSubHaptics(this), "#GameUI_Haptics_TabTitle");
		}

		// if (ModInfo.IsSinglePlayerOnly() && !ModInfo.NoDifficulty())
			// AddPage(new OptionsSubDifficulty(this), "#GameUI_Difficulty");

		// if (ModInfo.HasPortals())
			// AddPage(new OptionsSubPortal(this), "#GameUI_Portal");

		AddPage(new OptionsSubKeyboard(this, null), "#GameUI_Keyboard");
		AddPage(new OptionsSubMouse(this, null), "#GameUI_Mouse");

		OptionsSubAudio = new(this, null);
		AddPage(OptionsSubAudio, "#GameUI_Audio");
		OptionsSubVideo = new(this, null);
		AddPage(OptionsSubVideo, "#GameUI_Video");

		if (!ModInfo.IsSinglePlayerOnly())
			AddPage(new OptionsSubVoice(this, null), "#GameUI_Voice");

		if ((ModInfo.IsMultiPlayerOnly() && !ModInfo.IsSinglePlayerOnly()) ||
				(!ModInfo.IsMultiPlayerOnly() && !ModInfo.IsSinglePlayerOnly())) {
			OptionsSubMultiplayer = new OptionsSubMultiplayer(this, "OptionsSubMultiplayer");
			AddPage(OptionsSubMultiplayer, "#GameUI_Multiplayer");
		}
#endif

		SetApplyButtonVisible(true);
		GetPropertySheet().SetTabWidth(84);
	}

	public override void Activate() {
		base.Activate();
		EnableApplyButton(false);
	}

	public void Run() {
		SetTitle("#GameUI_Options", true);
		Activate();
	}

	static readonly KeyValues KV_GameUIHidden = new("GameUIHidden");
	public void OnGameUIHidden() {
		for (int i = 0; i < GetChildCount(); i++) {
			Panel child = GetChild(i);
			if (child != null && child.IsVisible())
				PostMessage(child, KV_GameUIHidden);
		}
	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		if (message.Name == "GameUIHidden") {
			OnGameUIHidden();
			return;
		}

		base.OnMessage(message, sender);
	}
}
