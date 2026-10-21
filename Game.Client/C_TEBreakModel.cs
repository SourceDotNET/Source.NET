using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
using Source.Common.Mathematics;
namespace Game.Client;
[NetworkName("CTEBreakModel")]
public partial class C_TEBreakModel : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		nameof(Origin),
		new(nameof(Rotation)) { Encoding = NetEncoding.VectorElements },
		nameof(Size),
		nameof(Velocity),
		nameof(ModelIndex),
		nameof(Randomization),
		nameof(Count),
		nameof(Time),
		nameof(Flags),
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_angRotation")]
	public QAngle Rotation;
	[NetworkVar("m_vecSize")]
	public Vector3 Size;
	[NetworkVar("m_vecVelocity")]
	public Vector3 Velocity;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_nRandomization")]
	public int Randomization;
	[NetworkVar("m_nCount")]
	public int Count;
	[NetworkVar("m_fTime")]
	public float Time;
	[NetworkVar("m_nFlags")]
	public int Flags;
}

public static partial class TempEnts
{
	public static void TE_BreakModel(IRecipientFilter filter, float delay, in Vector3 pos, in QAngle angles, in Vector3 size, in Vector3 vel, int modelIndex, int randomization, int count, float time, int flags) {
		throw new NotImplementedException();
	}
}
