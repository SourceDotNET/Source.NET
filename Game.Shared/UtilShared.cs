#if CLIENT_DLL || GAME_DLL
global using static Game.Util_Globals;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;

#endif

#if CLIENT_DLL
using Game.Client;
#endif

using Source.Common.Hashing;
using Source.Common.Mathematics;
using Source.Engine;

using System.Drawing.Drawing2D;
using System.Globalization;
using System.Numerics;

namespace Game;
#if CLIENT_DLL || GAME_DLL

public static partial class Util_Globals
{
	public static readonly ConVar developer = new("developer", "0", 0, "Set developer message level"); // developer mode
	public static readonly ConVar r_visualizetraces = new("r_visualizetraces", "0", FCvar.Cheat);

	public static int SeedFileLineHash(int seedvalue, ReadOnlySpan<char> sharedname, int additionalSeed) {
		CRC32_t retval = default;

		CRC32.Init(ref retval);

		CRC32.ProcessBuffer(ref retval, seedvalue);
		CRC32.ProcessBuffer(ref retval, additionalSeed);
		CRC32.ProcessBuffer(ref retval, sharedname, strlen(sharedname));

		CRC32.Final(ref retval);

		return (int)(retval);
	}

	public static float SharedRandomFloat(ReadOnlySpan<char> sharedname, int minVal, int maxVal, int additionalSeed = 0) {
		int seed = SeedFileLineHash(BaseEntity.GetPredictionRandomSeed(), sharedname, additionalSeed);
		RandomSeed(seed);
		return RandomFloat(minVal, maxVal);
	}

	public static int SharedRandomInt(ReadOnlySpan<char> sharedname, int minVal, int maxVal, int additionalSeed = 0) {
		int seed = SeedFileLineHash(BaseEntity.GetPredictionRandomSeed(), sharedname, additionalSeed);
		RandomSeed(seed);
		return RandomInt(minVal, maxVal);
	}

	public static bool PassServerEntityFilter(IHandleEntity? touch, IHandleEntity? pass) {
		if (pass == null)
			return true;

		if (touch == pass)
			return false;

		BaseEntity? entTouch = EntityFromEntityHandle(touch);
		BaseEntity? entPass = EntityFromEntityHandle(pass);
		if (entTouch == null || entPass == null)
			return true;

		// don't clip against own missiles
		if (entTouch.GetOwnerEntity() == entPass)
			return false;

		// don't clip against owner
		if (entPass.GetOwnerEntity() == entTouch)
			return false;


		return true;
	}

	//-----------------------------------------------------------------------------
	// A standard filter to be applied to just about everything.
	//-----------------------------------------------------------------------------
	public static bool StandardFilterRules(IHandleEntity? handleEntity, Contents contentsMask) {
		BaseEntity? collide = EntityFromEntityHandle(handleEntity);

		// Static prop case...
		if (collide == null)
			return true;

		SolidType solid = collide.GetSolid();
		Model? model = collide.GetModel();

		if ((modelinfo.GetModelType(model) != ModelType.Brush) || (solid != SolidType.BSP && solid != SolidType.VPhysics)) {
			if ((contentsMask & Contents.Monster) == 0)
				return false;
		}

		// This code is used to cull out tests against see-thru entities
		if ((contentsMask & Contents.Window) == 0 && collide.IsTransparent())
			return false;

		// FIXME: this is to skip BSP models that are entities that can be
		// potentially moved/deleted, similar to a monster but doors don't seem to
		// be flagged as monsters
		// FIXME: the FL_WORLDBRUSH looked promising, but it needs to be set on
		// everything that's actually a worldbrush and it currently isn't
		if ((contentsMask & Contents.Moveable) == 0 && (collide.GetMoveType() == MoveType.Push))// !(touch->flags & FL_WORLDBRUSH) )
			return false;

		return true;
	}

	public static bool EntityHasMatchingRootParent(BaseEntity? rootParent, BaseEntity entity) {
		if (rootParent != null) {
			// NOTE: Don't let siblings/parents collide.
			if (rootParent == entity.GetRootMoveParent())
				return true;
			if (entity.GetOwnerEntity() != null && rootParent == entity.GetOwnerEntity()!.GetRootMoveParent())
				return true;
		}
		return false;
	}

	public static BaseEntity? EntityFromEntityHandle(IHandleEntity? handle) {
#if CLIENT_DLL
		IClientUnknown? unk = (IClientUnknown?)handle;
		return (BaseEntity?)unk?.GetBaseEntity();
#else
		if (StaticPropMgrGlobals.g_StaticPropMgr.IsStaticProp(handle))
			return null;

		IServerUnknown? unk = (IServerUnknown?)handle;
		return (BaseEntity?)unk?.GetBaseEntity();
#endif
	}

