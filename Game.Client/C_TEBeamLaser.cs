using Source.Common;
using Source;

using Game.Shared;

namespace Game.Client;

[NetworkName("CTEBeamLaser")]
public partial class C_TEBeamLaser : C_BaseBeam
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartEntity)) { Bits = 24, Flags = PropFlags.Unsigned },
		new(nameof(EndEntity)) { Bits = 24, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nStartEntity")]
	public int StartEntity;
	[NetworkVar("m_nEndEntity")]
	public int EndEntity;

	public override void PostDataUpdate(DataUpdateType updateType) {
		beams.CreateBeamEnts(StartEntity, EndEntity, ModelIndex, HaloIndex, 0.0f, Life, Width, EndWidth, FadeLength, Amplitude, A, 0.1f * Speed, StartFrame, 0.1f * FrameRate, R, G, B, (int)TempEntType.BeamLaser);
	}
}

public static partial class TempEnts
{
	public static void TE_BeamLaser(IRecipientFilter filter, float delay, int start, int end, int modelIndex, int haloIndex, int startFrame, int frameRate, float life, float width, float endWidth, int fadeLength, float amplitude, int r, int g, int b, int a, int speed) {
		throw new NotImplementedException();
	}
}
