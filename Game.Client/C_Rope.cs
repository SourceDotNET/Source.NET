using Game.Shared;

using Source;
using Source.Common;
using System.Net;

using System.Security.Cryptography.X509Certificates;
using Source.Common.MaterialSystem;
using System.Numerics;
namespace Game.Client;

[NetworkName("CRopeKeyframe", NoBase = true)]
public partial class C_RopeKeyframe : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(RopeMaterialModelIndex),
		nameof(StartPoint),
		nameof(EndPoint),
		nameof(StartAttachment),
		nameof(EndAttachment),
		nameof(StartBone),
		nameof(EndBone),
		nameof(StartOffset),
		nameof(EndOffset),
		new(nameof(RenderColor)) { Encoding = NetEncoding.Int },
		nameof(LockedPoints),
		// todo: RecvProxy_RecomputeSprings
		nameof(Slack),
		// todo: RecvProxy_RecomputeSprings
		nameof(RopeLength),
		nameof(RopeFlags),
		nameof(TextureScale),
		nameof(Segments),
		nameof(ConstrainBetweenEndpoints),
		nameof(Subdiv),
		nameof(Width),
		nameof(ScrollSpeed),
		nameof(Origin),
		new(nameof(NetworkMoveParent)) { Encoding = NetEncoding.Int, Proxy = typeof(MoveParentProxy) },
		nameof(ParentAttachment),
	];

	[NetworkVar("m_hStartPoint")]
	public EHANDLE StartPoint = new();
	[NetworkVar("m_hEndPoint")]
	public EHANDLE EndPoint = new();
	[NetworkVar("m_iStartAttachment")]
	public int StartAttachment;
	[NetworkVar("m_iEndAttachment")]
	public int EndAttachment;
	[NetworkVar("m_iStartBone")]
	public int StartBone;
	[NetworkVar("m_iEndBone")]
	public int EndBone;
	[NetworkVar("m_vecStartOffset")]
	public Vector3 StartOffset;
	[NetworkVar("m_vecEndOffset")]
	public Vector3 EndOffset;
	[NetworkVar("m_clrRender")]
	public Color RenderColor;
	[NetworkVar("m_Slack")]
	public int Slack;
	[NetworkVar("m_RopeLength")]
	public int RopeLength;
	[NetworkVar("m_fLockedPoints")]
	public int LockedPoints;
	[NetworkVar("m_RopeFlags")]
	public int RopeFlags;
	[NetworkVar("m_nSegments")]
	public int Segments;
	[NetworkVar("m_bConstrainBetweenEndpoints")]
	public bool ConstrainBetweenEndpoints;
	[NetworkVar("m_iRopeMaterialModelIndex")]
	public int RopeMaterialModelIndex;
	[NetworkVar("m_Subdiv")]
	public int Subdiv;
	[NetworkVar("m_TextureScale")]
	public float TextureScale;
	[NetworkVar("m_Width")]
	public float Width;
	[NetworkVar("m_flScrollSpeed")]
	public float ScrollSpeed;
}

