#if CLIENT_DLL
using Game.Client;
#else
using Game.Server;
#endif

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;
using Source.Common.Mathematics;
using Source.Common.SoundEmitterSystem;

using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Shared;


public struct SoundEnvelope
{
	public SoundEnvelope() {
		Current = 0.0f;
		Target = 0.0f;
		Rate = 0.0f;
		ForceUpdate = false;
	}

	public void SetTarget(float target, TimeUnit_t deltaTime) {
		float deltaValue = target - Current;

		if (deltaValue != 0 && deltaTime > 0) {
			Target = target;
			Rate = Math.Max(0.1f, Math.Abs(deltaValue / deltaTime));
		}
		else {
			if (target != Current) 
				ForceUpdate = true;

			SetValue(target);
		}
	}


	public void SetValue(float value) {
		if (Target != value) 
			ForceUpdate = true;

		Current = Target = value;
		Rate = 0;
	}
	public bool ShouldUpdate() {
		if (ForceUpdate) {
			ForceUpdate = false;
			return true;
		}

		if (Current != Target) 
			return true;

		return false;
	}


	public void Update(TimeUnit_t deltaTime) => Current = (float)MathLib.Approach(Target, Current, Rate * deltaTime);
	public float Value() => Current;

	float Current;
	float Target;
	TimeUnit_t Rate;
	bool ForceUpdate;
};



public class CopyRecipientFilter : IRecipientFilter
{
	public CopyRecipientFilter() => Flags = 0;

	public void Init(IRecipientFilter pSrc) {
		Flags = FLAG_ACTIVE;
		if (pSrc.IsReliable())
			Flags |= FLAG_RELIABLE;

		if (pSrc.IsInitMessage())
			Flags |= FLAG_INIT_MESSAGE;

		for (int i = 0; i < pSrc.GetRecipientCount(); i++) {
			int index = pSrc.GetRecipientIndex(i);

			if (index >= 0)
				Recipients.Add(index);
		}
	}

	public bool IsActive() {
		return (Flags & FLAG_ACTIVE) != 0;
	}

	public bool IsReliable() {
		return (Flags & FLAG_RELIABLE) != 0;
	}

	public int GetRecipientCount() {
		return Recipients.Count;
	}

	public int GetRecipientIndex(int slot) {
		return Recipients[slot];
	}

	public bool IsInitMessage() {
		return (Flags & FLAG_INIT_MESSAGE) != 0;
	}

#if CLIENT_DLL || GAME_DLL
	public bool AddRecipient(BasePlayer player) {
		Assert(player);

		int index = player.EntIndex();

		if (index < 0)
			return false;

		// Already in list
		if (Recipients.IndexOf(index) != -1)
			return false;

		Recipients.Add(index);
		return true;
	}
#endif

	public const int FLAG_ACTIVE = 0x1;
	public const int FLAG_RELIABLE = 0x2;
	public const int FLAG_INIT_MESSAGE = 0x4;

	int Flags;
	readonly List<int> Recipients = [];
}


public class SoundPatch : IDisposable
{
	static int g_SoundPatchCount;
	static readonly ConVar soundpatch_captionlength = new("soundpatch_captionlength", "2.0", FCvar.Replicated, "How long looping soundpatch captions should display for.");


	SoundEnvelope Pitch;
	SoundEnvelope Volume;
	SoundLevel SoundLevel;
	TimeUnit_t ShutdownTime;
	TimeUnit_t LastTime;
	string? SoundName;
	string? SoundScriptName;
	internal EHANDLE Ent;
	int EntityChannel;
	int Flags;
	int BaseFlags;
	int IsPlaying;
	float ScriptVolume; // Volume for this sound in sounds.txt
	CopyRecipientFilter Filter;
	float CloseCaptionDuration;
#if GMOD_DLL
	int DSP;
#endif

	public SoundPatch() {
		g_SoundPatchCount++;
		SoundName = null;
		SoundScriptName = null;
		CloseCaptionDuration = soundpatch_captionlength.GetFloat();
	}
	public void Dispose() {
		g_SoundPatchCount--;
	}

	static readonly ConCommand report_soundpatch = new("report_soundpatch", ReportSoundPatch, "reports sound patch count");
	static void ReportSoundPatch() {
#if GAME_DLL
		if (!Util.IsCommandIssuedByServerAdmin())
			return;
#endif

		Msg($"Current sound patches: {g_SoundPatchCount}\n");
	}

