global using static Game.Client.C_BaseAnimatingGlobals;

using CommunityToolkit.HighPerformance;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;

using System;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

using ClientModelRenderInfo = Source.Common.Engine.ModelRenderInfo;
using Color = Source.Color;
using DEFINE = Source.DEFINE<Game.Client.C_BaseAnimating>;
using FIELD = Source.FIELD<Game.Client.C_BaseAnimating>;
using FIELD_ILR = Source.FIELD<Game.Client.C_InfoLightingRelative>;
namespace Game.Client;

[NetworkName("CInfoLightingRelative")]
public partial class C_InfoLightingRelative : C_BaseEntity
{
	public static readonly RecvTable DT_InfoLightingRelative = new(DT_BaseEntity, [
		RecvPropEHandle(FIELD_ILR.OF(nameof(LightingLandmark))),
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_InfoLightingRelative);

	[NetworkName("m_hLightingLandmark")]
	public EHANDLE LightingLandmark = new();
}

public static class C_BaseAnimatingGlobals
{
	public const ClientSideAnimationListHandle_t INVALID_CLIENTSIDEANIMATION_LIST_HANDLE = unchecked((ClientSideAnimationListHandle_t)(~0));
}

[NetworkName("CBaseAnimating")]
public partial class C_BaseAnimating : C_BaseEntity, IModelLoadCallback
{
	// They did some REALLY weird stuff for access tags...
	public record struct BoneAccessTag
	{
		public nint Data;
		public string? Str;

		public BoneAccessTag() { }
		public BoneAccessTag(ReadOnlySpan<char> text) { Str = new(text); }

		public static explicit operator BoneAccessTag(nint i) => new() { Data = i };
		public static explicit operator BoneAccessTag(string str) => new() { Str = str };
		public static implicit operator bool(BoneAccessTag tag) => tag.Data != 0 || tag.Str != null;
		public static implicit operator ReadOnlySpan<char>(BoneAccessTag tag) => tag.Str;
	}
	static readonly ConVar r_drawothermodels = new("1", FCvar.Cheat, "0=Off, 1=Normal, 2=Wireframe");
	static readonly HashSet<C_BaseAnimating> PreviousBoneSetups = [];
	static readonly Dictionary<int, double> AnimDebugLastLogTime = [];

	public static void PushAllowBoneAccess(bool allowForNormalModels, bool allowForViewModels, BoneAccessTag tag) {
		lock (BoneAccessMutex) {
			BoneAccess save = BoneAccessBase;
			BoneAccessStack.Push(save);

			BoneAccessBase.AllowBoneAccessForNormalModels = allowForNormalModels;
			BoneAccessBase.AllowBoneAccessForViewModels = allowForViewModels;
			BoneAccessBase.Tag = tag;
		}
	}

	public int GetNumBodyGroups() => Animation.GetNumBodyGroups(GetModelPtr());

	public void SetBodygroup(int group, int value) {
		Animation.SetBodygroup(GetModelPtr(), ref Body, group, value);
	}

	float GroundSpeed;
	const TimeUnit_t MAX_ANIMTIME_INTERVAL = 0.2;
	public TimeUnit_t GetAnimTimeInterval() => Math.Min(gpGlobals.CurTime - AnimTime, MAX_ANIMTIME_INTERVAL);

	public bool IsSelfAnimating() {
		if (ClientSideAnimation)
			return true;

		switch (GetMoveType()) {
			case Source.MoveType.Step:
			case Source.MoveType.None:
			case Source.MoveType.Walk:
			case Source.MoveType.Fly:
			case Source.MoveType.FlyGravity:
				return false;
			default:
				return true;
		}
	}
	public bool IsSequenceFinished() => SequenceFinished;
	public override void ResetLatched() {
		Ik = null;

		base.ResetLatched();
	}
	public bool IsRagdoll() => Ragdoll != null && RenderFX == (byte)RenderFx.Ragdoll;

	public override ShadowType ShadowCastType() {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null || !studioHdr.SequencesAvailable())
			return ShadowType.None;

		if (IsEffectActive(EntityEffects.NoDraw | EntityEffects.NoShadow))
			return ShadowType.None;

		if (studioHdr.GetNumSeq() == 0)
			return ShadowType.RenderToTexture;

		if (!IsRagdoll()) {
			if (studioHdr.GetNumPoseParameters() > 0)
				return ShadowType.RenderToTextureDynamic;

			if (studioHdr.GetRenderHdr().NumBoneControllers > 0)
				return ShadowType.RenderToTextureDynamic;

			if (studioHdr.GetRenderHdr().NumIKChains > 0)
				return ShadowType.RenderToTextureDynamic;
		}

		return ShadowType.RenderToTexture;
	}
	public bool IsAboutToRagdoll() => RenderFX == (byte)RenderFx.Ragdoll;
	public void StudioFrameAdvance() {
		if (ClientSideAnimation)
			return;

		StudioHdr? hdr = GetModelPtr();
		if (hdr == null)
			return;

		//if (!anim.prevanimtime)
		//{
		//anim.prevanimtime = m_flAnimTime = gpGlobals->curtime;
		//}

		// How long since last animtime
		TimeUnit_t flInterval = GetAnimTimeInterval();

		if (flInterval <= 0.001) {
			// Msg("%s : %s : %5.3f (skip)\n", STRING(pev->classname), GetSequenceName( GetSequence() ), GetCycle() );
			return;
		}

		UpdateModelScale();

		//anim.prevanimtime = m_flAnimTime;
		TimeUnit_t cycleAdvance = flInterval * GetSequenceCycleRate(hdr, GetSequence()) * PlaybackRate;
		TimeUnit_t flNewCycle = GetCycle() + cycleAdvance;
		AnimTime = gpGlobals.CurTime;

		if (flNewCycle < 0.0f || flNewCycle >= 1.0f) {
			if (IsSequenceLooping(hdr, GetSequence())) {
				flNewCycle -= (int)(flNewCycle);
			}
			else {
				flNewCycle = (flNewCycle < 0.0f) ? 0.0f : 1.0f;
			}

			SequenceFinished = true; // just in case it wasn't caught in GetEvents
		}

		SetCycle(flNewCycle);

		// todo: eval
		GroundSpeed = (float)GetSequenceGroundSpeed(hdr, GetSequence()) * GetModelScale();
	}

	public TimeUnit_t GetSequenceMoveDist(StudioHdr? studioHdr, int sequence) {
		Animation.GetSequenceLinearMotion(studioHdr, sequence, PoseParameter, out Vector3 vecReturn);

		return vecReturn.Length();
	}
	public TimeUnit_t GetSequenceGroundSpeed(int sequence) => GetSequenceGroundSpeed(GetModelPtr(), sequence);
	static string? Studio_GetKeyValueText(StudioHdr? studioHdr, int sequence) {
		if (studioHdr != null && studioHdr.SequencesAvailable()) {
			if (sequence >= 0 && sequence < studioHdr.GetNumSeq()) {
				MStudioSeqDesc seqdesc = studioHdr.Seqdesc(sequence);
				if (seqdesc.KeyValueSize != 0)
					return System.Text.Encoding.ASCII.GetString(((ReadOnlySpan<byte>)seqdesc.Data.Span[seqdesc.KeyValueIndex..]).SliceNullTerminatedString());
			}
		}
		return null;
	}

	public KeyValues? GetSequenceKeyValues(int sequence) {
		string? text = Studio_GetKeyValueText(GetModelPtr(), sequence);

		if (text != null) {
			KeyValues seqKeyValues = new("");
			if (seqKeyValues.LoadFromBuffer(modelinfo.GetModelName(GetModel()), text))
				return seqKeyValues;
		}
		return null;
	}

	public TimeUnit_t GetSequenceGroundSpeed(StudioHdr? studioHdr, int sequence) {
		TimeUnit_t t = SequenceDuration(studioHdr, sequence);
		if (t > 0)
			return GetSequenceMoveDist(studioHdr, sequence) / t;
		else
			return 0;
	}

	public virtual void UpdateModelScale() {
		// todo
	}

	bool SequenceLoops;
	bool SequenceFinished;
	int LastEventCheck;

	public static void PopAllowBoneAccess(BoneAccessTag tag) {
		lock (BoneAccessMutex) {
			Assert(BoneAccessBase.Tag == tag || (BoneAccessBase.Tag && BoneAccessBase.Tag != (BoneAccessTag)1 && tag && tag != (BoneAccessTag)1 && strcmp(BoneAccessBase.Tag, tag) == 0));
			int lastIndex = BoneAccessStack.Count - 1;
			if (lastIndex < 0) {
				AssertMsg(false, "C_BaseAnimating.PopBoneAccess:  Stack is empty!!!");
				return;
			}
			BoneAccessStack.Pop();
			if (lastIndex != 0)
				BoneAccessBase = BoneAccessStack.Peek();
			else
				BoneAccessBase = default;
		}
	}

	public struct BoneAccess
	{
		public bool AllowBoneAccessForNormalModels;
		public bool AllowBoneAccessForViewModels;
		public BoneAccessTag Tag;
	}

	static readonly object BoneAccessMutex = new();
	static readonly Stack<BoneAccess> BoneAccessStack = [];
	static BoneAccess BoneAccessBase;

