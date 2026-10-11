using Game.Shared;

using Source.Common;

using System.Numerics;
using System.Runtime.CompilerServices;

using FIELD = Source.FIELD<Game.Client.C_BaseAnimatingOverlay>;

namespace Game.Client;

[NetworkName("CBaseAnimatingOverlay")]
public partial class C_BaseAnimatingOverlay : C_BaseAnimating
{
	public static readonly RecvTable DT_OverlayVars = new(nameof(DT_OverlayVars), [
		RecvPropList<AnimationLayerRef>(FIELD.OF_LIST(nameof(AnimOverlay), MAX_OVERLAYS), ResizeAnimationLayerCallback, RecvPropDataTable(null!, AnimationLayerRef.DT_Animationlayer))
	]);

	public C_BaseAnimatingOverlay() : base() {
		for (int i = 0; i < MAX_OVERLAYS; i++) {
			AnimationLayerRef r = new AnimationLayerRef();
			AnimOverlay.Add(new());
			var iv = new InterpolatedVar<AnimationLayer>();
			iv.Setup(r, AnimationLayerRef.Accessor, LatchFlags.LatchAnimationVar);
			iv_AnimOverlay.Add(iv);
		}
	}

	public int ActiveOverlayCount = 0;


	public static readonly string[] iv_AnimOverlayNames = [
		"C_BaseAnimatingOverlay.iv_AnimOverlay00",
		"C_BaseAnimatingOverlay.iv_AnimOverlay01",
		"C_BaseAnimatingOverlay.iv_AnimOverlay02",
		"C_BaseAnimatingOverlay.iv_AnimOverlay03",
		"C_BaseAnimatingOverlay.iv_AnimOverlay04",
		"C_BaseAnimatingOverlay.iv_AnimOverlay05",
		"C_BaseAnimatingOverlay.iv_AnimOverlay06",
		"C_BaseAnimatingOverlay.iv_AnimOverlay07",
		"C_BaseAnimatingOverlay.iv_AnimOverlay08",
		"C_BaseAnimatingOverlay.iv_AnimOverlay09",
		"C_BaseAnimatingOverlay.iv_AnimOverlay10",
		"C_BaseAnimatingOverlay.iv_AnimOverlay11",
		"C_BaseAnimatingOverlay.iv_AnimOverlay12",
		"C_BaseAnimatingOverlay.iv_AnimOverlay13",
		"C_BaseAnimatingOverlay.iv_AnimOverlay14"
	];



	private static void ResizeAnimationLayerCallback(object instance, object list, int len) {
		C_BaseAnimatingOverlay ent = (C_BaseAnimatingOverlay)instance;
		List<AnimationLayerRef> vec = ent.AnimOverlay;
		List<InterpolatedVar<AnimationLayer>> iv = ent.iv_AnimOverlay;

		Assert(list == vec);

		if (len == ent.ActiveOverlayCount)
			return;

		for (int i = 0; i < ent.ActiveOverlayCount; i++)
			ent.RemoveVar(vec[i], AnimationLayerRef.Accessor);

		ent.ActiveOverlayCount = len;

		for (int i = 0; i < len; i++) {
			IInterpolatedVar watcher = iv[i];
			watcher.SetDebugName(iv_AnimOverlayNames[i]);
			ent.AddVar(vec[i], AnimationLayerRef.Accessor, watcher, LatchFlags.LatchAnimationVar, true);
		}
	}


	public static readonly RecvTable DT_BaseAnimatingOverlay = new(DT_BaseAnimating, [
		RecvPropDataTable("overlay_vars", DT_OverlayVars)
	]); public static readonly new ClientClass ClientClass = new ClientClass(null, null, DT_BaseAnimatingOverlay);

	[NetworkName("m_AnimOverlay")]
	readonly List<AnimationLayerRef> AnimOverlay = [];
	readonly List<InterpolatedVar<AnimationLayer>> iv_AnimOverlay = [];
	public readonly float[] OverlayPrevEventCycle = new float[MAX_OVERLAYS];

	public AnimationLayerRef GetAnimOverlay(int i) => AnimOverlay[i];

