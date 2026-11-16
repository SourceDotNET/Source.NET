using Datamodel;

using Source.Common;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.InteropServices;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public sealed class C_OP_RenderRope : ParticleOperatorInstance
{
	public int SubdivCount;
	public int ScaleCP1;
	public int ScaleCP2;
	public float TexelSizeInUnits;
	public float TextureScale;
	public float TextureScrollRate;
	public float TextureOffset;
	public float TStep;
	public bool ScaleByControlPointDistance;
	public bool ScaleScrollByControlPointDistance;
	public bool ScaleOffsetByControlPointDistance;
	public bool UsesCPScaling;

	public static readonly ParticleOperatorDefinition<C_OP_RenderRope> Definition = new("render_rope", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RenderRope>()
			.Int("subdivision_count", "3", (o, v) => o.SubdivCount = v)
			.Float("texel_size", "4.0f", (o, v) => o.TexelSizeInUnits = v)
			.Float("texture_scroll_rate", "0.0f", (o, v) => o.TextureScrollRate = v)
			.Float("texture_offset", "0.0f", (o, v) => o.TextureOffset = v)
			.Int("scale CP start", "-1", (o, v) => o.ScaleCP1 = v)
			.Int("scale CP end", "-1", (o, v) => o.ScaleCP2 = v)
			.Bool("scale texture by CP distance", "0", (o, v) => o.ScaleByControlPointDistance = v)
			.Bool("scale scroll by CP distance", "0", (o, v) => o.ScaleScrollByControlPointDistance = v)
			.Bool("scale offset by CP distance", "0", (o, v) => o.ScaleOffsetByControlPointDistance = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_RADIUS_MASK | PARTICLE_ATTRIBUTE_TINT_RGB_MASK | PARTICLE_ATTRIBUTE_ALPHA_MASK | PARTICLE_ATTRIBUTE_ALPHA2_MASK;
	public override ulong GetReadControlPointMask() => UsesCPScaling ? (1UL << (ScaleCP2 & 63)) | (1UL << (ScaleCP1 & 63)) : 0;
	public override int GetRequiredContextBytes() => SubdivCount * sizeof(float) + sizeof(float);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		if (SubdivCount < 1)
			SubdivCount = 1;
		if (TexelSizeInUnits <= 0.0f)
			TexelSizeInUnits = 1.0f;
		TStep = 1.0f / SubdivCount;

		UsesCPScaling = (ScaleByControlPointDistance || ScaleScrollByControlPointDistance || ScaleOffsetByControlPointDistance) && ScaleCP1 >= 0 && ScaleCP2 >= 0;
		ScaleCP1 = Math.Clamp(ScaleCP1, -1, MAX_PARTICLE_CONTROL_POINTS - 1);
		ScaleCP2 = Math.Clamp(ScaleCP2, -1, MAX_PARTICLE_CONTROL_POINTS - 1);
	}

	static Span<float> ContextFloats(Span<byte> context) => MemoryMarshal.Cast<byte, float>(context);

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		Span<float> ctx = ContextFloats(context);
		ctx[0] = 0.0f;
		for (int i = 0; i < SubdivCount; i++)
			ctx[1 + i] = (float)i / SubdivCount;

		IMaterial? material = particles.Def!.GetMaterial();
		TextureScale = material != null ? 1.0f / (material.GetMappingHeight() * TexelSizeInUnits) : 1.0f;
	}

	public override int GetParticlesToRender(ParticleCollection particles, Span<byte> context, int firstParticle, int remainingVertices, int remainingIndices, out int vertsUsed, out int indicesUsed) {
		int active = particles.ActiveParticles;
		if (firstParticle >= active - 1 || active < 2) {
			vertsUsed = 0;
			indicesUsed = 0;
			return 0;
		}

		int maxCount = Math.Min((remainingVertices - 2) / (SubdivCount * 2) + 1, remainingIndices / (SubdivCount * 6));
		int count = active - firstParticle;
		if (maxCount == count - 1)
			maxCount--;
		if (maxCount < count)
			count = maxCount;

		vertsUsed = (count * 2 - 2) * SubdivCount + 2;
		indicesUsed = count * SubdivCount * 6;
		return count;
	}

	float ControlPointDistance(ParticleCollection particles) => Vector3.Distance(particles.GetControlPointAtCurrentTime(ScaleCP1), particles.GetControlPointAtCurrentTime(ScaleCP2));

	public override void Render(IMatRenderContext renderContext, ParticleCollection particles, Span<byte> context) {
		IMaterial material = particles.Def!.GetMaterial()!;
		if (material.IsSpriteCard()) {
			RenderSpriteCard(renderContext, particles, material);
			return;
		}

		renderContext.Bind(material);
		int maxVertices = renderContext.GetMaxVerticesToRender(material);
		int maxIndices = renderContext.GetMaxIndicesToRender();

		int remaining = particles.ActiveParticles;
		int firstParticle = 0;
		while (remaining != 0) {
			int inBatch = GetParticlesToRender(particles, context, firstParticle, maxVertices, maxIndices, out int vertCount, out int indexCount);
			if (inBatch == 0)
				break;
			remaining -= inBatch;

			particles.Mgr!.TallyParticlesRendered(vertCount * 3, indexCount * 3);
			IMesh mesh = renderContext.GetDynamicMesh(true)!;
			MeshBuilder meshBuilder = new();
			meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, vertCount, indexCount);
			RenderUnsorted(particles, context, renderContext, ref meshBuilder, 0, firstParticle, inBatch);
			meshBuilder.End();
			mesh.Draw();

			firstParticle += inBatch;
		}
	}

	static float QuantizeColor(float value) => BuiltinParticleRenderers.UnitToByte(value) * (1.0f / 255.0f);

	static void GenerateSeg(ParticleCollection particles, int particle, ref BeamSeg seg) {
		seg.Pos = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle);
		Vector3 tint = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_TINT_RGB, particle);
		seg.Color = new(QuantizeColor(tint.X), QuantizeColor(tint.Y), QuantizeColor(tint.Z));
		seg.Alpha = QuantizeColor(particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA, particle) * particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA2, particle));
		seg.Width = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_RADIUS, particle);
	}

	public override void RenderUnsorted(ParticleCollection particles, Span<byte> context, IMatRenderContext renderContext, ref MeshBuilder meshBuilder, int vertexOffset, int firstParticle, int particleCount) {
		IMaterial material = particles.Def!.GetMaterial()!;
		if (material.IsSpriteCard())
			return;

		Span<float> ctx = ContextFloats(context);
		ref float renderedLength = ref ctx[0];
		Span<float> subdivList = ctx.Slice(1, SubdivCount);
		if (firstParticle == 0)
			renderedLength = 0.0f;

		float textureScale = TextureScale;
		float offsetScaled = TextureOffset;
		if (UsesCPScaling) {
			float dist = ControlPointDistance(particles);
			if (ScaleByControlPointDistance)
				textureScale = 1.0f / (dist * TexelSizeInUnits + 1.1920929e-07f);
			if (ScaleScrollByControlPointDistance)
				offsetScaled = offsetScaled / material.GetMappingHeight() * dist;
			if (ScaleOffsetByControlPointDistance)
				offsetScaled += TextureOffset / material.GetMappingHeight() * dist;
		}
		float texOffset = particles.CurTime * TextureScrollRate + offsetScaled;

		BeamSegDraw beam = new(ref meshBuilder, vertexOffset);
		beam.StartWithMeshBuilder(renderContext, (particleCount - 1) * SubdivCount + 1);

		Span<Vector3> catmullRom = stackalloc Vector3[4];
		Span<BeamSeg> seg = stackalloc BeamSeg[2];
		GenerateSeg(particles, firstParticle, ref seg[0]);
		seg[0].TexCoord = (texOffset + renderedLength) * textureScale;
		beam.NextSeg(ref seg[0]);

		catmullRom[1] = seg[0].Pos;
		catmullRom[0] = firstParticle == 0 ? catmullRom[1] : particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, firstParticle - 1);

		float ooSubdiv = 1.0f / SubdivCount;
		int particle = firstParticle + 1;
		for (int i = 1; i < particleCount; i++, particle++) {
			int cur = i & 1;
			int prev = 1 - cur;
			GenerateSeg(particles, particle, ref seg[cur]);
			renderedLength += Vector3.Distance(seg[cur].Pos, seg[prev].Pos);
			seg[cur].TexCoord = (renderedLength + texOffset) * textureScale;

			if (SubdivCount > 1) {
				catmullRom[(i + 1) & 3] = seg[cur].Pos;
				if (particle != particles.ActiveParticles - 1)
					catmullRom[(i + 2) & 3] = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle + 1);
				else
					catmullRom[(i + 2) & 3] = catmullRom[(i + 1) & 3];

				ref BeamSeg subdiv = ref seg[prev];
				Vector4 color = new(subdiv.Color, subdiv.Alpha);
				Vector4 colorInc = (new Vector4(seg[cur].Color, seg[cur].Alpha) - color) * ooSubdiv;
				float texCoordInc = (seg[cur].TexCoord - subdiv.TexCoord) * ooSubdiv;
				float widthInc = (seg[cur].Width - subdiv.Width) * ooSubdiv;
				for (int s = 1; s < SubdivCount; s++) {
					color += colorInc;
					subdiv.Color = new(QuantizeColor(color.X), QuantizeColor(color.Y), QuantizeColor(color.Z));
					subdiv.Alpha = QuantizeColor(color.W);
					subdiv.TexCoord += texCoordInc;
					subdiv.Width += widthInc;
					MathLib.Catmull_Rom_Spline(catmullRom[(i + 3) & 3], catmullRom[i & 3], catmullRom[(i + 1) & 3], catmullRom[(i + 2) & 3], subdivList[s], out subdiv.Pos);
					beam.NextSeg(ref subdiv);
				}
			}

			beam.NextSeg(ref seg[cur]);
		}

		beam.End();
	}

	struct SplineState
	{
		public Vector4 P0, P1, P2, P3;
		public byte R, G, B, A;
	}

	static void OutputSplineVerts(ref MeshBuilder meshBuilder, in SplineState state, float t, float u) {
		for (int side = 0; side < 2; side++) {
			meshBuilder.Color4ub(state.R, state.G, state.B, state.A);
			meshBuilder.Position3f(t, u, side);
			meshBuilder.TexCoord4f(0, state.P0.X, state.P0.Y, state.P0.Z, state.P0.W);
			meshBuilder.TexCoord4f(1, state.P1.X, state.P1.Y, state.P1.Z, state.P1.W);
			meshBuilder.TexCoord4f(2, state.P2.X, state.P2.Y, state.P2.Z, state.P2.W);
			meshBuilder.TexCoord4f(3, state.P3.X, state.P3.Y, state.P3.Z, state.P3.W);
			meshBuilder.AdvanceVertex();
		}
	}

	static void OutputSplineIndices(ref MeshBuilder meshBuilder, int index) {
		meshBuilder.FastIndex((ushort)index);
		meshBuilder.FastIndex((ushort)(index + 1));
		meshBuilder.FastIndex((ushort)(index + 2));
		meshBuilder.FastIndex((ushort)(index + 1));
		meshBuilder.FastIndex((ushort)(index + 3));
		meshBuilder.FastIndex((ushort)(index + 2));
	}

	static Vector4 PointRadius(ParticleCollection particles, int particle)
		=> new(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, particle), particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_RADIUS, particle));

	static void SetColor(ParticleCollection particles, int particle, ref SplineState state) {
		Vector3 tint = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_TINT_RGB, particle);
		state.R = BuiltinParticleRenderers.UnitToByte(tint.X);
		state.G = BuiltinParticleRenderers.UnitToByte(tint.Y);
		state.B = BuiltinParticleRenderers.UnitToByte(tint.Z);
		state.A = BuiltinParticleRenderers.UnitToByte(particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA, particle) * particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA2, particle));
	}

	void RenderSpriteCard(IMatRenderContext renderContext, ParticleCollection particles, IMaterial material) {
		int particleCount = particles.ActiveParticles;
		int segmentsToRender = particleCount - 1;
		if (segmentsToRender == 0)
			return;

		renderContext.Bind(material);
		float mappingHeight = material.GetMappingHeight();
		int maxVertices = renderContext.GetMaxVerticesToRender(material);
		int maxIndices = renderContext.GetMaxIndicesToRender();

		int vertsPerSegment = SubdivCount * 2;
		int indicesPerSegment = SubdivCount * 6;
		int segmentsPerBatch = Math.Min((maxVertices - 2) / vertsPerSegment, maxIndices / indicesPerSegment);
		int segmentsThisBatch = Math.Min(segmentsPerBatch, segmentsToRender);

		IMesh mesh = renderContext.GetDynamicMesh(true)!;

		float textureScale = TexelSizeInUnits;
		float scrollRate = TextureScrollRate;
		float offsetScaled = 1.0f / mappingHeight * TextureOffset;
		if (UsesCPScaling) {
			float dist = ControlPointDistance(particles);
			if (ScaleByControlPointDistance)
				textureScale = 1.0f / (dist * TexelSizeInUnits + 1.1920929e-07f);
			float distOverHeight = 1.0f / mappingHeight * dist;
			if (ScaleScrollByControlPointDistance)
				scrollRate = scrollRate * distOverHeight * textureScale;
			if (ScaleOffsetByControlPointDistance)
				offsetScaled *= distOverHeight + 1.0f;
		}
		float u = TextureOffset + scrollRate * particles.CurTime + offsetScaled;

		SplineState state = default;
		state.P1 = PointRadius(particles, 0);
		state.P2 = PointRadius(particles, 1);
		Vector4 delta = state.P2 - state.P1;
		state.P0 = state.P1 - delta;
		state.P3 = particleCount < 3 ? state.P2 + delta : PointRadius(particles, 2);
		SetColor(particles, 0, ref state);

		int vertexCount = segmentsThisBatch * vertsPerSegment + 2;
		int indexCount = segmentsThisBatch * indicesPerSegment;
		particles.Mgr!.TallyParticlesRendered(vertexCount * 3, indexCount * 3);
		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, vertexCount, indexCount);

		float duScale = TStep * TexelSizeInUnits;
		float t = 0.0f;
		int nextPoint = 3;
		int curIndex = 0;
		int available = segmentsThisBatch;
		bool firstPoint = true;

		do {
			if (available == 0) {
				meshBuilder.End();
				mesh.Draw();
				particles.Mgr!.TallyParticlesRendered(vertexCount * 3, indexCount * 3);
				meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, vertexCount, indexCount);
				OutputSplineVerts(ref meshBuilder, in state, t, u);
				available = segmentsThisBatch;
				curIndex = 0;
			}
			available--;

			t = 0.0f;
			float du = Vector3.Distance(new(state.P2.X, state.P2.Y, state.P2.Z), new(state.P1.X, state.P1.Y, state.P1.Z)) * duScale;
			for (int slice = 0; slice < SubdivCount; slice++) {
				OutputSplineVerts(ref meshBuilder, in state, t, u);
				u += du;
				t += TStep;
				if (!firstPoint) {
					OutputSplineIndices(ref meshBuilder, curIndex);
					curIndex += 2;
				}
				firstPoint = false;
			}

			if (segmentsToRender > 1) {
				state.P0 = state.P1;
				state.P1 = state.P2;
				state.P2 = state.P3;
				SetColor(particles, nextPoint - 2, ref state);
				if (nextPoint < particleCount) {
					state.P3 = PointRadius(particles, nextPoint);
					nextPoint++;
				}
				else
					state.P3 = state.P3 * 2.0f - state.P1;
			}
		} while (--segmentsToRender != 0);

		OutputSplineVerts(ref meshBuilder, in state, 1.0f, u);
		OutputSplineIndices(ref meshBuilder, curIndex);

		meshBuilder.End();
		mesh.Draw();
	}
}
