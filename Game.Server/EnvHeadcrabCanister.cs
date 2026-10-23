using Game.Shared;
using Source.Common;
using Source;
using System.Numerics;

namespace Game.Server;

public partial class EnvHeadcrabCanisterShared
{
	[NetworkTable("DT_EnvHeadcrabCanisterShared")]
	public static readonly NetworkProp[] Props = [
		new(nameof(FlightSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(LaunchTime),
		new(nameof(ParabolaDirection)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FlightTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(WorldEnterTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(InitialZSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ZAcceleration)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(HorizSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(LaunchedFromWithinWorld),
		new(nameof(StartPosition)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(EnterWorldPosition)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Direction)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(StartAngles)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SkyboxOrigin)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SkyboxScale)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(InSkybox),
	];

	public BaseEntity? Outer;
	public void NetworkStateChanged() => Outer?.NetworkStateChanged();
	public void NetworkStateChanged(IFieldAccessor field) => Outer?.NetworkStateChanged();

	[NetworkVar("m_flFlightSpeed")]
	public partial float FlightSpeed { get; set; }
	[NetworkVar("m_flLaunchTime")]
	public partial TimeUnit_t LaunchTime { get; set; }
	[NetworkVar("m_vecParabolaDirection")]
	public partial Vector3 ParabolaDirection { get; set; }
	[NetworkVar("m_flFlightTime")]
	public partial float FlightTime { get; set; }
	[NetworkVar("m_flWorldEnterTime")]
	public partial float WorldEnterTime { get; set; }
	[NetworkVar("m_flInitialZSpeed")]
	public partial float InitialZSpeed { get; set; }
	[NetworkVar("m_flZAcceleration")]
	public partial float ZAcceleration { get; set; }
	[NetworkVar("m_flHorizSpeed")]
	public partial float HorizSpeed { get; set; }
	[NetworkVar("m_bLaunchedFromWithinWorld")]
	public partial bool LaunchedFromWithinWorld { get; set; }
	[NetworkVar("m_vecStartPosition")]
	public partial Vector3 StartPosition { get; set; }
	[NetworkVar("m_vecEnterWorldPosition")]
	public partial Vector3 EnterWorldPosition { get; set; }
	[NetworkVar("m_vecDirection")]
	public partial Vector3 Direction { get; set; }
	[NetworkVar("m_vecStartAngles")]
	public partial Vector3 StartAngles { get; set; }
	[NetworkVar("m_vecSkyboxOrigin")]
	public partial Vector3 SkyboxOrigin { get; set; }
	[NetworkVar("m_flSkyboxScale")]
	public partial float SkyboxScale { get; set; }
	[NetworkVar("m_bInSkybox")]
	public partial bool InSkybox { get; set; }
}

[LinkEntityToClass("env_headcrabcanister")]
[NetworkName("CEnvHeadcrabCanister")]
public partial class EnvHeadcrabCanister : BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Shared)) { Encoding = NetEncoding.DataTable, DataTable = "DT_EnvHeadcrabCanisterShared" },
		nameof(Landed),
	];

	[NetworkVar("m_Shared")]
	public readonly EnvHeadcrabCanisterShared Shared = new();
	[NetworkVar("m_bLanded")]
	public partial bool Landed { get; set; }

	public EnvHeadcrabCanister() {
		Shared.Outer = this;
	}
}
