using CommunityToolkit.HighPerformance;

using SharpCompress.Common;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.Hashing;
using Source.Common.SoundEmitterSystem;
using Source.Common.Utilities;
using Source.SoundEmitterSystem;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Source.SoundEmitterSystem;

class SoundEmitterUniformRandomStream : IUniformRandomStream
{
	public static readonly SoundEmitterUniformRandomStream g_RandomStream = new();

	public void SetSeed(int _) => Assert(false);
	public float RandomFloat(float flMinVal = 0.0f, float flMaxVal = 1.0f) => RandomGlobals.RandomFloat(flMinVal, flMaxVal);
	public int RandomInt(int iMinVal, int iMaxVal) => RandomGlobals.RandomInt(iMinVal, iMaxVal);
	public float RandomFloatExp(float flMinVal = 0.0f, float flMaxVal = 1.0f, float flExponent = 1.0f) => RandomGlobals.RandomFloatExp(flMinVal, flMaxVal, flExponent);
}

public struct SoundEntry : IEquatable<SoundEntry>
{
	string? name;
	ulong hash;
	public SoundParametersInternal SoundParams;
	public int ScriptFileIndex;
	public bool Removed;
	public bool IsOverride;

	public ReadOnlySpan<char> Name {
		readonly get => name;
		set {
			name = value.Length == 0 ? null : string.Intern(new(value.SliceNullTerminatedString()));
			hash = name?.Hash() ?? 0;
		}
	}

	public readonly bool Equals(SoundEntry other) => string.Equals(name, other.name, StringComparison.OrdinalIgnoreCase);
	public override readonly bool Equals(object? obj) => obj is SoundEntry other && Equals(other);

	public override int GetHashCode() {
		return HashCode.Combine(hash);
	}
}

public class SymbolStringComparer_OrdinalIgnoreCase : IEqualityComparer<UtlSymId_t>
{
	public static readonly SymbolStringComparer_OrdinalIgnoreCase Instance = new();

	public bool Equals(UtlSymId_t x, UtlSymId_t y) {
		return x == y;
	}

	public int GetHashCode([DisallowNull] UtlSymId_t obj) {
		return HashCode.Combine(obj);
	}
}

public class SoundEmitterSystemBase : ISoundEmitterSystemBase
{
	public const string MANIFEST_FILE = "scripts/game_sounds_manifest.txt";
	public const string GAME_SOUNDS_HEADER_BLOCK = "scripts/game_sounds_header.txt";

	readonly Dictionary<UtlSymId_t, Gender> m_ActorGenders = new(SymbolStringComparer_OrdinalIgnoreCase.Instance);

	readonly LinkedList<SoundEntry> Sounds = new();
	readonly Dictionary<SoundEntry, LinkedListNode<SoundEntry>> SoundEntryLookup = new();
	readonly Dictionary<int, LinkedListNode<SoundEntry>> HandleToSound = [];
	readonly Dictionary<LinkedListNode<SoundEntry>, int> SoundToHandle = [];

	int CurrentHandle;
	int Sounds_AllocHandle(LinkedListNode<SoundEntry> node) {
		Sounds.AddLast(node);
		SoundEntryLookup[node.Value] = node;
		int handle = Interlocked.Increment(ref CurrentHandle);
		HandleToSound[handle] = node;
		SoundToHandle[node] = handle;
		return handle;
	}

	void Sounds_ReplaceKey(int handle, LinkedListNode<SoundEntry> newNode) {
		newNode.List?.Remove(newNode); // just in case...

		LinkedListNode<SoundEntry> oldNode = HandleToSound[handle];
		Sounds.AddAfter(oldNode, newNode);
		Sounds.Remove(oldNode);
		SoundToHandle.Remove(oldNode);

		SoundEntryLookup[newNode.Value] = newNode;
		HandleToSound[handle] = newNode;
		SoundToHandle[newNode] = handle;
	}

	readonly List<LinkedListNode<SoundEntry>> SavedOverrides = [];
	readonly List<FileNameHandle_t> OverrideFiles = [];

	struct SoundScriptFile
	{
		public FileNameHandle_t Filename;
		public bool Dirty;
	}

	readonly List<SoundScriptFile> SoundKeyValues = [];
	int InitCount;
	uint ManifestPlusScriptChecksum;

	readonly UtlSymbolTable Waves = new();

