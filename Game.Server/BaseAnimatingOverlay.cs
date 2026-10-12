using Game.Shared;

using Source.Common;
using Source.Common.Commands;

using System.Numerics;

namespace Game.Server;

using FIELD = Source.FIELD<BaseAnimatingOverlay>;

[NetworkName("CBaseAnimatingOverlay")]
public class BaseAnimatingOverlay : BaseAnimating
{
	public const int MAX_OVERLAYS = 15;

	internal static readonly ConVar ai_sequence_debug = new("ai_sequence_debug", "0");

	public static readonly SendTable DT_OverlayVars = new(nameof(DT_OverlayVars), [
		SendPropList<AnimationLayerRef>(FIELD.OF(nameof(AnimOverlay)), MAX_OVERLAYS, SendPropDataTable(null, AnimationLayerRef.DT_Animationlayer))
	]);

	public static readonly SendTable DT_BaseAnimatingOverlay = new(DT_BaseAnimating, [
		SendPropDataTable("overlay_vars", 0, DT_OverlayVars)
	]); public static readonly new ServerClass ServerClass = new ServerClass(DT_BaseAnimatingOverlay);

	[NetworkName("m_AnimOverlay")]
	readonly List<AnimationLayerRef> AnimOverlay = [];

	public AnimationLayerRef GetAnimOverlay(int i) {
		i = Math.Clamp(i, 0, AnimOverlay.Count - 1);

		return AnimOverlay[i];
	}

	public int GetNumAnimOverlays() => AnimOverlay.Count;
	public void SetNumAnimOverlays(int num) {
		if (AnimOverlay.Count < num)
			for (int i = 0, diff = num - AnimOverlay.Count; i < diff; i++)
				AnimOverlay.Add(new());
		else if (AnimOverlay.Count > num)
			for (int i = 0, diff = AnimOverlay.Count - num; i < diff; i++)
				AnimOverlay.RemoveAt(AnimOverlay.Count - 1);
	}

	public override void GetSkeleton(StudioHdr? studioHdr, Span<Vector3> pos, Span<Quaternion> q, int boneMask) {
		if (studioHdr == null) {
			AssertMsg(false, "BaseAnimating.GetSkeleton() without a model");
			return;
		}

		if (!studioHdr.SequencesAvailable())
			return;

		BoneSetup boneSetup = new(studioHdr, boneMask, PoseParameter);
		boneSetup.InitPose(pos, q);

		boneSetup.AccumulatePose(pos, q, GetSequence(), GetCycle(), 1.0f, gpGlobals.CurTime, null);

		Span<int> layer = stackalloc int[MAX_OVERLAYS];
		int i;
		for (i = 0; i < AnimOverlay.Count; i++)
			layer[i] = MAX_OVERLAYS;

		for (i = 0; i < AnimOverlay.Count; i++) {
			AnimationLayerRef pLayer = AnimOverlay[i];
			if ((pLayer.Weight > 0) && pLayer.IsActive() && pLayer.Order >= 0 && pLayer.Order < AnimOverlay.Count)
				layer[pLayer.Order] = i;
		}

		for (i = 0; i < AnimOverlay.Count; i++) {
			if (layer[i] >= 0 && layer[i] < AnimOverlay.Count) {
				AnimationLayerRef pLayer = AnimOverlay[layer[i]];
				boneSetup.AccumulatePose(pos, q, pLayer.Sequence, pLayer.Cycle, pLayer.Weight, gpGlobals.CurTime, null);
			}
		}

		boneSetup.CalcAutoplaySequences(pos, q, gpGlobals.CurTime, null);
	}

	public void VerifyOrder() {
#if DEBUG
		int i, j;
		Span<int> layer = stackalloc int[MAX_OVERLAYS];
		int maxOrder = -1;
		for (i = 0; i < MAX_OVERLAYS; i++)
			layer[i] = MAX_OVERLAYS;

		for (i = 0; i < AnimOverlay.Count; i++) {
			if (AnimOverlay[i].Order < MAX_OVERLAYS) {
				j = AnimOverlay[i].Order;
				Assert(layer[j] == MAX_OVERLAYS);
				layer[j] = i;
				if (j > maxOrder)
					maxOrder = j;
			}
		}
#endif
	}

