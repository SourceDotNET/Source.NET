using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEMetalSparks", NoBase = true)]
public partial class TEMetalSparks(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Pos)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Dir)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_vecPos")]
	public Vector3 Pos;
	[NetworkVar("m_vecDir")]
	public Vector3 Dir;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		Pos = current_origin;

		MathLib.AngleVectors(current_angles, out Dir);

		Pos += new Vector3(0, 0, 24);

		MathLib.AngleVectors(current_angles, out Vector3 forward);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Pos += forward * 100.0f;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEMetalSparks g_TEMetalSparks = new("Metal Sparks");

	public static void TE_MetalSparks<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, in Vector3 dir) where IRF : IRecipientFilter {
		g_TEMetalSparks.Pos = pos;
		g_TEMetalSparks.Dir = dir;

		Assert(dir.Length() < 1.01);

		g_TEMetalSparks.Create(ref filter, delay);
	}
}
