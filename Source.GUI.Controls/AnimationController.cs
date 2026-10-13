using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Mathematics;
using Source.Common.Utilities;

using System.Text;

using static Source.GUI.Controls.AnimationController;

namespace Source.GUI.Controls;

public enum Interpolators
{
	Linear,
	Accel,
	Deaccel,
	Pulse,
	Flicker,
	SimpleSpline,
	Bounce,
	Bias,
	Gain
}

// Animation controller singleton. But is a panel to receive messages.

public struct AnimValue
{
	public float A;
	public float B;
	public float C;
	public float D;
}

public enum AnimCommandType
{
	Animate,
	RunEvent,
	StopEvent,
	StopAnimation,
	StopPanelAnimations,
	SetFont,
	SetTexture,
	SetString,
	RunEventChild,
	FireCommand,
	PlaySound,
	SetVisible,
	SetInputEnabled,
}

public struct AnimAlign
{
	public bool RelativePosition;
	public UtlSymId_t AlignPanel;
	public Alignment Alignment;
}

public struct AnimCmdAnimate
{
	public UtlSymId_t Panel;
	public UtlSymId_t Variable;
	public AnimValue Target;
	public Interpolators InterpolationFunction;
	public float InterpolationParameter;
	public TimeUnit_t StartTime;
	public TimeUnit_t Duration;
	public AnimAlign Align;
}



public struct ActiveAnimation
{
	public Panel? Panel;
	public UtlSymId_t SeqName;
	public UtlSymId_t Variable;
	public bool Started;
	public AnimValue StartValue;
	public AnimValue EndValue;
	public Interpolators Interpolator;
	public float InterpolatorParam;
	public TimeUnit_t StartTime;
	public TimeUnit_t EndTime;
	public bool CanBeCancelled;
	public AnimAlign Align;
}

public struct PostedMessage
{
	public AnimCommandType CommandType;
	public UtlSymId_t SeqName;
	public UtlSymId_t Event;
	public UtlSymId_t Variable;
	public UtlSymId_t Variable2;
	public double StartTime;
	public Panel Parent;
	public bool CanBeCancelled;
}

public struct RanEvent
{
	public UtlSymId_t SeqName;
	public Panel Parent;
	public override bool Equals(object? obj) => obj is RanEvent other && SeqName == other.SeqName && Parent == other.Parent;
	public override int GetHashCode() => HashCode.Combine(SeqName, Parent);
}

public class AnimationController : Panel, IAnimationController
{
	List<ActiveAnimation> ActiveAnimations = [];
	List<PostedMessage> PostedMessages = [];

	ulong Position;
	ulong Size;
	ulong FgColor;
	ulong BgColor;
	ulong XPos;
	ulong YPos;
	ulong Wide;
	ulong Tall;
	int[] ScreenBounds = new int[4];

	// Static instance
	public AnimationController() {
		Init();
	}
	// Dynamic instance
	public AnimationController(Panel? parent) : base(parent, null) {
		Init();
	}

	public void Init() {
		ScreenBounds[0] = -1;
		ScreenBounds[1] = -1;
		ScreenBounds[2] = -1;
		ScreenBounds[3] = -1;

		SetVisible(false);
		SetProportional(true);

		Position = ScriptSymbols.AddString("position");
		Size = ScriptSymbols.AddString("size");
		FgColor = ScriptSymbols.AddString("fgcolor");
		BgColor = ScriptSymbols.AddString("bgcolor");
		XPos = ScriptSymbols.AddString("xpos");
		YPos = ScriptSymbols.AddString("ypos");
		Wide = ScriptSymbols.AddString("wide");
		Tall = ScriptSymbols.AddString("tall");

		CurrentTime = 0;
	}

	public void RunAnimationCommand(Panel panel, ReadOnlySpan<char> variable, float target, TimeUnit_t startDelaySeconds, TimeUnit_t durationSeconds, Interpolators interpolator, float animParameter = 0) {
		ulong var = ScriptSymbols.AddString(variable);
		RemoveQueuedAnimationByType(panel, var, UTL_INVAL_SYMBOL);

		AnimCmdAnimate animateCmd = new();
		animateCmd.Panel = 0;
		animateCmd.Variable = var;
		animateCmd.Target.A = target;
		animateCmd.InterpolationFunction = interpolator;
		animateCmd.InterpolationParameter = animParameter;
		animateCmd.StartTime = startDelaySeconds;
		animateCmd.Duration = durationSeconds;

		StartCmd_Animate(panel, 0, in animateCmd, true);
	}

	public void RunAnimationCommand(Panel panel, ReadOnlySpan<char> variable, Color target, TimeUnit_t startDelaySeconds, TimeUnit_t durationSeconds, Interpolators interpolator, float animParameter = 0) {
		ulong var = ScriptSymbols.AddString(variable);
		RemoveQueuedAnimationByType(panel, var, UTL_INVAL_SYMBOL);

		AnimCmdAnimate animateCmd = new();
		animateCmd.Panel = 0;
		animateCmd.Variable = var;
		animateCmd.Target.A = target[0];
		animateCmd.Target.B = target[1];
		animateCmd.Target.C = target[2];
		animateCmd.Target.D = target[3];
		animateCmd.InterpolationFunction = interpolator;
		animateCmd.InterpolationParameter = animParameter;
		animateCmd.StartTime = startDelaySeconds;
		animateCmd.Duration = durationSeconds;

		StartCmd_Animate(panel, 0, in animateCmd, true);
	}

