using Source.Common;
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
