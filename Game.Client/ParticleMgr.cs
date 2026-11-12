using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.MaterialSystem;

using System.Numerics;

namespace Game.Client;

public struct ParticleLightInfo
{
	[NetworkVar("m_vPos")]
	public Vector3 Pos;
	[NetworkVar("m_vColor")]
	public Vector3 Color;
	[NetworkVar("m_flIntensity")]
	public float Intensity;
}

public struct ParticleSimListEntry
{
	public CNewParticleEffect NewParticleEffect;
	public bool BoundingBoxOnly;
}

public sealed class ParticleMgr
{
	public const int MAX_TOTAL_PARTICLES = 16384;

	public static readonly ParticleMgr Instance = new();

	public static readonly ConVar r_DrawParticles = new("r_drawparticles", "1", FCvar.Cheat, "Enable/disable particle rendering");
	public static readonly ConVar cl_particleeffect_aabb_buffer = new("cl_particleeffect_aabb_buffer", "2", FCvar.Cheat, "Add this amount to a particle effect's bbox in the leaf system so if it's growing slowly, it won't have to be reinserted as often.");
	static readonly ConVar r_particle_timescale = new("r_particle_timescale", "1.0", FCvar.Cheat | FCvar.DevelopmentOnly);

	CNewParticleEffect? NewEffectsHead;
	IMaterialSystem? MaterialSystem;
	bool UpdatingEffects;
	bool RenderParticleSystems_ = true;

	static float ThreadedPSystemTimeStep;

	public bool Init(int count, IMaterialSystem materials) {
		Term();

		MaterialSystem = materials;

		g_pParticleSystemMgr.Init(ParticleSystemQuery.g_pParticleSystemQuery, filesystem);

		ParseParticleEffects(true, false);

		return true;
	}

	public void Term() {
		NewEffectsHead = null;

		g_pParticleSystemMgr.UncacheAllParticleSystems();
		MaterialSystem?.UncacheUnusedMaterials();
		MaterialSystem = null;
	}

	public void LevelInit() => g_pParticleSystemMgr.SetLastSimulationTime(gpGlobals.CurTime);

	public void RenderParticleSystems(bool enable) => RenderParticleSystems_ = enable;
	public bool ShouldRenderParticleSystems() => RenderParticleSystems_;

	public void AddEffect(CNewParticleEffect effect) {
		effect.PrevEffect = null;
		effect.NextEffect = NewEffectsHead;
		if (NewEffectsHead != null)
			NewEffectsHead.PrevEffect = effect;
		NewEffectsHead = effect;

		clientLeafSystem.CreateRenderableHandle(effect);
		if (effect.IsValid() && effect.Def!.IsViewModelEffect())
			clientLeafSystem.SetRenderGroup(effect.RenderHandle(), RenderGroup.ViewModelTranslucent);
	}

	public void RemoveEffect(CNewParticleEffect effect) {
		Assert(!UpdatingEffects);

		clientLeafSystem.RemoveRenderable(effect.RenderHandle());

		if (effect.PrevEffect != null)
			effect.PrevEffect.NextEffect = effect.NextEffect;
		else if (NewEffectsHead == effect)
			NewEffectsHead = effect.NextEffect;
		if (effect.NextEffect != null)
			effect.NextEffect.PrevEffect = effect.PrevEffect;
		effect.NextEffect = null;
		effect.PrevEffect = null;

		effect.NotifyRemove();
	}

	public void RemoveAllNewEffects() {
		for (CNewParticleEffect? newEffect = NewEffectsHead; newEffect != null;) {
			CNewParticleEffect? nextEffect = newEffect.NextEffect;
			for (C_BaseEntity? ent = cl_entitylist.FirstBaseEntity(); ent != null; ent = cl_entitylist.NextBaseEntity(ent))
				ent.ParticleProp().OnParticleSystemDeleted(newEffect);

			RemoveEffect(newEffect);
			newEffect = nextEffect;
		}
	}

	public void RemoveAllEffects() => RemoveAllNewEffects();

	public void Simulate(float timeDelta) {
		if (MaterialSystem == null) {
			Assert(false);
			return;
		}

		UpdateAllEffects(timeDelta);
	}

	static void ProcessPSystem(in ParticleSimListEntry entry) {
		CNewParticleEffect newEffect = entry.NewParticleEffect;
		bool updateBboxOnly = entry.BoundingBoxOnly;

		if (newEffect.GetNeedsBBoxUpdate()) {
			if (!newEffect.GetAutoUpdateBBox() || newEffect.RecalculateBoundingBox())
				newEffect.SetNeedsBBoxUpdate(false);
		}

		newEffect.SetDrawn(false);

		if (newEffect.GetFirstFrameFlag()) {
			newEffect.Simulate(0.0f, updateBboxOnly);
			newEffect.SetFirstFrameFlag(false);
		}
		else if (newEffect.ShouldSimulate())
			newEffect.Simulate(ThreadedPSystemTimeStep, updateBboxOnly);

		if (newEffect.IsFinished())
			newEffect.SetRemoveFlag();
	}

	void BuildParticleSimList(List<ParticleSimListEntry> list) {
		double now = g_pParticleSystemMgr.GetLastSimulationTime();
		for (CNewParticleEffect? newEffect = NewEffectsHead; newEffect != null; newEffect = newEffect.NextEffect) {
			bool skip = false;
			bool needsBboxUpdate = false;

			if (now >= newEffect.NextSleepTime && newEffect.ActiveParticles > 0)
				skip = true;
			if (newEffect.GetRemoveFlag())
				skip = true;

			if (skip) {
				needsBboxUpdate = newEffect.HasMoved();
				skip = !needsBboxUpdate;
			}

			if (!skip)
				list.Add(new() { NewParticleEffect = newEffect, BoundingBoxOnly = needsBboxUpdate });
		}
	}

	readonly List<ParticleSimListEntry> ParticlesToSimulate = [];

	void UpdateNewEffects(float timeDelta) {
		timeDelta *= r_particle_timescale.GetFloat();

		g_pParticleSystemMgr.SetLastSimulationTime(gpGlobals.CurTime);

		ThreadedPSystemTimeStep = timeDelta;

		for (CNewParticleEffect? newEffect = NewEffectsHead; newEffect != null; newEffect = newEffect.NextEffect)
			newEffect.Update(ThreadedPSystemTimeStep);

		ParticlesToSimulate.Clear();
		BuildParticleSimList(ParticlesToSimulate);
		int count = ParticlesToSimulate.Count;

		if (count != 0) {
			DirtySpatialPartitionEntityList.UpdateDirtySpatialPartitionEntities();
			for (int i = 0; i < count; i++)
				ProcessPSystem(ParticlesToSimulate[i]);
		}

		for (int i = 0; i < count; i++)
			ParticlesToSimulate[i].NewParticleEffect.DetectChanges();
	}

	void UpdateAllEffects(float timeDelta) {
		UpdatingEffects = true;

		if (timeDelta > 0.1f)
			timeDelta = 0.1f;

		UpdateNewEffects(timeDelta);

		UpdatingEffects = false;

		for (CNewParticleEffect? newEffect = NewEffectsHead; newEffect != null;) {
			CNewParticleEffect? nextEffect = newEffect.NextEffect;
			if (newEffect.GetRemoveFlag())
				RemoveEffect(newEffect);
			newEffect = nextEffect;
		}
	}
}
