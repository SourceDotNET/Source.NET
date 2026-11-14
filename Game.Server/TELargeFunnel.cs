using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTELargeFunnel")]
public partial class TELargeFunnel(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Reversed)) { Bits = 2, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nReversed")]
	public int Reversed;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		Reversed = 0;
		Origin = current_origin;

		Origin.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward, out Vector3 right, out _);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Origin += forward * 50.0f;
		Origin += right * 25.0f;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TELargeFunnel g_TELargeFunnel = new("Large Funnel");

	public static void TE_LargeFunnel<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, int modelindex, int reversed) where IRF : IRecipientFilter {
		g_TELargeFunnel.Origin = pos;
		g_TELargeFunnel.Reversed = reversed;

		g_TELargeFunnel.Create(ref filter, delay);
	}
}
