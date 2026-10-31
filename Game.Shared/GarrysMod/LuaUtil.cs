using Source;
#if CLIENT_DLL || GAME_DLL || MENU_DLL
#if !MENU_DLL
using Game.Shared;
#endif

using Source.Common;
using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;
using Source.Common.Physics;

#if CLIENT_DLL
using Steamworks;
#endif

using System.Numerics;
using System.Runtime.CompilerServices;
using Source.Common.Hashing;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaUtil
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_util = new("util");

#if !MENU_DLL
	public static bool UTIL_IsValidModel(ReadOnlySpan<char> name) {
		if (name.IsEmpty || name[0] <= ' ' || name.Length <= 3)
			return false;
		if (!stristr(name, ".bsp").IsEmpty)
			return false;
		if (stricmp(name[^4..], ".mdl") != 0)
			return false;
#if CLIENT_DLL
		int index = UTIL_GetModelIndex(name);
#else
		if (!engine.IsModelPrecached(name) && !filesystem.FileExists(name, "GAME"))
			return false;

		int index = BaseEntity.PrecacheModel(name);
#endif
		if (index == -1)
			return false;

		Model? model = (Model?)modelinfo.GetModel(index);
		if (modelinfo.GetModelType(model) != ModelType.Studio)
			return false;

		StudioHeader? studio = modelinfo.GetStudiomodel(model);
		if (studio != null && studio.NumBodyParts <= 0)
			return false;

		MDLHandle_t handle = mdlcache.FindMDL(name);
		if (handle == MDLHANDLE_INVALID)
			return true;

		bool error = mdlcache.IsErrorModel(handle);
		mdlcache.Release(handle);
		return !error;
	}

	public static int UTIL_GetModelIndex(ReadOnlySpan<char> name) {
		if (name.Contains('*'))
			return -1;
		if (name.IsEmpty || (name[0] & 0xDF) == 0)
			return -1;
		return modelinfo.GetModelIndex(name);
	}

	public static bool UTIL_IsValidPropModel(ReadOnlySpan<char> name) {
		if (!UTIL_IsValidModel(name))
			return false;
		if (!stristr(name, "coreball.mdl").IsEmpty)
			return false;
#if CLIENT_DLL
		VCollide? collide = modelinfo.GetVCollide(UTIL_GetModelIndex(name));
#else
		VCollide? collide = modelinfo.GetVCollide(BaseEntity.PrecacheModel(name));
#endif
		return collide != null && collide.SolidCount == 1;
	}

	public static bool UTIL_IsValidRagdollModel(ReadOnlySpan<char> name) {
		if (!UTIL_IsValidModel(name))
			return false;
#if CLIENT_DLL
		VCollide? collide = modelinfo.GetVCollide(UTIL_GetModelIndex(name));
#else
		VCollide? collide = modelinfo.GetVCollide(BaseEntity.PrecacheModel(name));
#endif
		return collide != null && collide.SolidCount > 1;
	}

#endif

	[LuaFunction]
	static int AimVector(ILuaInterface lua) {
		QAngle angles = LuaAngle.Get_Angle(1);
		float fov = (float)lua.GetNumber(2);
		float x = (float)lua.GetNumber(3);
		float y = (float)lua.GetNumber(4);
		float w = (float)lua.GetNumber(5);
		float h = (float)lua.GetNumber(6);
		float halfW = w * 0.5f;
		float right = x - halfW;
		float up = h * 0.5f - y;
		float forward = halfW / MathF.Tan(fov * (MathF.PI / 360.0f));
		MathLib.AngleVectors(angles, out Vector3 fwd, out Vector3 rt, out Vector3 upv);
		Vector3 dir = fwd * forward + rt * right + upv * up;
		MathLib.VectorNormalize(ref dir);
		LuaVector.Push_Vector(dir);
		return 1;
	}

