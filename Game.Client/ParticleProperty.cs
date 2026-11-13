using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Mathematics;
using Source.Particles;

using System.Numerics;

using static Source.Particles.ParticleConstants;

namespace Game.Client;

public sealed class ParticlePropertyControlPoint
{
	public int ControlPoint;
	public ParticleAttachment AttachType = ParticleAttachment.AbsOriginFollow;
	public int AttachmentPoint;
	public Vector3 OriginOffset;
	public EHANDLE Entity = new();
}

public sealed class ParticleEffectList
{
	public readonly List<ParticlePropertyControlPoint> ControlPoints = [];
	CNewParticleEffect? ParticleEffect_;

	public CNewParticleEffect? ParticleEffect {
		get => ParticleEffect_;
		set {
			value?.AddRef();
			ParticleEffect_?.Release();
			ParticleEffect_ = value;
		}
	}

	public void MarkDeleted() => ParticleEffect_ = null;
}

public sealed class ParticleProperty
{
	public const int INVALID_PARTICLE_ATTACHMENT = 0;

	public static readonly ConVar cl_particle_batch_mode = new("cl_particle_batch_mode", "1");

	C_BaseEntity? Outer;
	readonly List<ParticleEffectList> ParticleEffects = [];
	long DormancyChangedAtFrame;

	public ParticleProperty() => Init(null);

	public void Init(C_BaseEntity? entity) => Outer = entity;
	public C_BaseEntity? GetOuter() => Outer;

	void RemoveEffectAt(int index) {
		ParticleEffectList entry = ParticleEffects[index];
		ParticleEffects.RemoveAt(index);
		entry.ParticleEffect = null;
	}

	static int GetParticleAttachment(C_BaseEntity? entity, ReadOnlySpan<char> attachmentName, ReadOnlySpan<char> particleName) {
		C_BaseAnimating? animating = entity?.GetBaseAnimating();
		Assert(animating != null);
		if (animating == null)
			return INVALID_PARTICLE_ATTACHMENT;

		return animating.LookupAttachment(attachmentName);
	}

	public CNewParticleEffect? Create(string particleName, ParticleAttachment attachType, ReadOnlySpan<char> attachmentName) {
		int attachment = GetParticleAttachment(GetOuter(), attachmentName, particleName);
		if (attachment == INVALID_PARTICLE_ATTACHMENT)
			return null;

		return Create(particleName, attachType, attachment);
	}

	public CNewParticleEffect? Create(string particleName, ParticleAttachment attachType, int attachmentPoint = INVALID_PARTICLE_ATTACHMENT, Vector3 originOffset = default) {
		if (GameRules.g_pGameRules != null)
			particleName = GameRules.g_pGameRules.TranslateEffectForVisionFilter("particles", particleName).ToString();

		int batchMode = cl_particle_batch_mode.GetInt();
		ParticleSystemDefinition? def = g_pParticleSystemMgr.FindParticleSystem(particleName);
		bool requestedBatch = (batchMode == 2) || ((batchMode == 1) && def != null && def.ShouldBatch);
		if (attachType == ParticleAttachment.CustomOrigin && requestedBatch) {
			int index = FindEffect(particleName);
			if (index >= 0) {
				CNewParticleEffect effect = ParticleEffects[index].ParticleEffect!;
				effect.Restart();
				return effect;
			}
		}

		if (def == null) {
			Warning($"Attempting to create unknown particle system '{particleName}' \n");
			return null;
		}

		ParticleEffectList newEffect = new();
		ParticleEffects.Add(newEffect);
		int effectIndex = ParticleEffects.Count - 1;
		newEffect.ParticleEffect = CNewParticleEffect.Create(Outer, def);

		if (!newEffect.ParticleEffect!.IsValid()) {
			Warning($"Attempting to create particle system '{particleName}' with unprecached material!\n");
			ParticleMgr.Instance.RemoveEffect(newEffect.ParticleEffect);
			return null;
		}

		AddControlPoint(effectIndex, 0, GetOuter(), attachType, attachmentPoint, originOffset);

		Outer?.OnNewParticleEffect(particleName, newEffect.ParticleEffect);

		return newEffect.ParticleEffect;
	}

