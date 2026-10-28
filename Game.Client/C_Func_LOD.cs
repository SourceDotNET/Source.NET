using Source.Common;
using Source;

using Game.Shared;

namespace Game.Client;


[NetworkName("CFunc_LOD")]
public partial class C_Func_LOD : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(DisappearMinDist)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DisappearMaxDist)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_fDisappearMinDist")]
	public float DisappearMinDist = 5000.0f;
	[NetworkVar("m_fDisappearMaxDist")]
	public float DisappearMaxDist = 5800.0f;
}
