using CommunityToolkit.HighPerformance;

using Game.Client.GarrysMod;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod.Lua;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Engine;
using Source.GUI.Controls;

namespace Game.Client.HUD;

public class BaseHudChatLine : RichText
{
	protected IFont? Font;
	protected IFont? FontMarlett;
	Color TextColor;
	double ExpireTime;
	double StartTime;
	string? Text;
	int Count;
	protected List<TextRange> TextRanges = [];

	readonly ClientGlobalVariables gpGlobals = Singleton<ClientGlobalVariables>();

	public BaseHudChatLine(Panel? parent, string? name) : base(parent, name) {
		SetPaintBackgroundEnabled(true);
		SetVerticalScrollbar(true);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		Font = scheme.GetFont("Default");
		SetBgColor(new(0, 0, 0, 100));
		FontMarlett = scheme.GetFont("Marlett");
		TextColor = scheme.GetColor("FgColor", GetFgColor());
		SetFont(Font);
	}

	internal IFont? GetFont() {
		return Font;
	}

	public void SetExpireTime() {
		StartTime = gpGlobals.CurTime;
		ExpireTime = StartTime + BaseHudChat.hud_saytext_time.GetDouble();
		Count = BaseHudChat.LineCounter++;
	}

	int NameStart;
	int NameLength;
	Color NameColor;

	public int GetCount() => Count;
	readonly IEngineClient engine = Singleton<IEngineClient>();
	public bool IsReadyToExpire() {
		if (!engine.IsInGame() && !engine.IsConnected())
			return true;

		return gpGlobals.CurTime >= ExpireTime;
	}
	public double GetStartTime() => StartTime;
	public void Expire() {
		SetVisible(false);
	}

	internal void SetNameStart(int nameStart) => NameStart = nameStart;

	internal void SetNameLength(int nameLength) => NameLength = nameLength;

	internal void SetNameColor(Color clrNameColor) => NameColor = clrNameColor;
	internal void InsertAndColorizeText(ReadOnlySpan<char> buf, int playerIndex) {
		TextRanges.Clear();
		Text = new(buf);

		if (GetParent() is not BaseHudChat chat)
			return;

		int i = 0;
		int lineLen = Text.Length;

		TextColor tc = Text.Length > 0 ? (TextColor)Text[0] : 0;

		if (tc == HUD.TextColor.PlayerName || tc == HUD.TextColor.Location || tc == HUD.TextColor.Normal || tc == HUD.TextColor.Achievement || tc == HUD.TextColor.Custom || tc == HUD.TextColor.HexCode || tc == HUD.TextColor.HexCodeAlpha) {
			while (i < lineLen) {
				TextRange range = default;
				bool foundColorCode = false;
				bool done = false;
				int bytesIn = i;

				switch ((TextColor)Text[i]) {
					case HUD.TextColor.Custom:
					case HUD.TextColor.PlayerName:
					case HUD.TextColor.Location:
					case HUD.TextColor.Achievement:
					case HUD.TextColor.Normal:
						range.Start = bytesIn + 1;
						range.Color = chat.GetTextColorForClient((TextColor)Text[i], playerIndex);
						range.End = lineLen;
						foundColorCode = true;
						++i;
						break;
					case HUD.TextColor.HexCode:
					case HUD.TextColor.HexCodeAlpha:
						bool readAlpha = (TextColor)Text[i] == HUD.TextColor.HexCodeAlpha;
						int codeBytes = readAlpha ? 8 : 6;
						range.Start = bytesIn + codeBytes + 1;
						range.End = lineLen;
						range.PreserveAlpha = readAlpha;
						++i;

						if (range.End > range.Start) {
							int r = Text[i].Nibble() << 4 | Text[i + 1].Nibble();
							int g = Text[i + 2].Nibble() << 4 | Text[i + 3].Nibble();
							int b = Text[i + 4].Nibble() << 4 | Text[i + 5].Nibble();
							int a = readAlpha ? Text[i + 6].Nibble() << 4 | Text[i + 7].Nibble() : 255;

							range.Color = new(r, g, b, a);
							foundColorCode = true;

							i += codeBytes;
						}
						else
							done = true;
						break;
					default:
						++i;
						break;
				}

				if (done)
					break;

				if (foundColorCode) {
					int count = TextRanges.Count;
					if (count != 0) {
						TextRange r = TextRanges[count - 1];
						r.End = bytesIn;
						TextRanges[count - 1] = r;
					}

					TextRanges.Add(range);
				}
			}
		}

		if (TextRanges.Count == 0 && NameLength > 0 && Text[0] == (int)HUD.TextColor.UseOldColors) {
			TextRange range = default;
			range.Start = 0;
			range.End = NameStart;
			range.Color = chat.GetTextColorForClient(HUD.TextColor.Normal, playerIndex);
			TextRanges.Add(range);

			range.Start = NameStart;
			range.End = NameStart + NameLength;
			range.Color = chat.GetTextColorForClient(HUD.TextColor.PlayerName, playerIndex);
			TextRanges.Add(range);

			range.Start = range.End;
			range.End = Text.Length;
			range.Color = chat.GetTextColorForClient(HUD.TextColor.Normal, playerIndex);
			TextRanges.Add(range);
		}

		if (TextRanges.Count == 0) {
			TextRange range = default;
			range.Start = 0;
			range.End = Text.Length;
			range.Color = chat.GetTextColorForClient(HUD.TextColor.Normal, playerIndex);
			TextRanges.Add(range);
		}

		for (i = 0; i < TextRanges.Count; ++i) {
			ReadOnlySpan<char> start = Text.AsSpan()[TextRanges[i].Start..];
			if (start.Length > 0 && start[0] < (int)HUD.TextColor.Max) {
				Assert(start[0] != (int)HUD.TextColor.HexCode && start[0] != (int)HUD.TextColor.HexCodeAlpha);
				TextRange tr = TextRanges[i];
				tr.Start += 1;
				TextRanges[i] = tr;
			}
		}

		Colorize();
	}

