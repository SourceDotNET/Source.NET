using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Runtime.Intrinsics;

namespace Source.Common.Physics;

public static class PhysicsConversions
{
	public const float POUNDS_PER_KG = 2.2f;
	public const float KG_PER_POUND = 1.0f / POUNDS_PER_KG;

	public static float lbs2kg(float x) => x * KG_PER_POUND;
	public static float kg2lbs(float x) => x * POUNDS_PER_KG;
}

public static class PhysicsConstants
{
	public const float VPHYSICS_MIN_MASS = 0.1f;
	public const float VPHYSICS_MAX_MASS = 5e4f;
}

public enum PhysInterfaceId
{
	Unknown,
	IPhysicsObject,
	IPhysicsFluidController,
	IPhysicsSpring,
	IPhysicsConstraintGroup,
	IPhysicsConstraint,
	IPhysicsShadowController,
	IPhysicsPlayerController,
	IPhysicsMotionController,
	IPhysicsVehicleController,
	IPhysicsGameTrace,

	NumTypes
}

public interface IVPhysicsDebugOverlay
{
	void AddEntityTextOverlay(int ent_index, int line_offset, TimeUnit_t duration, int r, int g, int b, int a, ReadOnlySpan<char> text);
	void AddBoxOverlay(in Vector3 origin, in Vector3 mins, in Vector3 max, in QAngle orientation, int r, int g, int b, int a, TimeUnit_t duration);
	void AddTriangleOverlay(in Vector3 p1, in Vector3 p2, in Vector3 p3, int r, int g, int b, int a, bool noDepthTest, TimeUnit_t duration);
	void AddLineOverlay(in Vector3 origin, in Vector3 dest, int r, int g, int b, bool noDepthTest, TimeUnit_t duration);
	void AddTextOverlay(in Vector3 origin, TimeUnit_t duration, ReadOnlySpan<char> text);
	void AddTextOverlay(in Vector3 origin, int line_offset, TimeUnit_t duration, ReadOnlySpan<char> text);
	void AddScreenTextOverlay(float xPos, float yPos, TimeUnit_t duration, int r, int g, int b, int a, ReadOnlySpan<char> text);
	void AddSweptBoxOverlay(in Vector3 start, in Vector3 end, in Vector3 mins, in Vector3 max, in QAngle angles, int r, int g, int b, int a, TimeUnit_t duration);
	void AddTextOverlayRGB(in Vector3 origin, int line_offset, TimeUnit_t duration, float r, float g, float b, float alpha, ReadOnlySpan<char> text);
}

public interface IPhysics
{
	IPhysicsEnvironment CreateEnvironment();
	void DestroyEnvironment(IPhysicsEnvironment? env);
	IPhysicsEnvironment? GetActiveEnvironmentByIndex(int index);

	// Creates a fast hash of pairs of objects
	// Useful for maintaining a table of object relationships like pairs that do not collide.
	IPhysicsObjectPairHash CreateObjectPairHash();
	void DestroyObjectPairHash(IPhysicsObjectPairHash hash);

	// holds a cache of these by id.  So you can get by id to search for the previously created set
	// UNDONE: Sets are currently limited to 32 elements.  More elements will return NULL in create.
	// NOTE: id is not allowed to be zero.
	IPhysicsCollisionSet FindOrCreateCollisionSet(uint id, int maxElementCount);
	IPhysicsCollisionSet FindCollisionSet(uint id);
	void DestroyAllCollisionSets();
}
public struct TruncatedCone
{
	public Vector3 Origin;
	public Vector3 Normal;
	public float H;
	public float Theta;
}

public class PhysConvex;
public class PhysPolysoup;
public interface IPolyhedron;
public class Polyhedron;

public interface IPhysicsCollision
{
	// produce a convex element from verts (convex hull around verts)
	PhysConvex ConvexFromVerts(Span<Vector3> verts);
	// produce a convex element from planes (csg of planes)
	PhysConvex ConvexFromPlanes(Span<float> planes, float mergeDistance);
	// calculate volume of a convex element
	float ConvexVolume(PhysConvex convex);

	float ConvexSurfaceArea(PhysConvex convex);
	// store game-specific data in a convex solid
	void SetConvexGameData(PhysConvex convex, uint gameData);
	// If not converted, free the convex elements with this call
	void ConvexFree(PhysConvex convex);
	PhysConvex BBoxToConvex(in Vector3 mins, in Vector3 maxs);
	// produce a convex element from a convex polyhedron
	PhysConvex ConvexFromConvexPolyhedron<T>(in T convexPolyhedron) where T : IPolyhedron; // Turned generic in the hopes we can have class vs value type here?
																						   // produce a set of convex triangles from a convex polygon, normal is assumed to be on the side with forward point ordering, which should be clockwise, output will need to be able to hold exactly (iPointCount-2) convexes
	void ConvexesFromConvexPolygon(in Vector3 polyNormal, ReadOnlySpan<Vector3> points, int pointCount, Span<PhysConvex> output);

