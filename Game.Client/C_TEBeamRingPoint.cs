using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEBeamRingPoint")]
public partial class C_TEBeamRingPoint : C_BaseBeam
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Center)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(LStartRadius)) { Bits = 16, Flags = PropFlags.RoundUp, Low = 0.0f, High = 4096.0f },
		new(nameof(LEndRadius)) { Bits = 16, Flags = PropFlags.RoundUp, Low = 0.0f, High = 4096.0f },
	];

	[NetworkVar("m_vecCenter")]
	public Vector3 Center;
	[NetworkVar("m_flStartRadius")]
	public float LStartRadius;
	[NetworkVar("m_flEndRadius")]
	public float LEndRadius;
}

public static partial class TempEnts
{
	public static void TE_BeamRingPoint(IRecipientFilter filter, float delay, in Vector3 center, float startRadius, float endRadius, int modelIndex, int haloIndex, int startFrame, int frameRate, float life, float width, int spread, float amplitude, int r, int g, int b, int a, int speed, int flags = 0) {
		throw new NotImplementedException();
	}
}
