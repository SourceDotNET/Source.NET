using Box3D;

using Source.Common.Physics;

using System.Numerics;


namespace Source.Physics;

internal unsafe class PhysicsFrictionSnapshot : IPhysicsFrictionSnapshot
{
	struct Entry
	{
		public PhysicsObject Self;
		public PhysicsObject Other;
		public int SelfMaterial;
		public int OtherMaterial;
		public Vector3 Normal;
		public Vector3 Point;
		public float NormalForce;
		public float Energy;
	}

	readonly List<Entry> Entries = [];
	int Index;

	public PhysicsFrictionSnapshot(PhysicsObject self, float stepTime) {
		float invStep = stepTime > 0.0f ? 1.0f / stepTime : 0.0f;
		Body body = self.BodyId;

		int capacity = body.ContactCapacity;
		if (capacity <= 0)
			return;

		ContactData[] contacts = new ContactData[capacity];
		int count = body.GetContactData(contacts);

		for (int i = 0; i < count; i++) {
			ref ContactData contact = ref contacts[i];
			Body bodyA = contact.shapeIdA.Body;
			PhysicsObject? a = PhysicsObject.FromUserData(bodyA.UserData);
			bool selfIsA = a == self;
			PhysicsObject? other = selfIsA ? PhysicsObject.FromUserData(contact.shapeIdB.Body.UserData) : a;
			if (other == null)
				continue;

			int selfMaterial = self.GetShapeMaterialIndex(selfIsA ? contact.shapeIdA : contact.shapeIdB);
			int otherMaterial = other.GetShapeMaterialIndex(selfIsA ? contact.shapeIdB : contact.shapeIdA);
			Vector3 comA = BoxToSource.Unitless(bodyA.WorldCenter);

			{
				foreach (ref readonly Manifold manifold in contact.manifolds) {
					Vector3 normal = BoxToSource.Unitless(manifold.normal);
					if (!selfIsA)
						normal = -normal;

					for (int p = 0; p < manifold.pointCount; p++) {
						ref readonly ManifoldPoint point = ref manifold.points[p];
						if (point.totalNormalImpulse <= 0.0f)
							continue;

						Entries.Add(new Entry {
							Self = self,
							Other = other,
							SelfMaterial = selfMaterial,
							OtherMaterial = otherMaterial,
							Normal = normal,
							Point = BoxToSource.Distance(SourceToBox.Unitless(comA + BoxToSource.Unitless(point.anchorA))),
							NormalForce = BoxToSource.Distance(point.totalNormalImpulse * invStep),
							Energy = BoxToSource.Distance(BoxToSource.Distance(MathF.Abs(point.totalNormalImpulse * point.normalVelocity)))
						});
					}
				}
			}
		}
	}

	public bool IsValid() => Index < Entries.Count;

	public IPhysicsObject? GetObject(int index) {
		if (!IsValid())
			return null;
		return index == 1 ? Entries[Index].Other : Entries[Index].Self;
	}

	public int GetMaterial(int index) {
		if (!IsValid())
			return 0;
		return index == 1 ? Entries[Index].OtherMaterial : Entries[Index].SelfMaterial;
	}

	public void GetContactPoint(out Vector3 vec) => vec = IsValid() ? Entries[Index].Point : default;
	public void GetSurfaceNormal(out Vector3 vec) => vec = IsValid() ? Entries[Index].Normal : default;
	public float GetNormalForce() => IsValid() ? Entries[Index].NormalForce : 0.0f;
	public float GetEnergyAbsorbed() => IsValid() ? Entries[Index].Energy : 0.0f;

	public void RecomputeFriction() { }
	public void ClearFrictionForce() { }
	public void MarkContactForDelete() { }
	public void DeleteAllMarkedContacts(bool wakeObjects) { }

	public void NextFrictionData() => Index++;

	public float GetFrictionCoefficient() {
		if (!IsValid())
			return 0.0f;
		SurfaceData_ptr? selfSurface = physprops.GetSurfaceData(Entries[Index].SelfMaterial);
		SurfaceData_ptr? otherSurface = physprops.GetSurfaceData(Entries[Index].OtherMaterial);
		float self = selfSurface != null ? selfSurface.Physics.Friction : 0.0f;
		float other = otherSurface != null ? otherSurface.Physics.Friction : self;
		return self * other;
	}
}