	TimeUnit_t CurrentTime;

	public void UpdateAnimations(TimeUnit_t curTime) {
		CurrentTime = curTime;

		if (UpdateScreenSize() && ScriptFileNames.Count > 0) {
			RunAllAnimationsToCompletion();
			ReloadScriptFile();
		}

		UpdatePostedMessages(false);
		UpdateActiveAnimations(false);
	}

	private void RunAllAnimationsToCompletion() {
		UpdatePostedMessages(true);
		UpdateActiveAnimations(true);
	}

	private void UpdatePostedMessages(bool runToCompletion) {
		List<RanEvent> eventsRanThisFrame = [];

		for (int i = 0; i < PostedMessages.Count; i++) {
			Span<PostedMessage> msgs = PostedMessages.AsSpan();
			ref PostedMessage msgRef = ref msgs[i];

			if (!msgRef.CanBeCancelled && runToCompletion)
				continue;

			if (CurrentTime < msgRef.StartTime && !runToCompletion)
				continue;

			PostedMessage msg = msgRef;
			PostedMessages.RemoveAt(i);
			i = -1;

			if (!msg.Parent.IsValid())
				continue;

			switch (msg.CommandType) {
				case AnimCommandType.RunEvent: {
						RanEvent curEvent = new() {
							SeqName = msg.Event,
							Parent = msg.Parent
						};

						if (!eventsRanThisFrame.Contains(curEvent)) {
							eventsRanThisFrame.Add(curEvent);
							RunCmd_RunEvent(msg);
						}
					}
					break;
				case AnimCommandType.RunEventChild: {
						RanEvent curEvent = new() {
							SeqName = msg.Event,
							Parent = msg.Parent.FindChildByName(ScriptSymbols.String(msg.Variable), true)!
						};

						msg.Parent = curEvent.Parent;

						if (!eventsRanThisFrame.Contains(curEvent)) {
							eventsRanThisFrame.Add(curEvent);
							RunCmd_RunEvent(msg);
						}
					}
					break;
				case AnimCommandType.FireCommand: {
						msg.Parent.OnCommand(ScriptSymbols.String(msg.Variable));
					}
					break;
				case AnimCommandType.PlaySound:
					Surface.PlaySound(ScriptSymbols.String(msg.Variable));
					break;
				case AnimCommandType.SetVisible: {
						Panel? panel = msg.Parent.FindChildByName(ScriptSymbols.String(msg.Variable), true)!;
						panel?.SetVisible(msg.Variable2 == 1);
					}
					break;
				case AnimCommandType.SetInputEnabled: {
						Panel? panel = msg.Parent.FindChildByName(ScriptSymbols.String(msg.Variable), true)!;
						if (panel != null) {
							panel.SetMouseInputEnabled(msg.Variable2 == 1);
							panel.SetKeyboardInputEnabled(msg.Variable2 == 1);
						}
					}
					break;
				case AnimCommandType.StopEvent:
					RunCmd_StopEvent(msg);
					break;
				case AnimCommandType.StopPanelAnimations:
					RunCmd_StopPanelAnimations(msg);
					break;
				case AnimCommandType.StopAnimation:
					RunCmd_StopAnimation(msg);
					break;
				case AnimCommandType.SetFont:
					RunCmd_SetFont(msg);
					break;
				case AnimCommandType.SetTexture:
					RunCmd_SetTexture(msg);
					break;
				case AnimCommandType.SetString:
					RunCmd_SetString(msg);
					break;
			}
		}
	}

	public void CancelAllAnimations() {
		for (int i = ActiveAnimations.Count - 1; i >= 0; i--)
			if (ActiveAnimations[i].CanBeCancelled)
				ActiveAnimations.RemoveAt(i);

		for (int i = PostedMessages.Count - 1; i >= 0; i--)
			if (PostedMessages[i].CanBeCancelled)
				PostedMessages.RemoveAt(i);
	}

	private void UpdateActiveAnimations(bool runToCompletion) {
		for (int i = 0; i < ActiveAnimations.Count; i++) {
			Span<ActiveAnimation> anims = ActiveAnimations.AsSpan();
			ref ActiveAnimation anim = ref anims[i];

			if (!anim.CanBeCancelled && runToCompletion)
				continue;

			if (CurrentTime < anim.StartTime && !runToCompletion)
				continue;

			if (!anim.Panel.IsValid()) {
				ActiveAnimations.RemoveAt(i);
				--i;
				continue;
			}

			if (!anim.Started && !runToCompletion) {
				anim.StartValue = GetValue(anim, anim.Panel, anim.Variable);
				anim.Started = true;
			}

			AnimValue val;
			if (CurrentTime >= anim.EndTime || runToCompletion)
				val = anim.EndValue;
			else
				val = GetInterpolatedValue(anim.Interpolator, anim.InterpolatorParam, CurrentTime, anim.StartTime, anim.EndTime, in anim.StartValue, in anim.EndValue);

			SetValue(anim, anim.Panel, anim.Variable, val);

			if (CurrentTime >= anim.EndTime || runToCompletion) {
				ActiveAnimations.RemoveAt(i);
				--i;
			}
		}
	}

