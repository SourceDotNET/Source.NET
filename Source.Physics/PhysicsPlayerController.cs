using Box3D;

using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;


namespace Source.Physics;

internal class PhysicsPlayerController : IPhysicsPlayerController
{
	const float GroundNormalZ = -0.7f;

	PhysicsObject? Object;
	PhysicsObject? Ground;
	IPhysicsPlayerControllerEvent? Handler;

	Vector3 TargetPosition;
	Vector3 GroundPosition;
	Vector3 MaxSpeedValue;
	Vector3 CurrentSpeed;
	Vector3 LastImpulse;
	float SecondsToArrival;
	readonly float MaxDeltaPosition = 24.0f;
	readonly float DampFactor = 1.0f;
	float PushableMassLimit = PhysicsConstants.VPHYSICS_MAX_MASS;
	float PushableSpeedLimit = 1e4f;
	float SavedAngularDamping;
	bool Enable;
	bool UpdatedSinceLast;

	struct NormalList
	{
		const int MaxNormals = 8;
		InlineArray8<Vector3> Normals;
		int Count;

		public void AddNormal(in Vector3 normal) {
			if (Count == MaxNormals)
				return;
			for (int i = 0; i < Count; i++) {
				if (Vector3.Dot(Normals[i], normal) > 0.99f)
					return;
			}
			Normals[Count++] = normal;
		}

		public readonly Vector3 ClampVector(in Vector3 input, float limitVel) {
			if (Count > 2) {
				for (int i = 0; i < Count; i++) {
					if (Vector3.Dot(input, Normals[i]) > 0.0f)
						return default;
				}
			}
			else if (Count == 2) {
				Vector3 crease = Vector3.Cross(Normals[0], Normals[1]);
				return crease * Vector3.Dot(input, crease);
			}
			else if (Count == 1) {
				float dot = Vector3.Dot(input, Normals[0]);
				if (dot > limitVel)
					return input + Normals[0] * (limitVel - dot);
			}
			return input;
		}
	}

	public PhysicsPlayerController(PhysicsObject obj) => SetObjectInternal(obj);

	public void Destroy() => SetObjectInternal(null);

	static PhysicsObject? ContactOther(in ContactData contact, PhysicsObject self, out bool selfIsA) {
		PhysicsObject? a = PhysicsObject.FromUserData(contact.shapeIdA.Body.UserData);
		selfIsA = a == self;
		return selfIsA ? PhysicsObject.FromUserData(contact.shapeIdB.Body.UserData) : a;
	}

	static void ComputeController(ref Vector3 currentSpeed, in Vector3 delta, in Vector3 maxSpeed, float scaleDelta, float damping, out Vector3 impulse) {
		Vector3 acceleration = delta * scaleDelta;

		if (currentSpeed.LengthSquared() < 1e-6f)
			currentSpeed = default;

		acceleration += currentSpeed * -damping;

		for (int i = 0; i < 3; i++) {
			if (MathF.Abs(acceleration[i]) < maxSpeed[i])
				continue;
			acceleration[i] = acceleration[i] < 0.0f ? -maxSpeed[i] : maxSpeed[i];
		}

		currentSpeed += acceleration;
		impulse = acceleration;
	}

	void SetObjectInternal(PhysicsObject? obj) {
		if (Object == obj)
			return;

		if (Object != null) {
			Body oldId = Object.BodyId;
			if (oldId.IsValid)
				oldId.AngularDamping = SavedAngularDamping;
			Object.SetCallbackFlags(Object.GetCallbackFlags() & ~CallbackFlags.IsPlayerController);
		}

		Object = obj;
		SetGround(null);

		if (Object != null) {
			Body bodyId = Object.BodyId;

			Object.EnableDrag(false);
			SavedAngularDamping = bodyId.AngularDamping;
			bodyId.AngularDamping = 100.0f;

			Object.SetCallbackFlags(Object.GetCallbackFlags() | CallbackFlags.IsPlayerController);
		}
	}

	void SetGround(PhysicsObject? ground) => Ground = ground;

	public void ClearGround(PhysicsObject obj) {
		if (Ground == obj)
			Ground = null;
	}