	public int GetNumAnimOverlays() => ActiveOverlayCount;
	public void SetNumAnimOverlays(int num) {
		ActiveOverlayCount = num;
	}
	public void CheckForLayerChanges(StudioHdr hdr, TimeUnit_t currentTime) {
		bool layersChanged = false;

		// FIXME: damn, there has to be a better way than this.
		int i;
		for (i = 0; i < ActiveOverlayCount; i++) {
			iv_AnimOverlay[i].GetInterpolationInfo(currentTime, out int iHead, out int iPrev1, out int iPrev2);

			// fake up previous cycle values.
			ref AnimationLayer head = ref iv_AnimOverlay[i].GetHistoryValue(iHead, out double t0);
			// reset previous
			ref AnimationLayer prev1 = ref iv_AnimOverlay[i].GetHistoryValue(iPrev1, out double t1);
			// reset previous previous
			ref AnimationLayer prev2 = ref iv_AnimOverlay[i].GetHistoryValue(iPrev2, out double t2);

			if (!Unsafe.IsNullRef(ref head) && !Unsafe.IsNullRef(ref prev1) && head.Sequence != prev1.Sequence) {
				layersChanged = true;

				if (!Unsafe.IsNullRef(ref prev1)) {
					prev1.Sequence = head.Sequence;
					prev1.Cycle = head.PrevCycle;
					prev1.Weight = head.Weight;
				}

				if (!Unsafe.IsNullRef(ref prev2)) {
					double num = 0;
					if (Math.Abs(t0 - t1) > 0.001)
						num = (t2 - t1) / (t0 - t1);

					prev2.Sequence = head.Sequence;
					double flTemp;
					if (IsSequenceLooping(hdr, head.Sequence))
						flTemp = LerpFunctions.LoopingLerp(num, head.PrevCycle, head.Cycle);
					else
						flTemp = LerpFunctions.Lerp(num, head.PrevCycle, head.Cycle);
					prev2.Cycle = flTemp;
					prev2.Weight = head.Weight;
				}

				iv_AnimOverlay[i].SetLooping(IsSequenceLooping(hdr, head.Sequence));
				iv_AnimOverlay[i].Interpolate(currentTime);

				// reset event indexes
				OverlayPrevEventCycle[i] = head.PrevCycle - 0.01f;
			}
		}

		if (layersChanged)
			// render bounds may have changed
			UpdateVisibility();
	}
	public override void AccumulateLayers(ref BoneSetup boneSetup, Span<Vector3> pos, Span<Quaternion> q, double currentTime) {
		base.AccumulateLayers(ref boneSetup, pos, q, currentTime);

		int i;
		// resort the layers
		Span<int> layer = stackalloc int[MAX_OVERLAYS];
		for (i = 0; i < MAX_OVERLAYS; i++)
			layer[i] = MAX_OVERLAYS;

		for (i = 0; i < ActiveOverlayCount; i++) {
			if (AnimOverlay[i].Order < MAX_OVERLAYS) {
				if (layer[AnimOverlay[i].Order] != MAX_OVERLAYS)
					AnimOverlay[i].Order = MAX_OVERLAYS;
				else
					layer[AnimOverlay[i].Order] = i;
			}
		}

		CheckForLayerChanges(boneSetup.GetStudioHdr(), currentTime);

		int nSequences = boneSetup.GetStudioHdr().GetNumSeq();

		// add in the overlay layers
		int j;
		for (j = 0; j < MAX_OVERLAYS; j++) {
			i = layer[j];
			if (i < ActiveOverlayCount) {
				if (AnimOverlay[i].Sequence >= nSequences)
					continue;

				AnimOverlay[i].BlendWeight();

				float fWeight = AnimOverlay[i].Weight;

				if (fWeight > 0) {
					// check to see if the sequence changed
					// FIXME: move this to somewhere more reasonable
					// do a nice spline interpolation of the values
					// if ( m_AnimOverlay[i].m_nSequence != m_iv_AnimOverlay.GetPrev( i )->nSequence )
					TimeUnit_t fCycle = AnimOverlay[i].Cycle;

					fCycle = ClampCycle(fCycle, IsSequenceLooping(AnimOverlay[i].Sequence));

					if (fWeight > 1)
						fWeight = 1;

					boneSetup.AccumulatePose(pos, q, AnimOverlay[i].Sequence, fCycle, fWeight, currentTime, null);
				}
			}
		}
	}
	public override void DoAnimationEvents(StudioHdr hdr) {
		base.DoAnimationEvents(hdr);
	}
	protected override StudioHdr? OnNewModel() {
		StudioHdr? hdr = base.OnNewModel();
		for (int i = 0; i < ActiveOverlayCount; i++) {
			AnimOverlay[i].Reset();
			AnimOverlay[i].Order = MAX_OVERLAYS;
		}
		return hdr;
	}
}
