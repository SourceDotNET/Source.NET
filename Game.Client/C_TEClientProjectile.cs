using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEClientProjectile")]
public partial class C_TEClientProjectile : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Velocity)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(LifeTime)) { Bits = 6, Flags = PropFlags.Unsigned },
		new(nameof(HOwner)) { Bits = 23, Flags = PropFlags.Unsigned },
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
	public int HOwner;
}

public static partial class TempEnts
{
	public static void TE_ClientProjectile(IRecipientFilter filter, float delay, in Vector3 origin, in Vector3 velocity, int modelIndex, int lifetime, C_BaseEntity? owner) {
		throw new NotImplementedException();
	}
}
