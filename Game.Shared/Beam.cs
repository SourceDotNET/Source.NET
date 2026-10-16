#if CLIENT_DLL || GAME_DLL

using Source;
using Source.Common;
using Source.Common.Networking;

using System.Numerics;
namespace Game.Shared;
#if CLIENT_DLL
using FIELD = Source.FIELD<C_Beam>;
#else
using FIELD = Source.FIELD<Beam>;
#endif

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
public class
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

	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_BeamPredictableId = new(nameof(DT_BeamPredictableId), [
#if CLIENT_DLL
		RecvPropPredictableId(FIELD.OF(nameof(PredictableID))),
		RecvPropBool(FIELD.OF(nameof(b_IsPlayerSimulated)))
#else
		SendPropPredictableId(FIELD.OF(nameof(PredictableId))),
		SendPropBool(FIELD.OF(nameof(b_IsPlayerSimulated)))
#endif
		]);
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_Beam = new([
#if CLIENT_DLL
			RecvPropInt(FIELD.OF(nameof(BeamType))),
			RecvPropInt(FIELD.OF(nameof(BeamFlags))),
			RecvPropInt(FIELD.OF(nameof(NumBeamEnts))),
			RecvPropArray3(
				FIELD.OF_ARRAY(nameof(AttachEntity)),
				RecvPropEHandle(FIELD.OF_ARRAYINDEX(nameof(AttachEntity), 0))
			),
			RecvPropArray3
			(
				FIELD.OF_ARRAY(nameof(AttachIndex)),
				RecvPropInt(FIELD.OF_ARRAYINDEX(nameof(AttachIndex), 0))
			),
			RecvPropInt(FIELD.OF(nameof(HaloIndex))),
			RecvPropFloat(FIELD.OF(nameof(HaloScale))),
			RecvPropFloat(FIELD.OF(nameof(Width))),
			RecvPropFloat(FIELD.OF(nameof(EndWidth))),
			RecvPropFloat(FIELD.OF(nameof(FadeLength))),
			RecvPropFloat(FIELD.OF(nameof(Amplitude))),
			RecvPropFloat(FIELD.OF(nameof(StartFrame))),
			RecvPropFloat(FIELD.OF(nameof(Speed)), 0, RecvProxy_Beam_ScrollSpeed),
			RecvPropInt(FIELD.OF(nameof(RenderFX))),
			RecvPropInt(FIELD.OF(nameof(RenderMode))),
			RecvPropFloat(FIELD.OF(nameof(FrameRate))),
			RecvPropFloat(FIELD.OF(nameof(HDRColorScale))),
			RecvPropFloat(FIELD.OF(nameof(Frame))),
			RecvPropInt(FIELD.OF(nameof(RenderColor))),
			RecvPropVector(FIELD.OF(nameof(EndPos))),

			RecvPropInt(FIELD.OF(nameof(ModelIndex))),
			RecvPropVector(FIELD.OF(nameof(Origin))),
			RecvPropInt(FIELD.OF(nameof(MoveParent)), 0, RecvProxy_IntToMoveParent),
			RecvPropInt(FIELD.OF(nameof(MinDXLevel))),

			RecvPropDataTable( "beampredictable_id", DT_BeamPredictableId)
#else
			SendPropInt(FIELD.OF(nameof(BeamType)), (int)Math.Log2((int)BeamTypes.NumTypes)+1, PropFlags.Unsigned),
			SendPropInt(FIELD.OF(nameof(BeamFlags)), (int)Shared.BeamFlags.NumFlags, PropFlags.Unsigned),
			SendPropInt(FIELD.OF(nameof(NumBeamEnts)), 5, PropFlags.Unsigned),
			SendPropArray3(
				FIELD.OF_ARRAY(nameof(AttachEntity)),
				SendPropEHandle(FIELD.OF_ARRAYINDEX(nameof(AttachEntity), 0))
			),
			SendPropArray3
			(
				FIELD.OF_ARRAY(nameof(AttachIndex)),
				SendPropInt(FIELD.OF_ARRAYINDEX(nameof(AttachIndex), 0), ATTACHMENT_INDEX_BITS, PropFlags.Unsigned)
			),
			SendPropInt(FIELD.OF(nameof(HaloIndex)), 16, PropFlags.Unsigned),
			SendPropFloat(FIELD.OF(nameof(HaloScale)),0, PropFlags.NoScale),
			SendPropFloat(FIELD.OF(nameof(Width)), 10, PropFlags.RoundUp, 0.0f, MAX_BEAM_WIDTH),
			SendPropFloat(FIELD.OF(nameof(EndWidth)), 10, PropFlags.RoundUp, 0.0f, MAX_BEAM_WIDTH),
			SendPropFloat(FIELD.OF(nameof(FadeLength)),0, PropFlags.NoScale),
			SendPropFloat(FIELD.OF(nameof(Amplitude)), 8, PropFlags.RoundDown, 0.0f, MAX_BEAM_NOISEAMPLITUDE),
			SendPropFloat(FIELD.OF(nameof(StartFrame)), 8, PropFlags.RoundDown, 0.0f,   256.0f),
			SendPropFloat(FIELD.OF(nameof(Speed)), 8, PropFlags.NoScale, 0.0f, MAX_BEAM_SCROLLSPEED),
			SendPropInt(FIELD.OF(nameof(RenderFX)), 8, PropFlags.Unsigned),
			SendPropInt(FIELD.OF(nameof(RenderMode)), 8, PropFlags.Unsigned),
			SendPropFloat(FIELD.OF(nameof(FrameRate)), 10, PropFlags.RoundUp, -25.0f, 25.0f),
			SendPropFloat(FIELD.OF(nameof(HDRColorScale)), 0, PropFlags.NoScale, 0.0f, 0.0f),
			SendPropFloat(FIELD.OF(nameof(Frame)), 20, PropFlags.RoundDown | PropFlags.ChangesOften, 0.0f, 256.0f),
			SendPropInt(FIELD.OF(nameof(RenderColor)), 32, PropFlags.Unsigned | PropFlags.ChangesOften),
			SendPropVector(FIELD.OF(nameof(EndPos)), -1, PropFlags.Coord),

			SendPropModelIndex(FIELD.OF(nameof(ModelIndex))),
			SendPropVector(NetworkVarFields.Origin, 19, PropFlags.ChangesOften, WorldSize.MIN_COORD_INTEGER, WorldSize.MAX_COORD_INTEGER),
			SendPropEHandle(FIELD.OF(nameof(MoveParent))),
			SendPropInt(FIELD.OF(nameof(MinDXLevel)), 8, PropFlags.Unsigned),

			SendPropDataTable( "beampredictable_id", DT_BeamPredictableId, SendProxy_SendPredictableId)
#endif
		]);

