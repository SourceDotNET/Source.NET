#if CLIENT_DLL || GAME_DLL
using Source.Common.Audio;
using Source.Common.GarrysMod.Lua;
using Source.Common.SoundEmitterSystem;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaSound
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_sound = new("sound");

	[LuaFunction]
	static int Add(ILuaInterface lua) {
		LuaObject data = new();
		data.SetFromStack(1);
		if (!data.isTable()) {
			g_Lua!.TypeError("table", 1);
			data.UnReference();
			return 0;
		}

		SoundParametersInternal parms = new();
		parms.SetChannel(data.GetMemberInt("channel", 0));
		parms.SetSoundLevel(data.GetMemberInt("level", (int)SoundLevel.LvlNorm));

		LuaObject volume = new();
		data.GetMember("volume", volume);
		float volumeStart;
		float volumeEnd;
		if (volume.isTable()) {
			LuaObject entry = new();
			volume.GetMember(1, entry);
			volumeStart = entry.isNumber() ? entry.GetFloat() : -1.0f;
			volume.GetMember(2, entry);
			volumeEnd = entry.isNumber() ? entry.GetFloat() : volumeStart;
			entry.UnReference();
		}
		else {
			volumeStart = volume.isNumber() ? (float)volume.GetDouble() : 0.0f;
			volumeEnd = volumeStart;
		}
		if (volumeStart > 0.0f && volumeEnd > 0.0f)
			parms.SetVolume(volumeStart, volumeEnd - volumeStart);

		float pitchStart = data.GetMemberFloat("pitchstart", -1.0f);
		float pitchEnd = data.GetMemberFloat("pitchend", -1.0f);
		if (pitchStart <= 0.0f || pitchEnd <= 0.0f) {
			LuaObject pitch = new();
			data.GetMember("pitch", pitch);
			if (pitch.isTable()) {
				LuaObject entry = new();
				pitch.GetMember(1, entry);
				pitchStart = entry.isNumber() ? entry.GetFloat() : -1.0f;
				pitch.GetMember(2, entry);
				pitchEnd = entry.isNumber() ? entry.GetFloat() : pitchStart;
				entry.UnReference();
			}
			else {
				pitchStart = pitch.isNumber() ? (float)pitch.GetDouble() : 0.0f;
				pitchEnd = pitchStart;
			}
			pitch.UnReference();
		}
		if (pitchStart > 0.0f && pitchEnd > 0.0f)
			parms.SetPitch((int)pitchStart, (int)(pitchEnd - pitchStart));

		LuaObject sound = new();
		data.GetMember("sound", sound);
		if (sound.isString())
			soundemitterbase.ExpandSoundNameMacros(ref parms, sound.GetString());
		else if (sound.isTable()) {
			string? wave;
			for (int i = 1; (wave = sound.GetMemberStr(i, null)) != null; i++)
				soundemitterbase.ExpandSoundNameMacros(ref parms, wave);
		}
		else {
			lua.ErrorNoHalt("sound.Add called without anything in the 'sound' key! (or wrong type?)");
			sound.UnReference();
			volume.UnReference();
			data.UnReference();
			return 0;
		}

		string? name = data.GetMemberStr("name", null);
		if (name == null)
			lua.ErrorNoHalt("sound.Add called without anything in the 'name' key! (or wrong type?)");
		else
			soundemitterbase.AddSound(name, "scripts/sounds/lua.txt", parms);

		sound.UnReference();
		volume.UnReference();
		data.UnReference();
		return 0;
	}

	[LuaFunction]
	static int AddSoundOverrides(ILuaInterface lua) {
		soundemitterbase.AddSoundOverrides(lua.CheckString(1), false);
		return 0;
	}
}
#endif
