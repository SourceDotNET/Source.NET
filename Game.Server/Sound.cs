global using static Game.Server.SoundGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

// runtime pitch shift and volume fadein/out structure

// NOTE: IF YOU CHANGE THIS STRUCT YOU MUST CHANGE THE SAVE/RESTORE VERSION NUMBER
// SEE BELOW (in the typedescription for the class)
public struct DynPitchVol
{
	// NOTE: do not change the order of these parameters
	// NOTE: unless you also change order of rgdpvpreset array elements!
	public int Preset;

	public int PitchRun;     // pitch shift % when sound is running 0 - 255
	public int PitchStart;       // pitch shift % when sound stops or starts 0 - 255
	public int SpinUp;           // spinup time 0 - 100
	public int SpinDown;     // spindown time 0 - 100

	public int VolRun;           // volume change % when sound is running 0 - 10
	public int VolStart;     // volume change % when sound stops or starts 0 - 10
	public int FadeIn;           // volume fade in time 0 - 100
	public int FadeOut;          // volume fade out time 0 - 100

	// Low Frequency Oscillator
	public int LfoType;      // 0) off 1) square 2) triangle 3) random
	public int LfoRate;      // 0 - 1000, how fast lfo osciallates

	public int LfoModPitch;  // 0-100 mod of current pitch. 0 is off.
	public int LfoModVol;        // 0-100 mod of current volume. 0 is off.

	public int CSpinUp;      // each trigger hit increments counter and spinup pitch


	public int CSpinCount;

	public int Pitch;
	public int SpinUpSav;
	public int SpinDownSav;
	public int PitchFrac;

	public int Vol;
	public int FadeInSav;
	public int FadeOutSav;
	public int VolFrac;

	public int LfoFrac;
	public int LfoMult;

	public DynPitchVol(int preset, int pitchrun, int pitchstart, int spinup, int spindown, int volrun, int volstart, int fadein, int fadeout, int lfotype, int lforate, int lfomodpitch, int lfomodvol, int cspinup) {
		Preset = preset;
		PitchRun = pitchrun;
		PitchStart = pitchstart;
		SpinUp = spinup;
		SpinDown = spindown;
		VolRun = volrun;
		VolStart = volstart;
		FadeIn = fadein;
		FadeOut = fadeout;
		LfoType = lfotype;
		LfoRate = lforate;
		LfoModPitch = lfomodpitch;
		LfoModVol = lfomodvol;
		CSpinUp = cspinup;
	}
}

public static class SoundGlobals
{
	//-----------------------------------------------------------------------------
	// Purpose: Compute a suitable attenuation value given an audible radius
	// Input  : radius -
	//			playEverywhere - (disable attenuation)
	//-----------------------------------------------------------------------------
	public const double REFERENCE_dB = 60.0;

	public const int AMBIENT_GENERIC_UPDATE_RATE = 5; // update at 5hz
	public const float AMBIENT_GENERIC_THINK_DELAY = 1.0f / (float)AMBIENT_GENERIC_UPDATE_RATE;

#if HL1_DLL
	public static readonly ConVar hl1_ref_db_distance = new("hl1_ref_db_distance", "18.0");
	public static float REFERENCE_dB_DISTANCE => hl1_ref_db_distance.GetFloat();
#else
	public const float REFERENCE_dB_DISTANCE = 36.0f;
#endif

	public static SoundLevel ComputeSoundlevel(float radius, bool playEverywhere) {
		SoundLevel soundlevel = SoundLevel.LvlNone;

		if (radius > 0 && !playEverywhere) {
			// attenuation is set to a distance, compute falloff

			float dB_loss = 20 * MathF.Log10(radius / REFERENCE_dB_DISTANCE);

			soundlevel = (SoundLevel)(int)(40 + dB_loss); // sound at 40dB at reference distance
		}

		return soundlevel;
	}

	public const int CDPVPRESETMAX = 27;

	// presets for runtime pitch and vol modulation of ambient sounds

	public static readonly DynPitchVol[] rgdpvpreset = [
		// pitch	pstart	spinup	spindwn	volrun	volstrt	fadein	fadeout	lfotype	lforate	modptch modvol	cspnup
		new(1, 255, 75, 95, 95, 10, 1, 50, 95, 0, 0, 0, 0, 0),
		new(2, 255, 85, 70, 88, 10, 1, 20, 88, 0, 0, 0, 0, 0),
		new(3, 255, 100, 50, 75, 10, 1, 10, 75, 0, 0, 0, 0, 0),
		new(4, 100, 100, 0, 0, 10, 1, 90, 90, 0, 0, 0, 0, 0),
		new(5, 100, 100, 0, 0, 10, 1, 80, 80, 0, 0, 0, 0, 0),
		new(6, 100, 100, 0, 0, 10, 1, 50, 70, 0, 0, 0, 0, 0),
		new(7, 100, 100, 0, 0, 5, 1, 40, 50, 1, 50, 0, 10, 0),
		new(8, 100, 100, 0, 0, 5, 1, 40, 50, 1, 150, 0, 10, 0),
		new(9, 100, 100, 0, 0, 5, 1, 40, 50, 1, 750, 0, 10, 0),
		new(10, 128, 100, 50, 75, 10, 1, 30, 40, 2, 8, 20, 0, 0),
		new(11, 128, 100, 50, 75, 10, 1, 30, 40, 2, 25, 20, 0, 0),
		new(12, 128, 100, 50, 75, 10, 1, 30, 40, 2, 70, 20, 0, 0),
		new(13, 50, 50, 0, 0, 10, 1, 20, 50, 0, 0, 0, 0, 0),
		new(14, 70, 70, 0, 0, 10, 1, 20, 50, 0, 0, 0, 0, 0),
		new(15, 90, 90, 0, 0, 10, 1, 20, 50, 0, 0, 0, 0, 0),
		new(16, 120, 120, 0, 0, 10, 1, 20, 50, 0, 0, 0, 0, 0),
		new(17, 180, 180, 0, 0, 10, 1, 20, 50, 0, 0, 0, 0, 0),
		new(18, 255, 255, 0, 0, 10, 1, 20, 50, 0, 0, 0, 0, 0),
		new(19, 200, 75, 90, 90, 10, 1, 50, 90, 2, 100, 20, 0, 0),
		new(20, 255, 75, 97, 90, 10, 1, 50, 90, 1, 40, 50, 0, 0),
		new(21, 100, 100, 0, 0, 10, 1, 30, 50, 3, 15, 20, 0, 0),
		new(22, 160, 160, 0, 0, 10, 1, 50, 50, 3, 500, 25, 0, 0),
		new(23, 255, 75, 88, 0, 10, 1, 40, 0, 0, 0, 0, 0, 5),
		new(24, 200, 20, 95, 70, 10, 1, 70, 70, 3, 20, 50, 0, 0),
		new(25, 180, 100, 50, 60, 10, 1, 40, 60, 2, 90, 100, 100, 0),
		new(26, 60, 60, 0, 0, 10, 1, 40, 70, 3, 80, 20, 50, 0),
		new(27, 128, 90, 10, 10, 10, 1, 20, 40, 1, 5, 10, 20, 0)
	];

