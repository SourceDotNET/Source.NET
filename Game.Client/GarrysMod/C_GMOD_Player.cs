using Game.Client.HL2MP;
using Game.Client.HUD;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.GarrysMod.Lua;
using Source.Common.Hashing;
using Source.Common.Input;
using Source.Common.Mathematics;
using Source.GUI.Controls;

using System.Numerics;

using ClientModelRenderInfo = Source.Common.Engine.ModelRenderInfo;

namespace Game.Client.GarrysMod;

[LinkEntityToClass("player")]
[NetworkName("CGMOD_Player")]
public partial class C_GMOD_Player() : C_HL2MP_Player()
{
	static ConVar cl_playercolor = new("1.0 0.0 0.0", FCvar.UserInfo | FCvar.Archive | FCvar.ServerCanExecute, "Default Player Model");

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(GModPlayerFlags),
		nameof(HoveredWidget),
		nameof(PressedWidget),
		nameof(Driving),
		nameof(DrivingMode),
		new(nameof(PlayerClass)) { Proxy = typeof(PlayerClassProxy) },
		nameof(CanZoom),
		nameof(CanWalk),
		nameof(IsTyping),
		nameof(StepSize),
		nameof(JumpPower),
		new(nameof(ViewOffset)) { Name = "m_ViewOffset" },
		nameof(ViewOffsetDucked),
		nameof(GestureEndTime),
		nameof(PlayerColor),
		nameof(WeaponColor),
		nameof(Hands),
		nameof(WaterLevel),
		nameof(MaxArmor),
		nameof(Gravity),
		nameof(SprintEnabled),
	];

	[NetworkVar("m_iGModPlayerFlags")]
	public int GModPlayerFlags;
	[NetworkVar("m_HoveredWidget")]
	public EHANDLE HoveredWidget = new();
	[NetworkVar("m_PressedWidget")]
	public EHANDLE PressedWidget = new();
	[NetworkVar("m_Driving")]
	public EHANDLE Driving = new();
	[NetworkVar("m_DrivingMode")]
	public int DrivingMode;
	[NetworkVar("m_PlayerClass")]
	public int PlayerClass;
	[NetworkVar("m_bCanZoom")]
	public bool CanZoom;
	[NetworkVar("m_bCanWalk")]
	public bool CanWalk;
	[NetworkVar("m_bIsTyping")]
	public bool IsTyping;
	[NetworkVar("m_StepSize")]
	public float StepSize;
	[NetworkVar("m_JumpPower")]
	public float JumpPower;
	[NetworkVar("m_ViewOffsetDucked")]
	public Vector3 ViewOffsetDucked;
	[NetworkVar("m_fGestureEndTime")]
	public float GestureEndTime;
	[NetworkVar("m_PlayerColor")]
	public Vector3 PlayerColor;
	[NetworkVar("m_WeaponColor")]
	public Vector3 WeaponColor;

	public virtual Vector3 GetPlayerColor() => PlayerColor;
	public virtual Vector3 GetWeaponColor() => WeaponColor;

	public virtual void SetPlayerColor(in Vector3 color) {
		if (PlayerColor != color)
			PlayerColor = color;
	}

	public virtual void SetWeaponColor(in Vector3 color) {
		if (WeaponColor != color)
			WeaponColor = color;
	}
	[NetworkVar("m_Hands")]
	public EHANDLE Hands = new();
	[NetworkVar("m_iMaxArmor")]
	public int MaxArmor;
	[NetworkVar("m_flGravity")]
	public float Gravity;
	[NetworkVar("m_bSprintEnabled")]
	public bool SprintEnabled;

	public override bool RestrictPlayerPitch() => (GModPlayerFlags & 16) == 0;
	public override bool CanAttack() => PressedWidget.Get() == null;

	public C_BaseEntity? FindUseEntity() {
		C_BaseEntity? ent = null; // todo: base.FindUseEntity()
		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.FindUseEntity)) {
			LuaEntity.Push_Entity(this);
			LuaEntity.Push_Entity(ent);
			if (gGM.CallReturns(2, 1)) {
				ILuaObject ret = g_Lua!.GetReturn(0);
				if (ret.GetType() == LuaType.Entity)
					return (C_BaseEntity?)ret.GetEntity();
				return null;
			}
		}
		return ent;
	}

	private static void RecvProxy_PlayerClass(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		RecvProxy_Int32ToInt32(in data, instance, field);
		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PlayerClassChanged)) {
			C_GMOD_Player player = (C_GMOD_Player)instance;
			LuaEntity.Push_Entity(player);
			g_Lua!.PushNumber(player.PlayerClass);
			gGM.CallNoReturns(2);
		}
	}

	public static class PlayerClassProxy
	{
		public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field) => RecvProxy_PlayerClass(in data, instance, field);
	}

	InlineArray128<char> UniqueID;

	static uint UniqueIDCRC(ReadOnlySpan<char> str) {
		Span<byte> bytes = stackalloc byte[str.Length];
		for (int i = 0; i < str.Length; i++)
			bytes[i] = (byte)str[i];
		uint crc = 0;
		CRC32.Init(ref crc);
		CRC32.ProcessBuffer(ref crc, (ReadOnlySpan<byte>)bytes);
		CRC32.Final(ref crc);
		return crc;
	}

	public ReadOnlySpan<char> GetUniqueID() {
		if (gpGlobals.MaxClients == 1)
			return "1";

		if ((GetFlags() & EntityFlags.FakeClient) != 0) {
			ReadOnlySpan<char> name = g_pPlayerResource == null ? "" : g_pPlayerResource.GetPlayerName(EntIndex());
			if (name.IsEmpty)
				return "0";
			sprintf(UniqueID, "%u").U(UniqueIDCRC(name));
			return ((Span<char>)UniqueID).SliceNullTerminatedString();
		}

		if (stricmp(GetNetworkIDString(), "STEAM_ID_LAN") == 0) {
			if (engine.GetNetChannelInfo() == null)
				return "PENDING";
		}
		else if (stricmp(GetNetworkIDString(), "STEAMID_PENDING") == 0 || stricmp(GetNetworkIDString(), "STEAM_ID_PENDING") == 0)
			return "PENDING";

		Span<char> gmID = stackalloc char[128];
		sprintf(gmID, "gm_%s_gm").S(GetNetworkIDString());
		sprintf(UniqueID, "%u").U(UniqueIDCRC(gmID.SliceNullTerminatedString()));
		return ((Span<char>)UniqueID).SliceNullTerminatedString();
	}

	sbyte PrePlayerDrawDepth;
	sbyte PostPlayerDrawDepth;

	protected override bool OnInternalDrawModel(ref ClientModelRenderInfo info) {
		if (PrePlayerDrawDepth < 11) {
			if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PrePlayerDraw)) {
				LuaEntity.Push_Entity(this);
				g_Lua!.PushNumber((int)info.Flags);
				PrePlayerDrawDepth++;
				bool ret = gGM.CallFinish(2);
				PrePlayerDrawDepth--;
				if (ret)
					return false;
			}
		}
		else
			g_Lua!.ErrorNoHalt("PrePlayerDraw: We are 10 layers deep, runaway infinite loop?");
		return base.OnInternalDrawModel(ref info);
	}

	protected override bool OnPostInternalDrawModel(ref ClientModelRenderInfo info) {
		if (PostPlayerDrawDepth > 10) {
			g_Lua!.ErrorNoHalt("PostPlayerDraw: We are 10 layers deep, runaway infinite loop?");
			return true;
		}
		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PostPlayerDraw)) {
			LuaEntity.Push_Entity(this);
			g_Lua!.PushNumber((int)info.Flags);
			PostPlayerDrawDepth++;
			gGM.CallNoReturns(2);
			PostPlayerDrawDepth--;
		}
		return true;
	}

	public override void UpdateClientSideAnimation() {
		if (Driving.Get() != null) {
			if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PlayerDriveAnimate)) {
				LuaEntity.Push_Entity(this);
				gGM.CallNoReturns(1);
			}
		}
		else {
			QAngle eyeAngles = EyeAngles();
			PlayerAnimState.Update(eyeAngles[YAW], eyeAngles[PITCH]);
		}

		UpdateBaseClientSideAnimation();
	}

	class CreateMoveCmdRef
	{
		public UserCmd Cmd;
		public AnonymousSafeFieldPointer<UserCmd> Ptr => new(this, static o => ref ((CreateMoveCmdRef)o).Cmd);
	}

	static readonly CreateMoveCmdRef CreateMoveCmd = new();

	static void CreateMoveSWEPThink() {
		if (gpGlobals.MaxClients >= 2)
			return;
		C_BasePlayer? player = GetLocalPlayer();
		if (player == null)
			return;
		if (player.GetActiveWeapon() is not C_WeaponSWEP swep)
			return;
		if (player.NextAttack <= gpGlobals.CurTime)
			swep.CreateMoveThink();
	}

	public override bool CreateMove(TimeUnit_t inputSampleTime, ref UserCmd cmd) {
		CreateMoveCmd.Cmd = cmd;
		bool ret = CreateMoveInternal(inputSampleTime, ref CreateMoveCmd.Cmd);
		cmd = CreateMoveCmd.Cmd;
		return ret;
	}

	bool CreateMoveInternal(TimeUnit_t inputSampleTime, ref UserCmd cmd) {
		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.StartCommand)) {
			LuaEntity.Push_Entity(GetLocalPlayer());
			LuaUserCmd.Push_UserCmd(CreateMoveCmd.Ptr);
			LuaDisposable disposable = new(g_Lua!.Top(), LuaType.None);
			gGM.CallNoReturns(2);
			disposable.Dispose();
		}

		CreateMoveSWEPThink();

		cmd.IsTyping = false;
		BaseHudChat? chat = (BaseHudChat?)gHUD.FindElement("CHudChat");
		if (chat != null && chat.IsVisible() && chat.GetChatInput() != null && chat.GetChatInput().IsVisible())
			cmd.IsTyping = chat.GetChatInput().GetInputPanel() == vguiInput.GetFocus();

		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.CreateMove)) {
			LuaUserCmd.Push_UserCmd(CreateMoveCmd.Ptr);
			LuaDisposable disposable = new(g_Lua!.Top(), LuaType.None);
			bool handled = gGM.CallFinish(1);
			disposable.Dispose();
			if (handled)
				return true;
		}

		bool ret = base.CreateMove(inputSampleTime, ref cmd);

		if (gpGlobals.MaxClients == 1 && IsLocalPlayer())
			UpdateButtonState(cmd.Buttons);

		if ((GModPlayerFlags & 8) != 0)
			AvoidPlayers(ref cmd);

		cmd.WorldClickDirection = default;
		cmd.WorldClicking = false;

		if (surface.IsCursorVisible()) {
			Panel? panel = vguiInput.GetMouseFocus() as Panel;
			if (panel == null)
				panel = vguiInput.GetMouseOver() as Panel;

			if (panel != null && panel.IsWorldClicker() && panel.IsVisible()) {
				vguiInput.GetCursorPos(out int x, out int y);
				cmd.WorldClickDirection = LuaGui.ScreenToVector(x, y);
				cmd.WorldClicking = true;
			}
		}

		if (cmd.WorldClicking && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PreventScreenClicks) && !gGM.CallFinish(0)) {
			cmd.Buttons &= ~(InButtons.Attack | InButtons.Attack2);
			for (int i = 0; i < 5; i++) {
				if (inputsystem.IsButtonDown(ButtonCode.MouseLeft))
					cmd.Buttons |= InButtons.Attack;
				if (inputsystem.IsButtonDown(ButtonCode.MouseRight))
					cmd.Buttons |= InButtons.Attack2;
			}
		}

		if (gpGlobals.MaxClients == 1) {
			WorldClicking = cmd.WorldClicking;
			WorldClickVector = cmd.WorldClickDirection;
		}

		return ret;
	}

	public override void UpdateButtonState(InButtons userCmdButtonMask) {
		base.UpdateButtonState(userCmdButtonMask);
		if (gGM == null)
			return;

		uint bit = 1;
		for (int i = 0; i < 32; i++) {
			if (((uint)AfButtonPressed & bit) != 0) {
				if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.KeyPress)) {
					LuaEntity.Push_Entity(this);
					g_Lua!.PushNumber((int)bit);
					gGM.CallNoReturns(2);
				}
			}
			else if (((uint)AfButtonReleased & bit) != 0 && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.KeyRelease)) {
				LuaEntity.Push_Entity(this);
				g_Lua!.PushNumber((int)bit);
				gGM.CallNoReturns(2);
			}
			bit = BitOperations.RotateLeft(bit, 1);
		}
	}

	void AvoidPlayers(ref UserCmd cmd) {
		if (!IsAlive() || GetMoveType() == Source.MoveType.Noclip)
			return;

		Vector3 playerCenter = GetAbsOrigin();
		Vector3 playerMin = GetPlayerMins();
		Vector3 playerMax = GetPlayerMaxs();
		float zHeight = playerMax.Z - playerMin.Z;
		playerCenter.Z += 0.5f * zHeight;
		playerMin += playerCenter;
		playerMax += playerCenter;

		for (int i = 0; i < gpGlobals.MaxClients; i++) {
			C_BasePlayer? avoidPlayer = Util.PlayerByIndex(i);
			if (avoidPlayer == null || !avoidPlayer.IsPlayer() || avoidPlayer == this)
				continue;
			if (!InSameTeam(avoidPlayer) || avoidPlayer.IsDormant())
				continue;
			if (avoidPlayer.IsSolidFlagSet(SolidFlags.NotSolid) || avoidPlayer.GetMoveType() == Source.MoveType.Noclip)
				continue;

			Vector3 avoidCenter = avoidPlayer.GetAbsOrigin();
			Vector3 avoidMin = avoidPlayer.GetPlayerMins();
			Vector3 avoidMax = avoidPlayer.GetPlayerMaxs();
			zHeight = avoidMax.Z - avoidMin.Z;
			avoidCenter.Z += 0.5f * zHeight;
			avoidMin += avoidCenter;
			avoidMax += avoidCenter;

			if (!CollisionUtils.IsBoxIntersectingBox(playerMin, playerMax, avoidMin, avoidMax))
				continue;

			Vector3 delta = avoidPlayer.WorldSpaceCenter() - playerCenter;
			Vector3 rad = avoidPlayer.WorldAlignMaxs() - avoidPlayer.WorldAlignMins();
			rad.Z = 0;
			float avoidRadius = rad.Length();

			float pushStrength = (float)MathLib.RemapValClamped(delta.Length(), avoidRadius, 0, 0, 256);
			if (pushStrength < 0.01f)
				return;

			Vector3 push;
			if (GetAbsVelocity().Length2DSqr() > 0.1f) {
				Vector3 velocity = GetAbsVelocity();
				velocity.Z = 0.0f;
				MathLib.CrossProduct(new Vector3(0.0f, 0.0f, 1.0f), velocity, out push);
				MathLib.VectorNormalize(ref push);
			}
			else {
				QAngle view = cmd.ViewAngles;
				view.X = 0.0f;
				MathLib.AngleVectors(view, out _, out push, out _);
			}

			Vector3 separationVelocity;
			if (Vector3.Dot(delta, push) < 0)
				separationVelocity = push * pushStrength;
			else
				separationVelocity = push * -pushStrength;

			float maxPlayerSpeed = MaxSpeed();
			if ((GetFlags() & EntityFlags.Ducking) != 0 && GetGroundEntity() != null)
				maxPlayerSpeed *= 1.3333334f;

			if (separationVelocity.LengthSquared() > maxPlayerSpeed * maxPlayerSpeed) {
				separationVelocity.NormalizeInPlace();
				separationVelocity *= maxPlayerSpeed;
			}

			QAngle angles = cmd.ViewAngles;
			angles.X = 0;
			MathLib.AngleVectors(angles, out Vector3 currentDir, out Vector3 rightDir, out _);

			Vector3 direction = separationVelocity;
			MathLib.VectorNormalize(ref direction);

			cmd.ForwardMove += Vector3.Dot(currentDir, direction) * pushStrength;
			cmd.SideMove += Vector3.Dot(rightDir, direction) * pushStrength;

			float forwardScale = 1.0f;
			if (cmd.ForwardMove > 400.0f)
				forwardScale = 400.0f / cmd.ForwardMove;
			else if (cmd.ForwardMove < -400.0f)
				forwardScale = 400.0f / MathF.Abs(cmd.ForwardMove);

			float sideScale = 1.0f;
			if (MathF.Abs(cmd.SideMove) > 400.0f)
				sideScale = 400.0f / MathF.Abs(cmd.SideMove);

			float scale = MathF.Min(forwardScale, sideScale);
			cmd.ForwardMove *= scale;
			cmd.SideMove *= scale;
			return;
		}
	}
}
