using Game.Shared;

using Source;
using Source.Common;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Game.Server;

using DEFINE = Source.DEFINE<FuncBrush>;

[LinkEntityToClass("func_brush")]
[LinkEntityToClass("func_simpleladder")]
public class FuncBrush : BaseEntity {
	public enum BrushSolidities
	{
		Toggle = 0,
		Never = 1,
		Always = 2,
	}

	public static readonly new DataMap DataDesc = new(typeof(FuncBrush), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(Disabled), FieldType.Integer, "StartDisabled"),
		DEFINE.KEYFIELD(nameof(Solidity), FieldType.Integer, "Solidity"),
		DEFINE.KEYFIELD(nameof(SolidBsp), FieldType.Boolean, "solidbsp"),
		DEFINE.KEYFIELD(nameof(ExcludedClass), FieldType.String, "excludednpc"),
		DEFINE.KEYFIELD(nameof(InvertExclusion), FieldType.Boolean, "invert_exclusion"),

		DEFINE.INPUTFUNC(FieldType.String, "SetExcluded", nameof(InputSetExcluded), (INPUTFUNCPTR)((self, data) => ((FuncBrush)self).InputSetExcluded(data))),
		DEFINE.INPUTFUNC(FieldType.Boolean, "SetInvert", nameof(InputSetInvert), (INPUTFUNCPTR)((self, data) => ((FuncBrush)self).InputSetInvert(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public BrushSolidities Solidity;
	public int Disabled;
	public bool SolidBsp;
	public string? ExcludedClass;
	public bool InvertExclusion;

	public override void Spawn() {
		SetMoveType(Source.MoveType.Push);

		SetSolid(SolidType.VPhysics);
		AddEFlags(EFL.UsePartitionWhenNotSolid);

		if (Solidity == BrushSolidities.Never)
			AddSolidFlags(SolidFlags.NotSolid);

		SetModel(GetModelName());

		if (Disabled != 0)
			TurnOff();

		if (GetEntityName() == null || ParentName == null)
			AddFlag(EntityFlags.WorldBrush);

		CreateVPhysics();

		if (SolidBsp)
			SetSolid(SolidType.BSP);
	}

	public bool CreateVPhysics() {
		VPhysicsInitShadow(false, false);
		return true;
	}

	public void InputSetExcluded(InputData inputdata) => ExcludedClass = inputdata.Value.StringID();

	public void InputSetInvert(InputData inputdata) => InvertExclusion = inputdata.Value.Bool();

	public void TurnOff() {
		if (!IsOn())
			return;

		if (Solidity != BrushSolidities.Always)
			AddSolidFlags(SolidFlags.NotSolid);

		AddEffects(EntityEffects.NoDraw);
		Disabled = 1;
	}

	public virtual bool IsOn() => !IsEffectActive(EntityEffects.NoDraw);
}
