using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEArmorRicochet")]
public partial class C_TEArmorRicochet : C_TEMetalSparks
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

public static partial class TempEnts
{
	public static void TE_ArmorRicochet(IRecipientFilter filter, float delay, in Vector3 pos, in Vector3 dir) {
		throw new NotImplementedException();
	}
}
