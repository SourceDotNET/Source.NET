#if CLIENT_DLL || GAME_DLL
using SharpCompress.Common;

using Source;
using Source.Common;
using Source.Common.Mathematics;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using static Source.Common.StudioHdr.ActivityToSequenceMapping;

namespace Game.Shared;

public static class Animation
{
	public const float NOMOTION = 99999;

static Animation(){
		StudioHdr.SetActivityForSequence += SetActivityForSequence;
}
	public static StudioAnimSeqFlags GetSequenceFlags(StudioHdr? studioHdr, int sequence) {
		if (studioHdr == null || !studioHdr.SequencesAvailable() || sequence < 0 || sequence >= studioHdr.GetNumSeq())
			return 0;

		MStudioSeqDesc seqdesc = studioHdr.Seqdesc(sequence);
		return seqdesc.Flags;
	}

	public static Activity LookupActivity(StudioHdr? studiohdr, ReadOnlySpan<char> label) {
		if (studiohdr == null)
			return 0;

		for (int i = 0; i < studiohdr.GetNumSeq(); i++) {
			MStudioSeqDesc seqdesc = studiohdr.Seqdesc(i);
			if (stricmp(seqdesc.ActivityName(), label) == 0)
				return (Activity)seqdesc.Activity;
		}

		return Activity.ACT_INVALID;
	}

	public static int LookupSequence(StudioHdr? studiohdr, ReadOnlySpan<char> label) {
		if (studiohdr == null)
			return 0;

		if (!studiohdr.SequencesAvailable())
			return 0;

		for (int i = 0; i < studiohdr.GetNumSeq(); i++) {
			MStudioSeqDesc seqdesc = studiohdr.Seqdesc(i);
			if (stricmp(seqdesc.Label(), label) == 0)
				return i;
		}

		Activity activity = LookupActivity(studiohdr, label);
		if (activity != Activity.ACT_INVALID) 
			return SelectWeightedSequence(studiohdr, activity);
		

		return (int)Activity.ACT_INVALID;
	}

	internal static void GetSequenceLinearMotion(StudioHdr? studioHdr, int sequence, ReadOnlySpan<float> poseParameter, out Vector3 vecReturn) {
		vecReturn = default;

		if (studioHdr == null) {
			Msg("Bad pstudiohdr in GetSequenceLinearMotion()!\n");
			return;
		}

		if (!studioHdr.SequencesAvailable())
			return;

		if (sequence < 0 || sequence >= studioHdr.GetNumSeq()) {
			// Don't spam on bogus model
			if (studioHdr.GetNumSeq() > 0)
				Msg($"Bad sequence ({sequence} out of {studioHdr.GetNumSeq()} max) in GetSequenceLinearMotion() for model '{studioHdr.Name()}'!\n");

			return;
		}

		BoneSetup.Studio_SeqMovement(studioHdr, sequence, 0, 1.0f, poseParameter, out vecReturn, out _);
	}

	internal static string GetSequenceName(StudioHdr? studioHdr, int sequence) {
		if (studioHdr == null || !studioHdr.SequencesAvailable() || sequence < 0 || sequence >= studioHdr.GetNumSeq()) {
			if (studioHdr != null)
				Msg($"Bad sequence in GetSequenceName() for model '{studioHdr.Name()}'!\n");
			return "Unknown";
		}

		MStudioSeqDesc seqdesc = studioHdr.Seqdesc(sequence);
		return seqdesc.Label();
	}