	public bool AddSound(ReadOnlySpan<char> soundname, ReadOnlySpan<char> scriptfile, in SoundParametersInternal parms) {
		int idx = GetSoundIndex(soundname);


		int i = FindSoundScript(scriptfile);
		if (i == -1) {
			Warning($"SoundEmitterSystemBase.AddSound( '{soundname}', '{scriptfile}', ... ), script file not list in manifest '{MANIFEST_FILE}'\n");
			return false;
		}

		// More like an update...
		if (IsValidIndex(idx)) {
			ref SoundEntry entry = ref HandleToSound[idx].ValueRef;

			entry.Removed = false;
			entry.ScriptFileIndex = i;
			entry.SoundParams.CopyFrom(parms);

			SoundKeyValues.AsSpan()[i].Dirty = true;

			return true;
		}

		LinkedListNode<SoundEntry> entryNode = new(new());
		ref SoundEntry newEntry = ref entryNode.ValueRef;
		newEntry.Name = soundname;
		newEntry.Removed = false;
		newEntry.ScriptFileIndex = i;
		newEntry.SoundParams.CopyFrom(parms);

		if (SoundEntryLookup.TryGetValue(newEntry, out LinkedListNode<SoundEntry>? tryFindingDuplicate))
			Sounds_ReplaceKey(SoundToHandle[tryFindingDuplicate], entryNode);
		else
			Sounds_AllocHandle(entryNode);

		SoundKeyValues.AsSpan()[i].Dirty = true;

		return true;
	}

	public void AddSoundOverrides(ReadOnlySpan<char> scriptfile, bool preload = false) {
		FileNameHandle_t handle = filesystem.FindOrAddFileName(scriptfile);
		if (OverrideFiles.Find(handle) != -1)
			return;

		OverrideFiles.Add(handle);
		// These are overrides
		AddSoundsFromFile(scriptfile, preload, true);
	}

	public UtlSymbol AddWaveName(ReadOnlySpan<char> name) => Waves.AddString(name);

	public int CheckForMissingWavFiles(bool verbose) {
		int missing = 0;

		int c = GetSoundCount();
		int i;
		Span<char> testfile = stackalloc char[512];

		for (i = 0; i < c; i++) {
			ref SoundParametersInternal internalParams = ref InternalGetParametersForSound(i);
			if (Unsafe.IsNullRef(ref internalParams)) {
				Assert(false);
				continue;
			}

			int waveCount = internalParams.NumSoundNames();
			for (int wave = 0; wave < waveCount; wave++) {
				UtlSymbol sym = internalParams.GetSoundNames()[wave].Symbol;
				ReadOnlySpan<char> name = Waves.String(sym);
				if (name.IsStringEmpty) {
					Assert(false);
					continue;
				}

				// Skip ! sentence stuff
				if ((SoundChars)name[0] == SoundChars.Sentence)
					continue;

				sprintf(testfile, "sound/%s").S(SoundCharsUtils.SkipSoundChars(name));
				if (filesystem.FileExists(testfile))
					continue;

				internalParams.SetHadMissingWaveFiles(true);

				++missing;

				if (verbose)
					DevMsg($"Sound {GetSoundName(i)} references missing file {name}\n");
			}
		}

		return missing;
	}

	public void ClearSoundOverrides() {
		throw new NotImplementedException();
	}

	void AddSoundName(ref SoundParametersInternal parms, ReadOnlySpan<char> wavename, Gender gender) {
		UtlSymbol sym = Waves.AddString(wavename);
		SoundFile e = new();
		e.Symbol = sym;
		e.Gender = gender;
		if (gender != Gender.None)
			parms.SetUsesGenderToken(true);
		parms.AddSoundName(e);
	}

	public void ExpandSoundNameMacros(ref SoundParametersInternal parms, ReadOnlySpan<char> wavename) {
		int offset = wavename.IndexOf(SOUNDGENDER_MACRO, StringComparison.OrdinalIgnoreCase);
		if (offset == -1) {
			AddSoundName(ref parms, wavename, Gender.None);
			return;
		}

		Assert(offset >= 0);
		int duration = SOUNDGENDER_MACRO_LENGTH;

		// Create a "male" and "female" version of the sound
		Span<char> before = stackalloc char[256], after = stackalloc char[256];

		SplitName(wavename, offset, duration, before, after);

		Span<char> temp = stackalloc char[256];
		sprintf(temp, "%s%s%s").S(before).S("male").S(after);
		AddSoundName(ref parms, temp.SliceNullTerminatedString(), Gender.Male);
		sprintf(temp, "%s%s%s").S(before).S("female").S(after);
		AddSoundName(ref parms, temp.SliceNullTerminatedString(), Gender.Female);

		// Add the conversion entry with the gender tags still in it
		UtlSymbol sym = Waves.AddString(wavename);
		SoundFile e = new();
		e.Symbol = sym;
		e.Gender = Gender.None;
		parms.AddConvertedName(e);
	}

