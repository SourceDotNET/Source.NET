using CommunityToolkit.HighPerformance;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.GarrysMod;
using Source.Common.Audio;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

using DEFINE = Source.DEFINE<BaseEntity>;
using FIELD = Source.FIELD<BaseEntity>;

#if HL2_DLL
public enum Class_T
{
	None = 0,
	Player,
	PlayerAlly,
	PlayerAllyVital,
	Antlion,
	Barnacle,
	Bullseye,
	//BULLSQUID,	
	CitizenPassive,
	CitizenRebel,
	Combine,
	CombineGunship,
	Conscript,
	Headcrab,
	//Houndeye,
	Manhack,
	MetroPolice,
	Military,
	Scanner,
	Stalker,
	Vortigaunt,
	Zombie,
	ProtoSniper,
	Missile,
	Flare,
	EarthFauna,
	HackedRollermine,
	CombineHunter,

	NumAIClasses
}

#elif HL1_DLL
#endif

public struct ResponseContext
{
	public string Name;
	public string Value;
	public TimeUnit_t ExpirationTime;
}

public struct ThinkFunc
{
	public BaseEntity.BASEPTR? Think;
	public string? Context;
	public long NextThinkTick;
	public long LastThinkTick;
}
public enum EntityEvent
{
	WaterTouch,
	WaterUntouch,
	ParentChanged
}
public enum ToggleState
{
	AtTop,
	AtBottom,
	GoingUp,
	GoingDown
}

[Flags]
public enum DebugOverlayBits
{
	Text = 0x00000001,
	Name = 0x00000002,
	BBox = 0x00000004,
	Pivot = 0x00000008,
	Message = 0x00000010,
	AbsBox = 0x00000020,
	RBox = 0x00000040,
	ShowBlocksLOS = 0x00000080,
	Attachments = 0x00000100,
	Autoaim = 0x00000200,
	NPCSelected = 0x00001000,
	NPCNearest = 0x00002000,
	NPCRoute = 0x00004000,
	NPCTriangulate = 0x00008000,
	NPCZap = 0x00010000,
	NPCEnemies = 0x00020000,
	NPCConditions = 0x00040000,
	NPCSquad = 0x00080000,
	NPCTask = 0x00100000,
	NPCFocus = 0x00200000,
	NPCViewcone = 0x00400000,
	NPCKill = 0x00800000,
	WCChangeEntity = 0x01000000,
	BuddhaMode = 0x02000000,
	NPCSteeringRegulations = 0x04000000,
	TaskText = 0x08000000,
	PropDebug = 0x10000000,
	NPCRelation = 0x20000000,
	ViewOffset = 0x40000000
}

public static class BaseEntity_ConCommands
{
	public static void ConsoleFireTargets(BasePlayer? player, ReadOnlySpan<char> name) {
		if (FStrEq(name, "")) {
			BaseEntity? entity = FindPickerEntity(player);
			if (entity != null && !entity.IsMarkedForDeletion()) {
				Msg($"[{gpGlobals.TickCount % 1000:D3}] Found: {entity.GetDebugName()}, firing\n");
				entity.Use(player, player, UseType.Toggle, 0);
				return;
			}
		}

		FireTargets(name, player, player, UseType.Toggle, 0);
	}

	[ConCommand("ent_name", null, FCvar.Cheat)]
	public static void CC_Ent_Name(in TokenizedCommand args) => SetDebugBits(Util.GetCommandClient(), args[1], DebugOverlayBits.Name);
	[ConCommand("ent_text", "Displays text debugging information about the given entity(ies) on top of the entity (See Overlay Text)\n\tArguments:   	{entity_name} / {class_name} / no argument picks what player is looking at ", FCvar.Cheat)]
	public static void CC_Ent_Text(in TokenizedCommand args) => SetDebugBits(Util.GetCommandClient(), args[1], DebugOverlayBits.Text);
	[ConCommand("ent_bbox", "Displays the movement bounding box for the given entity(ies) in orange.  Some entites will also display entity specific overlays.\n\tArguments:   	{entity_name} / {class_name} / no argument picks what player is looking at ", FCvar.Cheat)]
	public static void CC_Ent_BBox(in TokenizedCommand args) => SetDebugBits(Util.GetCommandClient(), args[1], DebugOverlayBits.BBox);
	[ConCommand("ent_absbox", "Displays the total bounding box for the given entity(s) in green.  Some entites will also display entity specific overlays.\n\tArguments:   	{entity_name} / {class_name} / no argument picks what player is looking at ", FCvar.Cheat)]
	public static void CC_Ent_AbsBox(in TokenizedCommand args) => SetDebugBits(Util.GetCommandClient(), args[1], DebugOverlayBits.AbsBox);
	[ConCommand("ent_rbox", "Displays the total bounding box for the given entity(s) in green.  Some entites will also display entity specific overlays.\n\tArguments:   	{entity_name} / {class_name} / no argument picks what player is looking at ", FCvar.Cheat)]
	public static void CC_Ent_RBox(in TokenizedCommand args) => SetDebugBits(Util.GetCommandClient(), args[1], DebugOverlayBits.RBox);
	[ConCommand("ent_attachments", "Displays the attachment points on an entity.\n\tArguments:   	{entity_name} / {class_name} / no argument picks what player is looking at ", FCvar.Cheat)]
	public static void CC_Ent_AttachmentPoints(in TokenizedCommand args) => SetDebugBits(Util.GetCommandClient(), args[1], DebugOverlayBits.Attachments);
	[ConCommand("ent_viewoffset", "Displays the eye position for the given entity(ies) in red.\n\tArguments:   	{entity_name} / {class_name} / no argument picks what player is looking at ", FCvar.Cheat)]
	public static void CC_Ent_ViewOffset(in TokenizedCommand args) => SetDebugBits(Util.GetCommandClient(), args[1], DebugOverlayBits.ViewOffset);
	[ConCommand("ent_remove", "Removes the given entity(s)\n\tArguments:   	{entity_name} / {class_name} / no argument picks what player is looking at ", FCvar.Cheat)]
	public static void CC_Ent_Remove(in TokenizedCommand args) {
		BaseEntity? entity = null;

		if (FStrEq(args[1], ""))
			entity = FindPickerEntity(Util.GetCommandClient());
		else {
			int index = atoi(args[1]);
			if (index != 0)
				entity = BaseEntity.Instance(index);
			else {
				BaseEntity? ent = null;
				while ((ent = gEntList.NextEnt(ent)) != null)
					if ((ent.GetEntityName() != null && FStrEq(args[1], ent.GetEntityName())) || (ent.Classname != null && FStrEq(args[1], ent.Classname)) || (!ent.GetClassname().IsEmpty && FStrEq(args[1], ent.GetClassname()))) {
						entity = ent;
						break;
					}
			}
		}

		if (entity != null) {
			Msg($"Removed {entity.Classname}({entity.GetDebugName()})\n");
			Util.Remove(entity);
		}
	}
	[ConCommand("ent_remove_all", "Removes all entities of the specified type\n\tArguments:   	{entity_name} / {class_name} ", FCvar.Cheat)]
	public static void CC_Ent_RemoveAll(in TokenizedCommand args) {
		if (args.ArgC() < 2)
			Msg("Removes all entities of the specified type\n\tArguments:   	{entity_name} / {class_name}\n");
		else {
			int count = 0;
			BaseEntity? ent = null;
			while ((ent = gEntList.NextEnt(ent)) != null)
				if ((ent.GetEntityName() != null && FStrEq(args[1], ent.GetEntityName())) || (ent.Classname != null && FStrEq(args[1], ent.Classname)) || (!ent.GetClassname().IsEmpty && FStrEq(args[1], ent.GetClassname()))) {
					Util.Remove(ent);
					count++;
				}

			if (count != 0)
				Msg($"Removed {count} {args[1]}'s\n");
			else
				Msg($"No {args[1]} found.\n");
		}
	}
	[ConCommand("ent_setname", "Sets the targetname of the given entity(s)\n\tArguments:   	{new entity name} {entity_name} / {class_name} / no argument picks what player is looking at ", FCvar.Cheat)]
	public static void CC_Ent_SetName(in TokenizedCommand args) {
		BaseEntity? entity = null;

		if (args.ArgC() < 2) {
			BasePlayer? player = ToBasePlayer(Util.GetCommandClient());
			if (player == null)
				return;

			Util.ClientPrint(player, HudPrint.Console, "Usage:\n   ent_setname <new name> <entity name>\n");
		}
		else {
			if (FStrEq(args[2], ""))
				entity = FindPickerEntity(Util.GetCommandClient());
			else {
				BaseEntity? ent = null;
				while ((ent = gEntList.NextEnt(ent)) != null)
					if ((ent.GetEntityName() != null && FStrEq(args[2], ent.GetEntityName())) || (ent.Classname != null && FStrEq(args[2], ent.Classname)) || (!ent.GetClassname().IsEmpty && FStrEq(args[2], ent.GetClassname()))) {
						entity = ent;
						break;
					}

				if (entity == null) {
					Warning($"No such entity with name {args[2]} found to set new name for.\n");
					return;
				}
			}

			if (entity != null) {
				Msg($"Set the name of {entity.Classname} to {args[1]}\n");
				entity.SetName(args[1]);
			}
		}
	}
	[ConCommand("find_ent", "Find and list all entities with classnames or targetnames that contain the specified substring.\nFormat: find_ent <substring>\n", FCvar.Cheat)]
	public static void CC_Find_Ent(in TokenizedCommand args) {
		if (args.ArgC() < 2) {
			Msg($"Total entities: {gEntList.NumberOfEntities()} ({gEntList.NumberOfEdicts()} edicts)\n");
			Msg("Format: find_ent <substring>\n");
			return;
		}

		int count = 0;
		ReadOnlySpan<char> subString = args[1];
		Msg($"Searching for entities with class/target name containing substring: '{subString}'\n");

		BaseEntity? ent = null;
		while ((ent = gEntList.NextEnt(ent)) != null) {
			ReadOnlySpan<char> classname = ent.GetClassname();
			ReadOnlySpan<char> targetname = ent.GetEntityName();

			bool matches = false;
			if (!classname.IsEmpty)
				if (!stristr(classname, subString).IsEmpty)
					matches = true;

			if (!matches && !targetname.IsEmpty)
				if (!stristr(targetname, subString).IsEmpty)
					matches = true;

			if (matches) {
				count++;
				Msg($"   '{ent.GetClassname()}' : '{ent.GetEntityName()}' (entindex {ent.EntIndex()}) \n");
			}
		}

		Msg($"Found {count} matches.\n");
	}
	[ConCommand("find_ent_index", "Display data for entity matching specified index.\nFormat: find_ent_index <index>\n", FCvar.Cheat)]
	public static void CC_Find_Ent_Index(in TokenizedCommand args) {
		if (args.ArgC() < 2) {
			Msg("Format: find_ent_index <index>\n");
			return;
		}

		int index = atoi(args[1]);
		BaseEntity? ent = Util.EntityByIndex(index);
		if (ent != null)
			Msg($"   '{ent.GetClassname()}' : '{ent.GetEntityName()}' (entindex {index}) \n");
		else
			Msg($"Found no entity at {index}.\n");
	}
	[ConCommand("ent_dump", "Usage:\n   ent_dump <entity name>\n", FCvar.Cheat)]
	public static void CC_Ent_Dump(in TokenizedCommand args) {
		BasePlayer? player = ToBasePlayer(Util.GetCommandClient());
		if (player == null)
			return;

		if (args.ArgC() < 2)
			Util.ClientPrint(player, HudPrint.Console, "Usage:\n   ent_dump <entity name>\n");
		else {
			BaseEntity? ent = null;
			bool found = false;
			Span<char> buf = stackalloc char[256];
			while ((ent = gEntList.FindEntityByName(ent, args[1])) != null) {
				found = true;
				for (DataMap? dmap = ent.GetDataDescMap(); dmap != null; dmap = dmap.BaseMap)
					for (int i = 0; i < dmap.DataNumFields; i++) {
						Variant_t var = new();
						if (!ent.ReadKeyField(dmap.DataDesc[i].ExternalName, ref var))
							continue;

						buf[0] = '\0';
						switch (var.FieldType()) {
							case FieldType.String: strcpy(buf, var.String()); break;
							case FieldType.Integer: if (var.Int() != 0 && var.Int().TryFormat(buf, out int intWritten)) buf[intWritten] = '\0'; break;
							case FieldType.Float: if (var.Float() != 0 && var.Float().TryFormat(buf, out int floatWritten, "F2")) buf[floatWritten] = '\0'; break;
							case FieldType.EHandle: if (var.Entity().Get<BaseEntity>() != null) strcpy(buf, var.Entity().Get<BaseEntity>()!.GetEntityName()); break;
						}

						if (FStrEq("parentname", dmap.DataDesc[i].ExternalName) || FStrEq("targetname", dmap.DataDesc[i].ExternalName))
							continue;

						if (buf[0] != '\0')
							Util.ClientPrint(player, HudPrint.Console, $"  {dmap.DataDesc[i].ExternalName}: {buf.SliceNullTerminatedString()}\n");
					}
			}

			if (!found)
				Util.ClientPrint(player, HudPrint.Console, "ent_dump: no such entity");
		}
	}
	[ConCommand("firetarget", null, FCvar.Cheat)]
	public static void CC_Ent_FireTarget(in TokenizedCommand args) => ConsoleFireTargets(Util.GetCommandClient(), args[1]);
	[ConCommand("ent_cancelpendingentfires", "Cancels all ent_fire created outputs that are currently waiting for their delay to expire.")]
	public static void CC_Ent_CancelPendingEntFires(in TokenizedCommand args) {
		if (!Util.IsCommandIssuedByServerAdmin())
			return;

		BasePlayer? player = ToBasePlayer(Util.GetCommandClient());
		if (player == null)
			return;

		g_EventQueue.CancelEvents(player);
	}
	[ConCommand("ent_fire", "Usage:\n   ent_fire <target> [action] [value] [delay]\n", FCvar.Cheat)]
	public static void EntFireAutoComplete(in TokenizedCommand args) {
		BasePlayer? player = ToBasePlayer(Util.GetCommandClient());
		if (player == null)
			return;

		if (args.ArgC() < 2) {
			Util.ClientPrint(player, HudPrint.Console, "Usage:\n   ent_fire <target> [action] [value] [delay]\n");
		}
		else {
			ReadOnlySpan<char> target, action = "Use";
			Variant_t value = new();
			int delay = 0;

			target = args[1];

			if (engine.IsDedicatedServer()) {
				// if (player.IsAutoKickDisabled() == false)
				// 	return;
			}
			else if (gpGlobals.MaxClients > 1) {
				BasePlayer? hostPlayer = Util.GetListenServerHost();
				if (player != hostPlayer)
					return;
			}

			if (args.ArgC() >= 3)
				action = args[2];

			if (args.ArgC() >= 4)
				value.SetString(args[3]);

			if (args.ArgC() >= 5)
				delay = atoi(args[4]);

			g_EventQueue.AddEvent(target, action, value, delay, player, player);
		}
	}
	[ConCommand("ent_info", "Usage:\n   ent_info <class name>\n", FCvar.Cheat)]
	public static void CC_Ent_Info(in TokenizedCommand args) {
		BasePlayer? player = ToBasePlayer(Util.GetCommandClient());
		if (player == null)
			return;

		if (args.ArgC() < 2)
			Util.ClientPrint(player, HudPrint.Console, "Usage:\n   ent_info <class name>\n");
		else {
			BaseEntity? ent = CreateEntityByName(args[1]);

			if (ent != null) {
				DataMap? dmap;
				for (dmap = ent.GetDataDescMap(); dmap != null; dmap = dmap.BaseMap)
					for (int i = 0; i < dmap.DataNumFields; i++)
						if ((dmap.DataDesc[i].Flags & FieldTypeDescFlags.Output) != 0)
							Util.ClientPrint(player, HudPrint.Console, $"  output: {dmap.DataDesc[i].ExternalName}\n");

				for (dmap = ent.GetDataDescMap(); dmap != null; dmap = dmap.BaseMap)
					for (int i = 0; i < dmap.DataNumFields; i++)
						if ((dmap.DataDesc[i].Flags & FieldTypeDescFlags.Input) != 0)
							Util.ClientPrint(player, HudPrint.Console, $"  input: {dmap.DataDesc[i].ExternalName}\n");

				Util.Remove(ent);
			}
			else
				Util.ClientPrint(player, HudPrint.Console, $"no such entity {args[1]}\n");
		}
	}
	[ConCommand("ent_messages", "Toggles input/output message display for the selected entity(ies).  The name of the entity will be displayed as well as any messages that it sends or receives.\n\tArguments:   	{entity_name} / {class_name} / no argument picks what player is looking at", FCvar.Cheat)]
	public static void CC_Ent_Messages(in TokenizedCommand args) => SetDebugBits(Util.GetCommandClient(), args[1], DebugOverlayBits.Message);
	[ConCommand("ent_pause", "Toggles pausing of input/output message processing for entities.  When turned on processing of all message will stop.  Any messages displayed with 'ent_messages' will stop fading and be displayed indefinitely. To step through the messages one by one use 'ent_step'.", FCvar.Cheat)]
	public static void CC_Ent_Pause(in TokenizedCommand args) {
		if (BaseEntity.Debug_IsPaused()) {
			Msg("Resuming entity I/O events\n");
			BaseEntity.Debug_Pause(false);
		}
		else {
			Msg("Pausing entity I/O events\n");
			BaseEntity.Debug_Pause(true);
		}
	}
	[ConCommand("picker", "Toggles 'picker' mode.  When picker is on, the bounding box, pivot and debugging text is displayed for whatever entity the player is looking at.\n\tArguments:	full - enables all debug information", FCvar.Cheat)]
	public static void CC_Ent_Picker(in TokenizedCommand args) {
		BaseEntity.InDebugSelect = BaseEntity.InDebugSelect ? false : true;

		BaseEntity.DebugPlayer = Util.GetCommandClientIndex();
	}
	[ConCommand("ent_pivot", "Displays the pivot for the given entity(ies).\n\t(y=up=green, z=forward=blue, x=left=red). \n\tArguments:   	{entity_name} / {class_name} / no argument picks what player is looking at ", FCvar.Cheat)]
	public static void CC_Ent_Pivot(in TokenizedCommand args) => SetDebugBits(Util.GetCommandClient(), args[1], DebugOverlayBits.Pivot);
	[ConCommand("ent_step", "When 'ent_pause' is set this will step through one waiting input / output message at a time.", FCvar.Cheat)]
	public static void CC_Ent_Step(in TokenizedCommand args) {
		int steps = atoi(args[1]);
		if (steps <= 0)
			steps = 1;

		BaseEntity.Debug_SetSteps(steps);
	}
	[ConCommand("ent_show_response_criteria", "Print, to the console, an entity's current criteria set used to select responses.\n\tArguments:   	{entity_name} / {class_name} / no argument picks what player is looking at ", FCvar.Cheat)]
	public static void CC_Ent_Show_Response_Criteria(in TokenizedCommand args) {
		BaseEntity? entity = null;
		while ((entity = GetNextCommandEntity(Util.GetCommandClient(), args[1], entity)) != null) {
			// entity.DumpResponseCriteria();
		}
	}
	[ConCommand("ent_autoaim", "Displays the entity's autoaim radius.\n\tArguments:   	{entity_name} / {class_name} / no argument picks what player is looking at", FCvar.Cheat)]
	public static void CC_Ent_Autoaim(in TokenizedCommand args) => SetDebugBits(Util.GetCommandClient(), args[1], DebugOverlayBits.Autoaim);
	[ConCommand("ent_create", "Creates an entity of the given type where the player is looking.  Additional parameters can be passed in in the form: ent_create <entity name> <param 1 name> <param 1> <param 2 name> <param 2>...<param N name> <param N>", FCvar.GameDLL | FCvar.Cheat)]
	public static void CC_Ent_Create(in TokenizedCommand args) {
		BasePlayer? player = Util.GetCommandClient();
		if (player == null)
			return;

		if (FStrEq(args[1], "point_servercommand")) {
			if (engine.IsDedicatedServer()) {
				// if (player.IsAutoKickDisabled() == false)
				// 	return;
			}
			else if (gpGlobals.MaxClients > 1) {
				BasePlayer? hostPlayer = Util.GetListenServerHost();
				if (player != hostPlayer)
					return;
			}
		}

		bool allowPrecache = BaseEntity.IsPrecacheAllowed();
		BaseEntity.SetAllowPrecache(true);

		BaseEntity? entity = CreateEntityByName(args[1]);
		if (entity != null) {
			entity.Precache();

			for (int i = 2; i + 1 < args.ArgC(); i += 2) {
				ReadOnlySpan<char> keyName = args[i];
				ReadOnlySpan<char> value = args[i + 1];
				entity.KeyValue(keyName, value);
			}

			Util.DispatchSpawn(entity);

			player.EyeVectors(out Vector3 forward);
			Util.TraceLine(player.EyePosition(), player.EyePosition() + forward * MAX_TRACE_LENGTH, Mask.Solid, player, CollisionGroup.None, out Trace tr);
			if (tr.Fraction != 1.0) {
				tr.EndPos.Z += 12;
				entity.Teleport(tr.EndPos, null, null);
				Util.DropToFloor(entity, Mask.Solid);
			}

			entity.Activate();
		}
		BaseEntity.SetAllowPrecache(allowPrecache);
	}
	public static bool CC_GetCommandEnt(in TokenizedCommand args, out BaseEntity? ent, ref Vector3 targetPoint, ref QAngle playerAngle, bool wantTargetPoint, bool wantPlayerAngle) {
		ent = null;
		int entIndex = atoi(args[1]);
		if (entIndex != 0)
			ent = BaseEntity.Instance(entIndex);
		else {
			ent = gEntList.FindEntityByName(null, args[1]);
			ent ??= gEntList.FindEntityByClassname(null, args[1]);
		}

		if (ent == null) {
			Msg($"Couldn't find any entity named '{args[1]}'\n");
			return false;
		}

		BasePlayer? player = Util.GetCommandClient();
		if (player == null) {
			Msg("Command must originate from a player\n");
			return false;
		}

		if (wantTargetPoint) {
			player.EyeVectors(out Vector3 forward);
			Util.TraceLine(player.EyePosition(), player.EyePosition() + forward * MAX_TRACE_LENGTH, Mask.NPCSolid, player, CollisionGroup.None, out Trace tr);

			if (tr.Fraction != 1.0)
				targetPoint = tr.EndPos;
		}

		if (wantPlayerAngle)
			playerAngle = player.EyeAngles();

		return true;
	}
	[ConCommand("ent_teleport", "Teleport the specified entity to where the player is looking.\n\tFormat: ent_teleport <entity name>", FCvar.Cheat)]
	public static void CC_Ent_Teleport(in TokenizedCommand args) {
		if (args.ArgC() < 2) {
			Msg("Format: ent_teleport <entity name>\n");
			return;
		}

		Vector3 targetPoint = default;
		QAngle unused = default;
		if (CC_GetCommandEnt(args, out BaseEntity? ent, ref targetPoint, ref unused, true, false)) {
			// ent!.Teleport(targetPoint, null, null);
		}
	}
	[ConCommand("ent_orient", "Orient the specified entity to match the player's angles. By default, only orients target entity's YAW. Use the 'allangles' option to orient on all axis.\n\tFormat: ent_orient <entity name> <optional: allangles>", FCvar.Cheat)]
	public static void CC_Ent_Orient(in TokenizedCommand args) {
		if (args.ArgC() < 2) {
			Msg("Format: ent_orient <entity name> <optional: allangles>\n");
			return;
		}

		Vector3 unused = default;
		QAngle playerAngles = default;
		if (CC_GetCommandEnt(args, out BaseEntity? ent, ref unused, ref playerAngles, false, true)) {
			QAngle entAngles = ent!.GetAbsAngles();
			if (args.ArgC() == 3 && strncmp(args[2], "allangles", 9) == 0)
				entAngles = playerAngles;
			else
				entAngles[YAW] = playerAngles[YAW];

			ent.SetAbsAngles(entAngles);
		}
	}

}

