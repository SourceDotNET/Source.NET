using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEGlowSprite")]
public partial class TEGlowSprite(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(Scale)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0.0f, High = 25.6f },
		new(nameof(Life)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0.0f, High = 25.6f },
		new(nameof(Brightness)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_fScale")]
	public float Scale;
	[NetworkVar("m_fLife")]
	public float Life;
	[NetworkVar("m_nBrightness")]
	public int Brightness;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		ModelIndex = BaseCombatWeapon.g_sModelIndexSmoke;
		Scale = 0.8f;
		Brightness = 200;
		Life = 2.0f;
		Origin = current_origin;

		Origin.Z += 24;

		MathLib.AngleVectors(current_angles, out Vector3 forward, out Vector3 right, out _);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Origin += forward * 50.0f;
		Origin += right * -25.0f;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEGlowSprite g_TEGlowSprite = new("GlowSprite");

	public static void TE_GlowSprite<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, int modelindex, float life, float size, int brightness) where IRF : IRecipientFilter {
		g_TEGlowSprite.Origin = pos;
		g_TEGlowSprite.ModelIndex = modelindex;
		g_TEGlowSprite.Life = life;
		g_TEGlowSprite.Scale = size;
		g_TEGlowSprite.Brightness = brightness;

		g_TEGlowSprite.Create(ref filter, delay);
	}
}
