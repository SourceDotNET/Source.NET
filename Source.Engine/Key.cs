using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Input;

using System.Text;

namespace Source.Engine;

public struct KeyInfo
{
	public string? KeyBinding;
	public KeyUpTarget KeyUpTarget;
	public bool KeyDown;
}

public enum KeyUpTarget
{
	AnyTarget,
	Engine,
	VGui,
	Tools,
	Client
}

public class Key(IServiceProvider services, Cbuf Cbuf)
{
	readonly IInputSystem? inputSystem = OptionalSingleton<IInputSystem>();
	readonly IBaseClientDLL? clientDLL = OptionalSingleton<IBaseClientDLL>();

	readonly KeyInfo[] KeyInfo = new KeyInfo[(int)ButtonCode.Last];
	bool TrapMode = false;
	bool DoneTrapping = false;
	ButtonCode TrapKeyUp = ButtonCode.Invalid;
	ButtonCode TrapKey = ButtonCode.Invalid;
	delegate bool KeyFilterDelegate(in InputEvent ev);

	IEngineVGui? engineVGui;
	IEngineVGui EngineVGui => engineVGui ??= services.GetRequiredService<IEngineVGui>();

	public int CountBindings() {
		int i;
		int c = 0;

		for (i = 0; i < (int)ButtonCode.Last; i++) {
			if (string.IsNullOrEmpty(KeyInfo[i].KeyBinding))
				continue;
			c++;
		}
		return c;
	}

	public void WriteBindings(StringBuilder buf) {
		for (int i = 0; i < (int)ButtonCode.Last; i++) {
			string? keyBinding = KeyInfo[i].KeyBinding;
			if (string.IsNullOrEmpty(keyBinding))
				continue;

#if GMOD_DLL
			if (keyBinding.AsSpan().IndexOfAny("\"\n\r") >= 0)
				continue;
#endif

			buf.Append($"bind \"{inputSystem!.ButtonCodeToString((ButtonCode)i)}\" \"{keyBinding}\"\n");
		}
	}

	public ReadOnlySpan<char> NameForBinding(ReadOnlySpan<char> binding) {
		int i;

		ReadOnlySpan<char> bind = binding;
		if (binding[0] == '+')
			bind = bind[1..];

		for (i = 0; i < (int)ButtonCode.Last; i++) {
			string? keyBinding = KeyInfo[i].KeyBinding;
			if (!string.IsNullOrEmpty(keyBinding)) {
				if (keyBinding[0] == '+') {
					if (bind.Equals(keyBinding.AsSpan()[1..], StringComparison.OrdinalIgnoreCase))
						return inputSystem!.ButtonCodeToString((ButtonCode)i);
				}
				else {
					if (bind.Equals(keyBinding, StringComparison.OrdinalIgnoreCase))
						return inputSystem!.ButtonCodeToString((ButtonCode)i);
				}

			}
		}

		return null;
	}

	public void SetBinding(ButtonCode keynum, ReadOnlySpan<char> cmd) {
		if (keynum == ButtonCode.Invalid)
			return;

		string? current = KeyInfo[(int)keynum].KeyBinding;
		if (current != null && streq(current, cmd))
			return;

		KeyInfo[(int)keynum].KeyBinding = new(cmd);
	}

	public void Event(in InputEvent ev) {
#if SWDS
		return;
#endif
		bool down = ev.Type != InputEventType.IE_ButtonReleased;
		ButtonCode code = (ButtonCode)ev.Data;

		if (KeyInfo[(int)code].KeyDown == down)
			return;

		KeyInfo[(int)code].KeyDown = down;

		if (FilterTrappedKey(code, down))
			return;

		EngineVGui.UpdateButtonState(in ev);

		if (FilterKey(in ev, KeyUpTarget.Tools, HandleToolKey))
			return;

		if (FilterKey(in ev, KeyUpTarget.VGui, HandleVGuiKey))
			return;

		if (FilterKey(in ev, KeyUpTarget.Client, HandleClientKey))
			return;

		FilterKey(in ev, KeyUpTarget.Engine, HandleEngineKey);
	}
	public bool HandleToolKey(in InputEvent ev) {
		// TODO: Tools
		return false;
	}

