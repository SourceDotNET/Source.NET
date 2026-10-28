using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Game.Server;

public class ServerNetworkProperty : IServerNetworkable, IEventRegisterCallback
{
	public int AreaNum() {
		RecomputePVSInformation();
		return PVSInfo.AreaNum;
	}

	public void MarkForDeletion() => Outer!.AddEFlags(EFL.KillMe);
	public bool IsMarkedForDeletion() => (Outer!.GetEFlags() & EFL.KillMe) != 0;

	public void FireEvent() {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetClassName() => Outer.GetClassname();

	public int EntIndex() => ENTINDEX(Pev);

	public virtual Edict? GetEdict() {
		return Pev;
	}
	public Edict Edict() => Pev;

	public IHandleEntity? GetEntityHandle() {
		return Outer;
	}

	public ServerClass GetServerClass() {
		ServerClass ??= Outer!.GetServerClass();

		return ServerClass;
	}

	public void Release() {
		Outer!.Term();
	}

	public void Term() {
		engine.CleanUpEntityClusterList(ref PVSInfo);
		DetachEdict();
	}

	public void DetachEdict() {
		if (Pev != null) {
			Pev.SetEdict(null, false);
			engine.RemoveEdict(Pev);
			Pev = null!;
		}
	}

	object? IServerNetworkable.GetBaseEntity() => Outer;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public BaseEntity? GetBaseEntity() => Outer;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public BaseEntity? GetOuter() => Outer;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ref PVSInfo GetPVSInfo() => ref PVSInfo;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetNetworkParent(EHANDLE parent) => Parent.Index = parent.Index;

	public void AttachEdict(Edict? requiredEdict) {
		if (requiredEdict == null)
			requiredEdict = engine.CreateEdict();

		Pev = requiredEdict;
		Pev.SetEdict(GetBaseEntity(), true);
	}


	private BaseEntity? Outer;
	private Edict Pev;
	private PVSInfo PVSInfo;
	private ServerClass? ServerClass;
	private EHANDLE Parent = new();
	// event register later
	bool PendingStateChange;

	public static readonly DataMap DataDesc = new(typeof(ServerNetworkProperty), [
		//	DEFINE_FIELD( m_pOuter, FIELD_CLASSPTR ),
		//	DEFINE_FIELD( m_pPev, FIELD_CLASSPTR ),
		//	DEFINE_FIELD( m_PVSInfo, PVSInfo_t ),
		//	DEFINE_FIELD( m_pServerClass, FIELD_CLASSPTR ),
		DEFINE<ServerNetworkProperty>.GLOBAL_FIELD(nameof(Parent), FieldType.EHandle),
		//	DEFINE_FIELD( m_TimerEvent, CEventRegister ),
		//	DEFINE_FIELD( m_bPendingStateChange, FIELD_BOOLEAN ),
	]);

	public void Init(BaseEntity entity) {
		Pev = null;
		Outer = entity;
		ServerClass = null;
		PendingStateChange = false;
		PVSInfo.ClusterCount = 0;
		// timerevent todo
	}

	internal void NetworkStateForceUpdate() {
		Pev?.StateChanged();
	}

	internal void NetworkStateChanged() {
		Pev?.StateChanged();
	}

	internal void NetworkStateChanged(IFieldAccessor field) {
		Pev?.StateChanged(field);
	}

	public void MarkPVSInformationDirty() {
		if (Pev != null)
			Pev.StateFlags |= EdictFlags.DirtyPVSInformation;
	}

	public bool IsInPVS(Edict recipient, ReadOnlySpan<byte> pvs) {
		RecomputePVSInformation();

		Assert(!pvs.IsEmpty && (Pev != recipient));

		if (PVSInfo.ClusterCount < 0)
			return engine.CheckHeadnodeVisible(PVSInfo.HeadNode, pvs) != 0;

		ReadOnlySpan<ushort> clusters = PVSInfo.GetClusters();
		for (int i = clusters.Length; --i >= 0;) {
			if ((pvs[clusters[i] >> 3] & (1 << (clusters[i] & 7))) != 0)
				return true;
		}

		return false;
	}

	internal bool IsInPVS(CheckTransmitInfo info) {
		Assert(Pev == null || ((Pev.StateFlags & EdictFlags.DirtyPVSInformation) == 0));

		int i;

		if (PVSInfo.AreaNum2 == 0) {
			for (i = 0; i < info.AreasNetworked; i++) {
				int clientArea = info.Areas[i];
				if (clientArea == PVSInfo.AreaNum || engine.CheckAreasConnected(clientArea, PVSInfo.AreaNum) != 0)
					break;
			}
		}
		else {
			for (i = 0; i < info.AreasNetworked; i++) {
				int clientArea = info.Areas[i];
				if (clientArea == PVSInfo.AreaNum || clientArea == PVSInfo.AreaNum2)
					break;

				if (engine.CheckAreasConnected(clientArea, PVSInfo.AreaNum) != 0)
					break;

				if (engine.CheckAreasConnected(clientArea, PVSInfo.AreaNum2) != 0)
					break;
			}
		}

		if (i == info.AreasNetworked)
			return false;

		Assert(Pev != info.ClientEnt);

		byte[] pvs = info.PVS;

		if (PVSInfo.ClusterCount < 0)
			return engine.CheckHeadnodeVisible(PVSInfo.HeadNode, pvs.AsSpan(0, info.PVSSize)) != 0;

		ReadOnlySpan<ushort> clusters = PVSInfo.GetClusters();
		for (i = clusters.Length; --i >= 0;) {
			int cluster = clusters[i];
			if ((pvs[cluster >> 3] & (1 << (cluster & 7))) != 0)
				return true;
		}

		return false;
	}

	internal ServerNetworkProperty? GetNetworkParent() {
		BaseEntity? parent = Parent.Get();
		return parent?.NetworkProp();
	}

	internal void RecomputePVSInformation() {
		if (Pev != null && ((Pev.StateFlags & EdictFlags.DirtyPVSInformation) != 0)) {
			Pev.StateFlags &= ~EdictFlags.DirtyPVSInformation;
			engine.BuildEntityClusterList(Pev, ref PVSInfo);
		}
	}
}
