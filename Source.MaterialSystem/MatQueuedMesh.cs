using Source.Common.MaterialSystem;

using System.Runtime.InteropServices;

namespace Source.MaterialSystem;

public sealed unsafe class MatQueuedMesh : IMesh
{
	public const uint MQM_BUFFERED = 1 << 0;

	public struct MST_MeshInfo
	{
		public IMaterialInternal? Material;
		public VertexFormat VertexFormat;
		public uint Flags;
		public IMesh? VertexOverride;
		public IMesh? IndexOverride;
		public byte* VertexData;
		public int VertexCount;
		public int VertexSizeInBytes;
		public ushort* IndexData;
		public int IndexCount;
		public MaterialPrimitiveType PrimitiveType;
	}

	public struct MST_DrawInfo
	{
		public MaterialPrimitiveType Type;
		public int FirstIndex;
		public int IndexCount;
	}

	static readonly ushort* ScratchIndexBuffer = (ushort*)NativeMemory.AllocZeroed(6 * sizeof(ushort));

	readonly MatQueuedRenderContext Owner;
	readonly MatCallQueue CallQueue;
	readonly IMatRenderContextInternal HardwareContext;
	readonly bool FlexMesh;

	int MSTActualVertexOffsetInBytes;
	IMesh? MSTActualMesh;

	VertexFormat Format;
	byte* VertexData;
	ushort* IndexData;
	int Verts;
	int Indices;
	int VertexSize;
	uint Flags;
	bool CanSetAuxMeshes;
	MaterialPrimitiveType Type = MaterialPrimitiveType.Triangles;
	IMesh? VertexOverride;
	IMesh? IndexOverride;

	public MatQueuedMesh(MatQueuedRenderContext owner, IMatRenderContextInternal hardwareContext, bool flexMesh) {
		Owner = owner;
		CallQueue = owner.GetCallQueueInternal();
		HardwareContext = hardwareContext;
		FlexMesh = flexMesh;
	}

	public static IMesh? ResolveMesh(IMesh? mesh) => mesh is MatQueuedMesh queued ? queued.MSTActualMesh : mesh;

	public ushort* GetIndexData() => IndexData;
	public void InvalidateAuxMeshSet() => CanSetAuxMeshes = false;
	public int GetVertexSize() => VertexSize;

	public IMesh? MST_DetachActualMesh() {
		IMesh? mesh = MSTActualMesh;
		MSTActualMesh = null;
		return mesh;
	}
	public IMesh? MST_GetActualMesh() => MSTActualMesh;
	public int MST_GetActualVertexOffsetInBytes() => MSTActualVertexOffsetInBytes;

	static void MST_CopyDynamicVB(in MeshDesc desc, byte* vertexData, int sizeInBytes) {
		byte* dest;
		if (desc.Vertex.PositionSize != 0)
			dest = (byte*)desc.Vertex.Position;
		else {
			dest = (byte*)nuint.MaxValue;
			if (desc.Vertex.BoneWeightSize != 0 && (byte*)desc.Vertex.BoneWeight < dest) dest = (byte*)desc.Vertex.BoneWeight;
			if (desc.Vertex.BoneMatrixIndexSize != 0 && desc.Vertex.BoneMatrixIndex < dest) dest = desc.Vertex.BoneMatrixIndex;
			if (desc.Vertex.NormalSize != 0 && (byte*)desc.Vertex.Normal < dest) dest = (byte*)desc.Vertex.Normal;
			if (desc.Vertex.ColorSize != 0 && desc.Vertex.Color < dest) dest = desc.Vertex.Color;
			if (desc.Vertex.SpecularSize != 0 && desc.Vertex.Specular < dest) dest = desc.Vertex.Specular;
			if (desc.Vertex.TangentSSize != 0 && (byte*)desc.Vertex.TangentS < dest) dest = (byte*)desc.Vertex.TangentS;
			if (desc.Vertex.TangentTSize != 0 && (byte*)desc.Vertex.TangentT < dest) dest = (byte*)desc.Vertex.TangentT;
			if (desc.Vertex.WrinkleSize != 0 && (byte*)desc.Vertex.Wrinkle < dest) dest = (byte*)desc.Vertex.Wrinkle;
			if (desc.Vertex.UserDataSize != 0 && (byte*)desc.Vertex.UserData < dest) dest = (byte*)desc.Vertex.UserData;

			for (int i = 0; i < IMesh.VERTEX_MAX_TEXTURE_COORDINATES; i++) {
				byte* texCoord = i switch {
					0 => (byte*)desc.Vertex.TexCoord0,
					1 => (byte*)desc.Vertex.TexCoord1,
					2 => (byte*)desc.Vertex.TexCoord2,
					3 => (byte*)desc.Vertex.TexCoord3,
					4 => (byte*)desc.Vertex.TexCoord4,
					5 => (byte*)desc.Vertex.TexCoord5,
					6 => (byte*)desc.Vertex.TexCoord6,
					_ => (byte*)desc.Vertex.TexCoord7,
				};
				if (desc.Vertex.TexCoordSize[i] != 0 && texCoord < dest)
					dest = texCoord;
			}

			if (dest == (byte*)nuint.MaxValue)
				dest = null;
		}

		Assert(dest != null);
		if (dest != null)
			Buffer.MemoryCopy(vertexData, dest, sizeInBytes, sizeInBytes);
	}

