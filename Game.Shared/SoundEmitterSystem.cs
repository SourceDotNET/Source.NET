#if CLIENT_DLL || GAME_DLL
#if CLIENT_DLL
global using static Game.Client.SoundEmitterSystemGlobals;

#else
global using static Game.Server.SoundEmitterSystemGlobals;
#endif
using Game.Shared;

using SharpCompress.Common;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;
using Source.Common.SoundEmitterSystem;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

#if CLIENT_DLL
namespace Game.Client;
#else
namespace Game.Server;
#endif

public class SoundEmitterSystem : BaseGameSystem
{
	public override ReadOnlySpan<char> Name() => "CSoundEmitterSystem";

	public override bool Init() {
		Assert(soundemitterbase != null);
		return soundemitterbase.ModInit();
	}

	public override void Shutdown() {
		Assert(soundemitterbase != null);
		soundemitterbase.ModShutdown();
	}

	internal void EmitSoundByHandle<T>(T filter, int entIndex, in EmitSound_t ep, ref HSOUNDSCRIPTHANDLE handle) where T : IRecipientFilter {
		SoundParameters parms = default;

		// Try to deduce the actor's gender
		Gender gender = Gender.None;
		BaseEntity? ent = BaseEntity.Instance(entIndex);
		if (ent != null) {
			ReadOnlySpan<char> actorModel = ent.GetModelName();
			gender = soundemitterbase.GetActorGender(actorModel);
		}

		if (!soundemitterbase.GetParametersForSoundEx(ep.SoundName, ref handle, ref parms, gender, true))
			return;

		if (((ReadOnlySpan<char>)parms.SoundName).IsStringEmpty)
			return;

		// handle SND_CHANGEPITCH/SND_CHANGEVOL and other sound flags.etc.
		if ((ep.Flags & SoundFlags.ChangePitch) != 0)
			parms.Pitch = ep.Pitch;

		if ((ep.Flags & SoundFlags.ChangeVolume) != 0)
			parms.Volume = ep.Volume;

#if !CLIENT_DLL
		bool swallowed = EnvMicrophone.OnSoundPlayed(
			entIndex,
			parms.SoundName,
			parms.SoundLevel,
			parms.Volume,
			ep.Flags,
			parms.Pitch,
			Unsafe.IsNullRef(in ep.Origin) ? null : ep.Origin,
			ep.SoundTime,
			ep.SoundOrigin);
		if (swallowed)
			return;
#endif

		TimeUnit_t st = ep.SoundTime;
		if (st == 0 && parms.DelayMsec != 0)
			st = gpGlobals.CurTime + parms.DelayMsec / 1000d;

		enginesound.EmitSound(
			filter,
			entIndex,
			(int)parms.Channel,
			parms.SoundName,
			parms.Volume,
			parms.SoundLevel,
			ep.Flags,
			parms.Pitch,
			ep.SpecialDSP,
			Unsafe.IsNullRef(in ep.Origin) ? null : ep.Origin,
			null,
			ep.SoundOrigin,
			true,
			st,
			ep.SpeakerEntity);

		if (!Unsafe.IsNullRef(ref ep.SoundDuration))
			ep.SoundDuration = enginesound.GetSoundDuration(parms.SoundName);

		// if (0 == (ep.Flags & (SoundFlags.ChangePitch | SoundFlags.ChangeVolume)))
		// EmitCloseCaption(filter, entindex, params, ep);
	}

