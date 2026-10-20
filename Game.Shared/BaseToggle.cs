#if CLIENT_DLL || GAME_DLL
using Source;
using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;
namespace Game.Shared;
using FIELD = Source.FIELD<BaseToggle>;
[NetworkName("CBaseToggle")]
public class BaseToggle : BaseEntity
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_BaseToggle = new(DT_BaseEntity, [
#if CLIENT_DLL
		RecvPropVector(FIELD.OF(nameof(FinalDest))),
		RecvPropInt(FIELD.OF(nameof(MovementType))),
		RecvPropFloat(FIELD.OF(nameof(MoveTargetTime)))
#else
		SendPropVector(FIELD.OF(nameof(FinalDest)), 0, PropFlags.NoScale),
		SendPropInt(FIELD.OF(nameof(MovementType)), 4),
		SendPropFloat(FIELD.OF(nameof(MoveTargetTime)), 0, PropFlags.NoScale)
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_BaseToggle);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_BaseToggle);
#endif
	[NetworkName("m_vecFinalDest")]
	public Vector3 FinalDest;
	[NetworkName("m_movementType")]
	public int MovementType;
	[NetworkName("m_flMoveTargetTime")]
	public TimeUnit_t MoveTargetTime;

#if GAME_DLL
	public ToggleState ToggleState;
	public float MoveDistance;// how far a door should slide or rotate
	public float Wait;
	public float Lip;

	public Vector3 Position1;
	public Vector3 Position2;

	public QAngle MoveAng;
	public QAngle Angle1;
	public QAngle Angle2;

	public float Height;
	public EHANDLE Activator = new();
	public QAngle FinalAngle;

	public string? Master;     // If this button has a master switch, this is the targetname.
							   // A master switch must be of the multisource type. If all
							   // of the switches in the multisource have been triggered, then
							   // the button will be allowed to operate. Otherwise, it will be
							   // deactivated.

	public static readonly new DataMap DataDesc = new(typeof(BaseToggle), BaseEntity.DataDesc, [
		DEFINE<BaseToggle>.FIELD(nameof(ToggleState), FieldType.Integer),
		DEFINE<BaseToggle>.FIELD(nameof(MoveDistance), FieldType.Float),
		DEFINE<BaseToggle>.FIELD(nameof(Wait), FieldType.Float),
		DEFINE<BaseToggle>.FIELD(nameof(Lip), FieldType.Float),
		DEFINE<BaseToggle>.FIELD(nameof(Position1), FieldType.PositionVector),
		DEFINE<BaseToggle>.FIELD(nameof(Position2), FieldType.PositionVector),
		DEFINE<BaseToggle>.FIELD(nameof(MoveAng), FieldType.Vector),		// UNDONE: Position could go through transition, but also angle?
		DEFINE<BaseToggle>.FIELD(nameof(Angle1), FieldType.Vector),		// UNDONE: Position could go through transition, but also angle?
		DEFINE<BaseToggle>.FIELD(nameof(Angle2), FieldType.Vector),		// UNDONE: Position could go through transition, but also angle?
		DEFINE<BaseToggle>.FIELD(nameof(Height), FieldType.Float),
		DEFINE<BaseToggle>.FIELD(nameof(Activator), FieldType.EHandle),
		DEFINE<BaseToggle>.FIELD(nameof(FinalDest), FieldType.PositionVector),
		DEFINE<BaseToggle>.FIELD(nameof(FinalAngle), FieldType.Vector),
		DEFINE<BaseToggle>.FIELD(nameof(Master), FieldType.String),
		DEFINE<BaseToggle>.FIELD(nameof(MovementType), FieldType.Integer),	// Linear or angular movement? (togglemovetypes_t)
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		if (FStrEq(keyName, "lip"))
			Lip = strtof(value, out _);
		else if (FStrEq(keyName, "wait"))
			Wait = strtof(value, out _);
		else if (FStrEq(keyName, "master"))
			Master = new(value.SliceNullTerminatedString());
		else if (FStrEq(keyName, "distance"))
			MoveDistance = strtof(value, out _);
		else
			return base.KeyValue(keyName, value);

		return true;
	}

	public virtual float GetDelay() => Wait;
#endif
}

#if GAME_DLL
public enum ToggleState
{
	AtTop,
	AtBottom,
	GoingUp,
	GoingDown
}
#endif
#endif