#if !MENU_DLL
	[LuaFunction]
	static int IntersectRayWithOBB(ILuaInterface lua) {
		Vector3 rayStart = LuaVector.Get_Vector(1);
		Vector3 rayDelta = LuaVector.Get_Vector(2);
		Vector3 boxOrigin = LuaVector.Get_Vector(3);
		QAngle boxAngles = LuaAngle.Get_Angle(4);
		Vector3 boxMins = LuaVector.Get_Vector(5);
		Vector3 boxMaxs = LuaVector.Get_Vector(6);

		MathLib.AngleMatrix(boxAngles, out Matrix3x4 worldToBox);
		MathLib.MatrixInvert(worldToBox, out worldToBox);

		MathLib.VectorRotate(rayStart - boxOrigin, worldToBox, out Vector3 localStart);
		MathLib.VectorRotate(rayStart + rayDelta - boxOrigin, worldToBox, out Vector3 localEnd);

		Ray ray = default;
		ray.Init(boxOrigin + localStart, boxOrigin + localEnd);

		GameTrace trace = new();
		if (!CollisionUtils.IntersectRayWithBox(in ray, boxOrigin + boxMins, boxOrigin + boxMaxs, 0.0f, ref trace, ref Unsafe.NullRef<float>()))
			return 0;

		LuaVector.Push_Vector(rayStart + rayDelta * trace.Fraction);
		LuaVector.Push_Vector(trace.Plane.Normal);
		lua.PushNumber(trace.Fraction);
		return 3;
	}

	[LuaFunction]
	static int IsValidRagdoll(ILuaInterface lua) {
		string? name = lua.CheckString(1);
		if (!string.IsNullOrEmpty(name) && ' ' < name[0]) {
			lua.PushBool(UTIL_IsValidRagdollModel(name));
			return 1;
		}
		lua.PushBool(false);
		return 0;
	}

	[LuaFunction]
	static int IsValidProp(ILuaInterface lua) {
		string? name = lua.CheckString(1);
		if (!string.IsNullOrEmpty(name) && ' ' < name[0]) {
			lua.PushBool(UTIL_IsValidPropModel(name));
			return 1;
		}
		lua.PushBool(false);
		return 0;
	}

	[LuaFunction]
	static int PrecacheModel(ILuaInterface lua) {
		if (UTIL_IsValidModel(g_Lua!.CheckString(1)))
			BaseEntity.PrecacheModel(g_Lua.CheckString(1));
		return 0;
	}

	[LuaFunction]
	static int IsValidModel(ILuaInterface lua) {
		string? name = lua.CheckString(1);
		if (!string.IsNullOrEmpty(name) && ' ' < name[0]) {
			lua.PushBool(UTIL_IsValidModel(name));
			return 1;
		}
		lua.PushBool(false);
		return 0;
	}

	[LuaFunction]
	static int PrecacheSound(ILuaInterface lua) {
		BaseEntity.PrecacheScriptSound(g_Lua!.CheckString(1));
		return 0;
	}

	[LuaFunction]
	static string? NetworkIDToString(int id) => NetworkString.Convert(id);

	[LuaFunction]
	static int NetworkStringToID(string name) => NetworkString.Get(name);

#endif

	[LuaFunction]
	static int Base64Decode(ILuaInterface lua) {
		string str = lua.GetString(1) ?? "";
		List<byte> decoded = [];
		Bootil.String.Decode.Base64(System.Text.Encoding.Latin1.GetBytes(str), decoded);
		lua.PushString(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(decoded));
		return 1;
	}

	[LuaFunction]
	static int KeyValuesToTable(ILuaInterface lua) {
		LuaTable table = new(null, 0);
		KeyValues kv = new("KeyValuesToTable");
		kv.UsesEscapeSequences(g_Lua!.GetBool(2));
		kv.LoadFromBuffer("util.KeyValuesToTable", g_Lua.CheckString(1));
		LuaHelper.KeyValuesToTable(kv, table, g_Lua.GetBool(3));
		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaFunction]
	static int KeyValuesToTablePreserveOrder(ILuaInterface lua) {
		LuaTable table = new(null, 0);
		KeyValues kv = new("KeyValuesToTablePreserveOrder");
		kv.UsesEscapeSequences(g_Lua!.GetBool(2));
		kv.LoadFromBuffer("util.KeyValuesToTablePreserveOrder", g_Lua.CheckString(1));
		LuaHelper.KeyValuesToTablePreserveOrder(kv, table, g_Lua.GetBool(3));
		table.Push();
		table.UnReference();
		return 1;
	}

