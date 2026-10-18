using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Source.ShaderAPI.Gl46;

public struct PrimList
{
	public int FirstIndex;
	public int NumIndices;
}

public unsafe class MeshGl46 : IMesh
{
	public ShaderAPIGl46 ShaderAPI;
	public IShaderUtil ShaderUtil;
	public MeshMgr MeshMgr;
	public IShaderDevice ShaderDevice;

	protected VertexBufferGl46 VertexBuffer;
	protected IndexBufferGl46 IndexBuffer;

	protected VertexFormat LastVertexFormat;
	protected VertexFormat VertexFormat;
	protected IMaterialInternal Material;
	protected MaterialPrimitiveType Type = MaterialPrimitiveType.Triangles;
	protected bool IsDrawing;

	protected static PrimList* s_Prims;
	protected static int s_PrimsCount;
	protected static uint s_FirstVertex;
	protected static uint s_NumVertices;


	protected int Mode = ComputeMode(MaterialPrimitiveType.Triangles);
	public bool Locked;

	public VertexBufferGl46 GetVertexBuffer() => VertexBuffer;
	public IndexBufferGl46 GetIndexBuffer() => IndexBuffer;

	public virtual void BeginCastBuffer(VertexFormat format) {
		throw new NotImplementedException();
	}
	public void DrawMesh() {
		Assert(!IsDrawing);
		IsDrawing = true;

		ShaderAPI.DrawMesh(this);

		IsDrawing = false;
	}

	protected bool SetRenderState(int vertexOffsetInBytes, int firstIndex) => SetRenderState(vertexOffsetInBytes, firstIndex, VertexFormat.Invalid);
	protected virtual bool SetRenderState(int vertexOffsetInBytes, int firstIndex, VertexFormat vertexFormat) {
		if (ShaderDevice.IsDeactivated()) {
			ResetMeshRenderState();
			return false;
		}

		LastVertexFormat = vertexFormat;

		SetVertexStreamState(vertexOffsetInBytes);
		SetIndexStreamState(firstIndex);

		return true;
	}

	private void SetVertexStreamState(int vertexOffsetInBytes) {
		if (HasFlexMesh()) {
			Span<float> c = [1.0f, 1.0f, 0.0f, 0.0f];
			ShaderAPI.SetVertexShaderConstant(VertexShaderConst.FlexScale, c);
		}
		else {
			Assert(vertexOffsetInBytes == 0);

			Span<float> c = [0.0f, 0.0f, 0.0f, 0.0f];
			ShaderAPI.SetVertexShaderConstant(VertexShaderConst.FlexScale, c);
		}
	}

	int BaseVertexIndex;
	private void SetIndexStreamState(int firstVertexIdx) {
		BaseVertexIndex = firstVertexIdx;
	}

	private void ResetMeshRenderState() {
		throw new NotImplementedException();
	}

	public virtual void BeginCastBuffer(MaterialIndexFormat format) {
		throw new NotImplementedException();
	}

	public virtual unsafe void Draw(int firstIndex = -1, int indexCount = 0) {
		Assert(VertexBuffer != null);
		if (VertexBuffer == null)
			return;

		if (!ShaderUtil.OnDrawMesh(this, firstIndex, indexCount)) {
			MarkAsDrawn();
			return;
		}

		PrimList* primList = stackalloc PrimList[1];
		if (firstIndex == -1 || indexCount == 0) {
			primList->FirstIndex = 0;
			primList->NumIndices = NumIndices;
		}
		else {
			primList->FirstIndex = firstIndex;
			primList->NumIndices = indexCount;
		}
		DrawInternal(primList, 1);
	}

	public virtual void Draw(ReadOnlySpan<Source.Common.MaterialSystem.PrimList> lists, int numLists) {
		Assert(VertexBuffer != null);
		if (VertexBuffer == null)
			return;

		fixed (Source.Common.MaterialSystem.PrimList* lp = lists) {
			if (!ShaderUtil.OnDrawMesh(this, lp, numLists)) {
				MarkAsDrawn();
				return;
			}

			DrawInternal((PrimList*)lp, numLists);
		}
	}

