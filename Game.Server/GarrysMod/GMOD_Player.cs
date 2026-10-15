global using static Game.Server.GarrysMod.GMOD_PlayerGlobals;

using Game.Server.HL2MP;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.GarrysMod.Lua;

using System.Numerics;

namespace Game.Server.GarrysMod;

using FIELD = FIELD<GMOD_Player>;

public static class GMOD_PlayerGlobals
{
	public static GMOD_Player? ToGMODPlayer(BaseEntity? entity) {
		if (entity == null || !entity.IsPlayer())
			return null;
		return (GMOD_Player?)entity;
	}
}

[LinkEntityToClass("player")]
[NetworkName("CGMOD_Player")]
public partial class GMOD_Player : HL2MP_Player
{
	public static GMOD_Player? CreatePlayer(ReadOnlySpan<char> classname, Edict ed) {
		s_PlayerEdict = ed;
		return (GMOD_Player?)CreateEntityByName(classname);
	}
	public static readonly SendTable DT_GMOD_Player = new(DT_HL2MP_Player, [
		SendPropInt(FIELD.OF(nameof(GModPlayerFlags)), 5, 0),
		SendPropEHandle(FIELD.OF(nameof(HoveredWidget))),
		SendPropEHandle(FIELD.OF(nameof(PressedWidget))),
		SendPropEHandle(FIELD.OF(nameof(Driving))),
		SendPropInt(FIELD.OF(nameof(DrivingMode)), 15, 0),
		SendPropInt(FIELD.OF(nameof(PlayerClass)), 15, 0),
		SendPropBool(FIELD.OF(nameof(CanZoom))),
		SendPropBool(FIELD.OF(nameof(CanWalk))),
		SendPropBool(FIELD.OF(nameof(IsTyping))),
		SendPropFloat(FIELD.OF(nameof(StepSize)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(JumpPower)), 0, PropFlags.NoScale),
		SendPropVector(FIELD.OF_NAMED(nameof(ViewOffset), "m_ViewOffset"), 0, PropFlags.NoScale),
		SendPropVector(FIELD.OF(nameof(ViewOffsetDucked)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(GestureEndTime)), 0, PropFlags.NoScale),
		SendPropVector(FIELD.OF(nameof(PlayerColor)), 0, PropFlags.NoScale),
		SendPropVector(FIELD.OF(nameof(WeaponColor)), 0, PropFlags.NoScale),
		SendPropEHandle(FIELD.OF(nameof(Hands))),
		SendPropInt(FIELD.OF(nameof(WaterLevel)), 2, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(MaxArmor)), 32, 0),
		SendPropFloat(FIELD.OF(nameof(Gravity)), 0, PropFlags.NoScale),
		SendPropBool(FIELD.OF(nameof(SprintEnabled))),
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_GMOD_Player);

	public bool Transition;

	public override void InitialSpawn() {
		SetModel("models/player.mdl");
		base.InitialSpawn();

		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PlayerInitialSpawn)) {
			LuaEntity.Push_Entity(this);
			g_Lua!.PushBool(Transition);
			gGM.CallNoReturns(2);
		}

		LuaNetworkedVars.g_LuaNetworkedVars!.PlayerInsert(this);
	}

	public override void Spawn() {
		base.Spawn();

		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PlayerSpawn)) {
			LuaEntity.Push_Entity(this);
			g_Lua!.PushBool(Transition);
			gGM.CallNoReturns(2);
		}
	}

	public override bool RestrictPlayerPitch() => (GModPlayerFlags & 16) == 0;
	public override bool CanAttack() => PressedWidget.Get() == null;

	public override BaseEntity? FindUseEntity() {
		BaseEntity? ent = base.FindUseEntity();
		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.FindUseEntity)) {
			LuaEntity.Push_Entity(this);
			LuaEntity.Push_Entity(ent);
			if (gGM.CallReturns(2, 1)) {
				ILuaObject ret = g_Lua!.GetReturn(0);
				if (ret.GetType() == LuaType.Entity)
					return (BaseEntity?)ret.GetEntity();
				return null;
			}
		}
		return ent;
	}

	public override BaseEntity EntSelectSpawnPoint() {
		BaseEntity? spot = null;
		while ((spot = gEntList.FindEntityByClassname(spot, "info_player_start")) != null) {
			if (spot.HasSpawnFlags(1))
				return spot;
		}

		return base.EntSelectSpawnPoint();
	}

	public bool ShouldDropWeapon;
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
	public bool CanZoom = true;
	[NetworkName("m_bCanWalk")]
	public bool CanWalk = true;
	[NetworkName("m_bIsTyping")]
	public bool IsTyping;
	[NetworkName("m_StepSize")]
	public float StepSize = 18;
	[NetworkName("m_JumpPower")]
	public float JumpPower = 200;
	[NetworkName("m_ViewOffsetDucked")]
	public Vector3 ViewOffsetDucked;
	[NetworkName("m_fGestureEndTime")]
	public float GestureEndTime;
	[NetworkName("m_PlayerColor")]
	public Vector3 PlayerColor = new(255, 255, 255);
	[NetworkName("m_WeaponColor")]
	public Vector3 WeaponColor = new(255, 255, 255);

	public virtual Vector3 GetPlayerColor() => PlayerColor;

	public virtual void SetPlayerColor(in Vector3 color) {
		if (PlayerColor != color) {
			NetworkStateChanged();
			PlayerColor = color;
		}
	}

	public virtual void SetWeaponColor(in Vector3 color) {
		if (WeaponColor != color) {
			NetworkStateChanged();
			WeaponColor = color;
		}
	}
	[NetworkName("m_Hands")]
	public EHANDLE Hands = new();
	[NetworkName("m_iMaxArmor")]
	public int MaxArmor;
	[NetworkName("m_flGravity")]
	public float Gravity;
	[NetworkName("m_bSprintEnabled")]
	public bool SprintEnabled;

	public override void PlayerRunCommand(UserCmd ucmd, IMoveHelper moveHelper) {
		base.PlayerRunCommand(ucmd, moveHelper);
	}

	public override void PickupObject(BaseEntity obj, bool limitMassAndSize = true) {
		HL2_PickupObject(obj, limitMassAndSize);
	}

	public override void CheckChatText(ReadOnlySpan<char> text) {
		if (!stristr(text, "bloxwich").IsEmpty)
			AwardAchievement((int)GMODAchievementID.GMA_SAY_1, 1);
	}
}
