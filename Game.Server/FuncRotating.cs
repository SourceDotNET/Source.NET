using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Mathematics;

using System.Numerics;
using System.Xml.Linq;

namespace Game.Server;


using FIELD = FIELD<FuncRotating>;
using DEFINE = Source.DEFINE<FuncRotating>;

[LinkEntityToClass("func_rotating")]
[NetworkName("CFuncRotating")]
public class FuncRotating : BaseEntity
{
	[Flags]
	public enum SF
	{
		YAxis = 0,
		StartOn = 1,
		Backwards = 2,
		ZAxis = 4,
		XAxis = 8,
		ClientSide = 16,
		SmallRadius = 128,
		MediumRadius = 256,
		LargeRadius = 512,

		AccDcc = 16,
		Hurt = 32,
		NotSolid = 64,
	}

	const int FANPITCHMIN = 30;
	const int FANPITCHMAX = 100;

	public static readonly SendTable DT_FuncRotating = new(DT_BaseEntity, [
		SendPropExclude(nameof(DT_BaseEntity), "m_angRotation"),
		SendPropExclude(nameof(DT_BaseEntity), "m_vecOrigin"),
		SendPropExclude(nameof(DT_BaseEntity), "m_flSimulationTime"),

		SendPropVector(NetworkVarFields.Origin, 0, PropFlags.Coord | PropFlags.ChangesOften, 0, Constants.HIGH_DEFAULT, SendProxy_FuncRotatingOrigin),
		SendPropAngle(FIELD.OF_VECTORELEM(nameof(Rotation), 0), 13, PropFlags.RoundDown | PropFlags.ChangesOften, proxyFn: SendProxy_FuncRotatingAngle),
		SendPropAngle(FIELD.OF_VECTORELEM(nameof(Rotation), 1), 13, PropFlags.RoundDown | PropFlags.ChangesOften, proxyFn: SendProxy_FuncRotatingAngle),
		SendPropAngle(FIELD.OF_VECTORELEM(nameof(Rotation), 2), 13, PropFlags.RoundDown | PropFlags.ChangesOften, proxyFn: SendProxy_FuncRotatingAngle),
		SendPropInt(FIELD.OF(nameof(SimulationTime)), SIMULATION_TIME_WINDOW_BITS, PropFlags.Unsigned | PropFlags.ChangesOften | PropFlags.EncodedAgainstTickCount, SendProxy_FuncRotatingSimulationTime)
	]);

	private static void SendProxy_FuncRotatingOrigin(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		SendProxy_Origin(prop, instance, field, ref outData, element, objectID);
	}

	private static void SendProxy_FuncRotatingAngle(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		FuncRotating? entity = (FuncRotating?)instance;
		Assert(entity != null);

		float qa = field.GetValue<float>(instance);

		outData.Float = MathLib.anglemod(qa);
		Assert(float.IsFinite(outData.Float));
	}

	private static void SendProxy_FuncRotatingSimulationTime(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		SendProxy_SimulationTime(prop, instance, field, ref outData, element, objectID);
	}

	public static readonly new ServerClass ServerClass = new ServerClass(DT_FuncRotating);

