#if CLIENT_DLL || GAME_DLL
#if CLIENT_DLL
global using C_FuncLadder = Game.Shared.FuncLadder;
using InfoLadderDismountHandle = Source.Common.Handle<Game.Client.C_InfoLadderDismount>;
#else
using InfoLadderDismountHandle = Source.Common.Handle<Game.Server.InfoLadderDismount>;
#endif

using Game.Server;

using Source.Common;
using Source.GUI.Controls;

using System.Numerics;

namespace Game.Shared;

using FIELD = Source.FIELD<FuncLadder>;
[LinkEntityToClass("func_useableladder")]
[NetworkName("CFuncLadder")]
public partial class FuncLadder : BaseEntity
{
	static readonly List<FuncLadder> s_Ladders = [];

	public FuncLadder() {
		s_Ladders.Add(this);
	}

	public override void Term() {
		s_Ladders.Remove(this);
	}

	public static int GetLadderCount() => s_Ladders.Count;
	public static FuncLadder? GetLadder(int index) => (index < 0 || index >= s_Ladders.Count) ? null : s_Ladders[index];

	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_FuncLadder = new(DT_BaseEntity, [
#if CLIENT_DLL
			RecvPropVector(FIELD.OF(nameof(PlayerMountPositionTop))),
			RecvPropVector(FIELD.OF(nameof(PlayerMountPositionBottom))),
			RecvPropVector(FIELD.OF(nameof(LadderDir))),
			RecvPropBool(FIELD.OF(nameof(FakeLadder))),
#else
			SendPropVector(FIELD.OF(nameof(PlayerMountPositionTop)), 0, PropFlags.NoScale),
			SendPropVector(FIELD.OF(nameof(PlayerMountPositionBottom)), 0, PropFlags.NoScale),
			SendPropVector(FIELD.OF(nameof(LadderDir)), 0, PropFlags.NoScale),
			SendPropBool(FIELD.OF(nameof(FakeLadder))),
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_FuncLadder);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_FuncLadder);
#endif

	[NetworkName("m_vecPlayerMountPositionTop")]
	public Vector3 PlayerMountPositionTop;
	[NetworkName("m_vecPlayerMountPositionBottom")]
	public Vector3 PlayerMountPositionBottom;
	[NetworkName("m_vecLadderDir")]
	public Vector3 LadderDir;
	readonly List<InfoLadderDismountHandle> Dismounts = [];
	[NetworkName("m_bFakeLadder")]
	public bool FakeLadder;
	public bool Disabled;

	public void InputEnable(ref InputData inputdata) {
		Disabled = false;
	}

	public void InputDisable(ref InputData inputdata) {
		Disabled = true;
	}

	public void PlayerGotOn(BasePlayer player) {

	}

	public void PlayerGotOff(BasePlayer player) {

	}
	public bool DontGetOnLadder() => FakeLadder;
	public bool IsEnabled() => !Disabled;

	public void GetTopPosition(out Vector3 org) {
		ComputeAbsPosition(PlayerMountPositionTop + GetLocalOrigin(), out org);
	}
	public void GetBottomPosition(out Vector3 org) {
		ComputeAbsPosition(PlayerMountPositionBottom + GetLocalOrigin(), out org);
	}

	public void ComputeLadderDir(out Vector3 bottomToTop) {
		GetTopPosition(out Vector3 top);
		GetBottomPosition(out Vector3 bottom);

		bottomToTop = top - bottom;
	}

	public int GetDismountCount() {
		return Dismounts.Count;
	}

	public InfoLadderDismount? GetDismount(int index) => index < 0 || index >= Dismounts.Count ? null : Dismounts[index].Get();

	public void FindNearbyDismountPoints(in Vector3 origin, float radius, List<InfoLadderDismountHandle> list) {
#if !CLIENT_DLL
		// todo
#endif
	}
}
#endif
