using Source.Common.Hashing;
using Source.Common.Mathematics;
using Source.Common.Utilities;

using System.Globalization;
using System.Numerics;
using System.Text;

namespace Source.Common;

public struct EmphasisSample
{
	public float Time;
	public float Value;

	// Used by editors only
	public bool Selected;

	public void SetSelected(bool isSelected) {
		Selected = isSelected;
	}
}

public class BasePhonemeTag
{
	float startTime;
	float endTime;
	ushort phonemeCode;

	public BasePhonemeTag() {
		startTime = 0;
		endTime = 0;

		phonemeCode = 0;
	}

	public BasePhonemeTag(BasePhonemeTag from) {
		CopyFrom(from);
	}

	public void CopyFrom(BasePhonemeTag from) {
		startTime = from.startTime;
		endTime = from.endTime;
		phonemeCode = from.phonemeCode;
	}

	public float GetStartTime() => startTime;
	public void SetStartTime(float startTime) => this.startTime = startTime;
	public void AddStartTime(float startTime) => this.startTime += startTime;

	public float GetEndTime() => endTime;
	public void SetEndTime(float endTime) => this.endTime = endTime;
	public void AddEndTime(float startTime) => endTime += startTime;

	public int GetPhonemeCode() => phonemeCode;
	public void SetPhonemeCode(int phonemeCode) => this.phonemeCode = (ushort)phonemeCode;
}

public class PhonemeTag : BasePhonemeTag
{
	public bool Selected;
	public uint StartByte;
	public uint EndByte;

	string? phoneme;

	public PhonemeTag() {
		phoneme = null;

		SetStartAndEndBytes(0, 0);

		SetSelected(false);
	}

	public PhonemeTag(PhonemeTag from) : base(from) {
		SetStartAndEndBytes(from.GetStartByte(), from.GetEndByte());

		SetSelected(from.GetSelected());

		phoneme = null;
		SetTag(from.GetTag());
	}

	public PhonemeTag(ReadOnlySpan<char> phoneme) {
		SetStartAndEndBytes(0, 0);

		SetStartTime(0.0f);
		SetEndTime(0.0f);

		SetSelected(false);

		SetPhonemeCode(0);

		this.phoneme = null;
		SetTag(phoneme);
	}

	public void SetTag(ReadOnlySpan<char> phoneme) {
		this.phoneme = null;
		if (phoneme.IsEmpty || phoneme[0] == '\0')
			return;

		this.phoneme = new string(phoneme.SliceNullTerminatedString());
	}

	public ReadOnlySpan<char> GetTag() => phoneme ?? "";

	public uint ComputeDataCheckSum() {
		CRC32_t crc = 0;
		CRC32.Init(ref crc);

		// Checksum the text
		CRC32.ProcessBuffer(ref crc, Encoding.ASCII.GetBytes(phoneme ?? "").AsSpan());
		int phonemeCode = GetPhonemeCode();
		CRC32.ProcessBuffer(ref crc, in phonemeCode);

		// Checksum timestamps
		float startTime = GetStartTime();
		float endTime = GetEndTime();
		CRC32.ProcessBuffer(ref crc, in startTime);
		CRC32.ProcessBuffer(ref crc, in endTime);

		CRC32.Final(ref crc);

		return crc;
	}

	public void SetSelected(bool isSelected) => Selected = isSelected;
	public bool GetSelected() => Selected;
	public void SetStartAndEndBytes(uint start, uint end) {
		StartByte = start;
		EndByte = end;
	}
	public uint GetStartByte() => StartByte;
	public uint GetEndByte() => EndByte;
}

public class WordTag
{
	public float StartTime;
	public float EndTime;

	public readonly List<PhonemeTag> Phonemes = [];
	public bool Selected;
	public uint StartByte;
	public uint EndByte;

	string? word;

	public WordTag() {
		word = null;

		SetStartAndEndBytes(0, 0);

		StartTime = 0.0f;
		EndTime = 0.0f;

		SetSelected(false);
	}

	public WordTag(WordTag from) {
		word = null;
		SetWord(from.word);

		SetStartAndEndBytes(from.GetStartByte(), from.GetEndByte());

		StartTime = from.StartTime;
		EndTime = from.EndTime;

		SetSelected(from.GetSelected());

		foreach (PhonemeTag p in from.Phonemes)
			Phonemes.Add(new PhonemeTag(p));
	}

	public WordTag(ReadOnlySpan<char> word) {
		SetStartAndEndBytes(0, 0);

		StartTime = 0.0f;
		EndTime = 0.0f;

		this.word = null;

		SetSelected(false);

		SetWord(word);
	}

