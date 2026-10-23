using Source.Common;
using Source;

using Game.Shared;

using System.Numerics;
namespace Game.Client;

[NetworkName("CTEDynamicLight")]
public partial class C_TEDynamicLight : C_BaseTempEntity
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

	public override void PostDataUpdate(DataUpdateType updateType) {
		BroadcastRecipientFilter filter = new();
		TE_DynamicLight(filter, 0.0f, in Origin, R, G, B, Exponent, Radius, Time, Decay, (int)LightIndex.TEDynamic);
	}
}

public static partial class TempEnts
{
	public static void TE_DynamicLight(IRecipientFilter filter, float delay, in Vector3 org, int r, int g, int b, int exponent, float radius, float time, float decay, int lightIndex = (int)LightIndex.TEDynamic) {
		DLight? dl = effects.AllocDlight(lightIndex);
		if (dl == null)
			return;

		dl.Origin = org;
		dl.Radius = radius;
		dl.Color.R = (byte)r;
		dl.Color.G = (byte)g;
		dl.Color.B = (byte)b;
		dl.Color.Exponent = (sbyte)exponent;
		dl.Die = (float)gpGlobals.CurTime + time;
		dl.Decay = decay;
	}
}
