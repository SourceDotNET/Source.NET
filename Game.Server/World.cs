global using static Game.Server.WorldGlobals;

using Game.Shared;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Mathematics;
using Source.Engine;

using System.Numerics;

namespace Game.Server;


public static class WorldGlobals
{
	public static World? GetWorldEntity() => World.g_WorldEntity;
}

[LinkEntityToClass("worldspawn")]
[NetworkName("CWorld", Table = "DT_WORLD")]
public partial class World : BaseEntity
{
	public static World? g_WorldEntity { get; private set; }
	public override int RequiredEdictIndex() => 0;

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(WorldMins)) { Bits = -1, Flags = PropFlags.Coord },
		new(nameof(WorldMaxs)) { Bits = -1, Flags = PropFlags.Coord },
		nameof(StartDark),
		new(nameof(MaxOccludeeArea)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MinOccluderArea)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MaxPropScreenSpaceWidth)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MinPropScreenSpaceWidth)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DetailSpriteMaterial)) { Encoding = NetEncoding.String },
	];

	public World() {
		AddEFlags(EFL.NoAutoEdictAttach | EFL.KeepOnRecreateEntities);
		NetworkProp().AttachEdict(INDEXENT(RequiredEdictIndex()));

		ActivityList.Init();
		EventList.Init();
		SetSolid(Source.SolidType.BSP);
		SetMoveType(Source.MoveType.None);
		ColdWorld = false;
	}

	public override void Precache() {
		g_WorldEntity = this;
		g_fGameOver = false;

		ConVarRef stepsize = new("sv_stepsize");
		stepsize.SetValue(18);

		// ConVarRef roomtype = new("room_type");
		// roomtype.SetValue(0);

		Assert(g_pGameRules == null);

		InstallGameRules();
		Assert(g_pGameRules != null);
		g_pGameRules.Init();

		BaseEntity.SetAllowPrecache(true);
		IGameSystem.LevelInitPreEntityAllSystems(GetModelName());

		g_pGameRules.CreateStandardEntities();

		ActivityList.Free();
		ActivityList.RegisterSharedActivities();

		EventList.Free();
		EventList.RegisterSharedEvents();

		// InitBodyQue();

		SENTENCEG_Init();
		// PrecacheStandardParticleSystems();

		BaseCombatWeapon.W_Precache();
		GameServerClientMethods.ClientPrecache();
		g_pGameRules.Precache();
		BaseTempEntity.PrecacheTempEnts();

		for (int i = 0; i < g_DefaultLightstyles.Length; i++)
			engine.LightStyle(i, GetDefaultLightstyleString(i));
		// styles 32-62 are assigned by the light program for switchable lights

		// 63 testing
		engine.LightStyle(63, "a");

		AI_NetworkManager.InitializeAINetworks();
		g_AI_SchedulesManager.LoadAllSchedules();
		g_pGameRules.InitDefaultAIRelationships();

		// BaseCombatCharacter.InitInteractionSystem();

		PrecacheRegister.Precache();

		g_iszFuncBrushClassname = "func_brush";
	}

	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		if (FStrEq(keyName, "skyname")) {
			ConVarRef skyname = new("sv_skyname");
			skyname.SetValue(value);
		}
		else if (FStrEq(keyName, "newunit")) {
			// if (atoi(value) != 0)
			// 	Game_SetOneWayTransition();
		}
		else if (FStrEq(keyName, "world_mins")) {
			Vector3 vec = default;
			UTIL_StringToVector(vec.Base(), value);
			WorldMins = vec;
		}
		else if (FStrEq(keyName, "world_maxs")) {
			Vector3 vec = default;
			UTIL_StringToVector(vec.Base(), value);
			WorldMaxs = vec;
		}
		else
			return base.KeyValue(keyName, value);

		return true;
	}

	public static ReadOnlySpan<char> GetDefaultLightstyleString(int styleIndex) => styleIndex < g_DefaultLightstyles.Length ? g_DefaultLightstyles[styleIndex] : "m";
	static readonly string[] g_DefaultLightstyles = [
		// 0 normal
		"m",
		// 1 FLICKER (first variety)
		"mmnmmommommnonmmonqnmmo",
		// 2 SLOW STRONG PULSE
		"abcdefghijklmnopqrstuvwxyzyxwvutsrqponmlkjihgfedcba",
		// 3 CANDLE (first variety)
		"mmmmmaaaaammmmmaaaaaabcdefgabcdefg",
		// 4 FAST STROBE
		"mamamamamama",
		// 5 GENTLE PULSE 1
		"jklmnopqrstuvwxyzyxwvutsrqponmlkj",
		// 6 FLICKER (second variety)
		"nmonqnmomnmomomno",
		// 7 CANDLE (second variety)
		"mmmaaaabcdefgmmmmaaaammmaamm",
		// 8 CANDLE (third variety)
		"mmmaaammmaaammmabcdefaaaammmmabcdefmmmaaaa",
		// 9 SLOW STROBE (fourth variety)
		"aaaaaaaazzzzzzzz",
		// 10 FLUORESCENT FLICKER
		"mmamammmmammamamaaamammma",
		// 11 SLOW PULSE NOT FADE TO BLACK
		"abcdefghijklmnopqrrqponmlkjihgfedcba",
		// 12 UNDERWATER LIGHT MUTATION
		// this light only distorts the lightmap - no contribution
		// is made to the brightness of affected surfaces
		"mmnnmmnnnmmnn",
	];

	public override void Spawn() {
		SetLocalOrigin(vec3_origin);
		SetLocalAngles(vec3_angle);
		SetModelIndex(1);
		SetModelName(modelinfo.GetModelName(modelinfo.GetModel(GetModelIndex())));
		AddFlag(Source.EntityFlags.WorldBrush);

		g_EventQueue.Init();
		Precache();

		GlobalEntity.Add("is_console", gpGlobals.MapName, GlobalEState.Off);
		GlobalEntity.Add("is_pc", gpGlobals.MapName, GlobalEState.On);
	}

	float WaveHeight;
	[NetworkVar("m_WorldMins")]
	private partial Vector3 WorldMins { get; set; }
	[NetworkVar("m_WorldMaxs")]
	private partial Vector3 WorldMaxs { get; set; }
	[NetworkVar("m_bStartDark")]
	private partial bool StartDark { get; set; }
	[NetworkVar("m_flMaxOccludeeArea")]
	private partial float MaxOccludeeArea { get; set; }
	[NetworkVar("m_flMinOccluderArea")]
	private partial float MinOccluderArea { get; set; }
	[NetworkVar("m_flMaxPropScreenSpaceWidth")]
	private partial float MaxPropScreenSpaceWidth { get; set; }
	[NetworkVar("m_flMinPropScreenSpaceWidth")]
	private partial float MinPropScreenSpaceWidth { get; set; }
	[NetworkVar("m_iszDetailSpriteMaterial")]
	private partial string? DetailSpriteMaterial { get; set; }
	bool ColdWorld;
}
