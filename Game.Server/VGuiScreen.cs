using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("vgui_screen")]
[LinkEntityToClass("vgui_screen_team")]
[NetworkName("CVGuiScreen")]
public partial class VGuiScreen : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Width)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Height)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(AttachmentIndex)) { Bits = 5, Flags = PropFlags.Unsigned },
		new(nameof(PanelName)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(ScreenFlags)) { Bits = 5, Flags = PropFlags.Unsigned },
		new(nameof(OverlayMaterial)) { Bits = 10, Flags = PropFlags.Unsigned },
		nameof(HPlayerOwner),
	];

	public VGuiScreen() {
		HPlayerOwner = new();
	}


	[NetworkVar("m_flWidth")]
	public partial float Width { get; set; }
	[NetworkVar("m_flHeight")]
	public partial float Height { get; set; }
	[NetworkVar("m_nAttachmentIndex")]
	public partial int AttachmentIndex { get; set; }
	[NetworkVar("m_nPanelName")]
	public partial int PanelName { get; set; }
	[NetworkVar("m_fScreenFlags")]
	public partial int ScreenFlags { get; set; }
	[NetworkVar("m_nOverlayMaterial")]
	public partial int OverlayMaterial { get; set; }
	[NetworkVar("m_hPlayerOwner")]
	public partial EHANDLE HPlayerOwner { get; set; }
}
