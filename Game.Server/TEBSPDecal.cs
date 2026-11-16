using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBSPDecal")]
public partial class TEBSPDecal(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Entity)) { Bits = 13, Flags = PropFlags.Unsigned },
		new(nameof(Index)) { Bits = 9, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_nEntity")]
	public int Entity;
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
	static readonly TEBSPDecal g_TEBSPDecal = new("BSP Decal");

	public static void TE_BSPDecal<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, int entity, int index) where IRF : IRecipientFilter {
		g_TEBSPDecal.Origin = pos;
		g_TEBSPDecal.Entity = entity;
		g_TEBSPDecal.Index = index;

		g_TEBSPDecal.Create(ref filter, delay);
	}
}
