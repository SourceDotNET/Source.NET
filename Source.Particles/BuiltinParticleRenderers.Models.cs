using Datamodel;

using Source.Common.MaterialSystem;
using QAngle = Source.Common.Mathematics.QAngle;
using Source.Common.Mathematics;

using System.Numerics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public sealed class C_OP_RenderModels : ParticleRenderOperatorInstance
{
	public string ActivityName = "";
	public string ModelName = "";
	public bool OrientZ;
	public bool ScaleAnimationRate;
	public int AnimationScaleField;
	public int Skin;
	public float AnimationRate;
	public object? Model;
	public int Activity = -1;

	public static readonly ParticleOperatorDefinition<C_OP_RenderModels> Definition = new("Render models", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.BeginRender<C_OP_RenderModels>()
			.String("sequence 0 model", "NONE", 256, (o, v) => o.ModelName = v)
			.Float("animation rate", "30.0", (o, v) => o.AnimationRate = v)
			.Bool("scale animation rate", "0", (o, v) => o.ScaleAnimationRate = v)
			.Int("animation rate scale field", "10", (o, v) => o.AnimationScaleField = v)
			.Bool("orient model z to normal", "0", (o, v) => o.OrientZ = v)
			.Int("skin number", "0", (o, v) => o.Skin = v)
			.String("activity override", "", 256, (o, v) => o.ActivityName = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => (1u << (AnimationScaleField & 31)) | 0x3112d9;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		if (AnimationScaleField > 31)
			AnimationScaleField = 31;
	}

	public override void Precache(ParticleSystemDefinition def) {
		IParticleSystemQuery query = def.Mgr.Query();
		Model = query.GetModel(ModelName);
		Activity = ActivityName.Length != 0 ? query.GetActivityNumber(Model, ActivityName) : -1;
	}

	static void ComputeNormalBasis(Vector3 normal, out Vector3 v) {
		v = new((1.0f - normal.Z) * normal.X * normal.X + normal.Z, 0.0f, -normal.X);
		MathLib.VectorNormalize(ref v);
		v -= normal * Vector3.Dot(v, normal);
		MathLib.VectorNormalize(ref v);
	}

	public override void Render(IMatRenderContext renderContext, ParticleCollection particles, Span<byte> context) {
		ParticleVisibilityData visibility = new() { AlphaVisibility = 1.0f, RadiusVisibility = 1.0f };
		ParticleRenderData[] renderList = particles.GetRenderList(renderContext, false, out int count, visibility);

		IParticleSystemQuery query = particles.Mgr!.Query();
		query.SetUpLightingEnvironment(particles.Center);

		bool validScaleField = (uint)AnimationScaleField < 22;
		if (!validScaleField)
			Warning($"GetFloatAttributePtrForWrite invalid attribute {AnimationScaleField}\n");

		for (int i = count - 1; i >= 0; i--) {
			int particle = renderList[i].Index;

			int sequence = (int)particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER, particle);
			int activity = Activity == -1 ? (int)particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER1, particle) : Activity;

			float rate = AnimationRate;
			if (ScaleAnimationRate && validScaleField)
				rate *= particles.GetFloatAttributeValue(AnimationScaleField, particle);

			Vector3 normal = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_NORMAL, particle);
			MathLib.VectorNormalize(ref normal);
			ComputeNormalBasis(normal, out Vector3 v);
			Vector3 w = Vector3.Cross(normal, v);

			float scale = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_RADIUS, particle);
			Matrix3x4 basis = OrientZ
				? new(w.X * scale, -v.X * scale, normal.X * scale, 0, w.Y * scale, -v.Y * scale, normal.Y * scale, 0, w.Z * scale, -v.Z * scale, normal.Z * scale, 0)
				: new(normal.X * scale, v.X * scale, w.X * scale, 0, normal.Y * scale, v.Y * scale, w.Y * scale, 0, normal.Z * scale, v.Z * scale, w.Z * scale, 0);

			QAngle angles = new(
				particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_PITCH, particle) * 57.29578f,
				particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_YAW, particle) * 57.29578f,
				particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ROTATION, particle) * 57.29578f);
			MathLib.AngleMatrix(in angles, out Matrix3x4 angleMatrix);
			MathLib.ConcatTransforms(in basis, in angleMatrix, out Matrix3x4 drawMatrix);

			Vector3 position = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle);
			drawMatrix.M03 = position.X;
			drawMatrix.M13 = position.Y;
			drawMatrix.M23 = position.Z;

			Vector3 tint = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_TINT_RGB, particle);
			float alpha = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA, particle) * particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA2, particle);

			query.DrawModel(Model, in drawMatrix, particles, particle, sequence, 1, Skin, activity, rate, tint.X, tint.Y, tint.Z, alpha);
		}
	}
}
