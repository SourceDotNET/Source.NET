using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;

namespace Game.Server;

public enum HintIgnoreFacing
{
	No,
	Yes,
	Default,
}

public class HintNodeData
{
	public static readonly DataMap DataDesc = new(typeof(HintNodeData), [
		DEFINE<HintNodeData>.FIELD(nameof(EntityName), FieldType.String),
		DEFINE<HintNodeData>.KEYFIELD(nameof(HintType), FieldType.Short, "hinttype"),
		DEFINE<HintNodeData>.KEYFIELD(nameof(Group), FieldType.String, "Group"),
		DEFINE<HintNodeData>.KEYFIELD(nameof(Disabled), FieldType.Integer, "StartHintDisabled"),
		DEFINE<HintNodeData>.FIELD(nameof(NodeID), FieldType.Integer),
		DEFINE<HintNodeData>.KEYFIELD(nameof(ActivityName), FieldType.String, "hintactivity"),
		DEFINE<HintNodeData>.KEYFIELD(nameof(TargetWCNodeID), FieldType.Integer, "TargetNode"),
		DEFINE<HintNodeData>.KEYFIELD(nameof(WCNodeID), FieldType.Integer, "nodeid"),
		DEFINE<HintNodeData>.KEYFIELD(nameof(IgnoreFacing), FieldType.Integer, "IgnoreFacing"),
		DEFINE<HintNodeData>.KEYFIELD(nameof(MinState), FieldType.Integer, "MinimumState"),
		DEFINE<HintNodeData>.KEYFIELD(nameof(MaxState), FieldType.Integer, "MaximumState"),
	]);

	public string? EntityName;
	public Vector3 Position;
	public short HintType;
	public int NodeID;
	public string? Group;
	public int Disabled;
	public string? ActivityName;
	public int TargetWCNodeID;
	public HintIgnoreFacing IgnoreFacing;
	public NPCState MinState;
	public NPCState MaxState;

	public int WCNodeID;
}

[LinkEntityToClass("info_hint")]
[LinkEntityToClass("info_node")]
[LinkEntityToClass("info_node_hint")]
[LinkEntityToClass("info_node_air")]
[LinkEntityToClass("info_node_air_hint")]
[LinkEntityToClass("info_node_climb")]
public class NodeEnt : ServerOnlyPointEntity
{
	public static readonly new DataMap DataDesc = new(typeof(NodeEnt), BaseEntity.DataDesc, [
		DEFINE<NodeEnt>.EMBEDDED(nameof(NodeData)),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public static int NodeCount;

	public HintNodeData NodeData = new();

	public override void Spawn() {
		NodeData.EntityName = GetEntityName();
		NodeData.Position = GetAbsOrigin();
		NodeData.NodeID = NO_NODE;
		if (NodeData.MinState == NPCState.None)
			NodeData.MinState = NPCState.Idle;
		if (NodeData.MaxState == NPCState.None)
			NodeData.MaxState = NPCState.Combat;

		if (FClassnameIs(this, "info_hint")) {
			if (NodeData.HintType == 0)
				Warning($"info_hint (HammerID: {NodeData.WCNodeID}, position ({NodeData.Position.X:0.00}, {NodeData.Position.Y:0.00}, {NodeData.Position.Z:0.00})) with no hint type.\n");
			Util.RemoveImmediate(this);
			return;
		}

		if (AI_NetworkManager.NetworksLoaded()) {
			NodeCount++;
			Util.RemoveImmediate(this);
			return;
		}
		else
			NodeCount++;

		AI_Node newNode = g_pBigAINet!.AddNode(GetAbsOrigin(), GetAbsAngles().Y);

		AI_NetworkEditTools editOps = g_pAINetworkManager!.GetEditOps();
		if (editOps.NodeIndexTable != null)
			editOps.NodeIndexTable[newNode.GetId()] = NodeData.WCNodeID;
		if (editOps.NextWCIndex <= NodeData.WCNodeID)
			editOps.NextWCIndex = NodeData.WCNodeID + 1;

		if (FClassnameIs(this, "info_node_air") || FClassnameIs(this, "info_node_air_hint"))
			newNode.NodeType = NodeType.Air;
		else if (FClassnameIs(this, "info_node_climb"))
			newNode.NodeType = NodeType.Climb;
		else
			newNode.NodeType = NodeType.Ground;

		newNode.NodeInfo = (NodeInfoBits)(SpawnFlags << AI_Node.NODE_ENT_FLAGS_SHIFT);

		Util.RemoveImmediate(this);
	}
}