	private void SetValue(ActiveAnimation anim, Panel panel, ulong variable, AnimValue value) {
		if (variable == Position) {
			int x = (int)value.A + GetRelativeOffset(anim.Align, true);
			int y = (int)value.B + GetRelativeOffset(anim.Align, false);
			panel.SetPos(x, y);
		}
		else if (variable == Size)
			panel.SetSize((int)value.A, (int)value.B);
		else if (variable == FgColor) {
			Color col = panel.GetFgColor();
			col[0] = (byte)Math.Clamp((int)value.A, 0, 255);
			col[1] = (byte)Math.Clamp((int)value.B, 0, 255);
			col[2] = (byte)Math.Clamp((int)value.C, 0, 255);
			col[3] = (byte)Math.Clamp((int)value.D, 0, 255);
			panel.SetFgColor(col);
		}
		else if (variable == BgColor) {
			Color col = panel.GetBgColor();
			col[0] = (byte)Math.Clamp((int)value.A, 0, 255);
			col[1] = (byte)Math.Clamp((int)value.B, 0, 255);
			col[2] = (byte)Math.Clamp((int)value.C, 0, 255);
			col[3] = (byte)Math.Clamp((int)value.D, 0, 255);
			panel.SetBgColor(col);
		}
		else if (variable == XPos)
			panel.SetPos((int)value.A + GetRelativeOffset(anim.Align, true), panel.GetY());
		else if (variable == YPos)
			panel.SetPos(panel.GetX(), (int)value.A + GetRelativeOffset(anim.Align, false));
		else if (variable == Wide)
			panel.SetSize((int)value.A, panel.GetTall());
		else if (variable == Tall)
			panel.SetSize(panel.GetWide(), (int)value.A);
		else {
			string name = ScriptSymbols.String(variable)!;
			KeyValues inputData = new(name);
			if (value.B == 0.0f && value.C == 0.0f && value.D == 0.0f) {
				inputData.SetFloat(name, value.A);
			}
			else {
				Color col = new();
				col[0] = (byte)Math.Clamp((int)value.A, 0, 255);
				col[1] = (byte)Math.Clamp((int)value.B, 0, 255);
				col[2] = (byte)Math.Clamp((int)value.C, 0, 255);
				col[3] = (byte)Math.Clamp((int)value.D, 0, 255);
				inputData.SetColor(name, col);
			}

			panel.SetInfo(inputData);
		}
	}

	private AnimValue GetValue(ActiveAnimation anim, Panel panel, ulong variable) {
		AnimValue val = new();
		if (variable == Position) {
			panel.GetPos(out int x, out int y);
			val.A = x - GetRelativeOffset(in anim.Align, true);
			val.B = y - GetRelativeOffset(in anim.Align, false);
		}
		else if (variable == Size) {
			panel.GetSize(out int w, out int h);
			val.A = w;
			val.B = h;
		}
		else if (variable == FgColor) {
			Color col = panel.GetFgColor();
			val.A = col[0];
			val.B = col[1];
			val.C = col[2];
			val.D = col[3];
		}
		else if (variable == BgColor) {
			Color col = panel.GetBgColor();
			val.A = col[0];
			val.B = col[1];
			val.C = col[2];
			val.D = col[3];
		}
		else if (variable == XPos) {
			panel.GetPos(out int x, out _);
			val.A = x - GetRelativeOffset(in anim.Align, true);
		}
		else if (variable == YPos) {
			panel.GetPos(out _, out int y);
			val.A = y - GetRelativeOffset(in anim.Align, false);
		}
		else if (variable == Wide) {
			panel.GetSize(out int w, out _);
			val.A = w;
		}
		else if (variable == Tall) {
			panel.GetSize(out _, out int h);
			val.A = h;
		}
		else {
			KeyValues outputData = new KeyValues(ScriptSymbols.String(variable));
			if (panel.RequestInfo(outputData)) {
				KeyValues? kv = outputData.FindKey(ScriptSymbols.String(variable));
				if (kv != null && kv.Type == KeyValues.Types.Double) {
					val.A = kv.GetFloat();
					val.B = 0.0f;
					val.C = 0.0f;
					val.D = 0.0f;
				}
				else if (kv != null && kv.Type == KeyValues.Types.Color) {
					Color col = kv.GetColor();
					val.A = col[0];
					val.B = col[1];
					val.C = col[2];
					val.D = col[3];
				}
			}
		}
		return val;
	}

	private int GetRelativeOffset(in AnimAlign align, bool xcoord) {
		if (!align.RelativePosition)
			return 0;

		Panel? panel = GetParent()?.FindChildByName(ScriptSymbols.String(align.AlignPanel), true);
		if (panel == null)
			return 0;
		panel.GetBounds(out int x, out int y, out int w, out int h);

		int offset;
		offset = align.Alignment switch {
			Alignment.North => xcoord ? (x + w) / 2 : y,
			Alignment.Northeast => xcoord ? (x + w) : y,
			Alignment.West => xcoord ? x : (y + h) / 2,
			Alignment.Center => xcoord ? (x + w) / 2 : (y + h) / 2,
			Alignment.East => xcoord ? (x + w) : (y + h) / 2,
			Alignment.Southwest => xcoord ? x : (y + h),
			Alignment.South => xcoord ? (x + w) / 2 : (y + h),
			Alignment.Southeast => xcoord ? (x + w) : (y + h),
			_ => xcoord ? x : y,
		};
		return offset;
	}

