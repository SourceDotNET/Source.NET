using Game.Shared;

using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Particles;

using System.Numerics;

namespace Game.Client.GarrysMod;

public static partial class LuaParticleEffect
{
	[LuaClass(typeof(CNewParticleEffect), NullError = "Tried to use a NULL CNewParticleEffect!")]
	public static readonly LuaClass LC_CNewParticleEffect = new("CNewParticleEffect", LuaType.ParticleSystem, null, null);

	public static void Push_NewParticleEffect(CNewParticleEffect effect) => LC_CNewParticleEffect.Push(effect);

	static CNewParticleEffect? Get_ValidEffect(ILuaInterface lua) {
		CNewParticleEffect? effect = LC_CNewParticleEffect.Get(1) as CNewParticleEffect;
		if (effect == null)
			lua.Error("Tried to use a NULL CNewParticleEffect!");
		return effect.IsValid() ? effect : null;
	}

	internal static int ClampAttachType(double value) => Math.Clamp((int)value, 0, (int)ParticleAttachment.RootBoneFollow);

	[LuaGlobal]
	static int CreateParticleSystem(ILuaInterface lua) {
		C_BaseEntity ent = LuaEntity.Get_Entity(1, false)!;
		int attachType = ClampAttachType(lua.CheckNumber(3));
		int attachment = (int)lua.CheckNumberOpt(4, 0);
		Vector3 offset = lua.GetType(5) == LuaType.Nil ? Vector3.Zero : LuaVector.Get_Vector(5);
		string name = lua.CheckString(2);

		CNewParticleEffect? effect = ent.ParticleProp().Create(name, (ParticleAttachment)attachType, attachment, offset);
		if (effect == null || !effect.IsValid())
			return 0;

		Push_NewParticleEffect(effect);
		return 1;
	}

	[LuaGlobal]
	static int CreateParticleSystemNoEntity(ILuaInterface lua) {
		string name = lua.CheckString(1);
		Vector3 pos = LuaVector.Get_Vector(2);
		QAngle ang = lua.GetType(3) == LuaType.Nil ? MathLibConsts.vec3_angle : LuaAngle.Get_Angle(3);

		CNewParticleEffect effect = CNewParticleEffect.Create(null, name);
		effect.AddRef();
		effect.Release();
		if (!effect.IsValid())
			return 0;

		effect.SetSortOrigin(pos);
		effect.SetControlPoint(0, pos);
		effect.SetControlPoint(1, pos);
		if (lua.GetType(3) != LuaType.Nil) {
			MathLib.AngleVectors(ang, out Vector3 forward, out Vector3 right, out Vector3 up);
			effect.SetControlPointOrientation(0, forward, right, up);
		}

		Push_NewParticleEffect(effect);
		return 1;
	}

	[LuaMethod]
	static int CNewParticleEffect____tostring(ILuaInterface lua) {
		if (LC_CNewParticleEffect.Get(1) is CNewParticleEffect effect && effect.IsValid())
			lua.PushString($"CNewParticleEffect [{effect.GetEffectName()}]");
		else
			lua.PushString("CNewParticleEffect [NULL]");
		return 1;
	}

	[LuaMethod]
	static int CNewParticleEffect__IsValid(ILuaInterface lua) {
		lua.PushBool(LC_CNewParticleEffect.Get(1) is CNewParticleEffect effect && effect.IsValid());
		return 1;
	}

	[LuaMethod]
	static int CNewParticleEffect__IsFinished(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		lua.PushBool(effect.IsFinished());
		return 1;
	}

	[LuaMethod]
	static int CNewParticleEffect__IsViewModelEffect(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		lua.PushBool(effect.GetIsViewModelEffect());
		return 1;
	}