	public int FindSoundScript(ReadOnlySpan<char> name) {
		int i, c;

		FileNameHandle_t hFilename = filesystem.FindFileName(name);
		if (hFilename == 0) {
			// First, make sure it's known
			c = SoundKeyValues.Count;
			for (i = 0; i < c; i++)
				if (SoundKeyValues[i].Filename == hFilename)
					return i;
		}

		return -1;
	}

	public int First() => Sounds.Count == 0 ? -1 : SoundToHandle[Sounds.First!];

	public void Flush() {
		InternalModShutdown();
		InternalModInit();
	}

	public void GenderExpandString(ReadOnlySpan<char> actormodel, ReadOnlySpan<char> inText, Span<char> outText) {
		Gender gender = GetActorGender(actormodel);
		GenderExpandString(gender, inText, outText);
	}

	public const string SOUNDGENDER_MACRO = "$gender";
	public const int SOUNDGENDER_MACRO_LENGTH = 7;

	static void SplitName(ReadOnlySpan<char> input, int splitchar, int splitlen, Span<char> before, Span<char> after) {
		ReadOnlySpan<char> inText = input;
		Span<char> outText = before;

		int c = 0;
		int l = 0;
		int maxl = before.Length;
		while (inText.Length > 0 && inText[0] != '\0') {
			if (c == splitchar) {
				while (--splitlen >= 0)
					inText = inText[1..];

				outText[0] = '\0';
				outText = after;
				maxl = after.Length;
				c++;
				continue;
			}

			if (l >= maxl) {
				inText = inText[1..];
				c++;
				continue;
			}

			outText[0] = inText[0];
			inText = inText[1..];
			outText = outText[1..];
			l++;
			c++;
		}

		outText[0] = '\0';
	}

	public void GenderExpandString(Gender gender, ReadOnlySpan<char> inText, Span<char> outText) {
		// Assume the worst
		strcpy(outText, inText);

		int offset = inText.IndexOf(SOUNDGENDER_MACRO);
		if (offset == -1)
			return;

		// Look up actor gender
		if (gender == Gender.None)
			return;

		Assert(offset >= 0);
		int duration = SOUNDGENDER_MACRO_LENGTH;

		// Create a "male" and "female" version of the sound
		Span<char> before = stackalloc char[256], after = stackalloc char[256];

		SplitName(inText, offset, duration, before, after);

		switch (gender) {
			default:
			case Gender.None: AssertMsg(false, "CSoundEmitterSystemBase::GenderExpandString:  expecting MALE or FEMALE!"); break;
			case Gender.Male: sprintf(outText, "%s%s%s").S(before).S("male").S(after); break;
			case Gender.Female: sprintf(outText, "%s%s%s").S(before).S("female").S(after); break;
		}
	}

	readonly Dictionary<ulong, Gender> ActorGenders = [];

	public Gender GetActorGender(ReadOnlySpan<char> actormodel) {
		Span<char> actor = stackalloc char[256];
		actor[0] = '\0';
		scoped ReadOnlySpan<char> check = default;
		if (!actormodel.IsEmpty)
			check = StrTools.FileBase(actormodel, actor);

		return ActorGenders.TryGetValue(check.Hash(), out Gender g) ? g : Gender.None;
	}

	public uint GetManifestFileTimeChecksum() => ManifestPlusScriptChecksum;
	public int GetNumSoundScripts() => SoundKeyValues.Count;

	public bool GetParametersForSound(ReadOnlySpan<char> soundname, ref SoundParameters parms, Gender gender, bool isbeingemitted = false) {
		HSOUNDSCRIPTHANDLE index = (HSOUNDSCRIPTHANDLE)GetSoundIndex(soundname);
		if (index == SOUNDEMITTER_INVALID_HANDLE) {
			Span<char> key = stackalloc char[256];
			sprintf(key, "%s:%s").S(soundname).S(((Span<char>)parms.SoundName).SliceNullTerminatedString());
			if (UTL_INVAL_SYMBOL == soundWarnings.Find(key)) {
				soundWarnings.AddString(key);

				DevMsg($"CSoundEmitterSystemBase::GetParametersForSound:  No such sound {soundname}\n");
			}
			return false;
		}

		return GetParametersForSoundEx(soundname, ref index, ref parms, gender, isbeingemitted);
	}

