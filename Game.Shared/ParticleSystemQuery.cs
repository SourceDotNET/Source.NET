using Source;
using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Particles;

using System.Numerics;
using System.Runtime.CompilerServices;

#if CLIENT_DLL
using Game.Client;
#elif GAME_DLL
using Game.Server;
#endif

namespace Game.Shared;

public sealed class ParticleSystemQuery : IParticleSystemQuery
{
	public static readonly ParticleSystemQuery g_pParticleSystemQuery = new();

	static readonly object LightMutex = new();

	public void GetLightingAtPoint(in Vector3 origin, out Color tint) {
#if CLIENT_DLL
		if (engine.IsInGame()) {
			Vector3 totalColor;
			lock (LightMutex)
				totalColor = engine.GetLightForPoint(origin, true);

			tint = default;
			tint.SetColor((int)(totalColor.X * 255), (int)(totalColor.Y * 255), (int)(totalColor.Z * 255), 0);
			return;
		}
#endif
		tint = new(255, 255, 255, 255);
	}

	public void TraceLine(in Vector3 absStart, in Vector3 absEnd, Mask mask, IHandleEntity? ignore, int collisionGroup, ref GameTrace trace) {
		bool doTrace = false;
#if CLIENT_DLL
		doTrace = engine.IsInGame();
#endif
		if (doTrace) {
			Util.TraceLine(absStart, absEnd, mask, ignore, (CollisionGroup)collisionGroup, out Trace tempTrace);
			trace.StartPos = tempTrace.StartPos;
			trace.EndPos = tempTrace.EndPos;
			trace.Plane = tempTrace.Plane;
			trace.Fraction = tempTrace.Fraction;
			trace.Contents = tempTrace.Contents;
			trace.DispFlags = tempTrace.DispFlags;
			trace.AllSolid = tempTrace.AllSolid;
			trace.StartSolid = tempTrace.StartSolid;
		}
		else {
			trace.StartSolid = false;
			trace.Fraction = 1.0f;
		}
	}

	public bool MovePointInsideControllingObject(ParticleCollection particles, object? obj, ref Vector3 pnt) {
#if CLIENT_DLL
		if (obj is not C_BaseEntity entity)
			return true;

		Ray ray = default;
		ray.Init(pnt, pnt);
		Trace tr = default;
		enginetrace.ClipRayToEntity(in ray, Mask.All, entity, ref tr);
		return tr.StartSolid;
#else
		return true;
#endif
	}

#if CLIENT_DLL
	static C_BaseEntity? GetControlPointMoveParent(ParticleCollection particles, int controlPointNumber)
		=> particles.ControlPoints[controlPointNumber].Object is StrongBox<EHANDLE> handle ? handle.Value.Get() : null;

	static bool IsPointInsideEntity(in Vector3 point, C_BaseEntity entity) {
		Ray ray = default;
		ray.Init(point, point);
		Trace tr = default;
		enginetrace.ClipRayToEntity(in ray, Mask.All, entity, ref tr);
		return tr.StartSolid;
	}

	static bool IsZero(in Vector3 v, float tolerance)
		=> v.X > -tolerance && v.X < tolerance && v.Y > -tolerance && v.Y < tolerance && v.Z > -tolerance && v.Z < tolerance;
#endif

#if CLIENT_DLL
	static MStudioHitboxSet ResolveHitboxSet(StudioHdr studioHdr, C_BaseAnimating animating, string? hitboxSetName) {
		if (hitboxSetName != null) {
			for (int s = 0; s < studioHdr.NumHitboxSets(); s++) {
				MStudioHitboxSet candidate = studioHdr.HitboxSet(s);
				if (candidate.Name().Equals(hitboxSetName, StringComparison.OrdinalIgnoreCase))
					return candidate;
			}
		}
		return studioHdr.HitboxSet(animating.GetHitboxSet());
	}
#endif