	static void MST_CopyDynamicIB(in MeshDesc desc, ushort* indexData, int indexCount) {
		if (desc.Vertex.FirstVertex == 0) {
			Buffer.MemoryCopy(indexData, desc.Index.Indices, indexCount * sizeof(ushort), indexCount * sizeof(ushort));
			return;
		}

		int firstVertex = desc.Vertex.FirstVertex;
		ushort* dst = desc.Index.Indices;
		for (int i = 0; i < indexCount; i++)
			dst[i] = (ushort)(indexData[i] + firstVertex);
	}

	IMesh MST_SetupDynamicMesh(in MST_MeshInfo info) {
		int verticesToLock = info.VertexCount;
		int indicesToLock = info.IndexCount;

		IMesh dynamicMesh;
		if (!FlexMesh)
			dynamicMesh = HardwareContext.GetDynamicMeshEx(info.VertexFormat, (info.Flags & MQM_BUFFERED) != 0, info.VertexOverride, info.IndexOverride, info.Material)!;
		else
			dynamicMesh = HardwareContext.GetFlexMesh()!;

		if (verticesToLock == 0 && indicesToLock == 0)
			return dynamicMesh;

		dynamicMesh.SetPrimitiveType(info.PrimitiveType);

		MeshDesc desc = default;
		dynamicMesh.LockMesh(verticesToLock, indicesToLock, ref desc);
		MSTActualVertexOffsetInBytes = desc.Vertex.FirstVertex * desc.Vertex.ActualVertexSize;
		if (verticesToLock != 0 && desc.Vertex.ActualVertexSize != 0)
			MST_CopyDynamicVB(in desc, info.VertexData, info.VertexCount * info.VertexSizeInBytes);

		if (indicesToLock != 0 && info.IndexData != ScratchIndexBuffer && desc.Index.IndexSize != 0)
			MST_CopyDynamicIB(in desc, info.IndexData, info.IndexCount);

		dynamicMesh.UnlockMesh(verticesToLock, indicesToLock, ref desc);
		return dynamicMesh;
	}

	void MST_BuildDynamicBuffers(in MST_MeshInfo info) {
		Assert(info.VertexOverride != null || info.VertexCount != 0 || info.IndexOverride != null || info.IndexCount != 0);
		MSTActualMesh = MST_SetupDynamicMesh(in info);
	}

	void MST_Draw(in MST_DrawInfo info) {
		MSTActualMesh!.SetPrimitiveType(info.Type);
		MSTActualMesh.Draw(info.FirstIndex, info.IndexCount);
	}

