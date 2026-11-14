using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;

using System.Runtime.InteropServices;
namespace Source.ShaderAPI.Gl46;
public unsafe class IndexBufferGl46 : IDisposable
{
	internal MaterialIndexFormat IndexFormat;
	internal int IndexCount;
	internal int Position;
	internal void* SysmemBuffer;
	internal int SysmemBufferStartBytes;
	internal int BufferSize;

	internal uint LockCount;
	internal bool Dynamic;
	internal bool Locked;
	internal bool Flush;
	internal bool ExternalMemory;
	internal bool SoftwareVertexProcessing;
	internal bool LateCreateShouldDiscard;
	internal bool LateCreate;
	internal IShaderUtil? ShaderUtil;

	int ibo = -1;
	int lastBufferSize = -1;
	int LockedPosition;
	PersistentBufferRing? ring;
	bool Written;

	public unsafe void RecomputeIBO() {
		if (Dynamic) {
			if (ring == null || BufferSize > lastBufferSize) {
				lastBufferSize = BufferSize;
				ring?.Dispose();
				ring = new PersistentBufferRing(BufferSize);
				Written = false;
			}
			return;
		}

		bool renderThreadSafe = ShaderUtil == null || ShaderUtil.IsRenderThreadSafe();

		// Create the IBO if it doesn't exist
		if (ibo == -1 && renderThreadSafe)
			ibo = (int)glCreateBuffer();

		// Deallocate if Sysmembuffer != null and we cant fit in what we already allocated.
		if (BufferSize > lastBufferSize) {
			if (SysmemBuffer != null) {
				NativeMemory.Free(SysmemBuffer);
				SysmemBuffer = null;
			}
			lastBufferSize = BufferSize;
			SysmemBuffer = NativeMemory.AllocZeroed((nuint)BufferSize);
			if (renderThreadSafe)
				glNamedBufferData((uint)ibo, BufferSize, null, Dynamic ? GL_DYNAMIC_DRAW : GL_STATIC_DRAW);
			else
				LateCreate = true;
		}

		if (SysmemBuffer == null) {
			Warning("WARNING: RecomputeIBO failure (OpenGL's not happy...)\n");
			Warning($"    OpenGL error code    : {glGetErrorName()}\n");
			Warning($"    Vertex buffer object : {ibo}\n");
			Warning($"    Attempted alloc size : {BufferSize}\n");
		}
	}

	public void FlushASAP() => Flush = true;

	public short* Lock(bool readOnly, int indexCount, out int startIndex, int firstIndex) {
		Assert(!Locked);

		bool discard = false;
		if (Dynamic) {
			if (Position == 0 || Flush || !HasEnoughRoom(indexCount)) {
				if (SysmemBuffer != null)
					LateCreateShouldDiscard = true;

				Flush = false;
				Position = 0;
				discard = true;
			}
		}
		else {

		}

		int position = Position;
		if (firstIndex >= 0)
			position = firstIndex;

		startIndex = position;
		if (Dynamic) {
			if (ring == null)
				RecomputeIBO();
			else if (discard && Written)
				ring.Advance();

			if (discard)
				Written = false;

			Locked = true;
			LockedPosition = position;
			return (short*)ring!.Memory + position;
		}

		if (SysmemBuffer == null) {
			RecomputeIBO();
		}
		else if (discard) {
			glNamedBufferData((uint)ibo, BufferSize, null, GL_DYNAMIC_DRAW);
		}
		Locked = true;
		LockedPosition = position;
		return (short*)SysmemBuffer + position;
	}

	public void Unlock(int indexCount) {
		if (!Locked)
			return;

		if (indexCount > 0) {
			if (Dynamic)
				Written = true;
			else if (!LateCreate && (ShaderUtil == null || ShaderUtil.IsRenderThreadSafe()))
				glNamedBufferSubData((uint)ibo, LockedPosition * 2, indexCount * 2, (short*)SysmemBuffer + LockedPosition);
			else
				LateCreate = true;
		}
		Position += indexCount;
		Locked = false;
	}

	public short* ModifyLock(int firstIndex, int indexCount, out int startIndex) {
		Assert(!Locked);

		startIndex = firstIndex;
		if (Dynamic) {
			if (ring == null)
				RecomputeIBO();

			Locked = true;
			return (short*)ring!.Memory + firstIndex;
		}

		if (SysmemBuffer == null)
			RecomputeIBO();

		Locked = true;
		return (short*)SysmemBuffer + firstIndex;
	}

	public void ModifyUnlock(int firstIndex, int indexCount) {
		if (!Locked)
			return;

		if (indexCount > 0) {
			if (Dynamic)
				Written = true;
			else if (!LateCreate && (ShaderUtil == null || ShaderUtil.IsRenderThreadSafe()))
				glNamedBufferSubData((uint)ibo, firstIndex * 2, indexCount * 2, (short*)SysmemBuffer + firstIndex);
			else
				LateCreate = true;
		}
		Locked = false;
	}

	internal bool HasEnoughRoom(int indices) {
		return indices + Position <= IndexCount;
	}

	public void Dispose() {
		ring?.Dispose();
		ring = null;
		if (ibo != -1) {
			Assert(SysmemBuffer != null);
			fixed (int* ugh = &ibo)
				glDeleteBuffers(1, (uint*)ugh);
			ibo = -1;
			SysmemBuffer = null;
		}
	}

	public IndexBufferGl46(int count, bool dynamic = false, IShaderUtil? shaderUtil = null) {
		ShaderUtil = shaderUtil;
		Position = 0;
		Locked = false;
		Flush = true;
		Dynamic = dynamic;
		ExternalMemory = false;
		LateCreateShouldDiscard = false;

		count += count % 2;
		IndexCount = count;

		BufferSize = sizeof(ushort) * IndexCount;

		RecomputeIBO();
	}

	internal void HandleLateCreation() {
		if (!LateCreate)
			return;

		if (ibo == -1)
			ibo = (int)glCreateBuffer();
		glNamedBufferData((uint)ibo, BufferSize, SysmemBuffer, GL_STATIC_DRAW);
		LateCreate = false;
	}

	internal uint IBO() => ring != null ? ring.Buffer : ibo > 0 ? (uint)ibo : throw new NullReferenceException("Index Buffer Object was null");
}
