using Game.Server;
using Game.Shared;

using Source.Common;

using System.Numerics;

namespace Game.Server.HL2;
using FIELD = Source.FIELD<PropVehiclePrisonerPod>;
[LinkEntityToClass("prop_vehicle_prisoner_pod")]
[NetworkName("CPropVehiclePrisonerPod")]
public class PropVehiclePrisonerPod : PhysicsProp
{
	public static readonly SendTable DT_PropVehiclePrisonerPod = new(DT_PhysicsProp, [
		SendPropEHandle(FIELD.OF(nameof(Player))),
		SendPropBool(FIELD.OF(nameof(EnterAnimOn))),
		SendPropBool(FIELD.OF(nameof(ExitAnimOn))),
		SendPropVector(FIELD.OF(nameof(EyeExitEndpoint)), 0, PropFlags.Coord),
		SendPropBool(FIELD.OF(nameof(LimitView))),
		SendPropBool(FIELD.OF(nameof(Locked))),
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_PropVehiclePrisonerPod);

	[NetworkName("m_hPlayer")]
	public EHANDLE Player = new();
	[NetworkName("m_bEnterAnimOn")]
	public bool EnterAnimOn = new();
	[NetworkName("m_bExitAnimOn")]
	public bool ExitAnimOn = new();
	[NetworkName("m_vecEyeExitEndpoint")]
	public Vector3 EyeExitEndpoint = new();
	[NetworkName("m_bLimitView")]
	public bool LimitView = new();
	[NetworkName("m_bLocked")]
	public bool Locked;

	public override bool IsVehicle() => true;
}
