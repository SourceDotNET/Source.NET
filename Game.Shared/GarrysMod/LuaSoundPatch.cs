#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source.Common.Audio;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaSoundPatch
{
	[LuaClass(typeof(SoundPatch), NullError = "Tried to use a NULL CSoundPatch!")]
	public static readonly LuaClass LC_CSoundPatch = new("CSoundPatch", LuaType.Sound, null, null);

	public static void Push_SoundPatch(SoundPatch? sound) => LC_CSoundPatch.Push(sound);

	static SoundPatch? Get_SoundPatch(ILuaInterface lua) {
		SoundPatch? sound = (SoundPatch?)LC_CSoundPatch.Get(1);
		if (sound == null)
			lua.Error("Tried to use a NULL CSoundPatch!");
		return sound;
	}

	[LuaGlobal]
	static int CreateSound(ILuaInterface lua) {
		BaseEntity ent = LuaEntity.Get_Entity(1, false)!;
#if CLIENT_DLL
		PASAttenuationFilter filter = new(ent);
		SoundPatch sound = SoundEnvelopeController.GetController().SoundCreate(filter, ent.EntIndex(), lua.CheckString(2));
		Push_SoundPatch(sound);
		return 1;
#else
		RecipientFilter filter = new();
		if (lua.GetType(3) == LuaType.RecipientFilter)
			filter.CopyFrom(LuaRecipientFilter.Get_RecipientFilter(3));
		else
			filter.CopyFrom(new PASAttenuationFilter(ent, ATTN_NORM));

		SoundPatch sound = SoundEnvelopeController.GetController().SoundCreate(filter, ent.EntIndex(), lua.CheckString(2));
		Push_SoundPatch(sound);
		return 1;
#endif
	}

	[LuaMethod]
	static int CSoundPatch____gc(ILuaInterface lua) {
		SoundPatch? sound = (SoundPatch?)LC_CSoundPatch.Get(1);
		if (sound != null)
			SoundEnvelopeController.GetController().GMOD_OnLuaObjectDeleted(sound);
		lua.SetUserType(1, 0);
		return 0;
	}

	[LuaMethod]
	static int CSoundPatch____tostring(ILuaInterface lua) {
		SoundPatch? sound = (SoundPatch?)LC_CSoundPatch.Get(1);
		if (sound == null) {
			lua.PushString("CSoundPatch [NULL]");
			return 1;
		}
		string name = SoundEnvelopeController.GetController().SoundGetName(sound) ?? "";
		lua.PushString($"CSoundPatch [{name}]");
		return 1;
	}

	[LuaMethod]
	static int CSoundPatch__ChangePitch(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		double deltaTime = lua.GetType(3) == LuaType.Number ? lua.GetNumber(3) : 0.0;
		SoundEnvelopeController.GetController().SoundChangePitch(sound, (float)lua.CheckNumber(2), (float)deltaTime);
		return 0;
	}

	[LuaMethod]
	static int CSoundPatch__ChangeVolume(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		double deltaTime = lua.GetType(3) == LuaType.Number ? lua.GetNumber(3) : 0.0;
		SoundEnvelopeController.GetController().SoundChangeVolume(sound, (float)lua.CheckNumber(2), (float)deltaTime);
		return 0;
	}

	[LuaMethod]
	static int CSoundPatch__FadeOut(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		SoundEnvelopeController.GetController().SoundFadeOut(sound, (float)lua.CheckNumber(2), false);
		return 0;
	}

	[LuaMethod]
	static int CSoundPatch__GetDSP(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		lua.PushNumber(SoundEnvelopeController.GetController().GetDSP(sound));
		return 1;
	}

	[LuaMethod]
	static int CSoundPatch__GetPitch(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		lua.PushNumber(SoundEnvelopeController.GetController().SoundGetPitch(sound));
		return 1;
	}

	[LuaMethod]
	static int CSoundPatch__GetSoundLevel(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		lua.PushNumber((int)SoundEnvelopeController.GetController().GetSoundLevel(sound));
		return 1;
	}

	[LuaMethod]
	static int CSoundPatch__GetSoundName(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		lua.PushString(SoundEnvelopeController.GetController().SoundGetName(sound) ?? "");
		return 1;
	}

	[LuaMethod]
	static int CSoundPatch__GetVolume(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		lua.PushNumber(SoundEnvelopeController.GetController().SoundGetVolume(sound));
		return 1;
	}

	[LuaMethod]
	static int CSoundPatch__IsPlaying(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		lua.PushBool(SoundEnvelopeController.GetController().IsPlaying(sound));
		return 1;
	}

	[LuaMethod]
	static int CSoundPatch__Play(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		SoundEnvelopeController.GetController().CommandClear(sound);
		SoundEnvelopeController.GetController().Play(sound, 1.0f, 100.0f, 0);
		return 0;
	}

	[LuaMethod]
	static int CSoundPatch__PlayEx(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		SoundEnvelopeController.GetController().CommandClear(sound);
		float pitch = (float)lua.CheckNumber(3);
		float volume = (float)lua.CheckNumber(2);
		SoundEnvelopeController.GetController().Play(sound, volume, pitch, 0);
		return 0;
	}

	[LuaMethod]
	static int CSoundPatch__SetDSP(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		SoundEnvelopeController.GetController().SetDSP(sound, (int)lua.CheckNumber(2));
		return 0;
	}

	[LuaMethod]
	static int CSoundPatch__SetSoundLevel(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		SoundEnvelopeController.GetController().SetSoundLevel(sound, (SoundLevel)(int)lua.CheckNumber(2));
		return 0;
	}

	[LuaMethod]
	static int CSoundPatch__Stop(ILuaInterface lua) {
		SoundPatch? sound = Get_SoundPatch(lua);
		if (sound == null)
			return 0;
		SoundEnvelopeController.GetController().Shutdown(sound);
		return 0;
	}
}
#endif