	public ref struct AutoAllowBoneAccess : IDisposable
	{
		public AutoAllowBoneAccess(bool allowForNormalModes, bool allowForViewModels) {
			C_BaseAnimating.PushAllowBoneAccess(allowForNormalModes, allowForViewModels, (BoneAccessTag)1);
		}
		public void Dispose() {
			C_BaseAnimating.PopAllowBoneAccess((BoneAccessTag)1);
		}
	}

	public bool IsBoneAccessAllowed() => IsViewModel() ? BoneAccessBase.AllowBoneAccessForViewModels : BoneAccessBase.AllowBoneAccessForNormalModels;


	public TimeUnit_t GetPlaybackRate() => PlaybackRate;
	public void SetPlaybackRate(TimeUnit_t rate) => PlaybackRate = (float)rate; // todo: double?
	public ref readonly Matrix3x4 GetBone(int bone) => ref BoneAccessor.GetBone(bone);
	public ref Matrix3x4 GetBoneForWrite(int bone) => ref BoneAccessor.GetBoneForWrite(bone);

	BoneMergeCache? BoneMergeCache;

	static long ModelBoneCounter;
	long MostRecentModelBoneCounter;
	long MostRecentBoneSetupRequest;
	int PrevBoneMask;
	protected int AccumulatedBoneMask;
	readonly BoneAccessor BoneAccessor = new();
	// Note that Handle is not a pointer in this case so we need to initialize
	// each member in the bone attachments (when we get to this part).
	readonly List<Handle<C_BaseAnimating>> BoneAttachments = [];
	int BoneIndexAttached;
	Vector3 BonePosition;
	QAngle BoneAngles;
	Handle<C_BaseAnimating> AttachedTo = new();
	readonly List<Matrix3x4> CachedBoneData = [];

	public void InvalidateBoneCache() {
		MostRecentModelBoneCounter = ModelBoneCounter - 1;
		LastBoneSetupTime = -TimeUnit_t.MaxValue;
	}
	public virtual void DoAnimationEvents(StudioHdr hdr) {

	}
	public bool IsBoneCacheValid() => MostRecentModelBoneCounter == ModelBoneCounter;
	public static void InvalidateBoneCaches() => ModelBoneCounter++;

	public TimeUnit_t LastBoneSetupTime;
	public virtual TimeUnit_t LastBoneChangedTime() => TimeUnit_t.MaxValue;

	static TimeUnit_t SetupBones__lastWarning = 0.0;
	// TODO: REWRITE THIS FOR BONEMERGING
	public void BuildTransformations(StudioHdr? hdr, Span<Vector3> pos, Span<Quaternion> q, in Matrix3x4 cameraTransform, int boneMask, ref BoneBitList boneComputed) {
		if (hdr == null)
			return;

		Matrix3x4 bonematrix = default;
		Span<bool> boneSimulated = stackalloc bool[Studio.MAXSTUDIOBONES];

		// no bones have been simulated
		MStudioBone pbones = hdr.Bone(0);

		// todo
		if (Ragdoll != null) {

		}

		// For EF_BONEMERGE entities, copy the bone matrices for any bones that have matching names.
		bool boneMerge = IsEffectActive(EntityEffects.BoneMerge);
		if (boneMerge || BoneMergeCache != null) {
			if (boneMerge) {
				if (BoneMergeCache == null) {
					BoneMergeCache = new BoneMergeCache();
					BoneMergeCache.Init(this);
				}
				BoneMergeCache.MergeMatchingBones(boneMask);
			}
			else
				BoneMergeCache = null;
		}

		for (int i = 0; i < hdr.NumBones(); i++) {
			// Only update bones reference by the bone mask.
			if ((hdr.BoneFlags(i) & boneMask) == 0)
				continue;

			if (BoneMergeCache != null && BoneMergeCache.IsBoneMerged(i) != 0)
				continue;

			// animate all non-simulated bones
			// todo: || CalcProceduralBone(hdr, i, BoneAccessor)
			if (boneSimulated[i])
				continue;

			// skip bones that the IK has already setup
			else if (boneComputed.IsBoneMarked(i)) {
				// dummy operation, just used to verify in debug that this should have happened
				GetBoneForWrite(i);
			}
			else {
				MathLib.QuaternionMatrix(in q[i], in pos[i], out bonematrix);

				Assert(MathF.Abs(pos[i].X) < 100000);
				Assert(MathF.Abs(pos[i].Y) < 100000);
				Assert(MathF.Abs(pos[i].Z) < 100000);

				// todo
				// JiggleBones.BuildJiggleTransformations(i, gpGlobals.RealTime, jiggleInfo, goalMX, GetBoneForWrite(i));

				if (hdr.BoneParent(i) == -1)
					MathLib.ConcatTransforms(cameraTransform, bonematrix, out GetBoneForWrite(i));
				else
					MathLib.ConcatTransforms(GetBone(hdr.BoneParent(i)), bonematrix, out GetBoneForWrite(i));
			}

			if (hdr.BoneParent(i) == -1)
				// Apply client-side effects to the transformation matrix
				ApplyBoneMatrixTransform(ref GetBoneForWrite(i));
		}

		// dimhotepus: Fix jittery model rendering when spectating a ragdoll.
		if (Ragdoll != null) {
			C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
			if (player != null) {
				// todo
			}
		}
	}

	protected virtual void ApplyBoneMatrixTransform(ref Matrix3x4 transform) {
		switch ((RenderFx)RenderFX) {
			case RenderFx.Distort:
			case RenderFx.Hologram:
				if (RandomInt(0, 49) == 0) {
					int axis = RandomInt(0, 1);
					if (axis == 1)
						axis = 2;
					MathLib.VectorScale(transform[axis], RandomFloat(1, 1.484f), transform[axis]);
				}
				else if (RandomInt(0, 49) == 0) {
					float offset = RandomFloat(-10, 10);
					transform[RandomInt(0, 2)][3] += offset;
				}
				break;
			case RenderFx.Explode: {
					float scale = 1.0f + (float)(gpGlobals.CurTime - AnimTime) * 10.0f;
					if (scale > 2)
						scale = 2;
					transform[0, 1] *= scale;
					transform[1, 1] *= scale;
					transform[2, 1] *= scale;
				}
				break;
			default:
				break;
		}

		if (IsModelScaled()) {
			float scale = GetModelScale();

			MathLib.MatrixGetColumn(transform, 3, out Vector3 pos);
			pos -= GetRenderOrigin();
			pos *= scale;
			pos += GetRenderOrigin();
			MathLib.MatrixSetColumn(pos, 3, ref transform);

			MathLib.VectorScale(transform[0], scale, transform[0]);
			MathLib.VectorScale(transform[1], scale, transform[1]);
			MathLib.VectorScale(transform[2], scale, transform[2]);
		}
	}

	public void DelayedInitModelEffects() { /* todo */ }
	public void ClearRagdoll() { /* todo */ }

	public override void Simulate() {
		if (DelayInitModelEffects)
			DelayedInitModelEffects();

		if (gpGlobals.FrameTime != 0.0)
			DoAnimationEvents(GetModelPtr()!);

		base.Simulate();
		if (IsNoInterpolationFrame())
			ResetLatched();
		if (GetSequence() != -1 && Ragdoll != null && (RenderFX != (byte)RenderFx.Ragdoll))
			ClearRagdoll();
	}


	static readonly DynamicAccessor DA_PoseParameter = FIELD.OF_ARRAY(nameof(PoseParameter));
	static readonly DynamicAccessor DA_Cycle = FIELD.OF(nameof(Cycle));

	public void AddBaseAnimatingInterpolatedVars() {
		AddVar(this, DA_PoseParameter, iv_flPoseParameter, LatchFlags.LatchAnimationVar, true);

		LatchFlags flags = LatchFlags.LatchAnimationVar;
		if (ClientSideAnimation)
			flags |= LatchFlags.ExcludeAutoInterpolate;

		AddVar(this, DA_Cycle, iv_Cycle, flags, true);
	}

	public void RemoveBaseAnimatingInterpolatedVars() {
		RemoveVar(this, DA_PoseParameter, false);
		if (!GetPredictable())
			RemoveVar(this, DA_Cycle, false);
	}

	memhandle_t HitboxBoneCacheHandle;

	public BoneCache GetBoneCache(StudioHdr? studioHdr) {
		int boneMask = Studio.BONE_USED_BY_HITBOX;
		BoneCache cache = Studio.GetBoneCache(HitboxBoneCacheHandle);
		if (!cache.IsNull()) {
			if (cache.IsValid(gpGlobals.CurTime, 0.0))
				return cache;

			if ((cache.BoneMask & boneMask) != boneMask) {
				Studio.DestroyBoneCache(HitboxBoneCacheHandle);
				HitboxBoneCacheHandle = 0;
				cache = default;
			}
		}

		studioHdr ??= GetModelPtr();
		Assert(studioHdr != null);

		PushAllowBoneAccess(true, false, (BoneAccessTag)"GetBoneCache");
		SetupBones(null, -1, boneMask, gpGlobals.CurTime);
		PopAllowBoneAccess((BoneAccessTag)"GetBoneCache");

		if (!cache.IsNull())
			cache.UpdateBones(CachedBoneData.AsSpan(), studioHdr!.NumBones(), gpGlobals.CurTime);
		else {
			BoneCacheParams parms = new();
			parms.StudioHdr = studioHdr!;
			parms.BoneToWorld = CachedBoneData.AsSpan();
			parms.CurTime = gpGlobals.CurTime;
			parms.BoneMask = boneMask;

			HitboxBoneCacheHandle = Studio.CreateBoneCache(in parms);
			cache = Studio.GetBoneCache(HitboxBoneCacheHandle);
		}

		Assert(!cache.IsNull());
		return cache;
	}

