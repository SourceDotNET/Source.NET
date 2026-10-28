using Game.Shared;

using Source;
using Source.Common;

namespace Game.Server.HL2;
[LinkEntityToClass("ar2explosion")]
[NetworkName("AR2Explosion", Table = "DT_AR2Explosion")]
public partial class AR2Explosion : BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(MaterialName),
	];

	[NetworkVar("m_szMaterialName")]
	partial ref readonly InlineArray255<char> MaterialName { get; }
}
