#if CLIENT_DLL || GAME_DLL
global using static Game.Shared.SharedRagdollGlobals;

using Source;
using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

namespace Game.Shared;

public struct RagdollElement
{
	public Vector3 OriginParentSpace;
	public IPhysicsObject? Object;
	public IPhysicsConstraint? Constraint;
	public int ParentIndex;
}

public struct RagdollAnimatedFriction
{
	public float FrictionTimeIn;
	public float FrictionTimeOut;
	public float FrictionTimeHold;
	public int MinAnimatedFriction;
	public int MaxAnimatedFriction;
}

/// <summary>
/// analog of ragdoll_t, NOT CRagdoll!!!
/// </summary>
public class Ragdoll
{
	public const int RAGDOLL_MAX_ELEMENTS = 32;
	public const int RAGDOLL_INDEX_BITS = 6;

	public int ListCount;
	public bool AllowStretch;
	public bool Unused;
	public IPhysicsConstraintGroup? Group;
	public InlineArray32<RagdollElement> List;
	public InlineArray32<int> BoneIndex;
	public RagdollAnimatedFriction AnimFriction;
}

public ref struct RagdollParams
{
	public object? GameData;
	public VCollide Collide;
	public StudioHdr StudioHdr;
	public int ModelIndex;
	public Vector3 ForcePosition;
	public Vector3 ForceVector;
	public int ForceBoneIndex;
	public ReadOnlySpan<Matrix3x4> CurrentBones;
	public float JointFrictionScale;
	public bool AllowStretch;
	public bool FixedConstraints;
}

class RagdollCollisionRules(IPhysicsCollisionSet set) : IVPhysicsKeyHandler
{
	readonly IPhysicsCollisionSet Set = set;
	bool SelfCollisions = true;

	public void ParseKeyValue<T>(ref T data, ReadOnlySpan<char> key, ReadOnlySpan<char> value) {
		if (strcmpi(key, "selfcollisions") == 0) {
			Assert(atoi(value) == 0);
			SelfCollisions = false;
		}
		else if (strcmpi(key, "collisionpair") == 0) {
			if (SelfCollisions) {
				nexttoken(out ReadOnlySpan<char> token, value, ',', out ReadOnlySpan<char> str);
				int index0 = atoi(token);
				nexttoken(out token, str, ',', out _);
				int index1 = atoi(token);

				Set.EnableCollisions(index0, index1);
			}
			else
				Assert(false);
		}
	}

	public void SetDefaults<T>(ref T data) { }
}

class RagdollAnimatedFrictionParser(Ragdoll ragdoll) : IVPhysicsKeyHandler
{
	readonly Ragdoll Ragdoll = ragdoll;

	public void ParseKeyValue<T>(ref T data, ReadOnlySpan<char> key, ReadOnlySpan<char> value) {
		if (strcmpi(key, "animfrictionmin") == 0)
			Ragdoll.AnimFriction.MinAnimatedFriction = atoi(value);
		else if (strcmpi(key, "animfrictionmax") == 0)
			Ragdoll.AnimFriction.MaxAnimatedFriction = atoi(value);
		else if (strcmpi(key, "animfrictiontimein") == 0)
			Ragdoll.AnimFriction.FrictionTimeIn = strtof(value, out _);
		else if (strcmpi(key, "animfrictiontimeout") == 0)
			Ragdoll.AnimFriction.FrictionTimeOut = strtof(value, out _);
		else if (strcmpi(key, "animfrictiontimehold") == 0)
			Ragdoll.AnimFriction.FrictionTimeHold = strtof(value, out _);
	}

	public void SetDefaults<T>(ref T data) { }
}

public static class SharedRagdollGlobals
{
	public const int RAGDOLL_MAX_ELEMENTS = 24;
	public const int RAGDOLL_INDEX_BITS = 5;// NOTE 1<<RAGDOLL_INDEX_BITS >= RAGDOLL_MAX_ELEMENTS
	public const float CORE_DISSOLVE_FADE_START = 0.2f;
	public const float CORE_DISSOLVE_MODEL_FADE_START = 0.1f;
	public const float CORE_DISSOLVE_MODEL_FADE_LENGTH = 0.05f;
	public const float CORE_DISSOLVE_FADEIN_LENGTH = 0.1f;

	static void ParseCustomBlock(IVPhysicsKeyParser parse, IVPhysicsKeyHandler handler) {
		object? custom = handler;
		parse.ParseCustom(ref custom, handler);
	}