	public override bool GetSoundSpatialization(ref SpatializationInfo info) {
		using (AutoAllowBoneAccess boneaccess = new(true, false)) {
			if (!base.GetSoundSpatialization(ref info))
				return false;
		}

		if (!Unsafe.IsNullRef(ref info.Origin) && IsNPC() && Ik != null) {
			info.Origin = GetAbsOrigin();

			modelinfo.GetModelBounds(GetModel(), out Vector3 mins, out Vector3 maxs);
			MathLib.VectorAdd(mins, maxs, out Vector3 center);
			MathLib.VectorScale(center, 0.5f, out center);

			info.Origin += center;
		}
		return true;
	}

	public override bool SetupBones(Span<Matrix3x4> boneToWorldOut, int maxBones, int boneMask, double currentTime) {
		if (!boneToWorldOut.IsEmpty && !IsBoneAccessAllowed()) {
			if (gpGlobals.RealTime >= SetupBones__lastWarning + 1.0f) {
				DevMsg($"*** ERROR: Bone access not allowed (entity {Index}:{GetClassname()})\n");
				SetupBones__lastWarning = gpGlobals.RealTime;
			}
		}

		if (MostRecentModelBoneCounter != ModelBoneCounter) {
			if (LastBoneChangedTime() >= LastBoneSetupTime) {
				BoneAccessor.SetReadableBones(0);
				BoneAccessor.SetWritableBones(0);
				LastBoneSetupTime = currentTime;
			}
			PrevBoneMask = AccumulatedBoneMask;
			AccumulatedBoneMask = 0;
		}

		int nBoneCount = CachedBoneData.Count;

		AccumulatedBoneMask |= boneMask;

		MostRecentModelBoneCounter = ModelBoneCounter;

		if ((BoneAccessor.GetReadableBones() & boneMask) != boneMask) {  // TRUE IS A HACK! Why is this not happening???
			StudioHdr? hdr = GetModelPtr();
			if (hdr == null || !hdr.SequencesAvailable())
				return false;

			Matrix3x4 parentTransform = default;
			MathLib.AngleMatrix(GetRenderAngles(), GetRenderOrigin(), out parentTransform);
			// MathLib.AngleMatrix(new(19.56f, -145.89f, 0), new(-767, 143.9f, -12650), ref parentTransform);

			boneMask |= PrevBoneMask;

			int oldReadableBones = BoneAccessor.GetReadableBones();
			BoneAccessor.SetWritableBones(BoneAccessor.GetReadableBones() | boneMask);
			BoneAccessor.SetReadableBones(BoneAccessor.GetWritableBones());

			if ((hdr.Flags() & StudioHdrFlags.StaticProp) != 0)
				GetBoneForWrite(0) = parentTransform;
			else {
				CdllExts.TrackBoneSetupEnt(this);

				AddFlag((EntityFlags)EFL.SettingUpBones);

				UpdateIKContext(hdr);

				Span<Vector3> pos = stackalloc Vector3[Studio.MAXSTUDIOBONES];
				Span<Quaternion> q = stackalloc Quaternion[Studio.MAXSTUDIOBONES];
				memset(pos.Cast<Vector3, float>(), float.NaN);
				memset(q.Cast<Quaternion, float>(), float.NaN);

				int bonesMaskNeedRecalc = boneMask | oldReadableBones;

				if (Ik != null) {
					if (Teleported() || IsNoInterpolationFrame())
						Ik.ClearTargets();

					InitIK(hdr, currentTime, bonesMaskNeedRecalc);
				}

				StandardBlendingRules(hdr, pos, q, currentTime, bonesMaskNeedRecalc);

				BoneBitList boneComputed = new();
				if (Ik != null && !IsRagdoll())
					SolveIK(pos, q, BoneAccessor.GetBoneArrayForWrite(), ref boneComputed, currentTime);

				BuildTransformations(hdr, pos, q, parentTransform, bonesMaskNeedRecalc, ref boneComputed);

				RemoveFlag((EntityFlags)EFL.SettingUpBones);
			}

			if ((oldReadableBones & Studio.BONE_USED_BY_ATTACHMENT) == 0 && (boneMask & Studio.BONE_USED_BY_ATTACHMENT) != 0) {
				if (!SetupBones_AttachmentHelper(hdr)) {
					DevWarning(2, "SetupBones: SetupBones_AttachmentHelper failed.\n");
					return false;
				}
			}
		}

		if (!boneToWorldOut.IsEmpty) {
			if (maxBones >= CachedBoneData.Count) {
				memcpy(boneToWorldOut, CachedBoneData.AsSpan());
			}
			else {
				Warning($"SetupBones: invalid bone array size ({maxBones} - needs {CachedBoneData.Count})\n");
				return false;
			}
		}

		return true;
	}

	private bool SetupBones_AttachmentHelper(StudioHdr hdr) {
		if (hdr == null)
			return false;

		// calculate attachment points
		Matrix3x4 world;
		for (int i = 0; i < hdr.GetNumAttachments(); i++) {
			MStudioAttachment attachment = hdr.Attachment(i);
			int iBone = hdr.GetAttachmentBone(i);
			if ((attachment.Flags & Studio.ATTACHMENT_FLAG_WORLD_ALIGN) == 0)
				MathLib.ConcatTransforms(GetBone(iBone), attachment.Local, out world);
			else {
				Vector3 vecLocalBonePos, vecWorldBonePos;
				MathLib.MatrixGetColumn(attachment.Local, 3, out vecLocalBonePos);
				MathLib.VectorTransform(in vecLocalBonePos, in GetBone(iBone), out vecWorldBonePos);

				MathLib.SetIdentityMatrix(out world);
				MathLib.MatrixSetColumn(in vecWorldBonePos, 3, ref world);
			}

			PutAttachment(i + 1, world);
		}

		return true;
	}
	public override BaseAnimating? GetBaseAnimating() => this;
	public bool PutAttachment(int number, in Matrix3x4 attachmentToWorld) {
		if (number < 1 || number > Attachments.Count)
			return false;

		AttachmentData pAtt = Attachments[number - 1];
		if (gpGlobals.FrameTime > 0 && pAtt.LastFramecount > 0 && pAtt.LastFramecount == gpGlobals.FrameCount - 1) {
			Vector3 vecPreviousOrigin, vecOrigin;
			MathLib.MatrixPosition(pAtt.AttachmentToWorld, out vecPreviousOrigin);
			MathLib.MatrixPosition(attachmentToWorld, out vecOrigin);
			pAtt.OriginVelocity = (vecOrigin - vecPreviousOrigin) / (float)gpGlobals.FrameTime;
		}
		else {
			pAtt.OriginVelocity.Init();
		}
		pAtt.LastFramecount = gpGlobals.FrameCount;
		pAtt.AnglesComputed = false;
		pAtt.AttachmentToWorld = attachmentToWorld;


		return true;
	}

	public TimeUnit_t GetCycle() => Cycle;
	public void SetSequence(int sequence) {
		if (Sequence != sequence) {
			Sequence = sequence;
			InvalidatePhysicsRecursive(InvalidatePhysicsBits.AnimationChanged);
			if (ClientSideAnimation)
				ClientSideAnimationChanged();
		}
	}
	public virtual ClientAnimFlags ComputeClientSideAnimationFlags() => ClientAnimFlags.SequenceCycle;
	private void ClientSideAnimationChanged() {
		if (!ClientSideAnimation || ClientSideAnimationListHandle == INVALID_CLIENTSIDEANIMATION_LIST_HANDLE)
			return;

		ref ClientAnimating anim = ref g_ClientSideAnimationList.AsSpan()[(int)ClientSideAnimationListHandle];
		Assert(anim.Animating == this);
		anim.Flags = ComputeClientSideAnimationFlags();

		SequenceTransitioner.CheckForSequenceChange(
			GetModelPtr(),
			GetSequence(),
			NewSequenceParity != PrevNewSequenceParity,
			!IsNoInterpolationFrame()
		);
	}

	public void SetCycle(TimeUnit_t cycle) {
		if (cycle != Cycle) {
			Cycle = cycle;
			InvalidatePhysicsRecursive(InvalidatePhysicsBits.AnimationChanged);

		}
	}
	public virtual void StandardBlendingRules(StudioHdr hdr, Span<Vector3> pos, Span<Quaternion> q, TimeUnit_t currentTime, int boneMask) {
		Span<float> poseparam = stackalloc float[Studio.MAXSTUDIOPOSEPARAM];

		if (!hdr.SequencesAvailable())
			return;

		if (GetSequence() >= hdr.GetNumSeq() || GetSequence() == -1)
			SetSequence(0);

		GetPoseParameters(hdr, poseparam);
		TimeUnit_t cycle = GetCycle();
		BoneSetup setup = new(hdr, boneMask, poseparam);
		setup.InitPose(pos, q);
		setup.AccumulatePose(pos, q, GetSequence(), cycle, 1.0f, currentTime, Ik);
		MaintainSequenceTransitions(ref setup, cycle, pos, q);
		AccumulateLayers(ref setup, pos, q, currentTime);

		setup.CalcAutoplaySequences(pos, q, currentTime, CreateAutoplayIK(hdr, currentTime, boneMask));

		if (hdr.NumBoneControllers() != 0) {
			// todo
			// Span<float> controllers = stackalloc float[Studio.MAXSTUDIOBONECTRLS];
			// GetBoneControllers(controllers);
			// setup.CalcBoneAdj(pos, q, controllers);
		}

		// todo
		// ChildLayerBlend(pos, q, currentTime, boneMask);
		// UnragdollBlend(hdr, pos, q, currentTime);
	}

