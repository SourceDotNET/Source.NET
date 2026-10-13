
global using static Game.Shared.PhysicsSharedGlobals;

using Source;
using Source.Common;
using Source.Common.Filesystem;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

using Unsafe = System.Runtime.CompilerServices.Unsafe;

namespace Game.Shared;

#if CLIENT_DLL || GAME_DLL

public struct Friction
{
	public SoundPatch? Patch;
	public BaseEntity? Object;
	public TimeUnit_t LastUpdateTime;
	public TimeUnit_t LastEffectTime;
}

public struct GameVCollisionEvent {
	public VCollisionEvent VCollisionEvent;
	public InlineArray2<Vector3> PreVelocity;
	public InlineArray2<Vector3> PostVelocity;
	public InlineArray2<Vector3> PreAngularVelocity;
	public InlineArray2<BaseEntity?> Entities;

	public void Init(ref VCollisionEvent ev){
		this.VCollisionEvent = ev;
		Entities[0] = null;
		Entities[1] = null;
	}
}

public enum TouchType
{
	Start,
	End
}

public struct TouchEvent
{
	public BaseEntity? Entity0;
	public BaseEntity? Entity1;
	public TouchType TouchType;
	public Vector3 EndPoint;
	public Vector3 Normal;
}

public struct FluidEvent
{
	public EHANDLE Entity;
	public TimeUnit_t ImpactTime;
}

public struct TriggerEvent
{
	public BaseEntity? TriggerEntity;
	public IPhysicsObject? TriggerPhysics;
	public BaseEntity? Entity;
	public IPhysicsObject? Object;
	public bool Start;
	public void Init(BaseEntity? triggerEntity, IPhysicsObject? triggerPhysics, BaseEntity? entity, IPhysicsObject? @object, bool startTouch) {
		TriggerEntity = triggerEntity;
		TriggerPhysics = triggerPhysics;
		Entity = entity;
		Object = @object;
		Start = startTouch;
	}
	public void Clear() {
		this = default;
	}
}
#endif

public class SolidSetDefaults : IVPhysicsKeyHandler
{
	Contents ContentsMask;

	public void ParseKeyValue<T>(ref T data, ReadOnlySpan<char> key, ReadOnlySpan<char> value) {
		if (stricmp(key, "contents") == 0)
			ContentsMask = (Contents)atoi(value);
	}

	public void SetDefaults<T>(ref T data) {
		Assert(typeof(T) == typeof(Solid));
		ref Solid solid = ref Unsafe.As<T, Solid>(ref data);
		solid.Params = g_PhysDefaultObjectParams;
		ContentsMask = Contents.Solid;
	}

	public Contents GetContentsMask() => ContentsMask;
}

[EngineComponent]
public static class PhysicsSharedGlobals
{
	[Dependency] public static IPhysics physics { get; set; } = null!;
	[Dependency] public static IPhysicsCollision physcollision { get; set; } = null!;
	[Dependency] public static IPhysicsSurfaceProps physprops { get; set; } = null!;


	public static IPhysicsObject? g_PhysWorldObject = null!;
	public static IPhysicsEnvironment physenv = null!;
#if PORTAL
	public static IPhysicsEnvironment physenv_main = null!;
#endif
	public static IPhysicsObjectPairHash g_EntityCollisionHash = null!;

	public static float MASS_SPEED2ENERGY(float mass, float speed) => speed * speed * mass;
	public static float MASS10_SPEED2ENERGY(float speed) => MASS_SPEED2ENERGY(10, speed);
	public static float MASS_ENERGY2SPEED(float mass, float energy) => MathF.Sqrt(energy / mass);
	public const float ENERGY_VOLUME_SCALE = 1.0f / 15500.0f;
	public const float FLUID_TIME_MAX = 2.0f;

	const string SURFACEPROP_MANIFEST_FILE = "scripts/surfaceproperties_manifest.txt";

	public static readonly ObjectParams g_PhysDefaultObjectParams = new() {
		MassCenterOverrideFn = null,
		Mass = 1.0f,
		Inertia = 1.0f,
		Damping = 0.1f,
		RotDamping = 0.1f,
		RotInertiaLimit = 0.05f,
		Name = "DEFAULT",
		GameData = null,
		Volume = 0f,
		DragCoefficient = 1.0f,
		EnableCollisions = true
	};

