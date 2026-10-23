using Game.Shared;

using Source.Common;

namespace Game.Server;

[LinkEntityToClass("func_reflective_glass")]
[NetworkName("CFuncReflectiveGlass")]
public partial class FuncReflectiveGlass : Breakable
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