	public void EmitSound<T>(scoped in T filter, int entindex, ref EmitSound_t ep) where T : IRecipientFilter {
		if (!ep.SoundName.IsEmpty &&
			(!stristr(ep.SoundName, ".wav").IsEmpty ||
			 !stristr(ep.SoundName, ".mp3").IsEmpty ||
			 ep.SoundName[0] == '!')) {
#if !CLIENT_DLL
			bool swallowed = EnvMicrophone.OnSoundPlayed(
				entindex,
				ep.SoundName,
				ep.SoundLevel,
				ep.Volume,
				ep.Flags,
				ep.Pitch,
				Unsafe.IsNullRef(in ep.Origin) ? null : ep.Origin,
				ep.SoundTime,
				ep.SoundOrigin);
			if (swallowed)
				return;
#endif

			if (ep.WarnOnDirectWaveReference && !stristr(ep.SoundName, ".wav").IsEmpty) {
				// WaveTrace( ep.SoundName, "Emitsound" );
			}

			enginesound.EmitSound(
				filter,
				entindex,
				ep.Channel,
				ep.SoundName,
				ep.Volume,
				ep.SoundLevel,
				ep.Flags,
				ep.Pitch,
				ep.SpecialDSP,
				Unsafe.IsNullRef(in ep.Origin) ? null : ep.Origin,
				null,
				ep.SoundOrigin,
				true,
				ep.SoundTime,
				ep.SpeakerEntity);
			if (!Unsafe.IsNullRef(ref ep.SoundDuration))
				ep.SoundDuration = enginesound.GetSoundDuration(ep.SoundName);

			// TraceEmitSound( "EmitSound:  Raw wave emitted '%s' (ent %i)\n", ep.SoundName, entindex );
			return;
		}

		if (ep.SoundScriptHandle == SOUNDEMITTER_INVALID_HANDLE)
			ep.SoundScriptHandle = (HSOUNDSCRIPTHANDLE)soundemitterbase.GetSoundIndex(ep.SoundName);

		if (ep.SoundScriptHandle == -1) {
			DevMsg($"CSoundEmitterSystem::EmitSound:  No such sound {ep.SoundName}\n");
			return;
		}

		EmitSoundByHandle(filter, entindex, ep, ref ep.SoundScriptHandle);
	}

	internal void StopSoundByHandle(int entindex, ReadOnlySpan<char> soundname, ref HSOUNDSCRIPTHANDLE handle) {
		if (handle == SOUNDEMITTER_INVALID_HANDLE)
			handle = (HSOUNDSCRIPTHANDLE)soundemitterbase.GetSoundIndex(soundname);

		if (handle == SOUNDEMITTER_INVALID_HANDLE)
			return;

		ref SoundParametersInternal parms = ref soundemitterbase.InternalGetParametersForSound(handle);
		if (Unsafe.IsNullRef(ref parms))
			return;

		int c = parms.NumSoundNames();
		for (int i = 0; i < c; ++i) {
			ReadOnlySpan<char> wavename = soundemitterbase.GetWaveName(parms.GetSoundNames()[i].Symbol);
			Assert(!wavename.IsEmpty);

			enginesound.StopSound(
				entindex,
				(int)parms.GetChannel(),
				wavename);

			// TraceEmitSound( "StopSound:  '%s' stopped as '%s' (ent %i)\n", soundname, wavename, entindex );
		}
	}

	public void StopSound(int entindex, ReadOnlySpan<char> soundname) {
		HSOUNDSCRIPTHANDLE handle = (HSOUNDSCRIPTHANDLE)soundemitterbase.GetSoundIndex(soundname);
		if (handle == SOUNDEMITTER_INVALID_HANDLE)
			return;

		StopSoundByHandle(entindex, soundname, ref handle);
	}

	public void StopSound(int entindex, int channel, ReadOnlySpan<char> sample) {
		if (!sample.IsEmpty && (!stristr(sample, ".wav").IsEmpty || !stristr(sample, ".mp3").IsEmpty || sample[0] == '!')) {
			enginesound.StopSound(entindex, channel, sample);

			// TraceEmitSound( "StopSound:  Raw wave stopped '%s' (ent %i)\n", sample, entindex );
		}
		else
			StopSound(entindex, sample);
	}

	internal void InternalPrecacheWaves(int soundIndex) {
		ref SoundParametersInternal internalParms = ref soundemitterbase.InternalGetParametersForSound(soundIndex);
		if (Unsafe.IsNullRef(ref internalParms))
			return;

		int waveCount = internalParms.NumSoundNames();
		if (waveCount == 0) {
			DevMsg($"CSoundEmitterSystem:  sounds.txt entry '{soundemitterbase.GetSoundName(soundIndex)}' has no waves listed under 'wave' or 'rndwave' key!!!\n");
		}
		else {
			for (int wave = 0; wave < waveCount; wave++)
				BaseEntity.PrecacheSound(soundemitterbase.GetWaveName(internalParms.GetSoundNames()[wave].Symbol));
		}
	}

