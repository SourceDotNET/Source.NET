using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Mathematics;

namespace Game.Client;

using FIELD = FIELD<PlayerLocalData>;


public partial class PlayerLocalData
{
	[NetworkTable("DT_Local")]
	public static readonly NetworkProp[] Props = [
		nameof(AreaBits),
		nameof(AreaPortalBits),
		nameof(HideHUD),
		nameof(FOVRate),
		new(nameof(Ducked)) { Encoding = NetEncoding.Int },
		new(nameof(Ducking)) { Encoding = NetEncoding.Int },
		new(nameof(InDuckJump)) { Encoding = NetEncoding.Int },
		nameof(DuckTime),
		nameof(DuckJumpTime),
		nameof(JumpTime),
		nameof(FallVelocity),
		new(nameof(PunchAngle)) { Encoding = NetEncoding.Vector },
		new(nameof(PunchAngleVel)) { Encoding = NetEncoding.Vector },
		new(nameof(DrawViewmodel)) { Encoding = NetEncoding.Int },
		new(nameof(WearingSuit)) { Encoding = NetEncoding.Int },
		nameof(Poisoned),
		nameof(StepSize),
		new(nameof(AllowAutoMovement)) { Encoding = NetEncoding.Int },
		nameof(Skybox3D.Scale),
		nameof(Skybox3D.Origin),
		nameof(Skybox3D.Area),
		new(nameof(Skybox3D.Fog.Enable)) { Encoding = NetEncoding.Int },
		new(nameof(Skybox3D.Fog.Blend)) { Encoding = NetEncoding.Int },
		new(nameof(Skybox3D.Fog.Radial)) { Encoding = NetEncoding.Int },
		nameof(Skybox3D.Fog.DirPrimary),
		new(nameof(Skybox3D.Fog.ColorPrimary)) { Encoding = NetEncoding.Int },
		new(nameof(Skybox3D.Fog.ColorSecondary)) { Encoding = NetEncoding.Int },
		new(nameof(Skybox3D.Fog.ColorPrimaryHDR)) { Encoding = NetEncoding.Int },
		new(nameof(Skybox3D.Fog.ColorSecondaryHDR)) { Encoding = NetEncoding.Int },
		nameof(Skybox3D.Fog.Start),
		nameof(Skybox3D.Fog.End),
		nameof(Skybox3D.Fog.MaxDensity),
		nameof(Skybox3D.Fog.HDRColorScale),
		nameof(PlayerFog.Ctrl),
		new(nameof(Audio.LocalSound)) { Encoding = NetEncoding.Elements },
		nameof(Audio.SoundscapeIndex),
		nameof(Audio.LocalBits),
		nameof(Audio.Ent),
		nameof(SprintSpeed),
		nameof(WalkSpeed),
		nameof(SlowWalkSpeed),
		nameof(LadderSpeed),
		nameof(CrouchedWalkSpeed),
		nameof(DuckSpeed),
		nameof(UnDuckSpeed),
		nameof(DuckToggled),
	];

	[PredictionTable]
	public static readonly PredictionField[] Prediction = [
		// DEFINE.FIELD( nameof(StepSide), FieldType.Integer ),
		nameof(HideHUD),
		new(nameof(PunchAngle)) { Tolerance = 0.125f },
		new(nameof(PunchAngleVel)) { Tolerance = 0.125f },
		nameof(DrawViewmodel),
		nameof(WearingSuit),
		nameof(Poisoned),
		nameof(AllowAutoMovement),
		nameof(Ducked),
		nameof(Ducking),
		nameof(InDuckJump),
		nameof(DuckTime),
		nameof(DuckJumpTime),
		nameof(JumpTime),
		new(nameof(FallVelocity)) { Tolerance = 0.5f },
		nameof(OldButtons),
		nameof(StepSize),
		new(nameof(FOVRate)) { Flags = FieldTypeDescFlags.Save },
		nameof(SprintSpeed),
		nameof(WalkSpeed),
		nameof(SlowWalkSpeed),
		nameof(LadderSpeed),
		nameof(CrouchedWalkSpeed),
		nameof(DuckSpeed),
		nameof(UnDuckSpeed),
		nameof(DuckToggled),
	];

