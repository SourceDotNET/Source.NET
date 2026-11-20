global using static Game.Client.SoundscapeGlobals;

using CommunityToolkit.HighPerformance;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;
using Source.Common.SoundEmitterSystem;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Game.Client;

public static class SoundscapeGlobals
{
	public static readonly C_SoundscapeSystem g_SoundscapeSystem = new();
	public static IGameSystem ClientSoundscapeSystem() => g_SoundscapeSystem;
	public static void Soundscape_OnStopAllSounds() => g_SoundscapeSystem.OnStopAllSounds();
	public static void Soundscape_Update(ref AudioParams audio) => g_SoundscapeSystem.UpdateAudioParams(ref audio);
	public static ConVar soundscape_fadetime = new("soundscape_fadetime", "3.0", FCvar.Cheat, "Time to crossfade sound effects between soundscapes");

	[ConCommand(helpText: "Flushes the client side soundscapes", flags: FCvar.ServerCanExecute | FCvar.Cheat)]
	static void cl_soundscape_flush() {
		g_SoundscapeSystem.GetAudioParams(out AudioParams tmp);

		g_SoundscapeSystem.Shutdown();
		g_SoundscapeSystem.Init();

		Soundscape_Update(ref tmp);
	}
}

public struct LoopingSound
{
	public Vector3 Position;
	public string WaveName;
	public float VolumeTarget;
	public float VolumeCurrent;
	public SoundLevel SoundLevel;
	public int Pitch;
	public int Id;
	public bool IsAmbient;
}

public struct RandomSound
{
	public Vector3 Position;
	public float NextPlayTime;
	public Source.Common.Interval Time;
	public Source.Common.Interval Volume;
	public Source.Common.Interval Pitch;
	public Source.Common.Interval SoundLevel;
	public float MasterVolume;
	public int WaveCount;
	public bool IsAmbient;
	public bool IsRandom;
	public KeyValues? Waves;

	public void Init() => this = default;
}

public struct SubSoundscapeParams
{
	public int RecurseLevel;
	public float MasterVolume;
	public int StartingPosition;
	public int PositionOverride;
	public int AmbientPositionOverride;
	public bool AllowDSP;
	public bool WroteSoundMixer;
	public bool WroteDSPVolume;
}

public class C_SoundscapeSystem : AutoGameSystemPerFrame
{
	public const int MAX_SOUNDSCAPE_RECURSION = 8;
	public const float DEFAULT_SOUND_RADIUS = 36.0f;
	public const string SOUNDSCAPE_MANIFEST_FILE = "scripts/soundscapes_manifest.txt";

	int RestoreFrame;

	readonly List<KeyValues> SoundscapeScripts = [];
	readonly List<KeyValues> Soundscapes = [];
	AudioParams Params;
	readonly List<LoopingSound> LoopingSounds = [];
	readonly List<RandomSound> RandomSounds = [];
	float NextRandomTime;
	int LoopingSoundId;
	int ForcedSoundscapeIndex;
	float ForcedSoundscapeRadius;

	[CvarIgnore] static ConVar? DSPVolumeVar;
	[CvarIgnore] static ConVar? SoundMixerVar;

	public override ReadOnlySpan<char> Name() => "C_SoundScapeSystem";

	public C_SoundscapeSystem() : base("C_SoundScapeSystem") => RestoreFrame = -1;

	public void OnStopAllSounds() {
		Params.Ent.Set(null);
		Params.SoundscapeIndex = -1;
		LoopingSounds.Clear();
		RandomSounds.Clear();
	}

	public override void LevelInitPreEntity() {
		Shutdown();
		Init();
		TouchSoundFiles();
	}

	public override void LevelInitPostEntity() {
		SoundMixerVar ??= cvar.FindVar("snd_soundmixer");
		DSPVolumeVar ??= cvar.FindVar("dsp_volume");
	}

	public override void LevelShutdownPreEntity() { }

	public override void LevelShutdownPostEntity() => OnStopAllSounds();

	public override void OnSave() { }

	public override void OnRestore() => throw new NotImplementedException();

	public override void SafeRemoveIfDesired() { }

	public override void PreRender() { }

	public override void PostRender() { }

