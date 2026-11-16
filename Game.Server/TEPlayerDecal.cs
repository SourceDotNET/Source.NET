using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEPlayerDecal")]
public partial class TEPlayerDecal(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Entity)) { Bits = 13, Flags = PropFlags.Unsigned },
		new(nameof(Player)) { Bits = 7, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_nEntity")]
	public int Entity;
	[NetworkVar("m_nPlayer")]
	public int Player;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		Player = 1;
		Entity = 0;
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
	static readonly TEPlayerDecal g_TEPlayerDecal = new("Player Decal");

	public static void TE_PlayerDecal<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, int player, int entity) where IRF : IRecipientFilter {
		g_TEPlayerDecal.Origin = pos;
		g_TEPlayerDecal.Player = player;
		g_TEPlayerDecal.Entity = entity;

		g_TEPlayerDecal.Create(ref filter, delay);
	}
}
