global using static Game.Server.PhysicsImpactDamage;

using Game.Shared;

using Source;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

namespace Game.Server;

public struct VPhysicsObjectStress
{
	public float ExertedStress;
	public float ReceivedStress;
	public bool HasNonStaticStress;
	public bool HasLargeObjectContact;
	public IPhysicsObject? StressObject;
}

public static class PhysicsImpactDamage
{
	static bool IsPhysicallyControlled(BaseEntity entity, IPhysicsObject physics) {
		bool isPhysical = false;
		if (entity.GetMoveType() == Source.MoveType.VPhysics)
			isPhysical = true;
		else {
			IPhysicsShadowController? shadow = physics.GetShadowController();
			if (shadow != null)
				isPhysical = shadow.IsPhysicallyControlled();
		}
		return isPhysical;
	}

	public static float CalculateObjectStress(IPhysicsObject obj, BaseEntity inputOwnerEntity, out VPhysicsObjectStress output) {
		List<BaseEntity?> objectList = [];
		List<Vector3> objectForce = [];
		bool hasLargeObject = false;
		IPhysicsObject? stressObject = null;

		objectList.Add(null);
		objectForce.Add(vec3_origin);
		objectList.Add(null);
		objectForce.Add(vec3_origin);

		BaseCombatCharacter? bcc = inputOwnerEntity.MyCombatCharacterPointer();

		IPhysicsFrictionSnapshot snapshot = obj.CreateFrictionSnapshot();
		float objMass = obj.GetMass();
		while (snapshot.IsValid()) {
			float force = snapshot.GetNormalForce();
			if (force > 0.0f) {
				IPhysicsObject other = snapshot.GetObject(1)!;
				BaseEntity? otherEntity = other.GetGameData() as BaseEntity;
				otherEntity ??= GetWorldEntity()!;

				BaseEntity otherOwner = otherEntity;
				if (otherEntity.GetOwnerEntity() != null)
					otherOwner = otherEntity.GetOwnerEntity()!;

				int outIndex = 0;
				if (!other.IsMoveable())
					outIndex = 0;
				else if (bcc != null && (bcc.IRelationType(otherOwner) == Disposition.LI || otherEntity.IsNavIgnored()))
					outIndex = 1;
				else if ((other.GetGameFlags() & PhysicsFlags.PlayerHeld) != 0)
					outIndex = 1;
				else {
					if (other.GetMass() >= VPHYSICS_LARGE_OBJECT_MASS) {
						if (inputOwnerEntity.GetGroundEntity() != otherEntity)
							hasLargeObject = true;
					}

					outIndex = objectList.Count;
					for (int i = objectList.Count - 1; i >= 2; --i) {
						if (objectList[i] == otherOwner) {
							outIndex = i;
							break;
						}
					}

					if (outIndex == objectList.Count) {
						objectList.Add(otherOwner);
						objectForce.Add(vec3_origin);
						stressObject ??= other;
					}
				}

				if (outIndex != 0 && inputOwnerEntity.GetMoveType() != Source.MoveType.VPhysics && !IsPhysicallyControlled(otherEntity, other))
					force = 0.0f;

				snapshot.GetSurfaceNormal(out Vector3 normal);
				objectForce[outIndex] += normal * force;
			}
			snapshot.NextFrictionData();
		}
		obj.DestroyFrictionSnapshot(snapshot);

		objectForce[1] = vec3_origin;

		float sum = 0;
		Vector3 negativeForce = vec3_origin;
		Vector3 positiveForce = vec3_origin;

		Assert(objectList.Count == objectForce.Count);
		for (int objectIndex = objectList.Count - 1; objectIndex >= 0; --objectIndex) {
			Vector3 force = objectForce[objectIndex];
			sum += force.Length();
			for (int i = 0; i < 3; i++) {
				if (force[i] < 0)
					negativeForce[i] -= force[i];
				else
					positiveForce[i] += force[i];
			}
		}

		Vector3 internalForce = vec3_origin;
		Vector3 externalForce = vec3_origin;

		for (int i = 0; i < 3; i++) {
			if (negativeForce[i] < positiveForce[i]) {
				internalForce[i] = positiveForce[i] - negativeForce[i];
				externalForce[i] = negativeForce[i];
			}
			else {
				internalForce[i] = negativeForce[i] - positiveForce[i];
				externalForce[i] = positiveForce[i];
			}
		}

		physenv.GetGravity(out Vector3 gravVector);
		float gravity = gravVector.Length();
		if (inputOwnerEntity.GetMoveType() != Source.MoveType.VPhysics && obj.IsMoveable()) {
			Vector3 lastVel = vec3_origin;
			IPhysicsShadowController? shadow = obj.GetShadowController();
			if (shadow != null)
				shadow.GetLastImpulse(out lastVel);
			else {
				if ((obj.GetCallbackFlags() & CallbackFlags.IsPlayerController) != 0) {
					BasePlayer? player = ToBasePlayer(inputOwnerEntity);
					IPhysicsPlayerController? controller = player?.GetPhysicsController();
					controller?.GetLastImpulse(out lastVel);
				}
			}

			float inputLen = lastVel.Length() * (1.0f / (float)physenv.GetSimulationTimestep()) * objMass;
			if (inputLen > 0.0f) {
				float internalLen = internalForce.Length();
				if (internalLen < inputLen) {
					float ratio = internalLen / inputLen;
					Vector3 delta = internalForce * (1.0f - ratio);
					internalForce += delta;
					float deltaLen = delta.Length();
					sum -= deltaLen;
					float extLen = MathLib.VectorNormalize(ref externalForce) - deltaLen;
					if (extLen < 0)
						extLen = 0;

					externalForce *= extLen;
				}
			}
		}

		float invGravity = gravity;
		if (invGravity <= 0)
			invGravity = 1.0f;
		else
			invGravity = 1.0f / invGravity;

		sum *= invGravity;
		internalForce *= invGravity;
		externalForce *= invGravity;
		if (!obj.IsMoveable()) {
			externalForce = internalForce;
			internalForce = vec3_origin;

			if (!obj.IsStatic())
				sum += objMass;
		}
		else {
			if (sum > objMass)
				sum = objMass + (sum - objMass) * 0.5f;
		}

		output.ExertedStress = internalForce.Length();
		output.ReceivedStress = externalForce.Length();
		output.HasNonStaticStress = objectList.Count > 2;
		output.HasLargeObjectContact = hasLargeObject;
		output.StressObject = stressObject;

		return sum;
	}
}
