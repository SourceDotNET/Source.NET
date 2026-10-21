#if CLIENT_DLL || GAME_DLL

#if CLIENT_DLL
global using C_BaseGrenade = Game.Shared.BaseGrenade;
#endif

using Source.Common;

namespace Game.Shared;

[LinkEntityToClass("grenade")]
[NetworkName("CBaseGrenade")]
public partial class BaseGrenade : BaseProjectile
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Damage)) { Bits = 10, Flags = PropFlags.RoundDown, Low = 0, High = 256 },
		new(nameof(DmgRadius)) { Bits = 10, Flags = PropFlags.RoundDown, Low = 0, High = 1024 },
		nameof(IsLive),
		nameof(Thrower),
		new(nameof(Velocity)) { Encoding = NetEncoding.Vector, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Flags)) { Bits = 16, Flags = PropFlags.Unsigned },
	];

	public BaseGrenade() {
		Thrower = new();
	}

	[NetworkVar("m_flDamage")]
	public partial float Damage { get; set; }
	[NetworkVar("m_DmgRadius")]
	public partial float DmgRadius { get; set; }
	[NetworkVar("m_bIsLive")]
	public partial bool IsLive { get; set; }
	[NetworkVar("m_hThrower")]
	public partial EHANDLE Thrower { get; set; }
	[NetworkVar("m_fFlags")]
	public partial int Flags { get; set; }
}
#endif
