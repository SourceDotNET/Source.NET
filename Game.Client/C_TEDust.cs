using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEDust")]
public partial class C_TEDust : C_TEParticleSystem
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(LSize)) { Bits = 0, Flags = PropFlags.Coord | PropFlags.NoScale },
		new(nameof(LSpeed)) { Bits = 0, Flags = PropFlags.Coord | PropFlags.NoScale },
		new(nameof(Direction)) { Bits = 4, Low = -1.0f, High = 1.0f },
	];

	[NetworkVar("m_flSize")]
	public float LSize;
	[NetworkVar("m_flSpeed")]
	public float LSpeed;
	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;
}
