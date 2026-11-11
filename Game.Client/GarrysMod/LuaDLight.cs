using Source;
using Source.Common;
using Source.Common.GarrysMod.Lua;

namespace Game.Client.GarrysMod;

public static partial class LuaDLight
{
	[LuaClass(typeof(DLight), NullError = "Tried to use a NULL dlight_t!")]
	public static readonly LuaClass LC_dlight_t = new("dlight_t", LuaType.DLight, null, null);

	[LuaGlobal]
	static int DynamicLight(ILuaInterface lua) {
		int key = (int)lua.CheckNumber(1);
		DLight? light;
		if (lua.GetType(2) != LuaType.Nil && lua.GetBool(2))
			light = effects.AllocElight(key);
		else
			light = effects.AllocDlight(key);
		if (light == null)
			return 0;
		LC_dlight_t.Push(light);
		return 1;
	}

	[LuaMethod]
	static int dlight_t____tostring(ILuaInterface lua) {
		DLight? light = (DLight?)LC_dlight_t.Get(1);
		if (light == null) {
			lua.Error("Tried to use a NULL dlight_t!");
			return 0;
		}
		lua.PushString($"dlight_t [{light.Key}]");
		return 1;
	}

	[LuaMethod]
	static int dlight_t____newindex(ILuaInterface lua) {
		DLight? light = (DLight?)LC_dlight_t.Get(1);
		if (light == null) {
			lua.Error("Tried to use a NULL dlight_t!");
			return 0;
		}

		string key = lua.CheckString(2);
		if (key.Length == 0)
			return 0;

		if (stricmp(key, "pos") == 0 || stricmp(key, "position") == 0) {
			light.Origin = LuaVector.Get_Vector(3);
			return 0;
		}
		if (stricmp(key, "noworld") == 0) {
			if (lua.GetBool(3))
				light.Flags |= DLightFlags.NoWorldIllumination;
			else
				light.Flags &= ~DLightFlags.NoWorldIllumination;
			return 0;
		}
		if (stricmp(key, "nomodel") == 0) {
			if (lua.GetBool(3))
				light.Flags |= DLightFlags.NoModelIllumination;
			else
				light.Flags &= ~DLightFlags.NoModelIllumination;
			return 0;
		}
		if (stricmp(key, "dir") == 0 || stricmp(key, "direction") == 0) {
			light.Direction = LuaVector.Get_Vector(3);
			return 0;
		}
		if (stricmp(key, "radius") == 0 || stricmp(key, "size") == 0) {
			float radius = (float)lua.GetNumber(3);
			if (radius <= 0.0f)
				radius = 0.0f;
			if (2048.0f <= radius)
				radius = 2048.0f;
			light.Radius = radius;
			return 0;
		}
		if (stricmp(key, "die") == 0 || stricmp(key, "dietime") == 0) {
			light.Die = lua.GetNumber(3);
			return 0;
		}
		if (stricmp(key, "decay") == 0 || stricmp(key, "fade") == 0) {
			light.Decay = (float)lua.GetNumber(3);
			return 0;
		}
		if (stricmp(key, "minlight") == 0) {
			light.MinLight = (float)lua.GetNumber(3);
			return 0;
		}
		if (stricmp(key, "style") == 0) {
			light.Style = (int)lua.GetNumber(3);
			return 0;
		}
		if (stricmp(key, "key") == 0) {
			light.Key = (int)lua.GetNumber(3);
			return 0;
		}
		if (stricmp(key, "InnerAngle") == 0) {
			light.InnerAngle = (float)lua.GetNumber(3);
			return 0;
		}
		if (stricmp(key, "OuterAngle") == 0) {
			light.OuterAngle = (float)lua.GetNumber(3);
			return 0;
		}
		if (stricmp(key, "r") == 0) {
			light.Color.R = (byte)(int)lua.GetNumber(3);
			return 0;
		}
		if (stricmp(key, "g") == 0) {
			light.Color.G = (byte)(int)lua.GetNumber(3);
			return 0;
		}
		if (stricmp(key, "b") == 0) {
			light.Color.B = (byte)(int)lua.GetNumber(3);
			return 0;
		}
		if (stricmp(key, "Brightness") == 0) {
			light.Color.Exponent = (sbyte)(int)lua.GetNumber(3);
			return 0;
		}

		lua.ErrorNoHalt($"Tried to set wrong key '{key}' on Dynamic Light object.\n Valid keys: Pos, Dir, Size, DieTime, Brightness, Decay, MinLight, Style, Key, InnerAngle, OuterAngle, r, g, b, nomodel, noworld.\n");
		return 0;
	}
}
