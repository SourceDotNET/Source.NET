using Source.Common;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Source.Engine;

public struct EntityListAlongRay : IPartitionEnumerator
{
	public const int MAX_ENTITIES_ALONGRAY = 1024;
	[InlineArray(MAX_ENTITIES_ALONGRAY)]
	public struct InlineArrayMaxEntitiesAlongRay<T> { public T? first; }

	public int Length;
	public InlineArrayMaxEntitiesAlongRay<IHandleEntity?> EntityHandles;

	public void Reset() => Length = 0;
	public int Count() => Length;

	public IterationRetval EnumElement(IHandleEntity? handleEntity) {
		if (Length < MAX_ENTITIES_ALONGRAY)
			EntityHandles[Length++] = handleEntity;
		else
			DevMsg(1, "Max entity count along ray exceeded!\n");

		return IterationRetval.Continue;
	}
}

public ref struct EnumerationFilter<IEE> : IPartitionEnumerator where IEE : IEntityEnumerator, allows ref struct
{
	public IEE Enumerator;

	public EnumerationFilter(IEE enumerator) => Enumerator = enumerator;

	public IterationRetval EnumElement(IHandleEntity? handleEntity) {
		if (StaticPropMgr().IsStaticProp(handleEntity!))
			return IterationRetval.Continue;

		if (!Enumerator.EnumEntity(handleEntity))
			return IterationRetval.Stop;

		return IterationRetval.Continue;
	}
}

public abstract class EngineTrace : IEngineTrace
{
	public abstract ICollideable? GetWorldCollideable();
	public abstract void SetTraceEntity(ICollideable? collideable, ref Trace trace);

	protected Matrix3x4? RootMoveParent;

	public void ClipRayToCollideable(in Ray ray, Mask mask, ICollideable? collide, ref Trace trace) {
		CM.ClearTrace(ref trace);
		MathLib.VectorAdd(in ray.Start, in ray.StartOffset, out trace.StartPos);
		MathLib.VectorAdd(in trace.StartPos, in ray.Delta, out trace.EndPos);

		if (collide == null)
			return;

		Model? model = collide.GetCollisionModel();
		bool isStudioModel = false;
		StudioHeader? studioHdr = null;
		if (model != null && model.Type == ModelType.Studio) {
			isStudioModel = true;
			studioHdr = (StudioHeader?)modelloader.GetExtraData(model);
			// Cull if the collision mask isn't set + we're not testing hitboxes.
			if ((mask & (Mask)Contents.HitBox) == 0) {
				if (studioHdr == null || ((int)mask & studioHdr.Contents) == 0)
					return;
			}
		}

		Matrix3x4? oldRoot = RootMoveParent;
		if (((SolidFlags)collide.GetSolidFlags() & SolidFlags.RootParentAligned) != 0) {
			ref readonly Matrix3x4 rootParentToWorld = ref collide.GetRootParentToWorldTransform();
			RootMoveParent = Unsafe.IsNullRef(in rootParentToWorld) ? null : rootParentToWorld;
		}

		bool traced = false;
		bool customPerformed = false;
		if (ShouldPerformCustomRayTest(in ray, collide)) {
			ClipRayToCustom(in ray, mask, collide, ref trace);
			traced = true;
			customPerformed = true;
		}
		else {
			traced = ClipRayToVPhysics(in ray, mask, collide, studioHdr, ref trace);
		}

		// FIXME: Why aren't we using solid type to check what kind of collisions to test against?!?!
		if (!traced && model != null && model.Type == ModelType.Brush)
			traced = ClipRayToBSP(in ray, mask, collide, ref trace);

		if (!traced)
			traced = ClipRayToOBB(in ray, mask, collide, ref trace);

		// Hitboxes..
		bool tracedHitboxes = false;
		if (isStudioModel && (mask & (Mask)Contents.HitBox) != 0) {
			// Until hitboxes are no longer implemented as custom raytests,
			// don't bother to do the work twice
			if (!customPerformed) {
				traced = ClipRayToHitboxes(in ray, mask, collide, ref trace);
				if (traced) {
					// Hitboxes will set the surface properties
					tracedHitboxes = true;
				}
			}
		}

		if (!traced)
			ClipRayToBBox(in ray, mask, collide, ref trace);

		if (isStudioModel && studioHdr != null && !tracedHitboxes && trace.DidHit() && (!customPerformed || trace.Surface.SurfaceProps == 0)) {
			trace.Contents = (Contents)studioHdr.Contents;
			// use the default surface properties
			trace.Surface.Name = "**studio**";
			trace.Surface.Flags = 0;
			trace.Surface.SurfaceProps = (ushort)physprop.GetSurfaceIndex(studioHdr.SurfaceProp());
		}

		if (trace.EntHandle == null && trace.DidHit())
			SetTraceEntity(collide, ref trace);

		RootMoveParent = oldRoot;
	}

