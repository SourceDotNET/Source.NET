using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEEnergySplash", NoBase = true)]
public partial class TEEnergySplash(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Pos)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Dir)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Explosive)) { Bits = 1, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecPos")]
	public Vector3 Pos;
	[NetworkVar("m_vecDir")]
	public Vector3 Dir;
	[NetworkVar("m_bExplosive")]
	public int Explosive;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		Pos = current_origin;

		MathLib.AngleVectors(current_angles, out Dir);

		Pos.Z += 24;

		Vector3 forward = Dir;
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Pos += forward * 100.0f;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEEnergySplash g_TEEnergySplash = new("Energy Splash");

	public static void TE_EnergySplash<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, in Vector3 dir, bool explosive) where IRF : IRecipientFilter {
		g_TEEnergySplash.Pos = pos;
		g_TEEnergySplash.Dir = dir;
		g_TEEnergySplash.Explosive = explosive ? 1 : 0;

		g_TEEnergySplash.Create(ref filter, delay);
	}
}
