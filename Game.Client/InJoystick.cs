using Source.Common.Commands;

namespace Game.Client;

public enum GameAxis
{
	None = 0,
	Forward,
	Pitch,
	Side,
	Yaw,
	Max
}

public enum JoystickAxis
{
	X = 0,
	Y,
	Z,
	R,
	U,
	V,
	Max
}

public struct JoyAxis
{
	public uint AxisMap;
	public uint ControlMap;
}

public partial class Input
{
	const uint JOY_ABSOLUTE_AXIS = 0x00000000;
	const uint JOY_RELATIVE_AXIS = 0x00000010;

	static readonly ConVar joy_name = new("joy_name", "joystick", FCvar.Archive);
	static readonly ConVar joy_advanced = new("joy_advanced", "1", FCvar.Archive);
	static readonly ConVar joy_advaxisx = new("joy_advaxisx", "4", FCvar.Archive);
	static readonly ConVar joy_advaxisy = new("joy_advaxisy", "2", FCvar.Archive);
	static readonly ConVar joy_advaxisz = new("joy_advaxisz", "0", FCvar.Archive);
	static readonly ConVar joy_advaxisr = new("joy_advaxisr", "1", FCvar.Archive);
	static readonly ConVar joy_advaxisu = new("joy_advaxisu", "3", FCvar.Archive);
	static readonly ConVar joy_advaxisv = new("joy_advaxisv", "0", FCvar.Archive);
	static readonly ConVar joy_movement_stick = new("joy_movement_stick", "0", FCvar.Archive | FCvar.ArchiveXbox, "Which stick controls movement (0 is left stick)", callback: joy_movement_stick_Callback);
	static readonly ConVar joy_xcontroller_cfg_loaded = new("joy_xcontroller_cfg_loaded", "0", FCvar.Archive, "If 0, the 360controller.cfg file will be executed on startup & option changes.");

	// todo: the rest of in_joystick.cpp (Init_Joystick, JoyStickMove and their ConVars)

	readonly JoyAxis[] Axes = new JoyAxis[(int)JoystickAxis.Max];
	static ConVarRef? joy_xcontroller_found;

	static void joy_movement_stick_Callback(IConVar var, in ConVarChangeContext ctx) {
		engine.ClientCmd("joyadvancedupdate");
	}

	static ReadOnlySpan<char> DescribeAxis(uint index) {
		switch ((GameAxis)index) {
			case GameAxis.Forward:
				return "Forward";
			case GameAxis.Pitch:
				return "Look";
			case GameAxis.Side:
				return "Side";
			case GameAxis.Yaw:
				return "Turn";
			case GameAxis.None:
			default:
				return "Unknown";
		}
	}

	static void DescribeJoystickAxis(string axis, in JoyAxis mapping) {
		if (mapping.AxisMap == 0)
			Msg("%s:  unmapped\n", axis);
		else
			Msg("%s:  mapped to %s (%s)\n", axis, DescribeAxis(mapping.AxisMap).ToString(), mapping.ControlMap != 0 ? "relative" : "absolute");
	}

	void SetJoystickAxis(JoystickAxis axis, string name, int value) {
		uint temp = unchecked((uint)value);
		Axes[(int)axis].AxisMap = temp & 0x0000000f;
		Axes[(int)axis].ControlMap = temp & JOY_RELATIVE_AXIS;

		DescribeJoystickAxis(name, in Axes[(int)axis]);
	}

	public void Joystick_Advanced() {
		for (int i = 0; i < (int)JoystickAxis.Max; i++) {
			Axes[i].AxisMap = (uint)GameAxis.None;
			Axes[i].ControlMap = JOY_ABSOLUTE_AXIS;
		}

		if (!joy_advanced.GetBool()) {
			Axes[(int)JoystickAxis.X].AxisMap = (uint)GameAxis.Yaw;
			Axes[(int)JoystickAxis.Y].AxisMap = (uint)GameAxis.Forward;
		}
		else {
			if (stricmp(joy_name.GetString(), "joystick") != 0)
				Msg("Using joystick '%s' configuration\n", joy_name.GetString());

			SetJoystickAxis(JoystickAxis.X, "JOY_AXIS_X", joy_movement_stick.GetBool() ? joy_advaxisu.GetInt() : joy_advaxisx.GetInt());
			SetJoystickAxis(JoystickAxis.Y, "JOY_AXIS_Y", joy_movement_stick.GetBool() ? joy_advaxisr.GetInt() : joy_advaxisy.GetInt());
			SetJoystickAxis(JoystickAxis.Z, "JOY_AXIS_Z", joy_advaxisz.GetInt());
			SetJoystickAxis(JoystickAxis.R, "JOY_AXIS_R", joy_movement_stick.GetBool() ? joy_advaxisy.GetInt() : joy_advaxisr.GetInt());
			SetJoystickAxis(JoystickAxis.U, "JOY_AXIS_U", joy_movement_stick.GetBool() ? joy_advaxisx.GetInt() : joy_advaxisu.GetInt());
			SetJoystickAxis(JoystickAxis.V, "JOY_AXIS_V", joy_advaxisv.GetInt());

			Msg("Advanced Joystick settings initialized\n");
		}

		ConVarRef found = joy_xcontroller_found ??= new("joy_xcontroller_found");
		if (found.IsValid() && found.GetBool() && in_joystick.GetBool()) {
			if (joy_xcontroller_cfg_loaded.GetInt() < 2) {
				engine.ClientCmd_Unrestricted("exec 360controller.cfg");
#if LINUX
				engine.ClientCmd_Unrestricted("exec 360controller-linux.cfg");
#endif
				joy_xcontroller_cfg_loaded.SetValue(2);
			}
		}
		else if (joy_xcontroller_cfg_loaded.GetInt() > 0) {
			engine.ClientCmd_Unrestricted("exec undo360controller.cfg");
			joy_xcontroller_cfg_loaded.SetValue(0);
		}
	}
}