	//-----------------------------------------------------------------------------
	// Sets up bounding boxes for hitboxes
	//-----------------------------------------------------------------------------
	protected bool ClipRayToHitboxes(in Ray ray, Mask mask, ICollideable collideable, ref Trace trace) {
		Trace hitboxTrace = default;
		CM.ClearTrace(ref hitboxTrace);

		// Keep track of the contents of what was hit initially
		hitboxTrace.Contents = trace.Contents;
		MathLib.VectorAdd(ray.Start, ray.StartOffset, out hitboxTrace.StartPos);
		MathLib.VectorAdd(hitboxTrace.StartPos, ray.Delta, out hitboxTrace.EndPos);

		// At the moment, it has to be a true ray to work with hitboxes
		if (!ray.IsRay)
			return false;

		// If the hitboxes weren't even tested, then just use the original trace
		if (!collideable.TestHitboxes(ray, (Contents)mask, ref hitboxTrace))
			return false;

		// If they *were* tested and missed, clear the original trace
		if (!hitboxTrace.DidHit()) {
			CM.ClearTrace(ref trace);
			trace.StartPos = hitboxTrace.StartPos;
			trace.EndPos = hitboxTrace.EndPos;
		}
		else if (collideable.GetSolid() != SolidType.VPhysics) {
			// If we also hit the hitboxes, maintain fractionleftsolid +
			// startpos because those are reasonable enough values and the
			// hitbox code doesn't set those itself.
			Vector3 vecStartPos = trace.StartPos;
			float flFractionLeftSolid = trace.FractionLeftSolid;

			trace = hitboxTrace;

			if (hitboxTrace.StartSolid) {
				trace.StartPos = vecStartPos;
				trace.FractionLeftSolid = flFractionLeftSolid;
			}
		}
		else {
			// Fill out the trace hitbox details
			trace.Contents = hitboxTrace.Contents;
			trace.HitGroup = hitboxTrace.HitGroup;
			trace.HitBox = hitboxTrace.HitBox;
			trace.PhysicsBone = hitboxTrace.PhysicsBone;
			trace.Surface = hitboxTrace.Surface;
			Assert(trace.PhysicsBone >= 0);
			// Fill out the surfaceprop details from the hitbox. Use the physics bone instead of the hitbox bone
			Assert(trace.Surface.Flags == hitboxTrace.Surface.Flags);
		}

		return true;
	}

	public void ClipRayToEntity(in Ray ray, Mask mask, IHandleEntity ent, ref Trace trace) {
		ClipRayToCollideable(in ray, mask, GetCollideable(ent), ref trace);
	}

	protected virtual bool ShouldPerformCustomRayTest(in Ray ray, ICollideable collide) {
		return ((SolidFlags)collide.GetSolidFlags() & SolidFlags.CustomRayTest) != 0;
	}

	protected void ClipRayToCustom(in Ray ray, Mask mask, ICollideable collide, ref Trace trace) {
		collide.TestCollision(in ray, (Contents)mask, ref trace);
	}

