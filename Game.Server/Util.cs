global using static Game.Util_Globals;

using CommunityToolkit.HighPerformance;

using Game.Server;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;
using Source.Engine.Server;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Mail;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;

using static Source.Common.Engine.IEngine;
using Color = Source.Color;

namespace Game;


public interface IEntityFactory
{
	IServerNetworkable? Create(ReadOnlySpan<char> className);
	void Destroy(IServerNetworkable networkable);
}

public interface IEntityFactoryDictionary
{
	void InstallFactory(IEntityFactory factory, ReadOnlySpan<char> classname);
	IServerNetworkable? Create(ReadOnlySpan<char> classname);
	void Destroy(ReadOnlySpan<char> className, IServerNetworkable networkable);
	IEntityFactory? FindFactory(ReadOnlySpan<char> className);
	ReadOnlySpan<char> GetCannonicalName(ReadOnlySpan<char> className);
}

public class CEntityFactory<T> : IEntityFactory where T : BaseEntity, new()
{
	public CEntityFactory(string classname) {
		EntityFactoryDictionary().InstallFactory(this, classname);
	}
	static T _CreateEntityTemplate(ReadOnlySpan<char> classname) {
		T newEnt = new T();
		newEnt.PostConstructor(classname);
		return newEnt;
	}
	public IServerNetworkable Create(ReadOnlySpan<char> className) {
		T ent = _CreateEntityTemplate(className);
		return ent.NetworkProp();
	}

	public void Destroy(IServerNetworkable networkable) {
		if (networkable != null)
			networkable.Release();
	}
}

public class CEntityFactoryDictionary : IEntityFactoryDictionary
{
	public IServerNetworkable? Create(ReadOnlySpan<char> className) {
		className = className.SliceNullTerminatedString();
		IEntityFactory? factory = FindFactory(className);
#if GMOD_DLL
		if (Game.Client.GarrysMod.GarrysMod.g_LuaManager != null) {
			Span<char> lowered = stackalloc char[512];
			strcpy(lowered, className);
			strlower(lowered);
			IServerNetworkable? luaEnt = Game.Client.GarrysMod.GarrysMod.g_LuaManager.CreateEntity(((ReadOnlySpan<char>)lowered).SliceNullTerminatedString(), factory != null);
			if (luaEnt != null)
				return luaEnt;
		}
#endif
		if (factory == null) {
			Warning($"Attempted to create unknown entity type {className}!\n");
			return null;
		}
		return factory.Create(className);
	}

	public void Destroy(ReadOnlySpan<char> className, IServerNetworkable networkable) {
		className = className.SliceNullTerminatedString();

		IEntityFactory? factory = FindFactory(className);
		if (factory == null) {
			Warning($"Attempted to destroy unknown entity type {factory}!\n");
			return;
		}

		factory.Destroy(networkable);
	}

	public IEntityFactory? FindFactory(ReadOnlySpan<char> className) {
		if (!Factories.TryGetValue(className.Hash(), out IEntityFactory? factory))
			return null;

		return factory;
	}

	public ReadOnlySpan<char> GetCannonicalName(ReadOnlySpan<char> className) {
		return className; // ??
	}

	public void InstallFactory(IEntityFactory factory, ReadOnlySpan<char> classname) {
		Factories[classname.Hash()] = factory;
	}

	public readonly Dictionary<ulong, IEntityFactory> Factories = [];
}


public static partial class Util_Globals
{
	static readonly CEntityFactoryDictionary s_EntityFactory = new();
	public static IEntityFactoryDictionary EntityFactoryDictionary() => s_EntityFactory;

	public static bool CanCreateEntityClass(ReadOnlySpan<char> classname) => EntityFactoryDictionary() != null && EntityFactoryDictionary().FindFactory(classname) != null;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int ENTINDEX(Edict? edict) {
		int result = edict != null ? edict.EdictIndex : 0;
		Assert(result == engine.IndexOfEdict(edict));
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Edict? INDEXENT(int edictNum) => engine.PEntityOfEntIndex(edictNum);

	public static bool FStrEq(ReadOnlySpan<char> sz1, ReadOnlySpan<char> sz2)
		=> Unsafe.AreSame(in sz1.DangerousGetReference(), in sz2.DangerousGetReference()) || stricmp(sz1, sz2) == 0;
}

public struct EntitySphereQuery
{
	public const int MAX_SPHERE_QUERY = 512;

