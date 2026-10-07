using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Physics;

using System.Numerics;

namespace Game.Server;

/// <summary>
/// Compares a set of integer inputs to the one main input
/// Outputs true if they are all equivalant, false otherwise
/// </summary>
[LinkEntityToClass("logic_multicompare")]
public class LogicCompareInteger : LogicalEntity
{
	// outputs
	public readonly OutputEvent OnEqual = new();
	public readonly OutputEvent OnNotEqual = new();

	// data
	public int IntegerValue;
	public int ShouldCompareToValue;

	public readonly MultiInputVar AllIntCompares = new();

	public static readonly new DataMap DataDesc = new(typeof(LogicCompareInteger), LogicalEntity.DataDesc, [
		DEFINE<LogicCompareInteger>.OUTPUT(nameof(OnEqual), "OnEqual", eventFuncs),
		DEFINE<LogicCompareInteger>.OUTPUT(nameof(OnNotEqual), "OnNotEqual", eventFuncs),

		DEFINE<LogicCompareInteger>.KEYFIELD(nameof(IntegerValue), FieldType.Integer, "IntegerValue"),
		DEFINE<LogicCompareInteger>.KEYFIELD(nameof(ShouldCompareToValue), FieldType.Integer, "ShouldComparetoValue"),

		DEFINE<LogicCompareInteger>.FIELD(nameof(AllIntCompares), FieldType.Input),

		DEFINE<LogicCompareInteger>.INPUTFUNC(FieldType.Input, "InputValue", nameof(InputValue), (INPUTFUNCPTR)((self, data) => ((LogicCompareInteger)self).InputValue(data))),
		DEFINE<LogicCompareInteger>.INPUTFUNC(FieldType.Input, "CompareValues", nameof(InputCompareValues), (INPUTFUNCPTR)((self, data) => ((LogicCompareInteger)self).InputCompareValues(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	/// <summary>
	/// Adds to the list of compared values
	/// </summary>
	public void InputValue(InputData inputdata) {
		// make sure it's an int, if it can't be converted just throw it away
		if (!inputdata.Value.Convert(FieldType.Integer))
			return;

		// update the value list with the new value
		AllIntCompares.AddValue(inputdata.Value, inputdata.OutputID);

		// if we haven't already this frame, send a message to ourself to update and fire
		if (AllIntCompares.UpdatedThisFrame == 0) {
			// TODO: need to add this event with a lower priority, so it gets called after all inputs have arrived
			g_EventQueue.AddEvent(this, "CompareValues", 0, inputdata.Activator, this, inputdata.OutputID);
			AllIntCompares.UpdatedThisFrame = 1;
		}
	}

	/// <summary>
	/// Forces a recompare
	/// </summary>
	public void InputCompareValues(InputData inputdata) {
		AllIntCompares.UpdatedThisFrame = 0;

		// loop through all the values comparing them
		int value = IntegerValue;
		MultiInputVar.InputItem? input = AllIntCompares.InputList;

		if (ShouldCompareToValue == 0 && input != null)
			value = input.Value.Int();

		while (input != null) {
			if (input.Value.Int() != value) {
				// false
				OnNotEqual.FireOutput(inputdata.Activator, this);
				return;
			}

			input = input.Next;
		}

		// true! all values equal
		OnEqual.FireOutput(inputdata.Activator, this);
	}
}

/// <summary>
/// Timer entity. Fires an output at regular or random intervals.
/// </summary>
[LinkEntityToClass("logic_timer")]
public class TimerEntity : LogicalEntity
{
	//
	// Spawnflags and others constants.
	//
	const int SF_TIMER_UPDOWN = 1;
	const float LOGIC_TIMER_MIN_INTERVAL = 0.01f;

	// outputs
	public readonly OutputEvent OnTimer = new();
	public readonly OutputEvent OnTimerHigh = new();
	public readonly OutputEvent OnTimerLow = new();

	public int Disabled;
	public float RefireTime;
	public bool UpDownState;
	public int UseRandomTime;
	public float LowerRandomBound;
	public float UpperRandomBound;

	public static readonly new DataMap DataDesc = new(typeof(TimerEntity), LogicalEntity.DataDesc, [
		// Keys
		DEFINE<TimerEntity>.KEYFIELD(nameof(Disabled), FieldType.Integer, "StartDisabled"),
		DEFINE<TimerEntity>.KEYFIELD(nameof(RefireTime), FieldType.Float, "RefireTime"),

		DEFINE<TimerEntity>.FIELD(nameof(UpDownState), FieldType.Boolean),

		// Inputs
		DEFINE<TimerEntity>.INPUTFUNC(FieldType.Float, "RefireTime", nameof(InputRefireTime), (INPUTFUNCPTR)((self, data) => ((TimerEntity)self).InputRefireTime(data))),
		DEFINE<TimerEntity>.INPUTFUNC(FieldType.Void, "FireTimer", nameof(InputFireTimer), (INPUTFUNCPTR)((self, data) => ((TimerEntity)self).InputFireTimer(data))),
		DEFINE<TimerEntity>.INPUTFUNC(FieldType.Void, "Enable", nameof(InputEnable), (INPUTFUNCPTR)((self, data) => ((TimerEntity)self).InputEnable(data))),
		DEFINE<TimerEntity>.INPUTFUNC(FieldType.Void, "Disable", nameof(InputDisable), (INPUTFUNCPTR)((self, data) => ((TimerEntity)self).InputDisable(data))),
		DEFINE<TimerEntity>.INPUTFUNC(FieldType.Void, "Toggle", nameof(InputToggle), (INPUTFUNCPTR)((self, data) => ((TimerEntity)self).InputToggle(data))),
		DEFINE<TimerEntity>.INPUTFUNC(FieldType.Float, "AddToTimer", nameof(InputAddToTimer), (INPUTFUNCPTR)((self, data) => ((TimerEntity)self).InputAddToTimer(data))),
		DEFINE<TimerEntity>.INPUTFUNC(FieldType.Void, "ResetTimer", nameof(InputResetTimer), (INPUTFUNCPTR)((self, data) => ((TimerEntity)self).InputResetTimer(data))),
		DEFINE<TimerEntity>.INPUTFUNC(FieldType.Float, "SubtractFromTimer", nameof(InputSubtractFromTimer), (INPUTFUNCPTR)((self, data) => ((TimerEntity)self).InputSubtractFromTimer(data))),

		DEFINE<TimerEntity>.INPUT(nameof(UseRandomTime), FieldType.Integer, "UseRandomTime"),
		DEFINE<TimerEntity>.INPUT(nameof(LowerRandomBound), FieldType.Float, "LowerRandomBound"),
		DEFINE<TimerEntity>.INPUT(nameof(UpperRandomBound), FieldType.Float, "UpperRandomBound"),

		// Outputs
		DEFINE<TimerEntity>.OUTPUT(nameof(OnTimer), "OnTimer", eventFuncs),
		DEFINE<TimerEntity>.OUTPUT(nameof(OnTimerHigh), "OnTimerHigh", eventFuncs),
		DEFINE<TimerEntity>.OUTPUT(nameof(OnTimerLow), "OnTimerLow", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void Spawn() {
		if (UseRandomTime == 0 && (RefireTime < LOGIC_TIMER_MIN_INTERVAL))
			RefireTime = LOGIC_TIMER_MIN_INTERVAL;

		if (Disabled == 0 && (RefireTime > 0 || UseRandomTime != 0))
			Enable();
		else
			Disable();
	}

	public override void Think() => FireTimer();

	/// <summary>
	/// Sets the time the timerentity will next fire
	/// </summary>
	public void ResetTimer() {
		if (Disabled != 0)
			return;

		if (UseRandomTime != 0)
			RefireTime = RandomFloat(LowerRandomBound, UpperRandomBound);

		SetNextThink(gpGlobals.CurTime + RefireTime);
	}

	public void Enable() {
		Disabled = 0;
		ResetTimer();
	}

	public void Disable() {
		Disabled = 1;
		SetNextThink(TICK_NEVER_THINK);
	}

	public void Toggle() {
		if (Disabled != 0)
			Enable();
		else
			Disable();
	}

	public void FireTimer() {
		if (Disabled == 0) {
			//
			// Up/down timers alternate between two outputs.
			//
			if ((SpawnFlags & SF_TIMER_UPDOWN) != 0) {
				if (UpDownState)
					OnTimerHigh.FireOutput(this, this);
				else
					OnTimerLow.FireOutput(this, this);

				UpDownState = !UpDownState;
			}
			//
			// Regular timers only fire a single output.
			//
			else
				OnTimer.FireOutput(this, this);

			ResetTimer();
		}
	}

	public void InputEnable(InputData inputdata) => Enable();

	public void InputDisable(InputData inputdata) => Disable();

	public void InputToggle(InputData inputdata) => Toggle();

	public void InputFireTimer(InputData inputdata) => FireTimer();

	/// <summary>
	/// Changes the time interval between timer fires
	/// Resets the next firing to be time + newRefireTime
	/// </summary>
	/// <param name="inputdata">Float refire frequency in seconds.</param>
	public void InputRefireTime(InputData inputdata) {
		float refireInterval = inputdata.Value.Float();

		if (refireInterval < LOGIC_TIMER_MIN_INTERVAL)
			refireInterval = LOGIC_TIMER_MIN_INTERVAL;

		if (RefireTime != refireInterval) {
			RefireTime = refireInterval;
			ResetTimer();
		}
	}

	public void InputResetTimer(InputData inputdata) {
		// don't reset the timer if it isn't enabled
		if (Disabled != 0)
			return;

		ResetTimer();
	}

	/// <summary>
	/// Adds to the time interval if the timer is enabled
	/// </summary>
	/// <param name="inputdata">Float time to add in seconds</param>
	public void InputAddToTimer(InputData inputdata) {
		// don't add time if the timer isn't enabled
		if (Disabled != 0)
			return;

		// Add time to timer
		TimeUnit_t nextThink = GetNextThink();
		SetNextThink(nextThink += inputdata.Value.Float());
	}

	/// <summary>
	/// Subtract from the time interval if the timer is enabled
	/// </summary>
	/// <param name="inputdata">Float time to subtract in seconds</param>
	public void InputSubtractFromTimer(InputData inputdata) {
		// don't add time if the timer isn't enabled
		if (Disabled != 0)
			return;

		// Subtract time from the timer but don't let the timer go negative
		TimeUnit_t nextThink = GetNextThink();
		if ((nextThink - gpGlobals.CurTime) <= inputdata.Value.Float())
			SetNextThink(gpGlobals.CurTime);
		else
			SetNextThink(nextThink -= inputdata.Value.Float());
	}

	/// <summary>
	/// Draw any debug text overlays
	/// </summary>
	/// <returns>Current text offset from the top</returns>
	public override int DrawDebugTextOverlays() {
		int text_offset = base.DrawDebugTextOverlays();

		if ((DebugOverlays & DebugOverlayBits.Text) != 0) {
			// print refire time
			EntityText(text_offset, $"refire interval: {RefireTime:F2} sec", 0);
			text_offset++;

			// print seconds to next fire
			if (Disabled == 0) {
				TimeUnit_t nextThink = GetNextThink();
				EntityText(text_offset, $"      firing in: {nextThink - gpGlobals.CurTime:F2} sec", 0);
				text_offset++;
			}
		}
		return text_offset;
	}
}

/// <summary>
/// Computes a line between two entities
/// </summary>
[LinkEntityToClass("logic_lineto")]
public class LogicLineToEntity : LogicalEntity
{
	// outputs
	public readonly OutputVector Line = new();

	private string? SourceName;
	private EHANDLE StartEntity;
	private EHANDLE EndEntity;

	public static readonly new DataMap DataDesc = new(typeof(LogicLineToEntity), LogicalEntity.DataDesc, [
		// Keys
		// target is handled in the base class, stored in field Target
		DEFINE<LogicLineToEntity>.KEYFIELD(nameof(SourceName), FieldType.String, "source"),
		DEFINE<LogicLineToEntity>.FIELD(nameof(StartEntity), FieldType.EHandle),
		DEFINE<LogicLineToEntity>.FIELD(nameof(EndEntity), FieldType.EHandle),

		// Outputs
		DEFINE<LogicLineToEntity>.OUTPUT(nameof(Line), "Line", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	/// <summary>
	/// Find the entities
	/// </summary>
	public override void Activate() {
		base.Activate();

		if (Target != null) {
			EndEntity.Set(gEntList.FindEntityByName(null, Target));

			//
			// If we were given a bad measure target, just measure sound where we are.
			//
			if ((EndEntity.Get() == null) || (EndEntity.Get()!.Edict() == null)) {
				Warning("logic_lineto - Target not found or target with no origin!\n");
				EndEntity.Set(this);
			}
		}
		else
			EndEntity.Set(this);

		if (SourceName != null) {
			StartEntity.Set(gEntList.FindEntityByName(null, SourceName));

			//
			// If we were given a bad measure target, just measure sound where we are.
			//
			if ((StartEntity.Get() == null) || (StartEntity.Get()!.Edict() == null)) {
				Warning("logic_lineto - Source not found or source with no origin!\n");
				StartEntity.Set(this);
			}
		}
		else
			StartEntity.Set(this);
	}

	/// <summary>
	/// Find the entities
	/// </summary>
	public override void Spawn() => SetNextThink(gpGlobals.CurTime + 0.01f);

	/// <summary>
	/// Find the entities
	/// </summary>
	public override void Think() {
		BaseEntity? dest = EndEntity.Get();
		BaseEntity? src = StartEntity.Get();
		if (dest == null || src == null || dest.Edict() == null || src.Edict() == null) {
			// Can sleep for a long time, no more lines.
			Line.Set(vec3_origin, this, this);
			SetNextThink(gpGlobals.CurTime + 10);
			return;
		}

		Vector3 delta = dest.GetAbsOrigin() - src.GetAbsOrigin();
		Line.Set(delta, this, this);

		SetNextThink(gpGlobals.CurTime + 0.01f);
	}
}

/// <summary>
/// Remaps a given input range to an output range.
/// </summary>
[LinkEntityToClass("math_remap")]
public class MathRemap : LogicalEntity
{
	const int SF_MATH_REMAP_IGNORE_OUT_OF_RANGE = 1;
	const int SF_MATH_REMAP_CLAMP_OUTPUT_TO_RANGE = 2;

	// Keys
	public float InMin;
	public float InMax;
	public float Out1;     // Output value when input is InMin
	public float Out2;     // Output value when input is InMax

	public bool Enabled;

	// Outputs
	public readonly OutputFloat OutValue = new();

	public static readonly new DataMap DataDesc = new(typeof(MathRemap), LogicalEntity.DataDesc, [
		DEFINE<MathRemap>.INPUTFUNC(FieldType.Float, "InValue", nameof(InputValue), (INPUTFUNCPTR)((self, data) => ((MathRemap)self).InputValue(data))),

		DEFINE<MathRemap>.OUTPUT(nameof(OutValue), "OutValue", eventFuncs),

		DEFINE<MathRemap>.KEYFIELD(nameof(InMin), FieldType.Float, "in1"),
		DEFINE<MathRemap>.KEYFIELD(nameof(InMax), FieldType.Float, "in2"),
		DEFINE<MathRemap>.KEYFIELD(nameof(Out1), FieldType.Float, "out1"),
		DEFINE<MathRemap>.KEYFIELD(nameof(Out2), FieldType.Float, "out2"),

		DEFINE<MathRemap>.FIELD(nameof(Enabled), FieldType.Boolean),

		DEFINE<MathRemap>.INPUTFUNC(FieldType.Void, "Enable", nameof(InputEnable), (INPUTFUNCPTR)((self, data) => ((MathRemap)self).InputEnable(data))),
		DEFINE<MathRemap>.INPUTFUNC(FieldType.Void, "Disable", nameof(InputDisable), (INPUTFUNCPTR)((self, data) => ((MathRemap)self).InputDisable(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void Spawn() {
		//
		// Avoid a divide by zero in ValueChanged.
		//
		if (InMin == InMax) {
			InMin = 0;
			InMax = 1;
		}

		//
		// Make sure min and max are set properly relative to one another.
		//
		if (InMin > InMax)
			(InMin, InMax) = (InMax, InMin);

		Enabled = true;
	}

	public void InputEnable(InputData inputdata) => Enabled = true;

	public void InputDisable(InputData inputdata) => Enabled = false;

	/// <summary>
	/// Input handler that is called when the input value changes.
	/// </summary>
	public void InputValue(InputData inputdata) {
		float value = inputdata.Value.Float();

		//
		// Disallow out-of-range input values to avoid out-of-range output values.
		//
		float clampValue = Math.Clamp(value, InMin, InMax);

		if ((clampValue == value) || (SpawnFlags & SF_MATH_REMAP_IGNORE_OUT_OF_RANGE) == 0) {
			//
			// Remap the input value to the desired output range and update the output.
			//
			float remappedValue = Out1 + (((value - InMin) * (Out2 - Out1)) / (InMax - InMin));

			if ((SpawnFlags & SF_MATH_REMAP_CLAMP_OUTPUT_TO_RANGE) != 0)
				remappedValue = remappedValue < Out1 ? Out1 : remappedValue > Out2 ? Out2 : remappedValue;

			if (Enabled == true)
				OutValue.Set(remappedValue, inputdata.Activator, this);
		}
	}
}

/// <summary>
/// Remaps a given input range to an output range.
/// </summary>
[LinkEntityToClass("math_colorblend")]
public class MathColorBlend : LogicalEntity
{
	const int SF_COLOR_BLEND_IGNORE_OUT_OF_RANGE = 1;

	// Keys
	public float InMin;
	public float InMax;
	public Color OutColor1;        // Output color when input is InMin
	public Color OutColor2;        // Output color when input is InMax

	// Outputs
	public readonly OutputColor32 OutValue = new();

	public static readonly new DataMap DataDesc = new(typeof(MathColorBlend), LogicalEntity.DataDesc, [
		DEFINE<MathColorBlend>.INPUTFUNC(FieldType.Float, "InValue", nameof(InputValue), (INPUTFUNCPTR)((self, data) => ((MathColorBlend)self).InputValue(data))),

		DEFINE<MathColorBlend>.OUTPUT(nameof(OutValue), "OutColor", eventFuncs),

		DEFINE<MathColorBlend>.KEYFIELD(nameof(InMin), FieldType.Float, "inmin"),
		DEFINE<MathColorBlend>.KEYFIELD(nameof(InMax), FieldType.Float, "inmax"),
		DEFINE<MathColorBlend>.KEYFIELD(nameof(OutColor1), FieldType.Color32, "colormin"),
		DEFINE<MathColorBlend>.KEYFIELD(nameof(OutColor2), FieldType.Color32, "colormax"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void Spawn() {
		//
		// Avoid a divide by zero in ValueChanged.
		//
		if (InMin == InMax) {
			InMin = 0;
			InMax = 1;
		}

		//
		// Make sure min and max are set properly relative to one another.
		//
		if (InMin > InMax)
			(InMin, InMax) = (InMax, InMin);
	}

	/// <summary>
	/// Input handler that is called when the input value changes.
	/// </summary>
	public void InputValue(InputData inputdata) {
		float value = inputdata.Value.Float();

		//
		// Disallow out-of-range input values to avoid out-of-range output values.
		//
		float clampValue = Math.Clamp(value, InMin, InMax);
		if ((clampValue == value) || (SpawnFlags & SF_COLOR_BLEND_IGNORE_OUT_OF_RANGE) == 0) {
			//
			// Remap the input value to the desired output color and update the output.
			//
			Color color = default;
			color.R = (byte)(OutColor1.R + (((clampValue - InMin) * (OutColor2.R - OutColor1.R)) / (InMax - InMin)));
			color.G = (byte)(OutColor1.G + (((clampValue - InMin) * (OutColor2.G - OutColor1.G)) / (InMax - InMin)));
			color.B = (byte)(OutColor1.B + (((clampValue - InMin) * (OutColor2.B - OutColor1.B)) / (InMax - InMin)));
			color.A = (byte)(OutColor1.A + (((clampValue - InMin) * (OutColor2.A - OutColor1.A)) / (InMax - InMin)));

			OutValue.Set(color, inputdata.Activator, this);
		}
	}
}

/// <summary>
/// Holds a global state that can be queried by other entities to change
/// their behavior, such as "predistaster".
/// </summary>
[LinkEntityToClass("env_global")]
public class EnvGlobal : LogicalEntity
{
	/// <summary>
	/// Console command to set the state of a global
	/// </summary>
	[ConCommand("global_set", "global_set <globalname> <state>: Sets the state of the given env_global (0 = OFF, 1 = ON, 2 = DEAD).", FCvar.Cheat)]
	public static void CC_Global_Set(in TokenizedCommand args) {
		ReadOnlySpan<char> global = args[1];
		ReadOnlySpan<char> state = args[2];

		if (global.IsEmpty || state.IsEmpty) {
			Msg("Usage: global_set <globalname> <state>: Sets the state of the given env_global (0 = OFF, 1 = ON, 2 = DEAD).\n");
			return;
		}

		int newState = atoi(state);

		int index = GlobalEntity.GetIndex(global);

		if (index >= 0)
			GlobalEntity.SetState(index, (GlobalEState)newState);
		else
			GlobalEntity.Add(global, gpGlobals.MapName, (GlobalEState)newState);
	}

	public const int SF_GLOBAL_SET = 1;    // Set global state to initial state on spawn

	public readonly OutputInt OutCounter = new();

	public string? globalstate;
	public int triggermode;
	public int initialstate;
	public int counter;          // A counter value associated with this global.

	public static readonly new DataMap DataDesc = new(typeof(EnvGlobal), LogicalEntity.DataDesc, [
		DEFINE<EnvGlobal>.KEYFIELD(nameof(globalstate), FieldType.String, "globalstate"),
		DEFINE<EnvGlobal>.FIELD(nameof(triggermode), FieldType.Integer),
		DEFINE<EnvGlobal>.KEYFIELD(nameof(initialstate), FieldType.Integer, "initialstate"),
		DEFINE<EnvGlobal>.KEYFIELD(nameof(counter), FieldType.Integer, "counter"),

		// Inputs
		DEFINE<EnvGlobal>.INPUTFUNC(FieldType.Void, "TurnOn", nameof(InputTurnOn), (INPUTFUNCPTR)((self, data) => ((EnvGlobal)self).InputTurnOn(data))),
		DEFINE<EnvGlobal>.INPUTFUNC(FieldType.Void, "TurnOff", nameof(InputTurnOff), (INPUTFUNCPTR)((self, data) => ((EnvGlobal)self).InputTurnOff(data))),
		DEFINE<EnvGlobal>.INPUTFUNC(FieldType.Void, "Remove", nameof(InputRemove), (INPUTFUNCPTR)((self, data) => ((EnvGlobal)self).InputRemove(data))),
		DEFINE<EnvGlobal>.INPUTFUNC(FieldType.Void, "Toggle", nameof(InputToggle), (INPUTFUNCPTR)((self, data) => ((EnvGlobal)self).InputToggle(data))),

		DEFINE<EnvGlobal>.INPUTFUNC(FieldType.Integer, "SetCounter", nameof(InputSetCounter), (INPUTFUNCPTR)((self, data) => ((EnvGlobal)self).InputSetCounter(data))),
		DEFINE<EnvGlobal>.INPUTFUNC(FieldType.Integer, "AddToCounter", nameof(InputAddToCounter), (INPUTFUNCPTR)((self, data) => ((EnvGlobal)self).InputAddToCounter(data))),
		DEFINE<EnvGlobal>.INPUTFUNC(FieldType.Void, "GetCounter", nameof(InputGetCounter), (INPUTFUNCPTR)((self, data) => ((EnvGlobal)self).InputGetCounter(data))),

		DEFINE<EnvGlobal>.OUTPUT(nameof(OutCounter), "Counter", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void Spawn() {
		if (globalstate == null) {
			Util.Remove(this);
			return;
		}

		if ((SpawnFlags & SF_GLOBAL_SET) != 0) {
			if (!GlobalEntity.IsInTable(globalstate))
				GlobalEntity.Add(globalstate, gpGlobals.MapName, (GlobalEState)initialstate);

			if (counter != 0)
				GlobalEntity.SetCounter(globalstate, counter);
		}
	}

	public void InputTurnOn(InputData inputdata) {
		if (GlobalEntity.IsInTable(globalstate))
			GlobalEntity.SetState(globalstate, GlobalEState.On);
		else
			GlobalEntity.Add(globalstate, gpGlobals.MapName, GlobalEState.On);
	}

	public void InputTurnOff(InputData inputdata) {
		if (GlobalEntity.IsInTable(globalstate))
			GlobalEntity.SetState(globalstate, GlobalEState.Off);
		else
			GlobalEntity.Add(globalstate, gpGlobals.MapName, GlobalEState.Off);
	}

	public void InputRemove(InputData inputdata) {
		if (GlobalEntity.IsInTable(globalstate))
			GlobalEntity.SetState(globalstate, GlobalEState.Dead);
		else
			GlobalEntity.Add(globalstate, gpGlobals.MapName, GlobalEState.Dead);
	}

	public void InputSetCounter(InputData inputdata) {
		if (!GlobalEntity.IsInTable(globalstate))
			GlobalEntity.Add(globalstate, gpGlobals.MapName, GlobalEState.On);

		GlobalEntity.SetCounter(globalstate, inputdata.Value.Int());
	}

	public void InputAddToCounter(InputData inputdata) {
		if (!GlobalEntity.IsInTable(globalstate))
			GlobalEntity.Add(globalstate, gpGlobals.MapName, GlobalEState.On);

		GlobalEntity.AddToCounter(globalstate, inputdata.Value.Int());
	}

	public void InputGetCounter(InputData inputdata) {
		if (!GlobalEntity.IsInTable(globalstate))
			GlobalEntity.Add(globalstate, gpGlobals.MapName, GlobalEState.On);

		OutCounter.Set(GlobalEntity.GetCounter(globalstate), inputdata.Activator, this);
	}

	public void InputToggle(InputData inputdata) {
		GlobalEState oldState = GlobalEntity.GetState(globalstate);
		GlobalEState newState;

		if (oldState == GlobalEState.On)
			newState = GlobalEState.Off;
		else if (oldState == GlobalEState.Off)
			newState = GlobalEState.On;
		else
			return;

		if (GlobalEntity.IsInTable(globalstate))
			GlobalEntity.SetState(globalstate, newState);
		else
			GlobalEntity.Add(globalstate, gpGlobals.MapName, newState);
	}

	/// <summary>
	/// Draw any debug text overlays
	/// </summary>
	/// <returns>Current text offset from the top</returns>
	public override int DrawDebugTextOverlays() {
		// Skip AIClass debug overlays
		int text_offset = base.DrawDebugTextOverlays();

		if ((DebugOverlays & DebugOverlayBits.Text) != 0) {
			EntityText(text_offset, $"State: {globalstate}", 0);
			text_offset++;

			GlobalEState state = GlobalEntity.GetState(globalstate);

			string tempstr = "";
			switch (state) {
				case GlobalEState.Off:
					tempstr = "Value: OFF";
					break;

				case GlobalEState.On:
					tempstr = "Value: ON";
					break;

				case GlobalEState.Dead:
					tempstr = "Value: DEAD";
					break;
			}
			EntityText(text_offset, tempstr, 0);
			text_offset++;
		}
		return text_offset;
	}
}

[LinkEntityToClass("multisource")]
public class MultiSource : LogicalEntity
{
	public const int MS_MAX_TARGETS = 32;
	public const int SF_MULTI_INIT = 1;

	public readonly EHANDLE[] rgEntities = new EHANDLE[MS_MAX_TARGETS];
	public readonly int[] rgTriggered = new int[MS_MAX_TARGETS];

	public readonly OutputEvent OnTrigger = new();       // Fired when all connections are triggered.

	public int Total;
	public string? globalstate;

	public static readonly new DataMap DataDesc = new(typeof(MultiSource), LogicalEntity.DataDesc, [
		//!!!BUGBUG FIX
		DEFINE<MultiSource>.ARRAY(nameof(rgEntities), FieldType.EHandle, MS_MAX_TARGETS),
		DEFINE<MultiSource>.ARRAY(nameof(rgTriggered), FieldType.Integer, MS_MAX_TARGETS),
		DEFINE<MultiSource>.FIELD(nameof(Total), FieldType.Integer),

		DEFINE<MultiSource>.KEYFIELD(nameof(globalstate), FieldType.String, "globalstate"),

		// Function pointers
		// DEFINE_FUNCTION( Register ),

		// Outputs
		DEFINE<MultiSource>.OUTPUT(nameof(OnTrigger), "OnTrigger", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	/// <summary>
	/// Cache user entity field values until spawn is called.
	/// </summary>
	/// <param name="keyName">Key to handle.</param>
	/// <param name="value">Value for key.</param>
	/// <returns>Returns true if the key was handled, false if not.</returns>
	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		if (FStrEq(keyName, "style") ||
				FStrEq(keyName, "height") ||
				FStrEq(keyName, "killtarget") ||
				FStrEq(keyName, "value1") ||
				FStrEq(keyName, "value2") ||
				FStrEq(keyName, "value3")) {
		}
		else
			return base.KeyValue(keyName, value);

		return true;
	}

	public override void Spawn() {
		SetNextThink(gpGlobals.CurTime + 0.1f);
		SpawnFlags |= SF_MULTI_INIT;  // Until it's initialized
		SetThink(Register);
	}

	public override void Use(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		int i = 0;

		// Find the entity in our list
		while (i < Total)
			if (rgEntities[i++].Get() == caller)
				break;

		// if we didn't find it, report error and leave
		if (i > Total) {
			Warning($"MultiSrc: Used by non member {(caller!.Edict() != null ? caller.GetClassname() : "<logical entity>")}.\n");
			return;
		}

		// CONSIDER: a Use input to the multisource always toggles.  Could check useType for ON/OFF/TOGGLE

		if (i > 0)
			rgTriggered[i - 1] ^= 1;

		if (IsTriggered(activator)) {
			DevMsg(2, $"Multisource {GetDebugName()} enabled ({Total} inputs)\n");
			UseType newUseType = UseType.Toggle;
			if (globalstate != null)
				newUseType = UseType.On;

			OnTrigger.FireOutput(activator, this);
		}
	}

	public override EntityCapabilities ObjectCaps() => base.ObjectCaps() | EntityCapabilities.Master;

	public bool IsTriggered(BaseEntity? activator) {
		// Is everything triggered?
		int i = 0;

		// Still initializing?
		if ((SpawnFlags & SF_MULTI_INIT) != 0)
			return false;

		while (i < Total) {
			if (rgTriggered[i] == 0)
				break;
			i++;
		}

		if (i == Total) {
			if (globalstate == null || GlobalEntity.GetState(globalstate) == GlobalEState.On)
				return true;
		}

		return false;
	}

	public void Register() {
		BaseEntity? target = null;

		Total = 0;
		for (int i = 0; i < MS_MAX_TARGETS; i++)
			rgEntities[i].Index = 0;

		SetThink(SUB_DoNothing);

		// search for all entities which target this multisource (Name)
		// dvsents2: port multisource to entity I/O!

		target = gEntList.FindEntityByTarget(null, GetEntityName());

		while (target != null && (Total < MS_MAX_TARGETS)) {
			if (target != null)
				rgEntities[Total++].Set(target);

			target = gEntList.FindEntityByTarget(target, GetEntityName());
		}

		target = gEntList.FindEntityByClassname(null, "multi_manager");
		while (target != null && (Total < MS_MAX_TARGETS)) {
			if (target != null && target.HasTarget(GetEntityName()))
				rgEntities[Total++].Set(target);

			target = gEntList.FindEntityByClassname(target, "multi_manager");
		}

		SpawnFlags &= ~SF_MULTI_INIT;
	}
}

/// <summary>
/// Holds a value that can be added to and subtracted from.
/// </summary>
[LinkEntityToClass("math_counter")]
public class MathCounter : LogicalEntity
{
	float Min;      // Minimum clamp value. If min and max are BOTH zero, no clamping is done.
	float Max;      // Maximum clamp value.
	bool HitMin;     // Set when we reach or go below our minimum value, cleared if we go above it again.
	bool HitMax;     // Set when we reach or exceed our maximum value, cleared if we fall below it again.

	bool Disabled;

	// Outputs
	public readonly OutputFloat OutValue = new();
	public readonly OutputFloat OnGetValue = new();  // Used for polling the counter value.
	public readonly OutputEvent OnHitMin = new();
	public readonly OutputEvent OnHitMax = new();

	public static readonly new DataMap DataDesc = new(typeof(MathCounter), LogicalEntity.DataDesc, [
		DEFINE<MathCounter>.FIELD(nameof(HitMax), FieldType.Boolean),
		DEFINE<MathCounter>.FIELD(nameof(HitMin), FieldType.Boolean),

		// Keys
		DEFINE<MathCounter>.KEYFIELD(nameof(Min), FieldType.Float, "min"),
		DEFINE<MathCounter>.KEYFIELD(nameof(Max), FieldType.Float, "max"),

		DEFINE<MathCounter>.KEYFIELD(nameof(Disabled), FieldType.Boolean, "StartDisabled"),

		// Inputs
		DEFINE<MathCounter>.INPUTFUNC(FieldType.Float, "Add", nameof(InputAdd), (INPUTFUNCPTR)((self, data) => ((MathCounter)self).InputAdd(data))),
		DEFINE<MathCounter>.INPUTFUNC(FieldType.Float, "Divide", nameof(InputDivide), (INPUTFUNCPTR)((self, data) => ((MathCounter)self).InputDivide(data))),
		DEFINE<MathCounter>.INPUTFUNC(FieldType.Float, "Multiply", nameof(InputMultiply), (INPUTFUNCPTR)((self, data) => ((MathCounter)self).InputMultiply(data))),
		DEFINE<MathCounter>.INPUTFUNC(FieldType.Float, "SetValue", nameof(InputSetValue), (INPUTFUNCPTR)((self, data) => ((MathCounter)self).InputSetValue(data))),
		DEFINE<MathCounter>.INPUTFUNC(FieldType.Float, "SetValueNoFire", nameof(InputSetValueNoFire), (INPUTFUNCPTR)((self, data) => ((MathCounter)self).InputSetValueNoFire(data))),
		DEFINE<MathCounter>.INPUTFUNC(FieldType.Float, "Subtract", nameof(InputSubtract), (INPUTFUNCPTR)((self, data) => ((MathCounter)self).InputSubtract(data))),
		DEFINE<MathCounter>.INPUTFUNC(FieldType.Float, "SetHitMax", nameof(InputSetHitMax), (INPUTFUNCPTR)((self, data) => ((MathCounter)self).InputSetHitMax(data))),
		DEFINE<MathCounter>.INPUTFUNC(FieldType.Float, "SetHitMin", nameof(InputSetHitMin), (INPUTFUNCPTR)((self, data) => ((MathCounter)self).InputSetHitMin(data))),
		DEFINE<MathCounter>.INPUTFUNC(FieldType.Void, "GetValue", nameof(InputGetValue), (INPUTFUNCPTR)((self, data) => ((MathCounter)self).InputGetValue(data))),
		DEFINE<MathCounter>.INPUTFUNC(FieldType.Void, "Enable", nameof(InputEnable), (INPUTFUNCPTR)((self, data) => ((MathCounter)self).InputEnable(data))),
		DEFINE<MathCounter>.INPUTFUNC(FieldType.Void, "Disable", nameof(InputDisable), (INPUTFUNCPTR)((self, data) => ((MathCounter)self).InputDisable(data))),

		// Outputs
		DEFINE<MathCounter>.OUTPUT(nameof(OutValue), "OutValue", eventFuncs),
		DEFINE<MathCounter>.OUTPUT(nameof(OnHitMin), "OnHitMin", eventFuncs),
		DEFINE<MathCounter>.OUTPUT(nameof(OnHitMax), "OnHitMax", eventFuncs),
		DEFINE<MathCounter>.OUTPUT(nameof(OnGetValue), "OnGetValue", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	/// <summary>
	/// Handles key values from the BSP before spawn is called.
	/// </summary>
	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		//
		// Set the initial value of the counter.
		//
		if (stricmp(keyName, "startvalue") == 0) {
			OutValue.Init(atoi(value));
			return true;
		}

		return base.KeyValue(keyName, value);
	}

	/// <summary>
	/// Called before spawning, after key values have been set.
	/// </summary>
	public override void Spawn() {
		//
		// Make sure max and min are ordered properly or clamp won't work.
		//
		if (Min > Max)
			(Min, Max) = (Max, Min);

		//
		// Clamp initial value to within the valid range.
		//
		if ((Min != 0) || (Max != 0)) {
			float startValue = Math.Clamp(OutValue.Get(), Min, Max);
			OutValue.Init(startValue);
		}
	}

	/// <summary>
	/// Draw any debug text overlays
	/// </summary>
	/// <returns>Current text offset from the top</returns>
	public override int DrawDebugTextOverlays() {
		int text_offset = base.DrawDebugTextOverlays();

		if ((DebugOverlays & DebugOverlayBits.Text) != 0) {
			EntityText(text_offset, $"    min value: {Min:F6}", 0);
			text_offset++;

			EntityText(text_offset, $"    max value: {Max:F6}", 0);
			text_offset++;

			EntityText(text_offset, $"current value: {OutValue.Get():F6}", 0);
			text_offset++;

			if (Disabled)
				EntityText(text_offset, "*DISABLED*", 0);
			else
				EntityText(text_offset, "Enabled.", 0);
			text_offset++;
		}
		return text_offset;
	}

	/// <summary>
	/// Change min/max
	/// </summary>
	public void InputSetHitMax(InputData inputdata) {
		Max = inputdata.Value.Float();
		if (Max < Min)
			Min = Max;
		UpdateOutValue(inputdata.Activator, OutValue.Get());
	}

	public void InputSetHitMin(InputData inputdata) {
		Min = inputdata.Value.Float();
		if (Max < Min)
			Max = Min;
		UpdateOutValue(inputdata.Activator, OutValue.Get());
	}

	/// <summary>
	/// Input handler for adding to the accumulator value.
	/// </summary>
	/// <param name="inputdata">Float value to add.</param>
	public void InputAdd(InputData inputdata) {
		if (Disabled) {
			DevMsg($"Math Counter {GetDebugName()} ignoring ADD because it is disabled\n");
			return;
		}

		float newValue = OutValue.Get() + inputdata.Value.Float();
		UpdateOutValue(inputdata.Activator, newValue);
	}

	/// <summary>
	/// Input handler for multiplying the current value.
	/// </summary>
	/// <param name="inputdata">Float value to multiply the value by.</param>
	public void InputDivide(InputData inputdata) {
		if (Disabled) {
			DevMsg($"Math Counter {GetDebugName()} ignoring DIVIDE because it is disabled\n");
			return;
		}

		if (inputdata.Value.Float() != 0) {
			float newValue = OutValue.Get() / inputdata.Value.Float();
			UpdateOutValue(inputdata.Activator, newValue);
		}
		else {
			DevMsg(1, "LEVEL DESIGN ERROR: Divide by zero in math_value\n");
			UpdateOutValue(inputdata.Activator, OutValue.Get());
		}
	}

	/// <summary>
	/// Input handler for multiplying the current value.
	/// </summary>
	/// <param name="inputdata">Float value to multiply the value by.</param>
	public void InputMultiply(InputData inputdata) {
		if (Disabled) {
			DevMsg($"Math Counter {GetDebugName()} ignoring MULTIPLY because it is disabled\n");
			return;
		}

		float newValue = OutValue.Get() * inputdata.Value.Float();
		UpdateOutValue(inputdata.Activator, newValue);
	}

	/// <summary>
	/// Input handler for updating the value.
	/// </summary>
	/// <param name="inputdata">Float value to set.</param>
	public void InputSetValue(InputData inputdata) {
		if (Disabled) {
			DevMsg($"Math Counter {GetDebugName()} ignoring SETVALUE because it is disabled\n");
			return;
		}

		UpdateOutValue(inputdata.Activator, inputdata.Value.Float());
	}

	/// <summary>
	/// Input handler for updating the value.
	/// </summary>
	/// <param name="inputdata">Float value to set.</param>
	public void InputSetValueNoFire(InputData inputdata) {
		if (Disabled) {
			DevMsg($"Math Counter {GetDebugName()} ignoring SETVALUENOFIRE because it is disabled\n");
			return;
		}

		float newValue = inputdata.Value.Float();
		if ((Min != 0) || (Max != 0))
			newValue = Math.Clamp(newValue, Min, Max);

		OutValue.Init(newValue);
	}

	/// <summary>
	/// Input handler for subtracting from the current value.
	/// </summary>
	/// <param name="inputdata">Float value to subtract.</param>
	public void InputSubtract(InputData inputdata) {
		if (Disabled) {
			DevMsg($"Math Counter {GetDebugName()} ignoring SUBTRACT because it is disabled\n");
			return;
		}

		float newValue = OutValue.Get() - inputdata.Value.Float();
		UpdateOutValue(inputdata.Activator, newValue);
	}

	public void InputGetValue(InputData inputdata) {
		float outValue = OutValue.Get();
		OnGetValue.Set(outValue, inputdata.Activator, inputdata.Caller);
	}

	public void InputEnable(InputData inputdata) => Disabled = false;

	public void InputDisable(InputData inputdata) => Disabled = true;

	/// <summary>
	/// Sets the value to the new value, clamping and firing the output value.
	/// </summary>
	/// <param name="newValue">Value to set.</param>
	public void UpdateOutValue(BaseEntity? activator, float newValue) {
		if ((Min != 0) || (Max != 0)) {
			//
			// Fire an output any time we reach or exceed our maximum value.
			//
			if (newValue >= Max) {
				if (!HitMax) {
					HitMax = true;
					OnHitMax.FireOutput(activator, this);
				}
			}
			else
				HitMax = false;

			//
			// Fire an output any time we reach or go below our minimum value.
			//
			if (newValue <= Min) {
				if (!HitMin) {
					HitMin = true;
					OnHitMin.FireOutput(activator, this);
				}
			}
			else
				HitMin = false;

			newValue = Math.Clamp(newValue, Min, Max);
		}

		OutValue.Set(newValue, activator, this);
	}
}

/// <summary>
/// Compares a single string input to up to 16 case values, firing an
/// output corresponding to the case value that matched, or a default
/// output if the input value didn't match any of the case values.
///
/// This can also be used to fire a random output from a set of outputs.
/// </summary>
[LinkEntityToClass("logic_case")]
public class LogicCase : LogicalEntity
{
	public const int MAX_LOGIC_CASES = 16;

	string? Case01, Case02, Case03, Case04, Case05, Case06, Case07, Case08, Case09, Case10, Case11, Case12, Case13, Case14, Case15, Case16;

	int ShuffleCases;
	int LastShuffleCase;
	readonly byte[] ShuffleCaseMap = new byte[MAX_LOGIC_CASES];

	// Outputs
	readonly OutputEvent OnCase01 = new(), OnCase02 = new(), OnCase03 = new(), OnCase04 = new(), OnCase05 = new(), OnCase06 = new(), OnCase07 = new(), OnCase08 = new(),
		OnCase09 = new(), OnCase10 = new(), OnCase11 = new(), OnCase12 = new(), OnCase13 = new(), OnCase14 = new(), OnCase15 = new(), OnCase16 = new();
	readonly OutputEvent[] OnCase;     // Fired when the input value matches one of the case values.
	readonly OutputVariant OnDefault = new();                 // Fired when no match was found.

	public LogicCase() {
		OnCase = [OnCase01, OnCase02, OnCase03, OnCase04, OnCase05, OnCase06, OnCase07, OnCase08, OnCase09, OnCase10, OnCase11, OnCase12, OnCase13, OnCase14, OnCase15, OnCase16];
	}

	string? Case(int i) => i switch {
		0 => Case01, 1 => Case02, 2 => Case03, 3 => Case04, 4 => Case05, 5 => Case06, 6 => Case07, 7 => Case08,
		8 => Case09, 9 => Case10, 10 => Case11, 11 => Case12, 12 => Case13, 13 => Case14, 14 => Case15, 15 => Case16,
		_ => null
	};

	public static readonly new DataMap DataDesc = new(typeof(LogicCase), LogicalEntity.DataDesc, [
		// Silence, Classcheck!
		//	DEFINE_ARRAY( m_nCase, FIELD_STRING, MAX_LOGIC_CASES ),

		// Keys
		DEFINE<LogicCase>.KEYFIELD(nameof(Case01), FieldType.String, "Case01"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case02), FieldType.String, "Case02"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case03), FieldType.String, "Case03"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case04), FieldType.String, "Case04"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case05), FieldType.String, "Case05"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case06), FieldType.String, "Case06"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case07), FieldType.String, "Case07"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case08), FieldType.String, "Case08"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case09), FieldType.String, "Case09"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case10), FieldType.String, "Case10"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case11), FieldType.String, "Case11"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case12), FieldType.String, "Case12"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case13), FieldType.String, "Case13"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case14), FieldType.String, "Case14"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case15), FieldType.String, "Case15"),
		DEFINE<LogicCase>.KEYFIELD(nameof(Case16), FieldType.String, "Case16"),

		DEFINE<LogicCase>.FIELD(nameof(ShuffleCases), FieldType.Integer),
		DEFINE<LogicCase>.FIELD(nameof(LastShuffleCase), FieldType.Integer),
		DEFINE<LogicCase>.ARRAY(nameof(ShuffleCaseMap), FieldType.Character, MAX_LOGIC_CASES),

		// Inputs
		DEFINE<LogicCase>.INPUTFUNC(FieldType.Input, "InValue", nameof(InputValue), (INPUTFUNCPTR)((self, data) => ((LogicCase)self).InputValue(data))),
		DEFINE<LogicCase>.INPUTFUNC(FieldType.Void, "PickRandom", nameof(InputPickRandom), (INPUTFUNCPTR)((self, data) => ((LogicCase)self).InputPickRandom(data))),
		DEFINE<LogicCase>.INPUTFUNC(FieldType.Void, "PickRandomShuffle", nameof(InputPickRandomShuffle), (INPUTFUNCPTR)((self, data) => ((LogicCase)self).InputPickRandomShuffle(data))),

		// Outputs
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase01), "OnCase01", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase02), "OnCase02", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase03), "OnCase03", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase04), "OnCase04", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase05), "OnCase05", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase06), "OnCase06", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase07), "OnCase07", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase08), "OnCase08", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase09), "OnCase09", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase10), "OnCase10", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase11), "OnCase11", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase12), "OnCase12", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase13), "OnCase13", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase14), "OnCase14", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase15), "OnCase15", eventFuncs),
		DEFINE<LogicCase>.OUTPUT(nameof(OnCase16), "OnCase16", eventFuncs),

		DEFINE<LogicCase>.OUTPUT(nameof(OnDefault), "OnDefault", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	/// <summary>
	/// Called before spawning, after key values have been set.
	/// </summary>
	public override void Spawn() => LastShuffleCase = -1;

	/// <summary>
	/// Evaluates the new input value, firing the appropriate OnCaseX output
	/// if the input value matches one of the "CaseX" keys.
	/// </summary>
	/// <param name="inputdata">Variant value to compare against the values of the case fields.
	/// We use a variant so that we can convert any input type to a string.</param>
	public void InputValue(InputData inputdata) {
		ReadOnlySpan<char> value = inputdata.Value.String();
		for (int i = 0; i < MAX_LOGIC_CASES; i++) {
			string? caseValue = Case(i);
			if ((caseValue != null) && stricmp(caseValue, value) == 0) {
				OnCase[i].FireOutput(inputdata.Activator, this);
				return;
			}
		}

		OnDefault.Set(inputdata.Value, inputdata.Activator, this);
	}

	/// <summary>
	/// Count the number of valid cases, building a packed array
	/// that maps 0..NumCases to the actual CaseX values.
	///
	/// This allows our zany mappers to set up cases sparsely if they desire.
	/// NOTE: assumes caseMap points to an array of MAX_LOGIC_CASES
	/// </summary>
	public int BuildCaseMap(Span<byte> caseMap) {
		caseMap[..MAX_LOGIC_CASES].Clear();

		int numCases = 0;
		for (int i = 0; i < MAX_LOGIC_CASES; i++) {
			if (OnCase[i].NumberOfElements() > 0) {
				caseMap[numCases] = (byte)i;
				numCases++;
			}
		}

		return numCases;
	}

	/// <summary>
	/// Makes the case statement choose a case at random.
	/// </summary>
	public void InputPickRandom(InputData inputdata) {
		Span<byte> caseMap = stackalloc byte[MAX_LOGIC_CASES];
		int numCases = BuildCaseMap(caseMap);

		//
		// Choose a random case from the ones that were set up by the level designer.
		//
		if (numCases > 0) {
			int random = RandomInt(0, numCases - 1);
			int caseIndex = caseMap[random];

			Assert(caseIndex < MAX_LOGIC_CASES);

			if (caseIndex < MAX_LOGIC_CASES)
				OnCase[caseIndex].FireOutput(inputdata.Activator, this);
		}
		else
			DevMsg(1, $"Firing PickRandom input on logic_case {GetDebugName()} with no cases set up\n");
	}

	/// <summary>
	/// Makes the case statement choose a case at random.
	/// </summary>
	public void InputPickRandomShuffle(InputData inputdata) {
		int avoidCase = -1;
		int caseCount = ShuffleCases;

		if (caseCount == 0) {
			// Starting a new shuffle batch.
			caseCount = ShuffleCases = BuildCaseMap(ShuffleCaseMap);

			if ((ShuffleCases > 1) && (LastShuffleCase != -1)) {
				// Remove the previously picked case from the case map for this pick only.
				// This avoids repeats across shuffle batch boundaries.
				avoidCase = LastShuffleCase;

				for (int i = 0; i < ShuffleCases; i++) {
					if (ShuffleCaseMap[i] == avoidCase) {
						byte swap = ShuffleCaseMap[i];
						ShuffleCaseMap[i] = ShuffleCaseMap[caseCount - 1];
						ShuffleCaseMap[caseCount - 1] = swap;
						caseCount--;
						break;
					}
				}
			}
		}

		//
		// Choose a random case from the ones that were set up by the level designer.
		// Never repeat a case within a shuffle batch, nor consecutively across batches.
		//
		if (caseCount > 0) {
			int random = RandomInt(0, caseCount - 1);

			int caseIndex = ShuffleCaseMap[random];
			Assert(caseIndex < MAX_LOGIC_CASES);

			if (caseIndex < MAX_LOGIC_CASES)
				OnCase[caseIndex].FireOutput(inputdata.Activator, this);

			ShuffleCaseMap[random] = ShuffleCaseMap[ShuffleCases - 1];
			ShuffleCases--;

			LastShuffleCase = caseIndex;
		}
		else
			DevMsg(1, $"Firing PickRandom input on logic_case {GetDebugName()} with no cases set up\n");
	}
}