	public bool HandleVGuiKey(in InputEvent ev) {
		bool down = ev.Type != InputEventType.IE_ButtonReleased;
		ButtonCode code = (ButtonCode)ev.Data;

		return EngineVGui.Key_Event(in ev);
	}

	public bool HandleClientKey(in InputEvent ev) {
		bool down = ev.Type != InputEventType.IE_ButtonReleased;
		return clientDLL != null && clientDLL.IN_KeyEvent(down ? 1 : 0, (ButtonCode)ev.Data, KeyInfo[ev.Data].KeyBinding) == 0;
	}

	public bool HandleEngineKey(in InputEvent ev) {
		bool down = ev.Type != InputEventType.IE_ButtonReleased;
		ButtonCode code = (ButtonCode)ev.Data;

#if !SWDS
		if (code == ButtonCode.MouseWheelUp || code == ButtonCode.MouseWheelDown)
			clientDLL!.IN_OnMouseWheeled(code == ButtonCode.MouseWheelUp ? 1 : -1);
#endif

		ReadOnlySpan<char> kb = KeyInfo[(int)code].KeyBinding;
		if (kb.IsEmpty || kb.Length <= 0)
			return false;

		Span<char> cmd = stackalloc char[1024];
		if (!down) {
			if (kb[0] == '+') {
				sprintf(cmd, "-%s %i\n").S(kb[1..]).I((int)code);
				Cbuf.AddText(cmd);
				return true;
			}

			return false;
		}

		if (kb[0] == '+') {
			sprintf(cmd, "%s %i\n").S(kb).I((int)code);
			Cbuf.AddText(cmd);
			return true;
		}

		if (kb.Equals("toggleconsole", StringComparison.OrdinalIgnoreCase))
			if (KeyInfo[(int)ButtonCode.KeyLAlt].KeyDown || KeyInfo[(int)ButtonCode.KeyLShift].KeyDown || KeyInfo[(int)ButtonCode.KeyLControl].KeyDown ||
				KeyInfo[(int)ButtonCode.KeyRAlt].KeyDown || KeyInfo[(int)ButtonCode.KeyRShift].KeyDown || KeyInfo[(int)ButtonCode.KeyRControl].KeyDown)
				return false;

		Cbuf.AddText(kb);
		Cbuf.AddText("\n");

		return true;
	}

	private bool FilterKey(in InputEvent ev, KeyUpTarget target, KeyFilterDelegate func) {
		bool down = ev.Type != InputEventType.IE_ButtonReleased;
		ButtonCode code = (ButtonCode)ev.Data;

		if (!down && !ShouldPassKeyUpToTarget(code, target))
			return false;

		bool filtered = func(in ev);

		if (down) {
			if (filtered) {
				Assert(KeyInfo[(int)code].KeyUpTarget == KeyUpTarget.AnyTarget);
				KeyInfo[(int)code].KeyUpTarget = target;
			}
		}
		else {
			if (KeyInfo[(int)code].KeyUpTarget == target) {
				KeyInfo[(int)code].KeyUpTarget = KeyUpTarget.AnyTarget;
				filtered = true;
			}
			else {
				Assert(!filtered);
			}
		}

		return filtered;
	}

	private bool ShouldPassKeyUpToTarget(ButtonCode code, KeyUpTarget target)
		=> (KeyInfo[(int)code].KeyUpTarget == target) || (KeyInfo[(int)code].KeyUpTarget == KeyUpTarget.AnyTarget);

	private bool FilterTrappedKey(ButtonCode code, bool down) {
		if (TrapKeyUp == code && !down) {
			TrapKeyUp = ButtonCode.Invalid;
			return true;
		}

		if (TrapMode && down) {
			TrapKey = code;
			TrapMode = false;
			DoneTrapping = true;
			TrapKeyUp = code;
			return true;
		}

		return false;
	}
	[ConCommand(flags: FCvar.DontRecord, helpText: "Bind a key.")]
	void bind(in TokenizedCommand args) {
		Span<char> cmd = stackalloc char[1024];
		int c = args.ArgC();
		if (c != 2 && c != 3) {
			ConMsg("bind <key> [command] : attach a command to a key\n");
			return;
		}

		BindKey(args[1], c == 2, cmd[..strcpy(cmd, args[2])]);
	}
	[ConCommand(flags: FCvar.DontRecord, helpText: "Unbind a key.")]
	void unbind(in TokenizedCommand args) {
		if (args.ArgC() != 2) {
			ConMsg("unbind <key> : remove commands from a key\n");
			return;
		}

		if (inputSystem == null)
			return;

		ButtonCode b = inputSystem.StringToButtonCode(args[1]);
		if (b == ButtonCode.Invalid) {
			ConMsg($"\"{args[1]}\" isn't a valid key\n");
			return;
		}
		if (b == ButtonCode.KeyEscape) {
			ConMsg("Can't unbind ESCAPE key\n");
			return;
		}

		SetBinding(b, "");
	}

