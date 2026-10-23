using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEBeamRing")]
public partial class C_TEBeamRing : C_BaseBeam
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartEntity)) { Bits = 13, Flags = PropFlags.Unsigned },
		new(nameof(EndEntity)) { Bits = 13, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nStartEntity")]
	public int StartEntity;
	[NetworkVar("m_nEndEntity")]
	public int EndEntity;
}

public static partial class TempEnts
{
	public static void TE_BeamRing(IRecipientFilter filter, float delay, int start, int end, int modelIndex, int haloIndex, int startFrame, int frameRate, float life, float width, int spread, float amplitude, int r, int g, int b, int a, int speed, int flags = 0) {
		throw new NotImplementedException();
	}
}
