using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

using DEFINE = Source.DEFINE<PointTeleport>;

[LinkEntityToClass("point_teleport")]
public class PointTeleport : BaseEntity
{
	[Flags]
	public enum SF
	{
		ToSpawnPos = 0x00000001,
	}

	Vector3 SaveOrigin;
	QAngle SaveAngles;

	public static readonly new DataMap DataDesc = new(typeof(PointTeleport), BaseEntity.DataDesc, [
		DEFINE.FIELD(nameof(SaveOrigin), FieldType.Vector),
		DEFINE.FIELD(nameof(SaveAngles), FieldType.Vector),

		DEFINE.INPUTFUNC(FieldType.Void, "Teleport", nameof(InputTeleport), (INPUTFUNCPTR)((self, data) => ((PointTeleport)self).InputTeleport(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	bool EntityMayTeleport(BaseEntity target) {
		if (target.GetMoveParent() != null) {
			BaseCombatCharacter? bcc = target as BaseCombatCharacter;
			if (bcc == null || !(bcc is BasePlayer player && player.IsInAVehicle()))
				return false;
		}

		return true;
	}

	public override void Activate() {
		SaveOrigin = GetAbsOrigin();
		SaveAngles = GetAbsAngles();

		if (HasSpawnFlags(SF.ToSpawnPos)) {
			BaseEntity? target = gEntList.FindEntityByName(null, Target);
			if (target != null) {
				if (EntityMayTeleport(target)) {
					SaveOrigin = target.GetAbsOrigin();
					SaveAngles = target.GetAbsAngles();
				}
				else {
					Warning($"ERROR: ({GetDebugName()}) can't teleport object ({target.GetDebugName()}) as it has a parent ({target.GetMoveParent()!.GetDebugName()})!\n");
					base.Activate();
					return;
				}
			}
			else {
				Warning($"ERROR: ({GetDebugName()}) target '{Target}' not found. Deleting.\n");
				Util.Remove(this);
				return;
			}
		}

		base.Activate();
	}

	public void InputTeleport(InputData inputdata) {
		BaseEntity? target = gEntList.FindEntityByName(null, Target, this, inputdata.Activator, inputdata.Caller);
		if (target == null)
			return;

		if (!EntityMayTeleport(target)) {
			Warning($"ERROR: ({GetDebugName()}) can't teleport object ({target.GetDebugName()}) as it has a parent ({target.GetMoveParent()!.GetDebugName()})!\n");
			return;
		}

		target.Teleport(SaveOrigin, SaveAngles, null);
	}
}
