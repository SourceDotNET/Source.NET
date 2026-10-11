using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

using FIELD = FIELD<PlayerLocalData>;

public class PlayerLocalData
{
	public static readonly SendTable DT_Local = new(nameof(DT_Local), [
		SendPropArray3(FIELD.OF_ARRAY(nameof(AreaBits)), SendPropInt(FIELD.OF_ARRAYINDEX(nameof(AreaBits)), 8, PropFlags.Unsigned)),
		SendPropArray3(FIELD.OF_ARRAY(nameof(AreaPortalBits)), SendPropInt(FIELD.OF_ARRAYINDEX(nameof(AreaPortalBits)), 8, PropFlags.Unsigned)),
		SendPropInt(FIELD.OF(nameof(HideHUD)), (int)HideHudBits.BitCount, PropFlags.Unsigned),
		SendPropFloat(FIELD.OF(nameof(FOVRate)), 0, PropFlags.NoScale),
		SendPropInt(FIELD.OF(nameof(Ducked)), 1, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(Ducking)), 1, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(InDuckJump)), 1, PropFlags.Unsigned),
		SendPropFloat(FIELD.OF(nameof(DuckTime)), 12, PropFlags.RoundDown | PropFlags.ChangesOften, 0.0f, 2048.0f),
		SendPropFloat(FIELD.OF(nameof(DuckJumpTime)), 12, PropFlags.RoundDown, 0.0f, 2048.0f),
		SendPropFloat(FIELD.OF(nameof(JumpTime)), 12, PropFlags.RoundDown, 0.0f, 2048.0f),
		SendPropFloat(FIELD.OF(nameof(FallVelocity)), 32, PropFlags.NoScale | PropFlags.ChangesOften, -4096.0f, 4096.0f),
		SendPropVector(FIELD.OF(nameof(PunchAngle)), -1,  PropFlags.NoScale|PropFlags.ChangesOften),
		SendPropVector(FIELD.OF(nameof(PunchAngleVel)), -1,  PropFlags.NoScale),
		SendPropInt(FIELD.OF(nameof(DrawViewmodel)), 1, PropFlags.Unsigned),
		SendPropInt(FIELD.OF(nameof(WearingSuit)), 1, PropFlags.Unsigned),
		SendPropBool(FIELD.OF(nameof(Poisoned))),
		SendPropFloat(FIELD.OF(nameof(StepSize)), 16, PropFlags.RoundUp, 0.0f, 512.0f),
		SendPropInt(FIELD.OF(nameof(AllowAutoMovement)),1, PropFlags.Unsigned),

		SendPropInt(FIELD.OF("Skybox3D.Scale"), 12),
		SendPropVector(FIELD.OF("Skybox3D.Origin"), -1, PropFlags.Coord),
		SendPropInt(FIELD.OF("Skybox3D.Area"), 8, PropFlags.Unsigned),
		SendPropInt(FIELD.OF("Skybox3D.Fog.Enable"), 1, PropFlags.Unsigned),
		SendPropInt(FIELD.OF("Skybox3D.Fog.Blend"), 1, PropFlags.Unsigned),
		SendPropInt(FIELD.OF("Skybox3D.Fog.Radial"), 1, PropFlags.Unsigned),
		SendPropVector(FIELD.OF("Skybox3D.Fog.DirPrimary"), -1, PropFlags.Coord),
		SendPropInt(FIELD.OF("Skybox3D.Fog.ColorPrimary"), 32, PropFlags.Unsigned),
		SendPropInt(FIELD.OF("Skybox3D.Fog.ColorSecondary"), 32, PropFlags.Unsigned),
		SendPropInt(FIELD.OF("Skybox3D.Fog.ColorPrimaryHDR"), 32, PropFlags.Unsigned),
		SendPropInt(FIELD.OF("Skybox3D.Fog.ColorSecondaryHDR"), 32, PropFlags.Unsigned),
		SendPropFloat(FIELD.OF("Skybox3D.Fog.Start"), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF("Skybox3D.Fog.End"), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF("Skybox3D.Fog.MaxDensity"), 0, PropFlags.NoScale),
		SendPropFloat(FIELD.OF("Skybox3D.Fog.HDRColorScale"), 0, PropFlags.NoScale),

		SendPropEHandle( FIELD.OF("PlayerFog.Ctrl") ),

		SendPropVector(FIELD.OF_ARRAYINDEX("Audio.LocalSound", 0), -1, PropFlags.Coord),
		SendPropVector(FIELD.OF_ARRAYINDEX("Audio.LocalSound", 1), -1, PropFlags.Coord),
		SendPropVector(FIELD.OF_ARRAYINDEX("Audio.LocalSound", 2), -1, PropFlags.Coord),
		SendPropVector(FIELD.OF_ARRAYINDEX("Audio.LocalSound", 3), -1, PropFlags.Coord),
		SendPropVector(FIELD.OF_ARRAYINDEX("Audio.LocalSound", 4), -1, PropFlags.Coord),
		SendPropVector(FIELD.OF_ARRAYINDEX("Audio.LocalSound", 5), -1, PropFlags.Coord),
		SendPropVector(FIELD.OF_ARRAYINDEX("Audio.LocalSound", 6), -1, PropFlags.Coord),
		SendPropVector(FIELD.OF_ARRAYINDEX("Audio.LocalSound", 7), -1, PropFlags.Coord),
		SendPropInt(FIELD.OF("Audio.SoundscapeIndex"), 17, 0),
		SendPropInt(FIELD.OF("Audio.LocalBits"), NUM_AUDIO_LOCAL_SOUNDS, PropFlags.Unsigned),
		SendPropEHandle(FIELD.OF("Audio.Ent")),