	public const int LFO_SQUARE = 1;
	public const int LFO_TRIANGLE = 2;
	public const int LFO_RANDOM = 3;

	// ==================== SENTENCE GROUPS, UTILITY FUNCTIONS  ======================================

	public static bool fSentencesInit = false;

	// ===================== SENTENCE GROUPS, MAIN ROUTINES ========================

	// given sentence group index, play random sentence for given entity.
	// returns sentenceIndex - which sentence was picked
	// Ipick is only needed if you plan on stopping the sound before playback is done (see SENTENCEG_Stop).
	// sentenceIndex can be used to find the name/length of the sentence

	public static int SENTENCEG_PlayRndI(Edict entity, int isentenceg, float volume, SoundLevel soundlevel, int flags, int pitch) {
		Span<char> name = stackalloc char[64];
		int ipick;

		if (!fSentencesInit)
			return -1;

		name[0] = '\0';

		ipick = engine.SentenceGroupPick(isentenceg, name);
		if ((ipick > 0) && name[0] != '\0') {
			int sentenceIndex = SENTENCEG_Lookup(name.SliceNullTerminatedString());
			PASAttenuationFilter filter = new(BaseEntity.GetContainingEntity(entity)!, soundlevel);
			BaseEntity.EmitSentenceByIndex(filter, entity.EdictIndex, (int)SoundEntityChannel.Voice, sentenceIndex, volume, soundlevel, flags, pitch);
			return sentenceIndex;
		}

		return -1;
	}

	//-----------------------------------------------------------------------------
	// Picks a sentence, but doesn't play it
	//-----------------------------------------------------------------------------
	public static int SENTENCEG_PickRndSz(ReadOnlySpan<char> szgroupname) {
		Span<char> name = stackalloc char[64];
		int ipick;
		int isentenceg;

		if (!fSentencesInit)
			return -1;

		name[0] = '\0';

		isentenceg = engine.SentenceGroupIndexFromName(szgroupname);
		if (isentenceg < 0) {
			Warning($"No such sentence group {szgroupname}\n");
			return -1;
		}

		ipick = engine.SentenceGroupPick(isentenceg, name);
		if (ipick >= 0 && name[0] != '\0')
			return SENTENCEG_Lookup(name.SliceNullTerminatedString());
		return -1;
	}

	//-----------------------------------------------------------------------------
	// Plays a sentence by sentence index
	//-----------------------------------------------------------------------------
	public static void SENTENCEG_PlaySentenceIndex(Edict entity, int iSentenceIndex, float volume, SoundLevel soundlevel, int flags, int pitch) {
		if (iSentenceIndex >= 0) {
			PASAttenuationFilter filter = new(BaseEntity.GetContainingEntity(entity)!, soundlevel);
			BaseEntity.EmitSentenceByIndex(filter, entity.EdictIndex, (int)SoundEntityChannel.Voice, iSentenceIndex, volume, soundlevel, flags, pitch);
		}
	}

	public static int SENTENCEG_PlayRndSz(Edict entity, ReadOnlySpan<char> szgroupname, float volume, SoundLevel soundlevel, int flags, int pitch) {
		Span<char> name = stackalloc char[64];
		int ipick;
		int isentenceg;

		if (!fSentencesInit)
			return -1;

		name[0] = '\0';

		isentenceg = engine.SentenceGroupIndexFromName(szgroupname);
		if (isentenceg < 0) {
			Warning($"No such sentence group {szgroupname}\n");
			return -1;
		}

		ipick = engine.SentenceGroupPick(isentenceg, name);
		if (ipick >= 0 && name[0] != '\0') {
			int sentenceIndex = SENTENCEG_Lookup(name.SliceNullTerminatedString());
			PASAttenuationFilter filter = new(BaseEntity.GetContainingEntity(entity)!, soundlevel);
			BaseEntity.EmitSentenceByIndex(filter, entity.EdictIndex, (int)SoundEntityChannel.Voice, sentenceIndex, volume, soundlevel, flags, pitch);
			return sentenceIndex;
		}

		return -1;
	}

	// play sentences in sequential order from sentence group.  Reset after last sentence.

