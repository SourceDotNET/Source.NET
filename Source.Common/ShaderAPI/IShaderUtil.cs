using Source.Common.MaterialSystem;

namespace Source.Common.ShaderAPI;

public interface IShaderUtil
{
	bool InFlashlightMode();
	bool OnDrawMesh(IMesh mesh, int firstIndex, int indexCount);
	unsafe bool OnDrawMesh(IMesh mesh, PrimList* lists, int numLists);
	bool OnSetFlexMesh(IMesh staticMesh, IMesh? mesh, int vertexOffsetInBytes);
	bool OnSetColorMesh(IMesh staticMesh, IMesh? mesh, int vertexOffsetInBytes);
	bool OnSetPrimitiveType(IMesh mesh, MaterialPrimitiveType type);
	bool OnFlushBufferedPrimitives();
	void SyncMatrices();
	void SyncMatrix(MaterialMatrixMode mode);
	void RestoreShaderObjects(IServiceProvider services, int changeFlags = 0);
	void BindStandardTexture(Sampler sampler, StandardTextureId id);
}
