using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.Keyvalues;
using Source.Common.GameUI;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.MaterialSystem;
using Source.Common.Networking;
using Source.Engine;
using Source.GUI.Controls;

using System.Numerics;

namespace Game.UI;

struct RatioToAspectMode
{
	public int Anamorphic;
	public float AspectRatio;
}

struct AAMode
{
	public int NumSamples;
	public int QualityLevel;
}

class GammaDialog : Frame
{
	public GammaDialog(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {

	}
}

class OptionsSubVideoAdvancedDlg : Frame
{
	bool UseChanges;
	ComboBox ModelDetail, TextureDetail, AntialiasingMode, FilteringMode;
	ComboBox ShadowDetail, HDR, WaterDetail, VSync, Multicore, ShaderDetail;
	ComboBox ColorCorrection;
	ComboBox MotionBlur;
	ComboBox DXLevel;
	int NumAAModes;
	AAMode[] AAModes = new AAMode[16];

	public OptionsSubVideoAdvancedDlg(Panel? parent) : base(parent, "OptionsSubVideoAdvancedDlg") {
		SetTitle("#GameUI_VideoAdvanced_Title", true);
		SetSize(260, 400);

		DXLevel = new(this, "dxlevel", 6, false);

		MaterialSystem_Config config = Materials.GetCurrentConfigForVideoCard();
		// KeyValues kv = new("config");
		// Materials.GetRecommendedConfigurationInfo(0, kv);

		DXLevel.RemoveAll();
		DXLevel.AddItem("maybe", null);

		ModelDetail = new ComboBox(this, "ModelDetail", 6, false);
		ModelDetail.AddItem("#gameui_low", null);
		ModelDetail.AddItem("#gameui_medium", null);
		ModelDetail.AddItem("#gameui_high", null);

		TextureDetail = new ComboBox(this, "TextureDetail", 6, false);
		TextureDetail.AddItem("#gameui_low", null);
		TextureDetail.AddItem("#gameui_medium", null);
		TextureDetail.AddItem("#gameui_high", null);
		TextureDetail.AddItem("#gameui_ultra", null);

		NumAAModes = 0;
		AntialiasingMode = new ComboBox(this, "AntialiasingMode", 10, false);
		AntialiasingMode.AddItem("#GameUI_None", null);
		AAModes[NumAAModes].NumSamples = 1;
		AAModes[NumAAModes].QualityLevel = 0;
		NumAAModes++;

		// if (materials.SupportsMSAAMode(2)) {
		AntialiasingMode.AddItem("#GameUI_2X", null);
		AAModes[NumAAModes].NumSamples = 2;
		AAModes[NumAAModes].QualityLevel = 0;
		NumAAModes++;
		// }

		// if (materials.SupportsMSAAMode(4)) {
		AntialiasingMode.AddItem("#GameUI_4X", null);
		AAModes[NumAAModes].NumSamples = 4;
		AAModes[NumAAModes].QualityLevel = 0;
		NumAAModes++;
		// }

		// if (materials.SupportsMSAAMode(6)) {
		AntialiasingMode.AddItem("#GameUI_6X", null);
		AAModes[NumAAModes].NumSamples = 6;
		AAModes[NumAAModes].QualityLevel = 0;
		NumAAModes++;
		// }

		// if (materials.SupportsCSAAMode(4, 2)) // nVidia CSAA			"8x"
		// {
		AntialiasingMode.AddItem("#GameUI_8X_CSAA", null);
		AAModes[NumAAModes].NumSamples = 4;
		AAModes[NumAAModes].QualityLevel = 2;
		NumAAModes++;
		// }

		// if (materials.SupportsCSAAMode(4, 4)) // nVidia CSAA			"16x"
		// {
		AntialiasingMode.AddItem("#GameUI_16X_CSAA", null);
		AAModes[NumAAModes].NumSamples = 4;
		AAModes[NumAAModes].QualityLevel = 4;
		NumAAModes++;
		// }

		// if (materials.SupportsMSAAMode(8)) {
		AntialiasingMode.AddItem("#GameUI_8X", null);
		AAModes[NumAAModes].NumSamples = 8;
		AAModes[NumAAModes].QualityLevel = 0;
		NumAAModes++;
		// }

		// if (materials.SupportsCSAAMode(8, 2)) // nVidia CSAA			"16xQ"
		// {
		AntialiasingMode.AddItem("#GameUI_16XQ_CSAA", null);
		AAModes[NumAAModes].NumSamples = 8;
		AAModes[NumAAModes].QualityLevel = 2;
		NumAAModes++;
		// }

		FilteringMode = new ComboBox(this, "FilteringMode", 6, false);
		FilteringMode.AddItem("#GameUI_Bilinear", null);
		FilteringMode.AddItem("#GameUI_Trilinear", null);
		FilteringMode.AddItem("#GameUI_Anisotropic2X", null);
		FilteringMode.AddItem("#GameUI_Anisotropic4X", null);
		FilteringMode.AddItem("#GameUI_Anisotropic8X", null);
		FilteringMode.AddItem("#GameUI_Anisotropic16X", null);

		ShadowDetail = new ComboBox(this, "ShadowDetail", 6, false);
		ShadowDetail.AddItem("#gameui_low", null);
		ShadowDetail.AddItem("#gameui_medium", null);
		// if (materials.SupportsShadowDepthTextures())
		ShadowDetail.AddItem("#gameui_high", null);

		HDR = new ComboBox(this, "HDR", 6, false);
		HDR.AddItem("#GameUI_hdr_level0", null);
		HDR.AddItem("#GameUI_hdr_level1", null);

		// if (materials.SupportsHDRMode(HDR_TYPE_INTEGER))
		HDR.AddItem("#GameUI_hdr_level2", null);

		HDR.SetEnabled(true);

		WaterDetail = new ComboBox(this, "WaterDetail", 6, false);
		WaterDetail.AddItem("#gameui_noreflections", null);
		WaterDetail.AddItem("#gameui_reflectonlyworld", null);
		WaterDetail.AddItem("#gameui_reflectall", null);

		VSync = new ComboBox(this, "VSync", 2, false);
		VSync.AddItem("#gameui_disabled", null);
		VSync.AddItem("#gameui_enabled", null);

		Multicore = new ComboBox(this, "Multicore", 2, false);
		Multicore.AddItem("#gameui_disabled", null);
		Multicore.AddItem("#gameui_enabled", null);

		ShaderDetail = new ComboBox(this, "ShaderDetail", 6, false);
		ShaderDetail.AddItem("#gameui_low", null);
		ShaderDetail.AddItem("#gameui_high", null);

		ColorCorrection = new ComboBox(this, "ColorCorrection", 2, false);
		ColorCorrection.AddItem("#gameui_disabled", null);
		ColorCorrection.AddItem("#gameui_enabled", null);

		MotionBlur = new ComboBox(this, "MotionBlur", 2, false);
		MotionBlur.AddItem("#gameui_disabled", null);
		MotionBlur.AddItem("#gameui_enabled", null);

		LoadControlSettings("resource/OptionsSubVideoAdvancedDlg.res");
		MoveToCenterOfScreen();
		SetSizeable(false);

		DXLevel.SetEnabled(false);

		Label? dxLabel = FindChildByName("Label1") as Label;
		dxLabel?.SetText("Hardware OpenGL level:");

		dxLabel = FindChildByName("Label2") as Label;
		dxLabel?.SetText("Software OpenGL level:");

		ReadOnlySpan<char> version = OpenGL.Gl46.glGetStringSafe(OpenGL.Gl46.GL_VERSION).Split(' ')[0];
		dxLabel = FindChildByName("dxinstalledlabel") as Label;
		dxLabel?.SetText(version);
		SetControlString("dxlabel", version);

		ColorCorrection.SetEnabled(true);
		MotionBlur.SetEnabled(true);

		if (cvar.FindVar("fov_desired") == null) {
			Panel? fov = FindChildByName("FovSlider");
			fov?.SetVisible(false);
			fov = FindChildByName("FovLabel");
			fov?.SetVisible(false);
		}

		MarkDefaultSettingsAsRecommended();

		UseChanges = false;
	}

