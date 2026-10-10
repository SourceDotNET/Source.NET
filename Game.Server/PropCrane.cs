using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
using FIELD = FIELD<PropCrane>;
[LinkEntityToClass("prop_vehicle_crane")]
[NetworkName("CPropCrane")]
public class PropCrane : BaseAnimating
{
	public static readonly SendTable DT_PropCrane = new(DT_BaseAnimating, [
		SendPropEHandle(FIELD.OF(nameof(Player))),
		SendPropBool(FIELD.OF(nameof(MagnetOn))),
		SendPropBool(FIELD.OF(nameof(EnterAnimOn))),
		SendPropBool(FIELD.OF(nameof(ExitAnimOn))),
		SendPropVector(FIELD.OF(nameof(EyeExitEndpoint)), 0, PropFlags.Coord),
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_PropCrane);

	[NetworkName("m_hPlayer")]
	public EHANDLE Player = new();
	[NetworkName("m_bMagnetOn")]
	public bool MagnetOn;
	[NetworkName("m_bEnterAnimOn")]
	public bool EnterAnimOn;
	[NetworkName("m_bExitAnimOn")]
	public bool ExitAnimOn;
	[NetworkName("m_vecEyeExitEndpoint")]
	public Vector3 EyeExitEndpoint;

	public override bool IsVehicle() => true;
}
