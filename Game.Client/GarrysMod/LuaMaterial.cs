using Source;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Game.Client.GarrysMod;

public static partial class LuaMaterial
{
	[LuaClass(typeof(IMaterial), NullError = "Tried to use a NULL IMaterial!")]
	public static readonly LuaClass LC_IMaterial = new("IMaterial", LuaType.Material, null, null);

	[LuaMethod]
	static string IMaterial__GetName(IMaterial material) => new(material.GetName());

	[LuaMethod]
	static int IMaterial__GetShader(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		material.IsTwoSided();
		lua.PushString(material.GetShaderName());
		return 1;
	}

	[LuaMethod]
	static int IMaterial__GetString(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		string? value = var.GetStringValue();
		if (value == null || (value.Length > 0 && value[0] == '<'))
			return 0;

		lua.PushString(value);
		return 1;
	}

	[LuaMethod]
	static bool IMaterial__IsError(IMaterial material) => material.IsErrorMaterialInternal();

	static readonly TextureReference ErrorTexture = new();

	static ITexture? GetTextureValue(IMaterialVar var) {
		if (!var.IsTexture()) {
			if (!ErrorTexture.IsValid())
				ErrorTexture.Init("error", "Other textures", true);
			return ErrorTexture.Get();
		}
		return var.GetTextureValue();
	}

	[LuaMethod]
	static int IMaterial__GetColor(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null)
			lua.Error("Tried to use a NULL IMaterial!");

		IMaterialVar var = material.FindVar("$basetexture", out bool found, false);
		if (var == null || !found)
			return 0;

		ITexture? texture = GetTextureValue(var);
		if (texture == null)
			return 0;

