using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

namespace Source.Particles;

public struct ModelHitBoxInfo
{
	public Vector3 BoxMins;
	public Vector3 BoxMaxes;
	public Matrix3x4 Transform;
}

public sealed class ModelHitBoxesInfo
{
	public const int MAX_HITBOXES = 256;

	public float LastUpdateTime = -1;
	public float PrevLastUpdateTime;
	public int NumHitBoxes;
	public int NumPrevHitBoxes;
	public ModelHitBoxInfo[]? HitBoxes;
	public ModelHitBoxInfo[]? PrevBoxes;

	public bool CurAndPrevValid() => NumHitBoxes != 0 && NumPrevHitBoxes == NumHitBoxes;
}

public struct ParticleRayTraceResult
{
	public Vector3 SurfaceNormal;
	public float HitDistance;
	public int HitId;
}

public interface IParticleSystemQuery
{
	void GetLightingAtPoint(in Vector3 origin, out Color tint);
	void TraceLine(in Vector3 absStart, in Vector3 absEnd, Mask mask, IHandleEntity? ignore, int collisionGroup, ref GameTrace trace);

	bool MovePointInsideControllingObject(ParticleCollection particles, object? obj, ref Vector3 pnt) => true;
	bool IsPointInControllingObjectHitBox(ParticleCollection particles, int controlPointNumber, Vector3 pos, bool bboxOnly = false) => true;
	int GetCollisionGroupFromName(ReadOnlySpan<char> collisionGroupName) => 0;
	int GetRayTraceEnvironmentFromName(ReadOnlySpan<char> rtEnvName) => 0;

	void TraceAgainstRayTraceEnv(int envNumber, in Vector3 start, in Vector3 direction, float tMin, float tMax, out ParticleRayTraceResult result) {
		result = default;
		result.HitId = -1;
	}

	void GetRandomPointsOnControllingObjectHitBox(ParticleCollection particles, int controlPointNumber, int numPtsOut, float bboxScale, int numTrysToGetAPointInsideTheModel,
		Span<Vector3> pntsOut, Vector3 directionBias, Span<Vector3> hitBoxRelativeCoordOut = default, Span<int> hitBoxIndexOut = default, int desiredHitbox = -1, string? hitboxSetName = null) {
		Vector3 position = particles.ControlPoints[controlPointNumber].Position;
		for (int i = 0; i < numPtsOut; i++) {
			pntsOut[i] = position;
			if (!hitBoxRelativeCoordOut.IsEmpty)
				hitBoxRelativeCoordOut[i] = Vector3.Zero;
			if (!hitBoxIndexOut.IsEmpty)
				hitBoxIndexOut[i] = -1;
		}
	}

	void GetClosestControllingObjectHitBox(ParticleCollection particles, int controlPointNumber, int numPtsIn, float bboxScale, ReadOnlySpan<Vector3> pntsIn,
		Span<Vector3> hitBoxRelativeCoordOut = default, Span<int> hitBoxIndexOut = default, int desiredHitbox = -1, string? hitboxSetName = null) {
		for (int i = 0; i < numPtsIn; i++) {
			if (!hitBoxRelativeCoordOut.IsEmpty)
				hitBoxRelativeCoordOut[i] = Vector3.Zero;
			if (!hitBoxIndexOut.IsEmpty)
				hitBoxIndexOut[i] = 0;
		}
	}

	int GetControllingObjectHitBoxInfo(ParticleCollection particles, int controlPointNumber, Span<ModelHitBoxInfo> hitBoxOutputBuffer, string? hitboxSetName = null) => 0;

	Vector3 GetLocalPlayerPos() => Vector3.Zero;
	void GetLocalPlayerEyeVectors(out Vector3 forward, out Vector3 right, out Vector3 up) {
		forward = Vector3.Zero;
		right = Vector3.Zero;
		up = Vector3.Zero;
	}

	float GetPixelVisibility(ref int queryHandle, in Vector3 origin, float scale);

	void SetUpLightingEnvironment(in Vector3 pos) { }

	int GetActivityNumber(object? model, ReadOnlySpan<char> activityName) => -1;
	object? GetModel(ReadOnlySpan<char> mdlName) => null;
	void DrawModel(object? model, in Matrix3x4 drawMatrix, ParticleCollection particles, int particleNumber, int bodyPart, int subModel, int skin, int animationSequence, float animationRate, float r, float g, float b, float a) { }
}

public sealed class DefaultParticleSystemQuery : IParticleSystemQuery
{
	public void GetLightingAtPoint(in Vector3 origin, out Color tint) => tint = new Color(255, 255, 255, 255);

	public void TraceLine(in Vector3 absStart, in Vector3 absEnd, Mask mask, IHandleEntity? ignore, int collisionGroup, ref GameTrace trace) => trace.Fraction = 1.0f;

	public void GetRandomPointsOnControllingObjectHitBox(ParticleCollection particles, int controlPointNumber, int numPtsOut, float bboxScale, int numTrysToGetAPointInsideTheModel,
		Span<Vector3> pntsOut, Vector3 directionBias, Span<Vector3> hitBoxRelativeCoordOut = default, Span<int> hitBoxIndexOut = default, int desiredHitbox = -1, string? hitboxSetName = null) {
		pntsOut[..numPtsOut].Clear();
	}

	public float GetPixelVisibility(ref int queryHandle, in Vector3 origin, float scale) => 0;
}
