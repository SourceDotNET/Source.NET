using Source.Common;
using Source;
using System.Numerics;

namespace Game.Client;

public partial class C_EnvHeadcrabCanisterShared
{
	[NetworkTable("DT_EnvHeadcrabCanisterShared")]
	public static readonly NetworkProp[] Props = [
		nameof(FlightSpeed),
		nameof(LaunchTime),
		nameof(ParabolaDirection),
		nameof(FlightTime),
		nameof(WorldEnterTime),
		nameof(InitialZSpeed),
		nameof(ZAcceleration),
		nameof(HorizSpeed),
		nameof(LaunchedFromWithinWorld),
		nameof(StartPosition),
		nameof(EnterWorldPosition),
		nameof(Direction),
		nameof(StartAngles),
		nameof(SkyboxOrigin),
		nameof(SkyboxScale),
		nameof(InSkybox),
	];

	[NetworkVar("m_flFlightSpeed")]
	public float FlightSpeed;
	[NetworkVar("m_flLaunchTime")]
	public TimeUnit_t LaunchTime;
	[NetworkVar("m_vecParabolaDirection")]
	public Vector3 ParabolaDirection;
	[NetworkVar("m_flFlightTime")]
	public float FlightTime;
	[NetworkVar("m_flWorldEnterTime")]
	public float WorldEnterTime;
	[NetworkVar("m_flInitialZSpeed")]
	public float InitialZSpeed;
	[NetworkVar("m_flZAcceleration")]
	public float ZAcceleration;
	[NetworkVar("m_flHorizSpeed")]
	public float HorizSpeed;
	[NetworkVar("m_bLaunchedFromWithinWorld")]
	public bool LaunchedFromWithinWorld;
	[NetworkVar("m_vecStartPosition")]
	public Vector3 StartPosition;
	[NetworkVar("m_vecEnterWorldPosition")]
	public Vector3 EnterWorldPosition;
	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;
	[NetworkVar("m_vecStartAngles")]
	public Vector3 StartAngles;
	[NetworkVar("m_vecSkyboxOrigin")]
	public Vector3 SkyboxOrigin;
	[NetworkVar("m_flSkyboxScale")]
	public float SkyboxScale;
	[NetworkVar("m_bInSkybox")]
	public bool InSkybox;
}

[NetworkName("CEnvHeadcrabCanister")]
public partial class C_EnvHeadcrabCanister : C_BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Shared)) { Encoding = NetEncoding.DataTable, DataTable = "DT_EnvHeadcrabCanisterShared" },
		nameof(Landed),
	];

	[NetworkVar("m_Shared")]
	public C_EnvHeadcrabCanisterShared Shared = new();
	[NetworkVar("m_bLanded")]
	public bool Landed;
}
