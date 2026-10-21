#if CLIENT_DLL || GAME_DLL

using Source;
using Source.Common;
using Source.Common.Networking;

using System.Numerics;
namespace Game.Shared;

public enum BeamTypes
{
	Points,
	EntPoint,
	Ents,
	Hose,
	Spline,
	Laser,
	NumTypes
}


[LinkEntityToClass("beam")]
[NetworkName("CBeam")]
public partial class
#if CLIENT_DLL
	C_Beam
#else
	Beam
#endif
	: BaseEntity
{
	public const int ATTACHMENT_INDEX_BITS = 5;
	public const float MAX_BEAM_WIDTH = 102.3f;
	public const float MAX_BEAM_SCROLLSPEED = 100f;
	public const float MAX_BEAM_NOISEAMPLITUDE = 64;

	[NetworkTable("DT_BeamPredictableId")]
	public static readonly NetworkProp[] BeamPredictableIdProps = [
#if CLIENT_DLL
		new(nameof(PredictableID)) { Encoding = NetEncoding.PredictableId },
#else
		new(nameof(PredictableId)) { Encoding = NetEncoding.PredictableId },
#endif
		new(nameof(b_IsPlayerSimulated)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
	];

	[NetworkTable(NoBase = true)]
	public static readonly new NetworkProp[] Props = [
		new(nameof(BeamType)) { Bits = 3, Flags = PropFlags.Unsigned },
		new(nameof(BeamFlags)) { Bits = (int)Shared.BeamFlags.NumFlags, Flags = PropFlags.Unsigned },
		new(nameof(NumBeamEnts)) { Bits = 5, Flags = PropFlags.Unsigned },
		nameof(AttachEntity),
		new(nameof(AttachIndex)) { Bits = ATTACHMENT_INDEX_BITS, Flags = PropFlags.Unsigned },
		new(nameof(HaloIndex)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(HaloScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Width)) { Bits = 10, Flags = PropFlags.RoundUp, Low = 0.0f, High = MAX_BEAM_WIDTH },
		new(nameof(EndWidth)) { Bits = 10, Flags = PropFlags.RoundUp, Low = 0.0f, High = MAX_BEAM_WIDTH },
		new(nameof(FadeLength)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Amplitude)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0.0f, High = MAX_BEAM_NOISEAMPLITUDE },
		new(nameof(StartFrame)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0.0f, High = 256.0f },
		new(nameof(Speed)) { Bits = 8, Flags = PropFlags.NoScale, Low = 0.0f, High = MAX_BEAM_SCROLLSPEED, Proxy = typeof(BeamScrollSpeedProxy) },
		new(nameof(RenderFX)) { Bits = 8, Flags = PropFlags.Unsigned, Side = NetworkSide.Server },
		new(nameof(RenderMode)) { Bits = 8, Flags = PropFlags.Unsigned, Side = NetworkSide.Server },
		new(nameof(FrameRate)) { Bits = 10, Flags = PropFlags.RoundUp, Low = -25.0f, High = 25.0f },
		new(nameof(HDRColorScale)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
		new(nameof(RenderColor)) { Encoding = NetEncoding.Int, Side = NetworkSide.Client },
		new(nameof(RenderFX)) { Side = NetworkSide.Client },
		new(nameof(RenderMode)) { Side = NetworkSide.Client },
		new(nameof(Frame)) { Bits = 20, Flags = PropFlags.RoundDown | PropFlags.ChangesOften, Low = 0.0f, High = 256.0f },
		new(nameof(RenderColor)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned | PropFlags.ChangesOften, Side = NetworkSide.Server },
		new(nameof(EndPos)) { Bits = -1, Flags = PropFlags.Coord },

		new(nameof(ModelIndex)) { Encoding = NetEncoding.ModelIndex },
		new(nameof(MinDXLevel)) { Side = NetworkSide.Client },
		new(nameof(Origin)) { Bits = 19, Flags = PropFlags.ChangesOften, Low = WorldSize.MIN_COORD_INTEGER, High = WorldSize.MAX_COORD_INTEGER },
#if CLIENT_DLL
		new(nameof(NetworkMoveParent)) { Encoding = NetEncoding.Int, Proxy = typeof(MoveParentProxy) },
#else
		nameof(MoveParent),
#endif
		new(nameof(MinDXLevel)) { Bits = 8, Flags = PropFlags.Unsigned, Side = NetworkSide.Server },

#if CLIENT_DLL
		new(nameof(BeamPredictableIdProps)) { Name = "beampredictable_id" },
#else
		new(nameof(BeamPredictableIdProps)) { Name = "beampredictable_id", Proxy = typeof(BaseEntity.PredictableIdProxy) },
#endif
	];

	public static class BeamScrollSpeedProxy
	{
#if CLIENT_DLL
		public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
			C_Beam beam = (C_Beam)instance;
			float val = data.Value.Float * 0.1f;
			beam.Speed = val;
		}
#endif
	}

	[NetworkVar("m_hAttachEntity")]
	public partial ref readonly InlineArrayNewMaxBeamEnts<EHANDLE> AttachEntity { get; }
	[NetworkVar("m_nAttachIndex")]
	public partial ref readonly InlineArrayNewMaxBeamEnts<int> AttachIndex { get; }
#if CLIENT_DLL
	public C_Beam() {
#else
	public Beam() {
#endif
		AttachEntityForModify() = new();
		AttachIndexForModify() = new();
	}
	[NetworkVar("m_nBeamType")]
	public partial int BeamType { get; set; }
	[NetworkVar("m_nBeamFlags")]
	public partial int BeamFlags { get; set; }
	[NetworkVar("m_nNumBeamEnts")]
	public partial int NumBeamEnts { get; set; }
	[NetworkVar("m_nMinDXLevel")]
	public partial int MinDXLevel { get; set; }
	[NetworkVar("m_nHaloIndex")]
	public partial int HaloIndex { get; set; }
	[NetworkVar("m_fHaloScale")]
	public partial float HaloScale { get; set; }
	[NetworkVar("m_fWidth")]
	public partial float Width { get; set; }
	[NetworkVar("m_fEndWidth")]
	public partial float EndWidth { get; set; }
	[NetworkVar("m_fFadeLength")]
	public partial float FadeLength { get; set; }
	[NetworkVar("m_fAmplitude")]
	public partial float Amplitude { get; set; }
	[NetworkVar("m_fStartFrame")]
	public partial float StartFrame { get; set; }
	[NetworkVar("m_fSpeed")]
	public new partial float Speed { get; set; }
	[NetworkVar("m_flFrameRate")]
	public partial float FrameRate { get; set; }
	[NetworkVar("m_flHDRColorScale")]
	public partial float HDRColorScale { get; set; }
	[NetworkVar("m_flFrame")]
	public partial float Frame { get; set; }
	[NetworkVar("m_clrRender")]
	public partial Color RenderColor { get; set; }
	[NetworkVar("m_vecEndPos")]
	public partial Vector3 EndPos { get; set; }
}

public static class BeamEnt
{
	public static int Entity(int x) => x & 0xFFF;
	public static int Attachment(int x) => (x >> 12) & 0xF;
}
#endif
