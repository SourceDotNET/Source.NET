using Source.Common.MaterialSystem;

using System.Runtime.InteropServices;

namespace Source.ShaderAPI.Gl46;

public unsafe class VertexBufferGl46 : IDisposable
{
	internal VertexFormat VertexBufferFormat;
	internal int Position;
	internal int VertexCount;
	internal int VertexSize;
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
	internal bool Written;

	int vbo = -1;
	PersistentBufferRing? ring;

	internal uint VBO() => ring != null ? ring.Buffer : vbo > 0 ? (uint)vbo : throw new NullReferenceException("Vertex Buffer Object was null");

	public VertexBufferGl46(bool dynamic) {
		Dynamic = dynamic;
	}

	public VertexBufferGl46(VertexFormat format, int vertexSize, int vertexCount, bool dynamic) {
		VertexBufferFormat = format;
		VertexSize = vertexSize;
		VertexCount = vertexCount;
		BufferSize = VertexSize * VertexCount;
		Dynamic = dynamic;
		Locked = false;
		Flush = true;
		ExternalMemory = false;
	}

	public void FlushASAP() => Flush = true;

	public enum OpenGL_ShaderInputAttribute
	{
		Position = 0,
		Normal = 1,
		Color = 2,
		Specular = 3,
		TangentS = 4,
		TangentT = 5,
		Wrinkle = 6,
		BoneIndex = 7,
		BoneWeights = 8,
		UserData = 9,
		TexCoord0 = 10,
		TexCoord1 = 11,
		TexCoord2 = 12,
		TexCoord3 = 13,
		TexCoord4 = 14,
		TexCoord5 = 15,
		TexCoord6 = 16,
		TexCoord7 = 17,
		Count,

		FlexPosition = TexCoord4,
		FlexNormal = TexCoord5

	}

