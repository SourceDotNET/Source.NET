using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CVortigauntChargeToken")]
public partial class C_VortigauntChargeToken : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(FadeOut),
	];

	[NetworkVar("m_bFadeOut")]
	public bool FadeOut;
}