	// Parses up to 'count' whitespace-separated floats out of pString into vec, zero-filling the rest.
	public static void UTIL_StringToFloatArray(Span<float> vec, int count, ReadOnlySpan<char> pString) {
		vec[..count].Clear();

		int pos = 0;
		for (int j = 0; j < count; j++) {
			while (pos < pString.Length && pString[pos] <= ' ')
				pos++;
			if (pos >= pString.Length)
				break;

			int start = pos;
			while (pos < pString.Length && pString[pos] > ' ')
				pos++;

			float.TryParse(pString[start..pos], NumberStyles.Float, CultureInfo.InvariantCulture, out vec[j]);
		}
	}

	public static void UTIL_StringToVector(Span<float> vec, ReadOnlySpan<char> pString) => UTIL_StringToFloatArray(vec, 3, pString);

	public static void UTIL_StringToIntArray(Span<int> vec, int count, ReadOnlySpan<char> pString) {
		vec[..count].Clear();

		int pos = 0;
		for (int j = 0; j < count; j++) {
			while (pos < pString.Length && pString[pos] <= ' ')
				pos++;
			if (pos >= pString.Length)
				break;

			int start = pos;
			while (pos < pString.Length && pString[pos] > ' ')
				pos++;

			vec[j] = atoi(pString[start..pos]);
		}
	}
}

public static partial class Util
{

#if CLIENT_DLL
	public static BasePlayer PlayerByIndex(int entindex) => ToBasePlayer(cl_entitylist.GetEnt(entindex));
#endif
	static readonly ConVar developer = new("developer", "0", 0, "Set developer message level"); // developer mode

	public static Contents PointContents(in Vector3 vec) => enginetrace.GetPointContents(vec, out _);

	// UTIL_StringToColor32: parses "r g b a" into a color.
	public static void StringToColor32(out Color color, ReadOnlySpan<char> pString) {
		Span<int> tmp = stackalloc int[4];
		UTIL_StringToIntArray(tmp, 4, pString);
		// C++ assigns each channel into a byte (implicit truncation); mask so the ctor's range assert passes.
		color = new Color(tmp[0] & 0xFF, tmp[1] & 0xFF, tmp[2] & 0xFF, tmp[3] & 0xFF);
	}
	public static float VecToYaw(in Vector3 vec) {
		if (vec.Y == 0 && vec.X == 0)
			return 0;

		float yaw = MathF.Atan2(vec.Y, vec.X);
		yaw = MathLib.RAD2DEG(yaw);

		if (yaw < 0)
			yaw += 360;

		return yaw;
	}

	public static float VecToPitch(in Vector3 vec) {
		if (vec.Y == 0 && vec.X == 0) {
			if (vec.Z < 0)
				return 180.0f;
			else
				return -180.0f;
		}

		float dist = vec.Length2D();
		float pitch = MathF.Atan2(-vec.Z, dist);

		pitch = MathLib.RAD2DEG(pitch);

		return pitch;
	}

	public static float VecToYaw(in Matrix3x4 matrix, in Vector3 vec) {
		Vector3 tmp = vec;
		MathLib.VectorNormalize(ref tmp);

		float x = matrix[0][0] * tmp.X + matrix[1][0] * tmp.Y + matrix[2][0] * tmp.Z;
		float y = matrix[0][1] * tmp.X + matrix[1][1] * tmp.Y + matrix[2][1] * tmp.Z;

		if (x == 0.0f && y == 0.0f)
			return 0.0f;

		float yaw = MathF.Atan2(-y, x);
		yaw = MathLib.RAD2DEG(yaw);

		if (yaw < 0)
			yaw += 360;

		return yaw;
	}


	public static float VecToPitch(in Matrix3x4 matrix, in Vector3 vec) {
		Vector3 tmp = vec;
		MathLib.VectorNormalize(ref tmp);

		float x = matrix[0][0] * tmp.X + matrix[1][0] * tmp.Y + matrix[2][0] * tmp.Z;
		float z = matrix[0][2] * tmp.X + matrix[1][2] * tmp.Y + matrix[2][2] * tmp.Z;

		if (x == 0.0f && z == 0.0f)
			return 0.0f;

		float pitch = MathF.Atan2(z, x);
		pitch = MathLib.RAD2DEG(pitch);

		if (pitch < 0)
			pitch += 360;

		return pitch;
	}

