using Game.Client.HL2MP;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Client.GarrysMod;
using FIELD = FIELD<C_GMOD_Player>;

[LinkEntityToClass("player")]
[NetworkName("CGMOD_Player")]
public partial class C_GMOD_Player() : C_HL2MP_Player()
{
	static ConVar cl_playercolor = new("1.0 0.0 0.0", FCvar.UserInfo | FCvar.Archive | FCvar.ServerCanExecute, "Default Player Model");

	public static readonly RecvTable DT_GMOD_Player = new(DT_HL2MP_Player, [
		RecvPropInt(FIELD.OF(nameof(GModPlayerFlags))),
		RecvPropEHandle(FIELD.OF(nameof(HoveredWidget))),
		RecvPropEHandle(FIELD.OF(nameof(PressedWidget))),
		RecvPropEHandle(FIELD.OF(nameof(Driving))),
		RecvPropInt(FIELD.OF(nameof(DrivingMode))),
		RecvPropInt(FIELD.OF(nameof(PlayerClass))),
		RecvPropBool(FIELD.OF(nameof(CanZoom))),
		RecvPropBool(FIELD.OF(nameof(CanWalk))),
		RecvPropBool(FIELD.OF(nameof(IsTyping))),
		RecvPropFloat(FIELD.OF(nameof(StepSize))),
		RecvPropFloat(FIELD.OF(nameof(JumpPower))),
		RecvPropVector(FIELD.OF_NAMED(nameof(ViewOffset), "m_ViewOffset")),
		RecvPropVector(FIELD.OF(nameof(ViewOffsetDucked))),
		RecvPropFloat(FIELD.OF(nameof(GestureEndTime))),
		RecvPropVector(FIELD.OF(nameof(PlayerColor))),
		RecvPropVector(FIELD.OF(nameof(WeaponColor))),
		RecvPropEHandle(FIELD.OF(nameof(Hands))),
		RecvPropInt(FIELD.OF(nameof(WaterLevel))),
		RecvPropInt(FIELD.OF(nameof(MaxArmor))),
		RecvPropFloat(FIELD.OF(nameof(Gravity))),
		RecvPropBool(FIELD.OF(nameof(SprintEnabled))),
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_GMOD_Player);

	[NetworkName("m_iGModPlayerFlags")]
	public int GModPlayerFlags;
	[NetworkName("m_HoveredWidget")]
	public EHANDLE HoveredWidget = new();
	[NetworkName("m_PressedWidget")]
	public EHANDLE PressedWidget = new();
	[NetworkName("m_Driving")]
	public EHANDLE Driving = new();
	[NetworkName("m_DrivingMode")]
	public int DrivingMode;
	[NetworkName("m_PlayerClass")]
	public int PlayerClass;
	[NetworkName("m_bCanZoom")]
	public bool CanZoom;
	[NetworkName("m_bCanWalk")]
	public bool CanWalk;
	[NetworkName("m_bIsTyping")]
	public bool IsTyping;
	[NetworkName("m_StepSize")]
	public float StepSize;
	[NetworkName("m_JumpPower")]
	public float JumpPower;
	[NetworkName("m_ViewOffsetDucked")]
	public Vector3 ViewOffsetDucked;
	[NetworkName("m_fGestureEndTime")]
	public float GestureEndTime;
	[NetworkName("m_PlayerColor")]
	public Vector3 PlayerColor;
	[NetworkName("m_WeaponColor")]
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
	[NetworkName("m_Hands")]
	public EHANDLE Hands = new();
	[NetworkName("m_iMaxArmor")]
	public int MaxArmor;
	[NetworkName("m_flGravity")]
	public float Gravity;
	[NetworkName("m_bSprintEnabled")]
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
