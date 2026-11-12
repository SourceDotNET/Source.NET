global using static Source.AudioSystem.AudioGlobals;
global using static Source.Common.SoundCharsUtils;
global using static Source.Common.Audio.SndGain;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.GarrysMod;

namespace Source.AudioSystem;

[EngineComponent]
public static class AudioGlobals
{
	[Dependency] public static IFileSystem filesystem = null!;
	[Dependency] public static ISoundServices soundServices = null!;
	[Dependency] public static ICommandLine CommandLine = null!;
#if GMOD_DLL
	[Dependency] public static IGet? get = null;
#endif
}

public class AudioSystem : IAudioSystem
{
	public AudioSystem() {
		SfxTable_Init();
	}

	public void Init() => S_Init();
	public void Shutdown() => S_Shutdown();
	public bool IsInitted() => S_IsInitted();

	public void StopAllSounds(bool clear) => S_StopAllSounds(clear);
	public void Update(AudioState? audioState) => S_Update(audioState);
	public void ExtraUpdate() => S_ExtraUpdate();
	public void ClearBuffer() => S_ClearBuffer();
	public void BlockSound() => S_BlockSound();
	public void UnblockSound() => S_UnblockSound();
	public float GetMasterVolume() => S_GetMasterVolume();
	public void SoundFade(float percent, float holdtime, float intime, float outtime) => S_SoundFade(percent, holdtime, intime, outtime);
	public void OnLoadScreen(bool value) => S_OnLoadScreen(value);
	public void EnableMusic(bool enable) => S_EnableMusic(enable);

	public int StartSound(ref StartSoundParams parms) => S_StartSound(ref parms);
	public void StopSound(int entnum, int entchannel) => S_StopSound(entnum, entchannel);

	public float ComputeDelayForSoundtime(float soundtime, ClockSyncIndex syncIndex) => S_ComputeDelayForSoundtime(soundtime, syncIndex);

	public void StopSoundByGuid(int guid) => S_StopSoundByGuid(guid);
	public float SoundDurationByGuid(int guid) => S_SoundDurationByGuid(guid);
	public int GetGuidForLastSoundEmitted() => S_GetGuidForLastSoundEmitted();
	public bool IsSoundStillPlaying(int guid) => S_IsSoundStillPlaying(guid);
	public void GetActiveSounds(List<SndInfo> sndlist) => S_GetActiveSounds(sndlist);
	public void SetVolumeByGuid(int guid, float fvol) => S_SetVolumeByGuid(guid, fvol);
	public float GetElapsedTimeByGuid(int guid) => S_GetElapsedTimeByGuid(guid);
	public bool IsLoopingSoundByGuid(int guid) => S_IsLoopingSoundByGuid(guid);
	public void ReloadSound(ReadOnlySpan<char> sample) => S_ReloadSound(sample);
	public float GetMono16Samples(ReadOnlySpan<char> name, List<short> sampleList) => S_GetMono16Samples(name, sampleList);

	public SfxTable DummySfx(ReadOnlySpan<char> name) => S_DummySfx(name);
	public SfxTable? PrecacheSound(ReadOnlySpan<char> sample) => S_PrecacheSound(sample);
	public void PrefetchSound(ReadOnlySpan<char> name, bool playOnce) => S_PrefetchSound(name, playOnce);
	public void MarkUISound(SfxTable sfx) => S_MarkUISound(sfx);
	public void ReloadFilesInList(IFileList filesToReload) => S_ReloadFilesInList(filesToReload);

	public float GetNominalClipDist() => S_GetNominalClipDist();

	public void MovieStart() => SND_MovieStart();
	public void MovieEnd() => SND_MovieEnd();

	public int GetCurrentStaticSounds(Span<SoundInfo> result, int sizeResult, int entchannel) => S_GetCurrentStaticSounds(result, sizeResult, entchannel);

	public void GetCurrentlyPlayingMusic(List<MusicSave> list) => S_GetCurrentlyPlayingMusic(list);
	public void RestartSong(in MusicSave song) => S_RestartSong(in song);

	public float GetSoundDuration(ReadOnlySpan<char> name) => AudioSource_GetSoundDuration(name);
	public float GetSoundDuration(SfxTable? sfx) => AudioSource_GetSoundDuration(sfx);

	public void MarkFirstTime() => snd_firsttime = true;
	public void LevelInit(ReadOnlySpan<char> mapname) => audiosourcecache.LevelInit(mapname);
	public void LevelShutdown() => audiosourcecache.LevelShutdown();

	public void ReadSentenceFile(ReadOnlySpan<char> sentenceFileName) => VOX_ReadSentenceFile(new(sentenceFileName));
	public int SentenceCount() => VOX_SentenceCount();
	public float SentenceLength(int sentenceNum) => VOX_SentenceLength(sentenceNum);
	public ReadOnlySpan<char> SentenceNameFromIndex(int sentenceNum) => VOX_SentenceNameFromIndex(sentenceNum);
	public string? LookupSentence(ReadOnlySpan<char> sentenceName, out int sentenceNum) => VOX_LookupStringManaged(sentenceName, out sentenceNum);
	public int SentenceGroupIndexFromName(ReadOnlySpan<char> groupName) => VOX_GroupIndexFromName(groupName);
	public ReadOnlySpan<char> SentenceGroupNameFromIndex(int groupIndex) => VOX_GroupNameFromIndex(groupIndex);
	public int SentenceGroupPick(int groupIndex, out string found) => VOX_GroupPick(groupIndex, out found, 64);
	public int SentenceGroupPickSequential(int groupIndex, out string found, int pick, bool reset) => VOX_GroupPickSequential(groupIndex, out found, 64, pick, reset);
	public void PrecacheSentenceGroup(IEngineSound engineSound, ReadOnlySpan<char> groupName, ReadOnlySpan<char> pathOverride) => VOX_PrecacheSentenceGroup(engineSound, groupName, pathOverride);

	public void DSP_FastReset(int dspType) => SndDsp.DSP_FastReset(dspType);

	public bool VoiceSE_Init() => VoiceSoundEngineInterface.VoiceSE_Init();
	public void VoiceSE_Term() => VoiceSoundEngineInterface.VoiceSE_Term();
	public void VoiceSE_Idle(float frametime) => VoiceSoundEngineInterface.VoiceSE_Idle(frametime);
	public int VoiceSE_StartChannel(int channel, int entity, bool proximity, int viewEntityIndex) => VoiceSoundEngineInterface.VoiceSE_StartChannel(channel, entity, proximity, viewEntityIndex);
	public void VoiceSE_EndChannel(int channel, int entity) => VoiceSoundEngineInterface.VoiceSE_EndChannel(channel, entity);
	public void VoiceSE_StartOverdrive() => VoiceSoundEngineInterface.VoiceSE_StartOverdrive();
	public void VoiceSE_EndOverdrive() => VoiceSoundEngineInterface.VoiceSE_EndOverdrive();
	public void VoiceSE_InitMouth(int entnum) => VoiceSoundEngineInterface.VoiceSE_InitMouth(entnum);
	public void VoiceSE_CloseMouth(int entnum) => VoiceSoundEngineInterface.VoiceSE_CloseMouth(entnum);
	public void VoiceSE_MoveMouth(int entnum, Span<short> samples, int numSamples) => VoiceSoundEngineInterface.VoiceSE_MoveMouth(entnum, samples, numSamples);
	public IVoiceRecord? CreateVoiceRecord(int sampleRate) => VoiceRecord_SDL.CreateVoiceRecord_SDL(sampleRate);
}