	private void GetPoseParameters(StudioHdr? hdr, Span<float> poseparam) {
		if (hdr == null)
			return;

		int i;
		for (i = 0; i < Studio.MAXSTUDIOPOSEPARAM; i++)
			poseparam[i] = PoseParameter[i];

		/*if(GetSequenceName(GetSequence()) == "idle" && hdr.Name() == "weapons/c_physcannon.mdl") {
			Msg($"model   : {hdr.Name()}\n");
			Msg($"curtime : {gpGlobals.CurTime} :\n");
			for (i = 0; i < hdr.GetNumPoseParameters(); i++) {
				MStudioPoseParamDesc Pose = hdr.PoseParameter(i);
				Msg($"   poseparam idx#{i} {Pose.Name()} = {poseparam[i] * Pose.End + (1 - poseparam[i]) * Pose.Start}\n");
			}
			Msg("\n");
		}*/
	}

	public virtual void AccumulateLayers(ref BoneSetup setup, Span<Vector3> pos, Span<Quaternion> q, double currentTime) {

	}

	readonly SequenceTransitioner SequenceTransitioner = new();

	private void MaintainSequenceTransitions(ref BoneSetup boneSetup, double cycle, Span<Vector3> pos, Span<Quaternion> q) {
		if (boneSetup.GetStudioHdr() == null)
			return;

		if (prediction.InPrediction() || IsAboutToRagdoll()) {
			PrevNewSequenceParity = NewSequenceParity;
			return;
		}

		SequenceTransitioner.CheckForSequenceChange(
			boneSetup.GetStudioHdr(),
			GetSequence(),
			NewSequenceParity != PrevNewSequenceParity,
			!IsNoInterpolationFrame()
			);

		PrevNewSequenceParity = NewSequenceParity;

		// Update the transition sequence list.
		SequenceTransitioner.UpdateCurrent(
			boneSetup.GetStudioHdr(),
			GetSequence(),
			cycle,
			PlaybackRate,
			gpGlobals.CurTime
			);


		for (int i = SequenceTransitioner.AnimationQueue.Count - 2; i >= 0; i--) {
			ref AnimationLayer blend = ref SequenceTransitioner.AnimationQueue.AsSpan()[i];

			double dt = (gpGlobals.CurTime - blend.LayerAnimtime);
			cycle = blend.Cycle + dt * blend.PlaybackRate * GetSequenceCycleRate(boneSetup.GetStudioHdr(), blend.Sequence);
			cycle = ClampCycle(cycle, IsSequenceLooping(boneSetup.GetStudioHdr(), blend.Sequence));

			boneSetup.AccumulatePose(pos, q, blend.Sequence, cycle, blend.Weight, gpGlobals.CurTime, Ik);
		}
	}

	protected double ClampCycle(double cycle, bool isLooping) {
		if (isLooping) {
			cycle -= (int)cycle;
			if (cycle < 0.0) {
				cycle += 1.0;
			}
		}
		else {
			cycle = Math.Clamp(cycle, 0.0, 0.999);
		}
		return cycle;
	}

	public TimeUnit_t SequenceDuration() => SequenceDuration(GetSequence());
	public TimeUnit_t SequenceDuration(int sequence) => SequenceDuration(GetModelPtr(), sequence);
	public TimeUnit_t SequenceDuration(StudioHdr? studioHdr, int sequence) {
		if (studioHdr == null)
			return 0.1f;

		if (!studioHdr.SequencesAvailable())
			return 0.1;

		if (sequence >= studioHdr.GetNumSeq() || sequence < 0) {
			DevWarning(2, $"C_BaseAnimating::SequenceDuration({sequence}) out of range\n");
			return 0.1;
		}

		return BoneSetup.Studio_Duration(studioHdr, sequence, PoseParameter);
	}
	public TimeUnit_t GetSequenceCycleRate(StudioHdr? studioHdr, int sequence) {
		TimeUnit_t t = SequenceDuration(studioHdr, sequence);
		if (t != 0.0)
			return 1.0 / t;
		return t;
	}
	public string GetSequenceName(int sequence) {
		if (sequence == -1)
			return "Not Found!";

		if (GetModelPtr() == null)
			return "No model!";

		return Animation.GetSequenceName(GetModelPtr(), sequence);
	}

	public object? Ragdoll; // TODO

	public override void OnDataChanged(DataUpdateType updateType) {
		if (Ragdoll != null && GetSequence() != PrevSequence) {
			SetSequence(PrevSequence);
			PlaybackRate = 0;
		}

		if (Ragdoll == null && RestoreSequence != -1) {
			SetSequence(RestoreSequence);
			RestoreSequence = -1;
		}

		if (updateType == DataUpdateType.Created) {
			PrevSequence = -1;
			RestoreSequence = -1;
			SetNextClientThink(CLIENT_THINK_ALWAYS);
		}

		bool modelchanged = false;

		// UNDONE: The base class does this as well.  So this is kind of ugly
		// but getting a model by index is pretty cheap...
		Model? model = modelinfo.GetModel(GetModelIndex());

		if (model != GetModel())
			modelchanged = true;

		base.OnDataChanged(updateType);

		if ((updateType == DataUpdateType.Created) || modelchanged) {
			ResetLatched();
			// if you have this pose parameter, activate HL1-style lipsync/wave envelope tracking
		}

		// If there's a significant change, make sure the shadow updates
		if (modelchanged || (GetSequence() != PrevSequence)) {
			InvalidatePhysicsRecursive(InvalidatePhysicsBits.AnimationChanged);
			PrevSequence = GetSequence();
		}

		// Only need to think if animating client side
		if (ClientSideAnimation) {
			// Check to see if we should reset our frame
			if (ClientSideFrameReset != LastClientSideFrameReset)
				ResetClientsideFrame();

		}
		// build a ragdoll if necessary
		if (RenderFX == (byte)RenderFx.Ragdoll && !BuiltRagdoll)
			BecomeRagdollOnClient();

		//HACKHACK!!!
		if (RenderFX == (byte)RenderFx.Ragdoll && BuiltRagdoll == true)
			if (Ragdoll == null)
				AddEffects(EntityEffects.NoDraw);

		// todo: ragdoll stuff here
	}

	protected virtual void ResetClientsideFrame() {
		SetCycle(0);
	}

	public bool BuiltRagdoll;
	private void BecomeRagdollOnClient() {

	}

	public bool IsSequenceLooping(int sequence) => IsSequenceLooping(GetModelPtr(), sequence);
	public bool IsSequenceLooping(StudioHdr? studioHdr, int sequence) {
		return (Animation.GetSequenceFlags(studioHdr, sequence) & StudioAnimSeqFlags.Looping) != 0;
	}

	public static readonly RecvTable DT_ServerAnimationData = new(nameof(DT_ServerAnimationData), [
		RecvPropFloat(FIELD.OF(nameof(Cycle))),
	]);
	public static readonly new DataMap PredMap = new(typeof(C_BaseAnimating), C_BaseEntity.PredMap, [
		DEFINE.PRED_FIELD( nameof(Skin), FieldType.Integer, FieldTypeDescFlags.InSendTable ),
		DEFINE.PRED_FIELD( nameof(Body), FieldType.Integer, FieldTypeDescFlags.InSendTable ),
		DEFINE.FIELD( nameof(PrevSequence), FieldType.Integer ),
		DEFINE.PRED_FIELD( nameof(ResetEventsParity), FieldType.Integer, FieldTypeDescFlags.InSendTable | FieldTypeDescFlags.NoErrorCheck ),
		DEFINE.PRED_FIELD( nameof(MuzzleFlashParity), FieldType.Character, FieldTypeDescFlags.InSendTable ),
	]); public override DataMap? GetPredDescMap() => PredMap;
	public static readonly RecvTable DT_BaseAnimating = new(DT_BaseEntity, [
		RecvPropInt( FIELD.OF(nameof(ForceBone))),
		RecvPropVector( FIELD.OF(nameof(Force))),

		RecvPropInt( FIELD.OF(nameof(Skin))),
		RecvPropInt( FIELD.OF(nameof(Body))),

		RecvPropInt( FIELD.OF(nameof(HitboxSet))),

		RecvPropFloat( FIELD.OF(nameof(ModelScale))),

		RecvPropArray3( FIELD.OF_ARRAY(nameof(PoseParameter)), RecvPropFloat(null!)),

		RecvPropInt( FIELD.OF(nameof(Sequence)), 0, RecvProxy_Sequence),
		RecvPropFloat( FIELD.OF(nameof(PlaybackRate))),

		RecvPropArray3(FIELD.OF_ARRAY(nameof(EncodedController)), RecvPropFloat(null!) ),

		RecvPropInt( FIELD.OF(nameof( ClientSideAnimation ))),
		RecvPropInt( FIELD.OF(nameof( ClientSideFrameReset ))),

		RecvPropInt( FIELD.OF(nameof( NewSequenceParity) )),
		RecvPropInt( FIELD.OF(nameof( ResetEventsParity ))),
		RecvPropInt( FIELD.OF(nameof( MuzzleFlashParity ))),

		RecvPropEHandle( FIELD.OF(nameof( LightingOrigin )) ),
		RecvPropEHandle( FIELD.OF(nameof( LightingOriginRelative )) ),

		RecvPropDataTable( "serveranimdata", DT_ServerAnimationData ),

		RecvPropFloat( FIELD.OF(nameof(FadeMinDist) )),
		RecvPropFloat( FIELD.OF(nameof(FadeMaxDist ))),
		RecvPropFloat( FIELD.OF(nameof(FadeScale ))),

		// Gmod specific
		RecvPropEHandle(FIELD.OF(nameof(BoneManipulator))),
		RecvPropEHandle(FIELD.OF(nameof(FlexManipulator))),
		RecvPropVector(FIELD.OF(nameof(OverrideViewTarget))),
	]);