	public static int SENTENCEG_PlaySequentialSz(Edict entity, ReadOnlySpan<char> szgroupname, float volume, SoundLevel soundlevel, int flags, int pitch, int ipick, int freset) {
		Span<char> name = stackalloc char[64];
		int ipicknext;
		int isentenceg;

		if (!fSentencesInit)
			return -1;

		name[0] = '\0';

		isentenceg = engine.SentenceGroupIndexFromName(szgroupname);
		if (isentenceg < 0)
			return -1;

		ipicknext = engine.SentenceGroupPickSequential(isentenceg, name, ipick, freset);
		if (ipicknext >= 0 && name[0] != '\0') {
			int sentenceIndex = SENTENCEG_Lookup(name.SliceNullTerminatedString());
			PASAttenuationFilter filter = new(BaseEntity.GetContainingEntity(entity)!, soundlevel);
			BaseEntity.EmitSentenceByIndex(filter, entity.EdictIndex, (int)SoundEntityChannel.Voice, sentenceIndex, volume, soundlevel, flags, pitch);
			return sentenceIndex;
		}

		return -1;
	}

	// open sentences.txt, scan for groups, build rgsentenceg
	// Should be called from world spawn, only works on the
	// first call and is ignored subsequently.
	public static void SENTENCEG_Init() {
		if (fSentencesInit)
			return;

		engine.PrecacheSentenceFile("scripts/sentences.txt");
		fSentencesInit = true;
	}

	// convert sentence (sample) name to !sentencenum, return !sentencenum

	public static int SENTENCEG_Lookup(ReadOnlySpan<char> sample) {
		return engine.SentenceIndexFromName(sample[1..]);
	}

	public static int SENTENCEG_GetIndex(ReadOnlySpan<char> szrootname) {
		return engine.SentenceGroupIndexFromName(szrootname);
	}

	public static void UTIL_RestartAmbientSounds() {
		AmbientGeneric? ambient = null;
		while ((ambient = (AmbientGeneric?)gEntList.FindEntityByClassname(ambient, "ambient_generic")) != null) {
			if (ambient.Active) {
				if (ambient.SoundFile.Contains("mp3"))
					ambient.SendSound(SoundFlags.ChangeVolume); // fake a change, so we don't create 2 sounds
				ambient.SendSound(SoundFlags.ChangeVolume); // fake a change, so we don't create 2 sounds
			}
		}
	}
}

[LinkEntityToClass("ambient_generic")]
public class AmbientGeneric : PointEntity
{
	[Flags]
	public enum SF
	{
		Everywhere = 1,
		StartSilent = 16,
		NotLooping = 32,
	}

	public float Radius;
	public float MaxRadius;
	public SoundLevel SoundLevel;     // dB value
	public DynPitchVol Dpv;

	public bool Active;     // only true when the entity is playing a looping sound
	public bool Looping;        // true when the sound played will loop

	public string SoundFile = "";            // Path/filename of WAV or MP3 file to play.
	public string? SourceEntName;
	public EHANDLE SoundSource = new();    // entity from which the sound comes
	public int SoundSourceEntIndex; // In case the entity goes away before we finish stopping the sound...

	string? Sound;
	string? PrevSound; // track if the sound has changed and we need to re-Validate

