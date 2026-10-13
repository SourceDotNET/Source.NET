using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

using DEFINE = Source.DEFINE<Game.Server.AI_Hint>;

namespace Game.Server;

public enum Hint : short
{
	Any = -1,
	None = 0,
	NotUsedWorldDoor,
	WorldWindow,
	NotUsedWorldButton,
	NotUsedWorldMachinery,
	NotUsedWorldLedge,
	NotUsedWorldLightSource,
	NotUsedWorldHeatSource,
	NotUsedWorldBlinkingLight,
	NotUsedWorldBrightColors,
	NotUsedWorldHumanBlood,
	NotUsedWorldAlienBlood,

	WorldWorkPosition,
	WorldVisuallyInteresting,
	WorldVisuallyInterestingDontAim,
	WorldInhibitCombineMines,
	WorldVisuallyInterestingStealth,

	TacticalCoverMed = 100,
	TacticalCoverLow,
	TacticalSpawn,
	TacticalPinch,
	NotUsedTacticalGuard,
	TacticalEnemyDisadvantaged,
	NotUsedHealthKit,

	NotUsedUrbanStreetcorner = 200,
	NotUsedUrbanStreetlamp,
	NotUsedUrbanDarkSpot,
	NotUsedUrbanPoster,
	NotUsedUrbanShelter,

	NotUsedAssassinSecluded = 300,
	NotUsedAssassinRafters,
	NotUsedAssassinGround,
	NotUsedAssassinMonkeybars,

	AntlionBurrowPoint = 400,
	AntlionThumperFleePoint,

	HeadcrabBurrowPoint = 450,
	HeadcrabExitPodPoint,

	NotUsedRollerPatrolPoint = 500,
	NotUsedRollerCleanupPoint,

	NotUsedPstormRockSpawn = 600,

	CrowFlytoPoint = 700,

	BugPatrolPoint = 800,

	FollowWaitPoint = 900,
	JumpOverride = 901,
	PlayerSquadTransitonPoint = 902,
	NpcExitPoint = 903,
	StriderNode = 904,

	PlayerAllyMoveAwayDest = 950,
	PlayerAllyFearDest,

	HL1WorldMachinery = 1000,
	HL1WorldBlinkingLight,
	HL1WorldHumanBlood,
	HL1WorldAlienBlood,

	CStrikeHostageEscape = 1100,
}

[Flags]
public enum AI_HintNodeFlags
{
	None = 0x00000000,
	Visible = 0x00000001,
	Nearest = 0x00000002,
	Random = 0x00000004,
	Clear = 0x00000008,
	UseGroup = 0x00000010,
	VisibleToPlayer = 0x00000020,
	NotVisibleToPlayer = 0x00000040,
	ReportFailures = 0x00000080,
	InViewcone = 0x00000100,
	InAimcone = 0x00000200,
	NPCInNodeFOV = 0x00000400,
	NotCloseToEnemy = 0x00000800,
	HasLOSToPlayer = 0x00001000,
	HasEyePositionLOSToPlayer = 0x00002000,
}

public class HintCriteria
{
	struct HintZone
	{
		public Vector3 Position;
		public float RadiusSqr;
	}

	readonly List<Hint> HintTypes = [];

	AI_HintNodeFlags Flags;
	Hint FirstHintType;
	Hint LastHintType;
	string? Group;

	readonly List<HintZone> ZoneInclude = [];
	readonly List<HintZone> ZoneExclude = [];

	public HintCriteria() {
		FirstHintType = Hint.None;
		LastHintType = Hint.None;
		Group = null;
		Flags = 0;
		HintTypes.Clear();
	}

	public bool HasFlag(AI_HintNodeFlags bitmask) => (Flags & bitmask) != 0;

	public void SetHintType(Hint hintType) {
		FirstHintType = hintType;
		LastHintType = Hint.None;
		HintTypes.Clear();
	}

	public void AddHintType(Hint hintType) => HintTypes.Add(hintType);

	public int NumHintTypes() => HintTypes.Count;

	public Hint GetHintType(int idx) => HintTypes[idx];

	public Hint GetFirstHintType() => FirstHintType;
	public Hint GetLastHintType() => LastHintType;

	public bool MatchesSingleHintType() {
		if (HintTypes.Count != 0)
			return false;

		if (FirstHintType != Hint.Any && LastHintType == Hint.None)
			return true;

		return false;
	}

