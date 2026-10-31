using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod.Lua;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.MaterialSystem;
using Source.Common.Utilities;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.GUI.Controls;

public struct OverrideableColorEntry
{
	public delegate Color ColorFunc(in OverrideableColorEntry self);
	public string Name;
	public ColorFunc? Func;
	public Color ColorFromScript;
	public bool Overridden;
	public readonly Color Color() => Func == null ? default : Func(in this);
}

[Flags]
public enum PanelFlags
{
	MarkedForDeletion = 0x0001,
	NeedsRepaint = 0x0002,
	PaintBorderEnabled = 0x0004,
	PaintBackgroundEnabled = 0x0008,
	PaintEnabled = 0x0010,
	PostChildPaintEnabled = 0x0020,
	AutoDeleteEnabled = 0x0040,
	NeedsLayout = 0x0080,
	NeedsSchemeUpdate = 0x0100,
	NeedsDefaultSettingsApplied = 0x0200,
	AllowChainKeybindingToParent = 0x0400,
	InPerformLayout = 0x0800,
	IsProportional = 0x1000,
	TriplePressAllowed = 0x2000,
	DragRequiresPanelExit = 0x4000,
	IsMouseDisabledForThisPanelOnly = 0x8000,
	All = 0xFFFF,
}

public enum RoundedCorners
{
	TopLeft = 1 << 0,
	TopRight = 1 << 1,
	BottomLeft = 1 << 2,
	BottomRight = 1 << 3,
	All = TopLeft | TopRight | BottomLeft | BottomRight,
}

public enum Operator
{
	Add,
	Sub,
	Set
}

public enum BuildModeFlags
{
	Editable = 1 << 0,
	Deletable = 1 << 1,
	SaveXPos_RightAligned = 1 << 2,
	SaveXPos_CenterAligned = 1 << 3,
	SaveYPos_BottomAligned = 1 << 4,
	SaveYPos_CenterAligned = 1 << 5,
	SaveWideFull = 1 << 6,
	SaveTallFull = 1 << 7,
	SaveProportionalToParent = 1 << 8,
	SaveWideProportional = 1 << 9,
	SaveTallProportional = 1 << 10,
	SaveXPosProportionalSelf = 1 << 11,
	SaveYPosProportionalSelf = 1 << 12,
	SaveWideProportionalTall = 1 << 13,
	SaveTallProportionalWide = 1 << 14,
	SaveXPosProportionalParent = 1 << 15,
	SaveYPosProportionalParent = 1 << 16,
	SaveWideProportionalSelf = 1 << 17,
	SaveTallProportionalSelf = 1 << 18,
}

struct DragDrop
{
	public bool DragEnabled;
	public bool ShowDragHelper;
	public bool Dragging;
	public bool DragStarted;
	public int DragStartTolerance;
	public int[] StartPos = new int[2];
	public int[] LastPos = new int[2];
	public List<KeyValues> DragData = [];
	public List<Panel> DragPanels = [];
	public bool DropEnabled;
	public float HoverContextTime;
	public Panel? CurrentDrop;
	public long DropHoverTime;
	public bool DropMenuShown;
	public Menu? DropContextMenu;
	public bool PreventChaining;

	public DragDrop() {
		DragEnabled = false;
		ShowDragHelper = true;
		DropEnabled = false;
		DragStarted = false;
		DragStartTolerance = 0;
		Dragging = false;
		DropHoverTime = 0;
		DropMenuShown = false;
		PreventChaining = false;
		StartPos[0] = StartPos[1] = 0;
		LastPos[0] = LastPos[1] = 0;
	}
}


public interface IPanelAnimationPropertyConverter
{
	void GetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry);
	void SetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry);
	void InitFromDefault(Panel panel, ref PanelAnimationMapEntry entry);
}

[AttributeUsage(AttributeTargets.Field)]
public class PanelAnimationVarAliasTypeAttribute(string scriptName, string defaultValue, string typeAlias) : PanelAnimationVarAttribute(scriptName, defaultValue, typeAlias);
[AttributeUsage(AttributeTargets.Class)]
public class PanelAliasAttribute(string alias) : Attribute
{
	public readonly string Alias = alias;
}
public class PanelAnimationVarAttribute : Attribute
{
	public readonly string? Name;
	public readonly string DefaultValue;
	public readonly string? Type;

	public PanelAnimationVarAttribute(string defaultValue) {
		DefaultValue = defaultValue;
	}

	public PanelAnimationVarAttribute(string name, string defaultValue) {
		Name = name;
		DefaultValue = defaultValue;
	}

	public PanelAnimationVarAttribute(string name, string defaultValue, string? type) {
		Name = name;
		DefaultValue = defaultValue;
		Type = type;
	}
}

public class Panel : IPanel
{
	readonly static Dictionary<ulong, IPanelAnimationPropertyConverter> AnimationPropertyConverters = [];
	static Panel() {
		ChainToAnimationMap<Panel>();
	}
	public static IPanelAnimationPropertyConverter? FindConverter(ReadOnlySpan<char> typeName) {
		if (AnimationPropertyConverters.TryGetValue(typeName.Hash(), out var converter))
			return converter;
		return null;
	}
	static bool initialized;
	public static void InitPropertyConverters() {
		if (initialized)
			return;
		initialized = true;

		AddPropertyConverter("float", floatConverter);
		AddPropertyConverter("int", intConverter);
		AddPropertyConverter("Color", colorConverter);
		AddPropertyConverter("HFont", fontConverter);
		AddPropertyConverter("IFont", fontConverter);

		AddPropertyConverter("proportional_float", p_floatConverter);
		AddPropertyConverter("proportional_int", p_intConverter);

		// AddPropertyConverter("proportional_xpos", p_screnspace_intConverter_X);
		// AddPropertyConverter("proportional_ypos", p_screnspace_intConverter_Y);

		// AddPropertyConverter("proportional_width", p_widthConverter);
		// AddPropertyConverter("proportional_height", p_heightConverter);

		AddPropertyConverter("textureid", textureIdConverter);
	}
	static readonly FloatProperty floatConverter = new();
	static readonly IntProperty intConverter = new();
	static readonly ColorProperty colorConverter = new();
	static readonly FontProperty fontConverter = new();
	static readonly ProportionalFloatProperty p_floatConverter = new();
	static readonly ProportialIntProperty p_intConverter = new();
	static readonly TextureIdProperty textureIdConverter = new();

	public static void AddPropertyConverter(ReadOnlySpan<char> typeName, IPanelAnimationPropertyConverter converter) {
		var hash = typeName.Hash();
		if (AnimationPropertyConverters.TryGetValue(hash, out _)) {
			Msg($"Already have VGUI property converter for type {typeName}, ignoring...\n");
			return;
		}

		AnimationPropertyConverters[hash] = converter;
	}

	private AnimationController? ac;
	public AnimationController GetAnimationController() => ac ??= EngineAPI.GetRequiredService<AnimationController>();

	public void Init(int x, int y, int w, int h) {
		PanelName = null;
		TooltipText = null;
		SetPos(x, y);
		SetSize(w, h);
		Flags |= PanelFlags.NeedsLayout | PanelFlags.NeedsSchemeUpdate | PanelFlags.NeedsDefaultSettingsApplied;
		Flags |= PanelFlags.AutoDeleteEnabled | PanelFlags.PaintBorderEnabled | PanelFlags.PaintBackgroundEnabled | PanelFlags.PaintEnabled;
		Flags |= PanelFlags.AllowChainKeybindingToParent;
		Alpha = 255.0f;
		Visible = true;
		Enabled = true;
		Parent = null;
		Popup = false;
		TopmostPopup = false;

		MouseInput = true;
		KbInput = true;

		RoundedCorners = RoundedCorners.All;

		Cursor = CursorCode.Arrow;

		RegisterColorAsOverridable(FgColor, "fgcolor_override");
		RegisterColorAsOverridable(BgColor, "bgcolor_override");
	}

	public virtual IBorder? GetBorder() => Border;
	public void SetBorder(IBorder? border) {
		Border = border;
		if (border != null) {
			border.GetInset(out int x, out int y, out int x2, out int y2);
			SetInset(x, y, x2, y2);
			SetPaintBackgroundType(border.GetBackgroundType());
		}
		else {
			SetInset(0, 0, 0, 0);
		}
	}

	public void MakeReadyForUse() {
		Surface.SolveTraverse(this, true);
	}
	public int GetAlpha() => (int)Alpha;
	public void SetAlpha(float value) => Alpha = value;


	public Panel() {
		Init(0, 0, 64, 24);
	}

	public Panel(Panel? parent) {
		Init(0, 0, 64, 24);
		SetParent(parent);
	}

	public Panel(Panel? parent, ReadOnlySpan<char> panelName) {
		Init(0, 0, 64, 24);
		SetName(panelName);
		SetParent(parent);
	}
	public Panel(Panel? parent, ReadOnlySpan<char> panelName, bool showTaskbarIcon = true) {
		Init(0, 0, 64, 24);
		SetName(panelName);
		SetParent(parent);
	}
	public Panel(Panel? parent, ReadOnlySpan<char> panelName, IScheme scheme) {
		Init(0, 0, 64, 24);
		SetName(panelName);
		SetParent(parent);
		SetScheme(scheme);
	}

	public void SetScheme(IScheme scheme) {
		Scheme = scheme;
	}

	Panel? Parent;

	string? PanelName;
	string? TooltipText;
	short X, Y;
	short W, H;
	short MinW, MinH;
	short InsetLeft, InsetTop, InsetRight, InsetBottom;
	short ClipRectX, ClipRectY, ClipRectW, ClipRectH;
	short AbsX, AbsY;
	short ZPos;

	public bool Visible;
	bool Enabled;
	bool Popup;
	bool MouseInput;
	bool KbInput;
	bool TopmostPopup;
	bool PopupStayAtBack;
	[PanelAnimationVar("alpha", "255")] float Alpha;
	IBorder? Border;
	IScheme? Scheme;
	PanelFlags Flags;
	BuildModeFlags BuildModeFlags;
	BaseTooltip? Tooltips;
	bool ToolTipOverridden;
	readonly List<Panel> Children = [];
	readonly List<OverrideableColorEntry> OverrideableColorEntries = [];

	IPanel? SkipChild;
	bool IsSilent;

	Color BgColor;
	Color FgColor;

	PaintBackgroundType PaintBackgroundType;
	[PanelAnimationVarAliasType("Texture1", "vgui/hud/800corner1", "textureid")] public int BgTextureId1;
	[PanelAnimationVarAliasType("Texture2", "vgui/hud/800corner2", "textureid")] public int BgTextureId2;
	[PanelAnimationVarAliasType("Texture3", "vgui/hud/800corner3", "textureid")] public int BgTextureId3;
	[PanelAnimationVarAliasType("Texture4", "vgui/hud/800corner4", "textureid")] public int BgTextureId4;

	public PaintBackgroundType GetPaintBackgroundType() => PaintBackgroundType;
	public void SetPaintBackgroundType(PaintBackgroundType type) => PaintBackgroundType = type;
	public void SetSilentMode(bool silent) => IsSilent = silent;

	List<IPanel> ActionSignalTargets = [];
	HashSet<IPanel> ActionSignalTargetsLookup = [];

	public void AddActionSignalTarget(IPanel? messageTarget) {
		if (messageTarget == null)
			return;

		if (ActionSignalTargetsLookup.Add(messageTarget))
			ActionSignalTargets.Add(messageTarget);
	}

	public void RemoveActionSignalTarget(IPanel? messageTarget) {
		if (messageTarget == null)
			return;

		if (ActionSignalTargetsLookup.Remove(messageTarget))
			ActionSignalTargets.Remove(messageTarget);
	}

	public void PostActionSignal(KeyValues message) {
		if (!IsSilent) {
			message.SetPtr("panel", this);
			int i;
			for (i = ActionSignalTargets.Count - 1; i > 0; i--) {
				IPanel? panel = ActionSignalTargets[i];
				if (panel != null)
					VGui.PostMessage(panel, message.MakeCopy(), this);
			}

			if (i == 0) {
				IPanel? panel = ActionSignalTargets[i];
				if (panel != null) {
					VGui.PostMessage(panel, message, this);
					return;
				}
			}
		}
	}

	public void PostMessage(IPanel? target, KeyValues message, double delay = 0) {
		VGui.PostMessage(target, message, this, delay);
	}

	protected bool PassUnhandledInput = true;
	public void GetAbsPos(out int x, out int y) {
		x = AbsX;
		y = AbsY;
		Surface.OffsetAbsPos(ref x, ref y);
	}

	public Panel GetChild(int index) {
		return Children[index];
	}

	public int GetChildCount() {
		return Children.Count;
	}

	public IEnumerable<IPanel> GetChildren() {
		foreach (IPanel child in Children) {
			yield return child;
		}
	}

	public ReadOnlySpan<char> GetClassName() => GetType().Name;

	public void GetClipRect(out int x0, out int y0, out int x1, out int y1) {
		x0 = ClipRectX;
		y0 = ClipRectY;
		x1 = ClipRectW;
		y1 = ClipRectH;
	}

#if GMOD_DLL
	public virtual void SetText(ReadOnlySpan<char> text) { }

	public void SetClipRect(int x0, int y0, int x1, int y1) {
		ClipRectX = (short)x0;
		ClipRectY = (short)y0;
		ClipRectW = (short)x1;
		ClipRectH = (short)y1;
	}
#endif


	public void GetPaintSize(out int wide, out int tall) {
		GetSize(out wide, out tall);
		if (Border != null) {
			Border.GetInset(out int left, out int top, out int right, out int bottom);
			wide -= left + right;
			tall -= top + bottom;
		}
	}

	public void InternalInitDefaultValues(PanelAnimationMap map) {
		Flags &= ~PanelFlags.NeedsDefaultSettingsApplied;
		Span<PanelAnimationMapEntry> entries = map.Entries.AsSpan();
		int c = entries.Length;
		for (int i = 0; i < c; i++) {
			ref PanelAnimationMapEntry e = ref entries[i];
			Assert(!Unsafe.IsNullRef(ref e));
			IPanelAnimationPropertyConverter? converter = FindConverter(e.Type);
			converter?.InitFromDefault(this, ref e);
		}

		if (map.BaseMap != null)
			InternalInitDefaultValues(map.BaseMap);
	}

	public void InternalApplySettings(PanelAnimationMap map, KeyValues resources) {
		for (KeyValues? kv = resources.GetFirstSubKey(); kv != null; kv = kv.GetNextKey()) {
			ReadOnlySpan<char> varname = kv.Name;

			ref PanelAnimationMapEntry entry = ref FindPanelAnimationEntry(varname, GetAnimMap());
			if (!Unsafe.IsNullRef(ref entry)) {
				IPanelAnimationPropertyConverter? converter = FindConverter(entry.Type);
				converter?.SetData(this, resources, ref entry);
			}
		}
	}