	private void Colorize(int alpha = 255) {
		SetText("");

		BaseHudChat? chat = (BaseHudChat?)GetParent();

		if (chat != null && chat.GetChatHistory() != null)
			chat.GetChatHistory().InsertString("\n");

		Span<char> text = stackalloc char[4096];
		Color color = default;
		for (int i = 0; i < TextRanges.Count; ++i) {
			ReadOnlySpan<char> start = Text.AsSpan()[TextRanges[i].Start..];
			int len = TextRanges[i].End - TextRanges[i].Start + 1;
			if (len > 1 && len <= text.Length) {
				start.ClampedCopyTo(text);
				text[len - 1] = '\0';
				color = TextRanges[i].Color;
				if (!TextRanges[i].PreserveAlpha)
					color[3] = (byte)alpha;

				InsertColorChange(color);
				InsertString(text);

				if (chat != null && chat.GetChatHistory() != null) {
					chat.GetChatHistory().InsertColorChange(color);
					chat.GetChatHistory().InsertString(text);
					chat.GetChatHistory().InsertFade(BaseHudChat.hud_saytext_time.GetFloat(), BaseHudChat.CHAT_HISTORY_IDLE_FADE_TIME);

					if (i == TextRanges.Count - 1)
						chat.GetChatHistory().InsertFade(-1, -1);
				}
			}
		}

		InvalidateLayout(true);
	}
}

public class HudChatHistory : RichText
{
	readonly IEngineVGui enginevgui = Singleton<IEngineVGui>();
	public HudChatHistory(Panel? parent, string? name) : base(parent, "HudChatHistory") {
		IScheme scheme = SchemeManager.LoadSchemeFromFileEx(enginevgui.GetPanel(VGuiPanelType.ClientDll), "resource/ChatScheme.res", "ChatScheme")!;
		SetScheme(scheme);

		InsertFade(-1, -1);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetFont(scheme.GetFont("ChatFont"));
		SetAlpha(255);
	}
}