	//-----------------------------------------------------------------------------
	// Perform vphysics trace
	//-----------------------------------------------------------------------------
	protected bool ClipRayToVPhysics(in Ray ray, Mask mask, ICollideable collide, StudioHeader? studioHdr, ref Trace trace) {
		if (collide.GetSolid() != SolidType.VPhysics)
			return false;

		bool traced = false;

		// use the vphysics model for rotated brushes and vphysics simulated objects
		Model? model = collide.GetCollisionModel();

		if (model == null)
			return false;

		if (studioHdr != null) {
			StudioConvexInfo studioConvex = new(studioHdr);
			VCollide? pCollide = mdlcache.GetVCollide(model.Studio);
			if (pCollide != null && pCollide.SolidCount != 0) {
				physcollision.TraceBox(
					in ray,
					(Contents)mask,
					studioConvex,
					pCollide.Solids![0]!, // UNDONE: Support other solid indices?!?!?!? (forced zero)
					in collide.GetCollisionOrigin(),
					in collide.GetCollisionAngles(),
					out trace);
				traced = true;
			}
		}
		else {
			Assert(model.Type != ModelType.Studio);
			// use the regular code for raytraces against brushes
			// do ray traces with normal code, but use vphysics to do box traces
			if (!ray.IsRay || model.Type != ModelType.Brush) {
				int nModelIndex = collide.GetCollisionModelIndex();

				// BUGBUG: This only works when the vcollide in question is the first solid in the model
				VCollide? pCollide = CM.VCollideForModel(nModelIndex, model);

				if (pCollide != null && pCollide.SolidCount != 0) {
					BrushConvexInfo brushConvex = new();

					IConvexInfo? convexInfo = (model.Type) == ModelType.Brush ? brushConvex : null;
					physcollision.TraceBox(
						in ray,
						(Contents)mask,
						convexInfo,
						pCollide.Solids![0]!, // UNDONE: Support other solid indices?!?!?!? (forced zero)
						in collide.GetCollisionOrigin(),
						in collide.GetCollisionAngles(),
						out trace);
					traced = true;
				}
			}
		}

		return traced;
	}

	//-----------------------------------------------------------------------------
	// Perform bsp trace
	//-----------------------------------------------------------------------------
	protected bool ClipRayToBSP(in Ray ray, Mask mask, ICollideable collide, ref Trace trace) {
		int nModelIndex = collide.GetCollisionModelIndex();
		CollisionModel? cmodel = CM.InlineModelNumber(nModelIndex - 1);
		if (cmodel == null)
			return false;

		int nHeadNode = cmodel.HeadNode;
		CM.TransformedBoxTrace(in ray, nHeadNode, mask, in collide.GetCollisionOrigin(), in collide.GetCollisionAngles(), ref trace);
		return true;
	}

	protected bool ClipRayToOBB(in Ray ray, Mask mask, ICollideable collide, ref Trace trace) {
		if (collide.GetSolid() != SolidType.OBB)
			return false;

		IntersectRayWithOBB(in ray, in collide.GetCollisionOrigin(), in collide.GetCollisionAngles(), in collide.OBBMins(), in collide.OBBMaxs(), DIST_EPSILON, ref trace);
		return true;
	}