	string? NavUpName;
	Panel? NavUp;
	string? NavDownName;
	Panel? NavDown;
	string? NavLeftName;
	Panel? NavLeft;
	string? NavRightName;
	Panel? NavRight;

	string? NavToRelayName;
	Panel? NavToRelay;
	string? NavActivateName;
	Panel? NavActivate;
	string? NavBackName;
	Panel? NavBack;

	public Panel? SetNavUp(Panel? nav) {
		Panel? last = NavUp;
		NavUp = nav;
		if (nav != null)
			NavUpName = new(nav.GetName());
		else
			NavUpName = null;
		return last;
	}
	public Panel? SetNavDown(Panel? nav) {
		Panel? last = NavDown;
		NavDown = nav;
		if (nav != null)
			NavDownName = new(nav.GetName());
		else
			NavDownName = null;
		return last;
	}
	public Panel? SetNavLeft(Panel? nav) {
		Panel? last = NavLeft;
		NavLeft = nav;
		if (nav != null)
			NavLeftName = new(nav.GetName());
		else
			NavLeftName = null;
		return last;
	}
	public Panel? SetNavRight(Panel? nav) {
		Panel? last = NavRight;
		NavRight = nav;
		if (nav != null)
			NavRightName = new(nav.GetName());
		else
			NavRightName = null;
		return last;
	}
	public Panel? SetNavToRelay(Panel? nav) {
		Panel? last = NavToRelay;
		NavToRelay = nav;
		return last;
	}
	public Panel? SetNavActivate(Panel? nav) {
		Panel? last = NavActivate;
		NavActivate = nav;
		return last;
	}
	public Panel? SetNavBack(Panel? nav) {
		Panel? last = NavBack;
		NavBack = nav;
		return last;
	}

	public void SetNavUp(ReadOnlySpan<char> controlName) {
		if (!controlName.IsEmpty && controlName.Length > 0 && GetParent() != null) {
			NavUp = null;
			NavUpName = new(controlName);
		}
	}
	public void SetNavDown(ReadOnlySpan<char> controlName) {
		if (!controlName.IsEmpty && controlName.Length > 0 && GetParent() != null) {
			NavDown = null;
			NavDownName = new(controlName);
		}
	}
	public void SetNavLeft(ReadOnlySpan<char> controlName) {
		if (!controlName.IsEmpty && controlName.Length > 0 && GetParent() != null) {
			NavLeft = null;
			NavLeftName = new(controlName);
		}
	}
	public void SetNavRight(ReadOnlySpan<char> controlName) {
		if (!controlName.IsEmpty && controlName.Length > 0 && GetParent() != null) {
			NavRight = null;
			NavRightName = new(controlName);
		}
	}
	public void SetNavToRelay(ReadOnlySpan<char> controlName) {
		if (!controlName.IsEmpty && controlName.Length > 0 && GetParent() != null) {
			NavToRelay = null;
			NavToRelayName = new(controlName);
		}
	}
	public void SetNavActivate(ReadOnlySpan<char> controlName) {
		if (!controlName.IsEmpty && controlName.Length > 0 && GetParent() != null) {
			NavActivate = null;
			NavActivateName = new(controlName);
		}
	}
	public void SetNavBack(ReadOnlySpan<char> controlName) {
		if (!controlName.IsEmpty && controlName.Length > 0 && GetParent() != null) {
			NavBack = null;
			NavBackName = new(controlName);
		}
	}

	RoundedCorners RoundedCorners;

	static int ComputeWide(Panel panel, ref BuildModeFlags buildFlags, KeyValues resourceData, int parentWide, int parentTall, bool computingOther) {
		int wide = panel.GetWide();

		ReadOnlySpan<char> str = resourceData.GetString("wide", null);
		if (!str.IsEmpty) {
			if (str[0] == 'f' || str[0] == 'F') {
				buildFlags |= BuildModeFlags.SaveWideFull;
				str = str[1..];
			}
			else {
				if (str[0] == 'o' || str[0] == 'O') {
					str = str[1..];
					if (computingOther) {
						Warning($"Wide and Tall of panel {panel.GetName()} are set to be each other!\n");
						return 0;
					}

					buildFlags |= BuildModeFlags.SaveWideProportionalTall;
					wide = ComputeTall(panel, ref buildFlags, resourceData, parentWide, parentTall, true);

					if (panel.IsProportional())
						wide = SchemeManager.GetProportionalNormalizedValue(wide);
				}
				else if (str[0] == 'p' || str[0] == 'P') {
					buildFlags |= BuildModeFlags.SaveWideProportional;
					str = str[1..];
				}
				else if (str[0] == 's' || str[0] == 'S') {
					buildFlags |= BuildModeFlags.SaveWideProportionalSelf;
					str = str[1..];
				}
			}

			float flWide = float.TryParse(str, out float __r) ? __r : 0;
			if (0 == (buildFlags & BuildModeFlags.SaveWideProportionalTall))
				wide = int.TryParse(str, out int __r2) ? __r2 : 0;


			if (0 != (buildFlags & BuildModeFlags.SaveWideProportionalTall)) {
				wide = SchemeManager.GetProportionalScaledValueEx(panel.GetScheme()!, wide);
				wide = (int)(wide * flWide);
			}
			else if (0 != (buildFlags & BuildModeFlags.SaveWideProportional)) {
				wide = SchemeManager.GetProportionalScaledValueEx(panel.GetScheme()!, wide);
				wide = parentWide - wide;
				wide = (int)(wide * flWide);
			}
			else if (0 != (buildFlags & BuildModeFlags.SaveWideProportionalSelf)) {
				wide = (int)(panel.GetWide() * flWide);
			}
			else {
				if (panel.IsProportional())
					wide = SchemeManager.GetProportionalScaledValueEx(panel.GetScheme()!, wide);

				if (0 != (buildFlags & BuildModeFlags.SaveWideFull))
					wide = parentWide - wide;
			}
		}

		return wide;
	}
	static int ComputeTall(Panel panel, ref BuildModeFlags buildFlags, KeyValues resourceData, int parentWide, int parentTall, bool computingOther) {
		int tall = panel.GetTall();

		ReadOnlySpan<char> str = resourceData.GetString("tall", null);
		if (!str.IsEmpty) {
			if (str[0] == 'f' || str[0] == 'F') {
				buildFlags |= BuildModeFlags.SaveTallFull;
				str = str[1..];
			}
			else {
				if (str[0] == 'o' || str[0] == 'O') {
					str = str[1..];
					if (computingOther) {
						Warning($"Wide and Tall of panel {panel.GetName()} are set to be each other!\n");
						return 0;
					}

					buildFlags |= BuildModeFlags.SaveTallProportionalWide;
					tall = ComputeWide(panel, ref buildFlags, resourceData, parentWide, parentTall, true);
					if (panel.IsProportional())
						tall = SchemeManager.GetProportionalNormalizedValue(tall);
				}
				else if (str[0] == 'p' || str[0] == 'P') {
					buildFlags |= BuildModeFlags.SaveTallProportional;
					str = str[1..];
				}
				else if (str[0] == 's' || str[0] == 'S') {
					buildFlags |= BuildModeFlags.SaveTallProportionalSelf;
					str = str[1..];
				}
			}

			float flTall = float.TryParse(str, out float __r) ? __r : 0;
			if (0 == (buildFlags & BuildModeFlags.SaveTallProportionalWide))
				tall = int.TryParse(str, out int __r2) ? __r2 : 0;

			if (0 != (buildFlags & BuildModeFlags.SaveTallProportionalWide)) {
				tall = SchemeManager.GetProportionalScaledValueEx(panel.GetScheme()!, tall);
				tall = (int)(tall * flTall);
			}
			else if (0 != (buildFlags & BuildModeFlags.SaveTallProportional)) {
				tall = SchemeManager.GetProportionalScaledValueEx(panel.GetScheme()!, tall);
				tall = parentTall - tall;
				tall = (int)(tall * flTall);
			}
			else if (0 != (buildFlags & BuildModeFlags.SaveTallProportionalSelf)) {
				tall = (int)(panel.GetTall() * flTall);
			}
			else {
				if (panel.IsProportional())
					tall = SchemeManager.GetProportionalScaledValueEx(panel.GetScheme()!, tall);

				if (0 != (buildFlags & BuildModeFlags.SaveTallFull))
					tall = parentTall - tall;
			}
		}

		return tall;
	}
	static BuildModeFlags ComputePos(Panel panel, ReadOnlySpan<char> input, ref int pos, in int size, in int parentSize, in bool x, Operator op) {
		BuildModeFlags flagRightAlign = x ? BuildModeFlags.SaveXPos_RightAligned : BuildModeFlags.SaveYPos_BottomAligned;
		BuildModeFlags nFlagCenterAlign = x ? BuildModeFlags.SaveXPos_CenterAligned : BuildModeFlags.SaveYPos_CenterAligned;
		BuildModeFlags flagProportionalSelf = x ? BuildModeFlags.SaveXPosProportionalSelf : BuildModeFlags.SaveYPosProportionalSelf;
		BuildModeFlags flagProportionalParent = x ? BuildModeFlags.SaveXPosProportionalParent : BuildModeFlags.SaveYPosProportionalParent;

		BuildModeFlags flags = 0;
		int posDelta;
		if (!input.IsEmpty) {
			if (input[0] == 'r' || input[0] == 'R') {
				flags |= flagRightAlign;
				input = input[1..];
			}
			else if (input[0] == 'c' || input[0] == 'C') {
				flags |= nFlagCenterAlign;
				input = input[1..];
			}

			if (input[0] == 's' || input[0] == 'S') {
				flags |= flagProportionalSelf;
				input = input[1..];
			}
			else if (input[0] == 'p' || input[0] == 'P') {
				flags |= flagProportionalParent;
				input = input[1..];
			}

			int newPos = int.TryParse(input, out int __r1) ? __r1 : 0;
			float flPos = float.TryParse(input, out float __r2) ? __r2 : 0;

			float flProportion = 1;
			if (panel.IsProportional()) {
				int nOldPos = newPos;
				newPos = SchemeManager.GetProportionalScaledValueEx(panel.GetScheme()!, newPos);
				flProportion = (float)newPos / nOldPos;
			}

			if (0 != (flags & flagProportionalSelf))
				posDelta = (int)(size * flPos);
			else if (0 != (flags & flagProportionalParent))
				posDelta = (int)(parentSize * flPos);
			else
				posDelta = newPos;

			if (0 != (flags & flagRightAlign))
				newPos = parentSize - posDelta;
			else if (0 != (flags & nFlagCenterAlign))
				newPos = (parentSize / 2) + posDelta;
			else
				newPos = posDelta;

			switch (op) {
				case Operator.Add:
					pos += newPos;
					break;
				case Operator.Sub:
					pos -= newPos;
					break;
				case Operator.Set:
					pos = newPos;
					break;
			}

			if (input[0] == '-' || input[0] == '+')
				input = input[1..];

			while (input.Length > 0 && (char.IsDigit(input[0]) || input[0] == '.'))
				input = input[1..];

			if (!input.IsEmpty && input.Length > 0) {
				switch (input[0]) {
					case '+':
						ComputePos(panel, input[1..], ref pos, size, parentSize, x, Operator.Add);
						break;
					case '-':
						ComputePos(panel, input[1..], ref pos, size, parentSize, x, Operator.Sub);
						break;
				}
			}

		}

		return flags;
	}