#if !MENU_DLL
	static bool ParseSteam2Number(ref ReadOnlySpan<char> str, out uint value) {
		value = 0;
		int i = 0;
		while (i < str.Length && char.IsAsciiDigit(str[i])) {
			value = value * 10 + (uint)(str[i] - '0');
			i++;
		}
		if (i == 0)
			return false;
		str = str[i..];
		return true;
	}

	static bool SetFromSteam2String(ReadOnlySpan<char> str, Steamworks.EUniverse universe, out ulong steamID) {
		steamID = 0;
		str = str.SliceNullTerminatedString();
		if (strnicmp(str, "STEAM_", 6) == 0)
			str = str[6..];

		uint x = 0, y = 0, z = 0;
		int count = 0;
		if (ParseSteam2Number(ref str, out x)) {
			count++;
			if (str.Length > 0 && str[0] == ':') {
				str = str[1..];
				if (ParseSteam2Number(ref str, out y)) {
					count++;
					if (str.Length > 0 && str[0] == ':') {
						str = str[1..];
						if (ParseSteam2Number(ref str, out z)) {
							count++;
							if (str.Length > 0)
								count++;
						}
					}
				}
			}
		}

		if (count > 3 || count <= 1 || (count <= 2 && (ushort)x != 1))
			return false;

		Steamworks.CSteamID id = default;
		id.InstancedSet(new Steamworks.AccountID_t(y + z * 2), 1, universe, Steamworks.EAccountType.k_EAccountTypeIndividual);
		steamID = id.m_SteamID;
		return true;
	}

	[LuaFunction]
	static int SteamIDTo64(ILuaInterface lua) {
		string? str = lua.GetString(1);
		if (str == null)
			return 0;

		SetFromSteam2String(str, Steamworks.EUniverse.k_EUniversePublic, out ulong steamID);
		lua.PushString(steamID.ToString());
		return 1;
	}

	[LuaFunction]
	static int SteamIDFrom64(ILuaInterface lua) {
		string? str = lua.GetString(1);
		if (str == null)
			return 0;

		_ = ulong.TryParse(str, out ulong steamID);
		Span<char> buf = stackalloc char[1024];
		sprintf(buf, "STEAM_0:%u:%u").U((uint)steamID & 1).U((uint)(steamID >> 1) & 0x7fffffff);
		lua.PushString(((ReadOnlySpan<char>)buf).SliceNullTerminatedString());
		return 1;
	}

	[LuaFunction]
	static int SharedRandom(ILuaInterface lua) {
		int seed = 0;
		if (g_Lua!.GetType(4) == LuaType.Number)
			seed = (int)g_Lua.CheckNumber(4);
		g_Lua.PushNumber(SharedRandomFloat(g_Lua.CheckString(1), (float)g_Lua.CheckNumber(2), (float)g_Lua.CheckNumber(3), seed));
		return 1;
	}

	[LuaFunction]
	static int CRC(ILuaInterface lua) {
		if (!g_Lua!.IsType(1, LuaType.String))
			g_Lua.CheckString(1);
		ReadOnlySpan<byte> data = g_Lua.GetStringBytes(1);
		CRC32_t crc = 0;
		CRC32.Init(ref crc);
		CRC32.ProcessBuffer(ref crc, data, data.Length);
		CRC32.Final(ref crc);
		g_Lua.PushString(((uint)crc).ToString());
		return 1;
	}

	[LuaFunction]
	static int GetSurfacePropName(ILuaInterface lua) {
		int index = (int)g_Lua!.CheckNumber(1);
		if (index >= 0 && index < physprops.SurfacePropCount()) {
			g_Lua.PushString(physprops.GetPropName(index));
			return 1;
		}
		g_Lua.PushString("");
		return 1;
	}

#if GAME_DLL
	[LuaFunction]
	static int AddNetworkString(string name) => NetworkString.Add(name);
#endif

#if CLIENT_DLL
	[LuaFunction]
	static int FilterText(ILuaInterface lua) {
		string text = lua.CheckString(1);
		float context = (int)lua.CheckNumberOpt(2, 0);
		if (context <= 0)
			context = 0;
		if (3 <= context)
			context = 3;

		CSteamID steamID = default;
		if (lua.GetType(3) != LuaType.Nil) {
			C_BasePlayer? player = LuaPlayer.Get_Player(3, false);
			player?.GetSteamID(out steamID);
		}

		Span<char> filtered = stackalloc char[4096];
		get.FilterText(text, filtered, (ETextFilteringContext)(uint)context, steamID);
		lua.PushString(filtered);
		return 1;
	}