	// concave objects
	// create a triangle soup
	PhysPolysoup PolysoupCreate();
	// destroy the container and memory
	void PolysoupDestroy(PhysPolysoup soup);
	// add a triangle to the soup
	void PolysoupAddTriangle(PhysPolysoup soup, in Vector3 a, in Vector3 b, in Vector3 c, int materialIndex7bits);
	// convert the convex into a compiled collision model
	PhysCollide ConvertPolysoupToCollide(PhysPolysoup soup, bool useMOPP);

	// Convert an array of convex elements to a compiled collision model (this deletes the convex elements)
	PhysCollide ConvertConvexToCollide(Span<PhysConvex> convex);
	PhysCollide ConvertConvexToCollideParams(Span<PhysConvex> convex, in ConvertConvexParams convertParams);
	// Free a collide that was created with ConvertConvexToCollide()
	void DestroyCollide(PhysCollide collide);

	// Get the memory size in bytes of the collision model for serialization
	int CollideSize(PhysCollide collide);
	// serialize the collide to a block of memory
	int CollideWrite(Span<byte> dest, PhysCollide collide, bool swap = false);
	// unserialize the collide from a block of memory
	PhysCollide UnserializeCollide(ReadOnlySpan<byte> buffer, int size, int index);

	// compute the volume of a collide
	float CollideVolume(PhysCollide collide);
	// compute surface area for tools
	float CollideSurfaceArea(PhysCollide collide);

	// Get the support map for a collide in the given direction
	Vector3 CollideGetExtent(PhysCollide collide, in Vector3 collideOrigin, in QAngle collideAngles, in Vector3 direction);

	// Get an AABB for an oriented collision model
	void CollideGetAABB(out Vector3 mins, out Vector3 maxs, PhysCollide collide, in Vector3 collideOrigin, in QAngle collideAngles);

	void CollideGetMassCenter(PhysCollide collide, out Vector3 outMassCenter);
	void CollideSetMassCenter(PhysCollide collide, in Vector3 massCenter);
	// get the approximate cross-sectional area projected orthographically on the bbox of the collide
	// NOTE: These are fractional areas - unitless.  Basically this is the fraction of the OBB on each axis that
	// would be visible if the object were rendered orthographically.
	// NOTE: This has been precomputed when the collide was built or this function will return 1,1,1
	Vector3 CollideGetOrthographicAreas(PhysCollide collide);
	void CollideSetOrthographicAreas(PhysCollide collide, in Vector3 areas);

	// query the vcollide index in the physics model for the instance
	int CollideIndex(PhysCollide collide);

	// Convert a bbox to a collide
	PhysCollide BBoxToCollide(in Vector3 mins, in Vector3 maxs);
	int GetConvexesUsedInCollideable(PhysCollide collideable, Span<PhysConvex> outputArray);


	// Trace an AABB against a collide
	void TraceBox(in Vector3 start, in Vector3 end, in Vector3 mins, in Vector3 maxs, PhysCollide collide, in Vector3 collideOrigin, in QAngle collideAngles, out Trace trace);
	void TraceBox(in Ray ray, PhysCollide collide, in Vector3 collideOrigin, in QAngle collideAngles, out Trace trace);
	void TraceBox(in Ray ray, Contents contentsMask, IConvexInfo? convexInfo, PhysCollide collide, in Vector3 collideOrigin, in QAngle collideAngles, out Trace trace);

	// Trace one collide against another
	void TraceCollide(in Vector3 start, in Vector3 end, PhysCollide pSweepCollide, in QAngle sweepAngles, PhysCollide collide, in Vector3 collideOrigin, in QAngle collideAngles, out Trace trace);

	// relatively slow test for box vs. truncated cone
	bool IsBoxIntersectingCone(in Vector3 boxAbsMins, in Vector3 boxAbsMaxs, in TruncatedCone cone);

	// loads a set of solids into a vcollide_t
	void VCollideLoad(VCollide output, int solidCount, ReadOnlySpan<byte> buffer, bool swap = false);
	// destroyts the set of solids created by VCollideLoad
	void VCollideUnload(VCollide vCollide);

	// begins parsing a vcollide.  NOTE: This keeps pointers to the text
	// If you free the text and call members of IVPhysicsKeyParser, it will crash
	IVPhysicsKeyParser VPhysicsKeyParserCreate(ReadOnlySpan<byte> keyData);
	// Free the parser created by VPhysicsKeyParserCreate
	void VPhysicsKeyParserDestroy(IVPhysicsKeyParser parser);

	// creates a list of verts from a collision mesh
	int CreateDebugMesh(PhysCollide collisionModel, out Span<Vector3> outVerts);
	// destroy the list of verts created by CreateDebugMesh
	void DestroyDebugMesh(int vertCount, Span<Vector3> outVerts);

	// create a queryable version of the collision model
	ICollisionQuery CreateQueryModel(PhysCollide collide);
	// destroy the queryable version
	void DestroyQueryModel(ICollisionQuery query);

	IPhysicsCollision ThreadContextCreate();
	void ThreadContextDestroy(IPhysicsCollision threadContext);

	PhysCollide? CreateVirtualMesh(in VirtualMeshParams meshParams);
	bool SupportsVirtualMesh();

