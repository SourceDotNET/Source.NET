global using static Game.Client.PredictionConvars;

using Game.Client.HL2;
using Game.Client.HUD;
using Game.Shared;
using Game.Shared.HL2;

using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Mathematics;
using Source.Common.Networking;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Client;

public static class PredictionConvars
{
	public static readonly ConVar cl_predictweapons = new("cl_predictweapons", "1", FCvar.UserInfo | FCvar.NotConnected, "Perform client side prediction of weapon effects.");
	public static readonly ConVar cl_lagcompensation = new("cl_lagcompensation", "1", FCvar.UserInfo | FCvar.NotConnected, "Perform server side lag compensation of weapon firing events.");
	public static readonly ConVar cl_showerror = new("cl_showerror", "0", 0, "Show prediction errors, 2 for above plus detailed field deltas.");
	public static readonly ConVar cl_idealpitchscale = new("cl_idealpitchscale", "0.8", FCvar.Archive);
	public static readonly ConVar cl_predictionlist = new("cl_predictionlist", "0", FCvar.Cheat, "Show which entities are predicting\n");
	public static readonly ConVar cl_predictionentitydump = new("cl_pdump", "-1", FCvar.Cheat, "Dump info about this entity to screen.");
	public static readonly ConVar cl_predictionentitydumpbyclass = new("cl_pclass", "", FCvar.Cheat, "Dump entity by prediction classname.");
	public static readonly ConVar cl_pred_optimize = new("cl_pred_optimize", "2", 0, "Optimize for not copying data if didn't receive a network update (1), and also for not repredicting if there were no errors (2).");
}

public class Prediction : IPrediction
{
	public Prediction() {
		bInPrediction = false;
		FirstTimePredicted = false;
		IncomingPacketNumber = 0;
		IdealPitch = 0.0f;
		PreviousStartFrame = -1;
		CommandsPredicted = 0;
		ServerCommandsAcknowledged = 0;
		PreviousAckHadErrors = false;
	}
	public const float ON_EPSILON = 0.1f;
	public const float MAX_FORWARD = 6f;
	public const float MIN_CORRECTION_DISTANCE = 0.25f;
	public const float MIN_PREDICTION_EPSILON = 0.5f;
	public const float MAX_PREDICTION_ERROR = 64.0f;


	bool bInPrediction;
	bool FirstTimePredicted;
	bool OldCLPredictValue;
	bool EnginePaused;

	int PreviousStartFrame;

	int CommandsPredicted;
	int ServerCommandsAcknowledged;
	bool PreviousAckHadErrors;
	int IncomingPacketNumber;

	float IdealPitch;

