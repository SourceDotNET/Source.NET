using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public sealed class C_OP_RenderSpritesTrail : ParticleRenderOperatorInstance
{
	struct Context
	{
		public ParticleVisibilityData VisibilityData;
		public int QueryHandle;
	}

	public Vector4 FadeColor;
	public float AnimationRate;
	public float LengthFadeInTime;
	public float MaxLength;
	public float MinLength;
	public bool ConstrainRadius;
	public bool IgnoreDT;

	public static readonly ParticleOperatorDefinition<C_OP_RenderSpritesTrail> Definition = new("render_sprite_trail", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.BeginRender<C_OP_RenderSpritesTrail>()
			.Float("animation rate", ".1", (o, v) => o.AnimationRate = v)
			.Float("length fade in time", "0", (o, v) => o.LengthFadeInTime = v)
			.Float("max length", "2000", (o, v) => o.MaxLength = v)
			.Float("min length", "0", (o, v) => o.MinLength = v)
			.Bool("constrain radius to length", "1", (o, v) => o.ConstrainRadius = v)
			.Bool("ignore delta time", "0", (o, v) => o.IgnoreDT = v)
			.Vector4("tail color and alpha scale factor", "1 1 1 1", (o, v) => o.FadeColor = v));

	static ref Context Ctx(Span<byte> context) => ref MemoryMarshal.AsRef<Context>(context);

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_RADIUS_MASK | PARTICLE_ATTRIBUTE_TINT_RGB_MASK
		| PARTICLE_ATTRIBUTE_ALPHA_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER_MASK | PARTICLE_ATTRIBUTE_TRAIL_LENGTH_MASK;
	public override ulong GetReadControlPointMask() => VisibilityInputs.CPin < 0 ? 0 : 1UL << (VisibilityInputs.CPin & 63);
	public override int GetRequiredContextBytes() => Unsafe.SizeOf<Context>();

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		ctx.VisibilityData.UseVisibility = VisibilityInputs.CPin >= 0;
		ctx.VisibilityData.CameraBias = VisibilityInputs.CameraBias;
	}

	public override int GetParticlesToRender(ParticleCollection particles, Span<byte> context, int firstParticle, int remainingVertices, int remainingIndices, out int vertsUsed, out int indicesUsed) {
		int count = Math.Min(particles.ActiveParticles - firstParticle, Math.Min(remainingVertices / 4, remainingIndices / 6));
		vertsUsed = count * 4;
		indicesUsed = count * 6;
		return count;
	}

	float GetOODt(ParticleCollection particles) {
		if (IgnoreDT)
			return 1.0f;
		return particles.Dt != 0.0f ? 1.0f / particles.Dt : 1.0f;
	}

	static ref readonly SheetSequenceSample GetSample(ParticleCollection particles, float ageScale, int particle) {
		Sheet? sheet = particles.Sheet;
		if (sheet == null)
			return ref BuiltinParticleRenderers.DefaultSheetSequence;
		return ref BuiltinParticleRenderers.SampleOrDefault(in Sheet.GetSampleForSequence(sheet, particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, particle), particles.CurTime, ageScale, (int)particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER, particle)));
	}

	static void WriteQuadIndices(ref MeshBuilder meshBuilder, ref int vertexOffset) {
		meshBuilder.FastIndex((ushort)vertexOffset);
		meshBuilder.FastIndex((ushort)(vertexOffset + 1));
		meshBuilder.FastIndex((ushort)(vertexOffset + 2));
		meshBuilder.FastIndex((ushort)vertexOffset);
		meshBuilder.FastIndex((ushort)(vertexOffset + 2));
		meshBuilder.FastIndex((ushort)(vertexOffset + 3));
		vertexOffset += 4;
	}

	void RenderSpriteTrail(ref MeshBuilder meshBuilder, ParticleCollection particles, ref int vertexOffset, float ageScale, int particle, in Vector3 camera, float ooDt, in ParticleRenderData sortEntry) {
		byte alpha = sortEntry.Alpha;
		if (alpha == 0)
			return;

		Vector3 color = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_TINT_RGB, particle);
		float radius = sortEntry.Radius;
		ref readonly SequenceSampleTextureCoords coords = ref GetSample(particles, ageScale, particle).TextureCoordData[0];

		float age = particles.CurTime - particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, particle);
		float lengthScale = age >= LengthFadeInTime ? 1.0f : age / LengthFadeInTime;

		Vector3 position = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle);
		Vector3 delta = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, particle) - position;
		float mag = MathF.Sqrt(delta.LengthSquared());
		float length = lengthScale * particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_TRAIL_LENGTH, particle) * mag * ooDt;
		if (!(length > 0.0f))
			return;

		delta *= 1.0f / mag;
		float clamped = MaxLength;
		if (length <= MaxLength)
			clamped = length;
		if (clamped <= MinLength)
			clamped = MinLength;
		length = clamped;
		delta *= length;

		if (length < radius && ConstrainRadius)
			radius = length;

		Vector3 tangent = Vector3.Cross(position - camera, delta);
		MathLib.VectorNormalize(ref tangent);

		Vector3 v0 = position + tangent * (radius * 0.5f);
		Vector3 v1 = position + tangent * (radius * -0.5f);
		Vector3 v2 = v1 + delta;
		Vector3 v3 = v0 + delta;

		byte r = BuiltinParticleRenderers.UnitToByte(color.X);
		byte g = BuiltinParticleRenderers.UnitToByte(color.Y);
		byte b = BuiltinParticleRenderers.UnitToByte(color.Z);

		meshBuilder.Position3f(v0.X, v0.Y, v0.Z);
		meshBuilder.Color4ub(r, g, b, alpha);
		meshBuilder.TexCoord2f(0, coords.LeftU0, coords.BottomV0);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3f(v1.X, v1.Y, v1.Z);
		meshBuilder.Color4ub(r, g, b, alpha);
		meshBuilder.TexCoord2f(0, coords.RightU0, coords.BottomV0);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3f(v2.X, v2.Y, v2.Z);
		meshBuilder.Color4ub(r, g, b, alpha);
		meshBuilder.TexCoord2f(0, coords.RightU0, coords.TopV0);
		meshBuilder.AdvanceVertex();

		meshBuilder.Position3f(v3.X, v3.Y, v3.Z);
		meshBuilder.Color4ub(r, g, b, alpha);
		meshBuilder.TexCoord2f(0, coords.LeftU0, coords.TopV0);
		meshBuilder.AdvanceVertex();

		WriteQuadIndices(ref meshBuilder, ref vertexOffset);
	}

	static ReadOnlySpan<float> SplineCorners => [0, 1, 0, 0, 1, 1, 1, 0, 1, 1, 0, 0];

	bool RenderSpriteTrailSpriteCard(ref MeshBuilder meshBuilder, ParticleCollection particles, ref int vertexOffset, float ageScale, int particle, float ooDt, in ParticleRenderData sortEntry) {
		byte alpha = sortEntry.Alpha;
		if (alpha == 0)
			return false;

		Vector3 color = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_TINT_RGB, particle);
		float radius = sortEntry.Radius;
		ref readonly SequenceSampleTextureCoords coords = ref GetSample(particles, ageScale, particle).TextureCoordData[0];

		float age = particles.CurTime - particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, particle);
		float lengthScale = age / LengthFadeInTime;
		if (lengthScale > 1.0f)
			lengthScale = 1.0f;

		Vector3 position = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle);
		Vector3 delta = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, particle) - position;
		float magSquared = delta.LengthSquared();
		float invMag = magSquared == 0.0f ? 0.0f : 1.0f / MathF.Sqrt(magSquared);
		float length = invMag * particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_TRAIL_LENGTH, particle) * ooDt * lengthScale * magSquared;
		if (!(length > 0.0f))
			return false;

		float clamped = MaxLength;
		if (length <= MaxLength)
			clamped = length;
		if (clamped <= MinLength)
			clamped = MinLength;
		length = clamped;
		delta = delta * invMag * length;

		if (ConstrainRadius && length < radius)
			radius = length;

		Vector3 p0 = position - delta;
		Vector3 p1 = position;
		Vector3 p2 = position + delta;
		Vector3 p3 = position + delta * 2.0f;
		Vector4 fadeColor = new Vector4(color.X, color.Y, color.Z, alpha * (1.0f / 255.0f)) * FadeColor;

		byte r = BuiltinParticleRenderers.UnitToByte(color.X);
		byte g = BuiltinParticleRenderers.UnitToByte(color.Y);
		byte b = BuiltinParticleRenderers.UnitToByte(color.Z);
		bool hasFadeColor = meshBuilder.TextureCoordinateSize(5) != 0;

		ReadOnlySpan<float> corners = SplineCorners;
		for (int i = 0; i < 4; i++) {
			meshBuilder.Position3f(corners[i * 3], corners[i * 3 + 1], corners[i * 3 + 2]);
			meshBuilder.Color4ub(r, g, b, alpha);
			meshBuilder.TexCoord4f(0, p0.X, p0.Y, p0.Z, radius);
			meshBuilder.TexCoord4f(1, p1.X, p1.Y, p1.Z, radius);
			meshBuilder.TexCoord4f(2, p2.X, p2.Y, p2.Z, radius);
			meshBuilder.TexCoord4f(3, p3.X, p3.Y, p3.Z, radius);
			meshBuilder.TexCoord4f(4, coords.LeftU0, coords.TopV0, coords.RightU0, coords.BottomV0);
			if (hasFadeColor)
				meshBuilder.TexCoord4f(5, fadeColor.X, fadeColor.Y, fadeColor.Z, fadeColor.W);
			meshBuilder.AdvanceVertex();
		}

		WriteQuadIndices(ref meshBuilder, ref vertexOffset);
		return true;
	}

	public override void Render(IMatRenderContext renderContext, ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		IMaterial material = particles.Def!.GetMaterial()!;

		if (ctx.VisibilityData.UseVisibility)
			BuiltinParticleRenderers.SetupParticleVisibility(particles, ref ctx.VisibilityData, VisibilityInputs, ref ctx.QueryHandle);

		bool spriteCard = material.IsSpriteCard();
		renderContext.Bind(material);

		float ageScale = AnimationRate * 1024.0f;
		renderContext.GetWorldSpaceCameraPosition(out Vector3 camera);

		ParticleRenderData[] sortList = particles.GetRenderList(renderContext, true, out int particleCount, ctx.VisibilityData);
		int sortEnd = particleCount;

		int maxVertices = renderContext.GetMaxVerticesToRender(material);
		int maxIndices = renderContext.GetMaxIndicesToRender();
		int maxParticlesInBatch = Math.Min(maxVertices / 4, maxIndices / 6);
		float ooDt = GetOODt(particles);

		while (particleCount != 0) {
			int particlesInBatch = Math.Min(maxParticlesInBatch, particleCount);
			particleCount -= particlesInBatch;

			particles.Mgr!.TallyParticlesRendered(particlesInBatch * 4 * 3);
			IMesh mesh = renderContext.GetDynamicMesh(true)!;
			MeshBuilder meshBuilder = new();
			meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, particlesInBatch * 4, particlesInBatch * 6);

			int vertexOffset = 0;
			for (int i = 0; i < particlesInBatch; i++) {
				ref ParticleRenderData entry = ref sortList[--sortEnd];
				if (spriteCard)
					RenderSpriteTrailSpriteCard(ref meshBuilder, particles, ref vertexOffset, ageScale, entry.Index, ooDt, entry);
				else
					RenderSpriteTrail(ref meshBuilder, particles, ref vertexOffset, ageScale, entry.Index, camera, ooDt, entry);
			}

			meshBuilder.End();
			mesh.Draw();
		}
	}

	public override void RenderUnsorted(ParticleCollection particles, Span<byte> context, IMatRenderContext renderContext, ref MeshBuilder meshBuilder, int vertexOffset, int firstParticle, int particleCount) {
		ref Context ctx = ref Ctx(context);
		renderContext.GetWorldSpaceCameraPosition(out Vector3 camera);
		float ageScale = AnimationRate * 1024.0f;
		ParticleRenderData[] sortList = particles.GetRenderList(renderContext, false, out int sortCount, ctx.VisibilityData);
		float ooDt = GetOODt(particles);
		for (int i = 0; i < particleCount; i++)
			RenderSpriteTrail(ref meshBuilder, particles, ref vertexOffset, ageScale, firstParticle + i, camera, ooDt, sortList[sortCount]);
	}
}