	public void Init(IRecipientFilter filter, BaseEntity? ent, int channel, ReadOnlySpan<char> soundName, SoundLevel soundLevel) {
		Ent.Set(ent);
		EntityChannel = channel;
		SoundParameters parms = new();
		if (!soundName.Contains(".wav", StringComparison.OrdinalIgnoreCase) && !soundName.Contains(".mp3", StringComparison.OrdinalIgnoreCase) &&
			BaseEntity.GetParametersForSound(soundName, ref parms, null)) {
			ScriptVolume = parms.Volume;

			SoundScriptName = new(soundName);

			SoundName = new(((ReadOnlySpan<char>)parms.SoundName).SliceNullTerminatedString());
			SoundLevel = parms.SoundLevel;

			EntityChannel = (int)parms.Channel;
		}
		else {
			SoundScriptName = new(soundName);

			ScriptVolume = 1.0f;
			SoundLevel = soundLevel;

			SoundName = new(soundName);
		}

		Volume.SetValue(0);
		Pitch.SetValue(0);
		IsPlaying = 0;
		ShutdownTime = 0;
		LastTime = 0;
		Filter = new();
		Filter.Init(filter);
		BaseFlags = 0;

#if GMOD_DLL
		DSP = 0;
#endif
	}

	public void ChangePitch(float pitchTarget, TimeUnit_t deltaTime) {
		Flags |= (int)SoundFlags.ChangePitch;
		Pitch.SetTarget(pitchTarget, deltaTime);
	}

	public void ChangeVolume(float volumeTarget, TimeUnit_t deltaTime) {
		Flags |= (int)SoundFlags.ChangeVolume;
		if (volumeTarget > 1.0f)
			volumeTarget = 1.0f;
		Volume.SetTarget(volumeTarget, deltaTime);
	}

	public void FadeOut(TimeUnit_t deltaTime, bool destroyOnFadeout) {
		ChangeVolume(0, deltaTime);
		if (!destroyOnFadeout)
			ShutdownTime = gpGlobals.CurTime + deltaTime;
	}

	public float GetPitch() => Pitch.Value();
	public float GetVolume() => Volume.Value();
	public string? GetName() => SoundName;
	public string? GetScriptName() => SoundScriptName;
	public int GetIsPlaying() => IsPlaying;
	public void SetCloseCaptionDuration(float duration) => CloseCaptionDuration = duration;
	public void SetBaseFlags(int flags) => BaseFlags = flags;

#if GMOD_DLL
	public void SetSoundLevel(float volume) => SoundLevel = (SoundLevel)(int)volume;
	public void SetDSP(int dsp) => DSP = dsp;
	public int GetDSP() => DSP;
#endif

	public int EntIndex() {
		Assert(!Ent.IsValid() || Ent.Get() != null);
		return Ent.Get() != null ? Ent.Get()!.EntIndex() : -1;
	}

	float GetVolumeForEngine() => ScriptVolume * Volume.Value();

	public void Shutdown() {
		if (IsPlaying != 0) {
			int entIndex = EntIndex();
			Assert(entIndex >= 0);
			if (entIndex >= 0)
				BaseEntity.StopSound(entIndex, EntityChannel, SoundName);
			IsPlaying = 0;
		}
	}

	public bool Update(TimeUnit_t time, TimeUnit_t deltaTime) {
		if (ShutdownTime != 0 && time > ShutdownTime) {
			Shutdown();
			return false;
		}

		if (EntIndex() < 0) {
			DevWarning($"CSoundPatch::Update:  Removing CSoundPatch ({SoundName}) with NULL EHandle\n");
			return false;
		}

		if (Pitch.ShouldUpdate()) {
			Pitch.Update(deltaTime);
			Flags |= (int)SoundFlags.ChangePitch;
		}
		else
			Flags &= ~(int)SoundFlags.ChangePitch;

		if (Volume.ShouldUpdate()) {
			Volume.Update(deltaTime);
			Flags |= (int)SoundFlags.ChangeVolume;
		}
		else
			Flags &= ~(int)SoundFlags.ChangeVolume;

		if (Flags != 0 && Filter.IsActive()) {
			Flags |= (int)SoundFlags.ChangeVolume;

			scoped EmitSound_t ep = new();
			ep.Channel = EntityChannel;
			ep.SoundName = SoundName;
			ep.Volume = GetVolumeForEngine();
			ep.SoundLevel = SoundLevel;
			ep.Flags = (SoundFlags)Flags;
			ep.Pitch = (int)Pitch.Value();
#if GMOD_DLL
			ep.SpecialDSP = DSP;
#endif
			BaseEntity.EmitSound(Filter, EntIndex(), ref ep, ref ep.SoundScriptHandle);

			Flags = 0;
		}

		return true;
	}

