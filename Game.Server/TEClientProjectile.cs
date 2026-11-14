using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEClientProjectile")]
public partial class TEClientProjectile(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Velocity)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(LifeTime)) { Bits = 6, Flags = PropFlags.Unsigned },
		nameof(HOwner),
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecVelocity")]
	public Vector3 Velocity;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_nLifeTime")]
	public int LifeTime;
	[NetworkVar("m_hOwner")]
	public EHANDLE HOwner = new();

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		Origin = current_origin;

		MathLib.AngleVectors(current_angles, out Vector3 forward);
		forward.Z = 0.0f;
		MathLib.VectorNormalize(ref forward);

		Velocity = forward * 2048;
		LifeTime = 5;
		HOwner.Set(null);

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEClientProjectile g_TEClientProjectile = new("Client Projectile");

	public static void TE_ClientProjectile<IRF>(scoped ref IRF filter, float delay, in Vector3 origin, in Vector3 velocity, int modelindex, int lifetime, BaseEntity? owner) where IRF : IRecipientFilter {
		g_TEClientProjectile.Origin = origin;
		g_TEClientProjectile.Velocity = velocity;
		g_TEClientProjectile.ModelIndex = modelindex;
		g_TEClientProjectile.LifeTime = lifetime;
		g_TEClientProjectile.HOwner.Set(owner);

		g_TEClientProjectile.Create(ref filter, delay);
	}
}
