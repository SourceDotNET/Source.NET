using Source;
using Source.Common.Bitmap;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.MaterialSystem;
using Source.GUI.Controls;

using static Source.Common.GarrysMod.Lua.PooledStrings;

#if MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Client.GarrysMod;
#endif

public class HtmlPanel : EditablePanel, IHtmlClientListener
{
	static HtmlPanel() => ChainToAnimationMap<HtmlPanel>();

	readonly IHtmlClient? Client;
	TextureID TextureID;
	int TextureWide;
	int TextureTall;
	bool Loading;
	int CursorX;
	int CursorY;
	IMaterial? Material;
	bool FinishedLoading;

	public HtmlPanel(Panel? parent, ReadOnlySpan<char> panelName) : base(parent, panelName) {
		TextureID = -1;
		TextureWide = 0;
		TextureTall = 0;
		CursorX = 0;
		CursorY = 0;
		Material = null;
		FinishedLoading = false;
		Loading = true;
		Client = get.HTML()?.CreateClient(this);
	}

	public override void Dispose() {
		Client?.Close();
		// if (TextureID != -1)
		// 	Surface.DestroyTextureID(TextureID); // todo, I thought we had this??
		Material?.DecrementReferenceCount();
		TextureID = -1;
		Material = null;
		base.Dispose();
	}

	public override void OnSizeChanged(int newWide, int newTall) {
		if (newWide != 0 && newTall != 0)
			Client?.SetSize(newWide, newTall);
	}

	public override void OnSetFocus() {
		Client?.SetFocused(true);
		base.OnSetFocus();
	}

	public override void OnKillFocus(Panel? newPanel) {
		Client?.SetFocused(false);
		base.OnKillFocus(newPanel);
	}

	IHtmlClient.MouseEvent MouseEvent() => new() { X = CursorX, Y = CursorY, Modifiers = GetKeyModifiers() };

	public override void OnCursorMoved(int x, int y) {
		CursorX = x;
		CursorY = y;
		Client?.SendMouseMoveEvent(MouseEvent(), false);
	}

	public override void OnMousePressed(ButtonCode code) {
		RequestFocus();
		if (!GetMouseButton(code, out IHtmlClient.MouseButton button))
			return;
		Client?.SendMouseClickEvent(MouseEvent(), button, false, 1);
	}

	public override void OnMouseDoublePressed(ButtonCode code) {
		RequestFocus();
		if (!GetMouseButton(code, out IHtmlClient.MouseButton button))
			return;
		Client?.SendMouseClickEvent(MouseEvent(), button, false, 2);
	}

	public override void OnMouseReleased(ButtonCode code) {
		if (!GetMouseButton(code, out IHtmlClient.MouseButton button))
			return;
		Client?.SendMouseClickEvent(MouseEvent(), button, true, 1);
	}

	public override void OnMouseWheeled(int delta) {
		int deltaX = 0, deltaY = 0;
		if (vguiInput.IsKeyDown(ButtonCode.KeyLShift) || vguiInput.IsKeyDown(ButtonCode.KeyRShift))
			deltaX = delta * 100;
		else
			deltaY = delta * 100;
		Client?.SendMouseWheelEvent(MouseEvent(), deltaX, deltaY);
	}

	public override void OnKeyCodePressed(ButtonCode code) { }

	public override void OnKeyCodeTyped(ButtonCode code) {
		if (code == ButtonCode.None)
			return;

		if (code >= ButtonCode.KeyF1 && code <= ButtonCode.KeyF12) {
			// todo: run key binding
			return;
		}

		IHtmlClient.EventModifiers modifiers = GetKeyModifiers();
		if (code >= ButtonCode.KeyPad0 && code <= ButtonCode.KeyPadDecimal)
			modifiers |= IHtmlClient.EventModifiers.Numpad;

		Client?.SendKeyEvent(new IHtmlClient.KeyEvent {
			EventType = IHtmlClient.KeyEvent.Type.KeyDown,
			WindowsKeyCode = KeyCodeToVirtualKey(code),
			NativeKeyCode = KeyCodeToScanCode(code),
			Modifiers = modifiers
		});
	}

