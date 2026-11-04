using Game.Server;
using Game.Shared;
#if GMOD_DLL
using Game.Server.GarrysMod;
using Source.Common.GarrysMod.Lua;
#endif

using Source.Common.Commands;
using Source.Common.GUI;
using Source.Common.Mathematics;

using System.Numerics;

namespace Source.Engine;

public class PlayerMove
{
	static readonly ConVar sv_maxusrcmdprocessticks_warning = new("-1", FCvar.None, "Print a warning when user commands get dropped due to insufficient usrcmd ticks allocated, number of seconds to throttle, negative disabled");
	static readonly ConVar sv_maxusrcmdprocessticks_holdaim = new("1", FCvar.Cheat, "Hold client aim for multiple server sim ticks when client-issued usrcmd contains multiple actions (0: off; 1: hold this server tick; 2+: hold multiple ticks)");

	void StartCommand(BasePlayer player, AnonymousSafeFieldPointer<UserCmd> cmd) {
		PredictableId.ResetInstanceCounters();

		player.CurrentCommand = cmd;
		BaseEntity.SetPredictionRandomSeed(cmd.Get());
		BaseEntity.SetPredictionPlayer(player);

#if HL2_DLL
		// todo
#endif
	}

	void FinishCommand(BasePlayer player) {
		player.CurrentCommand = AnonymousSafeFieldPointer<UserCmd>.Null;
		BaseEntity.SetPredictionRandomSeed(default);
		BaseEntity.SetPredictionPlayer(null);
	}

	void CheckMovingGround(BasePlayer player, double frametime) {
		BaseEntity? groundEntity;

		if ((player.GetFlags() & EntityFlags.OnGround) != 0) {
			groundEntity = player.GetGroundEntity();
			if (groundEntity != null && (groundEntity.GetFlags() & EntityFlags.Conveyor) != 0) {
				// Vector3 newVelocity = groundEntity.GetGroundVelocityToApply(); TODO
				Vector3 newVelocity = vec3_origin;
				if ((player.GetFlags() & EntityFlags.BaseVelocity) != 0)
					newVelocity += player.GetBaseVelocity();

				player.SetBaseVelocity(newVelocity);
				player.AddFlag(EntityFlags.BaseVelocity);
			}
		}

		if ((player.GetFlags() & EntityFlags.BaseVelocity) == 0) {
			player.ApplyAbsVelocityImpulse((1.0f + (float)(frametime * 0.5)) * player.GetBaseVelocity());
			player.SetBaseVelocity(vec3_origin);
		}

		player.RemoveFlag(EntityFlags.BaseVelocity);
	}

	public virtual void SetupMove(BasePlayer player, ref UserCmd ucmd, IMoveHelper pHelper, MoveData move) {
		move.FirstRunOfFunctions = true;
		move.GameCodeMovedPlayer = false;

		if (player.GetPreviouslyPredictedOrigin() != player.GetAbsOrigin())
			move.GameCodeMovedPlayer = true;

		move.ImpulseCommand = ucmd.Impulse;
		move.ViewAngles = ucmd.ViewAngles;

		BaseEntity? moveParent = player.GetMoveParent();
		if (moveParent == null)
			move.AbsViewAngles = move.ViewAngles;
		else {
			Matrix3x4 viewToParent = default, viewToWorld = default;
			MathLib.ConcatTransforms(moveParent.EntityToWorldTransform(), viewToParent, out viewToWorld);
			MathLib.MatrixAngles(viewToWorld, out move.AbsViewAngles);
		}

		move.Buttons = ucmd.Buttons;

		if ((player.GetFlags() & EntityFlags.AtControls) != 0) {
			move.ForwardMove = 0;
			move.SideMove = 0;
			move.UpMove = 0;
		}
		else {
			move.ForwardMove = ucmd.ForwardMove;
			move.SideMove = ucmd.SideMove;
			move.UpMove = ucmd.UpMove;
		}

		move.ClientMaxSpeed = player.MaxSpeed();
		move.OldButtons = (InButtons)player.Local.OldButtons;
		move.OldForwardMove = player.Local.OldForwardMove;
		move.Angles = player.pl.ViewingAngle;

		move.Velocity = player.GetAbsVelocity();

		move.PlayerHandle.Set(player);

		move.SetAbsOrigin(player.GetAbsOrigin());

		if (player.ConstraintEntity.Get() != null)
			move.ConstraintCenter = player.ConstraintEntity.Get()!.GetAbsOrigin();
		else
			move.ConstraintCenter = player.ConstraintCenter;

		move.ConstraintRadius = player.ConstraintRadius;
		move.ConstraintWidth = player.ConstraintWidth;
		move.ConstraintSpeedFactor = player.ConstraintSpeedFactor;
	}

	public virtual void FinishMove(BasePlayer player, ref UserCmd ucmd, MoveData move) {
		player.SetAbsOrigin(move.GetAbsOrigin());
		player.SetAbsVelocity(move.Velocity);
		player.SetPreviouslyPredictedOrigin(move.GetAbsOrigin());

		player.Local.OldButtons = (int)move.Buttons;

		float pitch = move.Angles.X;
		if (pitch > 180.0f)
			pitch -= 360.0f;

		pitch = Math.Clamp(pitch, -90.0f, 90.0f);
		move.Angles.X = pitch;

		player.SetBodyPitch(pitch);
		player.SetLocalAngles(move.Angles);

		if (player.ConstraintEntity.Get() != null)
			Assert(move.ConstraintCenter == player.ConstraintEntity.Get()!.GetAbsOrigin());
		else
			Assert(move.ConstraintCenter == player.ConstraintCenter);

		Assert(move.ConstraintRadius == player.ConstraintRadius);
		Assert(move.ConstraintWidth == player.ConstraintWidth);
		Assert(move.ConstraintSpeedFactor == player.ConstraintSpeedFactor);
	}

