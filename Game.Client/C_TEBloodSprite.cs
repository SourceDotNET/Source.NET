using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEBloodSprite", NoBase = true)]
public partial class C_TEBloodSprite : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		nameof(Origin),
		nameof(Direction),
		nameof(R),
		nameof(G),
		nameof(B),
		nameof(A),
		nameof(Size),
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;
	[NetworkVar("r")]
	public int R;
	[NetworkVar("g")]
	public int G;
	[NetworkVar("b")]
	public int B;
	[NetworkVar("a")]
	public int A;
	public int SprayModel;
	public int DropModel;
	[NetworkVar("m_nSize")]
	public int Size;
}

public static partial class TempEnts
{
	public static void TE_BloodSprite(IRecipientFilter filter, float delay, in Vector3 org, in Vector3 dir, int r, int g, int b, int a, int size) {
		throw new NotImplementedException();
	}
}