	// NOTE: Switched over to SIMD ray/box test since there is a bug we haven't hunted down yet in the scalar version
	protected bool ClipRayToBBox(in Ray ray, Mask mask, ICollideable collide, ref Trace trace) {
		if (collide.GetSolid() != SolidType.BBox)
			return false;

		// We can't use the OBBMins/Maxs unless the collision angles are world-aligned
		Assert(collide.GetCollisionAngles() == vec3_angle);

		Vector3 vecAbsMins, vecAbsMaxs;
		Vector3 vecInvDelta;
		// NOTE: If m_pRootMoveParent is set, then the boxes should be rotated into the root parent's space
		if (!ray.IsRay && RootMoveParent.HasValue) {
			Matrix3x4 rootMoveParent = RootMoveParent.Value;
			Ray ray_l = default;

			ray_l.Extents = ray.Extents;

			MathLib.VectorIRotate(ray.Delta, rootMoveParent, out ray_l.Delta);
			ray_l.StartOffset = default;
			MathLib.VectorITransform(ray.Start, rootMoveParent, out ray_l.Start);

			vecInvDelta = ray_l.InvDelta();
			MathLib.VectorITransform(collide.GetCollisionOrigin(), rootMoveParent, out Vector3 localEntityOrigin);
			ray_l.IsRay = ray.IsRay;
			ray_l.IsSwept = ray.IsSwept;

			MathLib.VectorAdd(localEntityOrigin, collide.OBBMins(), out vecAbsMins);
			MathLib.VectorAdd(localEntityOrigin, collide.OBBMaxs(), out vecAbsMaxs);
			CM.IntersectRayWithBox(ray_l, vecInvDelta, vecAbsMins, vecAbsMaxs, ref trace);

			if (trace.DidHit()) {
				Vector3 temp = trace.Plane.Normal;
				MathLib.VectorRotate(temp, rootMoveParent, out trace.Plane.Normal);
				MathLib.VectorAdd(ray.Start, ray.StartOffset, out trace.StartPos);

				if (trace.Fraction == 1)
					MathLib.VectorAdd(trace.StartPos, ray.Delta, out trace.EndPos);
				else
					MathLib.VectorMA(trace.StartPos, trace.Fraction, ray.Delta, out trace.EndPos);
				trace.Plane.Dist = Vector3.Dot(trace.EndPos, trace.Plane.Normal);
				if (trace.FractionLeftSolid < 1)
					trace.StartPos += ray.Delta * trace.FractionLeftSolid;
			}
			else
				MathLib.VectorAdd(ray.Start, ray.StartOffset, out trace.StartPos);

			return true;
		}

		vecInvDelta = ray.InvDelta();
		MathLib.VectorAdd(collide.GetCollisionOrigin(), collide.OBBMins(), out vecAbsMins);
		MathLib.VectorAdd(collide.GetCollisionOrigin(), collide.OBBMaxs(), out vecAbsMaxs);
		CM.IntersectRayWithBox(ray, vecInvDelta, vecAbsMins, vecAbsMaxs, ref trace);
		return true;
	}


	protected virtual void IntersectRayWithOBB(in Ray ray, in Vector3 origin, in QAngle angles, in Vector3 mins, in Vector3 maxs, float tolerance, ref Trace trace) {
		throw new NotImplementedException();
	}


	public void EnumerateEntities<IEE>(in Ray ray, bool triggers, scoped ref IEE enumerator) where IEE : IEntityEnumerator, allows ref struct {
		int mask = !triggers ? SpatialPartitionMask() : SpatialPartitionTriggerMask();

		if (mask != 0) {
			EnumerationFilter<IEE> filter = new(enumerator);
			SpatialPartition().EnumerateElementsAlongRay(mask, ray, false, ref filter);
			enumerator = filter.Enumerator;
		}
	}

	public void EnumerateEntities<IEE>(in Vector3 absMins, in Vector3 absMaxs, scoped ref IEE enumerator) where IEE : IEntityEnumerator, allows ref struct {
		EnumerationFilter<IEE> filter = new(enumerator);
		SpatialPartition().EnumerateElementsInBox(SpatialPartitionMask(), absMins, absMaxs, false, ref filter);
		enumerator = filter.Enumerator;
	}

	public void GetBrushesInAABB(in Vector3 mins, in Vector3 maxs, List<int> output, Contents contentsMask = (Contents)(-1)) {
		throw new NotImplementedException();
	}

	public bool GetBrushInfo(int iBrush, ref List<Vector4> planesOut, out Contents contents) {
		throw new NotImplementedException();
	}

	public IPhysCollide? GetCollidableFromDisplacementsInAABB(in Vector3 mins, in Vector3 maxs) {
		throw new NotImplementedException();
	}

	public abstract ICollideable? GetCollideable(IHandleEntity? entity);

	public int GetLeafContainingPoint(in Vector3 test) {
		throw new NotImplementedException();
	}

	public Contents GetPointContents(in Vector3 absPosition, out IHandleEntity? entity) {
		entity = null;
		return 0; // todo
	}

