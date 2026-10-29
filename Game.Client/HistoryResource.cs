using Game.Client.HUD;
using Game.Shared;

using Source;
using Source.Common.Bitbuffers;
using Source.Common.GUI;
using Source.GUI.Controls;

namespace Game.Client;

enum HRType
{
	Empty,
	Ammo,
	Weapon,
	Item,
	AmmoDenied
}

#if !GMOD_DLL
[DeclareHudElement(Name = "CHudHistoryResource")]
#endif
class HudHistoryResource : EditableHudElement, IHudElement
{
	struct HistItem(TimeUnit_t displayTime)
	{
		public HRType? Type;
		public TimeUnit_t DisplayTime = displayTime;
		public int Count;
		public int Id;
		public BaseCombatWeapon? Weapon;
		public HudTexture? Icon;
	}
	readonly List<HistItem> PickupHistory = [];

	int HistoryGap;
	int CurrentHistorySlot;
	bool DoNotDraw;
	InlineArray16<char> AmmoFullMsg;
	bool NeedsDraw;

	[PanelAnimationVarAliasType("history_gap", "42", "proportional_float")] protected float fHistoryGap;
	[PanelAnimationVarAliasType("icon_inset", "28", "proportional_float")] protected float IconInset;
	[PanelAnimationVarAliasType("text_inset", "26", "proportional_float")] protected float TextInset;
	[PanelAnimationVar("NumberFont", "HudNumbersSmall", "HFont")] protected IFont NumberFont;
	[PanelAnimationVar("TextFont", "Default", "HFont")] protected IFont TextFont;

	public HudHistoryResource(string elementName) : base("HudHistoryResource", elementName) {
		SetParent(clientMode.GetViewport());

		DoNotDraw = true;
		AmmoFullMsg[0] = '\0';
		NeedsDraw = false;

		((IHudElement)this).SetHiddenBits(HideHudBits.MiscStatus);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetPaintBackgroundEnabled(false);

		ReadOnlySpan<char> wsc = localize.Find("#hl2_AmmoFull");
		if (!wsc.IsEmpty)
			strcpy(AmmoFullMsg, wsc);
	}

	public override void Init() {
		IHudElement.HookMessage("SWEPCmd", MsgFunc_SWEPCmd);
#if !GMOD_DLL
		IHudElement.HookMessage("ItemPickup", MsgFunc_ItemPickup);
#endif
		IHudElement.HookMessage("AmmoDenied", MsgFunc_AmmoDenied);
		Reset();
	}

	public void Reset() {
		PickupHistory.Clear();
		CurrentHistorySlot = 0;
		DoNotDraw = false;
	}

	public virtual void SetHistoryGap(int gap) { }

	public void AddToHistory(BaseCombatWeapon weapon) {
		if ((weapon.GetWpnData().Flags & WeaponFlags.Exhaustible) != 0)
			return;

		int index = weapon.EntIndex();

		for (int i = 0; index < PickupHistory.Count; i++) {
			if (PickupHistory[i].Id == index)
				return;
		}

		AddIconToHistory(HRType.Weapon, index, weapon, 0, null);
	}

	public void AddToHistory(HRType type, int id, int count = 0) {
		if (type == HRType.Ammo) {
			if (count == 0)
				return;

			for (int i = 0; i < PickupHistory.Count; i++) {
				var item = PickupHistory[i];
				if (item.Type == HRType.AmmoDenied && item.Id == id) {
					item.DisplayTime = 0;
					CurrentHistorySlot = i;
					break;
				}
			}
		}

		AddIconToHistory(type, id, null, count, null);
	}

	private void AddToHistory(HRType type, ReadOnlySpan<char> name, int count = 0) {
		if (type != HRType.Item)
			return;

		HudTexture? icon = gHUD.GetIcon(name);
		if (icon == null)
			return;

		AddIconToHistory(type, 1, null, count, icon);
	}

	private void AddIconToHistory(HRType type, int id, BaseCombatWeapon? weapon, int count, HudTexture? icon) {
		NeedsDraw = true;

		if ((fHistoryGap * (CurrentHistorySlot + 1)) > GetTall())
			CurrentHistorySlot = 0;

		if (CurrentHistorySlot == 0)
			clientMode.GetViewportAnimationController()!.StartAnimationSequence("HintMessageLower");

		while (PickupHistory.Count <= CurrentHistorySlot)
			PickupHistory.Add(new HistItem(0));

		HistItem freeslot = PickupHistory[CurrentHistorySlot];

		if (type == HRType.AmmoDenied && freeslot.DisplayTime != 0)
			return;

		freeslot.Id = id;
		freeslot.Icon = icon;
		freeslot.Type = type;
		freeslot.Weapon = weapon;
		freeslot.Count = count;

		if (type == HRType.AmmoDenied)
			freeslot.DisplayTime = gpGlobals.CurTime + (BaseHudWeaponSelection.hud_drawhistory_time.GetFloat() / 2.0f);
		else
			freeslot.DisplayTime = gpGlobals.CurTime + BaseHudWeaponSelection.hud_drawhistory_time.GetFloat();

		++CurrentHistorySlot;
	}

	static InlineArray32<char> SWEPCmdFunction;
	static InlineArray256<char> SWEPCmdArgs;
	private void MsgFunc_SWEPCmd(bf_read msg) {
		if (gHUD.FindElement("CHudGMod") == null || g_Lua == null)
			return;
		int encodedEHandle = msg.ReadLong();
		int entity = encodedEHandle & ((1 << Constants.MAX_EDICT_BITS) - 1);
		int serialNum = encodedEHandle >> Constants.MAX_EDICT_BITS;
		EHANDLE ent = new(entity, serialNum);
		if (ent.Get() is not C_WeaponSWEP weapon)
			return;
		msg.ReadString(SWEPCmdFunction);
		msg.ReadString(SWEPCmdArgs);
		weapon.RunSWEPCmd(((ReadOnlySpan<char>)SWEPCmdFunction).SliceNullTerminatedString(), ((ReadOnlySpan<char>)SWEPCmdArgs).SliceNullTerminatedString());
	}