	bool GetBBoxCacheSize(out uint cachedSize, out nint cachedCount);

	// extracts a polyhedron that defines a PhysConvex's shape
	Polyhedron PolyhedronFromConvex(PhysConvex convex, bool useTempPolyhedron);

	// dumps info about the collide to Msg()
	void OutputDebugInfo(PhysCollide collide);
	uint ReadStat(int statID);
}
public interface ICollisionQuery
{
	// number of convex pieces in the whole solid
	int ConvexCount();
	// triangle count for this convex piece
	int TriangleCount(int convexIndex);
	// get the stored game data
	uint GetGameData(int convexIndex);
	// Gets the triangle's verts to an array
	void GetTriangleVerts(int convexIndex, int triangleIndex, Span<Vector3> verts);

	// UNDONE: This doesn't work!!!
	void SetTriangleVerts(int convexIndex, int triangleIndex, ReadOnlySpan<Vector3> verts);

	// returns the 7-bit material index
	int GetTriangleMaterialIndex(int convexIndex, int triangleIndex);
	// sets a 7-bit material index for this triangle
	void SetTriangleMaterialIndex(int convexIndex, int triangleIndex, int index7bits);
}
public interface IPhysicsGameTrace
{
	void VehicleTraceRay(in Ray ray, object vehicle, out Trace trace);
	void VehicleTraceRayWithWater(in Ray ray, object vehicle, out Trace trace);
	bool VehiclePointInWater(in Vector3 vecPoint);
}
public interface IConvexInfo
{
	uint GetContents(int convexGameData);
}
public interface IPhysicsCollisionData
{
	void GetSurfaceNormal(out Vector3 vec);        // normal points toward second object (object index 1)
	void GetContactPoint(out Vector3 vec);     // contact point of collision (in world space)
	void GetContactSpeed(out Vector3 vec);     // speed of surface 1 relative to surface 0 (in world space)
}

public struct VCollisionEvent
{
	public InlineArray2<IPhysicsObject?> Objects;
	public InlineArray2<int> SurfaceProps;
	public bool IsCollision;
	public bool IsShadowCollision;
	public float DeltaCollisionTime;
	public float CollisionSpeed;               // only valid at postCollision
	public IPhysicsCollisionData? InternalData;       // may change pre/post collision
}

public interface IPhysicsCollisionEvent
{
	// returns the two objects that collided, time between last collision of these objects
	// and an opaque data block of collision information
	// NOTE: PreCollision/PostCollision ALWAYS come in matched pairs!!!
	void PreCollision(ref VCollisionEvent ev);
	void PostCollision(ref VCollisionEvent ev);

	// This is a scrape event.  The object has scraped across another object consuming the indicated energy
	void Friction(IPhysicsObject obj, float energy, int surfaceProps, int surfacePropsHit, IPhysicsCollisionData data);

	void StartTouch(IPhysicsObject obj1, IPhysicsObject obj2, IPhysicsCollisionData touchData);
	void EndTouch(IPhysicsObject obj1, IPhysicsObject obj2, IPhysicsCollisionData toichData);

	void FluidStartTouch(IPhysicsObject obj, IPhysicsFluidController fluid);
	void FluidEndTouch(IPhysicsObject obj, IPhysicsFluidController fluid);

	void PostSimulationFrame();

	void ObjectEnterTrigger(IPhysicsObject obj1, IPhysicsObject obj2) { }
	void ObjectLeaveTrigger(IPhysicsObject obj1, IPhysicsObject obj2) { }
}
public interface IPhysicsObjectEvent
{
	void ObjectWake(IPhysicsObject obj);
	// called when an object goes to sleep (no longer simulating)
	void ObjectSleep(IPhysicsObject obj);
}
public interface IPhysicsConstraintEvent
{
	void ConstraintBroken(IPhysicsConstraint constraint);
}

public struct HLShadowControlParams
{
	public Vector3 TargetPosition;
	public QAngle TargetRotation;
	public float MaxAngular;
	public float MaxDampAngular;
	public float MaxSpeed;
	public float MaxDampSpeed;
	public float DampFactor;
	public float TeleportDistance;
}

public interface IPhysicsShadowController
{
	void Update(in Vector3 position, in QAngle angles, float timeOffset);
	void MaxSpeed(float maxSpeed, float maxAngularSpeed);
	void StepUp(float height);

	// If the teleport distance is non-zero, the object will be teleported to 
	// the target location when the error exceeds this quantity.
	void SetTeleportDistance(float teleportDistance);
	bool AllowsTranslation();
	bool AllowsRotation();

	// There are two classes of shadow objects:
	// 1) Game physics controlled, shadow follows game physics (this is the default)
	// 2) Physically controlled - shadow position is a target, but the game hasn't guaranteed that the space can be occupied by this object
	void SetPhysicallyControlled(bool isPhysicallyControlled);
	bool IsPhysicallyControlled();
	void GetLastImpulse(out Vector3 vec);
	void UseShadowMaterial(bool useShadowMaterial);
	void ObjectMaterialChanged(int materialIndex);