public class BaseHudChatInputLine : Panel
{
	protected Label Prompt;
	protected BaseHudChatEntry Input;
	public BaseHudChatInputLine(Panel? parent, ReadOnlySpan<char> panelName) : base(parent, panelName) {
		SetMouseInputEnabled(false);
		Prompt = new Label(this, "ChatInputPrompt", "Enter text:");
		Input = new BaseHudChatEntry(this, "ChatInput", parent);
		Input.SetMaximumCharCount(127);
	}
	public Label GetPrompt() => Prompt;
	public Panel GetInputPanel() => Input;
	public void GetMessageText(Span<char> outBuffer) {
		Input.GetText(outBuffer);
	}
	public override IPanel? GetCurrentKeyFocus() => Input;

	internal void ClearEntry() {
		SetEntry("");
	}

	public override void PerformLayout() {
		base.PerformLayout();

		GetSize(out int wide, out int tall);

		Prompt.GetContentSize(out int w, out int h);
		Prompt.SetBounds(0, 0, w, tall);
		Input.SetBounds(w + 2, 0, wide - w - 2, tall);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		IFont? font = scheme.GetFont("ChatFont");

		Prompt.SetFont(font);
		Input.SetFont(font);

		Input.SetFgColor(scheme.GetColor("Chat.TypingText", scheme.GetColor("Panel.FgColor", new(255, 255, 255, 255))));

		SetPaintBackgroundEnabled(true);
		Prompt.SetPaintBackgroundEnabled(true);
		Prompt.SetContentAlignment(Alignment.West);
		Prompt.SetTextInset(2, 0);

		Input.SetMouseInputEnabled(true);

		SetBgColor(new(0, 0, 0, 0));
	}

	public override void ApplySettings(KeyValues resourceData) {
		base.ApplySettings(resourceData);
	}

	internal void SetEntry(ReadOnlySpan<char> entry) {
		Input.SetText(entry);
	}

	internal void SetPrompt(ReadOnlySpan<char> prompt) {
		Prompt.SetText(prompt);
		InvalidateLayout();
	}
}

public class HudChatFilterButton : Button
{
	public HudChatFilterButton(Panel parent, string name, string text) : base(parent, name, text) {
	}
}

public class HudChatFilterPanel : EditablePanel
{
	public HudChatFilterPanel(Panel? parent, string? panelName) : base(parent, panelName) {

	}

	public override void ApplySchemeSettings(IScheme scheme) {
		LoadControlSettings("resource/ui/ChatFilters.res");

		base.ApplySchemeSettings(scheme);

		Color color = scheme.GetColor("DullWhite", GetBgColor());
		SetBgColor(color with { A = (byte)BaseHudChat.CHAT_HISTORY_ALPHA });

		SetFgColor(scheme.GetColor("Blank", GetFgColor()));
	}
}

public class BaseHudChatEntry : TextEntry
{
	Panel? HudChat;
	public BaseHudChatEntry(Panel? parent, string? name, Panel chat) : base(parent, name) {
		SetCatchEnterKey(true);
		SetAllowNonAsciiCharacters(true);
		// set draw language id
		HudChat = chat;
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetPaintBorderEnabled(false);
	}

	public override void OnKeyCodeTyped(ButtonCode code) {
		if (code == ButtonCode.KeyEnter || code == ButtonCode.KeyPadEnter || code == ButtonCode.KeyEscape) {
			if (code != ButtonCode.KeyEscape && HudChat != null)
				PostMessage(HudChat, new KeyValues("ChatEntrySend"));

			if (HudChat != null)
				PostMessage(HudChat, new KeyValues("ChatEntryStopMessageMode"));
		}
		else if (code == ButtonCode.KeyTab)
			return;
		else
			base.OnKeyCodeTyped(code);
	}
}

public enum ChatFilters
{
	None,
	JoinLeave = 0x000001,
	NameChange = 0x000002,
	PublicChat = 0x000004,
	ServerMsg = 0x000008,
	TeamChange = 0x000010,
	Achievement = 0x000020,
}

public enum TextColor
{
	Normal = 1,
	UseOldColors = 2,
	PlayerName = 3,
	Location = 4,
	Achievement = 5,
	Custom = 6,
	HexCode = 7,
	HexCodeAlpha = 8,
	Max
}

public struct TextRange
{
	public int Start;
	public int End;
	public Color Color;
	public bool PreserveAlpha;
}

