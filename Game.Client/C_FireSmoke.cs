using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CFireSmoke")]
public partial class C_FireSmoke : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Scale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ScaleTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Flags)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(FlameModelIndex)) { Bits = 14 },
		new(nameof(FlameFromAboveModelIndex)) { Bits = 14 },
	];

	[NetworkVar("m_flStartScale")]
	public float StartScale;
	// todo: RecvProxy_Scale
	[NetworkVar("m_flScale")]
	public float Scale;
	// todo: RecvProxy_ScaleTime
	[NetworkVar("m_flScaleTime")]
	public float ScaleTime;
	[NetworkVar("m_nFlags")]
	public int Flags;
	[NetworkVar("m_nFlameModelIndex")]
	public int FlameModelIndex;
	[NetworkVar("m_nFlameFromAboveModelIndex")]
	public int FlameFromAboveModelIndex;
}