	//Basically get the last inputs to IPhysicsShadowController::Update(), returns last input to timeOffset in Update()
	float GetTargetPosition(out Vector3 positionOut, out QAngle anglesOut);

	float GetTeleportDistance();
	void GetMaxSpeed(out float maxSpeedOut, out float maxAngularSpeedOut);
}

public enum SimResult
{
	Nothing = 0,
	LocalAcceleration,
	LocalForce,
	GlobalAcceleration,
	GlobalForce
}

public interface IMotionEvent
{
	SimResult Simulate(IPhysicsMotionController? controller, IPhysicsObject? obj, TimeUnit_t deltaTime, out Vector3 linear, out Vector3 angular);
}
public interface IPhysicsMotionController
{
	void SetEventHandler(IMotionEvent handler);
	void AttachObject(IPhysicsObject obj, bool checkIfAlreadyAttached);
	void DetachObject(IPhysicsObject obj);

	// returns the number of objects currently attached to the controller
	nint CountObjects();
	// NOTE: pObjectList is an array with at least CountObjects() allocated
	void GetObjects(Span<IPhysicsObject> objectList);
	// detaches all attached objects
	void ClearObjects();
	// wakes up all attached objects
	void WakeObjects();

	public enum Priority
	{
		LowPriority = 0,
		MediumPriority = 1,
		HighPriority = 2,
	}
	void SetPriority(Priority priority);
}
public interface IPhysicsCollisionSolver
{
	int ShouldCollide(IPhysicsObject obj0, IPhysicsObject obj1, object gameData0, object gameData1);
	int ShouldSolvePenetration(IPhysicsObject obj0, IPhysicsObject obj1, object gameData0, object gameData1, TimeUnit_t dt);

	// pObject has already done the max number of collisions this tick, should we freeze it to save CPU?
	bool ShouldFreezeObject(IPhysicsObject obj);

	// The system has already done too many collision checks, performance will suffer.
	// How many more should it do?
	int AdditionalCollisionChecksThisTick(int currentChecksDone);

	// This list of objects is in a connected contact graph that is too large to solve quickly
	// return true to freeze the system, false to solve it
	bool ShouldFreezeContacts(Span<IPhysicsObject> objectList);
}

public enum PhysicsTraceType
{
	Everything = 0,
	StaticOnly,
	MovingOnly,
	TriggersOnly,
	StaticAndMoving,
}

public interface IPhysicsTraceFilter
{
	bool ShouldHitObject(IPhysicsObject? obj, int contentsMask);
	PhysicsTraceType GetTraceType();
}

public interface IPhysicsEnvironment
{
	void SetDebugOverlay(IServiceProvider debugOverlayFactory);
	IVPhysicsDebugOverlay? GetDebugOverlay();

	// gravity is a 3-vector in in/s^2
	void SetGravity(in Vector3 gravityVector);
	void GetGravity(out Vector3 gravityVector);

	// air density is in kg / m^3 (water is 1000)
	// This controls drag, air that is more dense has more drag.
	void SetAirDensity(float density);
	float GetAirDensity();

	// object creation
	// create a polygonal object.  pCollisionModel was created by the physics builder DLL in a pre-process.
	IPhysicsObject? CreatePolyObject(PhysCollide pCollisionModel, int materialIndex, in Vector3 position, in QAngle angles, ref ObjectParams objParams);
	// same as above, but this one cannot move or rotate (infinite mass/inertia)
	IPhysicsObject? CreatePolyObjectStatic(PhysCollide pCollisionModel, int materialIndex, in Vector3 position, in QAngle angles, ref ObjectParams objParams);
	// Create a perfectly spherical object
	IPhysicsObject? CreateSphereObject(float radius, int materialIndex, in Vector3 position, in QAngle angles, ref ObjectParams objParams, bool isStatic);
	// destroy an object created with CreatePolyObject() or CreatePolyObjectStatic()
	void DestroyObject(IPhysicsObject? obj);

	// Create a polygonal fluid body out of the specified collision model
	// This object will affect any other objects that collide with the collision model
	IPhysicsFluidController CreateFluidController(IPhysicsObject pFluidObject, ref FluidParams fluidParams);
	// Destroy an object created with CreateFluidController()
	void DestroyFluidController(IPhysicsFluidController fluidController);

	// Create a simulated spring that connects 2 objects
	IPhysicsSpring CreateSpring(IPhysicsObject objStart, IPhysicsObject objEnd, ref SpringParams fluidParams);
	void DestroySpring(IPhysicsSpring spring);

