global using static Game.Client.BaseEntityConsts;
global using static Game.Client.PredictableList;

using CommunityToolkit.HighPerformance;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.GarrysMod;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Mathematics;
using Source.Common.Networking;

using System;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using DEFINE = Source.DEFINE<Game.Client.C_BaseEntity>;
using FIELD = Source.FIELD<Game.Client.C_BaseEntity>;

using AimEntsListHandle_t = int;
using Source.Common.Physics;
using Source.Common.Formats.BSP;

namespace Game.Client;

public static class BaseEntityConsts
{
	public const int MULTIPLAYER_BACKUP = 90;
}

public enum InterpolateResult
{
	Stop = 0,
	Continue = 1
}

public enum EntClientFlags
{
	GettingShadowRenderBounds = 0x0001,
	DontUseIK = 0x0002,
	AlwaysInterpolate = 0x0004,
}

public class PredictionContext : IPoolableObject
{
	static readonly ObjectPool<PredictionContext> instances = new();
	public static PredictionContext Alloc() => instances.Alloc();
	public static void Free(PredictionContext instance) => instances.Free(instance);

	public void Init() { }

	public void Reset() {
		Active = false;
		CreationCommandNumber = -1;
		CreationModule = null;
		CreationLineNumber = 0;
		ServerEntity.Set(null);
	}

	public bool Active;
	public int CreationCommandNumber;
	public string? CreationModule;
	public int CreationLineNumber;
	public Handle<C_BaseEntity> ServerEntity = new();
}


public struct ThinkFunc
{
	public C_BaseEntity.BASEPTR? Think;
	public string Context;
	public long NextThinkTick;
	public long LastThinkTick;
}

public class PredictableList : IPredictableList
{
	public static readonly PredictableList g_Predictables = new();
	public C_BaseEntity? GetPredictable(int slot) {
		return cl_entitylist.GetBaseEntityFromHandle(Predictables[slot]);
	}

	public int GetPredictableCount() {
		return Predictables.Count;
	}

	public void AddToPredictableList(in ClientEntityHandle add) {
		Assert(!Unsafe.IsNullRef(in add));

		if (Predictables.Contains(add))
			return;

		Predictables.Add(add);
		int count = Predictables.Count;
		if (count < 2)
			return;

		int i, j;
		for (i = 0; i < count; i++) {
			for (j = i + 1; j < count; j++) {
				ClientEntityHandle h1 = Predictables[i];
				ClientEntityHandle h2 = Predictables[j];

				C_BaseEntity? p1 = cl_entitylist.GetBaseEntityFromHandle(h1);
				C_BaseEntity? p2 = cl_entitylist.GetBaseEntityFromHandle(h2);

				if (p1 == null || p2 == null) {
					Assert(false);
					continue;
				}

				if (p1.EntIndex() != -1 && p2.EntIndex() != -1) {
					if (p1.EntIndex() < p2.EntIndex())
						continue;
				}

				if (p2.EntIndex() == -1)
					continue;

				Predictables[i] = h2;
				Predictables[j] = h1;
			}
		}
	}

	internal void RemoveFromPredictablesList(in ClientEntityHandle remove) {
		Assert(!Unsafe.IsNullRef(in remove));

		Predictables.Remove(remove);
	}

	private readonly List<ClientEntityHandle> Predictables = [];
}

[NetworkName("CBaseEntity")]
public partial class C_BaseEntity : IClientEntity
{
	public delegate void BASEPTR(C_BaseEntity self);
	public delegate void ENTITYFUNCPTR(C_BaseEntity self, C_BaseEntity? other);

	public const int SLOT_ORIGINALDATA = -1;
	public static C_BaseEntity? CreateEntityByName(ReadOnlySpan<char> className) {
		C_BaseEntity? ent = GetClassMap().CreateEntity(className);
		if (ent != null)
			return ent;

		Warning($"Can't find factory for entity: {className}\n");
		return null;
	}

	static readonly LinkedList<C_BaseEntity> InterpolationList = [];
	static readonly LinkedList<C_BaseEntity> TeleportList = [];
	static bool s_bInterpolate = true;
	static bool g_bWasSkipping;
	static bool g_bWasThreaded;
	static bool s_bAbsQueriesValid = true;
	static bool s_bAbsRecomputationEnabled = true;
	static readonly bool[] AbsRecomputationStack = new bool[8];
	static ushort AbsRecomputationStackPos = 0;
	static ConVar cl_interpolate = new("cl_interpolate", "1", FCvar.UserInfo | FCvar.DevelopmentOnly);

	ClientThinkHandle_t thinkHandle;
	public ClientThinkHandle_t GetThinkHandle() => thinkHandle;
	public void SetThinkHandle(ClientThinkHandle_t handle) => thinkHandle = handle;

	static int PredictionRandomSeed = -1;
	static C_BasePlayer? PredictionPlayer;


	public virtual C_BaseAnimating? GetBaseAnimating() => null;

	public virtual bool IsWorld() => EntIndex() == 0;
	public virtual bool IsPlayer() => false;
	public virtual bool IsBaseCombatCharacter() => false;
	public virtual bool IsNPC() => false;
	public virtual bool IsNextBot() => false;
	public virtual bool IsBaseCombatWeapon() => false;
	public virtual bool IsCombatItem() => false;

	static readonly char[] GetClassname__outstr = new char[256];
	public ReadOnlySpan<char> GetClassname() {
		GetClassname__outstr[0] = '\0';
		bool gotname = false;
		if (GetPredDescMap() != null) {
			ReadOnlySpan<char> mapname = GetClassMap().Lookup(GetPredDescMap()!.DataClassName);
			if (!mapname.IsEmpty) {
				strcpy(GetClassname__outstr, mapname);
				gotname = true;
			}
		}

		if (!gotname)
			strcpy(GetClassname__outstr, GetType().Name);

		return GetClassname__outstr.SliceNullTerminatedString();
	}


	public virtual void PreEntityPacketReceived(int commandsAcknowledged) {
		bool copyintermediate = (commandsAcknowledged > 0) ? true : false;

		Assert(GetPredictable());
		Assert(cl_predict.GetInt() != 0);

		// First copy in any intermediate predicted data for non-networked fields
		if (copyintermediate) {
			RestoreData("PreEntityPacketReceived", commandsAcknowledged - 1, PredictionCopyType.NonNetworkedOnly);
			RestoreData("PreEntityPacketReceived", SLOT_ORIGINALDATA, PredictionCopyType.NetworkedOnly);
		}
		else {
			RestoreData("PreEntityPacketReceived(no commands ack)", SLOT_ORIGINALDATA, PredictionCopyType.Everything);
		}

		// At this point the entity has original network data restored as of the last time the 
		// networking was updated, and it has any intermediate predicted values properly copied over
		// Unpacked and OnDataChanged will fill in any changed, networked fields.

		// That networked data will be copied forward into the starting slot for the next prediction round
	}
	public virtual bool IsPredicted() => false;

	public void PostEntityPacketReceived() {
		// Always mark as changed
		HLClient.AddDataChangeEvent(this, DataUpdateType.DataTableChanged, DataChangeEventRef);

		// Save networked fields into "original data" store
		SaveData("PostEntityPacketReceived", SLOT_ORIGINALDATA, PredictionCopyType.NetworkedOnly);
	}

	public static void InterpolateServerEntities() {
		s_bInterpolate = cl_interpolate.GetBool();

		// Don't interpolate during timedemo playback
		if (engine.IsPlayingTimeDemo() || engine.IsPaused())
			s_bInterpolate = false;

		if (!engine.IsPlayingDemo()) {
			INetChannelInfo? nci = engine.GetNetChannelInfo();
			if (nci != null && nci.GetTimeSinceLastReceived() > 0.5)
				s_bInterpolate = false;
		}

		if (IsSimulatingOnAlternateTicks() != g_bWasSkipping || IsEngineThreaded() != g_bWasThreaded) {
			g_bWasSkipping = IsSimulatingOnAlternateTicks();
			g_bWasThreaded = IsEngineThreaded();

			C_BaseEntityIterator iterator = new();
			C_BaseEntity? ent;
			while ((ent = iterator.Next()) != null) {
				ent.Interp_UpdateInterpolationAmounts(ref ent.GetVarMapping());
			}
		}

		// Enable extrapolation?
		using InterpolationContext context = new();
		InterpolationContext.SetLastTimeStamp(engine.GetLastTimeStamp());
		if (cl_extrapolate.GetBool() && !engine.IsPaused())
			InterpolationContext.EnableExtrapolation(true);

		// Smoothly interpolate position for server entities.
		ProcessTeleportList();
		ProcessInterpolatedList();
	}

	public static void ProcessTeleportList() {
		LinkedListNode<C_BaseEntity>? entNode = TeleportList.First;
		while (entNode != null) {
			C_BaseEntity entity = entNode.Value;

			bool teleport = entity.Teleported();
			bool ef_nointerp = entity.IsNoInterpolationFrame();

			if (teleport || ef_nointerp) {
				entity.OldMoveParent.Set(entity.NetworkMoveParent);
				entity.OldParentAttachment = entity.ParentAttachment;
				entity.MoveToLastReceivedPosition(true);
				entity.ResetLatched();

				entNode = entNode.Next;
			}
			else {
				// Note: removing from the teleport list modifies the collection, so grab the next node now before removing
				var nextNode = entNode.Next;
				entity.RemoveFromTeleportList();
				entNode = nextNode;
			}
		}
	}

	public bool IsServer() => false;
	public bool IsClient() => true;
	public ReadOnlySpan<char> GetDLLType() => "client";

	public void SetMoveType(MoveType val, MoveCollide moveCollide = Source.MoveCollide.Default) {
		MoveType = (byte)val;
		SetMoveCollide(moveCollide);
	}

	public void SetMoveCollide(MoveCollide val) {
		MoveCollide = (byte)val;
	}

	public WaterLevel GetWaterLevel() => (WaterLevel)WaterLevel;
	public void SetWaterLevel(WaterLevel level) => WaterLevel = (byte)level;

	public virtual void CreateLightEffects() {

	}

	public virtual void AddEntity() {
		if (Index == 0)
			return;

		CreateLightEffects();
	}

	public virtual void Simulate() {
		AddEntity();
	}

	public virtual void ResetLatched() {
		if (IsClientCreated())
			return;

		Interp_Reset(ref GetVarMapping());
	}

	public static bool IsAbsQueriesValid() => !ThreadInMainThread() || s_bAbsQueriesValid;
	public static void SetAbsQueriesValid(bool valid) {
		if (!ThreadInMainThread())
			return;

		s_bAbsQueriesValid = valid;
	}

	public static bool IsAbsRecomputationsEnabled() => !ThreadInMainThread() || s_bAbsRecomputationEnabled;

	public static void PushEnableAbsRecomputations(bool enable) {
		if (!ThreadInMainThread())
			return;

		if (AbsRecomputationStackPos < AbsRecomputationStack.Length) {
			AbsRecomputationStack[AbsRecomputationStackPos] = s_bAbsRecomputationEnabled;
			++AbsRecomputationStackPos;
			s_bAbsRecomputationEnabled = enable;
		}
		else
			Assert(false);
	}

	public static void PopEnableAbsRecomputations() {
		if (!ThreadInMainThread())
			return;

		if (AbsRecomputationStackPos > 0) {
			--AbsRecomputationStackPos;
			s_bAbsRecomputationEnabled = AbsRecomputationStack[AbsRecomputationStackPos];
		}
		else
			Assert(false);
	}

	public static void EnableAbsRecomputations(bool enable) {
		if (!ThreadInMainThread())
			return;

		Assert(AbsRecomputationStackPos == 0);

		s_bAbsRecomputationEnabled = enable;
	}

	void Clear() {
		Dormant = true;
		CreationTick = -1;
		ModelInstance = MODEL_INSTANCE_INVALID;
		renderHandle = INVALID_CLIENT_RENDER_HANDLE;
		thinkHandle = INVALID_THINK_HANDLE;
		AimEntsListHandle = INVALID_AIMENTS_LIST_HANDLE;
		Index = -1;
		Collision.Init(this);
		SetLocalOrigin(vec3_origin);
		SetLocalAngles(vec3_angle);
		Model = null;
		AbsOrigin = default;
		AbsRotation = default;
		Velocity = default;
		ViewOffset = default;
		BaseVelocity = default;
		ModelIndex = default;
		AnimTime = default;
		SimulationTime = default;

		eflags = 0;
		RenderMode = 0;
		OldRenderMode = 0;
		RenderFX = 0;
		Friction = 0;

		UpdateVisibility();
	}

	static readonly List<C_BaseEntity> AimEntsList = [];
	public BASEPTR? FnThink;
	public virtual void Think() {
		if (FnThink != null)
			this.FnThink(this);
	}

	internal static void CalcAimEntPositions() {
		foreach (var ent in AimEntsList) {
			Assert(ent.GetMoveParent() != null);
			if (ent.IsEffectActive(EntityEffects.BoneMerge))
				ent.CalcAbsolutePosition();
		}
	}

	internal static void AddVisibleEntities() {
		int c = predictables.GetPredictableCount();

		for (int i = 0; i < c; i++) {
			BaseEntity? ent = predictables.GetPredictable(i);

			if (ent == null)
				continue;

			if (!ent.IsClientCreated())
				continue;

			if (ent.PredictableID.GetAcknowledged())
				continue;

			if (ent.IsDormantPredictable())
				continue;

			ent.UpdateVisibility();
		}

#if DEBUG
		DrawEntityDebugOverlays();
#endif
	}

	public bool IsNoInterpolationFrame() => OldInterpolationFrame != InterpolationFrame;

	public bool Teleported() => OldMoveParent != NetworkMoveParent || OldParentAttachment != ParentAttachment;

	public static void ProcessInterpolatedList() {
		LinkedListNode<C_BaseEntity>? next;
		for (LinkedListNode<C_BaseEntity>? curr = InterpolationList.First; curr != null; curr = next) {
			next = curr.Next;
			C_BaseEntity entity = curr.Value;
			entity.ReadyToDraw = entity.Interpolate(gpGlobals.CurTime);
		}
	}

	static ConVar cl_extrapolate = new("1", FCvar.Cheat, "Enable/disable extrapolation if interpolation history runs out.");
	static ConVar cl_interp_npcs = new("0.0", FCvar.UserInfo, "Interpolate NPC positions starting this many seconds in past (or cl_interp, if greater)");
	static ConVar cl_interp_all = new("0", 0, "Disable interpolation list optimizations.", 0, 0, cc_cl_interp_all_changed);

	private static void cc_cl_interp_all_changed(IConVar ivar, in ConVarChangeContext ctx) {
		ConVarRef var = new(ivar);
		if (var.GetInt() != 0) {
			C_BaseEntityIterator iterator = new();
			C_BaseEntity? ent;
			while ((ent = iterator.Next()) != null) {
				if (ent.ShouldInterpolate()) {
					ent.AddToInterpolationList();
				}
			}
		}
	}

