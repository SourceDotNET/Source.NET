using Source.Common;
using Source;

using Game.Shared;

namespace Game.Server;

using DEFINE = Source.DEFINE<Func_LOD>;

[LinkEntityToClass("func_lod")]
[NetworkName("CFunc_LOD")]
public partial class Func_LOD : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(DisappearMinDist)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DisappearMaxDist)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	public static readonly new DataMap DataDesc = new(typeof(Func_LOD), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(DisappearMinDist), FieldType.Float, "DisappearMinDist"),
		DEFINE.KEYFIELD(nameof(DisappearMaxDist), FieldType.Float, "DisappearMaxDist"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	[NetworkVar("m_fDisappearMinDist")]
	public partial float DisappearMinDist { get; set; }
	[NetworkVar("m_fDisappearMaxDist")]
	public partial float DisappearMaxDist { get; set; }

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
