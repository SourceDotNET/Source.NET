using CommunityToolkit.HighPerformance;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Source.Common;

public partial class VirtualModel
{
	static readonly ObjectPool<ModelLookup> ModelLookups = new();
	public class ModelLookup : IPoolableObject
	{
		public readonly Dictionary<UtlSymId_t, short> SeqTable = [];
		public readonly Dictionary<UtlSymId_t, short> AnimTable = [];
		public void Init() {
			SeqTable.Clear();
			AnimTable.Clear();
		}
		public void Reset() { }
	}
	public static readonly List<ModelLookup> g_ModelLookup = [];
	public static int g_ModelLookupIndex = -1;
	public static bool HasLookupTable() => g_ModelLookupIndex >= 0;

	public static readonly object g_SeqTableLock = new();
	public static Dictionary<UtlSymId_t, short> GetSeqTable() => g_ModelLookup[g_ModelLookupIndex].SeqTable;

	public static readonly object g_AnimTableLock = new();
	public static Dictionary<UtlSymId_t, short> GetAnimTable() => g_ModelLookup[g_ModelLookupIndex].AnimTable;

	public struct ModelLookupContext : IDisposable
	{
		public ModelLookupContext(int group, StudioHeader studioHdr) {
			LookupIndex = -1;
			if (group == 0 && studioHdr.NumIncludeModels != 0) {
				LookupIndex = g_ModelLookup.Count;
				g_ModelLookupIndex = g_ModelLookup.Count;
				g_ModelLookup.Add(ModelLookups.Alloc());
			}
		}

		public void Dispose() {
			if (LookupIndex >= 0) {
				Assert(LookupIndex == (g_ModelLookup.Count - 1));
				ModelLookups.Free(g_ModelLookup[LookupIndex]);
				g_ModelLookup.RemoveAt(LookupIndex);
				g_ModelLookupIndex = g_ModelLookup.Count - 1;
			}
		}

		private int LookupIndex;
	}

	void BeginTemporaryCopyList<T>(List<T> source, out List<T> dest) => dest = new List<T>(source);
	void EndTemporaryCopyList<T>(ref List<T> source, List<T> dest) => source = dest;

