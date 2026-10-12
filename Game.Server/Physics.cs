global using static Game.Server.PhysicsHookGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Mathematics;
using Source.Common.Physics;
using Source.Common.SoundEmitterSystem;
using Source.Engine;

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Game.Server;

[EngineComponent]
public static class PhysicsHookGlobals {
	public static readonly PhysicsHook g_PhysicsHook = new();
	public static readonly CollisionEvent g_Collisions = new();
	public static EntityList? g_ShadowEntities = null;
	public static TimeUnit_t g_PhysAverageSimTime;
	public static readonly ConcurrentQueue<Action> g_PostSimulationQueue = new();

	public const float VPHYSICS_LARGE_OBJECT_MASS = 500.0f;

	static PhysicsHookGlobals(){
		SetPhysicsGameSystem(g_PhysicsHook);
	}

	public static float PhysGetEntityMass(BaseEntity entity) {
		IPhysicsObject[] list = System.Buffers.ArrayPool<IPhysicsObject>.Shared.Rent(VPHYSICS_MAX_OBJECT_LIST_COUNT);
		int physCount = entity.VPhysicsGetObjectList(list.AsSpan(0, VPHYSICS_MAX_OBJECT_LIST_COUNT));
		float otherMass = 0;
		for (int i = 0; i < physCount; i++)
			otherMass += list[i].GetMass();

		System.Buffers.ArrayPool<IPhysicsObject>.Shared.Return(list);
		return otherMass;
	}

	class SkipKeys : IVPhysicsKeyHandler
	{
		public void ParseKeyValue<T>(ref T data, ReadOnlySpan<char> key, ReadOnlySpan<char> value) { }
		public void SetDefaults<T>(ref T data) { }
	}

	public static void PhysSolidOverride(ref Solid solid, string? overrideScript) {
		if (overrideScript != null) {
			bool collisions = solid.Params.EnableCollisions;

			StringBuilder tmpString = new("solid { ");

			nexttoken(out ReadOnlySpan<char> token, overrideScript, ',', out ReadOnlySpan<char> str);
			while (!token.IsEmpty) {
				tmpString.Append('"');
				tmpString.Append(token);
				tmpString.Append("\" ");
				nexttoken(out token, str, ',', out str);
			}
			tmpString.Append('}');

			IVPhysicsKeyParser parse = physcollision.VPhysicsKeyParserCreate(Encoding.ASCII.GetBytes(tmpString.ToString()));
			parse.ParseSolid(ref solid, new SkipKeys());
			physcollision.VPhysicsKeyParserDestroy(parse);

			solid.Params.EnableCollisions = collisions;
		}
	}

	public static void PhysSetMassCenterOverride(in MassCenterOverride massOverride) {
		if (massOverride.EntityName != null)
			g_PhysicsHook.MassCenterOverrides.Add(massOverride);
	}

	public static int PhysGetMassCenterOverrideIndex(string? name) {
		if (name != null && g_PhysicsHook.MassCenterOverrides.Count != 0) {
			for (int i = 0; i < g_PhysicsHook.MassCenterOverrides.Count; i++) {
				if (g_PhysicsHook.MassCenterOverrides[i].EntityName == name)
					return i;
			}
		}
		return -1;
	}

	public static void PhysGetMassCenterOverride(BaseEntity entity, VCollide? collide, ref Solid solidOut) {
		int index = PhysGetMassCenterOverrideIndex(entity.GetEntityName());

		if (index >= 0) {
			MassCenterOverride massOverride = g_PhysicsHook.MassCenterOverrides[index];
			Vector3 massCenterWS = massOverride.Center;
			switch (massOverride.AlignType) {
				case MassCenterOverride.Align.Point:
					MathLib.VectorITransform(massCenterWS, entity.EntityToWorldTransform(), out solidOut.MassCenterOverride);
					break;
				case MassCenterOverride.Align.Axis: {
						physcollision.CollideGetMassCenter(collide!.Solids![solidOut.Index], out Vector3 massCenterLocal);
						MathLib.VectorTransform(massCenterLocal, entity.EntityToWorldTransform(), out Vector3 defaultMassCenterWS);
						massCenterWS += massOverride.Axis *
							(Vector3.Dot(defaultMassCenterWS, massOverride.Axis) - Vector3.Dot(massOverride.Axis, massOverride.Center));
						MathLib.VectorITransform(massCenterWS, entity.EntityToWorldTransform(), out solidOut.MassCenterOverride);
					}
					break;
			}
			g_PhysicsHook.MassCenterOverrides[index] = g_PhysicsHook.MassCenterOverrides[^1];
			g_PhysicsHook.MassCenterOverrides.RemoveAt(g_PhysicsHook.MassCenterOverrides.Count - 1);

			if (solidOut.MassCenterOverride.Length() > DIST_EPSILON) {
				Vector3[] massCenter = [solidOut.MassCenterOverride];
				solidOut.Params.MassCenterOverrideFn = () => ref massCenter[0];
			}
		}
	}