	public static ReadOnlySpan<char> GetSequenceActivityName(StudioHdr? studiohdr, int sequence) {
		if (studiohdr == null || sequence < 0 || sequence >= studiohdr.GetNumSeq()) {
			if (studiohdr != null)
				Msg($"Bad sequence in GetSequenceActivityName() for model '{studiohdr.Name()}'!\n");
			return "Unknown";
		}

		MStudioSeqDesc seqdesc = studiohdr.Seqdesc(sequence);
		return seqdesc.ActivityName();
	}
	public static void SetActivityForSequence(StudioHdr studiohdr, int i) {
		int activityIndex;
		ReadOnlySpan<char> activityName;
		MStudioSeqDesc seqdesc = studiohdr.Seqdesc(i);

		seqdesc.Flags |= StudioAnimSeqFlags.Activity;

		activityName = GetSequenceActivityName(studiohdr, i);
		if (!activityName.IsEmpty && activityName[0] != '\0') {
			activityIndex = ActivityList.IndexForName(activityName);

			if (activityIndex == -1) {
				// Allow this now.  Animators can create custom activities that are referenced only on the client or by scripts, etc.
				//Warning( "***\nModel %s tried to reference unregistered activity: %s \n***\n", pstudiohdr->name, pszActivityName );
				//Assert(0);
				// HACK: the client and server don't share the private activity list so registering it on the client would hose the server
#if CLIENT_DLL
				seqdesc.Flags &= ~StudioAnimSeqFlags.Activity;
#else
				seqdesc.Activity = (int)ActivityList.RegisterPrivateActivity(activityName);
#endif
			}
			else {
				seqdesc.Activity = activityIndex;
			}
		}
	}


	public static int GetSequenceActivity(StudioHdr studiohdr, int sequence, out int weight) {
		if (null == studiohdr || !studiohdr.SequencesAvailable()) {
			weight = 0;
			return 0;
		}

		MStudioSeqDesc seqdesc = studiohdr.Seqdesc(sequence);

		if (0 == (seqdesc.Flags & StudioAnimSeqFlags.Activity))
			SetActivityForSequence(studiohdr, sequence);

		weight = seqdesc.ActWeight;
		return seqdesc.Activity;
	}

	public static void SetEventIndexForSequence(MStudioSeqDesc seqdesc) {
		seqdesc.Flags |= StudioAnimSeqFlags.Event;

		if (seqdesc.NumEvents == 0)
			return;

		for (int index = 0; index < (int)seqdesc.NumEvents; index++) {
			MStudioEvent? pevent = seqdesc.Event(index);

			if (pevent == null)
				continue;

			if (((AnimEventType)pevent.Type & AnimEventType.NewEventSystem) != 0) {
				string eventName = pevent.EventName();

				int eventIndex = EventList.IndexForName(eventName);

				if (eventIndex == -1)
					pevent.Event = (int)EventList.RegisterPrivateEvent(eventName);
				else {
					pevent.Event = eventIndex;
					pevent.Type |= (int)EventList.GetEventType(eventIndex);
				}
			}
		}
	}

	public static MStudioEvent? GetEventIndexForSequence(MStudioSeqDesc seqdesc) {
		if ((seqdesc.Flags & StudioAnimSeqFlags.Event) == 0)
			SetEventIndexForSequence(seqdesc);

		return seqdesc.NumEvents > 0 ? seqdesc.Event(0) : null;
	}

	public static void BuildAllAnimationEventIndexes(StudioHdr? studiohdr) {
		if (studiohdr == null)
			return;

		if (studiohdr.GetEventListVersion() != EventList.g_EventListVersion) {
			for (int i = 0; i < studiohdr.GetNumSeq(); i++)
				SetEventIndexForSequence(studiohdr.Seqdesc(i));

			studiohdr.SetEventListVersion(EventList.g_EventListVersion);
		}
	}

	public static int SelectHeaviestSequence(StudioHdr? studiohdr, Activity activity) {
		if (studiohdr == null)
			return 0;

		VerifySequenceIndex(studiohdr);

		int maxweight = 0;
		int seq = StudioHdr.ACTIVITY_NOT_AVAILABLE;
		for (int i = 0; i < studiohdr.GetNumSeq(); i++) {
			int curActivity = GetSequenceActivity(studiohdr, i, out int weight);
			if (curActivity == (int)activity) {
				if (Math.Abs(weight) > maxweight) {
					maxweight = Math.Abs(weight);
					seq = i;
				}
			}
		}

		return seq;
	}

