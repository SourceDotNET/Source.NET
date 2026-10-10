using Game.Client.HUD;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Engine;
using Source.GUI.Controls;

using System.Numerics;

namespace Game.Client;

public enum GameActionSet
{
	None = -1,
	MenuControls,
	FPSControls,
	InGameHUD,
	Spectator
}

public class ClientModeShared : GameEventListener, IClientMode
{
	static readonly ConVar cl_drawhud = new("cl_drawhud", "1", 0, "Enable the rendering of the hud");

#if GMOD_DLL
	public static InlineArray512<char> HostName;

	public ClientModeShared() {
		strcpy(HostName, "Unset");
	}

	public void SetupGModSurface(bool push) {
		if (push)
			surface.PushMakeCurrent(GarrysMod.GModBase.GetGModBasePanel(true)!, false);
		else
			surface.PopMakeCurrent(GarrysMod.GModBase.GetGModBasePanel(true)!);
	}

	public static void SetupVGuiMatrices(bool push, IMatRenderContext renderContext) {
		if (!push) {
			renderContext.MatrixMode(MaterialMatrixMode.Projection);
			renderContext.PopMatrix();
			renderContext.MatrixMode(MaterialMatrixMode.Model);
			renderContext.PopMatrix();
			renderContext.MatrixMode(MaterialMatrixMode.View);
			renderContext.PopMatrix();
			return;
		}

		renderContext.GetViewport(out _, out _, out int width, out int height);
		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
		renderContext.Scale(1, -1, 1);
		renderContext.Ortho(0.5, 0.5, width + 0.5f, height + 0.5f, -1, 1);
		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
	}

	public void PostRenderVGui(IMatRenderContext renderContext) {
		if (gGM == null)
			return;

		SetupVGuiMatrices(true, renderContext);
		surface.PushMakeCurrent(GarrysMod.GModBase.GetGModBasePanel(true)!, false);
		gGM.Call((int)LUA_POOLEDSTRING.DrawOverlay);
		gGM.Call((int)LUA_POOLEDSTRING.PostRenderVGUI);
		surface.PopMakeCurrent(GarrysMod.GModBase.GetGModBasePanel(true)!);
		SetupVGuiMatrices(false, renderContext);
	}
#endif

	public void Init() {
		ChatElement = (BaseHudChat?)gHUD.FindElement("CHudChat");
		Assert(ChatElement != null);

		WeaponSelection = (BaseHudWeaponSelection?)gHUD.FindElement("CHudWeaponSelection");
		Assert(WeaponSelection != null);

#if GMOD_DLL
		ListenForGameEvent("server_spawn");
#endif
		ListenForGameEvent("player_connect_client");
		ListenForGameEvent("player_disconnect");
		ListenForGameEvent("player_team");
		ListenForGameEvent("server_cvar");
		ListenForGameEvent("player_changename");
		ListenForGameEvent("teamplay_broadcast_audio");
		ListenForGameEvent("achievement_earned");


		UserMessages msgs = Singleton<UserMessages>();
		msgs.HookMessage("VGUIMenu", MsgFunc_VGUIMenu);
		// msgs.HookMessage("Rumble", MsgFunc_Rumble);
	}

	public bool IsTyping() => ChatElement!.GetMessageMode() != MessageModeType.None;

	public bool ShouldDrawDetailObjects() => true;

	public void Enable() {
		IPanel? root = enginevgui.GetPanel(VGuiPanelType.ClientDll);

		if (root != null)
			Viewport.SetParent(root);

		Viewport.SetProportional(true);
		Viewport.SetCursor(CursorCode.None);
		surface.SetCursor(CursorCode.None);

		Viewport.SetVisible(true);
		if (Viewport.IsKeyboardInputEnabled())
			Viewport.RequestFocus();

		Layout();
	}

	public bool CreateMove(TimeUnit_t inputSampleTime, ref UserCmd cmd) {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null)
			return true;

