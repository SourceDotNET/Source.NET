#if CLIENT_DLL || GAME_DLL
using Source.Common.GarrysMod.Lua;
using Source.Common.Physics;

using System.Numerics;

#if CLIENT_DLL
using static Game.Client.PhysicsSystem;

namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaPhysEnv
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_physenv = new("physenv");

	[LuaFunction]
	static int GetAirDensity(ILuaInterface lua) {
		if (physenv == null)
			return 0;
		lua.PushNumber(physenv.GetAirDensity());
		return 1;
	}

	[LuaFunction]
	static int SetAirDensity(ILuaInterface lua) {
		if (physenv != null)
			physenv.SetAirDensity((float)lua.CheckNumber(1));
		return 0;
	}

	[LuaFunction]
	static int GetGravity(ILuaInterface lua) {
		if (physenv == null)
			return 0;
		physenv.GetGravity(out Vector3 gravity);
		LuaVector.Push_Vector(gravity);
		return 1;
	}

	[LuaFunction]
	static int SetGravity(ILuaInterface lua) {
		if (physenv != null)
			physenv.SetGravity(LuaVector.Get_Vector(1));
		return 0;
	}
	// todo: AddSurfaceData
	[LuaFunction]
	static int GetPerformanceSettings(ILuaInterface lua) {
		if (physenv == null)
			return 0;
		physenv.GetPerformanceSettings(out PhysicsPerformanceParams settings);
		LuaTable table = new(null, 0);
		table.SetMember("MaxCollisionsPerObjectPerTimestep", settings.MaxCollisionsPerObjectPerTimestep);
		table.SetMember("MaxCollisionChecksPerTimestep", settings.MaxCollisionChecksPerTimestep);
		table.SetMember("MaxVelocity", settings.MaxVelocity);
		table.SetMember("MaxAngularVelocity", settings.MaxAngularVelocity);
		table.SetMember("LookAheadTimeObjectsVsWorld", settings.LookAheadTimeObjectsVsWorld);
		table.SetMember("LookAheadTimeObjectsVsObject", settings.LookAheadTimeObjectsVsObject);
		table.SetMember("MinFrictionMass", settings.MinFrictionMass);
		table.SetMember("MaxFrictionMass", settings.MaxFrictionMass);
		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaFunction]
	static int SetPerformanceSettings(ILuaInterface lua) {
		if (physenv == null)
			return 0;
		physenv.GetPerformanceSettings(out PhysicsPerformanceParams settings);
		LuaTable table = new(null, 0);
		table.SetFromStack(1);
		if (!table.isTable()) {
			lua.TypeError("table", 1);
			table.UnReference();
			return 0;
		}
		settings.MaxCollisionsPerObjectPerTimestep = table.GetMemberInt("MaxCollisionsPerObjectPerTimestep", settings.MaxCollisionsPerObjectPerTimestep);
		settings.MaxCollisionChecksPerTimestep = table.GetMemberInt("MaxCollisionChecksPerTimestep", settings.MaxCollisionChecksPerTimestep);
		settings.MaxVelocity = table.GetMemberFloat("MaxVelocity", settings.MaxVelocity);
		settings.MaxAngularVelocity = table.GetMemberFloat("MaxAngularVelocity", settings.MaxAngularVelocity);
		settings.LookAheadTimeObjectsVsWorld = table.GetMemberFloat("LookAheadTimeObjectsVsWorld", settings.LookAheadTimeObjectsVsWorld);
		settings.LookAheadTimeObjectsVsObject = table.GetMemberFloat("LookAheadTimeObjectsVsObject", settings.LookAheadTimeObjectsVsObject);
		settings.MinFrictionMass = table.GetMemberFloat("MinFrictionMass", settings.MinFrictionMass);
		settings.MaxFrictionMass = table.GetMemberFloat("MaxFrictionMass", settings.MaxFrictionMass);
		physenv.SetPerformanceSettings(settings);
		table.UnReference();
		return 0;
	}
	[LuaFunction]
	static int GetLastSimulationTime(ILuaInterface lua) {
		lua.PushNumber(g_GMOD_LastSimulationTime);
		return 1;
	}

	[LuaFunction]
	static int GetPhysicsPaused(ILuaInterface lua) {
		lua.PushBool(g_GMOD_PhysicsPaused);
		return 1;
	}

	[LuaFunction]
	static int SetPhysicsPaused(ILuaInterface lua) {
		g_GMOD_PhysicsPaused = lua.GetBool(1);
		return 0;
	}

	[LuaFunction]
	static int GetTimeScale(ILuaInterface lua) {
		lua.PushNumber(g_GMOD_PhysTimeScale);
		return 1;
	}

	[LuaFunction]
	static int SetTimeScale(ILuaInterface lua) {
		GMOD_PhysicsSetTimeScale((float)lua.CheckNumber(1));
		return 0;
	}
}
#endif
