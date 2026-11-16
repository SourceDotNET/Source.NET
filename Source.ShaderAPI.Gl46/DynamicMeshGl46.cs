using Source.Common.MaterialSystem;

namespace Source.ShaderAPI.Gl46;

public unsafe class DynamicMeshGl46 : MeshGl46
{
	bool HasDrawn;
	bool VertexOverride;
	bool IndexOverride;

	int TotalVertices;
	int TotalIndices;
	int FirstVertex;
	int FirstIndex;

	int BufferId;
	public void ResetVertexAndIndexCounts() {
		TotalVertices = TotalIndices = 0;
		FirstIndex = FirstVertex = -1;
		HasDrawn = false;
	}

	public override void PreLock() {
		if (HasDrawn) {
			ResetVertexAndIndexCounts();
		}
	}

	public override void MarkAsDrawn() {
		HasDrawn = true;
	}

	internal void OverrideVertexBuffer(VertexBufferGl46 vertexBuffer) {
		UseVertexBuffer(vertexBuffer);
		VertexOverride = true;
	}

	internal void OverrideIndexBuffer(IndexBufferGl46 indexBuffer) {
		UseIndexBuffer(indexBuffer);
		IndexOverride = true;
	}
	public override bool NeedsVertexFormatReset(VertexFormat fmt) {
		return VertexOverride || IndexOverride || base.NeedsVertexFormatReset(fmt);
	}
	public override bool HasEnoughRoom(int vertexCount, int indexCount) {
		if (ShaderDevice.IsDeactivated())
			return false;
		Assert(VertexBuffer != null);
		return VertexBuffer.HasEnoughRoom(vertexCount) && IndexBuffer.HasEnoughRoom(indexCount);
	}
	public override void LockMesh(int vertexCount, int indexCount, ref MeshDesc desc) {
		ShaderUtil.SyncMatrices();

		PreLock();

		if (VertexOverride) {
			vertexCount = 0;
		}

		if (IndexOverride) {
			indexCount = 0;
		}

		Lock(vertexCount, false, ref desc.Vertex);

		if (FirstVertex < 0)
			FirstVertex = desc.Vertex.FirstVertex;

		if (IndexOverride || HasFlexMesh())
			desc.Vertex.FirstVertex -= FirstVertex;

		if (Type != MaterialPrimitiveType.Points) {
			int firstIndex = Lock(false, -1, indexCount, ref desc.Index);
			if (FirstIndex < 0)
				FirstIndex = firstIndex;
		}
		else {
			desc.Index.Indices = ScratchIndexBuffer;
			desc.Index.IndexSize = 0;
		}

		base.Locked = true;
	}

	public void Init(int bufferId) {
		BufferId = bufferId;
	}

	public override void SetVertexFormat(VertexFormat format) {
		if (ShaderDevice.IsDeactivated())
			return;

		if (format != VertexFormat || VertexOverride || IndexOverride) {
			VertexFormat = format;
			UseVertexBuffer(MeshMgr.FindOrCreateVertexBuffer(BufferId, format));

			if (BufferId == 0)
				UseIndexBuffer(MeshMgr.GetDynamicIndexBuffer());

			VertexOverride = IndexOverride = false;
		}
	}
	public override void UnlockMesh(int vertexCount, int indexCount, ref MeshDesc desc) {
		TotalVertices += vertexCount;
		TotalIndices += indexCount;
		base.UnlockMesh(vertexCount, indexCount, ref desc);
	}
	public override void Draw(int firstIndex = -1, int indexCount = 0) {
		if (!ShaderUtil.OnDrawMesh(this, firstIndex, indexCount)) {
			MarkAsDrawn();
			return;
		}

		HasDrawn = true;

		if (IndexOverride || VertexOverride || TotalVertices > 0 && (TotalIndices > 0 || Type == MaterialPrimitiveType.Points || Type == MaterialPrimitiveType.InstancedQuads)) {
			Assert(!IsDrawing);

			HandleLateCreation();

			// only have a non-zero first vertex when we are using static indices
			int nFirstVertex = VertexOverride ? 0 : FirstVertex;
			int actualFirstVertex = IndexOverride ? nFirstVertex : 0;
			int nVertexOffsetInBytes = HasFlexMesh() ? nFirstVertex * MeshMgr.VertexFormatSize(GetVertexFormat()) : 0;
			int baseIndex = IndexOverride ? 0 : FirstIndex;

			// Overriding with the dynamic index buffer, preserve state!
			if (IndexOverride && IndexBuffer == MeshMgr.GetDynamicIndexBuffer()) {
				baseIndex = FirstIndex;
			}

			VertexFormat fmt = VertexOverride ? GetVertexFormat() : VertexFormat.Invalid;
			if (!SetRenderState(nVertexOffsetInBytes, actualFirstVertex, fmt))
				return;

			// Draws a portion of the mesh
			int numVertices = VertexOverride ? VertexBuffer.VertexCount : TotalVertices;
			if (firstIndex != -1 && indexCount != 0) {
				firstIndex += baseIndex;
			}
			else {
				firstIndex = baseIndex;
				if (IndexOverride) {
					indexCount = IndexBuffer.IndexCount;
					Assert(indexCount != 0);
				}
				else {
					indexCount = TotalIndices;

					if (Type == MaterialPrimitiveType.Points || Type == MaterialPrimitiveType.InstancedQuads)
						indexCount = TotalVertices;

					Assert(indexCount != 0);
				}
			}

			// Fix up nFirstVertex to indicate the first vertex used in the data
			if (!HasFlexMesh()) {
				actualFirstVertex = nFirstVertex - actualFirstVertex;
			}

			s_FirstVertex = (uint)actualFirstVertex;
			s_NumVertices = (uint)numVertices;

			PrimList* prim = stackalloc PrimList[1];
			prim->FirstIndex = firstIndex;
			prim->NumIndices = indexCount;
			Assert(indexCount != 0);
			s_Prims = prim;
			s_PrimsCount = 1;

			DrawMesh();

			s_Prims = null;
		}
	}
}
