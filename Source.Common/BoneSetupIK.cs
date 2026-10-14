using Source.Common.Mathematics;

using System.Numerics;

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