	public override bool Init() {
		LoopingSoundId = 0;

		ReadOnlySpan<char> mapname = IGameSystem.MapName();
		ReadOnlySpan<char> mapSoundscapeFilename = default;
		if (!mapname.IsEmpty)
			mapSoundscapeFilename = $"scripts/soundscapes_{mapname}.txt";

		KeyValues manifest = new(SOUNDSCAPE_MANIFEST_FILE);
		if (filesystem.LoadKeyValues(manifest, IFileSystem.KeyValuesPreloadType.SoundScape, SOUNDSCAPE_MANIFEST_FILE, "GAME")) {
			for (KeyValues? sub = manifest.GetFirstSubKey(); sub != null; sub = sub.GetNextKey()) {
				if (stricmp(sub.Name, "file") == 0) {
					AddSoundScapeFile(sub.GetString());
					if (!mapSoundscapeFilename.IsEmpty && stricmp(sub.GetString(), mapSoundscapeFilename) == 0)
						mapSoundscapeFilename = default;
					continue;
				}

				Warning($"C_SoundscapeSystem::Init:  Manifest '{SOUNDSCAPE_MANIFEST_FILE}' with bogus file type '{sub.Name}', expecting 'file'\n");
			}

			if (!mapSoundscapeFilename.IsEmpty && filesystem.FileExists(mapSoundscapeFilename))
				AddSoundScapeFile(mapSoundscapeFilename);
		}
		else
			Error($"Unable to load manifest file '{SOUNDSCAPE_MANIFEST_FILE}'\n");

		return true;
	}

	public override void Shutdown() {
		for (int i = LoopingSounds.Count - 1; i >= 0; --i) {
			LoopingSound sound = LoopingSounds[i];
			StopLoopingSound(ref sound);
		}

		SoundscapeScripts.Clear();
		LoopingSounds.Clear();
		RandomSounds.Clear();
		Soundscapes.Clear();

		Params.Ent.Set(null);
		Params.SoundscapeIndex = -1;
	}

	public override void Update(TimeUnit_t frametime) {
		if (ForcedSoundscapeIndex >= 0) {
			C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
			if (player != null) {
				player.EyePositionAndVectors(out Vector3 origin, out Vector3 forward, out Vector3 right, out _);

				Params.LocalSound[0] = origin + ForcedSoundscapeRadius * (forward - right);
				Params.LocalSound[1] = origin + ForcedSoundscapeRadius * (forward + right);
				Params.LocalSound[2] = origin + ForcedSoundscapeRadius * (-forward - right);
				Params.LocalSound[3] = origin + ForcedSoundscapeRadius * (-forward + right);
				Params.LocalBits = 0x0007;
			}
		}

		UpdateLoopingSounds(frametime);
		UpdateRandomSounds((float)gpGlobals.CurTime);
	}

	public void PrintDebugInfo() => throw new NotImplementedException();

	public void UpdateAudioParams(ref AudioParams audio) {
		if (Params.SoundscapeIndex == audio.SoundscapeIndex && Params.Ent.Get() == audio.Ent.Get())
			return;

		Params = audio;
		ForcedSoundscapeIndex = -1;
		if (audio.Ent.Get() != null && audio.SoundscapeIndex >= 0 && audio.SoundscapeIndex < Soundscapes.Count) {
			DevReportSoundscapeName(audio.SoundscapeIndex);
			StartNewSoundscape(Soundscapes[audio.SoundscapeIndex]);
		}
		else {
			if (audio.Ent.Get() != null && audio.SoundscapeIndex != -1)
				DevMsg(1, $"Error: Bad soundscape! index={audio.SoundscapeIndex} count={Soundscapes.Count}\n");
		}
	}

	public void GetAudioParams(out AudioParams outParams) => outParams = Params;

	public int GetCurrentSoundscape() => throw new NotImplementedException();

	public void DevReportSoundscapeName(int index) {
		ReadOnlySpan<char> name = "none";
		if (index >= 0 && index < Soundscapes.Count)
			name = Soundscapes[index].Name;
		DevMsg(1, $"Soundscape: {name}\n");
	}

