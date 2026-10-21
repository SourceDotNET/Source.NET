using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamEntPoint")]
public partial class TEBeamEntPoint(ReadOnlySpan<char> name) : BaseBeam(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartEntity)) { Bits = 24, Flags = PropFlags.Unsigned },
		new(nameof(EndEntity)) { Bits = 24, Flags = PropFlags.Unsigned },
		new(nameof(StartPoint)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(EndPoint)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_nStartEntity")]
	public int StartEntity;
	[NetworkVar("m_nEndEntity")]
	public int EndEntity;
	[NetworkVar("m_vecStartPoint")]
	public Vector3 StartPoint;
	[NetworkVar("m_vecEndPoint")]
	public Vector3 EndPoint;
}
