using Box3D;

using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;
using System.Runtime.InteropServices;


namespace Source.Physics;

internal unsafe class PhysicsObject : IPhysicsObject
{
	static ulong NextUniqueId = 1;

	static readonly ConVar vbox_inertia_scale = new("vbox_inertia_scale", "1", 0, "Max multiple of Box3D's native inertia a SetInertia may apply.", 0.0, 1000.0);

	public object? GameData;
	string Name = "NoName";

	PhysicsFlags GameFlags;
	ushort GameIndex;
	CallbackFlags Callbacks = CallbackFlags.GlobalCollision | CallbackFlags.GlobalFriction | CallbackFlags.FluidTouch | CallbackFlags.GlobalTouch | CallbackFlags.GlobalCollideStatic | CallbackFlags.DoFluidSimulation;

	readonly bool Static;
	bool MotionEnabled = true;
	bool GravityEnabled = true;
	bool CollisionEnabled = true;
	bool DragEnabled;

	int MaterialIndex;
	uint ContentsMask = (uint)Contents.Solid;
	float SphereRadius;
	float Volume;
	float MaterialDensity;
	float BuoyancyRatio = 1.0f;
	bool Trigger;

	float LinearDamping;
	float AngularDamping;
	float DragCoefficient;
	float AngularDragCoefficient;
	Vector3 DragBasis;
	Vector3 AngDragBasis;

	float CachedMass;
	float CachedInvMass;

	readonly BoxPhysCollide? Collide;

	Vector3 PreStepVelocity;
	Vector3 LocalMassCenter;
	bool LastAwake;

	public Body BodyId;
	public readonly PhysicsEnvironment Env;
	GCHandle Handle;

	IPhysicsShadowController? ShadowController;

	public bool HasTouchedDynamic;

	public float LastCollisionTime = -1000.0f;
	public ulong LastCollisionPartnerId;
	public readonly ulong UniqueId;
	public uint RulesEpoch = 1;
	readonly Dictionary<ulong, ulong> CollisionCache = [];

	public PhysicsObject(Body bodyId, PhysicsEnvironment environment, bool isStatic, int materialIndex, PhysCollide? collide, in ObjectParams objParams, bool hasParams) {
		Static = isStatic;
		MaterialIndex = materialIndex;
		Collide = collide as BoxPhysCollide;
		BodyId = bodyId;
		Env = environment;

		Handle = GCHandle.Alloc(this, GCHandleType.Normal);
		bodyId.UserData = GCHandle.ToIntPtr(Handle);
		UniqueId = NextUniqueId++;

		if (hasParams) {
			GameData = objParams.GameData;
			if (objParams.Name != null)
				Name = objParams.Name;

			LinearDamping = objParams.Damping;
			AngularDamping = objParams.RotDamping;
			Volume = objParams.Volume;
			DragCoefficient = objParams.DragCoefficient;
			AngularDragCoefficient = objParams.DragCoefficient;
			DragEnabled = objParams.DragCoefficient != 0.0f;
			if (!isStatic) {
				bodyId.LinearDamping = objParams.Damping;
				bodyId.AngularDamping = objParams.RotDamping;
			}

			if (objParams.Mass > 0.0f)
				SetMass(objParams.Mass);
		}

		if (CachedMass <= 0.0f) {
			CachedMass = isStatic ? 0.0f : bodyId.Mass;
			CachedInvMass = isStatic ? 0.0f : bodyId.InverseMass;
		}

		SurfaceData_ptr? surface = physprops.GetSurfaceData(MaterialIndex);
		if (surface != null)
			MaterialDensity = surface.Physics.Density;
		CalculateBuoyancy();
		RecomputeDragBases();
	}

	public static PhysicsObject? FromUserData(nint userData) {
		if (userData == 0)
			return null;
		return GCHandle.FromIntPtr(userData).Target as PhysicsObject;
	}

	internal void ReleaseHandle() {
		if (Handle.IsAllocated)
			Handle.Free();
	}