	public void AppendSequences(int group, StudioHeader studioHdr) {
		lock (Lock) {
			lock (g_SeqTableLock) {
				int numCheck = Seq.Count;
				int j, k;

				BeginTemporaryCopyList(Seq, out var seq);
				Group[group].MasterSeq.SetCount(studioHdr.NumLocalSeq);

				for (j = 0; j < studioHdr.NumLocalSeq; j++) {
					MStudioSeqDesc seqdesc = studioHdr.LocalSeqdesc(j);
					ReadOnlySpan<char> s1 = seqdesc.Label();
					UtlSymId_t s1h = s1.Hash(invariant: false);

					if (HasLookupTable()) {
						k = numCheck;
						if (GetSeqTable().TryGetValue(s1h, out var nk))
							k = nk;
					}
					else {
						for (k = 0; k < numCheck; k++) {
							StudioHeader hdr = Group[seq[k].Group].GetStudioHdr()!;
							ReadOnlySpan<char> s2 = hdr.LocalSeqdesc(seq[k].Index).Label();
							if (0 == stricmp(s1, s2))
								break;
						}
					}

					// no duplication
					if (k == numCheck) {
						VirtualSequence tmp;
						tmp.Group = group;
						tmp.Index = j;
						tmp.Flags = seqdesc.Flags;
						tmp.Activity = seqdesc.Activity;
						k = seq.Count; seq.Add(tmp);
					}
					else if ((Group[seq[k].Group].GetStudioHdr()!.LocalSeqdesc(seq[k].Index).Flags & StudioAnimSeqFlags.Override) != 0) {
						// the one in memory is a forward declared sequence, override it
						VirtualSequence tmp;
						tmp.Group = group;
						tmp.Index = j;
						tmp.Flags = seqdesc.Flags;
						tmp.Activity = seqdesc.Activity;
						seq[k] = tmp;
					}
					Group[group].MasterSeq[j] = k;
				}

				if (HasLookupTable()) {
					for (j = numCheck; j < seq.Count(); j++) {
						StudioHeader hdr = Group[seq[j].Group].GetStudioHdr()!;
						ReadOnlySpan<char> s1 = hdr.LocalSeqdesc(seq[j].Index).Label();
						GetSeqTable()[s1.Hash(invariant: false)] = (short)j;
					}
				}
				EndTemporaryCopyList(ref Seq, seq);
			}
		}
	}
	public void AppendAnimations(int group, StudioHeader studioHdr) {
		lock (Lock) {
			lock (g_AnimTableLock) {
				int numCheck = Anim.Count;

				BeginTemporaryCopyList(Anim, out var anim);

				int j, k;

				Group[group].MasterAnim.SetCount(studioHdr.NumLocalAnim);

				for (j = 0; j < studioHdr.NumLocalAnim; j++) {
					ReadOnlySpan<char> s1 = studioHdr.LocalAnimdesc(j).Name();
					UtlSymId_t h1 = s1.Hash(invariant: false);
					if (HasLookupTable()) {
						k = numCheck;
						if (GetAnimTable().TryGetValue(h1, out var nk))
							k = nk;
					}
					else {
						for (k = 0; k < numCheck; k++) {
							ReadOnlySpan<char> s2 = Group[anim[k].Group].GetStudioHdr()!.LocalAnimdesc(anim[k].Index).Name();
							if (stricmp(s1, s2) == 0)
								break;
						}
					}
					// no duplication
					if (k == numCheck) {
						VirtualGeneric tmp;
						tmp.Group = group;
						tmp.Index = j;
						k = anim.Count; anim.Add(tmp);
					}

					Group[group].MasterAnim[j] = k;
				}

				if (HasLookupTable()) {
					for (j = numCheck; j < anim.Count(); j++) {
						ReadOnlySpan<char> s1 = Group[anim[j].Group].GetStudioHdr()!.LocalAnimdesc(anim[j].Index).Name();
						GetAnimTable()[s1.Hash(invariant: false)] = (short)j;
					}
				}

				EndTemporaryCopyList(ref Anim, anim);
			}
		}
	}
	public void AppendAttachments(int group, StudioHeader studioHdr) {
		lock (Lock) {
			int numCheck = Attachment.Count;
			BeginTemporaryCopyList(Attachment, out var attachment);

			int j, k, n;

			Group[group].MasterAttachment.SetCount(studioHdr.NumLocalAttachments);

			for (j = 0; j < studioHdr.NumLocalAttachments; j++) {

				n = Group[group].MasterBone[studioHdr.LocalAttachment(j).LocalBone];

				// skip if the attachments bone doesn't exist in the root model
				if (n == -1)
					continue;

				ReadOnlySpan<char> s1 = studioHdr.LocalAttachment(j).Name();
				for (k = 0; k < numCheck; k++) {
					ReadOnlySpan<char> s2 = Group[attachment[k].Group].GetStudioHdr()!.LocalAttachment(attachment[k].Index).Name();

					if (stricmp(s1, s2) == 0)
						break;
				}
				// no duplication
				if (k == numCheck) {
					VirtualGeneric tmp;
					tmp.Group = group;
					tmp.Index = j;
					k = attachment.Count; attachment.Add(tmp);

					var grouphdr = Group[0].GetStudioHdr();
					// make sure bone flags are set so attachment calculates
					if ((grouphdr!.Bone(n).Flags & Studio.BONE_USED_BY_ATTACHMENT) == 0) {
						while (n != -1) {
							grouphdr.Bone(n).Flags |= Studio.BONE_USED_BY_ATTACHMENT;

							if (grouphdr.LinearBones() != null)
								grouphdr.LinearBones()!.RefFlags(n) |= Studio.BONE_USED_BY_ATTACHMENT;

							n = grouphdr!.Bone(n).Parent;
						}
						continue;
					}
				}

				Group[group].MasterAttachment[j] = k;
			}

			EndTemporaryCopyList(ref Attachment, attachment);
		}
	}
	public void AppendPoseParameters(int group, StudioHeader studioHdr) {
		lock (Lock) {
			int numCheck = Pose.Count;

			BeginTemporaryCopyList(Pose, out var pose);
			int j, k;

			Group[group].MasterPose.SetCount(studioHdr.NumLocalPoseParameters);

			for (j = 0; j < studioHdr.NumLocalPoseParameters; j++) {
				ReadOnlySpan<char> s1 = studioHdr.LocalPoseParameter(j).Name();
				for (k = 0; k < numCheck; k++) {
					ReadOnlySpan<char> s2 = Group[pose[k].Group].GetStudioHdr()!.LocalPoseParameter(pose[k].Index).Name();

					if (stricmp(s1, s2) == 0)
						break;
				}
				if (k == numCheck) {
					// no duplication
					VirtualGeneric tmp;
					tmp.Group = group;
					tmp.Index = j;
					k = pose.Count; pose.Add(tmp);
				}
				else {
					// duplicate, reset start and end to fit full dynamic range
					MStudioPoseParamDesc pose1 = studioHdr.LocalPoseParameter(j);
					MStudioPoseParamDesc pose2 = Group[pose[k].Group].GetStudioHdr()!.LocalPoseParameter(pose[k].Index);
					float start = MathF.Min(pose2.End, MathF.Min(pose1.End, MathF.Min(pose2.Start, pose1.Start)));
					float end = MathF.Max(pose2.End, MathF.Max(pose1.End, MathF.Max(pose2.Start, pose1.Start)));
					pose2.Start = start;
					pose2.End = end;
				}

				Group[group].MasterPose[j] = k;
			}

			EndTemporaryCopyList(ref Pose, pose);
		}
	}
	public void AppendBonemap(int group, StudioHeader studioHdr) {
		lock (Lock) {
			StudioHeader baseStudioHdr = Group[0].GetStudioHdr()!;

			Group[group].BoneMap.SetCount(baseStudioHdr.NumBones);
			Group[group].MasterBone.SetCount(studioHdr.NumBones);

			int j, k;

			if (group == 0) {
				for (j = 0; j < studioHdr.NumBones; j++) {
					Group[group].BoneMap[j] = j;
					Group[group].MasterBone[j] = j;
				}
			}
			else {
				for (j = 0; j < baseStudioHdr.NumBones; j++) {
					Group[group].BoneMap[j] = -1;
				}
				for (j = 0; j < studioHdr.NumBones; j++) {
					// NOTE: studiohdr has a bone table - using the table is ~5% faster than this for alyx.mdl on a P4/3.2GHz
					for (k = 0; k < baseStudioHdr.NumBones; k++) {
						if (stricmp(studioHdr.Bone(j).Name(), baseStudioHdr.Bone(k).Name()) == 0)
							break;
					}
					if (k < baseStudioHdr.NumBones) {
						Group[group].MasterBone[j] = k;
						Group[group].BoneMap[k] = j;

						// FIXME: these runtime messages don't display in hlmv
						if ((studioHdr.Bone(j).Parent == -1) || (baseStudioHdr.Bone(k).Parent == -1)) {
							if ((studioHdr.Bone(j).Parent != -1) || (baseStudioHdr.Bone(k).Parent != -1))
								Warning($"{baseStudioHdr.GetName()}/{studioHdr.GetName()} : mismatched parent bones on \"{studioHdr.Bone(j).Name()}\"\n");
						}
						else if (Group[group].MasterBone[studioHdr.Bone(j).Parent] != Group[0].MasterBone[baseStudioHdr.Bone(k).Parent])
							Warning($"{baseStudioHdr.GetName()}/{studioHdr.GetName()} : mismatched parent bones on \"{studioHdr.Bone(j).Name()}\"\n");
					}
					else {
						Group[group].MasterBone[j] = -1;
					}
				}
			}
		}
	}
	public void AppendNodes(int group, StudioHeader studioHdr) {
		lock (Lock) {
			int numCheck = Node.Count;

			BeginTemporaryCopyList(Node, out var node);
			int j, k;

			Group[group].MasterNode.SetCount(studioHdr.NumLocalNodes);

			for (j = 0; j < studioHdr.NumLocalNodes; j++) {
				ReadOnlySpan<char> s1 = studioHdr.LocalNodeName(j);
				for (k = 0; k < numCheck; k++) {
					ReadOnlySpan<char> s2 = Group[node[k].Group].GetStudioHdr().LocalNodeName(node[k].Index);

					if (stricmp(s1, s2) == 0)
						break;
				}
				// no duplication
				if (k == numCheck) {
					VirtualGeneric tmp;
					tmp.Group = group;
					tmp.Index = j;
					k = node.Count; node.Add(tmp);
				}

				Group[group].MasterNode[j] = k;
			}

			EndTemporaryCopyList(ref Node, node);
		}
	}