	public static bool IsOn(OpenGL_ShaderInputAttribute shaderAttr, VertexFormat format, out int size, out VertexElement element) {
		switch (shaderAttr) {
			case OpenGL_ShaderInputAttribute.Position: size = 1; element = VertexElement.Position; return (format & VertexFormat.Position) != 0;
			case OpenGL_ShaderInputAttribute.Normal: size = 1; element = VertexElement.Normal; return (format & VertexFormat.Normal) != 0;
			case OpenGL_ShaderInputAttribute.Color: size = 1; element = VertexElement.Color; return (format & VertexFormat.Color) != 0;
			case OpenGL_ShaderInputAttribute.Specular: size = 1; element = VertexElement.Specular; return (format & VertexFormat.Specular) != 0;
			case OpenGL_ShaderInputAttribute.TangentS: size = 1; element = VertexElement.TangentS; return (format & VertexFormat.TangentS) != 0;
			case OpenGL_ShaderInputAttribute.TangentT: size = 1; element = VertexElement.TangentT; return (format & VertexFormat.TangentT) != 0;
			case OpenGL_ShaderInputAttribute.Wrinkle: size = 1; element = VertexElement.Wrinkle; return (format & VertexFormat.Wrinkle) != 0;
			case OpenGL_ShaderInputAttribute.BoneIndex: size = 1; element = VertexElement.BoneIndex; return (format & VertexFormat.BoneIndex) != 0;
			case OpenGL_ShaderInputAttribute.BoneWeights:
				int numBoneWeights = format.GetBoneWeightsSize();
				size = numBoneWeights;
				element = VertexElement.BoneWeights1 + (numBoneWeights - 1);
				return numBoneWeights > 0;
			case OpenGL_ShaderInputAttribute.UserData:
				int userDataSize = format.GetUserDataSize();
				size = userDataSize;

				element = VertexElement.UserData1 + (userDataSize - 1);
				return userDataSize > 0;
			case OpenGL_ShaderInputAttribute.TexCoord0:
			case OpenGL_ShaderInputAttribute.TexCoord1:
			case OpenGL_ShaderInputAttribute.TexCoord2:
			case OpenGL_ShaderInputAttribute.TexCoord3:
			case OpenGL_ShaderInputAttribute.TexCoord4:
			case OpenGL_ShaderInputAttribute.TexCoord5:
			case OpenGL_ShaderInputAttribute.TexCoord6:
			case OpenGL_ShaderInputAttribute.TexCoord7:
				int index = shaderAttr - OpenGL_ShaderInputAttribute.TexCoord0;
				int texCoordSize = format.GetTexCoordDimensionSize(index);
				size = texCoordSize;
				element = index switch {
					0 => texCoordSize switch { 0 => default, 1 => VertexElement.TexCoord1D_0, 2 => VertexElement.TexCoord2D_0, 3 => VertexElement.TexCoord3D_0, 4 => VertexElement.TexCoord4D_0, _ => throw new NotSupportedException() },
					1 => texCoordSize switch { 0 => default, 1 => VertexElement.TexCoord1D_1, 2 => VertexElement.TexCoord2D_1, 3 => VertexElement.TexCoord3D_1, 4 => VertexElement.TexCoord4D_1, _ => throw new NotSupportedException() },
					2 => texCoordSize switch { 0 => default, 1 => VertexElement.TexCoord1D_2, 2 => VertexElement.TexCoord2D_2, 3 => VertexElement.TexCoord3D_2, 4 => VertexElement.TexCoord4D_2, _ => throw new NotSupportedException() },
					3 => texCoordSize switch { 0 => default, 1 => VertexElement.TexCoord1D_3, 2 => VertexElement.TexCoord2D_3, 3 => VertexElement.TexCoord3D_3, 4 => VertexElement.TexCoord4D_3, _ => throw new NotSupportedException() },
					4 => texCoordSize switch { 0 => default, 1 => VertexElement.TexCoord1D_4, 2 => VertexElement.TexCoord2D_4, 3 => VertexElement.TexCoord3D_4, 4 => VertexElement.TexCoord4D_4, _ => throw new NotSupportedException() },
					5 => texCoordSize switch { 0 => default, 1 => VertexElement.TexCoord1D_5, 2 => VertexElement.TexCoord2D_5, 3 => VertexElement.TexCoord3D_5, 4 => VertexElement.TexCoord4D_5, _ => throw new NotSupportedException() },
					6 => texCoordSize switch { 0 => default, 1 => VertexElement.TexCoord1D_6, 2 => VertexElement.TexCoord2D_6, 3 => VertexElement.TexCoord3D_6, 4 => VertexElement.TexCoord4D_6, _ => throw new NotSupportedException() },
					7 => texCoordSize switch { 0 => default, 1 => VertexElement.TexCoord1D_7, 2 => VertexElement.TexCoord2D_7, 3 => VertexElement.TexCoord3D_7, 4 => VertexElement.TexCoord4D_7, _ => throw new NotSupportedException() },
					_ => throw new NotSupportedException()
				};
				return texCoordSize > 0;
			default: throw new NotSupportedException();
		}
	}

	public int NextLockOffset() {
		int nextOffset = VertexSize == 0 ? 0 : (Position + VertexSize - 1) / VertexSize;
		nextOffset *= VertexSize;
		return nextOffset;
	}

	internal void ChangeConfiguration(VertexFormat format, int vertexSize, int totalSize) {
		VertexBufferFormat = format;
		VertexSize = vertexSize;
		VertexCount = BufferSize / vertexSize;
		RecomputeVBO();
	}

	int lastBufferSize = -1;

	public void RecomputeVBO() {
		if (Dynamic) {
			if (BufferSize > lastBufferSize) {
				lastBufferSize = BufferSize;
				ring?.Dispose();
				ring = new PersistentBufferRing(BufferSize);
				Written = false;
			}
			return;
		}

		// Create the VBO if it doesn't exist
		if (vbo == -1)
			vbo = (int)glCreateBuffer();
		// Deallocate if Sysmembuffer != null and we cant fit in what we already allocated.
		if (BufferSize > lastBufferSize) {
			if (SysmemBuffer != null) {
				NativeMemory.Free(SysmemBuffer);
				SysmemBuffer = null;
			}
			lastBufferSize = BufferSize;
			SysmemBuffer = NativeMemory.AllocZeroed((nuint)BufferSize);
			glNamedBufferData((uint)vbo, BufferSize, null, Dynamic ? GL_DYNAMIC_DRAW : GL_STATIC_DRAW);
		}

	}