	public static readonly SolidSetDefaults g_SolidSetup = new();

	public static void AddSurfacepropFile(ReadOnlySpan<char> filename, IPhysicsSurfaceProps props, IFileSystem fileSystem) {
		using IFileHandle? file = fileSystem.Open(filename, FileOpenOptions.Read | FileOpenOptions.Binary, "GAME");
		if (file != null) {
			int len = (int)file.Stream.Length;
			Span<char> buffer = stackalloc char[len];
			using StreamReader reader = new(file.Stream);
			reader.Read(buffer);
			props.ParseSurfaceData(filename, buffer);
		}
	}
	public static void PhysParseSurfaceData(IPhysicsSurfaceProps props, IFileSystem fileSystem) {
		KeyValues manifest = new KeyValues(SURFACEPROP_MANIFEST_FILE);
		if (manifest.LoadFromFile(fileSystem, SURFACEPROP_MANIFEST_FILE, "GAME")) {
			for (KeyValues? sub = manifest.GetFirstSubKey(); sub != null; sub = sub.GetNextKey()) {
				if (0 == stricmp(sub.Name, "file")) {
					AddSurfacepropFile(sub.GetString(), props, fileSystem);
					continue;
				}

				Warning($"surfaceprops::Init:  Manifest '{SURFACEPROP_MANIFEST_FILE}' with bogus file type '{sub.Name}', expecting 'file'\n");
			}
		}
		else
			Error($"Unable to load manifest file '{SURFACEPROP_MANIFEST_FILE}'\n");
	}

	static IGameSystem physicsGameSystem = null!;
	public static void SetPhysicsGameSystem(IGameSystem system) => physicsGameSystem = system;
	public static IGameSystem PhysicsGameSystem() => physicsGameSystem;

#if CLIENT_DLL || GAME_DLL
	public static void PhysDestroyObject(IPhysicsObject? obj, BaseEntity entity) {
		//g_pPhysSaveRestoreManager->ForgetModel(pObject);

		obj?.SetGameData(null);
		g_EntityCollisionHash.RemoveAllPairsForObject(obj);
		if (entity != null && entity.IsMarkedForDeletion())
			g_EntityCollisionHash.RemoveAllPairsForObject(entity);

		physenv?.DestroyObject(obj);
	}

	public static PhysicsFlags PhysSetGameFlags(IPhysicsObject phys, PhysicsFlags gameFlags) {
		PhysicsFlags flags = phys.GetGameFlags();
		flags |= gameFlags;
		phys.SetGameFlags(flags);

		return flags;
	}

	public static PhysicsFlags PhysClearGameFlags(IPhysicsObject phys, PhysicsFlags gameFlags) {
		PhysicsFlags flags = phys.GetGameFlags();
		flags &= ~gameFlags;
		phys.SetGameFlags(flags);

		return flags;
	}

	public static void PhysRecheckObjectPair(IPhysicsObject obj0, IPhysicsObject obj1) {
		if (!obj0.IsStatic())
			obj0.RecheckCollisionFilter();
		if (!obj1.IsStatic())
			obj1.RecheckCollisionFilter();
	}

	public static void PhysEnableEntityCollisions(IPhysicsObject? obj0, IPhysicsObject? obj1) {
		if (obj0 == null || obj1 == null)
			return;

		g_EntityCollisionHash.RemoveObjectPair(obj0.GetGameData()!, obj1.GetGameData()!);
		PhysRecheckObjectPair(obj0, obj1);
	}

	public static void PhysDisableEntityCollisions(IPhysicsObject? obj0, IPhysicsObject? obj1) {
		if (obj0 == null || obj1 == null)
			return;

		g_EntityCollisionHash.AddObjectPair(obj0.GetGameData()!, obj1.GetGameData()!);
		PhysRecheckObjectPair(obj0, obj1);
	}

	public static void PhysDisableEntityCollisions(BaseEntity? entity0, BaseEntity? entity1) {
		if (entity0 == null || entity1 == null)
			return;

		g_EntityCollisionHash.AddObjectPair(entity0, entity1);
#if !CLIENT_DLL
		entity0.CollisionRulesChanged();
		entity1.CollisionRulesChanged();
#endif
	}

