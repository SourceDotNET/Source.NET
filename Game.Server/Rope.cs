using Source.Common;
using Source;

using Game.Shared;
using System.Numerics;
using Source.Common.MaterialSystem;

namespace Game.Server;


[LinkEntityToClass("keyframe_rope")]
[LinkEntityToClass("move_rope")]
[NetworkName("CRopeKeyframe", NoBase = true)]
public partial class RopeKeyframe : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(StartPoint),
		nameof(EndPoint),
		new(nameof(StartAttachment)) { Bits = 5 },
		new(nameof(EndAttachment)) { Bits = 5 },
		new(nameof(StartBone)) { Bits = 5 },
		new(nameof(EndBone)) { Bits = 5 },
		new(nameof(StartOffset)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(EndOffset)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(RenderColor)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Slack)) { Bits = 12 },
		new(nameof(RopeLength)) { Bits = 15 },
		new(nameof(LockedPoints)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(RopeFlags)) { Bits = 9, Flags = PropFlags.Unsigned },
		new(nameof(Segments)) { Bits = 4, Flags = PropFlags.Unsigned },
		nameof(ConstrainBetweenEndpoints),
		new(nameof(RopeMaterialModelIndex)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(Subdiv)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(TextureScale)) { Bits = 10, Low = 0.1f, High = 10.0f },
		new(nameof(Width)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ScrollSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Origin)) { Encoding = NetEncoding.Vector, Bits = 0, Flags = PropFlags.Coord },
		new(nameof(MoveParent)) { Encoding = NetEncoding.EHandle },
		new(nameof(ParentAttachment)) { Encoding = NetEncoding.Int, Bits = 8, Flags = PropFlags.Unsigned },
	];

	public RopeKeyframe() {
		StartPoint = new();
		EndPoint = new();
	}

	[NetworkVar("m_hStartPoint")]
	public partial EHANDLE StartPoint { get; set; }
	[NetworkVar("m_hEndPoint")]
	public partial EHANDLE EndPoint { get; set; }
	[NetworkVar("m_iStartAttachment")]
	public partial int StartAttachment { get; set; }
	[NetworkVar("m_iEndAttachment")]
	public partial int EndAttachment { get; set; }
	[NetworkVar("m_iStartBone")]
	public partial int StartBone { get; set; }
	[NetworkVar("m_iEndBone")]
	public partial int EndBone { get; set; }
	[NetworkVar("m_vecStartOffset")]
	public partial Vector3 StartOffset { get; set; }
	[NetworkVar("m_vecEndOffset")]
	public partial Vector3 EndOffset { get; set; }
	[NetworkVar("m_clrRender")]
	public partial Color RenderColor { get; set; }
	[NetworkVar("m_Slack")]
	public partial int Slack { get; set; }
	[NetworkVar("m_RopeLength")]
	public partial int RopeLength { get; set; }
	[NetworkVar("m_fLockedPoints")]
	public partial int LockedPoints { get; set; }
	[NetworkVar("m_RopeFlags")]
	public partial int RopeFlags { get; set; }
	[NetworkVar("m_nSegments")]
	public partial int Segments { get; set; }
	[NetworkVar("m_bConstrainBetweenEndpoints")]
	public partial bool ConstrainBetweenEndpoints { get; set; }
	[NetworkVar("m_iRopeMaterialModelIndex")]
	public partial int RopeMaterialModelIndex { get; set; }
	[NetworkVar("m_Subdiv")]
	public partial int Subdiv { get; set; }
	[NetworkVar("m_TextureScale")]
	public partial float TextureScale { get; set; }
	[NetworkVar("m_Width")]
	public partial float Width { get; set; }
	[NetworkVar("m_flScrollSpeed")]
	public partial float ScrollSpeed { get; set; }
}
