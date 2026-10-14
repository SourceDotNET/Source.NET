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