	public Contents GetPointContents_Collideable(ICollideable? collide, in Vector3 absPosition) {
		throw new NotImplementedException();
	}

	public int GetStatByIndex(int index, bool clear) {
		throw new NotImplementedException();
	}

	public void SetupLeafAndEntityListBox(in Vector3 boxMin, in Vector3 boxMax, TraceListData traceData) {
		throw new NotImplementedException();
	}

	public void SetupLeafAndEntityListRay(in Ray ray, TraceListData traceData) {
		throw new NotImplementedException();
	}

	public void SweepCollideable<Filter>(ICollideable? collide, in Vector3 absStart, in Vector3 absEnd, in QAngle angles, Mask mask, scoped ref Filter traceFilter, ref Trace trace) where Filter : struct, ITraceFilter {
		Matrix3x4? oldRoot = RootMoveParent;
		Ray ray = default;
		Assert(angles == vec3_angle);
		if (((SolidFlags)collide!.GetSolidFlags() & SolidFlags.RootParentAligned) != 0) {
			ref readonly Matrix3x4 rootParentToWorld = ref collide.GetRootParentToWorldTransform();
			RootMoveParent = Unsafe.IsNullRef(in rootParentToWorld) ? null : rootParentToWorld;
		}
		ray.Init(absStart, absEnd, collide.OBBMins(), collide.OBBMaxs());
		TraceRay(ray, mask, ref traceFilter, out trace);
		RootMoveParent = oldRoot;
	}

	protected abstract int SpatialPartitionMask();
	protected abstract int SpatialPartitionTriggerMask();

	public void TraceRay<Filter>(in Ray ray, Mask mask, scoped ref Filter traceFilter, out Trace trace) where Filter : struct, ITraceFilter {
		trace = default;
		CM.ClearTrace(ref trace);

		if (traceFilter.GetTraceType() != TraceType.EntitiesOnly) {
			ICollideable? collide = GetWorldCollideable();
			Assert(collide != null);

			// Make sure the world entity is unrotated
			// FIXME: BAH! The !pCollide test here is because of
			// CStaticProp::PrecacheLighting.. it's occurring too early
			// need to fix that later
			Assert(collide == null || collide.GetCollisionOrigin() == vec3_origin);
			Assert(collide == null || collide.GetCollisionAngles() == vec3_angle);

			CM.BoxTrace(in ray, 0, mask, true, ref trace);
			SetTraceEntity(collide, ref trace);

			// inside world, no need to check being inside anything else
			if (trace.StartSolid)
				return;

			if (traceFilter.GetTraceType() == TraceType.WorldOnly)
				return;
		}
		else {
			// Set initial start + endpos, necessary if the world isn't traced against 
			// because we may not trace against *anything* below.
			MathLib.VectorAdd(ray.Start, ray.StartOffset, out trace.StartPos);
			MathLib.VectorAdd(trace.StartPos, ray.Delta, out trace.EndPos);
		}

		// Save the world collision fraction.
		float flWorldFraction = trace.Fraction;
		float flWorldFractionLeftSolidScale = flWorldFraction;

		// Create a ray that extends only until we hit the world
		// and adjust the trace accordingly
		Ray entityRay = ray;

		if (trace.Fraction == 0) {
			entityRay.Delta.Init();
			flWorldFractionLeftSolidScale = trace.FractionLeftSolid;
			trace.FractionLeftSolid = 1.0f;
			trace.Fraction = 1.0f;
		}
		else {
			// Explicitly compute end so that this computation happens at the quantization of
			// the output (endpos).  That way we won't miss any intersections we would get
			// by feeding these results back in to the tracer
			// This is not the same as entityRay.m_Delta *= pTrace->fraction which happens 
			// at a quantization that is more precise as m_Start moves away from the origin
			Vector3 end;
			MathLib.VectorMA(entityRay.Start, trace.Fraction, entityRay.Delta, out end);
			MathLib.VectorSubtract(end, entityRay.Start, out entityRay.Delta);
			// We know this is safe because pTrace->fraction != 0
			trace.FractionLeftSolid /= trace.Fraction;
			trace.Fraction = 1.0f;
		}

		// Collide with entities along the ray
		// FIXME: Hitbox code causes this to be re-entrant for the IK stuff.
		// If we could eliminate that, this could be static and therefore
		// not have to reallocate memory all the time
		EntityListAlongRay enumerator = default;
		enumerator.Reset();
		SpatialPartition().EnumerateElementsAlongRay(SpatialPartitionMask(), entityRay, false, ref enumerator);

		bool noStaticProps = traceFilter.GetTraceType() == TraceType.EntitiesOnly;
		bool filterStaticProps = traceFilter.GetTraceType() == TraceType.EverythingFilterProps;

		Trace tr = default;
		ICollideable? collideable;
		ReadOnlySpan<char> debugName;
		int nCount = enumerator.Count();
		for (int i = 0; i < nCount; ++i) {
			// Generate a collideable
			IHandleEntity? handleEntity = enumerator.EntityHandles[i];
			HandleEntityToCollideable(handleEntity, out collideable, out debugName);

			if (!StaticPropMgr().IsStaticProp(handleEntity)) {
				if (!traceFilter.ShouldHitEntity(handleEntity!, (Contents)mask))
					continue;
			}
			else {
				// FIXME: Could remove this check here by
				// using a different spatial partition mask. Look into it
				// if we want more speedups here.
				if (noStaticProps)
					continue;

				if (filterStaticProps) {
					if (!traceFilter.ShouldHitEntity(handleEntity!, (Contents)mask))
						continue;
				}
			}

			ClipRayToCollideable(entityRay, mask, collideable, ref tr);

			// Make sure the ray is always shorter than it currently is
			ClipTraceToTrace(ref tr, ref trace);

			// Stop if we're in allsolid
			if (trace.AllSolid)
				break;
		}

		// Fix up the fractions so they are appropriate given the original
		// unclipped-to-world ray
		trace.Fraction *= flWorldFraction;
		trace.FractionLeftSolid *= flWorldFractionLeftSolidScale;
	}

