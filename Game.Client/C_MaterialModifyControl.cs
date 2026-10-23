using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CMaterialModifyControl")]
public partial class C_MaterialModifyControl : C_BaseEntity
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
	public InlineArray255<char> SzMaterialName;
	[NetworkVar("m_szMaterialVar")]
	public InlineArray255<char> SzMaterialVar;
	[NetworkVar("m_szMaterialVarValue")]
	public InlineArray255<char> SzMaterialVarValue;
	[NetworkVar("m_iFrameStart")]
	public int FrameStart;
	[NetworkVar("m_iFrameEnd")]
	public int FrameEnd;
	[NetworkVar("m_bWrap")]
	public bool Wrap;
	[NetworkVar("m_flFramerate")]
	public float Framerate;
	[NetworkVar("m_bNewAnimCommandsSemaphore")]
	public bool NewAnimCommandsSemaphore;
	[NetworkVar("m_flFloatLerpStartValue")]
	public float FloatLerpStartValue;
	[NetworkVar("m_flFloatLerpEndValue")]
	public float FloatLerpEndValue;
	[NetworkVar("m_flFloatLerpTransitionTime")]
	public float FloatLerpTransitionTime;
	[NetworkVar("m_nModifyMode")]
	public int ModifyMode;
}
