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

[LinkEntityToClass("func_useableladder")]
[NetworkName("CFuncLadder")]
public partial class FuncLadder : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(PlayerMountPositionTop)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(PlayerMountPositionBottom)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(LadderDir)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(FakeLadder),
	];

	static readonly List<FuncLadder> s_Ladders = [];

	public FuncLadder() {
		s_Ladders.Add(this);
	}

	public override void Term() {
		s_Ladders.Remove(this);
	}

	public static int GetLadderCount() => s_Ladders.Count;
	public static FuncLadder? GetLadder(int index) => (index < 0 || index >= s_Ladders.Count) ? null : s_Ladders[index];

	[NetworkVar("m_vecPlayerMountPositionTop")]
	public partial Vector3 PlayerMountPositionTop { get; set; }
	[NetworkVar("m_vecPlayerMountPositionBottom")]
	public partial Vector3 PlayerMountPositionBottom { get; set; }
	[NetworkVar("m_vecLadderDir")]
	public partial Vector3 LadderDir { get; set; }
	readonly List<InfoLadderDismountHandle> Dismounts = [];
	[NetworkVar("m_bFakeLadder")]
	public partial bool FakeLadder { get; set; }
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
