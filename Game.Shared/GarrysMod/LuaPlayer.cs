#if CLIENT_DLL || GAME_DLL
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;

using System.Numerics;

#if CLIENT_DLL
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
	static int Player__GetShootPos(ILuaInterface lua) {
		BasePlayer player = Get_Player(1, false)!;
		LuaVector.Push_Vector(player.Weapon_ShootPosition());
		return 1;
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