		return player.CreateMove(inputSampleTime, ref cmd);
	}
	public virtual int KeyInput(int down, ButtonCode keynum, ReadOnlySpan<char> currentBinding) {
		if (engine.Con_IsVisible())
			return 1;

#if GMOD_DLL
		if (!currentBinding.IsEmpty && (currentBinding.Equals("messagemode", StringComparison.Ordinal) || currentBinding.Equals("say", StringComparison.Ordinal))) {
#else
		if (!currentBinding.IsEmpty && currentBinding.Equals("messagemode", StringComparison.Ordinal)) {
#endif
			if (down != 0)
				StartMessageMode(MessageModeType.Say);

			return 0;
		}
#if GMOD_DLL
		else if (!currentBinding.IsEmpty && (currentBinding.Equals("messagemode2", StringComparison.Ordinal) || currentBinding.Equals("say_team", StringComparison.Ordinal))) {
#else
		else if (!currentBinding.IsEmpty && currentBinding.Equals("messagemode2", StringComparison.Ordinal)) {
#endif
			if (down != 0)
				StartMessageMode(MessageModeType.SayTeam);

			return 0;
		}

		// In-game spectator
		// Weapon input

		if (HudElementKeyInput(down, keynum, currentBinding) == 0)
			return 0;

		return 1;
	}

	private int HudElementKeyInput(int down, ButtonCode keynum, ReadOnlySpan<char> currentBinding) {
		if (WeaponSelection != null) {
			if (WeaponSelection.KeyInput(down, keynum, currentBinding) == 0)
				return 0;
		}

		return 1;
	}

	public void StartMessageMode(MessageModeType messageModeType) {
#if !GMOD_DLL
		if (gpGlobals.MaxClients == 1)
			return;
#endif

		ChatElement?.StartMessageMode(messageModeType);
	}
	public void StopMessageMode() {
		ChatElement?.StopMessageMode();

	}

	public void OverrideMouseInput(ref float mouse_x, ref float mouse_y) {
		// nothing yet
	}

	protected BaseViewport Viewport = null!;

	public Panel GetViewport() {
		return Viewport;
	}

	public float GetViewModelFOV() {
		return v_viewmodel_fov.GetFloat();
	}

	public virtual void OverrideView(ref ViewSetup setup) {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		if (input.CAM_IsThirdPerson()) {
			Vector3 camOfs = g_ThirdPersonManager.GetCameraOffsetAngles();
			Vector3 camOfsDist = g_ThirdPersonManager.GetFinalCameraOffset();

			camOfsDist *= g_ThirdPersonManager.GetDistanceFraction();

			QAngle camAngles = new(camOfs[PITCH], camOfs[YAW], 0);

			if (g_ThirdPersonManager.IsOverridingThirdPerson() == false)
				engine.GetViewAngles(out camAngles);

			MathLib.AngleVectors(camAngles, out Vector3 camForward, out Vector3 camRight, out Vector3 camUp);

			setup.Origin -= camForward * camOfsDist[0];
			setup.Origin += camRight * camOfsDist[1];
			setup.Origin += camUp * camOfsDist[2];

			setup.Angles = camAngles;
		}

		// todo ortho
	}

	public void Layout() {
		IPanel? root = enginevgui.GetPanel(VGuiPanelType.ClientDll);

		if (root != null) {
			root.GetSize(out int wide, out int tall);

			bool changed = wide != RootSize[0] || tall != RootSize[1];
			RootSize[0] = wide;
			RootSize[1] = tall;

			Viewport.SetBounds(0, 0, wide, tall);
			if (changed)
				ReloadScheme(false);
		}
	}

	private void ReloadScheme(bool v) {
		BuildGroup.ClearResFileCache();

		Viewport.ReloadScheme("resource/ClientScheme.res");
	}

	public AnimationController? GetViewportAnimationController() => Viewport.GetAnimationController();

	bool PlayerNameNotSetYet(ReadOnlySpan<char> name) {
		if (!name.IsEmpty) {
			if (strieq(name, "unnamed"))
				return true;
			if (strieq(name, "NULLNAME"))
				return true;
		}
		return false;
	}

	public static C_BasePlayer? USERID2PLAYER(int i) => ToBasePlayer(cl_entitylist.GetEnt(engine.GetPlayerForUserID(i)));

	public override void FireGameEvent(IGameEvent ev) {
		BaseHudChat? hudChat = (BaseHudChat?)gHUD.FindElement("CHudChat");
		ReadOnlySpan<char> eventname = ev.GetName();

		switch (eventname) {
#if GMOD_DLL
			case "server_spawn":
				strcpy(HostName, ev.GetString("hostname"));
				break;
#endif
			case "player_connect_client": {
					if (hudChat == null)
						return;

					if (PlayerNameNotSetYet(ev.GetString("name")))
						return;

					if (!IsInCommentaryMode()) {
						Span<char> localized = stackalloc char[100];
						ReadOnlySpan<char> playerName = localize.TryFind(ev.GetString("name")).SliceNullTerminatedString();
						ReadOnlySpan<char> joined = localize.Find("#game_player_joined_game");

						int written = playerName.ClampedCopyTo(localized);
						written += " ".ClampedCopyTo(localized[written..]);
						written += joined.ClampedCopyTo(localized[written..]);

						hudChat.Printf(ChatFilters.JoinLeave, localized);
					}
				}
				break;
			case "player_disconnect": {
					C_BasePlayer? player = USERID2PLAYER(ev.GetInt("userid"));
					if (hudChat == null || player == null)
						return;
					if (PlayerNameNotSetYet(ev.GetString("name")))
						return;

					if (!IsInCommentaryMode()) {
						Span<char> localized = stackalloc char[100];
						ReadOnlySpan<char> playerName = player.GetPlayerName();
						ReadOnlySpan<char> reason = localize.TryFind(ev.GetString("reason")).SliceNullTerminatedString();

						localize.ConstructString(localized, localize.Find("#game_player_left_game"), playerName, reason);

						hudChat.Printf(ChatFilters.JoinLeave, localized);
					}
				}
				break;
			case "player_team": {

				}
				break;
			case "player_changename": {

				}
				break;
			case "teamplay_broadcast_audio": {

				}
				break;
			case "server_cvar": {
					if (!IsInCommentaryMode()) {
						ReadOnlySpan<char> cvarName = localize.TryFind(ev.GetString("cvarname"));
						ReadOnlySpan<char> cvarValue = localize.TryFind(ev.GetString("cvarvalue"));
						Span<char> localized = stackalloc char[256];
						localize.ConstructString(localized, localize.Find("#game_server_cvar_changed"), cvarName, cvarValue);

						hudChat?.Printf(ChatFilters.ServerMsg, localized);
					}
				}
				break;
			case "achievement_earned": {

				}
				break;
			default:
				DevMsg(2, $"Unhandled GameEvent in ClientModeShared.FireGameEvent - {ev.GetName()}\n");
				break;
		}
	}


	public void LevelInit(ReadOnlySpan<char> newmap) {
		Viewport.GetAnimationController().StartAnimationSequence("LevelInit");

		// if (ChatElement != null)
		// 	ChatElement.LevelInit(newmap);

		IGameEvent? evnt = gameeventmanager.CreateEvent("game_newmap");
		if (evnt != null) {
			evnt.SetString("mapname", newmap);
			gameeventmanager.FireEventClientSide(evnt);
		}
	}

	public void ProcessInput(bool active) {
		gHUD.ProcessInput(active);
	}

	InlineArray2<int> RootSize;

	public BaseHudChat? ChatElement;
	public BaseHudWeaponSelection? WeaponSelection;

	static void MsgFunc_VGUIMenu(bf_read msg) {
		Span<char> panelname = stackalloc char[256];
		msg.ReadString(panelname);

		bool show = msg.ReadByte() != 0;

		IViewPortPanel? panel = BaseViewport.g_ViewPortInterface!.FindPanelByName(panelname.SliceNullTerminatedString());
		if (panel == null)
			return;

		int count = msg.ReadByte();

		if (count > 0) {
			KeyValues keys = new("data");
			for (int i = 0; i < count; i++) {
				Span<char> name = stackalloc char[256];
				Span<char> data = stackalloc char[255];
				msg.ReadString(name);
				msg.ReadString(data);
				keys.SetString(name, data);
				// Console.WriteLine($"VGUIMenu KeyValue: {name} = {data}");
			}

			// todo

			panel.SetData(keys);
		}

		// todo

		panel.ShowPanel(show);
	}
}