	private AnimValue GetInterpolatedValue(Interpolators interpolator, float interpolatorParam, double currentTime, double startTime, double endTime, in AnimValue startValue, in AnimValue endValue) {
		double pos = (currentTime - startTime) / (endTime - startTime);

		switch (interpolator) {
			case Interpolators.Accel:
				pos *= pos;
				break;
			case Interpolators.Deaccel:
				pos = Math.Sqrt(pos);
				break;
			case Interpolators.SimpleSpline:
				pos = MathLib.SimpleSpline(pos);
				break;
			case Interpolators.Pulse:
				pos = 0.5f + 0.5f * (Math.Cos(pos * 2.0f * Math.PI * interpolatorParam));
				break;
			case Interpolators.Bias:
				pos = MathLib.Bias(pos, interpolatorParam);
				break;
			case Interpolators.Gain:
				pos = MathLib.Gain(pos, interpolatorParam);
				break;
			case Interpolators.Flicker:
				if (Random.Shared.NextSingle() < interpolatorParam) {
					pos = 1.0f;
				}
				else {
					pos = 0.0f;
				}
				break;
			case Interpolators.Bounce:
				const double hit1 = 0.33;
				const double hit2 = 0.67;
				const double hit3 = 1.0;

				if (pos < hit1) {
					pos = 1.0f - Math.Sin(Math.PI * pos / hit1);
				}
				else if (pos < hit2) {
					pos = 0.5f + 0.5f * (1.0f - Math.Sin(Math.PI * (pos - hit1) / (hit2 - hit1)));
				}
				else {
					pos = 0.8f + 0.2f * (1.0f - Math.Sin(Math.PI * (pos - hit2) / (hit3 - hit2)));
				}
				break;
			case Interpolators.Linear:
			default:
				break;
		}

		AnimValue val;
		val.A = (float)(((endValue.A - startValue.A) * pos) + startValue.A);
		val.B = (float)(((endValue.B - startValue.B) * pos) + startValue.B);
		val.C = (float)(((endValue.C - startValue.C) * pos) + startValue.C);
		val.D = (float)(((endValue.D - startValue.D) * pos) + startValue.D);
		return val;
	}

	private void StartCmd_Animate(Panel panel, ulong seqName, in AnimCmdAnimate cmd, bool canBeCancelled) {
		ActiveAnimations.Add(new());

		Span<ActiveAnimation> anims = ActiveAnimations.AsSpan();
		ref ActiveAnimation anim = ref anims[ActiveAnimations.Count - 1];

		anim.Panel = panel;
		anim.SeqName = seqName;
		anim.Variable = cmd.Variable;
		anim.Interpolator = cmd.InterpolationFunction;
		anim.InterpolatorParam = cmd.InterpolationParameter;
		anim.StartTime = CurrentTime + cmd.StartTime;
		anim.EndTime = anim.StartTime + cmd.Duration;
		anim.Started = false;
		anim.EndValue = cmd.Target;

		anim.CanBeCancelled = canBeCancelled;

		anim.Align = cmd.Align;
	}

	void RunCmd_RunEvent(PostedMessage msg) => StartAnimationSequence(msg.Parent, ScriptSymbols.String(msg.Event), msg.CanBeCancelled);
	void RunCmd_StopEvent(PostedMessage msg) => RemoveQueuedAnimationCommands(msg.Event, msg.Parent);

	void RunCmd_StopPanelAnimations(PostedMessage msg) {
		Panel? panel = FindSiblingByName(ScriptSymbols.String(msg.Event));
		Assert(panel != null);
		if (panel == null)
			return;

		for (int i = 0; i < ActiveAnimations.Count; i++) {
			Span<ActiveAnimation> anims = ActiveAnimations.AsSpan();
			ref ActiveAnimation anim = ref anims[i];
			if (anim.Panel == panel && anim.SeqName != msg.SeqName) {
				ActiveAnimations.RemoveAt(i);
				--i;
			}
		}
	}

	void RunCmd_StopAnimation(PostedMessage msg) {
		Panel? panel = FindSiblingByName(ScriptSymbols.String(msg.Event));
		Assert(panel != null);
		if (panel == null)
			return;

		RemoveQueuedAnimationByType(panel, msg.Variable, msg.SeqName);
	}

	void RunCmd_SetFont(PostedMessage msg) {
		Panel parent = msg.Parent;
		parent ??= GetParent()!;

		Panel? panel = parent.FindChildByName(ScriptSymbols.String(msg.Event), true);
		Assert(panel != null);
		if (panel == null)
			return;

		KeyValues inputData = new(ScriptSymbols.String(msg.Variable));
		inputData.SetString(ScriptSymbols.String(msg.Variable), ScriptSymbols.String(msg.Variable2));
		panel.SetInfo(inputData);
	}

	void RunCmd_SetTexture(PostedMessage msg) {
		Panel? panel = FindSiblingByName(ScriptSymbols.String(msg.Event));
		Assert(panel != null);
		if (panel == null)
			return;

		KeyValues inputData = new(ScriptSymbols.String(msg.Variable));
		inputData.SetString(ScriptSymbols.String(msg.Variable), ScriptSymbols.String(msg.Variable2));
		panel.SetInfo(inputData);
	}

	void RunCmd_SetString(PostedMessage msg) {
		Panel? panel = FindSiblingByName(ScriptSymbols.String(msg.Event));
		Assert(panel != null);
		if (panel == null)
			return;

		KeyValues inputData = new(ScriptSymbols.String(msg.Variable));
		inputData.SetString(ScriptSymbols.String(msg.Variable), ScriptSymbols.String(msg.Variable2));
		panel.SetInfo(inputData);
	}