	public void AddControlPoint(CNewParticleEffect effect, int point, C_BaseEntity? entity, ParticleAttachment attachType, string? attachmentName = null, Vector3 originOffset = default) {
		int attachment = INVALID_PARTICLE_ATTACHMENT;
		if (attachmentName != null)
			attachment = GetParticleAttachment(entity, attachmentName, effect.GetEffectName());

		for (int i = 0; i < ParticleEffects.Count; i++) {
			if (ParticleEffects[i].ParticleEffect == effect)
				AddControlPoint(i, point, entity, attachType, attachment, originOffset);
		}
	}

	public void AddControlPoint(int effectIndex, int point, C_BaseEntity? entity, ParticleAttachment attachType, int attachmentPoint = INVALID_PARTICLE_ATTACHMENT, Vector3 originOffset = default) {
		Assert(effectIndex >= 0 && effectIndex < ParticleEffects.Count);
		ParticleEffectList effect = ParticleEffects[effectIndex];
		Assert(effect.ControlPoints.Count < MAX_PARTICLE_CONTROL_POINTS);

		ParticlePropertyControlPoint? newPoint = null;
		int index = point;
		foreach (ParticlePropertyControlPoint existing in effect.ControlPoints) {
			if (existing.ControlPoint == point)
				newPoint = existing;
		}

		if (newPoint == null) {
			newPoint = new();
			effect.ControlPoints.Add(newPoint);
			index = effect.ControlPoints.Count - 1;
		}

		newPoint.ControlPoint = point;
		newPoint.Entity.Set(entity);
		newPoint.AttachType = attachType;
		newPoint.AttachmentPoint = attachmentPoint;
		newPoint.OriginOffset = originOffset;

		UpdateParticleEffect(effect, true, index);
	}

	public void SetControlPointParent(CNewParticleEffect effect, int whichControlPoint, int parentIdx) => effect.SetControlPointParent(whichControlPoint, parentIdx);
	public void SetControlPointParent(int effectIndex, int whichControlPoint, int parentIdx) { }

	public CNewParticleEffect? GetParticleEffectFromIdx(int idx) => ParticleEffects[idx].ParticleEffect;

	public void ReplaceParticleEffect(CNewParticleEffect oldEffect, CNewParticleEffect newEffect) {
		for (int i = 0; i < ParticleEffects.Count; ++i) {
			if (oldEffect != ParticleEffects[i].ParticleEffect)
				continue;

			ParticleEffects[i].ParticleEffect = newEffect;
			UpdateParticleEffect(ParticleEffects[i], true);
		}
	}

	public void StopEmission(CNewParticleEffect? effect = null, bool wakeOnStop = false, bool destroyAsleepSystems = false) {
		bool removeInstantly = DormancyChangedAtFrame == gpGlobals.FrameCount;

		if (effect != null) {
			if (FindEffect(effect) != -1)
				effect.StopEmission(false, removeInstantly, wakeOnStop);
			return;
		}

		double now = g_pParticleSystemMgr.GetLastSimulationTime();
		for (int i = ParticleEffects.Count - 1; i >= 0; i--) {
			CNewParticleEffect tmp = ParticleEffects[i].ParticleEffect!;
			bool removeSystem = removeInstantly || (destroyAsleepSystems && now >= tmp.NextSleepTime);
			if (removeSystem) {
				RemoveEffectAt(i);
				tmp.SetOwner(null);
			}
			tmp.StopEmission(false, removeSystem, !removeSystem && wakeOnStop);
		}
	}

