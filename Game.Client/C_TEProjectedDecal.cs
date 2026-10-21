using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
using Source.Common.Mathematics;
namespace Game.Client;
[NetworkName("CTEProjectedDecal")]
public partial class C_TEProjectedDecal : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Rotation)) { Bits = 10, Flags = PropFlags.RoundDown },
		new(nameof(LDistance)) { Bits = 10, Flags = PropFlags.RoundUp },
		new(nameof(Index)) { Bits = 9, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_angRotation")]
	public Vector3 Rotation;
	[NetworkVar("m_flDistance")]
	public float LDistance;
	[NetworkVar("m_nIndex")]
	public int Index;
}

public static partial class TempEnts
{
	public static void TE_ProjectDecal(IRecipientFilter filter, float delay, in Vector3 pos, in QAngle angles, float distance, int index) {
		throw new NotImplementedException();
	}
}