	private void RemoveQueuedAnimationByType(Panel panel, ulong variable, ulong sequenceToIgnore) {
		Span<ActiveAnimation> anims = ActiveAnimations.AsSpan();
		for (int i = 0; i < anims.Length; i++) {
			ref ActiveAnimation anim = ref anims[i];
			if (anim.Panel == panel && anim.Variable == variable && anim.SeqName != sequenceToIgnore) {
				ActiveAnimations.RemoveAt(i);
				break;
			}
		}
	}

	static readonly UtlSymbolTable ScriptSymbols = new(true);
	IPanel? SizePanel;

	public bool StartAnimationSequence(ReadOnlySpan<char> sequenceName, bool canBeCancelled = true) {
		return StartAnimationSequence(GetParent(), sequenceName, canBeCancelled);
	}

	public struct AnimCmdEvent
	{
		public UtlSymId_t Event;
		public UtlSymId_t Variable;
		public UtlSymId_t Variable2;
		public TimeUnit_t TimeDelay;
	}

	public struct AnimCommand
	{
		public AnimCommandType CommandType;
		public AnimCmdAnimate Animate;
		public AnimCmdEvent RunEvent;
	}

	public struct AnimSequence()
	{
		public UtlSymId_t Name;
		public TimeUnit_t Duration;
		public List<AnimCommand> CmdList = [];
	}
	public bool AutoReloadScript;
	readonly List<AnimSequence> Sequences = [];

	public bool StartAnimationSequence(Panel withinParent, ReadOnlySpan<char> sequenceName, bool canBeCancelled = true) {
		if (AutoReloadScript)
			ReloadScriptFile();

		UtlSymId_t seqName = ScriptSymbols.Find(sequenceName);
		if (seqName == UTL_INVAL_SYMBOL)
			return false;

		RemoveQueuedAnimationCommands(seqName, withinParent);

		int i;
		for (i = 0; i < Sequences.Count; i++) {
			if (Sequences[i].Name == seqName)
				break;
		}

		if (i >= Sequences.Count)
			return false;

		for (int cmdIndex = 0; cmdIndex < Sequences[i].CmdList.Count; cmdIndex++)
			ExecAnimationCommand(seqName, ref Sequences[i].CmdList.AsSpan()[cmdIndex], withinParent, canBeCancelled);

		return true;
	}

	private void ExecAnimationCommand(ulong seqName, ref AnimCommand animCommand, Panel withinParent, bool canBeCancelled) {
		if (animCommand.CommandType == AnimCommandType.Animate) {
			StartCmd_Animate(seqName, ref animCommand.Animate, withinParent, canBeCancelled);
		}
		else {
			PostedMessages.Add(default);
			ref PostedMessage msg = ref PostedMessages.AsSpan()[PostedMessages.Count - 1];
			msg.SeqName = seqName;
			msg.CommandType = animCommand.CommandType;
			msg.Event = animCommand.RunEvent.Event;
			msg.Variable = animCommand.RunEvent.Variable;
			msg.Variable2 = animCommand.RunEvent.Variable2;
			msg.StartTime = CurrentTime + animCommand.RunEvent.TimeDelay;
			msg.Parent = withinParent;
			msg.CanBeCancelled = canBeCancelled;
		}
	}

	private void StartCmd_Animate(UtlSymId_t seqName, ref AnimCmdAnimate cmd, Panel withinParent, bool canBeCancelled) {
		if (withinParent == null)
			return;

		Panel? panel = withinParent.FindChildByName(ScriptSymbols.String(cmd.Panel), true);
		if (panel == null) {
			Panel? parent = GetParent();
			if (parent != null && parent.GetName().Equals(ScriptSymbols.String(cmd.Panel), StringComparison.OrdinalIgnoreCase))
				panel = parent;
		}

		if (panel == null)
			return;

		StartCmd_Animate(panel, seqName, cmd, canBeCancelled);
	}

	private void RemoveQueuedAnimationCommands(UtlSymId_t seqName, Panel withinParent) {
		for (int i = 0; i < PostedMessages.Count; i++) {
			if ((PostedMessages[i].SeqName == seqName) && (withinParent == null || (PostedMessages[i].Parent == withinParent))) {
				PostedMessages.RemoveAt(i);
				--i;
			}
		}

		for (int i = 0; i < ActiveAnimations.Count; i++) {
			if (ActiveAnimations[i].SeqName != seqName)
				continue;

			if (withinParent != null) {
				Panel? animPanel = ActiveAnimations[i].Panel;

				if (animPanel == null)
					continue;

				Panel? foundPanel = withinParent.FindChildByName(animPanel.GetName(), true);

				if (foundPanel != animPanel)
					continue;
			}

			ActiveAnimations.RemoveAt(i);
			--i;
		}
	}

	readonly List<UtlSymId_t> ScriptFileNames = [];

	public void ReloadScriptFile() {
		Sequences.Clear();
		UpdateScreenSize();

		for (int i = 0; i < ScriptFileNames.Count; i++) {
			ReadOnlySpan<char> filename = ScriptSymbols.String(ScriptFileNames[i]);
			if (!filename.IsEmpty) {
				if (LoadScriptFile(filename) == false) {
					Assert(false);
				}
			}
		}
	}