	[LuaMethod]
	static int CNewParticleEffect__SetIsViewModelEffect(ILuaInterface lua) {
		Get_ValidEffect(lua)?.SetIsViewModelEffect(lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__GetShouldSimulate(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		lua.PushBool(effect.ShouldSimulate());
		return 1;
	}

	[LuaMethod]
	static int CNewParticleEffect__SetShouldSimulate(ILuaInterface lua) {
		Get_ValidEffect(lua)?.SetShouldSimulate(lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__SetControlPoint(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		int point = (int)lua.CheckNumber(2);
		if ((uint)point < ParticleConstants.MAX_PARTICLE_CONTROL_POINTS)
			effect.SetControlPoint(point, LuaVector.Get_Vector(3));
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__SetControlPointEntity(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		int point = (int)lua.CheckNumber(2);
		if ((uint)point < ParticleConstants.MAX_PARTICLE_CONTROL_POINTS)
			effect.SetControlPointEntity(point, LuaEntity.Get_Entity(3, false));
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__SetControlPointForwardVector(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		int point = (int)lua.CheckNumber(2);
		if ((uint)point < ParticleConstants.MAX_PARTICLE_CONTROL_POINTS)
			effect.SetControlPointForwardVector(point, LuaVector.Get_Vector(3));
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__SetControlPointRightVector(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		int point = (int)lua.CheckNumber(2);
		if ((uint)point < ParticleConstants.MAX_PARTICLE_CONTROL_POINTS)
			effect.SetControlPointRightVector(point, LuaVector.Get_Vector(3));
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__SetControlPointUpVector(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		int point = (int)lua.CheckNumber(2);
		if ((uint)point < ParticleConstants.MAX_PARTICLE_CONTROL_POINTS)
			effect.SetControlPointUpVector(point, LuaVector.Get_Vector(3));
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__SetControlPointParent(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		int point = (int)lua.CheckNumber(2);
		if ((uint)point >= ParticleConstants.MAX_PARTICLE_CONTROL_POINTS)
			return 0;
		int parent = (int)lua.CheckNumber(3);
		if ((uint)parent < ParticleConstants.MAX_PARTICLE_CONTROL_POINTS)
			effect.SetControlPointParent(point, parent);
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__SetControlPointOrientation(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		int point = (int)lua.CheckNumber(2);
		if ((uint)point >= ParticleConstants.MAX_PARTICLE_CONTROL_POINTS)
			return 0;

		if (lua.GetType(3) == LuaType.Angle) {
			QAngle ang = LuaAngle.Get_Angle(3);
			MathLib.AngleVectors(ang, out Vector3 forward, out Vector3 right, out Vector3 up);
			effect.SetControlPointOrientation(point, forward, right, up);
			return 0;
		}

		Vector3 upVec = LuaVector.Get_Vector(5);
		Vector3 rightVec = LuaVector.Get_Vector(4);
		Vector3 forwardVec = LuaVector.Get_Vector(3);
		effect.SetControlPointOrientation(point, forwardVec, rightVec, upVec);
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__SetSortOrigin(ILuaInterface lua) {
		Get_ValidEffect(lua)?.SetSortOrigin(LuaVector.Get_Vector(2));
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__GetEffectName(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		lua.PushString(effect.GetEffectName());
		return 1;
	}

	[LuaMethod]
	static int CNewParticleEffect__GetOwner(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		LuaEntity.Push_Entity(effect.GetOwner());
		return 1;
	}

	[LuaMethod]
	static int CNewParticleEffect__GetRenderBounds(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		effect.GetRenderBounds(out Vector3 mins, out Vector3 maxs);
		LuaVector.Push_Vector(mins);
		LuaVector.Push_Vector(maxs);
		return 2;
	}

	[LuaMethod]
	static int CNewParticleEffect__GetHighestControlPoint(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		lua.PushNumber(effect.GetHighestControlPoint());
		return 1;
	}

	[LuaMethod]
	static int CNewParticleEffect__GetAutoUpdateBBox(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		lua.PushBool(effect.GetAutoUpdateBBox());
		return 1;
	}

	[LuaMethod]
	static int CNewParticleEffect__Render(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		using MatRenderContextPtr renderContext = new(materials);
		effect.Render(renderContext.Context, effect.IsTwoPass(), null);
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__StartEmission(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		bool infiniteOnly = lua.GetType(2) == LuaType.Bool && lua.GetBool(2);
		effect.StartEmission(infiniteOnly);
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__StopEmission(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;
		bool infiniteOnly = lua.GetType(2) == LuaType.Bool && lua.GetBool(2);
		bool removeAllParticles = lua.GetType(3) == LuaType.Bool && lua.GetBool(3);
		bool wakeOnStop = lua.GetType(4) == LuaType.Bool && lua.GetBool(4);
		effect.StopEmission(infiniteOnly, removeAllParticles, wakeOnStop);
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__Restart(ILuaInterface lua) {
		Get_ValidEffect(lua)?.Restart();
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__SetShouldDraw(ILuaInterface lua) {
		Get_ValidEffect(lua)?.SetShouldDraw(lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__AddControlPoint(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		C_BaseEntity? owner = effect?.GetOwner();
		if (effect == null || owner == null)
			return 0;

		int point = (int)lua.CheckNumber(2);
		if ((uint)point >= ParticleConstants.MAX_PARTICLE_CONTROL_POINTS)
			return 0;

		int attachType = ClampAttachType(lua.CheckNumber(4));
		string? attachmentName = lua.GetType(5) == LuaType.Nil ? null : lua.CheckString(5);
		Vector3 offset = lua.GetType(6) == LuaType.Nil ? Vector3.Zero : LuaVector.Get_Vector(6);
		C_BaseEntity ent = LuaEntity.Get_Entity(3, false)!;

		owner.ParticleProp().AddControlPoint(effect, point, ent, (ParticleAttachment)attachType, attachmentName, offset);
		return 0;
	}

	[LuaMethod]
	static int CNewParticleEffect__StopEmissionAndDestroyImmediately(ILuaInterface lua) {
		CNewParticleEffect? effect = Get_ValidEffect(lua);
		if (effect == null)
			return 0;

		C_BaseEntity? owner = effect.GetOwner();
		if (owner != null) {
			owner.ParticleProp().StopEmissionAndDestroyImmediately(effect);
			return 0;
		}

		effect.StopEmission(false, true, false);
		return 0;
	}
}

public static partial class LuaEntity
{
	[LuaMethod]
	static int Entity__CreateParticleEffect(ILuaInterface lua) {
		C_BaseEntity ent = Get_Entity(1, false)!;
		int attachType = 0;
		if (lua.GetType(3) == LuaType.Number)
			attachType = LuaParticleEffect.ClampAttachType(lua.CheckNumber(3));

		ParticleProperty prop = ent.ParticleProp();
		CNewParticleEffect? effect = prop.Create(lua.CheckString(2), (ParticleAttachment)attachType, 0, Vector3.Zero);
		if (effect == null)
			return 0;

		LuaObject options = new();
		options.SetFromStack(3);
		if (options.isTable()) {
			for (int i = 1; i < ParticleConstants.MAX_PARTICLE_CONTROL_POINTS; i++) {
				LuaObject entry = new();
				options.GetMember(i, entry);
				if (!entry.isTable()) {
					entry.UnReference();
					break;
				}

				C_BaseEntity? pointEntity = null;
				LuaObject entityObj = new();
				entry.GetMember("entity", entityObj);
				if (entityObj.isEntity())
					pointEntity = entityObj.GetEntity() as C_BaseEntity;
				entityObj.UnReference();

				int pointAttachType = LuaParticleEffect.ClampAttachType(entry.GetMemberInt("attachtype", 0));
				prop.AddControlPoint(effect, i - 1, pointEntity, (ParticleAttachment)pointAttachType);

				LuaObject position = new();
				entry.GetMember("position", position);
				if (position.isVector())
					effect.SetControlPoint(i - 1, position.GetVector());
				position.UnReference();

				entry.UnReference();
			}
		}
		options.UnReference();

		LuaParticleEffect.Push_NewParticleEffect(effect);
		return 1;
	}

	[LuaMethod]
	static int Entity__StopParticleEmission(ILuaInterface lua) {
		Get_Entity(1, false)!.ParticleProp().StopEmission();
		return 0;
	}

	[LuaMethod]
	static int Entity__StopParticlesNamed(ILuaInterface lua) {
		C_BaseEntity ent = Get_Entity(1, false)!;
		bool forceRemoveInstantly = lua.GetType(3) == LuaType.Bool && lua.GetBool(3);
		ent.ParticleProp().StopParticlesNamed(lua.CheckString(2), forceRemoveInstantly);
		return 0;
	}

	[LuaMethod]
	static int Entity__StopParticlesWithNameAndAttachment(ILuaInterface lua) {
		C_BaseEntity ent = Get_Entity(1, false)!;
		bool forceRemoveInstantly = lua.GetType(3) == LuaType.Bool && lua.GetBool(3);
		int attachment = (int)lua.CheckNumber(3);
		ent.ParticleProp().StopParticlesWithNameAndAttachment(lua.CheckString(2), attachment, forceRemoveInstantly);
		return 0;
	}

	[LuaMethod]
	static int Entity__StopAndDestroyParticles(ILuaInterface lua) {
		Get_Entity(1, false)!.ParticleProp().StopEmissionAndDestroyImmediately();
		return 0;
	}
}
