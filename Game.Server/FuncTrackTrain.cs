using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

namespace Game.Server;

using DEFINE = Source.DEFINE<FuncTrackTrain>;

public enum TrainVelocityType
{
	Instantaneous = 0,
	LinearBlend,
	EaseInEaseOut,
}

public enum TrainOrientationType
{
	Fixed = 0,
	AtPathTracks,
	LinearBlend,
	EaseInEaseOut,
}

[LinkEntityToClass("func_tracktrain")]
[NetworkName("CFuncTrackTrain")]
public class FuncTrackTrain : BaseEntity
{
	[Flags]
	public enum SF
	{
		NoPitch = 0x0001,
		NoControl = 0x0002,
		ForwardOnly = 0x0004,
		Passable = 0x0008,
		FixedOrientation = 0x0010,
		HL1Train = 0x0080,
		UseMaxSpeedForPitch = 0x0100,
		UnblockableByPlayer = 0x0200,
	}

	public const int TRAIN_ACTIVE = 0x80;
	public const int TRAIN_NEW = 0xc0;
	public const int TRAIN_OFF = 0x00;
	public const int TRAIN_NEUTRAL = 0x01;
	public const int TRAIN_SLOW = 0x02;
	public const int TRAIN_MEDIUM = 0x03;
	public const int TRAIN_FAST = 0x04;
	public const int TRAIN_BACK = 0x05;

	const float TRAIN_MAXSPEED = 1000;