public class BaseHudChat : EditableHudElement
{
	readonly IEngineClient engine = Singleton<IEngineClient>();
	public const int CHAT_INTERFACE_LINES = 6;
	public const int MAX_CHARS_PER_LINE = 128;

	public const float CHATLINE_NUM_FLASHES = 8;
	public const float CHATLINE_FLASH_TIME = 5;
	public const float CHATLINE_FADE_TIME = 1;

	public const float CHAT_HISTORY_FADE_TIME = 0.25f;
	public const float CHAT_HISTORY_IDLE_TIME = 15;
	public const float CHAT_HISTORY_IDLE_FADE_TIME = 2.5f;
	public const float CHAT_HISTORY_ALPHA = 127;

	internal static ConVar hud_saytext_time = new("12", 0);
	internal static ConVar cl_showtextmsg = new("1", 0, "Enable/disable text messages printing on the screen.");
	internal static ConVar cl_chatfilters = new("63", FCvar.ClientDLL | FCvar.Archive, "Stores the chat filter settings ");
	internal static ConVar cl_chatfilter_version = new("0", FCvar.ClientDLL | FCvar.Archive | FCvar.Hidden, "Stores the chat filter version");
	internal static ConVar cl_mute_all_comms = new("1", FCvar.Archive, "If 1, then all communications from a player will be blocked when that player is muted, including chat messages.");

	public static readonly Color ColorBlue = new(153, 204, 255, 255);
	public static readonly Color ColorRed = new(255, 63, 63, 255);
	public static readonly Color ColorGreen = new(153, 255, 153, 255);
	public static readonly Color ColorDarkGreen = new(64, 255, 64, 255);
	public static readonly Color ColorYellow = new(255, 178, 0, 255);
	public static readonly Color ColorGrey = new(204, 204, 204, 255);

	protected BaseHudChatLine? FindUnusedChatLine() {
		return ChatLine;
	}

	protected BaseHudChatInputLine ChatInput;
	protected BaseHudChatLine ChatLine;
	protected int FontHeight;
	protected HudChatHistory ChatHistory;
	protected HudChatFilterButton FiltersButton;
	protected HudChatFilterPanel FilterPanel;
	protected Color ColorCustom;
	public static int LineCounter;

	int ComputeBreakChar(int width, ReadOnlySpan<char> text) {
		BaseHudChatLine line = ChatLine;
		IFont? font = line.GetFont();

		int currentlen = 0;
		int lastbreak = text.Length;
		for (int i = 0; i < text.Length; i++) {
			char ch = text[i];

			if (ch <= 32)
				lastbreak = i;

			Surface.GetCharABCwide(font, ch, out int a, out int b, out int c);
			currentlen += a + b + c;

			if (currentlen >= width) {
				if (lastbreak == text.Length)
					lastbreak = Math.Max(0, i - 1);

				break;
			}
		}

		if (currentlen >= width)
			return lastbreak;

		return text.Length;
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		LoadControlSettings("resource/ui/BaseChat.res");
		base.ApplySchemeSettings(scheme);

		SetPaintBackgroundType(PaintBackgroundType.Box);
		SetPaintBorderEnabled(true);
		SetPaintBackgroundEnabled(true);

		SetKeyboardInputEnabled(false);
		SetMouseInputEnabled(false);

		VisibleHeight = 0;

		Color dullWhite = scheme.GetColor("DullWhite", GetBgColor());
		SetBgColor(dullWhite with { A = (byte)CHAT_HISTORY_ALPHA });

		GetChatHistory().SetVerticalScrollbar(false);
	}

	MessageModeType MessageMode;
	int VisibleHeight;
	IFont? ChatFont;
	ChatFilters FilterFlags;
	bool EnteringVoice;
	double HistoryFadeTime;
	double HistoryIdleTime;

	readonly IEngineVGui EngineVGui = Singleton<IEngineVGui>();
	readonly ClientGlobalVariables gpGlobals = Singleton<ClientGlobalVariables>();