	public static void RagdollSetupAnimatedFriction(IPhysicsEnvironment physEnv, Ragdoll ragdoll, int modelIndex) {
		VCollide? collide = modelinfo.GetVCollide(modelIndex);

		if (collide != null) {
			IVPhysicsKeyParser parse = physcollision.VPhysicsKeyParserCreate(collide.KeyValues);

			while (!parse.Finished()) {
				ReadOnlySpan<char> block = parse.GetCurrentBlockName();

				if (strcmpi(block, "animatedfriction") == 0)
					ParseCustomBlock(parse, new RagdollAnimatedFrictionParser(ragdoll));
				else
					parse.SkipBlock();
			}

			physcollision.VPhysicsKeyParserDestroy(parse);
		}
	}

	static void RagdollAddSolid(IPhysicsEnvironment physEnv, Ragdoll ragdoll, in RagdollParams parms, ref Solid solid) {
		if (solid.Index >= 0 && solid.Index < parms.Collide.SolidCount) {
			Assert(ragdoll.ListCount == solid.Index);
			ReadOnlySpan<char> solidName = ((ReadOnlySpan<char>)solid.Name).SliceNullTerminatedString();
			int boneIndex = BoneSetup.Studio_BoneIndexByName(parms.StudioHdr, solidName);
			ragdoll.BoneIndex[ragdoll.ListCount] = boneIndex;

			if (boneIndex >= 0) {
				if (parms.FixedConstraints)
					solid.Params.Mass = 1000.0f;

				solid.Params.RotInertiaLimit = 0.1f;
				solid.Params.GameData = parms.GameData;
				int surfaceData = (int)physprops.GetSurfaceIndex(((ReadOnlySpan<char>)solid.SurfaceProp).SliceNullTerminatedString());

				if (surfaceData < 0)
					surfaceData = (int)physprops.GetSurfaceIndex("default");

				solid.Params.Name = parms.StudioHdr.Name();
				ref RagdollElement element = ref ragdoll.List[ragdoll.ListCount];
				element.Object = physEnv.CreatePolyObject(parms.Collide.Solids![solid.Index]!, surfaceData, vec3_origin, vec3_angle, ref solid.Params);
				element.Object!.SetPositionMatrix(parms.CurrentBones[boneIndex], true);
				element.ParentIndex = -1;
				element.Object.SetGameIndex((ushort)ragdoll.ListCount);

				ragdoll.ListCount++;
			}
			else
				Msg($"CRagdollProp::CreateObjects:  Couldn't Lookup Bone {solidName}\n");
		}
	}

	static void RagdollAddConstraint(IPhysicsEnvironment physEnv, Ragdoll ragdoll, in RagdollParams parms, ref ConstraintRagdollParams constraint) {
		if (constraint.ChildIndex == constraint.ParentIndex) {
			DevMsg(1, $"Bogus constraint on ragdoll {parms.StudioHdr.Name()}\n");
			constraint.ChildIndex = -1;
			constraint.ParentIndex = -1;
		}
		if (constraint.ChildIndex >= 0 && constraint.ParentIndex >= 0) {
			Assert(constraint.ChildIndex < ragdoll.ListCount);

			ref RagdollElement childElement = ref ragdoll.List[constraint.ChildIndex];
			childElement.ParentIndex = constraint.ParentIndex;

			if (parms.JointFrictionScale > 0) {
				for (int k = 0; k < 3; k++)
					constraint.Axes[k].Torque *= parms.JointFrictionScale;
			}

			BoneSetup.Studio_CalcBoneToBoneTransform(parms.StudioHdr, ragdoll.BoneIndex[constraint.ChildIndex], ragdoll.BoneIndex[constraint.ParentIndex], out constraint.ConstraintToAttached);
			MathLib.MatrixGetColumn(constraint.ConstraintToAttached, 3, out childElement.OriginParentSpace);
			MathLib.SetIdentityMatrix(out constraint.ConstraintToReference);
			IPhysicsObject parentObject = ragdoll.List[constraint.ParentIndex].Object!;
			if (parms.FixedConstraints) {
				ConstraintFixedParams fixedParams = default;
				fixedParams.Defaults();
				fixedParams.InitWithCurrentObjectState(childElement.Object!, parentObject);
				fixedParams.Constraint.Defaults();
				childElement.Constraint = physEnv.CreateFixedConstraint(childElement.Object!, parentObject, ragdoll.Group!, fixedParams);
			}
			else
				childElement.Constraint = physEnv.CreateRagdollConstraint(childElement.Object!, parentObject, ragdoll.Group!, constraint);
		}
	}

