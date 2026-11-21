using System.Numerics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public sealed class C_INIT_SetHitboxToClosest : ParticleOperatorInstance
{
	const int MAX_POINTS_PER_BATCH = 100;

	public int ControlPointNumber;
	public int DesiredHitbox;
	public float HitBoxScale;
	public string HitboxSetName = "effects";

	public static readonly ParticleOperatorDefinition<C_INIT_SetHitboxToClosest> Definition = new("Set Hitbox to Closest Hitbox", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_SetHitboxToClosest>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("model hitbox scale", "1.0", (o, v) => o.HitBoxScale = v)
			.Int("desired hitbox", "-1", (o, v) => o.DesiredHitbox = v)
			.String("hitbox set", "effects", 128, (o, v) => o.HitboxSetName = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_HITBOX_INDEX_MASK | PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ_MASK;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => 1UL << (ControlPointNumber & 63);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		int controlPoint = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		particles.UpdateHitBoxInfo(controlPoint, HitboxSetName);

		Span<Vector3> points = stackalloc Vector3[MAX_POINTS_PER_BATCH];
		Span<Vector3> relativeCoords = stackalloc Vector3[MAX_POINTS_PER_BATCH];
		Span<int> hitboxIndices = stackalloc int[MAX_POINTS_PER_BATCH];
		int particle = firstParticle;
		while (numParticles > 0) {
			int count = Math.Min(numParticles, MAX_POINTS_PER_BATCH);
			for (int i = 0; i < count; i++)
				points[i] = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle + i);

			particles.Mgr!.Query().GetClosestControllingObjectHitBox(particles, controlPoint, count, HitBoxScale, points, relativeCoords, hitboxIndices, DesiredHitbox, HitboxSetName);

			for (int i = 0; i < count; i++, particle++) {
				if ((attributeWriteMask & PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ_MASK) != 0)
					particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ, particle, relativeCoords[i]);
				if ((attributeWriteMask & PARTICLE_ATTRIBUTE_HITBOX_INDEX_MASK) != 0)
					particles.GetIntAttributeRefForWrite(PARTICLE_ATTRIBUTE_HITBOX_INDEX, particle) = hitboxIndices[i];
			}
			numParticles -= count;
		}
	}
}

public sealed class C_INIT_ModelCull : ParticleOperatorInstance
{
	public int ControlPointNumber;
	public bool BoundBox;
	public bool CullOutside;
	public string HitboxSetName = "effects";

	public static readonly ParticleOperatorDefinition<C_INIT_ModelCull> Definition = new("Cull relative to model", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_ModelCull>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Bool("use only bounding box", "0", (o, v) => o.BoundBox = v)
			.Bool("cull outside instead of inside", "0", (o, v) => o.CullOutside = v)
			.String("hitbox set", "effects", 128, (o, v) => o.HitboxSetName = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override ulong GetReadControlPointMask() => 1UL << (ControlPointNumber & 63);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		int controlPoint = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		particles.UpdateHitBoxInfo(controlPoint, HitboxSetName);
		if (!particles.ControlPointHitBoxes[controlPoint].CurAndPrevValid())
			return;

		IParticleSystemQuery query = particles.Mgr!.Query();
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			bool inside = query.IsPointInControllingObjectHitBox(particles, controlPoint, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p), BoundBox);
			if (inside != CullOutside)
				particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p) = -1.0f;
		}
	}
}

public sealed class C_INIT_SetHitboxToModel : ParticleOperatorInstance
{
	const int MAX_POINTS_PER_BATCH = 100;

	public int ControlPointNumber;
	public int ForceInModel;
	public int DesiredHitbox;
	public float HitBoxScale;
	public Vector3 DirectionBias;
	public bool MaintainHitbox;
	public string HitboxSetName = "effects";

	public static readonly ParticleOperatorDefinition<C_INIT_SetHitboxToModel> Definition = new("Set Hitbox Position on Model", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_SetHitboxToModel>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Int("force to be inside model", "0", (o, v) => o.ForceInModel = v)
			.Float("model hitbox scale", "1.0", (o, v) => o.HitBoxScale = v)
			.Vector3("direction bias", "0 0 0", (o, v) => o.DirectionBias = v)
			.Int("desired hitbox", "-1", (o, v) => o.DesiredHitbox = v)
			.String("hitbox set", "effects", 128, (o, v) => o.HitboxSetName = v)
			.Bool("maintain existing hitbox", "0", (o, v) => o.MaintainHitbox = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_HITBOX_INDEX_MASK | PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override ulong GetReadControlPointMask() => 1UL << (ControlPointNumber & 63);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		int controlPoint = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		particles.UpdateHitBoxInfo(controlPoint, HitboxSetName);

		Span<Vector3> points = stackalloc Vector3[MAX_POINTS_PER_BATCH];
		Span<Vector3> relativeCoords = stackalloc Vector3[MAX_POINTS_PER_BATCH];
		Span<int> hitboxIndices = stackalloc int[MAX_POINTS_PER_BATCH];
		int particle = firstParticle;
		while (numParticles > 0) {
			int count = Math.Min(numParticles, MAX_POINTS_PER_BATCH);
			int desiredHitbox = MaintainHitbox ? particles.GetIntAttributeValue(PARTICLE_ATTRIBUTE_HITBOX_INDEX, particle) : DesiredHitbox;
			points[..count].Clear();
			relativeCoords[..count].Clear();
			hitboxIndices[..count].Clear();

			particles.Mgr!.Query().GetRandomPointsOnControllingObjectHitBox(particles, controlPoint, count, HitBoxScale, ForceInModel, points, DirectionBias, relativeCoords, hitboxIndices, desiredHitbox, HitboxSetName);

			for (int i = 0; i < count; i++, particle++) {
				if ((attributeWriteMask & PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ_MASK) != 0)
					particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ, particle, relativeCoords[i]);
				if ((attributeWriteMask & PARTICLE_ATTRIBUTE_HITBOX_INDEX_MASK) != 0)
					particles.GetIntAttributeRefForWrite(PARTICLE_ATTRIBUTE_HITBOX_INDEX, particle) = hitboxIndices[i];
			}
			numParticles -= count;
		}
	}
}