	public void EnsureAvailableSlotsForGender(Span<SoundFile> soundNames, int c, Gender gender) {
		int i;
		if (c <= 0)
			return;

		List<int> slots = []; // todo: dont make a list here

		bool needsreset = false;
		for (i = 0; i < c; i++) {
			if (soundNames[i].Gender != gender)
				continue;

			// There was at least one match for the gender
			needsreset = true;

			// This sound is unavailable
			if (!soundNames[i].Available)
				continue;

			slots.Add(i);
		}

		if (slots.Count() == 0 && needsreset) {
			// Reset all slots for the specified gender!!!
			for (i = 0; i < c; i++) {
				if (soundNames[i].Gender != gender)
					continue;

				soundNames[i].Available = true;
			}
		}
	}

	public int FindBestSoundForGender(Span<SoundFile> soundNames, int c, Gender gender) {
		// Check for recycling of random sounds...
		EnsureAvailableSlotsForGender(soundNames, c, gender);

		if (c <= 0)
			return -1;

		List<int> slots = new(); // todo: dont make a list here

		for (int i = 0; i < c; i++)
			if (soundNames[i].Gender == gender && soundNames[i].Available)
				slots.Add(i);

		if (slots.Count >= 1)
			return slots[SoundEmitterUniformRandomStream.g_RandomStream.RandomInt(0, slots.Count - 1)];

		return SoundEmitterUniformRandomStream.g_RandomStream.RandomInt(0, c - 1);
	}

	public bool GetParametersForSoundEx(ReadOnlySpan<char> soundname, ref short handle, ref SoundParameters parms, Gender gender, bool isbeingemitted = false) {
		if (handle == SOUNDEMITTER_INVALID_HANDLE) {
			handle = (short)GetSoundIndex(soundname);
			if (handle == SOUNDEMITTER_INVALID_HANDLE)
				return false;
		}

		ref SoundParametersInternal internalParams = ref InternalGetParametersForSound((int)handle);
		if (Unsafe.IsNullRef(ref internalParams)) {
			Assert(false);
			DevMsg($"SoundEmitterSystemBase.GetParametersForSound:  No such sound {soundname}\n");
			return false;
		}

		parms.Channel = internalParams.GetChannel();
		parms.Volume = internalParams.GetVolume().Random();
		parms.Pitch = (int)internalParams.GetPitch().Random();
		parms.PitchLow = internalParams.GetPitch().Start;
		parms.PitchHigh = parms.PitchLow + internalParams.GetPitch().Range;
		parms.DelayMsec = internalParams.GetDelayMsec();
		parms.Count = internalParams.NumSoundNames();
		parms.SoundName[0] = '\0';

		int bestIndex = FindBestSoundForGender(internalParams.GetSoundNames(), internalParams.NumSoundNames(), gender);

		if (bestIndex >= 0) {
			strcpy(parms.SoundName, GetWaveName(internalParams.GetSoundNames()[bestIndex].Symbol));

			// If we are actually emitting the sound, mark it as not available...
			if (isbeingemitted)
				internalParams.GetSoundNames()[bestIndex].Available = false;
		}
		parms.SoundLevel = (SoundLevel)(int)internalParams.GetSoundLevel().Random();
		parms.PlayToOwnerOnly = internalParams.OnlyPlayToOwner();

		if (parms.SoundName[0] == '\0') {
			DevMsg($"SoundEmitterSystemBase.GetParametersForSound:  sound {soundname} has no wave or rndwave key!\n");
			return false;
		}

		if (internalParams.HadMissingWaveFiles() && (SoundChars)parms.SoundName[0] != SoundChars.Sentence) {
			Span<char> testfile = stackalloc char[256];
			sprintf(testfile, "sound/%s").S(SoundCharsUtils.SkipSoundChars(((Span<char>)parms.SoundName).SliceNullTerminatedString()));
			if (!filesystem.FileExists(testfile)) {
				// Prevent repetitive spew...
				Span<char> key = stackalloc char[256];
				sprintf(key, "%s:%s").S(soundname).S(((Span<char>)parms.SoundName).SliceNullTerminatedString());
				if (UTL_INVAL_SYMBOL == soundWarnings.Find(key)) {
					soundWarnings.AddString(key);

					DevMsg($"SoundEmitterSystemBase.GetParametersForSound:  sound '{soundname}' references wave '{((Span<char>)parms.SoundName).SliceNullTerminatedString()}' which doesn't exist on disk!\n");
				}
				return false;
			}
		}

		return true;
	}
	static readonly UtlSymbolTable soundWarnings = new();

	public int GetSoundCount() => Sounds.Count;
	public int GetSoundIndex(ReadOnlySpan<char> name) {
		if (name.IsEmpty)
			return -1;

		SoundEntry search = default;
		search.Name = name;
		if (!SoundEntryLookup.TryGetValue(search, out var node))
			return -1;
		return SoundToHandle[node];
	}