	// Create a constraint in the space of pReferenceObject which is attached by the constraint to pAttachedObject
	IPhysicsConstraint CreateRagdollConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintRagdollParams ragdoll);
	IPhysicsConstraint CreateHingeConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintHingeParams hinge);
	IPhysicsConstraint CreateFixedConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintFixedParams fixedParams);
	IPhysicsConstraint CreateSlidingConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintSlidingParams sliding);
	IPhysicsConstraint CreateBallsocketConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintBallSocketParams ballsocket);
	IPhysicsConstraint CreatePulleyConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintPulleyParams pulley);
	IPhysicsConstraint CreateLengthConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintLengthParams length);

	void DestroyConstraint(IPhysicsConstraint constraint);

	IPhysicsConstraintGroup CreateConstraintGroup(in ConstraintGroupParams groupParams);
	void DestroyConstraintGroup(IPhysicsConstraintGroup group);

	IPhysicsShadowController CreateShadowController(IPhysicsObject obj, bool allowTranslation, bool allowRotation);
	void DestroyShadowController(IPhysicsShadowController controller);

	IPhysicsPlayerController CreatePlayerController(IPhysicsObject obj);
	void DestroyPlayerController(IPhysicsPlayerController controller);

	IPhysicsMotionController CreateMotionController(IMotionEvent handler);
	void DestroyMotionController(IPhysicsMotionController controller);

	IPhysicsVehicleController CreateVehicleController(IPhysicsObject pVehicleBodyObject, in VehicleParams parms, VehicleType vehicleType, IPhysicsGameTrace gameTrace);
	void DestroyVehicleController(IPhysicsVehicleController controller);

	// install a function to filter collisions/penentration
	void SetCollisionSolver(IPhysicsCollisionSolver solver);

	// run the simulator for deltaTime seconds
	void Simulate(TimeUnit_t deltaTime);
	// true if currently running the simulator (i.e. in a callback during physenv->Simulate())
	bool IsInSimulation();

	// Manage the timestep (period) of the simulator.  The main functions are all integrated with
	// this period as dt.
	TimeUnit_t GetSimulationTimestep();
	void SetSimulationTimestep(TimeUnit_t timestep);

	// returns the current simulation clock's value.  This is an absolute time.
	TimeUnit_t GetSimulationTime();
	void ResetSimulationClock();
	// returns the current simulation clock's value at the next frame.  This is an absolute time.
	TimeUnit_t GetNextFrameTime();

	// Collision callbacks (game code collision response)
	void SetCollisionEventHandler(IPhysicsCollisionEvent collisionEvents);
	void SetObjectEventHandler(IPhysicsObjectEvent objectEvents);
	void SetConstraintEventHandler(IPhysicsConstraintEvent constraintEvents);

	void SetQuickDelete(bool quick);

	int GetActiveObjectCount();
	void GetActiveObjects(Span<IPhysicsObject> outputObjectList);
	ReadOnlySpan<IPhysicsObject> GetObjectList();
	bool TransferObject(IPhysicsObject obj, IPhysicsEnvironment destinationEnvironment);

	void CleanupDeleteList();
	void EnableDeleteQueue(bool enable);

	// Save/Restore methods
	bool Save(in PhysSaveParams parms);
	void PreRestore(in PhysPreRestoreParams parms);
	bool Restore(in PhysRestoreParams parms);
	void PostRestore();

	// Debugging:
	bool IsCollisionModelUsed(PhysCollide collide);

	// Physics world version of the enginetrace API:
	void TraceRay<Filter>(in Ray ray, uint fMask, in Filter traceFilter, out Trace trace) where Filter : IPhysicsTraceFilter;
	void SweepCollideable<Filter>(PhysCollide collide, in Vector3 absStart, in Vector3 absEnd, in QAngle angles, uint fMask, in Filter traceFilter, out Trace trace) where Filter : IPhysicsTraceFilter;

	// performance tuning
	void GetPerformanceSettings(out PhysicsPerformanceParams output);
	void SetPerformanceSettings(in PhysicsPerformanceParams settings);

	// perf/cost statistics
	void ReadStats(out PhysicsStats output);
	void ClearStats();

	uint GetObjectSerializeSize(IPhysicsObject obj);
	void SerializeObjectToBuffer(IPhysicsObject obj, Span<byte> buffer);
	IPhysicsObject UnserializeObjectFromBuffer(object gameData, ReadOnlySpan<byte> buffer, bool enableCollisions);


	void EnableConstraintNotify(bool enable);
	void DebugCheckContacts();
}

public enum CallbackFlags : ushort
{
	GlobalCollision = 0x0001,
	GlobalFriction = 0x0002,
	GlobalTouch = 0x0004,
	GlobalTouchStatic = 0x0008,
	ShadowCollision = 0x0010,
	GlobalCollideStatic = 0x0020,
	IsVehicleWheel = 0x0040,
	FluidTouch = 0x0100,
	NeverDeleted = 0x0200,          // HACKHACK: This means this object will never be deleted (set on the world)
	MarkedForDelete = 0x0400,       // This allows vphysics to skip some work for this object since it will be
									// deleted later this frame. (Set automatically by destroy calls)
	EnablingCollision = 0x0800,     // This is active during the time an object is enabling collisions
									// allows us to skip collisions between "new" objects and objects marked for delete
	DoFluidSimulation = 0x1000,     // remove this to opt out of fluid simulations
	IsPlayerController = 0x2000,    // HACKHACK: Set this on players until player cotrollers are unified with shadow controllers
	CheckCollisionDisable = 0x4000,
	MarkedForTest = 0x8000,         // debug -- marked object is being debugged
}

