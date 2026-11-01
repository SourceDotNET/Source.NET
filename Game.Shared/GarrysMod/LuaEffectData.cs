#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source.Common.GarrysMod.Lua;

using System.Numerics;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaEffectData
{
	[LuaClass(typeof(EffectData), NullError = "Tried to use a NULL CEffectData!")]
	public static readonly LuaClass LC_EffectData = new("CEffectData", LuaType.EffectData, null, null);

	public static void Push_EffectData(EffectData? data) => LC_EffectData.Push(data);

	public static EffectData Get_EffectData(int stackPos) {
		EffectData? data = LC_EffectData.Get(stackPos) as EffectData;
		if (data == null)
			g_Lua!.Error("Tried to use a NULL CEffectData!");
		return data!;
	}

	[LuaGlobal]
	static int EffectData(ILuaInterface lua) {
		Push_EffectData(new EffectData());
		return 1;
	}

	[LuaMethod]
	static int CEffectData____tostring(ILuaInterface lua) {
		lua.PushString("CEffectData");
		return 1;
	}

	[LuaMethod]
	static int CEffectData__IsValid(ILuaInterface lua) {
		lua.PushBool(LC_EffectData.Get(1) is EffectData);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__GetOrigin(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_EffectData(1).Origin);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetOrigin(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.Origin = LuaVector.Get_Vector(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetStart(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_EffectData(1).Start);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetStart(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.Start = LuaVector.Get_Vector(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetNormal(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_EffectData(1).Normal);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetNormal(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.Normal = LuaVector.Get_Vector(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetAngles(ILuaInterface lua) {
		LuaAngle.Push_Angle(Get_EffectData(1).Angles);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetAngles(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.Angles = LuaAngle.Get_Angle(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetFlags(ILuaInterface lua) {
		lua.PushNumber(Get_EffectData(1).Flags);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetFlags(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.Flags = (int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetScale(ILuaInterface lua) {
		lua.PushNumber(Get_EffectData(1).Scale);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetScale(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.Scale = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetMagnitude(ILuaInterface lua) {
		lua.PushNumber(Get_EffectData(1).Magnitude);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetMagnitude(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.Magnitude = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetRadius(ILuaInterface lua) {
		lua.PushNumber(Get_EffectData(1).Radius);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetRadius(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.Radius = (float)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetAttachment(ILuaInterface lua) {
		lua.PushNumber(Get_EffectData(1).AttachmentIndex);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetAttachment(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.AttachmentIndex = (int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetSurfaceProp(ILuaInterface lua) {
		lua.PushNumber(Get_EffectData(1).SurfaceProp);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetSurfaceProp(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.SurfaceProp = (short)(int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetMaterialIndex(ILuaInterface lua) {
		lua.PushNumber(Get_EffectData(1).Material);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetMaterialIndex(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.Material = (int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetDamageType(ILuaInterface lua) {
		lua.PushNumber(Get_EffectData(1).DamageType);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetDamageType(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.DamageType = (int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetHitBox(ILuaInterface lua) {
		lua.PushNumber(Get_EffectData(1).HitBox);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetHitBox(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.HitBox = (int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetColor(ILuaInterface lua) {
		lua.PushNumber(Get_EffectData(1).Color);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetColor(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.Color = (byte)(int)lua.CheckNumber(2);
		return 0;
	}

#if CLIENT_DLL
	[LuaMethod]
	static int CEffectData__GetEntity(ILuaInterface lua) {
		LuaEntity.Push_Entity(cl_entitylist.GetBaseEntityFromHandle(Get_EffectData(1).Entity));
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetEntity(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		BaseEntity? ent = LuaEntity.Get_Entity(2, true);
		if (ent == null)
			data.Entity.Invalidate();
		else
			data.Entity = ent.GetRefEHandle();
		return 0;
	}
#else
	[LuaMethod]
	static int CEffectData__GetEntIndex(ILuaInterface lua) {
		lua.PushNumber(Get_EffectData(1).EntIndex);
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetEntIndex(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		data.EntIndex = (int)lua.CheckNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CEffectData__GetEntity(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		if (data.EntIndex == 0) {
			LuaEntity.Push_Entity(null);
			return 1;
		}
		LuaEntity.Push_Entity(Util.EntityByIndex(data.EntIndex));
		return 1;
	}

	[LuaMethod]
	static int CEffectData__SetEntity(ILuaInterface lua) {
		EffectData data = Get_EffectData(1);
		BaseEntity? ent = LuaEntity.Get_Entity(2, true);
		if (ent == null || ent.Edict() == null)
			data.EntIndex = 0;
		else
			data.EntIndex = ent.EntIndex();
		return 0;
	}
#endif
}
#endif
