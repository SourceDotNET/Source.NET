using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
using Source.Common.Mathematics;
namespace Game.Client;
[NetworkName("CTEPhysicsProp")]
public partial class C_TEPhysicsProp : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		nameof(Origin),
		new(nameof(Rotation)) { Encoding = NetEncoding.VectorElements },
		nameof(Velocity),
		nameof(ModelIndex),
		nameof(Flags),
		nameof(Skin),
		nameof(Effects),
		nameof(ClrRender),
		nameof(ModelScale),
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_angRotation")]
	public QAngle Rotation;
	[NetworkVar("m_vecVelocity")]
	public Vector3 Velocity;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_nSkin")]
	public int Skin;
	[NetworkVar("m_nFlags")]
	public int Flags;
	[NetworkVar("m_nEffects")]
	public int Effects;
	[NetworkVar("m_clrRender")]
	public int ClrRender;
	[NetworkVar("m_fModelScale")]
	public float ModelScale;
}

public static partial class TempEnts
{
	public static void TE_PhysicsProp(IRecipientFilter filter, float delay, int modelIndex, int skin, in Vector3 pos, in QAngle angles, in Vector3 vel, bool breakModel, int effects) {
		throw new NotImplementedException();
	}
}