	private void MsgFunc_ItemPickup(bf_read msg) {
		Span<char> name = stackalloc char[1024];
		msg.ReadString(name);
		AddToHistory(HRType.Item, name);
	}

	private void MsgFunc_AmmoDenied(bf_read msg) {
		int ammo = msg.ReadShort();

		for (int i = 0; i < PickupHistory.Count; i++) {
			var item = PickupHistory[i];
			if (item.Type == HRType.Ammo && item.Id == ammo)
				break;
		}

		for (int i = 0; i < PickupHistory.Count; i++) {
			var item = PickupHistory[i];
			if (item.Type == HRType.AmmoDenied && item.Id == ammo) {
				item.DisplayTime = gpGlobals.CurTime + (BaseHudWeaponSelection.hud_drawhistory_time.GetFloat() / 2.0f);
				CurrentHistorySlot = i;
				break;
			}
		}

		AddToHistory(HRType.AmmoDenied, ammo, 0);
	}

	void CheckClearHistory() {
		foreach (var item in PickupHistory)
			if (item.Type != null)
				return;

		CurrentHistorySlot = 0;
		clientMode.GetViewportAnimationController()!.StartAnimationSequence("HintMessageRaise");
	}

	public bool ShouldDraw() => (CurrentHistorySlot > 0 || NeedsDraw) && IHudElement.DefaultShouldDraw(this);

	public override void Paint() {
		if (DoNotDraw) {
			DoNotDraw = false;
			return;
		}

		NeedsDraw = false;

		GetSize(out int wide, out int tall);

		for (int i = 0; i < PickupHistory.Count; i++) {
			var item = PickupHistory[i];
			if (item.Type != null) {
				item.DisplayTime = Math.Min(item.DisplayTime, gpGlobals.CurTime + BaseHudWeaponSelection.hud_drawhistory_time.GetFloat());
				if (item.DisplayTime <= gpGlobals.CurTime) {
					PickupHistory[i] = new HistItem(0);
					CheckClearHistory();
					continue;
				}

				TimeUnit_t elapsed = item.DisplayTime - gpGlobals.CurTime;
				float scale = (float)(elapsed * 80);
				Color clr = gHUD.ClrNormal;
				clr[3] = (byte)MathF.Min(scale, 255);

				bool useAmmoFullMsg = false;

				HudTexture? itemIcon = null;
				HudTexture? itemAmmoIcon = null;
				int amount = 0;
				bool halfHeight = true;

				switch (item.Type) {
					case HRType.Ammo: {
#if !HL2MP
							var wpnInfo = gWR.GetWeaponFromAmmo(item.Id);
							if (wpnInfo != null && (wpnInfo.MaxClip1 >= 0 || wpnInfo.MaxClip2 >= 0)) {
								itemIcon = wpnInfo.IconSmall;
								itemAmmoIcon = gWR.GetAmmoIconFromWeapon(item.Id);
							}
							else
#endif
							{
								itemIcon = gWR.GetAmmoIconFromWeapon(item.Id);
								itemAmmoIcon = null;
							}

							amount = item.Count;
						}
						break;
					case HRType.AmmoDenied: {
							itemIcon = gWR.GetAmmoIconFromWeapon(item.Id);
							amount = 0;
							useAmmoFullMsg = true;
							clr = gHUD.ClrCaution;
							clr[3] = (byte)MathF.Min(scale, 255);
						}
						break;
					case HRType.Weapon: {
							var weapon = item.Weapon;
							if (weapon == null)
								return;

							if (!weapon.HasAmmo()) {
								clr = gHUD.ClrCaution;
								clr[3] = (byte)MathF.Min(scale, 255);
							}

							itemIcon = weapon.GetSpriteInactive();
							halfHeight = false;
						}
						break;
					case HRType.Item: {
							if (item.Id == 0)
								continue;
							itemIcon = item.Icon;
							halfHeight = false;
						}
						break;
					default:
						Assert(false);
						break;
				}

				if (itemIcon == null)
					continue;

				if (clr[3] != 0)
					NeedsDraw = true;

				int ypos = tall - (HistoryGap * (i + 1));
				int xpos = wide - itemIcon.Width() - (int)IconInset;
				if (halfHeight)
					ypos += itemIcon.Height() / 2;

				itemIcon.DrawSelf(xpos, ypos, clr);
				itemAmmoIcon?.DrawSelf((int)(xpos - (itemAmmoIcon.Width() * 1.25f)), ypos, clr);

				if (amount != 0) {
					Span<char> text = stackalloc char[16];
					sprintf(text, "%i").I(amount);

					ypos -= (Surface.GetFontTall(NumberFont) - itemIcon.Height()) / 2;

					Surface.DrawSetTextFont(NumberFont);
					Surface.DrawSetTextColor(clr);
					Surface.DrawSetTextPos(wide - (int)TextInset, ypos);
					Surface.DrawString(text);
				}
				else if (useAmmoFullMsg) {
					ypos -= (Surface.GetFontTall(TextFont) - itemIcon.Height()) / 2;

					Surface.DrawSetTextFont(TextFont);
					Surface.DrawSetTextColor(clr);
					Surface.DrawSetTextPos(wide - (int)TextInset, ypos);
					Surface.DrawString(AmmoFullMsg);
				}
			}
		}
	}
}