	public bool SetScriptFile(IPanel sizingPanel, ReadOnlySpan<char> fileName, bool wipeAll = false) {
		SizePanel = sizingPanel;

		if (wipeAll) {
			Sequences.Clear();
			ScriptFileNames.Clear();

			CancelAllAnimations();
		}

		UtlSymId_t sFilename = ScriptSymbols.AddString(fileName);
		bool found = false;
		for (int i = 0; i < ScriptFileNames.Count; i++) {
			if (ScriptFileNames[i] == sFilename) {
				found = true;
				break;
			}
		}
		if (!found)
			ScriptFileNames.Add(sFilename);

		UpdateScreenSize();

		return LoadScriptFile(fileName);
	}

	public bool LoadScriptFile(ReadOnlySpan<char> filename) {
		using IFileHandle? f = fileSystem.Open(filename, FileOpenOptions.Read | FileOpenOptions.Text);

		if (f == null) {
			Warning($"Couldn't find script file {filename}\n");
			return false;
		}

		using var reader = new StreamReader(f.Stream, leaveOpen: true);
		string text = reader.ReadToEnd();

		return ParseScriptFile(text);
	}

	private bool _ParseScriptFile(ReadOnlySpan<char> mem, Span<char> token) {
		IScheme scheme = GetScheme()!;

		int screenWide = ScreenBounds[2];
		int screenTall = ScreenBounds[3];
		Span<char> token2 = stackalloc char[32];

		mem = FilesystemHelpers.ParseFile(mem, token, out _);
		while (token[0] != '\0') {
			bool accepted = true;

			if (stricmp(token, "event") != 0) {
				Warning($"Couldn't parse script file: expected 'event', found '{token.SliceNullTerminatedString()}'\n");
				return false;
			}

			mem = FilesystemHelpers.ParseFile(mem, token, out _);
			if (token[0] == '\0') {
				Warning("Couldn't parse script file: expected <event name>, found nothing\n");
				return false;
			}

			UtlSymId_t nameIndex = ScriptSymbols.AddString(token);
			// var nameOK = 0 == stricmp("WeaponHighlight", token);

			int seqIndex = Sequences.Count;
			Sequences.Add(new());
			ref AnimSequence seq = ref Sequences.AsSpan()[seqIndex];
			seq.Name = nameIndex;
			seq.Duration = 0.0;

			mem = FilesystemHelpers.ParseFile(mem, token, out _);
			if (token.Contains("[$", StringComparison.OrdinalIgnoreCase) || token.Contains("[!$", StringComparison.OrdinalIgnoreCase)) {
				accepted = KeyValues.EvaluateConditional(token);

				// now get the open brace
				mem = FilesystemHelpers.ParseFile(mem, token, out _);
			}

			if (stricmp(token, "{") != 0) {
				Warning($"Couldn't parse script sequence '{ScriptSymbols.String(seq.Name)}': expected '{{', found '{token.SliceNullTerminatedString()}'\n");
				return false;
			}

			while (token[0] != '\0') {
				mem = FilesystemHelpers.ParseFile(mem, token, out _);

				if (token[0] == '}')
					break;

				int cmdIndex = seq.CmdList.Count; seq.CmdList.Add(new());
				ref AnimCommand animCmd = ref seq.CmdList.AsSpan()[cmdIndex];
				memreset(ref animCmd);
				if (stricmp(token, "animate") == 0) {
					animCmd.CommandType = AnimCommandType.Animate;
					ref AnimCmdAnimate cmdAnimate = ref animCmd.Animate;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					cmdAnimate.Panel = ScriptSymbols.AddString(token);
					// var panelNameOK = 0 == stricmp("HudWeaponSelection", token);

					// variable to change
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					cmdAnimate.Variable = ScriptSymbols.AddString(token);
					// target value
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					if (cmdAnimate.Variable == Position) {
						// Get first token
						SetupPosition(ref cmdAnimate, ref cmdAnimate.Target.A, token, screenWide);

						// Get second token from "token"
						scoped ReadOnlySpan<char> psz = FilesystemHelpers.ParseFile(token, token2, out _);
						psz = FilesystemHelpers.ParseFile(psz, token2, out _);
						psz = token2;

						// Position Y goes into ".b"
						SetupPosition(ref cmdAnimate, ref cmdAnimate.Target.B, psz, screenTall);
					}
					else if (cmdAnimate.Variable == XPos) {
						// XPos and YPos both use target ".a"
						SetupPosition(ref cmdAnimate, ref cmdAnimate.Target.A, token, screenWide);
					}
					else if (cmdAnimate.Variable == YPos) {
						// XPos and YPos both use target ".a"
						SetupPosition(ref cmdAnimate, ref cmdAnimate.Target.A, token, screenTall);
					}
					else {
						var scanf = new ScanF(token, "%f %f %f %f").Read(out cmdAnimate.Target.A).Read(out cmdAnimate.Target.B).Read(out cmdAnimate.Target.C).Read(out cmdAnimate.Target.D);
						if (0 == scanf.ReadArguments) {
							Color default_invisible_black = new(0, 0, 0, 0);
							Color col = scheme.GetColor(token, default_invisible_black);

							// we don't have a way of seeing if the color is not declared in the scheme, so we use this
							// silly method of trying again with a different default to see if we get the fallback again
							if (col == default_invisible_black) {
								Color error_pink = new(255, 0, 255, 255); // make it extremely obvious if a scheme lookup fails
								col = scheme.GetColor(token, error_pink);
								if (col == error_pink)
									Warning($"AnimationController: missing scheme color '{token.SliceNullTerminatedString()}'\n");
							}

							cmdAnimate.Target.A = col[0];
							cmdAnimate.Target.B = col[1];
							cmdAnimate.Target.C = col[2];
							cmdAnimate.Target.D = col[3];
						}
					}

					if (cmdAnimate.Variable == Size) {
						if (IsProportional()) {
							cmdAnimate.Target.A = SchemeManager.GetProportionalScaledValueEx(GetScheme(), (int)cmdAnimate.Target.A);
							cmdAnimate.Target.B = SchemeManager.GetProportionalScaledValueEx(GetScheme(), (int)cmdAnimate.Target.B);
						}
					}
					else if (cmdAnimate.Variable == Wide || cmdAnimate.Variable == Tall) {
						if (IsProportional()) {
							// Wide and tall both use.a
							cmdAnimate.Target.A = SchemeManager.GetProportionalScaledValueEx(GetScheme(), (int)cmdAnimate.Target.A);
						}
					}

					// interpolation function
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					if (stricmp(token, "Accel") == 0)
						cmdAnimate.InterpolationFunction = Interpolators.Accel;
					else if (stricmp(token, "Deaccel") == 0)
						cmdAnimate.InterpolationFunction = Interpolators.Deaccel;
					else if (stricmp(token, "Spline") == 0)
						cmdAnimate.InterpolationFunction = Interpolators.SimpleSpline;
					else if (stricmp(token, "Pulse") == 0) {
						cmdAnimate.InterpolationFunction = Interpolators.Pulse;
						// frequencey
						mem = FilesystemHelpers.ParseFile(mem, token, out _);
						cmdAnimate.InterpolationParameter = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
					}
					else if (stricmp(token, "Bias") == 0) {
						cmdAnimate.InterpolationFunction = Interpolators.Bias;
						// bias
						mem = FilesystemHelpers.ParseFile(mem, token, out _);
						cmdAnimate.InterpolationParameter = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
					}
					else if (stricmp(token, "Gain") == 0) {
						cmdAnimate.InterpolationFunction = Interpolators.Gain;
						// bias
						mem = FilesystemHelpers.ParseFile(mem, token, out _);
						cmdAnimate.InterpolationParameter = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
					}
					else if (stricmp(token, "Flicker") == 0) {
						cmdAnimate.InterpolationFunction = Interpolators.Flicker;
						// noiseamount
						mem = FilesystemHelpers.ParseFile(mem, token, out _);
						cmdAnimate.InterpolationParameter = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
					}
					else if (stricmp(token, "Bounce") == 0)
						cmdAnimate.InterpolationFunction = Interpolators.Bounce;
					else
						cmdAnimate.InterpolationFunction = Interpolators.Linear;
					// start time
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					cmdAnimate.StartTime = float.TryParse(token.SliceNullTerminatedString(), out float f2) ? f2 : 0;
					// duration
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					cmdAnimate.Duration = float.TryParse(token.SliceNullTerminatedString(), out float f3) ? f3 : 0;
					// check max duration
					if (cmdAnimate.StartTime + cmdAnimate.Duration > seq.Duration) {
						seq.Duration = cmdAnimate.StartTime + cmdAnimate.Duration;
					}
				}
				else if (stricmp(token, "runevent") == 0) {
					animCmd.CommandType = AnimCommandType.RunEvent;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Event = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.TimeDelay = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
				}
				else if (stricmp(token, "runeventchild") == 0) {
					animCmd.CommandType = AnimCommandType.RunEventChild;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Event = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.TimeDelay = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
				}
				else if (stricmp(token, "firecommand") == 0) {
					animCmd.CommandType = AnimCommandType.FireCommand;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.TimeDelay = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable = ScriptSymbols.AddString(token);
				}
				else if (stricmp(token, "playsound") == 0) {
					animCmd.CommandType = AnimCommandType.PlaySound;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.TimeDelay = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable = ScriptSymbols.AddString(token);
				}
				else if (stricmp(token, "setvisible") == 0) {
					animCmd.CommandType = AnimCommandType.SetVisible;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable2 = (ulong)(int.TryParse(token.SliceNullTerminatedString(), out int i) ? i : 0);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.TimeDelay = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
				}
				else if (stricmp(token, "setinputenabled") == 0) {
					animCmd.CommandType = AnimCommandType.SetInputEnabled;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable2 = (ulong)(int.TryParse(token.SliceNullTerminatedString(), out int i) ? i : 0);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.TimeDelay = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
				}
				else if (stricmp(token, "stopevent") == 0) {
					animCmd.CommandType = AnimCommandType.StopEvent;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Event = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.TimeDelay = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
				}
				else if (stricmp(token, "StopPanelAnimations") == 0) {
					animCmd.CommandType = AnimCommandType.StopPanelAnimations;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Event = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.TimeDelay = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
				}
				else if (stricmp(token, "stopanimation") == 0) {
					animCmd.CommandType = AnimCommandType.StopAnimation;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Event = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.TimeDelay = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
				}
				else if (stricmp(token, "SetFont") == 0) {
					animCmd.CommandType = AnimCommandType.SetFont;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Event = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable2 = ScriptSymbols.AddString(token);

					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.TimeDelay = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
				}
				else if (stricmp(token, "SetTexture") == 0) {
					animCmd.CommandType = AnimCommandType.SetTexture;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Event = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable2 = ScriptSymbols.AddString(token);

					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.TimeDelay = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
				}
				else if (stricmp(token, "SetString") == 0) {
					animCmd.CommandType = AnimCommandType.SetString;
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Event = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable = ScriptSymbols.AddString(token);
					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.Variable2 = ScriptSymbols.AddString(token);

					mem = FilesystemHelpers.ParseFile(mem, token, out _);
					animCmd.RunEvent.TimeDelay = float.TryParse(token.SliceNullTerminatedString(), out float f) ? f : 0;
				}
				else {
					Warning($"Couldn't parse script sequence '{ScriptSymbols.String(seq.Name)}': expected <anim command>, found '{token.SliceNullTerminatedString()}'\n");
					return false;
				}

				// Look ahead one token for a conditional
				ReadOnlySpan<char> peek = FilesystemHelpers.ParseFile(mem, token, out _);
				if (token.Contains("[$", StringComparison.OrdinalIgnoreCase) || token.Contains("[!$", StringComparison.OrdinalIgnoreCase)) {
					if (!KeyValues.EvaluateConditional(token))
						seq.CmdList.RemoveAt(cmdIndex);

					mem = peek;
				}
			}

			if (accepted) {
				int seqIterator;
				for (seqIterator = 0; seqIterator < Sequences.Count - 1; seqIterator++) {
					if (Sequences[seqIterator].Name == nameIndex) {
						// Get rid of it, we're overriding it
						Sequences.RemoveAt(seqIndex);
						break;
					}
				}
			}
			else {
				Sequences.RemoveAt(seqIndex);
			}

			mem = FilesystemHelpers.ParseFile(mem, token, out _);
		}

		return true;
	}