	public override void Activate() {
		base.Activate();

		Input.SetAppModalSurface(this);

		if (!UseChanges)
			OnResetData();
	}

	void SetComboItemAsRecommended(ComboBox combo, int item) {
		Span<char> text = stackalloc char[512];
		combo.GetItemText(item, text);
		sprintf(text, "%s *").S(text);
		combo.UpdateItem(item, text, null);
	}

	int FindMSAAMode(int samples, int quality) {
		for (int i = 0; i < NumAAModes; i++) {
			if (AAModes[i].NumSamples == samples && AAModes[i].QualityLevel == quality)
				return i;
		}

		return 0;
	}

	public override void OnTextChanged(Panel from) {
		if (from == DXLevel && RequiresRestart()) {
			QueryBox box = new("#GameUI_SettingRequiresDisconnect_Title", "#GameUI_SettingRequiresDisconnect_Info");
			box.AddActionSignalTarget(this);
			box.SetCancelCommand(new KeyValues("ResetDXLevelCombo"));
			box.DoModal();
		}
	}

	private void OnGameUIHidden() => Close();

	private void ResetDXLevelCombo() {
		if (HDR.IsEnabled()) {
			ConVarRef mat_hdr_level = new("mat_hdr_level");
			Assert(mat_hdr_level.IsValid());
			HDR.ActivateItem(Math.Clamp(mat_hdr_level.GetInt(), 0, 2));
		}
	}