	public EntitySphereQuery(in Vector3 center, float radius, EntityFlags flagMask = 0) {
		ListIndex = 0;
		ListCount = Util.EntitiesInSphere(List, center, radius, flagMask);
	}
	public BaseEntity? GetCurrentEntity() {
		if (ListIndex < ListCount)
			return List[ListIndex];
		return null;
	}
	public void NextEntity() => ListIndex++;

	[InlineArray(MAX_SPHERE_QUERY)] struct InlineArrayMaxSphereQuery<T> { public T first; }
	int ListIndex;
	int ListCount;
	InlineArrayMaxSphereQuery<BaseEntity?> List;
}

public ref struct FlaggedEntitiesEnum : IPartitionEnumerator
{
	public FlaggedEntitiesEnum(Span<BaseEntity> list, EntityFlags flagMask) {
		List = list;
		FlagMask = flagMask;
		Count = 0;
	}

	public IterationRetval EnumElement(IHandleEntity? handleEntity) {
		BaseEntity? entity = gEntList.GetBaseEntity(handleEntity.GetRefEHandle());
		if (entity != null) {
			if (FlagMask != 0 && 0 == (entity.GetFlags() & FlagMask))  // Does it meet the criteria?
				return IterationRetval.Continue;

			if (!AddToList(entity))
				return IterationRetval.Stop;
		}

		return IterationRetval.Continue;
	}
	public int GetCount() => Count;
	public bool AddToList(BaseEntity? entity) {
		if (Count >= List.Length) {
			AssertMsg(false, "reached enumerated list limit.  Increase limit, decrease radius, or make it so entity flags will work for you");
			return false;
		}
		List[Count++] = entity;
		return true;
	}

	Span<BaseEntity> List;
	EntityFlags FlagMask;
	int Count;
}

public static partial class Util
{
	public static float AngleMod(float a) => MathLib.AngleMod(a);

	public static float AngleDiff(float destAngle, float srcAngle) {
		float delta;

		delta = (destAngle - srcAngle) % 360.0f;
		if (destAngle > srcAngle) {
			if (delta >= 180)
				delta -= 360;
		}
		else {
			if (delta <= -180)
				delta += 360;
		}
		return delta;
	}

	public static void ClearTrace(ref Trace trace) {
		trace = default;
		trace.Fraction = 1.0f;
		trace.FractionLeftSolid = 0;
	}

	public static bool PointAtEntity(BaseEntity? dest, BaseEntity? target) {
		if (dest == null || target == null)
			return false;

		Vector3 dir = target.GetAbsOrigin() - dest.GetAbsOrigin();

		MathLib.VectorNormalize(ref dir);

		MathLib.VectorAngles(dir, out QAngle angles);
		dest.SetLocalAngles(angles);
		dest.SetAbsAngles(angles);

#if GMOD_DLL
		IPhysicsObject? physicsObject = dest.VPhysicsGetObject();
		if (physicsObject != null) {
			physicsObject.GetPosition(out Vector3 worldPos, out _);
			physicsObject.SetPosition(worldPos, angles, true);
		}
#endif

		return true;
	}

	public static void PointAtNamedEntity(BaseEntity dest, string? target) {
		if (!PointAtEntity(dest, gEntList.FindEntityByName(null, target)))
			DevMsg(1, $"{dest.GetClassname()} ({dest.GetDebugName()}) was unable to point at an entity named: {target}\n");
	}

	public static int DropToFloor(BaseEntity entity, Mask mask) => DropToFloor(entity, mask, null);

	public static int DropToFloor(BaseEntity entity, Mask mask, BaseEntity? ignore) {
		entity.SetGroundEntity(null);

		TraceEntity(entity, entity.GetAbsOrigin(), entity.GetAbsOrigin() - new Vector3(0, 0, 256), mask, ignore, entity.GetCollisionGroup(), out Trace trace);

		if (trace.AllSolid)
			return -1;

		if (trace.Fraction == 1)
			return 0;

		entity.SetAbsOrigin(trace.EndPos);
		entity.SetGroundEntity(trace.Ent);
#if GMOD_DLL
		entity.VPhysicsGetObject()?.SetPosition(trace.EndPos, entity.GetAbsAngles(), true);
#endif

		return 1;
	}

