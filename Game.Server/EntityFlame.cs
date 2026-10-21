using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("entityflame")]
[LinkEntityToClass("env_entity_igniter")]
[NetworkName("CEntityFlame")]
public partial class EntityFlame : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(EntAttached),
	];

	public EntityFlame() {
		EntAttached = new();
	}


	[NetworkVar("m_hEntAttached")]
	public partial EHANDLE EntAttached { get; set; }
}