	public static void PhysicsImpactSound(BaseEntity entity, IPhysicsObject physObject, SoundEntityChannel channel, int surfaceProps, int surfacePropsHit, float volume, float impactSpeed) {
		PhysicsSound.AddImpactSound(g_PhysicsHook.ImpactSounds, entity, entity.EntIndex(), channel, physObject, surfaceProps, surfacePropsHit, volume, impactSpeed);
	}

	public static void PhysCollisionSound(BaseEntity entity, IPhysicsObject physObject, SoundEntityChannel channel, int surfaceProps, int surfacePropsHit, float deltaTime, float speed) {
		if (deltaTime < 0.05f || speed < 70.0f)
			return;

		float volume = speed * speed * (1.0f / (320.0f * 320.0f));
		if (volume > 1.0f)
			volume = 1.0f;

		PhysicsImpactSound(entity, physObject, channel, surfaceProps, surfacePropsHit, volume, speed);
	}

	public static void PhysBreakSound(BaseEntity entity, IPhysicsObject? physObject, Vector3 origin) {
		if (physObject == null)
			return;

		PhysicsSound.AddBreakSound(g_PhysicsHook.BreakSounds, origin, (ushort)physObject.GetMaterialIndex());
	}

	public static void PhysForceEntityToSleep(BaseEntity entity, IPhysicsObject? obj) {
		if (obj == null || !obj.IsMoveable())
			return;

		DevMsg(2, $"Putting entity to sleep: {entity.GetClassname()}\n");
		IPhysicsObject[] list = System.Buffers.ArrayPool<IPhysicsObject>.Shared.Rent(VPHYSICS_MAX_OBJECT_LIST_COUNT);
		int physCount = entity.VPhysicsGetObjectList(list.AsSpan(0, VPHYSICS_MAX_OBJECT_LIST_COUNT));
		for (int i = 0; i < physCount; i++) {
			PhysForceClearVelocity(list[i]);
			list[i].Sleep();
		}
		System.Buffers.ArrayPool<IPhysicsObject>.Shared.Return(list);
	}

	static readonly ConVar collision_shake_amp = new("collision_shake_amp", "0.2");
	static readonly ConVar collision_shake_freq = new("collision_shake_freq", "0.5");
	static readonly ConVar collision_shake_time = new("collision_shake_time", "0.5");

	public static void PhysCollisionScreenShake(ref GameVCollisionEvent ev, int index) {
		int otherIndex = index == 0 ? 1 : 0;
		float mass = ev.VCollisionEvent.Objects[index]!.GetMass();
		if (mass >= VPHYSICS_LARGE_OBJECT_MASS && ev.VCollisionEvent.Objects[otherIndex]!.IsStatic() &&
			(ev.VCollisionEvent.Objects[index]!.GetGameFlags() & PhysicsFlags.Penetrating) == 0) {
			mass = Math.Clamp(mass, VPHYSICS_LARGE_OBJECT_MASS, 2000.0f);
			if (ev.VCollisionEvent.CollisionSpeed > 30 && ev.VCollisionEvent.DeltaCollisionTime > 0.25f) {
				ev.VCollisionEvent.InternalData!.GetContactPoint(out Vector3 pos);
				float impulse = ev.VCollisionEvent.CollisionSpeed * mass;
				float amplitude = impulse * (collision_shake_amp.GetFloat() / (30.0f * VPHYSICS_LARGE_OBJECT_MASS));
				Util.ScreenShake(pos, amplitude, collision_shake_freq.GetFloat(), collision_shake_time.GetFloat(), amplitude * 60, ShakeCommand.Start);
			}
		}
	}

