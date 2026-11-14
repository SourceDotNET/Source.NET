using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTESprite")]
public partial class TESprite(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(Scale)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0.0f, High = 25.6f },
		new(nameof(Brightness)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_fScale")]
	public float Scale;
	[NetworkVar("m_nBrightness")]
	public int Brightness;

	public override void Precache() => BaseEntity.PrecacheModel("sprites/gunsmoke.vmt");

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		ModelIndex = BaseEntity.PrecacheModel("sprites/gunsmoke.vmt");
		Scale = 0.8f;
		Brightness = 200;
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
	static readonly TESprite g_TESprite = new("Sprite");

	public static void TE_Sprite<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, int modelindex, float size, int brightness) where IRF : IRecipientFilter {
		g_TESprite.Origin = pos;
		g_TESprite.ModelIndex = modelindex;
		g_TESprite.Scale = size;
		g_TESprite.Brightness = brightness;

		g_TESprite.Create(ref filter, delay);
	}
}