	public void Reset() => ShutdownTime = 0;

	public void StartSound(TimeUnit_t startTime = 0) {
		Flags = 0;
		if (Filter.IsActive()) {
			scoped EmitSound_t ep = new();
			ep.Channel = EntityChannel;
			ep.SoundName = SoundName;
			ep.Volume = GetVolumeForEngine();
			ep.SoundLevel = SoundLevel;
			ep.Flags = SoundFlags.ChangeVolume | (SoundFlags)BaseFlags;
			ep.Pitch = (int)Pitch.Value();
			ep.EmitCloseCaption = false;

			if (startTime != 0)
				ep.SoundTime = startTime;

#if GMOD_DLL
			ep.SpecialDSP = DSP;
#endif
			BaseEntity.EmitSound(Filter, EntIndex(), ref ep, ref ep.SoundScriptHandle);
		}
		IsPlaying = 1;
	}

	public void ResumeSound() {
		if (IsPlaying != 0 && Filter.IsActive()) {
			if (EntIndex() >= 0) {
				scoped EmitSound_t ep = new();
				ep.Channel = EntityChannel;
				ep.SoundName = SoundName;
				ep.Volume = GetVolumeForEngine();
				ep.SoundLevel = SoundLevel;
				ep.Flags = SoundFlags.ChangeVolume | SoundFlags.ChangePitch | (SoundFlags)BaseFlags;
				ep.Pitch = (int)Pitch.Value();
#if GMOD_DLL
				ep.SpecialDSP = DSP;
#endif
				BaseEntity.EmitSound(Filter, EntIndex(), ref ep, ref ep.SoundScriptHandle);
			}
			else
				DevWarning($"CSoundPatch::ResumeSound: Lost EHAndle on restore - destroy the sound patch in your entity's StopLoopingSounds! ({SoundName})\n");
		}
	}

	public void AddPlayerPost(BasePlayer player) {
		if (Filter.IsActive() && Filter.AddRecipient(player)) {
			SingleUserRecipientFilter filter = new(player);

			scoped EmitSound_t ep = new();
			ep.Channel = EntityChannel;
			ep.SoundName = SoundName;
			ep.Volume = GetVolumeForEngine();
			ep.SoundLevel = SoundLevel;
			ep.Flags = SoundFlags.ChangeVolume | (SoundFlags)BaseFlags;
			ep.Pitch = (int)Pitch.Value();
#if GMOD_DLL
			ep.SpecialDSP = DSP;
#endif
			BaseEntity.EmitSound(filter, EntIndex(), ref ep, ref ep.SoundScriptHandle);
		}
	}
}

public enum SoundCommands
{
	ChangeVolume,
	ChangePitch,
	Stop,
	Destroy,
}

public struct EnvelopePoint
{
	public float AmplitudeMin, AmplitudeMax;
	public float DurationMin, DurationMax;
}

public class SoundCommand
{
	public SoundCommand() { }
	public SoundCommand(SoundPatch? sound, TimeUnit_t executeTime, SoundCommands command, TimeUnit_t deltaTime, float value) {
		Patch = sound;
		Time = executeTime;
		DeltaTime = deltaTime;
		Command = command;
		Value = value;
	}

	public SoundPatch? Patch;
	public TimeUnit_t Time;
	public TimeUnit_t DeltaTime;
	public SoundCommands Command;
	public float Value;
}

public class SoundEnvelopeController() : AutoGameSystemPerFrame("CSoundControllerImp")
{
	static readonly SoundEnvelopeController g_Controller = new();
	public static SoundEnvelopeController GetController() => g_Controller;

	readonly List<SoundPatch> SoundList = [];
	readonly List<SoundCommand> CommandList = [];
	TimeUnit_t LastTime;

