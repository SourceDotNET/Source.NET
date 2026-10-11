using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;

using DEFINE = Source.DEFINE<Game.Server.AI_PoliceGoal>;

namespace Game.Server;

[LinkEntityToClass("ai_goal_police")]
public class AI_PoliceGoal : BaseEntity
{
	public const int SF_POLICE_GOAL_KNOCKOUT_BEHIND = 1 << 1;
	public const int SF_POLICE_GOAL_DO_NOT_LEAVE_POST = 1 << 2;

	public static readonly new DataMap DataDesc = new(typeof(AI_PoliceGoal), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(Radius), FieldType.Float, "PoliceRadius"),
		DEFINE.KEYFIELD(nameof(TargetName), FieldType.String, "PoliceTarget"),

		DEFINE.FIELD(nameof(OverrideKnockOut), FieldType.Boolean),

		DEFINE.INPUTFUNC(FieldType.Void, "EnableKnockOut", nameof(InputEnableKnockOut), (INPUTFUNCPTR)((self, data) => ((AI_PoliceGoal)self).InputEnableKnockOut(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "DisableKnockOut", nameof(InputDisableKnockOut), (INPUTFUNCPTR)((self, data) => ((AI_PoliceGoal)self).InputDisableKnockOut(data))),

		DEFINE.OUTPUT(nameof(OnKnockOut), "OnKnockOut", eventFuncs),
		DEFINE.OUTPUT(nameof(OnFirstWarning), "OnFirstWarning", eventFuncs),
		DEFINE.OUTPUT(nameof(OnSecondWarning), "OnSecondWarning", eventFuncs),
		DEFINE.OUTPUT(nameof(OnLastWarning), "OnLastWarning", eventFuncs),
		DEFINE.OUTPUT(nameof(OnSupressingTarget), "OnSupressingTarget", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public AI_PoliceGoal() {
		Target.Set(null);
		OverrideKnockOut = false;
	}

	public float GetRadius() => Radius;

	public BaseEntity? GetTarget() {
		if (Target.Get() == null) {
			BaseEntity? target = gEntList.FindEntityByName(null, TargetName);

			if (target == null) {
				DevMsg($"Unable to find ai_goal_police target: {TargetName}\n");
				return null;
			}

			Target.Set(target);
		}

		return Target.Get();
	}

	public bool ShouldKnockOutTarget(in Vector3 targetPos, bool targetVisible) {
		if (OverrideKnockOut)
			return true;

		if (HasSpawnFlags(SF_POLICE_GOAL_KNOCKOUT_BEHIND) == false)
			return false;

		if (!targetVisible)
			return false;

		Vector3 targetDir = targetPos - GetAbsOrigin();
		MathLib.VectorNormalize(ref targetDir);

		MathLib.AngleVectors(GetAbsAngles(), out Vector3 facingDir);

		if (Vector3.Dot(facingDir, targetDir) < 0)
			return true;

		return false;
	}

	public void KnockOutTarget(BaseEntity? target) => OnKnockOut.FireOutput(target, this);

	public bool ShouldRemainAtPost() => HasSpawnFlags(SF_POLICE_GOAL_DO_NOT_LEAVE_POST);

	public void FireWarningLevelOutput(int level) {
		switch (level) {
			case 1:
				OnFirstWarning.FireOutput(this, this);
				break;

			case 2:
				OnSecondWarning.FireOutput(this, this);
				break;

			case 3:
				OnLastWarning.FireOutput(this, this);
				break;

			default:
				OnSupressingTarget.FireOutput(this, this);
				break;
		}
	}

	public void InputEnableKnockOut(InputData data) => OverrideKnockOut = true;

	public void InputDisableKnockOut(InputData data) => OverrideKnockOut = false;

	public float Radius;
	public EHANDLE Target = new();
	public string? TargetName;
	public bool OverrideKnockOut;

	readonly OutputEvent OnKnockOut = new();
	readonly OutputEvent OnFirstWarning = new();
	readonly OutputEvent OnSecondWarning = new();
	readonly OutputEvent OnLastWarning = new();
	readonly OutputEvent OnSupressingTarget = new();
}