	private static C_BaseEntity? FindPreviouslyCreatedEntity(PredictableId testId) {
		int c = predictables.GetPredictableCount();

		for (int i = 0; i < c; i++) {
			BaseEntity? e = predictables.GetPredictable(i);
			if (e == null || !e.IsClientCreated())
				continue;

			if (testId == e.PredictableID)
				return e;
		}

		return null;
	}

	private static void RecvProxy_AnimTime(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		C_BaseEntity pEntity = (C_BaseEntity)instance;

		long t = gpGlobals.GetNetworkBase(gpGlobals.TickCount, pEntity.EntIndex()) + data.Value.Int;

		while (t < gpGlobals.TickCount - 127)
			t += 256;
		while (t > gpGlobals.TickCount + 127)
			t -= 256;

		pEntity.AnimTime = t * TICK_INTERVAL;
	}

	private static void RecvProxy_EffectFlags(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		((C_BaseEntity)instance).SetEffects((EntityEffects)data.Value.Int);
	}


	public static RecvTable DT_AnimTimeMustBeFirst = new(nameof(DT_AnimTimeMustBeFirst), [
		RecvPropInt(FIELD.OF(nameof(AnimTime)), 0, RecvProxy_AnimTime),
	]);


	public static RecvTable DT_PredictableId = new(nameof(DT_PredictableId), [
		RecvPropPredictableId(FIELD.OF(nameof(PredictableID))),
		RecvPropInt(FIELD.OF(nameof(b_IsPlayerSimulated))),
	]);

	protected static void RecvProxy_SimulationTime(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		C_BaseEntity entity = (C_BaseEntity)instance;

		int addt = data.Value.Int;
		int tickbase = (int)gpGlobals.GetNetworkBase(gpGlobals.TickCount, entity.EntIndex());

		int t = tickbase + addt;

		while (t < gpGlobals.TickCount - 127)
			t += 256;
		while (t > gpGlobals.TickCount + 127)
			t -= 256;

		entity.SimulationTime = (t * TICK_INTERVAL);
	}

	public static RecvTable DT_BaseEntity = new([
		RecvPropDataTable("AnimTimeMustBeFirst", DT_AnimTimeMustBeFirst),

		RecvPropInt(FIELD.OF(nameof(SimulationTime)), 0, RecvProxy_SimulationTime),
		RecvPropInt(FIELD.OF(nameof(InterpolationFrame))),

		RecvPropVector(FIELD.OF_NAMED(nameof(NetworkOrigin), "m_vecOrigin")),
		RecvPropQAngles(FIELD.OF_NAMED(nameof(NetworkAngles), "m_angRotation")),

		RecvPropInt(FIELD.OF(nameof(ModelIndex)), 0, RecvProxy_IntToModelIndex16_BackCompatible),

		RecvPropInt(FIELD.OF(nameof(Effects)), 0, RecvProxy_EffectFlags),
		RecvPropInt(FIELD.OF(nameof(RenderMode))),
		RecvPropInt(FIELD.OF(nameof(RenderFX))),
		RecvPropInt(FIELD.OF(nameof(ColorRender))),
		RecvPropInt(FIELD.OF(nameof(TeamNum))),
		RecvPropInt(FIELD.OF(nameof(CollisionGroup))),
		RecvPropFloat(FIELD.OF(nameof(Elasticity))),
		RecvPropFloat(FIELD.OF(nameof(ShadowCastDistance))),
		RecvPropEHandle(FIELD.OF(nameof(OwnerEntity))),
		RecvPropEHandle(FIELD.OF(nameof(EffectEntity))),
		RecvPropInt(FIELD.OF(nameof(NetworkMoveParent)), 0, RecvProxy_IntToMoveParent),
		RecvPropInt(FIELD.OF(nameof(ParentAttachment))),

		RecvPropInt(FIELD.OF(nameof(MoveType)), 0, RecvProxy_MoveType),
		RecvPropInt(FIELD.OF(nameof(MoveCollide)), 0, RecvProxy_MoveCollide),
		RecvPropQAngles (FIELD.OF(nameof(Rotation))),
		RecvPropInt( FIELD.OF(nameof( TextureFrameIndex) )),
		RecvPropDataTable( "predictable_id", DT_PredictableId ),
		RecvPropInt(FIELD.OF(nameof(SimulatedEveryTick)), 0, RecvProxy_InterpolationAmountChanged),
		RecvPropInt(FIELD.OF(nameof(AnimatedEveryTick)), 0, RecvProxy_InterpolationAmountChanged),
		RecvPropBool( FIELD.OF(nameof( AlternateSorting ))),

		RecvPropDataTable("m_Collision", FIELD.OF(nameof(Collision)), CollisionProperty.DT_CollisionProperty, 0, RECV_GET_OBJECT_AT_FIELD(FIELD.OF(nameof(Collision)))),

		// gmod specific
		RecvPropInt(FIELD.OF(nameof(m_takedamage))),
		RecvPropInt(FIELD.OF(nameof(RealClassName))),

		RecvPropInt(FIELD.OF(nameof(OverrideMaterial))),

		RecvPropInt(FIELD.OF_ARRAYINDEX(nameof(OverrideSubMaterials), 0), PropFlags.Unsigned),
		RecvPropArray2(null, 32, "m_OverrideSubMaterials"),

		RecvPropInt(FIELD.OF(nameof(Health))),
		RecvPropInt(FIELD.OF(nameof(MaxHealth))),
		RecvPropInt(FIELD.OF(nameof(SpawnFlags))),
		RecvPropInt(FIELD.OF(nameof(GModFlags))),
		RecvPropBool(FIELD.OF(nameof(OnFire))),
		RecvPropFloat(FIELD.OF(nameof(CreationTime))),

		RecvPropFloat(FIELD.OF_VECTORELEM(nameof(Velocity), 0)),
		RecvPropFloat(FIELD.OF_VECTORELEM(nameof(Velocity), 1)),
		RecvPropFloat(FIELD.OF_VECTORELEM(nameof(Velocity), 2)),

		// NW2 table
		RecvPropGModTable(FIELD.OF(nameof(GMOD_DataTable))),

		// Addon exposed data tables
		RecvPropArray3(FIELD.OF_ARRAY(nameof(GMOD_bool)), RecvPropBool(FIELD.OF_ARRAYINDEX(nameof(GMOD_bool), 0))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(GMOD_float)), RecvPropFloat(FIELD.OF_ARRAYINDEX(nameof(GMOD_float), 0))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(GMOD_int)), RecvPropInt(FIELD.OF_ARRAYINDEX(nameof(GMOD_int), 0))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(GMOD_Vector)), RecvPropVector(FIELD.OF_ARRAYINDEX(nameof(GMOD_Vector), 0))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(GMOD_QAngle)), RecvPropQAngles(FIELD.OF_ARRAYINDEX(nameof(GMOD_QAngle), 0))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(GMOD_EHANDLE)), RecvPropEHandle(FIELD.OF_ARRAYINDEX(nameof(GMOD_EHANDLE), 0))),
		RecvPropString(FIELD.OF(nameof(GMOD_String0))),
		RecvPropString(FIELD.OF(nameof(GMOD_String1))),
		RecvPropString(FIELD.OF(nameof(GMOD_String2))),
		RecvPropString(FIELD.OF(nameof(GMOD_String3))),

		// Creation IDs
		RecvPropInt(FIELD.OF(nameof(CreationID))),
		RecvPropInt(FIELD.OF(nameof(MapCreatedID))),
	]);

	private static void RecvProxy_MoveCollide(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		((C_BaseEntity)instance).SetMoveCollide((MoveCollide)data.Value.Int);
	}

	private static void RecvProxy_MoveType(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		((C_BaseEntity)instance).SetMoveType((MoveType)data.Value.Int);
	}

	public void SetCollisionBounds(in Vector3 mins, in Vector3 maxs) => CollisionProp().SetCollisionBounds(in mins, in maxs);

	public static readonly ClientClass ClientClass = new ClientClass(null, null, DT_BaseEntity);
	const float coordTolerance = 2.0f / (float)(1 << (int)COORD_FRACTIONAL_BITS);

	public float GetGravity() => Gravity;
	public void SetGravity(float gravity) => Gravity = gravity;


	public static readonly DataMap PredMap = new(typeof(C_BaseEntity), [
		DEFINE.PRED_TYPEDESCRIPTION( nameof(Collision), CollisionProperty.PredMap ),
		DEFINE.PRED_FIELD(nameof(MoveType), FieldType.Character, FieldTypeDescFlags.InSendTable),
		DEFINE.PRED_FIELD(nameof(MoveCollide), FieldType.Character, FieldTypeDescFlags.InSendTable),
		DEFINE.FIELD(nameof(AbsVelocity), FieldType.Vector),
		DEFINE.PRED_FIELD_TOL(nameof(Velocity), FieldType.Vector, FieldTypeDescFlags.InSendTable, 0.5f),
		DEFINE.PRED_FIELD(nameof(RenderMode), FieldType.Character, FieldTypeDescFlags.InSendTable ),
		DEFINE.PRED_FIELD(nameof(RenderFX), FieldType.Character, FieldTypeDescFlags.InSendTable ),
		DEFINE.PRED_FIELD(nameof(flags), FieldType.Integer, FieldTypeDescFlags.InSendTable ),
		DEFINE.PRED_FIELD_TOL(nameof(ViewOffset), FieldType.Vector, FieldTypeDescFlags.InSendTable, 0.25f ),
		DEFINE.PRED_FIELD(nameof(ModelIndex), FieldType.Short, FieldTypeDescFlags.InSendTable | FieldTypeDescFlags.ModelIndex ),
		DEFINE.PRED_FIELD(nameof(Friction), FieldType.Float, FieldTypeDescFlags.InSendTable ),
		DEFINE.PRED_FIELD(nameof(TeamNum), FieldType.Integer, FieldTypeDescFlags.InSendTable ),
		DEFINE.PRED_FIELD(nameof(OwnerEntity), FieldType.EHandle, FieldTypeDescFlags.InSendTable ),
		DEFINE.PRED_FIELD(nameof(NetworkMoveParent), FieldType.EHandle, FieldTypeDescFlags.InSendTable ),
		DEFINE.PRED_FIELD_TOL(nameof(NetworkOrigin), FieldType.Vector, FieldTypeDescFlags.InSendTable, coordTolerance ),
		DEFINE.PRED_FIELD(nameof(NetworkAngles), FieldType.Vector, FieldTypeDescFlags.InSendTable | FieldTypeDescFlags.NoErrorCheck ),
		DEFINE.FIELD(nameof(AbsOrigin), FieldType.Vector ),
		DEFINE.FIELD(nameof(AbsRotation), FieldType.Vector ),
		DEFINE.FIELD(nameof(Origin), FieldType.Vector ),
		DEFINE.FIELD(nameof(Rotation), FieldType.Vector ),
		DEFINE.FIELD(nameof(WaterLevel), FieldType.Character ),
		DEFINE.FIELD(nameof(WaterType), FieldType.Character ),
		DEFINE.FIELD(nameof(AngVelocity), FieldType.Vector ),
		DEFINE.FIELD(nameof(Dormant), FieldType.Boolean ),
		DEFINE.FIELD(nameof(BaseVelocity), FieldType.Vector ),
		DEFINE.FIELD(nameof(eflags), FieldType.Integer ),
		DEFINE.FIELD(nameof(Gravity), FieldType.Float ),
		DEFINE.FIELD(nameof(ProxyRandomValue), FieldType.Float ),
	]); public virtual DataMap? GetPredDescMap() => PredMap;


	static readonly DynamicAccessor DA_Origin = FIELD.OF(nameof(Origin));
	static readonly DynamicAccessor DA_Rotation = FIELD.OF(nameof(Rotation));

	public float Gravity;

	public C_BaseEntity() {
		AddVar(this, DA_Origin, IV_Origin, LatchFlags.LatchSimulationVar);
		AddVar(this, DA_Rotation, IV_Rotation, LatchFlags.LatchSimulationVar);

		DataChangeEventRef.Struct = unchecked((ulong)-1);
		EntClientFlags = 0;

		ColorRender = new(255, 255, 255, 255);
		RenderFXBlend = 255;
		Predictable = false;

		SimulatedEveryTick = false;
		AnimatedEveryTick = false;

		ReadyToDraw = true;
		PredictionContext = null;
		Clear();
#if GMOD_DLL
		GMOD_DataTable = engine.GMOD_CreateDataTable(Game.Client.GarrysMod.LuaDataTable.GMOD_DataTableRecvProxy);
#endif
	}

	public virtual bool IsTwoPass() => modelinfo.IsTranslucentTwoPass(GetModel());

	public int Index;

	private Model? Model;
	ModelInstanceHandle_t ModelInstance;

	[NetworkName("m_flAnimTime")]
	public TimeUnit_t AnimTime;
	public TimeUnit_t OldAnimTime;

	[NetworkName("m_flSimulationTime")]
	public TimeUnit_t SimulationTime;
	public TimeUnit_t OldSimulationTime;

	public TimeUnit_t CreateTime;

	[NetworkName("m_ubInterpolationFrame")]
	public byte InterpolationFrame;
	public byte OldInterpolationFrame;
	[NetworkName("m_nModelIndex")]
	public int ModelIndex;
	public byte OldParentAttachment;
	[NetworkName("m_iParentAttachment")]
	public byte ParentAttachment;

	[NetworkName("movetype")]
	public byte MoveType;
	[NetworkName("movecollide")]
	public byte MoveCollide;
	[NetworkName("m_iTextureFrameIndex")]
	public bool TextureFrameIndex;
	[NetworkName("m_bSimulatedEveryTick")]
	public bool SimulatedEveryTick;
	[NetworkName("m_bAnimatedEveryTick")]
	public bool AnimatedEveryTick;
	[NetworkName("m_bAlternateSorting")]
	public bool AlternateSorting;

	[NetworkName("m_PredictableID")]
	public PredictableId PredictableID = new();

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
	public int MaxHealth;
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
	[NetworkName("m_flFriction")]
	public float Friction;

	[NetworkName("m_Collision")]
	public CollisionProperty Collision = new();

	[NetworkName("m_fEffects")]
	public int Effects;
	[NetworkName("m_nRenderMode")]
	public byte RenderMode;
	[NetworkName("m_nRenderFX")]
	public byte RenderFX;
	public byte RenderFXBlend;
	[NetworkName("m_clrRender")]
	public Color ColorRender;
	[NetworkName("m_CollisionGroup")]
	public int CollisionGroup;
	[NetworkName("m_flElasticity")]
	public float Elasticity;
	[NetworkName("m_flShadowCastDistance")]
	public float ShadowCastDistance;
	public byte OldRenderMode;

	[NetworkName("m_GMOD_bool")]
	public InlineArray32<bool> GMOD_bool;
	[NetworkName("m_GMOD_float")]
	public InlineArray32<float> GMOD_float;
	[NetworkName("m_GMOD_int")]
	public InlineArray32<int> GMOD_int;
	[NetworkName("m_GMOD_Vector")]
	public InlineArray32<Vector3> GMOD_Vector;
	[NetworkName("m_GMOD_QAngle")]
	public InlineArray32<QAngle> GMOD_QAngle;
	[NetworkName("m_GMOD_EHANDLE")]
	public InlineArrayNew32<EHANDLE> GMOD_EHANDLE = new();
	[NetworkName("m_GMOD_String0")]
	public InlineArray512<char> GMOD_String0;
	[NetworkName("m_GMOD_String1")]
	public InlineArray512<char> GMOD_String1;
	[NetworkName("m_GMOD_String2")]
	public InlineArray512<char> GMOD_String2;
	[NetworkName("m_GMOD_String3")]
	public InlineArray512<char> GMOD_String3;

	[NetworkName("m_GMOD_DataTable")]
	public readonly IGMODDataTable GMOD_DataTable;

	[NetworkName("m_nSpeed")]
	public int Speed;
	[NetworkName("m_iTeamNum")]
	public int TeamNum;
	public int GetTeamNumber() => TeamNum;

	IPhysicsObject? PhysicsObject = null!;
	public void VPhysicsUpdate(IPhysicsObject physics) { }
	public IPhysicsObject? VPhysicsGetObject() => PhysicsObject;
	public int VPhysicsGetObjectList(Span<IPhysicsObject> list) => throw new NotImplementedException();

	public bool IsFloating() => false;

	[NetworkName("m_hOwnerEntity")]
	public EHANDLE OwnerEntity = new();
	public C_BaseEntity? GetOwnerEntity() => (C_BaseEntity?)OwnerEntity.Get();
	[NetworkName("m_hEffectEntity")]
	public EHANDLE EffectEntity = new();
	[NetworkName("m_hGroundEntity")]
	public EHANDLE GroundEntity = new();
	[NetworkName("moveparent")]
	public EHANDLE NetworkMoveParent = new();
	public EHANDLE OldMoveParent = new();
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

	public long CreationTick;

	public bool OldShouldDraw;

	public Vector3 AbsOrigin;
	public QAngle AbsRotation;
	[NetworkName("m_vecViewOffset")]
	public Vector3 ViewOffset;
	public Vector3 OldOrigin;
	public QAngle OldRotation;
	public Vector3 NetworkOrigin;
	public QAngle NetworkAngles;

	public EntClientFlags EntClientFlags;

	public Source.InlineArray4<float> RenderingClipPlane;
	public bool EnableRenderingClipPlane;

	[NetworkName("m_vecOrigin")]
	public Vector3 Origin;
	public readonly InterpolatedVar<Vector3> IV_Origin = new("Origin");
	[NetworkName("m_angRotation")]
	public QAngle Rotation;
	public readonly InterpolatedVar<QAngle> IV_Rotation = new("Rotation");


	public Handle<C_BasePlayer> PlayerSimulationOwner = new();
	public readonly ReusableBox<ulong> DataChangeEventRef = new();

	public static C_BaseEntity? Instance(BaseHandle handle) => cl_entitylist.GetBaseEntityFromHandle(handle);
	public static C_BaseEntity? Instance(int ent) => cl_entitylist.GetBaseEntity(ent);

	public static bool FClassnameIs(C_BaseEntity? entity, ReadOnlySpan<char> classname) {
		if (entity == null)
			return false;

		return 0 == strcmp(entity.GetClassname(), classname);
	}

	public void SetParent(C_BaseEntity parentEntity, int parentAttachment = 0) {
		EHANDLE newParentHandle = default;
		newParentHandle.Set(parentEntity);
		if (newParentHandle.Index == MoveParent.Index)
			return;

		// NOTE: Have to do this before the unlink to ensure local coords are valid
		Vector3 vecAbsOrigin = GetAbsOrigin();
		QAngle angAbsRotation = GetAbsAngles();
		Vector3 vecAbsVelocity = GetAbsVelocity();

		// First deal with unlinking
		if (MoveParent.IsValid())
			UnlinkChild(MoveParent.Get(), this);

		if (parentEntity != null)
			LinkChild(parentEntity, this);

		if (!IsServerEntity())
			NetworkMoveParent.Set(parentEntity);

		ParentAttachment = (byte)parentAttachment;

		AbsOrigin.Init(float.MaxValue, float.MaxValue, float.MaxValue);
		AbsRotation.Init(float.MaxValue, float.MaxValue, float.MaxValue);
		AbsVelocity.Init(float.MaxValue, float.MaxValue, float.MaxValue);

		SetAbsOrigin(vecAbsOrigin);
		SetAbsAngles(angAbsRotation);
		SetAbsVelocity(vecAbsVelocity);
	}

	public void SetOwnerEntity(C_BaseEntity? owner) => OwnerEntity.Set(owner);

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetSolidFlags(SolidFlags flags) => CollisionProp().SetSolidFlags(flags);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsSolidFlagSet(SolidFlags flagMask) => CollisionProp().IsSolidFlagSet(flagMask);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public SolidFlags GetSolidFlags() => (SolidFlags)CollisionProp().GetSolidFlags();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void AddSolidFlags(SolidFlags flags) => CollisionProp().AddSolidFlags(flags);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void RemoveSolidFlags(SolidFlags flags) => CollisionProp().RemoveSolidFlags(flags);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsSolid() => CollisionProp().IsSolid();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetSolid(SolidType val) => CollisionProp().SetSolid(val);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public SolidType GetSolid() => CollisionProp().GetSolid();

	public int GetFxBlend() => RenderFXBlend;
	public void GetColorModulation(Span<float> color) {
		color[0] = ColorRender.R / 255f;
		color[1] = ColorRender.G / 255f;
		color[2] = ColorRender.B / 255f;
	}
	public virtual void ClientThink() { }

	public bool ReadyToDraw;
	public virtual int DrawModel(StudioFlags flags) {
		if (!ReadyToDraw)
			return 0;
		int drawn = 0;
		if (Model == null)
			return drawn;

		switch (Model.Type) {
			case ModelType.Brush:
				drawn = DrawBrushModel((flags & StudioFlags.Transparency) != 0, flags, (flags & StudioFlags.TwoPass) != 0);
				break;
			case ModelType.Studio:
				Warning($"ERROR:  Can't draw studio model {modelinfo.GetModelName(Model)} because {GetClientClass().NetworkName ?? "unknown"} is not derived from C_BaseAnimating\n");
				break;
			case ModelType.Sprite:
				// Warning("ERROR:  Sprite model's not supported any more except in legacy temp ents\n");
				break;
		}

		DrawBBoxVisualizations();

		return drawn;
	}

	public virtual bool SetupBones(Span<Matrix3x4> boneToWorldOut, int maxBones, int boneMask, TimeUnit_t currentTime) {
		return true;
	}
	public virtual void SetupWeights(Span<Matrix3x4> boneToWorldOut, Span<float> flexWeights, Span<float> flexDelayedWeights) {

	}
	public virtual void DoAnimationEvents() {

	}

	public CollisionProperty CollisionProp() => Collision;

	static readonly ConVar r_drawrenderboxes = new("r_drawrenderboxes", "0", FCvar.Cheat);