	public void StopEmissionAndDestroyImmediately(CNewParticleEffect? effect = null) {
		if (effect != null) {
			int index = FindEffect(effect);
			if (index != -1) {
				RemoveEffectAt(index);
				effect.SetOwner(null);
				effect.StopEmission(false, true);
			}
			return;
		}

		for (int i = ParticleEffects.Count - 1; i >= 0; i--) {
			CNewParticleEffect tmp = ParticleEffects[i].ParticleEffect!;
			RemoveEffectAt(i);
			tmp.SetOwner(null);
			tmp.StopEmission(false, true);
		}
	}

	public void StopParticlesInvolving(C_BaseEntity entity) {
		Assert(entity != null);

		bool removeInstantly = DormancyChangedAtFrame == gpGlobals.FrameCount;

		foreach (ParticleEffectList part in ParticleEffects) {
			for (int j = 0; j < part.ControlPoints.Count; ++j) {
				if (part.ControlPoints[j].Entity.Get() == entity) {
					part.ParticleEffect!.StopEmission(false, removeInstantly);
					part.ControlPoints.RemoveAt(j);
					break;
				}
			}
		}
	}

	public void StopParticlesNamed(string? effectName, bool forceRemoveInstantly = false, bool inverse = false) {
		if (string.IsNullOrEmpty(effectName))
			return;

		ParticleSystemDefinition? def = g_pParticleSystemMgr.FindParticleSystem(effectName);
		AssertMsg(def != null, $"Could not find particle definition {effectName}");
		if (def == null)
			return;

		bool removeInstantly = DormancyChangedAtFrame == gpGlobals.FrameCount;
		removeInstantly |= forceRemoveInstantly;

		foreach (ParticleEffectList part in ParticleEffects) {
			CNewParticleEffect effect = part.ParticleEffect!;
			bool matches = effect.Def == def;
			if (matches == !inverse)
				effect.StopEmission(false, removeInstantly);
		}
	}

	public void StopParticlesWithNameAndAttachment(string effectName, int attachmentPoint, bool forceRemoveInstantly = false) {
		ParticleSystemDefinition? def = g_pParticleSystemMgr.FindParticleSystem(effectName);
		AssertMsg(def != null, $"Could not find particle definition {effectName}");
		if (def == null)
			return;

		bool removeInstantly = DormancyChangedAtFrame == gpGlobals.FrameCount;
		removeInstantly |= forceRemoveInstantly;

		foreach (ParticleEffectList part in ParticleEffects) {
			CNewParticleEffect effect = part.ParticleEffect!;
			if (effect.Def != def)
				continue;

			foreach (ParticlePropertyControlPoint point in part.ControlPoints) {
				if (point.AttachmentPoint == attachmentPoint) {
					effect.StopEmission(false, removeInstantly);
					break;
				}
			}
		}
	}

	public void OnParticleSystemUpdated(CNewParticleEffect effect, float timeDelta) {
		int index = FindEffect(effect);
		Assert(index != -1);
		if (index == -1)
			return;

		UpdateParticleEffect(ParticleEffects[index]);
	}

	public void OnParticleSystemDeleted(CNewParticleEffect effect) {
		int index = FindEffect(effect);
		if (index == -1)
			return;

		ParticleEffects[index].MarkDeleted();
		ParticleEffects.RemoveAt(index);
	}

	public void OwnerSetDormantTo(bool dormant) {
		DormancyChangedAtFrame = gpGlobals.FrameCount;

		foreach (ParticleEffectList part in ParticleEffects)
			part.ParticleEffect!.SetDormant(dormant);
	}

	int FindEffect(CNewParticleEffect effect) {
		for (int i = 0; i < ParticleEffects.Count; i++) {
			if (ParticleEffects[i].ParticleEffect == effect)
				return i;
		}
		return -1;
	}

	public int FindEffect(ReadOnlySpan<char> effectName, int start = 0) {
		for (int i = start; i < ParticleEffects.Count; i++) {
			if (effectName.Equals(ParticleEffects[i].ParticleEffect!.GetName(), StringComparison.OrdinalIgnoreCase))
				return i;
		}
		return -1;
	}

