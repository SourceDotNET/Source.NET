using Game.Shared;

using Source.Common;

using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Server.HL2;

[LinkEntityToClass("prop_combine_ball")]
[NetworkName("CPropCombineBall")]
public partial class PropCombineBall : BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Emit),
		new(nameof(Radius)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Held),
		nameof(Launched),
	];

	[NetworkVar("m_bEmit")]
	public partial bool Emit { get; set; }
	[NetworkVar("m_flRadius")]
	public partial float Radius { get; set; }
	[NetworkVar("m_bHeld")]
	public partial bool Held { get; set; }
	[NetworkVar("m_bLaunched")]
	public partial bool Launched { get; set; }
}
