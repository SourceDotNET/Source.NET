#if CLIENT_DLL || GAME_DLL
global using static Game.Shared.NPCEventGlobals;

#if GAME_DLL
using Game.Server;
#endif

namespace Game.Shared;

public struct AnimEvent
{
	public int Event;
	public string? Options;
	public float Cycle;
	public TimeUnit_t EventTime;
	public AnimEventType Type;
	public BaseAnimating? Source;
}

public static class NPCEventGlobals
{
	public const int EVENT_SPECIFIC = 0;
	public const int EVENT_SCRIPTED = 1000;
	public const int EVENT_SHARED = 2000;
	public const int EVENT_WEAPON = 3000;
	public const int EVENT_CLIENT = 5000;

	public const int EVENT_WEAPON_MELEE_HIT = 3001;
	public const int EVENT_WEAPON_SMG1 = 3002;
	public const int EVENT_WEAPON_MELEE_SWISH = 3003;
	public const int EVENT_WEAPON_SHOTGUN_FIRE = 3004;
	public const int EVENT_WEAPON_THROW = 3005;
	public const int EVENT_WEAPON_AR1 = 3006;
	public const int EVENT_WEAPON_AR2 = 3007;
	public const int EVENT_WEAPON_HMG1 = 3008;
	public const int EVENT_WEAPON_SMG2 = 3009;
	public const int EVENT_WEAPON_MISSILE_FIRE = 3010;
	public const int EVENT_WEAPON_SNIPER_RIFLE_FIRE = 3011;
	public const int EVENT_WEAPON_AR2_GRENADE = 3012;
	public const int EVENT_WEAPON_THROW2 = 3013;
	public const int EVENT_WEAPON_PISTOL_FIRE = 3014;
	public const int EVENT_WEAPON_RELOAD = 3015;
	public const int EVENT_WEAPON_THROW3 = 3016;
	public const int EVENT_WEAPON_RELOAD_SOUND = 3017;
	public const int EVENT_WEAPON_RELOAD_FILL_CLIP = 3018;
	public const int EVENT_WEAPON_SMG1_BURST1 = 3101;
	public const int EVENT_WEAPON_SMG1_BURSTN = 3102;
	public const int EVENT_WEAPON_AR2_ALTFIRE = 3103;
	public const int EVENT_WEAPON_SEQUENCE_FINISHED = 3900;
	public const int EVENT_WEAPON_LAST = 3999;
}
#endif
