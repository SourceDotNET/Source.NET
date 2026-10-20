using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Utilities;

namespace Source.GUI.Controls;

public class BuildGroup
{
	public string? ResourceName;
	public string? ResourcePathID;
	public Panel? BuildContext;
	public Panel? CurrentPanel;
	public Panel? ParentPanel;
	public Panel? BuildDialog;

	bool Enabled;
	public BuildGroup(Panel? parentPanel, Panel? contextPanel) {
		ParentPanel = parentPanel;
		BuildContext = contextPanel;
	}

	public List<Panel> Panels = [];
	public List<UtlSymbol> RegisteredControlSettingsFiles = [];

	readonly static Dictionary<ulong, KeyValues?> DictCachedResFiles = [];
	public static void ClearResFileCache() {
		DictCachedResFiles.Clear();
	}

	static ConVar vgui_cache_res_files = new("1", 0);
	internal void LoadControlSettings(ReadOnlySpan<char> controlResourceName, ReadOnlySpan<char> pathID, KeyValues? preloadedKeyValues, KeyValues? conditions) {
		RegisterControlSettingsFile(controlResourceName, pathID);

		KeyValues? dat = preloadedKeyValues;

		bool usePrecaching = vgui_cache_res_files.GetBool();
		bool usingPrecachedSourceKeys = false;
		bool shouldCacheKeys = true;
		bool deleteKeys = false;

		if (dat != null && usePrecaching) {
			if (DictCachedResFiles.TryGetValue(controlResourceName.Hash(), out KeyValues? cachedData)) {
				dat = cachedData;
				usingPrecachedSourceKeys = true;
				deleteKeys = false;
				shouldCacheKeys = false;
			}
		}

		while (dat == null) {
			if (usePrecaching) {
				if (DictCachedResFiles.TryGetValue(controlResourceName.Hash(), out KeyValues? cachedData)) {
					dat = cachedData;
					usingPrecachedSourceKeys = true;
					deleteKeys = false;
					shouldCacheKeys = false;
					break;
				}
			}

			dat = new KeyValues(controlResourceName);

			bool bSuccess = false;
			if (pathID.IsEmpty)
				bSuccess = dat.LoadFromFile(fileSystem, controlResourceName, "SKIN");

			if (!bSuccess)
				bSuccess = dat.LoadFromFile(fileSystem, controlResourceName, pathID);

			if (bSuccess) {
				deleteKeys = true;
				shouldCacheKeys = true;
			}
			else
				Warning($"Failed to load {controlResourceName}\n");

			break;
		}

		if (conditions != null && conditions.GetFirstSubKey() != null) {
			if (usingPrecachedSourceKeys) {
				dat = dat!.MakeCopy();
				deleteKeys = true;
			}

			ProcessConditionalKeys(dat, conditions);
			shouldCacheKeys = false;
		}

		ResourceName = new(controlResourceName);

		if (!pathID.IsEmpty)
			ResourcePathID = new(pathID);

		DeleteAllControlsCreatedByControlSettingsFile();

		ApplySettings(dat!);

		if (ParentPanel != null) {
			ParentPanel.InvalidateLayout();
			ParentPanel.Repaint();
		}

		if (shouldCacheKeys && usePrecaching) {
			Assert(!DictCachedResFiles.ContainsKey(controlResourceName.Hash()));
			DictCachedResFiles[controlResourceName.Hash()] = dat;
		}
		else if (deleteKeys) {
			Assert(DictCachedResFiles.ContainsKey(controlResourceName.Hash()) || conditions != null || preloadedKeyValues != null);
			DictCachedResFiles[controlResourceName.Hash()] = null;
		}
	}

	public void ApplySettings(KeyValues resourceData) {
		for (KeyValues? controlKeys = resourceData.GetFirstSubKey(); controlKeys != null; controlKeys = controlKeys.GetNextKey()) {
			bool found = false;

			if (controlKeys.Type != KeyValues.Types.None)
				continue;

			ReadOnlySpan<char> keyName = controlKeys.Name;

			for (int i = 0; i < Panels.Count; i++) {
				Panel? panel = Panels[i];

				if (panel == null) {
					Panels.RemoveAt(i);
					--i;
					continue;
				}

				Assert(panel);

				ReadOnlySpan<char> panelName = panel.GetName();

				if (panelName.Equals(keyName, StringComparison.OrdinalIgnoreCase)) {
					panel.ApplySettings(controlKeys);
					found = true;
					break;
				}
			}

			if (!found && !keyName.IsEmpty)
				NewControl(controlKeys);
		}
	}