	public void UpdateLoopingSounds(TimeUnit_t frametime) {
		float period = soundscape_fadetime.GetFloat();
		float amount = (float)frametime;
		if (period > 0)
			amount *= 1.0f / period;

		int fadeCount = LoopingSounds.Count;
		while (fadeCount > 0) {
			fadeCount--;
			ref LoopingSound sound = ref LoopingSounds.AsSpan()[fadeCount];

			if (sound.VolumeCurrent != sound.VolumeTarget) {
				sound.VolumeCurrent = MathLib.Approach(sound.VolumeTarget, sound.VolumeCurrent, amount);
				if (sound.VolumeTarget == 0 && sound.VolumeCurrent == 0) {
					StopLoopingSound(ref sound);
					LoopingSounds[fadeCount] = LoopingSounds[^1];
					LoopingSounds.RemoveAt(LoopingSounds.Count - 1);
				}
				else
					UpdateLoopingSound(ref sound);
			}
		}
	}

	public int AddLoopingAmbient(ReadOnlySpan<char> soundName, float volume, int pitch) => AddLoopingSound(soundName, true, volume, SoundLevel.LvlNorm, pitch, vec3_origin);

	public void UpdateLoopingSound(ref LoopingSound loopSound) {
		if (loopSound.IsAmbient)
			enginesound.EmitAmbientSound(loopSound.WaveName, loopSound.VolumeCurrent, loopSound.Pitch, (int)SoundFlags.ChangeVolume);
		else {
			LocalPlayerFilter filter = new();

			EmitSound_t ep = new() {
				Channel = (int)SoundEntityChannel.Static,
				SoundName = loopSound.WaveName,
				Volume = loopSound.VolumeCurrent,
				SoundLevel = loopSound.SoundLevel,
				Flags = SoundFlags.ChangeVolume,
				Pitch = loopSound.Pitch,
				Origin = ref loopSound.Position
			};

			C_BaseEntity.EmitSound(filter, SOUND_FROM_WORLD, in ep);
		}
	}

	public void StopLoopingSound(ref LoopingSound loopSound) {
		if (loopSound.IsAmbient)
			enginesound.EmitAmbientSound(loopSound.WaveName, 0, 0, (int)SoundFlags.Stop);
		else
			C_BaseEntity.StopSound(SOUND_FROM_WORLD, (int)SoundEntityChannel.Static, loopSound.WaveName);
	}

	public int AddLoopingSound(ReadOnlySpan<char> soundName, bool isAmbient, float volume, SoundLevel soundLevel, int pitch, in Vector3 position) {
		int soundSlot = LoopingSounds.Count - 1;
		bool forceSoundUpdate = false;
		Span<LoopingSound> sounds = LoopingSounds.AsSpan();
		while (soundSlot >= 0) {
			ref LoopingSound sound = ref sounds[soundSlot];

			if (sound.Id != LoopingSoundId &&
				sound.Pitch == pitch &&
				stricmp(soundName, sound.WaveName) == 0) {
				if (isAmbient == true &&
					sound.IsAmbient == true)
					break;
				else if (isAmbient == sound.IsAmbient) {
					if (MathLib.VectorsAreEqual(position, sound.Position, 0.1f))
						break;
					else {
						StopLoopingSound(ref sound);
						forceSoundUpdate = true;
						break;
					}
				}
			}
			soundSlot--;
		}

		if (soundSlot < 0) {
			LoopingSounds.Add(default);
			soundSlot = LoopingSounds.Count - 1;
			sounds = LoopingSounds.AsSpan();
			if (isAmbient) {
				enginesound.EmitAmbientSound(soundName, 0, pitch);
				sounds[soundSlot].VolumeCurrent = 0.0f;
			}
			else {
				LocalPlayerFilter filter = new();

				EmitSound_t ep = new() {
					Channel = (int)SoundEntityChannel.Static,
					SoundName = soundName,
					Volume = 0.05f,
					SoundLevel = soundLevel,
					Pitch = pitch,
					Origin = ref position
				};

				C_BaseEntity.EmitSound(filter, SOUND_FROM_WORLD, in ep);
				sounds[soundSlot].VolumeCurrent = 0.05f;
			}
		}

		ref LoopingSound slot = ref LoopingSounds.AsSpan()[soundSlot];
		slot.WaveName = new string(soundName);
		slot.VolumeTarget = volume;
		slot.Pitch = pitch;
		slot.Id = LoopingSoundId;
		slot.IsAmbient = isAmbient;
		slot.Position = position;
		slot.SoundLevel = soundLevel;

		if (forceSoundUpdate)
			UpdateLoopingSound(ref slot);

		return soundSlot;
	}