#endif

	public static void SetTableFromTrace<T>(ref Trace tr, T table) where T : ILuaObject {
		table.SetMember("Hit", tr.DidHit());
		table.SetMember("HitWorld", tr.DidHitWorld());
		table.SetMember("HitNonWorld", tr.DidHitNonWorldEntity());
		table.SetMember("Fraction", tr.Fraction);
		table.SetMemberEntity("Entity", tr.Ent);
		table.SetMember("HitGroup", (float)tr.HitGroup);
		table.SetMember("HitBox", (float)tr.HitBox);
		table.SetMemberVector("HitPos", tr.EndPos);
		table.SetMemberVector("StartPos", tr.StartPos);
		table.SetMemberVector("HitNormal", tr.Plane.Normal);
		table.SetMember("StartSolid", tr.StartSolid);
		table.SetMember("FractionLeftSolid", tr.FractionLeftSolid);
		table.SetMember("PhysicsBone", (float)tr.PhysicsBone);
		table.SetMember("HitSky", (tr.Surface.Flags & (ushort)Surf.Sky) != 0);
		table.SetMember("HitNoDraw", (tr.Surface.Flags & (ushort)Surf.NoDraw) != 0);
		table.SetMember("SurfaceProps", (float)(short)tr.Surface.SurfaceProps);
		table.SetMember("SurfaceFlags", (float)tr.Surface.Flags);
		table.SetMember("DispFlags", (float)(ushort)tr.DispFlags);
		table.SetMember("AllSolid", tr.AllSolid);
		table.SetMemberDouble("Contents", (int)tr.Contents);
		if (tr.Surface.Name != null)
			table.SetMember("HitTexture", tr.Surface.Name);

		if (tr.DidHit()) {
			SurfaceData_ptr? surfaceData = physprops.GetSurfaceData((short)tr.Surface.SurfaceProps);
			if (surfaceData != null)
				table.SetMember("MatType", (float)surfaceData.Game.Material);
		}

		if (tr.DidHitNonWorldEntity() && tr.HitBox != -1 && tr.Ent?.GetBaseAnimating() != null && tr.Ent.GetBaseAnimating()!.GetModelPtr() != null) {
			BaseAnimating animating = tr.Ent.GetBaseAnimating()!;
			int hitboxSet = animating.GetHitboxSet();
			int hitbox = tr.HitBox;
			StudioHdr studioHdr = animating.GetModelPtr()!;
			MStudioHitboxSet? set = hitboxSet >= 0 && hitboxSet < studioHdr.NumHitboxSets() ? studioHdr.HitboxSet(hitboxSet) : null;
			if (set != null) {
				int numHitboxes = set.NumHitboxes;
				if (numHitboxes != 0) {
					if (hitbox < 0 || hitbox >= numHitboxes)
						Warning($"[UH-OH!] Invalid pHitbox {hitbox} of {numHitboxes} (Sorry model name not available).\n");
					else
						table.SetMember("HitBoxBone", (float)set.Hitbox(hitbox).Bone);
				}
			}
			else
				Warning($"[UH-OH!] Invalid HitboxSet {hitboxSet} of {studioHdr.NumHitboxSets()} on model {studioHdr.Name()}.\n");
		}

		Vector3 normal = tr.EndPos - tr.StartPos;
		MathLib.VectorNormalize(ref normal);
		if (normal.Length() == 0.0f) {
			normal = -tr.Plane.Normal;
			if (normal.Length() == 0.0f)
				normal = new(0, 0, 1);
		}
		table.SetMemberVector("Normal", normal);
	}

	public static void PushTableFromTrace<T>(ref Trace tr, T? table) where T : ILuaObject {
		if (table != null && table.isTable()) {
			SetTableFromTrace(ref tr, table);
			table.Push();
			return;
		}

		LuaTable newTable = new();
		SetTableFromTrace(ref tr, newTable);
		newTable.Push();
		newTable.UnReference();
	}

	[LuaFunction]
	static int TraceLine(ILuaInterface lua) {
		if (g_PhysWorldObject == null)
			return 0;

		LuaObject data = new(1, LuaType.None);
		if (!data.isTable()) {
			g_Lua!.TypeError("table", 1);
			data.UnReference();
			return 0;
		}

		Vector3 start = data.GetMemberVector("start", vec3_origin);
		Vector3 end = data.GetMemberVector("endpos", vec3_origin);
		Mask mask = (Mask)data.GetMemberUInt("mask", (uint)Mask.Solid);

		TraceFilterLua filter = TraceFilterFromTable(data);

		Ray ray = default;
		ray.Init(start, end);

		enginetrace.TraceRay(ray, mask | (Mask)Contents.HitBox, ref filter, out Trace tr);

		if (r_visualizetraces.GetBool())
			Game.Shared.DebugOverlay.DebugDrawLine(tr.StartPos, tr.EndPos, 255, 0, 0, true, -1.0f);

		if (!tr.StartSolid)
			Util.ClipTraceToPlayers(start, end, mask | (Mask)Contents.HitBox, ref filter, ref tr);

		LuaObject output = new();
		data.GetMember("output", output);
		PushTableFromTrace(ref tr, output);
		output.UnReference();

		filter.Function.UnReference();
		data.UnReference();
		return 1;
	}

	[LuaFunction]
	static int GetSurfaceIndex(ILuaInterface lua) {
		g_Lua!.PushNumber(physprops.GetSurfaceIndex(g_Lua.CheckString(1)));
		return 1;
	}

	[LuaFunction]
	static int TraceHull(ILuaInterface lua) {
		if (g_PhysWorldObject == null)
			return 0;

		LuaObject data = new(1, LuaType.None);
		if (!data.isTable()) {
			g_Lua!.TypeError("table", 1);
			data.UnReference();
			return 0;
		}

		Vector3 start = data.GetMemberVector("start", vec3_origin);
		Vector3 end = data.GetMemberVector("endpos", vec3_origin);
		Mask mask = (Mask)data.GetMemberUInt("mask", (uint)Mask.Solid);
		Vector3 mins = data.GetMemberVector("mins", vec3_origin);
		Vector3 maxs = data.GetMemberVector("maxs", vec3_origin);

		TraceFilterLua filter = TraceFilterFromTable(data);

		Util.TraceHull(start, end, mins, maxs, mask, ref filter, out Trace tr);

		LuaObject output = new();
		data.GetMember("output", output);
		PushTableFromTrace(ref tr, output);
		output.UnReference();

		filter.Function.UnReference();
		data.UnReference();
		return 1;
	}

	static TraceFilterLua TraceFilterFromTable(LuaObject data) {
		TraceFilterLua filter = new((CollisionGroup)data.GetMemberInt("collisiongroup", 0));
		filter.SetIgnoreWorld(data.GetMemberBool("ignoreworld", false));
		filter.SetIsWhitelist(data.GetMemberBool("whitelist", false));
#if CLIENT_DLL
		filter.SetHitClientOnly(data.GetMemberBool("hitclientonly", false));
#endif

		LuaObject filterObj = new();
		data.GetMember("filter", filterObj);
		if (filterObj.isFunction())
			filter.SetFunction(filterObj);
		else if (filterObj.isTable()) {
			LuaObject entry = new();
			for (int i = 1; ; i++) {
				filterObj.GetMember(i, entry);
				if (entry.isString())
					filter.AddEntityClassToIgnore(entry.GetString()!);
				else if (entry.isEntity())
					filter.AddEntityToIgnore(entry.GetEntity());
				else
					break;
			}
			entry.UnReference();
		}
		else if (filterObj.GetType() == LuaType.Entity)
			filter.AddEntityToIgnore(filterObj.GetEntity());
		filterObj.UnReference();
		return filter;
	}
