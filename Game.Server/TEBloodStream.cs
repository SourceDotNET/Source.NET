using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBloodStream")]
public partial class TEBloodStream(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Direction)) { Bits = 11, Low = -10.0f, High = 10.0f },
		new(nameof(R)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(G)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(B)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(A)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Amount)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;
	[NetworkVar("r")]
	public int R;
	[NetworkVar("g")]
	public int G;
	[NetworkVar("b")]
	public int B;
	[NetworkVar("a")]
	public int A;
	[NetworkVar("m_nAmount")]
	public int Amount;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		R = 247;
		G = 0;
		B = 0;
		A = 255;
		Amount = random.RandomInt(50, 150);
		Origin = current_origin;

		Origin.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Origin += forward * 50.0f;

		Direction = Util.RandomBloodVector();

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEBloodStream g_TEBloodStream = new("Blood Stream");

	public static void TE_BloodStream<IRF>(scoped ref IRF filter, float delay, in Vector3 org, in Vector3 dir, int r, int g, int b, int a, int amount) where IRF : IRecipientFilter {
		g_TEBloodStream.Origin = org;
		g_TEBloodStream.Direction = dir;
		g_TEBloodStream.R = r;
		g_TEBloodStream.G = g;
		g_TEBloodStream.B = b;
		g_TEBloodStream.A = a;
		g_TEBloodStream.Amount = amount;

		g_TEBloodStream.Create(ref filter, delay);
	}
}