	public static Vector3 YawToVector(float yaw) {
		Vector3 ret;

		ret.Z = 0;
		float angle = MathLib.DEG2RAD(yaw);
		MathLib.SinCos(angle, out ret.Y, out ret.X);

		return ret;
	}

	public static void TraceEntity(BaseEntity entity, in Vector3 absStart, in Vector3 absEnd, Mask mask, out Trace ptr) {
		ICollideable collision = entity.GetCollideable()!;

		// Adding this assertion here so game code catches it, but really the assertion belongs in the engine
		// because one day, rotated collideables will work!
		Assert(collision.GetCollisionAngles() == vec3_angle);

		TraceFilterEntity traceFilter = new(entity, collision.GetCollisionGroup());

		ptr = default;
#if PORTAL
		// TODO:
		UTIL_Portal_TraceEntity(pEntity, vecAbsStart, vecAbsEnd, mask, &traceFilter, ptr);
#else
		enginetrace.SweepCollideable(collision, absStart, absEnd, collision.GetCollisionAngles(), mask, ref traceFilter, ref ptr);
#endif
	}

	public static void TraceEntity<IF>(BaseEntity entity, in Vector3 absStart, in Vector3 absEnd, Mask mask, scoped ref IF filter, out Trace ptr) where IF : struct, ITraceFilter {
		ICollideable collision = entity.GetCollideable()!;

		Assert(collision.GetCollisionAngles() == vec3_angle);

		ptr = default;
		enginetrace.SweepCollideable(collision, absStart, absEnd, collision.GetCollisionAngles(), mask, ref filter, ref ptr);
	}

	public static void ClipTraceToPlayers<IF>(in Vector3 absStart, in Vector3 absEnd, Mask mask, scoped ref IF filter, ref Trace tr) where IF : struct, ITraceFilter {
		Trace playerTrace = default;
		Ray ray = default;
		float smallestFraction = tr.Fraction;
		const float maxRange = 60.0f;

		ray.Init(absStart, absEnd);

		for (int k = 1; k <= gpGlobals.MaxClients; ++k) {
			BasePlayer? player = PlayerByIndex(k);

			if (player == null || !player.IsAlive() || !player.IsSolid())
				continue;

#if CLIENT_DLL
			if (player.IsDormant())
				continue;
#endif

			float range = DistanceToRay(player.WorldSpaceCenter(), absStart, absEnd);
			if (range < 0.0f || range > maxRange)
				continue;

			if (!filter.ShouldHitEntity(player, (Contents)mask))
				continue;

			enginetrace.ClipRayToEntity(ray, mask | (Mask)Contents.HitBox, player, ref playerTrace);
			if (playerTrace.Fraction < smallestFraction) {
				tr = playerTrace;
				smallestFraction = playerTrace.Fraction;
			}
		}
	}

	public static float DistanceToRay(in Vector3 pos, in Vector3 rayStart, in Vector3 rayEnd) => DistanceToRay(pos, rayStart, rayEnd, out _, out _);
	public static float DistanceToRay(in Vector3 pos, in Vector3 rayStart, in Vector3 rayEnd, out float along, out Vector3 pointOnRay) {
		Vector3 to = pos - rayStart;
		Vector3 dir = rayEnd - rayStart;
		float length = MathLib.VectorNormalize(ref dir);

		float rangeAlong = Vector3.Dot(dir, to);
		along = rangeAlong;

		float range;

		if (rangeAlong < 0.0f) {
			// off start point
			range = -(pos - rayStart).Length();
			pointOnRay = rayStart;
		}
		else if (rangeAlong > length) {
			// off end point
			range = -(pos - rayEnd).Length();
			pointOnRay = rayEnd;
		}
		else { // within ray bounds
			Vector3 onRay = rayStart + rangeAlong * dir;
			range = (pos - onRay).Length();
			pointOnRay = onRay;
		}

		return range;
	}

	public static void TraceRay(in Ray ray, Mask mask, IHandleEntity? ignore, CollisionGroup collisionGroup, out Trace ptr) {
		TraceFilterSimple traceFilter = new(ignore, collisionGroup);

		enginetrace.TraceRay(ray, mask, ref traceFilter, out ptr);
		// todo: visualize
	}

	public static void TraceLineFilterEntity(BaseEntity entity, in Vector3 absStart, in Vector3 absEnd, Mask mask, CollisionGroup collisionGroup, out Trace ptr) {
		TraceFilterEntity traceFilter = new(entity, collisionGroup);
		Ray ray = default;
		ray.Init(absStart, absEnd);
		enginetrace.TraceRay(ray, mask, ref traceFilter, out ptr);
	}

