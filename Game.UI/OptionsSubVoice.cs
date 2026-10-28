using Source.Common.Formats.Keyvalues;
using Source.GUI.Controls;
using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;

namespace Game.UI;

public class OptionsSubVoice : PropertyPage
{
	IVoiceTweak? VoiceTweak;
	IVoiceTweak voiceTeak => VoiceTweak!.Value;
	CheckButton MicBoost;
	ImagePanel MicMeter;
	ImagePanel MicMeter2;
	Button TestMicrophoneButton;
	Label MicrophoneSliderLabel;
	Slider MicrophoneVolume;
	Label ReceiveSliderLabel;
	CvarSlider ReceiveVolume;
	CvarToggleCheckButton VoiceEnableCheckButton;

	int MicVolumeValue;
	bool MicBoostSelected;
	float fReceiveVolume;
	int ReceiveSliderValue;
	bool VoiceOn;

	public OptionsSubVoice(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		VoiceTweak = Singleton<IEngineClient>().GetVoiceTweakAPI();

		MicMeter = new(this, "MicMeter");
		MicMeter2 = new(this, "MicMeter2");

		ReceiveSliderLabel = new(this, "ReceiveLabel", "#GameUI_VoiceReceiveVolume");
		ReceiveVolume = new(this, "VoiceReceive", "#GameUI_ReceiveVolume", 0.0f, 1.0f, "voice_scale");

		MicrophoneSliderLabel = new(this, "MicrophoneLabel", "#GameUI_VoiceTransmitVolume");
		MicrophoneVolume = new(this, "#GameUI_MicrophoneVolume");
		MicrophoneVolume.SetRange(0, 100);
		MicrophoneVolume.AddActionSignalTarget(this);

		VoiceEnableCheckButton = new CvarToggleCheckButton(this, "voice_modenable", "#GameUI_EnableVoice", "voice_modenable");

		MicBoost = new CheckButton(this, "MicBoost", "#GameUI_BoostMicrophone");
		MicBoost.AddActionSignalTarget(this);

		TestMicrophoneButton = new Button(this, "TestMicrophone", "#GameUI_TestMicrophone");

		LoadControlSettings("resource/OptionsSubVoice.res");

		VoiceOn = false;
		MicMeter2.SetVisible(false);

		if (VoiceTweak == null) {
			ReceiveVolume.SetEnabled(false);
			MicrophoneVolume.SetEnabled(false);
			VoiceEnableCheckButton.SetEnabled(false);
			MicBoost.SetEnabled(false);
			TestMicrophoneButton.SetEnabled(false);
		}
		else
			OnResetData();
	}

	// FIXME #37
	public override void Dispose() {
		base.Dispose();
		if (VoiceOn)
			EndTestMicrophone();
	}

	public override void OnResetData() {
		if (VoiceTweak == null)
			return;

		float micVolume = voiceTeak.GetControlFloat(VoiceTweakControl.MicrophoneVolume);
		MicrophoneVolume.SetValue((int)(100.0f * micVolume));
		MicVolumeValue = MicrophoneVolume.GetValue();

		float micBoost = voiceTeak.GetControlFloat(VoiceTweakControl.MicBoost);
		MicBoost.SetSelected(micBoost != 0.0f);
		MicBoostSelected = MicBoost.IsSelected();

		ReceiveVolume.Reset();
		fReceiveVolume = ReceiveVolume.GetSliderValue();

		VoiceEnableCheckButton.Reset();
	}

	public void OnSlierMoved(int position) {
		if (VoiceTweak != null) {
			if (MicrophoneVolume.GetValue() != MicVolumeValue)
				PostActionSignal(new KeyValues("ApplyButtonEnable"));
		}
	}

	public void OnCheckButtonChecked() {
		if (VoiceTweak != null) {
			if (MicBoost.IsSelected() != MicBoostSelected)
				PostActionSignal(new KeyValues("ApplyButtonEnable"));
		}
	}

	public override void OnApplyChanges() {
		if (VoiceTweak == null)
			return;

		MicVolumeValue = MicrophoneVolume.GetValue();
		float micVolume = MicVolumeValue / 100.0f;
		voiceTeak.SetControlFloat(VoiceTweakControl.MicrophoneVolume, micVolume);

		MicBoostSelected = MicBoost.IsSelected();
		voiceTeak.SetControlFloat(VoiceTweakControl.MicBoost, MicBoostSelected ? 1.0f : 0.0f);

		ReceiveVolume.ApplyChanges();
		fReceiveVolume = ReceiveVolume.GetSliderValue();

		VoiceEnableCheckButton.ApplyChanges();
	}

