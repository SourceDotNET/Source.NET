using Game.Client.HUD;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Input;

using System.Runtime.CompilerServices;

namespace Game.Client;

public partial class C_BaseCombatWeapon : C_BaseAnimating
{
	public C_BaseCombatWeapon() {
		Owner = new();
	}

	public override bool IsBaseCombatWeapon() => true;
	public override GarrysMod.LuaClass Lua_GetLuaClass() => GarrysMod.LuaEntity.LC_Weapon;
	public bool Dropped;
	public virtual bool WasDropped() => Dropped;
	public virtual void SetDropped() => Dropped = true;
	public virtual void DrawHUD() { }
	public virtual bool ShouldFreezeViewAngles() => false;
	public virtual int KeyInput(int down, ButtonCode keynum, ReadOnlySpan<char> currentBinding) => 1;
	public virtual void DrawHUDBackground() { }
	public override bool IsWeapon() => true;
	public static BaseCombatWeapon? GetActiveWeapon() {
		BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		return player?.GetActiveWeapon();
	}

	public bool IsBeingCarried() => GetOwner() != null;

	public override ShadowType ShadowCastType() {
		if (IsEffectActive(EntityEffects.NoShadow))
			return ShadowType.None;

		if (!IsBeingCarried())
			return ShadowType.RenderToTexture;

		if (IsCarriedByLocalPlayer() && !C_BasePlayer.ShouldDrawLocalPlayer())
			return ShadowType.None;

		return ShadowType.RenderToTexture;
	}

	public bool IsCarriedByLocalPlayer() {
		BaseEntity? owner = GetOwner();
		if (owner == null)
			return false;
		return owner == C_BasePlayer.GetLocalPlayer();
	}

	public bool ShouldDrawUsingViewModel() => IsCarriedByLocalPlayer() && !C_BasePlayer.ShouldDrawLocalPlayer();

	public void Redraw() {
		// if (clientMode.ShouldDrawCrosshair()) todo
		DrawCrosshair();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool ShouldDrawLocalPlayerViewModel() => C_BasePlayer.ShouldDrawLocalPlayer();

	public override bool ShouldDraw() {
		if (WorldModelIndex == 0)
			return false;

		// FIXME: All weapons with owners are set to transmit in CBaseCombatWeapon::UpdateTransmitState,
		// even if they have EF_NODRAW set, so we have to check this here. Ideally they would never
		// transmit except for the weapons owned by the local player.
		if (IsEffectActive(EntityEffects.NoDraw))
			return false;

		C_BaseCombatCharacter? owner = GetOwner();

		// weapon has no owner, always draw it
		if (owner == null)
			return true;

		bool isActive = (State == (int)WeaponState.IsActive);

		C_BasePlayer? localPlayer = C_BasePlayer.GetLocalPlayer();

		// carried by local player?
		if (owner == localPlayer) {
			// Only ever show the active weapon
			if (!isActive)
				return false;

			if (!owner.ShouldDraw()) {
				// Our owner is invisible.
				// This also tests whether the player is zoomed in, in which case you don't want to draw the weapon.
				return false;
			}

			// 3rd person mode?
			if (!ShouldDrawLocalPlayerViewModel())
				return true;

			// don't draw active weapon if not in some kind of 3rd person mode, the viewmodel will do that
			return false;
		}

		// If it's a player, then only show active weapons
		if (owner.IsPlayer()) {
			// Show it if it's active...
			return isActive;
		}

		// FIXME: We may want to only show active weapons on NPCs
		// These are carried by AIs; always show them
		return true;
	}

	void DrawCrosshair() {
		BasePlayer? player = BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		Color clr = gHUD.ClrNormal;

		if (gHUD.FindElement("CHudCrosshair") is not HudCrosshair crosshair)
			return;

		bool onTarget = State == 0x40; // WEAPON_IS_ONTARGET

		if (/*player.GetFOV() >= 90*/ false) { // todo

		}
		else {
			Color white = new(255, 255, 255, 255);

			FileWeaponInfo wepData = GetWpnData();
			if (onTarget && wepData.IconZoomedAutoaim != null)
				crosshair.SetCrosshair(wepData.IconZoomedAutoaim, white);
			else if (wepData.IconZoomedCrosshair != null)
				crosshair.SetCrosshair(wepData.IconZoomedCrosshair, white);
			else
				crosshair.ResetCrosshair();
		}
	}
}