	public static bool ItemCanBeTouchedByPlayer(BaseEntity? item, BasePlayer? player) {
		if (item == null || player == null)
			return false;

		if (player.IsInAVehicle())
			return true;

		Vector3 vecStartPos;
		Source.Common.Physics.IPhysicsObject? physObj = item.VPhysicsGetObject();
		if (physObj != null)
			physObj.GetPosition(out vecStartPos, out _);
		else
			vecStartPos = item.CollisionProp().WorldSpaceCenter();

		Vector3 vecEndPos = player.EyePosition();

		TraceFilterSkipTwoEntities filter = new(player, item, Source.CollisionGroup.PlayerMovement);
		TraceLine(vecStartPos, vecEndPos, Mask.Solid, ref filter, out Trace tr);

		if (tr.Fraction < 1.0f)
			return false;

		return true;
	}

	public static void EmitAmbientSound(int entindex, in Vector3 vecOrigin, ReadOnlySpan<char> samp, float vol, Source.Common.Audio.SoundLevel soundlevel, int fFlags, int pitch, TimeUnit_t soundtime = 0.0f) => EmitAmbientSound(entindex, vecOrigin, samp, vol, soundlevel, fFlags, pitch, soundtime, out _);

	public static void EmitAmbientSound(int entindex, in Vector3 vecOrigin, ReadOnlySpan<char> samp, float vol, Source.Common.Audio.SoundLevel soundlevel, int fFlags, int pitch, TimeUnit_t soundtime, out TimeUnit_t duration) {
		duration = 0;
		if (!samp.IsEmpty && samp[0] == '!') {
			int sentenceIndex = SENTENCEG_Lookup(samp);
			if (sentenceIndex >= 0) {
				string name = $"!{sentenceIndex}";
				engine.EmitAmbientSound(entindex, vecOrigin, name, vol, soundlevel, fFlags, pitch, (float)soundtime);
				duration = enginesound.GetSoundDuration(name);

				// g_SoundEmitterSystem.TraceEmitSound( "UTIL_EmitAmbientSound:  Sentence emitted '%s' (ent %i)\n", name, entindex );
			}
		}
		else
			g_SoundEmitterSystem.EmitAmbientSound(entindex, vecOrigin, samp, vol, soundlevel, fFlags, pitch, soundtime, out duration);
	}

	public static bool g_bDisableEhandleAccess = false;
	public static bool g_bReceivedChainedUpdateOnRemove = false;
	public static void LogPrintf(ReadOnlySpan<char> text) {
		engine.LogPrint(text);
	}
	public static void SetMinMaxSize(BaseEntity ent, in Vector3 mins, in Vector3 maxs) {
		for (int i = 0; i < 3; i++)
			if (mins[i] > maxs[i])
				Error($"{((ent != null) ? ent.GetDebugName() : "<NULL>")}: backwards mins/maxs");

		Assert(ent != null);

		ent.SetCollisionBounds(mins, maxs);
	}

	public static float DistApprox(in Vector3 vec1, in Vector3 vec2) {
		float dx = vec1.X - vec2.X;
		float dy = vec1.Y - vec2.Y;
		float dz = vec1.Z - vec2.Z;

		return MathF.Abs(dx) + MathF.Abs(dy) + MathF.Abs(dz);
	}

	public static float DistApprox2D(in Vector3 vec1, in Vector3 vec2) {
		float dx = vec1.X - vec2.X;
		float dy = vec1.Y - vec2.Y;

		return MathF.Abs(dx) + MathF.Abs(dy);
	}

	public static BasePlayer? GetLocalPlayer() {
		if (gpGlobals.MaxClients > 1) {
			if (developer.GetBool()) {
				AssertMsg(false, "Util.GetLocalPlayer");
#if DEBUG
				Warning("Util.GetLocalPlayer() called in multiplayer game.\n");
#endif
			}

			if (!engine.IsDedicatedServer()) // Raphael: I don't want broken stuff :/ (I should probably go thru all functions that use this and edit them to support multiplayer properly. Also look into AI_GetSinglePlayer)
				return Util.PlayerByIndex(1);

			return null;
		}

		return Util.PlayerByIndex(1);
	}

	public static void SetSize(BaseEntity ent, in Vector3 min, in Vector3 max) {
		SetMinMaxSize(ent, min, max);
	}

