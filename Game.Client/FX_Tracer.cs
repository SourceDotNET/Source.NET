using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Engine;
using Source.Common.Mathematics;
using Source.Common.SoundEmitterSystem;

using System.Numerics;

namespace Game.Client;

public static class FX_Tracer
{
	static float NextWhizTime;

	public static Vector3 GetTracerOrigin(EffectData data) {
		Vector3 vecStart = data.Start;
		int attachment = data.AttachmentIndex;

		if ((data.Flags & TRACER_FLAG_USEATTACHMENT) == 0)
			return vecStart;

		IClientRenderable? renderable = cl_entitylist.GetClientRenderableFromHandle(data.Entity);
		if (renderable == null)
			return vecStart;

		C_BaseEntity? ent = C_BaseEntity.Instance(data.Entity);
		if (ent != null && ent.IsDormant())
			return vecStart;

		if (ent is C_BaseCombatWeapon wpn) {
			if (wpn.GetTracerOrigin(ref vecStart))
				return vecStart;

			C_BasePlayer? player = ToBasePlayer(wpn.GetOwner());
			C_BaseViewModel? viewModel = player?.GetViewModel(0);
			if (viewModel != null && wpn.ShouldDrawUsingViewModel())
				renderable = viewModel;
		}

		if (renderable.GetAttachment(attachment, out Vector3 attachOrigin, out _))
			vecStart = attachOrigin;

		return vecStart;
	}

	public static void FX_TracerSound(in Vector3 start, in Vector3 end, int tracerType, ReadOnlySpan<char> customSoundName = default) {
		Vector3 vecListenOrigin = MainViewOrigin();
		ReadOnlySpan<char> soundName;
		float whizDistSqr;
		float minWhizTime = 0.1f;
		float maxWhizTime = 0.1f;

		switch (tracerType) {
			case TRACER_TYPE_DEFAULT: {
					soundName = "Bullets.DefaultNearmiss";
					whizDistSqr = 24.0f * 24.0f;

					Ray bullet = default;
					bullet.Init(start, end);

					Vector3 vecLower = vecListenOrigin;
					vecLower.Z -= 24.0f;
					Ray listener = default;
					listener.Init(vecListenOrigin, vecLower);

					CollisionUtils.IntersectRayWithRay(bullet, listener, out _, out float t);
					if (t <= 0.0f)
						t = 0.0f;
					if (1.0f <= t)
						t = 1.0f;
					vecListenOrigin.Z -= t * 24.0f;
				}
				break;
			case TRACER_TYPE_GUNSHIP:
				soundName = "Bullets.GunshipNearmiss";
				whizDistSqr = 72.0f * 72.0f;
				break;
			case TRACER_TYPE_STRIDER:
				soundName = "Bullets.StriderNearmiss";
				whizDistSqr = 72.0f * 72.0f;
				break;
			case TRACER_TYPE_WATERBULLET:
				soundName = "Underwater.BulletImpact";
				whizDistSqr = 48.0f * 48.0f;
				minWhizTime = 0.3f;
				maxWhizTime = 0.6f;
				break;
			default:
				return;
		}

		if (!customSoundName.IsEmpty)
			soundName = customSoundName;

		if ((float)(NextWhizTime - gpGlobals.CurTime) > 0.0f)
			return;

		if (MathLib.CalcDistanceSqrToLineSegment(vecListenOrigin, start, end) >= whizDistSqr)
			return;

		SoundParameters parms = new();
		if (C_BaseEntity.GetParametersForSound(soundName, ref parms, null)) {
			Vector3 shotDir = end - start;
			MathLib.VectorNormalize(ref shotDir);

			LocalPlayerFilter filter = new();
			enginesound.EmitSound(filter, EngineSoundGlobals.SOUND_FROM_WORLD, (int)SoundEntityChannel.Static, ((Span<char>)parms.SoundName).SliceNullTerminatedString(),
				parms.Volume, AttenuationValues.SNDLVL_TO_ATTN(parms.SoundLevel), 0, parms.Pitch, 0, start, shotDir, null, true);
		}

		NextWhizTime = (float)(gpGlobals.CurTime + random.RandomFloat(minWhizTime, maxWhizTime));
	}

	static void ParticleTracerCallback(EffectData data) {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		Vector3 vecStart = GetTracerOrigin(data);
		Vector3 vecEnd = data.Origin;

		C_BaseEntity? entity = C_BaseEntity.Instance(data.Entity);
		int entIndex = entity != null ? entity.EntIndex() : -1;
		if (entIndex != 0 && entIndex == player.EntIndex()) {
			engine.GetViewAngles(out QAngle vangles);
			MathLib.AngleVectors(vangles, out _, out Vector3 vright, out _);

			MathLib.VectorMA(data.Start, 4, vright, out vecStart);
			vecStart.Z -= 0.5f;
		}

		Vector3 vecToEnd = vecEnd - vecStart;
		MathLib.VectorNormalize(ref vecToEnd);
		MathLib.VectorAngles(vecToEnd, out QAngle vecAngles);
		DispatchParticleEffect(data.HitBox, vecStart, vecEnd, vecAngles, entity);

		if ((data.Flags & TRACER_FLAG_WHIZ) != 0)
			FX_TracerSound(vecStart, vecEnd, TRACER_TYPE_DEFAULT);
	}

	static readonly ClientEffectRegistration ClientEffectReg_ParticleTracerCallback = new("ParticleTracer", ParticleTracerCallback);
}