	public override void OnKeyTyped(char unichar) {
		Client?.SendKeyEvent(new IHtmlClient.KeyEvent {
			EventType = IHtmlClient.KeyEvent.Type.KeyChar,
			KeyChar = unichar,
			Modifiers = GetKeyModifiers()
		});
	}

	public override void OnKeyCodeReleased(ButtonCode code) {
		if (code == ButtonCode.None || (code >= ButtonCode.KeyF1 && code <= ButtonCode.KeyF12))
			return;

		IHtmlClient.EventModifiers modifiers = GetKeyModifiers();
		if (code >= ButtonCode.KeyPad0 && code <= ButtonCode.KeyPadDecimal)
			modifiers |= IHtmlClient.EventModifiers.Numpad;

		Client?.SendKeyEvent(new IHtmlClient.KeyEvent {
			EventType = IHtmlClient.KeyEvent.Type.KeyUp,
			WindowsKeyCode = KeyCodeToVirtualKey(code),
			NativeKeyCode = KeyCodeToScanCode(code),
			Modifiers = modifiers
		});
	}

	unsafe void UploadTexture(ReadOnlySpan<byte> rgba, int wide, int tall) {
		if (rgba.IsEmpty || wide <= 0 || tall <= 0)
			return;

		fixed (byte* data = rgba) {
			if (TextureID == -1 || TextureWide != wide || TextureTall != tall) {
				if (TextureID != -1)
					Surface.DestroyTextureID(TextureID);
				Material?.DecrementReferenceCount();
				TextureID = -1;
				Material = null;
				TextureID = Surface.CreateNewTextureID(true);
				Surface.DrawSetTextureRGBAEx(TextureID, (nint)data, wide, tall, ImageFormat.BGRA8888);
				// todo: Material from texture mat info, IncrementReferenceCount
				TextureWide = wide;
				TextureTall = tall;
			}
			else
				Surface.DrawUpdateRegionTextureRGBA(TextureID, 0, 0, (nint)data, wide, tall, ImageFormat.BGRA8888);
		}
	}

	public override void Paint() {
		UpdateHTMLTexture();

		if (TextureID != -1) {
			Surface.DrawSetColor(255, 255, 255, 255);
			Surface.DrawSetTexture(TextureID);
			Surface.DrawTexturedRect(0, 0, TextureWide, TextureTall);
		}
	}

	public void OpenURL(ReadOnlySpan<char> url) => Client?.LoadUrl(url);

	public void SetHTML(ReadOnlySpan<char> html) => Client?.SetHtml(html);

	public void Refresh() => Client?.Refresh();

	public void StopLoading() => Client?.Stop();

	public void GoBack() => Client?.GoBack();

	public void GoForward() => Client?.GoForward();

	public void RunJavascript(ReadOnlySpan<char> script) => Client?.RunJavaScript(script);

	public void NewObjectCallback(ReadOnlySpan<char> objName, ReadOnlySpan<char> funcName) => Client?.RegisterJavaScriptFunction(objName, funcName);

	public void SetOpenLinksExternally(bool openLinksExternally) => Client?.SetOpenLinksExternally(openLinksExternally);

	public bool IsLoading() => Loading;

	public IMaterial? GetHTMLMaterial() => FinishedLoading ? Material : null;

	public void UpdateHTMLTexture() {
		if (Client == null || !Client.LockImageData())
			return;

		ReadOnlySpan<byte> rgba = Client.GetImageData(out int wide, out int tall);
		UploadTexture(rgba, wide, tall);
		Client.UnlockImageData();
	}

	static bool GetMouseButton(ButtonCode code, out IHtmlClient.MouseButton button) {
		switch (code) {
			case ButtonCode.MouseLeft: button = IHtmlClient.MouseButton.Left; return true;
			case ButtonCode.MouseRight: button = IHtmlClient.MouseButton.Right; return true;
			case ButtonCode.MouseMiddle: button = IHtmlClient.MouseButton.Middle; return true;
		}
		button = default;
		return false;
	}

