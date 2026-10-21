#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
using Game.Shared;
using Source;
using System.Numerics;

#if CLIENT_DLL
namespace Game.Client;
#else
namespace Game.Server;
#endif

[LinkEntityToClass("physgun_beam")]
[NetworkName("CPhysBeam")]
public partial class
#if CLIENT_DLL
    C_PhysBeam 
#else
	PhysBeam
#endif
	: BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(TargetEnt),
		new(nameof(HoldPos)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(IsOn),
		new(nameof(PhysBone)) { Bits = 12 },
	];

#if CLIENT_DLL
	public C_PhysBeam() {
		TargetEnt = new();
	}
#else
	public PhysBeam() {
		TargetEnt = new();
	}
#endif

	[NetworkVar("m_hTargetEnt")]
	public partial EHANDLE TargetEnt { get; set; }
	[NetworkVar("m_HoldPos")]
	public partial Vector3 HoldPos { get; set; }
	[NetworkVar("m_bIsOn")]
	public partial bool IsOn { get; set; }
	[NetworkVar("m_iPhysBone")]
	public partial int PhysBone { get; set; }
}
#endif
