using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

using DEFINE = Source.DEFINE<Game.Server.PhysImpact>;

namespace Game.Server;

[LinkEntityToClass("func_physbox")]
[NetworkName("CPhysBox")]
public class PhysBox : Breakable
{
	public const int SF_PHYSBOX_ENABLE_ON_PHYSCANNON = 0x20000;

	public static readonly SendTable DT_PhysBox = new(DT_BaseEntity, []);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_PhysBox);
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
