namespace Game.Server.HL2;

using Game.Shared;
using Game.Shared.HL2;

using Source.Common;

using System.Numerics;

public partial class HL2PlayerLocalData
{
	[NetworkTable("DT_HL2Local")]
	public static readonly NetworkProp[] Props = [
		new(nameof(SuitPower)) { Bits = 10, Flags = PropFlags.Unsigned | PropFlags.RoundUp, Low = 0.0f, High = 100.0f },
		new(nameof(Zooming)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(BitsActiveDevices)) { Bits = SharedDefs.MAX_SUIT_DEVICES, Flags = PropFlags.Unsigned },
		new(nameof(SquadMemberCount)) { Bits = 5, Flags = PropFlags.Unsigned },
		new(nameof(SquadMedicCount)) { Bits = 5, Flags = PropFlags.Unsigned },
		nameof(SquadInFollowMode),
		nameof(WeaponLowered),
		nameof(Ladder),
		nameof(DisplayReticle),
	];

	public BaseEntity? Outer;
	public void NetworkStateChanged() => Outer?.NetworkStateChanged();
	public void NetworkStateChanged(IFieldAccessor field) => Outer?.NetworkStateChanged();

	[NetworkVar("m_flSuitPower")]
	public partial float SuitPower { get; set; }
	[NetworkVar("m_bZooming")]
	public partial bool Zooming { get; set; }
	[NetworkVar("m_bitsActiveDevices")]
	public partial int BitsActiveDevices { get; set; }
	[NetworkVar("m_iSquadMemberCount")]
	public partial int SquadMemberCount { get; set; }
	[NetworkVar("m_iSquadMedicCount")]
	public partial int SquadMedicCount { get; set; }
	[NetworkVar("m_fSquadInFollowMode")]
	public partial bool SquadInFollowMode { get; set; }
	[NetworkVar("m_bWeaponLowered")]
	public partial bool WeaponLowered { get; set; }
	[NetworkVar("m_hLadder")]
	public partial EHANDLE Ladder { get; set; }
	public EHANDLE AutoAimTargetHandle = new();
	public Vector3 AutoAimPoint;
	[NetworkVar("m_bDisplayReticle")]
	public partial bool DisplayReticle { get; set; }
	public bool StickyAutoAim;
	public bool AutoAimTarget;
#if HL2_EPISODIC
	public float FlashBattery;
	public Vector3 LocatorOrigin;
#endif
	public LadderMove LadderMove = new();

	public HL2PlayerLocalData() {
		Ladder = new();
	}
}
