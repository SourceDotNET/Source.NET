using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

#if GMOD_DLL
using Game.Server.GarrysMod;
#endif

using DEFINE = Source.DEFINE<Game.Server.PhysImpact>;

namespace Game.Server;

[LinkEntityToClass("func_physbox")]
[NetworkName("CPhysBox")]
public partial class PhysBox : Breakable
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

	[Flags]
	public enum SF
	{
		EnableOnPhyscannon = 0x20000,
	}

}

[LinkEntityToClass("phys_spring")]
public class PhysicsSpring : BaseEntity
{
	public const int SF_SPRING_ONLYSTRETCH = 1;

	public static readonly new DataMap DataDesc = new(typeof(PhysicsSpring), BaseEntity.DataDesc, [
		DEFINE<PhysicsSpring>.KEYFIELD(nameof(TempConstant), FieldType.Float, "constant"),
		DEFINE<PhysicsSpring>.KEYFIELD(nameof(TempLength), FieldType.Float, "length"),
		DEFINE<PhysicsSpring>.KEYFIELD(nameof(TempDamping), FieldType.Float, "damping"),
		DEFINE<PhysicsSpring>.KEYFIELD(nameof(TempRelativeDamping), FieldType.Float, "relativedamping"),

		DEFINE<PhysicsSpring>.KEYFIELD(nameof(NameAttachStart), FieldType.String, "attach1"),
		DEFINE<PhysicsSpring>.KEYFIELD(nameof(NameAttachEnd), FieldType.String, "attach2"),

		DEFINE<PhysicsSpring>.FIELD(nameof(Start), FieldType.PositionVector),
		DEFINE<PhysicsSpring>.KEYFIELD(nameof(End), FieldType.PositionVector, "springaxis"),
		DEFINE<PhysicsSpring>.FIELD(nameof(IsLocal), FieldType.Boolean),

		DEFINE<PhysicsSpring>.INPUTFUNC(FieldType.Float, "SetSpringConstant", nameof(InputSetSpringConstant), (INPUTFUNCPTR)((self, data) => ((PhysicsSpring)self).InputSetSpringConstant(data))),
		DEFINE<PhysicsSpring>.INPUTFUNC(FieldType.Float, "SetSpringLength", nameof(InputSetSpringLength), (INPUTFUNCPTR)((self, data) => ((PhysicsSpring)self).InputSetSpringLength(data))),
		DEFINE<PhysicsSpring>.INPUTFUNC(FieldType.Float, "SetSpringDamping", nameof(InputSetSpringDamping), (INPUTFUNCPTR)((self, data) => ((PhysicsSpring)self).InputSetSpringDamping(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	IPhysicsSpring? Spring;
	bool IsLocal;

	float TempConstant;
	float TempLength;
	float TempDamping;
	float TempRelativeDamping;

	string? NameAttachStart;
	string? NameAttachEnd;
	Vector3 Start;
	Vector3 End;
	uint TeleportTick;
#if GMOD_DLL
	InlineArray2<IPhysicsObject?> PhysObjects;
#endif

	public PhysicsSpring() {
		Spring = null;
		TempConstant = 150;
		TempLength = 0;
		TempDamping = 2.0f;
		TempRelativeDamping = 0.01f;
		IsLocal = false;
		TeleportTick = uint.MaxValue;
#if GMOD_DLL
		PhysObjects[0] = null;
		PhysObjects[1] = null;
#endif
	}

	public override void Term() {
		if (Spring != null)
			physenv.DestroySpring(Spring);
		base.Term();
	}

	public IPhysicsObject? GetStartObject() => Spring?.GetStartObject();
	public IPhysicsObject? GetEndObject() => Spring?.GetEndObject();

#if GMOD_DLL
	public override void SetPhysObject(int index, IPhysicsObject? obj) => PhysObjects[index] = obj;
#endif

	public static bool GetSpringAttachments(BaseEntity entity, Span<BaseEntity?> attachOut, Span<IPhysicsObject?> attachVPhysics) {
		if (entity is PhysicsSpring springEntity) {
			IPhysicsObject? refObj = springEntity.GetStartObject();
			attachOut[0] = refObj != null ? (BaseEntity?)refObj.GetGameData() : null;
			attachVPhysics[0] = refObj;
			IPhysicsObject? attach = springEntity.GetEndObject();
			attachOut[1] = attach != null ? (BaseEntity?)attach.GetGameData() : null;
			attachVPhysics[1] = attach;
			return true;
		}
		return false;
	}

	void InputSetSpringConstant(in InputData inputdata) {
#if GMOD_DLL
		TempConstant = Math.Clamp(inputdata.Value.Float(), 0.0f, 50000.0f);
		Spring?.SetSpringConstant(TempConstant);
#else
		TempConstant = inputdata.Value.Float();
		Spring!.SetSpringConstant(inputdata.Value.Float());
#endif
	}

	void InputSetSpringDamping(in InputData inputdata) {
#if GMOD_DLL
		TempDamping = Math.Clamp(inputdata.Value.Float(), 0.0f, 10000.0f);
		Spring?.SetSpringDamping(TempDamping);
#else
		TempDamping = inputdata.Value.Float();
		Spring!.SetSpringDamping(inputdata.Value.Float());
#endif
	}

	void InputSetSpringLength(in InputData inputdata) {
#if GMOD_DLL
		TempLength = Math.Clamp(inputdata.Value.Float(), 0.0f, 4096.0f);
		Spring?.SetSpringLength(TempLength);
#else
		TempLength = inputdata.Value.Float();
		Spring!.SetSpringLength(inputdata.Value.Float());
#endif
	}

	public override int DrawDebugTextOverlays() {
		int text_offset = base.DrawDebugTextOverlays();

		if ((DebugOverlays & DebugOverlayBits.Text) != 0) {
			EntityText(text_offset, $"Constant: {TempConstant:F2}", 0);
			text_offset++;

			EntityText(text_offset, $"Length: {TempLength:F2}", 0);
			text_offset++;

			EntityText(text_offset, $"Damping: {TempDamping:F2}", 0);
			text_offset++;
		}
		return text_offset;
	}

	public override void DrawDebugGeometryOverlays() {
		if (Spring == null)
			return;

		if ((DebugOverlays & DebugOverlayBits.BBox) != 0) {
			Spring.GetEndpoints(out Vector3 startPos, out Vector3 endPos);

			Vector3 springDir = endPos - startPos;
			MathLib.VectorNormalize(ref springDir);

			Vector3 length = startPos + (springDir * TempLength);

			DebugOverlay.Line(startPos, length, 0, 0, 255, false, 0);
			DebugOverlay.Line(length, endPos, 255, 0, 0, false, 0);
		}
		base.DrawDebugGeometryOverlays();
	}

	public static bool PointIsNearer(IPhysicsObject object1, in Vector3 point1, in Vector3 point2) {
		object1.GetPosition(out Vector3 center, out _);

		float dist1 = (center - point1).LengthSquared();
		float dist2 = (center - point2).LengthSquared();

		if (dist1 < dist2)
			return true;

		return false;
	}

	void GetSpringObjectConnections(string? nameStart, string? nameEnd, out IPhysicsObject? start, out IPhysicsObject? end) {
		IPhysicsObject? startObject = PhysicsHook.FindPhysicsObjectByName(nameStart, this);
		IPhysicsObject? endObject = PhysicsHook.FindPhysicsObjectByName(nameEnd, this);

#if GMOD_DLL
		if (PhysObjects[0] != null || PhysObjects[1] != null) {
			if (!LuaPhysObj.IsPhysicsObjectValid(PhysObjects[0]))
				PhysObjects[0] = null;
			if (!LuaPhysObj.IsPhysicsObjectValid(PhysObjects[1]))
				PhysObjects[1] = null;
		}
		if (startObject == null)
			startObject = PhysObjects[0];
		if (endObject == null)
			endObject = PhysObjects[1];
#endif

		if (startObject == null)
			startObject = g_PhysWorldObject;
		else if (endObject == null) {
			endObject = startObject;
			startObject = g_PhysWorldObject;
		}
		else {
			BaseEntity? entity0 = (BaseEntity?)startObject.GetGameData();
			if (entity0 != null)
				g_pNotify.AddEntity(this, entity0);

			BaseEntity? entity1 = (BaseEntity?)endObject.GetGameData();
			if (entity1 != null)
				g_pNotify.AddEntity(this, entity1);
		}

		start = startObject;
		end = endObject;
	}

	public override void Activate() {
		base.Activate();

		if (Spring == null) {
			GetSpringObjectConnections(NameAttachStart, NameAttachEnd, out IPhysicsObject? start, out IPhysicsObject? end);

			if ((start == null || end == null) || (start == end)) {
				DevMsg($"ERROR: Can't init spring {GetDebugName()} from \"{NameAttachStart}\" to \"{NameAttachEnd}\"\n");
				Util.Remove(this);
				return;
			}

#if !GMOD_DLL
			if (!PointIsNearer(end, End, Start))
				(Start, End) = (End, Start);
#endif

			SpringParams spring = default;
#if GMOD_DLL
			spring.Constant = Math.Clamp(TempConstant, 0.0f, 50000.0f);
			spring.Damping = Math.Clamp(TempDamping, 0.0f, 10000.0f);
			spring.NaturalLength = TempLength;
			spring.RelativeDamping = Math.Clamp(TempRelativeDamping, 0.0f, 1000.0f);
#else
			spring.Constant = TempConstant;
			spring.Damping = TempDamping;
			spring.NaturalLength = TempLength;
			spring.RelativeDamping = TempRelativeDamping;
#endif
			spring.StartPosition = Start;
			spring.EndPosition = End;
			spring.UseLocalPositions = false;
			spring.OnlyStretch = HasSpawnFlags(SF_SPRING_ONLYSTRETCH);
			Spring = physenv.CreateSpring(start, end, ref spring);
		}
	}

	public override void Spawn() {
		SetSolid(SolidType.None);
		Start = GetAbsOrigin();
		if (TempLength <= 0)
			TempLength = (End - Start).Length();
	}

	public override void NotifySystemEvent(BaseEntity notify, NotifySystemEvent eventType, in NotifySystemEventParams parms) {
		if (eventType != Server.NotifySystemEvent.Teleport || (uint)gpGlobals.TickCount == TeleportTick)
			return;

		TeleportTick = (uint)gpGlobals.TickCount;
		PhysTeleportConstrainedEntity(notify, Spring!.GetStartObject(), Spring.GetEndObject(), parms.Teleport.PrevOrigin, parms.Teleport.PrevAngles, parms.Teleport.PhysicsRotate);
	}
}

[LinkEntityToClass("env_physimpact")]
public class PhysImpact : PointEntity
{
	public const int bitsPHYSIMPACT_NOFALLOFF = 0x00000001;
	public const int bitsPHYSIMPACT_INFINITE_LENGTH = 0x00000002;
	public const int bitsPHYSIMPACT_IGNORE_MASS = 0x00000004;
	public const int bitsPHYSIMPACT_IGNORE_NORMAL = 0x00000008;

	public const float DEFAULT_EXPLODE_DISTANCE = 256;

	public static readonly ConVar debug_physimpact = new("debug_physimpact", "0");

	public static readonly new DataMap DataDesc = new(typeof(PhysImpact), PointEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(Damage), FieldType.Float, "magnitude"),
		DEFINE.KEYFIELD(nameof(Distance), FieldType.Float, "distance"),
		DEFINE.KEYFIELD(nameof(DirectionEntityName), FieldType.String, "directionentityname"),

		DEFINE.INPUTFUNC(FieldType.Void, "Impact", nameof(InputImpact), (INPUTFUNCPTR)((self, data) => ((PhysImpact)self).InputImpact(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	float Damage;
	float Distance;
	string? DirectionEntityName;

	public override void Activate() {
		base.Activate();
	}

	public override void Spawn() {
		SetMoveType(Source.MoveType.None);
		SetSolid(SolidType.None);
		SetModelName(null);

		if (Distance == 0)
			Distance = DEFAULT_EXPLODE_DISTANCE;
	}

	void PointAtEntity() {
		if (string.IsNullOrEmpty(DirectionEntityName))
			return;

		Util.PointAtNamedEntity(this, DirectionEntityName);
	}

	public void InputImpact(InputData inputdata) {
		if (!string.IsNullOrEmpty(DirectionEntityName))
			PointAtEntity();

		MathLib.AngleVectors(GetAbsAngles(), out Vector3 dir);

		float dist = HasSpawnFlags(bitsPHYSIMPACT_INFINITE_LENGTH) ? MAX_TRACE_LENGTH : Distance;
		Vector3 start = GetAbsOrigin();
		Vector3 end = start + (dir * dist);

		Util.TraceLine(start, end, Mask.Shot, this, Source.CollisionGroup.None, out Trace trace);
		if (trace.StartSolid) {
			Vector3 startOffset = start + (dir * 0.1f);
			Util.TraceLine(startOffset, end, Mask.Shot, this, Source.CollisionGroup.None, out trace);
		}

		if (trace.Fraction != 1.0) {
			if (trace.StartSolid)
				trace.Plane.Normal = -dir;
			BaseEntity? ent = (BaseEntity?)trace.Ent;

			IPhysicsObject? physics = ent?.VPhysicsGetObject();
			if (ent != null && physics != null) {
				TakeDamageInfo info = new();
				info.SetAttacker(this);
				info.SetInflictor(this);
				info.SetDamage(0);
				info.SetDamageForce(vec3_origin);
				info.SetDamageType(DamageType.Generic);

				ent.DispatchTraceAttack(info, dir, ref trace);
				ApplyMultiDamage();

				float damage = HasSpawnFlags(bitsPHYSIMPACT_NOFALLOFF | bitsPHYSIMPACT_INFINITE_LENGTH) ?
									Damage : (Damage * (1.0f - trace.Fraction));

				if (HasSpawnFlags(bitsPHYSIMPACT_IGNORE_MASS))
					damage *= physics.GetMass();

				if (!HasSpawnFlags(bitsPHYSIMPACT_IGNORE_NORMAL))
					dir = -trace.Plane.Normal;

				physics.ApplyForceOffset(damage * dir * phys_pushscale.GetFloat(), trace.EndPos);
			}
		}
	}
}