	private void OK_Confirmed() {
		UseChanges = true;
		Close();
	}

	private void MarkDefaultSettingsAsRecommended() {
		KeyValues config = new("config");
		// Materials.GetRecommendedConfigurationInfo(0, kv);

		int skipLevels = config.GetInt("ConVar.mat_picmip", 0);
		int anisotropicLevel = config.GetInt("ConVar.mat_forceaniso", 1);
		int forceTrilinear = config.GetInt("ConVar.mat_trilinear", 0);
		int aASamples = config.GetInt("ConVar.mat_antialias", 0);
		int aAQuality = config.GetInt("ConVar.mat_aaquality", 0);
		int renderToTextureShadows = config.GetInt("ConVar.r_shadowrendertotexture", 0);
		int shadowDepthTextureShadows = config.GetInt("ConVar.r_flashlightdepthtexture", 0);
		int waterUseRealtimeReflection = config.GetInt("ConVar.r_waterforceexpensive", 0);
		int waterUseEntityReflection = config.GetInt("ConVar.r_waterforcereflectentities", 0);
		int matVSync = config.GetInt("ConVar.mat_vsync", 1);
		int rootLOD = config.GetInt("ConVar.r_rootlod", 0);
		int reduceFillRate = config.GetInt("ConVar.mat_reducefillrate", 0);
		int colorCorrection = config.GetInt("ConVar.mat_colorcorrection", 0);
		int motionBlur = config.GetInt("ConVar.mat_motion_blur_enabled", 0);
		int multicore = 1;//GetCPUInformation().PhysicalProcessors >= 2;


		SetComboItemAsRecommended(ModelDetail, 2 - rootLOD);
		SetComboItemAsRecommended(TextureDetail, 2 - skipLevels);

		switch (anisotropicLevel) {
			case 2:
				SetComboItemAsRecommended(FilteringMode, 2);
				break;
			case 4:
				SetComboItemAsRecommended(FilteringMode, 3);
				break;
			case 8:
				SetComboItemAsRecommended(FilteringMode, 4);
				break;
			case 16:
				SetComboItemAsRecommended(FilteringMode, 5);
				break;
			case 0:
			default:
				if (forceTrilinear != 0)
					SetComboItemAsRecommended(FilteringMode, 1);
				else
					SetComboItemAsRecommended(FilteringMode, 0);
				break;
		}

		int MSAAMode = FindMSAAMode(aASamples, aAQuality);
		SetComboItemAsRecommended(AntialiasingMode, MSAAMode);

		if (shadowDepthTextureShadows != 0)
			SetComboItemAsRecommended(ShadowDetail, 2); // Shadow depth mapping (in addition to RTT shadows)
		else if (renderToTextureShadows != 0)
			SetComboItemAsRecommended(ShadowDetail, 1); // RTT shadows
		else
			SetComboItemAsRecommended(ShadowDetail, 0); // Blobbies

		SetComboItemAsRecommended(ShaderDetail, reduceFillRate == 1 ? 0 : 1);

		if (waterUseRealtimeReflection != 0) {
			if (waterUseEntityReflection != 0)
				SetComboItemAsRecommended(WaterDetail, 2);
			else
				SetComboItemAsRecommended(WaterDetail, 1);
		}
		else
			SetComboItemAsRecommended(WaterDetail, 0);

		SetComboItemAsRecommended(VSync, (matVSync != 0) ? 1 : 0);
		SetComboItemAsRecommended(Multicore, (multicore != 0) ? 1 : 0);
		SetComboItemAsRecommended(HDR, 2);
		SetComboItemAsRecommended(ColorCorrection, colorCorrection);
		SetComboItemAsRecommended(MotionBlur, motionBlur);
	}

	private void ApplyChangesToConVar(ReadOnlySpan<char> cvarName, int value) {
		Assert(cvar.FindVar(cvarName) != null);
		Span<char> cmd = stackalloc char[256];
		sprintf(cmd, "%s %d\n").S(cvarName).I(value);
		Singleton<IEngineClient>().ClientCmd_Unrestricted(cmd);
	}

