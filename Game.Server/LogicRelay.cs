using Game.Shared;

using Source;
using Source.Common;

namespace Game.Server;

using DEFINE = Source.DEFINE<LogicRelay>;

[LinkEntityToClass("logic_relay")]
public class LogicRelay : LogicalEntity
{
	[Flags]
	public enum SF
	{
		RemoveOnFire = 0x001,
		AllowFastRetrigger = 0x002,
	}

	public OutputEvent OnTrigger = new();
	public OutputEvent OnSpawn = new();

	bool Disabled;
	bool WaitForRefire;

	public static readonly new DataMap DataDesc = new(typeof(LogicRelay), BaseEntity.DataDesc, [
		DEFINE.FIELD(nameof(WaitForRefire), FieldType.Boolean),
		DEFINE.KEYFIELD(nameof(Disabled), FieldType.Boolean, "StartDisabled"),

		DEFINE.INPUTFUNC(FieldType.Void, "Enable", nameof(InputEnable), (INPUTFUNCPTR)((self, data) => ((LogicRelay)self).InputEnable(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "EnableRefire", nameof(InputEnableRefire), (INPUTFUNCPTR)((self, data) => ((LogicRelay)self).InputEnableRefire(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Disable", nameof(InputDisable), (INPUTFUNCPTR)((self, data) => ((LogicRelay)self).InputDisable(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Toggle", nameof(InputToggle), (INPUTFUNCPTR)((self, data) => ((LogicRelay)self).InputToggle(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Trigger", nameof(InputTrigger), (INPUTFUNCPTR)((self, data) => ((LogicRelay)self).InputTrigger(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "CancelPending", nameof(InputCancelPending), (INPUTFUNCPTR)((self, data) => ((LogicRelay)self).InputCancelPending(data))),

		DEFINE.OUTPUT(nameof(OnTrigger), "OnTrigger", eventFuncs),
		DEFINE.OUTPUT(nameof(OnSpawn), "OnSpawn", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void Activate() {
		base.Activate();

		if (OnSpawn.NumberOfElements() > 0)
			SetNextThink(gpGlobals.CurTime + 0.01f);
	}

	public override void Think() {
		OnSpawn.FireOutput(this, this);

		if (HasSpawnFlags(SF.RemoveOnFire))
			Util.Remove(this);
	}

	public void InputEnable(InputData inputdata) => Disabled = false;

	public void InputEnableRefire(InputData inputdata) => WaitForRefire = false;

	public void InputCancelPending(InputData inputdata) {
		g_EventQueue.CancelEvents(this);
		WaitForRefire = false;
	}

	public void InputDisable(InputData inputdata) => Disabled = true;

	public void InputToggle(InputData inputdata) => Disabled = !Disabled;

	public void InputTrigger(InputData inputdata) {
		if (!Disabled && !WaitForRefire) {
			OnTrigger.FireOutput(inputdata.Activator, this);

			if (HasSpawnFlags(SF.RemoveOnFire))
				Util.Remove(this);
			else if (!HasSpawnFlags(SF.AllowFastRetrigger)) {
				WaitForRefire = true;
				g_EventQueue.AddEvent(this, "EnableRefire", OnTrigger.GetMaxDelay() + 0.001f, this, this);
			}
		}
	}
}
