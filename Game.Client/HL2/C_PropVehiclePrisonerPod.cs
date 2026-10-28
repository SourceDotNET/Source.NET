using Game.Shared;

using Source.Common;

using System.Numerics;

namespace Game.Client.HL2;

[NetworkName("CPropVehiclePrisonerPod")]
public partial class C_PropVehiclePrisonerPod : C_PhysicsProp
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Player),
		nameof(EnterAnimOn),
		nameof(ExitAnimOn),
		new(nameof(EyeExitEndpoint)) { Bits = 0, Flags = PropFlags.Coord },
		nameof(LimitView),
		nameof(Locked),
	];

	[NetworkVar("m_hPlayer")]
	public EHANDLE Player = new();
	[NetworkVar("m_bEnterAnimOn")]
	public bool EnterAnimOn = new();
	[NetworkVar("m_bExitAnimOn")]
	public bool ExitAnimOn = new();
	[NetworkVar("m_vecEyeExitEndpoint")]
	public Vector3 EyeExitEndpoint = new();
	[NetworkVar("m_bLimitView")]
	public bool LimitView = new();
	[NetworkVar("m_bLocked")]
	public bool Locked = new();
}