	internal void InternalPrefetchWaves(int soundIndex) {
		ref SoundParametersInternal internalParms = ref soundemitterbase.InternalGetParametersForSound(soundIndex);
		if (Unsafe.IsNullRef(ref internalParms))
			return;

		int waveCount = internalParms.NumSoundNames();
		if (waveCount == 0) {
			DevMsg($"CSoundEmitterSystem:  sounds.txt entry '{soundemitterbase.GetSoundName(soundIndex)}' has no waves listed under 'wave' or 'rndwave' key!!!\n");
		}
		else {
			for (int wave = 0; wave < waveCount; wave++)
				BaseEntity.PrefetchSound(soundemitterbase.GetWaveName(internalParms.GetSoundNames()[wave].Symbol));
		}
	}

	public HSOUNDSCRIPTHANDLE PrecacheScriptSound(ReadOnlySpan<char> soundname) {
		int soundIndex = soundemitterbase.GetSoundIndex(soundname);
		if (!soundemitterbase.IsValidIndex(soundIndex)) {
			if (!stristr(soundname, ".wav").IsEmpty || !stristr(soundname, ".mp3").IsEmpty) {
				BaseEntity.PrecacheSound(soundname);
				return SOUNDEMITTER_INVALID_HANDLE;
			}

#if !CLIENT_DLL
			if (!soundname.IsEmpty && soundname[0] != '\0') {
				if (PrecacheScriptSoundFailures.Add(new string(soundname)))
					DevMsg($"PrecacheScriptSound '{soundname}' failed, no such sound script entry\n");
			}
#endif
			return (HSOUNDSCRIPTHANDLE)soundIndex;
		}

		InternalPrecacheWaves(soundIndex);
		return (HSOUNDSCRIPTHANDLE)soundIndex;
	}

	public void PrefetchScriptSound(ReadOnlySpan<char> soundname) {
		int soundIndex = soundemitterbase.GetSoundIndex(soundname);
		if (!soundemitterbase.IsValidIndex(soundIndex)) {
			if (!stristr(soundname, ".wav").IsEmpty || !strstr(soundname, ".mp3").IsEmpty)
				BaseEntity.PrefetchSound(soundname);
			return;
		}

		InternalPrefetchWaves(soundIndex);
	}

#if !CLIENT_DLL
	static readonly HashSet<string> PrecacheScriptSoundFailures = new();
#endif

	public void EmitAmbientSound(int entindex, in Vector3 origin, ReadOnlySpan<char> soundname, float volume, int flags, int pitch, TimeUnit_t soundtime, out TimeUnit_t duration) {
		duration = 0;

		// Pull data from parameters
		SoundParameters parms = default;

		if (!soundemitterbase.GetParametersForSound(soundname, ref parms, Gender.None))
			return;

		if ((flags & (int)SoundFlags.ChangePitch) != 0)
			parms.Pitch = pitch;

		if ((flags & (int)SoundFlags.ChangeVolume) != 0)
			parms.Volume = volume;

#if CLIENT_DLL
		enginesound.EmitAmbientSound(parms.SoundName, parms.Volume, parms.Pitch, flags, soundtime);
#else
		engine.EmitAmbientSound(entindex, origin, parms.SoundName, parms.Volume, parms.SoundLevel, flags, parms.Pitch, (float)soundtime);
#endif

		bool needsCC = (flags & (int)(SoundFlags.Stop | SoundFlags.ChangeVolume | SoundFlags.ChangePitch)) == 0;

		TimeUnit_t soundduration = 0.0f;

		if (needsCC) {
			soundduration = enginesound.GetSoundDuration(parms.SoundName);
			duration = soundduration;
		}

		// TraceEmitSound( "EmitAmbientSound:  '%s' emitted as '%s' (ent %i)\n", soundname, params.soundname, entindex );

		// We only want to trigger the CC on the start of the sound, not on any changes or halting of the sound
		// if ( needsCC ) EmitCloseCaption( filter, entindex, false, soundname, dummy, soundduration, false );
	}

