using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEDynamicLight")]
public partial class TEDynamicLight(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(R)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(G)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(B)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Exponent)) { Bits = 8 },
		new(nameof(Radius)) { Bits = 8, Flags = PropFlags.RoundUp, Low = 0, High = 2560.0f },
		new(nameof(Time)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0, High = 25.6f },
		new(nameof(Decay)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0, High = 2560.0f },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("r")]
	public int R;
	[NetworkVar("g")]
	public int G;
	[NetworkVar("b")]
	public int B;
	[NetworkVar("exponent")]
	public int Exponent;
	[NetworkVar("m_fRadius")]
	public float Radius;
	[NetworkVar("m_fTime")]
	public float Time;
	[NetworkVar("m_fDecay")]
	public float Decay;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		R = 255;
		G = 255;
		B = 63;
		Origin = current_origin;

		Radius = 200;
		Time = 2.0f;
		Decay = 0.0f;

		Origin.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Origin += forward * 50.0f;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEDynamicLight g_TEDynamicLight = new("Dynamic Light");

	public static void TE_DynamicLight<IRF>(scoped ref IRF filter, float delay, in Vector3 org, int r, int g, int b, int exponent, float radius, float time, float decay) where IRF : IRecipientFilter {
		g_TEDynamicLight.Origin = org;
		g_TEDynamicLight.R = r;
		g_TEDynamicLight.G = g;
		g_TEDynamicLight.B = b;
		g_TEDynamicLight.Exponent = exponent;
		g_TEDynamicLight.Radius = radius;
		g_TEDynamicLight.Time = time;
		g_TEDynamicLight.Decay = decay;

		g_TEDynamicLight.Create(ref filter, delay);
	}
}
