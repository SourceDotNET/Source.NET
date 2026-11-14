using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEProjectedDecal")]
public partial class TEProjectedDecal(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Rotation)) { Encoding = NetEncoding.QAngles, Bits = 10, Flags = PropFlags.RoundDown },
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

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		LDistance = 1024.0f;
		Index = 0;
		Origin = current_origin;
		Rotation = current_angles;

		Origin.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Origin += forward * 24.0f;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEProjectedDecal g_TEProjectedDecal = new("Projected Decal");

	public static void TE_ProjectDecal<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, in QAngle angles, float distance, int index) where IRF : IRecipientFilter {
		g_TEProjectedDecal.Origin = pos;
		g_TEProjectedDecal.Rotation = angles;
		g_TEProjectedDecal.LDistance = distance;
		g_TEProjectedDecal.Index = index;

		g_TEProjectedDecal.Create(ref filter, delay);
	}
}
