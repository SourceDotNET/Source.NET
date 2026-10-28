using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("point_camera")]
[NetworkName("CPointCamera")]
public partial class PointCamera : BaseEntity
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
	public partial float FOV { get; set; }
	[NetworkVar("m_Resolution")]
	public partial float Resolution { get; set; }
	[NetworkVar("m_bFogEnable")]
	public partial bool FogEnable { get; set; }
	[NetworkVar("m_FogColor")]
	public partial int FogColor { get; set; }
	[NetworkVar("m_FogColorHDR")]
	public partial int FogColorHDR { get; set; }
	[NetworkVar("m_flFogStart")]
	public partial float FogStart { get; set; }
	[NetworkVar("m_flFogEnd")]
	public partial float FogEnd { get; set; }
	[NetworkVar("m_flFogMaxDensity")]
	public partial float FogMaxDensity { get; set; }
	[NetworkVar("m_bFogRadial")]
	public partial bool FogRadial { get; set; }
	[NetworkVar("m_bActive")]
	public partial bool Active { get; set; }
	[NetworkVar("m_bUseScreenAspectRatio")]
	public partial bool UseScreenAspectRatio { get; set; }
	[NetworkVar("m_bGlobalOverride")]
	public partial bool GlobalOverride { get; set; }
}
