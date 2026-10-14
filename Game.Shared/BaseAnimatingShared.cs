#if CLIENT_DLL || GAME_DLL

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

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

#if CLIENT_DLL
		protected void UpdateIKContext(StudioHdr hdr) {
			if (!IsModelScaled()) {
				if (Ik == null && hdr.GetNumIKChains() > 0 && (EntClientFlags & EntClientFlags.DontUseIK) == 0)
					Ik = new IKContext();
			}
			else
				Ik = null;
		}

		protected void InitIK(StudioHdr hdr, TimeUnit_t currentTime, int boneMask) {
			Ik!.Init(hdr, GetIKAngles(), GetIKOrigin(), currentTime, (int)gpGlobals.FrameCount, boneMask);
		}
#endif

		protected IKContext? CreateAutoplayIK(StudioHdr hdr, TimeUnit_t currentTime, int boneMask) {
			if (hdr.GetNumIKAutoplayLocks() == 0)
				return null;

			IKContext autoIK = new();
			autoIK.Init(hdr, GetIKAngles(), GetIKOrigin(), currentTime, 0, boneMask);
			return autoIK;
		}

#if CLIENT_DLL
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

		protected void SolveIK(Span<Vector3> pos, Span<Quaternion> q, Span<Matrix3x4> boneToWorld, ref BoneBitList boneComputed, TimeUnit_t currentTime) {
			UpdateIKLocks(currentTime);

			Ik!.UpdateTargets(pos, q, boneToWorld, ref boneComputed);

			CalculateIKLocks(currentTime);
			Ik.SolveDependencies(pos, q, boneToWorld, ref boneComputed);
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
	}
}
#endif
