using Source;
using Source.Common;
using Source.Common.Formats.BSP;
using Source.Particles;

using System.Numerics;

#if CLIENT_DLL
using Game.Client;
#elif GAME_DLL
using Game.Server;
#endif

namespace Game.Shared;

public sealed class ParticleSystemQuery : IParticleSystemQuery
{
	public static readonly ParticleSystemQuery g_pParticleSystemQuery = new();

	static readonly object LightMutex = new();

	public void GetLightingAtPoint(in Vector3 origin, out Color tint) {
#if CLIENT_DLL
		if (engine.IsInGame()) {
			Vector3 totalColor;
			lock (LightMutex)
				totalColor = engine.GetLightForPoint(origin, true);

			tint = default;
			tint.SetColor((int)(totalColor.X * 255), (int)(totalColor.Y * 255), (int)(totalColor.Z * 255), 0);
			return;
		}
#endif
		tint = new(255, 255, 255, 255);
	}

	public void TraceLine(in Vector3 absStart, in Vector3 absEnd, Mask mask, IHandleEntity? ignore, int collisionGroup, ref GameTrace trace) {
		bool doTrace = false;
#if CLIENT_DLL
		doTrace = engine.IsInGame();
#endif
		if (doTrace) {
			Util.TraceLine(absStart, absEnd, mask, ignore, (CollisionGroup)collisionGroup, out Trace tempTrace);
			trace.StartPos = tempTrace.StartPos;
			trace.EndPos = tempTrace.EndPos;
			trace.Plane = tempTrace.Plane;
			trace.Fraction = tempTrace.Fraction;
			trace.Contents = tempTrace.Contents;
			trace.DispFlags = tempTrace.DispFlags;
			trace.AllSolid = tempTrace.AllSolid;
			trace.StartSolid = tempTrace.StartSolid;
		}
		else {
			trace.StartSolid = false;
			trace.Fraction = 1.0f;
		}
	}

	public bool MovePointInsideControllingObject(ParticleCollection particles, object? obj, ref Vector3 pnt) {
#if CLIENT_DLL
		if (obj is not C_BaseEntity entity)
			return true;

		Ray ray = default;
		ray.Init(pnt, pnt);
		Trace tr = default;
		enginetrace.ClipRayToEntity(in ray, Mask.All, entity, ref tr);
		return tr.StartSolid;
#else
		return true;
#endif
	}

	static readonly (string Name, CollisionGroup Group)[] NameMap = [
		("NONE", CollisionGroup.None),
		("DEBRIS", CollisionGroup.Debris),
		("INTERACTIVE", CollisionGroup.Interactive),
		("NPC", CollisionGroup.NPC),
		("ACTOR", CollisionGroup.NPCActor),
		("PASSABLE", CollisionGroup.PassableDoor),
	];

	public int GetCollisionGroupFromName(ReadOnlySpan<char> collisionGroupName) {
		foreach ((string name, CollisionGroup group) in NameMap) {
			if (collisionGroupName.Equals(name, StringComparison.OrdinalIgnoreCase))
				return (int)group;
		}
		return (int)CollisionGroup.None;
	}

	public Vector3 GetLocalPlayerPos() {
#if CLIENT_DLL
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
#elif GAME_DLL
		BasePlayer? player = AI_UtilsGlobals.AI_GetSinglePlayer();
#else
		object? player = null;
#endif
#if CLIENT_DLL || GAME_DLL
		if (player == null)
			return Vector3.Zero;
		return player.WorldSpaceCenter();
#else
		return Vector3.Zero;
#endif
	}

	public void GetLocalPlayerEyeVectors(out Vector3 forward, out Vector3 right, out Vector3 up) {
#if CLIENT_DLL
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
#elif GAME_DLL
		BasePlayer? player = AI_UtilsGlobals.AI_GetSinglePlayer();
#endif
#if CLIENT_DLL || GAME_DLL
		if (player != null) {
			player.EyeVectors(out forward, out right, out up);
			return;
		}
#endif
		forward = Vector3.Zero;
		right = Vector3.Zero;
		up = Vector3.Zero;
	}

	public float GetPixelVisibility(ref int queryHandle, in Vector3 origin, float scale) => 0.0f;
}