	private void SetupPosition(ref AnimCmdAnimate cmd, ref float output, ReadOnlySpan<char> token, int screendimension) {
		bool r = false, c = false;
		int pos;
		if (token[0] == '(') {
			token = token[1..];

			if (token.Contains(")", StringComparison.Ordinal)) {
				Span<char> sz = stackalloc char[256];
				strcpy(sz, token);

				int colonIdx = sz.IndexOf(":", StringComparison.Ordinal);
				Span<char> colon = colonIdx == -1 ? null : sz[colonIdx..];
				if (!colon.IsEmpty) {
					colon[0] = '\0';

					Alignment ra = LookupAlignment(sz);

					colon = colon[1..];

					Span<char> panelName = colon;
					int panelEndIdx = panelName.IndexOf(")", StringComparison.Ordinal);
					Span<char> panelEnd = panelEndIdx == -1 ? null : panelName[panelEndIdx..];
					if (!panelEnd.IsEmpty) {
						panelEnd[0] = '\0';

						if (!panelName.IsEmpty && strlen(panelName) > 0) {
							cmd.Align.RelativePosition = true;
							cmd.Align.AlignPanel = ScriptSymbols.AddString(panelName);
							cmd.Align.Alignment = ra;
						}
					}
				}

				int endIdx = token.IndexOf(")", StringComparison.Ordinal);
				token = endIdx == -1 ? null : token[(endIdx + 1)..];
			}
		}
		else if (token[0] == 'r' || token[0] == 'R') {
			r = true;
			token = token[1..];
		}
		else if (token[0] == 'c' || token[0] == 'C') {
			c = true;
			token = token[1..];
		}

		token = token.SliceNullTerminatedString();
		int endindex = token.IndexOf(' ');
		if (endindex != -1)
			token = token[..endindex];
		// get the number

		pos = int.TryParse(token, null, out int i) ? i : 0;

		// scale the values
		if (IsProportional())
			pos = SchemeManager.GetProportionalScaledValueEx(GetScheme(), pos);

		// adjust the positions
		if (r)
			pos = screendimension - pos;
		if (c)
			pos = (screendimension / 2) + pos;

		// set the value
		output = (float)pos;
	}