	public void GetRandomPointsOnControllingObjectHitBox(ParticleCollection particles, int controlPointNumber, int numPtsOut, float bboxScale, int numTrysToGetAPointInsideTheModel,
		Span<Vector3> pntsOut, Vector3 directionBias, Span<Vector3> hitBoxRelativeCoordOut = default, Span<int> hitBoxIndexOut = default, string? hitboxSetName = null) {
		bool success = false;

#if CLIENT_DLL
		C_BaseEntity? moveParent = GetControlPointMoveParent(particles, controlPointNumber);
		if (moveParent != null) {
			float randMax = bboxScale;
			float randMin = 1.0f - bboxScale;
			particles.GetControlPointAtTime(controlPointNumber, particles.CurTime, out Vector3 basePos);

			int numIters = numTrysToGetAPointInsideTheModel;
			if (!IsZero(directionBias, 0.0001f))
				numIters = Math.Max(numIters, 5);

			C_BaseAnimating? animating = moveParent.GetBaseAnimating();
			Span<int> boneIndices = stackalloc int[Studio.MAXSTUDIOBONES];
			if (animating != null && animating.HitboxToWorldTransforms(out Span<Matrix3x4> bones, boneIndices)) {
				StudioHdr? studioHdr = animating.GetModelPtr();
				if (studioHdr != null) {
					MStudioHitboxSet set = ResolveHitboxSet(studioHdr, animating, hitboxSetName);
					success = true;
					float modelScale = animating.GetModelScale();

					for (int i = 0; i < numPtsOut; i++) {
						Vector3 worldPosition = Vector3.Zero;
						float u = 0, v = 0, w = 0;
						int hitbox = 0;
						int tryCnt = numIters;
						float bestPointGoodness = -1.0e20f;
						do {
							int tryHitbox = particles.RandomInt(0, set.NumHitboxes - 1);
							MStudioBBox box = set.Hitbox(tryHitbox);

							float tryU = particles.RandomFloat(randMin, randMax);
							float tryV = particles.RandomFloat(randMin, randMax);
							float tryW = particles.RandomFloat(randMin, randMax);

							Vector3 localPosition = new(
								MathLib.Lerp(tryU, box.BBMin.X * modelScale, box.BBMax.X * modelScale),
								MathLib.Lerp(tryV, box.BBMin.Y * modelScale, box.BBMax.Y * modelScale),
								MathLib.Lerp(tryW, box.BBMin.Z * modelScale, box.BBMax.Z * modelScale));

							MathLib.VectorTransform(in localPosition, in bones[boneIndices[box.Bone]], out Vector3 tryWorldPosition);

							float pointGoodness = particles.RandomFloat(0, 72) + Vector3.Dot(tryWorldPosition - basePos, directionBias);
							if (numTrysToGetAPointInsideTheModel != 0 && IsPointInsideEntity(tryWorldPosition, moveParent))
								pointGoodness += 1000.0f;

							if (pointGoodness > bestPointGoodness) {
								u = tryU;
								v = tryV;
								w = tryW;
								worldPosition = tryWorldPosition;
								hitbox = tryHitbox;
								bestPointGoodness = pointGoodness;
							}
						} while (tryCnt-- != 0);

						pntsOut[i] = worldPosition;
						if (!hitBoxRelativeCoordOut.IsEmpty)
							hitBoxRelativeCoordOut[i] = new(u, v, w);
						if (!hitBoxIndexOut.IsEmpty)
							hitBoxIndexOut[i] = hitbox;
					}
				}
			}

			if (moveParent.IsBrushModel()) {
				moveParent.GetRenderBounds(out Vector3 min, out Vector3 max);
				ref readonly Matrix3x4 orientation = ref moveParent.EntityToWorldTransform();

				for (int i = 0; i < numPtsOut; i++) {
					Vector3 worldPosition = Vector3.Zero;
					float u = 0, v = 0, w = 0;
					int tryCnt = numIters;
					float bestPointGoodness = -1.0e20f;
					do {
						float tryU = particles.RandomFloat(randMin, randMax);
						float tryV = particles.RandomFloat(randMin, randMax);
						float tryW = particles.RandomFloat(randMin, randMax);

						Vector3 localPosition = new(MathLib.Lerp(tryU, min.X, max.X), MathLib.Lerp(tryV, min.Y, max.Y), MathLib.Lerp(tryW, min.Z, max.Z));
						MathLib.VectorTransform(in localPosition, in orientation, out Vector3 tryWorldPosition);

						float pointGoodness = particles.RandomFloat(0, 72) + Vector3.Dot(tryWorldPosition - basePos, directionBias);
						if (numTrysToGetAPointInsideTheModel != 0 && IsPointInsideEntity(tryWorldPosition, moveParent))
							pointGoodness += 1000.0f;

						if (pointGoodness > bestPointGoodness) {
							u = tryU;
							v = tryV;
							w = tryW;
							worldPosition = tryWorldPosition;
							bestPointGoodness = pointGoodness;
						}
					} while (tryCnt-- != 0);

					pntsOut[i] = worldPosition;
					if (!hitBoxRelativeCoordOut.IsEmpty)
						hitBoxRelativeCoordOut[i] = new(u, v, w);
					if (!hitBoxIndexOut.IsEmpty)
						hitBoxIndexOut[i] = 0;
				}
			}
		}
#endif

		if (!success) {
			for (int i = 0; i < numPtsOut; i++) {
				pntsOut[i] = particles.ControlPoints[controlPointNumber].Position;
				if (!hitBoxIndexOut.IsEmpty)
					hitBoxIndexOut[i] = 0;
				if (!hitBoxRelativeCoordOut.IsEmpty)
					hitBoxRelativeCoordOut[i] = Vector3.Zero;
			}
		}
	}

