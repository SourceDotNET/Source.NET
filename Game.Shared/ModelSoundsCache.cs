using Source;
using Source.Common;

using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Shared;

public class ModelSoundsCache() : IBaseCacheInfo
{
	const int CL_EVENT_SOUND = 5004;
	const int CL_EVENT_FOOTSTEP_LEFT = 6004;
	const int CL_EVENT_FOOTSTEP_RIGHT = 6005;
	const int CL_EVENT_MFOOTSTEP_LEFT = 6006;
	const int CL_EVENT_MFOOTSTEP_RIGHT = 6007;

	public ReadOnlySpan<char> GetSoundName(int index) => soundemitterbase.GetSoundName(sounds[index]);

	public void Rebuild(ReadOnlySpan<char> filename) {
#if GAME_DLL
		sounds.Clear();
		StudioHdr? hdr = BaseEntity.ModelSoundsCache_LoadModel(filename);
		if (hdr != null) {
			BuildAnimationEventSoundList(hdr, sounds);
			BaseEntity.ModelSoundsCache_FinishModel(hdr);
		}
#endif
	}

	public void Restore(Stream buf) => throw new NotImplementedException();
	public void Save(Stream buf) => throw new NotImplementedException();

	internal void PrecacheSoundList() {
#if GAME_DLL
		for (int i = 0; i < sounds.Count; ++i)
			BaseEntity.ModelSoundsCache_PrecacheScriptSound(GetSoundName(i));
#endif
	}

	static void FindOrAddScriptSound(List<ushort> sounds, ReadOnlySpan<char> soundname) {
		int soundindex = soundemitterbase.GetSoundIndex(soundname);
		if (soundindex != -1) {
			if (!sounds.Contains((ushort)soundindex))
				sounds.Add((ushort)soundindex);
		}
	}

	static void FindOrAddScriptSound(List<ushort> sounds, MStudioEvent ev) => FindOrAddScriptSound(sounds, Encoding.ASCII.GetString(((ReadOnlySpan<byte>)ev.Options()).SliceNullTerminatedString()));

	public static void BuildAnimationEventSoundList(StudioHdr hdr, List<ushort> sounds) {
		Assert(hdr != null);

		Animation.VerifySequenceIndex(hdr);

		for (int iSeq = 0; iSeq < hdr.GetNumSeq(); iSeq++) {
			MStudioSeqDesc seq = hdr.Seqdesc(iSeq);

			for (int iEvent = 0; iEvent < (int)seq.NumEvents; iEvent++) {
				MStudioEvent? ev = seq.Event(iEvent);
				if (ev == null)
					continue;

				switch (ev.Event) {
					default:
						if (((AnimEventType)ev.Type & AnimEventType.NewEventSystem) != 0) {
							if (ev.Event == (int)AnimEventId.AE_SV_PLAYSOUND)
								FindOrAddScriptSound(sounds, ev);
						}
						break;
					case CL_EVENT_SOUND:
						FindOrAddScriptSound(sounds, ev);
						break;
					case CL_EVENT_FOOTSTEP_LEFT:
					case CL_EVENT_FOOTSTEP_RIGHT: {
							Span<char> soundname = stackalloc char[256];
							ReadOnlySpan<char> options = Encoding.ASCII.GetString(((ReadOnlySpan<byte>)ev.Options()).SliceNullTerminatedString());
							if (options.IsEmpty)
								options = "NPC_CombineS";
							sprintf(soundname, "%s.RunFootstepLeft").S(options);
							FindOrAddScriptSound(sounds, soundname.SliceNullTerminatedString());
							sprintf(soundname, "%s.RunFootstepRight").S(options);
							FindOrAddScriptSound(sounds, soundname.SliceNullTerminatedString());
							sprintf(soundname, "%s.FootstepLeft").S(options);
							FindOrAddScriptSound(sounds, soundname.SliceNullTerminatedString());
							sprintf(soundname, "%s.FootstepRight").S(options);
							FindOrAddScriptSound(sounds, soundname.SliceNullTerminatedString());
						}
						break;
					case (int)AnimEventId.AE_CL_PLAYSOUND:
						if (((AnimEventType)ev.Type & AnimEventType.Client) == 0)
							break;
						if (ev.Options()[0] != 0)
							FindOrAddScriptSound(sounds, ev);
						else
							Warning($"-- Error --:  empty soundname, .qc error on AE_CL_PLAYSOUND in model {hdr.Name()}, sequence {seq.Label()}, animevent # {iEvent + 1}\n");
						break;
					case SCRIPT_EVENT_SOUND:
						FindOrAddScriptSound(sounds, ev);
						break;
					case SCRIPT_EVENT_SOUND_VOICE:
						FindOrAddScriptSound(sounds, ev);
						break;
				}
			}
		}
	}

	public readonly List<ushort> sounds = [];
}