	public byte* Lock(int numVerts, out int baseVertexIndex) {
		Assert(!Locked);

		if (numVerts > VertexCount) {
			baseVertexIndex = 0;
			return null;
		}

		bool discard = false;
		if (Dynamic) {
			if (Position == 0 || Flush || !HasEnoughRoom(numVerts)) {
				if (SysmemBuffer != null)
					LateCreateShouldDiscard = true;

				Flush = false;
				Position = 0;
				discard = true;
			}
		}
		else {
			Position = 0;
		}

		int lockOffset = NextLockOffset();
		baseVertexIndex = VertexSize == 0 ? 0 : (lockOffset / VertexSize);
		if (Dynamic) {
			if (ring == null)
				RecomputeVBO();
			else if (discard && Written)
				ring.Advance();

			if (discard)
				Written = false;

			Locked = true;
			Position = lockOffset;
			return ring!.Memory + lockOffset;
		}

		if (SysmemBuffer == null)
			RecomputeVBO();

		Locked = true;
		Position = lockOffset;
		return (byte*)SysmemBuffer + lockOffset;
	}

	public void Unlock(int vertexCount) {
		if (!Locked)
			return;

		int lockOffset = NextLockOffset();
		int bufferSize = vertexCount * VertexSize;

		if (bufferSize > 0) {
			if (Dynamic)
				Written = true;
			else
				glNamedBufferSubData((uint)vbo, lockOffset, bufferSize, (byte*)SysmemBuffer + lockOffset);
		}
		Position = lockOffset + bufferSize;
		Locked = false;
	}

	int modifyOffset;

	public byte* ModifyLock(int firstVertex, int numVerts, out int baseVertexIndex) {
		Assert(!Locked);

		modifyOffset = firstVertex * VertexSize;
		baseVertexIndex = firstVertex;
		if (Dynamic) {
			if (ring == null)
				RecomputeVBO();

			Locked = true;
			return ring!.Memory + modifyOffset;
		}

		if (SysmemBuffer == null)
			RecomputeVBO();

		Locked = true;
		return (byte*)SysmemBuffer + modifyOffset;
	}

	public void ModifyUnlock(int vertexCount) {
		if (!Locked)
			return;

		if (vertexCount > 0) {
			if (Dynamic)
				Written = true;
			else
				glNamedBufferSubData((uint)vbo, modifyOffset, vertexCount * VertexSize, (byte*)SysmemBuffer + modifyOffset);
		}
		Locked = false;
	}

	internal bool HasEnoughRoom(int numVertices) {
		return NextLockOffset() + (numVertices * VertexSize) <= BufferSize;
	}

	unsafe static nint dummyData = (nint)NativeMemory.AlignedAlloc(512, 16);

