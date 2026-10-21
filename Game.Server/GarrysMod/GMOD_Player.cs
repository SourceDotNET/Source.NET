global using static Game.Server.GarrysMod.GMOD_PlayerGlobals;

using Game.Server.HL2MP;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.GarrysMod.Lua;

using System.Numerics;

namespace Game.Server.GarrysMod;

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
	public GMOD_Player() {
		HoveredWidget = new();
		PressedWidget = new();
		Driving = new();
		CanZoom = true;
		CanWalk = true;
		StepSize = 18;
		JumpPower = 200;
		PlayerColor = new(255, 255, 255);
		WeaponColor = new(255, 255, 255);
		Hands = new();
	}

	public static GMOD_Player? CreatePlayer(ReadOnlySpan<char> classname, Edict ed) {
		s_PlayerEdict = ed;
		return (GMOD_Player?)CreateEntityByName(classname);
	}
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(GModPlayerFlags)) { Bits = 5 },
		nameof(HoveredWidget),
		nameof(PressedWidget),
		nameof(Driving),
		new(nameof(DrivingMode)) { Bits = 15 },
		new(nameof(PlayerClass)) { Bits = 15 },
		nameof(CanZoom),
		nameof(CanWalk),
		nameof(IsTyping),
		new(nameof(StepSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(JumpPower)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ViewOffset)) { Name = "m_ViewOffset", Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ViewOffsetDucked)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(GestureEndTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(PlayerColor)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(WeaponColor)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Hands),
		new(nameof(WaterLevel)) { Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(MaxArmor)) { Bits = 32 },
		new(nameof(Gravity)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(SprintEnabled),
	];

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
	[NetworkVar("m_iGModPlayerFlags")]
	public partial int GModPlayerFlags { get; set; }
	[NetworkVar("m_HoveredWidget")]
	public partial EHANDLE HoveredWidget { get; set; }
	[NetworkVar("m_PressedWidget")]
	public partial EHANDLE PressedWidget { get; set; }
	[NetworkVar("m_Driving")]
	public partial EHANDLE Driving { get; set; }
	[NetworkVar("m_DrivingMode")]
	public partial int DrivingMode { get; set; }
	[NetworkVar("m_PlayerClass")]
	public partial int PlayerClass { get; set; }
	[NetworkVar("m_bCanZoom")]
	public partial bool CanZoom { get; set; }
	[NetworkVar("m_bCanWalk")]
	public partial bool CanWalk { get; set; }
	[NetworkVar("m_bIsTyping")]
	public partial bool IsTyping { get; set; }
	[NetworkVar("m_StepSize")]
	public partial float StepSize { get; set; }
	[NetworkVar("m_JumpPower")]
	public partial float JumpPower { get; set; }
	[NetworkVar("m_ViewOffsetDucked")]
	public partial Vector3 ViewOffsetDucked { get; set; }
	[NetworkVar("m_fGestureEndTime")]
	public partial float GestureEndTime { get; set; }
	[NetworkVar("m_PlayerColor")]
	public partial Vector3 PlayerColor { get; set; }
	[NetworkVar("m_WeaponColor")]
	public partial Vector3 WeaponColor { get; set; }

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
	[NetworkVar("m_Hands")]
	public partial EHANDLE Hands { get; set; }
	[NetworkVar("m_iMaxArmor")]
	public partial int MaxArmor { get; set; }
	[NetworkVar("m_flGravity")]
	public partial float Gravity { get; set; }
	[NetworkVar("m_bSprintEnabled")]
	public partial bool SprintEnabled { get; set; }

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
