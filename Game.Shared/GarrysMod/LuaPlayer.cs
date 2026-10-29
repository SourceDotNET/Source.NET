using Source;

#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source.Common.Commands;
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;

using System.Numerics;
using Source.Common.Client;


#if CLIENT_DLL
using GMOD_Player = Game.Client.GarrysMod.C_GMOD_Player;

namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaPlayer
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_player = new("player");

	[LuaClass]
	static readonly LuaEntityClass LC_Player = LuaEntity.LC_Player;

	public static BasePlayer? Get_Player(int stackPos, bool allowNull) {
		BaseEntity? ent = LuaEntity.Get_Entity(stackPos, allowNull);
		if (ent != null && ent.IsPlayer())
			return (BasePlayer)ent;
		if (allowNull)
			return null;
		g_Lua!.Error("Player entity is NULL or not a player (!?)");
		return null;
	}

#if CLIENT_DLL
	static void RequestConnectToServer(string address) {
		if (address.Contains('\n') || address.Contains('\r'))
			return;

		ILuaInterface? menu = get.LuaShared()!.GetLuaInterface(2);
		if (menu == null)
			return;

		menu.PushSpecial(Special.Glob);
		menu.GetField(-1, "RequestConnectToServer");
		menu.PushString(address);
		menu.CallFunctionProtected(1, 0, true);
		menu.Pop(1);
	}

	static void ConnectToServer(string address) {
		address = address.Trim(" \t".ToCharArray());
		if (!address.Contains(':'))
			address += ":27015";
		RequestConnectToServer(address);
	}
#endif

	[LuaMethod]
	static int Player__ConCommand(ILuaInterface lua) {
#if CLIENT_DLL
		string command = lua.CheckString(2);
		string? blocked = LuaConCommands.ConCommand_ParseAndCheckBlocked(command);
		if (blocked == null) {
			engine.ClientCmd(command);
			return 0;
		}

		if (strcmp(blocked, "connect") != 0) {
			lua.ErrorFromLua($"ConCommand is blocked! ({blocked})");
			return 0;
		}

		string address = command[(command.IndexOf(blocked, StringComparison.Ordinal) + 8)..];
		int semicolon = address.IndexOf(';');
		if (semicolon != -1)
			address = address[..semicolon];
		ConnectToServer(address);
		return 0;
#else
		string command = lua.CheckString(2);
		string? blocked = LuaConCommands.ConCommand_ParseAndCheckBlocked(command);
		if (blocked != null) {
			lua.ErrorFromLua($"ConCommand is blocked! ({blocked})");
			return 0;
		}
		BasePlayer player = Get_Player(1, false)!;
		engine.ClientCommand(player.Edict(), command);
		return 0;
#endif
	}

	[LuaMethod]
	static int Player__IsPlayer(ILuaInterface lua) {
		lua.PushBool(true);
		return 1;
	}

	[LuaMethod]
	static int Player__SteamID(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushString(player.GetNetworkIDString());
		return 1;
	}

	[LuaMethod]
	static int Player__SteamID64(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		if (!player.GetSteamID(out Steamworks.CSteamID steamID)) {
			lua.PushString("0");
			return 1;
		}
		lua.PushString(steamID.m_SteamID.ToString());
		return 1;
	}

	[LuaMethod]
	static int Player__AccountID(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		if (!player.GetSteamID(out Steamworks.CSteamID steamID)) {
			lua.PushNumber(0);
			return 1;
		}
		lua.PushNumber(steamID.GetAccountID().m_AccountID);
		return 1;
	}

#if GAME_DLL
	[LuaMethod]
	static int Player__OwnerSteamID64(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushString(engine.GMOD_GetPlayerOwnerSteamID(player.Edict()).m_SteamID.ToString());
		return 1;
	}
