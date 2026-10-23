using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CVGuiScreen")]
public partial class C_VGuiScreen : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Width)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Height)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ScreenFlags)) { Bits = 5, Flags = PropFlags.Unsigned },
		new(nameof(PanelName)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(AttachmentIndex)) { Bits = 5, Flags = PropFlags.Unsigned },
		new(nameof(OverlayMaterial)) { Bits = 10, Flags = PropFlags.Unsigned },
		nameof(HPlayerOwner),
	];

	[NetworkVar("m_flWidth")]
	public float Width;
	[NetworkVar("m_flHeight")]
	public float Height;
	[NetworkVar("m_fScreenFlags")]
	public int ScreenFlags;
	[NetworkVar("m_nPanelName")]
	public int PanelName;
	[NetworkVar("m_nAttachmentIndex")]
	public int AttachmentIndex;
	[NetworkVar("m_nOverlayMaterial")]
	public int OverlayMaterial;
	[NetworkVar("m_hPlayerOwner")]
	public EHANDLE HPlayerOwner = new();
}