	public static void TraceLine(in Vector3 absStart, in Vector3 absEnd, Mask mask, IHandleEntity? ignore, CollisionGroup collisionGroup, out Trace ptr) {
		Ray ray = default;
		ray.Init(absStart, absEnd);

		TraceFilterSimple traceFilter = new(ignore, collisionGroup);

		enginetrace.TraceRay(ray, mask, ref traceFilter, out ptr);
		// todo: visualize
	}

	public static void TraceLine<IF>(in Vector3 absStart, in Vector3 absEnd, Mask mask, IHandleEntity? ignore, scoped ref IF filter, out Trace ptr) where IF : struct, ITraceFilter {
		Ray ray = default;
		ray.Init(absStart, absEnd);

		enginetrace.TraceRay(ray, mask, ref filter, out ptr);
		// todo: visualize
	}

	public static void TraceLine<IF>(in Vector3 absStart, in Vector3 absEnd, Mask mask, scoped ref IF filter, out Trace ptr) where IF : struct, ITraceFilter {
		Ray ray = default;
		ray.Init(absStart, absEnd);

		enginetrace.TraceRay(ray, mask, ref filter, out ptr);

		// todo: visualize
	}

	public static void TraceModel(in Vector3 vecStart, in Vector3 vecEnd, in Vector3 hullMin, in Vector3 hullMax, BaseEntity? entModel, CollisionGroup collisionGroup, out Trace ptr) {
		if (entModel != null && entModel.ShouldCollide(collisionGroup, unchecked((Contents)Mask.All))) {
			Ray ray = default;
			ray.Init(vecStart, vecEnd, hullMin, hullMax);
			ptr = default;
			enginetrace.ClipRayToEntity(ray, Mask.All, entModel, ref ptr);
		}
		else {
			ptr = default;
			ptr.Fraction = 1.0f;
		}
	}

	public static void TraceHull(in Vector3 vecAbsStart, in Vector3 vecAbsEnd, in Vector3 hullMin, in Vector3 hullMax, Mask mask, IHandleEntity? ignore, CollisionGroup collisionGroup, out Trace ptr) {
		Ray ray = default;
		ray.Init(vecAbsStart, vecAbsEnd, hullMin, hullMax);

		TraceFilterSimple traceFilter = new(ignore, collisionGroup);

		enginetrace.TraceRay(ray, mask, ref traceFilter, out ptr);

		// todo: visualize
	}


	public static void TraceHull<IF>(in Vector3 absStart, in Vector3 absEnd, in Vector3 hullMin, in Vector3 hullMax, Mask mask, scoped ref IF filter, out Trace ptr) where IF : struct, ITraceFilter {
		Ray ray = default;
		ray.Init(absStart, absEnd, hullMin, hullMax);

		enginetrace.TraceRay(ray, mask, ref filter, out ptr);

		// todo: visualize
	}
}

public delegate bool ShouldHitFunc(IHandleEntity handleEntity, Contents contentsMask);

public struct TraceFilterSimple(IHandleEntity? passentity, CollisionGroup collisionGroup, ShouldHitFunc? extraShouldHitCheckFn = null) : ITraceFilter
{
	public IHandleEntity? PassEntity = passentity;
	public CollisionGroup CollisionGroup = collisionGroup;
	public ShouldHitFunc? ExtraShouldHitCheckFunction = extraShouldHitCheckFn;

	public bool ShouldHitEntity(IHandleEntity handleEntity, Contents contentsMask) {
		if (!StandardFilterRules(handleEntity, contentsMask))
			return false;

		if (PassEntity != null) {
			if (!PassServerEntityFilter(handleEntity, PassEntity))
				return false;
		}

		// Don't test if the game code tells us we should ignore this collision...
		BaseEntity? entity = EntityFromEntityHandle(handleEntity);
		if (entity == null)
			return false;
		if (!entity.ShouldCollide(CollisionGroup, contentsMask))
			return false;
		if (entity != null && !g_pGameRules.ShouldCollide(CollisionGroup, entity.GetCollisionGroup()))
			return false;
		if (ExtraShouldHitCheckFunction != null &&
			(!(ExtraShouldHitCheckFunction(handleEntity, contentsMask))))
			return false;

		return true;
	}
}

public struct TraceFilterSimpleList(CollisionGroup collisionGroup) : ITraceFilter
{
	public TraceFilterSimple Simple = new(null, collisionGroup);
	public List<IHandleEntity> PassEntities = [];

	public bool ShouldHitEntity(IHandleEntity handleEntity, Contents contentsMask) {
		if (PassEntities.Contains(handleEntity))
			return false;
		return Simple.ShouldHitEntity(handleEntity, contentsMask);
	}