	public int GetControllingObjectHitBoxInfo(ParticleCollection particles, int controlPointNumber, Span<ModelHitBoxInfo> hitBoxOutputBuffer, string? hitboxSetName = null) {
		int ret = 0;
#if CLIENT_DLL
		C_BaseEntity? moveParent = GetControlPointMoveParent(particles, controlPointNumber);
		if (moveParent != null) {
			C_BaseAnimating? animating = moveParent.GetBaseAnimating();
			Span<int> boneIndices = stackalloc int[Studio.MAXSTUDIOBONES];
			if (animating != null && animating.HitboxToWorldTransforms(out Span<Matrix3x4> bones, boneIndices)) {
				StudioHdr? studioHdr = animating.GetModelPtr();
				if (studioHdr != null) {
					MStudioHitboxSet set = ResolveHitboxSet(studioHdr, animating, hitboxSetName);
					ret = Math.Min(hitBoxOutputBuffer.Length, set.NumHitboxes);
					for (int i = 0; i < ret; i++) {
						MStudioBBox box = set.Hitbox(i);
						hitBoxOutputBuffer[i].BoxMins = box.BBMin;
						hitBoxOutputBuffer[i].BoxMaxes = box.BBMax;
						hitBoxOutputBuffer[i].Transform = bones[boneIndices[box.Bone]];
					}
				}
			}
			if (moveParent.IsBrushModel()) {
				moveParent.GetRenderBounds(out Vector3 min, out Vector3 max);
				hitBoxOutputBuffer[0].BoxMins = min;
				hitBoxOutputBuffer[0].BoxMaxes = max;
				hitBoxOutputBuffer[0].Transform = moveParent.EntityToWorldTransform();
				ret = 1;
			}
		}
#endif
		return ret;
	}

	static readonly (string Name, CollisionGroup Group)[] NameMap = [
		("NONE", CollisionGroup.None),
		("DEBRIS", CollisionGroup.Debris),
		("INTERACTIVE", CollisionGroup.Interactive),
		("NPC", CollisionGroup.NPC),
		("ACTOR", CollisionGroup.NPCActor),
		("PASSABLE", CollisionGroup.PassableDoor),
	];

	public int GetCollisionGroupFromName(ReadOnlySpan<char> collisionGroupName) {
		foreach ((string name, CollisionGroup group) in NameMap) {
			if (collisionGroupName.Equals(name, StringComparison.OrdinalIgnoreCase))
				return (int)group;
		}
		return (int)CollisionGroup.None;
	}

	public Vector3 GetLocalPlayerPos() {
#if CLIENT_DLL
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
#elif GAME_DLL
		BasePlayer? player = AI_UtilsGlobals.AI_GetSinglePlayer();
#else
		object? player = null;
#endif
#if CLIENT_DLL || GAME_DLL
		if (player == null)
			return Vector3.Zero;
		return player.WorldSpaceCenter();
#else
		return Vector3.Zero;
#endif
	}

	public void GetLocalPlayerEyeVectors(out Vector3 forward, out Vector3 right, out Vector3 up) {
#if CLIENT_DLL
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
#elif GAME_DLL
		BasePlayer? player = AI_UtilsGlobals.AI_GetSinglePlayer();
#endif
#if CLIENT_DLL || GAME_DLL
		if (player != null) {
			player.EyeVectors(out forward, out right, out up);
			return;
		}
#endif
		forward = Vector3.Zero;
		right = Vector3.Zero;
		up = Vector3.Zero;
	}

	public float GetPixelVisibility(ref int queryHandle, in Vector3 origin, float scale) => 0.0f;
}