	public virtual void ApplySettings(KeyValues resourceData) {
		if (0 != (Flags & PanelFlags.NeedsDefaultSettingsApplied))
			InternalInitDefaultValues(GetAnimMap());

		InternalApplySettings(GetAnimMap(), resourceData);

		BuildModeFlags &= ~(BuildModeFlags.SaveXPos_RightAligned | BuildModeFlags.SaveXPos_CenterAligned
							| BuildModeFlags.SaveYPos_BottomAligned | BuildModeFlags.SaveYPos_CenterAligned
							| BuildModeFlags.SaveWideFull | BuildModeFlags.SaveTallFull
							| BuildModeFlags.SaveProportionalToParent
							| BuildModeFlags.SaveWideProportional | BuildModeFlags.SaveTallProportional
							| BuildModeFlags.SaveXPosProportionalSelf | BuildModeFlags.SaveYPosProportionalSelf
							| BuildModeFlags.SaveWideProportionalTall | BuildModeFlags.SaveTallProportionalWide
							| BuildModeFlags.SaveXPosProportionalParent | BuildModeFlags.SaveYPosProportionalParent
							| BuildModeFlags.SaveWideProportionalSelf | BuildModeFlags.SaveTallProportionalSelf);

		Surface.GetScreenSize(out int alignScreenWide, out int alignScreenTall);

		int screenWide = alignScreenWide;
		int screenTall = alignScreenTall;

		if (Surface.IsScreenSizeOverrideActive()) {
			Surface.ForceScreenSizeOverride(false, 0, 0);
			Surface.GetScreenSize(out screenWide, out screenTall);

			Surface.ForceScreenSizeOverride(true, alignScreenWide, alignScreenTall);
		}

		int parentX = 0;
		int parentY = 0;

		if (resourceData.GetInt("proportionalToParent", 0) == 1) {
			BuildModeFlags |= BuildModeFlags.SaveProportionalToParent;
			GetParent()?.GetBounds(out parentX, out parentY, out alignScreenWide, out alignScreenTall);
		}

		int wide = ComputeWide(this, ref BuildModeFlags, resourceData, alignScreenWide, alignScreenTall, false);
		int tall = ComputeTall(this, ref BuildModeFlags, resourceData, alignScreenWide, alignScreenTall, false);

		GetPos(out int x, out int y);
		ReadOnlySpan<char> xstr = resourceData.GetString("xpos", null);
		ReadOnlySpan<char> ystr = resourceData.GetString("ypos", null);
		BuildModeFlags |= ComputePos(this, xstr, ref x, wide, alignScreenWide, true, Operator.Set);
		BuildModeFlags |= ComputePos(this, ystr, ref y, tall, alignScreenTall, false, Operator.Set);


		bool usesTitleSafeArea = false;
		int titleSafeWide = 0;
		int titleSafeTall = 0;

		Rectangle excludeEdgeFromTitleSafe = new();
		excludeEdgeFromTitleSafe.X = 0;
		excludeEdgeFromTitleSafe.Y = 0;
		excludeEdgeFromTitleSafe.Width = 0;
		excludeEdgeFromTitleSafe.Height = 0;

		SetNavUp(resourceData.GetString("navUp"));
		SetNavDown(resourceData.GetString("navDown"));
		SetNavLeft(resourceData.GetString("navLeft"));
		SetNavRight(resourceData.GetString("navRight"));
		SetNavToRelay(resourceData.GetString("navToRelay"));
		SetNavActivate(resourceData.GetString("navActivate"));
		SetNavBack(resourceData.GetString("navBack"));

		SetPos(x, y);

		if (resourceData.FindKey("zpos") != null)
			SetZPos(resourceData.GetInt("zpos"));

		if (usesTitleSafeArea) {
			if (0 != (BuildModeFlags & BuildModeFlags.SaveWideFull)) {
				if (excludeEdgeFromTitleSafe.X == 0)
					wide -= titleSafeWide;

				if (excludeEdgeFromTitleSafe.Width == 0)
					wide -= titleSafeWide;
			}

			if (0 != (BuildModeFlags & BuildModeFlags.SaveTallFull)) {
				if (excludeEdgeFromTitleSafe.Y == 0)
					tall -= titleSafeTall;

				if (excludeEdgeFromTitleSafe.Height == 0)
					tall -= titleSafeTall;
			}
		}

		SetSize(wide, tall);

		ApplyAutoResizeSettings(resourceData);

		if (resourceData.GetInt("IgnoreScheme", 0) != 0)
			PerformApplySchemeSettings();

		int state = resourceData.GetInt("visible", 1);
		if (state == 0)
			SetVisible(false);
		else if (state == 1)
			SetVisible(true);

		SetEnabled(resourceData.GetInt("enabled", 1) != 0);

		bool mouseEnabled = resourceData.GetInt("mouseinputenabled", 1) != 0;
		if (!mouseEnabled)
			SetMouseInputEnabled(false);

		SetTabPosition(resourceData.GetInt("tabPosition", 0));

		ReadOnlySpan<char> tooltip = resourceData.GetString("tooltiptext", null);
		if (!tooltip.IsEmpty && tooltip.Length > 0)
			GetTooltip()?.SetText(tooltip);

		int paintBackground = resourceData.GetInt("paintbackground", -1);
		if (paintBackground >= 0)
			SetPaintBackgroundEnabled(paintBackground != 0);

		int paintBorder = resourceData.GetInt("paintborder", -1);
		if (paintBorder >= 0)
			SetPaintBorderEnabled(paintBorder != 0);

		ReadOnlySpan<char> border = resourceData.GetString("border", "");
		if (!border.IsEmpty && border.Length > 0) {
			IScheme? scheme = GetScheme();
			SetBorder(scheme!.GetBorder(border));
		}

		ReadOnlySpan<char> newName = resourceData.GetString("fieldName", null);
		if (!newName.IsEmpty)
			SetName(newName);


		int actionSignalLevel = resourceData.GetInt("actionsignallevel", -1);
		if (actionSignalLevel != -1) {
			Panel? pActionSignalTarget = this;
			while (actionSignalLevel-- != 0) {
				pActionSignalTarget = pActionSignalTarget?.GetParent();
			}
			AddActionSignalTarget(pActionSignalTarget);
		}

		ForceStereoRenderToFrameBuffer = resourceData.GetBool("ForceStereoRenderToFrameBuffer", false);

		int roundedCorners = resourceData.GetInt("RoundedCorners", -1);
		if (roundedCorners >= 0)
			RoundedCorners = (RoundedCorners)roundedCorners;

		// ReadOnlySpan<char> SiblingName = resourceData.GetString("pin_to_sibling", null);
		// PinCorner pinCornerToSibling = GetPinCornerFromString(resourceData.GetString("pin_corner_to_sibling", null));
		// PinCorner pinSiblingCorner = GetPinCornerFromString(resourceData.GetString("pin_sibling_corner", null));
		// PinToSibling(SiblingName, pinCornerToSibling, pinSiblingCorner);

		ReadOnlySpan<char> pKeyboardInputEnabled = resourceData.GetString("keyboardinputenabled", null);
		if (!pKeyboardInputEnabled.IsEmpty && pKeyboardInputEnabled.Length > 0) {
			SetKeyboardInputEnabled(int.TryParse(pKeyboardInputEnabled, out int _r) && _r != 0);
		}

		OnChildSettingsApplied(resourceData, this);
	}

	private void ApplyAutoResizeSettings(KeyValues resourceData) {
		GetPos(out int x, out int y);
		GetSize(out int wide, out int tall);

		AutoResize autoResize = (AutoResize)resourceData.GetInt("AutoResize", (int)AutoResize.None);
		PinCorner pinCorner = (PinCorner)resourceData.GetInt("PinCorner", (int)PinCorner.TopLeft);

		int pw = wide;
		int pt = tall;

		if (GetParent() != null)
			GetParent()!.GetSize(out pw, out pt);

		int pinnedCornerOffsetX = 0; ;
		int pinnedCornerOffsetY = 0;
		int unpinnedCornerOffsetX = 0;
		int unpinnedCornerOffsetY = 0;

		switch (pinCorner) {
			case PinCorner.TopLeft:
				pinnedCornerOffsetX = x;
				pinnedCornerOffsetY = y;
				unpinnedCornerOffsetX = x + wide - pw;
				unpinnedCornerOffsetY = y + tall - pt;
				break;
			case PinCorner.TopRight:
				pinnedCornerOffsetX = x + wide - pw;
				pinnedCornerOffsetY = y;
				unpinnedCornerOffsetX = x;
				unpinnedCornerOffsetY = y + tall - pt;
				break;
			case PinCorner.BottomLeft:
				pinnedCornerOffsetX = x;
				pinnedCornerOffsetY = y + tall - pt;
				unpinnedCornerOffsetX = x + wide - pw;
				unpinnedCornerOffsetY = y;
				break;
			case PinCorner.BottomRight:
				pinnedCornerOffsetX = x + wide - pw;
				pinnedCornerOffsetY = y + tall - pt;
				unpinnedCornerOffsetX = x;
				unpinnedCornerOffsetY = y;
				break;
		}

		if (IsProportional()) {
			if (resourceData.FindKey("PinnedCornerOffsetX") != null)
				pinnedCornerOffsetX = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, resourceData.GetInt("PinnedCornerOffsetX"));
			if (resourceData.FindKey("PinnedCornerOffsetY") != null)
				pinnedCornerOffsetY = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, resourceData.GetInt("PinnedCornerOffsetY"));
			if (resourceData.FindKey("UnpinnedCornerOffsetX") != null)
				unpinnedCornerOffsetX = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, resourceData.GetInt("UnpinnedCornerOffsetX"));
			if (resourceData.FindKey("UnpinnedCornerOffsetY") != null)
				unpinnedCornerOffsetY = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, resourceData.GetInt("UnpinnedCornerOffsetY"));
		}
		else {
			pinnedCornerOffsetX = resourceData.GetInt("PinnedCornerOffsetX", pinnedCornerOffsetX);
			pinnedCornerOffsetY = resourceData.GetInt("PinnedCornerOffsetY", pinnedCornerOffsetY);
			unpinnedCornerOffsetX = resourceData.GetInt("UnpinnedCornerOffsetX", unpinnedCornerOffsetX);
			unpinnedCornerOffsetY = resourceData.GetInt("UnpinnedCornerOffsetY", unpinnedCornerOffsetY);
		}

		if (autoResize == AutoResize.None) {
			unpinnedCornerOffsetX = 0;
			unpinnedCornerOffsetY = 0;
		}

		SetAutoResize(pinCorner, autoResize, pinnedCornerOffsetX, pinnedCornerOffsetY, unpinnedCornerOffsetX, unpinnedCornerOffsetY);
	}

	public AutoResize GetAutoResize() => AutoResizeDirection;

	public PinCorner GetPinCorner() => PinCorner;

	public void GetPinOffset(out int dx, out int dy) {
		dx = PinDeltaX;
		dy = PinDeltaY;
	}

	public void GetResizeOffset(out int dx, out int dy) {
		dx = ResizeDeltaX;
		dy = ResizeDeltaY;
	}

	PinCorner PinCorner;
	AutoResize AutoResizeDirection;
	int PinDeltaX;
	int PinDeltaY;
	int ResizeDeltaX;
	int ResizeDeltaY;
	public void SetAutoResize(PinCorner pinCorner, AutoResize autoResize, int pinnedCornerOffsetX, int pinnedCornerOffsetY, int unpinnedCornerOffsetX, int unpinnedCornerOffsetY) {
		PinCorner = pinCorner;
		AutoResizeDirection = autoResize;
		PinDeltaX = pinnedCornerOffsetX;
		PinDeltaY = pinnedCornerOffsetY;
		ResizeDeltaX = unpinnedCornerOffsetX;
		ResizeDeltaY = unpinnedCornerOffsetY;
	}

	readonly string[] PinCornerStrings = [
		"PIN_TOPLEFT",
		"PIN_TOPRIGHT",
		"PIN_BOTTOMLEFT",
		"PIN_BOTTOMRIGHT",
		"PIN_CENTER_TOP",
		"PIN_CENTER_RIGHT",
		"PIN_CENTER_BOTTOM",
		"PIN_CENTER_LEFT"
	];

	private PinCorner GetPinCornerFromString(ReadOnlySpan<char> cornerName) {
		if (cornerName.IsEmpty)
			return PinCorner.TopLeft;

		if (cornerName.Length == 1 && int.TryParse(cornerName, out int val))
			return (PinCorner)val;

		for (int i = 0; i < PinCornerStrings.Length; i++) {
			if (cornerName.Equals(PinCornerStrings[i], StringComparison.OrdinalIgnoreCase))
				return (PinCorner)i;
		}

		return PinCorner.TopLeft;
	}

	public virtual void OnChildSettingsApplied(KeyValues resources, Panel child) {
		Panel? parent = GetParent();
		parent?.OnChildSettingsApplied(resources, child);
	}

	public virtual IPanel? GetCurrentKeyFocus() {
		return null;
	}

	public void GetInset(out int left, out int top, out int right, out int bottom) {
		left = InsetLeft;
		top = InsetTop;
		right = InsetRight;
		bottom = InsetBottom;
	}

	public void GetMinimumSize(out int wide, out int tall) {
		wide = MinW;
		tall = MinH;
	}

	public ReadOnlySpan<char> GetName() {
		if (PanelName != null)
			return PanelName;

		return "";
	}

	public Panel? GetParent() => Parent;
	IPanel? IPanel.GetParent() => Parent;

	[UiMessage("OnScreenSizeChanged")]
	public virtual void OnScreenSizeChanged(int oldWide, int oldTall) {
		for (int i = 0; i < GetChildCount(); i++) {
			IPanel child = GetChild(i);
			PostMessage(child, new("OnScreenSizeChanged", "oldwide", oldWide, "oldtall", oldTall));
		}

		GetBounds(out int x, out int y, out int wide, out int tall);
		Surface.GetScreenSize(out int screenWide, out int screenTall);

		if (x == 0 && y == 0 && wide == oldWide && tall == oldTall)
			SetBounds(0, 0, screenWide, screenTall);

#if GMOD_DLL
		if (PushLuaHook(LUA_POOLEDSTRING.OnScreenSizeChanged)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushNumber(oldWide);
			Lua.PushNumber(oldTall);
			Lua.PushNumber(screenWide);
			Lua.PushNumber(screenTall);
			Lua.CallInternalNoReturns(5);
		}
#endif

		Flags |= PanelFlags.NeedsLayout;

		InvalidateLayout();
	}

	public void GetPos(out int x, out int y) {
		x = X;
		y = Y;
	}

	public IScheme? GetScheme() {
		if (Scheme != null)
			return Scheme;
		if (Parent != null)
			return Parent.GetScheme();

		return SchemeManager.GetDefaultScheme();
	}

	public void GetSize(out int wide, out int tall) {
		wide = W;
		tall = H;
	}

	int TabPosition;

	public int GetTabPosition() => TabPosition;

	public void SetSkipChildDuringPainting(Panel child) => SkipChild = child;

	public int GetZPos() {
		return ZPos;
	}

	public bool HasParent(IPanel potentialParent) {
		if (this == potentialParent)
			return true;

		if (Parent != null)
			return Parent.HasParent(potentialParent);

		return false;
	}

	public virtual void InternalFocusChanged(bool lost) {

	}

	public bool IsEnabled() {
		return Enabled;
	}

	public bool IsFullyVisible() {
		Panel? panel = this;
		while (panel != null) {
			if (!panel.Visible) {
				return false;
			}

			panel = panel.Parent;
		}

		return true;
	}

	public BaseTooltip GetTooltip() {
		if (Tooltips == null) {
			Tooltips = new TextTooltip(this, null);
			ToolTipOverridden = false;

			if (IsConsoleStylePanel())
				Tooltips.SetEnabled(false);
		}

		return Tooltips;
	}

	public void SetTooltip(BaseTooltip? tooltip, ReadOnlySpan<char> text) {
		if (!ToolTipOverridden)
			Tooltips?.Dispose();

		Tooltips = tooltip;
		ToolTipOverridden = true;

		if (TooltipText != null)
			TooltipText = null;

		if (!text.IsEmpty)
			TooltipText = new(text.SliceNullTerminatedString());
	}

	public virtual void SetProportional(bool state) {
		if (state != ((Flags & PanelFlags.IsProportional) != 0)) {
			Flags ^= PanelFlags.IsProportional;
			for (int i = 0; i < GetChildCount(); i++) {
				GetChild(i).SetProportional(IsProportional());
			}
		}
		InvalidateLayout();
	}

	public bool IsKeyboardInputEnabled() => KbInput;

	public bool IsMouseInputEnabled() => MouseInput;

	public bool IsPopup() {
		return Popup;
	}

	public bool IsProportional() {
		return (Flags & PanelFlags.IsProportional) != 0;
	}

	public bool IsTopmostPopup() => TopmostPopup;
	public bool IsPopupStayAtBack() => PopupStayAtBack;
	public void SetPopupStayAtBack(bool state) => PopupStayAtBack = state;

	public bool IsVisible() {
		return Visible;
	}

	public IPanel? IsWithinTraverse(int x, int y, bool traversePopups) {
		if (!IsVisible() || !IsMouseInputEnabled())
			return null;

		if (traversePopups) {
			int i;
			List<Panel> children = Children;
			int childCount = children.Count;
			for (i = childCount - 1; i >= 0; i--) {
				IPanel? panel = children[i];
				if (panel.IsPopup()) {
					panel = panel.IsWithinTraverse(x, y, true);
					if (panel != null)
						return panel;
				}
			}

			for (i = childCount - 1; i >= 0; i--) {
				IPanel? panel = children[i];
				if (!panel.IsPopup()) {
					panel = panel.IsWithinTraverse(x, y, true);
					if (panel != null)
						return panel;
				}
			}

			if (!IsMouseInputDisabledForThisPanel() && IsWithin(x, y)) {
				return this;
			}
		}
		else {
			if (IsWithin(x, y)) {
				List<Panel> children = Children;
				int childCount = children.Count;
				for (int i = childCount - 1; i >= 0; i--) {
					IPanel? panel = children[i];
					if (!panel.IsPopup()) {
						panel = panel.IsWithinTraverse(x, y, false);
						if (panel != null)
							return panel;
					}
				}

				if (!IsMouseInputDisabledForThisPanel())
					return this;
			}
		}

		return null;
	}

	public bool IsWithin(int x, int y) {
#if GMOD_DLL
		if (PushLuaHook(LUA_POOLEDSTRING.TestHover)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushNumber(x);
			Lua.PushNumber(y);
			if (Lua.CallInternal(3, 1)) {
				if (Lua.GetType(-1) == LuaType.Bool) {
					bool ret = Lua.GetBool(-1);
					Lua.Pop(1);
					return ret;
				}
				Lua.Pop(1);
			}
		}
#endif
		if (x < ClipRectX)
			return false;

		if (y < ClipRectY)
			return false;

		if (x >= ClipRectW)
			return false;

		if (y >= ClipRectH)
			return false;

		return true;
	}

	public bool IsCursorOver() {
		Input.GetCursorPos(out int x, out int y);
		return IsWithin(x, y);
	}

	private bool IsMouseInputDisabledForThisPanel() {
		return (Flags & PanelFlags.IsMouseDisabledForThisPanelOnly) != 0;
	}

	// These implementations break everything. Fix later

	public void MoveToBack() {
		if (Parent != null) {
			Parent.Children.Remove(this);
			Parent.Children.Insert(0, this);

			int i = 1;
			while (i < Parent.Children.Count) {
				if (Parent.Children[i].ZPos < ZPos) {
					Parent.Children[i - 1] = Parent.Children[i];
					Parent.Children[i] = this;
					i++;
				}
				else
					break;
			}
		}
	}

	public void MoveToFront() {
		Surface.MovePopupToFront(this);

		if (Parent != null) {
			Parent.Children.Remove(this);
			Parent.Children.Add(this);

			int i = Parent.Children.Count - 2;
			while (i >= 0) {
				if (Parent.Children[i].ZPos > ZPos) {
					Parent.Children[i + 1] = Parent.Children[i];
					Parent.Children[i] = this;
					i--;
				}
				else
					break;
			}
		}
	}

	public bool IsLayoutInvalid() => (Flags & PanelFlags.NeedsLayout) != 0;
	public void InvalidateLayout(bool layoutNow = false, bool reloadScheme = false) {
		Flags |= PanelFlags.NeedsLayout;

		if (reloadScheme) {
			// make all our children reload the scheme
			Flags |= PanelFlags.NeedsSchemeUpdate;

			for (int i = 0; i < GetChildCount(); i++)
				GetChild(i)?.InvalidateLayout(layoutNow, true);

			PerformApplySchemeSettings();
		}

		if (layoutNow) {
#if GMOD_DLL
			if (LayoutCount > 20) {
				Msg("Warning: Tried to InvalidateLayout( true ) from PANEL:PerformLayout over 20 times in a row. Breaking the loop.\n");
				return;
			}
#endif
			InternalPerformLayout();
			Repaint();
		}
	}

