using Source.Common.MaterialSystem;

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

	int ibo = -1;
	int lastBufferSize = -1;
	int LockedPosition;
	public unsafe void RecomputeIBO() {
		// Create the IBO if it doesn't exist
		if (ibo == -1)
			ibo = (int)glCreateBuffer();

		// Deallocate if Sysmembuffer != null and we cant fit in what we already allocated.
		if (BufferSize > lastBufferSize) {
			if (SysmemBuffer != null) {
				NativeMemory.Free(SysmemBuffer);
				SysmemBuffer = null;
			}
			lastBufferSize = BufferSize;
			SysmemBuffer = NativeMemory.AllocZeroed((nuint)BufferSize);
			glNamedBufferData((uint)ibo, BufferSize, null, Dynamic ? GL_DYNAMIC_DRAW : GL_STATIC_DRAW);

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

		if (indexCount > 0)
			glNamedBufferSubData((uint)ibo, LockedPosition * 2, indexCount * 2, (short*)SysmemBuffer + LockedPosition);
		Position += indexCount;
		Locked = false;
	}

	public short* ModifyLock(int firstIndex, int indexCount, out int startIndex) {
		Assert(!Locked);

		if (SysmemBuffer == null)
			RecomputeIBO();

		startIndex = firstIndex;
		Locked = true;
		return (short*)SysmemBuffer + firstIndex;
	}

	public void ModifyUnlock(int firstIndex, int indexCount) {
		if (!Locked)
			return;

		if (indexCount > 0)
			glNamedBufferSubData((uint)ibo, firstIndex * 2, indexCount * 2, (short*)SysmemBuffer + firstIndex);
		Locked = false;
	}

	internal bool HasEnoughRoom(int indices) {
		return indices + Position <= IndexCount;
	}

	public void Dispose() {
		if (ibo != -1) {
			Assert(SysmemBuffer != null);
			fixed (int* ugh = &ibo)
				glDeleteBuffers(1, (uint*)ugh);
			ibo = -1;
			SysmemBuffer = null;
		}
	}

	public IndexBufferGl46(int count, bool dynamic = false) {
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

	}

	internal uint IBO() => ibo > 0 ? (uint)ibo : throw new NullReferenceException("Index Buffer Object was null");
}