	void UpdateParticleEffect(ParticleEffectList effect, bool initializing = false, int onlyThisControlPoint = -1) {
		if (onlyThisControlPoint != -1) {
			UpdateControlPoint(effect, onlyThisControlPoint, initializing);
			return;
		}

		for (int i = 0; i < effect.ControlPoints.Count; i++)
			UpdateControlPoint(effect, i, initializing);
	}

	void UpdateControlPoint(ParticleEffectList effectList, int pointIndex, bool initializing) {
		ParticlePropertyControlPoint point = effectList.ControlPoints[pointIndex];
		CNewParticleEffect effect = effectList.ParticleEffect!;

		if (effect.Def != null && effect.Def.IsScreenSpaceEffect() && pointIndex == 0) {
			effect.SetControlPointOrientation(point.ControlPoint, new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1));
			effect.SetControlPoint(point.ControlPoint, Vector3.Zero);
			return;
		}

		C_BaseEntity? entity = point.Entity.Get();
		if (entity == null) {
			if (point.AttachType == ParticleAttachment.WorldOrigin && initializing) {
				effect.SetControlPointOrientation(point.ControlPoint, new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1));
				effect.SetControlPoint(point.ControlPoint, point.OriginOffset);
				effect.SetSortOrigin(point.OriginOffset);
			}

			effect.SetControlPointEntity(point.ControlPoint, null);
			return;
		}

		if (!initializing && entity.GetMoveParent() == null && (point.AttachType == ParticleAttachment.AbsOrigin || point.AttachType == ParticleAttachment.Point))
			return;

		if (point.AttachType == ParticleAttachment.CustomOrigin)
			return;

		Vector3 origin = default, forward = default, right = default, up = default;

		switch (point.AttachType) {
			case ParticleAttachment.Point:
			case ParticleAttachment.PointFollow: {
					C_BaseAnimating? animating = entity.GetBaseAnimating();
					if (animating != null) {
						if (!animating.GetAttachment(point.AttachmentPoint, out Matrix3x4 attachmentToWorld)) {
							DevWarning($"Cannot update control point {point.AttachmentPoint} for effect '{effect.GetEffectName()}'.\n");
							attachmentToWorld = animating.RenderableToWorldTransform();
						}

						MathLib.MatrixVectors(attachmentToWorld, out forward, out right, out up);
						MathLib.MatrixGetColumn(attachmentToWorld, 3, out origin);

						if (effect.Def != null && effect.Def.IsViewModelEffect())
							C_BaseViewModel.FormatViewModelAttachment(ref origin, true);
					}
				}
				break;

			case ParticleAttachment.RootBoneFollow: {
					C_BaseAnimating? animating = entity.GetBaseAnimating();
					if (animating != null && animating.GetRootBone(out Matrix3x4 rootBone)) {
						MathLib.MatrixVectors(rootBone, out forward, out right, out up);
						MathLib.MatrixGetColumn(rootBone, 3, out origin);
					}
				}
				break;

			default:
				origin = entity.GetAbsOrigin() + point.OriginOffset;
				entity.GetVectors(out forward, out right, out up);
				break;
		}

		effect.SetControlPointOrientation(point.ControlPoint, forward, right, up);
		effect.SetControlPointEntity(point.ControlPoint, entity);
		effect.SetControlPoint(point.ControlPoint, origin);
		effect.SetSortOrigin(origin);
	}

	public void DebugPrintEffects() {
		for (int i = 0; i < ParticleEffects.Count; ++i) {
			CNewParticleEffect? effect = ParticleEffects[i].ParticleEffect;
			if (effect == null)
				continue;

			Msg($"({i})  EffectName \"{effect.GetEffectName()}\"  Dormant? {(effect.Dormant ? "yes" : "no")}  Emission Stopped? {(effect.EmissionStopped ? "yes" : "no")} \n");
		}
	}
}
