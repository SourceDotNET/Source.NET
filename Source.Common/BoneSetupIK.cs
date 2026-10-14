using Source.Common.Mathematics;

using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Source.Common;

public struct IKSolver
{
	static float FindD(float a, float b, float c) => (c + (a * a - b * b) / c) / 2;
	static float FindE(float a, float d) => MathF.Sqrt(a * a - d * d);

	Vector3 MfwdX, MfwdY, MfwdZ;
	Vector3 MinvX, MinvY, MinvZ;

	public bool Solve(float A, float B, in Vector3 P, in Vector3 D, out Vector3 Q) {
		DefineM(P, D);
		Vector3 R = Rot(MinvX, MinvY, MinvZ, P);
		float r = Length(R);
		float d = FindD(A, B, r);
		float e = FindE(A, d);
		Vector3 S = new(d, e, 0);
		Q = Rot(MfwdX, MfwdY, MfwdZ, S);
		return d > (r - B) && d < A;
	}

	void DefineM(in Vector3 P, in Vector3 D) {
		MinvX = P;
		Normalize(ref MinvX);

		float dDOTx = Dot(D, MinvX);
		MinvY = new(D.X - dDOTx * MinvX.X, D.Y - dDOTx * MinvX.Y, D.Z - dDOTx * MinvX.Z);
		Normalize(ref MinvY);

		MinvZ = Cross(MinvX, MinvY);

		MfwdX = new(MinvX.X, MinvY.X, MinvZ.X);
		MfwdY = new(MinvX.Y, MinvY.Y, MinvZ.Y);
		MfwdZ = new(MinvX.Z, MinvY.Z, MinvZ.Z);
	}