	void RunPreThink(BasePlayer player) {
		if (!player.PhysicsRunThink())
			return;

		g_pGameRules.PlayerThink(player);

		player.PreThink();
	}

	void RunThink(BasePlayer player, double frametime) {
		int thinkTick = (int)player.GetNextThinkTick();

		if (thinkTick <= 0 || thinkTick > player.TickBase)
			return;

		player.SetNextThink(TICK_NEVER_THINK);

		player.Think();
	}

	void RunPostThink(BasePlayer player) => player.PostThink();

#if GMOD_DLL
	static void CallButtonHooks(BasePlayer player, ReadOnlySpan<byte> buttons, ReadOnlySpan<byte> others, LUA_POOLEDSTRING hook) {
		foreach (byte button in buttons) {
			if (button == 0)
				break;
			if (others.Contains(button) || gGM == null || !gGM.CallWithArgs((int)hook))
				continue;
			LuaEntity.Push_Entity(player);
			g_Lua!.PushNumber(button);
			gGM.CallNoReturns(2);
		}
	}
#endif

	static double lastWarningTime;
	public void RunCommand(BasePlayer player, AnonymousSafeFieldPointer<UserCmd> ucmd, IMoveHelper moveHelper) {
		ref UserCmd cmd = ref ucmd.Get();

		TimeUnit_t playerCurTime = player.TickBase * gpGlobals.IntervalPerTick;
		TimeUnit_t playerFrameTime = player.GamePaused ? 0 : gpGlobals.IntervalPerTick;
		TimeUnit_t timeAllowedForProcessing = player.ConsumeMovementTimeForUserCmdProcessing(playerFrameTime);

		if (!player.IsBot() && (timeAllowedForProcessing < playerFrameTime)) {
			double warningFrequencyThrottle = sv_maxusrcmdprocessticks_warning.GetFloat();
			if (warningFrequencyThrottle >= 0) {
				double timeNow = Platform.Time;
				if (lastWarningTime == 0 || (timeNow - lastWarningTime >= warningFrequencyThrottle)) {
					lastWarningTime = timeNow;
					Warning($"sv_maxusrcmdprocessticks_warning at server tick {gpGlobals.TickCount}: Ignored client {player.GetPlayerName()} usrcmd ({timeAllowedForProcessing:F6} < {playerFrameTime:F6})!\n");
				}
			}
			return;
		}

		StartCommand(player, ucmd);

		gpGlobals.CurTime = playerCurTime;
		gpGlobals.FrameTime = playerFrameTime;

		if (!cmd.ViewAngles.IsValid() || !BaseEntity.IsEntityQAngleReasonable(cmd.ViewAngles))
			cmd.ViewAngles = vec3_angle;

		cmd.Buttons |= player.AfButtonForced;
		cmd.Buttons &= ~player.AfButtonDisabled;

		if (player.GamePaused) {
			if (player.GetMoveType() == MoveType.Noclip && sv_cheats.GetBool() && sv_noclipduringpause.GetBool())
				gpGlobals.FrameTime = gpGlobals.IntervalPerTick;
		}

		g_pGameMovement.StartTrackPredictionErrors(player);

#if GMOD_DLL
		CallButtonHooks(player, cmd.ButtonsPressed, player.ButtonsPressed, LUA_POOLEDSTRING.PlayerButtonDown);
		CallButtonHooks(player, player.ButtonsPressed, cmd.ButtonsPressed, LUA_POOLEDSTRING.PlayerButtonUp);
		player.ButtonsPressed = cmd.ButtonsPressed;
#endif

		// CommentarySystem_PePlayerRunCommand( player, ucmd );

		if (cmd.WeaponSelect != 0) {
			if (BaseEntity.Instance(cmd.WeaponSelect) is BaseCombatWeapon weapon)
				player.SelectItem(weapon.GetName(), cmd.WeaponSubtype);
		}

		IServerVehicle? vehicle = player.GetVehicle();

		if (cmd.Impulse != 0) {
			if (vehicle == null || player.UsingStandardWeaponsInVehicle())
				player.Impulse = cmd.Impulse;
		}

		player.UpdateButtonState(cmd.Buttons);

		CheckMovingGround(player, gpGlobals.IntervalPerTick);

		g_MoveData.OldAngles = player.pl.ViewingAngle;

		if (player.pl.FixAngle == (int)FixAngle.None)
			player.pl.ViewingAngle = cmd.ViewAngles;
		else if (player.pl.FixAngle == (int)FixAngle.Relative)
			player.pl.ViewingAngle = cmd.ViewAngles + player.pl.AngleChange;

		RunPreThink(player);

		RunThink(player, gpGlobals.IntervalPerTick);

		SetupMove(player, ref cmd, moveHelper, g_MoveData);

		if (vehicle == null) {
			Assert(g_pGameMovement != null);
			g_pGameMovement.ProcessMovement(player, g_MoveData);
		}
		else
			vehicle.ProcessMovement(player, g_MoveData);

		FinishMove(player, ref cmd, g_MoveData);

		if (!player.IsBot() && (gpGlobals.TickCount - player.GetLockViewanglesTickNumber() < sv_maxusrcmdprocessticks_holdaim.GetInt()))
			player.pl.ViewingAngle = player.GetLockViewanglesData();

		moveHelper.ProcessImpacts();

		RunPostThink(player);

		g_pGameMovement.FinishTrackPredictionErrors(player);

		FinishCommand(player);

		if (gpGlobals.FrameTime > 0)
			player.TickBase++;
	}
}