	public static int EntitiesInBox(Span<BaseEntity> list, in Vector3 mins, in Vector3 maxs, EntityFlags flagMask) {
		FlaggedEntitiesEnum boxEnum = new(list, flagMask);
		return EntitiesInBox(mins, maxs, ref boxEnum);
	}

	public static int EntitiesInBox(in Vector3 mins, in Vector3 maxs, scoped ref FlaggedEntitiesEnum enumerator) {
		partition.EnumerateElementsInBox((SpatialPartitionListMask_t)PartitionListMask.EngineNonStaticEdicts, mins, maxs, false, ref enumerator);
		return enumerator.GetCount();
	}

	public static int EntitiesInSphere(Span<BaseEntity> list, in Vector3 center, float radius, EntityFlags flagMask) {
		FlaggedEntitiesEnum sphereEnum = new(list, flagMask);
		return EntitiesInSphere(center, radius, ref sphereEnum);
	}

	public static int EntitiesInSphere(in Vector3 center, float radius, scoped ref FlaggedEntitiesEnum enumerator) {
		partition.EnumerateElementsInSphere((int)PartitionListMask.EngineNonStaticEdicts, center, radius, false, ref enumerator);
		return enumerator.GetCount();
	}
	public static void ShowMessage(ReadOnlySpan<char> str, BasePlayer? player) {
		RecipientFilter filter = new();

		if (player != null)
			filter.AddRecipient(player);
		else
			filter.AddAllPlayers();

		filter.MakeReliable();

		UserMessageBegin(filter, "HudText");
		WRITE_STRING(str);
		MessageEnd();
	}
	public static void ShowMessageAll(ReadOnlySpan<char> str) {
		ShowMessage(str, null);
	}
  public static void ScreenShake(in Vector3 center, float amplitude, float frequency, float duration, float radius, ShakeCommand command, bool airShake = false) { }

#if GMOD_DLL
	public static void SayTextFilter<T>(scoped in T filter, ReadOnlySpan<char> pText, BasePlayer? player, bool chat, bool teamOnly, bool dead) where T : IRecipientFilter {
		UserMessageBegin(filter, "SayText");
		WRITE_BYTE((byte)(player?.EntIndex() ?? 0));

		WRITE_STRING(pText);
		WRITE_BYTE((byte)(chat ? 1 : 0));
		WRITE_BYTE((byte)(teamOnly ? 1 : 0));
		WRITE_BYTE((byte)(dead ? 1 : 0));
		MessageEnd();
	}
#else
	public static void SayTextFilter<T>(scoped in T filter, ReadOnlySpan<char> pText, BasePlayer? player, bool chat) where T : IRecipientFilter {
		UserMessageBegin(filter, "SayText");
		WRITE_BYTE((byte)(player?.EntIndex() ?? 0));

		WRITE_STRING(pText);
		WRITE_BYTE((byte)(chat ? 1 : 0));
		MessageEnd();
	}
#endif
	public static void SayText2Filter<T>(scoped in T filter, BasePlayer? entity, bool chat, ReadOnlySpan<char> msgName, ReadOnlySpan<char> param1 = default, ReadOnlySpan<char> param2 = default, ReadOnlySpan<char> param3 = default, ReadOnlySpan<char> param4 = default) where T : IRecipientFilter {
		UserMessageBegin(filter, "SayText2");
		WRITE_BYTE((byte)(entity?.EntIndex() ?? 0));

		WRITE_BYTE((byte)(chat ? 1 : 0));

		WRITE_STRING(msgName);

		WRITE_STRING(param1);
		WRITE_STRING(param2);
		WRITE_STRING(param3);
		WRITE_STRING(param4);

		MessageEnd();
	}


	public static void ScreenFade(BaseEntity? entity, in Color color, TimeUnit_t fadeTime, TimeUnit_t fadeHold, FadeFlags flags) {
		ScreenFade fade = default;

		Util.ScreenFadeBuild(ref fade, color, fadeTime, fadeHold, flags);
		Util.ScreenFadeWrite(in fade, entity);
	}

	public static void ScreenFadeAll(in Color color, TimeUnit_t fadeTime, TimeUnit_t fadeHold, FadeFlags flags) {
		int i;
		ScreenFade fade = default;

		Util.ScreenFadeBuild(ref fade, color, fadeTime, fadeHold, flags);

		for (i = 1; i <= gpGlobals.MaxClients; i++) {
			BaseEntity? player = Util.PlayerByIndex(i);

			Util.ScreenFadeWrite(in fade, player);
		}
	}