	public BaseHudChat(string? elementName) : base("HudChat", elementName) {
		Panel parent = clientMode.GetViewport();
		SetParent(parent);

		IScheme? scheme = SchemeManager.LoadSchemeFromFileEx(EngineVGui.GetPanel(VGuiPanelType.ClientDll), "resource/ChatScheme.res", "ChatScheme");
		SetScheme(scheme);

		Localize.AddFile("resource/chat_%language%.txt");

		MessageMode = 0;
		VGui.AddTickSignal(this);

		MakePopup();
		SetZPos(-30);

		HiddenBits = HideHudBits.Chat;

		FiltersButton = new HudChatFilterButton(this, "ChatFiltersButton", "Filters");

		FiltersButton.SetScheme(scheme);
		FiltersButton.SetVisible(true);
		FiltersButton.SetEnabled(true);
		FiltersButton.SetMouseInputEnabled(true);
		FiltersButton.SetKeyboardInputEnabled(false);

		ChatHistory = new HudChatHistory(this, "HudChatHistory");

		CreateChatLines();
		CreateChatInputLine();
		GetChatFilterPanel();

		FilterFlags = (ChatFilters)cl_chatfilters.GetInt();
	}

	public override void SetVisible(bool state) {
		base.SetVisible(state);
	}

	public void Reset() {
		VisibleHeight = 0;
		Clear();
	}

	public void Clear() {

	}

	public override void OnParentChanged(IPanel? oldParent, IPanel? newParent) {
		base.OnParentChanged(oldParent, newParent);
	}

	public override void OnTick() {
		VisibleHeight = 0;

		BaseHudChatLine? line = ChatLine;

		if (line != null) {
			IFont? font = line.GetFont();
			FontHeight = Surface.GetFontTall(font) + 2;
			ChatInput.GetBounds(out int iInputX, out int iInputY, out int iInputW, out int iInputH);
			GetBounds(out int iChatX, out int iChatY, out int iChatW, out int iChatH);
			ChatInput.SetBounds(iInputX, (int)(iChatH - (FontHeight * 1.75f)), iInputW, FontHeight);
			GetChatHistory().GetBounds(out int iChatHistoryX, out int iChatHistoryY, out int iChatHistoryW, out int iChatHistoryH);
			iChatHistoryH = (iChatH - (FontHeight * 9 / 4)) - iChatHistoryY;
			GetChatHistory().SetBounds(iChatHistoryX, iChatHistoryY, iChatHistoryW, iChatHistoryH);
		}

		FadeChatHistory();
	}

	private void FadeChatHistory() {
		float frac = (float)((HistoryFadeTime - gpGlobals.CurTime) / CHAT_HISTORY_FADE_TIME);

		int alpha = (int)(frac * CHAT_HISTORY_ALPHA);
		alpha = Math.Clamp(alpha, 0, (int)CHAT_HISTORY_ALPHA);

		if (alpha >= 0) {
			if (GetChatHistory() != null) {
				if (IsMouseInputEnabled()) {
					SetAlpha(255);
					GetChatHistory().SetBgColor(new(0, 0, 0, (int)(CHAT_HISTORY_ALPHA - alpha)));
					ChatInput.GetPrompt().SetAlpha((CHAT_HISTORY_ALPHA * 2) - alpha);
					ChatInput.GetInputPanel().SetAlpha((CHAT_HISTORY_ALPHA * 2) - alpha);
					SetBgColor(GetBgColor() with { A = (byte)(CHAT_HISTORY_ALPHA - alpha) });
					FiltersButton.SetAlpha((CHAT_HISTORY_ALPHA * 2) - alpha);
				}
				else {
					GetChatHistory().SetBgColor(new(0, 0, 0, alpha));
					SetBgColor(GetBgColor() with { A = (byte)alpha });
					ChatInput.GetPrompt().SetAlpha(alpha);
					ChatInput.GetInputPanel().SetAlpha(alpha);
					FiltersButton.SetAlpha(alpha);
				}
			}
		}
	}

	public override void Paint() {
		if (VisibleHeight == 0)
			return;
	}

	private void CreateChatLines() {
		ChatLine = new BaseHudChatLine(this, "ChatLine1");
		ChatLine.SetVisible(false);
	}