	public static void PhysEnableEntityCollisions(BaseEntity? entity0, BaseEntity? entity1) {
		if (entity0 == null || entity1 == null)
			return;

		g_EntityCollisionHash.RemoveObjectPair(entity0, entity1);
#if !CLIENT_DLL
		entity0.CollisionRulesChanged();
		entity1.CollisionRulesChanged();
#endif
	}

	public static bool PhysEntityCollisionsAreDisabled(BaseEntity entity0, BaseEntity entity1) => g_EntityCollisionHash.IsObjectPairInHash(entity0, entity1);

	public static void PhysEnableObjectCollisions(IPhysicsObject? obj0, IPhysicsObject? obj1) {
		if (obj0 == null || obj1 == null)
			return;

		g_EntityCollisionHash.RemoveObjectPair(obj0, obj1);
		PhysRecheckObjectPair(obj0, obj1);
	}

	public static void PhysDisableObjectCollisions(IPhysicsObject? obj0, IPhysicsObject? obj1) {
		if (obj0 == null || obj1 == null)
			return;

		g_EntityCollisionHash.AddObjectPair(obj0, obj1);
		PhysRecheckObjectPair(obj0, obj1);
	}

	public static void PhysComputeSlideDirection(IPhysicsObject physics, in Vector3 inputVelocity, in Vector3 inputAngularVelocity, out Vector3 outputVelocity, out Vector3 outputAngularVelocity, bool computeAngular, float minMass) {
		Vector3 velocity = inputVelocity;
		Vector3 angVel = inputAngularVelocity;

		IPhysicsFrictionSnapshot snapshot = physics.CreateFrictionSnapshot();
		while (snapshot.IsValid()) {
			IPhysicsObject other = snapshot.GetObject(1)!;
			if (!other.IsMoveable() || other.GetMass() > minMass) {
				snapshot.GetSurfaceNormal(out Vector3 normal);

				if (computeAngular)
					angVel = normal * Vector3.Dot(angVel, normal);

				float proj = Vector3.Dot(velocity, normal);
				if (proj > 0.0f)
					velocity -= normal * proj;
			}
			snapshot.NextFrictionData();
		}
		physics.DestroyFrictionSnapshot(snapshot);

		outputVelocity = velocity;
		outputAngularVelocity = angVel;
	}

	public static void PhysForceClearVelocity(IPhysicsObject phys) {
		IPhysicsFrictionSnapshot snapshot = phys.CreateFrictionSnapshot();
		Vector3 vel = default;
		Vector3 angVel = default;
		phys.SetVelocity(vel, angVel);
		while (snapshot.IsValid()) {
			snapshot.ClearFrictionForce();
			snapshot.RecomputeFriction();
			snapshot.NextFrictionData();
		}
		phys.DestroyFrictionSnapshot(snapshot);
	}

#if GAME_DLL
	public static void PhysFrictionSound(BaseEntity? entity, IPhysicsObject obj, float energy, int surfaceProps, int surfacePropsHit) {
		if (entity == null || energy < 75.0f || surfaceProps < 0)
			return;

		SurfaceData_ptr hit = physprops.GetSurfaceData(surfacePropsHit)!;
		SurfaceData_ptr surf = physprops.GetSurfaceData(surfaceProps)!;

		if ((CharTex)hit.Game.Material == CharTex.Default || (CharTex)surf.Game.Material == CharTex.Default)
			return;

		energy *= ENERGY_VOLUME_SCALE;

		float volume = energy * energy;

		UtlSymId_t soundName = surf.Sounds.ScrapeRough;
		ref short soundHandle = ref surf.SoundHandles.ScrapeRough;

		if (surf.Sounds.ScrapeSmooth != 0 && hit.Audio.RoughnessFactor < surf.Audio.RoughThreshold) {
			soundName = surf.Sounds.ScrapeSmooth;
			soundHandle = ref surf.SoundHandles.ScrapeRough;
		}

		ReadOnlySpan<char> soundStr = physprops.GetString(soundName);

		Game.Server.PhysicsHookGlobals.PhysFrictionSound(entity, obj, soundStr, ref soundHandle, volume);
	}