#if DEBUG
	internal static readonly ConVar sdn_entdebug = new("sdn_entdebug", "0");

	internal static void DrawEntityDebugOverlays() {
		if (!sdn_entdebug.GetBool())
			return;

		Span<char> text = stackalloc char[256];
		int highest = cl_entitylist.GetHighestEntityIndex();
		for (int i = 0; i <= highest; i++) {
			C_BaseEntity? ent = cl_entitylist.GetBaseEntity(i);
			if (ent == null)
				continue;

			text.Clear();

			ref readonly Vector3 origin = ref ent.GetAbsOrigin();
			sprintf(text, "[%d] %s midx=%d model=%s (%d %d %d)%s")
				.D(i).S(ent.GetClassname()).D(ent.ModelIndex).S(ent.Model == null ? "NULL" : "ok")
				.D((int)origin.X).D((int)origin.Y).D((int)origin.Z)
				.S(ent.ShouldDraw() ? "" : ent.IsEffectActive(EntityEffects.NoDraw) ? " EF_NODRAW" : " NODRAW");

			debugoverlay.AddTextOverlay(in origin, i & 14, 0, text.SliceNullTerminatedString());
		}
	}
#endif

	public void DrawBBoxVisualizations() {
		if (r_drawrenderboxes.GetInt() != 0) {
			GetRenderBounds(out Vector3 vecRenderMins, out Vector3 vecRenderMaxs);
			debugoverlay.AddBoxOverlay(GetRenderOrigin(), vecRenderMins, vecRenderMaxs, GetRenderAngles(), 255, 0, 255, 0, 0.01f);
		}
	}

	private int DrawBrushModel(bool drawingTranslucency, StudioFlags flags, bool twoPass) {
		Assert(modelinfo.GetModelType(Model) == ModelType.Brush);

		RenderDepthMode depthMode = RenderDepthMode.Normal;
		if ((flags & StudioFlags.SSAODepthTexture) != 0)
			depthMode = RenderDepthMode.SSAO;
		else if ((flags & StudioFlags.ShadowDepthTexture) != 0)
			depthMode = RenderDepthMode.Shadow;

		if (depthMode != RenderDepthMode.Normal) {
			// todo
		}
		else
			render.DrawBrushModel(GetIClientEntity(), Model!, GetAbsOrigin(), GetAbsAngles());

		return 1;
	}

	public ref readonly Vector3 GetLocalOrigin() => ref Origin;
	public ref readonly QAngle GetLocalAngles() => ref Rotation;
	public ref readonly Vector3 GetAbsOrigin() {
		CalcAbsolutePosition();
		return ref AbsOrigin;
	}
	public ref readonly QAngle GetAbsAngles() {
		CalcAbsolutePosition();
		return ref AbsRotation;
	}
	public ref readonly Vector3 GetViewOffset() => ref ViewOffset;

	public virtual Source.Common.Audio.MouthInfo? GetMouth() => null;

	public virtual bool GetSoundSpatialization(ref SpatializationInfo info) {
		if (EntIndex() == 0)
			return true;

		if (IsDormant())
			return false;

		Model? model = GetModel();

		if (!Unsafe.IsNullRef(ref info.Radius))
			info.Radius = modelinfo.GetModelRadius(model);

		if (!Unsafe.IsNullRef(ref info.Origin)) {
			info.Origin = GetAbsOrigin();

			if (modelinfo.GetModelType(model) == ModelType.Brush) {
				modelinfo.GetModelBounds(model, out Vector3 mins, out Vector3 maxs);
				MathLib.VectorAdd(mins, maxs, out Vector3 center);
				MathLib.VectorScale(center, 0.5f, out center);

				info.Origin += center;
			}
		}

		if (!Unsafe.IsNullRef(ref info.Angles))
			info.Angles = GetAbsAngles();

		return true;
	}

	public virtual ClientClass GetClientClass() => ClientClassRetriever.GetOrError(GetType());


	public IClientNetworkable GetClientNetworkable() => this;
	public IClientRenderable GetClientRenderable() => this;
	public IClientThinkable GetClientThinkable() => this;
	public IClientEntity GetIClientEntity() => this;
	public IClientUnknown GetIClientUnknown() => this;


	public Model? GetModel() => Model;
	public virtual void ValidateModelIndex() {
		SetModelByIndex(ModelIndex);
	}
	public void SetModelPointer(Model? model) {
		if (model != Model) {
			DestroyModelInstance();
			Model = model;
			OnNewModel();

			UpdateVisibility();
		}
	}
	protected virtual StudioHdr? OnNewModel() {
		return null; // what the hell????????????????? 
	}
	void SetModelIndex(int index) {
		ModelIndex = index;
		Model? model = modelinfo.GetModel(ModelIndex);
		SetModelPointer(model);
	}
	public void SetModelByIndex(int modelIndex) {
		SetModelIndex(modelIndex);
	}
	public bool SetModel(ReadOnlySpan<char> modelName) {
		if (!modelName.IsEmpty) {
			int modelIndex = modelinfo.GetModelIndex(modelName);
			SetModelByIndex(modelIndex);
			return modelIndex != -1;
		}
		else {
			SetModelByIndex(-1);
			return false;
		}
	}

	public virtual ref readonly Vector3 GetRenderOrigin() => ref GetAbsOrigin();
	public virtual ref readonly QAngle GetRenderAngles() => ref GetAbsAngles();
	public void GetRenderBounds(out Vector3 mins, out Vector3 maxs) {
		ModelType nModelType = modelinfo.GetModelType(Model);
		if (nModelType == ModelType.Studio || nModelType == ModelType.Brush)
			modelinfo.GetModelRenderBounds(GetModel(), out mins, out maxs);
		else {
			// todo
			mins = maxs = default;
		}
	}

	public void GetRenderBoundsWorldspace(out Vector3 mins, out Vector3 maxs) {
		ClientLeafSystem.DefaultRenderBoundsWorldspace(this, out mins, out maxs);
	}

	public virtual bool IsTransparent() {
		return modelinfo.IsTranslucent(Model) || RenderMode != (int)Source.RenderMode.Normal;
	}

	public virtual void UpdateOnRemove() {
		VPhysicsDestroyObject();

		Assert(GetMoveParent() == null);
		UnlinkFromHierarchy();
		SetGroundEntity(null);
	}

	public EHANDLE MoveParent = new();
	public EHANDLE MoveChild = new();
	public EHANDLE MovePeer = new();
	public EHANDLE MovePrevPeer = new();

	public void UnlinkFromHierarchy() {
		if (MoveParent.IsValid())
			UnlinkChild(MoveParent.Get(), this);
	}

	public void HierarchySetParent(C_BaseEntity? newParent) {
		EHANDLE newParentHandle = default;
		newParentHandle.Set(newParent);
		if (newParentHandle.Index == MoveParent.Index)
			return;

		if (MoveParent.IsValid())
			UnlinkChild(MoveParent.Get(), this);

		if (newParent != null)
			LinkChild(newParent, this);

		InvalidatePhysicsRecursive(InvalidatePhysicsBits.PositionChanged | InvalidatePhysicsBits.AnglesChanged | InvalidatePhysicsBits.VelocityChanged);
	}

	public void LinkChild(C_BaseEntity parent, C_BaseEntity child) {
		Assert(!child.MovePeer.IsValid());
		Assert(!child.MovePrevPeer.IsValid());
		Assert(!child.MoveParent.IsValid());
		Assert(parent != child);

#if DEBUG
		// Make sure the child isn't already in this list
		C_BaseEntity? existingChild;
		for (existingChild = parent.FirstMoveChild(); existingChild != null; existingChild = existingChild.NextMovePeer())
			Assert(child != existingChild);
#endif

		child.MovePrevPeer.Set(null);
		child.MovePeer.Set(parent.MoveChild);
		if (child.MovePeer.Get() != null)
			child.MovePeer.Get()!.MovePrevPeer.Set(child);

		parent.MoveChild.Set(child);
		child.MoveParent.Set(parent);
		child.AddToAimEntsList();

		Interp_HierarchyUpdateInterpolationAmounts();
	}
	public void UnlinkChild(C_BaseEntity? parent, C_BaseEntity? child) {
		Assert(child != null);
		Assert(parent != child);
		Assert(child.GetMoveParent() == parent);

		if (parent != null && (parent.MoveChild.Get() == child)) {
			Assert(!(child.MovePrevPeer.IsValid()));
			parent.MoveChild.Set(child.MovePeer);
		}

		if (child.MovePrevPeer.IsValid())
			child.MovePrevPeer.Get()!.MovePeer.Set(child.MovePeer.Get());

		if (child.MovePeer.IsValid())
			child.MovePeer.Get()!.MovePrevPeer.Set(child.MovePrevPeer.Get());

		child.MovePeer.Set(null);
		child.MovePrevPeer.Set(null);
		child.MoveParent.Set(null);
		child.RemoveFromAimEntsList();

		Interp_HierarchyUpdateInterpolationAmounts();
	}

	static readonly List<C_BaseEntity> g_AimEntsList = [];

	AimEntsListHandle_t AimEntsListHandle;

	const AimEntsListHandle_t INVALID_AIMENTS_LIST_HANDLE = unchecked((AimEntsListHandle_t)~0);

	public void AddToAimEntsList() {
		if (AimEntsListHandle != INVALID_AIMENTS_LIST_HANDLE)
			return;

		AimEntsListHandle = g_AimEntsList.Count;
		g_AimEntsList.Add(this);
	}

	public void RemoveFromAimEntsList() {
		if (AimEntsListHandle == INVALID_AIMENTS_LIST_HANDLE)
			return;

		int c = g_AimEntsList.Count;

		Assert(AimEntsListHandle < c);

		var last = c - 1;

		if (last == AimEntsListHandle) {
			// Just wipe the final entry
			g_AimEntsList.RemoveAt(last);
		}
		else {
			C_BaseEntity lastEntity = g_AimEntsList[last];
			// Remove the last entry
			g_AimEntsList.RemoveAt(last);

			// And update it's handle to point to this slot.
			lastEntity.AimEntsListHandle = AimEntsListHandle;
			g_AimEntsList[AimEntsListHandle] = lastEntity;
		}

		// Invalidate our handle no matter what.
		AimEntsListHandle = INVALID_AIMENTS_LIST_HANDLE;
	}

	public virtual void Release() {
		using (C_BaseAnimating.AutoAllowBoneAccess boneaccess = new(true, true))
			UnlinkFromHierarchy();

		if (IsIntermediateDataAllocated())
			DestroyIntermediateData();

		UpdateOnRemove();
		Term();
	}

	public int DataObjectTypes;

	/// <summary>
	/// The equiv of the dtor (kinda...)
	/// see C_BaseEntity::~C_BaseEntity() etc
	/// </summary>
	public virtual void Term() {
		DestroyAllDataObjects();

		if (GetPredictable() || IsClientCreated())
			g_Predictables.RemoveFromPredictablesList(GetClientHandle());

		if (IsPlayerSimulated() && C_BasePlayer.GetLocalPlayer() != null)
			C_BasePlayer.GetLocalPlayer()!.RemoveFromPlayerSimulationList(this);

		if (GetClientHandle() != INVALID_CLIENTENTITY_HANDLE) {
			if (GetThinkHandle() != INVALID_THINK_HANDLE)
				ClientThinkList().RemoveThinkable(GetClientHandle());

			// Remove from the client entity list.
			cl_entitylist.RemoveEntity(GetClientHandle());

			RefEHandle = INVALID_CLIENTENTITY_HANDLE;
		}

		CollisionProp().DestroyPartitionHandle();

		if (Index != -1)
			beams.KillDeadBeams(this);

		DestroyModelInstance();
		RemoveFromLeafSystem();
		RemoveFromAimEntsList();

		LuaEntityObject?.UnReference();
		LuaEntityObject = null;
		LuaTableObject?.UnReference();
		LuaTableObject = null;
		LuaCalcAbsolutePosition.UnReference();
	}

	public bool OnPredictedEntityRemove(bool isbeingremoved, C_BaseEntity predicted) {
		PredictionContext? ctx = predicted.PredictionContext;
		Assert(ctx != null);
		if (ctx != null) {
			// Create backlink to actual entity
			ctx.ServerEntity.Set(this);
		}

		// If it comes through with an ID, it should be eligible
		SetPredictionEligible(true);

		// Start predicting simulation forward from here
		CheckInitPredictable("OnPredictedEntityRemove");

		// Always mark it dormant since we are the "real" entity now
		predicted.SetDormantPredictable(true);

		InvalidatePhysicsRecursive(InvalidatePhysicsBits.PositionChanged | InvalidatePhysicsBits.AnglesChanged | InvalidatePhysicsBits.VelocityChanged);

		// By default, signal that it should be deleted right away
		// If a derived class implements this method, it might chain to here but return
		// false if it wants to keep the dormant predictable around until the chain of
		//  DATA_UPDATE_CREATED messages passes
		return true;
	}

	public virtual bool ShouldPredict() => false;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int GetModelIndex() => ModelIndex;

	public static int PrecacheModel(ReadOnlySpan<char> name) => modelinfo.GetModelIndex(name);

	public void OnPostRestoreData() {
		InvalidatePhysicsRecursive(InvalidatePhysicsBits.PositionChanged | InvalidatePhysicsBits.AnglesChanged | InvalidatePhysicsBits.VelocityChanged);

		if (GetMoveParent() != null)
			AddToAimEntsList();

		if (GetModel() != modelinfo.GetModel(GetModelIndex()))
			SetModelByIndex(GetModelIndex());
	}

	public void CheckInitPredictable(ReadOnlySpan<char> context) {
		if (cl_predict.GetInt() == 0)
			return;

		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();

		if (player == null)
			return;

		if (!GetPredictionEligible()) {
			if (PredictableID.IsActive() && (player.Index - 1) == PredictableID.GetPlayer())
				SetPredictionEligible(true);
			else
				return;

		}

		if (IsClientCreated())
			return;

		if (!ShouldPredict())
			return;

		if (IsIntermediateDataAllocated())
			return;

		// Msg( "Predicting init %s at %s\n", GetClassname(), context );

		InitPredictable();
	}

	bool PredictionEligible;
	public long SimulationTick;
	public bool GetPredictionEligible() => PredictionEligible;
	public void SetPredictionEligible(bool canpredict) => PredictionEligible = canpredict;


	public virtual bool ShouldDraw() {
		if ((RenderMode)RenderMode == Source.RenderMode.None)
			return false;

		return Model != null && !IsEffectActive(EntityEffects.NoDraw) && Index != 0;
	}

	public virtual bool Init(int entNum, int serialNum) {
		Index = entNum;
		cl_entitylist.AddNetworkableEntity(GetIClientUnknown(), entNum, serialNum);
		CollisionProp().CreatePartitionHandle();
		Interp_SetupMappings(ref GetVarMapping());
		CreationTick = gpGlobals.TickCount;
		return true;
	}

	public virtual EntityCapabilities ObjectCaps() => 0;

	public virtual void Dispose() {
		GC.SuppressFinalize(this);
	}

	double flSpawnTime;
	double LastMessageTime;

	public void MoveToLastReceivedPosition(bool force = false) {
		if (force || (RenderFx)RenderFX != RenderFx.Ragdoll) {
			SetLocalOrigin(GetNetworkOrigin());
			SetLocalAngles(GetNetworkAngles());
		}
	}
	public virtual void PreDataUpdate(DataUpdateType updateType) {
		if (HLClient.AddDataChangeEvent(this, updateType, DataChangeEventRef))
			OnPreDataChanged(updateType);

		bool newentity = updateType == DataUpdateType.Created;

		if (!newentity)
			Interp_RestoreToLastNetworked(ref GetVarMapping());

		if (newentity && !IsClientCreated()) {
			flSpawnTime = engine.GetLastTimeStamp();
			Spawn();
		}

		OldOrigin = GetNetworkOrigin();
		OldRotation = GetNetworkAngles();
		OldAnimTime = AnimTime;
		OldSimulationTime = SimulationTime;

		OldRenderMode = RenderMode;

		clientLeafSystem.EnableAlternateSorting(renderHandle, AlternateSorting);

		OldInterpolationFrame = InterpolationFrame;
		OldShouldDraw = ShouldDraw();
	}

	public virtual void PostDataUpdate(DataUpdateType updateType) {
		if ((RenderFx)RenderFX == RenderFx.Ragdoll && updateType == DataUpdateType.Created)
			MoveToLastReceivedPosition(true);
		else
			MoveToLastReceivedPosition(false);

		if (Index == 0) {
			ModelIndex = 1;
			SetSolid(SolidType.BSP);
		}

		if (OldRenderMode != RenderMode)
			SetRenderMode((RenderMode)RenderMode, true);

		bool animTimeChanged = AnimTime != OldAnimTime;
		bool originChanged = OldOrigin != GetLocalOrigin();
		bool anglesChanged = OldRotation != GetLocalAngles();
		bool simTimeChanged = SimulationTime != OldSimulationTime;

		// Detect simulation changes 
		bool simulationChanged = originChanged || anglesChanged || simTimeChanged;

		bool predictable = GetPredictable();

		if (!predictable && !IsClientCreated()) {
			if (animTimeChanged)
				OnLatchInterpolatedVariables(LatchFlags.LatchAnimationVar);

			if (simulationChanged)
				OnLatchInterpolatedVariables(LatchFlags.LatchSimulationVar);
		}
		else if (predictable)
			OnStoreLastNetworkedValue();

		Assert(NetworkMoveParent.Get() != null || !NetworkMoveParent.IsValid());
		HierarchySetParent(NetworkMoveParent.Get());

		MarkMessageReceived();

		ValidateModelIndex();

		if (updateType == DataUpdateType.Created) {
			ProxyRandomValue = Random.Shared.NextSingle();
			ResetLatched();
			CreationTick = gpGlobals.TickCount;
		}

		CheckInitPredictable("PostDataUpdate");
		C_BasePlayer? local = C_BasePlayer.GetLocalPlayer();
		if (IsPlayerSimulated() && (null != local) && (local == OwnerEntity.Get()))
			// Make sure player is driving simulation (field is only ever sent to local player)
			SetPlayerSimulated(local);

		// TODO: Partition/leaf stuff

		if (!IsClientCreated())
			if (Teleported() || IsNoInterpolationFrame())
				AddToTeleportList();

		if (OldMoveParent != NetworkMoveParent)
			UpdateVisibility();
		if (OldShouldDraw != ShouldDraw())
			UpdateVisibility();
	}
	public ReadOnlySpan<char> GetModelName() => ModelName.SliceNullTerminatedString();
	public void AllocateIntermediateData() {
		if (OriginalData != null)
			return;

		ComputePackedOffsets();

		OriginalData = new byte[GetPredDescMap()!.PackedSize];
		for (int i = 0; i < MULTIPLAYER_BACKUP; i++)
			IntermediateData[i] = new byte[GetPredDescMap()!.PackedSize];

		IntermediateDataCount = 0;
	}

	public virtual bool PostNetworkDataReceived(int commandsAcknowledged) {
		bool haderrors = false;

		Assert(GetPredictable());

		bool errorcheck = (commandsAcknowledged > 0) ? true : false;

		// Store network data into post networking pristine state slot (slot 64) 
		SaveData("PostNetworkDataReceived", SLOT_ORIGINALDATA, PredictionCopyType.Everything);

		// Show any networked fields that are different
		bool showthis = cl_showerror.GetInt() >= 2;

		if (cl_showerror.GetInt() < 0)
			showthis = EntIndex() == -cl_showerror.GetInt();

		if (errorcheck) {
			byte[]? predictedStateData = GetPredictedFrame(commandsAcknowledged - 1);
			Assert(predictedStateData != null);
			byte[]? originalStateData = GetOriginalNetworkDataObject();
			Assert(originalStateData != null);

			bool counterrors = true;
			bool reporterrors = showthis;
			bool copydata = false;

			PredictionCopy errorCheckHelper = new(PredictionCopyType.NetworkedOnly, predictedStateData, originalStateData, counterrors, reporterrors, copydata);
			// Suppress debugging output
			int ecount = errorCheckHelper.TransferData("", -1, GetPredDescMap());
			if (ecount > 0) {
				haderrors = true;
				//	Msg( "%i errors %i on entity %i %s\n", gpGlobals->tickcount, ecount, index, IsClientCreated() ? "true" : "false" );
			}
		}

		return haderrors;
	}

	public void InitPredictable() {
		Assert(!GetPredictable());
		SetPredictable(true);
		// Allocate buffers into which we copy data
		AllocateIntermediateData();
		// Add to list of predictables
		g_Predictables.AddToPredictableList(GetClientHandle());
		// Copy everything from "this" into the original_state_data
		//  object.  Don't care about client local stuff, so pull from slot 0 which

		//  should be empty anyway...
		PostNetworkDataReceived(0);

		// Copy original data into all prediction slots, so we don't get an error saying we "mispredicted" any
		//  values which are still at their initial values
		for (int i = 0; i < MULTIPLAYER_BACKUP; i++)
			SaveData("InitPredictable", i, PredictionCopyType.Everything);
	}

	public EFL GetEFlags() => eflags;

	public int RestoreData(ReadOnlySpan<char> context, int slot, PredictionCopyType type) {
		byte[]? src = slot == SLOT_ORIGINALDATA ? GetOriginalNetworkDataObject() : GetPredictedFrame(slot);

		// This assert will fire if the server ack'd a CUserCmd which we hadn't predicted yet...
		// In that case, we'd be comparing "old" data from this "unused" slot with the networked data and reporting all kinds of prediction errors possibly.
		Assert(slot == SLOT_ORIGINALDATA || slot <= IntermediateDataCount);

		Span<char> sz = stackalloc char[64];

		// some flags shouldn't be predicted - as we find them, add them to the savedEFlagsMask
		const EFL savedEFlagsMask = EFL.DirtyShadowUpdate;
		EFL savedEFlags = GetEFlags() & savedEFlagsMask;

		// model index needs to be set manually for dynamic model refcounting purposes
		int oldModelIndex = ModelIndex;

		PredictionCopy copyHelper = new(type, this, src!);
		int error_count = copyHelper.TransferData(sz, EntIndex(), GetPredDescMap());

		// set non-predicting flags back to their prior state
		RemoveEFlags(savedEFlagsMask);
		AddEFlags(savedEFlags);

		// restore original model index and change via SetModelIndex
		int newModelIndex = ModelIndex;
		ModelIndex = oldModelIndex;
		int overrideModelIndex = CalcOverrideModelIndex();
		if (overrideModelIndex != -1)
			newModelIndex = overrideModelIndex;
		if (oldModelIndex != newModelIndex)
			SetModelIndex(newModelIndex);

		OnPostRestoreData();

		return error_count;
	}

	public virtual int CalcOverrideModelIndex() => -1;

	public int SaveData(ReadOnlySpan<char> context, int slot, PredictionCopyType type) {
		byte[]? dest = slot == SLOT_ORIGINALDATA ? GetOriginalNetworkDataObject() : GetPredictedFrame(slot);
		Span<char> sz = stackalloc char[64];

		if (slot != SLOT_ORIGINALDATA)
			// Remember high water mark so that we can detect below if we are reading from a slot not yet predicted into...
			IntermediateDataCount = slot;

		PredictionCopy copyHelper = new(type, dest!, this);
		int errorCount = copyHelper.TransferData(sz, EntIndex(), GetPredDescMap());
		return errorCount;
	}

	public bool IsIntermediateDataAllocated() {
		return OriginalData != null; // todo
	}

	public void DestroyIntermediateData() {
		if (OriginalData == null)
			return;

		for (int i = 0; i < MULTIPLAYER_BACKUP; i++)
			IntermediateData[i] = null!;

		OriginalData = null!;
		IntermediateDataCount = 0;
	}

	private void OnStoreLastNetworkedValue() {
		bool bRestore = false;
		Vector3 savePos = default;
		QAngle saveAng = default;

		if (RenderFX == (byte)RenderFx.Ragdoll && GetPredictable()) {
			bRestore = true;
			savePos = GetLocalOrigin();
			saveAng = GetLocalAngles();

			MoveToLastReceivedPosition(true);
		}

		int c = VarMap.Entries.Count;
		for (int i = 0; i < c; i++) {
			VarMapEntry e = VarMap.Entries[i];
			IInterpolatedVar watcher = e.Watcher;

			LatchFlags type = watcher.GetVarType();

			if ((type & LatchFlags.ExcludeAutoLatch) != 0)
				continue;

			watcher.NoteLastNetworkedValue();
		}

		if (bRestore) {
			SetLocalOrigin(savePos);
			SetLocalAngles(saveAng);
		}
	}

	float ProxyRandomValue;

	public virtual void SetRenderMode(RenderMode renderMode, bool forceUpdate) {
		RenderMode = (byte)renderMode;
	}

	protected void MarkMessageReceived() {
		LastMessageTime = engine.GetLastTimeStamp();
	}

	public void SetDormant(bool dormant) {
		Dormant = dormant;
		UpdateVisibility();
	}

	public ref readonly BaseHandle GetClientHandle() => ref RefEHandle;

	public bool InitializeAsClientEntity(ReadOnlySpan<char> modelName, RenderGroup renderGroup) {
		int modelIndex;

		if (!modelName.IsEmpty) {
			modelIndex = modelinfo.GetModelIndex(modelName);

			if (modelIndex == -1) {
				// Model could not be found
				AssertMsg(false, "Model could not be found, index is -1");
				return false;
			}
		}
		else
			modelIndex = -1;

		Interp_SetupMappings(ref GetVarMapping());
		return InitializeAsClientEntityByIndex(modelIndex, renderGroup);
	}

	public bool InitializeAsClientEntityByIndex(int index, RenderGroup renderGroup) {
		this.Index = -1;

		// Setup model data.
		SetModelByIndex(index);

		// Add the client entity to the master entity list.
		cl_entitylist.AddNonNetworkableEntity(GetIClientUnknown());
		Assert(GetClientHandle() != cl_entitylist.InvalidHandle());

		// Add the client entity to the renderable "leaf system." (Renderable)
		AddToLeafSystem(renderGroup);

		// Add the client entity to the spatial partition. (Collidable)
		CollisionProp().CreatePartitionHandle();

		SpawnClientEntity();

		return true;
	}


	// stubs on client
	public void NetworkStateManualMode(bool _) { }
	public void NetworkStateChanged() { }
	public void NetworkStateChanged(IFieldAccessor _) { }
	public void NetworkStateSetUpdateInterval(float _) { }
	public void NetworkStateForceUpdate() { }


	public static C_BaseEntity? CreatePredictedEntityByName(ReadOnlySpan<char> classname, [CallerFilePath] string? module = null, [CallerLineNumber] int line = -1, bool persist = false) {
		C_BasePlayer? player = C_BaseEntity.GetPredictionPlayer();

		Assert(player != null);
		Assert(!player.CurrentCommand.IsNull);
		Assert(prediction.InPrediction());

		C_BaseEntity? ent = null;

		// What's my birthday (should match server)
		int command_number = player.CurrentCommand.Get().CommandNumber;
		// Who's my daddy?
		int player_index = player.EntIndex() - 1;

		// Create id/context
		PredictableId testId = default;
		testId.Init(player_index, command_number, classname, module, line);

		// If repredicting, should be able to find the entity in the previously created list
		if (!prediction.IsFirstTimePredicted()) {
			// Only find previous instance if entity was created with persist set
			if (persist) {
				ent = FindPreviouslyCreatedEntity(testId);
				if (ent != null)
					return ent;
			}

			return null;
		}

		ent = CreateEntityByName(classname);
		if (ent == null)
			return null;

		// It's predictable
		ent.SetPredictionEligible(true);

		// Set up "shared" id number
		ent.PredictableID.SetRaw(testId.GetRaw());

		// Get a context (mostly for debugging purposes)
		PredictionContext context = new PredictionContext();
		context.Active = true;
		context.CreationCommandNumber = command_number;
		context.CreationLineNumber = line;
		context.CreationModule = new(module.SliceNullTerminatedString());

		// Attach to entity
		ent.PredictionContext = context;

		// Add to client entity list
		cl_entitylist.AddNonNetworkableEntity(ent);

		//  and predictables
		g_Predictables.AddToPredictableList(ent.GetClientHandle());

		// Duhhhh..., but might as well be safe
		Assert(!ent.GetPredictable());
		Assert(ent.IsClientCreated());

		// Add the client entity to the spatial partition. (Collidable)
		ent.CollisionProp().CreatePartitionHandle();

		// CLIENT ONLY FOR NOW!!!
		ent.Index = -1;

		if (HLClient.AddDataChangeEvent(ent, DataUpdateType.Created, ent.DataChangeEventRef))
			ent.OnPreDataChanged(DataUpdateType.Created);


		ent.Interp_UpdateInterpolationAmounts(ref ent.GetVarMapping());

		return ent;
	}

	public virtual void UpdateVisibility() {
		// todo: tools
		if (ShouldDraw() && !IsDormant())
			AddToLeafSystem();
		else
			RemoveFromLeafSystem();
	}

	ClientRenderHandle_t renderHandle;

	public virtual void ComputeWorldSpaceSurroundingBox(out Vector3 vecWorldMins, out Vector3 vecWorldMaxs) {
		Assert(false);
		vecWorldMins = default;
		vecWorldMaxs = default;
	}

	public void MarkRenderHandleDirty() {
		ClientRenderHandle_t handle = GetRenderHandle();
		if (handle != INVALID_CLIENT_RENDER_HANDLE)
			clientLeafSystem.RenderableChanged(handle);
	}

	public ClientRenderHandle_t GetRenderHandle() => renderHandle;
	public ref ClientRenderHandle_t RenderHandle() => ref renderHandle;

	public virtual RenderGroup GetRenderGroup() {
		if (RenderMode == (int)Source.RenderMode.None)
			return RenderGroup.OpaqueEntity;

		// todo
		// int tempComputeFrame = FXComputeFrame;
		// FXComputeFrame = gpGlobals.FrameCount;

		int fxBlend = GetFxBlend();

		// todo
		// FXComputeFrame = tempComputeFrame;

		if (fxBlend == 0)
			return RenderGroup.OpaqueEntity;

		ModelType modelType = modelinfo.GetModelType(Model);
		RenderGroup renderGroup = (modelType == ModelType.Brush) ? RenderGroup.OpaqueBrush : RenderGroup.OpaqueEntity;
		if ((fxBlend != 255) || IsTransparent()) {
			if (RenderMode != (int)Source.RenderMode.Environmental)
				renderGroup = RenderGroup.TranslucentEntity;
			else
				renderGroup = RenderGroup.Other;
		}

		if ((renderGroup == RenderGroup.TranslucentEntity) && modelinfo.IsTranslucentTwoPass(Model))
			renderGroup = RenderGroup.TwoPass;

		return renderGroup;
	}

	public void AddToLeafSystem() => AddToLeafSystem(GetRenderGroup());
	public void AddToLeafSystem(RenderGroup group) {
		if (renderHandle == INVALID_CLIENT_RENDER_HANDLE) {
			clientLeafSystem.AddRenderable(this, group);
			clientLeafSystem.EnableAlternateSorting(renderHandle, AlternateSorting);
		}
		else {
			clientLeafSystem.SetRenderGroup(renderHandle, group);
			clientLeafSystem.RenderableChanged(renderHandle);
		}
	}

	private void RemoveFromLeafSystem() {
		if (renderHandle != INVALID_CLIENT_RENDER_HANDLE) {
			clientLeafSystem.RemoveRenderable(renderHandle);
			renderHandle = INVALID_CLIENT_RENDER_HANDLE;
		}
		DestroyShadow();
	}

	public ref readonly QAngle GetLocalAngularVelocity() => ref AngVelocity;

	public void SetLocalAngularVelocity(in QAngle vecAngVelocity) {
		if (AngVelocity != vecAngVelocity)
			AngVelocity = vecAngVelocity;
	}

	public virtual void NotifyShouldTransmit(ShouldTransmiteState state) {
		if (EntIndex() < 0)
			return;

		switch (state) {
			case ShouldTransmiteState.Start: {
					SetDormant(false);

					if (PredictableID.IsActive()) {
						PredictableID.SetAcknowledged(true);

						C_BaseEntity? otherEntity = FindPreviouslyCreatedEntity(PredictableID);
						if (otherEntity != null) {
							Assert(otherEntity.IsClientCreated());
							Assert(otherEntity.PredictableID.IsActive());
							// We need IsHandleValid/GetClientHandle stuff.
							// Assert(cl_entitylist.IsHandleValid(otherEntity.GetClientHandle()));

							otherEntity.PredictableID.SetAcknowledged(true);

							if (OnPredictedEntityRemove(false, otherEntity))
								otherEntity.Release();
						}
					}
				}
				break;

			case ShouldTransmiteState.End: {
					UnlinkFromHierarchy();
					SetDormant(true);
				}
				break;

			default:
				Assert(false);
				break;
		}
	}

	LinkedListNode<C_BaseEntity>? InterpolationListEntry;
	LinkedListNode<C_BaseEntity>? TeleportListEntry;

	public void AddToInterpolationList() {
		if (InterpolationListEntry == null)
			InterpolationListEntry = InterpolationList.AddLast(this);
	}

	public void RemoveFromInterpolationList() {
		if (InterpolationListEntry != null) {
			InterpolationList.Remove(InterpolationListEntry);
			InterpolationListEntry = null;
		}
	}

	public void AddToTeleportList() {
		if (TeleportListEntry == null)
			TeleportListEntry = TeleportList.AddLast(this);
	}

	public void RemoveFromTeleportList() {
		if (TeleportListEntry != null) {
			TeleportList.Remove(TeleportListEntry);
			TeleportListEntry = null;
		}
	}

	public virtual void OnPreDataChanged(DataUpdateType updateType) {
		OldMoveParent.Set(NetworkMoveParent);
		OldParentAttachment = ParentAttachment;
	}

	public virtual void OnDataChanged(DataUpdateType updateType) {
		CheckInitPredictable("OnDataChanged");
		CreateShadow();

		if (updateType == DataUpdateType.Created)
			UpdateVisibility();
	}

	public virtual void OnLatchInterpolatedVariables(LatchFlags flags) {
		TimeUnit_t changetime = GetLastChangeTime(flags);

		bool updateLastNetworkedValue = (flags & LatchFlags.InteroplateOmitUpdateLastNetworked) == 0;

		int c = VarMap.Entries.Count;
		for (int i = 0; i < c; i++) {
			VarMapEntry e = VarMap.Entries[i];
			IInterpolatedVar watcher = e.Watcher;

			LatchFlags type = watcher.GetVarType();

			if ((type & flags) == 0)
				continue;

			if ((type & LatchFlags.ExcludeAutoLatch) != 0)
				continue;

			if (watcher.NoteChanged(changetime, updateLastNetworkedValue))
				e.NeedsToInterpolate = true;
		}

		if (ShouldInterpolate())
			AddToInterpolationList();
	}

	private TimeUnit_t GetLastChangeTime(LatchFlags flags) {
		if (GetPredictable() || IsClientCreated())
			return gpGlobals.CurTime;

		Assert(!((flags & LatchFlags.LatchAnimationVar) != 0 && (flags & LatchFlags.LatchSimulationVar) != 0));

		if ((flags & LatchFlags.LatchAnimationVar) != 0)
			return GetAnimTime();

		if ((flags & LatchFlags.LatchSimulationVar) != 0) {
			TimeUnit_t st = GetSimulationTime();
			if (st == 0.0)
				return gpGlobals.CurTime;

			return st;
		}

		Assert(false);

		return gpGlobals.CurTime;
	}

	private void CreateShadow() {
		ShadowType shadowType = ShadowCastType();
		if (shadowType == ShadowType.None)
			DestroyShadow();
		else {
			if (ShadowHandle == CLIENTSHADOW_INVALID_HANDLE) {
				int flags = (int)ShadowFlags.Shadow;
				if (shadowType != ShadowType.Simple)
					flags |= (int)ClientShadowFlags.UseRenderToTexture;
				if (shadowType == ShadowType.RenderToTextureDynamic)
					flags |= (int)ClientShadowFlags.AnimatingSource;
				ShadowHandle = g_ClientShadowMgr.CreateShadow(GetClientHandle(), flags);
			}
		}
	}

	private void DestroyShadow() {
		if (ShadowHandle != CLIENTSHADOW_INVALID_HANDLE) {
			g_ClientShadowMgr.DestroyShadow(ShadowHandle);
			ShadowHandle = CLIENTSHADOW_INVALID_HANDLE;
		}
	}


	public void ComputeAbsPosition(in Vector3 localPosition, out Vector3 absPosition) {
		C_BaseEntity? moveParent = GetMoveParent();
		if (moveParent == null)
			absPosition = localPosition;
		else
			MathLib.VectorTransform(localPosition, moveParent.EntityToWorldTransform(), out absPosition);
	}


	public virtual void Spawn() { }
	public virtual void Activate() { }
	public virtual void SpawnClientEntity() { }
	public virtual void Precache() { }

	public PredictionContext? PredictionContext;
	bool Dormant;
	bool Predictable;
	bool DormantPredictable;
	long IncomingPacketEntityBecameDormant;

	public bool GetPredictable() => Predictable;
	public void SetPredictable(bool state) {
		Predictable = state;
		Interp_UpdateInterpolationAmounts(ref GetVarMapping());
	}

	public void ShutdownPredictable() {
		Assert(GetPredictable());

		g_Predictables.RemoveFromPredictablesList(GetClientHandle());
		DestroyIntermediateData();
		SetPredictable(false);
	}


	public bool BecameDormantThisPacket() {
		Assert(IsDormantPredictable());
		if (IncomingPacketEntityBecameDormant != prediction.GetIncomingPacketNumber())
			return false;
		return true;
	}

	public bool IsDormantPredictable() {
		return DormantPredictable;
	}

	public void SetDormantPredictable(bool dormant) {
		Assert(IsClientCreated());
		DormantPredictable = true;
		IncomingPacketEntityBecameDormant = prediction.GetIncomingPacketNumber();
	}

	public bool IsClientCreated() {
		if (PredictionContext != null) {
			Assert(!GetPredictable());
			return true;
		}
		return false;
	}

	public ref Matrix3x4 EntityToWorldTransform() {
		CalcAbsolutePosition();
		return ref CoordinateFrame;
	}

	public virtual void GetVectors(out Vector3 forward, out Vector3 right, out Vector3 up) {
		ref readonly Matrix3x4 entityToWorld = ref EntityToWorldTransform();

		MathLib.MatrixGetColumn(entityToWorld, 0, out forward);

		MathLib.MatrixGetColumn(entityToWorld, 1, out right);
		right *= -1.0f;

		MathLib.MatrixGetColumn(entityToWorld, 2, out up);
	}

	public ref Vector3 GetNetworkOrigin() => ref NetworkOrigin;
	public ref readonly Vector3 GetOldOrigin() => ref OldOrigin;
	public ref QAngle GetNetworkAngles() => ref NetworkAngles;

	public void SetLocalOrigin(in Vector3 origin) {
		// Safety check against NaN's or really huge numbers
		if (!IsEntityPositionReasonable(origin)) {
			if (CheckEmitReasonablePhysicsSpew())
				Warning($"Bad SetLocalOrigin({origin}) on {GetDebugName()}\n");
			Assert(false);
			return;
		}

		//	if ( !origin.IsValid() )
		//	{
		//		AssertMsg( 0, "Bad origin set" );
		//		return;
		//	}

		if (Origin != origin) {
			// Sanity check to make sure the origin is valid.
#if DEBUG
			float largeVal = 1024 * 128;
			Assert(origin.X >= -largeVal && origin.X <= largeVal);
			Assert(origin.Y >= -largeVal && origin.Y <= largeVal);
			Assert(origin.Z >= -largeVal && origin.Z <= largeVal);
#endif

			InvalidatePhysicsRecursive(InvalidatePhysicsBits.PositionChanged);
			Origin = origin;
			SetSimulationTime(gpGlobals.CurTime);
		}
	}

	public ReadOnlySpan<char> GetDebugName() => GetClassname(); // todo

	public void SetLocalAngles(in QAngle angles) {
		// NOTE: The angle normalize is a little expensive, but we can save
		// a bunch of time in interpolation if we don't have to invalidate everything
		// and sometimes it's off by a normalization amount

		// FIXME: The normalize caused problems in server code like momentary_rot_button that isn't
		//        handling things like +/-180 degrees properly. This should be revisited.
		//QAngle angleNormalize( AngleNormalize( angles.x ), AngleNormalize( angles.y ), AngleNormalize( angles.z ) );

		// Safety check against NaN's or really huge numbers
		if (!IsEntityQAngleReasonable(angles)) {
			if (CheckEmitReasonablePhysicsSpew())
				Warning($"Bad SetLocalAngles({angles}) on {GetDebugName()}\n");
			AssertMsg(false, $"Bad SetLocalAngles({angles}) on {GetDebugName()}\n");
			return;
		}

		if (Rotation != angles) {
			InvalidatePhysicsRecursive(InvalidatePhysicsBits.AnglesChanged);
			Rotation = angles;
			SetSimulationTime(gpGlobals.CurTime);
		}
	}

	public void SetNetworkAngles(in QAngle angles) {
		NetworkAngles = angles;
	}



	[NetworkName("m_fFlags")]
	protected int flags;
	EFL eflags = EFL.DirtyAbsTransform; // << TODO: FIGURE OUT WHAT ACTUALLY INITIALIZES THIS.
	public Matrix3x4 CoordinateFrame;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsMarkedForDeletion() => (eflags & EFL.KillMe) != 0;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void AddEFlags(EFL flags) => eflags |= flags;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void RemoveEFlags(EFL flags) => eflags &= ~flags;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsEFlagSet(EFL mask) => (eflags & mask) != 0;


	readonly object CalcAbsolutePositionMutex = new();

	public TimeUnit_t SpawnTime() => flSpawnTime;

	protected void CalcAbsolutePosition() {
		if (!s_bAbsRecomputationEnabled)
			return;

		if ((eflags & EFL.DirtyAbsTransform) == 0)
			return;

		lock (CalcAbsolutePositionMutex) {
			if ((eflags & EFL.DirtyAbsTransform) == 0)
				return;

			RemoveEFlags(EFL.DirtyAbsTransform);

			if (!MoveParent.IsValid()) {
				MathLib.AngleMatrix(GetLocalAngles(), GetLocalOrigin(), out CoordinateFrame);
				AbsOrigin = GetLocalOrigin();
				AbsRotation = GetLocalAngles();
				MathLib.NormalizeAngles(ref AbsRotation);
				return;
			}

			if (IsEffectActive(EntityEffects.BoneMerge)) {
				MoveToAimEnt();
				return;
			}

			MathLib.AngleMatrix(GetLocalAngles(), out Matrix3x4 matEntityToParent);
			MathLib.MatrixSetColumn(GetLocalOrigin(), 3, ref matEntityToParent);

			// concatenate with our parent's transform
			Matrix3x4 scratchMatrix = default;
			MathLib.ConcatTransforms(GetParentToWorldTransform(ref scratchMatrix), matEntityToParent, out CoordinateFrame);

			// pull our absolute position out of the matrix
			MathLib.MatrixGetColumn(CoordinateFrame, 3, out AbsOrigin);

			// if we have any angles, we have to extract our absolute angles from our matrix
			if (Rotation == vec3_angle && ParentAttachment == 0)
				// just copy our parent's absolute angles
				MathLib.VectorCopy(MoveParent.Get()!.GetAbsAngles(), out AbsRotation);
			else
				MathLib.MatrixAngles(CoordinateFrame, out AbsRotation);


			// This is necessary because it's possible that our moveparent's CalculateIKLocks will trigger its move children 
			// (ie: this entity) to call GetAbsOrigin(), and they'll use the moveparent's OLD bone transforms to get their attachments
			// since the moveparent is right in the middle of setting up new transforms. 
			//
			// So here, we keep our absorigin invalidated. It means we're returning an origin that is a frame old to CalculateIKLocks,
			// but we'll still render with the right origin.
			if (ParentAttachment != 0 && (MoveParent.Get()!.GetEFlags() & EFL.SettingUpBones) != 0)
				eflags |= EFL.DirtyAbsTransform;
		}
	}

	public ref Matrix3x4 GetParentToWorldTransform(ref Matrix3x4 tempMatrix) {
		C_BaseEntity? moveParent = GetMoveParent();
		if (moveParent == null) {
			Assert(false);
			MathLib.SetIdentityMatrix(out tempMatrix);
			return ref tempMatrix;
		}

		if (ParentAttachment != 0) {
			Vector3 origin;
			QAngle angles;
			if (moveParent.GetAttachment(ParentAttachment, out origin, out angles)) {
				MathLib.AngleMatrix(angles, origin, out tempMatrix);
				return ref tempMatrix;
			}
		}

		// If we fall through to here, then just use the move parent's abs origin and angles.
		return ref moveParent.EntityToWorldTransform();
	}

	public virtual IClientVehicle? GetClientVehicle() => null;

	public void SetRemovalFlag(bool remove) {
		if (remove)
			eflags |= EFL.KillMe;
		else
			eflags &= ~EFL.KillMe;
	}

	public readonly List<ThinkFunc> ThinkFunctions = [];
	public int CurrentThinkContext = NO_THINK_CONTEXT;
	public void SetNextClientThink(TimeUnit_t nextThinkTime) {
		Assert(GetClientHandle() != INVALID_CLIENTENTITY_HANDLE);
		ClientThinkList().SetNextClientThink(GetClientHandle()!, nextThinkTime);
	}

	public virtual void GetAimEntOrigin(C_BaseEntity attachedTo, out Vector3 origin, out QAngle angles) {
		origin = attachedTo.GetAbsOrigin();
		angles = attachedTo.GetAbsAngles();
	}

	public void SetAbsOrigin(in Vector3 absOrigin) {
		CalcAbsolutePosition();

		if (AbsOrigin == absOrigin)
			return;

		InvalidatePhysicsRecursive(InvalidatePhysicsBits.PositionChanged);
		RemoveEFlags(EFL.DirtyAbsTransform);

		AbsOrigin = absOrigin;
		// TODO: Coordinate frame...
		C_BaseEntity? moveParent = GetMoveParent();

		if (moveParent == null) {
			Origin = absOrigin;
			return;
		}

		// TODO: Handle move parents...
	}

	public void SetAbsAngles(in QAngle absAngles) {
		CalcAbsolutePosition();

		if (AbsRotation == absAngles)
			return;

		InvalidatePhysicsRecursive(InvalidatePhysicsBits.AnglesChanged);
		RemoveEFlags(EFL.DirtyAbsTransform);

		AbsRotation = absAngles;
		// TODO: Set coordinate frame

		C_BaseEntity? moveParent = GetMoveParent();

		if (moveParent == null) {
			Rotation = absAngles;
			return;
		}

		// TODO: Handle move parents...
	}

	public void MoveToAimEnt() {
		GetAimEntOrigin(GetMoveParent(), out Vector3 aimEntOrigin, out QAngle aimEntAngles);
		SetAbsOrigin(aimEntOrigin);
		SetAbsAngles(aimEntAngles);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsDormant() => IsServerEntity() && Dormant;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool IsServerEntity() => Index != -1;

	public int EntIndex() {
		return Index;
	}

	public virtual void ReceiveMessage(int classID, bf_read msg) {
		Assert(classID == GetClientClass().ClassID);

		int messageType = msg.ReadByte();
		switch (messageType) {
			case BASEENTITY_MSG_REMOVE_DECALS:
				RemoveAllDecals();
				break;
		}
	}

	private void RemoveAllDecals() {

	}

	public void SetDestroyedOnRecreateEntities() {

	}

	public virtual ICollideable GetCollideable() => Collision;
	public virtual ref readonly BaseHandle GetRefEHandle() {
		return ref RefEHandle;
	}
	public virtual void SetRefEHandle(in BaseHandle handle) {
		RefEHandle.Index = handle.Index;
	}


	private ref readonly Vector3 GetLocalVelocity() {
		return ref Velocity;
	}


	public void OnDataUnchangedInPVS() {
		Assert(NetworkMoveParent.Get() != null || !NetworkMoveParent.IsValid());
		HierarchySetParent(NetworkMoveParent.Get());
		MarkMessageReceived();
	}

	public virtual IPVSNotify? GetPVSNotifyInterface() {
		return null;
	}

	public virtual void ComputeFxBlend() {
		// todo
	}

	public virtual object GetDataTableBasePtr() {
		return this;
	}

	public BaseHandle RefEHandle = new();

	static double AdjustInterpolationAmount(C_BaseEntity entity, double baseInterpolation) {
		// We don't have cl_interp_npcs yet so this isn't needed
		return baseInterpolation;
	}

	public double GetInterpolationAmount(LatchFlags flags) {
		int serverTickMultiple = 1;
		// TODO: IsSimulatingOnAlternateTicks

		if (GetPredictable() || IsClientCreated()) {
			return TICK_INTERVAL * serverTickMultiple;
		}

		bool playingDemo = false; // engine.IsPlayingDemo();
		bool playingMultiplayer = !playingDemo && (gpGlobals.MaxClients > 1);
		bool playingNonLocallyRecordedDemo = playingDemo; // && !engine.IsPlayingDemoALocallyRecordedDemo();
		if (playingMultiplayer || playingNonLocallyRecordedDemo) {
			return AdjustInterpolationAmount(this, TICKS_TO_TIME(TIME_TO_TICKS(CdllBoundedCVars.GetClientInterpAmount()) + serverTickMultiple));
		}

		// TODO: Re-evaluate this later
		//expandedServerTickMultiple += g_nThreadModeTicks;
		int expandedServerTickMultiple = 1;

		if (IsAnimatedEveryTick() && IsSimulatedEveryTick()) {
			return TICK_INTERVAL * expandedServerTickMultiple;
		}

		if ((flags & LatchFlags.LatchAnimationVar) != 0 && IsAnimatedEveryTick()) {
			return TICK_INTERVAL * expandedServerTickMultiple;
		}
		if ((flags & LatchFlags.LatchSimulationVar) != 0 && IsSimulatedEveryTick()) {
			return TICK_INTERVAL * expandedServerTickMultiple;
		}

		return AdjustInterpolationAmount(this, TICKS_TO_TIME(TIME_TO_TICKS(CdllBoundedCVars.GetClientInterpAmount()) + serverTickMultiple));
	}


	public void AddVar(object instance, DynamicAccessor accessor, IInterpolatedVar watcher, LatchFlags type, bool setup = false) {
		bool addIt = true;
		for (int i = 0; i < VarMap.Entries.Count; i++) {
			if (VarMap.Entries[i].Watcher == watcher) {
				if ((type & LatchFlags.ExcludeAutoInterpolate) != (watcher.GetVarType() & LatchFlags.ExcludeAutoInterpolate))
					RemoveVar(instance, VarMap.Entries[i].Accessor, true);
				else
					addIt = false;

				break;
			}
		}

		if (addIt) {
			VarMapEntry map = new() {
				Accessor = accessor,
				Watcher = watcher,
				Type = type,
				NeedsToInterpolate = true
			};
			if ((type & LatchFlags.ExcludeAutoInterpolate) != 0) {
				VarMap.Entries.Add(map);
			}
			else {
				VarMap.Entries.Insert(0, map);
				++VarMap.InterpolatedEntries;
			}
		}

		if (setup) {
			watcher.Setup(instance, accessor, type);
			watcher.SetInterpolationAmount(GetInterpolationAmount(watcher.GetVarType()));
		}
	}
	public void RemoveVar(object instance, DynamicAccessor accessor, bool assert = true) {
		for (int i = 0; i < VarMap.Entries.Count; i++) {
			if (VarMap.Entries[i].Watcher.GetInstance() == instance && VarMap.Entries[i].Accessor == accessor) {
				if ((VarMap.Entries[i].Type & LatchFlags.ExcludeAutoInterpolate) == 0)
					--VarMap.InterpolatedEntries;

				VarMap.Entries.RemoveAt(i);
				return;
			}
		}

		if (assert)
			AssertMsg(false, "RemoveVar");
	}
	public ref VarMapping GetVarMapping() => ref VarMap;
	public VarMapping VarMap = new();
	static double LastValue_Interp = -1;
	static double LastValue_InterpNPCs = -1;
	public static void CheckCLInterpChanged() {
		double curValue_Interp = CdllBoundedCVars.GetClientInterpAmount();
		if (LastValue_Interp == -1) LastValue_Interp = curValue_Interp;

		float curValue_InterpNPCs = cl_interp_npcs.GetFloat();
		if (LastValue_InterpNPCs == -1) LastValue_InterpNPCs = curValue_InterpNPCs;

		if (LastValue_Interp != curValue_Interp || LastValue_InterpNPCs != curValue_InterpNPCs) {
			LastValue_Interp = curValue_Interp;
			LastValue_InterpNPCs = curValue_InterpNPCs;

			C_BaseEntityIterator iterator = new();
			C_BaseEntity? ent;
			while ((ent = iterator.Next()) != null)
				ent.Interp_UpdateInterpolationAmounts(ref ent.GetVarMapping());
		}
	}

	private void Interp_SetupMappings(ref VarMapping map) {
		if (Unsafe.IsNullRef(ref map))
			return;

		int c = map.Entries.Count;
		for (int i = 0; i < c; i++) {
			VarMapEntry e = map.Entries[i];
			IInterpolatedVar watcher = e.Watcher;
			DynamicAccessor accessor = e.Accessor;
			LatchFlags type = e.Type;

			watcher.Setup(this, accessor, type);
			watcher.SetInterpolationAmount(GetInterpolationAmount(watcher.GetVarType()));
		}
	}
	private int Interp_Interpolate(ref VarMapping map, double currentTime) {
		int noMoreChanges = 1;
		if (currentTime < map.LastInterpolationTime) {
			for (int i = 0; i < map.InterpolatedEntries; i++) {
				VarMapEntry e = map.Entries[i];

				e.NeedsToInterpolate = true;
			}
		}
		map.LastInterpolationTime = currentTime;

		for (int i = 0; i < map.InterpolatedEntries; i++) {
			VarMapEntry e = map.Entries[i];

			if (!e.NeedsToInterpolate)
				continue;

			IInterpolatedVar watcher = e.Watcher;
			Assert((watcher.GetVarType() & LatchFlags.ExcludeAutoInterpolate) == 0);

			if (watcher.Interpolate(currentTime) != 0)
				e.NeedsToInterpolate = false;
			else
				noMoreChanges = 0;
		}

		return noMoreChanges;
	}
	private void Interp_RestoreToLastNetworked(ref VarMapping map) {
		Vector3 oldOrigin = GetLocalOrigin();
		QAngle oldAngles = GetLocalAngles();
		Vector3 oldVel = GetLocalVelocity();

		int c = map.Entries.Count();
		for (int i = 0; i < c; i++) {
			VarMapEntry e = map.Entries[i];
			IInterpolatedVar watcher = e.Watcher;
			watcher.RestoreToLastNetworked();
		}

		BaseInterpolatePart2(oldOrigin, oldAngles, oldVel, 0);
	}

	public virtual bool Interpolate(TimeUnit_t currentTime) {
		Vector3 oldOrigin = default;
		QAngle oldAngles = default;
		Vector3 oldVel = default;

		int noMoreChanges = 0;
		InterpolateResult retVal = BaseInterpolatePart1(ref currentTime, ref oldOrigin, ref oldAngles, ref oldVel, ref noMoreChanges);

		if (noMoreChanges != 0)
			RemoveFromInterpolationList();

		if (retVal == InterpolateResult.Stop)
			return true;

		InvalidatePhysicsBits changeFlags = 0;
		BaseInterpolatePart2(oldOrigin, oldAngles, oldVel, changeFlags);

		return true;
	}



	protected InterpolateResult BaseInterpolatePart1(ref TimeUnit_t currentTime, ref Vector3 oldOrigin, ref QAngle oldAngles, ref Vector3 oldVel, ref int noMoreChanges) {
		noMoreChanges = 1;

		if (IsFollowingEntity() || !IsInterpolationEnabled()) {
			MoveToLastReceivedPosition();
			return InterpolateResult.Stop;
		}


		if (GetPredictable() || IsClientCreated()) {
			C_BasePlayer? localplayer = C_BasePlayer.GetLocalPlayer();
			if (localplayer != null && currentTime == gpGlobals.CurTime) {
				currentTime = localplayer.GetFinalPredictedTime();
				currentTime -= TICK_INTERVAL;
				currentTime += (gpGlobals.InterpolationAmount * TICK_INTERVAL);
			}
		}

		oldOrigin = Origin;
		oldAngles = Rotation;
		oldVel = Velocity;

		noMoreChanges = Interp_Interpolate(ref GetVarMapping(), currentTime);
		if (cl_interp_all.GetInt() != 0 || (EntClientFlags & EntClientFlags.AlwaysInterpolate) != 0)
			noMoreChanges = 0;

		return InterpolateResult.Continue;
	}

	void PhysicsStep() { }
	void PhysicsPusher() { }
	void PhysicsNone() { }
	void PhysicsRigidChild() { }
	void PhysicsNoclip() { }
	void PhysicsStepRunTimestep(TimeUnit_t timestep) { }
	void PhysicsToss() { }
	void PhysicsCustom() { }
	void PerformPush(TimeUnit_t movetime) { }
	void UpdateBaseVelocity() { }


	private static bool IsInterpolationEnabled() => s_bInterpolate;
	public C_BaseEntity? GetMoveParent() => MoveParent.Get();
	public C_BaseEntity? FirstMoveChild() => MoveChild.Get();
	public C_BaseEntity? NextMovePeer() => MovePeer.Get();
	public bool IsVisible() => renderHandle != INVALID_CLIENT_RENDER_HANDLE;


	public bool IsFollowingEntity() => IsEffectActive(EntityEffects.BoneMerge) && (GetMoveType() == Source.MoveType.None) && GetMoveParent() != null;

	public virtual C_BaseEntity? GetFollowedEntity() {
		if (!IsFollowingEntity())
			return null;
		return GetMoveParent();
	}

	public virtual ModelInstanceHandle_t GetModelInstance() => ModelInstance;
	public virtual void SetModelInstance(ModelInstanceHandle_t modelInstance) => ModelInstance = modelInstance;
	public void CreateModelInstance() {
		if (ModelInstance == MODEL_INSTANCE_INVALID)
			ModelInstance = modelrender.CreateInstance(this);
	}
	public void DestroyModelInstance() {
		if (ModelInstance != MODEL_INSTANCE_INVALID) {
			modelrender.DestroyInstance(ModelInstance);
			ModelInstance = MODEL_INSTANCE_INVALID;
		}
	}

	public virtual void AddStudioDecal(in Ray ray, int hitbox, int decalIndex, bool doTrace, ref Trace tr, int maxLODToDecal = -1) {
		// throw new NotImplementedException();
		DevWarning("AddStudioDecal not implemented\n");
	}

	public virtual void AddBrushModelDecal(in Ray ray, in Vector3 decalCenter, int decalIndex, bool doTrace, ref Trace tr) {
		if (doTrace) {
			enginetrace.ClipRayToEntity(in ray, Mask.Shot, this, ref tr);
			if (tr.Fraction == 1.0f)
				return;
		}

		effects.DecalShoot(decalIndex, Index, Model, GetAbsOrigin(), GetAbsAngles(), decalCenter, null, 0);
	}

	public void AddDecal(in Vector3 rayStart, in Vector3 rayEnd, in Vector3 decalCenter, int hitbox, int decalIndex, bool doTrace, ref Trace tr, int maxLODToDecal = -1) {
		Ray ray = new();
		ray.Init(in rayStart, in rayEnd);
		ray.Delta *= 1.1f;

		ModelType modelType = modelinfo.GetModelType(Model);
		switch (modelType) {
			case ModelType.Studio:
				AddStudioDecal(in ray, hitbox, decalIndex, doTrace, ref tr, maxLODToDecal);
				break;
			case ModelType.Brush:
				AddBrushModelDecal(in ray, in decalCenter, decalIndex, doTrace, ref tr);
				break;
			default:
				tr.Fraction = 1.0f;
				break;
		}
	}

	public virtual bool GetAttachment(int number, out Vector3 origin, out QAngle angles) {
		origin = GetAbsOrigin();
		angles = GetAbsAngles();
		return true;
	}

	readonly object m_CalcAbsoluteVelocityMutex = new();

	public void CalcAbsoluteVelocity() {
		if ((eflags & EFL.DirtyAbsVelocity) == 0)
			return;
		lock (m_CalcAbsoluteVelocityMutex) {
			if ((eflags & EFL.DirtyAbsVelocity) == 0) // need second check in event another thread grabbed mutex and did the calculation
				return;

			eflags &= ~EFL.DirtyAbsVelocity;

			BaseEntity? moveParent = GetMoveParent();
			if (moveParent == null) {
				AbsVelocity = Velocity;
				return;
			}

			MathLib.VectorRotate(Velocity, moveParent.EntityToWorldTransform(), out AbsVelocity);


			// Add in the attachments velocity if it exists
			if (ParentAttachment != 0) {
				Vector3 vOriginVel;
				Quaternion vAngleVel;
				if (moveParent.GetAttachmentVelocity(ParentAttachment, out vOriginVel, out vAngleVel)) {
					AbsVelocity += vOriginVel;
					return;
				}
			}

			// Now add in the parent abs velocity
			AbsVelocity += moveParent.GetAbsVelocity();
		}
	}

	Vector3 AbsVelocity;
	QAngle AngVelocity;
	// todo
	public ref readonly Vector3 GetAbsVelocity() {
		CalcAbsoluteVelocity();
		return ref AbsVelocity;
	}
	public void SetAbsVelocity(in Vector3 absVelocity) {
		if (AbsVelocity == absVelocity)
			return;

		// The abs velocity won't be dirty since we're setting it here
		InvalidatePhysicsRecursive(InvalidatePhysicsBits.VelocityChanged);
		eflags &= ~EFL.DirtyAbsVelocity;

		AbsVelocity = absVelocity;

		C_BaseEntity? moveParent = GetMoveParent();

		if (moveParent == null) {
			Velocity = absVelocity;
			return;
		}

		// First subtract out the parent's abs velocity to get a relative
		// velocity measured in world space
		Vector3 relVelocity;
		MathLib.VectorSubtract(absVelocity, moveParent.GetAbsVelocity(), out relVelocity);

		// Transform velocity into parent space
		MathLib.VectorIRotate(relVelocity, moveParent.EntityToWorldTransform(), out Velocity);
	}

	public ref readonly Vector3 GetBaseVelocity() => ref BaseVelocity;
	public void SetBaseVelocity(in Vector3 v) => BaseVelocity = v;

	public virtual bool GetAttachment(int number, out Vector3 origin) {
		origin = GetAbsOrigin();
		return true;
	}

	public virtual bool GetAttachment(int number, out Matrix3x4 matrix) {
		matrix = EntityToWorldTransform();
		return true;
	}

	public virtual bool GetAttachmentVelocity(int number, out Vector3 originVel, out Quaternion angleVel) {
		originVel = GetAbsVelocity();
		angleVel = default;
		angleVel.Init();
		return true;
	}


	protected virtual bool ShouldInterpolate() {
		if (render.GetViewEntity() == Index)
			return true;

		if (Index == 0 || GetModel() == null)
			return false;

		if (IsVisible())
			return true;

		C_BaseEntity? child = FirstMoveChild();
		while (child != null) {
			if (child.ShouldInterpolate())
				return true;

			child = child.NextMovePeer();
		}

		return false;
	}

	public void Interp_Reset(ref VarMapping map) {
		int c = map.Entries.Count;
		for (int i = 0; i < c; i++) {
			VarMapEntry e = map.Entries[i];
			IInterpolatedVar watcher = e.Watcher;
			watcher.Reset();
		}
	}

	protected void BaseInterpolatePart2(Vector3 oldOrigin, QAngle oldAngles, Vector3 oldVel, InvalidatePhysicsBits changeFlags) {
		if (Origin != oldOrigin)
			changeFlags |= InvalidatePhysicsBits.PositionChanged;
		if (Rotation != oldAngles)
			changeFlags |= InvalidatePhysicsBits.AnglesChanged;
		if (Velocity != oldVel)
			changeFlags |= InvalidatePhysicsBits.VelocityChanged;

		if (changeFlags != 0)
			InvalidatePhysicsRecursive(changeFlags);
	}

	public void Interp_UpdateInterpolationAmounts(ref VarMapping map) {
		if (Unsafe.IsNullRef(ref map))
			return;

		int c = map.Entries.Count;
		for (int i = 0; i < c; i++) {
			VarMapEntry e = map.Entries[i];
			IInterpolatedVar watcher = e.Watcher;
			watcher.SetInterpolationAmount(GetInterpolationAmount(watcher.GetVarType()));
		}
	}
	private void Interp_HierarchyUpdateInterpolationAmounts() {
		Interp_UpdateInterpolationAmounts(ref GetVarMapping());

		for (C_BaseEntity? child = FirstMoveChild(); child != null; child = child.NextMovePeer())
			child.Interp_HierarchyUpdateInterpolationAmounts();
	}


	public void ShiftIntermediateDataForward(int slots_to_remove, int number_of_commands_run) {
		// Just moving pointers, yeah
		List<byte[]> saved = ListPool<byte[]>.Shared.Alloc();

		// Remember first slots
		int i = 0;
		for (; i < slots_to_remove; i++)
			saved.Add(IntermediateData[i]);

		// Move rest of slots forward up to last slot
		for (; i < number_of_commands_run; i++)
			IntermediateData[i - slots_to_remove] = IntermediateData[i];

		// Put remembered slots onto end
		for (i = 0; i < slots_to_remove; i++) {
			int slot = number_of_commands_run - slots_to_remove + i;

			IntermediateData[slot] = saved[i];
		}

		ListPool<byte[]>.Shared.Free(saved);
	}

	public bool GetCheckUntouch() => IsEFlagSet(EFL.CheckUntouch);

	public static bool sm_bDisableTouchFuncs = false;  // Disables PhysicsTouch and PhysicsStartTouch function calls
	public int TouchStamp;

	public delegate void TOUCHPTR(C_BaseEntity? other);
	public TOUCHPTR? FnTouch;

	public virtual void StartTouch(C_BaseEntity? other) {
		// notify parent
		//	if ( m_pParent != NULL )
		//		m_pParent->StartTouch( pOther );
	}

	//-----------------------------------------------------------------------------
	// Purpose: Call touch function if one is set
	// Input  : *pOther -
	//-----------------------------------------------------------------------------
	public virtual void Touch(C_BaseEntity? other) {
		FnTouch?.Invoke(other);

		// notify parent of touch
		//	if ( m_pParent != NULL )
		//		m_pParent->Touch( pOther );
	}

	public virtual void EndTouch(C_BaseEntity? other) {
		// notify parent
		//	if ( m_pParent != NULL )
		//	{
		//		m_pParent->EndTouch( pOther );
		//	}
	}

	public void SetCheckUntouch(bool check) {
		// Invalidate touchstamp
		if (check) {
			TouchStamp++;
			AddEFlags(EFL.CheckUntouch);
		}
		else
			RemoveEFlags(EFL.CheckUntouch);
	}

	public readonly byte[][] IntermediateData = new byte[MULTIPLAYER_BACKUP][];
	public byte[]? OriginalData;
	public int IntermediateDataCount;

	public byte[]? GetPredictedFrame(int framenumber) {
		if (OriginalData == null) {
			Assert(false);
			return default;
		}

		return IntermediateData[framenumber % MULTIPLAYER_BACKUP];
	}

	public byte[]? GetOriginalNetworkDataObject() {
		if (OriginalData == null) {
			Assert(false);
			return default;
		}
		return OriginalData;
	}

	public virtual Vector3 GetSoundEmissionOrigin() => WorldSpaceCenter();

	public static void EmitSound<IRF>(scoped in IRF filter, int entIndex, scoped in EmitSound_t parms) where IRF : IRecipientFilter {
		C_BaseEntity? entity = cl_entitylist.GetEnt(entIndex);
		entity?.ModifyEmitSoundParams(ref Unsafe.AsRef(in parms));
		g_SoundEmitterSystem.EmitSound(filter, entIndex, ref Unsafe.AsRef(in parms));
	}


	public static readonly nuint SIZEOF_MANAGED = nuint.MaxValue - 16;

	static unsafe readonly nuint[] g_FieldSizes = [
		0,							// FIELD_VOID
		sizeof(float),				// FIELD_FLOAT
		SIZEOF_MANAGED,				// FIELD_STRING
		(nuint)sizeof(Vector3),		// FIELD_VECTOR
		(nuint)sizeof(Quaternion),	// FIELD_QUATERNION
		sizeof(int),				// FIELD_INTEGER
		sizeof(bool),				// FIELD_BOOLEAN
		sizeof(short),				// FIELD_SHORT
		sizeof(char),				// FIELD_CHARACTER
		(nuint)sizeof(Color),		// FIELD_COLOR32
		sizeof(int),				// FIELD_EMBEDDED	(handled specially)
		sizeof(int),				// FIELD_CUSTOM		(handled specially)
	
		//---------------------------------

		SIZEOF_MANAGED,				// FIELD_CLASSPTR
		sizeof(uint),				// FIELD_EHANDLE
		SIZEOF_MANAGED,				// FIELD_EDICT

		(nuint)sizeof(Vector3),		// FIELD_POSITION_VECTOR
		sizeof(float),				// FIELD_TIME
		sizeof(int),				// FIELD_TICK
		SIZEOF_MANAGED,				// FIELD_MODELNAME
		SIZEOF_MANAGED,				// FIELD_SOUNDNAME

		SIZEOF_MANAGED,				// FIELD_INPUT		(uses custom type)
		SIZEOF_MANAGED,				// FIELD_FUNCTION
		(nuint)sizeof(Matrix4x4),	// FIELD_VMATRIX
		(nuint)sizeof(Matrix4x4),	// FIELD_VMATRIX_WORLDSPACE
		(nuint)sizeof(Matrix3x4),	// FIELD_MATRIX3X4_WORLDSPACE	// NOTE: Use array(FIELD_FLOAT, 12) for matrix3x4_t NOT in worldspace
		(nuint)sizeof(Interval),	// FIELD_INTERVAL
		sizeof(int),				// FIELD_MODELINDEX
	];

	public nuint ComputePackedSize_R(DataMap? map) {
		if (map == null) {
			Assert(false);
			return 0;
		}

		// Already computed
		if (map.PackedOffsetsComputed)
			return map.PackedSize;

		nuint currentPosition = 0;

		// Recurse to base classes first...
		if (map.BaseMap != null)
			currentPosition += ComputePackedSize_R(map.BaseMap);

		int c = map.DataNumFields;
		int i;
		TypeDescription field;

		for (i = 0; i < c; i++) {
			field = map.DataDesc[i];

			// Always descend into embedded types...
			if (field.FieldType != FieldType.Embedded)
				// Skip all private fields
				if ((field.Flags & FieldTypeDescFlags.Private) != 0)
					continue;

			field.PackedOffset = currentPosition;
			int sizeOfField = 0;
			switch (field.FieldType) {
				default:
				case FieldType.ModelIndex:
				case FieldType.ModelName:
				case FieldType.SoundName:
				case FieldType.Time:
				case FieldType.Tick:
				case FieldType.Custom:
				case FieldType.ClassPtr:
				case FieldType.EDict:
				case FieldType.PositionVector:
				case FieldType.Function:
					Assert(false);
					break;
				case FieldType.Embedded:
					Assert(field.TD != null);
					nuint embeddedSize = ComputePackedSize_R(field.TD);
					sizeOfField = (int)embeddedSize;
					break;
				case FieldType.Float: sizeOfField = sizeof(float); break;
				case FieldType.Double: sizeOfField = sizeof(double); break;
				case FieldType.Vector: sizeOfField = (int)Unsafe.SizeOf<Vector3>(); break;
				case FieldType.Quaternion: sizeOfField = (int)Unsafe.SizeOf<Quaternion>(); break;
				case FieldType.Integer: sizeOfField = sizeof(int); break;
				case FieldType.EHandle: sizeOfField = (int)Unsafe.SizeOf<EHANDLE>(); break;
				case FieldType.Short: sizeOfField = sizeof(short); break;
				case FieldType.String: sizeOfField = (int)Unsafe.SizeOf<GCHandle>(); break;
				case FieldType.Color32: sizeOfField = (int)Unsafe.SizeOf<Color>(); break;
				case FieldType.Boolean: sizeOfField = sizeof(bool); break;
				case FieldType.Character: sizeOfField = sizeof(sbyte); break;
				case FieldType.StringCharacter: sizeOfField = sizeof(char); break;
				case FieldType.Void: {
						// Special case, just skip it
					}
					break;
			}
			currentPosition += (nuint)(sizeOfField * field.FieldSize);
		}

		map.PackedSize = currentPosition;
		map.PackedOffsetsComputed = true;

		return currentPosition;
	}

	public void ComputePackedOffsets() {
		DataMap? map = GetPredDescMap();
		if (map == null)
			return;

		if (map.PackedOffsetsComputed)
			return;

		ComputePackedSize_R(map);
		Assert(map.PackedOffsetsComputed);
	}

	public void DumpPackedOffsets(DataMap? map) {
		if (map == null) {
			Assert(false);
			return;
		}

		ComputePackedSize_R(map);

		/*
		List<(string ClassName, string FieldName, nuint AbsoluteOffset, int ByteSize)> entries = [];
		CollectPackedOffsets_R(map, 0, entries);

		entries.Sort((a, b) => a.AbsoluteOffset.CompareTo(b.AbsoluteOffset));
		Msg($"PackedOffset dump for {map.DataClassName} ({entries.Count} fields)\n");
		foreach (var entry in entries)
			Msg($"  [{entry.AbsoluteOffset,5}, {entry.AbsoluteOffset + (nuint)entry.ByteSize,5}) {entry.ClassName}::{entry.FieldName}\n");

		for (int i = 0; i < entries.Count - 1; i++) {
			nuint end = entries[i].AbsoluteOffset + (nuint)entries[i].ByteSize;
			if (end > entries[i + 1].AbsoluteOffset)
				Warning($"PackedOffset OVERLAP: {entries[i].ClassName}::{entries[i].FieldName} [{entries[i].AbsoluteOffset}, {end}) overlaps {entries[i + 1].ClassName}::{entries[i + 1].FieldName} starting at {entries[i + 1].AbsoluteOffset}\n");
		}
		*/
	}

	void CollectPackedOffsets_R(DataMap? map, nuint baseOffset, List<(string ClassName, string FieldName, nuint AbsoluteOffset, int ByteSize)> entries) {
		if (map == null)
			return;

		if (map.BaseMap != null)
			CollectPackedOffsets_R(map.BaseMap, baseOffset, entries);

		int c = map.DataNumFields;
		for (int i = 0; i < c; i++) {
			TypeDescription field = map.DataDesc[i];

			// Always descend into embedded types...
			if (field.FieldType != FieldType.Embedded)
				// Skip all private fields
				if ((field.Flags & FieldTypeDescFlags.Private) != 0)
					continue;

			nuint absoluteOffset = baseOffset + field.PackedOffset;

			if (field.FieldType == FieldType.Embedded) {
				CollectPackedOffsets_R(field.TD, absoluteOffset, entries);
				continue;
			}

			int sizeOfField;
			switch (field.FieldType) {
				default:
				case FieldType.ModelIndex:
				case FieldType.ModelName:
				case FieldType.SoundName:
				case FieldType.Time:
				case FieldType.Tick:
				case FieldType.Custom:
				case FieldType.ClassPtr:
				case FieldType.EDict:
				case FieldType.PositionVector:
				case FieldType.Function:
					sizeOfField = 0;
					break;
				case FieldType.Float: sizeOfField = sizeof(float); break;
				case FieldType.Double: sizeOfField = sizeof(double); break;
				case FieldType.Vector: sizeOfField = (int)Unsafe.SizeOf<Vector3>(); break;
				case FieldType.Quaternion: sizeOfField = (int)Unsafe.SizeOf<Quaternion>(); break;
				case FieldType.Integer: sizeOfField = sizeof(int); break;
				case FieldType.EHandle: sizeOfField = (int)Unsafe.SizeOf<EHANDLE>(); break;
				case FieldType.Short: sizeOfField = sizeof(short); break;
				case FieldType.String: sizeOfField = (int)Unsafe.SizeOf<GCHandle>(); break;
				case FieldType.Color32: sizeOfField = (int)Unsafe.SizeOf<Color>(); break;
				case FieldType.Boolean: sizeOfField = sizeof(bool); break;
				case FieldType.Character: sizeOfField = sizeof(sbyte); break;
				case FieldType.StringCharacter: sizeOfField = sizeof(char); break;
				case FieldType.Void: sizeOfField = 0; break;
			}

			entries.Add((map.DataClassName, field.FieldName, absoluteOffset, sizeOfField * field.FieldSize));
		}
	}

	ClientShadowHandle_t ShadowHandle = CLIENTSHADOW_INVALID_HANDLE;

	public bool UsesPowerOfTwoFrameBufferTexture() => false;
	public bool UsesFullFrameBufferTexture() => false;

	public virtual ClientShadowHandle_t GetShadowHandle() => ShadowHandle;

	public virtual int GetBody() => 0;

	public virtual bool LODTest() => true;

	public virtual void GetShadowRenderBounds(out Vector3 mins, out Vector3 maxs, ShadowType shadowType) {
		EntClientFlags |= EntClientFlags.GettingShadowRenderBounds;
		GetRenderBounds(out mins, out maxs);
		EntClientFlags &= ~EntClientFlags.GettingShadowRenderBounds;
	}

	public Color GetRenderColor() => ColorRender;
	public RenderMode GetRenderMode() => (RenderMode)RenderMode;

	public virtual bool ShouldReceiveProjectedTextures(ShadowFlags flags) {
		if (IsEffectActive(EntityEffects.NoDraw))
			return false;

		if ((flags & ShadowFlags.Flashlight) != 0) {
			if (GetRenderMode() > Source.RenderMode.Normal && GetRenderColor().A == 0)
				return false;

			return true;
		}

		Assert((flags & ShadowFlags.Shadow) != 0);

		if (IsEffectActive(EntityEffects.NoReceiveShadow))
			return false;

		if (modelinfo.GetModelType(Model) == ModelType.Studio)
			return false;

		return true;
	}

	public bool GetShadowCastDistance(ref float dist, ShadowType shadowType) {
		if (ShadowCastDistance != 0.0f) {
			dist = ShadowCastDistance;
			return true;
		}
		return false;
	}

	EHANDLE ShadowDirUseOtherEntity = new();

	public bool GetShadowCastDirection(ref Vector3 direction, ShadowType shadowType) {
		if (ShadowDirUseOtherEntity.Get() != null)
			return ShadowDirUseOtherEntity.Get()!.GetShadowCastDirection(ref direction, shadowType);
		return false;
	}

	public bool IsShadowDirty() => IsEFlagSet(EFL.DirtyShadowUpdate);
	public void MarkShadowDirty(bool bDirty) {
		if (bDirty)
			AddEFlags(EFL.DirtyShadowUpdate);
		else
			RemoveEFlags(EFL.DirtyShadowUpdate);
	}

	public IClientRenderable? GetShadowParent() {
		C_BaseEntity? parent = GetMoveParent();
		return parent?.GetClientRenderable();
	}

	public IClientRenderable? FirstShadowChild() {
		C_BaseEntity? parent = FirstMoveChild();
		return parent?.GetClientRenderable();
	}

	public IClientRenderable? NextShadowPeer() {
		C_BaseEntity? parent = NextMovePeer();
		return parent?.GetClientRenderable();
	}

	public virtual ShadowType ShadowCastType() {
		if (IsEffectActive(EntityEffects.NoDraw | EntityEffects.NoShadow))
			return ShadowType.None;

		ModelType modelType = modelinfo.GetModelType(Model);
		return (modelType == ModelType.Studio) ? ShadowType.RenderToTexture : ShadowType.None;
	}

	public virtual ref readonly Matrix3x4 RenderableToWorldTransform() => ref EntityToWorldTransform();

	public virtual int LookupAttachment(ReadOnlySpan<char> attachmentName) => -1;

	public Span<float> GetRenderClipPlane() {
		if (EnableRenderingClipPlane)
			return RenderingClipPlane;
		else
			return null;
	}

	public virtual int GetSkin() => 0;
	public virtual void OnThreadedDrawSetup() { }

	public virtual bool UsesFlexDelayedWeights() => false;

	public void RecordToolMessage() { }
	public bool IgnoresZBuffer() => RenderMode == (byte)Source.RenderMode.Glow || RenderMode == (byte)Source.RenderMode.WorldGlow;
}

public class VarMapEntry
{
	public required LatchFlags Type;
	public required bool NeedsToInterpolate;
	public required DynamicAccessor Accessor;
	public required IInterpolatedVar Watcher;
}

public struct VarMapping
{
	public int InterpolatedEntries;
	public TimeUnit_t LastInterpolationTime;
	public List<VarMapEntry> Entries = [];
	public VarMapping() {
		InterpolatedEntries = 0;
	}
}
