#if CLIENT_DLL || GAME_DLL
using Source;
using Source.Common;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Game.Shared;

public abstract class ITempEntsSystem : IPredictionSystem
{
	public abstract void ArmorRicochet<IRF>(scoped ref IRF filer, float delay, in Vector3 pos, in Vector3 dir) where IRF : IRecipientFilter;
	public abstract void BeamEntPoint<IRF>(scoped ref IRF filer, float delay,
		int nStartEntity, in Vector3 start, int nEndEntity, in Vector3 end,
		int modelindex, int haloindex, int startframe, int framerate,
		float life, float width, float endWidth, int fadeLength, float amplitude,
		int r, int g, int b, int a, int speed) where IRF : IRecipientFilter;
	public abstract void BeamEnts<IRF>(scoped ref IRF filer, float delay,
		int start, int end, int modelindex, int haloindex, int startframe, int framerate,
		float life, float width, float endWidth, int fadeLength, float amplitude,
		int r, int g, int b, int a, int speed) where IRF : IRecipientFilter;
	public abstract void BeamFollow<IRF>(scoped ref IRecipientFilter filter, float delay,
		int iEntIndex, int modelIndex, int haloIndex, float life, float width, float endWidth,
		float fadeLength, float r, float g, float b, float a) where IRF : IRecipientFilter;
	public abstract void BeamPoints<IRF>(scoped ref IRF filer, float delay,
		in Vector3 start, in Vector3 end, int modelindex, int haloindex, int startframe, int framerate,
		float life, float width, float endWidth, int fadeLength, float amplitude,
		int r, int g, int b, int a, int speed) where IRF : IRecipientFilter;
	public abstract void BeamLaser<IRF>(scoped ref IRF filer, float delay,
		int start, int end, int modelindex, int haloindex, int startframe, int framerate,
		float life, float width, float endWidth, int fadeLength, float amplitude, int r, int g, int b, int a, int speed) where IRF : IRecipientFilter;
	public abstract void BeamRing<IRF>(scoped ref IRF filer, float delay,
		int start, int end, int modelindex, int haloindex, int startframe, int framerate,
		float life, float width, int spread, float amplitude, int r, int g, int b, int a, int speed, int flags = 0) where IRF : IRecipientFilter;
	public abstract void BeamRingPoint<IRF>(scoped ref IRF filer, float delay,
		in Vector3 center, float start_radius, float end_radius, int modelindex, int haloindex, int startframe, int framerate,
		float life, float width, int spread, float amplitude, int r, int g, int b, int a, int speed, int flags = 0) where IRF : IRecipientFilter;
	public abstract void BeamSpline<IRF>(scoped ref IRF filer, float delay,
		int points, Span<Vector3> rgPoints) where IRF : IRecipientFilter;
	public abstract void BloodStream<IRF>(scoped ref IRF filer, float delay,
		in Vector3 org, in Vector3 dir, int r, int g, int b, int a, int amount) where IRF : IRecipientFilter;
	public abstract void BloodSprite<IRF>(scoped ref IRF filer, float delay,
		in Vector3 org, in Vector3 dir, int r, int g, int b, int a, int size) where IRF : IRecipientFilter;
	public abstract void BreakModel<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, in QAngle angle, in Vector3 size, in Vector3 vel,
		int modelindex, int randomization, int count, float time, int flags) where IRF : IRecipientFilter;
	public abstract void BSPDecal<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, int entity, int index) where IRF : IRecipientFilter;
	public abstract void ProjectDecal<IRF>(scoped ref IRF filter, float delay,
		in Vector3 pos, in QAngle angles, float distance, int index ) where IRF : IRecipientFilter;
	public abstract void Bubbles<IRF>(scoped ref IRF filer, float delay,
		in Vector3 mins, in Vector3 maxs, float height, int modelindex, int count, float speed) where IRF : IRecipientFilter;
	public abstract void BubbleTrail<IRF>(scoped ref IRF filer, float delay,
		in Vector3 mins, in Vector3 maxs, float height, int modelindex, int count, float speed) where IRF : IRecipientFilter;
	public abstract void Decal<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, in Vector3 start, int entity, int hitbox, int index) where IRF : IRecipientFilter;
	public abstract void DynamicLight<IRF>(scoped ref IRF filer, float delay,
		in Vector3 org, int r, int g, int b, int exponent, float radius, float time, float decay) where IRF : IRecipientFilter;
	public abstract void Explosion<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, int modelindex, float scale, int framerate, int flags, int radius, int magnitude, Vector3? normal = null, byte materialType = (byte)'C') where IRF : IRecipientFilter;
	public abstract void ShatterSurface<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, in QAngle angle, in Vector3 force, in Vector3 forcePos,
		float width, float height, float shardsize, ShatterSurface surfacetype,
		int front_r, int front_g, int front_b, int back_r, int back_g, int back_b) where IRF : IRecipientFilter;
	public abstract void GlowSprite<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, int modelindex, float life, float size, int brightness) where IRF : IRecipientFilter;
	public abstract void FootprintDecal<IRF>(scoped ref IRF filer, float delay, in Vector3 origin, in Vector3 right,
		int entity, int index, byte materialType) where IRF : IRecipientFilter;
	public abstract void Fizz<IRF>(scoped ref IRF filer, float delay,
		BaseEntity? ed, int modelindex, int density, int current ) where IRF : IRecipientFilter;
	public abstract void KillPlayerAttachments<IRF>(scoped ref IRF filer, float delay,
		int player) where IRF : IRecipientFilter;
	public abstract void LargeFunnel<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, int modelindex, int reversed) where IRF : IRecipientFilter;
	public abstract void MetalSparks<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, in Vector3 dir) where IRF : IRecipientFilter;
	public abstract void EnergySplash<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, in Vector3 dir, bool bExplosive) where IRF : IRecipientFilter;
	public abstract void PlayerDecal<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, int player, int entity) where IRF : IRecipientFilter;
	public abstract void ShowLine<IRF>(scoped ref IRF filer, float delay,
		in Vector3 start, in Vector3 end) where IRF : IRecipientFilter;
	public abstract void Smoke<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, int modelindex, float scale, int framerate) where IRF : IRecipientFilter;
	public abstract void Sparks<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, int nMagnitude, int nTrailLength, in Vector3 pDir) where IRF : IRecipientFilter;
	public abstract void Sprite<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, int modelindex, float size, int brightness) where IRF : IRecipientFilter;
	public abstract void SpriteSpray<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, in Vector3 dir, int modelindex, int speed, float noise, int count) where IRF : IRecipientFilter;
	public abstract void WorldDecal<IRF>(scoped ref IRF filer, float delay,
		in Vector3 pos, int index) where IRF : IRecipientFilter;
	public abstract void MuzzleFlash<IRF>(scoped ref IRF filer, float delay,
		in Vector3 start, in QAngle angles, float scale, int type) where IRF : IRecipientFilter;
	public abstract void Dust<IRF>(scoped ref IRF filer, float delay,
				 in Vector3 pos, in Vector3 dir, float size, float speed) where IRF : IRecipientFilter;
	public abstract void GaussExplosion<IRF>(scoped ref IRF filer, float delay,
				in Vector3 pos, in Vector3 dir, int type) where IRF : IRecipientFilter;
	public abstract void DispatchEffect<IRF>(scoped ref IRF filter, float delay,
				in Vector3 pos, ReadOnlySpan<char> name, EffectData data) where IRF : IRecipientFilter;
	public abstract void PhysicsProp<IRF>(scoped ref IRF filter, float delay, int modelindex, int skin,
		in Vector3 pos, in QAngle angles, in Vector3 vel, int flags, int effects, Color clrRender, float modelScale) where IRF : IRecipientFilter;

	// For playback from external tools
	public abstract void TriggerTempEntity(KeyValues pKeyValues);

	public abstract void ClientProjectile<IRF>(scoped ref IRF filter, float delay,
		in Vector3 vecOrigin, in Vector3 vecVelocity, int modelindex, int lifetime, BaseEntity? owner) where IRF : IRecipientFilter;
}
#endif
