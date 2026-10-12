using SharpCompress.Common;

using Source.Common.Audio;
using Source.Common.SoundEmitterSystem;
using Source.Common.Utilities;

using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using System.Xml.Linq;

using PitchInterval = Source.Common.SoundEmitterSystem.SoundInterval<System.Byte>;
using SoundLevelInterval = Source.Common.SoundEmitterSystem.SoundInterval<System.UInt16>;
using VolumeInterval = Source.Common.SoundEmitterSystem.SoundInterval<System.Half>;

namespace Source.Common.SoundEmitterSystem;

public static class SoundEmitterSystemGlobals
{
	public const HSOUNDSCRIPTHANDLE SOUNDEMITTER_INVALID_HANDLE = unchecked((HSOUNDSCRIPTHANDLE)(-1));
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SoundFile
{
	public SoundFile() {
		Symbol = new(UTL_INVAL_SYMBOL);
		Gender = Gender.None;
		Available = true;
	}
	public UtlSymbol Symbol;
	public Gender Gender;
	public bool Available;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SoundInterval<T> where T : INumber<T>
{
	public T Start;
	public T Range;
	public readonly ref Interval ToInterval(ref Interval dest) {
		dest.Start = float.CreateTruncating(Start);
		dest.Range = float.CreateTruncating(Range);
		return ref dest;
	}
	public void FromInterval(in Interval from) {
		Start = T.CreateTruncating(from.Start);
		Range = T.CreateTruncating(from.Range);
	}
	public readonly float Random() => RandomFloat(float.CreateTruncating(Start), float.CreateTruncating(Start) + float.CreateTruncating(Range));
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SoundParametersInternal : IEquatable<SoundParametersInternal>
{
	public SoundParametersInternal() {

	}

	public void CopyFrom(in SoundParametersInternal src) {
		if (SoundNamesCount > 1)
			SoundNames = null;
		if (ConvertedNamesCount > 1)
			ConvertedNames = null;

		Channel = src.Channel;
		Volume = src.Volume;
		Pitch = src.Pitch;
		SoundLevel = src.SoundLevel;
		DelayMsec = src.DelayMsec;
		bPlayToOwnerOnly = src.bPlayToOwnerOnly;

		SoundNamesCount = src.SoundNamesCount;
		if (SoundNamesCount != 0) {
			if (SoundNamesCount > 1) {
				SoundNames = new SoundFile[SoundNamesCount];
				memcpy(SoundNames, src.SoundNames, SoundNamesCount);
			}
			else {
				SoundNames = src.SoundNames;
			}
		}
		else
			SoundNames = null;

		ConvertedNamesCount = src.ConvertedNamesCount;
		if (ConvertedNamesCount != 0) {
			if (ConvertedNamesCount > 1) {
				ConvertedNames = new SoundFile[ConvertedNamesCount];
				memcpy(ConvertedNames, src.ConvertedNames, ConvertedNamesCount);
			}
			else {
				ConvertedNames = src.ConvertedNames;
			}
		}
		else
			ConvertedNames = null;

		bHadMissingWaveFiles = src.bHadMissingWaveFiles;
		bUsesGenderToken = src.bUsesGenderToken;
	}

	public bool Equals(SoundParametersInternal other) {
		if (Unsafe.AreSame(in this, in other))
			return true;

		if (Channel != other.Channel)
			return false;
		if (!Interval.Compare(Volume, other.Volume))
			return false;
		if (!Interval.Compare(Pitch, other.Pitch))
			return false;
		if (!Interval.Compare(SoundLevel, other.SoundLevel))
			return false;
		if (DelayMsec != other.DelayMsec)
			return false;
		if (bPlayToOwnerOnly != other.bPlayToOwnerOnly)
			return false;

		if (SoundNames.Length != other.SoundNames.Length)
			return false;

		// Compare items
		int c = SoundNames.Length;
		for (int i = 0; i < c; i++) {
			if (GetSoundNames()[i].Symbol != other.GetSoundNames()[i].Symbol)
				return false;
		}

		return true;
	}


	public static ReadOnlySpan<char> _SoundLevelToString(SoundLevel level) {
		switch (level) {
			case Audio.SoundLevel.LvlNone: return "SNDLVL_NONE";
			case Audio.SoundLevel.Lvl20dB: return "SNDLVL_20dB";
			case Audio.SoundLevel.Lvl25dB: return "SNDLVL_25dB";
			case Audio.SoundLevel.Lvl30dB: return "SNDLVL_30dB";
			case Audio.SoundLevel.Lvl35dB: return "SNDLVL_35dB";
			case Audio.SoundLevel.Lvl40dB: return "SNDLVL_40dB";
			case Audio.SoundLevel.Lvl45dB: return "SNDLVL_45dB";
			case Audio.SoundLevel.Lvl50dB: return "SNDLVL_50dB";
			case Audio.SoundLevel.Lvl55dB: return "SNDLVL_55dB";
			case Audio.SoundLevel.Lvl60dB: return "SNDLVL_60dB";
			case Audio.SoundLevel.Lvl65dB: return "SNDLVL_65dB";
			case Audio.SoundLevel.Lvl70dB: return "SNDLVL_70dB";
			case Audio.SoundLevel.Lvl75dB: return "SNDLVL_75dB";
			case Audio.SoundLevel.Lvl80dB: return "SNDLVL_80dB";
			case Audio.SoundLevel.Lvl85dB: return "SNDLVL_85dB";
			case Audio.SoundLevel.Lvl90dB: return "SNDLVL_90dB";
			case Audio.SoundLevel.Lvl95dB: return "SNDLVL_95dB";
			case Audio.SoundLevel.Lvl100dB: return "SNDLVL_100dB";
			case Audio.SoundLevel.Lvl105dB: return "SNDLVL_105dB";
			case Audio.SoundLevel.Lvl110dB: return "SNDLVL_110dB";
			case Audio.SoundLevel.Lvl120dB: return "SNDLVL_120dB";
			case Audio.SoundLevel.Lvl130dB: return "SNDLVL_130dB";
			case Audio.SoundLevel.Lvl140dB: return "SNDLVL_140dB";
			case Audio.SoundLevel.Lvl150dB: return "SNDLVL_150dB";
			case Audio.SoundLevel.Lvl180dB: return "SNDLVL_180dB";
		}

		return $"{level}";
	}

	static bool NamedSoundLevel(ReadOnlySpan<char> name, out SoundLevel level) {
		level = default;
		if (name.IsEmpty)
			return false;

		switch (name[0] | 0x20) {
			case 'n':
				if (0 == stricmp(name, "NORM")) {
					level = Audio.SoundLevel.LvlNorm;
					return true;
				}
				if (0 == stricmp(name, "NONE")) {
					level = Audio.SoundLevel.LvlNone;
					return true;
				}
				break;
			case 'i':
				if (0 == stricmp(name, "IDLE")) {
					level = Audio.SoundLevel.LvlIdle;
					return true;
				}
				break;
			case 't':
				if (0 == stricmp(name, "TALKING")) {
					level = Audio.SoundLevel.LvlTalking;
					return true;
				}
				break;
			case 's':
				if (0 == stricmp(name, "STATIC")) {
					level = Audio.SoundLevel.LvlStatic;
					return true;
				}
				break;
			case 'g':
				if (0 == stricmp(name, "GUNFIRE")) {
					level = Audio.SoundLevel.LvlGunfire;
					return true;
				}
				break;
		}

		return false;
	}

	static string? ChannelName(SoundEntityChannel channel) {
		switch (channel) {
			case SoundEntityChannel.Auto: return "CHAN_AUTO";
			case SoundEntityChannel.Weapon: return "CHAN_WEAPON";
			case SoundEntityChannel.Voice: return "CHAN_VOICE";
			case SoundEntityChannel.Item: return "CHAN_ITEM";
			case SoundEntityChannel.Body: return "CHAN_BODY";
			case SoundEntityChannel.Stream: return "CHAN_STREAM";
			case SoundEntityChannel.Static: return "CHAN_STATIC";
			case SoundEntityChannel.Voice2: return "CHAN_VOICE2";
		}

		return null;
	}

	static bool NamedChannel(ReadOnlySpan<char> name, out SoundEntityChannel channel) {
		channel = default;
		if (name.IsEmpty)
			return false;

		switch (name[0] | 0x20) {
			case 'a':
				if (0 == stricmp(name, "AUTO")) {
					channel = SoundEntityChannel.Auto;
					return true;
				}
				break;
			case 'w':
				if (0 == stricmp(name, "WEAPON")) {
					channel = SoundEntityChannel.Weapon;
					return true;
				}
				break;
			case 'v':
				if (0 == stricmp(name, "VOICE")) {
					channel = SoundEntityChannel.Voice;
					return true;
				}
				if (0 == stricmp(name, "VOICE2")) {
					channel = SoundEntityChannel.Voice2;
					return true;
				}
				break;
			case 'i':
				if (0 == stricmp(name, "ITEM")) {
					channel = SoundEntityChannel.Item;
					return true;
				}
				break;
			case 'b':
				if (0 == stricmp(name, "BODY")) {
					channel = SoundEntityChannel.Body;
					return true;
				}
				break;
			case 's':
				if (0 == stricmp(name, "STREAM")) {
					channel = SoundEntityChannel.Stream;
					return true;
				}
				if (0 == stricmp(name, "STATIC")) {
					channel = SoundEntityChannel.Static;
					return true;
				}
				break;
		}

		return false;
	}

	public static ReadOnlySpan<char> _ChannelToString(int channel) {
		string? v = ChannelName((SoundEntityChannel)channel);
		if (v != null)
			return v;

		return $"{channel}";
	}

	public static ReadOnlySpan<char> _ChannelToString(SoundEntityChannel channel) {
		string? v = ChannelName(channel);
		if (v != null)
			return v;

		return $"{channel}";
	}

	public static ReadOnlySpan<char> _VolumeToString(float volume) {
		if (volume == VOL_NORM)
			return "VOL_NORM";

		return $"{volume}";
	}

	static string? PitchName(byte pitch) {
		switch (pitch) {
			case PITCH_NORM: return "PITCH_NORM";
			case PITCH_LOW: return "PITCH_LOW";
			case PITCH_HIGH: return "PITCH_HIGH";
		}

		return null;
	}

	public static ReadOnlySpan<char> _PitchToString(float pitch) {
		string? v = PitchName((byte)pitch);
		if (v != null)
			return v;

		return $"{pitch}";
	}

	public static ReadOnlySpan<char> _PitchToString(byte pitch) {
		string? v = PitchName(pitch);
		if (v != null)
			return v;

		return $"{pitch}";
	}

	public ReadOnlySpan<char> VolumeToString() {
		if (Volume.Range == (Half)0)
			return _VolumeToString((float)Volume.Start);

		return $"{Volume.Start}, {Volume.Start + Volume.Range}";
	}
	public ReadOnlySpan<char> ChannelToString() => _ChannelToString(Channel);
	public ReadOnlySpan<char> SoundLevelToString() {
		if (SoundLevel.Range == 0)
			return _SoundLevelToString((SoundLevel)(int)SoundLevel.Start);

		return $"{SoundLevel.Start}, {(SoundLevel)((int)SoundLevel.Start + (int)SoundLevel.Range)}";
	}
	public ReadOnlySpan<char> PitchToString() {
		if (Pitch.Range == 0)
			return _PitchToString((int)Pitch.Start);

		return $"{Pitch.Start}, {Pitch.Start + Pitch.Range}";
	}

	public void VolumeFromString(ReadOnlySpan<char> sz) {
		if (0 == strcmp(sz, "VOL_NORM")) {
			Volume.Start = (Half)VOL_NORM;
			Volume.Range = (Half)0.0f;
		}
		else {
			Volume.FromInterval(Interval.Read(sz));
		}
	}
	public SoundEntityChannel TextToChannel(ReadOnlySpan<char> name) {
		if (name.IsEmpty) {
			Assert(false);
			return SoundEntityChannel.Auto;
		}

		if (strnicmp(name, "chan_", "chan_".Length) != 0)
			return (SoundEntityChannel)atoi(name);

		if (NamedChannel(name["chan_".Length..], out SoundEntityChannel chan))
			return chan;

		// At this point, it starts with chan_ but is not recognized
		// atoi would return 0, so just do chan auto
		DevMsg($"SoundEmitterSystem:  Warning, unknown channel type in sounds.txt ({name})\n");

		return SoundEntityChannel.Auto;
	}

	public void ChannelFromString(ReadOnlySpan<char> sz) {
		Channel = TextToChannel(sz);
	}
	public const string SNDLVL_PREFIX = "SNDLVL_";

	public static SoundLevel TextToSoundLevel(ReadOnlySpan<char> key) {
		if (key.IsEmpty) {
			Assert(0);
			return Audio.SoundLevel.LvlNorm;
		}

		if (0 == stricmp(key[..Math.Min((int)strlen(SNDLVL_PREFIX), key.Length)], SNDLVL_PREFIX)) {
			ReadOnlySpan<char> val = key[(int)strlen(SNDLVL_PREFIX)..];
			if (NamedSoundLevel(val, out SoundLevel level))
				return level;

			int sndlvl = atoi(val);
			if (sndlvl > 0 && sndlvl <= 180)
				return (SoundLevel)sndlvl;
		}

		DevMsg($"SoundEmitterSystem:  Unknown sound level {key}\n");

		return Audio.SoundLevel.LvlNorm;
	}

	public void PitchFromString(ReadOnlySpan<char> sz) {
		if (0 == strcmp(sz, "PITCH_NORM")) {
			Pitch.Start = PITCH_NORM;
			Pitch.Range = 0;
		}
		else if (0 == strcmp(sz, "PITCH_LOW")) {
			Pitch.Start = PITCH_LOW;
			Pitch.Range = 0;
		}
		else if (0 == strcmp(sz, "PITCH_HIGH")) {
			Pitch.Start = PITCH_HIGH;
			Pitch.Range = 0;
		}
		else
			Pitch.FromInterval(Interval.Read(sz));
	}
	public void SoundLevelFromString(ReadOnlySpan<char> sz) {
		if (0 == strnicmp(sz, "SNDLVL_", "SNDLVL_".Length)) {
			SoundLevel.Start = (ushort)TextToSoundLevel(sz);
			SoundLevel.Range = 0;
		}
		else
			SoundLevel.FromInterval(Interval.Read(sz));
	}

	public readonly SoundEntityChannel GetChannel() => Channel;

	public unsafe ref readonly VolumeInterval GetVolume() => ref Volume;
	public unsafe ref readonly PitchInterval GetPitch() => ref Pitch;
	public unsafe ref readonly SoundLevelInterval GetSoundLevel() => ref SoundLevel;
	public int GetDelayMsec() => DelayMsec;
	public bool OnlyPlayToOwner() => bPlayToOwnerOnly;
	public bool HadMissingWaveFiles() => bHadMissingWaveFiles;
	public bool UsesGenderToken() => bUsesGenderToken;
	public bool ShouldPreload() => bShouldPreload;

	public void SetChannel(SoundEntityChannel newChannel) => Channel = newChannel;
	public void SetChannel(int newChannel) => Channel = (SoundEntityChannel)newChannel;
	public void SetVolume(float start, float range = 0.0f) { Volume.Start = (Half)start; Volume.Range = (Half)range; }
	public void SetPitch(float start, float range = 0.0f) { Pitch.Start = (byte)start; Pitch.Range = (byte)range; }
	public void SetSoundLevel(float start, float range = 0.0f) { SoundLevel.Start = (ushort)start; SoundLevel.Range = (ushort)range; }
	public void SetDelayMsec(int delay) => DelayMsec = (ushort)delay;
	public void SetShouldPreload(bool bShouldPreload) => this.bShouldPreload = bShouldPreload;
	public void SetOnlyPlayToOwner(bool b) => bPlayToOwnerOnly = b;
	public void SetHadMissingWaveFiles(bool b) => bHadMissingWaveFiles = b;
	public void SetUsesGenderToken(bool b) => bUsesGenderToken = b;

	public void AddSoundName(in SoundFile soundFile) => AddToTail(ref SoundNames, ref SoundNamesCount, soundFile);
	public int NumSoundNames() => SoundNamesCount;
	public Span<SoundFile> GetSoundNames() => SoundNames;

	public void AddConvertedName(in SoundFile soundFile) => AddToTail(ref ConvertedNames, ref ConvertedNamesCount, soundFile);
	int NumConvertedNames() => ConvertedNamesCount;
	Span<SoundFile> GetConvertedNames() => ConvertedNames;

	void AddToTail(ref SoundFile[]? dest, ref ushort destCount, in SoundFile source) {
		destCount++;
		if (destCount == 1) {
			// NOTE: when there's only one soundfile in the list, we store it
			// packed into the pointer itself, the four bytes for the pointer is just used to store the sound file!
			dest = new SoundFile[1];
			dest[0] = source;
		}
		else {
			SoundFile temp = default;
			if (destCount == 2) {
				temp = dest![0];
				dest = null!;
			}

			Array.Resize(ref dest, destCount);
			dest[destCount - 1] = source;

			if (destCount == 2)
				dest[0] = temp;
		}
	}

	SoundFile[]? SoundNames;
	SoundFile[]? ConvertedNames;

	ushort ConvertedNamesCount;
	ushort SoundNamesCount;

	VolumeInterval Volume;
	SoundLevelInterval SoundLevel;
	PitchInterval Pitch;
	SoundEntityChannel Channel;
	ushort DelayMsec;

	bool bPlayToOwnerOnly;
	bool bHadMissingWaveFiles;
	bool bUsesGenderToken;
	bool bShouldPreload;
}
public struct SoundParameters
{
	public SoundParameters() {
		Channel = SoundEntityChannel.Auto; // 0
		Volume = VOL_NORM;  // 1.0f
		Pitch = PITCH_NORM; // 100

		PitchLow = PITCH_NORM;
		PitchHigh = PITCH_NORM;

		SoundLevel = SoundLevel.LvlNorm; // 75dB
		SoundName[0] = '\0';
		PlayToOwnerOnly = false;
		Count = 0;

		DelayMsec = 0;
	}

	public SoundEntityChannel Channel;
	public float Volume;
	public int Pitch;
	public int PitchLow, PitchHigh;
	public SoundLevel SoundLevel;
	public bool PlayToOwnerOnly;
	public int Count;
	public InlineArray128<char> SoundName;
	public int DelayMsec;
}

public enum Gender : sbyte
{
	None,
	Male,
	Female
}

public interface ISoundEmitterSystemBase
{
	bool ModInit();
	void ModShutdown();

	int GetSoundIndex(ReadOnlySpan<char> name);
	bool IsValidIndex(int index);
	int GetSoundCount();

	ReadOnlySpan<char> GetSoundName(int index);
	bool GetParametersForSound(ReadOnlySpan<char> soundname, ref SoundParameters parms, Gender gender, bool isbeingemitted = false);

	ReadOnlySpan<char> GetWaveName(UtlSymbol sym);
	UtlSymbol AddWaveName(ReadOnlySpan<char> name);

	SoundLevel LookupSoundLevel(ReadOnlySpan<char> soundname);
	ReadOnlySpan<char> GetWavFileForSound(ReadOnlySpan<char> soundname, ReadOnlySpan<char> actormodel);
	ReadOnlySpan<char> GetWavFileForSound(ReadOnlySpan<char> soundname, Gender gender);
	int CheckForMissingWavFiles(bool verbose);
	ReadOnlySpan<char> GetSourceFileForSound(int index);

	// Iteration methods
	int First();
	int Next(int i);
	int InvalidIndex();

	ref SoundParametersInternal InternalGetParametersForSound(int index);

	// The host application is responsible for dealing with dirty sound scripts, etc.
	bool AddSound(ReadOnlySpan<char> soundname, ReadOnlySpan<char> scriptfile, in SoundParametersInternal parms);
	void RemoveSound(ReadOnlySpan<char> soundname);
	void MoveSound(ReadOnlySpan<char> soundname, ReadOnlySpan<char> newscript);
	void RenameSound(ReadOnlySpan<char> soundname, ReadOnlySpan<char> newname);

	void UpdateSoundParameters(ReadOnlySpan<char> soundname, in SoundParametersInternal parms);

	int GetNumSoundScripts();
	ReadOnlySpan<char> GetSoundScriptName(int index);
	bool IsSoundScriptDirty(int index);
	int FindSoundScript(ReadOnlySpan<char> name);
	void SaveChangesToSoundScript(int scriptindex);

	void ExpandSoundNameMacros(ref SoundParametersInternal parms, ReadOnlySpan<char> wavename);
	Gender GetActorGender(ReadOnlySpan<char> actormodel);
	void GenderExpandString(ReadOnlySpan<char> actormodel, ReadOnlySpan<char> inText, Span<char> outText);
	void GenderExpandString(Gender gender, ReadOnlySpan<char> inText, Span<char> outText);
	bool IsUsingGenderToken(ReadOnlySpan<char> soundname);

	// For blowing away caches based on filetimstamps of the manifest, or of any of the
	//  .txt files that are read into the sound emitter system
	uint GetManifestFileTimeChecksum();

	// Called from both client and server (single player) or just one (server only in dedicated server and client only if connected to a remote server)
	// Called by LevelInitPreEntity to override sound scripts for the mod with level specific overrides based on custom mapnames, etc.
	void AddSoundOverrides(ReadOnlySpan<char> scriptfile, bool preload = false);

	// Called by either client or server in LevelShutdown to clear out custom overrides
	void ClearSoundOverrides();

	bool GetParametersForSoundEx(ReadOnlySpan<char> soundname, ref HSOUNDSCRIPTHANDLE handle, ref SoundParameters parms, Gender gender, bool isbeingemitted = false);
	SoundLevel LookupSoundLevelByHandle(ReadOnlySpan<char> soundname, ref HSOUNDSCRIPTHANDLE handle);

	// TODO: void ReloadSoundEntriesInList(IFileList filesToReload);

	// Called by either client or server to force ModShutdown and ModInit
	void Flush();
}