#if GMOD_DLL
	public enum DockType : byte
	{
		NoDock = 0,
		Fill,
		Left,
		Right,
		Top,
		Bottom
	}

	static readonly ConVar vgui_luapaint = new("vgui_luapaint", "1", FCvar.None);

	static readonly List<Panel> DrawOnTopPanels = [];
	static bool PaintingDrawOnTop;
	static bool PaintingManually;

	bool PaintedManually;
	bool RenderInScreenshots = true;
	bool DrawOnTop;
	bool NoClipping;
	public DockType Dock;
	bool LayoutDirty;
	bool WorldClicker;
	public bool LuaPanel;

	public virtual void SetPaintedManually(bool state) => PaintedManually = state;
	public virtual bool IsPaintedManually() => PaintedManually;
	public static void SetPaintingManually(bool state) => PaintingManually = state;
	public void SetRenderInScreenshots(bool state) => RenderInScreenshots = state;
	public void SetNoClipping(bool state) => NoClipping = state;

	public virtual void SetDrawOnTop(bool state) {
		if (state == DrawOnTop)
			return;

		if (state)
			DrawOnTopPanels.Add(this);
		else
			DrawOnTopPanels.Remove(this);

		DrawOnTop = state;
	}

	public virtual int GetCaretPos() => 0;
	public virtual void SetCaretPos(int pos) { }

	public virtual void PaintManual(bool repaint, bool allowForce = true) => PaintTraverse(repaint, allowForce);

	public ILuaObject? LuaTable;
	public ILuaObject? LuaObject;
	public ILuaObject? LuaAnimationThink;
	public ILuaObject? LuaThink;
	public ILuaObject? LuaPaint;
	public ILuaObject? LuaPaintOver;
	public ILuaObject? LuaOnChildRemoved;
	public ILuaObject? LuaOnChildAdded;
	public bool LuaHandle;
#if GMOD_DLL
	public bool RunningOnRemove;
	public ILuaInterface? Lua;

	public void PushLua(ILuaInterface lua, LuaType type) {
		if (LuaObject != null && LuaObject.isNil()) {
			Warning("Panel object is fucked - might be using an older Lua interface.. why wasn't it cleared??\n");
			ClearLuaReferences();
		}

		if (LuaObject != null && Lua != lua) {
			lua.PushNil();
			return;
		}

		if (LuaObject != null) {
			if (LuaObject.GetType() == LuaType.Panel) {
				LuaObject.Push();
				return;
			}
			Warning("NOT A PANEL!!!\n");
		}

		Lua = lua;
		if (LuaHandle)
			lua.ReleaseUserTypeObject(this);
		LuaHandle = true;
		lua.PushObjectUserType(this, type);
		LuaObject = lua.CreateObject();
		LuaObject.SetFromStack(-1);
	}

	protected bool PushLuaHook(LUA_POOLEDSTRING name) {
		if (Lua == null || LuaTable == null || LuaTable.isNil() || IsMarkedForDeletion())
			return false;

		LuaTable.Push();
		Lua.PushPooledString((int)name);
		Lua.GetTable(-2);
		Lua.Remove(-2);
		if (Lua.GetType(-1) == LuaType.Function)
			return true;

		Lua.Pop(1);
		return false;
	}

#if GMOD_DLL
	[UiMessage("activate")]
#endif
	public virtual void OnActivate() {
		if (!PushLuaHook(LUA_POOLEDSTRING.OnActivate))
			return;

		PushLua(Lua!, LuaType.Panel);
		Lua!.CallInternalNoReturns(1);
	}

#if GMOD_DLL
	[UiMessage("deactivate")]
#endif
	public virtual void OnDeactivate() {
		if (!PushLuaHook(LUA_POOLEDSTRING.OnDeactivate))
			return;

		PushLua(Lua!, LuaType.Panel);
		Lua!.CallInternalNoReturns(1);
	}

	bool CanCallLuaHook([NotNullWhen(true)] ILuaObject? hook) => Lua != null && LuaTable != null && !LuaTable.isNil() && !IsMarkedForDeletion() && hook != null && hook.isFunction();

	public void ClearLuaReferences() {
		LuaThink?.UnReference();
		LuaThink = null;
		LuaPaint?.UnReference();
		LuaPaint = null;
		LuaPaintOver?.UnReference();
		LuaPaintOver = null;
		LuaAnimationThink?.UnReference();
		LuaAnimationThink = null;
		LuaOnChildRemoved?.UnReference();
		LuaOnChildRemoved = null;
		LuaOnChildAdded?.UnReference();
		LuaOnChildAdded = null;
		if (LuaHandle) {
			Lua!.ReleaseUserTypeObject(this);
			LuaHandle = false;
		}
		LuaTable?.UnReference();
		LuaTable = null;
		LuaObject?.UnReference();
		LuaObject = null;
	}
#endif

	public virtual bool HasLuaTable() => LuaTable != null;

	public virtual void PaintAt(int x, int y) {
		SetPaintingManually(true);

		GetPos(out int oldX, out int oldY);
		bool wasVisible = IsVisible();
		int zpos = GetZPos();
		Panel? parent = GetParent();

		if (parent != null) {
			int i = parent.GetChildCount();
			while (true) {
				i--;
				if (i < 1)
					goto done;
				if (parent.GetChild(i).GetZPos() == 0)
					break;
			}

			for (i = parent.GetChildCount() - 1; i > 0; i--) {
				Panel child = parent.GetChild(i);
				if (child.GetZPos() != i)
					child.SetZPos(i);
				if (child == this)
					zpos = i;
			}
		}
	done:
		SetParent(Surface.GetEmbeddedPanel());
		SetVisible(true);

		GetPos(out int curX, out int curY);
		if (curX != x || curY != y)
			SetPos(x, y);

		MakeReadyForUse();
		SetPos(x, y);
		PaintTraverse(true);
		SetParent(parent);

		if (GetZPos() != zpos)
			SetZPos(zpos);

		GetPos(out curX, out curY);
		if (curX != oldX || curY != oldY)
			SetPos(oldX, oldY);

		SetVisible(wasVisible);
		MakeReadyForUse();

		SetPaintingManually(false);
	}

	public static void PaintDrawOnTopPanels() {
		PaintingDrawOnTop = true;
		for (int i = 0; i < DrawOnTopPanels.Count; i++) {
			if (!DrawOnTopPanels[i].IsVisible())
				continue;

			using MatRenderContextPtr renderContext = new(Materials);
			renderContext.ClearBuffers(false, true);
			Surface.PaintTraverseEx(DrawOnTopPanels[i], false);
		}
		PaintingDrawOnTop = false;
	}
	short LayoutCount;
	public int DockPaddingLeft;
	public int DockPaddingTop;
	public int DockPaddingRight;
	public int DockPaddingBottom;
	public int DockMarginLeft;
	public int DockMarginTop;
	public int DockMarginRight;
	public int DockMarginBottom;

	void PerformDockLayout() {
		int left = DockPaddingLeft;
		GetSize(out int wide, out _);
		int right = wide - DockPaddingRight;
		int top = DockPaddingTop;
		GetSize(out _, out int tall);
		int bottom = tall - DockPaddingBottom;

		for (int i = 0; i < GetChildCount(); i++) {
			Panel child = GetChild(i);
			if (child.IsVisible() && !child.IsMarkedForDeletion() && child.Dock > DockType.Fill)
				DockChild(child, ref left, ref top, ref right, ref bottom);
		}

		for (int i = 0; i < GetChildCount(); i++) {
			Panel child = GetChild(i);
			if (child.IsVisible() && !child.IsMarkedForDeletion() && child.Dock == DockType.Fill) {
				child.SetPos(left + child.DockMarginLeft, top + child.DockMarginTop);
				child.SetSize(right - child.DockMarginRight - child.DockMarginLeft - left, bottom - child.DockMarginBottom - child.DockMarginTop - top);
			}
		}
	}

	void DockChild(Panel child, ref int left, ref int top, ref int right, ref int bottom) {
		if (child.Dock == DockType.Left) {
			child.SetPos(left + child.DockMarginLeft, top + child.DockMarginTop);
			child.SetTall(bottom - child.DockMarginBottom - top - child.DockMarginTop);
			child.GetSize(out int wide, out _);
			left += child.DockMarginRight + wide + child.DockMarginLeft;
		}

		if (child.Dock == DockType.Right) {
			child.GetSize(out int wide, out _);
			child.SetPos(right - wide - child.DockMarginRight, top + child.DockMarginTop);
			child.SetTall(bottom - child.DockMarginBottom - top - child.DockMarginTop);
			child.GetSize(out wide, out _);
			right -= child.DockMarginLeft + wide + child.DockMarginRight;
		}

		if (child.Dock == DockType.Top) {
			child.SetPos(left + child.DockMarginLeft, top + child.DockMarginTop);
			child.SetWide(right - child.DockMarginRight - left - child.DockMarginLeft);
			child.GetSize(out _, out int tall);
			top += child.DockMarginBottom + child.DockMarginTop + tall;
		}

		if (child.Dock == DockType.Bottom) {
			child.GetSize(out _, out int tall);
			child.SetPos(left + child.DockMarginLeft, bottom - child.DockMarginBottom - tall);
			child.SetWide(right - child.DockMarginRight - left - child.DockMarginLeft);
			child.GetSize(out _, out tall);
			bottom -= child.DockMarginTop + child.DockMarginBottom + tall;
		}
	}

	public virtual void SetWorldClicker(bool state) => WorldClicker = state;
	public virtual bool IsWorldClicker() => WorldClicker;

	public virtual void InvalidateParentDock() => Parent?.OnChildDockChanged();

	public virtual void OnChildDockChanged() {
		PerformDockLayout();
		InvalidateLayout();
	}
