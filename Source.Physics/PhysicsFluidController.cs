using Box3D;

using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CollisionPlane = Source.Common.Mathematics.CollisionPlane;

namespace Source.Physics;

internal class PhysicsFluidController : IPhysicsFluidController
{
	PhysicsObject? FluidObject;
	FluidParams Params;
	CollisionPlane LocalPlane;
	List<PhysicsObject> ObjectsInFluid = [];

	public PhysicsFluidController(PhysicsObject fluidObject, in FluidParams parms) {
		FluidObject = fluidObject;
		Params = parms;
		LocalPlane = PlaneToLocalSpace(fluidObject, parms.SurfacePlane);
		FluidObject.BecomeTrigger();
	}

	public void Destroy() => FluidObject?.RemoveTrigger();

	static CollisionPlane PlaneToLocalSpace(PhysicsObject fluidObject, in Vector4 worldSurfacePlane) {
		fluidObject.GetPositionMatrix(out Matrix3x4 objectToWorld);

		MathLib.MatrixGetColumn(objectToWorld, 3, out Vector3 translation);
		MathLib.VectorIRotate(translation, objectToWorld, out Vector3 invTranslation);

		CollisionPlane localPlane = default;
		MathLib.VectorIRotate(new Vector3(worldSurfacePlane.X, worldSurfacePlane.Y, worldSurfacePlane.Z), objectToWorld, out localPlane.Normal);
		localPlane.Dist = worldSurfacePlane.W * Vector3.Dot(localPlane.Normal, localPlane.Normal);
		localPlane.Dist -= Vector3.Dot(localPlane.Normal, invTranslation);
		return localPlane;
	}

	CollisionPlane GetWorldSurfacePlane() {
		if (FluidObject == null)
			return default;

		FluidObject.GetPositionMatrix(out Matrix3x4 objectToWorld);
		MathLib.MatrixGetColumn(objectToWorld, 3, out Vector3 translation);

		CollisionPlane worldPlane = default;
		MathLib.VectorRotate(LocalPlane.Normal, objectToWorld, out worldPlane.Normal);
		worldPlane.Dist = LocalPlane.Dist * Vector3.Dot(worldPlane.Normal, worldPlane.Normal);
		worldPlane.Dist += Vector3.Dot(worldPlane.Normal, translation);
		return worldPlane;
	}

	static float ComputeBodyBuoyancy(PhysicsObject obj, out AABB aabb) {
		Body bodyId = obj.BodyId;
		Transform xf = bodyId.Transform;

		float volume = 0.0f;
		bool hasBounds = false;
		AABB bounds = default;

		if (obj.GetCollide() is BoxPhysCollide collide) {
			foreach (BoxPhysConvex convex in collide.Convexes) {
				if (convex.Hull.IsNull)
					continue;
				volume += convex.Hull.ComputeMass(1.0f).mass;
				AABB hullAABB = convex.Hull.ComputeAABB(xf);
				bounds = hasBounds ? BoxMath.AABB_Union(bounds, hullAABB) : hullAABB;
				hasBounds = true;
			}
		}

		if (!hasBounds) {
			int count = bodyId.ShapeCount;
			Span<Shape> shapes = stackalloc Shape[Math.Max(count, 1)];
			count = bodyId.GetShapes(shapes);
			for (int i = 0; i < count; i++) {
				if (shapes[i].Type != ShapeType.Sphere)
					continue;
				Sphere sphere = shapes[i].Sphere;
				volume += (4.0f / 3.0f) * MathF.PI * sphere.radius * sphere.radius * sphere.radius;
				Vector3 center = BoxToSource.Unitless(BoxMath.TransformPoint(xf, sphere.center));
				Vector3 rad = new(sphere.radius);
				AABB sphAABB = new() { lowerBound = SourceToBox.Unitless(center - rad), upperBound = SourceToBox.Unitless(center + rad) };
				bounds = hasBounds ? BoxMath.AABB_Union(bounds, sphAABB) : sphAABB;
				hasBounds = true;
			}
		}

		aabb = bounds;
		return volume;
	}

	struct FluidQuery : IOverlapResultHandler
	{
		public PhysicsObject? FluidObject;
		public List<PhysicsObject> Out;

		public bool OnOverlapResult(Shape shapeId) {
			PhysicsObject? obj = PhysicsObject.FromUserData(shapeId.Body.UserData);
			if (obj != null && obj != FluidObject && !obj.IsStatic() && !Out.Contains(obj))
				Out.Add(obj);
			return true;
		}
	}

	public void SetGameData(object? gameData) => Params.GameData = gameData;
	public object? GetGameData() => Params.GameData;
	public int GetContents() => (int)Params.Contents;

	public void GetSurfacePlane(out Vector3 normal, out float dist) {
		CollisionPlane worldPlane = GetWorldSurfacePlane();
		normal = worldPlane.Normal;
		dist = worldPlane.Dist;
	}

	public float GetDensity() {
		SurfaceData_ptr? surface = FluidObject != null ? physprops.GetSurfaceData(FluidObject.GetMaterialIndex()) : null;
		return surface != null && surface.Physics.Density > 0.0f ? surface.Physics.Density : 1000.0f;
	}