	private void StartTestMicrophone() {
		if (VoiceTweak == null || VoiceOn)
			return;

		VoiceOn = true;

		UseCurrentVoiceParameters();

		if (voiceTeak.StartVoiceTweakMode() != 0) {
			TestMicrophoneButton.SetText("#GameUI_StopTestMicrophone");

			ReceiveVolume.SetEnabled(false);
			MicrophoneVolume.SetEnabled(false);
			VoiceEnableCheckButton.SetEnabled(false);
			MicBoost.SetEnabled(false);
			MicrophoneSliderLabel.SetEnabled(false);
			ReceiveSliderLabel.SetEnabled(false);

			MicMeter2.SetVisible(true);
		}
		else {
			ResetVoiceParameters();

			// we couldn't start it
			VoiceOn = false;
			return;
		}
	}

	private void UseCurrentVoiceParameters() {
		int nVal = MicrophoneVolume.GetValue();
		float val = (float)nVal / 100.0f;
		voiceTeak.SetControlFloat(VoiceTweakControl.MicrophoneVolume, val);

		bool selected = MicBoost.IsSelected();
		val = selected ? 1.0f : 0.0f;
		voiceTeak.SetControlFloat(VoiceTweakControl.MicBoost, val);

		// get where the current slider is
		ReceiveSliderValue = ReceiveVolume.GetValue();
		ReceiveVolume.ApplyChanges();
	}

	private void ResetVoiceParameters() {
		float fMicVolume = (float)MicVolumeValue / 100.0f;
		voiceTeak.SetControlFloat(VoiceTweakControl.MicrophoneVolume, fMicVolume);
		voiceTeak.SetControlFloat(VoiceTweakControl.MicBoost, MicBoostSelected ? 1.0f : 0.0f);

		// restore the old value
		ConVarRef voice_scale = new("voice_scale");
		voice_scale.SetValue(fReceiveVolume);

		ReceiveVolume.Reset();
		// set the slider to 'new' value, but we've reset the 'start' value where it was
		ReceiveVolume.SetValue(ReceiveSliderValue);
	}

	private void EndTestMicrophone() {
		if (VoiceTweak == null || !VoiceOn)
			return;

		if (voiceTeak.IsStillTweaking())
			voiceTeak.EndVoiceTweakMode();

		ResetVoiceParameters();
		TestMicrophoneButton.SetText("#GameUI_TestMicrophone");
		VoiceOn = false;

		ReceiveVolume.SetEnabled(true);
		MicrophoneVolume.SetEnabled(true);
		VoiceEnableCheckButton.SetEnabled(true);
		MicBoost.SetEnabled(true);
		MicrophoneSliderLabel.SetEnabled(true);
		ReceiveSliderLabel.SetEnabled(true);
		MicMeter2.SetVisible(false);
	}

	[UiCommand("TestMicrophone")]
	private void OnTestMicrophone() {
		if (!VoiceOn)
			StartTestMicrophone();
		else
			EndTestMicrophone();
	}

	[UiCommand("SteamVoiceSettings")]
	private void OnSteamVoiceSettings() => Steamworks.SteamFriends.ActivateGameOverlay("VoiceSettings");

	public override void OnPageHide() {
		if (VoiceOn)
			EndTestMicrophone();
		base.OnPageHide();
	}

	private void OnControlModified() => PostActionSignal(new KeyValues("ApplyButtonEnable"));

	private const int BAR_WIDTH = 160;
	private const int BAR_INCREMENT = 8;
	public override void OnThink() {
		base.OnThink();

		if (VoiceOn) {
			if (!voiceTeak.IsStillTweaking()) {
				DevMsg(1, "Lost Voice Tweak channels, resetting\n");
				EndTestMicrophone();
			}
			else {
				float val = voiceTeak.GetControlFloat(VoiceTweakControl.SpeakingVolume);
				int nValue = (int)(val * 32768.0f + 0.5f);

				int width = (BAR_WIDTH * nValue) / 32768;
				width = ((width + (BAR_INCREMENT - 1)) / BAR_INCREMENT) * BAR_INCREMENT;  // round to nearest BAR_INCREMENT

				MicMeter2.GetSize(out int wide, out int tall);
				MicMeter2.SetSize(width, tall);
				MicMeter2.Repaint();
			}
		}
	}
}