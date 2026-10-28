global using static Game.Client.ClientRagdollGlobals;
global using static Game.Client.RagdollConvars;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Game.Client;

public interface IRagdoll
{
	void RagdollBone(C_BaseEntity ent, Span<MStudioBone> bones, Span<bool> boneSimulated, BoneAccessor boneToWorld);
	ref readonly Vector3 GetRagdollOrigin();
	void GetRagdollBounds(out Vector3 mins, out Vector3 maxs);
	int RagdollBoneCount();
	IPhysicsObject? GetElement(int elementNum);
	void DrawWireframe();
	void VPhysicsUpdate(IPhysicsObject obj);
	bool TransformVectorToWorld(int boneIndex, in Vector3 temp, out Vector3 outVec);
}
public static class RagdollConvars
{
	public static readonly ConVar ragdoll_sleepaftertime = new("ragdoll_sleepaftertime", "5.0f", 0, "After this many seconds of being basically stationary, the ragdoll will go to sleep.");
}

/// <summary>
/// Analog of CRagdoll
/// </summary>
public class ClientRagdoll : IRagdoll
{
	public void Init(
		C_BaseEntity ent,
		StudioHdr studiohdr,
		in Vector3 forceVector,
		int forceBone,
		ReadOnlySpan<Matrix3x4> deltaBones0,
		ReadOnlySpan<Matrix3x4> deltaBones1,
		ReadOnlySpan<Matrix3x4> currentBonePosition,
		TimeUnit_t boneDt,
		bool fixedConstraints = false
	) {

	}

	public void RagdollBone(C_BaseEntity ent, Span<MStudioBone> bones, Span<bool> boneSimulated, BoneAccessor boneToWorld) {

	}
	public ref readonly Vector3 GetRagdollOrigin() {
		Ragdoll.List[0].Object!.GetPosition(out Origin, out _);
		return ref Origin;
	}
	public void GetRagdollBounds(out Vector3 mins, out Vector3 maxs) {
		mins = Mins;
		maxs = Maxs;
	}
	public int RagdollBoneCount() => Ragdoll.ListCount;
	public IPhysicsObject? GetElement(int elementNum) {
		return Ragdoll.List[elementNum].Object;
	}
	public void DrawWireframe() {
		IMaterial? wireframe = materials.FindMaterial("shadertest/wireframevertexcolor", MaterialDefines.TEXTURE_GROUP_OTHER);

		int i;
		Matrix3x4 matrix;
		for (i = 0; i < Ragdoll.ListCount; i++) {
			Color debugColor = new(0, 255, 255, 0);

			// draw the actual physics positions, not the cleaned up animation position
			Ragdoll.List[i].Object!.GetPositionMatrix(out matrix);
			PhysCollide collide = Ragdoll.List[i].Object!.GetCollide();
			engine.DebugDrawPhysCollide(collide, wireframe, matrix, debugColor);
		}
	}
	public void VPhysicsUpdate(IPhysicsObject obj) {
		if (LastUpdate == gpGlobals.CurTime)
			return;
		LastUpdate = gpGlobals.CurTime;
		AllAsleep = RagdollIsAsleep(Ragdoll);
		if (AllAsleep) {
			// NOTE: This is the bbox of the ragdoll's physics
			// It's not always correct to use for culling, but it sure beats 
			// using the radius box!
			Vector3 origin = GetRagdollOrigin();
			RagdollComputeExactBbox(Ragdoll, origin, ref Mins, ref Maxs);
			Mins -= origin;
			Maxs -= origin;
		}
		else {
			Mins.Init(-Radius, -Radius, -Radius);
			Maxs.Init(Radius, Radius, Radius);

			if (Ragdoll.Group!.IsInErrorState()) {
				C_BaseEntity? entity = (C_BaseEntity?)(Ragdoll.List[0].Object!.GetGameData());
				RagdollSolveSeparation(Ragdoll, entity);
			}
		}

		// See if we should go to sleep...
		CheckSettleStationaryRagdoll();
	}
	public bool TransformVectorToWorld(int boneIndex, in Vector3 temp, out Vector3 outVec) {
		int listIndex = -1;
		if (boneIndex >= 0 && boneIndex < Ragdoll.ListCount) {
			for (int i = 0; i < Ragdoll.ListCount; ++i) {
				if (Ragdoll.BoneIndex[i] == boneIndex)
					listIndex = i;
			}
			if (listIndex != -1) {
				Ragdoll.List[listIndex].Object.LocalToWorld(out outVec, in temp);
				return true;
			}
		}
		outVec = default;
		return false;
	}

