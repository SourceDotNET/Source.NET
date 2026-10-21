using Game.Client.HL2MP;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;

using System.Numerics;

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
		nameof(PlayerClass),
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