	private unsafe void DrawInternal(PrimList* primList, int lists) {
		HandleLateCreation();

		int i;
		for (i = 0; i < lists; i++) {
			if (primList[i].NumIndices > 0)
				break;
		}

		if (i == lists)
			return;

		// Assert(ShaderAPI.IsInSelectionMode());

		if (!SetRenderState(0, 0))
			return;

		s_Prims = primList;
		s_PrimsCount = lists;

#if DEBUG
		for (i = 0; i < lists; ++i)
			Assert(primList[i].NumIndices > 0);
#endif

		s_FirstVertex = 0;
		s_NumVertices = (uint)VertexBuffer.VertexCount;

		DrawMesh();

		// Source doesn't seem to reset these. I don't know why it doesn't, but I can't see a good reason not to,
		// and considering this is almost always going to be a stack-backed pointer, I see a very good reason to do so
		s_Prims = null;
		s_PrimsCount = 0;
	}

	public virtual void EndCastBuffer() {
		throw new NotImplementedException();
	}

	public virtual int GetRoomRemaining() {
		throw new NotImplementedException();
	}

	public virtual VertexFormat GetVertexFormat() {
		return VertexFormat;
	}

	public virtual int IndexCount() {
		throw new NotImplementedException();
	}

	public virtual MaterialIndexFormat IndexFormat() {
		throw new NotImplementedException();
	}

	public virtual bool IsDynamic() {
		throw new NotImplementedException();
	}

	public void HandleLateCreation() {
		VertexBuffer?.HandleLateCreation();
		IndexBuffer?.HandleLateCreation();
	}

	public virtual bool Lock(int vertexCount, bool append, ref VertexDesc desc) {
		if (VertexBuffer == null) {
			int size = MeshMgr.VertexFormatSize(VertexFormat);
			VertexBuffer = new VertexBufferGl46(VertexFormat, size, vertexCount, false);
		}

		byte* vertexMemory = VertexBuffer.Lock(vertexCount, out desc.FirstVertex);
		VertexBufferGl46.ComputeVertexDescription(vertexMemory, VertexFormat, ref desc);

		return true;
	}

	public virtual int Lock(bool readOnly, int firstIndex, int indexCount, ref IndexDesc desc) {
		if (ShaderDevice.IsDeactivated() || indexCount == 0) {
			desc.Indices = ScratchIndexBuffer;
			desc.IndexSize = 0;
			return 0;
		}

		IndexBuffer ??= new IndexBufferGl46(indexCount, false);

		desc.Indices = (ushort*)IndexBuffer.Lock(readOnly, indexCount, out int startIndex, firstIndex);
		if (desc.Indices == null) {
			desc.IndexSize = 0;
			Assert(false);
			Warning("Failed to lock index buffer...\n");
			return 0;
		}

		desc.IndexSize = 1;
		IsIBLocked = true;
		return startIndex;
	}
	bool IsIBLocked;
	static readonly ushort* ScratchIndexBuffer = (ushort*)NativeMemory.Alloc(6 * sizeof(ushort));
	public virtual void LockMesh(int vertexCount, int indexCount, ref MeshDesc desc) {
		ShaderUtil.SyncMatrices();

		Lock(vertexCount, false, ref desc.Vertex);
		if (Type != MaterialPrimitiveType.Points)
			Lock(false, -1, indexCount, ref desc.Index);
		else {
			desc.Index.Indices = ScratchIndexBuffer;
			desc.Index.IndexSize = 0;
		}

		Locked = true;
	}

	public virtual void MarkAsDrawn() { }

	int modifyVertexCount;
	int modifyFirstIndex;
	int modifyIndexCount;

