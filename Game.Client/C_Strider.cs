using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CNPC_Strider")]
public partial class C_Strider : C_AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(HitPos),
		new(nameof(IKTarget)) { Encoding = NetEncoding.Elements },
	];

	[NetworkVar("m_vecHitPos")]
	public Vector3 HitPos;
	[NetworkVar("m_vecIKTarget")]
	public InlineArray6<Vector3> IKTarget;
}
