using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;

namespace Game.Client;

[NetworkName("CWorld", Table = "DT_WORLD")]
public partial class C_World : C_BaseEntity
{
	[ImplementClientClassFactory]
	public static C_World ClientWorldFactory(int entnum, int serialnum) {
		Assert(g_ClientWorld != null);
		g_ClientWorld.Init(entnum, serialnum);
		return g_ClientWorld;
	}

	public C_World() : base() {

	}

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(WorldMins),
		nameof(WorldMaxs),
		nameof(StartDark),
		nameof(MaxOccludeeArea),
		nameof(MinOccluderArea),
		nameof(MaxPropScreenSpaceWidth),
		nameof(MinPropScreenSpaceWidth),
		nameof(DetailSpriteMaterial),
	];

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
	[NetworkVar("m_WorldMins")]
	Vector3 WorldMins;
	[NetworkVar("m_WorldMaxs")]
	Vector3 WorldMaxs;
	[NetworkVar("m_bStartDark")]
	bool StartDark;
	[NetworkVar("m_flMaxOccludeeArea")]
	float MaxOccludeeArea;
	[NetworkVar("m_flMinOccluderArea")]
	float MinOccluderArea;
	[NetworkVar("m_flMaxPropScreenSpaceWidth")]
	float MaxPropScreenSpaceWidth;
	[NetworkVar("m_flMinPropScreenSpaceWidth")]
	float MinPropScreenSpaceWidth;
	[NetworkVar("m_iszDetailSpriteMaterial")]
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