	public static void PhysCollisionDust(ref GameVCollisionEvent ev, SurfaceData_ptr hit) {
		switch ((CharTex)hit.Game.Material) {
			case CharTex.Sand:
			case CharTex.Dirt:
				if (ev.VCollisionEvent.CollisionSpeed < 200.0f)
					return;

				break;

			case CharTex.Concrete:
				if (ev.VCollisionEvent.CollisionSpeed < 340.0f)
					return;

				break;

			default:
				return;
		}

		ev.VCollisionEvent.InternalData!.GetContactPoint(out Vector3 pos);

		Vector3 vel = default;
		vel.Random(-1.0f, 1.0f);
		vel.Z = random.RandomFloat(0.3f, 1.0f);
		MathLib.VectorNormalize(ref vel);
		g_pEffects.Dust(pos, vel, 8.0f, ev.VCollisionEvent.CollisionSpeed);
	}

	public static void PhysFrictionSound(BaseEntity? entity, IPhysicsObject obj, ReadOnlySpan<char> soundName, ref HSOUNDSCRIPTHANDLE handle, float volume) {
		if (entity == null)
			return;

		volume = Math.Clamp(volume, 0.0f, 1.0f);
		if (volume > (1.0f / 128.0f)) {
			ref Friction friction = ref g_Collisions.FindFriction(entity);
			if (Unsafe.IsNullRef(ref friction))
				return;

			SoundParameters parms = new();
			if (!BaseEntity.GetParametersForSound(soundName, ref handle, ref parms, null))
				return;

			if (friction.Object == null) {
				if (parms.Volume * volume <= 0.1f)
					return;

				friction.Object = entity;
				PASAttenuationFilter filter = new(entity, parms.SoundLevel);
				friction.Patch = SoundEnvelopeController.GetController().SoundCreate(filter, entity.EntIndex(), (int)SoundEntityChannel.Body, soundName, parms.SoundLevel);
				SoundEnvelopeController.GetController().Play(friction.Patch, parms.Volume * volume, parms.Pitch);
			}
			else {
				float pitch = (volume * (parms.PitchHigh - parms.PitchLow)) + parms.PitchLow;
				SoundEnvelopeController.GetController().SoundChangeVolume(friction.Patch!, parms.Volume * volume, 0.1);
				SoundEnvelopeController.GetController().SoundChangePitch(friction.Patch!, pitch, 0.1);
			}

			friction.LastUpdateTime = gpGlobals.CurTime;
			friction.LastEffectTime = gpGlobals.CurTime;
		}
	}

	public static void PhysCleanupFrictionSounds(BaseEntity entity) {
		ref Friction friction = ref g_Collisions.FindFriction(entity);
		if (!Unsafe.IsNullRef(ref friction) && friction.Patch != null)
			g_Collisions.ShutdownFriction(ref friction);
	}

	public static bool PhysIsInCallback(){
		return (physenv != null && physenv.IsInSimulation()) || g_Collisions.IsInCallback();
	}

	public static void PhysAddShadow(BaseEntity entity) => g_ShadowEntities!.AddEntity(entity);
	public static void PhysRemoveShadow(BaseEntity entity) => g_ShadowEntities!.DeleteEntity(entity);

	public static void PhysOnCleanupDeleteList() {
		g_Collisions.FlushQueuedOperations();
		physenv?.CleanupDeleteList();
	}

	public static void PhysCallbackRemove(IServerNetworkable remove){
		if (PhysIsInCallback()) 
			g_Collisions.AddRemoveObject(remove);
		else 
			Util.Remove(remove);
	}