	static IHtmlClient.EventModifiers GetKeyModifiers() {
		IHtmlClient.EventModifiers modifiers = vguiInput.IsKeyDown(ButtonCode.KeyLShift) ? IHtmlClient.EventModifiers.Shift : IHtmlClient.EventModifiers.None;
		if (vguiInput.IsKeyDown(ButtonCode.KeyRShift))
			modifiers |= IHtmlClient.EventModifiers.ShiftR;
		if (vguiInput.IsKeyDown(ButtonCode.KeyLControl))
			modifiers |= IHtmlClient.EventModifiers.Control;
		if (vguiInput.IsKeyDown(ButtonCode.KeyRControl))
			modifiers |= IHtmlClient.EventModifiers.ControlR;
		if (vguiInput.IsKeyDown(ButtonCode.KeyLAlt))
			modifiers |= IHtmlClient.EventModifiers.Alt;
		if (vguiInput.IsKeyDown(ButtonCode.KeyRAlt))
			modifiers |= IHtmlClient.EventModifiers.AltGr;
		if (vguiInput.IsMouseDown(ButtonCode.MouseLeft))
			modifiers |= IHtmlClient.EventModifiers.LeftMouse;
		if (vguiInput.IsMouseDown(ButtonCode.MouseMiddle))
			modifiers |= IHtmlClient.EventModifiers.MiddleMouse;
		if (vguiInput.IsMouseDown(ButtonCode.MouseRight))
			modifiers |= IHtmlClient.EventModifiers.RightMouse;
		return modifiers;
	}

	static readonly int[] ScanCodes = [
		0x0000, 0x000b, 0x0002, 0x0003, 0x0004, 0x0005, 0x0006, 0x0007, 0x0008, 0x0009,
		0x000a, 0x001e, 0x0030, 0x002e, 0x0020, 0x0012, 0x0021, 0x0022, 0x0023, 0x0017,
		0x0024, 0x0025, 0x0026, 0x0032, 0x0031, 0x0018, 0x0019, 0x0010, 0x0013, 0x001f,
		0x0014, 0x0016, 0x002f, 0x0011, 0x002d, 0x0015, 0x002c, 0x0052, 0x004f, 0x0050,
		0x0051, 0x004b, 0x004c, 0x004d, 0x0047, 0x0048, 0x0049, 0x0000, 0x0000, 0x0000,
		0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
		0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
		0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
		0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0xe048, 0xe04b,
		0xe050, 0xe04d
	];

	static readonly int[] VirtualKeys = [
		0x00, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38,
		0x39, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
		0x4a, 0x4b, 0x4c, 0x4d, 0x4e, 0x4f, 0x50, 0x51, 0x52, 0x53,
		0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x60, 0x61, 0x62,
		0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6f, 0x6a, 0x6d,
		0x6b, 0x0d, 0x6e, 0xdb, 0xdd, 0xba, 0xde, 0xc0, 0xbc, 0xbe,
		0xbf, 0xdc, 0x6d, 0xbb, 0x0d, 0x20, 0x08, 0x09, 0x14, 0x90,
		0x1b, 0x91, 0x2d, 0x2e, 0x24, 0x23, 0x21, 0x22, 0x13, 0xa0,
		0xa1, 0xa4, 0xa5, 0xa2, 0xa3, 0x5b, 0x5c, 0x5d, 0x26, 0x25,
		0x28, 0x27
	];

	static int KeyCodeToScanCode(ButtonCode code) => (uint)code < ScanCodes.Length ? ScanCodes[(int)code] : 0;
	static int KeyCodeToVirtualKey(ButtonCode code) => (uint)code < VirtualKeys.Length ? VirtualKeys[(int)code] : 0;

	void CallStringHook(LUA_POOLEDSTRING hook, ReadOnlySpan<char> value) {
		if (!PushLuaHook(hook))
			return;

		PushLua(Lua!, LuaType.Panel);
		Lua!.PushString(value);
		Lua.CallInternalNoReturns(2);
	}

	public void OnAddressChange(ReadOnlySpan<char> address) => CallStringHook(LUA_POOLEDSTRING.OnChangeAddressBar, address);

