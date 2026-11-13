using Game.Client.HUD;
using Game.Shared;

using Source;
using Source.Common.Commands;
using Source.Common.Input;
using Source.GUI.Controls;

namespace Game.Client;

public class BaseHudWeaponSelection : EditableHudElement
{
	public const int HUDTYPE_BUCKETS = 0;
	public const int HUDTYPE_FASTSWITCH = 1;
	public const int HUDTYPE_PLUS = 2;
	public const int HUDTYPE_CAROUSEL = 3;

	public const int MAX_SELECTABLE_SLOTS = 10;

	public static ConVar hud_drawhistory_time = new("hud_drawhistory_time", "5", 0);
	public static ConVar hud_fastswitch = new("hud_fastswitch", "0", FCvar.Archive);
	public double SelectionTime;
	static BaseHudWeaponSelection? Instance;
	public bool SelectionVisible;
	public BaseCombatWeapon? SelectedWeapon;
	public IHudElement HudElement => this;

	public BaseHudWeaponSelection(string elementName, Panel? parent, string panelName) : base(elementName, parent, panelName) {
		Instance = this;
		HudElement.SetHiddenBits(HideHudBits.WeaponSelection | HideHudBits.NeedSuit | HideHudBits.PlayerDead | HideHudBits.InVehicle);
	}

	public override void Init() {
		Reset();
		gWR.Init();
		SelectionTime = gpGlobals.CurTime;
	}

	void Reset() {
		gWR.Reset();
		SelectionVisible = false;
		SelectionTime = gpGlobals.CurTime;
		// UnlockRenderGroup todo
	}

	void UpdateSelectionTime() => SelectionTime = gpGlobals.CurTime;
	static BaseHudWeaponSelection GetInstance() => Instance!;
	public static BaseHudWeaponSelection GetHudWeaponSelection() => GetInstance();

	public void VidInit() {
		gWR.LoadAllWeaponSprites();

		// todo hudhr
	}

	public override void OnThink() {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		if ((player.GetFlags() & EntityFlags.Frozen) != 0/* || player.IsPlayerDead()*/) { //todo
			if (IsInSelectionMode())
				CancelWeaponSelection();
		}
	}

	public void ProcessInput() {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		int fastSwitchMode = hud_fastswitch.GetInt();

		if (/*player.IsInVGuiInputMode() && !player.IsInViewModelVGuiInputMode()*/ false) {
			if ((gHUD.KeyBits & InButtons.Attack) != 0) {
				if (HUDTYPE_PLUS != fastSwitchMode) {
					gHUD.KeyBits &= ~InButtons.Attack;
					input.ClearInputButton(InButtons.Attack);
				}

				engine.ClientCmd("cancelselect\n");
			}

			return;
		}

		if ((gHUD.KeyBits & (InButtons.Attack | InButtons.Attack2)) != 0) {
			if (IsWeaponSelectable()) {
				if (HUDTYPE_PLUS != fastSwitchMode) {
					gHUD.KeyBits &= ~(InButtons.Attack | InButtons.Attack2);
					input.ClearInputButton(InButtons.Attack);
					input.ClearInputButton(InButtons.Attack2);
				}
				SelectWeapon();
			}
		}
	}

	public bool IsInSelectionMode() => SelectionVisible;

	public virtual void OpenSelection() {
		SelectionVisible = true;
		// lockrendergroup todo

		input.ClearInputButton(InButtons.Attack | InButtons.Attack2);
	}

	public virtual void HideSelection() {
		SelectionVisible = false;
		// unlockrendergroup todo
	}

	private bool IsWeaponSelectable() => IsInSelectionMode();

	public bool CanBeSelectedInHUD(BaseCombatWeapon pWeapon) { return true; } // todo

	public int KeyInput(int down, ButtonCode keynum, ReadOnlySpan<char> currentBinding) {
		if (IsInSelectionMode() && !currentBinding.IsEmpty && currentBinding == "cancelselect") {
			CancelWeaponSelection();
			return 0;
		}

		if (down >= 1 && keynum > ButtonCode.Key1 && keynum <= ButtonCode.Key9) {
			if (HandleHudMenuInput((int)keynum - (int)ButtonCode.Key0))
				return 0;
		}

		return 1;
	}

	public virtual void OnWeaponPickup(BaseCombatWeapon weapon) { }

	static private void UserCmd_Slot(int slot) {
		int fastSwitchMode = hud_fastswitch.GetInt();
		if (HUDTYPE_CAROUSEL == fastSwitchMode)
			UserCmd_LastWeapon();
		else
			Instance?.SelectSlot(slot);
	}
	[ConCommand("slot1", flags: FCvar.ServerCanExecute)]
	static void UserCmd_Slot1() => UserCmd_Slot(1);
	[ConCommand("slot2", flags: FCvar.ServerCanExecute)]
	static void UserCmd_Slot2() => UserCmd_Slot(2);
	[ConCommand("slot3", flags: FCvar.ServerCanExecute)]
	static void UserCmd_Slot3() => UserCmd_Slot(3);
	[ConCommand("slot4", flags: FCvar.ServerCanExecute)]
	static void UserCmd_Slot4() => UserCmd_Slot(4);
	[ConCommand("slot5", flags: FCvar.ServerCanExecute)]
	static void UserCmd_Slot5() => UserCmd_Slot(5);
	[ConCommand("slot6", flags: FCvar.ServerCanExecute)]
	static void UserCmd_Slot6() => UserCmd_Slot(6);
	[ConCommand("slot7", flags: FCvar.ServerCanExecute)]
	static void UserCmd_Slot7() => UserCmd_Slot(7);
	[ConCommand("slot8", flags: FCvar.ServerCanExecute)]
	static void UserCmd_Slot8() => UserCmd_Slot(8);
	[ConCommand("slot9", flags: FCvar.ServerCanExecute)]
	static void UserCmd_Slot9() => UserCmd_Slot(9);
	[ConCommand("slot0", flags: FCvar.ServerCanExecute)]
	static void UserCmd_Slot0() => UserCmd_Slot(10);
	[ConCommand("slot10", flags: FCvar.ServerCanExecute)]
	static void UserCmd_Slot10() => UserCmd_Slot(10);