	static void RagdollCreateObjects(IPhysicsEnvironment physEnv, Ragdoll ragdoll, in RagdollParams parms) {
		ragdoll.ListCount = 0;
		ragdoll.Group = null;
		ragdoll.AllowStretch = parms.AllowStretch;
		ragdoll.List = default;
		ragdoll.AnimFriction = default;

		if (parms.Collide == null || parms.Collide.SolidCount > RAGDOLL_MAX_ELEMENTS)
			return;

		ConstraintGroupParams group = default;
		group.Defaults();
		ragdoll.Group = physEnv.CreateConstraintGroup(group);

		IVPhysicsKeyParser parse = physcollision.VPhysicsKeyParserCreate(parms.Collide.KeyValues);
		while (!parse.Finished()) {
			ReadOnlySpan<char> block = parse.GetCurrentBlockName();
			if (strcmpi(block, "solid") == 0) {
				Solid solid = default;

				parse.ParseSolid(ref solid, g_SolidSetup);
				RagdollAddSolid(physEnv, ragdoll, parms, ref solid);
			}
			else if (strcmpi(block, "ragdollconstraint") == 0) {
				ConstraintRagdollParams constraint = default;
				parse.ParseRagdollConstraint(ref constraint, null);
				RagdollAddConstraint(physEnv, ragdoll, parms, ref constraint);
			}
			else if (strcmpi(block, "collisionrules") == 0) {
				IPhysicsCollisionSet set = physics.FindOrCreateCollisionSet((uint)parms.ModelIndex, ragdoll.ListCount);
				ParseCustomBlock(parse, new RagdollCollisionRules(set));
			}
			else if (strcmpi(block, "animatedfriction") == 0)
				ParseCustomBlock(parse, new RagdollAnimatedFrictionParser(ragdoll));
			else
				parse.SkipBlock();
		}
		physcollision.VPhysicsKeyParserDestroy(parse);
	}

	public static void RagdollSetupCollisions(Ragdoll ragdoll, VCollide? collide, int modelIndex) {
		Assert(collide != null);
		if (collide == null)
			return;

		IPhysicsCollisionSet? set = physics.FindCollisionSet((uint)modelIndex);
		if (set == null) {
			set = physics.FindOrCreateCollisionSet((uint)modelIndex, ragdoll.ListCount);
			if (set == null)
				return;

			bool foundRules = false;

			IVPhysicsKeyParser parse = physcollision.VPhysicsKeyParserCreate(collide.KeyValues);
			while (!parse.Finished()) {
				ReadOnlySpan<char> block = parse.GetCurrentBlockName();
				if (strcmpi(block, "collisionrules") == 0) {
					IPhysicsCollisionSet setRules = physics.FindOrCreateCollisionSet((uint)modelIndex, ragdoll.ListCount);
					ParseCustomBlock(parse, new RagdollCollisionRules(setRules));
					foundRules = true;
				}
				else
					parse.SkipBlock();
			}
			physcollision.VPhysicsKeyParserDestroy(parse);

			if (!foundRules) {
				int i;
				for (i = 0; i < ragdoll.ListCount; i++) {
					for (int j = i + 1; j < ragdoll.ListCount; j++)
						set.EnableCollisions(i, j);
				}
				for (i = 0; i < ragdoll.ListCount; i++) {
					int parent = ragdoll.List[i].ParentIndex;
					if (parent >= 0) {
						Assert(ragdoll.List[i].Object != null);
						Assert(ragdoll.List[i].Constraint != null);
						set.DisableCollisions(i, parent);
					}
				}
			}
		}
	}

	public static void RagdollActivate(Ragdoll ragdoll, VCollide? collide, int modelIndex, bool forceWake) {
		RagdollSetupCollisions(ragdoll, collide, modelIndex);
		for (int i = 0; i < ragdoll.ListCount; i++) {
			IPhysicsObject obj = ragdoll.List[i].Object!;
			obj.SetGameIndex((ushort)i);
			PhysSetGameFlags(obj, PhysicsFlags.MultiObjectEntity);
			obj.EnableCollisions(true);

			if (forceWake)
				obj.Wake();
		}
		if (ragdoll.Group != null) {
			ragdoll.Group.Activate();
			if (!forceWake) {
				for (int i = 0; i < ragdoll.ListCount; i++)
					ragdoll.List[i].Object!.Sleep();
			}
		}
	}

