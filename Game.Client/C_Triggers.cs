using Game.Shared;

using Source.Common;
using Source;

namespace Game.Client;

// I don't know if this is correct, but they have a datatable, so...

[NetworkName("CBaseTrigger")]
public partial class C_BaseTrigger : BaseToggle
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(ClientSidePredicted),
		new(nameof(SpawnFlags)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_bClientSidePredicted")]
	public bool ClientSidePredicted;
}
