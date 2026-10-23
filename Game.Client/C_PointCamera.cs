using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CPointCamera")]
public partial class C_PointCamera : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(FOV)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Resolution)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(FogEnable),
		new(nameof(FogColor)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(FogColorHDR)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(FogStart)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FogEnd)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FogMaxDensity)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(FogRadial),
		nameof(Active),
		nameof(UseScreenAspectRatio),
		nameof(GlobalOverride),
	];

	[NetworkVar("m_FOV")]
	public float FOV;
	[NetworkVar("m_Resolution")]
	public float Resolution;
	[NetworkVar("m_bFogEnable")]
	public bool FogEnable;
	[NetworkVar("m_FogColor")]
	public int FogColor;
	[NetworkVar("m_FogColorHDR")]
	public int FogColorHDR;
	[NetworkVar("m_flFogStart")]
	public float FogStart;
	[NetworkVar("m_flFogEnd")]
	public float FogEnd;
	[NetworkVar("m_flFogMaxDensity")]
	public float FogMaxDensity;
	[NetworkVar("m_bFogRadial")]
	public bool FogRadial;
	[NetworkVar("m_bActive")]
	public bool Active;
	[NetworkVar("m_bUseScreenAspectRatio")]
	public bool UseScreenAspectRatio;
	[NetworkVar("m_bGlobalOverride")]
	public bool GlobalOverride;
}