	public PhysicsObject? GetControlledObject() => Object;

	public void Update(in Vector3 position, in Vector3 velocity, float secondsToArrival, bool onground, IPhysicsObject ground) {
		UpdatedSinceLast = true;

		if ((velocity - CurrentSpeed).LengthSquared() < 1e-6f && (position - TargetPosition).LengthSquared() < 1e-6f)
			return;

		TargetPosition = position;
		CurrentSpeed = velocity;
		SecondsToArrival = secondsToArrival < 0.0f ? 0.0f : secondsToArrival;

		if (Object != null)
			Object.BodyId.IsAwake = true;

		IPhysicsObject? groundObject = ground;
		Enable = true;
		if (velocity.LengthSquared() <= 0.1f) {
			Enable = false;
			groundObject = null;
		}
		else
			MaxSpeed(velocity);

		SetGround(groundObject as PhysicsObject);
		if (Ground != null)
			Ground.WorldToLocal(out GroundPosition, TargetPosition);
	}

	public void SetEventHandler(IPhysicsPlayerControllerEvent handler) => Handler = handler;

	static bool IsControlledByGame(PhysicsObject obj) {
		IPhysicsShadowController? shadow = obj.GetShadowController();
		if (shadow != null && !shadow.IsPhysicallyControlled())
			return true;

		return (obj.GetCallbackFlags() & CallbackFlags.IsPlayerController) != 0;
	}

	public bool IsInContact() {
		if (Object == null || !Object.IsCollisionEnabled())
			return false;

		Span<ContactData> contacts = stackalloc ContactData[32];
		int count = Object.BodyId.GetContactData(contacts);
		for (int i = 0; i < count; i++) {
			bool touching = false;
			foreach (ref readonly Manifold manifold in contacts[i].manifolds)
				touching |= manifold.pointCount > 0;
			if (!touching)
				continue;

			PhysicsObject? other = ContactOther(contacts[i], Object, out _);
			if (other == null || !other.IsCollisionEnabled() || !other.IsMoveable())
				continue;

			if (IsControlledByGame(other))
				continue;

			return true;
		}
		return false;
	}

	public void MaxSpeed(in Vector3 maxVelocity) {
		if (Object == null)
			return;

		Object.GetVelocity(out Vector3 currentVelocity, out _);

		float length = maxVelocity.Length();
		Vector3 direction = length > 0.0f ? maxVelocity / length : default;

		Vector3 available = maxVelocity;
		float dot = Vector3.Dot(direction, currentVelocity);
		if (dot > 0.0f)
			available -= direction * (dot * length);

		MaxSpeedValue = Vector3.Abs(available);
	}

	public void SetObject(IPhysicsObject obj) => SetObjectInternal(obj as PhysicsObject);

	public int GetShadowPosition(out Vector3 position, out QAngle angles) {
		position = default;
		angles = default;
		Object?.GetPosition(out position, out angles);
		return 1;
	}

	public void StepUp(float height) {
		if (height == 0.0f || Object == null)
			return;

		Object.GetPosition(out Vector3 position, out QAngle angles);
		position.Z += height;
		Object.SetPosition(position, angles, true);
	}

	public void Jump() { }

	public void GetShadowVelocity(out Vector3 velocity) {
		velocity = default;
		if (Object == null)
			return;

		Object.GetVelocity(out velocity, out _);

		if (Ground != null) {
			Ground.LocalToWorld(out Vector3 groundPoint, GroundPosition);
			Ground.GetVelocityAtPoint(groundPoint, out Vector3 baseVelocity);
			velocity -= baseVelocity;
		}
	}

	public IPhysicsObject? GetObject() => Object;

	public void GetLastImpulse(out Vector3 vec) => vec = LastImpulse;

	public void SetPushMassLimit(float maxPushMass) => PushableMassLimit = maxPushMass;
	public void SetPushSpeedLimit(float maxPushSpeed) => PushableSpeedLimit = maxPushSpeed;
	public float GetPushMassLimit() => PushableMassLimit;
	public float GetPushSpeedLimit() => PushableSpeedLimit;
	public bool WasFrozen() => false;

