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

public interface IParticleSystemQuery
{
	void GetLightingAtPoint(in Vector3 origin, out Color tint);
	void TraceLine(in Vector3 absStart, in Vector3 absEnd, Mask mask, IHandleEntity? ignore, int collisionGroup, ref GameTrace trace);

	bool MovePointInsideControllingObject(ParticleCollection particles, object? obj, ref Vector3 pnt) => true;
	bool IsPointInControllingObjectHitBox(ParticleCollection particles, int controlPointNumber, Vector3 pos, bool bboxOnly = false) => true;
	int GetCollisionGroupFromName(ReadOnlySpan<char> collisionGroupName) => 0;

	void GetRandomPointsOnControllingObjectHitBox(ParticleCollection particles, int controlPointNumber, int numPtsOut, float bboxScale, int numTrysToGetAPointInsideTheModel,
		Span<Vector3> pntsOut, Vector3 directionBias, Span<Vector3> hitBoxRelativeCoordOut = default, Span<int> hitBoxIndexOut = default);

	int GetControllingObjectHitBoxInfo(ParticleCollection particles, int controlPointNumber, Span<ModelHitBoxInfo> hitBoxOutputBuffer) => 0;

	Vector3 GetLocalPlayerPos() => Vector3.Zero;
	void GetLocalPlayerEyeVectors(out Vector3 forward, out Vector3 right, out Vector3 up) {
		forward = Vector3.Zero;
		right = Vector3.Zero;
		up = Vector3.Zero;
	}

	float GetPixelVisibility(ref int queryHandle, in Vector3 origin, float scale);

	void SetUpLightingEnvironment(in Vector3 pos) { }
}

public sealed class DefaultParticleSystemQuery : IParticleSystemQuery
{
	public void GetLightingAtPoint(in Vector3 origin, out Color tint) => tint = new Color(255, 255, 255, 255);

	public void TraceLine(in Vector3 absStart, in Vector3 absEnd, Mask mask, IHandleEntity? ignore, int collisionGroup, ref GameTrace trace) => trace.Fraction = 1.0f;

	public void GetRandomPointsOnControllingObjectHitBox(ParticleCollection particles, int controlPointNumber, int numPtsOut, float bboxScale, int numTrysToGetAPointInsideTheModel,
		Span<Vector3> pntsOut, Vector3 directionBias, Span<Vector3> hitBoxRelativeCoordOut = default, Span<int> hitBoxIndexOut = default) {
		pntsOut[..numPtsOut].Clear();
	}

	public float GetPixelVisibility(ref int queryHandle, in Vector3 origin, float scale) => 0;
}