	public void HandleEntityToCollideable(IHandleEntity? handleEntity, out ICollideable? collide, out ReadOnlySpan<char> debugName) {
		collide = StaticPropMgr().GetStaticProp(handleEntity);
		if (collide != null) {
			debugName = "static prop";
			return;
		}

		IServerUnknown? serverUnknown = (IServerUnknown?)handleEntity;
		if (serverUnknown == null || serverUnknown.GetNetworkable() == null) {
			collide = null;
			debugName = "<null>";
			return;
		}

		collide = serverUnknown.GetCollideable();
		debugName = serverUnknown.GetNetworkable()!.GetClassName();
	}

	public void TraceRayAgainstLeafAndEntityList<Filter>(in Ray ray, TraceListData traceData, Mask mask, scoped ref Filter traceFilter, ref Trace trace) where Filter : struct, ITraceFilter {
		throw new NotImplementedException();
	}

	public bool ClipTraceToTrace(ref Trace clipTrace, ref Trace finalTrace) {
		if (clipTrace.AllSolid || clipTrace.StartSolid || (clipTrace.Fraction < finalTrace.Fraction)) {
			if (finalTrace.StartSolid) {
				float flFractionLeftSolid = finalTrace.FractionLeftSolid;
				Vector3 vecStartPos = finalTrace.StartPos;

				finalTrace = clipTrace;
				finalTrace.StartSolid = true;

				if (flFractionLeftSolid > clipTrace.FractionLeftSolid) {
					finalTrace.FractionLeftSolid = flFractionLeftSolid;
					finalTrace.StartPos = vecStartPos;
				}
			}
			else
				finalTrace = clipTrace;
			return true;
		}

		if (clipTrace.StartSolid) {
			finalTrace.StartSolid = true;

			if (clipTrace.FractionLeftSolid > finalTrace.FractionLeftSolid) {
				finalTrace.FractionLeftSolid = clipTrace.FractionLeftSolid;
				finalTrace.StartPos = clipTrace.StartPos;
			}
		}

		return false;
	}
}


