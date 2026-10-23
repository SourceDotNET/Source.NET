using Source.Common;
using Source;

using Game.Shared;

namespace Game.Server;


[LinkEntityToClass("func_conveyor")]
[NetworkName("CFuncConveyor")]
public partial class FuncConveyor : FuncWall
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ConveyorSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flConveyorSpeed")]
	public partial float ConveyorSpeed { get; set; }
}