	public bool MatchesHintType(Hint hintType) {
		int c = HintTypes.Count;
		for (int i = 0; i < c; ++i) {
			if (HintTypes[i] == hintType)
				return true;
		}

		if (GetFirstHintType() != Hint.Any) {
			if (GetLastHintType() == Hint.None) {
				if (GetFirstHintType() != hintType)
					return false;
			}
			else {
				if (hintType < GetFirstHintType() || hintType > GetLastHintType())
					return false;
			}

			return true;
		}

		return false;
	}

	public void SetHintTypeRange(Hint firstType, Hint lastType) {
		if (lastType < firstType) {
			DevMsg(2, "Hint Type Range is backwards - Fixing up.\n");

			(lastType, firstType) = (firstType, lastType);
		}

		FirstHintType = firstType;
		LastHintType = lastType;
		HintTypes.Clear();
	}

	public void SetFlag(AI_HintNodeFlags bitmask) => Flags |= bitmask;

	public void ClearFlag(AI_HintNodeFlags bitmask) => Flags &= ~bitmask;

	public void SetGroup(string? group) => Group = group;
	public string? GetGroup() => Group;

	public bool HasIncludeZones() => ZoneInclude.Count != 0;
	public bool HasExcludeZones() => ZoneExclude.Count != 0;

	static void AddZone(List<HintZone> list, in Vector3 position, float radius) {
		list.Add(new HintZone {
			Position = position,
			RadiusSqr = radius * radius
		});
	}

	public void AddIncludePosition(in Vector3 position, float radius) => AddZone(ZoneInclude, position, radius);

	public void AddExcludePosition(in Vector3 position, float radius) => AddZone(ZoneExclude, position, radius);

	static bool InZone(List<HintZone> zone, in Vector3 testPosition) {
		int numZones = zone.Count;

		for (int i = 0; i < numZones; i++) {
			if ((zone[i].Position - testPosition).LengthSquared() < zone[i].RadiusSqr)
				return true;
		}

		return false;
	}

	public bool InIncludedZone(in Vector3 testPosition) => InZone(ZoneInclude, testPosition);

	public bool InExcludedZone(in Vector3 testPosition) => InZone(ZoneExclude, testPosition);
}

[LinkEntityToClass("ai_hint")]
public class AI_Hint : ServerOnlyEntity
{
	[Flags]
	public enum SF
	{
		AllowJumpUp = 65536,
	}

