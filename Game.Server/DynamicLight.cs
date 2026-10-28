using Source.Common;
using Source;

using Game.Shared;

namespace Game.Server;

using static Source.Common.Networking.SVC_ClassInfo;


[LinkEntityToClass("light_dynamic")]
[NetworkName("CDynamicLight")]
public partial class DynamicLight : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Flags)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(LightStyle)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(Radius)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Exponent)) { Bits = 8 },
		new(nameof(InnerAngle)) { Bits = 8, Low = 0.0f, High = 360.0f },
		new(nameof(OuterAngle)) { Bits = 8, Low = 0.0f, High = 360.0f },
		new(nameof(SpotRadius)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_Flags")]
	public partial int Flags { get; set; }
	[NetworkVar("m_LightStyle")]
	public partial int LightStyle { get; set; }
	[NetworkVar("m_Radius")]
	public partial float Radius { get; set; }
	[NetworkVar("m_Exponent")]
	public partial int Exponent { get; set; }
	[NetworkVar("m_InnerAngle")]
	public partial float InnerAngle { get; set; }
	[NetworkVar("m_OuterAngle")]
	public partial float OuterAngle { get; set; }
	[NetworkVar("m_SpotRadius")]
	public partial float SpotRadius { get; set; }
}