	public void ApplyChanges() {
		if (!UseChanges)
			return;

		ApplyChangesToConVar("r_rootlod", 2 - ModelDetail.GetActiveItem());
		ApplyChangesToConVar("mat_picmip", 2 - TextureDetail.GetActiveItem());

		ApplyChangesToConVar("mat_trilinear", 0);
		ApplyChangesToConVar("mat_forceaniso", 1);
		switch (FilteringMode.GetActiveItem()) {
			case 0:
				break;
			case 1:
				ApplyChangesToConVar("mat_trilinear", 1);
				break;
			case 2:
				ApplyChangesToConVar("mat_forceaniso", 2);
				break;
			case 3:
				ApplyChangesToConVar("mat_forceaniso", 4);
				break;
			case 4:
				ApplyChangesToConVar("mat_forceaniso", 8);
				break;
			case 5:
				ApplyChangesToConVar("mat_forceaniso", 16);
				break;
			default:
				// Trilinear.
				ApplyChangesToConVar("mat_forceaniso", 1);
				break;
		}

		// Set the AA convars according to the menu item chosen
		int activeAAItem = AntialiasingMode.GetActiveItem();
		ApplyChangesToConVar("mat_antialias", AAModes[activeAAItem].NumSamples);
		ApplyChangesToConVar("mat_aaquality", AAModes[activeAAItem].QualityLevel);

		if (HDR.IsEnabled()) {
			ConVarRef mat_hdr_level = new("mat_hdr_level");
			Assert(mat_hdr_level.IsValid());
			mat_hdr_level.SetValue(HDR.GetActiveItem());
		}

		if (ShadowDetail.GetActiveItem() == 0) { // Blobby shadows
			ApplyChangesToConVar("r_shadowrendertotexture", 0);  // Turn off RTT shadows
			ApplyChangesToConVar("r_flashlightdepthtexture", 0); // Turn off shadow depth textures
		}
		else if (ShadowDetail.GetActiveItem() == 1) { // RTT shadows only
			ApplyChangesToConVar("r_shadowrendertotexture", 1);  // Turn on RTT shadows
			ApplyChangesToConVar("r_flashlightdepthtexture", 0); // Turn off shadow depth textures
		}
		else if (ShadowDetail.GetActiveItem() == 2) { // Shadow depth textures
			ApplyChangesToConVar("r_shadowrendertotexture", 1);  // Turn on RTT shadows
			ApplyChangesToConVar("r_flashlightdepthtexture", 1); // Turn on shadow depth textures
		}

		ApplyChangesToConVar("mat_reducefillrate", (ShaderDetail.GetActiveItem() > 0) ? 0 : 1);

		switch (WaterDetail.GetActiveItem()) {
			default:
			case 0:
				ApplyChangesToConVar("r_waterforceexpensive", 0);
				ApplyChangesToConVar("r_waterforcereflectentities", 0);
				break;
			case 1:
				ApplyChangesToConVar("r_waterforceexpensive", 1);
				ApplyChangesToConVar("r_waterforcereflectentities", 0);
				break;
			case 2:
				ApplyChangesToConVar("r_waterforceexpensive", 1);
				ApplyChangesToConVar("r_waterforcereflectentities", 1);
				break;
		}

		ApplyChangesToConVar("mat_vsync", VSync.GetActiveItem());

		int mc = Multicore.GetActiveItem();
		ApplyChangesToConVar("mat_queue_mode", (mc == 0) ? 0 : -1);
		ApplyChangesToConVar("mat_colorcorrection", ColorCorrection.GetActiveItem());
		ApplyChangesToConVar("mat_motion_blur_enabled", MotionBlur.GetActiveItem());

		CvarSlider? FOV = (CvarSlider?)FindChildByName("FOVSlider");
		FOV?.ApplyChanges();
	}

