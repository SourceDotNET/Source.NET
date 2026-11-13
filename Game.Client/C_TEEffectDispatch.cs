using Game.Shared;

using Source.Common;

using System.Numerics;
namespace Game.Client;

public delegate void ClientEffectCallback(EffectData data);

public sealed class ClientEffectRegistration
{
	public static ClientEffectRegistration? Head;

	public readonly string EffectName;
	public readonly ClientEffectCallback Function;
	public readonly ClientEffectRegistration? Next;

	public ClientEffectRegistration(string effectName, ClientEffectCallback fn) {
		EffectName = effectName;
		Function = fn;
		Next = Head;
		Head = this;
	}
}

[NetworkName("CTEEffectDispatch")]
public partial class C_TEEffectDispatch : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(EffectData)) { Encoding = NetEncoding.DataTable, DataTable = "DT_EffectData" },
	];

	[NetworkVar("m_EffectData")]
	public readonly EffectData EffectData = new();

	public override void PostDataUpdate(DataUpdateType updateType) {
		ReadOnlySpan<char> effectName = HLClient.g_pStringTableEffectDispatch!.GetString(EffectData.EffectName);
		if (!effectName.IsEmpty)
			DispatchEffectToCallback(effectName, EffectData);
	}
}

public static partial class TempEnts
{
	public static void DispatchEffectToCallback(ReadOnlySpan<char> effectName, EffectData data) {
		if (g_Lua == null)
			return;

		for (ClientEffectRegistration? reg = ClientEffectRegistration.Head; reg != null; reg = reg.Next) {
			if (effectName.Equals(reg.EffectName, StringComparison.OrdinalIgnoreCase)) {
				reg.Function(data);
				return;
			}
		}

		DevMsg($"DispatchEffect: effect '{effectName}' not found on client\n");
	}

	public static void TE_DispatchEffect(IRecipientFilter filter, float delay, in Vector3 pos, ReadOnlySpan<char> name, EffectData data) => DispatchEffectToCallback(name, data);

	public static void DispatchEffect(ReadOnlySpan<char> name, EffectData data) {
		PASFilter filter = new(data.Origin);
		DispatchEffect(name, data, filter);
	}

	public static void DispatchEffect(ReadOnlySpan<char> name, EffectData data, C_RecipientFilter filter) => te.DispatchEffect(filter, 0.0f, data.Origin, name, data);
}