	public static void PhysFrictionEffect(in Vector3 pos, Vector3 vel, float energy, int surfaceProps, int surfacePropsHit) {
		Vector3 invVecVel = -vel;
		MathLib.VectorNormalize(ref invVecVel);

		SurfaceData_ptr surf = physprops.GetSurfaceData(surfaceProps)!;
		SurfaceData_ptr hit = physprops.GetSurfaceData(surfacePropsHit)!;

		switch ((CharTex)hit.Game.Material) {
			case CharTex.Dirt:
				if (energy < MASS10_SPEED2ENERGY(15))
					break;

				g_pEffects.Dust(pos, invVecVel, 1, 16);
				break;

			case CharTex.Concrete:
				if (energy < MASS10_SPEED2ENERGY(28))
					break;

				g_pEffects.Dust(pos, invVecVel, 1, 16);
				break;
		}

		if (energy > MASS10_SPEED2ENERGY(50)) {
			if ((CharTex)surf.Game.Material == CharTex.Metal || (CharTex)surf.Game.Material == CharTex.Grate) {
				switch ((CharTex)hit.Game.Material) {
					case CharTex.Concrete:
					case CharTex.Metal:
						g_pEffects.MetalSparks(pos, invVecVel);
						break;
				}
			}
		}
	}
#endif

	public static bool PhysModelParseSolidByIndex(ref Solid solid, BaseEntity entity, int modelIndex, int solidIndex) {
		VCollide? collide = modelinfo.GetVCollide(modelIndex);
		if (collide == null)
			return false;

		return PhysModelParseSolidByIndex(ref solid, entity, collide, solidIndex);
	}

	public static bool PhysModelParseSolid(ref Solid solid, BaseEntity entity, int modelIndex) {
		return PhysModelParseSolidByIndex(ref solid, entity, modelIndex, -1);
	}

	public static bool PhysModelParseSolidByIndex(ref Solid solid, BaseEntity entity, VCollide? collide, int solidIndex) {
		if (collide == null || collide.KeyValues == null)
			return false;

		bool parsed = false;

		solid = default;
		solid.Params = g_PhysDefaultObjectParams;

		IVPhysicsKeyParser parse = physcollision.VPhysicsKeyParserCreate(collide.KeyValues);
		while (!parse.Finished()) {
			ReadOnlySpan<char> block = parse.GetCurrentBlockName();
			if (strcmpi(block, "solid") == 0) {
				Solid tmpSolid = default;
				tmpSolid.Params = g_PhysDefaultObjectParams;

				parse.ParseSolid(ref tmpSolid, g_SolidSetup);

				if (solidIndex < 0 || tmpSolid.Index == solidIndex) {
					parsed = true;
					solid = tmpSolid;
					break;
				}
			}
			else
				parse.SkipBlock();
		}
		physcollision.VPhysicsKeyParserDestroy(parse);

		// collisions are off by default
		solid.Params.EnableCollisions = true;

		solid.Params.GameData = entity;
		solid.Params.Name = new string(((ReadOnlySpan<char>)entity.GetModelName()).SliceNullTerminatedString());
		return parsed;
	}

	public static IPhysicsObject? PhysModelCreate(BaseEntity entity, int modelIndex, in Vector3 origin, in QAngle angles, ref Solid solid) {
		if (physenv == null)
			return null;

		VCollide? collide = modelinfo.GetVCollide(modelIndex);
		if (collide == null || collide.SolidCount == 0 || collide.Solids == null) {
			return null;
		}

		if (Unsafe.IsNullRef(ref solid)) {
			Solid tmpSolid = default;
			if (!PhysModelParseSolidByIndex(ref tmpSolid, entity, collide, -1)) {
				return null;
			}
			IPhysicsObject? r = PhysModelCreateInternal(entity, collide, in origin, in angles, ref tmpSolid);
			return r;
		}

		return PhysModelCreateInternal(entity, collide, in origin, in angles, ref solid);
	}

	static IPhysicsObject? PhysModelCreateInternal(BaseEntity entity, VCollide collide, in Vector3 origin, in QAngle angles, ref Solid solid) {
		int surfaceProp = -1;
		ReadOnlySpan<char> surfacePropName = ((ReadOnlySpan<char>)solid.SurfaceProp).SliceNullTerminatedString();
		if (!surfacePropName.IsEmpty)
			surfaceProp = (int)physprops.GetSurfaceIndex(surfacePropName);

		IPhysicsObject? obj = physenv.CreatePolyObject(collide.Solids![solid.Index]!, surfaceProp, in origin, in angles, ref solid.Params);
		return obj;
	}