	private static void RecvProxy_Sequence(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		RecvProxy_Int32ToInt32(in data, instance, field);
		C_BaseAnimating? animating = (C_BaseAnimating)instance;
		if (animating == null)
			return;
		animating.SetReceivedSequence();
		animating.UpdateVisibility();
	}
	public virtual void DoMuzzleFlash() => MuzzleFlashParity = unchecked((byte)((MuzzleFlashParity + 1) & ((1 << (int)EntityEffects.MuzzleflashBits) - 1)));


	public static readonly new ClientClass ClientClass = new ClientClass(DT_BaseAnimating);
	protected override StudioHdr? OnNewModel() {
		InvalidateMdlCache();
		int i;

		// remove transition animations playback
		// SequenceTransitioner.RemoveAll();

		// TODO: Jiggle bones
		// TODO: Dynamic model pending
		// TODO: AutoRefModelIndex

		if (GetModel() == null || modelinfo.GetModelType(GetModel()) != ModelType.Studio)
			return null;

		// TODO: Dynamic model loading

		StudioHdr? hdr = GetModelPtr();
		if (hdr == null)
			return null;

		InvalidateBoneCache();
		BoneMergeCache = null;

		Studio.DestroyBoneCache(HitboxBoneCacheHandle);
		HitboxBoneCacheHandle = 0;

		if (CachedBoneData.Count != hdr.NumBones()) {
			CachedBoneData.SetSize(hdr.NumBones());
			for (i = 0; i < hdr.NumBones(); i++)
				MathLib.SetIdentityMatrix(out CachedBoneData.AsSpan()[i]);
		}
		BoneAccessor.Init(CachedBoneData.Base());

		Ik = null;

		if (Attachments.Count != hdr.GetNumAttachments())
			Attachments.SetSizeInitialized(hdr.GetNumAttachments());

		for (int j = 0; j < Attachments.Count; j++)
			Attachments[j].Reset();

		Assert(hdr.GetNumPoseParameters() <= PoseParameter.Length);
		iv_flPoseParameter.SetMaxCount(hdr.GetNumPoseParameters());

		for (i = 0; i < hdr.GetNumPoseParameters(); i++) {
			MStudioPoseParamDesc Pose = hdr.PoseParameter(i);
			iv_flPoseParameter.SetLooping(Pose.Loop != 0.0f, i);
		}

		EyeAttachment = LookupAttachment("eyes");

		if (ShouldInterpolate())
			AddToInterpolationList();

		int forceSequence = ShouldResetSequenceOnNewModel() ? 0 : Sequence;
		if (GetSequence() >= hdr.GetNumSeq())
			forceSequence = 0;

		Sequence = -1;
		SetSequence(forceSequence);
		if (ResetSequenceInfoOnLoad) {
			ResetSequenceInfoOnLoad = false;
			ResetSequenceInfo();
		}
		UpdateRelevantInterpolatedVars();

		// todo: the rest of this

		return hdr;
	}
	public int PrevSequence = -1;
	public int RestoreSequence = -1;
	bool ResetSequenceInfoOnLoad = false;

	public bool IsDynamicModelLoading() => DynamicModelPending;

	public void ResetSequenceInfo() {
		if (GetSequence() == -1)
			// This shouldn't happen.  Setting m_nSequence blindly is a horrible coding practice.
			SetSequence(0);

		if (IsDynamicModelLoading()) {
			ResetSequenceInfoOnLoad = true;
			return;
		}

		StudioHdr? studioHdr = GetModelPtr();
		GroundSpeed = (float)GetSequenceGroundSpeed(studioHdr, GetSequence()) * GetModelScale();
		SequenceLoops = ((Animation.GetSequenceFlags(studioHdr, GetSequence()) & StudioAnimSeqFlags.Looping) != 0);
		// m_flAnimTime = gpGlobals->time;
		PlaybackRate = 1.0f;
		SequenceFinished = false;
		LastEventCheck = 0;

		NewSequenceParity = (NewSequenceParity + 1) & (int)EntityEffects.ParityMask;
		ResetEventsParity = (ResetEventsParity + 1) & (int)EntityEffects.ParityMask;

		// FIXME: why is this called here?  Nothing should have changed to make this nessesary
		if (studioHdr != null)
			Animation.SetEventIndexForSequence(studioHdr.Seqdesc(GetSequence()));
	}

	public int SelectWeightedSequence(Activity activity) {
		return Animation.SelectWeightedSequence(GetModelPtr(), activity);
	}
	public int FindTransitionSequence(int currentSequence, int goalSequence) {
		StudioHdr? hdr = GetModelPtr();
		if (hdr == null) {
			return -1;
		}

		int dir = 1;
		int sequence = Animation.FindTransitionSequence(hdr, currentSequence, goalSequence, ref dir);
		if (dir != 1)
			return -1;
		else
			return sequence;
	}
	public int FindTransitionSequence(int currentSequence, int goalSequence, ref int dir) {
		StudioHdr? hdr = GetModelPtr();
		if (hdr == null) {
			dir = 0;
			return -1;
		}

		return Animation.FindTransitionSequence(hdr, currentSequence, goalSequence, ref dir);
	}

	public bool ReceivedSequence;
	public void SetReceivedSequence() => ReceivedSequence = true;
	public bool ShouldResetSequenceOnNewModel() => ReceivedSequence == false;
	private void UpdateRelevantInterpolatedVars() {
		if (!IsMarkedForDeletion() && !GetPredictable() && !IsClientCreated() && GetModelPtr() != null && GetModelPtr()!.SequencesAvailable())
			AddBaseAnimatingInterpolatedVars();
		else
			RemoveBaseAnimatingInterpolatedVars();
	}

	public void ResetSequence(int sequence) {
		SetSequence(sequence);
		ResetSequenceInfo();
	}

	public override bool Interpolate(TimeUnit_t currentTime) {
		Vector3 oldOrigin = default;
		QAngle oldAngles = default;
		Vector3 oldVel = default;
		TimeUnit_t flOldCycle = GetCycle();
		InvalidatePhysicsBits nChangeFlags = 0;

		if (!ClientSideAnimation)
			iv_Cycle.SetLooping(IsSequenceLooping(GetSequence()));

		int noMoreChanges = 0;
		InterpolateResult retVal = BaseInterpolatePart1(ref currentTime, ref oldOrigin, ref oldAngles, ref oldVel, ref noMoreChanges);
		if (retVal == InterpolateResult.Stop) {
			if (noMoreChanges != 0)
				RemoveFromInterpolationList();
			return true;
		}

		// Did cycle change?
		if (GetCycle() != flOldCycle)
			nChangeFlags |= InvalidatePhysicsBits.AnimationChanged;

		if (noMoreChanges != 0)
			RemoveFromInterpolationList();

		BaseInterpolatePart2(oldOrigin, oldAngles, oldVel, nChangeFlags);
		return true;
	}

	public static int g_PreviousBoneCounter = 0;

	public C_BaseAnimating() {
		iv_Cycle = new($"{nameof(C_BaseAnimating)}.{nameof(iv_Cycle)}");
		iv_flPoseParameter = new(Studio.MAXSTUDIOPOSEPARAM, $"{nameof(C_BaseAnimating)}.{nameof(iv_flPoseParameter)}");

		Force.Init();
		ForceBone = -1;

		PrevSequence = -1;
		RestoreSequence = -1;

		AddBaseAnimatingInterpolatedVars();

		MostRecentModelBoneCounter = 0xFFFFFFFF;
		MostRecentBoneSetupRequest = g_PreviousBoneCounter - 1;
		LastBoneSetupTime = -float.MaxValue;

		PlaybackRate = 1.0f;

		EventSequence = -1;

		// Assume false. Derived classes might fill in a receive table entry
		// and in that case this would show up as true
		ClientSideAnimation = false;

		PrevNewSequenceParity = -1;
		PrevResetEventsParity = -1;

		OldMuzzleFlashParity = 0;
		MuzzleFlashParity = 0;

		ModelScale = 1.0f;

		pStudioHdr = null;
		hStudioHdr = MDLHANDLE_INVALID;

		ReceivedSequence = false;

		BoneIndexAttached = -1;
		OldModelScale = 0.0f;

		AttachedTo.Set(null);

		DynamicModelAllowed = false;
		DynamicModelPending = false;
		ResetSequenceInfoOnLoad = false;

		InitModelEffects = false;
		DelayInitModelEffects = false;

		Cycle = 0;
		OldCycle = 0;
	}