public sealed class C_OP_RenderScreenVelocityRotate : ParticleRenderOperatorInstance
{
	struct Context
	{
		public ParticleVisibilityData VisibilityData;
		public int QueryHandle;
	}

	public float RotateRateDegrees;
	public float ForwardDegrees;

	public static readonly ParticleOperatorDefinition<C_OP_RenderScreenVelocityRotate> Definition = new("render_screen_velocity_rotate", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.BeginRender<C_OP_RenderScreenVelocityRotate>()
			.Float("rotate_rate(dps)", "0", (o, v) => o.RotateRateDegrees = v)
			.Float("forward_angle", "-90", (o, v) => o.ForwardDegrees = v));

	static ref Context Ctx(Span<byte> context) => ref MemoryMarshal.AsRef<Context>(context);

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_ROTATION_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_ROTATION_MASK;
	public override ulong GetReadControlPointMask() => VisibilityInputs.CPin < 0 ? 0 : 1UL << (VisibilityInputs.CPin & 63);
	public override int GetRequiredContextBytes() => Unsafe.SizeOf<Context>();

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		ctx.VisibilityData.UseVisibility = VisibilityInputs.CPin >= 0;
		ctx.VisibilityData.CameraBias = VisibilityInputs.CameraBias;
	}

	public override void Render(IMatRenderContext renderContext, ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		if (ctx.VisibilityData.UseVisibility)
			BuiltinParticleRenderers.SetupParticleVisibility(particles, ref ctx.VisibilityData, VisibilityInputs, ref ctx.QueryHandle);

		renderContext.GetMatrix(MaterialMatrixMode.View, out Matrix4x4 view);
		ParticleRenderData[] sortList = particles.GetRenderList(renderContext, false, out int particleCount, ctx.VisibilityData);
		float forwardRadians = ForwardDegrees * 0.017453292f;

		for (int i = 0, sortEnd = particleCount; i < particleCount; i++) {
			int particle = sortList[--sortEnd].Index;
			MathLib.Vector3DMultiplyPosition(in view, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle), out Vector3 viewPos);
			if (!float.IsFinite(viewPos.X))
				continue;
			MathLib.Vector3DMultiplyPosition(in view, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, particle), out Vector3 prevViewPos);
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_ROTATION, particle) = (float)(Math.Atan2(viewPos.Y - prevViewPos.Y, viewPos.X - prevViewPos.X) + forwardRadians);
		}
	}
}
