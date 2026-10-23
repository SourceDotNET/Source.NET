using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTESparks")]
public partial class C_TESparks : C_TEParticleSystem
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
}

public static partial class TempEnts
{
	public static void TE_Sparks(IRecipientFilter filter, float delay, in Vector3 pos, int magnitude, int trailLength, in Vector3 dir) {
		throw new NotImplementedException();
	}
}
