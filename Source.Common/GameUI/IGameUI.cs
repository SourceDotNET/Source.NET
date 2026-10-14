using Source.Common.Engine;
using Source.Common.GUI;

namespace Source.Common.GameUI;

public enum SteamLoginFailure : byte
{
	None,
	BadTicket,
	NoSteamLogin,
	VacBanned,
	LoggedInElseWhere
}

public interface IGameUI
{
	public void Initialize(IEngineAPI engineAPI);
	public void PostInit();
	public void Start();
	public void Shutdown();
	public void RunFrame();
	public void OnGameUIActivated();
	public void OnGameUIHidden();
	public void OnLevelLoadingStarted(bool showProgressDialog);
	public void OnLevelLoadingFinished(bool error, ReadOnlySpan<char> failureReason, ReadOnlySpan<char> extendedReason);

	public bool UpdateProgressBar(float progress, ReadOnlySpan<char> statusText);
	public bool SetShowProgressText(bool show);

	public void OnConnectToServer(ReadOnlySpan<char> game, int ip, int connectionPort, int queryPort);
	public void OnDisconnectFromServer(SteamLoginFailure steamLoginFailure);
	public void OnConfirmQuit();
	public bool IsMainMenuVisible();

	public void SetMainMenuOverride(IPanel? panel);
	bool IsInLevel();
	bool IsInReplay();
	bool IsConsoleUI();
	bool HasSavedThisMenuSession();
	bool IsInBackgroundLevel();
	bool HasLoadingBackgroundDialog();
	void ShowLoadingBackgroundDialog();
	void HideLoadingBackgroundDialog();
	void SetLoadingBackgroundDialog(IPanel? panel);
	void PreventEngineHideGameUI();
	void AllowEngineHideGameUI();
	void HideGameUI();
}