	bool IsHudMenuTakingInput() => gHUD.FindElement("CHudMenu") is HudMenu hudMenu && hudMenu.IsMenuOpen();

	bool HandleHudMenuInput(int slot) {
		if (gHUD.FindElement("CHudMenu") is not HudMenu hudMenu || !hudMenu.IsMenuOpen())
			return false;

		hudMenu.SelectMenuItem(slot);
		return true;
	}

	// bool IsHudMenuPreventingWeaponSelection() { }

	void SelectSlot(int slot) {
		if (HandleHudMenuInput(slot))
			return;

		if (!IHudElement.DefaultShouldDraw(this))
			return;

		UpdateSelectionTime();
		SelectWeaponSlot(slot);
	}

	[ConCommand("cancelselect", flags: FCvar.ServerCanExecute)]
	static void UserCmd_Close() => Instance?.CancelWeaponSelection();

	[ConCommand("invnext", flags: FCvar.ServerCanExecute)]
	static void UserCmd_NextWeapon() {
		if (!IHudElement.DefaultShouldDraw(Instance!))
			return;

		int fastSwitchMode = hud_fastswitch.GetInt();
		Instance.CycleToNextWeapon();

		if (fastSwitchMode > 0)
			Instance.SelectWeapon();
		Instance.UpdateSelectionTime();
	}

	[ConCommand("invprev", flags: FCvar.ServerCanExecute)]
	static void UserCmd_PrevWeapon() {
		if (!IHudElement.DefaultShouldDraw(Instance!))
			return;

		int fastSwitchMode = hud_fastswitch.GetInt();
		Instance.CycleToPrevWeapon();

		if (fastSwitchMode > 0)
			Instance.SelectWeapon();
		Instance.UpdateSelectionTime();
	}

	[ConCommand("lastinv", flags: FCvar.ServerCanExecute)]
	static void UserCmd_LastWeapon() {
		if (!IHudElement.DefaultShouldDraw(Instance!))
			return;

		Instance.SwitchToLastWeapon();
	}

	void SwitchToLastWeapon() {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		input.MakeWeaponSelection(player.GetLastWeapon());
	}

	public virtual void SetWeaponSelected() {
		Assert(GetSelectedWeapon());
		input.MakeWeaponSelection(GetSelectedWeapon());
	}

	public void SelectWeapon() {
		if (GetSelectedWeapon() == null) {
			engine.ClientCmd("cancelselect\n");
			return;
		}

		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		if (!GetSelectedWeapon()!.CanBeSelected()) {
			player.EmitSound("Player.DenyWeaponSelection");
			DevMsg("Player.DenyWeaponSelection\n");
		}
		else {
			SetWeaponSelected();
			SelectedWeapon = null;
			engine.ClientCmd("cancelselect\n");

			player.EmitSound("Player.WeaponSelected");
		}
	}

	public BaseCombatWeapon? GetSelectedWeapon() => SelectedWeapon;

	void CancelWeaponSelection() {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		if (HudElement.ShouldDraw()) {
			HideSelection();
			SelectedWeapon = null;

			player.EmitSound("Player.WeaponSelectionClose");
		}
		else
			engine.ClientCmd("escape\n");
	}

	public List<BaseCombatWeapon> GetWeaponsInSlot(int slot) {
		List<BaseCombatWeapon> weapons = [];

		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return weapons;

		for (int i = 0; i < MAX_WEAPONS; i++) {
			BaseCombatWeapon? weapon = player.GetWeapon(i);
			if (weapon != null && weapon.GetSlot() == slot && weapon.VisibleInWeaponSelection())
				weapons.Add(weapon);
		}

		weapons.Sort((a, b) => a.GetPosition() != b.GetPosition() ? a.GetPosition() - b.GetPosition() : a.EntIndex() - b.EntIndex());
		return weapons;
	}

#if GMOD_DLL
	public int GetWeaponPosition(BaseCombatWeapon weapon) {
		int index = GetWeaponsInSlot(weapon.GetSlot()).IndexOf(weapon);
		return index >= 0 ? index : weapon.GetPosition();
	}
#else
	public int GetWeaponPosition(BaseCombatWeapon weapon) => GetWeaponsInSlot(weapon.GetSlot()).IndexOf(weapon);
#endif

	public BaseCombatWeapon? GetFirstPos(int slot) {
		List<BaseCombatWeapon> weapons = GetWeaponsInSlot(slot);
		return weapons.Count > 0 ? weapons[0] : null;
	}

	public BaseCombatWeapon? GetNextActivePos(int slot, int slotPos) {
		if (slot >= MAX_SELECTABLE_SLOTS)
			return null;

		List<BaseCombatWeapon> weapons = GetWeaponsInSlot(slot);
		return slotPos >= 0 && slotPos < weapons.Count ? weapons[slotPos] : null;
	}

	public virtual void CycleToNextWeapon() { }
	public virtual void CycleToPrevWeapon() { }
	public virtual void SelectWeaponSlot(int slot) { }
}