	public static ushort FixedUnsigned16(TimeUnit_t value, float scale) {
		int output;

		output = (int)(value * scale);
		if (output < 0)
			output = 0;
		if (output > 0xFFFF)
			output = 0xFFFF;

		return (ushort)output;
	}


	public static void ScreenFadeWrite(in ScreenFade fade, BaseEntity? entity) {
		if (entity == null || !entity.IsNetClient())
			return;

		SingleUserRecipientFilter user = new((BasePlayer)entity);
		user.MakeReliable();

		UserMessageBegin(user, "Fade");     // use the magic #1 for "one client"
		WRITE_SHORT((short)fade.Duration);     // fade lasts this long
		WRITE_SHORT((short)fade.HoldTime);     // fade lasts this long
		WRITE_SHORT((short)fade.FadeFlags);        // fade type (in / out)
		WRITE_BYTE(fade.R);             // fade red
		WRITE_BYTE(fade.G);             // fade green
		WRITE_BYTE(fade.B);             // fade blue
		WRITE_BYTE(fade.A);             // fade blue
		MessageEnd();
	}

	public static void ScreenFadeBuild(ref ScreenFade fade, Source.Color color, double fadeTime, double fadeHold, FadeFlags flags) {
		fade.Duration = FixedUnsigned16(fadeTime, 1 << Source.Common.ScreenFade.SCREENFADE_FRACBITS);        // 7.9 fixed
		fade.HoldTime = FixedUnsigned16(fadeHold, 1 << Source.Common.ScreenFade.SCREENFADE_FRACBITS);        // 7.9 fixed
		fade.R = color.R;
		fade.G = color.G;
		fade.B = color.B;
		fade.A = color.A;
		fade.FadeFlags = flags;
	}

	public static void TransmitShakeEvent(BasePlayer player, float localAmplitude, float frequency, TimeUnit_t duration, ShakeCommand command) {
		if ((localAmplitude > 0) || (command == ShakeCommand.Stop)) {
			if (command == ShakeCommand.Stop)
				localAmplitude = 0;

			SingleUserRecipientFilter user = new(player);
			user.MakeReliable();
			UserMessageBegin(user, "Shake");
			WRITE_BYTE((byte)command);          // shake command (SHAKE_START, STOP, FREQUENCY, AMPLITUDE)
			WRITE_FLOAT(localAmplitude);        // shake magnitude/amplitude
			WRITE_FLOAT(frequency);             // shake noise frequency
			WRITE_FLOAT((float)duration);       // shake lasts this long
			MessageEnd();
		}
	}

	public static void PrecacheOther(ReadOnlySpan<char> className, ReadOnlySpan<char> modelName = default) {
		bool precaching = s_RemoveImmediateSemaphore == 0;
		if (precaching)
			Game.Client.GarrysMod.GarrysMod.g_GModPrecaching++;
		if (Game.Client.GarrysMod.GarrysMod.g_GModPrecaching > 0x20)
			Error("Something seems to have gone wroong");

		BaseEntity? entity = CreateEntityByName(className);
		if (entity == null)
			Warning("NULL Ent in Util.PrecacheOther\n");
		else {
			// If we have a specified model, set it before calling precache
			if (!modelName.IsStringEmpty)
				entity.SetModelName(modelName);

			entity.Precache();
			Util.RemoveImmediate(entity);
		}

		if (precaching)
			Game.Client.GarrysMod.GarrysMod.g_GModPrecaching--;
	}

	public static void ClientPrintFilter<Filter>(scoped in Filter filter, HudPrint dest, ReadOnlySpan<char> msgName, ReadOnlySpan<char> param1 = default, ReadOnlySpan<char> param2 = default, ReadOnlySpan<char> param3 = default, ReadOnlySpan<char> param4 = default) where Filter : IRecipientFilter {
		UserMessageBegin(filter, "TextMsg");
		WRITE_BYTE((byte)dest);
		WRITE_STRING(msgName);

		if (!param1.IsEmpty)
			WRITE_STRING(param1);
		else
			WRITE_STRING("");

		if (!param2.IsEmpty)
			WRITE_STRING(param2);
		else
			WRITE_STRING("");

		if (!param3.IsEmpty)
			WRITE_STRING(param3);
		else
			WRITE_STRING("");

		if (!param4.IsEmpty)
			WRITE_STRING(param4);
		else
			WRITE_STRING("");

		MessageEnd();
	}
	public static void ClientPrint(BasePlayer? player, HudPrint dest, ReadOnlySpan<char> msgName, ReadOnlySpan<char> param1 = default, ReadOnlySpan<char> param2 = default, ReadOnlySpan<char> param3 = default, ReadOnlySpan<char> param4 = default) {
		if (player == null)
			return;

		SingleUserRecipientFilter user = new(player);
		user.MakeReliable();

		ClientPrintFilter(user, dest, msgName, param1, param2, param3, param4);
	}