	private void CreateChatInputLine() {
		ChatInput = new BaseHudChatInputLine(this, "ChatInputLine");
		ChatInput.SetVisible(false);

		if (GetChatHistory() != null) {
			GetChatHistory().SetMaximumCharCount(127 * 100);
			GetChatHistory().SetVisible(true);
		}
	}

	private HudChatFilterPanel GetChatFilterPanel() {
		if (FilterPanel == null) {
			FilterPanel = new HudChatFilterPanel(this, "HudChatFilterPanel");

			IScheme? scheme = SchemeManager.LoadSchemeFromFileEx(EngineVGui.GetPanel(VGuiPanelType.ClientDll), "resource/ChatScheme.res", "ChatScheme");

			FilterPanel.SetScheme(scheme);
			FilterPanel.InvalidateLayout(true, true);
			FilterPanel.SetMouseInputEnabled(true);
			FilterPanel.SetPaintBackgroundType(PaintBackgroundType.Box);
			FilterPanel.SetPaintBorderEnabled(true);
			FilterPanel.SetVisible(false);
		}

		return FilterPanel;
	}

	protected void SayText(bf_read msg) {
		Span<char> str = stackalloc char[256];

		int client = msg.ReadByte();
		str = str[..msg.ReadString(str)];
		bool wantsToChat = msg.ReadByte() != 0;
#if GMOD_DLL
		bool teamChat = msg.ReadByte() != 0;
		bool isDead = msg.ReadByte() != 0;

		C_BasePlayer? player = Util.PlayerByIndex(client);
		if (wantsToChat && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.OnPlayerChat)) {
			LuaEntity.Push_Entity(player);
			g_Lua!.PushString(str);
			g_Lua.PushBool(teamChat);
			g_Lua.PushBool(isDead);
			if (gGM.CallFinish(4))
				return;
		}

		if (wantsToChat)
			ChatPrintf(client, ChatFilters.PublicChat, str);
#else
		if (wantsToChat)
			ChatPrintf(client, ChatFilters.None, str);
#endif
		else
			Printf(ChatFilters.None, str);
	}

	public HudChatHistory GetChatHistory() => ChatHistory;

	public void Printf(ChatFilters filter, ReadOnlySpan<char> str) {
		ChatPrintf(0, filter, str);
	}

	public ChatFilters GetFilterFlags() => FilterFlags;

	private void ChatPrintf(int playerIndex, ChatFilters filter, ReadOnlySpan<char> str) {
		ReadOnlySpan<char> trimmed = str;

		trimmed = trimmed.TrimEnd('\n');
		while (trimmed.Length > 0 && trimmed[0] != '\0' && (trimmed[0] == '\n' || (trimmed[0] > 0 && trimmed[0] < (int)HUD.TextColor.Max)))
			trimmed = trimmed[1..];
		trimmed.TrimStart('\n');

		BaseHudChatLine? line = FindUnusedChatLine();
		if (line == null)
			return;

		if (filter != ChatFilters.None)
			if ((filter & GetFilterFlags()) == 0) return;

		line.SetText("");

		PlayerInfo playerInfo = new();
		if (playerIndex == 0)
			strcpy(playerInfo.Name, "Console");
		else
			engine.GetPlayerInfo(playerIndex, out playerInfo);

#if GMOD_DLL
		if (g_Lua != null && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.ChatText)) {
			g_Lua.PushNumber(playerIndex);
			g_Lua.PushString(((ReadOnlySpan<char>)playerInfo.Name).SliceNullTerminatedString());
			g_Lua.PushString(trimmed);
			g_Lua.PushString(filter switch {
				ChatFilters.PublicChat => "chat",
				ChatFilters.JoinLeave => "joinleave",
				ChatFilters.NameChange => "namechange",
				ChatFilters.ServerMsg => "servermsg",
				ChatFilters.TeamChange => "teamchange",
				_ => "none"
			});
			if (gGM.CallFinish(4))
				return;
		}