		SendPropFloat(FIELD.OF(nameof(SprintSpeed))),
		SendPropFloat(FIELD.OF(nameof(WalkSpeed))),
		SendPropFloat(FIELD.OF(nameof(SlowWalkSpeed))),
		SendPropFloat(FIELD.OF(nameof(LadderSpeed))),
		SendPropFloat(FIELD.OF(nameof(CrouchedWalkSpeed))),
		SendPropFloat(FIELD.OF(nameof(DuckSpeed))),
		SendPropFloat(FIELD.OF(nameof(UnDuckSpeed))),
		SendPropBool(FIELD.OF(nameof(DuckToggled))),
	]);

	[NetworkName("m_fSprintSpeed")]
	public float SprintSpeed;
	[NetworkName("m_fWalkSpeed")]
	public float WalkSpeed;
	[NetworkName("m_fSlowWalkSpeed")]
	public float SlowWalkSpeed;
	[NetworkName("m_fLadderSpeed")]
	public float LadderSpeed;
	[NetworkName("m_fCrouchedWalkSpeed")]
	public float CrouchedWalkSpeed;
	[NetworkName("m_fDuckSpeed")]
	public float DuckSpeed;
	[NetworkName("m_fUnDuckSpeed")]
	public float UnDuckSpeed;
	[NetworkName("m_bDuckToggled")]
	public bool DuckToggled;

	// TODO: NETWORK VARS!!!!!
	[NetworkName("m_chAreaBits")]
	public InlineArrayMaxAreaStateBytes<byte> AreaBits;
	[NetworkName("m_chAreaPortalBits")]
	public InlineArrayMaxAreaPortalStateBytes<byte> AreaPortalBits;
	[NetworkName("m_iHideHUD")]
	public bool HideHUD;
	[NetworkName("m_flFOVRate")]
	public float FOVRate;
	[NetworkName("m_bDucked")]
	public bool Ducked;
	[NetworkName("m_bDucking")]
	public bool Ducking;
	[NetworkName("m_bInDuckJump")]
	public bool InDuckJump;
	[NetworkName("m_flDucktime")]
	public double DuckTime;
	[NetworkName("m_flDuckJumpTime")]
	public double DuckJumpTime;
	[NetworkName("m_flJumpTime")]
	public double JumpTime;
	public int StepSide;
	[NetworkName("m_flFallVelocity")]
	public float FallVelocity;
	public int OldButtons;
	public int OldForwardMove;
	[NetworkName("m_vecPunchAngle")]
	public QAngle PunchAngle;
	[NetworkName("m_vecPunchAngleVel")]
	public QAngle PunchAngleVel;
	[NetworkName("m_bDrawViewmodel")]
	public bool DrawViewmodel;
	[NetworkName("m_bWearingSuit")]
	public bool WearingSuit;
	[NetworkName("m_bPoisoned")]
	public bool Poisoned;
	[NetworkName("m_flStepSize")]
	public float StepSize;
	[NetworkName("m_bAllowAutoMovement")]
	public bool AllowAutoMovement;
	public bool SlowMovement;

	[NetworkName("m_skybox3d")]
	public Sky3DParams Skybox3D = new();
	[NetworkName("m_PlayerFog")]
	public FogPlayerParams PlayerFog = new();
	[NetworkName("m_audio")]
	public AudioParams Audio = new();

	public void UpdateAreaBits(BasePlayer pl, ReadOnlySpan<byte> areaPortalBits) {
		Vector3 origin = pl.EyePosition();

		Span<byte> tempBits = stackalloc byte[32];

		int i;
		int area = engine.GetArea(origin);
		engine.GetAreaBits(area, tempBits);
		Span<byte> areaBits = AreaBits;
		for (i = 0; i < areaBits.Length; i++) {
			if (tempBits[i] != areaBits[i])
				areaBits[i] = tempBits[i];
		}

		Span<byte> portalBits = AreaPortalBits;
		for (i = 0; i < Constants.MAX_AREA_PORTAL_STATE_BYTES; i++) {
			if (areaPortalBits[i] != portalBits[i])
				portalBits[i] = areaPortalBits[i];
		}
	}

	public SkyCamera? OldSkyCamera;

	public static void ClientData_Update(BasePlayer pl) {
		SkyCamera? skyCamera = SkyCamera.GetCurrentSkyCamera();

		if (skyCamera == null) {
			pl.Local.Skybox3D.Area = 255;
			pl.Local.OldSkyCamera = null;
		}
		else if (skyCamera != pl.Local.OldSkyCamera) {
			pl.Local.OldSkyCamera = skyCamera;
			pl.Local.Skybox3D = skyCamera.SkyboxData;
		}
	}

	public static void UpdateAllClientData() {
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? pl = Util.PlayerByIndex(i);
			if (pl == null)
				continue;

			ClientData_Update(pl);
		}
	}

	public PlayerLocalData() {
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
