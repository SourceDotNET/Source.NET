// Made shared to avoid code duplication which was annoying me in the base animating overlay classes.

#if CLIENT_DLL
using Game.Client;
#endif
#if GAME_DLL
using Game.Server;
#endif

using Source.Common;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Game.Shared;

#if CLIENT_DLL || GAME_DLL

[Flags]
public enum AnimLayerFlags : int
{
	Active = 0x0001,
	AutoKill = 0x0002,
	KillMe = 0x0004,
	DontRestore = 0x0008,
	CheckAccess = 0x0010,
	Dying = 0x0020,
}

public record struct AnimationLayer
{
	public AnimLayerFlags Flags;
	public bool SequenceFinished;
	public bool Looping;

	[NetworkVar("m_nSequence")]
	public int Sequence;
	[NetworkVar("m_flCycle")]
	public TimeUnit_t Cycle;
	[NetworkVar("m_flPrevCycle")]
	public float PrevCycle;
	[NetworkVar("m_flWeight")]
	public float Weight;
	[NetworkVar("m_nOrder")]
	public int Order;

	public TimeUnit_t PlaybackRate;
	public TimeUnit_t LayerAnimtime;
	public TimeUnit_t LayerFadeOuttime;
	public double BlendIn;
	public double BlendOut;
	public bool ClientBlend;
#if GAME_DLL
	public float KillRate;
	public float KillDelay;

	// For checking for duplicates
	public Activity Activity;

	// order of layering on client
	public int Priority;

