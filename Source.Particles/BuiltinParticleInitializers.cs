using Source.Common.Mathematics;

using System.Numerics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public static class BuiltinParticleInitializers
{
	public static void AddBuiltInParticleInitializers(ParticleSystemMgr mgr) {
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_CreateWithinSphere.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Initializer, C_INIT_RandomLifeTime.Definition);
	}
}

public sealed class C_INIT_CreateWithinSphere : ParticleOperatorInstance
{
	public float RadiusMin;
	public float RadiusMax;
	public Vector3 DistanceBias;
	public Vector3 DistanceBiasAbs;
	public int ControlPointNumber;
	public float SpeedMin;
	public float SpeedMax;
	public float SpeedRandExp;
	public bool LocalCoords;
	public Vector3 LocalCoordinateSystemSpeedMin;
	public Vector3 LocalCoordinateSystemSpeedMax;
	public int CreateInModel;
	public bool UseHighestEndCP;
	public float EndCPGrowthTime;
	bool HasDistanceBias;
	bool HasDistanceBiasAbs;

	public static readonly ParticleOperatorDefinition<C_INIT_CreateWithinSphere> Definition = new("Position Within Sphere Random", ParticleOperatorId.PiPosition, false,
		ParticleOperatorUnpack.Begin<C_INIT_CreateWithinSphere>()
			.Float("distance_min", "0", (o, v) => o.RadiusMin = v)
			.Float("distance_max", "0", (o, v) => o.RadiusMax = v)
			.Vector3("distance_bias", "1 1 1", (o, v) => o.DistanceBias = v)
			.Vector3("distance_bias_absolute_value", "0 0 0", (o, v) => o.DistanceBiasAbs = v)
			.Bool("bias in local system", "0", (o, v) => o.LocalCoords = v)
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("speed_min", "0", (o, v) => o.SpeedMin = v)
			.Float("speed_max", "0", (o, v) => o.SpeedMax = v)
			.Float("speed_random_exponent", "1", (o, v) => o.SpeedRandExp = v)
			.Vector3("speed_in_local_coordinate_system_min", "0 0 0", (o, v) => o.LocalCoordinateSystemSpeedMin = v)
			.Vector3("speed_in_local_coordinate_system_max", "0 0 0", (o, v) => o.LocalCoordinateSystemSpeedMax = v)
			.Int("create in model", "0", (o, v) => o.CreateInModel = v)
			.Bool("randomly distribute to highest supplied Control Point", "0", (o, v) => o.UseHighestEndCP = v)
			.Float("randomly distribution growth time", "0", (o, v) => o.EndCPGrowthTime = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;

	public override ulong GetReadControlPointMask() {
		ulong mask = 1UL << (ControlPointNumber & 63);
		return UseHighestEndCP ? (ulong)-(long)mask : mask;
	}

	public override void InitParams(ParticleSystemDefinition def, Datamodel.Element element) {
		ControlPointNumber = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		HasDistanceBias = DistanceBias.X != 1.0f || DistanceBias.Y != 1.0f || DistanceBias.Z != 1.0f;
		HasDistanceBiasAbs = DistanceBiasAbs.X != 0.0f || DistanceBiasAbs.Y != 0.0f || DistanceBiasAbs.Z != 0.0f;
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);

			int currentControlPoint = ControlPointNumber;
			if (UseHighestEndCP) {
				float currentEndCP = EndCPGrowthTime != 0.0f ? Math.Min(particles.CurTime, EndCPGrowthTime) / EndCPGrowthTime : 1.0f;
				currentControlPoint = particles.RandomInt(ControlPointNumber, (int)MathF.Floor(particles.GetHighestControlPoint() * currentEndCP));
			}

			Vector3 randomPosition = default;
			Vector3 randomDirection = default;
			for (int tries = 10; ; ) {
				float length = particles.RandomVectorInUnitSphere(out randomPosition);

				if (HasDistanceBiasAbs) {
					if (DistanceBiasAbs.X != 0.0f)
						randomPosition.X = MathF.Abs(randomPosition.X);
					if (DistanceBiasAbs.Y != 0.0f)
						randomPosition.Y = MathF.Abs(randomPosition.Y);
					if (DistanceBiasAbs.Z != 0.0f)
						randomPosition.Z = MathF.Abs(randomPosition.Z);
				}

				randomPosition *= DistanceBias;
				randomDirection = randomPosition * (1.0f / MathF.Sqrt(randomPosition.LengthSquared() + 1e-10f));
				randomPosition = randomDirection * ((RadiusMax - RadiusMin) * length + RadiusMin);

				if (!HasDistanceBias || !LocalCoords) {
					particles.GetControlPointAtTime(currentControlPoint, creationTime, out Vector3 controlPoint);
					randomPosition += controlPoint;
				}
				else {
					particles.GetControlPointTransformAtTime(currentControlPoint, creationTime, out Matrix3x4 transform);
					MathLib.VectorTransform(in randomPosition, in transform, out randomPosition);
				}

				if (CreateInModel == 0)
					break;
				if (particles.Mgr!.Query().MovePointInsideControllingObject(particles, particles.ControlPoints[currentControlPoint].Object, ref randomPosition))
					break;
				if (--tries == 0)
					break;
			}

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, randomPosition);

			if ((attributeWriteMask & PARTICLE_ATTRIBUTE_PREV_XYZ_MASK) == 0)
				continue;

			Vector3 offset = Vector3.Zero;
			if (SpeedMax > 0.0f) {
				float randomSpeed = particles.RandomFloatExp(SpeedMin, SpeedMax, SpeedRandExp);
				offset -= randomSpeed * randomDirection;
			}

			ref readonly ParticleControlPoint cp = ref particles.ControlPoint(currentControlPoint);
			offset -= particles.RandomFloat(LocalCoordinateSystemSpeedMin.X, LocalCoordinateSystemSpeedMax.X) * cp.ForwardVector;
			offset -= particles.RandomFloat(LocalCoordinateSystemSpeedMin.Y, LocalCoordinateSystemSpeedMax.Y) * cp.RightVector;
			offset -= particles.RandomFloat(LocalCoordinateSystemSpeedMin.Z, LocalCoordinateSystemSpeedMax.Z) * cp.UpVector;

			offset *= particles.PreviousDt;
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, randomPosition + offset);
		}
	}
}

public sealed class C_INIT_RandomLifeTime : ParticleOperatorInstance
{
	public float LifetimeMin;
	public float LifetimeMax;
	public float LifetimeRandExponent;

	public static readonly ParticleOperatorDefinition<C_INIT_RandomLifeTime> Definition = new("Lifetime Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RandomLifeTime>()
			.Float("lifetime_min", "0", (o, v) => o.LifetimeMin = v)
			.Float("lifetime_max", "0", (o, v) => o.LifetimeMax = v)
			.Float("lifetime_random_exponent", "1", (o, v) => o.LifetimeRandExponent = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadAttributes() => 0;

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++)
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p) = particles.RandomFloatExp(LifetimeMin, LifetimeMax, LifetimeRandExponent);
	}
}