	public void BuildRagdollBounds(C_BaseEntity ent) {
		Vector3 mins, maxs, size;
		modelinfo.GetModelBounds(ent.GetModel(), out mins, out maxs);
		size = (maxs - mins) * 0.5f;
		Radius = size.Length();

		Mins.Init(-Radius, -Radius, -Radius);
		Maxs.Init(Radius, Radius, Radius);
	}

	public IPhysicsConstraintGroup? GetConstraintGroup() => Ragdoll.Group;

	public bool IsValid() => Ragdoll.ListCount > 0;

	public Ragdoll GetRagdoll() => Ragdoll;

	public void ResetRagdollSleepAfterTime() => LastOriginChangeTime = gpGlobals.CurTime;
	public TimeUnit_t GetLastVPhysicsUpdateTime() => LastUpdate;

	void CheckSettleStationaryRagdoll() {
		Vector3 delta = GetRagdollOrigin() - LastOrigin;
		LastOrigin = GetRagdollOrigin();
		for (int i = 0; i < 3; ++i) {
			// It's still moving...
			if (MathF.Abs(delta[i]) > RAGDOLL_SLEEP_TOLERANCE) {
				LastOriginChangeTime = gpGlobals.CurTime;
				// Msg( "%d [%p] Still moving\n", gpGlobals->tickcount, this );
				return;
			}
		}

		// It's totally asleep, don't worry about forcing it to settle
		if (AllAsleep)
			return;

		// Msg( "%d [%p] Settling\n", gpGlobals->tickcount, this );

		// It has stopped moving, see if it
		TimeUnit_t dt = gpGlobals.CurTime - LastOriginChangeTime;
		if (dt < ragdoll_sleepaftertime.GetFloat())
			return;

		// Msg( "%d [%p] FORCE SLEEP\n",gpGlobals->tickcount, this );

		// Force it to go to sleep
		PhysForceRagdollToSleep();
	}
	void PhysForceRagdollToSleep() {
		for (int i = 0; i < Ragdoll.ListCount; i++) {
			if (Ragdoll.List[i].Object != null) {
				PhysForceClearVelocity(Ragdoll.List[i].Object!);
				Ragdoll.List[i].Object!.Sleep();
			}
		}
	}

	readonly Ragdoll Ragdoll = new();
	Vector3 Mins, Maxs;
	Vector3 Origin;
	float Radius;
	TimeUnit_t LastUpdate;
	bool AllAsleep;
	Vector3 LastOrigin;
	TimeUnit_t LastOriginChangeTime;
}

public static class ClientRagdollGlobals
{
	public const float RAGDOLL_SLEEP_TOLERANCE = 1.0f;
	public static ClientRagdoll CreateRagdoll(
		C_BaseEntity ent,
		StudioHdr studiohdr,
		in Vector3 forceVector,
		int forceBone,
		ReadOnlySpan<Matrix3x4> deltaBones0,
		ReadOnlySpan<Matrix3x4> deltaBones1,
		ReadOnlySpan<Matrix3x4> currentBonePosition,
		TimeUnit_t dt,
		bool fixedConstraints = false
	) {
		ClientRagdoll ragdoll = new();
		ragdoll.Init(ent, studiohdr, forceVector, forceBone, deltaBones0, deltaBones1, currentBonePosition, dt, fixedConstraints);

		if (!ragdoll.IsValid())
			Msg($"Bad ragdoll for {studiohdr.Name()}");
		return null;

		return ragdoll;
	}

	struct RagdollRemember
	{
		public C_BaseEntity Ragdoll;
		public long TickCount;
	}
	struct RagdollMemoryList()
	{
		public readonly List<RagdollRemember> List = [];
		public long TickCount = 0; // is this even used? wtf?
		public void Update() {
			if (TickCount > gpGlobals.TickCount) {
				List.Clear();
				return;
			}

			for (int i = List.Count - 1; i >= 0; --i)
				if (List[i].TickCount != gpGlobals.TickCount)
					List.RemoveAt(i);
		}

		public bool IsInList(C_BaseEntity ragdoll) {
			for (int i = List.Count() - 1; i >= 0; --i) {
				if (List[i].Ragdoll == ragdoll)
					return true;
			}

			return false;
		}

		public void AddToList(C_BaseEntity ragdoll) {
			Update();
			List.Add(new() {
				Ragdoll = ragdoll,
				TickCount = gpGlobals.TickCount
			});
		}
	}

	static RagdollMemoryList gRagdolls = new();

	public static void NoteRagdollCreationTick(C_BaseEntity ragdoll) {
		gRagdolls.AddToList(ragdoll);
	}

	public static bool WasRagdollCreatedOnCurrentTick(C_BaseEntity ragdoll) {
		gRagdolls.Update();
		return gRagdolls.IsInList(ragdoll);
	}
}
