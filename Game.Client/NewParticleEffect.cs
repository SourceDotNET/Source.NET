using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Mathematics;
using Source.Particles;

using System.Numerics;
using System.Runtime.CompilerServices;

using static Source.Particles.ParticleConstants;

namespace Game.Client;

public class CNewParticleEffect : ParticleCollection, IClientUnknown, IClientRenderable
{
	public CNewParticleEffect? NextEffect;
	public CNewParticleEffect? PrevEffect;

	protected string? DebugName;

	bool DontRemove;
	bool RemoveFlag;
	bool Drawn;
	bool NeedsBBoxUpdate;
	bool IsFirstFrame;
	bool AutoUpdateBBox;
	bool Allocated;
	bool ShouldSimulate_;
	bool ShouldPerformCullCheck_;

	Vector3 SortOrigin;
	EHANDLE Owner = new();
	readonly StrongBox<EHANDLE>[] ControlPointOwners = new StrongBox<EHANDLE>[MAX_PARTICLE_CONTROL_POINTS];

	Vector3 LastMin;
	Vector3 LastMax;

	bool ViewModelEffect;
	bool ShouldDraw_;

	int RefCount;

	ClientRenderHandle_t RenderHandle_ = INVALID_CLIENT_RENDER_HANDLE;

	public CNewParticleEffect(C_BaseEntity? owner, ParticleSystemDefinition? effect) {
		for (int i = 0; i < MAX_PARTICLE_CONTROL_POINTS; i++)
			ControlPointOwners[i] = new(new EHANDLE());
		Owner.Set(owner);
		Init(g_pParticleSystemMgr, effect);
		Construct();
	}

	public CNewParticleEffect(C_BaseEntity? owner, string? effectName) {
		for (int i = 0; i < MAX_PARTICLE_CONTROL_POINTS; i++)
			ControlPointOwners[i] = new(new EHANDLE());
		Owner.Set(owner);
		Init(g_pParticleSystemMgr, effectName);
		Construct();
	}

	void Construct() {
		SortOrigin = Vector3.Zero;

		DontRemove = false;
		RemoveFlag = false;
		Drawn = false;
		NeedsBBoxUpdate = false;
		IsFirstFrame = true;
		AutoUpdateBBox = false;
		Allocated = true;
		ShouldSimulate_ = true;
		ShouldPerformCullCheck_ = false;

		RefCount = 0;
		ParticleMgr.Instance.AddEffect(this);
		LastMax = new(-1.0e6f, -1.0e6f, -1.0e6f);
		LastMin = new(1.0e6f, 1.0e6f, 1.0e6f);
		MinBounds = new(1.0e6f, 1.0e6f, 1.0e6f);
		MaxBounds = new(-1.0e6f, -1.0e6f, -1.0e6f);
		DebugName = null;

		ViewModelEffect = Def != null && Def.IsViewModelEffect();
		ShouldDraw_ = true;
	}

	public static CNewParticleEffect Create(C_BaseEntity? owner, string? particleSystemName, string? debugName = null) {
		CNewParticleEffect ret = new(owner, particleSystemName);
		ret.DebugName = debugName ?? particleSystemName;
		ret.SetDynamicallyAllocated(true);
		return ret;
	}

	public static CNewParticleEffect Create(C_BaseEntity? owner, ParticleSystemDefinition def, string? debugName = null) {
		CNewParticleEffect ret = new(owner, def);
		ret.DebugName = debugName ?? def.GetName();
		ret.SetDynamicallyAllocated(true);
		return ret;
	}

	public void AddRef() => ++RefCount;

	public void Release() {
		Assert(RefCount > 0);
		--RefCount;

		if (RefCount == 0 && Allocated && IsFinished())
			SetRemoveFlag();
	}

	public void NotifyRemove() {
		if (!Allocated)
			return;

		Allocated = false;
		Owner.Get()?.ParticleProp().OnParticleSystemDeleted(this);
		g_Lua?.ReleaseUserTypeObject(this);
		Destroy();
	}

	public int IsReleased() => RefCount == 0 ? 1 : 0;

	public void SetSortOrigin(in Vector3 sortOrigin) => SortOrigin = sortOrigin;
	public ref readonly Vector3 GetSortOrigin() => ref SortOrigin;

	public bool ShouldDraw() => true;
	public void SetShouldDraw(bool shouldDraw) => ShouldDraw_ = shouldDraw;
	public bool GetShouldDraw() => ShouldDraw_;
	public bool IsTransparent() => IsTranslucent();
	public bool UsesPowerOfTwoFrameBufferTexture() => UsesPowerOfTwoFrameBufferTexture(true);
	public bool UsesFullFrameBufferTexture() => UsesFullFrameBufferTexture(true);

