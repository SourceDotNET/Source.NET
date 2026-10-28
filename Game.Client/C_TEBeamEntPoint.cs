using Source.Common;
using Source;

using Game.Shared;

using System.Numerics;
namespace Game.Client;

[NetworkName("CTEBeamEntPoint")]
public partial class C_TEBeamEntPoint : C_BaseBeam
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartEntity)) { Bits = 24, Flags = PropFlags.Unsigned },
		new(nameof(EndEntity)) { Bits = 24, Flags = PropFlags.Unsigned },
		new(nameof(StartPoint)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(EndPoint)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_nStartEntity")]
	public int StartEntity;
	[NetworkVar("m_nEndEntity")]
	public int EndEntity;
	[NetworkVar("m_vecStartPoint")]
	public Vector3 StartPoint;
	[NetworkVar("m_vecEndPoint")]
	public Vector3 EndPoint;

	public override void PostDataUpdate(DataUpdateType updateType) {
		beams.CreateBeamEntPoint(StartEntity, in StartPoint, EndEntity, in EndPoint, ModelIndex, HaloIndex, 0.0f, Life, Width, EndWidth, FadeLength, Amplitude, A, 0.1f * Speed, StartFrame, 0.1f * FrameRate, R, G, B);
	}
}

public static partial class TempEnts
{
	public static void TE_BeamEntPoint(IRecipientFilter filter, float delay, int startEntity, in Vector3 start, int endEntity, in Vector3 end, int modelIndex, int haloIndex, int startFrame, int frameRate, float life, float width, float endWidth, int fadeLength, float amplitude, int r, int g, int b, int a, int speed) {
		throw new NotImplementedException();
	}
}
