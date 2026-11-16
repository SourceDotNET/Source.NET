using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEFizz")]
public partial class TEFizz(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Entity)) { Bits = 13, Flags = PropFlags.Unsigned },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(Density)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Current)) { Bits = 16 },
	];

	[NetworkVar("m_nEntity")]
	public int Entity;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_nDensity")]
	public int Density;
	[NetworkVar("m_nCurrent")]
	public int Current;

	public override void Precache() => BaseEntity.PrecacheModel("sprites/bubble.vmt");

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		ModelIndex = BaseEntity.PrecacheModel("sprites/bubble.vmt");
		Density = 200;
		Entity = 1;
		Current = 100;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEFizz g_TEFizz = new("Fizz");

	public static void TE_Fizz<IRF>(scoped ref IRF filter, float delay, BaseEntity? entity, int modelindex, int density, int current) where IRF : IRecipientFilter {
		Assert(entity != null);

		g_TEFizz.Entity = entity!.EntIndex();
		g_TEFizz.ModelIndex = modelindex;
		g_TEFizz.Density = density;
		g_TEFizz.Current = current;

		g_TEFizz.Create(ref filter, delay);
	}
}
