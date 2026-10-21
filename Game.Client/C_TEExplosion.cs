using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEExplosion")]
public partial class C_TEExplosion : C_TEParticleSystem
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Scale)) { Bits = 9, Low = 0.0f, High = 51.2f },
		new(nameof(Flags)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Radius)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(Magnitude)) { Bits = 16, Flags = PropFlags.Unsigned },
	];

	public int ModelIndex;
	[NetworkVar("m_fScale")]
	public float Scale;
	public int FrameRate;
	[NetworkVar("m_nFlags")]
	public int Flags;
	public Vector3 Normal;
	public int ChMaterialType;
	[NetworkVar("m_nRadius")]
	public int Radius;
	[NetworkVar("m_nMagnitude")]
	public int Magnitude;
}

public static partial class TempEnts
{
	public static void TE_Explosion(IRecipientFilter filter, float delay, in Vector3 pos, int modelIndex, float scale, int frameRate, int flags, int radius, int magnitude, in Vector3 normal, byte materialType = (byte)'C', bool shouldAffectRagdolls = true) {
		throw new NotImplementedException();
	}
}
