global using static Game.Client.PhysicsSystemHook;

using Game.Shared;

using Source;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Physics;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

using static Source.Engine.StaticPropMgrGlobals;

namespace Game.Client;

[EngineComponent]
public static class PhysicsSystemHook
{
	public static readonly PhysicsSystem g_PhysicsSystem = new();
	static PhysicsSystemHook() {
		SetPhysicsGameSystem(g_PhysicsSystem);
	}

	public static bool PhysIsInCallback() {
		if (physenv != null && physenv.IsInSimulation())
			return true;
		// return g_Collisions.IsInCallback(); TODO
		return false;
	}
}

public class PhysicsSystem : AutoGameSystemPerFrame
{
	public override bool Init() => true;

	public override void LevelInitPostEntity() {
		PhysicsLevelInit();
	}

	static readonly ConVar cl_phys_timescale = new("cl_phys_timescale", "1.0", FCvar.Cheat, "Sets the scale of time for client-side physics (ragdolls)");

#if GMOD_DLL
	public static float g_GMOD_PhysTimeScale = 1.0f;
	public static bool g_GMOD_PhysicsPaused;
	public static TimeUnit_t g_GMOD_LastSimulationTime;

	public static void GMOD_PhysicsSetTimeScale(float scale) {
		if (scale <= 0.001f)
			scale = 0.001f;
		if (5.0f <= scale)
			scale = 5.0f;
		g_GMOD_PhysTimeScale = scale;
	}

	public static float GMOD_PhysicsGetTotalTimeScale() => cl_phys_timescale.GetFloat() * g_GMOD_PhysTimeScale;
#endif

	internal void PhysicsSimulate() {
		TimeUnit_t frametime = gpGlobals.FrameTime;
		if (physenv != null) {
#if GMOD_DLL
			TimeUnit_t startTime = engine.Time();
#endif
			physenv.DebugCheckContacts();
#if GMOD_DLL
			physenv.Simulate(GMOD_PhysicsGetTotalTimeScale() * frametime);
#else
			physenv.Simulate(frametime * cl_phys_timescale.GetDouble());
#endif

			int activeCount = physenv.GetActiveObjectCount();
			IPhysicsObject?[]? activeList = null;
			if (activeCount != 0) {
				activeList = ArrayPool<IPhysicsObject>.Shared.Rent(activeCount);
				physenv.GetActiveObjects(activeList);

				for (int i = 0; i < activeCount; i++) {
					C_BaseEntity? entity = (C_BaseEntity?)(activeList[i]?.GetGameData());
					if (entity != null) {
						//  TODO:   if (entity.CollisionProp().DoesVPhysicsInvalidateSurroundingBox()) {
						//  TODO:   	entity.CollisionProp().MarkSurroundingBoundsDirty();
						//  TODO:   }
						entity.PhysicsUpdate(activeList[i]);
					}
				}

				ArrayPool<IPhysicsObject>.Shared.Return(activeList, true);
			}
#if GMOD_DLL
			g_GMOD_LastSimulationTime = engine.Time() - startTime;
#endif

			// g_Collisions.BufferTouchEvents(false);
			// g_Collisions.FrameUpdate();
		}
		// play impact sounds
	}

	private void PhysicsLevelInit() {
		physenv = physics.CreateEnvironment();
		Assert(physenv != null);

		g_EntityCollisionHash = physics.CreateObjectPairHash();

		// TODO: need to get the right factory function here
		//physenv->SetDebugOverlay( appSystemFactory );
		physenv.SetGravity(new(0, 0, -GetCurrentGravity()));
		// 15 ms per tick
		// NOTE: Always run client physics at this rate - helps keep ragdolls stable
		physenv.SetSimulationTimestep(gpGlobals.IntervalPerTick);
		// TODO: physenv.SetCollisionEventHandler(g_Collisions);
		// TODO: physenv.SetCollisionSolver(g_Collisions);

		g_PhysWorldObject = PhysCreateWorld_Shared(C_World.GetClientWorldEntity(), modelinfo.GetVCollide(1), g_PhysDefaultObjectParams)!;

		g_StaticPropMgr.CreateVPhysicsRepresentations(physenv, g_SolidSetup, null);
	}
}