	public override void StudioFrameAdvance() {
		TimeUnit_t advance = GetAnimTimeInterval();

		VerifyOrder();

		base.StudioFrameAdvance();

		for (int i = 0; i < AnimOverlay.Count; i++) {
			AnimationLayerRef layer = AnimOverlay[i];

			if (layer.IsActive()) {
				if (layer.IsKillMe()) {
					if (layer.KillDelay > 0) {
						layer.KillDelay -= (float)advance;
						layer.KillDelay = Math.Clamp(layer.KillDelay, 0.0f, 1.0f);
					}
					else if (layer.Weight != 0.0f) {
						layer.Weight -= layer.KillRate * (float)advance;
						layer.Weight = Math.Clamp(layer.Weight, 0.0f, 1.0f);
					}
					else {
						if (ai_sequence_debug.GetBool() == true && (DebugOverlays & DebugOverlayBits.NPCSelected) != 0)
							Msg($"removing {i} ({layer.Order}): {Animation.GetSequenceName(GetModelPtr(), layer.Sequence)} : {layer.Cycle,5:F3} ({layer.Weight:F3})\n");

						FastRemoveLayer(i);
						layer.Dying();
						continue;
					}
				}

				layer.StudioFrameAdvance(advance, this);
				if (layer.SequenceFinished && layer.IsAutokill()) {
					layer.Weight = 0.0f;
					layer.KillMe();
				}
			}
			else if (layer.IsDying())
				layer.Dead();
			else if (layer.Weight > 0.0) {
				layer.Init(this);
				layer.Dying();
			}
		}

		if (ai_sequence_debug.GetBool() == true && (DebugOverlays & DebugOverlayBits.NPCSelected) != 0) {
			for (int i = 0; i < AnimOverlay.Count; i++) {
				if (AnimOverlay[i].IsActive())
					Msg($" {i} ({AnimOverlay[i].Order}): {Animation.GetSequenceName(GetModelPtr(), AnimOverlay[i].Sequence)} : {AnimOverlay[i].Cycle,5:F3} ({AnimOverlay[i].Weight:F3})\n");
			}
		}

		VerifyOrder();
	}

	public int AddGestureSequence(int sequence, bool autokill = true) {
		int i = AddLayeredSequence(sequence, 0);
		if (IsValidLayer(i))
			SetLayerAutokill(i, autokill);

		return i;
	}

	public int AddGestureSequence(int sequence, TimeUnit_t duration, bool autokill = true) {
		int layer = AddGestureSequence(sequence, autokill);
		Assert(layer != -1);

		if (layer >= 0 && duration > 0)
			AnimOverlay[layer].PlaybackRate = SequenceDuration(sequence) / duration;

		return layer;
	}

	public int AddGesture(Activity activity, bool autokill = true) {
		if (IsPlayingGesture(activity))
			return FindGestureLayer(activity);

		int seq = SelectWeightedSequence(activity);
		if (seq <= 0) {
			ReadOnlySpan<char> actname = AI_BaseNPC.GetActivityName(activity);
			DevMsg($"BaseAnimatingOverlay.AddGesture:  model {GetModelName()} missing activity {actname}\n");
			return -1;
		}

		int i = AddGestureSequence(seq, autokill);
		Assert(i != -1);
		if (i != -1)
			AnimOverlay[i].Activity = activity;

		return i;
	}

	public int AddGesture(Activity activity, TimeUnit_t duration, bool autokill = true) {
		int layer = AddGesture(activity, autokill);
		SetLayerDuration(layer, duration);

		return layer;
	}

	public int FindGestureLayer(Activity activity) {
		for (int i = 0; i < AnimOverlay.Count; i++) {
			if (!AnimOverlay[i].IsActive())
				continue;

			if (AnimOverlay[i].IsKillMe())
				continue;

			if (AnimOverlay[i].Activity == Activity.ACT_INVALID)
				continue;

			if (AnimOverlay[i].Activity == activity)
				return i;
		}

		return -1;
	}

	public bool IsPlayingGesture(Activity activity) => FindGestureLayer(activity) != -1;

	public void RestartGesture(Activity activity, bool addIfMissing = true, bool autokill = true) {
		int idx = FindGestureLayer(activity);
		if (idx == -1) {
			if (addIfMissing)
				AddGesture(activity, autokill);
			return;
		}

		AnimOverlay[idx].Cycle = 0.0f;
		AnimOverlay[idx].PrevCycle = 0.0f;
		AnimOverlay[idx].LastEventCheck = 0.0f;
	}

