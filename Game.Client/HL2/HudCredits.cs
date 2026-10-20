using CommunityToolkit.HighPerformance;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Mathematics;
using Source.GUI.Controls;

namespace Game.Client.HUD;

public struct CreditName
{
	public InlineArray256<char> Name;
	public InlineArray256<char> FontName;
	public float YPos;
	public float XPos;
	public bool Active;
	public TimeUnit_t Time;
	public TimeUnit_t TimeAdd;
	public TimeUnit_t TimeStart;
	public int Slot;
}

public enum LogoState
{
	FadeIn,
	FadeHold,
	FadeOut,
	FadeOff
}

public enum CreditsType
{
	Logo = 1,
	Intro = 2,
	Outro = 3
}

[DeclareHudElement(Name = "HudCredits")]
public class HudCredits : Panel, IHudElement
{
	public const string CREDITS_FILE = "scripts/credits.txt";

	public static bool g_bRollingCredits = false;
	public static int g_iCreditsPixelHeight = 0;

	public string? ElementName { get; set; }
	public HideHudBits HiddenBits { get; set; }
	public bool Active { get; set; }
	public bool NeedsRemove { get; set; }
	public bool IsParentedToClientDLLRootPanel { get; set; }
	public List<int> HudRenderGroups { get; set; } = [];

	public HudCredits(string elementName) : base(null, "HudCredits") {
		((IHudElement)this).Ctor(elementName);
		Panel? parent = clientMode.GetViewport();
		SetParent(parent);
	}
	public void Init() {
		usermessages.HookMessage("CreditsMsg", MsgFunc_CreditsMsg);
		usermessages.HookMessage("LogoTimeMsg", MsgFunc_LogoTimeMsg);
		((IHudElement)this).SetActive(false);
	}

	public void LevelShutdown() {
		Clear();
	}
	public int GetStringPixelWidth(ReadOnlySpan<char> str, IFont hFont) {
		int iLength = 0;

		for (ReadOnlySpan<char> wch = str; !wch.IsStringEmpty; wch = wch[1..])
			iLength += surface.GetCharacterWidth(hFont, wch[0]);

		return iLength;
	}
	public void MsgFunc_CreditsMsg(bf_read msg) {
		CreditsType = (CreditsType)msg.ReadByte();

		switch (CreditsType) {
			case CreditsType.Logo: PrepareLogo(5.0f); break;
			case CreditsType.Intro: PrepareIntroCredits(); break;
			case CreditsType.Outro: PrepareOutroCredits(); break;
		}
	}
	public void MsgFunc_LogoTimeMsg(bf_read msg) {
		CreditsType = CreditsType.Logo;
		PrepareLogo(msg.ReadFloat());
	}

	public bool ShouldDraw() {
		g_bRollingCredits = ((IHudElement)this).IsActive();

		if (g_bRollingCredits && CreditsType == CreditsType.Intro)
			g_bRollingCredits = false;

		return ((IHudElement)this).IsActive();
	}