	public static Edict? INDEXENT(int edictNum) => engine.PEntityOfEntIndex(edictNum);

	public static BasePlayer? PlayerByIndex(int playerIndex) {
		BasePlayer? player = null;

		if (playerIndex > 0 && playerIndex <= gpGlobals.MaxClients) {
			Edict? playerEdict = INDEXENT(playerIndex);
			if (playerEdict != null && !playerEdict.IsFree())
				player = (BasePlayer?)BaseEntity.GetContainingEntity(playerEdict);
		}

		return player;
	}

	public static BaseEntity? EntityByIndex(int entityIndex) {
		BaseEntity? entity = null;

		if (entityIndex > 0) {
			Edict? edict = INDEXENT(entityIndex);
			if (edict != null && !edict.IsFree())
				entity = BaseEntity.GetContainingEntity(edict);
		}

		return entity;
	}

	public static BasePlayer? GetListenServerHost() {
		if (engine.IsDedicatedServer()) {
			Assert("Util.GetListenServerHost");
			Warning("Util.GetListenServerHost() called from a dedicated server or single-player game.\n");
			return null;
		}

		return PlayerByIndex(1);
	}

	public static bool IsCommandIssuedByServerAdmin() {
		int issuingPlayerIndex = GetCommandClientIndex();

		if (engine.IsDedicatedServer() && issuingPlayerIndex > 0)
			return false;

		return issuingPlayerIndex <= 1;
	}

	public static void ClientPrintAll(HudPrint dest, ReadOnlySpan<char> msgName, ReadOnlySpan<char> param1 = default, ReadOnlySpan<char> param2 = default, ReadOnlySpan<char> param3 = default, ReadOnlySpan<char> param4 = default) {
		ReliableBroadcastRecipientFilter filter = new();
		ClientPrintFilter(filter, dest, msgName, param1, param2, param3, param4);
	}

	public static int DispatchSpawn(BaseEntity? entity) {
		Msg($"Dispatching spawn for {entity}\n");
		if (entity != null) {
			// keep a smart pointer that will know if the object gets deleted
			EHANDLE pEntSafe = new();
			pEntSafe.Set(entity);

			// TODO: GetBaseAnimating / SetBoneCacheFlags(BCF_IS_IN_SPAWN)
			entity.Spawn();
			// TODO: ClearBoneCacheFlags(BCF_IS_IN_SPAWN)

			// Try to get the pointer again, in case the spawn function deleted the entity.
			if (!pEntSafe.IsValid() || entity.IsMarkedForDeletion())
				return -1;

			if (entity.GlobalName != null) {
				int globalIndex = GlobalEntity.GetIndex(entity.GlobalName);
				if (globalIndex >= 0) {
					if (GlobalEntity.GetState(globalIndex) == GlobalEState.Dead) {
						entity.Remove();
						return -1;
					}
					else if (!FStrEq(gpGlobals.MapName, GlobalEntity.GetMap(globalIndex))) {
						entity.MakeDormant();
					}
				}
				else
					GlobalEntity.Add(entity.GlobalName, gpGlobals.MapName, GlobalEState.On);

			}

			gEntList.NotifySpawn(entity);
		}

		return 0;
	}

#if GMOD_DLL
	public static void RemoveSafe(BaseEntity? entity) {
		if (entity == null)
			return;
		if (Game.Client.GarrysMod.GarrysMod.IsSacredEntity(entity.Classname ?? "")) {
			Warning($"Refusing to delete entity '{entity.Classname ?? ""}'!\n");
			return;
		}
		Remove(entity.NetworkProp());
	}
#endif