	public static void PhysCallbackDamage(BaseEntity entity, in TakeDamageInfo info){
		if (PhysIsInCallback()) {
			BaseEntity inflictor = info.GetInflictor();
			IPhysicsObject? inflictorPhysics = inflictor?.VPhysicsGetObject();
			g_Collisions.AddDamageEvent(entity, info, inflictorPhysics, false, vec3_origin, vec3_origin);
			if (entity != null && info.GetInflictor() != null) 
				DevMsg(2, $"Warning: Physics damage event with no recovery info!\nObjects: {entity.GetClassname()}, {info.GetInflictor()!.GetClassname()}\n");
		}
		else 
			entity.TakeDamage(info);
	}
}

public struct MassCenterOverride
{
	public enum Align
	{
		Point = 0,
		Axis = 1,
	}

	public void Defaults() {
		EntityName = null;
	}

	public void SnapToPoint(string? name, in Vector3 pointWS) {
		EntityName = name;
		Center = pointWS;
		Axis = default;
		AlignType = Align.Point;
	}

	public void SnapToAxis(string? name, in Vector3 axisStartWS, in Vector3 unitAxisDirWS) {
		EntityName = name;
		Center = axisStartWS;
		Axis = unitAxisDirWS;
		AlignType = Align.Axis;
	}

	public Vector3 Center;
	public Vector3 Axis;
	public Align AlignType;
	public string? EntityName;
}

public class PhysicsHook : BaseGameSystemPerFrame
{
	public static TimeUnit_t g_PhysAverageSimTime;

	public override ReadOnlySpan<char> Name() => "PhysicsHook";

	public bool Paused;

	public override bool Init() {
		PhysParseSurfaceData(physprops, filesystem);

		return base.Init();
	}
	public override void LevelInitPreEntity() {
		physenv = physics.CreateEnvironment();
		PhysicsPerformanceParams parms = default;
		parms.Defaults();
		parms.MaxCollisionsPerObjectPerTimestep = 10;
		physenv.SetPerformanceSettings(in parms);

#if PORTAL
		physenv_main = physenv;
#endif
		g_EntityCollisionHash = physics.CreateObjectPairHash();
		physenv.SetDebugOverlay(Singleton<IEngineAPI>());
		physenv.EnableDeleteQueue(true);

		physenv.SetCollisionSolver(g_Collisions);
		physenv.SetCollisionEventHandler(g_Collisions);
		physenv.SetConstraintEventHandler(g_pConstraintEvents);
		physenv.EnableConstraintNotify(true); // callback when an object gets deleted that is attached to a constraint

		physenv.SetObjectEventHandler(g_Collisions);

		physenv.SetSimulationTimestep(gpGlobals.IntervalPerTick); 
																	
		physenv.SetGravity(new Vector3(0, 0, -GetCurrentGravity()));
		g_PhysAverageSimTime = 0;

		// todo
		g_PhysWorldObject = PhysCreateWorld(GetWorldEntity());

		g_ShadowEntities = new EntityList();
		PrecachePhysicsSounds();

		Paused = true;
	}
	public static IPhysicsObject? FindPhysicsObjectByName(ReadOnlySpan<char> name, BaseEntity? errorEntity) {
		if (name.IsEmpty)
			return null;

		BaseEntity? entity = null;
		IPhysicsObject? bestObject = null;
		while (true) {
			entity = gEntList.FindEntityByName(entity, name);
			if (entity == null)
				break;
			if (entity.VPhysicsGetObject() != null) {
				if (bestObject != null) {
					ReadOnlySpan<char> errorName = errorEntity != null ? errorEntity.GetClassname() : "Unknown";
					Vector3 origin = errorEntity != null ? errorEntity.GetAbsOrigin() : vec3_origin;
					DevWarning($"entity {errorName} at {origin.X:F2} {origin.Y:F2} {origin.Z:F2} has physics attachment to more than one entity with the name {name}!!!");
					while ((entity = gEntList.FindEntityByName(entity, name)) != null)
						DevWarning($"Found {entity.GetClassname()}");
					break;
				}
				bestObject = entity.VPhysicsGetObject();
			}
		}
		return bestObject;
	}

