using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.GarrysMod.Lua;

using System.Numerics;

namespace Game.Client.GarrysMod;

public static partial class LuaCSEnt
{
	[LuaClass]
	public static readonly LuaEntityClass LC_CSEnt = new("CSEnt", LuaType.Entity, null, "Entity");

	[LuaMethod]
	static int CSEnt____tostring(ILuaInterface lua) {
		C_BaseEntity? ent = LuaEntity.Get_Entity(1, true);
		if (ent == null) {
			lua.PushString("[NULL CSEntity]");
			return 1;
		}
		lua.PushString($"CSEntity [{ent.GetClassname()}]");
		return 1;
	}

	[LuaGlobal]
	static int ClientsideModel(ILuaInterface lua) {
		string name = lua.CheckString(1);
		if (name.Length < 2)
			return 0;

		RenderGroup renderGroup = RenderGroup.Other;
		if (lua.GetType(2) == LuaType.Number) {
			int group = (int)lua.GetNumber(2);
			if (group < 0 || group == 0)
				renderGroup = RenderGroup.OpaqueEntityHuge;
			else if (group > (int)RenderGroup.Other)
				renderGroup = RenderGroup.Other;
			else if (group == (int)RenderGroup.OpaqueStatic)
				renderGroup = RenderGroup.OpaqueEntity;
			else
				renderGroup = (RenderGroup)group;
		}

		C_BaseFlex ent = new();
		int modelIndex = LuaUtil.UTIL_GetModelIndex(name);
		if (modelIndex == -1 && (name[0] != '*' || (modelIndex = modelinfo.GetModelIndex(name)) == -1))
			ent.SetModelPointer(modelinfo.FindOrLoadModel(name));

		if (!ent.InitializeAsClientEntity(modelIndex == -1 ? null : name, renderGroup)) {
			Warning($"Failed to initialize ClientsideModel '{name}'!\n");
			ent.Release();
			return 0;
		}

		ent.Dormant = false;
		ent.InvalidateBoneCache();
		ent.FrameAdvance(1.0f);
		ent.StudioFrameAdvance();
		Model? model = ent.GetModel();
		if (model != null) {
			modelinfo.GetModelRenderBounds(model, out Vector3 mins, out Vector3 maxs);
			ent.SetCollisionBounds(mins, maxs);
		}
		ent.SetCollisionGroup(CollisionGroup.Debris);
		ent.Spawn();

		LuaObject? luaEnt = ent.GetLuaEntity();
		if (luaEnt == null) {
			ent.Release();
			return 0;
		}
		luaEnt.Push();
		LC_CSEnt.MetaTable.Push();
		lua.SetMetaTable(-2);
		return 1;
	}
}