	[ConCommand(flags: FCvar.DontRecord, helpText: "Bind this key but only on Mac, not win32")]
	void bind_mac(in TokenizedCommand args) {
#if OSX
		Span<char> cmd = stackalloc char[1024];
		int c = args.ArgC();
		if (c != 2 && c != 3) {
			ConMsg("bind <key> [command] : attach a command to a key\n");
			return;
		}

		BindKey(args[1], c == 2, cmd[..strcpy(cmd, args[2])]);
#endif
	}

	[ConCommand(flags: FCvar.DontRecord, helpText: "Unbind a key on the Mac only.")]
	void unbind_mac(in TokenizedCommand args) {
#if OSX
		unbind(in args);
#endif
	}

	[ConCommand(flags: FCvar.DontRecord, helpText: "Unbind all keys.")]
	void unbindall(in TokenizedCommand args) {
		for (int i = 0; i < (int)ButtonCode.Last; i++) {
			if (KeyInfo[i].KeyBinding == null)
				continue;

			if (i == (int)ButtonCode.KeyEscape)
				continue;

			if (i == (int)ButtonCode.KeyBackquote)
				continue;

			SetBinding((ButtonCode)i, "");
		}
	}

	[ConCommand(helpText: "List bound keys with bindings.")]
	void key_listboundkeys(in TokenizedCommand args) {
		for (int i = 0; i < (int)ButtonCode.Last; i++) {
			string? keyBinding = KeyInfo[i].KeyBinding;
			if (!string.IsNullOrEmpty(keyBinding))
				ConMsg($"\"{inputSystem!.ButtonCodeToString((ButtonCode)i)}\" = \"{keyBinding}\"\n");
		}
	}

	public void BindKey(ReadOnlySpan<char> bind, bool show, ReadOnlySpan<char> cmd) {
		if (inputSystem == null)
			return;

		ButtonCode code = inputSystem.StringToButtonCode(bind);
		if (code == ButtonCode.Invalid) {
			ConMsg($"\"{bind}\" isn't a valid key\n");
			return;
		}

		if (show) {
			if (KeyInfo[(int)code].KeyBinding != null)
				ConMsg($"\"{bind}\" = \"{KeyInfo[(int)code].KeyBinding}\"\n");
			else
				ConMsg($"\"{bind}\" is not bound\n");

			return;
		}

		if (code == ButtonCode.KeyEscape)
			cmd = "cancelselect";

		SetBinding(code, cmd);
	}

	internal void Shutdown() {

	}

	public string? BindingForKey(ButtonCode code) {
		if (code < 0 || code >= ButtonCode.Last)
			return null;

		return KeyInfo[(int)code].KeyBinding;
	}

	internal ReadOnlySpan<char> NameForBindingExact(ReadOnlySpan<char> binding) {
		ButtonCode i;

		for (i = 0; i < ButtonCode.Last; i++)
			if (!string.IsNullOrEmpty(KeyInfo[(int)i].KeyBinding))
				if (0 == stricmp(KeyInfo[(int)i].KeyBinding, binding))
					return inputSystem == null ? null : inputSystem.ButtonCodeToString(i);

		return null;
	}

	public void StartTrapMode() {
		if (TrapMode)
			return;

		Assert(!DoneTrapping && TrapKeyUp == ButtonCode.Invalid);

		DoneTrapping = false;
		TrapMode = true;
		TrapKeyUp = ButtonCode.Invalid;
	}

	public bool CheckDoneTrapping(out ButtonCode code) {
		code = ButtonCode.Invalid;

		if (TrapMode)
			return false;

		if (!DoneTrapping)
			return false;

		code = TrapKey;
		TrapKey = ButtonCode.Invalid;
		DoneTrapping = false;

		return true;
	}
}
