using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CFleshEffectTarget")]
public partial class C_FleshEffectTarget : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Radius)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ScaleTime)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flRadius")]
	public float Radius;
	[NetworkVar("m_flScaleTime")]
	public float ScaleTime;
}