	void ProcessCommand(SoundCommand cmd) {
#if GMOD_DLL
		if (cmd.Patch == null) {
			RemoveFromList(cmd.Patch);
			cmd.Patch = null;
			return;
		}
#endif
		switch (cmd.Command) {
			case SoundCommands.ChangeVolume:
				cmd.Patch!.ChangeVolume(cmd.Value, cmd.DeltaTime);
				break;

			case SoundCommands.ChangePitch:
				cmd.Patch!.ChangePitch(cmd.Value, cmd.DeltaTime);
				break;

			case SoundCommands.Stop:
				cmd.Patch!.Shutdown();
				break;

			case SoundCommands.Destroy:
				RemoveFromList(cmd.Patch);
				cmd.Patch!.Dispose();
				cmd.Patch = null;
				break;
		}
	}

	void RemoveFromList(SoundPatch? sound) {
		if (sound != null)
			SoundList.Remove(sound);
#if GMOD_DLL
		if (sound == null)
			return;
#endif
		sound!.Shutdown();
	}

	public void Play(SoundPatch sound, float volume, float pitch, TimeUnit_t startTime = 0) {
		sound.Reset();

		sound.ChangeVolume(volume, 0);
		sound.ChangePitch(pitch, 0);

		if (sound.GetIsPlaying() != 0)
			CommandClear(sound);
		else {
			SoundList.Add(sound);
			sound.StartSound(startTime);
		}
	}

	void CommandInsert(SoundCommand command) {
		int index = CommandList.Count;
		while (index > 0 && CommandList[index - 1].Time > command.Time)
			index--;
		CommandList.Insert(index, command);
	}

	public void CommandAdd(SoundPatch sound, TimeUnit_t executeDeltaTime, SoundCommands command, TimeUnit_t commandTime, float commandValue) {
		SoundCommand cmd = new(sound, gpGlobals.CurTime + executeDeltaTime, command, commandTime, commandValue);
		CommandInsert(cmd);
	}

	public void SystemReset() {
		for (int i = SoundList.Count - 1; i >= 0; i--) {
			SoundPatch node = SoundList[i];
			node.Shutdown();
		}

		SoundList.Clear();

		CommandList.Clear();
	}

	public void SystemUpdate() {
		TimeUnit_t time = gpGlobals.CurTime;
		TimeUnit_t deltaTime = time - LastTime;

		if (deltaTime < 0)
			deltaTime = 0;

		LastTime = time;

		while (CommandList.Count != 0) {
			SoundCommand cmd = CommandList[0];
			if (time >= cmd.Time) {
				CommandList.RemoveAt(0);
				ProcessCommand(cmd);
			}
			else
				break;
		}

		for (int i = SoundList.Count - 1; i >= 0; i--) {
			SoundPatch node = SoundList[i];
			if (!node.Update(time, deltaTime)) {
				node.Reset();
				SoundList[i] = SoundList[^1];
				SoundList.RemoveAt(SoundList.Count - 1);
			}
		}
	}

	public void CommandClear(SoundPatch sound) {
		for (int i = CommandList.Count - 1; i >= 0; i--) {
			if (CommandList[i].Patch == sound)
				CommandList.RemoveAt(i);
		}
	}

#if GMOD_DLL
	public void ShutdownSoundsForEntity(int entIndex) {
		if (entIndex <= 0)
			return;
		for (int i = SoundList.Count - 1; i >= 0; i--) {
			SoundPatch sound = SoundList[i];
			BaseEntity? ent = sound.Ent.Get();
			if (ent != null && ent.EntIndex() == entIndex)
				Shutdown(sound);
		}
	}
#endif

	public void Shutdown(SoundPatch? sound) {
		if (sound == null)
			return;

		sound.Shutdown();
		CommandClear(sound);
		RemoveFromList(sound);
	}

	static BaseEntity? EntityFromIndex(int entIndex) {
		if (entIndex == -1)
			return null;
#if CLIENT_DLL
		return cl_entitylist.GetEnt(entIndex);
#else
		return Util.EntityByIndex(entIndex);
#endif
	}

	public SoundPatch SoundCreate(IRecipientFilter filter, int entIndex, ReadOnlySpan<char> soundName) {
		SoundPatch sound = new();
		sound.Init(filter, EntityFromIndex(entIndex), (int)SoundEntityChannel.Auto, soundName, SoundLevel.LvlNorm);
		return sound;
	}

	public SoundPatch SoundCreate(IRecipientFilter filter, int entIndex, int channel, ReadOnlySpan<char> soundName, float attenuation) {
		SoundPatch sound = new();
		sound.Init(filter, EntityFromIndex(entIndex), channel, soundName, ATTN_TO_SNDLVL(attenuation));
		return sound;
	}

