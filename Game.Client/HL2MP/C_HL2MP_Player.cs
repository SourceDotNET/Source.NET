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

using FIELD = FIELD<C_HL2MP_Player>;

[NetworkName("CHL2MP_Player")]
public partial class C_HL2MP_Player : C_BaseHLPlayer
{
	static ConVar cl_playermodel = new("none", FCvar.UserInfo | FCvar.Archive | FCvar.ServerCanExecute, "Default Player Model");
	static ConVar cl_defaultweapon = new("weapon_physcannon", FCvar.UserInfo | FCvar.Archive, "Default Spawn Weapon");

	[NetworkTable("DT_HL2MPLocalPlayerExclusive")]
	public static readonly new NetworkProp[] LocalPlayerExclusiveProps = [
		nameof(NetworkOrigin),
		new(nameof(AngEyeAngles)) { Encoding = NetEncoding.VectorElements, Count = 2 },
	];

	[NetworkTable("DT_HL2MPNonLocalPlayerExclusive")]
	public static readonly NetworkProp[] NonLocalPlayerExclusiveProps = [
		nameof(NetworkOrigin),
		new(nameof(AngEyeAngles)) { Encoding = NetEncoding.VectorElements, Count = 2 },
	];

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(LocalPlayerExclusiveProps)) { Name = "hl2mplocaldata" },
		new(nameof(NonLocalPlayerExclusiveProps)) { Name = "hl2mpnonlocaldata" },
		nameof(Ragdoll),
		nameof(SpawnInterpCounter),
		nameof(IsWalking),
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		new(nameof(Cycle)) { Flags = FieldTypeDescFlags.Override | FieldTypeDescFlags.Private | FieldTypeDescFlags.NoErrorCheck },
		new(nameof(Sequence)) { Flags = FieldTypeDescFlags.Override | FieldTypeDescFlags.Private | FieldTypeDescFlags.NoErrorCheck },
		new(nameof(PlaybackRate)) { Flags = FieldTypeDescFlags.Override | FieldTypeDescFlags.Private | FieldTypeDescFlags.NoErrorCheck },
		new(nameof(EncodedController)) { Flags = FieldTypeDescFlags.Override | FieldTypeDescFlags.Private, Tolerance = 0.02f },
		new(nameof(NewSequenceParity)) { Flags = FieldTypeDescFlags.Override | FieldTypeDescFlags.Private | FieldTypeDescFlags.NoErrorCheck },
		nameof(IsWalking),
	];

	public readonly HL2MPPlayerAnimState PlayerAnimState;
	[NetworkVar("m_angEyeAngles")]
	public QAngle AngEyeAngles;
	readonly InterpolatedVar<QAngle> IV_AngEyeAngles = new(nameof(AngEyeAngles));
	[NetworkVar("m_hRagdoll")]
	public new EHANDLE Ragdoll = new();
	[NetworkVar("m_iSpawnInterpCounter")]
	public int SpawnInterpCounter;
	public int SpawnInterpCounterCache;
	public int PlayerSoundType;
	[NetworkVar("m_fIsWalking")]
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

#if GMOD_DLL
	public override void UpdateClientSideAnimation() {
		QAngle eyeAngles = EyeAngles();
		PlayerAnimState.Update(eyeAngles[YAW], eyeAngles[PITCH]);
		UpdateBaseClientSideAnimation();
	}
#else
	public override void ClientThink() {
		base.ClientThink();
		PlayerAnimState.Update(AngEyeAngles[YAW], AngEyeAngles[PITCH]);
	}
#endif

	public override ShadowType ShadowCastType() {
		if (!IsVisible())
			return ShadowType.None;

		return ShadowType.RenderToTextureDynamic;
	}

	public override ref readonly QAngle GetRenderAngles() => ref PlayerAnimState.GetRenderAngles();
}

[NetworkName("CHL2MPRagdoll", NoBase = true)]
public partial class C_HL2MPRagdoll : C_BaseAnimatingOverlay
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(RagdollOrigin),
		nameof(Player),
		nameof(ModelIndex),
		nameof(ForceBone),
		nameof(Force),
		nameof(RagdollVelocity),
	];

	[NetworkVar("m_vecRagdollOrigin")]
	public Vector3 RagdollOrigin;
	[NetworkVar("m_hPlayer")]
	public EHANDLE Player = new();
	[NetworkVar("m_vecRagdollVelocity")]
	public Vector3 RagdollVelocity;

	public override bool IsRagdollProp() => true;
}
