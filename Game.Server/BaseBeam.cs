using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBaseBeam", Table = "DT_BaseBeam", NoBase = true)]
public partial class BaseBeam(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(HaloIndex)) { Bits = 14 },
		new(nameof(StartFrame)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(FrameRate)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Life)) { Bits = 8, Low = 0.0f, High = 25.6f },
		new(nameof(Width)) { Bits = 10, Low = 0.0f, High = 128.0f },
		new(nameof(EndWidth)) { Bits = 10, Low = 0.0f, High = 128.0f },
		new(nameof(FadeLength)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Amplitude)) { Bits = 8, Low = 0.0f, High = 64.0f },
		new(nameof(Speed)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(R)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(G)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(B)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(A)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Flags)) { Bits = 20, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_nHaloIndex")]
	public int HaloIndex;
	[NetworkVar("m_nStartFrame")]
	public int StartFrame;
	[NetworkVar("m_nFrameRate")]
	public int FrameRate;
	[NetworkVar("m_fLife")]
	public float Life;
	[NetworkVar("m_fWidth")]
	public float Width;
	[NetworkVar("m_fEndWidth")]
	public float EndWidth;
	[NetworkVar("m_nFadeLength")]
	public int FadeLength;
	[NetworkVar("m_fAmplitude")]
	public float Amplitude;
	[NetworkVar("m_nSpeed")]
	public int Speed;
	[NetworkVar("r")]
	public int R;
	[NetworkVar("g")]
	public int G;
	[NetworkVar("b")]
	public int B;
	[NetworkVar("a")]
	public int A;
	[NetworkVar("m_nFlags")]
	public int Flags;
}
