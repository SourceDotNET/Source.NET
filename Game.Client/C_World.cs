using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;

using FIELD = Source.FIELD<Game.Client.C_World>;

namespace Game.Client;

[NetworkName("CWorld")]
public class C_World : C_BaseEntity
{
	[ImplementClientClassFactory]
	public static C_World ClientWorldFactory(int entnum, int serialnum) {
		Assert(g_ClientWorld != null);
		g_ClientWorld.Init(entnum, serialnum);
		return g_ClientWorld;
	}

	public C_World() : base() {

	}

	public static readonly RecvTable DT_WORLD = new(DT_BaseEntity, [
		RecvPropVector(FIELD.OF(nameof(WorldMins))),
		RecvPropVector(FIELD.OF(nameof(WorldMaxs))),
		RecvPropInt(FIELD.OF(nameof(StartDark))),
		RecvPropFloat(FIELD.OF(nameof(MaxOccludeeArea))),
		RecvPropFloat(FIELD.OF(nameof(MinOccluderArea))),
		RecvPropFloat(FIELD.OF(nameof(MaxPropScreenSpaceWidth))),
		RecvPropFloat(FIELD.OF(nameof(MinPropScreenSpaceWidth))),
		RecvPropString(FIELD.OF(nameof(DetailSpriteMaterial))),
	]);

	public static new readonly ClientClass ClientClass = new ClientClass(DT_WORLD);

	public override bool Init(int entNum, int serialNum) {
		WaveHeight = 0.0f;
		ActivityList.Init();
		EventList.Init();

		return base.Init(entNum, serialNum);
	}

	public override void Release() {
		ActivityList.Free();
		Term();
	}

	float WaveHeight;
	[NetworkName("m_WorldMins")]
	Vector3 WorldMins;
	[NetworkName("m_WorldMaxs")]
	Vector3 WorldMaxs;
	[NetworkName("m_bStartDark")]
	bool StartDark;
	[NetworkName("m_flMaxOccludeeArea")]
	float MaxOccludeeArea;
	[NetworkName("m_flMinOccluderArea")]
	float MinOccluderArea;
	[NetworkName("m_flMaxPropScreenSpaceWidth")]
	float MaxPropScreenSpaceWidth;
	[NetworkName("m_flMinPropScreenSpaceWidth")]
	float MinPropScreenSpaceWidth;
	[NetworkName("m_iszDetailSpriteMaterial")]
	InlineArray256<char> DetailSpriteMaterial;
	bool ColdWorld;

	void W_Precache() {
		WeaponParse.PrecacheFileWeaponInfoDatabase(filesystem);
	}

	public void RegisterSharedActivities() {
		ActivityList.RegisterSharedActivities();
		EventList.RegisterSharedEvents();
	}

	public override void Precache() {
		ActivityList.Free();
		EventList.Free();

		RegisterSharedActivities();

		// Get weapon precaches
		W_Precache();


		// Call all registered precachers.
		PrecacheRegister.Precache();
	}

	public override void Spawn() {
		Precache();
	}

	public ReadOnlySpan<char> GetDetailSpriteMaterial() => ((ReadOnlySpan<char>)DetailSpriteMaterial).SliceNullTerminatedString();

	static C_World? g_ClientWorld;
	public static C_World GetClientWorldEntity() {
		Assert(g_ClientWorld != null);
		return g_ClientWorld;
	}

	internal static void ClientWorldFactoryInit() {
		g_ClientWorld = new();
	}

	internal static void ClientWorldFactoryShutdown() {
		g_ClientWorld = null;
	}
}
