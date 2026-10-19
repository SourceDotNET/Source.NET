using Source.Common;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;

using System.Numerics;

namespace Source.MaterialSystem;

public interface IMatRenderContextInternal : IMatRenderContext
{
	float GetFloatRenderingParameter(int parmNumber);
	int GetIntRenderingParameter(int parmNumber);
	Vector3 GetVectorRenderingParameter(int parmNumber);

	void SwapBuffers();

	void SetCurrentMaterialInternal(IMaterialInternal? currentMaterial);
	IMaterialInternal? GetCurrentMaterialInternal();
	int GetLightmapPage();
	void ForceDepthFuncEquals(bool force);

	bool InFlashlightMode();
	void BindStandardTexture(Sampler sampler, StandardTextureId id );
	void GetLightmapDimensions(out int w, out int h);
	MorphFormatFlags GetBoundMorphFormat();
	ITexture? GetRenderTargetEx(int renderTargetId);
	void DrawClearBufferQuad(byte r, byte g, byte b, byte a, bool clearColor, bool clearAlpha, bool clearDepth);

	bool OnDrawMesh(IMesh mesh, int firstIndex, int numIndices);
	unsafe bool OnDrawMesh(IMesh mesh, PrimList* pLists, int nLists);
	bool OnSetFlexMesh(IMesh staticMesh, IMesh? mesh, int nVertexOffsetInBytes);
	bool OnSetColorMesh(IMesh staticMesh, IMesh? mesh, int nVertexOffsetInBytes);
	bool OnSetPrimitiveType(IMesh mesh, MaterialPrimitiveType type);
	bool OnFlushBufferedPrimitives();

	void SyncMatrices();
	void SyncMatrix(MaterialMatrixMode mode);

	void ForceHardwareSync();
	void BeginFrame();
	void EndFrame();

	void SetFrameTime(TimeUnit_t frameTime);
	void SetCurrentProxy(object? proxy);
	void MarkRenderDataUnused(bool bBeginFrame);
	MatCallQueue? GetCallQueueInternal();

#if GMOD_DLL
	bool GMOD_IsLowOnMemory() { return false; }
#else
	// Map and unmap a texture. The pRecipient->OnAsyncMapComplete is called when complete. 
	void AsyncMap<T>(ITextureInternal texToMap, IAsyncTextureOperationReceiver<T> recipient, ref T extraArgs);
	void AsyncUnmap(ITextureInternal texToUnmap);
	
	// Copy from a render target to a staging texture, in order with other async commands.
	void AsyncCopyRenderTargetToStagingTexture<T>(ITexture dst, ITexture src, IAsyncTextureOperationReceiver<T> recipient, ref T extraArgs);
#endif
}
