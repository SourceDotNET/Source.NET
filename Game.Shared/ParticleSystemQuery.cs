using Source;
using Source.Common;
using Source.Common.Engine;
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
		Span<Vector3> pntsOut, Vector3 directionBias, Span<Vector3> hitBoxRelativeCoordOut = default, Span<int> hitBoxIndexOut = default, int desiredHitbox = -1, string? hitboxSetName = null) {
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
					float modelScale = animating.GetModelScale();
					int lastHitbox = set.NumHitboxes - 1;
					int firstHitbox = 0;
					if (desiredHitbox >= 0) {
						firstHitbox = Math.Min(desiredHitbox, lastHitbox);
						lastHitbox = firstHitbox;
					}

					Vector3 worldPosition = Vector3.Zero;
					float u = 0, v = 0, w = 0;
					int hitbox = 0;
					for (int i = 0; i < numPtsOut; i++) {
						int tryCnt = numIters;
						float bestPointGoodness = -1.0e20f;
						do {
							int tryHitbox = particles.RandomInt(firstHitbox, lastHitbox);
							MStudioBBox box = set.Hitbox(tryHitbox);
							if ((uint)box.Bone >= Studio.MAXSTUDIOBONES || box.Bone >= studioHdr.NumBones())
								continue;

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
			return;
		}
#endif

		for (int i = 0; i < numPtsOut; i++) {
			pntsOut[i] = particles.ControlPoints[controlPointNumber].Position;
			if (!hitBoxIndexOut.IsEmpty)
				hitBoxIndexOut[i] = 0;
			if (!hitBoxRelativeCoordOut.IsEmpty)
				hitBoxRelativeCoordOut[i] = Vector3.Zero;
		}
	}

	public void GetClosestControllingObjectHitBox(ParticleCollection particles, int controlPointNumber, int numPtsIn, float bboxScale, ReadOnlySpan<Vector3> pntsIn,
		Span<Vector3> hitBoxRelativeCoordOut = default, Span<int> hitBoxIndexOut = default, int desiredHitbox = -1, string? hitboxSetName = null) {
#if CLIENT_DLL
		C_BaseEntity? moveParent = GetControlPointMoveParent(particles, controlPointNumber);
		if (moveParent != null) {
			float randMax = bboxScale;
			float randMin = 1.0f - bboxScale;

			C_BaseAnimating? animating = moveParent.GetBaseAnimating();
			Span<int> boneIndices = stackalloc int[Studio.MAXSTUDIOBONES];
			if (animating != null && animating.HitboxToWorldTransforms(out Span<Matrix3x4> bones, boneIndices)) {
				StudioHdr? studioHdr = animating.GetModelPtr();
				if (studioHdr != null) {
					MStudioHitboxSet set = ResolveHitboxSet(studioHdr, animating, hitboxSetName);
					int endHitbox = set.NumHitboxes - 1;
					int startHitbox = 0;
					if (desiredHitbox >= 0) {
						startHitbox = Math.Min(desiredHitbox, endHitbox);
						endHitbox = startHitbox;
					}

					float u = 0, v = 0, w = 0;
					int hitbox = 0;
					for (int i = 0; i < numPtsIn; i++) {
						float closest = float.MaxValue;
						for (int tryHitbox = startHitbox; tryHitbox < endHitbox; tryHitbox++) {
							MStudioBBox box = set.Hitbox(tryHitbox);
							float tryU = particles.RandomFloat(randMin, randMax);
							float tryV = particles.RandomFloat(randMin, randMax);
							float tryW = particles.RandomFloat(randMin, randMax);

							Vector3 center = (box.BBMin + box.BBMax) * 0.5f;
							MathLib.VectorTransform(in center, in bones[boneIndices[box.Bone]], out Vector3 worldCenter);
							Vector3 delta = worldCenter - pntsIn[i];
							float distance = MathF.Sqrt(delta.Y * delta.Y + delta.X * delta.X + delta.Z * delta.Z);
							if (distance < closest) {
								closest = distance;
								u = tryU;
								v = tryV;
								w = tryW;
								hitbox = tryHitbox;
							}
						}

						if (!hitBoxRelativeCoordOut.IsEmpty)
							hitBoxRelativeCoordOut[i] = new(u, v, w);
						if (!hitBoxIndexOut.IsEmpty)
							hitBoxIndexOut[i] = hitbox;
					}
					return;
				}
			}
		}
#endif

		for (int i = 0; i < numPtsIn; i++) {
			if (!hitBoxIndexOut.IsEmpty)
				hitBoxIndexOut[i] = 0;
			if (!hitBoxRelativeCoordOut.IsEmpty)
				hitBoxRelativeCoordOut[i] = Vector3.Zero;
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

	public void SetUpLightingEnvironment(in Vector3 pos) {
#if CLIENT_DLL
		if (!engine.IsInGame())
			return;

		lock (LightMutex)
			modelrender.SetupLighting(pos);
#endif
	}

	public object? GetModel(ReadOnlySpan<char> mdlName) {
#if CLIENT_DLL
		return engine.LoadModel($"models/{mdlName}");
#else
		return null;
#endif
	}

	public int GetActivityNumber(object? model, ReadOnlySpan<char> activityName) {
#if CLIENT_DLL
		if (model is not Model mdl)
			return -1;

		StudioHeader? studioHeader = modelinfo.GetStudiomodel(mdl);
		if (studioHeader == null)
			return -1;

		StudioHdr studioHdr = new();
		studioHdr.Init(studioHeader, mdlcache);
		Activity activity = Animation.LookupActivity(studioHdr, activityName);
		return Animation.SelectWeightedSequence(studioHdr, activity, -1);
#else
		return -1;
#endif
	}

	public void DrawModel(object? model, in Matrix3x4 drawMatrix, ParticleCollection particles, int particleNumber, int bodyPart, int subModel, int skin, int animationSequence, float animationRate, float r, float g, float b, float a) {
#if CLIENT_DLL
		if (model is not Model mdl)
			return;

		MDLHandle_t handle = modelinfo.GetCacheHandle(mdl);
		StudioHeader? studioHeader = mdlcache.GetStudioHdr(handle);

		ParticleModelDraw draw = new();
		draw.SetMDL(handle);

		if (studioHeader != null && bodyPart >= 0 && bodyPart < studioHeader.NumBodyParts) {
			MStudioBodyParts bodyParts = studioHeader.BodyPart(bodyPart);
			if (subModel < bodyParts.NumModels)
				draw.Body += (subModel - draw.Body / bodyParts.Base % bodyParts.NumModels) * bodyParts.Base;
		}

		draw.Time = particles.CurTime;
		draw.R = unchecked((byte)(int)(r * 255.0f));
		draw.G = unchecked((byte)(int)(g * 255.0f));
		draw.B = unchecked((byte)(int)(b * 255.0f));
		draw.A = unchecked((byte)(int)(a * 255.0f));
		draw.Skin = skin;
		draw.Sequence = animationSequence;
		draw.PlaybackRate = animationRate;

		if (studioHeader != null) {
			Span<Matrix3x4> boneToWorld = new Matrix3x4[studioHeader.NumBones];
			draw.SetUpBones(drawMatrix, studioHeader.NumBones, boneToWorld);
			draw.Draw(drawMatrix, boneToWorld, StudioRenderFlags.DrawNoShadows);
		}

		draw.UnreferenceMDL();
#endif
	}
}

#if CLIENT_DLL
struct ParticleModelDraw()
{
	public MDLHandle_t Handle = MDLHANDLE_INVALID;
	public byte R = 255, G = 255, B = 255, A = 255;
	public int Skin;
	public int Body;
	public int Sequence;
	public int Lod;
	public float PlaybackRate = 30.0f;
	public float Time;

	static readonly float[] FlexControls = new float[Studio.MAXSTUDIOFLEXCTRL * 4];

	public void SetMDL(MDLHandle_t handle) {
		UnreferenceMDL();
		Handle = handle;
		if (Handle == MDLHANDLE_INVALID)
			return;

		mdlcache.AddRef(Handle);
		StudioHeader? hdr = mdlcache.LockStudioHdr(Handle);
		if (hdr == null)
			return;

		for (int i = 0; i < hdr.NumFlexControllers; i++) {
			MStudioFlexController controller = hdr.FlexController((LocalFlexController)i);
			if (controller.LocalToGlobal == -1)
				controller.LocalToGlobal = i;
		}
	}

	public void UnreferenceMDL() {
		if (Handle == MDLHANDLE_INVALID)
			return;

		mdlcache.UnlockStudioHdr(Handle);
		mdlcache.Release(Handle);
		Handle = MDLHANDLE_INVALID;
	}

	public readonly void SetUpBones(in Matrix3x4 rootToWorld, int maxBoneCount, Span<Matrix3x4> boneToWorld) {
		StudioHdr studioHdr = new();
		studioHdr.Init(mdlcache.GetStudioHdr(Handle), mdlcache);

		Span<float> poseParameter = stackalloc float[Studio.MAXSTUDIOPOSEPARAM];
		int poseCount = studioHdr.GetNumPoseParameters();
		for (int i = 0; i < Studio.MAXSTUDIOPOSEPARAM; i++) {
			poseParameter[i] = 0.5f;
			if (i < poseCount) {
				MStudioPoseParamDesc pose = studioHdr.PoseParameter(i);
				if (pose.Start < 0.0f && pose.End > 0.0f)
					poseParameter[i] = -pose.Start / (pose.End - pose.Start);
			}
		}

		int frameCount = BoneSetup.Studio_MaxFrame(studioHdr, Sequence, poseParameter);
		if (frameCount == 0)
			frameCount = 1;
		float cycle = Time * PlaybackRate / frameCount;
		cycle -= (int)cycle;

		Span<Vector3> pos = stackalloc Vector3[Studio.MAXSTUDIOBONES];
		Span<Quaternion> q = stackalloc Quaternion[Studio.MAXSTUDIOBONES];

		BoneSetup boneSetup = new(studioHdr, Studio.BONE_USED_BY_ANYTHING_AT_LOD(Lod), poseParameter);
		boneSetup.InitPose(pos, q);
		boneSetup.AccumulatePose(pos, q, Sequence, cycle, 1.0f, Time, null);

		int numBones = Math.Min(maxBoneCount, studioHdr.NumBones());
		for (int i = 0; i < numBones; i++) {
			MathLib.QuaternionMatrix(q[i], pos[i], out Matrix3x4 boneMatrix);
			int parent = studioHdr.Bone(i).Parent;
			if (parent == -1)
				MathLib.ConcatTransforms(rootToWorld, boneMatrix, out boneToWorld[i]);
			else
				MathLib.ConcatTransforms(boneToWorld[parent], boneMatrix, out boneToWorld[i]);
		}
	}

	public readonly void Draw(in Matrix3x4 rootToWorld, Span<Matrix3x4> boneToWorld, StudioRenderFlags flags) {
		if (Handle == MDLHANDLE_INVALID)
			return;

		studiorender.SetColorModulation(new(R / 255.0f, G / 255.0f, B / 255.0f));
		studiorender.SetAlphaModulation(A / 255.0f);

		DrawModelInfo info = default;
		info.StudioHdr = mdlcache.GetStudioHdr(Handle)!;
		info.HardwareData = mdlcache.GetHardwareData(Handle)!;
		info.Skin = Skin;
		info.Body = Body;
		info.HitboxSet = 0;
		info.Lod = Lod;

		Vector3 modelOrigin = new(rootToWorld.M03, rootToWorld.M13, rootToWorld.M23);
		studiorender.SetEyeViewTarget(info.StudioHdr, info.Body, in modelOrigin);

		Span<float> flexWeights = default;
		if (info.StudioHdr.NumFlexDesc != 0) {
			StudioHdr studioHdr = new();
			studioHdr.Init(info.StudioHdr, mdlcache);
			studiorender.LockFlexWeights(info.StudioHdr.NumFlexDesc, out flexWeights);
			studioHdr.RunFlexRules(FlexControls, flexWeights);
			studiorender.UnlockFlexWeights();
		}

		studiorender.DrawModel(ref Unsafe.NullRef<DrawModelResults>(), ref info, boneToWorld, flexWeights, default, in modelOrigin, flags);
	}
}
#endif