	public int AddRandomSound(in RandomSound sound) {
		RandomSounds.Add(sound);
		int index = RandomSounds.Count - 1;
		RandomSounds.AsSpan()[index].NextPlayTime = (float)gpGlobals.CurTime + 0.5f * Interval.Random(sound.Time);
		return index;
	}

	public void PlayRandomSound(ref RandomSound sound) {
		Assert(sound.WaveCount > 0);

		int waveId = RandomInt(0, sound.WaveCount - 1);
		KeyValues? waves = sound.Waves;
		while (waveId > 0 && waves != null) {
			waves = waves.GetNextKey();
			waveId--;
		}
		if (waves == null)
			return;

		ReadOnlySpan<char> waveName = waves.GetString();

		if (waveName.IsEmpty)
			return;

		if (sound.IsAmbient)
			enginesound.EmitAmbientSound(waveName, sound.MasterVolume * Interval.Random(sound.Volume), (int)Interval.Random(sound.Pitch));
		else {
			LocalPlayerFilter filter = new();

			if (sound.IsRandom)
				sound.Position = GenerateRandomSoundPosition();

			EmitSound_t ep = new() {
				Channel = (int)SoundEntityChannel.Static,
				SoundName = waveName,
				Volume = sound.MasterVolume * Interval.Random(sound.Volume),
				SoundLevel = (SoundLevel)(int)Interval.Random(sound.SoundLevel),
				Pitch = (int)Interval.Random(sound.Pitch),
				Origin = ref sound.Position
			};

			C_BaseEntity.EmitSound(filter, SOUND_FROM_WORLD, in ep);
		}
	}

	public void UpdateRandomSounds(float gameClock) {
		if (gameClock < NextRandomTime)
			return;

		NextRandomTime = gameClock + 3600;

		Span<RandomSound> randomSounds = RandomSounds.AsSpan();
		for (int i = RandomSounds.Count - 1; i >= 0; i--) {
			if (gameClock >= randomSounds[i].NextPlayTime) {
				PlayRandomSound(ref randomSounds[i]);
				randomSounds[i].NextPlayTime = gameClock + Interval.Random(randomSounds[i].Time);
			}

			if (randomSounds[i].NextPlayTime < NextRandomTime)
				NextRandomTime = randomSounds[i].NextPlayTime;
		}
	}