public enum PhysicsFlags : ushort
{
	///<summary> does slice damage, not just blunt damage </summary>
	DamageSlice = 0x0001,

	///<summary> object is constrained to the world, so it should behave like a static </summary>
	ConstraintStatic = 0x0002,

	///<summary> object is held by the player, so have a very inelastic collision response </summary>
	PlayerHeld = 0x0004,

	///<summary> object is part of a client or server ragdoll </summary>
	PartOfRagdoll = 0x0008,

	///<summary> object is part of a multi-object entity </summary>
	MultiObjectEntity = 0x0010,

	///<summary> HULK SMASH! (Do large damage even if the mass is small) </summary>
	HeavyObject = 0x0020,

	///<summary> This object is currently stuck inside another object </summary>
	Penetrating = 0x0040,

	///<summary> Player can't pick this up for some game rule reason </summary>
	NoPlayerPickup = 0x0080,

	///<summary> Player threw this object </summary>
	WasThrown = 0x0100,

	///<summary> does dissolve damage, not just blunt damage </summary>
	DamageDissolve = 0x0200,

	///<summary> don't do impact damage to anything </summary>
	NoImpactDamage = 0x0400,

	///<summary> Don't do impact damage to NPC's. This is temporary for NPC's shooting combine balls (sjb) </summary>
	NoNPCImpactDamage = 0x0800,

	///<summary> don't collide with other objects that are part of the same entity </summary>
	NoSelfCollisions = 0x8000,

}

public interface IPhysicsObject
{
	// returns true if this object is static/unmoveable
	// NOTE: returns false for objects that are not created static, but set EnableMotion(false);
	// Call IsMoveable() to find if the object is static OR has motion disabled
	bool IsStatic();
	bool IsAsleep();
	bool IsTrigger();
	bool IsFluid();       // fluids are special triggers with fluid controllers attached, they return true to IsTrigger() as well!
	bool IsHinged();
	bool IsCollisionEnabled();
	bool IsGravityEnabled();
	bool IsDragEnabled();
	bool IsMotionEnabled();
	bool IsMoveable();     // legacy: IsMotionEnabled() && !IsStatic()
	bool IsAttachedToConstraint(bool externalOnly);

	// Enable / disable collisions for this object
	void EnableCollisions(bool enable);
	// Enable / disable gravity for this object
	void EnableGravity(bool enable);
	// Enable / disable air friction / drag for this object
	void EnableDrag(bool enable);
	// Enable / disable motion (pin / unpin the object)
	void EnableMotion(bool enable);

	// Game can store data in each object (link back to game object)
	void SetGameData(object? gameData);
	object? GetGameData();
	// This flags word can be defined by the game as well
	void SetGameFlags(PhysicsFlags userFlags);
	PhysicsFlags GetGameFlags();
	void SetGameIndex(ushort gameIndex);
	ushort GetGameIndex();

	// setup various callbacks for this object
	void SetCallbackFlags(CallbackFlags callbackflags);
	// get the current callback state for this object
	CallbackFlags GetCallbackFlags();

	// "wakes up" an object
	// NOTE: ALL OBJECTS ARE "Asleep" WHEN CREATED
	void Wake();
	void Sleep();
	// call this when the collision filter conditions change due to this 
	// object's state (e.g. changing solid type or collision group)
	void RecheckCollisionFilter();
	// NOTE: Contact points aren't updated when collision rules change, call this to force an update
	// UNDONE: Force this in RecheckCollisionFilter() ?
	void RecheckContactPoints();

	// mass accessors
	void SetMass(float mass);
	float GetMass();
	// get 1/mass (it's cached)
	float GetInvMass();
	Vector3 GetInertia();
	Vector3 GetInvInertia();
	void SetInertia(in Vector3 inertia);

	void SetDamping(ref float speed, ref float rot);
	void GetDamping(out float speed, out float rot);

	// coefficients are optional, pass either
	void SetDragCoefficient(in float? drag, in float? angularDrag);
	void SetBuoyancyRatio(float ratio);         // Override bouyancy
	float GetBuoyancyRatio();

	// material index
	int GetMaterialIndex();
	void SetMaterialIndex(int materialIndex);

	// contents bits
	uint GetContents();
	void SetContents(uint contents);

	// Get the radius if this is a sphere object (zero if this is a polygonal mesh)
	float GetSphereRadius();
	float GetEnergy();
	Vector3 GetMassCenterLocalSpace();

	// NOTE: This will teleport the object
	void SetPosition(in Vector3 worldPosition, in QAngle angles, bool isTeleport);
	void SetPositionMatrix(in Matrix3x4 matrix, bool isTeleport);

	void GetPosition(out Vector3 worldPosition, out QAngle angles);
	void GetPositionMatrix(out Matrix3x4 positionMatrix);
	// force the velocity to a new value
	// NOTE: velocity is in worldspace, angularVelocity is relative to the object's 
	// local axes (just like pev->velocity, pev->avelocity)
	void SetVelocity(in Vector3 velocity, in Vector3 angularVelocity);

	// like the above, but force the change into the simulator immediately
	void SetVelocityInstantaneous(in Vector3 velocity, in Vector3 angularVelocity);