	public int AddLayeredSequence(int sequence, int priority) {
		int i = AllocateLayer(priority);
		if (IsValidLayer(i)) {
			AnimOverlay[i].Cycle = 0;
			AnimOverlay[i].PrevCycle = 0;
			AnimOverlay[i].PlaybackRate = 1.0;
			AnimOverlay[i].Activity = Activity.ACT_INVALID;
			AnimOverlay[i].Sequence = sequence;
			AnimOverlay[i].Weight = 1.0f;
			AnimOverlay[i].BlendIn = 0.0f;
			AnimOverlay[i].BlendOut = 0.0f;
			AnimOverlay[i].SequenceFinished = false;
			AnimOverlay[i].LastEventCheck = 0;
			AnimOverlay[i].Looping = ((Animation.GetSequenceFlags(GetModelPtr(), sequence) & StudioAnimSeqFlags.Looping) != 0);
			if (ai_sequence_debug.GetBool() == true && (DebugOverlays & DebugOverlayBits.NPCSelected) != 0)
				Msg($"{gpGlobals.CurTime,5:F3} : adding {i} ({AnimOverlay[i].Order}): {Animation.GetSequenceName(GetModelPtr(), AnimOverlay[i].Sequence)} : {AnimOverlay[i].Cycle,5:F3} ({AnimOverlay[i].Weight:F3})\n");
		}

		return i;
	}

	public bool IsValidLayer(int layer) => layer >= 0 && layer < AnimOverlay.Count && AnimOverlay[layer].IsActive();

	int AllocateLayer(int priority = 0) {
		int newOrder = 0;
		int openLayer = -1;
		int numOpen = 0;
		for (int i = 0; i < AnimOverlay.Count; i++) {
			if (AnimOverlay[i].IsActive()) {
				if (AnimOverlay[i].Priority <= priority)
					newOrder = Math.Max(newOrder, AnimOverlay[i].Order + 1);
			}
			else if (AnimOverlay[i].IsDying()) {
				// skip
			}
			else if (openLayer == -1)
				openLayer = i;
			else
				numOpen++;
		}

		if (openLayer == -1) {
			if (AnimOverlay.Count >= MAX_OVERLAYS)
				return -1;

			openLayer = AnimOverlay.Count;
			AnimOverlay.Add(new());
			AnimOverlay[openLayer].Init(this);
		}

		if (numOpen == 0) {
			if (AnimOverlay.Count < MAX_OVERLAYS) {
				int i = AnimOverlay.Count;
				AnimOverlay.Add(new());
				AnimOverlay[i].Init(this);
			}
		}

		for (int i = 0; i < AnimOverlay.Count; i++) {
			if (AnimOverlay[i].Order >= newOrder && AnimOverlay[i].Order < MAX_OVERLAYS)
				AnimOverlay[i].Order++;
		}

		AnimOverlay[openLayer].Flags = AnimLayerFlags.Active;
		AnimOverlay[openLayer].Order = newOrder;
		AnimOverlay[openLayer].Priority = priority;

		AnimOverlay[openLayer].MarkActive();
		VerifyOrder();

		return openLayer;
	}

	public void SetLayerPriority(int layer, int priority) {
		if (!IsValidLayer(layer))
			return;

		if (AnimOverlay[layer].Priority == priority)
			return;

		for (int i = 0; i < AnimOverlay.Count; i++) {
			if (AnimOverlay[i].IsActive()) {
				if (AnimOverlay[i].Order > AnimOverlay[layer].Order)
					AnimOverlay[i].Order--;
			}
		}

		int newOrder = 0;
		for (int i = 0; i < AnimOverlay.Count; i++) {
			if (i != layer && AnimOverlay[i].IsActive()) {
				if (AnimOverlay[i].Priority <= priority)
					newOrder = Math.Max(newOrder, AnimOverlay[i].Order + 1);
			}
		}

		for (int i = 0; i < AnimOverlay.Count; i++) {
			if (i != layer && AnimOverlay[i].IsActive()) {
				if (AnimOverlay[i].Order >= newOrder)
					AnimOverlay[i].Order++;
			}
		}

		AnimOverlay[layer].Order = newOrder;
		AnimOverlay[layer].Priority = priority;
		AnimOverlay[layer].MarkActive();

		VerifyOrder();
	}

	public void SetLayerDuration(int layer, TimeUnit_t duration) {
		if (IsValidLayer(layer) && duration > 0)
			AnimOverlay[layer].PlaybackRate = SequenceDuration(AnimOverlay[layer].Sequence) / duration;
	}

	public TimeUnit_t GetLayerDuration(int layer) {
		if (IsValidLayer(layer)) {
			if (AnimOverlay[layer].PlaybackRate != 0.0)
				return (1.0 - AnimOverlay[layer].Cycle) * SequenceDuration(AnimOverlay[layer].Sequence) / AnimOverlay[layer].PlaybackRate;

			return SequenceDuration(AnimOverlay[layer].Sequence);
		}

		return 0.0;
	}