	public override void Paint() {
		switch (CreditsType) {
			case CreditsType.Logo: DrawLogo(); break;
			case CreditsType.Intro: DrawIntroCreditsName(); break;
			case CreditsType.Outro: DrawOutroCreditsName(); break;
		}
	}
	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetVisible(ShouldDraw());
		SetBgColor(new Color(0, 0, 0, 0));
	}

	private void Clear() {
		((IHudElement)this).SetActive(false);
		CreditsList.Clear();
		LastOneInPlace = false;
		Alpha = TextColor[3];
		LogoState = LogoState.FadeOff;
	}

	private void ReadNames(KeyValues? keyValue) {
		if (keyValue == null) {
			AssertMsg(false, "HudCredits couldn't be initialized!");
			return;
		}

		// Now try and parse out each act busy anim
		KeyValues? kvNames = keyValue.GetFirstSubKey();

		while (kvNames != null) {
			CreditName Credits = default;
			strcpy(Credits.Name, kvNames.Name);
			strcpy(Credits.FontName, keyValue.GetString(kvNames.Name, "Default"));

			CreditsList.Add(Credits);
			kvNames = kvNames.GetNextKey();
		}
	}
	private void ReadParams(KeyValues? keyValue) {
		if (keyValue == null) {
			AssertMsg(false, "CHudCredits couldn't be initialized!");
			return;
		}

		ScrollTime = keyValue.GetFloat("scrolltime", 57);
		Separation = keyValue.GetFloat("separation", 5);

		FadeInTime = keyValue.GetFloat("fadeintime", 1);
		FadeHoldTime = keyValue.GetFloat("fadeholdtime", 3);
		FadeOutTime = keyValue.GetFloat("fadeouttime", 2);
		NextStartTime = keyValue.GetFloat("nextfadetime", 2);
		PauseBetweenWaves = keyValue.GetFloat("pausebetweenwaves", 2);

		LogoTimeMod = keyValue.GetFloat("logotime", 2);

		X = keyValue.GetFloat("posx", 2);
		Y = keyValue.GetFloat("posy", 2);

		Color = keyValue.GetColor("color");

		strcpy(Logo, keyValue.GetString("logo", "HALF-LIFE'"));
		strcpy(Logo2, keyValue.GetString("logo2", ""));
	}
	private void PrepareCredits(ReadOnlySpan<char> keyName) {
		Clear();

		KeyValues kv = new KeyValues("CreditsFile");
		if (!kv.LoadFromFile(filesystem, CREDITS_FILE, "MOD")) {
			AssertMsg(false, "env_credits couldn't be initialized!");
			return;
		}

		KeyValues? kvSubkey;
		if (!keyName.IsEmpty) {
			kvSubkey = kv.FindKey(keyName);
			ReadNames(kvSubkey);
		}

		kvSubkey = kv.FindKey("CreditsParams");
		ReadParams(kvSubkey);
	}
	private void DrawOutroCreditsName() {
		if (CreditsList.Count == 0)
			return;

		// fill the screen
		int iWidth, iTall;
		GetHudSize(out iWidth, out iTall);
		SetSize(iWidth, iTall);

		Span<char> unicode = stackalloc char[256];

		for (int i = 0; i < CreditsList.Count; i++) {
			ref CreditName credit = ref CreditsList.AsSpan()[i];

			IScheme scheme = SchemeManager.GetScheme("ClientScheme");
			IFont m_hTFont = scheme.GetFont(credit.FontName, true)!;

			int iFontTall = surface.GetFontTall(m_hTFont);

			if (credit.YPos < -iFontTall || credit.YPos > iTall) 
				credit.Active = false;
			else 
				credit.Active = true;
			

			Color color = TextColor;

			//HACKHACK
			//Last one stays on screen and fades out
			if (i == CreditsList.Count - 1) {
				if (LastOneInPlace == false) {
					credit.YPos -= (float)(gpGlobals.FrameTime * (g_iCreditsPixelHeight / ScrollTime));

					if ((int)credit.YPos + (iFontTall / 2) <= iTall / 2) {
						LastOneInPlace = true;

						// 360 certification requires that we not hold a static image too long.
						FadeTime = gpGlobals.CurTime + 10.0f;
					}
				}
				else {
					if (FadeTime <= gpGlobals.CurTime) {
						if (Alpha > 0) {
							Alpha = (int)(Alpha - gpGlobals.FrameTime * (ScrollTime * 2));

							if (Alpha <= 0) {
								credit.Active = false;
								engine.ClientCmd("creditsdone");
							}
						}
					}

					color[3] = (byte)Math.Max(0, Alpha);
				}
			}
			else 
				credit.YPos -= (float)(gpGlobals.FrameTime * (g_iCreditsPixelHeight / ScrollTime));

			if (credit.Active == false)
				continue;

			surface.DrawSetTextFont(m_hTFont);
			surface.DrawSetTextColor(color[0], color[1], color[2], color[3]);

			unicode.Clear();

			if (credit.Name[0] == '#')
				localize.ConstructString(unicode, localize.Find(credit.Name));
			else
				strcpy(unicode, credit.Name);

			int iStringWidth = GetStringPixelWidth(unicode, m_hTFont);

			surface.DrawSetTextPos((iWidth / 2) - (iStringWidth / 2), (int)credit.YPos);
			surface.DrawString(unicode.SliceNullTerminatedString());
		}
	}
	private void DrawIntroCreditsName() {
		if (CreditsList.Count == 0)
			return;

		// fill the screen
		int iWidth, iTall;
		GetHudSize(out iWidth, out iTall);
		SetSize(iWidth, iTall);

		for (int i = 0; i < CreditsList.Count; i++) {
			ref CreditName credit = ref CreditsList.AsSpan()[i];

			if (credit.Active == false)
				continue;

			IScheme scheme = SchemeManager.GetScheme("ClientScheme");
			IFont m_hTFont = scheme.GetFont(credit.FontName)!;

			TimeUnit_t localTime = gpGlobals.CurTime - credit.TimeStart;

			surface.DrawSetTextFont(m_hTFont);
			surface.DrawSetTextColor(Color[0], Color[1], Color[2], (int)(FadeBlend(FadeInTime, FadeOutTime, FadeHoldTime + credit.TimeAdd, localTime) * Color[3]));

			surface.DrawSetTextPos((int)XRES(credit.XPos), (int)YRES(credit.YPos));
			surface.DrawString(((ReadOnlySpan<char>)credit.Name).SliceNullTerminatedString());

			if (LogoTime > gpGlobals.CurTime)
				continue;

			if (credit.Time - NextStartTime <= gpGlobals.CurTime) {
				if (CreditsList.IsValidIndex(i + 3)) {
					ref CreditName pNextCredits = ref CreditsList.AsSpan()[i + 3];

					if (pNextCredits.Time == 0.0f) {
						pNextCredits.Active = true;

						if (i < 3) {
							pNextCredits.TimeAdd = (i + 1.0f);
							pNextCredits.Time = gpGlobals.CurTime + FadeInTime + FadeOutTime + FadeHoldTime + pNextCredits.TimeAdd;
						}
						else {
							pNextCredits.TimeAdd = PauseBetweenWaves;
							pNextCredits.Time = gpGlobals.CurTime + FadeInTime + FadeOutTime + FadeHoldTime + pNextCredits.TimeAdd;
						}

						pNextCredits.TimeStart = gpGlobals.CurTime;

						pNextCredits.Slot = credit.Slot;
					}
				}
			}

			if (credit.Time <= gpGlobals.CurTime) {
				credit.Active = false;

				if (i == CreditsList.Count - 1) {
					Clear();
				}
			}
		}
	}
	private void DrawLogo() {
		if (LogoState == LogoState.FadeOff) {
			((IHudElement)this).SetActive(false);
			return;
		}

		switch (LogoState) {
			case LogoState.FadeIn: {
					TimeUnit_t flDeltaTime = (FadeTime - gpGlobals.CurTime);

					Alpha = (int)Math.Max(0, MathLib.RemapValClamped(flDeltaTime, 5.0f, 0, -128, 255));

					if (flDeltaTime <= 0.0f) {
						LogoState = LogoState.FadeHold;
						FadeTime = gpGlobals.CurTime + LogoDesiredLength;
					}

					break;
				}

			case LogoState.FadeHold: {
					if (FadeTime <= gpGlobals.CurTime) {
						LogoState = LogoState.FadeOut;
						FadeTime = gpGlobals.CurTime + 2.0f;
					}
					break;
				}

			case LogoState.FadeOut: {
					TimeUnit_t flDeltaTime = (FadeTime - gpGlobals.CurTime);

					Alpha = (int)MathLib.RemapValClamped(flDeltaTime, 0.0f, 2.0f, 0, 255);

					if (flDeltaTime <= 0.0f) {
						LogoState = LogoState.FadeOff;
						((IHudElement)this).SetActive(false);
					}

					break;
				}
		}

		int iWidth, iTall;
		GetHudSize(out iWidth, out iTall);
		SetSize(iWidth, iTall);

		string szLogoFont;

		if (hl2_episodic.GetBool())
			szLogoFont = "ClientTitleFont";
		else
			szLogoFont = "WeaponIcons";

		IScheme scheme = SchemeManager.GetScheme("ClientScheme")!;
		IFont m_hTFont = scheme.GetFont(szLogoFont)!;

		int iFontTall = surface.GetFontTall(m_hTFont);

		Color cColor = TextColor;
		cColor[3] = (byte)Alpha;

		surface.DrawSetTextFont(m_hTFont);
		surface.DrawSetTextColor(cColor[0], cColor[1], cColor[2], cColor[3]);

		ReadOnlySpan<char> logo = ((ReadOnlySpan<char>)Logo).SliceNullTerminatedString();
		int iStringWidth = GetStringPixelWidth(logo, m_hTFont);

		surface.DrawSetTextPos((iWidth / 2) - (iStringWidth / 2), (iTall / 2) - (iFontTall / 2));
		surface.DrawString(logo);

		ReadOnlySpan<char> logo2 = ((ReadOnlySpan<char>)Logo2).SliceNullTerminatedString();
		if (logo2.Length > 0) {
			iStringWidth = GetStringPixelWidth(logo2, m_hTFont);

			surface.DrawSetTextPos((iWidth / 2) - (iStringWidth / 2), (iTall / 2) + (iFontTall / 2));
			surface.DrawString(logo2);
		}
	}

	private void PrepareLogo(TimeUnit_t time) {
		PrepareCredits(null);

		Alpha = 0;
		LogoDesiredLength = time;
		FadeTime = gpGlobals.CurTime + 5.0f;
		LogoState = LogoState.FadeIn;
		((IHudElement)this).SetActive(true);
	}
	private void PrepareOutroCredits() {
		PrepareCredits("OutroCreditsNames");

		if (CreditsList.Count == 0)
			return;

		// fill the screen
		int iWidth, iTall;
		GetHudSize(out iWidth, out iTall);
		SetSize(iWidth, iTall);

		int iHeight = iTall;

		for (int i = 0; i < CreditsList.Count; i++) {
			ref CreditName credit = ref CreditsList.AsSpan()[i];

			IScheme scheme = SchemeManager.GetScheme("ClientScheme");
			IFont m_hTFont = scheme.GetFont(credit.FontName, true)!;

			credit.YPos = iHeight;
			credit.Active = false;

			iHeight += (int)(float)(surface.GetFontTall(m_hTFont) + Separation);

			PrepareLine(m_hTFont, credit.Name);
		}

		((IHudElement)this).SetActive(true);

		g_iCreditsPixelHeight = iHeight;
	}
	private void PrepareIntroCredits() {
		PrepareCredits("IntroCreditsNames");

		int iSlot = 0;

		for (int i = 0; i < CreditsList.Count; i++) {
			ref CreditName credit = ref CreditsList.AsSpan()[i];

			IScheme scheme = SchemeManager.GetScheme("ClientScheme");
			IFont m_hTFont = scheme.GetFont(credit.FontName)!;

			credit.YPos = Y + (iSlot * surface.GetFontTall(m_hTFont));
			credit.XPos = X;

			if (i < 3) {
				credit.Active = true;
				credit.Slot = iSlot;
				credit.Time = gpGlobals.CurTime + FadeInTime + FadeOutTime + FadeHoldTime;
				credit.TimeStart = gpGlobals.CurTime;
				LogoTime = credit.Time + LogoTimeMod;
			}
			else {
				credit.Active = false;
				credit.Time = 0.0f;
			}

			iSlot = (iSlot + 1) % 3;

			PrepareLine(m_hTFont, credit.Name);
		}

		((IHudElement)this).SetActive(true);
	}

	private TimeUnit_t FadeBlend(TimeUnit_t fadein, TimeUnit_t fadeout, TimeUnit_t hold, TimeUnit_t localTime) {
		TimeUnit_t fadeTime = fadein + hold;
		TimeUnit_t fadeBlend;

		if (localTime < 0)
			return 0;

		if (localTime < fadein) {
			fadeBlend = 1 - ((fadein - localTime) / fadein);
		}
		else if (localTime > fadeTime) {
			if (fadeout > 0)
				fadeBlend = 1 - ((localTime - fadeTime) / fadeout);
			else
				fadeBlend = 0;
		}
		else
			fadeBlend = 1;

		if (fadeBlend < 0)
			fadeBlend = 0;

		return fadeBlend;
	}
	private void PrepareLine(IFont? font, ReadOnlySpan<char> line) {
		Assert(!line.IsEmpty);

		Span<char> unicode = stackalloc char[256];

		if (line[0] == '#')
			localize.ConstructString(unicode, localize.Find(line));
		else
			strcpy(unicode, line);

		surface.PrecacheFontCharacters(font, unicode);
	}

	[PanelAnimationVar("TextFont", "Default")] private IFont TextFont;
	[PanelAnimationVar("TextColor", "FgColor")] private Color TextColor;

	readonly List<CreditName> CreditsList = [];

	TimeUnit_t ScrollTime;
	float Separation;
	TimeUnit_t FadeTime;
	bool LastOneInPlace;
	int Alpha;

	CreditsType CreditsType;
	LogoState LogoState;

	TimeUnit_t FadeInTime;
	TimeUnit_t FadeHoldTime;
	TimeUnit_t FadeOutTime;
	TimeUnit_t NextStartTime;
	TimeUnit_t PauseBetweenWaves;

	TimeUnit_t LogoTimeMod;
	TimeUnit_t LogoTime;
	TimeUnit_t LogoDesiredLength;

	float X;
	float Y;

	InlineArray256<char> Logo;
	InlineArray256<char> Logo2;

	Color Color;
}
