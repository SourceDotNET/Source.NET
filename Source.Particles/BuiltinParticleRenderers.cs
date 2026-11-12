using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public static class BuiltinParticleRenderers
{
	public static void AddBuiltInParticleRenderers(ParticleSystemMgr mgr) {
		mgr.AddParticleOperator(ParticleFunctionType.Renderer, C_OP_RenderSprites.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Renderer, C_OP_RenderSpritesTrail.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Renderer, C_OP_RenderScreenVelocityRotate.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Renderer, C_OP_RenderRope.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Renderer, C_OP_RenderModels.Definition);
	}

	internal static readonly SheetSequenceSample DefaultSheetSequence = CreateDefaultSheetSequence();

	static SheetSequenceSample CreateDefaultSheetSequence() {
		SheetSequenceSample sample = default;
		for (int i = 0; i < Sheet.MAX_IMAGES_PER_FRAME_IN_MEMORY; i++)
			sample.TextureCoordData[i] = new() { LeftU0 = 0, TopV0 = 0, RightU0 = 1, BottomV0 = 1, LeftU1 = 0, TopV1 = 0, RightU1 = 1, BottomV1 = 1 };
		sample.BlendFactor = 1.0f;
		return sample;
	}

	internal static ref readonly SheetSequenceSample SampleOrDefault(ref readonly SheetSequenceSample sample) {
		if (Unsafe.IsNullRef(in sample))
			return ref DefaultSheetSequence;
		return ref sample;
	}

	internal static byte UnitToByte(float value) => (byte)((int)MathF.Round(value * 255.0f) & 0xFF);

	internal static Vector3 ApplyCameraBias(Vector3 position, in Vector3 camera, float cameraBias) {
		if (cameraBias == 0.0f)
			return position;
		Vector3 delta = camera - position;
		float invLength = 1.0f / MathF.Sqrt(delta.LengthSquared() + 1e-10f);
		return position + delta * cameraBias * invLength;
	}

	internal static void SetupParticleVisibility(ParticleCollection particles, ref ParticleVisibilityData visibilityData, in ParticleVisibilityInputs inputs, ref int queryHandle) {
		float visibility = particles.Mgr!.Query().GetPixelVisibility(ref queryHandle, particles.GetControlPointAtCurrentTime(inputs.CPin), inputs.ProxyRadius);

		if (inputs.InputMax == inputs.InputMin) {
			bool max = inputs.InputMax <= visibility;
			visibilityData.AlphaVisibility = max ? inputs.AlphaScaleMax : inputs.AlphaScaleMin;
			visibilityData.RadiusVisibility = max ? inputs.RadiusScaleMax : inputs.RadiusScaleMin;
		}
		else {
			float fraction = Math.Clamp((visibility - inputs.InputMin) / (inputs.InputMax - inputs.InputMin), 0.0f, 1.0f);
			visibilityData.AlphaVisibility = inputs.AlphaScaleMin + (inputs.AlphaScaleMax - inputs.AlphaScaleMin) * fraction;
			visibilityData.RadiusVisibility = inputs.RadiusScaleMin + (inputs.RadiusScaleMax - inputs.RadiusScaleMin) * fraction;
		}

		visibilityData.CameraBias = inputs.CameraBias;
	}
}

struct SpriteRenderInfo
{
	public ParticleCollection Particles;
	public Sheet? Sheet;
	public float AgeScale;
	public float AgeScale2;
	public int VertexOffset;
}

public sealed class C_OP_RenderSprites : ParticleRenderOperatorInstance
{
	struct Context
	{
		public TokenCache OrientationVarToken;
		public TokenCache OrientationMatrixVarToken;
		public ParticleVisibilityData VisibilityData;
		public int QueryHandle;
	}

	public float AnimationRate;
	public bool FitCycleToLifetime;
	public int OrientationType;
	public int OrientationControlPoint;
	public float AnimationRate2;
	public bool AnimateInFPS;

