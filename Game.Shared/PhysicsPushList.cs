using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Shared;

public class PhysicsPushList
{
	public const int MAX_PUSHED_ENTITIES = 32;

	public TimeUnit_t LocalMoveTime;
	public Vector3 LocalOrigin;
	public QAngle LocalAngles;
	public int PushedCount;
	public readonly EHANDLE[] PushedEnts = new EHANDLE[MAX_PUSHED_ENTITIES];
	public readonly Vector3[] PushVec = new Vector3[MAX_PUSHED_ENTITIES];
}
