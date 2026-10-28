using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEFizz")]
public partial class C_TEFizz : C_BaseTempEntity
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
}

public static partial class TempEnts
{
	public static void TE_Fizz(IRecipientFilter filter, float delay, C_BaseEntity? ed, int modelIndex, int density, int current) {
		throw new NotImplementedException();
	}
}
