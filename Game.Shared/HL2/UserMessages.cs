namespace Game.Shared;

public partial class UserMessages
{
	public UserMessages() {
		RegisterUsermessages();
	}
	public void RegisterUsermessages() {
		Register("Geiger", 1);
		Register("Train", 1);
		Register("HudText", -1);
		Register("SayText", -1);
		Register("TextMsg", -1);
		Register("HudMsg", -1);
		Register("ResetHUD", 1);
		Register("GameTitle", 0);
		Register("ItemPickup", -1);
		Register("Shake", 0xd);
		Register("Fade", 10);
		Register("VGUIMenu", -1);
		Register("Rumble", 3);
		Register("Battery", 2);
		Register("Damage", 0x12);
		Register("VoiceMask", 0x21);
		Register("RequestState", 0);
		Register("CloseCaption", -1);
		Register("SquadMemberDied", 0);
		Register("CreditsMsg", 1);
		Register("LogoTimeMsg", 4);
		Register("AchievementEvent", -1);
		Register("UpdateJalopyRadar", -1);
#if CLIENT_DLL
		Game.Client.GarrysMod.LuaUserMessages.RegisterUserMessages(this);
#else
		Game.Server.GarrysMod.LuaUserMessages.RegisterUserMessages(this);
#endif
		// Register("LuaCmd", -1);
		Register("SWEPCmd", -1);
		Register("AmmoPickup", -1);
		Register("WeaponPickup", -1);
#if CLIENT_DLL
		Game.Client.GarrysMod.LuaNetworkedVars.RegisterUserMessages(this);
#else
		Game.Server.GarrysMod.LuaNetworkedVars.RegisterUserMessages(this);
#endif
		Register("BreakModel", -1);
		Register("CheapBreakModel", -1);
	}
}
