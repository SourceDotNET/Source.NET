using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamRing")]
public partial class TEBeamRing(ReadOnlySpan<char> name) : BaseBeam(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartEntity)) { Bits = 13, Flags = PropFlags.Unsigned },
		new(nameof(EndEntity)) { Bits = 13, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nStartEntity")]
	public int StartEntity;
	[NetworkVar("m_nEndEntity")]
	public int EndEntity;
}