	public bool OnGetDynamicMesh(VertexFormat vertexFormat, uint flags, IMesh? vertexOverride, IMesh? indexOverride, IMaterialInternal? material, int hwSkinBoneCount) {
		FreeBuffers();

		VertexOverride = vertexOverride;
		IndexOverride = indexOverride;
		Flags = flags;

		if (!FlexMesh) {
			if (vertexOverride != null)
				Format = vertexOverride.GetVertexFormat();
			else {
				VertexFormat materialFormat = material!.GetVertexFormat();
				Format = (vertexFormat != 0) ? vertexFormat : materialFormat;

				if (vertexFormat != 0) {
					int vertexFormatBoneWeights = vertexFormat.GetBoneWeightsSize();
					if (hwSkinBoneCount < vertexFormatBoneWeights)
						hwSkinBoneCount = vertexFormatBoneWeights;
				}

				Format &= ~(VertexFormat.BoneWeights1 | VertexFormat.BoneWeights2 | VertexFormat.BoneWeights3 | VertexFormat.BoneWeights4);
				if (hwSkinBoneCount > 0) {
					Format |= VertexExts.GetBoneWeight(2);
					Format |= VertexFormat.BoneIndex;
				}
			}
		}
		else
			Format = VertexFormat.Position | VertexFormat.Normal | VertexFormat.Wrinkle;

		MeshDesc temp = default;
		Owner.ShaderAPI.ComputeVertexDescription(default, Format, ref temp);
		VertexSize = temp.Vertex.ActualVertexSize;
		return true;
	}

	public int VertexCount() => VertexSize != 0 ? Verts : 0;
	public int IndexCount() => Indices;
	public VertexFormat GetVertexFormat() => Format;
	public bool IsDynamic() => true;
	public MaterialIndexFormat IndexFormat() {
		MatQueuedRenderContext.CannotSupport();
		return MaterialIndexFormat.x16;
	}

	public void BeginCastBuffer(VertexFormat format) => MatQueuedRenderContext.CannotSupport();
	public void BeginCastBuffer(MaterialIndexFormat format) => MatQueuedRenderContext.CannotSupport();
	public void EndCastBuffer() => MatQueuedRenderContext.CannotSupport();
	public int GetRoomRemaining() {
		MatQueuedRenderContext.CannotSupport();
		return 0;
	}

	public bool Lock(int vertexCount, bool append, ref VertexDesc desc) {
		MatQueuedRenderContext.CannotSupport();
		return false;
	}

	public bool Unlock(int vertexCount, ref VertexDesc desc) {
		MatQueuedRenderContext.CannotSupport();
		return false;
	}

	public int Lock(bool readOnly, int firstIndex, int indexCount, ref IndexDesc desc) {
		MatQueuedRenderContext.CannotSupport();
		return 0;
	}

	public bool Unlock(int writtenIndexCount, ref IndexDesc desc) {
		MatQueuedRenderContext.CannotSupport();
		return false;
	}

	public void ModifyBegin(int firstVertex, int vertexCount, int firstIndex, int indexCount, ref MeshDesc desc) => MatQueuedRenderContext.CannotSupport();
	public void ModifyEnd(ref MeshDesc desc) => MatQueuedRenderContext.CannotSupport();
	public void Draw(ReadOnlySpan<PrimList> lists, int numLists) => MatQueuedRenderContext.CannotSupport();

	public void SetPrimitiveType(MaterialPrimitiveType type) {
		Type = type;
	}

	public void LockMesh(int vertexCount, int indexCount, ref MeshDesc desc) {
		if (VertexOverride != null)
			vertexCount = 0;
		if (IndexOverride != null)
			indexCount = 0;

		Verts = vertexCount;
		Indices = indexCount;

		desc.Vertex.FirstVertex = 0;
		desc.Vertex.OffsetVertex = 0;
		if (vertexCount > 0) {
			Assert(VertexSize != 0);
			Assert(VertexData == null);
			VertexData = Owner.AllocVertices(vertexCount, VertexSize);
			Owner.ShaderAPI.ComputeVertexDescription(new Span<byte>(VertexData, vertexCount * VertexSize), Format, ref desc);
		}
		else
			Owner.ShaderAPI.ComputeVertexDescription(default, 0, ref desc);

		if (Type != MaterialPrimitiveType.Points && indexCount > 0) {
			Assert(IndexData == null);
			IndexData = Owner.AllocIndices(indexCount);
			desc.Index.Indices = IndexData;
			desc.Index.IndexSize = 1;
			desc.Index.FirstIndex = 0;
			desc.Index.OffsetIndex = 0;
		}
		else {
			desc.Index.Indices = ScratchIndexBuffer;
			desc.Index.IndexSize = 0;
			desc.Index.FirstIndex = 0;
			desc.Index.OffsetIndex = 0;
		}
	}

