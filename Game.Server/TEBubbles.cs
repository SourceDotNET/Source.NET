using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBubbles")]
public partial class TEBubbles(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Mins)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Maxs)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(Height)) { Bits = 17, Low = MIN_COORD_INTEGER, High = MAX_COORD_INTEGER },
		new(nameof(Count)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Speed)) { Bits = 17, Low = MIN_COORD_INTEGER, High = MAX_COORD_INTEGER },
	];

	[NetworkVar("m_vecMins")]
	public Vector3 Mins;
	[NetworkVar("m_vecMaxs")]
	public Vector3 Maxs;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_fHeight")]
	public float Height;
	[NetworkVar("m_nCount")]
	public int Count;
	[NetworkVar("m_fSpeed")]
	public float Speed;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		Mins = current_origin;

		Mins.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Mins += forward * 100.0f;

		Maxs = Mins + new Vector3(256, 256, 256);

		Speed = 2;
		Count = 50;
		Height = 256;

		ModelIndex = BaseCombatWeapon.g_sModelIndexBubbles;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEBubbles g_TEBubbles = new("Bubbles");

	public static void TE_Bubbles<IRF>(scoped ref IRF filter, float delay, in Vector3 mins, in Vector3 maxs, float height, int modelindex, int count, float speed) where IRF : IRecipientFilter {
		g_TEBubbles.Mins = mins;
		g_TEBubbles.Maxs = maxs;
		g_TEBubbles.Height = height;
		g_TEBubbles.ModelIndex = modelindex;
		g_TEBubbles.Count = count;
		g_TEBubbles.Speed = speed;

		g_TEBubbles.Create(ref filter, delay);
	}
}
