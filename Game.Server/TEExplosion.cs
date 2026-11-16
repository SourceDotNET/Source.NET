using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEExplosion")]
public partial class TEExplosion(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Scale)) { Bits = 9, Low = 0.0f, High = 51.2f },
		new(nameof(Flags)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Radius)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(Magnitude)) { Bits = 16, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_fScale")]
	public float Scale;
	[NetworkVar("m_nFlags")]
	public int Flags;
	[NetworkVar("m_nRadius")]
	public int Radius;
	[NetworkVar("m_nMagnitude")]
	public int Magnitude;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		Scale = 0.5f;
		Flags = (int)TempEntExplFlags.None;
		Origin = current_origin;

		Origin.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Origin += forward * 50;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEExplosion g_TEExplosion = new("Explosion");

	public static void TE_Explosion<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, int modelindex, float scale, int framerate, int flags, int radius, int magnitude, Vector3? normal = null, byte materialType = (byte)'C') where IRF : IRecipientFilter {
		g_TEExplosion.Origin = pos;
		g_TEExplosion.Scale = scale;
		g_TEExplosion.Flags = flags;
		g_TEExplosion.Radius = radius;
		g_TEExplosion.Magnitude = magnitude;

		g_TEExplosion.Create(ref filter, delay);
	}
}
