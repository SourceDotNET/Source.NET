using Source.Common;
using Source;

using Game.Shared;

namespace Game.Server;

using FIELD = FIELD<Func_LOD>;
using DEFINE = Source.DEFINE<Func_LOD>;

[LinkEntityToClass("func_lod")]
[NetworkName("CFunc_LOD")]
public class Func_LOD : BaseEntity
{
	public static readonly SendTable DT_Func_LOD = new(DT_BaseEntity, [
		SendPropFloat(FIELD.OF(nameof(DisappearMinDist)), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF(nameof(DisappearMaxDist)), 0, PropFlags.NoScale),
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_Func_LOD);

	public static readonly new DataMap DataDesc = new(typeof(Func_LOD), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(DisappearMinDist), FieldType.Float, "DisappearMinDist"),
		DEFINE.KEYFIELD(nameof(DisappearMaxDist), FieldType.Float, "DisappearMaxDist"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	[NetworkName("m_fDisappearMinDist")]
	public float DisappearMinDist;
	[NetworkName("m_fDisappearMaxDist")]
	public float DisappearMaxDist;

	public override void Spawn() {
		SetModel(GetModelName());
		SetSolid(SolidType.BSP);
		base.Spawn();
		CreateVPhysics();
	}

	public bool CreateVPhysics() {
		VPhysicsInitStatic();
		return true;
	}

	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		if (FStrEq(keyName, "DisappearDist")) {
			DisappearMinDist = atoi(value);
			DisappearMaxDist = DisappearMinDist + 800.0f;
		}
		else if (FStrEq(keyName, "Solid")) {
			if (atoi(value) != 0)
				AddSolidFlags(SolidFlags.NotSolid);
		}
		else
			return base.KeyValue(keyName, value);

		return true;
	}
}
