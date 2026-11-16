global using static Game.Server.TempEnts;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

public static partial class TempEnts
{
	static readonly TempEntsSystem g_TESystem = new();
	public static ITempEntsSystem te = g_TESystem;
}

public class TempEntsSystem : ITempEntsSystem
{
	public bool SuppressTE<IRF>(scoped ref IRF filter) where IRF : IRecipientFilter {
		if (GetSuppressHost() != null) {
			RecipientFilter _filter = (RecipientFilter)(object)filter;

			if (!_filter.IgnorePredictionCull()) 
				_filter.RemoveRecipient((BasePlayer)GetSuppressHost()!);
			

			if (0 == _filter.GetRecipientCount()) {
				// Suppress it
				return true;
			}
		}

		// There's at least one recipient
		return false;
	}

	public override void ArmorRicochet<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, in Vector3 dir) {
		if (!SuppressTE(ref filer))
			TE_ArmorRicochet(ref filer, delay, pos, dir);
	}

	public override void BeamEntPoint<IRF>(scoped ref IRF filer, float delay, int nStartEntity, in Vector3 start, int nEndEntity, in Vector3 end, int modelindex, int haloindex, int startframe, int framerate, float life, float width, float endWidth, int fadeLength, float amplitude, int r, int g, int b, int a, int speed) {
		if (!SuppressTE(ref filer))
			TE_BeamEntPoint(ref filer, delay, nStartEntity, start, nEndEntity, end, modelindex, haloindex, startframe, framerate, life, width, endWidth, fadeLength, amplitude, r, g, b, a, speed);
	}

	public override void BeamEnts<IRF>(scoped ref IRF filer, float delay, int start, int end, int modelindex, int haloindex, int startframe, int framerate, float life, float width, float endWidth, int fadeLength, float amplitude, int r, int g, int b, int a, int speed) {
		if (!SuppressTE(ref filer))
			TE_BeamEnts(ref filer, delay, start, end, modelindex, haloindex, startframe, framerate, life, width, endWidth, fadeLength, amplitude, r, g, b, a, speed);
	}

	public override void BeamFollow<IRF>(scoped ref IRecipientFilter filter, float delay, int iEntIndex, int modelIndex, int haloIndex, float life, float width, float endWidth, float fadeLength, float r, float g, float b, float a) {
		if (!SuppressTE(ref filter))
			TE_BeamFollow(ref filter, delay, iEntIndex, modelIndex, haloIndex, life, width, endWidth, fadeLength, r, g, b, a);
	}

	public override void BeamLaser<IRF>(scoped ref IRF filer, float delay, int start, int end, int modelindex, int haloindex, int startframe, int framerate, float life, float width, float endWidth, int fadeLength, float amplitude, int r, int g, int b, int a, int speed) {
		if (!SuppressTE(ref filer))
			TE_BeamLaser(ref filer, delay, start, end, modelindex, haloindex, startframe, framerate, life, width, endWidth, fadeLength, amplitude, r, g, b, a, speed);
	}

	public override void BeamPoints<IRF>(scoped ref IRF filer, float delay, in Vector3 start, in Vector3 end, int modelindex, int haloindex, int startframe, int framerate, float life, float width, float endWidth, int fadeLength, float amplitude, int r, int g, int b, int a, int speed) {
		if (!SuppressTE(ref filer))
			TE_BeamPoints(ref filer, delay, start, end, modelindex, haloindex, startframe, framerate, life, width, endWidth, fadeLength, amplitude, r, g, b, a, speed);
	}

	public override void BeamRing<IRF>(scoped ref IRF filer, float delay, int start, int end, int modelindex, int haloindex, int startframe, int framerate, float life, float width, int spread, float amplitude, int r, int g, int b, int a, int speed, int flags = 0) {
		if (!SuppressTE(ref filer))
			TE_BeamRing(ref filer, delay, start, end, modelindex, haloindex, startframe, framerate, life, width, spread, amplitude, r, g, b, a, speed, flags);
	}

	public override void BeamRingPoint<IRF>(scoped ref IRF filer, float delay, in Vector3 center, float start_radius, float end_radius, int modelindex, int haloindex, int startframe, int framerate, float life, float width, int spread, float amplitude, int r, int g, int b, int a, int speed, int flags = 0) {
		if (!SuppressTE(ref filer))
			TE_BeamRingPoint(ref filer, delay, center, start_radius, end_radius, modelindex, haloindex, startframe, framerate, life, width, spread, amplitude, r, g, b, a, speed, flags);
	}

	public override void BeamSpline<IRF>(scoped ref IRF filer, float delay, int points, Span<Vector3> rgPoints) {
		if (!SuppressTE(ref filer))
			TE_BeamSpline(ref filer, delay, points, rgPoints);
	}

	public override void BloodSprite<IRF>(scoped ref IRF filer, float delay, in Vector3 org, in Vector3 dir, int r, int g, int b, int a, int size) {
		if (!SuppressTE(ref filer))
			TE_BloodSprite(ref filer, delay, org, dir, r, g, b, a, size);
	}

	public override void BloodStream<IRF>(scoped ref IRF filer, float delay, in Vector3 org, in Vector3 dir, int r, int g, int b, int a, int amount) {
		if (!SuppressTE(ref filer))
			TE_BloodStream(ref filer, delay, org, dir, r, g, b, a, amount);
	}

	public override void BreakModel<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, in QAngle angle, in Vector3 size, in Vector3 vel, int modelindex, int randomization, int count, float time, int flags) {
		if (!SuppressTE(ref filer))
			TE_BreakModel(ref filer, delay, pos, angle, size, vel, modelindex, randomization, count, time, flags);
	}

	public override void BSPDecal<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, int entity, int index) {
		if (!SuppressTE(ref filer))
			TE_BSPDecal(ref filer, delay, pos, entity, index);
	}

	public override void Bubbles<IRF>(scoped ref IRF filer, float delay, in Vector3 mins, in Vector3 maxs, float height, int modelindex, int count, float speed) {
		if (!SuppressTE(ref filer))
			TE_Bubbles(ref filer, delay, mins, maxs, height, modelindex, count, speed);
	}

	public override void BubbleTrail<IRF>(scoped ref IRF filer, float delay, in Vector3 mins, in Vector3 maxs, float height, int modelindex, int count, float speed) {
		if (!SuppressTE(ref filer))
			TE_BubbleTrail(ref filer, delay, mins, maxs, height, modelindex, count, speed);
	}

	public override void ClientProjectile<IRF>(scoped ref IRF filter, float delay, in Vector3 vecOrigin, in Vector3 vecVelocity, int modelindex, int lifetime, BaseEntity? owner) {
		if (!SuppressTE(ref filter))
			TE_ClientProjectile(ref filter, delay, vecOrigin, vecVelocity, modelindex, lifetime, owner);
	}

	public override void Decal<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, in Vector3 start, int entity, int hitbox, int index) {
		if (!SuppressTE(ref filer))
			TE_Decal(ref filer, delay, pos, start, entity, hitbox, index);
	}

	public override void DispatchEffect<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, ReadOnlySpan<char> name, EffectData data) {
		if (!SuppressTE(ref filter))
			TE_DispatchEffect(ref filter, delay, pos, name, data);
	}

	public override void Dust<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, in Vector3 dir, float size, float speed) {
		if (!SuppressTE(ref filer))
			TE_Dust(ref filer, delay, pos, dir, size, speed);
	}

	public override void DynamicLight<IRF>(scoped ref IRF filer, float delay, in Vector3 org, int r, int g, int b, int exponent, float radius, float time, float decay) {
		if (!SuppressTE(ref filer))
			TE_DynamicLight(ref filer, delay, org, r, g, b, exponent, radius, time, decay);
	}

	public override void EnergySplash<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, in Vector3 dir, bool bExplosive) {
		if (!SuppressTE(ref filer))
			TE_EnergySplash(ref filer, delay, pos, dir, bExplosive);
	}

	public override void Explosion<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, int modelindex, float scale, int framerate, int flags, int radius, int magnitude, Vector3? normal = null, byte materialType = 67) {
		if (!SuppressTE(ref filer))
			TE_Explosion(ref filer, delay, pos, modelindex, scale, framerate, flags, radius, magnitude, normal, materialType);
	}

	public override void Fizz<IRF>(scoped ref IRF filer, float delay, BaseEntity? ed, int modelindex, int density, int current) {
		if (!SuppressTE(ref filer))
			TE_Fizz(ref filer, delay, ed, modelindex, density, current);
	}

	public override void FootprintDecal<IRF>(scoped ref IRF filer, float delay, in Vector3 origin, in Vector3 right, int entity, int index, byte materialType) {
		if (!SuppressTE(ref filer))
			TE_FootprintDecal(ref filer, delay, origin, right, entity, index, materialType);
	}

	public override void GaussExplosion<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, in Vector3 dir, int type) {
#if HL2_DLL
		if (!SuppressTE(ref filer))
			TE_GaussExplosion(ref filer, delay, pos, dir, type);
#endif
	}

	public override void GlowSprite<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, int modelindex, float life, float size, int brightness) {
		if (!SuppressTE(ref filer))
			TE_GlowSprite(ref filer, delay, pos, modelindex, life, size, brightness);
	}

	public override void KillPlayerAttachments<IRF>(scoped ref IRF filer, float delay, int player) {
		if (!SuppressTE(ref filer))
			TE_KillPlayerAttachments(ref filer, delay, player);
	}

	public override void LargeFunnel<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, int modelindex, int reversed) {
		if (!SuppressTE(ref filer))
			TE_LargeFunnel(ref filer, delay, pos, modelindex, reversed);
	}

	public override void MetalSparks<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, in Vector3 dir) {
		if (!SuppressTE(ref filer))
			TE_MetalSparks(ref filer, delay, pos, dir);
	}

	public override void MuzzleFlash<IRF>(scoped ref IRF filer, float delay, in Vector3 start, in QAngle angles, float scale, int type) {
		if (!SuppressTE(ref filer))
			TE_MuzzleFlash(ref filer, delay, start, angles, scale, type);
	}

	public override void PhysicsProp<IRF>(scoped ref IRF filter, float delay, int modelindex, int skin, in Vector3 pos, in QAngle angles, in Vector3 vel, int flags, int effects, Color clrRender, float modelScale) {
		if (!SuppressTE(ref filter))
			TE_PhysicsProp(ref filter, delay, modelindex, skin, pos, angles, vel, flags, effects, clrRender, modelScale);
	}

	public override void PlayerDecal<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, int player, int entity) {
		if (!SuppressTE(ref filer))
			TE_PlayerDecal(ref filer, delay, pos, player, entity);
	}

	public override void ProjectDecal<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, in QAngle angles, float distance, int index) {
		if (!SuppressTE(ref filter))
			TE_ProjectDecal(ref filter, delay, pos, angles, distance, index);
	}

	public override void ShatterSurface<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, in QAngle angle, in Vector3 force, in Vector3 forcePos, float width, float height, float shardsize, ShatterSurface surfacetype, int front_r, int front_g, int front_b, int back_r, int back_g, int back_b) {
		if (!SuppressTE(ref filer))
			TE_ShatterSurface(ref filer, delay, pos, angle, force, forcePos, width, height, shardsize, surfacetype, front_r, front_g, front_b, back_r, back_g, back_b);
	}

	public override void ShowLine<IRF>(scoped ref IRF filer, float delay, in Vector3 start, in Vector3 end) {
		if (!SuppressTE(ref filer))
			TE_ShowLine(ref filer, delay, start, end);
	}

	public override void Smoke<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, int modelindex, float scale, int framerate) {
		if (!SuppressTE(ref filer))
			TE_Smoke(ref filer, delay, pos, modelindex, scale, framerate);
	}

	public override void Sparks<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, int nMagnitude, int nTrailLength, in Vector3 pDir) {
		if (!SuppressTE(ref filer))
			TE_Sparks(ref filer, delay, pos, nMagnitude, nTrailLength, pDir);
	}

	public override void Sprite<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, int modelindex, float size, int brightness) {
		if (!SuppressTE(ref filer))
			TE_Sprite(ref filer, delay, pos, modelindex, size, brightness);
	}

	public override void SpriteSpray<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, in Vector3 dir, int modelindex, int speed, float noise, int count) {
		if (!SuppressTE(ref filer))
			TE_SpriteSpray(ref filer, delay, pos, dir, modelindex, speed, noise, count);
	}

	public override void TriggerTempEntity(KeyValues pKeyValues) {
		Assert(false);
	}

	public override void WorldDecal<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, int index) {
		if (!SuppressTE(ref filer))
			TE_WorldDecal(ref filer, delay, pos, index);
	}
}