	public static int GetAnimationEvent(StudioHdr? studiohdr, int sequence, ref AnimEvent npcEvent, float start, float end, int index) {
		if (studiohdr == null || sequence >= studiohdr.GetNumSeq())
			return 0;

		MStudioSeqDesc seqdesc = studiohdr.Seqdesc(sequence);
		if (seqdesc.NumEvents == 0 || index >= (int)seqdesc.NumEvents)
			return 0;

		GetEventIndexForSequence(seqdesc);
		for (; index < (int)seqdesc.NumEvents; index++) {
			MStudioEvent pevent = seqdesc.Event(index);

			if (((AnimEventType)pevent.Type & AnimEventType.NewEventSystem) != 0) {
				if (((AnimEventType)pevent.Type & AnimEventType.Server) == 0)
					continue;
			}
			else if (pevent.Event >= EVENT_CLIENT)
				continue;

			bool overlapEvent = false;

			if (pevent.Cycle >= start && pevent.Cycle < end)
				overlapEvent = true;
			else if ((seqdesc.Flags & StudioAnimSeqFlags.Looping) != 0 && end < start) {
				if (pevent.Cycle >= start || pevent.Cycle < end)
					overlapEvent = true;
			}

			if (overlapEvent) {
				npcEvent.Source = null;
				npcEvent.Cycle = pevent.Cycle;
				npcEvent.EventTime = gpGlobals.CurTime;
				npcEvent.Event = pevent.Event;
				npcEvent.Options = Encoding.ASCII.GetString(((ReadOnlySpan<byte>)pevent.Options()).SliceNullTerminatedString());
				npcEvent.Type = (AnimEventType)pevent.Type;
				return index + 1;
			}
		}
		return 0;
	}
	public static int FindTransitionSequence(StudioHdr? studiohdr, int currentSequence, int goalSequence, ref int dir){
		if (studiohdr == null)
			return goalSequence;

		if (!studiohdr.SequencesAvailable())
			return goalSequence;

		if ((currentSequence < 0) || (currentSequence >= studiohdr.GetNumSeq()))
			return goalSequence;

		if ((goalSequence < 0) || (goalSequence >= studiohdr.GetNumSeq())) {
			// asking for a bogus sequence.  Punt.
			Assert(0);
			return goalSequence;
		}


		// bail if we're going to or from a node 0
		if (studiohdr.EntryNode(currentSequence) == 0 || studiohdr.EntryNode(goalSequence) == 0) {
			dir = 1;
			return goalSequence;
		}

		int iEndNode;

		// Msg( "from %d to %d: ", pEndNode->iEndNode, pGoalNode->iStartNode );

		// check to see if we should be going forward or backward through the graph
		if (dir > 0) 
			iEndNode = studiohdr.ExitNode(currentSequence);
		else 
			iEndNode = studiohdr.EntryNode(currentSequence);

		// if both sequences are on the same node, just go there
		if (iEndNode == studiohdr.EntryNode(goalSequence)) {
			dir = 1;
			return goalSequence;
		}

		int iInternNode = studiohdr.GetTransition(iEndNode, studiohdr.EntryNode(goalSequence));

		// if there is no transitionial node, just go to the goal sequence
		if (iInternNode == 0)
			return goalSequence;

		int i;

		// look for someone going from the entry node to next node it should hit
		// this may be the goal sequences node or an intermediate node
		for (i = 0; i < studiohdr.GetNumSeq(); i++) {
			MStudioSeqDesc seqdesc = studiohdr.Seqdesc(i);
			if (studiohdr.EntryNode(i) == iEndNode && studiohdr.ExitNode(i) == iInternNode) {
				dir = 1;
				return i;
			}
			if (seqdesc.NodeFlags != 0) {
				if (studiohdr.ExitNode(i) == iEndNode && studiohdr.EntryNode(i) == iInternNode) {
					dir = -1;
					return i;
				}
			}
		}

		// Go ahead and jump to the goal sequence
		return goalSequence;
	}

	public static void IndexModelSequences(StudioHdr? studiohdr) {
		int i;

		if (studiohdr == null)
			return;

		if (!studiohdr.SequencesAvailable())
			return;

		for (i = 0; i < studiohdr.GetNumSeq(); i++) {
			SetActivityForSequence(studiohdr, i);
			SetEventIndexForSequence(studiohdr.Seqdesc(i));
		}

		studiohdr.SetActivityListVersion(ActivityList.Version);
	}
	public static void ResetEventIndexes(StudioHdr? studiohdr) {
		if (studiohdr == null)
			return;

		studiohdr.SetEventListVersion(EventList.g_EventListVersion - 1);
	}

	public static void ResetActivityIndexes(StudioHdr? studiohdr) {
		if (studiohdr == null)
			return;

		studiohdr.SetActivityListVersion(ActivityList.Version - 1);
	}

