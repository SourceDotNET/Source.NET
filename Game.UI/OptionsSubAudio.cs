using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.GUI.Controls;

namespace Game.UI;

enum SoundQuality
{
	Low,
	Medium,
	High
}

public class OptionsSubAudio : PropertyPage
{
	ComboBox SpeakerSetupCombo;
	ComboBox SoundQualityCombo;
	CvarSlider SFXSlider;
	CvarSlider MusicSlider;
	ComboBox CloseCaptionCombo;
	bool RequireRestart;
	ComboBox SpokenLanguageCombo;
	// ELanguage CurrentAudioLanguage;
	// char[] UpdatedAudioLanguage;
	CvarToggleCheckButton SoundMuteLoseFocusCheckButton;

	public OptionsSubAudio(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		SFXSlider = new(this, "SFXSlider", "#GameUI_SoundEffectVolume", 0.0f, 1.0f, "volume");
		MusicSlider = new(this, "MusicSlider", "#GameUI_MusicVolume", 0.0f, 1.0f, "Snd_MusicVolume");

		CloseCaptionCombo = new(this, "CloseCaptionCheck", 6, false);
		CloseCaptionCombo.AddItem("#GameUI_NoClosedCaptions", null);
		CloseCaptionCombo.AddItem("#GameUI_SubtitlesAndSoundEffects", null);
		CloseCaptionCombo.AddItem("#GameUI_Subtitles", null);

		SoundQualityCombo = new(this, "SoundQuality", 6, false);
		SoundQualityCombo.AddItem("#GameUI_High", null);
		SoundQualityCombo.AddItem("#GameUI_Medium", null);
		SoundQualityCombo.AddItem("#GameUI_Low", null);

		SpeakerSetupCombo = new(this, "SpeakerSetup", 6, false);
		if (!IsPosix)
			SpeakerSetupCombo.AddItem("#GameUI_Headphones", null);
		SpeakerSetupCombo.AddItem("#GameUI_2Speakers", null);
		if (!IsPosix) {
			SpeakerSetupCombo.AddItem("#GameUI_4Speakers", null);
			SpeakerSetupCombo.AddItem("#GameUI_5Speakers", null);
			SpeakerSetupCombo.AddItem("#GameUI_7Speakers", null);
		}
		SpokenLanguageCombo = new(this, "AudioSpokenLanguage", 6, false);

		SoundMuteLoseFocusCheckButton = new(this, "snd_mute_losefocus", "#GameUI_SndMuteLoseFocus", "snd_mute_losefocus");

		LoadControlSettings("resource/OptionsSubAudio.res");
		FindChildByName("ThirdPartySoundCredits")?.SetVisible(false);
	}

	public override void OnResetData() {
		RequireRestart = false;
		SFXSlider.Reset();
		MusicSlider.Reset();

		ConVarRef closecaption = new("closecaption");
		ConVarRef cc_subtitles = new("cc_subtitles");

		if (closecaption.GetBool()) {
			if (cc_subtitles.GetBool())
				CloseCaptionCombo.ActivateItem(2);
			else
				CloseCaptionCombo.ActivateItem(1);
		}
		else
			CloseCaptionCombo.ActivateItem(0);

		ConVarRef snd_surround_speakers = new("snd_surround_speakers");
		int speakers = snd_surround_speakers.GetInt();

		if (IsPosix && speakers == 0)
			speakers = 2;

		if (speakers < 0)
			speakers = 2;

		for (int itemId = 0; itemId < SpeakerSetupCombo.GetItemCount(); itemId++) {
			KeyValues? kv = SpeakerSetupCombo.GetItemUserData(itemId);
			if (kv != null && kv.GetInt("speakers") == speakers) {
				SpeakerSetupCombo.ActivateItem(itemId);
				break;
			}
		}

		// todo finish
	}


	static readonly KeyValues KV_ApplyButtonEnable = new("ApplyButtonEnable");
	[UiMessage("ControlModified")]
	public void OnControlModified(Panel sender) {
		PostActionSignal(KV_ApplyButtonEnable);
	}

	public override void OnApplyChanges() {
		SFXSlider.ApplyChanges();
		MusicSlider.ApplyChanges();

		// More to do
	}
}