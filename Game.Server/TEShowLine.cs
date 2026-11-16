using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEShowLine")]
public partial class TEShowLine(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(End)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_vecEnd")]
	public Vector3 End;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		Origin = current_origin;

		Origin.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward, out Vector3 right, out _);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		End = Origin + forward * 100.0f;

		Origin = End + right * -128;
		End += right * 128;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEShowLine g_TEShowLine = new("Show Line");

	public static void TE_ShowLine<IRF>(scoped ref IRF filter, float delay, in Vector3 start, in Vector3 end) where IRF : IRecipientFilter {
		g_TEShowLine.Origin = start;
		g_TEShowLine.End = end;

		g_TEShowLine.Create(ref filter, delay);
	}
}
