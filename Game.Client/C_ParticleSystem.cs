using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Client;

[NetworkName("CParticleSystem", NoBase = true)]
public partial class C_ParticleSystem : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Origin)) { Encoding = NetEncoding.Vector, Bits = 0, Flags = PropFlags.Coord | PropFlags.ChangesOften },
		new(nameof(OwnerEntity)) { Encoding = NetEncoding.EHandle },
		new(nameof(NetworkMoveParent)) { Encoding = NetEncoding.Int, Proxy = typeof(MoveParentProxy) },
		new(nameof(ParentAttachment)) { Encoding = NetEncoding.Int, Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Rotation)) { Encoding = NetEncoding.Vector, Bits = 13, Flags = PropFlags.RoundDown | PropFlags.ChangesOften },
		new(nameof(EffectIndex)) { Bits = 12, Flags = PropFlags.Unsigned },
		nameof(Active),
		new(nameof(StartTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		nameof(ControlPointEnts),
		new(nameof(ControlPointParents)) { Bits = 3, Flags = PropFlags.Unsigned },
		nameof(WeatherEffect),
	];

	[NetworkVar("m_iEffectIndex")]
	public int EffectIndex;
	[NetworkVar("m_bActive")]
	public bool Active;
	[NetworkVar("m_flStartTime")]
	public TimeUnit_t StartTime;
	[NetworkVar("m_hControlPointEnts")]
	public InlineArrayNewMaxControlPoints<EHANDLE> ControlPointEnts = new();

	[NetworkVar("m_iControlPointParents")]
	public InlineArrayNewMaxControlPoints<byte> ControlPointParents = new();
	[NetworkVar("m_bWeatherEffect")]
	public bool WeatherEffect;

	bool OldActive;

	public override void PreDataUpdate(DataUpdateType updateType) {
		OldActive = Active;

		base.PreDataUpdate(updateType);
	}

	public override void PostDataUpdate(DataUpdateType updateType) {
		base.PostDataUpdate(updateType);

		if (updateType == DataUpdateType.Created) {
			if (Active)
				SetNextClientThink(gpGlobals.CurTime);
		}
		else if (OldActive != Active) {
			if (Active)
				SetNextClientThink(gpGlobals.CurTime);
			else
				ParticleProp().StopEmission();
		}
	}

	public override void ClientThink() {
		if (!Active)
			return;

		ReadOnlySpan<char> name = HLClient.GetParticleSystemNameFromIndex(EffectIndex);
		if (name.IsEmpty)
			return;

		if (!GameRules.g_pGameRules!.AllowMapParticleEffect(name))
			return;

		if (WeatherEffect && !GameRules.g_pGameRules.AllowWeatherParticles())
			return;

		CNewParticleEffect? effect = ParticleProp().Create(name.ToString(), ParticleAttachment.AbsOriginFollow);
		AssertMsg(effect != null, $"Particle system couldn't make {name}");
		if (effect == null)
			return;

		for (int i = 0; i < InlineArrayNewMaxControlPoints<byte>.kMAXCONTROLPOINTS; ++i) {
			C_BaseEntity? onEntity = ControlPointEnts[i].Get();
			if (onEntity != null)
				ParticleProp().AddControlPoint(effect, i + 1, onEntity, ParticleAttachment.AbsOriginFollow);

			AssertMsg(ControlPointParents[i] <= InlineArrayNewMaxControlPoints<byte>.kMAXCONTROLPOINTS, $"Particle system specified bogus control point parent ({ControlPointParents[i]}) for point {i}.");

			if (ControlPointParents[i] != 0)
				effect.SetControlPointParent(i + 1, ControlPointParents[i]);
		}

		ParticleProp().OnParticleSystemUpdated(effect, 0.0f);

		float timeDelta = (float)(gpGlobals.CurTime - (float)StartTime);
		if (timeDelta > 0.01f)
			effect.SkipToTime(timeDelta);
	}

	static void ParticleEffectCallback(EffectData data) {
		if (EffectData.SuppressingParticleEffects())
			return;

		string name = HLClient.GetParticleSystemNameFromIndex(data.HitBox).ToString();

		CNewParticleEffect? effect;
		if ((data.Flags & PARTICLE_DISPATCH_FROM_ENTITY) != 0) {
			if (!data.Entity.IsValid())
				return;
			C_BaseEntity? ent = Instance(data.Entity);
			if (ent == null || ent.IsDormant())
				return;

			if ((data.Flags & PARTICLE_DISPATCH_RESET_PARTICLES) != 0)
				ent.ParticleProp().StopEmission();

			Vector3 offset = Vector3.Zero;
			ParticleAttachment attachType = (ParticleAttachment)data.DamageType;
			if (attachType == ParticleAttachment.AbsOriginFollow || attachType == ParticleAttachment.PointFollow || attachType == ParticleAttachment.RootBoneFollow)
				offset = data.Start;

			effect = ent.ParticleProp().Create(name, attachType, data.AttachmentIndex, offset);
			if (effect == null)
				return;

			effect.AddRef();
			if (effect.IsValid() && attachType == ParticleAttachment.CustomOrigin)
				SetupCustomOrigin(effect, data);
		}
		else {
			if (GameRules.g_pGameRules != null)
				name = GameRules.g_pGameRules.TranslateEffectForVisionFilter("particles", name).ToString();

			effect = CNewParticleEffect.Create(null, name);
			effect.AddRef();
			if (effect.IsValid())
				SetupCustomOrigin(effect, data);
		}

		if (effect.IsValid()) {
			if (data.HasCustomColors) {
				effect.SetControlPoint(EffectData.CUSTOM_COLOR_CP1, data.CustomColors.Color1);
				effect.SetControlPoint(EffectData.CUSTOM_COLOR_CP2, data.CustomColors.Color2);
			}

			if (data.HasControlPoint1)
				effect.SetControlPoint(1, data.ControlPoint1.Offset);
		}

		effect.Release();
	}

	static void SetupCustomOrigin(CNewParticleEffect effect, EffectData data) {
		effect.SetSortOrigin(data.Origin);
		effect.SetControlPoint(0, data.Origin);
		effect.SetControlPoint(1, data.Start);
		MathLib.AngleVectors(data.Angles, out Vector3 forward, out Vector3 right, out Vector3 up);
		effect.SetControlPointOrientation(0, forward, right, up);
	}

	static readonly ClientEffectRegistration ClientEffectReg_ParticleEffectCallback = new("ParticleEffect", ParticleEffectCallback);

	static void ParticleEffectStopCallback(EffectData data) {
		if (!data.Entity.IsValid())
			return;
		C_BaseEntity? ent = Instance(data.Entity);
		ent?.ParticleProp().StopEmission();
	}

	static readonly ClientEffectRegistration ClientEffectReg_ParticleEffectStopCallback = new("ParticleEffectStop", ParticleEffectStopCallback);
}