	public static bool RagdollCreate(Ragdoll ragdoll, in RagdollParams parms, IPhysicsEnvironment physEnv) {
		RagdollCreateObjects(physEnv, ragdoll, parms);

		if (ragdoll.ListCount == 0)
			return false;

		int forceBone = parms.ForceBoneIndex;

		int i;
		float totalMass = 0;
		for (i = 0; i < ragdoll.ListCount; i++)
			totalMass += ragdoll.List[i].Object!.GetMass();
		totalMass = MathF.Max(totalMass, 1);

		Vector3 nudgeForce = parms.ForceVector;
		Vector3 forcePosition = parms.ForcePosition;

		Assert(forceBone < ragdoll.ListCount);

		if (forceBone >= 0 && forceBone < ragdoll.ListCount) {
			ragdoll.List[forceBone].Object!.ApplyForceCenter(nudgeForce);
			ragdoll.List[forceBone].Object!.GetPosition(out forcePosition, out _);
		}

		for (i = 0; i < ragdoll.ListCount; i++)
			PhysSetGameFlags(ragdoll.List[i].Object!, PhysicsFlags.PartOfRagdoll);

		if (forcePosition != vec3_origin) {
			for (i = 0; i < ragdoll.ListCount; i++) {
				if (forceBone != i) {
					float scale = ragdoll.List[i].Object!.GetMass() / totalMass;
					ragdoll.List[i].Object!.ApplyForceOffset(scale * nudgeForce, forcePosition);
				}
			}
		}

		return true;
	}

	public static void RagdollApplyAnimationAsVelocity(Ragdoll ragdoll, ReadOnlySpan<Matrix3x4> prevBones, ReadOnlySpan<Matrix3x4> currentBones, TimeUnit_t dt) {
		for (int i = 0; i < ragdoll.ListCount; i++) {
			int boneIndex = ragdoll.BoneIndex[i];
			BoneSetup.CalcBoneDerivatives(out Vector3 velocity, out Vector3 angVel, prevBones[boneIndex], currentBones[boneIndex], (float)dt);

			// Angular velocity is always applied in local space in vphysics
			ragdoll.List[i].Object!.WorldToLocalVector(out Vector3 localAngVelocity, angVel);
			ragdoll.List[i].Object!.AddVelocity(velocity, localAngVelocity);
		}
	}

	public static void RagdollApplyAnimationAsVelocity(Ragdoll ragdoll, ReadOnlySpan<Matrix3x4> boneToWorld) {
		for (int i = 0; i < ragdoll.ListCount; i++) {
			MathLib.MatrixInvert(boneToWorld[i], out Matrix3x4 inverse);
			MathLib.MatrixAngles(inverse, out Quaternion q, out Vector3 pos);

			Vector3 velocity;
			float flSpin;

			Vector3 localVelocity;
			Vector3 localAngVelocity;

			MathLib.QuaternionAxisAngle(q, out localAngVelocity, out flSpin);
			localAngVelocity *= flSpin;
			localVelocity = pos;

			// move those bone-local coords back to world space using the ragdoll transform
			ragdoll.List[i].Object!.LocalToWorldVector(out velocity, localVelocity);
			ragdoll.List[i].Object!.AddVelocity(velocity, localAngVelocity);
		}
	}

	public static void RagdollDestroy(Ragdoll ragdoll) {
		if (ragdoll.ListCount == 0)
			return;

		int i;
		for (i = 0; i < ragdoll.ListCount; i++) {
			physenv.DestroyConstraint(ragdoll.List[i].Constraint!);
			ragdoll.List[i].Constraint = null;
		}
		for (i = 0; i < ragdoll.ListCount; i++) {
			if (ragdoll.List[i].Object != null)
				physenv.DestroyObject(ragdoll.List[i].Object);
			ragdoll.List[i].Object = null;
		}
		physenv.DestroyConstraintGroup(ragdoll.Group!);
		ragdoll.Group = null;
		ragdoll.ListCount = 0;
	}

	public static int RagdollExtractBoneIndices(Span<int> boneIndexOut, StudioHdr studioHdr, VCollide collide) {
		int elementCount = 0;

		IVPhysicsKeyParser parse = physcollision.VPhysicsKeyParserCreate(collide.KeyValues);
		while (!parse.Finished()) {
			ReadOnlySpan<char> block = parse.GetCurrentBlockName();
			if (strcmpi(block, "solid") == 0) {
				Solid solid = default;
				parse.ParseSolid(ref solid, null);
				if (elementCount < RAGDOLL_MAX_ELEMENTS) {
					boneIndexOut[elementCount] = BoneSetup.Studio_BoneIndexByName(studioHdr, ((ReadOnlySpan<char>)solid.Name).SliceNullTerminatedString());
					elementCount++;
				}
			}
			else
				parse.SkipBlock();
		}
		physcollision.VPhysicsKeyParserDestroy(parse);

		return elementCount;
	}

