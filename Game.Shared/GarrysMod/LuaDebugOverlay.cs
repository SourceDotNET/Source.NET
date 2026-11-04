#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaDebugOverlay
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_debugoverlay = new("debugoverlay");

	[LuaFunction]
	static int Box(ILuaInterface lua) {
		if (developer.GetInt() == 0)
			return 0;
		double duration = lua.CheckNumberOpt(4, 1.0);
		Color color = lua.GetColor(5);
		Game.Shared.DebugOverlay.Box(LuaVector.Get_Vector(1), LuaVector.Get_Vector(2), LuaVector.Get_Vector(3), color.R, color.G, color.B, color.A, (float)duration);
		return 0;
	}

	[LuaFunction]
	static int Line(ILuaInterface lua) {
		if (developer.GetInt() == 0)
			return 0;
		double duration = lua.CheckNumberOpt(3, 1.0);
		Color color = lua.GetColor(4);
		bool ignoreZ = lua.GetType(5) != LuaType.Nil && lua.GetBool(5);
		Game.Shared.DebugOverlay.Line(LuaVector.Get_Vector(1), LuaVector.Get_Vector(2), color.R, color.G, color.B, ignoreZ, (float)duration);
		return 0;
	}

	[LuaFunction]
	static int Axis(ILuaInterface lua) {
		if (developer.GetInt() == 0)
			return 0;
		double duration = lua.CheckNumberOpt(4, 1.0);
		bool ignoreZ = lua.GetType(5) != LuaType.Nil && lua.GetBool(5);
		Game.Shared.DebugOverlay.Axis(LuaVector.Get_Vector(1), LuaAngle.Get_Angle(2), (float)lua.CheckNumber(3), ignoreZ, (float)duration);
		return 0;
	}

	[LuaFunction]
	static int Grid(ILuaInterface lua) {
		if (developer.GetInt() == 0)
			return 0;
		Game.Shared.DebugOverlay.Grid(LuaVector.Get_Vector(1));
		return 0;
	}

	[LuaFunction]
	static int Text(ILuaInterface lua) {
		if (developer.GetInt() == 0)
			return 0;
		double duration = lua.CheckNumberOpt(3, 1.0);
		bool viewCheck = lua.GetType(4) != LuaType.Nil && lua.GetBool(4);
		Game.Shared.DebugOverlay.Text(LuaVector.Get_Vector(1), lua.CheckString(2), viewCheck, (float)duration);
		return 0;
	}

	[LuaFunction]
	static int Sphere(ILuaInterface lua) {
		if (developer.GetInt() == 0)
			return 0;
		double duration = lua.CheckNumberOpt(3, 1.0);
		Color color = lua.GetColor(4);
		bool ignoreZ = lua.GetType(5) != LuaType.Nil && lua.GetBool(5);
		Game.Shared.DebugOverlay.Sphere(LuaVector.Get_Vector(1), vec3_angle, (float)lua.CheckNumber(2), color.R, color.G, color.B, color.A, ignoreZ, (float)duration);
		return 0;
	}

	[LuaFunction]
	static int Cross(ILuaInterface lua) {
		if (developer.GetInt() == 0)
			return 0;
		double duration = lua.CheckNumberOpt(3, 1.0);
		Color color = lua.GetColor(4);
		bool ignoreZ = lua.GetType(5) != LuaType.Nil && lua.GetBool(5);
		Game.Shared.DebugOverlay.Cross3D(LuaVector.Get_Vector(1), (float)lua.CheckNumber(2), color.R, color.G, color.B, ignoreZ, (float)duration);
		return 0;
	}

	[LuaFunction]
	static int Triangle(ILuaInterface lua) {
		if (developer.GetInt() == 0)
			return 0;
		double duration = lua.CheckNumberOpt(4, 1.0);
		Color color = lua.GetColor(5);
		bool ignoreZ = lua.GetType(6) != LuaType.Nil && lua.GetBool(6);
		Game.Shared.DebugOverlay.Triangle(LuaVector.Get_Vector(1), LuaVector.Get_Vector(2), LuaVector.Get_Vector(3), color.R, color.G, color.B, color.A, ignoreZ, (float)duration);
		return 0;
	}

	[LuaFunction]
	static int BoxAngles(ILuaInterface lua) {
		if (developer.GetInt() == 0)
			return 0;
		double duration = lua.CheckNumberOpt(5, 1.0);
		Color color = lua.GetColor(6);
		Game.Shared.DebugOverlay.BoxAngles(LuaVector.Get_Vector(1), LuaVector.Get_Vector(2), LuaVector.Get_Vector(3), LuaAngle.Get_Angle(4), color.R, color.G, color.B, color.A, (float)duration);
		return 0;
	}

	[LuaFunction]
	static int SweptBox(ILuaInterface lua) {
		if (developer.GetInt() == 0)
			return 0;
		double duration = lua.CheckNumberOpt(6, 1.0);
		Color color = lua.GetColor(7);
		Game.Shared.DebugOverlay.SweptBox(LuaVector.Get_Vector(1), LuaVector.Get_Vector(2), LuaVector.Get_Vector(3), LuaVector.Get_Vector(4), LuaAngle.Get_Angle(5), color.R, color.G, color.B, color.A, (float)duration);
		return 0;
	}

	[LuaFunction]
	static int EntityTextAtPosition(ILuaInterface lua) {
		if (developer.GetInt() == 0)
			return 0;
		double duration = lua.CheckNumberOpt(4, 1.0);
		Color color = lua.GetColor(5);
		Game.Shared.DebugOverlay.EntityTextAtPosition(LuaVector.Get_Vector(1), (int)lua.CheckNumber(2), lua.CheckString(3), (float)duration, color.R, color.G, color.B, color.A);
		return 0;
	}

	[LuaFunction]
	static int ScreenText(ILuaInterface lua) {
		if (developer.GetInt() == 0)
			return 0;
		double duration = lua.CheckNumberOpt(4, 1.0);
		Color color = lua.GetColor(5);
		Game.Shared.DebugOverlay.ScreenText((float)lua.CheckNumber(1), (float)lua.CheckNumber(2), lua.CheckString(3), color.R, color.G, color.B, color.A, (float)duration);
		return 0;
	}
}
#endif
