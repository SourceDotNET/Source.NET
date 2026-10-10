using Game.Shared;

using Source.Common;

using System.Numerics;

namespace Game.Server;
using FIELD = Source.FIELD<PropVehicleDriveable>;

[LinkEntityToClass("prop_vehicle_driveable")]
[NetworkName("CPropVehicleDriveable")]
public class PropVehicleDriveable : BaseAnimating
{
	public static readonly SendTable DT_PropVehicleDriveable = new(DT_BaseAnimating, [
		SendPropEHandle(FIELD.OF(nameof(Player))),
		SendPropInt(FIELD.OF(nameof(Speed)), 8),
		SendPropInt(FIELD.OF(nameof(RPM)), 13),
		SendPropFloat(FIELD.OF(nameof(Throttle)), 0, PropFlags.NoScale),
		SendPropInt(FIELD.OF(nameof(BoostTimeLeft)), 8),
		SendPropBool(FIELD.OF(nameof(HasBoost))),
		SendPropBool(FIELD.OF(nameof(EnterAnimOn))),
		SendPropBool(FIELD.OF(nameof(ExitAnimOn))),
		SendPropBool(FIELD.OF(nameof(UnableToFire))),
		SendPropVector(FIELD.OF(nameof(EyeExitEndpoint)), 0, PropFlags.Coord),
		SendPropBool(FIELD.OF(nameof(HasGun))),
		SendPropVector(FIELD.OF(nameof(GunCrosshair)), 0, PropFlags.Coord),
		SendPropBool(FIELD.OF(nameof(Locked))),
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_PropVehicleDriveable);

	[NetworkName("m_hPlayer")]
	public EHANDLE Player = new();
	[NetworkName("m_nSpeed")]
	public new int Speed;
	[NetworkName("m_nRPM")]
	public int RPM;
	[NetworkName("m_flThrottle")]
	public float Throttle;
	[NetworkName("m_nBoostTimeLeft")]
	public int BoostTimeLeft;
	[NetworkName("m_nHasBoost")]
	public bool HasBoost;
	public bool ScannerDisabledWeapons;
	public bool ScannerDisabledVehicle;
	[NetworkName("m_bEnterAnimOn")]
	public bool EnterAnimOn;
	[NetworkName("m_bExitAnimOn")]
	public bool ExitAnimOn;
	[NetworkName("m_bUnableToFire")]
	public bool UnableToFire;
	[NetworkName("m_vecEyeExitEndpoint")]
	public Vector3 EyeExitEndpoint;
	[NetworkName("m_bHasGun")]
	public bool HasGun;
	[NetworkName("m_vecGunCrosshair")]
	public Vector3 GunCrosshair;
	[NetworkName("m_bLocked")]
	public bool Locked;

	public override bool IsVehicle() => true;
}