	public void UnlockMesh(int vertexCount, int indexCount, ref MeshDesc desc) {
		if (VertexData != null && vertexCount < Verts) {
			Owner.ReallocVertices(VertexData, Verts, vertexCount, VertexSize);
			Verts = vertexCount;
		}

		if (IndexData != null && indexCount < Indices) {
			Owner.ReallocIndices(IndexData, Indices, indexCount);
			Indices = indexCount;
		}

		QueueBuild();
	}

	public void QueueBuild() {
		if (VertexOverride == null && Verts == 0 && IndexOverride == null && Indices == 0)
			return;

		MST_MeshInfo info = new() {
			Material = Owner.GetCurrentMaterialInternal(),
			Flags = 0,
			VertexData = VertexData,
			VertexCount = Verts,
			VertexFormat = Format,
			VertexSizeInBytes = VertexSize,
			VertexOverride = VertexOverride,
			IndexData = IndexData,
			IndexCount = Indices,
			IndexOverride = IndexOverride,
			PrimitiveType = Type
		};
		Assert(info.IndexData != null || Indices == 0);

		CallQueue.QueueCall(static s => s.Mesh.MST_BuildDynamicBuffers(in s.Info), (Mesh: this, Info: info));
		CanSetAuxMeshes = true;
	}

	public void SetColorMesh(IMesh colorMesh, int vertexOffset) {
		Assert(!FlexMesh);
		if (!CanSetAuxMeshes)
			QueueBuild();
		CallQueue.QueueCall(static s => s.Mesh.MSTActualMesh?.SetColorMesh(ResolveMesh(s.ColorMesh)!, s.Offset), (Mesh: this, ColorMesh: colorMesh, Offset: vertexOffset));
	}

	public void SetFlexMesh(IMesh? mesh, int vertexOffset) {
		Assert(!FlexMesh);
		if (!CanSetAuxMeshes)
			QueueBuild();
		CallQueue.QueueCall(static s => s.Mesh.MSTActualMesh?.SetFlexMesh(ResolveMesh(s.FlexMesh), s.Offset), (Mesh: this, FlexMesh: mesh, Offset: vertexOffset));
	}

	public void DisableFlexMesh() {
		Assert(!FlexMesh);
		if (!CanSetAuxMeshes)
			QueueBuild();
		CallQueue.QueueCall(static mesh => mesh.MSTActualMesh?.DisableFlexMesh(), this);
	}

	public void Draw(int firstIndex = -1, int indexCount = 0) {
		if ((Verts == 0 && VertexOverride == null) && (Indices == 0 && IndexOverride == null)) {
			MarkAsDrawn();
			return;
		}

		if (Type == MaterialPrimitiveType.InstancedQuads || Type == MaterialPrimitiveType.Points) {
			if (Verts == 0 && VertexOverride == null) {
				MarkAsDrawn();
				return;
			}
		}

		if (!CanSetAuxMeshes)
			QueueBuild();

		MST_DrawInfo info = new() {
			Type = Type,
			FirstIndex = firstIndex,
			IndexCount = indexCount
		};
		CallQueue.QueueCall(static s => s.Mesh.MST_Draw(in s.Info), (Mesh: this, Info: info));
	}

	public void MarkAsDrawn() {
		if (CanSetAuxMeshes)
			CallQueue.QueueCall(static mesh => mesh.MSTActualMesh?.MarkAsDrawn(), this);
		FreeBuffers();
	}

	public void FreeBuffers() {
		Indices = 0;
		IndexData = null;
		Verts = 0;
		Format = 0;
		VertexSize = 0;
		VertexData = null;
		CanSetAuxMeshes = false;
		Flags = 0;
	}
}
