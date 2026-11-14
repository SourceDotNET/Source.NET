using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBreakModel")]
public partial class TEBreakModel(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Rotation)) { Encoding = NetEncoding.VectorElements, Bits = 13, Flags = PropFlags.RoundDown },
		new(nameof(Size)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Velocity)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(Randomization)) { Bits = 9, Flags = PropFlags.Unsigned },
		new(nameof(Count)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Time)) { Bits = 10, Low = 0.0f, High = 102.4f },
		new(nameof(Flags)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_angRotation")]
	public QAngle Rotation;
	[NetworkVar("m_vecSize")]
	public Vector3 Size;
	[NetworkVar("m_vecVelocity")]
	public Vector3 Velocity;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_nRandomization")]
	public int Randomization;
	[NetworkVar("m_nCount")]
	public int Count;
	[NetworkVar("m_fTime")]
	public float Time;
	[NetworkVar("m_nFlags")]
	public int Flags;

	public override void Precache() => BaseEntity.PrecacheModel("models/gibs/hgibs.mdl");

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		ModelIndex = BaseEntity.PrecacheModel("models/gibs/hgibs.mdl");
		Origin = current_origin;
		Rotation = current_angles;
		Size = new Vector3(16, 16, 16);
		Velocity = new Vector3(random.RandomFloat(-10, 10), random.RandomFloat(-10, 10), random.RandomFloat(0, 20));
		Randomization = 100;
		Count = 10;
		Time = 5.0f;
		Flags = 0;

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
	static readonly TEBreakModel g_TEBreakModel = new("breakmodel");

	public static void TE_BreakModel<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, in QAngle angles, in Vector3 size, in Vector3 vel, int modelindex, int randomization, int count, float time, int flags) where IRF : IRecipientFilter {
		g_TEBreakModel.Origin = pos;
		g_TEBreakModel.Rotation = angles;
		g_TEBreakModel.Size = size;
		g_TEBreakModel.Velocity = vel;
		g_TEBreakModel.ModelIndex = modelindex;
		g_TEBreakModel.Randomization = randomization;
		g_TEBreakModel.Count = count;
		g_TEBreakModel.Time = time;
		g_TEBreakModel.Flags = flags;

		g_TEBreakModel.Create(ref filter, delay);
	}
}