#endif
#if CLIENT_DLL || MENU_DLL
	static void TableToKeyValues(ILuaInterface lua, ILuaObject table, System.Text.StringBuilder output, string name, int depth) {
		output.Append('\t', depth).Append('"').Append(name).Append("\"\n");
		output.Append('\t', depth).Append("{\n");

		LuaObject key = new();
		LuaObject value = new();
		table.Push();
		lua.PushNil();
		while (lua.Next(-2)) {
			value.SetFromStack(-1);
			key.SetFromStack(-2);
			lua.Pop(1);
			string keyName = key.GetString() ?? "";
			if (value.isTable())
				TableToKeyValues(lua, value, output, keyName, depth + 1);
			else
				output.Append('\t', depth + 1).Append('"').Append(keyName).Append("\"\t\t\"").Append(value.GetString() ?? "").Append("\"\n");
		}
		lua.Pop(1);
		key.UnReference();
		value.UnReference();

		output.Append('\t', depth).Append("}\n");
	}

	[LuaFunction]
	static int TableToKeyValues(ILuaInterface lua) {
		string name = lua.CheckStringOpt(2, "TableToKeyValues");
		ILuaObject? table = lua.GetObject(1);
		if (table == null || !table.isTable()) {
			lua.TypeError("table", 1);
			return 0;
		}
		System.Text.StringBuilder output = new();
		TableToKeyValues(lua, table, output, name, 0);
		lua.PushString(output.ToString());
		return 1;
	}

	[LuaFunction]
	static int Compress(ILuaInterface lua) {
		if (lua.GetType(1) != LuaType.String)
			lua.CheckString(1);
		ReadOnlySpan<byte> data = lua.GetDataString(1);
		if (data.IsEmpty) {
			lua.PushString("");
			return 1;
		}
		using MemoryStream output = new();
		if (!Bootil.Compression.LZMA.Compress(data, output, 5, 0x10000))
			return 0;
		lua.PushString(output.ToArray());
		return 1;
	}

	[LuaFunction]
	static int Decompress(ILuaInterface lua) {
		if (lua.GetType(1) != LuaType.String)
			lua.CheckString(1);
		ReadOnlySpan<byte> data = lua.GetDataString(1);
		if (data.IsEmpty)
			return 0;
		using MemoryStream output = new();
		if (!Bootil.Compression.LZMA.Extract(data, output))
			return 0;
		lua.PushString(output.ToArray());
		return 1;
	}

	[LuaFunction]
	static int Base64Encode(ILuaInterface lua) {
		if (lua.GetType(1) != LuaType.String)
			lua.CheckString(1);
		bool inline = lua.GetType(2) != LuaType.Nil && lua.GetBool(2);
		string encoded = Convert.ToBase64String(lua.GetDataString(1));
		if (!inline) {
			System.Text.StringBuilder wrapped = new();
			for (int i = 0; i < encoded.Length; i += 76) {
				wrapped.Append(encoded, i, Math.Min(76, encoded.Length - i));
				wrapped.Append('\n');
			}
			encoded = wrapped.ToString();
		}
		lua.PushString(encoded);
		return 1;
	}

	static long LastTimerCycle;

	[LuaFunction]
	static int TimerCycle(ILuaInterface lua) {
		long now = System.Diagnostics.Stopwatch.GetTimestamp();
		long elapsed = now - LastTimerCycle;
		LastTimerCycle = now;
		lua.PushNumber(elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
		return 1;
	}

	static string JSONNumber(double value) {
		if (double.IsNaN(value) || double.IsInfinity(value))
			return "0";
		string str = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
		if (str.IndexOfAny(['.', 'E', 'e']) < 0)
			str += ".0";
		return str;
	}

	static string VectorNumber(float value) => value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

	static void JSONString(System.Text.StringBuilder output, string value) {
		output.Append('"');
		foreach (char c in value) {
			switch (c) {
				case '"': output.Append("\\\""); break;
				case '\\': output.Append("\\\\"); break;
				case '\b': output.Append("\\b"); break;
				case '\f': output.Append("\\f"); break;
				case '\n': output.Append("\\n"); break;
				case '\r': output.Append("\\r"); break;
				case '\t': output.Append("\\t"); break;
				default:
					if (c < 0x20)
						output.Append("\\u").Append(((int)c).ToString("X4"));
					else
						output.Append(c);
					break;
			}
		}
		output.Append('"');
	}

	static bool JSONValue(ILuaInterface lua, ILuaObject value, System.Text.StringBuilder output, bool pretty, int depth, HashSet<int> visiting) {
		switch (value.GetType()) {
			case LuaType.Bool:
				output.Append(value.GetBool() ? "true" : "false");
				return true;
			case LuaType.Number:
				output.Append(JSONNumber(value.GetDouble()));
				return true;
			case LuaType.String:
				JSONString(output, value.GetString() ?? "");
				return true;
			case LuaType.Vector: {
					value.Push();
					System.Numerics.Vector3 vec = LuaVector.Get_Vector(-1);
					lua.Pop(1);
					JSONString(output, $"[{VectorNumber(vec.X)} {VectorNumber(vec.Y)} {VectorNumber(vec.Z)}]");
					return true;
				}
			case LuaType.Angle: {
					value.Push();
					Source.Common.Mathematics.QAngle ang = LuaAngle.Get_Angle(-1);
					lua.Pop(1);
					JSONString(output, $"{{{VectorNumber(ang.X)} {VectorNumber(ang.Y)} {VectorNumber(ang.Z)}}}");
					return true;
				}
			case LuaType.Table:
				return JSONTable(lua, value, output, pretty, depth + 1, visiting);
			default:
				return false;
		}
	}

	static bool JSONTable(ILuaInterface lua, ILuaObject table, System.Text.StringBuilder output, bool pretty, int depth, HashSet<int> visiting) {
		if (depth > 3000) {
			lua.ErrorFromLua("JSON Depth is too high! > 3000");
			return false;
		}


		List<(LuaObject Key, LuaObject Value)> entries = [];
		table.Push();
		lua.PushNil();
		while (lua.Next(-2)) {
			LuaObject value = new(-1, LuaType.None);
			LuaObject key = new(-2, LuaType.None);
			lua.Pop(1);
			entries.Add((key, value));
		}
		lua.Pop(1);

		bool isArray = entries.Count > 0;
		for (int i = 0; i < entries.Count && isArray; i++) {
			if (entries[i].Key.GetType() != LuaType.Number)
				isArray = false;
		}
		if (isArray) {
			HashSet<double> keys = [.. entries.Select(e => e.Key.GetDouble())];
			for (int i = 1; i <= entries.Count; i++) {
				if (!keys.Contains(i)) {
					isArray = false;
					break;
				}
			}
			if (isArray)
				entries.Sort((a, b) => a.Key.GetDouble().CompareTo(b.Key.GetDouble()));
		}

		output.Append(isArray ? '[' : '{');
		bool first = true;
		foreach ((LuaObject key, LuaObject value) in entries) {
			if (!isArray && key.GetType() != LuaType.String && key.GetType() != LuaType.Number)
				continue;
			int mark = output.Length;
			if (!first)
				output.Append(',');
			if (pretty)
				output.Append('\n').Append('\t', depth);
			if (!isArray) {
				JSONString(output, key.GetType() == LuaType.Number ? JSONKey(key.GetDouble()) : key.GetString() ?? "");
				output.Append(':');
				if (pretty)
					output.Append(' ');
			}
			if (!JSONValue(lua, value, output, pretty, depth, visiting))
				output.Length = mark;
			else
				first = false;
		}
		if (pretty && !first)
			output.Append('\n').Append('\t', depth - 1);
		output.Append(isArray ? ']' : '}');

		foreach ((LuaObject key, LuaObject value) in entries) {
			key.UnReference();
			value.UnReference();
		}
		return true;
	}

	static string JSONKey(double key) {
		if (key == Math.Floor(key) && Math.Abs(key) < 1e15)
			return ((long)key).ToString(System.Globalization.CultureInfo.InvariantCulture);
		return key.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
	}

	[LuaFunction]
	static int TableToJSON(ILuaInterface lua) {
		LuaObject table = new(1, LuaType.None);
		if (!table.isTable()) {
			lua.TypeError("table", 1);
			table.UnReference();
			return 0;
		}
		System.Text.StringBuilder output = new();
		bool ok = JSONTable(lua, table, output, lua.GetBool(2), 1, []);
		table.UnReference();
		if (!ok)
			return 0;
		lua.PushString(output.ToString());
		return 1;
	}

	static bool TryParseVector(string str, char open, char close, out float x, out float y, out float z) {
		x = y = z = 0;
		if (str.Length < 2 || str[0] != open || str[^1] != close)
			return false;
		string[] parts = str[1..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != 3)
			return false;
		var inv = System.Globalization.CultureInfo.InvariantCulture;
		return float.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out x)
			&& float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out y)
			&& float.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out z);
	}

	static void PushJSONElement(ILuaInterface lua, System.Text.Json.JsonElement element, int depth, bool ignoreLimits, bool ignoreConversions) {
		switch (element.ValueKind) {
			case System.Text.Json.JsonValueKind.Object:
			case System.Text.Json.JsonValueKind.Array:
				if (!ignoreLimits && depth > 5000) {
					lua.ErrorFromLua("JSON Depth is too high! > 5000");
					lua.PushNil();
					return;
				}
				LuaTable table = new(null, 0);
				if (element.ValueKind == System.Text.Json.JsonValueKind.Array) {
					int i = 1;
					foreach (System.Text.Json.JsonElement item in element.EnumerateArray()) {
						table.Push();
						lua.PushNumber(i++);
						PushJSONElement(lua, item, depth + 1, ignoreLimits, ignoreConversions);
						lua.SetTable(-3);
						lua.Pop(1);
					}
				}
				else {
					foreach (System.Text.Json.JsonProperty property in element.EnumerateObject()) {
						table.Push();
						if (double.TryParse(property.Name, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double numKey))
							lua.PushNumber(numKey);
						else
							lua.PushString(property.Name);
						PushJSONElement(lua, property.Value, depth + 1, ignoreLimits, ignoreConversions);
						lua.SetTable(-3);
						lua.Pop(1);
					}
				}
				table.Push();
				table.UnReference();
				return;
			case System.Text.Json.JsonValueKind.String: {
					string str = element.GetString() ?? "";
					if (!ignoreConversions) {
						if (TryParseVector(str, '[', ']', out float x, out float y, out float z)) {
							LuaVector.Push_Vector(new System.Numerics.Vector3(x, y, z));
							return;
						}
						if (TryParseVector(str, '{', '}', out x, out y, out z)) {
							LuaAngle.Push_Angle(new Source.Common.Mathematics.QAngle(x, y, z));
							return;
						}
					}
					lua.PushString(str);
					return;
				}
			case System.Text.Json.JsonValueKind.Number:
				lua.PushNumber(element.GetDouble());
				return;
			case System.Text.Json.JsonValueKind.True:
				lua.PushBool(true);
				return;
			case System.Text.Json.JsonValueKind.False:
				lua.PushBool(false);
				return;
			default:
				lua.PushNil();
				return;
		}
	}

	[LuaFunction]
	static int JSONToTable(ILuaInterface lua) {
		string json = lua.CheckString(1);
		bool ignoreLimits = lua.GetBool(2);
		bool ignoreConversions = lua.GetBool(3);
		System.Text.Json.JsonDocument document;
		try {
			document = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions { MaxDepth = int.MaxValue, AllowTrailingCommas = false });
		}
		catch (System.Text.Json.JsonException) {
			return 0;
		}
		using (document) {
			if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object && document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
				return 0;
			PushJSONElement(lua, document.RootElement, 1, ignoreLimits, ignoreConversions);
		}
		return 1;
	}