#endif

	private void InternalPerformLayout() {
#if !GMOD_DLL
		if (0 != (Flags & PanelFlags.NeedsSchemeUpdate))
			return;
#endif

#if DEBUG
		if (sdn_vgui_visualizelayout.GetBool())
			VisualizeLayout(this);
#endif

		Flags |= PanelFlags.InPerformLayout;
#if GMOD_DLL
		if ((Flags & PanelFlags.NeedsSchemeUpdate) == 0)
#endif
			Flags &= ~PanelFlags.NeedsLayout;

#if GMOD_DLL
		// todo VisualizeLayout hook
		PerformDockLayout();

		LayoutCount++;
		if (PushLuaHook(LUA_POOLEDSTRING.PerformLayout)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushNumber(GetWide());
			Lua!.PushNumber(GetTall());
			Lua!.CallInternalNoReturns(3);
		}
		LayoutCount--;

		if ((Flags & PanelFlags.NeedsSchemeUpdate) == 0)
#endif
			PerformLayout();
		Flags &= ~PanelFlags.InPerformLayout;
	}

	public virtual void PerformLayout() {

	}


	public void PaintTraverse(bool repaint, bool allowForce = true) {
		if (!IsVisible())
			return;

#if GMOD_DLL
		if (DrawOnTop && !PaintingDrawOnTop)
			return;

		if (engine.IsTakingScreenshot() && !RenderInScreenshots)
			return;

		if (IsPaintedManually() && !PaintingManually)
			return;
#endif

		float oldAlphaMultiplier = Surface.DrawGetAlphaMultiplier();
		float newAlphaMultiplier = oldAlphaMultiplier * Alpha * 1.0f / 255.0f;

		if (!repaint && allowForce && 0 != (Flags & PanelFlags.NeedsRepaint)) {
			repaint = true;
			Flags &= ~PanelFlags.NeedsRepaint;
		}

		bool bPushedViewport = false;
		if (GetForceStereoRenderToFrameBuffer()) {
			using MatRenderContextPtr renderContext = new(Materials);
			if (renderContext.GetRenderTarget() != null) {
				Surface.PushFullscreenViewport();
				bPushedViewport = true;
			}
		}

		Span<int> clipRect = stackalloc int[4];
		GetClipRect(out clipRect[0], out clipRect[1], out clipRect[2], out clipRect[3]);
		if ((clipRect[2] <= clipRect[0]) || (clipRect[3] <= clipRect[1]))
			repaint = false;

		Surface.DrawSetAlphaMultiplier(newAlphaMultiplier);

		bool bBorderPaintFirst = Border != null ? Border.PaintFirst() : false;

		if (bBorderPaintFirst && repaint && (Flags & PanelFlags.PaintBorderEnabled) != 0 && (Border != null)) {
			Surface.PushMakeCurrent(this, false);
			PaintBorder();
			Surface.PopMakeCurrent(this);
		}

		DebugVisualize();

		if (repaint) {
#if GMOD_DLL
			Surface.PushMakeCurrent(this, false);
			bool luaPainted = false;
			if (vgui_luapaint.GetInt() != 0) {
				if (NoClipping)
					matSys.DisableClipping(true);
				if (CanCallLuaHook(LuaPaint)) {
					GetSize(out int wide, out int tall);
					LuaPaint!.Push();
					PushLua(Lua!, LuaType.Panel);
					Lua!.PushNumber(wide);
					Lua.PushNumber(tall);
					luaPainted = Lua.CallInternalGetBool(3);
				}
				if (NoClipping)
					matSys.DisableClipping(false);
			}

			if (!luaPainted) {
				if (0 != (Flags & PanelFlags.PaintBackgroundEnabled))
					PaintBackground();

				if (0 != (Flags & PanelFlags.PaintEnabled))
					Paint();
			}
			Surface.PopMakeCurrent(this);
#else
			if (0 != (Flags & PanelFlags.PaintBackgroundEnabled)) {
				Surface.PushMakeCurrent(this, false);
				PaintBackground();
				Surface.PopMakeCurrent(this);
			}

			if (0 != (Flags & PanelFlags.PaintEnabled)) {
				Surface.PushMakeCurrent(this, true);
				Paint();
				Surface.PopMakeCurrent(this);
			}
#endif
		}

		for (int i = 0, childCount = Children.Count; i < childCount; i++) {
			Panel child = Children[i];
			bool bVisible = child.IsVisible();

			if (Surface.ShouldPaintChildPanel(child)) {
				if (bVisible) {
					child.PaintTraverse(repaint, allowForce);
				}
			}
			else {
				Surface.Invalidate(child);

				if (bVisible)
					child.PaintTraverse(false, false);
			}
		}

		if (repaint) {
			if (!bBorderPaintFirst && 0 != (Flags & PanelFlags.PaintBorderEnabled) && (Border != null)) {
				Surface.PushMakeCurrent(this, false);
				PaintBorder();
				Surface.PopMakeCurrent(this);
			}

#if GMOD_DLL
			if (vgui_luapaint.GetInt() != 0 && CanCallLuaHook(LuaPaintOver)) {
				GetSize(out int wide, out int tall);
				LuaPaintOver!.Push();
				Surface.PushMakeCurrent(this, false);
				if (NoClipping)
					matSys.DisableClipping(true);
				PushLua(Lua!, LuaType.Panel);
				Lua!.PushNumber(wide);
				Lua.PushNumber(tall);
				Lua.CallInternalNoReturns(3);
				if (NoClipping)
					matSys.DisableClipping(false);
				Surface.PopMakeCurrent(this);
			}
#endif

			if (0 != (Flags & PanelFlags.PostChildPaintEnabled)) {
				Surface.PushMakeCurrent(this, false);
				PostChildPaint();
				Surface.PopMakeCurrent(this);
			}
		}


		Surface.DrawSetAlphaMultiplier(oldAlphaMultiplier);

		Surface.SwapBuffers(this);

		if (bPushedViewport)
			Surface.PopFullscreenViewport();
	}

	IFont? _dbgfont;
	IFont dbgfont => _dbgfont ??= SchemeManager.GetDefaultScheme().GetFont("DebugFixed")!;

#if DEBUG
	public static readonly ConVar sdn_vgui_visualizelayout = new("sdn_vgui_visualizelayout", "0", FCvar.None);
	public static readonly Dictionary<Panel, double> LayoutVisualizations = [];
	internal void VisualizeLayout(Panel panel) => LayoutVisualizations[panel] = system.GetCurrentTime() + 0.3;
