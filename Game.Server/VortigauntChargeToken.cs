using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("vort_charge_token")]
[NetworkName("CVortigauntChargeToken")]
public partial class VortigauntChargeToken : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(FadeOut),
	];

	[NetworkVar("m_bFadeOut")]
	public partial bool FadeOut { get; set; }
}