	public void AddEntityToIgnore(IHandleEntity? entity) => PassEntities.Add(entity!);
}

public struct TraceFilterEntity(BaseEntity entity, CollisionGroup collisionGroup) : ITraceFilter
{
	public TraceFilterSimple TraceFilterSimple = new TraceFilterSimple { PassEntity = entity, CollisionGroup = collisionGroup };
	public BaseEntity? RootParent = entity.GetRootMoveParent();
	public BaseEntity? Entity = entity;
	public bool CheckHash = g_EntityCollisionHash.IsObjectInHash(entity);

	public bool ShouldHitEntity(IHandleEntity handleEntity, Contents contentsMask) {
		BaseEntity? entity = EntityFromEntityHandle(handleEntity);
		if (entity == null)
			return false;

		// Check parents against each other
		// NOTE: Don't let siblings/parents collide.
		if (EntityHasMatchingRootParent(RootParent, entity))
			return false;

		if (CheckHash) {
			if (g_EntityCollisionHash.IsObjectPairInHash(Entity!, entity))
				return false;
		}

		return TraceFilterSimple.ShouldHitEntity(handleEntity, contentsMask);
	}
}

public struct TraceFilterSkipTwoEntities(IHandleEntity? passentity, IHandleEntity? passentity2, CollisionGroup collisionGroup) : ITraceFilter
{
	public TraceFilterSimple Simple = new(passentity, collisionGroup);
	public IHandleEntity? PassEnt2 = passentity2;

	public bool ShouldHitEntity(IHandleEntity handleEntity, Contents contentsMask) {
		if (!PassServerEntityFilter(handleEntity, PassEnt2))
			return false;

		return Simple.ShouldHitEntity(handleEntity, contentsMask);
	}
}

#if !CLIENT_DLL
public struct TraceFilterLOS(IHandleEntity? handleEntity, CollisionGroup collisionGroup, IHandleEntity? handleEntity2 = null) : ITraceFilter
{
	public TraceFilterSkipTwoEntities SkipTwo = new(handleEntity, handleEntity2, collisionGroup);

	public bool ShouldHitEntity(IHandleEntity handleEntity, Contents contentsMask) {
		BaseEntity? entity = EntityFromEntityHandle(handleEntity);

		if (!entity!.BlocksLOS())
			return false;

		return SkipTwo.Simple.ShouldHitEntity(handleEntity, contentsMask);
	}
}
#endif

public struct TraceFilterNoNPCsOrPlayer(IHandleEntity? passentity, CollisionGroup collisionGroup) : ITraceFilter
{
	TraceFilterSimple Inner = new(passentity, collisionGroup);

	public bool ShouldHitEntity(IHandleEntity serverEntity, Contents contentsMask) {
		if (!Inner.ShouldHitEntity(serverEntity, contentsMask))
			return false;

		BaseEntity? entity = EntityFromEntityHandle(serverEntity);
		if (entity == null)
			return false;

		return !entity.IsNPC() && !entity.IsPlayer();
	}
}

public class CountdownTimer
{
	private TimeUnit_t Duration;
	private TimeUnit_t Timestamp;

	public CountdownTimer() {
		Timestamp = -1f;
		Duration = 0f;
	}

	public void Start(float duration) {
		Timestamp = Now() + duration;
		Duration = duration;
	}

	public void Reset() => Timestamp = Now() + Duration;
	public void Invalidate() => Timestamp = -1f;
	public bool HasStarted() => Timestamp > 0f;
	public bool IsElapsed() => Now() > Timestamp;
	public TimeUnit_t GetElapsedTime() => Now() - Timestamp + Duration;
	public TimeUnit_t GetRemainingTime() => Timestamp - Now();
	public TimeUnit_t GetCountdownDuration() => Timestamp > 0f ? Duration : 0f;
	protected virtual TimeUnit_t Now()
#if CLIENT_DLL || GAME_DLL
		=> gpGlobals.CurTime;
#else
		=> 0;
#endif
}


public class IntervalTimer
{
	private TimeUnit_t Timestamp;
	public IntervalTimer() => Timestamp = -1f;
	public void Reset() => Timestamp = Now();
	public void Start() => Timestamp = Now();
	public void Invalidate() => Timestamp = -1f;
	public bool HasStarted() => Timestamp > 0f;
	public TimeUnit_t GetElapsedTime() => HasStarted() ? Now() - Timestamp : 99999.9f;
	public bool IsLessThen(float duration) => Now() - Timestamp < duration;
	public bool IsGreaterThen(float duration) => Now() - Timestamp > duration;
	protected virtual TimeUnit_t Now() => gpGlobals.CurTime;
}
#endif