	public static readonly new DataMap DataDesc = new(typeof(AmbientGeneric), PointEntity.DataDesc, [
		DEFINE<AmbientGeneric>.KEYFIELD(nameof(Sound), FieldType.SoundName, "message"),
		DEFINE<AmbientGeneric>.KEYFIELD(nameof(Radius), FieldType.Float, "radius"),
		DEFINE<AmbientGeneric>.KEYFIELD(nameof(SourceEntName), FieldType.String, "SourceEntityName"),
		// recomputed in Activate()
		// DEFINE_FIELD( m_hSoundSource, EHANDLE ),
		// DEFINE_FIELD( m_nSoundSourceEntIndex, FIELD_INTERGER ),

		DEFINE<AmbientGeneric>.FIELD(nameof(MaxRadius), FieldType.Float),
		DEFINE<AmbientGeneric>.FIELD(nameof(Active), FieldType.Boolean),
		DEFINE<AmbientGeneric>.FIELD(nameof(Looping), FieldType.Boolean),
		DEFINE<AmbientGeneric>.FIELD(nameof(SoundLevel), FieldType.Integer),

		// Function Pointers
		DEFINE<AmbientGeneric>.FUNCTION(nameof(RampThink)),

		// Inputs
		DEFINE<AmbientGeneric>.INPUTFUNC(FieldType.Void, "PlaySound", nameof(InputPlaySound), (INPUTFUNCPTR)((self, data) => ((AmbientGeneric)self).InputPlaySound(data))),
		DEFINE<AmbientGeneric>.INPUTFUNC(FieldType.Void, "StopSound", nameof(InputStopSound), (INPUTFUNCPTR)((self, data) => ((AmbientGeneric)self).InputStopSound(data))),
		DEFINE<AmbientGeneric>.INPUTFUNC(FieldType.Void, "ToggleSound", nameof(InputToggleSound), (INPUTFUNCPTR)((self, data) => ((AmbientGeneric)self).InputToggleSound(data))),
		DEFINE<AmbientGeneric>.INPUTFUNC(FieldType.Float, "Pitch", nameof(InputPitch), (INPUTFUNCPTR)((self, data) => ((AmbientGeneric)self).InputPitch(data))),
		DEFINE<AmbientGeneric>.INPUTFUNC(FieldType.Float, "Volume", nameof(InputVolume), (INPUTFUNCPTR)((self, data) => ((AmbientGeneric)self).InputVolume(data))),
		DEFINE<AmbientGeneric>.INPUTFUNC(FieldType.Float, "FadeIn", nameof(InputFadeIn), (INPUTFUNCPTR)((self, data) => ((AmbientGeneric)self).InputFadeIn(data))),
		DEFINE<AmbientGeneric>.INPUTFUNC(FieldType.Float, "FadeOut", nameof(InputFadeOut), (INPUTFUNCPTR)((self, data) => ((AmbientGeneric)self).InputFadeOut(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	//-----------------------------------------------------------------------------
	//
	//-----------------------------------------------------------------------------
	public AmbientGeneric() {
		SoundFile = "";
		Sound = null;
		PrevSound = null;
	}

	//-----------------------------------------------------------------------------
	// Spawn
	//-----------------------------------------------------------------------------
	public override void Spawn() {
		SoundLevel = ComputeSoundlevel(Radius, HasSpawnFlags(SF.Everywhere));
		ComputeMaxAudibleDistance();

		ValidateSoundFile();

		if (SoundFile.Length < 1) {
			Warning($"Empty {GetClassname()} ({GetDebugName()}) at {GetAbsOrigin().X:F2}, {GetAbsOrigin().Y:F2}, {GetAbsOrigin().Z:F2}\n");
			Util.Remove(this);
			return;
		}

		SetSolid(Source.SolidType.None);
		SetMoveType(Source.MoveType.None);

		// Set up think function for dynamic modification
		// of ambient sound's pitch or volume. Don't
		// start thinking yet.

		SetThink(RampThink);
		SetNextThink(TICK_NEVER_THINK);

		Active = false;

		if (HasSpawnFlags(SF.NotLooping))
			Looping = false;
		else
			Looping = true;

		SoundSource.Set(null);
		SoundSourceEntIndex = -1;

		Precache();

		// init all dynamic modulation parms
		InitModulationParms();
	}

	//-----------------------------------------------------------------------------
	// Computes the max audible radius for a given sound level
	//-----------------------------------------------------------------------------
	const float MIN_AUDIBLE_VOLUME = 1.01e-3f;

	void ComputeMaxAudibleDistance() {
		if ((SoundLevel == SoundLevel.LvlNone) || (Radius == 0.0f)) {
			MaxRadius = -1.0f;
			return;
		}

		// Sadly, there's no direct way of getting at this.
		// We have to do an interative computation.
		float gain = enginesound.GetDistGainFromSoundLevel(SoundLevel, Radius);
		if (gain <= MIN_AUDIBLE_VOLUME) {
			MaxRadius = Radius;
			return;
		}

		float minRadius = Radius;
		float maxRadius = Radius * 2;
		while (true) {
			// First, find a min + max range surrounding the desired distance gain
			gain = enginesound.GetDistGainFromSoundLevel(SoundLevel, maxRadius);
			if (gain <= MIN_AUDIBLE_VOLUME)
				break;

			// Always audible.
			if (maxRadius > 1e5) {
				MaxRadius = -1.0f;
				return;
			}

			minRadius = maxRadius;
			maxRadius *= 2.0f;
		}

		// Now home in a little bit
		int interations = 4;
		while (--interations >= 0) {
			float testRadius = (minRadius + maxRadius) * 0.5f;
			gain = enginesound.GetDistGainFromSoundLevel(SoundLevel, testRadius);
			if (gain <= MIN_AUDIBLE_VOLUME)
				maxRadius = testRadius;
			else
				minRadius = testRadius;
		}

		MaxRadius = maxRadius;
	}

	//-----------------------------------------------------------------------------
	// Purpose: Input handler for changing pitch.
	// Input  : Float new pitch from 0 - 255 (100 = as recorded).
	//-----------------------------------------------------------------------------
	public void InputPitch(InputData inputdata) {
		Dpv.Pitch = Math.Clamp(MathLib.FastFloatToSmallInt(inputdata.Value.Float()), 0, 255);

		SendSound(SoundFlags.ChangePitch);
	}

	//-----------------------------------------------------------------------------
	// Purpose: Input handler for changing volume.
	// Input  : Float new volume, from 0 - 10.
	//-----------------------------------------------------------------------------
	public void InputVolume(InputData inputdata) {
		//
		// Multiply the input value by ten since volumes are expected to be from 0 - 100.
		//
		Dpv.Vol = Math.Clamp((int)MathF.Round(inputdata.Value.Float() * 10.0f), 0, 100);
		Dpv.VolFrac = Dpv.Vol << 8;

		SendSound(SoundFlags.ChangeVolume);
	}

	//-----------------------------------------------------------------------------
	// Purpose: Input handler for fading in volume over time.
	// Input  : Float volume fade in time 0 - 100 seconds
	//-----------------------------------------------------------------------------
	public void InputFadeIn(InputData inputdata) {
		// cancel any fade out that might be happening
		Dpv.FadeOut = 0;

		Dpv.FadeIn = (int)inputdata.Value.Float();
		if (Dpv.FadeIn > 100) Dpv.FadeIn = 100;
		if (Dpv.FadeIn < 0) Dpv.FadeIn = 0;

		if (Dpv.FadeIn > 0)
			Dpv.FadeIn = (100 << 8) / (Dpv.FadeIn * AMBIENT_GENERIC_UPDATE_RATE);

		SetNextThink(gpGlobals.CurTime + 0.1f);
	}

	//-----------------------------------------------------------------------------
	// Purpose: Input handler for fading out volume over time.
	// Input  : Float volume fade out time 0 - 100 seconds
	//-----------------------------------------------------------------------------
	public void InputFadeOut(InputData inputdata) {
		// cancel any fade in that might be happening
		Dpv.FadeIn = 0;

		Dpv.FadeOut = (int)inputdata.Value.Float();

		if (Dpv.FadeOut > 100) Dpv.FadeOut = 100;
		if (Dpv.FadeOut < 0) Dpv.FadeOut = 0;

		if (Dpv.FadeOut > 0)
			Dpv.FadeOut = (100 << 8) / (Dpv.FadeOut * AMBIENT_GENERIC_UPDATE_RATE);

		SetNextThink(gpGlobals.CurTime + 0.1f);
	}

	void ValidateSoundFile() {
		if (Sound == null) {
			PrevSound = null;
			SoundFile = "";
			return;
		}

		if (ReferenceEquals(Sound, PrevSound) || Sound == PrevSound)
			return;

		PrevSound = Sound;

		string soundFile = Sound;
		if (soundFile.Length < 1)
			return;

		// try to fix some legacy ambient generic entities that still use .wav file references for vo sounds instead of .mp3
		if (soundFile.StartsWith("vo", StringComparison.Ordinal)) {
			string ext = Path.GetExtension(soundFile).TrimStart('.');
			if (!string.IsNullOrEmpty(ext) && FStrEq(ext, "wav")) {
				string pathTest = $"sound/{soundFile}";
				if (!filesystem.FileExists(pathTest)) {
					string newSoundFile = Path.ChangeExtension(soundFile, ".mp3");
					pathTest = $"sound/{newSoundFile}";
					if (filesystem.FileExists(pathTest))
						soundFile = newSoundFile;
				}
			}
		}

		SoundFile = soundFile;
	}

	public override void Precache() {
		if (SoundFile.Length > 1) {
			if (SoundFile[0] != '!')
				PrecacheScriptSound(SoundFile);
		}

		if (!HasSpawnFlags(SF.StartSilent)) {
			// start the sound ASAP
			if (Looping)
				Active = true;
		}
	}

	//------------------------------------------------------------------------------
	// Purpose:
	//------------------------------------------------------------------------------
	public override void Activate() {
		base.Activate();

		// Initialize sound source.  If no source was given, or source can't be found
		// then this is the source
		if (SoundSource.Get() == null) {
			if (SourceEntName != null) {
				BaseEntity? source = gEntList.FindEntityByName(null, SourceEntName);
				SoundSource.Set(source);
				if (source != null)
					SoundSourceEntIndex = source.EntIndex();
			}

			if (SoundSource.Get() == null) {
				SoundSource.Set(this);
				SoundSourceEntIndex = EntIndex();
			}
			else {
				if (!HasSpawnFlags(SF.Everywhere))
					AddEFlags(EFL.ForceCheckTransmit);
			}
		}

		// If active start the sound
		if (Active) {
			SoundFlags flags = SoundFlags.Spawning;
			// If we are loading a saved game, we can't write into the init/signon buffer here, so just issue
			//  as a regular sound message...
			if (gpGlobals.LoadType == MapLoadType.Transition ||
				 gpGlobals.LoadType == MapLoadType.LoadGame ||
				 g_pGameRules.InRoundRestart()) {
				flags = SoundFlags.NoFlags;
			}

			// Tracker 76119:  8/12/07 ywb:
			//  Make sure pitch and volume are set up to the correct value (especially after restoring a .sav file)
			flags |= (SoundFlags.ChangePitch | SoundFlags.ChangeVolume);

			// Don't bother sending over to client if volume is zero, though
			if (Dpv.Vol > 0)
				SendSound(flags);

			SetNextThink(gpGlobals.CurTime + 0.1f);
		}
	}

	//-----------------------------------------------------------------------------
	// Rules about which entities need to transmit along with me
	//-----------------------------------------------------------------------------
	public override void SetTransmit(CheckTransmitInfo info, bool always) {
		// Ambient generics never transmit; this is just a way for us to ensure
		// the sound source gets transmitted; that's why we don't call pInfo->m_pTransmitEdict->Set
		BaseEntity? soundSource = (BaseEntity?)SoundSource.Get();
		if (soundSource == null || soundSource == this || !Active)
			return;

		// Don't bother sending the position of the source if we have to play everywhere
		if (HasSpawnFlags(SF.Everywhere))
			return;

		Assert(info.ClientEnt != null);
		BaseEntity? client = BaseEntity.GetContainingEntity(info.ClientEnt!);
		if (client == null)
			return;

		// Send the sound source if he's close enough
		if ((MaxRadius < 0) || (Vector3.DistanceSquared(client.GetAbsOrigin(), soundSource.GetAbsOrigin()) <= MaxRadius * MaxRadius))
			soundSource.SetTransmit(info, false);
	}

	//-----------------------------------------------------------------------------
	// Purpose:
	//-----------------------------------------------------------------------------
	public override void UpdateOnRemove() {
		if (Active) {
			// Stop the sound we're generating
			SendSound(SoundFlags.Stop);
		}

		base.UpdateOnRemove();
	}

	//-----------------------------------------------------------------------------
	// Purpose: Think at 5hz if we are dynamically modifying pitch or volume of the
	//			playing sound.  This function will ramp pitch and/or volume up or
	//			down, modify pitch/volume with lfo if active.
	//-----------------------------------------------------------------------------
	void RampThink() {
		int pitch = Dpv.Pitch;
		int vol = Dpv.Vol;
		SoundFlags flags = 0;
		bool changed = false;       // false if pitch and vol remain unchanged this round
		int prev;

		if (Dpv.SpinUp == 0 && Dpv.SpinDown == 0 && Dpv.FadeIn == 0 && Dpv.FadeOut == 0 && Dpv.LfoType == 0)
			return;                     // no ramps or lfo, stop thinking

		// ==============
		// pitch envelope
		// ==============
		if (Dpv.SpinUp != 0 || Dpv.SpinDown != 0) {
			prev = Dpv.PitchFrac >> 8;

			if (Dpv.SpinUp > 0)
				Dpv.PitchFrac += Dpv.SpinUp;
			else if (Dpv.SpinDown > 0)
				Dpv.PitchFrac -= Dpv.SpinDown;

			pitch = Dpv.PitchFrac >> 8;

			if (pitch > Dpv.PitchRun) {
				pitch = Dpv.PitchRun;
				Dpv.SpinUp = 0;              // done with ramp up
			}

			if (pitch < Dpv.PitchStart) {
				pitch = Dpv.PitchStart;
				Dpv.SpinDown = 0;                // done with ramp down

				// shut sound off
				SendSound(SoundFlags.Stop);

				// return without setting m_flNextThink
				return;
			}

			if (pitch > 255) pitch = 255;
			if (pitch < 1) pitch = 1;

			Dpv.Pitch = pitch;

			changed |= (prev != pitch);
			flags |= SoundFlags.ChangePitch;
		}

		// ==================
		// amplitude envelope
		// ==================
		if (Dpv.FadeIn != 0 || Dpv.FadeOut != 0) {
			prev = Dpv.VolFrac >> 8;

			if (Dpv.FadeIn > 0)
				Dpv.VolFrac += Dpv.FadeIn;
			else if (Dpv.FadeOut > 0)
				Dpv.VolFrac -= Dpv.FadeOut;

			vol = Dpv.VolFrac >> 8;

			if (vol > Dpv.VolRun) {
				vol = Dpv.VolRun;
				Dpv.VolFrac = vol << 8;
				Dpv.FadeIn = 0;              // done with ramp up
			}

			if (vol < Dpv.VolStart) {
				vol = Dpv.VolStart;
				Dpv.Vol = vol;
				Dpv.VolFrac = vol << 8;
				Dpv.FadeOut = 0;             // done with ramp down

				// shut sound off
				SendSound(SoundFlags.Stop);

				// return without setting m_flNextThink
				return;
			}

			if (vol > 100) {
				vol = 100;
				Dpv.VolFrac = vol << 8;
			}
			if (vol < 1) {
				vol = 1;
				Dpv.VolFrac = vol << 8;
			}

			Dpv.Vol = vol;

			changed |= (prev != vol);
			flags |= SoundFlags.ChangeVolume;
		}

		// ===================
		// pitch/amplitude LFO
		// ===================
		if (Dpv.LfoType != 0) {
			int pos;

			if (Dpv.LfoFrac > 0x6fffffff)
				Dpv.LfoFrac = 0;

			// update lfo, lfofrac/255 makes a triangle wave 0-255
			Dpv.LfoFrac += Dpv.LfoRate;
			pos = Dpv.LfoFrac >> 8;

			if (Dpv.LfoFrac < 0) {
				Dpv.LfoFrac = 0;
				Dpv.LfoRate = Math.Abs(Dpv.LfoRate);
				pos = 0;
			}
			else if (pos > 255) {
				pos = 255;
				Dpv.LfoFrac = (255 << 8);
				Dpv.LfoRate = -Math.Abs(Dpv.LfoRate);
			}

			switch (Dpv.LfoType) {
				case LFO_SQUARE:
					if (pos < 128)
						Dpv.LfoMult = 255;
					else
						Dpv.LfoMult = 0;

					break;
				case LFO_RANDOM:
					if (pos == 255)
						Dpv.LfoMult = random.RandomInt(0, 255);
					break;
				case LFO_TRIANGLE:
				default:
					Dpv.LfoMult = pos;
					break;
			}

			if (Dpv.LfoModPitch != 0) {
				prev = pitch;

				// pitch 0-255
				pitch += ((Dpv.LfoMult - 128) * Dpv.LfoModPitch) / 100;

				if (pitch > 255) pitch = 255;
				if (pitch < 1) pitch = 1;


				changed |= (prev != pitch);
				flags |= SoundFlags.ChangePitch;
			}

			if (Dpv.LfoModVol != 0) {
				// vol 0-100
				prev = vol;

				vol += ((Dpv.LfoMult - 128) * Dpv.LfoModVol) / 100;

				if (vol > 100) vol = 100;
				if (vol < 0) vol = 0;

				changed |= (prev != vol);
				flags |= SoundFlags.ChangeVolume;
			}

		}

		// Send update to playing sound only if we actually changed
		// pitch or volume in this routine.

		if (flags != 0 && changed) {
			if (pitch == PITCH_NORM)
				pitch = PITCH_NORM + 1; // don't send 'no pitch' !

			BaseEntity? soundSource = (BaseEntity?)SoundSource.Get();
			if (soundSource != null) {
				ValidateSoundFile();

				Util.EmitAmbientSound(soundSource.GetSoundSourceIndex(), soundSource.GetAbsOrigin(),
					SoundFile, (vol * 0.01f), SoundLevel, (int)flags, pitch);
			}
		}

		// update ramps at 5hz
		SetNextThink(gpGlobals.CurTime + AMBIENT_GENERIC_THINK_DELAY);
		return;
	}

	//-----------------------------------------------------------------------------
	// Purpose: Init all ramp params in preparation to play a new sound.
	//-----------------------------------------------------------------------------
	void InitModulationParms() {
		int pitchinc;

		Dpv.VolRun = Health * 10;    // 0 - 100
		if (Dpv.VolRun > 100) Dpv.VolRun = 100;
		if (Dpv.VolRun < 0) Dpv.VolRun = 0;

		// get presets
		if (Dpv.Preset != 0 && Dpv.Preset <= CDPVPRESETMAX) {
			// load preset values
			Dpv = rgdpvpreset[Dpv.Preset - 1];

			// fixup preset values, just like
			// fixups in KeyValue routine.
			if (Dpv.SpinDown > 0)
				Dpv.SpinDown = (101 - Dpv.SpinDown) * 64;
			if (Dpv.SpinUp > 0)
				Dpv.SpinUp = (101 - Dpv.SpinUp) * 64;

			Dpv.VolStart *= 10;
			Dpv.VolRun *= 10;

			if (Dpv.FadeIn > 0)
				Dpv.FadeIn = (101 - Dpv.FadeIn) * 64;
			if (Dpv.FadeOut > 0)
				Dpv.FadeOut = (101 - Dpv.FadeOut) * 64;

			Dpv.LfoRate *= 256;

			Dpv.FadeInSav = Dpv.FadeIn;
			Dpv.FadeOutSav = Dpv.FadeOut;
			Dpv.SpinUpSav = Dpv.SpinUp;
			Dpv.SpinDownSav = Dpv.SpinDown;
		}

		Dpv.FadeIn = Dpv.FadeInSav;
		Dpv.FadeOut = 0;

		if (Dpv.FadeIn != 0)
			Dpv.Vol = Dpv.VolStart;
		else
			Dpv.Vol = Dpv.VolRun;

		Dpv.SpinUp = Dpv.SpinUpSav;
		Dpv.SpinDown = 0;

		if (Dpv.SpinUp != 0)
			Dpv.Pitch = Dpv.PitchStart;
		else
			Dpv.Pitch = Dpv.PitchRun;

		if (Dpv.Pitch == 0)
			Dpv.Pitch = PITCH_NORM;

		Dpv.PitchFrac = Dpv.Pitch << 8;
		Dpv.VolFrac = Dpv.Vol << 8;

		Dpv.LfoFrac = 0;
		Dpv.LfoRate = Math.Abs(Dpv.LfoRate);

		Dpv.CSpinCount = 1;

		if (Dpv.CSpinUp != 0) {
			pitchinc = (255 - Dpv.PitchStart) / Dpv.CSpinUp;

			Dpv.PitchRun = Dpv.PitchStart + pitchinc;
			if (Dpv.PitchRun > 255) Dpv.PitchRun = 255;
		}

		if ((Dpv.SpinUpSav != 0 || Dpv.SpinDownSav != 0 || (Dpv.LfoType != 0 && Dpv.LfoModPitch != 0))
			&& (Dpv.Pitch == PITCH_NORM))
			Dpv.Pitch = PITCH_NORM + 1; // must never send 'no pitch' as first pitch
										 // if we intend to pitch shift later!
	}

	//-----------------------------------------------------------------------------
	// Purpose: Input handler that begins playing the sound.
	//-----------------------------------------------------------------------------
	public void InputPlaySound(InputData inputdata) {
		if (!Active) {
			//Adrian: Stop our current sound before starting a new one!
			SendSound(SoundFlags.Stop);

			ToggleSound();
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose: Input handler that stops playing the sound.
	//-----------------------------------------------------------------------------
	public void InputStopSound(InputData inputdata) {
		if (Active)
			ToggleSound();
	}

	public void SendSound(SoundFlags flags) {
		ValidateSoundFile();

		BaseEntity? soundSource = (BaseEntity?)SoundSource.Get();
		if (soundSource != null) {
			if (flags == SoundFlags.Stop) {
				Util.EmitAmbientSound(soundSource.GetSoundSourceIndex(), soundSource.GetAbsOrigin(), SoundFile,
							0, SoundLevel.LvlNone, (int)flags, 0);
			}
			else {
				Util.EmitAmbientSound(soundSource.GetSoundSourceIndex(), soundSource.GetAbsOrigin(), SoundFile,
					(Dpv.Vol * 0.01f), SoundLevel, (int)flags, Dpv.Pitch);
			}
		}
		else {
			if ((flags == SoundFlags.Stop) &&
				(SoundSourceEntIndex != -1)) {
				Util.EmitAmbientSound(SoundSourceEntIndex, GetAbsOrigin(), SoundFile,
						0, SoundLevel.LvlNone, (int)flags, 0);
			}
		}
	}

	//-----------------------------------------------------------------------------
	// Purpose: Input handler that stops playing the sound.
	//-----------------------------------------------------------------------------
	public void InputToggleSound(InputData inputdata) {
		ToggleSound();
	}

	//-----------------------------------------------------------------------------
	// Purpose: Turns an ambient sound on or off.  If the ambient is a looping sound,
	//			mark sound as active (m_fActive) if it's playing, innactive if not.
	//			If the sound is not a looping sound, never mark it as active.
	// Input  : pActivator -
	//			pCaller -
	//			useType -
	//			value -
	//-----------------------------------------------------------------------------
	void ToggleSound() {
		// m_fActive is true only if a looping sound is playing.

		if (Active) {// turn sound off

			if (Dpv.CSpinUp != 0) {
				// Don't actually shut off. Each toggle causes
				// incremental spinup to max pitch

				if (Dpv.CSpinCount <= Dpv.CSpinUp) {
					int pitchinc;

					// start a new spinup
					Dpv.CSpinCount++;

					pitchinc = (255 - Dpv.PitchStart) / Dpv.CSpinUp;

					Dpv.SpinUp = Dpv.SpinUpSav;
					Dpv.SpinDown = 0;

					Dpv.PitchRun = Dpv.PitchStart + pitchinc * Dpv.CSpinCount;
					if (Dpv.PitchRun > 255) Dpv.PitchRun = 255;

					SetNextThink(gpGlobals.CurTime + 0.1f);
				}

			}
			else {
				Active = false;

				// HACKHACK - this makes the code in Precache() work properly after a save/restore
				AddSpawnFlags(SF.StartSilent);

				if (Dpv.SpinDownSav != 0 || Dpv.FadeOutSav != 0) {
					// spin it down (or fade it) before shutoff if spindown is set
					Dpv.SpinDown = Dpv.SpinDownSav;
					Dpv.SpinUp = 0;

					Dpv.FadeOut = Dpv.FadeOutSav;
					Dpv.FadeIn = 0;
					SetNextThink(gpGlobals.CurTime + 0.1f);
				}
				else
					SendSound(SoundFlags.Stop); // stop sound
			}
		}
		else {// turn sound on

			// only toggle if this is a looping sound.  If not looping, each
			// trigger will cause the sound to play.  If the sound is still
			// playing from a previous trigger press, it will be shut off
			// and then restarted.

			if (Looping)
				Active = true;
			else {
				// shut sound off now - may be interrupting a long non-looping sound
				SendSound(SoundFlags.Stop); // stop sound
			}

			// init all ramp params for startup

			InitModulationParms();

			SendSound(SoundFlags.NoFlags); // send sound

			SetNextThink(gpGlobals.CurTime + 0.1f);

		}
	}

	// KeyValue - load keyvalue pairs into member data of the
	// ambient generic. NOTE: called BEFORE spawn!
	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		// NOTE: changing any of the modifiers in this code
		// NOTE: also requires changing InitModulationParms code.

		// preset
		if (FStrEq(keyName, "preset"))
			Dpv.Preset = atoi(value);
		// pitchrun
		else if (FStrEq(keyName, "pitch")) {
			Dpv.PitchRun = atoi(value);

			if (Dpv.PitchRun > 255) Dpv.PitchRun = 255;
			if (Dpv.PitchRun < 0) Dpv.PitchRun = 0;
		}
		// pitchstart
		else if (FStrEq(keyName, "pitchstart")) {
			Dpv.PitchStart = atoi(value);

			if (Dpv.PitchStart > 255) Dpv.PitchStart = 255;
			if (Dpv.PitchStart < 0) Dpv.PitchStart = 0;
		}
		// spinup
		else if (FStrEq(keyName, "spinup")) {
			Dpv.SpinUp = atoi(value);

			if (Dpv.SpinUp > 100) Dpv.SpinUp = 100;
			if (Dpv.SpinUp < 0) Dpv.SpinUp = 0;

			if (Dpv.SpinUp > 0)
				Dpv.SpinUp = (101 - Dpv.SpinUp) * 64;
			Dpv.SpinUpSav = Dpv.SpinUp;
		}
		// spindown
		else if (FStrEq(keyName, "spindown")) {
			Dpv.SpinDown = atoi(value);

			if (Dpv.SpinDown > 100) Dpv.SpinDown = 100;
			if (Dpv.SpinDown < 0) Dpv.SpinDown = 0;

			if (Dpv.SpinDown > 0)
				Dpv.SpinDown = (101 - Dpv.SpinDown) * 64;
			Dpv.SpinDownSav = Dpv.SpinDown;
		}
		// volstart
		else if (FStrEq(keyName, "volstart")) {
			Dpv.VolStart = atoi(value);

			if (Dpv.VolStart > 10) Dpv.VolStart = 10;
			if (Dpv.VolStart < 0) Dpv.VolStart = 0;

			Dpv.VolStart *= 10;  // 0 - 100
		}
		// legacy fadein
		else if (FStrEq(keyName, "fadein")) {
			Dpv.FadeIn = atoi(value);

			if (Dpv.FadeIn > 100) Dpv.FadeIn = 100;
			if (Dpv.FadeIn < 0) Dpv.FadeIn = 0;

			if (Dpv.FadeIn > 0)
				Dpv.FadeIn = (101 - Dpv.FadeIn) * 64;
			Dpv.FadeInSav = Dpv.FadeIn;
		}
		// legacy fadeout
		else if (FStrEq(keyName, "fadeout")) {
			Dpv.FadeOut = atoi(value);

			if (Dpv.FadeOut > 100) Dpv.FadeOut = 100;
			if (Dpv.FadeOut < 0) Dpv.FadeOut = 0;

			if (Dpv.FadeOut > 0)
				Dpv.FadeOut = (101 - Dpv.FadeOut) * 64;
			Dpv.FadeOutSav = Dpv.FadeOut;
		}
		// fadeinsecs
		else if (FStrEq(keyName, "fadeinsecs")) {
			Dpv.FadeIn = atoi(value);

			if (Dpv.FadeIn > 100) Dpv.FadeIn = 100;
			if (Dpv.FadeIn < 0) Dpv.FadeIn = 0;

			if (Dpv.FadeIn > 0)
				Dpv.FadeIn = (100 << 8) / (Dpv.FadeIn * AMBIENT_GENERIC_UPDATE_RATE);
			Dpv.FadeInSav = Dpv.FadeIn;
		}
		// fadeoutsecs
		else if (FStrEq(keyName, "fadeoutsecs")) {
			Dpv.FadeOut = atoi(value);

			if (Dpv.FadeOut > 100) Dpv.FadeOut = 100;
			if (Dpv.FadeOut < 0) Dpv.FadeOut = 0;

			if (Dpv.FadeOut > 0)
				Dpv.FadeOut = (100 << 8) / (Dpv.FadeOut * AMBIENT_GENERIC_UPDATE_RATE);
			Dpv.FadeOutSav = Dpv.FadeOut;
		}
		// lfotype
		else if (FStrEq(keyName, "lfotype")) {
			Dpv.LfoType = atoi(value);
			if (Dpv.LfoType > 4) Dpv.LfoType = LFO_TRIANGLE;
		}
		// lforate
		else if (FStrEq(keyName, "lforate")) {
			Dpv.LfoRate = atoi(value);

			if (Dpv.LfoRate > 1000) Dpv.LfoRate = 1000;
			if (Dpv.LfoRate < 0) Dpv.LfoRate = 0;

			Dpv.LfoRate *= 256;
		}
		// lfomodpitch
		else if (FStrEq(keyName, "lfomodpitch")) {
			Dpv.LfoModPitch = atoi(value);
			if (Dpv.LfoModPitch > 100) Dpv.LfoModPitch = 100;
			if (Dpv.LfoModPitch < 0) Dpv.LfoModPitch = 0;
		}

		// lfomodvol
		else if (FStrEq(keyName, "lfomodvol")) {
			Dpv.LfoModVol = atoi(value);
			if (Dpv.LfoModVol > 100) Dpv.LfoModVol = 100;
			if (Dpv.LfoModVol < 0) Dpv.LfoModVol = 0;
		}
		// cspinup
		else if (FStrEq(keyName, "cspinup")) {
			Dpv.CSpinUp = atoi(value);
			if (Dpv.CSpinUp > 100) Dpv.CSpinUp = 100;
			if (Dpv.CSpinUp < 0) Dpv.CSpinUp = 0;
		}
		else
			return base.KeyValue(keyName, value);

		return true;
	}
}
