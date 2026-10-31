using Game.Client.HL2MP;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.GarrysMod.Lua;
using Source.Common.Hashing;
using Source.Common.Mathematics;

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
}
