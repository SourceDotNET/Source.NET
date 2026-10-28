using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CEnvScreenEffect")]
public partial class C_EnvScreenEffect : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Duration)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Type)) { Bits = 12, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_flDuration")]
	public float Duration;
	[NetworkVar("m_nType")]
	public int Type;
}