	// NOTE: velocity is in worldspace, angularVelocity is relative to the object's 
	// local axes (just like pev->velocity, pev->avelocity)
	void GetVelocity(out Vector3 velocity, out Vector3 angularVelocity);

	// NOTE: These are velocities, not forces.  i.e. They will have the same effect regardless of
	// the object's mass or inertia
	void AddVelocity(in Vector3 velocity, in Vector3 angularVelocity);
	// gets a velocity in the object's local frame of reference at a specific point
	void GetVelocityAtPoint(in Vector3 worldPosition, out Vector3 velocity);
	// gets the velocity actually moved by the object in the last simulation update
	void GetImplicitVelocity(out Vector3 velocity, out Vector3 angularVelocity);
	// NOTE:	These are here for convenience, but you can do them yourself by using the matrix
	//			returned from GetPositionMatrix()
	// convenient coordinate system transformations (params - dest, src)
	void LocalToWorld(out Vector3 worldPosition, in Vector3 localPosition);
	void WorldToLocal(out Vector3 localPosition, in Vector3 worldPosition);

	// transforms a vector (no translation) from object-local to world space
	void LocalToWorldVector(out Vector3 worldVector, in Vector3 localVector);
	// transforms a vector (no translation) from world to object-local space
	void WorldToLocalVector(out Vector3 localVector, in Vector3 worldVector);

	// push on an object
	// force vector is direction & magnitude of impulse kg in / s
	void ApplyForceCenter(in Vector3 forceVector);
	void ApplyForceOffset(in Vector3 forceVector, in Vector3 worldPosition);
	// apply torque impulse.  This will change the angular velocity on the object.
	// HL Axes, kg degrees / s
	void ApplyTorqueCenter(in Vector3 torque);

	// Calculates the force/torque on the center of mass for an offset force impulse (pass output to ApplyForceCenter / ApplyTorqueCenter)
	void CalculateForceOffset(in Vector3 forceVector, in Vector3 worldPosition, out Vector3 centerForce, out Vector3 centerTorque);
	// Calculates the linear/angular velocities on the center of mass for an offset force impulse (pass output to AddVelocity)
	void CalculateVelocityOffset(in Vector3 forceVector, in Vector3 worldPosition, out Vector3 centerVelocity, out Vector3 centerAngularVelocity);
	// calculate drag scale
	float CalculateLinearDrag(in Vector3 unitDirection);
	float CalculateAngularDrag(in Vector3 objectSpaceRotationAxis);

	// returns true if the object is in contact with another object
	// if true, puts a point on the contact surface in contactPoint, and
	// a pointer to the object in contactObject
	// NOTE: You can pass NULL for either to avoid computations
	// BUGBUG: Use CreateFrictionSnapshot instead of this - this is a simple hack
	bool GetContactPoint(out Vector3 contactPoint, IPhysicsObject contactObject);

	// refactor this a bit - move some of this to IPhysicsShadowController
	void SetShadow(float maxSpeed, float maxAngularSpeed, bool allowPhysicsMovement, bool allowPhysicsRotation);
	void UpdateShadow(in Vector3 targetPosition, in QAngle targetAngles, bool tempDisableGravity, float timeOffset);

	// returns number of ticks since last Update() call
	int GetShadowPosition(out Vector3 position, out QAngle angles);
	IPhysicsShadowController GetShadowController();
	void RemoveShadowController();
	// applies the math of the shadow controller to this object.
	// for use in your own controllers
	// returns the new value of secondsToArrival with dt time elapsed
	float ComputeShadowControl(in HLShadowControlParams parms, TimeUnit_t secondsToArrival, TimeUnit_t dt);

	PhysCollide GetCollide();
	ReadOnlySpan<char> GetName();

	void BecomeTrigger();
	void RemoveTrigger();

	// sets the object to be hinged.  Fixed it place, but able to rotate around one axis.
	void BecomeHinged(int localAxis);
	// resets the object to original state
	void RemoveHinged();

	// used to iterate the contact points of an object
	IPhysicsFrictionSnapshot CreateFrictionSnapshot();
	void DestroyFrictionSnapshot(IPhysicsFrictionSnapshot snapshot);

	// dumps info about the object to Msg()
	void OutputDebugInfo();
}
public interface IPhysicsSpring
{
	void GetEndpoints(out Vector3 worldPositionStart, out Vector3 worldPositionEnd);
	void SetSpringConstant(float springContant);
	void SetSpringDamping(float springDamping);
	void SetSpringLength(float springLenght);

	// Get the starting object
	IPhysicsObject GetStartObject();

	// Get the end object
	IPhysicsObject GetEndObject();

}

public struct SurfacePhysicsParams
{
	public float Friction;
	public float Elasticity;
	public float Density;
	public float Thickness;
	public float Dampening;
}

public struct SurfaceAudioParams
{
	public float Reflectivity;
	public float HardnessFactor;
	public float RoughnessFactor;
	public float RoughThreshold;
	public float HardThreshold;
	public float HardVelocityThreshold;
}