	// gmod data
	[NetworkVar("m_fSprintSpeed")]
	public float SprintSpeed = 400;
	[NetworkVar("m_fWalkSpeed")]
	public float WalkSpeed = 200;
	[NetworkVar("m_fSlowWalkSpeed")]
	public float SlowWalkSpeed = 100;
	[NetworkVar("m_fLadderSpeed")]
	public float LadderSpeed = 200;
	[NetworkVar("m_fCrouchedWalkSpeed")]
	public float CrouchedWalkSpeed = 0.3f;
	[NetworkVar("m_fDuckSpeed")]
	public float DuckSpeed = 0.1f;
	[NetworkVar("m_fUnDuckSpeed")]
	public float UnDuckSpeed = 0.1f;
	[NetworkVar("m_bDuckToggled")]
	public bool DuckToggled;

	// TODO: NETWORK VARS!!!!!
	[NetworkVar("m_chAreaBits")]
	public InlineArrayMaxAreaStateBytes<byte> AreaBits;
	[NetworkVar("m_chAreaPortalBits")]
	public InlineArrayMaxAreaPortalStateBytes<byte> AreaPortalBits;
	[NetworkVar("m_iHideHUD")]
	public int HideHUD;
	[NetworkVar("m_flFOVRate")]
	public float FOVRate;
	[NetworkVar("m_bDucked")]
	public bool Ducked;
	[NetworkVar("m_bDucking")]
	public bool Ducking;
	[NetworkVar("m_bInDuckJump")]
	public bool InDuckJump;
	[NetworkVar("m_flDucktime")]
	public float DuckTime;
	[NetworkVar("m_flDuckJumpTime")]
	public float DuckJumpTime;
	[NetworkVar("m_flJumpTime")]
	public float JumpTime;
	public int StepSide;
	[NetworkVar("m_flFallVelocity")]
	public float FallVelocity;
	public int OldButtons;
	public int OldForwardMove;
	[NetworkVar("m_vecPunchAngle")]
	public QAngle PunchAngle;
	public readonly InterpolatedVar<QAngle> iv_PunchAngle;
	[NetworkVar("m_vecPunchAngleVel")]
	public QAngle PunchAngleVel;
	public readonly InterpolatedVar<QAngle> iv_PunchAngleVel;
	[NetworkVar("m_bDrawViewmodel")]
	public bool DrawViewmodel;
	[NetworkVar("m_bWearingSuit")]
	public bool WearingSuit;
	[NetworkVar("m_bPoisoned")]
	public bool Poisoned;
	[NetworkVar("m_flStepSize")]
	public float StepSize;
	[NetworkVar("m_bAllowAutoMovement")]
	public bool AllowAutoMovement;
	public bool SlowMovement;

	[NetworkVar("m_skybox3d")]
	public Sky3DParams Skybox3D = new();
	[NetworkVar("m_PlayerFog")]
	public FogPlayerParams PlayerFog = new();
	[NetworkVar("m_audio")]
	public AudioParams Audio = new();

	public static readonly DynamicAccessor FIELD_PUNCHANGLE = FIELD.OF(nameof(PunchAngle));
	public static readonly DynamicAccessor FIELD_PUNCHANGLEVEL = FIELD.OF(nameof(PunchAngleVel));

	public PlayerLocalData() {
		iv_PunchAngle = new("PlayerLocalData.iv_PunchAngle");
		iv_PunchAngleVel = new("PlayerLocalData.iv_PunchAngleVel");

		iv_PunchAngle.Setup(this, FIELD_PUNCHANGLE, LatchFlags.LatchSimulationVar);
		iv_PunchAngleVel.Setup(this, FIELD_PUNCHANGLEVEL, LatchFlags.LatchSimulationVar);
		FOVRate = 0;

		Ducked = false;
		Ducking = false;
		DuckSpeed = 0.1f;
		UnDuckSpeed = 0.1f;
		SprintSpeed = 400.0f;
		WalkSpeed = 200.0f;
		SlowWalkSpeed = 100.0f;
		LadderSpeed = 150.0f;
		CrouchedWalkSpeed = 0.3f;
	}
}
