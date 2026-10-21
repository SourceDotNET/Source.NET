using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CEntityDissolve")]
public partial class C_EntityDissolve : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeOutStart)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeOutLength)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeOutModelStart)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeOutModelLength)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeInStart)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeInLength)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DissolveType)) { Bits = 3, Flags = PropFlags.Unsigned },
		new(nameof(DissolverOrigin)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Magnitude)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_flStartTime")]
	public float StartTime;
	[NetworkVar("m_flFadeOutStart")]
	public float FadeOutStart;
	[NetworkVar("m_flFadeOutLength")]
	public float FadeOutLength;
	[NetworkVar("m_flFadeOutModelStart")]
	public float FadeOutModelStart;
	[NetworkVar("m_flFadeOutModelLength")]
	public float FadeOutModelLength;
	[NetworkVar("m_flFadeInStart")]
	public float FadeInStart;
	[NetworkVar("m_flFadeInLength")]
	public float FadeInLength;
	[NetworkVar("m_nDissolveType")]
	public int DissolveType;
	[NetworkVar("m_vDissolverOrigin")]
	public Vector3 DissolverOrigin;
	[NetworkVar("m_nMagnitude")]
	public int Magnitude;
}