	public ref readonly QAngle GetRenderAngles() => ref MathLibConsts.vec3_angle;

	static Matrix3x4 RenderableToWorldMat;
	public ref readonly Matrix3x4 RenderableToWorldTransform() {
		MathLib.SetIdentityMatrix(out RenderableToWorldMat);
		MathLib.PositionMatrix(GetRenderOrigin(), ref RenderableToWorldMat);
		return ref RenderableToWorldMat;
	}

	public ref readonly Vector3 GetRenderOrigin() => ref SortOrigin;

	public string GetEffectName() => GetName();

	public void SetDontRemove(bool set) => DontRemove = set;
	public void SetDrawn(bool drawn) => Drawn = drawn;
	public void SetFirstFrameFlag(bool first) => IsFirstFrame = first;
	public void SetDynamicallyAllocated(bool dynamic = true) => Allocated = dynamic;
	public void SetNeedsBBoxUpdate(bool needsUpdate) => NeedsBBoxUpdate = needsUpdate;
	public void SetAutoUpdateBBox(bool needsUpdate) => AutoUpdateBBox = needsUpdate;
	public void SetRemoveFlag() => RemoveFlag = true;
	public bool GetRemoveFlag() => RemoveFlag;
	public bool GetFirstFrameFlag() => IsFirstFrame;
	public bool GetNeedsBBoxUpdate() => NeedsBBoxUpdate;
	public bool GetAutoUpdateBBox() => AutoUpdateBBox;
	public bool ShouldPerformCullCheck() => ShouldPerformCullCheck_;
	public void MarkShouldPerformCullCheck(bool enable) => ShouldPerformCullCheck_ = enable;
	public C_BaseEntity? GetOwner() => Owner.Get();
	public void SetOwner(C_BaseEntity? owner) => Owner.Set(owner);
	public void SetIsViewModelEffect(bool isViewModelEffect) => ViewModelEffect = isViewModelEffect;
	public bool GetIsViewModelEffect() => ViewModelEffect;
	public bool ShouldSimulate() => ShouldSimulate_;
	public void SetShouldSimulate(bool sim) => ShouldSimulate_ = sim;

	public ref readonly EHANDLE GetControlPointEntity(int whichPoint) => ref ControlPointOwners[whichPoint].Value;

	public void SetControlPointEntity(int whichPoint, C_BaseEntity? entity) {
		if ((uint)whichPoint >= MAX_PARTICLE_CONTROL_POINTS) {
			Warning($"CNewParticleEffect::SetControlPointEntity invalid control point {whichPoint}\n");
			return;
		}

		if (entity != null) {
			SetControlPointObject(whichPoint, ControlPointOwners[whichPoint]);
			ControlPointOwners[whichPoint].Value.Set(entity);
		}
		else
			SetControlPointObject(whichPoint, null);
	}

	public void Update(float timeDelta) => Owner.Get()?.ParticleProp().OnParticleSystemUpdated(this, timeDelta);

	public CNewParticleEffect? ReplaceWith(string? particleSystemName) {
		StopEmission(false, true, true);
		if (string.IsNullOrEmpty(particleSystemName))
			return null;

		CNewParticleEffect newEffect = Create(GetOwner(), particleSystemName, particleSystemName);
		if (!newEffect.IsValid())
			return newEffect;

		for (int i = 0; i < MAX_PARTICLE_CONTROL_POINTS; ++i) {
			if (!ReadsControlPoint(i))
				continue;

			newEffect.SetControlPoint(i, GetControlPointAtCurrentTime(i));
			GetControlPointOrientationAtCurrentTime(i, out Vector3 forward, out Vector3 right, out Vector3 up);
			newEffect.SetControlPointOrientation(i, forward, right, up);
			newEffect.SetControlPointParent(i, GetControlPointParent(i));
		}

		C_BaseEntity? owner = Owner.Get();
		if (owner == null)
			return newEffect;

		owner.ParticleProp().ReplaceParticleEffect(this, newEffect);
		return newEffect;
	}

	public void SetParticleCullRadius(float radius) { }

	public bool RecalculateBoundingBox() {
		BloatBoundsUsingControlPoint();
		if (MaxBounds.X < MinBounds.X) {
			MaxBounds = MinBounds = GetSortOrigin();
			return false;
		}
		return true;
	}

	public void GetRenderBounds(out Vector3 mins, out Vector3 maxs) {
		mins = MinBounds - GetRenderOrigin();
		maxs = MaxBounds - GetRenderOrigin();
	}