	public virtual void ModifyBegin(int firstVertex, int vertexCount, int firstIndex, int indexCount, ref MeshDesc desc) {
		Assert(VertexBuffer != null);

		byte* vertexMemory = VertexBuffer.ModifyLock(firstVertex, vertexCount, out desc.Vertex.FirstVertex);
		VertexBufferGl46.ComputeVertexDescription(vertexMemory, VertexFormat, ref desc.Vertex);

		if (indexCount > 0) {
			IndexBuffer ??= new IndexBufferGl46(indexCount, false);
			desc.Index.Indices = (ushort*)IndexBuffer.ModifyLock(firstIndex, indexCount, out int startIndex);
			desc.Index.IndexSize = 1;
			IsIBLocked = true;
		}
		else {
			desc.Index.Indices = ScratchIndexBuffer;
			desc.Index.IndexSize = 0;
		}

		modifyVertexCount = vertexCount;
		modifyFirstIndex = firstIndex;
		modifyIndexCount = indexCount;
		Locked = true;
	}

	public virtual void ModifyEnd(ref MeshDesc desc) {
		Assert(Locked);

		if (IsIBLocked) {
			IndexBuffer.ModifyUnlock(modifyFirstIndex, modifyIndexCount);
			IsIBLocked = false;
		}

		VertexBuffer.ModifyUnlock(modifyVertexCount);
		Locked = false;
	}

	IMesh? ColorMesh;
	int ColorMeshVertOffsetInBytes = 0;

	public virtual void SetColorMesh(IMesh colorMesh, int vertexOffset) {
		if (!ShaderUtil.OnSetColorMesh(this, colorMesh, vertexOffset))
			return;

		ColorMesh = colorMesh;
		ColorMeshVertOffsetInBytes = vertexOffset;
	}

	public virtual MaterialPrimitiveType GetPrimitiveType() {
		return Type;
	}

	public virtual void SetPrimitiveType(MaterialPrimitiveType type) {
		if (!ShaderUtil.OnSetPrimitiveType(this, type))
			return;

		Type = type;
		Mode = ComputeMode(type);
	}

	public virtual bool Unlock(int vertexCount, ref VertexDesc desc) {
		VertexBuffer.Unlock(vertexCount);
		return true;
	}

	public virtual bool Unlock(int indexCount, ref IndexDesc desc) {
		if (!IsIBLocked)
			return true;
		IndexBuffer.Unlock(indexCount);
		IsIBLocked = false;
		return true;
	}

	int NumVertices;
	int NumIndices;

	public virtual void UnlockMesh(int vertexCount, int indexCount, ref MeshDesc desc) {
		Assert(Locked);

		Unlock(vertexCount, ref desc.Vertex);
		if (Type != MaterialPrimitiveType.Points)
			Unlock(indexCount, ref desc.Index);

		NumVertices = vertexCount;
		NumIndices = indexCount;
		Locked = false;
	}

	public virtual int VertexCount() {
		return NumVertices;
	}

	public virtual void SetMaterial(IMaterialInternal matInternal) {
		Material = matInternal;
	}

	public virtual void SetVertexFormat(VertexFormat fmt) {
		VertexFormat = fmt;
	}

	public virtual unsafe void RenderPass() {
		HandleLateCreation();
		Assert(Type != MaterialPrimitiveType.Heterogenous);

		MeshGl46? colorMesh = (MeshGl46?)ColorMesh;

		bool bound = false;
		for (int iPrim = 0; iPrim < s_PrimsCount; iPrim++) {
			PrimList* pPrim = &s_Prims[iPrim];

			if (pPrim->NumIndices == 0)
				continue;

			if (Type == MaterialPrimitiveType.Points || Type == MaterialPrimitiveType.InstancedQuads) {
				throw new NotImplementedException();
			}
			else {
				int numPrimitives = NumPrimitives(s_NumVertices, pPrim->NumIndices);

				CheckIndices(pPrim, numPrimitives);
				if (!bound) {
					uint vao = ((ShaderAPIGl46)ShaderAPI).CurrentVertexDecl;
					glVertexArrayVertexBuffer(vao, 0, VertexBuffer!.VBO(), 0, VertexBuffer.VertexSize);
					if (colorMesh != null)
						glVertexArrayVertexBuffer(vao, 1, colorMesh.VertexBuffer!.VBO(), ColorMeshVertOffsetInBytes, colorMesh.VertexBuffer.VertexSize);

					if (FlexVertexBuffer != null)
						glVertexArrayVertexBuffer(vao, 2, FlexVertexBuffer.VBO(), FlexVertOffsetInBytes, FlexVertexBuffer.VertexSize);
					else
						glVertexArrayVertexBuffer(vao, 2, MeshMgr.GetZeroVertexBuffer(), 0, 4);

					glVertexArrayElementBuffer(vao, IndexBuffer!.IBO());
					bound = true;
				}
				glDrawElementsBaseVertex(Mode, pPrim->NumIndices, GL_UNSIGNED_SHORT, (void*)(pPrim->FirstIndex * 2), BaseVertexIndex);
			}
		}
	}