//-----------------------------------------------------------------------------
// Convex info for studio + brush models
//-----------------------------------------------------------------------------
class BrushConvexInfo : IConvexInfo
{
	public BrushConvexInfo() {
		BSPData = GetCollisionBSPData();
	}

	public uint GetContents(int convexGameData) {
		return (uint)BSPData.MapBrushes[convexGameData].Contents;
	}

	readonly CollisionBSPData BSPData;
}

class StudioConvexInfo : IConvexInfo
{
	public StudioConvexInfo(StudioHeader studioHdr) {
		StudioHdr = studioHdr;
	}

	public uint GetContents(int convexGameData) {
		if (convexGameData == 0)
			return (uint)StudioHdr.Contents;

		Assert(convexGameData <= StudioHdr.NumBones);
		MStudioBone bone = StudioHdr.Bone(convexGameData - 1);
		return (uint)bone.Contents;
	}

	readonly StudioHeader StudioHdr;
}

public class EngineTraceClient : EngineTrace
{
	protected override int SpatialPartitionMask() => (int)PartitionListMask.ClientSolidEdicts;
	protected override int SpatialPartitionTriggerMask() => 0;
	public override ICollideable? GetWorldCollideable() {
		IClientEntity? pUnk = entitylist.GetClientEntity(0);
		return pUnk?.GetCollideable();
	}

	public override ICollideable? GetCollideable(IHandleEntity? entity) {
		Assert(entity != null);

		ICollideable? prop = StaticPropMgr().GetStaticProp(entity);
		if (prop != null)
			return prop;

		IClientUnknown? unk = entitylist.GetClientUnknownFromHandle(entity!.GetRefEHandle());
		return unk!.GetCollideable();
	}

	public override void SetTraceEntity(ICollideable? collideable, ref Trace trace) {
		if (!trace.DidHit())
			return;

		// FIXME: This is only necessary because of traces occurring during
		// LevelInit (a suspect time to be tracing)
		if (collideable == null) {
			trace.EntHandle = null;
			return;
		}

		IClientUnknown? unk = (IClientUnknown?)collideable.GetEntityHandle();
		if (!StaticPropMgr().IsStaticProp(unk))
			trace.EntHandle = unk?.GetIClientEntity();
		else {
			trace.EntHandle = entitylist.GetClientEntity(0);
			trace.HitBox = StaticPropMgr().GetStaticPropIndex(unk) + 1;
		}
	}
}

public class EngineTraceServer : EngineTrace
{
	protected override int SpatialPartitionMask() => (int)PartitionListMask.EngineSolidEdicts;
	protected override int SpatialPartitionTriggerMask() => (int)PartitionListMask.EngineTriggerEdicts;

	public override ICollideable? GetWorldCollideable() {
		if (sv.Edicts == null)
			return null;
		return sv.Edicts[0].GetCollideable();
	}

	public override ICollideable? GetCollideable(IHandleEntity? entity) {
		Assert(entity != null);

		ICollideable? prop = StaticPropMgr().GetStaticProp(entity);
		if (prop != null)
			return prop;

		IServerUnknown netUnknown = (IServerUnknown)entity!;
		return netUnknown.GetCollideable();
	}

	public override void SetTraceEntity(ICollideable? collideable, ref Trace trace) {
		if (!trace.DidHit())
			return;

		// FIXME: This is only necessary because of traces occurring during
		// LevelInit (a suspect time to be tracing)
		if (collideable == null) {
			trace.EntHandle = null;
			return;
		}

		IHandleEntity? handleEntity = collideable.GetEntityHandle();
		if (!StaticPropMgr().IsStaticProp(handleEntity))
			trace.EntHandle = handleEntity;
		else {
			trace.EntHandle = sv.Edicts![0].GetIServerEntity();
			trace.HitBox = StaticPropMgr().GetStaticPropIndex(handleEntity) + 1;
		}
	}
}
