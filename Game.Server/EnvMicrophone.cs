using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

using DEFINE = Source.DEFINE<EnvMicrophone>;

public enum MicrophoneResult
{
	Ok = 0,
	Swallow,
	Remove,
}

[LinkEntityToClass("env_microphone")]
public class EnvMicrophone : PointEntity
{
	[Flags]
	public enum SF
	{
		SoundCombat = 0x01,
		SoundWorld = 0x02,
		SoundPlayer = 0x04,
		SoundBulletImpact = 0x08,
		SwallowRoutedSounds = 0x10,
		SoundExplosion = 0x20,
		IgnoreNonattenuated = 0x40,
	}

	const float MICROPHONE_SETTLE_EPSILON = 0.005f;

	static readonly List<Handle<EnvMicrophone>> s_Microphones = [];

	public static readonly new DataMap DataDesc = new(typeof(EnvMicrophone), PointEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(Disabled), FieldType.Boolean, "StartDisabled"),
		DEFINE.FIELD(nameof(MeasureTarget), FieldType.EHandle),
		DEFINE.KEYFIELD(nameof(SoundMask), FieldType.Integer, "SoundMask"),
		DEFINE.KEYFIELD(nameof(Sensitivity), FieldType.Float, "Sensitivity"),
		DEFINE.KEYFIELD(nameof(SmoothFactor), FieldType.Float, "SmoothFactor"),
		DEFINE.KEYFIELD(nameof(SpeakerName), FieldType.String, "SpeakerName"),
		DEFINE.KEYFIELD(nameof(ListenFilterName), FieldType.String, "ListenFilter"),
		DEFINE.FIELD(nameof(ListenFilter), FieldType.EHandle),
		DEFINE.FIELD(nameof(Speaker), FieldType.EHandle),
		DEFINE.KEYFIELD(nameof(SpeakerDSPPreset), FieldType.Integer, "speaker_dsp_preset"),
		DEFINE.KEYFIELD(nameof(MaxRange), FieldType.Float, "MaxRange"),
		DEFINE.FIELD(nameof(LastSound), FieldType.String),

