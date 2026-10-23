using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CEntityFlame")]
public partial class C_EntityFlame : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(EntAttached),
	];

	[NetworkVar("m_hEntAttached")]
	public EHANDLE EntAttached = new();
}