	public static readonly new DataMap DataDesc = new(typeof(AI_Hint), BaseEntity.DataDesc, [
		DEFINE.EMBEDDED(nameof(NodeData)),

		DEFINE.FIELD(nameof(HintOwner), FieldType.EHandle),
		DEFINE.FIELD(nameof(NextUseTime), FieldType.Time),
		DEFINE.FIELD(nameof(Forward), FieldType.Vector),
		DEFINE.KEYFIELD(nameof(NodeFOV), FieldType.Float, "nodeFOV"),

		DEFINE.INPUTFUNC(FieldType.Void, "EnableHint", nameof(InputEnableHint), (INPUTFUNCPTR)((self, data) => ((AI_Hint)self).InputEnableHint(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "DisableHint", nameof(InputDisableHint), (INPUTFUNCPTR)((self, data) => ((AI_Hint)self).InputDisableHint(data))),

		DEFINE.OUTPUT(nameof(OnNPCStartedUsing), "OnNPCStartedUsing", eventFuncs),
		DEFINE.OUTPUT(nameof(OnNPCStoppedUsing), "OnNPCStoppedUsing", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public HintNodeData NodeData = new();
	int TargetNodeID;
	EHANDLE HintOwner = new();
	TimeUnit_t NextUseTime;
	readonly OutputEHANDLE OnNPCStartedUsing = new();
	readonly OutputEHANDLE OnNPCStoppedUsing = new();
	float NodeFOV;
	Vector3 Forward;

	public AI_Hint() {
		NextUseTime = 0;
		TargetNodeID = NO_NODE;
	}

	public string? GetGroup() => NodeData.Group;
	public BaseEntity? User() => HintOwner.Get();
	public Hint HintType() => (Hint)NodeData.HintType;
	public string? HintActivityName() => NodeData.ActivityName;
	public int GetTargetNode() => TargetNodeID;
	public bool IsDisabled() => NodeData.Disabled != 0;
	public void SetDisabled(bool disabled) => NodeData.Disabled = disabled ? 1 : 0;
	public HintIgnoreFacing GetIgnoreFacing() => NodeData.IgnoreFacing;
	public NPCState GetMinState() => NodeData.MinState;
	public NPCState GetMaxState() => NodeData.MaxState;
	public int GetNodeId() => NodeData.NodeID;
	public int GetWCId() => NodeData.WCNodeID;

	public override EntityCapabilities ObjectCaps() => base.ObjectCaps() & ~EntityCapabilities.AcrossTransition;

	public void SetHintType(Hint hintType, bool force = false) {
		if (!force && (short)hintType == NodeData.HintType)
			return;

		AI_HintManager.RemoveHintByType(this);
		NodeData.HintType = (short)hintType;
		AI_HintManager.AddHintByType(this);
	}

	void InputEnableHint(InputData inputdata) => NodeData.Disabled = 0;

	void InputDisableHint(InputData inputdata) => NodeData.Disabled = 1;

	public override void Spawn() {
		GetVectors(out Forward, out _, out _);

		if (NodeFOV != 360)
			NodeFOV = MathF.Cos(MathLib.DEG2RAD(NodeFOV / 2));

		SetSolid(SolidType.None);
	}

	public override void Activate() {
		base.Activate();
		AI_HintManager.AddHint(this);
	}

	public override void UpdateOnRemove() {
		AI_HintManager.RemoveHint(this);
		base.UpdateOnRemove();
	}

	public void GetPosition(BaseCombatCharacter? bcc, out Vector3 position) {
		if (NodeData.NodeID != NO_NODE)
			position = g_pBigAINet!.GetNodePosition(bcc, NodeData.NodeID);
		else
			position = GetAbsOrigin();
	}

	public void GetPosition(AI_HullType hull, out Vector3 position) {
		if (NodeData.NodeID != NO_NODE)
			position = g_pBigAINet!.GetNodePosition(hull, NodeData.NodeID);
		else
			position = GetAbsOrigin();
	}

	public Vector3 GetDirection() => Util.YawToVector(Yaw());

	public float Yaw() {
		if (NodeData.NodeID != NO_NODE)
			return g_pBigAINet!.GetNodeYaw(NodeData.NodeID);
		else
			return GetLocalAngles().Y;
	}

	bool IsViewable() {
		if (NodeData.Disabled != 0)
			return false;

		switch (HintType()) {
			case Hint.WorldVisuallyInteresting:
			case Hint.WorldVisuallyInterestingDontAim:
			case Hint.WorldVisuallyInterestingStealth:
				return true;
		}

		return false;
	}

	public bool IsInNodeFOV(BaseEntity other) {
		if (NodeFOV == 360)
			return true;

		Vector3 vecToNPC = other.GetAbsOrigin() - GetAbsOrigin();
		MathLib.VectorNormalize(ref vecToNPC);
		float dot = Vector3.Dot(vecToNPC, Forward);

		if (dot > NodeFOV)
			return true;

		return false;
	}

	public bool Lock(BaseEntity? npc) {
		if (HintOwner.Get() != npc && HintOwner.Get() != null)
			return false;
		HintOwner.Set(npc);
		return true;
	}

	public void Unlock(float delay = 0.0f) {
		HintOwner.Set(null);
		NextUseTime = gpGlobals.CurTime + delay;
	}

	public bool IsLockedBy(BaseEntity? npc) => HintOwner.Get() == npc;

	public bool IsLocked() {
		if (NodeData.Disabled != 0)
			return true;

		if (gpGlobals.CurTime < NextUseTime)
			return true;

		if (HintOwner.Get() != null)
			return true;
		return false;
	}

	public bool HintMatchesCriteria(AI_BaseNPC? npc, HintCriteria hintCriteria, in Vector3 position, ref float nearestDistance, bool ignoreLock = false, bool ignoreHintType = false) {
		if (!ignoreLock && IsLocked())
			return false;

		if (!ignoreHintType && !hintCriteria.MatchesHintType(HintType()))
			return false;

		if (GetMinState() > NPCState.Idle || GetMaxState() < NPCState.Combat) {
			if (npc != null && (npc.GetState() < GetMinState() || npc.GetState() > GetMaxState()))
				return false;
		}

		if (hintCriteria.GetGroup() != null) {
			if (GetGroup() == null || GetGroup() != hintCriteria.GetGroup())
				return false;
		}

		if (hintCriteria.HasIncludeZones() && hintCriteria.InIncludedZone(GetAbsOrigin()) == false)
			return false;

		if (hintCriteria.HasExcludeZones() && hintCriteria.InExcludedZone(GetAbsOrigin()))
			return false;

		if (npc != null && npc.FValidateHintType(this) == false)
			return false;

		if (hintCriteria.HasFlag(AI_HintNodeFlags.NPCInNodeFOV)) {
			if (npc == null)
				AssertMsg(false, "Hint node attempted to verify NPC in node FOV without NPC!\n");
			else {
				if (!IsInNodeFOV(npc))
					return false;
			}
		}

		if (hintCriteria.HasFlag(AI_HintNodeFlags.InAimcone)) {
			if (npc == null)
				AssertMsg(false, "Hint node attempted to find node in aimcone without specifying NPC!\n");
			else {
				if (!npc.FInAimCone(GetAbsOrigin()))
					return false;
			}
		}

		if (hintCriteria.HasFlag(AI_HintNodeFlags.InViewcone)) {
			if (npc == null)
				AssertMsg(false, "Hint node attempted to find node in viewcone without specifying NPC!\n");
			else {
				if (!npc.FInViewCone(this))
					return false;
			}
		}

		if (hintCriteria.HasFlag(AI_HintNodeFlags.NotCloseToEnemy)) {
			if (npc == null)
				AssertMsg(false, "Hint node attempted to find node not close to enemy without specifying NPC!\n");
			else {
				if (npc.GetEnemy() != null) {
					float distHintToEnemySqr = Vector3.DistanceSquared(GetAbsOrigin(), npc.GetEnemy()!.GetAbsOrigin());

					if (distHintToEnemySqr < (30.0f * 12.0f) * (30.0f * 12.0f))
						return false;
				}
			}
		}

		{
			if (hintCriteria.HasFlag(AI_HintNodeFlags.Visible)) {
				if (npc == null)
					AssertMsg(false, "Hint node attempted to find visible node without specifying NPC!\n");
				else {
					if (NodeData.NodeID == NO_NODE) {
						if (!npc.FVisible(this))
							return false;
					}
					else {
						GetPosition(npc, out Vector3 hintPos);
						Util.TraceLine(npc.EyePosition(), hintPos + npc.GetViewOffset(), Mask.NPCSolidBrushOnly, npc, Source.CollisionGroup.None, out Trace tr);
						if (tr.Fraction != 1.0f)
							return false;
					}
				}
			}
		}

		if (hintCriteria.HasFlag(AI_HintNodeFlags.Clear)) {
			if (npc == null)
				AssertMsg(false, "Hint node attempted to find clear node without specifying NPC!\n");
			else {
				Util.TraceHull(GetAbsOrigin(), GetAbsOrigin(), npc.WorldAlignMins(), npc.WorldAlignMaxs(),
					Mask.Solid, npc, Source.CollisionGroup.None, out Trace tr);

				if (tr.Fraction != 1.0)
					return false;
			}
		}

		if (hintCriteria.HasFlag(AI_HintNodeFlags.Nearest)) {
			float distance = (GetAbsOrigin() - position).Length();

			if (distance > nearestDistance)
				return false;

			nearestDistance = distance;
		}

		if (hintCriteria.HasFlag(AI_HintNodeFlags.HasLOSToPlayer | AI_HintNodeFlags.HasEyePositionLOSToPlayer)) {
			BasePlayer? player = AI_GetClosestPlayer();
			if (player != null) {
				Vector3 dest = GetAbsOrigin();

				if (hintCriteria.HasFlag(AI_HintNodeFlags.HasEyePositionLOSToPlayer))
					dest += npc!.DefaultEyeOffset;

				if (!player.FVisible(dest))
					return false;
			}
		}

		if (hintCriteria.HasFlag(AI_HintNodeFlags.NotVisibleToPlayer | AI_HintNodeFlags.VisibleToPlayer)) {
			bool wasSeen = false;
			for (int i = 1; i <= gpGlobals.MaxClients; i++) {
				BasePlayer? player = Util.PlayerByIndex(i);

				if (player != null) {
					Vector3 lookDir = player.EyeDirection3D();
					Vector3 targetDir = GetAbsOrigin() - player.EyePosition();
					MathLib.VectorNormalize(ref targetDir);

					float dotPr = Vector3.Dot(lookDir, targetDir);
					if (dotPr > 0) {
						Util.TraceLine(player.EyePosition(), GetAbsOrigin(), Mask.SolidBrushOnly, player, Source.CollisionGroup.None, out Trace tr);

						if (tr.Fraction == 1.0) {
							if (hintCriteria.HasFlag(AI_HintNodeFlags.NotVisibleToPlayer))
								return false;
							wasSeen = true;
						}
					}
				}
			}

			if (!wasSeen && hintCriteria.HasFlag(AI_HintNodeFlags.VisibleToPlayer))
				return false;
		}

		return true;
	}

	public AI_Node? GetNode() {
		if (NodeData.NodeID != NO_NODE)
			return g_pBigAINet!.GetNode(NodeData.NodeID, false);
		return null;
	}

	public void DisableForSeconds(float seconds) => Unlock(seconds);

	public void EnableThink() {
		SetDisabled(false);
		SetThink(null);
	}

	public void FixupTargetNode() {
		if (NodeData.TargetWCNodeID != -1)
			TargetNodeID = g_pAINetworkManager!.GetEditOps().GetNodeIdFromWCId(NodeData.TargetWCNodeID);
		else
			TargetNodeID = NO_NODE;
	}

	public override void OnRestore() {
		base.OnRestore();

		NodeData.NodeID = g_pAINetworkManager!.GetEditOps().GetNodeIdFromWCId(NodeData.WCNodeID);
		FixupTargetNode();

		AI_Node? node = GetNode();

		if (node == null) {
			if (NodeData.WCNodeID > 0)
				DevMsg("Warning: AI hint has incorrect or no AI node\n");
		}
		else {
			NodeData.Position = node.GetOrigin();
			Teleport(NodeData.Position, null, null);
			node.SetHint(this);
		}
	}

	public void NPCStartedUsing(AI_BaseNPC npc) {
		EHANDLE hNPC = new();
		hNPC.Set(npc);
		OnNPCStartedUsing.Set(hNPC, npc, this);
	}

	public void NPCStoppedUsing(AI_BaseNPC npc) {
		EHANDLE hNPC = new();
		hNPC.Set(npc);
		OnNPCStoppedUsing.Set(hNPC, npc, this);
	}
}

public static class AI_HintManager
{
	const int HINT_HISTORY = 1 << 3;
	const int HINT_HISTORY_MASK = HINT_HISTORY - 1;

	static int gm_nFoundHintIndex = 0;
	static readonly AI_Hint?[] gm_pLastFoundHints = new AI_Hint?[HINT_HISTORY];
	static readonly List<AI_Hint> gm_AllHints = [];
	static readonly SortedDictionary<Hint, List<AI_Hint>> gm_TypedHints = [];

	static AI_Hint? AddFoundHint(AI_Hint? hint) {
		if (hint != null) {
			gm_nFoundHintIndex = (gm_nFoundHintIndex + 1) & HINT_HISTORY_MASK;
			gm_pLastFoundHints[gm_nFoundHintIndex] = hint;
		}
		return hint;
	}

	static int GetFoundHintCount() => HINT_HISTORY;

	static AI_Hint? GetFoundHint(int index) => gm_pLastFoundHints[(gm_nFoundHintIndex + index) & HINT_HISTORY_MASK];

	static AI_Hint? GetLastFoundHint() {
		for (int i = 0; i < HINT_HISTORY; ++i) {
			int slot = (gm_nFoundHintIndex - i) & HINT_HISTORY_MASK;
			if (gm_pLastFoundHints[slot] != null)
				return gm_pLastFoundHints[slot];
		}
		return null;
	}

	static void ResetFoundHints() {
		Array.Clear(gm_pLastFoundHints);
		gm_nFoundHintIndex = 0;
	}

	static bool IsInFoundHintList(AI_Hint hint) {
		for (int i = 0; i < HINT_HISTORY; ++i) {
			if (gm_pLastFoundHints[i] == hint)
				return true;
		}
		return false;
	}

	public static int FindAllHints(AI_BaseNPC? npc, in Vector3 position, HintCriteria hintCriteria, List<AI_Hint> result) {
		int c = gm_AllHints.Count;
		if (c == 0)
			return 0;

		bool hadNearest = hintCriteria.HasFlag(AI_HintNodeFlags.Nearest);
		hintCriteria.ClearFlag(AI_HintNodeFlags.Nearest);

		AI_Hint testHint;
		float unused = 0;
		for (int i = 0; i < c; ++i) {
			testHint = gm_AllHints[i];
			Assert(testHint != null);
			if (testHint!.HintMatchesCriteria(npc, hintCriteria, position, ref unused))
				result.Add(testHint);
		}

		if (hadNearest)
			hintCriteria.SetFlag(AI_HintNodeFlags.Nearest);

		return result.Count;
	}

	public static int FindAllHints(in Vector3 position, HintCriteria hintCriteria, List<AI_Hint> result) => FindAllHints(null, position, hintCriteria, result);
	public static int FindAllHints(AI_BaseNPC npc, HintCriteria hintCriteria, List<AI_Hint> result) => FindAllHints(npc, npc.GetAbsOrigin(), hintCriteria, result);

	public static AI_Hint? FindHintRandom(AI_BaseNPC? npc, in Vector3 position, HintCriteria hintCriteria) {
		List<AI_Hint> hintList = [];

		if (FindAllHints(npc, position, hintCriteria, hintList) > 0)
			return AddFoundHint(hintList[random.RandomInt(0, hintList.Count - 1)]);

		ResetFoundHints();
		return null;
	}

	public static AI_Hint? FindHint(AI_BaseNPC? npc, in Vector3 position, HintCriteria hintCriteria) {
		bool singleType = hintCriteria.MatchesSingleHintType();
		bool lookingForNearest = hintCriteria.HasFlag(AI_HintNodeFlags.Nearest);
		bool ignoreHintType = true;

		List<List<AI_Hint>> lists = [];
		if (singleType) {
			if (gm_TypedHints.TryGetValue(hintCriteria.GetFirstHintType(), out List<AI_Hint>? typed))
				lists.Add(typed);
		}
		else {
			int typeCount = hintCriteria.NumHintTypes();
			if (typeCount > 0) {
				for (int listType = 0; listType < typeCount; ++listType) {
					if (gm_TypedHints.TryGetValue(hintCriteria.GetHintType(listType), out List<AI_Hint>? typed))
						lists.Add(typed);
				}
			}
			else {
				lists.Add(gm_AllHints);
				ignoreHintType = false;
			}
		}

		AI_Hint? bestHint = null;

		int listCount = lists.Count;

		if (listCount == 0)
			return null;

		int i, count;
		AI_Hint? testHint;

		float bestDistance = MAX_TRACE_LENGTH;

		if (!lookingForNearest) {
			count = GetFoundHintCount();
			for (i = 0; i < count; ++i) {
				testHint = GetFoundHint(i);
				if (testHint != null) {
					if (testHint.HintMatchesCriteria(npc, hintCriteria, position, ref bestDistance))
						return testHint;
				}
			}
		}

		bestDistance = MAX_TRACE_LENGTH;

		for (int listNum = 0; listNum < listCount; ++listNum) {
			List<AI_Hint> list = lists[listNum];
			count = list.Count;
			if (count == 0)
				continue;

			for (i = 0; i < count; ++i) {
				testHint = list[i];
				Assert(testHint != null);

				if (testHint!.HintMatchesCriteria(npc, hintCriteria, position, ref bestDistance, false, ignoreHintType)) {
					if (lookingForNearest)
						bestHint = testHint;
					else {
						AddFoundHint(testHint);
						return testHint;
					}
				}
			}
		}

		if (bestHint != null)
			AddFoundHint(bestHint);

		return bestHint;
	}

	public static AI_Hint? FindHint(AI_BaseNPC? npc, Hint hintType, AI_HintNodeFlags flags, float maxDist, Vector3? maxDistFrom = null) {
		Assert(npc != null);
		if (npc == null)
			return null;

		HintCriteria hintCriteria = new();
		hintCriteria.SetHintType(hintType);
		hintCriteria.SetFlag(flags);

		if ((flags & AI_HintNodeFlags.UseGroup) != 0)
			hintCriteria.SetGroup(npc.GetHintGroup());

		Vector3 position = maxDistFrom ?? npc.GetAbsOrigin();
		hintCriteria.AddIncludePosition(position, maxDist);

		if ((flags & AI_HintNodeFlags.Random) != 0)
			return FindHintRandom(npc, position, hintCriteria);

		return FindHint(npc, position, hintCriteria);
	}

	public static AI_Hint? FindHint(in Vector3 position, HintCriteria hintCriteria) => FindHint(null, position, hintCriteria);

	public static AI_Hint? FindHint(AI_BaseNPC? npc, HintCriteria hintCriteria) {
		Assert(npc != null);
		if (npc == null)
			return null;

		return FindHint(npc, npc.GetAbsOrigin(), hintCriteria);
	}

	public static AI_Hint? CreateHint(HintNodeData nodeData, ReadOnlyMemory<byte>? mapData = null) {
		ResetFoundHints();

		AI_Hint? hint = (AI_Hint?)CreateEntityByName("ai_hint");
		if (hint != null) {
			if (mapData != null) {
				EntityMapData entData = new(mapData.Value);
				hint.ParseMapData(entData);

				hint.SetClassname("ai_hint");
			}

			hint.SetName(nodeData.EntityName);
			hint.SetAbsOrigin(nodeData.Position);
			hint.NodeData = nodeData.Copy();
			Util.DispatchSpawn(hint);

			return hint;
		}

		return null;
	}

	public static void AddHint(AI_Hint hint) {
		gm_AllHints.Add(hint);
		AddHintByType(hint);
	}

	public static void AddHintByType(AI_Hint hint) {
		Hint type = hint.HintType();

		if (!gm_TypedHints.TryGetValue(type, out List<AI_Hint>? list)) {
			list = [];
			gm_TypedHints[type] = list;
		}
		list.Add(hint);
	}

	public static void RemoveHintByType(AI_Hint hintToRemove) {
		if (gm_TypedHints.TryGetValue(hintToRemove.HintType(), out List<AI_Hint>? list))
			list.Remove(hintToRemove);
	}

	public static void RemoveHint(AI_Hint hintToRemove) {
		gm_AllHints.Remove(hintToRemove);
		RemoveHintByType(hintToRemove);

		if (IsInFoundHintList(hintToRemove))
			ResetFoundHints();
	}

	public static AI_HintNodeFlags GetFlags(ReadOnlySpan<char> token) {
		if (token.Length <= 0)
			return AI_HintNodeFlags.None;

		string lowercase = new string(token).ToLowerInvariant();

		if ("none".Contains(lowercase))
			return AI_HintNodeFlags.None;

		AI_HintNodeFlags bits = 0;

		if ("visible".Contains(lowercase))
			bits |= AI_HintNodeFlags.Visible;

		if ("nearest".Contains(lowercase))
			bits |= AI_HintNodeFlags.Nearest;

		if ("random".Contains(lowercase))
			bits |= AI_HintNodeFlags.Random;

		if ((bits & AI_HintNodeFlags.Nearest) != 0 &&
			 (bits & AI_HintNodeFlags.Random) != 0) {
			bits &= ~AI_HintNodeFlags.Random;

			DevMsg($"HINTFLAGS:{token}, inconsistent, the nearest node is never a random hint node, treating as nearest request!\n");
		}

		return bits;
	}

	public static AI_Hint? GetFirstHint(out int iter) {
		if (gm_AllHints.Count == 0) {
			iter = -1;
			return null;
		}
		iter = 0;
		return gm_AllHints[0];
	}

	public static AI_Hint? GetNextHint(ref int iter) {
		if (iter != -1) {
			int i = iter + 1;
			if (gm_AllHints.Count <= i) {
				iter = -1;
				return null;
			}
			iter = i;
			return gm_AllHints[i];
		}
		return null;
	}

	public static void DumpHints() {
		AI_Hint? curHint = GetFirstHint(out int iter);
		while (curHint != null) {
			Vector3 v = curHint.GetAbsOrigin();
			Msg($"({v.X:F1}, {v.Y:F1}, {v.Z:F1}) -- Node ID: {curHint.GetNodeId()}; WC id {curHint.GetWCId()}; type {(int)curHint.HintType()}\n");
			curHint = GetNextHint(ref iter);
		}
	}

	[ConCommand("ai_dump_hints", "")]
	public static void ai_dump_hints(in TokenizedCommand args) {
		if (!Util.IsCommandIssuedByServerAdmin())
			return;

		DumpHints();
	}
}