		DEFINE.INPUTFUNC(FieldType.Void, "Enable", nameof(InputEnable), (INPUTFUNCPTR)((self, data) => ((EnvMicrophone)self).InputEnable(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Disable", nameof(InputDisable), (INPUTFUNCPTR)((self, data) => ((EnvMicrophone)self).InputDisable(data))),
		DEFINE.INPUTFUNC(FieldType.String, "SetSpeakerName", nameof(InputSetSpeakerName), (INPUTFUNCPTR)((self, data) => ((EnvMicrophone)self).InputSetSpeakerName(data))),

		DEFINE.OUTPUT(nameof(SoundLevel), "SoundLevel", eventFuncs),
		DEFINE.OUTPUT(nameof(OnRoutedSound), "OnRoutedSound", eventFuncs),
		DEFINE.OUTPUT(nameof(OnHeardSound), "OnHeardSound", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public bool Disabled;
	public EHANDLE MeasureTarget = new();
	public SoundInstanceType SoundMask;
	public float Sensitivity;
	public float SmoothFactor;
	public float MaxRange;
	public string? SpeakerName;
	public EHANDLE Speaker = new();
	bool AvoidFeedback;
	public int SpeakerDSPPreset;
	public string? ListenFilterName;
	public Handle<BaseFilter> ListenFilter = new();

	public readonly OutputFloat SoundLevel = new();
	public readonly OutputEvent OnRoutedSound = new();
	public readonly OutputEvent OnHeardSound = new();

	public string LastSound = "";

	public override void UpdateOnRemove() {
		RemoveMicrophone(this);
		base.UpdateOnRemove();
	}

	static readonly (SF SpawnFlag, SoundInstanceType Type)[] SpawnFlagSoundTypes = [
		(SF.SoundCombat, SoundInstanceType.Combat),
		(SF.SoundWorld, SoundInstanceType.World),
		(SF.SoundPlayer, SoundInstanceType.Player),
		(SF.SoundBulletImpact, SoundInstanceType.BulletImpact),
		(SF.SoundExplosion, SoundInstanceType.ContextExplosion),
	];

	public override void Spawn() {
		for (int i = 0; i < SpawnFlagSoundTypes.Length; i++) {
			if (HasSpawnFlags(SpawnFlagSoundTypes[i].SpawnFlag))
				SoundMask |= SpawnFlagSoundTypes[i].Type;
		}

		if (Sensitivity == 0)
			Sensitivity = 1;
		else if (Sensitivity > 10)
			Sensitivity = 10;

		SmoothFactor = Math.Clamp(SmoothFactor, 0.0f, 0.9f);

		if (!Disabled)
			SetNextThink(gpGlobals.CurTime + 0.1f);
	}

	public override void Activate() {
		base.Activate();

		if (!string.IsNullOrEmpty(ListenFilterName))
			ListenFilter.Set(gEntList.FindEntityByName(null, ListenFilterName) as BaseFilter);

		if (!string.IsNullOrEmpty(Target)) {
			MeasureTarget.Set(gEntList.FindEntityByName(null, Target));

			if (MeasureTarget.Get() == null || MeasureTarget.Get()!.Edict() == null)
				MeasureTarget.Set(this);
		}
		else
			MeasureTarget.Set(this);

		ActivateSpeaker();
	}

	public override void OnRestore() {
		base.OnRestore();

		ActivateSpeaker();
	}

	public void ActivateSpeaker() {
		if (!Disabled) {
			ConVarRef dsp_speaker = new("dsp_speaker");
			if (dsp_speaker.IsValid()) {
				int dspPreset = SpeakerDSPPreset;
				if (dspPreset == 0)
					dspPreset = atoi(dsp_speaker.GetDefault());
				DevMsg(2, $"Microphone {GetEntityName()} set dsp_speaker to {dspPreset}.\n");
				dsp_speaker.SetValue(SpeakerDSPPreset);
			}
		}

		if (!string.IsNullOrEmpty(SpeakerName)) {
			if (FindMicrophone(this) == -1) {
				Handle<EnvMicrophone> handle = new();
				handle.Set(this);
				s_Microphones.Add(handle);
			}
		}
	}

	public void InputEnable(InputData inputdata) {
		if (Disabled) {
			Disabled = false;
			SetNextThink(gpGlobals.CurTime + 0.1f);

			ActivateSpeaker();
		}
	}

	public void InputDisable(InputData inputdata) {
		Disabled = true;
		if (Speaker.Get() != null) {
			StopSound(Speaker.Get()!.EntIndex(), (int)SoundEntityChannel.Static, LastSound);
			LastSound = "";

			RemoveMicrophone(this);
		}
		SetNextThink(TICK_NEVER_THINK);
	}

	public void InputSetSpeakerName(InputData inputdata) => SetSpeakerName(inputdata.Value.StringID());

	public bool CanHearSound(ref WorldSoundInstance sound, out float volume) {
		volume = 0;

		if (Disabled)
			return false;

		BaseFilter? filter = ListenFilter.Get();
		if (filter != null) {
			BaseEntity? soundOwner = sound.Owner.Get();
			if (soundOwner == null || !filter.PassesFilter(this, soundOwner))
				return false;
		}

		float distance = (sound.GetSoundOrigin() - MeasureTarget.Get()!.GetAbsOrigin()).Length();

		if (distance == 0) {
			volume = 1.0f;
			return true;
		}

		if (MaxRange != 0 && distance > MaxRange)
			return false;

		if (distance <= sound.Volume() * Sensitivity) {
			volume = 1 - (distance / (sound.Volume() * Sensitivity));
			volume = Math.Clamp(volume, 0.0f, 1.0f);
			return true;
		}

		return false;
	}

	public bool CanHearSound(int entindex, SoundLevel soundlevel, ref float volume, Vector3? origin) {
		if (Disabled) {
			volume = 0;
			return false;
		}

		if (HasSpawnFlags(SF.IgnoreNonattenuated) && soundlevel == Source.Common.Audio.SoundLevel.LvlNone)
			return false;

		BaseEntity? entity = null;
		if (entindex != 0)
			entity = BaseEntity.Instance(entindex);

		BaseFilter? filter = ListenFilter.Get();
		if (filter != null) {
			if (entity == null || !filter.PassesFilter(this, entity)) {
				volume = 0;
				return false;
			}
		}

		float distance = 0;
		if (origin.HasValue)
			distance = Vector3.Distance(origin.Value, MeasureTarget.Get()!.GetAbsOrigin());
		else if (entity != null)
			distance = Vector3.Distance(entity.WorldSpaceCenter(), MeasureTarget.Get()!.GetAbsOrigin());

		if (MaxRange != 0 && distance > MaxRange)
			return false;

		float gain = enginesound.GetDistGainFromSoundLevel(soundlevel, distance);
		volume *= gain;

		return volume > 0;
	}

	public void SetSensitivity(float sensitivity) => Sensitivity = sensitivity;

	public void SetSpeakerName(string? speakerName) {
		SpeakerName = speakerName;

		Speaker.Set(null);
		ActivateSpeaker();
	}

	public override void Think() {
		int soundIndex = SoundEnt.ActiveList();
		bool heardSound = false;

		float maxVolume = 0;

		while (soundIndex != SOUNDLIST_EMPTY) {
			ref WorldSoundInstance currentSound = ref SoundEnt.SoundPointerForIndex(soundIndex);

			if (!Unsafe.IsNullRef(ref currentSound)) {
				if ((SoundMask & currentSound.SoundType()) != 0) {
					if (CanHearSound(ref currentSound, out float volume) && volume > maxVolume) {
						maxVolume = volume;
						heardSound = true;
					}
				}
			}

			soundIndex = currentSound.NextSound();
		}

		if (heardSound)
			OnHeardSound.FireOutput(this, this);

		if (maxVolume != SoundLevel.Get()) {
			if (MathF.Abs(maxVolume - SoundLevel.Get()) < MICROPHONE_SETTLE_EPSILON)
				SoundLevel.Set(maxVolume, this, this);
			else
				SoundLevel.Set(maxVolume * (1 - SmoothFactor) + SoundLevel.Get() * SmoothFactor, this, this);
		}

		SetNextThink(gpGlobals.CurTime + 0.1f);
	}

	MicrophoneResult SoundPlayed(int entindex, ReadOnlySpan<char> soundname, SoundLevel soundlevel, float volume, SoundFlags flags, int pitch, Vector3? origin, TimeUnit_t soundtime, List<Vector3>? soundorigins) {
		if (AvoidFeedback)
			return MicrophoneResult.Ok;

		if ((flags & SoundFlags.Speaker) != 0)
			return MicrophoneResult.Ok;

		if (!CanHearSound(entindex, soundlevel, ref volume, origin))
			return MicrophoneResult.Ok;

		if (Speaker.Get() == null) {
			if (!string.IsNullOrEmpty(SpeakerName)) {
				Speaker.Set(gEntList.FindEntityByName(null, SpeakerName));

				if (Speaker.Get() == null) {
					Warning($"EnvMicrophone {GetEntityName()} specifies a non-existent speaker name: {SpeakerName}\n");
					SpeakerName = null;
				}
			}

			if (Speaker.Get() == null)
				return MicrophoneResult.Remove;
		}

		AvoidFeedback = true;

		flags |= SoundFlags.Speaker;
		BaseEntity speaker = Speaker.Get()!;
		PASAttenuationFilter filter = new(speaker);

		EmitSound_t ep = new();
		ep.Channel = (int)SoundEntityChannel.Static;
		ep.SoundName = soundname;
		ep.Volume = volume;
		ep.SoundLevel = soundlevel;
		ep.Flags = flags;
		ep.Pitch = pitch;
		ep.Origin = ref speaker.GetAbsOrigin();
		ep.SoundTime = soundtime;
		ep.SpeakerEntity = entindex;
		ep.SoundOrigin = soundorigins != null ? [] : null;

		BaseEntity.EmitSound(filter, speaker.EntIndex(), ep);

		LastSound = new(soundname);
		OnRoutedSound.FireOutput(this, this, 0);

		AvoidFeedback = false;

		if (soundorigins != null)
			soundorigins.AddRange(ep.SoundOrigin!);

		if (HasSpawnFlags(SF.SwallowRoutedSounds))
			return MicrophoneResult.Swallow;

		return MicrophoneResult.Ok;
	}

	public static bool OnSoundPlayed(int entindex, ReadOnlySpan<char> soundname, SoundLevel soundlevel, float volume, SoundFlags flags, int pitch, Vector3? origin, TimeUnit_t soundtime, List<Vector3>? soundorigins) {
		bool swallowed = false;

		int count = s_Microphones.Count;
		if (count > 0) {
			for (int i = count - 1; i >= 0; i--) {
				EnvMicrophone? microphone = s_Microphones[i].Get();
				if (microphone != null) {
					MicrophoneResult result = microphone.SoundPlayed(entindex, soundname, soundlevel, volume, flags, pitch, origin, soundtime, soundorigins);

					if (result == MicrophoneResult.Swallow)
						swallowed = true;
					else if (result == MicrophoneResult.Remove) {
						s_Microphones[i] = s_Microphones[^1];
						s_Microphones.RemoveAt(s_Microphones.Count - 1);
					}
				}
			}
		}

		return swallowed;
	}

	static int FindMicrophone(EnvMicrophone microphone) {
		for (int i = 0; i < s_Microphones.Count; i++) {
			if (s_Microphones[i].Get() == microphone)
				return i;
		}
		return -1;
	}

	static void RemoveMicrophone(EnvMicrophone microphone) {
		int index = FindMicrophone(microphone);
		if (index != -1)
			s_Microphones.RemoveAt(index);
	}
}
