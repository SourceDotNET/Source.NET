using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_steam")]
[LinkEntityToClass("env_steamjet")]
[NetworkName("CSteamJet")]
public partial class SteamJet : BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SpreadSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Speed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(StartSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(EndSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Rate)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(JetLength)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Emit),
		nameof(FaceLeft),
		new(nameof(Type)) { Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(Spawnflags)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(RollSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_SpreadSpeed")]
	public partial float SpreadSpeed { get; set; }
	[NetworkVar("m_Speed")]
	public new partial float Speed { get; set; }
	[NetworkVar("m_StartSize")]
	public partial float StartSize { get; set; }
	[NetworkVar("m_EndSize")]
	public partial float EndSize { get; set; }
	[NetworkVar("m_Rate")]
	public partial float Rate { get; set; }
	[NetworkVar("m_JetLength")]
	public partial float JetLength { get; set; }
	[NetworkVar("m_bEmit")]
	public partial bool Emit { get; set; }
	[NetworkVar("m_bFaceLeft")]
	public partial bool FaceLeft { get; set; }
	[NetworkVar("m_nType")]
	public partial int Type { get; set; }
	[NetworkVar("m_spawnflags")]
	public partial int Spawnflags { get; set; }
	[NetworkVar("m_flRollSpeed")]
	public partial float RollSpeed { get; set; }
}