#endif

	static readonly ConVar sdn_vgui_debug = new("0", FCvar.None, "Show debug info for panels under the mouse cursor.");
	private void DebugVisualize() {
		if (sdn_vgui_debug.GetInt() == 0)
			return;

		Input.GetCursorPos(out int mx, out int my);
		if (!IsWithin(mx, my))
			return;

		Surface.PushMakeCurrent(this, false);
		GetSize(out int w, out int h);
		Surface.DrawSetColor(255, 0, 0, 255);
		Surface.DrawOutlinedRect(0, 0, w, h);
		Surface.DrawSetTextFont(dbgfont);
		Surface.DrawSetTextPos(4, 4);
		if (sdn_vgui_debug.GetInt() == 2)
			Surface.DrawPrintText($"{GetType().Name} {GetName()} {BgColor}");
		else
			Surface.DrawPrintText($"{GetType().Name} {GetName()}");
		Surface.PopMakeCurrent(this);
	}

	public virtual void PostChildPaint() {

	}

	public virtual void Paint() {

	}

	public void GetBgColor(in Color c) => BgColor = c;
	public void GetFgColor(in Color c) => FgColor = c;
	public Color GetBgColor() => BgColor;
	public Color GetFgColor() => FgColor;

	public virtual void SetTabPosition(int pos) => TabPosition = pos;

	public virtual void PaintBackground() {
		GetSize(out int wide, out int tall);
		if (SkipChild != null && SkipChild.IsVisible()) {
			if (GetPaintBackgroundType() == PaintBackgroundType.Box) {
				GetCornerTextureSize(out int cornerWide, out int cornerTall);

				Color col = GetBgColor();
				DrawHollowBox(0, 0, wide, tall, col, 1.0f);

				wide -= 2 * cornerWide;
				tall -= 2 * cornerTall;

				FillRectSkippingPanel(GetBgColor(), cornerWide, cornerTall, wide, tall, SkipChild);
			}
			else {
				FillRectSkippingPanel(GetBgColor(), 0, 0, wide, tall, SkipChild);
			}
		}
		else {
			Color col = GetBgColor();

			switch (PaintBackgroundType) {
				default:
				case PaintBackgroundType.Filled: {
						Surface.DrawSetColor(col);
						Surface.DrawFilledRect(0, 0, wide, tall);
					}
					break;
				case PaintBackgroundType.Textured: {
						DrawTexturedBox(0, 0, wide, tall, col, 1.0f);
					}
					break;
				case PaintBackgroundType.Box: {
						DrawBox(0, 0, wide, tall, col, 1.0f);
					}
					break;
				case PaintBackgroundType.BoxFade: {
						DrawBoxFade(0, 0, wide, tall, col, 1.0f, 255, 0, true);
					}
					break;
			}
		}
	}

	public bool ShouldDrawTopLeftCornerRounded() => (RoundedCorners & RoundedCorners.TopLeft) != 0;
	public bool ShouldDrawTopRightCornerRounded() => (RoundedCorners & RoundedCorners.TopRight) != 0;
	public bool ShouldDrawBottomLeftCornerRounded() => (RoundedCorners & RoundedCorners.BottomLeft) != 0;
	public bool ShouldDrawBottomRightCornerRounded() => (RoundedCorners & RoundedCorners.BottomRight) != 0;

	protected void DrawBox(int x, int y, int wide, int tall, Color color, float normalizedAlpha, bool hollow = false) {
		if (BgTextureId1 == -1 || BgTextureId2 == -1 || BgTextureId3 == -1 || BgTextureId4 == -1)
			return;

		color[3] = (byte)(color[3] * normalizedAlpha);
		GetCornerTextureSize(out int cornerWide, out int cornerTall);

		Surface.DrawSetColor(color);
		Surface.DrawFilledRect(x + cornerWide, y, x + wide - cornerWide, y + cornerTall);
		if (!hollow) {
			Surface.DrawFilledRect(x, y + cornerTall, x + wide, y + tall - cornerTall);
		}
		else {
			Surface.DrawFilledRect(x, y + cornerTall, x + cornerWide, y + tall - cornerTall);
			Surface.DrawFilledRect(x + wide - cornerWide, y + cornerTall, x + wide, y + tall - cornerTall);
		}
		Surface.DrawFilledRect(x + cornerWide, y + tall - cornerTall, x + wide - cornerWide, y + tall);

		if (ShouldDrawTopLeftCornerRounded()) {
			Surface.DrawSetTexture(BgTextureId1);
			Surface.DrawTexturedRect(x, y, x + cornerWide, y + cornerTall);
		}
		else
			Surface.DrawFilledRect(x, y, x + cornerWide, y + cornerTall);

		if (ShouldDrawTopRightCornerRounded()) {
			Surface.DrawSetTexture(BgTextureId2);
			Surface.DrawTexturedRect(x + wide - cornerWide, y, x + wide, y + cornerTall);
		}
		else
			Surface.DrawFilledRect(x + wide - cornerWide, y, x + wide, y + cornerTall);

		if (ShouldDrawBottomLeftCornerRounded()) {
			Surface.DrawSetTexture(BgTextureId4);
			Surface.DrawTexturedRect(x + 0, y + tall - cornerTall, x + cornerWide, y + tall);
		}
		else
			Surface.DrawFilledRect(x + 0, y + tall - cornerTall, x + cornerWide, y + tall);

		if (ShouldDrawBottomRightCornerRounded()) {
			Surface.DrawSetTexture(BgTextureId3);
			Surface.DrawTexturedRect(x + wide - cornerWide, y + tall - cornerTall, x + wide, y + tall);
		}
		else
			Surface.DrawFilledRect(x + wide - cornerWide, y + tall - cornerTall, x + wide, y + tall);
	}

	private void DrawBoxFade(int v1, int v2, int wide, int tall, Color col, float v3, int v4, int v5, bool v6) {
		throw new NotImplementedException();
	}

	private void DrawTexturedBox(int v1, int v2, int wide, int tall, Color col, float v3) {
		throw new NotImplementedException();
	}

	private void FillRectSkippingPanel(in Color color, int cornerWide, int cornerTall, int wide, int tall, IPanel skipChild) {
		throw new NotImplementedException();
	}

	private void DrawHollowBox(int v1, int v2, int wide, int tall, Color col, float v3) {
		throw new NotImplementedException();
	}

	private void GetCornerTextureSize(out int w, out int h) {
		if (BgTextureId1 == -1) {
			w = h = 0;
			return;
		}
		Surface.DrawGetTextureSize(BgTextureId1, out w, out h);
	}

	public virtual void PaintBorder() {
		Border?.Paint(this);
	}

	public void PerformApplySchemeSettings() {
		if (0 != (Flags & PanelFlags.NeedsDefaultSettingsApplied)) {
			InternalInitDefaultValues(GetAnimMap());
		}

		if (0 != (Flags & PanelFlags.NeedsSchemeUpdate)) {
			IScheme? scheme = GetScheme();
			Assert(scheme != null);
			if (scheme != null) {
				ApplySchemeSettings(scheme);
				ApplyOverridableColors();
			}
		}
	}

	public virtual void SetBgColor(in Color color) => BgColor = color;
	public virtual void SetFgColor(in Color color) => FgColor = color;

	public virtual void GetSettings(KeyValues outResourceData) {
		outResourceData.SetString("ControlName", GetClassName());
		outResourceData.SetString("fieldName", GetName());

		Surface.GetScreenSize(out int screenWide, out int screenTall);
		GetPos(out int x, out int y);

		if (IsProportional()) {
			x = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, x);
			y = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, y);
		}

		if (0 != (BuildModeFlags & BuildModeFlags.SaveXPos_RightAligned)) {

		}
		else if (0 != (BuildModeFlags & BuildModeFlags.SaveXPos_CenterAligned)) {

		}
		else {
			outResourceData.SetInt("xpos", x);
		}

		if (0 != (BuildModeFlags & BuildModeFlags.SaveYPos_BottomAligned)) {
		}
		else if (0 != (BuildModeFlags & BuildModeFlags.SaveYPos_CenterAligned)) {
		}
		else {
			outResourceData.SetInt("ypos", y);
		}

		if (Tooltips != null && Tooltips.GetText().Length > 0)
			outResourceData.SetString("tooltiptext", Tooltips.GetText());

		GetSize(out int wide, out int tall);
		if (IsProportional()) {
			wide = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, wide);
			tall = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, tall);
		}

		outResourceData.SetInt("zpos", GetZPos());

		if (0 != (BuildModeFlags & BuildModeFlags.SaveWideFull)) {

		}
		else {
			outResourceData.SetInt("wide", wide);
		}

		//outResourceData.SetInt("AutoResize", GetAutoResize());
		//outResourceData.SetInt("PinCorner", GetPinCorner());

		outResourceData.SetInt("RoundedCorners", (int)RoundedCorners);

		//outResourceData.SetString("pin_to_sibling", pinToSibling);
		//outResourceData.SetString("pin_corner_to_sibling", pinCornerToSibling);
		//outResourceData.SetString("pin_to_sibling_corner", pinToSiblingCorner);

		outResourceData.SetInt("visible", IsVisible() ? 1 : 0);
		outResourceData.SetInt("enabled", IsEnabled() ? 1 : 0);

		outResourceData.SetInt("tabPosition", GetTabPosition());

		Span<OverrideableColorEntry> entries = CollectionsMarshal.AsSpan(OverrideableColorEntries);
		for (int i = 0, c = entries.Length; i < c; i++) {
			ref OverrideableColorEntry entry = ref entries[i];
			outResourceData.SetColor(entry.Name, entry.ColorFromScript);
		}
	}

	private void AddToOverridableColors(Color color, string scriptName) {

	}

	public void RegisterColorAsOverridable(Color name, string scriptName) => AddToOverridableColors(name, scriptName);

	// This in theory will replicate the pointer logic?
	private void ApplyOverridableColors() {
		Span<OverrideableColorEntry> entries = CollectionsMarshal.AsSpan(OverrideableColorEntries);
		for (int i = 0, c = entries.Length; i < c; i++) {
			ref OverrideableColorEntry entry = ref entries[i];
			if (entry.Overridden)
				entry.Func = (in OverrideableColorEntry e) => e.ColorFromScript;
		}
	}


	public void SetOverridableColor(ref Color outColor, in Color color) { // is this correct?
		for (int i = 0; i < OverrideableColorEntries.Count; i++) {
			OverrideableColorEntry entry = OverrideableColorEntries[i];
			if (entry.Overridden) {
				if (entry.Color() == outColor)
					return;
			}
		}

		// Didn't find it, or it's not been overridden.
		outColor = color;
	}

	public Color GetSchemeColor(ReadOnlySpan<char> keyName, IScheme scheme) {
		return scheme.GetColor(keyName, new(255, 255, 255, 255));
	}
	public Color GetSchemeColor(ReadOnlySpan<char> keyName, Color defaultColor, IScheme scheme) {
		return scheme.GetColor(keyName, defaultColor);
	}

	public static ReadOnlySpan<char> GetDescription() => "string fieldName, int xpos, int ypos, int wide, int tall, bool visible, bool enabled, int tabPosition, corner pinCorner, autoresize autoResize, string tooltiptext".AsSpan();

	public virtual void ApplySchemeSettings(IScheme scheme) {
		SetFgColor(GetSchemeColor("Panel.FgColor", scheme));
		SetBgColor(GetSchemeColor("Panel.BgColor", scheme));

		Flags &= ~PanelFlags.NeedsSchemeUpdate;
#if GMOD_DLL
		if (PushLuaHook(LUA_POOLEDSTRING.ApplySchemeSettings)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.CallInternalNoReturns(1);
		}
#endif
	}

	public void Repaint() {
		Flags |= PanelFlags.NeedsRepaint;
		Surface?.Invalidate(this);
	}

	public virtual void RequestFocus(int direction = 0) {
		OnRequestFocus(this, null);
	}

	public virtual bool IsBuildGroupEnabled() {
		if (BuildGroup == null)
			return false;

		bool enabled = BuildGroup.IsEnabled();
		if (enabled)
			return enabled;

		if (GetParent() != null && GetParent()!.IsBuildGroupEnabled())
			return true;

		return false;
	}

	public void CallParentFunction(KeyValues message) => GetParent()?.SendMessage(message, this);

	[UiMessage("OnRequestFocus")]
	public virtual void OnRequestFocus(Panel sender, Panel? defaultPanel)
		=> GetParent()?.SendMessage(new KeyValues("OnRequestFocus").AddSubKey(new("subFocus", sender)).AddSubKey(new("defaultPanel", defaultPanel)), sender);

	public virtual bool RequestFocusNext(IPanel? existingPanel = null) {
#if GMOD_DLL
		// todo m_bDisableTabbing return
#endif
		if (GetParent() != null)
			return GetParent()!.RequestFocusNext(this);

		return false;
	}

	public virtual bool RequestFocusPrev(IPanel? existingPanel = null) {
#if GMOD_DLL
		// todo m_bDisableTabbing return
#endif
		if (GetParent() != null)
			return GetParent()!.RequestFocusPrev(this);

		return false;
	}

	public void SetBounds(int x, int y, int wide, int tall) {
		SetPos(x, y);
		SetSize(wide, tall);
	}

	CursorCode Cursor;
	public void SetCursor(CursorCode cursor) {
		Cursor = cursor;
	}

	public virtual void SetEnabled(bool state) {
		if (state != IsEnabled()) {
			Enabled = state;
			InvalidateLayout();
			Repaint();
		}
	}

	public void SetInset(int left, int top, int right, int bottom) {
		InsetLeft = (short)left;
		InsetTop = (short)top;
		InsetRight = (short)right;
		InsetBottom = (short)bottom;
	}

	public void SetKeyboardInputEnabled(bool state) {
		KbInput = state;
	}

	public void SetMinimumSize(int wide, int tall) {
		MinW = (short)wide;
		MinH = (short)tall;
#if GMOD_DLL
		LayoutDirty = true;
#endif

		int currentWidth = W;
		if (currentWidth < wide)
			currentWidth = wide;

		int currentHeight = H;
		if (currentHeight < tall)
			currentHeight = tall;

		if (currentWidth != W || currentHeight != H)
			SetSize(currentWidth, currentHeight);
	}

	public void SetMouseInputEnabled(bool state) {
		MouseInput = state;
	}

	public void SetName(ReadOnlySpan<char> panelName) {
		if (PanelName != null && !panelName.IsEmpty && !panelName.Equals(this.PanelName, StringComparison.Ordinal))
			return;

		if (this.PanelName != null)
			panelName = null;

		if (!panelName.IsEmpty)
			PanelName = new(panelName);
	}

	public virtual void SetParent(IPanel? newParent) {
#if GMOD_DLL
		if (newParent != null && IsMarkedForDeletion())
			newParent = null;
		if (newParent != null)
			LayoutDirty = true;
#endif
		if (this == newParent)
			return;

		if (Parent == newParent)
			return;

		var oldParent = Parent;

		if (Parent != null) {
			Parent?.Children.Remove(this);
			Parent = null;
			OnParentChanged(Parent, null);
		}

		if (newParent != null) {
			Parent = (Panel)newParent!;
			Parent.Children.Add(this);
			SetZPos(ZPos);
			Parent.OnChildAdded(this);
			OnParentChanged(oldParent, Parent);
		}

		var parent = GetParent();
		if (parent != null && !IsPopup()) {
			SetProportional(parent!.IsProportional());

			if (parent.IsKeyboardInputEnabled() != IsKeyboardInputEnabled())
				SetKeyboardInputEnabled(parent.IsKeyboardInputEnabled());

			if (parent.IsMouseInputEnabled() != IsMouseInputEnabled())
				SetMouseInputEnabled(parent.IsMouseInputEnabled());
		}
#if GMOD_DLL
		if (!PaintingManually && oldParent != null && oldParent.CanCallLuaHook(oldParent.LuaOnChildRemoved)) {
			oldParent.LuaOnChildRemoved!.Push();
			oldParent.PushLua(oldParent.Lua!, LuaType.Panel);
			PushLua(oldParent.Lua!, LuaType.Panel);
			oldParent.Lua!.CallInternalNoReturns(2);
		}
#endif
	}

	public void SetPopup(bool enabled) {
		Popup = enabled;
	}
	public void MakePopup(bool showTaskbarIcon = true, bool disabled = false) {
		Surface.CreatePopup(this, false, showTaskbarIcon, disabled);
	}

	public void SetPos(int x, int y) {
#if GMOD_DLL
		if (X != x || Y != y)
			LayoutDirty = true;
#endif
		X = (short)x;
		Y = (short)y;
	}

	public void SetSize(int wide, int tall) {
#if GMOD_DLL
		if (W != wide || H != tall)
			LayoutDirty = true;
#endif
		if (wide < MinW)
			wide = MinW;
		if (tall < MinH)
			tall = MinH;

		if (W == wide && H == tall)
			return;

		W = (short)wide;
		H = (short)tall;

		OnSizeChanged(wide, tall);
	}

	public void SetTopmostPopup(bool state) {
		throw new NotImplementedException();
	}

	public virtual void SetVisible(bool state) {
		if (Visible == state)
			return;

		Surface.SetPanelVisible(this, state);

		Visible = state;
#if GMOD_DLL
		LayoutDirty = true;
#endif

		if (IsPopup())
			Surface.CalculateMouseVisible();
	}

	public void SetZPos(int z) {
#if GMOD_DLL
		if (ZPos != z)
			LayoutDirty = true;
#endif
		ZPos = (short)z;
		if (Parent != null) {
			int childCount = Parent.GetChildCount();
			int i;
			for (i = 0; i < childCount; i++) {
				if (Parent.GetChild(i) == this)
					break;
			}

			if (i == childCount)
				return;

			while (true) {
				Panel? prevChild = null, nextChild = null;
				if (i > 0)
					prevChild = Parent.GetChild(i - 1);
				if (i < (childCount - 1))
					nextChild = Parent.GetChild(i + 1);

				if (i > 0 && prevChild != null && prevChild.ZPos > ZPos) {
					// Swap with lower
					Parent.Children[i] = prevChild;
					Parent.Children[i - 1] = this;
					i--;
				}
				else if (i < (childCount - 1) && nextChild != null && nextChild.ZPos < ZPos) {
					Parent.Children[i] = nextChild;
					Parent.Children[i + 1] = this;
					i++;
				}
				else
					break;
			}
		}
	}

	public void Solve() {
		GetPos(out int x, out int y);
		GetSize(out int w, out int h);

		IPanel? parent = GetParent();
		if (IsPopup()) {
			// if we're a popup, draw at the highest level
			parent = Surface.GetEmbeddedPanel();
		}

		int pabsX = 0, pabsY = 0;
		parent?.GetAbsPos(out pabsX, out pabsY);

		// TODO: pin siblings.
		// Need to look into how those work 

		int absX = x;
		int absY = y;
		AbsX = (short)x;
		AbsY = (short)y;

		// put into parent space
		int pinsetX = 0, pinsetY = 0, pinsetW = 0, pinsetH = 0;
		if (parent != null) {
			parent.GetInset(out pinsetX, out pinsetY, out pinsetW, out pinsetH);

			absX += pabsX + pinsetX;
			absY += pabsY + pinsetY;

			AbsX = (short)Math.Clamp(absX, short.MinValue, short.MaxValue);
			AbsY = (short)Math.Clamp(absY, short.MinValue, short.MaxValue);
		}

		// set initial bounds
		ClipRectX = AbsX;
		ClipRectY = AbsY;

		int absX2 = absX + w;
		int absY2 = absY + h;
		ClipRectW = (short)Math.Clamp(absX2, short.MinValue, short.MaxValue);
		ClipRectH = (short)Math.Clamp(absY2, short.MinValue, short.MaxValue);

		// clip to parent, if we're not a popup
		if (parent != null && !IsPopup()) {
			parent.GetClipRect(out int pclipX, out int pclipY, out int pclipW, out int pclipH);

			if (ClipRectX < pclipX) {
				ClipRectX = (short)pclipX;
			}

			if (ClipRectY < pclipY) {
				ClipRectY = (short)pclipY;
			}

			if (ClipRectW > pclipW) {
				ClipRectW = (short)(pclipW - pinsetW);
			}

			if (ClipRectH > pclipH) {
				ClipRectH = (short)(pclipH - pinsetH);
			}

			if (ClipRectX > ClipRectW) {
				ClipRectW = ClipRectX;
			}

			if (ClipRectY > ClipRectH) {
				ClipRectH = ClipRectY;
			}
		}

		Assert(ClipRectX <= ClipRectW);
		Assert(ClipRectY <= ClipRectH);
	}

	public virtual void Think() {
#if GMOD_DLL
		if (LayoutDirty) {
			LayoutDirty = false;
			InvalidateParentDock();
		}

		if (CanCallLuaHook(LuaAnimationThink)) {
			LuaAnimationThink!.Push();
			PushLua(Lua!, LuaType.Panel);
			Lua!.CallInternalNoReturns(1);
		}
#endif
		if (IsVisible()) {
#if GMOD_DLL
			if (CanCallLuaHook(LuaThink)) {
				LuaThink!.Push();
				PushLua(Lua!, LuaType.Panel);
				Lua!.CallInternalNoReturns(1);
			}
#else
			Tooltips?.PerformLayout();
#endif

			if ((Flags & PanelFlags.NeedsLayout) != 0)
				InternalPerformLayout();
		}

		OnThink();
	}
	public void SetPanelBorderEnabled(bool enabled) => Flags = enabled ? Flags |= PanelFlags.PaintEnabled : Flags &= ~PanelFlags.PaintEnabled;
	public void SetPaintBackgroundEnabled(bool enabled) => Flags = enabled ? Flags | PanelFlags.PaintBackgroundEnabled : Flags & ~PanelFlags.PaintBackgroundEnabled;
	public void SetPaintBorderEnabled(bool enabled) => Flags = enabled ? Flags | PanelFlags.PaintBorderEnabled : Flags & ~PanelFlags.PaintBorderEnabled;
	public void SetPaintEnabled(bool enabled) => Flags = enabled ? Flags | PanelFlags.PaintEnabled : Flags & ~PanelFlags.PaintEnabled;
	public void SetPostChildPaintEnabled(bool enabled) => Flags = enabled ? Flags | PanelFlags.PostChildPaintEnabled : Flags & ~PanelFlags.PostChildPaintEnabled;

	IPanel IPanel.GetChild(int index) => GetChild(index);

	public int GetX() => X;
	public int GetY() => Y;
	public int GetWide() => W;
	public int GetTall() => H;
	public void SetWide(int wide) => SetSize(wide, GetTall());
	public void SetTall(int tall) => SetSize(GetWide(), tall);

	public void TraverseLevel(int v) {

	}

	public bool IsMarkedForDeletion() => (Flags & PanelFlags.MarkedForDeletion) != 0;
	public virtual void MarkForDeletion() {
		if ((Flags & PanelFlags.MarkedForDeletion) != 0)
			return;

		Flags |= PanelFlags.MarkedForDeletion;
		Flags &= ~PanelFlags.AutoDeleteEnabled;
		VGui.MarkPanelForDeletion(this);
	}

	public bool HasFocus() => Input.GetFocus() == this;

#if GMOD_DLL
	public virtual void DoModal() { }
#endif

	PanelHandlerTable? handlerTable;
	PanelHandlerTable HandlerTable => handlerTable ??= PanelMessageMap.GetTable(GetType());

	public void DoCommand(ReadOnlySpan<char> command, IPanel? sender = null) {
		if (HandlerTable.TryGetCommand(command, out PanelCommandHandler handler))
			handler(this, sender);
		else
			OnCommand(command);
	}

	[UiMessage("Command")]
	private void OnCommandMessage(Panel? sender, ReadOnlySpan<char> command) => DoCommand(command, sender);

	public virtual void OnCommand(ReadOnlySpan<char> command) {
		if (command.Equals("performlayout", StringComparison.OrdinalIgnoreCase))
			InvalidateLayout();
		else if (command.Equals("reloadscheme", StringComparison.OrdinalIgnoreCase))
			InvalidateLayout(false, true);
		else
			PostActionSignal(new KeyValues(command));
	}
	[UiMessage("MouseCaptureLost")]
	public virtual void OnMouseCaptureLost() => Tooltips?.ResetDelay();
	public virtual void OnSetFocus() {
		Repaint();
#if GMOD_DLL
		CallFocusChanged(true);
#endif
	}
	public virtual void OnKillFocus(Panel? newPanel) {
		Repaint();
#if GMOD_DLL
		CallFocusChanged(false);
#endif
	}
#if GMOD_DLL
	void CallFocusChanged(bool gained) {
		if (PushLuaHook(LUA_POOLEDSTRING.OnFocusChanged)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushBool(gained);
			Lua.CallInternalNoReturns(2);
		}
	}
#endif
	public virtual void OnThink() { }
	public virtual void OnParentChanged(IPanel? oldParent, IPanel? newParent) { }
	public virtual void OnChildAdded(IPanel child) {
#if GMOD_DLL
		if ((Flags & PanelFlags.InPerformLayout) != 0)
			Warning("vgui: Adding child in layout!\n");
		if (!PaintingManually && CanCallLuaHook(LuaOnChildAdded)) {
			LuaOnChildAdded!.Push();
			PushLua(Lua!, LuaType.Panel);
			((Panel)child).PushLua(Lua!, LuaType.Panel);
			Lua!.CallInternalNoReturns(2);
		}
#endif
	}
	public virtual void OnSizeChanged(int newWide, int newTall) {
#if GMOD_DLL
		if (PushLuaHook(LUA_POOLEDSTRING.OnSizeChanged)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushNumber(newWide);
			Lua.PushNumber(newTall);
			Lua.CallInternalNoReturns(3);
		}
#endif
		InvalidateLayout();
	}
	public virtual void OnCursorMoved(int x, int y) { }