		Color color = default;
		if (get.Resources() != null)
			color = get.Resources()!.GetTextureColour(texture, (int)lua.GetNumber(2), (int)lua.GetNumber(3));
		lua.PushColor(color);
		return 1;
	}

	[LuaMethod]
	static int IMaterial__Width(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar("$basetexture", out bool found, false);
		if (var == null || !found)
			return 0;

		ITexture? texture = GetTextureValue(var);
		if (texture == null)
			return 0;

		lua.PushNumber(texture.GetActualWidth());
		return 1;
	}

	[LuaMethod]
	static int IMaterial__Height(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar("$basetexture", out bool found, false);
		if (var == null || !found)
			return 0;

		ITexture? texture = GetTextureValue(var);
		if (texture == null)
			return 0;

		lua.PushNumber(texture.GetActualHeight());
		return 1;
	}

	[LuaMethod]
	static int IMaterial__GetTexture(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null)
			lua.Error("Tried to use a NULL IMaterial!");

		IMaterialVar var = material.FindVar(lua.GetString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		ITexture? texture = var.GetTextureValue();
		if (texture == null || texture.IsError())
			return 0;

		texture.IncrementReferenceCount();
		LuaTexture.LC_ITexture.Push(texture);
		return 1;
	}

	[LuaMethod]
	static int IMaterial__SetTexture(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		ITexture? texture;
		if (lua.GetType(3) == LuaType.String)
			texture = materials.FindTexture(lua.CheckString(3), "", true, 0);
		else
			texture = (ITexture?)LuaTexture.LC_ITexture.Get(3);

		if (texture == null || texture == GetTextureValue(var))
			return 0;

		var.SetTextureValue(texture);
		material.RecomputeStateSnapshots();
		return 0;
	}

	[LuaMethod]
	static int IMaterial__GetMatrix(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		LuaVMatrix.Push_VMatrix(var.GetMatrixValue());
		return 1;
	}

	[LuaMethod]
	static int IMaterial__SetMatrix(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		var.SetMatrixValue(LuaVMatrix.Get_VMatrix(3));
		return 0;
	}

	[LuaMethod]
	static int IMaterial__GetInt(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		lua.PushNumber(var.GetIntValue());
		return 1;
	}

	[LuaMethod]
	static int IMaterial__SetInt(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		int value = (int)lua.CheckNumber(3);
		if (value != var.GetIntValue()) {
			var.SetIntValue(value);
			material.RecomputeStateSnapshots();
		}
		return 0;
	}

	[LuaMethod]
	static int IMaterial__GetFloat(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		lua.PushNumber(var.GetFloatValue());
		return 1;
	}

	[LuaMethod]
	static int IMaterial__SetFloat(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		var.SetFloatValue((float)lua.CheckNumber(3));
		return 0;
	}

	[LuaMethod]
	static int IMaterial__SetUndefined(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		var.SetUndefined();
		return 0;
	}

	[LuaMethod]
	static int IMaterial__SetVector(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		ref Vector3 vec = ref LuaVector.Get_Vector(3);
		var.SetVecValue(vec.X, vec.Y, vec.Z);
		return 0;
	}

	[LuaMethod]
	static int IMaterial__GetVector(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		Vector3 vec = default;
		var.GetVecValue(MemoryMarshal.CreateSpan(ref vec.X, 3));
		LuaVector.Push_Vector(vec);
		return 1;
	}

	[LuaMethod]
	static int IMaterial__SetVector4D(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		float x = (float)lua.CheckNumber(3);
		float y = (float)lua.CheckNumber(4);
		float z = (float)lua.CheckNumber(5);
		float w = (float)lua.CheckNumber(6);
		var.SetVecValue(x, y, z, w);
		return 0;
	}

	[LuaMethod]
	static int IMaterial__GetVector4D(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		Span<float> vec = stackalloc float[4];
		var.GetVecValue(vec);
		lua.PushNumber(vec[0]);
		lua.PushNumber(vec[1]);
		lua.PushNumber(vec[2]);
		lua.PushNumber(vec[3]);
		return 4;
	}

	[LuaMethod]
	static int IMaterial__GetVectorLinear(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		Vector3 vec = default;
		var.GetLinearVecValue(MemoryMarshal.CreateSpan(ref vec.X, 3), 3);
		LuaVector.Push_Vector(vec);
		return 1;
	}

	[LuaMethod]
	static int IMaterial__SetString(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		IMaterialVar var = material.FindVar(lua.CheckString(2), out bool found, false);
		if (var == null || !found)
			return 0;

		var.SetStringValue(lua.CheckString(3));
		return 0;
	}

	[LuaMethod]
	static int IMaterial__SetShader(ILuaInterface lua) => 0;

	[LuaMethod]
	static int IMaterial__Recompute(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		material.RecomputeStateSnapshots();
		return 0;
	}

	[LuaMethod]
	static int IMaterial__GetKeyValues(ILuaInterface lua) {
		IMaterial? material = (IMaterial?)LC_IMaterial.Get(1);
		if (material == null) {
			lua.Error("Tried to use a NULL IMaterial!");
			return 0;
		}

		int count = material.ShaderParamCount();
		IMaterialVar[] vars = material.GetShaderParams()!;
		lua.CreateTable();
		for (int i = 0; i < count; i++) {
			IMaterialVar var = vars[i];
			lua.PushString(var.GetName());
			switch (var.GetVarType()) {
				case MaterialVarType.Float:
					lua.PushNumber(var.GetFloatValue());
					break;
				default:
					lua.PushString(var.GetStringValue());
					break;
				case MaterialVarType.Vector: {
						Vector3 vec = default;
						var.GetLinearVecValue(MemoryMarshal.CreateSpan(ref vec.X, 3), 3);
						LuaVector.Push_Vector(vec);
						break;
					}
				case MaterialVarType.Texture: {
						ITexture? texture = var.GetTextureValue();
						if (texture == null)
							lua.PushNil();
						else
							LuaTexture.LC_ITexture.Push(texture);
						break;
					}
				case MaterialVarType.Int:
					lua.PushNumber(var.GetIntValue());
					break;
				case MaterialVarType.Undefined:
					lua.PushNil();
					break;
				case MaterialVarType.Matrix:
					LuaVMatrix.Push_VMatrix(var.GetMatrixValue());
					break;
			}
			lua.SetTable(-3);
		}
		return 1;
	}

	static bool IsAllowedMaterialPath(ReadOnlySpan<char> name) {
		Span<char> buffer = stackalloc char[MAX_PATH];
		strcpy(buffer, name);
		StrTools.FixSlashes(buffer, '/');
		StrTools.FixDoubleSlashes(buffer);
		string path = new(((ReadOnlySpan<char>)buffer).SliceNullTerminatedString());

		if (path.Contains(':'))
			return false;

		if (!path.Contains("../") && !path.Contains("./") && (path.Length == 0 || (path[0] != '/' && path[0] != ' ')))
			return true;

		ReadOnlySpan<string> prefixes = ["../data/", "../sound/", "../models/", "../resource/", "../html/", "../dupes/", "../demos/", "../saves/", "../cache/", "../backgrounds/", "../screenshots/", "../gamemodes/"];
		foreach (string prefix in prefixes) {
			if (path.StartsWith(prefix, StringComparison.Ordinal) && !path.AsSpan(prefix.Length).Contains("../", StringComparison.Ordinal))
				return true;
		}

		return false;
	}

	[LuaGlobal]
	static int Material(ILuaInterface lua) {
		string? name = lua.GetString(1);
		if (name == null || name.Length == 0)
			return 0;

		if (!IsAllowedMaterialPath(name))
			name = "";

		string parameters = lua.CheckStringOpt(2, "");
		long start = Stopwatch.GetTimestamp();

		IMaterial? material = null;
		if (get.Resources() != null)
			material = get.Resources()!.FindMaterial(name, parameters, true, false, false);
		if (material == null) {
			material = materials.FindMaterial(name, "Lua Materials", false, null);
			if (material == null)
				return 0;
		}

		material.GetMappingHeight();
		material.IncrementReferenceCount();
		LC_IMaterial.Push(material);
		lua.PushNumber((Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency);
		return 2;
	}

	struct CreatedMaterial
	{
		public InlineArray260<char> Name;
		public IMaterial? Material;
	}

	static readonly List<CreatedMaterial> CreatedMaterials = [];

	public static void Push(IMaterial? material) {
		material?.IncrementReferenceCount();
		LC_IMaterial.Push(material);
	}

	public static void TableToKeyValues(ILuaObject? table, KeyValues kv, ILuaObject visited) {
		if (table == null || !table.isTable())
			return;

		visited.Push();
		table.Push();
		g_Lua!.GetTable(-2);
		LuaType type = g_Lua.GetType(-1);
		g_Lua.Pop(2);
		if (type == LuaType.Bool) {
			visited.UnReference();
			g_Lua.Error("TableToKeyValues: attempt to serialize structure with cyclic reference");
			return;
		}

		visited.Push();
		table.Push();
		g_Lua.PushBool(true);
		g_Lua.SetTable(-3);
		g_Lua.Pop(1);

		table.Push();
		g_Lua.PushNil();
		while (g_Lua.Next(-2)) {
			g_Lua.Push(-2);
			string? key = g_Lua.GetString(-1);
			g_Lua.Pop(1);
			if (g_Lua.GetType(-1) == LuaType.Table) {
				KeyValues sub = kv.CreateNewKey();
				sub.SetName(key);
				LuaObject subTable = new();
				subTable.SetFromStack(-1);
				TableToKeyValues(subTable, sub, visited);
				subTable.UnReference();
			}
			else
				kv.SetString(key, g_Lua.GetString(-1));
			g_Lua.Pop(1);
		}
		g_Lua.Pop(1);

		visited.Push();
		table.Push();
		g_Lua.PushNil();
		g_Lua.SetTable(-3);
		g_Lua.Pop(1);
	}

	[LuaGlobal]
	static int CreateMaterial(ILuaInterface lua) {
		string name = lua.CheckString(1);
		string shader = lua.CheckString(2);
		ILuaObject data = lua.GetObject(3);
		if (!data.isNil() && !data.isTable()) {
			lua.TypeError("table", 3);
			return 0;
		}

		foreach (CreatedMaterial created in CreatedMaterials) {
			if (strncmp(((ReadOnlySpan<char>)created.Name).SliceNullTerminatedString(), name, 260) == 0) {
				Push(created.Material);
				return 1;
			}
		}

		KeyValues kv = new(shader);
		LuaTable visited = new(null, 0);
		TableToKeyValues(data, kv, visited);

		string fileName = $"{name}.vmt";
		IMaterial? material = materials.FindMaterial("!" + name, "Other textures", false, null);
		if (material == null || material.IsErrorMaterial()) {
			material = materials.CreateMaterial(fileName, kv);
			if (material == null) {
				visited.UnReference();
				return 0;
			}
			material.DecrementReferenceCount();
		}
		else {
			// todo: IMaterial.SetShaderAndParams
			// material.SetShaderAndParams(kv);
		}

		CreatedMaterial entry = default;
		strcpy(entry.Name, name);
		entry.Material = material;
		CreatedMaterials.Add(entry);
		material.IncrementReferenceCount();
		material.Refresh();
		Push(material);

		visited.UnReference();
		return 1;
	}
}
