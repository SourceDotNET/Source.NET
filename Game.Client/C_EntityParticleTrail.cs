using Source.Common;
using Source;

namespace Game.Client;

public partial class C_EntityParticleTrailInfo
{
	[NetworkTable("DT_EntityParticleTrailInfo")]
	public static readonly NetworkProp[] Props = [
		nameof(Lifetime),
		nameof(StartSize),
		nameof(EndSize),
	];

	[NetworkVar("m_flLifetime")]
	public float Lifetime;
	[NetworkVar("m_flStartSize")]
	public float StartSize;
	[NetworkVar("m_flEndSize")]
	public float EndSize;
}

[NetworkName("CEntityParticleTrail")]
public partial class C_EntityParticleTrail : C_BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(MaterialName)) { Bits = 10, Flags = PropFlags.Unsigned },
		new(nameof(Info)) { Encoding = NetEncoding.DataTable, DataTable = "DT_EntityParticleTrailInfo" },
		nameof(ConstraintEntity),
	];

	[NetworkVar("m_iMaterialName")]
	public int MaterialName;
	[NetworkVar("m_Info")]
	public C_EntityParticleTrailInfo Info = new();
	[NetworkVar("m_hConstraintEntity")]
	public EHANDLE ConstraintEntity;
}
