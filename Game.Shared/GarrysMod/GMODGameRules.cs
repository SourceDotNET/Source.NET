#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
#if CLIENT_DLL
global using static Game.Client.GarrysMod.GMOD_GameRules_Globals;
#else
global using static Game.Server.GarrysMod.GMOD_GameRules_Globals;
#endif
#if CLIENT_DLL
global using GMODGameRules = Game.Client.GarrysMod.C_GMODGameRules;
global using GMODGameRulesProxy = Game.Client.GarrysMod.C_GMODGameRulesProxy;
namespace Game.Client.GarrysMod;
#else
global using GMODGameRules = Game.Server.GarrysMod.GMODGameRules;
global using GMODGameRulesProxy = Game.Server.GarrysMod.GMODGameRulesProxy;
namespace Game.Server.GarrysMod;
#endif

using Source.Common;
using Source.Common.Engine;
using Source.Common.GarrysMod.Lua;
using Source;

using Game.Shared;
using Game.Shared.GarrysMod;
using Source.GUI.Controls;

public static class GMOD_GameRules_Globals
{
	static readonly GameRulesRegister s_GMODRulesRegister = new("CGMODRules", () => new GMODGameRules());

	public static GMODGameRules GMODRules() => (GMODGameRules)g_pGameRules;
}

[LinkEntityToClass("gmod_gamerules")]
[NetworkName("CGMODGameRulesProxy")]
public partial class
#if CLIENT_DLL
	C_GMODGameRulesProxy
#else
	GMODGameRulesProxy
#endif
	: GameRulesProxy
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(GMODGameRules.Props)) { Name = "gmod_gamerules_data", Proxy = typeof(GMODGameRulesProxy.RulesProxy) },
	];

	public override GameRules GameRules => GMODRules();
	public static class RulesProxy
	{
#if CLIENT_DLL
		public static void Recv(RecvProp prop, out object? outInstance, object? instance, IFieldAccessor fieldInfo, int objectID) => RecvProxy_GMODRules(prop, out outInstance, instance, fieldInfo, objectID);
#else
		public static object? Send(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID) => SendProxy_GMODRules(prop, instance, data, recipients, objectID);
#endif
	}

#if CLIENT_DLL
	public static void RecvProxy_GMODRules(RecvProp prop, out object? outInstance, object? instance, IFieldAccessor fieldInfo, int objectID) {
		GMODGameRules rules = GMODRules();
		Assert(rules != null);
		outInstance = rules;
	}
#else
	public static object SendProxy_GMODRules(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID) {
		GMODGameRules rules = GMODRules();
		Assert(rules != null);
		return rules;
	}
#endif
}

public partial class
#if CLIENT_DLL
	C_GMODGameRules
#else
	GMODGameRules
#endif
	: HL2MPGameRules