	public static bool RagdollGetBoneMatrix(Ragdoll ragdoll, BoneAccessor boneToWorld, int objectIndex) {
		int BoneIndex = ragdoll.BoneIndex[objectIndex];
		if (BoneIndex < 0)
			return false;

		ref readonly RagdollElement element = ref ragdoll.List[objectIndex];

		// during restore if a model has changed since the file was saved, this could be NULL
		if (element.Object == null)
			return false;
		element.Object.GetPositionMatrix(out boneToWorld.GetBoneForWrite(BoneIndex));
		if (element.ParentIndex >= 0 && !ragdoll.AllowStretch) {
			// overwrite the position from physics to force rigid attachment
			// UNDONE: If we support other types of constraints (or multiple constraints per object)
			// make sure these don't fight !
			int parentBoneIndex = ragdoll.BoneIndex[element.ParentIndex];
			MathLib.VectorTransform(element.OriginParentSpace, boneToWorld.GetBone(parentBoneIndex), out Vector3 outVec);
			MathLib.MatrixSetColumn(outVec, 3, ref boneToWorld.GetBoneForWrite(BoneIndex));
		}
		return true;
	}

	public static void RagdollComputeExactBbox(Ragdoll ragdoll, in Vector3 origin, ref Vector3 outMins, ref Vector3 outMaxs) {
		outMins = origin;
		outMaxs = origin;

		for (int i = 0; i < ragdoll.ListCount; i++) {
			IPhysicsObject obj = ragdoll.List[i].Object!;
			obj.GetPosition(out Vector3 objectOrg, out QAngle objectAng);
			physcollision.CollideGetAABB(out Vector3 mins, out Vector3 maxs, obj.GetCollide(), objectOrg, objectAng);
			outMins = Vector3.Min(outMins, mins);
			outMaxs = Vector3.Max(outMaxs, maxs);
		}
	}

	public static bool RagdollIsAsleep(Ragdoll ragdoll) {
		for (int i = 0; i < ragdoll.ListCount; i++) {
			if (ragdoll.List[i].Object != null && !ragdoll.List[i].Object!.IsAsleep())
				return false;
		}

		return true;
	}

	public static void RagdollSolveSeparation(Ragdoll ragdoll, BaseEntity? entity) {
		Span<byte> needsFix = stackalloc byte[256];
		int fixCount = 0;
		Assert(ragdoll.ListCount <= needsFix.Length);
		for (int i = 0; i < ragdoll.ListCount; i++) {
			needsFix[i] = 0;
			ref readonly RagdollElement element = ref ragdoll.List[i];
			if (element.Constraint != null && element.ParentIndex >= 0) {
				element.Object!.GetPosition(out Vector3 start, out _);
				ragdoll.List[element.ParentIndex].Object!.LocalToWorld(out Vector3 target, element.OriginParentSpace);
				if (needsFix[element.ParentIndex] != 0) {
					needsFix[i] = 1;
					++fixCount;
					continue;
				}
				Vector3 dir = target - start;
				if (dir.LengthSquared() > 1.0f) {
#if !TF_CLIENT_DLL
					float mass = element.Object.GetMass();
					float massParent = ragdoll.List[element.ParentIndex].Object!.GetMass();

					if (mass * 2.0f < massParent) {
						needsFix[i] = 1;
						++fixCount;
						continue;
					}
#endif

					if (PhysHasContactWithOtherInDirection(element.Object, dir)) {
						Ray ray = default;
						ray.Init(target, start);
						Util.TraceRay(ray, Mask.Solid, entity, CollisionGroup.None, out Trace tr);
						if (tr.DidHit()) {
							needsFix[i] = 1;
							++fixCount;
						}
					}
				}
			}
		}

		if (fixCount != 0) {
			for (int i = 0; i < ragdoll.ListCount; i++) {
				if (needsFix[i] == 0)
					continue;

				ref readonly RagdollElement element = ref ragdoll.List[i];
				IPhysicsObject parentObject = ragdoll.List[element.ParentIndex].Object!;
				parentObject.LocalToWorld(out Vector3 target, element.OriginParentSpace);
				parentObject.GetVelocityAtPoint(target, out Vector3 velocity);
				element.Object!.GetPositionMatrix(out Matrix3x4 xform);
				MathLib.MatrixSetColumn(target, 3, ref xform);
				element.Object.SetPositionMatrix(xform, true);
				element.Object.SetVelocity(velocity, vec3_origin);
			}
			DevMsg(2, $"TICK:{gpGlobals.TickCount,5}:Ragdoll separation count: {fixCount}\n");
		}
		else
			ragdoll.Group!.ClearErrorState();
	}
}
#endif