	static bool IsFinite(in Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

	void ForEachShape(Action<Shape> fn) {
		int count = BodyId.ShapeCount;
		if (count <= 0)
			return;
		Shape[] shapes = new Shape[count];
		BodyId.GetShapes(shapes);
		foreach (Shape shape in shapes)
			fn(shape);
	}

	static float AngDragIntegral(float invInertia, float l, float w, float h) {
		float w2 = w * w, l2 = l * l, h2 = h * h;
		return invInertia * ((1.0f / 3.0f) * w2 * l * l2 + 0.5f * w2 * w2 * l + l * w2 * h2);
	}

	void CalculateBuoyancy() {
		if (Volume > 0.0f && MaterialDensity > 0.0f) {
			float volume = SourceToBox.Volume(MathF.Max(Volume, 5.0f));
			float actualDensity = CachedMass / volume;
			BuoyancyRatio = actualDensity / MaterialDensity;
		}
		else
			BuoyancyRatio = 1.0f;
	}

	public bool IsStatic() => Static;
	public bool IsAsleep() => Static || !BodyId.IsAwake;
	public bool IsTrigger() => Trigger;
	public bool IsFluid() => false;
	public bool IsHinged() => false;
	public bool IsCollisionEnabled() => CollisionEnabled;
	public bool IsGravityEnabled() => !Static && GravityEnabled;
	public bool IsDragEnabled() => DragEnabled;
	public bool IsMotionEnabled() => !Static && MotionEnabled;
	public bool IsMoveable() => IsMotionEnabled();
	public bool IsAttachedToConstraint(bool externalOnly) => false;

	public void EnableCollisions(bool enable) {
		if (CollisionEnabled == enable)
			return;

		CollisionEnabled = enable;

		if (!BodyId.IsValid)
			return;

		Filter filter = Filter.Default;
		if (!enable)
			filter.maskBits = 0;

		ForEachShape(shape => shape.SetFilter(filter, true));
	}

	public void EnableGravity(bool enable) {
		GravityEnabled = enable;
		if (!Static)
			BodyId.GravityScale = enable ? 1.0f : 0.0f;
	}

	public void EnableDrag(bool enable) => DragEnabled = enable;

	public void EnableMotion(bool enable) {
		if (Static || MotionEnabled == enable)
			return;

		MotionEnabled = enable;
		BodyId.Type = enable ? BodyType.Dynamic : BodyType.Static;
		if (enable) {
			BodyId.ApplyMassFromShapes();
			SetMass(CachedMass);
			if (LocalMassCenter != Vector3.Zero) {
				MassData massData = BodyId.MassData;
				massData.center = SourceToBox.Distance(LocalMassCenter);
				BodyId.MassData = massData;
			}
			BodyId.IsAwake = true;
		}
	}

	public void SetGameData(object? gameData) => GameData = gameData;
	public object? GetGameData() => GameData;
	public void SetGameFlags(PhysicsFlags userFlags) => GameFlags = userFlags;
	public PhysicsFlags GetGameFlags() => GameFlags;
	public void SetGameIndex(ushort gameIndex) => GameIndex = gameIndex;
	public ushort GetGameIndex() => GameIndex;
	public void SetCallbackFlags(CallbackFlags callbackflags) => Callbacks = callbackflags;
	public CallbackFlags GetCallbackFlags() => Callbacks;

	public void Wake() {
		if (!Static)
			BodyId.IsAwake = true;
	}

	public void Sleep() {
		if (!Static)
			BodyId.IsAwake = false;
	}

	public void RecheckCollisionFilter() {
		++RulesEpoch;
		CollisionCache.Clear();
	}

	public void RecheckContactPoints() { }

	public bool TryGetCachedCollision(ulong partnerId, uint partnerEpoch, out bool collide) {
		collide = false;
		if (!CollisionCache.TryGetValue(partnerId, out ulong value) || (uint)(value >> 1) != partnerEpoch)
			return false;
		collide = (value & 1ul) != 0;
		return true;
	}

	public void CacheCollision(ulong partnerId, uint partnerEpoch, bool collide) {
		if (CollisionCache.Count > 4096)
			CollisionCache.Clear();
		CollisionCache[partnerId] = ((ulong)partnerEpoch << 1) | (collide ? 1ul : 0ul);
	}

	public void SetMass(float mass) {
		mass = Math.Clamp(mass, 1.0f, PhysicsConstants.VPHYSICS_MAX_MASS);
		CachedMass = mass;
		CachedInvMass = 1.0f / mass;
		CalculateBuoyancy();

		if (Static)
			return;

		MassData massData = BodyId.MassData;
		float scale = massData.mass > 0.0f ? mass / massData.mass : 1.0f;
		massData.mass = mass;
		massData.inertia.cx = SourceToBox.Unitless(BoxToSource.Unitless(massData.inertia.cx) * scale);
		massData.inertia.cy = SourceToBox.Unitless(BoxToSource.Unitless(massData.inertia.cy) * scale);
		massData.inertia.cz = SourceToBox.Unitless(BoxToSource.Unitless(massData.inertia.cz) * scale);
		BodyId.MassData = massData;
		RecomputeDragBases();
	}

	public float GetMass() => CachedMass;
	public float GetInvMass() => CachedInvMass;

	public Vector3 GetInertia() {
		Matrix3 inertia = BodyId.LocalRotationalInertia;
		return new Vector3(MathF.Abs(inertia.cx.X), MathF.Abs(inertia.cy.Y), MathF.Abs(inertia.cz.Z));
	}

	public Vector3 GetInvInertia() {
		Vector3 inertia = GetInertia();
		return new Vector3(inertia.X > 0.0f ? 1.0f / inertia.X : 0.0f, inertia.Y > 0.0f ? 1.0f / inertia.Y : 0.0f, inertia.Z > 0.0f ? 1.0f / inertia.Z : 0.0f);
	}

	public void SetInertia(in Vector3 inertia) {
		if (Static)
			return;

		float scale = vbox_inertia_scale.GetFloat();
		MassData massData = BodyId.MassData;
		float capX = MathF.Abs(massData.inertia.cx.X) * scale;
		float capY = MathF.Abs(massData.inertia.cy.Y) * scale;
		float capZ = MathF.Abs(massData.inertia.cz.Z) * scale;
		massData.inertia = default;
		massData.inertia.cx.X = MathF.Min(MathF.Abs(inertia.X), capX);
		massData.inertia.cy.Y = MathF.Min(MathF.Abs(inertia.Y), capY);
		massData.inertia.cz.Z = MathF.Min(MathF.Abs(inertia.Z), capZ);
		BodyId.MassData = massData;
		RecomputeDragBases();
	}

	public void SetDamping(ref float speed, ref float rot) {
		LinearDamping = speed;
		AngularDamping = rot;
		if (!Static) {
			BodyId.LinearDamping = speed;
			BodyId.AngularDamping = rot;
		}
	}

	public void GetDamping(out float speed, out float rot) {
		speed = LinearDamping;
		rot = AngularDamping;
	}

	public void SetDragCoefficient(ref float drag, ref float angularDrag) {
		DragCoefficient = drag;
		AngularDragCoefficient = angularDrag;
		DragEnabled = DragCoefficient != 0.0f || AngularDragCoefficient != 0.0f;
		RecomputeDragBases();
	}

	public void SetBuoyancyRatio(float ratio) => BuoyancyRatio = ratio;
	public float GetBuoyancyRatio() => BuoyancyRatio;

	void RecomputeDragBases() {
		DragBasis = default;
		AngDragBasis = default;
		if (Static || Collide == null)
			return;

		physcollision.CollideGetAABB(out Vector3 mins, out Vector3 maxs, Collide, default, default);
		Vector3 areaFractions = physcollision.CollideGetOrthographicAreas(Collide);

		Vector3 delta = maxs - mins;
		delta = new Vector3(MathF.Abs(SourceToBox.Distance(delta.X)), MathF.Abs(SourceToBox.Distance(delta.Y)), MathF.Abs(SourceToBox.Distance(delta.Z)));

		DragBasis.X = delta.Y * delta.Z * areaFractions.X;
		DragBasis.Y = delta.X * delta.Z * areaFractions.Y;
		DragBasis.Z = delta.X * delta.Y * areaFractions.Z;
		DragBasis *= GetInvMass();

		Vector3 invInertia = GetInvInertia();
		delta *= 0.5f;
		AngDragBasis.X = areaFractions.Z * AngDragIntegral(invInertia.X, delta.X, delta.Y, delta.Z) + areaFractions.Y * AngDragIntegral(invInertia.X, delta.X, delta.Z, delta.Y);
		AngDragBasis.Y = areaFractions.Z * AngDragIntegral(invInertia.Y, delta.Y, delta.X, delta.Z) + areaFractions.X * AngDragIntegral(invInertia.Y, delta.Y, delta.Z, delta.X);
		AngDragBasis.Z = areaFractions.Y * AngDragIntegral(invInertia.Z, delta.Z, delta.X, delta.Y) + areaFractions.X * AngDragIntegral(invInertia.Z, delta.Z, delta.Y, delta.X);
	}

	public void ApplyAirDrag(float airDensity, float dt) {
		if (!DragEnabled || Static)
			return;

		Vector3 vWorld = BodyId.LinearVelocity;
		Vector3 vLocal = BoxToSource.Unitless(BodyId.GetLocalVector(vWorld));
		float drag = DragCoefficient * (MathF.Abs(vLocal.X * DragBasis.X) + MathF.Abs(vLocal.Y * DragBasis.Y) + MathF.Abs(vLocal.Z * DragBasis.Z));
		float dragForce = -0.5f * drag * airDensity * dt;
		if (dragForce < 0.0f)
			BodyId.LinearVelocity = SourceToBox.Unitless(BoxToSource.Unitless(vWorld) * (1.0f + MathF.Max(dragForce, -1.0f)));

		Vector3 wWorld = BodyId.AngularVelocity;
		Vector3 wLocal = BoxToSource.Unitless(BodyId.GetLocalVector(wWorld));
		float angDrag = AngularDragCoefficient * (MathF.Abs(wLocal.X * AngDragBasis.X) + MathF.Abs(wLocal.Y * AngDragBasis.Y) + MathF.Abs(wLocal.Z * AngDragBasis.Z));
		float angDragForce = -angDrag * airDensity * dt;
		if (angDragForce < 0.0f)
			BodyId.AngularVelocity = SourceToBox.Unitless(BoxToSource.Unitless(wWorld) * (1.0f + MathF.Max(angDragForce, -1.0f)));
	}

	public int GetMaterialIndex() => MaterialIndex;

	public void SetMaterialIndex(int materialIndex) {
		if (MaterialIndex == materialIndex)
			return;

		MaterialIndex = materialIndex;

		SurfaceData_ptr? surface = physprops.GetSurfaceData(materialIndex);
		if (surface != null) {
			MaterialDensity = surface.Physics.Density;
			CalculateBuoyancy();
		}
		if (surface == null || !BodyId.IsValid)
			return;

		float friction = MathF.Max(surface.Physics.Friction, 0.0f);
		float restitution = MathF.Max(surface.Physics.Elasticity, 0.0f);
		ForEachShape(shape => {
			shape.Friction = friction;
			shape.Restitution = restitution;
		});

		ShadowController?.ObjectMaterialChanged(materialIndex);
	}

	public uint GetContents() => ContentsMask;
	public void SetContents(uint contents) => ContentsMask = contents;

	public float GetSphereRadius() => SphereRadius;
	public void SetSphereRadius(float radius) => SphereRadius = radius;

	public float GetEnergy() {
		if (Static)
			return 0.0f;

		Vector3 v = BoxToSource.Unitless(BodyId.LinearVelocity);
		Vector3 w = BoxToSource.Unitless(BoxMath.InvRotateVector(BodyId.Transform.q, BodyId.AngularVelocity));
		MassData massData = BodyId.MassData;

		Vector3 cx = BoxToSource.Unitless(massData.inertia.cx), cy = BoxToSource.Unitless(massData.inertia.cy), cz = BoxToSource.Unitless(massData.inertia.cz);
		Vector3 iw = cx * w.X + cy * w.Y + cz * w.Z;

		return BoxToSource.Energy(0.5f * massData.mass * Vector3.Dot(v, v) + 0.5f * Vector3.Dot(w, iw));
	}

	public Vector3 GetMassCenterLocalSpace() => BoxToSource.Distance(BodyId.LocalCenter);

	public void SetLocalMassCenter(in Vector3 massCenter) => LocalMassCenter = massCenter;

	public void SetPosition(in Vector3 worldPosition, in QAngle angles, bool isTeleport) => BodyId.SetTransform(SourceToBox.Distance(worldPosition), SourceToBox.Angle(angles));

	public void SetPositionMatrix(in Matrix3x4 matrix, bool isTeleport) {
		Transform xf = SourceToBox.Transform(matrix);
		BodyId.SetTransform(xf.p, xf.q);
	}

	public void GetPosition(out Vector3 worldPosition, out QAngle angles) {
		Transform xf = BodyId.Transform;
		worldPosition = BoxToSource.Distance(xf.p);
		angles = BoxToSource.Angle(xf.q);
	}

	public void GetPositionMatrix(out Matrix3x4 positionMatrix) => positionMatrix = BoxToSource.Matrix(BodyId.Transform);

	public void SetVelocity(in Vector3 velocity, in Vector3 angularVelocity) {
		if (Static)
			return;

		bool vel = IsFinite(velocity);
		bool ang = IsFinite(angularVelocity);
		if (vel)
			BodyId.LinearVelocity = SourceToBox.Distance(velocity);
		if (ang) {
			LocalToWorldVector(out Vector3 worldAngular, angularVelocity);
			BodyId.AngularVelocity = SourceToBox.AngularImpulse(worldAngular);
		}
		if (vel || ang)
			BodyId.IsAwake = true;
	}

	public void SetVelocityInstantaneous(in Vector3 velocity, in Vector3 angularVelocity) => SetVelocity(velocity, angularVelocity);

	public void GetVelocity(out Vector3 velocity, out Vector3 angularVelocity) {
		velocity = BoxToSource.Distance(BodyId.LinearVelocity);
		if (!IsFinite(velocity))
			velocity = default;

		float maxAngular = Env.GetMaxAngularVelocity();
		Vector3 w = BoxToSource.Unitless(BodyId.AngularVelocity);
		float len = w.Length();
		if (len > maxAngular) {
			w *= maxAngular / len;
			if (!Static)
				BodyId.AngularVelocity = SourceToBox.Unitless(w);
		}
		WorldToLocalVector(out angularVelocity, BoxToSource.AngularImpulse(SourceToBox.Unitless(w)));
		if (!IsFinite(angularVelocity))
			angularVelocity = default;
	}

	public void SnapshotPreStepVelocity() => PreStepVelocity = Static ? default : BoxToSource.Distance(BodyId.LinearVelocity);
	public Vector3 GetPreStepVelocity() => PreStepVelocity;

	public Vector3 FakeVelocity(in Vector3 velocity) {
		Vector3 old = BoxToSource.Distance(BodyId.LinearVelocity);
		if (!Static)
			BodyId.LinearVelocity = SourceToBox.Distance(velocity);
		return old;
	}

	public void RestoreVelocity(in Vector3 velocity) {
		if (!Static)
			BodyId.LinearVelocity = SourceToBox.Distance(velocity);
	}

	public bool WasAwakeLastStep() => LastAwake;
	public void SetAwakeLastStep(bool awake) => LastAwake = awake;

	public void AddVelocity(in Vector3 velocity, in Vector3 angularVelocity) {
		if (Static)
			return;

		BodyId.LinearVelocity = SourceToBox.Unitless(BoxToSource.Unitless(BodyId.LinearVelocity) + BoxToSource.Unitless(SourceToBox.Distance(velocity)));
		LocalToWorldVector(out Vector3 worldAngular, angularVelocity);
		BodyId.AngularVelocity = SourceToBox.Unitless(BoxToSource.Unitless(BodyId.AngularVelocity) + BoxToSource.Unitless(SourceToBox.AngularImpulse(worldAngular)));
		BodyId.IsAwake = true;
	}

	public void GetVelocityAtPoint(in Vector3 worldPosition, out Vector3 velocity) => velocity = BoxToSource.Distance(BodyId.GetWorldPointVelocity(SourceToBox.Distance(worldPosition)));

	public void GetImplicitVelocity(out Vector3 velocity, out Vector3 angularVelocity) => GetVelocity(out velocity, out angularVelocity);

	public void LocalToWorld(out Vector3 worldPosition, in Vector3 localPosition) => worldPosition = BoxToSource.Distance(BodyId.GetWorldPoint(SourceToBox.Distance(localPosition)));
	public void WorldToLocal(out Vector3 localPosition, in Vector3 worldPosition) => localPosition = BoxToSource.Distance(BodyId.GetLocalPoint(SourceToBox.Distance(worldPosition)));
	public void LocalToWorldVector(out Vector3 worldVector, in Vector3 localVector) => worldVector = BoxToSource.Unitless(BodyId.GetWorldVector(SourceToBox.Unitless(localVector)));
	public void WorldToLocalVector(out Vector3 localVector, in Vector3 worldVector) => localVector = BoxToSource.Unitless(BodyId.GetLocalVector(SourceToBox.Unitless(worldVector)));

	public void ApplyForceCenter(in Vector3 forceVector) {
		if (!Static)
			BodyId.ApplyLinearImpulseToCenter(SourceToBox.Distance(forceVector), true);
	}

	public void ApplyForceOffset(in Vector3 forceVector, in Vector3 worldPosition) {
		if (!Static)
			BodyId.ApplyLinearImpulse(SourceToBox.Distance(forceVector), SourceToBox.Distance(worldPosition), true);
	}

	public void ApplyTorqueCenter(in Vector3 torque) {
		if (!Static)
			BodyId.ApplyAngularImpulse(SourceToBox.AngularImpulse(torque), true);
	}

	public void CalculateForceOffset(in Vector3 forceVector, in Vector3 worldPosition, out Vector3 centerForce, out Vector3 centerTorque) {
		centerForce = forceVector;
		Vector3 pos = BoxToSource.Unitless(SourceToBox.Distance(worldPosition));
		Vector3 cross = Vector3.Cross(pos - BoxToSource.Unitless(BodyId.WorldCenter), BoxToSource.Unitless(SourceToBox.Distance(forceVector)));
		WorldToLocalVector(out centerTorque, BoxToSource.AngularImpulse(SourceToBox.Unitless(cross)));
	}

	public void CalculateVelocityOffset(in Vector3 forceVector, in Vector3 worldPosition, out Vector3 centerVelocity, out Vector3 centerAngularVelocity) {
		centerVelocity = forceVector * GetInvMass();
		CalculateForceOffset(forceVector, worldPosition, out _, out Vector3 centerTorque);
		Vector3 invInertia = GetInvInertia();
		centerAngularVelocity = new Vector3(centerTorque.X * invInertia.X, centerTorque.Y * invInertia.Y, centerTorque.Z * invInertia.Z);
	}

	public float CalculateLinearDrag(in Vector3 unitDirection) => 0.0f;
	public float CalculateAngularDrag(in Vector3 objectSpaceRotationAxis) => 0.0f;

	public bool GetContactPoint(out Vector3 contactPoint, IPhysicsObject contactObject) {
		contactPoint = default;
		return false;
	}

	public void SetShadow(float maxSpeed, float maxAngularSpeed, bool allowPhysicsMovement, bool allowPhysicsRotation) {
		ShadowController ??= Env.CreateShadowController(this, allowPhysicsMovement, allowPhysicsRotation);
		ShadowController.MaxSpeed(maxSpeed, maxAngularSpeed);
	}

	public void UpdateShadow(in Vector3 targetPosition, in QAngle targetAngles, bool tempDisableGravity, float timeOffset) => ShadowController?.Update(targetPosition, targetAngles, timeOffset);

	public int GetShadowPosition(out Vector3 position, out QAngle angles) {
		GetPosition(out position, out angles);
		return 1;
	}

	public IPhysicsShadowController GetShadowController() => ShadowController!;
	public bool HasShadowController() => ShadowController != null;

	public void RemoveShadowController() {
		if (ShadowController != null) {
			Env.DestroyShadowController(ShadowController);
			ShadowController = null;
		}
	}

	static void ShadowComputeVelocity(ref Vector3 velocity, in Vector3 delta, float maxSpeed, float maxDampSpeed, float scaleDelta, float damping) {
		if (velocity.LengthSquared() < 1e-6f)
			velocity = default;
		else if (maxDampSpeed > 0.0f) {
			Vector3 dampen = velocity * -damping;
			float speed = velocity.Length() * MathF.Abs(damping);
			if (speed > maxDampSpeed)
				dampen *= maxDampSpeed / speed;
			velocity += dampen;
		}

		if (maxSpeed > 0.0f) {
			Vector3 accel = delta * scaleDelta;
			float speed = delta.Length() * scaleDelta;
			if (speed > maxSpeed)
				accel *= maxSpeed / speed;
			velocity += accel;
		}
	}

	static Vector3 ShadowRotationDeltaDegrees(in QAngle current, in QAngle target) {
		MathLib.AngleQuaternion(current, out Quaternion qCur);
		MathLib.AngleQuaternion(target, out Quaternion qTarget);

		Quaternion qDelta = Quaternion.Multiply(qTarget, Quaternion.Inverse(qCur));
		qDelta.W = Math.Clamp(qDelta.W, -1.0f, 1.0f);

		float angle = 2.0f * MathF.Acos(qDelta.W);
		if (angle > MathF.PI)
			angle -= 2.0f * MathF.PI;
		float sinHalf = MathF.Sqrt(MathF.Max(1.0f - qDelta.W * qDelta.W, 0.0f));
		Vector3 axis = sinHalf > 1e-6f ? new Vector3(qDelta.X, qDelta.Y, qDelta.Z) / sinHalf : default;
		return axis * MathLib.RAD2DEG(angle);
	}

	void ClampShadowVelocityAgainstContacts(ref Vector3 velocity) {
		if (velocity == Vector3.Zero)
			return;

		Span<ContactData> contacts = stackalloc ContactData[16];
		int count = BodyId.GetContactData(contacts);
		for (int i = 0; i < count; i++) {
			PhysicsObject? a = FromUserData(contacts[i].shapeIdA.Body.UserData);
			bool selfIsA = a == this;
			PhysicsObject? other = selfIsA ? FromUserData(contacts[i].shapeIdB.Body.UserData) : a;
			if (other == null || !other.IsStatic())
				continue;

			foreach (ref readonly Manifold manifold in contacts[i].manifolds) {
				if (manifold.pointCount <= 0)
					continue;

				Vector3 normal = BoxToSource.Unitless(manifold.normal);
				if (!selfIsA)
					normal = -normal;

				float into = Vector3.Dot(velocity, normal);
				if (into > 0.0f)
					velocity -= normal * into;
			}
		}
	}

	public float ComputeShadowControl(in HLShadowControlParams parms, TimeUnit_t secondsToArrival, TimeUnit_t dt) {
		Vector3 lastPosition = default;
		return ComputeShadowControlEx(parms, (float)secondsToArrival, (float)dt, ref lastPosition, false);
	}

	public float ComputeShadowControlEx(in HLShadowControlParams parms, float secondsToArrival, float deltaTime, ref Vector3 lastPosition, bool trackLastPosition) {
		GetPosition(out Vector3 position, out QAngle angles);

		Vector3 linearVelocity = BoxToSource.Distance(BodyId.LinearVelocity);
		Vector3 angularVelocity = BoxToSource.AngularImpulse(BodyId.AngularVelocity);

		float fraction = secondsToArrival > 0.0f ? MathF.Min(deltaTime / secondsToArrival, 1.0f) : 1.0f;
		secondsToArrival = MathF.Max(secondsToArrival - deltaTime, 0.0f);

		if (fraction <= 0.0f)
			return secondsToArrival;

		Vector3 deltaPosition = parms.TargetPosition - position;

		Vector3 error = deltaPosition;
		if (trackLastPosition && lastPosition != Vector3.Zero)
			error = position - lastPosition;

		bool teleport = false;
		if (parms.TeleportDistance > 0.0f && error.LengthSquared() > parms.TeleportDistance * parms.TeleportDistance) {
			position = parms.TargetPosition;
			angles = parms.TargetRotation;
			deltaPosition = default;
			teleport = true;
		}

		float fractionTime = fraction / deltaTime;

		ShadowComputeVelocity(ref linearVelocity, deltaPosition, parms.MaxSpeed, parms.MaxDampSpeed, fractionTime, parms.DampFactor);

		Vector3 deltaAngles = ShadowRotationDeltaDegrees(angles, parms.TargetRotation);
		ShadowComputeVelocity(ref angularVelocity, deltaAngles, parms.MaxAngular, parms.MaxDampAngular, fractionTime, parms.DampFactor);

		if (teleport) {
			if (IsCollisionEnabled()) {
				EnableCollisions(false);
				SetPosition(position, angles, true);
				EnableCollisions(true);
			}
			else
				SetPosition(position, angles, true);
		}

		if (!Static) {
			ClampShadowVelocityAgainstContacts(ref linearVelocity);
			BodyId.LinearVelocity = SourceToBox.Distance(linearVelocity);
			BodyId.AngularVelocity = SourceToBox.AngularImpulse(angularVelocity);
			BodyId.IsAwake = true;
		}

		if (trackLastPosition)
			lastPosition = teleport ? default : position + linearVelocity * deltaTime;

		return secondsToArrival;
	}

	public PhysCollide GetCollide() => Collide!;
	public ReadOnlySpan<char> GetName() => Name;

	static string HullInfo(HullData hull) {
		if (hull.IsNull)
			return "none";

		ReadOnlySpan<Vector3> points = hull.Points;
		Vector3 mins = new(float.MaxValue), maxs = new(float.MinValue);
		foreach (Vector3 point in points) {
			mins = Vector3.Min(mins, point);
			maxs = Vector3.Max(maxs, point);
		}
		mins = BoxToSource.Distance(mins);
		maxs = BoxToSource.Distance(maxs);
		Vector3 size = maxs - mins;
		return $"{hull.VertexCount} verts, {hull.FaceCount} faces, mins ({mins.X:F2} {mins.Y:F2} {mins.Z:F2}) maxs ({maxs.X:F2} {maxs.Y:F2} {maxs.Z:F2}) size ({size.X:F2} {size.Y:F2} {size.Z:F2})";
	}

	internal void DumpCollide(int environmentIndex) {
		GetPosition(out Vector3 position, out QAngle angles);
		Vector3 velocity = BoxToSource.Distance(BodyId.LinearVelocity);
		Vector3 angularVelocity = BoxToSource.AngularImpulse(BodyId.AngularVelocity);
		Msg($"[env {environmentIndex}] \"{Name}\" static {Static}, asleep {IsAsleep()}, material {physprops.GetPropName(MaterialIndex)} ({MaterialIndex})\n");
		Msg($"  pos ({position.X:F2} {position.Y:F2} {position.Z:F2}) ang ({angles.X:F2} {angles.Y:F2} {angles.Z:F2})\n");
		Msg($"  vel ({velocity.X:F2} {velocity.Y:F2} {velocity.Z:F2}) angvel ({angularVelocity.X:F2} {angularVelocity.Y:F2} {angularVelocity.Z:F2})\n");

		if (!Static) {
			MassData massData = BodyId.MassData;
			Vector3 center = BoxToSource.Distance(massData.center);
			Matrix3 inertia = massData.inertia;
			Msg($"  mass {massData.mass:F3} (game {CachedMass:F3}), local mass center ({center.X:F3} {center.Y:F3} {center.Z:F3})\n");
			Msg($"  inertia ({inertia.cx.X:G4} {inertia.cx.Y:G4} {inertia.cx.Z:G4}) ({inertia.cy.X:G4} {inertia.cy.Y:G4} {inertia.cy.Z:G4}) ({inertia.cz.X:G4} {inertia.cz.Y:G4} {inertia.cz.Z:G4})\n");
		}

		if (Collide == null) {
			Msg(SphereRadius > 0.0f ? $"  sphere radius {SphereRadius:F2}\n" : "  no collide\n");
			return;
		}

		Vector3 collideCenter = Collide.MassCenter;
		Msg($"  collide: {Collide.Convexes.Count} convexes, mesh {(Collide.Mesh.IsNull ? "none" : $"{Collide.Mesh.Triangles.Length} tris")}, authored mass center ({collideCenter.X:F3} {collideCenter.Y:F3} {collideCenter.Z:F3})\n");
		for (int i = 0; i < Collide.Convexes.Count; i++) {
			BoxPhysConvex convex = Collide.Convexes[i];
			Msg($"  convex {i}: gamedata {convex.GameData}, ivp triangles {convex.QueryMaterials?.Length ?? 0}\n");
			Msg($"    hull     {HullInfo(convex.Hull)}\n");
			if (!Static)
				Msg($"    sim hull {HullInfo(convex.GetSimHull())}\n");
		}
	}

	internal static int ConvexMaterialIndex(BoxPhysConvex convex, int materialIndex) {
		byte[]? materials = convex.QueryMaterials;
		Vector3[]? verts = convex.QueryVerts;
		if (materials == null || verts == null)
			return materialIndex;

		Span<float> areas = stackalloc float[128];
		int best = 0;
		for (int i = 0; i < materials.Length; i++) {
			int material = materials[i];
			if (material == 0)
				continue;

			Vector3 a = verts[i * 3 + 0];
			areas[material] += Vector3.Cross(verts[i * 3 + 1] - a, verts[i * 3 + 2] - a).Length();
			if (best == 0 || areas[material] > areas[best])
				best = material;
		}

		return best == 0 ? materialIndex : (int)((PhysicsSurfaceProps)physprops).RemapWorldMaterial(best);
	}

	static int FillFaceMaterials(BoxPhysConvex convex, HullData hull, int materialIndex, Span<SurfaceMaterial> faceMaterials) {
		byte[]? materials = convex.QueryMaterials;
		Vector3[]? verts = convex.QueryVerts;
		if (materials == null || verts == null || hull.IsNull || !materials.AsSpan().ContainsAnyExcept((byte)0))
			return 0;

		Vector3 center = default;
		foreach (Vector3 vert in verts)
			center += vert;
		center /= verts.Length;

		ReadOnlySpan<Box3D.Plane> planes = hull.Planes;
		int faceCount = Math.Min(planes.Length, faceMaterials.Length);
		for (int f = 0; f < faceCount; f++) {
			float bestDot = float.MinValue;
			int best = 0;
			for (int t = 0; t < materials.Length; t++) {
				Vector3 a = verts[t * 3 + 0], b = verts[t * 3 + 1], c = verts[t * 3 + 2];
				Vector3 normal = Vector3.Cross(b - a, c - a);
				float length = normal.Length();
				if (length <= 0.0f)
					continue;

				normal /= length;
				if (Vector3.Dot(normal, (a + b + c) / 3.0f - center) < 0.0f)
					normal = -normal;

				float dot = Vector3.Dot(normal, planes[f].normal);
				if (dot > bestDot) {
					bestDot = dot;
					best = materials[t];
				}
			}

			int faceMaterial = best == 0 ? materialIndex : (int)((PhysicsSurfaceProps)physprops).RemapWorldMaterial(best);
			faceMaterials[f] = MakeSurfaceMaterial(faceMaterial, materialIndex);
		}
		return faceCount;
	}

	internal static ShapeDef MakeConvexShapeDef(BoxPhysConvex convex, HullData hull, int materialIndex, bool sensor, Span<SurfaceMaterial> faceMaterials) {
		ShapeDef shapeDef = MakeShapeDef(sensor ? materialIndex : ConvexMaterialIndex(convex, materialIndex), materialIndex, sensor);
		if (!sensor) {
			int faceCount = FillFaceMaterials(convex, hull, materialIndex, faceMaterials);
			if (faceCount > 0)
				shapeDef.materials = faceMaterials[..faceCount];
		}
		return shapeDef;
	}

	static SurfaceMaterial MakeSurfaceMaterial(int shapeMaterial, int materialIndex) {
		SurfaceMaterial material = ShapeDef.Default.baseMaterial;
		SurfaceData_ptr? surface = physprops.GetSurfaceData(shapeMaterial);
		if (surface != null)
			ApplySurfaceMaterial(ref material, surface);
		if (shapeMaterial != materialIndex)
			material.userMaterialId = (ulong)shapeMaterial + 1;
		return material;
	}

	static void ApplySurfaceMaterial(ref SurfaceMaterial material, SurfaceData_ptr surface) {
		material.friction = MathF.Max(surface.Physics.Friction, 0.0f);
		material.restitution = MathF.Max(surface.Physics.Elasticity, 0.0f);
	}

	internal static ShapeDef MakeMeshShapeDef(BoxPhysCollide collide, int materialIndex, bool sensor)
		=> MakeShapeDef(sensor || collide.MeshMaterialIndex < 0 ? materialIndex : collide.MeshMaterialIndex, materialIndex, sensor);

	static ShapeDef MakeShapeDef(int shapeMaterial, int materialIndex, bool sensor) {
		ShapeDef shapeDef = MakeShapeDef(shapeMaterial, sensor);
		if (shapeMaterial != materialIndex)
			shapeDef.baseMaterial.userMaterialId = (ulong)shapeMaterial + 1;
		return shapeDef;
	}

	internal int GetShapeMaterialIndex(Shape shape) {
		ulong id = shape.SurfaceMaterial.userMaterialId;
		return id != 0 ? (int)(id - 1) : MaterialIndex;
	}

	internal static ShapeDef MakeShapeDef(int materialIndex, bool sensor) {
		ShapeDef shapeDef = ShapeDef.Default;
		shapeDef.enableSensorEvents = true;
		if (sensor) {
			shapeDef.isSensor = true;
			return shapeDef;
		}

		shapeDef.enableContactEvents = true;
		shapeDef.enableHitEvents = true;
		shapeDef.enableCustomFiltering = true;
		shapeDef.enablePreSolveEvents = true;

		SurfaceData_ptr? surface = physprops.GetSurfaceData(materialIndex);
		if (surface != null) {
			ApplySurfaceMaterial(ref shapeDef.baseMaterial, surface);
			if (surface.Physics.Density > 0.0f)
				shapeDef.density = surface.Physics.Density;
		}
		return shapeDef;
	}

	void RebuildShapes(bool asSensor) {
		Span<Shape> shapes = stackalloc Shape[32];
		int oldCount = BodyId.GetShapes(shapes);
		for (int i = 0; i < oldCount; i++)
			shapes[i].Destroy(false);

		ShapeDef shapeDef = MakeShapeDef(MaterialIndex, asSensor);
		shapeDef.updateBodyMass = false;

		if (Collide != null) {
			Span<SurfaceMaterial> faceMaterials = stackalloc SurfaceMaterial[Box3D.Constants.MaxHullFaces];
			foreach (BoxPhysConvex convex in Collide.Convexes) {
				if (convex.Hull.IsNull)
					continue;
				HullData hull = Static ? convex.Hull : convex.GetSimHull();
				ShapeDef convexShapeDef = MakeConvexShapeDef(convex, hull, MaterialIndex, asSensor, faceMaterials);
				convexShapeDef.updateBodyMass = false;
				Shape.CreateHull(BodyId, convexShapeDef, hull);
			}
			if (!Collide.Mesh.IsNull) {
				ShapeDef meshShapeDef = MakeMeshShapeDef(Collide, MaterialIndex, asSensor);
				meshShapeDef.updateBodyMass = false;
				Shape.CreateMesh(BodyId, meshShapeDef, Collide.Mesh, Vector3.One);
			}
		}
		else if (SphereRadius > 0.0f) {
			Sphere sphere = new() { center = default, radius = SourceToBox.Distance(SphereRadius) };
			Shape.CreateSphere(BodyId, shapeDef, sphere);
		}
	}

	public void BecomeTrigger() {
		if (Trigger)
			return;
		Trigger = true;
		RebuildShapes(true);
	}

	public void RemoveTrigger() {
		if (!Trigger)
			return;
		Trigger = false;
		RebuildShapes(false);
		if (!Static)
			SetMass(CachedMass);
	}

	public void BecomeHinged(int localAxis) { }
	public void RemoveHinged() { }

	public IPhysicsFrictionSnapshot CreateFrictionSnapshot() => new PhysicsFrictionSnapshot(this, Env.GetLastStepTime());
	public void DestroyFrictionSnapshot(IPhysicsFrictionSnapshot snapshot) { }

	public void OutputDebugInfo() { }
}
