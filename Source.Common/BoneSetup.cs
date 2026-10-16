using CommunityToolkit.HighPerformance;

using Source.Common.Commands;
using Source.Common.Mathematics;

using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.Common;

public class BoneSetupMemoryPool<T> where T : struct
{
	readonly ArrayPool<T> instance = ArrayPool<T>.Create();

	public T[] Alloc() => instance.Rent(Studio.MAXSTUDIOBONES);
	public void Free(T[] p) => instance.Return(p, true);
}

public ref struct BoneCacheParams
{
	public StudioHdr StudioHdr;
	public Span<Matrix3x4> BoneToWorld;
	public TimeUnit_t CurTime;
	public int BoneMask;
}

public readonly struct BoneCache
{
	public static BoneCache CreateResource(in BoneCacheParams parms) {
		Span<ushort> studioToCachedIndex = stackalloc ushort[Studio.MAXSTUDIOBONES];
		Span<ushort> cachedToStudioIndex = stackalloc ushort[Studio.MAXSTUDIOBONES];
		ushort cachedBoneCount = 0;
		ushort bonesCount = (ushort)Math.Clamp(parms.StudioHdr.NumBones(), 0, Studio.MAXSTUDIOBONES);
		for (ushort i = 0; i < bonesCount; i++) {
			// skip bones that aren't part of the boneMask (and aren't the root bone)
			if (i != 0 && 0 == (parms.StudioHdr.BoneFlags(i) & parms.BoneMask)) {
				studioToCachedIndex[i] = ushort.MaxValue;
				continue;
			}
			studioToCachedIndex[i] = cachedBoneCount;
			cachedToStudioIndex[cachedBoneCount] = i;
			cachedBoneCount++;
		}
		nint tableSizeStudio = sizeof(short) * bonesCount;
		nint tableSizeCached = sizeof(short) * cachedBoneCount;
		nint matrixSize = Unsafe.SizeOf<Matrix3x4>() * cachedBoneCount;
		nint size = (nint)((BoneCache_SIZE + tableSizeStudio + tableSizeCached + matrixSize + 3U) & ~3U);

		BoneCache mem = new(ArrayPool<byte>.Shared.Rent((int)size));
		mem.Construct(parms, (uint)size, studioToCachedIndex, cachedToStudioIndex, cachedBoneCount);
		return mem;
	}

	public static int EstimatedSize(in BoneCacheParams parms) => (parms.StudioHdr.NumBones() * (sizeof(short) + sizeof(short) + Unsafe.SizeOf<Matrix3x4>()) + 3) & ~3;

	public void DestroyResource() {
		ArrayPool<byte>.Shared.Return(backingMemory);
	}

	public BoneCache GetData() => this;
	public uint Size() => _Size;

	public bool IsNull() => backingMemory == null;

	public void Construct(in BoneCacheParams parms, uint size, Span<ushort> studioToCached, Span<ushort> cachedToStudio, ushort cachedBoneCount) {
		Array.Clear(backingMemory);
		CachedBoneCount = cachedBoneCount;
		_Size = size;
		TimeValid = parms.CurTime;
		BoneMask = parms.BoneMask;

		ushort studioTableSize = (ushort)((parms.StudioHdr.NumBones()) * sizeof(short));
		CachedToStudioOffset = studioTableSize;
		memcpy(StudioToCached(), studioToCached, studioTableSize);

		uint cachedTableSize = (uint)(cachedBoneCount * sizeof(short));
		memcpy(CachedToStudio(), cachedToStudio, (int)cachedTableSize);

		MatrixOffset = (ushort)((CachedToStudioOffset + cachedTableSize + 3U) & ~3U);

		UpdateBones(parms.BoneToWorld, parms.StudioHdr.NumBones(), parms.CurTime);
	}

	public void UpdateBones(ReadOnlySpan<Matrix3x4> boneToWorld, int numBones, TimeUnit_t curtime) {
		Span<Matrix3x4> bones = BoneArray();
		Span<ushort> cachedToStudio = CachedToStudio();

		for (ushort i = 0; i < CachedBoneCount; i++) {
			ushort index = cachedToStudio[i];
			MathLib.MatrixCopy(boneToWorld[index], out bones[i]);
		}
		TimeValid = curtime;
	}

	public ref Matrix3x4 GetCachedBone(int studioIndex) {
		ushort cachedIndex = StudioToCached()[studioIndex];
		if (cachedIndex != ushort.MaxValue)
			return ref BoneArray()[cachedIndex];

		return ref Unsafe.NullRef<Matrix3x4>();
	}

	public void ReadCachedBones(Span<Matrix3x4> boneToWorld) {
		Span<Matrix3x4> bones = BoneArray();
		Span<ushort> cachedToStudio = CachedToStudio();
		for (ushort i = 0; i < CachedBoneCount; i++)
			MathLib.MatrixCopy(bones[i], out boneToWorld[cachedToStudio[i]]);
	}

	public void ReadCachedBonePointers(out Span<Matrix3x4> bones, Span<int> boneIndices) {
		bones = BoneArray();
		memset(boneIndices, 0);
		Span<ushort> cachedToStudio = CachedToStudio();
		for (ushort i = 0; i < CachedBoneCount; i++)
			boneIndices[cachedToStudio[i]] = i;
	}

	public bool IsValid(TimeUnit_t curtime, TimeUnit_t dt = 0.1) {
		if (curtime - TimeValid <= dt)
			return true;
		return false;
	}

	private Span<Matrix3x4> BoneArray() {
		Span<byte> mem = backingMemory.AsSpan()[BoneCache_SIZE..][MatrixOffset..];
		int numMatrices = mem.Length / Unsafe.SizeOf<Matrix3x4>();
		return mem.Cast<byte, Matrix3x4>()[..numMatrices];
	}

	private Span<ushort> StudioToCached() {
		Span<byte> mem = backingMemory.AsSpan()[BoneCache_SIZE..];
		int numPtrs = mem.Length / Unsafe.SizeOf<ushort>();
		return mem.Cast<byte, ushort>()[..numPtrs];
	}

	private Span<ushort> CachedToStudio() {
		Span<byte> mem = backingMemory.AsSpan()[BoneCache_SIZE..][CachedToStudioOffset..];
		int numPtrs = mem.Length / Unsafe.SizeOf<ushort>();
		return mem.Cast<byte, ushort>()[..numPtrs];
	}

	private BoneCache(byte[] mem) => backingMemory = mem;
	readonly byte[] backingMemory;

	public ref TimeUnit_t TimeValid { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref MemoryMarshal.AsRef<double>(backingMemory.AsSpan(0, 8)); }
	public ref int BoneMask { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref MemoryMarshal.AsRef<int>(backingMemory.AsSpan(8, 4)); }
	private ref uint _Size { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref MemoryMarshal.AsRef<uint>(backingMemory.AsSpan(12, 4)); }
	private ref ushort CachedBoneCount { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref MemoryMarshal.AsRef<ushort>(backingMemory.AsSpan(16, 2)); }
	private ref ushort MatrixOffset { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref MemoryMarshal.AsRef<ushort>(backingMemory.AsSpan(18, 2)); }
	private ref ushort CachedToStudioOffset { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref MemoryMarshal.AsRef<ushort>(backingMemory.AsSpan(20, 2)); }

	public const int BoneCache_SIZE = 24;
}

