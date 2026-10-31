using Game.Client.HUD;
using Game.Shared;

using Source;
using Source.Common.Bitbuffers;
using Source.Common.GUI;
using Source.Engine;
using Source.GUI.Controls;

using static Source.Common.GarrysMod.Lua.PooledStrings;

namespace Game.Client.GarrysMod;

[DeclareHudElement(Name = "CHudGMod")]
public class HudGMod : EditableHudElement, IHudElement
{
	public static Panel? g_HudGMod;

	public HudGMod(string? elementName) : base(elementName, "GModHudHookAndUmsgReceiver") {
		g_HudGMod = this;
		SetParent(enginevgui.GetPanel(VGuiPanelType.ClientDll));
		((IHudElement)this).SetHiddenBits(HideHudBits.WeaponSelection);
		SetMouseInputEnabled(false);
		SetKeyboardInputEnabled(false);
		SetProportional(false);
	}

	public override void Init() {
		IHudElement.HookMessage("ItemPickup", MsgFunc_ItemPickup);
		IHudElement.HookMessage("AmmoPickup", MsgFunc_AmmoPickup);
		IHudElement.HookMessage("WeaponPickup", MsgFunc_WeaponPickup);
	}

	struct PendingWeaponPickup
	{
		public uint Handle;
		public TimeUnit_t Time;
	}

	readonly List<PendingWeaponPickup> WeaponPickups = [];

	static void MsgFunc_WeaponPickup(bf_read msg) {
		HudGMod? hud = (HudGMod?)gHUD.FindElement("CHudGMod");
		if (hud == null || g_Lua == null)
			return;
		uint handle = (uint)msg.ReadLong();
		hud.WeaponPickups.Add(new() { Handle = handle, Time = gpGlobals.CurTime });
	}

	public override void OnThink() {
		for (int i = 0; i < WeaponPickups.Count; i++) {
			C_BaseEntity? ent = cl_entitylist.GetBaseEntityFromHandle(new ClientEntityHandle(WeaponPickups[i].Handle));
			if (ent != null && ent.IsBaseCombatWeapon()) {
				if (g_Lua != null && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.HUDWeaponPickedUp)) {
					LuaEntity.Push_Entity(ent);
					gGM.CallNoReturns(1);
				}
				WeaponPickups.RemoveAt(i);
				return;
			}
			if (gpGlobals.CurTime - WeaponPickups[i].Time > 5.0) {
				WeaponPickups.RemoveAt(i);
				return;
			}
		}
	}

	static void MsgFunc_ItemPickup(bf_read msg) {
		if (gHUD.FindElement("CHudGMod") == null)
			return;
		Span<char> name = stackalloc char[256];
		msg.ReadString(name);
		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.HUDItemPickedUp)) {
			g_Lua!.PushString(name.SliceNullTerminatedString());
			gGM.CallNoReturns(1);
		}
	}

	static void MsgFunc_AmmoPickup(bf_read msg) {
		if (gHUD.FindElement("CHudGMod") == null || g_Lua == null)
			return;
		Span<char> name = stackalloc char[128];
		msg.ReadString(name);
		short count = msg.ReadShort();
		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.HUDAmmoPickedUp)) {
			g_Lua.PushString(name.SliceNullTerminatedString());
			g_Lua.PushNumber(count);
			gGM.CallNoReturns(2);
		}
	}

	void IHudElement.VidInit() => ((IHudElement)this).Reset();

	void IHudElement.Reset() => SetSize(ScreenWidth(), ScreenHeight());

	public override void PaintBackground() {
		if (g_Lua == null)
			return;

		gGM!.Call((int)LUA_POOLEDSTRING.HUDPaintBackground);
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player != null && player.GetActiveWeapon() != null)
			player.GetActiveWeapon()!.DrawHUDBackground();
	}

	public override void Paint() {
		if (g_Lua == null)
			return;

		gGM!.Call((int)LUA_POOLEDSTRING.HUDPaint);
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player != null && player.GetActiveWeapon() != null)
			player.GetActiveWeapon()!.DrawHUD();
		gGM.Call((int)LUA_POOLEDSTRING.HUDDrawScoreBoard);
		base.Paint();
	}
}