	public void OnResetData() {
		ConVarRef r_rootlod = new("r_rootlod");
		ConVarRef mat_picmip = new("mat_picmip");
		ConVarRef mat_trilinear = new("mat_trilinear");
		ConVarRef mat_forceaniso = new("mat_forceaniso");
		ConVarRef mat_antialias = new("mat_antialias");
		ConVarRef mat_aaquality = new("mat_aaquality");
		ConVarRef mat_vsync = new("mat_vsync");
		ConVarRef mat_queue_mode = new("mat_queue_mode");
		ConVarRef r_flashlightdepthtexture = new("r_flashlightdepthtexture");
		ConVarRef r_waterforceexpensive = new("r_waterforceexpensive");
		ConVarRef r_waterforcereflectentities = new("r_waterforcereflectentities");
		ConVarRef mat_reducefillrate = new("mat_reducefillrate");
		ConVarRef mat_hdr_level = new("mat_hdr_level");
		ConVarRef mat_colorcorrection = new("mat_colorcorrection");
		ConVarRef mat_motion_blur_enabled = new("mat_motion_blur_enabled");
		ConVarRef r_shadowrendertotexture = new("r_shadowrendertotexture");

		ResetDXLevelCombo();

		ModelDetail.ActivateItem(2 - Math.Clamp(r_rootlod.GetInt(), 0, 2));
		TextureDetail.ActivateItem(2 - Math.Clamp(mat_picmip.GetInt(), -1, 2));

		if (r_flashlightdepthtexture.GetBool()) { // If we're doing flashlight shadow depth texturing...
			r_shadowrendertotexture.SetValue(1); // ...be sure render to texture shadows are also on
			ShadowDetail.ActivateItem(2);
		}
		else if (r_shadowrendertotexture.GetBool()) // RTT shadows, but not shadow depth texturing
			ShadowDetail.ActivateItem(1);
		else // Lowest shadow quality
			ShadowDetail.ActivateItem(0);

		ShaderDetail.ActivateItem(mat_reducefillrate.GetBool() ? 0 : 1);
		HDR.ActivateItem(Math.Clamp(mat_hdr_level.GetInt(), 0, 2));

		switch (mat_forceaniso.GetInt()) {
			case 2:
				FilteringMode.ActivateItem(2);
				break;
			case 4:
				FilteringMode.ActivateItem(3);
				break;
			case 8:
				FilteringMode.ActivateItem(4);
				break;
			case 16:
				FilteringMode.ActivateItem(5);
				break;
			case 0:
			default:
				if (mat_trilinear.GetBool())
					FilteringMode.ActivateItem(1);
				else
					FilteringMode.ActivateItem(0);
				break;
		}

		int AASamples = mat_antialias.GetInt();
		int AAQuality = mat_aaquality.GetInt();
		int MSAAMode = FindMSAAMode(AASamples, AAQuality);
		AntialiasingMode.ActivateItem(MSAAMode);
		AntialiasingMode.SetEnabled(NumAAModes > 1);

		if (r_waterforceexpensive.GetBool()) {
			if (r_waterforcereflectentities.GetBool())
				WaterDetail.ActivateItem(2);
			else
				WaterDetail.ActivateItem(1);
		}
		else
			WaterDetail.ActivateItem(0);

		VSync.ActivateItem(mat_vsync.GetInt());

		int mc = mat_queue_mode.GetInt();

		Multicore.ActivateItem((mc == 0) ? 0 : 1);
		ColorCorrection.ActivateItem(mat_colorcorrection.GetInt());
		MotionBlur.ActivateItem(mat_motion_blur_enabled.GetInt());
	}

	public override void OnCommand(ReadOnlySpan<char> command) {
		if (command == "OK") {
			if (RequiresRestart()) {
				QueryBox box = new("#GameUI_SettingRequiresDisconnect_Title", "#GameUI_SettingRequiresDisconnect_Info");
				box.AddActionSignalTarget(this);
				box.SetOKCommand(new KeyValues("OK_Confirmed"));
				box.SetCancelCommand(new KeyValues("ResetDXLevelCombo"));
				box.DoModal();
				box.MoveToFront();
				return;
			}

			UseChanges = true;
			Close();
		}
		else
			base.OnCommand(command);
	}

	public override void OnKeyCodeTyped(ButtonCode code) {
		if (code == ButtonCode.KeyEscape)
			Close();
		else
			base.OnKeyCodeTyped(code);
	}

	public bool RequiresRestart() {
		if (gameUI.IsInLevel()) {
			if (gameUI.IsInBackgroundLevel())
				return false;

			// if (!gameUI.IsInMultiplayer()) //todo
			// 	return false;

			if (HDR.IsEnabled()) {
				ConVarRef mat_hdr_level = new("mat_hdr_level");
				Assert(mat_hdr_level.IsValid());
				if (mat_hdr_level.GetInt() != HDR.GetActiveItem())
					return true;
			}
		}

		return false;
	}

	public override void OnMessage(KeyValues message, IPanel? from) {
		switch (message.Name) {
			case "TextChanged":
				OnTextChanged((Panel)from!);
				break;
			case "GameUIHidden":
				OnGameUIHidden();
				break;
			case "ResetDXLevelCombo":
				ResetDXLevelCombo();
				break;
			case "OK_Confirmed":
				OK_Confirmed();
				break;
			default:
				base.OnMessage(message, from);
				break;
		}
	}
}

public class OptionsSubVideo : PropertyPage
{
	readonly public ModInfo ModInfo = Singleton<ModInfo>();

	readonly RatioToAspectMode[] RatioToAspectModes = [
		new () { Anamorphic = 0, AspectRatio = 4.0f / 3.0f },
		new () { Anamorphic = 1, AspectRatio = 16.0f / 9.0f },
		new () { Anamorphic = 2, AspectRatio = 16.0f / 10.0f },
		new () { Anamorphic = 2, AspectRatio = 1.0f }
	];

	// Indexed by AspectRatio combo item ID (0,1,2), matching the order items were added in the
	// constructor (#GameUI_AspectNormal, #GameUI_AspectWide16x9, #GameUI_AspectWide16x10).
	readonly (int Numerator, int Denominator)[] AspectRatioFractions = [
		(4, 3),
		(16, 9),
		(16, 10)
	];

	int[] DirectXLevels = [
		70,
		80,
		81,
		90,
		95
	];

	int SelectedMode;
	bool DisplayedVRModeMessage;