	public static void Remove(BaseEntity? entity) {
		if (entity == null)
			return;
		Remove(entity.NetworkProp());
	}

	public static void Remove(IServerNetworkable? oldObj) {
		ServerNetworkProperty? prop = (ServerNetworkProperty?)oldObj;
		if (prop == null || prop.IsMarkedForDeletion())
			return;

		if (PhysIsInCallback()) {
			// This assert means that someone is deleting an entity inside a callback.  That isn't supported so
			// this code will defer the deletion of that object until the end of the current physics simulation frame
			// Since this is hidden from the calling code it's preferred to call PhysCallbackRemove() directly from the caller
			// in case the deferred delete will have unwanted results (like continuing to receive callbacks).  That will make it 
			// obvious why the unwanted results are happening so the caller can handle them appropriately. (some callbacks can be masked 
			// or the calling entity can be flagged to filter them in most cases)
			Assert(0);
			PhysCallbackRemove(oldObj);
			return;
		}

		// mark it for deletion	
		prop.MarkForDeletion();

		BaseEntity baseEnt = (BaseEntity?)oldObj.GetBaseEntity();
		if (baseEnt != null) {
			g_bReceivedChainedUpdateOnRemove = false;
			baseEnt.UpdateOnRemove();

			Assert(g_bReceivedChainedUpdateOnRemove);

			// clear oldObj targetname / other flags now
			baseEnt.SetName(null);
		}

		gEntList.AddToDeleteList(oldObj);
	}
	static int s_RemoveImmediateSemaphore = 0;
	public static void DisableRemoveImmediate() {
		s_RemoveImmediateSemaphore++;
	}
	public static void EnableRemoveImmediate() {
		s_RemoveImmediateSemaphore--;
		Assert(s_RemoveImmediateSemaphore >= 0);
	}
	public static void RemoveImmediate(BaseEntity? oldObj) {
		if (oldObj == null || oldObj.IsEFlagSet(EFL.KillMe))
			return;

		if (s_RemoveImmediateSemaphore != 0) {
			Remove(oldObj);
			return;
		}


		oldObj.AddEFlags(EFL.KillMe);  // Make sure to ignore further calls into here or Util.Remove.

		g_bReceivedChainedUpdateOnRemove = false;
		oldObj.UpdateOnRemove();
		Assert(g_bReceivedChainedUpdateOnRemove);

		// Entities shouldn't reference other entities in their destructors
		//  that type of code should only occur in an UpdateOnRemove call
		g_bDisableEhandleAccess = true;
		oldObj.Term();
		g_bDisableEhandleAccess = false;
	}

	public static int GetCommandClientIndex() => ServerGameClients.CommandClientIndex + 1;

	public static BasePlayer? GetCommandClient() {
		int id = GetCommandClientIndex();
		if (id > 0)
			return PlayerByIndex(id)!;

		return null;
	}

	internal static void SetOrigin(BaseEntity entity, in Vector3 origin, bool fireTriggers = false) {
		entity.SetLocalOrigin(origin);
	}

	internal static void SetModel(BaseEntity baseEntity, ReadOnlySpan<char> modelName) {
		int i = modelinfo.GetModelIndex(modelName);
#if GMOD_DLL
		if (i == -1) {
			i = BaseEntity.PrecacheModel(modelName, true);
			if (i < 0)
				return;
		}
#else
		if (i == -1)
			Error($"{baseEntity.EntIndex()}/{baseEntity/*.GetEntityName()*/} - {baseEntity.GetClassname()}:  Util.SetModel:  not precached: {modelName}\n");
#endif

		BaseAnimating? animating = baseEntity.GetBaseAnimating();
		animating?.ForceBone = 0;

		baseEntity.SetModelName(modelName);
		baseEntity.SetModelIndex(i);
		SetMinMaxSize(baseEntity, vec3_origin, vec3_origin);
		baseEntity.SetCollisionBoundsFromModel();
	}

