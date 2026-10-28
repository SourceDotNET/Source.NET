using Game.Shared;

using Source.Common;

namespace Game.Client;

[NetworkName("CInfoOverlayAccessor", NoBase = true)]
public partial class C_InfoOverlayAccessor : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(TextureFrameIndex)) { Encoding = NetEncoding.Int, Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(OverlayID)) { Bits = 32, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_iOverlayID")]
	public int OverlayID;
}