	public void EmitAmbientSound(int entindex, in Vector3 origin, ReadOnlySpan<char> sample, float volume, SoundLevel soundlevel, int flags, int pitch, TimeUnit_t soundtime, out TimeUnit_t duration) {
		duration = 0;

#if !CLIENT_DLL
		bool swallowed = EnvMicrophone.OnSoundPlayed(
			entindex,
			sample,
			soundlevel,
			volume,
			(SoundFlags)flags,
			pitch,
			origin,
			soundtime,
			null);
		if (swallowed)
			return;
#endif

		if (!sample.IsEmpty && (!stristr(sample, ".wav").IsEmpty || !stristr(sample, ".mp3").IsEmpty)) {
#if CLIENT_DLL
			enginesound.EmitAmbientSound(sample, volume, pitch, flags, soundtime);
#else
			engine.EmitAmbientSound(entindex, origin, sample, volume, soundlevel, flags, pitch, (float)soundtime);
#endif

			duration = enginesound.GetSoundDuration(sample);

			// TraceEmitSound( "EmitAmbientSound:  Raw wave emitted '%s' (ent %i)\n", pSample, entindex );
		}
		else
			EmitAmbientSound(entindex, origin, sample, volume, flags, pitch, soundtime, out duration);
	}
}

public static class SoundEmitterSystemGlobals
{
	public static readonly SoundEmitterSystem g_SoundEmitterSystem = new();

#if CLIENT_DLL
	[ConCommand("playgamesound", "Play a sound from the game sounds txt file", FCvar.ClientCmdCanExecute | FCvar.ServerCanExecute, autoCompleteMethod: nameof(GamesoundCompletion))]
	static void Playgamesound_f(in TokenizedCommand args) {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player != null) {
			if (args.ArgC() > 2) {
				Vector3 position = player.EyePosition();
				player.GetVectors(out Vector3 forward, out _, out _);
				position += (float)atof(args[2]) * forward;
				PASAttenuationFilter filter = new(player);
				scoped EmitSound_t parms = new();
				parms.SoundName = args[1];
				parms.Origin = ref position;
				parms.Volume = 0.0f;
				parms.Pitch = 0;
				g_SoundEmitterSystem.EmitSound(filter, 0, ref parms);
			}
			else
				player.EmitSound(args[1]);
		}
		else
			Msg("Can't play until a game is started.\n");
	}

	static IEnumerable<string> GamesoundCompletion(string partial) {
		const string cmdname = "playgamesound";
		const int COMMAND_COMPLETION_MAXITEMS = 64;
		string? substring = null;
		if (partial.Contains(cmdname, StringComparison.Ordinal) && partial.Length > cmdname.Length + 1)
			substring = partial[(cmdname.Length + 1)..];

		int current = 0;
		for (int i = soundemitterbase.GetSoundCount() - 1; i >= 0 && current < COMMAND_COMPLETION_MAXITEMS; i--) {
			if (soundemitterbase.GetSoundName(i).IsEmpty)
				continue;

			if (substring == null || soundemitterbase.GetSoundName(i).StartsWith(substring, StringComparison.OrdinalIgnoreCase)) {
				yield return $"{cmdname} {soundemitterbase.GetSoundName(i)}";
				current++;
			}
		}
	}
#endif
}

