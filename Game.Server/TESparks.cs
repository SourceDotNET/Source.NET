using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTESparks")]
public partial class TESparks(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Magnitude)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(TrailLength)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(Dir)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_nMagnitude")]
	public int Magnitude;
	[NetworkVar("m_nTrailLength")]
	public int TrailLength;
	[NetworkVar("m_vecDir")]
	public Vector3 Dir;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		Origin = current_origin;

		Origin.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Origin += forward * 100;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TESparks g_TESparks = new("Sparks");

	public static void TE_Sparks<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, int magnitude, int trailLength, in Vector3 dir) where IRF : IRecipientFilter {
		g_TESparks.Origin = pos;
		g_TESparks.Magnitude = magnitude;
		g_TESparks.TrailLength = trailLength;
		g_TESparks.Dir = dir;

		g_TESparks.Create(ref filter, delay);
	}
}
