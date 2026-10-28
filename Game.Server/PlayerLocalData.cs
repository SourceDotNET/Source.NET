using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

public partial class PlayerLocalData
{
	[NetworkTable("DT_Local")]
	public static readonly NetworkProp[] Props = [
		new(nameof(AreaBits)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(AreaPortalBits)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(HideHUD)) { Encoding = NetEncoding.Int, Bits = (int)HideHudBits.BitCount, Flags = PropFlags.Unsigned },
		new(nameof(FOVRate)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Ducked)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(Ducking)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(InDuckJump)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(DuckTime)) { Encoding = NetEncoding.Float, Bits = 12, Flags = PropFlags.RoundDown | PropFlags.ChangesOften, Low = 0.0f, High = 2048.0f },
		new(nameof(DuckJumpTime)) { Encoding = NetEncoding.Float, Bits = 12, Flags = PropFlags.RoundDown, Low = 0.0f, High = 2048.0f },
		new(nameof(JumpTime)) { Encoding = NetEncoding.Float, Bits = 12, Flags = PropFlags.RoundDown, Low = 0.0f, High = 2048.0f },
		new(nameof(FallVelocity)) { Bits = 32, Flags = PropFlags.NoScale | PropFlags.ChangesOften, Low = -4096.0f, High = 4096.0f },
		new(nameof(PunchAngle)) { Encoding = NetEncoding.Vector, Bits = -1, Flags = PropFlags.NoScale | PropFlags.ChangesOften },
		new(nameof(PunchAngleVel)) { Encoding = NetEncoding.Vector, Bits = -1, Flags = PropFlags.NoScale },
		new(nameof(DrawViewmodel)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(WearingSuit)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		nameof(Poisoned),
		new(nameof(StepSize)) { Bits = 16, Flags = PropFlags.RoundUp, Low = 0.0f, High = 512.0f },
		new(nameof(AllowAutoMovement)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(Skybox3D.Scale)) { Bits = 12 },
		new(nameof(Skybox3D.Origin)) { Bits = -1, Flags = PropFlags.Coord },
		new(nameof(Skybox3D.Area)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Skybox3D.Fog.Enable)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(Skybox3D.Fog.Blend)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(Skybox3D.Fog.Radial)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(Skybox3D.Fog.DirPrimary)) { Bits = -1, Flags = PropFlags.Coord },
		new(nameof(Skybox3D.Fog.ColorPrimary)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Skybox3D.Fog.ColorSecondary)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Skybox3D.Fog.ColorPrimaryHDR)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Skybox3D.Fog.ColorSecondaryHDR)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Skybox3D.Fog.Start)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Skybox3D.Fog.End)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Skybox3D.Fog.MaxDensity)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Skybox3D.Fog.HDRColorScale)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(PlayerFog.Ctrl),
		new(nameof(Audio.LocalSound)) { Encoding = NetEncoding.Elements, Bits = -1, Flags = PropFlags.Coord },
		new(nameof(Audio.SoundscapeIndex)) { Bits = 17 },
		new(nameof(Audio.LocalBits)) { Bits = NUM_AUDIO_LOCAL_SOUNDS, Flags = PropFlags.Unsigned },
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

	public BaseEntity? Outer;
	public void NetworkStateChanged() => Outer?.NetworkStateChanged();
	public void NetworkStateChanged(IFieldAccessor field) => Outer?.NetworkStateChanged();

	[NetworkVar("m_fSprintSpeed")]
	public partial float SprintSpeed { get; set; }
	[NetworkVar("m_fWalkSpeed")]
	public partial float WalkSpeed { get; set; }
	[NetworkVar("m_fSlowWalkSpeed")]
	public partial float SlowWalkSpeed { get; set; }
	[NetworkVar("m_fLadderSpeed")]
	public partial float LadderSpeed { get; set; }
	[NetworkVar("m_fCrouchedWalkSpeed")]
	public partial float CrouchedWalkSpeed { get; set; }
	[NetworkVar("m_fDuckSpeed")]
	public partial float DuckSpeed { get; set; }
	[NetworkVar("m_fUnDuckSpeed")]
	public partial float UnDuckSpeed { get; set; }
	[NetworkVar("m_bDuckToggled")]
	public partial bool DuckToggled { get; set; }

	// TODO: NETWORK VARS!!!!!
	[NetworkVar("m_chAreaBits")]
	public partial ref readonly InlineArrayMaxAreaStateBytes<byte> AreaBits { get; }
	[NetworkVar("m_chAreaPortalBits")]
	public partial ref readonly InlineArrayMaxAreaPortalStateBytes<byte> AreaPortalBits { get; }
	[NetworkVar("m_iHideHUD")]
	public partial bool HideHUD { get; set; }
	[NetworkVar("m_flFOVRate")]
	public partial float FOVRate { get; set; }
	[NetworkVar("m_bDucked")]
	public partial bool Ducked { get; set; }
	[NetworkVar("m_bDucking")]
	public partial bool Ducking { get; set; }
	[NetworkVar("m_bInDuckJump")]
	public partial bool InDuckJump { get; set; }
	[NetworkVar("m_flDucktime")]
	public partial double DuckTime { get; set; }
	[NetworkVar("m_flDuckJumpTime")]
	public partial double DuckJumpTime { get; set; }
	[NetworkVar("m_flJumpTime")]
	public partial double JumpTime { get; set; }
	public int StepSide;
	[NetworkVar("m_flFallVelocity")]
	public partial float FallVelocity { get; set; }
	public int OldButtons;
	public int OldForwardMove;
	[NetworkVar("m_vecPunchAngle")]
	public partial QAngle PunchAngle { get; set; }
	[NetworkVar("m_vecPunchAngleVel")]
	public partial QAngle PunchAngleVel { get; set; }
	[NetworkVar("m_bDrawViewmodel")]
	public partial bool DrawViewmodel { get; set; }
	[NetworkVar("m_bWearingSuit")]
	public partial bool WearingSuit { get; set; }
	[NetworkVar("m_bPoisoned")]
	public partial bool Poisoned { get; set; }
	[NetworkVar("m_flStepSize")]
	public partial float StepSize { get; set; }
	[NetworkVar("m_bAllowAutoMovement")]
	public partial bool AllowAutoMovement { get; set; }
	public bool SlowMovement;

	[NetworkVar("m_skybox3d")]
	public partial ref readonly Sky3DParams Skybox3D { get; }
	[NetworkVar("m_PlayerFog")]
	public partial ref readonly FogPlayerParams PlayerFog { get; }
	[NetworkVar("m_audio")]
	public partial ref readonly AudioParams Audio { get; }

	public void UpdateAreaBits(BasePlayer pl, ReadOnlySpan<byte> areaPortalBits) {
		Vector3 origin = pl.EyePosition();

		Span<byte> tempBits = stackalloc byte[32];

		int i;
		int area = engine.GetArea(origin);
		engine.GetAreaBits(area, tempBits);
		ReadOnlySpan<byte> areaBits = AreaBits;
		for (i = 0; i < areaBits.Length; i++) {
			if (tempBits[i] != areaBits[i])
				AreaBitsForModify()[i] = tempBits[i];
		}

		ReadOnlySpan<byte> portalBits = AreaPortalBits;
		for (i = 0; i < Constants.MAX_AREA_PORTAL_STATE_BYTES; i++) {
			if (areaPortalBits[i] != portalBits[i])
				AreaPortalBitsForModify()[i] = areaPortalBits[i];
		}
	}

	public SkyCamera? OldSkyCamera;

	public static void ClientData_Update(BasePlayer pl) {
		SkyCamera? skyCamera = SkyCamera.GetCurrentSkyCamera();

		if (skyCamera == null) {
			if (pl.Local.Skybox3D.Area != 255)
				pl.Local.Skybox3DForModify().Area = 255;
			pl.Local.OldSkyCamera = null;
		}
		else if (skyCamera != pl.Local.OldSkyCamera) {
			pl.Local.OldSkyCamera = skyCamera;
			pl.Local.Skybox3DForModify() = skyCamera.SkyboxData;
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
		Skybox3DForModify() = new();
		PlayerFogForModify() = new();
		AudioForModify() = new();
	}
}
