using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Game.Server;

[LinkEntityToClass("info_lighting_relative")]
[NetworkName("CInfoLightingRelative")]
public partial class InfoLightingRelative : BaseEntity
{
	public InfoLightingRelative() {
		LightingLandmark = new();
	}

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(LightingLandmark),
	];

	[NetworkVar("m_hLightingLandmark")]
	public partial EHANDLE LightingLandmark { get; set; }
}

[NetworkName("CBaseAnimating")]
public partial class BaseAnimating : BaseEntity
{
	public BaseAnimating() {
		ModelScale = 1.0f;
		LightingOrigin = new();
		LightingOriginRelative = new();
		BoneManipulator = new();
		FlexManipulator = new();
	}

	public const int ANIMATION_SKIN_BITS = 10;
	public const int ANIMATION_BODY_BITS = 32;
	public const int ANIMATION_HITBOXSET_BITS = 2;
	public const int ANIMATION_POSEPARAMETER_BITS = 11;
	public const int ANIMATION_PLAYBACKRATE_BITS = 8;

	[PredictionTable]
	public static readonly PredictionField[] Prediction = [];

	[NetworkTable("DT_ServerAnimationData")]
	public static readonly NetworkProp[] ServerAnimationDataProps = [
		new(nameof(Cycle)) { Encoding = NetEncoding.Float, Bits = ANIMATION_CYCLE_BITS, Flags = PropFlags.ChangesOften | PropFlags.RoundDown, Low = -1.0f, High = 1.0f },
	];

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ForceBone)) { Bits = 8 },
		new(nameof(Force)) { Bits = 0, Flags = PropFlags.NoScale },

		new(nameof(Skin)) { Bits = ANIMATION_SKIN_BITS },
		new(nameof(Body)) { Bits = ANIMATION_BODY_BITS },

		new(nameof(HitboxSet)) { Bits = ANIMATION_HITBOXSET_BITS, Flags = PropFlags.Unsigned },

		nameof(ModelScale),

		new(nameof(PoseParameter)) { Bits = ANIMATION_POSEPARAMETER_BITS, Low = 0.0f, High = 1.0f },

		new(nameof(Sequence)) { Bits = ANIMATION_SEQUENCE_BITS, Flags = PropFlags.Unsigned },
		new(nameof(PlaybackRate)) { Encoding = NetEncoding.Float, Bits = ANIMATION_PLAYBACKRATE_BITS, Flags = PropFlags.RoundUp, Low = -4.0f, High = 12.0f },

		new(nameof(EncodedController)) { Bits = 11, Flags = PropFlags.RoundDown, Low = 0.0f, High = 1.0f },

		new(nameof(ClientSideAnimation)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(ClientSideFrameReset)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },

		new(nameof(NewSequenceParity)) { Bits = (int)EntityEffects.ParityBits, Flags = PropFlags.Unsigned },
		new(nameof(ResetEventsParity)) { Bits = (int)EntityEffects.ParityBits, Flags = PropFlags.Unsigned },
		new(nameof(MuzzleFlashParity)) { Bits = (int)EntityEffects.MuzzleflashBits, Flags = PropFlags.Unsigned },

		nameof(LightingOrigin),
		nameof(LightingOriginRelative),

		new(nameof(ServerAnimationDataProps)) { Name = "serveranimdata", Proxy = typeof(ClientSideAnimationProxy) },

		new(nameof(FadeMinDist)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeMaxDist)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeScale)) { Bits = 0, Flags = PropFlags.NoScale },

		// Gmod specific
		nameof(BoneManipulator),
		nameof(FlexManipulator),
		new(nameof(OverrideViewTarget)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_nForceBone")]
	public partial int ForceBone { get; set; }
	[NetworkVar("m_vecForce")]
	public partial Vector3 Force { get; set; }
	[NetworkVar("m_nSkin")]
	public partial int Skin { get; set; }
	[NetworkVar("m_nBody")]
	public partial int Body { get; set; }
	[NetworkVar("m_nHitboxSet")]
	public partial int HitboxSet { get; set; }

	public int GetHitboxSet() => HitboxSet;

	[NetworkVar("m_flModelScale")]
	public partial float ModelScale { get; set; }
	[NetworkVar("m_flPoseParameter")]
	public partial ref readonly InlineArrayMaxStudioPoseParam<float> PoseParameter { get; }
	public InlineArrayMaxStudioPoseParam<float> OldPoseParameters;
	public float PrevEventCycle;
	public int EventSequence;
	[NetworkVar("m_flEncodedController")]
	public partial ref readonly InlineArrayMaxStudioBoneCtrls<float> EncodedController { get; }
	public InlineArrayMaxStudioBoneCtrls<float> OldEncodedController;
	[NetworkVar("m_nSequence")] public partial int Sequence { get; set; }
	[NetworkVar("m_flPlaybackRate")] public partial TimeUnit_t PlaybackRate { get; set; }
	[NetworkVar("m_bClientSideAnimation")]
	public partial bool ClientSideAnimation { get; set; }
	[NetworkVar("m_bClientSideFrameReset")]
	public partial bool ClientSideFrameReset { get; set; }
	[NetworkVar("m_nNewSequenceParity")] public partial int NewSequenceParity { get; set; }
	[NetworkVar("m_nResetEventsParity")] public partial int ResetEventsParity { get; set; }
	[NetworkVar("m_nMuzzleFlashParity")]
	public partial int MuzzleFlashParity { get; set; }
	[NetworkVar("m_hLightingOrigin")]
	public partial EHANDLE LightingOrigin { get; set; }
	[NetworkVar("m_hLightingOriginRelative")]
	public partial EHANDLE LightingOriginRelative { get; set; }
	[NetworkVar("m_pBoneManipulator")]
	public partial EHANDLE BoneManipulator { get; set; }
	[NetworkVar("m_pFlexManipulator")]
	public partial EHANDLE FlexManipulator { get; set; }

	public virtual void SetFlexManipulator(BaseEntity? manipulator) => FlexManipulator = EHANDLE.FromEntity(manipulator);

	public virtual BaseEntity? GetFlexManipulator(bool create) {
		if (FlexManipulator.Get() == null && create) {
			BaseEntity? manipulator = CreateEntityByName("manipulate_flex");
			if (manipulator == null)
				return null;

			manipulator.SetParent(this, -1);
			manipulator.SetLocalOrigin(default);
			manipulator.Spawn();
			SetFlexManipulator(manipulator);
		}

		return FlexManipulator.Get();
	}

	[NetworkVar("m_fadeMinDist")]
	public partial float FadeMinDist { get; set; }
	[NetworkVar("m_fadeMaxDist")]
	public partial float FadeMaxDist { get; set; }
	[NetworkVar("m_flFadeScale")]
	public partial float FadeScale { get; set; }
	[NetworkVar("m_flCycle")] public partial TimeUnit_t Cycle { get; set; }
	[NetworkVar("m_OverrideViewTarget")]
	public partial Vector3 OverrideViewTarget { get; set; }

	public override void SetModel(ReadOnlySpan<char> modelName) {
		UnlockStudioHdr();
		StudioHdr = null;

		if (!modelName.IsStringEmpty) {
			int modelIndex = modelinfo.GetModelIndex(modelName);
			Model? model = modelinfo.GetModel(modelIndex);
			if (model != null && modelinfo.GetModelType(model) != ModelType.Studio)
				Msg($"Setting CBaseAnimating to non-studio model {modelName}  (type:{modelinfo.GetModelType(model)})\n");
		}

		if (BoneCacheHandle != 0) {
			Studio.DestroyBoneCache(BoneCacheHandle);
			BoneCacheHandle = 0;
		}

		Util.SetModel(this, modelName);

		// InitBoneControllers();
		SetSequence(0);

		PopulatePoseParameters();
	}

	protected virtual void PopulatePoseParameters() { }

	public void ResetSequence(int sequence) {
		SetSequence(sequence);
		ResetSequenceInfo();
	}

	public const int NUM_POSEPAREMETERS = 24;
	public const int NUM_BONECTRLS = 4;

	public virtual void InitBoneControllers() {
		int i;

		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null)
			return;

		int boneControllerCount = studioHdr.NumBoneControllers();
		if (boneControllerCount > NUM_BONECTRLS) {
			boneControllerCount = NUM_BONECTRLS;
#if DEBUG
			Warning($"Model {studioHdr.Name()} has too many bone controllers! (Max {NUM_BONECTRLS} allowed)\n");
#endif
		}

		for (i = 0; i < boneControllerCount; i++)
			SetBoneController(i, 0.0f);

		Assert(studioHdr.SequencesAvailable());

		if (studioHdr.SequencesAvailable()) {
			for (i = 0; i < studioHdr.GetNumPoseParameters(); i++)
				SetPoseParameter(i, 0.0f);
		}
	}

	public float SetBoneController(int controller, float value) {
		Assert(GetModelPtr() != null);

		StudioHdr? model = GetModelPtr();

		Assert(controller >= 0 && controller < NUM_BONECTRLS);

		float retVal = BoneSetup.Studio_SetController(model, controller, value, out float newValue);
		EncodedControllerForModify()[controller] = newValue;

		return retVal;
	}

	public float GetBoneController(int controller) {
		Assert(GetModelPtr() != null);

		StudioHdr? model = GetModelPtr();

		return BoneSetup.Studio_GetController(model, controller, EncodedController[controller]);
	}

	public void ResetActivityIndexes() {
		Assert(GetModelPtr() != null);
		Animation.ResetActivityIndexes(GetModelPtr());
	}

	public void ResetEventIndexes() {
		Assert(GetModelPtr() != null);
		Animation.ResetEventIndexes(GetModelPtr());
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

	public string? GetFlexControllerType(LocalFlexController flexController) {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null)
			return null;

		MStudioFlexController flexcontroller = studioHdr.FlexController(flexController);

		return flexcontroller.Type();
	}

	public Activity LookupActivity(ReadOnlySpan<char> label) {
		return Animation.LookupActivity(GetModelPtr(), label);
	}

	public int LookupSequence(ReadOnlySpan<char> label) {
		return Animation.LookupSequence(GetModelPtr(), label);
	}

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
	public TimeUnit_t GetSequenceGroundSpeed(int sequence) => GetSequenceGroundSpeed(GetModelPtr(), sequence);

	protected override StudioHdr? OnNewModel() {
		base.OnNewModel();

		if (IsDynamicModelLoading()) {
			// Called while dynamic model still loading -> new model, clear deferred state
			ResetSequenceInfoOnLoad = false;
			return null;
		}

		StudioHdr? hdr = GetModelPtr();

		if (ResetSequenceInfoOnLoad) {
			ResetSequenceInfoOnLoad = false;
			ResetSequenceInfo();
		}

		return hdr;
	}

	public static readonly ConVar npc_height_adjust = new("npc_height_adjust", "1", FCvar.Archive, "Enable test mode for ik height adjustment");

	public Activity GetSequenceActivity(int sequence) {
		if (sequence == -1) {
			return Activity.ACT_INVALID;
		}

		if (null == GetModelPtr())
			return Activity.ACT_INVALID;

		return (Activity)Animation.GetSequenceActivity(GetModelPtr()!, sequence, out _);
	}

	public TimeUnit_t GetPlaybackRate() => PlaybackRate;
	public void SetPlaybackRate(TimeUnit_t rate) => PlaybackRate = rate;

	public bool IsSequenceFinished() => SequenceFinished;
	public bool IsDissolving() => (GetFlags() & EntityFlags.Dissolving) != 0;

	public virtual void SetLightingOriginRelative(BaseEntity? lightingOriginRelative) => LightingOriginRelative = EHANDLE.FromEntity(lightingOriginRelative);
	public BaseEntity? GetLightingOriginRelative() => LightingOriginRelative.Get();

	public bool IsModelScaleFractional() => ModelScale < 1.0f;
	public bool IsModelScaled() => ModelScale > 1.0f + FLT_EPSILON || ModelScale < 1.0f - FLT_EPSILON;
	public float GetModelScale() => ModelScale;

	StudioHdr? StudioHdr;
	public Model? GetModel() => modelinfo.GetModel(GetModelIndex());

	// todo...
	public StudioHdr? GetModelPtr() {
		if (IsDynamicModelLoading())
			return null;

		if (StudioHdr == null && GetModel() != null)
			LockStudioHdr();

		return (StudioHdr != null && StudioHdr.IsValid()) ? StudioHdr : null;
	}

	readonly object StudioHdrInitLock = new();

	public void LockStudioHdr() {
		lock (StudioHdrInitLock) {
			Model? mdl = GetModel();
			if (mdl != null) {
				MDLHandle_t hStudioHdr = modelinfo.GetCacheHandle(mdl);
				if (hStudioHdr != MDLHANDLE_INVALID) {
					StudioHeader? pStudioHdr = mdlcache.LockStudioHdr(hStudioHdr);
					StudioHdr? pStudioHdrContainer = null;
					if (StudioHdr == null) {
						if (pStudioHdr != null) {
							pStudioHdrContainer = new StudioHdr();
							pStudioHdrContainer.Init(pStudioHdr, mdlcache);
						}
					}
					else
						pStudioHdrContainer = StudioHdr;

					Assert((pStudioHdr == null && pStudioHdrContainer == null) || (pStudioHdrContainer != null && pStudioHdrContainer.GetRenderHdr() == pStudioHdr));

					if (pStudioHdrContainer != null && pStudioHdrContainer.GetVirtualModel() != null) {
						MDLHandle_t hVirtualModel = (MDLHandle_t)(nint)(pStudioHdrContainer.GetRenderHdr().VirtualModel) & 0xffff;
						mdlcache.LockStudioHdr(hVirtualModel);
					}
					StudioHdr = pStudioHdrContainer;
				}
			}
		}
	}

	public void UnlockStudioHdr() {
		if (StudioHdr != null) {
			Model? mdl = GetModel();
			if (mdl != null) {
				mdlcache.UnlockStudioHdr(modelinfo.GetCacheHandle(mdl));
				if (StudioHdr.GetVirtualModel() != null) {
					MDLHandle_t virtualModel = (MDLHandle_t)(nint)(StudioHdr.GetRenderHdr().VirtualModel) & 0xffff;
					mdlcache.UnlockStudioHdr(virtualModel);
				}
			}
		}
	}

	public ReadOnlySpan<float> GetPoseParameterArray() => PoseParameter;

	public int GetSequence() => Sequence;


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

	public void GetBonePosition(int bone, out Vector3 origin, out QAngle angles) {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null) {
			AssertMsg(false, "BaseAnimating.GetBonePosition: model missing");
			origin = default;
			angles = default;
			return;
		}

		if (bone < 0 || bone >= studioHdr.NumBones()) {
			AssertMsg(false, "BaseAnimating.GetBonePosition: invalid bone index");
			origin = default;
			angles = default;
			return;
		}

		GetBoneTransform(bone, out Matrix3x4 boneToWorld);

		MathLib.MatrixAngles(boneToWorld, out angles, out origin);
	}

	public void GetBoneTransform(int bone, out Matrix3x4 boneToWorld) {
		StudioHdr? studioHdr = GetModelPtr();

		if (studioHdr == null) {
			AssertMsg(false, "BaseAnimating.GetBoneTransform: model missing");
			boneToWorld = default;
			return;
		}

		if (bone < 0 || bone >= studioHdr.NumBones()) {
			AssertMsg(false, "BaseAnimating.GetBoneTransform: invalid bone index");
			boneToWorld = default;
			return;
		}

		BoneCache cache = GetBoneCache();

		ref Matrix3x4 matrix = ref cache.GetCachedBone(bone);

		if (Unsafe.IsNullRef(ref matrix)) {
			MathLib.MatrixCopy(EntityToWorldTransform(), out boneToWorld);
			return;
		}

		MathLib.MatrixCopy(matrix, out boneToWorld);
	}

	public memhandle_t BoneCacheHandle;

	public BoneCache GetBoneCache() {
		StudioHdr? studioHdr = GetModelPtr();
		Assert(studioHdr != null);

		BoneCache pcache = Studio.GetBoneCache(BoneCacheHandle);
		int boneMask = Studio.BONE_USED_BY_HITBOX | Studio.BONE_USED_BY_ATTACHMENT;

		if (!pcache.IsNull()) {
			if (pcache.IsValid(gpGlobals.CurTime) && (pcache.BoneMask & boneMask) == boneMask && pcache.TimeValid <= gpGlobals.CurTime) {
				// Msg("%s:%s:%s (%x:%x:%8.4f) cache\n", GetClassname(), GetDebugName(), STRING(GetModelName()), boneMask, pcache->m_boneMask, pcache->m_timeValid );
				// in memory and still valid, use it!
				return pcache;
			}

			// in memory, but missing some of the bone masks
			if ((pcache.BoneMask & boneMask) != boneMask) {
				Studio.DestroyBoneCache(BoneCacheHandle);
				BoneCacheHandle = 0;
				pcache = default;
			}
		}

		Span<Matrix3x4> bonetoworld = stackalloc Matrix3x4[Studio.MAXSTUDIOBONES];
		SetupBones(bonetoworld, boneMask);

		if (!pcache.IsNull()) {
			// still in memory but out of date, refresh the bones.
			pcache.UpdateBones(bonetoworld, studioHdr.NumBones(), gpGlobals.CurTime);
		}
		else {
			BoneCacheParams parms = new();
			parms.StudioHdr = studioHdr;
			unsafe {
				parms.BoneToWorld = bonetoworld;
			}
			parms.CurTime = gpGlobals.CurTime;
			parms.BoneMask = boneMask;

			BoneCacheHandle = Studio.CreateBoneCache(in parms);
			pcache = Studio.GetBoneCache(BoneCacheHandle);
		}

		Assert(!pcache.IsNull());
		return pcache;
	}

	public void StopAnimation() => PlaybackRate = 0;

	public virtual void ClampRagdollForce(in Vector3 forceIn, out Vector3 forceOut) => forceOut = forceIn;

	public virtual bool BecomeRagdollOnClient(in Vector3 force) {
		if (CanBecomeRagdoll()) {
			VPhysicsDestroyObject();
			AddSolidFlags(SolidFlags.NotSolid);
			RenderFX = (byte)RenderFx.Ragdoll;

			ClampRagdollForce(force, out Vector3 clampedForce);
			Force = clampedForce;

			SetParent(null);

			AddFlag(EntityFlags.TransRagdoll);

			SetMoveType(Source.MoveType.None);
			SetThink(null);

			SetNextThink(gpGlobals.CurTime + 2.0f);
			SetThink(SUB_Remove);

			if (EffectEntity.Get() is EntityFlame fireChild) {
				fireChild.SetThink(fireChild.SUB_Remove);
				fireChild.SetNextThink(gpGlobals.CurTime + 0.1f);
			}

			return true;
		}
		return false;
	}

	public override bool IsRagdoll() => RenderFX == (byte)RenderFx.Ragdoll;

	public override void Teleport(Vector3? newPosition, QAngle? newAngles, Vector3? newVelocity) {
		base.Teleport(newPosition, newAngles, newVelocity);
		Ik?.ClearTargets();
		InitStepHeightAdjust();
	}

	TimeUnit_t IKGroundContactTime;
	float IKGroundMinHeight;
	float IKGroundMaxHeight;

	float EstIkFloor;
	float EstIkOffset;

	public void InitStepHeightAdjust() {
		IKGroundContactTime = 0;
		IKGroundMinHeight = 0;
		IKGroundMaxHeight = 0;

		EstIkFloor = GetAbsOrigin().Z;
		EstIkOffset = 0;
	}

	public void UpdateStepOrigin() {
		if (!npc_height_adjust.GetBool()) {
			EstIkOffset = 0;
			EstIkFloor = GetLocalOrigin().Z;
			return;
		}

		if (IKGroundContactTime > 0.2 && IKGroundContactTime > gpGlobals.CurTime - 0.2) {
			if ((GetFlags() & (EntityFlags.Fly | EntityFlags.Swim)) == 0 && GetMoveParent() == null && GetGroundEntity() != null && !GetGroundEntity()!.IsMoving()) {
				Vector3 toAbs = GetAbsOrigin() - GetLocalOrigin();
				if (toAbs.Z == 0.0) {
					AI_BaseNPC? npc = MyNPCPointer();
					float height = 18.0f;
					if (npc != null)
						height = npc.StepHeight();

					EstIkFloor = (float)(EstIkFloor * 0.2 + IKGroundMinHeight * 0.8);

					float bias = Math.Clamp((IKGroundMaxHeight - IKGroundMinHeight) - height, 0.0f, height);
					EstIkOffset = Math.Clamp(EstIkFloor - GetAbsOrigin().Z, -height + bias, 0.0f);
					return;
				}
			}
		}

		EstIkOffset *= 0.5f;
		EstIkFloor = GetLocalOrigin().Z;
	}

	public override Vector3 GetStepOrigin() {
		Vector3 tmp = GetLocalOrigin();
		tmp.Z += EstIkOffset;
		return tmp;
	}

	struct TraceFilterSkipNPCs(IHandleEntity? passentity, CollisionGroup collisionGroup) : ITraceFilter
	{
		TraceFilterSimple Simple = new(passentity, collisionGroup);

		public bool ShouldHitEntity(IHandleEntity serverEntity, Contents contentsMask) {
			if (Simple.ShouldHitEntity(serverEntity, contentsMask)) {
				BaseEntity? entity = EntityFromEntityHandle(serverEntity);
				if (entity == null)
					return true;

				if (entity.IsNPC())
					return false;

				return true;
			}
			return false;
		}
	}

	public virtual void CalculateIKLocks(TimeUnit_t currentTime) {
		if (Ik != null) {
			Ray ray = default;
			TraceFilterSkipNPCs traceFilter = new(this, GetCollisionGroup());
			GetVectors(out _, out _, out Vector3 up);

			float minHeight = float.MaxValue;
			float maxHeight = -float.MaxValue;

			for (int i = 0; i < Ik.Target.Count; i++) {
				Trace trace;
				IKTarget target = Ik.Target[i];

				if (!target.IsActive())
					continue;

				switch (target.Type) {
					case Studio.IK_GROUND: {
							Vector3 estGround;
							estGround = target.Est.Pos - GetAbsOrigin();
							estGround = estGround - (estGround * up) * up;
							estGround = GetAbsOrigin() + estGround + target.Est.Floor * up;

							MathLib.VectorMA(estGround, target.Est.Height, up, out Vector3 p1);
							MathLib.VectorMA(estGround, -target.Est.Height, up, out Vector3 p2);

							float r = Math.Max(target.Est.Radius, 1);

							ray.Init(p1, p2, new Vector3(-r, -r, 0), new Vector3(r, r, 1));
							enginetrace.TraceRay(ray, Mask.Solid, ref traceFilter, out trace);

							if (trace.StartSolid) {
								ray.Init(target.Trace.Hip, target.Est.Pos, new Vector3(-r, -r, 0), new Vector3(r, r, 1));

								enginetrace.TraceRay(ray, Mask.Solid, ref traceFilter, out trace);

								p1 = trace.EndPos;
								MathLib.VectorMA(p1, -target.Est.Height, up, out p2);
								ray.Init(p1, p2, new Vector3(-r, -r, 0), new Vector3(r, r, 1));

								enginetrace.TraceRay(ray, Mask.Solid, ref traceFilter, out trace);

								if (trace.StartSolid)
									break;
							}

							if (trace.DidHitWorld()) {
								target.SetPosWithNormalOffset(trace.EndPos, trace.Plane.Normal);
								target.SetNormal(trace.Plane.Normal);
							}
							else {
								target.SetPos(trace.EndPos);
								target.SetAngles(GetAbsAngles());
							}

							if (target.Est.Release < 0.1) {
								float height = Vector3.Dot(target.Est.Pos, up);
								maxHeight = Math.Max(height, maxHeight);
								minHeight = Math.Min(height, minHeight);
							}
						}
						break;
				}
			}

			if (minHeight != float.MaxValue && maxHeight != -float.MaxValue) {
				IKGroundContactTime = gpGlobals.CurTime;
				IKGroundMinHeight = minHeight;
				IKGroundMaxHeight = maxHeight;
			}
		}
	}

	public virtual bool CanBecomeRagdoll() {
		int ragdollSequence = SelectWeightedSequence(Activity.ACT_DIERAGDOLL);

		if (ragdollSequence == StudioHdr.ACTIVITY_NOT_AVAILABLE)
			return false;

		if ((GetFlags() & EntityFlags.TransRagdoll) != 0)
			return false;

		return true;
	}

	public virtual void GetSkeleton(StudioHdr? studioHdr, Span<Vector3> pos, Span<Quaternion> q, int boneMask) {
		if (studioHdr == null) {
			AssertMsg(false, "BaseAnimating.GetSkeleton() without a model");
			return;
		}

		BoneSetup boneSetup = new(studioHdr, boneMask, PoseParameter);
		boneSetup.InitPose(pos, q);

		boneSetup.AccumulatePose(pos, q, GetSequence(), GetCycle(), 1.0f, gpGlobals.CurTime, Ik);

		if (Ik == null) {
			if (!IsRagdoll())
				boneSetup.CalcAutoplaySequences(pos, q, gpGlobals.CurTime, null);
		}
		else
			boneSetup.CalcAutoplaySequences(pos, q, gpGlobals.CurTime, CreateAutoplayIK(studioHdr, gpGlobals.CurTime, boneMask));
	}

	public virtual void SetupBones(Span<Matrix3x4> boneToWorld, int boneMask) {
		Assert(GetModelPtr() != null);

		StudioHdr? studioHdr = GetModelPtr();

		if (studioHdr == null) {
			AssertMsg(false, "BaseAnimating.GetSkeleton() without a model");
			return;
		}

		Assert(!IsEFlagSet(EFL.SettingUpBones));

		AddEFlags(EFL.SettingUpBones);

		Span<Vector3> pos = stackalloc Vector3[Studio.MAXSTUDIOBONES];
		Span<Quaternion> q = stackalloc Quaternion[Studio.MAXSTUDIOBONES];

		Vector3 adjOrigin = GetAbsOrigin() + new Vector3(0, 0, EstIkOffset);

		if (Ik != null) {
			BoneBitList boneComputed = new();
			Ik.Init(studioHdr, GetAbsAngles(), adjOrigin, gpGlobals.CurTime, NextIKFrameCounter(), boneMask);
			GetSkeleton(studioHdr, pos, q, boneMask);

			Ik.UpdateTargets(pos, q, boneToWorld, ref boneComputed);
			CalculateIKLocks(gpGlobals.CurTime);
			Ik.SolveDependencies(pos, q, boneToWorld, ref boneComputed);
			ApplyFootClearance(studioHdr, pos, q, boneToWorld);
		}
		else
			GetSkeleton(studioHdr, pos, q, boneMask);

		BoneSetup.Studio_BuildMatrices(studioHdr, GetAbsAngles(), adjOrigin, pos, q, -1, GetModelScale(), boneToWorld, boneMask);

		RemoveEFlags(EFL.SettingUpBones);
	}

	public int LookupBone(ReadOnlySpan<char> name) {
		StudioHdr? studioHdr = GetModelPtr();
		Assert(studioHdr);
		if (studioHdr == null)
			return -1;
		return BoneSetup.Studio_BoneIndexByName(studioHdr, name);
	}

	public int LookupAttachment(ReadOnlySpan<char> name) {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null) {
			AssertMsg(false, "BaseAnimating.LookupAttachment: model missing");
			return 0;
		}

		// The +1 is to make attachment indices be 1-based (namely 0 == invalid or unused attachment)
		return BoneSetup.Studio_FindAttachment(studioHdr, name) + 1;
	}

	public bool GetAttachment(ReadOnlySpan<char> attachmentName, out Vector3 absOrigin, out QAngle absAngles) {
		return GetAttachment(LookupAttachment(attachmentName), out absOrigin, out absAngles);
	}


	public bool GetAttachment(int attachment, out Vector3 absOrigin, out QAngle absAngles) {
		Matrix3x4 attachmentToWorld;

		bool bRet = GetAttachment(attachment, out attachmentToWorld);
		MathLib.MatrixAngles(attachmentToWorld, out absAngles, out absOrigin);
		return bRet;
	}


	public bool GetAttachment(int attachment, out Matrix3x4 attachmentToWorld) {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null) {
			MathLib.MatrixCopy(EntityToWorldTransform(), out attachmentToWorld);
			AssertMsg(false, "BaseAnimating.GetAttachment: model missing");
			return false;
		}

		if (attachment < 1 || attachment > studioHdr.GetNumAttachments()) {
			MathLib.MatrixCopy(EntityToWorldTransform(), out attachmentToWorld);
			// Assert(!"BaseAnimating.GetAttachment: invalid attachment index");
			return false;
		}

		MStudioAttachment pattachment = studioHdr.Attachment(attachment - 1)!;
		int iBone = studioHdr.GetAttachmentBone(attachment - 1);

		GetBoneTransform(iBone, out Matrix3x4 bonetoworld);
		if ((pattachment.Flags & Studio.ATTACHMENT_FLAG_WORLD_ALIGN) == 0) {
			MathLib.ConcatTransforms(bonetoworld, pattachment.Local, out attachmentToWorld);
		}
		else {
			Vector3 vecLocalBonePos, vecWorldBonePos;
			MathLib.MatrixGetColumn(pattachment.Local, 3, out vecLocalBonePos);
			MathLib.VectorTransform(vecLocalBonePos, bonetoworld, out vecWorldBonePos);

			MathLib.SetIdentityMatrix(out attachmentToWorld);
			MathLib.MatrixSetColumn(vecWorldBonePos, 3, ref attachmentToWorld);
		}

		return true;
	}

	public bool GetAttachment(ReadOnlySpan<char> attachmentName, out Vector3 absOrigin, out Vector3 forward, out Vector3 right, out Vector3 up) {
		return GetAttachment(LookupAttachment(attachmentName), out absOrigin, out forward, out right, out up);
	}

	public bool GetAttachment(int attachment, out Vector3 absOrigin, out Vector3 forward, out Vector3 right, out Vector3 up) {
		bool bRet = GetAttachment(attachment, out Matrix3x4 attachmentToWorld);
		MathLib.MatrixPosition(attachmentToWorld, out absOrigin);
		MathLib.MatrixGetColumn(attachmentToWorld, 0, out forward);
		MathLib.MatrixGetColumn(attachmentToWorld, 1, out right);
		MathLib.MatrixGetColumn(attachmentToWorld, 2, out up);
		return bRet;
	}

	public float EdgeLimitPoseParameter(int parameter, float value, float baseValue = 0.0f) {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null)
			return value;

		if (parameter < 0 || parameter >= studioHdr.GetNumPoseParameters())
			return value;

		MStudioPoseParamDesc pose = studioHdr.PoseParameter(parameter);

		if (pose.Loop != 0 || pose.Start == pose.End)
			return value;

		return MathLibShared.RangeCompressor(value, pose.Start, pose.End, baseValue);
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
			PoseParameterForModify()[parameter] = newValue;
		}

		return value;
	}

	public TimeUnit_t SequenceDuration(StudioHdr? studioHdr, int sequence) {
		if (studioHdr == null) {
			DevWarning(2, $"BaseAnimating.SequenceDuration( {sequence} ) NULL pstudiohdr on {GetClassname()}!\n");
			return 0.1;
		}
		if (!studioHdr.SequencesAvailable()) {
			return 0.1;
		}
		if (sequence >= studioHdr.GetNumSeq() || sequence < 0) {
			DevWarning(2, $"BaseAnimating.SequenceDuration( {sequence} ) out of range\n");
			return 0.1;
		}

		return BoneSetup.Studio_Duration(studioHdr, sequence, GetPoseParameterArray());
	}
	public TimeUnit_t SequenceDuration(int sequence) => SequenceDuration(GetModelPtr(), sequence);
	public TimeUnit_t SequenceDuration() => SequenceDuration(GetSequence());

	public float GetSequenceCycleRate(StudioHdr? studioHdr, int sequence) {
		float t = (float)SequenceDuration(studioHdr, sequence);

		if (t != 0.0f)
			return 1.0f / t;

		return t;
	}

	public float GetSequenceCycleRate(int sequence) => GetSequenceCycleRate(GetModelPtr(), sequence);

	public float GetLastVisibleCycle(StudioHdr? studioHdr, int sequence) {
		if (studioHdr == null) {
			DevWarning(2, $"BaseAnimating.LastVisibleCycle( {sequence} ) NULL pstudiohdr on {GetClassname()}!\n");
			return 1.0f;
		}

		if (0 == (Animation.GetSequenceFlags(studioHdr, sequence) & StudioAnimSeqFlags.Looping))
			return 1.0f - studioHdr.Seqdesc(sequence).FadeOutTime * GetSequenceCycleRate(sequence) * (float)PlaybackRate;
		else
			return 1.0f;
	}

	public const float MAX_ANIMTIME_INTERVAL = 0.2f;

	public TimeUnit_t GetAnimTimeInterval() {
		TimeUnit_t interval;
		if (AnimTime < gpGlobals.CurTime)
			interval = Math.Clamp(gpGlobals.CurTime - AnimTime, 0, MAX_ANIMTIME_INTERVAL);
		else
			interval = Math.Clamp(AnimTime - PrevAnimTime, 0, MAX_ANIMTIME_INTERVAL);
		return interval;
	}

	public bool HasMovement(int sequence) {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null)
			return false;

		if (BoneSetup.Studio_SeqMovement(studioHdr, sequence, 0.0f, 1.0f, GetPoseParameterArray(), out _, out _))
			return true;

		return false;
	}

	public bool GetIntervalMovement(TimeUnit_t intervalUsed, out bool moveSeqFinished, out Vector3 newPosition, out QAngle newAngles) {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null || !studioHdr.SequencesAvailable()) {
			moveSeqFinished = default;
			newPosition = default;
			newAngles = default;
			return false;
		}

		float computedCycleRate = GetSequenceCycleRate(GetSequence());

		TimeUnit_t nextCycle = GetCycle() + intervalUsed * computedCycleRate * PlaybackRate;

		if (!SequenceLoops && nextCycle > 1.0) {
			intervalUsed = GetCycle() / (computedCycleRate * PlaybackRate);
			nextCycle = 1.0;
			moveSeqFinished = true;
		}
		else {
			moveSeqFinished = false;
		}

		if (BoneSetup.Studio_SeqMovement(studioHdr, GetSequence(), (float)GetCycle(), (float)nextCycle, GetPoseParameterArray(), out Vector3 deltaPos, out QAngle deltaAngles)) {
			MathLib.VectorYawRotate(deltaPos, GetLocalAngles().Y, out deltaPos);
			newPosition = GetLocalOrigin() + deltaPos;
			newAngles = default;
			newAngles.Y = GetLocalAngles().Y + deltaAngles.Y;
			return true;
		}
		else {
			newPosition = GetLocalOrigin();
			newAngles = GetLocalAngles();
			return false;
		}
	}

	public void InvalidateBoneCache() => Studio.InvalidateBoneCache(BoneCacheHandle);

	public void InvalidateBoneCacheIfOlderThan(TimeUnit_t deltaTime) {
		BoneCache pcache = Studio.GetBoneCache(BoneCacheHandle);
		if (pcache.IsNull() || !pcache.IsValid(gpGlobals.CurTime, deltaTime) || pcache.TimeValid > gpGlobals.CurTime)
			InvalidateBoneCache();
	}

	public void StudioFrameAdvanceInternal(StudioHdr? studioHdr, TimeUnit_t cycleDelta) {
		TimeUnit_t newCycle = GetCycle() + cycleDelta;
		if (newCycle < 0.0 || newCycle >= 1.0) {
			if (SequenceLoops)
				newCycle -= (int)newCycle;
			else
				newCycle = (newCycle < 0.0) ? 0.0 : 1.0;
			SequenceFinished = true;
		}
		else if (newCycle > GetLastVisibleCycle(studioHdr, GetSequence()))
			SequenceFinished = true;

		SetCycle(newCycle);

		GroundSpeed = GetSequenceGroundSpeed(studioHdr, GetSequence()) * GetModelScale();

		InvalidatePhysicsRecursive(InvalidatePhysicsBits.AnimationChanged);

		InvalidateBoneCacheIfOlderThan(0);
	}

	public virtual void StudioFrameAdvance() {
		StudioHdr? studioHdr = GetModelPtr();

		if (studioHdr == null || !studioHdr.SequencesAvailable())
			return;

		if (PrevAnimTime == 0)
			PrevAnimTime = AnimTime;

		TimeUnit_t interval = gpGlobals.CurTime - AnimTime;
		interval = Math.Clamp(interval, 0, MAX_ANIMTIME_INTERVAL);

		if (interval <= 0.001)
			return;

		PrevAnimTime = AnimTime;
		AnimTime = gpGlobals.CurTime;

		TimeUnit_t cycleRate = GetSequenceCycleRate(studioHdr, GetSequence()) * PlaybackRate;
		StudioFrameAdvanceInternal(studioHdr, interval * cycleRate);
	}
	public virtual void DoMuzzleFlash() => MuzzleFlashParity = unchecked((byte)((MuzzleFlashParity + 1) & ((1 << (int)EntityEffects.MuzzleflashBits) - 1)));
	public virtual void SetSequence(int sequence) {
		Sequence = sequence;
	}
	public int SelectWeightedSequence(Activity activity) {
		return Animation.SelectWeightedSequence(GetModelPtr(), activity, GetSequence());
	}
	public int SelectWeightedSequence(Activity activity, int curSequence) {
		Assert(activity != Activity.ACT_INVALID);
		Assert(GetModelPtr() != null);
		return Animation.SelectWeightedSequence(GetModelPtr(), activity, curSequence);
	}
	public string GetSequenceName(int sequence) {
		if (sequence == -1)
			return "Not Found!";

		if (GetModelPtr() == null)
			return "No model!";

		return Animation.GetSequenceName(GetModelPtr(), sequence);
	}
	public int SelectHeaviestSequence(Activity activity) {
		Assert(GetModelPtr() != null);
		return Animation.SelectHeaviestSequence(GetModelPtr(), activity);
	}

	public virtual void DispatchAnimEvents(BaseAnimating eventHandler) {
		if (PlaybackRate == 0.0)
			return;

		AnimEvent animEvent = default;

		StudioHdr? studiohdr = GetModelPtr();

		if (studiohdr == null) {
			AssertMsg(false, "BaseAnimating.DispatchAnimEvents: model missing");
			return;
		}

		if (!studiohdr.SequencesAvailable())
			return;

		if (studiohdr.Seqdesc(GetSequence()).NumEvents == 0)
			return;

		float cycleRate = GetSequenceCycleRate(GetSequence()) * (float)PlaybackRate;
		float start = (float)LastEventCheck;
		float end = (float)GetCycle();

		if (!SequenceLoops && SequenceFinished)
			end = 1.01f;
		LastEventCheck = end;

		int index = 0;
		while ((index = Animation.GetAnimationEvent(studiohdr, GetSequence(), ref animEvent, start, end, index)) != 0) {
			animEvent.Source = this;
			if (cycleRate > 0.0f) {
				float cycle = animEvent.Cycle;
				if (cycle > GetCycle())
					cycle = cycle - 1.0f;
				animEvent.EventTime = AnimTime + (cycle - GetCycle()) / cycleRate + GetAnimTimeInterval();
			}

			eventHandler.HandleAnimEvent(ref animEvent);

			StudioHdr? nowStudioHdr = GetModelPtr();
			if (nowStudioHdr != studiohdr) {
				AssertMsg(false, $"{GetDebugName()} has changed its model while processing AnimEvents on sequence {GetSequence()}. Aborting dispatch.\n");
				Warning($"{GetDebugName()} has changed its model while processing AnimEvents on sequence {GetSequence()}. Aborting dispatch.\n");
				break;
			}
		}
	}

	public virtual void HandleAnimEvent(ref AnimEvent animEvent) {
		if ((animEvent.Type & AnimEventType.NewEventSystem) != 0 && (animEvent.Type & AnimEventType.Server) != 0) {
			if (animEvent.Event == (int)AnimEventId.AE_SV_PLAYSOUND) {
				EmitSound(animEvent.Options);
				return;
			}
			else if (animEvent.Event == (int)AnimEventId.AE_RAGDOLL) {
				throw new NotImplementedException();
			}
			else if (animEvent.Event == (int)AnimEventId.AE_SV_DUSTTRAIL) {
				throw new NotImplementedException();
			}
		}

		string? name = EventList.NameForIndex(animEvent.Event);
		if (name != null)
			DevWarning(1, $"Unhandled animation event {name} for {GetClassname()}\n");
		else
			DevWarning(1, $"Unhandled animation event {animEvent.Event} for {GetClassname()}\n");
	}
	public float GroundSpeed;
	public bool SequenceLoops;
	public bool ResetSequenceInfoOnLoad;
	public bool SequenceFinished;
	public TimeUnit_t LastEventCheck;
	public TimeUnit_t GetCycle() => Cycle;
	public void SetCycle(TimeUnit_t cycle) => Cycle = cycle;

	public void SetFadeDistance(float minFadeDist, float maxFadeDist) {
		FadeMinDist = minFadeDist;
		FadeMaxDist = maxFadeDist;
	}
	public float GetSequenceMoveDist(StudioHdr? studioHdr, int sequence) {
		Animation.GetSequenceLinearMotion(studioHdr, sequence, GetPoseParameterArray(), out Vector3 ret);

		return ret.Length();
	}
	public float GetSequenceGroundSpeed(StudioHdr? studioHdr, int sequence) {
		TimeUnit_t t = SequenceDuration(studioHdr, sequence);

		if (t > 0)
			return (GetSequenceMoveDist(studioHdr, sequence) / (float)t);
		else
			return 0;
	}
	public float GetSequenceMoveYaw(int sequence) {
		Assert(GetModelPtr() != null);
		Animation.GetSequenceLinearMotion(GetModelPtr(), sequence, GetPoseParameterArray(), out Vector3 vecReturn);

		if (vecReturn.Length() > 0)
			return Util.VecToYaw(vecReturn);

		return Animation.NOMOTION;
	}
	public void GetSequenceLinearMotion(int sequence, out Vector3 vec) {
		Assert(GetModelPtr() != null);
		Animation.GetSequenceLinearMotion(GetModelPtr(), sequence, GetPoseParameterArray(), out vec);
	}
	public virtual float GetIdealSpeed() => GroundSpeed;
	public virtual float GetIdealAccel() => GetIdealSpeed() + 50;
	public bool HasPoseParameter(int sequence, ReadOnlySpan<char> name) {
		int parameter = LookupPoseParameter(name);
		if (parameter == -1)
			return false;

		return HasPoseParameter(sequence, parameter);
	}
	public bool HasPoseParameter(int sequence, int parameter) {
		StudioHdr? studioHdr = GetModelPtr();

		if (studioHdr == null)
			return false;

		if (!studioHdr.SequencesAvailable())
			return false;

		if (sequence < 0 || sequence >= studioHdr.GetNumSeq())
			return false;

		MStudioSeqDesc seqdesc = studioHdr.Seqdesc(sequence);
		if (studioHdr.GetSharedPoseParameter(sequence, seqdesc.ParamIndex[0]) == parameter ||
			studioHdr.GetSharedPoseParameter(sequence, seqdesc.ParamIndex[1]) == parameter)
			return true;
		return false;
	}
	public float GetInstantaneousVelocity(float interval = 0.0f) {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null)
			return 0;

		float nextCycle = (float)(GetCycle() + interval * GetSequenceCycleRate(GetSequence()) * PlaybackRate);

		BoneSetup.Studio_SeqVelocity(studioHdr, GetSequence(), nextCycle, GetPoseParameterArray(), out Vector3 vecVelocity);
		vecVelocity *= (float)PlaybackRate;

		return vecVelocity.Length();
	}
	public float GetEntryVelocity(int sequence) {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null)
			return 0;

		BoneSetup.Studio_SeqVelocity(studioHdr, sequence, 0.0f, GetPoseParameterArray(), out Vector3 vecVelocity);

		return vecVelocity.Length();
	}
	public float GetMovementFrame(float dist) {
		StudioHdr? studioHdr = GetModelPtr();
		if (studioHdr == null)
			return 0;

		return BoneSetup.Studio_FindSeqDistance(studioHdr, GetSequence(), GetPoseParameterArray(), dist);
	}
	public void ResetSequenceInfo() {
		if (GetSequence() == -1)
			// This shouldn't happen.  Setting m_nSequence blindly is a horrible coding practice.
			SetSequence(0);

		if (IsDynamicModelLoading()) {
			ResetSequenceInfoOnLoad = true;
			return;
		}

		StudioHdr? studioHdr = GetModelPtr();
		GroundSpeed = GetSequenceGroundSpeed(studioHdr, GetSequence()) * GetModelScale();
		SequenceLoops = ((Animation.GetSequenceFlags(studioHdr, GetSequence()) & StudioAnimSeqFlags.Looping) != 0);
		// m_flAnimTime = gpGlobals.time;
		PlaybackRate = 1.0;
		SequenceFinished = false;
		LastEventCheck = 0;

		NewSequenceParity = (NewSequenceParity + 1) & (int)EntityEffects.ParityMask;
		ResetEventsParity = (ResetEventsParity + 1) & (int)EntityEffects.ParityMask;

		// FIXME: why is this called here?  Nothing should have changed to make this necessary
		if (studioHdr != null)
			Animation.SetEventIndexForSequence(studioHdr.Seqdesc(GetSequence()));
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

	public int GetNumBodyGroups() => Animation.GetNumBodyGroups(GetModelPtr());

	public void SetBodygroup(int group, int value) {
		int newBody = Body;
		Animation.SetBodygroup(GetModelPtr(), ref newBody, group, value);
		Body = newBody;
	}

	public override BaseAnimating? GetBaseAnimating() => this;

	public bool IsUsingClientSideAnimation() {
		return ClientSideAnimation;
	}

	public void UseClientSideAnimation(bool use) {
		if (ClientSideAnimation != use) {
			NetworkStateChanged();
			ClientSideAnimation = use;
		}
	}
}
