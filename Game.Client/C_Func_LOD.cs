using Source.Common;
using Source;

using Game.Shared;

namespace Game.Client;

using FIELD = FIELD<C_Func_LOD>;

[NetworkName("CFunc_LOD")]
public class C_Func_LOD : C_BaseEntity
{
	public static readonly RecvTable DT_Func_LOD = new(DT_BaseEntity, [
		RecvPropFloat(FIELD.OF(nameof(DisappearMinDist))),
		RecvPropFloat(FIELD.OF(nameof(DisappearMaxDist))),
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_Func_LOD);

	[NetworkName("m_fDisappearMinDist")]
	public float DisappearMinDist = 5000.0f;
	[NetworkName("m_fDisappearMaxDist")]
	public float DisappearMaxDist = 5800.0f;
}
