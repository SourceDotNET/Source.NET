using Game.Shared;

using Source;
using Source.Common;

namespace Game.Server;
[LinkEntityToClass("info_overlay_accessor")]
[NetworkName("CInfoOverlayAccessor", NoBase = true)]
public partial class InfoOverlayAccessor : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(TextureFrameIndex)) { Encoding = NetEncoding.Int, Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(OverlayID)) { Bits = 32, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_iOverlayID")]
	public partial int OverlayID { get; set; }
}