	public SoundPatch SoundCreate(IRecipientFilter filter, int entIndex, int channel, ReadOnlySpan<char> soundName, SoundLevel soundLevel) {
		SoundPatch sound = new();
		sound.Init(filter, EntityFromIndex(entIndex), channel, soundName, soundLevel);
		return sound;
	}

	public SoundPatch SoundCreate(IRecipientFilter filter, int entIndex, scoped in EmitSound_t es) {
		SoundPatch sound = new();
		sound.Init(filter, EntityFromIndex(entIndex), es.Channel, es.SoundName, es.SoundLevel);
		sound.ChangeVolume(es.Volume, 0);
		sound.ChangePitch(es.Pitch, 0);

		if ((es.Flags & SoundFlags.ShouldPause) != 0)
			sound.SetBaseFlags((int)SoundFlags.ShouldPause);

		return sound;
	}

	public void SoundDestroy(SoundPatch? sound) {
		if (sound == null)
			return;

		Shutdown(sound);
		sound.Dispose();
	}

	public void SoundChangePitch(SoundPatch sound, float pitchTarget, TimeUnit_t deltaTime) => sound.ChangePitch(pitchTarget, deltaTime);

	public void SoundChangeVolume(SoundPatch sound, float volumeTarget, TimeUnit_t deltaTime) {
#if GMOD_DLL
		if (deltaTime == -10) {
			sound.SetSoundLevel(volumeTarget);
			return;
		}
#endif

		sound.ChangeVolume(volumeTarget, deltaTime);
	}

	public void SoundFadeOut(SoundPatch sound, TimeUnit_t deltaTime, bool destroyOnFadeout = false) {
		if (destroyOnFadeout && deltaTime == 0.0) {
			SoundDestroy(sound);
			return;
		}

		sound.FadeOut(deltaTime, destroyOnFadeout);
		if (destroyOnFadeout)
			CommandAdd(sound, deltaTime, SoundCommands.Destroy, 0.0, 0.0f);
	}

	public float SoundGetPitch(SoundPatch sound) => sound.GetPitch();
	public float SoundGetVolume(SoundPatch sound) => sound.GetVolume();
	public string? SoundGetName(SoundPatch sound) => sound.GetName();
	public void SoundSetCloseCaptionDuration(SoundPatch sound, float duration) => sound.SetCloseCaptionDuration(duration);

	public float SoundPlayEnvelope(SoundPatch sound, SoundCommands soundCommand, ReadOnlySpan<EnvelopePoint> points) {
		float amplitude = 0.0f;
		float duration = 0.0f;
		float totalDuration = 0.0f;

		CommandClear(sound);

		for (int i = 0; i < points.Length; i++) {
			if ((points[i].AmplitudeMin != -1.0f) || (points[i].AmplitudeMax != -1.0f))
				amplitude = random.RandomFloat(points[i].AmplitudeMin, points[i].AmplitudeMax);
			else if (i == 0)
				Msg("Invalid starting amplitude value in envelope!  (Cannot be -1)\n");

			if ((points[i].DurationMin != -1.0f) || (points[i].DurationMax != -1.0f))
				duration = random.RandomFloat(points[i].DurationMin, points[i].DurationMax);
			else if (i == 0)
				Msg("Invalid starting duration value in envelope! (Cannot be -1)\n");

			CommandAdd(sound, totalDuration, soundCommand, duration, amplitude);

			totalDuration += duration;
		}

		return totalDuration;
	}

	public void CheckLoopingSoundsForPlayer(BasePlayer player) {
		for (int i = SoundList.Count - 1; i >= 0; i--) {
			SoundPatch node = SoundList[i];
			node.AddPlayerPost(player);
		}
	}

	public bool IsPlaying(SoundPatch sound) => sound.GetIsPlaying() != 0;

#if GMOD_DLL
	public int GetDSP(SoundPatch sound) => sound.GetDSP();
	public void SetDSP(SoundPatch sound, int dsp) => sound.SetDSP(dsp);
#endif

#if CLIENT_DLL
	public override void Update(TimeUnit_t frametime) => SystemUpdate();
#else
	public override void PreClientUpdate() => SystemUpdate();
#endif

	public override void LevelShutdownPreEntity() => SystemReset();

	public override void OnRestore() {
		for (int i = SoundList.Count - 1; i >= 0; i--) {
			SoundPatch node = SoundList[i];
			if (node != null && node.GetIsPlaying() != 0)
				node.ResumeSound();
		}
	}
}

