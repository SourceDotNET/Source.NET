using Game.Shared;

using Source.Common.Audio;
using Source.Common.Commands;
using Source.Common.SoundEmitterSystem;

namespace Game.Server;

public enum SentenceCriteria
{
	Always = 0,
	Normal,
	InSquad,
	SquadLeader,
}

public enum SentencePriority
{
	Invalid = -1,
	Normal = 0,
	Medium = 1,
	High = 2,
}

public abstract class AI_SentenceBase : AI_Component
{
	public static readonly ConVar npc_sentences = new("npc_sentences", "0");

	protected AI_SentenceBase() => ClearQueue();

	public void SetVoicePitch(int voicePitch) => VoicePitch = voicePitch;
	public int GetVoicePitch() => VoicePitch;

	void SentenceMsg(ReadOnlySpan<char> status, ReadOnlySpan<char> sentence) {
		int mode = npc_sentences.GetInt();
		switch (mode) {
			case 0:
				return;

			case 1:
				DevMsg($"SENTENCE [{GetOuter()!.EntIndex()} {gpGlobals.CurTime:F2}] {status}: {sentence}\n");
				break;
		}
	}

	public void ClearQueue() => QueuedSentenceIndex = -1;

	public void UpdateSentenceQueue() {
		if (QueuedSentenceIndex == -1)
			return;

		if (QueueTimeout < gpGlobals.CurTime) {
			ClearQueue();
			return;
		}

		if (GetOuter()!.FOkToMakeSound(QueueSoundPriority)) {
			SENTENCEG_PlaySentenceIndex(GetOuter()!.Edict(), QueuedSentenceIndex, GetVolume(), GetSoundLevel(), 0, GetVoicePitch());

			ReadOnlySpan<char> sentenceName = engine.SentenceNameFromIndex(QueuedSentenceIndex);
			SentenceMsg("Speaking [from QUEUE]", sentenceName);

			GetOuter()!.JustMadeSound(QueueSoundPriority);
			ClearQueue();
		}
	}

	bool MatchesCriteria(SentenceCriteria criteria) {
		switch (criteria) {
			case SentenceCriteria.Always:
				return true;

			case SentenceCriteria.Normal:
				return (GetOuter()!.GetState() == NPCState.Combat) || (GetOuter()!.HasSpawnFlags(AI_BaseNPC.SF.Gag) == false);

			case SentenceCriteria.InSquad:
				if ((GetOuter()!.GetState() != NPCState.Combat) && GetOuter()!.HasSpawnFlags(AI_BaseNPC.SF.Gag))
					return false;
				return GetOuter()!.GetSquad() != null && (GetOuter()!.GetSquad()!.NumMembers() > 1);

			case SentenceCriteria.SquadLeader: {
					if ((GetOuter()!.GetState() != NPCState.Combat) && GetOuter()!.HasSpawnFlags(AI_BaseNPC.SF.Gag))
						return false;

					AI_Squad? squad = GetOuter()!.GetSquad();
					return squad != null && (squad.NumMembers() > 1) && squad.IsLeader(GetOuter()!);
				}
		}

		return true;
	}

	int PlaySentence(ReadOnlySpan<char> sentence) {
		int sentenceIndex = SENTENCEG_PlayRndSz(GetOuter()!.Edict(), sentence, GetVolume(), GetSoundLevel(), 0, GetVoicePitch());
		if (sentenceIndex < 0) {
			SentenceMsg("BOGUS", sentence);
			return -1;
		}

		ReadOnlySpan<char> sentenceName = engine.SentenceNameFromIndex(sentenceIndex);
		SentenceMsg("Speaking", sentenceName);
		return sentenceIndex;
	}

	public int Speak(ReadOnlySpan<char> sentence, SentencePriority soundPriority = SentencePriority.Normal, SentenceCriteria criteria = SentenceCriteria.InSquad) {
		if (!MatchesCriteria(criteria))
			return -1;

		ClearQueue();

		if (soundPriority == SentencePriority.Invalid)
			return PlaySentence(sentence);

		int sentenceIndex = -1;
		if (GetOuter()!.FOkToMakeSound(soundPriority)) {
			sentenceIndex = PlaySentence(sentence);

			GetOuter()!.JustMadeSound(soundPriority, 2.0f);
		}
		else
			SentenceMsg("CULL", sentence);

		return sentenceIndex;
	}

	public int SpeakQueued(ReadOnlySpan<char> sentence, SentencePriority soundPriority = SentencePriority.Normal, SentenceCriteria criteria = SentenceCriteria.InSquad) {
		if (!MatchesCriteria(criteria))
			return -1;

		ClearQueue();

		int sentenceIndex = Speak(sentence, soundPriority, criteria);
		if (sentenceIndex >= 0)
			return sentenceIndex;

		int queuedSentenceIndex = SENTENCEG_PickRndSz(sentence);
		if (queuedSentenceIndex == -1)
			return -1;

		int squadCount = GetOuter()!.GetSquad() != null ? GetOuter()!.GetSquad()!.NumMembers() : 1;
		QueueTimeout = gpGlobals.CurTime + squadCount * 2.0f;
		QueueSoundPriority = soundPriority;
		QueuedSentenceIndex = queuedSentenceIndex;
		return -1;
	}

	protected abstract float GetVolume();
	protected abstract SoundLevel GetSoundLevel();

	int VoicePitch;
	int QueuedSentenceIndex;
	TimeUnit_t QueueTimeout;
	SentencePriority QueueSoundPriority;
}

public class AI_Sentence_NPC_MetroPolice : AI_SentenceBase
{
	public void Init(NPC_MetroPolice outer, ReadOnlySpan<char> gameSound) {
		SetOuter(outer);

		if (VoicePitchMin <= 0 || VoicePitchMax <= 0) {
			SoundParameters parms = new();
			if (BaseEntity.GetParametersForSound(gameSound, ref parms, null)) {
				SentenceVolume = parms.Volume;
				SentenceSoundlevel = parms.SoundLevel;
				VoicePitchMin = parms.PitchLow;
				VoicePitchMax = parms.PitchHigh;
			}
		}

		if (random.RandomInt(0, 1) != 0)
			SetVoicePitch(random.RandomInt(VoicePitchMin, VoicePitchMax));
		else
			SetVoicePitch(100);
	}

	protected override float GetVolume() => SentenceVolume;
	protected override SoundLevel GetSoundLevel() => SentenceSoundlevel;

	static float SentenceVolume = 1.0f;
	static SoundLevel SentenceSoundlevel = SoundLevel.LvlNorm;
	static int VoicePitchMin = 0;
	static int VoicePitchMax = 0;
}