	public void OnConsoleMessage(ReadOnlySpan<char> message, ReadOnlySpan<char> source, int line, int level) {
		ReadOnlySpan<char> levelName = "log";
		if (level == 1)
			levelName = "debug";
		else if (level == 3)
			levelName = "warn";
		else if (level == 4)
			levelName = "error";

		if (message.StartsWith("RUNLUA:") && PushLuaHook(LUA_POOLEDSTRING.ConsoleMessage)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushString(message);
			Lua.CallInternalNoReturns(2);
			return;
		}

		if (PushLuaHook(LUA_POOLEDSTRING.ConsoleMessage)) {
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushString(message);
			Lua.PushString(source);
			Lua.PushNumber(line);
			Lua.PushString(levelName);
			Lua.CallInternalNoReturns(5);
			return;
		}

		ConColorMsg(new Color(255, 160, 255, 255), "[HTML] ");
		if (level == 1)
			ConColorMsg(new Color(160, 160, 160, 255), "[Debug] ");
		else if (level == 3)
			ConColorMsg(new Color(255, 255, 90, 255), "[Warn]  ");
		else if (level == 4)
			ConColorMsg(new Color(255, 90, 90, 255), "[Error] ");

		string sourceStr = new(source);
		if (sourceStr.Length > 64)
			sourceStr = $"{sourceStr[..32]}...{sourceStr[^32..]}";

		ConColorMsg(new Color(210, 210, 210, 255), "{0}:{1}: ", sourceStr, line);
		ConColorMsg(new Color(255, 255, 255, 255), "{0}\n", new string(message));
	}

	public void OnTitleChange(ReadOnlySpan<char> title) => CallStringHook(LUA_POOLEDSTRING.OnChangeTitle, title);

	public void OnTargetUrlChange(ReadOnlySpan<char> url) => CallStringHook(LUA_POOLEDSTRING.OnChangeTargetURL, url);

	public void OnCursorChange(IHtmlClientListener.CursorType cursor) {
		CursorCode code = cursor switch {
			IHtmlClientListener.CursorType.Crosshair => CursorCode.Crosshair,
			IHtmlClientListener.CursorType.Hand => CursorCode.Hand,
			IHtmlClientListener.CursorType.IBeam => CursorCode.IBeam,
			IHtmlClientListener.CursorType.Hourglass => CursorCode.Hourglass,
			IHtmlClientListener.CursorType.SizeEW => CursorCode.SizeWE,
			IHtmlClientListener.CursorType.SizeNS => CursorCode.SizeNS,
			IHtmlClientListener.CursorType.SizeNWSE => CursorCode.SizeNWSE,
			IHtmlClientListener.CursorType.SizeNESW => CursorCode.SizeNESW,
			IHtmlClientListener.CursorType.SizeAll => CursorCode.SizeAll,
			IHtmlClientListener.CursorType.No => CursorCode.No,
			IHtmlClientListener.CursorType.None => CursorCode.None,
			_ => CursorCode.Arrow
		};
		SetCursor(code);
	}

	public void OnLoadStart(ReadOnlySpan<char> address) {
		Loading = true;
		CallStringHook(LUA_POOLEDSTRING.OnBeginLoadingDocument, address);
	}

	public void OnLoadEnd(ReadOnlySpan<char> address) {
		Loading = false;
		FinishedLoading = true;
		CallStringHook(LUA_POOLEDSTRING.OnFinishLoadingDocument, address);
	}

	public void OnDocumentReady(ReadOnlySpan<char> address) => CallStringHook(LUA_POOLEDSTRING.OnDocumentReady, address);

	public void OnCreateChildView(ReadOnlySpan<char> sourceUrl, ReadOnlySpan<char> targetUrl, bool isPopup) {
		if (!PushLuaHook(LUA_POOLEDSTRING.OnChildViewCreated))
			return;

		PushLua(Lua!, LuaType.Panel);
		Lua!.PushString(sourceUrl);
		Lua.PushString(targetUrl);
		Lua.PushBool(isPopup);
		Lua.CallInternalNoReturns(4);
	}

