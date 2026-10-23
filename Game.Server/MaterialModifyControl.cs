using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("material_modify_control")]
[NetworkName("CMaterialModifyControl")]
public partial class MaterialModifyControl : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SzMaterialName)) { Encoding = NetEncoding.String },
		new(nameof(SzMaterialVar)) { Encoding = NetEncoding.String },
		new(nameof(SzMaterialVarValue)) { Encoding = NetEncoding.String },
		new(nameof(FrameStart)) { Bits = 8 },
		new(nameof(FrameEnd)) { Bits = 8 },
		nameof(Wrap),
		new(nameof(Framerate)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(NewAnimCommandsSemaphore),
		new(nameof(FloatLerpStartValue)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FloatLerpEndValue)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FloatLerpTransitionTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ModifyMode)) { Bits = 2, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_szMaterialName")]
	public partial ref readonly InlineArray255<char> SzMaterialName { get; }
	[NetworkVar("m_szMaterialVar")]
	public partial ref readonly InlineArray255<char> SzMaterialVar { get; }
	[NetworkVar("m_szMaterialVarValue")]
	public partial ref readonly InlineArray255<char> SzMaterialVarValue { get; }
	[NetworkVar("m_iFrameStart")]
	public partial int FrameStart { get; set; }
	[NetworkVar("m_iFrameEnd")]
	public partial int FrameEnd { get; set; }
	[NetworkVar("m_bWrap")]
	public partial bool Wrap { get; set; }
	[NetworkVar("m_flFramerate")]
	public partial float Framerate { get; set; }
	[NetworkVar("m_bNewAnimCommandsSemaphore")]
	public partial bool NewAnimCommandsSemaphore { get; set; }
	[NetworkVar("m_flFloatLerpStartValue")]
	public partial float FloatLerpStartValue { get; set; }
	[NetworkVar("m_flFloatLerpEndValue")]
	public partial float FloatLerpEndValue { get; set; }
	[NetworkVar("m_flFloatLerpTransitionTime")]
	public partial float FloatLerpTransitionTime { get; set; }
	[NetworkVar("m_nModifyMode")]
	public partial int ModifyMode { get; set; }
}
