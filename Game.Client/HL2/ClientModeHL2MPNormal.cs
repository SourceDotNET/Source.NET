global using static Game.Client.HL2.ClientModeHL2MPNormal;

using Game.Client.GarrysMod;
using Game.Client.HUD;

using Source.Common;
using Source.Common.Commands;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Engine;

namespace Game.Client.HL2;

public class HudViewport : BaseViewport
{
	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		gHUD.InitColors(scheme);
		SetPaintBackgroundEnabled(false);
	}
}

public class ClientModeHL2MPNormal : ClientModeShared
{
	static readonly ConVar fov_desired = new("75", FCvar.Archive | FCvar.UserInfo, "Sets the base field-of-view.", 75, MAX_FOV);

	static ClientModeHL2MPNormal g_ClientModeNormal = null!;
	public static IClientMode GetClientModeNormal() => g_ClientModeNormal ??= new();

	public ClientModeHL2MPNormal() {
		Viewport = new HudViewport();
		Viewport.Start();
	}

#if GMOD_DLL
	public override int KeyInput(int down, ButtonCode keynum, ReadOnlySpan<char> currentBinding) {
		// todo

		if (engine.Con_IsVisible())
			return 1;

		if (currentBinding.IsEmpty || strcmp(currentBinding, "toggleconsole") != 0) {
			C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
			if (player != null && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PlayerBindPress)) {
				LuaEntity.Push_Entity(player);
				g_Lua!.PushString(currentBinding);
				g_Lua.PushBool(down != 0);
				g_Lua.PushNumber((int)keynum);
				if (gGM.CallFinish(4))
					return 0;
			}
		}

		return base.KeyInput(down, keynum, currentBinding);
	}
#endif
}