	public JSValue OnJavaScriptCall(ReadOnlySpan<char> objName, ReadOnlySpan<char> funcName, in JSValue parameters) {
		if (parameters.GetJSType() != JSValue.Type.Array)
			Error("HtmlPanel::OnJavaScriptCall encounted non array parameter list\n");

		IHtmlSystem html = get.HTML()!;

		if (Lua == null)
			return html.CreateArray([]);

		int top = Lua.Top();

		if (!PushLuaHook(LUA_POOLEDSTRING.OnCallback))
			return html.CreateArray([]);

		PushLua(Lua, LuaType.Panel);
		Lua.PushString(objName);
		Lua.PushString(funcName);

		Lua.CreateTable();
		ReadOnlySpan<JSValue> args = parameters.GetArray();
		for (int i = 0; i < args.Length; i++) {
			Lua.PushNumber(i + 1);
			PushJSValue(Lua, args[i]);
			Lua.SetTable(-3);
		}

		if (!Lua.CallFunctionProtected(4, -1, true))
			return html.CreateArray([]);

		int returns = Lua.Top() - top;
		JSValue[] values = new JSValue[Math.Max(returns, 0)];
		for (int i = 0; i < values.Length; i++)
			if (!LuaToJSValue(Lua, html, out values[i], -values.Length + i))
				values[i] = default;

		Lua.Pop(values.Length);
		return html.CreateArray(values);
	}

	static void PushJSValue(ILuaInterface lua, in JSValue value, int depth = 0) {
		if (depth > 16) {
			lua.PushNil();
			return;
		}

		switch (value.GetJSType()) {
			case JSValue.Type.Bool:
				lua.PushBool(value.GetBool());
				break;
			case JSValue.Type.Number:
				lua.PushNumber(value.GetNumber());
				break;
			case JSValue.Type.String:
				lua.PushString(value.GetString());
				break;
			case JSValue.Type.Array: {
					lua.CreateTable();
					ReadOnlySpan<JSValue> array = value.GetArray();
					for (int i = 0; i < array.Length; i++) {
						lua.PushNumber(i + 1);
						PushJSValue(lua, array[i], depth + 1);
						lua.SetTable(-3);
					}
					break;
				}
			case JSValue.Type.HashMap: {
					lua.CreateTable();
					if (value.HashMap_Begin(out ReadOnlySpan<char> key, out JSValue entry)) {
						do {
							lua.PushString(key);
							PushJSValue(lua, entry, depth + 1);
							lua.SetTable(-3);
						}
						while (value.HashMap_Next(out key, out entry));
					}
					break;
				}
			default:
				lua.PushNil();
				break;
		}
	}

	static bool LuaToJSValue(ILuaInterface lua, IHtmlSystem html, out JSValue value, int index, int depth = 0) {
		value = default;

		if (depth > 16)
			return false;

		switch (lua.GetType(index)) {
			case LuaType.Bool:
				value = new JSValue(lua.GetBool(index));
				return true;
			case LuaType.Number:
				value = new JSValue(lua.GetNumber(index));
				return true;
			case LuaType.String:
				value = html.CreateString(lua.GetString(index));
				return true;
			case LuaType.Table:
				break;
			default:
				return true;
		}

		lua.Push(index);

		int sequential = 0;
		while (true) {
			lua.PushNumber(sequential + 1);
			lua.GetTable(-2);
			bool isNil = lua.IsType(-1, LuaType.Nil);
			lua.Pop(1);
			if (isNil)
				break;
			sequential++;
		}

		int total = 0;
		lua.PushNil();
		while (lua.Next(-2)) {
			lua.Pop(1);
			total++;
		}

		if (sequential == total) {
			JSValue[] list = new JSValue[sequential];
			for (int i = 0; i < sequential; i++) {
				lua.PushNumber(i + 1);
				lua.GetTable(-2);
				if (!LuaToJSValue(lua, html, out list[i], -1, depth + 1)) {
					lua.Pop(2);
					return false;
				}
				lua.Pop(1);
			}
			lua.Pop(1);
			value = html.CreateArray(list);
			return true;
		}

		List<string> keys = new(total);
		List<JSValue> values = new(total);
		lua.PushNil();
		while (lua.Next(-2)) {
			lua.Push(-2);
			keys.Add(lua.GetString(-1) ?? "");
			lua.Pop(1);

			if (!LuaToJSValue(lua, html, out JSValue entry, -1, depth + 1)) {
				lua.Pop(3);
				return false;
			}
			values.Add(entry);
			lua.Pop(1);
		}
		lua.Pop(1);

		value = html.CreateHashMap(keys.ToArray(), values.ToArray());
		return true;
	}
}
