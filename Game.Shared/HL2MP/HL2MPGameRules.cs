#if CLIENT_DLL || GAME_DLL
#if CLIENT_DLL
global using static Game.Client.GarrysMod.HL2MP_GameRules_Globals;
#else
global using static Game.Server.GarrysMod.HL2MP_GameRules_Globals;
#endif
#if CLIENT_DLL
global using HL2MPGameRules = Game.Client.GarrysMod.C_HL2MPGameRules;
global using HL2MPGameRulesProxy = Game.Client.GarrysMod.C_HL2MPGameRulesProxy;
namespace Game.Client.GarrysMod;
#else
global using HL2MPGameRules = Game.Server.GarrysMod.HL2MPGameRules;
global using HL2MPGameRulesProxy = Game.Server.GarrysMod.HL2MPGameRulesProxy;
namespace Game.Server.GarrysMod;
#endif

using Source.Common;
using Source;

using Game.Shared;
using Source.Common.Engine;

[LinkEntityToClass("hl2mp_gamerules")]
[NetworkName("CHL2MPGameRulesProxy")]
public partial class
#if CLIENT_DLL
	C_HL2MPGameRulesProxy
#else
	HL2MPGameRulesProxy
#endif
	: GameRulesProxy
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(HL2MPGameRules.Props)) { Name = "hl2mp_gamerules_data", Proxy = typeof(HL2MPGameRulesProxy.RulesProxy) },
	];

	public override GameRules GameRules => hl2mp_gamerules_data;
	public HL2MPGameRules hl2mp_gamerules_data = new();
	public static class RulesProxy
	{
#if CLIENT_DLL
		public static void Recv(RecvProp prop, out object? outInstance, object? instance, IFieldAccessor fieldInfo, int objectID) {
			outInstance = ((HL2MPGameRulesProxy)instance!).hl2mp_gamerules_data;
		}
#else
		public static object? Send(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID) => SendProxy_HL2MPRules(prop, instance, data, recipients, objectID);
#endif
	}

#if !CLIENT_DLL
	public static object SendProxy_HL2MPRules(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID) {
		HL2MPGameRules rules = HL2MPRules();
		Assert(rules != null);
		return rules;
	}
#endif
}

public partial class
#if CLIENT_DLL
	C_HL2MPGameRules
#else
	HL2MPGameRules
#endif
	: MultiplayRules
// TODO: AutoGameSystemPerFrame
{
	[NetworkTable("DT_HL2MPRules")]
	public static readonly NetworkProp[] Props = [
		nameof(TeamPlayEnabled),
	];

	public
#if CLIENT_DLL
		C_HL2MPGameRules
#else
		HL2MPGameRules
#endif
	() : base() {

	}
	public override ReadOnlySpan<char> Name() => "HL2MPGameRules";
	[NetworkVar("m_bTeamPlayEnabled")]
	public partial bool TeamPlayEnabled { get; set; }

	public override bool ShouldCollide(CollisionGroup collisionGroup0, CollisionGroup collisionGroup1) {
		if (collisionGroup0 > collisionGroup1) {
			// swap so that lowest is always first
			(collisionGroup0, collisionGroup1) = (collisionGroup1, collisionGroup0);
		}

		if ((collisionGroup0 == CollisionGroup.Player || collisionGroup0 == CollisionGroup.PlayerMovement) &&
			collisionGroup1 == CollisionGroup.Weapon) {
			return false;
		}

		return base.ShouldCollide(collisionGroup0, collisionGroup1);

	}
}

#if !CLIENT_DLL
public class VoiceGameMgrHelper : IVoiceGameMgrHelper
{
	public bool CanPlayerHearPlayer(BasePlayer listener, BasePlayer talker, ref bool proximity) {
		return listener.GetTeamNumber() == talker.GetTeamNumber();
	}
}
#endif

public static class HL2MP_GameRules_Globals
{
	public const int TEAM_COMBINE = 2;
	public const int TEAM_REBELS = 3;

	static readonly AmmoDef def = new();
	static bool initted = false;

	public static HL2MPGameRules HL2MPRules() => (HL2MPGameRules)g_pGameRules;

	public static float BULLET_MASS_GRAINS_TO_LB(int grains) => 0.002285f * (grains) / 16.0f;
	public static float BULLET_MASS_GRAINS_TO_KG(int grains) => lbs2kg(BULLET_MASS_GRAINS_TO_LB(grains));
	public const float BULLET_IMPULSE_EXAGGERATION = 3.5f;
	public static float BULLET_IMPULSE(int grains, float ftpersec)
		=> ((ftpersec) * 12 * BULLET_MASS_GRAINS_TO_KG(grains) * BULLET_IMPULSE_EXAGGERATION);

	public static AmmoDef GetAmmoDef() {
		if (!initted) {
			initted = true;

			def.AddAmmoType("AR2", DamageType.Bullet, AmmoTracer.LineAndWhiz, 0, 0, 60, BULLET_IMPULSE(200, 1225), 0);
			def.AddAmmoType("AR2AltFire", DamageType.Dissolve, AmmoTracer.None, 0, 0, 3, 0, 0);
			def.AddAmmoType("Pistol", DamageType.Bullet, AmmoTracer.LineAndWhiz, 0, 0, 150, BULLET_IMPULSE(200, 1225), 0);
			def.AddAmmoType("SMG1", DamageType.Bullet, AmmoTracer.LineAndWhiz, 0, 0, 225, BULLET_IMPULSE(200, 1225), 0);
			def.AddAmmoType("357", DamageType.Bullet, AmmoTracer.LineAndWhiz, 0, 0, 12, BULLET_IMPULSE(800, 5000), 0);
			def.AddAmmoType("XBowBolt", DamageType.Bullet, AmmoTracer.Line, 0, 0, 10, BULLET_IMPULSE(800, 8000), 0);
			def.AddAmmoType("Buckshot", DamageType.Bullet | DamageType.Buckshot, AmmoTracer.Line, 0, 0, 30, BULLET_IMPULSE(400, 1200), 0);
			def.AddAmmoType("RPG_Round", DamageType.Burn, AmmoTracer.None, 0, 0, 3, 0, 0);
			def.AddAmmoType("SMG1_Grenade", DamageType.Burn, AmmoTracer.None, 0, 0, 3, 0, 0);
			def.AddAmmoType("Grenade", DamageType.Burn, AmmoTracer.None, 0, 0, 5, 0, 0);
			def.AddAmmoType("slam", DamageType.Burn, AmmoTracer.None, 0, 0, 5, 0, 0);
		}

		return def;
	}
}

#endif
