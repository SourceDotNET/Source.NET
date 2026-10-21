using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;
namespace Game.Client;

[NetworkName("CFunc_Dust", NoBase = true)]
public partial class C_Func_Dust : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Color)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(SpawnRate)) { Bits = 12, Flags = PropFlags.Unsigned },
		new(nameof(SizeMin)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SizeMax)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(LifetimeMin)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(LifetimeMax)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(DustFlags)) { Bits = 3, Flags = PropFlags.Unsigned },
		new(nameof(SpeedMax)) { Bits = 12, Flags = PropFlags.Unsigned },
		new(nameof(DistMax)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(ModelIndex)) { Encoding = NetEncoding.ModelIndex },
		new(nameof(FallSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(AffectedByWind),
		new(nameof(Collision)) { Encoding = NetEncoding.DataTable, DataTable = "DT_CollisionProperty" },
	];

	[NetworkVar("m_Color")]
	public Color Color;
	[NetworkVar("m_SpawnRate")]
	public int SpawnRate;
	[NetworkVar("m_flSizeMin")]
	public float SizeMin;
	[NetworkVar("m_flSizeMax")]
	public float SizeMax;
	[NetworkVar("m_LifetimeMin")]
	public int LifetimeMin;
	[NetworkVar("m_LifetimeMax")]
	public int LifetimeMax;
	[NetworkVar("m_DustFlags")]
	public int DustFlags;
	[NetworkVar("m_SpeedMax")]
	public int SpeedMax;
	[NetworkVar("m_DistMax")]
	public int DistMax;
	[NetworkVar("m_FallSpeed")]
	public float FallSpeed;
	[NetworkVar("m_bAffectedByWind")]
	public bool AffectedByWind;
}

public static partial class TempEnts
{
	public static void TE_Dust(IRecipientFilter filter, float delay, in Vector3 pos, in Vector3 dir, float size, float speed) {
		throw new NotImplementedException();
	}
}