#endif

		int nameStart = 0;
		int nameLength = 0;

		Color clrNameColor = GetClientColor(playerIndex);

		ReadOnlySpan<char> playerName = ((ReadOnlySpan<char>)playerInfo.Name).SliceNullTerminatedString();
		ReadOnlySpan<char> nameInString = strstr(trimmed, playerName);
		if (!playerName.IsEmpty && !nameInString.IsEmpty) {
			nameStart = trimmed.Length - nameInString.Length;
			nameLength = playerName.Length;
		}

		line.SetExpireTime();

		line.SetVisible(false);
		line.SetNameStart(nameStart);
		line.SetNameLength(nameLength);
		line.SetNameColor(clrNameColor);
		line.InsertAndColorizeText(trimmed, playerIndex);

#if GMOD_DLL
		// todo ChatHistory
#endif

		Msg($"{trimmed}\n");
	}

	public virtual Color GetClientColor(int clientIndex) {
#if GMOD_DLL
		if (clientIndex != 0 && g_pPlayerResource != null)
			return g_pPlayerResource.GetTeamColor(clientIndex);
		return new(204, 204, 204, 235);
#else
		if (clientIndex == 0)
			return ColorGreen;
		else
			return ColorYellow;
#endif
	}

	protected void TextMsg(bf_read msg) {

	}

	internal void StartMessageMode(MessageModeType messageModeType) {
		MessageMode = messageModeType;
		ChatInput.ClearEntry();

		ReadOnlySpan<char> prompt = MessageMode == MessageModeType.Say ? Localize.Find("#chat_say") : Localize.Find("#chat_say_team");
		if (!prompt.IsEmpty)
			ChatInput.SetPrompt(prompt);
		else
			ChatInput.SetPrompt(MessageMode == MessageModeType.Say ? "Say : " : "Say (TEAM) :");

		if (GetChatHistory() != null) {
			GetChatHistory().SetMouseInputEnabled(true);
			GetChatHistory().SetKeyboardInputEnabled(false);
			GetChatHistory().SetVerticalScrollbar(true);
			GetChatHistory().ResetAllFades(true);
			GetChatHistory().SetPaintBorderEnabled(true);
			GetChatHistory().SetVisible(true);
		}

		MakeReadyForUse();
		SetKeyboardInputEnabled(true);
		SetMouseInputEnabled(true);
		ChatInput.SetVisible(true);
		Surface.CalculateMouseVisible();
		ChatInput.RequestFocus();
		ChatInput.SetPaintBorderEnabled(true);
		ChatInput.SetMouseInputEnabled(true);

		GetChatHistory().GetBounds(out int x, out int y, out int w, out int h);
		vguiInput.SetCursorPos(x + (w / 2), y + (h / 2));

		HistoryFadeTime = gpGlobals.CurTime + CHAT_HISTORY_FADE_TIME;

		FilterPanel.SetVisible(false);

		engine.ClientCmd_Unrestricted("gameui_preventescapetoshow\n");
	}

	public void Send() {
		Span<char> text = stackalloc char[128];
		ChatInput.GetMessageText(text);
		if (text.Length > 0 && text[^1] == '\n')
			text[^1] = '\0';

		text = text.SliceNullTerminatedString();
		Span<char> buf = stackalloc char[144];
		int writePtr = 0;
		string cmd = (MessageMode == MessageModeType.Say ? "say " : "say_team ");
		cmd.CopyTo(buf[writePtr..]); writePtr += cmd.Length;
		text.CopyTo(buf[writePtr..]); writePtr += text.Length;
		engine.ClientCmd_Unrestricted(buf[..writePtr]);
	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		switch (message.Name) {
			case "ChatEntryStopMessageMode": StopMessageMode(); break;
			case "ChatEntrySend": Send(); break;
			default: base.OnMessage(message, sender); break;
		}
	}

	public void StopMessageMode() {
		engine.ClientCmd_Unrestricted("gameui_allowescapetoshow\n");

		SetKeyboardInputEnabled(false);
		SetMouseInputEnabled(false);

		if (GetChatHistory() != null) {
			GetChatHistory().SetPaintBorderEnabled(false);
			GetChatHistory().GotoTextEnd();
			GetChatHistory().SetMouseInputEnabled(false);
			GetChatHistory().SetVerticalScrollbar(false);
			GetChatHistory().ResetAllFades(false, true, CHAT_HISTORY_FADE_TIME);
			GetChatHistory().SelectNoText();
		}

		ChatInput.ClearEntry();
		FilterPanel.SetVisible(false);
		HistoryFadeTime = gpGlobals.CurTime + CHAT_HISTORY_FADE_TIME;
		MessageMode = MessageModeType.None;
	}

	public MessageModeType GetMessageMode() => MessageMode;