	public static int ComputeMode(MaterialPrimitiveType type) {
		switch (type) {
			case MaterialPrimitiveType.Points: return GL_POINTS;
			case MaterialPrimitiveType.Lines: return GL_LINES;
			case MaterialPrimitiveType.Triangles: return GL_TRIANGLES;
			case MaterialPrimitiveType.TriangleStrip: return GL_TRIANGLE_STRIP;
			case MaterialPrimitiveType.Heterogenous: return -1;
			default: throw new Exception();
		}
	}

	private int NumPrimitives(uint vertexCount, int indexCount) {
		switch (Mode) {
			case GL_POINTS: return (int)vertexCount;
			case GL_LINES: return indexCount / 2;
			case GL_TRIANGLES: return indexCount / 3;
			case GL_TRIANGLE_STRIP: return indexCount - 2;
			default: Assert(false); return 0;
		}
	}

	[Conditional("DEBUG")]
	private void CheckIndices(PrimList* pPrim, int numPrimitives) {
		int indexCount = 0;
		if (Mode == GL_TRIANGLES) {
			indexCount = numPrimitives * 3;
		}
		else if (Mode == GL_TRIANGLE_STRIP) {
			indexCount = numPrimitives + 2;
		}

		if (indexCount != 0) {
			// TODO: Should index buffer be global? Why the hell was it global before??
			Assert(pPrim->FirstIndex >= 0 && pPrim->FirstIndex < IndexBuffer.IndexCount);

			for (int j = 0; j < indexCount; j++) {
				// TODO
				//uint index = IndexBuffer.GetShadowIndex(j + pPrim->FirstIndex);

				//if (index >= s_FirstVertex && index < s_FirstVertex + s_NumVertices) {
				//	continue;
				//}

				//Assert(false);
			}
		}
	}

	internal bool HasColorMesh() {
		return ColorMesh != null;
	}

	internal bool HasFlexMesh() {
		return HasFlexVerts;
	}

	VertexBufferGl46? FlexVertexBuffer;
	int FlexVertOffsetInBytes;
	int FlexVertCount;
	bool HasFlexVerts;

	public virtual void SetFlexMesh(IMesh? mesh, int vertexOffsetInBytes) {
		if (!ShaderUtil.OnSetFlexMesh(this, mesh, vertexOffsetInBytes))
			return;

		FlexVertOffsetInBytes = vertexOffsetInBytes;

		if (mesh != null) {
			FlexVertCount = mesh.VertexCount();
			mesh.MarkAsDrawn();

			MeshGl46 baseMesh = (MeshGl46)mesh;
			FlexVertexBuffer = baseMesh.GetVertexBuffer();

			HasFlexVerts = true;
		}
		else {
			FlexVertCount = 0;
			FlexVertexBuffer = null;
			HasFlexVerts = false;
		}
	}

	public virtual void DisableFlexMesh() => SetFlexMesh(null, 0);

	public virtual bool NeedsVertexFormatReset(VertexFormat fmt) {
		return VertexFormat != fmt;
	}

	public virtual bool HasEnoughRoom(int vertexCount, int indexCount) => true;

	public virtual void PreLock() {
		throw new NotImplementedException();
	}

	public virtual void UseVertexBuffer(VertexBufferGl46 vertexBuffer) {
		VertexBuffer = vertexBuffer;
	}

	public virtual void UseIndexBuffer(IndexBufferGl46 indexBuffer) {
		IndexBuffer = indexBuffer;
	}
}
