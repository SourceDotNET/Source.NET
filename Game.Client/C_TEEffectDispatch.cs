using Game.Shared;

using Source.Common;

using System.Numerics;
namespace Game.Client; 

[NetworkName("CTEEffectDispatch")]
public partial class C_TEEffectDispatch : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(EffectData)) { Encoding = NetEncoding.DataTable, DataTable = "DT_EffectData" },
	];

	[NetworkVar("m_EffectData")]
	public readonly EffectData EffectData = new();
}

public static partial class TempEnts
{
	public static void TE_DispatchEffect(IRecipientFilter filter, float delay, in Vector3 pos, ReadOnlySpan<char> name, EffectData data) {
		throw new NotImplementedException();
	}
}
