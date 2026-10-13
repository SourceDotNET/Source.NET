using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

using DEFINE = Source.DEFINE<PointViewControl>;

// Port of CTriggerCamera (game/server/triggers.cpp)
[LinkEntityToClass("point_viewcontrol")]
public class PointViewControl : BaseEntity
{
	[Flags]
	public enum SF
	{
		PlayerPosition = 1,
		PlayerTarget = 2,
		TakeControl = 4,
		InfiniteWait = 8,
		SnapTo = 16,
		NotSolid = 32,
		Interrupt = 64,
	}

	EHANDLE Player = new();
	EHANDLE TargetEnt = new();

	BaseEntity? Path;
	string? PathName;
	float Wait;
	TimeUnit_t ReturnTime;
	TimeUnit_t StopTime;
	float MoveDistance;
	float TargetSpeed;
	float InitialSpeed;
	float Acceleration;
	float Deceleration;
	int State;
	Vector3 MoveDir;

	string? TargetAttachment;
	int AttachmentIndex;
	bool SnapToGoal;

	InButtons PlayerButtons;
	byte OldTakeDamage;

	public OutputEvent OnEndFollow = new();

	public static readonly new DataMap DataDesc = new(typeof(PointViewControl), BaseEntity.DataDesc, [
		DEFINE.FIELD(nameof(Player), FieldType.EHandle),
		DEFINE.FIELD(nameof(TargetEnt), FieldType.EHandle),
		DEFINE.FIELD(nameof(PathName), FieldType.String),
		DEFINE.FIELD(nameof(Wait), FieldType.Float),
		DEFINE.FIELD(nameof(ReturnTime), FieldType.Time),
		DEFINE.FIELD(nameof(StopTime), FieldType.Time),
		DEFINE.FIELD(nameof(MoveDistance), FieldType.Float),
		DEFINE.FIELD(nameof(TargetSpeed), FieldType.Float),
		DEFINE.FIELD(nameof(InitialSpeed), FieldType.Float),
		DEFINE.FIELD(nameof(Acceleration), FieldType.Float),
		DEFINE.FIELD(nameof(Deceleration), FieldType.Float),
		DEFINE.FIELD(nameof(State), FieldType.Integer),
		DEFINE.FIELD(nameof(MoveDir), FieldType.Vector),
		DEFINE.KEYFIELD(nameof(TargetAttachment), FieldType.String, "targetattachment"),
		DEFINE.FIELD(nameof(AttachmentIndex), FieldType.Integer),
		DEFINE.FIELD(nameof(SnapToGoal), FieldType.Boolean),
		DEFINE.FIELD(nameof(PlayerButtons), FieldType.Integer),
		DEFINE.FIELD(nameof(OldTakeDamage), FieldType.Integer),

		DEFINE.INPUTFUNC(FieldType.Void, "Enable", nameof(InputEnable), (INPUTFUNCPTR)((self, data) => ((PointViewControl)self).InputEnable(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Disable", nameof(InputDisable), (INPUTFUNCPTR)((self, data) => ((PointViewControl)self).InputDisable(data))),

		DEFINE.OUTPUT(nameof(OnEndFollow), "OnEndFollow", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public override void Spawn() {
		base.Spawn();

		SetMoveType(Source.MoveType.Noclip);
		SetSolid(Source.SolidType.None);
		SetRenderColorA(0);
		RenderMode = (byte)Source.RenderMode.TransTexture;

		State = (int)UseType.Off;

		InitialSpeed = Speed;

		if (Acceleration == 0)
			Acceleration = 500;

		if (Deceleration == 0)
			Deceleration = 500;

		DispatchUpdateTransmitState();
	}

	public override EdictFlags UpdateTransmitState() {
		if (State == (int)UseType.On)
			return SetTransmitState(EdictFlags.Always);
		else
			return SetTransmitState(EdictFlags.DontSend);
	}

	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		if (FStrEq(keyName, "wait"))
			Wait = strtof(value, out _);
		else if (FStrEq(keyName, "moveto"))
			PathName = new(value);
		else if (FStrEq(keyName, "acceleration"))
			Acceleration = strtof(value, out _);
		else if (FStrEq(keyName, "deceleration"))
			Deceleration = strtof(value, out _);
		else
			return base.KeyValue(keyName, value);

		return true;
	}

	public void InputEnable(InputData inputdata) {
		Player.Set(inputdata.Activator);
		Enable();
	}

	public void InputDisable(InputData inputdata) => Disable();

	public void Enable() {
		State = (int)UseType.On;

		if (Player.Get() == null || !Player.Get()!.IsPlayer()) {
			if (gpGlobals.MaxClients == 1)
				Player.Set(Util.GetLocalPlayer());
		}

		if (Player.Get() == null) {
			DispatchUpdateTransmitState();
			return;
		}

		Assert(Player.Get()!.IsPlayer());
		BasePlayer? player = null;

		if (Player.Get()!.IsPlayer())
			player = (BasePlayer)Player.Get()!;
		else {
			Warning("PointViewControl could not find a player!\n");
			return;
		}

		{
			BaseEntity? prevViewControl = player.GetViewEntity();
			if (prevViewControl != null && prevViewControl != player) {
				if (prevViewControl is PointViewControl otherCamera) {
					if (otherCamera == this) {
						Warning($"Viewcontrol {GetDebugName()} was enabled twice in a row!\n");
						return;
					}
					else
						otherCamera.Disable();
				}
			}
		}

		PlayerButtons = player.Buttons;

		OldTakeDamage = Player.Get()!.m_takedamage;
		Player.Get()!.m_takedamage = (byte)Damage.No;

		if (HasSpawnFlags(SF.NotSolid))
			Player.Get()!.AddSolidFlags(SolidFlags.NotSolid);

		ReturnTime = gpGlobals.CurTime + Wait;
		Speed = InitialSpeed;
		TargetSpeed = InitialSpeed;

		if (HasSpawnFlags(SF.SnapTo))
			SnapToGoal = true;

		if (HasSpawnFlags(SF.PlayerTarget))
			TargetEnt.Set(Player.Get());
		else
			TargetEnt.Set(GetNextTarget());

		BaseEntity? target = TargetEnt.Get();
		if (target != null) {
			AttachmentIndex = 0;
			if (!string.IsNullOrEmpty(TargetAttachment)) {
				if (target.GetBaseAnimating() == null)
					Warning($"{GetClassname()} tried to target an attachment ({TargetAttachment}) on target {target.GetEntityName()}, which has no model.\n");
				else {
					AttachmentIndex = target.GetBaseAnimating()!.LookupAttachment(TargetAttachment);
					if (AttachmentIndex <= 0)
						Warning($"{GetClassname()} could not find attachment {TargetAttachment} on target {target.GetEntityName()}.\n");
				}
			}
		}

		if (HasSpawnFlags(SF.TakeControl))
			((BasePlayer)Player.Get()!).EnableControl(false);

		if (!string.IsNullOrEmpty(PathName))
			Path = gEntList.FindEntityByName(null, PathName, null, Player.Get());
		else
			Path = null;

		StopTime = gpGlobals.CurTime;
		if (Path != null) {
			if (Path.Speed != 0)
				TargetSpeed = Path.Speed;

			StopTime += Path is BaseToggle toggle ? toggle.GetDelay() : 0;
		}

		if (HasSpawnFlags(SF.PlayerPosition)) {
			Util.SetOrigin(this, Player.Get()!.EyePosition());
			SetLocalAngles(new QAngle(Player.Get()!.GetLocalAngles().X, Player.Get()!.GetLocalAngles().Y, 0));
			SetAbsVelocity(Player.Get()!.GetAbsVelocity());
		}
		else
			SetAbsVelocity(vec3_origin);

		player.SetViewEntity(this);

		player.GetActiveWeapon()?.AddEffects(EntityEffects.NoDraw);

		if (TargetEnt.Get() != null) {
			SetThink(FollowTarget);
			SetNextThink(gpGlobals.CurTime);
		}

		MoveDistance = 0;
		Move();

		DispatchUpdateTransmitState();
	}

	public void Disable() {
		if (Player.Get() != null && Player.Get()!.IsAlive()) {
			BasePlayer player = (BasePlayer)Player.Get()!;

			if (HasSpawnFlags(SF.NotSolid))
				player.RemoveSolidFlags(SolidFlags.NotSolid);

			player.SetViewEntity(player);
			player.EnableControl(true);

			player.GetActiveWeapon()?.RemoveEffects(EntityEffects.NoDraw);

			player.m_takedamage = OldTakeDamage;
		}

		State = (int)UseType.Off;
		ReturnTime = gpGlobals.CurTime;
		SetThink(null);

		OnEndFollow.FireOutput(this, this);
		SetLocalAngularVelocity(vec3_angle);

		DispatchUpdateTransmitState();
	}

	public override void Use(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		if (ShouldToggle(useType, State) == 0)
			return;

		if (State != (int)UseType.Off)
			Disable();
		else {
			Player.Set(activator);
			Enable();
		}
	}

	void FollowTarget() {
		if (Player.Get() == null)
			return;

		BaseEntity? target = TargetEnt.Get();
		if (target == null) {
			Disable();
			return;
		}

		if (!HasSpawnFlags(SF.InfiniteWait) && (target == null || ReturnTime < gpGlobals.CurTime)) {
			Disable();
			return;
		}

		QAngle vecGoal;
		if (AttachmentIndex != 0) {
			target.GetBaseAnimating()!.GetAttachment(AttachmentIndex, out Vector3 vecOrigin, out _);
			MathLib.VectorAngles(vecOrigin - GetAbsOrigin(), out vecGoal);
		}
		else {
			if (target != null)
				MathLib.VectorAngles(target.GetAbsOrigin() - GetAbsOrigin(), out vecGoal);
			else
				vecGoal = GetAbsAngles();
		}

		if (SnapToGoal) {
			SetAbsAngles(vecGoal);
			SnapToGoal = false;
		}
		else {
			QAngle angles = GetLocalAngles();

			if (angles.Y > 360)
				angles.Y -= 360;

			if (angles.Y < 0)
				angles.Y += 360;

			SetLocalAngles(angles);

			float dx = vecGoal.X - GetLocalAngles().X;
			float dy = vecGoal.Y - GetLocalAngles().Y;

			if (dx < -180)
				dx += 360;
			if (dx > 180)
				dx = dx - 360;

			if (dy < -180)
				dy += 360;
			if (dy > 180)
				dy = dy - 360;

			QAngle vecAngVel = new(dx * 40 * (float)gpGlobals.FrameTime, dy * 40 * (float)gpGlobals.FrameTime, GetLocalAngularVelocity().Z);
			SetLocalAngularVelocity(vecAngVel);
		}

		if (!HasSpawnFlags(SF.TakeControl)) {
			SetAbsVelocity(GetAbsVelocity() * 0.8f);
			if (GetAbsVelocity().Length() < 10.0f)
				SetAbsVelocity(vec3_origin);
		}

		SetNextThink(gpGlobals.CurTime);

		Move();
	}

	void Move() {
		if (HasSpawnFlags(SF.Interrupt)) {
			if (Player.Get() != null) {
				BasePlayer? player = ToBasePlayer(Player.Get());

				if (player != null) {
					InButtons buttonsChanged = PlayerButtons ^ player.Buttons;

					if (buttonsChanged != 0 && player.Buttons != 0) {
						Disable();
						return;
					}

					PlayerButtons = player.Buttons;
				}
			}
		}

		if (Path == null)
			return;

		{
			float frameTime = (float)gpGlobals.FrameTime;

			MoveDistance -= Speed * frameTime;

			if (MoveDistance <= 0) {
				Variant_t emptyVariant = default;
				Path.AcceptInput("InPass", this, this, emptyVariant, 0);
				Path = Path.GetNextTarget();

				if (Path == null)
					SetAbsVelocity(vec3_origin);
				else {
					if (Path.Speed != 0)
						TargetSpeed = Path.Speed;

					MoveDir = Path.GetLocalOrigin() - GetLocalOrigin();
					MoveDistance = MathLib.VectorNormalize(ref MoveDir);
					StopTime = gpGlobals.CurTime + (Path is BaseToggle toggle ? toggle.GetDelay() : 0);
				}
			}

			if (StopTime > gpGlobals.CurTime)
				Speed = MathLib.Approach(0, Speed, Deceleration * frameTime);
			else
				Speed = MathLib.Approach(TargetSpeed, Speed, Acceleration * frameTime);

			float fraction = 2 * frameTime;
			SetAbsVelocity(((MoveDir * Speed) * fraction) + (GetAbsVelocity() * (1 - fraction)));
		}
	}
}
