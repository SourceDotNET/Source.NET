using Game.Client.HL2;
using Game.Shared;
using Game.Shared.GarrysMod;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

namespace Game.Client.HL2MP;

using DEFINE = Source.DEFINE<Game.Client.HL2MP.C_HL2MP_Player>;
using FIELD = FIELD<C_HL2MP_Player>;
using FIELD_RD = FIELD<C_HL2MPRagdoll>;

[NetworkName("CHL2MP_Player")]
public partial class C_HL2MP_Player : C_BaseHLPlayer
{
	static ConVar cl_playermodel = new("none", FCvar.UserInfo | FCvar.Archive | FCvar.ServerCanExecute, "Default Player Model");
	static ConVar cl_defaultweapon = new("weapon_physcannon", FCvar.UserInfo | FCvar.Archive, "Default Spawn Weapon");

	public static readonly new DataMap PredMap = new(typeof(C_HL2MP_Player), C_BaseHLPlayer.PredMap, [
		DEFINE.PRED_FIELD( nameof(IsWalking), FieldType.Boolean, FieldTypeDescFlags.InSendTable ),
	]); public override DataMap? GetPredDescMap() => PredMap;

	public static readonly RecvTable DT_HL2MPLocalPlayerExclusive = new(nameof(DT_HL2MPLocalPlayerExclusive), [
		RecvPropVector(FIELD.OF_NAMED(nameof(NetworkOrigin), "m_vecOrigin")),

		RecvPropFloat(FIELD.OF_VECTORELEM(nameof(AngEyeAngles), 0)),
		RecvPropFloat(FIELD.OF_VECTORELEM(nameof(AngEyeAngles), 1)),
	]);

	public static readonly RecvTable DT_HL2MPNonLocalPlayerExclusive = new(nameof(DT_HL2MPNonLocalPlayerExclusive), [
		RecvPropVector(FIELD.OF_NAMED(nameof(NetworkOrigin), "m_vecOrigin")),

		RecvPropFloat(FIELD.OF_VECTORELEM(nameof(AngEyeAngles), 0)),
		RecvPropFloat(FIELD.OF_VECTORELEM(nameof(AngEyeAngles), 1)),
	]);


	public static readonly RecvTable DT_HL2MP_Player = new(DT_BasePlayer, [
		RecvPropDataTable("hl2mplocaldata", DT_HL2MPLocalPlayerExclusive),
		RecvPropDataTable("hl2mpnonlocaldata", DT_HL2MPNonLocalPlayerExclusive),
		RecvPropEHandle(FIELD.OF(nameof(Ragdoll))),
		RecvPropInt(FIELD.OF(nameof(SpawnInterpCounter))),
		RecvPropBool(FIELD.OF(nameof(IsWalking)))
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(null, null, DT_HL2MP_Player);

	public readonly HL2MPPlayerAnimState PlayerAnimState;
	[NetworkName("m_angEyeAngles")]
	public QAngle AngEyeAngles;
	readonly InterpolatedVar<QAngle> IV_AngEyeAngles = new(nameof(AngEyeAngles));
	[NetworkName("m_hRagdoll")]
	public new EHANDLE Ragdoll = new();
	[NetworkName("m_iSpawnInterpCounter")]
	public int SpawnInterpCounter;
	public int SpawnInterpCounterCache;
	public int PlayerSoundType;
	[NetworkName("m_fIsWalking")]
	public bool IsWalking;

	public C_HL2MP_Player() : base() {
		PlayerAnimState = HL2MPPlayerAnimState.CreateHL2MPPlayerAnimState(this);
		AddVar(this, FIELD.OF(nameof(AngEyeAngles)), IV_AngEyeAngles, LatchFlags.LatchSimulationVar);
	}

	public override void PostDataUpdate(DataUpdateType updateType) {
		if (SpawnInterpCounter != SpawnInterpCounterCache) {
			MoveToLastReceivedPosition(true);
			ResetLatched();
			SpawnInterpCounterCache = SpawnInterpCounter;
		}
		base.PostDataUpdate(updateType);
	}

	public override ref readonly QAngle EyeAngles() {
		if (IsLocalPlayer())
			return ref base.EyeAngles();
		else
			return ref AngEyeAngles;
	}

	public override void ItemPostFrame() {
		if ((GetFlags() & EntityFlags.Frozen) != 0)
			return;
		base.ItemPostFrame();
	}

	public override void CalcView(ref Vector3 eyeOrigin, ref QAngle eyeAngles, ref float zNear, ref float zFar, ref float fov) {
		if ((LifeState)LifeState != Source.LifeState.Alive) {

		}
		base.CalcView(ref eyeOrigin, ref eyeAngles, ref zNear, ref zFar, ref fov);
	}

	public override void PostThink() {
		base.PostThink();
		AngEyeAngles = EyeAngles();
	}

	public override void ClientThink() {
		base.ClientThink();
		PlayerAnimState.Update(AngEyeAngles[YAW], AngEyeAngles[PITCH]);
	}

	public override ShadowType ShadowCastType() {
		if (!IsVisible())
			return ShadowType.None;

		return ShadowType.RenderToTextureDynamic;
	}

	public override ref readonly QAngle GetRenderAngles() => ref PlayerAnimState.GetRenderAngles();
}

[NetworkName("CHL2MPRagdoll")]
public class C_HL2MPRagdoll : C_BaseAnimatingOverlay
{
	public static readonly RecvTable DT_HL2MPRagdoll = new([
		RecvPropVector(FIELD_RD.OF(nameof(RagdollOrigin))),
		RecvPropEHandle(FIELD_RD.OF(nameof(Player))),
		RecvPropInt(FIELD_RD.OF(nameof(ModelIndex))),
		RecvPropInt(FIELD_RD.OF(nameof(ForceBone))),
		RecvPropVector(FIELD_RD.OF(nameof(Force))),
		RecvPropVector(FIELD_RD.OF(nameof(RagdollVelocity)))
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(null, null, DT_HL2MPRagdoll);

	[NetworkName("m_vecRagdollOrigin")]
	public Vector3 RagdollOrigin;
	[NetworkName("m_hPlayer")]
	public EHANDLE Player = new();
	[NetworkName("m_vecRagdollVelocity")]
	public Vector3 RagdollVelocity;

	public override bool IsRagdollProp() => true;
}
