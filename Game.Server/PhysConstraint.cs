global using static Game.Server.PhysConstraintEvents;
global using static Game.Server.PhysConstraintGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;
using Source.Common.Mathematics;
using Source.Common.Physics;
using Source.Common.SoundEmitterSystem;

#if GMOD_DLL
using Game.Server.GarrysMod;
#endif

using System.Numerics;

namespace Game.Server;

public static class PhysConstraintGlobals
{
	public const int SF_CONSTRAINT_DISABLE_COLLISION = 1;
	public const int SF_SLIDE_LIMIT_ENDS = 2;
	public const int SF_PULLEY_RIGID = 2;
	public const int SF_LENGTH_RIGID = 2;
	public const int SF_RAGDOLL_FREEMOVEMENT = 2;
	public const int SF_CONSTRAINT_START_INACTIVE = 4;
	public const int SF_CONSTRAINT_ASSUME_WORLD_GEOMETRY = 8;
	public const int SF_CONSTRAINT_NO_CONNECT_UNTIL_ACTIVATED = 16;

	public static readonly ConVar g_debug_constraint_sounds = new("g_debug_constraint_sounds", "0", FCvar.Cheat, "Enable debug printing about constraint sounds.");

	internal static readonly AnchorList g_AnchorList = new("CAnchorList");

	public static void PhysTeleportConstrainedEntity(BaseEntity teleportSource, IPhysicsObject? object0, IPhysicsObject? object1, in Vector3 prevPosition, in QAngle prevAngles, bool physicsRotate) {
		if (object0 == null || object1 == null)
			return;

		BaseEntity? entity0 = (BaseEntity?)object0.GetGameData();
		BaseEntity? entity1 = (BaseEntity?)object1.GetGameData();
		if (entity0 == null || entity1 == null)
			return;

		BaseEntity fixup = entity1;
		if (teleportSource != entity0) {
			if (teleportSource != entity1) {
				Msg("Bogus teleport notification!!\n");
				return;
			}
			fixup = entity0;
		}

		if (fixup.GetMoveType() != MoveType.VPhysics)
			return;

		if (fixup.VPhysicsGetObject() == null || !fixup.VPhysicsGetObject()!.IsMoveable())
			return;

		QAngle oldAngles = prevAngles;

		if (!physicsRotate)
			oldAngles = teleportSource.GetAbsAngles();

		MathLib.AngleMatrix(oldAngles, prevPosition, out Matrix3x4 startCoord);
		MathLib.MatrixInvert(startCoord, out Matrix3x4 startInv);
		MathLib.ConcatTransforms(teleportSource.EntityToWorldTransform(), startInv, out Matrix3x4 xform);

		MathLib.ConcatTransforms(xform, fixup.EntityToWorldTransform(), out Matrix3x4 endCoord);
		MathLib.MatrixAngles(endCoord, out QAngle fixupAngles, out Vector3 fixupPos);
		fixup.Teleport(fixupPos, fixupAngles, null);
	}

	static void DrawPhysicsBounds(IPhysicsObject obj, int r, int g, int b, int a) {
		PhysCollide collide = obj.GetCollide();
		obj.GetPosition(out Vector3 pos, out QAngle angles);
		physcollision.CollideGetAABB(out Vector3 mins, out Vector3 maxs, collide, vec3_origin, vec3_angle);
		mins -= new Vector3(1, 1, 1);
		maxs += new Vector3(1, 1, 1);
		DebugOverlay.BoxAngles(pos, mins, maxs, angles, r, g, b, a, 0);
	}

	internal static void DrawConstraintObjectsAxes(BaseEntity? constraintEntity, IPhysicsConstraint? constraint) {
		if (constraint == null || constraintEntity == null)
			return;
		bool xform = constraint.GetConstraintTransform(out Matrix3x4 xformRef, out Matrix3x4 xformAtt);
		IPhysicsObject? refObj = constraint.GetReferenceObject();

		if (refObj != null && !refObj.IsStatic()) {
			if (xform) {
				MathLib.MatrixAngles(xformRef, out QAngle angles, out Vector3 pos);
				refObj.LocalToWorld(out Vector3 posWorld, pos);
				DebugOverlay.Axis(posWorld, vec3_angle, 12, false, 0);
			}
			DrawPhysicsBounds(refObj, 0, 255, 0, 12);
		}
		IPhysicsObject? attach = constraint.GetAttachedObject();
		if (attach != null && !attach.IsStatic()) {
			if (xform) {
				MathLib.MatrixAngles(xformAtt, out QAngle angles, out Vector3 pos);
				attach.LocalToWorld(out Vector3 posWorld, pos);
				DebugOverlay.Axis(posWorld, vec3_angle, 12, false, 0);
			}
			DrawPhysicsBounds(attach, 255, 0, 0, 12);
		}
	}

	public static bool GetConstraintAttachments(BaseEntity entity, Span<BaseEntity?> attachOut, Span<IPhysicsObject?> attachVPhysics) {
		if (entity is PhysConstraint constraintEntity) {
			IPhysicsConstraint? constraint = constraintEntity.GetPhysConstraint();
			if (constraint != null) {
				IPhysicsObject? refObj = constraint.GetReferenceObject();
				attachVPhysics[0] = refObj;
				attachOut[0] = refObj != null ? (BaseEntity?)refObj.GetGameData() : null;
				IPhysicsObject? attach = constraint.GetAttachedObject();
				attachVPhysics[1] = attach;
				attachOut[1] = attach != null ? (BaseEntity?)attach.GetGameData() : null;
				return true;
			}
		}
		return false;
	}

	public static void DebugConstraint(BaseEntity entity) {
		if (entity is PhysConstraint constraintEntity) {
			IPhysicsConstraint? constraint = constraintEntity.GetPhysConstraint();
			constraint?.OutputDebugInfo();
		}
	}

	internal static void FindPhysicsAnchor(string? name, ref HlConstraintInfo info, int index, BaseEntity errorEntity) {
#if GMOD_DLL
		if (info.Objects[0] != null || info.Objects[1] != null) {
			if (!LuaPhysObj.IsPhysicsObjectValid(info.Objects[0]))
				info.Objects[0] = null;
			if (!LuaPhysObj.IsPhysicsObjectValid(info.Objects[1]))
				info.Objects[1] = null;
		}
#endif
		int anchorIndex = g_AnchorList.Find(name);
		if (anchorIndex >= 0) {
			ref ConstraintAnchorT anchor = ref g_AnchorList.Get(anchorIndex);
			BaseEntity? entity = anchor.Entity.Get();
			if (entity != null) {
				info.MassScale[index] = anchor.MassScale;
				bool wroteAttachment = false;
				if (anchor.ParentAttachment > 0) {
					BaseAnimating? anim = entity.GetBaseAnimating();
					if (anim != null) {
						IPhysicsObject?[] list = new IPhysicsObject?[VPHYSICS_MAX_OBJECT_LIST_COUNT];
						int listCount = entity.VPhysicsGetObjectList(list);
						int physicsBone = anim.GetPhysicsBone(anim.GetAttachmentBone(anchor.ParentAttachment));
						if (physicsBone < listCount) {
							info.Objects[index] = list[physicsBone];
							anim.GetAttachment(anchor.ParentAttachment, out Vector3 pos, out QAngle _);
							list[physicsBone]!.WorldToLocal(out info.AnchorPosition[index], pos);
							wroteAttachment = true;
						}
					}
				}
				if (!wroteAttachment) {
					info.AnchorPosition[index] = anchor.LocalOrigin;
					info.Objects[index] = entity.VPhysicsGetObject();
				}
			}
			else
				anchorIndex = -1;
		}
		if (anchorIndex < 0) {
			info.AnchorPosition[index] = vec3_origin;
			info.Objects[index] = PhysicsHook.FindPhysicsObjectByName(name, errorEntity);
			info.MassScale[index] = 1.0f;
		}
	}

	internal static IPhysicsConstraintGroup? GetRagdollConstraintGroup(IPhysicsObject? obj) {
		if (obj != null) {
			BaseEntity? entity = (BaseEntity?)obj.GetGameData();
			Ragdoll? ragdoll = RagdollProp.Ragdoll_GetRagdoll(entity!);
			if (ragdoll != null)
				return ragdoll.Group;
		}
		return null;
	}

	public static IPhysicsConstraintGroup? GetConstraintGroup(string? systemName) {
#if GMOD_DLL
		if (LuaGlobalFunctions.g_PhysConstrantSystem.Get() is PhysConstraintSystem globalGroup)
			return globalGroup.GetVPhysicsGroup();
#endif
		BaseEntity? machine = gEntList.FindEntityByName(null, systemName);

		if (machine != null) {
			if (machine is PhysConstraintSystem group)
				return group.GetVPhysicsGroup();
		}
		return null;
	}