	public int IndexOfPhoneme(PhonemeTag tag) {
		int i = 0;
		foreach (PhonemeTag p in Phonemes) {
			if (p == tag)
				return i;
			++i;
		}
		return -1;
	}

	public void SetWord(ReadOnlySpan<char> word) {
		this.word = null;
		if (word.IsEmpty || word[0] == '\0')
			return;

		this.word = new string(word.SliceNullTerminatedString());
	}

	public ReadOnlySpan<char> GetWord() => word ?? "";

	public uint ComputeDataCheckSum() {
		CRC32_t crc = 0;
		CRC32.Init(ref crc);

		// Checksum the text
		if (word != null)
			CRC32.ProcessBuffer(ref crc, Encoding.ASCII.GetBytes(word).AsSpan());
		// Checksum phonemes
		foreach (PhonemeTag p in Phonemes) {
			uint phonemeCheckSum = p.ComputeDataCheckSum();
			CRC32.ProcessBuffer(ref crc, in phonemeCheckSum);
		}
		// Checksum timestamps
		CRC32.ProcessBuffer(ref crc, in StartTime);
		CRC32.ProcessBuffer(ref crc, in EndTime);

		CRC32.Final(ref crc);

		return crc;
	}

	public void SetSelected(bool isSelected) => Selected = isSelected;
	public bool GetSelected() => Selected;
	public void SetStartAndEndBytes(uint start, uint end) {
		StartByte = start;
		EndByte = end;
	}
	public uint GetStartByte() => StartByte;
	public uint GetEndByte() => EndByte;
}

// Close Captioning Support
// The phonemes drive the mouth in english, but the CC text can
//  be one of several languages
public enum CCLanguageType
{
	CC_ENGLISH = 0,
	CC_FRENCH,
	CC_GERMAN,
	CC_ITALIAN,
	CC_KOREAN,
	CC_SCHINESE,  // Simplified Chinese
	CC_SPANISH,
	CC_TCHINESE,  // Traditional Chinese
	CC_JAPANESE,
	CC_RUSSIAN,
	CC_THAI,
	CC_PORTUGUESE,
	// etc etc

	CC_NUM_LANGUAGES
}

public class Sentence
{
	public const int CACHED_SENTENCE_VERSION = 1;
	public const int CACHED_SENTENCE_VERSION_ALIGNED = 4;

	record struct CCLanguage(CCLanguageType Type, string Name, byte R, byte G, byte B);  // For faceposer, indicator color for this language

	static readonly CCLanguage[] g_CCLanguageLookup = [
		new(CCLanguageType.CC_ENGLISH, "english", 0, 0, 0),
		new(CCLanguageType.CC_FRENCH, "french", 150, 0, 0),
		new(CCLanguageType.CC_GERMAN, "german", 0, 150, 0),
		new(CCLanguageType.CC_ITALIAN, "italian", 0, 150, 150),
		new(CCLanguageType.CC_KOREAN, "koreana", 150, 0, 150),
		new(CCLanguageType.CC_SCHINESE, "schinese", 150, 0, 150),
		new(CCLanguageType.CC_SPANISH, "spanish", 0, 0, 150),
		new(CCLanguageType.CC_TCHINESE, "tchinese", 150, 0, 150),
		new(CCLanguageType.CC_JAPANESE, "japanese", 250, 150, 0),
		new(CCLanguageType.CC_RUSSIAN, "russian", 0, 250, 150),
		new(CCLanguageType.CC_THAI, "thai", 0, 150, 250),
		new(CCLanguageType.CC_PORTUGUESE, "portuguese", 0, 0, 150),
	];

	public string? Text;

	public readonly List<WordTag> Words = [];
	public readonly List<BasePhonemeTag> RunTimePhonemes = [];

	public int ResetWordBase;
	// Phoneme emphasis data
	public readonly List<EmphasisSample> EmphasisSamples = [];

	public uint CheckSum;
	public bool IsValid;
	public bool StoreCheckSum;
	public bool ShouldVoiceDuck;
	public bool IsCached;

	public static void ColorForLanguage(int language, out byte r, out byte g, out byte b) {
		r = g = b = 0;

		if (language < 0 || language >= (int)CCLanguageType.CC_NUM_LANGUAGES)
			return;

		r = g_CCLanguageLookup[language].R;
		g = g_CCLanguageLookup[language].G;
		b = g_CCLanguageLookup[language].B;
	}

