using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBubbleTrail")]
public partial class TEBubbleTrail(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	public const int BUBBLE_TRAIL_COUNT_BITS = 8;
	public const int BUBBLE_TRAIL_MAX_COUNT = (1 << BUBBLE_TRAIL_COUNT_BITS) - 1;

	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Mins)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Maxs)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(LWaterZ)) { Bits = 17, Low = MIN_COORD_INTEGER, High = MAX_COORD_INTEGER },
		new(nameof(Count)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Speed)) { Bits = 17, Low = MIN_COORD_INTEGER, High = MAX_COORD_INTEGER },
	];

	[NetworkVar("m_vecMins")]
	public Vector3 Mins;
	[NetworkVar("m_vecMaxs")]
	public Vector3 Maxs;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_flWaterZ")]
	public float LWaterZ;
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

		Speed = 8;
		Count = 20;
		LWaterZ = 0;

		ModelIndex = BaseCombatWeapon.g_sModelIndexBubbles;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEBubbleTrail g_TEBubbleTrail = new("Bubble Trail");

	public static void TE_BubbleTrail<IRF>(scoped ref IRF filter, float delay, in Vector3 mins, in Vector3 maxs, float waterZ, int modelindex, int count, float speed) where IRF : IRecipientFilter {
		g_TEBubbleTrail.Mins = mins;
		g_TEBubbleTrail.Maxs = maxs;
		g_TEBubbleTrail.LWaterZ = waterZ;
		g_TEBubbleTrail.ModelIndex = modelindex;
		g_TEBubbleTrail.Count = Math.Min(count, TEBubbleTrail.BUBBLE_TRAIL_MAX_COUNT);
		g_TEBubbleTrail.Speed = speed;

		g_TEBubbleTrail.Create(ref filter, delay);
	}
}
