using Source.Common;
using Source;

using Game.Shared;

namespace Game.Client;


[NetworkName("CFuncConveyor")]
public partial class C_FuncConveyor : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ConveyorSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flConveyorSpeed")]
	public float ConveyorSpeed;
}