	public static ReadOnlySpan<char> NameForLanguage(int language) {
		if (language < 0 || language >= (int)CCLanguageType.CC_NUM_LANGUAGES)
			return "unknown_language";

		ref CCLanguage entry = ref g_CCLanguageLookup[language];
		Assert((int)entry.Type == language);
		return entry.Name;
	}

	public static int LanguageForName(ReadOnlySpan<char> name) {
		for (int l = 0; l < (int)CCLanguageType.CC_NUM_LANGUAGES; l++) {
			ref CCLanguage entry = ref g_CCLanguageLookup[l];
			Assert((int)entry.Type == l);
			if (stricmp(entry.Name, name) == 0)
				return l;
		}
		return -1;
	}

	public Sentence() {
		ResetWordBase = 0;
		Text = null;
		CheckSum = 0;
		ShouldVoiceDuck = false;
		StoreCheckSum = false;
		IsValid = false;
		IsCached = false;
	}

	static float StrToF(ReadOnlySpan<char> token) {
		token = token.SliceNullTerminatedString();
		int end = 0;
		while (end < token.Length && (char.IsDigit(token[end]) || token[end] is '.' or '-' or '+' or 'e' or 'E'))
			end++;
		return float.TryParse(token[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : 0.0f;
	}

	static int AToI(ReadOnlySpan<char> token) {
		token = token.SliceNullTerminatedString();
		int end = 0;
		if (end < token.Length && (token[end] == '-' || token[end] == '+'))
			end++;
		while (end < token.Length && char.IsDigit(token[end]))
			end++;
		return int.TryParse(token[..end], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int result) ? result : 0;
	}

	void ParsePlaintext(UtlBuffer buf) {
		Span<char> token = stackalloc char[4096];
		StringBuilder text = new();
		while (true) {
			buf.GetString(token);
			if (token[0] == '}')
				break;

			text.Append(token.SliceNullTerminatedString());
			text.Append(' ');
		}

		SetText(text.ToString());
	}

	void ParseWords(UtlBuffer buf) {
		Span<char> token = stackalloc char[4096];
		Span<char> word = stackalloc char[256];
		Span<char> phonemename = stackalloc char[256];

		while (true) {
			buf.GetString(token);
			if (token[0] == '}')
				break;

			if (stricmp(token.SliceNullTerminatedString(), "WORD") != 0)
				break;

			buf.GetString(token);
			strcpy(word, token.SliceNullTerminatedString());

			buf.GetString(token);
			float start = StrToF(token);
			buf.GetString(token);
			float end = StrToF(token);

			WordTag wt = new WordTag(word);
			wt.StartTime = start;
			wt.EndTime = end;

			AddWordTag(wt);

			buf.GetString(token);
			if (token[0] != '{')
				break;

			while (true) {
				buf.GetString(token);
				if (token[0] == '}')
					break;

				// Parse phoneme
				int code = AToI(token);
				buf.GetString(token);
				strcpy(phonemename, token.SliceNullTerminatedString());
				buf.GetString(token);
				start = StrToF(token);
				buf.GetString(token);
				end = StrToF(token);
				buf.GetString(token);
				float volume = StrToF(token);

				PhonemeTag pt = new PhonemeTag();
				pt.SetPhonemeCode(code);
				pt.SetTag(phonemename);
				pt.SetStartTime(start);
				pt.SetEndTime(end);

				AddPhonemeTag(wt, pt);
			}
		}
	}

	void ParseEmphasis(UtlBuffer buf) {
		Span<char> token = stackalloc char[4096];
		Span<char> t = stackalloc char[256];
		Span<char> value = stackalloc char[256];
		while (true) {
			buf.GetString(token);
			if (token[0] == '}')
				break;

			strcpy(t, token.SliceNullTerminatedString());
			buf.GetString(token);

			strcpy(value, token.SliceNullTerminatedString());

			EmphasisSample sample = default;
			sample.SetSelected(false);
			sample.Time = StrToF(t);
			sample.Value = StrToF(value);


			EmphasisSamples.Add(sample);

		}
	}

	// This is obsolete, so it doesn't do anything with the data which is parsed.
	void ParseCloseCaption(UtlBuffer buf) {
		Span<char> token = stackalloc char[4096];
		Span<char> cc_type = stackalloc char[32];
		Span<byte> cc_stream = stackalloc byte[4096];
		while (true) {
			// Format is
			// language_name
			// {
			//   PHRASE char streamlength "streambytes" starttime endtime
			//   PHRASE unicode streamlength "streambytes" starttime endtime
			// }
			buf.GetString(token);
			if (token[0] == '}')
				break;

			buf.GetString(token);
			if (token[0] != '{')
				break;

			buf.GetString(token);
			while (true) {
				if (token[0] == '}')
					break;

				if (stricmp(token.SliceNullTerminatedString(), "PHRASE") != 0)
					break;

				int cc_length;

				buf.GetString(token);
				strcpy(cc_type, token.SliceNullTerminatedString());

				bool unicode = false;
				if (stricmp(cc_type.SliceNullTerminatedString(), "unicode") == 0)
					unicode = true;
				else if (stricmp(cc_type.SliceNullTerminatedString(), "char") != 0)
					Assert(false);

				buf.GetString(token);
				cc_length = AToI(token);
				Assert(cc_length >= 0 && cc_length < cc_stream.Length);
				// Skip space
				buf.GetChar();
				buf.Get(cc_stream[..cc_length]);
				cc_stream[cc_length] = 0;

				// Skip space
				buf.GetChar();
				buf.GetString(token);
				buf.GetString(token);

				buf.GetString(token);
			}
		}
	}

	void ParseOptions(UtlBuffer buf) {
		Span<char> token = stackalloc char[4096];
		Span<char> key = stackalloc char[256];
		Span<char> value = stackalloc char[256];
		while (true) {
			buf.GetString(token);
			if (token[0] == '\0' || token[0] == '}')
				break;

			strcpy(key, token.SliceNullTerminatedString());
			buf.GetString(token);
			strcpy(value, token.SliceNullTerminatedString());

			if (stricmp(key.SliceNullTerminatedString(), "voice_duck") == 0)
				SetVoiceDuck(AToI(value) != 0);
			else if (stricmp(key.SliceNullTerminatedString(), "checksum") == 0)
				SetDataCheckSum((uint)AToI(value));
		}
	}

	void ParseDataVersionOnePointZero(UtlBuffer buf) {
		Span<char> token = stackalloc char[4096];
		Span<char> section = stackalloc char[256];

		while (true) {
			buf.GetString(token);
			if (token[0] == '\0')
				break;

			// end of block, return
			if (token[0] == '}')
				break;

			strcpy(section, token.SliceNullTerminatedString());

			buf.GetString(token);
			if (token[0] != '{')
				break;

			ReadOnlySpan<char> sectionName = section.SliceNullTerminatedString();
			if (stricmp(sectionName, "PLAINTEXT") == 0)
				ParsePlaintext(buf);
			else if (stricmp(sectionName, "WORDS") == 0)
				ParseWords(buf);
			else if (stricmp(sectionName, "EMPHASIS") == 0)
				ParseEmphasis(buf);
			else if (stricmp(sectionName, "CLOSECAPTION") == 0) {
				// NOTE:  CLOSECAPTION IS NO LONGER VALID
				// This just skips the section of data.
				ParseCloseCaption(buf);
			}
			else if (stricmp(sectionName, "OPTIONS") == 0)
				ParseOptions(buf);
		}
	}

	// This is a compressed save of just the data needed to drive phonemes in the engine (no word / sentence text, etc )
	public void CacheSaveToBuffer(UtlBuffer buf, int version) {
		Assert(!buf.IsText());
		Assert(IsCached);

		int i;
		ushort pcount = (ushort)GetRuntimePhonemeCount();

		// header
		if (version == CACHED_SENTENCE_VERSION_ALIGNED) {
			buf.PutChar((char)CACHED_SENTENCE_VERSION_ALIGNED);
			buf.PutChar((char)0);
			buf.PutChar((char)0);
			buf.PutChar((char)0);
			buf.PutInt(pcount);
		}
		else {
			buf.PutChar((char)version);
			buf.PutShort((short)pcount);
		}

		// phoneme
		if (version == CACHED_SENTENCE_VERSION_ALIGNED) {
			for (i = 0; i < pcount; ++i) {
				BasePhonemeTag phoneme = GetRuntimePhoneme(i);
				Assert(phoneme);
				buf.PutInt(phoneme.GetPhonemeCode());
				buf.PutFloat(phoneme.GetStartTime());
				buf.PutFloat(phoneme.GetEndTime());
			}
		}
		else {
			for (i = 0; i < pcount; ++i) {
				BasePhonemeTag phoneme = GetRuntimePhoneme(i);
				Assert(phoneme);
				buf.PutShort((short)phoneme.GetPhonemeCode());
				buf.PutFloat(phoneme.GetStartTime());
				buf.PutFloat(phoneme.GetEndTime());
			}
		}

		// emphasis samples and voice duck
		int c = EmphasisSamples.Count;
		Assert(c <= 32767);

		if (version == CACHED_SENTENCE_VERSION_ALIGNED) {
			buf.PutInt(c);
			foreach (EmphasisSample sample in EmphasisSamples) {
				buf.PutFloat(sample.Time);
				buf.PutFloat(sample.Value);
			}
			buf.PutInt(GetVoiceDuck() ? 1 : 0);
		}
		else {
			buf.PutShort((short)c);
			foreach (EmphasisSample sample in EmphasisSamples) {
				buf.PutFloat(sample.Time);
				short scaledValue = Math.Clamp((short)(sample.Value * 32767), (short)0, (short)32767);
				buf.PutShort(scaledValue);
			}
			buf.PutChar((char)(GetVoiceDuck() ? 1 : 0));
		}
	}

	public void CacheRestoreFromBuffer(UtlBuffer buf) {
		Assert(!buf.IsText());

		Reset();

		IsCached = true;

		// determine format
		int version = buf.GetChar();
		if (version != CACHED_SENTENCE_VERSION && version != CACHED_SENTENCE_VERSION_ALIGNED) {
			// Uh oh, version changed...
			IsValid = false;
			return;
		}

		ushort pcount;
		if (version == CACHED_SENTENCE_VERSION_ALIGNED) {
			buf.GetChar();
			buf.GetChar();
			buf.GetChar();
			pcount = (ushort)buf.GetInt();
		}
		else
			pcount = (ushort)buf.GetShort();

		RunTimePhonemes.EnsureCapacity(pcount);

		// phonemes
		PhonemeTag pt = new();
		if (version == CACHED_SENTENCE_VERSION_ALIGNED) {
			for (int i = 0; i < pcount; ++i) {
				int code = buf.GetInt();
				float st = buf.GetFloat();
				float et = buf.GetFloat();

				pt.SetPhonemeCode(code);
				pt.SetStartTime(st);
				pt.SetEndTime(et);
				AddRuntimePhoneme(pt);
			}
		}
		else {
			for (int i = 0; i < pcount; ++i) {
				ushort code = (ushort)buf.GetShort();
				float st = buf.GetFloat();
				float et = buf.GetFloat();

				pt.SetPhonemeCode(code);
				pt.SetStartTime(st);
				pt.SetEndTime(et);
				AddRuntimePhoneme(pt);
			}
		}

		// emphasis samples and voice duck
		if (version == CACHED_SENTENCE_VERSION_ALIGNED) {
			int c = buf.GetInt();
			EmphasisSamples.EnsureCapacity(c);
			for (int i = 0; i < c; i++) {
				EmphasisSample sample = default;
				sample.SetSelected(false);
				sample.Time = buf.GetFloat();
				sample.Value = buf.GetFloat();
				EmphasisSamples.Add(sample);
			}
			SetVoiceDuck(buf.GetInt() != 0);
		}
		else {
			int c = buf.GetShort();
			EmphasisSamples.EnsureCapacity(c);
			for (int i = 0; i < c; i++) {
				EmphasisSample sample = default;
				sample.SetSelected(false);
				sample.Time = buf.GetFloat();
				sample.Value = (float)buf.GetShort() / 32767.0f;
				EmphasisSamples.Add(sample);
			}
			SetVoiceDuck(buf.GetChar() != 0);
		}

		IsValid = true;
	}

	public int GetRuntimePhonemeCount() => RunTimePhonemes.Count;

	public BasePhonemeTag GetRuntimePhoneme(int i) {
		Assert(IsCached);
		return RunTimePhonemes[i];
	}

	public void ClearRuntimePhonemes() {
		RunTimePhonemes.Clear();
	}

	public void AddRuntimePhoneme(PhonemeTag src) {
		Assert(IsCached);

		BasePhonemeTag tag = new BasePhonemeTag();
		tag.CopyFrom(src);

		RunTimePhonemes.Add(tag);
	}

	// This strips out all of the stuff used by the editor, leaving just one blank work, no sentence text, and just
	// the phonemes without the phoneme text...(same as the cacherestore version below)
	public void MakeRuntimeOnly() {
		IsCached = true;
		Text = null;

		foreach (WordTag word in Words) {
			Assert(word);
			foreach (PhonemeTag phoneme in word.Phonemes) {
				Assert(phoneme);
				AddRuntimePhoneme(phoneme);
			}
		}

		// Remove all existing words
		Words.Clear();
		IsValid = true;
	}

	public void SaveToBuffer(UtlBuffer buf) {
		Assert(!IsCached);

		buf.Printf("VERSION 1.0\n");

		buf.Printf("PLAINTEXT\n");
		buf.Printf("{\n");
		buf.Printf($"{GetText()}\n");
		buf.Printf("}\n");
		buf.Printf("WORDS\n");
		buf.Printf("{\n");
		foreach (WordTag word in Words) {
			Assert(word);
			buf.Printf(string.Create(CultureInfo.InvariantCulture, $"WORD {word.GetWord()} {word.StartTime:F3} {word.EndTime:F3}\n"));

			buf.Printf("{\n");
			foreach (PhonemeTag phoneme in word.Phonemes) {
				Assert(phoneme);
				buf.Printf(string.Create(CultureInfo.InvariantCulture, $"{phoneme.GetPhonemeCode()} {phoneme.GetTag()} {phoneme.GetStartTime():F3} {phoneme.GetEndTime():F3} 1\n"));
			}

			buf.Printf("}\n");
		}
		buf.Printf("}\n");
		buf.Printf("EMPHASIS\n");
		buf.Printf("{\n");
		foreach (EmphasisSample sample in EmphasisSamples)
			buf.Printf(string.Create(CultureInfo.InvariantCulture, $"{sample.Time:F6} {sample.Value:F6}\n"));

		buf.Printf("}\n");
		buf.Printf("OPTIONS\n");
		buf.Printf("{\n");
		buf.Printf($"voice_duck {(GetVoiceDuck() ? 1 : 0)}\n");
		if (StoreCheckSum)
			buf.Printf($"checksum {(int)CheckSum}\n");
		buf.Printf("}\n");
	}

	public void InitFromDataChunk(ReadOnlySpan<byte> data) {
		UtlBuffer buf = new(0, 0, UtlBuffer.BufferFlags.TextBuffer);
		buf.EnsureCapacity(data.Length);
		buf.Put(data);
		buf.SeekPut(SeekOrigin.Begin, data.Length);

		InitFromBuffer(buf);
	}

	public void InitFromBuffer(UtlBuffer buf) {
		Assert(buf.IsText());

		Reset();

		Span<char> token = stackalloc char[4096];
		buf.GetString(token);

		if (stricmp(token.SliceNullTerminatedString(), "VERSION") != 0)
			return;

		buf.GetString(token);
		if (StrToF(token) == 1.0f) {
			ParseDataVersionOnePointZero(buf);
			IsValid = true;
		}
		else {
			Assert(false);
			return;
		}
	}

	public int GetWordBase() => ResetWordBase;

	public void ResetToBase() {
		// Delete everything after m_nResetWordBase
		while (Words.Count > ResetWordBase)
			Words.RemoveAt(Words.Count - 1);

		ClearRuntimePhonemes();
	}

	public void MarkNewPhraseBase() {
		ResetWordBase = Math.Max(Words.Count, 0);
	}

	public void Reset() {
		ResetWordBase = 0;

		Words.Clear();

		EmphasisSamples.Clear();

		ClearRuntimePhonemes();
	}

	public void AddPhonemeTag(WordTag word, PhonemeTag tag) {
		word.Phonemes.Add(tag);
	}

	public void AddWordTag(WordTag tag) {
		Words.Add(tag);
	}

	public int CountPhonemes() {
		int c = 0;
		foreach (WordTag word in Words)
			c += word.Phonemes.Count;
		return c;
	}

	public WordTag? EstimateBestWord(float time) {
		WordTag? bestWord = null;

		foreach (WordTag word in Words) {
			if (word == null)
				continue;

			if (word.StartTime <= time && word.EndTime >= time)
				return word;

			if (time < word.StartTime)
				bestWord = word;

			if (time > word.EndTime && bestWord != null)
				return bestWord;
		}

		// return best word if we found one
		if (bestWord != null)
			return bestWord;

		// Return last word
		if (Words.Count >= 1)
			return Words[Words.Count - 1];

		// Oh well
		return null;
	}

	public WordTag? GetWordForPhoneme(PhonemeTag phoneme) {
		foreach (WordTag word in Words) {
			if (word == null)
				continue;

			foreach (PhonemeTag p in word.Phonemes) {
				if (p == phoneme)
					return word;
			}

		}
		return null;
	}

	public Sentence CopyFrom(Sentence src) {
		// Clear current stuff
		Reset();

		// Copy everything
		foreach (WordTag word in src.Words)
			AddWordTag(new WordTag(word));

		SetText(src.GetText());
		ResetWordBase = src.ResetWordBase;

		foreach (EmphasisSample s in src.EmphasisSamples)
			EmphasisSamples.Add(s);

		IsCached = src.IsCached;

		int c = src.GetRuntimePhonemeCount();
		for (int i = 0; i < c; i++) {
			Assert(IsCached);

			BasePhonemeTag tag = src.GetRuntimePhoneme(i);
			PhonemeTag full = new();
			full.CopyFrom(tag);

			AddRuntimePhoneme(full);
		}

		ShouldVoiceDuck = src.ShouldVoiceDuck;
		StoreCheckSum = src.StoreCheckSum;
		CheckSum = src.CheckSum;
		IsValid = src.IsValid;

		return this;
	}

	public void Append(float starttime, Sentence src) {
		// Combine
		foreach (WordTag word in src.Words) {
			WordTag newWord = new WordTag(word);

			newWord.StartTime += starttime;
			newWord.EndTime += starttime;

			// Offset times
			foreach (PhonemeTag tag in newWord.Phonemes) {
				tag.AddStartTime(starttime);
				tag.AddEndTime(starttime);
			}

			AddWordTag(newWord);
		}

		if (!src.GetText().IsEmpty) {
			if (!GetText().IsEmpty)
				SetText($"{GetText()} {src.GetText()}");
			else
				SetText(src.GetText());
		}

		foreach (EmphasisSample s in src.EmphasisSamples) {
			EmphasisSample sample = s;
			sample.Time += starttime;

			EmphasisSamples.Add(sample);
		}

		// Or in voice duck settings
		ShouldVoiceDuck |= src.ShouldVoiceDuck;
	}

	public void SetText(ReadOnlySpan<char> text) {
		Text = null;

		if (text.IsEmpty || text[0] == '\0')
			return;

		Text = new string(text.SliceNullTerminatedString());
	}

	public ReadOnlySpan<char> GetText() => Text ?? "";

	public void SetTextFromWords() {
		StringBuilder fulltext = new();
		for (int i = 0; i < Words.Count; i++) {
			WordTag word = Words[i];

			fulltext.Append(word.GetWord());

			if (i != Words.Count)
				fulltext.Append(' ');
		}

		SetText(fulltext.ToString());
	}

	public void Resort() {
		int c = EmphasisSamples.Count;
		for (int i = 0; i < c; i++) {
			for (int j = i + 1; j < c; j++) {
				EmphasisSample src = EmphasisSamples[i];
				EmphasisSample dest = EmphasisSamples[j];

				if (src.Time > dest.Time)
					(EmphasisSamples[i], EmphasisSamples[j]) = (EmphasisSamples[j], EmphasisSamples[i]);
			}
		}
	}

	static EmphasisSample nullstart;
	static EmphasisSample nullend;

	public EmphasisSample GetBoundedSample(int number, float endtime) {
		// Search for two samples which span time f
		nullstart.Time = 0.0f;
		nullstart.Value = 0.5f;
		nullend.Time = endtime;
		nullend.Value = 0.5f;

		if (number < 0)
			return nullstart;
		else if (number >= GetNumSamples())
			return nullend;

		return GetSample(number)!.Value;
	}

	public float GetIntensity(float time, float endtime) {
		float zeroValue = 0.5f;

		int c = GetNumSamples();

		if (c <= 0)
			return zeroValue;

		int i;
		for (i = -1; i < c; i++) {
			EmphasisSample s = GetBoundedSample(i, endtime);
			EmphasisSample n = GetBoundedSample(i + 1, endtime);

			if (time >= s.Time && time <= n.Time)
				break;
		}

		int prev = i - 1;
		int start = i;
		int end = i + 1;
		int next = i + 2;

		prev = Math.Max(-1, prev);
		start = Math.Max(-1, start);
		end = Math.Min(end, GetNumSamples());
		next = Math.Min(next, GetNumSamples());

		EmphasisSample esPre = GetBoundedSample(prev, endtime);
		EmphasisSample esStart = GetBoundedSample(start, endtime);
		EmphasisSample esEnd = GetBoundedSample(end, endtime);
		EmphasisSample esNext = GetBoundedSample(next, endtime);

		float dt = esEnd.Time - esStart.Time;
		dt = Math.Clamp(dt, 0.01f, 1.0f);

		Vector3 vPre = new(esPre.Time, esPre.Value, 0);
		Vector3 vStart = new(esStart.Time, esStart.Value, 0);
		Vector3 vEnd = new(esEnd.Time, esEnd.Value, 0);
		Vector3 vNext = new(esNext.Time, esNext.Value, 0);

		float f2 = (time - esStart.Time) / dt;
		f2 = Math.Clamp(f2, 0.0f, 1.0f);

		MathLib.Catmull_Rom_Spline(
			vPre,
			vStart,
			vEnd,
			vNext,
			f2,
			out Vector3 vOut);

		float retval = Math.Clamp(vOut.Y, 0.0f, 1.0f);
		return retval;
	}

	public int GetNumSamples() => EmphasisSamples.Count;

	public EmphasisSample? GetSample(int index) {
		if (index < 0 || index >= GetNumSamples())
			return null;

		return EmphasisSamples[index];
	}

	// Compute start and endtime based on all words
	public void GetEstimatedTimes(ref float start, ref float end) {
		float beststart = 100000.0f;
		float bestend = -100000.0f;

		int c = Words.Count;
		if (c == 0) {
			start = end = 0.0f;
			return;
		}

		foreach (WordTag w in Words) {
			Assert(w);
			if (w.StartTime < beststart)
				beststart = w.StartTime;
			if (w.EndTime > bestend)
				bestend = w.EndTime;
		}

		if (beststart == 100000.0f) {
			Assert(false);
			beststart = 0.0f;
		}
		if (bestend == -100000.0f) {
			Assert(false);
			bestend = 1.0f;
		}
		start = beststart;
		end = bestend;
	}

	public void SetVoiceDuck(bool shouldDuck) => ShouldVoiceDuck = shouldDuck;
	public bool GetVoiceDuck() => ShouldVoiceDuck;

	public void SetDataCheckSum(uint chk) {
		StoreCheckSum = true;
		CheckSum = chk;
	}

	public uint ComputeDataCheckSum() {
		CRC32_t crc = 0;
		CRC32.Init(ref crc);

		// Checksum the text
		CRC32.ProcessBuffer(ref crc, Encoding.ASCII.GetBytes(Text ?? "").AsSpan());
		// Checsum words and phonemes
		foreach (WordTag word in Words) {
			uint wordCheckSum = word.ComputeDataCheckSum();
			CRC32.ProcessBuffer(ref crc, in wordCheckSum);
		}

		// Checksum emphasis data
		foreach (EmphasisSample s in EmphasisSamples) {
			CRC32.ProcessBuffer(ref crc, in s.Time);
			CRC32.ProcessBuffer(ref crc, in s.Value);
		}

		CRC32.Final(ref crc);

		return crc;
	}

	public uint GetDataCheckSum() {
		Assert(StoreCheckSum);
		Assert(CheckSum != 0);
		return CheckSum;
	}

	const float STARTEND_TIMEGAP = 0.1F;

	public static int CountWords(ReadOnlySpan<char> str) {
		str = str.SliceNullTerminatedString();
		if (str.IsEmpty)
			return 0;

		int c = 1;

		int p = 0;
		while (p < str.Length) {
			if (str[p] <= 32) {
				c++;

				while (p < str.Length && str[p] <= 32)
					p++;
			}

			if (p >= str.Length)
				break;

			p++;
		}

		return c;
	}


	public static bool ShouldSplitWord(char @in) {
		if (@in <= 32)
			return true;

		if (@in > sbyte.MaxValue)
			return true;

		if (char.IsPunctuation(@in) || char.IsSymbol(@in)) {
			// don't split on apostrophe
			if (@in == '\'')
				return false;
			return true;
		}

		return false;
	}

	public void CreateEventWordDistribution(ReadOnlySpan<char> text, float sentenceDuration) {
		text = text.SliceNullTerminatedString();
		if (text.IsEmpty)
			return;

		int wordCount = CountWords(text);
		if (wordCount <= 0)
			return;

		float wordLength = (sentenceDuration - 2 * STARTEND_TIMEGAP) / (float)wordCount;
		float wordStart = STARTEND_TIMEGAP;

		Reset();

		StringBuilder word = new();
		int @in = 0;

		while (@in < text.Length) {
			if (!ShouldSplitWord(text[@in]))
				word.Append(text[@in++]);
			else {
				// Skip over splitters
				while (@in < text.Length && ShouldSplitWord(text[@in]))
					@in++;

				if (word.Length > 0) {
					WordTag w = new WordTag();
					w.SetWord(word.ToString());
					w.StartTime = wordStart;
					w.EndTime = wordStart + wordLength;

					AddWordTag(w);

					wordStart += wordLength;
				}

				word.Clear();
			}
		}

		if (word.Length > 0) {
			WordTag w = new WordTag();
			w.SetWord(word.ToString());
			w.StartTime = wordStart;
			w.EndTime = wordStart + wordLength;

			AddWordTag(w);

			wordStart += wordLength;
		}
	}
}
