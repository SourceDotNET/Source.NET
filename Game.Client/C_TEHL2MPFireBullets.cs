using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;

using System.Numerics;
namespace Game.Client;
[NetworkName("CTEHL2MPFireBullets", NoBase = true)]
public partial class C_TEHL2MPFireBullets : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Dir)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(AmmoID)) { Bits = 5, Flags = PropFlags.Unsigned },
		new(nameof(Seed)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Shots)) { Bits = 5, Flags = PropFlags.Unsigned },
		new(nameof(Player)) { Bits = 6, Flags = PropFlags.Unsigned },
		new(nameof(Spread)) { Bits = 10, Low = 0, High = 1 },
		new(nameof(DoImpacts)) { Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(DoTracers)) { Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(TracerType)) { Encoding = NetEncoding.String },
		new(nameof(SpreadY)) { Bits = 10, Low = 0, High = 1 },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecDir")]
	public Vector3 Dir;
	[NetworkVar("m_iAmmoID")]
	public int AmmoID;
	[NetworkVar("m_iSeed")]
	public int Seed;
	[NetworkVar("m_iShots")]
	public int Shots;
	[NetworkVar("m_iPlayer")]
	public int Player;
	[NetworkVar("m_flSpread")]
	public float Spread;
	[NetworkVar("m_bDoImpacts")]
	public int DoImpacts;
	[NetworkVar("m_bDoTracers")]
	public int DoTracers;
	[NetworkVar("m_TracerType")]
	public InlineArray512<char> TracerType;
	[NetworkVar("m_flSpreadY")]
	public float SpreadY;
}