	ComboBox Mode;
	ComboBox Windowed;
	ComboBox AspectRatio;
	ComboBox VRMode;
	Button GammaButton;
	Button Advanced;
	Button Benchmark;
	CheckButton HDContent;

	OptionsSubVideoAdvancedDlg OptionsSubVideoAdvancedDlg;
	OptionsSubVideoThirdPartyCreditsDlg OptionsSubVideoThirdPartyCreditsDlg;
	GammaDialog GammaDialog;

	bool RequireRestart;
	URLButton ThirdPartyCredits;

	readonly static KeyValues KV_LaunchBenchmark = new("LaunchBenchmark");
	readonly static KeyValues KV_OpenAdvanced = new("OpenAdvanced");
	public OptionsSubVideo(Panel? parent, ReadOnlySpan<char> name) : base(parent, null) {
		GammaButton = new(this, "GammaButton", "#GameUI_AdjustGamma");
		Mode = new(this, "Resolution", 8, false);
		AspectRatio = new(this, "AspectRatio", 6, false);
		VRMode = new(this, "VRMode", 2, false);
		Advanced = new(this, "AdvancedButton", "#GameUI_AdvancedEllipsis");
		Advanced.SetCommand(KV_OpenAdvanced);
		Benchmark = new(this, "BenchmarkButton", "#GameUI_LaunchBenchmark");
		Benchmark.SetCommand(KV_LaunchBenchmark);
		ThirdPartyCredits = new(this, "ThirdPartyVideoCredits", "#GameUI_ThirdPartyTechCredits");
		// ThirdPartyCredits.SetCommand(new KeyValues("OpenThirdPartyVideoCreditsDialog"));//static
		HDContent = new(this, "HDContentButton", "#GameUI_HDContent");

		ReadOnlySpan<char> aspect1 = Localize.Find("#GameUI_AspectNormal");
		ReadOnlySpan<char> aspect2 = Localize.Find("#GameUI_AspectWide16x9");
		ReadOnlySpan<char> aspect3 = Localize.Find("#GameUI_AspectWide16x10");

		int NormalItemID = AspectRatio.AddItem(aspect1, null);
		int i16x9ItemID = AspectRatio.AddItem(aspect2, null);
		int i16x10ItemID = AspectRatio.AddItem(aspect3, null);

		MaterialSystem_Config config = Materials.GetCurrentConfigForVideoCard();

		int aspectMode = GetScreenAspectMode(config.VideoMode.Width, config.VideoMode.Height);
		switch (aspectMode) {
			case 0:
				AspectRatio.ActivateItem(NormalItemID);
				break;
			case 1:
				AspectRatio.ActivateItem(i16x9ItemID);
				break;
			case 2:
				AspectRatio.ActivateItem(i16x10ItemID);
				break;
		}

		Windowed = new(this, "DisplayModeCombo", 5, false);
		Windowed.AddItem("#GameUI_Fullscreen", null);
		Windowed.AddItem("#GameUI_Windowed", null);
		Windowed.AddItem("Borderless Window", null);

		PrepareResolutionList();

		LoadControlSettings("resource/OptionsSubVideo.res");

		Benchmark.SetVisible(g_pFileSystem.FileExists("maps/test_hardware.bsp"));
		if (!ModInfo.SupportsVR()) VRMode.SetVisible(false);
		if (!ModInfo.HasHDContent()) HDContent.SetVisible(false);
		ThirdPartyCredits.SetVisible(false);
	}

	// FIXME #37
	public override void Dispose() {
		base.Dispose();
		OptionsSubVideoThirdPartyCreditsDlg?.MarkForDeletion();
	}

