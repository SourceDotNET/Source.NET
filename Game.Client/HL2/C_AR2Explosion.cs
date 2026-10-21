using Game.Shared;

using Source;
using Source.Common;

namespace Game.Client.HL2;

[NetworkName("AR2Explosion", Table = "DT_AR2Explosion")]
public partial class C_AR2Explosion : C_BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(MaterialName),
	];

	[NetworkVar("m_szMaterialName")]
	InlineArray255<char> MaterialName;
}