#if GMOD_DLL
	void CallGlobalTooltipFunction(ReadOnlySpan<char> name) {
		if (Lua == null || Lua.Global() == null || LuaTable == null || IsMarkedForDeletion())
			return;

		ILuaObject func = Lua.CreateObject();
		Lua.Global().GetMember(name, func);
		if (func.isFunction()) {
			func.Push();
			PushLua(Lua, LuaType.Panel);
			Lua.CallInternalNoReturns(1);
		}
		func.UnReference();
	}
#endif
	public virtual void OnCursorEntered() {
#if GMOD_DLL
		CallGlobalTooltipFunction("ChangeTooltip");
		if (PushLuaHook(LUA_POOLEDSTRING.OnCursorEntered)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.CallInternalNoReturns(1);
		}
#endif
	}
	public virtual void OnCursorExited() {
#if GMOD_DLL
		if (PushLuaHook(LUA_POOLEDSTRING.OnCursorExited)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.CallInternalNoReturns(1);
		}
		CallGlobalTooltipFunction("EndTooltip");
#endif
	}
	public virtual void OnMousePressed(ButtonCode code) {
#if GMOD_DLL
		if (IsWorldClicker())
			Input.SetMouseCapture(this);
		if (PushLuaHook(LUA_POOLEDSTRING.OnMousePressed)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushNumber((int)code);
			Lua.CallInternalGetBool(2);
		}
#endif
	}
	public virtual void OnMouseDoublePressed(ButtonCode code) { }
	[UiMessage("MouseReleased")]
	public virtual void OnMouseReleased(ButtonCode code) {
#if GMOD_DLL
		if (IsWorldClicker())
			Input.SetMouseCapture(null);
		if (PushLuaHook(LUA_POOLEDSTRING.OnMouseReleased)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushNumber((int)code);
			Lua.CallInternalGetBool(2);
		}
#endif
	}
	public virtual void OnMouseMismatchedRelease(ButtonCode code, IPanel? pressedPanel) { }
	[UiMessage("MouseWheeled")]
	public virtual void OnMouseWheeled(int delta) {
#if GMOD_DLL
		if (PushLuaHook(LUA_POOLEDSTRING.OnMouseWheeled)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushNumber(delta);
			if (Lua.CallInternalGetBool(2))
				return;
		}
#endif
		CallParentFunction(new KeyValues("MouseWheeled", "delta", delta));
	}
	[UiMessage("DialogVariables")]
	public virtual void OnDialogVariablesChanged(KeyValues variables) { }
	bool TriplePressAllowed;
	public virtual void SetTriplePressAllowed(bool state) => TriplePressAllowed = state;
	public virtual bool IsTriplePressAllowed() => TriplePressAllowed;
	public virtual void OnMouseTriplePressed(ButtonCode code) { }

	public CursorCode GetCursor() => Cursor;

	public void InternalSetCursor() {
		bool visible = IsVisible();
		if (visible) {
			IPanel? p = GetParent();
			while (p != null) {
				visible &= p.IsVisible();
				p = p.GetParent();
			}

			if (visible && HasParent(Surface.GetEmbeddedPanel())) {
				CursorCode cursor = GetCursor();
				if (Input.GetCursorOveride() != 0)
					cursor = Input.GetCursorOveride();
				Surface.SetCursor((HCursor)cursor);
			}
		}
	}

	bool AutoDelete;
	public virtual void SetAutoDelete(bool state) {
		AutoDelete = state;
	}
	public bool IsAutoDeleteSet() => AutoDelete;

	public bool Disposed() => IsMarkedForDeletion();
	public virtual void Dispose() {
#if GMOD_DLL
		RunningOnRemove = true;
		if (Lua != null && LuaTable != null && !LuaTable.isNil()) {
			LuaTable.Push();
			Lua.GetField(-1, "OnRemove");
			Lua.Remove(-2);
			if (Lua.GetType(-1) == LuaType.Function) {
				PushLua(Lua, LuaType.Panel);
				Lua.CallInternalNoReturns(1);
			}
			else
				Lua.Pop(1);
		}
		RunningOnRemove = false;
#endif
		Flags &= ~PanelFlags.AutoDeleteEnabled;
		Flags |= PanelFlags.MarkedForDeletion;

		SetParent(null);
		while (GetChildCount() > 0) {
			Panel child = GetChild(0);
			if (child.IsAutoDeleteSet())
				child.DeletePanel();
			else
				child.SetParent(null);
		}

#if GMOD_DLL
		SetDrawOnTop(false);
		ClearLuaReferences();
#endif
		VGui.PanelDeleted(this);
		GC.SuppressFinalize(this);
	}

	public void DeletePanel() {
		Flags |= PanelFlags.MarkedForDeletion;
		Flags &= ~PanelFlags.AutoDeleteEnabled;
		Dispose();
	}

	public virtual void OnKeyCodePressed(ButtonCode code) {
		// todo vgui_nav_lock
		bool handled = false;
		if (!handled && !PassUnhandledInput)
			return;
#if GMOD_DLL
		if (PushLuaHook(LUA_POOLEDSTRING.OnKeyCodePressed)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushNumber((int)code);
			if (Lua.CallInternalGetBool(2))
				return;
		}
#endif
		CallParentFunction(new KeyValues("KeyCodePressed").AddSubKey("code", (int)code));
	}
	public virtual void OnKeyCodeTyped(ButtonCode code) {
		if (code == ButtonCode.KeyTab) {
			bool shiftDown = Input.IsKeyDown(ButtonCode.KeyLShift) || Input.IsKeyDown(ButtonCode.KeyRShift);

			if (IsConsoleStylePanel()) {
				if (shiftDown)
					NavigateUp();
				else
					NavigateDown();
			}
			else {
				if (shiftDown)
					RequestFocusPrev();
				else
					RequestFocusNext();
			}
		}
		else {
			if (this == Surface.GetEmbeddedPanel())
				Input.OnKeyCodeUnhandled(code);

			CallParentFunction(new KeyValues("KeyCodeTyped", "code", (int)code));
		}
	}

	public void NavigateUp() {
		// todo
	}

	public void NavigateDown() {
		// todo
	}


	[UiMessage("KeyTyped")]
	public virtual void OnKeyTyped(char unichar) { }
	public virtual void OnKeyCodeReleased(ButtonCode code) {
#if GMOD_DLL
		if (PushLuaHook(LUA_POOLEDSTRING.OnKeyCodeReleased)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushNumber((int)code);
			if (Lua.CallInternalGetBool(2))
				return;
		}
#endif
		CallParentFunction(new KeyValues("KeyCodeReleased", "code", (int)code));
	}
	[UiMessage("UnhandledMouseClick")]
	public virtual void OnUnhandledMouseClick(ButtonCode code) { }
	[UiMessage("KeyFocusTicked")]
	public void InternalKeyFocusTicked() {
		OnKeyFocusTicked();
	}
	public virtual void OnKeyFocusTicked() { }
	[UiMessage("MouseFocusTicked")]
	public void InternalMouseFocusTicked() {
		InternalSetCursor();
		OnMouseFocusTicked();
	}
	public virtual void OnMouseFocusTicked() {
		// Send it to the parent
		GetParent()?.OnMouseFocusTicked();
	}
	[UiMessage("Close")]
	public virtual void OnClose() { }
	[UiMessage("Delete")]
	public virtual void OnDelete() {
		Dispose();
	}

	public void LocalToScreen(ref int x, ref int y) {
		GetAbsPos(out int px, out int py);
		x += px;
		y += py;
	}

	public void ScreenToLocal(ref int x, ref int y) {
		GetAbsPos(out int px, out int py);
		x -= px;
		y -= py;
	}
	public void ParentLocalToScreen(ref int x, ref int y) {
		int px = 0, py = 0;
		GetParent()?.GetAbsPos(out px, out py);
		x += px;
		y += py;
	}

	static ConVar vgui_print_messages = new("0", FCvar.None);

	[UiMessage("KeyCodePressed")]
	public void InternalKeyCodePressed(ButtonCode code) {
		if (!ShouldHandleInputMessage())
			return;

		if (IsKeyboardInputEnabled())
			OnKeyCodePressed(code);
		else
			CallParentFunction(new KeyValues("KeyCodePressed", "code", (int)code));
	}

	static bool SuppressRebindChecks = false;

	public bool ShouldHandleInputMessage() {
		if (Input.GetModalSubTree() == null)
			return true;

		bool childOfModal = false;
		IPanel? subTree = Input.GetModalSubTree();
		if (subTree == null)
			childOfModal = true;
		else if (HasParent(subTree))
			childOfModal = true;

		if (Input.ShouldModalSubTreeReceiveMessages())
			return childOfModal;

		return !childOfModal;
	}

	bool ForceStereoRenderToFrameBuffer;
	private bool GetForceStereoRenderToFrameBuffer() => ForceStereoRenderToFrameBuffer;
	public void SetForceStereoRenderToFrameBuffer(bool state) => ForceStereoRenderToFrameBuffer = state;

	[UiMessage("KeyCodeTyped")]
	public void InternalKeyCodeTyped(ButtonCode code) {
		if (!ShouldHandleInputMessage()) {
			Input.OnKeyCodeUnhandled(code);
			return;
		}

		if (IsKeyboardInputEnabled()) {
			bool shift = Input.IsKeyDown(ButtonCode.KeyLShift) || Input.IsKeyDown(ButtonCode.KeyRShift);
			bool ctrl = Input.IsKeyDown(ButtonCode.KeyLControl) || Input.IsKeyDown(ButtonCode.KeyRControl);
			bool alt = Input.IsKeyDown(ButtonCode.KeyLAlt) || Input.IsKeyDown(ButtonCode.KeyRAlt);

			KeyModifier modifiers = 0;
			if (shift)
				modifiers |= KeyModifier.Shift;
			if (ctrl)
				modifiers |= KeyModifier.Control;
			if (alt)
				modifiers |= KeyModifier.Alt;


			if (!SuppressRebindChecks && IsKeyRebound(code, modifiers)) {
				return;
			}

			bool oldVal = SuppressRebindChecks;
			SuppressRebindChecks = true;
			OnKeyCodeTyped(code);
			SuppressRebindChecks = oldVal;
		}
		else {
			if (this == Surface.GetEmbeddedPanel())
				Input.OnKeyCodeUnhandled(code);

			CallParentFunction(new KeyValues("KeyCodeTyped", "code", (int)code));
		}
	}

	virtual public bool IsKeyRebound(ButtonCode code, KeyModifier modifiers) {
		return false;
	}

	[UiMessage("MousePressed")]
	private void InternalMousePressed(ButtonCode code) {
		// todo triple press/dragdrop etc

		Menu.OnInternalMousePressed(this, code);

		if (!ShouldHandleInputMessage())
			return;

		if (IsCursorNone())
			return;

		if (!IsMouseInputEnabled())
			return;

		if (IsBuildGroupEnabled()) {
			// if (BuildGroup.MousePressed(code, this))
			// 	return;
		}

		OnMousePressed(code);
	}

	[UiMessage("KeyCodeReleased")]
	private void InternalKeyCodeReleased(ButtonCode code) {
		if (!ShouldHandleInputMessage())
			return;

		if (IsKeyboardInputEnabled())
			OnKeyCodeReleased(code);
		else
			CallParentFunction(new KeyValues("KeyCodeReleased", "code", (int)code));
	}

	[UiMessage("CursorMoved")]
	private void InternalCursorMoved([MessageParam("xpos")] int x, [MessageParam("ypos")] int y) {
		// dragdrop todo

		if (!ShouldHandleInputMessage())
			return;

		if (IsCursorNone())
			return;

		if (!IsMouseInputEnabled())
			return;

		if (IsBuildGroupEnabled()) {
			// if (BuildGroup.CursorMoved(x, y, this))
			// 	return;
		}

#if !GMOD_DLL
		if (Tooltips != null) {
			if (TooltipText != null)
				Tooltips.SetText(TooltipText);
			Tooltips.ShowTooltip(this);
		}
#endif

		ScreenToLocal(ref x, ref y);
#if GMOD_DLL
		if (PushLuaHook(LUA_POOLEDSTRING.OnCursorMoved)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushNumber(x);
			Lua.PushNumber(y);
			if (Lua.CallInternalGetBool(3))
				return;
		}
#endif
		OnCursorMoved(x, y);
	}

	private bool IsCursorNone() => GetCursor() == CursorCode.None;

	[UiMessage("CursorEntered")]
	private void InternalCursorEntered() {
		if (IsCursorNone() || !IsMouseInputEnabled())
			return;

		if (IsBuildGroupEnabled())
			return;

		if (Tooltips != null) {
			Tooltips.ResetDelay();
			if (TooltipText != null)
				Tooltips.SetText(TooltipText);
			Tooltips.ShowTooltip(this);
		}

#if GMOD_DLL
		if (Lua != null && LuaTable != null)
			LuaTable.SetMember("Hovered", true);
#endif
		OnCursorEntered();
	}

	[UiMessage("CursorExited")]
	private void InternalCursorExited() {
#if GMOD_DLL
		if (Lua != null && LuaTable != null)
			LuaTable.SetMember("Hovered", false);
#endif
		if (IsCursorNone() || !IsMouseInputEnabled())
			return;

		if (IsBuildGroupEnabled())
			return;

#if !GMOD_DLL
		Tooltips?.HideTooltip();
#endif

		OnCursorExited();
	}

	public bool IsConsoleStylePanel() => false; // todo

	public virtual void OnMessage(KeyValues message, IPanel? sender) {
		if (HandlerTable.TryGetMessage(message.Name, out PanelMessageHandler handler))
			handler(this, message, sender);
#if DEBUG
		else
			DevMsg(3, $"Unhandled message '{message.Name}' from {sender}");
#endif
		if (vgui_print_messages.GetBool())
			if (vgui_print_messages.GetInt() == 2 || (!message.Name.Contains("Ticked") && !message.Name.Contains("Moved")))
				Msg($"Message from {sender} to {this}: {message.Name}\n");
	}

	[UiMessage("TextChanged")]
	public virtual void OnTextChanged(Panel sender) {

	}

	[UiMessage("SetFocus")]
	private void InternalSetFocus() {
		OnSetFocus();
	}

	[UiMessage("KillFocus")]
	private void InternalKillFocus(Panel? sender) {
		OnKillFocus(sender);
	}


	public virtual void OnTick() { }

	public virtual void OnMove() { }

	public void SendMessage(KeyValues parms, IPanel? sender) {
		OnMessage(parms, sender);
	}

	public void SetAllowKeyBindingChainToParent(bool state) {
		if (state)
			Flags |= PanelFlags.AllowChainKeybindingToParent;
		else
			Flags &= ~PanelFlags.AllowChainKeybindingToParent;
	}

	public virtual bool RequestInfo(KeyValues outputData) {
		return InternalRequestInfo(GetAnimMap(), outputData) || (((Panel?)GetParent())?.RequestInfo(outputData) ?? false);
	}

	private bool InternalRequestInfo(PanelAnimationMap? map, KeyValues outputData) {
		if (map == null)
			return false;

		Assert(outputData);

		ReadOnlySpan<char> name = outputData.Name;

		ref PanelAnimationMapEntry e = ref FindPanelAnimationEntry(name, map);
		if (!Unsafe.IsNullRef(ref e)) {
			IPanelAnimationPropertyConverter? converter = FindConverter(e.Type);
			if (converter != null) {
				converter.GetData(this, outputData, ref e);
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Note: modifying the animation map may result in this handle being invalid!
	/// </summary>
	/// <param name="name"></param>
	/// <param name="map"></param>
	/// <returns></returns>
	private ref PanelAnimationMapEntry FindPanelAnimationEntry(ReadOnlySpan<char> scriptname, PanelAnimationMap map) {
		if (map == null)
			return ref Unsafe.NullRef<PanelAnimationMapEntry>();

		int c = map.Entries.Count;
		Span<PanelAnimationMapEntry> entries = map.Entries.AsSpan();
		for (int i = 0; i < c; i++) {
			ref PanelAnimationMapEntry e = ref entries[i];

			if (((ReadOnlySpan<char>)e.ScriptName).Equals(scriptname, StringComparison.OrdinalIgnoreCase)) {
				return ref e;
			}
		}

		if (map.BaseMap != null)
			return ref FindPanelAnimationEntry(scriptname, map.BaseMap);

		return ref Unsafe.NullRef<PanelAnimationMapEntry>();
	}

	public virtual bool SetInfo(KeyValues inputData) {
		if (InternalSetInfo(GetAnimMap(), inputData))
			return true;
		return false;
	}

	private bool InternalSetInfo(PanelAnimationMap? map, KeyValues inputData) {
		if (map == null)
			return false;

		Assert(inputData != null);
		ReadOnlySpan<char> name = inputData.Name;

		ref PanelAnimationMapEntry e = ref FindPanelAnimationEntry(name, map);
		if (!Unsafe.IsNullRef(ref e)) {
			IPanelAnimationPropertyConverter? converter = FindConverter(e.Type);
			if (converter != null) {
				converter.SetData(this, inputData, ref e);
				return true;
			}
		}

		return false;
	}

	IPanel? IPanel.FindChildByName(ReadOnlySpan<char> childName, bool recurseDown) => FindChildByName(childName, recurseDown);

	public Panel? FindChildByName(ReadOnlySpan<char> childName, bool recurseDown = false) {
		for (int i = 0; i < GetChildCount(); i++) {
			Panel child = GetChild(i);
			if (childName.Equals(child.GetName(), StringComparison.OrdinalIgnoreCase))
				return child;

			if (recurseDown) {
				Panel? found = child.FindChildByName(childName, true);
				if (found != null)
					return found;
			}
		}

		return null;
	}

	public Panel? FindSiblingByName(ReadOnlySpan<char> siblingName) {
		if (GetParent() == null)
			return null;

		int siblingCount = GetParent()!.GetChildCount();
		for (int i = 0; i < siblingCount; i++) {
			Panel sibling = GetParent()!.GetChild(i);
			if (siblingName.Equals(sibling.GetName(), StringComparison.OrdinalIgnoreCase))
				return sibling;
		}

		return null;
	}

	public void GetBounds(out int x, out int y, out int w, out int h) {
		GetPos(out x, out y);
		GetSize(out w, out h);
	}

	public static void AddToAnimationMap(Type t, ReadOnlySpan<char> scriptName, ReadOnlySpan<char> type, ReadOnlySpan<char> var, ReadOnlySpan<char> defaultValue, bool array, PanelGetFunc get, PanelSetFunc set) {
		PanelAnimationMap map = PanelAnimationDictionary.FindOrAddPanelAnimationMap(t.FullName);

		PanelAnimationMapEntry entry = new() {
			ScriptName = new string(scriptName),
			Variable = new string(var),
			Type = new string(type),
			DefaultValue = new string(defaultValue),
			Array = array,
			Get = get,
			Set = set
		};
		map.Entries.Add(entry);
	}
	public static void ChainToAnimationMap<T>() where T : Panel => ChainToAnimationMap(typeof(T));
	public static void ChainToAnimationMap(Type t) {
		PanelRegistry.AddAnimationVars(t);

		PanelAnimationMap map = PanelAnimationDictionary.FindOrAddPanelAnimationMap(t.FullName);
		map.ClassName = t.Name;
		Type? baseClass = t.BaseType;
		if (baseClass?.IsAssignableTo(typeof(Panel)) ?? false)
			map.BaseMap = PanelAnimationDictionary.FindOrAddPanelAnimationMap(baseClass.FullName);
	}

	public virtual PanelAnimationMap GetAnimMap() => PanelAnimationDictionary.FindOrAddPanelAnimationMap(GetType().FullName);

	static readonly ConcurrentDictionary<UtlSymId_t, Func<Panel>> PanelFactories = [];

	public static void InitializeControls() {
		foreach (Assembly assembly in ReflectionUtils.GetAssemblies())
			RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

		int count = 0;
		foreach (PanelRegistry.Entry entry in PanelRegistry.Entries) {
			ChainToAnimationMap(entry.Type);

			if (entry.Factory != null) {
				PanelFactories[new UtlSymbol(entry.Name)] = entry.Factory;
				if (entry.Alias != null)
					PanelFactories[new UtlSymbol(entry.Alias)] = entry.Factory;
			}

			count++;
		}

		Msg($"Initialized {count} VGUI controls in all currently loaded assemblies\n");
	}

	public static Panel? InstancePanel(ReadOnlySpan<char> className) {
		if (PanelFactories.TryGetValue(new UtlSymbol(className), out Func<Panel>? fn))
			return fn();

		return null;
	}

	public void SetDragEnabled(bool enabled) {

	}

	public void IsDragEnabled() {

	}

	public void SetShowDragHelper(bool enabled) {

	}

	public void SetBlockDragChaining(bool state) {

	}

	public void IsBlockingDragChaining() {

	}

	public void GetDragStartTolerance() {

	}

	public void SetDragStartTolerance(int tolerance) {

	}

	public void SetDropEnabled(bool enabled, float hoverContextTime = 0.0f) {

	}

	public void IsDropEnabled() {

	}

	public void GetDropTarget(List<KeyValues> msgList) {

	}

	public virtual IPanel? GetDragPanel() { //todo
		return GetParent()?.GetDragPanel();
	}

	private void OnStartDragging() {

	}

	public virtual void OnDragFailed(List<KeyValues> msgList) {

	}

	private void OnFinishDragging(bool mouseReleased, ButtonCode code, bool abort = false) {

	}

	private void OnDropContextHoverShow() {

	}

	private void OnDropContextHoverHide() {

	}

	public virtual bool IsDroppable(List<KeyValues> msgList) => false;

	public void CanStartDragging(int startx, int starty, int mx, int my) {

	}

	public virtual CursorCode GetDropCursor(List<KeyValues> msgList) => CursorCode.Arrow;

	private void OnContinueDragging() {

	}

	public void GetDragDropInfo() {

	}

	public virtual void OnGetAdditionalDragPanels(ref List<Panel> panelList) { }
	public virtual void OnCreateDragData(KeyValues dragData) { }
	public virtual bool GetDropContextMenu(Menu menu, List<KeyValues> msgList) => false;

	public void CreateDragData() {

	}

	public void GetDragData() {

	}

	public bool IsBuildModeEditable() => true;
	public bool IsBuildModeDeletable() => 0 != (BuildModeFlags & BuildModeFlags.Deletable);

	internal void SetBuildModeDeletable(bool state) {
		if (state) BuildModeFlags |= BuildModeFlags.Deletable; else BuildModeFlags &= ~BuildModeFlags.Deletable;
	}

	public void SetBuildModeEditable(bool state) {
		if (state) BuildModeFlags |= BuildModeFlags.Editable; else BuildModeFlags &= ~BuildModeFlags.Editable;
	}

	BuildGroup? BuildGroup;

	internal void SetBuildGroup(BuildGroup? buildGroup) {
		if (BuildGroup == buildGroup)
			return;

		BuildGroup = buildGroup;

		BuildGroup?.PanelAdded(this);
	}

	public int QuickPropScale(int x) => SchemeManager.QuickPropScaleCond(IsProportional(), GetScheme()!, x);
}

class FloatProperty : IPanelAnimationPropertyConverter
{
	public void GetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		object? data = entry.Get(panel);
		if (data == null) return;
		kv.SetFloat(entry.ScriptName, (float)data);
	}

	public void SetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		entry.Set(panel, kv.GetFloat(entry.ScriptName));
	}

	public void InitFromDefault(Panel panel, ref PanelAnimationMapEntry entry) {
		entry.Set(panel, float.TryParse(entry.DefaultValue, out float r) ? r : 0);
	}
}
class IntProperty : IPanelAnimationPropertyConverter
{
	public void GetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		object? data = entry.Get(panel);
		if (data == null) return;
		kv.SetFloat(entry.ScriptName, (int)data);
	}

	public void SetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		entry.Set(panel, kv.GetInt(entry.ScriptName));
	}

	public void InitFromDefault(Panel panel, ref PanelAnimationMapEntry entry) {
		entry.Set(panel, int.TryParse(entry.DefaultValue, out int r) ? r : 0);
	}
}
class ColorProperty : IPanelAnimationPropertyConverter
{
	public void GetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		object? data = entry.Get(panel);
		if (data == null) return;
		kv.SetColor(entry.ScriptName, (Color)data);
	}

	public void SetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		IScheme? scheme = panel.GetScheme();
		Assert(scheme != null);
		if (scheme != null) {
			Color col;

			ReadOnlySpan<char> colorName = kv.GetString(entry.ScriptName);
			if (colorName.IsEmpty || colorName.Length <= 0)
				col = kv.GetColor(entry.ScriptName);
			else
				col = scheme.GetColor(colorName, new(0, 0, 0, 0));

			entry.Set(panel, col);
		}
	}

	public void InitFromDefault(Panel panel, ref PanelAnimationMapEntry entry) {
		IScheme? scheme = panel.GetScheme();
		Assert(scheme != null);
		if (scheme != null)
			entry.Set(panel, scheme.GetColor(entry.DefaultValue, new(0, 0, 0, 0)));
	}
}
class FontProperty : IPanelAnimationPropertyConverter
{
	public void GetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		object? data = entry.Get(panel);
		if (data == null) return;
		kv.SetString(entry.ScriptName, ""); // todo
	}

	public void SetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		entry.Set(panel, panel.GetScheme()?.GetFont(kv.GetString(entry.ScriptName))!);
	}

	public void InitFromDefault(Panel panel, ref PanelAnimationMapEntry entry) {
		IScheme? scheme = panel.GetScheme();
		if (scheme != null) {
			entry.Set(panel, scheme.GetFont(entry.DefaultValue, panel.IsProportional())!);
		}
	}
}
class ProportionalFloatProperty : IPanelAnimationPropertyConverter
{
	public void GetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		float f = (float)entry.Get(panel);
		f = panel.GetScheme()?.GetProportionalScaledValueEx((int)f) ?? f;
		kv.SetFloat(entry.ScriptName, f);
	}

	public void SetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		float f = kv.GetFloat(entry.ScriptName);
		f = panel.GetScheme()?.GetProportionalScaledValueEx((int)f) ?? f;
		entry.Set(panel, f);
	}

	public void InitFromDefault(Panel panel, ref PanelAnimationMapEntry entry) {
		float.TryParse(entry.DefaultValue, out float f);
		f = panel.GetScheme()?.GetProportionalScaledValueEx((int)f) ?? f;
		entry.Set(panel, f);
	}
}
class ProportialIntProperty : IPanelAnimationPropertyConverter
{
	public void GetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		int i = (int)entry.Get(panel);
		i = panel.GetScheme()?.GetProportionalScaledValueEx(i) ?? i;
		kv.SetInt(entry.ScriptName, i);
	}

	public void SetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		int i = kv.GetInt(entry.ScriptName);
		i = panel.GetScheme()?.GetProportionalScaledValueEx(i) ?? i;
		entry.Set(panel, i);
	}

	public void InitFromDefault(Panel panel, ref PanelAnimationMapEntry entry) {
		_ = int.TryParse(entry.DefaultValue, out int i);
		i = panel.GetScheme()?.GetProportionalScaledValueEx(i) ?? i;
		entry.Set(panel, i);
	}
}
class TextureIdProperty : IPanelAnimationPropertyConverter
{
	public void GetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		object? data = entry.Get(panel);
		if (data == null) return;

