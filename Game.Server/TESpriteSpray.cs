using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTESpriteSpray")]
public partial class TESpriteSpray(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Direction)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(Noise)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0.0f, High = 2.56f },
		new(nameof(Speed)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Count)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_fNoise")]
	public float Noise;
	[NetworkVar("m_nSpeed")]
	public int Speed;
	[NetworkVar("m_nCount")]
	public int Count;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		ModelIndex = BaseCombatWeapon.g_sModelIndexSmoke;
		Noise = 0.8f;
		Count = 5;
		Speed = 30;
		Origin = current_origin;

		Origin.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward, out Vector3 right, out _);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Origin += forward * 50.0f;
		Origin += right * -25.0f;

		Direction = new Vector3(random.RandomInt(-100, 100), random.RandomInt(-100, 100), random.RandomInt(0, 100));

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TESpriteSpray g_TESpriteSpray = new("Sprite Spray");

	public static void TE_SpriteSpray<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, in Vector3 dir, int modelindex, int speed, float noise, int count) where IRF : IRecipientFilter {
		g_TESpriteSpray.Origin = pos;
		g_TESpriteSpray.Direction = dir;
		g_TESpriteSpray.ModelIndex = modelindex;
		g_TESpriteSpray.Speed = speed;
		g_TESpriteSpray.Noise = noise;
		g_TESpriteSpray.Count = count;

		g_TESpriteSpray.Create(ref filter, delay);
	}
}