	bool DelayInitModelEffects;
	bool InitModelEffects;
	bool DynamicModelAllowed;
	bool DynamicModelPending;

	public void OnModelLoadComplete(Model model) {
		if (DynamicModelPending && model == GetModel()) {
			DynamicModelPending = false;
			OnNewModel();
			UpdateVisibility();
		}
	}
	TimeUnit_t OldCycle;
	int OldSequence;
	float OldModelScale;

	public override void PreDataUpdate(DataUpdateType updateType) {
		OldCycle = GetCycle();
		OldSequence = GetSequence();
		OldModelScale = GetModelScale();

		int i;
		for (i = 0; i < Studio.MAXSTUDIOBONECTRLS; i++)
			OldEncodedController[i] = EncodedController[i];

		for (i = 0; i < Studio.MAXSTUDIOPOSEPARAM; i++)
			OldPoseParameters[i] = PoseParameter[i];

		base.PreDataUpdate(updateType);
	}

	public ClientSideAnimationListHandle_t ClientSideAnimationListHandle = INVALID_CLIENTSIDEANIMATION_LIST_HANDLE;

	public void AddToClientSideAnimationList() {
		if (ClientSideAnimationListHandle != INVALID_CLIENTSIDEANIMATION_LIST_HANDLE)
			return;

		ClientAnimating list = new(this, 0);
		ClientSideAnimationListHandle = (ClientSideAnimationListHandle_t)g_ClientSideAnimationList.Count; g_ClientSideAnimationList.Add(list);
		ClientSideAnimationChanged();

		UpdateRelevantInterpolatedVars();
	}

	public override void Term() {
		RemoveFromClientSideAnimationList(true);
		base.Term();
	}

	public void RemoveFromClientSideAnimationList(bool beingDestroyed = false) {
		if (INVALID_CLIENTSIDEANIMATION_LIST_HANDLE == ClientSideAnimationListHandle)
			return;

		uint c = (uint)g_ClientSideAnimationList.Count;

		Assert(ClientSideAnimationListHandle < c);

		uint last = c - 1;

		if (last == ClientSideAnimationListHandle)
			g_ClientSideAnimationList.RemoveAt((int)last);
		else {
			ClientAnimating lastEntry = g_ClientSideAnimationList[(int)last];
			// Remove the last entry
			g_ClientSideAnimationList.RemoveAt((int)last);

			// And update it's handle to point to this slot.
			lastEntry.Animating!.ClientSideAnimationListHandle = (ClientSideAnimationListHandle_t)ClientSideAnimationListHandle;
			g_ClientSideAnimationList[(int)ClientSideAnimationListHandle] = lastEntry;
		}

		// Invalidate our handle no matter what.
		ClientSideAnimationListHandle = INVALID_CLIENTSIDEANIMATION_LIST_HANDLE;

		if (!beingDestroyed)
			UpdateRelevantInterpolatedVars();
	}

	public override void PostDataUpdate(DataUpdateType updateType) {
		base.PostDataUpdate(updateType);

		if (ClientSideAnimation) {
			SetCycle(OldCycle);
			AddToClientSideAnimationList();
		}
		else
			RemoveFromClientSideAnimationList();

		bool bBoneControllersChanged = false;

		int i;
		for (i = 0; i < Studio.MAXSTUDIOBONECTRLS && !bBoneControllersChanged; i++)
			if (OldEncodedController[i] != EncodedController[i])
				bBoneControllersChanged = true;

		bool bPoseParametersChanged = false;

		for (i = 0; i < Studio.MAXSTUDIOPOSEPARAM && !bPoseParametersChanged; i++)
			if (OldPoseParameters[i] != PoseParameter[i])
				bPoseParametersChanged = true;

		// Cycle change? Then re-render
		bool bAnimationChanged = OldCycle != GetCycle() || bBoneControllersChanged || bPoseParametersChanged;
		bool bSequenceChanged = OldSequence != GetSequence();
		bool bScaleChanged = (OldModelScale != GetModelScale());
		if (bAnimationChanged || bSequenceChanged || bScaleChanged)
			InvalidatePhysicsRecursive(InvalidatePhysicsBits.AnimationChanged);

		if (bAnimationChanged || bSequenceChanged) {
			if (ClientSideAnimation) {
				ClientSideAnimationChanged();
			}
		}

		// reset prev cycle if new sequence
		if (NewSequenceParity != PrevNewSequenceParity) {
			// It's important not to call Reset() on a static prop, because if we call
			// Reset(), then the entity will stay in the interpolated entities list
			// forever, wasting CPU.
			StudioHdr? hdr = GetModelPtr();
			if (hdr != null && (hdr.Flags() & StudioHdrFlags.StaticProp) == 0) {
				iv_Cycle.Reset();
			}
		}
	}

	[Flags]
	public enum ClientAnimFlags : uint
	{
		SequenceCycle = 1
	}

	public struct ClientAnimating
	{
		public C_BaseAnimating? Animating;
		public ClientAnimFlags Flags;
		public ClientAnimating(C_BaseAnimating anim, ClientAnimFlags flags) {
			Animating = anim;
			Flags = flags;
		}
	}

	public static readonly List<ClientAnimating> g_ClientSideAnimationList = [];

	internal static void UpdateClientSideAnimations() {
		int c = g_ClientSideAnimationList.Count;
		for (int i = 0; i < c; ++i) {
			ref ClientAnimating anim = ref g_ClientSideAnimationList.AsSpan()[i];
			if (0 == (anim.Flags & ClientAnimFlags.SequenceCycle))
				continue;
			Assert(anim.Animating != null);
			anim.Animating.UpdateClientSideAnimation();
		}
	}

	public double FrameAdvance(double interval) {
		StudioHdr? hdr = GetModelPtr();
		if (hdr == null)
			return 0;

		TimeUnit_t curtime = gpGlobals.CurTime;

		double flInterval = interval;
		if (flInterval == 0.0) {
			flInterval = curtime - AnimTime;
			if (flInterval <= 0.001)
				return 0;
		}

		if (AnimTime == 0)
			flInterval = 0.0;

		TimeUnit_t cyclerate = GetSequenceCycleRate(hdr, GetSequence());
		TimeUnit_t addcycle = flInterval * cyclerate * PlaybackRate;

		// todo
		// if (GetServerIntendedCycle() != -1.0f) {

		// }

		double flNewCycle = GetCycle() + addcycle;
		AnimTime = curtime;

		if (flNewCycle < 0.0 || flNewCycle >= 1.0) {
			if (IsSequenceLooping(hdr, GetSequence()))
				flNewCycle -= (int)flNewCycle;
			else
				flNewCycle = (flNewCycle < 0.0) ? 0.0 : 1.0;

			SequenceFinished = true;
		}

		SetCycle(flNewCycle);

		return flInterval;
	}

	public virtual void UpdateClientSideAnimation() => UpdateBaseClientSideAnimation();

	protected void UpdateBaseClientSideAnimation() {
		if (ClientSideAnimation) {
			Assert(ClientSideAnimationListHandle != INVALID_CLIENTSIDEANIMATION_LIST_HANDLE);
			if (GetSequence() != -1) {
				// latch old values
				OnLatchInterpolatedVariables(LatchFlags.LatchAnimationVar);
				// move frame forward
				FrameAdvance(0.0); // 0 means to use the time we last advanced instead of a constant
			}
		}
		else {
			Assert(ClientSideAnimationListHandle == INVALID_CLIENTSIDEANIMATION_LIST_HANDLE);
		}
	}

	StudioHdr? pStudioHdr;
	MDLHandle_t hStudioHdr;

	public void LockStudioHdr() {
		Assert(hStudioHdr == MDLHANDLE_INVALID && pStudioHdr == null);

		if (hStudioHdr != MDLHANDLE_INVALID || pStudioHdr != null) {
			Assert(pStudioHdr != null ? pStudioHdr.GetRenderHdr() == mdlcache.GetStudioHdr(hStudioHdr) : hStudioHdr == MDLHANDLE_INVALID);
			return;
		}

		Model? mdl = GetModel();
		if (mdl == null)
			return;

		hStudioHdr = modelinfo.GetCacheHandle(mdl);
		if (hStudioHdr == MDLHANDLE_INVALID)
			return;

		StudioHeader? studioHdr = mdlcache.LockStudioHdr(hStudioHdr);
		if (studioHdr == null) {
			hStudioHdr = MDLHANDLE_INVALID;
			return;
		}

		StudioHdr newWrapper = new StudioHdr();
		newWrapper.Init(studioHdr, mdlcache);
		Assert(newWrapper.IsValid());

		if (newWrapper.GetVirtualModel() != null) {
			MDLHandle_t hVirtualModel = studioHdr.VirtualModel;
			mdlcache.LockStudioHdr(hVirtualModel);
		}

		pStudioHdr = newWrapper; // must be last to ensure virtual model correctly set up
	}

	public void UnlockStudioHdr() {
		if (hStudioHdr != MDLHANDLE_INVALID) {
			StudioHeader? studioHdr = mdlcache.GetStudioHdr(hStudioHdr);
			Assert(studioHdr != null && pStudioHdr!.GetRenderHdr() == studioHdr);

			{
				// Immediate-mode rendering, can unlock immediately
				if (studioHdr.GetVirtualModel() != null) {
					MDLHandle_t hVirtualModel = studioHdr.VirtualModel;
					mdlcache.UnlockStudioHdr(hVirtualModel);
				}
				mdlcache.UnlockStudioHdr(hStudioHdr);
			}
			hStudioHdr = MDLHANDLE_INVALID;

			pStudioHdr = null;
		}
	}

