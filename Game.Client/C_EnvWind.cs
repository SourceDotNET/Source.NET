using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;

[NetworkName("CEnvWind")]
public partial class C_EnvWind : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(EnvWindShared)) { Encoding = NetEncoding.DataTable, DataTable = "DT_EnvWindShared" },
	];

	[NetworkVar("m_EnvWindShared")]
	public readonly EnvWindShared EnvWindShared = new();
}
