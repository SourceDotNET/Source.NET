global using static Game.Server.Hierarchy;

using Game.Shared;

using Source.Common.Mathematics;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Game.Server;

public static class Hierarchy
{
	public static void UnlinkChild(BaseEntity parent, BaseEntity child) {
		BaseEntity? list;
		ref EHANDLE prev = ref Unsafe.NullRef<EHANDLE>();

		list = parent.MoveChild.Get<BaseEntity>();
		prev = ref parent.MoveChild;
		while (list != null) {
			BaseEntity? next = list.MovePeer.Get<BaseEntity>();
			if (list == child) {
				// patch up the list
				prev.Set(next);

				// Clear hierarchy bits for this guy
				list.MoveParent = EHANDLE.FromEntity(null);
				list.MovePeer.Set(null);
				list.NetworkProp().SetNetworkParent(new());
				list.DispatchUpdateTransmitState();
				list.OnEntityEvent<object>(EntityEvent.ParentChanged, null);

				parent.RecalcHasPlayerChildBit();
				return;
			}
			else {
				prev = ref list.MovePeer;
				list = next;
			}
		}

		// This only happens if the child wasn't found in the parent's child list
		Assert(false);
	}
	public static void LinkChild(BaseEntity parent, BaseEntity child) {
		EHANDLE hParent = new();
		hParent.Set(parent);
		child.MovePeer.Set(parent.FirstMoveChild());
		parent.MoveChild.Set(child);
		child.MoveParent = hParent;
		child.NetworkProp().SetNetworkParent(hParent);
		child.DispatchUpdateTransmitState();
		child.OnEntityEvent<object>(EntityEvent.ParentChanged, null);
		parent.RecalcHasPlayerChildBit();
	}
	public static void TransferChildren(BaseEntity? oldParent, BaseEntity newParent) {
		BaseEntity? child = oldParent.FirstMoveChild();
		while (child != null) {
			// NOTE: Have to do this before the unlink to ensure local coords are valid
			Vector3 vecAbsOrigin = child.GetAbsOrigin();
			QAngle angAbsRotation = child.GetAbsAngles();
			Vector3 vecAbsVelocity = child.GetAbsVelocity();
			//		QAngle vecAbsAngVelocity = child->GetAbsAngularVelocity();

			UnlinkChild(oldParent, child);
			LinkChild(newParent, child);

			// FIXME: This is a hack to guarantee update of the local origin, angles, etc.
			child.AbsOrigin.Init(float.MaxValue, float.MaxValue, float.MaxValue);
			child.AbsRotation.Init(float.MaxValue, float.MaxValue, float.MaxValue);
			child.AbsVelocity.Init(float.MaxValue, float.MaxValue, float.MaxValue);

			child.SetAbsOrigin(vecAbsOrigin);
			child.SetAbsAngles(angAbsRotation);
			child.SetAbsVelocity(vecAbsVelocity);
			//		child->SetAbsAngularVelocity(vecAbsAngVelocity);

			child = oldParent.FirstMoveChild();
		}
	}
	public static void UnlinkFromParent(BaseEntity? remove) {
		if (remove.GetMoveParent() != null) {
			// NOTE: Have to do this before the unlink to ensure local coords are valid
			Vector3 vecAbsOrigin = remove.GetAbsOrigin();
			QAngle angAbsRotation = remove.GetAbsAngles();
			Vector3 vecAbsVelocity = remove.GetAbsVelocity();
			//		QAngle vecAbsAngVelocity = pRemove->GetAbsAngularVelocity();

			UnlinkChild(remove.GetMoveParent(), remove);

			remove.SetLocalOrigin(vecAbsOrigin);
			remove.SetLocalAngles(angAbsRotation);
			remove.SetLocalVelocity(vecAbsVelocity);
			remove.UpdateWaterState();
		}
	}
	public static void UnlinkAllChildren(BaseEntity parent) {
		BaseEntity? child = parent.FirstMoveChild();
		while (child != null) {
			BaseEntity? next = child.NextMovePeer();
			UnlinkFromParent(child);
			child = next;
		}
	}
	public static bool EntityIsParentOf(BaseEntity? parent, BaseEntity? entity) {
		while (entity.GetMoveParent() != null) {
			entity = entity.GetMoveParent();
			if (parent == entity)
				return true;
		}

		return false;
	}
	public static void GetAllChildren_r(BaseEntity? entity, List<BaseEntity> list) {
		for (; entity != null; entity = entity.NextMovePeer()) {
			list.Add(entity);
			GetAllChildren_r(entity.FirstMoveChild(), list);
		}
	}
	public static int GetAllChildren(BaseEntity? parent, List<BaseEntity> list) {
		if (parent == null)
			return 0;

		GetAllChildren_r(parent.FirstMoveChild(), list);
		return list.Count;
	}
	public static int GetAllInHierarchy(BaseEntity? parent, List<BaseEntity> list) {
		if (parent == null)
			return 0;
		list.Add(parent);
		return GetAllChildren(parent, list) + 1;
	}
}