	public TimeUnit_t LastEventCheck;
	public TimeUnit_t LastAccess;
#endif

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsActive() => ((Flags & AnimLayerFlags.Active) != 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsAutokill() => ((Flags & AnimLayerFlags.AutoKill) != 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsKillMe() => ((Flags & AnimLayerFlags.KillMe) != 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsAutoramp() => (BlendIn != 0.0f || BlendOut != 0.0f);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void KillMe() => Flags |= AnimLayerFlags.KillMe;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void Dying() => Flags |= AnimLayerFlags.Dying;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsDying() => ((Flags & AnimLayerFlags.Dying) != 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void Dead() => Flags &= ~AnimLayerFlags.Dying;
	public void SetOrder(int order) => Order = order;
#if CLIENT_DLL
	public static AnimationLayer LoopingLerp(TimeUnit_t percent, in AnimationLayer from, in AnimationLayer to) {
		AnimationLayer output = default;

		output.Sequence = to.Sequence;
		output.Cycle = LerpFunctions.LoopingLerp(percent, (float)from.Cycle, (float)to.Cycle);
		output.PrevCycle = to.PrevCycle;
		output.Weight = LerpFunctions.Lerp(percent, from.Weight, to.Weight);
		output.Order = to.Order;

		output.LayerAnimtime = to.LayerAnimtime;
		output.LayerFadeOuttime = to.LayerFadeOuttime;
		return output;
	}
	public static AnimationLayer Lerp(TimeUnit_t percent, in AnimationLayer from, in AnimationLayer to) {
		AnimationLayer output = default;

		output.Sequence = to.Sequence;
		output.Cycle = LerpFunctions.Lerp(percent, from.Cycle, to.Cycle);
		output.PrevCycle = to.PrevCycle;
		output.Weight = LerpFunctions.Lerp(percent, from.Weight, to.Weight);
		output.Order = to.Order;

		output.LayerAnimtime = to.LayerAnimtime;
		output.LayerFadeOuttime = to.LayerFadeOuttime;
		return output;
	}
	public static AnimationLayer LoopingLerp_Hermite(TimeUnit_t percent, in AnimationLayer prev, in AnimationLayer from, in AnimationLayer to) {
		AnimationLayer output = default;

		output.Sequence = to.Sequence;
		output.Cycle = LerpFunctions.LoopingLerp_Hermite(percent, (float)prev.Cycle, (float)from.Cycle, (float)to.Cycle);
		output.PrevCycle = to.PrevCycle;
		output.Weight = LerpFunctions.Lerp(percent, from.Weight, to.Weight);
		output.Order = to.Order;

		output.LayerAnimtime = to.LayerAnimtime;
		output.LayerFadeOuttime = to.LayerFadeOuttime;
		return output;
	}
	public static AnimationLayer Lerp_Hermite(TimeUnit_t percent, in AnimationLayer prev, in AnimationLayer from, in AnimationLayer to) {
		AnimationLayer output = default;

		output.Sequence = to.Sequence;
		output.Cycle = LerpFunctions.Lerp_Hermite(percent, prev.Cycle, from.Cycle, to.Cycle);
		output.PrevCycle = to.PrevCycle;
		output.Weight = LerpFunctions.Lerp(percent, from.Weight, to.Weight);
		output.Order = to.Order;

		output.LayerAnimtime = to.LayerAnimtime;
		output.LayerFadeOuttime = to.LayerFadeOuttime;
		return output;
	}
#endif
	public double GetFadeout(double curTime) {
		double s;

		if (LayerFadeOuttime <= 0.0f) {
			s = 0;
		}
		else {
			// blend in over 0.2 seconds
			s = 1.0 - (curTime - LayerAnimtime) / LayerFadeOuttime;
			if (s > 0 && s <= 1.0f) {
				// do a nice spline curve
				s = 3 * s * s - 2 * s * s * s;
			}
			else if (s > 1.0f) {
				// Shouldn't happen, but maybe curtime is behind animtime?
				s = 1.0;
			}
		}

		return s;
	}

	public void Reset() {
		Sequence = 0;
		PrevCycle = 0;
		Weight = 0;
		PlaybackRate = 0;
		Cycle = 0;
		LayerAnimtime = 0;
		LayerFadeOuttime = 0;
		BlendIn = 0;
		BlendOut = 0;
		ClientBlend = false;
	}

#if GAME_DLL
	public BaseAnimatingOverlay? OwnerEntity;

	public void Init(BaseAnimatingOverlay? overlay) {
		OwnerEntity = overlay;
		Flags = 0;
		Weight = 0;
		Cycle = 0;
		PrevCycle = 0;
		SequenceFinished = false;
		Activity = Activity.ACT_INVALID;
		Sequence = 0;
		Priority = 0;
		Order = BaseAnimatingOverlay.MAX_OVERLAYS;

		BlendIn = 0.0;
		BlendOut = 0.0;

		KillRate = 100.0f;
		KillDelay = 0.0f;
		PlaybackRate = 1.0;
		LastEventCheck = 0.0;
		LastAccess = gpGlobals.CurTime;
		LayerAnimtime = 0;
		LayerFadeOuttime = 0;
	}

	public void StudioFrameAdvance(TimeUnit_t interval, BaseAnimating owner) {
		float cycleRate = owner.GetSequenceCycleRate(Sequence);

		PrevCycle = (float)Cycle;
		Cycle += interval * cycleRate * PlaybackRate;

		if (Cycle < 0.0) {
			if (Looping)
				Cycle -= (int)Cycle;
			else
				Cycle = 0;
		}
		else if (Cycle >= 1.0) {
			SequenceFinished = true;

			if (Looping)
				Cycle -= (int)Cycle;
			else
				Cycle = 1.0;
		}

		if (IsAutoramp()) {
			Weight = 1;

			if (BlendIn != 0.0f)
				if (Cycle < BlendIn)
					Weight = (float)(Cycle / BlendIn);

			if (BlendOut != 0.0f)
				if (Cycle > 1.0 - BlendOut)
					Weight = (float)((1.0 - Cycle) / BlendOut);

			Weight = 3.0f * Weight * Weight - 2.0f * Weight * Weight * Weight;
			if (Sequence == 0)
				Weight = 0;
		}
	}

	public bool IsAbandoned() {
		if (IsActive() && !IsAutokill() && !IsKillMe() && LastAccess > 0.0 && (gpGlobals.CurTime - LastAccess > 0.2))
			return true;
		else
			return false;
	}

	public void MarkActive() => LastAccess = gpGlobals.CurTime;
#endif

	public void BlendWeight() {
		if (!ClientBlend)
			return;

		Weight = 1;

		// blend in?
		if (BlendIn != 0.0f)
			if (Cycle < BlendIn)
				Weight = (float)(Cycle / BlendIn);

		// blend out?
		if (BlendOut != 0.0f)
			if (Cycle > 1.0f - BlendOut)
				Weight = (float)((1.0f - (float)(Cycle)) / BlendOut);

		Weight = 3.0f * (float)(Weight) * (float)(Weight) * (3.0f - 2.0f * (float)(Weight));
		if (Sequence == 0)
			Weight = 0;
	}
}

/// <summary>
/// Because of FieldILAccess, these must be class instances. So this is basically just a wrapper around the AnimationLayer struct.
/// </summary>
public partial class AnimationLayerRef
{
	[NetworkTable("DT_Animationlayer")]
	public static readonly NetworkProp[] Props = [
		new(nameof(Struct.Sequence)) { Bits = SharedDefs.ANIMATION_SEQUENCE_BITS, Flags = PropFlags.Unsigned },
		new(nameof(Struct.Cycle)) { Encoding = NetEncoding.Float, Bits = SharedDefs.ANIMATION_CYCLE_BITS, Flags = PropFlags.RoundDown, Low = 0.0f, High = 1.0f },
		new(nameof(Struct.PrevCycle)) { Bits = SharedDefs.ANIMATION_CYCLE_BITS, Flags = PropFlags.RoundDown, Low = 0.0f, High = 1.0f },
		new(nameof(Struct.Weight)) { Bits = AnimationLayerRef.WEIGHT_BITS, Low = 0.0f, High = 1.0f },
		new(nameof(Struct.Order)) { Bits = AnimationLayerRef.ORDER_BITS, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("")]
	public AnimationLayer Struct;
	public static DynamicAccessor Accessor = Source.FIELD<AnimationLayerRef>.OF(nameof(Struct));

	public ref AnimLayerFlags Flags { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.Flags; }
	public ref bool SequenceFinished { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.SequenceFinished; }
	public ref bool Looping { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.Looping; }
	public ref int Sequence { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.Sequence; }
	public ref TimeUnit_t Cycle { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.Cycle; }
	public ref float PrevCycle { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.PrevCycle; }
	public ref float Weight { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.Weight; }
	public ref int Order { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.Order; }
	public ref TimeUnit_t PlaybackRate { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.PlaybackRate; }
	public ref TimeUnit_t LayerAnimtime { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.LayerAnimtime; }
	public ref TimeUnit_t LayerFadeOuttime { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.LayerFadeOuttime; }
	public ref double BlendIn { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.BlendIn; }
	public ref double BlendOut { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.BlendOut; }
	public ref bool ClientBlend { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.ClientBlend; }
#if GAME_DLL
	public ref float KillRate { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.KillRate; }
	public ref float KillDelay { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.KillDelay; }
	public ref Activity Activity { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.Activity; }
	public ref int Priority { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.Priority; }
	public ref TimeUnit_t LastEventCheck { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.LastEventCheck; }
	public ref TimeUnit_t LastAccess { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref Struct.LastAccess; }
#endif

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsActive() => Struct.IsActive();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsAutokill() => Struct.IsAutokill();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsKillMe() => Struct.IsKillMe();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsAutoramp() => Struct.IsAutoramp();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void KillMe() => Struct.KillMe();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void Dying() => Struct.Dying();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsDying() => Struct.IsDying();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void Dead() => Struct.Dead();

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public double GetFadeout(double curtime) => Struct.GetFadeout(curtime);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void Reset() => Struct.Reset();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void BlendWeight() => Struct.BlendWeight();
#if GAME_DLL
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void Init(BaseAnimatingOverlay? overlay) => Struct.Init(overlay);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void StudioFrameAdvance(TimeUnit_t interval, BaseAnimating owner) => Struct.StudioFrameAdvance(interval, owner);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsAbandoned() => Struct.IsAbandoned();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void MarkActive() => Struct.MarkActive();
#endif
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetOrder(int order) => Struct.SetOrder(order);

	public const int ORDER_BITS = 4;
	public const int WEIGHT_BITS = 8;
}

#endif