[LinkEntityToClass("func_proprrespawnzone")]
[NetworkName("CBaseEntity")]
public partial class BaseEntity : IServerEntity
{
	public static Edict? g_pForceAttachEdict;

	public delegate void BASEPTR(BaseEntity self);
	public delegate void INPUTFUNCPTR(BaseEntity self, InputData data);
	public delegate void ENTITYFUNCPTR(BaseEntity self, BaseEntity? other);
	public delegate void TOUCHPTR(BaseEntity? other);
	public delegate void USEPTR(BaseEntity? activator, BaseEntity? caller, UseType useType, float value);
	public delegate void BLOCKPTR(BaseEntity? other);

	public virtual int RequiredEdictIndex() => -1;

	public BASEPTR? FnThink;
	public TOUCHPTR? FnTouch;
	public USEPTR? FnUse;
	public BLOCKPTR? FnBlocked;

	/// <summary>
	/// Classify - returns the type of group (i.e, "houndeye", or "human military" so that NPCs with different classnames
	/// still realize that they are teammates. (overridden for NPCs that form groups)
	/// </summary>
	/// <returns></returns>
	public virtual Class_T Classify() => Class_T.None;

	static int PredictionRandomSeed = -1;
	static BasePlayer? PredictionPlayer;

	public static bool DisableTouchFuncs = false;
	public static bool AccurateTriggerBboxChecks = true;

	public const int TEAMNUM_NUM_BITS = 15; // < gmod increased 6 . 15
	public virtual bool IsPlayer() => false;
	public virtual bool IsBaseCombatCharacter() => false;
	public virtual bool IsNPC() => false;
	public AI_BaseNPC? MyNPCPointer() {
		if (IsNPC())
			return (AI_BaseNPC)this;

		return null;
	}
	public bool IsTransparent() => RenderMode != (byte)Source.RenderMode.Normal;
	public virtual bool IsNextBot() => false;
	public virtual bool IsBaseCombatWeapon() => false;
	public virtual bool IsCombatItem() => false;
	public virtual bool IsNetClient() => false;
	static bool GoodMatch(char cName, char cQuery) {
		if (cName == cQuery)
			return true;

		if ((uint)(cName - 'A') <= 'Z' - 'A' && cName - 'A' + 'a' == cQuery)
			return true;
		else if ((uint)(cName - 'a') <= 'z' - 'a' && cName - 'a' + 'A' == cQuery)
			return true;

		return false;
	}

	static bool NamesMatch(ReadOnlySpan<char> query, ReadOnlySpan<char> nameToMatch, bool nullName) {
		if (nullName)
			return query.IsEmpty || query[0] == '*';

		int n = 0, q = 0;
		while (n < nameToMatch.Length && q < query.Length) {
			if (!GoodMatch(nameToMatch[n], query[q]))
				break;

			++n;
			++q;
		}

		if (q == query.Length && n == nameToMatch.Length)
			return true;

		if (q < query.Length && query[q] == '*')
			return true;

		return false;
	}

	public bool NameMatchesComplex(ReadOnlySpan<char> nameOrWildcard) {
		if (stricmp("!player", nameOrWildcard) == 0)
			return IsPlayer();

		return NamesMatch(nameOrWildcard, Name, Name == null);
	}

	public bool ClassMatchesComplex(ReadOnlySpan<char> classOrWildcard) => NamesMatch(classOrWildcard, Classname, Classname == null);
	public bool ClassMatches(ReadOnlySpan<char> classOrWildcard) => ClassMatchesComplex(classOrWildcard);
	public bool NameMatches(ReadOnlySpan<char> nameOrWildcard) => NameMatchesComplex(nameOrWildcard);
	public virtual bool IsPredicted() => false;
	public virtual bool IsTemplate() => false;
	public bool IsDormant() => IsEFlagSet(EFL.Dormant);

	public ReadOnlySpan<char> TeamID() {
		var team = GetTeam();
		return team == null ? "" : team.GetName();
	}

	private static void SendProxy_AnimTime(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		BaseEntity entity = (BaseEntity)instance;

#if false
		BaseAnimating? animating = entity.GetBaseAnimating();
		Assert(animating != null);
		if (animating != null)
			Assert(!animating.IsUsingClientSideAnimation());
#endif

		int tickNumber = TIME_TO_TICKS(entity.AnimTime);
		long tickBase = gpGlobals.GetNetworkBase(gpGlobals.TickCount, entity.EntIndex());

		int addt = 0;
		if (tickNumber >= tickBase)
			addt = (int)((tickNumber - tickBase) & 0xff);

		outData.Int = addt;
	}

	internal static void SendProxy_SimulationTime(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		BaseEntity entity = (BaseEntity)instance;

		int tickNumber = TIME_TO_TICKS(entity.SimulationTime);
		long tickBase = gpGlobals.GetNetworkBase(gpGlobals.TickCount, entity.EntIndex());

		int addt = 0;
		if (tickNumber >= tickBase)
			addt = (int)((tickNumber - tickBase) & 0xff);

		outData.Int = addt;
	}

	public static SendTable DT_AnimTimeMustBeFirst = new(nameof(DT_AnimTimeMustBeFirst), [
		SendPropInt (FIELD.OF(nameof(AnimTime)), 8, PropFlags.Unsigned|PropFlags.ChangesOften|PropFlags.EncodedAgainstTickCount, proxyFn: SendProxy_AnimTime),
	]);

	public virtual int Save(ISave save) { throw new NotImplementedException(); }
	public virtual int Restore(IRestore save) { throw new NotImplementedException(); }
	public virtual bool ShouldSavePhysics() => true;
	public virtual void OnSave(IEntitySaveUtils utils) {
		// CalcAbsolutePosition();
		// CalcAbsoluteVelocity();
	}
	public virtual void OnRestore() {

	}

	public static object? SendProxy_ClientSideAnimation(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID) {
		BaseEntity entity = (BaseEntity)instance;
		BaseAnimating? animating = entity.GetBaseAnimating();

		if (animating != null && !animating.IsUsingClientSideAnimation())
			return instance;
		else
			return null;
	}
	public static SendTable DT_PredictableId = new(nameof(DT_PredictableId), [
		SendPropPredictableId(FIELD.OF(nameof(PredictableId))),
		SendPropInt(FIELD.OF(nameof(b_IsPlayerSimulated)), 1, PropFlags.Unsigned)
	]);