public struct SurfaceSoundNames
{
	public UtlSymId_t StepLeft;
	public UtlSymId_t StepRight;
	public UtlSymId_t ImpactSoft;
	public UtlSymId_t ImpactHard;
	public UtlSymId_t ScrapeSmooth;
	public UtlSymId_t ScrapeRough;
	public UtlSymId_t BulletImpact;
	public UtlSymId_t Rolling;
	public UtlSymId_t BreakSound;
	public UtlSymId_t StrainSound;
}

public struct SurfaceGameProps
{
	public float MaxSpeedFactor;
	public float JumpFactor;
	public UtlSymId_t Material;
	public byte Climbable;
	public byte Pad;
}

public struct SurfaceSoundHandles
{
	public short StepLeft;
	public short StepRight;
	public short ImpactSoft;
	public short ImpactHard;
	public short ScrapeSmooth;
	public short ScrapeRough;
	public short BulletImpact;
	public short Rolling;
	public short BreakSound;
	public short StrainSound;
}

public struct SurfaceData
{
	public SurfacePhysicsParams Physics;
	public SurfaceAudioParams Audio;
	public SurfaceSoundNames Sounds;
	public SurfaceGameProps Game;
	public SurfaceSoundHandles SoundHandles;
}


public interface IPhysicsSurfaceProps
{
	nint ParseSurfaceData(ReadOnlySpan<char> filename, ReadOnlySpan<char> textfile);
	// current number of entries in the database
	nint SurfacePropCount();

	nint GetSurfaceIndex(ReadOnlySpan<char> surfacePropName);
	void GetPhysicsProperties(nint surfaceDataIndex, out float density, out float thickness, out float friction, out float elasticity);

	SurfaceData_ptr? GetSurfaceData(nint surfaceDataIndex);
	ReadOnlySpan<char> GetString(UtlSymId_t stringTableIndex);


	ReadOnlySpan<char> GetPropName(nint surfaceDataIndex);

	// sets the global index table for world materials
	// UNDONE: Make this per-PhysCollide
	void SetWorldMaterialIndexTable(Span<nint> mapArray);

	// NOTE: Same as GetPhysicsProperties, but maybe more convenient
	void GetPhysicsParameters(nint surfaceDataIndex, out SurfacePhysicsParams paramsOut);
}

public interface IPhysicsFluidController
{
	void SetGameData(object? gameData);
	object? GetGameData();

	void GetSurfacePlane(out Vector3 normal, out float dist);
	float GetDensity();
	void WakeAllSleepingObjects();
	int GetContents();
}

public struct ConvertConvexParams
{

}

public struct FluidParams
{
	public Vector4 SurfacePlane;        // x,y,z normal, dist (plane constant) fluid surface
	public Vector3 CurrentVelocity;     // velocity of the current in inches/second
	public float Damping;               // damping factor for buoyancy (tweak)
	public float TorqueFactor;
	public float ViscosityFactor;
	public object? GameData;
	public bool UseAerodynamics;        // true if this controller should calculate surface pressure
	public Contents Contents;
}

public struct SpringParams
{
	public float Constant;          // spring constant
	public float NaturalLength;     // relaxed length
	public float Damping;           // damping factor
	public float RelativeDamping;   // relative damping (damping proportional to the change in the relative position of the objects)
	public Vector3 StartPosition;
	public Vector3 EndPosition;
	public bool UseLocalPositions;  // start & end Position are in local space to start and end objects if this is true
	public bool OnlyStretch;        // only apply forces when the length is greater than the natural length
}

public delegate ref Vector3 GetMassCenterOverrideFn();
public struct ObjectParams
{
	public GetMassCenterOverrideFn? MassCenterOverrideFn;
	public readonly ref Vector3 MassCenterOverride => ref MassCenterOverrideFn!();
	public float Mass;
	public float Inertia;
	public float Damping;
	public float RotDamping;
	public float RotInertiaLimit;
	public string? Name;
	public object? GameData;
	public float Volume;
	public float DragCoefficient;
	public bool EnableCollisions;
}

public struct PhysSaveParams
{
	public ISave Save;
	public object? Object;
	public PhysInterfaceId Type;
}

public struct PhysRestoreParams
{
	public IRestore Restore;
	public object? Object;
	public PhysInterfaceId Type;
	public object GameData;
	public PhysCollide CollisionModel;
	public IPhysicsEnvironment Environment;
	public IPhysicsGameTrace GameTrace;
}

public struct PhysRecreateParams
{
	public object? OldObject;
	public object? NewObject;
}

public struct PhysPreRestoreParams
{
	public int RecreatedObjectCount;
	InlineArray1<PhysRecreateParams> RecreatedObjectList;
}


public class SurfaceData_ptr : ReusableBox<SurfaceData>
{
	public ref SurfacePhysicsParams Physics => ref Struct.Physics;
	public ref SurfaceAudioParams Audio => ref Struct.Audio;
	public ref SurfaceSoundNames Sounds => ref Struct.Sounds;
	public ref SurfaceGameProps Game => ref Struct.Game;
	public ref SurfaceSoundHandles SoundHandles => ref Struct.SoundHandles;
}
