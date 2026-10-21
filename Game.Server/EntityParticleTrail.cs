using Game.Shared;
using Source.Common;
using Source;

namespace Game.Server;

public partial class EntityParticleTrailInfo
{
	[NetworkTable("DT_EntityParticleTrailInfo")]
	public static readonly NetworkProp[] Props = [
		new(nameof(Lifetime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(StartSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(EndSize)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	public BaseEntity? Outer;
	public void NetworkStateChanged() => Outer?.NetworkStateChanged();
	public void NetworkStateChanged(IFieldAccessor field) => Outer?.NetworkStateChanged();

	[NetworkVar("m_flLifetime")]
	public partial float Lifetime { get; set; }
	[NetworkVar("m_flStartSize")]
	public partial float StartSize { get; set; }
	[NetworkVar("m_flEndSize")]
	public partial float EndSize { get; set; }
}

// Datatable-accurate stub (gmod DT_EntityParticleTrail, baseclass DT_BaseParticleEntity).
[LinkEntityToClass("env_particle_trail")]
[NetworkName("CEntityParticleTrail")]
public partial class EntityParticleTrail : BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(MaterialName)) { Bits = 10, Flags = PropFlags.Unsigned },
		new(nameof(Info)) { Encoding = NetEncoding.DataTable, DataTable = "DT_EntityParticleTrailInfo" },
		nameof(ConstraintEntity),
	];

	[NetworkVar("m_iMaterialName")]
	public partial int MaterialName { get; set; }
	[NetworkVar("m_Info")]
	public readonly EntityParticleTrailInfo Info = new();
	[NetworkVar("m_hConstraintEntity")]
	public partial EHANDLE ConstraintEntity { get; set; }

	public EntityParticleTrail() {
		Info.Outer = this;
	}
}
