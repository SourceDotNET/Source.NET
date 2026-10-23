using Game.Shared;

using Source.Common;

using System.Numerics;

namespace Game.Server;

[NetworkName("CTEEffectDispatch")]
public partial class TEEffectDispatch(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(EffectData)) { Encoding = NetEncoding.DataTable, DataTable = "DT_EffectData" },
	];

	[NetworkVar("m_EffectData")]
	public EffectData EffectData = new();
}

public static partial class TempEnts
{
	static readonly TEEffectDispatch g_TEEffectDispatch = new("EffectDispatch");

	public static void TE_DispatchEffect<IRF>(scoped ref IRF filter, float delay, in Vector3 pos, ReadOnlySpan<char> name, EffectData data) where IRF : IRecipientFilter {
		g_TEEffectDispatch.EffectData = data.Copy();

		g_TEEffectDispatch.EffectData.EffectName = g_pStringTableEffectDispatch!.AddString(true, name);

		g_TEEffectDispatch.Create(ref filter, 0);
	}

	public static void DispatchEffect(ReadOnlySpan<char> name, EffectData data) {
		PASFilter filter = new(data.Origin);
		DispatchEffect(name, data, filter);
	}

	public static void DispatchEffect(ReadOnlySpan<char> name, EffectData data, RecipientFilter filter) => te.DispatchEffect(ref filter, 0.0f, data.Origin, name, data);
}