	public void SetLayerCycle(int layer, TimeUnit_t cycle) {
		if (!IsValidLayer(layer))
			return;

		if (!AnimOverlay[layer].Looping)
			cycle = Math.Clamp(cycle, 0.0, 1.0);

		AnimOverlay[layer].Cycle = cycle;
		AnimOverlay[layer].MarkActive();
	}

	public void SetLayerCycle(int layer, TimeUnit_t cycle, float prevCycle) {
		if (!IsValidLayer(layer))
			return;

		if (!AnimOverlay[layer].Looping) {
			cycle = Math.Clamp(cycle, 0.0, 1.0);
			prevCycle = Math.Clamp(prevCycle, 0.0f, 1.0f);
		}

		AnimOverlay[layer].Cycle = cycle;
		AnimOverlay[layer].PrevCycle = prevCycle;
		AnimOverlay[layer].LastEventCheck = prevCycle;
		AnimOverlay[layer].MarkActive();
	}

	public void SetLayerCycle(int layer, TimeUnit_t cycle, float prevCycle, TimeUnit_t lastEventCheck) {
		if (!IsValidLayer(layer))
			return;

		if (!AnimOverlay[layer].Looping) {
			cycle = Math.Clamp(cycle, 0.0, 1.0);
			prevCycle = Math.Clamp(prevCycle, 0.0f, 1.0f);
		}

		AnimOverlay[layer].Cycle = cycle;
		AnimOverlay[layer].PrevCycle = prevCycle;
		AnimOverlay[layer].LastEventCheck = lastEventCheck;
		AnimOverlay[layer].MarkActive();
	}

	public TimeUnit_t GetLayerCycle(int layer) {
		if (!IsValidLayer(layer))
			return 0.0;

		return AnimOverlay[layer].Cycle;
	}

	public void SetLayerPlaybackRate(int layer, TimeUnit_t playbackRate) {
		if (!IsValidLayer(layer))
			return;

		Assert(playbackRate > -1.0 && playbackRate < 40.0);

		AnimOverlay[layer].PlaybackRate = playbackRate;
	}

	public void SetLayerWeight(int layer, float weight) {
		if (!IsValidLayer(layer))
			return;

		weight = Math.Clamp(weight, 0.0f, 1.0f);
		AnimOverlay[layer].Weight = weight;
		AnimOverlay[layer].MarkActive();
	}

	public float GetLayerWeight(int layer) {
		if (!IsValidLayer(layer))
			return 0.0f;

		return AnimOverlay[layer].Weight;
	}

	public void SetLayerAutokill(int layer, bool autokill) {
		if (!IsValidLayer(layer))
			return;

		if (autokill)
			AnimOverlay[layer].Flags |= AnimLayerFlags.AutoKill;
		else
			AnimOverlay[layer].Flags &= ~AnimLayerFlags.AutoKill;
	}

	public void SetLayerNoRestore(int layer, bool noRestore) {
		if (!IsValidLayer(layer))
			return;

		if (noRestore)
			AnimOverlay[layer].Flags |= AnimLayerFlags.DontRestore;
		else
			AnimOverlay[layer].Flags &= ~AnimLayerFlags.DontRestore;
	}

	public void SetLayerLooping(int layer, bool looping) {
		if (!IsValidLayer(layer))
			return;

		AnimOverlay[layer].Looping = looping;
	}

	public Activity GetLayerActivity(int layer) {
		if (!IsValidLayer(layer))
			return Activity.ACT_INVALID;

		return AnimOverlay[layer].Activity;
	}

	public int GetLayerSequence(int layer) {
		if (!IsValidLayer(layer))
			return -1;

		return AnimOverlay[layer].Sequence;
	}

	public void RemoveLayer(int layer, float killRate = 0.2f, float killDelay = 0.0f) {
		if (!IsValidLayer(layer))
			return;

		if (killRate > 0)
			AnimOverlay[layer].KillRate = AnimOverlay[layer].Weight / killRate;
		else
			AnimOverlay[layer].KillRate = 100;

		AnimOverlay[layer].KillDelay = killDelay;
		AnimOverlay[layer].KillMe();
	}

	public void FastRemoveLayer(int layer) {
		if (!IsValidLayer(layer))
			return;

		for (int j = 0; j < AnimOverlay.Count; j++) {
			if (AnimOverlay[j].IsActive() && AnimOverlay[j].Order > AnimOverlay[layer].Order)
				AnimOverlay[j].Order--;
		}
		AnimOverlay[layer].Init(this);

		VerifyOrder();
	}

	public bool HasActiveLayer() {
		for (int j = 0; j < AnimOverlay.Count; j++) {
			if (AnimOverlay[j].IsActive())
				return true;
		}

		return false;
	}
}