	public void AppendIKLocks(int group, StudioHeader studioHDR) {
		lock (Lock) {
			int numCheck = IKLock.Count;

			BeginTemporaryCopyList(IKLock, out var iklock);

			int j, k;

			for (j = 0; j < studioHDR.NumLocalIKAutoplayLocks; j++) {
				int chain1 = studioHDR.LocalIKAutoplayLock(j).Chain;
				for (k = 0; k < numCheck; k++) {
					int chain2 = Group[iklock[k].Group].GetStudioHdr()!.LocalIKAutoplayLock(iklock[k].Index).Chain;

					if (chain1 == chain2)
						break;
				}
				if (k == numCheck) {
					VirtualGeneric tmp;
					tmp.Group = group;
					tmp.Index = j;
					k = iklock.Count; iklock.Add(tmp);
				}
			}

			EndTemporaryCopyList(ref IKLock, iklock);
		}
	}

	struct HandleAndHeader_t
	{
		public MDLHandle_t Handle;
		public StudioHeader Hdr;
	}
	public void AppendModels(int group, StudioHeader studioHdr) {
		using ModelLookupContext ctx = new ModelLookupContext(group, studioHdr);
		if (studioHdr == null) return;

		AppendSequences(group, studioHdr);
		AppendAnimations(group, studioHdr);
		AppendBonemap(group, studioHdr);
		AppendAttachments(group, studioHdr);
		AppendPoseParameters(group, studioHdr);
		AppendNodes(group, studioHdr);
		AppendIKLocks(group, studioHdr);
		// This was ThreadLocal, but for some reason that was causing issues/crashes in MDLCache
		// I've just changed it back to how Source has it for now,
		// but maybe returning to this at some point is worth it - Callum
		HandleAndHeader_t[] list = new HandleAndHeader_t[64];

		// determine quantity of valid include models in one pass only
		// temporarily cache results off, otherwise FindModel() causes ref counting problems
		int j;
		int validIncludes = 0;
		for (j = 0; j < studioHdr.NumIncludeModels; j++) {
			// find model (increases ref count)
			MDLHandle_t tmp = 0;
			StudioHeader? tmpHdr = studioHdr.FindModel(out tmp, studioHdr.ModelGroup(j).Name());
			if (tmpHdr != null) {
				if (validIncludes >= list.Length) {
					// would cause overflow
					Assert(false);
					break;
				}

				list[validIncludes].Handle = tmp;
				list[validIncludes].Hdr = tmpHdr;
				validIncludes++;
			}
		}

		if (validIncludes != 0) {
			Group.EnsureCapacity(Group.Count + validIncludes);
			for (j = 0; j < validIncludes; j++) {
				int groupi = Group.Count; Group.Add(new());
				Group[groupi].Cache = (nint)list[j].Handle;
				AppendModels(groupi, list[j].Hdr);
			}
		}

		UpdateAutoplaySequences(studioHdr);
	}
	public void UpdateAutoplaySequences(StudioHeader studioHdr) {
		int autoplayCount = studioHdr.CountAutoplaySequences();
		if (AutoplaySequences.Length != autoplayCount)
			AutoplaySequences = new short[autoplayCount];
		studioHdr.CopyAutoplaySequences(AutoplaySequences, autoplayCount);
	}
}