	public void WakeAllSleepingObjects() {
		foreach (PhysicsObject obj in ObjectsInFluid)
			obj.Wake();
	}

	public void DetachObject(PhysicsObject obj) {
		if (obj == FluidObject)
			FluidObject = null;
		ObjectsInFluid.Remove(obj);
	}

	public PhysicsObject? GetFluidObject() => FluidObject;

	public void OnPreSimulate(float deltaTime) {
		if (FluidObject == null || deltaTime <= 0.0f)
			return;

		World worldId = FluidObject.Env.GetWorldId();

		FluidQuery query = new() { FluidObject = FluidObject, Out = [] };
		worldId.OverlapAABB(FluidObject.BodyId.ComputeAABB(), QueryFilter.Default, ref query);

		List<PhysicsObject> nowInFluid = [];

		CollisionPlane worldPlane = GetWorldSurfacePlane();
		Vector3 n = worldPlane.Normal;
		float planeDist = SourceToBox.Distance(worldPlane.Dist);
		Vector3 gravity = BoxToSource.Unitless(worldId.Gravity);
		Vector3 current = BoxToSource.Unitless(SourceToBox.Distance(Params.CurrentVelocity));
		float waterDensity = GetDensity();
		float linearDrag = Params.Damping;
		const float angularDrag = 0.1f;

		foreach (PhysicsObject obj in query.Out) {
			if (obj.HasShadowController() || (obj.GetCallbackFlags() & CallbackFlags.DoFluidSimulation) == 0)
				continue;

			Body body = obj.BodyId;
			float invMass = body.InverseMass;
			float totalVolume = ComputeBodyBuoyancy(obj, out AABB aabb);
			if (invMass <= 0.0f || totalVolume <= 0.0f)
				continue;

			Vector3 lower = BoxToSource.Unitless(aabb.lowerBound), upper = BoxToSource.Unitless(aabb.upperBound);
			Vector3 c = 0.5f * (lower + upper);
			Vector3 half = 0.5f * (upper - lower);
			float r = MathF.Abs(n.X) * half.X + MathF.Abs(n.Y) * half.Y + MathF.Abs(n.Z) * half.Z;
			float cn = Vector3.Dot(n, c);
			float lo = cn - r, hi = cn + r;
			if (hi <= lo)
				continue;
			float top = MathF.Min(hi, planeDist);
			float subLen = Math.Clamp(top - lo, 0.0f, hi - lo);
			if (subLen <= 0.0f)
				continue;

			nowInFluid.Add(obj);

			float fraction = subLen / (hi - lo);
			float submergedVolume = totalVolume * fraction;

			float midSub = 0.5f * (lo + top);
			Vector3 cob = c + (midSub - cn) * n;
			Vector3 relCob = cob - BoxToSource.Unitless(body.WorldCenter);

			float fluidDensity = waterDensity * obj.GetBuoyancyRatio();

			Vector3 buoyImpulse = -fluidDensity * submergedVolume * deltaTime * gravity;

			Vector3 linVel = BoxToSource.Unitless(body.LinearVelocity);
			Vector3 angVel = BoxToSource.Unitless(body.AngularVelocity);
			Vector3 relVel = current - (linVel + Vector3.Cross(angVel, relCob));

			Vector3 size = upper - lower;
			float relSq = relVel.LengthSquared();
			Vector3 dragImpulse = default;
			if (relSq > 1e-12f) {
				float area = (MathF.Abs(relVel.X) * size.Y * size.Z + MathF.Abs(relVel.Y) * size.Z * size.X + MathF.Abs(relVel.Z) * size.X * size.Y) / MathF.Sqrt(relSq);
				dragImpulse = 0.5f * fluidDensity * linearDrag * area * deltaTime * MathF.Sqrt(relSq) * relVel;

				float linVelSq = linVel.LengthSquared();
				Vector3 dragDv = invMass * dragImpulse;
				float dragDvSq = dragDv.LengthSquared();
				if (dragDvSq > linVelSq && dragDvSq > 0.0f)
					dragImpulse *= MathF.Sqrt(linVelSq / dragDvSq);
			}

			Vector3 linImpulse = buoyImpulse + dragImpulse;
			body.ApplyLinearImpulseToCenter(SourceToBox.Unitless(linImpulse), false);
			body.ApplyAngularImpulse(SourceToBox.Unitless(Vector3.Cross(relCob, linImpulse)), false);

			float l = (size.X + size.Y + size.Z) / 3.0f;
			body.ApplyAngularImpulse(SourceToBox.Unitless(-angularDrag * fraction * deltaTime * l * l / invMass * angVel), false);
		}

		IPhysicsCollisionEvent? ev = FluidObject.Env.GetCollisionEvent();
		if (ev != null) {
			foreach (PhysicsObject obj in nowInFluid)
				if (!ObjectsInFluid.Contains(obj))
					ev.FluidStartTouch(obj, this);
			foreach (PhysicsObject obj in ObjectsInFluid)
				if (!nowInFluid.Contains(obj))
					ev.FluidEndTouch(obj, this);
		}

		ObjectsInFluid = nowInFluid;
	}
}