	public static readonly ParticleOperatorDefinition<C_OP_RenderSprites> Definition = new("render_animated_sprites", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.BeginRender<C_OP_RenderSprites>()
			.Float("animation rate", ".1", (o, v) => o.AnimationRate = v)
			.Bool("animation_fit_lifetime", "0", (o, v) => o.FitCycleToLifetime = v)
			.Int("orientation_type", "0", (o, v) => o.OrientationType = v)
			.Int("orientation control point", "-1", (o, v) => o.OrientationControlPoint = v)
			.Float("second sequence animation rate", "0", (o, v) => o.AnimationRate2 = v)
			.Bool("use animation rate as FPS", "0", (o, v) => o.AnimateInFPS = v));

	static ref Context Ctx(Span<byte> context) => ref MemoryMarshal.AsRef<Context>(context);

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0x23db;
	public override ulong GetReadControlPointMask() => OrientationControlPoint < 0 ? 0 : 1UL << (OrientationControlPoint & 63);
	public override int GetRequiredContextBytes() => Unsafe.SizeOf<Context>();

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		ctx.OrientationVarToken = default;
		ctx.OrientationMatrixVarToken = default;
		ctx.VisibilityData.UseVisibility = VisibilityInputs.CPin >= 0;
		ctx.VisibilityData.CameraBias = VisibilityInputs.CameraBias;
	}

	public override int GetParticlesToRender(ParticleCollection particles, Span<byte> context, int firstParticle, int remainingVertices, int remainingIndices, out int vertsUsed, out int indicesUsed) {
		int count = Math.Min(Math.Min(particles.ActiveParticles - firstParticle, remainingIndices / 6), remainingVertices / 4);
		vertsUsed = count * 4;
		indicesUsed = count * 6;
		return count;
	}

	SpriteRenderInfo CreateRenderInfo(ParticleCollection particles) => new() {
		Particles = particles,
		Sheet = particles.Sheet,
		AgeScale = AnimationRate * 1024.0f,
		AgeScale2 = AnimationRate2 * 1024.0f,
		VertexOffset = 0
	};

	public override void Render(IMatRenderContext renderContext, ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		IMaterial material = particles.Def!.GetMaterial()!;

		if (ctx.VisibilityData.UseVisibility)
			BuiltinParticleRenderers.SetupParticleVisibility(particles, ref ctx.VisibilityData, VisibilityInputs, ref ctx.QueryHandle);

		material.FindVarFast("$orientation", ref ctx.OrientationVarToken)?.SetIntValue(OrientationType);

		renderContext.Bind(material);

		if (!material.IsSpriteCard()) {
			switch (OrientationType) {
				case 0: RenderNonSpriteCardCameraFacing(particles, ref ctx, renderContext, material); break;
				case 1: RenderNonSpriteCardZRotating(particles, ref ctx, renderContext, material); break;
				case 2: RenderNonSpriteCardOriented(particles, ref ctx, renderContext, material, false); break;
				case 3: RenderNonSpriteCardOriented(particles, ref ctx, renderContext, material, true); break;
			}
			return;
		}

		if (OrientationType == 2) {
			IMaterialVar? matrixVar = material.FindVarFast("$orientationMatrix", ref ctx.OrientationMatrixVarToken);
			if (matrixVar != null) {
				if (OrientationControlPoint < 0)
					matrixVar.SetMatrixValue(Matrix4x4.Identity);
				else {
					particles.GetControlPointTransformAtCurrentTime(OrientationControlPoint, out Matrix3x4 transform);
					matrixVar.SetMatrixValue((Matrix4x4)transform);
				}
			}
		}

		SpriteRenderInfo info = CreateRenderInfo(particles);

		int maxVertices = renderContext.GetMaxVerticesToRender(material);
		int maxIndices = renderContext.GetMaxIndicesToRender();
		int maxParticlesInBatch = Math.Min(maxVertices / 4, maxIndices / 6);

		ParticleRenderData[] sortList = particles.GetRenderList(renderContext, true, out int particleCount, ctx.VisibilityData);
		int sortEnd = particleCount;

		renderContext.GetWorldSpaceCameraPosition(out Vector3 camera);

		while (particleCount != 0) {
			int particlesInBatch = Math.Min(maxParticlesInBatch, particleCount);
			particleCount -= particlesInBatch;

			IMesh mesh = renderContext.GetDynamicMesh(true)!;
			particles.Mgr!.TallyParticlesRendered(particlesInBatch * 4 * 3);

			MeshBuilder meshBuilder = new();
			meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, particlesInBatch * 4, particlesInBatch * 6);
			info.VertexOffset = 0;

			bool twoSequence = meshBuilder.TextureCoordinateSize(5) != 0;
			for (int i = 0; i < particlesInBatch; i++) {
				ref ParticleRenderData entry = ref sortList[--sortEnd];
				if (twoSequence)
					RenderTwoSequenceSpriteCard(ref meshBuilder, ref ctx, ref info, entry.Index, entry, camera);
				else
					RenderSpriteCard(ref meshBuilder, ref ctx, ref info, entry.Index, entry, camera);
			}

			meshBuilder.End();
			mesh.Draw();
		}
	}

	public override void RenderUnsorted(ParticleCollection particles, Span<byte> context, IMatRenderContext renderContext, ref MeshBuilder meshBuilder, int vertexOffset, int firstParticle, int particleCount) {
		ref Context ctx = ref Ctx(context);
		IMaterial material = particles.Def!.GetMaterial()!;

		if (!material.IsSpriteCard()) {
			switch (OrientationType) {
				case 1: RenderUnsortedNonSpriteCardZRotating(particles, ref ctx, renderContext, ref meshBuilder, vertexOffset, firstParticle, particleCount); break;
				case 2: RenderUnsortedNonSpriteCardOriented(particles, ref ctx, renderContext, ref meshBuilder, vertexOffset, firstParticle, particleCount); break;
				case 0: Warning($"C_OP_RenderSprites::RenderUnsorted: Attempting to use an unimplemented sprite renderer for system \"{particles.Def.GetName()}\"!\n"); break;
			}
			return;
		}

		SpriteRenderInfo info = CreateRenderInfo(particles);
		ParticleRenderData[] sortList = particles.GetRenderList(renderContext, false, out int sortCount, ctx.VisibilityData);
		renderContext.GetWorldSpaceCameraPosition(out Vector3 camera);

		for (int i = 0; i < particleCount; i++)
			RenderSpriteCard(ref meshBuilder, ref ctx, ref info, firstParticle + i, sortList[sortCount], camera);
	}

	static void WriteQuadIndices(ref MeshBuilder meshBuilder, ref SpriteRenderInfo info) {
		int offset = info.VertexOffset;
		meshBuilder.FastIndex((ushort)offset);
		meshBuilder.FastIndex((ushort)(offset + 1));
		meshBuilder.FastIndex((ushort)(offset + 2));
		meshBuilder.FastIndex((ushort)offset);
		meshBuilder.FastIndex((ushort)(offset + 2));
		meshBuilder.FastIndex((ushort)(offset + 3));
		info.VertexOffset += 4;
	}

	static ReadOnlySpan<float> CornerIds => [0.0f, 0.0f, 1.0f, 0.0f, 1.0f, 1.0f, 0.0f, 1.0f];

	void RenderSpriteCard(ref MeshBuilder meshBuilder, ref Context ctx, ref SpriteRenderInfo info, int particle, in ParticleRenderData sortEntry, in Vector3 camera) {
		ParticleCollection particles = info.Particles;
		byte alpha = sortEntry.Alpha;

		Vector3 color = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_TINT_RGB, particle);
		byte r = BuiltinParticleRenderers.UnitToByte(color.X);
		byte g = BuiltinParticleRenderers.UnitToByte(color.Y);
		byte b = BuiltinParticleRenderers.UnitToByte(color.Z);
		float rotation = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ROTATION, particle);
		float radius = sortEntry.Radius;
		float yaw = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_YAW, particle);
		Vector3 position = BuiltinParticleRenderers.ApplyCameraBias(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle), camera, ctx.VisibilityData.CameraBias);

		ref readonly SheetSequenceSample sample = ref BuiltinParticleRenderers.DefaultSheetSequence;
		if (info.Sheet != null) {
			float ageScale = info.AgeScale;
			int sequence = (int)particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER, particle);
			if (AnimateInFPS)
				ageScale /= particles.Sheet!.FrameSpan[sequence];
			sample = ref BuiltinParticleRenderers.SampleOrDefault(in Sheet.GetSampleForSequence(info.Sheet, particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, particle), particles.CurTime, ageScale, sequence));
		}

		ref readonly SequenceSampleTextureCoords coords0 = ref sample.TextureCoordData[0];
		ref readonly SequenceSampleTextureCoords coords1 = ref sample.TextureCoordData[1];
		ReadOnlySpan<float> cornerIds = CornerIds;
		for (int corner = 0; corner < 4; corner++) {
			meshBuilder.Position3f(position.X, position.Y, position.Z);
			meshBuilder.Color4ub(r, g, b, alpha);
			meshBuilder.TexCoord4f(0, coords0.LeftU0, coords0.TopV0, coords0.RightU0, coords0.BottomV0);
			meshBuilder.TexCoord4f(1, coords0.LeftU1, coords0.TopV1, coords0.RightU1, coords0.BottomV1);
			meshBuilder.TexCoord4f(2, sample.BlendFactor, rotation, radius, yaw);
			meshBuilder.TexCoord2f(3, cornerIds[corner * 2], cornerIds[corner * 2 + 1]);
			meshBuilder.TexCoord4f(4, coords1.LeftU0, coords1.TopV0, coords1.RightU0, coords1.BottomV0);
			meshBuilder.AdvanceVertex();
		}

		WriteQuadIndices(ref meshBuilder, ref info);
	}

	void RenderTwoSequenceSpriteCard(ref MeshBuilder meshBuilder, ref Context ctx, ref SpriteRenderInfo info, int particle, in ParticleRenderData sortEntry, in Vector3 camera) {
		ParticleCollection particles = info.Particles;
		byte alpha = sortEntry.Alpha;
		float radius = sortEntry.Radius;

		Vector3 color = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_TINT_RGB, particle);
		byte r = BuiltinParticleRenderers.UnitToByte(color.X);
		byte g = BuiltinParticleRenderers.UnitToByte(color.Y);
		byte b = BuiltinParticleRenderers.UnitToByte(color.Z);
		float rotation = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ROTATION, particle);
		float yaw = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_YAW, particle);
		Vector3 position = BuiltinParticleRenderers.ApplyCameraBias(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle), camera, ctx.VisibilityData.CameraBias);

		ref readonly SheetSequenceSample sample = ref BuiltinParticleRenderers.DefaultSheetSequence;
		ref readonly SheetSequenceSample sample2 = ref BuiltinParticleRenderers.DefaultSheetSequence;
		if (info.Sheet != null) {
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, particle);
			sample = ref BuiltinParticleRenderers.SampleOrDefault(in Sheet.GetSampleForSequence(info.Sheet, creationTime, particles.CurTime, info.AgeScale, (int)particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER, particle)));
			sample2 = ref BuiltinParticleRenderers.SampleOrDefault(in Sheet.GetSampleForSequence(info.Sheet, creationTime, particles.CurTime, info.AgeScale2, (int)particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER1, particle)));
		}

		ref readonly SequenceSampleTextureCoords coords0 = ref sample.TextureCoordData[0];
		ref readonly SequenceSampleTextureCoords coords1 = ref sample.TextureCoordData[1];
		ref readonly SequenceSampleTextureCoords secondCoords = ref sample2.TextureCoordData[0];
		ReadOnlySpan<float> cornerIds = CornerIds;
		for (int corner = 0; corner < 4; corner++) {
			meshBuilder.Position3f(position.X, position.Y, position.Z);
			meshBuilder.Color4ub(r, g, b, alpha);
			meshBuilder.TexCoord4f(0, coords0.LeftU0, coords0.TopV0, coords0.RightU0, coords0.BottomV0);
			meshBuilder.TexCoord4f(1, coords0.LeftU1, coords0.TopV1, coords0.RightU1, coords0.BottomV1);
			meshBuilder.TexCoord4f(2, sample.BlendFactor, rotation, radius, yaw);
			meshBuilder.TexCoord2f(3, cornerIds[corner * 2], cornerIds[corner * 2 + 1]);
			meshBuilder.TexCoord4f(4, coords1.LeftU0, coords1.TopV0, coords1.RightU0, coords1.BottomV0);
			meshBuilder.TexCoord4f(5, secondCoords.LeftU0, secondCoords.TopV0, secondCoords.RightU0, secondCoords.BottomV0);
			meshBuilder.TexCoord4f(6, secondCoords.LeftU1, secondCoords.TopV1, secondCoords.RightU1, secondCoords.BottomV1);
			meshBuilder.TexCoord4f(7, sample2.BlendFactor, 0, 0, 0);
			meshBuilder.AdvanceVertex();
		}

		WriteQuadIndices(ref meshBuilder, ref info);
	}

	ref readonly SheetSequenceSample NonSpriteCardSample(ParticleCollection particles, Sheet? sheet, float ageScale, int particle) {
		if (sheet == null)
			return ref BuiltinParticleRenderers.DefaultSheetSequence;
		return ref BuiltinParticleRenderers.SampleOrDefault(in Sheet.GetSampleForSequence(sheet, particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, particle), particles.CurTime, ageScale, (int)particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER, particle)));
	}

	void RenderNonSpriteCardCameraFacing(ParticleCollection particles, ref Context ctx, IMatRenderContext renderContext, IMaterial material) {
		ParticleRenderData[] sortList = particles.GetRenderList(renderContext, true, out int particleCount, ctx.VisibilityData);
		int sortEnd = particleCount;

		float cameraBias = ctx.VisibilityData.CameraBias;
		renderContext.GetWorldSpaceCameraPosition(out Vector3 camera);

		renderContext.GetMatrix(MaterialMatrixMode.View, out Matrix4x4 tempView);
		renderContext.EnableUserClipTransformOverride(true);
		renderContext.UserClipTransform(tempView);
		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		int maxVertices = renderContext.GetMaxVerticesToRender(material);
		int maxIndices = renderContext.GetMaxIndicesToRender();
		int maxParticlesInBatch = Math.Min(maxVertices / 4, maxIndices / 6);
		Sheet? sheet = particles.Sheet;

		while (particleCount != 0) {
			int particlesInBatch = Math.Min(maxParticlesInBatch, particleCount);
			particleCount -= particlesInBatch;

			particles.Mgr!.TallyParticlesRendered(particlesInBatch * 4);
			IMesh mesh = renderContext.GetDynamicMesh(true)!;
			MeshBuilder meshBuilder = new();
			meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, particlesInBatch);

			for (int i = 0; i < particlesInBatch; i++) {
				ref ParticleRenderData entry = ref sortList[--sortEnd];
				byte alpha = entry.Alpha;
				if (alpha == 0)
					continue;

				int particle = entry.Index;
				float radius = entry.Radius;
				Vector3 color = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_TINT_RGB, particle);
				Vector3 worldPosition = BuiltinParticleRenderers.ApplyCameraBias(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle), camera, cameraBias);

				MathLib.Vector3DMultiplyPosition(in tempView, in worldPosition, out Vector3 viewPosition);
				if (!float.IsFinite(viewPosition.X))
					continue;

				float rotation = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ROTATION, particle);
				float ca = MathF.Cos(rotation);
				float sa = MathF.Sin(rotation);

				ref readonly SheetSequenceSample sample = ref BuiltinParticleRenderers.DefaultSheetSequence;
				if (sheet != null) {
					int sequence = (int)particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER, particle);
					float ageScale;
					if (FitCycleToLifetime) {
						float lifetime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_LIFE_DURATION, particle);
						ageScale = lifetime > 0.0f ? 1024.0f / lifetime : 0.0f;
					}
					else {
						ageScale = AnimationRate * 1024.0f;
						if (AnimateInFPS)
							ageScale /= sheet.FrameSpan[sequence];
					}
					sample = ref BuiltinParticleRenderers.SampleOrDefault(in Sheet.GetSampleForSequence(sheet, particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, particle), particles.CurTime, ageScale, sequence));
				}
				ref readonly SequenceSampleTextureCoords coords = ref sample.TextureCoordData[0];

				byte r = BuiltinParticleRenderers.UnitToByte(color.X);
				byte g = BuiltinParticleRenderers.UnitToByte(color.Y);
				byte b = BuiltinParticleRenderers.UnitToByte(color.Z);

				meshBuilder.Position3f(viewPosition.X + (-ca + sa) * radius, viewPosition.Y + (-sa - ca) * radius, viewPosition.Z);
				meshBuilder.Color4ub(r, g, b, alpha);
				meshBuilder.TexCoord2f(0, coords.LeftU0, coords.BottomV0);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(viewPosition.X + (-ca - sa) * radius, viewPosition.Y + (-sa + ca) * radius, viewPosition.Z);
				meshBuilder.Color4ub(r, g, b, alpha);
				meshBuilder.TexCoord2f(0, coords.LeftU0, coords.TopV0);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(viewPosition.X + (ca - sa) * radius, viewPosition.Y + (sa + ca) * radius, viewPosition.Z);
				meshBuilder.Color4ub(r, g, b, alpha);
				meshBuilder.TexCoord2f(0, coords.RightU0, coords.TopV0);
				meshBuilder.AdvanceVertex();

				meshBuilder.Position3f(viewPosition.X + (ca + sa) * radius, viewPosition.Y + (sa - ca) * radius, viewPosition.Z);
				meshBuilder.Color4ub(r, g, b, alpha);
				meshBuilder.TexCoord2f(0, coords.RightU0, coords.BottomV0);
				meshBuilder.AdvanceVertex();
			}

			meshBuilder.End();
			mesh.Draw();
		}

		renderContext.EnableUserClipTransformOverride(false);
		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PopMatrix();
	}

	static void EmitRotatedQuad(ref MeshBuilder meshBuilder, ref SpriteRenderInfo info, in Vector3 position, in Vector3 right, in Vector3 up, float a, float b, in SequenceSampleTextureCoords coords, bool oriented, byte red, byte green, byte blue, byte alpha) {
		Span<Vector2> corners = oriented
			? [new(a, b), new(-b, a), new(-a, -b), new(b, -a)]
			: [new(a, b), new(b, -a), new(-a, -b), new(-b, a)];
		Span<Vector2> texCoords = oriented
			? [new(coords.RightU0, coords.BottomV0), new(coords.RightU0, coords.TopV0), new(coords.LeftU0, coords.TopV0), new(coords.LeftU0, coords.BottomV0)]
			: [new(coords.LeftU0, coords.BottomV0), new(coords.LeftU0, coords.TopV0), new(coords.RightU0, coords.TopV0), new(coords.RightU0, coords.BottomV0)];

		for (int i = 0; i < 4; i++) {
			Vector3 vertex = position + right * corners[i].X + up * corners[i].Y;
			meshBuilder.Position3f(vertex.X, vertex.Y, vertex.Z);
			meshBuilder.Color4ub(red, green, blue, alpha);
			meshBuilder.TexCoord2f(0, texCoords[i].X, texCoords[i].Y);
			meshBuilder.AdvanceVertex();
		}

		WriteQuadIndices(ref meshBuilder, ref info);
	}

	void RenderNonSpriteCardZRotating(ref MeshBuilder meshBuilder, ref Context ctx, ref SpriteRenderInfo info, int particle, in Vector3 camera, in ParticleRenderData sortEntry) {
		byte alpha = sortEntry.Alpha;
		if (alpha == 0)
			return;

		ParticleCollection particles = info.Particles;
		float radius = sortEntry.Radius;
		Vector3 position = BuiltinParticleRenderers.ApplyCameraBias(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle), camera, ctx.VisibilityData.CameraBias);

		if (Vector3.Distance(position, camera) < radius * 0.5f)
			return;

		Vector3 color = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_TINT_RGB, particle);
		float rotation = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ROTATION, particle);

		float invLength = 1.0f / MathF.Sqrt(camera.Y * camera.Y + camera.X * camera.X + 1e-10f);
		Vector3 right = new Vector3(-camera.Y, camera.X, 0.0f) * invLength * radius;
		Vector3 up = new(0.0f, 0.0f, radius);

		ref readonly SheetSequenceSample sample = ref NonSpriteCardSample(particles, info.Sheet, info.AgeScale, particle);

		float ca = MathF.Cos(rotation);
		float sa = MathF.Sin(-rotation);
		EmitRotatedQuad(ref meshBuilder, ref info, position, right, up, -(ca + sa), sa - ca, sample.TextureCoordData[0], false,
			BuiltinParticleRenderers.UnitToByte(color.X), BuiltinParticleRenderers.UnitToByte(color.Y), BuiltinParticleRenderers.UnitToByte(color.Z), alpha);
	}

	void RenderNonSpriteCardOriented(ref MeshBuilder meshBuilder, ref Context ctx, ref SpriteRenderInfo info, int particle, in Vector3 camera, in ParticleRenderData sortEntry, bool useYaw) {
		byte alpha = sortEntry.Alpha;
		if (alpha == 0)
			return;

		ParticleCollection particles = info.Particles;
		float radius = sortEntry.Radius;
		Vector3 position = BuiltinParticleRenderers.ApplyCameraBias(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle), camera, ctx.VisibilityData.CameraBias);

		if (Vector3.Distance(position, camera) < radius * 0.5f)
			return;

		Vector3 color = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_TINT_RGB, particle);
		float rotation = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ROTATION, particle);

		Vector3 right, up;
		if (OrientationControlPoint < 0) {
			right = new(1.0f, 0.0f, 0.0f);
			up = new(0.0f, -1.0f, 0.0f);
		}
		else {
			ref readonly ParticleControlPoint cp = ref particles.ControlPoint(OrientationControlPoint);
			right = cp.ForwardVector;
			up = cp.RightVector;
		}

		if (useYaw) {
			float yaw = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_YAW, particle);
			if (yaw != 0.0f) {
				MathLib.MatrixBuildRotationAboutAxis(out Matrix4x4 rotationMatrix, up, yaw * (180.0f / MathF.PI));
				MathLib.Vector3DMultiply(in rotationMatrix, in right, out right);
			}
		}

		ref readonly SheetSequenceSample sample = ref NonSpriteCardSample(particles, info.Sheet, info.AgeScale, particle);

		float ca = MathF.Cos(rotation);
		float sa = MathF.Sin(-rotation);
		EmitRotatedQuad(ref meshBuilder, ref info, position, right * radius, up * radius, ca - sa, -(ca + sa), sample.TextureCoordData[0], true,
			BuiltinParticleRenderers.UnitToByte(color.X), BuiltinParticleRenderers.UnitToByte(color.Y), BuiltinParticleRenderers.UnitToByte(color.Z), alpha);
	}

	void RenderNonSpriteCardBatched(ParticleCollection particles, ref Context ctx, IMatRenderContext renderContext, IMaterial material, int orientation, bool useYaw) {
		renderContext.GetWorldSpaceCameraPosition(out Vector3 camera);

		SpriteRenderInfo info = CreateRenderInfo(particles);
		info.AgeScale2 = 0.0f;

		ParticleRenderData[] sortList = particles.GetRenderList(renderContext, true, out int particleCount, ctx.VisibilityData);
		int sortEnd = particleCount;

		int maxVertices = renderContext.GetMaxVerticesToRender(material);
		int maxIndices = renderContext.GetMaxIndicesToRender();
		int maxParticlesInBatch = Math.Min(maxVertices / 4, maxIndices / 6);

		while (particleCount != 0) {
			int particlesInBatch = Math.Min(maxParticlesInBatch, particleCount);
			particleCount -= particlesInBatch;

			particles.Mgr!.TallyParticlesRendered(particlesInBatch * 4 * 3);
			IMesh mesh = renderContext.GetDynamicMesh(true)!;
			MeshBuilder meshBuilder = new();
			meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, particlesInBatch * 4, particlesInBatch * 6);
			info.VertexOffset = 0;

			for (int i = 0; i < particlesInBatch; i++) {
				ref ParticleRenderData entry = ref sortList[--sortEnd];
				if (orientation == 1)
					RenderNonSpriteCardZRotating(ref meshBuilder, ref ctx, ref info, entry.Index, camera, entry);
				else
					RenderNonSpriteCardOriented(ref meshBuilder, ref ctx, ref info, entry.Index, camera, entry, useYaw);
			}

			meshBuilder.End();
			mesh.Draw();
		}
	}

	void RenderNonSpriteCardZRotating(ParticleCollection particles, ref Context ctx, IMatRenderContext renderContext, IMaterial material)
		=> RenderNonSpriteCardBatched(particles, ref ctx, renderContext, material, 1, false);

	void RenderNonSpriteCardOriented(ParticleCollection particles, ref Context ctx, IMatRenderContext renderContext, IMaterial material, bool useYaw)
		=> RenderNonSpriteCardBatched(particles, ref ctx, renderContext, material, 2, useYaw);

	void RenderUnsortedNonSpriteCardZRotating(ParticleCollection particles, ref Context ctx, IMatRenderContext renderContext, ref MeshBuilder meshBuilder, int vertexOffset, int firstParticle, int particleCount) {
		renderContext.GetWorldSpaceCameraPosition(out Vector3 camera);
		SpriteRenderInfo info = CreateRenderInfo(particles);
		info.VertexOffset = vertexOffset;
		ParticleRenderData[] sortList = particles.GetRenderList(renderContext, false, out int sortCount, ctx.VisibilityData);
		for (int i = 0; i < particleCount; i++)
			RenderNonSpriteCardZRotating(ref meshBuilder, ref ctx, ref info, firstParticle + i, camera, sortList[sortCount]);
	}

	void RenderUnsortedNonSpriteCardOriented(ParticleCollection particles, ref Context ctx, IMatRenderContext renderContext, ref MeshBuilder meshBuilder, int vertexOffset, int firstParticle, int particleCount) {
		renderContext.GetWorldSpaceCameraPosition(out Vector3 camera);
		SpriteRenderInfo info = CreateRenderInfo(particles);
		info.VertexOffset = vertexOffset;
		ParticleRenderData[] sortList = particles.GetRenderList(renderContext, false, out int sortCount, ctx.VisibilityData);
		for (int i = 0; i < particleCount; i++)
			RenderNonSpriteCardOriented(ref meshBuilder, ref ctx, ref info, firstParticle + i, camera, sortList[sortCount], false);
	}
}