	public void GetLocalViewAngles(out QAngle ang) {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null)
			ang = default;
		else
			ang = player.pl.ViewingAngle;
	}

	public void GetViewAngles(out QAngle ang) {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null)
			ang = default;
		else
			ang = player.GetLocalAngles();
	}

	public void GetViewOrigin(out Vector3 org) {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null)
			org = default;
		else
			org = player.GetLocalOrigin();
	}

	public void Init() {
		OldCLPredictValue = cl_predict.GetInt() != 0;
	}

	static int pos = 0;
	public void CheckError(int commandsAcknowledged) {
		Vector3 delta;
		float len;
		if (!engine.IsInGame())
			return;

		if (cl_predict.GetInt() == 0)
			return;

		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null)
			return;

		if (!player.IsIntermediateDataAllocated())
			return;

		Vector3 origin = player.GetNetworkOrigin();
		byte[]? slot = player.GetPredictedFrame(commandsAcknowledged - 1);
		if (slot == null)
			return;

		// Find the origin field in the database
		TypeDescription? td = PredictionCopy.FindFieldByName(nameof(C_BaseEntity.NetworkOrigin), player.GetPredDescMap());
		Assert(td != null);
		if (td == null)
			return;

		Vector3 predicted_origin = slot.GetValueType<Vector3>((int)td.PackedOffset);

		// Compare what the server returned with what we had predicted it to be
		MathLib.VectorSubtract(predicted_origin, origin, out delta);

		len = MathLib.VectorLength(delta);
		if (len > MAX_PREDICTION_ERROR)
			len = 0;
		else {
			if (len > MIN_PREDICTION_EPSILON) {
				player.NotePredictionError(delta);

				if (cl_showerror.GetInt() >= 1) {
					Con_NPrint_s np = default;
					np.FixedWidthFont = true;
					np.Color[0] = 1.0f;
					np.Color[1] = 0.95f;
					np.Color[2] = 0.7f;
					np.Index = 20 + (++pos % 20);
					np.TimeToLive = 2.0f;

					engine.Con_NXPrintf(in np, $"pred error {len} units ({delta.X} {delta.Y} {delta.Z})");
				}
			}
		}
	}

	public void OnReceivedUncompressedPacket() {
		CommandsPredicted = 0;
		ServerCommandsAcknowledged = 0;
		PreviousStartFrame = -1;
	}

	public void PostEntityPacketReceived() {
		if (cl_predict.GetInt() == 0)
			return;

		C_BasePlayer? current = C_BasePlayer.GetLocalPlayer();
		// No local player object?
		if (current == null)
			return;

		// Transfer intermediate data from other predictables
		int c = predictables.GetPredictableCount();
		int i;
		for (i = 0; i < c; i++) {
			C_BaseEntity? ent = predictables.GetPredictable(i);
			if (ent == null)
				continue;

			if (!ent.GetPredictable())
				continue;

			ent.PostEntityPacketReceived();
		}
	}

	public bool ShouldDumpEntity(C_BaseEntity ent){
		int dump_entity = cl_predictionentitydump.GetInt();
		if (dump_entity != -1) {
			bool dump = false;
			if (ent.EntIndex() == -1) 
				dump = (dump_entity == ent.EntIndex()) ? true : false;
			else 
				dump = (ent.EntIndex() == dump_entity) ? true : false;

			if (!dump) 
				return false;
		}
		else {
			if (cl_predictionentitydumpbyclass.GetString().IsStringEmpty)
				return false;

			if (!C_BaseEntity.FClassnameIs(ent, cl_predictionentitydumpbyclass.GetString()))
				return false;
		}
		return true;
	}

	public void PostNetworkDataReceived(int commandsAcknowledged) {
		bool error_check = (commandsAcknowledged > 0) ? true : false;
		PDumpPanel dump = GetPDumpPanel();

		ServerCommandsAcknowledged += commandsAcknowledged;
		PreviousAckHadErrors = false;

		bool entityDumped = false;

		C_BasePlayer? current = C_BasePlayer.GetLocalPlayer();
		// No local player object?
		if (current == null)
			return;

		// Don't screw up memory of current player from history buffers if not filling in history buffers
		//  during prediction!!!
		if (cl_predict.GetInt() != 0) {
			int showlist = cl_predictionlist.GetInt();
			int totalsize = 0;
			int totalsize_intermediate = 0;

			Con_NPrint_s np = new();
			np.FixedWidthFont = true;
			np.Color[0] = 0.8f;
			np.Color[1] = 1.0f;
			np.Color[2] = 1.0f;
			np.TimeToLive = 2.0f;

			// Transfer intermediate data from other predictables
			int c = predictables.GetPredictableCount();
			int i;
			Span<char> szBuf = stackalloc char[32];
			for (i = 0; i < c; i++) {
				C_BaseEntity? ent = predictables.GetPredictable(i);
				if (ent == null)
					continue;

				if (ent.GetPredictable())
					if (ent.PostNetworkDataReceived(ServerCommandsAcknowledged))
						PreviousAckHadErrors = true;

				if (showlist != 0) {
					Span<char> sz = szBuf;
					if (ent.EntIndex() == -1)
						sz = sprintf(sz, "handle %u").U((uint)ent.GetClientHandle().Index);
					else
						sz = sprintf(sz, "%i").I(ent.EntIndex());

					np.Index = i;

					if (showlist >= 2) {
						// todo
					}
					else
						engine.Con_NXPrintf(np, $"{sz} {ent.GetClassname()}: {(ent.GetPredictable() ? "predicted" : "client created")}");
				}
				if (error_check && !entityDumped && dump != null && ShouldDumpEntity(ent)) {
					entityDumped = true;
					dump.DumpEntity(ent, ServerCommandsAcknowledged);
				}
			}

			if (showlist >= 2) {
				// todo
			}

			// Zero out rest of list
			if (showlist != 0) {
				while (i < 20) {
					engine.Con_NXPrintf(new(){ Index= i}, "");
					i++;
				}
			}

			if (error_check) 
				CheckError(ServerCommandsAcknowledged);
		}

		// Can also look at regular entities
		int dumpentindex = cl_predictionentitydump.GetInt();
		if (dump != null && error_check && !entityDumped && dumpentindex != -1) {
			int last_entity = cl_entitylist.GetHighestEntityIndex();
			if (dumpentindex >= 0 && dumpentindex <= last_entity) {
				C_BaseEntity? ent = cl_entitylist.GetBaseEntity(dumpentindex);
				if (ent != null) {
					dump.DumpEntity(ent, ServerCommandsAcknowledged);
					entityDumped = true;
				}
			}
		}

		if (cl_predict.GetBool() != OldCLPredictValue) {
			if (!OldCLPredictValue) 
				ReinitPredictables();
			
			CommandsPredicted = 0;
			ServerCommandsAcknowledged = 0;
			PreviousStartFrame = -1;
		}

		OldCLPredictValue = cl_predict.GetInt() != 0;

		if (dump != null && error_check && !entityDumped) {
			dump.Clear();
		}
	}

	public void ReinitPredictables() {
		int i;
		int c = cl_entitylist.GetHighestEntityIndex();
		for (i = 0; i <= c; i++) {
			C_BaseEntity? e = cl_entitylist.GetBaseEntity(i);
			if (e == null)
				continue;

			if (e.GetPredictable())
				continue;
			e.CheckInitPredictable("ReinitPredictables");
		}

		Msg($"Reinitialized {predictables.GetPredictableCount()} predictable entities\n");
	}

	public void PreEntityPacketReceived(int commandsAcknowledged, int currentWorldUpdatePacket) {
		Span<char> sz = stackalloc char[32];
		sprintf(sz, "preentitypacket%d").D(commandsAcknowledged);
		IncomingPacketNumber = currentWorldUpdatePacket;
#if GMOD_DLL
		garrysmod.Think();
#endif

		if (cl_predict.GetInt() == 0) {
			ShutdownPredictables();
			return;
		}

		C_BasePlayer? current = C_BasePlayer.GetLocalPlayer();
		if (current == null)
			return;

		int c = predictables.GetPredictableCount();
		int i;
		for (i = 0; i < c; i++) {
			C_BaseEntity? ent = predictables.GetPredictable(i);
			if (ent == null)
				continue;

			if (!ent.GetPredictable())
				continue;

			ent.PreEntityPacketReceived(commandsAcknowledged);
		}
	}

	public void StartCommand(C_BasePlayer player, AnonymousSafeFieldPointer<UserCmd> cmd) {
		PredictableId.ResetInstanceCounters();

		player.CurrentCommand = cmd;
		C_BaseEntity.SetPredictionRandomSeed(in cmd.Get());
		C_BaseEntity.SetPredictionPlayer(player);
	}

	public void RunCommand(C_BasePlayer player, AnonymousSafeFieldPointer<UserCmd> ucmdptr, IMoveHelper moveHelper) {
		StartCommand(player, ucmdptr);

		// Set globals appropriately
		gpGlobals.CurTime = player.TickBase * TICK_INTERVAL;
		gpGlobals.FrameTime = EnginePaused ? 0 : TICK_INTERVAL;

		g_pGameMovement.StartTrackPredictionErrors(player);

		// TODO
		// TODO:  Check for impulse predicted?

		ref UserCmd ucmd = ref ucmdptr.Get();

		// Do weapon selection
		if (ucmd.WeaponSelect != 0) {
			C_BaseCombatWeapon? weapon = (C_BaseCombatWeapon?)(BaseEntity.Instance(ucmd.WeaponSelect));
			if (weapon != null)
				player.SelectItem(weapon.GetName(), ucmd.WeaponSubtype);
		}

		// Latch in impulse.
		IClientVehicle? vehicle = player.GetVehicle();
		if (ucmd.Impulse != 0) {
			// Discard impulse commands unless the vehicle allows them.
			// FIXME: UsingStandardWeapons seems like a bad filter for this. 
			// The flashlight is an impulse command, for example.
			if (vehicle == null || player.UsingStandardWeaponsInVehicle()) {
				player.Impulse = ucmd.Impulse;
			}
		}

		// Get button states
		player.UpdateButtonState(ucmd.Buttons);

		// TODO
		//	CheckMovingGround( player, ucmd->frametime );

		// TODO
		//	g_pMoveData->m_vecOldAngles = player->pl.v_angle;

		// Copy from command to player unless game .dll has set angle using fixangle
		// if ( !player->pl.fixangle )
		{
			player.SetLocalViewAngles(ucmd.ViewAngles);
		}

		// Call standard client pre-think
		RunPreThink(player);

		// Call Think if one is set
		RunThink(player, TICK_INTERVAL);

		// Setup input.
		{

			SetupMove(player, ucmd, moveHelper, g_pMoveData);
		}

		// RUN MOVEMENT
		if (vehicle == null) {
			Assert(g_pGameMovement != null);
			g_pGameMovement.ProcessMovement(player, g_pMoveData);
		}
		else {
			vehicle.ProcessMovement(player, g_pMoveData);
		}

		FinishMove(player, ref ucmd, g_pMoveData);

		RunPostThink(player);

		g_pGameMovement.FinishTrackPredictionErrors(player);

		FinishCommand(player);

		if (gpGlobals.FrameTime > 0)
			player.TickBase++;
	}

	private void FinishCommand(BasePlayer player) {
		player.CurrentCommand = AnonymousSafeFieldPointer<UserCmd>.Null;
		C_BaseEntity.SetPredictionRandomSeed(in Unsafe.NullRef<UserCmd>());
		C_BaseEntity.SetPredictionPlayer(null);
	}

	private void RunPostThink(BasePlayer player) {
		player.PostThink();
	}

	private void FinishMove(BasePlayer player, ref UserCmd ucmd, MoveData move) {
		player.RefEHandle.Index = move.PlayerHandle.Index;
		player.Velocity = move.Velocity;
		player.NetworkOrigin = move.GetAbsOrigin();
		player.Local.OldButtons = (int)move.Buttons;

		// NOTE: Don't copy this.  the movement code modifies its local copy but is not expecting to be authoritative
		//player->m_flMaxspeed = move->m_flClientMaxSpeed;

		LastGround.Set(player.GetGroundEntity());

		player.SetLocalOrigin(move.GetAbsOrigin());

		IClientVehicle? vehicle = player.GetVehicle();
		if (vehicle != null)
			vehicle.FinishMove(player, ref ucmd, move);
	}

	protected EHANDLE LastGround = new();

	private void SetupMove(BasePlayer player, UserCmd ucmd, IMoveHelper helper, MoveData move) {
		move.FirstRunOfFunctions = IsFirstTimePredicted();

		move.PlayerHandle.Index = player.GetClientHandle()!.Index;
		move.Velocity = player.GetAbsVelocity();
		move.SetAbsOrigin(player.GetNetworkOrigin());
		move.OldAngles = move.Angles;
		move.OldButtons = (InButtons)player.Local.OldButtons;
		move.OldForwardMove = player.Local.OldForwardMove;
		move.ClientMaxSpeed = player.Maxspeed;

		move.Angles = ucmd.ViewAngles;
		move.ViewAngles = ucmd.ViewAngles;
		move.ImpulseCommand = ucmd.Impulse;
		move.Buttons = ucmd.Buttons;

		C_BaseEntity? moveParent = player.GetMoveParent();
		if (moveParent == null)
			move.AbsViewAngles = move.ViewAngles;
		else {
			Matrix3x4 viewToParent, viewToWorld;
			MathLib.AngleMatrix(move.ViewAngles, out viewToParent);
			MathLib.ConcatTransforms(moveParent.EntityToWorldTransform(), viewToParent, out viewToWorld);
			MathLib.MatrixAngles(viewToWorld, out move.AbsViewAngles);
		}

		// Ingore buttons for movement if at controls
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

		IClientVehicle? pVehicle = player.GetVehicle();
		if (pVehicle != null)
			pVehicle.SetupMove(player, ref ucmd, helper, move);

		// Copy constraint information
		if (player.ConstraintEntity.Get() != null)
			move.ConstraintCenter = player.ConstraintEntity.Get()!.GetAbsOrigin();
		else
			move.ConstraintCenter = player.ConstraintCenter;

		move.ConstraintRadius = player.ConstraintRadius;
		move.ConstraintWidth = player.ConstraintWidth;
		move.ConstraintSpeedFactor = player.ConstraintSpeedFactor;

#if HL2_DLL
		// Convert to HL2 data.
		C_BaseHLPlayer? hlPlayer = (C_BaseHLPlayer?)player;
		Assert(hlPlayer != null);

		HLMoveData? hlMove = (HLMoveData?)move;
		Assert(hlMove != null);

		hlMove.IsSprinting = hlPlayer.IsSprinting();
#endif
	}

	private void RunThink(BasePlayer player, TimeUnit_t frametime) {
		long thinktick = player.GetNextThinkTick();

		if (thinktick <= 0 || thinktick > player.TickBase)
			return;

		player.SetNextThink(TICK_NEVER_THINK);

		// Think
		player.Think();
	}

	private void RunPreThink(BasePlayer player) {
		if (!player.PhysicsRunThink())
			return;

		player.PreThink();
	}

	private void ShutdownPredictables() {
		// todo
	}

	public void SetLocalViewAngles(in QAngle ang) {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		player?.SetLocalViewAngles(ang);
	}

	public void SetViewAngles(in QAngle ang) {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null) return;

		player.SetViewAngles(ang);
		player.IV_Rotation.Reset();
	}

	public void SetViewOrigin(in Vector3 org) {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null) return;

		player.SetLocalOrigin(org);
		player.NetworkOrigin = org;
		player.IV_Origin.Reset();
	}

	public void Shutdown() {
		throw new NotImplementedException();
	}


	readonly GlobalVarsBase saveVars = new(true);
	public void Update(int startFrame, bool validFrame, int incomingAcknowledged, int outgoingCommand) {
		EnginePaused = engine.IsPaused();

		bool receivedNewWorldUpdate = true;
		if (PreviousStartFrame == startFrame && cl_pred_optimize.GetBool() && cl_predict.GetInt() != 0)
			receivedNewWorldUpdate = false;

		PreviousStartFrame = startFrame;

		double unpredictedCurTime = UnpredictedCurTime;
		gpGlobals.CopyInstantiatedReferenceTo(saveVars);
		UnpredictedCurTime = gpGlobals.CurTime;
		_Update(receivedNewWorldUpdate, validFrame, incomingAcknowledged, outgoingCommand);
		UnpredictedCurTime = unpredictedCurTime;
		saveVars.CopyInstantiatedReferenceTo(gpGlobals);
	}

	public static double UnpredictedCurTime;

	void _Update(bool receivedNewWorldUpdate, bool validFrame, int incomingAcknowledged, int outgoingCommand) {
		C_BasePlayer? localPlayer = C_BasePlayer.GetLocalPlayer();
		if (localPlayer == null)
			return;

		engine.GetViewAngles(out QAngle viewangles);
		localPlayer.SetLocalAngles(viewangles);

		if (!validFrame)
			return;

		if (cl_predict.GetInt() == 0) {
			localPlayer.SetLocalViewAngles(viewangles);
			return;
		}


		C_BaseAnimating.InvalidateBoneCaches();
		using (C_BaseAnimating.AutoAllowBoneAccess boneaccess = new(true, true)) {

			// Remove any purely client predicted entities that were left "dangling" because the 
			//  server didn't acknowledge them or which can now safely be removed
			RemoveStalePredictedEntities(incomingAcknowledged);

			// Restore objects back to "pristine" state from last network/world state update
			if (receivedNewWorldUpdate)
				RestoreOriginalEntityState();

			if (!PerformPrediction(receivedNewWorldUpdate, localPlayer, incomingAcknowledged, outgoingCommand))
				return;
		}


		// Overwrite predicted angles with the actual view angles
		localPlayer.SetLocalAngles(viewangles);

		// This allows us to sample the world when it may not be ready to be sampled
		Assert(C_BaseEntity.IsAbsQueriesValid());

		// FIXME: What about hierarchy here?!?
		SetIdealPitch(localPlayer, localPlayer.GetLocalOrigin(), localPlayer.GetLocalAngles(), localPlayer.ViewOffset);
	}

	private void RemoveStalePredictedEntities(int sequenceNumber) {
		int oldest_allowable_command = sequenceNumber;

		int c = predictables.GetPredictableCount();
		int i;
		for (i = c - 1; i >= 0; i--) {
			C_BaseEntity? ent = predictables.GetPredictable(i);
			if (ent == null)
				continue;

			// Don't do anything to truly predicted things (like player and weapons )
			if (ent.GetPredictable())
				continue;

			// What's left should be things like projectiles that are just waiting to be "linked"
			//  to their server counterpart and deleted
			Assert(ent.IsClientCreated());
			if (!ent.IsClientCreated())
				continue;

			// Snag the PredictionContext
			PredictionContext? ctx = ent.PredictionContext;
			if (ctx == null)
				continue;

			// If it was ack'd then the server sent us the entity.
			// Leave it unless it wasn't made dormant this frame, in
			//  which case it can be removed now
			if (ent.PredictableID.GetAcknowledged()) {
				// Hasn't become dormant yet!!!
				if (!ent.IsDormantPredictable()) {
					Assert(0);
					continue;
				}

				// Still gets to live till next frame
				if (ent.BecameDormantThisPacket())
					continue;

				C_BaseEntity? serverEntity = ctx.ServerEntity.Get();
				if (serverEntity != null) {
					// Notify that it's going to go away
					serverEntity.OnPredictedEntityRemove(true, ent);
				}
			}
			else {
				// Check context to see if it's too old?
				int command_entity_creation_happened = ctx.CreationCommandNumber;
				// Give it more time to live...not time to kill it yet
				if (command_entity_creation_happened > oldest_allowable_command)
					continue;

				// If the client predicted the KILLME flag it's possible
				//  that entity had such a short life that it actually
				//  never was sent to us.  In that case, just let it die a silent death
				if (!ent.IsEFlagSet(EFL.KillMe)) {
					if (cl_showerror.GetInt() != 0) {
						// It's bogus, server doesn't have a match, destroy it:
						Msg($"Removing unack'ed predicted entity: {ent.GetClassname()} created {ctx.CreationModule}({ctx.CreationLineNumber}) id == {ent.PredictableID.Describe()} : {ent}\n");
					}
				}

				// FIXME:  Do we need an OnPredictedEntityRemove call with an "it's not valid"
				// flag of some kind
			}

			// This will remove it from predictables list and will also free the entity, etc.
			ent.Release();
		}
	}

	private bool PerformPrediction(bool receivedNewWorldUpdate, BasePlayer localPlayer, int incomingAcknowledged, int outgoingCommand) {
		Assert(C_BaseEntity.IsAbsQueriesValid());
		Assert(C_BaseEntity.IsAbsRecomputationsEnabled());

		bInPrediction = true;

		// undo interpolation changes for entities we stand on
		C_BaseEntity? entity = localPlayer.GetGroundEntity();

		while (entity != null && entity.EntIndex() > 0) {
			entity.MoveToLastReceivedPosition();
			// undo changes for moveparents too
			entity = entity.GetMoveParent();
		}

		// Start at command after last one server has processed and 
		//  go until we get to targettime or we run out of new commands
		int i = ComputeFirstCommandToExecute(receivedNewWorldUpdate, incomingAcknowledged, outgoingCommand);

		Assert(i >= 1);
		while (true) {
			// Incoming_acknowledged is the last usercmd the server acknowledged having acted upon
			int current_command = incomingAcknowledged + i;

			// We've caught up to the current command.
			if (current_command > outgoingCommand)
				break;

			if (i >= MULTIPLAYER_BACKUP)
				break;

			ref UserCmd cmd = ref input.GetUserCmd(current_command);

			if (Unsafe.IsNullRef(ref cmd))
				break;

			// Is this the first time predicting this
			FirstTimePredicted = !cmd.HasBeenPredicted;

			// Set globals appropriately
			TimeUnit_t curtime = (localPlayer.TickBase) * TICK_INTERVAL;

			RunSimulation(current_command, curtime, ref cmd, localPlayer);

			gpGlobals.CurTime = curtime;
			gpGlobals.FrameTime = EnginePaused ? 0 : TICK_INTERVAL;

			// Call untouch on any entities no longer predicted to be touching
			Untouch();

			// Store intermediate data into appropriate slot
			StorePredictionResults(i - 1); // Note that I starts at 1

			CommandsPredicted = i;

			if (current_command == outgoingCommand) {
				localPlayer.FinalPredictedTick = localPlayer.TickBase;
			}
			// Mark that we issued any needed sounds, of not done already
			cmd.HasBeenPredicted = true;

			// Copy the state over.
			i++;
		}

		//	Msg( "%i : predicted %i commands forward, %i ack'd last frame, had errors %s\n", 
		//		gpGlobals->tickcount, 
		//		m_nCommandsPredicted, 
		//		m_nServerCommandsAcknowledged,
		//		m_bPreviousAckHadErrors ? "true" : "false" );

		bInPrediction = false;

		// Somehow we looped past the end of the list (severe lag), don't predict at all
		if (i > MULTIPLAYER_BACKUP)
			return false;

		return true;
	}

	private void RunSimulation(int current_command, double curtime, ref UserCmd cmd, C_BasePlayer localPlayer) {
		C_CommandContext ctx = localPlayer.GetCommandContext();
		Assert(ctx);

		ctx.NeedsProcessing = true;
		ctx.Cmd = cmd;
		ctx.CommandNumber = current_command;

		IPredictionSystem.SuppressEvents(!IsFirstTimePredicted());

		int i;

		// Make sure simulation occurs at most once per entity per usercmd
		for (i = 0; i < predictables.GetPredictableCount(); i++) {
			C_BaseEntity? entity = predictables.GetPredictable(i);
			entity?.SimulationTick = -1;
		}

		// Don't used cached numpredictables since entities can be created mid-prediction by the player
		for (i = 0; i < predictables.GetPredictableCount(); i++) {
			// Always reset
			gpGlobals.CurTime = curtime;
			gpGlobals.FrameTime = EnginePaused ? 0 : TICK_INTERVAL;

			C_BaseEntity? entity = predictables.GetPredictable(i);

			if (entity == null)
				continue;

			bool islocal = (localPlayer == entity) ? true : false;

			// Local player simulates first, if this assert fires then the predictables list isn't sorted 
			//  correctly (or we started predicting C_World???)
			if (islocal) {
				Assert(i == 0);
			}

			// Player can't be this so cull other entities here
			if ((entity.GetFlags() & EntityFlags.StaticProp) != 0)
				continue;

			// Player is not actually in the m_SimulatedByThisPlayer list, of course
			if (entity.IsPlayerSimulated())
				continue;

			if (HLClient.AddDataChangeEvent(entity, DataUpdateType.DataTableChanged, entity.DataChangeEventRef))
				entity.OnPreDataChanged(DataUpdateType.DataTableChanged);

			// Certain entities can be created locally and if so created, should be 
			//  simulated until a network update arrives
			if (entity.IsClientCreated()) {
				// Only simulate these on new usercmds
				if (!IsFirstTimePredicted())
					continue;

				entity.PhysicsSimulate();
			}
			else {
				entity.PhysicsSimulate();
			}

			// Don't update last networked data here!!!
			entity.OnLatchInterpolatedVariables(LatchFlags.LatchSimulationVar | LatchFlags.LatchAnimationVar | LatchFlags.InteroplateOmitUpdateLastNetworked);
		}

		// Always reset after running command
		IPredictionSystem.SuppressEvents(false);
	}

	private void InvalidateEFlagsRecursive(C_BaseEntity ent, EFL dirtyFlags, EFL childFlags = 0) {
		ent.AddEFlags(dirtyFlags);
		dirtyFlags |= childFlags;
		for (C_BaseEntity? child = ent.FirstMoveChild(); child != null; child = child.NextMovePeer())
			InvalidateEFlagsRecursive(child, dirtyFlags);
	}

	private void StorePredictionResults(int predicted_frame) {
		int i;
		int numpredictables = predictables.GetPredictableCount();

		// Now save off all of the results
		for (i = 0; i < numpredictables; i++) {
			C_BaseEntity? entity = predictables.GetPredictable(i);
			if (entity == null)
				continue;

			// Certain entities can be created locally and if so created, should be 
			//  simulated until a network update arrives
			if (!entity.GetPredictable())
				continue;

			// FIXME: The lack of this call inexplicably actually creates prediction errors
			InvalidateEFlagsRecursive(entity, EFL.DirtyAbsTransform | EFL.DirtyAbsVelocity | EFL.DirtyAbsAngVelocity);

			entity.SaveData("StorePredictionResults", predicted_frame, PredictionCopyType.Everything);
		}
	}

	private void Untouch() {
		int numpredictables = predictables.GetPredictableCount();

		// Loop through all entities again, checking their untouch if flagged to do so
		int i;
		for (i = 0; i < numpredictables; i++) {
			C_BaseEntity? entity = predictables.GetPredictable(i);
			if (entity == null)
				continue;

			if (!entity.GetCheckUntouch())
				continue;

			entity.PhysicsCheckForEntityUntouch();
		}
	}

	private int ComputeFirstCommandToExecute(bool receivedNewWorldUpdate, int incomingAcknowledged, int outgoingCommand) {
		int destination_slot = 1;
#if !NO_ENTITY_PREDICTION
		int skipahead = 0;

		// If we didn't receive a new update ( or we received an update that didn't ack any new CUserCmds -- 
		//  so for the player it should be just like receiving no update ), just jump right up to the very 
		//  last command we created for this very frame since we probably wouldn't have had any errors without 
		//  being notified by the server of such a case.
		// NOTE:  received_new_world_update only gets set to false if cl_pred_optimize >= 1
		if (!receivedNewWorldUpdate || 0 == ServerCommandsAcknowledged) {
			// this is where we would normally start
			int start = incomingAcknowledged + 1;
			// outgoing_command is where we really want to start
			skipahead = Math.Max(0, (outgoingCommand - start));
			// Don't start past the last predicted command, though, or we'll get prediction errors
			skipahead = Math.Min(skipahead, CommandsPredicted);

			// Always restore since otherwise we might start prediction using an "interpolated" value instead of a purely predicted value
			RestoreEntityToPredictedFrame(skipahead - 1);

			//Msg( "%i/%i no world, skip to %i restore from slot %i\n", 
			//	gpGlobals->framecount,
			//	gpGlobals->tickcount,
			//	skipahead,
			//	skipahead - 1 );
		}
		else {
			int nPredictedLimit = CommandsPredicted;
			// Otherwise, there is a second optimization, wherein if we did receive an update, but no
			//  values differed (or were outside their epsilon) and the server actually acknowledged running
			//  one or more commands, then we can revert the entity to the predicted state from last frame, 
			//  shift the # of commands worth of intermediate state off of front the intermediate state array, and
			//  only predict the usercmd from the latest render frame.
			if (cl_pred_optimize.GetInt() >= 2 &&
				!PreviousAckHadErrors &&
				CommandsPredicted > 0 &&
				ServerCommandsAcknowledged <= nPredictedLimit) {
				// Copy all of the previously predicted data back into entity so we can skip repredicting it
				// This is the final slot that we previously predicted
				RestoreEntityToPredictedFrame(CommandsPredicted - 1);

				// Shift intermediate state blocks down by # of commands ack'd
				ShiftIntermediateDataForward(ServerCommandsAcknowledged, CommandsPredicted);

				// Only predict new commands (note, this should be the same number that we could compute
				//  above based on outgoing_command - incoming_acknowledged - 1
				skipahead = (CommandsPredicted - ServerCommandsAcknowledged);

				//Msg( "%i/%i optimize2, skip to %i restore from slot %i\n", 
				//	gpGlobals->framecount,
				//	gpGlobals->tickcount,
				//	skipahead,
				//	m_nCommandsPredicted - 1 );
			}
			else {
				if (PreviousAckHadErrors) {
					C_BasePlayer? localPlayer = C_BasePlayer.GetLocalPlayer();

					// If an entity gets a prediction error, then we want to clear out its interpolated variables
					// so we don't mix different samples at the same timestamps. We subtract 1 tick interval here because
					// if we don't, we'll have 3 interpolation entries with the same timestamp as this predicted
					// frame, so we won't be able to interpolate (which leads to jerky movement in the player when
					// ANY entity like your gun gets a prediction error).
					TimeUnit_t prev = gpGlobals.CurTime;
					gpGlobals.CurTime = localPlayer!.GetTimeBase() - TICK_INTERVAL;

					for (int i = 0; i < predictables.GetPredictableCount(); i++) {
						C_BaseEntity? entity = predictables.GetPredictable(i);
						entity?.ResetLatched();
					}

					gpGlobals.CurTime = prev;
				}
			}
		}

		destination_slot += skipahead;

		// Always reset these values now that we handled them
		CommandsPredicted = 0;
		PreviousAckHadErrors = false;
		ServerCommandsAcknowledged = 0;
#endif
		return destination_slot;
	}

	private void ShiftIntermediateDataForward(int slots_to_remove, int number_of_commands_run) {
		C_BasePlayer? current = C_BasePlayer.GetLocalPlayer();
		// No local player object?
		if (current == null)
			return;

		// Don't screw up memory of current player from history buffers if not filling in history buffers
		//  during prediction!!!
		if (0 == cl_predict.GetInt())
			return;

		int c = predictables.GetPredictableCount();
		int i;
		for (i = 0; i < c; i++) {
			C_BaseEntity? ent = predictables.GetPredictable(i);
			if (ent == null)
				continue;

			if (!ent.GetPredictable())
				continue;

			ent.ShiftIntermediateDataForward(slots_to_remove, number_of_commands_run);
		}
	}
	private void RestoreEntityToPredictedFrame(int predicted_frame) {
		C_BasePlayer? current = C_BasePlayer.GetLocalPlayer();
		// No local player object?
		if (current == null)
			return;

		// Don't screw up memory of current player from history buffers if not filling in history buffers
		//  during prediction!!!
		if (0 == cl_predict.GetInt())
			return;

		int c = predictables.GetPredictableCount();
		int i;
		for (i = 0; i < c; i++) {
			C_BaseEntity? ent = predictables.GetPredictable(i);
			if (ent == null)
				continue;

			if (!ent.GetPredictable())
				continue;

			ent.RestoreData("RestoreEntityToPredictedFrame", predicted_frame, PredictionCopyType.Everything);
		}
	}

	private void RestoreOriginalEntityState() {
		Assert(C_BaseEntity.IsAbsRecomputationsEnabled());

		// Transfer intermediate data from other predictables
		int pc = predictables.GetPredictableCount();
		int p;
		for (p = 0; p < pc; p++) {
			C_BaseEntity? ent = predictables.GetPredictable(p);
			if (ent == null)
				continue;

			if (ent.GetPredictable()) {
				ent.RestoreData("RestoreOriginalEntityState", C_BaseEntity.SLOT_ORIGINALDATA, PredictionCopyType.Everything);
			}
		}
	}

	private void SetIdealPitch(C_BasePlayer localPlayer, Vector3 vector3, QAngle qAngle, Vector3 viewOffset) {
		// todo
	}

	public int GetIncomingPacketNumber() {
		return IncomingPacketNumber;
	}

	public bool IsFirstTimePredicted() {
		return FirstTimePredicted;
	}
	public bool InPrediction() {
		return bInPrediction;
	}
}