	private Panel? NewControl(KeyValues controlKeys, int x = 0, int y = 0) {
		Panel? newPanel;
		if (controlKeys != null) {
			KeyValues? keyVal = new KeyValues("ControlFactory", "ControlName", controlKeys.GetString("ControlName"));
			BuildContext!.RequestInfo(keyVal);
			newPanel = (Panel?)keyVal.GetPtr("PanelPtr");
		}
		else {
			return null;
		}

		if (newPanel != null) {
			newPanel.SetParent(ParentPanel);
			newPanel.SetBuildGroup(this);
			newPanel.SetPos(x, y);

			newPanel.SetName(controlKeys.Name);
			newPanel.ApplySettings(controlKeys);

			newPanel.AddActionSignalTarget(ParentPanel);
			newPanel.SetBuildModeEditable(true);
			newPanel.SetBuildModeDeletable(true);

			newPanel.SetAutoDelete(true);
		}
#if false //DEBUG
		else {
			Warning($"BuildGroup::NewControl Could not create panel '{controlKeys.Name}', '{controlKeys.GetString("ControlName")}'\n");
		}
#endif
		return newPanel;
	}

	private void DeleteAllControlsCreatedByControlSettingsFile() {
		for (int i = 1; i < Panels.Count; i++) {
			if (Panels[i] == null) {
				Panels.RemoveAt(i);
				--i;
				continue;
			}

			if (Panels[i].IsBuildModeDeletable()) {
				Panels[i].DeletePanel();
				Panels.RemoveAt(i);
				--i;
			}
		}

		CurrentPanel = BuildContext;
		CurrentPanel!.InvalidateLayout();
		BuildContext!.Repaint();
	}

	private void ProcessConditionalKeys(KeyValues? data, KeyValues conditions) {
		if (data != null) {
			KeyValues? subKey = data.GetFirstSubKey();
			if (subKey == null)
				return;

			for (; subKey != null; subKey = subKey.GetNextKey()) {
				ProcessConditionalKeys(subKey, conditions);

				KeyValues? cond = conditions.GetFirstSubKey();
				for (; cond != null; cond = cond.GetNextKey()) {
					KeyValues? conditionBlock = subKey.FindKey(cond.Name);
					if (conditionBlock != null) {
						KeyValues? overridingKey;
						for (overridingKey = conditionBlock.GetFirstSubKey(); overridingKey != null; overridingKey = overridingKey.GetNextKey()) {
							KeyValues? existingKey = subKey.FindKey(overridingKey.Name);
							if (existingKey != null)
								existingKey.SetStringValue(overridingKey.GetString());
							else {
								KeyValues copy = overridingKey.MakeCopy();
								subKey.AddSubKey(copy);
							}
						}
					}
				}
			}
		}
	}

	private void RegisterControlSettingsFile(ReadOnlySpan<char> controlResourceName, ReadOnlySpan<char> pathID) {
		UtlSymbol symbol = new(controlResourceName);

		if (!RegisteredControlSettingsFiles.Contains(symbol))
			RegisteredControlSettingsFiles.Add(symbol);
	}

	public int GetRegisteredControlSettingsFileCount() => RegisteredControlSettingsFiles.Count;

	public ReadOnlySpan<char> GetRegisteredControlSettingsFileByIndex(int index) {
		if (index < 0 || index >= RegisteredControlSettingsFiles.Count)
			return [];

		return RegisteredControlSettingsFiles[index].ToString();
	}

	public void PanelAdded(Panel panel) {
		if (!Panels.Contains(panel))
			Panels.Add(panel);
	}

	public void PanelRemoved(Panel panel) {
		Panels.Remove(panel);
	}

	public void SetEnabled(bool state) {
		if (Enabled != state) {
			Enabled = state;
			CurrentPanel = null;

			if (state)
				ActivateBuildDialog();
			else {
				BuildDialog?.OnCommand("Close");
				ParentPanel!.RequestFocus();
			}
		}
	}

	public Panel? GetCurrentPanel() => CurrentPanel;

	public Panel? CreateBuildDialog() {
		if (BuildContext == null)
			return null;

		Panel? buildDialog = null;
		KeyValues data = new KeyValues("BuildDialog");
		data.SetPtr("BuildGroupPtr", this);
		if (BuildContext.RequestInfo(data))
			buildDialog = (Panel?)data.GetPtr("PanelPtr");

		if (buildDialog != null)
			Input.ReleaseAppModalSurface();

		return buildDialog;
	}

	private void ActivateBuildDialog() {
		if (BuildDialog == null) {
			BuildDialog = CreateBuildDialog();

			if (BuildDialog == null)
				return;
		}

		BuildDialog.SetVisible(true);

		CurrentPanel = ParentPanel;
		KeyValues keyval = new KeyValues("SetActiveControl");
		keyval.SetPtr("PanelPtr", GetCurrentPanel());
		VGui.PostMessage(BuildDialog, keyval, GetCurrentPanel());
	}

	public bool IsEnabled() => Enabled;

	public Panel? GetContextPanel() => BuildContext;
}
