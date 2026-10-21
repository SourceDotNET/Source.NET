using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("vort_effect_dispel")]
[NetworkName("CVortigauntEffectDispel")]
public partial class VortigauntEffectDispel : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(FadeOut),
	];

	[NetworkVar("m_bFadeOut")]
	public partial bool FadeOut { get; set; }
}
