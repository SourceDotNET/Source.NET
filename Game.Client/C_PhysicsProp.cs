using Game.Client;
using Game.Shared;

using Source.Common;

namespace Game.Client;

[NetworkName("CPhysicsProp")]
public partial class C_PhysicsProp : C_BreakableProp
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Awake),
	];

	[NetworkVar("m_bAwake")]
	public bool Awake;
}
