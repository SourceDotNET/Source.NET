using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.GUI.Controls;

namespace Game.UI;

public class OptionsSubMultiplayer : PropertyPage
{
	ImagePanel LogoImage;
	LabeledCommandComboBox LogoList;
	InlineArray128<char> LogoName;

	readonly List<CvarToggleCheckButton> CvarToggleCheckButtons = [];

	ComboBox DownloadFilterCombo;

	int LogoR;
	int LogoG;
	int LogoB;

	FileOpenDialog? ImportSprayDialog;

	readonly ModInfo ModInfo = Singleton<ModInfo>();

	public OptionsSubMultiplayer(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		Button cancel = new(this, "Cancel", "#GameUI_Cancel");
		cancel.SetCommand("Close");

		Button ok = new(this, "OK", "#GameUI_OK");
		ok.SetCommand("Ok");

		Button apply = new(this, "Apply", "#GameUI_Apply");
		apply.SetCommand("Apply");

		Button advanced = new(this, "Advanced", "#GameUI_AdvancedEllipsis");
		advanced.SetCommand("Advanced");

		Button importSprayImage = new(this, "ImportSprayImage", "#GameUI_ImportSprayEllipsis");
		importSprayImage.SetCommand("ImportSprayImage");

		ImportSprayDialog = null;

		LogoList = new LabeledCommandComboBox(this, "SpraypaintList");
		LogoName[0] = '\0';
		InitLogoList(LogoList);

		LogoImage = new ImagePanel(this, "LogoImage");
		LogoImage.AddActionSignalTarget(this);

		LogoR = 255;
		LogoG = 255;
		LogoB = 255;

		DownloadFilterCombo = new ComboBox(this, "DownloadFilterCheck", 4, false);
		DownloadFilterCombo.AddItem("#GameUI_DownloadFilter_ALL", null);
		DownloadFilterCombo.AddItem("#GameUI_DownloadFilter_NoSounds", null);
		DownloadFilterCombo.AddItem("#GameUI_DownloadFilter_MapsOnly", null);
		DownloadFilterCombo.AddItem("#GameUI_DownloadFilter_None", null);

		LoadControlSettings("Resource/OptionsSubMultiplayer.res");

		if (ModInfo.NoModels()) {
			Panel? tempPanel;

			tempPanel = FindChildByName("Label1");
			tempPanel?.SetVisible(false);

			tempPanel = FindChildByName("Colors");
			tempPanel?.SetVisible(false);
		}
	}

	// void OnCommand(ReadOnlySpan<char> command) { }

	// void ConversionError(ConversionErrorType nError) { }

	void OnFileSelected(ReadOnlySpan<char> fullpath) { }

	void InitLogoList(LabeledCommandComboBox cb) {
		Span<char> directory = stackalloc char[512];

		ConVarRef cl_logofile = new("cl_logofile", true);
		if (!cl_logofile.IsValid())
			return;

		cb.DeleteAllItems();

		ReadOnlySpan<char> logoFile = cl_logofile.GetString();
		sprintf(directory, "materials/vgui/logos/*.vtf");

		ReadOnlySpan<char> fn = g_pFileSystem.FindFirstEx(directory.SliceNullTerminatedString(), null, out FileFindHandle_t fh);
		int i = 0, initialItem = 0;
		while (!fn.IsEmpty) {
			Span<char> filename = stackalloc char[512];
			sprintf(filename, "materials/vgui/logos/%s").S(fn);
			if (strlen(filename) >= 4) {
				filename[(int)strlen(filename) - 4] = '\0';
				strcat(filename, ".vmt");
				if (g_pFileSystem.FileExists(filename.SliceNullTerminatedString())) {
					strcpy(filename, fn);
					filename[(int)strlen(filename) - 4] = '\0';
					cb.AddItem(filename.SliceNullTerminatedString(), "");
					sprintf(filename, "materials/vgui/logos/%s").S(fn);

					if (stricmp(filename, logoFile) == 0)
						initialItem = i;

					i++;
				}
			}

			fn = g_pFileSystem.FindNext(fh);
		}

		g_pFileSystem.FindClose(fh);
		cb.SetInitialItem(initialItem);
	}