	bool TryTeleportObject() {
		if (Handler != null && !Handler.ShouldMoveTo(Object!, TargetPosition))
			return false;

		Object!.GetPosition(out _, out QAngle angles);

		if (Object.IsCollisionEnabled()) {
			Object.EnableCollisions(false);
			Object.SetPosition(TargetPosition, angles, true);
			Object.EnableCollisions(true);
		}
		else
			Object.SetPosition(TargetPosition, angles, true);
		return true;
	}

	public void OnPreSimulate(float deltaTime) {
		if (Object == null || !Enable || deltaTime <= 0.0f)
			return;

		Body bodyId = Object.BodyId;
		if (!bodyId.IsAwake)
			return;

		Object.GetPosition(out Vector3 position, out _);
		Object.GetVelocity(out Vector3 speed, out _);

		Vector3 baseVelocity = default;
		if (Ground != null) {
			Ground.LocalToWorld(out TargetPosition, GroundPosition);
			Ground.GetVelocityAtPoint(TargetPosition, out baseVelocity);
			speed -= baseVelocity;
		}

		Vector3 deltaPos = TargetPosition - position;

		if (deltaPos.LengthSquared() > MaxDeltaPosition * MaxDeltaPosition) {
			if (TryTeleportObject()) {
				bodyId.LinearVelocity = SourceToBox.Distance(speed);
				return;
			}
		}

		float fraction = 1.0f;
		if (SecondsToArrival > 0.0f)
			fraction = MathF.Min(deltaTime / SecondsToArrival, 1.0f);

		if (!UpdatedSinceLast) {
			float len = LastImpulse.Length();
			ComputeController(ref speed, deltaPos, new Vector3(len), fraction / deltaTime, DampFactor, out _);
		}
		else
			ComputeController(ref speed, deltaPos, MaxSpeedValue, fraction / deltaTime, DampFactor, out LastImpulse);
		speed += baseVelocity;
		UpdatedSinceLast = false;

		float mass = Object.GetMass();
		float invMass = mass > 0.0f ? 1.0f / mass : 0.0f;
		float limitVel = PushableSpeedLimit;
		bool onGround = false;
		NormalList normalList = default;

		Span<ContactData> contacts = stackalloc ContactData[32];
		int count = bodyId.GetContactData(contacts);
		for (int i = 0; i < count; i++) {
			PhysicsObject? other = ContactOther(contacts[i], Object, out bool selfIsA);

			foreach (ref readonly Manifold manifold in contacts[i].manifolds) {
				if (manifold.pointCount <= 0)
					continue;

				Vector3 normal = BoxToSource.Unitless(manifold.normal);
				if (!selfIsA)
					normal = -normal;

				if (normal.Z < GroundNormalZ)
					onGround = true;

				if (normal.Z > -0.99f) {
					if (other == null || !other.IsMoveable() || other.GetMass() > PushableMassLimit)
						limitVel = 0.0f;

					float impulse = 0.0f;
					for (int p = 0; p < manifold.pointCount; p++)
						impulse = MathF.Max(impulse, manifold.points[p].totalNormalImpulse);

					float pushSpeed = Vector3.Dot(speed, normal);
					float contactVel = BoxToSource.Distance(impulse * invMass);
					if (pushSpeed + contactVel > limitVel)
						normalList.AddNormal(normal);
				}
			}
		}

		Vector3 limit = normalList.ClampVector(speed, limitVel) - speed;
		speed += limit;
		LastImpulse += limit;

		if (onGround) {
			Vector3 gravity = BoxToSource.Unitless(Object.Env.GetWorldId().Gravity);
			float gravDt = BoxToSource.Distance(gravity.Length()) * deltaTime;
			if (LastImpulse.Z <= 0.0f) {
				float delta = -gravDt - LastImpulse.Z;
				speed.Z += delta;
				LastImpulse.Z += delta;
			}
		}

		bodyId.LinearVelocity = SourceToBox.Distance(speed);

		SecondsToArrival = MathF.Max(SecondsToArrival - deltaTime, 0.0f);
	}
}
