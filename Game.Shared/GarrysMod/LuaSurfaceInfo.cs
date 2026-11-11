#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaSurfaceInfo
{
	[LuaClass(typeof(int))]
	public static readonly LuaClass LC_SurfaceInfo = new("SurfaceInfo", LuaType.SurfaceInfo, null, null);

	public static void Push_SurfaceInfo(int surfID) {
		if (!LC_SurfaceInfo.MetaTable.isTable())
			Error("CLuaClass::Push - Not Table!");
		g_Lua!.PushValueUserType(surfID, LuaType.SurfaceInfo);
	}

	[LuaMethod]
	static int Entity__GetBrushSurfaces(ILuaInterface lua) {
		BaseEntity ent = LuaEntity.Get_Entity(1, false)!;
		if (ent.IsMarkedForDeletion())
			return 0;

		Model? model = ent.GetModel();
		if (model == null || modelinfo.GetModelType(model) != ModelType.Brush)
			return 0;

		int count = modelinfo.GetBrushModelSurfaceCount(model);
		lua.PreCreateTable(count, 0);
		int[] surfaces = new int[count];
		if (modelinfo.GetBrushModelSurfaces(model, surfaces)) {
			for (int i = 0; i < count; i++) {
				lua.PushNumber(i + 1);
				Push_SurfaceInfo(surfaces[i]);
				lua.SetTable(-3);
			}
		}
		return 1;
	}

	[LuaMethod]
	static int SurfaceInfo__GetMaterial(ILuaInterface lua) {
		int surfID = LC_SurfaceInfo.GetValue<int>(1);
		IMaterial? material = modelinfo.GetBrushSurfaceMaterial(surfID);
		if (material == null) {
			lua.PushNil();
			return 1;
		}
		LuaMaterial.LC_IMaterial.Push(material);
		return 1;
	}

	[LuaMethod]
	static int SurfaceInfo__GetVertices(ILuaInterface lua) {
		int surfID = LC_SurfaceInfo.GetValue<int>(1);
		BSPDertex[]? vertices = modelinfo.GetBrushSurfaceVertices(surfID, out int count);
		ReadOnlySpan<ushort> indices = modelinfo.GetBrushSurfaceVertIndices(surfID);
		lua.PreCreateTable(count, 0);
		if (!indices.IsEmpty && vertices != null) {
			for (int i = 0; i < count; i++) {
				lua.PushNumber(i + 1);
				LuaVector.Push_Vector(vertices[indices[i]].Position);
				lua.SetTable(-3);
			}
		}
		return 1;
	}

	[LuaMethod]
	static int SurfaceInfo__IsNoDraw(ILuaInterface lua) {
		lua.PushBool(modelinfo.IsBrushSurfaceNoDraw(LC_SurfaceInfo.GetValue<int>(1)));
		return 1;
	}

	[LuaMethod]
	static int SurfaceInfo__IsSky(ILuaInterface lua) {
		lua.PushBool(modelinfo.IsBrushSurfaceSky(LC_SurfaceInfo.GetValue<int>(1)));
		return 1;
	}

	[LuaMethod]
	static int SurfaceInfo__IsWater(ILuaInterface lua) {
		lua.PushBool(modelinfo.IsBrushSurfaceWater(LC_SurfaceInfo.GetValue<int>(1)));
		return 1;
	}

	[LuaMethod]
	static int SurfaceInfo____tostring(ILuaInterface lua) {
		lua.PushString($"SurfaceInfo [{LC_SurfaceInfo.GetValue<int>(1)}]");
		return 1;
	}
}
#endif