#endif

	[LuaMethod]
	static int Player__KeyDown(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushBool(((int)lua.CheckNumber(2) & (int)player.Buttons) != 0);
		return 1;
	}

	[LuaMethod]
	static int Player__Alive(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		g_Lua!.PushBool(player.IsAlive());
		return 1;
	}

	[LuaMethod]
	static int Player__GetActiveWeapon(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		LuaEntity.Push_Entity(player.GetActiveWeapon());
		return 1;
	}

	[LuaMethod]
	static int Player__TranslateWeaponActivity(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		int act = (int)lua.CheckNumber(2);
		BaseCombatWeapon? weapon = player.GetActiveWeapon();
		if (weapon != null) {
			bool required = false;
			act = (int)weapon.ActivityOverride((Activity)act, ref required);
		}
		lua.PushNumber(act);
		return 1;
	}

#if GAME_DLL
	[LuaMethod]
	static int Player__UnSpectate(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		player.StopObserverMode();
		player.ResetObserverMode();
		player.RemoveEFlags(EFL.NoClipActive);
		player.SetMoveType(MoveType.Walk);
		return 0;
	}
#endif

	[LuaMethod]
	static int Player__SetClassID(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		int classID = (int)lua.CheckNumber(2);
		if (gpGlobals.MaxClients > 1 && (uint)(classID + 16384) > 32767)
			lua.ErrorNoHalt($"Out of bounds value {classID} for networking in range {-16384} to {16383}\n");

		GMOD_Player gmodPlayer = (GMOD_Player)player;
#if GAME_DLL
		if (gmodPlayer.PlayerClass != classID)
			gmodPlayer.NetworkStateChanged();
#endif
		gmodPlayer.PlayerClass = classID;
		return 1;
	}

	[LuaMethod]
	static int Player__GetClassID(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushNumber(((GMOD_Player)player).PlayerClass);
		return 1;
	}

	[LuaMethod]
	static int Player__GetAllowWeaponsInVehicle(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushBool(player.GetAllowWeaponsInVehicle());
		return 1;
	}

	[LuaMethod]
	static int Player__IsPlayingTaunt(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushBool(((GMOD_Player)player).GestureEndTime > gpGlobals.CurTime);
		return 1;
	}

	[LuaMethod]
	static int Player__IsTyping(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushBool(((GMOD_Player)player).IsTyping);
		return 1;
	}

	[LuaMethod]
	static int Player__IsSpeaking(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
#if CLIENT_DLL
		if (player.IsLocalPlayer())
			lua.PushBool(GetClientVoiceMgr()!.IsLocalPlayerSpeaking());
		else
			lua.PushBool(GetClientVoiceMgr()!.IsPlayerSpeaking(player.EntIndex()));
#else
		lua.PushBool(gpGlobals.CurTime - player.LastTimePlayerTalked() < 0.1);
#endif
		return 1;
	}

#if CLIENT_DLL
	[LuaMethod]
	static int Player__VoiceVolume(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		int index = player.EntIndex();
		if ((uint)(index - 1) < 128) {
			lua.PushNumber(HLClient.g_VoiceVolume[index - 1]);
			return 1;
		}
		return 0;
	}