	public void DetectChanges() {
		if (RenderHandle_ == INVALID_CLIENT_RENDER_HANDLE)
			return;

		float buffer = ParticleMgr.cl_particleeffect_aabb_buffer.GetFloat();
		float extraBuffer = buffer * 1.3f;

		if (MinBounds.X < LastMin.X ||
			MinBounds.Y < LastMin.Y ||
			MinBounds.Z < LastMin.Z ||

			MinBounds.X > (LastMin.X + extraBuffer) ||
			MinBounds.Y > (LastMin.Y + extraBuffer) ||
			MinBounds.Z > (LastMin.Z + extraBuffer) ||

			MaxBounds.X > LastMax.X ||
			MaxBounds.Y > LastMax.Y ||
			MaxBounds.Z > LastMax.Z ||

			MaxBounds.X < (LastMax.X - extraBuffer) ||
			MaxBounds.Y < (LastMax.Y - extraBuffer) ||
			MaxBounds.Z < (LastMax.Z - extraBuffer)
		) {
			clientLeafSystem.RenderableChanged(RenderHandle_);
			LastMin = MinBounds - new Vector3(buffer, buffer, buffer);
			LastMax = MaxBounds + new Vector3(buffer, buffer, buffer);
		}
	}

	public int DrawModel(StudioFlags flags) => 0;

	public IClientUnknown GetIClientUnknown() => this;
	public ICollideable? GetCollideable() => null;
	public IClientNetworkable? GetClientNetworkable() => null;
	public IClientRenderable? GetClientRenderable() => this;
	public IClientEntity? GetIClientEntity() => null;
	public IClientThinkable? GetClientThinkable() => null;

	public void SetRefEHandle(in BaseHandle handle) => Assert(false);
	public ref readonly BaseHandle GetRefEHandle() {
		Assert(false);
		return ref Unsafe.NullRef<BaseHandle>();
	}

	public ClientShadowHandle_t GetShadowHandle() => ClientRenderableGlobals.CLIENTSHADOW_INVALID_HANDLE;
	public ref ClientRenderHandle_t RenderHandle() => ref RenderHandle_;
	public Model? GetModel() => null;
	public int GetBody() => 0;
	public int GetSkin() => 0;
	public bool UsesFlexDelayedWeights() => false;
	public void ComputeFxBlend() { }
	public int GetFxBlend() => 255;
	public void GetColorModulation(Span<float> color) => color[..3].Fill(1.0f);
	public bool LODTest() => true;
	public bool SetupBones(Span<Matrix3x4> boneToWorldOut, int maxBones, int boneMask, TimeUnit_t currentTime) => true;
	public void SetupWeights(Span<Matrix3x4> boneToWorld, Span<float> flexWeights, Span<float> flexDelayedWeights) { }
	public void DoAnimationEvents() { }
	public IPVSNotify? GetPVSNotifyInterface() => null;
	public void GetRenderBoundsWorldspace(out Vector3 mins, out Vector3 maxs) => IClientLeafSystemEngine.DefaultRenderBoundsWorldspace(this, out mins, out maxs);
	public void GetShadowRenderBounds(out Vector3 mins, out Vector3 maxs, ShadowType shadowType) => GetRenderBounds(out mins, out maxs);
	public bool ShouldReceiveProjectedTextures(ShadowFlags flags) => false;
	public bool GetShadowCastDistance(ref float dist, ShadowType shadowType) => false;
	public bool GetShadowCastDirection(ref Vector3 direction, ShadowType shadowType) => false;
	public bool IsShadowDirty() => false;
	public void MarkShadowDirty(bool dirty) { }
	public IClientRenderable? GetShadowParent() => null;
	public IClientRenderable? FirstShadowChild() => null;
	public IClientRenderable? NextShadowPeer() => null;
	public ShadowType ShadowCastType() => ShadowType.None;
	public void CreateModelInstance() { }
	public ModelInstanceHandle_t GetModelInstance() => MODEL_INSTANCE_INVALID;
	public int LookupAttachment(ReadOnlySpan<char> attachmentName) => -1;
	public bool GetAttachment(int number, out Vector3 origin, out QAngle angles) {
		origin = default;
		angles = default;
		return false;
	}
	public bool GetAttachment(int number, out Matrix3x4 matrix) {
		matrix = default;
		return false;
	}
	public Span<float> GetRenderClipPlane() => default;
	public void OnThreadedDrawSetup() { }
	public void RecordToolMessage() { }
	public bool IgnoresZBuffer() => false;
}