#endif

#if MENU_DLL
	[LuaFunction]
	static int FullPathToRelative_Menu(ILuaInterface lua) {
		string path = lua.CheckString(1);
		string pathID = lua.CheckStringOpt(2, "MOD");
		Span<char> relative = stackalloc char[MAX_PATH];
		if (!filesystem.FullPathToRelativePathEx(path, pathID, relative)) {
			lua.PushNil();
			return 1;
		}
		lua.PushString(((ReadOnlySpan<char>)relative).SliceNullTerminatedString());
		return 1;
	}

	[LuaFunction]
	static int RelativePathToFull_Menu(ILuaInterface lua) {
		string path = lua.CheckString(1);
		string pathID = lua.CheckStringOpt(2, "MOD");
		Span<char> full = stackalloc char[MAX_PATH];
		ReadOnlySpan<char> result = filesystem.RelativePathToFullPath(path, pathID, full);
		if (result.IsEmpty) {
			lua.PushNil();
			return 1;
		}
		lua.PushString(result);
		return 1;
	}

	[LuaFunction]
	static int RelativePathToGMA_Menu(ILuaInterface lua) {
		string path = lua.CheckString(1);
		if (!filesystem.Addons().FindFileOwner(path, out Source.Common.GarrysMod.IAddonSystem.Information info))
			return 0;
		LuaTable table = new(null, 0);
		table.SetMember("ID", info.WorkshopID.ToString());
		table.SetMember("File", info.File ?? "");
		table.SetMember("Title", info.Title ?? "");
		table.SetMember("Author", info.Creator.ToString());
		table.Push();
		table.UnReference();
		return 1;
	}
#endif
}
#endif
