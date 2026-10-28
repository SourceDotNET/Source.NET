using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamFollow")]
public partial class TEBeamFollow(ReadOnlySpan<char> name) : BaseBeam(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(EntIndex)) { Bits = 24, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_iEntIndex")]
	public int EntIndex;
}