	public static void ParentToWorldSpace(BaseEntity? entity, ref Vector3 position, ref QAngle angles) {
		if (entity == null)
			return;

		// Construct the entity-to-world matrix
		// Start with making an entity-to-parent matrix
		Matrix3x4 matEntityToParent;
		MathLib.AngleMatrix(angles, out matEntityToParent);
		MathLib.MatrixSetColumn(position, 3, ref matEntityToParent);

		// concatenate with our parent's transform
		Matrix3x4 matScratch = default, matResult;
		Matrix3x4 matParentToWorld;

		if (entity.GetParent() != null)
			matParentToWorld = entity.GetParentToWorldTransform(ref matScratch);
		else
			matParentToWorld = entity.EntityToWorldTransform();


		MathLib.ConcatTransforms(matParentToWorld, matEntityToParent, out matResult);

		// pull our absolute position out of the matrix
		MathLib.MatrixGetColumn(matResult, 3, out position);
		MathLib.MatrixAngles(matResult, out angles);
	}

	public static void ParentToWorldSpace(BaseEntity? entity, ref Vector3 position, ref Quaternion quat) {
		if (entity == null)
			return;

		QAngle angles;
		MathLib.QuaternionAngles(quat, out angles);
		ParentToWorldSpace(entity, ref position, ref angles);
		MathLib.AngleQuaternion(angles, out quat);
	}

	public static void WorldToParentSpace(BaseEntity? entity, ref Vector3 position, ref QAngle angles) {
		if (entity == null)
			return;

		// Construct the entity-to-world matrix
		// Start with making an entity-to-parent matrix
		Matrix3x4 matEntityToParent;
		MathLib.AngleMatrix(angles, out matEntityToParent);
		MathLib.MatrixSetColumn(position, 3, ref matEntityToParent);

		// concatenate with our parent's transform
		Matrix3x4 matScratch = default, matResult;
		Matrix3x4 matWorldToParent;

		if (entity.GetParent() != null)
			matScratch = entity.GetParentToWorldTransform(ref matScratch);
		else
			matScratch = entity.EntityToWorldTransform();


		MathLib.MatrixInvert(matScratch, out matWorldToParent);
		MathLib.ConcatTransforms(matWorldToParent, matEntityToParent, out matResult);

		// pull our absolute position out of the matrix
		MathLib.MatrixGetColumn(matResult, 3, out position);
		MathLib.MatrixAngles(matResult, out angles);
	}

	public static void WorldToParentSpace(BaseEntity? entity, ref Vector3 position, ref Quaternion quat) {
		if (entity == null)
			return;

		QAngle angles;
		MathLib.QuaternionAngles(quat, out angles);
		WorldToParentSpace(entity, ref position, ref angles);
		MathLib.AngleQuaternion(angles, out quat);
	}
}

public struct EntityMatrix
{
	public Matrix4x4 Underlying;
	public Matrix4x4 Transpose() => Matrix4x4.Transpose(Underlying);

	public static implicit operator Matrix4x4(EntityMatrix matrix) => matrix.Underlying;
	public static implicit operator EntityMatrix(Matrix4x4 matrix) => new EntityMatrix { Underlying = matrix };

	public void InitFromEntity(BaseEntity? entity, int attachment = 0) {
		if (entity == null) {
			Underlying = Matrix4x4.Identity;
			return;
		}

		// Get an attachment's matrix?
		if (attachment != 0) {
			BaseAnimating? animating = entity.GetBaseAnimating();
			if (animating != null && animating.GetModelPtr() != null) {
				Vector3 origin;
				QAngle angles;
				if (animating.GetAttachment(attachment, out origin, out angles)) {
					Underlying.SetupMatrixOrgAngles(origin, angles);
					return;
				}
			}
		}

		Underlying.SetupMatrixOrgAngles(entity.GetAbsOrigin(), entity.GetAbsAngles());
	}
	public void InitFromEntityLocal(BaseEntity? entity, int attachment = 0) {
		if (entity == null || entity.Edict() == null) {
			Underlying = Matrix4x4.Identity;
			return;
		}
		Underlying.SetupMatrixOrgAngles(entity.GetLocalOrigin(), entity.GetLocalAngles());
	}

	public Vector3 LocalToWorld(in Vector3 vVec) {
		return MathLib.VMul4x3(ref Underlying, vVec);
	}

	public Vector3 WorldToLocal(in Vector3 vVec) {
		return MathLib.VMul4x3Transpose(ref Underlying, vVec);
	}

	public Vector3 LocalToWorldRotation(in Vector3 vVec) {
		return MathLib.VMul3x3(ref Underlying, vVec);
	}

	public Vector3 WorldToLocalRotation(in Vector3 vVec) {
		return MathLib.VMul3x3Transpose(ref Underlying, vVec);
	}
}
