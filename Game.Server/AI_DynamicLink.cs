using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

public enum DynamicLinkState
{
	Off = 0,
	On = 1,
}

[LinkEntityToClass("info_node_link_controller")]
public class AI_DynamicLinkController : ServerOnlyEntity
{
	public static readonly new DataMap DataDesc = new(typeof(AI_DynamicLinkController), BaseEntity.DataDesc, [
		DEFINE<AI_DynamicLinkController>.KEYFIELD(nameof(LinkState), FieldType.Integer, "initialstate"),
		DEFINE<AI_DynamicLinkController>.KEYFIELD(nameof(AllowUse), FieldType.String, "AllowUse"),
		DEFINE<AI_DynamicLinkController>.KEYFIELD(nameof(InvertAllow), FieldType.Boolean, "InvertAllow"),
		DEFINE<AI_DynamicLinkController>.KEYFIELD(nameof(UseAirLinkRadius), FieldType.Boolean, "useairlinkradius"),

		DEFINE<AI_DynamicLinkController>.INPUTFUNC(FieldType.Void, "TurnOn", nameof(InputTurnOn), (INPUTFUNCPTR)((self, data) => ((AI_DynamicLinkController)self).InputTurnOn(data))),
		DEFINE<AI_DynamicLinkController>.INPUTFUNC(FieldType.Void, "TurnOff", nameof(InputTurnOff), (INPUTFUNCPTR)((self, data) => ((AI_DynamicLinkController)self).InputTurnOff(data))),
		DEFINE<AI_DynamicLinkController>.INPUTFUNC(FieldType.String, "SetAllowed", nameof(InputSetAllowed), (INPUTFUNCPTR)((self, data) => ((AI_DynamicLinkController)self).InputSetAllowed(data))),
		DEFINE<AI_DynamicLinkController>.INPUTFUNC(FieldType.Boolean, "SetInvert", nameof(InputSetInvert), (INPUTFUNCPTR)((self, data) => ((AI_DynamicLinkController)self).InputSetInvert(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public void GenerateLinksFromVolume() {
		Assert(ControlledLinks.Count == 0);

		int nodeCount = g_pBigAINet!.NumNodes();
		AI_Node?[] nodes = g_pBigAINet.AccessNodes()!;

		float minDistCareSq;
		if (UseAirLinkRadius)
			minDistCareSq = (AI_Node.MAX_AIR_NODE_LINK_DIST + 0.1f) * (AI_Node.MAX_AIR_NODE_LINK_DIST + 0.1f);
		else
			minDistCareSq = (AI_Node.MAX_NODE_LINK_DIST + 0.1f) * (AI_Node.MAX_NODE_LINK_DIST + 0.1f);

		Vector3 origin = WorldSpaceCenter();
		CollisionProp().WorldSpaceAABB(out Vector3 absMins, out Vector3 absMaxs);
		absMins -= new Vector3(1, 1, 1);
		absMaxs += new Vector3(1, 1, 1);

		for (int i = 0; i < nodeCount; i++) {
			AI_Node node = nodes[i]!;
			ref readonly Vector3 nodeOrigin = ref node.GetOrigin();
			if (Vector3.DistanceSquared(origin, nodeOrigin) < minDistCareSq) {
				int linkCount = node.NumLinks();
				for (int j = 0; j < linkCount; j++) {
					int linkDest = node.GetLinkByIndex(j).DestNodeID(i);
					if (linkDest > i) {
						ref readonly Vector3 originOther = ref nodes[linkDest]!.GetOrigin();
						if (Vector3.DistanceSquared(origin, originOther) < minDistCareSq) {
							Vector3 delta = originOther - nodeOrigin;
							Vector3 invDelta = new(delta.X != 0 ? 1.0f / delta.X : float.MaxValue, delta.Y != 0 ? 1.0f / delta.Y : float.MaxValue, delta.Z != 0 ? 1.0f / delta.Z : float.MaxValue);
							if (CollisionUtils.IsBoxIntersectingRay(absMins, absMaxs, nodeOrigin, delta, invDelta)) {
								AI_DynamicLink link = (AI_DynamicLink)CreateEntityByName("info_node_link")!;
								link.SrcID = i;
								link.DestID = linkDest;
								link.SrcEditID = g_pAINetworkManager!.GetEditOps().GetWCIdFromNodeId(link.SrcID);
								link.DestEditID = g_pAINetworkManager.GetEditOps().GetWCIdFromNodeId(link.DestID);
								link.LinkState = LinkState;
								link.AllowUse = AllowUse;
								link.InvertAllow = InvertAllow;
								link.FixedUpIds = true;
								link.NotSaved = true;

								link.Spawn();
								ControlledLinks.Add(new(link.GetRefEHandle()));
							}
						}
					}
				}
			}
		}
	}

	public void InputTurnOn(InputData inputdata) {
		for (int i = 0; i < ControlledLinks.Count; i++) {
			if (ControlledLinks[i].Get() == null) {
				ControlledLinks.RemoveAt(i);
				if (i >= ControlledLinks.Count)
					break;
			}
			ControlledLinks[i].Get()!.InputTurnOn(inputdata);
		}

		LinkState = DynamicLinkState.On;
	}

	public void InputTurnOff(InputData inputdata) {
		for (int i = 0; i < ControlledLinks.Count; i++) {
			if (ControlledLinks[i].Get() == null) {
				ControlledLinks.RemoveAt(i);
				if (i >= ControlledLinks.Count)
					break;
			}
			ControlledLinks[i].Get()!.InputTurnOff(inputdata);
		}

		LinkState = DynamicLinkState.Off;
	}

	public void InputSetAllowed(InputData inputdata) {
		AllowUse = inputdata.Value.StringID();
		for (int i = 0; i < ControlledLinks.Count; i++) {
			if (ControlledLinks[i].Get() == null) {
				ControlledLinks.RemoveAt(i);
				if (i >= ControlledLinks.Count)
					break;
			}
			ControlledLinks[i].Get()!.AllowUse = AllowUse;
		}
	}

	public void InputSetInvert(InputData inputdata) {
		InvertAllow = inputdata.Value.Bool();
		for (int i = 0; i < ControlledLinks.Count; i++) {
			if (ControlledLinks[i].Get() == null) {
				ControlledLinks.RemoveAt(i);
				if (i >= ControlledLinks.Count)
					break;
			}
			ControlledLinks[i].Get()!.InvertAllow = InvertAllow;
		}
	}

	public readonly List<Handle<AI_DynamicLink>> ControlledLinks = [];
	public DynamicLinkState LinkState;
	public string? AllowUse;
	public bool InvertAllow;
	public bool UseAirLinkRadius;
}

[LinkEntityToClass("info_node_link")]
public class AI_DynamicLink : ServerOnlyEntity
{
	public static readonly new DataMap DataDesc = new(typeof(AI_DynamicLink), BaseEntity.DataDesc, [
		DEFINE<AI_DynamicLink>.KEYFIELD(nameof(LinkState), FieldType.Integer, "initialstate"),
		DEFINE<AI_DynamicLink>.KEYFIELD(nameof(SrcEditID), FieldType.Integer, "startnode"),
		DEFINE<AI_DynamicLink>.KEYFIELD(nameof(DestEditID), FieldType.Integer, "endnode"),
		DEFINE<AI_DynamicLink>.KEYFIELD(nameof(LinkType), FieldType.Integer, "linktype"),
		DEFINE<AI_DynamicLink>.FIELD(nameof(InvertAllow), FieldType.Boolean),
		DEFINE<AI_DynamicLink>.KEYFIELD(nameof(AllowUse), FieldType.String, "AllowUse"),

		DEFINE<AI_DynamicLink>.INPUTFUNC(FieldType.Void, "TurnOn", nameof(InputTurnOn), (INPUTFUNCPTR)((self, data) => ((AI_DynamicLink)self).InputTurnOn(data))),
		DEFINE<AI_DynamicLink>.INPUTFUNC(FieldType.Void, "TurnOff", nameof(InputTurnOff), (INPUTFUNCPTR)((self, data) => ((AI_DynamicLink)self).InputTurnOff(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public static AI_DynamicLink? AllDynamicLinks;
	public static bool gm_bInitialized;

	public AI_DynamicLink() {
		NextDynamicLink = AllDynamicLinks;
		AllDynamicLinks = this;
	}

	public override void UpdateOnRemove() {
		AI_DynamicLink? dynamicLink = AllDynamicLinks;
		if (dynamicLink == this)
			AllDynamicLinks = dynamicLink.NextDynamicLink;
		else {
			while (dynamicLink != null) {
				if (dynamicLink.NextDynamicLink == this) {
					dynamicLink.NextDynamicLink = dynamicLink.NextDynamicLink.NextDynamicLink;
					break;
				}
				dynamicLink = dynamicLink.NextDynamicLink;
			}
		}

		base.UpdateOnRemove();
	}

	public static void GenerateControllerLinks() {
		AI_DynamicLinkController? controller = null;
		while ((controller = gEntList.NextEntByClass(controller)) != null)
			controller.GenerateLinksFromVolume();
	}

	public static void InitDynamicLinks() {
		if (g_pAINetworkManager!.GetEditOps().NodeIndexTable == null) {
			Warning("ERROR: Trying initialize links with no WC ID table!\n");
			return;
		}

		if (gm_bInitialized)
			return;

		gm_bInitialized = true;

		bool updateZones = false;

		GenerateControllerLinks();

		AI_DynamicLink? dynamicLink = AllDynamicLinks;

		while (dynamicLink != null) {
			if (!dynamicLink.FixedUpIds) {
				int srcID = g_pAINetworkManager.GetEditOps().GetNodeIdFromWCId(dynamicLink.SrcEditID);
				if (srcID == -1) {
					DevMsg($"ERROR: Dynamic link source WC node {dynamicLink.SrcEditID} not found\n");
					srcID = NO_NODE;
				}

				int destID = g_pAINetworkManager.GetEditOps().GetNodeIdFromWCId(dynamicLink.DestEditID);
				if (destID == -1) {
					DevMsg($"ERROR: Dynamic link dest WC node {dynamicLink.DestEditID} not found\n");
					destID = NO_NODE;
				}

				dynamicLink.SrcID = srcID;
				dynamicLink.DestID = destID;
				dynamicLink.FixedUpIds = true;
			}

			if (dynamicLink.SrcID != NO_NODE && dynamicLink.DestID != NO_NODE) {
				AI_HullBits hullBits = (AI_HullBits)dynamicLink.SpawnFlags & AI_HullBits.HullBitsMask;
				if (hullBits != 0) {
					short link = dynamicLink.FindLink();
					if (link == AI_Network.NO_LINK) {
						AI_Node? node1 = g_pBigAINet!.GetNode(dynamicLink.SrcID);
						AI_Node? node2 = g_pBigAINet.GetNode(dynamicLink.DestID);

						if (node1 != null && node2 != null) {
							link = g_pBigAINet.CreateLink(dynamicLink.SrcID, dynamicLink.DestID);
							if (link == AI_Network.NO_LINK)
								DevMsg($"Failed to create dynamic link ({dynamicLink.SrcEditID} <--> {dynamicLink.DestEditID})\n");
						}
					}

					if (link != AI_Network.NO_LINK) {
						updateZones = true;

						ref AI_Link networkLink = ref g_pBigAINet!.GetLink(link);
						for (int i = 0; i < (int)AI_HullType.NumHulls; i++) {
							if (((int)hullBits & (1 << i)) != 0)
								networkLink.AcceptedMoveTypes[i] = dynamicLink.LinkType;
						}
					}
				}

				dynamicLink.SetLinkState();

				dynamicLink = dynamicLink.NextDynamicLink;
			}
			else {
				AI_DynamicLink badDynamicLink = dynamicLink;

				dynamicLink = dynamicLink.NextDynamicLink;

				Util.RemoveImmediate(badDynamicLink);
			}
		}

		if (updateZones)
			g_AINetworkBuilder.InitZones(g_pBigAINet!);
	}

	public static void ResetDynamicLinks() {
		AI_DynamicLink? dynamicLink = AllDynamicLinks;

		while (dynamicLink != null) {
			dynamicLink.SetLinkState();
			dynamicLink = dynamicLink.NextDynamicLink;
		}
	}

	public bool IsLinkValid() {
		AI_Node node = g_pBigAINet!.GetNode(SrcID)!;
		return !Unsafe.IsNullRef(ref node.GetLink(DestID));
	}

	public void InputTurnOn(InputData inputdata) {
		if (LinkState == DynamicLinkState.Off) {
			LinkState = DynamicLinkState.On;
			SetLinkState();
		}
	}

	public void InputTurnOff(InputData inputdata) {
		if (LinkState == DynamicLinkState.On) {
			LinkState = DynamicLinkState.Off;
			SetLinkState();
		}
	}

	public short FindLink() {
		AI_Node? srcNode = g_pBigAINet!.GetNode(SrcID, false);
		if (srcNode != null) {
			int linkCount = srcNode.NumLinks();
			for (int i = 0; i < linkCount; i++) {
				ref AI_Link link = ref srcNode.GetLinkByIndex(i);

				if ((link.SrcID == SrcID && link.DestID == DestID) ||
					(link.SrcID == DestID && link.DestID == SrcID)) {
					return srcNode.Links[i];
				}
			}
		}
		return AI_Network.NO_LINK;
	}

	public override EntityCapabilities ObjectCaps() {
		EntityCapabilities caps = base.ObjectCaps();

		if (NotSaved)
			caps |= EntityCapabilities.DontSave;

		return caps;
	}

	public void SetLinkState() {
		if (!gm_bInitialized)
			return;

		if (SrcID == NO_NODE || DestID == NO_NODE) {
			Vector3 pos = GetAbsOrigin();
			DevWarning($"ERROR: Dynamic link at {pos.X} {pos.Y} {pos.Z} pointing to invalid node ID!!\n");
			return;
		}

		AI_Node? srcNode = g_pBigAINet!.GetNode(SrcID, false);
		if (srcNode != null) {
			short link = FindLink();
			if (link != AI_Network.NO_LINK) {
				ref AI_Link networkLink = ref g_pBigAINet.GetLink(link);
				networkLink.DynamicLink = this;
				if (LinkState == DynamicLinkState.Off)
					networkLink.LinkInfo |= LinkInfo.Off;
				else
					networkLink.LinkInfo &= ~LinkInfo.Off;
			}
			else
				DevMsg($"Dynamic Link Error: ({GetDebugName()}) unable to form between nodes {SrcID} and {DestID}\n");
		}
	}

	public static AI_DynamicLink? GetDynamicLink(int srcID, int dstID) {
		AI_DynamicLink? dynamicLink = AllDynamicLinks;

		while (dynamicLink != null) {
			if ((srcID == dynamicLink.SrcID && dstID == dynamicLink.DestID) ||
				(srcID == dynamicLink.DestID && dstID == dynamicLink.SrcID)) {
				return dynamicLink;
			}

			dynamicLink = dynamicLink.NextDynamicLink;
		}
		return null;
	}

	public AI_DynamicLink? NextDynamicLink;

	public int SrcEditID;
	public int DestEditID;

	public int SrcID = NO_NODE;
	public int DestID = NO_NODE;
	public DynamicLinkState LinkState = DynamicLinkState.Off;
	public string? AllowUse;
	public bool InvertAllow;

	public bool FixedUpIds;
	public bool NotSaved;
	public Capability LinkType = Capability.MoveGround;
}
