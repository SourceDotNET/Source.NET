using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEDecal")]
public partial class TEDecal(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Start)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Entity)) { Bits = 13, Flags = PropFlags.Unsigned },
		new(nameof(Hitbox)) { Bits = 12, Flags = PropFlags.Unsigned },
		new(nameof(Index)) { Bits = 9, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecStart")]
	public Vector3 Start;
	[NetworkVar("m_nEntity")]
	public int Entity;
	[NetworkVar("m_nHitbox")]
	public int Hitbox;
	[NetworkVar("m_nIndex")]
	public int Index;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		Entity = 0;
		Index = 0;
		Origin = current_origin;

		Origin.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Origin += forward * 50.0f;
		Vector3 vecEnd = Origin + forward * 1024.0f;

		Util.TraceLine(Origin, vecEnd, Mask.SolidBrushOnly, null, Source.CollisionGroup.None, out Trace tr);

		Origin = tr.EndPos;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEDecal g_TEDecal = new("Entity Decal");

	public static void TE_Decal<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, in Vector3 start, int entity, int hitbox, int index) where IRF : IRecipientFilter {
		g_TEDecal.Origin = pos;
		g_TEDecal.Start = start;
		g_TEDecal.Entity = entity;
		g_TEDecal.Hitbox = hitbox;
		g_TEDecal.Index = index;

		g_TEDecal.Create(ref filter, delay);
	}
}