// TODO: AutoGameSystemPerFrame
{
	[NetworkTable("DT_GMODRules")]
	public static readonly new NetworkProp[] Props = [
		new(nameof(TimeScale)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
		new(nameof(SkillLevel)) { Bits = 4, Flags = PropFlags.Unsigned },
	];

	public override ReadOnlySpan<char> Name() => "GMODGameRules";

	[NetworkVar("m_fTimeScale")]
	public partial float TimeScale { get; set; }
	[NetworkVar("m_iSkillLevel")]
	public partial int SkillLevel { get; set; }

#if GAME_DLL
	public override bool FlPlayerFallDeathDoesScreenFade(BasePlayer player) {
		return base.FlPlayerFallDeathDoesScreenFade(player);
	}
	public override float FlPlayerFallDamage(BasePlayer player) {
		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.GetFallDamage)) {
			LuaEntity.Push_Entity(player);
			g_Lua!.PushNumber(player.Local.FallVelocity);
			if (gGM.CallReturns(2, 1))
				return g_Lua.GetReturn(0).GetFloat();
		}
		return 10;
	}

	public override void ClientDisconnected(Edict client) {
		BasePlayer? player = (BasePlayer?)BaseEntity.Instance(client);
		if (player != null && player.IsPlayer()) {
			if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PlayerDisconnected)) {
				LuaEntity.Push_Entity(player);
				gGM.CallNoReturns(1);
			}
			base.ClientDisconnected(client);
		}
	}

	public override void DeathNotice(BasePlayer victim, in TakeDamageInfo info) {
		if (gGM == null || !gGM.CallWithArgs((int)LUA_POOLEDSTRING.PlayerDeath))
			return;
		LuaEntity.Push_Entity(victim);
		LuaEntity.Push_Entity(info.GetInflictor());
		LuaEntity.Push_Entity(info.GetAttacker());
		gGM.CallNoReturns(3);
	}

	public override bool CanHaveItem(BasePlayer player, Item item) {
		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PlayerCanPickupItem)) {
			LuaEntity.Push_Entity(player);
			LuaEntity.Push_Entity(item);
			if (!gGM.CallFinish(2))
				return false;
		}
		return true;
	}

	public override void InitDefaultAIRelationships() {
		int i, j;
		BaseCombatCharacter.AllocateDefaultRelationships();

		for (i = 0; i < (int)Class_T.NumAIClasses; i++) {
			for (j = 0; j < (int)Class_T.NumAIClasses; j++)
				BaseCombatCharacter.SetDefaultRelationship((Class_T)i, (Class_T)j, Disposition.NU, 0);
		}

		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.CitizenPassive, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Combine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.CombineHunter, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Manhack, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.MetroPolice, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Military, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Scanner, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Stalker, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.ProtoSniper, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.Antlion, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Antlion, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Barnacle, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.CitizenPassive, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Combine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.CombineHunter, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Manhack, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.MetroPolice, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Military, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Stalker, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.EarthFauna, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Barnacle, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Player, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Antlion, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.CitizenRebel, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Combine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.CombineHunter, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Conscript, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Headcrab, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Vortigaunt, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.Zombie, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.PlayerAlly, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.PlayerAllyVital, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Bullseye, Class_T.HackedRollermine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Player, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Barnacle, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.CitizenRebel, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Combine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.CombineHunter, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Conscript, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Headcrab, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Manhack, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Missile, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Vortigaunt, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.Zombie, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.PlayerAlly, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.PlayerAllyVital, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenPassive, Class_T.HackedRollermine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Player, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Barnacle, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.CitizenRebel, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Combine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.CombineHunter, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Conscript, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Manhack, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.MetroPolice, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Military, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Missile, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Scanner, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Stalker, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Vortigaunt, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.PlayerAlly, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.PlayerAllyVital, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CitizenRebel, Class_T.HackedRollermine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Barnacle, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Combine, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.CombineGunship, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.CombineHunter, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Combine, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Combine, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.CombineGunship, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.CombineHunter, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Headcrab, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Missile, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineGunship, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.CitizenPassive, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Combine, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.CombineGunship, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.CombineHunter, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.CombineHunter, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Player, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Barnacle, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.CitizenRebel, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Combine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.CombineHunter, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Conscript, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Manhack, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.MetroPolice, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Military, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Scanner, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Stalker, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Vortigaunt, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.PlayerAlly, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.PlayerAllyVital, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Conscript, Class_T.HackedRollermine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Player, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Antlion, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.CitizenRebel, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Combine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.CombineHunter, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Conscript, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Headcrab, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Vortigaunt, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.Zombie, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.PlayerAlly, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.PlayerAllyVital, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Flare, Class_T.HackedRollermine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.CitizenPassive, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Combine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.CombineHunter, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Headcrab, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.MetroPolice, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.Zombie, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Headcrab, Class_T.HackedRollermine, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.CitizenPassive, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Combine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.CombineHunter, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Headcrab, Disposition.HT, -1);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Manhack, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Combine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.CombineHunter, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.MetroPolice, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Combine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.CombineHunter, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Military, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Combine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.CombineHunter, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Missile, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Player, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Antlion, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.CitizenRebel, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Combine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.CombineHunter, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Conscript, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Headcrab, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Vortigaunt, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.Zombie, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.PlayerAlly, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.PlayerAllyVital, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.None, Class_T.HackedRollermine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Player, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Barnacle, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Bullseye, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.CitizenPassive, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.CitizenRebel, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Combine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.CombineGunship, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.CombineHunter, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Conscript, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Manhack, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.MetroPolice, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Military, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Scanner, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Stalker, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Vortigaunt, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.ProtoSniper, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.PlayerAlly, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.PlayerAllyVital, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Player, Class_T.HackedRollermine, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Player, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Barnacle, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.CitizenRebel, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Combine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.CombineHunter, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Conscript, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Headcrab, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Manhack, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.MetroPolice, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Military, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Scanner, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Stalker, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Vortigaunt, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.Zombie, Disposition.FR, 1);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.ProtoSniper, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.PlayerAlly, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.PlayerAllyVital, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAlly, Class_T.HackedRollermine, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Player, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Barnacle, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.CitizenRebel, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Combine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.CombineHunter, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Conscript, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Manhack, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.MetroPolice, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Military, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Scanner, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Stalker, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Vortigaunt, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.ProtoSniper, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.PlayerAlly, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.PlayerAllyVital, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.PlayerAllyVital, Class_T.HackedRollermine, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Combine, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.CombineGunship, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.CombineHunter, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Headcrab, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Manhack, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.MetroPolice, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Military, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Scanner, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Stalker, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.Zombie, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.ProtoSniper, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Scanner, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Combine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.CombineHunter, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Headcrab, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.Zombie, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Stalker, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Player, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Barnacle, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.CitizenPassive, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.CitizenRebel, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Combine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.CombineHunter, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Conscript, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Manhack, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.MetroPolice, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Military, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Scanner, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Stalker, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Vortigaunt, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.PlayerAlly, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.PlayerAllyVital, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Vortigaunt, Class_T.HackedRollermine, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.CitizenPassive, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Combine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.CombineHunter, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Headcrab, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Manhack, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.MetroPolice, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Military, Disposition.FR, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.Zombie, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.Zombie, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.CitizenPassive, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Combine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.CombineHunter, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.MetroPolice, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Military, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Missile, Disposition.NU, 5);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Stalker, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.ProtoSniper, Class_T.HackedRollermine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.None, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Player, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.CitizenPassive, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.CitizenRebel, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Combine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.CombineGunship, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.CombineHunter, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Conscript, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Flare, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Manhack, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.MetroPolice, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Military, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Missile, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Scanner, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Stalker, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Vortigaunt, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.Zombie, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.ProtoSniper, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.EarthFauna, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.PlayerAlly, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.PlayerAllyVital, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.EarthFauna, Class_T.HackedRollermine, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.None, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Player, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Antlion, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Barnacle, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Bullseye, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.CitizenPassive, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.CitizenRebel, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Combine, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.CombineGunship, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.CombineHunter, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Conscript, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Flare, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Headcrab, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Manhack, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.MetroPolice, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Military, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Missile, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Scanner, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Stalker, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Vortigaunt, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.Zombie, Disposition.HT, 1);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.ProtoSniper, Disposition.NU, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.EarthFauna, Disposition.HT, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.PlayerAlly, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.PlayerAllyVital, Disposition.LI, 0);
		BaseCombatCharacter.SetDefaultRelationship(Class_T.HackedRollermine, Class_T.HackedRollermine, Disposition.LI, 0);
	}
#endif
}
#endif