	static float Dot(in Vector3 a, in Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

	static float Length(in Vector3 v) => MathF.Sqrt(Dot(v, v));

	static void Normalize(ref Vector3 v) {
		float norm = Length(v);
		v.X /= norm;
		v.Y /= norm;
		v.Z /= norm;
	}

	static Vector3 Cross(in Vector3 a, in Vector3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

	static Vector3 Rot(in Vector3 m0, in Vector3 m1, in Vector3 m2, in Vector3 src) => new(Dot(m0, src), Dot(m1, src), Dot(m2, src));
}

public ref partial struct BoneSetup
{
	public static bool Studio_SolveIK(MStudioIKChain ikchain, in Vector3 targetFoot, Span<Matrix3x4> boneToWorld) {
		if (ikchain.Link(0).KneeDir.LengthSquared() > 0.0) {
			Vector3 tmp = ikchain.Link(0).KneeDir;
			MathLib.VectorRotate(tmp, boneToWorld[ikchain.Link(0).Bone], out Vector3 targetKneeDir);
			MathLib.MatrixPosition(boneToWorld[ikchain.Link(1).Bone], out Vector3 targetKneePos);
			return Studio_SolveIK(ikchain.Link(0).Bone, ikchain.Link(1).Bone, ikchain.Link(2).Bone, targetFoot, targetKneePos, targetKneeDir, boneToWorld);
		}
		else
			return Studio_SolveIK(ikchain.Link(0).Bone, ikchain.Link(1).Bone, ikchain.Link(2).Bone, targetFoot, boneToWorld);
	}

	const double KNEEMAX_EPSILON = 0.9998;

	public static bool Studio_SolveIK(int thigh, int knee, int foot, in Vector3 targetFoot, Span<Matrix3x4> boneToWorld) {
		MathLib.MatrixPosition(boneToWorld[thigh], out Vector3 worldThigh);
		MathLib.MatrixPosition(boneToWorld[knee], out Vector3 worldKnee);
		MathLib.MatrixPosition(boneToWorld[foot], out Vector3 worldFoot);

		Vector3 ikFoot, ikKnee;

		ikFoot = targetFoot - worldThigh;
		ikKnee = worldKnee - worldThigh;

		float l1 = (worldKnee - worldThigh).Length();
		float l2 = (worldFoot - worldKnee).Length();
		float l3 = (worldFoot - worldThigh).Length();

		if (l3 > (l1 + l2) * KNEEMAX_EPSILON)
			return false;

		Vector3 ikHalf = (worldFoot - worldThigh) * (l1 / l3);

		Vector3 ikKneeDir = ikKnee - ikHalf;
		MathLib.VectorNormalize(ref ikKneeDir);

		return Studio_SolveIK(thigh, knee, foot, targetFoot, worldKnee, ikKneeDir, boneToWorld);
	}

	public static void Studio_AlignIKMatrix(ref Matrix3x4 mat, in Vector3 alignTo) {
		Vector3 tmp1, tmp2, tmp3;

		tmp1 = alignTo;
		MathLib.VectorNormalize(ref tmp1);
		MathLib.MatrixSetColumn(tmp1, 0, ref mat);

		MathLib.MatrixGetColumn(mat, 2, out tmp3);
		tmp2 = Vector3.Cross(tmp3, tmp1);
		MathLib.VectorNormalize(ref tmp2);
		MathLib.MatrixSetColumn(tmp2, 1, ref mat);

		tmp3 = Vector3.Cross(tmp1, tmp2);
		MathLib.MatrixSetColumn(tmp3, 2, ref mat);
	}

	public static bool Studio_SolveIK(int thigh, int knee, int foot, in Vector3 targetFoot, in Vector3 targetKneePos, in Vector3 targetKneeDir, Span<Matrix3x4> boneToWorld) {
		MathLib.MatrixPosition(boneToWorld[thigh], out Vector3 worldThigh);
		MathLib.MatrixPosition(boneToWorld[knee], out Vector3 worldKnee);
		MathLib.MatrixPosition(boneToWorld[foot], out Vector3 worldFoot);

		Vector3 ikFoot, ikTargetKnee, ikKnee;

		ikFoot = targetFoot - worldThigh;
		ikKnee = targetKneePos - worldThigh;

		float l1 = (worldKnee - worldThigh).Length();
		float l2 = (worldFoot - worldKnee).Length();

		float d = (targetFoot - worldThigh).Length() - Math.Min(l1, l2);
		d = Math.Max(l1 + l2, d);
		d = d * 100;

		ikTargetKnee = ikKnee + targetKneeDir * d;

		if (ikFoot.Length() > (l1 + l2) * KNEEMAX_EPSILON) {
			MathLib.VectorNormalize(ref ikFoot);
			ikFoot = ikFoot * (float)((l1 + l2) * KNEEMAX_EPSILON);
		}

		float minDist = (float)Math.Max(MathF.Abs(l1 - l2) * 1.15, Math.Min(l1, l2) * 0.15);
		if (ikFoot.Length() < minDist) {
			ikFoot = worldFoot - worldThigh;
			MathLib.VectorNormalize(ref ikFoot);
			ikFoot = ikFoot * minDist;
		}

		IKSolver ik = default;
		if (ik.Solve(l1, l2, ikFoot, ikTargetKnee, out ikKnee)) {
			ref Matrix3x4 mWorldThigh = ref boneToWorld[thigh];
			ref Matrix3x4 mWorldKnee = ref boneToWorld[knee];
			ref Matrix3x4 mWorldFoot = ref boneToWorld[foot];

			Studio_AlignIKMatrix(ref mWorldThigh, ikKnee);
			Studio_AlignIKMatrix(ref mWorldKnee, ikFoot - ikKnee);

			mWorldKnee[0, 3] = ikKnee.X + worldThigh.X;
			mWorldKnee[1, 3] = ikKnee.Y + worldThigh.Y;
			mWorldKnee[2, 3] = ikKnee.Z + worldThigh.Z;

			mWorldFoot[0, 3] = ikFoot.X + worldThigh.X;
			mWorldFoot[1, 3] = ikFoot.Y + worldThigh.Y;
			mWorldFoot[2, 3] = ikFoot.Z + worldThigh.Z;

			return true;
		}
		else
			return false;
	}
}

public struct IKContextIKRule
{
	public int Index;

	public int Type;
	public int Chain;

	public int Bone;

	public int Slot;
	public float Height;
	public float Radius;
	public float Floor;
	public Vector3 Pos;
	public Quaternion Q;

	public float Start;
	public float Peak;
	public float Tail;
	public float End;

	public float Top;
	public float Drop;

	public float Commit;
	public float Release;

	public float Weight;
	public float RuleWeight;
	public float Latched;
	public string? Label;

	public Vector3 KneeDir;
	public Vector3 KneePos;
}

public ref partial struct BoneSetup
{
	public static void QuaternionAccumulate(in Quaternion p, float s, in Quaternion q, out Quaternion qt) {
		MathLib.QuaternionAlign(p, q, out Quaternion q2);

		Quaternion result;
		result.X = p.X + s * q2.X;
		result.Y = p.Y + s * q2.Y;
		result.Z = p.Z + s * q2.Z;
		result.W = p.W + s * q2.W;
		qt = result;
	}

	static void CalcDecompressedAnimation(MStudioCompressedIKError compressed, int frame, TimeUnit_t fraq, out Vector3 pos, out Quaternion q) {
		if (fraq > 0.0001f) {
			Vector3 p1, p2;
			ExtractAnimValue(frame, compressed.Animvalue(0), compressed.Scale[0], out p1.X, out p2.X);
			ExtractAnimValue(frame, compressed.Animvalue(1), compressed.Scale[1], out p1.Y, out p2.Y);
			ExtractAnimValue(frame, compressed.Animvalue(2), compressed.Scale[2], out p1.Z, out p2.Z);
			pos = p1 * (float)(1 - fraq) + p2 * (float)fraq;

			Quaternion q1, q2;
			RadianEuler angle1, angle2;
			ExtractAnimValue(frame, compressed.Animvalue(3), compressed.Scale[3], out angle1.X, out angle2.X);
			ExtractAnimValue(frame, compressed.Animvalue(4), compressed.Scale[4], out angle1.Y, out angle2.Y);
			ExtractAnimValue(frame, compressed.Animvalue(5), compressed.Scale[5], out angle1.Z, out angle2.Z);

			if (angle1.X != angle2.X || angle1.Y != angle2.Y || angle1.Z != angle2.Z) {
				MathLib.AngleQuaternion(angle1, out q1);
				MathLib.AngleQuaternion(angle2, out q2);
				MathLib.QuaternionBlend(q1, q2, (float)fraq, out q);
			}
			else
				MathLib.AngleQuaternion(angle1, out q);
		}
		else {
			ExtractAnimValue(frame, compressed.Animvalue(0), compressed.Scale[0], out pos.X);
			ExtractAnimValue(frame, compressed.Animvalue(1), compressed.Scale[1], out pos.Y);
			ExtractAnimValue(frame, compressed.Animvalue(2), compressed.Scale[2], out pos.Z);

			RadianEuler angle;
			ExtractAnimValue(frame, compressed.Animvalue(3), compressed.Scale[3], out angle.X);
			ExtractAnimValue(frame, compressed.Animvalue(4), compressed.Scale[4], out angle.Y);
			ExtractAnimValue(frame, compressed.Animvalue(5), compressed.Scale[5], out angle.Z);

			MathLib.AngleQuaternion(angle, out q);
		}
	}

	public static float Studio_IKRuleWeight(MStudioIKRule ikRule, MStudioAnimDesc anim, TimeUnit_t cycle, out int frame, out TimeUnit_t fraq) {
		if (ikRule.End > 1.0f && cycle < ikRule.Start)
			cycle = cycle + 1.0f;

		TimeUnit_t value = 0.0;
		fraq = (anim.NumFrames - 1) * (cycle - ikRule.Start) + ikRule.StartFrame;
		frame = (int)fraq;
		fraq = fraq - frame;

		if (cycle < ikRule.Start) {
			frame = ikRule.StartFrame;
			fraq = 0.0;
			return 0.0f;
		}
		else if (cycle < ikRule.Peak)
			value = (cycle - ikRule.Start) / (ikRule.Peak - ikRule.Start);
		else if (cycle < ikRule.Tail)
			return 1.0f;
		else if (cycle < ikRule.End)
			value = 1.0 - ((cycle - ikRule.Tail) / (ikRule.End - ikRule.Tail));
		else {
			fraq = (anim.NumFrames - 1) * (ikRule.End - ikRule.Start) + ikRule.StartFrame;
			frame = (int)fraq;
			fraq = fraq - frame;
		}
		return (float)MathLib.SimpleSpline(value);
	}

	public static float Studio_IKRuleWeight(ref IKContextIKRule ikRule, TimeUnit_t cycle) {
		if (ikRule.End > 1.0f && cycle < ikRule.Start)
			cycle = cycle + 1.0f;

		TimeUnit_t value = 0.0;
		if (cycle < ikRule.Start)
			return 0.0f;
		else if (cycle < ikRule.Peak)
			value = (cycle - ikRule.Start) / (ikRule.Peak - ikRule.Start);
		else if (cycle < ikRule.Tail)
			return 1.0f;
		else if (cycle < ikRule.End)
			value = 1.0 - ((cycle - ikRule.Tail) / (ikRule.End - ikRule.Tail));

		return (float)(3.0 * value * value - 2.0 * value * value * value);
	}

	public static bool Studio_IKShouldLatch(ref IKContextIKRule ikRule, TimeUnit_t cycle) {
		if (ikRule.End > 1.0f && cycle < ikRule.Start)
			cycle = cycle + 1.0f;

		if (cycle < ikRule.Peak)
			return false;
		else if (cycle < ikRule.End)
			return true;

		return false;
	}

	public static float Studio_IKTail(ref IKContextIKRule ikRule, TimeUnit_t cycle) {
		if (ikRule.End > 1.0f && cycle < ikRule.Start)
			cycle = cycle + 1.0f;

		if (cycle <= ikRule.Tail)
			return 0.0f;
		else if (cycle < ikRule.End)
			return (float)((cycle - ikRule.Tail) / (ikRule.End - ikRule.Tail));

		return 0.0f;
	}

	public static bool Studio_IKAnimationError(StudioHdr studioHdr, MStudioIKRule rule, MStudioAnimDesc anim, TimeUnit_t cycle, out Vector3 pos, out Quaternion q, out float weight) {
		weight = Studio_IKRuleWeight(rule, anim, cycle, out int frame, out TimeUnit_t fraq);
		Assert(fraq >= 0.0 && fraq < 1.0);
		Assert(weight >= 0.0f && weight <= 1.0f);

		weight = Math.Clamp(weight, 0.0f, 1.0f);

		pos = default;
		q = default;

		if (rule.Type != Studio.IK_GROUND && weight < 0.0001)
			return false;

		ReadOnlySpan<MStudioIKError> error = rule.Error(frame);
		if (!error.IsEmpty) {
			if (fraq < 0.001) {
				q = error[0].Q;
				pos = error[0].Pos;
			}
			else {
				MathLib.QuaternionBlend(error[0].Q, error[1].Q, (float)fraq, out q);
				pos = error[0].Pos * (float)(1.0 - fraq) + error[1].Pos * (float)fraq;
			}
			return true;
		}

		MStudioCompressedIKError compressed = rule.CompressedError();
		CalcDecompressedAnimation(compressed, frame - rule.StartFrame, fraq, out pos, out q);
		return true;
	}

	public static bool Studio_IKSequenceError(StudioHdr studioHdr, MStudioSeqDesc seqdesc, int sequence, TimeUnit_t cycle, int rule, ReadOnlySpan<float> poseParameter, Span<MStudioAnimDesc> panim, Span<float> weight, ref IKContextIKRule ikRule) {
		int i;

		if (panim[0] == null) {
			Warning("Crash prevented: Studio_IKSequenceError: panim has a nullptr element\n");
			return false;
		}

		ikRule = default;
		ikRule.Start = ikRule.Peak = ikRule.Tail = ikRule.End = 0;

		MStudioIKRule? prevRule = null;

		for (i = 0; i < 4; i++) {
			if (weight[i] != 0) {
				if (rule >= panim[i].NumIKRules || panim[i].NumIKRules != panim[0].NumIKRules) {
					Assert(false);
					return false;
				}

				MStudioIKRule? pRule = panim[i].IKRule(rule);
				if (pRule == null)
					return false;

				float dt = 0.0f;
				if (prevRule != null) {
					if (pRule.Start - prevRule.Start > 0.5)
						dt = -1.0f;
					else if (pRule.Start - prevRule.Start < -0.5)
						dt = 1.0f;
				}
				else
					prevRule = pRule;

				ikRule.Start += (pRule.Start + dt) * weight[i];
				ikRule.Peak += (pRule.Peak + dt) * weight[i];
				ikRule.Tail += (pRule.Tail + dt) * weight[i];
				ikRule.End += (pRule.End + dt) * weight[i];
			}
		}
		if (ikRule.Start > 1.0) {
			ikRule.Start -= 1.0f;
			ikRule.Peak -= 1.0f;
			ikRule.Tail -= 1.0f;
			ikRule.End -= 1.0f;
		}
		else if (ikRule.Start < 0.0) {
			ikRule.Start += 1.0f;
			ikRule.Peak += 1.0f;
			ikRule.Tail += 1.0f;
			ikRule.End += 1.0f;
		}

		ikRule.Weight = Studio_IKRuleWeight(ref ikRule, cycle);
		if (ikRule.Weight <= 0.001f) {
			if (panim[0].IKRule(rule) == null)
				return false;
			if ((panim[0].Flags & StudioAnimSeqFlags.Looping) != 0 && panim[0].IKRule(rule)!.Type == Studio.IK_GROUND && ikRule.End - ikRule.Start > 0.75) {
				ikRule.Weight = 0.001f;
				cycle = ikRule.End - 0.001;
			}
			else
				return false;
		}

		Assert(ikRule.Weight > 0.0f);

		ikRule.Pos = default;
		ikRule.Q = default;

		float total = 0.0f;
		for (i = 0; i < 4; i++) {
			if (weight[i] != 0) {
				MStudioIKRule? pRule = panim[i].IKRule(rule);
				if (pRule == null)
					return false;

				ikRule.Chain = pRule.Chain;
				ikRule.Bone = pRule.Bone;
				ikRule.Type = pRule.Type;
				ikRule.Slot = pRule.Slot;

				ikRule.Height += pRule.Height * weight[i];
				ikRule.Floor += pRule.Floor * weight[i];
				ikRule.Radius += pRule.Radius * weight[i];
				ikRule.Drop += pRule.Drop * weight[i];
				ikRule.Top += pRule.Top * weight[i];

				ikRule.Release += Studio_IKTail(ref ikRule, cycle) * weight[i];

				switch (ikRule.Type) {
					case Studio.IK_SELF:
					case Studio.IK_WORLD:
					case Studio.IK_GROUND:
					case Studio.IK_ATTACHMENT: {
							bool result = Studio_IKAnimationError(studioHdr, pRule, panim[i], cycle, out Vector3 pos1, out Quaternion q1, out _);

							if (result) {
								ikRule.Pos = ikRule.Pos + pos1 * weight[i];
								QuaternionAccumulate(ikRule.Q, weight[i], q1, out ikRule.Q);
								total += weight[i];
							}
						}
						break;
					default:
						total += weight[i];
						break;
				}

				ikRule.Latched = (Studio_IKShouldLatch(ref ikRule, cycle) ? 1 : 0) * ikRule.Weight;

				if (ikRule.Type == Studio.IK_ATTACHMENT)
					ikRule.Label = pRule.Attachment();
			}
		}

		if (total <= 0.0001f)
			return false;

		if (total < 0.999f) {
			ikRule.Pos = ikRule.Pos * (1.0f / total);
			MathLib.QuaternionScale(ikRule.Q, 1.0f / total, out Quaternion scaledQ);
			ikRule.Q = scaledQ;
		}

		if (ikRule.Type == Studio.IK_SELF && ikRule.Bone != -1) {
			ikRule.Bone = studioHdr.RemapSeqBone(sequence, ikRule.Bone);
			if (ikRule.Bone == -1)
				return false;
		}

		MathLib.QuaternionNormalize(ref ikRule.Q);
		return true;
	}
}

public class IKTarget
{
	public struct OffsetData
	{
		public string? AttachmentName;
		public Vector3 Pos;
		public Quaternion Q;
	}

	public struct IdealData
	{
		public Vector3 Pos;
		public Quaternion Q;
	}

	public struct EstimateData
	{
		public float Latched;
		public float Release;
		public float Height;
		public float Floor;
		public float Radius;
		public TimeUnit_t Time;
		public float Weight;
		public Vector3 Pos;
		public Quaternion Q;
		public bool OnWorld;
	}

	public struct TraceData
	{
		public float HipToFoot;
		public float HipToKnee;
		public float KneeToFoot;
		public Vector3 Hip;
		public Vector3 Closest;
		public Vector3 Knee;
		public Vector3 Farthest;
		public Vector3 Lowest;
	}

	public struct LatchedData
	{
		public bool NeedsLatch;
		public bool HasLatch;
		public float Influence;
		public int FrameCounter;
		public int Owner;
		public Vector3 AbsOrigin;
		public QAngle AbsAngles;
		public Vector3 Pos;
		public Quaternion Q;
		public Vector3 DeltaPos;
		public Quaternion DeltaQ;
		public Vector3 DebouncePos;
		public Quaternion DebounceQ;
	}

	public struct ErrorData
	{
		public TimeUnit_t Time;
		public TimeUnit_t ErrorTime;
		public float Ramp;
		public bool InError;
	}

	public int Chain;
	public int Type;

	public OffsetData Offset;
	internal IdealData Ideal;
	public EstimateData Est;
	public TraceData Trace;
	internal LatchedData Latched;
	public ErrorData Error;

	public void SetOwner(int entindex, in Vector3 pos, in QAngle angles) {
		Latched.Owner = entindex;
		Latched.AbsOrigin = pos;
		Latched.AbsAngles = angles;
	}

	public void ClearOwner() {
		Latched.Owner = -1;
	}

	public int GetOwner() => Latched.Owner;

	public void UpdateOwner(int entindex, in Vector3 pos, in QAngle angles) {
		if (pos == Latched.AbsOrigin && angles == Latched.AbsAngles)
			return;

		MathLib.AngleMatrix(angles, pos, out Matrix3x4 inMat);
		MathLib.AngleIMatrix(Latched.AbsAngles, Latched.AbsOrigin, out Matrix3x4 outMat);

		MathLib.QuaternionMatrix(Latched.Q, Latched.Pos, out Matrix3x4 tmp1);
		MathLib.ConcatTransforms(outMat, tmp1, out Matrix3x4 tmp2);
		MathLib.ConcatTransforms(inMat, tmp2, out tmp1);
		MathLib.MatrixAngles(tmp1, out Latched.Q, out Latched.Pos);
	}

	public void SetPos(in Vector3 pos) {
		Est.Pos = pos;
	}

	public void SetAngles(in QAngle angles) {
		MathLib.AngleQuaternion(angles, out Est.Q);
	}

	public void SetQuaternion(in Quaternion q) {
		Est.Q = q;
	}

	public void SetNormal(in Vector3 normal) {
		Vector3 forward, right;
		MathLib.QuaternionMatrix(Est.Q, out Matrix3x4 m1);

		MathLib.MatrixGetColumn(m1, 1, out right);
		MathLib.CrossProduct(right, normal, out forward);
		MathLib.CrossProduct(normal, forward, out right);
		MathLib.MatrixSetColumn(forward, 0, ref m1);
		MathLib.MatrixSetColumn(right, 1, ref m1);
		MathLib.MatrixSetColumn(normal, 2, ref m1);
		MathLib.MatrixAngles(m1, out Est.Q, out _);
	}

	public void SetPosWithNormalOffset(in Vector3 pos, in Vector3 normal) {
		Est.Pos = pos;
		if (normal.Z > 0.9999)
			return;
		else if (normal.Z > 0.707) {
			float tan = MathF.Sqrt(1 - normal.Z * normal.Z) / normal.Z;
			Est.Pos.Z = Est.Pos.Z - Est.Radius * tan;
		}
		else
			Est.Pos.Z = Est.Pos.Z - Est.Radius;
	}

	public void SetOnWorld(bool onWorld = true) {
		Est.OnWorld = onWorld;
	}

	public bool IsActive() => Est.Weight > 0.0f;

	public void IKFailed() {
		Latched.DeltaPos = default;
		Latched.DeltaQ = default;
		Latched.Pos = Ideal.Pos;
		Latched.Q = Ideal.Q;
		Est.Latched = 0.0f;
		Est.Weight = 0.0f;
		Est.OnWorld = false;
	}

	public void MoveReferenceFrame(in Vector3 deltaPos, in QAngle deltaAngles) {
		Est.Pos -= deltaPos;
		Latched.Pos -= deltaPos;
		Offset.Pos -= deltaPos;
		Ideal.Pos -= deltaPos;
	}
}

public struct IKChainResult
{
	public int Target;
	public Vector3 Pos;
	public Quaternion Q;
	public float Weight;
}

public ref partial struct BoneSetup
{
	public static void SolveBone(StudioHdr studioHdr, int bone, Span<Matrix3x4> boneToWorld, Span<Vector3> pos, Span<Quaternion> q) {
		int parent = studioHdr.BoneParent(bone);

		MathLib.MatrixInvert(boneToWorld[parent], out Matrix3x4 worldToBone);

		MathLib.ConcatTransforms(worldToBone, boneToWorld[bone], out Matrix3x4 local);

		MathLib.MatrixAngles(local, out q[bone], out pos[bone]);
	}
}

public class IKContext
{
	static readonly BoneSetupMemoryPool<Matrix3x4> MatrixPool = new();

	public readonly List<IKTarget> Target = new(12);

	StudioHdr? studioHdr;

	readonly List<List<IKContextIKRule>> ikChainRule = [];
	readonly List<IKContextIKRule> ikLock = [];
	Matrix3x4 rootxform;

	int frameCounter;
	TimeUnit_t time;
	int boneMask;

	public IKContext() {
		frameCounter = -1;
		studioHdr = null;
		time = -1.0;
	}

	void ResetTargets() {
		Target.Clear();
		for (int i = 0; i < 12; i++)
			Target.Add(new IKTarget());
		ClearTargets();
	}

	public void Init(StudioHdr studioHdr, in QAngle angles, in Vector3 pos, TimeUnit_t time, int frameCounter, int boneMask) {
		this.studioHdr = studioHdr;
		ikChainRule.Clear();
		if (studioHdr.GetNumIKChains() != 0) {
			for (int i = 0; i < studioHdr.GetNumIKChains(); i++)
				ikChainRule.Add([]);

			if (Target.Count == 0)
				ResetTargets();
		}
		else
			Target.Clear();

		MathLib.AngleMatrix(angles, pos, out rootxform);
		this.frameCounter = frameCounter;
		this.time = time;
		this.boneMask = boneMask;
	}

	public void AddDependencies(MStudioSeqDesc seqdesc, int sequence, TimeUnit_t cycle, ReadOnlySpan<float> poseParameters, float weight = 1.0f) {
		int i;

		if (studioHdr!.GetNumIKChains() == 0)
			return;

		if (seqdesc.NumIKRules == 0)
			return;

		IKContextIKRule ikrule = default;

		Assert(weight >= 0.0f && weight <= 1.0f);
		weight = Math.Clamp(weight, 0.0f, 1.0f);

		if ((seqdesc.Flags & StudioAnimSeqFlags.Realtime) != 0) {
			float cps = BoneSetup.Studio_CPS(studioHdr, seqdesc, sequence, poseParameters);
			cycle = time * cps;
			cycle = cycle - (int)cycle;
		}
		else if (cycle < 0 || cycle >= 1) {
			if ((seqdesc.Flags & StudioAnimSeqFlags.Looping) != 0) {
				cycle = cycle - (int)cycle;
				if (cycle < 0)
					cycle += 1;
			}
			else
				cycle = Math.Max(0.0, Math.Min(cycle, 0.9999));
		}

		MStudioAnimDesc[] panim = ArrayPool<MStudioAnimDesc>.Shared.Rent(4);
		Span<float> animWeight = stackalloc float[4];

		BoneSetup.Studio_SeqAnims(studioHdr, seqdesc, sequence, poseParameters, panim, animWeight);

		for (i = 0; i < seqdesc.NumIKRules; i++) {
			if (!BoneSetup.Studio_IKSequenceError(studioHdr, seqdesc, sequence, cycle, i, poseParameters, panim, animWeight, ref ikrule))
				continue;

			if (ikrule.Chain < 0 || ikrule.Chain >= studioHdr.GetNumIKChains() || studioHdr.IKChain(ikrule.Chain).NumLinks <= 2)
				continue;

			int bone = studioHdr.IKChain(ikrule.Chain).Link(2).Bone;
			if (bone >= studioHdr.NumBones() || ikrule.Bone >= studioHdr.NumBones()) {
				Warning($"Out of bounds bone in CIKContext::AddDependencies {bone} / {ikrule.Bone} of {studioHdr.NumBones()}\n");
				continue;
			}

			if ((studioHdr.BoneFlags(bone) & boneMask) == 0)
				continue;

			if (ikrule.Bone >= 0 && (studioHdr.BoneFlags(ikrule.Bone) & boneMask) == 0)
				continue;

			if (Target.Count == 0)
				ResetTargets();

			ikrule.RuleWeight = weight;

			if (ikrule.RuleWeight * ikrule.Weight > 0.999) {
				if (ikrule.Type != Studio.IK_UNLATCH) {
					ikChainRule[ikrule.Chain].Clear();
					if (ikrule.Type == Studio.IK_RELEASE)
						continue;
				}
			}

			ikChainRule[ikrule.Chain].Add(ikrule);
		}

		ArrayPool<MStudioAnimDesc>.Shared.Return(panim, true);
	}

	public void AddAutoplayLocks(Span<Vector3> pos, Span<Quaternion> q) {
		if (studioHdr!.GetNumIKAutoplayLocks() == 0)
			return;

		if (studioHdr.GetNumIKChains() == 0 && studioHdr.GetNumIKAutoplayLocks() != 0)
			return;

		Matrix3x4[] boneToWorld = MatrixPool.Alloc();
		BoneBitList boneComputed = new();

		int ikOffset = ikLock.Count;
		for (int i = 0; i < studioHdr.GetNumIKAutoplayLocks(); i++)
			ikLock.Add(default);

		for (int i = 0; i < studioHdr.GetNumIKAutoplayLocks(); i++) {
			MStudioIKLock lck = studioHdr.IKAutoplayLock(i);

			if (!IsValidChain(lck.Chain)) {
				Warning($"Model has IK errors.. {lck.Chain}/{studioHdr.GetNumIKChains()}\n");
				MatrixPool.Free(boneToWorld);
				return;
			}

			MStudioIKChain chain = studioHdr.IKChain(lck.Chain);
			int bone = chain.Link(2).Bone;

			if ((studioHdr.BoneFlags(bone) & boneMask) == 0)
				continue;

			BuildBoneChain(pos, q, bone, boneToWorld, ref boneComputed);

			ref IKContextIKRule ikrule = ref CollectionsMarshal.AsSpan(ikLock)[i + ikOffset];

			ikrule.Chain = lck.Chain;
			ikrule.Slot = i;
			ikrule.Type = Studio.IK_WORLD;

			MathLib.MatrixAngles(boneToWorld[bone], out ikrule.Q, out ikrule.Pos);

			if (chain.Link(0).KneeDir.LengthSquared() > 0.0) {
				MathLib.VectorRotate(chain.Link(0).KneeDir, boneToWorld[chain.Link(0).Bone], out ikrule.KneeDir);
				MathLib.MatrixPosition(boneToWorld[chain.Link(1).Bone], out ikrule.KneePos);
			}
			else
				ikrule.KneeDir = default;
		}
		MatrixPool.Free(boneToWorld);
	}

	public void AddSequenceLocks(MStudioSeqDesc seqdesc, Span<Vector3> pos, Span<Quaternion> q) {
		if (studioHdr!.GetNumIKChains() == 0)
			return;

		if (seqdesc.NumIKLocks == 0)
			return;

		Matrix3x4[] boneToWorld = MatrixPool.Alloc();
		BoneBitList boneComputed = new();

		int ikOffset = ikLock.Count;
		for (int i = 0; i < seqdesc.NumIKLocks; i++)
			ikLock.Add(default);

		for (int i = 0; i < seqdesc.NumIKLocks; i++) {
			MStudioIKLock lck = seqdesc.IKLock(i);
			if (!IsValidChain(lck.Chain))
				continue;

			MStudioIKChain chain = studioHdr.IKChain(lck.Chain);
			int bone = chain.Link(2).Bone;

			if ((studioHdr.BoneFlags(bone) & boneMask) == 0)
				continue;

			BuildBoneChain(pos, q, bone, boneToWorld, ref boneComputed);

			ref IKContextIKRule ikrule = ref CollectionsMarshal.AsSpan(ikLock)[i + ikOffset];
			ikrule.Chain = i;
			ikrule.Slot = i;
			ikrule.Type = Studio.IK_WORLD;

			MathLib.MatrixAngles(boneToWorld[bone], out ikrule.Q, out ikrule.Pos);

			if (chain.Link(0).KneeDir.LengthSquared() > 0.0)
				MathLib.VectorRotate(chain.Link(0).KneeDir, boneToWorld[chain.Link(0).Bone], out ikrule.KneeDir);
			else
				ikrule.KneeDir = default;
		}
		MatrixPool.Free(boneToWorld);
	}

	bool IsValidChain(int chain) {
		if (chain < 0 || chain >= studioHdr!.GetNumIKChains())
			return false;

		MStudioIKChain ikChain = studioHdr.IKChain(chain);
		if (ikChain.NumLinks < 3)
			return false;

		for (int i = 0; i < 3; i++) {
			int bone = ikChain.Link(i).Bone;
			if (bone < 0 || bone >= studioHdr.NumBones())
				return false;
		}

		return true;
	}

	void BuildBoneChain(ReadOnlySpan<Vector3> pos, ReadOnlySpan<Quaternion> q, int bone, Span<Matrix3x4> boneToWorld, ref BoneBitList boneComputed) {
		Assert((studioHdr!.BoneFlags(bone) & boneMask) != 0);
		BoneSetup.BuildBoneChain(studioHdr, rootxform, pos, q, bone, boneToWorld, ref boneComputed);
	}

	public void ClearTargets() {
		for (int i = 0; i < Target.Count; i++)
			Target[i].Latched.FrameCounter = -9999;
	}

	public void UpdateTargets(Span<Vector3> pos, Span<Quaternion> q, Span<Matrix3x4> boneToWorld, ref BoneBitList boneComputed) {
		int i, j;

		for (i = 0; i < Target.Count; i++) {
			Target[i].Est.Weight = 0.0f;
			Target[i].Est.Latched = 1.0f;
			Target[i].Est.Release = 1.0f;
			Target[i].Est.Height = 0.0f;
			Target[i].Est.Floor = 0.0f;
			Target[i].Est.Radius = 0.0f;
			Target[i].Offset.Pos = default;
			Target[i].Offset.Q = default;
		}

		AutoIKRelease();

		for (j = 0; j < ikChainRule.Count; j++) {
			for (i = 0; i < ikChainRule[j].Count; i++) {
				ref IKContextIKRule rule = ref CollectionsMarshal.AsSpan(ikChainRule[j])[i];

				if (rule.Slot < 0 || rule.Slot >= Target.Count)
					continue;

				switch (rule.Type) {
					case Studio.IK_ATTACHMENT:
					case Studio.IK_GROUND: {
							IKTarget target = Target[rule.Slot];
							target.Chain = rule.Chain;
							target.Type = rule.Type;

							if (rule.Type == Studio.IK_ATTACHMENT)
								target.Offset.AttachmentName = rule.Label;
							else
								target.Offset.AttachmentName = null;

							if (rule.RuleWeight == 1.0f || target.Est.Weight == 0.0f) {
								target.Offset.Q = rule.Q;
								target.Offset.Pos = rule.Pos;
								target.Est.Height = rule.Height;
								target.Est.Floor = rule.Floor;
								target.Est.Radius = rule.Radius;
								target.Est.Latched = rule.Latched * rule.RuleWeight;
								target.Est.Release = rule.Release;
								target.Est.Weight = rule.Weight * rule.RuleWeight;
							}
							else {
								MathLib.QuaternionSlerp(target.Offset.Q, rule.Q, rule.RuleWeight, out Quaternion offsetQ);
								target.Offset.Q = offsetQ;
								target.Offset.Pos = target.Offset.Pos + (rule.Pos - target.Offset.Pos) * rule.RuleWeight;
								target.Est.Height = MathLib.Lerp(rule.RuleWeight, target.Est.Height, rule.Height);
								target.Est.Floor = MathLib.Lerp(rule.RuleWeight, target.Est.Floor, rule.Floor);
								target.Est.Radius = MathLib.Lerp(rule.RuleWeight, target.Est.Radius, rule.Radius);
								target.Est.Latched = Math.Min(target.Est.Latched, rule.Latched);
								target.Est.Release = MathLib.Lerp(rule.RuleWeight, target.Est.Release, rule.Release);
								target.Est.Weight = MathLib.Lerp(rule.RuleWeight, target.Est.Weight, rule.Weight);
							}

							if (rule.Type == Studio.IK_GROUND) {
								target.Latched.DeltaPos.Z = 0;
								target.Est.Pos.Z = target.Est.Floor + rootxform[2][3];
							}
						}
						break;
					case Studio.IK_UNLATCH: {
							IKTarget target = Target[rule.Slot];
							if (rule.Latched > 0.0)
								target.Est.Latched = 0.0f;
							else
								target.Est.Latched = Math.Min(target.Est.Latched, 1.0f - rule.Weight);
						}
						break;
					case Studio.IK_RELEASE: {
							IKTarget target = Target[rule.Slot];
							if (rule.Latched > 0.0)
								target.Est.Latched = 0.0f;
							else
								target.Est.Latched = Math.Min(target.Est.Latched, 1.0f - rule.Weight);

							target.Est.Weight = target.Est.Weight * (1 - rule.Weight * rule.RuleWeight);
						}
						break;
				}
			}
		}

		for (i = 0; i < Target.Count; i++) {
			IKTarget target = Target[i];
			if (target.Est.Weight > 0.0) {
				MStudioIKChain chain = studioHdr!.IKChain(target.Chain);
				int bone = chain.Link(2).Bone;

				BuildBoneChain(pos, q, bone, boneToWorld, ref boneComputed);

				MathLib.QuaternionMatrix(target.Offset.Q, target.Offset.Pos, out Matrix3x4 local);
				MathLib.MatrixInvert(local, out Matrix3x4 localInv);
				MathLib.ConcatTransforms(boneToWorld[bone], localInv, out Matrix3x4 worldFootpad);

				if (target.Est.Latched == 1.0)
					target.Latched.NeedsLatch = true;
				else
					target.Latched.NeedsLatch = false;

				if (frameCounter < 0 || target.Latched.FrameCounter < frameCounter - 1 || target.Latched.FrameCounter > frameCounter) {
					target.Latched.HasLatch = false;
					target.Latched.Influence = 0.0f;
				}
				target.Latched.FrameCounter = frameCounter;

				MathLib.MatrixAngles(worldFootpad, out target.Ideal.Q, out target.Ideal.Pos);
				target.Est.Q = target.Ideal.Q;
				target.Est.Pos = target.Ideal.Pos;

				float latched = target.Est.Latched;

				if (target.Latched.HasLatch) {
					if (target.Est.Latched == 1.0) {
						target.Latched.DeltaPos = target.Latched.Pos - target.Est.Pos;
						MathLib.QuaternionSM(-1, target.Est.Q, target.Latched.Q, ref target.Latched.DeltaQ);
						target.Est.Q = target.Latched.Q;
						target.Est.Pos = target.Latched.Pos;
					}
					else if (target.Est.Latched > 0.0) {
						if (latched > 0 && latched < target.Latched.Influence) {
							float dt = target.Latched.Influence - latched;
							if (target.Latched.Influence > 0.0)
								dt = dt / target.Latched.Influence;

							target.Latched.DeltaPos = target.Latched.DeltaPos * (1 - dt);
							MathLib.QuaternionScale(target.Latched.DeltaQ, 1 - dt, out Quaternion deltaQ);
							target.Latched.DeltaQ = deltaQ;
						}

						target.Est.Pos = target.Est.Pos + target.Latched.DeltaPos;
						MathLib.QuaternionMA(target.Est.Q, 1, target.Latched.DeltaQ, ref target.Est.Q);
						target.Latched.Q = target.Est.Q;
						target.Latched.Pos = target.Est.Pos;
					}
					else {
						target.Latched.HasLatch = false;
						target.Latched.Q = target.Est.Q;
						target.Latched.Pos = target.Est.Pos;
						target.Latched.DeltaPos = default;
						target.Latched.DeltaQ = default;
					}
					target.Latched.Influence = latched;
				}

				MathLib.MatrixPosition(boneToWorld[chain.Link(0).Bone], out Vector3 p1);
				MathLib.MatrixPosition(boneToWorld[chain.Link(1).Bone], out Vector3 p2);
				MathLib.MatrixPosition(boneToWorld[chain.Link(2).Bone], out Vector3 p3);

				float d1 = (p2 - p1).Length();
				float d2 = (p3 - p2).Length();

				if (target.Latched.HasLatch) {
					float d4 = (p3 + target.Latched.DeltaPos - p1).Length();

					if ((d4 < MathF.Abs(d1 - d2) || d4 * 0.95 > d1 + d2) && target.Est.Latched > 0.2)
						target.Error.Time = time;

					if (target.Est.Latched > 0.2) {
						float d = MathF.Abs(target.Latched.DeltaQ.W) * 2.0f - 1.0f;

						if (d < 0.707)
							target.Error.Time = time;
					}
				}

				Vector3 dir = target.Est.Pos - p1;
				target.Trace.HipToFoot = MathLib.VectorNormalize(ref dir);
				target.Trace.HipToKnee = d1;
				target.Trace.KneeToFoot = d2;
				target.Trace.Hip = p1;
				target.Trace.Knee = p2;
				target.Trace.Closest = p1 + dir * (float)(MathF.Abs(d1 - d2) * 1.01);
				target.Trace.Farthest = p1 + dir * (d1 + d2) * 0.99f;
				target.Trace.Lowest = p1 + new Vector3(0, 0, -1) * (d1 + d2) * 0.99f;
			}
		}
	}

	public void AutoIKRelease() {
		int i;

		for (i = 0; i < Target.Count; i++) {
			IKTarget target = Target[i];

			TimeUnit_t dt = time - target.Error.Time;
			if (target.Error.InError || dt < 0.5) {
				if (!target.Error.InError) {
					target.Error.Ramp = 0.0f;
					target.Error.ErrorTime = target.Error.Time;
					target.Error.InError = true;
				}

				TimeUnit_t ft = time - target.Error.ErrorTime;
				if (dt < 0.25)
					target.Error.Ramp = (float)Math.Min(target.Error.Ramp + ft * 4.0, 1.0);
				else
					target.Error.Ramp = (float)Math.Max(target.Error.Ramp - ft * 4.0, 0.0);

				if (target.Error.Ramp > 0.0) {
					IKContextIKRule ikrule = default;

					ikrule.Chain = target.Chain;
					ikrule.Bone = 0;
					ikrule.Type = Studio.IK_RELEASE;
					ikrule.Slot = i;
					ikrule.Weight = MathLib.SimpleSpline(target.Error.Ramp);
					ikrule.RuleWeight = 1.0f;
					ikrule.Latched = dt < 0.25 ? 0.0f : ikrule.Weight;

					if (IsValidChain(target.Chain)) {
						MStudioIKChain chain = studioHdr.IKChain(target.Chain);
						int bone = chain.Link(2).Bone;
						if (bone >= 0 && bone < studioHdr.NumBones()) {
							if ((studioHdr.BoneFlags(bone) & boneMask) == 0) {
								target.Error.InError = false;
								continue;
							}

							ikChainRule[ikrule.Chain].Add(ikrule);
						}
						else
							DevWarning(1, $"AutoIKRelease ({studioHdr.GetRenderHdr().GetName()}) got an out of range bone {bone} ({studioHdr.NumBones()})\n");
					}
					else
						DevWarning(1, $"AutoIKRelease ({studioHdr!.GetRenderHdr().GetName()}) got an out of range chain {target.Chain} ({studioHdr.GetNumIKChains()})\n");
				}
				else
					target.Error.InError = false;

				target.Error.ErrorTime = time;
			}
		}
	}

	public void SolveDependencies(Span<Vector3> pos, Span<Quaternion> q, Span<Matrix3x4> boneToWorld, ref BoneBitList boneComputed) {
		Matrix3x4 worldTarget;
		int i, j;

		Span<IKChainResult> chainResult = stackalloc IKChainResult[32];
		chainResult.Clear();

		for (i = 0; i < studioHdr!.GetNumIKChains(); i++) {
			if (i >= 32)
				continue;

			ref IKChainResult chainRes = ref chainResult[i];

			chainRes.Target = -1;
			chainRes.Weight = 0.0f;

			if (!IsValidChain(i))
				continue;

			MStudioIKChain chain = studioHdr.IKChain(i);
			int bone = chain.Link(2).Bone;

			if ((studioHdr.BoneFlags(bone) & boneMask) == 0)
				continue;

			BuildBoneChain(pos, q, bone, boneToWorld, ref boneComputed);

			MathLib.MatrixAngles(boneToWorld[bone], out chainRes.Q, out chainRes.Pos);
		}

		for (j = 0; j < ikChainRule.Count; j++) {
			for (i = 0; i < ikChainRule[j].Count; i++) {
				ref IKContextIKRule rule = ref CollectionsMarshal.AsSpan(ikChainRule[j])[i];
				if ((uint)rule.Chain >= 32)
					continue;

				ref IKChainResult chainRes = ref chainResult[rule.Chain];
				chainRes.Target = -1;

				switch (rule.Type) {
					case Studio.IK_SELF: {
							MathLib.QuaternionMatrix(rule.Q, rule.Pos, out Matrix3x4 local);
							if (rule.Bone != -1) {
								BuildBoneChain(pos, q, rule.Bone, boneToWorld, ref boneComputed);
								MathLib.ConcatTransforms(boneToWorld[rule.Bone], local, out worldTarget);
							}
							else
								MathLib.ConcatTransforms(rootxform, local, out worldTarget);

							float weight = rule.Weight * rule.RuleWeight;
							chainRes.Weight = chainRes.Weight * (1 - weight) + weight;

							MathLib.MatrixAngles(worldTarget, out Quaternion q2, out Vector3 p2);

							chainRes.Pos = chainRes.Pos * (float)(1.0 - weight) + p2 * weight;
							MathLib.QuaternionSlerp(chainRes.Q, q2, weight, out Quaternion blendedQ);
							chainRes.Q = blendedQ;
						}
						break;
					case Studio.IK_WORLD:
						Assert(false);
						break;

					case Studio.IK_ATTACHMENT:
						break;

					case Studio.IK_GROUND:
						break;

					case Studio.IK_RELEASE: {
							float weight = rule.Weight * rule.RuleWeight;
							MStudioIKChain chain = studioHdr.IKChain(rule.Chain);
							int bone = chain.Link(2).Bone;

							BuildBoneChain(pos, q, bone, boneToWorld, ref boneComputed);
							MathLib.MatrixAngles(boneToWorld[bone], out Quaternion q2, out Vector3 p2);

							chainRes.Pos = chainRes.Pos * (float)(1.0 - weight) + p2 * weight;
							MathLib.QuaternionSlerp(chainRes.Q, q2, weight, out Quaternion blendedQ);
							chainRes.Q = blendedQ;
						}
						break;
					case Studio.IK_UNLATCH:
						break;
				}
			}
		}

		for (i = 0; i < Target.Count; i++) {
			IKTarget target = Target[i];

			if ((uint)target.Chain >= 32)
				continue;

			if (Target[i].Est.Weight > 0.0) {
				ref IKChainResult chainRes = ref chainResult[target.Chain];

				MathLib.QuaternionAngles(target.Offset.Q, out RadianEuler offsetAngles);
				MathLib.AngleMatrix(offsetAngles, target.Offset.Pos, out Matrix3x4 local);

				MathLib.QuaternionAngles(target.Est.Q, out RadianEuler estAngles);
				MathLib.AngleMatrix(estAngles, target.Est.Pos, out Matrix3x4 worldFootpad);

				MathLib.ConcatTransforms(worldFootpad, local, out worldTarget);

				MathLib.MatrixAngles(worldTarget, out Quaternion q2, out Vector3 p2);

				chainRes.Weight = target.Est.Weight;
				chainRes.Pos = chainRes.Pos * (float)(1.0 - chainRes.Weight) + p2 * chainRes.Weight;
				MathLib.QuaternionSlerp(chainRes.Q, q2, chainRes.Weight, out Quaternion blendedQ);
				chainRes.Q = blendedQ;
			}

			if (target.Latched.NeedsLatch) {
				target.Latched.HasLatch = true;
				target.Latched.Q = target.Est.Q;
				target.Latched.Pos = target.Est.Pos;
			}
		}

		for (i = 0; i < studioHdr.GetNumIKChains(); i++) {
			if (i >= 32)
				continue;

			ref IKChainResult chainRes = ref chainResult[i];
			MStudioIKChain chain = studioHdr.IKChain(i);

			if (chainRes.Weight > 0.0) {
				if (BoneSetup.Studio_SolveIK(chain, chainRes.Pos, boneToWorld)) {
					MathLib.MatrixGetColumn(boneToWorld[chain.Link(2).Bone], 3, out Vector3 p3);
					MathLib.QuaternionMatrix(chainRes.Q, p3, out boneToWorld[chain.Link(2).Bone]);

					BoneSetup.SolveBone(studioHdr, chain.Link(2).Bone, boneToWorld, pos, q);
					BoneSetup.SolveBone(studioHdr, chain.Link(1).Bone, boneToWorld, pos, q);
					BoneSetup.SolveBone(studioHdr, chain.Link(0).Bone, boneToWorld, pos, q);
				}
				else {
					if (chainRes.Target != -1) {
						IKTarget target = Target[chainRes.Target];
						target.Latched.DeltaPos = target.Latched.DeltaPos * 0.8f;
						MathLib.QuaternionScale(target.Latched.DeltaQ, 0.8f, out Quaternion deltaQ);
						target.Latched.DeltaQ = deltaQ;
					}
				}
			}
		}
	}

	public void SolveAutoplayLocks(Span<Vector3> pos, Span<Quaternion> q) {
		Matrix3x4[] boneToWorld = MatrixPool.Alloc();
		BoneBitList boneComputed = new();
		int i;

		for (i = 0; i < ikLock.Count; i++) {
			MStudioIKLock lck = studioHdr!.IKAutoplayLock(i);
			SolveLock(lck.Chain, lck.PosWeight, lck.LocalQWeight, i, pos, q, boneToWorld, ref boneComputed);
		}
		MatrixPool.Free(boneToWorld);
	}

	public void SolveSequenceLocks(MStudioSeqDesc seqdesc, Span<Vector3> pos, Span<Quaternion> q) {
		Matrix3x4[] boneToWorld = MatrixPool.Alloc();
		BoneBitList boneComputed = new();
		int i;

		for (i = 0; i < ikLock.Count; i++) {
			MStudioIKLock lck = seqdesc.IKLock(i);
			SolveLock(lck.Chain, lck.PosWeight, lck.LocalQWeight, i, pos, q, boneToWorld, ref boneComputed);
		}
		MatrixPool.Free(boneToWorld);
	}

	public void AddAllLocks(Span<Vector3> pos, Span<Quaternion> q) {
		if (studioHdr!.GetNumIKChains() == 0)
			return;

		Matrix3x4[] boneToWorld = MatrixPool.Alloc();
		BoneBitList boneComputed = new();

		int ikOffset = ikLock.Count;
		for (int i = 0; i < studioHdr.GetNumIKChains(); i++)
			ikLock.Add(default);

		for (int i = 0; i < studioHdr.GetNumIKChains(); i++) {
			if (!IsValidChain(i))
				continue;

			MStudioIKChain chain = studioHdr.IKChain(i);
			int bone = chain.Link(2).Bone;

			if ((studioHdr.BoneFlags(bone) & boneMask) == 0)
				continue;

			BuildBoneChain(pos, q, bone, boneToWorld, ref boneComputed);

			ref IKContextIKRule ikrule = ref CollectionsMarshal.AsSpan(ikLock)[i + ikOffset];

			ikrule.Chain = i;
			ikrule.Slot = i;
			ikrule.Type = Studio.IK_WORLD;

			MathLib.MatrixAngles(boneToWorld[bone], out ikrule.Q, out ikrule.Pos);

			if (chain.Link(0).KneeDir.LengthSquared() > 0.0) {
				MathLib.VectorRotate(chain.Link(0).KneeDir, boneToWorld[chain.Link(0).Bone], out ikrule.KneeDir);
				MathLib.MatrixPosition(boneToWorld[chain.Link(1).Bone], out ikrule.KneePos);
			}
			else
				ikrule.KneeDir = default;
		}
		MatrixPool.Free(boneToWorld);
	}

	public void SolveAllLocks(Span<Vector3> pos, Span<Quaternion> q) {
		Matrix3x4[] boneToWorld = MatrixPool.Alloc();
		BoneBitList boneComputed = new();
		int i;

		for (i = 0; i < ikLock.Count; i++)
			SolveLock(i, 1.0f, 0.0f, i, pos, q, boneToWorld, ref boneComputed);

		MatrixPool.Free(boneToWorld);
	}

	public void SolveLock(int lockChain, float lockPosWeight, float lockLocalQWeight, int i, Span<Vector3> pos, Span<Quaternion> q, Span<Matrix3x4> boneToWorld, ref BoneBitList boneComputed) {
		if (!IsValidChain(lockChain))
			return;

		MStudioIKChain chain = studioHdr!.IKChain(lockChain);
		int bone = chain.Link(2).Bone;

		if ((studioHdr.BoneFlags(bone) & boneMask) == 0)
			return;

		BuildBoneChain(pos, q, bone, boneToWorld, ref boneComputed);

		Vector3 p1, p3;
		Quaternion q2;

		MathLib.MatrixPosition(boneToWorld[bone], out p1);

		ref IKContextIKRule ikLockRule = ref CollectionsMarshal.AsSpan(ikLock)[i];

		p3 = p1 * (float)(1.0 - lockPosWeight) + ikLockRule.Pos * lockPosWeight;

		if (ikLockRule.KneeDir.LengthSquared() > 0)
			BoneSetup.Studio_SolveIK(chain.Link(0).Bone, chain.Link(1).Bone, chain.Link(2).Bone, p3, ikLockRule.KneePos, ikLockRule.KneeDir, boneToWorld);
		else
			BoneSetup.Studio_SolveIK(chain, p3, boneToWorld);

		MathLib.MatrixPosition(boneToWorld[bone], out p3);
		MathLib.QuaternionMatrix(ikLockRule.Q, p3, out boneToWorld[bone]);

		q2 = q[bone];
		BoneSetup.SolveBone(studioHdr, chain.Link(2).Bone, boneToWorld, pos, q);
		MathLib.QuaternionSlerp(q[bone], q2, lockLocalQWeight, out Quaternion slerped);
		q[bone] = slerped;

		BoneSetup.SolveBone(studioHdr, chain.Link(1).Bone, boneToWorld, pos, q);
		BoneSetup.SolveBone(studioHdr, chain.Link(0).Bone, boneToWorld, pos, q);
	}
}