	public static IPhysicsObject? PhysCreateWorld(BaseEntity world){
		// todo staticpropmgr
		VCollide? worldCollide = modelinfo.GetVCollide(1);
		return PhysCreateWorld_Shared(world, worldCollide, g_PhysDefaultObjectParams);
	}
	public static HSOUNDSCRIPTHANDLE PrecachePhysicsSoundByStringIndex(UtlSymId_t idx) => idx != 0 ? BaseEntity.PrecacheScriptSound(physprops.GetString(idx)) : SOUNDEMITTER_INVALID_HANDLE;
	public void PrecachePhysicsSounds(){
		// precache the surface prop sounds
		for (nint i = 0; i < physprops.SurfacePropCount(); i++) {
			var prop = physprops.GetSurfaceData(i);
			Assert(prop != null);

			prop.SoundHandles.StepLeft = PrecachePhysicsSoundByStringIndex(prop.Sounds.StepLeft);
			prop.SoundHandles.StepRight = PrecachePhysicsSoundByStringIndex(prop.Sounds.StepRight);
			prop.SoundHandles.ImpactSoft = PrecachePhysicsSoundByStringIndex(prop.Sounds.ImpactSoft);
			prop.SoundHandles.ImpactHard = PrecachePhysicsSoundByStringIndex(prop.Sounds.ImpactHard);
			prop.SoundHandles.ScrapeSmooth = PrecachePhysicsSoundByStringIndex(prop.Sounds.ScrapeSmooth);
			prop.SoundHandles.ScrapeRough = PrecachePhysicsSoundByStringIndex(prop.Sounds.ScrapeRough);
			prop.SoundHandles.BulletImpact = PrecachePhysicsSoundByStringIndex(prop.Sounds.BulletImpact);
			prop.SoundHandles.Rolling = PrecachePhysicsSoundByStringIndex(prop.Sounds.Rolling);
			prop.SoundHandles.BreakSound = PrecachePhysicsSoundByStringIndex(prop.Sounds.BreakSound);
			prop.SoundHandles.StrainSound = PrecachePhysicsSoundByStringIndex(prop.Sounds.StrainSound);
		}
	}
	public override void LevelInitPostEntity() {
		base.LevelInitPostEntity();
		Paused = false;
	}

	public bool ShouldSimulate() => physenv != null && !Paused;

	void PhysFrame(TimeUnit_t deltaTime) {
		if (!ShouldSimulate())
			return;

		// Trap interrupts and clock changes
		if (deltaTime > 1.0 || deltaTime < 0.0)
			deltaTime = 0;
		else if (deltaTime > 0.1)
			deltaTime = 0.1; // limit incoming time to 100ms

		physenv!.DebugCheckContacts();
		g_Collisions.BufferTouchEvents(true);
		physenv.Simulate(deltaTime);

		int activeCount = physenv.GetActiveObjectCount();
		if (activeCount != 0) {
			IPhysicsObject?[] activeList = ArrayPool<IPhysicsObject>.Shared.Rent(activeCount);
			physenv.GetActiveObjects(activeList);

			for (int i = 0; i < activeCount; i++) {
				BaseEntity? entity = (BaseEntity?)activeList[i]?.GetGameData();
				if (entity != null)
					entity.VPhysicsUpdate(activeList[i]!);
			}

			ArrayPool<IPhysicsObject>.Shared.Return(activeList, true);
		}

		g_Collisions.BufferTouchEvents(false);
		g_Collisions.FrameUpdate();
	}
	public override void LevelShutdownPreEntity() {
		base.LevelShutdownPreEntity();
	}
	public override void LevelShutdownPostEntity() {
		base.LevelShutdownPostEntity();
		MassCenterOverrides.Clear();
	}
	public override void FrameUpdatePostEntityThink() {
		base.FrameUpdatePostEntityThink();
		PhysFrame(gpGlobals.FrameTime);
	}
	public override void PreClientUpdate() {
		ImpactSoundTime += gpGlobals.FrameTime;
		if (ImpactSoundTime > 0.05) {
			PhysicsSound.PlayImpactSounds(ImpactSounds);
			ImpactSoundTime = 0.0;
			PhysicsSound.PlayBreakSounds(BreakSounds);
		}
	}

	TimeUnit_t ImpactSoundTime;
	public readonly List<ImpactSound> ImpactSounds = [];
	public readonly List<BreakSound> BreakSounds = [];
	public readonly List<MassCenterOverride> MassCenterOverrides = [];
}