public partial class
#if CLIENT_DLL
C_BaseEntity
#else
BaseEntity
#endif
{
	public static void StopSound(int entIndex, int channel, ReadOnlySpan<char> sample) => g_SoundEmitterSystem.StopSound(entIndex, channel, sample);
	public static void StopSound(int entIndex, ReadOnlySpan<char> soundname) => g_SoundEmitterSystem.StopSound(entIndex, soundname);
	public void StopSound(ReadOnlySpan<char> soundname) {
#if CLIENT_DLL
		if (EntIndex() == -1) {
			StopSound(GetSoundSourceIndex(), soundname);
			return;
		}
#endif
		StopSound(EntIndex(), soundname);
	}

	public static SoundLevel LookupSoundLevel(ReadOnlySpan<char> soundname) {
		return soundemitterbase.LookupSoundLevel(soundname);
	}
	public static SoundLevel LookupSoundLevel(ReadOnlySpan<char> soundname, ref HSOUNDSCRIPTHANDLE handle) {
		return soundemitterbase.LookupSoundLevelByHandle(soundname, ref handle);
	}
	public void EmitSound(ReadOnlySpan<char> soundname, TimeUnit_t soundtime = 0) {
		PASAttenuationFilter filter = new(this, soundname);

		scoped EmitSound_t parms = new();
		parms.SoundName = soundname;
		parms.SoundTime = soundtime;
		parms.SoundDuration = ref Unsafe.NullRef<TimeUnit_t>();
		parms.WarnOnDirectWaveReference = true;

		BaseEntity.EmitSound(filter, EntIndex(), in parms);
	}

	public void EmitSound(ReadOnlySpan<char> soundname, TimeUnit_t soundtime, out TimeUnit_t duration) {
		duration = default;
		PASAttenuationFilter filter = new(this, soundname);

		scoped EmitSound_t parms = new();
		parms.SoundName = soundname;
		parms.SoundTime = soundtime;
		parms.SoundDuration = ref duration;
		parms.WarnOnDirectWaveReference = true;

		BaseEntity.EmitSound(filter, EntIndex(), in parms);
	}

	public static void EmitSound<T>(in T filter, int entIndex, ReadOnlySpan<char> soundname) where T : IRecipientFilter
		=> EmitSound<T>(filter, entIndex, soundname, default, default, out _);
	public static void EmitSound<T>(in T filter, int entIndex, ReadOnlySpan<char> soundname, out TimeUnit_t duration) where T : IRecipientFilter
		=> EmitSound<T>(filter, entIndex, soundname, default, default, out duration);
	public static void EmitSound<T>(in T filter, int entIndex, ReadOnlySpan<char> soundname, in Vector3 origin, out TimeUnit_t duration) where T : IRecipientFilter
		=> EmitSound<T>(filter, entIndex, soundname, in origin, default, out duration);
	public static void EmitSound<T>(in T filter, int entIndex, ReadOnlySpan<char> soundname, in Vector3 origin, TimeUnit_t soundtime, out TimeUnit_t duration) where T : IRecipientFilter {
		duration = default;
		if (soundname.IsStringEmpty)
			return;

		scoped EmitSound_t parms = new();
		parms.SoundName = soundname;
		parms.SoundTime = soundtime;
		parms.Origin = ref origin;
		parms.SoundDuration = ref duration;
		parms.WarnOnDirectWaveReference = true;

		EmitSound(filter, entIndex, ref parms, ref parms.SoundScriptHandle);
	}

	public static void EmitSound<T>(in T filter, int entIndex, ref EmitSound_t parms, ref HSOUNDSCRIPTHANDLE handle) where T : IRecipientFilter {
#if GAME_DLL
		BaseEntity? entity = Util.EntityByIndex(entIndex);
#else
		C_BaseEntity? entity = cl_entitylist.GetEnt(entIndex);
#endif
#if CLIENT_DLL || GAME_DLL
		entity?.ModifyEmitSoundParams(ref parms);
#endif
		// VPROF( "CBaseEntity::EmitSound" );
		// Call into the sound emitter system...
		g_SoundEmitterSystem.EmitSoundByHandle(filter, entIndex, parms, ref handle);
	}

	public static bool GetParametersForSound(ReadOnlySpan<char> soundName, ref SoundParameters parms, ReadOnlySpan<char> actorModel) {
		Gender gender = soundemitterbase.GetActorGender(actorModel);
		return soundemitterbase.GetParametersForSound(soundName, ref parms, gender);
	}

	public static bool GetParametersForSound(ReadOnlySpan<char> soundName, ref HSOUNDSCRIPTHANDLE handle, ref SoundParameters parms, ReadOnlySpan<char> actorModel) {
		Gender gender = soundemitterbase.GetActorGender(actorModel);
		return soundemitterbase.GetParametersForSoundEx(soundName, ref handle, ref parms, gender);
	}
}
#endif