	public static unsafe void ComputeVertexDescription(byte* vertexMemory, VertexFormat vertexFormat, ref VertexDesc desc) {
		desc.NumBoneWeights = vertexFormat.GetBoneWeightsSize();
		fixed (VertexDesc* descPtr = &desc) {
			nint offset = 0;
			nint baseptr = (nint)vertexMemory;
			int** vertexSizesToSet = stackalloc int*[64];
			int vertexSizesToSetPtr = 0;

			if ((vertexFormat & VertexFormat.Position) != 0) {
				descPtr->Position = (float*)(baseptr + offset);
				offset += VertexElement.Position.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->PositionSize;

				if ((vertexFormat & VertexFormat.Wrinkle) != 0) {
					descPtr->Wrinkle = (float*)(baseptr + offset);
					offset += VertexElement.Wrinkle.GetSize();
					vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->WrinkleSize;
				}
				else {
					descPtr->Wrinkle = (float*)dummyData;
					descPtr->WrinkleSize = 0;
				}
			}
			else {
				descPtr->Position = (float*)dummyData;
				descPtr->PositionSize = 0;
				descPtr->Wrinkle = (float*)dummyData;
				descPtr->WrinkleSize = 0;
			}

			if ((vertexFormat & VertexFormat.BoneIndex) != 0) {
				if (desc.NumBoneWeights > 0) {
					VertexElement boneWeightElement = VertexElement.BoneWeights1 + (desc.NumBoneWeights - 1);
					descPtr->BoneWeight = (float*)(baseptr + offset);
					offset += boneWeightElement.GetSize();
					vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->BoneWeightSize;
				}
				else {
					descPtr->BoneWeight = (float*)dummyData;
					descPtr->BoneWeightSize = 0;
				}

				descPtr->BoneMatrixIndex = (byte*)(baseptr + offset);
				offset += VertexElement.BoneIndex.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->BoneMatrixIndexSize;
			}
			else {
				descPtr->BoneMatrixIndex = (byte*)dummyData;
				descPtr->BoneMatrixIndexSize = 0;
			}

			if ((vertexFormat & VertexFormat.Normal) != 0) {
				descPtr->Normal = (float*)(baseptr + offset);
				offset += VertexElement.Normal.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->NormalSize;
			}
			else {
				descPtr->Normal = (float*)dummyData;
				descPtr->NormalSize = 0;
			}

			if ((vertexFormat & VertexFormat.Color) != 0) {
				descPtr->Color = (byte*)(baseptr + offset);
				offset += VertexElement.Color.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->ColorSize;
			}
			else {
				descPtr->Color = (byte*)dummyData;
				descPtr->ColorSize = 0;
			}

			if ((vertexFormat & VertexFormat.Specular) != 0) {
				descPtr->Specular = (byte*)(baseptr + offset);
				offset += VertexElement.Specular.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->SpecularSize;
			}
			else {
				descPtr->Specular = (byte*)dummyData;
				descPtr->SpecularSize = 0;
			}

			Span<VertexElement> texCoordElements = [VertexElement.TexCoord1D_0, VertexElement.TexCoord2D_0, VertexElement.TexCoord3D_0, VertexElement.TexCoord4D_0];
			for (int i = 0; i < IMesh.VERTEX_MAX_TEXTURE_COORDINATES; i++) {
				int size = (int)vertexFormat.GetTexCoordDimensionSize(i);
				if (size != 0) {
					desc.SetTexCoord(i, (float*)(baseptr + offset));
					offset += ((VertexElement)((int)texCoordElements[size - 1] + i)).GetSize();
					vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->TexCoordSize[i];
				}
				else {
					desc.SetTexCoord(i, (float*)dummyData);
					desc.TexCoordSize[i] = 0;
				}
			}

			if ((vertexFormat & VertexFormat.TangentS) != 0) {
				descPtr->TangentS = (float*)(baseptr + offset);
				offset += VertexElement.TangentS.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->TangentSSize;
			}
			else {
				descPtr->TangentS = (float*)dummyData;
				descPtr->TangentSSize = 0;
			}

			if ((vertexFormat & VertexFormat.TangentT) != 0) {
				descPtr->TangentT = (float*)(baseptr + offset);
				offset += VertexElement.TangentT.GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->TangentTSize;
			}
			else {
				descPtr->TangentT = (float*)dummyData;
				descPtr->TangentTSize = 0;
			}

			int userDataSize = (int)vertexFormat.GetUserDataSize();
			if (userDataSize > 0) {
				desc.UserData = (float*)(baseptr + offset);
				offset += (VertexElement.UserData1 + (userDataSize - 1)).GetSize();
				vertexSizesToSet[vertexSizesToSetPtr++] = &descPtr->UserDataSize;
			}
			else {
				descPtr->UserData = (float*)dummyData;
				descPtr->UserDataSize = 0;
			}

			desc.ActualVertexSize = (int)offset;
			for (int i = 0; i < vertexSizesToSetPtr; i++) {
				*vertexSizesToSet[i] = (int)offset;
			}
		}
	}

	public void Dispose() {
		if (ring != null) {
			ring.Dispose();
			ring = null;
			lastBufferSize = -1;
		}

		if (vbo != -1) {
			Assert(SysmemBuffer != null);
			fixed (int* ugh = &vbo)
				glDeleteBuffers(1, (uint*)ugh);
			vbo = -1;
			NativeMemory.Free(SysmemBuffer);
			SysmemBuffer = null;
		}
	}

	internal void HandleLateCreation() {

	}
}