	private void PrepareResolutionList() {
		Span<char> sz = stackalloc char[256];
		Mode.GetText(sz);

		new ScanF(sz, "%i x %i").Read(out int currentWidth).Read(out int currentHeight);

		Mode.RemoveAll();
		AspectRatio.SetItemEnabled(1, false);
		AspectRatio.SetItemEnabled(2, false);

		MaterialSystem_Config config = Materials.GetCurrentConfigForVideoCard();

		bool windowed = Windowed.GetActiveItem() == (Windowed.GetItemCount() - 2);
		bool borderless = Windowed.GetActiveItem() == (Windowed.GetItemCount() - 1);

		gameuifuncs.GetDesktopResolution(out int desktopWidth, out int desktopHeight);

		bool foundWidescreen = false;
		int selectedItemID = -1;

		// Enable widescreen aspect-ratio options if the desktop can actually produce
		// resolutions for them.
		for (int i = 1; i < AspectRatioFractions.Length; i++) {
			var (num, den) = AspectRatioFractions[i];
			List<Vector2> widescreenModes = [];
			UserVideoMode.ProduceAvailableResolutionList(num, den, desktopWidth, desktopHeight, widescreenModes);
			if (widescreenModes.Count > 0) {
				AspectRatio.SetItemEnabled(i, true);
				foundWidescreen = true;
			}
		}

		int activeAspect = Math.Clamp(AspectRatio.GetActiveItem(), 0, AspectRatioFractions.Length - 1);
		var (activeNum, activeDen) = AspectRatioFractions[activeAspect];

		List<Vector2> modes = [];
		UserVideoMode.ProduceAvailableResolutionList(activeNum, activeDen, desktopWidth, desktopHeight, modes);

		Span<char> modeText = stackalloc char[32];
		foreach (Vector2 mode in modes) {
			int w = (int)mode.X;
			int h = (int)mode.Y;

			int itemID = Mode.AddItem(sprintf(modeText, "%i x %i").I(w).I(h).ToSpan(), null);

			if (w == currentWidth && h == currentHeight)
				selectedItemID = itemID;
		}

		AspectRatio.SetEnabled(foundWidescreen);

		SelectedMode = selectedItemID;

		if (selectedItemID != -1)
			Mode.ActivateItem(selectedItemID);
		else {
			int width = config.VideoMode.Width;
			int height = config.VideoMode.Height;

			// if ((width > desktopWidth) || (height > desktopHeight)) {
			// 	width = desktopWidth;
			// 	height = desktopHeight;
			// }

			sprintf(sz, "%i x %i").I(width).I(height);
			Mode.SetText(sz);
		}
	}

	private bool BUseHDContent() {
		return false; //todo
	}

	private void SetUseHDContent(bool use) {
		// throw new NotImplementedException();
	}

	public override void OnResetData() {
		RequireRestart = false;

		MaterialSystem_Config config = Materials.GetCurrentConfigForVideoCard();

		if (config.Windowed())
			Windowed.ActivateItem(Windowed.GetItemCount() - 2);
		else if (config.NoWindowBorder())
			Windowed.ActivateItem(Windowed.GetItemCount() - 1);
		else
			Windowed.ActivateItem(0);

		GammaButton.SetEnabled(!config.Windowed());
		HDContent.SetSelected(BUseHDContent());

		SetCurrentResolutionComboItem();
	}

	private void SetCurrentResolutionComboItem() {
		MaterialSystem_Config config = Materials.GetCurrentConfigForVideoCard();

		int currentResolutionItemID = -1;
		Span<char> itemText = stackalloc char[32];
		for (int i = 0; i < Mode.GetItemCount(); i++) {
			Mode.GetItemText(i, itemText);
			new ScanF(itemText, "%i x %i").Read(out int width).Read(out int height);

			if (width == config.VideoMode.Width && height == config.VideoMode.Height) {
				currentResolutionItemID = i;
				break;
			}
		}

		if (currentResolutionItemID != -1) {
			SelectedMode = currentResolutionItemID;
			Mode.ActivateItem(currentResolutionItemID);
		}
	}

	readonly IEngineClient engine = Singleton<IEngineClient>();
	public override void OnApplyChanges() {
		if (RequiresRestart()) {
			INetChannelInfo? nci = engine.GetNetChannelInfo();
			if (nci != null) {
				ReadOnlySpan<char> addr = nci.GetAddress();
				if (addr.Length > 0) {
					if (strncmp(addr, "127.0.0.1", 9) != 0 && strncmp(addr, "localhost", 9) != 0) {
						engine.ClientCmd_Unrestricted("retry\n");
					}
					else {
						engine.ClientCmd_Unrestricted("disconnect\n");
					}
				}
			}
		}

		OptionsSubVideoAdvancedDlg?.ApplyChanges();

		Span<char> sz = stackalloc char[256];
		if (SelectedMode == -1)
			Mode.GetText(sz);
		else
			Mode.GetItemText(SelectedMode, sz);

		new ScanF(sz, "%i x %i").Read(out int width).Read(out int height);

		bool configChanged = false;
		bool windowed = Windowed.GetActiveItem() == (Windowed.GetItemCount() - 2);
		bool borderless = Windowed.GetActiveItem() == (Windowed.GetItemCount() - 1);
		MaterialSystem_Config config = Materials.GetCurrentConfigForVideoCard();

		bool vrMode = VRMode.GetActiveItem() != 0;
		// todo vr mode

		if (config.VideoMode.Width != width || config.VideoMode.Height != height || config.Windowed() != windowed || config.NoWindowBorder() != borderless)
			configChanged = true;

		if (configChanged) {
			Span<char> cmd = stackalloc char[256];
			sprintf(cmd, "mat_setvideomode %i %i %i %i\n").I(width).I(height).I((windowed || borderless) ? 1 : 0).I(borderless ? 1 : 0);
			engine.ClientCmd_Unrestricted(cmd);
		}

		if (ModInfo.HasHDContent()) {
			if (BUseHDContent() != HDContent.IsSelected()) {
				SetUseHDContent(HDContent.IsSelected());
				MessageBox box = new("#GameUI_OptionsRestartRequired_Title", "#GameUI_HDRestartRequired_Info");
				box.DoModal();
				box.MoveToFront();
			}
		}

		engine.ClientCmd_Unrestricted("mat_savechanges\n");
	}