#if GMOD_DLL
	public void AddText() {
		BaseHudChatLine? line = ChatLine;
		if (line == null)
			return;

		line.SetText("");
		Color color = new(150, 210, 255, 255);
		ChatHistory.InsertColorChange(color);
		line.InsertColorChange(color);

		int top = g_Lua!.Top();
		for (int i = 1; i <= top; i++) {
			LuaObject arg = new(i, LuaType.None);

			if (arg.isString() || arg.isNumber()) {
				line.InsertString(arg.GetString());
				ChatHistory.InsertString(arg.GetString());
				cvar.ConsoleColorPrintf(color, "%s", arg.GetString());
				ChatHistory.InsertFade(hud_saytext_time.GetFloat(), 2.5f);
			}

			if (arg.isTable()) {
				color = new((byte)arg.GetMemberInt("r", 255), (byte)arg.GetMemberInt("g", 255), (byte)arg.GetMemberInt("b", 255), 255);
				line.InsertColorChange(color);
				ChatHistory.InsertColorChange(color);
			}

			if (arg.isUserData() && arg.GetType() == LuaType.Entity) {
				C_BaseEntity? ent = LuaEntity.Get_Entity(i, true);
				if (ent == null) {
					line.InsertString("NULL");
					ChatHistory.InsertString("NULL");
					ChatHistory.InsertFade(hud_saytext_time.GetFloat(), 2.5f);
					cvar.ConsoleColorPrintf(color, "%s", "NULL");
				}
				else if (!ent.IsPlayer()) {
					line.InsertString(ent.GetClassname());
					ChatHistory.InsertString(ent.GetClassname());
					cvar.ConsoleColorPrintf(color, "%s", ent.GetClassname().ToString());
					ChatHistory.InsertFade(hud_saytext_time.GetFloat(), 2.5f);
				}
				else {
					C_BasePlayer player = (C_BasePlayer)ent;
					Color playerColor = GetClientColor(player.EntIndex());
					line.InsertColorChange(playerColor);
					ChatHistory.InsertColorChange(playerColor);
					line.InsertString(player.GetPlayerName());
					ChatHistory.InsertString(player.GetPlayerName());
					cvar.ConsoleColorPrintf(playerColor, "%s", player.GetPlayerName().ToString());
					ChatHistory.InsertFade(hud_saytext_time.GetFloat(), 2.5f);
					line.InsertColorChange(color);
					ChatHistory.InsertColorChange(color);
				}
			}

			arg.UnReference();
		}

		ChatHistory.InsertString("\n");
		ChatHistory.InsertFade(hud_saytext_time.GetFloat(), 2.5f);
		ChatHistory.InsertFade(-1, -1);
		Msg("\n");
	}
#endif

	public Color GetTextColorForClient(TextColor tcCur, int playerIndex) {
		Color c;
		switch (tcCur) {
			case TextColor.Custom:
				c = ColorCustom;
				break;

			case TextColor.PlayerName:
				c = GetClientColor(playerIndex);
				break;

			case TextColor.Location:
				c = ColorDarkGreen;
				break;

			case TextColor.Achievement:
				IScheme? sourceScheme = SchemeManager.GetScheme("SourceScheme");
				c = sourceScheme?.GetColor("SteamLightGreen", GetBgColor()) ?? GetDefaultTextColor();
				break;

			default:
				c = GetDefaultTextColor();
				break;
		}

		return c with { A = 255 };
	}

	private Color GetDefaultTextColor() {
		return ColorYellow;
	}
}

[DeclareHudElement(Name = "CHudChat")]
public class HudChat : BaseHudChat
{
	public HudChat(string? panelName) : base(panelName) {

	}
	public override void Init() {
		base.Init();

		IHudElement.HookMessage("SayText", SayText);
		IHudElement.HookMessage("TextMsg", TextMsg);
	}
	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
	}
}
