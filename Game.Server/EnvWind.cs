using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;

[LinkEntityToClass("env_wind")]
[NetworkName("CEnvWind", NoBase = true)]
public partial class EnvWind : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(EnvWindShared)) { Encoding = NetEncoding.DataTable, DataTable = "DT_EnvWindShared" },
	];

	[NetworkVar("m_EnvWindShared")]
	public readonly EnvWindShared EnvWindShared = new();

	public EnvWind() {
		EnvWindShared.Outer = this;
	}
}