	internal static int GetUnitAxisIndex(in Vector3 axis) {
		bool valid = false;
		int index = -1;

		for (int i = 0; i < 3; i++) {
			if (axis[i] != 0) {
				if (MathF.Abs(axis[i]) == 1) {
					if (index < 0) {
						index = i;
						valid = true;
						continue;
					}
				}
				valid = false;
			}
		}
		return valid ? index : -1;
	}
}

public struct ConstraintAnchorT
{
	public Vector3 LocalOrigin;
	public EHANDLE Entity;
	public int ParentAttachment;
	public string? Name;
	public float MassScale;
}

public class AnchorList(ReadOnlySpan<char> name) : AutoGameSystem(name)
{
	readonly List<ConstraintAnchorT> List = [];

	public override void LevelShutdownPostEntity() => List.Clear();

	public void AddToList(BaseEntity entity, float massScale) {
		ConstraintAnchorT anchor = new();
		anchor.Entity = new();
		anchor.Entity.Set(entity.GetParent());
		anchor.ParentAttachment = entity.ParentAttachment;
		anchor.Name = entity.GetEntityName();
		anchor.LocalOrigin = entity.GetLocalOrigin();
		anchor.MassScale = massScale;
		List.Add(anchor);
	}

	public int Find(string? name) {
		for (int i = List.Count - 1; i >= 0; i--) {
			if (FStrEq(List[i].Name ?? "", name ?? ""))
				return i;
		}
		return -1;
	}

	public ref ConstraintAnchorT Get(int index) => ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(List)[index];
}