	public static SendTable DT_BaseEntity = new([
		SendPropDataTable("AnimTimeMustBeFirst", DT_AnimTimeMustBeFirst, SendProxy_ClientSideAnimation),

		SendPropInt(FIELD.OF(nameof(SimulationTime)), SIMULATION_TIME_WINDOW_BITS, PropFlags.Unsigned | PropFlags.ChangesOften | PropFlags.EncodedAgainstTickCount, proxyFn: SendProxy_SimulationTime /* todo */),
		SendPropVector(NetworkVarFields.Origin, -1, PropFlags.Coord | PropFlags.ChangesOften, 0, Constants.HIGH_DEFAULT, SendProxy_Origin),
		SendPropInt(FIELD.OF(nameof(InterpolationFrame)), NOINTERP_PARITY_MAX_BITS, PropFlags.Unsigned),
		SendPropModelIndex(FIELD.OF(nameof(ModelIndex))),
		SendPropDataTable("m_Collision", FIELD.OF(nameof(Collision)), CollisionProperty.DT_CollisionProperty),
		SendPropInt(FIELD.OF(nameof(RenderFX)), 8, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(RenderMode)), 8, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(Effects)), (int)EntityEffects.MaxBits, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(ColorRender)), 32, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(TeamNum)), TEAMNUM_NUM_BITS, 0),
		SendPropInt(FIELD.OF(nameof(CollisionGroup)), 5, PropFlags.Unsigned),
		SendPropFloat(FIELD.OF(nameof(Elasticity)), 0, PropFlags.Coord | PropFlags.NoScale),
		SendPropFloat(NetworkVarFields.ShadowCastDistance, 12, PropFlags.Unsigned),
		SendPropEHandle(FIELD.OF(nameof(OwnerEntity))),
		SendPropEHandle(FIELD.OF(nameof(EffectEntity))),
		SendPropEHandle(FIELD.OF(nameof(MoveParent))),
		SendPropInt(FIELD.OF(nameof(ParentAttachment)), NUM_PARENTATTACHMENT_BITS, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(MoveType)), (int)Source.MoveType.MaxBits, PropFlags.Unsigned ),
		SendPropInt(FIELD.OF(nameof(MoveCollide)), (int)Source.MoveCollide.MaxBits, PropFlags.Unsigned ),
		SendPropQAngles (NetworkVarFields.Rotation, 24, PropFlags.ChangesOften | PropFlags.RoundDown, SendProxy_Angles ),
		SendPropInt( FIELD.OF(nameof( TextureFrameIndex) ),     8, PropFlags.Unsigned ),
		SendPropDataTable( "predictable_id", DT_PredictableId, SendProxy_SendPredictableId ),
		SendPropInt(FIELD.OF(nameof(SimulatedEveryTick)),       1, PropFlags.Unsigned ),
		SendPropInt(FIELD.OF(nameof(AnimatedEveryTick)),        1, PropFlags.Unsigned ),
		SendPropBool( NetworkVarFields.AlternateSorting ),

		// The rest of this is Garry's Mod specific in order
		SendPropInt(FIELD.OF(nameof(m_takedamage)), 8),
		SendPropInt(FIELD.OF(nameof(RealClassName)), 16, PropFlags.Unsigned),

		SendPropInt(FIELD.OF(nameof(OverrideMaterial)), 16, PropFlags.Unsigned, SendProxy_OverrideMaterial),

		SendPropInt(FIELD.OF_SENDINFO_ARRAY(nameof(OverrideSubMaterials)), 16, PropFlags.Unsigned),
		SendPropArray2(null, 32, "m_OverrideSubMaterials"),

		SendPropInt(FIELD.OF(nameof(Health)), 32, PropFlags.Normal | PropFlags.ChangesOften | PropFlags.VarInt),
		SendPropInt(NetworkVarFields.MaxHealth, 32),
		SendPropInt(FIELD.OF(nameof(SpawnFlags)), 32, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(GModFlags)), 7),
		SendPropBool(FIELD.OF(nameof(OnFire))),
		SendPropFloat(FIELD.OF(nameof(CreationTime)), 0, PropFlags.NoScale),

		SendPropFloat(FIELD.OF_VECTORELEM(nameof(Velocity), 0), 0, PropFlags.NoScale | PropFlags.ChangesOften),
		SendPropFloat(FIELD.OF_VECTORELEM(nameof(Velocity), 1), 0, PropFlags.NoScale | PropFlags.ChangesOften),
		SendPropFloat(FIELD.OF_VECTORELEM(nameof(Velocity), 2), 0, PropFlags.NoScale | PropFlags.ChangesOften),

		SendPropGModTable(FIELD.OF(nameof(GMOD_DataTable))),

		// Addon exposed data tables
		SendPropArray3(FIELD.OF_ARRAY(nameof(GMOD_bool)), SendPropBool(FIELD.OF_ARRAYINDEX(nameof(GMOD_bool), 0))),
		SendPropArray3(FIELD.OF_ARRAY(nameof(GMOD_float)), SendPropFloat(FIELD.OF_ARRAYINDEX(nameof(GMOD_float), 0))),
		SendPropArray3(FIELD.OF_ARRAY(nameof(GMOD_int)), SendPropInt(FIELD.OF_ARRAYINDEX(nameof(GMOD_int), 0))),
		SendPropArray3(FIELD.OF_ARRAY(nameof(GMOD_Vector)),SendPropVector(FIELD.OF_ARRAYINDEX(nameof(GMOD_Vector), 0))),
		SendPropArray3(FIELD.OF_ARRAY(nameof(GMOD_QAngle)), SendPropQAngles(FIELD.OF_ARRAYINDEX(nameof(GMOD_QAngle), 0), 32, PropFlags.RoundDown)),
		SendPropArray3(FIELD.OF_ARRAY(nameof(GMOD_EHANDLE)), SendPropEHandle(FIELD.OF_ARRAYINDEX(nameof(GMOD_EHANDLE), 0))),
		SendPropString(FIELD.OF(nameof(GMOD_String0))),
		SendPropString(FIELD.OF(nameof(GMOD_String1))),
		SendPropString(FIELD.OF(nameof(GMOD_String2))),
		SendPropString(FIELD.OF(nameof(GMOD_String3))),

		// Creation IDs
		SendPropInt(FIELD.OF(nameof(CreationID)), 24),
		SendPropInt(FIELD.OF(nameof(MapCreatedID)), 16),
	]);

	public BaseEntity() : this(false) { }

	public BaseEntity(bool serverOnly) {
		CollisionGroup = (int)Source.CollisionGroup.None;

		CollisionProp().Init(this);
		NetworkProp().Init(this);

		AddEFlags(EFL.NoThinkFunction | EFL.NoGamePhysicsSimulation | EFL.UsePartitionWhenNotSolid);

		Elasticity = 1.0f;

		SetRenderColor(255, 255, 255, 255);

		// TeamNum = InitialTeamNum =Constants.TEAM_UNASSIGNED;
		LastThinkTick = (int)gpGlobals.TickCount;
		SimulationTick = -1;

		// SetIdentityMatrix(m_rgflCoordinateFrame);

		SetSolid(SolidType.None);
		ClearSolidFlags();

		SetMoveType(Source.MoveType.None);
		SetOwnerEntity(null);
		SetCheckUntouch(false);
		SetModelIndex(0);
		SetModelName(null);

		SetCollisionBounds(vec3_origin, vec3_origin);
		ClearFlags();

		// SetFriction(1.0f);

		if (serverOnly)
			AddEFlags(EFL.ServerOnly);

		NetworkProp().MarkPVSInformationDirty();

		AddEFlags(EFL.UsePartitionWhenNotSolid);
#if GMOD_DLL
		GMOD_DataTable = engine.GMOD_CreateDataTable();
#endif
	}

	public virtual void StopLoopingSounds() { }

	public void Remove() => Util.Remove(this);

	public void MakeDormant() {
		AddEFlags(EFL.Dormant);
		SetThink(null);

		if (Edict() == null)
			return;

		eflags |= EFL.Dormant;
		AddSolidFlags(SolidFlags.NotSolid);
		SetMoveType(Source.MoveType.None);
		AddEffects(EntityEffects.NoDraw);
		SetNextThink(TICK_NEVER_THINK);
	}

	public bool IsBSPModel() {
		if (GetSolid() == SolidType.BSP)
			return true;

		Model? model = modelinfo.GetModel(GetModelIndex());
		if (GetSolid() == SolidType.VPhysics && modelinfo.GetModelType(model) == ModelType.Brush)
			return true;

		return false;
	}

	public bool IsStandable() {
		if ((GetSolidFlags() & SolidFlags.NotStandable) != 0)
			return false;

		if (GetSolid() == SolidType.BSP || GetSolid() == SolidType.VPhysics || GetSolid() == SolidType.BBox)
			return true;

		return IsBSPModel();
	}

	public virtual bool CanStandOn(BaseEntity? surface) => (surface != null && !surface.IsStandable()) ? false : true;

	float GroundChangeTime;
	public void SetGroundChangeTime(float time) => GroundChangeTime = time;
	public float GetGroundChangeTime() => GroundChangeTime;

	public bool IsEdictFree() => Edict()!.IsFree();

	public virtual DamageType GetDamageType() => DamageType.Generic;

	public virtual Mask PhysicsSolidMaskForEntity() => Mask.Solid;

	public BaseEntity? Link;

	public virtual bool CanBeSeenBy(AI_BaseNPC npc) => true;

	public bool IsViewable() {
		if (IsEffectActive(EntityEffects.NoDraw))
			return false;

		if (IsBSPModel()) {
			if (GetMoveType() != Source.MoveType.None)
				return true;
		}
		else if (GetModelIndex() != 0)
			return true;
		return false;
	}

	public virtual void UpdateOnRemove() {
		Util.g_bReceivedChainedUpdateOnRemove = true;

		// Virtual call to shut down any looping sounds.
		StopLoopingSounds();

		// Notifies entity listeners, etc
		gEntList.NotifyRemoveEntity(GetRefEHandle());

		if (Edict() != null) {
			AddFlag(EntityFlags.KillMe);
			if ((GetFlags() & EntityFlags.Graphed) != 0) {
				/*	<<TODO>>
				// this entity was a LinkEnt in the world node graph, so we must remove it from
				// the graph since we are removing it from the world.
				for ( int i = 0 ; i < WorldGraph.m_cLinks ; i++ )
				{
					if ( WorldGraph.m_pLinkPool [ i ].m_pLinkEnt == pev )
					{
						// if this link has a link ent which is the same ent that is removing itself, remove it!
						WorldGraph.m_pLinkPool [ i ].m_pLinkEnt = NULL;
					}
				}
				*/
			}
		}

		if (GlobalName != null) {
			// NOTE: During level shutdown the global list will suppress this
			// it assumes your changing levels or the game will end
			// causing the whole list to be flushed
			GlobalEntity.SetState(GlobalName, GlobalEState.Dead);
		}

		VPhysicsDestroyObject();

		// This is only here to allow the MOVETYPE_NONE to be set without the
		// assertion triggering. Why do we bother setting the MOVETYPE to none here?
		RemoveEffects(EntityEffects.BoneMerge);
		SetMoveType(Source.MoveType.None);

		// If we have a parent, unlink from it.
		UnlinkFromParent(this);

		// Any children still connected are orphans, mark all for delete
		List<BaseEntity> childrenList = [];
		GetAllChildren(this, childrenList);
		if (childrenList.Count != 0) {
			DevMsg(2, $"Warning: Deleting orphaned children of {GetClassname()}\n");
			for (int i = childrenList.Count() - 1; i >= 0; --i)
				Util.Remove(childrenList[i]);
		}

		SetGroundEntity(null);

		// if (DynamicModelPending) 
		// 	sg_DynamicLoadHandlers.Remove(this);


		if (IVModelInfo.IsDynamicModelIndex(ModelIndex)) {
			modelinfo.ReleaseDynamicModel(ModelIndex); // no-op if not dynamic
			ModelIndex = -1;
		}
	}

	public string GetDebugName() {
		if (this == null)
			return "<<null>>";

		return Name ?? Classname ?? "<<no name>>";
	}

	EHANDLE Parent;
	public float Gravity;
	public void SetPredictionEligible(bool canpredict) { } // nothing in game code
	public ref readonly Vector3 GetLocalOrigin() => ref __nv_Origin;
	public ref readonly QAngle GetLocalAngles() => ref __nv_Rotation;
	private static void SendProxy_OverrideMaterial(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		BaseEntity entity = (BaseEntity)instance;
		outData.Int = entity.OverrideMaterial;
	}
	private static void SendProxy_Angles(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		BaseEntity entity = (BaseEntity)instance;
		Assert(entity != null);


		ref readonly QAngle angles = ref entity.UseStepSimulationNetworkAngles(out bool ok);
		if (!ok)
			angles = ref entity.GetLocalAngles();

		outData.Vector[0] = MathLib.AngleMod(angles.X);
		outData.Vector[1] = MathLib.AngleMod(angles.Y);
		outData.Vector[2] = MathLib.AngleMod(angles.Z);
	}

	private ref QAngle UseStepSimulationNetworkAngles(out bool ok) {
		if (Physics.g_bTestMoveTypeStepSimulation && GetMoveType() == Source.MoveType.Step && HasDataObjectType(DataObjectType.StepSimulation)) {
			ref StepSimulationData step = ref GetDataObject<StepSimulationData>(DataObjectType.StepSimulation);
			ComputeStepSimulationNetwork(ref step);
			
			ok = step.AnglesActive;
			return ref step.NetworkAngles;
		}

		ok = false;
		return ref Unsafe.NullRef<QAngle>();
	}

	internal static void SendProxy_Origin(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
		BaseEntity entity = (BaseEntity)instance;
		Assert(entity != null);

		ref readonly Vector3 vector3 = ref entity.UseStepSimulationNetworkOrigin(out bool ok);
		if (!ok)
			vector3 = ref entity.GetLocalOrigin();

		outData.Vector[0] = vector3.X;
		outData.Vector[1] = vector3.Y;
		outData.Vector[2] = vector3.Z;
	}

	private ref Vector3 UseStepSimulationNetworkOrigin(out bool ok) {
		if (Physics.g_bTestMoveTypeStepSimulation && GetMoveType() == Source.MoveType.Step && HasDataObjectType(DataObjectType.StepSimulation)) {
			ref StepSimulationData step = ref GetDataObject<StepSimulationData>(DataObjectType.StepSimulation);
			ComputeStepSimulationNetwork(ref step);
			ok = step.OriginActive;
			return ref step.NetworkOrigin;
		}

		ok = false;
		return ref Unsafe.NullRef<Vector3>();
	}

	private void ComputeStepSimulationNetwork(ref StepSimulationData step) {
		if (Unsafe.IsNullRef(ref step)) {
			AssertMsg(false, "ComputeStepSimulationNetworkOriginAndAngles with NULL step\n");
			return;
		}

		// Don't run again if we've already calculated this tick
		if (step.LastProcessTickCount == gpGlobals.TickCount) {
			return;
		}

		step.LastProcessTickCount = (int)gpGlobals.TickCount;

		// Origin
		// It's inactive
		if (step.OriginActive) {
			// First see if any external code moved the entity
			if (GetStepOrigin() != step.Next.Origin) {
				step.OriginActive = false;
			}
			else {
				// Compute interpolated info based on tick interval
				float frac = 1.0f;
				long tickdelta = step.Next.TickCount - step.Previous.TickCount;
				if (tickdelta > 0) {
					frac = (float)((int)gpGlobals.TickCount - step.Previous.TickCount) / (float)tickdelta;
					frac = Math.Clamp(frac, 0.0f, 1.0f);
				}

				if (step.Previous2.TickCount == 0 || step.Previous2.TickCount >= step.Previous.TickCount) {
					Vector3 delta = step.Next.Origin - step.Previous.Origin;
					MathLib.VectorMA(step.Previous.Origin, frac, delta, out step.NetworkOrigin);
				}
				else if (!step_spline.GetBool()) {
					ref StepSimulationStep pOlder = ref step.Previous;
					ref StepSimulationStep pNewer = ref step.Next;

					if (step.Discontinuity.TickCount > step.Previous.TickCount) {
						if (gpGlobals.TickCount > step.Discontinuity.TickCount)
							pOlder = ref step.Discontinuity;
						else
							pNewer = ref step.Discontinuity;

						tickdelta = pNewer.TickCount - pOlder.TickCount;
						if (tickdelta > 0) {
							frac = (float)(gpGlobals.TickCount - pOlder.TickCount) / (float)tickdelta;
							frac = Math.Clamp(frac, 0.0f, 1.0f);
						}
					}

					Vector3 delta = pNewer.Origin - pOlder.Origin;
					MathLib.VectorMA(pOlder.Origin, frac, delta, out step.NetworkOrigin);
				}
				else {
					MathLib.Hermite_Spline(step.Previous2.Origin, step.Previous.Origin, step.Next.Origin, frac, out step.NetworkOrigin);
				}
			}
		}

		// Angles
		if (step.AnglesActive) {
			// See if external code changed the orientation of the entity
			if (GetStepAngles() != step.NextRotation) {
				step.AnglesActive = false;
			}
			else {
				// Compute interpolated info based on tick interval
				float frac = 1.0f;
				long tickdelta = step.Next.TickCount - step.Previous.TickCount;
				if (tickdelta > 0) {
					frac = (float)(gpGlobals.TickCount - step.Previous.TickCount) / (float)tickdelta;
					frac = Math.Clamp(frac, 0.0f, 1.0f);
				}

				if (step.Previous2.TickCount == 0 || step.Previous2.TickCount >= step.Previous.TickCount) {
					// Pure blend between start/end orientations
					Quaternion outangles;
					MathLib.QuaternionBlend(step.Previous.Rotation, step.Next.Rotation, frac, out outangles);
					MathLib.QuaternionAngles(outangles, out step.NetworkAngles);
				}
				else if (!step_spline.GetBool()) {
					ref StepSimulationStep pOlder = ref step.Previous;
					ref StepSimulationStep pNewer = ref step.Next;

					if (step.Discontinuity.TickCount > step.Previous.TickCount) {
						if (gpGlobals.TickCount > step.Discontinuity.TickCount)
							pOlder = ref step.Discontinuity;
						else
							pNewer = ref step.Discontinuity;

						tickdelta = pNewer.TickCount - pOlder.TickCount;
						if (tickdelta > 0) {
							frac = (float)(gpGlobals.TickCount - pOlder.TickCount) / (float)tickdelta;
							frac = Math.Clamp(frac, 0.0f, 1.0f);
						}
					}

					// Pure blend between start/end orientations
					Quaternion outangles;
					MathLib.QuaternionBlend(pOlder.Rotation, pNewer.Rotation, frac, out outangles);
					MathLib.QuaternionAngles(outangles, out step.NetworkAngles);
				}
				else {
					// FIXME: enable spline interpolation when turning is debounced.
					Quaternion outangles;
					MathLib.Hermite_Spline(step.Previous2.Rotation, step.Previous.Rotation, step.Next.Rotation, frac, out outangles);
					MathLib.QuaternionAngles(outangles, out step.NetworkAngles);
				}
			}
		}
	}
	static readonly ConVar step_spline = new("step_spline", "0");


	protected static object? SendProxy_SendPredictableId(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID) {
		BaseEntity entity = (BaseEntity)instance;
		if (entity == null || !entity.PredictableId.IsActive())
			return null;

		int id_player_index = entity.PredictableId.GetPlayer();
		recipients.SetOnly(id_player_index);

		return instance;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public BaseEntity? GetMoveParent() => MoveParent.Get();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public BaseEntity? FirstMoveChild() => MoveChild.Get();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public BaseEntity? NextMovePeer() => MovePeer.Get();
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool AnyPlayersInHierarchy_R(BaseEntity ent) {
		if (ent.IsPlayer())
			return true;

		for (BaseEntity? cur = ent.FirstMoveChild(); cur != null; cur = cur.NextMovePeer())
			if (AnyPlayersInHierarchy_R(cur))
				return true;

		return false;
	}
	public void RecalcHasPlayerChildBit() {
		if (AnyPlayersInHierarchy_R(this))
			AddEFlags(EFL.HasPlayerChild);
		else
			RemoveEFlags(EFL.HasPlayerChild);
	}
	public bool DoesHavePlayerChild() {
		return IsEFlagSet(EFL.HasPlayerChild);
	}
	public virtual void OnEntityEvent<T>(EntityEvent ev, T? data) {
		switch (ev) {
			case EntityEvent.WaterTouch:

				break;
			case EntityEvent.WaterUntouch:

				break;

			default:
				return;
		}
	}

	public void TransformStepData_ParentToWorld(BaseEntity? parent) {
		// Fix up our step simulation points to be in the proper local space
		ref StepSimulationData step = ref GetDataObject<StepSimulationData>(DataObjectType.StepSimulation);
		if (!Unsafe.IsNullRef(ref step)) {
			// Convert our positions
			Util.ParentToWorldSpace(parent, ref step.Previous2.Origin, ref step.Previous2.Rotation);
			Util.ParentToWorldSpace(parent, ref step.Previous.Origin, ref step.Previous.Rotation);
		}
	}

	public void TransformStepData_ParentToParent(BaseEntity? oldParent, BaseEntity newParent) {
		// Fix up our step simulation points to be in the proper local space
		ref StepSimulationData step = ref GetDataObject<StepSimulationData>(DataObjectType.StepSimulation);
		if (!Unsafe.IsNullRef(ref step)) {
			// Convert our positions
			Util.ParentToWorldSpace(oldParent, ref step.Previous2.Origin, ref step.Previous2.Rotation);
			Util.WorldToParentSpace(newParent, ref step.Previous2.Origin, ref step.Previous2.Rotation);

			Util.ParentToWorldSpace(oldParent, ref step.Previous.Origin, ref step.Previous.Rotation);
			Util.WorldToParentSpace(newParent, ref step.Previous.Origin, ref step.Previous.Rotation);
		}
	}

	public void TransformStepData_WorldToParent(BaseEntity parent) {
		// Fix up our step simulation points to be in the proper local space
		ref StepSimulationData step = ref GetDataObject<StepSimulationData>(DataObjectType.StepSimulation);
		if (!Unsafe.IsNullRef(ref step)) {
			// Convert our positions
			Util.WorldToParentSpace(parent, ref step.Previous2.Origin, ref step.Previous2.Rotation);
			Util.WorldToParentSpace(parent, ref step.Previous.Origin, ref step.Previous.Rotation);
		}
	}

	public void SetParent(string? newParent, BaseEntity activator, int attachment = -1) {
		BaseEntity? parent = gEntList.FindEntityByName(null, newParent, null, activator);

		if (newParent != null && parent == null)
			Msg($"Entity {Classname}({GetDebugName()}) has bad parent {newParent}\n");
		else {
			if (gEntList.FindEntityByName(parent, newParent, null, activator) != null)
				Msg($"Entity {Classname}({GetDebugName()}) has amigious parent {newParent}\n");
			SetParent(parent, attachment);
		}
	}

	public void SetParent(BaseEntity? parentEntity, int attachment = -1) {
		if (attachment == -1)
			attachment = ParentAttachment;

		bool wasNotParented = GetParent() == null;
		BaseEntity? oldParent = GetParent();

		Parent.Set(parentEntity);

		if (parentEntity == this) {
			Assert(false);
			Parent.Set(null);
		}

		if (Parent.Get() == null) {
			ParentName = null;
			TransformStepData_ParentToWorld(oldParent);
			return;
		}

		ParentName = parentEntity!.Name;
		RemoveSolidFlags(SolidFlags.RootParentAligned);

		if (parentEntity != null) {
			if (parentEntity.GetRootMoveParent()!.GetSolid() == SolidType.BSP)
				AddSolidFlags(SolidFlags.RootParentAligned);
			else {
				// Must be SOLID_VPHYSICS because parent might rotate
				if (GetSolid() == SolidType.BSP)
					SetSolid(SolidType.VPhysics);
			}
		}

		// set the move parent if we have one
		if (Edict() != null) {
			// add ourselves to the list
			LinkChild(Parent.Get()!, this);

			ParentAttachment = (byte)attachment;

			EntityMatrix matrix = default, childMatrix = default;
			matrix.InitFromEntity(parentEntity, ParentAttachment); // parent.world
			childMatrix.InitFromEntityLocal(this); // child.world
			Vector3 localOrigin = matrix.WorldToLocal(GetLocalOrigin());

			// I have the axes of local space in world space. (childMatrix)
			// I want to compute those world space axes in the parent's local space
			// and set that transform (as angles) on the child's object so the net
			// result is that the child is now in parent space, but still oriented the same way
			Matrix4x4 tmp = matrix.Transpose(); // world.parent
			tmp.MatrixMul(childMatrix, out matrix.Underlying); // child.parent
			MathLib.MatrixToAngles(matrix, out QAngle angles);
			SetLocalAngles(angles);
			Util.SetOrigin(this, localOrigin);

			// Move our step data into the correct space
			if (wasNotParented) {
				// Transform step data from world to parent-space
				TransformStepData_WorldToParent(this);
			}
			else {
				// Transform step data between parent-spaces
				TransformStepData_ParentToParent(oldParent, this);
			}
		}
		if (VPhysicsGetObject() != null) {
			if (VPhysicsGetObject()!.IsStatic()) {
				if (VPhysicsGetObject()!.IsAttachedToConstraint(false))
					Warning($"SetParent on static object, all constraints attached to {GetDebugName()} ({GetClassname()})will now be broken!\n");

				VPhysicsDestroyObject();
				VPhysicsInitShadow(false, false);
			}
		}
		CollisionRulesChanged();
	}


	public static void EmitSound<IRF>(scoped in IRF filter, int entIndex, scoped in EmitSound_t parms) where IRF : IRecipientFilter {
		BaseEntity? entity = Util.EntityByIndex(entIndex);
		entity?.ModifyEmitSoundParams(ref Unsafe.AsRef(in parms));
		g_SoundEmitterSystem.EmitSound(filter, entIndex, ref Unsafe.AsRef(in parms));
	}

	public virtual Vector3 GetSoundEmissionOrigin() => WorldSpaceCenter();

	public BaseEntity? GetParent() => Parent.Get();

	public static BaseEntity? GetContainingEntity(Edict? ent) {
		if (ent != null && ent.GetUnknown() != null)
			return (BaseEntity?)ent.GetUnknown()!.GetBaseEntity();
		return null;
	}

	public virtual Vector3 GetStepOrigin() => GetLocalOrigin();
	public virtual QAngle GetStepAngles() => GetLocalAngles();

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetSolidFlags(SolidFlags flags) => CollisionProp().SetSolidFlags(flags);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsSolidFlagSet(SolidFlags flagMask) => CollisionProp().IsSolidFlagSet(flagMask);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public SolidFlags GetSolidFlags() => (SolidFlags)CollisionProp().GetSolidFlags();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void AddSolidFlags(SolidFlags flags) => CollisionProp().AddSolidFlags(flags);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void RemoveSolidFlags(SolidFlags flags) => CollisionProp().RemoveSolidFlags(flags);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsSolid() => CollisionProp().IsSolid();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetSolid(SolidType val) => CollisionProp().SetSolid(val);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public SolidType GetSolid() => CollisionProp().GetSolid();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetCollisionBounds(in Vector3 mins, in Vector3 maxs) => CollisionProp().SetCollisionBounds(in mins, in maxs);


	public virtual void ChangeTeam(int teamNum) => TeamNum = teamNum;

	public Team? GetTeam() => GetGlobalTeam(TeamNum);

	public int GetTeamNumber() => TeamNum;

	IPhysicsObject? PhysicsObject = null!;
	public void VPhysicsUpdate(IPhysicsObject physics) {
		switch (GetMoveType()) {
			case Source.MoveType.VPhysics: {
					if (GetMoveParent() != null) {
						DevWarning($"Updating physics on object in hierarchy {GetClassname()}!\n");
						return;
					}

					physics.GetPosition(out Vector3 origin, out QAngle angles);

					if (!IsEntityQAngleReasonable(angles))
						angles = vec3_angle;

					if (IsEntityPositionReasonable(origin))
						SetAbsOrigin(origin);

					angles.X = MathLib.AngleNormalize(angles.X);
					angles.Y = MathLib.AngleNormalize(angles.Y);
					angles.Z = MathLib.AngleNormalize(angles.Z);
					SetAbsAngles(angles);
					break;
				}
			default:
				break;
		}
	}

	public virtual void VPhysicsShadowCollision(int index, ref GameVCollisionEvent ev) { }

	public virtual void VPhysicsCollision(int index, ref GameVCollisionEvent ev) {
		int otherIndex = index == 0 ? 1 : 0;
		BaseEntity hitEntity = ev.Entities[otherIndex]!;

		if (GetMoveType() != Source.MoveType.VPhysics && hitEntity.GetMoveType() != Source.MoveType.VPhysics)
			return;

		if (ev.VCollisionEvent.DeltaCollisionTime < 0.5 && (hitEntity == this))
			return;

		if (hitEntity == this)
			return;

		SurfaceData_ptr hit = physprops.GetSurfaceData(ev.VCollisionEvent.SurfaceProps[otherIndex])!;
		SurfaceData_ptr props = physprops.GetSurfaceData(ev.VCollisionEvent.SurfaceProps[index])!;
		if ((CharTex)hit.Game.Material == CharTex.Default || (CharTex)props.Game.Material == CharTex.Default)
			return;

		if (hitEntity == this)
			PhysCollisionSound(this, ev.VCollisionEvent.Objects[index]!, SoundEntityChannel.Body, ev.VCollisionEvent.SurfaceProps[index], ev.VCollisionEvent.SurfaceProps[otherIndex], ev.VCollisionEvent.DeltaCollisionTime, ev.VCollisionEvent.CollisionSpeed);
		else
			PhysCollisionSound(this, ev.VCollisionEvent.Objects[index]!, SoundEntityChannel.Static, ev.VCollisionEvent.SurfaceProps[index], ev.VCollisionEvent.SurfaceProps[otherIndex], ev.VCollisionEvent.DeltaCollisionTime, ev.VCollisionEvent.CollisionSpeed);
		PhysCollisionScreenShake(ref ev, index);

		PhysCollisionDust(ref ev, hit);
	}

	public virtual void VPhysicsFriction(IPhysicsObject obj, float energy, int surfaceProps, int surfacePropsHit) {
		PhysFrictionSound(this, obj, energy, surfaceProps, surfacePropsHit);
	}

	public virtual bool ForceVPhysicsCollide(BaseEntity entity) => false;

	public virtual bool PhysicsSplash(in Vector3 centerPoint, in Vector3 normal, float rawSpeed, float scaledSpeed) => false;

	public IPhysicsObject? VPhysicsGetObject() => PhysicsObject;

	public void VPhysicsSetObject(IPhysicsObject? physics) {
		if (PhysicsObject != null && physics != null)
			Warning($"Overwriting physics object for {GetClassname()}\n");
		PhysicsObject = physics;
	}

	bool VPhysicsInitSetup() {
		// don't support logical ents
		if (Edict() == null || IsMarkedForDeletion())
			return false;

		// If this entity already has a physics object, then it should have been deleted prior to making this call.
		Assert(PhysicsObject == null);
		VPhysicsDestroyObject();

		return true;
	}

	public IPhysicsObject? VPhysicsInitNormal(SolidType solidType, SolidFlags nSolidFlags, bool createAsleep) {
		return VPhysicsInitNormal(solidType, nSolidFlags, createAsleep, ref Unsafe.NullRef<Solid>());
	}

	public IPhysicsObject? VPhysicsInitNormal(SolidType solidType, SolidFlags nSolidFlags, bool createAsleep, ref Solid solid) {
		if (!VPhysicsInitSetup())
			return null;

		// NOTE: This has to occur before PhysModelCreate because that call will
		// call back into ShouldCollide(), which uses solidtype for rules.
		SetSolid(solidType);
		SetSolidFlags(nSolidFlags);

		// No physics
		if (solidType == SolidType.None)
			return null;

		// create a normal physics object
		IPhysicsObject? physicsObject = PhysModelCreate(this, GetModelIndex(), GetAbsOrigin(), GetAbsAngles(), ref solid);
		if (physicsObject != null) {
			VPhysicsSetObject(physicsObject);
			SetMoveType(Source.MoveType.VPhysics);

			if (!createAsleep)
				physicsObject.Wake();
		}

		return physicsObject;
	}
	public int VPhysicsGetObjectList(Span<IPhysicsObject> list) {
		IPhysicsObject? phys = VPhysicsGetObject();
		if (phys != null) {
			Assert((phys.GetGameFlags() & PhysicsFlags.MultiObjectEntity) == 0);
			if (list.Length > 0) {
				list[0] = phys;
				return 1;
			}
		}
		return 0;
	}

	public class TimedOverlay
	{
		public string Msg = "";
		public TimeUnit_t MsgEndTime;
		public TimeUnit_t MsgStartTime;
		public TimedOverlay? NextTimedOverlay;
	}

	public TimedOverlay? TimedOverlayList;

	public void AddTimedOverlay(string msg, int endTime) {
		TimedOverlay newTO = new();
		newTO.Msg = msg;
		newTO.MsgEndTime = gpGlobals.CurTime + endTime;
		newTO.MsgStartTime = gpGlobals.CurTime;
		newTO.NextTimedOverlay = TimedOverlayList;
		TimedOverlayList = newTO;
	}

	public bool IsFloating() {
		if (!IsEFlagSet(EFL.TouchingFluid))
			return false;

		IPhysicsObject? phys = VPhysicsGetObject();
		if (phys == null)
			return false;

		int materialIndex = phys.GetMaterialIndex();
		physprops.GetPhysicsProperties(materialIndex, out float density, out float thickness, out float friction, out float elasticity);

		return density < 1000.0f;
	}

	public static BaseEntity? Instance(Edict? ent) => GetContainingEntity(ent);
	public static BaseEntity? Instance(int ent) => Instance(INDEXENT(ent)!);

	public static BaseEntity? Create(ReadOnlySpan<char> name, Vector3 origin, QAngle angles, BaseEntity? owner = null) {
		BaseEntity? ent = CreateNoSpawn(name, origin, angles, owner);
		Util.DispatchSpawn(ent);
		return ent;
	}

	public static BaseEntity? CreateNoSpawn(ReadOnlySpan<char> name, Vector3 origin, QAngle angles, BaseEntity? owner = null) {
		BaseEntity? ent = CreateEntityByName(name);
		if (ent == null) {
			AssertMsg(false, "CreateNoSpawn: only works for CBaseEntities");
			return null;
		}

		ent.SetLocalOrigin(origin);
		ent.SetLocalAngles(angles);
		ent.SetOwnerEntity(owner);

		gEntList.NotifyCreateEntity(ent);

		return ent;
	}

	[NetworkName("m_nRenderFX")]
	public byte RenderFX;
	[NetworkName("m_nRenderMode")]
	public byte RenderMode;
	public byte OldRenderMode;
	[NetworkName("m_fEffects")]
	public int Effects;
	[NetworkName("m_clrRender")]
	public Source.Color ColorRender;
	[NetworkName("m_iTeamNum")]
	public int TeamNum;
	[NetworkName("m_CollisionGroup")]
	public int CollisionGroup;
	[NetworkName("m_flElasticity")]
	public float Elasticity;
	[NetworkName("m_flShadowCastDistance")]
	[NetworkVar] public partial float ShadowCastDistance { get; set; }
	[NetworkName("m_iParentAttachment")]
	public byte ParentAttachment;
	[NetworkName("movetype")]
	public byte MoveType;
	[NetworkName("movecollide")]
	public byte MoveCollide;
	public Vector3 AbsOrigin;
	public QAngle AbsRotation;
	[NetworkName("m_vecOrigin")]
	[NetworkVar] public partial Vector3 Origin { get; set; }
	[NetworkName("m_angRotation")]
	[NetworkVar] public partial QAngle Rotation { get; set; }
	[NetworkName("m_iTextureFrameIndex")]
	public bool TextureFrameIndex;
	[NetworkName("m_bSimulatedEveryTick")]
	public bool SimulatedEveryTick;
	[NetworkName("m_bAnimatedEveryTick")]
	public bool AnimatedEveryTick;
	[NetworkName("m_bAlternateSorting")]
	[NetworkVar] public partial bool AlternateSorting { get; set; }

	public byte m_takedamage;
	[NetworkName("m_RealClassName")]
	public ushort RealClassName;
	[NetworkName("m_OverrideMaterial")]
	public ushort OverrideMaterial;
	[NetworkName("m_OverrideSubMaterials")]
	public InlineArray32<ushort> OverrideSubMaterials;
	[NetworkName("m_iHealth")]
	public int Health;
	[NetworkName("m_iMaxHealth")]
	[NetworkVar] public partial int MaxHealth { get; set; }
	[NetworkName("m_spawnflags")]
	public int SpawnFlags;
	[NetworkName("m_iGModFlags")]
	public int GModFlags;
	[NetworkName("m_bOnFire")]
	public bool OnFire;
	[NetworkName("m_CreationTime")]
	public float CreationTime;
	[NetworkName("m_vecVelocity")]
	public Vector3 Velocity;
	[NetworkName("m_iCreationID")]
	public int CreationID;
	[NetworkName("m_iMapCreatedID")]
	public int MapCreatedID;

	public readonly List<ThinkFunc> ThinkFunctions = [];
	public int CurrentThinkContext = NO_THINK_CONTEXT;

	[NetworkName("m_PredictableID")]
	public readonly PredictableId PredictableId = new();

	[NetworkName("m_GMOD_DataTable")]
	public readonly IGMODDataTable GMOD_DataTable;

	public float Speed;

	[NetworkName("m_hOwnerEntity")]
	public EHANDLE OwnerEntity = new();
	[NetworkName("m_hEffectEntity")]
	public EHANDLE EffectEntity = new();
	[NetworkName("moveparent")]
	public EHANDLE MoveParent = new();
	public EHANDLE MoveChild = new();
	public EHANDLE MovePeer = new();
	[NetworkName("m_hGroundEntity")]
	public EHANDLE GroundEntity = new();

	public string? ModelName;

	[NetworkName("m_lifeState")]
	public int LifeState;
	[NetworkName("m_vecBaseVelocity")]
	public Vector3 BaseVelocity;
	[NetworkName("m_nNextThinkTick")]
	public int NextThinkTick;
	public int LastThinkTick;
	[NetworkName("m_nWaterLevel")]
	public byte WaterLevel;
	public byte WaterType;

	[NetworkName("m_GMOD_bool")]
	InlineArray32<bool> GMOD_bool;
	[NetworkName("m_GMOD_float")]
	InlineArray32<float> GMOD_float;
	[NetworkName("m_GMOD_int")]
	InlineArray32<int> GMOD_int;
	[NetworkName("m_GMOD_Vector")]
	InlineArray32<Vector3> GMOD_Vector;
	[NetworkName("m_GMOD_QAngle")]
	InlineArray32<QAngle> GMOD_QAngle;
	[NetworkName("m_GMOD_EHANDLE")]
	InlineArray32<EHANDLE> GMOD_EHANDLE; // << ENSURE THESE ARE INITIALIZED!!!!
	[NetworkName("m_GMOD_String0")]
	InlineArray512<char> GMOD_String0;
	[NetworkName("m_GMOD_String1")]
	InlineArray512<char> GMOD_String1;
	[NetworkName("m_GMOD_String2")]
	InlineArray512<char> GMOD_String2;
	[NetworkName("m_GMOD_String3")]
	InlineArray512<char> GMOD_String3;

	public int DataObjectTypes;

	public static readonly ServerClass ServerClass = new ServerClass(DT_BaseEntity);

	[NetworkName("m_flAnimTime")]
	public TimeUnit_t AnimTime;
	public TimeUnit_t PrevAnimTime;
	[NetworkName("m_flSimulationTime")]
	public TimeUnit_t SimulationTime;
	[NetworkName("m_vecViewOffset")]
	public Vector3 ViewOffset;
	public Vector3 NetworkAngles;
	[NetworkName("m_ubInterpolationFrame")]
	public byte InterpolationFrame;
	[NetworkName("m_nModelIndex")]
	public int ModelIndex;
	[NetworkName("m_Collision")]
	public CollisionProperty Collision = new();
	[NetworkName("m_flFriction")]
	public float Friction;
	public long SimulationTick;

	public virtual Vector3 BodyTarget(in Vector3 posSrc, bool noisy) => WorldSpaceCenter();
	public virtual Vector3 HeadTarget(in Vector3 posSrc) => EyePosition();

	public virtual int GetMaxHealth() => MaxHealth;
	public void GetMaxHealth(int amt) => MaxHealth = amt;

	public int GetHealth() => Health;
	public int SetHealth(int amt) => Health = amt;

	public float HealthFraction() {
		if (GetMaxHealth() == 0)
			return 1.0f;

		float fraction = (float)GetHealth() / (float)GetMaxHealth();
		fraction = Math.Clamp(fraction, 0.0f, 1.0f);
		return fraction;
	}

	public int TakeHealth(float health, DamageType damageType) {
		if (Edict() == null || (Damage)m_takedamage < Damage.Yes)
			return 0;

		int iMax = GetMaxHealth();

		// heal
		if (Health >= iMax)
			return 0;

		int oldHealth = Health;

		Health += (int)health;

		if (Health > iMax)
			Health = iMax;

		return Health - oldHealth;
	}

	static int TakeDamage__warningCount = 0;

	public int TakeDamage(in TakeDamageInfo inputInfo) {
		if (null == g_pGameRules)
			return 0;

		bool bHasPhysicsForceDamage = !g_pGameRules.Damage_NoPhysicsForce(inputInfo.GetDamageType());
		if (bHasPhysicsForceDamage && inputInfo.GetDamageType() != DamageType.Generic) {
			// If you hit this assert, you've called TakeDamage with a damage type that requires a physics damage
			// force & position without specifying one or both of them. Decide whether your damage that's causing 
			// this is something you believe should impart physics force on the receiver. If it is, you need to 
			// setup the damage force & position inside the CTakeDamageInfo (Utility functions for this are in
			// takedamageinfo.cpp. If you think the damage shouldn't cause force (unlikely!) then you can set the 
			// damage type to DMG_GENERIC, or | DMG_CRUSH if you need to preserve the damage type for purposes of HUD display.

			if (inputInfo.GetDamageForce() == vec3_origin || inputInfo.GetDamagePosition() == vec3_origin) {
				if (++TakeDamage__warningCount < 10) {
					if (inputInfo.GetDamageForce() == vec3_origin)
						DevWarning("CBaseEntity::TakeDamage:  with inputInfo.GetDamageForce() == vec3_origin\n");
					if (inputInfo.GetDamagePosition() == vec3_origin)
						DevWarning("CBaseEntity::TakeDamage:  with inputInfo.GetDamagePosition() == vec3_origin\n");
				}
			}
		}

		// Make sure our damage filter allows the damage.
		if (!PassesDamageFilter(in inputInfo))
			return 0;

		if (!g_pGameRules.AllowDamage(this, in inputInfo))
			return 0;


		if (PhysIsInCallback())
			PhysCallbackDamage(this, in inputInfo);
		else {
			TakeDamageInfo info = inputInfo;

			// Scale the damage by the attacker's modifier.
			if (info.GetAttacker() != null)
				info.ScaleDamage(info.GetAttacker()!.GetAttackDamageScale(this));

			// Scale the damage by my own modifiers
			info.ScaleDamage(GetReceivedDamageScale(info.GetAttacker()));

			//Msg("%s took %.2f Damage, at %.2f\n", GetClassname(), info.GetDamage(), gpGlobals.curtime );

			return OnTakeDamage(info);
		}
		return 0;
	}

	public readonly LinkedList<DamageModifier> DamageModifiers = [];

	public virtual float GetAttackDamageScale(BaseEntity? victim) {
		float flScale = 1;
		foreach (var damageModifier in DamageModifiers)
			if (!damageModifier.IsDamageDoneToMe())
				flScale *= damageModifier.GetModifier();
		return flScale;
	}

	EHANDLE DamageFilter;

	public virtual bool PassesDamageFilter(in TakeDamageInfo info) {
		if (DamageFilter.Get() != null) {
			BaseFilter filter = (BaseFilter)DamageFilter.Get()!;
			return filter.PassesDamageFilter(in info);
		}
		return true;
	}

	public virtual float GetReceivedDamageScale(BaseEntity? victim) {
		float flScale = 1;
		foreach (var damageModifier in DamageModifiers)
			if (damageModifier.IsDamageDoneToMe())
				flScale *= damageModifier.GetModifier();
		return flScale;
	}

	public virtual void NetworkStateChanged() => NetworkProp().NetworkStateChanged();
	public virtual void NetworkStateChanged(IFieldAccessor accessor) => NetworkProp().NetworkStateChanged(accessor);

	public int VPhysicsTakeDamage(in TakeDamageInfo info) {
		// don't let physics impacts or fire cause objects to move (again)
		bool bNoPhysicsForceDamage = g_pGameRules.Damage_NoPhysicsForce(info.GetDamageType());
		if (bNoPhysicsForceDamage || info.GetDamageType() == DamageType.Generic)
			return 1;

		Assert(VPhysicsGetObject() != null);
		if (VPhysicsGetObject() != null) {
			Vector3 force = info.GetDamageForce();
			Vector3 offset = info.GetDamagePosition();

			// If you hit this assert, you've called TakeDamage with a damage type that requires a physics damage
			// force & position without specifying one or both of them. Decide whether your damage that's causing 
			// this is something you believe should impart physics force on the receiver. If it is, you need to 
			// setup the damage force & position inside the CTakeDamageInfo (Utility functions for this are in
			// takedamageinfo.cpp. If you think the damage shouldn't cause force (unlikely!) then you can set the 
			// damage type to DMG_GENERIC, or | DMG_CRUSH if you need to preserve the damage type for purposes of HUD display.
#if !TF_DLL
			Assert(force != vec3_origin && offset != vec3_origin);
#else
			// todo
#endif

			PhysicsFlags gameFlags = VPhysicsGetObject()!.GetGameFlags();
			if ((gameFlags & PhysicsFlags.PlayerHeld) != 0) {
				// if the player is holding the object, use it's real mass (player holding reduced the mass)
				BasePlayer? player = Util.GetLocalPlayer();
				if (player != null) {
					float mass = player.GetHeldObjectMass(VPhysicsGetObject()!);
					if (mass != 0.0f) {
						float ratio = VPhysicsGetObject()!.GetMass() / mass;
						force *= ratio;
					}
				}
			}
			else if ((gameFlags & PhysicsFlags.PartOfRagdoll) != 0 && (gameFlags & PhysicsFlags.ConstraintStatic) != 0) {
				IPhysicsObject[] list = ArrayPool<IPhysicsObject>.Shared.Rent(VPHYSICS_MAX_OBJECT_LIST_COUNT);
				int count = VPhysicsGetObjectList(list);
				for (int i = 0; i < count; i++) {
					if (0 == (list[i].GetGameFlags() & PhysicsFlags.ConstraintStatic)) {
						list[i].ApplyForceOffset(force, offset);
						return 1;
					}
				}

			}
			VPhysicsGetObject()!.ApplyForceOffset(force, offset);
		}

		return 1;
	}

	public ref readonly QAngle GetLocalAngularVelocity() => ref AngVelocity;

	public EHANDLE Blocker = new();
	public TimeUnit_t LocalTime;
	public TimeUnit_t VPhysicsUpdateLocalTime;
	public TimeUnit_t MoveDoneTime;
	public int PushEnumCount;
	public BASEPTR? FnMoveDone;

	public TimeUnit_t GetLocalTime() => LocalTime;
	public void IncrementLocalTime(TimeUnit_t timeDelta) => LocalTime += timeDelta;
	public TimeUnit_t GetMoveDoneTime() => (MoveDoneTime >= 0) ? MoveDoneTime - GetLocalTime() : -1;

	public void SetMoveDoneTime(TimeUnit_t delay) {
		if (delay >= 0)
			MoveDoneTime = GetLocalTime() + delay;
		else
			MoveDoneTime = -1;
		CheckHasGamePhysicsSimulation();
	}

	public void SetMoveDone(Action? a) => FnMoveDone = a == null ? null : _ => a();
	public virtual void MoveDone() => FnMoveDone?.Invoke(this);

	public void SUB_CallUseToggle() => Use(this, this, UseType.Toggle, 0);
	public void SUB_DoNothing() { }

	public bool HasTarget(ReadOnlySpan<char> targetname) {
		if (!targetname.IsEmpty && Target != null)
			return FStrEq(targetname, Target);
		else
			return false;
	}

	public void EntityText(int text_offset, ReadOnlySpan<char> text, float duration, int r = 255, int g = 255, int b = 255, int a = 255) {
		Vector3 origin;
		Vector3 vecLocalCenter = (CollisionProp().OBBMins() + CollisionProp().OBBMaxs()) * 0.5f;

		if ((CollisionProp().GetCollisionAngles() == vec3_angle) || (vecLocalCenter == vec3_origin))
			origin = vecLocalCenter + CollisionProp().GetCollisionOrigin();
		else
			MathLib.VectorTransform(vecLocalCenter, CollisionProp().CollisionToWorldTransform(), out origin);

		DebugOverlay.EntityTextAtPosition(origin, text_offset, text, duration, r, g, b, a);
	}

	public virtual void DrawDebugGeometryOverlays() {
		// todo	
	}
	public virtual int DrawDebugTextOverlays() {
		int offset = 1;
		if ((DebugOverlays & DebugOverlayBits.Text) != 0) {
			EntityText(offset, $"({EntIndex()}) Name: {GetDebugName()} ({GetClassname()})", 0);
			offset++;

			if (GlobalName != null) {
				EntityText(offset, $"GLOBALNAME: {GlobalName}", 0);
				offset++;
			}

			Vector3 vecOrigin = GetAbsOrigin();
			EntityText(offset, $"Position: {vecOrigin.X:F1}, {vecOrigin.Y:F1}, {vecOrigin.Z:F1}", 0);
			offset++;

			if (ModelName != null || GetBaseAnimating() != null) {
				EntityText(offset, $"Model:{ModelName}", 0);
				offset++;
			}

			if (DamageFilter.Get() != null) {
				EntityText(offset, $"DAMAGE FILTER:{DamageFilter.Get()!.GetDebugName()}", 0);
				offset++;
			}
		}

		if ((DebugOverlays & DebugOverlayBits.ViewOffset) != 0)
			DebugOverlay.Cross3D(EyePosition(), 16, 255, 0, 0, true, 0.05f);

		return offset;
	}

	public void UpdatePhysicsShadowToCurrentPosition(TimeUnit_t deltaTime) {
		if (GetMoveType() != Source.MoveType.VPhysics) {
			IPhysicsObject? phys = VPhysicsGetObject();
			phys?.UpdateShadow(GetAbsOrigin(), GetAbsAngles(), false, (float)deltaTime);
		}
	}

	public void ComputeAbsPosition(in Vector3 localPosition, out Vector3 absPosition) {
		BaseEntity? moveParent = GetMoveParent();
		if (moveParent == null)
			absPosition = localPosition;
		else
			MathLib.VectorTransform(localPosition, moveParent.EntityToWorldTransform(), out absPosition);
	}

	public void SetLocalAngularVelocity(in QAngle vecAngVelocity) {
		if (!IsEntityQAngleVelReasonable(vecAngVelocity)) {
			if (CheckEmitReasonablePhysicsSpew())
				Warning($"Bad SetLocalAngularVelocity({vecAngVelocity.X},{vecAngVelocity.Y},{vecAngVelocity.Z}) on {GetDebugName()}");
			Assert(false);
			return;
		}

		if (AngVelocity != vecAngVelocity)
			AngVelocity = vecAngVelocity;
	}

	public void SetLocalVelocity(in Vector3 velocity) {
		Vector3 vecVelocity = velocity;

		// Safety check against receive a huge impulse, which can explode physics
		switch (CheckEntityVelocity(ref vecVelocity)) {
			case -1:
				Warning($"Discarding SetLocalVelocity({vecVelocity.X},{vecVelocity.Y},{vecVelocity.Z}) on {GetDebugName()}\n");
				Assert(false);
				return;
			case 0:
				if (CheckEmitReasonablePhysicsSpew())
					Warning($"Clamping SetLocalVelocity({velocity.X},{velocity.Y},{velocity.Z}) on {GetDebugName()}\n");
				break;
		}

		if (Velocity != vecVelocity) {
			InvalidatePhysicsRecursive(InvalidatePhysicsBits.VelocityChanged);
			Velocity = vecVelocity;
		}
	}

	public void SetAbsVelocity(in Vector3 absVelocity) {
		if (AbsVelocity == absVelocity)
			return;

		// The abs velocity won't be dirty since we're setting it here
		// All children are invalid, but we are not
		InvalidatePhysicsRecursive(InvalidatePhysicsBits.VelocityChanged);
		RemoveEFlags(EFL.DirtyAbsVelocity);

		AbsVelocity = absVelocity;

		// NOTE: Do *not* do a network state change in this case.
		// m_vecVelocity is only networked for the player, which is not manual mode
		BaseEntity? moveParent = GetMoveParent();
		if (moveParent == null) {
			Velocity = absVelocity;
			return;
		}

		// First subtract out the parent's abs velocity to get a relative
		// velocity measured in world space
		Vector3 relVelocity;
		MathLib.VectorSubtract(AbsVelocity, moveParent.GetAbsVelocity(), out relVelocity);

		// Transform relative velocity into parent space
		Vector3 vNew;
		MathLib.VectorIRotate(relVelocity, moveParent.EntityToWorldTransform(), out vNew);
		Velocity = vNew;
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref readonly Vector3 WorldAlignMins() {
		Assert(!CollisionProp().IsBoundsDefinedInEntitySpace());
		Assert(CollisionProp().GetCollisionAngles() == vec3_angle);
		return ref CollisionProp().OBBMins();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref readonly Vector3 WorldAlignMaxs() {
		Assert(!CollisionProp().IsBoundsDefinedInEntitySpace());
		Assert(CollisionProp().GetCollisionAngles() == vec3_angle);
		return ref CollisionProp().OBBMaxs();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref readonly Vector3 WorldAlignSize() {
		Assert(!CollisionProp().IsBoundsDefinedInEntitySpace());
		Assert(CollisionProp().GetCollisionAngles() == vec3_angle);
		return ref CollisionProp().OBBSize();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float BoundingRadius() => CollisionProp().BoundingRadius();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsPointSized() => CollisionProp().BoundingRadius() == 0.0f;
	public virtual int OnTakeDamage(in TakeDamageInfo info) {
		Vector3 vecTemp = default;

		if (Edict() == null || (Damage)m_takedamage == 0)
			return 0;

		if (info.GetInflictor() != null)
			vecTemp = info.GetInflictor()!.WorldSpaceCenter() - (WorldSpaceCenter());
		else
			vecTemp.Init(1, 0, 0);


		// this global is still used for glass and other non-NPC killables, along with decals.
		g_vecAttackDir = vecTemp;
		MathLib.VectorNormalize(ref g_vecAttackDir);

		// save damage based on the target's armor level

		// figure momentum add (don't let hurt brushes or other triggers move player)

		// physics objects have their own calcs for this: (don't let fire move things around!)
		if (!IsEFlagSet(EFL.NoDamageForces)) {
			if ((GetMoveType() == Source.MoveType.VPhysics)) {
				VPhysicsTakeDamage(info);
			}
			else {
				if (info.GetInflictor() != null && (GetMoveType() == Source.MoveType.Walk || GetMoveType() == Source.MoveType.Step) &&
					!info.GetAttacker()!.IsSolidFlagSet(SolidFlags.Trigger)) {
					Vector3 vecDir, vecInflictorCentroid;
					vecDir = WorldSpaceCenter();
					vecInflictorCentroid = info.GetInflictor()!.WorldSpaceCenter();
					vecDir -= vecInflictorCentroid;
					MathLib.VectorNormalize(ref vecDir);

					Vector3 worldSize = WorldAlignSize();
					float flForce = info.GetDamage() * ((32 * 32 * 72.0f) / (worldSize.X * worldSize.Y * worldSize.Z)) * 5;

					if (flForce > 1000.0f)
						flForce = 1000.0f;
					ApplyAbsVelocityImpulse(vecDir * flForce);
				}
			}
		}

		if ((Damage)m_takedamage != Damage.EventsOnly) {
			// do the damage
			Health -= (int)info.GetDamage();
			if (Health <= 0) {
				Event_Killed(info);
				return 0;
			}
		}

		return 1;
	}

	public virtual void Event_KilledOther(BaseEntity killed, in TakeDamageInfo info) {

	}

	public virtual void Event_Killed(in TakeDamageInfo info) {
		info.GetAttacker()?.Event_KilledOther(this, info);

		m_takedamage = (byte)Damage.No;
		LifeState = (int)Source.LifeState.Dead;
		Util.Remove(this);
	}

	public static bool FClassnameIs(BaseEntity? entity, ReadOnlySpan<char> classname) {
		if (entity == null)
			return false;

		return 0 == strcmp(entity.GetClassname(), classname);
	}

	public bool IsServer() => true;
	public bool IsClient() => false;
	public ReadOnlySpan<char> GetDLLType() => "server";

	public WaterLevel GetWaterLevel() => (WaterLevel)WaterLevel;
	public void SetWaterLevel(WaterLevel level) => WaterLevel = (byte)level;
	public void SetMoveCollide(MoveCollide moveCollide) => MoveCollide = (byte)moveCollide;
	public CollisionProperty CollisionProp() => Collision;

	public virtual void SetModel(ReadOnlySpan<char> modelName) {
		modelName = modelName.SliceNullTerminatedString();

		int modelIndex = modelinfo.GetModelIndex(modelName);
		Model? model = modelinfo.GetModel(modelIndex);
		if (model != null && modelinfo.GetModelType(model) != ModelType.Brush)
			Msg($"Setting CBaseEntity to non-brush model {modelName}\n");

		Util.SetModel(this, modelName);
	}

	internal void SetAbsAngles(in QAngle absAngles) {
		// This is necessary to get the other fields of m_rgflCoordinateFrame ok
		CalcAbsolutePosition();

		// FIXME: The normalize caused problems in server code like momentary_rot_button that isn't
		//        handling things like +/-180 degrees properly. This should be revisited.
		//QAngle angleNormalize( AngleNormalize( absAngles.x ), AngleNormalize( absAngles.y ), AngleNormalize( absAngles.z ) );

		if (AbsRotation == absAngles)
			return;

		// All children are invalid, but we are not
		InvalidatePhysicsRecursive(InvalidatePhysicsBits.AnglesChanged);
		RemoveEFlags(EFL.DirtyAbsTransform);

		AbsRotation = absAngles;
		MathLib.AngleMatrix(absAngles, out CoordinateFrame);
		MathLib.MatrixSetColumn(AbsOrigin, 3, ref CoordinateFrame);

		QAngle angNewRotation = default;
		BaseEntity? moveParent = GetMoveParent();
		if (moveParent == null)
			angNewRotation = absAngles;
		else {
			if (AbsRotation == moveParent.GetAbsAngles())
				angNewRotation.Init();
			else {
				// Moveparent case: transform the abs transform into local space
				Matrix3x4 worldToParent, localMatrix;
				MathLib.MatrixInvert(moveParent.EntityToWorldTransform(), out worldToParent);
				MathLib.ConcatTransforms(worldToParent, CoordinateFrame, out localMatrix);
				MathLib.MatrixAngles(localMatrix, out angNewRotation);
			}
		}

		if (Rotation != angNewRotation) {
			Rotation = angNewRotation;
			SetSimulationTime(gpGlobals.CurTime);
		}
	}

	public string? Name;

	public string GetEntityName() {
		return Name;
	}
	public void SetName(ReadOnlySpan<char> name) {
		Name = new(name.SliceNullTerminatedString());
	}
	public void SetName(string? name) {
		Name = name;
	}
	public void SetOwnerEntity(BaseEntity? owner) => OwnerEntity.Set(owner);
	public BaseEntity? GetOwnerEntity() => OwnerEntity.Get();

	public void SetMoveType(MoveType val, MoveCollide moveCollide = Source.MoveCollide.Default) {
		if (MoveType == (byte)val) {
			MoveCollide = (byte)moveCollide;
			return;
		}

		// This is needed to the removal of MOVETYPE_FOLLOW:
		// We can't transition from follow to a different movetype directly
		// or the leaf code will break.
		Assert(!IsEffectActive(EntityEffects.BoneMerge));
		MoveType = (byte)val;
		MoveCollide = (byte)moveCollide;

		CollisionRulesChanged();

		switch ((MoveType)MoveType) {
			case Source.MoveType.Walk: {
					SetSimulatedEveryTick(true);
					SetAnimatedEveryTick(true);
				}
				break;
			case Source.MoveType.Step: {
					// This will probably go away once I remove the cvar that controls the test code
					SetSimulatedEveryTick(Physics.g_bTestMoveTypeStepSimulation);
					SetAnimatedEveryTick(false);
				}
				break;
			case Source.MoveType.Fly:
			case Source.MoveType.FlyGravity: {
					// Initialize our water state, because these movetypes care about transitions in/out of water
					UpdateWaterState();
				}
				break;
			default: {
					SetSimulatedEveryTick(true);
					SetAnimatedEveryTick(false);
				}
				break;
		}

		// This will probably go away or be handled in a better way once I remove the cvar that controls the test code
		CheckStepSimulationChanged();
		CheckHasGamePhysicsSimulation();
	}


	public bool GetCheckUntouch() => IsEFlagSet(EFL.CheckUntouch);
	public Handle<BasePlayer> PlayerSimulationOwner = new();

	void UpdateBaseVelocity() { }

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsMarkedForDeletion() => (eflags & EFL.KillMe) != 0;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void SetEFlags(EFL flags) {
		eflags = flags;
		if ((flags & (EFL.ForceCheckTransmit | EFL.InSkybox)) != 0)
			DispatchUpdateTransmitState();
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddEFlags(EFL flags) {
		eflags |= flags;
		if ((flags & (EFL.ForceCheckTransmit | EFL.InSkybox)) != 0)
			DispatchUpdateTransmitState();
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void RemoveEFlags(EFL flags) {
		eflags &= ~flags;
		if ((flags & (EFL.ForceCheckTransmit | EFL.InSkybox)) != 0)
			DispatchUpdateTransmitState();
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsEFlagSet(EFL mask) => (eflags & mask) != 0;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public EFL GetEFlags() => eflags;

	public void SetBlocksLOS(bool blocksLOS) {
		if (blocksLOS)
			RemoveEFlags(EFL.DontBlockLOS);
		else
			AddEFlags(EFL.DontBlockLOS);
	}

	public bool BlocksLOS() => !IsEFlagSet(EFL.DontBlockLOS);

	public Vector3 AbsVelocity;
	public QAngle AngVelocity;

	public ref readonly Vector3 GetLocalVelocity() => ref Velocity;

	public ref readonly Vector3 GetAbsVelocity() {
		if (IsEFlagSet(EFL.DirtyAbsVelocity))
			CalcAbsoluteVelocity();

		return ref AbsVelocity;
	}

	public void CalcAbsoluteVelocity() {
		if (!IsEFlagSet(EFL.DirtyAbsVelocity))
			return;

		RemoveEFlags(EFL.DirtyAbsVelocity);

		BaseEntity? moveParent = GetMoveParent();
		if (moveParent == null) {
			AbsVelocity = Velocity;
			return;
		}

		MathLib.VectorRotate(Velocity, moveParent.EntityToWorldTransform(), out AbsVelocity);

		AbsVelocity += moveParent.GetAbsVelocity();
	}

	public string? Classname; // prev m_iClassname
	public string? GlobalName; // prev m_iGlobalname
	public string? ParentName; // prev m_iParent
	public int HammerID;

	public void SetClassname(ReadOnlySpan<char> classname) {
		Classname = new(classname);
	}

	public Edict Edict() => NetworkProp().Edict();

	public virtual void PostConstructor(ReadOnlySpan<char> classname) {
		if (!classname.IsEmpty)
			SetClassname(classname);

		Assert(Classname != null);

		// Possibly get an edict, and add self to global list of entites.
		if (IsEFlagSet(EFL.ServerOnly))
			gEntList.AddNonNetworkableEntity(this);
		else {
			// Certain entities set up their edicts in the constructor
			if (!IsEFlagSet(EFL.NoAutoEdictAttach)) {
				NetworkProp().AttachEdict(g_pForceAttachEdict);
				g_pForceAttachEdict = null;
			}

			// Some ents like the player override the AttachEdict function and do it at a different time.
			// While precaching, they don't ever have an edict, so we don't need to add them to
			// the entity list in that case.
			if (Edict() != null) {
				gEntList.AddNetworkableEntity(this, EntIndex());

				// Cache our IServerNetworkable pointer for the engine for fast access.
				Edict()?.Networkable = NetworkProp();
			}
		}

		CheckHasThinkFunction(false);
		CheckHasGamePhysicsSimulation();
	}

	public virtual IServerVehicle? GetServerVehicle() => null;
	public ICollideable? GetCollideable() {
		return Collision;
	}

	public virtual ReadOnlySpan<char> GetClassname() {
		return Classname;
	}

	public static readonly DataMap DataDesc = new(typeof(BaseEntity), [
		DEFINE.KEYFIELD(nameof(Classname), FieldType.String, "classname"),
		DEFINE.GLOBAL_KEYFIELD(nameof(GlobalName), FieldType.String, "globalname"),
		DEFINE.KEYFIELD(nameof(ParentName), FieldType.String, "parentname"),
		DEFINE.KEYFIELD(nameof(HammerID), FieldType.Integer, "hammerid"),
		DEFINE.KEYFIELD(nameof(Speed), FieldType.Float, "speed"),
		DEFINE.KEYFIELD(nameof(RenderFX), FieldType.Character, "renderfx"),
		DEFINE.KEYFIELD(nameof(RenderMode), FieldType.Character, "rendermode"),
		DEFINE.FIELD(nameof(PrevAnimTime), FieldType.Time),
		DEFINE.FIELD(nameof(AnimTime), FieldType.Time),
		DEFINE.FIELD(nameof(SimulationTime), FieldType.Time),
		DEFINE.FIELD(nameof(LastThinkTick), FieldType.Tick),
		DEFINE.KEYFIELD(nameof(NextThinkTick), FieldType.Tick, "nextthink"),
		DEFINE.KEYFIELD(nameof(Effects), FieldType.Integer, "effects"),
		DEFINE.KEYFIELD(nameof(ColorRender), FieldType.Color32, "rendercolor"),
		DEFINE.GLOBAL_KEYFIELD(nameof(ModelIndex), FieldType.Short, "modelindex"),
		// DEFINE.FIELD(nameof(TouchStamp), FieldType.Integer),
		// DEFINE_CUSTOM_FIELD( m_aThinkFunctions, thinkcontextFuncs ),
		// DEFINE_UTLVECTOR(m_ResponseContexts, FIELD_EMBEDDED),
		// DEFINE.KEYFIELD(nameof(ResponseContext), FieldType.String, "ResponseContext"),
		// DEFINE.FIELD(nameof(PfnThink), FieldType.Function),
		// DEFINE.FIELD(nameof(PfnTouch), FieldType.Function),
		// DEFINE.FIELD(nameof(PfnUse), FieldType.Function),
		// DEFINE.FIELD(nameof(PfnBlocked), FieldType.Function),
		// DEFINE.FIELD(nameof(PfnMoveDone), FieldType.Function),
		DEFINE.FIELD(nameof(LifeState), FieldType.Character),
		// DEFINE.FIELD(nameof(TakeDamage), FieldType.Character),
		DEFINE.KEYFIELD(nameof(__nv_MaxHealth), FieldType.Integer, "max_health"),
		DEFINE.KEYFIELD(nameof(Health), FieldType.Integer, "health"),
		DEFINE.KEYFIELD(nameof(Target), FieldType.String, "target"),
		// DEFINE.KEYFIELD(nameof(DamageFilterName), FieldType.String, "damagefilter"),
		DEFINE.FIELD(nameof(DamageFilter), FieldType.EHandle),
		// DEFINE.FIELD(nameof(DebugOverlays), FieldType.Integer),
		DEFINE.GLOBAL_FIELD(nameof(Parent), FieldType.EHandle),
		DEFINE.FIELD(nameof(ParentAttachment), FieldType.Character),
		DEFINE.GLOBAL_FIELD(nameof(MoveParent), FieldType.EHandle),
		DEFINE.GLOBAL_FIELD(nameof(MoveChild), FieldType.EHandle),
		DEFINE.GLOBAL_FIELD(nameof(MovePeer), FieldType.EHandle),
		DEFINE.FIELD(nameof(eflags), FieldType.Integer),
		DEFINE.FIELD(nameof(Name), FieldType.String),
		DEFINE.EMBEDDED(nameof(Collision)),
		DEFINE.EMBEDDED(nameof(Network)),
		DEFINE.FIELD(nameof(MoveType), FieldType.Character),
		DEFINE.FIELD(nameof(MoveCollide), FieldType.Character),
		DEFINE.FIELD(nameof(OwnerEntity), FieldType.EHandle),
		DEFINE.FIELD(nameof(CollisionGroup), FieldType.Integer),
		// DEFINE_PHYSPTR( m_pPhysicsObject),
		DEFINE.FIELD(nameof(Elasticity), FieldType.Float),
		DEFINE.KEYFIELD(nameof(__nv_ShadowCastDistance), FieldType.Float, "shadowcastdist"),
		// DEFINE.FIELD(nameof(DesiredShadowCastDistance), FieldType.Float),
		// DEFINE.INPUT(nameof(InitialTeamNum), FieldType.Integer, "TeamNum"),
		DEFINE.FIELD(nameof(TeamNum), FieldType.Integer),
		DEFINE.FIELD(nameof(GroundEntity), FieldType.EHandle),
		// DEFINE.FIELD(nameof(GroundChangeTime), FieldType.Time),
		DEFINE.GLOBAL_KEYFIELD(nameof(ModelName), FieldType.ModelName, "model"),
		DEFINE.KEYFIELD(nameof(BaseVelocity), FieldType.Vector, "basevelocity"),
		DEFINE.FIELD(nameof(AbsVelocity), FieldType.Vector),
		DEFINE.KEYFIELD(nameof(AngVelocity), FieldType.Vector, "avelocity"),
		// DEFINE.ARRAY(nameof(CoordinateFrame), FieldType.Float, 12),
		DEFINE.KEYFIELD(nameof(WaterLevel), FieldType.Character, "waterlevel"),
		DEFINE.FIELD(nameof(WaterType), FieldType.Character),
		DEFINE.FIELD(nameof(Blocker), FieldType.EHandle),
		DEFINE.KEYFIELD(nameof(Gravity), FieldType.Float, "gravity"),
		DEFINE.KEYFIELD(nameof(Friction), FieldType.Float, "friction"),
		DEFINE.KEYFIELD(nameof(LocalTime), FieldType.Float, "ltime"),
		DEFINE.FIELD(nameof(VPhysicsUpdateLocalTime), FieldType.Float),
		DEFINE.FIELD(nameof(MoveDoneTime), FieldType.Float),
		DEFINE.FIELD(nameof(AbsOrigin), FieldType.PositionVector),
		DEFINE.KEYFIELD(nameof(Velocity), FieldType.Vector, "velocity"),
		DEFINE.KEYFIELD(nameof(TextureFrameIndex), FieldType.Character, "texframeindex"),
		DEFINE.FIELD(nameof(SimulatedEveryTick), FieldType.Boolean),
		DEFINE.FIELD(nameof(AnimatedEveryTick), FieldType.Boolean),
		// DEFINE.FIELD(nameof(AlternateSorting), FieldType.Boolean),
		DEFINE.KEYFIELD(nameof(SpawnFlags), FieldType.Integer, "spawnflags"),
		DEFINE.FIELD(nameof(TransmitStateOwnedCounter), FieldType.Character),
		DEFINE.FIELD(nameof(AbsRotation), FieldType.Vector),
		DEFINE.FIELD(nameof(__nv_Origin), FieldType.Vector),
		DEFINE.FIELD(nameof(__nv_Rotation), FieldType.Vector),
		DEFINE.KEYFIELD(nameof(ViewOffset), FieldType.Vector, "view_ofs"),
		DEFINE.FIELD(nameof(flags), FieldType.Integer),
		DEFINE.FIELD(nameof(SimulationTick), FieldType.Tick),
		// DEFINE.FIELD(nameof(NavIgnoreUntilTime), FieldType.Time),
		// DEFINE.INPUTFUNC(FieldType.Integer, "SetTeam", nameof(InputSetTeam), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputSetTeam(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Kill", nameof(InputKill), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputKill(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "KillHierarchy", nameof(InputKillHierarchy), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputKillHierarchy(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Use", nameof(InputUse), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputUse(data))),
		DEFINE.INPUTFUNC(FieldType.Integer, "Alpha", nameof(InputAlpha), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputAlpha(data))),
		// DEFINE.INPUTFUNC(FieldType.Boolean, "AlternativeSorting", nameof(InputAlternativeSorting), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputAlternativeSorting(data))),
		DEFINE.INPUTFUNC(FieldType.Color32, "Color", nameof(InputColor), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputColor(data))),
		DEFINE.INPUTFUNC(FieldType.String, "SetParent", nameof(InputSetParent), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputSetParent(data))),
		// DEFINE.INPUTFUNC(FieldType.String, "SetParentAttachment", nameof(InputSetParentAttachment), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputSetParentAttachment(data))),
		// DEFINE.INPUTFUNC(FieldType.String, "SetParentAttachmentMaintainOffset", nameof(InputSetParentAttachmentMaintainOffset), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputSetParentAttachmentMaintainOffset(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "ClearParent", nameof(InputClearParent), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputClearParent(data))),
		// DEFINE.INPUTFUNC(FieldType.String, "SetDamageFilter", nameof(InputSetDamageFilter), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputSetDamageFilter(data))),
		// DEFINE.INPUTFUNC(FieldType.Void, "EnableDamageForces", nameof(InputEnableDamageForces), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputEnableDamageForces(data))),
		// DEFINE.INPUTFUNC(FieldType.Void, "DisableDamageForces", nameof(InputDisableDamageForces), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputDisableDamageForces(data))),
		// DEFINE.INPUTFUNC(FieldType.String, "DispatchEffect", nameof(InputDispatchEffect), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputDispatchEffect(data))),
		// DEFINE.INPUTFUNC(FieldType.String, "DispatchResponse", nameof(InputDispatchResponse), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputDispatchResponse(data))),
		// DEFINE.INPUTFUNC(FieldType.String, "AddContext", nameof(InputAddContext), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputAddContext(data))),
		// DEFINE.INPUTFUNC(FieldType.String, "RemoveContext", nameof(InputRemoveContext), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputRemoveContext(data))),
		// DEFINE.INPUTFUNC(FieldType.String, "ClearContext", nameof(InputClearContext), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputClearContext(data))),
		// DEFINE.INPUTFUNC(FieldType.Void, "DisableShadow", nameof(InputDisableShadow), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputDisableShadow(data))),
		// DEFINE.INPUTFUNC(FieldType.Void, "EnableShadow", nameof(InputEnableShadow), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputEnableShadow(data))),
		// DEFINE.INPUTFUNC(FieldType.String, "AddOutput", nameof(InputAddOutput), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputAddOutput(data))),
		DEFINE.INPUTFUNC(FieldType.String, "FireUser1", nameof(InputFireUser1), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputFireUser1(data))),
		DEFINE.INPUTFUNC(FieldType.String, "FireUser2", nameof(InputFireUser2), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputFireUser2(data))),
		DEFINE.INPUTFUNC(FieldType.String, "FireUser3", nameof(InputFireUser3), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputFireUser3(data))),
		DEFINE.INPUTFUNC(FieldType.String, "FireUser4", nameof(InputFireUser4), (INPUTFUNCPTR)((self, data) => ((BaseEntity)self).InputFireUser4(data))),
		DEFINE.OUTPUT(nameof(OnUser1), "OnUser1", eventFuncs),
		DEFINE.OUTPUT(nameof(OnUser2), "OnUser2", eventFuncs),
		DEFINE.OUTPUT(nameof(OnUser3), "OnUser3", eventFuncs),
		DEFINE.OUTPUT(nameof(OnUser4), "OnUser4", eventFuncs),
		DEFINE.FUNCTION(nameof(SUB_Remove)),
		DEFINE.FUNCTION(nameof(SUB_DoNothing)),
		// DEFINE_FUNCTION( SUB_StartFadeOut ),
		// DEFINE_FUNCTION( SUB_StartFadeOutInstant ),
		// DEFINE_FUNCTION( SUB_FadeOut ),
		// DEFINE_FUNCTION( SUB_Vanish ),
		DEFINE.FUNCTION(nameof(SUB_CallUseToggle)),
		// DEFINE_THINKFUNC( ShadowCastDistThink ),
		DEFINE.FIELD(nameof(EffectEntity), FieldType.EHandle),
		// DEFINE_ARRAY( m_nModelIndexOverrides, FIELD_INTEGER, MAX_VISION_MODES ),
	]);
	public virtual DataMap? GetDataDescMap() => DataDesc;

	public OutputEvent OnUser1 = new();
	public OutputEvent OnUser2 = new();
	public OutputEvent OnUser3 = new();
	public OutputEvent OnUser4 = new();

	public void InputAlpha(InputData inputdata) => SetRenderColorA((byte)Math.Clamp(inputdata.Value.Int(), 0, 255));

	public void InputColor(InputData inputdata) {
		Source.Color clr = inputdata.Value.Color32();
		SetRenderColor(clr.R, clr.G, clr.B);
	}

	public void InputUse(InputData inputdata) => Use(inputdata.Activator, inputdata.Caller, (UseType)inputdata.OutputID, 0);

	public void InputKill(InputData inputdata) {
		BaseEntity? owner = GetOwnerEntity();
		if (owner != null) {
			// owner.DeathNotice(this);
			SetOwnerEntity(null);
		}

		Util.Remove(this);
	}

	public void InputKillHierarchy(InputData inputdata) {
		BaseEntity? child, next;
		for (child = FirstMoveChild(); child != null; child = next) {
			next = child.NextMovePeer();
			child.InputKillHierarchy(inputdata);
		}

		BaseEntity? owner = GetOwnerEntity();
		if (owner != null) {
			// owner.DeathNotice(this);
			SetOwnerEntity(null);
		}

		Util.Remove(this);
	}

	public void InputSetParent(InputData inputdata) {
		if (ParentAttachment != 0)
			ParentAttachment = 0;

		SetParent(inputdata.Value.StringID(), inputdata.Activator!);
	}

	public void InputClearParent(InputData inputdata) => SetParent(null);
	public void InputFireUser1(InputData inputdata) => OnUser1.FireOutput(inputdata.Activator, this);
	public void InputFireUser2(InputData inputdata) => OnUser2.FireOutput(inputdata.Activator, this);
	public void InputFireUser3(InputData inputdata) => OnUser3.FireOutput(inputdata.Activator, this);
	public void InputFireUser4(InputData inputdata) => OnUser4.FireOutput(inputdata.Activator, this);

	public virtual bool AcceptInput(ReadOnlySpan<char> inputName, BaseEntity? activator, BaseEntity? caller, Variant_t value, int outputID) {
		// if (ent_messages_draw.GetBool()) todo

		for (DataMap? dmap = GetDataDescMap(); dmap != null; dmap = dmap.BaseMap) {
			for (int i = 0; i < dmap.DataNumFields; i++) {
				TypeDescription desc = dmap.DataDesc[i];
				if ((desc.Flags & FieldTypeDescFlags.Input) == 0)
					continue;

				if (stricmp(desc.ExternalName, inputName) != 0)
					continue;

				if (caller != null)
					DevMsg(2, $"({gpGlobals.CurTime:F2}) input {caller.GetEntityName()}: {GetDebugName()}.{inputName}({value.ToString()})\n");
				else
					DevMsg(2, $"({gpGlobals.CurTime:F2}) input <NULL>: {GetDebugName()}.{inputName}({value.ToString()})\n");

				// if ((DebugOverlays & OVERLAY_MESSAGE_BIT) != 0)
				// 	DrawInputOverlay(inputName, caller, value);

				if (value.FieldType() != desc.FieldType)
					if (!(value.FieldType() == Source.Common.FieldType.Void && desc.FieldType == Source.Common.FieldType.String))
						if (!value.Convert(desc.FieldType)) {
							Warning($"!! ERROR: bad input/output link:\n!! {GetClassname()}({GetDebugName()},{inputName}) doesn't match type from {(caller != null ? caller.GetClassname() : "<null>")}({(caller != null ? caller.GetEntityName() : "<null>")})\n");
							return false;
						}

				if (desc.InputFunc is INPUTFUNCPTR pfnInput) {
					InputData data = new() {
						Activator = activator,
						Caller = caller,
						Value = value,
						OutputID = outputID
					};

					pfnInput(this, data);
				}
				else if ((desc.Flags & FieldTypeDescFlags.Key) != 0) {
					value.SetOther(desc.Accessor, this);
					NetworkStateChanged();
				}

				return true;
			}
		}

		DevMsg(2, $"unhandled input: ({inputName}) . ({GetClassname()},{GetDebugName()})\n");
		return false;
	}
	public virtual void Spawn() { }
	public virtual void Activate() { }
	public virtual void Precache() {

	}

	static bool _AllowPrecache;
	public static bool IsPrecacheAllowed() => _AllowPrecache;
	public static bool SetAllowPrecache(bool allow) => _AllowPrecache = allow;

	public DebugOverlayBits DebugOverlays;
	public static bool InDebugSelect = false;
	public static int DebugPlayer = -1;
	static bool DebugPause = false;
	static int DebugSteps = 1;

	public static void Debug_Pause(bool pause) => DebugPause = pause;
	public static bool Debug_IsPaused() => DebugPause;
	public static void Debug_SetSteps(int steps) => DebugSteps = steps;
	public static bool Debug_Step() {
		if (DebugSteps > 0)
			DebugSteps--;

		return DebugSteps > 0;
	}

	public static bool Debug_ShouldStep() => !DebugPause || DebugSteps > 0;

	public bool ReadKeyField(ReadOnlySpan<char> varName, ref Variant_t var) {
		if (varName.IsEmpty)
			return false;

		for (DataMap? dmap = GetDataDescMap(); dmap != null; dmap = dmap.BaseMap)
			for (int i = 0; i < dmap.DataNumFields; i++)
				if ((dmap.DataDesc[i].Flags & (FieldTypeDescFlags.Output | FieldTypeDescFlags.Key)) != 0)
					if (FStrEq(dmap.DataDesc[i].ExternalName, varName)) {
						var.Set(dmap.DataDesc[i].FieldType, dmap.DataDesc[i].Accessor, this);
						return true;
					}

		return false;
	}

	public static int PrecacheModel(ReadOnlySpan<char> name, bool preload = true) {
		if (name.IsStringEmpty)
			return -1;

		if (!BaseEntity.IsPrecacheAllowed())
			if (!engine.IsModelPrecached(name))
				DevMsg($"Late precache of {name} -- not necessarily a bug now that we allow ~everything to be dynamically loaded.\n");

		int idx = engine.PrecacheModel(name, preload);
		if (idx != -1)
			PrecacheModelComponents(idx);

		return idx;
	}

	public static void PrecacheModelComponents(int modelIndex) {
		Model? model = (Model?)modelinfo.GetModel(modelIndex);
		if (model != null || modelinfo.GetModelType(model) != ModelType.Studio)
			return;

		// sounds
		if (IsPC()) {
			ReadOnlySpan<char> name = modelinfo.GetModelName(model);
			if (!g_ModelSoundsCache.TryGetValue(name.Hash(), out _)) {
				ReadOnlySpan<char> extension = Path.GetExtension(name);

				if (!stristr(extension, "mdl").IsEmpty)
					DevMsg(2, $"Late precache of {name}, need to rebuild modelsounds.cache\n");
				else {
					if (extension[0] == '\0')
						Warning($"Precache of {name} ambigious (no extension specified)\n");
					else
						Warning($"Late precache of {name} (file missing?)\n");
					return;
				}
			}

			if (g_ModelSoundsCache.TryGetValue(name.Hash(), out ModelSoundsCache? entry))
				entry.PrecacheSoundList();
		}
	}

	static readonly Dictionary<UtlSymId_t, ModelSoundsCache> g_ModelSoundsCache = [];

	public bool HasSpawnFlags(int flags) => (SpawnFlags & flags) != 0;

	public int GetModelIndex() => ModelIndex;

	/// <summary>
	/// The equiv of the dtor (kinda...)
	/// see CBaseEntity::~CBaseEntity() etc
	/// </summary>
	public virtual void Term() {
		VPhysicsDestroyObject();

		// Need to remove references to this entity before EHANDLES go null
		{
			Util.g_bDisableEhandleAccess = false;
			BaseEntity.PhysicsRemoveTouchedList(this);
			BaseEntity.PhysicsRemoveGroundList(this);
			SetGroundEntity(null); // remove us from the ground entity if we are on it
			DestroyAllDataObjects();
			Util.g_bDisableEhandleAccess = true;

			// Remove this entity from the ent list (NOTE:  This Makes EHANDLES go NULL)
			gEntList.RemoveEntity(GetRefEHandle());
		}

		LuaEntityObject?.UnReference();
		LuaEntityObject = null;
		LuaTableObject?.UnReference();
		LuaTableObject = null;
		LuaCalcAbsolutePosition.UnReference();

		CollisionProp().DestroyPartitionHandle();

		NetworkProp().Term();
	}

	public ReadOnlySpan<char> GetModelName() => ModelName;
	public void SetModelName(ReadOnlySpan<char> modelName) {
		ModelName = new(modelName.SliceNullTerminatedString());
		DispatchUpdateTransmitState();
	}

	public IServerNetworkable? GetNetworkable() => Network;

	public ref readonly BaseHandle GetRefEHandle() => ref RefEHandle;

	public bool DynamicModelAllowed;
	public bool DynamicModelSetBounds;
	public bool DynamicModelPending;

	static readonly IFieldAccessor ModelIndexField = FIELD.OF(nameof(ModelIndex));
	public void SetModelIndex(int index) {
		if (IVModelInfo.IsDynamicModelIndex(index) && !(GetBaseAnimating() != null && DynamicModelAllowed)) {
			AssertMsg(false, "dynamic model support not enabled on server entity");
			index = -1;
		}

		if (index != ModelIndex) {
			if (DynamicModelPending) {
				// sg_DynamicLoadHandlers.Remove(this);
				throw new NotImplementedException("sg_DynamicLoadHandlers.Remove(this)");
			}

			modelinfo.ReleaseDynamicModel(ModelIndex);
			modelinfo.AddRefDynamicModel(index);
			ModelIndex = index;
			NetworkStateChanged(ModelIndexField);

			DynamicModelSetBounds = false;

			if (IVModelInfo.IsDynamicModelIndex(index)) {
				DynamicModelPending = true;
				throw new NotImplementedException("sg_DynamicLoadHandlers[sg_DynamicLoadHandlers.Insert(this)].Register(index)");
				// sg_DynamicLoadHandlers[sg_DynamicLoadHandlers.Insert(this)].Register(index);
			}
			else {
				DynamicModelPending = false;
				OnNewModel();
			}
		}
		DispatchUpdateTransmitState();
	}

	public void OnModelLoadComplete(Model model) {
		Assert(DynamicModelPending && IVModelInfo.IsDynamicModelIndex(ModelIndex));
		Assert(model == modelinfo.GetModel(ModelIndex));

		DynamicModelPending = false;

		if (DynamicModelSetBounds) {
			DynamicModelSetBounds = false;
			SetCollisionBoundsFromModel();
		}

		OnNewModel();
	}

	public Model? GetModel() => modelinfo.GetModel(GetModelIndex());

	public void SetCollisionBoundsFromModel() {
		if (IsDynamicModelLoading()) {
			DynamicModelSetBounds = true;
			return;
		}

		Model? model = GetModel();
		if (model != null) {
			modelinfo.GetModelBounds(model, out Vector3 mns, out Vector3 mxs);
			Util.SetSize(this, mns, mxs);
		}
	}

	protected void EnableDynamicModels() => DynamicModelAllowed = true;

	public bool IsDynamicModelLoading() => DynamicModelPending;

	protected virtual StudioHdr? OnNewModel() {
		return null;
	}


	BaseHandle RefEHandle = new();
	public void SetRefEHandle(in BaseHandle handle) {
		RefEHandle = handle;
		Edict? edict = NetworkProp()?.Edict();
		if (edict != null)
			edict.NetworkSerialNumber = (short)(RefEHandle.GetSerialNumber() & ((1 << Constants.NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS) - 1));
	}

	[NetworkName("m_fFlags")]
	protected int flags;
	EFL eflags;
	public Matrix3x4 CoordinateFrame;


	public ref readonly Vector3 GetBaseVelocity() => ref BaseVelocity;
	public void SetBaseVelocity(in Vector3 v) => BaseVelocity = v;

	public void SetAbsOrigin(Vector3 vector3) {
		AssertMsg(vector3.IsValid(), "Invalid origin set");

		if (AbsOrigin == vector3)
			return;

		InvalidatePhysicsRecursive(InvalidatePhysicsBits.PositionChanged);
		RemoveEFlags(EFL.DirtyAbsVelocity);

		AbsOrigin = vector3;

		MathLib.MatrixSetColumn(in vector3, 3, ref CoordinateFrame);

		Vector3 newOrigin;
		BaseEntity? moveParent = GetMoveParent();
		if (moveParent == null)
			newOrigin = vector3;
		else {
			MathLib.ConcatTransforms(moveParent.EntityToWorldTransform(), CoordinateFrame, out Matrix3x4 tempMat);
			MathLib.VectorTransform(in vector3, in tempMat, out newOrigin);
		}

		if (Origin != newOrigin) {
			Origin = newOrigin;
			SetSimulationTime(gpGlobals.CurTime);
		}
	}

	static bool s_bAbsQueriesValid = true;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void SetAbsQueriesValid(bool valid) => s_bAbsQueriesValid = valid;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsAbsQueriesValid() => s_bAbsQueriesValid;

	public ref readonly Vector3 GetAbsOrigin() {
		Assert(BaseEntity.IsAbsQueriesValid());

		if (IsEFlagSet(EFL.DirtyAbsTransform))
			this.CalcAbsolutePosition();

		return ref AbsOrigin;
	}
	public ref readonly Vector3 GetViewOffset() => ref ViewOffset;
	public ref readonly QAngle GetAbsAngles() {
		Assert(BaseEntity.IsAbsQueriesValid());

		if (IsEFlagSet(EFL.DirtyAbsTransform))
			this.CalcAbsolutePosition();

		return ref AbsRotation;
	}

	public void SetLocalOrigin(in Vector3 origin) {
		if (!IsEntityPositionReasonable(origin)) {
			if (CheckEmitReasonablePhysicsSpew())
				Warning($"Bad SetLocalOrigin({origin.X},{origin.Y},{origin.Z}) on {GetDebugName()}\n");
			Assert(false);
			return;
		}

		if (Origin != origin) {
			InvalidatePhysicsRecursive(InvalidatePhysicsBits.PositionChanged);
			Origin = origin;
			SetSimulationTime(gpGlobals.CurTime);
		}
	}

	public void SetLocalAngles(in QAngle angles) {
		if (!IsEntityQAngleReasonable(angles)) {
			if (CheckEmitReasonablePhysicsSpew())
				Warning($"Bad SetLocalAngles({angles.X},{angles.Y},{angles.Z}) on {GetDebugName()}\n");
			AssertMsg(false, $"Bad SetLocalAngles({angles.X},{angles.Y},{angles.Z}) on {GetDebugName()}\n");
			return;
		}

		if (Rotation != angles) {
			InvalidatePhysicsRecursive(InvalidatePhysicsBits.AnglesChanged);
			Rotation = angles;
			SetSimulationTime(gpGlobals.CurTime);
		}
	}

	public ref Matrix3x4 GetParentToWorldTransform(ref Matrix3x4 tempMatrix) {
		BaseEntity? moveParent = GetMoveParent();
		if (moveParent == null) {
			Assert(false);
			MathLib.SetIdentityMatrix(out tempMatrix);
			return ref tempMatrix;
		}

		if (ParentAttachment != 0) {
			BaseAnimating? animating = moveParent.GetBaseAnimating();
			if (animating != null && animating.GetAttachment(ParentAttachment, out tempMatrix))
				return ref tempMatrix;
		}

		// If we fall through to here, then just use the move parent's abs origin and angles.
		return ref moveParent.EntityToWorldTransform();
	}
	public ref Matrix3x4 EntityToWorldTransform() {
		// Assert()

		if (IsEFlagSet(EFL.DirtyAbsTransform))
			CalcAbsolutePosition();

		return ref CoordinateFrame;
	}

	readonly object CalcAbsolutePositionMutex = new();

	protected void CalcAbsolutePosition() {
		if (!IsEFlagSet(EFL.DirtyAbsTransform))
			return;

		{
#if !BUILD_GMOD
			lock (CalcAbsolutePositionMutex)
#endif
			{
				// Test again under the lock, in case another thread did the work in the interim
				if (!IsEFlagSet(EFL.DirtyAbsTransform)) {
					return;
				}

				// Plop the entity.parent matrix into m_rgflCoordinateFrame
				MathLib.AngleMatrix(Rotation, Origin, out CoordinateFrame);

				BaseEntity? moveParent = GetMoveParent();
				if (moveParent == null) {
					// no move parent, so just copy existing values
					AbsOrigin = Origin;
					AbsRotation = Rotation;
				}
				else {
					// concatenate with our parent's transform
					Matrix3x4 tmpMatrix, scratchSpace = default;
					MathLib.ConcatTransforms(GetParentToWorldTransform(ref scratchSpace), CoordinateFrame, out tmpMatrix);
					MathLib.MatrixCopy(tmpMatrix, out CoordinateFrame);

					// pull our absolute position out of the matrix
					MathLib.MatrixGetColumn(CoordinateFrame, 3, out AbsOrigin);

					// if we have any angles, we have to extract our absolute angles from our matrix
					if ((Rotation == vec3_angle) && (ParentAttachment == 0))
						// just copy our parent's absolute angles
						MathLib.VectorCopy(moveParent.GetAbsAngles(), out AbsRotation);
					else
						MathLib.MatrixAngles(CoordinateFrame, out AbsRotation);
				}

				RemoveEFlags(EFL.DirtyAbsTransform);
			}

			// Do this callback *after* we have updated the position, and (importantly) after we clear the dirty flag, because this callback can potentially
			// end up recursively calling back in here, so the dirty flag must be cleared to break the recursion in that case.
			if (HasDataObjectType(DataObjectType.PositionWatcher))
				ReportPositionChanged(this);
		}
	}

	public virtual void Use(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		if (FnUse != null)
			FnUse(activator, caller, useType, value);
		else
			Parent.Get()?.Use(activator, caller, useType, value);
	}

	public int ShouldToggle(UseType useType, int currentState) {
		if (useType != UseType.Toggle && useType != UseType.Set) {
			if ((currentState != 0 && useType == UseType.On) || (currentState == 0 && useType == UseType.Off))
				return 0;
		}
		return 1;
	}

	public string? Target;
	public BaseEntity? GetNextTarget() {
		if (Target == null)
			return null;
		return gEntList.FindEntityByName(null, Target);
	}

	public void TraceAttackToTriggers(in TakeDamageInfo info, in Vector3 start, in Vector3 end, in Vector3 dir) {
		Ray ray = default;
		ray.Init(start, end);

		TriggerTraceEnum triggerTraceEnum = new(ref ray, info, dir, Mask.Shot);
		enginetrace.EnumerateEntities(ray, true, ref triggerTraceEnum);
	}

	public virtual void Think() => FnThink?.Invoke(this);

	public virtual EntityCapabilities ObjectCaps() {
		Model? model = GetModel();
		bool isBrush = (model != null && modelinfo.GetModelType(model) == ModelType.Brush);

		// We inherit our parent's use capabilities so that we can forward use commands
		// to our parent.
		BaseEntity? parent = GetParent();
		if (parent != null) {
			EntityCapabilities caps = parent.ObjectCaps();

			if (!isBrush)
				caps &= (EntityCapabilities.AcrossTransition | EntityCapabilities.ImpulseUse | EntityCapabilities.ContinuousUse | EntityCapabilities.OnOffUse | EntityCapabilities.DirectionalUse);
			else
				caps &= (EntityCapabilities.ImpulseUse | EntityCapabilities.ContinuousUse | EntityCapabilities.OnOffUse | EntityCapabilities.DirectionalUse);

			if (parent.IsPlayer())
				caps |= EntityCapabilities.AcrossTransition;

			return caps;
		}
		else if (!isBrush)
			return EntityCapabilities.AcrossTransition;

		return 0;
	}

	public static bool sm_bDisableTouchFuncs = false;  // Disables PhysicsTouch and PhysicsStartTouch function calls
	public static bool sm_bAccurateTriggerBboxChecks = true;   // SOLID_BBOX entities do a fully accurate trigger vs bbox check when this is set
	public int TouchStamp;

	public void SetTouch(TOUCHPTR? func) => FnTouch = func;

	public void GetVelocity(out Vector3 velocity, out Vector3 angVelocity) {
		if (GetMoveType() == Source.MoveType.VPhysics && PhysicsObject != null)
			PhysicsObject.GetVelocity(out velocity, out angVelocity);
		else {
			velocity = GetAbsVelocity();
			QAngle tmp = GetLocalAngularVelocity();
			angVelocity = new(tmp.Z, tmp.X, tmp.Y);
		}
	}

	public bool Intersects(BaseEntity other) {
		if (Edict() == null || other.Edict() == null)
			return false;

		CollisionProperty myProp = CollisionProp();
		CollisionProperty otherProp = other.CollisionProp();

		return CollisionUtils.IsOBBIntersectingOBB(
			myProp.GetCollisionOrigin(), myProp.GetCollisionAngles(), myProp.OBBMins(), myProp.OBBMaxs(),
			otherProp.GetCollisionOrigin(), otherProp.GetCollisionAngles(), otherProp.OBBMins(), otherProp.OBBMaxs());
	}

	public virtual bool IsMoving() {
		GetVelocity(out Vector3 velocity, out _);
		return velocity != vec3_origin;
	}

	public virtual Vector3 GetSmoothedVelocity() {
		GetVelocity(out Vector3 vel, out _);
		return vel;
	}

	public virtual void PostClientActive() { }

	public void AddSpawnFlags(int flags) => SpawnFlags |= flags;
	public void RemoveSpawnFlags(int flags) => SpawnFlags &= ~flags;
	public void SetFriction(float friction) => Friction = friction;

	public TimeUnit_t GetNextThink(ReadOnlySpan<char> context = default) {
		long tick = GetNextThinkTick(context);
		if (tick == TICK_NEVER_THINK)
			return TICK_NEVER_THINK;

		return TICK_INTERVAL * tick;
	}

	public void IncrementInterpolationFrame() => InterpolationFrame = (byte)((InterpolationFrame + 1) % NOINTERP_PARITY_MAX);

	public void SUB_Remove() {
		if (Health > 0) {
			// this situation can screw up NPCs who can't tell their entity pointers are invalid.
			Health = 0;
			DevWarning(2, "SUB_Remove called on entity with health > 0\n");
		}

		Util.Remove(this);
	}

	struct TeleportListEntry
	{
		public BaseEntity Entity;
		public Vector3 PrevAbsOrigin;
		public QAngle PrevAbsAngles;
	}

	static void TeleportEntity(BaseEntity sourceEntity, ref TeleportListEntry entry, Vector3? newPosition, QAngle? newAngles, Vector3? newVelocity) {
		BaseEntity teleport = entry.Entity;

		SolidFlags solidFlags = (SolidFlags)teleport.GetSolidFlags();
		teleport.AddSolidFlags(SolidFlags.NotSolid);

		// I'm teleporting myself
		if (sourceEntity == teleport) {
			if (newAngles.HasValue) {
				teleport.SetLocalAngles(newAngles.Value);
				if (teleport.IsPlayer()) {
					BasePlayer player = (BasePlayer)teleport;
					player.SnapEyeAngles(newAngles.Value);
				}
			}

			if (newVelocity.HasValue) {
				teleport.SetAbsVelocity(newVelocity.Value);
				teleport.SetBaseVelocity(vec3_origin);
			}

			if (newPosition.HasValue) {
				teleport.IncrementInterpolationFrame();
				Util.SetOrigin(teleport, newPosition.Value);
			}
		}
		else {
			// My parent is teleporting, just update my position & physics
			teleport.CalcAbsolutePosition();
		}
		IPhysicsObject? phys = teleport.VPhysicsGetObject();

		// handle physics objects / shadows
		if (phys != null) {
			if (newVelocity.HasValue) {
				phys.GetVelocity(out _, out Vector3 angVelocity);
				phys.SetVelocity(newVelocity.Value, angVelocity);
			}
			QAngle rotAngles = teleport.GetAbsAngles();
			// don't rotate physics on players or bbox entities
			if (teleport.IsPlayer() || teleport.GetSolid() == SolidType.BBox)
				rotAngles = vec3_angle;

			phys.SetPosition(teleport.GetAbsOrigin(), rotAngles, true);
		}

		teleport.SetSolidFlags(solidFlags);
	}

	//-----------------------------------------------------------------------------
	// Purpose: Recurses an entity hierarchy and fills out a list of all entities
	//			in the hierarchy with their current origins and angles.
	//
	//			This list is necessary to keep lazy updates of abs origins and angles
	//			from messing up our child/constrained entity fixup.
	//-----------------------------------------------------------------------------
	static void BuildTeleportList_r(BaseEntity teleport, List<TeleportListEntry> teleportList) {
		TeleportListEntry entry;

		entry.Entity = teleport;
		entry.PrevAbsOrigin = teleport.GetAbsOrigin();
		entry.PrevAbsAngles = teleport.GetAbsAngles();

		teleportList.Add(entry);

		BaseEntity? list = teleport.FirstMoveChild();
		while (list != null) {
			BuildTeleportList_r(list, teleportList);
			list = list.NextMovePeer();
		}
	}

	static readonly List<BaseEntity> g_TeleportStack = [];
	public virtual void Teleport(Vector3? newPosition, QAngle? newAngles, Vector3? newVelocity) {
		if (g_TeleportStack.Contains(this))
			return;
		g_TeleportStack.Add(this);

		List<TeleportListEntry> teleportList = [];
		BuildTeleportList_r(this, teleportList);

		int i;
		for (i = 0; i < teleportList.Count; i++) {
			TeleportListEntry entry = teleportList[i];
			TeleportEntity(this, ref entry, newPosition, newAngles, newVelocity);
		}

		for (i = 0; i < teleportList.Count; i++)
			teleportList[i].Entity.CollisionRulesChanged();

		if (IsPlayer()) {
			// Tell the client being teleported
			IGameEvent? ev = gameeventmanager.CreateEvent("base_player_teleported");
			if (ev != null) {
				ev.SetInt("entindex", EntIndex());
				gameeventmanager.FireEventClientSide(ev);
			}
		}

		g_TeleportStack.Remove(this);
	}
	public bool IsWorld() => EntIndex() == 0;

	public virtual bool CanBeHitByMeleeAttack(BaseEntity? attacker) => true;

	public virtual bool FVisible(BaseEntity entity, Mask traceMask = Mask.BlockLOS) {
		if ((entity.GetFlags() & EntityFlags.NoTarget) != 0)
			return false;

		Vector3 vecLookerOrigin = EyePosition();
		Vector3 vecTargetOrigin = entity.EyePosition();

		Trace tr;
		if (AI_BaseNPCGlobals.ai_LOS_mode.GetBool())
			Util.TraceLine(vecLookerOrigin, vecTargetOrigin, traceMask, this, Source.CollisionGroup.None, out tr);
		else {
			if (traceMask == Mask.BlockLOS)
				traceMask = Mask.BlockLOSAndNPCs;

			if (IsPlayer())
				traceMask &= ~(Mask)Contents.BlockLOS;

			TraceFilterLOS traceFilter = new(this, Source.CollisionGroup.None, entity);
			Util.TraceLine(vecLookerOrigin, vecTargetOrigin, traceMask, ref traceFilter, out tr);
		}

		if (tr.Fraction != 1.0 || tr.StartSolid) {
			if (tr.Ent == entity)
				return true;

			if (entity.IsPlayer()) {
				BasePlayer player = (BasePlayer)entity;
				if (tr.Ent == player.GetVehicleEntity())
					return true;
			}

			return false;
		}

		return true;
	}

	public virtual void GetVectors(out Vector3 forward, out Vector3 right, out Vector3 up) {
		ref readonly Matrix3x4 entityToWorld = ref EntityToWorldTransform();

		MathLib.MatrixGetColumn(entityToWorld, 0, out forward);

		MathLib.MatrixGetColumn(entityToWorld, 1, out right);
		right *= -1.0f;

		MathLib.MatrixGetColumn(entityToWorld, 2, out up);
	}

	public TimeUnit_t NavIgnoreUntilTime;

	public bool IsNavIgnored() => gpGlobals.CurTime <= NavIgnoreUntilTime;

	public BasePlayer? AI_GetClosestPlayer() {
		Vector3 pos = GetAbsOrigin();
		float closestDistSqr = float.MaxValue;
		BasePlayer? closest = null;

		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player == null)
				continue;

			float distSqr = (player.GetAbsOrigin() - pos).LengthSquared();
			if (distSqr < closestDistSqr) {
				closestDistSqr = distSqr;
				closest = player;
			}
		}

		return closest;
	}

	public virtual void StartTouch(BaseEntity? other) {
		// notify parent
		GetMoveParent()?.StartTouch(other);
	}

	public virtual void Touch(BaseEntity? other) {
		FnTouch?.Invoke(other);

		// notify parent of touch
		GetMoveParent()?.Touch(other);
	}

	public virtual void EndTouch(BaseEntity? other) {
		// notify parent
		GetMoveParent()?.EndTouch(other);
	}

	public void SetCheckUntouch(bool check) {
		// Invalidate touchstamp
		if (check) {
			TouchStamp++;
			if (!IsEFlagSet(EFL.CheckUntouch)) {
				AddEFlags(EFL.CheckUntouch);
				EntityTouchManager.EntityTouch_Add(this);
			}
		}
		else
			RemoveEFlags(EFL.CheckUntouch);
	}

	public void PhysicsTouchTriggers(Vector3? prevAbsOrigin = null) {
		Edict? edict = Edict();
		if (edict != null && !IsWorld()) {
			Assert(CollisionProp() != null);
			bool isTriggerCheckSolids = IsSolidFlagSet(SolidFlags.Trigger);
			bool isSolidCheckTriggers = IsSolid() && !isTriggerCheckSolids;     // NOTE: Moving triggers (items, ammo etc) are not
																				// checked against other triggers to reduce the number of touchlinks created
			if (!(isSolidCheckTriggers || isTriggerCheckSolids))
				return;

			if (GetSolid() == SolidType.BSP) {
				if (GetModel() == null && GetModelName().IsEmpty) {
					Warning($"Inserted {GetClassname()} with no model\n");
					return;
				}
			}

			SetCheckUntouch(true);
			if (isSolidCheckTriggers)
				engine.SolidMoved(edict, CollisionProp(), prevAbsOrigin, sm_bAccurateTriggerBboxChecks);
			if (isTriggerCheckSolids)
				engine.TriggerMoved(edict, sm_bAccurateTriggerBboxChecks);
		}
	}
	public virtual void StartBlocked(BaseEntity? other) { }
	public virtual void Blocked(BaseEntity? other) { }
	public virtual void EndBlocked() { }

	private void ReportPositionChanged(BaseEntity baseEntity) {
		ref WatcherList list = ref baseEntity.GetDataObject<WatcherList>(DataObjectType.PositionWatcher);
		if (!Unsafe.IsNullRef(ref list))
			list.NotifyPositionChanged(baseEntity);
	}

	readonly ServerNetworkProperty Network = new();
	public ServerNetworkProperty NetworkProp() => Network;
	public int EntIndex() => Network.EntIndex();
	public float GetGravity() => Gravity;
	public void SetGravity(float gravity) => Gravity = gravity;
	public void ClearSolidFlags() => CollisionProp().ClearSolidFlags();
	public object? GetBaseEntity() => this;
	public virtual BaseAnimating? GetBaseAnimating() => null;

	public virtual void ComputeWorldSpaceSurroundingBox(out Vector3 vecMins, out Vector3 vecMaxs) {
		Assert(false);
		vecMins = default;
		vecMaxs = default;
	}
	private float GetFriction() => Friction;

	public int GetSoundSourceIndex() => EntIndex();

	public static void EmitSentenceByIndex<T>(in T filter, int entIndex, int channel, int sentenceIndex, float volume, Source.Common.Audio.SoundLevel soundlevel, int flags = 0, int pitch = PITCH_NORM, Vector3? origin = null, Vector3? direction = null, bool updatePositions = true, TimeUnit_t soundtime = 0.0f) where T : IRecipientFilter {
		List<Vector3> dummy = [];
		enginesound.EmitSentenceByIndex(filter, entIndex, channel, sentenceIndex, volume, soundlevel, (Source.Common.Audio.SoundFlags)flags, pitch, 0, origin, direction, dummy, updatePositions, soundtime);
	}

	public virtual void SetTransmit(CheckTransmitInfo info, bool always) {
		int entIndex = EntIndex();

		if (info.TransmitEdict.Get(entIndex) != 0)
			return;

		ServerNetworkProperty networkParent = NetworkProp().GetNetworkParent();

		info.TransmitEdict.Set(entIndex);

		if (always || networkParent != null)
			info.TransmitAlways.Set(entIndex);
		else
			Network.RecomputePVSInformation();

		if (networkParent != null) {
			BaseEntity? moveParent = networkParent.GetBaseEntity();
			moveParent!.SetTransmit(info, always);
		}
	}

	public virtual EdictFlags ShouldTransmit(CheckTransmitInfo info) {
		EdictFlags flags = DispatchUpdateTransmitState();

		if ((flags & EdictFlags.PVSCheck) != 0)
			return EdictFlags.PVSCheck;
		else if ((flags & EdictFlags.Always) != 0)
			return EdictFlags.Always;
		else if ((flags & EdictFlags.DontSend) != 0)
			return EdictFlags.DontSend;

		BaseEntity? recipientEntity = Instance(info.ClientEnt);
		Assert(recipientEntity != null && recipientEntity.IsPlayer());

		BasePlayer recipientPlayer = (BasePlayer)recipientEntity!;

		Team? team = recipientPlayer.GetTeam();
		if (team != null) {
			// 	if (team.ShouldTransmitToPlayer(recipientPlayer, this))
			// 		return EdictFlags.Always;
		}

		return EdictFlags.PVSCheck;
	}

	public virtual EdictFlags UpdateTransmitState() {
		Assert(g_InsideDispatchUpdateTransmitState > 0);

		if (IsEffectActive(EntityEffects.NoDraw) && MoveChild.Get() == null)
			return SetTransmitState(EdictFlags.DontSend);

		if (!IsEFlagSet(EFL.ForceCheckTransmit)) {
			if (GetModelIndex() == 0 || GetModelName().IsEmpty)
				return SetTransmitState(EdictFlags.DontSend);
		}

		if (GetModelIndex() == 1)
			return SetTransmitState(EdictFlags.Always);

		if (IsEFlagSet(EFL.InSkybox))
			return SetTransmitState(EdictFlags.Always);

		return SetTransmitState(EdictFlags.PVSCheck);
	}

	static int g_InsideDispatchUpdateTransmitState = 0;
	byte TransmitStateOwnedCounter = 0;
	public EdictFlags DispatchUpdateTransmitState() {
		Edict ed = Edict();

		if (TransmitStateOwnedCounter != 0)
			return ed != null ? ed.StateFlags : 0;

		g_InsideDispatchUpdateTransmitState++;
		EdictFlags ret = UpdateTransmitState();
		g_InsideDispatchUpdateTransmitState--;

		return ret;
	}

	public EdictFlags SetTransmitState(EdictFlags flag) {
		Edict ed = Edict();

		if (ed == null)
			return 0;

		ed.ClearTransmitState();

		EdictFlags old = ed.StateFlags;
		ed.StateFlags |= flag;

		if ((old & EdictFlags.DontSend) != (ed.StateFlags & EdictFlags.DontSend))
			engine.NotifyEdictFlagsChange(EntIndex());

		return ed.StateFlags;
	}
}

[LinkEntityToClass("info_player_start")]
[LinkEntityToClass("info_landmark")]
[LinkEntityToClass("info_player_combine")]
[LinkEntityToClass("info_player_rebel")]
[LinkEntityToClass("info_target_helicopter_crash")]
[LinkEntityToClass("info_teleport_destination")]
[LinkEntityToClass("logic_proximity")]
public class PointEntity : BaseEntity
{
	public override void Spawn() {
		SetSolid(Source.SolidType.None);
	}

	public override EntityCapabilities ObjectCaps() => base.ObjectCaps() & ~EntityCapabilities.AcrossTransition;
}

public class ServerOnlyEntity : BaseEntity
{

}

public class ServerOnlyPointEntity : ServerOnlyEntity
{

}

public class LogicalEntity : ServerOnlyEntity
{

}