#if CLIENT_DLL
	private static void RecvProxy_Beam_ScrollSpeed(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		C_Beam beam = (C_Beam)instance;
		float val = data.Value.Float * 0.1f;
		beam.Speed = val;
	}

	public static readonly new ClientClass ClientClass = new ClientClass(DT_Beam);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_Beam);
#endif
	[NetworkName("m_hAttachEntity")]
	public InlineArrayNewMaxBeamEnts<EHANDLE> AttachEntity = new();
	[NetworkName("m_nAttachIndex")]
	public InlineArrayNewMaxBeamEnts<int> AttachIndex = new();
	[NetworkName("m_nBeamType")]
	public int BeamType;
	[NetworkName("m_nBeamFlags")]
	public int BeamFlags;
	[NetworkName("m_nNumBeamEnts")]
	public int NumBeamEnts;
	[NetworkName("m_nMinDXLevel")]
	public int MinDXLevel;
	[NetworkName("m_nHaloIndex")]
	public int HaloIndex;
	[NetworkName("m_fHaloScale")]
	public float HaloScale;
	[NetworkName("m_fWidth")]
	public float Width;
	[NetworkName("m_fEndWidth")]
	public float EndWidth;
	[NetworkName("m_fFadeLength")]
	public float FadeLength;
	[NetworkName("m_fAmplitude")]
	public float Amplitude;
	[NetworkName("m_fStartFrame")]
	public float StartFrame;
	[NetworkName("m_fSpeed")]
	public new float Speed;
	[NetworkName("m_flFrameRate")]
	public float FrameRate;
	[NetworkName("m_flHDRColorScale")]
	public float HDRColorScale;
	[NetworkName("m_flFrame")]
	public float Frame;
	[NetworkName("m_clrRender")]
	public Color RenderColor;
	[NetworkName("m_vecEndPos")]
	public Vector3 EndPos;
}

public static class BeamEnt
{
	public static int Entity(int x) => x & 0xFFF;
	public static int Attachment(int x) => (x >> 12) & 0xF;
}
#endif