	public static readonly new DataMap DataDesc = new(typeof(FuncRotating), BaseEntity.DataDesc, [
		DEFINE.FIELD(nameof(MoveAng), FieldType.Vector),
		DEFINE.FIELD(nameof(FanFriction), FieldType.Float),
		DEFINE.FIELD(nameof(Attenuation), FieldType.Float),
		DEFINE.FIELD(nameof(Volume), FieldType.Float),
		DEFINE.FIELD(nameof(TargetSpeed), FieldType.Float),
		DEFINE.KEYFIELD(nameof(MaxSpeed), FieldType.Float, "maxspeed"),
		DEFINE.KEYFIELD(nameof(BlockDamage), FieldType.Float, "dmg"),
		DEFINE.KEYFIELD(nameof(NoiseRunning), FieldType.SoundName, "message"),
		DEFINE.FIELD(nameof(Reversed), FieldType.Boolean),
		DEFINE.FIELD(nameof(AngStart), FieldType.Vector),
		DEFINE.FIELD(nameof(StopAtStartPos), FieldType.Boolean),
		DEFINE.KEYFIELD(nameof(SolidBsp), FieldType.Boolean, "solidbsp"),

		DEFINE.FUNCTION(nameof(SpinUpMove)),
		DEFINE.FUNCTION(nameof(SpinDownMove)),
		DEFINE.FUNCTION(nameof(HurtTouch)),
		DEFINE.FUNCTION(nameof(RotatingUse)),
		DEFINE.FUNCTION(nameof(RotateMove)),
		DEFINE.FUNCTION(nameof(ReverseMove)),

		DEFINE.INPUTFUNC(FieldType.Float, "SetSpeed", nameof(InputSetSpeed), (INPUTFUNCPTR)((self, data) => ((FuncRotating)self).InputSetSpeed(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Start", nameof(InputStart), (INPUTFUNCPTR)((self, data) => ((FuncRotating)self).InputStart(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Stop", nameof(InputStop), (INPUTFUNCPTR)((self, data) => ((FuncRotating)self).InputStop(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Toggle", nameof(InputToggle), (INPUTFUNCPTR)((self, data) => ((FuncRotating)self).InputToggle(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Reverse", nameof(InputReverse), (INPUTFUNCPTR)((self, data) => ((FuncRotating)self).InputReverse(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "StartForward", nameof(InputStartForward), (INPUTFUNCPTR)((self, data) => ((FuncRotating)self).InputStartForward(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "StartBackward", nameof(InputStartBackward), (INPUTFUNCPTR)((self, data) => ((FuncRotating)self).InputStartBackward(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "StopAtStartPos", nameof(InputStopAtStartPos), (INPUTFUNCPTR)((self, data) => ((FuncRotating)self).InputStopAtStartPos(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	QAngle MoveAng;

	float FanFriction;
	float Attenuation;
	float Volume;
	float TargetSpeed;
	float MaxSpeed;
	float BlockDamage;
	string? NoiseRunning;
	bool Reversed;

	QAngle AngStart;
	bool StopAtStartPos;

	bool SolidBsp;

	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		if (FStrEq(keyName, "fanfriction"))
			FanFriction = strtof(value, out _) / 100;
		else if (FStrEq(keyName, "Volume")) {
			Volume = strtof(value, out _) / 10.0f;
			Volume = Math.Clamp(Volume, 0.0f, 1.0f);
		}
		else
			return base.KeyValue(keyName, value);

		return true;
	}

	public override void Spawn() {
		if (Volume == 0.0)
			Volume = 1.0f;

		if (HasSpawnFlags(SF.SmallRadius))
			Attenuation = ATTN_IDLE;
		else if (HasSpawnFlags(SF.MediumRadius))
			Attenuation = ATTN_STATIC;
		else if (HasSpawnFlags(SF.LargeRadius))
			Attenuation = ATTN_NORM;
		else
			Attenuation = ATTN_NORM;

		if (FanFriction == 0)
			FanFriction = 1;

		if (HasSpawnFlags(SF.ZAxis))
			MoveAng = new(0, 0, 1);
		else if (HasSpawnFlags(SF.XAxis))
			MoveAng = new(1, 0, 0);
		else
			MoveAng = new(0, 1, 0);

		if (HasSpawnFlags(SF.Backwards))
			MoveAng = MoveAng * -1;

		SetSolid(SolidType.VPhysics);

		if (HasSpawnFlags(SF.NotSolid)) {
			AddSolidFlags(SolidFlags.NotSolid);
			SetMoveType(Source.MoveType.Push);
		}
		else {
			RemoveSolidFlags(SolidFlags.NotSolid);
			SetMoveType(Source.MoveType.Push);
		}

		SetModel(GetModelName());

		FnUse = RotatingUse;

		MaxSpeed = MathF.Abs(MaxSpeed);
		if (MaxSpeed == 0)
			MaxSpeed = 100;

		if (HasSpawnFlags(SF.StartOn)) {
			SetThink(SUB_CallUseToggle);
			SetNextThink(gpGlobals.CurTime + .2);
		}

		if (HasSpawnFlags(SF.Hurt))
			SetTouch(HurtTouch);

		Speed = 0;

		Precache();
		CreateVPhysics();

		AngStart = GetLocalAngles();

		if (SolidBsp)
			SetSolid(SolidType.BSP);
	}

	public bool CreateVPhysics() {
		if (!IsSolidFlagSet(SolidFlags.NotSolid))
			VPhysicsInitShadow(false, false);
		return true;
	}

	public override void Precache() {
		if (string.IsNullOrEmpty(NoiseRunning))
			NoiseRunning = "DoorSound.Null";
		PrecacheScriptSound(NoiseRunning);

		if (GetLocalAngularVelocity() != vec3_angle) {
			SetMoveDone(SpinUpMove);
			SetMoveDoneTime(1.5);
		}
	}

	public void HurtTouch(BaseEntity? other) {
		if (other!.m_takedamage == 0)
			return;

		BlockDamage = GetLocalAngularVelocity().Length() / 10;

		other.TakeDamage(new TakeDamageInfo(this, this, BlockDamage, DamageType.Crush));

		Vector3 newVelocity = other.GetAbsOrigin() - WorldSpaceCenter();
		MathLib.VectorNormalize(ref newVelocity);
		newVelocity *= BlockDamage;
		other.SetAbsVelocity(newVelocity);
	}

	public void RampPitchVol() {
		float fpct = MathF.Abs(Speed) / MaxSpeed;
		float fvol = Math.Clamp(Volume * fpct, 0.0f, 1.0f);

		float fpitch = FANPITCHMIN + (FANPITCHMAX - FANPITCHMIN) * fpct;

		int pitch = Math.Clamp(MathLib.FastFloatToSmallInt(fpitch), 0, 255);
		if (pitch == PITCH_NORM)
			pitch = PITCH_NORM - 1;

		PASAttenuationFilter filter = new(GetAbsOrigin(), Attenuation);
		filter.MakeReliable();

		scoped EmitSound_t ep = new();
		ep.Channel = (int)SoundEntityChannel.Static;
		ep.SoundName = NoiseRunning;
		ep.Volume = fvol;
		ep.SoundLevel = ATTN_TO_SNDLVL(Attenuation);
		ep.Flags = SoundFlags.ChangePitch | SoundFlags.ChangeVolume;
		ep.Pitch = pitch;

		EmitSound(filter, EntIndex(), ep);
	}

	public TimeUnit_t GetNextMoveInterval() {
		if (StopAtStartPos)
			return TICK_INTERVAL;
		return 0.1;
	}

	public void UpdateSpeed(float newSpeed) {
		float oldSpeed = Speed;
		Speed = Math.Clamp(newSpeed, -MaxSpeed, MaxSpeed);

		if (StopAtStartPos) {
			int checkAxis = 2;
			if (MoveAng[0] != 0)
				checkAxis = 0;
			else if (MoveAng[1] != 0)
				checkAxis = 1;

			float angDelta = MathLib.anglemod(GetLocalAngles()[checkAxis] - AngStart[checkAxis]);
			if (angDelta > 180.0f)
				angDelta -= 360.0f;

			if (newSpeed < 100) {
				if (newSpeed <= 25 && MathF.Abs(angDelta) < 1.0f) {
					TargetSpeed = 0;
					StopAtStartPos = false;
					Speed = 0.0f;

					SetLocalAngles(AngStart);
				}
				else if (MathF.Abs(angDelta) > 90.0f)
					Speed = oldSpeed;
				else {
					float minSpeed = MathF.Abs(angDelta);
					if (minSpeed < 20)
						minSpeed = 20;

					Speed = oldSpeed > 0.0f ? minSpeed : -minSpeed;
				}
			}
		}

		if ((oldSpeed == 0) && (Speed != 0)) {
			PASAttenuationFilter filter = new(GetAbsOrigin(), Attenuation);
			filter.MakeReliable();

			scoped EmitSound_t ep = new();
			ep.Channel = (int)SoundEntityChannel.Static;
			ep.SoundName = NoiseRunning;
			ep.Volume = 0.01f;
			ep.SoundLevel = ATTN_TO_SNDLVL(Attenuation);
			ep.Pitch = FANPITCHMIN;

			EmitSound(filter, EntIndex(), ep);
			RampPitchVol();
		}
		else if ((oldSpeed != 0) && (Speed == 0))
			StopSound(EntIndex(), (int)SoundEntityChannel.Static, NoiseRunning);
		else
			RampPitchVol();

		SetLocalAngularVelocity(MoveAng * Speed);
	}

	public void SpinUpMove() {
		bool spinUpDone = false;
		float newSpeed = MathF.Abs(Speed) + 0.2f * MaxSpeed * FanFriction;
		if (MathF.Abs(newSpeed) >= MathF.Abs(TargetSpeed)) {
			newSpeed = TargetSpeed;
			spinUpDone = !StopAtStartPos;
		}
		else if (TargetSpeed < 0)
			newSpeed *= -1;

		UpdateSpeed(newSpeed);

		if (spinUpDone) {
			SetMoveDone(RotateMove);
			RotateMove();
		}

		SetMoveDoneTime(GetNextMoveInterval());
	}

	protected bool SpinDown(float targetSpeed) {
		bool spinDownDone = false;
		float newSpeed = MathF.Abs(Speed) - 0.1f * MaxSpeed * FanFriction;
		if (newSpeed < 0)
			newSpeed = 0;

		if (MathF.Abs(newSpeed) <= MathF.Abs(targetSpeed)) {
			newSpeed = targetSpeed;
			spinDownDone = !StopAtStartPos;
		}
		else if (Speed < 0)
			newSpeed *= -1;

		UpdateSpeed(newSpeed);

		return spinDownDone;
	}

	public void SpinDownMove() {
		if (SpinDown(TargetSpeed)) {
			SetMoveDone(RotateMove);
			RotateMove();
		}
		else
			SetMoveDoneTime(GetNextMoveInterval());
	}

	public void ReverseMove() {
		if (SpinDown(0))
			SetTargetSpeed(TargetSpeed);
		else
			SetMoveDoneTime(GetNextMoveInterval());
	}

	public void RotateMove() {
		SetMoveDoneTime(10);

		if (StopAtStartPos) {
			SetMoveDoneTime(GetNextMoveInterval());
			int checkAxis = 2;

			if (MoveAng[0] != 0)
				checkAxis = 0;
			else if (MoveAng[1] != 0)
				checkAxis = 1;

			float angDelta = MathLib.anglemod(GetLocalAngles()[checkAxis] - AngStart[checkAxis]);
			if (angDelta > 180.0f)
				angDelta -= 360.0f;

			QAngle avel = GetLocalAngularVelocity();
			QAngle avelpertick = avel * (float)TICK_INTERVAL;

			if (MathF.Abs(angDelta) < MathF.Abs(avelpertick[checkAxis])) {
				SetTargetSpeed(0);
				SetLocalAngles(AngStart);
				StopAtStartPos = false;
			}
		}
	}

	protected float GetMoveSpeed(float speed) {
		if (MoveAng[0] != 0)
			return speed * MoveAng[0];

		if (MoveAng[1] != 0)
			return speed * MoveAng[1];

		return speed * MoveAng[2];
	}

	public void SetTargetSpeed(float speed) {
		speed = MathF.Abs(speed);
		if (Reversed)
			speed *= -1;

		TargetSpeed = speed;

		if (!HasSpawnFlags(SF.AccDcc)) {
			UpdateSpeed(TargetSpeed);
			SetMoveDone(RotateMove);
		}
		else {
			if (((Speed > 0) && (TargetSpeed < 0)) ||
				((Speed < 0) && (TargetSpeed > 0)))
				SetMoveDone(ReverseMove);
			else if (MathF.Abs(Speed) < MathF.Abs(TargetSpeed))
				SetMoveDone(SpinUpMove);
			else if (MathF.Abs(Speed) > MathF.Abs(TargetSpeed))
				SetMoveDone(SpinDownMove);
			else
				SetMoveDone(RotateMove);
		}

		SetMoveDoneTime(GetNextMoveInterval());
	}

	public void RotatingUse(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		if (Speed != 0)
			SetTargetSpeed(0);
		else
			SetTargetSpeed(MaxSpeed);
	}

	public void InputReverse(InputData inputdata) {
		StopAtStartPos = false;
		Reversed = !Reversed;
		SetTargetSpeed(Speed);
	}

	public void InputSetSpeed(InputData inputdata) {
		StopAtStartPos = false;
		float speed = inputdata.Value.Float();
		Reversed = speed < 0;
		speed = MathF.Abs(speed);
		SetTargetSpeed(Math.Clamp(speed, 0.0f, 1.0f) * MaxSpeed);
	}

	public void InputStart(InputData inputdata) {
		StopAtStartPos = false;
		SetTargetSpeed(MaxSpeed);
	}

	public void InputStartForward(InputData inputdata) {
		Reversed = false;
		SetTargetSpeed(MaxSpeed);
	}

	public void InputStartBackward(InputData inputdata) {
		StopAtStartPos = false;
		Reversed = true;
		SetTargetSpeed(MaxSpeed);
	}

	public void InputStop(InputData inputdata) {
		StopAtStartPos = false;
		SetTargetSpeed(0);
	}

	public void InputStopAtStartPos(InputData inputdata) {
		StopAtStartPos = true;
		SetTargetSpeed(0);
		SetMoveDoneTime(GetNextMoveInterval());
	}

	public void InputToggle(InputData inputdata) {
		if (Speed > 0)
			SetTargetSpeed(0);
		else
			SetTargetSpeed(MaxSpeed);
	}

	public override void Blocked(BaseEntity? other) {
		other!.TakeDamage(new TakeDamageInfo(this, this, BlockDamage, DamageType.Crush));
	}
}
