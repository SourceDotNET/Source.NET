
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Engine;

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Game.Client;

[NetworkName("CBaseTempEntity", NoBase = true)]
public partial class C_BaseTempEntity : IClientUnknown, IClientNetworkable
{
	public static C_BaseTempEntity? GetDynamicList() => s_DynamicEntities;
	public static C_BaseTempEntity? GetList() => s_TempEntities;
	public static void PrecacheTempEnts() {
		C_BaseTempEntity? te = GetList();
		while(te != null) {
			te.Precache();
			te = te.GetNext();
		}
	}
	public static void ClearDynamicTempEnts() {
		s_DynamicEntities = null;
	}
	public static void CheckDynamicTempEnts() {
		// todo
	}

	public C_BaseTempEntity() {
		Next = s_TempEntities;
		s_TempEntities = this;
		NextDynamic = null;
	}

	public C_BaseTempEntity? GetNext() => Next; 
	public C_BaseTempEntity? GetNextDynamic() => NextDynamic; 

	private C_BaseTempEntity? Next;
	private C_BaseTempEntity? NextDynamic;
	private static C_BaseTempEntity? s_TempEntities;
	private static C_BaseTempEntity? s_DynamicEntities;

	public int EntIndex() => 0;

	public IClientNetworkable GetClientNetworkable() => this;
	public IClientRenderable GetClientRenderable() => null!;
	public IClientThinkable GetClientThinkable() => null!;
	public ICollideable GetCollideable() => null!;
	public object GetDataTableBasePtr() => this;
	public IClientEntity GetIClientEntity() => null!;
	public IClientUnknown GetIClientUnknown() => this;

	public ref readonly BaseHandle GetRefEHandle() {
		Debugger.Break();
		Environment.Exit(0);
		return ref Unsafe.NullRef<BaseHandle>();
	}

	public bool IsDormant() {
		Assert(false);
		return false;
	}

	public void Release() {
		throw new NotImplementedException();
	}

	public void SetDestroyedOnRecreateEntities() {
		throw new NotImplementedException();
	}

	public void SetRefEHandle(in BaseHandle handle) {
		throw new NotImplementedException();
	}

	public virtual bool Init(int entnum, int serialnum) {
		if(entnum != -1) {
			Assert(false);
		}

		NextDynamic = s_DynamicEntities;
		s_DynamicEntities = this;
		return true;
	}

	public virtual void Precache() { }
	public virtual void NotifyShouldTransmit(ShouldTransmiteState state) { }
	public virtual void PreDataUpdate(DataUpdateType updateType) { }
	public virtual void PostDataUpdate(DataUpdateType updateType) { }
	public virtual void OnDataUnchangedInPVS() { }
	public virtual void OnPreDataChanged(DataUpdateType updateType) { }
	public virtual void OnDataChanged(DataUpdateType updateType) { }
	public virtual void ReceiveMessage(int classID, bf_read msg) { }
}