	public StudioHdr? GetModelPtr() {
		if (pStudioHdr == null)
			LockStudioHdr();
		return pStudioHdr;
	}

	public void InvalidateMdlCache() {
		UnlockStudioHdr();
	}


	public bool IsModelScaleFractional() => ModelScale < 1.0f;
	public bool IsModelScaled() => ModelScale > 1.0f + FLT_EPSILON || ModelScale < 1.0f - FLT_EPSILON;
	public float GetModelScale() => ModelScale;

	public int GetBody() => Body;
	public int GetSkin() => Skin;

	public int InternalDrawModel(StudioFlags flags) {
		var model = GetModel();
		if (model == null)
			return 0;

		// This should never happen, but if the server class hierarchy has bmodel entities derived from CBaseAnimating or does a
		//  SetModel with the wrong type of model, this could occur.
		if (modelinfo.GetModelType(model) != ModelType.Studio) {
			return base.DrawModel(flags);
		}

		// Make sure hdr is valid for drawing
		if (GetModelPtr() == null)
			return 0;

		UpdateBoneAttachments();

		if (IsEffectActive(EntityEffects.ItemBlink))
			flags |= StudioFlags.ItemBlink;

		ClientModelRenderInfo info = default;

		info.Flags = flags;
		info.Renderable = this;
		info.Instance = GetModelInstance();
		info.EntityIndex = Index;
		info.Model = GetModel();
		info.Origin = GetRenderOrigin();
		info.Angles = GetRenderAngles();
		info.Skin = GetSkin();
		info.Body = GetBody();
		info.HitboxSet = HitboxSet;

		if (!OnInternalDrawModel(ref info))
			return 0;

		// Turns the origin + angles into a matrix
		MathLib.AngleMatrix(info.Angles, info.Origin, out info.ModelToWorld);

		DrawModelState state = default;
		bool markAsDrawn = modelrender.DrawModelSetup(ref info, ref state, default, out Span<Matrix3x4> boneToWorld);

		// Scale the base transform if we don't have a bone hierarchy
		if (IsModelScaled()) {
			StudioHdr? pHdr = GetModelPtr();
			if (pHdr != null && !boneToWorld.IsEmpty && pHdr.NumBones() == 1) {
				// Scale the bone to world at this point
				float flScale = GetModelScale();
				ref Matrix3x4 boneToWorld0 = ref boneToWorld[0];
				MathLib.VectorScale(boneToWorld0[0], flScale, boneToWorld0[0]);
				MathLib.VectorScale(boneToWorld0[1], flScale, boneToWorld0[1]);
				MathLib.VectorScale(boneToWorld0[2], flScale, boneToWorld0[2]);
			}
		}

		if (markAsDrawn && (info.Flags & StudioFlags.Render) != 0)
			DoInternalDrawModel(ref info, ref state, boneToWorld);
		else
			DoInternalDrawModel(ref info, ref Unsafe.NullRef<DrawModelState>(), boneToWorld);

		OnPostInternalDrawModel(ref info);

		return markAsDrawn ? 1 : 0;
	}

	public static readonly ConVar vcollide_wireframe = new("vcollide_wireframe", "0", FCvar.Cheat, "Render physics collision models in wireframe", callback: VCollideWireframe_ChangeCallback);

	private static void VCollideWireframe_ChangeCallback(IConVar var, in ConVarChangeContext ctx) {
		for (C_BaseEntity? entity = cl_entitylist.FirstBaseEntity(); entity != null; entity = cl_entitylist.NextBaseEntity(entity))
			entity.UpdateVisibility();
	}

	static readonly Color debugColor = new(0, 255, 255, 0);
	static readonly Color debugColorPhys = new(255, 0, 0, 0);
	private void DoInternalDrawModel(ref ClientModelRenderInfo info, ref DrawModelState state, Span<Matrix3x4> boneToWorldArray) {
		if (!Unsafe.IsNullRef(ref state))
			modelrender.DrawModelExecute(ref state, ref info, boneToWorldArray);

		if (vcollide_wireframe.GetBool()) {
			//if (IsRagdoll()) 
			// m_pRagdoll->DrawWireframe();
			// else
			if (IsSolid() && CollisionProp().GetSolid() == SolidType.VPhysics) {
				VCollide? collide = modelinfo.GetVCollide(GetModelIndex());
				if (collide != null && collide.SolidCount == 1) {
					MathLib.AngleMatrix(GetAbsAngles(), GetAbsOrigin(), out Matrix3x4 matrix);
					engine.DebugDrawPhysCollide(collide.Solids![0]!, null, matrix, debugColor);
					if (VPhysicsGetObject() != null) {
						VPhysicsGetObject()!.GetPositionMatrix(out matrix);
						engine.DebugDrawPhysCollide(collide.Solids![0]!, null, matrix, debugColorPhys);
					}
				}
			}
		}
	}

	protected virtual bool OnPostInternalDrawModel(ref ClientModelRenderInfo info) {
		return true;
	}

	protected virtual bool OnInternalDrawModel(ref ClientModelRenderInfo info) {
		var lor = LightingOriginRelative.Get();
		var lo = LightingOrigin.Get();
		if (lor != null) {
			// todo
		}

		if (lo != null) {
			info.LightingOrigin = lo.GetAbsOrigin();
		}

		return true;
	}

	private void UpdateBoneAttachments() {

	}

	public virtual void RemoveFromPreviousBoneSetups() => PreviousBoneSetups.Remove(this);

	public int LookupBone(ReadOnlySpan<char> name) {
		Assert(GetModelPtr());
		return BoneSetup.Studio_BoneIndexByName(GetModelPtr(), name);
	}

	public int LookupAttachment(ReadOnlySpan<char> attachmentName) {
		StudioHdr? hdr = GetModelPtr();
		if (hdr == null)
			return -1;
		return BoneSetup.Studio_FindAttachment(hdr, attachmentName);
	}

	public C_BaseAnimating? FindFollowedEntity() {
		C_BaseEntity? follow = GetFollowedEntity();

		if (follow == null)
			return null;

		if (follow.IsDormant())
			return null;

		if (follow.GetModel() == null) {
			Warning("ModelType.Studio: MoveType.Follow with no model.\n");
			return null;
		}

		if (modelinfo.GetModelType(follow.GetModel()) != ModelType.Studio) {
			Warning($"Attached {modelinfo.GetModelName(GetModel())} (ModelType.Studio) to {modelinfo.GetModelName(follow.GetModel())} ({modelinfo.GetModelType(follow.GetModel())})\n");
			return null;
		}

		return (C_BaseAnimating?)follow;
	}

	public Activity LookupActivity(ReadOnlySpan<char> label) {
		return Animation.LookupActivity(GetModelPtr(), label);
	}

	public int LookupSequence(ReadOnlySpan<char> label) {
		return Animation.LookupSequence(GetModelPtr(), label);
	}

	public Activity GetSequenceActivity(int sequence) {
		if (sequence == -1) {
			return Activity.ACT_INVALID;
		}

		if (null == GetModelPtr())
			return Activity.ACT_INVALID;

		return (Activity)Animation.GetSequenceActivity(GetModelPtr()!, sequence, out _);
	}
	public bool GetPoseParameterRange(ReadOnlySpan<char> name, out float minValue, out float maxValue) => GetPoseParameterRange(LookupPoseParameter(name), out minValue, out maxValue);
	public bool GetPoseParameterRange(int parameter, out float minValue, out float maxValue) {
		StudioHdr? pStudioHdr = GetModelPtr();

		if (pStudioHdr != null) {
			if (parameter >= 0 && parameter < pStudioHdr.GetNumPoseParameters()) {
				MStudioPoseParamDesc pose = pStudioHdr.PoseParameter(parameter);
				minValue = pose.Start;
				maxValue = pose.End;
				return true;
			}
		}
		minValue = 0.0f;
		maxValue = 1.0f;
		return false;
	}

	public float GetPoseParameter(ReadOnlySpan<char> name) => GetPoseParameter(LookupPoseParameter(name));
	public float GetPoseParameter(int parameter) {
		StudioHdr? pStudioHdr = GetModelPtr();

		if (pStudioHdr == null)
			return 0.0f;

		if (pStudioHdr.GetNumPoseParameters() < parameter)
			return 0.0f;

		if (parameter < 0)
			return 0.0f;

		return PoseParameter[parameter];
	}

	public float SetPoseParameter(ReadOnlySpan<char> name, float value) => SetPoseParameter(GetModelPtr(), name, value);
	public float SetPoseParameter(int parameter, float value) => SetPoseParameter(GetModelPtr(), parameter, value);
	public float SetPoseParameter(StudioHdr? studioHdr, ReadOnlySpan<char> name, float value) => SetPoseParameter(studioHdr, LookupPoseParameter(studioHdr, name), value);

	public int LookupPoseParameter(ReadOnlySpan<char> name) => LookupPoseParameter(GetModelPtr(), name);
	public int LookupPoseParameter(StudioHdr? studioHdr, ReadOnlySpan<char> name) {
		if (studioHdr == null)
			return 0;

		for (int i = 0; i < studioHdr.GetNumPoseParameters(); i++) {
			if (name.Equals(studioHdr.PoseParameter(i).Name(), StringComparison.OrdinalIgnoreCase))
				return i;
		}

		return -1;
	}

