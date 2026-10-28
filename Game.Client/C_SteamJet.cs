using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CSteamJet")]
public partial class C_SteamJet : C_BaseParticleEntity
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
	public float SpreadSpeed;
	[NetworkVar("m_Speed")]
	public new float Speed;
	[NetworkVar("m_StartSize")]
	public float StartSize;
	[NetworkVar("m_EndSize")]
	public float EndSize;
	[NetworkVar("m_Rate")]
	public float Rate;
	[NetworkVar("m_JetLength")]
	public float JetLength;
	[NetworkVar("m_bEmit")]
	public bool Emit;
	[NetworkVar("m_bFaceLeft")]
	public bool FaceLeft;
	[NetworkVar("m_nType")]
	public int Type;
	[NetworkVar("m_spawnflags")]
	public int Spawnflags;
	[NetworkVar("m_flRollSpeed")]
	public float RollSpeed;
}
