using Source.Common;
using Source;

using Game.Shared;
using System.Numerics;
using Source.Common.MaterialSystem;
using System;

namespace Game.Server;


[LinkEntityToClass("func_occluder")]
[NetworkName("CFuncOccluder", NoBase = true)]
public partial class FuncOccluder : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Active),
		new(nameof(OccluderIndex)) { Bits = 10, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_bActive")]
	public partial bool Active { get; set; }
	[NetworkVar("m_nOccluderIndex")]
	public partial int OccluderIndex { get; set; }
}
