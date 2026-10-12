#if CLIENT_DLL || GAME_DLL
global using static Game.Shared.ScriptEventGlobals;

namespace Game.Shared;

public static class ScriptEventGlobals
{
	public const int SCRIPT_EVENT_DEAD = 1000;
	public const int SCRIPT_EVENT_NOINTERRUPT = 1001;
	public const int SCRIPT_EVENT_CANINTERRUPT = 1002;
	public const int SCRIPT_EVENT_FIREEVENT = 1003;
	public const int SCRIPT_EVENT_SOUND = 1004;
	public const int SCRIPT_EVENT_SENTENCE = 1005;
	public const int SCRIPT_EVENT_INAIR = 1006;
	public const int SCRIPT_EVENT_ENDANIMATION = 1007;
	public const int SCRIPT_EVENT_SOUND_VOICE = 1008;
	public const int SCRIPT_EVENT_SENTENCE_RND1 = 1009;
	public const int SCRIPT_EVENT_NOT_DEAD = 1010;
	public const int SCRIPT_EVENT_EMPHASIS = 1011;
	public const int SCRIPT_EVENT_BODYGROUPON = 1020;
	public const int SCRIPT_EVENT_BODYGROUPOFF = 1021;
	public const int SCRIPT_EVENT_BODYGROUPTEMP = 1022;
	public const int SCRIPT_EVENT_FIRE_INPUT = 1100;
}
#endif