[LinkEntityToClass("info_constraint_anchor")]
public class ConstraintAnchor : PointEntity
{
	public static readonly new DataMap DataDesc = new(typeof(ConstraintAnchor), BaseEntity.DataDesc, [
		DEFINE<ConstraintAnchor>.KEYFIELD(nameof(MassScale), FieldType.Float, "massScale"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public float MassScale;

	public ConstraintAnchor() {
		MassScale = 1.0f;
	}

	public override void Spawn() {
		if (GetParent() != null) {
			g_AnchorList.AddToList(this, MassScale);
			Util.Remove(this);
		}
	}
}

[LinkEntityToClass("phys_constraintsystem")]
public class PhysConstraintSystem : LogicalEntity
{
	public static readonly new DataMap DataDesc = new(typeof(PhysConstraintSystem), BaseEntity.DataDesc, [
		DEFINE<PhysConstraintSystem>.KEYFIELD(nameof(AdditionalIterations), FieldType.Integer, "additionaliterations"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	IPhysicsConstraintGroup? Machine;
	int AdditionalIterations;

	public IPhysicsConstraintGroup? GetVPhysicsGroup() => Machine;

	public override void Spawn() {
		if (Machine != null)
			Error("CPhysConstraintSystem already has constraint group?\n");

		ConstraintGroupParams group = default;
		group.Defaults();
		group.AdditionalIterations = AdditionalIterations;
		Machine = physenv.CreateConstraintGroup(group);
	}
}

public struct HlConstraintInfo
{
	public HlConstraintInfo() {
		Objects[0] = Objects[1] = null;
		Group = null;
		AnchorPosition[0].Init();
		AnchorPosition[1].Init();
		Swapped = false;
		MassScale[0] = MassScale[1] = 1.0f;
	}

	public InlineArray2<Vector3> AnchorPosition;
	public InlineArray2<IPhysicsObject?> Objects;
	public IPhysicsConstraintGroup? Group;
	public InlineArray2<float> MassScale;
	public bool Swapped;
}

public abstract class PhysConstraint : LogicalEntity
{
	public static readonly new DataMap DataDesc = new(typeof(PhysConstraint), BaseEntity.DataDesc, [
		DEFINE<PhysConstraint>.KEYFIELD(nameof(NameSystem), FieldType.String, "constraintsystem"),
		DEFINE<PhysConstraint>.KEYFIELD(nameof(NameAttach1), FieldType.String, "attach1"),
		DEFINE<PhysConstraint>.KEYFIELD(nameof(NameAttach2), FieldType.String, "attach2"),
		DEFINE<PhysConstraint>.KEYFIELD(nameof(BreakSound), FieldType.SoundName, "breaksound"),
		DEFINE<PhysConstraint>.KEYFIELD(nameof(ForceLimit), FieldType.Float, "forcelimit"),
		DEFINE<PhysConstraint>.KEYFIELD(nameof(TorqueLimit), FieldType.Float, "torquelimit"),
		DEFINE<PhysConstraint>.KEYFIELD(nameof(MinTeleportDistance), FieldType.Float, "teleportfollowdistance"),

		DEFINE<PhysConstraint>.OUTPUT(nameof(OnBreakOutput), "OnBreak", eventFuncs),

		DEFINE<PhysConstraint>.INPUTFUNC(FieldType.Void, "Break", nameof(InputBreak), (INPUTFUNCPTR)((self, data) => ((PhysConstraint)self).InputBreak(data))),
		DEFINE<PhysConstraint>.INPUTFUNC(FieldType.Void, "ConstraintBroken", nameof(InputOnBreak), (INPUTFUNCPTR)((self, data) => ((PhysConstraint)self).InputOnBreak(data))),

		DEFINE<PhysConstraint>.INPUTFUNC(FieldType.Void, "TurnOn", nameof(InputTurnOn), (INPUTFUNCPTR)((self, data) => ((PhysConstraint)self).InputTurnOn(data))),
		DEFINE<PhysConstraint>.INPUTFUNC(FieldType.Void, "TurnOff", nameof(InputTurnOff), (INPUTFUNCPTR)((self, data) => ((PhysConstraint)self).InputTurnOff(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	protected IPhysicsConstraint? Constraint;

	protected string? NameAttach1;
	protected string? NameAttach2;
	protected string? BreakSound;
	protected string? NameSystem;
	protected float ForceLimit;
	protected float TorqueLimit;
	protected uint TeleportTick;
	protected float MinTeleportDistance;

	protected readonly OutputEvent OnBreakOutput = new();

#if GMOD_DLL
	protected InlineArray2<IPhysicsObject?> PhysObjects;
#endif

	public PhysConstraint() {
		Constraint = null;
		NameAttach1 = null;
		NameAttach2 = null;
		BreakSound = null;
		NameSystem = null;
		ForceLimit = 0;
		TorqueLimit = 0;
		TeleportTick = uint.MaxValue;
		MinTeleportDistance = 0.0f;
#if GMOD_DLL
		PhysObjects[0] = null;
		PhysObjects[1] = null;
#endif
	}

	public override void Term() {
		Deactivate();
		physenv.DestroyConstraint(Constraint!);
		base.Term();
	}

	public override void Precache() {
		if (BreakSound != null)
			PrecacheScriptSound(BreakSound);
	}

	public override void Spawn() {
		base.Spawn();

		Precache();
	}

	public void ClearStaticFlag(IPhysicsObject? obj) {
		if (obj == null)
			return;
		PhysClearGameFlags(obj, PhysicsFlags.ConstraintStatic);
	}

	public virtual void Deactivate() {
		if (Constraint == null)
			return;
		Constraint.Deactivate();
		ClearStaticFlag(Constraint.GetReferenceObject());
		ClearStaticFlag(Constraint.GetAttachedObject());
		if ((SpawnFlags & SF_CONSTRAINT_DISABLE_COLLISION) != 0) {
			IPhysicsObject? refObj = Constraint.GetReferenceObject();
			IPhysicsObject? att = Constraint.GetAttachedObject();
			if (refObj != null && att != null)
				PhysEnableEntityCollisions(refObj, att);
		}
	}

	public void OnBreak() {
		Deactivate();
		if (BreakSound != null) {
			PASAttenuationFilter filter = new(this, ATTN_STATIC);

			Vector3 origin = GetAbsOrigin();
			Vector3 refPos = origin, attachPos = origin;

			IPhysicsObject? refObj = Constraint!.GetReferenceObject();
			if (refObj != null && refObj != g_PhysWorldObject) {
				refObj.GetPosition(out refPos, out _);
				attachPos = refPos;
			}
			IPhysicsObject? attach = Constraint.GetAttachedObject();
			if (attach != null && attach != g_PhysWorldObject) {
				attach.GetPosition(out attachPos, out _);
				if (refObj == null || refObj == g_PhysWorldObject)
					refPos = attachPos;
			}

			origin = refPos + attachPos;
			origin *= 0.5f;

			scoped EmitSound_t ep = new();
			ep.Channel = (int)SoundEntityChannel.Static;
			ep.SoundName = BreakSound;
			ep.Volume = VOL_NORM;
			ep.SoundLevel = ATTN_TO_SNDLVL(ATTN_STATIC);
			ep.Origin = ref origin;

			HSOUNDSCRIPTHANDLE handle = ep.SoundScriptHandle;
			EmitSound(filter, EntIndex(), ref ep, ref handle);
		}
		OnBreakOutput.FireOutput(this, this);
		PhysCallbackRemove(NetworkProp());
	}

	public void InputBreak(in InputData inputdata) {
		Constraint?.Deactivate();

		OnBreak();
	}

	public void InputOnBreak(in InputData inputdata) {
		OnBreak();
	}

	public void InputTurnOn(in InputData inputdata) {
		if (HasSpawnFlags(SF_CONSTRAINT_NO_CONNECT_UNTIL_ACTIVATED))
			ActivateConstraint();

		if (Constraint == null || Constraint.GetReferenceObject() == null || Constraint.GetAttachedObject() == null)
			return;

		Constraint.Activate();
		Constraint.GetReferenceObject()!.Wake();
		Constraint.GetAttachedObject()!.Wake();
	}

	public void InputTurnOff(in InputData inputdata) {
		Deactivate();
	}

	public override int DrawDebugTextOverlays() {
		int pos = base.DrawDebugTextOverlays();
		if (Constraint != null && (DebugOverlays & DebugOverlayBits.Text) != 0) {
			if (Constraint.GetConstraintParams(out ConstraintBreakableParams parms)) {
				if ((parms.BodyMassScale[0] != 1.0f && parms.BodyMassScale[0] != 0.0f) || (parms.BodyMassScale[1] != 1.0f && parms.BodyMassScale[1] != 0.0f)) {
					string str = $"mass ratio {parms.BodyMassScale[0]:F4}:{parms.BodyMassScale[1]:F4}\n";
					DebugOverlay.EntityTextAtPosition(GetAbsOrigin(), pos, str, 0, 255, 255, 0, 255);
				}
			}
			else
				DebugOverlay.EntityTextAtPosition(GetAbsOrigin(), pos, "<no constraint params>", 0, 255, 255, 0, 255);

			pos++;
		}
		return pos;
	}

	public override void DrawDebugGeometryOverlays() {
		if ((DebugOverlays & (DebugOverlayBits.BBox | DebugOverlayBits.Pivot | DebugOverlayBits.AbsBox)) != 0)
			DrawConstraintObjectsAxes(this, Constraint);
		base.DrawDebugGeometryOverlays();
	}

	public void GetBreakParams(ref ConstraintBreakableParams parms, in HlConstraintInfo info) {
		parms.Defaults();
		parms.ForceLimit = lbs2kg(ForceLimit);
		parms.TorqueLimit = lbs2kg(TorqueLimit);
		parms.IsActive = HasSpawnFlags(SF_CONSTRAINT_START_INACTIVE) ? false : true;
		parms.BodyMassScale[0] = info.MassScale[0];
		parms.BodyMassScale[1] = info.MassScale[1];
	}

	public override void NotifySystemEvent(BaseEntity notify, NotifySystemEvent eventType, in NotifySystemEventParams parms) {
		if (eventType != Game.Server.NotifySystemEvent.Teleport || (uint)gpGlobals.TickCount == TeleportTick)
			return;

		float distance = (parms.Teleport.PrevOrigin - notify.GetAbsOrigin()).Length();

		if (distance <= MinTeleportDistance)
			return;

		TeleportTick = (uint)gpGlobals.TickCount;

		PhysTeleportConstrainedEntity(notify, Constraint!.GetReferenceObject()!, Constraint.GetAttachedObject()!, parms.Teleport.PrevOrigin, parms.Teleport.PrevAngles, parms.Teleport.PhysicsRotate);
	}

	public virtual void OnConstraintSetup(ref HlConstraintInfo info) {
#if GMOD_DLL
		if (info.Objects[0] != null || info.Objects[1] != null) {
			if (!LuaPhysObj.IsPhysicsObjectValid(info.Objects[0]))
				info.Objects[0] = null;
			if (!LuaPhysObj.IsPhysicsObjectValid(info.Objects[1]))
				info.Objects[1] = null;
		}
#endif
		if (info.Objects[0] != null && info.Objects[1] != null)
			SetupTeleportationHandling(ref info);
		if ((SpawnFlags & SF_CONSTRAINT_DISABLE_COLLISION) != 0)
			PhysDisableEntityCollisions(info.Objects[0], info.Objects[1]);
	}

	public IPhysicsConstraint? GetPhysConstraint() => Constraint;

	public string? GetNameAttach1() => NameAttach1;
	public string? GetNameAttach2() => NameAttach2;

#if GMOD_DLL
	public override void SetPhysObject(int index, IPhysicsObject? obj) {
		if (index < 2) {
			PhysObjects[index] = obj;
			return;
		}
		Msg($" SetPhysObject Error. {index} is too high! Should be either 0 or 1!\n");
	}
#endif

	protected void GetConstraintObjects(ref HlConstraintInfo info) {
		FindPhysicsAnchor(NameAttach1, ref info, 0, this);
		FindPhysicsAnchor(NameAttach2, ref info, 1, this);

#if GMOD_DLL
		if (info.Objects[0] != null || info.Objects[1] != null) {
			if (!LuaPhysObj.IsPhysicsObjectValid(info.Objects[0]))
				info.Objects[0] = null;
			if (!LuaPhysObj.IsPhysicsObjectValid(info.Objects[1]))
				info.Objects[1] = null;
		}

		if (PhysObjects[0] != null || PhysObjects[1] != null) {
			if (!LuaPhysObj.IsPhysicsObjectValid(PhysObjects[0]))
				PhysObjects[0] = null;
			if (!LuaPhysObj.IsPhysicsObjectValid(PhysObjects[1]))
				PhysObjects[1] = null;
		}

		if (info.Objects[0] == null)
			info.Objects[0] = PhysObjects[0];
		if (info.Objects[1] == null)
			info.Objects[1] = PhysObjects[1];

		if (info.Objects[0] == null && info.Objects[1] != null) {
			info.Objects[0] = g_PhysWorldObject;
			info.MassScale[0] = info.MassScale[1] = 1.0f;
		}
		else if (info.Objects[0] != null && info.Objects[1] == null) {
			info.Objects[1] = info.Objects[0];
			info.Objects[0] = g_PhysWorldObject;
			info.MassScale[0] = info.MassScale[1] = 1.0f;
			info.Swapped = true;
		}
#else
		if (info.Objects[0] == null && info.Objects[1] != null) {
			if (!string.IsNullOrEmpty(NameAttach1)) {
				Warning($"Bogus constraint {GetDebugName()} (attaches ENTITY NOT FOUND:{NameAttach1} to {NameAttach2})\n");
#if HL2_EPISODIC
				info.Objects[0] = info.Objects[1] = null;
				return;
#endif
			}
			info.Objects[0] = g_PhysWorldObject;
			info.MassScale[0] = info.MassScale[1] = 1.0f;
		}
		else if (info.Objects[0] != null && info.Objects[1] == null) {
			if (!string.IsNullOrEmpty(NameAttach2)) {
				Warning($"Bogus constraint {GetDebugName()} (attaches {NameAttach1} to ENTITY NOT FOUND:{NameAttach2})\n");
#if HL2_EPISODIC
				info.Objects[0] = info.Objects[1] = null;
				return;
#endif
			}
			info.Objects[1] = info.Objects[0];
			info.Objects[0] = g_PhysWorldObject;
			info.MassScale[0] = info.MassScale[1] = 1.0f;
			info.Swapped = true;
		}
#endif

		info.Group = GetRagdollConstraintGroup(info.Objects[0]);
		if (info.Group == null)
			info.Group = GetRagdollConstraintGroup(info.Objects[1]);
	}

	protected void SetupTeleportationHandling(ref HlConstraintInfo info) {
#if GMOD_DLL
		if (info.Objects[0] != null || info.Objects[1] != null) {
			if (!LuaPhysObj.IsPhysicsObjectValid(info.Objects[0]))
				info.Objects[0] = null;
			if (!LuaPhysObj.IsPhysicsObjectValid(info.Objects[1]))
				info.Objects[1] = null;
		}
#endif
		BaseEntity? entity0 = (BaseEntity?)info.Objects[0]!.GetGameData();
		if (entity0 != null)
			g_pNotify.AddEntity(this, entity0);

		BaseEntity? entity1 = (BaseEntity?)info.Objects[1]!.GetGameData();
		if (entity1 != null)
			g_pNotify.AddEntity(this, entity1);
	}

	public override void Activate() {
		base.Activate();

		if (HasSpawnFlags(SF_CONSTRAINT_NO_CONNECT_UNTIL_ACTIVATED) == false) {
			if (!ActivateConstraint())
				Util.Remove(this);
		}
	}

	protected bool ActivateConstraint() {
#if GMOD_DLL
		if (LuaPhysObj.gPhysCallback) {
			Warning($"Tried to activate constraint ({GetDebugName()}) in physics callback! This could cause crashes! Ignoring!\n");
			return false;
		}
#endif
		HlConstraintInfo info = new();
		if (Constraint != null) {
			info.Objects[0] = Constraint.GetReferenceObject();
			info.Objects[1] = Constraint.GetAttachedObject();
			OnConstraintSetup(ref info);
			return true;
		}

		GetConstraintObjects(ref info);
		if (info.Objects[0] == null || info.Objects[1] == null)
			return false;

		if (info.Objects[0]!.IsStatic() && info.Objects[1]!.IsStatic()) {
			Warning($"Constraint ({GetEntityName()}) attached to two static objects ({NameAttach1} and {(NameAttach2 == null ? "world" : NameAttach2)})!!!\n");
			return false;
		}

		if (info.Objects[0] == info.Objects[1])
			return false;

		if (info.Objects[0]!.GetShadowController() != null && info.Objects[1]!.GetShadowController() != null) {
			Warning($"Constraint ({GetEntityName()}) attached to two shadow objects ({NameAttach1} and {(NameAttach2 == null ? "world" : NameAttach2)})!!!\n");
			return false;
		}
		IPhysicsConstraintGroup? group = GetConstraintGroup(NameSystem);
		if (group == null)
			group = info.Group;
		Constraint = CreateConstraint(group, info);
		if (Constraint == null)
			return false;

		Constraint.SetGameData(this);

		group?.Activate();

		OnConstraintSetup(ref info);

		return true;
	}

	protected abstract IPhysicsConstraint? CreateConstraint(IPhysicsConstraintGroup? group, in HlConstraintInfo info);
}

[LinkEntityToClass("phys_hinge")]
public class PhysHinge : PhysConstraint, IVPhysicsWatcher
{
	public static readonly new DataMap DataDesc = new(typeof(PhysHinge), PhysConstraint.DataDesc, [
		DEFINE<PhysHinge>.KEYFIELD(nameof(HingeFriction), FieldType.Float, "hingefriction"),
		DEFINE<PhysHinge>.FIELD("Hinge.WorldPosition", FieldType.PositionVector),
		DEFINE<PhysHinge>.KEYFIELD("Hinge.WorldAxisDirection", FieldType.Vector, "hingeaxis"),
		DEFINE<PhysHinge>.KEYFIELD(nameof(SystemLoadScale), FieldType.Float, "systemloadscale"),
		DEFINE<PhysHinge>.INPUTFUNC(FieldType.Float, "SetAngularVelocity", nameof(InputSetVelocity), (INPUTFUNCPTR)((self, data) => ((PhysHinge)self).InputSetVelocity(data))),
		DEFINE<PhysHinge>.INPUTFUNC(FieldType.Float, "SetHingeFriction", nameof(InputSetHingeFriction), (INPUTFUNCPTR)((self, data) => ((PhysHinge)self).InputSetHingeFriction(data))),
#if HL2_EPISODIC
		DEFINE<PhysHinge>.KEYFIELD("SoundInfo.SoundProfile.KeyPoints[0]", FieldType.Float, "minSoundThreshold"),
		DEFINE<PhysHinge>.KEYFIELD("SoundInfo.SoundProfile.KeyPoints[1]", FieldType.Float, "maxSoundThreshold"),
		DEFINE<PhysHinge>.KEYFIELD("SoundInfo.TravelSoundFwd", FieldType.SoundName, "slidesoundfwd"),
		DEFINE<PhysHinge>.KEYFIELD("SoundInfo.TravelSoundBack", FieldType.SoundName, "slidesoundback"),

		DEFINE<PhysHinge>.KEYFIELD("SoundInfo.ReversalSounds[0]", FieldType.SoundName, "reversalsoundSmall"),
		DEFINE<PhysHinge>.KEYFIELD("SoundInfo.ReversalSounds[1]", FieldType.SoundName, "reversalsoundMedium"),
		DEFINE<PhysHinge>.KEYFIELD("SoundInfo.ReversalSounds[2]", FieldType.SoundName, "reversalsoundLarge"),

		DEFINE<PhysHinge>.KEYFIELD("SoundInfo.SoundProfile.ReversalSoundThresholds[0]", FieldType.Float, "reversalsoundthresholdSmall"),
		DEFINE<PhysHinge>.KEYFIELD("SoundInfo.SoundProfile.ReversalSoundThresholds[1]", FieldType.Float, "reversalsoundthresholdMedium"),
		DEFINE<PhysHinge>.KEYFIELD("SoundInfo.SoundProfile.ReversalSoundThresholds[2]", FieldType.Float, "reversalsoundthresholdLarge"),

		DEFINE<PhysHinge>.THINKFUNC(nameof(SoundThink)),
#endif
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

#if HL2_EPISODIC
	protected readonly ConstraintSoundInfo SoundInfo = new();

	public override void Term() {
		SoundInfo.Dispose();
		base.Term();
	}
#endif

	ConstraintHingeParams Hinge;
	float HingeFriction;
	float SystemLoadScale;

	protected override IPhysicsConstraint? CreateConstraint(IPhysicsConstraintGroup? group, in HlConstraintInfo info) {
		if (Hinge.WorldAxisDirection == vec3_origin) {
			DevMsg("ERROR: Hinge with bad data!!!\n");
			return null;
		}
		GetBreakParams(ref Hinge.Constraint, info);
		Hinge.Constraint.Strength = 1.0f;
		Hinge.HingeAxis.SetAxisFriction(0, 0, HingeFriction * 1000);

		if (IsWorldHinge(info, out int hingeAxis))
			info.Objects[1]!.BecomeHinged(hingeAxis);
		else
			RemoveSpawnFlags(SF_CONSTRAINT_ASSUME_WORLD_GEOMETRY);

		return physenv.CreateHingeConstraint(info.Objects[0]!, info.Objects[1]!, group!, Hinge);
	}

	public override void DrawDebugGeometryOverlays() {
		if ((DebugOverlays & (DebugOverlayBits.BBox | DebugOverlayBits.Pivot | DebugOverlayBits.AbsBox)) != 0)
			DebugOverlay.Line(Hinge.WorldPosition, Hinge.WorldPosition + 48 * Hinge.WorldAxisDirection, 0, 255, 0, false, 0);
		base.DrawDebugGeometryOverlays();
	}

	void InputSetVelocity(in InputData inputdata) {
		if (Constraint == null || Constraint.GetReferenceObject() == null || Constraint.GetAttachedObject() == null)
			return;

		float speed = inputdata.Value.Float();
		float massLoad = 1;
		int numMasses = 0;
		if (Constraint.GetReferenceObject()!.IsMoveable()) {
			massLoad = Constraint.GetReferenceObject()!.GetInertia().Length();
			numMasses++;
			Constraint.GetReferenceObject()!.Wake();
		}
		if (Constraint.GetAttachedObject()!.IsMoveable()) {
			massLoad += Constraint.GetAttachedObject()!.GetInertia().Length();
			numMasses++;
			Constraint.GetAttachedObject()!.Wake();
		}
		if (numMasses > 0)
			massLoad /= (float)numMasses;

		float loadscale = SystemLoadScale != 0 ? SystemLoadScale : 1;
		Constraint.SetAngularMotor(speed, speed * loadscale * massLoad * loadscale * (1.0f / (float)TICK_INTERVAL));
	}

	void InputSetHingeFriction(in InputData inputdata) {
		HingeFriction = inputdata.Value.Float();
		Msg($"Setting hinge friction to {HingeFriction:F6}\n");
		Hinge.HingeAxis.SetAxisFriction(0, 0, HingeFriction * 1000);
	}

	public override void Deactivate() {
		if (HasSpawnFlags(SF_CONSTRAINT_ASSUME_WORLD_GEOMETRY)) {
			if (Constraint != null && Constraint.GetAttachedObject() != null)
				Constraint.GetAttachedObject()!.RemoveHinged();
		}

		base.Deactivate();
	}

	public void NotifyVPhysicsStateChanged(IPhysicsObject physics, BaseEntity entity, bool awake) {
#if HL2_EPISODIC
		Assert(Constraint != null);
		if (Constraint == null)
			return;

		if (awake) {
			if (GetNextThink() == TICK_NEVER_THINK) {
				SoundInfo.StartThinking(this, VelocitySampler.GetRelativeAngularVelocity(Constraint.GetAttachedObject()!, Constraint.GetReferenceObject()), Hinge.WorldAxisDirection);

				SetThink(SoundThink);
				SetNextThink(gpGlobals.CurTime + SoundInfo.GetThinkRate());
			}
		}
		else {
			if (GetNextThink() != TICK_NEVER_THINK && Constraint.GetAttachedObject()!.IsAsleep() && Constraint.GetReferenceObject()!.IsAsleep()) {
				SoundInfo.StopThinking(this);
				SetNextThink(TICK_NEVER_THINK);
			}
		}
#endif
	}

#if HL2_EPISODIC
	public override void OnConstraintSetup(ref HlConstraintInfo info) {
		BaseEntity? entity0 = info.Objects[0] != null ? (BaseEntity?)info.Objects[0]!.GetGameData() : null;
		if (entity0 != null && !info.Objects[0]!.IsStatic())
			WatchVPhysicsStateChanges(this, entity0);
		BaseEntity? entity1 = info.Objects[1] != null ? (BaseEntity?)info.Objects[1]!.GetGameData() : null;
		if (entity1 != null && !info.Objects[1]!.IsStatic())
			WatchVPhysicsStateChanges(this, entity1);
		base.OnConstraintSetup(ref info);
	}
#endif

	public override void Spawn() {
		Hinge.WorldPosition = GetLocalOrigin();
		Hinge.WorldAxisDirection -= GetLocalOrigin();
		MathLib.VectorNormalize(ref Hinge.WorldAxisDirection);
#if !GMOD_DLL
		Util.SnapDirectionToAxis(ref Hinge.WorldAxisDirection);
#endif

		Hinge.HingeAxis.SetAxisFriction(0, 0, 0);

		if (HasSpawnFlags(SF_CONSTRAINT_ASSUME_WORLD_GEOMETRY)) {
			MassCenterOverride parms = default;
			if (NameAttach1 == null) {
				parms.SnapToAxis(NameAttach2, Hinge.WorldPosition, Hinge.WorldAxisDirection);
				PhysSetMassCenterOverride(parms);
			}
			else if (NameAttach2 == null) {
				parms.SnapToAxis(NameAttach1, Hinge.WorldPosition, Hinge.WorldAxisDirection);
				PhysSetMassCenterOverride(parms);
			}
			else
				RemoveSpawnFlags(SF_CONSTRAINT_ASSUME_WORLD_GEOMETRY);
		}

		Precache();
	}

#if HL2_EPISODIC
	public override void Activate() {
		base.Activate();

		SoundInfo.OnActivate(this);
		if (Constraint != null) {
			SoundInfo.StartThinking(this, VelocitySampler.GetRelativeAngularVelocity(Constraint.GetAttachedObject()!, Constraint.GetReferenceObject()), Hinge.WorldAxisDirection);

			SetThink(SoundThink);
			SetNextThink(gpGlobals.CurTime + SoundInfo.GetThinkRate());
		}
	}

	public override void Precache() {
		base.Precache();
		SoundInfo.OnPrecache(this);
	}

	void SoundThink() {
		Assert(Constraint != null);
		if (Constraint == null)
			return;

		IPhysicsObject? attached = Constraint.GetAttachedObject(), reference = Constraint.GetReferenceObject();
		Assert(attached != null && reference != null);
		if (attached != null && reference != null) {
			Vector3 relativeVel = VelocitySampler.GetRelativeAngularVelocity(attached, reference);
			if (g_debug_constraint_sounds.GetBool())
				DebugOverlay.Line(GetAbsOrigin(), GetAbsOrigin() + relativeVel, 255, 255, 0, true, 0.1f);
			SoundInfo.OnThink(this, relativeVel);

			SetNextThink(gpGlobals.CurTime + SoundInfo.GetThinkRate());
		}
	}
#endif

	bool IsWorldHinge(in HlConstraintInfo info, out int axisOut) {
		axisOut = 0;
		if (HasSpawnFlags(SF_CONSTRAINT_ASSUME_WORLD_GEOMETRY) && info.Objects[0] == g_PhysWorldObject) {
			info.Objects[1]!.WorldToLocalVector(out Vector3 localHinge, Hinge.WorldAxisDirection);
			Util.SnapDirectionToAxis(ref localHinge);
			int hingeAxis = GetUnitAxisIndex(localHinge);
			if (hingeAxis >= 0) {
				axisOut = hingeAxis;
				return true;
			}
		}
		return false;
	}
}

[LinkEntityToClass("phys_ballsocket")]
public class PhysBallSocket : PhysConstraint
{
	protected override IPhysicsConstraint? CreateConstraint(IPhysicsConstraintGroup? group, in HlConstraintInfo info) {
		ConstraintBallSocketParams ballsocket = default;

		ballsocket.Defaults();

		for (int i = 0; i < 2; i++) {
			info.Objects[i]!.WorldToLocal(out ballsocket.ConstraintPosition[i], GetAbsOrigin());
#if GMOD_DLL
			info.Objects[i]!.GetDamping(out float speed, out float rot);
			if (speed < 0.2f)
				speed = 0.2f;
			if (rot < 0.2f)
				rot = 0.2f;
			info.Objects[i]!.SetDamping(ref speed, ref speed);
#endif
		}
		GetBreakParams(ref ballsocket.Constraint, info);
		ballsocket.Constraint.TorqueLimit = 0;

		return physenv.CreateBallsocketConstraint(info.Objects[0]!, info.Objects[1]!, group!, ballsocket);
	}
}

[LinkEntityToClass("phys_slideconstraint")]
public class PhysSlideConstraint : PhysConstraint, IVPhysicsWatcher
{
	public static readonly new DataMap DataDesc = new(typeof(PhysSlideConstraint), PhysConstraint.DataDesc, [
		DEFINE<PhysSlideConstraint>.KEYFIELD(nameof(AxisEnd), FieldType.PositionVector, "slideaxis"),
		DEFINE<PhysSlideConstraint>.KEYFIELD(nameof(SlideFriction), FieldType.Float, "slidefriction"),
		DEFINE<PhysSlideConstraint>.KEYFIELD(nameof(SystemLoadScale), FieldType.Float, "systemloadscale"),
		DEFINE<PhysSlideConstraint>.INPUTFUNC(FieldType.Float, "SetVelocity", nameof(InputSetVelocity), (INPUTFUNCPTR)((self, data) => ((PhysSlideConstraint)self).InputSetVelocity(data))),
#if HL2_EPISODIC
		DEFINE<PhysSlideConstraint>.KEYFIELD("SoundInfo.SoundProfile.KeyPoints[0]", FieldType.Float, "minSoundThreshold"),
		DEFINE<PhysSlideConstraint>.KEYFIELD("SoundInfo.SoundProfile.KeyPoints[1]", FieldType.Float, "maxSoundThreshold"),
		DEFINE<PhysSlideConstraint>.KEYFIELD("SoundInfo.TravelSoundFwd", FieldType.SoundName, "slidesoundfwd"),
		DEFINE<PhysSlideConstraint>.KEYFIELD("SoundInfo.TravelSoundBack", FieldType.SoundName, "slidesoundback"),

		DEFINE<PhysSlideConstraint>.KEYFIELD("SoundInfo.ReversalSounds[0]", FieldType.SoundName, "reversalsoundSmall"),
		DEFINE<PhysSlideConstraint>.KEYFIELD("SoundInfo.ReversalSounds[1]", FieldType.SoundName, "reversalsoundMedium"),
		DEFINE<PhysSlideConstraint>.KEYFIELD("SoundInfo.ReversalSounds[2]", FieldType.SoundName, "reversalsoundLarge"),

		DEFINE<PhysSlideConstraint>.KEYFIELD("SoundInfo.SoundProfile.ReversalSoundThresholds[0]", FieldType.Float, "reversalsoundthresholdSmall"),
		DEFINE<PhysSlideConstraint>.KEYFIELD("SoundInfo.SoundProfile.ReversalSoundThresholds[1]", FieldType.Float, "reversalsoundthresholdMedium"),
		DEFINE<PhysSlideConstraint>.KEYFIELD("SoundInfo.SoundProfile.ReversalSoundThresholds[2]", FieldType.Float, "reversalsoundthresholdLarge"),

		DEFINE<PhysSlideConstraint>.THINKFUNC(nameof(SoundThink)),
#endif
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public Vector3 AxisEnd;
	public float SlideFriction;
	public float SystemLoadScale;

#if HL2_EPISODIC
	protected readonly ConstraintSoundInfo SoundInfo = new();

	public override void Term() {
		SoundInfo.Dispose();
		base.Term();
	}

#endif

	void InputSetVelocity(in InputData inputdata) {
		if (Constraint == null || Constraint.GetReferenceObject() == null || Constraint.GetAttachedObject() == null)
			return;

		float speed = inputdata.Value.Float();
		float massLoad = 1;
		int numMasses = 0;
		if (Constraint.GetReferenceObject()!.IsMoveable()) {
			massLoad = Constraint.GetReferenceObject()!.GetMass();
			numMasses++;
			Constraint.GetReferenceObject()!.Wake();
		}
		if (Constraint.GetAttachedObject()!.IsMoveable()) {
			massLoad += Constraint.GetAttachedObject()!.GetMass();
			numMasses++;
			Constraint.GetAttachedObject()!.Wake();
		}
		if (numMasses > 0)
			massLoad /= (float)numMasses;
		float loadscale = SystemLoadScale != 0 ? SystemLoadScale : 1;
		Constraint.SetLinearMotor(speed, speed * loadscale * massLoad * (1.0f / (float)TICK_INTERVAL));
	}

	public override void DrawDebugGeometryOverlays() {
		if ((DebugOverlays & (DebugOverlayBits.BBox | DebugOverlayBits.Pivot | DebugOverlayBits.AbsBox)) != 0) {
			DebugOverlay.Box(GetAbsOrigin(), -new Vector3(8, 8, 8), new Vector3(8, 8, 8), 0, 255, 0, 0, 0);
			DebugOverlay.Box(AxisEnd, -new Vector3(4, 4, 4), new Vector3(4, 4, 4), 0, 0, 255, 0, 0);
			DebugOverlay.Line(GetAbsOrigin(), AxisEnd, 255, 255, 0, false, 0);
		}
		base.DrawDebugGeometryOverlays();
	}

	public void NotifyVPhysicsStateChanged(IPhysicsObject physics, BaseEntity entity, bool awake) {
#if HL2_EPISODIC
		Assert(Constraint != null);
		if (Constraint == null)
			return;

		if (awake) {
			if (GetNextThink() == TICK_NEVER_THINK) {
				Vector3 axisDirection = AxisEnd - GetAbsOrigin();
				MathLib.VectorNormalize(ref axisDirection);
				Util.SnapDirectionToAxis(ref axisDirection);

				SoundInfo.StartThinking(this, VelocitySampler.GetRelativeVelocity(Constraint.GetAttachedObject(), Constraint.GetReferenceObject()), axisDirection);
				SetThink(SoundThink);
				SetNextThink(gpGlobals.CurTime + SoundInfo.GetThinkRate());
			}
		}
		else {
			if (GetNextThink() != TICK_NEVER_THINK && Constraint.GetAttachedObject()!.IsAsleep() && Constraint.GetReferenceObject()!.IsAsleep()) {
				SoundInfo.StopThinking(this);
				SetNextThink(TICK_NEVER_THINK);
			}
		}
#endif
	}

#if HL2_EPISODIC
	public override void OnConstraintSetup(ref HlConstraintInfo info) {
		BaseEntity? entity0 = info.Objects[0] != null ? (BaseEntity?)info.Objects[0]!.GetGameData() : null;
		if (entity0 != null && !info.Objects[0]!.IsStatic())
			WatchVPhysicsStateChanges(this, entity0);
		BaseEntity? entity1 = info.Objects[1] != null ? (BaseEntity?)info.Objects[1]!.GetGameData() : null;
		if (entity1 != null && !info.Objects[1]!.IsStatic())
			WatchVPhysicsStateChanges(this, entity1);
		base.OnConstraintSetup(ref info);
	}
#endif

	protected override IPhysicsConstraint? CreateConstraint(IPhysicsConstraintGroup? group, in HlConstraintInfo info) {
		ConstraintSlidingParams sliding = default;
		sliding.Defaults();
		GetBreakParams(ref sliding.Constraint, info);
		sliding.Constraint.Strength = 1.0f;

		Vector3 axisDirection = AxisEnd - GetAbsOrigin();
#if GMOD_DLL
		if (axisDirection.Length() < 1.0f) {
			axisDirection = new(0, 0, 1);
			Msg("GMOD: Forcing sane values for slider constraint!\n");
		}
		MathLib.VectorNormalize(ref axisDirection);
#else
		MathLib.VectorNormalize(ref axisDirection);
		Util.SnapDirectionToAxis(ref axisDirection);
#endif

		sliding.InitWithCurrentObjectState(info.Objects[0]!, info.Objects[1]!, axisDirection);
		sliding.Friction = SlideFriction;
		if ((SpawnFlags & SF_SLIDE_LIMIT_ENDS) != 0) {
			info.Objects[1]!.GetPosition(out Vector3 position, out _);

			sliding.LimitMin = Vector3.Dot(axisDirection, GetAbsOrigin());
			sliding.LimitMax = Vector3.Dot(axisDirection, AxisEnd);
			if (sliding.LimitMax < sliding.LimitMin)
				(sliding.LimitMin, sliding.LimitMax) = (sliding.LimitMax, sliding.LimitMin);

			float limit = Vector3.Dot(position, axisDirection);
			if (limit < sliding.LimitMin)
				sliding.LimitMin = limit;
			else if (limit > sliding.LimitMax)
				sliding.LimitMax = limit;
			sliding.LimitMin -= limit;
			sliding.LimitMax -= limit;
		}

		return physenv.CreateSlidingConstraint(info.Objects[0]!, info.Objects[1]!, group!, sliding);
	}

#if HL2_EPISODIC
	void SoundThink() {
		Assert(Constraint != null);
		if (Constraint == null)
			return;

		IPhysicsObject? attached = Constraint.GetAttachedObject(), reference = Constraint.GetReferenceObject();
		Assert(attached != null && reference != null);
		if (attached != null && reference != null) {
			Vector3 relativeVel = VelocitySampler.GetRelativeVelocity(attached, reference);

			Vector3 axisDirection = AxisEnd - GetAbsOrigin();
			relativeVel = AxisEnd * Vector3.Dot(relativeVel, AxisEnd) / Vector3.Dot(AxisEnd, AxisEnd);

			SoundInfo.OnThink(this, relativeVel);

			SetNextThink(gpGlobals.CurTime + SoundInfo.GetThinkRate());
		}
	}

	public override void Activate() {
		base.Activate();

		if (Constraint == null)
			return;

		SoundInfo.OnActivate(this);

		Vector3 axisDirection = AxisEnd - GetAbsOrigin();
		MathLib.VectorNormalize(ref axisDirection);
		Util.SnapDirectionToAxis(ref axisDirection);
		SoundInfo.StartThinking(this, VelocitySampler.GetRelativeVelocity(Constraint.GetAttachedObject(), Constraint.GetReferenceObject()), axisDirection);

		SetThink(SoundThink);
		SetNextThink(gpGlobals.CurTime + SoundInfo.GetThinkRate());
	}

	public override void Precache() {
		SoundInfo.OnPrecache(this);
	}
#endif
}

[LinkEntityToClass("phys_constraint")]
public class PhysFixed : PhysConstraint
{
	protected override IPhysicsConstraint? CreateConstraint(IPhysicsConstraintGroup? group, in HlConstraintInfo info) {
		ConstraintFixedParams fixedParams = default;
		fixedParams.Defaults();
		fixedParams.InitWithCurrentObjectState(info.Objects[0]!, info.Objects[1]!);
		GetBreakParams(ref fixedParams.Constraint, info);

		if (info.Objects[0] == g_PhysWorldObject && info.Objects[1] != null)
			PhysSetGameFlags(info.Objects[1]!, PhysicsFlags.ConstraintStatic);

		return physenv.CreateFixedConstraint(info.Objects[0]!, info.Objects[1]!, group!, fixedParams);
	}

	public void MoveToRefPosition() {
		if (Constraint != null) {
			Constraint.GetConstraintTransform(out Matrix3x4 xformRef, out _);
			IPhysicsObject? obj = Constraint.GetReferenceObject();
			if (obj != null && obj.IsMoveable()) {
				MathLib.MatrixPosition(xformRef, out Vector3 pos);
				obj.LocalToWorld(out Vector3 posWorld, pos);
				SetAbsOrigin(posWorld);
			}
		}
	}

	public override int DrawDebugTextOverlays() {
		if ((DebugOverlays & DebugOverlayBits.Text) != 0)
			MoveToRefPosition();
		return base.DrawDebugTextOverlays();
	}

	public override void DrawDebugGeometryOverlays() {
		if ((DebugOverlays & (DebugOverlayBits.BBox | DebugOverlayBits.Pivot | DebugOverlayBits.AbsBox)) != 0)
			MoveToRefPosition();
		base.DrawDebugGeometryOverlays();
	}
}

[LinkEntityToClass("phys_pulleyconstraint")]
public class PhysPulley : PhysConstraint
{
	public static readonly new DataMap DataDesc = new(typeof(PhysPulley), PhysConstraint.DataDesc, [
		DEFINE<PhysPulley>.KEYFIELD(nameof(Position2), FieldType.PositionVector, "position2"),
		DEFINE<PhysPulley>.AUTO_ARRAY(nameof(Offset), FieldType.Vector),
		DEFINE<PhysPulley>.KEYFIELD(nameof(AddLength), FieldType.Float, "addlength"),
		DEFINE<PhysPulley>.KEYFIELD(nameof(GearRatio), FieldType.Float, "gearratio"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	Vector3 Position2;
	InlineArray2<Vector3> Offset;
	float AddLength;
	float GearRatio;

	public override void DrawDebugGeometryOverlays() {
		if ((DebugOverlays & (DebugOverlayBits.BBox | DebugOverlayBits.Pivot | DebugOverlayBits.AbsBox)) != 0) {
			Vector3 origin = GetAbsOrigin();
			Vector3 refPos = origin, attachPos = origin;
			IPhysicsObject? refObj = Constraint!.GetReferenceObject();
			if (refObj != null) {
				refObj.GetPositionMatrix(out Matrix3x4 matrix);
				MathLib.VectorTransform(Offset[0], matrix, out refPos);
			}
			IPhysicsObject? attach = Constraint.GetAttachedObject();
			if (attach != null) {
				attach.GetPositionMatrix(out Matrix3x4 matrix);
				MathLib.VectorTransform(Offset[1], matrix, out attachPos);
			}
			DebugOverlay.Line(refPos, origin, 0, 255, 0, false, 0);
			DebugOverlay.Line(origin, Position2, 128, 128, 128, false, 0);
			DebugOverlay.Line(Position2, attachPos, 0, 255, 0, false, 0);
			DebugOverlay.Box(origin, -new Vector3(8, 8, 8), new Vector3(8, 8, 8), 128, 255, 128, 32, 0);
			DebugOverlay.Box(Position2, -new Vector3(8, 8, 8), new Vector3(8, 8, 8), 255, 128, 128, 32, 0);
		}
		base.DrawDebugGeometryOverlays();
	}

	protected override IPhysicsConstraint? CreateConstraint(IPhysicsConstraintGroup? group, in HlConstraintInfo info) {
		ConstraintPulleyParams pulley = default;
		pulley.Defaults();
		pulley.PulleyPosition[0] = GetAbsOrigin();
		pulley.PulleyPosition[1] = Position2;

		Matrix3x4 matrix;
		InlineArray2<Vector3> world = default;

		info.Objects[0]!.GetPositionMatrix(out matrix);
		MathLib.VectorTransform(info.AnchorPosition[0], matrix, out world[0]);
		info.Objects[1]!.GetPositionMatrix(out matrix);
		MathLib.VectorTransform(info.AnchorPosition[1], matrix, out world[1]);

		for (int i = 0; i < 2; i++) {
			pulley.ObjectPosition[i] = info.AnchorPosition[i];
			Offset[i] = info.AnchorPosition[i];
		}

		pulley.TotalLength = AddLength + (world[0] - pulley.PulleyPosition[0]).Length() + ((world[1] - pulley.PulleyPosition[1]).Length() * GearRatio);

		if (GearRatio != 0)
			pulley.GearRatio = GearRatio;
		GetBreakParams(ref pulley.Constraint, info);
		if ((SpawnFlags & SF_PULLEY_RIGID) != 0)
			pulley.IsRigid = true;

		return physenv.CreatePulleyConstraint(info.Objects[0]!, info.Objects[1]!, group!, pulley);
	}
}

[LinkEntityToClass("phys_lengthconstraint")]
public class PhysLength : PhysConstraint
{
	public static readonly new DataMap DataDesc = new(typeof(PhysLength), PhysConstraint.DataDesc, [
		DEFINE<PhysLength>.AUTO_ARRAY(nameof(Offset), FieldType.Vector),
		DEFINE<PhysLength>.KEYFIELD(nameof(AddLength), FieldType.Float, "addlength"),
		DEFINE<PhysLength>.KEYFIELD(nameof(MinLength), FieldType.Float, "minlength"),
		DEFINE<PhysLength>.KEYFIELD(nameof(Attach), FieldType.PositionVector, "attachpoint"),
#if GMOD_DLL
		DEFINE<PhysLength>.KEYFIELD(nameof(Length), FieldType.Float, "length"),
#else
		DEFINE<PhysLength>.FIELD(nameof(TotalLength), FieldType.Float),
#endif
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	InlineArray2<Vector3> Offset;
	Vector3 Attach;
	float AddLength;
	float MinLength;
	float TotalLength;
#if GMOD_DLL
	float Length;
#endif

	public override void DrawDebugGeometryOverlays() {
		if ((DebugOverlays & (DebugOverlayBits.BBox | DebugOverlayBits.Pivot | DebugOverlayBits.AbsBox)) != 0) {
			Vector3 origin = GetAbsOrigin();
			Vector3 refPos = origin, attachPos = origin;
			IPhysicsObject? refObj = Constraint!.GetReferenceObject();
			if (refObj != null) {
				refObj.GetPositionMatrix(out Matrix3x4 matrix);
				MathLib.VectorTransform(Offset[0], matrix, out refPos);
			}
			IPhysicsObject? attach = Constraint.GetAttachedObject();
			if (attach != null) {
				attach.GetPositionMatrix(out Matrix3x4 matrix);
				MathLib.VectorTransform(Offset[1], matrix, out attachPos);
			}
			Vector3 dir = attachPos - refPos;

			float len = MathLib.VectorNormalize(ref dir);
			if (len > TotalLength) {
				Vector3 mid = refPos + dir * TotalLength;
				DebugOverlay.Line(refPos, mid, 0, 255, 0, false, 0);
				DebugOverlay.Line(mid, attachPos, 255, 0, 0, false, 0);
			}
			else
				DebugOverlay.Line(refPos, attachPos, 0, 255, 0, false, 0);
		}
		base.DrawDebugGeometryOverlays();
	}

	protected override IPhysicsConstraint? CreateConstraint(IPhysicsConstraintGroup? group, in HlConstraintInfo info) {
		ConstraintLengthParams length = default;
		length.Defaults();
		InlineArray2<Vector3> position = default;
		position[0] = GetAbsOrigin();
		position[1] = Attach;
		int index = info.Swapped ? 1 : 0;
		length.InitWorldspace(info.Objects[0]!, info.Objects[1]!, position[index], position[index == 0 ? 1 : 0]);
#if GMOD_DLL
		if (Length <= 0)
			length.TotalLength += AddLength;
		else
			length.TotalLength = Length;
		if (length.TotalLength <= 0.1f)
			length.TotalLength = 0.1f;
#else
		length.TotalLength += AddLength;
#endif
		length.MinLength = MinLength;
		TotalLength = length.TotalLength;
		if (HasSpawnFlags(SF_LENGTH_RIGID))
			length.MinLength = length.TotalLength;

		for (int i = 0; i < 2; i++)
			Offset[i] = length.ObjectPosition[i];
		GetBreakParams(ref length.Constraint, info);

		return physenv.CreateLengthConstraint(info.Objects[0]!, info.Objects[1]!, group!, length);
	}
}

[LinkEntityToClass("phys_ragdollconstraint")]
public class RagdollConstraint : PhysConstraint
{
	public static readonly new DataMap DataDesc = new(typeof(RagdollConstraint), PhysConstraint.DataDesc, [
		DEFINE<RagdollConstraint>.KEYFIELD(nameof(XMin), FieldType.Float, "xmin"),
		DEFINE<RagdollConstraint>.KEYFIELD(nameof(XMax), FieldType.Float, "xmax"),
		DEFINE<RagdollConstraint>.KEYFIELD(nameof(YMin), FieldType.Float, "ymin"),
		DEFINE<RagdollConstraint>.KEYFIELD(nameof(YMax), FieldType.Float, "ymax"),
		DEFINE<RagdollConstraint>.KEYFIELD(nameof(ZMin), FieldType.Float, "zmin"),
		DEFINE<RagdollConstraint>.KEYFIELD(nameof(ZMax), FieldType.Float, "zmax"),
		DEFINE<RagdollConstraint>.KEYFIELD(nameof(XFriction), FieldType.Float, "xfriction"),
		DEFINE<RagdollConstraint>.KEYFIELD(nameof(YFriction), FieldType.Float, "yfriction"),
		DEFINE<RagdollConstraint>.KEYFIELD(nameof(ZFriction), FieldType.Float, "zfriction"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	float XMin;
	float XMax;
	float YMin;
	float YMax;
	float ZMin;
	float ZMax;

	float XFriction;
	float YFriction;
	float ZFriction;

	protected override IPhysicsConstraint? CreateConstraint(IPhysicsConstraintGroup? group, in HlConstraintInfo info) {
		ConstraintRagdollParams ragdoll = default;
		ragdoll.Defaults();

		info.Objects[0]!.GetPositionMatrix(out Matrix3x4 entityToWorld);
		MathLib.MatrixInvert(entityToWorld, out Matrix3x4 worldToEntity);
		MathLib.ConcatTransforms(worldToEntity, EntityToWorldTransform(), out ragdoll.ConstraintToReference);

		info.Objects[1]!.GetPositionMatrix(out entityToWorld);
		MathLib.MatrixInvert(entityToWorld, out worldToEntity);
		MathLib.ConcatTransforms(worldToEntity, EntityToWorldTransform(), out ragdoll.ConstraintToAttached);

		ragdoll.OnlyAngularLimits = HasSpawnFlags(SF_RAGDOLL_FREEMOVEMENT) ? true : false;

		ragdoll.Axes[0].SetAxisFriction(XMin, XMax, XFriction);
		ragdoll.Axes[1].SetAxisFriction(YMin, YMax, YFriction);
		ragdoll.Axes[2].SetAxisFriction(ZMin, ZMax, ZFriction);

		if (HasSpawnFlags(SF_CONSTRAINT_START_INACTIVE))
			ragdoll.IsActive = false;
		return physenv.CreateRagdollConstraint(info.Objects[0]!, info.Objects[1]!, group!, ragdoll);
	}
}

public class PhysConstraintEvents : IPhysicsConstraintEvent
{
	public static readonly PhysConstraintEvents constraintevents = new();
	public static readonly IPhysicsConstraintEvent g_pConstraintEvents = constraintevents;

	public void ConstraintBroken(IPhysicsConstraint constraint) {
		BaseEntity? entity = (BaseEntity?)constraint.GetGameData();
		if (entity != null) {
			if (entity is IPhysicsConstraintEvent constraintEvent)
				constraintEvent.ConstraintBroken(constraint);
			else {
				Variant_t emptyVariant = default;
				entity.AcceptInput("ConstraintBroken", null, null, emptyVariant, 0);
			}
		}
	}
}

#if HL2_EPISODIC
public struct VelocitySampler
{
	public readonly float GetSampleRate() => IdealSampleRate;

	public void BeginSampling(in Vector3 relativeVelocity) => AddSample(relativeVelocity);

	public void AddSample(in Vector3 relativeVelocity) {
		PrevSample = relativeVelocity;
		PrevSampleTime = (float)gpGlobals.CurTime;
	}

	public readonly int HasReversed(in Vector3 relativeVelocity, ReadOnlySpan<float> thresholdAcceleration, ushort numThresholds) {
		float vDot = Vector3.Dot(relativeVelocity, PrevSample);
		if (vDot <= 0) {
			Vector3 accel = relativeVelocity - PrevSample;

			float prevSampleLength = PrevSample.Length();
			float projection = 0;
			if (prevSampleLength != 0)
				projection = -(Vector3.Dot(accel, PrevSample) / prevSampleLength) / ((float)gpGlobals.CurTime - PrevSampleTime);
			else
				projection = accel.Length() / ((float)gpGlobals.CurTime - PrevSampleTime);

			if (g_debug_constraint_sounds.GetBool())
				Msg($"Reversal accel is {projection:F6}/{thresholdAcceleration[0]:F6}\n");

			int retval;
			for (retval = numThresholds - 1; retval >= 0; --retval) {
				if (projection > thresholdAcceleration[retval])
					break;
			}

			return retval;
		}
		else
			return -1;
	}

	public void Initialize(float samplerate) {
		IdealSampleRate = samplerate;
	}

	public static Vector3 GetRelativeVelocity(IPhysicsObject? obj, IPhysicsObject? referenceFrame) {
		if (obj == null || referenceFrame == null)
			return vec3_origin;

		obj.GetImplicitVelocity(out Vector3 childVelocity, out _);
		referenceFrame!.GetImplicitVelocity(out Vector3 parentVelocity, out _);

		return childVelocity - parentVelocity;
	}

	public static Vector3 GetRelativeAngularVelocity(IPhysicsObject obj, IPhysicsObject? referenceFrame) {
		Assert(obj != null);

		if (referenceFrame != null) {
			obj.GetImplicitVelocity(out _, out Vector3 childVelocityLocal);
			obj.LocalToWorldVector(out Vector3 childVelocityWorld, childVelocityLocal);
			referenceFrame.GetImplicitVelocity(out _, out Vector3 parentVelocityLocal);
			obj.LocalToWorldVector(out Vector3 parentVelocityWorld, parentVelocityLocal);

			return childVelocityWorld - parentVelocityWorld;
		}
		else {
			obj.GetImplicitVelocity(out _, out Vector3 childVelocityLocal);
			obj.LocalToWorldVector(out Vector3 childVelocityWorld, childVelocityLocal);

			return childVelocityWorld;
		}
	}

	Vector3 PrevSample;
	float PrevSampleTime;

	float IdealSampleRate;
}

public struct SimpleConstraintSoundProfile
{
	public const int kMIN_THRESHOLD = 0;
	public const int kMIN_FULL = 1;
	public const int kHIGHWATER = 2;

	public InlineArray2<float> KeyPoints;

	public const int kREVERSAL_SOUND_ARRAY_SIZE = 3;

	public InlineArray3<float> ReversalSoundThresholds;

	public readonly float GetVolume(float inVel) {
		if (inVel <= KeyPoints[kMIN_THRESHOLD])
			return 0;
		else if (inVel >= KeyPoints[kMIN_FULL])
			return 1;
		else
			return (inVel - KeyPoints[kMIN_THRESHOLD]) / (KeyPoints[kMIN_FULL] - KeyPoints[kMIN_THRESHOLD]);
	}
}

public class ConstraintSoundInfo : IDisposable
{
	public void Dispose() => DeleteAllSounds();

	public VelocitySampler Sampler;
	public SimpleConstraintSoundProfile SoundProfile;

	public Vector3 ForwardAxis;

	public string? TravelSoundFwd, TravelSoundBack;
	public SoundPatch? TravelSound;
	public bool PlayTravelSound;

	public InlineArray3<string?> ReversalSounds;
	public bool PlayReversalSound;

	public float GetThinkRate() => 0.09f;

	static bool IsEmpty(string? str) => str == null || strlen(str) < 1;

	public void OnActivate(PhysConstraint outer) {
		TravelSound = null;
		Sampler.Initialize(GetThinkRate());

		ValidateInternals(outer);

		PlayTravelSound = !IsEmpty(TravelSoundFwd) || !IsEmpty(TravelSoundBack);
		PlayReversalSound = false;
		for (int i = 0; i < SimpleConstraintSoundProfile.kREVERSAL_SOUND_ARRAY_SIZE; ++i) {
			if (!IsEmpty(ReversalSounds[i])) {
				PlayReversalSound = true;
				break;
			}
		}
	}

	protected void ValidateInternals(PhysConstraint outer) {
		for (int i = 1; i < SimpleConstraintSoundProfile.kREVERSAL_SOUND_ARRAY_SIZE; ++i) {
			if (SoundProfile.ReversalSoundThresholds[i] < SoundProfile.ReversalSoundThresholds[i - 1]) {
				Warning($"Constraint reversal sounds for {outer.GetDebugName()} are out of order!");
				SoundProfile.ReversalSoundThresholds[i] = SoundProfile.ReversalSoundThresholds[i - 1];
				ReversalSounds[i] = ReversalSounds[i - 1];
			}
		}
	}

	public void OnPrecache(PhysConstraint outer) {
		BaseEntity.PrecacheScriptSound(TravelSoundFwd);
		BaseEntity.PrecacheScriptSound(TravelSoundBack);
		for (int i = 0; i < SimpleConstraintSoundProfile.kREVERSAL_SOUND_ARRAY_SIZE; ++i)
			BaseEntity.PrecacheScriptSound(ReversalSounds[i]);
	}

	public void OnThink(PhysConstraint outer, in Vector3 relativeVelocity) {
		int playReversal = Sampler.HasReversed(relativeVelocity, SoundProfile.ReversalSoundThresholds, SimpleConstraintSoundProfile.kREVERSAL_SOUND_ARRAY_SIZE);
		float relativeVelMag = relativeVelocity.Length();

		BaseEntity? childEntity = (BaseEntity?)outer.GetPhysConstraint()!.GetAttachedObject()!.GetGameData();
		if (childEntity == null)
			return;

		float soundVol = SoundProfile.GetVolume(relativeVelMag);

		if (g_debug_constraint_sounds.GetBool()) {
			childEntity.EntityText(0, $"Velocity: {relativeVelMag:F3}", Sampler.GetSampleRate());

			childEntity.EntityText(1, $"Sound volume: {soundVol:F3}", Sampler.GetSampleRate());

			if (playReversal >= 0)
				childEntity.EntityText(2, $"Reversal [{playReversal}]", Sampler.GetSampleRate());
		}

		if (PlayTravelSound) {
			if (soundVol > 0) {
				if (TravelSound != null)
					SoundEnvelopeController.GetController().SoundChangeVolume(TravelSound, soundVol, 0.1f);
				else {
					bool travellingForward = Vector3.Dot(relativeVelocity, ForwardAxis) > 0;

					SoundEnvelopeController controller = SoundEnvelopeController.GetController();
					PASAttenuationFilter filter = new(childEntity);
					TravelSound = controller.SoundCreate(filter, childEntity.EntIndex(), travellingForward ? TravelSoundFwd : TravelSoundBack);
					controller.Play(TravelSound, soundVol, 100);
				}
			}
			else {
				if (TravelSound != null) {
					SoundEnvelopeController.GetController().SoundDestroy(TravelSound);
					TravelSound = null;
				}
			}
		}

		if (PlayReversalSound && (playReversal >= 0))
			childEntity.EmitSound(ReversalSounds[playReversal]);

		Sampler.AddSample(relativeVelocity);
	}

	public void StartThinking(PhysConstraint outer, in Vector3 relativeVelocity, in Vector3 forwardVector) {
		ForwardAxis = forwardVector;
		Sampler.BeginSampling(relativeVelocity);
	}

	public void StopThinking(PhysConstraint outer) {
		DeleteAllSounds();
	}

	protected void DeleteAllSounds() {
		if (TravelSound != null) {
			SoundEnvelopeController.GetController().SoundDestroy(TravelSound);
			TravelSound = null;
		}
	}
}
#endif
