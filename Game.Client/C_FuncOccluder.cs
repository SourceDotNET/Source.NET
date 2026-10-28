using Game.Shared;

using Source;
using Source.Common;

namespace Game.Client;

[NetworkName("CFuncOccluder")]
public partial class C_FuncOccluder : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Active),
		new(nameof(OccluderIndex)) { Bits = 10, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_bActive")]
	public bool Active;
	[NetworkVar("m_nOccluderIndex")]
	public int OccluderIndex;
}

