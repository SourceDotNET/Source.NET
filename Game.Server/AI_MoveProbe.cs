global using static Game.Server.AI_MoveProbeGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;

using System.Numerics;

namespace Game.Server;

public static class AI_MoveProbeGlobals
{
	public static float MOVE_HEIGHT_EPSILON = 0.0625f;

	public static readonly ConVar ai_strong_optimizations_no_checkstand = new("ai_strong_optimizations_no_checkstand", "0");
}

public class AI_MoveProbe : AI_Component
{
	public AI_MoveProbe(AI_BaseNPC? outer) : base(outer) {
		IgnoreTransientEntities = false;
		TraceListData = null;
	}

	public bool ShouldBrushBeIgnored(BaseEntity entity) {
		if (entity.Classname == g_iszFuncBrushClassname) {
			FuncBrush funcBrush = (FuncBrush)entity;

			bool nameMatches = funcBrush.ExcludedClass == GetOuter()!.Classname;

			return funcBrush.InvertExclusion ? !nameMatches : nameMatches;
		}

		return false;
	}

	public void TraceHull(in Vector3 start, in Vector3 end, in Vector3 hullMin, in Vector3 hullMax, Mask mask, out Trace result) {
		TraceFilterNav traceFilter = new(GetOuter()!, IgnoreTransientEntities, GetOuter(), GetOuter()!.GetCollisionGroup());

		Ray ray = default;
		ray.Init(start, end, hullMin, hullMax);

		if (TraceListData == null || TraceListData.IsEmpty())
			enginetrace.TraceRay(ray, mask, ref traceFilter, out result);
		else {
			result = default;
			enginetrace.TraceRayAgainstLeafAndEntityList(ray, TraceListData, mask, ref traceFilter, ref result);
		}

		if (r_visualizetraces.GetBool())
			DebugOverlay.DebugDrawLine(result.StartPos, result.EndPos, 255, 255, 0, true, -1.0f);

		Assert(!result.AllSolid || result.StartSolid);
	}

	public void TraceHull(in Vector3 start, in Vector3 end, Mask mask, out Trace result) {
		TraceHull(start, end, GetOuter()!.WorldAlignMins(), GetOuter()!.WorldAlignMaxs(), mask, out result);
	}

	public bool FloorPoint(in Vector3 start, Mask collisionMask, float startZ, float endZ, out Vector3 result) {
		Vector3 mins = GetOuter()!.WorldAlignMins();
		Vector3 maxs = new(GetOuter()!.WorldAlignMaxs().X, GetOuter()!.WorldAlignMaxs().Y, mins.Z);

		Vector3 vecUp = new(start.X, start.Y, start.Z + startZ + MOVE_HEIGHT_EPSILON);
		Vector3 vecDown = new(start.X, start.Y, start.Z + endZ);

		TraceHull(vecUp, vecDown, mins, maxs, collisionMask, out Trace trace);

		bool startedInObject = false;

		if (trace.StartSolid) {
			if (trace.Ent != null &&
				 (trace.Ent.GetMoveType() == Source.MoveType.VPhysics || trace.Ent.IsNPC()) &&
				 (start - GetOuter()!.GetLocalOrigin()).Length() < 0.1) {
				startedInObject = true;
			}

			vecUp.Z = start.Z + MOVE_HEIGHT_EPSILON;
			TraceHull(vecUp, vecDown, mins, maxs, collisionMask, out trace);
		}

		if (trace.Fraction == 1 || trace.AllSolid || (startedInObject && trace.StartSolid)) {
			result = start;
			if (startedInObject)
				return true;
			return false;
		}

		result = trace.EndPos;
		return true;
	}

	public bool CheckStandPosition(in Vector3 start, Mask collisionMask) {
		if ((GetOuter()!.CapabilitiesGet() & Capability.SkipNavGroundCheck) != 0)
			return true;

		if (ai_strong_optimizations_no_checkstand.GetBool())
			return true;

		Vector3 contactMin, contactMax;

		Vector3 vecUp = new(start.X, start.Y, start.Z + 0.1f);
		Vector3 vecDown = new(start.X, start.Y, start.Z - StepHeight() * GetOuter()!.GetStepDownMultiplier());

		Vector3 hullMins = GetOuter()!.WorldAlignMins();
		Vector3 hullMaxs = GetOuter()!.WorldAlignMaxs();

		if (hullMaxs == vec3_origin && hullMins == hullMaxs) {
			hullMins = GetOuter()!.GetHullMins();
			hullMaxs = GetOuter()!.GetHullMaxs();
		}

		contactMin.X = hullMins.X * 0.75f + hullMaxs.X * 0.25f;
		contactMax.X = hullMins.X * 0.25f + hullMaxs.X * 0.75f;
		contactMin.Y = hullMins.Y * 0.75f + hullMaxs.Y * 0.25f;
		contactMax.Y = hullMins.Y * 0.25f + hullMaxs.Y * 0.75f;
		contactMin.Z = hullMins.Z;
		contactMax.Z = hullMins.Z;

		Trace trace1, trace2;

		if (!GetOuter()!.IsFlaggedEfficient()) {
			Vector3 hullBottomCenter = new(0, 0, hullMins.Z);

			TraceHull(vecUp, vecDown, contactMin, hullBottomCenter, collisionMask, out trace1);
			if (trace1.Fraction != 1.0f && CanStandOn(trace1.Ent)) {
				TraceHull(vecUp, vecDown, hullBottomCenter, contactMax, collisionMask, out trace2);
				if (trace2.Fraction != 1.0f && (trace1.Ent == trace2.Ent || CanStandOn(trace2.Ent)))
					return true;
			}

			Vector3 testMin = new(contactMin.X, 0, hullMins.Z);
			Vector3 testMax = new(0, contactMax.Y, hullMins.Z);

			TraceHull(vecUp, vecDown, testMin, testMax, collisionMask, out trace1);
			if (trace1.Fraction != 1.0f && CanStandOn(trace1.Ent)) {
				testMin = new(0, contactMin.Y, hullMins.Z);
				testMax = new(contactMax.X, 0, hullMins.Z);
				TraceHull(vecUp, vecDown, testMin, testMax, collisionMask, out trace2);
				if (trace2.Fraction != 1.0f && (trace1.Ent == trace2.Ent || CanStandOn(trace2.Ent)))
					return true;
			}
		}
		else {
			TraceHull(vecUp, vecDown, contactMin, contactMax, collisionMask, out trace1);
			if (trace1.Fraction != 1.0f && CanStandOn(trace1.Ent))
				return true;
		}

		return false;
	}

	public bool CanStandOn(BaseEntity? surface) => GetOuter()!.CanStandOn(surface);

	public float StepHeight() => GetOuter()!.StepHeight();

	public void ClearBlockingEntity() => LastBlockingEnt.Set(null);
	public BaseEntity? GetBlockingEntity() => LastBlockingEnt.Get();

	bool IgnoreTransientEntities;
	TraceListData? TraceListData;
	EHANDLE LastBlockingEnt = new();
}
