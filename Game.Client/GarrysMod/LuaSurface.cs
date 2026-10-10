using Game.Client.HUD;

using Source;
using Source.Common.GarrysMod.Lua;
using Source.Common.GUI;
using Source.Common.MaterialSystem;
using Source.Common.SoundEmitterSystem;

using System.Runtime.CompilerServices;

namespace Game.Client.GarrysMod;

public static partial class LuaSurface
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_surface = new("surface");

	static readonly List<int> TextureIDs = [];

	static Color GetColor(ILuaInterface lua, int stackPos) {
		if (lua.GetType(stackPos) == LuaType.Table) {
			ILuaObject obj = lua.GetObject(stackPos)!;
			byte a = (byte)obj.GetMemberInt("a", 255);
			byte b = (byte)obj.GetMemberInt("b", 255);
			byte g = (byte)obj.GetMemberInt("g", 255);
			byte r = (byte)obj.GetMemberInt("r", 255);
			return new(r, g, b, a);
		}

		int alpha = 255;
		if (lua.GetType(stackPos + 3) == LuaType.Number)
			alpha = (int)lua.GetNumber(stackPos + 3);
		int blue = (int)lua.GetNumber(stackPos + 2);
		int green = (int)lua.GetNumber(stackPos + 1);
		int red = (int)lua.GetNumber(stackPos);
		return new((byte)Math.Clamp(red, 0, 255), (byte)Math.Clamp(green, 0, 255), (byte)Math.Clamp(blue, 0, 255), (byte)Math.Clamp(alpha, 0, 255));
	}

	[LuaFunction]
	static int CreateFont(ILuaInterface lua) {
		string? name = lua.GetString(1);
		if (name == null)
			return 0;

		LuaObject data = new();
		data.SetFromStack(2);
		if (!data.isTable())
			lua.TypeError("table", 2);
		else if (data.GetMemberStr("font", "")!.Length >= 32)
			lua.ArgError(2, "font name is too long");
		else
			LuaFonts.CreateFont(name, data.GetMemberStr("font", "Arial"), data.GetMemberBool("extended", false), data.GetMemberFloat("size", 13), data.GetMemberFloat("weight", 500), data.GetMemberFloat("blursize", 0), data.GetMemberFloat("scanlines", 0), data.GetMemberBool("antialias", true), data.GetMemberBool("underline", false), data.GetMemberBool("italic", false), data.GetMemberBool("strikeout", false), data.GetMemberBool("symbol", false), data.GetMemberBool("rotary", false), data.GetMemberBool("shadow", false), data.GetMemberBool("additive", false), data.GetMemberBool("outline", false));

		data.UnReference();
		return 0;
	}

	[LuaFunction]
	static int SetDrawColor(ILuaInterface lua) {
		surface.DrawSetColor(GetColor(lua, 1));
		return 0;
	}

	[LuaFunction]
	static int GetDrawColor(ILuaInterface lua) {
		surface.DrawGetColor(out Color color);
		lua.PushColor(color);
		return 1;
	}

	[LuaFunction]
	static void DrawRect([LuaGet] int x, [LuaGet] int y, [LuaGet] int w, [LuaGet] int h) => surface.DrawFilledRect(x, y, x + w, y + h);

	[LuaFunction]
	static void DrawOutlinedRect([LuaGet] int x, [LuaGet] int y, [LuaGet] int w, [LuaGet] int h, [LuaOpt<int>(1)] int thickness) {
		surface.DrawFilledRect(x, y, x + w, thickness + y);
		surface.DrawFilledRect(x, h - thickness + y, x + w, h + y);
		surface.DrawFilledRect(x, thickness + y, thickness + x, h - thickness + y);
		surface.DrawFilledRect(w - thickness + x, thickness + y, x + w, h - thickness + y);
	}

	[LuaFunction]
	static int DrawLine(ILuaInterface lua) {
		float y1 = (float)lua.CheckNumber(4);
		float x1 = (float)lua.CheckNumber(3);
		float y0 = (float)lua.CheckNumber(2);
		float x0 = (float)lua.CheckNumber(1);
		surface.DrawLine((int)x0, (int)y0, (int)x1, (int)y1);
		return 0;
	}

	[LuaFunction]
	static int SetTextColor(ILuaInterface lua) {
		surface.DrawSetTextColor(GetColor(lua, 1));
		return 0;
	}

	[LuaFunction]
	static int GetTextColor(ILuaInterface lua) {
		surface.DrawGetTextColor(out Color color);
		lua.PushColor(color);
		return 1;
	}

	[LuaFunction]
	static void SetTextPos([LuaGet] int x, [LuaGet] int y) => surface.DrawSetTextPos(x, y);

	[LuaFunction]
	static (int, int) GetTextPos() {
		surface.DrawGetTextPos(out int x, out int y);
		return (x, y);
	}

	struct LocalizeCacheEntry
	{
		public string Name;
		public string Text;
	}

	static readonly List<LocalizeCacheEntry> LocalizeCache = [];

	static ReadOnlySpan<char> LocalizeFind(ReadOnlySpan<char> token) {
		ReadOnlySpan<char> found = localize.Find(token);
		if (!found.IsEmpty)
			return found;

		foreach (LocalizeCacheEntry entry in LocalizeCache)
			if (token.SequenceEqual(entry.Name))
				return entry.Text;

		found = localize.Find($"#@{token[1..]}");
		if (found.IsEmpty)
			return null;

		int start = 0;
		for (int i = 0; i < found.Length; i++) {
			if (found[i] == '{')
				start = i;
			else if (found[i] == '}') {
				if (start == 0 || i == 0)
					break;

				ReadOnlySpan<char> binding = found[(start + 1)..i];
				if (binding.Length > 0 && binding[0] == '+')
					binding = binding[1..];
				ReadOnlySpan<char> key = engine.Key_LookupBinding(binding);
				if (key.IsEmpty)
					key = "< NONE >";

				string text = $"{found[..start]}{key.ToString().ToUpperInvariant()}{found[(i + 1)..]}";
				LocalizeCache.Add(new() { Name = new(token), Text = text });
				return text;
			}
		}

		return null;
	}

	[InlineArray(2048)] struct InlineArrayDrawTextBuffer { char first; }
	static InlineArrayDrawTextBuffer DrawTextBuffer;

	[LuaFunction]
	static int DrawText(ILuaInterface lua) {
		string text = lua.CheckString(1);

		FontDrawType drawType = FontDrawType.Default;
		if (lua.GetType(2) != LuaType.Nil)
			drawType = lua.GetBool(2) ? FontDrawType.Additive : FontDrawType.NonAdditive;

		if (text.Length > 0 && text[0] == '#') {
			if (filesystem.Language().GetString(text.AsSpan(1), DrawTextBuffer)) {
				surface.DrawPrintText(((ReadOnlySpan<char>)DrawTextBuffer).SliceNullTerminatedString(), drawType);
				return 0;
			}

			ReadOnlySpan<char> localized = LocalizeFind(text);
			if (!localized.IsEmpty) {
				surface.DrawPrintText(localized, drawType);
				return 0;
			}
		}

		surface.DrawPrintText(text, drawType);
		return 0;
	}

	[LuaFunction]
	[LuaGlobal("ScrW")]
	static int ScreenWidth() {
		surface.GetScreenSize(out int wide, out _);
		return wide;
	}

	[LuaFunction]
	[LuaGlobal("ScrH")]
	static int ScreenHeight() {
		surface.GetScreenSize(out _, out int tall);
		return tall;
	}

	static IFont? CurrentFont;

	[LuaFunction]
	static int GetTextSize(ILuaInterface lua) {
		if (CurrentFont == null)
			return 0;

		string? text = lua.CheckString(1);
		if (text == null) {
			lua.PushNumber(0);
			lua.PushNumber(0);
			return 2;
		}

		int wide, tall;
		if (text.Length > 0 && text[0] == '#' && !filesystem.Language().GetString(text.AsSpan(1), DrawTextBuffer)) {
			ReadOnlySpan<char> localized = LocalizeFind(text);
			if (!localized.IsEmpty) {
				surface.GetTextSize(CurrentFont, localized, out wide, out tall);
				lua.PushNumber(wide);
				lua.PushNumber(tall);
				return 2;
			}
			text.AsSpan().ClampedCopyTo(DrawTextBuffer);
			((Span<char>)DrawTextBuffer)[Math.Min(text.Length, 2047)] = '\0';
		}
		else if (text.Length == 0 || text[0] != '#') {
			text.AsSpan().ClampedCopyTo(DrawTextBuffer);
			((Span<char>)DrawTextBuffer)[Math.Min(text.Length, 2047)] = '\0';
		}

		surface.GetTextSize(CurrentFont, ((ReadOnlySpan<char>)DrawTextBuffer).SliceNullTerminatedString(), out wide, out tall);
		lua.PushNumber(wide);
		lua.PushNumber(tall);
		return 2;
	}

	[LuaFunction]
	static int SetFont(ILuaInterface lua) {
		string name = lua.CheckString(1);
		CurrentFont = LuaFonts.GetFont(name);
		if (CurrentFont == null) {
			CurrentFont = GModBase.GetGModBasePanel(true)!.GetScheme()!.GetFont(name, false);
			if (CurrentFont == null) {
				lua.ErrorFromLua($"'{name}' isn't a valid font\n");
				return 0;
			}
		}
		surface.DrawSetTextFont(CurrentFont);
		return 0;
	}

	[LuaFunction]
	static int GetTextureID([LuaGet] string? name) {
		int id = surface.DrawGetTextureId(name);
		if (id == -1) {
			id = (int)surface.CreateNewTextureID(false);
			surface.DrawSetTextureFile(id, name, 0, false);
			TextureIDs.Add(id);
		}
		return id;
	}

	[LuaFunction]
	static string GetTextureNameByID([LuaGet] int id) {
		if (!surface.DrawGetTextureFile(id, out ReadOnlySpan<char> filename))
			return "";
		return new(filename);
	}

	[LuaFunction]
	static void SetTexture([LuaGet] int id) => surface.DrawSetTexture(id);

	static int MaterialTextureID = -1;

	[LuaFunction]
	static void SetMaterial(IMaterial material) {
		if (MaterialTextureID == -1)
			MaterialTextureID = (int)surface.CreateNewTextureID(false);
		surface.DrawSetTextureMaterial(MaterialTextureID, material);
		surface.DrawSetTexture(MaterialTextureID);
	}

	[LuaFunction]
	static (int, int) GetTextureSize([LuaGet] int id) {
		surface.DrawGetTextureSize(id, out int wide, out int tall);
		return (wide, tall);
	}

	[LuaFunction]
	static int GetHUDTexture(ILuaInterface lua) {
		HudTexture? icon = gHUD.GetIcon(lua.CheckString(1));
		if (icon == null)
			return 0;
		lua.PushNumber((int)icon.TextureID);
		return 1;
	}

	[LuaFunction]
	static int DrawTexturedRect(ILuaInterface lua) {
		int x = (int)lua.CheckNumber(1);
		int y = (int)lua.CheckNumber(2);
		int w = (int)lua.CheckNumber(3);
		int h = (int)lua.CheckNumber(4);
		// TODO: poster cmd split scaling (?)
		surface.DrawTexturedRect(x, y, x + w, y + h);
		return 0;
	}

	[InlineArray(4)] struct InlineArrayRotatedVerts { SurfaceVertex first; }
	static InlineArrayRotatedVerts RotatedVerts;

	[LuaFunction]
	static int DrawTexturedRectRotated(ILuaInterface lua) {
		int x = (int)lua.CheckNumber(1);
		int y = (int)lua.CheckNumber(2);
		float w = (int)lua.CheckNumber(3);
		float h = (int)lua.CheckNumber(4);
		float rotation = (float)lua.CheckNumber(5);

		(float sin, float cos) = MathF.SinCos(-rotation * 0.017453292f);

		RotatedVerts[0].Position = new(cos * w * -0.5f + x + -sin * h * -0.5f, sin * w * -0.5f + y + cos * h * -0.5f);
		RotatedVerts[0].TexCoord = new(0, 0);
		RotatedVerts[1].Position = new(cos * w + RotatedVerts[0].Position.X, sin * w + RotatedVerts[0].Position.Y);
		RotatedVerts[1].TexCoord = new(1, 0);
		RotatedVerts[2].Position = new(-sin * h + RotatedVerts[1].Position.X, RotatedVerts[1].Position.Y + cos * h);
		RotatedVerts[2].TexCoord = new(1, 1);
		RotatedVerts[3].Position = new(-sin * h + RotatedVerts[0].Position.X, RotatedVerts[0].Position.Y + cos * h);
		RotatedVerts[3].TexCoord = new(0, 1);

		surface.DrawTexturedPolygon(RotatedVerts, true);
		return 0;
	}

	[LuaFunction]
	static int PlaySound(ILuaInterface lua) {
		ReadOnlySpan<char> sound = lua.CheckString(1);
		int index = soundemitterbase.GetSoundIndex(sound);
		if (soundemitterbase.IsValidIndex(index)) {
			ref SoundParametersInternal internalParams = ref soundemitterbase.InternalGetParametersForSound(index);
			Span<SoundFile> soundNames = internalParams.GetSoundNames();
			int pick = RandomInt(0, soundNames.Length - 1);
			sound = soundemitterbase.GetWaveName(soundNames[pick].Symbol);
		}
		surface.PlaySound(sound);
		return 0;
	}
	[InlineArray(4096)] struct InlineArrayPolyVerts { SurfaceVertex first; }
	static InlineArrayPolyVerts PolyVerts;

	[LuaFunction]
	static int DrawPoly(ILuaInterface lua) {
		LuaObject vertices = new();
		vertices.SetFromStack(1);
		if (!vertices.isTable()) {
			lua.TypeError("table", 1);
			vertices.UnReference();
			return 0;
		}

		int count = 0;
		for (int i = 1; i < 4096; i++) {
			LuaObject vertex = new();
			vertices.GetMember(i, vertex);
			if (!vertex.isTable()) {
				vertex.UnReference();
				break;
			}

			PolyVerts[count].Position = new(vertex.GetMemberFloat("x", 0), vertex.GetMemberFloat("y"));
			PolyVerts[count].TexCoord = new(vertex.GetMemberFloat("u"), vertex.GetMemberFloat("v"));
			count++;
			vertex.UnReference();
		}

		surface.DrawTexturedPolygon(((Span<SurfaceVertex>)PolyVerts)[..count], true);
		vertices.UnReference();
		return 0;
	}
	[LuaFunction]
	static int DisableClipping(ILuaInterface lua) {
		surface.GetClippingRect(out _, out _, out _, out _, out bool clippingDisabled);
		surface.DisableClipping(lua.GetBool(1));
		lua.PushBool(clippingDisabled);
		return 1;
	}

	[LuaFunction]
	static int DrawCircle(ILuaInterface lua) {
		double x = lua.CheckNumber(1);
		double y = lua.CheckNumber(2);
		double radius = lua.CheckNumber(3);
		if (lua.GetType(4) != LuaType.Nil)
			surface.DrawSetColor(GetColor(lua, 4));

		float segments = Math.Clamp(MathF.Abs((float)radius), 8, 64);
		surface.DrawOutlinedCircle((int)x, (int)y, (int)radius, (int)segments);
		return 0;
	}
	[LuaFunction]
	static int DrawTexturedRectUV(ILuaInterface lua) {
		int x = (int)lua.GetNumber(1);
		int y = (int)lua.GetNumber(2);
		int w = (int)lua.GetNumber(3);
		int h = (int)lua.GetNumber(4);
		// TODO: poster cmd split scaling (?)
		surface.DrawTexturedSubRect(x, y, w + x, h + y, (float)lua.GetNumber(5), (float)lua.GetNumber(6), (float)lua.GetNumber(7), (float)lua.GetNumber(8));
		return 0;
	}

	[LuaFunction]
	static void SetAlphaMultiplier([LuaGet] float alpha) => surface.DrawSetAlphaMultiplier(alpha);

	[LuaFunction]
	static float GetAlphaMultiplier() => surface.DrawGetAlphaMultiplier();

	[LuaFunction]
	static int GetPanelPaintState(ILuaInterface lua) {
		LuaTable table = new(null, 0);
		surface.DrawGetTranslate(out int translateX, out int translateY);
		table.SetMember("translate_x", translateX);
		table.SetMember("translate_y", translateY);
		surface.GetClippingRect(out int left, out int top, out int right, out int bottom, out bool clippingDisabled);
		table.SetMember("scissor_left", left);
		table.SetMember("scissor_top", top);
		table.SetMember("scissor_right", right);
		table.SetMember("scissor_bottom", bottom);
		table.SetMember("scissor_enabled", !clippingDisabled);
		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaFunction]
	static int GetScissorRect(ILuaInterface lua) {
		surface.GetClippingRect(out int left, out int top, out int right, out int bottom, out bool clippingDisabled);
		lua.PushBool(!clippingDisabled);
		lua.PushNumber(left);
		lua.PushNumber(top);
		lua.PushNumber(right);
		lua.PushNumber(bottom);
		return 5;
	}
}
