using Game.Shared;

using Source.Common.GarrysMod.Lua;

namespace Game.Server.GarrysMod;

public static partial class LuaNPC
{
	[LuaClass]
	static readonly LuaEntityClass LC_NPC = LuaEntity.LC_NPC;

	public static AI_BaseNPC? Get_NPC(int stackPos, bool allowNull) {
		BaseEntity? ent = LuaEntity.Get_Entity(stackPos, allowNull);
		if (ent == null)
			return null;

		AI_BaseNPC? npc = ent.MyNPCPointer();
		if (npc == null && !allowNull)
			g_Lua!.Error("Tried to use a NULL NPC!");
		return npc;
	}

	[LuaMethod]
	static int NPC__SetCurrentWeaponProficiency(ILuaInterface lua) {
		AI_BaseNPC? npc = Get_NPC(1, false);
		if (npc == null)
			return 0;

		npc.SetCurrentWeaponProficiency((WeaponProficiency)Math.Clamp((int)lua.CheckNumber(2), (int)WeaponProficiency.Poor, (int)WeaponProficiency.Perfect));
		return 0;
	}

	[LuaMethod]
	static int NPC__GetCurrentWeaponProficiency(ILuaInterface lua) {
		AI_BaseNPC? npc = Get_NPC(1, false);
		if (npc == null)
			return 0;

		lua.PushNumber((int)npc.GetCurrentWeaponProficiency());
		return 1;
	}
}