	public Vector3 GenerateRandomSoundPosition() {
		float angle = RandomFloat(-180, 180);
		MathLib.SinCos(angle, out float sinAngle, out float cosAngle);
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player != null) {
			player.EyePositionAndVectors(out Vector3 origin, out Vector3 forward, out Vector3 right, out _);
			return origin + DEFAULT_SOUND_RADIUS * (cosAngle * right + sinAngle * forward);
		}
		else
			return CurrentViewOrigin() + DEFAULT_SOUND_RADIUS * (cosAngle * CurrentViewRight() + sinAngle * CurrentViewForward());
	}

	public void ForceSoundscape(ReadOnlySpan<char> soundscapeName, float radius) => throw new NotImplementedException();

	public int FindSoundscapeByName(ReadOnlySpan<char> soundscapeName) {
		for (int i = Soundscapes.Count - 1; i >= 0; --i) {
			if (stricmp(Soundscapes[i].Name, soundscapeName) == 0)
				return i;
		}

		return -1;
	}

	public ReadOnlySpan<char> SoundscapeNameByIndex(int index) => throw new NotImplementedException();

	public KeyValues? SoundscapeByIndex(int index) {
		if (index >= 0 && index < Soundscapes.Count)
			return Soundscapes[index];
		return null;
	}

	public void StartNewSoundscape(KeyValues? soundscape) {
		for (int i = LoopingSounds.Count - 1; i >= 0; i--) {
			ref LoopingSound sound = ref CollectionsMarshal.AsSpan(LoopingSounds)[i];
			sound.VolumeTarget = 0;
			if (soundscape == null)
				sound.VolumeCurrent = 0;
		}

		LoopingSoundId++;

		RandomSounds.Clear();
		NextRandomTime = (float)gpGlobals.CurTime;

		if (soundscape != null) {
			SubSoundscapeParams parms = default;
			parms.AllowDSP = true;
			parms.WroteSoundMixer = false;
			parms.WroteDSPVolume = false;

			parms.MasterVolume = 1.0f;
			parms.StartingPosition = 0;
			parms.RecurseLevel = 0;
			parms.PositionOverride = -1;
			parms.AmbientPositionOverride = -1;
			StartSubSoundscape(soundscape, ref parms);

			// if (!parms.WroteDSPVolume)
			// 	DSPVolumeVar!.Revert();
			// if (!parms.WroteSoundMixer)
			// 	SoundMixerVar!.Revert();
			// TODO
		}
	}

	public void StartSubSoundscape(KeyValues? soundscape, ref SubSoundscapeParams parms) {
		KeyValues? key = soundscape!.GetFirstSubKey();
		while (key != null) {
			if (stricmp(key.Name, "dsp") == 0) {
				if (parms.AllowDSP)
					ProcessDSP(key);
			}
			else if (stricmp(key.Name, "dsp_player") == 0) {
				if (parms.AllowDSP)
					ProcessDSPPlayer(key);
			}
			else if (stricmp(key.Name, "playlooping") == 0)
				ProcessPlayLooping(key, in parms);
			else if (stricmp(key.Name, "playrandom") == 0)
				ProcessPlayRandom(key, in parms);
			else if (stricmp(key.Name, "playsoundscape") == 0)
				ProcessPlaySoundscape(key, ref parms);
			else if (stricmp(key.Name, "Soundmixer") == 0) {
				if (parms.AllowDSP)
					ProcessSoundMixer(key, ref parms);
			}
			else if (stricmp(key.Name, "dsp_volume") == 0) {
				if (parms.AllowDSP)
					ProcessDSPVolume(key, ref parms);
			}
			else
				DevMsg(1, $"Soundscape {soundscape.Name}:Unknown command {key.Name}\n");

			key = key.GetNextKey();
		}
	}

	public void ProcessDSP(KeyValues? dsp) => enginesound.SetRoomType(new LocalPlayerFilter(), dsp!.GetInt());

	public void ProcessDSPPlayer(KeyValues? dspPlayer) => enginesound.SetPlayerDSP(new LocalPlayerFilter(), dspPlayer!.GetInt(), false);

	public void ProcessPlayLooping(KeyValues? playLooping, in SubSoundscapeParams parms) {
		float volume = 0;
		SoundLevel soundlevel = ATTN_TO_SNDLVL(ATTN_NORM);
		ReadOnlySpan<char> soundName = default;
		int pitch = PITCH_NORM;
		int positionIndex = -1;
		bool suppress = false;
		KeyValues? key = playLooping!.GetFirstSubKey();
		while (key != null) {
			if (stricmp(key.Name, "volume") == 0)
				volume = parms.MasterVolume * Interval.Random(Interval.Read(key.GetString()));
			else if (stricmp(key.Name, "pitch") == 0)
				pitch = (int)Interval.Random(Interval.Read(key.GetString()));
			else if (stricmp(key.Name, "wave") == 0)
				soundName = key.GetString();
			else if (stricmp(key.Name, "position") == 0)
				positionIndex = parms.StartingPosition + key.GetInt();
			else if (stricmp(key.Name, "attenuation") == 0)
				soundlevel = ATTN_TO_SNDLVL(Interval.Random(Interval.Read(key.GetString())));
			else if (stricmp(key.Name, "soundlevel") == 0) {
				if (strnicmp(key.GetString(), "SNDLVL_", "SNDLVL_".Length) == 0)
					soundlevel = SoundParametersInternal.TextToSoundLevel(key.GetString());
				else
					soundlevel = (SoundLevel)(int)Interval.Random(Interval.Read(key.GetString()));
			}
			else if (stricmp(key.Name, "suppress_on_restore") == 0)
				suppress = key.GetInt() != 0;
			else
				DevMsg(1, $"Ambient {playLooping.Name}:Unknown command {key.Name}\n");

			key = key.GetNextKey();
		}

		if (positionIndex < 0)
			positionIndex = parms.AmbientPositionOverride;
		else if (parms.PositionOverride >= 0)
			positionIndex = parms.PositionOverride;

		if (IsBeingRestored() && suppress)
			return;

		if (volume != 0 && !soundName.IsEmpty) {
			if (positionIndex < 0)
				AddLoopingAmbient(soundName, volume, pitch);
			else {
				if (positionIndex > 31 || (Params.LocalBits & (1 << positionIndex)) == 0)
					return;
				AddLoopingSound(soundName, false, volume, soundlevel, pitch, Params.LocalSound[positionIndex]);
			}
		}
	}

	public void ProcessPlayRandom(KeyValues? playRandom, in SubSoundscapeParams parms) {
		RandomSound sound = default;
		sound.Init();
		sound.MasterVolume = parms.MasterVolume;
		int positionIndex = -1;
		bool suppress = false;
		bool randomPosition = false;
		KeyValues? key = playRandom!.GetFirstSubKey();
		while (key != null) {
			if (stricmp(key.Name, "volume") == 0)
				sound.Volume = Interval.Read(key.GetString());
			else if (stricmp(key.Name, "pitch") == 0)
				sound.Pitch = Interval.Read(key.GetString());
			else if (stricmp(key.Name, "attenuation") == 0) {
				Source.Common.Interval atten = Interval.Read(key.GetString());
				sound.SoundLevel.Start = (float)ATTN_TO_SNDLVL(atten.Start);
				sound.SoundLevel.Range = (float)ATTN_TO_SNDLVL(atten.Start + atten.Range) - sound.SoundLevel.Start;
			}
			else if (stricmp(key.Name, "soundlevel") == 0) {
				if (strnicmp(key.GetString(), "SNDLVL_", "SNDLVL_".Length) == 0) {
					sound.SoundLevel.Start = (float)SoundParametersInternal.TextToSoundLevel(key.GetString());
					sound.SoundLevel.Range = 0;
				}
				else
					sound.SoundLevel = Interval.Read(key.GetString());
			}
			else if (stricmp(key.Name, "time") == 0)
				sound.Time = Interval.Read(key.GetString());
			else if (stricmp(key.Name, "rndwave") == 0) {
				KeyValues? waves = key.GetFirstSubKey();
				sound.Waves = waves;
				sound.WaveCount = 0;
				while (waves != null) {
					sound.WaveCount++;
					waves = waves.GetNextKey();
				}
			}
			else if (stricmp(key.Name, "position") == 0) {
				if (stricmp(key.GetString(), "random") == 0)
					randomPosition = true;
				else
					positionIndex = parms.StartingPosition + key.GetInt();
			}
			else if (stricmp(key.Name, "suppress_on_restore") == 0)
				suppress = key.GetInt() != 0;
			else
				DevMsg(1, $"Random Sound {playRandom.Name}:Unknown command {key.Name}\n");

			key = key.GetNextKey();
		}

		if (positionIndex < 0)
			positionIndex = parms.AmbientPositionOverride;
		else if (parms.PositionOverride >= 0) {
			positionIndex = parms.PositionOverride;
			randomPosition = false;
		}

		if (IsBeingRestored() && suppress)
			return;

		if (sound.WaveCount != 0) {
			if (positionIndex < 0 && !randomPosition) {
				sound.IsAmbient = true;
				AddRandomSound(in sound);
			}
			else {
				sound.IsAmbient = false;
				if (randomPosition)
					sound.IsRandom = true;
				else {
					if (positionIndex > 31 || (Params.LocalBits & (1 << positionIndex)) == 0)
						return;
					sound.Position = Params.LocalSound[positionIndex];
				}
				AddRandomSound(in sound);
			}
		}
	}

	public void ProcessPlaySoundscape(KeyValues? playSoundscape, ref SubSoundscapeParams paramsIn) {
		SubSoundscapeParams subParams = paramsIn;

		subParams.AllowDSP = false;
		subParams.RecurseLevel++;
		if (subParams.RecurseLevel > MAX_SOUNDSCAPE_RECURSION) {
			DevMsg("Error!  Soundscape recursion overrun!\n");
			return;
		}
		KeyValues? key = playSoundscape!.GetFirstSubKey();
		ReadOnlySpan<char> soundscapeName = default;
		while (key != null) {
			if (stricmp(key.Name, "volume") == 0)
				subParams.MasterVolume = paramsIn.MasterVolume * Interval.Random(Interval.Read(key.GetString()));
			else if (stricmp(key.Name, "position") == 0)
				subParams.StartingPosition = paramsIn.StartingPosition + key.GetInt();
			else if (stricmp(key.Name, "positionoverride") == 0) {
				if (paramsIn.PositionOverride < 0) {
					subParams.PositionOverride = paramsIn.StartingPosition + key.GetInt();
					subParams.AmbientPositionOverride = paramsIn.StartingPosition + key.GetInt();
				}
			}
			else if (stricmp(key.Name, "ambientpositionoverride") == 0) {
				if (paramsIn.AmbientPositionOverride < 0)
					subParams.AmbientPositionOverride = paramsIn.StartingPosition + key.GetInt();
			}
			else if (stricmp(key.Name, "name") == 0)
				soundscapeName = key.GetString();
			else if (stricmp(key.Name, "soundlevel") == 0)
				DevMsg(1, "soundlevel not supported on sub-soundscapes\n");
			else
				DevMsg(1, $"Playsoundscape {(!soundscapeName.IsEmpty ? soundscapeName : (ReadOnlySpan<char>)playSoundscape.Name)}:Unknown command {key.Name}\n");

			key = key.GetNextKey();
		}

		if (!soundscapeName.IsEmpty) {
			KeyValues? soundscapeKeys = SoundscapeByIndex(FindSoundscapeByName(soundscapeName));
			if (soundscapeKeys != null)
				StartSubSoundscape(soundscapeKeys, ref subParams);
			else
				DevMsg(1, $"Trying to play unknown soundscape {soundscapeName}\n");
		}
	}

	public void ProcessSoundMixer(KeyValues? soundMixer, ref SubSoundscapeParams parms) => throw new NotImplementedException();

	public void ProcessDSPVolume(KeyValues? key, ref SubSoundscapeParams parms) {
		// DSPVolumeVar.SetValue(key.GetFloat());
		parms.WroteDSPVolume = true;
		// TODO
	}

	bool IsBeingRestored() => gpGlobals.FrameCount == RestoreFrame;

	void AddSoundScapeFile(ReadOnlySpan<char> filename) {
		DevMsg($"Adding soundscape file {filename.SliceNullTerminatedString()}\n");
		KeyValues script = new(filename);
		if (script.LoadFromFile(filesystem, filename)) {
			KeyValues? keys = script;
			while (keys != null) {
				if (keys.GetFirstSubKey() != null)
					Soundscapes.Add(keys);
				keys = keys.GetNextKey();
			}

			SoundscapeScripts.Add(script);
		}
	}

	void TouchPlayLooping(KeyValues? ambient) {
		for (KeyValues? key = ambient?.GetFirstSubKey(); key != null; key = key.GetNextKey()) {
			if (stricmp(key.Name, "wave") == 0)
				TouchSoundFile(key.GetString());
		}
	}

	void TouchPlayRandom(KeyValues? playRandom) {
		for (KeyValues? key = playRandom?.GetFirstSubKey(); key != null; key = key.GetNextKey()) {
			if (stricmp(key.Name, "rndwave") == 0) {
				for (KeyValues? waves = key.GetFirstSubKey(); waves != null; waves = waves.GetNextKey())
					TouchSoundFile(waves.GetString());
			}
		}
	}

	void TouchWaveFiles(KeyValues? soundScape) {
		for (KeyValues? key = soundScape?.GetFirstSubKey(); key != null; key = key.GetNextKey()) {
			if (stricmp(key.Name, "playlooping") == 0)
				TouchPlayLooping(key);
			else if (stricmp(key.Name, "playrandom") == 0)
				TouchPlayRandom(key);
		}
	}

	void TouchSoundFile(ReadOnlySpan<char> wavefile) => filesystem.GetFileTime($"sound/{SoundCharsUtils.SkipSoundChars(wavefile)}", "GAME");

	void TouchSoundFiles() {
		if (commandLine.FindParm("-makereslists") == 0)
			return;

		int c = Soundscapes.Count;
		for (int i = 0; i < c; ++i)
			TouchWaveFiles(Soundscapes[i]);
	}
}