	void SelectLogo(ReadOnlySpan<char> logoName) { }

	void RemapLogo() {
		Span<char> logoName = stackalloc char[256];
		LogoList.GetText(logoName);

		if (logoName.IsEmpty)
			return;

		using ScopedPanelWaitCursor _ = new(this);

		g_pFileSystem.CreateDirHierarchy("materials/VGUI/logo/UI", "GAME");

		Span<char> fullLogoName = stackalloc char[512];
		sprintf(fullLogoName, "materials/VGUI/logo/UI/%s.vmt").S(logoName);

		if (!g_pFileSystem.FileExists(fullLogoName)) {
			using IFileHandle? fp = g_pFileSystem.Open(fullLogoName, FileOpenOptions.Read);
			if (fp == null)
				return;

			Span<char> data = stackalloc char[1024];
			sprintf(data, @"""UnlitGeneric""
{
	// Original shader: BaseTimesVertexColorAlphaBlendNoOverbright
	""$translucent"" 1
	""$basetexture"" ""VGUI/logos/%s""
	""$vertexcolor"" 1
	""$vertexalpha"" 1
	""$no_fullbright"" 1
	""$ignorez"" 1
}").S(logoName);

			// g_pFileSystem.Write(data, strlen(data), fp); // TODO!!
		}

		sprintf(fullLogoName, "logos/UI/%s").S(logoName);
		LogoImage.SetImage(fullLogoName);
	}

	public override void OnTextChanged(Panel sender) => RemapLogo();

	void OnControlModified() { }

	public override void OnResetData() {
		if (DownloadFilterCombo != null) {
			ConVarRef cl_downloadfilter = new("cl_downloadfilter");

			if (stricmp(cl_downloadfilter.GetString(), "none") == 0)
				DownloadFilterCombo.ActivateItem(3);
			else if (stricmp(cl_downloadfilter.GetString(), "nosounds") == 0)
				DownloadFilterCombo.ActivateItem(1);
			else if (stricmp(cl_downloadfilter.GetString(), "mapsonly") == 0)
				DownloadFilterCombo.ActivateItem(2);
			else
				DownloadFilterCombo.ActivateItem(0);
		}
	}

	public override void OnApplyChanges() {
		LogoList.ApplyChanges();
		LogoList.GetText(LogoName);

		foreach (CvarToggleCheckButton btn in CvarToggleCheckButtons) {
			if (btn.IsVisible() && btn.IsEnabled())
				btn.ApplyChanges();
		}

		ConVarRef cl_logofile = new("cl_logofile");
		ReadOnlySpan<char> value = "";
		if (LogoName[0] != '\0')
			value = $"materials/vgui/logos/{LogoName}.vtf";
		cl_logofile.SetValue(value);

		if (DownloadFilterCombo != null) {
			ConVarRef cl_downloadfilter = new("cl_downloadfilter");
			switch (DownloadFilterCombo.GetActiveItem()) {
				case 0:
					cl_downloadfilter.SetValue("all");
					break;
				case 1:
					cl_downloadfilter.SetValue("nosounds");
					break;
				case 2:
					cl_downloadfilter.SetValue("mapsonly");
					break;
				case 3:
					cl_downloadfilter.SetValue("none");
					break;
			}
		}
	}

	public override Panel? CreateControlByName(ReadOnlySpan<char> controlName) {
		if (stricmp("CCvarToggleCheckButton", controlName) == 0) {
			CvarToggleCheckButton newButton = new(this, controlName, "", "");
			CvarToggleCheckButtons.Add(newButton);
			return newButton;
		}
		return base.CreateControlByName(controlName);
	}
}