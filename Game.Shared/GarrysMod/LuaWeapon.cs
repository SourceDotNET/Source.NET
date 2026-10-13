#if CLIENT_DLL || GAME_DLL
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaWeapon
{
	[LuaClass]
	static readonly LuaEntityClass LC_Weapon = LuaEntity.LC_Weapon;

	[LuaMethod]
	static int Weapon__IsScripted(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		ReadOnlySpan<char> name = weapon.GetLuaScriptName();
		lua.PushBool(!name.IsEmpty && name[0] != '\0');
		return 1;
	}
}
#endif