#endif

	[LuaMethod]
	static int Player__InVehicle(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushBool(player.IsInAVehicle());
		return 1;
	}

	[LuaMethod]
	static int Player__AnimResetGestureSlot(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		HL2MPPlayerAnimState? animState = (player as HL2MP_Player)?.PlayerAnimState;
		if (animState == null)
			return 0;

		animState.ResetGestureSlot((int)lua.CheckNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Player__AnimRestartGesture(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		HL2MPPlayerAnimState? animState = (player as HL2MP_Player)?.PlayerAnimState;
		if (animState == null)
			return 0;

		animState.RestartGesture((int)lua.CheckNumber(2), (Activity)(int)lua.CheckNumber(3), lua.GetBool(4));
		return 0;
	}

	[LuaMethod]
	static int Player__AnimRestartMainSequence(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		HL2MPPlayerAnimState? animState = (player as HL2MP_Player)?.PlayerAnimState;
		if (animState == null)
			return 0;

		animState.RestartMainSequence();
		return 0;
	}

	[LuaMethod]
	static int Player__AnimSetGestureWeight(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		HL2MPPlayerAnimState? animState = (player as HL2MP_Player)?.PlayerAnimState;
		if (animState == null)
			return 0;

		AnimationLayerRef? layer = animState.GetGestureSlotLayer((int)lua.CheckNumber(2));
		if (layer == null)
			return 0;

		layer.Weight = (float)lua.CheckNumber(3);
		return 0;
	}

	[LuaMethod]
	static int Player__HasWeapon(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		string? className = lua.CheckString(2);
		if (className != null) {
			for (int i = 0; i < MAX_WEAPONS; i++) {
				BaseEntity? weapon = player.GetWeapon(i);
				if (weapon == null)
					continue;
#if CLIENT_DLL
				ReadOnlySpan<char> weaponClass = weapon.GetClassname();
#else
				ReadOnlySpan<char> weaponClass = weapon.Classname ?? "";
#endif
				if (stricmp(className, weaponClass) == 0) {
					lua.PushBool(true);
					return 1;
				}
			}
		}
		lua.PushBool(false);
		return 1;
	}

	[LuaMethod]
	static int Player__GetWeapon(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		string? className = lua.CheckString(2);
		if (className != null) {
			for (int i = 0; i < MAX_WEAPONS; i++) {
				BaseEntity? weapon = player.GetWeapon(i);
				if (weapon == null)
					continue;
#if CLIENT_DLL
				ReadOnlySpan<char> weaponClass = weapon.GetClassname();
				if (weaponClass.IsEmpty)
					continue;
#else
				ReadOnlySpan<char> weaponClass = weapon.Classname ?? "";
#endif
				if (stricmp(className, weaponClass) == 0) {
					LuaEntity.Push_Entity(weapon);
					return 1;
				}
			}
		}
		LuaEntity.Push_Entity(null);
		return 1;
	}

	[LuaMethod]
	static int Player__GetAimVector(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		Vector3 forward;
		if (!player.WorldClicking || player.DisableWorldClicking) {
			QAngle angles;
			if (player.IsInAVehicle()) {
				player.CacheVehicleView();
				angles = player.VehicleViewAngles;
			}
			else
				angles = player.EyeAngles();
			MathLib.AngleVectors(angles, out forward);
		}
		else
			forward = player.WorldClickVector;
		LuaVector.Push_Vector(forward);
		return 1;
	}

	[LuaMethod]
	static int Player__GetVehicle(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
#if CLIENT_DLL
		LuaEntity.Push_Entity(player.GetVehicle()?.GetVehicleEnt());
#else
		LuaEntity.Push_Entity(player.IsInAVehicle() ? player.GetVehicleEntity() : null);
#endif
		return 1;
	}

	[LuaMethod]
	static int Player__GetWeapons(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		LuaTable table = new(null, MAX_WEAPONS);
		int count = 0;
		for (int i = 0; i < MAX_WEAPONS; i++) {
			BaseCombatWeapon? weapon = player.MyWeapons[i].Get();
			if (weapon != null)
				table.SetMemberEntity(++count, weapon);
		}
		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaMethod]
	static int Player__GetUseEntity(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
#if CLIENT_DLL
		LuaEntity.Push_Entity(((GMOD_Player)player).FindUseEntity());
#else
		LuaEntity.Push_Entity(player.FindUseEntity());
#endif
		return 1;
	}

	[LuaMethod]
	static int Player__SetFOV(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		BaseEntity requester = player;
		if (lua.GetType(4) != LuaType.Nil)
			requester = LuaEntity.Get_Entity(4, false)!;
		double zoomRate = lua.CheckNumberOpt(3, 0);
		double fov = lua.GetNumber(2);
		player.SetFOV(requester, (int)(float)fov, (float)zoomRate, 0);
		return 0;
	}

	[LuaMethod]
	static int Player__GetFOV(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushNumber(player.GetFOV());
		return 1;
	}

	[LuaMethod]
	static int Player__Nick(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushString(player.GetPlayerName());
		return 1;
	}

	[LuaMethod]
	static int Player__GetShootPos(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		LuaVector.Push_Vector(player.Weapon_ShootPosition());
		return 1;
	}

	[LuaMethod]
	static int Player__SetRunSpeed(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		player.SetSprintSpeed((float)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Player__SetWalkSpeed(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		player.SetWalkSpeed((float)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Player__SetSlowWalkSpeed(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		player.SetSlowWalkSpeed((float)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Player__SetCrouchedWalkSpeed(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		player.SetCrouchedWalkSpeed((float)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Player__SetDuckSpeed(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		player.SetDuckSpeed((float)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Player__SetUnDuckSpeed(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		player.SetUnDuckSpeed((float)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Player__SetJumpPower(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		float power = (float)lua.GetNumber(2);
		if (player.JumpPower != power) {
#if GAME_DLL
			player.NetworkStateChanged();
#endif
			player.JumpPower = power;
		}
		return 0;
	}

	[LuaMethod]
	static int Player__SetAvoidPlayers(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		int flags = lua.GetBool(2) ? player.GModPlayerFlags | 8 : player.GModPlayerFlags & ~8;
#if GAME_DLL
		if (player.GModPlayerFlags != flags)
			player.NetworkStateChanged();
#endif
		player.GModPlayerFlags = flags;
		return 0;
	}

	[LuaMethod]
	static int Player__SetNoCollideWithTeammates(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		int flags = lua.GetBool(2) ? player.GModPlayerFlags | 2 : player.GModPlayerFlags & ~2;
#if GAME_DLL
		if (player.GModPlayerFlags != flags)
			player.NetworkStateChanged();
#endif
		player.GModPlayerFlags = flags;
		return 0;
	}

#if GAME_DLL
	[LuaMethod]
	static int Player__SetArmor(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		player.SetArmorValue((int)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Player__SetMaxArmor(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		int armor = (int)lua.GetNumber(2);
		if (player.MaxArmor != armor) {
			player.NetworkStateChanged();
			player.MaxArmor = armor;
		}
		return 0;
	}

	[LuaMethod]
	static int Player__ShouldDropWeapon(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		player.ShouldDropWeapon = lua.GetBool(2);
		return 0;
	}
#endif

	[LuaMethod]
	static int Player__GetInfo(ILuaInterface lua) {
		if (!LuaConVar.IsAllowedToGetConvarInfo(lua.GetString(2)))
			return 0;
#if CLIENT_DLL
		ConVar? convar = cvar.FindVar(lua.GetString(2));
		if (convar == null)
			return 0;
		lua.PushString(convar.GetString());
		return 1;
#else
		BasePlayer player = Get_Player(1, false)!;
		if (player.IsFakeClient()) {
			lua.PushString("");
			return 1;
		}
		lua.PushString(player.GetInfo(lua.GetString(2)));
		return 1;
#endif
	}

	[LuaMethod]
	static int Player__GetInfoNum(ILuaInterface lua) {
		if (!LuaConVar.IsAllowedToGetConvarInfo(lua.GetString(2)))
			return 0;
		float fallback = (float)lua.GetNumber(3);
#if CLIENT_DLL
		ConVar? convar = cvar.FindVar(lua.GetString(2));
		if (convar != null)
			fallback = convar.GetFloat();
		lua.PushNumber(fallback);
		return 1;
#else
		BasePlayer player = Get_Player(1, false)!;
		if (player.IsFakeClient()) {
			lua.PushNumber(0);
			return 1;
		}
		lua.PushNumber(player.GetInfoNum(lua.GetString(2), fallback));
		return 1;
#endif
	}

#if GAME_DLL
	[LuaMethod]
	static int Player__RemoveAllAmmo(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		player.RemoveAllAmmo();
		return 0;
	}

	[LuaMethod]
	static int Player__SelectWeapon(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		if (lua.GetType(2) == LuaType.Entity) {
			BaseCombatWeapon? weapon = LuaEntity.Get_Weapon(2, true);
			if (weapon != null) {
				player.SelectItem(weapon.Classname ?? "", 0);
				return 0;
			}
		}
		player.SelectItem(lua.GetString(2), 0);
		return 0;
	}

	[LuaMethod]
	static int Player__Give(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		bool noAmmo = false;
		if (lua.GetType(3) != LuaType.Nil) {
			noAmmo = lua.GetBool(3);
			if (noAmmo && player.MyCombatCharacterPointer() != null)
				player.MyCombatCharacterPointer()!.NoAmmo = true;
		}
		BaseEntity? ent = player.GiveNamedItem(lua.GetString(2), 0);
		if (noAmmo && player.MyCombatCharacterPointer() != null) {
			player.MyCombatCharacterPointer()!.NoAmmo = false;
			if (ent is BaseCombatWeapon weapon) {
				int clip1 = weapon.iClip1 >= 0 ? 0 : weapon.iClip1;
				if (weapon.iClip1 != clip1)
					weapon.iClip1 = clip1;
				int clip2 = weapon.iClip2 >= 0 ? 0 : weapon.iClip2;
				if (weapon.iClip2 != clip2)
					weapon.iClip2 = clip2;
			}
		}
		LuaEntity.Push_Entity(ent);
		return 1;
	}

	[LuaMethod]
	static int Player__GiveAmmo(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		int count = (int)lua.GetNumber(2);
		if (count < 1)
			return 0;
		if (lua.GetType(3) == LuaType.String) {
			bool suppress = lua.GetBool(4);
			int ammoIndex = GetAmmoDef().Index(lua.GetString(3));
			lua.PushNumber(player.GiveAmmo(count, ammoIndex, suppress));
		}
		else {
			bool suppress = lua.GetBool(4);
			lua.PushNumber(player.GiveAmmo(count, (int)lua.GetNumber(3), suppress));
		}
		return 1;
	}
#endif

	[LuaMethod]
	static int Player__GetViewModel(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		int index = 0;
		if (lua.GetType(2) == LuaType.Number) {
			int value = (int)lua.GetNumber(2);
			if (value > -1) {
				if (value > 2)
					value = 2;
				index = value;
			}
		}
		LuaEntity.Push_Entity(player.GetViewModel(index, true));
		return 1;
	}

	[LuaMethod]
	static int Player__GetViewEntity(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		BaseEntity? viewEntity = player.GetViewEntity();
		if (viewEntity == null)
			viewEntity = player;
		LuaEntity.Push_Entity(viewEntity);
		return 1;
	}

	[LuaMethod]
	static int Player____tostring(ILuaInterface lua) {
		BasePlayer? player = Get_Player(1, true);
		string str = player == null ? "Player [NULL]" : $"Player [{player.EntIndex()}][{player.GetPlayerName()}]";
		lua.PushString(str.Length > 0xFF ? str[..0xFF] : str);
		return 1;
	}

#if CLIENT_DLL
	[LuaMethod]
	static int Player__GetFriendStatus(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		if (!Steamworks.SteamAPI.IsSteamRunning()) {
			lua.PushString("error_steamworks");
			return 1;
		}

		if (!engine.GetPlayerInfo(player.EntIndex(), out PlayerInfo info)) {
			lua.PushString("error_noplayer");
			return 1;
		}

		string status;
		if (info.FriendsID == 0)
			status = "error_nofriendid";
		else if (info.FakePlayer)
			status = "error_bot";
		else {
			Steamworks.CSteamID steamID = default;
			steamID.InstancedSet(new Steamworks.AccountID_t(info.FriendsID), 1, Steamworks.SteamUtils.GetConnectedUniverse(), Steamworks.EAccountType.k_EAccountTypeIndividual);
			status = Steamworks.SteamFriends.GetFriendRelationship(steamID) switch {
				Steamworks.EFriendRelationship.k_EFriendRelationshipBlocked => "blocked",
				Steamworks.EFriendRelationship.k_EFriendRelationshipRequestRecipient => "requestrecipient",
				Steamworks.EFriendRelationship.k_EFriendRelationshipFriend => "friend",
				Steamworks.EFriendRelationship.k_EFriendRelationshipRequestInitiator => "requested",
				Steamworks.EFriendRelationship.k_EFriendRelationshipIgnored => "blocked",
				_ => "none",
			};
		}
		lua.PushString(status);
		return 1;
	}
#endif

	[LuaMethod]
	static int Player__Team(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushNumber(player.GetTeamNumber());
		return 1;
	}

	[LuaMethod]
	static int Player__GetCurrentCommand(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		if (player.CurrentCommand.IsNull) {
			lua.ErrorNoHalt("Player:GetCurrentCommand called outside of command/prediction\n");
			return 0;
		}
		LuaUserCmd.Push_CurrentCommand(player.CurrentCommand.Get());
		return 1;
	}

#if CLIENT_DLL
	[LuaMethod]
	static int Player__UniqueID(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		lua.PushString(player.GetUniqueID());
		return 1;
	}
#endif

#if GAME_DLL
	[LuaMethod]
	static int Player__SetTeam(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		int team = (int)lua.CheckNumber(2);
		if (gpGlobals.MaxClients > 1 && (team < short.MinValue || team > short.MaxValue))
			lua.ErrorNoHalt($"Out of bounds value {team} for networking in range {short.MinValue} to {short.MaxValue}\n");
		player.ChangeTeam(team);
		return 0;
	}

	[LuaMethod]
	static int Player__SendLua(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		ReadOnlySpan<char> code = lua.CheckString(2);
		if (stristr(code, "while true do end").IsEmpty) {
			Game.Server.RecipientFilter filter = new Game.Server.SingleUserRecipientFilter(player);
			filter.MakeReliable();
			Game.Client.GarrysMod.GarrysMod.BroadcastLua(filter, code);
		}
		return 0;
	}
#endif

	[LuaMethod]
	static int Player__Armor(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
#if CLIENT_DLL
		if (g_pPlayerResource == null)
			return 0;
		lua.PushNumber(g_pPlayerResource.GetArmor(player.EntIndex()));
#else
		lua.PushNumber(player.ArmorValue);
#endif
		return 1;
	}

	[LuaMethod]
	static int Player__GetMaxArmor(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		lua.PushNumber(player.MaxArmor);
		return 1;
	}

	[LuaMethod]
	static int Player__Frags(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
#if CLIENT_DLL
		if (g_pPlayerResource == null)
			return 0;
		lua.PushNumber(g_pPlayerResource.GetFrags(player.EntIndex()));
#else
		lua.PushNumber(player.Frags);
#endif
		return 1;
	}

	[LuaMethod]
	static int Player__Deaths(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
#if CLIENT_DLL
		if (g_pPlayerResource == null)
			return 0;
		lua.PushNumber(g_pPlayerResource.GetDeaths(player.EntIndex()));
#else
		lua.PushNumber(player.Deaths);
#endif
		return 1;
	}

	[LuaMethod]
	static int Player__Ping(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
#if CLIENT_DLL
		if (g_pPlayerResource == null)
			return 0;
		lua.PushNumber(g_pPlayerResource.GetPing(player.EntIndex()));
#else
		Util.GetPlayerConnectionInfo(player.EntIndex(), out int ping, out _);
		lua.PushNumber(ping);
#endif
		return 1;
	}

	[LuaMethod]
	static int Player__GetObserverMode(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushNumber((int)player.GetObserverMode());
		return 1;
	}

	[LuaMethod]
	static int Player__GetObserverTarget(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		LuaEntity.Push_Entity(player.GetObserverTarget());
		return 1;
	}

	[LuaMethod]
	static int Player__Crouching(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushBool((player.GetFlags() & EntityFlags.Ducking) != 0);
		return 1;
	}

	[LuaMethod]
	static int Player__UserID(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushNumber(player.GetUserID());
		return 1;
	}

	[LuaGlobal]
	static int Player(ILuaInterface lua) {
#if CLIENT_DLL
		LuaEntity.Push_Entity(ClientModeShared.USERID2PLAYER((int)lua.CheckNumber(1)));
#else
		LuaEntity.Push_Entity(Util.PlayerByUserId((int)lua.CheckNumber(1)));
#endif
		return 1;
	}

	[LuaMethod]
	static int Player__IsBot(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		lua.PushBool(player.IsBot() || player.IsHLTV());
		return 1;
	}

	[LuaMethod]
	static int Player__GetHands(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		LuaEntity.Push_Entity(player.Hands.Get());
		return 1;
	}

	[LuaMethod]
	static int Player__SetHands(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		BaseEntity? hands = LuaEntity.Get_Entity(2, false);
#if GAME_DLL
		if (player.Hands.Get() != hands)
			player.NetworkStateChanged();
#endif
		player.Hands = EHANDLE.FromEntity(hands);
		return 0;
	}

	[LuaMethod]
	static int Player__GetPlayerColor(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		LuaVector.Push_Vector(player.GetPlayerColor());
		return 1;
	}

	[LuaMethod]
	static int Player__SetPlayerColor(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		Vector3 color = LuaVector.Get_Vector(2);
		player.SetPlayerColor(color);
		return 0;
	}

	[LuaMethod]
	static int Player__GetWeaponColor(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		LuaVector.Push_Vector(player.GetWeaponColor());
		return 1;
	}

	[LuaMethod]
	static int Player__SetWeaponColor(ILuaInterface lua) {
		GMOD_Player player = (GMOD_Player)Get_Player(1, false)!;
		Vector3 color = LuaVector.Get_Vector(2);
		player.SetWeaponColor(color);
		return 0;
	}

	[LuaFunction]
	static int GetByID(ILuaInterface lua) {
		BasePlayer? player = Util.PlayerByIndex((int)g_Lua!.CheckNumber(1));
		LuaEntity.Push_Entity(player);
		return 1;
	}

	[LuaFunction]
	static int GetCount(ILuaInterface lua) {
		int count = 0;
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player == null || player.IsMarkedForDeletion())
				continue;
#if GAME_DLL
			if (player.Connected != PlayerConnectedState.Disconnected)
#endif
			count++;
		}
		g_Lua!.PushNumber(count);
		return 1;
	}

#if GAME_DLL
	[LuaFunction]
	static int GetCountConnecting(ILuaInterface lua) {
		int count = 0;
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player == null && engine.GetPlayerInfo(i, out _))
				count++;
		}
		g_Lua!.PushNumber(count);
		return 1;
	}
#endif

	[LuaFunction]
	static int GetAll(ILuaInterface lua) {
		Assert(ThreadInMainThread());
		lua.PreCreateTable(gpGlobals.MaxClients, 0);
		int count = 0;
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player == null || player.IsMarkedForDeletion())
				continue;
#if GAME_DLL
			if (player.Connected == PlayerConnectedState.Disconnected)
				continue;
#endif
			g_Lua!.PushNumber(++count);
			LuaEntity.Push_Entity(player);
			g_Lua.SetTable(-3);
		}
		return 1;
	}

	[LuaFunction]
	static int GetBots(ILuaInterface lua) {
		Assert(ThreadInMainThread());
		lua.PreCreateTable(gpGlobals.MaxClients, 0);
		int count = 0;
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player == null || player.IsMarkedForDeletion() || !(player.IsBot() || player.IsHLTV()))
				continue;
#if GAME_DLL
			if (player.Connected == PlayerConnectedState.Disconnected)
				continue;
#endif
			g_Lua!.PushNumber(++count);
			LuaEntity.Push_Entity(player);
			g_Lua.SetTable(-3);
		}
		return 1;
	}

	[LuaFunction]
	static int GetHumans(ILuaInterface lua) {
		Assert(ThreadInMainThread());
		lua.PreCreateTable(gpGlobals.MaxClients, 0);
		int count = 0;
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player == null || player.IsMarkedForDeletion() || player.IsBot() || player.IsHLTV())
				continue;
#if GAME_DLL
			if (player.Connected == PlayerConnectedState.Disconnected)
				continue;
#endif
			g_Lua!.PushNumber(++count);
			LuaEntity.Push_Entity(player);
			g_Lua.SetTable(-3);
		}
		return 1;
	}

#if GAME_DLL
	// todo
	// [LuaFunction]
	// static int CreateNextBot(ILuaInterface lua) {
	// 	if (g_PhysWorldObject == null) {
	// 		lua.ErrorFromLua("Trying to create nextbot player too early!\n");
	// 		return 0;
	// 	}
	// 	if (gpGlobals.MaxClients < 2) {
	// 		lua.ErrorFromLua("Cannot create a player bot in singleplayer!\n");
	// 		return 0;
	// 	}
	// 	NextBotPlayer<GMOD_Player>? bot = NextBotCreatePlayerBot<NextBotPlayer<GMOD_Player>>(g_Lua!.CheckString(1), true);
	// 	if (bot == null)
	// 		return 0;
	// 	LuaEntity.Push_Entity(bot);
	// 	return 1;
	// }
#endif
}
#endif
