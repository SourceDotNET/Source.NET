using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTESmoke")]
public partial class TESmoke(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(Scale)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0.0f, High = 25.6f },
		new(nameof(FrameRate)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_fScale")]
	public float Scale;
	[NetworkVar("m_nFrameRate")]
	public int FrameRate;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		ModelIndex = BaseCombatWeapon.g_sModelIndexSmoke;
		Scale = 5.0f;
		FrameRate = 12;
		Origin = current_origin;

		Origin.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward, out Vector3 right, out _);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Origin += forward * 50.0f;
		Origin += right * 25.0f;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TESmoke g_TESmoke = new("Smoke");

	public static void TE_Smoke<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, int modelindex, float scale, int framerate) where IRF : IRecipientFilter {
		g_TESmoke.Origin = pos;
		g_TESmoke.ModelIndex = modelindex;
		g_TESmoke.Scale = scale;
		g_TESmoke.FrameRate = framerate;

		g_TESmoke.Create(ref filter, delay);
	}
}
