#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaMotionSensor
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_motionsensor = new("motionsensor");

#if CLIENT_DLL || MENU_DLL
	[LuaFunction]
	static int IsAvailable(ILuaInterface lua) {
		if (commandLine.FindParm("-kinectdebug") != 0) {
			Msg("motionsesnor - isavailable\n");
			Msg($"motionsesnor - G={get}\n");
			Msg($"motionsesnor - MS={get.MotionSensor()}\n");
		}

		IMotionSensor? sensor = get.MotionSensor();
		if (sensor == null) {
			if (commandLine.FindParm("-kinectdebug") != 0)
				Msg("motionsesnor - returning false\n");
			g_Lua!.PushBool(false);
			return 1;
		}

		if (commandLine.FindParm("-kinectdebug") != 0)
			Msg($"num sensors: - {sensor.NumSensors()}\n");

		g_Lua!.PushBool(sensor.NumSensors() > 0);
		return 1;
	}

	[LuaFunction]
	static int Start(ILuaInterface lua) {
		get.MotionSensor()?.Start();
		return 0;
	}

	[LuaFunction]
	static int GetColourMaterial(ILuaInterface lua) {
		IMotionSensor? sensor = get.MotionSensor();
		if (sensor == null || sensor.NumSensors() == 0 || !sensor.IsActive())
			return 0;

		IMaterial? material = sensor.GetColourMaterial();
		if (material == null)
			return 0;

		LuaMaterial.Push(material);
		return 1;
	}
#endif
}
#endif