/// <summary>
/// Compares a floating point input to a predefined value, firing an
/// output to indicate the result of the comparison.
/// </summary>
[LinkEntityToClass("logic_compare")]
public class LogicCompare : LogicalEntity
{
	float InValue;                  // Place to hold the last input value for a recomparison.
	float CompareValue;             // The value to compare the input value against.

	// Outputs
	readonly OutputFloat OnLessThan = new();          // Fired when the input value is less than the compare value.
	readonly OutputFloat OnEqualTo = new();           // Fired when the input value is equal to the compare value.
	readonly OutputFloat OnNotEqualTo = new();        // Fired when the input value is not equal to the compare value.
	readonly OutputFloat OnGreaterThan = new();       // Fired when the input value is greater than the compare value.

	public static readonly new DataMap DataDesc = new(typeof(LogicCompare), LogicalEntity.DataDesc, [
		// Keys
		DEFINE<LogicCompare>.KEYFIELD(nameof(CompareValue), FieldType.Float, "CompareValue"),
		DEFINE<LogicCompare>.KEYFIELD(nameof(InValue), FieldType.Float, "InitialValue"),

		// Inputs
		DEFINE<LogicCompare>.INPUTFUNC(FieldType.Float, "SetValue", nameof(InputSetValue), (INPUTFUNCPTR)((self, data) => ((LogicCompare)self).InputSetValue(data))),
		DEFINE<LogicCompare>.INPUTFUNC(FieldType.Float, "SetValueCompare", nameof(InputSetValueCompare), (INPUTFUNCPTR)((self, data) => ((LogicCompare)self).InputSetValueCompare(data))),
		DEFINE<LogicCompare>.INPUTFUNC(FieldType.Float, "SetCompareValue", nameof(InputSetCompareValue), (INPUTFUNCPTR)((self, data) => ((LogicCompare)self).InputSetCompareValue(data))),
		DEFINE<LogicCompare>.INPUTFUNC(FieldType.Void, "Compare", nameof(InputCompare), (INPUTFUNCPTR)((self, data) => ((LogicCompare)self).InputCompare(data))),

		// Outputs
		DEFINE<LogicCompare>.OUTPUT(nameof(OnEqualTo), "OnEqualTo", eventFuncs),
		DEFINE<LogicCompare>.OUTPUT(nameof(OnNotEqualTo), "OnNotEqualTo", eventFuncs),
		DEFINE<LogicCompare>.OUTPUT(nameof(OnGreaterThan), "OnGreaterThan", eventFuncs),
		DEFINE<LogicCompare>.OUTPUT(nameof(OnLessThan), "OnLessThan", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	/// <summary>
	/// Input handler for a new input value without performing a comparison.
	/// </summary>
	void InputSetValue(InputData inputdata) => InValue = inputdata.Value.Float();

	/// <summary>
	/// Input handler for a setting a new value and doing the comparison.
	/// </summary>
	void InputSetValueCompare(InputData inputdata) {
		InValue = inputdata.Value.Float();
		DoCompare(inputdata.Activator, InValue);
	}

	/// <summary>
	/// Input handler for a new input value without performing a comparison.
	/// </summary>
	void InputSetCompareValue(InputData inputdata) => CompareValue = inputdata.Value.Float();

	/// <summary>
	/// Input handler for forcing a recompare of the last input value.
	/// </summary>
	void InputCompare(InputData inputdata) => DoCompare(inputdata.Activator, InValue);

	/// <summary>
	/// Compares the input value to the compare value, firing the appropriate
	/// output(s) based on the comparison result.
	/// </summary>
	/// <param name="inValue">Value to compare against the comparison value.</param>
	void DoCompare(BaseEntity? activator, float inValue) {
		if (inValue == CompareValue)
			OnEqualTo.Set(inValue, activator, this);
		else {
			OnNotEqualTo.Set(inValue, activator, this);

			if (inValue > CompareValue)
				OnGreaterThan.Set(inValue, activator, this);
			else
				OnLessThan.Set(inValue, activator, this);
		}
	}

	/// <summary>
	/// Draw any debug text overlays
	/// </summary>
	/// <returns>Current text offset from the top</returns>
	public override int DrawDebugTextOverlays() {
		int text_offset = base.DrawDebugTextOverlays();

		if ((DebugOverlays & DebugOverlayBits.Text) != 0) {
			// print duration
			EntityText(text_offset, $"    Initial Value: {InValue:F6}", 0);
			text_offset++;

			// print hold time
			EntityText(text_offset, $"    Compare Value: {CompareValue:F6}", 0);
			text_offset++;
		}
		return text_offset;
	}
}

/// <summary>
/// Tests a boolean value, firing an output to indicate whether the
/// value was true or false.
/// </summary>
[LinkEntityToClass("logic_branch")]
public class LogicBranch : LogicalEntity
{
	enum LogicBranchFire
	{
		Fire,
		NoFire,
	}

	bool InValue;                    // Place to hold the last input value for a future test.

	readonly List<EHANDLE> Listeners = [];    // A list of logic_branch_listeners that are monitoring us.

	// Outputs
	readonly OutputEvent OnTrue = new();              // Fired when the value is true.
	readonly OutputEvent OnFalse = new();             // Fired when the value is false.

	public static readonly new DataMap DataDesc = new(typeof(LogicBranch), LogicalEntity.DataDesc, [
		// Keys
		DEFINE<LogicBranch>.KEYFIELD(nameof(InValue), FieldType.Boolean, "InitialValue"),

		// DEFINE_UTLVECTOR( m_Listeners, FIELD_EHANDLE ),

		// Inputs
		DEFINE<LogicBranch>.INPUTFUNC(FieldType.Boolean, "SetValue", nameof(InputSetValue), (INPUTFUNCPTR)((self, data) => ((LogicBranch)self).InputSetValue(data))),
		DEFINE<LogicBranch>.INPUTFUNC(FieldType.Boolean, "SetValueTest", nameof(InputSetValueTest), (INPUTFUNCPTR)((self, data) => ((LogicBranch)self).InputSetValueTest(data))),
		DEFINE<LogicBranch>.INPUTFUNC(FieldType.Void, "Toggle", nameof(InputToggle), (INPUTFUNCPTR)((self, data) => ((LogicBranch)self).InputToggle(data))),
		DEFINE<LogicBranch>.INPUTFUNC(FieldType.Void, "ToggleTest", nameof(InputToggleTest), (INPUTFUNCPTR)((self, data) => ((LogicBranch)self).InputToggleTest(data))),
		DEFINE<LogicBranch>.INPUTFUNC(FieldType.Void, "Test", nameof(InputTest), (INPUTFUNCPTR)((self, data) => ((LogicBranch)self).InputTest(data))),

		// Outputs
		DEFINE<LogicBranch>.OUTPUT(nameof(OnTrue), "OnTrue", eventFuncs),
		DEFINE<LogicBranch>.OUTPUT(nameof(OnFalse), "OnFalse", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void UpdateOnRemove() {
		for (int i = 0; i < Listeners.Count; i++) {
			BaseEntity? entity = Listeners[i].Get();
			if (entity != null)
				g_EventQueue.AddEvent(this, "_OnLogicBranchRemoved", 0, this, this);
		}

		base.UpdateOnRemove();
	}

	/// <summary>
	/// Input handler to set a new input value without firing outputs.
	/// </summary>
	/// <param name="inputdata">Boolean value to set.</param>
	void InputSetValue(InputData inputdata) => UpdateValue(inputdata.Value.Bool(), inputdata.Activator, LogicBranchFire.NoFire);

	/// <summary>
	/// Input handler to set a new input value and fire appropriate outputs.
	/// </summary>
	/// <param name="inputdata">Boolean value to set.</param>
	void InputSetValueTest(InputData inputdata) => UpdateValue(inputdata.Value.Bool(), inputdata.Activator, LogicBranchFire.Fire);

	/// <summary>
	/// Input handler for toggling the boolean value without firing outputs.
	/// </summary>
	void InputToggle(InputData inputdata) => UpdateValue(!InValue, inputdata.Activator, LogicBranchFire.NoFire);

	/// <summary>
	/// Input handler for toggling the boolean value and then firing the
	/// appropriate output based on the new value.
	/// </summary>
	void InputToggleTest(InputData inputdata) => UpdateValue(!InValue, inputdata.Activator, LogicBranchFire.Fire);

	/// <summary>
	/// Input handler for forcing a test of the last input value.
	/// </summary>
	void InputTest(InputData inputdata) => UpdateValue(InValue, inputdata.Activator, LogicBranchFire.Fire);

	/// <summary>
	/// Tests the last input value, firing the appropriate output based on
	/// the test result.
	/// </summary>
	void UpdateValue(bool newValue, BaseEntity? activator, LogicBranchFire fire) {
		if (InValue != newValue) {
			InValue = newValue;

			for (int i = 0; i < Listeners.Count; i++) {
				BaseEntity? entity = Listeners[i].Get();
				if (entity != null)
					g_EventQueue.AddEvent(entity, "_OnLogicBranchChanged", 0, this, this);
			}
		}

		if (fire == LogicBranchFire.Fire) {
			if (InValue)
				OnTrue.FireOutput(activator, this);
			else
				OnFalse.FireOutput(activator, this);
		}
	}

	/// <summary>
	/// Accessor for logic_branchlist to test the value of the branch on demand.
	/// </summary>
	public bool GetLogicBranchState() => InValue;

	public void AddLogicBranchListener(BaseEntity entity) {
		EHANDLE handle = entity.GetRefEHandle();
		if (!Listeners.Contains(handle))
			Listeners.Add(handle);
	}

	public override int DrawDebugTextOverlays() {
		int text_offset = base.DrawDebugTextOverlays();

		if ((DebugOverlays & DebugOverlayBits.Text) != 0) {
			// print refire time
			EntityText(text_offset, $"Branch value: {(InValue ? "TRUE" : "FALSE")}", 0);
			text_offset++;
		}

		return text_offset;
	}
}

/// <summary>
/// Autosaves when triggered
/// </summary>
[LinkEntityToClass("logic_autosave")]
public class LogicAutosave : LogicalEntity
{
	protected bool ForceNewLevelUnit;
	protected int MinHitPoints;
	protected int MinHitPointsToCommit;

	public static readonly new DataMap DataDesc = new(typeof(LogicAutosave), LogicalEntity.DataDesc, [
		DEFINE<LogicAutosave>.KEYFIELD(nameof(ForceNewLevelUnit), FieldType.Boolean, "NewLevelUnit"),
		DEFINE<LogicAutosave>.KEYFIELD(nameof(MinHitPoints), FieldType.Integer, "MinimumHitPoints"),
		DEFINE<LogicAutosave>.KEYFIELD(nameof(MinHitPointsToCommit), FieldType.Integer, "MinHitPointsToCommit"),
		// Inputs
		DEFINE<LogicAutosave>.INPUTFUNC(FieldType.Void, "Save", nameof(InputSave), (INPUTFUNCPTR)((self, data) => ((LogicAutosave)self).InputSave(data))),
		DEFINE<LogicAutosave>.INPUTFUNC(FieldType.Float, "SaveDangerous", nameof(InputSaveDangerous), (INPUTFUNCPTR)((self, data) => ((LogicAutosave)self).InputSaveDangerous(data))),
		DEFINE<LogicAutosave>.INPUTFUNC(FieldType.Integer, "SetMinHitpointsThreshold", nameof(InputSetMinHitpointsThreshold), (INPUTFUNCPTR)((self, data) => ((LogicAutosave)self).InputSetMinHitpointsThreshold(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	/// <summary>
	/// Save!
	/// </summary>
	protected void InputSave(InputData inputdata) {
		if (ForceNewLevelUnit)
			engine.ClearSaveDir();

		engine.ServerCommand("autosave\n");
	}

	/// <summary>
	/// Save safely!
	/// </summary>
	protected void InputSaveDangerous(InputData inputdata) {
		BasePlayer? player = Util.PlayerByIndex(1);

		if (TriggerSave.AutoSaveDangerousTime != 0.0f && TriggerSave.AutoSaveDangerousTime >= gpGlobals.CurTime) {
			// A previous dangerous auto save was waiting to become safe

			if (player!.GetDeathTime() == 0.0f || player.GetDeathTime() > gpGlobals.CurTime) {
				// The player isn't dead, so make the dangerous auto save safe
				engine.ServerCommand("autosavedangerousissafe\n");
			}
		}

		if (ForceNewLevelUnit)
			engine.ClearSaveDir();

		if (player!.GetHealth() >= MinHitPoints) {
			engine.ServerCommand("autosavedangerous\n");
			TriggerSave.AutoSaveDangerousTime = gpGlobals.CurTime + inputdata.Value.Float();

			// Player must have this much health when we go to commit, or we don't commit.
			TriggerSave.AutoSaveDangerousMinHealthToCommit = MinHitPointsToCommit;
		}
	}

	/// <summary>
	/// Keyfield set func
	/// </summary>
	protected void InputSetMinHitpointsThreshold(InputData inputdata) {
		int setTo = inputdata.Value.Int();
		AssertMsg(setTo >= 0 && setTo <= 100, $"Tried to set autosave MinHitpointsThreshold to {setTo}!\n");
		MinHitPoints = setTo;
	}
}

/// <summary>
/// Autosaves when triggered
/// </summary>
[LinkEntityToClass("logic_active_autosave")]
public class LogicActiveAutosave : LogicAutosave
{
	int TriggerHitPoints;
	float TimeToTrigger;
	TimeUnit_t StartTime;
	float DangerousTime;

	public static readonly new DataMap DataDesc = new(typeof(LogicActiveAutosave), LogicAutosave.DataDesc, [
		DEFINE<LogicActiveAutosave>.KEYFIELD(nameof(TriggerHitPoints), FieldType.Integer, "TriggerHitPoints"),
		DEFINE<LogicActiveAutosave>.KEYFIELD(nameof(TimeToTrigger), FieldType.Float, "TimeToTrigger"),
		DEFINE<LogicActiveAutosave>.KEYFIELD(nameof(DangerousTime), FieldType.Float, "DangerousTime"),
		DEFINE<LogicActiveAutosave>.FIELD(nameof(StartTime), FieldType.Time),
		// DEFINE_THINKFUNC( SaveThink ),
		DEFINE<LogicActiveAutosave>.INPUTFUNC(FieldType.Void, "Enable", nameof(InputEnable), (INPUTFUNCPTR)((self, data) => ((LogicActiveAutosave)self).InputEnable(data))),
		DEFINE<LogicActiveAutosave>.INPUTFUNC(FieldType.Void, "Disable", nameof(InputDisable), (INPUTFUNCPTR)((self, data) => ((LogicActiveAutosave)self).InputDisable(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	void InputEnable(InputData inputdata) {
		StartTime = -1;
		SetThink(SaveThink);
		SetNextThink(gpGlobals.CurTime);
	}

	void InputDisable(InputData inputdata) => SetThink(null);

	void SaveThink() {
		BasePlayer? player = Util.GetLocalPlayer();
		if (player != null) {
			if (StartTime < 0) {
				if (player.GetHealth() <= MinHitPoints)
					StartTime = gpGlobals.CurTime;
			}
			else {
				if (player.GetHealth() >= TriggerHitPoints) {
					InputData inputdata = default;
					DevMsg(2, $"logic_active_autosave ({GetEntityName()}, {EntIndex()}) triggered\n");
					if (DangerousTime == 0)
						InputSave(inputdata);
					else {
						inputdata.Value.SetFloat(DangerousTime);
						InputSaveDangerous(inputdata);
					}
					StartTime = -1;
				}
				else if (TimeToTrigger > 0 && gpGlobals.CurTime - StartTime > TimeToTrigger)
					StartTime = -1;
			}
		}

		float thinkInterval = (StartTime < 0) ? 1.0f : 0.5f;
		SetNextThink(gpGlobals.CurTime + thinkInterval);
	}
}

[LinkEntityToClass("logic_collision_pair")]
public class LogicCollisionPair : LogicalEntity
{
	string? NameAttach1;
	string? NameAttach2;
	bool Disabled;
	bool Succeeded;

	public static readonly new DataMap DataDesc = new(typeof(LogicCollisionPair), LogicalEntity.DataDesc, [
		DEFINE<LogicCollisionPair>.KEYFIELD(nameof(NameAttach1), FieldType.String, "attach1"),
		DEFINE<LogicCollisionPair>.KEYFIELD(nameof(NameAttach2), FieldType.String, "attach2"),
		DEFINE<LogicCollisionPair>.KEYFIELD(nameof(Disabled), FieldType.Boolean, "startdisabled"),
		DEFINE<LogicCollisionPair>.FIELD(nameof(Succeeded), FieldType.Boolean),

		// Inputs
		DEFINE<LogicCollisionPair>.INPUTFUNC(FieldType.Void, "DisableCollisions", nameof(InputDisableCollisions), (INPUTFUNCPTR)((self, data) => ((LogicCollisionPair)self).InputDisableCollisions(data))),
		DEFINE<LogicCollisionPair>.INPUTFUNC(FieldType.Void, "EnableCollisions", nameof(InputEnableCollisions), (INPUTFUNCPTR)((self, data) => ((LogicCollisionPair)self).InputEnableCollisions(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	/// <summary>
	/// Finds the named physics object.  If no name, returns the world
	/// If a name is specified and an object not found - errors are reported
	/// </summary>
	public static IPhysicsObject? FindPhysicsObjectByNameOrWorld(string? name, BaseEntity errorEntity) {
		if (name == null)
			return g_PhysWorldObject;

		IPhysicsObject? physics = PhysicsHook.FindPhysicsObjectByName(name, errorEntity);
		if (physics == null)
			DevWarning($"{errorEntity.GetClassname()}: can't find {name}\n");
		return physics;
	}

	public void EnableCollisions(bool enable) {
		IPhysicsObject? physics0 = FindPhysicsObjectByNameOrWorld(NameAttach1, this);
		IPhysicsObject? physics1 = FindPhysicsObjectByNameOrWorld(NameAttach2, this);

		// need two different objects to do anything
		if (physics0 != null && physics1 != null && physics0 != physics1) {
			Disabled = !enable;
			Succeeded = true;
			if (enable)
				PhysEnableEntityCollisions(physics0, physics1);
			else
				PhysDisableEntityCollisions(physics0, physics1);
		}
		else
			Succeeded = false;
	}

	public override void Activate() {
		if (Disabled)
			EnableCollisions(false);
		base.Activate();
	}

	public void InputDisableCollisions(InputData inputdata) {
		if (Succeeded && Disabled)
			return;
		EnableCollisions(false);
	}

	public void InputEnableCollisions(InputData inputdata) {
		if (Succeeded && !Disabled)
			return;
		EnableCollisions(true);
	}
	// If Activate() becomes PostSpawn()
	//void OnRestore() { Activate(); }
}

[LinkEntityToClass("logic_branch_listener")]
public class LogicBranchList : LogicalEntity
{
	const int MAX_LOGIC_BRANCH_NAMES = 16;

	enum LogicBranchListenerLastState
	{
		NotInit = 0,
		AllTrue,
		AllFalse,
		Mixed,
	}

	string? LogicBranchName01, LogicBranchName02, LogicBranchName03, LogicBranchName04, LogicBranchName05, LogicBranchName06, LogicBranchName07, LogicBranchName08,
		LogicBranchName09, LogicBranchName10, LogicBranchName11, LogicBranchName12, LogicBranchName13, LogicBranchName14, LogicBranchName15, LogicBranchName16;
	readonly List<EHANDLE> List = [];
	LogicBranchListenerLastState LastState;

	// Outputs
	readonly OutputEvent OnAllTrue = new();           // Fired when all the registered logic_branches become true.
	readonly OutputEvent OnAllFalse = new();          // Fired when all the registered logic_branches become false.
	readonly OutputEvent OnMixed = new();             // Fired when one of the registered logic branches changes, but not all are true or false.

	string? LogicBranchName(int i) => i switch {
		0 => LogicBranchName01, 1 => LogicBranchName02, 2 => LogicBranchName03, 3 => LogicBranchName04,
		4 => LogicBranchName05, 5 => LogicBranchName06, 6 => LogicBranchName07, 7 => LogicBranchName08,
		8 => LogicBranchName09, 9 => LogicBranchName10, 10 => LogicBranchName11, 11 => LogicBranchName12,
		12 => LogicBranchName13, 13 => LogicBranchName14, 14 => LogicBranchName15, 15 => LogicBranchName16,
		_ => null
	};

	public static readonly new DataMap DataDesc = new(typeof(LogicBranchList), LogicalEntity.DataDesc, [
		// Silence, classcheck!
		//DEFINE_ARRAY( m_nLogicBranchNames, FIELD_STRING, MAX_LOGIC_BRANCH_NAMES ),

		// Keys
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName01), FieldType.String, "Branch01"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName02), FieldType.String, "Branch02"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName03), FieldType.String, "Branch03"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName04), FieldType.String, "Branch04"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName05), FieldType.String, "Branch05"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName06), FieldType.String, "Branch06"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName07), FieldType.String, "Branch07"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName08), FieldType.String, "Branch08"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName09), FieldType.String, "Branch09"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName10), FieldType.String, "Branch10"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName11), FieldType.String, "Branch11"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName12), FieldType.String, "Branch12"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName13), FieldType.String, "Branch13"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName14), FieldType.String, "Branch14"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName15), FieldType.String, "Branch15"),
		DEFINE<LogicBranchList>.KEYFIELD(nameof(LogicBranchName16), FieldType.String, "Branch16"),

		// DEFINE_UTLVECTOR( m_LogicBranchList, FIELD_EHANDLE ),

		DEFINE<LogicBranchList>.FIELD(nameof(LastState), FieldType.Integer),

		// Inputs
		DEFINE<LogicBranchList>.INPUTFUNC(FieldType.Input, "Test", nameof(InputTest), (INPUTFUNCPTR)((self, data) => ((LogicBranchList)self).InputTest(data))),
		DEFINE<LogicBranchList>.INPUTFUNC(FieldType.Input, "_OnLogicBranchChanged", nameof(Input_OnLogicBranchChanged), (INPUTFUNCPTR)((self, data) => ((LogicBranchList)self).Input_OnLogicBranchChanged(data))),
		DEFINE<LogicBranchList>.INPUTFUNC(FieldType.Input, "_OnLogicBranchRemoved", nameof(Input_OnLogicBranchRemoved), (INPUTFUNCPTR)((self, data) => ((LogicBranchList)self).Input_OnLogicBranchRemoved(data))),

		// Outputs
		DEFINE<LogicBranchList>.OUTPUT(nameof(OnAllTrue), "OnAllTrue", eventFuncs),
		DEFINE<LogicBranchList>.OUTPUT(nameof(OnAllFalse), "OnAllFalse", eventFuncs),
		DEFINE<LogicBranchList>.OUTPUT(nameof(OnMixed), "OnMixed", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	/// <summary>
	/// Called before spawning, after key values have been set.
	/// </summary>
	public override void Spawn() { }

	/// <summary>
	/// Finds all the logic_branches that we are monitoring and register ourselves with them.
	/// </summary>
	public override void Activate() {
		for (int i = 0; i < MAX_LOGIC_BRANCH_NAMES; i++) {
			BaseEntity? entity = null;
			while ((entity = gEntList.FindEntityGeneric(entity, LogicBranchName(i), this)) != null) {
				if (FClassnameIs(entity, "logic_branch")) {
					LogicBranch branch = (LogicBranch)entity;
					branch.AddLogicBranchListener(this);
					List.Add(branch.GetRefEHandle());
				}
				else
					DevWarning($"logic_branchlist {GetDebugName()} refers to entity {entity.GetDebugName()}, which is not a logic_branch\n");
			}
		}

		base.Activate();
	}

	/// <summary>
	/// Called when a monitored logic branch is deleted from the world, since that
	/// might affect our final result.
	/// </summary>
	void Input_OnLogicBranchRemoved(InputData inputdata) {
		int index = inputdata.Activator != null ? List.IndexOf(inputdata.Activator.GetRefEHandle()) : -1;
		if (index != -1) {
			List[index] = List[^1];
			List.RemoveAt(List.Count - 1);
		}

		// See if this logic_branch's deletion affects the final result.
		DoTest(inputdata.Activator);
	}

	/// <summary>
	/// Called when the value of a monitored logic branch changes.
	/// </summary>
	void Input_OnLogicBranchChanged(InputData inputdata) => DoTest(inputdata.Activator);

	/// <summary>
	/// Input handler to manually test the monitored logic branches and fire the
	/// appropriate output.
	/// </summary>
	void InputTest(InputData inputdata) {
		// Force an output.
		LastState = LogicBranchListenerLastState.NotInit;

		DoTest(inputdata.Activator);
	}

	void DoTest(BaseEntity? activator) {
		bool oneTrue = false;
		bool oneFalse = false;

		for (int i = 0; i < List.Count; i++) {
			LogicBranch? branch = (LogicBranch?)List[i].Get();
			if (branch != null && branch.GetLogicBranchState())
				oneTrue = true;
			else
				oneFalse = true;
		}

		// Only fire the output if the new result differs from the last result.
		if (oneTrue && !oneFalse) {
			if (LastState != LogicBranchListenerLastState.AllTrue) {
				OnAllTrue.FireOutput(activator, this);
				LastState = LogicBranchListenerLastState.AllTrue;
			}
		}
		else if (oneFalse && !oneTrue) {
			if (LastState != LogicBranchListenerLastState.AllFalse) {
				OnAllFalse.FireOutput(activator, this);
				LastState = LogicBranchListenerLastState.AllFalse;
			}
		}
		else {
			if (LastState != LogicBranchListenerLastState.Mixed) {
				OnMixed.FireOutput(activator, this);
				LastState = LogicBranchListenerLastState.Mixed;
			}
		}
	}

	public override int DrawDebugTextOverlays() {
		int text_offset = base.DrawDebugTextOverlays();

		if ((DebugOverlays & DebugOverlayBits.Text) != 0) {
			for (int i = 0; i < List.Count; i++) {
				LogicBranch? branch = (LogicBranch?)List[i].Get();
				if (branch != null) {
					EntityText(text_offset, $"Branch ({branch.GetEntityName()}): {(branch.GetLogicBranchState() ? "TRUE" : "FALSE")}", 0);
					text_offset++;
				}
			}
		}

		return text_offset;
	}
}