	private static Alignment LookupAlignment(ReadOnlySpan<char> sz) {
		sz = sz.SliceNullTerminatedString();
		if (sz.Length > "northwest".Length)
			return Alignment.Northwest;

		Span<char> lower = stackalloc char[sz.Length];
		sz.ToLowerInvariant(lower);
		switch (lower) {
			case "northwest": case "nw": return Alignment.Northwest;
			case "north": case "n": return Alignment.North;
			case "northeast": case "ne": return Alignment.Northeast;
			case "west": case "w": return Alignment.West;
			case "center": case "c": return Alignment.Center;
			case "east": case "e": return Alignment.East;
			case "southwest": case "sw": return Alignment.Southwest;
			case "south": case "s": return Alignment.South;
			case "southeast": case "se": return Alignment.Southeast;
		}

		return Alignment.Northwest;
	}

	private bool ParseScriptFile(ReadOnlySpan<char> mem) {
		return _ParseScriptFile(mem, stackalloc char[512]);
	}

	public bool UpdateScreenSize() {
		int screenWide, screenTall;
		int sx = 0, sy = 0;

		if (SizePanel != null) {
			SizePanel.GetSize(out screenWide, out screenTall);
			SizePanel.GetPos(out sx, out sy);
		}
		else
			Surface.GetScreenSize(out screenWide, out screenTall);

		bool changed = ScreenBounds[0] != sx ||
			ScreenBounds[1] != sy ||
			ScreenBounds[2] != screenWide ||
			ScreenBounds[3] != screenTall;

		ScreenBounds[0] = sx;
		ScreenBounds[1] = sy;
		ScreenBounds[2] = screenWide;
		ScreenBounds[3] = screenTall;

		return changed;
	}
}
