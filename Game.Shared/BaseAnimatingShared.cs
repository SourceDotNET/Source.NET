#if CLIENT_DLL || GAME_DLL

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

#if CLIENT_DLL
namespace Game.Client
#else
namespace Game.Server
#endif
{
	public partial class
#if CLIENT_DLL
		C_BaseAnimating
#else
		BaseAnimating
#endif
	{
		public IKContext? Ik;

#if !CLIENT_DLL
		int IKCounter;
#endif

		public void SetModelScale(float scale, float changeDuration = 0.0f) {
			if (changeDuration > 0.0f) {
				ref ModelScaleData mvs = ref CreateDataObject<ModelScaleData>(DataObjectType.ModelScale);
				mvs.ModelScaleStart = ModelScale;
				mvs.ModelScaleGoal = scale;
				mvs.ModelScaleStartTime = (float)gpGlobals.CurTime;
				mvs.ModelScaleFinishTime = mvs.ModelScaleStartTime + changeDuration;
#if !CLIENT_DLL
				SetContextThink(UpdateModelScale, gpGlobals.CurTime, "UpdateModelScaleThink");
#endif
			}
			else {
				ModelScale = scale;
				RefreshCollisionBounds();

				if (HasDataObjectType(DataObjectType.ModelScale))
					DestroyDataObject(DataObjectType.ModelScale);
			}
		}

#if CLIENT_DLL
		public virtual void UpdateModelScale() {
#else
		public void UpdateModelScale() {
#endif
			ref ModelScaleData mvs = ref GetDataObject<ModelScaleData>(DataObjectType.ModelScale);
			if (Unsafe.IsNullRef(ref mvs))
				return;

			float dt = mvs.ModelScaleFinishTime - mvs.ModelScaleStartTime;
			Assert(dt > 0.0f);

			float frac = ((float)gpGlobals.CurTime - mvs.ModelScaleStartTime) / dt;
			frac = Math.Clamp(frac, 0.0f, 1.0f);

			if (gpGlobals.CurTime >= mvs.ModelScaleFinishTime) {
				ModelScale = mvs.ModelScaleGoal;
				DestroyDataObject(DataObjectType.ModelScale);
			}
			else
				ModelScale = MathLib.Lerp(frac, mvs.ModelScaleStart, mvs.ModelScaleGoal);

			RefreshCollisionBounds();
#if !CLIENT_DLL
			if (frac < 1.0f)
				SetContextThink(UpdateModelScale, gpGlobals.CurTime, "UpdateModelScaleThink");
#endif
		}

		public void RefreshCollisionBounds() => CollisionProp().RefreshScaledCollisionBounds();

		Vector3 GetIKOrigin() {
#if CLIENT_DLL
			return GetRenderOrigin();
#else
			return GetAbsOrigin();
#endif
		}

		QAngle GetIKAngles() {
#if CLIENT_DLL
			return GetRenderAngles();
#else
			return GetAbsAngles();
#endif
		}

		int NextIKFrameCounter() {
#if CLIENT_DLL
			return (int)gpGlobals.FrameCount;
#else
			return ++IKCounter;
#endif
		}

#if CLIENT_DLL
		protected void UpdateIKContext(StudioHdr hdr) {
			if (!IsModelScaled()) {
				if (Ik == null && hdr.GetNumIKChains() > 0 && (EntClientFlags & EntClientFlags.DontUseIK) == 0)
					Ik = new IKContext();
			}
			else
				Ik = null;
		}
#else
		public void EnableServerIK() {
			if (Ik == null) {
				Ik = new IKContext();
				IKCounter = 0;
			}
		}

		public void DisableServerIK() {
			Ik = null;
		}
#endif

#if CLIENT_DLL
		protected void InitIK(StudioHdr hdr, TimeUnit_t currentTime, int boneMask) {
			Ik!.Init(hdr, GetIKAngles(), GetIKOrigin(), currentTime, NextIKFrameCounter(), boneMask);
		}
#endif

		protected IKContext? CreateAutoplayIK(StudioHdr hdr, TimeUnit_t currentTime, int boneMask) {
			if (hdr.GetNumIKAutoplayLocks() == 0)
				return null;

			IKContext autoIK = new();
			autoIK.Init(hdr, GetIKAngles(), GetIKOrigin(), currentTime, 0, boneMask);
			return autoIK;
		}

		struct TraceFilterSkipNPCsAndPlayers(IHandleEntity? passentity, CollisionGroup collisionGroup) : ITraceFilter
		{
			TraceFilterSimple Simple = new(passentity, collisionGroup);

			public bool ShouldHitEntity(IHandleEntity serverEntity, Contents contentsMask) {
				if (Simple.ShouldHitEntity(serverEntity, contentsMask)) {
					BaseEntity? entity = EntityFromEntityHandle(serverEntity);
					if (entity == null)
						return true;

					if (entity.IsNPC() || entity.IsPlayer())
						return false;

					return true;
				}
				return false;
			}
		}

		protected void ApplyFootClearance(StudioHdr hdr, Span<Vector3> pos, Span<Quaternion> q, Span<Matrix3x4> boneToWorld) {
			if (Ik == null)
				return;

			MathLib.AngleVectors(GetIKAngles(), out _, out _, out Vector3 up);

			for (int i = 0; i < Ik.Target.Count; i++) {
				IKTarget target = Ik.Target[i];
				if (target.Type != Studio.IK_GROUND || !target.IsActive() || (uint)target.Chain >= 32)
					continue;

				MathLib.QuaternionMatrix(target.Est.Q, out Matrix3x4 footpad);
				MathLib.VectorRotate(target.Offset.Pos, footpad, out Vector3 ankle);
				float ankleHeight = Vector3.Dot(ankle, up);
				if (ankleHeight <= 0)
					continue;

				Ik.FootClearanceHeight[target.Chain] = ankleHeight;
				Ik.FootClearanceRadius[target.Chain] = Math.Max(target.Est.Radius, 1);
				Ik.HasFootClearance[target.Chain] = true;
			}

			Ray ray = default;
			TraceFilterSkipNPCsAndPlayers traceFilter = new(this, GetCollisionGroup());

			int numChains = Math.Min(hdr.GetNumIKChains(), 32);
			for (int i = 0; i < numChains; i++) {
				if (!Ik.HasFootClearance[i])
					continue;

				MStudioIKChain chain = hdr.IKChain(i);
				if (chain.NumLinks < 3)
					continue;

				int foot = chain.Link(2).Bone;
				if (foot < 0 || foot >= hdr.NumBones())
					continue;

				float ankleHeight = Ik.FootClearanceHeight[i];
				float r = Ik.FootClearanceRadius[i];

				MathLib.MatrixPosition(boneToWorld[foot], out Vector3 footPos);

				ray.Init(footPos + up * FootClearanceMaxRaise, footPos - up * ankleHeight, new Vector3(-r, -r, 0), new Vector3(r, r, 1));
				enginetrace.TraceRay(ray, PhysicsSolidMaskForEntity(), ref traceFilter, out Trace trace);

				if (trace.StartSolid || trace.Fraction >= 1.0f)
					continue;

				float raise = Vector3.Dot(trace.EndPos, up) + ankleHeight - Vector3.Dot(footPos, up);
				if (raise <= FootClearanceTolerance)
					continue;

				raise = Math.Min(raise, FootClearanceMaxRaise);

				if (BoneSetup.Studio_SolveIK(chain, footPos + up * raise, boneToWorld)) {
					BoneSetup.SolveBone(hdr, chain.Link(2).Bone, boneToWorld, pos, q);
					BoneSetup.SolveBone(hdr, chain.Link(1).Bone, boneToWorld, pos, q);
					BoneSetup.SolveBone(hdr, chain.Link(0).Bone, boneToWorld, pos, q);
				}
			}
		}

		const float FootClearanceMaxRaise = 18.0f;
		const float FootClearanceTolerance = 0.1f;

#if CLIENT_DLL
		protected void SolveIK(Span<Vector3> pos, Span<Quaternion> q, Span<Matrix3x4> boneToWorld, ref BoneBitList boneComputed, TimeUnit_t currentTime) {
			UpdateIKLocks(currentTime);

			Ik!.UpdateTargets(pos, q, boneToWorld, ref boneComputed);

			CalculateIKLocks(currentTime);
			Ik.SolveDependencies(pos, q, boneToWorld, ref boneComputed);
			ApplyFootClearance(GetModelPtr()!, pos, q, boneToWorld);
		}

		public void UpdateIKLocks(TimeUnit_t currentTime) {
			if (Ik == null)
				return;

			int targetCount = Ik.Target.Count;
			if (targetCount == 0)
				return;

			for (int i = 0; i < targetCount; i++) {
				IKTarget target = Ik.Target[i];

				if (!target.IsActive())
					continue;

				if (target.GetOwner() != -1) {
					BaseEntity? owner = cl_entitylist.GetEnt(target.GetOwner());
					if (owner != null)
						target.UpdateOwner(owner.EntIndex(), owner.GetAbsOrigin(), owner.GetAbsAngles());
				}
			}
		}

		public void CalculateIKLocks(TimeUnit_t currentTime) {
			if (Ik == null)
				return;

			int targetCount = Ik.Target.Count;
			if (targetCount == 0)
				return;

			SpatialPartitionListMask_t curSuppressed = partition.GetSuppressedLists();
			partition.SuppressLists((SpatialPartitionListMask_t)PartitionListMask.AllClientEdicts, false);
			PushEnableAbsRecomputations(false);

			Ray ray = default;
			TraceFilterSkipNPCsAndPlayers traceFilter = new(this, GetCollisionGroup());

			MathLib.AngleVectors(GetIKAngles(), out _, out _, out Vector3 up);

			for (int i = 0; i < targetCount; i++) {
				Trace trace;
				IKTarget target = Ik.Target[i];

				if (!target.IsActive())
					continue;

				switch (target.Type) {
					case Studio.IK_GROUND: {
							Vector3 estGround;
							Vector3 p1, p2;

							estGround = target.Est.Pos - GetIKOrigin();
							estGround = estGround - (estGround * up) * up;
							estGround = GetAbsOrigin() + estGround + target.Est.Floor * up;

							MathLib.VectorMA(estGround, target.Est.Height, up, out p1);
							MathLib.VectorMA(estGround, -target.Est.Height, up, out p2);

							float r = Math.Max(target.Est.Radius, 1);

							ray.Init(p1, p2, new Vector3(-r, -r, 0), new Vector3(r, r, r * 2));
							enginetrace.TraceRay(ray, PhysicsSolidMaskForEntity(), ref traceFilter, out trace);

							if (trace.Ent != null && trace.Ent.GetMoveType() == Source.MoveType.Push)
								target.SetOwner(trace.Ent.EntIndex(), trace.Ent.GetAbsOrigin(), trace.Ent.GetAbsAngles());
							else
								target.ClearOwner();

							if (trace.StartSolid) {
								Vector3 tmp = estGround - target.Trace.Closest;
								tmp.NormalizeInPlace();
								ray.Init(estGround - tmp * target.Est.Height, estGround, new Vector3(-r, -r, 0), new Vector3(r, r, 1));

								enginetrace.TraceRay(ray, Mask.Solid, ref traceFilter, out trace);

								if (!trace.StartSolid) {
									p1 = trace.EndPos;
									MathLib.VectorMA(p1, -target.Est.Height, up, out p2);
									ray.Init(p1, p2, new Vector3(-r, -r, 0), new Vector3(r, r, 1));

									enginetrace.TraceRay(ray, Mask.Solid, ref traceFilter, out trace);
								}
							}

							if (!trace.StartSolid) {
								if (trace.DidHitWorld()) {
									const float limit = 0.832f;
									float dot = Vector3.Dot(trace.Plane.Normal, up);
									if (dot < limit) {
										Assert(dot >= 0);
										Vector3 diff = trace.Plane.Normal - up * dot;
										float d = MathF.Sqrt((1 - limit * limit) / Vector3.Dot(diff, diff));
										trace.Plane.Normal = up * limit + d * diff;
									}
									target.SetPosWithNormalOffset(trace.EndPos, trace.Plane.Normal);
									target.SetNormal(trace.Plane.Normal);
									target.SetOnWorld(true);
								}
								else if (trace.DidHitNonWorldEntity()) {
									target.SetPos(trace.EndPos);
									target.SetAngles(GetIKAngles());
								}
								else
									target.IKFailed();
							}
							else {
								if (!trace.DidHitWorld())
									target.IKFailed();
								else {
									target.SetPos(trace.EndPos);
									target.SetAngles(GetIKAngles());
									target.SetOnWorld(true);
								}
							}
						}
						break;

					case Studio.IK_ATTACHMENT: {
							BaseEntity? entity;
							float dist = target.Est.Radius;

							for (EntitySphereQuery sphere = new(target.Est.Pos, 64); (entity = sphere.GetCurrentEntity()) != null; sphere.NextEntity()) {
								BaseAnimating? anim = entity.GetBaseAnimating();
								if (anim == null)
									continue;

								int attachment = anim.LookupAttachment(target.Offset.AttachmentName);
								if (attachment <= 0)
									continue;

								anim.GetAttachment(attachment, out Vector3 origin, out QAngle angles);

								float d = (target.Est.Pos - origin).Length();

								if (d >= dist)
									continue;

								dist = d;
								target.SetPos(origin);
								target.SetAngles(angles);
							}

							if (dist >= target.Est.Radius)
								target.IKFailed();
						}
						break;
				}
			}

			PopEnableAbsRecomputations();
			partition.SuppressLists(curSuppressed, true);
		}
#endif

		public bool ComputeHitboxSurroundingBox(out Vector3 vecWorldMins, out Vector3 vecWorldMaxs) {
			vecWorldMins = default;
			vecWorldMaxs = default;

#if CLIENT_DLL
			if (IsDynamicModelLoading())
				return false;
#endif

			StudioHdr? studioHdr = GetModelPtr();
			if (studioHdr == null)
				return false;

			if (HitboxSet < 0 || HitboxSet >= studioHdr.NumHitboxSets()) {
				Warning($"[UH-OH!] Invalid HitboxSet {HitboxSet} of {studioHdr.NumHitboxSets()} on model {studioHdr.Name()}.\n");
				return false;
			}

			MStudioHitboxSet set = studioHdr.HitboxSet(HitboxSet);
			if (set.NumHitboxes == 0)
				return false;

#if CLIENT_DLL
			BoneCache cache = GetBoneCache(studioHdr);
#else
			BoneCache cache = GetBoneCache();
#endif
			if (cache.IsNull())
				return false;

			vecWorldMins = new(float.MaxValue);
			vecWorldMaxs = new(-float.MaxValue);

#if !CLIENT_DLL
			float modelScale = GetModelScale();
#endif
			for (int i = 0; i < set.NumHitboxes; i++) {
				MStudioBBox box = set.Hitbox(i);
				ref Matrix3x4 matrix = ref cache.GetCachedBone(box.Bone);
				if (Unsafe.IsNullRef(ref matrix))
					continue;

#if CLIENT_DLL
				MathLib.TransformAABB(in matrix, box.BBMin, box.BBMax, out Vector3 boxAbsMins, out Vector3 boxAbsMaxs);
#else
				MathLib.TransformAABB(in matrix, box.BBMin * modelScale, box.BBMax * modelScale, out Vector3 boxAbsMins, out Vector3 boxAbsMaxs);
#endif
				vecWorldMins = Vector3.Min(vecWorldMins, boxAbsMins);
				vecWorldMaxs = Vector3.Max(vecWorldMaxs, boxAbsMaxs);
			}

			return true;
		}
	}
}
#endif
