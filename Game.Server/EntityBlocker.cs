using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Formats.BSP;

using System.Numerics;

namespace Game.Server;

[LinkEntityToClass("entity_blocker")]
public class EntityBlocker : BaseEntity
{
	public static EntityBlocker? Create(in Vector3 origin, in Vector3 mins, in Vector3 maxs, BaseEntity? owner = null, bool blockPhysics = false) {
		EntityBlocker? blocker = (EntityBlocker?)Create("entity_blocker", origin, vec3_angle, owner);

		if (blocker != null) {
			Util.SetSize(blocker, mins, maxs);
			if (blockPhysics)
				blocker.VPhysicsInitStatic();
		}

		return blocker;
	}

	public override void Spawn() {
		SetSolid(SolidType.BBox);
		AddSolidFlags(SolidFlags.CustomRayTest);
	}

	public override bool TestCollision(in Ray ray, Contents mask, ref Trace trace) => false;
}