public ref partial struct BoneSetup
{
	public static readonly ConVar anim_3wayblend = new("anim_3wayblend", "1", FCvar.Replicated, "Toggle the 3-way animation blending code.");

	static readonly BoneSetupMemoryPool<Vector3> VectorPool = new();
	static readonly BoneSetupMemoryPool<Quaternion> QuaternionPool = new();
	static readonly BoneSetupMemoryPool<Matrix3x4> MatrixPool = new();

	StudioHdr studioHdr;
	int boneMask;
	Span<float> poseParameter;

	public StudioHdr GetStudioHdr() => studioHdr;

	public BoneSetup(StudioHdr studioHdr, int boneMask, Span<float> poseParameter) {
		this.studioHdr = studioHdr;
		this.boneMask = boneMask;
		this.poseParameter = poseParameter;
	}

	public void InitPose(Span<Vector3> pos, Span<Quaternion> q) => InitPose(studioHdr, pos, q, boneMask);
	public void InitPose(StudioHdr studioHdr, Span<Vector3> pos, Span<Quaternion> q, int boneMask) {
		if (studioHdr.LinearBones() == null) {
			for (int i = 0; i < studioHdr.NumBones(); i++) {
				if ((studioHdr.BoneFlags(i) & boneMask) != 0) {
					MStudioBone pbone = studioHdr.Bone(i);
					pos[i] = pbone.Position;
					q[i] = pbone.Quat;
				}
			}
		}
		else {
			MStudioLinearBone pLinearBones = studioHdr.LinearBones()!;
			for (int i = 0; i < studioHdr.NumBones(); i++) {
				if ((studioHdr.BoneFlags(i) & boneMask) != 0) {
					pos[i] = pLinearBones.Pos(i);
					q[i] = pLinearBones.Quat(i);
				}
			}
		}
	}

	public unsafe void AccumulatePose(Span<Vector3> pos, Span<Quaternion> q, int sequence, TimeUnit_t cycle, float weight, TimeUnit_t time, IKContext? ikContext) {
		Span<Vector3> pos2 = stackalloc Vector3[Studio.MAXSTUDIOBONES];
		Span<Quaternion> q2 = stackalloc Quaternion[Studio.MAXSTUDIOBONES];

		Assert(weight >= 0.0f && weight <= 1.0f);
		// This shouldn't be necessary, but the Assert should help us catch whoever is screwing this up
		weight = Math.Clamp(weight, 0.0f, 1.0f);

		if (sequence < 0)
			return;

		MStudioSeqDesc seqdesc = studioHdr.Seqdesc(sequence);

		IKContext? seqIK = null;
		if (seqdesc.NumIKLocks != 0) {
			seqIK = new();
			seqIK.Init(studioHdr, vec3_angle, vec3_origin, 0.0, 0, boneMask);
			seqIK.AddSequenceLocks(seqdesc, pos, q);
		}

		if ((seqdesc.Flags & StudioAnimSeqFlags.Local) != 0)
			InitPose(studioHdr, pos2, q2, boneMask);

		if (CalcPoseSingle(studioHdr, pos2, q2, seqdesc, sequence, cycle, poseParameter, boneMask, time)) {
			AddLocalLayers(pos2, q2, seqdesc, sequence, cycle, 1.0, time, ikContext);
			SlerpBones(studioHdr, q, pos, seqdesc, sequence, q2, pos2, weight, boneMask);
		}

		ikContext?.AddDependencies(seqdesc, sequence, cycle, poseParameter, weight);

		AddSequenceLayers(pos, q, seqdesc, sequence, cycle, weight, time, ikContext);

		if (seqdesc.NumIKLocks != 0)
			seqIK!.SolveSequenceLocks(seqdesc, pos, q);
	}

	private static bool PoseIsAllZeros(StudioHdr studioHdr, int sequence, MStudioSeqDesc seqdesc, int i0, int i1) {
		int baseanim;

		baseanim = studioHdr.iRelativeAnim(sequence, seqdesc.Anim(i0, i1));
		MStudioAnimDesc anim = studioHdr.Animdesc(baseanim);
		return (anim.Flags & StudioAnimSeqFlags.AllZeros) != 0;
	}
	private static bool CalcPoseSingle(StudioHdr studioHdr, Span<Vector3> pos, Span<Quaternion> q, MStudioSeqDesc seqdesc, int sequence, double cycle, Span<float> poseParameter, int boneMask, double time) {
		bool bResult = true;

		Vector3[] pos2 = VectorPool.Alloc();
		Quaternion[] q2 = QuaternionPool.Alloc();
		Vector3[] pos3 = VectorPool.Alloc();
		Quaternion[] q3 = QuaternionPool.Alloc();

		if (sequence >= studioHdr.GetNumSeq()) {
			sequence = 0;
			seqdesc = studioHdr.Seqdesc(sequence);
		}

		int i0 = 0, i1 = 0;
		float s0 = 0, s1 = 0;

		Studio_LocalPoseParameter(studioHdr, poseParameter, seqdesc, sequence, 0, ref s0, ref i0);
		Studio_LocalPoseParameter(studioHdr, poseParameter, seqdesc, sequence, 1, ref s1, ref i1);

		if ((seqdesc.Flags & StudioAnimSeqFlags.Realtime) != 0) {
			float cps = Studio_CPS(studioHdr, seqdesc, sequence, poseParameter);
			cycle = time * cps;
			cycle = cycle - (int)cycle;
		}
		else if ((seqdesc.Flags & StudioAnimSeqFlags.CyclePose) != 0) {
			int iPose = studioHdr.GetSharedPoseParameter(sequence, seqdesc.CyclePoseIndex);
			if (iPose != -1) {
				cycle = poseParameter[iPose];
			}
			else {
				cycle = 0.0f;
			}
		}
		else if (cycle < 0 || cycle >= 1) {
			if ((seqdesc.Flags & StudioAnimSeqFlags.Looping) != 0) {
				cycle = cycle - (int)cycle;
				if (cycle < 0) cycle += 1;
			}
			else {
				cycle = Math.Clamp(cycle, 0.0, 1.0);
			}
		}

		if (s0 < 0.001) {
			if (s1 < 0.001) {
				if (PoseIsAllZeros(studioHdr, sequence, seqdesc, i0, i1)) {
					bResult = false;
				}
				else {
					CalcAnimation(studioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1), cycle, boneMask);
				}
			}
			else if (s1 > 0.999) {
				CalcAnimation(studioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1 + 1), cycle, boneMask);
			}
			else {
				CalcAnimation(studioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1), cycle, boneMask);
				CalcAnimation(studioHdr, pos2, q2, seqdesc, sequence, seqdesc.Anim(i0, i1 + 1), cycle, boneMask);
				BlendBones(studioHdr, q, pos, seqdesc, sequence, q2, pos2, s1, boneMask);
			}
		}
		else if (s0 > 0.999) {
			if (s1 < 0.001) {
				if (PoseIsAllZeros(studioHdr, sequence, seqdesc, i0 + 1, i1)) {
					bResult = false;
				}
				else {
					CalcAnimation(studioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1), cycle, boneMask);
				}
			}
			else if (s1 > 0.999) {
				CalcAnimation(studioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1 + 1), cycle, boneMask);
			}
			else {
				CalcAnimation(studioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1), cycle, boneMask);
				CalcAnimation(studioHdr, pos2, q2, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1 + 1), cycle, boneMask);
				BlendBones(studioHdr, q, pos, seqdesc, sequence, q2, pos2, s1, boneMask);
			}
		}
		else {
			if (s1 < 0.001) {
				if (PoseIsAllZeros(studioHdr, sequence, seqdesc, i0 + 1, i1)) {
					CalcAnimation(studioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1), cycle, boneMask);
					ScaleBones(studioHdr, q, pos, sequence, 1.0f - s0, boneMask);
				}
				else if (PoseIsAllZeros(studioHdr, sequence, seqdesc, i0, i1)) {
					CalcAnimation(studioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1), cycle, boneMask);
					ScaleBones(studioHdr, q, pos, sequence, s0, boneMask);
				}
				else {
					CalcAnimation(studioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1), cycle, boneMask);
					CalcAnimation(studioHdr, pos2, q2, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1), cycle, boneMask);

					BlendBones(studioHdr, q, pos, seqdesc, sequence, q2, pos2, s0, boneMask);
				}
			}
			else if (s1 > 0.999) {
				CalcAnimation(studioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1 + 1), cycle, boneMask);
				CalcAnimation(studioHdr, pos2, q2, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1 + 1), cycle, boneMask);
				BlendBones(studioHdr, q, pos, seqdesc, sequence, q2, pos2, s0, boneMask);
			}
			else if (!anim_3wayblend.GetBool()) {
				CalcAnimation(studioHdr, pos, q, seqdesc, sequence, seqdesc.Anim(i0, i1), cycle, boneMask);
				CalcAnimation(studioHdr, pos2, q2, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1), cycle, boneMask);
				BlendBones(studioHdr, q, pos, seqdesc, sequence, q2, pos2, s0, boneMask);

				CalcAnimation(studioHdr, pos2, q2, seqdesc, sequence, seqdesc.Anim(i0, i1 + 1), cycle, boneMask);
				CalcAnimation(studioHdr, pos3, q3, seqdesc, sequence, seqdesc.Anim(i0 + 1, i1 + 1), cycle, boneMask);
				BlendBones(studioHdr, q2, pos2, seqdesc, sequence, q3, pos3, s0, boneMask);

				BlendBones(studioHdr, q, pos, seqdesc, sequence, q2, pos2, s1, boneMask);
			}
			else {
				Span<int> iAnimIndices = stackalloc int[3];
				Span<float> weight = stackalloc float[3];

				Calc3WayBlendIndices(i0, i1, s0, s1, seqdesc, iAnimIndices, weight);

				if (weight[1] < 0.001) {
					CalcAnimation(studioHdr, pos, q, seqdesc, sequence, iAnimIndices[0], cycle, boneMask);
					CalcAnimation(studioHdr, pos2, q2, seqdesc, sequence, iAnimIndices[2], cycle, boneMask);
					BlendBones(studioHdr, q, pos, seqdesc, sequence, q2, pos2, weight[2] / (weight[0] + weight[2]), boneMask);
				}
				else {
					CalcAnimation(studioHdr, pos, q, seqdesc, sequence, iAnimIndices[0], cycle, boneMask);
					CalcAnimation(studioHdr, pos2, q2, seqdesc, sequence, iAnimIndices[1], cycle, boneMask);
					BlendBones(studioHdr, q, pos, seqdesc, sequence, q2, pos2, weight[1] / (weight[0] + weight[1]), boneMask);

					CalcAnimation(studioHdr, pos3, q3, seqdesc, sequence, iAnimIndices[2], cycle, boneMask);
					BlendBones(studioHdr, q, pos, seqdesc, sequence, q3, pos3, weight[2], boneMask);
				}
			}
		}

		VectorPool.Free(pos2);
		QuaternionPool.Free(q2);
		VectorPool.Free(pos3);
		QuaternionPool.Free(q3);

		return bResult;
	}

	internal static void Studio_SeqAnims(StudioHdr studioHdr, MStudioSeqDesc seqdesc, int sequence, ReadOnlySpan<float> poseParameter, Span<MStudioAnimDesc> panim, Span<float> weight) {
		if (studioHdr == null || sequence >= studioHdr.GetNumSeq()) {
			weight[0] = weight[1] = weight[2] = weight[3] = 0.0f;
			return;
		}

		int i0 = 0, i1 = 0;
		float s0 = 0, s1 = 0;

		Studio_LocalPoseParameter(studioHdr, poseParameter, seqdesc, sequence, 0, ref s0, ref i0);
		Studio_LocalPoseParameter(studioHdr, poseParameter, seqdesc, sequence, 1, ref s1, ref i1);

		panim[0] = studioHdr.Animdesc(studioHdr.iRelativeAnim(sequence, seqdesc.Anim(i0, i1)));
		weight[0] = (1 - s0) * (1 - s1);

		panim[1] = studioHdr.Animdesc(studioHdr.iRelativeAnim(sequence, seqdesc.Anim(i0 + 1, i1)));
		weight[1] = (s0) * (1 - s1);

		panim[2] = studioHdr.Animdesc(studioHdr.iRelativeAnim(sequence, seqdesc.Anim(i0, i1 + 1)));
		weight[2] = (1 - s0) * (s1);

		panim[3] = studioHdr.Animdesc(studioHdr.iRelativeAnim(sequence, seqdesc.Anim(i0 + 1, i1 + 1)));
		weight[3] = (s0) * (s1);

		Assert(weight[0] >= 0.0f && weight[1] >= 0.0f && weight[2] >= 0.0f && weight[3] >= 0.0f);
	}

	internal static float Studio_CPS(StudioHdr studioHdr, MStudioSeqDesc seqdesc, int sequence, ReadOnlySpan<float> poseParameter) {
		MStudioAnimDesc[] panim = ArrayPool<MStudioAnimDesc>.Shared.Rent(4);
		Span<float> weight = stackalloc float[4];

		Studio_SeqAnims(studioHdr, seqdesc, sequence, poseParameter, panim, weight);

		float t = 0;

		for (int i = 0; i < 4; i++) {
			if (weight[i] > 0 && panim[i].NumFrames > 1) {
				t += (panim[i].FPS / (panim[i].NumFrames - 1)) * weight[i];
			}
		}
		ArrayPool<MStudioAnimDesc>.Shared.Return(panim);
		return t;
	}
	private static void Calc3WayBlendIndices(int i0, int i1, float s0, float s1, MStudioSeqDesc seqdesc, Span<int> animIndices, Span<float> weight) {
		bool bEven = (((i0 + i1) & 0x1) == 0);

		int x1, y1;
		int x2, y2;
		int x3, y3;

		// diagonal is between elements 1 & 3
		// TL to BR
		if (bEven) {
			if (s0 > s1) {
				// B
				x1 = 0; y1 = 0;
				x2 = 1; y2 = 0;
				x3 = 1; y3 = 1;
				weight[0] = (1.0f - s0);
				weight[1] = s0 - s1;
			}
			else {
				// C
				x1 = 1; y1 = 1;
				x2 = 0; y2 = 1;
				x3 = 0; y3 = 0;
				weight[0] = s0;
				weight[1] = s1 - s0;
			}
		}
		// BL to TR
		else {
			float flTotal = s0 + s1;

			if (flTotal > 1.0f) {
				// D
				x1 = 1; y1 = 0;
				x2 = 1; y2 = 1;
				x3 = 0; y3 = 1;
				weight[0] = (1.0f - s1);
				weight[1] = s0 - 1.0f + s1;
			}
			else {
				// A
				x1 = 0; y1 = 1;
				x2 = 0; y2 = 0;
				x3 = 1; y3 = 0;
				weight[0] = s1;
				weight[1] = 1.0f - s0 - s1;
			}
		}

		animIndices[0] = seqdesc.Anim(i0 + x1, i1 + y1);
		animIndices[1] = seqdesc.Anim(i0 + x2, i1 + y2);
		animIndices[2] = seqdesc.Anim(i0 + x3, i1 + y3);

		// clamp the diagonal
		if (weight[1] < 0.001f)
			weight[1] = 0.0f;
		weight[2] = 1.0f - weight[0] - weight[1];

		Assert(weight[0] >= 0.0f && weight[0] <= 1.0f);
		Assert(weight[1] >= 0.0f && weight[1] <= 1.0f);
		Assert(weight[2] >= 0.0f && weight[2] <= 1.0f);
	}

	private static unsafe void CalcZeroframeData(StudioHdr studioHdr, StudioHeader animStudioHdr, VirtualGroup? animGroup, Span<MStudioBone> animBone, MStudioAnimDesc animdesc, TimeUnit_t frame, Span<Vector3> pos, Span<Quaternion> q, int boneMask, float weight) {
		Span<byte> pData = animdesc.ZeroFrameData();

		if (pData.IsEmpty)
			return;

		int i, j;

		if (animdesc.ZeroFrameCount == 1) {
			for (j = 0; j < animStudioHdr.NumBones; j++) {
				if (animGroup != null)
					i = animGroup.MasterBone[j];
				else
					i = j;

				if ((animBone[j].Flags & Studio.BONE_HAS_SAVEFRAME_POS) != 0) {
					if ((i >= 0) && (studioHdr.BoneFlags(i) & boneMask) != 0) {
						Vector3 p = pData.Cast<byte, Vector48>()[0];
						pos[i] = pos[i] * (1.0f - weight) + p * weight;
						Assert(pos[i].IsValid());
					}
					pData = pData[sizeof(Vector48)..];
				}
				if ((animBone[j].Flags & Studio.BONE_HAS_SAVEFRAME_ROT) != 0) {
					if ((i >= 0) && (studioHdr.BoneFlags(i) & boneMask) != 0) {
						Quaternion q0 = pData.Cast<byte, Quaternion64>()[0];
						MathLib.QuaternionBlend(q[i], q0, weight, out q[i]);
						Assert(q[i].IsValid());
					}
					pData = pData[sizeof(Quaternion64)..];
				}
			}
		}
		else {
			TimeUnit_t s1;
			int index = (int)(long)frame / animdesc.ZeroFrameSpan;
			if (index >= animdesc.ZeroFrameCount - 1) {
				index = animdesc.ZeroFrameCount - 2;
				s1 = 1.0;
			}
			else {
				s1 = Math.Clamp((frame - index * animdesc.ZeroFrameSpan) / animdesc.ZeroFrameSpan, 0.0, 1.0);
			}
			int i0 = Math.Max(index - 1, 0);
			int i1 = index;
			int i2 = Math.Min(index + 1, animdesc.ZeroFrameCount - 1);
			for (j = 0; j < animStudioHdr.NumBones; j++) {
				if (animGroup != null)
					i = animGroup.MasterBone[j];
				else
					i = j;

				if ((animBone[j].Flags & Studio.BONE_HAS_SAVEFRAME_POS) != 0) {
					if ((i >= 0) && (studioHdr.BoneFlags(i) & boneMask) != 0) {
						Span<Vector48> data = pData.Cast<byte, Vector48>();
						Vector3 p0 = data[i0];
						Vector3 p1 = data[i1];
						Vector3 p2 = data[i2];
						MathLib.Hermite_Spline(p0, p1, p2, (float)s1, out Vector3 p3);
						pos[i] = pos[i] * (1.0f - weight) + p3 * weight;
						Assert(pos[i].IsValid());
					}
					pData = pData[(sizeof(Vector48) * animdesc.ZeroFrameCount)..];
				}
				if ((animBone[j].Flags & Studio.BONE_HAS_SAVEFRAME_ROT) != 0) {
					if ((i >= 0) && (studioHdr.BoneFlags(i) & boneMask) != 0) {
						Span<Quaternion64> data = pData.Cast<byte, Quaternion64>();
						Quaternion q0 = data[i0];
						Quaternion q1 = data[i1];
						Quaternion q2 = data[i2];
						if (weight == 1.0f)
							MathLib.Hermite_Spline(q0, q1, q2, (float)s1, out q[i]);
						else {
							MathLib.Hermite_Spline(q0, q1, q2, (float)s1, out Quaternion q3);
							MathLib.QuaternionBlend(q[i], q3, weight, out q[i]);
						}
						Assert(q[i].IsValid());
					}
					pData = pData[(sizeof(Quaternion64) * animdesc.ZeroFrameCount)..];
				}
			}
		}
	}
	private static void CalcVirtualAnimation(VirtualModel vModel, StudioHdr studioHdr, Span<Vector3> pos, Span<Quaternion> q, MStudioSeqDesc seqdesc, int sequence, int animation, TimeUnit_t cycle, int boneMask) {
		MStudioBone bone;
		VirtualGroup seqGroup;
		StudioHeader seqStudioHdr;
		MStudioLinearBone seqLinearBones;
		Span<MStudioBone> seqbone;
		MStudioAnim? anim;
		StudioHeader animStudioHdr;
		MStudioLinearBone animLinearBones;
		Span<MStudioBone> animbone;
		VirtualGroup animGroup;

		seqGroup = vModel.SeqGroup(sequence);
		int baseanimation = studioHdr.iRelativeAnim(sequence, animation);
		MStudioAnimDesc animdesc = studioHdr.Animdesc(baseanimation);
		seqStudioHdr = studioHdr.SeqStudioHdr(sequence)!;
		seqLinearBones = seqStudioHdr.LinearBones()!;
		seqbone = seqStudioHdr.Bones(0);
		animGroup = vModel.AnimGroup(baseanimation);
		animStudioHdr = studioHdr.AnimStudioHdr(baseanimation)!;
		animLinearBones = animStudioHdr.LinearBones()!;
		animbone = animStudioHdr.Bones(0);

		int iFrame;
		float s;

		float fFrame = (float)cycle * (animdesc.NumFrames - 1); // todo: double?

		iFrame = (int)fFrame;
		s = (fFrame - iFrame);

		int iLocalFrame = iFrame;
		TimeUnit_t flStall;
		anim = animdesc.Anim(ref iLocalFrame, out flStall);

		Span<float> pweight = seqdesc.Boneweights(0);
		bone = studioHdr.Bone(0);

		for (int i = 0; i < studioHdr.NumBones(); i++) {
			if ((studioHdr.BoneFlags(i) & boneMask) != 0) {
				int j = seqGroup.BoneMap[i];
				if (j >= 0 && pweight[j] > 0.0f) {
					if ((animdesc.Flags & StudioAnimSeqFlags.Delta) != 0) {
						q[i].Init(0.0f, 0.0f, 0.0f, 1.0f);
						pos[i].Init(0.0f, 0.0f, 0.0f);
					}
					else if (seqLinearBones != null) {
						q[i] = seqLinearBones.Quat(j);
						pos[i] = seqLinearBones.Pos(j);
					}
					else {
						q[i] = seqbone[j].Quat;
						pos[i] = seqbone[j].Position;
					}
				}
			}
		}

		// if the animation isn't available, look for the zero frame cache
		if (anim == null) {
			CalcZeroframeData(studioHdr, animStudioHdr, animGroup, animbone, animdesc, fFrame, pos, q, boneMask, 1.0f);
			return;
		}

		// FIXME: change encoding so that bone -1 is never the case
		while (anim != null && anim.Bone < 255) {
			int j = animGroup.MasterBone[anim.Bone];
			if (j >= 0 && ((studioHdr.BoneFlags(j) & boneMask) != 0)) {
				int k = seqGroup.BoneMap[j];

				if (k >= 0 && pweight[k] > 0.0f) {
					CalcBoneQuaternion(iLocalFrame, s, animbone[anim.Bone], animLinearBones, anim, ref q[j]);
					CalcBonePosition(iLocalFrame, s, animbone[anim.Bone], animLinearBones, anim, ref pos[j]);
				}
			}
			anim = anim.Next();
		}

		// cross fade in previous zeroframe data
		if (flStall > 0.0f)
			CalcZeroframeData(studioHdr, animStudioHdr, animGroup, animbone, animdesc, fFrame, pos, q, boneMask, (float)flStall);

		// calculate a local hierarchy override
		if (animdesc.NumLocalHierarchy != 0) {
			Matrix3x4[] boneToWorld = MatrixPool.Alloc();
			BoneBitList boneComputed = new();

			int i;
			for (i = 0; i < animdesc.NumLocalHierarchy; i++) {
				MStudioLocalHierarchy? hierarchy = animdesc.Hierarchy(i);

				if (hierarchy == null)
					break;

				int iBone = animGroup.MasterBone[hierarchy.Bone];
				if (iBone >= 0 && ((studioHdr.BoneFlags(iBone) & boneMask) != 0)) {
					if (hierarchy.NewParent != -1) {
						int newParent = animGroup.MasterBone[hierarchy.NewParent];
						if (newParent >= 0 && ((studioHdr.BoneFlags(newParent) & boneMask) != 0))
							CalcLocalHierarchyAnimation(studioHdr, boneToWorld, ref boneComputed, pos, q, bone, hierarchy, iBone, newParent, (float)cycle, iFrame, s, boneMask);
					}
					else
						CalcLocalHierarchyAnimation(studioHdr, boneToWorld, ref boneComputed, pos, q, bone, hierarchy, iBone, -1, (float)cycle, iFrame, s, boneMask);
				}
			}

			MatrixPool.Free(boneToWorld);
		}
	}
	public static void CalcBoneQuaternion(int frame, float s, MStudioBone? bone, MStudioLinearBone? linearBones, MStudioAnim anim, ref Quaternion q) {
		if (linearBones != null)
			CalcBoneQuaternion(frame, s, linearBones.Quat(anim.Bone), linearBones.Rot(anim.Bone), linearBones.RotScale(anim.Bone), linearBones.Flags(anim.Bone), linearBones.Alignment(anim.Bone), anim, ref q);
		else if (bone != null)
			CalcBoneQuaternion(frame, s, in bone.Quat, in bone.Rot, in bone.RotScale, bone.Flags, in bone.Alignment, anim, ref q);
		else
			throw new NullReferenceException();
	}
	private static void ExtractAnimValue(int frame, Span<MStudioAnimValue> animvalue, float scale, out float v1, out float v2) {
		if (animvalue.IsEmpty) {
			v1 = v2 = 0;
			return;
		}

		// Avoids a crash reading off the end of the data
		// There is probably a better long-term solution; Ken is going to look into it.
		if ((animvalue[0].Total == 1) && (animvalue[0].Valid == 1)) {
			v1 = v2 = animvalue[1].Value * scale;
			return;
		}

		int k = frame;

		// find the data list that has the frame
		while (animvalue[0].Total <= k) {
			k -= animvalue[0].Total;
			animvalue = animvalue[(animvalue[0].Valid + 1)..];
			if (animvalue[0].Total == 0) {
				Assert(false);
				v1 = v2 = 0;
				return;
			}
		}
		if (animvalue[0].Valid > k) {
			// has valid animation data
			v1 = animvalue[k + 1].Value * scale;

			if (animvalue[0].Valid > k + 1) {
				// has valid animation blend data
				v2 = animvalue[k + 2].Value * scale;
			}
			else {
				if (animvalue[0].Total > k + 1) {
					// data repeats, no blend
					v2 = v1;
				}
				else {
					// pull blend from first data block in next list
					v2 = animvalue[animvalue[0].Valid + 2].Value * scale;
				}
			}
		}
		else {
			// get last valid data block
			v1 = animvalue[animvalue[0].Valid].Value * scale;
			if (animvalue[0].Total > k + 1) {
				// data repeats, no blend
				v2 = v1;
			}
			else {
				// pull blend from first data block in next list
				v2 = animvalue[animvalue[0].Valid + 2].Value * scale;
			}
		}
	}
	private static void ExtractAnimValue(int frame, Span<MStudioAnimValue> animvalue, float scale, out float v1) {
		if (animvalue.IsEmpty) {
			v1 = 0;
			return;
		}
		int k = frame;

		while (animvalue[0].Total <= k) {
			k -= animvalue[0].Total;
			animvalue = animvalue[(animvalue[0].Valid + 1)..];
			if (animvalue[0].Total == 0) {
				Assert(false); // running off the end of the animation stream is bad
				v1 = 0;
				return;
			}
		}
		if (animvalue[0].Valid > k) {
			v1 = animvalue[k + 1].Value * scale;
		}
		else {
			// get last valid data block
			v1 = animvalue[animvalue[0].Valid].Value * scale;
		}

	}
	private static void CalcBoneQuaternion(int frame, float s, in Quaternion baseQuat, in RadianEuler baseRot, in Vector3 baseRotScale, int baseFlags, in Quaternion baseAlignment, MStudioAnim anim, ref Quaternion q) {
		if ((anim.Flags & StudioAnimFlags.RawRot) != 0) {
			q = anim.Quat48();
			Assert(q.IsValid());
			return;
		}

		if ((anim.Flags & StudioAnimFlags.RawRot2) != 0) {
			q = anim.Quat64();
			Assert(q.IsValid());
			return;
		}

		if ((anim.Flags & StudioAnimFlags.AnimRot) == 0) {
			if ((anim.Flags & StudioAnimFlags.Delta) != 0)
				q.Init(0.0f, 0.0f, 0.0f, 1.0f);
			else
				q = baseQuat;
			return;
		}

		MStudioAnimValuePtr pValuesPtr = anim.RotV();

		if (s > 0.001f) {
			Quaternion q1, q2;
			RadianEuler angle1, angle2;

			ExtractAnimValue(frame, pValuesPtr.Animvalue(0), baseRotScale.X, out angle1.X, out angle2.X);
			ExtractAnimValue(frame, pValuesPtr.Animvalue(1), baseRotScale.Y, out angle1.Y, out angle2.Y);
			ExtractAnimValue(frame, pValuesPtr.Animvalue(2), baseRotScale.Z, out angle1.Z, out angle2.Z);

			if ((anim.Flags & StudioAnimFlags.Delta) == 0) {
				angle1.X = angle1.X + baseRot.X;
				angle1.Y = angle1.Y + baseRot.Y;
				angle1.Z = angle1.Z + baseRot.Z;
				angle2.X = angle2.X + baseRot.X;
				angle2.Y = angle2.Y + baseRot.Y;
				angle2.Z = angle2.Z + baseRot.Z;
			}

			Assert(angle1.IsValid() && angle2.IsValid());
			if (angle1.X != angle2.X || angle1.Y != angle2.Y || angle1.Z != angle2.Z) {
				MathLib.AngleQuaternion(angle1, out q1);
				MathLib.AngleQuaternion(angle2, out q2);


				MathLib.QuaternionBlend(q1, q2, s, out q);
			}
			else {
				MathLib.AngleQuaternion(angle1, out q);
			}
		}
		else {
			RadianEuler angle;

			ExtractAnimValue(frame, pValuesPtr.Animvalue(0), baseRotScale.X, out angle.X);
			ExtractAnimValue(frame, pValuesPtr.Animvalue(1), baseRotScale.Y, out angle.Y);
			ExtractAnimValue(frame, pValuesPtr.Animvalue(2), baseRotScale.Z, out angle.Z);

			if ((anim.Flags & StudioAnimFlags.Delta) == 0) {
				angle.X = angle.X + baseRot.X;
				angle.Y = angle.Y + baseRot.Y;
				angle.Z = angle.Z + baseRot.Z;
			}

			Assert(angle.IsValid());
			MathLib.AngleQuaternion(angle, out q);
		}

		Assert(q.IsValid());

		// align to unified bone
		if ((anim.Flags & StudioAnimFlags.Delta) == 0 && (baseFlags & Studio.BONE_FIXED_ALIGNMENT) != 0)
			MathLib.QuaternionAlign(baseAlignment, q, out q);
	}

	public static void CalcBonePosition(int frame, float s, MStudioBone? bone, MStudioLinearBone? linearBones, MStudioAnim anim, ref Vector3 pos) {
		if (linearBones != null)
			CalcBonePosition(frame, s, linearBones.Pos(anim.Bone), linearBones.PosScale(anim.Bone), anim, ref pos);
		else if (bone != null)
			CalcBonePosition(frame, s, in bone.Position, in bone.PosScale, anim, ref pos);
		else
			throw new NullReferenceException();
	}
	public static void CalcBonePosition(int frame, float s, in Vector3 basePos, in Vector3 baseBoneScale, MStudioAnim anim, ref Vector3 pos) {
		if ((anim.Flags & StudioAnimFlags.RawPos) != 0) {
			pos = anim.Pos();
			Assert(pos.IsValid());

			return;
		}
		else if ((anim.Flags & StudioAnimFlags.AnimPos) == 0) {
			if ((anim.Flags & StudioAnimFlags.Delta) != 0)
				pos.Init(0.0f, 0.0f, 0.0f);
			else
				pos = basePos;
			return;
		}

		MStudioAnimValuePtr pPosV = anim.PosV();
		int j;

		if (s > 0.001f) {
			float v1, v2;
			for (j = 0; j < 3; j++) {
				ExtractAnimValue(frame, pPosV.Animvalue(j), baseBoneScale[j], out v1, out v2);
				pos[j] = v1 * (1.0f - s) + v2 * s;
			}
		}
		else {
			ExtractAnimValue(frame, pPosV.Animvalue(0), baseBoneScale[0], out pos.X);
			ExtractAnimValue(frame, pPosV.Animvalue(1), baseBoneScale[1], out pos.Y);
			ExtractAnimValue(frame, pPosV.Animvalue(2), baseBoneScale[2], out pos.Z);
		}

		if ((anim.Flags & StudioAnimFlags.Delta) == 0) {
			pos.X = pos.X + basePos.X;
			pos.Y = pos.Y + basePos.Y;
			pos.Z = pos.Z + basePos.Z;
		}

		Assert(pos.IsValid());
	}
	public static void CalcLocalHierarchyAnimation(StudioHdr studioHdr, Span<Matrix3x4> boneToWorld, ref BoneBitList boneComputed, Span<Vector3> pos, Span<Quaternion> q, MStudioBone bone, MStudioLocalHierarchy hierarchy, int iBone, int newParent, float cycle, int frame, float fraq, int boneMask) {

	}
	private static void CalcAnimation(StudioHdr studioHdr, Span<Vector3> pos, Span<Quaternion> q, MStudioSeqDesc seqdesc, int sequence, int animation, TimeUnit_t cycle, int boneMask) {
		VirtualModel? vModel = studioHdr.GetVirtualModel();

		if (vModel != null) {
			CalcVirtualAnimation(vModel, studioHdr, pos, q, seqdesc, sequence, animation, cycle, boneMask);
			return;
		}

		MStudioAnimDesc animdesc = studioHdr.Animdesc(animation);
		MStudioBone pbone = studioHdr.Bone(0);
		MStudioLinearBone? linearBones = studioHdr.LinearBones();

		int iFrame;
		TimeUnit_t s;

		TimeUnit_t fFrame = cycle * (animdesc.NumFrames - 1);

		iFrame = (int)fFrame;
		s = (fFrame - iFrame);

		int iLocalFrame = iFrame;
		MStudioAnim? panim = animdesc.Anim(ref iLocalFrame, out TimeUnit_t flStall);

		ref float pweight = ref seqdesc.Boneweight(0);

		if (panim == null) {
			for (int i = 0; i < studioHdr.NumBones(); i++) {
				pbone = studioHdr.Bone(i);
				pweight = ref seqdesc.Boneweight(i);
				if (pweight > 0 && (studioHdr.BoneFlags(i) & boneMask) != 0) {
					if ((animdesc.Flags & StudioAnimSeqFlags.Delta) != 0) {
						q[i].Init(0.0f, 0.0f, 0.0f, 1.0f);
						pos[i].Init(0.0f, 0.0f, 0.0f);
					}
					else {
						q[i] = pbone.Quat;
						pos[i] = pbone.Position;
					}
				}
			}

			CalcZeroframeData(studioHdr, studioHdr.GetRenderHdr(), null, studioHdr.Bones(), animdesc, fFrame, pos, q, boneMask, 1.0f);

			return;
		}

		for (int i = 0; i < studioHdr.NumBones(); i++) {
			pweight = ref seqdesc.Boneweight(i);
			pbone = studioHdr.Bone(i);
			if (panim != null && panim.Bone == i) {
				if (pweight > 0 && (studioHdr.BoneFlags(i) & boneMask) != 0) {
					CalcBoneQuaternion(iLocalFrame, (float)s, pbone, linearBones, panim, ref q[i]);
					CalcBonePosition(iLocalFrame, (float)s, pbone, linearBones, panim, ref pos[i]);
				}
				panim = panim.Next();
			}
			else if (pweight > 0 && (studioHdr.BoneFlags(i) & boneMask) != 0) {
				if ((animdesc.Flags & StudioAnimSeqFlags.Delta) != 0) {
					q[i].Init(0.0f, 0.0f, 0.0f, 1.0f);
					pos[i].Init(0.0f, 0.0f, 0.0f);
				}
				else {
					q[i] = pbone.Quat;
					pos[i] = pbone.Position;
				}
			}
		}

		// cross fade in previous zeroframe data
		if (flStall > 0.0f)
			CalcZeroframeData(studioHdr, studioHdr.GetRenderHdr(), null, studioHdr.Bones(), animdesc, fFrame, pos, q, boneMask, (float)flStall);

		if (animdesc.NumLocalHierarchy != 0) {
			Matrix3x4[] boneToWorld = MatrixPool.Alloc();
			BoneBitList boneComputed = new();

			int i;
			for (i = 0; i < animdesc.NumLocalHierarchy; i++) {
				MStudioLocalHierarchy? hierarchy = animdesc.Hierarchy(i);

				if (hierarchy == null)
					break;

				if ((studioHdr.BoneFlags(hierarchy.Bone) & boneMask) != 0) {
					if ((studioHdr.BoneFlags(hierarchy.NewParent) & boneMask) != 0) {
						CalcLocalHierarchyAnimation(studioHdr, boneToWorld, ref boneComputed, pos, q, pbone, hierarchy, hierarchy.Bone, hierarchy.NewParent, (float)cycle, iFrame, (float)s, boneMask);
					}
				}
			}

			MatrixPool.Free(boneToWorld);
		}
	}
	private static void BlendBones(StudioHdr studioHdr, Span<Quaternion> q1, Span<Vector3> pos1, MStudioSeqDesc seqdesc, int sequence, ReadOnlySpan<Quaternion> q2, ReadOnlySpan<Vector3> pos2, float s, int boneMask) {
		int i, j;
		Quaternion q3;

		VirtualModel? vModel = studioHdr.GetVirtualModel();
		VirtualGroup? seqGroup = null;
		if (vModel != null)
			seqGroup = vModel.SeqGroup(sequence);

		if (s <= 0) {
			Assert(false);
			return;
		}
		else if (s >= 1.0) {
			Assert(false);
			for (i = 0; i < studioHdr.NumBones(); i++) {
				if ((studioHdr.BoneFlags(i) & boneMask) == 0)
					continue;

				if (seqGroup != null)
					j = seqGroup.BoneMap[i];
				else
					j = i;

				if (j >= 0 && seqdesc.Weight(j) > 0.0) {
					q1[i] = q2[i];
					pos1[i] = pos2[i];
				}
			}
			return;
		}

		float s2 = s;
		float s1 = 1.0F - s2;

		for (i = 0; i < studioHdr.NumBones(); i++) {
			if ((studioHdr.BoneFlags(i) & boneMask) == 0)
				continue;

			if (seqGroup != null)
				j = seqGroup.BoneMap[i];
			else
				j = i;


			if (j >= 0 && seqdesc.Weight(j) > 0.0) {
				if ((studioHdr.BoneFlags(i) & Studio.BONE_FIXED_ALIGNMENT) != 0) {
					MathLib.QuaternionBlendNoAlign(q2[i], q1[i], s1, out q3);
				}
				else {
					MathLib.QuaternionBlend(q2[i], q1[i], s1, out q3);
				}

				q1[i][0] = q3[0];
				q1[i][1] = q3[1];
				q1[i][2] = q3[2];
				q1[i][3] = q3[3];
				pos1[i][0] = pos1[i][0] * s1 + pos2[i][0] * s2;
				pos1[i][1] = pos1[i][1] * s1 + pos2[i][1] * s2;
				pos1[i][2] = pos1[i][2] * s1 + pos2[i][2] * s2;
			}
		}
	}
	private static void ScaleBones(StudioHdr studioHdr, Span<Quaternion> q1, Span<Vector3> pos1, int sequence, float s, int boneMask) {
		int i, j;

		MStudioSeqDesc seqdesc = studioHdr.Seqdesc(sequence);

		VirtualModel? vModel = studioHdr.GetVirtualModel();
		VirtualGroup? seqGroup = null;
		if (vModel != null)
			seqGroup = vModel.SeqGroup(sequence);

		float s2 = s;
		float s1 = 1.0f - s2;

		for (i = 0; i < studioHdr.NumBones(); i++) {
			// skip unused bones
			if ((studioHdr.BoneFlags(i) & boneMask) == 0)
				continue;

			if (seqGroup != null)
				j = seqGroup.BoneMap[i];
			else
				j = i;

			if (j >= 0 && seqdesc.Weight(j) > 0.0) {
				MathLib.QuaternionIdentityBlend(q1[i], s1, out q1[i]);
				MathLib.VectorScale(pos1[i], s2, out pos1[i]);
			}
		}
	}

	private static void Studio_LocalPoseParameter(StudioHdr studioHdr, ReadOnlySpan<float> poseParameter, MStudioSeqDesc seqdesc, int sequence, int localIndex, ref float flSetting, ref int index) {
		if (studioHdr == null) {
			flSetting = 0;
			index = 0;
			return;
		}

		int iPose = studioHdr.GetSharedPoseParameter(sequence, seqdesc.ParamIndex[localIndex]);

		if (iPose == -1) {
			flSetting = 0;
			index = 0;
			return;
		}

		MStudioPoseParamDesc Pose = studioHdr.PoseParameter(iPose);

		float flValue = poseParameter[iPose];

		if (Pose.Loop != 0) {
			float wrap = (Pose.Start + Pose.End) / 2.0F + Pose.Loop / 2.0F;
			float shift = Pose.Loop - wrap;

			flValue = flValue - Pose.Loop * MathF.Floor((flValue + shift) / Pose.Loop);
		}

		if (seqdesc.PoseKeyIndex == 0) {
			float flLocalStart = ((float)seqdesc.ParamStart[localIndex] - Pose.Start) / (Pose.End - Pose.Start);
			float flLocalEnd = ((float)seqdesc.ParamEnd[localIndex] - Pose.Start) / (Pose.End - Pose.Start);

			flSetting = (flValue - flLocalStart) / (flLocalEnd - flLocalStart);

			if (flSetting < 0)
				flSetting = 0;
			if (flSetting > 1)
				flSetting = 1;

			index = 0;
			if (seqdesc.GroupSize[localIndex] > 2) {
				index = (int)(flSetting * (seqdesc.GroupSize[localIndex] - 1));
				if (index == seqdesc.GroupSize[localIndex] - 1) index = seqdesc.GroupSize[localIndex] - 2;
				flSetting = flSetting * (seqdesc.GroupSize[localIndex] - 1) - index;
			}
		}
		else {
			flValue = flValue * (Pose.End - Pose.Start) + Pose.Start;
			index = 0;

			// FIXME: this needs to be 2D
			// FIXME: this shouldn't be a linear search

			while (true) {
				flSetting = (flValue - seqdesc.PoseKey(localIndex, index)) / (seqdesc.PoseKey(localIndex, index + 1) - seqdesc.PoseKey(localIndex, index));
				if (index < seqdesc.GroupSize[localIndex] - 2 && flSetting > 1.0) {
					index++;
					continue;
				}
				break;
			}

			// clamp.
			if (flSetting < 0.0f)
				flSetting = 0.0f;
			if (flSetting > 1.0f)
				flSetting = 1.0f;
		}
	}

	private void AddLocalLayers(Span<Vector3> pos, Span<Quaternion> q, MStudioSeqDesc seqdesc, int sequence, double cycle, double weight, double time, IKContext? ikContext) {
		if ((seqdesc.Flags & StudioAnimSeqFlags.Local) == 0)
			return;

		for (int i = 0; i < seqdesc.NumAutoLayers; i++) {
			MStudioAutoLayer pLayer = seqdesc.Autolayer(i);

			if ((pLayer.Flags & StudioAutolayerFlags.Local) == 0)
				continue;

			float layerCycle = (float)cycle;
			float layerWeight = (float)weight;

			if (pLayer.Start != pLayer.End) {
				float s = 1.0f;

				if (cycle < pLayer.Start)
					continue;
				if (cycle >= pLayer.End)
					continue;

				if (cycle < pLayer.Peak && pLayer.Start != pLayer.Peak) {
					s = (float)(cycle - pLayer.Start) / (pLayer.Peak - pLayer.Start);
				}
				else if (cycle > pLayer.Tail && pLayer.End != pLayer.Tail) {
					s = (float)(pLayer.End - cycle) / (pLayer.End - pLayer.Tail);
				}

				if ((pLayer.Flags & StudioAutolayerFlags.Spline) != 0)
					s = MathLib.SimpleSpline(s);

				if ((pLayer.Flags & StudioAutolayerFlags.XFade) != 0 && (cycle > pLayer.Tail))
					layerWeight = (float)((s * weight) / (1 - weight + s * weight));
				else if ((pLayer.Flags & StudioAutolayerFlags.NoBlend) != 0)
					layerWeight = s;
				else
					layerWeight = (float)weight * s;

				layerCycle = (float)(cycle - pLayer.Start) / (pLayer.End - pLayer.Start);
			}

			int iSequence = studioHdr.RelativeSeq(sequence, pLayer.Sequence);
			AccumulatePose(pos, q, iSequence, layerCycle, layerWeight, time, ikContext);
		}
	}

	private static void SlerpBones(StudioHdr studioHdr, Span<Quaternion> q1, Span<Vector3> pos1, MStudioSeqDesc seqdesc, int sequence, Span<Quaternion> q2, Span<Vector3> pos2, float s, int boneMask) {
		if (s <= 0.0f)
			return;

		if (s > 1.0f)
			s = 1.0f;

		if ((seqdesc.Flags & StudioAnimSeqFlags.World) != 0) {
			WorldSpaceSlerp(studioHdr, q1, pos1, seqdesc, sequence, q2, pos2, s, boneMask);
			return;
		}

		int i, j;
		VirtualModel? vModel = studioHdr.GetVirtualModel();
		VirtualGroup? seqGroup = vModel != null ? vModel.SeqGroup(sequence) : null;

		// Build weightlist for all bones
		int nBoneCount = studioHdr.NumBones();
		Span<float> pS2 = stackalloc float[nBoneCount];
		for (i = 0; i < nBoneCount; i++) {
			// skip unused bones
			if ((studioHdr.BoneFlags(i) & boneMask) == 0) {
				pS2[i] = 0.0f;
				continue;
			}

			if (seqGroup == null) {
				pS2[i] = s * seqdesc.Weight(i);
				continue;
			}

			j = seqGroup.BoneMap[i];
			if (j >= 0)
				pS2[i] = s * seqdesc.Weight(j);
			else
				pS2[i] = 0.0f;
		}

		float s1, s2;
		if ((seqdesc.Flags & StudioAnimSeqFlags.Delta) != 0) {
			for (i = 0; i < nBoneCount; i++) {
				s2 = pS2[i];

				if (s2 <= 0.0f)
					continue;

				if ((seqdesc.Flags & StudioAnimSeqFlags.Post) != 0) {
					MathLib.QuaternionMA(q1[i], s2, q2[i], ref q1[i]);

					// FIXME: are these correct?
					MathLib.VectorMA(pos1[i], s2, pos2[i], out pos1[i]);
				}
				else {
					MathLib.QuaternionSM(s2, q2[i], q1[i], ref q1[i]);

					// FIXME: are these correct?
					MathLib.VectorMA(pos1[i], s2, pos2[i], out pos1[i]);
				}
			}
			return;
		}

		for (i = 0; i < nBoneCount; i++) {
			s2 = pS2[i];

			if (s2 <= 0.0f)
				continue;

			s1 = 1.0f - s2;

			if ((studioHdr.BoneFlags(i) & Studio.BONE_FIXED_ALIGNMENT) != 0)
				MathLib.QuaternionSlerpNoAlign(q2[i], q1[i], s1, out q1[i]);
			else
				MathLib.QuaternionSlerp(q2[i], q1[i], s1, out q1[i]);

			pos1[i].X = pos1[i].X * s1 + pos2[i].X * s2;
			pos1[i].Y = pos1[i].Y * s1 + pos2[i].Y * s2;
			pos1[i].Z = pos1[i].Z * s1 + pos2[i].Z * s2;
		}
	}

	private static void WorldSpaceSlerp(StudioHdr studioHdr, Span<Quaternion> q1, Span<Vector3> pos1, MStudioSeqDesc seqdesc, int sequence, Span<Quaternion> q2, Span<Vector3> pos2, float s, int boneMask) {
		int i, j;
		float s1;
		float s2;

		MathLib.SetIdentityMatrix(out Matrix3x4 rootXform);

		Matrix3x4[] srcBoneToWorld = MatrixPool.Alloc();
		BoneBitList srcBoneComputed = default;

		Matrix3x4[] destBoneToWorld = MatrixPool.Alloc();
		BoneBitList destBoneComputed = default;

		Matrix3x4[] targetBoneToWorld = MatrixPool.Alloc();

		VirtualModel? vModel = studioHdr.GetVirtualModel();
		VirtualGroup? seqGroup = null;
		if (vModel != null)
			seqGroup = vModel.SeqGroup(sequence);

		for (i = 0; i < studioHdr.NumBones(); i++) {
			if ((studioHdr.BoneFlags(i) & boneMask) == 0)
				continue;

			int n = studioHdr.BoneParent(i);
			s1 = 0.0f;
			if (seqGroup != null) {
				j = seqGroup.BoneMap[i];
				if (j >= 0) {
					s2 = s * seqdesc.Weight(j);
					if (n != -1)
						s1 = s * seqdesc.Weight(seqGroup.BoneMap[n]);
				}
				else
					s2 = 0.0f;
			}
			else {
				s2 = s * seqdesc.Weight(i);
				if (n != -1)
					s1 = s * seqdesc.Weight(n);
			}

			if (s1 == 1.0f && s2 == 1.0f) {
				pos1[i] = pos2[i];
				q1[i] = q2[i];
			}
			else if (s2 > 0.0f) {
				BuildBoneChain(studioHdr, rootXform, pos1, q1, i, destBoneToWorld, ref destBoneComputed);
				BuildBoneChain(studioHdr, rootXform, pos2, q2, i, srcBoneToWorld, ref srcBoneComputed);

				MathLib.MatrixAngles(destBoneToWorld[i], out Quaternion destQ, out Vector3 destPos);
				MathLib.MatrixAngles(srcBoneToWorld[i], out Quaternion srcQ, out _);

				MathLib.QuaternionSlerp(destQ, srcQ, s2, out Quaternion targetQ);
				MathLib.QuaternionAngles(targetQ, out RadianEuler targetAngles);
				MathLib.AngleMatrix(targetAngles, destPos, out targetBoneToWorld[i]);

				if (n == -1)
					MathLib.MatrixAngles(targetBoneToWorld[i], out q1[i], out _);
				else {
					MathLib.MatrixInvert(targetBoneToWorld[n], out Matrix3x4 worldToBone);

					MathLib.ConcatTransforms(worldToBone, targetBoneToWorld[i], out Matrix3x4 local);
					MathLib.MatrixAngles(local, out q1[i], out _);

					pos1[i] = Vector3.Lerp(pos1[i], pos2[i], s2);
				}
			}
		}
		MatrixPool.Free(srcBoneToWorld);
		MatrixPool.Free(destBoneToWorld);
		MatrixPool.Free(targetBoneToWorld);
	}

	internal static void BuildBoneChain(StudioHdr studioHdr, in Matrix3x4 rootxform, ReadOnlySpan<Vector3> pos, ReadOnlySpan<Quaternion> q, int bone, Span<Matrix3x4> boneToWorld, ref BoneBitList boneComputed) {
		if (boneComputed.IsBoneMarked(bone))
			return;

		MathLib.QuaternionMatrix(q[bone], pos[bone], out Matrix3x4 bonematrix);

		int parent = studioHdr.BoneParent(bone);
		if (parent == -1)
			MathLib.ConcatTransforms(rootxform, bonematrix, out boneToWorld[bone]);
		else {
			BuildBoneChain(studioHdr, rootxform, pos, q, parent, boneToWorld, ref boneComputed);
			MathLib.ConcatTransforms(boneToWorld[parent], bonematrix, out boneToWorld[bone]);
		}
		boneComputed.MarkBone(bone);
	}

	private void AddSequenceLayers(Span<Vector3> pos, Span<Quaternion> q, MStudioSeqDesc seqdesc, int sequence, double cycle, float weight, double time, IKContext? ikContext) {
		for (int i = 0; i < seqdesc.NumAutoLayers; i++) {
			MStudioAutoLayer pLayer = seqdesc.Autolayer(i);

			if ((pLayer.Flags & StudioAutolayerFlags.Local) != 0)
				continue;

			float layerCycle = (float)cycle;
			float layerWeight = (float)weight;

			if (pLayer.Start != pLayer.End) {
				float s = 1.0f;
				float index;

				if ((pLayer.Flags & StudioAutolayerFlags.Pose) == 0) {
					index = (float)cycle;
				}
				else {
					int iSequence = studioHdr.RelativeSeq(sequence, pLayer.Sequence);
					int iPose = studioHdr.GetSharedPoseParameter(iSequence, pLayer.Pose);
					if (iPose != -1) {
						MStudioPoseParamDesc Pose = studioHdr.PoseParameter(iPose);
						index = poseParameter[iPose] * (Pose.End - Pose.Start) + Pose.Start;
					}
					else {
						index = 0;
					}
				}

				if (index < pLayer.Start)
					continue;
				if (index >= pLayer.End)
					continue;

				if (index < pLayer.Peak && pLayer.Start != pLayer.Peak) {
					s = (index - pLayer.Start) / (pLayer.Peak - pLayer.Start);
				}
				else if (index > pLayer.Tail && pLayer.End != pLayer.Tail) {
					s = (pLayer.End - index) / (pLayer.End - pLayer.Tail);
				}

				if ((pLayer.Flags & StudioAutolayerFlags.Spline) != 0) {
					s = MathLib.SimpleSpline(s);
				}

				if ((pLayer.Flags & StudioAutolayerFlags.XFade) != 0 && (index > pLayer.Tail)) {
					layerWeight = (float)(s * weight) / (1 - weight + s * weight);
				}
				else if ((pLayer.Flags & StudioAutolayerFlags.NoBlend) != 0) {
					layerWeight = s;
				}
				else {
					layerWeight = (float)weight * s;
				}

				if ((pLayer.Flags & StudioAutolayerFlags.Pose) == 0) {
					layerCycle = (float)(cycle - pLayer.Start) / (pLayer.End - pLayer.Start);
				}
			}

			int seq = studioHdr.RelativeSeq(sequence, pLayer.Sequence);
			AccumulatePose(pos, q, seq, layerCycle, layerWeight, time, ikContext);
		}
	}

	public static double Studio_Duration(StudioHdr studioHdr, int sequence, ReadOnlySpan<float> poseParameter) {
		MStudioSeqDesc seqdesc = studioHdr.Seqdesc(sequence);
		float cps = Studio_CPS(studioHdr, seqdesc, sequence, poseParameter);

		if (cps == 0)
			return 0.0;

		return 1.0 / cps;
	}

	public static float Studio_SetPoseParameter(StudioHdr studioHdr, int parameter, float value, out float ctlValue) {
		if (parameter < 0 || parameter >= studioHdr.GetNumPoseParameters()) {
			ctlValue = default;
			return 0;
		}

		MStudioPoseParamDesc PoseParam = studioHdr.PoseParameter(parameter);

		Assert(float.IsFinite(value));

		if (PoseParam.Loop != 0) {
			float wrap = (PoseParam.Start + PoseParam.End) / 2.0F + PoseParam.Loop / 2.0F;
			float shift = PoseParam.Loop - wrap;

			value = value - PoseParam.Loop * MathF.Floor((value + shift) / PoseParam.Loop);
		}

		ctlValue = (value - PoseParam.Start) / (PoseParam.End - PoseParam.Start);

		if (ctlValue < 0) ctlValue = 0;
		if (ctlValue > 1) ctlValue = 1;

		Assert(float.IsFinite(ctlValue));

		return ctlValue * (PoseParam.End - PoseParam.Start) + PoseParam.Start;
	}

	static MStudioBoneController? FindController(StudioHdr studioHdr, int controller) {
		for (int i = 0; i < studioHdr.NumBoneControllers(); i++) {
			if (studioHdr.BoneController(i).InputField == controller)
				return studioHdr.BoneController(i);
		}

		return null;
	}

	public static float Studio_SetController(StudioHdr? studioHdr, int controller, float value, out float ctlValue) {
		if (studioHdr == null) {
			ctlValue = default;
			return value;
		}

		MStudioBoneController? boneController = FindController(studioHdr, controller);
		if (boneController == null) {
			ctlValue = 0;
			return value;
		}

		if ((boneController.Type & (StudioMotionFlags.XR | StudioMotionFlags.YR | StudioMotionFlags.ZR)) != 0) {
			if (boneController.End < boneController.Start)
				value = -value;

			if (boneController.Start + 359.0 >= boneController.End) {
				if (value > ((boneController.Start + boneController.End) / 2.0) + 180)
					value = value - 360;
				if (value < ((boneController.Start + boneController.End) / 2.0) - 180)
					value = value + 360;
			}
			else {
				if (value > 360)
					value = (float)(value - (int)(value / 360.0) * 360.0);
				else if (value < 0)
					value = (float)(value + (int)((value / -360.0) + 1) * 360.0);
			}
		}

		ctlValue = (value - boneController.Start) / (boneController.End - boneController.Start);
		if (ctlValue < 0) ctlValue = 0;
		if (ctlValue > 1) ctlValue = 1;

		float returnVal = (float)((1.0 - ctlValue) * boneController.Start + ctlValue * boneController.End);

		if ((boneController.Type & (StudioMotionFlags.XR | StudioMotionFlags.YR | StudioMotionFlags.ZR)) != 0 &&
			boneController.End < boneController.Start) {
			returnVal *= -1;
		}

		return returnVal;
	}

	public static float Studio_GetController(StudioHdr? studioHdr, int controller, float ctlValue) {
		if (studioHdr == null)
			return 0.0f;

		MStudioBoneController? boneController = FindController(studioHdr, controller);
		if (boneController == null)
			return 0;

		return ctlValue * (boneController.End - boneController.Start) + boneController.Start;
	}

	public static float Studio_GetMass(StudioHdr? studioHdr) {
		if (studioHdr == null) return 1.0f;
		return studioHdr.Mass();
	}

	public void CalcAutoplaySequences(Span<Vector3> pos, Span<Quaternion> q, TimeUnit_t realTime, IKContext? ikContext) {
		if (studioHdr.GetNumIKChains() == 0 && studioHdr.GetNumIKAutoplayLocks() != 0)
			return;

		ikContext?.AddAutoplayLocks(pos, q);

		int count = studioHdr.GetAutoplayList(out ReadOnlySpan<short> pList);
		for (int i = 0; i < count; i++) {
			int sequenceIndex = pList[i];
			MStudioSeqDesc seqdesc = studioHdr.Seqdesc(sequenceIndex);
			if ((seqdesc.Flags & StudioAnimSeqFlags.Autoplay) != 0) {
				double cycle = 0;
				float cps = Studio_CPS(studioHdr, seqdesc, sequenceIndex, poseParameter);
				cycle = realTime * cps;
				cycle = cycle - (int)cycle;

				AccumulatePose(pos, q, sequenceIndex, cycle, 1.0f, realTime, ikContext);
			}
		}

		ikContext?.SolveAutoplayLocks(pos, q);
	}

	public static void Studio_BuildMatrices(StudioHdr studioHdr, in QAngle angles, in Vector3 origin, ReadOnlySpan<Vector3> pos, ReadOnlySpan<Quaternion> q, int iBone, float flScale, Span<Matrix3x4> bonetoworld, int boneMask) {
		int i, j;

		Span<int> chain = stackalloc int[Studio.MAXSTUDIOBONES];
		int chainlength = 0;

		if (iBone < -1 || iBone >= studioHdr.NumBones())
			iBone = 0;

		if (iBone == -1) {
			chainlength = studioHdr.NumBones();
			for (i = 0; i < studioHdr.NumBones(); i++)
				chain[chainlength - i - 1] = i;
		}
		else {
			i = iBone;
			while (i != -1) {
				chain[chainlength++] = i;
				i = studioHdr.BoneParent(i);
			}
		}

		MathLib.AngleMatrix(angles, origin, out Matrix3x4 rotationmatrix);

		if (flScale < 1.0f - FLT_EPSILON || flScale > 1.0f + FLT_EPSILON) {
			MathLib.MatrixGetColumn(rotationmatrix, 3, out Vector3 vecOffset);
			vecOffset -= origin;
			vecOffset *= flScale;
			vecOffset += origin;
			MathLib.MatrixSetColumn(vecOffset, 3, ref rotationmatrix);

			for (int row = 0; row < 3; row++) {
				Span<float> r = rotationmatrix[row];
				r[0] *= flScale;
				r[1] *= flScale;
				r[2] *= flScale;
			}
		}

		for (j = chainlength - 1; j >= 0; j--) {
			i = chain[j];
			if ((studioHdr.BoneFlags(i) & boneMask) != 0) {
				MathLib.QuaternionMatrix(q[i], pos[i], out Matrix3x4 bonematrix);

				if (studioHdr.BoneParent(i) == -1)
					MathLib.ConcatTransforms(rotationmatrix, bonematrix, out bonetoworld[i]);
				else
					MathLib.ConcatTransforms(bonetoworld[studioHdr.BoneParent(i)], bonematrix, out bonetoworld[i]);
			}
		}
	}

	public static int Studio_FindAttachment(StudioHdr hdr, ReadOnlySpan<char> attachmentName) {
		if (hdr != null && hdr.SequencesAvailable()) {
			for (int i = 0; i < hdr.GetNumAttachments(); i++) {
				if (attachmentName.CompareTo(hdr.Attachment(i).Name(), StringComparison.InvariantCultureIgnoreCase) == 0) {
					return i;
				}
			}
		}

		return -1;
	}

	public static int Studio_BoneIndexByName(StudioHdr? studioHdr, ReadOnlySpan<char> name) {
		if (studioHdr != null) {
			int start = 0, end = studioHdr.NumBones() - 1;
			ReadOnlySpan<byte> boneTable = studioHdr.GetBoneTableSortedByName();
			while (start <= end) {
				int mid = (start + end) >> 1;
				int cmp = stricmp(studioHdr.Bone(boneTable[mid]).Name(), name);

				if (cmp < 0)
					start = mid + 1;
				else if (cmp > 0)
					end = mid - 1;
				else
					return boneTable[mid];
			}
		}

		return -1;
	}

	public static void SetupSingleBoneMatrix(StudioHdr ownerHdr, int sequence, int frame, int bone, out Matrix3x4 boneLocal) {
		MStudioSeqDesc seqdesc = ownerHdr.Seqdesc(sequence);
		MStudioAnimDesc animdesc = ownerHdr.Animdesc(seqdesc.Anim(0, 0));
		int localFrame = frame;
		MStudioAnim? panim = animdesc.Anim(ref localFrame);
		float s = 0;
		MStudioBone pbone = ownerHdr.Bone(bone);

		Quaternion boneQuat;
		Vector3 bonePos;

		while (panim != null && panim.Bone != bone)
			panim = panim.Next();

		if (panim != null && seqdesc.Weight(bone) > 0) {
			boneQuat = default;
			bonePos = default;
			CalcBoneQuaternion(localFrame, s, pbone, null, panim, ref boneQuat);
			CalcBonePosition(localFrame, s, pbone, null, panim, ref bonePos);
		}
		else if ((animdesc.Flags & StudioAnimSeqFlags.Delta) != 0) {
			boneQuat = new(0.0f, 0.0f, 0.0f, 1.0f);
			bonePos = new(0.0f, 0.0f, 0.0f);
		}
		else {
			boneQuat = pbone.Quat;
			bonePos = pbone.Position;
		}

		MathLib.QuaternionMatrix(in boneQuat, in bonePos, out boneLocal);
	}

	public static bool Studio_SeqMovement(StudioHdr studioHdr, int sequence, float cycleFrom, float cycleTo, ReadOnlySpan<float> poseParameter, out Vector3 deltaPos, out QAngle deltaAngles) {
		MStudioAnimDesc[] panim = ArrayPool<MStudioAnimDesc>.Shared.Rent(4);
		Span<float> weight = stackalloc float[4];

		MStudioSeqDesc seqdesc = studioHdr.Seqdesc(sequence);

		Studio_SeqAnims(studioHdr, seqdesc, sequence, poseParameter, panim, weight);

		deltaPos = default;
		deltaAngles = default;

		bool found = false;

		for (int i = 0; i < 4; i++) {
			if (weight[i] != 0) {
				if (Studio_AnimMovement(panim[i], cycleFrom, cycleTo, out Vector3 localPos, out QAngle localAngles)) {
					found = true;
					deltaPos = deltaPos + localPos * weight[i];
					// FIXME: this makes no sense
					deltaAngles = deltaAngles + localAngles * weight[i];
				}
				else if (0 == (panim[i].Flags & StudioAnimSeqFlags.Delta) && panim[i].NumMovements == 0 && seqdesc.Weight(0) > 0.0) {
					found = true;
				}
			}
		}

		ArrayPool<MStudioAnimDesc>.Shared.Return(panim, true);
		return found;
	}

	private static bool Studio_AnimMovement(MStudioAnimDesc anim, float cycleFrom, float cycleTo, out Vector3 deltaPos, out QAngle deltaAngle) {
		deltaPos = default;
		deltaAngle = default;
		if (anim.NumMovements == 0)
			return false;

		Vector3 startPos;
		QAngle startA;
		Studio_AnimPosition(anim, cycleFrom, out startPos, out startA);

		Vector3 endPos;
		QAngle endA;
		Studio_AnimPosition(anim, cycleTo, out endPos, out endA);

		Vector3 tmp = endPos - startPos;
		deltaAngle.Y = endA.Y - startA.Y;
		MathLib.VectorYawRotate(tmp, -startA.Y, out deltaPos);

		return true;
	}

	private static bool Studio_AnimPosition(MStudioAnimDesc anim, float cycle, out Vector3 vecPos, out QAngle vecAngle) {
		float prevframe = 0;
		vecPos = default;
		vecAngle = default;

		if (anim.NumMovements == 0)
			return false;

		int iLoops = 0;
		if (cycle > 1.0)
			iLoops = (int)cycle;
		else if (cycle < 0.0)
			iLoops = (int)cycle - 1;

		cycle = cycle - iLoops;

		float flFrame = cycle * (anim.NumFrames - 1);

		for (int i = 0; i < anim.NumMovements; i++) {
			MStudioMovement pmove = anim.Movement(i);

			if (pmove.EndFrame >= flFrame) {
				float f = (flFrame - prevframe) / (pmove.EndFrame - prevframe);

				float d = pmove.V0 * f + 0.5f * (pmove.V1 - pmove.V0) * f * f;

				vecPos = vecPos + d * pmove.Vector;
				vecAngle.Y = vecAngle.Y * (1 - f) + pmove.Angle * f;
				if (iLoops != 0) {
					MStudioMovement lastMove = anim.Movement(anim.NumMovements - 1);
					vecPos = vecPos + iLoops * lastMove.Position;
					vecAngle.Y = vecAngle.Y + iLoops * lastMove.Angle;
				}
				return true;
			}
			else {
				prevframe = pmove.EndFrame;
				vecPos = pmove.Position;
				vecAngle.Y = pmove.Angle;
			}
		}

		return false;
	}

	private static bool Studio_AnimVelocity(MStudioAnimDesc anim, float cycle, out Vector3 vecVelocity) {
		float prevframe = 0;
		vecVelocity = default;

		float flFrame = cycle * (anim.NumFrames - 1);
		flFrame = flFrame - (int)(flFrame / (anim.NumFrames - 1));

		for (int i = 0; i < anim.NumMovements; i++) {
			MStudioMovement pmove = anim.Movement(i);

			if (pmove.EndFrame >= flFrame) {
				float f = (flFrame - prevframe) / (pmove.EndFrame - prevframe);

				float vel = pmove.V0 * (1 - f) + pmove.V1 * f;
				vel = vel * anim.FPS / (pmove.EndFrame - prevframe);

				vecVelocity = pmove.Vector * vel;
				return true;
			}
			else
				prevframe = pmove.EndFrame;
		}
		return false;
	}

	private static float Studio_FindAnimDistance(MStudioAnimDesc anim, float dist) {
		float prevframe = 0;

		if (dist <= 0)
			return 0.0f;

		for (int i = 0; i < anim.NumMovements; i++) {
			MStudioMovement pmove = anim.Movement(i);

			float move = (pmove.V0 + pmove.V1) * 0.5f;

			if (move >= dist) {
				if (MathLib.SolveQuadratic(0.5f * (pmove.V1 - pmove.V0), pmove.V0, -dist, out float root1, out _)) {
					float cpf = 1.0f / (anim.NumFrames - 1);

					return (prevframe + root1 * (pmove.EndFrame - prevframe)) * cpf;
				}
				return 0.0f;
			}
			else {
				dist -= move;
				prevframe = pmove.EndFrame;
			}
		}
		return 1.0f;
	}

	public static bool Studio_SeqVelocity(StudioHdr studioHdr, int sequence, float cycle, ReadOnlySpan<float> poseParameter, out Vector3 vecVelocity) {
		MStudioAnimDesc[] panim = ArrayPool<MStudioAnimDesc>.Shared.Rent(4);
		Span<float> weight = stackalloc float[4];

		MStudioSeqDesc seqdesc = studioHdr.Seqdesc(sequence);
		Studio_SeqAnims(studioHdr, seqdesc, sequence, poseParameter, panim, weight);

		vecVelocity = default;

		bool found = false;

		for (int i = 0; i < 4; i++) {
			if (weight[i] != 0) {
				if (Studio_AnimVelocity(panim[i], cycle, out Vector3 localVelocity)) {
					vecVelocity = vecVelocity + localVelocity * weight[i];
					found = true;
				}
			}
		}

		ArrayPool<MStudioAnimDesc>.Shared.Return(panim, true);
		return found;
	}

	public static float Studio_FindSeqDistance(StudioHdr studioHdr, int sequence, ReadOnlySpan<float> poseParameter, float dist) {
		MStudioAnimDesc[] panim = ArrayPool<MStudioAnimDesc>.Shared.Rent(4);
		Span<float> weight = stackalloc float[4];

		MStudioSeqDesc seqdesc = studioHdr.Seqdesc(sequence);
		Studio_SeqAnims(studioHdr, seqdesc, sequence, poseParameter, panim, weight);

		float cycle = 0;

		for (int i = 0; i < 4; i++) {
			if (weight[i] != 0) {
				float localCycle = Studio_FindAnimDistance(panim[i], dist);
				cycle = cycle + localCycle * weight[i];
			}
		}

		ArrayPool<MStudioAnimDesc>.Shared.Return(panim, true);
		return cycle;
	}
}
