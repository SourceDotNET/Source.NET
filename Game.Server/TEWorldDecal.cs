using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEWorldDecal")]
public partial class TEWorldDecal(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Index)) { Bits = 9, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_nIndex")]
	public int Index;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
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
	static readonly TEWorldDecal g_TEWorldDecal = new("World Decal");

	public static void TE_WorldDecal<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, int index) where IRF : IRecipientFilter {
		g_TEWorldDecal.Origin = pos;
		g_TEWorldDecal.Index = index;

		g_TEWorldDecal.Create(ref filter, delay);
	}
}