	public ReadOnlySpan<char> GetSoundName(int index) {
		if (!IsValidIndex(index))
			return "";
		return HandleToSound[index].Value.Name;
	}

	public ReadOnlySpan<char> GetSoundScriptName(int index) {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetSourceFileForSound(int index) {
		if (index < 0 || index >= (int)Sounds.Count) {
			Assert(0);
			return "";
		}

		ref SoundEntry entry = ref HandleToSound[index].ValueRef;
		int scriptindex = entry.ScriptFileIndex;
		if (scriptindex < 0 || scriptindex >= SoundKeyValues.Count) {
			Assert(0);
			return "";
		}

		ReadOnlySpan<char> fn = filesystem.String(SoundKeyValues[scriptindex].Filename);
		if (!fn.IsStringEmpty)
			return fn;

		Assert(0);
		return "";
	}

	public ReadOnlySpan<char> GetWaveName(UtlSymbol sym) => Waves.String(sym);
	public ReadOnlySpan<char> GetWavFileForSound(ReadOnlySpan<char> soundname, ReadOnlySpan<char> actormodel) {
		Gender gender = GetActorGender(actormodel);
		return GetWavFileForSound(soundname, gender);
	}

	static readonly char[] GetWavFileForSound__outsound = new char[512];
	public ReadOnlySpan<char> GetWavFileForSound(ReadOnlySpan<char> soundname, Gender gender) {
		SoundParameters parms = default;
		if (!GetParametersForSound(soundname, ref parms, gender))
			return soundname;


		if (parms.SoundName[0] == '\0')
			return soundname;

		int chars = strcpy(GetWavFileForSound__outsound, parms.SoundName);
		return GetWavFileForSound__outsound.AsSpan()[..chars];
	}

	public ref SoundParametersInternal InternalGetParametersForSound(int index) {
		if (!IsValidIndex(index)) {
			AssertMsg(false, "CSoundEmitterSystemBase::InternalGetParametersForSound:  Bogus index");
			return ref Unsafe.NullRef<SoundParametersInternal>();
		}

		return ref HandleToSound[index].ValueRef.SoundParams;
	}

	public int InvalidIndex() => -1;

	public bool IsSoundScriptDirty(int index) {
		if (index < 0 || index >= SoundKeyValues.Count)
			return false;
		return SoundKeyValues.AsSpan()[index].Dirty;
	}

	public bool IsUsingGenderToken(ReadOnlySpan<char> soundname) {
		int soundindex = GetSoundIndex(soundname);
		if (soundindex < 0)
			return false;

		// Look up the sound level from the soundemitter system
		ref SoundParametersInternal parms = ref InternalGetParametersForSound(soundindex);
		if (Unsafe.IsNullRef(ref parms))
			return false;

		return parms.UsesGenderToken();
	}

	public bool IsValidIndex(int index) => HandleToSound.ContainsKey(index);

	public SoundLevel LookupSoundLevel(ReadOnlySpan<char> soundname) {
		Console.WriteLine($"LookupSoundLevel not implemented {soundname}");
		return SoundLevel.LvlNorm;
	}

	public SoundLevel LookupSoundLevelByHandle(ReadOnlySpan<char> soundname, ref HSOUNDSCRIPTHANDLE handle) {
		if (handle == SOUNDEMITTER_INVALID_HANDLE) {
			handle = (HSOUNDSCRIPTHANDLE)GetSoundIndex(soundname);
			if (handle == SOUNDEMITTER_INVALID_HANDLE)
				return SoundLevel.LvlNorm;
		}

		ref SoundParametersInternal internl = ref InternalGetParametersForSound((int)handle);
		if (Unsafe.IsNullRef(ref internl))
			return SoundLevel.LvlNorm;

		return (SoundLevel)(int)internl.GetSoundLevel().Random();
	}

	public bool ModInit() {
		InitCount++;
		if (InitCount > 1)
			return true;
		return InternalModInit();
	}

	public bool InitSoundInternalParameters(ReadOnlySpan<char> soundname, KeyValues kv, ref SoundParametersInternal parms) {
		KeyValues? key = kv.GetFirstSubKey();
		while (key != null) {
			if (0 == strcmp(key.Name, "channel")) {
				parms.ChannelFromString(key.GetString());
			}
			else if (0 == strcmp(key.Name, "volume")) {
				parms.VolumeFromString(key.GetString());
			}
			else if (0 == strcmp(key.Name, "pitch")) {
				parms.PitchFromString(key.GetString());
			}
			else if (0 == strcmp(key.Name, "wave")) {
				ExpandSoundNameMacros(ref parms, key.GetString());
			}
			else if (0 == strcmp(key.Name, "rndwave")) {
				KeyValues? waves = key.GetFirstSubKey();
				while (waves != null) {
					ExpandSoundNameMacros(ref parms, waves.GetString());

					waves = waves.GetNextKey();
				}
			}
			else if (0 == strcmp(key.Name, "attenuation") || 0 == strcmp(key.Name, "CompatibilityAttenuation")) {
				if (key.GetString().StartsWith("SNDLVL_"))
					DevMsg($"SoundEmitterSystemBase.InitSoundInternalParameters:  sound {soundname} has \"attenuation\" with {key.GetString()} value!\n");

				if (key.GetString().StartsWith("ATTN_"))
					parms.SetSoundLevel((float)ATTN_TO_SNDLVL(TranslateAttenuation(key.GetString())));
				else {
					Interval interval = Interval.Read(key.GetString());

					// Translate from attenuation to soundlevel
					float start = interval.Start;
					float end = interval.Start + interval.Range;

					parms.SetSoundLevel((float)ATTN_TO_SNDLVL(start), ATTN_TO_SNDLVL(end) - ATTN_TO_SNDLVL(start));
				}

				// Goldsrc compatibility mode.. feed the sndlevel value through the sound engine interface in such a way
				// that it can reconstruct the original sndlevel value and flag the sound as using Goldsrc attenuation.
				bool bCompatibilityAttenuation = 0 == strcmp(key.Name, "CompatibilityAttenuation");
				if (bCompatibilityAttenuation) {
					if (parms.GetSoundLevel().Range != 0)
						Warning($"CompatibilityAttenuation for sound {soundname} must have same start and end values.\n");

					parms.SetSoundLevel((float)SNDLEVEL_TO_COMPATIBILITY_MODE(parms.GetSoundLevel().Start));
				}
			}
			else if (0 == strcmp(key.Name, "soundlevel")) {
				if (key.GetString().StartsWith("ATTN_"))
					DevMsg($"SoundEmitterSystemBase.GetParametersForSound:  sound {soundname} has \"soundlevel\" with {key.GetString()} value!\n");

				parms.SoundLevelFromString(key.GetString());
			}
			else if (0 == strcmp(key.Name, "play_to_owner_only")) {
				parms.SetOnlyPlayToOwner(key.GetInt() != 0);
			}
			else if (0 == strcmp(key.Name, "delay_msec")) {
				// Don't allow negative delay
				parms.SetDelayMsec(Math.Max(0, key.GetInt()));
			}

			key = key.GetNextKey();
		}

		return true;
	}

	private static float TranslateAttenuation(ReadOnlySpan<char> key) {
		switch (key) {
			case "ATTN_NONE": return ATTN_NONE;
			case "ATTN_NORM": return ATTN_NORM;
			case "ATTN_IDLE": return ATTN_IDLE;
			case "ATTN_STATIC": return ATTN_STATIC;
			case "ATTN_RICOCHET": return ATTN_RICOCHET;
			case "ATTN_GUNFIRE": return ATTN_GUNFIRE;
			default:
				DevMsg($"SoundEmitterSystem: Unknown attenuation key {key}\n");
				return ATTN_NORM;
		}
	}

	public void AddSoundsFromFile(ReadOnlySpan<char> filename, bool preload, bool isOverride = false, bool refresh = false) {
		SoundScriptFile sf = default;
		sf.Filename = filesystem.FindOrAddFileName(filename);
		sf.Dirty = false;

		int scriptindex = SoundKeyValues.Count; SoundKeyValues.Add(sf);

		int replaceCount = 0;
		int newOverrideCount = 0;
		int duplicatedReplacements = 0;

		// Open the soundscape data file, and abort if we can't
		KeyValues kv = new KeyValues("");
		if (filesystem.LoadKeyValues(kv, IFileSystem.KeyValuesPreloadType.SoundEmitter, filename, "GAME")) {
			// parse out all of the top level sections and save their names
			KeyValues? pKeys = kv;
			while (pKeys != null) {
				if (pKeys.GetFirstSubKey() != null) {
					if (Sounds.Count >= 65534) {
						Warning("Exceeded maximum number of sound emitter entries\n");
						break;
					}

					LinkedListNode<SoundEntry> entryNode = new(new());
					ref SoundEntry entry = ref entryNode.ValueRef;
					entry.Name = pKeys.Name;
					entry.Removed = false;
					entry.ScriptFileIndex = scriptindex;
					entry.IsOverride = isOverride;

					if (isOverride)
						++newOverrideCount;

					bool isDuplicate;
					int lookup = -1;
					{
						if (SoundEntryLookup.TryGetValue(entry, out var lookupEntry)) {
							isDuplicate = true;
							lookup = SoundToHandle[lookupEntry];
						}
						else
							isDuplicate = false;
					}

					if (isDuplicate) {
						if (isOverride) {
							// Store off the old sound if it's not already an "override" from another file!!!
							// Otherwise, just whack it again!!!
							if (!HandleToSound[lookup].ValueRef.IsOverride)
								SavedOverrides.Add(HandleToSound[lookup]);
							else
								++duplicatedReplacements;

							InitSoundInternalParameters(pKeys.Name, pKeys, ref entry.SoundParams);
							entry.SoundParams.SetShouldPreload(preload); // this gets handled by game code after initting.

							Sounds_ReplaceKey(lookup, entryNode);

							++replaceCount;
						}
						else if (refresh) {
							InitSoundInternalParameters(pKeys.Name, pKeys, ref HandleToSound[lookup].ValueRef.SoundParams);
						}
					}
					else {
						InitSoundInternalParameters(pKeys.Name, pKeys, ref entry.SoundParams);
						entry.SoundParams.SetShouldPreload(preload); // this gets handled by game code after initting.

						Sounds_AllocHandle(entryNode);
					}
				}
				pKeys = pKeys.GetNextKey();
			}
		}
		else {
			if (isOverride)
				Warning($"SoundEmitterSystem.AddSoundsFromFile:  No such file {filename}\n");

			// Discard
			SoundKeyValues.RemoveAt(scriptindex);

			return;
		}

		if (isOverride)
			DevMsg($"SoundEmitter: adding map sound overrides from {filename} [{newOverrideCount} total, {replaceCount} replacements, {duplicatedReplacements} duplicated replacements]\n");

		Assert(scriptindex >= 0);
	}

	static void AccumulateFileNameAndTimestampIntoChecksum(ref CRC32_t crc, ReadOnlySpan<char> filename) {
		long ft = filesystem.GetFileTime(filename, "GAME").Ticks;
		CRC32.ProcessBuffer(ref crc, in ft);
		CRC32.ProcessBuffer(ref crc, filename);
	}

	private bool InternalModInit() {
		LoadGlobalActors();

		ManifestPlusScriptChecksum = 0u;

		CRC32_t crc = default;
		CRC32.Init(ref crc);

		KeyValues manifest = new KeyValues(MANIFEST_FILE);
		if (filesystem.LoadKeyValues(manifest, IFileSystem.KeyValuesPreloadType.SoundEmitter, MANIFEST_FILE, "GAME")) {
			AccumulateFileNameAndTimestampIntoChecksum(ref crc, MANIFEST_FILE);

			for (KeyValues? sub = manifest.GetFirstSubKey(); sub != null; sub = sub.GetNextKey()) {
				if (0 == stricmp(sub.Name, "precache_file")) {
					AccumulateFileNameAndTimestampIntoChecksum(ref crc, sub.GetString());

					// Add and always precache
					AddSoundsFromFile(sub.GetString(), false);
					continue;
				}
				else if (0 == stricmp(sub.Name, "preload_file")) {
					AccumulateFileNameAndTimestampIntoChecksum(ref crc, sub.GetString());

					// Add and always precache
					AddSoundsFromFile(sub.GetString(), true);
					continue;
				}
				else if (0 == stricmp(sub.Name, "faceposer_file"))
					// do nothing for these files; they're only used for faceposer
					continue;


				Warning($"SoundEmitterSystemBase.BaseInit:  Manifest '{MANIFEST_FILE}' with bogus file type '{sub.Name}', expecting 'declare_file' or 'precache_file'\n");
			}
		}
		else
			Error("Unable to load manifest file '%s'\n", MANIFEST_FILE);

		CRC32.Final(ref crc);
		ManifestPlusScriptChecksum = crc;

		// Only print total once, on server
		DevMsg(1, $"SoundEmitterSystem:  Registered {Sounds.Count} sounds\n");

		return true;
	}

	private void LoadGlobalActors() {
		// Now load the global actor list from the scripts/globalactors.txt file
		KeyValues? allActors = new KeyValues("allactors");
		if (allActors.LoadFromFile(filesystem, "scripts/global_actors.txt", null)) {
			KeyValues? pvkActor;
			for (pvkActor = allActors.GetFirstSubKey(); pvkActor != null; pvkActor = pvkActor.GetNextKey()) {
				UtlSymId_t actorNameHash = pvkActor.Name.Hash();
				if (!m_ActorGenders.TryGetValue(actorNameHash, out _)) ;
				if (m_ActorGenders.Count() > 254) {
					Warning("Exceeded max number of actors in scripts/global_actors.txt\n");
					break;
				}

				Gender gender = Gender.None;
				if (0 == stricmp(pvkActor.GetString(), "male"))
					gender = Gender.Male;
				else if (0 == stricmp(pvkActor.GetString(), "female"))
					gender = Gender.Female;
				ActorGenders[actorNameHash] = gender;
			}
		}
	}

	public void ModShutdown() {
		if (--InitCount > 0)
			return;
		InternalModShutdown();
	}

	private void InternalModShutdown() {
		int i;
		SoundKeyValues.Clear();

		Sounds.Clear();
		SoundEntryLookup.Clear();
		HandleToSound.Clear();
		CurrentHandle = 0;

		SavedOverrides.Clear();
		SavedOverrides.Clear();
		Waves.Clear();
		ActorGenders.Clear();
	}

	public void MoveSound(ReadOnlySpan<char> soundname, ReadOnlySpan<char> newscript) {
		int idx = GetSoundIndex(soundname);
		if (!IsValidIndex(idx)) {
			Warning($"Can't move '{soundname}', no such sound!\n");
			return;
		}

		ref SoundEntry entry = ref HandleToSound[idx].ValueRef;
		int oldscriptindex = entry.ScriptFileIndex;
		if (oldscriptindex < 0 || oldscriptindex >= SoundKeyValues.Count) {
			Assert(0);
			return;
		}

		int newscriptindex = FindSoundScript(newscript);
		if (newscriptindex == -1) {
			Warning($"CSoundEmitterSystemBase::MoveSound( '{soundname}', '{newscript}' ), script file not list in manifest '{MANIFEST_FILE}'\n");
			return;
		}

		// No actual change
		if (oldscriptindex == newscriptindex)
			return;

		// Move it
		entry.ScriptFileIndex = newscriptindex;

		// Mark both scripts as dirty
		SoundKeyValues.AsSpan()[oldscriptindex].Dirty = true;
		SoundKeyValues.AsSpan()[newscriptindex].Dirty = true;
	}

	public int Next(int i) {
		if (!HandleToSound.TryGetValue(i, out var ptr))
			return -1;

		return ptr.Next == null ? -1 : SoundToHandle[ptr.Next];
	}

	public void RemoveSound(ReadOnlySpan<char> soundname) {
		int idx = GetSoundIndex(soundname);
		if (!IsValidIndex(idx)) {
			Warning($"Can't remove {soundname}, no such sound!\n");
			return;
		}

		ref SoundEntry entry = ref HandleToSound[idx].ValueRef;
		entry.Removed = true;

		// Mark script as dirty
		int scriptindex = entry.ScriptFileIndex;
		if (scriptindex < 0 || scriptindex >= SoundKeyValues.Count) {
			Assert(0);
			return;
		}

		SoundKeyValues.AsSpan()[scriptindex].Dirty = true;
	}

	public void RenameSound(ReadOnlySpan<char> soundname, ReadOnlySpan<char> newname) {
		if (0 == stricmp(soundname, newname))
			return;

		int oldindex = GetSoundIndex(soundname);
		if (!IsValidIndex(oldindex)) {
			Msg($"Can't rename {soundname}, no such sound\n");
			return;
		}

		int check = GetSoundIndex(newname);
		if (IsValidIndex(check)) {
			Msg($"Can't rename {soundname} to {newname}, new name already in list\n");
			return;
		}

		// Copy out old entry
		LinkedListNode<SoundEntry> entryNode = HandleToSound[oldindex];
		ref SoundEntry entry = ref entryNode.ValueRef;
		// Remove it
		Sounds.Remove(entryNode);
		entry.Name = newname;
		// Re-insert in new spot
		Sounds.AddLast(entryNode);
		SoundEntryLookup[entry] = entryNode;

		// Mark associated script as dirty
		SoundKeyValues.AsSpan()[entry.ScriptFileIndex].Dirty = true;
	}

	public void SaveChangesToSoundScript(int scriptindex) {
		throw new NotImplementedException();
	}

	public void UpdateSoundParameters(ReadOnlySpan<char> soundname, in SoundParametersInternal parms) {
		int idx = GetSoundIndex(soundname);
		if (!IsValidIndex(idx)) {
			Msg($"Can't UpdateSoundParameters {soundname}, no such sound\n");
			return;
		}

		ref SoundEntry entry = ref HandleToSound[idx].ValueRef;

		if (entry.SoundParams.Equals(parms)) {
			// No changes
			return;
		}

		// Update parameters
		entry.SoundParams.CopyFrom(in parms);
		// Set dirty flag
		SoundKeyValues.AsSpan()[entry.ScriptFileIndex].Dirty = true;
	}
}