	public static void GetEyePosition(StudioHdr? studiohdr, ref Vector3 eyePosition) {
		if (studiohdr == null) {
			Warning("GetEyePosition() Can't get pstudiohdr ptr!\n");
			return;
		}

		eyePosition = studiohdr.EyePosition();
	}

	public static void VerifySequenceIndex(StudioHdr? studiohdr) {
		if (studiohdr == null)
			return;

		if (studiohdr.GetActivityListVersion() != ActivityList.Version) {
			// this model's sequences have not yet been indexed by activity
			IndexModelSequences(studiohdr);
		}
	}

	public static int SelectWeightedSequence(StudioHdr? studiohdr, Activity activity, int curSequence = -1) {
		if (studiohdr == null)
			return 0;

		if (!studiohdr.SequencesAvailable())
			return 0;

		VerifySequenceIndex(studiohdr);

		return studiohdr.SelectWeightedSequence((int)activity, curSequence);
	}

	internal static void SetBodygroup(StudioHdr? studioHdr, ref int body, int group, int value) {
		if (studioHdr == null)
			return;

		if (group >= studioHdr.NumBodyParts())
			return;

		MStudioBodyParts bodypart = studioHdr.Bodypart(group);

		if (value >= bodypart.NumModels)
			return;

		int iCurrent = (body / bodypart.Base) % bodypart.NumModels;

		body = (body - (iCurrent * bodypart.Base) + (value * bodypart.Base));
	}
}

public static class ImplementStudioHdrFns {
	extension(StudioHdr map) {
		public int SelectWeightedSequence(int activity, int curSequence) {
			if (!map.ActivityToSequence.IsInitialized())
				map.ActivityToSequence.Initialize(map);

			return map.ActivityToSequence.SelectWeightedSequence(map, activity, curSequence);
		}
	}

	static bool IsInPrediction() => BaseEntity.GetPredictionPlayer() != null;

	extension(StudioHdr.ActivityToSequenceMapping map) {
		public int SelectWeightedSequence(StudioHdr studiohdr, int activity, int curSequence) {
			if (!map.ValidateAgainst(studiohdr)) {
				AssertMsg(false, $"StudioHdr {studiohdr.Name()} has changed its vmodel pointer without reinitializing its activity mapping! Now performing emergency reinitialization.");
				map.Reinitialize(studiohdr);
			}

			// a null m_pSequenceTuples just means that this studio header has no activities.
			if (map.SequenceTuples == null)
				return StudioHdr.ACTIVITY_NOT_AVAILABLE;

			// is the current sequence appropriate?
			if (curSequence >= 0) {
				MStudioSeqDesc seqdesc = studiohdr.Seqdesc(curSequence);

				if (seqdesc.Activity == activity && seqdesc.ActWeight < 0)
					return curSequence;
			}

			// get the data for the given activity
			HashValueType dummy = new(activity, 0, 0, 0);
			ref HashValueType actData = ref map.ActToSeqHash.TryGetRef(dummy.ActivityIdx, out bool ok);
			if (!ok)
				return StudioHdr.ACTIVITY_NOT_AVAILABLE;

			int weighttotal = actData.TotalWeight;
			// generate a random number from 0 to the total weight
			int randomValue;
			if (IsInPrediction())
				// dimhotepus: At least vortigaunt model has weighttotal equal to 0.
				randomValue = weighttotal == 0 ? 0 : SharedRandomInt("SelectWeightedSequence", 0, weighttotal - 1);
			else
				// dimhotepus: At least vortigaunt model has weighttotal equal to 0.
				randomValue = weighttotal == 0 ? 0 : RandomInt(0, weighttotal - 1);

			// chug through the entries in the list (they are sequential therefore cache-coherent)
			// until we run out of random juice
			Span<SequenceTuple> sequenceInfo =  map.SequenceTuples.AsSpan()[actData.StartingIdx..][..actData.Count];
			while (randomValue >= sequenceInfo[0].Weight && sequenceInfo.Length > 0) {
				randomValue -= sequenceInfo[0].Weight;
				sequenceInfo = sequenceInfo[1..];
			}

			return sequenceInfo[0].SeqNum;
		}
	}
}
#endif
