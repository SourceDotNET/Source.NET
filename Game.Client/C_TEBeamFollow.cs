using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEBeamFollow")]
public partial class C_TEBeamFollow : C_BaseBeam
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(EntIndex)) { Bits = 24, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_iEntIndex")]
	public int EntIndex;
}

public static partial class TempEnts
{
	public static void TE_BeamFollow(IRecipientFilter filter, float delay, int entIndex, int modelIndex, int haloIndex, float life, float width, float endWidth, float fadeLength, float r, float g, float b, float a) {
		throw new NotImplementedException();
	}
}