	public override void PerformLayout() {
		base.PerformLayout();

		if (GammaButton != null) {
			MaterialSystem_Config config = Materials.GetCurrentConfigForVideoCard();
			GammaButton.SetEnabled(!config.Windowed());
		}
	}

	public override void OnTextChanged(Panel from) {
		if (from == Mode) {
			MaterialSystem_Config config = Materials.GetCurrentConfigForVideoCard();
			SelectedMode = Mode.GetActiveItem();

			Span<char> text = stackalloc char[256];
			Mode.GetText(text);
			new ScanF(text, "%i x %i").Read(out int w).Read(out int h);
			if (w != config.VideoMode.Width || h != config.VideoMode.Height)
				OnDataChanged();
		}
		else if (from == AspectRatio)
			PrepareResolutionList();
		else if (from == Windowed) {
			PrepareResolutionList();
			OnDataChanged();
		}
	}

	private void OnDataChanged() => PostActionSignal(new KeyValues("ApplyButtonEnabled"));//static

	private bool RequiresRestart() {
		if (OptionsSubVideoAdvancedDlg != null && OptionsSubVideoAdvancedDlg.RequiresRestart())
			return true;

		return RequireRestart;
	}

	private void OpenAdvanced() {
		OptionsSubVideoAdvancedDlg ??= new(BasePanel.g_BasePanel!.FindChildByName("OptionsDialog"));
		OptionsSubVideoAdvancedDlg.Activate();
	}

	private void OpenGammaDialog() {
		GammaDialog ??= new(this, "GammaDialog");
		GammaDialog.Activate();
	}

	private void LaunchBenchmark() {
		// BasePanel.g_BasePanel?.OnOpenBenchmarkDialog(); // todo
	}

	// private void OpenThirdPartyVideoCreditsDialog() {
	// 	OptionsSubVideoThirdPartyCreditsDlg ??= new(this);
	// 	OptionsSubVideoThirdPartyCreditsDlg.Activate();
	// }

	private int GetScreenAspectMode(int width, int height) {
		float aspectRatio = (float)width / height;
		float closestAspectRatioDist = 99999.0f;
		int closestAnamorphic = 0;

		for (int i = 0; i < RatioToAspectModes.Length; i++) {
			float dist = MathF.Abs(RatioToAspectModes[i].AspectRatio - aspectRatio);
			if (dist < closestAspectRatioDist) {
				closestAspectRatioDist = dist;
				closestAnamorphic = RatioToAspectModes[i].Anamorphic;
			}
		}

		return closestAnamorphic;
	}

	FileStream FOpenGameHDFile(FileMode mode) {
		// ReadOnlySpan<char> gameDir = engine.GetGameDirectory(); // todo

		// Span<char> modSteamInfPath = stackalloc char[1024];
		// sprintf(modSteamInfPath, "{0}/game_hd.txt").S(gameDir);

		// return File.Open(modSteamInfPath.ToString(), mode);

		throw new NotImplementedException();
	}

	public override void OnMessage(KeyValues message, IPanel? from) {
		switch (message.Name) {
			case "ControlModified":
				OnDataChanged();
				break;
			case "TextChanged":
				OnTextChanged((Panel)from!);
				break;
			case "OpenAdvanced":
				OpenAdvanced();
				break;
			case "LaunchBenchmark":
				LaunchBenchmark();
				break;
			case "OpenGammaDialog":
				OpenGammaDialog();
				break;
			// case "OpenThirdPartyVideoCreditsDialog":
			// 	OpenThirdPartyVideoCreditsDialog();
			// 	break;
			default:
				base.OnMessage(message, from);
				break;
		}
	}
}

class OptionsSubVideoThirdPartyCreditsDlg : Frame
{
	public OptionsSubVideoThirdPartyCreditsDlg(Panel? parent) : base(null, null) {
		SetTitle("#GameUI_ThirdPartyVideo_Title", true);
		SetSize(500, 200);
		LoadControlSettings("resource/OptionsSubVideoThirdPartyDlg.res");
		MoveToCenterOfScreen();
		SetSizeable(false);
		SetDeleteSelfOnClose(true);
	}

	public override void Activate() {
		base.Activate();
		Input.SetAppModalSurface(this);
	}

	public override void OnKeyCodeTyped(ButtonCode code) {
		if (code == ButtonCode.KeyEscape)
			Close();
		else
			base.OnKeyCodeTyped(code);
	}
}
