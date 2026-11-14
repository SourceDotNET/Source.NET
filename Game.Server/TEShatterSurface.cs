using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;

[NetworkName("CTEShatterSurface")]
public partial class TEShatterSurface(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Angles)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Force)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ForcePos)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Width)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Height)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ShardSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SurfaceType)) { Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(UchFrontColor)) { Encoding = NetEncoding.Elements, Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(UchBackColor)) { Encoding = NetEncoding.Elements, Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecAngles")]
	public Vector3 Angles;
	[NetworkVar("m_vecForce")]
	public Vector3 Force;
	[NetworkVar("m_vecForcePos")]
	public Vector3 ForcePos;
	[NetworkVar("m_flWidth")]
	public float Width;
	[NetworkVar("m_flHeight")]
	public float Height;
	[NetworkVar("m_flShardSize")]
	public float ShardSize;
	[NetworkVar("m_nSurfaceType")]
	public int SurfaceType;
	[NetworkVar("m_uchFrontColor")]
	public InlineArray3<byte> UchFrontColor;
	[NetworkVar("m_uchBackColor")]
	public InlineArray3<byte> UchBackColor;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
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
	static readonly TEShatterSurface g_TEShatterSurface = new("Surface Shatter");

	public static void TE_ShatterSurface<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, in QAngle angle, in Vector3 force, in Vector3 forcePos, float width, float height, float shardsize, ShatterSurface surfacetype, int front_r, int front_g, int front_b, int back_r, int back_g, int back_b) where IRF : IRecipientFilter {
		g_TEShatterSurface.Origin = pos;
		g_TEShatterSurface.Angles = angle;
		g_TEShatterSurface.Force = force;
		g_TEShatterSurface.ForcePos = forcePos;
		g_TEShatterSurface.Width = width;
		g_TEShatterSurface.Height = height;
		g_TEShatterSurface.ShardSize = shardsize;
		g_TEShatterSurface.SurfaceType = (int)surfacetype;
		g_TEShatterSurface.UchFrontColor[0] = (byte)front_r;
		g_TEShatterSurface.UchFrontColor[1] = (byte)front_g;
		g_TEShatterSurface.UchFrontColor[2] = (byte)front_b;
		g_TEShatterSurface.UchBackColor[0] = (byte)back_r;
		g_TEShatterSurface.UchBackColor[1] = (byte)back_g;
		g_TEShatterSurface.UchBackColor[2] = (byte)back_b;

		g_TEShatterSurface.Create(ref filter, delay);
	}
}