		int currentId = (int)data;
		if (currentId != -1 && Surface.DrawGetTextureFile(currentId, out ReadOnlySpan<char> textureName))
			kv.SetString(entry.ScriptName, textureName);
		else
			kv.SetString(entry.ScriptName, "");
	}

	public void SetData(Panel panel, KeyValues kv, ref PanelAnimationMapEntry entry) {
		IScheme? scheme = panel.GetScheme();

		TextureID currentId = -1;
		string textureName = new(kv.GetString(entry.ScriptName));

		if (!string.IsNullOrEmpty(textureName)) {
			currentId = Surface.DrawGetTextureId(textureName);
			if (currentId == -1)
				currentId = Surface.CreateNewTextureID();
			Surface.DrawSetTextureFile(currentId, textureName, 0, true);
		}

		entry.Set(panel, (int)currentId);
	}

	public void InitFromDefault(Panel panel, ref PanelAnimationMapEntry entry) {
		IScheme? scheme = panel.GetScheme();

		TextureID currentId = -1;
		string textureName = entry.DefaultValue;

		if (!string.IsNullOrEmpty(textureName)) {
			currentId = Surface.DrawGetTextureId(textureName);
			if (currentId == -1)
				currentId = Surface.CreateNewTextureID();
			Surface.DrawSetTextureFile(currentId, textureName, 0, true);
		}

		entry.Set(panel, (int)currentId);
	}
}

public readonly struct ScopedPanelWaitCursor : IDisposable
{
	const CursorCode dc_hourglass = CursorCode.Hourglass;

	readonly Panel Panel;
	readonly CursorCode OldCursor;

	public ScopedPanelWaitCursor(Panel panel) {
		Panel = panel;
		OldCursor = panel.GetCursor();
		panel.SetCursor(dc_hourglass);
	}

	public void Dispose() {
		CursorCode nowCursor = Panel.GetCursor();
		if (nowCursor == dc_hourglass)
			Panel.SetCursor(OldCursor);
		else
			Assert(nowCursor == dc_hourglass, $"Expected dc_hourglass cursor, got {nowCursor} one.");
	}
}
