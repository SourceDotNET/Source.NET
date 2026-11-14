using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEArmorRicochet")]
public partial class TEArmorRicochet(ReadOnlySpan<char> name) : TEMetalSparks(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

public static partial class TempEnts
{
	static readonly TEArmorRicochet g_TEArmorRicochet = new("Armor Ricochet");

	public static void TE_ArmorRicochet<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, in Vector3 dir) where IRF : IRecipientFilter {
		g_TEArmorRicochet.Pos = pos;
		g_TEArmorRicochet.Dir = dir;

		g_TEArmorRicochet.Create(ref filter, delay);
	}
}