	public static readonly SendTable DT_FuncTrackTrain = new(DT_BaseEntity, []);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_FuncTrackTrain);

	public static readonly new DataMap DataDesc = new(typeof(FuncTrackTrain), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(Length), FieldType.Float, "wheels"),
		DEFINE.KEYFIELD(nameof(Height), FieldType.Float, "height"),
		DEFINE.KEYFIELD(nameof(MaxSpeed), FieldType.Float, "startspeed"),
		DEFINE.KEYFIELD(nameof(Bank), FieldType.Float, "bank"),
		DEFINE.KEYFIELD(nameof(BlockDamage), FieldType.Float, "dmg"),
		DEFINE.KEYFIELD(nameof(SoundMove), FieldType.SoundName, "MoveSound"),
		DEFINE.KEYFIELD(nameof(SoundMovePing), FieldType.SoundName, "MovePingSound"),
		DEFINE.KEYFIELD(nameof(SoundStart), FieldType.SoundName, "StartSound"),
		DEFINE.KEYFIELD(nameof(SoundStop), FieldType.SoundName, "StopSound"),
		DEFINE.KEYFIELD(nameof(MoveSoundMinPitch), FieldType.Integer, "MoveSoundMinPitch"),
		DEFINE.KEYFIELD(nameof(MoveSoundMaxPitch), FieldType.Integer, "MoveSoundMaxPitch"),
		DEFINE.KEYFIELD(nameof(MoveSoundMinTime), FieldType.Float, "MoveSoundMinTime"),
		DEFINE.KEYFIELD(nameof(MoveSoundMaxTime), FieldType.Float, "MoveSoundMaxTime"),
		DEFINE.FIELD(nameof(NextMoveSoundTime), FieldType.Time),
		DEFINE.KEYFIELD(nameof(VelocityType), FieldType.Integer, "velocitytype"),
		DEFINE.KEYFIELD(nameof(OrientationType), FieldType.Integer, "orientationtype"),

		DEFINE.FIELD(nameof(Path), FieldType.ClassPtr),
		DEFINE.FIELD(nameof(Dir), FieldType.Float),
		DEFINE.FIELD(nameof(ControlMins), FieldType.Vector),
		DEFINE.FIELD(nameof(ControlMaxs), FieldType.Vector),
		DEFINE.FIELD(nameof(Volume), FieldType.Float),
		DEFINE.FIELD(nameof(OldSpeed), FieldType.Float),

		DEFINE.FIELD(nameof(SoundPlaying), FieldType.Boolean),

		DEFINE.KEYFIELD(nameof(ManualSpeedChanges), FieldType.Boolean, "ManualSpeedChanges"),
		DEFINE.KEYFIELD(nameof(AccelSpeed), FieldType.Float, "ManualAccelSpeed"),
		DEFINE.KEYFIELD(nameof(DecelSpeed), FieldType.Float, "ManualDecelSpeed"),

		DEFINE.INPUTFUNC(FieldType.Void, "Stop", nameof(InputStop), (INPUTFUNCPTR)((self, data) => ((FuncTrackTrain)self).InputStop(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "StartForward", nameof(InputStartForward), (INPUTFUNCPTR)((self, data) => ((FuncTrackTrain)self).InputStartForward(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "StartBackward", nameof(InputStartBackward), (INPUTFUNCPTR)((self, data) => ((FuncTrackTrain)self).InputStartBackward(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Toggle", nameof(InputToggle), (INPUTFUNCPTR)((self, data) => ((FuncTrackTrain)self).InputToggle(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Resume", nameof(InputResume), (INPUTFUNCPTR)((self, data) => ((FuncTrackTrain)self).InputResume(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Reverse", nameof(InputReverse), (INPUTFUNCPTR)((self, data) => ((FuncTrackTrain)self).InputReverse(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetSpeed", nameof(InputSetSpeed), (INPUTFUNCPTR)((self, data) => ((FuncTrackTrain)self).InputSetSpeed(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetSpeedDir", nameof(InputSetSpeedDir), (INPUTFUNCPTR)((self, data) => ((FuncTrackTrain)self).InputSetSpeedDir(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetSpeedReal", nameof(InputSetSpeedReal), (INPUTFUNCPTR)((self, data) => ((FuncTrackTrain)self).InputSetSpeedReal(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetSpeedDirAccel", nameof(InputSetSpeedDirAccel), (INPUTFUNCPTR)((self, data) => ((FuncTrackTrain)self).InputSetSpeedDirAccel(data))),
		DEFINE.INPUTFUNC(FieldType.String, "TeleportToPathTrack", nameof(InputTeleportToPathTrack), (INPUTFUNCPTR)((self, data) => ((FuncTrackTrain)self).InputTeleportToPathTrack(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "SetSpeedForwardModifier", nameof(InputSetSpeedForwardModifier), (INPUTFUNCPTR)((self, data) => ((FuncTrackTrain)self).InputSetSpeedForwardModifier(data))),

		DEFINE.OUTPUT(nameof(OnStart), "OnStart", eventFuncs),
		DEFINE.OUTPUT(nameof(OnNext), "OnNextPoint", eventFuncs),

		DEFINE.FUNCTION(nameof(Next)),
		DEFINE.FUNCTION(nameof(Find)),
		DEFINE.FUNCTION(nameof(NearestPath)),
		DEFINE.FUNCTION(nameof(DeadEnd)),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public PathTrack? Path;
	public float Length;

	Vector3 ControlMins;
	Vector3 ControlMaxs;
	Vector3 LastBlockPos;
	long LastBlockTick;
	float Volume;
	float Bank;
	float OldSpeed;
	float BlockDamage;
	float Height;
	float MaxSpeed;
	float Dir;

	string? SoundMove;
	string? SoundMovePing;
	string? SoundStart;
	string? SoundStop;

	float MoveSoundMinTime;
	float MoveSoundMaxTime;
	TimeUnit_t NextMoveSoundTime;

	int MoveSoundMinPitch;
	int MoveSoundMaxPitch;

	TrainOrientationType OrientationType;
	TrainVelocityType VelocityType;
	bool SoundPlaying;

	readonly OutputEvent OnStart = new();
	readonly OutputEvent OnNext = new();

	bool ManualSpeedChanges;
	float DesiredSpeed;
	TimeUnit_t SpeedChangeTime;
	float AccelSpeed;
	float DecelSpeed;
	bool AccelToSpeed;

	TimeUnit_t NextMPSoundTime;

	float SpeedForwardModifier;
	float UnmodifiedDesiredSpeed;

	bool DamageChild;

	public FuncTrackTrain() {
		OrientationType = TrainOrientationType.AtPathTracks;
		VelocityType = TrainVelocityType.Instantaneous;
		LastBlockPos = Vector3.Zero;
		LastBlockTick = gpGlobals.TickCount;

		SpeedForwardModifier = 1.0f;
		UnmodifiedDesiredSpeed = 0.0f;

		DamageChild = false;
	}

	public override int DrawDebugTextOverlays() {
		int offset = base.DrawDebugTextOverlays();

		if ((DebugOverlays & DebugOverlayBits.Text) != 0) {
			QAngle angles = GetLocalAngles();
			EntityText(offset, $"angles: {angles.X:G} {angles.Y:G} {angles.Z:G}", 0);
			offset++;

			float curSpeed = GetLocalVelocity().Length();
			EntityText(offset, $"current speed (goal): {curSpeed:G} ({Speed:G})", 0);
			offset++;

			EntityText(offset, $"max speed: {MaxSpeed:G}", 0);
			offset++;
		}

		return offset;
	}

	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		if (FStrEq(keyName, "volume")) {
			Volume = atoi(value);
			Volume *= 0.1f;
		}
		else
			return base.KeyValue(keyName, value);

		return true;
	}

	public void InputStop(InputData inputdata) => Stop();

	public void InputResume(InputData inputdata) {
		Speed = OldSpeed;
		Start();
	}

	public void InputReverse(InputData inputdata) {
		SetDirForward(!IsDirForward());
		SetSpeed(Speed);
	}

	public bool IsDirForward() => Dir == 1;

	public void SetDirForward(bool forward) {
		if (forward && Dir != 1) {
			if (Path != null && Path.GetPrevious() != null)
				Path = Path.GetPrevious();

			Dir = 1;
		}
		else if (!forward && Dir != -1) {
			if (Path != null && Path.GetNext() != null)
				Path = Path.GetNext();

			Dir = -1;
		}
	}

	public void InputStartForward(InputData inputdata) {
		SetDirForward(true);
		SetSpeed(MaxSpeed);
	}

	public void InputStartBackward(InputData inputdata) {
		SetDirForward(false);
		SetSpeed(MaxSpeed);
	}

	public void Start() {
		OnStart.FireOutput(this, this);
		Next();
	}

	public void InputToggle(InputData inputdata) {
		if (Speed == 0)
			SetSpeed(MaxSpeed);
		else
			SetSpeed(0);
	}

	public override void Use(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		if (useType == UseType.Set) {
			float delta = value;

			delta = ((int)(Speed * 4) / (int)MaxSpeed) * 0.25f + 0.25f * delta;
			if (delta > 1)
				delta = 1;
			else if (delta < -0.25f)
				delta = -0.25f;
			if (HasSpawnFlags(SF.ForwardOnly)) {
				if (delta < 0)
					delta = 0;
			}
			SetDirForward(delta >= 0);
			delta = MathF.Abs(delta);
			SetSpeed(MaxSpeed * delta);
		}
	}

	public void InputSetSpeedReal(InputData inputdata) => SetSpeed(Math.Clamp(inputdata.Value.Float(), 0.0f, MaxSpeed));

	public void InputSetSpeed(InputData inputdata) {
		float scale = Math.Clamp(inputdata.Value.Float(), 0.0f, 1.0f);
		SetSpeed(MaxSpeed * scale);
	}

	public void InputSetSpeedDir(InputData inputdata) {
		float newSpeed = inputdata.Value.Float();
		SetDirForward(newSpeed >= 0);
		newSpeed = MathF.Abs(newSpeed);
		float scale = Math.Clamp(newSpeed, 0.0f, 1.0f);
		SetSpeed(MaxSpeed * scale);
	}

	public void InputSetSpeedDirAccel(InputData inputdata) => SetSpeedDirAccel(inputdata.Value.Float());

	public void SetSpeedDirAccel(float newSpeed) {
		SetDirForward(newSpeed >= 0);
		newSpeed = MathF.Abs(newSpeed);
		float scale = Math.Clamp(newSpeed, 0.0f, 1.0f);
		SetSpeed(MaxSpeed * scale, true);
	}

	public void InputSetSpeedForwardModifier(InputData inputdata) => SetSpeedForwardModifier(inputdata.Value.Float());

	public void SetSpeedForwardModifier(float modifier) {
		float speedForwardModifier = MathF.Abs(modifier);

		SpeedForwardModifier = Math.Clamp(speedForwardModifier, 0.0f, 1.0f);
		SetSpeed(UnmodifiedDesiredSpeed, true);
	}

	public void InputTeleportToPathTrack(InputData inputdata) {
		ReadOnlySpan<char> name = inputdata.Value.String();
		PathTrack? track = gEntList.FindEntityByName(null, name) as PathTrack;

		if (track != null) {
			TeleportToPathTrack(track);
			Path = track;
		}
	}

	public void SetSpeed(float speed, bool accel = false) {
		AccelToSpeed = accel;

		UnmodifiedDesiredSpeed = speed;
		float oldSpeed = Speed;

		if (SpeedForwardModifier < 1.0 && Dir > 0)
			speed = speed * SpeedForwardModifier;

		if (AccelToSpeed) {
			DesiredSpeed = MathF.Abs(speed) * Dir;
			SpeedChangeTime = gpGlobals.CurTime;

			if (Speed == 0 && MathF.Abs(DesiredSpeed) > 0)
				Speed = 0.1f;

			Start();

			return;
		}

		Speed = MathF.Abs(speed) * Dir;

		if (Speed != oldSpeed) {
			if (Speed != 0) {
				if (oldSpeed == 0)
					Start();
				else
					Next();
			}
			else
				Stop();
		}

		DevMsg(2, $"TRAIN({GetDebugName()}), speed to {Speed:F2}\n");
	}

	public void Stop() {
		SetLocalVelocity(vec3_origin);
		SetLocalAngularVelocity(vec3_angle);
		OldSpeed = Speed;
		Speed = 0;
		SoundStopPlaying();
		SetThink(null);
	}

	static BaseEntity? FindPhysicsBlockerForHierarchy(BaseEntity parentEntity) {
		List<BaseEntity> list = [];
		Hierarchy.GetAllInHierarchy(parentEntity, list);
		BaseEntity? physicsBlocker = null;
		float maxForce = 0;
		for (int i = 0; i < list.Count; i++) {
			IPhysicsObject? physics = list[i].VPhysicsGetObject();
			if (physics != null) {
				IPhysicsFrictionSnapshot snapshot = physics.CreateFrictionSnapshot();
				while (snapshot.IsValid()) {
					IPhysicsObject other = snapshot.GetObject(1)!;
					BaseEntity otherEntity = (BaseEntity)other.GetGameData()!;
					if (otherEntity.GetMoveType() == Source.MoveType.VPhysics) {
						snapshot.GetSurfaceNormal(out Vector3 normal);
						float dot = Vector3.Dot(parentEntity.GetAbsVelocity(), snapshot.GetNormalForce() * normal);
						if (physicsBlocker == null || dot > maxForce) {
							physicsBlocker = otherEntity;
							maxForce = dot;
						}
					}
					snapshot.NextFrictionData();
				}
				physics.DestroyFrictionSnapshot(snapshot);
			}
		}
		return physicsBlocker;
	}

	public override void Blocked(BaseEntity? other) {
		if ((other!.GetFlags() & EntityFlags.OnGround) != 0 && other.GetGroundEntity() == this) {
			DevMsg(1, $"TRAIN({GetDebugName()}): Blocked by {other.GetClassname()}\n");
			float deltaSpeed = MathF.Abs(Speed);
			if (deltaSpeed > 50)
				deltaSpeed = 50;

			other.GetVelocity(out Vector3 newVelocity, out _);
			if (newVelocity.Z == 0)
				other.ApplyAbsVelocityImpulse(new Vector3(0, 0, deltaSpeed));
			return;
		}
		else {
			Vector3 newVelocity = other.GetAbsOrigin() - GetAbsOrigin();
			MathLib.VectorNormalize(ref newVelocity);
			newVelocity *= BlockDamage;
			other.SetAbsVelocity(newVelocity);
		}
		if (HasSpawnFlags(SF.UnblockableByPlayer)) {
			BaseEntity? physicsBlocker = FindPhysicsBlockerForHierarchy(this);
			if (physicsBlocker != null) {
				long ticksBlocked = gpGlobals.TickCount - LastBlockTick;
				float dist = 0.0f;
				const int MIN_BLOCKED_TICKS = 10;
				if (ticksBlocked > MIN_BLOCKED_TICKS) {
					dist = (GetAbsOrigin() - LastBlockPos).Length();
					float minLength = GetAbsVelocity().Length() * (float)TICK_INTERVAL * MIN_BLOCKED_TICKS * 0.10f;
					if (dist < minLength)
						EntityPhysics_CreateSolver(this, physicsBlocker, true, 4.0f);
				}
				if (dist > 1.0f || LastBlockTick < 0) {
					LastBlockPos = GetAbsOrigin();
					LastBlockTick = gpGlobals.TickCount;
				}
			}
			if (other.IsPlayer())
				return;
		}

		DevWarning(2, $"TRAIN({GetDebugName()}): Blocked by {other.GetClassname()} (dmg:{BlockDamage:F2})\n");
		if (BlockDamage <= 0)
			return;

		other.TakeDamage(new TakeDamageInfo(this, this, BlockDamage, DamageType.Crush));
	}

	public void SoundStopPlaying() {
		if (SoundPlaying) {
			if (!string.IsNullOrEmpty(SoundMove))
				StopSound(EntIndex(), (int)SoundEntityChannel.Static, SoundMove);

			if (!string.IsNullOrEmpty(SoundStop)) {
				PASAttenuationFilter filter = new(this);

				scoped EmitSound_t ep = new();
				ep.Channel = (int)SoundEntityChannel.Item;
				ep.SoundName = SoundStop;
				ep.Volume = Volume;
				ep.SoundLevel = SoundLevel.LvlNorm;

				EmitSound(filter, EntIndex(), ep);
			}
		}

		SoundPlaying = false;
	}

	public void SoundUpdate() {
		if (string.IsNullOrEmpty(SoundMove) && string.IsNullOrEmpty(SoundStart) && string.IsNullOrEmpty(SoundMovePing))
			return;

		if (g_pGameRules.IsMultiplayer() && SoundPlaying) {
			if (NextMPSoundTime > gpGlobals.CurTime)
				return;

			NextMPSoundTime = gpGlobals.CurTime + 1.0;
		}

		float speedRatio;
		if (HasSpawnFlags(SF.UseMaxSpeedForPitch))
			speedRatio = Math.Clamp(MathF.Abs(Speed) / MaxSpeed, 0.0f, 1.0f);
		else
			speedRatio = Math.Clamp(MathF.Abs(Speed) / TRAIN_MAXSPEED, 0.0f, 1.0f);

		float pitch = MathLib.RemapVal(speedRatio, 0, 1, MoveSoundMinPitch, MoveSoundMaxPitch);

		PASAttenuationFilter filter = new(this);
		PASAttenuationFilter filterReliable = new(this);
		filterReliable.MakeReliable();

		Vector3 worldSpaceCenter = WorldSpaceCenter();

		if (!SoundPlaying) {
			if (!string.IsNullOrEmpty(SoundStart)) {
				scoped EmitSound_t ep = new();
				ep.Channel = (int)SoundEntityChannel.Item;
				ep.SoundName = SoundStart;
				ep.Volume = Volume;
				ep.SoundLevel = SoundLevel.LvlNorm;
				ep.Origin = ref worldSpaceCenter;

				EmitSound(filter, EntIndex(), ep);
			}

			if (!string.IsNullOrEmpty(SoundMove)) {
				scoped EmitSound_t ep = new();
				ep.Channel = (int)SoundEntityChannel.Static;
				ep.SoundName = SoundMove;
				ep.Volume = Volume;
				ep.SoundLevel = SoundLevel.LvlNorm;
				ep.Pitch = (int)pitch;
				ep.Origin = ref worldSpaceCenter;

				EmitSound(filterReliable, EntIndex(), ep);
			}

			NextMoveSoundTime = gpGlobals.CurTime + MathLib.RemapVal(speedRatio, 0, 1, MoveSoundMaxTime, MoveSoundMinTime);

			SoundPlaying = true;
		}
		else {
			if (!string.IsNullOrEmpty(SoundMove)) {
				scoped EmitSound_t ep = new();
				ep.Channel = (int)SoundEntityChannel.Static;
				ep.SoundName = SoundMove;
				ep.Volume = Volume;
				ep.SoundLevel = SoundLevel.LvlNorm;
				ep.Pitch = (int)pitch;
				ep.Flags = SoundFlags.ChangePitch;
				ep.Origin = ref worldSpaceCenter;

				if (g_pGameRules.IsMultiplayer())
					EmitSound(filter, EntIndex(), ep);
				else
					EmitSound(filterReliable, EntIndex(), ep);
			}

			if (!string.IsNullOrEmpty(SoundMovePing) && gpGlobals.CurTime > NextMoveSoundTime) {
				EmitSound(SoundMovePing);
				NextMoveSoundTime = gpGlobals.CurTime + MathLib.RemapVal(speedRatio, 0, 1, MoveSoundMaxTime, MoveSoundMinTime);
			}
		}
	}

	void ArriveAtNode(PathTrack node) {
		FirePassInputs(node, node.GetNext(), true);

		if (node.HasSpawnFlags(PathTrack.SF.DisableTrain))
			AddSpawnFlags(SF.NoControl);

		if (HasSpawnFlags(SF.NoControl)) {
			if (node.Speed != 0) {
				SetSpeed(node.Speed);
				DevMsg(2, $"TrackTrain {GetDebugName()} arrived at {node.GetDebugName()}, speed to {node.Speed,4:F2}\n");
			}
		}
	}

	TrainVelocityType GetTrainVelocityType() => VelocityType;

	void UpdateTrainVelocity(PathTrack? prev, PathTrack? next, in Vector3 nextPos, float interval) {
		switch (GetTrainVelocityType()) {
			case TrainVelocityType.Instantaneous: {
					Vector3 velDesired = nextPos - GetLocalOrigin();
					MathLib.VectorNormalize(ref velDesired);
					velDesired *= MathF.Abs(Speed);
					SetLocalVelocity(velDesired);
					break;
				}

			case TrainVelocityType.LinearBlend:
			case TrainVelocityType.EaseInEaseOut: {
					if (AccelToSpeed) {
						float prevSpeed = Speed;
						float nextSpeed = DesiredSpeed;

						if (prevSpeed != nextSpeed) {
							float speedChangeTime = (MathF.Abs(nextSpeed) > MathF.Abs(prevSpeed)) ? AccelSpeed : DecelSpeed;
							Speed = MathLib.Approach(DesiredSpeed, Speed, speedChangeTime * (float)gpGlobals.FrameTime);
						}
					}
					else if (prev != null && next != null) {
						float prevSpeed = Speed;
						if (prev.Speed != 0)
							prevSpeed = prev.Speed;

						float nextSpeed = prevSpeed;
						if (next.Speed != 0)
							nextSpeed = next.Speed;

						if (prevSpeed != nextSpeed) {
							Vector3 segment = next.GetLocalOrigin() - prev.GetLocalOrigin();
							float segmentLen = segment.Length();
							if (segmentLen != 0) {
								Vector3 curOffset = GetLocalOrigin() - prev.GetLocalOrigin();
								float p = curOffset.Length() / segmentLen;
								if (GetTrainVelocityType() == TrainVelocityType.EaseInEaseOut)
									p = (float)MathLib.SimpleSplineRemapVal(p, 0.0f, 1.0f, 0.0f, 1.0f);

								Speed = Dir * (prevSpeed * (1 - p) + nextSpeed * p);
							}
						}
						else
							Speed = Dir * prevSpeed;
					}

					Vector3 velDesired = nextPos - GetLocalOrigin();
					MathLib.VectorNormalize(ref velDesired);
					velDesired *= MathF.Abs(Speed);
					SetLocalVelocity(velDesired);
					break;
				}
		}
	}

	TrainOrientationType GetTrainOrientationType() => OrientationType;

	void UpdateTrainOrientation(PathTrack? prev, PathTrack? next, in Vector3 nextPos, float interval) {
		if (HasSpawnFlags(SF.FixedOrientation))
			return;

		Assert(prev == null || prev.GetMoveParent() == GetMoveParent());

		switch (GetTrainOrientationType()) {
			case TrainOrientationType.Fixed:
				break;

			case TrainOrientationType.AtPathTracks:
				UpdateOrientationAtPathTracks(prev, next, nextPos, interval);
				break;

			case TrainOrientationType.EaseInEaseOut:
			case TrainOrientationType.LinearBlend:
				UpdateOrientationBlend(GetTrainOrientationType(), prev!, next, nextPos, interval);
				break;
		}
	}

	void UpdateOrientationAtPathTracks(PathTrack? prev, PathTrack? next, in Vector3 nextPos, float interval) {
		if (Path == null)
			return;

		Vector3 nextFront = GetLocalOrigin();

		PathTrack? nextNode;

		nextFront.Z -= Height;
		if (Length > 0)
			Path.LookAhead(ref nextFront, IsDirForward() ? Length : -Length, 0, out nextNode);
		else
			Path.LookAhead(ref nextFront, IsDirForward() ? 100 : -100, 0, out nextNode);
		nextFront.Z += Height;

		Vector3 faceDir = nextFront - GetLocalOrigin();
		if (!IsDirForward())
			faceDir *= -1;

		MathLib.VectorAngles(faceDir, out QAngle angles);
		FixupAngles(ref angles);

		if (ManualSpeedChanges) {
			if (nextNode != null && nextNode.GetOrientationType() == TrackOrientationType.FacePathAngles)
				angles = nextNode.GetOrientation(IsDirForward());
		}

		QAngle curAngles = GetLocalAngles();
		FixupAngles(ref curAngles);

		if (prev == null || (faceDir.X == 0 && faceDir.Y == 0))
			angles = curAngles;

		DoUpdateOrientation(curAngles, angles, interval);
	}

	void UpdateOrientationBlend(TrainOrientationType orientationType, PathTrack prev, PathTrack? next, in Vector3 nextPos, float interval) {
		QAngle angPrev = prev.GetOrientation(IsDirForward());
		FixupAngles(ref angPrev);

		QAngle angNext;
		if (next != null) {
			angNext = next.GetOrientation(IsDirForward());
			FixupAngles(ref angNext);
		}
		else
			angNext = angPrev;

		if (HasSpawnFlags(SF.NoPitch))
			angNext[PITCH] = angPrev[PITCH];

		float p = 0;
		if (prev != null && angPrev != angNext) {
			Vector3 segment = next!.GetLocalOrigin() - prev.GetLocalOrigin();
			float segmentLen = segment.Length();
			if (segmentLen != 0) {
				Vector3 curOffset = GetLocalOrigin() - prev.GetLocalOrigin();
				p = curOffset.Length() / segmentLen;
			}
		}

		if (orientationType == TrainOrientationType.EaseInEaseOut)
			p = (float)MathLib.SimpleSplineRemapVal(p, 0.0f, 1.0f, 0.0f, 1.0f);

		MathLib.AngleQuaternion(angPrev, out Quaternion qtPrev);
		MathLib.AngleQuaternion(angNext, out Quaternion qtNext);

		QAngle angNew = angNext;
		float angleDiff = MathLib.QuaternionAngleDiff(qtPrev, qtNext);
		if (angleDiff != 0) {
			MathLib.QuaternionSlerp(qtPrev, qtNext, p, out Quaternion qtNew);
			MathLib.QuaternionAngles(qtNew, out angNew);
		}

		if (HasSpawnFlags(SF.NoPitch))
			angNew[PITCH] = angPrev[PITCH];

		DoUpdateOrientation(GetLocalAngles(), angNew, interval);
	}

	void DoUpdateOrientation(in QAngle curAngles, in QAngle angles, float interval) {
		float vy, vx;
		if (!HasSpawnFlags(SF.NoPitch))
			vx = MathLib.AngleDistance(angles.X, curAngles.X);
		else
			vx = 0;

		vy = MathLib.AngleDistance(angles.Y, curAngles.Y);

		if (MathF.Abs(vx) < 0.1)
			vx = 0;
		if (MathF.Abs(vy) < 0.1)
			vy = 0;

		if (interval == 0)
			interval = 0.1f;

		QAngle angVel = new(vx / interval, vy / interval, GetLocalAngularVelocity().Z);

		if (Bank != 0) {
			if (angVel.Y < -5)
				angVel.Z = MathLib.AngleDistance(MathLib.ApproachAngle(-Bank, curAngles.Z, Bank * 2), curAngles.Z);
			else if (angVel.Y > 5)
				angVel.Z = MathLib.AngleDistance(MathLib.ApproachAngle(Bank, curAngles.Z, Bank * 2), curAngles.Z);
			else
				angVel.Z = MathLib.AngleDistance(MathLib.ApproachAngle(0, curAngles.Z, Bank * 4), curAngles.Z) * 4;
		}

		SetLocalAngularVelocity(angVel);
	}

	void TeleportToPathTrack(PathTrack teleport) {
		QAngle angCur = GetLocalAngles();

		Vector3 nextPos = teleport.GetLocalOrigin();
		Vector3 look = nextPos;
		teleport.LookAhead(ref look, Length, 0);

		QAngle nextAngles;
		if (HasSpawnFlags(SF.FixedOrientation) || look == nextPos)
			nextAngles = GetLocalAngles();
		else {
			nextAngles = teleport.GetOrientation(IsDirForward());
			if (HasSpawnFlags(SF.NoPitch))
				nextAngles[PITCH] = angCur[PITCH];
		}

		Teleport(teleport.GetLocalOrigin(), nextAngles, null);
		SetLocalAngularVelocity(vec3_angle);

		Variant_t emptyVariant = default;
		teleport.AcceptInput("InTeleport", this, this, emptyVariant, 0);
	}

	public void Next() {
		if (Speed == 0) {
			DevMsg(2, $"TRAIN({GetDebugName()}): Speed is 0\n");
			SoundStopPlaying();
			return;
		}

		if (Path == null) {
			DevMsg(2, $"TRAIN({GetDebugName()}): Lost path\n");
			SoundStopPlaying();
			Speed = 0;
			return;
		}

		SoundUpdate();

		Vector3 nextPos = GetLocalOrigin();
		float speed = Speed;

		nextPos.Z -= Height;
		PathTrack? next = Path.LookAhead(ref nextPos, speed * 0.1f, 1, out PathTrack? nextNext);

		if (ManualSpeedChanges && ((speed < 0) != (DesiredSpeed < 0))) {
			if (next == null)
				next = Path;
		}

		if ((DebugOverlays & DebugOverlayBits.BBox) != 0) {
			if (next != null) {
				DebugOverlay.Line(GetAbsOrigin(), next.GetAbsOrigin(), 255, 0, 0, true, 0.1f);
				DebugOverlay.Line(next.GetAbsOrigin(), next.GetAbsOrigin() + new Vector3(0, 0, 32), 255, 0, 0, true, 0.1f);
				DebugOverlay.Box(next.GetAbsOrigin(), new Vector3(-8, -8, -8), new Vector3(8, 8, 8), 255, 0, 0, 0, 0.1f);
			}

			if (nextNext != null) {
				DebugOverlay.Line(GetAbsOrigin(), nextNext.GetAbsOrigin(), 0, 255, 0, true, 0.1f);
				DebugOverlay.Line(nextNext.GetAbsOrigin(), nextNext.GetAbsOrigin() + new Vector3(0, 0, 32), 0, 255, 0, true, 0.1f);
				DebugOverlay.Box(nextNext.GetAbsOrigin(), new Vector3(-8, -8, -8), new Vector3(8, 8, 8), 0, 255, 0, 0, 0.1f);
			}
		}

		nextPos.Z += Height;

		Assert(next == null || next.GetMoveParent() == GetMoveParent());

		if (next != null) {
			UpdateTrainVelocity(next, nextNext, nextPos, (float)gpGlobals.FrameTime);
			UpdateTrainOrientation(next, nextNext, nextPos, (float)gpGlobals.FrameTime);

			if (next != Path) {
				Path = next;
				ArriveAtNode(next);

				PathTrack? teleport = next.GetNext();
				if (teleport != null && teleport.HasSpawnFlags(PathTrack.SF.Teleport))
					TeleportToPathTrack(teleport);
			}

			OnNext.FireOutput(next, this);

			SetThink(Next);
			SetMoveDoneTime(0.5);
			SetNextThink(gpGlobals.CurTime);
			SetMoveDone(null);
		}
		else {
			SoundStopPlaying();
			SetLocalVelocity(nextPos - GetLocalOrigin());
			SetLocalAngularVelocity(vec3_angle);
			float distance = GetLocalVelocity().Length();
			OldSpeed = Speed;

			Speed = 0;

			if (distance > 0) {
				float time = distance / MathF.Abs(OldSpeed);
				SetLocalVelocity(GetLocalVelocity() * (OldSpeed / distance));
				SetMoveDone(DeadEnd);
				SetNextThink(TICK_NEVER_THINK);
				SetMoveDoneTime(time);
			}
			else
				DeadEnd();
		}
	}

	void FirePassInputs(PathTrack? start, PathTrack? end, bool forward) {
		PathTrack? current = start;

		if (!forward) {
			current = end;
			end = start;
		}
		Variant_t emptyVariant = default;

		while (current != null && current != end) {
			current.AcceptInput("InPass", this, this, emptyVariant, 0);
			current = forward ? current.GetNext() : current.GetPrevious();
		}
	}

	public void DeadEnd() {
		PathTrack? track, next;

		track = Path;

		DevMsg(2, $"TRAIN({GetDebugName()}): Dead end ");
		if (track != null) {
			if (OldSpeed < 0) {
				do {
					next = PathTrack.ValidPath(track.GetPrevious(), 1);
					if (next != null)
						track = next;
				} while (next != null);
			}
			else {
				do {
					next = PathTrack.ValidPath(track.GetNext(), 1);
					if (next != null)
						track = next;
				} while (next != null);
			}
		}

		SetLocalVelocity(vec3_origin);
		SetLocalAngularVelocity(vec3_angle);
		if (track != null) {
			DevMsg(2, $"at {track.GetDebugName()}\n");
			Variant_t emptyVariant = default;
			track.AcceptInput("InPass", this, this, emptyVariant, 0);
		}
		else
			DevMsg(2, "\n");
	}

	public void SetControls(BaseEntity controls) {
		Vector3 offset = controls.GetLocalOrigin();

		ControlMins = controls.WorldAlignMins() + offset;
		ControlMaxs = controls.WorldAlignMaxs() + offset;
	}

	public override bool OnControls(BaseEntity test) {
		Vector3 offset = test.GetLocalOrigin() - GetLocalOrigin();

		if (HasSpawnFlags(SF.NoControl))
			return false;

		Matrix4x4 tmp = default;
		tmp.SetupMatrixOrgAngles(vec3_origin, GetLocalAngles());
		Vector3 local = tmp.VMul3x3Transpose(offset);

		if (local.X >= ControlMins.X && local.Y >= ControlMins.Y && local.Z >= ControlMins.Z &&
			 local.X <= ControlMaxs.X && local.Y <= ControlMaxs.Y && local.Z <= ControlMaxs.Z)
			return true;

		return false;
	}

	public void Find() {
		Path = gEntList.FindEntityByName(null, Target) as PathTrack;
		if (Path == null)
			return;

		if (!FClassnameIs(Path, "path_track")
#if !PORTAL
			 && !FClassnameIs(Path, "env_portal_path_track")
#endif
			) {
			Warning("func_track_train must be on a path of path_track\n");
			Assert(false);
			Path = null;
			return;
		}

		Vector3 nextPos = Path.GetLocalOrigin();
		Vector3 look = nextPos;
		Path.LookAhead(ref look, Length, 0);
		nextPos.Z += Height;
		look.Z += Height;

		QAngle nextAngles;
		if (HasSpawnFlags(SF.FixedOrientation))
			nextAngles = GetLocalAngles();
		else {
			MathLib.VectorAngles(look - nextPos, out nextAngles);
			if (HasSpawnFlags(SF.NoPitch))
				nextAngles.X = 0;
		}

		Teleport(nextPos, nextAngles, null);

		ArriveAtNode(Path);

		if (Speed != 0) {
			SetNextThink(gpGlobals.CurTime + 0.1f);
			SetThink(Next);
			SoundUpdate();
		}
	}

	public void NearestPath() {
		BaseEntity? track;
		BaseEntity? nearest = null;
		float dist, closest;

		closest = 1024;

		for (EntitySphereQuery sphere = new(GetAbsOrigin(), 1024); (track = sphere.GetCurrentEntity()) != null; sphere.NextEntity()) {
			if ((track.GetFlags() & (EntityFlags.Client | EntityFlags.NPC)) == 0 && FClassnameIs(track, "path_track")) {
				dist = (GetAbsOrigin() - track.GetAbsOrigin()).Length();
				if (dist < closest) {
					closest = dist;
					nearest = track;
				}
			}
		}

		if (nearest == null) {
			Msg("Can't find a nearby track !!!\n");
			SetThink(null);
			return;
		}

		DevMsg(2, $"TRAIN: {GetDebugName()}, Nearest track is {nearest.GetDebugName()}\n");
		track = ((PathTrack)nearest).GetNext();
		if (track != null) {
			if ((GetLocalOrigin() - track.GetLocalOrigin()).Length() < (GetLocalOrigin() - nearest.GetLocalOrigin()).Length())
				nearest = track;
		}

		Path = (PathTrack)nearest;

		if (Speed != 0) {
			SetMoveDoneTime(0.1);
			SetMoveDone(Next);
		}
	}

	public override void OnRestore() {
		base.OnRestore();
		if (Path == null) {
			NearestPath();
			SetThink(null);
		}
	}

	public override EntityCapabilities ObjectCaps() => base.ObjectCaps() | EntityCapabilities.DirectionalUse | EntityCapabilities.UseOnGround;

	public float GetMaxSpeed() => MaxSpeed;
	public float GetCurrentSpeed() => Speed;
	public float GetDesiredSpeed() => DesiredSpeed;

	public void SetBlockDamage(float damage) => BlockDamage = damage;
	public void SetDamageChild(bool damageChild) => DamageChild = damageChild;

	public void SetTrack(PathTrack track) => Path = track.Nearest(GetLocalOrigin());

	public override void Spawn() {
		if (MaxSpeed == 0) {
			if (Speed == 0)
				MaxSpeed = 100;
			else
				MaxSpeed = Speed;
		}

		if (MoveSoundMinPitch == 0)
			MoveSoundMinPitch = 60;

		if (MoveSoundMaxPitch == 0)
			MoveSoundMaxPitch = 200;

		SetLocalVelocity(vec3_origin);
		SetLocalAngularVelocity(vec3_angle);

		Dir = 1;

		if (string.IsNullOrEmpty(Target))
			Msg($"FuncTrackTrain '{GetDebugName()}' has no target.\n");

		SetModel(GetModelName());
		SetMoveType(Source.MoveType.Push);

		SetSolid(HasSpawnFlags(SF.HL1Train) ? SolidType.BSP : SolidType.VPhysics);

		if (HasSpawnFlags(SF.UnblockableByPlayer))
			AddFlag(EntityFlags.UnblockableByPlayer);
		if (HasSpawnFlags(SF.Passable))
			AddSolidFlags(SolidFlags.NotSolid);

		ControlMins = CollisionProp().OBBMins();
		ControlMaxs = CollisionProp().OBBMaxs();
		ControlMaxs.Z += 72;

		SetThink(Find);
		SetNextThink(gpGlobals.CurTime);
		Precache();

		CreateVPhysics();
	}

	public bool CreateVPhysics() {
		VPhysicsInitShadow(false, false);
		return true;
	}

	public override void Precache() {
		if (Volume == 0.0)
			Volume = 1.0f;

		if (!string.IsNullOrEmpty(SoundMove))
			PrecacheScriptSound(SoundMove);

		if (!string.IsNullOrEmpty(SoundMovePing))
			PrecacheScriptSound(SoundMovePing);

		if (!string.IsNullOrEmpty(SoundStart))
			PrecacheScriptSound(SoundStart);

		if (!string.IsNullOrEmpty(SoundStop))
			PrecacheScriptSound(SoundStop);
	}

	public override void UpdateOnRemove() {
		SoundStopPlaying();
		base.UpdateOnRemove();
	}

	public override void MoveDone() {
		LastBlockPos = Vector3.Zero;
		LastBlockTick = -1;
		base.MoveDone();
	}

	public override int OnTakeDamage(in TakeDamageInfo info) {
		if (DamageChild) {
			FirstMoveChild()?.TakeDamage(info);

			return 0;
		}
		else
			return base.OnTakeDamage(info);
	}

	static void FixupAngles(ref QAngle v) {
		v.X = Fix(v.X);
		v.Y = Fix(v.Y);
		v.Z = Fix(v.Z);
	}

	static float Fix(float angle) {
		while (angle < 0)
			angle += 360;
		while (angle > 360)
			angle -= 360;

		return angle;
	}
}