	public static void PhysCreateVirtualTerrain(BaseEntity world, in ObjectParams defaultParams) {
		if (physenv == null)
			return;

		for (int i = 0; i < BSPFileCommon.MAX_MAP_DISPINFO; i++) {
			PhysCollide? collide = modelinfo.GetCollideForVirtualTerrain(i);
			if (collide != null) {
				Solid solid = default;
				solid.Params = defaultParams;
				solid.Params.EnableCollisions = true;
				solid.Params.GameData = world;
				solid.Params.Name = $"vdisp_{i:D4}";
				int surfaceData = (int)physprops.GetSurfaceIndex("default");
				IPhysicsObject? obj = physenv.CreatePolyObjectStatic(collide, surfaceData, vec3_origin, vec3_angle, ref solid.Params);
				obj!.SetCallbackFlags(obj.GetCallbackFlags() | CallbackFlags.NeverDeleted);
			}
		}
	}

	public static IPhysicsObject? PhysCreateWorld_Shared(BaseEntity world, VCollide? worldCollide, in ObjectParams defaultParams) {
		Solid solid = default;
		Fluid fluid = default;

		if (physenv == null || worldCollide == null || worldCollide.SolidCount < 1 || worldCollide.Solids == null)
			return null;

		int surfaceData = (int)physprops.GetSurfaceIndex("default");

		ObjectParams oparams = defaultParams;
		oparams.GameData = world;
		oparams.Name = "world";

		Span<nint> surfaceTable = stackalloc nint[128];
		IVPhysicsKeyParser tableParse = physcollision.VPhysicsKeyParserCreate(worldCollide.KeyValues!);
		while (!tableParse.Finished()) {
			if (strcmpi(tableParse.GetCurrentBlockName(), "materialtable") == 0) {
				tableParse.ParseSurfaceTable(surfaceTable, null);
				physprops.SetWorldMaterialIndexTable(surfaceTable);
			}
			else
				tableParse.SkipBlock();
		}
		physcollision.VPhysicsKeyParserDestroy(tableParse);

		IPhysicsObject? worldPhysics = physenv.CreatePolyObjectStatic(worldCollide.Solids[0]!, surfaceData, vec3_origin, vec3_angle, ref oparams);

		IVPhysicsKeyParser parse = physcollision.VPhysicsKeyParserCreate(worldCollide.KeyValues!);

		bool createVirtualTerrain = false;
		while (!parse.Finished()) {
			ReadOnlySpan<char> block = parse.GetCurrentBlockName();

			if (strcmpi(block, "solid") == 0 || strcmpi(block, "staticsolid") == 0) {
				solid = default;
				solid.Params = defaultParams;
				parse.ParseSolid(ref solid, g_SolidSetup);
				solid.Params.EnableCollisions = true;
				solid.Params.GameData = world;
				solid.Params.Name = "world";
				surfaceData = (int)physprops.GetSurfaceIndex("default");

				if (solid.Index == 0)
					continue;

				if (solid.Index >= worldCollide.SolidCount)
					continue;

				if (worldCollide.Solids[solid.Index] == null) {
					createVirtualTerrain = true;
					continue;
				}

				IPhysicsObject? obj = physenv.CreatePolyObjectStatic(worldCollide.Solids[solid.Index]!, surfaceData, vec3_origin, vec3_angle, ref solid.Params);
				if (obj == null)
					continue;

				Assert(g_SolidSetup.GetContentsMask() != 0);
				obj.SetContents((uint)g_SolidSetup.GetContentsMask());

				worldPhysics ??= obj;
			}
			else if (strcmpi(block, "fluid") == 0) {
				parse.ParseFluid(ref fluid, null);
			}
			else if (strcmpi(block, "virtualterrain") == 0) {
				createVirtualTerrain = true;
				parse.SkipBlock();
			}
			else
				parse.SkipBlock();
		}
		physcollision.VPhysicsKeyParserDestroy(parse);

		if (createVirtualTerrain && physcollision.SupportsVirtualMesh())
			PhysCreateVirtualTerrain(world, defaultParams);

		return worldPhysics;
	}
#endif
}
