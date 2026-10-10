using Source;
#if CLIENT_DLL || GAME_DLL
using Game.Shared;

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

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaUtil
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_util = new("util");

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
}
#endif
