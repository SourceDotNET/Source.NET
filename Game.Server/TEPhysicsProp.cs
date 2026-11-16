using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
using Source.Common.Mathematics;
namespace Game.Server;
[NetworkName("CTEPhysicsProp")]
public partial class TEPhysicsProp(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Rotation)) { Encoding = NetEncoding.VectorElements, Bits = 13, Flags = PropFlags.RoundDown },
		new(nameof(Velocity)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(Skin)) { Bits = 10 },
		new(nameof(Flags)) { Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(Effects)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(ClrRender)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(ModelScale)) { Bits = 0, Flags = PropFlags.NoScale },
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
	public int ClrRender = -1;
	[NetworkVar("m_fModelScale")]
	public float ModelScale = 1.0f;

	public override void Precache() => BaseEntity.PrecacheModel("models/gibs/hgibs.mdl");

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		ModelIndex = BaseEntity.PrecacheModel("models/gibs/hgibs.mdl");
		Skin = 0;
		Origin = current_origin;
		Rotation = current_angles;
		Velocity = new Vector3(random.RandomFloat(-10, 10), random.RandomFloat(-10, 10), random.RandomFloat(0, 20));
		Flags = 0;
		Effects = 0;

		Origin += new Vector3(0, 0, 24);

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
	static readonly TEPhysicsProp s_TEPhysicsProp = new("physicsprop");

	public static void TE_PhysicsProp<IRF>(scoped ref IRF filter, float delay, int modelindex, int skin, in Vector3 pos, in QAngle angles, in Vector3 vel, int flags, int effects, Color clrRender, float modelScale) where IRF : IRecipientFilter {
		s_TEPhysicsProp.Origin = pos;
		s_TEPhysicsProp.Rotation = angles;
		s_TEPhysicsProp.Velocity = vel;
		s_TEPhysicsProp.ModelIndex = modelindex;
		s_TEPhysicsProp.Skin = skin;
		s_TEPhysicsProp.Flags = flags;
		s_TEPhysicsProp.Effects = effects;
		s_TEPhysicsProp.ClrRender = clrRender.GetRawColor();
		s_TEPhysicsProp.ModelScale = modelScale;

		s_TEPhysicsProp.Create(ref filter, delay);
	}
}
