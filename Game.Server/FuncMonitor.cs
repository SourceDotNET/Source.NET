using Game.Shared;

using Source.Common;

namespace Game.Server;

[LinkEntityToClass("func_monitor")]
[NetworkName("CFuncMonitor")]
public partial class FuncMonitor : FuncBrush
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