	public float SetPoseParameter(StudioHdr? studioHdr, int parameter, float value) {
		if (studioHdr == null) {
			AssertMsg(false, "C_BaseAnimating.SetPoseParameter: model missing");
			return value;
		}

		if (parameter >= 0) {
			value = BoneSetup.Studio_SetPoseParameter(studioHdr, parameter, value, out float newValue);
			PoseParameter[parameter] = newValue;
		}

		return value;
	}

	class AttachmentData
	{
		public Matrix3x4 AttachmentToWorld;
		public QAngle Rotation;
		public Vector3 OriginVelocity;
		public long LastFramecount;
		public bool AnglesComputed;

		public void Reset() {
			AttachmentToWorld = default;
			Rotation = default;
			AttachmentToWorld = default;
			LastFramecount = default;
			AnglesComputed = default;
		}
	}

	readonly List<AttachmentData> Attachments = [];
	protected int EyeAttachment;

	public bool CalcAttachments() {
		return SetupBones(null, -1, Studio.BONE_USED_BY_ATTACHMENT, gpGlobals.CurTime);
	}

	public override bool GetAttachment(int number, out Matrix3x4 matrix) {
		if (number < 1 || number > Attachments.Count) {
			matrix = default;
			return false;
		}

		if (!CalcAttachments()) {
			matrix = default;
			return false;
		}

		matrix = Attachments[number - 1].AttachmentToWorld;
		return true;
	}

	public LocalFlexController GetNumFlexControllers() {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null)
			return 0;

		return studioHdr.NumFlexControllers();
	}

	public string? GetFlexControllerName(LocalFlexController flexController) {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null)
			return null;

		MStudioFlexController flexcontroller = studioHdr.FlexController(flexController);

		return flexcontroller.Name();
	}

	public override bool GetAttachment(int number, out Vector3 origin, out QAngle angles) {
		if (number < 1 || number > Attachments.Count || !CalcAttachments()) {
			// Set this to the model origin/angles so that we don't have stack fungus in origin and angles.
			origin = GetAbsOrigin();
			angles = GetAbsAngles();
			return false;
		}

		AttachmentData pData = Attachments[number - 1];
		if (!pData.AnglesComputed) {
			MathLib.MatrixAngles(pData.AttachmentToWorld, out pData.Rotation);
			pData.AnglesComputed = true;
		}
		angles = pData.Rotation;
		MathLib.MatrixPosition(in pData.AttachmentToWorld, out origin);
		return true;
	}

	public override int DrawModel(StudioFlags flags) {
		if (!ReadyToDraw)
			return 0;

		int drawn = 0;

		if (r_drawothermodels.GetInt() != 0) {
			StudioFlags extraFlags = 0;
			if (r_drawothermodels.GetInt() == 2)
				extraFlags |= StudioFlags.Wireframe;

			if ((flags & StudioFlags.ShadowDepthTexture) != 0)
				extraFlags |= StudioFlags.ShadowDepthTexture;

			if ((flags & StudioFlags.SSAODepthTexture) != 0)
				extraFlags |= StudioFlags.SSAODepthTexture;

			// todo: g_pStudioStatsEntity
			if ((flags & (StudioFlags.SSAODepthTexture | StudioFlags.ShadowDepthTexture)) == 0 && false)
				extraFlags |= StudioFlags.GenerateStats;

			if ((flags & StudioFlags.NoOverrideForAttach) != 0)
				extraFlags |= StudioFlags.NoOverrideForAttach;

			// Necessary for lighting blending
			CreateModelInstance();

			if (!IsFollowingEntity()) {
				drawn = InternalDrawModel(flags | extraFlags);
			}
			else {
				// this doesn't draw unless master entity is visible and it's a studio model!!!
				C_BaseAnimating? follow = FindFollowedEntity();
				if (follow != null) {
					// recompute master entity bone structure
					int baseDrawn = follow.DrawModel(0);

					// draw entity
					// FIXME: Currently only draws if aiment is drawn.  
					// BUGBUG: Fixup bbox and do a separate cull for follow object
					if (baseDrawn != 0)
						drawn = InternalDrawModel(StudioFlags.Render | extraFlags);
				}
			}
		}

		DrawBBoxVisualizations();

		return drawn;
	}

	void DisableMuzzleFlash() {

	}

	int PrevNewSequenceParity;
	int PrevResetEventsParity;
#if GMOD_DLL
	public virtual void ClearPrevResetEventsParity() => PrevResetEventsParity = -1;
#endif

	public int GetSequence() => Sequence;
	public override void UpdateVisibility() {
		base.UpdateVisibility();

		// todo
	}
	public override void ValidateModelIndex() {
		base.ValidateModelIndex();
		Assert(ModelIndex == 0);
	}
	public virtual bool IsViewModel() => false;
	public override void NotifyShouldTransmit(ShouldTransmiteState state) {
		base.NotifyShouldTransmit(state);

		if (state == ShouldTransmiteState.Start) {
			DisableMuzzleFlash();

			PrevResetEventsParity = ResetEventsParity;
			EventSequence = GetSequence();
		}
	}
	public override void GetAimEntOrigin(C_BaseEntity attachedTo, out Vector3 origin, out QAngle angles) {
		C_BaseEntity? moveParent = null;
		if (IsEffectActive(EntityEffects.BoneMerge) && IsEffectActive(EntityEffects.BoneMergeFastCull) && (moveParent = GetMoveParent()) != null) {
			origin = moveParent.GetAbsOrigin(); //TODO:  moveParent.WorldSpaceCenter();
			angles = moveParent.GetRenderAngles();
		}
		else {
			origin = default;
			angles = default;
			if (BoneMergeCache == null || !BoneMergeCache.GetAimEntOrigin(ref origin, ref angles))
				base.GetAimEntOrigin(attachedTo, out origin, out angles);
		}
	}

	[NetworkName("m_nSequence")]
	public int Sequence;
	[NetworkName("m_nForceBone")]
	public int ForceBone;
	[NetworkName("m_vecForce")]
	public Vector3 Force;
	[NetworkName("m_nSkin")]
	public int Skin;
	[NetworkName("m_nBody")]
	public int Body;
	[NetworkName("m_nHitboxSet")]
	public int HitboxSet;

	public int GetHitboxSet() => HitboxSet;
	[NetworkName("m_flModelScale")]
	public float ModelScale = 1.0f;
	[NetworkName("m_flPlaybackRate")]
	public float PlaybackRate;
	[NetworkName("m_bClientSideAnimation")]
	public bool ClientSideAnimation;
	public bool LastClientSideFrameReset;
	[NetworkName("m_bClientSideFrameReset")]
	public bool ClientSideFrameReset;
	[NetworkName("m_nNewSequenceParity")]
	public int NewSequenceParity;
	[NetworkName("m_nResetEventsParity")]
	public int ResetEventsParity;
	public byte OldMuzzleFlashParity;
	[NetworkName("m_nMuzzleFlashParity")]
	public byte MuzzleFlashParity;
	[NetworkName("m_hLightingOrigin")]
	public EHANDLE LightingOrigin = new();
	[NetworkName("m_hLightingOriginRelative")]
	public EHANDLE LightingOriginRelative = new();
	[NetworkName("m_pBoneManipulator")]
	public EHANDLE BoneManipulator = new();
	[NetworkName("m_pFlexManipulator")]
	public EHANDLE FlexManipulator = new();
	public EHANDLE ClientFlexManipulator = new();

	public virtual C_BaseEntity? GetFlexManipulator(bool create) {
		if (FlexManipulator.IsValid())
			return FlexManipulator.Get();

		if (ClientFlexManipulator.Get() != null)
			return ClientFlexManipulator.Get();

		if (create) {
			C_FlexManipulate manipulator = new();
			if (!manipulator.InitializeAsClientEntity(null, RenderGroup.OpaqueEntity))
				return null;

			manipulator.SetParent(this, 0);
			manipulator.SetOwnerEntity(this);
			manipulator.SetLocalOrigin(default);
			manipulator.Spawn();
			ClientFlexManipulator.Set(manipulator);
			return ClientFlexManipulator.Get();
		}

		return null;
	}

	[NetworkName("m_OverrideViewTarget")]
	public Vector3 OverrideViewTarget;
	[NetworkName("m_fadeMinDist")]
	public float FadeMinDist;
	[NetworkName("m_fadeMaxDist")]
	public float FadeMaxDist;
	[NetworkName("m_flFadeScale")]
	public float FadeScale;
	[NetworkName("m_flCycle")]
	public TimeUnit_t Cycle;
	public readonly InterpolatedVar<float> iv_Cycle;
	public readonly InterpolatedVarArray<float> iv_flPoseParameter;

	[NetworkName("m_flPoseParameter")]
	public InlineArrayMaxStudioPoseParam<float> PoseParameter;
	public InlineArrayMaxStudioPoseParam<float> OldPoseParameters;
	public float PrevEventCycle;
	public int EventSequence;
	[NetworkName("m_flEncodedController")]
	public InlineArrayMaxStudioBoneCtrls<float> EncodedController;
	public InlineArrayMaxStudioBoneCtrls<float> OldEncodedController;
}
